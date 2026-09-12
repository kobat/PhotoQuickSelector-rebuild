using System.Buffers;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoQuickSelector.Core;

/// <summary>
/// <see cref="SharpnessMetrics.Compute"/> が計算できる鮮鋭度指標の種類。
/// いずれも <see cref="SharpnessAnalyzer"/>（Tenengrad）とは別系統の実装で、比較検証用
/// （tools/sharpness/SharpnessBench）に追加したもの。
/// </summary>
public enum SharpnessMetric
{
    /// <summary>ラプラシアン分散（4近傍ラプラシアンの分散。値が大きいほど鮮鋭）。</summary>
    LaplacianVariance,

    /// <summary>Brenner勾配（2px離れた画素との差の二乗和。値が大きいほど鮮鋭）。</summary>
    Brenner,

    /// <summary>Crete-Roffet の no-reference ブラー推定（0..1、値が大きいほど鮮鋭）。</summary>
    Reblur,

    /// <summary>Marziliano 式のエッジ幅推定（px。値が小さいほど鮮鋭＝<see cref="MetricScores.HigherIsSharper"/>=false）。</summary>
    EdgeWidth,
}

/// <summary>
/// 1 手法ぶんの領域別スコア。<see cref="HigherIsSharper"/>=false の手法（<see cref="SharpnessMetric.EdgeWidth"/>）は
/// 値が小さいほど鮮鋭であることに注意（他の 3 手法とは大小の向きが逆）。
/// </summary>
/// <param name="Global">画像全体（評価対象画素のみ）の集約値。評価対象画素が無ければ手法ごとの既定値
/// （分散・平均系は 0、比率系は <see cref="double.NaN"/>）。</param>
/// <param name="AfWindow">AF 窓内の同集約値。窓が無い、またはクリップ後に評価画素が 0 なら NaN。</param>
/// <param name="MaxTile">「最良タイル」のスコア（<see cref="HigherIsSharper"/>=true なら最大値、false なら
/// 条件を満たすタイル中の最小値）。条件を満たすタイルが無ければ <see cref="Global"/> と同じ値。</param>
/// <param name="MaxTileX">最良タイルの左上 X（px）。タイルが無ければ 0。</param>
/// <param name="MaxTileY">最良タイルの左上 Y（px）。タイルが無ければ 0。</param>
/// <param name="HigherIsSharper">true なら値が大きいほど鮮鋭、false なら小さいほど鮮鋭。</param>
public readonly record struct MetricScores(
    double Global, double AfWindow, double MaxTile, int MaxTileX, int MaxTileY, bool HigherIsSharper);

/// <summary>
/// Tenengrad（<see cref="SharpnessAnalyzer"/>）と比較するための 4 手法の鮮鋭度指標。
/// UI 非依存の純粋計算のみ（デコード済み BGRA8 バッファを受け取るだけ）。
/// tools/sharpness/SharpnessBench から比較用途で呼ばれるほか、App 本体でも「全手法」表示モード
/// （<c>SharpnessMode.All</c>）で焦点写真のみ計算し、画像情報パネル／ルーペオーバーレイに表示する。
/// </summary>
public static class SharpnessMetrics
{
    /// <summary>各手法共通の「これ未満は評価対象画素が事実上 0 になる」下限（一辺 px）。</summary>
    private const int MinDimension = 5;

    /// <summary>Marziliano 式エッジ幅の片側ウォーク上限（px）。値の暴走・処理時間の上限を兼ねる。</summary>
    private const int MaxWalkSteps = 64;

    /// <summary>タイルが「最良タイル」候補となるために必要な最小エッジ数（<see cref="SharpnessMetric.EdgeWidth"/> 専用）。
    /// ボケたタイルはエッジ自体が少なくノイズになるため、少数エッジのタイルは除外する。</summary>
    private const long MinEdgesPerTile = 100;

    /// <summary>Reblur の box blur 半径（1x9／9x1 タップ＝半径4。Crete et al. 2007 の既定に準拠）。</summary>
    private const int ReblurBlurRadius = 4;

