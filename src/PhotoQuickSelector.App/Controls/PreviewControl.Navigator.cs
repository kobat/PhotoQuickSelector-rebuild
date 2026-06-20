using System;
using System.Numerics;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.UI;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// ナビゲーター（全体縮小画像＋青枠＝メイン表示領域＋緑枠＝AF枠）。
/// クリック/ドラッグでメインプレビューの表示位置を移動する。
/// </summary>
public sealed partial class PreviewControl
{
    /// <summary>ナビゲーターの全体フィット変換（表示空間 px → ナビキャンバス座標）を返す。</summary>
    private (double Scale, double OffsetX, double OffsetY) NavFit()
    {
        double iw = _viewport.ImageWidth, ih = _viewport.ImageHeight;
        double cw = NavCanvas.ActualWidth, ch = NavCanvas.ActualHeight;
        if (iw <= 0 || ih <= 0 || cw <= 0 || ch <= 0) return (0, 0, 0);
        double scale = Math.Min(cw / iw, ch / ih);
        return (scale, (cw - iw * scale) / 2, (ch - ih * scale) / 2);
    }

    private void NavCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_bitmap == null) return;
        var (scale, ox, oy) = NavFit();
        if (scale <= 0) return;

        var ds = args.DrawingSession;
        ds.Clear(NavBackdropColor); // 写真表示中は暗い余白にする（空/読み込み中は ClearColor＝テーマ背景）
        ds.Transform = Matrix3x2.CreateScale((float)scale)
                       * Matrix3x2.CreateTranslation((float)ox, (float)oy);
        ds.DrawImage(_bitmap);
        ds.Transform = Matrix3x2.Identity; // 矩形はキャンバス空間（固定線幅）で描く

        // 青枠: メインプレビューの表示領域
        var (vx, vy, vw, vh) = _viewport.VisibleImageRect();
        if (vw > 0 && vh > 0)
        {
            var blue = Color.FromArgb(0xE0, 0x33, 0x99, 0xFF);
            ds.DrawRectangle((float)(ox + vx * scale), (float)(oy + vy * scale),
                             (float)(vw * scale), (float)(vh * scale), blue, 2f);
        }

        // 緑枠: AF フォーカス枠（フィット変換でナビキャンバスへ）
        DrawFocusFrame(ds, (x, y) => (ox + x * scale, oy + y * scale), 2f);
    }

    private void NavCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        NavCanvas.Invalidate();
        // ナビゲーター高さの変更（スプリッター/復元）を設定へ控える（実保存は終了時）。
        if (_viewModel != null && NavRow.ActualHeight > 0)
            _viewModel.Settings.NavigatorHeight = NavRow.ActualHeight;
    }

    private void NavCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isNavPanning = true;
        NavCanvas.CapturePointer(e.Pointer);
        NavMoveMainTo(e.GetCurrentPoint(NavCanvas).Position);
    }

    private void NavCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isNavPanning) return;
        NavMoveMainTo(e.GetCurrentPoint(NavCanvas).Position);
    }

    private void NavCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isNavPanning = false;
        NavCanvas.ReleasePointerCapture(e.Pointer);
    }

    /// <summary>ナビゲーター上のクリック位置を画像点に逆変換し、メインプレビューがその点を中央に表示する。</summary>
    private void NavMoveMainTo(Point navPos)
    {
        if (_bitmap == null) return;
        var (scale, ox, oy) = NavFit();
        if (scale <= 0) return;

        double dispX = (navPos.X - ox) / scale;
        double dispY = (navPos.Y - oy) / scale;
        var (curX, curY) = _viewport.ImageToCanvas(dispX, dispY);
        _viewport.Pan(MainCanvas.ActualWidth / 2 - curX, MainCanvas.ActualHeight / 2 - curY);
        InvalidateMain();
    }
}
