using System.Buffers;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoQuickSelector.Core;

/// <summary>
/// 整数矩形（左上＋サイズ、px）。<see cref="SharpnessAnalyzer"/> の AF 窓・タイル位置に使う。
/// </summary>
public readonly record struct RectI(int X, int Y, int Width, int Height);

/// <summary>
/// <see cref="SharpnessAnalyzer.Analyze"/> のパラメータ。
/// </summary>
public sealed record SharpnessOptions
{
    /// <summary>タイル一辺 px。既定 256。<see cref="SharpnessScore.MaxTile"/> の粒度を決める。</summary>
    public int TileSize { get; init; } = 256;

    /// <summary>
    /// 勾配の大きさ（sqrt(Gx²+Gy²)、Sobel 単位＝1 軸最大 1020）の閾値。これ未満の画素は加算しない
    /// （ノイズ由来の微小勾配を合算に含めないため）。既定 32。0 で閾値なし（全画素を合算）。
    /// </summary>
    public int Threshold { get; init; } = 32;
}

/// <summary>
/// <see cref="SharpnessAnalyzer.Analyze"/> の結果。すべて「Tenengrad」系の指標＝
/// 評価対象画素の Gx²+Gy²（閾値超のみ）の総和 ÷ 評価対象画素数。値が大きいほど鮮鋭。
/// </summary>
/// <param name="Global">画像全体（内部画素のみ・境界 1px は評価対象外）の平均。</param>
/// <param name="AfWindow">AF 窓内の同指標。窓が無い、またはクリップ後に評価画素が 0 なら <see cref="double.NaN"/>。</param>
/// <param name="MaxTile">全タイル中の最大値。画像に完全に収まるタイルが 1 つも無ければ <see cref="Global"/> と同じ値。</param>
/// <param name="MaxTileX">最大タイルの左上 X（px）。タイルが無ければ 0。</param>
/// <param name="MaxTileY">最大タイルの左上 Y（px）。タイルが無ければ 0。</param>
/// <param name="Anisotropy">画像全体の ΣGx² / ΣGy²（閾値超画素のみ）。値が 1 から離れるほど勾配方向に偏りがある
/// （横方向のボケ＝ΣGx²が減り値は小さく、縦方向のボケ＝値は大きくなる）。分母 0 なら NaN。</param>
/// <param name="AfWindowAnisotropy">AF 窓内の同比。窓が無い、または分母が 0 なら NaN。</param>
/// <param name="TileSize">解析に使ったタイル一辺 px（<see cref="SharpnessOptions.TileSize"/> の反映値）。</param>
/// <param name="Threshold">解析に使った閾値（<see cref="SharpnessOptions.Threshold"/> の反映値）。</param>
/// <param name="Version">アルゴリズム版。将来アルゴリズムを変えた際に過去の保存値と区別するための番号。今回は 1。</param>
public readonly record struct SharpnessScore(
    double Global,
    double AfWindow,
    double MaxTile,
    int MaxTileX,
    int MaxTileY,
    double Anisotropy,
    double AfWindowAnisotropy,
    int TileSize,
    int Threshold,
    byte Version);

/// <summary>
/// Sobel 勾配（Tenengrad 系）によるピンボケ／鮮鋭度の解析。UI 非依存の純粋計算のみ
/// （デコード済み BGRA8 バッファを受け取るだけで、画像デコード自体は行わない）。
/// </summary>
public static class SharpnessAnalyzer
{
    /// <summary>アルゴリズム版。保存済みスコアとの互換性判定に使う。</summary>
    public const byte AlgorithmVersion = 1;

