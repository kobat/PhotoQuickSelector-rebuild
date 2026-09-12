using System.Threading;
using PhotoQuickSelector.Core;
using Xunit;

namespace PhotoQuickSelector.Core.Tests;

/// <summary>
/// <see cref="SharpnessMetrics"/>（LaplacianVariance/Brenner/Reblur/EdgeWidth）の検証。
/// 合成画像を使う方針は <see cref="SharpnessAnalyzerTests"/> と同じ（ヘルパーもそちらから複製）。
/// </summary>
public class SharpnessMetricsTests
{
    // ------------------------------------------------------------------
    // ヘルパー（SharpnessAnalyzerTests.cs から複製。private のため共有できない）。
    // ------------------------------------------------------------------

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

    private static byte[] MakeBgraFromPlane(int w, int h, double[,] plane)
        => MakeBgra(w, h, (x, y) => (byte)Math.Clamp(Math.Round(plane[y, x]), 0, 255));

    private static double[,] ToPlane(int w, int h, Func<int, int, double> f)
    {
        var plane = new double[h, w];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                plane[y, x] = f(x, y);
        return plane;
    }

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

    /// <summary>チェッカー模様（period=2px の白黒市松）を矩形内に敷く。エッジが密で EdgeWidth 用の
    /// 「≥100 エッジ」を満たしやすい。</summary>
    private static byte[] MakeCheckerRegionImage(int w, int h, int regionX, int regionY, int regionSize, int period)
    {
        return MakeBgra(w, h, (x, y) =>
        {
            bool inRegion = x >= regionX && x < regionX + regionSize && y >= regionY && y < regionY + regionSize;
            if (!inRegion) return 128;
            int cell = (x / period + y / period) % 2;
            return cell == 0 ? (byte)0 : (byte)255;
        });
    }

    // ------------------------------------------------------------------
    // 1. ボケ単調性：垂直エッジをボックスブラーすると、higher-is-sharper 系は単調減少、
    //    EdgeWidth は単調増加。
    // ------------------------------------------------------------------
    [Theory]
    [InlineData(SharpnessMetric.LaplacianVariance)]
    [InlineData(SharpnessMetric.Brenner)]
    public void BlurMonotonicity_HigherIsSharperMetricsStrictlyDecrease(SharpnessMetric metric)
    {
        const int w = 200, h = 120;
        var edge = ToPlane(w, h, (x, _) => x < w / 2 ? 40.0 : 220.0);

        double Score(int radius)
        {
            var blurred = radius == 0 ? edge : BoxBlurHorizontal(edge, w, h, radius);
            var bgra = MakeBgraFromPlane(w, h, blurred);
            return SharpnessMetrics.Compute(metric, bgra, w, h, w * 4, 256, null).Global;
        }

        double s0 = Score(0), s1 = Score(1), s2 = Score(2), s4 = Score(4);

        Assert.True(s0 > s1, $"r0={s0} r1={s1}");
        Assert.True(s1 > s2, $"r1={s1} r2={s2}");
        Assert.True(s2 > s4, $"r2={s2} r4={s4}");
    }

    /// <summary>
    /// Reblur は「垂直方向の勾配減衰」と「水平方向の勾配減衰」の max を取るため、単一方向にしか
    /// 変化の無い画像（縦一様な垂直エッジ等）では片方の sF が 0 になり NaN 化してしまう
    /// （0 除算が自然に NaN になる仕様どおりだが、この検証には使えない）。両方向に十分なテクスチャを
    /// 持つ乱数画像へ等方 box blur を掛けて単調性を確認する。
    /// </summary>
    [Fact]
    public void BlurMonotonicity_ReblurStrictlyDecreases()
    {
        const int w = 160, h = 160;
        var rnd = new Random(31415);
        var noise = ToPlane(w, h, (_, _) => rnd.Next(0, 256));

        double Score(int radius)
        {
            var blurred = radius == 0 ? noise : BoxBlurVertical(BoxBlurHorizontal(noise, w, h, radius), w, h, radius);
            var bgra = MakeBgraFromPlane(w, h, blurred);
            return SharpnessMetrics.Compute(SharpnessMetric.Reblur, bgra, w, h, w * 4, 256, null).Global;
        }

        double s0 = Score(0), s1 = Score(1), s2 = Score(2), s4 = Score(4);

        Assert.True(s0 > s1, $"r0={s0} r1={s1}");
        Assert.True(s1 > s2, $"r1={s1} r2={s2}");
        Assert.True(s2 > s4, $"r2={s2} r4={s4}");
    }

