using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// アプリ設定の編集ダイアログ（モーダル）。一般タブ（表示言語・ズーム倍率・共有先）と
/// 高度な設定タブ（先読みキャッシュのパラメータ）に分かれる。各設定グループには「既定に戻す」を持つ。
/// 保存（値の <see cref="AppSettings"/> への反映＋<c>Save()</c>）は呼び出し側（<see cref="PhotoStatusBar"/>）が行う。
/// </summary>
public sealed partial class SettingsDialog : ContentDialog
{
    /// <summary>「既定に戻す」の参照元（各プロパティの初期化子＝既定値を持つ新規インスタンス）。</summary>
    private static readonly AppSettings Defaults = new();

    public SettingsDialog()
    {
        InitializeComponent();
        // ContentDialog は既定で幅を ContentDialogMaxWidth（≈548）にクランプするため広げる。
        Resources["ContentDialogMaxWidth"] = 700.0;
    }

    // --- 編集中の値（保存時に呼び出し側が読み取る） ---

    /// <summary>編集中の共有先 exe パス（前後空白は除去）。</summary>
    public string SharePath => SharePathBox.Text.Trim();

    /// <summary>
    /// 編集中の表示言語（""=自動 / "ja" / "en"。<see cref="AppSettings.Language"/> と同じ表現）。
    /// 名前が Language だと <see cref="FrameworkElement.Language"/>（xml:lang）を隠すため別名。
    /// </summary>
    public string SelectedLanguage => (LanguageCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    /// <summary>編集中のズーム段（倍率。パーセント入力を 1/100 して昇順・重複除去。空なら既定）。</summary>
    public List<double> ZoomStops
    {
        get
        {
            var stops = ParseZoomStops(ZoomStopsBox.Text);
            return stops.Count > 0 ? stops : new List<double>(AppSettings.DefaultZoomStops);
        }
    }

    public double CacheBudgetGB => Val(CacheBudgetBox, Defaults.CacheBudgetGB);
    public int PrefetchForward => (int)System.Math.Round(Val(PrefetchForwardBox, Defaults.PrefetchForward));
    public int PrefetchBackward => (int)System.Math.Round(Val(PrefetchBackwardBox, Defaults.PrefetchBackward));
    public int MaxConcurrentDecodes => (int)System.Math.Round(Val(ConcurrencyBox, Defaults.MaxConcurrentDecodes));
    public int RateBudget => (int)System.Math.Round(Val(RateBudgetBox, Defaults.RateBudget));
    public int RateWindowMs => (int)System.Math.Round(Val(RateWindowBox, Defaults.RateWindowMs));

    /// <summary>NumberBox の値を取り出す。空欄（NaN）のときは既定値へフォールバックする。</summary>
    private static double Val(NumberBox box, double fallback) =>
        double.IsNaN(box.Value) ? fallback : box.Value;

    /// <summary>表示前に現在の設定値を流し込む。</summary>
    public void Configure(AppSettings settings)
    {
        SharePathBox.Text = settings.SharePath ?? "";
        var lang = settings.Language ?? "";
        LanguageCombo.SelectedIndex = lang switch { "ja" => 1, "en" => 2, _ => 0 };

        ZoomStopsBox.Text = FormatZoomStops(settings.ZoomStops);
        CacheBudgetBox.Value = settings.CacheBudgetGB;
        PrefetchForwardBox.Value = settings.PrefetchForward;
        PrefetchBackwardBox.Value = settings.PrefetchBackward;
        ConcurrencyBox.Value = settings.MaxConcurrentDecodes;
        RateBudgetBox.Value = settings.RateBudget;
        RateWindowBox.Value = settings.RateWindowMs;
    }

    // --- ズーム段の整形／解析（表示はパーセント、内部は倍率） ---

    /// <summary>倍率リスト → 「25, 50, 100」のパーセント文字列（整数優先・小数は最大2桁）。</summary>
    private static string FormatZoomStops(IEnumerable<double>? stops)
    {
        var src = stops ?? AppSettings.DefaultZoomStops;
        return string.Join(", ", src
            .Where(v => v > 0)
            .Select(v => (v * 100).ToString("0.##", CultureInfo.InvariantCulture)));
    }

    /// <summary>パーセント文字列（カンマ/空白区切り）→ 倍率リスト。正の値のみ・昇順・重複除去。</summary>
    private static List<double> ParseZoomStops(string text)
    {
        var result = new List<double>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        foreach (var token in text.Split(new[] { ',', ' ', '\t', '\r', '\n', '、', '％', '%' },
                                          System.StringSplitOptions.RemoveEmptyEntries))
        {
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent)
                && percent > 0)
            {
                result.Add(percent / 100.0);
            }
        }
        return result.Distinct().OrderBy(v => v).ToList();
    }

    // --- 参照／クリア ---

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

    // --- 「既定に戻す」（設定グループごと） ---

    private void ResetLanguage_Click(object sender, RoutedEventArgs e) => LanguageCombo.SelectedIndex = 0;

    private void ResetShare_Click(object sender, RoutedEventArgs e) => SharePathBox.Text = "";

    private void ResetZoom_Click(object sender, RoutedEventArgs e) =>
        ZoomStopsBox.Text = FormatZoomStops(AppSettings.DefaultZoomStops);

    private void ResetCache_Click(object sender, RoutedEventArgs e) =>
        CacheBudgetBox.Value = Defaults.CacheBudgetGB;

    private void ResetPrefetch_Click(object sender, RoutedEventArgs e)
    {
        PrefetchForwardBox.Value = Defaults.PrefetchForward;
        PrefetchBackwardBox.Value = Defaults.PrefetchBackward;
    }

    private void ResetConcurrency_Click(object sender, RoutedEventArgs e) =>
        ConcurrencyBox.Value = Defaults.MaxConcurrentDecodes;

    private void ResetRate_Click(object sender, RoutedEventArgs e)
    {
        RateBudgetBox.Value = Defaults.RateBudget;
        RateWindowBox.Value = Defaults.RateWindowMs;
    }
}
