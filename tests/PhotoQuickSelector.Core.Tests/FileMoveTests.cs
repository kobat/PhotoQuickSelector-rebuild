using PhotoQuickSelector.Core;
using Xunit;

namespace PhotoQuickSelector.Core.Tests;

public class FileMoveTests
{
    [Fact]
    public void BuildBatch_EmitsHeaderAndMoveLines()
    {
        var filter = new PhotoFilter { Enabled = true, RatingValue = 3 };
        var text = FileMove.BuildBatch(
            @"D:\Photos\20260228", @"E:\Selected\20260228", "2026-06-21 12:34:56", 2, 71,
            filter.DescribeConditions(),
            new[] { "DSC001.JPG", "DSC002.JPG" });

        var lines = text.Split("\r\n");
        Assert.Equal("chcp 65001 > nul", lines[0]);
        Assert.Equal("@rem File move generated 2026-06-21 12:34:56", lines[1]);
        Assert.Equal(@"@rem From: D:\Photos\20260228", lines[2]);
        Assert.Equal(@"@rem To: E:\Selected\20260228", lines[3]);
        Assert.Equal("@rem Count: 2/71", lines[4]);
        Assert.Equal("@rem Rating: ≧3", lines[5]);
        Assert.Equal(@"set FROMDIR=D:\Photos\20260228", lines[6]);
        Assert.Equal("set TODIR=.", lines[7]);
        Assert.Equal(@"move ""%FROMDIR%\DSC001*"" ""%TODIR%""", lines[8]);
        Assert.Equal(@"move ""%FROMDIR%\DSC002*"" ""%TODIR%""", lines[9]);
        Assert.Equal("", lines[10]);
    }

    [Fact]
    public void BuildBatch_NoFilterConditions_OmitsExtraRemLines()
    {
        var text = FileMove.BuildBatch(
            @"D:\Photos\20260228", @"E:\Selected", "2026-06-21 12:34:56", 1, 1,
            new List<string>(),
            new[] { "DSC001.JPG" });

        var lines = text.Split("\r\n");
        Assert.Equal("@rem Count: 1/1", lines[4]);
        Assert.Equal(@"set FROMDIR=D:\Photos\20260228", lines[5]);
    }

    [Fact]
    public void BuildBatch_StripsExtension_ForRawJpegPairing()
    {
        var text = FileMove.BuildBatch(
            "f", "t", "now", 1, 1, new List<string>(), new[] { "P2230001.JPG" });
        Assert.Contains(@"move ""%FROMDIR%\P2230001*"" ""%TODIR%""", text);
    }

    [Fact]
    public void BuildBatch_MultipleFiles_OneMoveLineEach()
    {
        var text = FileMove.BuildBatch(
            "f", "t", "now", 3, 3, new List<string>(),
            new[] { "A.JPG", "B.ARW", "C.JPG" });

        var moveLines = text.Split("\r\n").Where(l => l.StartsWith("move ")).ToArray();
        Assert.Equal(3, moveLines.Length);
        Assert.Equal(@"move ""%FROMDIR%\A*"" ""%TODIR%""", moveLines[0]);
        Assert.Equal(@"move ""%FROMDIR%\B*"" ""%TODIR%""", moveLines[1]);
        Assert.Equal(@"move ""%FROMDIR%\C*"" ""%TODIR%""", moveLines[2]);
    }

    [Fact]
    public void BuildBatch_EmptyFileList_HeaderOnlyPlusTrailingBlankLine()
    {
        var text = FileMove.BuildBatch(
            "f", "t", "now", 0, 5, new List<string>(), Array.Empty<string>());

        var lines = text.Split("\r\n");
        Assert.DoesNotContain(lines, l => l.StartsWith("move "));
        Assert.Equal("", lines[^1]);
    }
}
