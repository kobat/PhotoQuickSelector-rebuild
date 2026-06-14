using PhotoQuickSelector.Core;
using Xunit;

namespace PhotoQuickSelector.Core.Tests;

public class MetadataCalcTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    [InlineData(8, true)]
    public void IsRotated_TrueForOrientationFiveOrAbove(int orientation, bool expected)
        => Assert.Equal(expected, MetadataCalc.IsRotated(orientation));

    [Fact]
    public void DisplayDimensions_SwapWhenRotated()
    {
        // 横 6000x4000、Orientation=6（90度回転）→ 表示は 4000x6000
        Assert.Equal(4000, MetadataCalc.DisplayWidth(6, 6000, 4000));
        Assert.Equal(6000, MetadataCalc.DisplayHeight(6, 6000, 4000));
    }

    [Fact]
    public void DisplayDimensions_KeepWhenNotRotated()
    {
        Assert.Equal(6000, MetadataCalc.DisplayWidth(1, 6000, 4000));
        Assert.Equal(4000, MetadataCalc.DisplayHeight(1, 6000, 4000));
    }

    [Fact]
    public void Compute35mm_UsesExifValueWhenPresent()
        => Assert.Equal(50, MetadataCalc.Compute35mmEquivalent(35, 50, 0));

    [Fact]
    public void Compute35mm_MicroFourThirds_DoublesFocalLength()
    {
        // 対角 21.6mm（マイクロフォーサーズ）は 2 倍
        Assert.Equal(50, MetadataCalc.Compute35mmEquivalent(25, 0, 21.6));
    }

    [Fact]
    public void Compute35mm_OtherSensor_UsesDiagonalRatio()
    {
        // 25mm, 対角 43.27mm（フルサイズ相当）→ ほぼ等倍
        var result = MetadataCalc.Compute35mmEquivalent(25, 0, 43.27);
        Assert.Equal(25, result);
    }

    [Fact]
    public void Compute35mm_ReturnsZero_WhenNoInfo()
        => Assert.Equal(0, MetadataCalc.Compute35mmEquivalent(0, 0, 0));

    [Theory]
    [InlineData(0, 0, "")]
    [InlineData(35, 0, "35mm")]
    [InlineData(35, 52.5, "35mm (52.5mm)")]
    public void FocalLengthDescription(double fl, double fl35, string expected)
        => Assert.Equal(expected, MetadataCalc.FocalLengthDescription(fl, fl35));

    [Theory]
    [InlineData(0, "")]
    [InlineData(2.8, "F2.8")]
    public void ApertureDescription(double aperture, string expected)
        => Assert.Equal(expected, MetadataCalc.ApertureDescription(aperture));

    [Theory]
    [InlineData(0, "")]
    [InlineData(100, "ISO100")]
    public void IsoDescription(int iso, string expected)
        => Assert.Equal(expected, MetadataCalc.IsoDescription(iso));

    [Theory]
    [InlineData(0, "±0EV")]
    [InlineData(0.7, "+0.7EV")]
    [InlineData(-1.0, "-1.0EV")]
    public void ExposureBiasDescription(double bias, string expected)
        => Assert.Equal(expected, MetadataCalc.ExposureBiasDescription(bias));
}
