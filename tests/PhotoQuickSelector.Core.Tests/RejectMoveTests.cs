using PhotoQuickSelector.Core;
using Xunit;

namespace PhotoQuickSelector.Core.Tests;

public class RejectMoveTests
{
    [Fact]
    public void IsRejectTarget_NoFlagNoRating_IsTarget()
    {
        var eval = new PhotoEvaluation();
        Assert.True(RejectMove.IsRejectTarget(eval));
    }

    [Fact]
    public void IsRejectTarget_RejectFlagUnrated_IsTarget()
    {
        var eval = new PhotoEvaluation { PersistedFlagRating = -1 };
        Assert.True(RejectMove.IsRejectTarget(eval));
    }

    [Fact]
    public void IsRejectTarget_PickFlag_IsNotTarget()
    {
        var eval = new PhotoEvaluation { PersistedFlagRating = 1 };
        Assert.False(RejectMove.IsRejectTarget(eval));
    }

    [Fact]
    public void IsRejectTarget_Rated_IsNotTarget()
    {
        var eval = new PhotoEvaluation { PersistedRating = 3 };
        Assert.False(RejectMove.IsRejectTarget(eval));

        // EXIF レーティングのみでも対象外。
        var exif = new PhotoEvaluation { ExifRating = 2 };
        Assert.False(RejectMove.IsRejectTarget(exif));
    }

    [Fact]
    public void BuildBatch_EmitsHeaderAndMoveLines()
    {
        var text = RejectMove.BuildBatch(
            @"D:\Photos\20260228", "2026-06-21 12:34:56", 2, 71,
            new[] { "DSC001.JPG", "DSC002.JPG" });

        var lines = text.Split("\r\n");
        Assert.Equal("@echo off", lines[0]);
        Assert.Equal("chcp 65001 > nul", lines[1]);
        Assert.Equal("@rem Reject move generated 2026-06-21 12:34:56", lines[2]);
        Assert.Equal(@"@rem Folder: D:\Photos\20260228", lines[3]);
        Assert.Equal("@rem Count: 2/71 (no pick flag, no rating)", lines[4]);
        Assert.Equal("set FROMDIR=..", lines[5]);
        Assert.Equal("set TODIR=.", lines[6]);
        Assert.Equal(@"move %FROMDIR%\DSC001* %TODIR%", lines[7]);
        Assert.Equal(@"move %FROMDIR%\DSC002* %TODIR%", lines[8]);
        Assert.Equal("", lines[9]);
    }

    [Fact]
    public void BuildBatch_StripsExtension_ForRawJpegPairing()
    {
        var text = RejectMove.BuildBatch(
            "f", "t", 1, 1, new[] { "P2230001.JPG" });
        Assert.Contains(@"move %FROMDIR%\P2230001* %TODIR%", text);
    }
}
