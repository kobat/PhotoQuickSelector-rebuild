using System;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// 右上ズームプレビュー（100% ルーペ）。メインとは独立スクロールで、ロード時は鮮鋭度最大タイル
/// （鮮鋭度表示が無い/未計算なら AF 点）へ寄せる（<see cref="_loupeAutoPositionPending"/> 参照）。
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

    /// <summary>右上ズームプレビューで指定の表示空間座標を中央へスクロールする。</summary>
    private void ScrollZoomToDisplayPoint(double dispX, double dispY)
    {
        if (_bitmap == null) return;
        var (curX, curY) = _zoomViewport.ImageToCanvas(dispX, dispY);
        _zoomViewport.Pan(ZoomCanvas.ActualWidth / 2 - curX, ZoomCanvas.ActualHeight / 2 - curY);
        ZoomCanvas.Invalidate();
    }

    /// <summary>右上ズームプレビューで AF フォーカス点を中央へスクロールする（Shift+Ctrl+Alt+F）。</summary>
    private void ScrollZoomToFocus()
    {
        var (dispX, dispY) = FocusDisplayPoint();
        ScrollZoomToDisplayPoint(dispX, dispY);
    }

    /// <summary>右上ズームプレビューで鮮鋭度最大タイル（無ければ AF フォーカス点）を中央へスクロールする
    /// （Ctrl+Alt+F・写真ロード時・E キーでルーペへ戻った時。<see cref="_loupeAutoPositionPending"/> 参照）。</summary>
    private void ScrollZoomToSharpestOrFocus()
    {
        var (dispX, dispY) = SharpestOrFocusDisplayPoint();
        ScrollZoomToDisplayPoint(dispX, dispY);
    }

    /// <summary>右上ズームプレビューを短辺基準の割合でスクロールする（Ctrl+Alt+矢印）。</summary>
    private void ZoomPanByRatio(double rx, double ry)
    {
        if (_bitmap == null) return;
        _loupeAutoPositionPending = false; // ユーザー操作なので自動センタリングを止める
        double shortSide = Math.Min(ZoomCanvas.ActualWidth, ZoomCanvas.ActualHeight);
        _zoomViewport.Pan(shortSide * rx, shortSide * ry);
        ZoomCanvas.Invalidate();
    }

    private void ZoomCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_bitmap == null) return;  // 空/読み込み中は ClearColor（テーマ背景）を見せる
        var ds = args.DrawingSession;
        ds.Clear(PhotoBackdropColor); // 写真表示中は暗い余白にする
        DrawScaledBitmap(ds, _zoomViewport); // 100% ルーペは実質常に NearestNeighbor（縮小時のみ Linear）
        DrawFocusFrame(ds, _zoomViewport.ImageToCanvas, 2f); // AF 枠はメインでなくここに表示
        DrawSharpestTileFrame(ds, _zoomViewport.ImageToCanvas, 2f); // 鮮鋭度最大タイル枠（オレンジ）
        DrawSubjectRegionFrame(ds, _zoomViewport.ImageToCanvas, 1.5f); // 被写体領域の外接矩形（シアン）
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
        _loupeAutoPositionPending = false; // ユーザーがドラッグで動かした＝自動センタリングを止める
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
        _loupeAutoPositionPending = false; // ユーザーがホイールで動かした＝自動センタリングを止める
        double factor = delta > 0 ? 1.15 : 1.0 / 1.15;
        _zoomViewport.ZoomBy(factor, point.Position.X, point.Position.Y);
        ZoomCanvas.Invalidate();
        e.Handled = true;
    }
}
