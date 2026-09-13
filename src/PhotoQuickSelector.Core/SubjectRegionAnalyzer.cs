using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoQuickSelector.Core;

/// <summary>
/// <see cref="SubjectRegionAnalyzer.Analyze"/> のパラメータ。
/// </summary>
public sealed record SubjectRegionOptions
{
    /// <summary>タイル一辺 px。既定 256。<see cref="SharpnessAnalyzer"/> と同じ粒度概念。</summary>
    public int TileSize { get; init; } = 256;

    /// <summary>Sobel 勾配の大きさの閾値（二乗同士で比較。<see cref="SharpnessAnalyzer"/> と同じ規約）。既定 32。</summary>
    public int Threshold { get; init; } = 32;

    /// <summary>
    /// 「被写体タイル」判定の比率。タイルのエッジ画素数がこの比率×最大タイルのエッジ画素数以上なら
    /// 被写体タイルとみなす。既定 0.25。
    /// </summary>
    public double DensityRatio { get; init; } = 0.25;

    /// <summary>
    /// 被写体タイル判定の絶対下限（タイル画素数＝<see cref="TileSize"/>² に対する比率）。既定 0.005
    /// （タイル面積の 0.5%）。全体にエッジがほとんど無い画像（ピンボケでコントラストが乏しい等）では
    /// <see cref="DensityRatio"/> による相対基準だけでは、実質ノイズ程度のエッジ数しかないタイルまで
    /// 「最大タイルの一定割合」を満たして被写体扱いになってしまう（外接矩形が画面全体に広がる等）。
    /// <see cref="Analyze"/> は、タイルのエッジ画素数の絶対値がこの下限未満ならどれだけ相対的に大きくても
    /// 被写体タイルにしない。最大タイルのエッジ画素数自体がこの下限未満なら、被写体タイルは 1 つも
    /// 見つからない（<see cref="SubjectRegionScore.TileCount"/>=0）。
    /// </summary>
    public double MinTileEdgeFraction { get; init; } = 0.005;

    /// <summary>勾配方向を [0,π) で分割するビン数。既定 8（22.5°刻み）。</summary>
    public int OrientationBins { get; init; } = 8;

    /// <summary>
    /// ビンが「最悪/最良」判定の対象になるために必要な最小エッジ幅サンプル数。既定 50。
    /// サンプルが少ないビンは中央値が不安定なため Worst/Best 選定から除外する（表示・カウントはする）。
    /// </summary>
    public int MinEdgesPerBin { get; init; } = 50;

    /// <summary>エッジ幅の採用上限 px。これ以上は「歩いても収束しなかった＝計測失敗」として破棄。既定 64。</summary>
    public int MaxEdgeWidth { get; init; } = 64;

    /// <summary>エッジ幅を測るウォークの 1 歩の距離 px（双線形補間でサブピクセル歩行）。既定 0.5。</summary>
    public double WalkStep { get; init; } = 0.5;

    /// <summary>
    /// ウォーク継続に必要な、1 歩あたりの輝度変化量の下限。これ未満に落ちたら「山/谷に達した」と
    /// みなして打ち切る（<see cref="SharpnessMetrics"/> の Marziliano 実装は整数近傍の狭義単調判定だが、
    /// こちらはサブピクセル歩行のため閾値式にしている）。既定 0.75。
    /// </summary>
    public double WalkMinDelta { get; init; } = 0.75;
}

