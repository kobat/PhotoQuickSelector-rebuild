using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PhotoQuickSelector_App.ViewModels;

namespace PhotoQuickSelector_App;

/// <summary>
/// メイン画面の骨組み。3カラム（左ナビ／スプリッター／右）配置と、ペイン間調停
/// （左ペイン開閉・幅の永続化、MainWindow からのキー委譲）を担う。中身は各ペインの独立コントロールへ委譲。
/// </summary>
public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel { get; } = new();

    private double _lastLeftWidth = 300;

    // --- 左ペインのピン留め/フライアウト表示 ---
    private bool _pinned = true;
    private bool _flyoutOpen;

    /// <summary>左ペインがフライアウト表示中か（MainWindow の Esc 分岐から参照）。</summary>
    public bool IsLeftPaneFlyoutOpen => _flyoutOpen;

    // --- 完全全画面モード（Shift+F）。入る前の状態を退避して解除時に正確復元する ---
    private bool _fullImageMode;
    private GridLength _savedLeftWidth;
    private double _savedLeftMinWidth;
    private Visibility _savedStatusBarVisibility;
    private Thickness _savedRightPanePadding;
    private bool _wasPreviewMode;

    /// <summary>完全全画面モード中か（MainWindow の Esc 分岐から参照）。</summary>
    public bool IsFullImageMode => _fullImageMode;

    public MainPage()
    {
        InitializeComponent();
        Loaded += MainPage_Loaded;

        // 各ペイン（左ナビ／ステータスバー／サムネイルグリッド／大画面プレビュー）に共有ビューモデルを注入する。
        LeftNav.ViewModel = ViewModel;
        StatusBar.ViewModel = ViewModel;
        ThumbnailGrid.ViewModel = ViewModel;
        Preview.ViewModel = ViewModel;
        // ステータスバーの開閉ボタンは LeftColumn（骨組み）を操作するので MainPage が受ける。
        StatusBar.ToggleLeftPaneRequested += (_, _) => ToggleLeftPane();
        // 全画面ボタンは AppWindow を持つ MainWindow で切り替える（F11 と同じ経路）。
        StatusBar.ToggleFullScreenRequested += (_, _) => (App.Window as MainWindow)?.ToggleFullScreen();
        // メニュー「完全全画面」は複数要素にまたがるので MainPage のコーディネータへ（Shift+F と同じ経路）。
        StatusBar.ToggleFullImageRequested += (_, _) => ToggleFullImageMode();
        // メニュー「イマーシブ表示」はグリッド表示中でも押せる（Shift+F の ToggleFullImageMode と同じ流儀。
        // EnterPreview は空フォルダなら no-op なので、実際に入れたときだけイマーシブを適用する）。
        StatusBar.ToggleImmersiveRequested += (_, _) =>
        {
            if (!ViewModel.IsPreviewMode)
            {
                ViewModel.EnterPreview();
                if (ViewModel.IsPreviewMode) Preview.SetImmersive(true);
            }
            else
            {
                Preview.SetImmersive(!Preview.IsImmersive);
            }
        };
        // メニュー「EXIF 詳細パネル」は Preview 内の操作（E キーと同じ経路）。
        StatusBar.ToggleExifPanelRequested += (_, _) => Preview.ToggleExifPanel();
        // 設定ダイアログ保存時、ズーム段・先読み・レート・キャッシュ予算をプレビューへ反映（同時デコード数のみ再起動後）。
        StatusBar.SettingsChanged += (_, _) => Preview.ApplyPreviewSettings(ViewModel.Settings);
        // メニューのチェック表示用の状態プロバイダ（Preview / MainWindow から供給）。
        StatusBar.IsImmersiveProvider = () => Preview.IsImmersive;
        StatusBar.IsExifPanelProvider = () => Preview.IsExifPanelShown;
        StatusBar.IsFullScreenProvider = () => (App.Window as MainWindow)?.IsFullScreen ?? false;
        // メイン画像の右クリックメニュー（全画面表示/完全全画面はハンバーガーメニューと同じ経路で委譲）。
        Preview.ToggleFullScreenRequested += (_, _) => (App.Window as MainWindow)?.ToggleFullScreen();
        Preview.ToggleFullImageRequested += (_, _) => ToggleFullImageMode();
        Preview.IsFullScreenProvider = () => (App.Window as MainWindow)?.IsFullScreen ?? false;
        // 左ペインの幅変化（ボタン/スプリッター/完全全画面/復元のいずれでも）を唯一の起点に開閉ボタンの
        // グリフ／ツールチップを追従させる。LeftNav は左カラムの子なので幅0で ActualWidth=0 になる。
        LeftNav.SizeChanged += (_, _) => StatusBar.UpdateLeftPaneGlyph(LeftNav.ActualWidth > 0);
        // ピン留め切替（左ペイン内のボタン）。ドッキング⇄フライアウトの遷移は MainPage が一元管理する。
        LeftNav.TogglePinRequested += (_, _) => TogglePin();
        // フォルダ読み込みでフライアウトを自動クローズする（ピン留め時は _flyoutOpen=false なので no-op）。
        LeftNav.FolderLoaded += (_, _) => CloseLeftPaneFlyout();
        // 評価データファイル（sqlite）の初回作成確認ダイアログ。VM は XamlRoot を持たないので View が出す。
        ViewModel.ConfirmCreateAsync = ConfirmCreateStoreAsync;
    }

    /// <summary>
    /// 対象フォルダにまだ評価データファイルが無いとき、最初の評価操作の直前に作成可否を尋ねる。
    /// OK で true（＝ファイル作成して評価を保存）、キャンセルで false（＝何もしない）。
    /// </summary>
    private bool _creationDialogOpen;

    private async Task<bool> ConfirmCreateStoreAsync()
    {
        if (_creationDialogOpen) return false; // 多重表示防止
        _creationDialogOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                Title = Loc.Get("Msg_ConfirmCreateStoreTitle"),
                Content = Loc.Get("Msg_ConfirmCreateStoreContent"),
                PrimaryButtonText = Loc.Get("Msg_Create"),
                CloseButtonText = Loc.Get("Msg_Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally
        {
            _creationDialogOpen = false;
        }
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        RestoreLeftPaneLayout();
        StatusBar.UpdateLeftPaneGlyph(LeftColumn.ActualWidth > 0); // 復元直後の初期同期（以後は SizeChanged が追従）
        LeftNav.UpdatePinGlyph(_pinned); // ピンボタンの初期グリフ同期
        _ = RestoreSessionAsync();
    }

    // --- 前回セッションの復元（開いていたフォルダ／選択ファイル／表示モード／フィルタ） ---

    /// <summary>
    /// 起動時に <see cref="AppSettings.LastSession"/> を復元する。フォルダ/ファイルの消失や
    /// 絞り込みで外れたケースは <see cref="MainViewModel.LoadFolderAsync"/> 側が防御的に処理する。
    /// </summary>
    private async Task RestoreSessionAsync()
    {
        var session = ViewModel.Settings.LastSession;
        var folder = session.FolderPath;
        if (string.IsNullOrEmpty(folder)) return;

        // フォルダが存在しない（削除/未接続の外付け・ネットワーク等）→ 復元中止。最近一覧は残す。
        if (!Directory.Exists(folder))
        {
            ViewModel.StatusText = Loc.Get("Status_PreviousFolderNotFound", folder);
            return;
        }

        // フィルタを先に復元する（LoadFolderAsync 末尾の ApplyFilter が反映するため、選択復元より前に）。
        ViewModel.Filter.ApplyState(session.Filter);

        await ViewModel.LoadFolderAsync(folder, session.SelectedFileName, session.IsPreviewMode);

        // 左ツリーも同フォルダへ展開＆選択（お気に入りクリックと同じ挙動）。
        await LeftNav.ExpandAndSelectFolderAsync(folder);
    }

    // --- 左ペインの幅／折りたたみ状態の復元・保存（AppSettings） ---

    private void RestoreLeftPaneLayout()
    {
        var s = ViewModel.Settings;
        _lastLeftWidth = s.LeftPaneWidth > 0 ? s.LeftPaneWidth : 300;
        _pinned = s.LeftPanePinned;
        if (!_pinned)
        {
            // フライアウトモード: 左カラムは常に幅0・スプリッター非表示（LeftPaneCollapsed は無視）。
            // フライアウト自体の開閉状態は永続化しない＝起動時は常に閉。
            LeftColumn.Width = new GridLength(0);
            LeftSplitter.Visibility = Visibility.Collapsed;
            return;
        }
        LeftColumn.Width = s.LeftPaneCollapsed
            ? new GridLength(0)
            : new GridLength(_lastLeftWidth);
    }

    /// <summary>左ペインの現在の幅／折りたたみ／ピン状態を設定へ書き戻して保存する（ウィンドウ終了時）。</summary>
    public void SaveLeftPaneLayout()
    {
        var s = ViewModel.Settings;
        if (LeftColumn.ActualWidth > 0)
        {
            s.LeftPaneCollapsed = false;
            s.LeftPaneWidth = LeftColumn.ActualWidth;
        }
        else
        {
            s.LeftPaneCollapsed = true;
            s.LeftPaneWidth = _lastLeftWidth > 0 ? _lastLeftWidth : 300;
        }
        s.LeftPanePinned = _pinned;
        s.Save();
    }

    // --- 左ペインの折りたたみ（ステータスバーの ToggleLeftPaneRequested から呼ばれる） ---

    private void ToggleLeftPane()
    {
        if (!_pinned)
        {
            // ピン解除時は常にフライアウトの開閉のみ（左カラム幅は常に0・GridSplitter は使わない）。
            if (_flyoutOpen) CloseLeftPaneFlyout();
            else OpenLeftPaneFlyout();
            return;
        }

        if (LeftColumn.ActualWidth > 0)
        {
            _lastLeftWidth = LeftColumn.ActualWidth;
            LeftColumn.Width = new GridLength(0);
        }
        else
        {
            LeftColumn.Width = new GridLength(_lastLeftWidth <= 0 ? 300 : _lastLeftWidth);
        }
    }

    // --- 左ペインのピン留め/フライアウト表示（左ペイン内のピンボタン・LeftNav.TogglePinRequested から） ---

    /// <summary>
    /// 左ペインをオーバーレイとして右ペインの上に表示する（ピン解除時、ステータスバーの開閉ボタンから）。
    /// <see cref="LeftNav"/> は再ペアレントせず、ColumnSpan/Width/ZIndex の切替だけでフライアウト化する
    /// （WinUI TreeView は再ペアレントで内部状態が壊れるリスクがあるため）。
    /// </summary>
    private void OpenLeftPaneFlyout()
    {
        if (_flyoutOpen) return;

        var width = _lastLeftWidth <= 0 ? 300 : _lastLeftWidth;
        FlyoutBackdrop.Width = width;
        FlyoutBackdrop.Visibility = Visibility.Visible;
        FlyoutDismissLayer.Visibility = Visibility.Visible;
        Grid.SetColumnSpan(LeftNav, 3);
        LeftNav.HorizontalAlignment = HorizontalAlignment.Left;
        LeftNav.Width = width;
        Canvas.SetZIndex(LeftNav, 12);
        _flyoutOpen = true;
        StatusBar.UpdateLeftPaneGlyph(true);
    }

    /// <summary>
    /// フライアウト表示中の左ペインを閉じる（再トグル／外側クリック／Esc／フォルダ読み込みのいずれからも）。
    /// LeftNav のレイアウトプロパティを開く前の状態へ戻す。<c>Width</c> の明示指定を <c>NaN</c> で
    /// 解除し忘れると、幅0のカラムに置かれていても直前に設定した px 幅のまま描画され続けてしまう。
    /// </summary>
    public void CloseLeftPaneFlyout()
    {
        if (!_flyoutOpen) return;

        Grid.SetColumnSpan(LeftNav, 1);
        LeftNav.HorizontalAlignment = HorizontalAlignment.Stretch;
        LeftNav.Width = double.NaN;
        Canvas.SetZIndex(LeftNav, 0);
        FlyoutBackdrop.Visibility = Visibility.Collapsed;
        FlyoutDismissLayer.Visibility = Visibility.Collapsed;
        _flyoutOpen = false;
        StatusBar.UpdateLeftPaneGlyph(false);
    }

    /// <summary>ライトディスミス層のクリックで閉じる。下の写真が誤選択されないようイベントを消費する。</summary>
    private void FlyoutDismissLayer_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        CloseLeftPaneFlyout();
        e.Handled = true;
    }

    /// <summary>
    /// 左ペインのピン留め状態を切り替える（左ペイン内のピンボタンから）。ピン解除の瞬間はドッキング表示から
    /// フライアウト表示へシームレスに引き継ぐ（消えない）。ピン留めの瞬間はフライアウトを閉じてドッキング
    /// 幅（<see cref="_lastLeftWidth"/>）へ戻す。
    /// </summary>
    private void TogglePin()
    {
        if (_pinned)
        {
            if (LeftColumn.ActualWidth > 0) _lastLeftWidth = LeftColumn.ActualWidth;
            LeftColumn.Width = new GridLength(0);
            LeftSplitter.Visibility = Visibility.Collapsed;
            _pinned = false;
            OpenLeftPaneFlyout();
        }
        else
        {
            CloseLeftPaneFlyout();
            _pinned = true;
            LeftSplitter.Visibility = Visibility.Visible;
            LeftColumn.Width = new GridLength(_lastLeftWidth <= 0 ? 300 : _lastLeftWidth);
        }
        LeftNav.UpdatePinGlyph(_pinned);
    }

    // --- 完全全画面モード（Shift+F / Esc で解除） ---

    /// <summary>
    /// 完全全画面モードをトグルする。1 操作で「ウィンドウ全画面＋左ペイン非表示＋ステータスバー非表示＋
    /// プレビューのイマーシブ（右パネル/フィルム畳む）＋右ペイン余白0」を切り替える。グリッド表示中なら
    /// 先にプレビューへ入る。入る前の状態をスナップショットし、解除時に正確に復元する（元がグリッドなら
    /// グリッドへ戻す）。<c>Shift+F</c>（<see cref="MainWindow"/> のキー集約点）と <c>Esc</c> から呼ばれる。
    /// </summary>
    public void ToggleFullImageMode()
    {
        var window = App.Window as MainWindow;

        if (!_fullImageMode)
        {
            // フライアウト表示中なら閉じてから入る（フライアウトの開閉状態はスナップショットに含めない
            // ＝完全全画面を解除しても閉じた状態で復帰する）。
            CloseLeftPaneFlyout();

            // 入る: 現在状態を退避。
            _savedLeftWidth = LeftColumn.Width;
            _savedLeftMinWidth = LeftColumn.MinWidth;
            _savedStatusBarVisibility = StatusBar.Visibility;
            _savedRightPanePadding = RightPaneRoot.Padding;
            _wasPreviewMode = ViewModel.IsPreviewMode;
            if (LeftColumn.ActualWidth > 0) _lastLeftWidth = LeftColumn.ActualWidth;

            // グリッド表示中ならプレビューへ入る（空フォルダなら EnterPreview は no-op）。
            if (!ViewModel.IsPreviewMode) ViewModel.EnterPreview();

            // 全要素を畳む。MinWidth も 0 にしないと Width=0 が効かない。
            LeftColumn.MinWidth = 0;
            LeftColumn.Width = new GridLength(0);
            LeftSplitter.Visibility = Visibility.Collapsed;
            StatusBar.Visibility = Visibility.Collapsed;
            RightPaneRoot.Padding = new Thickness(0);
            Preview.SetImmersive(true);
            window?.SetFullScreen(true);

            _fullImageMode = true;
        }
        else
        {
            // 出る: スナップショットへ復元。
            window?.SetFullScreen(false);
            Preview.SetImmersive(false);
            RightPaneRoot.Padding = _savedRightPanePadding;
            StatusBar.Visibility = _savedStatusBarVisibility;
            // ピン解除中はスプリッターを復活させない（フライアウトモードでは使わないため）。
            LeftSplitter.Visibility = _pinned ? Visibility.Visible : Visibility.Collapsed;
            LeftColumn.MinWidth = _savedLeftMinWidth;
            LeftColumn.Width = _savedLeftWidth;
            // 元がグリッド表示だったらグリッドへ戻す。
            if (!_wasPreviewMode) ViewModel.ExitPreview();

            _fullImageMode = false;
        }
    }

    // --- キー操作（MainWindow ルートから委譲） ---

    /// <summary>
    /// Window 直下のルート要素（<see cref="MainWindow"/> の RootGrid）の <c>PreviewKeyDown</c> から
    /// 委譲されるキー処理。tunneling で子より先に呼ばれるフォーカス非依存の集約点なので、画像クリックで
    /// フォーカスが祖先 ScrollViewer へ移っても、フィルムストリップ（ListView）にフォーカスがあっても、
    /// 評価/ナビキーを ListView のナビゲーションより先に処理できる。処理したら <paramref name="e"/> を
    /// Handled にして後続の bubbling KeyDown（子コントロールのナビ等）を抑止する。
    /// プレビュー時は <see cref="Controls.PreviewControl.HandleKeyDown"/> へ委譲し、サムネイル一覧時は
    /// 評価キーのみ処理する（素の矢印は未処理のまま通して GridView の通常ナビへ流す）。
    /// </summary>
    public void HandleGlobalKeyDown(KeyRoutedEventArgs e)
    {
        // Ctrl+L: フィルタ ON/OFF（両モード共通、SPEC §3-7）。フライアウトは開かずトグルのみ。
        if (KeyboardModifiers.Ctrl && e.Key == Windows.System.VirtualKey.L)
        {
            ViewModel.Filter.Enabled = !ViewModel.Filter.Enabled;
            e.Handled = true;
            return;
        }

        if (ViewModel.IsPreviewMode)
        {
            if (Preview.HandleKeyDown(e.Key))
                e.Handled = true;
            return;
        }

        // 複数選択キー（Shift+←/→ / Ctrl+←/→ / Ctrl+Space / Esc）。プレビューと共通化。
        if (SelectionKeyCommands.TryHandle(e.Key, ViewModel))
        {
            e.Handled = true;
            return;
        }

        // 選択集合があるときの素 ←/→ はメンバー内で焦点を巡回する（横取り。空なら GridView の通常ナビへ）。
        if (ViewModel.SelectedPhotos.Count > 0 && KeyboardModifiers.None)
        {
            if (e.Key == Windows.System.VirtualKey.Left) { ViewModel.MovePrevious(); e.Handled = true; return; }
            if (e.Key == Windows.System.VirtualKey.Right) { ViewModel.MoveNext(); e.Handled = true; return; }
        }

        // 一括評価（Alt+数字）: 選択集合の全メンバーへ。集合が無ければ無効（消費のみ）。
        if (PhotoKeyCommands.ResolveBulkEvaluation(e.Key) is { } bulkOp)
        {
            if (ViewModel.SelectedPhotos.Count > 0)
                _ = ViewModel.ApplyEvaluationAsync(bulkOp, ViewModel.SelectedPhotos.ToList());
            e.Handled = true;
            return;
        }

        if (ViewModel.FocusedPhoto is { } item)
        {
            // 外部連携（Ctrl+E / Alt+E / Ctrl+Alt+E / Alt+S）を評価キーより先に判定（SPEC §3-8）。
            // 対象は選択集合があれば全メンバー（Ctrl+E のみ焦点1枚。TryHandle 内で解決）。
            if (PhotoFileCommands.TryHandle(e.Key, ViewModel, XamlRoot))
            {
                e.Handled = true;
            }
            else if (PhotoKeyCommands.ResolveEvaluation(e.Key) is { } op)
            {
                // 初回はファイル作成確認ダイアログを挟むため非同期 gate 経由（キーハンドラなので待たない）。
                _ = ViewModel.ApplyEvaluationAsync(op, new[] { item });
                e.Handled = true;
            }
        }
    }
}
