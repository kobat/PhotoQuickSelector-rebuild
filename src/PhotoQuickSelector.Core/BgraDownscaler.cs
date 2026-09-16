using System.Buffers;
using System.Threading.Tasks;

namespace PhotoQuickSelector.Core;

/// <summary>
/// BGRA8 バッファの面積平均（area-average）縮小。フィット表示相当（長辺を固定 px に縮小した見え方）
/// でボケが目立つかを確認するため、鮮鋭度解析（<see cref="SharpnessAnalyzer"/>・
/// <see cref="SubjectRegionAnalyzer"/>）の入力を作る。UI・デコード非依存の純粋計算のみ。
/// </summary>
public static class BgraDownscaler
{
    /// <summary>
    /// 長辺を <paramref name="longEdge"/> に揃えた縮小後サイズを、アスペクト比を保って求める
    /// （短辺は四捨五入・最小 1px）。長辺が既に <paramref name="longEdge"/> 以下なら拡大はせず、
    /// 元サイズをそのまま返す（フィット表示は「収まるように縮小する」用途であり、
    /// 小さい画像を無理に拡大する意味が無いため）。
    /// </summary>
    /// <exception cref="ArgumentException">srcWidth/srcHeight/longEdge が 1 未満の場合。</exception>
    public static (int Width, int Height) FitSize(int srcWidth, int srcHeight, int longEdge)
    {
        if (srcWidth < 1) throw new ArgumentException("srcWidth は 1 以上である必要がある。", nameof(srcWidth));
        if (srcHeight < 1) throw new ArgumentException("srcHeight は 1 以上である必要がある。", nameof(srcHeight));
        if (longEdge < 1) throw new ArgumentException("longEdge は 1 以上である必要がある。", nameof(longEdge));

        int srcLong = Math.Max(srcWidth, srcHeight);
        if (srcLong <= longEdge) return (srcWidth, srcHeight); // 既に長辺以下＝拡大しない

        double scale = longEdge / (double)srcLong;
        if (srcWidth >= srcHeight)
            return (longEdge, Math.Max(1, (int)Math.Round(srcHeight * scale, MidpointRounding.AwayFromZero)));
        return (Math.Max(1, (int)Math.Round(srcWidth * scale, MidpointRounding.AwayFromZero)), longEdge);
    }