/// <summary>
/// <see cref="SubjectRegionAnalyzer.Analyze"/> の結果。
/// </summary>
/// <remarks>
/// <see cref="BinEdgeCounts"/>/<see cref="BinWidthCounts"/>/<see cref="BinMedianWidths"/> は配列（参照型）を
/// <see cref="IReadOnlyList{T}"/> として保持しているため、レコードの既定 Equals はこれらを**参照比較**する
/// （要素が同じでも別インスタンスなら不一致になる）。同値比較をしたい場合はプロパティ単位で
/// シーケンス比較すること（全体を <c>Assert.Equal</c> 等に渡さない）。
/// </remarks>
/// <param name="TileCount">被写体タイルの数。0 なら以降の実数値プロパティはすべて <see cref="double.NaN"/>。</param>
/// <param name="Bounds">被写体タイル群の外接矩形（px、タイル格子基準）。被写体タイルが無ければ既定値（0,0,0,0）。</param>
/// <param name="Tenengrad">被写体タイル内の Σ(閾値超mag²) ÷ 全内部画素数（<see cref="SharpnessScore.Global"/> と同じ向き）。</param>
/// <param name="EdgeDensity">被写体タイル内のエッジ画素数 ÷ 全内部画素数。</param>
/// <param name="PerEdgeMag2">被写体タイル内のエッジ画素のみでの平均 mag²（Σmag² ÷ エッジ画素数）。</param>
/// <param name="AnisotropyRatio">構造テンソルの λ2/λ1（0..1、1に近いほど等方的＝ボケが方向非依存）。λ1=0 なら NaN。</param>
/// <param name="DominantGradientDegrees">構造テンソルから求めた支配的勾配方向（度）。</param>
/// <param name="BinEdgeCounts">方向ビンごとのエッジ画素数（NMS 前・全エッジ画素が対象）。</param>
/// <param name="BinWidthCounts">方向ビンごとに実際にエッジ幅を測れた画素数（NMS 通過＋有効幅のみ）。</param>
/// <param name="BinMedianWidths">方向ビンごとのエッジ幅中央値 px。サンプル 0 件なら NaN。</param>
/// <param name="WorstWidth">
/// <see cref="MinEdgesPerBin"/> 以上のサンプルを持つビンの中で最大の中央値（＝最もボケている方向の幅）。
/// 該当ビンが無ければ NaN。
/// </param>
/// <param name="WorstBin">上記ビンの番号。無ければ -1。</param>
/// <param name="BestWidth">同条件での最小の中央値。該当ビンが無ければ NaN。</param>
/// <param name="WidthRatio">WorstWidth ÷ BestWidth（値が大きいほど一方向ブレ＝異方性ボケが強い）。片方でも NaN、
/// または BestWidth が 0 以下なら NaN。</param>
/// <param name="AfWindowEdgeCount">AF 窓内（クリップ後）のエッジ画素数。窓が無ければ 0。</param>
/// <param name="AfWindowPixelCount">AF 窓内（クリップ後）の内部画素数。窓が無ければ 0。</param>
/// <param name="TileSize">解析に使ったタイル一辺 px。</param>
/// <param name="Threshold">解析に使った閾値。</param>
/// <param name="Version">アルゴリズム版。</param>
public sealed record SubjectRegionScore(
    int TileCount,
    RectI Bounds,
    double Tenengrad,
    double EdgeDensity,
    double PerEdgeMag2,
    double AnisotropyRatio,
    double DominantGradientDegrees,
    IReadOnlyList<long> BinEdgeCounts,
    IReadOnlyList<int> BinWidthCounts,
    IReadOnlyList<double> BinMedianWidths,
    double WorstWidth,
    int WorstBin,
    double BestWidth,
    double WidthRatio,
    long AfWindowEdgeCount,
    long AfWindowPixelCount,
    int TileSize,
    int Threshold,
    byte Version)
{
    /// <summary>ビン番号からビン中心の角度（度、[0,180) の範囲）を返す。</summary>
    public static double BinCenterDegrees(int bin, int bins) => (bin + 0.5) * 180.0 / bins;
}

/// <summary>
/// 「被写体領域」（エッジ密度の高いタイル群）を特定し、勾配方向ビンごとのエッジ幅からブレの
/// 方向依存性を推定する解析。<see cref="SharpnessAnalyzer"/>（Tenengrad・最大タイル）は一方向ブレの
/// 被写体では「ブレ方向に平行な、たまたま残った鋭いエッジ」を最大タイルとして誤検出しうる
/// （動きに平行なエッジはボケの影響を受けにくいため）。本解析は方向ビン別にエッジ幅を測ることで、
/// 「最も幅が広い＝最もボケている方向」を特定し、方向間の幅の比（<see cref="SubjectRegionScore.WidthRatio"/>）
/// を異方性ボケの指標として提供する。UI 非依存の純粋計算のみ。
/// タイル判定には相対基準（<see cref="SubjectRegionOptions.DensityRatio"/>）に加え絶対下限
/// （<see cref="SubjectRegionOptions.MinTileEdgeFraction"/>）があり、画像全体がほぼ平坦（ピンボケで
/// コントラストに乏しい等）なタイルまで相対基準だけで被写体扱いになる誤検出を防ぐ。
/// </summary>
public static class SubjectRegionAnalyzer
{
    /// <summary>アルゴリズム版。保存済みスコアとの互換性判定に使う。</summary>
    public const byte AlgorithmVersion = 1;

