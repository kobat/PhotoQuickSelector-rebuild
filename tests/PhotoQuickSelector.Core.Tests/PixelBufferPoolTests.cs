using System;
using PhotoQuickSelector_App.Controls;
using Xunit;

namespace PhotoQuickSelector.Core.Tests;

/// <summary>
/// <see cref="PixelBufferPool"/>（デコード済みピクセル用 byte[] の再利用プール）の単体テスト。
/// 「同じバイト長だけ再利用する」「上限超過は古い順に捨てる」の 2 点が要。
/// </summary>
public class PixelBufferPoolTests
{
    [Fact]
    public void Rent_EmptyPool_AllocatesRequestedLength()
    {
        var pool = new PixelBufferPool { MaxBytes = 1000 };

        var buffer = pool.Rent(100);

        Assert.Equal(100, buffer.Length);
        Assert.Equal(0, pool.HitCount);
        Assert.Equal(1, pool.MissCount);
    }

    [Fact]
    public void Rent_AfterReturn_ReusesSameInstance()
    {
        var pool = new PixelBufferPool { MaxBytes = 1000 };
        var first = pool.Rent(100);
        pool.Return(first);

        var second = pool.Rent(100);

        Assert.Same(first, second);
        Assert.Equal(1, pool.HitCount);
        Assert.Equal(0, pool.Count);
        Assert.Equal(0, pool.PooledBytes);
    }

    [Fact]
    public void Rent_DifferentLength_DoesNotReuse()
    {
        var pool = new PixelBufferPool { MaxBytes = 1000 };
        var stored = pool.Rent(100);
        pool.Return(stored);

        var other = pool.Rent(200);

        Assert.NotSame(stored, other);
        Assert.Equal(200, other.Length);
        // 借りられなかった 100 バイトはプールに残ったまま（上限内なので捨てられない）。
        Assert.Equal(1, pool.Count);
        Assert.Equal(100, pool.PooledBytes);
    }

    [Fact]
    public void Return_OverBudget_DropsOldestFirst()
    {
        var pool = new PixelBufferPool { MaxBytes = 250 };
        var oldest = new byte[100];
        var middle = new byte[100];
        var newest = new byte[100];

        pool.Return(oldest);
        pool.Return(middle);
        pool.Return(newest);  // 合計 300 > 250 → 最古の 1 本を捨てて 200 にする

        Assert.Equal(2, pool.Count);
        Assert.Equal(200, pool.PooledBytes);
        Assert.NotSame(oldest, pool.Rent(100));  // 残っているのは middle（次に古い）
    }

    [Fact]
    public void Return_LargerThanBudget_IsDiscarded()
    {
        var pool = new PixelBufferPool { MaxBytes = 50 };

        pool.Return(new byte[100]);

        Assert.Equal(0, pool.Count);
        Assert.Equal(0, pool.PooledBytes);
    }

    [Fact]
    public void Return_ZeroBudget_KeepsNothing()
    {
        // 割合 0%（プール無効）の設定に相当。デコードのたびに新規確保する従来動作へ縮退する。
        var pool = new PixelBufferPool { MaxBytes = 0 };

        pool.Return(new byte[100]);

        Assert.Equal(0, pool.Count);
        Assert.Equal(1, pool.Rent(100).Length / 100);
        Assert.Equal(0, pool.HitCount);
    }

    [Fact]
    public void Rent_RefreshesRecency_SoReusedSizeSurvivesEviction()
    {
        // 使われ続けるサイズは Return のたびに新しくなるので、使われないサイズより後まで残る。
        var pool = new PixelBufferPool { MaxBytes = 250 };
        var stale = new byte[100];
        pool.Return(stale);

        var hot = pool.Rent(100);   // stale が借り出される
        Assert.Same(stale, hot);
        pool.Return(hot);           // 新しい順序で戻る
        pool.Return(new byte[100]);
        pool.Return(new byte[100]); // 合計 300 > 250 → 最古（hot）が落ちる

        Assert.Equal(2, pool.Count);
        Assert.Equal(200, pool.PooledBytes);
    }

    [Fact]
    public void Clear_EmptiesPoolButKeepsCounters()
    {
        var pool = new PixelBufferPool { MaxBytes = 1000 };
        pool.Return(new byte[100]);
        pool.Rent(100);
        pool.Return(new byte[100]);

        pool.Clear();

        Assert.Equal(0, pool.Count);
        Assert.Equal(0, pool.PooledBytes);
        Assert.Equal(1, pool.HitCount);  // 診断用カウンタは残す
    }

    [Fact]
    public void Rent_NonPositiveLength_Throws()
    {
        var pool = new PixelBufferPool { MaxBytes = 1000 };

        Assert.Throws<ArgumentOutOfRangeException>(() => pool.Rent(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => pool.Rent(-1));
    }

    [Fact]
    public void Return_Null_Throws()
    {
        var pool = new PixelBufferPool { MaxBytes = 1000 };

        Assert.Throws<ArgumentNullException>(() => pool.Return(null!));
    }
}
