using PhotoQuickSelector.Core;
using Xunit;

namespace PhotoQuickSelector.Core.Tests;

/// <summary>
/// <see cref="BgraDownscaler"/> の検証。面積平均縮小の正しさ（既知の合成画像で手計算した期待値との一致）・
/// 決定性・引数検証を確認する。
/// </summary>
public class BgraDownscalerTests
{
    /// <summary>グレースケール生成関数から BGRA8（alpha=255 固定・行末パディング <paramref name="pad"/> byte）を作る。</summary>
    private static byte[] MakeBgra(int w, int h, Func<int, int, byte> gray, int pad = 0)
    {
        int stride = w * 4 + pad;
        var buf = new byte[stride * h];
        // パディング領域はデコード対象外であることを検証するため、あえて非ゼロの番兵値で埋める。
        Array.Fill(buf, (byte)0xAB);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = y * stride + x * 4;
                byte v = gray(x, y);
                buf[idx] = v;     // B
                buf[idx + 1] = v; // G
                buf[idx + 2] = v; // R
                buf[idx + 3] = 255;
            }
        }
        return buf;
    }

    // ------------------------------------------------------------------
    // FitSize
    // ------------------------------------------------------------------
    [Fact]
    public void FitSize_Landscape_ScalesHeightToKeepAspect()
    {
        var (w, h) = BgraDownscaler.FitSize(4000, 3000, 1600);
        Assert.Equal(1600, w);
        Assert.Equal(1200, h);
    }

    [Fact]
    public void FitSize_Portrait_ScalesWidthToKeepAspect()
    {
        var (w, h) = BgraDownscaler.FitSize(3000, 4000, 1600);
        Assert.Equal(1200, w);
        Assert.Equal(1600, h);
    }

    [Fact]
    public void FitSize_AlreadySmallerThanLongEdge_DoesNotUpscale()
    {
        var (w, h) = BgraDownscaler.FitSize(800, 600, 1600);
        Assert.Equal(800, w);
        Assert.Equal(600, h);
    }

    [Fact]
    public void FitSize_LongEdgeEqualsSourceLongEdge_ReturnsSourceUnchanged()
    {
        var (w, h) = BgraDownscaler.FitSize(1600, 1200, 1600);
        Assert.Equal(1600, w);
        Assert.Equal(1200, h);
    }

    [Theory]
    [InlineData(0, 100, 10)]
    [InlineData(100, 0, 10)]
    [InlineData(100, 100, 0)]
    public void FitSize_NonPositiveArguments_Throws(int w, int h, int longEdge)
        => Assert.Throws<ArgumentException>(() => BgraDownscaler.FitSize(w, h, longEdge));

    // ------------------------------------------------------------------
    // AreaAverage: 一様画像はどう縮小しても一様のまま（整数比・非整数比の両方）。
    // ------------------------------------------------------------------
    [Theory]
    [InlineData(6, 4, 3, 2)] // 整数比 2:1
    [InlineData(5, 5, 3, 3)] // 非整数比 5:3
    public void AreaAverage_UniformImage_StaysUniform(int srcW, int srcH, int dstW, int dstH)
    {
        const byte gray = 77;
        var bgra = MakeBgra(srcW, srcH, (_, _) => gray);

        var result = BgraDownscaler.AreaAverage(bgra, srcW, srcH, srcW * 4, dstW, dstH);

        Assert.Equal(dstW * 4 * dstH, result.Length);
        for (int y = 0; y < dstH; y++)
        {
            for (int x = 0; x < dstW; x++)
            {
                int idx = (y * dstW + x) * 4;
                Assert.Equal(gray, result[idx]);
                Assert.Equal(gray, result[idx + 1]);
                Assert.Equal(gray, result[idx + 2]);
                Assert.Equal((byte)255, result[idx + 3]);
            }
        }
    }

    // ------------------------------------------------------------------
    // AreaAverage: 2x2 黒白チェッカーを 2:1 で 1x1 に縮小 → (0+255+255+0)/4=127.5 → 128 に丸める
    // （実装は MidpointRounding.AwayFromZero＝0.5 は絶対値方向、非負値なので実質「切り上げ」）。
    // ------------------------------------------------------------------
    [Fact]
    public void AreaAverage_Checkerboard2x2_DownscaledToOnePixel_RoundsHalfUpTo128()
    {
        var bgra = MakeBgra(2, 2, (x, y) => (x + y) % 2 == 0 ? (byte)0 : (byte)255);

        var result = BgraDownscaler.AreaAverage(bgra, 2, 2, 2 * 4, 1, 1);

        Assert.Equal(4, result.Length);
        Assert.Equal((byte)128, result[0]);
        Assert.Equal((byte)128, result[1]);
        Assert.Equal((byte)128, result[2]);
        Assert.Equal((byte)255, result[3]);
    }

    // ------------------------------------------------------------------
    // AreaAverage: 非整数比 3→2（scale=1.5）を既知の勾配 [0,100,200] で手計算した重みと突き合わせる。
    // dst[0] = (v0*1.0 + v1*0.5) / 1.5 = 50/1.5 = 33.33.. → 33
    // dst[1] = (v1*0.5 + v2*1.0) / 1.5 = 250/1.5 = 166.66.. → 167
    // ------------------------------------------------------------------
    [Fact]
    public void AreaAverage_NonIntegerRatio_MatchesHandComputedAreaWeights()
    {
        byte[] values = { 0, 100, 200 };
        var bgra = MakeBgra(3, 1, (x, _) => values[x]);

        var result = BgraDownscaler.AreaAverage(bgra, 3, 1, 3 * 4, 2, 1);

        Assert.Equal((byte)33, result[0]);
        Assert.Equal((byte)167, result[4]);
    }

    // ------------------------------------------------------------------
    // stride のパディング（行末の余りバイト）が読み飛ばされる（番兵値 0xAB が結果に混入しない）ことを確認。
    // ------------------------------------------------------------------
    [Fact]
    public void AreaAverage_StridePadding_IsHonored()
    {
        const int w = 4, h = 4, pad = 16;
        var bgra = MakeBgra(w, h, (x, y) => (byte)((x + y) % 2 == 0 ? 10 : 200), pad);

        var result = BgraDownscaler.AreaAverage(bgra, w, h, w * 4 + pad, 2, 2);

        // パディングの番兵値 0xAB(171) が混入していれば平均が大きく外れるはずなので、範囲チェックで検出する。
        for (int i = 0; i < result.Length; i += 4)
        {
            Assert.InRange(result[i], (byte)10, (byte)200);
            Assert.InRange(result[i + 1], (byte)10, (byte)200);
            Assert.InRange(result[i + 2], (byte)10, (byte)200);
        }
    }

    // ------------------------------------------------------------------
    // 決定性：同一入力を2回処理してビット同一の結果になること（Parallel.For のスケジューリング非依存）。
    // ------------------------------------------------------------------
    [Fact]
    public void AreaAverage_SameInput_IsDeterministic()
    {
        const int w = 400, h = 300;
        var rng = new Random(12345);
        var bgra = MakeBgra(w, h, (_, _) => (byte)rng.Next(256));

        var result1 = BgraDownscaler.AreaAverage(bgra, w, h, w * 4, 137, 91);
        var result2 = BgraDownscaler.AreaAverage(bgra, w, h, w * 4, 137, 91);

        Assert.Equal(result1, result2);
    }

    [Fact]
    public void AreaAverage_SameSizeDestination_ReturnsPackedCopyWithAlphaForced()
    {
        const int w = 3, h = 2, pad = 8;
        var bgra = MakeBgra(w, h, (x, y) => (byte)(x * 10 + y), pad);
        // alpha を255以外にして「常に255に固定」を検証する。
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                bgra[y * (w * 4 + pad) + x * 4 + 3] = 42;

        var result = BgraDownscaler.AreaAverage(bgra, w, h, w * 4 + pad, w, h);

        Assert.Equal(w * 4 * h, result.Length);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = (y * w + x) * 4;
                byte expected = (byte)(x * 10 + y);
                Assert.Equal(expected, result[idx]);
                Assert.Equal(expected, result[idx + 1]);
                Assert.Equal(expected, result[idx + 2]);
                Assert.Equal((byte)255, result[idx + 3]);
            }
        }
    }

    // ------------------------------------------------------------------
    // 引数検証。
    // ------------------------------------------------------------------
    [Fact]
    public void AreaAverage_NegativeWidth_Throws()
    {
        var bgra = new byte[16];
        Assert.Throws<ArgumentException>(() => BgraDownscaler.AreaAverage(bgra, -1, 4, 16, 1, 1));
    }

    [Fact]
    public void AreaAverage_NegativeHeight_Throws()
    {
        var bgra = new byte[16];
        Assert.Throws<ArgumentException>(() => BgraDownscaler.AreaAverage(bgra, 4, -1, 16, 1, 1));
    }

    [Fact]
    public void AreaAverage_StrideTooSmall_Throws()
    {
        var bgra = new byte[64];
        Assert.Throws<ArgumentException>(() => BgraDownscaler.AreaAverage(bgra, 4, 4, 8, 2, 2));
    }

    [Fact]
    public void AreaAverage_BufferTooShort_Throws()
    {
        var bgra = new byte[10];
        Assert.Throws<ArgumentException>(() => BgraDownscaler.AreaAverage(bgra, 4, 4, 16, 2, 2));
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(5, 4)] // srcWidth(4) を超える＝拡大は非対応
    public void AreaAverage_DstWidthOutOfRange_Throws(int dstWidth, int srcWidth)
    {
        var bgra = new byte[srcWidth * 4 * 4];
        Assert.Throws<ArgumentException>(() => BgraDownscaler.AreaAverage(bgra, srcWidth, 4, srcWidth * 4, dstWidth, 2));
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(5, 4)]
    public void AreaAverage_DstHeightOutOfRange_Throws(int dstHeight, int srcHeight)
    {
        var bgra = new byte[4 * 4 * srcHeight];
        Assert.Throws<ArgumentException>(() => BgraDownscaler.AreaAverage(bgra, 4, srcHeight, 4 * 4, 2, dstHeight));
    }
}