    /// <summary>エッジ幅ウォークの片方向あたりの最大歩数（固定値。<see cref="SubjectRegionOptions.MaxEdgeWidth"/> とは独立）。</summary>
    private const int MaxWalkSteps = 128;

    /// <summary>
    /// BGRA8 バッファを解析する。引数規約は <see cref="SharpnessAnalyzer.Analyze"/> と同一
    /// （stride ≥ width*4、alpha は輝度計算に使わない）。
    /// </summary>
    /// <param name="bgra">1 画素 4 byte（B,G,R,A の順）のバッファ。</param>
    /// <param name="width">画像幅 px。3 未満は空スコア（<see cref="SubjectRegionScore.TileCount"/>=0・実数値 NaN）を返す。</param>
    /// <param name="height">画像高さ px。3 未満は同上。</param>
    /// <param name="stride">1 行のバイト数。width*4 以上が必須。</param>
    /// <param name="options">タイル・閾値・ビン等のパラメータ。</param>
    /// <param name="afWindow">AF 窓（表示空間 px の矩形）。画像外へのはみ出しは内部でクリップする。null なら AF 系は 0。</param>
    /// <param name="cancellationToken">
    /// 解析中断用。並列走査（<see cref="Parallel"/> の <c>ParallelOptions</c>）へ渡し、要求されていれば
    /// <see cref="OperationCanceledException"/> をそのまま伝播させる。既定は無効。
    /// </param>
    /// <exception cref="ArgumentException">
    /// width/height が負・stride が width*4 未満・bgra が stride*height に満たない場合。
    /// </exception>
    /// <remarks>
    /// 決定性：被写体タイルは互いに素な画素範囲を占有するため、タイルごとの解析（<see cref="Parallel.For"/>）は
    /// 結果配列の排他的な1スロットへだけ書き込む（他タイルと同じ添字を共有しない）。最終集約は
    /// スレッド完了順に依存しない**固定順（タイル添字の昇順）の逐次ループ**で行うため、浮動小数点の
    /// 加算順序が実行のたびに変わることがなく、同一入力からは常にビット同一の結果になる。
    /// </remarks>
    public static SubjectRegionScore Analyze(
        ReadOnlySpan<byte> bgra, int width, int height, int stride,
        SubjectRegionOptions options, RectI? afWindow, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (width < 0) throw new ArgumentException("width は 0 以上である必要がある。", nameof(width));
        if (height < 0) throw new ArgumentException("height は 0 以上である必要がある。", nameof(height));
        if (stride < width * 4) throw new ArgumentException("stride は width*4 以上である必要がある。", nameof(stride));
        if (bgra.Length < (long)stride * height)
            throw new ArgumentException("bgra の長さが stride*height に満たない。", nameof(bgra));

        if (width < 3 || height < 3)
            return Empty(options, 0, 0);

        int tileSize = options.TileSize;
        long thresholdSq = (long)options.Threshold * options.Threshold;
        var parallelOptions = new ParallelOptions { CancellationToken = cancellationToken };

        byte[] yPlane = ArrayPool<byte>.Shared.Rent(width * height);
        try
        {
            SharpnessAnalyzer.BuildLuminancePlane(bgra, width, height, stride, yPlane);

            (long AfEdgeCount, long AfPixelCount) af = afWindow is { } rect
                ? ComputeAfWindowCounts(yPlane, width, height, rect, thresholdSq)
                : (0L, 0L);

            int tilesX = tileSize > 0 ? width / tileSize : 0;
            int tilesY = tileSize > 0 ? height / tileSize : 0;

            long[] tileEdgeCounts = tilesX > 0 && tilesY > 0
                ? ComputeTileEdgeCounts(yPlane, width, height, tileSize, tilesX, tilesY, thresholdSq, parallelOptions)
                : Array.Empty<long>();

            long maxEdgeCount = 0;
            foreach (long c in tileEdgeCounts)
                if (c > maxEdgeCount) maxEdgeCount = c;

            if (tilesX == 0 || tilesY == 0 || maxEdgeCount == 0)
                return Empty(options, af.AfEdgeCount, af.AfPixelCount);

            // 絶対下限（タイル面積比）。最大タイルすら満たさなければ被写体タイルは無しと確定する
            // （相対基準＝DensityRatio だけだと、画像全体が平坦でもノイズ程度のタイルが「最大の25%」を
            // 満たして被写体扱いになってしまうため。CLAUDE.md「鮮鋭度スコアの改善①」節の申し送り対応）。
            long minTileEdges = (long)Math.Ceiling(options.MinTileEdgeFraction * tileSize * tileSize);
            if (maxEdgeCount < minTileEdges)
                return Empty(options, af.AfEdgeCount, af.AfPixelCount);

            double densityThreshold = Math.Max(options.DensityRatio * maxEdgeCount, minTileEdges);

            // 被写体タイル＝行優先（タイル添字の昇順）で走査・収集。この順序を以後の集約でも維持する。
            var subjectIdx = new List<int>();
            for (int ty = 0; ty < tilesY; ty++)
                for (int tx = 0; tx < tilesX; tx++)
                {
                    int idx = ty * tilesX + tx;
                    if (tileEdgeCounts[idx] >= densityThreshold)
                        subjectIdx.Add(idx);
                }

            var tileResults = new TileResult[subjectIdx.Count];
            Parallel.For(0, subjectIdx.Count, parallelOptions, i =>
            {
                int idx = subjectIdx[i];
                int tx = idx % tilesX, ty = idx / tilesX;
                tileResults[i] = ProcessSubjectTile(
                    yPlane, width, height, tx * tileSize, ty * tileSize, tileSize, thresholdSq, options);
            });

            return Combine(options, tilesX, subjectIdx, tileResults, af.AfEdgeCount, af.AfPixelCount);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(yPlane);
        }
    }

