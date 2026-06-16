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

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

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
        (RootFrame.Content as MainPage)?.HandleGlobalKeyDown(e);
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        (RootFrame.Content as MainPage)?.SaveLeftPaneLayout();
    }
}