    /// <summary>
    /// BGRA8（premultiplied でも alpha=255 前提。alpha 値自体は輝度計算に使わない）バッファを解析する。
    /// </summary>
    /// <param name="bgra">1 画素 4 byte（B,G,R,A の順）のバッファ。<paramref name="stride"/> 行間隔で読む。</param>
    /// <param name="width">画像幅 px。3 未満は全ゼロのスコアを返す（Sobel 3×3 が組めないため）。</param>
    /// <param name="height">画像高さ px。3 未満は全ゼロのスコアを返す。</param>
    /// <param name="stride">1 行のバイト数。行末パディングを許容するため width*4 以上が必須。</param>
    /// <param name="options">タイルサイズ・閾値。</param>
    /// <param name="afWindow">
    /// AF 窓（表示空間 px の矩形）。画像外へはみ出す分・全体が画像外の場合も呼び出し側は気にせず渡してよい
    /// （このメソッド内でクリップする）。null なら AF 系の指標は NaN。
    /// </param>
    /// <param name="cancellationToken">
    /// 解析中断用。並列走査（<see cref="Parallel"/> の <c>ParallelOptions</c>）へ渡し、要求されていれば
    /// <see cref="OperationCanceledException"/> をそのまま伝播させる（呼び出し側で握りつぶさない）。
    /// 既定は無効（<see cref="CancellationToken.None"/>）。
    /// </param>
    /// <exception cref="ArgumentException">
    /// width/height が負・stride が width*4 未満・bgra が stride*height に満たない場合。
    /// </exception>
    public static SharpnessScore Analyze(
        ReadOnlySpan<byte> bgra, int width, int height, int stride,
        SharpnessOptions options, RectI? afWindow, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (width < 0) throw new ArgumentException("width は 0 以上である必要がある。", nameof(width));
        if (height < 0) throw new ArgumentException("height は 0 以上である必要がある。", nameof(height));
        if (stride < width * 4) throw new ArgumentException("stride は width*4 以上である必要がある。", nameof(stride));
        if (bgra.Length < (long)stride * height)
            throw new ArgumentException("bgra の長さが stride*height に満たない。", nameof(bgra));

        int tileSize = options.TileSize;
        int threshold = options.Threshold;

        // Sobel は 3x3 近傍が要るため、境界 1px を除いた内部画素だけを評価する。
        // 2x2 以下では内部画素が存在しないので、デコード失敗等の異常系として素通しできるよう全ゼロで返す（例外にしない）。
        if (width < 3 || height < 3)
            return new SharpnessScore(0, double.NaN, 0, 0, 0, double.NaN, double.NaN, tileSize, threshold, AlgorithmVersion);

        // 閾値判定は sqrt を避け二乗同士で比較する（Threshold=0 は「全画素通す」に自然に一致＝mag2>=0 は常に真）。
        long thresholdSq = (long)threshold * threshold;

        // 輝度平面（BGRA→Y）をまず 1 パスで作る。50MP 級（8640x5760）でも行毎確保をしないよう ArrayPool を使う。
        byte[] yPlane = ArrayPool<byte>.Shared.Rent(width * height);
        try
        {
            BuildLuminancePlane(bgra, width, height, stride, yPlane);

            // タイルは (0,0) 起点で画像に完全に収まるものだけ（端数の余り行/列はタイル扱いしない＝Global 側にのみ算入）。
            int tilesX = tileSize > 0 ? width / tileSize : 0;
            int tilesY = tileSize > 0 ? height / tileSize : 0;

            // バンド＝タイル1段分（TileSize 行）の水平帯。Parallel.For をバンド単位に割ることで、
            // 各バンドが担当する画像行は他バンドと重ならず、タイル別カウンタもバンド内で完結する
            // （ロック不要。最後にバンド間だけロックして合算する）。タイルを跨がない最終端の余り行も
            // 1 つのバンドとして扱い、Global にだけ寄与させる。
            int bandRows = tileSize > 0 ? tileSize : height;
            int bandCount = (height + bandRows - 1) / bandRows;

            var tileSums = tilesX > 0 && tilesY > 0 ? new double[tilesX * tilesY] : Array.Empty<double>();
            var tileCounts = tilesX > 0 && tilesY > 0 ? new long[tilesX * tilesY] : Array.Empty<long>();
            double globalSum = 0, globalGx2 = 0, globalGy2 = 0;
            long globalCount = 0;
            var reduceLock = new object();
            var parallelOptions = new ParallelOptions { CancellationToken = cancellationToken };

            Parallel.For(
                0, bandCount,
                parallelOptions,
                () => new BandAccumulator(tilesX, tilesY),
                (band, _, local) =>
                {
                    int rowStart = Math.Max(1, band * bandRows);
                    int rowEndExclusive = Math.Min(height - 1, (band + 1) * bandRows); // 内部行は 1..height-2
                    bool fullTileBand = tileSize > 0 && band < tilesY;
                    int tileRow = band; // fullTileBand 時、バンド＝タイル行そのもの（bandRows==tileSize のため）
                    for (int y = rowStart; y < rowEndExclusive; y++)
                        ProcessRow(yPlane, width, y, thresholdSq, tileSize, tilesX, fullTileBand, tileRow, local);
                    return local;
                },
                local =>
                {
                    lock (reduceLock)
                    {
                        globalSum += local.GlobalSum;
                        globalCount += local.GlobalCount;
                        globalGx2 += local.GlobalGx2;
                        globalGy2 += local.GlobalGy2;
                        if (local.TileSums != null)
                        {
                            for (int i = 0; i < local.TileSums.Length; i++)
                            {
                                tileSums[i] += local.TileSums[i];
                                tileCounts[i] += local.TileCounts![i];
                            }
                        }
                    }
                });

            double global = globalCount > 0 ? globalSum / globalCount : 0;
            double anisotropy = globalGy2 != 0 ? globalGx2 / globalGy2 : double.NaN;

            // 最大タイル探索。同点は行優先走査での最初のタイルを採る（厳密な '>' のみで更新するため自然に満たす）。
            double maxTileScore = 0;
            int maxTileX = 0, maxTileY = 0;
            bool foundTile = false;
            for (int ty = 0; ty < tilesY; ty++)
            {
                for (int tx = 0; tx < tilesX; tx++)
                {
                    int idx = ty * tilesX + tx;
                    long count = tileCounts[idx];
                    if (count == 0) continue; // TileSize が極小の場合、境界タイルは内部画素 0 になり得る
                    double score = tileSums[idx] / count;
                    if (!foundTile || score > maxTileScore)
                    {
                        foundTile = true;
                        maxTileScore = score;
                        maxTileX = tx * tileSize;
                        maxTileY = ty * tileSize;
                    }
                }
            }
            if (!foundTile)
            {
                maxTileScore = global;
                maxTileX = 0;
                maxTileY = 0;
            }

            (double AfScore, double AfAnisotropy) af = afWindow is { } rect
                ? ComputeRegion(yPlane, width, height, rect, thresholdSq)
                : (double.NaN, double.NaN);

            return new SharpnessScore(
                global, af.AfScore, maxTileScore, maxTileX, maxTileY,
                anisotropy, af.AfAnisotropy, tileSize, threshold, AlgorithmVersion);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(yPlane);
        }
    }

