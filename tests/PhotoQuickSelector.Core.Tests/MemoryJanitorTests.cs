using System;
using PhotoQuickSelector_App.Controls;
using Xunit;

namespace PhotoQuickSelector.Core.Tests;

/// <summary>
/// <see cref="MemoryJanitor"/>（3段構成のメモリ掃除係）の単体テスト。
/// 「捨てバイト累計が閾値でバックグラウンド GC を1回」「回収待ちメモリ（ゴミ＋在庫）の実測概算が閾値を
/// 超えたら（再武装ガード付きで）ブロッキング gen2 GC を1回」「アイドル継続で完全 GC を1回」の3系統が
/// それぞれ独立して正しくトリガ・リセットされることが要。
/// </summary>
public class MemoryJanitorTests
{
    [Fact]
    public void NoteDiscarded_BelowThreshold_DoesNotRequestBackgroundGc()
    {
        int bgcCount = 0;
        var janitor = new MemoryJanitor(1000, TimeSpan.FromSeconds(1),
            () => bgcCount++, () => { }, () => { }, () => default);

        janitor.NoteDiscarded(999);

        Assert.Equal(0, bgcCount);
        Assert.True(janitor.IsDirty); // ゴミは記録されている
    }

    [Fact]
    public void NoteDiscarded_ReachesThreshold_RequestsBackgroundGcOnceAndResetsAccumulation()
    {
        int bgcCount = 0;
        var janitor = new MemoryJanitor(1000, TimeSpan.FromSeconds(1),
            () => bgcCount++, () => { }, () => { }, () => default);

        janitor.NoteDiscarded(1000);
        Assert.Equal(1, bgcCount);

        // 累積はリセットされているので、続く NoteDiscarded は閾値未満なら再発火しない。
        janitor.NoteDiscarded(999);
        Assert.Equal(1, bgcCount);

        // さらに積み増して閾値へ届けば再び発火する。
        janitor.NoteDiscarded(1);
        Assert.Equal(2, bgcCount);
    }

    [Fact]
    public void NoteDiscarded_NonPositiveBytes_IsIgnored()
    {
        int bgcCount = 0;
        var janitor = new MemoryJanitor(1000, TimeSpan.FromSeconds(1),
            () => bgcCount++, () => { }, () => { }, () => default);

        janitor.NoteDiscarded(0);
        janitor.NoteDiscarded(-100);

        Assert.Equal(0, bgcCount);
        Assert.False(janitor.IsDirty);
    }

    [Fact]
    public void Tick_DirtyAndIdleElapsed_RequestsFullGcOnceAndClearsDirty()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        int fullGcCount = 0;
        var janitor = new MemoryJanitor(1000, TimeSpan.FromSeconds(20),
            () => { }, () => { }, () => fullGcCount++, () => default, () => now);

        janitor.NoteDiscarded(1); // dirty にするだけ（閾値未満）
        now = now.AddSeconds(21);

        janitor.Tick();
        Assert.Equal(1, fullGcCount);
        Assert.False(janitor.IsDirty);