    /// <summary>
    /// BGRA8 バッファを指定手法で解析する。<see cref="SharpnessAnalyzer.Analyze"/> と入力規約は共通
    /// （stride ≥ width*4、alpha は輝度計算に使わない）。
    /// </summary>
    /// <param name="metric">計算する手法。</param>
    /// <param name="bgra">1 画素 4 byte（B,G,R,A の順）のバッファ。</param>
    /// <param name="width">画像幅 px。5 未満は手法ごとの既定値（一様画像相当）を例外なく返す。</param>
    /// <param name="height">画像高さ px。5 未満は同上。</param>
    /// <param name="stride">1 行のバイト数。width*4 以上が必須。</param>
    /// <param name="tileSize">タイル一辺 px。(0,0) 起点で画像に完全に収まるタイルのみ評価する。</param>
    /// <param name="afWindow">AF 窓（表示空間 px の矩形）。画像外へのはみ出しはこのメソッド内でクリップする。null なら AF 系は NaN。</param>
    /// <param name="threshold">
    /// <see cref="SharpnessMetric.EdgeWidth"/> のみで使う Sobel 応答（|Gx| または |Gy|）の下限。既定 32。
    /// 他手法では未使用（引数互換のため共通シグネチャに含めている）。
    /// </param>
    /// <param name="cancellationToken">
    /// 解析中断用。並列パス（<see cref="Parallel"/> の <c>ParallelOptions</c>）へ渡すほか、
    /// <see cref="SharpnessMetric.EdgeWidth"/> の逐次ウォーク（<see cref="AccumulateVerticalEdges"/>／
    /// <see cref="AccumulateHorizontalEdges"/>）では 64 行/列ごとにポーリングする。要求されていれば
    /// <see cref="OperationCanceledException"/> をそのまま伝播させる（呼び出し側で握りつぶさない）。
    /// 既定は無効（<see cref="CancellationToken.None"/>）。
    /// </param>
    /// <exception cref="ArgumentException">width/height が負・stride が width*4 未満・bgra が短すぎる場合。</exception>
    public static MetricScores Compute(
        SharpnessMetric metric, ReadOnlySpan<byte> bgra, int width, int height, int stride,
        int tileSize, RectI? afWindow, int threshold = 32, CancellationToken cancellationToken = default)
    {
        if (width < 0) throw new ArgumentException("width は 0 以上である必要がある。", nameof(width));
        if (height < 0) throw new ArgumentException("height は 0 以上である必要がある。", nameof(height));
        if (stride < width * 4) throw new ArgumentException("stride は width*4 以上である必要がある。", nameof(stride));
        if (bgra.Length < (long)stride * height)
            throw new ArgumentException("bgra の長さが stride*height に満たない。", nameof(bgra));

        bool higherIsSharper = metric != SharpnessMetric.EdgeWidth;

        // 各手法とも 3x3〜5x5 近傍を要する（EdgeWidth の局所極大判定が最も広い）。5x5 未満では
        // 評価対象画素が事実上 0 になる異常系なので、例外にせず一様画像と同じ既定値で返す
        // （分散・平均系＝0、比率系＝NaN。デコード失敗等の呼び出し元がそのまま渡せるように）。
        if (width < MinDimension || height < MinDimension)
        {
            double zero = metric is SharpnessMetric.Reblur or SharpnessMetric.EdgeWidth ? double.NaN : 0.0;
            return new MetricScores(zero, double.NaN, zero, 0, 0, higherIsSharper);
        }

        byte[] yPlane = ArrayPool<byte>.Shared.Rent(width * height);
        try
        {
            SharpnessAnalyzer.BuildLuminancePlane(bgra, width, height, stride, yPlane);
            return metric switch
            {
                SharpnessMetric.LaplacianVariance => ComputeLaplacianVariance(yPlane, width, height, tileSize, afWindow, cancellationToken),
                SharpnessMetric.Brenner => ComputeBrenner(yPlane, width, height, tileSize, afWindow, cancellationToken),
                SharpnessMetric.Reblur => ComputeReblur(yPlane, width, height, tileSize, afWindow, cancellationToken),
                SharpnessMetric.EdgeWidth => ComputeEdgeWidth(yPlane, width, height, tileSize, afWindow, threshold, cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(metric)),
            };
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(yPlane);
        }
    }

    // ====================================================================
    // 1. LaplacianVariance: L = 4Y(x,y) - 上下左右。分散 = E[L²] - E[L]²。
    // ====================================================================

