using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace PhotoQuickSelector_App;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // システム標準タイトルバーを使う（ExtendsContentIntoTitleBar=false）。
        // カスタムタイトルバー（ExtendsContentIntoTitleBar=true）にすると、×/最小化/最大化が
        // XAML の非クライアント入力経路＝フォーカス機構と同じ土俵で処理され、画像操作後に
        // フォーカスが GridViewItem/CanvasControl へ乗った状態だと、×の押下がフォーカス後退に
        // 消費されて「1回で閉じない（時々2回必要）」レースが起きる。診断ログ＋実機実験で確定済み
        // （TitleBar コントロール／旧来 SetTitleBar の双方で再現、false でのみ100%確実）。
        // 詳細はメモリ close-button-titlebar-focus-race を参照。
        ExtendsContentIntoTitleBar = false;

        AppWindow.SetIcon("Assets/AppIcon.ico");

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));

        // キー入力を Window 直下のルート要素（RootGrid）で集約する。
        // PreviewKeyDown はルート→フォーカス要素へ tunneling するため、RootGrid が最初に受け取る。
        // ここで処理して Handled にすれば、子（FilmStrip の ListView 等）の bubbling KeyDown による
        // ナビゲーションよりも先にキーを奪える（Alt+矢印が画像切替になる誤動作を防ぐ）。tunneling の
        // Preview と bubbling の KeyDown はイベントデータを共有するため、Preview を Handled にすると
        // 後続 KeyDown も抑止される。画像クリックでフォーカスが祖先 ScrollViewer へ移ってもルートは
        // 常に最初に届くため、フォーカス位置によらず評価/ナビキーが効く。
        RootGrid.PreviewKeyDown += RootGrid_PreviewKeyDown;

        // 終了時に左ペインの幅／折りたたみ状態を保存する。
        Closed += MainWindow_Closed;
    }

    private void RootGrid_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled) return;

        // F11: フルスクリーン表示のトグル。AppWindow を所有する Window 側で完結させる。
        // tunneling のルートで拾うのでフォーカス位置によらず確実に届く（評価/ナビキーと競合しない）。
        if (e.Key == Windows.System.VirtualKey.F11)
        {
            ToggleFullScreen();
            e.Handled = true;
            return;
        }

        // Esc: フルスクリーン中のときだけ通常表示へ戻す。Esc は SPEC §3-7 で本来「選択リセット」用途
        // なので、全画面でないときは未処理のまま通す（将来用途を潰さない）。PreviewKeyDown(tunneling)
        // はフォーカス管理の Esc 消費より前に届くため、ここで拾えば確実（実キーボードで要確認）。
        if (e.Key == Windows.System.VirtualKey.Escape &&
            AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
        {
            AppWindow.SetPresenter(AppWindowPresenterKind.Default);
            e.Handled = true;
            return;
        }

        (RootFrame.Content as MainPage)?.HandleGlobalKeyDown(e);
    }

    /// <summary>
    /// フルスクリーン表示と通常表示を切り替える。WinAppSDK 標準の
    /// <see cref="AppWindowPresenterKind.FullScreen"/>（枠・タイトルバー・タスクバーごと非表示）を使う。
    /// 通常へ戻すとシステム標準タイトルバー（<c>ExtendsContentIntoTitleBar=false</c>）が復活するため、
    /// ×ボタンのフォーカスレース対策（close-button-titlebar-focus-race）はそのまま維持される。
    /// F11 キーとステータスバーの全画面ボタンの両方から呼ばれる。
    /// </summary>
    public void ToggleFullScreen()
    {
        var kind = AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen
            ? AppWindowPresenterKind.Default
            : AppWindowPresenterKind.FullScreen;
        AppWindow.SetPresenter(kind);
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (RootFrame.Content is MainPage page)
        {
            // セッション（フォルダ/選択/表示モード/フィルタ）を控えてから、左ペイン状態と一緒に保存。
            page.ViewModel.CaptureSession();
            page.SaveLeftPaneLayout(); // 末尾で Settings.Save() を1回呼ぶ
        }
    }
}
