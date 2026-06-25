using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// アプリ設定の編集ダイアログ（モーダル）。現状は共有（Alt+S）の起動先 exe パス
/// （<see cref="AppSettings.SharePath"/>）を編集する。保存は呼び出し側（<see cref="PhotoStatusBar"/>）が行う。
/// </summary>
public sealed partial class SettingsDialog : ContentDialog
{
    public SettingsDialog()
    {
        InitializeComponent();
        // ContentDialog は既定で幅を ContentDialogMaxWidth（≈548）にクランプするため広げる。
        Resources["ContentDialogMaxWidth"] = 620.0;
    }

    /// <summary>編集中の共有先 exe パス（前後空白は除去）。</summary>
    public string SharePath => SharePathBox.Text.Trim();

    /// <summary>表示前に現在の設定値を流し込む。</summary>
    public void Configure(AppSettings settings)
        => SharePathBox.Text = settings.SharePath ?? "";

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        // 共有先は実行ファイルなので WinRT の FileOpenPicker（.exe フィルタ）で選ぶ。
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        picker.FileTypeFilter.Add(".exe");

        var file = await picker.PickSingleFileAsync();
        if (file != null) SharePathBox.Text = file.Path;
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => SharePathBox.Text = "";
}