    /// <summary>
    /// BGRA8 バッファを面積平均で <paramref name="dstWidth"/>×<paramref name="dstHeight"/> へ縮小する
    /// （OpenCV の <c>INTER_AREA</c> 相当。整数比に限らず任意比率で、区間の境界にまたがる元画素は
    /// 重なり量で重み付けする）。返り値は行間パディング無し（stride=dstWidth*4）の BGRA8 バッファ。
    /// B/G/R は独立に平均し、A は常に 255 とする（入力の alpha は使わない。
    /// <see cref="SharpnessAnalyzer.BuildLuminancePlane"/> と同じ「alpha=255 前提」の踏襲）。
    /// </summary>
    /// <param name="bgra">1 画素 4 byte（B,G,R,A の順）のバッファ。<paramref name="srcStride"/> 行間隔で読む。</param>
    /// <param name="srcWidth">元画像幅 px。</param>
    /// <param name="srcHeight">元画像高さ px。</param>
    /// <param name="srcStride">元画像 1 行のバイト数。行末パディングを許容するため srcWidth*4 以上が必須。</param>
    /// <param name="dstWidth">縮小後の幅 px。1 以上・<paramref name="srcWidth"/> 以下（拡大は非対応）。</param>
    /// <param name="dstHeight">縮小後の高さ px。1 以上・<paramref name="srcHeight"/> 以下。</param>
    /// <exception cref="ArgumentException">
    /// srcWidth/srcHeight が負・srcStride が srcWidth*4 未満・bgra が srcStride*srcHeight に満たない、
    /// または dstWidth/dstHeight が 1 未満か srcWidth/srcHeight を超える場合。
    /// </exception>
    /// <remarks>
    /// 決定性：<see cref="Parallel.For"/> は縮小後の行単位で分割し、各行は自分の出力位置にしか書き込まない
    /// （読み取り元は事前に配列へコピーして固定するため、複数スレッドが同じアキュムレータへ加算することが無い）。
    /// よってスケジューリングに関わらず、同一入力からは常にビット同一の結果になる。
    /// <c>ReadOnlySpan&lt;byte&gt;</c> は ref struct のため <see cref="Parallel.For"/> のラムダへ直接キャプチャできない
    /// （コンパイルエラー）。<see cref="SharpnessAnalyzer"/> の輝度平面と同様、<see cref="ArrayPool{T}"/> 経由の
    /// 配列へコピーしてから並列処理する。
    /// </remarks>
    public static byte[] AreaAverage(
        ReadOnlySpan<byte> bgra, int srcWidth, int srcHeight, int srcStride, int dstWidth, int dstHeight)
    {
        if (srcWidth < 0) throw new ArgumentException("srcWidth は 0 以上である必要がある。", nameof(srcWidth));
        if (srcHeight < 0) throw new ArgumentException("srcHeight は 0 以上である必要がある。", nameof(srcHeight));
        if (srcStride < srcWidth * 4) throw new ArgumentException("srcStride は srcWidth*4 以上である必要がある。", nameof(srcStride));
        if (bgra.Length < (long)srcStride * srcHeight)
            throw new ArgumentException("bgra の長さが srcStride*srcHeight に満たない。", nameof(bgra));
        if (dstWidth < 1 || dstWidth > srcWidth)
            throw new ArgumentException("dstWidth は 1 以上 srcWidth 以下である必要がある。", nameof(dstWidth));
        if (dstHeight < 1 || dstHeight > srcHeight)
            throw new ArgumentException("dstHeight は 1 以上 srcHeight 以下である必要がある。", nameof(dstHeight));

        int dstStride = dstWidth * 4;
        if (dstWidth == srcWidth && dstHeight == srcHeight)
            return PackedCopy(bgra, srcWidth, srcHeight, srcStride, dstStride);

        var colCov = BuildAxis(srcWidth, dstWidth);
        var rowCov = BuildAxis(srcHeight, dstHeight);

        int srcLen = srcStride * srcHeight;
        byte[] srcBuf = ArrayPool<byte>.Shared.Rent(srcLen);
        try
        {
            bgra.Slice(0, srcLen).CopyTo(srcBuf);
            var dst = new byte[dstStride * dstHeight];

            Parallel.For(0, dstHeight, ry =>
            {
                int rs = rowCov.Start[ry], re = rowCov.End[ry];
                double rowTotal = rowCov.TotalWeight[ry];
                int outRowOffset = ry * dstStride;

                for (int cx = 0; cx < dstWidth; cx++)
                {
                    int cs = colCov.Start[cx], ce = colCov.End[cx];
                    double norm = rowTotal * colCov.TotalWeight[cx];
                    double sumB = 0, sumG = 0, sumR = 0;

                    for (int sy = rs; sy <= re; sy++)
                    {
                        double wy = sy == rs ? rowCov.StartWeight[ry] : sy == re ? rowCov.EndWeight[ry] : 1.0;
                        int rowBase = sy * srcStride;
                        for (int sx = cs; sx <= ce; sx++)
                        {
                            double w = sx == cs ? colCov.StartWeight[cx] * wy : sx == ce ? colCov.EndWeight[cx] * wy : wy;
                            int idx = rowBase + sx * 4;
                            sumB += srcBuf[idx] * w;
                            sumG += srcBuf[idx + 1] * w;
                            sumR += srcBuf[idx + 2] * w;
                        }
                    }

                    int outIdx = outRowOffset + cx * 4;
                    dst[outIdx] = RoundToByte(sumB / norm);
                    dst[outIdx + 1] = RoundToByte(sumG / norm);
                    dst[outIdx + 2] = RoundToByte(sumR / norm);
                    dst[outIdx + 3] = 255;
                }
            });

            return dst;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(srcBuf);
        }
    }

