using PhotoQuickSelector.Core;
using Xunit;

namespace PhotoQuickSelector.Core.Tests;

public class ClipboardExportTests
{
    [Fact]
    public void BuildFileNameList_OneNamePerLine_WithTrailingNewline()
    {
        var text = ClipboardExport.BuildFileNameList(new[] { "DSC001.JPG", "DSC002.JPG" });
        Assert.Equal("DSC001.JPG\r\nDSC002.JPG\r\n", text);
    }

    [Fact]
    public void BuildMoveBatch_EmitsRemHeaderAndMoveLines()
    {
        var filter = new PhotoFilter { Enabled = true, RatingValue = 3 };
        var text = ClipboardExport.BuildMoveBatch(
            @"D:\Photos\20260228", 2, 71,
            filter.DescribeConditions(),
            new[] { "DSC001.JPG", "DSC002.ARW" });

        var lines = text.Split("\r\n");
        Assert.Equal(@"@rem Folder: D:\Photos\20260228", lines[0]);
        Assert.Equal("@rem Count: 2/71", lines[1]);
        Assert.Equal("@rem Rating: ≧3", lines[2]);
        Assert.Equal("set FROMDIR=..", lines[3]);
        Assert.Equal("set TODIR=.", lines[4]);
        Assert.Equal(@"move %FROMDIR%\DSC001* %TODIR%", lines[5]);
        Assert.Equal(@"move %FROMDIR%\DSC002* %TODIR%", lines[6]);
        Assert.Equal("", lines[7]);
    }

    [Fact]
    public void BuildMoveBatch_StripsExtension_ForRawJpegPairing()
    {
        var text = ClipboardExport.BuildMoveBatch(
            "f", 1, 1, new List<string>(), new[] { "P2230001.JPG" });
        Assert.Contains(@"move %FROMDIR%\P2230001* %TODIR%", text);
    }
}
