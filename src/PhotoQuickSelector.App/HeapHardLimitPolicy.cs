namespace PhotoQuickSelector_App;

/// <summary>
/// マネージドヒープのハードリミット（<c>System.GC.HeapHardLimit</c>）の設定値を検証する純ロジック。
/// UI（WinUI/WinRT）に依存しない static クラスとして分離し、xUnit からソースリンクしてテストする
/// （<c>tests\PhotoQuickSelector.Core.Tests</c> の <c>PreviewViewport</c>/<c>DecodeGate</c> 等と同じ方式）。
/// </summary>
public static class HeapHardLimitPolicy
{
    /// <summary>設定画面が受け付ける絶対下限（GB）。これを下回る値は用途を問わず引き上げる。</summary>
    private const double AbsoluteMinimumGB = 2.0;

    /// <summary>
    /// 上限値（GB）を、現在のキャッシュ予算に対して安全な下限へクランプする。
    /// <para>
    /// 上限がキャッシュ予算＋デコード中バッファ（同時 2 本×約 200MB）＋サムネイル常駐を下回ると、
    /// GC で回収しても確保が収まらず <see cref="System.OutOfMemoryException"/> でクラッシュする。
    /// 設定画面の数値入力だけでその組み合わせを作れてしまわないよう、
    /// 「キャッシュ予算＋1GB」を下限として常に適用する（絶対下限 <see cref="AbsoluteMinimumGB"/> も併用）。
    /// </para>
    /// </summary>
    /// <param name="limitGB">設定画面で入力されたハードリミット（GB）。</param>
    /// <param name="cacheBudgetGB">現在のキャッシュ容量予算（GB。<see cref="AppSettings.CacheBudgetGB"/>）。</param>
    /// <returns>クランプ後のハードリミット（GB）。</returns>
    public static double ClampGB(double limitGB, double cacheBudgetGB)
    {
        // NaN は Math.Max で伝播する（NaN と比較すると常に false）ため、既定値 3.5 扱いに正規化してから比べる。
        if (double.IsNaN(limitGB)) limitGB = 3.5;
        if (double.IsNaN(cacheBudgetGB)) cacheBudgetGB = 2.5;

        var minByBudget = cacheBudgetGB + 1.0;
        return Math.Max(limitGB, Math.Max(minByBudget, AbsoluteMinimumGB));
    }

    /// <summary>GB → バイト（<c>System.GC.HeapHardLimit</c> / <c>AppContext.SetData("GCHeapHardLimit", ...)</c> の単位）。</summary>
    public static ulong ToBytes(double gb) => (ulong)(gb * 1024 * 1024 * 1024);
}
