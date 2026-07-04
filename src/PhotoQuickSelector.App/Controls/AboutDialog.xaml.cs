using System.Reflection;
using Microsoft.UI.Xaml.Controls;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// バージョン情報ダイアログ（モーダル）。アプリ名・バージョン・著作権を表示する。
/// バージョンは csproj の &lt;Version&gt; を単一情報源としてアセンブリから取得する（ハードコードしない）。
/// 「ライセンス情報」ボタン（Primary）の結果は呼び出し側（<see cref="PhotoStatusBar"/>）が受け取り、
/// 本ダイアログを閉じてから <see cref="LicenseDialog"/> を開く（ContentDialog の入れ子表示はクラッシュ要因のため）。
/// </summary>
public sealed partial class AboutDialog : ContentDialog
{
    public AboutDialog()
    {
        InitializeComponent();
        VersionText.Text = Loc.Get("About_VersionFormat", GetVersionText());
    }

    /// <summary>アセンブリのバージョンを "x.y.z" 形式で返す（取得できなければ既定値）。</summary>
    private static string GetVersionText()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
