using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoQuickSelector_App.ViewModels;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// 仮想化リスト（GridView/ListView）の <see cref="ListViewBase.ContainerContentChanging"/> 用ローダ。
/// 圧縮サムネイルバイトは <see cref="PhotoItemViewModel"/> が全件常駐保持し、重い非圧縮
/// <see cref="BitmapImage"/> は「画面に見えているコンテナ」分だけ生成、リサイクル時に <see cref="Image.Source"/> を解放する。
/// さらに **デコード済み BitmapImage を容量上限つき LRU で保持**し、スクロールで同じセルが戻っても再デコードしない
/// （UI スレッドの瞬間負荷を抑えフリーズを防ぐ）。LRU 容量は固定なのでメモリは枚数に依存しない。
/// </summary>
internal sealed class ThumbnailContainerLoader
{
    private readonly string _imageName;
    private readonly int _decodePixelWidth;
    private readonly int _capacity;
    private readonly Action<int>? _onAnchor;   // 実体化インデックス通知（背景先読みのアンカー。グリッドのみ）

    // デコード済み BitmapImage の LRU。先頭＝最近使用。すべて UI スレッドからのみ触る（ロック不要）。
    private readonly LinkedList<PhotoItemViewModel> _order = new();
    private readonly Dictionary<PhotoItemViewModel, BitmapImage> _cache = new();

    /// <param name="imageName">DataTemplate 内サムネイル <see cref="Image"/> の x:Name。</param>
    /// <param name="decodePixelWidth">デコード解像度（表示サイズ）。非圧縮サーフェスを小さく保つ。</param>
    /// <param name="capacity">デコード済み BitmapImage の LRU 上限枚数。</param>
    /// <param name="onAnchor">実体化時に項目インデックスを通知（背景先読みの中心）。不要なら null。</param>
    public ThumbnailContainerLoader(string imageName, int decodePixelWidth, int capacity, Action<int>? onAnchor = null)
    {
        _imageName = imageName;
        _decodePixelWidth = decodePixelWidth;
        _capacity = capacity;
        _onAnchor = onAnchor;
    }

    /// <summary>フォルダ切替時などに LRU を破棄して旧フォルダ分を早期解放する。</summary>
    public void Clear()
    {
        _cache.Clear();
        _order.Clear();
    }

    public void Handle(ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer?.ContentTemplateRoot is not FrameworkElement root) return;
        if (root.FindName(_imageName) is not Image img) return;

        if (args.InRecycleQueue)
        {
            img.Source = null;   // Image の参照を解放（デコード済み画像は LRU に残す）
            img.Tag = null;
            return;
        }

        if (args.Item is not PhotoItemViewModel vm) return;
        _onAnchor?.Invoke(args.ItemIndex);

        img.Tag = vm;            // リサイクル先取り検出用トークン
        if (TryTouch(vm) is { } cached)
        {
            img.Source = cached; // LRU ヒット＝再デコードなしで即表示
            return;
        }
        img.Source = null;       // 再利用コンテナの前の内容を消す
        _ = LoadAsync(img, vm);
    }

    private async System.Threading.Tasks.Task LoadAsync(Image img, PhotoItemViewModel vm)
    {
        try
        {
            var bitmap = await vm.CreateThumbnailImageAsync(_decodePixelWidth);
            if (bitmap == null) return;
            Put(vm, bitmap);
            // await 中にコンテナが別アイテムへ回されていたら捨てる（LRU には載せた）
            if (!ReferenceEquals(img.Tag, vm)) return;
            img.Source = bitmap;
        }
        catch
        {
            // fire-and-forget の取りこぼし例外で落とさない（サムネイルなしで継続）
        }
    }

    /// <summary>LRU を引いて最近使用へ繰り上げる。無ければ null。</summary>
    private BitmapImage? TryTouch(PhotoItemViewModel vm)
    {
        if (!_cache.TryGetValue(vm, out var image)) return null;
        _order.Remove(vm);
        _order.AddFirst(vm);
        return image;
    }

    private void Put(PhotoItemViewModel vm, BitmapImage image)
    {
        if (_cache.ContainsKey(vm))
        {
            _cache[vm] = image;
            _order.Remove(vm);
            _order.AddFirst(vm);
            return;
        }
        _cache[vm] = image;
        _order.AddFirst(vm);
        while (_cache.Count > _capacity && _order.Last is { } last)
        {
            _order.RemoveLast();
            _cache.Remove(last.Value);
        }
    }
}
