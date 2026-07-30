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
    /// 全世代のブロッキング GC ＋ LOH 圧縮 ＋可能な限りの decommit を強制し、
    /// 前後のスナップショットを返す。
    /// <para>
    /// ファイナライザ経由で解放されるアンマネージド資源（Win2D の CanvasBitmap 等の COM ラッパ）は
    /// 1 回の <see cref="GC.Collect()"/> では回収し切れないため、
    /// 「回収 → ファイナライザ待ち → もう一度回収」の 2 段で回す。
    /// 1 段目はファイナライザ対象を確定するだけの通常 GC とし、ファイナライザ完了後の
    /// 最終段を <see cref="GCCollectionMode.Aggressive"/> にする。Aggressive は通常の
    /// Forced と異なり、GC が保持している未使用ページを可能な限り decommit する。
    /// プレビュー用のピクセルバッファは LOH 行きの大きな byte[] なので、最終段に合わせて
    /// LOH 圧縮も一度だけ有効にする。
    /// </para>
    /// <para>
    /// 通常の Forced GC は空き領域の OS への返却（decommit）を 1 回あたりの予算内で
    /// 分割実施するため、連打しないとワーキングセットが階段状に減らないことがある。
    /// Aggressive を最終段に使うことで、その連打を 1 回の診断操作に集約する。
    /// </para>
    /// </summary>
    /// <returns>GC 前後のスナップショットと所要時間。</returns>
    public static (MemorySnapshot Before, MemorySnapshot After, TimeSpan Elapsed) ForceFullCollect()
    {
        var before = Snapshot();
        var sw = Stopwatch.StartNew();

        // まず到達不能な COM ラッパ等をファイナライザキューへ送り、解放完了を待つ。
        // ここではまだ圧縮/decommit を要求せず、重い処理は最終段へ集約する。
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
        GC.WaitForPendingFinalizers();

        // 最終段で、ファイナライズ済みオブジェクトの回収、SOH/LOH 圧縮、未使用ページの
        // 最大限の decommit をまとめて行う。.NET 7+ の Aggressive は MaxGeneration・
        // blocking・compacting が必須で、この 2 引数オーバーロードが3条件を満たす。
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive);

        sw.Stop();
        return (before, Snapshot(), sw.Elapsed);
    }

    /// <summary>
    /// バイト値を MB 表記（整数・3 桁区切り）にする。桁揃えは表示側（<see cref="Controls.MemoryOverlay"/>）が
    /// Grid の列で行うため、ここでは空白の詰め物を入れない。
    /// </summary>
    public static string Mb(long bytes) => $"{bytes / (1024.0 * 1024.0):#,0}MB";
}
