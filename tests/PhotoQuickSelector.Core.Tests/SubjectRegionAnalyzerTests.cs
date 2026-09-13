using System.Threading;
using PhotoQuickSelector.Core;
using Xunit;

namespace PhotoQuickSelector.Core.Tests;

/// <summary>
/// <see cref="SubjectRegionAnalyzer"/> の検証。合成画像（平坦背景＋鋭い正方形、方向別ボックスブラー）で
/// 「被写体タイルが見つかる」「一方向ブレの向きが検出できる」「決定性」等を確認する。
/// </summary>
public class SubjectRegionAnalyzerTests
{
    /// <summary>グレースケール生成関数から BGRA8（alpha=255 固定・パディング無し）バッファを作る。</summary>
    private static byte[] MakeBgra(int w, int h, Func<int, int, byte> gray)
    {
        var buf = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = (y * w + x) * 4;
                byte v = gray(x, y);
                buf[idx] = v;
                buf[idx + 1] = v;
                buf[idx + 2] = v;
                buf[idx + 3] = 255;
            }
        }
        return buf;
    }

    /// <summary>double 平面から BGRA8 バッファを作る（0..255 にクランプして丸める）。</summary>
    private static byte[] MakeBgraFromPlane(int w, int h, double[,] plane)
        => MakeBgra(w, h, (x, y) => (byte)Math.Clamp(Math.Round(plane[y, x]), 0, 255));

    /// <summary>平坦背景（40）の中央に鋭い正方形（220）を置いた平面を作る。</summary>
    private static double[,] MakeSquarePlane(int w, int h, int squareX, int squareY, int squareSize)
    {
        var plane = new double[h, w];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                plane[y, x] = (x >= squareX && x < squareX + squareSize && y >= squareY && y < squareY + squareSize) ? 220.0 : 40.0;
        return plane;
    }

    /// <summary>水平方向のみの分離ボックスブラー（境界は端値複製）。垂直エッジ（x方向の勾配）だけを鈍らせる。</summary>
    private static double[,] BoxBlurHorizontal(double[,] src, int w, int h, int radius)
    {
        if (radius <= 0) return (double[,])src.Clone();
        var dst = new double[h, w];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double sum = 0;
                int count = 0;
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int xx = Math.Clamp(x + dx, 0, w - 1);
                    sum += src[y, xx];
                    count++;
                }
                dst[y, x] = sum / count;
            }
        return dst;
    }

    /// <summary>両方向の分離ボックスブラー（等方性ボケの比較用）。</summary>
    private static double[,] BoxBlurBoth(double[,] src, int w, int h, int radius)
    {
        if (radius <= 0) return (double[,])src.Clone();
        var h1 = BoxBlurHorizontal(src, w, h, radius);
        var dst = new double[h, w];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double sum = 0;
                int count = 0;
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int yy = Math.Clamp(y + dy, 0, h - 1);
                    sum += h1[yy, x];
                    count++;
                }
                dst[y, x] = sum / count;
            }
        return dst;
    }

    // ------------------------------------------------------------------
    // 1. 鋭い正方形：被写体タイルが見つかり、全ビンの幅が小さく、比が 1 に近い。
    // ------------------------------------------------------------------
    [Fact]
    public void SharpSquare_FindsSubjectTilesWithSmallUniformWidths()
    {
        const int w = 1024, h = 768, tileSize = 64;
        var plane = MakeSquarePlane(w, h, 400, 300, 256);
        var bgra = MakeBgraFromPlane(w, h, plane);
        var options = new SubjectRegionOptions { TileSize = tileSize };

        var score = SubjectRegionAnalyzer.Analyze(bgra, w, h, w * 4, options, null);

        Assert.True(score.TileCount >= 1, $"TileCount={score.TileCount}");
        for (int b = 0; b < options.OrientationBins; b++)
        {
            if (score.BinWidthCounts[b] == 0) continue;
            Assert.True(score.BinMedianWidths[b] <= 3.0, $"bin {b} width={score.BinMedianWidths[b]}");
        }
        Assert.False(double.IsNaN(score.WidthRatio));
        Assert.True(score.WidthRatio <= 1.5, $"WidthRatio={score.WidthRatio}");
    }

    // ------------------------------------------------------------------
    // 2. 水平方向のみのブラー：垂直エッジ（0°ビン）が最悪となり、比が大きい。
    // ------------------------------------------------------------------
    [Fact]
    public void HorizontalBlurOnly_WorstBinIsNearZeroDegrees_RatioIsLarge()
    {
        const int w = 1024, h = 768, tileSize = 64;
        var plane = MakeSquarePlane(w, h, 400, 300, 256);
        var blurred = BoxBlurHorizontal(plane, w, h, 15);
        var bgra = MakeBgraFromPlane(w, h, blurred);
        var options = new SubjectRegionOptions { TileSize = tileSize };

        var score = SubjectRegionAnalyzer.Analyze(bgra, w, h, w * 4, options, null);

        Assert.True(score.TileCount >= 1);
        Assert.NotEqual(-1, score.WorstBin);
        // 0° ビン＝ [0, 180/8) 度。水平ブラーは gx を鈍らせる＝勾配は x 方向（角度 0°）の垂直エッジがボケる。
        double worstDeg = SubjectRegionScore.BinCenterDegrees(score.WorstBin, options.OrientationBins);
        Assert.True(worstDeg < 180.0 / options.OrientationBins, $"worst bin center={worstDeg}");
        Assert.True(score.WorstWidth > score.BestWidth * 2, $"worst={score.WorstWidth} best={score.BestWidth}");
        Assert.True(score.WorstWidth >= 8, $"WorstWidth={score.WorstWidth}");
    }

    // ------------------------------------------------------------------
    // 3. 等方性ボケ：ボケが強いほど WorstWidth が単調に増える。
    // ------------------------------------------------------------------
    [Fact]
    public void IsotropicBlur_WorstWidthIncreasesWithBlurRadius()
    {
        const int w = 1024, h = 768, tileSize = 64;
        var plane = MakeSquarePlane(w, h, 400, 300, 256);
        var options = new SubjectRegionOptions { TileSize = tileSize };

        double WorstWidth(int radius)
        {
            var blurred = BoxBlurBoth(plane, w, h, radius);
            var bgra = MakeBgraFromPlane(w, h, blurred);
            return SubjectRegionAnalyzer.Analyze(bgra, w, h, w * 4, options, null).WorstWidth;
        }

        double sharp = WorstWidth(0);
        double blurred = WorstWidth(6);

        Assert.False(double.IsNaN(sharp));
        Assert.False(double.IsNaN(blurred));
        Assert.True(blurred > sharp, $"sharp={sharp} blurred={blurred}");
    }

    // ------------------------------------------------------------------
    // 4. 一様画像：被写体タイル無し・幅は NaN。
    // ------------------------------------------------------------------
    [Fact]
    public void UniformImage_NoTilesAndNaNWidths()
    {
        var bgra = MakeBgra(256, 256, (_, _) => 128);
        var options = new SubjectRegionOptions { TileSize = 64 };

        var score = SubjectRegionAnalyzer.Analyze(bgra, 256, 256, 256 * 4, options, null);

        Assert.Equal(0, score.TileCount);
        Assert.True(double.IsNaN(score.Tenengrad));
        Assert.True(double.IsNaN(score.EdgeDensity));
        Assert.True(double.IsNaN(score.PerEdgeMag2));
        Assert.True(double.IsNaN(score.AnisotropyRatio));
        Assert.True(double.IsNaN(score.WorstWidth));
        Assert.True(double.IsNaN(score.BestWidth));
        Assert.True(double.IsNaN(score.WidthRatio));
        Assert.Equal(-1, score.WorstBin);
        for (int b = 0; b < options.OrientationBins; b++)
        {
            Assert.Equal(0, score.BinEdgeCounts[b]);
            Assert.Equal(0, score.BinWidthCounts[b]);
            Assert.True(double.IsNaN(score.BinMedianWidths[b]));
        }
    }

    // ------------------------------------------------------------------
    // 5. AF 窓：正方形に重なる窓はエッジ有り、平坦部の窓は 0、null は 0。
    // ------------------------------------------------------------------
    [Fact]
    public void AfWindow_EdgeCountsReflectOverlapWithSubject()
    {
        const int w = 1024, h = 768, tileSize = 64;
        var plane = MakeSquarePlane(w, h, 400, 300, 256);
        var bgra = MakeBgraFromPlane(w, h, plane);
        var options = new SubjectRegionOptions { TileSize = tileSize };

        var onSquare = SubjectRegionAnalyzer.Analyze(bgra, w, h, w * 4, options, new RectI(390, 300, 40, 40));
        Assert.True(onSquare.AfWindowEdgeCount > 0, $"AfWindowEdgeCount={onSquare.AfWindowEdgeCount}");
        Assert.True(onSquare.AfWindowPixelCount > 0);

        var onFlat = SubjectRegionAnalyzer.Analyze(bgra, w, h, w * 4, options, new RectI(10, 10, 40, 40));
        Assert.Equal(0, onFlat.AfWindowEdgeCount);
        Assert.True(onFlat.AfWindowPixelCount > 0);

        var noWindow = SubjectRegionAnalyzer.Analyze(bgra, w, h, w * 4, options, null);
        Assert.Equal(0, noWindow.AfWindowEdgeCount);
        Assert.Equal(0, noWindow.AfWindowPixelCount);
    }

    // ------------------------------------------------------------------
    // 5b. 絶対下限（MinTileEdgeFraction）：平坦背景に極小の暗点があるだけの画像は、相対基準
    //     （DensityRatio）だけなら「最大タイルの25%」を満たしてしまうが、絶対下限未満なので
    //     被写体タイルは見つからない。
    // ------------------------------------------------------------------
    [Fact]
    public void TinyDot_BelowMinTileEdgeFraction_ReturnsEmptySubjectRegion()
    {
        const int w = 1024, h = 768; // 既定 TileSize=256 のまま（minTileEdges = ceil(0.005*256*256) = 328）
        var bgra = MakeBgra(w, h, (x, y) =>
            (x >= 500 && x < 502 && y >= 400 && y < 402) ? (byte)20 : (byte)128);
        var options = new SubjectRegionOptions();

        var score = SubjectRegionAnalyzer.Analyze(bgra, w, h, w * 4, options, null);

        Assert.Equal(0, score.TileCount);
        Assert.True(double.IsNaN(score.Tenengrad));
        Assert.True(double.IsNaN(score.WorstWidth));
        Assert.True(double.IsNaN(score.WidthRatio));
        Assert.Equal(-1, score.WorstBin);
    }

    // ------------------------------------------------------------------
    // 6. 決定性：同一入力を2回解析すると全フィールドが一致する（配列は要素単位で比較。既定 Equals は
    //    参照比較になるため全体を Assert.Equal に渡さない＝SubjectRegionScore の remarks 参照）。
    // ------------------------------------------------------------------
    [Fact]
    public void Analyze_IsDeterministicAcrossRepeatedRuns()
    {
        const int w = 1024, h = 768, tileSize = 64;
        var rnd = new Random(2024);
        var bgra = MakeBgra(w, h, (_, _) => (byte)rnd.Next(0, 256));
        var options = new SubjectRegionOptions { TileSize = tileSize };
        var window = new RectI(100, 100, 300, 300);

        var first = SubjectRegionAnalyzer.Analyze(bgra, w, h, w * 4, options, window);
        var second = SubjectRegionAnalyzer.Analyze(bgra, w, h, w * 4, options, window);

        Assert.Equal(first.TileCount, second.TileCount);
        Assert.Equal(first.Bounds, second.Bounds);
        Assert.Equal(first.Tenengrad, second.Tenengrad);
        Assert.Equal(first.EdgeDensity, second.EdgeDensity);
        Assert.Equal(first.PerEdgeMag2, second.PerEdgeMag2);
        Assert.Equal(first.AnisotropyRatio, second.AnisotropyRatio);
        Assert.Equal(first.DominantGradientDegrees, second.DominantGradientDegrees);
        Assert.Equal(first.WorstWidth, second.WorstWidth);
        Assert.Equal(first.WorstBin, second.WorstBin);
        Assert.Equal(first.BestWidth, second.BestWidth);
        Assert.Equal(first.WidthRatio, second.WidthRatio);
        Assert.Equal(first.AfWindowEdgeCount, second.AfWindowEdgeCount);
        Assert.Equal(first.AfWindowPixelCount, second.AfWindowPixelCount);
        Assert.Equal(first.TileSize, second.TileSize);
        Assert.Equal(first.Threshold, second.Threshold);
        Assert.Equal(first.Version, second.Version);
        Assert.Equal(first.BinEdgeCounts, second.BinEdgeCounts);
        Assert.Equal(first.BinWidthCounts, second.BinWidthCounts);
        Assert.Equal(first.BinMedianWidths, second.BinMedianWidths);
    }

    // ------------------------------------------------------------------
    // 7. 引数検証。
    // ------------------------------------------------------------------
    [Fact]
    public void WrongStride_ThrowsArgumentException()
    {
        var bgra = MakeBgra(10, 10, (_, _) => 100);
        Assert.Throws<ArgumentException>(() =>
            SubjectRegionAnalyzer.Analyze(bgra, 10, 10, 10 * 4 - 1, new SubjectRegionOptions(), null));
    }

    [Fact]
    public void TooShortBuffer_ThrowsArgumentException()
    {
        var bgra = new byte[10 * 10 * 4 - 1];
        Assert.Throws<ArgumentException>(() =>
            SubjectRegionAnalyzer.Analyze(bgra, 10, 10, 10 * 4, new SubjectRegionOptions(), null));
    }

    [Fact]
    public void TinyImage_ReturnsEmptyScoreWithoutThrowing()
    {
        var bgra = MakeBgra(2, 2, (_, _) => 200);
        var score = SubjectRegionAnalyzer.Analyze(bgra, 2, 2, 2 * 4, new SubjectRegionOptions(), null);

        Assert.Equal(0, score.TileCount);
        Assert.True(double.IsNaN(score.Tenengrad));
        Assert.Equal(0, score.AfWindowEdgeCount);
        Assert.Equal(0, score.AfWindowPixelCount);
    }

    // ------------------------------------------------------------------
    // 8. キャンセル：事前キャンセル済みトークンを渡すと OperationCanceledException を投げる。
    // ------------------------------------------------------------------
    [Fact]
    public void Analyze_PreCancelledToken_ThrowsOperationCanceledException()
    {
        const int w = 512, h = 512;
        var rnd = new Random(2024);
        var bgra = MakeBgra(w, h, (_, _) => (byte)rnd.Next(0, 256));
        var options = new SubjectRegionOptions();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            SubjectRegionAnalyzer.Analyze(bgra, w, h, w * 4, options, null, cts.Token));
    }

    // ------------------------------------------------------------------
    // 9. BinCenterDegrees ヘルパー。
    // ------------------------------------------------------------------
    [Fact]
    public void BinCenterDegrees_ReturnsBinMidpoint()
    {
        Assert.Equal(11.25, SubjectRegionScore.BinCenterDegrees(0, 8), 6);
        Assert.Equal(168.75, SubjectRegionScore.BinCenterDegrees(7, 8), 6);
    }
}
