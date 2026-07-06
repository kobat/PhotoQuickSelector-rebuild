using PhotoQuickSelector_App.Controls;
using Xunit;

namespace PhotoQuickSelector.Core.Tests;

/// <summary>
/// <see cref="DecodeGate"/>（キー付き・優先昇格可能な非同期ゲート）の単体テスト。
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
    public void Promote_MovesKeyToFrontOfWaitQueue()
    {
        var gate = new DecodeGate(1);
        gate.WaitAsync("x"); // 即時取得（容量1）で埋まる
        var a = gate.WaitAsync("a");
        var b = gate.WaitAsync("b");
        var c = gate.WaitAsync("c");

        gate.Promote("c");

        gate.Release(); // x を返却 → 待機列先頭（Promote 済みの c）が完了する
        Assert.True(c.IsCompleted);
        Assert.False(a.IsCompleted);
        Assert.False(b.IsCompleted);

        gate.Release(); // c を返却 → 元の順序どおり a が完了する
        Assert.True(a.IsCompleted);
        Assert.False(b.IsCompleted);
    }

    [Fact]
    public void Promote_UnknownOrAlreadyAcquiredKey_IsNoOp()
    {
        var gate = new DecodeGate(1);
        gate.WaitAsync("x"); // 取得済み（待機列には居ない）
        var a = gate.WaitAsync("a");
        var b = gate.WaitAsync("b");

        // 存在しないキー
        gate.Promote("does-not-exist");
        // 既に取得済み（待機列に居ない）キー
        gate.Promote("x");

        // 順序が変わっていないこと（a が先に完了する）を確認する。
        gate.Release();
        Assert.True(a.IsCompleted);
        Assert.False(b.IsCompleted);
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
    public void Promote_IsCaseInsensitive()
    {
        var gate = new DecodeGate(1);
        gate.WaitAsync("x"); // 取得済みで埋める
        var a = gate.WaitAsync("a");
        var b = gate.WaitAsync("B");

        gate.Promote("b"); // 小文字で積んだ "B" を大文字違いで昇格

        gate.Release(); // x を返却 → 昇格済みの b が先に完了する
        Assert.True(b.IsCompleted);
        Assert.False(a.IsCompleted);
    }
}
