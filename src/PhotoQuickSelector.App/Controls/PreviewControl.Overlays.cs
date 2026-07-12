using System;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Windows.UI;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// オーバーレイ描画: 構図グリッド線（中央十字／三分割／正方形 × 画像/Canvas 基準。メインのみ）と
/// Sony AF フォーカス枠（ルーペ/ナビに表示。メインには重ねない）、
/// および AF フォーカス点の幾何計算（メイン/ルーペのセンタリングで共用）。
/// </summary>
public sealed partial class PreviewControl
{
    /// <summary>グリッド線の色（半透明白）。種類・基準によらず共通。</summary>
    private static readonly Color GridColor = Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF);

    /// <summary>
    /// 構図グリッドを描く（SPEC §3-6）。基準（画像 / Canvas）で領域矩形を決め、種類別に線を引く。
    /// 画像基準は表示中の画像矩形（ズーム/パン追従・画面外へはみ出しうる）、Canvas 基準はコントロール全面。
    /// いずれも線はキャンバス枠＋領域でクリップする。
    /// </summary>
    private void DrawGrid(CanvasDrawingSession ds)
    {
        if (_viewModel is not { } vm || vm.GridKind == GridOverlayKind.None) return;

        double canvasW = MainCanvas.ActualWidth, canvasH = MainCanvas.ActualHeight;
        if (canvasW <= 0 || canvasH <= 0) return;

        // 領域矩形（rl, rt, rw, rh）を基準で決める。
        double rl, rt, rw, rh;
        if (vm.GridReference == GridOverlayReference.Canvas)
        {
            rl = 0; rt = 0; rw = canvasW; rh = canvasH;
        }
        else
        {
            rl = _viewport.OffsetX; rt = _viewport.OffsetY;
            rw = _viewport.DrawWidth; rh = _viewport.DrawHeight;
        }
        if (rw <= 0 || rh <= 0) return;

        // 線の描画範囲は「領域 ∩ キャンバス」にクリップ（縦線は y、横線は x の範囲を限定）。
        float xLo = (float)Math.Max(rl, 0);
        float xHi = (float)Math.Min(rl + rw, canvasW);
        float yLo = (float)Math.Max(rt, 0);
        float yHi = (float)Math.Min(rt + rh, canvasH);
        if (xHi <= xLo || yHi <= yLo) return;

        // 縦線 x / 横線 y。可視範囲外（クリップ枠の外）は描かない。
        void V(double x) { if (x >= xLo - 0.5 && x <= xHi + 0.5) ds.DrawLine((float)x, yLo, (float)x, yHi, GridColor, 1f); }
        void H(double y) { if (y >= yLo - 0.5 && y <= yHi + 0.5) ds.DrawLine(xLo, (float)y, xHi, (float)y, GridColor, 1f); }

        switch (vm.GridKind)
        {
            case GridOverlayKind.CenterCross:
                V(rl + rw / 2);
                H(rt + rh / 2);
                break;

            case GridOverlayKind.RuleOfThirds:
                V(rl + rw / 3); V(rl + 2 * rw / 3);
                H(rt + rh / 3); H(rt + 2 * rh / 3);
                break;

            case GridOverlayKind.Square:
                DrawSquareGrid(V, H, rl, rt, rw, rh, vm.GridSquareDivisions);
                break;
        }
    }

    /// <summary>
    /// 正方形グリッド。短辺を N 等分した一辺長 cell の正方セルを、領域中心から対称に敷き詰める。
    /// 中心からの線オフセット集合は、N が偶数なら {0, ±cell, ±2cell…}（中央に線）、
    /// N が奇数なら {±cell/2, ±3cell/2…}（中央は半セルずれ＝中央線なし）。長辺方向も同位相で延長する。
    /// </summary>
    private static void DrawSquareGrid(Action<double> V, Action<double> H,
        double rl, double rt, double rw, double rh, int divisions)
    {
        int n = Math.Max(2, divisions);
        double cell = Math.Min(rw, rh) / n;
        if (cell <= 0) return;

        double cx = rl + rw / 2, cy = rt + rh / 2;
        double halfMax = Math.Max(rw, rh) / 2;
        double start = (n % 2 == 0) ? 0 : cell / 2;  // 偶数=中央に線 / 奇数=半セルずらし

        for (double d = start; d <= halfMax + 0.5; d += cell)
        {
            if (d <= 1e-9)
            {
                V(cx); H(cy);  // 中央線（偶数 N のみ）
            }
            else
            {
                V(cx - d); V(cx + d);
                H(cy - d); H(cy + d);
            }
        }
    }

    /// <summary>半透明グリーンの AF 枠色。メイン・ナビゲーターで共通。</summary>
    private static readonly Color FocusColor = Color.FromArgb(0xEE, 0x66, 0xFF, 0x66);

    /// <summary>
    /// AF フォーカス枠を描く（Sony／Olympus 共通。読取り側で同じ中心＋枠＋基準サイズ表現へ正規化済み）。
    /// フォーカス点・枠は「生センサー基準（Orientation 適用前）」の値なので、
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

    /// <summary>メインプレビューで AF フォーカス点が画面中央へ来るようスクロールする（Alt+F）。
    /// すでにズーム中なら現在の倍率を保ったまま寄せる。フィット以下（画像全体が収まりパンできない）
    /// のときだけ等倍化してから寄せる。</summary>
    private void ScrollToFocus()
    {
        if (_bitmap == null) return;
        // Scale <= FitScale はフィット中またはフィット未満の縮小（Custom）＝スクロール余地が無い状態。
        // このときだけ等倍にする。ズーム中（Scale > FitScale）は倍率を変えずパンだけで寄せる。
        if (_viewport.Scale <= _viewport.FitScale) _viewport.SetActualSize();
        var (dispX, dispY) = FocusDisplayPoint();
        var (curX, curY) = _viewport.ImageToCanvas(dispX, dispY);
        _viewport.Pan(MainCanvas.ActualWidth / 2 - curX, MainCanvas.ActualHeight / 2 - curY);
        InvalidateMain();
    }
}
