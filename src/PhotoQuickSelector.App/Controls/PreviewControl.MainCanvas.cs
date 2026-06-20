using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// メイン大画面キャンバスの描画とポインタ操作（パン / ホイールズーム / ダブルクリックで退出）。
/// グリッド線・AF枠の描画は <c>PreviewControl.Overlays.cs</c> に分離。
/// </summary>
public sealed partial class PreviewControl
{
    private void MainCanvas_CreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
    {
        // デバイス生成・ロストからの復帰時はキャッシュを破棄して再ロード。
        if (args.Reason == CanvasCreateResourcesReason.NewDevice ||
            args.Reason == CanvasCreateResourcesReason.DpiChanged)
        {
            ResetCacheAndReload();
        }
    }

    private void MainCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_bitmap == null) return;  // 空/読み込み中は ClearColor（テーマ背景）を見せる

        var ds = args.DrawingSession;
        ds.Clear(PhotoBackdropColor); // 写真表示中は暗い余白（レターボックス）にする
        DrawScaledBitmap(ds, _viewport);

        // オーバーレイはキャンバス空間（ImageToCanvas 経由・固定線幅）で描く。
        if (_viewModel?.ShowGrid == true) DrawGrid(ds);
        DrawFocusFrame(ds);
    }

    /// <summary>
    /// ビットマップを現在のビューポート（スケール＋平行移動）でキャンバスへ描く。
    /// ビットマップは Orientation 適用済み（正立）なので回転は加えない。
    /// ピクセル等倍以上（<see cref="PreviewViewport.DeviceScale"/> ≥ 1）では補間を NearestNeighbor にして
    /// にじみのないくっきりした拡大表示にし、縮小時は Linear で滑らかに保つ。
    /// </summary>
    private void DrawScaledBitmap(CanvasDrawingSession ds, PreviewViewport vp)
    {
        if (_bitmap == null) return;
        var interp = vp.DeviceScale >= 1.0 - 1e-6
            ? CanvasImageInterpolation.NearestNeighbor   // 等倍以上はくっきり（補間なし）
            : CanvasImageInterpolation.Linear;           // 縮小は滑らかに
        var dest = new Rect(vp.OffsetX, vp.OffsetY, vp.DrawWidth, vp.DrawHeight);
        ds.DrawImage(_bitmap, dest, _bitmap.Bounds, 1.0f, interp);
    }

    private void MainCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _viewport.SetCanvasSize(MainCanvas.ActualWidth, MainCanvas.ActualHeight);
        InvalidateMain();
    }

    // --- ポインタ操作（パン / ホイールズーム） ---

    private void MainCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isPanning = true;
        _lastPointer = e.GetCurrentPoint(MainCanvas).Position;
        MainCanvas.CapturePointer(e.Pointer);
        MainCanvas.Focus(FocusState.Programmatic);
    }

    private void MainCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPanning) return;
        var p = e.GetCurrentPoint(MainCanvas).Position;
        _viewport.Pan(p.X - _lastPointer.X, p.Y - _lastPointer.Y);
        _lastPointer = p;
        InvalidateMain();
    }

    private void MainCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isPanning = false;
        MainCanvas.ReleasePointerCapture(e.Pointer);
    }

    private void MainCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(MainCanvas);
        int delta = point.Properties.MouseWheelDelta;
        if (delta == 0) return;
        double factor = delta > 0 ? 1.15 : 1.0 / 1.15;
        _viewport.ZoomBy(factor, point.Position.X, point.Position.Y);
        InvalidateMain();
        e.Handled = true;
    }

    private void MainCanvas_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // 大画面のダブルクリックでサムネイル一覧へ戻る（SPEC §2）。
        _viewModel?.ExitPreview();
        e.Handled = true;
    }
}
