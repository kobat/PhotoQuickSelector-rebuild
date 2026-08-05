using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace PhotoQuickSelector_App;

/// <summary>
/// メモリログ 1 サンプルに添える、先読みキャッシュ／バッファプール／メモリ掃除係の統計
/// （デバッグ用。<see cref="Controls.PreviewControl.CollectMemoryLogExtras"/> が組み立てる）。
/// </summary>
public readonly struct MemoryLogExtras
{
    public MemoryLogExtras(
        long cacheBytes, int cacheCount, int inflightCount,
        long poolBytes, int poolCount, long poolHit, long poolMiss,
        long discardedBytes, bool janitorDirty)
    {
        CacheBytes = cacheBytes;
        CacheCount = cacheCount;
        InflightCount = inflightCount;
        PoolBytes = poolBytes;
        PoolCount = poolCount;
        PoolHit = poolHit;
        PoolMiss = poolMiss;
        DiscardedBytes = discardedBytes;
        JanitorDirty = janitorDirty;
    }

    /// <summary>先読みキャッシュ（デコード済み PixelFrame）在籍分の合計バイト数。</summary>
    public long CacheBytes { get; }
    /// <summary>先読みキャッシュの在籍件数。</summary>
    public int CacheCount { get; }
    /// <summary>読込中/待機中（inflight）の件数。</summary>
    public int InflightCount { get; }
    /// <summary>バッファプールが現在抱えている合計バイト数。</summary>
    public long PoolBytes { get; }
    /// <summary>バッファプールが現在抱えている本数。</summary>
    public int PoolCount { get; }
    /// <summary>プール Rent の累積ヒット回数（プロセス起動からの累積）。</summary>
    public long PoolHit { get; }
    /// <summary>プール Rent の累積ミス回数（プロセス起動からの累積）。</summary>
    public long PoolMiss { get; }
    /// <summary>
    /// キャッシュ＋プールから捨てられ LOH ゴミ（回収待ち byte[]）になった累積バイト数
    /// （<see cref="Controls.PreviewBitmapCache.DiscardedBytes"/>）。
    /// </summary>
    public long DiscardedBytes { get; }
    /// <summary>メモリ掃除係（<see cref="Controls.MemoryJanitor"/>）が dirty（アイドル完全 GC 待ち）かどうか。</summary>
    public bool JanitorDirty { get; }
}

/// <summary>
/// 操作イベント＋周期メモリサンプルを TSV へ時系列記録するデバッグ用ロガー（<c>Ctrl+Shift+M</c>）。
/// <para>
/// メモリオーバーレイ（<c>M</c>）は目視・スクショでしか推移を追えず、「いつ・何をしたら・どれだけ
/// 増減したか」の因果が残らない。本クラスはそれを TSV に落として後から機械的に分析できるようにする。
/// スキーマ・使い方は docs/HISTORY.md「メモリ時系列ログ」節を参照。
/// </para>
/// <para>
/// <b>WinUI/WinRT 非依存の純 C#</b>（<see cref="Microsoft.UI.Dispatching.DispatcherQueue"/> は使わない）。
/// 250ms 周期のサンプリングはホスト（<see cref="MainPage"/>）が <c>DispatcherQueueTimer</c> で駆動し
/// <see cref="Sample"/> を呼ぶ（このクラス自身はタイマーを持たない）。これにより
/// <c>PhotoQuickSelector.Core.Tests</c> からソースリンクしてテストできる（<see cref="MemoryDiagnostics"/>
/// と同じ方式）。
/// </para>
/// <para>
/// 各記録メソッドは行文字列を組んで <see cref="ConcurrentQueue{T}"/> へ積むだけ（ロックなし・
/// 非記録中は即 return の no-op・任意スレッドから呼んでよい）。書き出しは 500ms 周期の
/// <see cref="Timer"/> がキューを drain して <see cref="StreamWriter"/> へ書く。ライター本体の
/// 開閉／書き込みだけ最小限のロック（<see cref="_writerLock"/>）で保護する。
/// </para>
/// </summary>
public sealed class MemoryLog
{
    /// <summary>アプリ全体で共有する既定インスタンス。テストは <c>new MemoryLog()</c> で独立させる。</summary>
    public static MemoryLog Current { get; } = new();

