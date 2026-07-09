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

    /// <summary>
    /// Olympus / OM の AF 枠（Camera Settings tag 0x0304）を既知の 1 枚で厳密検証する。
    /// P2280057.JPG は AF Areas = (140/255,118/255)-(147/255,127/255)。案A の 2×255 基準へ
    /// 中心=(287,245)・サイズ=(14,18)・基準=(510,510) で無損失にマップされることを確認する。
    /// </summary>
    [Fact]
    public void ReadOlympusFocus_MapsAfAreaToDoubledReference()
    {
        var path = System.IO.Path.Combine(ImagesFolder, "P2280057.JPG");
        if (!File.Exists(path)) return; // 実画像が無い環境ではスキップ相当

        var meta = MetadataReader.Read(path);

        Assert.NotNull(meta.FocusPoint);
        Assert.NotNull(meta.FocusSize);
        Assert.NotNull(meta.FocusReferenceSize);

        Assert.Equal(new SizeI(510, 510), meta.FocusReferenceSize!.Value);
        // 中心 = (left+right, top+bottom), サイズ = (2*(right-left), 2*(bottom-top))
        Assert.Equal(new PointI(140 + 147, 118 + 127), meta.FocusPoint!.Value);
        Assert.Equal(new SizeI(2 * (147 - 140), 2 * (127 - 118)), meta.FocusSize!.Value);
    }
}