    /// <summary>
    /// 被写体タイル無し／極小画像の空スコアを返す。AF 窓のカウントは（計算済みなら）そのまま持たせる
    /// （被写体タイルの有無と無関係に成立する値のため）。
    /// </summary>
    private static SubjectRegionScore Empty(SubjectRegionOptions options, long afEdgeCount, long afPixelCount)
    {
        int bins = options.OrientationBins;
        var zeroCounts = new long[bins];
        var zeroWidthCounts = new int[bins];
        var nanWidths = new double[bins];
        for (int i = 0; i < bins; i++) nanWidths[i] = double.NaN;

        return new SubjectRegionScore(
            TileCount: 0,
            Bounds: default,
            Tenengrad: double.NaN,
            EdgeDensity: double.NaN,
            PerEdgeMag2: double.NaN,
            AnisotropyRatio: double.NaN,
            DominantGradientDegrees: double.NaN,
            BinEdgeCounts: zeroCounts,
            BinWidthCounts: zeroWidthCounts,
            BinMedianWidths: nanWidths,
            WorstWidth: double.NaN,
            WorstBin: -1,
            BestWidth: double.NaN,
            WidthRatio: double.NaN,
            AfWindowEdgeCount: afEdgeCount,
            AfWindowPixelCount: afPixelCount,
            TileSize: options.TileSize,
            Threshold: options.Threshold,
            Version: AlgorithmVersion);
    }

