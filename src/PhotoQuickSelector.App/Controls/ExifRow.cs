using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// EXIF 詳細パネル（<see cref="PreviewControl"/> の右上、E キー）の 1 行。
/// ディレクトリ見出し行（<see cref="IsHeader"/>=true）とタグ行を平坦化した ListView の要素。
/// 平坦化＋<see cref="ExifRowTemplateSelector"/> で見出し/タグを描き分け、ListView の仮想化を活かす。
/// </summary>
public sealed class ExifRow
{
    public ExifRow(bool isHeader, string name, string value)
    {
        IsHeader = isHeader;
        Name = name;
        Value = value;
    }

    /// <summary>ディレクトリ見出し行（Exif SubIFD / GPS / Sony Makernote 等）なら true。</summary>
    public bool IsHeader { get; }

    /// <summary>見出し名（見出し行）またはタグ名（タグ行）。</summary>
    public string Name { get; }

    /// <summary>タグの説明文字列（タグ行のみ。見出し行は空）。</summary>
    public string Value { get; }
}

/// <summary>
/// <see cref="ExifRow.IsHeader"/> により見出し用/タグ用のテンプレートを選ぶセレクタ。
/// テンプレート本体は <see cref="PreviewControl"/> の XAML リソースで与える。
/// </summary>
public sealed partial class ExifRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate? HeaderTemplate { get; set; }
    public DataTemplate? TagTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item)
        => (item as ExifRow)?.IsHeader == true ? HeaderTemplate : TagTemplate;

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
        => SelectTemplateCore(item);
}
