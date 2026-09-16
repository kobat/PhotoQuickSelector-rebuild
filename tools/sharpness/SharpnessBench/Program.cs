using System.Diagnostics;
using System.Globalization;
using PhotoQuickSelector.Core;
using PhotoQuickSelector_App.Controls;

namespace SharpnessBench;

/// <summary>
/// SharpnessAnalyzer（<see cref="SharpnessAnalyzer"/>）を実写真フォルダにかけて、鮮鋭度スコアと
/// 処理時間を TSV へ記録する計測ツール。App 本体には組み込まない（開発専用のオフライン計測）。
/// 使い方・列の意味は tools/sharpness/README.md 参照。
/// </summary>
internal static class Program
{
    // 解凍爆弾ガード用の上限。App 本体の既定（1GB）と同じにして通常画像には影響しない値にする。
    private const long MaxPixelBytes = 1L << 30;

    private static int Main(string[] args)
    {
        Options options;
        try
        {
            options = Options.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine(Options.Usage);
            return 1;
        }

        if (!Directory.Exists(options.Folder))
        {
            Console.Error.WriteLine($"フォルダが見つかりません: {options.Folder}");
            return 1;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(options.OutPath)!);
        var partialPath = options.OutPath + ".partial.tsv";

        // 非再帰・ファイル名の序数ソート。IsSupported が .jpg/.jpeg のみ true を返すため、
        // 同フォルダに混在する RAW（.ARW/.ORF）・動画は自動的にスキップ対象へ回る。
        var allFiles = Directory.GetFiles(options.Folder)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToArray();
        var targetFiles = allFiles.Where(MetadataReader.IsSupported).ToArray();
        int skippedCount = allFiles.Length - targetFiles.Length;

        var rows = new List<Row>(targetFiles.Length);
        byte[]? reusableBuffer = null; // 直前フレームと同寸なら使い回し、確保由来の時間ブレを消す。
        bool warmedUp = false;
        bool warmedUpSubject = false;
        bool warmedUpFit = false;
        bool warmedUpFitSubject = false;
        var warmedUpMetrics = new HashSet<SharpnessMetric>();

        var thresholdOptions = new SharpnessOptions { TileSize = options.TileSize, Threshold = options.Threshold };
        var zeroThresholdOptions = thresholdOptions with { Threshold = 0 };

        using (var partialWriter = new StreamWriter(partialPath, append: false))
        {
            partialWriter.WriteLine(TsvHeader);

            for (int i = 0; i < targetFiles.Length; i++)
            {
                var path = targetFiles[i];
                var row = ProcessFile(
                    path, thresholdOptions, zeroThresholdOptions, options.FitLong, options.FitTile,
                    ref reusableBuffer, ref warmedUp, ref warmedUpSubject, ref warmedUpFit, ref warmedUpFitSubject,
                    warmedUpMetrics);
                rows.Add(row);

                partialWriter.WriteLine(FormatRow(row));
                partialWriter.Flush(); // クラッシュしても途中経過が残るように毎行フラッシュ。

                Console.WriteLine(
                    $"[{i + 1}/{targetFiles.Length}] {row.File}  af_window={FormatDouble(row.AfWindowScore, 1)}  " +
                    $"max_tile={FormatDouble(row.MaxTile, 1)}  analyze_ms={row.AnalyzeMs.ToString("F1", CultureInfo.InvariantCulture)}");
            }
        }

        AssignGroupsAndRelatives(rows, options.GroupSeconds);

        using (var finalWriter = new StreamWriter(options.OutPath, append: false))
        {
            finalWriter.WriteLine(TsvHeader);
            foreach (var row in rows)
                finalWriter.WriteLine(FormatRow(row));
        }
        File.Delete(partialPath);

        PrintSummary(rows, allFiles.Length, skippedCount, options.OutPath);
        return 0;
    }

    /// <summary>1 ファイル分の計測を行う。例外・デコード失敗はスコア列を空のまま行を返す（継続のため）。</summary>
    private static Row ProcessFile(
        string path, SharpnessOptions thresholdOptions, SharpnessOptions zeroThresholdOptions, int fitLong, int fitTile,
        ref byte[]? reusableBuffer, ref bool warmedUp, ref bool warmedUpSubject, ref bool warmedUpFit, ref bool warmedUpFitSubject,
        HashSet<SharpnessMetric> warmedUpMetrics)
    {
        var row = new Row { File = Path.GetFileName(path) };

        ImageMetadata? meta = null;
        try
        {
            var metaSw = Stopwatch.StartNew();
            meta = MetadataReader.Read(path);
            row.MetaMs = metaSw.Elapsed.TotalMilliseconds;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"メタデータ読取失敗: {path}: {ex.Message}");
            row.MetaMs = 0;
            row.TotalMs = row.ReadMs + row.MetaMs + row.DecodeMs + row.AnalyzeMs;
            return row; // カメラ/撮影時刻が無いと group 判定もできないので打ち切り。
        }

        row.Camera = string.IsNullOrEmpty(meta.CameraModel) ? meta.CameraMaker : meta.CameraModel;
        row.Width = meta.Width;
        row.Height = meta.Height;
        row.Orientation = meta.Orientation;
        row.Iso = meta.Iso;
        row.Exposure = meta.ExposureTimeDescription;
        row.FocalMm = meta.FocalLength;
        row.Aperture = meta.Aperture;
        row.HasTaken = meta.TakenDateTimeOffset != DateTimeOffset.MinValue;
        row.Taken = meta.TakenDateTimeOffset;

