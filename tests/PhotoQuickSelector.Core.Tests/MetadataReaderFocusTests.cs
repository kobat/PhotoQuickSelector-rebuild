using PhotoQuickSelector.Core;
using Xunit;

namespace PhotoQuickSelector.Core.Tests;

/// <summary>
/// Sony AF フォーカス情報（tag 0x2027 / 0x2037）の読取り検証。
/// 実画像に依存するため、テスト画像フォルダが無い環境では何もせず通過する
/// （環境変数 <c>PQS_TEST_IMAGES</c> でフォルダを上書き可能）。
/// </summary>
public class MetadataReaderFocusTests
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

    [Fact]
    public void ReadSonyFocus_PopulatesReferenceSizeAndPoint()
    {
        var path = FindSonyImage();
        if (path == null) return; // 実画像が無い環境ではスキップ相当

        var meta = MetadataReader.Read(path);

        Assert.NotNull(meta.FocusPoint);
        Assert.NotNull(meta.FocusReferenceSize);

        var reference = meta.FocusReferenceSize!.Value;
        Assert.True(reference.Width > 0, "AF 基準幅が正であること");
        Assert.True(reference.Height > 0, "AF 基準高さが正であること");

        // 基準サイズは元画像サイズとほぼ一致するはず（フル解像度 JPEG 前提）。
        Assert.InRange(reference.Width, meta.OriginalWidth / 2, meta.OriginalWidth * 2);
        Assert.InRange(reference.Height, meta.OriginalHeight / 2, meta.OriginalHeight * 2);

        // フォーカス点は基準サイズの範囲内に収まる。
        var point = meta.FocusPoint!.Value;
        Assert.InRange(point.X, 0, reference.Width);
        Assert.InRange(point.Y, 0, reference.Height);
    }
}
