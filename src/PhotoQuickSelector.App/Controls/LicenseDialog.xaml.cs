using System.IO;
using System.Reflection;
using Microsoft.UI.Xaml.Controls;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// ライセンス情報ダイアログ（モーダル）。本アプリ（MIT）と同梱第三者ライブラリの許諾全文を表示する。
/// 全文はアセンブリへ埋め込んだ <c>LICENSE</c> / <c>THIRD-PARTY-NOTICES.txt</c>（csproj の EmbeddedResource、
/// LogicalName 指定）から読み出す。single-file 発行でも exe 隣のファイル有無に依存しない。
/// </summary>
public sealed partial class LicenseDialog : ContentDialog
{
    public LicenseDialog()
    {
        InitializeComponent();
        // ContentDialog は既定で幅を ContentDialogMaxWidth（≈548）にクランプするため広げる。
        Resources["ContentDialogMaxWidth"] = 780.0;

        AppLicenseText.Text = ReadEmbedded("LICENSE");
        ThirdPartyText.Text = ReadEmbedded("THIRD-PARTY-NOTICES.txt");
    }

    /// <summary>埋め込みリソース（LogicalName 指定）をテキストとして読む。失敗時は空欄にしない代替文言。</summary>
    private static string ReadEmbedded(string resourceName)
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream is null) return $"（{resourceName} を読み込めませんでした）";

        using (stream)
        using (var reader = new StreamReader(stream))
            return reader.ReadToEnd();
    }
}