        // dirty 解除後は Tick を重ねても再発火しない。
        janitor.Tick();
        Assert.Equal(1, fullGcCount);
    }

    [Fact]
    public void Tick_IdleNotElapsed_DoesNotRequestFullGc()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        int fullGcCount = 0;
        var janitor = new MemoryJanitor(1000, TimeSpan.FromSeconds(20),
            () => { }, () => { }, () => fullGcCount++, () => default, () => now);

        janitor.NoteDiscarded(1);
        now = now.AddSeconds(10); // idleDelay 未満

        janitor.Tick();

        Assert.Equal(0, fullGcCount);
        Assert.True(janitor.IsDirty);
    }

    [Fact]
    public void NoteActivity_PostponesIdleDetection()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        int fullGcCount = 0;
        var janitor = new MemoryJanitor(1000, TimeSpan.FromSeconds(20),
            () => { }, () => { }, () => fullGcCount++, () => default, () => now);

        janitor.NoteDiscarded(1);
        now = now.AddSeconds(15);
        janitor.NoteActivity(); // 最終活動を更新＝ここからさらに20秒必要になる

        now = now.AddSeconds(10); // NoteActivity から10秒しか経っていない
        janitor.Tick();
        Assert.Equal(0, fullGcCount);

        now = now.AddSeconds(11); // NoteActivity から21秒経過
        janitor.Tick();
        Assert.Equal(1, fullGcCount);
    }

    [Fact]
    public void BackgroundGc_LeavesDirty_SoIdleFullGcStillFires()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        int bgcCount = 0, fullGcCount = 0;
        var janitor = new MemoryJanitor(1000, TimeSpan.FromSeconds(20),
            () => bgcCount++, () => { }, () => fullGcCount++, () => default, () => now);

        janitor.NoteDiscarded(1000); // 閾値到達 → BGC 発火
        Assert.Equal(1, bgcCount);
        Assert.True(janitor.IsDirty); // BGC 後も dirty は残る

        now = now.AddSeconds(21);
        janitor.Tick();

        Assert.Equal(1, fullGcCount);
        Assert.False(janitor.IsDirty);
    }

    // --- ブロッキング gen2 GC（実測の回収待ちメモリ＝ゴミ＋在庫が閾値超え＝背景 GC が追いついていない
    //     事実で判定する保険段。発行は常に Aggressive＝ホスト配線）---

    [Fact]
    public void NoteDiscarded_BlockingThresholdDisabled_NeverRequestsBlockingGc()
    {
        int bgcCount = 0, blockingCount = 0;
        var janitor = new MemoryJanitor(1000, TimeSpan.FromSeconds(1),
            () => bgcCount++, () => blockingCount++, () => { },
            () => new PendingMemoryEstimate(long.MaxValue / 2, long.MaxValue / 2));
        // BlockingGcThresholdBytes は既定 0＝無効のまま。合計概算が巨大でも発火しない。

        janitor.NoteDiscarded(10_000); // 背景 GC 閾値も想定上の高閾値もゆうに超える量

        Assert.Equal(0, blockingCount);
        Assert.Equal(1, bgcCount); // 背景 GC は従来どおり発行される
    }

    [Fact]
    public void NoteDiscarded_TotalAtThreshold_RequestsBlockingGcAndSkipsBackgroundGc()
    {
        int bgcCount = 0, blockingCount = 0;
        // ゴミ＋在庫の合計が閾値ちょうど（内訳は判定に影響しない）。
        var pending = new PendingMemoryEstimate(GarbageBytes: 4000, InventoryBytes: 1000);
        var janitor = new MemoryJanitor(1000, TimeSpan.FromSeconds(1),
            () => bgcCount++, () => blockingCount++, () => { }, () => pending)
        {
            BlockingGcThresholdBytes = 5000,
        };

        janitor.NoteDiscarded(2500); // 閾値の半分ちょうど（再武装達成）＋合計概算が閾値以上 → 発火
        Assert.Equal(1, blockingCount);
        Assert.Equal(0, bgcCount); // この回は段1（背景 GC）の判定をスキップする

        // 発行時に背景 GC 用の累積もリセットされているので、直後の小さい捨ては背景 GC を出さない。
        janitor.NoteDiscarded(1);
        Assert.Equal(0, bgcCount);
    }

    [Fact]
    public void NoteDiscarded_TotalBelowThreshold_NeverFires_BackgroundGcStillFiresPerThreshold()
    {
        int bgcCount = 0, blockingCount = 0;
        // 合計が常に閾値未満（背景 GC が追いついている状況）。
        var pending = new PendingMemoryEstimate(GarbageBytes: 2000, InventoryBytes: 2999);
        var janitor = new MemoryJanitor(1000, TimeSpan.FromSeconds(1),
            () => bgcCount++, () => blockingCount++, () => { }, () => pending)
        {
            BlockingGcThresholdBytes = 5000,
        };

        // 背景 GC 閾値（1000）ぶんずつ3回捨てる。捨てバイト累計（再武装ガード起算）は3回目で
        // 半分（2500）を超えるが、合計概算が常に閾値未満なのでブロッキング段は一度も発火しない。
        janitor.NoteDiscarded(1000);
        janitor.NoteDiscarded(1000);
        janitor.NoteDiscarded(1000);

        Assert.Equal(0, blockingCount);
        Assert.Equal(3, bgcCount); // 背景 GC は閾値ごとに従来どおり発火する
    }

    [Fact]
    public void NoteDiscarded_RearmGuard_BlocksRefireUntilHalfThresholdDiscardedAgain()
    {
        int blockingCount = 0;
        // 発火後も合計概算は閾値以上のまま（概算誤差の高止まりを模擬）。
        var pending = new PendingMemoryEstimate(GarbageBytes: 5000, InventoryBytes: 0);
        var janitor = new MemoryJanitor(1_000_000, TimeSpan.FromSeconds(1),
            () => { }, () => blockingCount++, () => { }, () => pending)
        {
            BlockingGcThresholdBytes = 5000,
        };

        janitor.NoteDiscarded(2500); // 半分到達 → 発火
        Assert.Equal(1, blockingCount);

        janitor.NoteDiscarded(2499); // 再武装ガード未達（半分未満）→ 再発火しない
        Assert.Equal(1, blockingCount);

        janitor.NoteDiscarded(1); // 累計で半分（2500）に到達 → 再発火する
        Assert.Equal(2, blockingCount);
    }

    [Fact]
    public void Tick_SweepsPendingBlockingGc_WhenPendingRoseAfterDiscardWithoutFiring()
    {
        // バースト直後に捨てが止まり、誰も新たな NoteDiscarded を呼ばないまま残債（合計概算）が
        // 遅れて閾値へ達するケース＝周期 Tick が掃く対象。
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        int blockingCount = 0;
        var pending = new PendingMemoryEstimate(0, 0); // 捨てた時点ではまだ合計概算が閾値未満
        var janitor = new MemoryJanitor(1_000_000, TimeSpan.FromSeconds(20),
            () => { }, () => blockingCount++, () => { }, () => pending, () => now)
        {
            BlockingGcThresholdBytes = 5000,
        };

        janitor.NoteDiscarded(2500); // 再武装ガードは満たすが、合計概算未達で発火しない（起算は残る）
        Assert.Equal(0, blockingCount);

        pending = new PendingMemoryEstimate(GarbageBytes: 5000, InventoryBytes: 0); // 閾値以上へ判明
        janitor.Tick(); // アイドル未経過（now 据え置き）でも TryBlockingGc は独立して試みる
        Assert.Equal(1, blockingCount);
    }

    [Fact]
    public void Tick_IdleFullGc_ResetsBlockingRearmAccumulation()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        int blockingCount = 0;
        var pending = new PendingMemoryEstimate(0, 0); // アイドル GC 判定には無関係（閾値未満に保つ）
        var janitor = new MemoryJanitor(1_000_000, TimeSpan.FromSeconds(20),
            () => { }, () => blockingCount++, () => { }, () => pending, () => now)
        {
            BlockingGcThresholdBytes = 5000,
        };

        janitor.NoteDiscarded(4000); // 閾値未満のまま dirty に（半分は超えるが合計概算 0 なので発火しない）
        now = now.AddSeconds(21);
        janitor.Tick(); // アイドル完全 GC で再武装ガードの起算もリセット

        pending = new PendingMemoryEstimate(GarbageBytes: 5000, InventoryBytes: 0); // アイドル GC 後に閾値以上へ変化
        janitor.NoteDiscarded(2499); // リセット後なので再武装ガード未達（半分=2500未満）→ 発火しない
        Assert.Equal(0, blockingCount);

        janitor.NoteDiscarded(1); // 累計 2500（半分）に到達 → 発火する
        Assert.Equal(1, blockingCount);
    }

    [Fact]
    public void NoteDiscarded_PendingBytes_OnlyCalledWhenDiscardConditionMet()
    {
        int pendingCalls = 0;
        var janitor = new MemoryJanitor(1_000_000, TimeSpan.FromSeconds(1),
            () => { }, () => { }, () => { },
            () => { pendingCalls++; return default; })
        {
            BlockingGcThresholdBytes = 5000,
        };

        // 半分（2500）未満の捨てでは、閾値・再武装ガードの短絡判定だけで false 確定するため
        // pendingBytes は呼ばれない。
        janitor.NoteDiscarded(2499);
        Assert.Equal(0, pendingCalls);

        // 半分に到達すると、初めて pendingBytes を呼んで判定する（結果は既定値=0 なので発火はしない）。
        janitor.NoteDiscarded(1);
        Assert.Equal(1, pendingCalls);
    }

    [Fact]
    public void PendingMemoryEstimate_TotalBytes_IsSumOfGarbageAndInventory()
    {
        var estimate = new PendingMemoryEstimate(GarbageBytes: 300, InventoryBytes: 700);
        Assert.Equal(1000, estimate.TotalBytes);
    }
}
