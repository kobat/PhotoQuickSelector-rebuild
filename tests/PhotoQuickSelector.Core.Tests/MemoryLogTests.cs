using System;
using System.IO;
using System.Threading;
using PhotoQuickSelector_App;
using Xunit;

namespace PhotoQuickSelector.Core.Tests;

/// <summary>
/// <see cref="MemoryLog"/>（メモリ時系列ログ・Ctrl+Shift+M）の単体テスト。
/// 行フォーマットの純関数（各レコード種別・タブ区切り・GC の bgc 空フィールド）と、
/// Start/Stop のライフサイクル（ファイル作成・ヘッダ・停止後の no-op 化）が対象。
/// </summary>
public class MemoryLogTests
{
    private static MemorySnapshot Snapshot(long managed, long committed, long priv, long ws) =>
        new(managed, committed, priv, ws);

    // --- 行フォーマット（static な純関数） ---

    [Fact]
    public void FormatSample_JoinsAllColumnsWithTabs_InSchemaOrder()
    {
        // native = private - max(committed, managed) = 400 - 150 = 250
        var s = Snapshot(managed: 100, committed: 150, priv: 400, ws: 380);
        var extras = new MemoryLogExtras(
            cacheBytes: 900, cacheCount: 3, inflightCount: 1,
            poolBytes: 200, poolCount: 1, poolHit: 5, poolMiss: 2,
            discardedBytes: 700, janitorDirty: true);

        var line = MemoryLog.FormatSample(1234, s, gen0: 10, gen1: 3, gen2: 1, lohSize: 5000, lohFrag: 100,
            allocatedBytes: 88888, pohSize: 640, extras);

        Assert.Equal(
            "1234\tSAMPLE\t100\t150\t400\t380\t250\t10\t3\t1\t5000\t100\t900\t3\t1\t200\t1\t5\t2\t700\t1\t88888\t640",
            line);
    }

    [Fact]
    public void FormatSample_JanitorNotDirty_EncodesDirtyAsZero()
    {
        var s = Snapshot(0, 0, 0, 0);
        var extras = new MemoryLogExtras(0, 0, 0, 0, 0, 0, 0, 0, janitorDirty: false);

        var line = MemoryLog.FormatSample(0, s, 0, 0, 0, 0, 0, 0, 0, extras);

        // janitorDirty は末尾ではなく allocatedBytes/pohSize の前（後付け列は末尾追加の方針）。
        Assert.EndsWith("\t0\t0\t0", line);
    }

    [Fact]
    public void FormatNav_JoinsColumns()
    {
        var line = MemoryLog.FormatNav(500, "DSC01234.JPG", 8640, 5760);
        Assert.Equal("500\tNAV\tDSC01234.JPG\t8640\t5760", line);
    }

    [Fact]
    public void FormatDecode_EncodesPoolHitAsOneOrZero_AndAppendsFileBytes()
    {
        var hit = MemoryLog.FormatDecode(10, "a.jpg", 199_000_000, 42.5, poolHit: true, fileBytes: 21_000_000);
        var miss = MemoryLog.FormatDecode(10, "a.jpg", 199_000_000, 42.5, poolHit: false, fileBytes: 21_000_000);

        Assert.Equal("10\tDECODE\ta.jpg\t199000000\t42.5\t1\t21000000", hit);
        Assert.Equal("10\tDECODE\ta.jpg\t199000000\t42.5\t0\t21000000", miss);
    }

    [Fact]
    public void FormatDiscard_JoinsColumns()
    {
        Assert.Equal("77\tDISCARD\t12345", MemoryLog.FormatDiscard(77, 12345));
    }

    [Fact]
    public void FormatGc_WithAfter_IncludesAfterColumns()
    {
        var before = Snapshot(100, 150, 400, 380); // native = 250
        var after = Snapshot(50, 60, 120, 110);     // native = 60

        var line = MemoryLog.FormatGc(999, "ctrlm", 12.5, before, after);

        Assert.Equal("999\tGC\tctrlm\t12.5\t100\t250\t380\t50\t60\t110", line);
    }