        try
        {
            var readSw = Stopwatch.StartNew();
            var bytes = File.ReadAllBytes(path);
            row.ReadMs = readSw.Elapsed.TotalMilliseconds;

            var decodeSw = Stopwatch.StartNew();
            byte[]? lastBufferLocal = reusableBuffer;
            var frame = WicPixelDecoder.Decode(bytes, MaxPixelBytes, n =>
            {
                if (lastBufferLocal != null && lastBufferLocal.Length == n) return lastBufferLocal;
                lastBufferLocal = new byte[n];
                return lastBufferLocal;
            });
            reusableBuffer = lastBufferLocal;
            row.DecodeMs = decodeSw.Elapsed.TotalMilliseconds;

            if (frame == null)
            {
                Console.Error.WriteLine($"デコード失敗（解凍爆弾ガード or 非対応）: {path}");
                row.TotalMs = row.ReadMs + row.MetaMs + row.DecodeMs + row.AnalyzeMs;
                return row;
            }

            var afWindow = SharpnessAnalyzer.AfWindowFor(meta, frame.Width, frame.Height);
            row.AfX = afWindow?.X;
            row.AfY = afWindow?.Y;
            row.AfW = afWindow?.Width;
            row.AfH = afWindow?.Height;

            if (!warmedUp)
            {
                // JIT ウォームアップ：計測対象外で 1 回流し、初回行の analyze_ms が突出するのを防ぐ。
                SharpnessAnalyzer.Analyze(frame.Bytes, frame.Width, frame.Height, frame.Width * 4, thresholdOptions, afWindow);
                warmedUp = true;
            }

            var analyzeSw = Stopwatch.StartNew();
            var score = SharpnessAnalyzer.Analyze(frame.Bytes, frame.Width, frame.Height, frame.Width * 4, thresholdOptions, afWindow);
            row.AnalyzeMs = analyzeSw.Elapsed.TotalMilliseconds;

            var analyze0Sw = Stopwatch.StartNew();
            var score0 = SharpnessAnalyzer.Analyze(frame.Bytes, frame.Width, frame.Height, frame.Width * 4, zeroThresholdOptions, afWindow);
            row.Analyze0Ms = analyze0Sw.Elapsed.TotalMilliseconds;

            row.Global = score.Global;
            row.AfWindowScore = score.AfWindow;
            row.MaxTile = score.MaxTile;
            row.MaxTileX = score.MaxTileX;
            row.MaxTileY = score.MaxTileY;
            row.Anisotropy = score.Anisotropy;
            row.AfAnisotropy = score.AfWindowAnisotropy;
            row.Global0 = score0.Global;
            row.AfWindow0 = score0.AfWindow;
            row.MaxTile0 = score0.MaxTile;

            // 比較用の4手法。SharpnessAnalyzer と同じタイルサイズ・AF窓・閾値で計算し、
            // 手法ごとに個別タイミングを取る（初回のみ空打ちして JIT ウォームアップ）。
            int tileSize = thresholdOptions.TileSize, threshold = thresholdOptions.Threshold;
            ComputeMetric(SharpnessMetric.LaplacianVariance, row.Lapv, frame, afWindow, tileSize, threshold, warmedUpMetrics);
            ComputeMetric(SharpnessMetric.Brenner, row.Bren, frame, afWindow, tileSize, threshold, warmedUpMetrics);
            ComputeMetric(SharpnessMetric.Reblur, row.Reblur, frame, afWindow, tileSize, threshold, warmedUpMetrics);
            ComputeMetric(SharpnessMetric.EdgeWidth, row.Edgew, frame, afWindow, tileSize, threshold, warmedUpMetrics);

            // 被写体領域解析（SubjectRegionAnalyzer）。SharpnessAnalyzer と同じタイル/閾値/AF窓で解析する。
            var subjectOptions = new SubjectRegionOptions { TileSize = tileSize, Threshold = threshold };
            if (!warmedUpSubject)
            {
                SubjectRegionAnalyzer.Analyze(frame.Bytes, frame.Width, frame.Height, frame.Width * 4, subjectOptions, afWindow);
                warmedUpSubject = true;
            }
            var subjSw = Stopwatch.StartNew();
            var subjectScore = SubjectRegionAnalyzer.Analyze(frame.Bytes, frame.Width, frame.Height, frame.Width * 4, subjectOptions, afWindow);
            row.Subject.Ms = subjSw.Elapsed.TotalMilliseconds;

            row.Subject.TileCount = subjectScore.TileCount;
            row.Subject.X = subjectScore.Bounds.X;
            row.Subject.Y = subjectScore.Bounds.Y;
            row.Subject.W = subjectScore.Bounds.Width;
            row.Subject.H = subjectScore.Bounds.Height;
            row.Subject.Tenengrad = subjectScore.Tenengrad;
            row.Subject.EdgeDensity = subjectScore.EdgeDensity;
            row.Subject.PerEdgeMag2 = subjectScore.PerEdgeMag2;
            row.Subject.Anisotropy = subjectScore.AnisotropyRatio;
            row.Subject.DominantDeg = subjectScore.DominantGradientDegrees;
            row.Subject.WorstWidth = subjectScore.WorstWidth;
            row.Subject.WorstBin = subjectScore.WorstBin;
            row.Subject.BestWidth = subjectScore.BestWidth;
            row.Subject.WidthRatio = subjectScore.WidthRatio;
            row.Subject.BinMedianWidths = subjectScore.BinMedianWidths;
            row.Subject.AfEdges = subjectScore.AfWindowEdgeCount;
            row.Subject.AfPixels = subjectScore.AfWindowPixelCount;

            // subj_w_rel_extent：被写体の外接矩形の長辺に対する worst 幅の千分率（矩形サイズに対する相対的なボケ幅）。
            if (!double.IsNaN(subjectScore.WorstWidth))
            {
                int maxExtent = Math.Max(subjectScore.Bounds.Width, subjectScore.Bounds.Height);
                if (maxExtent > 0) row.Subject.RelExtent = subjectScore.WorstWidth / maxExtent * 1000.0;
            }

            // フィット表示相当（縮小して見たときにボケが目立つか）の計測ブロック。面積平均縮小＋
            // 同じ手法群を fit 解像度で再計算する。ダウンスケール自体の時間は fit_downscale_ms、
            // 解析（3手法分）は合算して fit_ms に記録し、いずれも total_ms には含めない
            // （ベンチ用の追加計測で通常経路のコストではないため。analyze0_ms と同じ扱い）。
            var fitDownscaleSw = Stopwatch.StartNew();
            (int fw, int fh) = BgraDownscaler.FitSize(frame.Width, frame.Height, fitLong);
            byte[] fitBytes = BgraDownscaler.AreaAverage(frame.Bytes, frame.Width, frame.Height, frame.Width * 4, fw, fh);
            row.Fit.DownscaleMs = fitDownscaleSw.Elapsed.TotalMilliseconds;
            row.Fit.Width = fw;
            row.Fit.Height = fh;

            double fitScale = fw / (double)frame.Width;
            RectI? fitAf = ScaleRect(afWindow, fitScale);
            var fitOptions = new SharpnessOptions { TileSize = fitTile, Threshold = threshold };

            if (!warmedUpFit)
            {
                SharpnessAnalyzer.Analyze(fitBytes, fw, fh, fw * 4, fitOptions, fitAf);
                warmedUpFit = true;
            }
            var fitAnalyzeSw = Stopwatch.StartNew();
            var fitScore = SharpnessAnalyzer.Analyze(fitBytes, fw, fh, fw * 4, fitOptions, fitAf);
            double fitMs = fitAnalyzeSw.Elapsed.TotalMilliseconds;

            row.Fit.Global = fitScore.Global;
            row.Fit.Af = fitScore.AfWindow;
            row.Fit.MaxTile = fitScore.MaxTile;
            row.Fit.MaxTileX = fitScore.MaxTileX;
            row.Fit.MaxTileY = fitScore.MaxTileY;
            row.Fit.Anisotropy = fitScore.Anisotropy;

            // fit_subj_ten：全解像度側で見つけた被写体領域（subjectScore.Bounds）を fit 座標へ縮尺し、
            // その矩形を「AF窓」として渡して fit 画像上の Tenengrad だけを取り出す（被写体タイル探索の
            // やり直しではない）。全解像度側に被写体タイルが無ければ計算しない（NaNのまま）。
            if (subjectScore.TileCount > 0)
            {
                RectI scaledSubjectBounds = ScaleRect(subjectScore.Bounds, fitScale)!.Value;
                var subjTenSw = Stopwatch.StartNew();
                var subjTenScore = SharpnessAnalyzer.Analyze(fitBytes, fw, fh, fw * 4, fitOptions, scaledSubjectBounds);
                fitMs += subjTenSw.Elapsed.TotalMilliseconds;
                row.Fit.SubjectTenengrad = subjTenScore.AfWindow;
            }

            // fit 画像そのものに対する独立の被写体領域解析（全解像度側の被写体タイルとは tile size が
            // 異なるスケールのため、境界が一致するとは限らない＝別物として fit_subj_x/y/w/h 等に記録）。
            var fitSubjectOptions = new SubjectRegionOptions { TileSize = fitTile, Threshold = threshold };
            if (!warmedUpFitSubject)
            {
                SubjectRegionAnalyzer.Analyze(fitBytes, fw, fh, fw * 4, fitSubjectOptions, fitAf);
                warmedUpFitSubject = true;
            }
            var fitSubjSw = Stopwatch.StartNew();
            var fitSubjectScore = SubjectRegionAnalyzer.Analyze(fitBytes, fw, fh, fw * 4, fitSubjectOptions, fitAf);
            fitMs += fitSubjSw.Elapsed.TotalMilliseconds;

            row.Fit.SubjectTiles = fitSubjectScore.TileCount;
            row.Fit.SubjectX = fitSubjectScore.Bounds.X;
            row.Fit.SubjectY = fitSubjectScore.Bounds.Y;
            row.Fit.SubjectW = fitSubjectScore.Bounds.Width;
            row.Fit.SubjectH = fitSubjectScore.Bounds.Height;
            row.Fit.SubjectWorstWidth = fitSubjectScore.WorstWidth;
            row.Fit.SubjectBestWidth = fitSubjectScore.BestWidth;
            row.Fit.SubjectWidthRatio = fitSubjectScore.WidthRatio;
            row.Fit.SubjectAnisotropy = fitSubjectScore.AnisotropyRatio;

            row.Fit.AnalyzeMs = fitMs;
            row.Fit.RatioGlobal = RatioOrNaN(row.Global, row.Fit.Global);
            row.Fit.RatioSubject = RatioOrNaN(row.Subject.Tenengrad, row.Fit.SubjectTenengrad);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"処理失敗: {path}: {ex.Message}");
        }

        // 仕様どおり analyze0_ms は含めない（Threshold=0 再計測はベンチ用の追加計測で、通常経路の
        // コストではないため）。
        row.TotalMs = row.ReadMs + row.MetaMs + row.DecodeMs + row.AnalyzeMs;
        return row;
    }

    /// <summary>
    /// 比較用4手法（<see cref="SharpnessMetric"/>）のうち1つを計算し、時間込みで <paramref name="target"/> へ書く。
    /// 手法ごとに初回だけ空打ちして JIT ウォームアップする（<paramref name="warmedUpMetrics"/> で1回だけに制御）。
    /// </summary>
    private static void ComputeMetric(
        SharpnessMetric metric, MetricRow target, PixelFrame frame, RectI? afWindow,
        int tileSize, int threshold, HashSet<SharpnessMetric> warmedUpMetrics)
    {
        if (warmedUpMetrics.Add(metric))
            SharpnessMetrics.Compute(metric, frame.Bytes, frame.Width, frame.Height, frame.Width * 4, tileSize, afWindow, threshold);

        var sw = Stopwatch.StartNew();
        var score = SharpnessMetrics.Compute(metric, frame.Bytes, frame.Width, frame.Height, frame.Width * 4, tileSize, afWindow, threshold);
        target.Ms = sw.Elapsed.TotalMilliseconds;
        target.Global = score.Global;
        target.Af = score.AfWindow;
        target.MaxTile = score.MaxTile;
        target.MaxTileX = score.MaxTileX;
        target.MaxTileY = score.MaxTileY;
        target.HigherIsSharper = score.HigherIsSharper;
    }

    /// <summary>
    /// 連写グループを割り当て、グループ内相対値（rel_*）を埋める。
    /// グループ境界＝カメラ機種が変わる／直前ファイルの撮影時刻が無い／直前ファイルとの間隔が
    /// group-seconds を超える。加えて（仕様に明記は無いが）当該ファイル自身の撮影時刻が無い場合も
    /// 前後との連続性を判定できないため境界にする（対称性のための拡張）。
    /// </summary>
    private static void AssignGroupsAndRelatives(List<Row> rows, double groupSeconds)
    {
        int groupId = 0;
        Row? prev = null;
        foreach (var row in rows)
        {
            bool newGroup = prev == null
                || !string.Equals(row.Camera, prev.Camera, StringComparison.Ordinal)
                || !prev.HasTaken
                || !row.HasTaken
                || Math.Abs((row.Taken - prev.Taken).TotalSeconds) > groupSeconds;
            if (newGroup) groupId++;
            row.Group = groupId;
            prev = row;
        }

        foreach (var group in rows.GroupBy(r => r.Group))
        {
            double maxGlobal = MaxOrNaN(group.Select(r => r.Global));
            double maxAf = MaxOrNaN(group.Select(r => r.AfWindowScore));
            double maxTile = MaxOrNaN(group.Select(r => r.MaxTile));
            foreach (var row in group)
            {
                row.RelGlobal = RelativeTo(row.Global, maxGlobal);
                row.RelAf = RelativeTo(row.AfWindowScore, maxAf);
                row.RelMaxTile = RelativeTo(row.MaxTile, maxTile);
            }

            // 比較用4手法。LaplacianVariance/Brenner/Reblur は Tenengrad と同じ「値/グループ最大*100」、
            // EdgeWidth のみ値が小さいほど鮮鋭なので「グループ最小/値*100」（どちらも 100=グループ内最鋭）。
            AssignMetricRelatives(group, r => r.Lapv, higherIsSharper: true);
            AssignMetricRelatives(group, r => r.Bren, higherIsSharper: true);
            AssignMetricRelatives(group, r => r.Reblur, higherIsSharper: true);
            AssignMetricRelatives(group, r => r.Edgew, higherIsSharper: false);

            // SubjectRegionAnalyzer：edgew と同じ「値が小さいほど鮮鋭」の向き（グループ最小/値×100）。
            var subjects = group.Select(r => r.Subject).ToArray();
            double minWorst = MinOrNaN(subjects.Select(s => s.WorstWidth));
            double minExtent = MinOrNaN(subjects.Select(s => s.RelExtent));
            foreach (var s in subjects)
            {
                s.RelWorst = RelativeToInverse(s.WorstWidth, minWorst);
                s.RelExtentGroup = RelativeToInverse(s.RelExtent, minExtent);
            }

            // フィット表示相当の相対値。fit_maxtile/fit_subj_ten は Tenengrad 系＝大きいほど鮮鋭
            // （既存の rel_maxtile と同じ向き）、fit_subj_w_worst は edgew と同じ「小さいほど鮮鋭」。
            double maxFitMaxTile = MaxOrNaN(group.Select(r => r.Fit.MaxTile));
            double maxFitSubj = MaxOrNaN(group.Select(r => r.Fit.SubjectTenengrad));
            double minFitSubjWorst = MinOrNaN(group.Select(r => r.Fit.SubjectWorstWidth));
            foreach (var row in group)
            {
                row.Fit.RelMaxTile = RelativeTo(row.Fit.MaxTile, maxFitMaxTile);
                row.Fit.RelSubject = RelativeTo(row.Fit.SubjectTenengrad, maxFitSubj);
                row.Fit.RelSubjectWidth = RelativeToInverse(row.Fit.SubjectWorstWidth, minFitSubjWorst);
            }
        }
    }

    private static void AssignMetricRelatives(IEnumerable<Row> group, Func<Row, MetricRow> selector, bool higherIsSharper)
    {
        var metrics = group.Select(selector).ToArray();
        if (higherIsSharper)
        {
            double maxAf = MaxOrNaN(metrics.Select(m => m.Af));
            double maxTile = MaxOrNaN(metrics.Select(m => m.MaxTile));
            foreach (var m in metrics)
            {
                m.RelAf = RelativeTo(m.Af, maxAf);
                m.RelMaxTile = RelativeTo(m.MaxTile, maxTile);
            }
        }
        else
        {
            double minAf = MinOrNaN(metrics.Select(m => m.Af));
            double minTile = MinOrNaN(metrics.Select(m => m.MaxTile));
            foreach (var m in metrics)
            {
                m.RelAf = RelativeToInverse(m.Af, minAf);
                m.RelMaxTile = RelativeToInverse(m.MaxTile, minTile);
            }
        }
    }

    private static double MaxOrNaN(IEnumerable<double> values)
    {
        double max = double.NaN;
        foreach (var v in values)
        {
            if (double.IsNaN(v)) continue;
            if (double.IsNaN(max) || v > max) max = v;
        }
        return max;
    }

    private static double MinOrNaN(IEnumerable<double> values)
    {
        double min = double.NaN;
        foreach (var v in values)
        {
            if (double.IsNaN(v)) continue;
            if (double.IsNaN(min) || v < min) min = v;
        }
        return min;
    }

    private static double RelativeTo(double value, double groupMax)
        => double.IsNaN(value) || double.IsNaN(groupMax) || groupMax <= 0 ? double.NaN : value / groupMax * 100.0;

    /// <summary>EdgeWidth 用（値が小さいほど鮮鋭）。「グループ最小/値*100」＝100 がグループ内最鋭。</summary>
    private static double RelativeToInverse(double value, double groupMin)
        => double.IsNaN(value) || double.IsNaN(groupMin) || groupMin <= 0 ? double.NaN : groupMin / value * 100.0;

    /// <summary>ratio_* 列用：分子/分母のどちらかが NaN、または分母が 0 なら比は無意味として NaN を返す。</summary>
    private static double RatioOrNaN(double numerator, double denominator)
        => double.IsNaN(numerator) || double.IsNaN(denominator) || denominator == 0 ? double.NaN : numerator / denominator;

    /// <summary>
    /// 矩形（表示px）を <paramref name="scale"/> 倍した矩形を返す（四捨五入・幅高さは最小1pxを維持）。
    /// フィット画像はダウンスケールなので scale&lt;=1 だが、丸めで幅/高さが 0 になり得るため下限を敷く。
    /// </summary>
    private static RectI? ScaleRect(RectI? rect, double scale)
    {
        if (rect is not { } r) return null;
        int x = (int)Math.Round(r.X * scale, MidpointRounding.AwayFromZero);
        int y = (int)Math.Round(r.Y * scale, MidpointRounding.AwayFromZero);
        int w = Math.Max(1, (int)Math.Round(r.Width * scale, MidpointRounding.AwayFromZero));
        int h = Math.Max(1, (int)Math.Round(r.Height * scale, MidpointRounding.AwayFromZero));
        return new RectI(x, y, w, h);
    }

    private const string TsvHeader =
        "file\tgroup\tcamera\twidth\theight\torientation\tiso\texposure\tfocal_mm\taperture\ttaken\t" +
        "af_x\taf_y\taf_w\taf_h\tglobal\taf_window\tmax_tile\tmax_tile_x\tmax_tile_y\tanisotropy\taf_anisotropy\t" +
        "global_t0\taf_window_t0\tmax_tile_t0\trel_global\trel_af\trel_maxtile\t" +
        "read_ms\tmeta_ms\tdecode_ms\tanalyze_ms\tanalyze0_ms\ttotal_ms\t" +
        "lapv_global\tlapv_af\tlapv_maxtile\tlapv_maxtile_x\tlapv_maxtile_y\trel_lapv_af\trel_lapv_maxtile\tlapv_ms\t" +
        "bren_global\tbren_af\tbren_maxtile\tbren_maxtile_x\tbren_maxtile_y\trel_bren_af\trel_bren_maxtile\tbren_ms\t" +
        "reblur_global\treblur_af\treblur_maxtile\treblur_maxtile_x\treblur_maxtile_y\trel_reblur_af\trel_reblur_maxtile\treblur_ms\t" +
        "edgew_global\tedgew_af\tedgew_maxtile\tedgew_maxtile_x\tedgew_maxtile_y\trel_edgew_af\trel_edgew_maxtile\tedgew_ms\t" +
        "subj_tiles\tsubj_x\tsubj_y\tsubj_w\tsubj_h\tsubj_ten\tsubj_edge_density\tsubj_per_edge\tsubj_aniso\tsubj_dir_deg\t" +
        "subj_w_worst\tsubj_w_worst_deg\tsubj_w_best\tsubj_w_ratio\tsubj_w_rel_extent\tsubj_bins\t" +
        "af_edges\taf_edge_density\trel_subj_w\trel_subj_extent\tsubj_ms\t" +
        "fit_w\tfit_h\tfit_global\tfit_af\tfit_maxtile\tfit_maxtile_x\tfit_maxtile_y\tfit_aniso\tfit_subj_ten\t" +
        "fit_subj_tiles\tfit_subj_x\tfit_subj_y\tfit_subj_w\tfit_subj_h\tfit_subj_w_worst\tfit_subj_w_best\tfit_subj_w_ratio\tfit_subj_aniso\t" +
        "ratio_global\tratio_subj\trel_fit_maxtile\trel_fit_subj\trel_fit_subj_w\tfit_downscale_ms\tfit_ms";

    private static string FormatRow(Row r) => string.Join('\t',
        r.File,
        r.Group.ToString(CultureInfo.InvariantCulture),
        r.Camera,
        r.Width.ToString(CultureInfo.InvariantCulture),
        r.Height.ToString(CultureInfo.InvariantCulture),
        r.Orientation.ToString(CultureInfo.InvariantCulture),
        r.Iso.ToString(CultureInfo.InvariantCulture),
        r.Exposure,
        FormatDouble(r.FocalMm, 3),
        FormatDouble(r.Aperture, 3),
        r.HasTaken ? r.Taken.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) : "",
        FormatInt(r.AfX), FormatInt(r.AfY), FormatInt(r.AfW), FormatInt(r.AfH),
        FormatDouble(r.Global, 3), FormatDouble(r.AfWindowScore, 3), FormatDouble(r.MaxTile, 3),
        FormatInt(r.MaxTileX), FormatInt(r.MaxTileY),
        FormatDouble(r.Anisotropy, 3), FormatDouble(r.AfAnisotropy, 3),
        FormatDouble(r.Global0, 3), FormatDouble(r.AfWindow0, 3), FormatDouble(r.MaxTile0, 3),
        FormatDouble(r.RelGlobal, 3), FormatDouble(r.RelAf, 3), FormatDouble(r.RelMaxTile, 3),
        r.ReadMs.ToString("F1", CultureInfo.InvariantCulture),
        r.MetaMs.ToString("F1", CultureInfo.InvariantCulture),
        r.DecodeMs.ToString("F1", CultureInfo.InvariantCulture),
        r.AnalyzeMs.ToString("F1", CultureInfo.InvariantCulture),
        r.Analyze0Ms.ToString("F1", CultureInfo.InvariantCulture),
        r.TotalMs.ToString("F1", CultureInfo.InvariantCulture),
        FormatMetricRow(r.Lapv), FormatMetricRow(r.Bren), FormatMetricRow(r.Reblur), FormatMetricRow(r.Edgew),
        FormatSubjectRow(r.Subject),
        FormatFitRow(r.Fit));

    private static string FormatMetricRow(MetricRow m) => string.Join('\t',
        FormatDouble(m.Global, 3), FormatDouble(m.Af, 3), FormatDouble(m.MaxTile, 3),
        FormatInt(m.MaxTileX), FormatInt(m.MaxTileY),
        FormatDouble(m.RelAf, 3), FormatDouble(m.RelMaxTile, 3),
        m.Ms.ToString("F1", CultureInfo.InvariantCulture));

    private static string FormatSubjectRow(Row.SubjectRow s) => string.Join('\t',
        s.TileCount.ToString(CultureInfo.InvariantCulture),
        s.TileCount > 0 ? s.X.ToString(CultureInfo.InvariantCulture) : "",
        s.TileCount > 0 ? s.Y.ToString(CultureInfo.InvariantCulture) : "",
        s.TileCount > 0 ? s.W.ToString(CultureInfo.InvariantCulture) : "",
        s.TileCount > 0 ? s.H.ToString(CultureInfo.InvariantCulture) : "",
        FormatDouble(s.Tenengrad, 3),
        FormatDouble(s.EdgeDensity, 3),
        FormatDouble(s.PerEdgeMag2, 3),
        FormatDouble(s.Anisotropy, 3),
        FormatDouble(s.DominantDeg, 3),
        FormatDouble(s.WorstWidth, 3),
        FormatDouble(s.WorstBin >= 0 && s.BinMedianWidths != null
            ? SubjectRegionScore.BinCenterDegrees(s.WorstBin, s.BinMedianWidths.Count) : double.NaN, 3),
        FormatDouble(s.BestWidth, 3),
        FormatDouble(s.WidthRatio, 3),
        FormatDouble(s.RelExtent, 3),
        FormatBins(s.BinMedianWidths),
        s.AfEdges.ToString(CultureInfo.InvariantCulture),
        FormatDouble(s.AfPixels > 0 ? (double)s.AfEdges / s.AfPixels : double.NaN, 3),
        FormatDouble(s.RelWorst, 3),
        FormatDouble(s.RelExtentGroup, 3),
        s.Ms.ToString("F1", CultureInfo.InvariantCulture));

    private static string FormatFitRow(Row.FitRow f) => string.Join('\t',
        FormatInt(f.Width), FormatInt(f.Height),
        FormatDouble(f.Global, 3), FormatDouble(f.Af, 3), FormatDouble(f.MaxTile, 3),
        FormatInt(f.MaxTileX), FormatInt(f.MaxTileY),
        FormatDouble(f.Anisotropy, 3),
        FormatDouble(f.SubjectTenengrad, 3),
        f.SubjectTiles.ToString(CultureInfo.InvariantCulture),
        f.SubjectTiles > 0 ? f.SubjectX.ToString(CultureInfo.InvariantCulture) : "",
        f.SubjectTiles > 0 ? f.SubjectY.ToString(CultureInfo.InvariantCulture) : "",
        f.SubjectTiles > 0 ? f.SubjectW.ToString(CultureInfo.InvariantCulture) : "",
        f.SubjectTiles > 0 ? f.SubjectH.ToString(CultureInfo.InvariantCulture) : "",
        FormatDouble(f.SubjectWorstWidth, 3), FormatDouble(f.SubjectBestWidth, 3), FormatDouble(f.SubjectWidthRatio, 3),
        FormatDouble(f.SubjectAnisotropy, 3),
        FormatDouble(f.RatioGlobal, 3), FormatDouble(f.RatioSubject, 3),
        FormatDouble(f.RelMaxTile, 3), FormatDouble(f.RelSubject, 3), FormatDouble(f.RelSubjectWidth, 3),
        f.DownscaleMs.ToString("F1", CultureInfo.InvariantCulture),
        f.AnalyzeMs.ToString("F1", CultureInfo.InvariantCulture));

    /// <summary>方向ビンの中央値を '/' 区切りで並べる（NaN は '-'）。<c>subj_bins</c> 列。</summary>
    private static string FormatBins(IReadOnlyList<double>? medians)
        => medians == null ? "" : string.Join('/', medians.Select(m => double.IsNaN(m) ? "-" : m.ToString("F1", CultureInfo.InvariantCulture)));

    private static string FormatDouble(double v, int decimals)
        => double.IsNaN(v) ? "" : v.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private static string FormatInt(int? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "";

    private static void PrintSummary(List<Row> rows, int totalFiles, int skippedCount, string outPath)
    {
        var decodeMs = rows.Where(r => !double.IsNaN(r.Global)).Select(r => r.DecodeMs).OrderBy(v => v).ToArray();
        var analyzeMs = rows.Where(r => !double.IsNaN(r.Global)).Select(r => r.AnalyzeMs).OrderBy(v => v).ToArray();

        Console.WriteLine();
        Console.WriteLine("=== サマリ ===");
        Console.WriteLine($"対象ファイル数: {rows.Count} / スキップ（非対応拡張子）: {skippedCount} / フォルダ内総数: {totalFiles}");
        Console.WriteLine($"decode_ms: mean={Mean(decodeMs):F1} median={Median(decodeMs):F1} max={(decodeMs.Length > 0 ? decodeMs[^1] : 0):F1}");
        Console.WriteLine($"analyze_ms: mean={Mean(analyzeMs):F1} median={Median(analyzeMs):F1} max={(analyzeMs.Length > 0 ? analyzeMs[^1] : 0):F1}");

        // 比較用4手法：手法ごとの計測時間（成功行のみ。lapv_global 等が NaN な行＝デコード失敗行は除く）。
        (string Prefix, Func<Row, MetricRow> Select)[] metricSelectors =
        {
            ("lapv", r => r.Lapv), ("bren", r => r.Bren), ("reblur", r => r.Reblur), ("edgew", r => r.Edgew),
        };
        foreach (var (prefix, select) in metricSelectors)
        {
            var ms = rows.Select(select).Where(m => !double.IsNaN(m.Global)).Select(m => m.Ms).OrderBy(v => v).ToArray();
            Console.WriteLine($"{prefix}_ms: mean={Mean(ms):F1} median={Median(ms):F1} max={(ms.Length > 0 ? ms[^1] : 0):F1}");
        }

        // フィット表示相当：ダウンスケール自体と解析3手法合計、それぞれの所要時間（成功行のみ）。
        var fitDownscaleMs = rows.Where(r => !double.IsNaN(r.Global)).Select(r => r.Fit.DownscaleMs).OrderBy(v => v).ToArray();
        var fitAnalyzeMs = rows.Where(r => !double.IsNaN(r.Global)).Select(r => r.Fit.AnalyzeMs).OrderBy(v => v).ToArray();
        Console.WriteLine($"fit_downscale_ms: mean={Mean(fitDownscaleMs):F1} median={Median(fitDownscaleMs):F1} max={(fitDownscaleMs.Length > 0 ? fitDownscaleMs[^1] : 0):F1}");
        Console.WriteLine($"fit_ms: mean={Mean(fitAnalyzeMs):F1} median={Median(fitAnalyzeMs):F1} max={(fitAnalyzeMs.Length > 0 ? fitAnalyzeMs[^1] : 0):F1}");

        Console.WriteLine($"マシン: {Environment.MachineName} / 論理プロセッサ数: {Environment.ProcessorCount}");
        Console.WriteLine($"出力: {outPath}");

        Console.WriteLine();
        Console.WriteLine("=== 連写グループ ===");
        foreach (var group in rows.GroupBy(r => r.Group).OrderBy(g => g.Key))
        {
            var members = group.ToArray();
            bool hasAf = members.Any(r => !double.IsNaN(r.RelAf));
            var best = hasAf
                ? members.Where(r => !double.IsNaN(r.RelAf)).OrderByDescending(r => r.RelAf).FirstOrDefault()
                : members.Where(r => !double.IsNaN(r.RelMaxTile)).OrderByDescending(r => r.RelMaxTile).FirstOrDefault();
            var bestDesc = best == null ? "（有効スコアなし）" : $"{best.File}（{(hasAf ? "rel_af" : "rel_maxtile")}={(hasAf ? best.RelAf : best.RelMaxTile):F1}）";

            // SubjectRegionAnalyzer は値が小さいほど鮮鋭（edgew と同じ向き）なので最小値を採る。
            var bestSubj = members.Where(r => !double.IsNaN(r.Subject.WorstWidth)).OrderBy(r => r.Subject.WorstWidth).FirstOrDefault();
            var bestSubjDesc = bestSubj == null ? "（有効値なし）" : $"{bestSubj.File}（subj_w_worst={bestSubj.Subject.WorstWidth:F1}）";
            var bestExtent = members.Where(r => !double.IsNaN(r.Subject.RelExtent)).OrderBy(r => r.Subject.RelExtent).FirstOrDefault();
            var bestExtentDesc = bestExtent == null ? "（有効値なし）" : bestExtent.File;

            // フィット表示相当：fit_subj_ten は Tenengrad系（大きいほど鮮鋭）、fit_subj_w_worst は
            // edgew と同じ向き（小さいほど鮮鋭）。
            var bestFitSubj = members.Where(r => !double.IsNaN(r.Fit.SubjectTenengrad)).OrderByDescending(r => r.Fit.SubjectTenengrad).FirstOrDefault();
            var bestFitSubjDesc = bestFitSubj == null ? "（有効値なし）" : bestFitSubj.File;
            var bestFitW = members.Where(r => !double.IsNaN(r.Fit.SubjectWorstWidth)).OrderBy(r => r.Fit.SubjectWorstWidth).FirstOrDefault();
            var bestFitWDesc = bestFitW == null ? "（有効値なし）" : bestFitW.File;

            Console.WriteLine(
                $"group {group.Key}: {members[0].File}..{members[^1].File} ({members.Length} 枚) best={bestDesc} " +
                $"best_subj={bestSubjDesc} best_extent={bestExtentDesc} best_fit_subj={bestFitSubjDesc} best_fit_w={bestFitWDesc}");
        }
    }

    private static double Mean(double[] values) => values.Length == 0 ? 0 : values.Average();

    private static double Median(double[] values)
    {
        if (values.Length == 0) return 0;
        int mid = values.Length / 2;
        return values.Length % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2.0;
    }
}

