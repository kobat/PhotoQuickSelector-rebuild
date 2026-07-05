using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoQuickSelector_App.ViewModels;
using Windows.System;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// 右ペイン上部のステータスバー（フォルダパス等のステータス＋選択写真のメタ情報パネル案A＋GPS＋読み込み中表示）。
/// 件数はフィルタボタン（<see cref="FilterBar"/>）の FilteredCountText に集約し、ここには表示しない。
/// 共有 <see cref="MainViewModel"/> を <see cref="MainPage"/> が注入する。
/// 左ペイン開閉ボタンは骨組み側（<see cref="MainPage"/> の LeftColumn）を操作するため、
/// <see cref="ToggleLeftPaneRequested"/> イベントで委譲する。
/// </summary>
public sealed partial class PhotoStatusBar : UserControl
{
    private MainViewModel? _viewModel;

    public PhotoStatusBar() => InitializeComponent();

    /// <summary>表示対象のビューモデル。<see cref="MainPage"/> が生成後に注入する。</summary>
    public MainViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (ReferenceEquals(_viewModel, value)) return;
            _viewModel = value;
            FilterControl.ViewModel = value; // 内包するフィルタボタンへも注入
            Bindings.Update();
        }
    }

    /// <summary>左ペインの表示/非表示ボタンが押されたとき発火する（開閉は <see cref="MainPage"/> が担う）。</summary>
    public event EventHandler? ToggleLeftPaneRequested;

    /// <summary>全画面表示ボタンが押されたとき発火する（切替は AppWindow を持つ <see cref="MainWindow"/> が担う）。</summary>
    public event EventHandler? ToggleFullScreenRequested;

    /// <summary>完全全画面モードの切替要求（<see cref="MainPage.ToggleFullImageMode"/> を呼ぶ）。</summary>
    public event EventHandler? ToggleFullImageRequested;

    /// <summary>イマーシブ表示の切替要求（<see cref="MainPage"/> が <see cref="PreviewControl.SetImmersive"/> を呼ぶ）。</summary>
    public event EventHandler? ToggleImmersiveRequested;

    /// <summary>設定ダイアログが保存されたとき発火する（<see cref="MainPage"/> が <see cref="PreviewControl.ApplyPreviewSettings"/> を呼ぶ）。</summary>
    public event EventHandler? SettingsChanged;

    /// <summary>イマーシブ表示中かを返す（メニューのチェック表示用。<see cref="MainPage"/> が Preview から供給）。</summary>
    public Func<bool>? IsImmersiveProvider { get; set; }

    /// <summary>全画面表示中かを返す（メニューのチェック表示用。<see cref="MainPage"/> が <see cref="MainWindow"/> から供給）。</summary>
    public Func<bool>? IsFullScreenProvider { get; set; }

    private void ToggleButton_Click(object sender, RoutedEventArgs e)
        => ToggleLeftPaneRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// 左ペイン開閉ボタンのグリフ／ツールチップを実際の開閉状態へ追従させる。開閉のきっかけ
    /// （ボタン／スプリッタードラッグ／完全全画面／復元）に依らず、<see cref="MainPage"/> が左ペインの
    /// 幅変化を観測して呼ぶ（＝唯一の更新起点）。開いている時は「隠す」、閉じている時は「表示」を示す。
    /// </summary>
    public void UpdateLeftPaneGlyph(bool open)
    {
        LeftPaneIcon.Glyph = open ? "" : ""; // open=OpenPane(E8A0, left arrow) / closed=ClosePane(E89F, right arrow)
        ToolTipService.SetToolTip(LeftPaneButton,
            Loc.Get(open ? "StatusBar_HideLeftPane" : "StatusBar_ShowLeftPane"));
    }

    private void FullScreenButton_Click(object sender, RoutedEventArgs e)
        => ToggleFullScreenRequested?.Invoke(this, EventArgs.Empty);

    // === ハンバーガーメニュー（クリックで実行。各実体はキー処理と共通） ===

    /// <summary>メニューを開く直前に、トグル項目のチェック状態と有効/無効を現在の VM 状態へ同期する。</summary>
    private void Menu_Opening(object sender, object e)
    {
        if (_viewModel is null) return;

        FilterToggleItem.IsChecked = _viewModel.Filter.Enabled;
        FullScreenToggleItem.IsChecked = IsFullScreenProvider?.Invoke() ?? false;
        ImmersiveToggleItem.IsChecked = IsImmersiveProvider?.Invoke() ?? false;
        InfoToggleItem.IsChecked = _viewModel.ShowInfoOverlay;

        // プレビュー専用群はプレビュー時のみ、ファイル連携は写真選択時のみ有効。
        PreviewSubItem.IsEnabled = _viewModel.IsPreviewMode;
        FileSubItem.IsEnabled = _viewModel.FocusedPhoto is not null;
    }

    private void MenuFilter_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null) _viewModel.Filter.Enabled = FilterToggleItem.IsChecked;
    }

    private void MenuFullScreen_Click(object sender, RoutedEventArgs e)
        => ToggleFullScreenRequested?.Invoke(this, EventArgs.Empty);

    private void MenuFullImage_Click(object sender, RoutedEventArgs e)
        => ToggleFullImageRequested?.Invoke(this, EventArgs.Empty);

    private void MenuImmersive_Click(object sender, RoutedEventArgs e)
        => ToggleImmersiveRequested?.Invoke(this, EventArgs.Empty);

    private void MenuInfo_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null) _viewModel.ShowInfoOverlay = InfoToggleItem.IsChecked;
    }

    private void MenuGridKind_Click(object sender, RoutedEventArgs e)
        => _viewModel?.CycleGridKind();

    private void MenuGridRef_Click(object sender, RoutedEventArgs e)
        => _viewModel?.ToggleGridReference();

    private void MenuExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.FocusedPhoto is { } item) PhotoFileCommands.OpenInExplorer(item);
    }

    private void MenuOpenDefault_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.FocusedPhoto is { } item) PhotoFileCommands.OpenWithDefault(item);
    }

    private void MenuCopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.FocusedPhoto is { } item) PhotoFileCommands.CopyPath(item);
    }

    private void MenuShare_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.FocusedPhoto is { } item) PhotoFileCommands.Share(item, _viewModel.Settings);
    }

    /// <summary>GPS 地図ボタン。撮影位置をブラウザの地図で開く（十進緯度経度がある場合）。</summary>
    private async void GpsButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PhotoItemViewModel { MapUri: { } uri })
            await Launcher.LaunchUriAsync(uri);
    }

    /// <summary>メニュー「設定…」。設定ダイアログを開き、保存されたら <see cref="AppSettings"/> へ反映する。</summary>
    private async void MenuSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;

        var dialog = new SettingsDialog { XamlRoot = XamlRoot };
        dialog.Configure(_viewModel.Settings);

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var s = _viewModel.Settings;
            s.SharePath = dialog.SharePath;
            s.Language = dialog.SelectedLanguage;
            s.ZoomStops = dialog.ZoomStops;
            s.CacheBudgetGB = dialog.CacheBudgetGB;
            s.PrefetchForward = dialog.PrefetchForward;
            s.PrefetchBackward = dialog.PrefetchBackward;
            s.MaxConcurrentDecodes = dialog.MaxConcurrentDecodes;
            s.RateBudget = dialog.RateBudget;
            s.RateWindowMs = dialog.RateWindowMs;
            s.Save();
            // ズーム段・先読み・レート・キャッシュ予算はプレビューへ即時反映（同時デコード数のみ再起動後）。
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>メニュー「ショートカット一覧…」。チートシートをモーダルで表示する（F1 と同経路）。</summary>
    private async void MenuShortcuts_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ShortcutsDialog { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
    }

    /// <summary>メニュー「バージョン情報…」。About を表示し、「ライセンス情報」が押されたら
    /// About を閉じてから License を開く（ContentDialog の入れ子表示はクラッシュ要因のため連鎖させる）。</summary>
    private async void MenuAbout_Click(object sender, RoutedEventArgs e)
    {
        var about = new AboutDialog { XamlRoot = XamlRoot };
        if (await about.ShowAsync() == ContentDialogResult.Primary)
        {
            var license = new LicenseDialog { XamlRoot = XamlRoot };
            await license.ShowAsync();
        }
    }
}
