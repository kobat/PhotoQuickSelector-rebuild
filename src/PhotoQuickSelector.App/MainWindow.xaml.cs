using System.Runtime.InteropServices;
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

        // 標準タイトルバー（ExtendsContentIntoTitleBar=false）を、OS テーマに関わらず暗色で描かせる。
        // アプリは Dark 固定（RootGrid の RequestedTheme="Dark"）だが、非クライアント領域＝タイトルバーは
        // DWM が描くため別途このフラグが要る。ExtendsContentIntoTitleBar=true（カスタムタイトルバー）に
        // すると ×ボタンのフォーカスレースが再発するので、標準タイトルバーのまま色だけ暗くする。
        // 詳細はメモリ close-button-titlebar-focus-race を参照。
        EnableDarkTitleBar();

        // 窓/タスクバーのアイコンを設定する。
        // AppWindow.SetIcon(path) は「ディスク上の .ico ファイル」を相対パスで読むため、単一ファイル発行
        // （Assets\AppIcon.ico も resources.pri も exe 内へ埋め込まれてディスクに残らない）では失敗し、
        // 既定アイコンに戻ってしまう。そこで exe 自身に埋め込んだアイコンリソース（<ApplicationIcon> 由来）
        // を直接ロードして適用する＝ファイルパスに依存しないので packaged/フォルダ/単一ファイルすべてで効く。
        SetWindowIconFromEmbedded();

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

        // Alt+英数字（Alt+F / Alt+数字 …）押下時に鳴る Windows 標準のメニュー ビープを抑止する。
        // XAML 側で Handled にしても Win32 のメニュー処理までは止められないため、HWND サブクラスで潰す。
        SuppressAltKeyMenuBeep();

        // 終了時に左ペインの幅／折りたたみ状態を保存する。
        Closed += MainWindow_Closed;
    }

    private void RootGrid_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled) return;

        // ダイアログ/フライアウト等のポップアップが開いている間はグローバルキー集約を停止し、
        // キー入力をそのモーダル（TextBox 等）へ委ねる。これをしないと、ルートの tunneling が
        // Z や ←/→ を「ズーム/前後移動」として先取りして消費し、ダイアログ内で入力できない。
        if (RootGrid.XamlRoot is { } xamlRoot &&
            Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot).Count > 0)
            return;

        // F1: ショートカット一覧（チートシート）をモーダルで開く。ポップアップ開時は冒頭の早期 return で
        // 弾かれるため二重表示しない。tunneling のルートで拾うのでフォーカス位置によらず届く。
        if (e.Key == Windows.System.VirtualKey.F1)
        {
            _ = ShowShortcutsAsync();
            e.Handled = true;
            return;
        }

        // F11: フルスクリーン表示のトグル。AppWindow を所有する Window 側で完結させる。
        // tunneling のルートで拾うのでフォーカス位置によらず確実に届く（評価/ナビキーと競合しない）。
        if (e.Key == Windows.System.VirtualKey.F11)
        {
            ToggleFullScreen();
            e.Handled = true;
            return;
        }

        // Shift+F: 完全全画面モード（ウィンドウ全画面＋左ペイン/ステータスバー非表示＋イマーシブ＋余白0）の
        // トグル。複数コンポーネントにまたがるので MainPage のコーディネータへ委譲する。グリッド表示中でも
        // ここで拾える（F11 と同じフォーカス非依存の集約点）。素の F（イマーシブ）は MainPage→Preview 側で処理。
        if (e.Key == Windows.System.VirtualKey.F && KeyboardModifiers.Shift)
        {
            (RootFrame.Content as MainPage)?.ToggleFullImageMode();
            e.Handled = true;
            return;
        }

        // Esc: 左ペインフライアウト表示中なら閉じるのを最優先（段階的 Esc の最上段）。
        if (e.Key == Windows.System.VirtualKey.Escape &&
            RootFrame.Content is MainPage fp && fp.IsLeftPaneFlyoutOpen)
        {
            fp.CloseLeftPaneFlyout();
            e.Handled = true;
            return;
        }

        // Esc: 完全全画面モード中ならそれを解除（左ペイン/ステータスバー/余白も復元）。そうでなく素の
        // フルスクリーン中なら通常表示へ戻す。Esc は SPEC §3-7 で本来「選択リセット」用途なので、いずれでも
        // ないときは未処理のまま通す（将来用途を潰さない）。PreviewKeyDown(tunneling) はフォーカス管理の
        // Esc 消費より前に届くため、ここで拾えば確実。
        // ただし選択集合があるときは全画面解除より選択解除を優先する（段階的 Esc＝1階層ずつ剥がす）。
        // ここで消費せず HandleGlobalKeyDown → SelectionKeyCommands.ClearSelection へ通し、次の Esc（選択なし）で
        // 全画面を解除する。全画面中の選別作業でレイアウトが不意に崩れるのを防ぐ。
        if (e.Key == Windows.System.VirtualKey.Escape &&
            !(RootFrame.Content is MainPage sel && sel.ViewModel.SelectedPhotos.Count > 0))
        {
            if (RootFrame.Content is MainPage page && page.IsFullImageMode)
            {
                page.ToggleFullImageMode();
                e.Handled = true;
                return;
            }
            if (AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
            {
                AppWindow.SetPresenter(AppWindowPresenterKind.Default);
                e.Handled = true;
                return;
            }
        }

        (RootFrame.Content as MainPage)?.HandleGlobalKeyDown(e);
    }

    /// <summary>ショートカット一覧ダイアログを開く（F1 から呼ぶ。XamlRoot はルート要素のものを使う）。</summary>
    private async System.Threading.Tasks.Task ShowShortcutsAsync()
    {
        var dialog = new Controls.ShortcutsDialog { XamlRoot = RootGrid.XamlRoot };
        await dialog.ShowAsync();
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

    /// <summary>現在フルスクリーン表示中か（メニューのチェック表示用にステータスバーから参照）。</summary>
    public bool IsFullScreen => AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;

    /// <summary>
    /// ウィンドウ全画面の ON/OFF を明示指定する。完全全画面モード（<see cref="MainPage.ToggleFullImageMode"/>）の
    /// コーディネータが他要素（左ペイン/ステータスバー等）と同時に切り替えるために使う。
    /// </summary>
    public void SetFullScreen(bool on)
    {
        var kind = on ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Default;
        if (AppWindow.Presenter.Kind != kind)
            AppWindow.SetPresenter(kind);
    }

    /// <summary>
    /// DWM のイマーシブ ダークモード属性を ON にし、標準タイトルバーを暗色（濃いグレー＋明るいグリフ）で
    /// 描かせる。AppWindow ではなく Win32 の HWND に対する属性なので P/Invoke で設定する。
    /// </summary>
    private void EnableDarkTitleBar()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        int useDark = 1; // TRUE
        _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
    }

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll", SetLastError = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int pvAttribute, int cbAttribute);

    /// <summary>
    /// Alt+英数字（Alt+F / Alt+数字 …）押下時に Windows のメニュー処理が鳴らすシステム標準ビープ
    /// （いわゆる「ポーン」音）を抑止する。Alt を伴うキーは Win32 のメニュー活性化経路に入り、対応する
    /// メニュー ニーモニックが無いと <c>WM_MENUCHAR</c> の既定処理（<c>MNC_IGNORE</c>）で MessageBeep が鳴る。
    /// XAML 側で <c>KeyDown</c> を Handled にしてもこの下位経路までは止められないため、HWND をサブクラス化して
    /// <c>WM_MENUCHAR</c> を横取りし、<c>MNC_CLOSE</c> を返してメニューモードを黙って閉じる。Alt+F4/Alt+Space
    /// 等は別メッセージ経路（WM_SYSCOMMAND）なので影響しない。
    /// </summary>
    private void SuppressAltKeyMenuBeep()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _menuBeepSubclassProc = MenuBeepSubclassProc; // GC で回収されないようフィールドで保持する。
        _ = SetWindowSubclass(hwnd, _menuBeepSubclassProc, MenuBeepSubclassId, UIntPtr.Zero);
    }

    private IntPtr MenuBeepSubclassProc(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData)
    {
        // WM_MENUCHAR: ニーモニックに一致しない Alt+英数字が押されたときに送られる。ここで MNC_CLOSE
        // （HIWORD）を返すとメニューモードが無音で閉じ、既定処理のビープが鳴らない。
        if (uMsg == WM_MENUCHAR)
            return (IntPtr)(MNC_CLOSE << 16);
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private const uint WM_MENUCHAR = 0x0120;
    private const int MNC_CLOSE = 1;
    private static readonly UIntPtr MenuBeepSubclassId = (UIntPtr)1;

    // GC 対象にならないようインスタンスで保持する。サブクラスは HWND 生存中ずっと有効。
    private SubclassProc? _menuBeepSubclassProc;

    private delegate IntPtr SubclassProc(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData);

    // comctl32 のサブクラス API は「名前ではなく序数」でのみエクスポートされる（410/412/413）。
    // 名前指定の DllImport は EntryPointNotFoundException になるため EntryPoint に序数を指定する。
    [DllImport("comctl32.dll", EntryPoint = "#410", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, UIntPtr dwRefData);

    [DllImport("comctl32.dll", EntryPoint = "#412", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass);

    [DllImport("comctl32.dll", EntryPoint = "#413")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// exe に埋め込まれたアイコンリソース（<c>&lt;ApplicationIcon&gt;</c> が埋めた RT_GROUP_ICON）を
    /// ディスクのファイルに依存せず HICON としてロードし、窓/タスクバーへ適用する。
    /// 単一ファイル発行で Assets\AppIcon.ico がディスクに存在しないケースに対応する。
    /// </summary>
    private void SetWindowIconFromEmbedded()
    {
        IntPtr hModule = GetModuleHandleW(null); // 自プロセス（exe）のモジュールハンドル
        if (hModule == IntPtr.Zero)
        {
            return;
        }

        // 最初の RT_GROUP_ICON のリソース ID を取得する（ApplicationIcon が埋めたメインアイコン）。
        IntPtr iconResId = IntPtr.Zero;
        EnumResourceNamesW(hModule, RT_GROUP_ICON, (m, t, name, l) =>
        {
            iconResId = name; // 整数 ID（MAKEINTRESOURCE 形式）として渡される
            return false;     // 最初の 1 件で列挙を打ち切る
        }, IntPtr.Zero);

        if (iconResId == IntPtr.Zero)
        {
            return;
        }

        // 既定サイズ（システム大アイコン）でロード。LR_SHARED なので解放不要。
        IntPtr hIcon = LoadImageW(hModule, iconResId, IMAGE_ICON, 0, 0, LR_DEFAULTSIZE | LR_SHARED);
        if (hIcon == IntPtr.Zero)
        {
            return;
        }

        var iconId = Microsoft.UI.Win32Interop.GetIconIdFromIcon(hIcon);
        AppWindow.SetIcon(iconId);
    }

    private static readonly IntPtr RT_GROUP_ICON = (IntPtr)14;
    private const uint IMAGE_ICON = 1;
    private const uint LR_DEFAULTSIZE = 0x00000040;
    private const uint LR_SHARED = 0x00008000;

    private delegate bool EnumResNameProc(IntPtr hModule, IntPtr lpszType, IntPtr lpszName, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumResourceNamesW(IntPtr hModule, IntPtr lpszType, EnumResNameProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImageW(IntPtr hInst, IntPtr name, uint type, int cx, int cy, uint fuLoad);

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (RootFrame.Content is MainPage page)
        {
            // セッション（フォルダ/選択/表示モード/フィルタ）を控えてから、左ペイン状態と一緒に保存。
            page.ViewModel.CaptureSession();
            page.SaveLeftPaneLayout(); // 末尾で Settings.Save() を1回呼ぶ
        }

        // Alt キー ビープ抑止のサブクラスを解除する（デリゲート参照も解放）。
        if (_menuBeepSubclassProc != null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            _ = RemoveWindowSubclass(hwnd, _menuBeepSubclassProc, MenuBeepSubclassId);
            _menuBeepSubclassProc = null;
        }
    }
}