/// <summary>コマンドライン引数の解析結果。</summary>
internal sealed class Options
{
    public required string Folder { get; init; }
    public required string OutPath { get; init; }
    public int TileSize { get; init; } = 256;
    public int Threshold { get; init; } = 32;
    public double GroupSeconds { get; init; } = 3;
    public int FitLong { get; init; } = 1600;
    public int FitTile { get; init; } = 64;

    public const string Usage =
        "使い方: SharpnessBench <folder> [--out <file.tsv>] [--tile 256] [--threshold 32] [--group-seconds 3] " +
        "[--fit-long 1600] [--fit-tile 64]";

    public static Options Parse(string[] args)
    {
        string? folder = null;
        string? outPath = null;
        int tile = 256;
        int threshold = 32;
        double groupSeconds = 3;
        int fitLong = 1600;
        int fitTile = 64;

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "--out":
                    outPath = RequireValue(args, ref i, "--out");
                    break;
                case "--tile":
                    tile = int.Parse(RequireValue(args, ref i, "--tile"), CultureInfo.InvariantCulture);
                    break;
                case "--threshold":
                    threshold = int.Parse(RequireValue(args, ref i, "--threshold"), CultureInfo.InvariantCulture);
                    break;
                case "--group-seconds":
                    groupSeconds = double.Parse(RequireValue(args, ref i, "--group-seconds"), CultureInfo.InvariantCulture);
                    break;
                case "--fit-long":
                    fitLong = int.Parse(RequireValue(args, ref i, "--fit-long"), CultureInfo.InvariantCulture);
                    break;
                case "--fit-tile":
                    fitTile = int.Parse(RequireValue(args, ref i, "--fit-tile"), CultureInfo.InvariantCulture);
                    break;
                default:
                    if (a.StartsWith("--", StringComparison.Ordinal))
                        throw new ArgumentException($"未知のオプション: {a}");
                    if (folder != null)
                        throw new ArgumentException($"フォルダ引数が重複しています: {a}");
                    folder = a;
                    break;
            }
        }

        if (folder == null) throw new ArgumentException("フォルダを指定してください。");

        outPath ??= BuildDefaultOutPath(folder);
        return new Options
        {
            Folder = folder, OutPath = outPath, TileSize = tile, Threshold = threshold, GroupSeconds = groupSeconds,
            FitLong = fitLong, FitTile = fitTile,
        };
    }

    private static string RequireValue(string[] args, ref int i, string optionName)
    {
        if (i + 1 >= args.Length) throw new ArgumentException($"{optionName} には値が必要です。");
        return args[++i];
    }

    /// <summary>既定の出力先＝リポジトリ直下（CLAUDE.md のあるフォルダ）基準の tools/sharpness/results/。</summary>
    private static string BuildDefaultOutPath(string folder)
    {
        var repoRoot = FindRepoRoot();
        var leaf = new DirectoryInfo(folder.TrimEnd('\\', '/')).Name;
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return Path.Combine(repoRoot, "tools", "sharpness", "results", $"{leaf}-{timestamp}.tsv");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) return dir.FullName;
            dir = dir.Parent;
        }
        return Directory.GetCurrentDirectory();
    }
}

