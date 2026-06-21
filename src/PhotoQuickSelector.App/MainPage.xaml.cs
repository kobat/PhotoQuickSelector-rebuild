using System.IO;
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
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        RestoreLeftPaneLayout();
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
            ViewModel.StatusText = $"前回のフォルダが見つかりません: {folder}";
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
        LeftColumn.Width = s.LeftPaneCollapsed
            ? new GridLength(0)
            : new GridLength(_lastLeftWidth);
    }

    /// <summary>左ペインの現在の幅／折りたたみ状態を設定へ書き戻して保存する（ウィンドウ終了時）。</summary>
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
        s.Save();
    }

    // --- 左ペインの折りたたみ（ステータスバーの ToggleLeftPaneRequested から呼ばれる） ---

    private void ToggleLeftPane()
    {
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

        if (ViewModel.SelectedPhoto is { } item && PhotoKeyCommands.TryHandleEvaluation(e.Key, item))
            e.Handled = true;
    }
}
