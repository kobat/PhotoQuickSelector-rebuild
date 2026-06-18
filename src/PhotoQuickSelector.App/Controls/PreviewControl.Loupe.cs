using System;
using System.Numerics;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// 右上ズームプレビュー（100% ルーペ）。メインとは独立スクロールで、ロード時は AF 点へ寄せる。
/// 描画はメインがロードした共有 <see cref="_bitmap"/> を流用する（再デコードしない）。
/// </summary>
public sealed partial class PreviewControl
{
    /// <summary>
    /// ズーム/ナビは共有ビットマップを描くだけ。デバイス再生成時は再描画のみ（ロードはメインが担う）。
    /// ズーム・ナビ両キャンバスの CreateResources で共用する。
    /// </summary>
    private void SubCanvas_CreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
        => sender.Invalidate();

    /// <summary>右上ズームプレビューで AF フォーカス点を中央へスクロールする（Ctrl+Alt+F / ロード時）。</summary>
    private void ScrollZoomToFocus()
    {
        if (_bitmap == null) return;
        var (dispX, dispY) = FocusDisplayPoint();
        var (curX, curY) = _zoomViewport.ImageToCanvas(dispX, dispY);
        _zoomViewport.Pan(ZoomCanvas.ActualWidth / 2 - curX, ZoomCanvas.ActualHeight / 2 - curY);
        ZoomCanvas.Invalidate();
    }

    /// <summary>右上ズームプレビューを短辺基準の割合でスクロールする（Ctrl+Alt+矢印）。</summary>
    private void ZoomPanByRatio(double rx, double ry)
    {
        if (_bitmap == null) return;
        double shortSide = Math.Min(ZoomCanvas.ActualWidth, ZoomCanvas.ActualHeight);
        _zoomViewport.Pan(shortSide * rx, shortSide * ry);
        ZoomCanvas.Invalidate();
    }

    private void ZoomCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_bitmap == null) return;
        var ds = args.DrawingSession;
        ds.Transform = Matrix3x2.CreateScale((float)_zoomViewport.Scale)
                       * Matrix3x2.CreateTranslation((float)_zoomViewport.OffsetX, (float)_zoomViewport.OffsetY);
        ds.DrawImage(_bitmap);
    }

    private void ZoomCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _zoomViewport.SetCanvasSize(ZoomCanvas.ActualWidth, ZoomCanvas.ActualHeight);
        ZoomCanvas.Invalidate();
    }

    private void ZoomCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isZoomPanning = true;
        _zoomLastPointer = e.GetCurrentPoint(ZoomCanvas).Position;
        ZoomCanvas.CapturePointer(e.Pointer);
    }

    private void ZoomCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isZoomPanning) return;
        var p = e.GetCurrentPoint(ZoomCanvas).Position;
        _zoomViewport.Pan(p.X - _zoomLastPointer.X, p.Y - _zoomLastPointer.Y);
        _zoomLastPointer = p;
        ZoomCanvas.Invalidate();
    }

    private void ZoomCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isZoomPanning = false;
        ZoomCanvas.ReleasePointerCapture(e.Pointer);
    }

    private void ZoomCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(ZoomCanvas);
        int delta = point.Properties.MouseWheelDelta;
        if (delta == 0) return;
        double factor = delta > 0 ? 1.15 : 1.0 / 1.15;
        _zoomViewport.ZoomBy(factor, point.Position.X, point.Position.Y);
        ZoomCanvas.Invalidate();
        e.Handled = true;
    }
}