    /// <summary>
    /// EdgeWidth のウォークは狭義単調（<see cref="SharpnessMetrics"/> の Walk 参照）で止まるため、
    /// 無限に広い平坦領域を挟む単一エッジでは平坦部の遥か先まで進まず境界で正しく止まる。
    /// ここでは有限幅の平坦部を持つ周期的な矩形波（各プラトー幅20px、ウォーク上限64pxより十分小さい）を
    /// 使い、ボケが強くなるほど遷移部の実効幅（＝隣接プラトーとの狭義単調が続く距離）が伸びることを確認する。
    /// </summary>
    [Fact]
    public void BlurMonotonicity_EdgeWidthStrictlyIncreases()
    {
        const int w = 200, h = 40, period = 40;
        var square = ToPlane(w, h, (x, _) => (x / (period / 2)) % 2 == 0 ? 40.0 : 220.0);

        double Score(int radius)
        {
            var blurred = radius == 0 ? square : BoxBlurHorizontal(square, w, h, radius);
            var bgra = MakeBgraFromPlane(w, h, blurred);
            return SharpnessMetrics.Compute(SharpnessMetric.EdgeWidth, bgra, w, h, w * 4, 256, null).Global;
        }

        double s0 = Score(0), s1 = Score(1), s2 = Score(2), s4 = Score(4);

        Assert.True(s0 < s1, $"r0={s0} r1={s1}");
        Assert.True(s1 < s2, $"r1={s1} r2={s2}");
        Assert.True(s2 < s4, $"r2={s2} r4={s4}");
    }