    /// <summary>
    /// タイルごとのエッジ画素数（被写体タイル判定用）。<see cref="SharpnessAnalyzer"/> の
    /// バンド並列パターンを踏襲＝各バンド（タイル1行分）が担当するタイル添字は他バンドと重ならないため、
    /// マージは順不同でも（各添字は実質1バンドからしか値が来ないため）決定的。
    /// </summary>
    private static long[] ComputeTileEdgeCounts(
        byte[] yPlane, int width, int height, int tileSize, int tilesX, int tilesY,
        long thresholdSq, ParallelOptions parallelOptions)
    {
        var tileEdgeCounts = new long[tilesX * tilesY];
        int tileColLimit = tilesX * tileSize;
        int bandCount = tilesY; // フルタイル行のみが対象（端数の余り行はタイル判定に使わない）
        var reduceLock = new object();

        Parallel.For(
            0, bandCount,
            parallelOptions,
            () => new long[tilesX * tilesY],
            (band, _, local) =>
            {
                int rowStart = Math.Max(1, band * tileSize);
                int rowEndExclusive = Math.Min(height - 1, (band + 1) * tileSize);
                for (int row = rowStart; row < rowEndExclusive; row++)
                {
                    int rowBase = row * width;
                    int colEnd = Math.Min(width - 1, tileColLimit);
                    for (int col = 1; col < colEnd; col++)
                    {
                        int idx = rowBase + col;
                        ComputeSobel(yPlane, width, idx, out int gx, out int gy);
                        long mag2 = (long)gx * gx + (long)gy * gy;
                        if (mag2 < thresholdSq) continue;
                        local[band * tilesX + col / tileSize]++;
                    }
                }
                return local;
            },
            local =>
            {
                lock (reduceLock)
                {
                    for (int i = 0; i < local.Length; i++)
                        tileEdgeCounts[i] += local[i];
                }
            });

        return tileEdgeCounts;
    }

    /// <summary>
    /// 被写体タイル群の結果を、タイル添字の昇順（<paramref name="subjectIdx"/> の並び＝行優先で構築済み）で
    /// 逐次集約する。この順序固定が決定性の要（並列実行の完了順に依存させない）。
    /// </summary>
    private static SubjectRegionScore Combine(
        SubjectRegionOptions options, int tilesX, List<int> subjectIdx, TileResult[] tileResults,
        long afEdgeCount, long afPixelCount)
    {
        int bins = options.OrientationBins;
        int tileSize = options.TileSize;

        long totalPixelCount = 0, totalEdgeCount = 0;
        double totalEdgeMag2Sum = 0, gx2Sum = 0, gy2Sum = 0, gxGySum = 0;
        var binCounts = new long[bins];
        var binWidths = new List<double>[bins];
        for (int b = 0; b < bins; b++) binWidths[b] = new List<double>();

        int minTx = int.MaxValue, minTy = int.MaxValue, maxTx = int.MinValue, maxTy = int.MinValue;

        for (int i = 0; i < subjectIdx.Count; i++)
        {
            int idx = subjectIdx[i];
            int tx = idx % tilesX, ty = idx / tilesX;
            if (tx < minTx) minTx = tx;
            if (tx > maxTx) maxTx = tx;
            if (ty < minTy) minTy = ty;
            if (ty > maxTy) maxTy = ty;

            var r = tileResults[i];
            totalPixelCount += r.PixelCount;
            totalEdgeCount += r.EdgeCount;
            totalEdgeMag2Sum += r.EdgeMag2Sum;
            gx2Sum += r.Gx2Sum;
            gy2Sum += r.Gy2Sum;
            gxGySum += r.GxGySum;
            for (int b = 0; b < bins; b++)
            {
                binCounts[b] += r.BinCounts[b];
                binWidths[b].AddRange(r.BinWidths[b]);
            }
        }

        var bounds = new RectI(minTx * tileSize, minTy * tileSize, (maxTx - minTx + 1) * tileSize, (maxTy - minTy + 1) * tileSize);

        var binWidthCounts = new int[bins];
        var binMedianWidths = new double[bins];
        for (int b = 0; b < bins; b++)
        {
            var list = binWidths[b];
            list.Sort();
            binWidthCounts[b] = list.Count;
            binMedianWidths[b] = list.Count > 0 ? list[list.Count / 2] : double.NaN;
        }

        double worst = double.NaN, best = double.NaN;
        int worstBin = -1;
        for (int b = 0; b < bins; b++)
        {
            if (binWidthCounts[b] < options.MinEdgesPerBin) continue;
            double m = binMedianWidths[b];
            if (double.IsNaN(worst) || m > worst) { worst = m; worstBin = b; }
            if (double.IsNaN(best) || m < best) best = m;
        }
        double widthRatio = !double.IsNaN(worst) && !double.IsNaN(best) && best > 0 ? worst / best : double.NaN;

        double tenengrad = totalPixelCount > 0 ? totalEdgeMag2Sum / totalPixelCount : double.NaN;
        double edgeDensity = totalPixelCount > 0 ? (double)totalEdgeCount / totalPixelCount : double.NaN;
        double perEdgeMag2 = totalEdgeCount > 0 ? totalEdgeMag2Sum / totalEdgeCount : double.NaN;

        double trace = gx2Sum + gy2Sum;
        double det = gx2Sum * gy2Sum - gxGySum * gxGySum;
        double disc = Math.Sqrt(Math.Max(0, trace / 2 * (trace / 2) - det));
        double lambda1 = trace / 2 + disc;
        double lambda2 = Math.Max(0, trace / 2 - disc);
        double anisotropyRatio = lambda1 != 0 ? lambda2 / lambda1 : double.NaN;
        double dominantDeg = 0.5 * Math.Atan2(2 * gxGySum, gx2Sum - gy2Sum) * (180.0 / Math.PI);

        return new SubjectRegionScore(
            TileCount: subjectIdx.Count,
            Bounds: bounds,
            Tenengrad: tenengrad,
            EdgeDensity: edgeDensity,
            PerEdgeMag2: perEdgeMag2,
            AnisotropyRatio: anisotropyRatio,
            DominantGradientDegrees: dominantDeg,
            BinEdgeCounts: binCounts,
            BinWidthCounts: binWidthCounts,
            BinMedianWidths: binMedianWidths,
            WorstWidth: worst,
            WorstBin: worstBin,
            BestWidth: best,
            WidthRatio: widthRatio,
            AfWindowEdgeCount: afEdgeCount,
            AfWindowPixelCount: afPixelCount,
            TileSize: options.TileSize,
            Threshold: options.Threshold,
            Version: AlgorithmVersion);
    }

