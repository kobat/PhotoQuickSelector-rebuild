using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace PhotoQuickSelector_App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// The main application window. Use <c>App.Window</c> from any class that needs
    /// the window reference (for dialogs, pickers, interop, etc.).
    /// </summary>
    public static Window Window { get; private set; } = null!;

    /// <summary>
    /// The UI thread dispatcher. Use <c>App.DispatcherQueue</c> to marshal calls
    /// to the UI thread. Fully qualified to avoid CS0104 ambiguity with
    /// <see cref="Windows.System.DispatcherQueue"/>.
    /// </summary>
    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;

    /// <summary>
    /// The native window handle (HWND). Use for file pickers,
    /// <c>DataTransferManager</c>, and any WinRT interop that requires
    /// <c>InitializeWithWindow</c>.
    /// </summary>
    public static nint WindowHandle =>
        WinRT.Interop.WindowNative.GetWindowHandle(Window);

    /// <summary>
    /// Initializes the singleton application object.
    /// </summary>
    public App()
    {
        // どのリソース参照よりも先（InitializeComponent より前）に表示言語を確定させる。
        ApplyLanguageOverride();
        InitializeComponent();
    }

    /// <summary>
    /// <see cref="AppSettings.Language"/>（"ja"/"en"）に応じて表示言語を上書きする。
    /// 空（自動）のときは何も設定しない＝OS の表示言語に追従。
    /// PrimaryLanguageOverride は一度設定するとプロセス内で解除不可（""/null は 0x80070057）だが、
    /// プロセスをまたいで永続化はされないため「毎起動ここで設定 or 触らない」で完結する（検証済み）。
    /// </summary>
    private static void ApplyLanguageOverride()
    {
        try
        {
            var tag = AppSettings.Load().Language switch
            {
                "ja" => "ja-JP",
                "en" => "en-US",
                _ => null,
            };
            if (tag != null)
                Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = tag;
        }
        catch
        {
            // 言語設定の失敗でアプリを起動不能にしない（既定＝OS 言語で続行）
        }
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Window.Activate();
    }
}
