using System;
using PhotoQuickSelector_App.Controls;
using Xunit;

namespace PhotoQuickSelector.Core.Tests;

/// <summary>
/// <see cref="MemoryJanitor"/>（3段構成のメモリ掃除係）の単体テスト。
/// 「捨てバイト累計が閾値でバックグラウンド GC を1回」「回収待ちマネージドゴミの実測概算が閾値を超えたら
/// （再武装ガード付きで）ブロッキング gen2 GC を1回」「アイドル継続で完全 GC を1回」の3系統がそれぞれ
/// 独立して正しくトリガ・リセットされることが要。
/// </summary>
public class MemoryJanitorTests
{
    [Fact]
    public void NoteDiscarded_BelowThreshold_DoesNotRequestBackgroundGc()
    {
        int bgcCount = 0;
        var janitor = new MemoryJanitor(1000, TimeSpan.FromSeconds(1), () => bgcCount++, () => { }, () => { }, () => 0L);

        janitor.NoteDiscarded(999);

        Assert.Equal(0, bgcCount);
        Assert.True(janitor.IsDirty); // ゴミは記録されている
    }

    [Fact]
    public void NoteDiscarded_ReachesThreshold_RequestsBackgroundGcOnceAndResetsAccumulation()
    {
        int bgcCount = 0;
        var janitor = new MemoryJanitor(1000, TimeSpan.FromSeconds(1), () => bgcCount++, () => { }, () => { }, () => 0L);

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
        var janitor = new MemoryJanitor(1000, TimeSpan.FromSeconds(1), () => bgcCount++, () => { }, () => { }, () => 0L);

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
        var janitor = new MemoryJanitor(1000, TimeSpan.FromSeconds(20), () => { }, () => { }, () => fullGcCount++, () => 0L, () => now);

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
        var janitor = new MemoryJanitor(1000, TimeSpan.FromSeconds(20), () => { }, () => { }, () => fullGcCount++, () => 0L, () => now);

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
        var janitor = new MemoryJanitor(1000, TimeSpan.FromSeconds(20), () => { }, () => { }, () => fullGcCount++, () => 0L, () => now);

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
            () => bgcCount++, () => { }, () => fullGcCount++, () => 0L, () => now);

        janitor.NoteDiscarded(1000); // 閾値到達 → BGC 発火
        Assert.Equal(1, bgcCount);
        Assert.True(janitor.IsDirty); // BGC 後も dirty は残る

        now = now.AddSeconds(21);
        janitor.Tick();