    private static double[,] BoxBlurVertical(double[,] src, int w, int h, int radius)
    {
        if (radius <= 0) return (double[,])src.Clone();
        var dst = new double[h, w];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double sum = 0;
                int count = 0;
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int yy = Math.Clamp(y + dy, 0, h - 1);
                    sum += src[yy, x];
                    count++;
                }
                dst[y, x] = sum / count;
            }
        return dst;
    }

    // ------------------------------------------------------------------
    // 2. 一様画像：LaplacianVariance=0, Brenner=0, Reblur=NaN, EdgeWidth=NaN。
    // ------------------------------------------------------------------
    [Fact]
    public void UniformImage_ReturnsExpectedNeutralValues()
    {
        var bgra = MakeBgra(64, 64, (_, _) => 128);

        var lapv = SharpnessMetrics.Compute(SharpnessMetric.LaplacianVariance, bgra, 64, 64, 64 * 4, 256, null);
        var bren = SharpnessMetrics.Compute(SharpnessMetric.Brenner, bgra, 64, 64, 64 * 4, 256, null);
        var reblur = SharpnessMetrics.Compute(SharpnessMetric.Reblur, bgra, 64, 64, 64 * 4, 256, null);
        var edgew = SharpnessMetrics.Compute(SharpnessMetric.EdgeWidth, bgra, 64, 64, 64 * 4, 256, null);

        Assert.Equal(0, lapv.Global);
        Assert.Equal(0, bren.Global);
        Assert.True(double.IsNaN(reblur.Global));
        Assert.True(double.IsNaN(edgew.Global));

        Assert.True(lapv.HigherIsSharper);
        Assert.True(bren.HigherIsSharper);
        Assert.True(reblur.HigherIsSharper);
        Assert.False(edgew.HigherIsSharper);
    }

    // ------------------------------------------------------------------
    // 3. タイル位置特定（higher-is-sharper 3手法）：市松模様タイルを (128,64) に埋め込む。
    // ------------------------------------------------------------------
    [Theory]
    [InlineData(SharpnessMetric.LaplacianVariance)]
    [InlineData(SharpnessMetric.Brenner)]
    [InlineData(SharpnessMetric.Reblur)]
    public void TileLocalization_HigherIsSharperMetricsFindHighContrastTile(SharpnessMetric metric)
    {
        const int w = 256, h = 256, tileSize = 64;
        var bgra = MakeCheckerRegionImage(w, h, 128, 64, tileSize, period: 2);

        var score = SharpnessMetrics.Compute(metric, bgra, w, h, w * 4, tileSize, null);

        Assert.Equal(128, score.MaxTileX);
        Assert.Equal(64, score.MaxTileY);
        Assert.True(score.MaxTile > score.Global, $"MaxTile={score.MaxTile} Global={score.Global}");
    }

    // ------------------------------------------------------------------
    // 4. タイル位置特定（EdgeWidth）：鮮鋭タイル (128,64) に十分なエッジ数、
    //    もう1タイル (0,0) は同じ模様をボケさせてエッジ数不足（<100）にする。
    // ------------------------------------------------------------------
    [Fact]
    public void TileLocalization_EdgeWidthPicksSharpTileOverBlurredOne()
    {
        const int w = 256, h = 256, tileSize = 64, period = 4;

        // (128,64): 鮮鋭な市松（period=4 の縞。閾値32を十分超えるコントラスト）。
        var bgra = MakeCheckerRegionImage(w, h, 128, 64, tileSize, period);

        // (0,0): 同じ模様を作ってから radius=2 でボックスブラーし、エッジをほぼ消す。
        var plane = ToPlane(w, h, (x, y) =>
        {
            bool inRegion = x < tileSize && y < tileSize;
            if (!inRegion) return 128.0;
            int cell = (x / period + y / period) % 2;
            return cell == 0 ? 0.0 : 255.0;
        });
        var blurredPlane = BoxBlurHorizontal(plane, w, h, 2);
        // 縦方向にも同じ半径でぼかして両方向のエッジを十分減衰させる。
        var blurredPlaneV = BoxBlurVertical(blurredPlane, w, h, 2);
        var blurredBgra = MakeBgraFromPlane(w, h, blurredPlaneV);

        // 2領域を1枚に合成：(0,0) 側はぼかし版、それ以外は鮮鋭版から採用。
        var combined = MakeBgra(w, h, (x, y) =>
        {
            bool inBlurredTile = x < tileSize && y < tileSize;
            int idx = (y * w + x) * 4;
            return inBlurredTile ? blurredBgra[idx] : bgra[idx];
        });

        var score = SharpnessMetrics.Compute(SharpnessMetric.EdgeWidth, combined, w, h, w * 4, tileSize, null);

        Assert.Equal(128, score.MaxTileX);
        Assert.Equal(64, score.MaxTileY);
    }

    // ------------------------------------------------------------------
    // 5. AF 窓：タイルと同一矩形なら higher-is-sharper 系で MaxTile と一致・画像外は NaN。
    // ------------------------------------------------------------------
    [Theory]
    [InlineData(SharpnessMetric.LaplacianVariance)]
    [InlineData(SharpnessMetric.Brenner)]
    [InlineData(SharpnessMetric.Reblur)]
    public void AfWindow_MatchesTileWhenSameRect_NaNWhenOutside(SharpnessMetric metric)
    {
        const int w = 256, h = 256, tileSize = 64;
        var bgra = MakeCheckerRegionImage(w, h, 128, 64, tileSize, period: 2);

        var withTile = SharpnessMetrics.Compute(metric, bgra, w, h, w * 4, tileSize, null);
        var sameRect = SharpnessMetrics.Compute(metric, bgra, w, h, w * 4, tileSize, new RectI(128, 64, 64, 64));
        Assert.Equal(withTile.MaxTile, sameRect.AfWindow, 6);

        var outside = SharpnessMetrics.Compute(metric, bgra, w, h, w * 4, tileSize, new RectI(1000, 1000, 64, 64));
        Assert.True(double.IsNaN(outside.AfWindow));
    }

    // ------------------------------------------------------------------
    // 6. stride：パディングが混入していても結果は詰め込み版と完全一致する（全手法）。
    // ------------------------------------------------------------------
    [Theory]
    [MemberData(nameof(AllMetrics))]
    public void Stride_PaddingBytesDoNotAffectResult(SharpnessMetric metric)
    {
        const int w = 50, h = 40;
        var rnd = new Random(4242);
        var packed = MakeBgra(w, h, (_, _) => (byte)rnd.Next(0, 256));

        const int extraPad = 12;
        int paddedStride = w * 4 + extraPad;
        var padded = new byte[paddedStride * h];
        new Random(1).NextBytes(padded);
        for (int y = 0; y < h; y++)
            Array.Copy(packed, y * w * 4, padded, y * paddedStride, w * 4);

        var window = new RectI(5, 5, 20, 20);
        var scorePacked = SharpnessMetrics.Compute(metric, packed, w, h, w * 4, 16, window);
        var scorePadded = SharpnessMetrics.Compute(metric, padded, w, h, paddedStride, 16, window);

        Assert.Equal(scorePacked, scorePadded);
    }

    public static IEnumerable<object[]> AllMetrics()
    {
        yield return new object[] { SharpnessMetric.LaplacianVariance };
        yield return new object[] { SharpnessMetric.Brenner };
        yield return new object[] { SharpnessMetric.Reblur };
        yield return new object[] { SharpnessMetric.EdgeWidth };
    }

    // ------------------------------------------------------------------
    // 7. Reblur は乱数画像で常に [0,1] の範囲に収まる。
    // ------------------------------------------------------------------
    [Fact]
    public void Reblur_StaysWithinZeroToOneOnNoiseImage()
    {
        const int w = 200, h = 150;
        var rnd = new Random(555);
        var bgra = MakeBgra(w, h, (_, _) => (byte)rnd.Next(0, 256));

        var score = SharpnessMetrics.Compute(SharpnessMetric.Reblur, bgra, w, h, w * 4, 256, null);

        Assert.InRange(score.Global, 0.0, 1.0);
    }

    // ------------------------------------------------------------------
    // 8. EdgeWidth：既知幅の線形ランプエッジで平均幅が 7..10px 程度になる。
    // ------------------------------------------------------------------
    [Fact]
    public void EdgeWidth_LinearRampReportsExpectedMeanWidth()
    {
        const int w = 200, h = 120;
        const int rampStart = 96, rampLen = 8;
        var plane = ToPlane(w, h, (x, _) =>
        {
            if (x < rampStart) return 40.0;
            if (x >= rampStart + rampLen) return 220.0;
            double t = (x - rampStart) / (double)rampLen;
            return 40.0 + t * (220.0 - 40.0);
        });
        var bgra = MakeBgraFromPlane(w, h, plane);

        var score = SharpnessMetrics.Compute(SharpnessMetric.EdgeWidth, bgra, w, h, w * 4, 256, null);

        Assert.False(double.IsNaN(score.Global));
        Assert.InRange(score.Global, 7.0, 10.0);
    }

    // ------------------------------------------------------------------
    // 9. 決定性：全手法で同じ入力から毎回ビット同一の結果を返す。
    // ------------------------------------------------------------------
    [Theory]
    [MemberData(nameof(AllMetrics))]
    public void Compute_IsDeterministicAcrossRepeatedRuns(SharpnessMetric metric)
    {
        const int w = 1000, h = 700;
        var rnd = new Random(2024);
        var bgra = MakeBgra(w, h, (_, _) => (byte)rnd.Next(0, 256));
        var window = new RectI(100, 100, 300, 300);

        var first = SharpnessMetrics.Compute(metric, bgra, w, h, w * 4, 256, window);
        var second = SharpnessMetrics.Compute(metric, bgra, w, h, w * 4, 256, window);

        Assert.Equal(first, second);
    }

    // ------------------------------------------------------------------
    // 10. 極小画像：例外にならず一様画像相当の既定値を返す。
    // ------------------------------------------------------------------
    [Theory]
    [MemberData(nameof(AllMetrics))]
    public void TinyImage_ReturnsNeutralValueWithoutThrowing(SharpnessMetric metric)
    {
        var bgra = MakeBgra(3, 3, (_, _) => 200);
        var score = SharpnessMetrics.Compute(metric, bgra, 3, 3, 3 * 4, 256, null);

        Assert.Equal(0, score.MaxTileX);
        Assert.Equal(0, score.MaxTileY);
        Assert.True(double.IsNaN(score.AfWindow));
        if (metric is SharpnessMetric.Reblur or SharpnessMetric.EdgeWidth)
        {
            Assert.True(double.IsNaN(score.Global));
            Assert.True(double.IsNaN(score.MaxTile));
        }
        else
        {
            Assert.Equal(0, score.Global);
            Assert.Equal(0, score.MaxTile);
        }
    }

    [Fact]
    public void WrongStride_ThrowsArgumentException()
    {
        var bgra = MakeBgra(10, 10, (_, _) => 100);
        Assert.Throws<ArgumentException>(() =>
            SharpnessMetrics.Compute(SharpnessMetric.LaplacianVariance, bgra, 10, 10, 10 * 4 - 1, 256, null));
    }

    // ------------------------------------------------------------------
    // 12. キャンセル：事前キャンセル済みトークンは手法ごとに OperationCanceledException を投げる
    //     （LaplacianVariance/Brenner/Reblur は並列パスの ParallelOptions 経由、EdgeWidth は
    //     逐次ウォークの 64 行ごとのポーリング経由。いずれも呼び出し側で握りつぶさず伝播させる）。
    // ------------------------------------------------------------------
    [Theory]
    [InlineData(SharpnessMetric.LaplacianVariance)]
    [InlineData(SharpnessMetric.Brenner)]
    [InlineData(SharpnessMetric.Reblur)]
    [InlineData(SharpnessMetric.EdgeWidth)]
    public void Compute_PreCancelledToken_ThrowsOperationCanceledException(SharpnessMetric metric)
    {
        const int w = 512, h = 512;
        var rnd = new Random(2024);
        var bgra = MakeBgra(w, h, (_, _) => (byte)rnd.Next(0, 256));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            SharpnessMetrics.Compute(metric, bgra, w, h, w * 4, 256, null, cancellationToken: cts.Token));
    }
}