    /// <summary>
    /// ImageMetadata の AF 点（生センサー座標・FocusReferenceSize 基準）を表示空間 px の正方窓へ写す。
    /// Orientation の座標変換は表示（<c>PreviewViewport.OrientationMatrix</c>・
    /// <c>PreviewControl.Overlays</c> の AF 枠描画）と同じ規則。Core は WinUI/System.Numerics に依存しないため、
    /// 同じ写像を素の四則演算で再実装している（値は一致するが型は共有していない。両者を変更するときは対で見直すこと）。
    /// </summary>
    /// <param name="meta">対象画像のメタデータ。FocusPoint が無ければ null を返す。</param>
    /// <param name="displayWidth">正立済みビットマップの幅 px。</param>
    /// <param name="displayHeight">正立済みビットマップの高さ px。</param>
    /// <param name="minSize">窓一辺の下限 px。</param>
    /// <param name="maxSize">窓一辺の上限 px。</param>
    /// <param name="defaultSize">AF 枠サイズが無いときの窓一辺 px。</param>
    public static RectI? AfWindowFor(
        ImageMetadata meta, int displayWidth, int displayHeight,
        int minSize = 256, int maxSize = 1024, int defaultSize = 512)
    {
        if (meta.FocusPoint is not { } fp) return null;

        double rawW = meta.OriginalWidth, rawH = meta.OriginalHeight; // 生センサー寸法（Orientation 適用前）
        if (rawW <= 0 || rawH <= 0) return null;

        // AF 点は FocusReferenceSize 基準の正規化値。基準が無ければ生寸法そのものを基準とみなす
        // （PreviewControl.Overlays.DrawFocusFrame と同じ規約）。
        double refW = meta.FocusReferenceSize is { Width: > 0 } rs ? rs.Width : rawW;
        double refH = meta.FocusReferenceSize is { Height: > 0 } rs2 ? rs2.Height : rawH;
        double rawCx = fp.X * rawW / refW;
        double rawCy = fp.Y * rawH / refH;

        // 窓一辺＝写した AF 枠の長辺（min/max でクランプ）。長辺は Orientation で軸が入れ替わっても
        // 値そのものは不変（Orientation 行列は 0/±1 成分のみの置換＋反転で、辺の長さを変えないため）。
        int side;
        if (meta.FocusSize is { Width: > 0, Height: > 0 } fs)
        {
            double fw = fs.Width * rawW / refW;
            double fh = fs.Height * rawH / refH;
            side = Math.Clamp((int)Math.Round(Math.Max(fw, fh)), minSize, maxSize);
        }
        else
        {
            side = defaultSize;
        }

        (double MappedX, double MappedY) = MapOrientedPoint(meta.Orientation, rawW, rawH, rawCx, rawCy);

        // displayWidth/Height が「Orientation 適用後の生寸法」と食い違う場合（メタデータ不整合等）に備え、
        // 中心座標だけを比率で合わせる（窓サイズは既に px 指定のためスケールしない）。
        int orientedRawWidth = MetadataCalc.DisplayWidth(meta.Orientation, meta.OriginalWidth, meta.OriginalHeight);
        int orientedRawHeight = MetadataCalc.DisplayHeight(meta.Orientation, meta.OriginalWidth, meta.OriginalHeight);
        double scaleX = orientedRawWidth > 0 ? displayWidth / (double)orientedRawWidth : 1.0;
        double scaleY = orientedRawHeight > 0 ? displayHeight / (double)orientedRawHeight : 1.0;
        double centerX = MappedX * scaleX;
        double centerY = MappedY * scaleY;

        int half = side / 2;
        return new RectI((int)Math.Round(centerX) - half, (int)Math.Round(centerY) - half, side, side);
    }

