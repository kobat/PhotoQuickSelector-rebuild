using System;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.UI;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// ナビゲーター（全体縮小画像＋青枠＝メイン表示領域＋緑枠＝AF枠）。
/// クリック/ドラッグでメインプレビューの表示位置を移動する。
/// <para>
/// 縮小描画は毎パンのコストが高い（50MP フル解像度を HighQualityCubic で毎フレーム縮小すると
/// iGPU でフレーム落ちの原因になる）ため、表示中の 1 枚だけ縮小済み <see cref="_navBitmap"/> を
/// キャッシュし、以降の Draw はそれを 1:1 で描くだけにする。写真切替/リサイズ直後の最初の
/// フレームだけは暫定的に NearestNeighbor でフル解像度を直描きし、高品質版は低優先度で
/// 遅延生成する（<see cref="ScheduleNavRegen"/>）。
/// </para>
/// </summary>
public sealed partial class PreviewControl
{
    // ナビ用の縮小ビットマップ（表示中の1枚だけ保持）。毎パンの 50MP HighQualityCubic 縮小を
    // やめ、写真切替/リサイズ時に一度だけ生成した小さな画像を以降の Draw で使い回す。
    private CanvasRenderTarget? _navBitmap;
    private bool _navBitmapDirty = true;      // 写真切替・デバイス再生成で立てる
    private bool _navRegenScheduled;           // 再生成の多重スケジュール防止

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
        if (_immersive) return; // 畳み中は描画しない（Collapsed でも Draw が発火し続けるため明示ガード）
        var (scale, ox, oy) = NavFit();
        if (scale <= 0) return;

        double iw = _viewport.ImageWidth, ih = _viewport.ImageHeight;
        double dw = iw * scale, dh = ih * scale;
        var ds = args.DrawingSession;
        ds.Clear(NavBackdropColor); // 写真表示中は暗い余白にする（空/読み込み中は ClearColor＝テーマ背景）

        bool miniValid = !_navBitmapDirty && _navBitmap != null
            && Math.Abs(_navBitmap.Size.Width - dw) < 0.5
            && Math.Abs(_navBitmap.Size.Height - dh) < 0.5;
        if (miniValid)
        {
            // 縮小済みキャッシュを 1:1（DIP サイズ一致）で描くだけ＝毎パンのコストは僅少。
            ds.DrawImage(_navBitmap, (float)ox, (float)oy);
        }
        else
        {
            // 切替/リサイズ直後の暫定フレーム: 最速の NearestNeighbor でフル解像度を直描きし、
            // 高品質縮小（HighQualityCubic）は低優先度で 1 回だけ遅延生成する。
            ds.DrawImage(_bitmap, new Rect(ox, oy, dw, dh), _bitmap.Bounds, 1.0f,
                CanvasImageInterpolation.NearestNeighbor);
            ScheduleNavRegen();
        }

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

    /// <summary>
    /// 高品質縮小版ナビビットマップの生成を低優先度で 1 回だけスケジュールする。
    /// メイン画像のフレーム提示を優先し、切替直後の最初のフレームから HQC の重さを追い出す。
    /// </summary>
    private void ScheduleNavRegen()
    {
        if (_navRegenScheduled) return;
        _navRegenScheduled = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _navRegenScheduled = false;
            RegenerateNavBitmap();
        });
    }

    /// <summary>フル解像度 <see cref="_bitmap"/> を HighQualityCubic で縮小し <see cref="_navBitmap"/> へキャッシュする。</summary>
    private void RegenerateNavBitmap()
    {
        if (_bitmap == null || _immersive) return; // 畳み中は不要（開いたときの Draw が再スケジュールする）
        var (scale, ox, oy) = NavFit();
        if (scale <= 0) return;
        float dw = (float)(_viewport.ImageWidth * scale), dh = (float)(_viewport.ImageHeight * scale);
        if (dw < 1 || dh < 1) return;
        try
        {
            var rt = new CanvasRenderTarget(NavCanvas, dw, dh); // DPI は NavCanvas に追従＝物理解像度で生成
            using (var rds = rt.CreateDrawingSession())
            {
                rds.DrawImage(_bitmap, new Rect(0, 0, dw, dh), _bitmap.Bounds, 1.0f,
                    CanvasImageInterpolation.HighQualityCubic);
            }
            _navBitmap?.Dispose();
            _navBitmap = rt;
            _navBitmapDirty = false;
            NavCanvas.Invalidate(); // 高品質版で描き直す
        }
        catch
        {
            // デバイスロスト等。次の Draw の暫定描画（NearestNeighbor）が再スケジュールする。
        }
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