    /// <summary>
    /// 任意矩形内（AF 窓）のエッジ画素数／内部画素数を数える。<see cref="SharpnessAnalyzer"/> の
    /// ComputeRegion 同様、タイル格子と無関係な位置になり得るため独立して計算する（単スレッド・逐次＝決定的）。
    /// </summary>
    private static (long EdgeCount, long PixelCount) ComputeAfWindowCounts(
        byte[] y, int width, int height, RectI rect, long thresholdSq)
    {
        int x0 = Math.Max(1, rect.X);
        int y0 = Math.Max(1, rect.Y);
        int x1 = Math.Min(width - 2, rect.X + rect.Width - 1);
        int y1 = Math.Min(height - 2, rect.Y + rect.Height - 1);
        if (x1 < x0 || y1 < y0) return (0, 0);

        long edgeCount = 0, pixelCount = 0;
        for (int yy = y0; yy <= y1; yy++)
        {
            int rowBase = yy * width;
            for (int xx = x0; xx <= x1; xx++)
            {
                ComputeSobel(y, width, rowBase + xx, out int gx, out int gy);
                long mag2 = (long)gx * gx + (long)gy * gy;
                pixelCount++;
                if (mag2 >= thresholdSq) edgeCount++;
            }
        }
        return (edgeCount, pixelCount);
    }