    /// <summary>dst==src のときの高速経路（重み計算をせず単純に詰め直すだけ）。alpha は 255 に固定する。</summary>
    private static byte[] PackedCopy(ReadOnlySpan<byte> bgra, int width, int height, int srcStride, int dstStride)
    {
        var dst = new byte[dstStride * height];
        for (int y = 0; y < height; y++)
        {
            int srcRowBase = y * srcStride;
            int dstRowBase = y * dstStride;
            for (int x = 0; x < width; x++)
            {
                int si = srcRowBase + x * 4;
                int di = dstRowBase + x * 4;
                dst[di] = bgra[si];
                dst[di + 1] = bgra[si + 1];
                dst[di + 2] = bgra[si + 2];
                dst[di + 3] = 255;
            }
        }
        return dst;
    }

    /// <summary>
    /// 四捨五入して byte へ丸める（0..255 にクランプ）。0.5 は絶対値方向へ丸める
    /// （<see cref="MidpointRounding.AwayFromZero"/>。BGRA 値は非負のため実質「0.5 以上は切り上げ」）。
    /// </summary>
    private static byte RoundToByte(double v) => (byte)Math.Clamp(Math.Round(v, MidpointRounding.AwayFromZero), 0, 255);

    /// <summary>1 軸ぶんの被覆表（縮小後の各座標が元座標のどの範囲を、どの重みでカバーするか）。</summary>
    private readonly struct AxisCoverage
    {
        public readonly int[] Start;
        public readonly int[] End;
        public readonly double[] StartWeight;
        public readonly double[] EndWeight;
        public readonly double[] TotalWeight;

        public AxisCoverage(int[] start, int[] end, double[] startWeight, double[] endWeight, double[] totalWeight)
        {
            Start = start;
            End = end;
            StartWeight = startWeight;
            EndWeight = endWeight;
            TotalWeight = totalWeight;
        }
    }

    /// <summary>
    /// 1 軸ぶんの被覆表を作る。縮小後座標 j は元座標区間 [j*scale, (j+1)*scale)（scale=srcSize/dstSize≥1）を
    /// カバーする。区間の両端にまたがる元画素は重なり量、内側の元画素は重み 1 として扱う
    /// （OpenCV の <c>INTER_AREA</c> と同じ考え方）。<see cref="AxisCoverage.TotalWeight"/> は正規化用の
    /// 区間長で、固定の <c>scale</c> ではなく実際の重み和（right-left）を使う（末尾列は浮動小数点誤差を
    /// 避けるため right を <paramref name="srcSize"/> に丸めており、scale と一致しない場合があるため）。
    /// </summary>
    private static AxisCoverage BuildAxis(int srcSize, int dstSize)
    {
        var start = new int[dstSize];
        var end = new int[dstSize];
        var startWeight = new double[dstSize];
        var endWeight = new double[dstSize];
        var totalWeight = new double[dstSize];
        double scale = srcSize / (double)dstSize;

        for (int j = 0; j < dstSize; j++)
        {
            double left = j * scale;
            double right = j + 1 == dstSize ? srcSize : (j + 1) * scale; // 末尾だけ厳密値にして誤差の持ち越しを断つ
            int s = Math.Clamp((int)Math.Floor(left), 0, srcSize - 1);
            int e = Math.Clamp((int)Math.Ceiling(right) - 1, s, srcSize - 1);

            start[j] = s;
            end[j] = e;
            totalWeight[j] = right - left;
            if (s == e)
            {
                startWeight[j] = right - left;
                endWeight[j] = 0;
            }
            else
            {
                startWeight[j] = (s + 1) - left;
                endWeight[j] = right - e;
            }
        }

        return new AxisCoverage(start, end, startWeight, endWeight, totalWeight);
    }
}