    /// <summary>キュー掃き出し（ファイル書き込み）の周期。表示更新ではなく永続化なので短め固定。</summary>
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// ホスト（<see cref="MainPage"/>）が <see cref="Sample"/> を呼ぶ想定周期。実際に駆動するのは
    /// ホスト側のタイマーで、この値はヘッダに記す表示専用の定数（このクラス自身は周期を強制しない）。
    /// </summary>
    private const int SampleIntervalMs = 250;

    private readonly ConcurrentQueue<string> _queue = new();
    private readonly object _lifecycleLock = new();
    private readonly object _writerLock = new();

    private StreamWriter? _writer;
    private Timer? _flushTimer;
    private Stopwatch? _stopwatch;
    private volatile bool _isRecording;

    /// <summary>記録中かどうか。</summary>
    public bool IsRecording => _isRecording;

    /// <summary>
    /// 記録中（または直近に記録した）ファイルのフルパス。一度も <see cref="Start"/> していなければ null。
    /// <see cref="Stop"/> 後もクリアしない（直近ログの場所を UI から参照できるように）。
    /// </summary>
    public string? CurrentFilePath { get; private set; }

    private long ElapsedMs => _stopwatch?.ElapsedMilliseconds ?? 0;

    /// <summary>
    /// <paramref name="logDirectory"/> 配下に <c>memlog_yyyyMMdd_HHmmss.tsv</c> を作成し記録を開始する。
    /// 既に記録中なら no-op（多重開始で複数ファイルへ分裂させない）。
    /// </summary>
    public void Start(string logDirectory)
    {
        lock (_lifecycleLock)
        {
            if (_isRecording) return;

            Directory.CreateDirectory(logDirectory);
            var path = Path.Combine(logDirectory, $"memlog_{DateTime.Now:yyyyMMdd_HHmmss}.tsv");
            // BOM なし UTF-8。他のログ/TSV 出力と同じく、Excel の自動判定より grep/awk での扱いやすさを優先。
            var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = false,
            };
            WriteHeader(writer);
            writer.Flush();

            _writer = writer;
            CurrentFilePath = path;
            _stopwatch = Stopwatch.StartNew();
            _isRecording = true;

            _flushTimer = new Timer(_ => Drain(), null, FlushInterval, FlushInterval);
        }

