using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PhotoQuickSelector.Core;
using PhotoQuickSelector_App.ViewModels;
using Windows.Foundation;
using Windows.System;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// 右ペインの大画面プレビュー（Win2D 描画）。メイン大画面＋フィルムストリップ＋右パネル
/// （ズームルーペ／ナビゲーター）の 3 面構成。ズーム/パン・前後ナビ・AF枠/グリッド線を担う。
/// <para>
/// 実装は関心事ごとに partial で分割している:
/// <list type="bullet">
///   <item><c>PreviewControl.xaml.cs</c> … 骨組み・ViewModel 配線・ライフサイクル（本ファイル）</item>
///   <item><c>PreviewControl.MainCanvas.cs</c> … メイン描画・パン/ズーム・ポインタ</item>
///   <item><c>PreviewControl.Overlays.cs</c> … 三分割グリッド線・AF フォーカス枠</item>
///   <item><c>PreviewControl.Loupe.cs</c> … 右上ズームプレビュー（100% ルーペ）</item>
///   <item><c>PreviewControl.Navigator.cs</c> … ナビゲーター（全体縮小＋表示領域矩形）</item>
///   <item><c>PreviewControl.Input.cs</c> … キー処理（<see cref="HandleKeyDown"/>）</item>
/// </list>
/// 先読みキャッシュは <see cref="PreviewBitmapCache"/> に分離。
/// </para>
/// </summary>
public sealed partial class PreviewControl : UserControl
{
    // 写真表示中の余白（レターボックス）背景。空/読み込み中はキャンバスの ClearColor
    // （テーマ背景＝目立たない色）を見せ、ビットマップがある時だけ各 Draw でこの暗色に塗る。
    private static readonly Windows.UI.Color PhotoBackdropColor =
        Windows.UI.Color.FromArgb(0xFF, 0x1E, 0x1E, 0x1E);
    private static readonly Windows.UI.Color NavBackdropColor =
        Windows.UI.Color.FromArgb(0xFF, 0x2A, 0x2A, 0x2A);

    private readonly PreviewViewport _viewport = new();
    private readonly PreviewViewport _zoomViewport = new();  // 右上ズームプレビュー（100% ルーペ）の独立ビューポート
    private readonly PreviewBitmapCache _cache;              // 前後 N 枚先読みキャッシュ（SPEC §4）
    private CanvasBitmap? _bitmap;          // 現在表示中（_cache 内の参照。直接 Dispose しない）
    private ImageMetadata? _currentMeta;    // 表示中ビットマップに対応するメタデータ（AF枠描画用）
    private int _currentOrientation = 1;
    private int _loadToken;                 // 現在表示ロードの世代（高速ナビでの追い越し対策）

    // 先読みキャッシュの保持窓（現在位置の前後 N 枚）。
    private const int PrefetchForward = 2;
    private const int PrefetchBackward = 1;

    private bool _isPanning;
    private Point _lastPointer;

    private bool _isZoomPanning;            // 右上ズームプレビューのドラッグ中
    private Point _zoomLastPointer;
    private bool _isNavPanning;             // ナビゲーターのドラッグ中

    private MainViewModel? _viewModel;

    /// <summary>キャッシュ中の画像ファイル名一覧（デバッグオーバーレイ用。C キーでトグル）。</summary>
    public ObservableCollection<string> CachedFileNames { get; } = new();

    // フィルムストリップのサムネイル・デコード/破棄＋デコード済み LRU（小さめ容量）。
    // 高さ調節で大きめに広げてもボケないよう、デコード幅は最大セル一辺ぶんを見込む。
    private readonly ThumbnailContainerLoader _filmLoader =
        new("FilmThumbImage", decodePixelWidth: 140, capacity: 60);

    // フィルムストリップのセル寸法（XAML リソース）。高さ調節時に Edge を更新して各セルを追従させる。
    private readonly FilmStripMetrics _filmMetrics;

    // ListView 高 → セル一辺へ変換するときの内訳分
    // （Padding 上0+下2 ＋ 項目 Margin 縦0 ＋ アクセント外枠 6 ＋ カラーラベル枠 6 ＋ ファイル名行 ≒ 18）。
    private const double FilmChromeHeight = 32;
    private const double MinThumbEdge = 40;