    [Fact]
    public void FormatGc_BgcWithoutAfter_LeavesAfterColumnsEmpty()
    {
        var before = Snapshot(100, 150, 400, 380); // native = 250

        var line = MemoryLog.FormatGc(999, "bgc", 0, before, after: null);

        Assert.Equal("999\tGC\tbgc\t0\t100\t250\t380\t\t\t", line);
    }

    [Fact]
    public void FormatNote_JoinsColumns_IncludingEmptyDetail()
    {
        Assert.Equal("42\tNOTE\tfolder\tC:\\photos", MemoryLog.FormatNote(42, "folder", "C:\\photos"));
        Assert.Equal("0\tNOTE\trec-start\t", MemoryLog.FormatNote(0, "rec-start", ""));
    }

    // --- Start/Stop のライフサイクル ---

    [Fact]
    public void StartStop_CreatesTimestampedFile_WithHeaderAndMarkers()
    {
        var dir = CreateTempDir();
        var log = new MemoryLog();
        try
        {
            log.Start(dir);
            Assert.True(log.IsRecording);
            var path = log.CurrentFilePath;
            Assert.NotNull(path);
            Assert.Matches(@"memlog_\d{8}_\d{6}\.tsv$", path!);
            Assert.True(File.Exists(path));

            log.Stop();

            Assert.False(log.IsRecording);
            // Stop 後も直近ファイルのパスは参照できる（UI がログの場所を示せるように）。
            Assert.Equal(path, log.CurrentFilePath);

            var lines = File.ReadAllLines(path!);
            Assert.StartsWith("#", lines[0]);
            Assert.Contains("sampleIntervalMs=250", lines[0]);
            Assert.Contains(lines, l => l.Contains("\tNOTE\trec-start\t"));
            Assert.Contains(lines, l => l.Contains("\tNOTE\trec-stop\t"));
        }
        finally
        {
            DeleteTempDir(dir);
        }
    }

    [Fact]
    public void Start_WhileAlreadyRecording_IsNoOp()
    {
        var dir = CreateTempDir();
        var log = new MemoryLog();
        try
        {
            log.Start(dir);
            var path = log.CurrentFilePath;

            log.Start(dir); // 多重開始 → 別ファイルを作らない

            Assert.Equal(path, log.CurrentFilePath);
            log.Stop();
        }
        finally
        {
            DeleteTempDir(dir);
        }
    }

    [Fact]
    public void RecordingCalls_AfterStop_DoNotAppendToFile()
    {
        var dir = CreateTempDir();
        var log = new MemoryLog();
        try
        {
            log.Start(dir);
            var path = log.CurrentFilePath!;
            log.Stop();
            var linesAfterStop = File.ReadAllLines(path);

            // 記録終了後の呼び出しは例外を投げず、ファイルへも書き足さない。
            log.Note("late-note", "should-not-appear");
            log.Sample(default, default);
            log.Nav("x.jpg", 1, 1);
            log.DecodeDone("x.jpg", 1, 1.0, true, 1);
            log.Discarded(1);
            log.Gc("ctrlm", default, default, 1.0);
            Thread.Sleep(50); // 掃き出しタイマーは Stop 済みなので、待っても増えないはず

            var linesAfterCalls = File.ReadAllLines(path);
            Assert.Equal(linesAfterStop.Length, linesAfterCalls.Length);
        }
        finally
        {
            DeleteTempDir(dir);
        }
    }

    [Fact]
    public void Note_BeforeStart_IsNoOp()
    {
        var log = new MemoryLog();

        // 例外を投げないことのみ確認（記録先が無いので書き込み先も無い）。
        log.Note("folder", "C:\\somewhere");
        log.Sample(default, default);

        Assert.False(log.IsRecording);
        Assert.Null(log.CurrentFilePath);
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PhotoQuickSelector.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void DeleteTempDir(string dir)
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
}
