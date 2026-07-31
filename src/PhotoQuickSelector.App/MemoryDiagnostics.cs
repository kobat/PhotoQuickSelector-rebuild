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
    long ManagedBytes, long CommittedBytes, long PrivateBytes, long WorkingSetBytes)
{
    /// <summary>
    /// GC の管轄外で使っているメモリの概算（プライベート − GC コミット）。
    /// Win2D/D3D のテクスチャ・WIC のデコードバッファ等、GC の設定やモードでは一切動かない領域を
    /// 切り分けるための値。
    /// <para>
    /// あくまで概算。<see cref="PrivateBytes"/> は現在値だが <see cref="CommittedBytes"/> は
    /// 直前の GC 時点のスナップショット（<see cref="GC.GetGCMemoryInfo(GCKind)"/> の仕様）なので、
    /// GC がしばらく走っていないと差が過大に出る。GC 直後の値どうしで比べるのが最も正確。
    /// 差が負になる状況（コミットの方が新しく見える）もあり得るので 0 でクランプする。
    /// </para>
    /// </summary>
    public long NativeBytes => Math.Max(0, PrivateBytes - CommittedBytes);
}

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
    /// 1 段目は浮かせるのが目的なので <see cref="GCCollectionMode.Forced"/>、
    /// 2 段目だけ <see cref="GCCollectionMode.Aggressive"/> にする（両方 Aggressive にしても
    /// 1 段目の decommit は 2 段目で上書きされるだけで、所要時間が伸びるだけ）。
    /// </para>
    /// <para>
    /// <see cref="GCCollectionMode.Aggressive"/> は free list を捨てて空きリージョン/セグメントを
    /// 積極的に OS へ返す。<see cref="GCCollectionMode.Forced"/> は「今すぐ回収しろ」だけで
    /// 返却は GC 1 回あたりの予算で分割実施されるため、1 回押しでは WS が少ししか減らなかった
    /// （連打で階段状に減る）。Aggressive は generation＝<see cref="GC.MaxGeneration"/> /
    /// blocking / compacting の 3 つがすべて必須で、1 つでも欠けると
    /// <see cref="ArgumentException"/> になる。
    /// </para>
    /// <para>
    /// LOH 圧縮は <c>compacting: true</c> では効かず（そちらは SOH）、
    /// <see cref="GCSettings.LargeObjectHeapCompactionMode"/> の指定が要る。しかもこの指定が
    /// 効くのは次の「ブロッキング」gen2 GC なので、バックグラウンド GC では圧縮されない。
    /// プレビュー用のピクセルバッファは LOH 行きの大きな byte[] なので、断片化ぶんも含めて
    /// 減らせるよう一度だけ有効にする。
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