    private static MetricScores ComputeLaplacianVariance(
        byte[] y, int width, int height, int tileSize, RectI? afWindow, CancellationToken cancellationToken)
    {
        int tilesX = tileSize > 0 ? width / tileSize : 0;
        int tilesY = tileSize > 0 ? height / tileSize : 0;
        int bandRows = tileSize > 0 ? tileSize : height;
        int bandCount = (height + bandRows - 1) / bandRows;
        int tileColLimit = tilesX * tileSize;

        var tileSumL = tilesX > 0 && tilesY > 0 ? new long[tilesX * tilesY] : Array.Empty<long>();
        var tileSumL2 = tilesX > 0 && tilesY > 0 ? new long[tilesX * tilesY] : Array.Empty<long>();
        var tileCount = tilesX > 0 && tilesY > 0 ? new long[tilesX * tilesY] : Array.Empty<long>();
        long globalSumL = 0, globalSumL2 = 0, globalCount = 0;
        var reduceLock = new object();
        var parallelOptions = new ParallelOptions { CancellationToken = cancellationToken };

        // 内部画素のみ評価（境界1px はラプラシアンの4近傍が組めないため除外＝Sobel と同じ規約）。
        Parallel.For(
            0, bandCount,
            parallelOptions,
            () => new SumAccumulator3(tilesX, tilesY),
            (band, _, local) =>
            {
                int rowStart = Math.Max(1, band * bandRows);
                int rowEndExclusive = Math.Min(height - 1, (band + 1) * bandRows);
                bool fullTileBand = tileSize > 0 && band < tilesY;
                int tileRow = band;
                for (int row = rowStart; row < rowEndExclusive; row++)
                {
                    int rowBase = row * width;
                    for (int x = 1; x < width - 1; x++)
                    {
                        int idx = rowBase + x;
                        long l = 4L * y[idx] - y[idx - 1] - y[idx + 1] - y[idx - width] - y[idx + width];
                        long l2 = l * l;

                        local.GlobalA++;
                        local.GlobalB += l;
                        local.GlobalC += l2;

                        if (fullTileBand && x < tileColLimit)
                        {
                            int tileIdx = tileRow * tilesX + x / tileSize;
                            local.TileA![tileIdx]++;
                            local.TileB![tileIdx] += l;
                            local.TileC![tileIdx] += l2;
                        }
                    }
                }
                return local;
            },
            local =>
            {
                lock (reduceLock)
                {
                    globalCount += local.GlobalA;
                    globalSumL += local.GlobalB;
                    globalSumL2 += local.GlobalC;
                    MergeTiles(local, tileCount, tileSumL, tileSumL2);
                }
            });

        double global = Variance(globalCount, globalSumL, globalSumL2);

        FindBestTile(
            tilesX, tilesY, tileSize, higherWins: true,
            qualifies: idx => tileCount[idx] > 0,
            score: idx => Variance(tileCount[idx], tileSumL[idx], tileSumL2[idx]),
            fallback: global,
            out double maxTileScore, out int maxTileX, out int maxTileY);

        double af = double.NaN;
        if (afWindow is { } rect)
        {
            int x0 = Math.Max(1, rect.X), y0 = Math.Max(1, rect.Y);
            int x1 = Math.Min(width - 2, rect.X + rect.Width - 1);
            int y1 = Math.Min(height - 2, rect.Y + rect.Height - 1);
            if (x1 >= x0 && y1 >= y0)
            {
                long sumL = 0, sumL2 = 0, count = 0;
                for (int yy = y0; yy <= y1; yy++)
                {
                    int rowBase = yy * width;
                    for (int xx = x0; xx <= x1; xx++)
                    {
                        int idx = rowBase + xx;
                        long l = 4L * y[idx] - y[idx - 1] - y[idx + 1] - y[idx - width] - y[idx + width];
                        sumL += l;
                        sumL2 += l * l;
                        count++;
                    }
                }
                af = Variance(count, sumL, sumL2);
            }
        }

        return new MetricScores(global, af, maxTileScore, maxTileX, maxTileY, true);
    }

    /// <summary>E[L²]-E[L]² を丸め誤差で負にならないようクランプして返す（count=0 は 0）。</summary>
    private static double Variance(long count, long sumL, long sumL2)
    {
        if (count == 0) return 0;
        double mean = (double)sumL / count;
        double meanSq = (double)sumL2 / count;
        return Math.Max(0, meanSq - mean * mean);
    }

    // ====================================================================
    // 2. Brenner: (Y(x+2,y)-Y(x,y))² + (Y(x,y+2)-Y(x,y))² の平均（両方向で方向非依存）。
    // ====================================================================

    private static MetricScores ComputeBrenner(
        byte[] y, int width, int height, int tileSize, RectI? afWindow, CancellationToken cancellationToken)
    {
        int tilesX = tileSize > 0 ? width / tileSize : 0;
        int tilesY = tileSize > 0 ? height / tileSize : 0;
        int bandRows = tileSize > 0 ? tileSize : height;
        int bandCount = (height + bandRows - 1) / bandRows;
        int tileColLimit = tilesX * tileSize;

        var tileSum = tilesX > 0 && tilesY > 0 ? new long[tilesX * tilesY] : Array.Empty<long>();
        var tileCount = tilesX > 0 && tilesY > 0 ? new long[tilesX * tilesY] : Array.Empty<long>();
        long globalSum = 0, globalCount = 0;
        var reduceLock = new object();
        var parallelOptions = new ParallelOptions { CancellationToken = cancellationToken };

        // 評価範囲は x∈[0,w-3]・y∈[0,h-3]（x+2,y+2 が画像内に収まる範囲）。
        Parallel.For(
            0, bandCount,
            parallelOptions,
            () => new SumAccumulator1(tilesX, tilesY),
            (band, _, local) =>
            {
                int rowStart = band * bandRows;
                int rowEndExclusive = Math.Min(height - 2, (band + 1) * bandRows);
                bool fullTileBand = tileSize > 0 && band < tilesY;
                int tileRow = band;
                for (int row = rowStart; row < rowEndExclusive; row++)
                {
                    int rowBase = row * width;
                    int rowBaseDown2 = (row + 2) * width;
                    for (int x = 0; x <= width - 3; x++)
                    {
                        int idx = rowBase + x;
                        int center = y[idx];
                        int dx = y[idx + 2] - center;
                        int dy = y[rowBaseDown2 + x] - center;
                        long val = (long)dx * dx + (long)dy * dy;

                        local.GlobalA++;
                        local.GlobalB += val;

                        if (fullTileBand && x < tileColLimit)
                        {
                            int tileIdx = tileRow * tilesX + x / tileSize;
                            local.TileA![tileIdx]++;
                            local.TileB![tileIdx] += val;
                        }
                    }
                }
                return local;
            },
            local =>
            {
                lock (reduceLock)
                {
                    globalCount += local.GlobalA;
                    globalSum += local.GlobalB;
                    if (local.TileA != null)
                    {
                        for (int i = 0; i < local.TileA.Length; i++)
                        {
                            tileCount[i] += local.TileA[i];
                            tileSum[i] += local.TileB![i];
                        }
                    }
                }
            });

        double global = globalCount > 0 ? (double)globalSum / globalCount : 0;

        FindBestTile(
            tilesX, tilesY, tileSize, higherWins: true,
            qualifies: idx => tileCount[idx] > 0,
            score: idx => (double)tileSum[idx] / tileCount[idx],
            fallback: global,
            out double maxTileScore, out int maxTileX, out int maxTileY);

        double af = double.NaN;
        if (afWindow is { } rect)
        {
            int x0 = Math.Max(0, rect.X), y0 = Math.Max(0, rect.Y);
            int x1 = Math.Min(width - 3, rect.X + rect.Width - 1);
            int y1 = Math.Min(height - 3, rect.Y + rect.Height - 1);
            if (x1 >= x0 && y1 >= y0)
            {
                long sum = 0, count = 0;
                for (int yy = y0; yy <= y1; yy++)
                {
                    int rowBase = yy * width;
                    int rowBaseDown2 = (yy + 2) * width;
                    for (int xx = x0; xx <= x1; xx++)
                    {
                        int idx = rowBase + xx;
                        int center = y[idx];
                        int dx = y[idx + 2] - center;
                        int dy = y[rowBaseDown2 + xx] - center;
                        sum += (long)dx * dx + (long)dy * dy;
                        count++;
                    }
                }
                af = count > 0 ? (double)sum / count : double.NaN;
            }
        }

        return new MetricScores(global, af, maxTileScore, maxTileX, maxTileY, true);
    }

