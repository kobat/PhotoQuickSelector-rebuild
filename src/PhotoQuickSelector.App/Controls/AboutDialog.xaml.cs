using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// バージョン情報ダイアログ（モーダル）。アプリ名・バージョン・著作権に加え、
/// 設定ファイル（settings.json）の場所と評価データ（フォルダごとの sqlite）のファイル名を表示する。
/// バージョンは csproj の &lt;Version&gt; を単一情報源としてアセンブリから取得する（ハードコードしない）。
/// 「ライセンス情報」ボタン（Primary）の結果は呼び出し側（<see cref="PhotoStatusBar"/>）が受け取り、
/// 本ダイアログを閉じてから <see cref="LicenseDialog"/> を開く（ContentDialog の入れ子表示はクラッシュ要因のため）。
/// </summary>
public sealed partial class AboutDialog : ContentDialog
{
    public AboutDialog()
    {
        InitializeComponent();
        // ContentDialog は既定で幅を ContentDialogMaxWidth（≈548）にクランプするため広げる
        // （設定ファイルのパスが細かく折り返さないように）。
        Resources["ContentDialogMaxWidth"] = 700.0;

        VersionText.Text = Loc.Get("About_VersionFormat", GetVersionText());
        SettingsPathText.Text = AppSettings.SettingsFileDisplayPath;
        DatabaseNameText.Text = Loc.Get("About_DatabaseFileFormat", AppSettings.DatabaseFileName);
    }

    /// <summary>
    /// アセンブリのバージョンを "x.y.z"（開発中は "x.y.z-dev"）形式で返す（取得できなければ既定値）。
    /// csproj の &lt;Version&gt; がそのまま載るのは AssemblyInformationalVersion だけなので優先して読む
    /// （AssemblyVersion は数値4組で、プレリリース表記が落ちる）。ビルド構成によっては
    /// "+&lt;git sha&gt;" が付くため '+' 以降は捨てる。
    /// </summary>
    private static string GetVersionText()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus < 0 ? informational : informational[..plus];
        }

        var version = assembly.GetName().Version;
        return version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    /// <summary>
    /// 設定ファイルをエクスプローラで選択表示する。一度も保存していないとファイルは無いので、
    /// その場合は実在する直近の親フォルダを開く（表示のためにフォルダを作りはしない）。
    /// </summary>
    private void ShowSettingsFile_Click(object sender, RoutedEventArgs e)
    {
        var path = AppSettings.SettingsFileDisplayPath;
        if (File.Exists(path))
        {
            PhotoFileCommands.ShowInExplorer(path);
            return;
        }

        try
        {
            var dir = Path.GetDirectoryName(path);
            while (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) dir = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(dir)) return;

            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch { /* 権限・シェル失敗は黙殺（クラッシュさせない） */ }
    }

    /// <summary>設定ファイルのパスをクリップボードへコピーする。</summary>
    private void CopySettingsPath_Click(object sender, RoutedEventArgs e) =>
        PhotoFileCommands.SetClipboardText(new[] { AppSettings.SettingsFileDisplayPath });
}
