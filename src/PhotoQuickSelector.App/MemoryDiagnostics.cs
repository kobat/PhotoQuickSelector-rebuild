using System;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;

namespace PhotoQuickSelector_App;

/// <summary>
/// メモリ使用量のスナップショット（すべてバイト単位の生値）。
/// マネージド分だけでは Win2D/WIC のアンマネージド確保が見えないため、プロセス側の値も併せ持つ。
/// </summary>
/// <param name="ManagedBytes">GC が把握しているマネージドヒープの使用量（<see cref="GC.GetTotalMemory(bool)"/>）。</param>
/// <param name="CommittedBytes">GC がコミット済みのヒープ容量（未使用の空き領域を含む＝OS へ返していない分）。</param>
/// <param name="PrivateBytes">プロセスのプライベートコミット（タスクマネージャーの「コミット サイズ」）。</param>
/// <param name="WorkingSetBytes">プロセスのワーキングセット（タスクマネージャーの「メモリ」に相当）。</param>
public readonly record struct MemorySnapshot(
    long ManagedBytes, long CommittedBytes, long PrivateBytes, long WorkingSetBytes);

/// <summary>
/// デバッグ用のメモリ計測と強制 GC（<c>M</c> でオーバーレイ表示 / <c>Ctrl+M</c> で GC 実行）。
/// 「連続ナビでメモリが微増するのは GC が追いついていないだけなのか」を切り分けるための道具で、
/// 通常動作では一切呼ばれない（＝製品ロジックが GC に依存しないことを前提とした診断専用）。
/// </summary>
public static class MemoryDiagnostics
{
    // プロセスのメモリ量は psapi の GetProcessMemoryInfo で直接引く。
    // System.Diagnostics.Process の WorkingSet64 等は、Windows 実装では
    // NtQuerySystemInformation(SystemProcessInformation) で「マシン上の全プロセス」の
    // スナップショットを取ってから自分の分を拾うため、1 回で数〜数十 ms かかる。
    // 500ms 周期とはいえ UI スレッドで毎回それを払うのは割に合わないので使わない。
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCountersEx
    {
        public uint cb;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
        public nuint PrivateUsage;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(IntPtr process, out ProcessMemoryCountersEx counters, uint cb);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();  // 疑似ハンドル（-1）。CloseHandle 不要

    /// <summary>現在のメモリ使用量を取得する（GC は誘発しない）。</summary>
    public static MemorySnapshot Snapshot()
    {
        long privateBytes = 0, workingSet = 0;
        uint size = (uint)Marshal.SizeOf<ProcessMemoryCountersEx>();
        if (GetProcessMemoryInfo(GetCurrentProcess(), out var counters, size))
        {
            privateBytes = (long)counters.PrivateUsage;
            workingSet = (long)counters.WorkingSetSize;
        }

        return new MemorySnapshot(
            GC.GetTotalMemory(forceFullCollection: false),
            GC.GetGCMemoryInfo().TotalCommittedBytes,
            privateBytes,
            workingSet);
    }

    /// <summary>
    /// 全世代のブロッキング GC ＋ LOH 圧縮を強制し、前後のスナップショットを返す。
    /// <para>
    /// ファイナライザ経由で解放されるアンマネージド資源（Win2D の CanvasBitmap 等の COM ラッパ）は
    /// 1 回の <see cref="GC.Collect()"/> では回収し切れないため、
    /// 「回収 → ファイナライザ待ち → もう一度回収」の 2 段で回す。
    /// プレビュー用のピクセルバッファは LOH 行きの大きな byte[] なので、
    /// 断片化ぶんも含めて減らせるよう LOH 圧縮も一度だけ有効にする。
    /// </para>
    /// <para>
    /// 通常の Forced GC では空き領域の OS への返却（decommit）が GC 1 回あたりの予算で
    /// 分割実施されるため、ワーキングセットが 1 回では減らない（連打で階段状に減る）。
    /// 仕上げの 1 回を <see cref="GCCollectionMode.Aggressive"/>（.NET 7+）にすることで
    /// 可能な限りの OS 返却まで 1 回で行わせる。Aggressive は
    /// generation=MaxGeneration・blocking=true・compacting=true 以外の組み合わせだと
    /// 例外になる点に注意。
    /// </para>
    /// </summary>
    /// <returns>GC 前後のスナップショットと所要時間。</returns>
    public static (MemorySnapshot Before, MemorySnapshot After, TimeSpan Elapsed) ForceFullCollect()
    {
        var before = Snapshot();
        var sw = Stopwatch.StartNew();

        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);

        sw.Stop();
        return (before, Snapshot(), sw.Elapsed);
    }

    /// <summary>
    /// バイト値を MB 表記（整数・3 桁区切り）にする。桁揃えは表示側（<see cref="Controls.MemoryOverlay"/>）が
    /// Grid の列で行うため、ここでは空白の詰め物を入れない。
    /// </summary>
    public static string Mb(long bytes) => $"{bytes / (1024.0 * 1024.0):#,0}MB";
}