    /// <summary>
    /// 生センサー px の点を Orientation 適用後（表示空間・スケール前）の px へ写す。
    /// <c>PreviewViewport.OrientationMatrix</c> と同じ規則（1:恒等 2:水平反転 3:180度 4:垂直反転
    /// 5:転置 6:90度CW 7:転地 8:270度CW）を素の座標変換として再実装したもの。
    /// </summary>
    private static (double X, double Y) MapOrientedPoint(int orientation, double rawW, double rawH, double x, double y)
        => orientation switch
        {
            2 => (rawW - x, y),
            3 => (rawW - x, rawH - y),
            4 => (x, rawH - y),
            5 => (y, x),
            6 => (rawH - y, x),
            7 => (rawH - y, rawW - x),
            8 => (y, rawW - x),
            _ => (x, y),
        };

    /// <summary>BGRA バッファから輝度平面（width*height、行間パディング無し）を 1 パスで作る。</summary>
    /// <remarks>
    /// Y = (77*R + 150*G + 29*B) &gt;&gt; 8 は ITU-R BT.601 相当の整数固定小数点近似（係数の合計 256）。
    /// alpha は輝度に使わない（premultiplied でも呼び出し側は alpha=255 前提のため無視してよい）。
    /// <see cref="SharpnessMetrics"/>（別手法の比較用）も同じ平面を使うため internal で共有する
    /// （同一アセンブリ内・重複実装を避けるため）。
    /// </remarks>
    internal static void BuildLuminancePlane(ReadOnlySpan<byte> bgra, int width, int height, int stride, byte[] yPlane)
    {
        for (int row = 0; row < height; row++)
        {
            int rowOffset = row * stride;
            int outOffset = row * width;
            for (int col = 0; col < width; col++)
            {
                int idx = rowOffset + col * 4;
                byte b = bgra[idx];
                byte g = bgra[idx + 1];
                byte r = bgra[idx + 2];
                yPlane[outOffset + col] = (byte)((77 * r + 150 * g + 29 * b) >> 8);
            }
        }
    }

