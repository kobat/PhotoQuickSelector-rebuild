using System.Numerics;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

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
        if (_bitmap == null) return;

        var ds = args.DrawingSession;
        // ビットマップは Orientation 適用済み（正立）なので回転は加えず、スケール＋平行移動のみ。
        var saved = ds.Transform;
        ds.Transform = Matrix3x2.CreateScale((float)_viewport.Scale)
                       * Matrix3x2.CreateTranslation((float)_viewport.OffsetX, (float)_viewport.OffsetY);
        ds.DrawImage(_bitmap);
        ds.Transform = saved; // オーバーレイはキャンバス空間（固定線幅）で描画

        if (_viewModel?.ShowGrid == true) DrawGrid(ds);
        DrawFocusFrame(ds);
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