        Assert.Equal(1, fullGcCount);
        Assert.False(janitor.IsDirty);
    }

    // --- ブロッキング gen2 GC（実測ゴミ量が閾値超え＝背景 GC が追いついていない事実で判定する保険段）---

    [Fact]
    public void NoteDiscarded_BlockingThresholdDisabled_NeverRequestsBlockingGc()
    {
        int bgcCount = 0, blkCount = 0;
        var janitor = new MemoryJanitor(1000, TimeSpan.FromSeconds(1),
            () => bgcCount++, () => blkCount++, () => { }, () => long.MaxValue);
        // BlockingGcThresholdBytes は既定 0＝無効のまま。ゴミ概算が巨大でも発火しない。

        janitor.NoteDiscarded(10_000); // 背景 GC 閾値も想定上の高閾値もゆうに超える量

        Assert.Equal(0, blkCount);
        Assert.Equal(1, bgcCount); // 背景 GC は従来どおり発行される
    }

    [Fact]
    public void NoteDiscarded_DiscardedHalfAndGarbageAtThreshold_RequestsBlockingGcAndSkipsBackgroundGc()
    {
        int bgcCount = 0, blkCount = 0;
        long garbage = 5000; // 閾値以上のゴミが実測されている状況を模擬
        var janitor = new MemoryJanitor(1000, TimeSpan.FromSeconds(1),
            () => bgcCount++, () => blkCount++, () => { }, () => garbage)
        {
            BlockingGcThresholdBytes = 5000,
        };

        janitor.NoteDiscarded(2500); // 閾値の半分ちょうど＋ゴミ概算が閾値以上 → ブロッキング発行
        Assert.Equal(1, blkCount);
        Assert.Equal(0, bgcCount); // この回は段1（背景 GC）の判定をスキップする

        // ブロッキング発行時に背景 GC 用の累積もリセットされているので、直後の小さい捨ては背景 GC を出さない。
        janitor.NoteDiscarded(1);
        Assert.Equal(0, bgcCount);
    }

    [Fact]
    public void NoteDiscarded_GarbageBelowThreshold_NeverRequestsBlockingGc_BackgroundGcStillFiresPerThreshold()
    {
        int bgcCount = 0, blkCount = 0;
        long garbage = 4999; // 閾値未満のまま（背景 GC が追いついている状況）
        var janitor = new MemoryJanitor(1000, TimeSpan.FromSeconds(1),
            () => bgcCount++, () => blkCount++, () => { }, () => garbage)
        {
            BlockingGcThresholdBytes = 5000,
        };

        // 背景 GC 閾値（1000）ぶんずつ3回捨てる。捨てバイト累計（再武装ガード起算）は3回目で
        // 半分（2500）を超えるが、ゴミ概算が常に閾値未満なのでブロッキングは一度も発火しない。
        janitor.NoteDiscarded(1000);
        janitor.NoteDiscarded(1000);
        janitor.NoteDiscarded(1000);

        Assert.Equal(0, blkCount);
        Assert.Equal(3, bgcCount); // 背景 GC は閾値ごとに従来どおり発火する
    }

    [Fact]
    public void NoteDiscarded_RearmGuard_BlocksRefireUntilHalfThresholdDiscardedAgain()
    {
        int blkCount = 0;
        long garbage = 5000; // 発火後もゴミ概算は閾値以上のまま
        var janitor = new MemoryJanitor(1_000_000, TimeSpan.FromSeconds(1),
            () => { }, () => blkCount++, () => { }, () => garbage)
        {
            BlockingGcThresholdBytes = 5000,
        };

        janitor.NoteDiscarded(2500); // 半分到達 → 発火
        Assert.Equal(1, blkCount);

        janitor.NoteDiscarded(2499); // 再武装ガード未達（半分未満）→ 再発火しない
        Assert.Equal(1, blkCount);

        janitor.NoteDiscarded(1); // 累計で半分（2500）に到達 → 再発火する
        Assert.Equal(2, blkCount);
    }

    [Fact]
    public void Tick_SweepsPendingBlockingGc_WhenGarbageRoseAfterDiscardWithoutFiring()
    {
        // バースト直後に捨てが止まり、誰も新たな NoteDiscarded を呼ばないまま残債（ゴミ概算）が
        // 遅れて閾値へ達するケース＝周期 Tick が掃く対象。
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        int blkCount = 0;
        long garbage = 0; // 捨てた時点ではまだゴミ概算が閾値未満（回収が追いついて見える）
        var janitor = new MemoryJanitor(1_000_000, TimeSpan.FromSeconds(20),
            () => { }, () => blkCount++, () => { }, () => garbage, () => now)
        {
            BlockingGcThresholdBytes = 5000,
        };

        janitor.NoteDiscarded(2500); // 再武装ガードは満たすが、ゴミ概算未達で発火しない（起算は残る）
        Assert.Equal(0, blkCount);

        garbage = 5000; // その後の計測でゴミ概算が閾値以上へ判明（背景 GC が追いついていなかった）
        janitor.Tick(); // アイドル未経過（now 据え置き）でも TryBlockingGc は独立して試みる
        Assert.Equal(1, blkCount);
    }

    [Fact]
    public void Tick_IdleFullGc_ResetsBlockingRearmAccumulation()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        int blkCount = 0;
        long garbage = 0; // アイドル GC 判定には無関係（閾値未満に保つ）
        var janitor = new MemoryJanitor(1_000_000, TimeSpan.FromSeconds(20),
            () => { }, () => blkCount++, () => { }, () => garbage, () => now)
        {
            BlockingGcThresholdBytes = 5000,
        };

        janitor.NoteDiscarded(4000); // 閾値未満のまま dirty に（半分は超えるがゴミ概算 0 なので発火しない）
        now = now.AddSeconds(21);
        janitor.Tick(); // アイドル完全 GC で再武装ガードの起算もリセット

        garbage = 5000; // アイドル GC 後にゴミ概算が閾値以上へ変化
        janitor.NoteDiscarded(2499); // リセット後なので再武装ガード未達（半分=2500未満）→ 発火しない
        Assert.Equal(0, blkCount);

        janitor.NoteDiscarded(1); // 累計 2500（半分）に到達 → 発火する
        Assert.Equal(1, blkCount);
    }

    [Fact]
    public void NoteDiscarded_GarbageBytes_OnlyCalledWhenDiscardConditionMet()
    {
        int garbageCalls = 0;
        var janitor = new MemoryJanitor(1_000_000, TimeSpan.FromSeconds(1),
            () => { }, () => { }, () => { }, () => { garbageCalls++; return 0L; })
        {
            BlockingGcThresholdBytes = 5000,
        };

        // 半分（2500）未満の捨てでは、閾値・再武装ガードの短絡判定だけで false 確定するため
        // garbageBytes は呼ばれない。
        janitor.NoteDiscarded(2499);
        Assert.Equal(0, garbageCalls);

        // 半分に到達すると、初めて garbageBytes を呼んで判定する（結果は 0 なので発火はしない）。
        janitor.NoteDiscarded(1);
        Assert.Equal(1, garbageCalls);
    }
}
