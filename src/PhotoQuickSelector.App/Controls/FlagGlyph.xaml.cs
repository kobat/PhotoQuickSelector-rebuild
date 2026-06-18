using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// 評価フラグのグリフ（採用=旗 / 拒否=×）。グリッド・フィルムストリップ・メタ情報パネル（案A/案B）で
/// 共用する。DataContext に <c>PhotoItemViewModel</c> を継承させ、採用/拒否の出し分けは
/// <c>PickVisibility</c>/<c>RejectVisibility</c> に束縛する。フォントサイズは <see cref="GlyphSize"/> で指定。
/// </summary>
public sealed partial class FlagGlyph : UserControl
{
    public FlagGlyph() => InitializeComponent();

    /// <summary>旗/× グリフのフォントサイズ（px）。</summary>
    public double GlyphSize
    {
        get => (double)GetValue(GlyphSizeProperty);
        set => SetValue(GlyphSizeProperty, value);
    }

    public static readonly DependencyProperty GlyphSizeProperty =
        DependencyProperty.Register(nameof(GlyphSize), typeof(double), typeof(FlagGlyph),
            new PropertyMetadata(12.0));
}