        Note("rec-start");
    }

    /// <summary>記録を終了する。キューを掃き切って flush・クローズする。以後の記録呼び出しは no-op。</summary>
    public void Stop()
    {
        if (!_isRecording) return;

        // ロック外で積む（Note 自体は _isRecording を見て動く no-op ガード付きなので、
        // まだ true のこの時点で呼ばないと "rec-stop" 自身が捨てられる）。
        Note("rec-stop");

        lock (_lifecycleLock)
        {
            if (!_isRecording) return; // 二重 Stop 防止

            _isRecording = false;
            _flushTimer?.Dispose();
            _flushTimer = null;

            Drain();

            lock (_writerLock)
            {
                _writer?.Flush();
                _writer?.Dispose();
                _writer = null;
            }

            _stopwatch = null;
        }
    }

    /// <summary>250ms 周期のメモリサンプル 1 行を積む（ホスト側タイマーから呼ぶ）。</summary>
    public void Sample(MemorySnapshot s, in MemoryLogExtras extras)
    {
        if (!_isRecording) return;

        int gen0 = GC.CollectionCount(0);
        int gen1 = GC.CollectionCount(1);
        int gen2 = GC.CollectionCount(2);

        // GenerationInfo[3]（LOH）/[4]（POH）は GCMemoryInfo の仕様上「直前の GC 完了時点」の
        // スナップショットで現在値ではない。GC が走らない間は更新されないため、プールミス時の
        // LOH 変動仮説を検証する際はこの前提込みで読むこと（docs/HISTORY.md「メモリ時系列ログ」節）。
        var genInfo = GC.GetGCMemoryInfo().GenerationInfo;
        long lohSize = genInfo.Length > 3 ? genInfo[3].SizeAfterBytes : 0;
        long lohFrag = genInfo.Length > 3 ? genInfo[3].FragmentationAfterBytes : 0;
        long pohSize = genInfo.Length > 4 ? genInfo[4].SizeAfterBytes : 0;

        // 累積アロケーションバイト（現在値・単調増加）。サンプル間の差分がアロケーションレートになり、
        // 「GC 間の WS 増加＝新規確保の積み上がりか・真のネイティブ増か」を機械的に切り分けられる。
        long allocated = GC.GetTotalAllocatedBytes(precise: false);

        Enqueue(FormatSample(ElapsedMs, s, gen0, gen1, gen2, lohSize, lohFrag, allocated, pohSize, extras));
    }

    /// <summary>写真切替（<c>FocusedPhoto</c> 変更）を記録する。</summary>
    public void Nav(string fileName, int width, int height)
    {
        if (!_isRecording) return;
        Enqueue(FormatNav(ElapsedMs, fileName, width, height));
    }

    /// <summary>
    /// プレビューのフルデコード完了を記録する（<see cref="Controls.PreviewBitmapCache"/> から）。
    /// <paramref name="fileBytes"/> は読み込んだ圧縮ファイルのバイト数（読み捨て byte[] の LOH 負荷を
    /// 定量するための列。ピクセルの <paramref name="bytes"/> と別枠）。
    /// </summary>
    public void DecodeDone(string fileName, long bytes, double elapsedMs, bool poolHit, long fileBytes)
    {
        if (!_isRecording) return;
        Enqueue(FormatDecode(ElapsedMs, fileName, bytes, elapsedMs, poolHit, fileBytes));
    }

    /// <summary>キャッシュ/プールからの破棄（LOH ゴミ化）を記録する。</summary>
    public void Discarded(long deltaBytes)
    {
        if (!_isRecording) return;
        Enqueue(FormatDiscard(ElapsedMs, deltaBytes));
    }

    /// <summary>
    /// GC 実行を記録する。<paramref name="kind"/> は <c>ctrlm</c>（Ctrl+M の強制フル GC）／
    /// <c>idle</c>（メモリ掃除係のアイドル完全 GC）／<c>bgc</c>（メモリ掃除係の背景 gen2 GC）／
    /// <c>agr</c>（メモリ掃除係のブロッキング gen2 GC＝Aggressive・OS への返却込み。
    /// 旧ログの <c>blk</c>＝素の Forced 段は 2026-08-06 に廃止）。
    /// <c>bgc</c> は非ブロッキング発行で前後値を取らないため <paramref name="after"/> は null でよい
    /// （出力では after 側の 3 列を空文字にする）。
    /// </summary>
    public void Gc(string kind, MemorySnapshot before, MemorySnapshot? after, double elapsedMs)
    {
        if (!_isRecording) return;
        Enqueue(FormatGc(ElapsedMs, kind, elapsedMs, before, after));
    }

    /// <summary>
    /// 任意のマーカーイベントを記録する（<c>rec-start</c>/<c>rec-stop</c>/<c>folder</c>/
    /// <c>preview-enter</c>/<c>preview-exit</c> 等）。
    /// </summary>
    public void Note(string kind, string detail = "")
    {
        if (!_isRecording) return;
        Enqueue(FormatNote(ElapsedMs, kind, detail));
    }

    private void Enqueue(string line) => _queue.Enqueue(line);

    /// <summary>キューを掃き切って <see cref="_writer"/> へ書く（500ms 周期タイマー／<see cref="Stop"/> から呼ぶ）。</summary>
    private void Drain()
    {
        if (_queue.IsEmpty) return;

        lock (_writerLock)
        {
            if (_writer is not { } writer) return; // Stop 済み（タイマー破棄とのわずかな競合はここで吸収する）

            bool wrote = false;
            while (_queue.TryDequeue(out var line))
            {
                writer.WriteLine(line);
                wrote = true;
            }
            if (wrote) writer.Flush();
        }
    }

    private static void WriteHeader(StreamWriter writer)
    {
        var version = GetAppVersionText();
        var config =
#if DEBUG
            "Debug";
#else
            "Release";
#endif
        writer.WriteLine(
            $"# app=PhotoQuickSelector version={version} build={config} start={DateTime.Now:yyyy-MM-dd HH:mm:ss} sampleIntervalMs={SampleIntervalMs}");
    }

    /// <summary>
    /// アセンブリのバージョンを "x.y.z"（開発中は "x.y.z-dev"）形式で返す。
    /// <see cref="Controls.AboutDialog"/> の取得方法（csproj の &lt;Version&gt; を単一情報源とする）と同じ。
    /// </summary>
    private static string GetAppVersionText()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus < 0 ? informational : informational[..plus];
        }

        var version = assembly.GetName().Version;
        return version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    // --- 行フォーマット（static な純関数。ホストが積む行と同じものをテストが直接検証できるよう internal で公開） ---

    /// <summary>
    /// SAMPLE 行を組み立てる（列は docs/HISTORY.md「メモリ時系列ログ」節のスキーマ参照）。
    /// 後から足した列（allocatedBytes/pohSize）は既存 TSV のパーサを壊さないよう末尾へ追加する。
    /// </summary>
    internal static string FormatSample(
        long elapsedMs, MemorySnapshot s, int gen0, int gen1, int gen2, long lohSize, long lohFrag,
        long allocatedBytes, long pohSize, in MemoryLogExtras extras) => string.Join('\t',
            elapsedMs, "SAMPLE",
            s.ManagedBytes, s.CommittedBytes, s.PrivateBytes, s.WorkingSetBytes, s.NativeBytes,
            gen0, gen1, gen2, lohSize, lohFrag,
            extras.CacheBytes, extras.CacheCount, extras.InflightCount,
            extras.PoolBytes, extras.PoolCount, extras.PoolHit, extras.PoolMiss,
            extras.DiscardedBytes, extras.JanitorDirty ? 1 : 0,
            allocatedBytes, pohSize);

    /// <summary>NAV 行を組み立てる。</summary>
    internal static string FormatNav(long elapsedMs, string fileName, int width, int height) =>
        string.Join('\t', elapsedMs, "NAV", fileName, width, height);

    /// <summary>DECODE 行を組み立てる（fileBytes は後付け列のため末尾）。</summary>
    internal static string FormatDecode(long elapsedMs, string fileName, long bytes, double decodeElapsedMs, bool poolHit, long fileBytes) =>
        string.Join('\t', elapsedMs, "DECODE", fileName, bytes,
            decodeElapsedMs.ToString("0.###", CultureInfo.InvariantCulture), poolHit ? 1 : 0, fileBytes);

    /// <summary>DISCARD 行を組み立てる。</summary>
    internal static string FormatDiscard(long elapsedMs, long deltaBytes) =>
        string.Join('\t', elapsedMs, "DISCARD", deltaBytes);

    /// <summary>GC 行を組み立てる。<paramref name="after"/> が null（bgc）なら after 側 3 列は空文字にする。</summary>
    internal static string FormatGc(long elapsedMs, string kind, double gcElapsedMs, MemorySnapshot before, MemorySnapshot? after)
    {
        string afterManaged = "", afterNative = "", afterWs = "";
        if (after is { } a)
        {
            afterManaged = a.ManagedBytes.ToString(CultureInfo.InvariantCulture);
            afterNative = a.NativeBytes.ToString(CultureInfo.InvariantCulture);
            afterWs = a.WorkingSetBytes.ToString(CultureInfo.InvariantCulture);
        }
        return string.Join('\t', elapsedMs, "GC", kind,
            gcElapsedMs.ToString("0.###", CultureInfo.InvariantCulture),
            before.ManagedBytes, before.NativeBytes, before.WorkingSetBytes,
            afterManaged, afterNative, afterWs);
    }

    /// <summary>NOTE 行を組み立てる。</summary>
    internal static string FormatNote(long elapsedMs, string kind, string detail) =>
        string.Join('\t', elapsedMs, "NOTE", kind, detail);
}