/// <summary>1 ファイル分の計測結果（TSV の 1 行に対応）。スコア系は未計測なら NaN、時間系は 0。</summary>
internal sealed class Row
{
    public required string File { get; init; }
    public int Group { get; set; }
    public string Camera { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public int Orientation { get; set; }
    public int Iso { get; set; }
    public string Exposure { get; set; } = "";
    public double FocalMm { get; set; }
    public double Aperture { get; set; }
    public DateTimeOffset Taken { get; set; } = DateTimeOffset.MinValue;
    public bool HasTaken { get; set; }

    public int? AfX { get; set; }
    public int? AfY { get; set; }
    public int? AfW { get; set; }
    public int? AfH { get; set; }

    public double Global { get; set; } = double.NaN;
    public double AfWindowScore { get; set; } = double.NaN;
    public double MaxTile { get; set; } = double.NaN;
    public int? MaxTileX { get; set; }
    public int? MaxTileY { get; set; }
    public double Anisotropy { get; set; } = double.NaN;
    public double AfAnisotropy { get; set; } = double.NaN;

    public double Global0 { get; set; } = double.NaN;
    public double AfWindow0 { get; set; } = double.NaN;
    public double MaxTile0 { get; set; } = double.NaN;

    public double RelGlobal { get; set; } = double.NaN;
    public double RelAf { get; set; } = double.NaN;
    public double RelMaxTile { get; set; } = double.NaN;

    public double ReadMs { get; set; }
    public double MetaMs { get; set; }
    public double DecodeMs { get; set; }
    public double AnalyzeMs { get; set; }
    public double Analyze0Ms { get; set; }
    public double TotalMs { get; set; }

    // 比較用4手法（SharpnessMetrics）。列プレフィックスは lapv/bren/reblur/edgew。
    public MetricRow Lapv { get; } = new();
    public MetricRow Bren { get; } = new();
    public MetricRow Reblur { get; } = new();
    public MetricRow Edgew { get; } = new();

    // 被写体領域解析（SubjectRegionAnalyzer）。列プレフィックスは subj。
    public SubjectRow Subject { get; } = new();

    // フィット表示相当（面積平均縮小＋同手法群の再計算）。列プレフィックスは fit / ratio。
    public FitRow Fit { get; } = new();

    /// <summary>
    /// フィット表示相当の計測結果＋所要時間（TSV の <c>fit_*</c>/<c>ratio_*</c> 列に対応）。
    /// <see cref="Program.ProcessFile"/> がデコード成功後にのみ埋める。未計測時の既定値は NaN/null/0。
    /// </summary>
    public sealed class FitRow
    {
        public int? Width;
        public int? Height;
        public double Global = double.NaN;
        public double Af = double.NaN;
        public double MaxTile = double.NaN;
        public int? MaxTileX;
        public int? MaxTileY;
        public double Anisotropy = double.NaN;

        /// <summary>
        /// fit_subj_ten＝全解像度側の被写体領域（<see cref="SubjectRow.Tenengrad"/> の元になった矩形）を
        /// fit 座標へ縮尺し、その矩形内で fit 画像上の Tenengrad を測った値。全解像度側に被写体タイルが
        /// 無ければ（<c>subj_tiles</c>=0）NaN のまま。
        /// </summary>
        public double SubjectTenengrad = double.NaN;

        // fit 画像そのものに対する独立の SubjectRegionAnalyzer 結果（タイルサイズが fit_tile のため、
        // 全解像度側の被写体タイルとは境界が一致するとは限らない）。
        public int SubjectTiles;
        public int SubjectX, SubjectY, SubjectW, SubjectH;
        public double SubjectWorstWidth = double.NaN;
        public double SubjectBestWidth = double.NaN;
        public double SubjectWidthRatio = double.NaN;
        public double SubjectAnisotropy = double.NaN;

        /// <summary>ratio_global＝全解像度 global ÷ fit_global（大きいほど、縮小表示でボケが目立たなくなる度合いが強い）。</summary>
        public double RatioGlobal = double.NaN;

        /// <summary>ratio_subj＝全解像度 subj_ten ÷ fit_subj_ten。</summary>
        public double RatioSubject = double.NaN;

        /// <summary>rel_fit_maxtile＝グループ内相対値（%）。Tenengrad系＝大きいほど鮮鋭（グループ最大値比）。</summary>
        public double RelMaxTile = double.NaN;

        /// <summary>rel_fit_subj＝同上を <see cref="SubjectTenengrad"/> に適用したもの。</summary>
        public double RelSubject = double.NaN;

        /// <summary>rel_fit_subj_w＝グループ内相対値（%）。edgew と同じ向き＝グループ最小/値×100。</summary>
        public double RelSubjectWidth = double.NaN;

        public double DownscaleMs;

        /// <summary>fit_ms＝ダウンスケールを除く解析3手法（Analyze×2＋SubjectRegionAnalyzer×1）の合計所要時間。</summary>
        public double AnalyzeMs;
    }

    /// <summary>1 ファイル分の <see cref="SubjectRegionAnalyzer"/> 結果＋計測時間（TSV の <c>subj_*</c> 列に対応）。</summary>
    public sealed class SubjectRow
    {
        public int TileCount;
        public int X, Y, W, H;
        public double Tenengrad = double.NaN;
        public double EdgeDensity = double.NaN;
        public double PerEdgeMag2 = double.NaN;
        public double Anisotropy = double.NaN;
        public double DominantDeg = double.NaN;
        public double WorstWidth = double.NaN;
        public int WorstBin = -1;
        public double BestWidth = double.NaN;
        public double WidthRatio = double.NaN;

        /// <summary>subj_w_rel_extent＝WorstWidth / max(Bounds.Width, Bounds.Height) × 1000（被写体外接矩形の長辺に対する千分率）。</summary>
        public double RelExtent = double.NaN;
        public IReadOnlyList<double>? BinMedianWidths;
        public long AfEdges;
        public long AfPixels;

        /// <summary>rel_subj_w＝グループ内相対値（%）。edgew と同じ向き＝グループ最小/値×100（小さいほど鮮鋭）。</summary>
        public double RelWorst = double.NaN;

        /// <summary>rel_subj_extent＝同上を <see cref="RelExtent"/> に適用したもの。</summary>
        public double RelExtentGroup = double.NaN;

        public double Ms;
    }
}

/// <summary>1 手法ぶんの領域別スコア＋計測時間（TSV の <c>&lt;prefix&gt;_*</c> 列に対応）。未計測なら NaN。</summary>
internal sealed class MetricRow
{
    public double Global { get; set; } = double.NaN;
    public double Af { get; set; } = double.NaN;
    public double MaxTile { get; set; } = double.NaN;
    public int? MaxTileX { get; set; }
    public int? MaxTileY { get; set; }
    public double RelAf { get; set; } = double.NaN;
    public double RelMaxTile { get; set; } = double.NaN;
    public double Ms { get; set; }
    public bool HigherIsSharper { get; set; }
}
