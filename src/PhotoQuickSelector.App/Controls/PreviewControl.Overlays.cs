using System;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Windows.UI;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// メイン/ナビへ重ねるオーバーレイ描画: 三分割グリッド線と Sony AF フォーカス枠、
/// および AF フォーカス点の幾何計算（メイン/ルーペのセンタリングで共用）。
/// </summary>
public sealed partial class PreviewControl
{
    /// <summary>三分割グリッド線を表示中の画像領域に描く（SPEC §3-6）。</summary>
    private void DrawGrid(CanvasDrawingSession ds)
    {
        double left = _viewport.OffsetX, top = _viewport.OffsetY;
        double w = _viewport.DrawWidth, h = _viewport.DrawHeight;
        if (w <= 0 || h <= 0) return;

        var color = Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF);
        float l = (float)Math.Max(left, 0);
        float r = (float)Math.Min(left + w, MainCanvas.ActualWidth);
        float t = (float)Math.Max(top, 0);
        float b = (float)Math.Min(top + h, MainCanvas.ActualHeight);

        float vx1 = (float)(left + w / 3), vx2 = (float)(left + 2 * w / 3);
        float hy1 = (float)(top + h / 3), hy2 = (float)(top + 2 * h / 3);

        ds.DrawLine(vx1, t, vx1, b, color, 1f);
        ds.DrawLine(vx2, t, vx2, b, color, 1f);
        ds.DrawLine(l, hy1, r, hy1, color, 1f);
        ds.DrawLine(l, hy2, r, hy2, color, 1f);
    }

    /// <summary>半透明グリーンの AF 枠色。メイン・ナビゲーターで共通。</summary>
    private static readonly Color FocusColor = Color.FromArgb(0xEE, 0x66, 0xFF, 0x66);

    /// <summary>
    /// Sony AF フォーカス枠を描く。フォーカス点・枠は「生センサー px（Orientation 適用前）」の値なので、
    /// <see cref="PreviewViewport.OrientationMatrix"/> で表示空間（正立ビットマップ座標）へ写してから
    /// <see cref="PreviewViewport.ImageToCanvas"/> でキャンバスへ変換する。線幅はキャンバス空間で固定。
    /// </summary>
    private void DrawFocusFrame(CanvasDrawingSession ds)
        => DrawFocusFrame(ds, _viewport.ImageToCanvas, 2f);

    /// <summary>
    /// AF フォーカス枠を、表示空間 px → キャンバス座標の写像 <paramref name="toCanvas"/> を使って描く。
    /// メイン（<see cref="PreviewViewport.ImageToCanvas"/>）とナビゲーター（フィット変換）で共用する。
    /// </summary>
    private void DrawFocusFrame(CanvasDrawingSession ds, Func<double, double, (double X, double Y)> toCanvas, float thickness)
    {
        if (_currentMeta is not { } meta || meta.FocusPoint is not { } fp || _bitmap == null) return;

        double rawW = meta.OriginalWidth, rawH = meta.OriginalHeight; // 生センサー寸法（Orientation 前）
        if (rawW <= 0 || rawH <= 0) return;
        double refW = meta.FocusReferenceSize is { Width: > 0 } rs ? rs.Width : rawW;
        double refH = meta.FocusReferenceSize is { Height: > 0 } rs2 ? rs2.Height : rawH;

        var om = PreviewViewport.OrientationMatrix(_currentOrientation, rawW, rawH);
        double cx = fp.X * rawW / refW;
        double cy = fp.Y * rawH / refH;

        if (meta.FocusSize is { Width: > 0, Height: > 0 } fs)
        {
            double fw = fs.Width * rawW / refW;
            double fh = fs.Height * rawH / refH;
            // 生 px の矩形 4 隅 → 表示空間 → キャンバス（90/270 度回転で軸が入替わっても矩形は軸平行のまま）。
            var o0 = Vector2.Transform(new Vector2((float)(cx - fw / 2), (float)(cy - fh / 2)), om);
            var o1 = Vector2.Transform(new Vector2((float)(cx + fw / 2), (float)(cy + fh / 2)), om);
            var (x0, y0) = toCanvas(o0.X, o0.Y);
            var (x1, y1) = toCanvas(o1.X, o1.Y);
            float x = (float)Math.Min(x0, x1), y = (float)Math.Min(y0, y1);
            ds.DrawRectangle(x, y, (float)Math.Abs(x1 - x0), (float)Math.Abs(y1 - y0), FocusColor, thickness);
        }
        else
        {
            var o = Vector2.Transform(new Vector2((float)cx, (float)cy), om);
            var (px, py) = toCanvas(o.X, o.Y);
            const float radius = 8f;
            ds.DrawRectangle((float)px - radius, (float)py - radius, 2 * radius, 2 * radius, FocusColor, thickness);
        }
    }

    /// <summary>
    /// AF フォーカス点（無ければ画像中心）の「表示空間（正立ビットマップ）座標」を返す。
    /// フォーカス点は生センサー px なので <see cref="PreviewViewport.OrientationMatrix"/> で写す。
    /// </summary>
    private (double X, double Y) FocusDisplayPoint()
    {
        if (_currentMeta is { FocusPoint: { } fp } meta && meta.OriginalWidth > 0 && meta.OriginalHeight > 0)
        {
            double rawW = meta.OriginalWidth, rawH = meta.OriginalHeight;
            double refW = meta.FocusReferenceSize is { Width: > 0 } rs ? rs.Width : rawW;
            double refH = meta.FocusReferenceSize is { Height: > 0 } rs2 ? rs2.Height : rawH;
            var disp = Vector2.Transform(
                new Vector2((float)(fp.X * rawW / refW), (float)(fp.Y * rawH / refH)),
                PreviewViewport.OrientationMatrix(_currentOrientation, rawW, rawH));
            return (disp.X, disp.Y);
        }
        return (_viewport.ImageWidth / 2, _viewport.ImageHeight / 2);
    }

    /// <summary>メインプレビューで AF フォーカス点が画面中央へ来るよう 100% 表示でスクロールする（Alt+F）。</summary>
    private void ScrollToFocus()
    {
        if (_bitmap == null) return;
        _viewport.SetActualSize(); // スクロールできるよう等倍にする
        var (dispX, dispY) = FocusDisplayPoint();
        var (curX, curY) = _viewport.ImageToCanvas(dispX, dispY);
        _viewport.Pan(MainCanvas.ActualWidth / 2 - curX, MainCanvas.ActualHeight / 2 - curY);
        InvalidateMain();
    }
}