    // ====================================================================
    // 3. Reblur（Crete et al. 2007）: 水平/垂直の box blur との勾配減衰比から no-reference ブラー度を推定。
    // ====================================================================

    private static MetricScores ComputeReblur(
        byte[] y, int width, int height, int tileSize, RectI? afWindow, CancellationToken cancellationToken)
    {
        byte[] bh = ArrayPool<byte>.Shared.Rent(width * height); // 水平 1x9 box blur
        byte[] bv = ArrayPool<byte>.Shared.Rent(width * height); // 垂直 9x1 box blur
        try
        {
            BuildBoxBlurHorizontal(y, width, height, ReblurBlurRadius, bh, cancellationToken);
            BuildBoxBlurVertical(y, width, height, ReblurBlurRadius, bv, cancellationToken);

            int tilesX = tileSize > 0 ? width / tileSize : 0;
            int tilesY = tileSize > 0 ? height / tileSize : 0;
            int bandRows = tileSize > 0 ? tileSize : height;
            int bandCount = (height + bandRows - 1) / bandRows;
            int tileColLimit = tilesX * tileSize;

            var tileSFv = tilesX > 0 && tilesY > 0 ? new long[tilesX * tilesY] : Array.Empty<long>();
            var tileSVv = tilesX > 0 && tilesY > 0 ? new long[tilesX * tilesY] : Array.Empty<long>();
            var tileSFh = tilesX > 0 && tilesY > 0 ? new long[tilesX * tilesY] : Array.Empty<long>();
            var tileSVh = tilesX > 0 && tilesY > 0 ? new long[tilesX * tilesY] : Array.Empty<long>();
            long globalSFv = 0, globalSVv = 0, globalSFh = 0, globalSVh = 0;
            var reduceLock = new object();
            var parallelOptions = new ParallelOptions { CancellationToken = cancellationToken };

            // 垂直方向（行差分・Bv と比較）は row>=1、水平方向（列差分・Bh と比較）は x>=1 でのみ加算。
            // どちらも「評価対象画素」の範囲がタイル/バンド境界と無関係な条件（絶対座標基準）なので、
            // AF 窓側の再計算でも同じ条件を使えば厳密に一致する。
            Parallel.For(
                0, bandCount,
                parallelOptions,
                () => new SumAccumulator4(tilesX, tilesY),
                (band, _, local) =>
                {
                    int rowStart = band * bandRows;
                    int rowEndExclusive = Math.Min(height, (band + 1) * bandRows);
                    bool fullTileBand = tileSize > 0 && band < tilesY;
                    int tileRow = band;
                    for (int row = rowStart; row < rowEndExclusive; row++)
                    {
                        int rowBase = row * width;
                        for (int x = 0; x < width; x++)
                        {
                            int idx = rowBase + x;
                            int tileIdx = fullTileBand && x < tileColLimit ? tileRow * tilesX + x / tileSize : -1;

                            if (row >= 1)
                            {
                                int dF = Math.Abs(y[idx] - y[idx - width]);
                                int dB = Math.Abs(bv[idx] - bv[idx - width]);
                                int v = Math.Max(0, dF - dB);
                                local.GlobalA += dF;
                                local.GlobalB += v;
                                if (tileIdx >= 0) { local.TileA![tileIdx] += dF; local.TileB![tileIdx] += v; }
                            }
                            if (x >= 1)
                            {
                                int dF = Math.Abs(y[idx] - y[idx - 1]);
                                int dB = Math.Abs(bh[idx] - bh[idx - 1]);
                                int v = Math.Max(0, dF - dB);
                                local.GlobalC += dF;
                                local.GlobalD += v;
                                if (tileIdx >= 0) { local.TileC![tileIdx] += dF; local.TileD![tileIdx] += v; }
                            }
                        }
                    }
                    return local;
                },
                local =>
                {
                    lock (reduceLock)
                    {
                        globalSFv += local.GlobalA; globalSVv += local.GlobalB;
                        globalSFh += local.GlobalC; globalSVh += local.GlobalD;
                        if (local.TileA != null)
                        {
                            for (int i = 0; i < local.TileA.Length; i++)
                            {
                                tileSFv[i] += local.TileA[i]; tileSVv[i] += local.TileB![i];
                                tileSFh[i] += local.TileC![i]; tileSVh[i] += local.TileD![i];
                            }
                        }
                    }
                });

            double global = ReblurScore(globalSFv, globalSVv, globalSFh, globalSVh);

            FindBestTile(
                tilesX, tilesY, tileSize, higherWins: true,
                qualifies: idx => !double.IsNaN(ReblurScore(tileSFv[idx], tileSVv[idx], tileSFh[idx], tileSVh[idx])),
                score: idx => ReblurScore(tileSFv[idx], tileSVv[idx], tileSFh[idx], tileSVh[idx]),
                fallback: global,
                out double maxTileScore, out int maxTileX, out int maxTileY);

            double af = double.NaN;
            if (afWindow is { } rect)
            {
                int x0 = Math.Max(0, rect.X), y0 = Math.Max(0, rect.Y);
                int x1 = Math.Min(width - 1, rect.X + rect.Width - 1);
                int y1 = Math.Min(height - 1, rect.Y + rect.Height - 1);
                if (x1 >= x0 && y1 >= y0)
                {
                    long sFv = 0, sVv = 0, sFh = 0, sVh = 0;
                    for (int yy = y0; yy <= y1; yy++)
                    {
                        int rowBase = yy * width;
                        for (int xx = x0; xx <= x1; xx++)
                        {
                            int idx = rowBase + xx;
                            if (yy >= 1)
                            {
                                int dF = Math.Abs(y[idx] - y[idx - width]);
                                int dB = Math.Abs(bv[idx] - bv[idx - width]);
                                sFv += dF; sVv += Math.Max(0, dF - dB);
                            }
                            if (xx >= 1)
                            {
                                int dF = Math.Abs(y[idx] - y[idx - 1]);
                                int dB = Math.Abs(bh[idx] - bh[idx - 1]);
                                sFh += dF; sVh += Math.Max(0, dF - dB);
                            }
                        }
                    }
                    af = ReblurScore(sFv, sVv, sFh, sVh);
                }
            }

            return new MetricScores(global, af, maxTileScore, maxTileX, maxTileY, true);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bh);
            ArrayPool<byte>.Shared.Return(bv);
        }
    }

    /// <summary>
    /// blur_ver=(sFv-sVv)/sFv, blur_hor=(sFh-sVh)/sFh, score=1-max(blur_ver,blur_hor)。
    /// sFv/sFh が 0（当該方向が完全に平坦）なら 0.0/0.0 の double 演算として自然に NaN になる
    /// （0&lt;=sV&lt;=sF が常に成り立つため sF=0 なら sV=0 も保証されており、Infinity にはならない）。
    /// </summary>
    private static double ReblurScore(long sFv, long sVv, long sFh, long sVh)
    {
        double blurVer = (double)(sFv - sVv) / sFv;
        double blurHor = (double)(sFh - sVh) / sFh;
        return 1.0 - Math.Max(blurVer, blurHor);
    }

    /// <summary>水平方向 1x(2r+1) box blur（境界は端値クランプ）。列非依存なので行ごとに並列化できる。</summary>
    private static void BuildBoxBlurHorizontal(
        byte[] y, int width, int height, int radius, byte[] outPlane, CancellationToken cancellationToken)
    {
        int taps = 2 * radius + 1;
        Parallel.For(0, height, new ParallelOptions { CancellationToken = cancellationToken }, row =>
        {
            int rowBase = row * width;
            for (int x = 0; x < width; x++)
            {
                int sum = 0;
                for (int dx = -radius; dx <= radius; dx++)
                    sum += y[rowBase + Math.Clamp(x + dx, 0, width - 1)];
                outPlane[rowBase + x] = (byte)((sum + taps / 2) / taps);
            }
        });
    }

    /// <summary>垂直方向 (2r+1)x1 box blur（境界は端値クランプ）。行非依存なので列ごとに並列化できる。</summary>
    private static void BuildBoxBlurVertical(
        byte[] y, int width, int height, int radius, byte[] outPlane, CancellationToken cancellationToken)
    {
        int taps = 2 * radius + 1;
        Parallel.For(0, width, new ParallelOptions { CancellationToken = cancellationToken }, col =>
        {
            for (int yy = 0; yy < height; yy++)
            {
                int sum = 0;
                for (int dy = -radius; dy <= radius; dy++)
                    sum += y[Math.Clamp(yy + dy, 0, height - 1) * width + col];
                outPlane[yy * width + col] = (byte)((sum + taps / 2) / taps);
            }
        });
    }

    // ====================================================================
    // 4. EdgeWidth（Marziliano et al. 2002）: Sobel の局所極大点からエッジ幅を歩いて測る。
    //    値は小さいほど鮮鋭（HigherIsSharper=false）。
    // ====================================================================

    private static MetricScores ComputeEdgeWidth(
        byte[] y, int width, int height, int tileSize, RectI? afWindow, int threshold,
        CancellationToken cancellationToken)
    {
        int tilesX = tileSize > 0 ? width / tileSize : 0;
        int tilesY = tileSize > 0 ? height / tileSize : 0;
        var tileSum = tilesX > 0 && tilesY > 0 ? new long[tilesX * tilesY] : Array.Empty<long>();
        var tileCount = tilesX > 0 && tilesY > 0 ? new long[tilesX * tilesY] : Array.Empty<long>();
        long globalSum = 0, globalCount = 0;

        // 垂直エッジ（Gx 由来）。左右隣の Gx も要るので列範囲は [2, w-3]。
        int vColMin = 2, vColMax = width - 3, vRowMin = 1, vRowMax = height - 2;
        AccumulateVerticalEdges(y, width, height, threshold, vColMin, vColMax, vRowMin, vRowMax,
            tileSize, tilesX, tilesY, tileSum, tileCount, ref globalSum, ref globalCount, cancellationToken);

        // 水平エッジ（Gy 由来）。上下隣の Gy も要るので行範囲は [2, h-3]。
        int hColMin = 1, hColMax = width - 2, hRowMin = 2, hRowMax = height - 3;
        AccumulateHorizontalEdges(y, width, height, threshold, hColMin, hColMax, hRowMin, hRowMax,
            tileSize, tilesX, tilesY, tileSum, tileCount, ref globalSum, ref globalCount, cancellationToken);

        // 両方向のエッジをプールした平均幅。エッジが1つも無ければ NaN。
        double global = globalCount > 0 ? (double)globalSum / globalCount : double.NaN;

        FindBestTile(
            tilesX, tilesY, tileSize, higherWins: false,
            qualifies: idx => tileCount[idx] >= MinEdgesPerTile,
            score: idx => (double)tileSum[idx] / tileCount[idx],
            fallback: global,
            out double bestScore, out int bestX, out int bestY);

        double af = double.NaN;
        if (afWindow is { } rect)
        {
            long sum = 0, count = 0;
            int vx0 = Math.Max(vColMin, rect.X), vx1 = Math.Min(vColMax, rect.X + rect.Width - 1);
            int vy0 = Math.Max(vRowMin, rect.Y), vy1 = Math.Min(vRowMax, rect.Y + rect.Height - 1);
            AccumulateVerticalEdges(y, width, height, threshold, vx0, vx1, vy0, vy1,
                0, 0, 0, null, null, ref sum, ref count, cancellationToken);

            int hx0 = Math.Max(hColMin, rect.X), hx1 = Math.Min(hColMax, rect.X + rect.Width - 1);
            int hy0 = Math.Max(hRowMin, rect.Y), hy1 = Math.Min(hRowMax, rect.Y + rect.Height - 1);
            AccumulateHorizontalEdges(y, width, height, threshold, hx0, hx1, hy0, hy1,
                0, 0, 0, null, null, ref sum, ref count, cancellationToken);

            af = count > 0 ? (double)sum / count : double.NaN;
        }

        return new MetricScores(global, af, bestScore, bestX, bestY, false);
    }

    /// <summary>
    /// 垂直エッジ（Gx が閾値超かつ左右隣の Gx 以上＝局所極大）を検出し、
    /// 各エッジ画素のエッジ幅（px）を <paramref name="sum"/>/<paramref name="count"/>（グローバルまたは
    /// AF 窓ぶんの部分和）と、タイル配列（<paramref name="tileSize"/>=0 なら不使用）へ加算する。
    /// タイル配列を渡さない呼び出し（AF 窓の単独計算）は tileSize/tilesX/tilesY に 0 を渡せばよい
    /// （タイル判定条件が常に偽になり自然にスキップされる）。
    /// </summary>
    private static void AccumulateVerticalEdges(
        byte[] y, int width, int height, int threshold,
        int colMin, int colMax, int rowMin, int rowMax,
        int tileSize, int tilesX, int tilesY, long[]? tileSum, long[]? tileCount,
        ref long sum, ref long count, CancellationToken cancellationToken)
    {
        if (colMax < colMin || rowMax < rowMin) return;
        int tileColLimit = tilesX * tileSize;
        int tileRowLimit = tilesY * tileSize;

        for (int row = rowMin; row <= rowMax; row++)
        {
            if ((row - rowMin) % 64 == 0) cancellationToken.ThrowIfCancellationRequested();
            for (int col = colMin; col <= colMax; col++)
            {
                int idx = row * width + col;
                int gx = ComputeGx(y, width, idx);
                int absGx = Math.Abs(gx);
                if (absGx < threshold) continue;
                if (absGx < Math.Abs(ComputeGx(y, width, idx - 1))) continue;
                if (absGx < Math.Abs(ComputeGx(y, width, idx + 1))) continue;

                int leftCap = Math.Min(MaxWalkSteps, col);
                int rightCap = Math.Min(MaxWalkSteps, width - 1 - col);
                int leftMoved, rightMoved;
                if (gx > 0)
                {
                    leftMoved = Walk(y, idx, -1, strictlyDecreasing: true, leftCap);
                    rightMoved = Walk(y, idx, 1, strictlyDecreasing: false, rightCap);
                }
                else
                {
                    leftMoved = Walk(y, idx, -1, strictlyDecreasing: false, leftCap);
                    rightMoved = Walk(y, idx, 1, strictlyDecreasing: true, rightCap);
                }
                int edgeWidth = leftMoved + rightMoved;

                sum += edgeWidth;
                count++;
                if (tileSize > 0 && col < tileColLimit && row < tileRowLimit)
                {
                    int tileIdx = row / tileSize * tilesX + col / tileSize;
                    tileSum![tileIdx] += edgeWidth;
                    tileCount![tileIdx]++;
                }
            }
        }
    }

    /// <summary>水平エッジ版（Gy・上下隣・上下ウォーク）。<see cref="AccumulateVerticalEdges"/> の行列入替版。</summary>
    private static void AccumulateHorizontalEdges(
        byte[] y, int width, int height, int threshold,
        int colMin, int colMax, int rowMin, int rowMax,
        int tileSize, int tilesX, int tilesY, long[]? tileSum, long[]? tileCount,
        ref long sum, ref long count, CancellationToken cancellationToken)
    {
        if (colMax < colMin || rowMax < rowMin) return;
        int tileColLimit = tilesX * tileSize;
        int tileRowLimit = tilesY * tileSize;

        for (int row = rowMin; row <= rowMax; row++)
        {
            if ((row - rowMin) % 64 == 0) cancellationToken.ThrowIfCancellationRequested();
            for (int col = colMin; col <= colMax; col++)
            {
                int idx = row * width + col;
                int gy = ComputeGy(y, width, idx);
                int absGy = Math.Abs(gy);
                if (absGy < threshold) continue;
                if (absGy < Math.Abs(ComputeGy(y, width, idx - width))) continue;
                if (absGy < Math.Abs(ComputeGy(y, width, idx + width))) continue;

                int upCap = Math.Min(MaxWalkSteps, row);
                int downCap = Math.Min(MaxWalkSteps, height - 1 - row);
                int upMoved, downMoved;
                if (gy > 0)
                {
                    upMoved = Walk(y, idx, -width, strictlyDecreasing: true, upCap);
                    downMoved = Walk(y, idx, width, strictlyDecreasing: false, downCap);
                }
                else
                {
                    upMoved = Walk(y, idx, -width, strictlyDecreasing: false, upCap);
                    downMoved = Walk(y, idx, width, strictlyDecreasing: true, downCap);
                }
                int edgeWidth = upMoved + downMoved;

                sum += edgeWidth;
                count++;
                if (tileSize > 0 && col < tileColLimit && row < tileRowLimit)
                {
                    int tileIdx = row / tileSize * tilesX + col / tileSize;
                    tileSum![tileIdx] += edgeWidth;
                    tileCount![tileIdx]++;
                }
            }
        }
    }

    /// <summary>3x3 Sobel Gx（<see cref="SharpnessAnalyzer"/> と同じ核）。idx±width±1 が範囲内である前提。</summary>
    private static int ComputeGx(byte[] y, int width, int idx)
    {
        int p00 = y[idx - width - 1], p02 = y[idx - width + 1];
        int p10 = y[idx - 1], p12 = y[idx + 1];
        int p20 = y[idx + width - 1], p22 = y[idx + width + 1];
        return (p02 - p00) + 2 * (p12 - p10) + (p22 - p20);
    }

    /// <summary>3x3 Sobel Gy。idx±width±1 が範囲内である前提。</summary>
    private static int ComputeGy(byte[] y, int width, int idx)
    {
        int p00 = y[idx - width - 1], p01 = y[idx - width], p02 = y[idx - width + 1];
        int p20 = y[idx + width - 1], p21 = y[idx + width], p22 = y[idx + width + 1];
        return (p20 - p00) + 2 * (p21 - p01) + (p22 - p02);
    }

    /// <summary>
    /// エッジ画素 <paramref name="startIdx"/> から <paramref name="step"/> 方向へ、
    /// 「隣接画素の輝度が現在値に対して <paramref name="strictlyDecreasing"/> の向きで狭義単調に動く」間だけ進む
    /// （Marziliano のエッジ幅推定＝谷/山に達するまで歩く）。<paramref name="cap"/> 歩で打ち切り、
    /// 実際に進んだ歩数（px）を返す。
    /// </summary>
    /// <remarks>
    /// 等号を許す（≤/≥）と、輝度が完全に一定な平坦領域へ入った瞬間も条件を満たし続けてしまい、
    /// 平坦部を抜けるまで際限なく歩いて <paramref name="cap"/> に張り付く（実写真でも量子化で
    /// 隣接画素が同値になる平坦部は珍しくない）。狭義不等号にすることで「谷/山＝直前まで単調で
    /// 次の1歩が同値以上（同値含む）になった地点」で正しく止まる。
    /// </remarks>
    private static int Walk(byte[] y, int startIdx, int step, bool strictlyDecreasing, int cap)
    {
        int pos = startIdx;
        int moved = 0;
        while (moved < cap)
        {
            int next = pos + step;
            int cur = y[pos];
            int nxt = y[next];
            bool cont = strictlyDecreasing ? nxt < cur : nxt > cur;
            if (!cont) break;
            pos = next;
            moved++;
        }
        return moved;
    }

    // ====================================================================
    // 共通: タイル探索・バンド累算のヘルパー
    // ====================================================================

    /// <summary>
    /// 全タイルを行優先で走査し、条件を満たすタイル中の最良スコアを探す
    /// （<paramref name="higherWins"/>=true なら最大値、false なら最小値。同点は先に見つかった方＝
    /// 左上優先。厳密な比較演算子のみで更新するため自然に満たす）。
    /// 条件を満たすタイルが1つも無ければ <paramref name="fallback"/>（=Global）を (0,0) として返す。
    /// </summary>
    private static void FindBestTile(
        int tilesX, int tilesY, int tileSize, bool higherWins,
        Func<int, bool> qualifies, Func<int, double> score, double fallback,
        out double bestScore, out int bestX, out int bestY)
    {
        bool found = false;
        bestScore = 0;
        bestX = 0;
        bestY = 0;
        for (int ty = 0; ty < tilesY; ty++)
        {
            for (int tx = 0; tx < tilesX; tx++)
            {
                int idx = ty * tilesX + tx;
                if (!qualifies(idx)) continue;
                double s = score(idx);
                bool better = !found || (higherWins ? s > bestScore : s < bestScore);
                if (better)
                {
                    found = true;
                    bestScore = s;
                    bestX = tx * tileSize;
                    bestY = ty * tileSize;
                }
            }
        }
        if (!found)
        {
            bestScore = fallback;
            bestX = 0;
            bestY = 0;
        }
    }

    /// <summary>バンド内スレッドローカルの合算をタイル配列（グローバル側）へマージする（呼び出し元でロック済み前提）。</summary>
    private static void MergeTiles(SumAccumulator3 local, long[] tileCount, long[] tileSumL, long[] tileSumL2)
    {
        if (local.TileA == null) return;
        for (int i = 0; i < local.TileA.Length; i++)
        {
            tileCount[i] += local.TileA[i];
            tileSumL[i] += local.TileB![i];
            tileSumL2[i] += local.TileC![i];
        }
    }

    /// <summary>1 バンドぶんの累算（グローバル1項＋タイル1項）。<see cref="ComputeBrenner"/> 用。</summary>
    private sealed class SumAccumulator1
    {
        public long GlobalA, GlobalB;
        public readonly long[]? TileA, TileB;

        public SumAccumulator1(int tilesX, int tilesY)
        {
            if (tilesX > 0 && tilesY > 0)
            {
                int n = tilesX * tilesY;
                TileA = new long[n];
                TileB = new long[n];
            }
        }
    }

    /// <summary>1 バンドぶんの累算（グローバル3項＋タイル3項）。<see cref="ComputeLaplacianVariance"/> 用。</summary>
    private sealed class SumAccumulator3
    {
        public long GlobalA, GlobalB, GlobalC;
        public readonly long[]? TileA, TileB, TileC;

        public SumAccumulator3(int tilesX, int tilesY)
        {
            if (tilesX > 0 && tilesY > 0)
            {
                int n = tilesX * tilesY;
                TileA = new long[n];
                TileB = new long[n];
                TileC = new long[n];
            }
        }
    }

    /// <summary>1 バンドぶんの累算（グローバル4項＋タイル4項）。<see cref="ComputeReblur"/> 用
    /// （A/B=垂直方向 sF/sV、C/D=水平方向 sF/sV）。</summary>
    private sealed class SumAccumulator4
    {
        public long GlobalA, GlobalB, GlobalC, GlobalD;
        public readonly long[]? TileA, TileB, TileC, TileD;

        public SumAccumulator4(int tilesX, int tilesY)
        {
            if (tilesX > 0 && tilesY > 0)
            {
                int n = tilesX * tilesY;
                TileA = new long[n];
                TileB = new long[n];
                TileC = new long[n];
                TileD = new long[n];
            }
        }
    }
}
