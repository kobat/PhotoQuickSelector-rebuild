using System.Collections.Generic;
using PhotoQuickSelector_App.Controls;
using Xunit;

namespace PhotoQuickSelector.Core.Tests;

/// <summary>
/// <see cref="DecodeGate"/>（キー付き・優先度コールバック対応の非同期ゲート）の単体テスト。
/// <see cref="System.Threading.Tasks.TaskCompletionSource.SetResult()"/> は Task の完了状態を
/// 同期的に確定させる（非同期なのは継続の実行のみ）ため、<c>Release()</c> 直後に
/// <c>IsCompleted</c> をアサートしてよい。
/// </summary>
public class DecodeGateTests
{
    [Fact]
    public void WaitAsync_WithinCapacity_CompletesImmediately()
    {
        var gate = new DecodeGate(2);

        var t1 = gate.WaitAsync("a");
        var t2 = gate.WaitAsync("b");
        var t3 = gate.WaitAsync("c");

        Assert.True(t1.IsCompleted);
        Assert.True(t2.IsCompleted);
        Assert.False(t3.IsCompleted);
    }

    [Fact]
    public void Release_ReleasesWaitersInFifoOrder()
    {
        // GetPriority 未設定（null）＝優先度コールバックなしの従来動作＝純 FIFO で grant されることの検証。
        var gate = new DecodeGate(2);
        gate.WaitAsync("a");
        gate.WaitAsync("b");
        var t3 = gate.WaitAsync("c");
        var t4 = gate.WaitAsync("d");

        gate.Release(); // a を返却 → 待機列の先頭（c）が完了する

        Assert.True(t3.IsCompleted);
        Assert.False(t4.IsCompleted);
    }

    [Fact]
    public void Release_WithEmptyQueue_IncreasesAvailableSlots()
    {
        var gate = new DecodeGate(1);
        var t1 = gate.WaitAsync("a");
        Assert.True(t1.IsCompleted);

        gate.Release(); // 待機列は空 → 空きが増える

        var t2 = gate.WaitAsync("b");
        Assert.True(t2.IsCompleted);
    }

    [Fact]
    public void Release_WithPriority_GrantsSmallestPriorityWaiter()
    {
        var gate = new DecodeGate(1);
        gate.WaitAsync("x"); // 即時取得（容量1）で埋まる
        var a = gate.WaitAsync("a");
        var b = gate.WaitAsync("b");
        var c = gate.WaitAsync("c");

        var priorities = new Dictionary<string, int> { ["c"] = 0, ["a"] = 1, ["b"] = 2 };
        gate.GetPriority = key => priorities.TryGetValue(key, out var p) ? p : int.MaxValue;

        gate.Release(); // x を返却 → 待機列中で最小優先度（c=0）へ譲渡される

        Assert.True(c.IsCompleted);
        Assert.False(a.IsCompleted);
        Assert.False(b.IsCompleted);
    }

    [Fact]
    public void Release_ReevaluatesPriorityOnEachGrant()
    {
        // grant のたびに現在の優先度を再評価する（陳腐化しない）ことの検証。
        // 1 回目の grant 時点では a が最小 → a が完了。
        // 2 回目までにコールバックの中身を書き換え、c を最小にする → 投入順(b が先)によらず c が先に完了。
        var gate = new DecodeGate(1);
        gate.WaitAsync("x"); // 即時取得で埋まる
        var a = gate.WaitAsync("a");
        var b = gate.WaitAsync("b");
        var c = gate.WaitAsync("c");

        var priorities = new Dictionary<string, int> { ["a"] = 0, ["b"] = 1, ["c"] = 2 };
        gate.GetPriority = key => priorities.TryGetValue(key, out var p) ? p : int.MaxValue;

        gate.Release(); // x を返却 → a（優先度0）が完了する
        Assert.True(a.IsCompleted);
        Assert.False(b.IsCompleted);
        Assert.False(c.IsCompleted);

        // 残りは b, c。優先度を書き換えて c を最優先にする（b はまだ投入順で先だが優先度は劣後させる）。
        priorities["c"] = 0;
        priorities["b"] = 1;

        gate.Release(); // a を返却 → 再評価された優先度に従い c が完了する（b ではない）
        Assert.True(c.IsCompleted);
        Assert.False(b.IsCompleted);
    }

    [Fact]
    public void Release_EqualPriorities_PreservesFifoOrder()
    {
        var gate = new DecodeGate(1);
        gate.WaitAsync("x"); // 即時取得で埋まる
        var a = gate.WaitAsync("a");
        var b = gate.WaitAsync("b");
        var c = gate.WaitAsync("c");

        gate.GetPriority = _ => 5; // 全員同値

        gate.Release(); // x を返却 → 同値なので先に並んだ a が完了する
        Assert.True(a.IsCompleted);
        Assert.False(b.IsCompleted);
        Assert.False(c.IsCompleted);

        gate.Release(); // a を返却 → 次に並んだ b が完了する
        Assert.True(b.IsCompleted);
        Assert.False(c.IsCompleted);
    }

    [Fact]
    public void Release_MaxValuePriority_SinksBelowOthers()
    {
        var gate = new DecodeGate(1);
        gate.WaitAsync("x"); // 即時取得で埋まる
        var outside = gate.WaitAsync("outside"); // 窓外相当（先に投入されるが優先度は最下位）
        var inside = gate.WaitAsync("inside");

        var priorities = new Dictionary<string, int> { ["inside"] = 0 };
        gate.GetPriority = key => priorities.TryGetValue(key, out var p) ? p : int.MaxValue;

        gate.Release(); // x を返却 → 投入順では outside が先だが、優先度は inside が勝つ

        Assert.True(inside.IsCompleted);
        Assert.False(outside.IsCompleted);
    }
}