    /// <summary>
    /// 内部画素 1 行分の Sobel を計算し、グローバル累計と（フルタイル帯なら）タイル別累計へ加算する。
    /// x=0/width-1（この行の端）は呼び出し元の範囲指定で既に除外されている前提。
    /// </summary>
    private static void ProcessRow(
        byte[] y, int width, int row, long thresholdSq,
        int tileSize, int tilesX, bool fullTileBand, int tileRow, BandAccumulator acc)
    {
        int tileColLimit = tilesX * tileSize; // これ未満の x だけがフルタイルの列範囲に入る
        for (int x = 1; x < width - 1; x++)
        {
            int idx = row * width + x;
            int p00 = y[idx - width - 1], p01 = y[idx - width], p02 = y[idx - width + 1];
            int p10 = y[idx - 1], p12 = y[idx + 1];
            int p20 = y[idx + width - 1], p21 = y[idx + width], p22 = y[idx + width + 1];

            int gx = (p02 - p00) + 2 * (p12 - p10) + (p22 - p20);
            int gy = (p20 - p00) + 2 * (p21 - p01) + (p22 - p02);
            long gx2 = (long)gx * gx, gy2 = (long)gy * gy;
            long mag2 = gx2 + gy2;

            acc.GlobalCount++;
            if (mag2 >= thresholdSq)
            {
                acc.GlobalSum += mag2;
                acc.GlobalGx2 += gx2;
                acc.GlobalGy2 += gy2;
            }

            if (fullTileBand && x < tileColLimit)
            {
                int tileIdx = tileRow * tilesX + (x / tileSize);
                acc.TileCounts![tileIdx]++;
                if (mag2 >= thresholdSq) acc.TileSums![tileIdx] += mag2;
            }
        }
    }

    /// <summary>
    /// 任意矩形（画像外へのはみ出しは呼び出し前提でクリップ済みでなくてよい）内の Tenengrad 平均と異方性比を求める。
    /// AF 窓はタイル格子と無関係な任意位置になり得るため、タイルの合算を流用せず単独で再計算する
    /// （2 回 Sobel を計算する分の効率は捨て、実装をシンプルに保つ）。
    /// </summary>
    private static (double Score, double Anisotropy) ComputeRegion(byte[] y, int width, int height, RectI rect, long thresholdSq)
    {
        // 内部画素の範囲（画像境界 1px 除く）と矩形の共通部分。空なら「評価対象なし」＝NaN。
        int x0 = Math.Max(1, rect.X);
        int y0 = Math.Max(1, rect.Y);
        int x1 = Math.Min(width - 2, rect.X + rect.Width - 1);
        int y1 = Math.Min(height - 2, rect.Y + rect.Height - 1);
        if (x1 < x0 || y1 < y0) return (double.NaN, double.NaN);

        double sum = 0, gx2Sum = 0, gy2Sum = 0;
        long count = 0;
        for (int yy = y0; yy <= y1; yy++)
        {
            int rowBase = yy * width;
            for (int xx = x0; xx <= x1; xx++)
            {
                int idx = rowBase + xx;
                int p00 = y[idx - width - 1], p01 = y[idx - width], p02 = y[idx - width + 1];
                int p10 = y[idx - 1], p12 = y[idx + 1];
                int p20 = y[idx + width - 1], p21 = y[idx + width], p22 = y[idx + width + 1];

                int gx = (p02 - p00) + 2 * (p12 - p10) + (p22 - p20);
                int gy = (p20 - p00) + 2 * (p21 - p01) + (p22 - p02);
                long gx2 = (long)gx * gx, gy2 = (long)gy * gy;
                long mag2 = gx2 + gy2;

                count++;
                if (mag2 >= thresholdSq)
                {
                    sum += mag2;
                    gx2Sum += gx2;
                    gy2Sum += gy2;
                }
            }
        }
        if (count == 0) return (double.NaN, double.NaN);
        double score = sum / count;
        double anisotropy = gy2Sum != 0 ? gx2Sum / gy2Sum : double.NaN;
        return (score, anisotropy);
    }

    /// <summary>
    /// 1 バンド（TileSize 行の水平帯、または末尾の余り行）分のスレッドローカル累計。
    /// タイル配列はバンドをまたいで同じ添字を共有しない（各バンド＝タイル行が排他）ので、
    /// バンド内はロック無しで加算できる。
    /// </summary>
    private sealed class BandAccumulator
    {
        public double GlobalSum;
        public long GlobalCount;
        public double GlobalGx2;
        public double GlobalGy2;
        public readonly double[]? TileSums;
        public readonly long[]? TileCounts;

        public BandAccumulator(int tilesX, int tilesY)
        {
            if (tilesX > 0 && tilesY > 0)
            {
                TileSums = new double[tilesX * tilesY];
                TileCounts = new long[tilesX * tilesY];
            }
        }
    }
}
