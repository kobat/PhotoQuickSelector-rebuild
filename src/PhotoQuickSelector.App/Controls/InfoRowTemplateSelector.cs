using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// 画像情報パネルの行テンプレートを行の型で振り分ける。
/// 評価行だけ「更新日時」列を持ち、値が可変（OneWay）なのでテンプレートを分ける必要がある。
/// WinUI は WPF の暗黙 DataTemplate（DataType 指定）を持たないため、セレクタで明示する。
/// </summary>
public sealed partial class InfoRowTemplateSelector : DataTemplateSelector
{
    /// <summary>EXIF タグ行（タグ名＋値）のテンプレート。</summary>
    public DataTemplate? ExifTemplate { get; set; }

    /// <summary>評価行（項目名＋値＋更新日時）のテンプレート。</summary>
    public DataTemplate? EvaluationTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) =>
        item is EvaluationInfoRow ? EvaluationTemplate : ExifTemplate;

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) =>
        SelectTemplateCore(item);
}
