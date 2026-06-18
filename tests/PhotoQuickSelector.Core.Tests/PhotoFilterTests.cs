using PhotoQuickSelector.Core;
using Xunit;

namespace PhotoQuickSelector.Core.Tests;

public class PhotoFilterTests
{
    private static PhotoEvaluation Eval(int rating = 0, int flag = 0, params ColorLabel[] colors)
    {
        var e = new PhotoEvaluation();
        if (rating > 0) e.SetRating(rating);
        e.PersistedFlagRating = flag;
        foreach (var c in colors) e.ToggleColorLabel(c);
        return e;
    }

    [Fact]
    public void Disabled_PassesEverything()
    {
        var f = new PhotoFilter { Enabled = false, RatingValue = 5 };
        Assert.True(f.Matches(Eval(rating: 1)));
    }

    [Fact]
    public void EnabledWithNoActiveCondition_PassesEverything()
    {
        var f = new PhotoFilter { Enabled = true };
        Assert.True(f.Matches(Eval()));
        Assert.True(f.Matches(Eval(rating: 3)));
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(1, false)]
    public void Rating_GreaterEqual(int rating, bool expected)
    {
        var f = new PhotoFilter { Enabled = true, RatingValue = 2, RatingCompareMode = RatingCompareMode.GreaterEqual };
        Assert.Equal(expected, f.Matches(Eval(rating: rating)));
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(2, false)]
    [InlineData(4, false)]
    public void Rating_Equal(int rating, bool expected)
    {
        var f = new PhotoFilter { Enabled = true, RatingValue = 3, RatingCompareMode = RatingCompareMode.Equal };
        Assert.Equal(expected, f.Matches(Eval(rating: rating)));
    }

    [Fact]
    public void Rating_NoRating_IncludesZeroOnly()
    {
        var f = new PhotoFilter { Enabled = true, NoRating = true };
        Assert.True(f.Matches(Eval(rating: 0)));
        Assert.False(f.Matches(Eval(rating: 1)));
    }

    [Fact]
    public void Rating_ThresholdPlusNoRating_IncludesBoth()
    {
        var f = new PhotoFilter { Enabled = true, RatingValue = 4, NoRating = true };
        Assert.True(f.Matches(Eval(rating: 0)));  // 未評価
        Assert.True(f.Matches(Eval(rating: 5)));  // ≧4
        Assert.False(f.Matches(Eval(rating: 2))); // 中途半端
    }

    [Theory]
    [InlineData(1, true)]   // 採用
    [InlineData(0, false)]  // 中立
    [InlineData(-1, false)] // 拒否
    public void Flag_AcceptOnly(int flag, bool expected)
    {
        var f = new PhotoFilter { Enabled = true, FlagAccept = true };
        Assert.Equal(expected, f.Matches(Eval(flag: flag)));
    }

    [Fact]
    public void Flag_AcceptOrReject()
    {
        var f = new PhotoFilter { Enabled = true, FlagAccept = true, FlagReject = true };
        Assert.True(f.Matches(Eval(flag: 1)));
        Assert.True(f.Matches(Eval(flag: -1)));
        Assert.False(f.Matches(Eval(flag: 0)));
    }

    [Fact]
    public void Color_RequiresAllSelectedColors()
    {
        var f = new PhotoFilter { Enabled = true };
        f.SetColor(ColorLabel.Red, true);
        f.SetColor(ColorLabel.Blue, true);

        Assert.True(f.Matches(Eval(colors: new[] { ColorLabel.Red, ColorLabel.Blue })));
        Assert.False(f.Matches(Eval(colors: new[] { ColorLabel.Red })));     // Blue 欠け
        Assert.False(f.Matches(Eval()));                                     // どちらも無し
    }

    [Fact]
    public void Conditions_AreAnded()
    {
        var f = new PhotoFilter { Enabled = true, RatingValue = 3, FlagAccept = true };
        f.SetColor(ColorLabel.Green, true);

        Assert.True(f.Matches(Eval(rating: 5, flag: 1, colors: new[] { ColorLabel.Green })));
        Assert.False(f.Matches(Eval(rating: 5, flag: 1)));            // 色欠け
        Assert.False(f.Matches(Eval(rating: 2, flag: 1, colors: new[] { ColorLabel.Green }))); // 評価不足
    }

    [Fact]
    public void DescribeConditions_FormatsActiveConditions()
    {
        var f = new PhotoFilter
        {
            Enabled = true,
            RatingValue = 3,
            RatingCompareMode = RatingCompareMode.GreaterEqual,
            NoRating = true,
            FlagAccept = true,
            FlagReject = true,
        };
        f.SetColor(ColorLabel.Red, true);

        var lines = f.DescribeConditions();
        Assert.Equal("Rating: ≧3, NoRating", lines[0]);
        Assert.Equal("Flag: Accept, Reject", lines[1]);
        Assert.Equal("ColorLabel: Red", lines[2]);
    }

    [Fact]
    public void DescribeConditions_EmptyWhenDisabled()
    {
        var f = new PhotoFilter { Enabled = false, RatingValue = 5 };
        Assert.Empty(f.DescribeConditions());
    }
}
