using System;
using PhotoQuickSelector_App.Controls;
using Xunit;

namespace PhotoQuickSelector.Core.Tests;

/// <summary>
/// <see cref="MemoryJanitor"/>（2段構成のメモリ掃除係）の単体テスト。
/// 「捨てバイト累計が閾値でバックグラウンド GC を1回」「アイドル継続で完全 GC を1回」の
/// 2 系統がそれぞれ独立して正しくトリガ・リセットされることが要。
/// </summary>
public class MemoryJanitorTests
{
    [Fact]
    public void NoteDiscarded_BelowThreshold_DoesNotRequestBackgroundGc()
    {
        int bgcCount = 0;
        var janitor = new MemoryJanitor(1000, TimeSpan.FromSeconds(1), () => bgcCount++, () => { });

        janitor.NoteDiscarded(999);

        Assert.Equal(0, bgcCount);
        Assert.True(janitor.IsDirty); // ゴミは記録されている
    }

    [Fact]
    public void NoteDiscarded_ReachesThreshold_RequestsBackgroundGcOnceAndResetsAccumulation()
    {
        int bgcCount = 0;
        var janitor = new MemoryJanitor(1000, TimeSpan.FromSeconds(1), () => bgcCount++, () => { });

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
        var janitor = new MemoryJanitor(1000, TimeSpan.FromSeconds(1), () => bgcCount++, () => { });

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
        var janitor = new MemoryJanitor(1000, TimeSpan.FromSeconds(20), () => { }, () => fullGcCount++, () => now);

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
        var janitor = new MemoryJanitor(1000, TimeSpan.FromSeconds(20), () => { }, () => fullGcCount++, () => now);

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
        var janitor = new MemoryJanitor(1000, TimeSpan.FromSeconds(20), () => { }, () => fullGcCount++, () => now);

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
            () => bgcCount++, () => fullGcCount++, () => now);

        janitor.NoteDiscarded(1000); // 閾値到達 → BGC 発火
        Assert.Equal(1, bgcCount);
        Assert.True(janitor.IsDirty); // BGC 後も dirty は残る

        now = now.AddSeconds(21);
        janitor.Tick();

        Assert.Equal(1, fullGcCount);
        Assert.False(janitor.IsDirty);
    }
}
