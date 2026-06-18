using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// カラーラベルの色ドット列（Red/Yellow/Green/Blue/Purple）。グリッド・フィルムストリップ・
/// メタ情報パネル（案A/案B）で共用する。DataContext に <c>PhotoItemViewModel</c> を継承させ、
/// 各色の表示可否は <c>RedVisibility</c> 等に束縛する。ドット径・間隔は本コントロールのプロパティで指定。
/// </summary>
public sealed partial class ColorLabelDots : UserControl
{
    public ColorLabelDots() => InitializeComponent();

    /// <summary>各色ドットの直径（px）。</summary>
    public double DotSize
    {
        get => (double)GetValue(DotSizeProperty);
        set => SetValue(DotSizeProperty, value);
    }

    public static readonly DependencyProperty DotSizeProperty =
        DependencyProperty.Register(nameof(DotSize), typeof(double), typeof(ColorLabelDots),
            new PropertyMetadata(10.0));

    /// <summary>ドット間の間隔（px）。</summary>
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register(nameof(Spacing), typeof(double), typeof(ColorLabelDots),
            new PropertyMetadata(3.0));
}