    /// <summary>
    /// 1 被写体タイル分の解析（方向ビン別エッジ数・エッジ幅・構造テンソル和・画素数）。
    /// 被写体タイルは互いに素な画素範囲のみを読み書きするため、呼び出し元の並列ループから
    /// スレッドセーフに呼べる（このメソッド自身はロックを持たない）。
    /// </summary>
    private static TileResult ProcessSubjectTile(
        byte[] yPlane, int width, int height, int tileX, int tileY, int tileSize,
        long thresholdSq, SubjectRegionOptions options)
    {
        int bins = options.OrientationBins;
        var result = new TileResult(bins);

        int x0 = Math.Max(1, tileX);
        int x1 = Math.Min(width - 2, tileX + tileSize - 1);
        int y0 = Math.Max(1, tileY);
        int y1 = Math.Min(height - 2, tileY + tileSize - 1);
        if (x1 < x0 || y1 < y0) return result; // タイルが小さすぎて内部画素が無い

        for (int row = y0; row <= y1; row++)
        {
            int rowBase = row * width;
            for (int col = x0; col <= x1; col++)
            {
                int idx = rowBase + col;
                ComputeSobel(yPlane, width, idx, out int gx, out int gy);
                long gx2 = (long)gx * gx, gy2 = (long)gy * gy;
                long mag2 = gx2 + gy2;

                result.PixelCount++;
                if (mag2 < thresholdSq) continue;

                result.EdgeCount++;
                result.EdgeMag2Sum += mag2;
                result.Gx2Sum += gx2;
                result.Gy2Sum += gy2;
                result.GxGySum += (double)gx * gy;

                // 勾配角を [0,π) へ畳み込む（符号の無い「向き」だけを見る＝エッジ方向のビン分け）。
                double angle = Math.Atan2(gy, gx);
                if (angle < 0) angle += Math.PI;
                else if (angle >= Math.PI) angle -= Math.PI;
                int bin = (int)(angle / Math.PI * bins);
                if (bin < 0) bin = 0;
                if (bin >= bins) bin = bins - 1;
                result.BinCounts[bin]++;

                double magnitude = Math.Sqrt((double)mag2);
                double ux = gx / magnitude, uy = gy / magnitude;

                // 非最大抑制：勾配方向に半歩ずつ進んだ2点の mag² を双線形補間で求め、両方以上なら局所極大。
                // 補間に要る周囲4画素のいずれかが内部画素の範囲外（Sobel が組めない）なら、幅計測せず次の画素へ
                // （このタイルの端＝画像の端に近い画素は稀に抜けるが、被写体タイル全体からは十分なサンプルが集まる）。
                if (!TryBilinearMag2(yPlane, width, height, col + ux, row + uy, out double magForward)) continue;
                if (!TryBilinearMag2(yPlane, width, height, col - ux, row - uy, out double magBackward)) continue;
                if (mag2 < magForward || mag2 < magBackward) continue;

                double forward = WalkEdgeWidth(yPlane, width, height, col, row, ux, uy, options);
                double backward = WalkEdgeWidth(yPlane, width, height, col, row, -ux, -uy, options);
                double edgeWidth = forward + backward;
                if (edgeWidth > 0 && edgeWidth < options.MaxEdgeWidth)
                    result.BinWidths[bin].Add(edgeWidth);
            }
        }

        return result;
    }

    /// <summary>
    /// エッジ画素から <paramref name="ux"/>,<paramref name="uy"/>（単位ベクトル）方向へ
    /// <see cref="SubjectRegionOptions.WalkStep"/> 刻みで歩き、輝度が単調に変化する距離を測る
    /// （Marziliano 式の考え方をサブピクセル化したもの）。
    /// </summary>
    /// <remarks>
    /// 1 歩目で変化の符号を確定させ（以降は固定）、各歩の**直前に採用した点からの**変化量が
    /// <see cref="SubjectRegionOptions.WalkMinDelta"/> 未満に落ちた時点（＝山/谷に達した）で打ち切る。
    /// 1 歩目の変化が符号無し（0）または閾値未満なら距離 0（採用歩なし）を返す。
    /// </remarks>
    private static double WalkEdgeWidth(
        byte[] y, int width, int height, int startCol, int startRow, double ux, double uy, SubjectRegionOptions options)
    {
        double prev = y[startRow * width + startCol];
        int sign = 0;
        double distance = 0;

        for (int step = 1; step <= MaxWalkSteps; step++)
        {
            double fx = startCol + step * options.WalkStep * ux;
            double fy = startRow + step * options.WalkStep * uy;
            if (!TryBilinearLuma(y, width, height, fx, fy, out double v)) break;

            double dv = v - prev;
            if (step == 1)
            {
                sign = Math.Sign(dv);
                if (sign == 0) break;
            }
            if (dv * sign < options.WalkMinDelta) break;

            distance = step * options.WalkStep;
            prev = v;
        }

        return distance;
    }

