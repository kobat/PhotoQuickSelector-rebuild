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
using Windows.Graphics.DirectX;
using Windows.System;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// 先読み保持窓内でのパスの由来分類（デバッグオーバーレイのラベル表示用）。
/// <see cref="PreviewControl.WindowEntries"/> が付与する。
/// </summary>
internal enum WindowSlot
{
    /// <summary>焦点写真そのもの。</summary>
    Focus,
    /// <summary>選択集合のメンバー窓（巡回順で前後）。</summary>
    Member,
    /// <summary>位置窓（現在位置の前後 N 枚）。</summary>
    Position,
}

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
    private PreviewBitmapCache _cache;                       // 前後 N 枚先読みキャッシュ（SPEC §4）。同時デコード数変更で再構築するため readonly ではない
    private CanvasBitmap? _bitmap;          // 現在表示中の GPU ビットマップ（本コントロールが所有。差し替え時に Dispose する。ピクセル実体はキャッシュの byte[]（PixelFrame）側）
    private ImageMetadata? _currentMeta;    // 表示中ビットマップに対応するメタデータ（AF枠描画用）
    private int _currentOrientation = 1;
    private int _loadToken;                 // 現在表示ロードの世代（高速ナビでの追い越し対策）

    // 先読みキャッシュの保持窓（先読み対象＝Trim で保護する対象。WindowPaths() 参照）。
    // 選択集合が無いとき: 現在位置（焦点）の前後 N 枚（位置窓）。設定で変更可（ApplyPreviewSettings）。
    private int _prefetchForward = 2;
    private int _prefetchBackward = 2;
    // 選択集合があるとき: 「位置窓（狭め）」＋「メンバー窓（巡回順で前後）」の和集合。
    // 素の ←/→（MoveFocusWithinSelection）はメンバー間を巡回するため、次に表示される可能性が
    // 高いのは位置的な隣接枚ではなくメンバーの前後。位置窓も残すのは、Ctrl+←/→
    // （MoveFocusKeepingSelection＝集合を変えず焦点だけ位置移動）で集合外へ焦点が出る操作にも
    // 備えるため。
    private const int SelectionPositionForward = 1;
    private const int SelectionPositionBackward = 1;
    private const int SelectionMemberForward = 2;
    private const int SelectionMemberBackward = 1;

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
    private TimeSpan _rateWindow = TimeSpan.FromMilliseconds(1500);                           // レートを見る窓（設定で変更可）
    private int _rateBudget = 3;                                                              // 窓内で即デコードを許す枚数（設定で変更可）
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

    /// <summary>
    /// 設定（ズーム段・先読み枚数・連打抑制レート・キャッシュ予算）をプレビューへ反映する。
    /// ViewModel 注入時（起動時）と、設定ダイアログ保存時（<see cref="PhotoStatusBar"/> 経由）に呼ばれる。
    /// 同時デコード数（Semaphore サイズ）だけは即時反映せず、次回起動時の <see cref="RebuildCacheForConcurrency"/> に委ねる。
    /// </summary>
    public void ApplyPreviewSettings(AppSettings s)
    {
        // ズーム段: 正の有効値のみ、重複除去・昇順。空になったら既定へフォールバック。
        var stops = (s.ZoomStops ?? new())
            .Where(v => v > 0 && !double.IsNaN(v) && !double.IsInfinity(v))
            .Distinct()
            .OrderBy(v => v)
            .ToArray();
        if (stops.Length == 0)
            stops = AppSettings.DefaultZoomStops.ToArray();
        _viewport.ZoomStops = stops;
        // ズーム段が既定上限（16.0）を超えて設定されても弾かれないよう、クランプ上限を段の最大に追従させる。
        _viewport.MaxScale = Math.Max(16.0, stops[^1]);

        // 先読み枚数（過大値はメモリ保護のため上限クランプ）。
        _prefetchForward = Math.Clamp(s.PrefetchForward, 0, 50);
        _prefetchBackward = Math.Clamp(s.PrefetchBackward, 0, 50);

        // 連打抑制のレート。
        _rateBudget = Math.Max(1, s.RateBudget);
        _rateWindow = TimeSpan.FromMilliseconds(Math.Clamp(s.RateWindowMs, 100, 60000));

        // キャッシュのバイト予算（GB → bytes）。
        _cache.MaxCacheBytes = (long)(Math.Max(0.1, s.CacheBudgetGB) * (1L << 30));
    }

    /// <summary>
    /// 同時デコード数が現在のキャッシュと異なるとき、キャッシュを作り直して反映する。Semaphore は
    /// 構築時にサイズ決定するため作り直しが必要。ViewModel 注入時（起動時・キャッシュが空のうち）にのみ呼ぶ
    /// （実行中に呼ぶと保持中のデコード済み画像を失うため、設定ダイアログ保存では呼ばない＝再起動後に反映）。
    /// </summary>
    private void RebuildCacheForConcurrency(int concurrency)
    {
        concurrency = Math.Clamp(concurrency, 1, 16);
        if (_cache.MaxConcurrentDecodes == concurrency) return;

        _cache.Changed -= RefreshCacheOverlay;
        long budget = _cache.MaxCacheBytes;
        _cache = new PreviewBitmapCache(concurrency) { MaxCacheBytes = budget };
        _cache.Changed += RefreshCacheOverlay;
        _cache.IsWanted = IsPathInWindow;
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
                // 同時デコード数は Semaphore を構築時にサイズ決定するため、注入時（＝起動時）に一度だけ反映する。
                RebuildCacheForConcurrency(_viewModel.Settings.MaxConcurrentDecodes);
                ApplyPreviewSettings(_viewModel.Settings);
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
    /// キャッシュ済みは常に即表示。未キャッシュは直近 <see cref="_rateWindow"/> 内のデコード回数が
    /// <see cref="_rateBudget"/> 未満なら即デコード、超過（押しっぱなしの大量連発）なら間引き、
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
        if (PrunedDecodeCount(now) < _rateBudget)
        {
            _recentDecodes.Enqueue(now);
            LoadCurrentAsync(preserveView: true, prefetch: false);
        }
        RestartSettleTimer();
    }

    /// <summary>直近 <see cref="_rateWindow"/> より古いデコード時刻を捨て、窓内の残数を返す。</summary>
    private int PrunedDecodeCount(DateTime now)
    {
        while (_recentDecodes.Count > 0 && now - _recentDecodes.Peek() > _rateWindow)
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

    /// <summary>
    /// 新画像が表示中の <see cref="_bitmap"/> と同一寸法なら、作り直さず既存ビットマップへ
    /// <see cref="CanvasBitmap.SetPixelBytes(byte[])"/> で上書き転送して再利用する。
    /// VRAM の確保/解放 churn を避けるための最適化（連写フォルダではほぼ常に同寸）。
    /// キャッシュのバイト列（密詰め BGRA8）をそのまま渡すため、切替時の CPU コピーは発生しない。
    /// 成功時 true。寸法違い・転送失敗（デバイスロスト等）は false を返し、
    /// 呼び出し側が CreateFromBytes での作り直しへフォールバックする。
    /// </summary>
    private bool TryUpdateBitmapInPlace(PixelFrame frame)
    {
        if (_bitmap == null) return false;
        if (_bitmap.SizeInPixels.Width != (uint)frame.Width ||
            _bitmap.SizeInPixels.Height != (uint)frame.Height) return false;

        try
        {
            _bitmap.SetPixelBytes(frame.Bytes);
            return true;
        }
        catch
        {
            // デバイスロスト等。false で作り直しへ（それも失敗すれば null 表示→CreateResources 経由で復帰）。
            return false;
        }
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
            _navBitmap?.Dispose();
            _navBitmap = null;
            _navBitmapDirty = true;
            InvalidateAll();
            return;
        }

        var frame = await _cache.GetAsync(photo.Meta.Path, forDisplay: true);
        if (token != _loadToken)
        {
            // 新しい読み込みに追い越された。表示は最新側に任せるが、ここで Trim を
            // 通さないと押しっぱなしナビ中に Trim が一度も走らずキャッシュが膨張する。
            // WindowPaths() は現在の FocusedPhoto 基準なので常に最新窓へ収束する。
            _cache.Trim(WindowPaths());
            RefreshCacheOverlay();
            return;
        }

        // メインメモリの BGRA8 バイト列から GPU へ転送。同一寸法なら既存 _bitmap へ SetPixelBytes で
        // 上書き転送して再利用する（VRAM の確保/解放 churn 回避・CPU コピーなし。連写フォルダでは
        // ほぼ常に同寸）。寸法違い・初回は CreateFromBytes で作り直す。稀にデバイスロスト直後だと
        // 転送/生成に失敗しうるが、その場合は CreateResources → ResetCacheAndReload 経由で
        // 再ロードされるので null 表示で流してよい。
        CanvasBitmap? bmp = null;
        if (frame != null)
        {
            if (TryUpdateBitmapInPlace(frame))
            {
                bmp = _bitmap; // 再利用（同一インスタンス）
            }
            else
            {
                try
                {
                    // dpi=96 明示＝SizeInPixels と Bounds の基準を一致させる（既存の描画は SizeInPixels 基準）。
                    bmp = CanvasBitmap.CreateFromBytes(
                        MainCanvas, frame.Bytes, frame.Width, frame.Height,
                        DirectXPixelFormat.B8G8R8A8UIntNormalized, 96);
                }
                catch { bmp = null; }
            }
        }
        if (!ReferenceEquals(_bitmap, bmp)) _bitmap?.Dispose();
        _bitmap = bmp;
        _currentMeta = bmp != null ? photo.Meta : null;
        // 写真が切り替わった（同寸の SetPixelBytes 再利用でも中身は別写真）ので、ナビ用縮小
        // キャッシュは必ず作り直す。参照比較では検出できないため無条件でフラグを立てる。
        _navBitmapDirty = true;
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
        // 両隣が全部キャッシュ済みの移動では Changed が発火せず（ヒットは LastUse 更新のみ・
        // Trim も削除ゼロなら発火しない）、窓ラベルが古いまま残るため、ナビゲーション後に明示更新する。
        // 非表示中は RefreshCacheOverlay 冒頭のガードで即 return するのでコストなし。
        RefreshCacheOverlay();
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
    /// デバイス再生成・ロスト復帰時に呼ばれる。キャッシュは byte[]（PixelFrame・デバイス非依存）の
    /// ため、デバイス再生成/DPI 変更でも破棄不要。旧デバイスに属する表示中の CanvasBitmap だけ破棄し、
    /// キャッシュヒットの再転送で即復帰する。
    /// </summary>
    private void ResetCacheAndReload()
    {
        _bitmap?.Dispose();
        _bitmap = null;
        _currentMeta = null;
        // ナビ用縮小キャッシュも旧デバイスのリソースなので破棄（新デバイスで描くと例外になる）。
        _navBitmap?.Dispose();
        _navBitmap = null;
        _navBitmapDirty = true;
        // デバイス再生成/DPI 変更時は現在のズーム状態を保ったまま即時再ロードする（レート制限はバイパス）。
        _settleTimer?.Stop();
        _recentDecodes.Clear();
        LoadCurrentAsync(preserveView: true);
    }

    /// <summary>
    /// キャッシュ内容のデバッグオーバーレイ（<see cref="CacheOverlay"/>）を最新化する。
    /// キャッシュ変更通知（<see cref="PreviewBitmapCache.Changed"/>）から呼ばれ、表示中のときだけ更新する。
    /// 通知は非同期ロード継続（UI スレッド）から来るが、安全のため UI スレッドへ束ねる。
    /// <para>
    /// 表示行には「窓分類（フォーカス/選択窓/位置窓/窓外）」「表示実績（表示済み/未表示）」
    /// 「容量(MB)」「寸法」を付け、並び順はフィルムストリップ（Photos の表示順）と同じにする
    /// （読込中・待機中も同列。キャッシュがフィルム上のどの範囲を覆っているかを読みやすくする）。
    /// ラベルはデバッグ専用オーバーレイなのでローカライズせず日本語ハードコードのまま（resw 追加は不要）。
    /// </para>
    /// </summary>
    private void RefreshCacheOverlay()
    {
        if (CacheOverlay.Visibility != Visibility.Visible) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            var items = _cache.Snapshot();
            var window = WindowEntries();
            // 窓分類の辞書（先勝ち＝WindowEntries の重複除去ルールと一致）。
            var windowSlots = new Dictionary<string, WindowSlot>(StringComparer.OrdinalIgnoreCase);
            foreach (var (path, slot) in window)
                if (!windowSlots.ContainsKey(path)) windowSlots[path] = slot;

            // 並び順＝フィルムストリップ（Photos の表示順）と同じ。読込中・待機中も状態で
            // グループ化せず同列に並べる（キャッシュがフィルム上のどの範囲を覆っているかを読む用途）。
            // Photos に無いパス（フィルタ変更で絞込結果から外れた残留キャッシュ等）は末尾にファイル名順。
            var photoIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var photos = _viewModel?.Photos;
            if (photos != null)
                for (int i = 0; i < photos.Count; i++)
                    photoIndex.TryAdd(photos[i].Meta.Path, i);

            var ordered = items
                .OrderBy(i => photoIndex.TryGetValue(i.Path, out int idx) ? idx : int.MaxValue)
                .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // ファイル名列は全項目中の最大文字数で揃える（半角ファイル名前提の桁揃え）。
            int nameWidth = ordered.Count > 0 ? ordered.Max(i => i.Name.Length) : 0;

            CachedFileNames.Clear();
            foreach (var item in ordered)
            {
                string namePad = item.Name.PadRight(nameWidth);
                switch (item.State)
                {
                    case CacheItemState.Cached:
                        double mb = item.Bytes / (1024.0 * 1024.0);
                        string dims = $"{item.Width}×{item.Height}".PadRight(11);
                        string label = windowSlots.TryGetValue(item.Path, out var slot)
                            ? slot switch
                            {
                                WindowSlot.Focus => "フォーカス",
                                WindowSlot.Member => "選択窓",
                                _ => "位置窓",
                            }
                            : "窓外";
                        label += "・" + (item.WasDisplayed ? "表示済み" : "未表示");
                        CachedFileNames.Add(new CacheEntry(
                            $"{namePad}  {mb,4:0}MB  {dims}{label}", CachedBrush));
                        break;
                    case CacheItemState.Loading:
                        CachedFileNames.Add(new CacheEntry($"{namePad}  （読込中）", LoadingBrush));
                        break;
                    default: // Waiting
                        CachedFileNames.Add(new CacheEntry($"{namePad}  （待機中）", WaitingBrush));
                        break;
                }
            }

            // ヘッダ集計: デコード済み件数/合計MB/予算MB、直近デコード回数/レート予算、表示中の VRAM 目安。
            var cachedItems = items.Where(i => i.State == CacheItemState.Cached).ToList();
            double totalMb = cachedItems.Sum(i => i.Bytes) / (1024.0 * 1024.0);
            double budgetMb = _cache.MaxCacheBytes / (1024.0 * 1024.0);
            string summary = $"キャッシュ {cachedItems.Count}枚 {totalMb:0}MB / {budgetMb:0}MB" +
                              $"   直近デコード {PrunedDecodeCount(DateTime.UtcNow)}/{_rateBudget}";
            if (_bitmap != null && _currentMeta != null)
            {
                double vramMb = _bitmap.SizeInPixels.Width * (double)_bitmap.SizeInPixels.Height * 4 / (1024.0 * 1024.0);
                summary += $"\n表示中: {_currentMeta.FileName}（VRAM ≈{vramMb:0}MB）";
            }
            CacheOverlaySummary.Text = summary;
        });
    }

    /// <summary>
    /// 現在の保持窓（先読み対象＝Trim 保護対象）のファイルパス。<see cref="Prefetch"/>・
    /// <see cref="PreviewBitmapCache.Trim"/> の保護集合・<see cref="IsPathInWindow"/> の 3 箇所すべてが
    /// このメソッド経由なので、ここを変えるだけで全部に反映される。実体は <see cref="WindowEntries"/>
    /// （分類付き）で、本メソッドはその Path 射影。
    /// </summary>
    private IEnumerable<string> WindowPaths() => WindowEntries().Select(e => e.Path);

    /// <summary>
    /// 現在の保持窓を、由来分類（<see cref="WindowSlot"/>）付きで返す（デバッグオーバーレイのラベル用）。
    /// <see cref="WindowPaths"/> はこの Path 射影＝列挙順・内容は完全一致する。
    /// <para>
    /// 選択集合（<see cref="MainViewModel.SelectedPhotos"/>）が空のときは従来どおり位置窓のみ
    /// （焦点自身の要素だけ <see cref="WindowSlot.Focus"/>、残りは <see cref="WindowSlot.Position"/>）。
    /// 選択集合があるときは「位置窓（前後1）」と「メンバー窓（巡回順で前後）」の和集合を返す。
    /// yield 順は「焦点 → メンバー窓 → 位置窓」＝<see cref="Prefetch"/> はこの列挙順でゲート
    /// （同時2本）に並ぶため、巡回移動（素の ←/→）で次に表示される可能性が高いメンバーを
    /// 位置的な隣接枚より優先して先読みする狙い。重複除去は「先に足した分類が勝つ」。
    /// </para>
    /// </summary>
    private List<(string Path, WindowSlot Slot)> WindowEntries()
    {
        var vm = _viewModel;
        if (vm?.FocusedPhoto == null) return new List<(string, WindowSlot)>();

        if (vm.SelectedPhotos.Count == 0)
        {
            // 選択集合なし: 従来どおり焦点の位置窓のみ。焦点自身の index だけ Focus、他は Position。
            int index = vm.Photos.IndexOf(vm.FocusedPhoto);
            if (index < 0) return new List<(string, WindowSlot)>();

            int start = Math.Max(0, index - _prefetchBackward);
            int end = Math.Min(vm.Photos.Count - 1, index + _prefetchForward);
            var positionOnly = new List<(string, WindowSlot)>(end - start + 1);
            for (int i = start; i <= end; i++)
                positionOnly.Add((vm.Photos[i].Meta.Path, i == index ? WindowSlot.Focus : WindowSlot.Position));
            return positionOnly;
        }

        // 選択集合あり: 重複除去しつつ「焦点 → メンバー窓 → 位置窓」の順で返す（先勝ち）。
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<(string Path, WindowSlot Slot)>();

        void Add(string path, WindowSlot slot)
        {
            if (seen.Add(path)) result.Add((path, slot));
        }

        Add(vm.FocusedPhoto.Meta.Path, WindowSlot.Focus);

        // メンバー窓: MainViewModel.MoveFocusWithinSelection と同じ「Photos 表示順のメンバー一覧＋
        // modulo 巻き戻し」でアンカーを決める（素の ←/→ が実際に辿る順序と一致させる）。
        // 焦点がメンバーに含まれる場合は焦点の位置を、含まれない場合（Ctrl+←/→ で集合外へ出た等）は
        // MoveFocusWithinSelection の「delta>0→先頭 / delta<0→末尾」規則に合わせて両端を基準にする。
        var ordered = vm.Photos.Where(p => p.IsInSelection).ToList();
        int n = ordered.Count;
        if (n > 0)
        {
            int cur = ordered.IndexOf(vm.FocusedPhoto); // 集合外なら -1

            for (int d = 1; d <= SelectionMemberForward; d++)
            {
                // 焦点がメンバー外のとき、delta>0（→ 相当）の着地点は先頭（index 0）なので
                // 1 手目はその先頭そのもの、以降は先頭から巡回して先読みする。
                int idx = cur >= 0 ? (((cur + d) % n) + n) % n : (d - 1) % n;
                Add(ordered[idx].Meta.Path, WindowSlot.Member);
            }
            for (int d = 1; d <= SelectionMemberBackward; d++)
            {
                // delta<0（← 相当）の着地点は末尾（index n-1）。
                int idx = cur >= 0 ? (((cur - d) % n) + n) % n : (n - 1 - (d - 1) % n);
                Add(ordered[idx].Meta.Path, WindowSlot.Member);
            }
        }

        // 位置窓（狭め）: Ctrl+←/→ で集合外へ焦点が出る操作にも備える。
        int posIndex = vm.Photos.IndexOf(vm.FocusedPhoto);
        if (posIndex >= 0)
        {
            int start = Math.Max(0, posIndex - SelectionPositionBackward);
            int end = Math.Min(vm.Photos.Count - 1, posIndex + SelectionPositionForward);
            for (int i = start; i <= end; i++)
                Add(vm.Photos[i].Meta.Path, WindowSlot.Position);
        }

        return result;
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
