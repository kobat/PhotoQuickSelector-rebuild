using PhotoQuickSelector_App;
using Xunit;

namespace PhotoQuickSelector.Core.Tests;

/// <summary>
/// <see cref="HeapHardLimitPolicy"/>（マネージドヒープのハードリミット設定値の検証）の単体テスト。
/// 「上限がキャッシュ予算＋1GB を下回る組み合わせを作れない」「絶対下限 2.0GB」「NaN の正規化」が要。
/// </summary>
public class HeapHardLimitPolicyTests
{
    [Fact]
    public void ClampGB_ValueAboveBudgetFloor_PassesThrough()
    {
        // 予算 2.5GB の下限は 3.5GB。入力 3.5 はちょうど下限なので素通し。
        Assert.Equal(3.5, HeapHardLimitPolicy.ClampGB(3.5, 2.5));
    }

    [Fact]
    public void ClampGB_ValueBelowBudgetFloor_RaisedToFloor()
    {
        // 予算 2.5GB の下限は 3.5GB。入力 3.0 はこれを下回るため引き上げられる。
        Assert.Equal(3.5, HeapHardLimitPolicy.ClampGB(3.0, 2.5));
    }

    [Fact]
    public void ClampGB_LargerBudget_RaisesFloorAccordingly()
    {
        // 予算 4.0GB の下限は 5.0GB。入力 3.5 は下回るため 5.0 へ引き上げられる。
        Assert.Equal(5.0, HeapHardLimitPolicy.ClampGB(3.5, 4.0));
    }

    [Fact]
    public void ClampGB_TinyBudget_UsesAbsoluteMinimum()
    {
        // 予算 0.5GB なら「予算+1GB」は 1.5GB だが、絶対下限 2.0GB がそれを上回るので 2.0 になる。
        Assert.Equal(2.0, HeapHardLimitPolicy.ClampGB(1.0, 0.5));
    }

    [Fact]
    public void ClampGB_NaNLimit_NormalizesToDefaultThenClamps()
    {
        // NaN は既定値 3.5 扱いに正規化してからクランプする（予算 2.5GB の下限 3.5GB と一致）。
        Assert.Equal(3.5, HeapHardLimitPolicy.ClampGB(double.NaN, 2.5));
    }

    [Fact]
    public void ToBytes_ThreePointFiveGB_MatchesCsprojConstant()
    {
        // csproj の RuntimeHostConfigurationOption（System.GC.HeapHardLimit）の既定値と一致すること。
        Assert.Equal(3758096384UL, HeapHardLimitPolicy.ToBytes(3.5));
    }
}