    /// <summary>3x3 Sobel（Gx・Gy 同時）。idx±width±1 が範囲内である前提（呼び出し元が内部画素であることを保証する）。</summary>
    private static void ComputeSobel(byte[] y, int width, int idx, out int gx, out int gy)
    {
        int p00 = y[idx - width - 1], p01 = y[idx - width], p02 = y[idx - width + 1];
        int p10 = y[idx - 1], p12 = y[idx + 1];
        int p20 = y[idx + width - 1], p21 = y[idx + width], p22 = y[idx + width + 1];
        gx = (p02 - p00) + 2 * (p12 - p10) + (p22 - p20);
        gy = (p20 - p00) + 2 * (p21 - p01) + (p22 - p02);
    }

    /// <summary>整数画素位置の Sobel mag²（<see cref="TryBilinearMag2"/> の補間元）。</summary>
    private static double Mag2At(byte[] y, int width, int idx)
    {
        ComputeSobel(y, width, idx, out int gx, out int gy);
        return (double)gx * gx + (double)gy * gy;
    }

    /// <summary>
    /// 任意のサブピクセル位置の mag² を、周囲4画素（それぞれで Sobel を計算）の双線形補間で求める。
    /// 4画素のいずれかが内部画素の範囲外（境界1px・画像外）なら false（呼び出し元は計測をスキップする）。
    /// </summary>
    private static bool TryBilinearMag2(byte[] y, int width, int height, double fx, double fy, out double mag2)
    {
        int x0 = (int)Math.Floor(fx);
        int y0 = (int)Math.Floor(fy);
        mag2 = 0;
        if (x0 < 1 || x0 + 1 > width - 2 || y0 < 1 || y0 + 1 > height - 2) return false;

        double tx = fx - x0, ty = fy - y0;
        double m00 = Mag2At(y, width, y0 * width + x0);
        double m10 = Mag2At(y, width, y0 * width + x0 + 1);
        double m01 = Mag2At(y, width, (y0 + 1) * width + x0);
        double m11 = Mag2At(y, width, (y0 + 1) * width + x0 + 1);
        mag2 = (1 - tx) * (1 - ty) * m00 + tx * (1 - ty) * m10 + (1 - tx) * ty * m01 + tx * ty * m11;
        return true;
    }

    /// <summary>
    /// 任意のサブピクセル位置の輝度を、周囲4画素の双線形補間で求める（エッジ幅ウォークのサンプリング用）。
    /// 4画素のいずれかが画像外なら false（ウォークはそこで打ち切り）。
    /// </summary>
    private static bool TryBilinearLuma(byte[] y, int width, int height, double fx, double fy, out double value)
    {
        int x0 = (int)Math.Floor(fx);
        int y0 = (int)Math.Floor(fy);
        value = 0;
        if (x0 < 0 || x0 + 1 >= width || y0 < 0 || y0 + 1 >= height) return false;

        double tx = fx - x0, ty = fy - y0;
        double v00 = y[y0 * width + x0];
        double v10 = y[y0 * width + x0 + 1];
        double v01 = y[(y0 + 1) * width + x0];
        double v11 = y[(y0 + 1) * width + x0 + 1];
        value = (1 - tx) * (1 - ty) * v00 + tx * (1 - ty) * v10 + (1 - tx) * ty * v01 + tx * ty * v11;
        return true;
    }

    /// <summary>1 被写体タイル分の集計結果。方向ビン数ぶんの配列/リストを ctor で確保する。</summary>
    private sealed class TileResult
    {
        public long PixelCount;
        public long EdgeCount;
        public double EdgeMag2Sum;
        public double Gx2Sum, Gy2Sum, GxGySum;
        public readonly long[] BinCounts;
        public readonly List<double>[] BinWidths;

        public TileResult(int bins)
        {
            BinCounts = new long[bins];
            BinWidths = new List<double>[bins];
            for (int i = 0; i < bins; i++) BinWidths[i] = new List<double>();
        }
    }
}
