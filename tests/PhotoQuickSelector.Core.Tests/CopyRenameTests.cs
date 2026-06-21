using PhotoQuickSelector.Core;
using Xunit;

namespace PhotoQuickSelector.Core.Tests;

public class CopyRenameTests
{
    private static readonly DateTimeOffset Taken =
        new(2026, 2, 28, 9, 5, 3, TimeSpan.FromHours(9));

    [Fact]
    public void ResolveName_ExpandsAllTokens()
    {
        var ctx = new CopyRename.RenameContext("DSC09432.JPG", Taken);
        var name = CopyRename.ResolveName(
            "{folder}_{YYYY}{MM}{DD}_{hh}{mm}{ss}_{name}_{seq:000}",
            "20260228", ctx, 7);

        // 年月日＝大文字 / 時分秒＝小文字。月(02)と分(05)は大小で区別される。
        Assert.Equal("20260228_20260228_090503_DSC09432_007", name);
    }

    [Fact]
    public void ResolveName_SeqWithoutPadding()
    {
        var ctx = new CopyRename.RenameContext("a.JPG", Taken);
        Assert.Equal("12", CopyRename.ResolveName("{seq}", "f", ctx, 12));
    }

    [Fact]
    public void ResolveName_NoTakenDate_DateTokensEmpty()
    {
        var ctx = new CopyRename.RenameContext("a.JPG", null);
        Assert.Equal("img_", CopyRename.ResolveName("img_{YYYY}{MM}", "f", ctx, 1));
    }

    [Fact]
    public void ResolveName_SanitizesInvalidChars()
    {
        var ctx = new CopyRename.RenameContext("a.JPG", Taken);
        // テンプレートに区切り文字等が混ざっても安全な名前にする。
        var name = CopyRename.ResolveName("a/b:c*?", "f", ctx, 1);
        Assert.Equal("a_b_c__", name);
    }

    [Fact]
    public void ResolveAll_DetectsDuplicates()
    {
        var items = new[]
        {
            new CopyRename.RenameContext("DSC1.JPG", Taken),
            new CopyRename.RenameContext("DSC2.JPG", Taken),
        };
        // テンプレートが定数（連番なし）だと全件同名へ衝突する。
        CopyRename.ResolveAll("{YYYY}", "f", items, out var dups);
        Assert.Single(dups);
        Assert.Equal("2026", dups[0]);
    }

    [Fact]
    public void ResolveAll_NoDuplicates_WhenSeqUsed()
    {
        var items = new[]
        {
            new CopyRename.RenameContext("DSC1.JPG", Taken),
            new CopyRename.RenameContext("DSC2.JPG", Taken),
        };
        CopyRename.ResolveAll("{YYYY}_{seq}", "f", items, out var dups);
        Assert.Empty(dups);
    }

    [Fact]
    public void BuildBatch_Overwrite_EmitsHeaderAndForCopyLines()
    {
        var items = new[]
        {
            new CopyRename.RenameContext("DSC001.JPG", Taken),
            new CopyRename.RenameContext("DSC002.JPG", Taken),
        };
        var text = CopyRename.BuildBatch(
            @"D:\Photos\20260228", @"E:\selected", "{folder}_{seq:000}",
            CopyRename.OnExist.Overwrite, "2026-06-21 12:34:56", items);

        var lines = text.Split("\r\n");
        // @echo off は付けない（コマンドをログにエコーさせるため）。
        Assert.Equal("chcp 65001 > nul", lines[0]);
        Assert.Equal("@rem CopyRename generated 2026-06-21 12:34:56", lines[1]);
        Assert.Equal(@"@rem From: D:\Photos\20260228", lines[2]);
        Assert.Equal(@"@rem To: E:\selected", lines[3]);
        Assert.Equal("@rem Template: {folder}_{seq:000}   OnExist: Overwrite   Count: 2", lines[4]);
        Assert.Equal(@"set FROMDIR=D:\Photos\20260228", lines[5]);
        Assert.Equal("set TODIR=.", lines[6]);
        // RAW+JPEG をまとめる for ループ＋拡張子保持。新ベース名はフォルダ名＋連番。
        Assert.Equal(
            @"for %%F in (""%FROMDIR%\DSC001.*"") do copy /y ""%%F"" ""%TODIR%\20260228_001%%~xF""",
            lines[7]);
        Assert.Equal(
            @"for %%F in (""%FROMDIR%\DSC002.*"") do copy /y ""%%F"" ""%TODIR%\20260228_002%%~xF""",
            lines[8]);
        Assert.Equal("", lines[9]);
    }

    [Fact]
    public void BuildBatch_Skip_EmitsIfNotExist()
    {
        var items = new[] { new CopyRename.RenameContext("DSC001.JPG", Taken) };
        var text = CopyRename.BuildBatch(
            @"D:\src", @"E:\dst", "{name}", CopyRename.OnExist.Skip, "t", items);

        Assert.Contains(
            @"for %%F in (""%FROMDIR%\DSC001.*"") do if not exist ""%TODIR%\DSC001%%~xF"" copy ""%%F"" ""%TODIR%\DSC001%%~xF""",
            text);
    }
}
