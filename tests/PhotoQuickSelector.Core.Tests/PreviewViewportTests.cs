using PhotoQuickSelector_App.Controls;

namespace PhotoQuickSelector.Core.Tests;

/// <summary>
/// <see cref="PreviewViewport.SetImagePreservingView"/> の検証。
/// 写真切替でズーム状態（モード/フィット比/相対中心）を維持し、画像サイズが異なる場合は
/// 中心を相対位置・倍率をフィット比で保つことを確認する。UI 非依存ロジックなのでリンク参照で単体テスト。
/// </summary>
public class PreviewViewportTests
{
    private const int Precision = 3;

    /// <summary>キャンバス中心が指す画像上の点（px）。</summary>
    private static (double X, double Y) CenterImagePoint(PreviewViewport vp)
        => ((vp.CanvasWidth / 2 - vp.OffsetX) / vp.Scale,
            (vp.CanvasHeight / 2 - vp.OffsetY) / vp.Scale);

    /// <summary>キャンバス中心が指す画像上の相対位置（0..1）。</summary>
    private static (double X, double Y) CenterImageRatio(PreviewViewport vp)
    {
        var (cx, cy) = CenterImagePoint(vp);
        return (cx / vp.ImageWidth, cy / vp.ImageHeight);
    }

    private static PreviewViewport NewViewport(double imgW, double imgH,
        double canvasW = 500, double canvasH = 400)
    {
        var vp = new PreviewViewport();
        vp.SetCanvasSize(canvasW, canvasH);
        vp.SetImage(imgW, imgH);
        return vp;
    }

    [Fact]
    public void PreserveView_SameSize_KeepsScaleAndCenterExactly()
    {
        var vp = NewViewport(1000, 800);   // FitScale = 0.5
        vp.ZoomBy(2.0, 250, 200);          // Custom, Scale = 1.0
        vp.Pan(50, -30);                   // パンして中心をずらす

        double scaleBefore = vp.Scale;
        var (cxBefore, cyBefore) = CenterImagePoint(vp);

        // 同サイズの写真へ切替。
        vp.SetImagePreservingView(1000, 800, 500, 400);

        Assert.Equal(ZoomMode.Custom, vp.Mode);
        Assert.Equal(scaleBefore, vp.Scale, Precision);
        var (cxAfter, cyAfter) = CenterImagePoint(vp);
        // 同サイズなら相対=絶対、ピクセル単位で完全一致。
        Assert.Equal(cxBefore, cxAfter, Precision);
        Assert.Equal(cyBefore, cyAfter, Precision);
    }

    [Fact]
    public void PreserveView_Custom_DifferentSize_KeepsFitRatioAndRelativeCenter()
    {
        var vp = NewViewport(1000, 800);   // FitScale = 0.5
        vp.ZoomBy(2.0, 200, 160);          // Custom, Scale = 1.0 → フィット比 2.0
        double ratioBefore = vp.Scale / vp.FitScale;
        var (relXBefore, relYBefore) = CenterImageRatio(vp);

        // 2 倍の解像度（同アスペクト）の写真へ切替。
        vp.SetImagePreservingView(2000, 1600, 500, 400); // newFit = 0.25

        Assert.Equal(ZoomMode.Custom, vp.Mode);
        // フィット比（フィットの何倍か）が維持される。
        Assert.Equal(ratioBefore, vp.Scale / vp.FitScale, Precision);
        Assert.Equal(0.5, vp.Scale, Precision); // 2.0 * 0.25
        // 中心は相対位置で維持。
        var (relXAfter, relYAfter) = CenterImageRatio(vp);
        Assert.Equal(relXBefore, relXAfter, Precision);
        Assert.Equal(relYBefore, relYAfter, Precision);
    }

    [Fact]
    public void PreserveView_ActualSize_DifferentSize_StaysHundredPercent()
    {
        var vp = NewViewport(1000, 800);
        vp.DpiScale = 1.5;                 // 高 DPI。ActualScale = 1/1.5 ≈ 0.6667
        vp.SetActualSize();                // Mode = ActualSize, 中央
        double actualScale = vp.Scale;
        var (relXBefore, relYBefore) = CenterImageRatio(vp);

        vp.SetImagePreservingView(1200, 900, 500, 400);

        Assert.Equal(ZoomMode.ActualSize, vp.Mode);
        // 100%（DPI 基準）はサイズに依存せず維持。
        Assert.Equal(actualScale, vp.Scale, Precision);
        var (relXAfter, relYAfter) = CenterImageRatio(vp);
        Assert.Equal(relXBefore, relXAfter, Precision);
        Assert.Equal(relYBefore, relYAfter, Precision);
    }

