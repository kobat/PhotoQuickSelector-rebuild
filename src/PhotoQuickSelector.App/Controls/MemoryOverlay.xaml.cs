using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// メモリ使用量のデバッグオーバーレイ。<c>M</c> で表示/非表示、<c>Ctrl+M</c> で強制 GC を実行して
/// 前後の値を出す（キー処理は <see cref="MainPage.HandleGlobalKeyDown"/>）。
/// <para>
/// キャッシュ一覧オーバーレイ（<see cref="PreviewControl"/> の <c>C</c>）とは分離してある。
/// キャッシュ一覧は写真切替に同期して更新する必要があるが、メモリ計測をその経路に載せると
/// 連打時のナビが目に見えて重くなり、しかも「観測が対象に影響する」構造になるため。
/// こちらは一定周期の自己更新に閉じている。
/// </para>
/// <para>
/// グリッド表示／プレビュー表示のどちらでも見えるよう <see cref="MainPage"/> の右ペインに置く
/// （メモリはプレビュー固有の情報ではないため）。
/// </para>
/// </summary>
public sealed partial class MemoryOverlay : UserControl
{
    /// <summary>表示中の更新周期。連打中の推移を追うには十分細かく、UI を邪魔しない程度。</summary>
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromMilliseconds(500);

    private DispatcherQueueTimer? _timer;

    public MemoryOverlay()
    {
        InitializeComponent();
    }

    /// <summary>オーバーレイを表示しているか。</summary>
    public bool IsShown => Root.Visibility == Visibility.Visible;

    /// <summary>表示/非表示を切り替える（<c>M</c>）。</summary>
    public void Toggle() => SetShown(!IsShown);

    /// <summary>
    /// 強制フル GC を実行し、前後の値を表示する（<c>Ctrl+M</c>）。
    /// 結果が見えないと意味がないので、非表示なら同時に表示する。
    /// </summary>
    public void ForceGarbageCollect()
    {
        var (before, after, elapsed) = MemoryDiagnostics.ForceFullCollect();
        ShowCollectResult($"GC 実行（{elapsed.TotalMilliseconds:#,0}ms）", before, after, elapsed, show: true);
    }

    /// <summary>
    /// GC 前後の計測結果パネルを更新する。<see cref="ForceGarbageCollect"/>（<c>Ctrl+M</c>・常時表示）と
    /// <see cref="PreviewControl"/> のアイドル完全 GC（<see cref="MemoryJanitor"/> 経由・
    /// <paramref name="show"/>=false＝勝手にオーバーレイを開かない。開いていれば次回 Update で見える）
    /// の両方から呼ぶ共通経路。
    /// </summary>
    /// <param name="header">結果パネルの見出し（実行種別＋所要時間）。</param>
    /// <param name="before">GC 前のスナップショット。</param>
    /// <param name="after">GC 後のスナップショット。</param>
    /// <param name="elapsed">所要時間（未使用。<paramref name="header"/> に既に整形済みだが呼び出し規約として受け取る）。</param>
    /// <param name="show">true ならオーバーレイ自体を表示状態にする（Ctrl+M 相当）。</param>
    public void ShowCollectResult(string header, MemorySnapshot before, MemorySnapshot after, TimeSpan elapsed, bool show)
    {
        CollectHeader.Text = header;
        CollectManagedValue.Text = $"{MemoryDiagnostics.Mb(before.ManagedBytes)} → {MemoryDiagnostics.Mb(after.ManagedBytes)}";
        CollectNativeValue.Text = $"{MemoryDiagnostics.Mb(before.NativeBytes)} → {MemoryDiagnostics.Mb(after.NativeBytes)}";
        CollectWorkingSetValue.Text = $"{MemoryDiagnostics.Mb(before.WorkingSetBytes)} → {MemoryDiagnostics.Mb(after.WorkingSetBytes)}";
        CollectPanel.Visibility = Visibility.Visible;
        if (show) SetShown(true);
        Update();
    }

    private void SetShown(bool shown)
    {
        if (IsShown == shown) return;
        Root.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;

        if (shown)
        {
            Update(); // タイマーの初回 Tick を待たずに即座に出す
            _timer ??= CreateTimer();
            _timer.Start();
        }
        else
        {
            // 非表示中は計測も止める（見ていない間はコストゼロ）。
            _timer?.Stop();
        }
    }

    private DispatcherQueueTimer CreateTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = UpdateInterval;
        timer.IsRepeating = true;
        timer.Tick += (_, _) => Update();
        return timer;
    }

    private void Update()
    {
        var s = MemoryDiagnostics.Snapshot();
        ManagedValue.Text = MemoryDiagnostics.Mb(s.ManagedBytes);
        CommittedValue.Text = MemoryDiagnostics.Mb(s.CommittedBytes);
        PrivateValue.Text = MemoryDiagnostics.Mb(s.PrivateBytes);
        WorkingSetValue.Text = MemoryDiagnostics.Mb(s.WorkingSetBytes);
        NativeValue.Text = MemoryDiagnostics.Mb(s.NativeBytes);
        GcCountText.Text = $"GC回数  gen0 {GC.CollectionCount(0)} / gen1 {GC.CollectionCount(1)} / gen2 {GC.CollectionCount(2)}";
    }
}