    public PreviewControl()
    {
        InitializeComponent();
        _filmMetrics = (FilmStripMetrics)Resources["FilmMetrics"];
        _cache = new PreviewBitmapCache(MainCanvas);
        _cache.Changed += RefreshCacheOverlay;
        // 【案2】デコード順が回ってきた時点で「まだ保持窓内か」を判定し、窓外なら読み込みを破棄する。
        // 押しっぱなしで通過した写真の不要ロードを安価に捨て、同時実行ゲートと併せて膨張を抑える。
        _cache.IsWanted = IsPathInWindow;

        // Esc ではプレビューを抜けない（ユーザー要望）。プレビュー表示のまま維持する。
        // プレビュー終了はダブルクリック（SPEC §2）で行う。以前あった Esc→ExitPreview の
        // KeyboardAccelerator は撤去した。なお全画面中の Esc（通常表示へ復帰）は MainWindow 側で処理する。
    }

    // フィルムストリップも可視コンテナの分だけサムネイルをデコード/破棄（メモリは枚数に依存しない）。
    private void FilmStrip_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        => _filmLoader.Handle(args);

    // フィルムストリップのダブルクリックでサムネイル一覧（グリッド）へ戻る。マウスでのプレビュー終了導線
    // （旧: メイン大画面のダブルクリック）の受け皿。戻り先はグリッドが FocusedPhoto を選択状態で表示する。
    private void FilmStrip_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        _viewModel?.ExitPreview();
        e.Handled = true;
    }

    // フィルムストリップの高さ変更（スプリッター/復元）に合わせてセル一辺を再計算し、現在高を設定へ控える。
    private void FilmStrip_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateFilmMetrics();
        if (_viewModel != null && FilmStripRow.ActualHeight > 0)
            _viewModel.Settings.FilmStripHeight = FilmStripRow.ActualHeight;
    }

    private void UpdateFilmMetrics()
        => _filmMetrics.Edge = Math.Max(MinThumbEdge, FilmStrip.ActualHeight - FilmChromeHeight);

    // ViewModel 注入時に保存済みの高さを復元する（行の高さ＝GridLength を直接設定。SizeChanged で再計算される）。
    private void RestoreFilmStripHeight()
    {
        if (_viewModel == null) return;
        double h = _viewModel.Settings.FilmStripHeight;
        if (h >= FilmStripRow.MinHeight && h <= FilmStripRow.MaxHeight)
            FilmStripRow.Height = new GridLength(h);
        UpdateFilmMetrics();
    }

    // ViewModel 注入時に右パネルの幅／ナビゲーター高さを復元する（フィルムストリップと同じ要領）。
    private void RestoreRightPanelLayout()
    {
        if (_viewModel == null) return;
        double w = _viewModel.Settings.RightPanelWidth;
        if (w >= RightPanelColumn.MinWidth) RightPanelColumn.Width = new GridLength(w);
        double h = _viewModel.Settings.NavigatorHeight;
        if (h >= NavRow.MinHeight) NavRow.Height = new GridLength(h);
    }

    // 右パネルの幅変更（スプリッター/復元）に合わせて現在幅を設定へ控える（実保存は終了時）。
    private void RightPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_viewModel != null && RightPanelColumn.ActualWidth > 0)
            _viewModel.Settings.RightPanelWidth = RightPanelColumn.ActualWidth;
    }

    /// <summary>
    /// 表示対象のビューモデル。<see cref="MainPage"/> が生成後に注入する。
    /// 設定時に x:Bind を更新し、選択写真の変更を購読する。
    /// </summary>
    public MainViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (ReferenceEquals(_viewModel, value)) return;
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                ((INotifyCollectionChanged)_viewModel.Photos).CollectionChanged -= OnPhotosChanged;
            }
            _viewModel = value;
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
                ((INotifyCollectionChanged)_viewModel.Photos).CollectionChanged += OnPhotosChanged;
                RestoreFilmStripHeight();
                RestoreRightPanelLayout();
            }
            Bindings.Update();
        }
    }

    // フォルダ再読込（Photos リセット）でフィルムストリップのデコード済み画像を解放。
    private void OnPhotosChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset) _filmLoader.Clear();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.FocusedPhoto):
                // プレビュー中の写真切替はズーム状態（モード/フィット比/相対中心）を維持する。
                LoadCurrentAsync(preserveView: true);
                ScrollSelectedIntoView();
                break;
            case nameof(MainViewModel.IsPreviewMode):
                if (_viewModel?.IsPreviewMode == true)
                {
                    // 入場時はフィット表示から始める。
                    LoadCurrentAsync(preserveView: false);
                    FocusForKeys();
                    ScrollSelectedIntoView();
                }
                break;
            case nameof(MainViewModel.GridKind):
            case nameof(MainViewModel.GridReference):
                MainCanvas.Invalidate();
                break;
        }
    }

    private void FocusForKeys()
    {
        // 可視化直後はレイアウト未確定なのでディスパッチしてキャンバスへフォーカスを取る。
        // （UserControl 自体は Focus が通らないことがあるため、フォーカス可能な CanvasControl を対象にする）
        DispatcherQueue.TryEnqueue(() => MainCanvas.Focus(FocusState.Programmatic));
    }

    private void ScrollSelectedIntoView()
    {
        if (_viewModel?.FocusedPhoto is { } photo)
            DispatcherQueue.TryEnqueue(() => FilmStrip.ScrollIntoView(photo));
    }

    /// <summary>
    /// 選択中の写真をフィルムストリップへスクロールし、そのコンテナにフォーカスを移す。
    /// ←/→ で前後移動したあとに呼び、PageUp/PageDown/Home/End 等の ListView キー操作を
    /// フィルムストリップ上で使えるようにする。コンテナ未実体化時は ListView 自体へフォーカス。
    /// </summary>
    private void FocusFilmStripSelected()
    {
        if (_viewModel?.FocusedPhoto is not { } photo) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            FilmStrip.ScrollIntoView(photo);
            if (FilmStrip.ContainerFromItem(photo) is Control container)
                container.Focus(FocusState.Programmatic);
            else
                FilmStrip.Focus(FocusState.Programmatic);
        });
    }

    // --- 画像ロード ---

    /// <param name="preserveView">
    /// true なら現在のズーム状態（モード/フィット比/相対中心）を維持して差し替える（写真切替）。
    /// false なら新画像をフィット表示で初期化する（プレビュー入場時）。
    /// </param>
    private async void LoadCurrentAsync(bool preserveView = false)
    {
        if (_viewModel?.IsPreviewMode != true) return;

        var photo = _viewModel.FocusedPhoto;
        int token = ++_loadToken;

        if (photo == null)
        {
            _bitmap = null;
            _currentMeta = null;
            InvalidateAll();
            return;
        }

        var bmp = await _cache.GetAsync(photo.Meta.Path);
        if (token != _loadToken)
        {
            // 新しい読み込みに追い越された。表示は最新側に任せるが、ここで Trim を
            // 通さないと押しっぱなしナビ中に Trim が一度も走らずキャッシュが膨張する。
            // WindowPaths() は現在の FocusedPhoto 基準なので常に最新窓へ収束する。
            _cache.Trim(WindowPaths(), _bitmap);
            return;
        }

        _bitmap = bmp;
        _currentMeta = bmp != null ? photo.Meta : null;
        if (bmp != null)
        {
            _currentOrientation = photo.Meta.Orientation;
            double w = bmp.SizeInPixels.Width, h = bmp.SizeInPixels.Height;
            // CanvasBitmap.LoadAsync は EXIF Orientation を適用済みのビットマップを返す（既に正立）。
            // よって SizeInPixels がそのまま表示サイズになる（回転は描画側で加えない）。
            // Size は DPI 依存（高 DPI で縮む）ため、寸法基準は SizeInPixels に統一する。
            // 等倍（100%）を 1 画像px = 1 物理px にするため、現在の DPI を両ビューポートへ供給。
            UpdateDpiScale();
            // 写真切替時はズーム状態を維持（モード/フィット比/相対中心）。入場時はフィット初期化。
            if (preserveView && _viewport.ImageWidth > 0)
            {
                _viewport.SetImagePreservingView(w, h, MainCanvas.ActualWidth, MainCanvas.ActualHeight);
            }
            else
            {
                _viewport.SetCanvasSize(MainCanvas.ActualWidth, MainCanvas.ActualHeight);
                _viewport.SetImage(w, h);
            }

            // 右上ズームプレビューは 100% 表示で AF フォーカス点へ寄せる（旧アプリ準拠）。
            _zoomViewport.SetCanvasSize(ZoomCanvas.ActualWidth, ZoomCanvas.ActualHeight);
            _zoomViewport.SetImage(w, h);
            _zoomViewport.SetActualSize();
            ScrollZoomToFocus();
        }
        InvalidateAll();

        _cache.Prefetch(WindowPaths());
        _cache.Trim(WindowPaths(), _bitmap);
    }

    /// <summary>メイン・ズームプレビュー・ナビゲーターの 3 キャンバスを再描画する。</summary>
    private void InvalidateAll()
    {
        MainCanvas.Invalidate();
        ZoomCanvas.Invalidate();
        NavCanvas.Invalidate();
        UpdateZoomDisplay();
    }

    /// <summary>メイン操作（ズーム/パン）後に呼ぶ。メインとナビ（表示領域矩形が追従）を再描画する。</summary>
    private void InvalidateMain()
    {
        MainCanvas.Invalidate();
        NavCanvas.Invalidate();
        UpdateZoomDisplay();
    }

    /// <summary>現在のメイン倍率（<see cref="PreviewViewport.DeviceScale"/>）をステータスバー表示用に VM へ反映する。</summary>
    private void UpdateZoomDisplay()
    {
        if (_viewModel != null) _viewModel.ZoomScale = _viewport.DeviceScale;
    }

    /// <summary>
    /// 現在の DPI（<c>MainCanvas.Dpi/96</c>＝物理px/DIP）を両ビューポートへ供給する。
    /// 等倍（100%）を 1 画像px = 1 物理px にするために使う。別モニタ移動など DPI 変化時にも更新する。
    /// </summary>
    private void UpdateDpiScale()
    {
        double s = MainCanvas.Dpi / 96.0;
        if (s <= 0) s = 1.0;
        _viewport.DpiScale = s;
        _zoomViewport.DpiScale = s;
    }

    /// <summary>デバイス再生成・ロスト復帰時はキャッシュを破棄して再ロードする。</summary>
    private void ResetCacheAndReload()
    {
        _cache.Clear();
        _bitmap = null;
        _currentMeta = null;
        // デバイス再生成/DPI 変更時は現在のズーム状態を保ったまま再ロードする。
        LoadCurrentAsync(preserveView: true);
    }

    /// <summary>
    /// キャッシュ内容のデバッグオーバーレイ（<see cref="CacheOverlay"/>）を最新化する。
    /// キャッシュ変更通知（<see cref="PreviewBitmapCache.Changed"/>）から呼ばれ、表示中のときだけ更新する。
    /// 通知は非同期ロード継続（UI スレッド）から来るが、安全のため UI スレッドへ束ねる。
    /// </summary>
    private void RefreshCacheOverlay()
    {
        if (CacheOverlay.Visibility != Visibility.Visible) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            CachedFileNames.Clear();
            foreach (var name in _cache.SnapshotFileNames())
                CachedFileNames.Add(name);
        });
    }

    /// <summary>現在位置を中心とした保持窓 [index-backward, index+forward] のファイルパス。</summary>
    private IEnumerable<string> WindowPaths()
    {
        var vm = _viewModel;
        if (vm?.FocusedPhoto == null) yield break;
        int index = vm.Photos.IndexOf(vm.FocusedPhoto);
        if (index < 0) yield break;

        int start = Math.Max(0, index - PrefetchBackward);
        int end = Math.Min(vm.Photos.Count - 1, index + PrefetchForward);
        for (int i = start; i <= end; i++)
            yield return vm.Photos[i].Meta.Path;
    }

    /// <summary>【案2】指定パスが現在の保持窓内かどうか。キャッシュのデコード可否判定に使う。</summary>
    private bool IsPathInWindow(string path)
    {
        foreach (var p in WindowPaths())
            if (string.Equals(p, path, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
