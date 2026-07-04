using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
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
///   <item><c>PreviewControl.Overlays.cs</c> … 構図グリッド線・AF フォーカス枠</item>
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
    private CanvasBitmap? _bitmap;          // 現在表示中の GPU ビットマップ（本コントロールが所有。差し替え時に Dispose する。ピクセル実体はキャッシュの SoftwareBitmap 側）
    private ImageMetadata? _currentMeta;    // 表示中ビットマップに対応するメタデータ（AF枠描画用）
    private int _currentOrientation = 1;
    private int _loadToken;                 // 現在表示ロードの世代（高速ナビでの追い越し対策）

    // 先読みキャッシュの保持窓（現在位置の前後 N 枚）。
    private const int PrefetchForward = 2;
    private const int PrefetchBackward = 1;

    // 連打中のフル解像度デコード（≈200MB/枚）抑制。素の毎キー・デコードは VRAM 生成レートを
    // 飽和させ（回収が追いつかず増え続ける）ため、未キャッシュ（＝重いデコードが要る）画像の読み込みを
    // 「直近 RateWindow 内のデコード回数」でレート制限する:
    //   ・キャッシュ済み＝デコード不要（VRAM 生成なし）→ 常に即表示（別途 IsCached で判定）
    //   ・未キャッシュでも直近のデコード回数が RateBudget 未満＝レートに余裕あり → 即デコード
    //     （離れたファイルへ数枚ジャンプ、通常の連続切替はここで遅延なく通る）
    //   ・未キャッシュでレート超過（押しっぱなしの大量連発）→ 間引く
    //   ・停止後 LoadSettleDelay ＝最終位置を確定デコード＋近傍を先読み
    // 経過時間ではなく「回数（レート）」で絞るので、キーリピート速度の OS 設定に依存しない。
    // 持続レート上限は概ね RateBudget ÷ RateWindow（＝許容バーストと連動）。VRAM が増えるなら
    // RateBudget を下げる／RateWindow を延ばす。バーストが足りず遅延を感じるなら逆に緩める。
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _settleTimer;
    private readonly Queue<DateTime> _recentDecodes = new();                                  // 直近のフル解像度デコード時刻（レート判定用）
    private static readonly TimeSpan RateWindow = TimeSpan.FromMilliseconds(1500);            // レートを見る窓
    private const int RateBudget = 3;                                                         // 窓内で即デコードを許す枚数
    private static readonly TimeSpan LoadSettleDelay = TimeSpan.FromMilliseconds(150);        // 停止後の確定＋先読み

    private bool _isPanning;
    private Point _lastPointer;

    private bool _isZoomPanning;            // 右上ズームプレビューのドラッグ中
    private Point _zoomLastPointer;
    private bool _isNavPanning;             // ナビゲーターのドラッグ中

    private MainViewModel? _viewModel;

    /// <summary>キャッシュ中の画像（状態色付き）一覧（デバッグオーバーレイ用。C キーでトグル）。</summary>
    public ObservableCollection<CacheEntry> CachedFileNames { get; } = new();

    // 先読みキャッシュオーバーレイの状態別文字色（cached=白／loading=緑系／waiting=灰系）。
    private static readonly Brush CachedBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xE8, 0xFF, 0xFF, 0xFF));
    private static readonly Brush LoadingBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x7C, 0xE3, 0x8B));
    private static readonly Brush WaitingBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x9A, 0xA0, 0xA6));

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
        _cache = new PreviewBitmapCache();
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
                // 連打中のフル解像度デコード膨張を防ぐため throttle 経由で要求する。
                RequestPreviewLoad();
                ScrollSelectedIntoView();
                break;
            case nameof(MainViewModel.IsPreviewMode):
                if (_viewModel?.IsPreviewMode == true)
                {
                    // 入場時はフィット表示から始める（レート制限はバイパスして即時ロード）。
                    _settleTimer?.Stop();
                    _recentDecodes.Clear(); // 入場後の最初のナビは必ず即デコード
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

    /// <summary>
    /// FocusedPhoto 変更時のプレビュー読み込み要求（連打時の VRAM 膨張対策のレート制限）。
    /// キャッシュ済みは常に即表示。未キャッシュは直近 <see cref="RateWindow"/> 内のデコード回数が
    /// <see cref="RateBudget"/> 未満なら即デコード、超過（押しっぱなしの大量連発）なら間引き、
    /// 停止後に最終位置を確定デコード＋近傍先読み。間引かれている間メインは直前の画像のまま
    /// （どの写真かはフィルムストリップのハイライトで分かる）。
    /// </summary>
    private void RequestPreviewLoad()
    {
        if (_viewModel?.IsPreviewMode != true) return;

        // 既にデコード済み（キャッシュ在籍）ならデコード不要＝VRAM を生成しない。レート制限せず即表示する。
        // 通常のゆっくりした前後移動は settle 先読みで近傍が温まっているため、これで2枚目以降も遅延なく出る。
        var photo = _viewModel.FocusedPhoto;
        if (photo != null && _cache.IsCached(photo.Meta.Path))
        {
            LoadCurrentAsync(preserveView: true, prefetch: false);
            RestartSettleTimer(); // 止まった後に近傍を先読みして次の移動も温める
            return;
        }

        // 未キャッシュ＝重いフル解像度デコード（≈200MB/枚）。直近 RateWindow 内のデコード回数で絞る:
        //   ・回数が RateBudget 未満＝レートに余裕あり → 即デコード（数枚のジャンプ・通常連続切替はここを通る）
        //   ・超過＝押しっぱなしの大量連発 → 間引き。停止後の settle で最終位置を確定＋先読み。
        var now = DateTime.UtcNow;
        if (PrunedDecodeCount(now) < RateBudget)
        {
            _recentDecodes.Enqueue(now);
            LoadCurrentAsync(preserveView: true, prefetch: false);
        }
        RestartSettleTimer();
    }

    /// <summary>直近 <see cref="RateWindow"/> より古いデコード時刻を捨て、窓内の残数を返す。</summary>
    private int PrunedDecodeCount(DateTime now)
    {
        while (_recentDecodes.Count > 0 && now - _recentDecodes.Peek() > RateWindow)
            _recentDecodes.Dequeue();
        return _recentDecodes.Count;
    }

    private void RestartSettleTimer()
    {
        _settleTimer ??= CreateSettleTimer();
        _settleTimer.Stop();
        _settleTimer.Start();
    }

    private Microsoft.UI.Dispatching.DispatcherQueueTimer CreateSettleTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = LoadSettleDelay;
        timer.IsRepeating = false;
        timer.Tick += (s, _) =>
        {
            s.Stop();
            if (_viewModel?.IsPreviewMode != true) return;
            // 停止後の確定: 最終位置をデコード（既にデコード済みならキャッシュヒットで無駄なし）＋近傍を先読み。
            // 未キャッシュ＝実デコードするならレート窓に計上する（停止後なので通常は低レート）。
            var photo = _viewModel.FocusedPhoto;
            if (photo != null && !_cache.IsCached(photo.Meta.Path))
            {
                var now = DateTime.UtcNow;
                PrunedDecodeCount(now);
                _recentDecodes.Enqueue(now);
            }
            LoadCurrentAsync(preserveView: true, prefetch: true);
        };
        return timer;
    }

    /// <param name="preserveView">
    /// true なら現在のズーム状態（モード/フィット比/相対中心）を維持して差し替える（写真切替）。
    /// false なら新画像をフィット表示で初期化する（プレビュー入場時）。
    /// </param>
    /// <param name="prefetch">
    /// true なら読み込み後に近傍を先読みする。ナビ中のロード（<see cref="RequestPreviewLoad"/> の
    /// キャッシュヒット/レート内デコード）では false にして近傍デコードの膨張を避け、
    /// 停止後（settle）の確定ロードでのみ先読みする。
    /// </param>
    private async void LoadCurrentAsync(bool preserveView = false, bool prefetch = true)
    {
        if (_viewModel?.IsPreviewMode != true) return;

        var photo = _viewModel.FocusedPhoto;
        int token = ++_loadToken;

        if (photo == null)
        {
            _bitmap?.Dispose();
            _bitmap = null;
            _currentMeta = null;
            InvalidateAll();
            return;
        }

        var sb = await _cache.GetAsync(photo.Meta.Path);
        if (token != _loadToken)
        {
            // 新しい読み込みに追い越された。表示は最新側に任せるが、ここで Trim を
            // 通さないと押しっぱなしナビ中に Trim が一度も走らずキャッシュが膨張する。
            // WindowPaths() は現在の FocusedPhoto 基準なので常に最新窓へ収束する。
            _cache.Trim(WindowPaths());
            return;
        }

        // メインメモリの SoftwareBitmap から GPU へ転送（デコード済みピクセルの生コピーなので速い）。
        // 稀にデバイスロスト直後だと生成に失敗しうるが、その場合は CreateResources →
        // ResetCacheAndReload 経由で再ロードされるので null 表示で流してよい。
        CanvasBitmap? bmp = null;
        if (sb != null)
        {
            try { bmp = CanvasBitmap.CreateFromSoftwareBitmap(MainCanvas, sb); }
            catch { bmp = null; }
        }
        _bitmap?.Dispose();
        _bitmap = bmp;
        _currentMeta = bmp != null ? photo.Meta : null;
        if (bmp != null)
        {
            _currentOrientation = photo.Meta.Orientation;
            double w = bmp.SizeInPixels.Width, h = bmp.SizeInPixels.Height;
            // キャッシュのデコード（WIC・RespectExifOrientation）が Orientation 適用済みの正立ピクセルを
            // 返すため、従来どおり回転は描画側で加えない。SizeInPixels がそのまま表示サイズになる。
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

        if (prefetch) _cache.Prefetch(WindowPaths());
        _cache.Trim(WindowPaths());
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

    /// <summary>
    /// デバイス再生成・ロスト復帰時に呼ばれる。キャッシュは SoftwareBitmap（デバイス非依存）になった
    /// ため、デバイス再生成/DPI 変更でも破棄不要。旧デバイスに属する表示中の CanvasBitmap だけ破棄し、
    /// キャッシュヒットの再転送で即復帰する。
    /// </summary>
    private void ResetCacheAndReload()
    {
        _bitmap?.Dispose();
        _bitmap = null;
        _currentMeta = null;
        // デバイス再生成/DPI 変更時は現在のズーム状態を保ったまま即時再ロードする（レート制限はバイパス）。
        _settleTimer?.Stop();
        _recentDecodes.Clear();
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
            foreach (var (name, state) in _cache.Snapshot())
            {
                var (suffix, brush) = state switch
                {
                    CacheItemState.Loading => (" (loading)", LoadingBrush),
                    CacheItemState.Waiting => (" (waiting)", WaitingBrush),
                    _ => ("", CachedBrush),
                };
                CachedFileNames.Add(new CacheEntry(name + suffix, brush));
            }
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

/// <summary>先読みキャッシュオーバーレイの 1 行（表示文字＋状態色）。XAML の <c>x:Bind</c> 用に top-level 公開。</summary>
/// <remarks>
/// ポジショナル record だと <c>init</c> 専用プロパティになり XamlTypeInfo の生成（setter 代入）と
/// 衝突する（CS8852）。get-only クラスにして読み取り専用バインド（OneWay）に揃える。
/// </remarks>
public sealed class CacheEntry
{
    public CacheEntry(string text, Brush foreground)
    {
        Text = text;
        Foreground = foreground;
    }

    public string Text { get; }
    public Brush Foreground { get; }
}
