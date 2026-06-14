using PhotoQuickSelector.Core;
using Xunit;

namespace PhotoQuickSelector.Core.Tests;

public class PhotoEvaluationTests
{
    [Fact]
    public void Rating_FallsBackToExif_WhenNotPersisted()
    {
        var e = new PhotoEvaluation { ExifRating = 3 };
        Assert.Equal(3, e.Rating);
    }

    [Fact]
    public void Rating_PrefersPersistedOverExif()
    {
        var e = new PhotoEvaluation { ExifRating = 3 };
        e.SetRating(5);
        Assert.Equal(5, e.Rating);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(4, 5)]
    [InlineData(5, 5)] // 上限でクランプ
    public void RatingUp_ClampsAtFive(int start, int expected)
    {
        var e = new PhotoEvaluation();
        e.SetRating(start);
        Assert.Equal(expected, e.RatingUp());
    }

    [Theory]
    [InlineData(5, 4)]
    [InlineData(1, 0)]
    [InlineData(0, 0)] // 下限でクランプ
    public void RatingDown_ClampsAtZero(int start, int expected)
    {
        var e = new PhotoEvaluation();
        e.SetRating(start);
        Assert.Equal(expected, e.RatingDown());
    }

    [Fact]
    public void RatingUp_FromExifFallback_PersistsExifPlusOne()
    {
        var e = new PhotoEvaluation { ExifRating = 2 };
        Assert.Equal(3, e.RatingUp());
        Assert.Equal(3, e.PersistedRating);
    }

    [Theory]
    [InlineData(-1, 0)]  // 拒否 → 中立
    [InlineData(0, 1)]   // 中立 → 採用
    [InlineData(1, 1)]   // 採用は維持
    public void FlagUp_StepsTowardAccept(int start, int expected)
    {
        var e = new PhotoEvaluation { PersistedFlagRating = start };
        Assert.Equal(expected, e.FlagUp());
    }

    [Theory]
    [InlineData(1, 0)]   // 採用 → 中立
    [InlineData(0, -1)]  // 中立 → 拒否
    [InlineData(-1, -1)] // 拒否は維持
    public void FlagDown_StepsTowardReject(int start, int expected)
    {
        var e = new PhotoEvaluation { PersistedFlagRating = start };
        Assert.Equal(expected, e.FlagDown());
    }

    [Fact]
    public void FlagRating_DefaultsToNeutral()
    {
        var e = new PhotoEvaluation();
        Assert.Equal(0, e.FlagRating);
    }

    [Fact]
    public void ToggleColorLabel_TogglesOnAndOff()
    {
        var e = new PhotoEvaluation();
        Assert.False(e.HasColorLabel(ColorLabel.Purple));

        Assert.Equal(1, e.ToggleColorLabel(ColorLabel.Purple));
        Assert.True(e.HasColorLabel(ColorLabel.Purple));

        Assert.Equal(0, e.ToggleColorLabel(ColorLabel.Purple));
        Assert.False(e.HasColorLabel(ColorLabel.Purple));
    }

    [Fact]
    public void ColorLabels_AreIndependent()
    {
        var e = new PhotoEvaluation();
        e.ToggleColorLabel(ColorLabel.Red);
        Assert.True(e.HasColorLabel(ColorLabel.Red));
        Assert.False(e.HasColorLabel(ColorLabel.Blue));
    }
}
