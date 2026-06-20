using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoQuickSelector_App.ViewModels;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// 仮想化リスト（GridView/ListView）の <see cref="ListViewBase.ContainerContentChanging"/> 用の共通処理。
/// 圧縮サムネイルバイトは <see cref="PhotoItemViewModel"/> が全件常駐保持し、重い非圧縮
/// <see cref="Microsoft.UI.Xaml.Media.Imaging.BitmapImage"/> は「画面に見えているコンテナ」の分だけ生成、
/// コンテナがリサイクル（画面外へ）されたら破棄する。これで枚数に依存しないメモリで全件常駐できる。
/// </summary>
internal static class ThumbnailContainerLoader
{
    /// <param name="imageName">DataTemplate 内のサムネイル <see cref="Image"/> の x:Name。</param>
    /// <param name="decodePixelWidth">デコード解像度（表示サイズ）。非圧縮サーフェスを小さく保つ。</param>
    public static void Handle(ContainerContentChangingEventArgs args, string imageName, int decodePixelWidth)
    {
        if (args.ItemContainer?.ContentTemplateRoot is not FrameworkElement root) return;
        if (root.FindName(imageName) is not Image img) return;

        if (args.InRecycleQueue)
        {
            img.Source = null;   // 画面外＝非圧縮サーフェスを解放
            img.Tag = null;
            return;
        }

        if (args.Item is not PhotoItemViewModel vm) return;
        img.Tag = vm;            // リサイクル先取り検出用トークン
        img.Source = null;       // 再利用コンテナの前の内容を消す
        _ = LoadAsync(img, vm, decodePixelWidth);
    }

    private static async System.Threading.Tasks.Task LoadAsync(Image img, PhotoItemViewModel vm, int decodePixelWidth)
    {
        var bitmap = await vm.CreateThumbnailImageAsync(decodePixelWidth);
        if (bitmap == null) return;
        // await 中にコンテナが別アイテムへ回されていたら捨てる
        if (!ReferenceEquals(img.Tag, vm)) return;
        img.Source = bitmap;
    }
}