    [Fact]
    public void PreserveView_Fit_DifferentSize_Refits()
    {
        var vp = NewViewport(1000, 800);   // FitScale = 0.5（Mode = Fit）

        vp.SetImagePreservingView(800, 600, 500, 400); // newFit = min(0.625, 0.6667) = 0.625

        Assert.Equal(ZoomMode.Fit, vp.Mode);
        Assert.Equal(0.625, vp.Scale, Precision);
        // 横は画面いっぱい（DrawW=500）→ OffsetX=0、縦は中央寄せ。
        Assert.Equal(0.0, vp.OffsetX, Precision);
        Assert.Equal((400 - 600 * 0.625) / 2, vp.OffsetY, Precision);
    }

    [Fact]
    public void ZoomToStop_In_SnapsToNextRoundStop()
    {
        var vp = NewViewport(1000, 800);   // FitScale = 0.5（DeviceScale 0.5）
        // フィット(50%)から1ティック拡大 → 次の round 段 67% へ。
        vp.ZoomToStop(zoomIn: true, 250, 200);
        Assert.Equal(ZoomMode.Custom, vp.Mode);
        Assert.Equal(0.6667, vp.DeviceScale, Precision);

        // さらに1ティック → 75%。
        vp.ZoomToStop(zoomIn: true, 250, 200);
        Assert.Equal(0.75, vp.DeviceScale, Precision);

        // さらに1ティック → 100%。
        vp.ZoomToStop(zoomIn: true, 250, 200);
        Assert.Equal(1.0, vp.DeviceScale, Precision);
    }

    [Fact]
    public void ZoomToStop_Out_StopsAtFitStop_AsFitMode()
    {
        var vp = NewViewport(1000, 800);   // FitScale = 0.5
        vp.ZoomToStop(zoomIn: true, 250, 200);  // 50% → 67%（Custom）
        vp.ZoomToStop(zoomIn: true, 250, 200);  // 67% → 75%
        Assert.Equal(0.75, vp.DeviceScale, Precision);

        // 縮小していくとフィット段(50%)を通過し、そこは Fit モードになる。
        vp.ZoomToStop(zoomIn: false, 250, 200); // 75% → 67%
        Assert.Equal(0.6667, vp.DeviceScale, Precision);
        Assert.Equal(ZoomMode.Custom, vp.Mode);

        vp.ZoomToStop(zoomIn: false, 250, 200); // 67% → 50%（＝フィット段）
        Assert.Equal(0.5, vp.DeviceScale, Precision);
        Assert.Equal(ZoomMode.Fit, vp.Mode);
    }

    [Fact]
    public void ZoomToStop_HighDpi_SnapsByDisplayPercent()
    {
        // 高 DPI でも段は表示倍率（DeviceScale）基準なので 100% などに正しく止まる。
        var vp = NewViewport(2000, 1600);  // FitScale = 0.25 → DeviceScale はその×DpiScale
        vp.DpiScale = 1.5;
        // フィット段（DeviceScale 0.375）から拡大していくと 50% に止まる。
        vp.ZoomToStop(zoomIn: true, 250, 200);
        Assert.Equal(0.5, vp.DeviceScale, Precision);
        // Scale 自体は DeviceScale/DpiScale。
        Assert.Equal(0.5 / 1.5, vp.Scale, Precision);
    }

    [Fact]
    public void ZoomToStop_AtMaxStop_DoesNothing()
    {
        var vp = NewViewport(1000, 800);
        // 16x（最大段）まで上げてから、さらに拡大しても変わらない。
        for (int i = 0; i < 30; i++) vp.ZoomToStop(zoomIn: true, 250, 200);
        Assert.Equal(16.0, vp.DeviceScale, Precision);
        double before = vp.DeviceScale;
        vp.ZoomToStop(zoomIn: true, 250, 200);
        Assert.Equal(before, vp.DeviceScale, Precision);
    }

    [Fact]
    public void PreserveView_FromEmpty_FallsBackToFitCenter()
    {
        // 画像未設定（ImageWidth=0）から呼ばれても破綻せず、フィット中央で初期化される。
        var vp = new PreviewViewport();
        vp.SetCanvasSize(500, 400);

        vp.SetImagePreservingView(1000, 800, 500, 400);

        Assert.Equal(ZoomMode.Fit, vp.Mode);
        Assert.Equal(0.5, vp.Scale, Precision);
        var (relX, relY) = CenterImageRatio(vp);
        Assert.Equal(0.5, relX, Precision);
        Assert.Equal(0.5, relY, Precision);
    }
}
