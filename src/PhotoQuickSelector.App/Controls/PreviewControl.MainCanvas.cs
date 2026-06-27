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
        // AF 枠はメインには重ねず、右上ルーペ（ZoomCanvas）とナビゲーターにのみ表示する。
        if (_viewModel is { GridKind: not GridOverlayKind.None }) DrawGrid(ds);
    }

    /// <summary>
    /// デバイスピクセル等倍率に応じた補間モードを選ぶ。ピクセル等倍以上は NearestNeighbor で
    /// くっきり（補間なし）、縮小時は HighQualityCubic で高品質に。メイン/ルーペ/ナビ共通。
    /// </summary>
    private static CanvasImageInterpolation PickInterpolation(double deviceScale)
        => deviceScale >= 1.0 - 1e-6
            ? CanvasImageInterpolation.NearestNeighbor   // 等倍以上はくっきり（補間なし）
            : CanvasImageInterpolation.HighQualityCubic; // 縮小は高品質

    /// <summary>
    /// ビットマップを指定のオフセット/描画サイズでキャンバスへ描く（回転は加えない＝Orientation 適用済み）。
    /// 補間は <see cref="PickInterpolation"/> で <paramref name="deviceScale"/>（物理px/画像px）に応じて選ぶ。
    /// </summary>
    private void DrawScaledBitmap(CanvasDrawingSession ds,
        double offsetX, double offsetY, double drawW, double drawH, double deviceScale)
    {
        if (_bitmap == null) return;
        var dest = new Rect(offsetX, offsetY, drawW, drawH);
        ds.DrawImage(_bitmap, dest, _bitmap.Bounds, 1.0f, PickInterpolation(deviceScale));
    }

    /// <summary>ビューポート（メイン/ルーペ）の現在状態でビットマップを描く薄いオーバーロード。</summary>
    private void DrawScaledBitmap(CanvasDrawingSession ds, PreviewViewport vp)
        => DrawScaledBitmap(ds, vp.OffsetX, vp.OffsetY, vp.DrawWidth, vp.DrawHeight, vp.DeviceScale);

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
        // 中途半端な倍率にならないよう、round なズーム段（＋フィット段）へスナップする。
        _viewport.ZoomToStop(delta > 0, point.Position.X, point.Position.Y);
        InvalidateMain();
        e.Handled = true;
    }

    private void MainCanvas_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // 大画面のシングルクリックでフィット ⇄ ズームをトグル（倍率は Z キーと同一）。
        // 中心はクリック位置を基準にする（ホイールズームと同様にカーソル下の画像点を固定）。
        // Tapped はドラッグ（しきい値超えの移動）では発火しないため、パン操作とは自然に分離される。
        var p = e.GetPosition(MainCanvas);
        _viewport.ToggleZoomAround(p.X, p.Y);
        InvalidateMain();
        e.Handled = true;
    }

    private void MainCanvas_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // 大画面のダブルクリックで 100% 表示（倍率は Shift+Z キーと同一）。中心はクリック位置基準。
        // ダブルクリック時は Tapped（トグル）が先に発火するが、最終状態は 100% に確定する。
        // ※プレビュー終了（サムネイル一覧へ戻る）はフィルムストリップのダブルクリックへ移設。
        var p = e.GetPosition(MainCanvas);
        _viewport.SetActualSizeAround(p.X, p.Y);
        InvalidateMain();
        e.Handled = true;
    }
}
