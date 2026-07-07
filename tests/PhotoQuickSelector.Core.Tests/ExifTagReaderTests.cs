using PhotoQuickSelector.Core;
using Xunit;

namespace PhotoQuickSelector.Core.Tests;

/// <summary>
/// <see cref="ExifTagReader"/> の全タグダンプ検証。
/// 実画像に依存するため、テスト画像フォルダが無い環境では何もせず通過する
/// （環境変数 <c>PQS_TEST_IMAGES</c> でフォルダを上書き可能）。
/// </summary>
public class ExifTagReaderTests
{
    private static readonly string ImagesFolder =
        Environment.GetEnvironmentVariable("PQS_TEST_IMAGES")
        ?? @"D:\Users\kobat\tmp_ClaudeCode用\20260228";

    /// <summary>テストフォルダ内の Sony 機（DSC*.JPG）の先頭ファイル。無ければ null。</summary>
    private static string? FindSonyImage()
    {
        if (!Directory.Exists(ImagesFolder)) return null;
        return Directory
            .EnumerateFiles(ImagesFolder, "DSC*.JPG")
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    /// <summary>テストフォルダ内の Olympus 機（P22*.JPG）の先頭ファイル。無ければ null。</summary>
    private static string? FindOlympusImage()
    {
        if (!Directory.Exists(ImagesFolder)) return null;
        return Directory
            .EnumerateFiles(ImagesFolder, "P*.JPG")
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    [Fact]
    public void ReadAllTags_SonyImage_ContainsFNumberTag()
    {
        var path = FindSonyImage();
        if (path == null) return; // 実画像が無い環境ではスキップ相当

        var groups = ExifTagReader.ReadAllTags(path);
        Assert.NotEmpty(groups);

        var fNumberTag = groups
            .SelectMany(g => g.Tags)
            .FirstOrDefault(t => t.Name == "F-Number");

        Assert.NotNull(fNumberTag);
        Assert.False(string.IsNullOrWhiteSpace(fNumberTag!.Description));
    }

    [Fact]
    public void ReadAllTags_OlympusImage_ContainsMakernoteDirectory()
    {
        var path = FindOlympusImage();
        if (path == null) return; // 実画像が無い環境ではスキップ相当

        var groups = ExifTagReader.ReadAllTags(path);
        Assert.NotEmpty(groups);

        Assert.Contains(groups, g => g.DirectoryName.Contains("Olympus"));
    }

    [Fact]
    public void ReadAllTags_NonExistentPath_ReturnsEmptyWithoutThrowing()
    {
        var groups = ExifTagReader.ReadAllTags(@"C:\this\path\does\not\exist.jpg");
        Assert.Empty(groups);
    }

    [Fact]
    public void ReadAllTags_SonyImage_AllDescriptionsWithinMaxLength()
    {
        var path = FindSonyImage();
        if (path == null) return; // 実画像が無い環境ではスキップ相当

        var groups = ExifTagReader.ReadAllTags(path);
        Assert.NotEmpty(groups);

        foreach (var entry in groups.SelectMany(g => g.Tags))
        {
            Assert.True(entry.Description.Length <= 300,
                $"{entry.Name} の Description が 300 文字を超えている: {entry.Description.Length}");
        }
    }
}
