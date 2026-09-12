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
        var warmedUpMetrics = new HashSet<SharpnessMetric>();

        var thresholdOptions = new SharpnessOptions { TileSize = options.TileSize, Threshold = options.Threshold };
        var zeroThresholdOptions = thresholdOptions with { Threshold = 0 };

        using (var partialWriter = new StreamWriter(partialPath, append: false))
        {
            partialWriter.WriteLine(TsvHeader);

            for (int i = 0; i < targetFiles.Length; i++)
            {
                var path = targetFiles[i];
                var row = ProcessFile(path, thresholdOptions, zeroThresholdOptions, ref reusableBuffer, ref warmedUp, warmedUpMetrics);
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
        string path, SharpnessOptions thresholdOptions, SharpnessOptions zeroThresholdOptions,
        ref byte[]? reusableBuffer, ref bool warmedUp, HashSet<SharpnessMetric> warmedUpMetrics)
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

    private const string TsvHeader =
        "file\tgroup\tcamera\twidth\theight\torientation\tiso\texposure\tfocal_mm\taperture\ttaken\t" +
        "af_x\taf_y\taf_w\taf_h\tglobal\taf_window\tmax_tile\tmax_tile_x\tmax_tile_y\tanisotropy\taf_anisotropy\t" +
        "global_t0\taf_window_t0\tmax_tile_t0\trel_global\trel_af\trel_maxtile\t" +
        "read_ms\tmeta_ms\tdecode_ms\tanalyze_ms\tanalyze0_ms\ttotal_ms\t" +
        "lapv_global\tlapv_af\tlapv_maxtile\tlapv_maxtile_x\tlapv_maxtile_y\trel_lapv_af\trel_lapv_maxtile\tlapv_ms\t" +
        "bren_global\tbren_af\tbren_maxtile\tbren_maxtile_x\tbren_maxtile_y\trel_bren_af\trel_bren_maxtile\tbren_ms\t" +
        "reblur_global\treblur_af\treblur_maxtile\treblur_maxtile_x\treblur_maxtile_y\trel_reblur_af\trel_reblur_maxtile\treblur_ms\t" +
        "edgew_global\tedgew_af\tedgew_maxtile\tedgew_maxtile_x\tedgew_maxtile_y\trel_edgew_af\trel_edgew_maxtile\tedgew_ms";

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
        FormatMetricRow(r.Lapv), FormatMetricRow(r.Bren), FormatMetricRow(r.Reblur), FormatMetricRow(r.Edgew));

    private static string FormatMetricRow(MetricRow m) => string.Join('\t',
        FormatDouble(m.Global, 3), FormatDouble(m.Af, 3), FormatDouble(m.MaxTile, 3),
        FormatInt(m.MaxTileX), FormatInt(m.MaxTileY),
        FormatDouble(m.RelAf, 3), FormatDouble(m.RelMaxTile, 3),
        m.Ms.ToString("F1", CultureInfo.InvariantCulture));

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
            Console.WriteLine($"group {group.Key}: {members[0].File}..{members[^1].File} ({members.Length} 枚) best={bestDesc}");
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

    public const string Usage =
        "使い方: SharpnessBench <folder> [--out <file.tsv>] [--tile 256] [--threshold 32] [--group-seconds 3]";

    public static Options Parse(string[] args)
    {
        string? folder = null;
        string? outPath = null;
        int tile = 256;
        int threshold = 32;
        double groupSeconds = 3;

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
        return new Options { Folder = folder, OutPath = outPath, TileSize = tile, Threshold = threshold, GroupSeconds = groupSeconds };
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
