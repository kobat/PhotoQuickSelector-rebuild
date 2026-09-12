using System.Threading;
using PhotoQuickSelector.Core;
using Xunit;

namespace PhotoQuickSelector.Core.Tests;

/// <summary>
/// <see cref="SharpnessAnalyzer"/> の検証。実画像に依存せず、合成画像（エッジ／乱数テクスチャ／
/// チェッカー）で「ボケるほど下がる」「異方性の符号」「タイル位置特定」等の関係性を確認する。
/// </summary>
public class SharpnessAnalyzerTests
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
                buf[idx] = v;     // B
                buf[idx + 1] = v; // G
                buf[idx + 2] = v; // R
                buf[idx + 3] = 255;
            }
        }
        return buf;
    }

    /// <summary>double 平面から BGRA8 バッファを作る（0..255 にクランプして丸める）。</summary>
    private static byte[] MakeBgraFromPlane(int w, int h, double[,] plane)
    {
        return MakeBgra(w, h, (x, y) => (byte)Math.Clamp(Math.Round(plane[y, x]), 0, 255));
    }

    /// <summary>生成関数を double 平面へ展開する。</summary>
    private static double[,] ToPlane(int w, int h, Func<int, int, double> f)
    {
        var plane = new double[h, w];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                plane[y, x] = f(x, y);
        return plane;
    }

    /// <summary>
    /// 分離型ボックスブラー（水平パス→垂直パス、境界は端値複製）を n 回繰り返す。
    /// radius&lt;=0 は無変化。水平のみ／垂直のみを試したい呼び出し元は
    /// <see cref="BoxBlurHorizontal"/>／<see cref="BoxBlurVertical"/> を使う。
    /// </summary>
    private static double[,] BoxBlur(double[,] src, int w, int h, int radius, int repeats = 1)
    {
        var plane = src;
        for (int i = 0; i < repeats; i++)
            plane = BoxBlurVertical(BoxBlurHorizontal(plane, w, h, radius), w, h, radius);
        return plane;
    }

    private static double[,] BoxBlurHorizontal(double[,] src, int w, int h, int radius)
    {
        if (radius <= 0) return (double[,])src.Clone();
        var dst = new double[h, w];
        for (int y = 0; y < h; y++)
        {
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
        }
        return dst;
    }

    private static double[,] BoxBlurVertical(double[,] src, int w, int h, int radius)
    {
        if (radius <= 0) return (double[,])src.Clone();
        var dst = new double[h, w];
        for (int y = 0; y < h; y++)
        {
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
        }
        return dst;
    }

    // ------------------------------------------------------------------
    // 1. ボケるほどスコアが単調に下がる（垂直エッジを水平方向にボックスブラー）。
    // ------------------------------------------------------------------
    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public void BlurMonotonicity_ScoreStrictlyDecreasesAsRadiusIncreases(int threshold)
    {
        const int w = 128, h = 96;
        var edge = ToPlane(w, h, (x, _) => x < w / 2 ? 40.0 : 220.0);
        var options = new SharpnessOptions { Threshold = threshold };

        double Score(int radius)
        {
            var blurred = radius == 0 ? edge : BoxBlurHorizontal(edge, w, h, radius);
            var bgra = MakeBgraFromPlane(w, h, blurred);
            return SharpnessAnalyzer.Analyze(bgra, w, h, w * 4, options, null).Global;
        }

        double s0 = Score(0), s1 = Score(1), s2 = Score(2), s4 = Score(4);

        Assert.True(s0 > s1, $"{s0} > {s1}");
        Assert.True(s1 > s2, $"{s1} > {s2}");
        Assert.True(s2 > s4, $"{s2} > {s4}");
    }

    // ------------------------------------------------------------------
    // 2. 一様画像 → Global=0、Anisotropy=NaN、AF 窓無しなら AfWindow=NaN。
    // ------------------------------------------------------------------
    [Fact]
    public void UniformImage_GlobalZeroAndAnisotropyNaN()
    {
        var bgra = MakeBgra(32, 32, (_, _) => 128);
        var score = SharpnessAnalyzer.Analyze(bgra, 32, 32, 32 * 4, new SharpnessOptions(), null);

        Assert.Equal(0, score.Global);
        Assert.True(double.IsNaN(score.Anisotropy));
        Assert.True(double.IsNaN(score.AfWindow));
        Assert.True(double.IsNaN(score.AfWindowAnisotropy));
    }

    // ------------------------------------------------------------------
    // 3. ノイズ閾値。振幅は「Sobel 最大応答²でも閾値 32²=1024 を必ず下回る」よう ±2 に抑え、
    //    確率的にではなく決定論的に Threshold=32→Global==0 を保証する
    //    （振幅の片道 D=4 として Gx/Gy 最大 4D=16 → Gx²+Gy² 最大 512 < 1024）。
    // ------------------------------------------------------------------
    [Fact]
    public void NoiseBelowThreshold_IsExcludedButVisibleWithoutThreshold()
    {
        const int w = 64, h = 64;
        var rnd = new Random(12345);
        var bgra = MakeBgra(w, h, (_, _) => (byte)(128 + rnd.Next(-2, 3)));

        var withThreshold = SharpnessAnalyzer.Analyze(bgra, w, h, w * 4, new SharpnessOptions { Threshold = 32 }, null);
        var noThreshold = SharpnessAnalyzer.Analyze(bgra, w, h, w * 4, new SharpnessOptions { Threshold = 0 }, null);

        Assert.Equal(0, withThreshold.Global);
        Assert.True(noThreshold.Global > 0);
    }

    // ------------------------------------------------------------------
    // 4. 異方性：乱数テクスチャを片方向だけボックスブラーすると比が偏る。
    // ------------------------------------------------------------------
    [Fact]
    public void Anisotropy_ReflectsDirectionalBlur()
    {
        const int w = 96, h = 96;
        var rnd = new Random(777);
        var noise = ToPlane(w, h, (_, _) => rnd.Next(0, 256));
        var options = new SharpnessOptions { Threshold = 0 };

        double Aniso(double[,] plane)
        {
            var bgra = MakeBgraFromPlane(w, h, plane);
            return SharpnessAnalyzer.Analyze(bgra, w, h, w * 4, options, null).Anisotropy;
        }

        double unblurred = Aniso(noise);
        double blurredH = Aniso(BoxBlurHorizontal(noise, w, h, 3));
        double blurredV = Aniso(BoxBlurVertical(noise, w, h, 3));

        Assert.InRange(unblurred, 0.7, 1.4);
        Assert.True(blurredH < 0.5, $"blurredH={blurredH}");
        Assert.True(blurredV > 2.0, $"blurredV={blurredV}");
    }

    // ------------------------------------------------------------------
    // 5. タイル位置特定：平坦な画像の中に 1 タイル分だけチェッカーを埋め込む。
    // ------------------------------------------------------------------
    private static byte[] MakeCheckerTileImage(int w, int h, int checkerX, int checkerY, int tileSize)
    {
        return MakeBgra(w, h, (x, y) =>
        {
            bool inChecker = x >= checkerX && x < checkerX + tileSize && y >= checkerY && y < checkerY + tileSize;
            if (!inChecker) return 128;
            return ((x + y) % 2 == 0) ? (byte)0 : (byte)255;
        });
    }

    [Fact]
    public void TileLocalization_FindsHighContrastTile()
    {
        const int w = 256, h = 256, tileSize = 64;
        var bgra = MakeCheckerTileImage(w, h, 128, 64, tileSize);
        var options = new SharpnessOptions { TileSize = tileSize, Threshold = 0 };

        var score = SharpnessAnalyzer.Analyze(bgra, w, h, w * 4, options, null);

        Assert.Equal(128, score.MaxTileX);
        Assert.Equal(64, score.MaxTileY);
        Assert.True(score.MaxTile > score.Global, $"MaxTile={score.MaxTile} Global={score.Global}");
    }

    // ------------------------------------------------------------------
    // 6. AF 窓：タイルと同一矩形なら一致・全体が画像外なら NaN・一部はみ出しは例外にならない。
    // ------------------------------------------------------------------
    [Fact]
    public void AfWindow_MatchesTileWhenSameRect_NaNWhenOutside_ClipsWithoutThrow()
    {
        const int w = 256, h = 256, tileSize = 64;
        var bgra = MakeCheckerTileImage(w, h, 128, 64, tileSize);
        var options = new SharpnessOptions { TileSize = tileSize, Threshold = 0 };

        var withoutWindow = SharpnessAnalyzer.Analyze(bgra, w, h, w * 4, options, null);
        var sameRect = SharpnessAnalyzer.Analyze(bgra, w, h, w * 4, options, new RectI(128, 64, 64, 64));
        Assert.Equal(withoutWindow.MaxTile, sameRect.AfWindow, 6);

        var outside = SharpnessAnalyzer.Analyze(bgra, w, h, w * 4, options, new RectI(1000, 1000, 64, 64));
        Assert.True(double.IsNaN(outside.AfWindow));

        var partiallyOutside = SharpnessAnalyzer.Analyze(bgra, w, h, w * 4, options, new RectI(-20, -20, 64, 64));
        Assert.False(double.IsNaN(partiallyOutside.AfWindow)); // 画像内に重なりが残るのでクリップされ NaN にはならない
    }

    // ------------------------------------------------------------------
    // 7. stride：パディングが混入していても結果は詰め込み版と完全一致する。
    // ------------------------------------------------------------------
    [Fact]
    public void Stride_PaddingBytesDoNotAffectResult()
    {
        const int w = 40, h = 30;
        var rnd = new Random(99);
        var packed = MakeBgra(w, h, (_, _) => (byte)rnd.Next(0, 256));

        const int extraPad = 16;
        int paddedStride = w * 4 + extraPad;
        var padded = new byte[paddedStride * h];
        var garbage = new Random(1);
        garbage.NextBytes(padded); // パディング部分にゴミを詰めておく
        for (int y = 0; y < h; y++)
            Array.Copy(packed, y * w * 4, padded, y * paddedStride, w * 4);

        var options = new SharpnessOptions();
        var scorePacked = SharpnessAnalyzer.Analyze(packed, w, h, w * 4, options, new RectI(5, 5, 10, 10));
        var scorePadded = SharpnessAnalyzer.Analyze(padded, w, h, paddedStride, options, new RectI(5, 5, 10, 10));

        Assert.Equal(scorePacked, scorePadded);
    }

    // ------------------------------------------------------------------
    // 8. 極小画像・不正 stride。
    // ------------------------------------------------------------------
    [Fact]
    public void TinyImage_ReturnsZeroWithoutThrowing()
    {
        var bgra = MakeBgra(2, 2, (_, _) => 200);
        var score = SharpnessAnalyzer.Analyze(bgra, 2, 2, 2 * 4, new SharpnessOptions(), null);

        Assert.Equal(0, score.Global);
        Assert.Equal(0, score.MaxTile);
        Assert.Equal(0, score.MaxTileX);
        Assert.Equal(0, score.MaxTileY);
        Assert.True(double.IsNaN(score.Anisotropy));
    }

    [Fact]
    public void WrongStride_ThrowsArgumentException()
    {
        var bgra = MakeBgra(10, 10, (_, _) => 100);
        Assert.Throws<ArgumentException>(() =>
            SharpnessAnalyzer.Analyze(bgra, 10, 10, 10 * 4 - 1, new SharpnessOptions(), null));
    }

    // ------------------------------------------------------------------
    // 9. AfWindowFor：Orientation ごとの中心写像とサイズのクランプ／既定値。
    // ------------------------------------------------------------------
    private static ImageMetadata MakeMeta(int orientation, int originalWidth, int originalHeight,
        PointI? focusPoint, SizeI? focusSize, SizeI? focusReferenceSize) => new()
    {
        Path = "test.jpg",
        FileName = "test.jpg",
        DirectoryName = ".",
        OriginalWidth = originalWidth,
        OriginalHeight = originalHeight,
        Orientation = orientation,
        FocusPoint = focusPoint,
        FocusSize = focusSize,
        FocusReferenceSize = focusReferenceSize,
    };

    [Fact]
    public void AfWindowFor_Orientation1_MapsCenterDirectly()
    {
        var meta = MakeMeta(1, 1000, 600, new PointI(100, 50), null, new SizeI(1000, 600));
        var rect = SharpnessAnalyzer.AfWindowFor(meta, 1000, 600);

        Assert.NotNull(rect);
        Assert.Equal(100 - 256, rect!.Value.X);
        Assert.Equal(50 - 256, rect.Value.Y);
        Assert.Equal(512, rect.Value.Width);
        Assert.Equal(512, rect.Value.Height);
    }

    [Fact]
    public void AfWindowFor_Orientation6_RotatesPointAndDims()
    {
        // 6 = 90度CW。(h-y, x) = (600-50, 100) = (550, 100)。表示寸法は縦横入替の 600x1000。
        var meta = MakeMeta(6, 1000, 600, new PointI(100, 50), null, new SizeI(1000, 600));
        var rect = SharpnessAnalyzer.AfWindowFor(meta, 600, 1000);

        Assert.NotNull(rect);
        Assert.Equal(550 - 256, rect!.Value.X);
        Assert.Equal(100 - 256, rect.Value.Y);
    }

    [Fact]
    public void AfWindowFor_Orientation8_RotatesPointAndDims()
    {
        // 8 = 270度CW。(y, w-x) = (50, 1000-100) = (50, 900)。
        var meta = MakeMeta(8, 1000, 600, new PointI(100, 50), null, new SizeI(1000, 600));
        var rect = SharpnessAnalyzer.AfWindowFor(meta, 600, 1000);

        Assert.NotNull(rect);
        Assert.Equal(50 - 256, rect!.Value.X);
        Assert.Equal(900 - 256, rect.Value.Y);
    }

    [Fact]
    public void AfWindowFor_ClampsSizeToMinAndMax()
    {
        var small = MakeMeta(1, 1000, 600, new PointI(500, 300), new SizeI(10, 10), new SizeI(1000, 600));
        var rectSmall = SharpnessAnalyzer.AfWindowFor(small, 1000, 600, minSize: 256, maxSize: 1024);
        Assert.Equal(256, rectSmall!.Value.Width);

        var large = MakeMeta(1, 1000, 600, new PointI(500, 300), new SizeI(2000, 50), new SizeI(1000, 600));
        var rectLarge = SharpnessAnalyzer.AfWindowFor(large, 1000, 600, minSize: 256, maxSize: 1024);
        Assert.Equal(1024, rectLarge!.Value.Width);
    }

    [Fact]
    public void AfWindowFor_UsesDefaultSizeWhenFocusSizeMissing()
    {
        var meta = MakeMeta(1, 1000, 600, new PointI(500, 300), null, new SizeI(1000, 600));
        var rect = SharpnessAnalyzer.AfWindowFor(meta, 1000, 600, defaultSize: 384);
        Assert.Equal(384, rect!.Value.Width);
        Assert.Equal(384, rect.Value.Height);
    }

    [Fact]
    public void AfWindowFor_NullWhenNoFocusPoint()
    {
        var meta = MakeMeta(1, 1000, 600, null, null, null);
        Assert.Null(SharpnessAnalyzer.AfWindowFor(meta, 1000, 600));
    }

    // ------------------------------------------------------------------
    // 10. 決定性：並列化していても同じ入力からは毎回ビット同一の結果を返す。
    // ------------------------------------------------------------------
    [Fact]
    public void Analyze_IsDeterministicAcrossRepeatedRuns()
    {
        const int w = 1000, h = 700;
        var rnd = new Random(2024);
        var bgra = MakeBgra(w, h, (_, _) => (byte)rnd.Next(0, 256));
        var options = new SharpnessOptions();
        var window = new RectI(100, 100, 300, 300);

        var first = SharpnessAnalyzer.Analyze(bgra, w, h, w * 4, options, window);
        var second = SharpnessAnalyzer.Analyze(bgra, w, h, w * 4, options, window);

        Assert.Equal(first, second);
    }

    // ------------------------------------------------------------------
    // 11. キャンセル：事前キャンセル済みトークンを渡すと並列走査の途中で OperationCanceledException を投げる
    //     （呼び出し側で握りつぶさず伝播させる規約）。
    // ------------------------------------------------------------------
    [Fact]
    public void Analyze_PreCancelledToken_ThrowsOperationCanceledException()
    {
        const int w = 512, h = 512;
        var rnd = new Random(2024);
        var bgra = MakeBgra(w, h, (_, _) => (byte)rnd.Next(0, 256));
        var options = new SharpnessOptions();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            SharpnessAnalyzer.Analyze(bgra, w, h, w * 4, options, null, cts.Token));
    }
}
