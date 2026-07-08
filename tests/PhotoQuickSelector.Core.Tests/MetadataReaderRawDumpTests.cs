using PhotoQuickSelector.Core;
using Xunit;

namespace PhotoQuickSelector.Core.Tests;

/// <summary>
/// 全生タグダンプ（<see cref="MetadataReader.ReadRawDump"/>、EXIF 詳細パネル用）の検証。
/// 実画像に依存するため、テスト画像フォルダが無い環境では何もせず通過する
/// （環境変数 <c>PQS_TEST_IMAGES</c> でフォルダを上書き可能）。
/// </summary>
public class MetadataReaderRawDumpTests
{
    private static readonly string ImagesFolder =
        Environment.GetEnvironmentVariable("PQS_TEST_IMAGES")
        ?? @"D:\Users\kobat\tmp_ClaudeCode用\20260228";

    private static string? FindSonyImage()
    {
        if (!Directory.Exists(ImagesFolder)) return null;
        return Directory
            .EnumerateFiles(ImagesFolder, "DSC*.JPG")
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    [Fact]
    public void ReadRawDump_MissingFile_ReturnsEmpty()
    {
        // 例外を投げず空リストを返す（存在しないパス）。
        var dump = MetadataReader.ReadRawDump(@"Z:\no\such\file.jpg");
        Assert.Empty(dump);
    }

    [Fact]
    public void ReadRawDump_RealImage_EnumeratesDirectoriesAndTags()
    {
        var path = FindSonyImage();
        if (path == null) return; // 実画像が無い環境ではスキップ相当

        var dump = MetadataReader.ReadRawDump(path);

        Assert.NotEmpty(dump);
        // 各ディレクトリは名前を持ち、タグを 1 件以上含む（空ディレクトリは除外している）。
        Assert.All(dump, d =>
        {
            Assert.False(string.IsNullOrWhiteSpace(d.Name));
            Assert.NotEmpty(d.Tags);
            Assert.All(d.Tags, t => Assert.False(string.IsNullOrWhiteSpace(t.Name)));
        });

        // JPEG の基本ディレクトリと Exif SubIFD は必ず含まれる。
        Assert.Contains(dump, d => d.Name.Contains("Exif", StringComparison.OrdinalIgnoreCase));

        // Sony 機なので Sony メーカーノートも列挙される（AF 情報等の生タグを含む）。
        Assert.Contains(dump, d => d.Name.Contains("Sony", StringComparison.OrdinalIgnoreCase));
    }
}
