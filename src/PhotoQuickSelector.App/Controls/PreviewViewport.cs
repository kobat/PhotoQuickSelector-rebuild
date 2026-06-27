using System.Collections.Generic;
using System.Numerics;

namespace PhotoQuickSelector_App.Controls;

/// <summary>ズーム表示のモード。</summary>
public enum ZoomMode
{
    /// <summary>キャンバスに収まるようフィット。</summary>
    Fit,
    /// <summary>等倍（画像 1px = 1 物理 px、DPI 考慮）。SPEC §6-5 の 100%。</summary>
    ActualSize,
    /// <summary>任意倍率（ホイール等で変更）。</summary>
    Custom,
}

/// <summary>
/// プレビューのズーム/パン計算を集約した UI 非依存クラス。
/// SPEC §3-6 の ResizeImage / OriginalSizeImage 相当（DrawOffset / DrawWidth/Height を公開）。
/// 画像座標は「Orientation 補正済みの表示サイズ」（<see cref="ImageMetadata.Width"/>/<c>Height</c>）で扱う。
/// </summary>
public sealed class PreviewViewport
{
    /// <summary>キャンバス（描画領域）のサイズ。単位は描画単位（DIP）。</summary>
    public double CanvasWidth { get; private set; }
    public double CanvasHeight { get; private set; }

    /// <summary>Orientation 補正済みの画像表示サイズ（px）。</summary>
    public double ImageWidth { get; private set; }
    public double ImageHeight { get; private set; }

    /// <summary>現在のズームモード。</summary>
    public ZoomMode Mode { get; private set; } = ZoomMode.Fit;

    /// <summary>現在の倍率（画像 px → 描画単位）。DrawWidth = ImageWidth * Scale。</summary>
    public double Scale { get; private set; } = 1.0;

    /// <summary>
    /// DPI スケール（物理 px / DIP ＝ Dpi/96）。Win2D は DIP 座標で描画し最終的に
    /// 物理px = DIP × Dpi/96 でラスタライズされるため、等倍（1 画像px = 1 物理px）の倍率は
    /// 1.0 ではなく <c>1.0 / DpiScale</c> になる。コントロール側が <c>MainCanvas.Dpi/96</c> を与える。
    /// </summary>
    public double DpiScale { get; set; } = 1.0;

    /// <summary>等倍（1 画像px = 1 物理px）に対応する Scale。SPEC §6-5 の 100%。</summary>
    private double ActualScale => DpiScale > 0 ? 1.0 / DpiScale : 1.0;

    /// <summary>描画先の左上オフセット（DrawOffsetX/Y, 単位 DIP）。</summary>
    public double OffsetX { get; private set; }
    public double OffsetY { get; private set; }

    public double DrawWidth => ImageWidth * Scale;
    public double DrawHeight => ImageHeight * Scale;

    /// <summary>デバイスピクセル等倍率（物理px / 画像px ＝ Scale × DpiScale）。
    /// 1.0 以上ならピクセル等倍以上に拡大されている（補間を NearestNeighbor に切り替える判定に使う）。</summary>
    public double DeviceScale => Scale * DpiScale;

    private const double MinScale = 0.02;
    private const double MaxScale = 16.0;

    /// <summary>
    /// ホイールズームのスナップ段（表示倍率 DeviceScale ＝ 物理px/画像px、100%=1.0）。
    /// ブラウザ/Photoshop 風の round な段。これにフィット倍率を暫定段として動的に挟む
    /// （<see cref="ZoomToStop"/>）。中途半端な倍率にならないよう、ホイール1ティックで隣の段へ移動する。
    /// </summary>
    private static readonly double[] ZoomStops =
    {
        0.05, 0.0833, 0.125, 0.1667, 0.25, 0.3333, 0.5, 0.6667, 0.75,
        1.0, 1.25, 1.5, 2.0, 3.0, 4.0, 6.0, 8.0, 12.0, 16.0,
    };

    // 直近のズーム状態（フィットに戻る直前の倍率・モード・相対中心）を覚えておき、
    // 再度ズーム（Z トグル）したときに同じ表示位置へ復元する。null＝記憶なし。
    private double? _rememberedScale;
    private double _rememberedRelCx = 0.5, _rememberedRelCy = 0.5;
    private ZoomMode _rememberedMode = ZoomMode.ActualSize;

    /// <summary>フィット時の倍率。</summary>
    public double FitScale
    {
        get
        {
            if (ImageWidth <= 0 || ImageHeight <= 0) return 1.0;
            if (CanvasWidth <= 0 || CanvasHeight <= 0) return 1.0;
            return Math.Min(CanvasWidth / ImageWidth, CanvasHeight / ImageHeight);
        }
    }

    /// <summary>キャンバスサイズを更新し、フィット/カスタムを維持したまま再センタリングする。</summary>
    public void SetCanvasSize(double width, double height)
    {
        CanvasWidth = width;
        CanvasHeight = height;
        ApplyMode();
    }

    /// <summary>新しい画像をセットしてフィット表示に初期化する。</summary>
    public void SetImage(double imageWidth, double imageHeight)
    {
        ImageWidth = imageWidth;
        ImageHeight = imageHeight;
        Mode = ZoomMode.Fit;
        _rememberedScale = null; // 新しい画像なので記憶ズーム位置はリセット
        ApplyMode();
    }

    /// <summary>
    /// 現在のズームモード/倍率と「キャンバス中心が指す画像上の相対位置」を維持したまま画像を差し替える。
    /// 写真切替（前後移動）でズーム表示のままにするために使う。
    /// <list type="bullet">
    ///   <item><see cref="ZoomMode.Fit"/> … 新サイズで再フィット（中央）。</item>
    ///   <item><see cref="ZoomMode.ActualSize"/> … 100%（DPI 基準）を維持。相対中心を維持。</item>
    ///   <item><see cref="ZoomMode.Custom"/> … 「フィットの何倍か」（フィット比）を維持。相対中心を維持。</item>
    /// </list>
    /// 切替前後で画像サイズが異なる場合、中心は相対位置（0..1）で保つので同じ構図位置が中心に来る
    /// （同サイズなら絶対位置と完全一致）。<see cref="Clamp"/> で新サイズの範囲に収める。
    /// <para>
    /// 注意: <see cref="SetCanvasSize"/> は内部で <see cref="ApplyMode"/>→<see cref="Center"/> を呼び
    /// Custom でも再センタリングしてしまうため、本メソッドは canvas サイズも引数で受け取り
    /// （変更前の相対中心/フィット比をキャプチャした後に）一括で更新する。
    /// </para>
    /// </summary>
    public void SetImagePreservingView(double imageWidth, double imageHeight,
                                       double canvasWidth, double canvasHeight)
    {
        // ① 変更前にキャプチャ（旧 ImageWidth/Height・旧 CanvasWidth/Height・旧 Scale 基準）。
        double relCx = 0.5, relCy = 0.5;
        if (ImageWidth > 0 && ImageHeight > 0 && Scale > 0)
        {
            relCx = Math.Clamp(((CanvasWidth / 2 - OffsetX) / Scale) / ImageWidth, 0, 1);
            relCy = Math.Clamp(((CanvasHeight / 2 - OffsetY) / Scale) / ImageHeight, 0, 1);
        }
        double zoomRatio = 1.0;
        if (Mode == ZoomMode.Custom && FitScale > 0)
            zoomRatio = Scale / FitScale; // フィットの何倍か（フィット比）を保持する

        // ② 新しいキャンバス/画像サイズへ差し替え。
        CanvasWidth = canvasWidth;
        CanvasHeight = canvasHeight;
        ImageWidth = imageWidth;
        ImageHeight = imageHeight;

        // ③ 倍率を再決定（モード別）。
        Scale = Mode switch
        {
            ZoomMode.Fit => FitScale,
            ZoomMode.ActualSize => ActualScale,
            _ => Math.Clamp(zoomRatio * FitScale, MinScale, MaxScale),
        };

        // ④ 中心を相対位置で復元（Fit は中央）。
        if (Mode == ZoomMode.Fit)
        {
            Center();
        }
        else
        {
            OffsetX = CanvasWidth / 2 - relCx * ImageWidth * Scale;
            OffsetY = CanvasHeight / 2 - relCy * ImageHeight * Scale;
        }
        Clamp();
    }

    /// <summary>フィット表示にする。</summary>
    public void SetFit()
    {
        // ズーム中からフィットへ戻るときは、戻る前の表示位置を記憶しておく
        // （次に Z でズームしたとき同じ位置へ復元するため）。
        if (Mode != ZoomMode.Fit) RememberCurrentView();
        Mode = ZoomMode.Fit;
        ApplyMode();
    }

    /// <summary>等倍（1 画像px = 1 物理px）表示にする。中心を維持。</summary>
    public void SetActualSize()
    {
        SetScaleAround(ActualScale, CanvasWidth / 2, CanvasHeight / 2);
        Mode = ZoomMode.ActualSize;
    }

    /// <summary>等倍（1 画像px = 1 物理px）を指定キャンバス座標 (cx,cy) を基準に表示する
    /// （マウスダブルクリックの 100% 用。ホイールズームと同様にカーソル下の画像点を固定）。</summary>
    public void SetActualSizeAround(double cx, double cy)
    {
        SetScaleAround(ActualScale, cx, cy);
        Mode = ZoomMode.ActualSize;
    }

    /// <summary>
    /// フィット ⇄ ズームをトグルする（SPEC §3-7 の Z キー）。
    /// フィットからズームへ戻すときは、直前にスクロールしていた表示位置（倍率・中心）を復元する
    /// （記憶がなければ等倍）。
    /// </summary>
    public void ToggleZoom()
    {
        if (Mode == ZoomMode.Fit) RestoreZoomView();
        else SetFit();
    }

    /// <summary>
    /// 指定キャンバス座標 (cx,cy) を基準にフィット ⇄ ズームをトグルする（マウスクリックズーム用）。
    /// 倍率は <see cref="ToggleZoom"/> と同じ（記憶があればその倍率/モード、無ければ等倍）だが、
    /// 中心は記憶の相対位置ではなくカーソル位置を基準にする（ホイールズームと同様）。
    /// ズーム中からの呼び出しはフィットへ戻す（座標は不要）。
    /// </summary>
    public void ToggleZoomAround(double cx, double cy)
    {
        if (Mode == ZoomMode.Fit)
        {
            double target = _rememberedScale is { } s ? Math.Clamp(s, MinScale, MaxScale) : ActualScale;
            SetScaleAround(target, cx, cy);
            Mode = _rememberedScale is not null ? _rememberedMode : ZoomMode.ActualSize;
        }
        else SetFit();
    }

    /// <summary>現在の表示状態（倍率・モード・キャンバス中心が指す相対位置）を記憶する。</summary>
    private void RememberCurrentView()
    {
        if (ImageWidth <= 0 || ImageHeight <= 0 || Scale <= 0) return;
        _rememberedScale = Scale;
        _rememberedMode = Mode;
        _rememberedRelCx = Math.Clamp(((CanvasWidth / 2 - OffsetX) / Scale) / ImageWidth, 0, 1);
        _rememberedRelCy = Math.Clamp(((CanvasHeight / 2 - OffsetY) / Scale) / ImageHeight, 0, 1);
    }

    /// <summary>記憶した表示状態（倍率・中心）を復元する。記憶がなければ等倍にする。</summary>
    private void RestoreZoomView()
    {
        if (_rememberedScale is not { } s || ImageWidth <= 0 || ImageHeight <= 0)
        {
            SetActualSize();
            return;
        }
        Scale = Math.Clamp(s, MinScale, MaxScale);
        Mode = _rememberedMode;
        OffsetX = CanvasWidth / 2 - _rememberedRelCx * ImageWidth * Scale;
        OffsetY = CanvasHeight / 2 - _rememberedRelCy * ImageHeight * Scale;
        Clamp();
    }

    /// <summary>指定キャンバス座標を中心に倍率を factor 倍する（連続ズーム。ルーペで使用）。</summary>
    public void ZoomBy(double factor, double centerX, double centerY)
    {
        var target = Math.Clamp(Scale * factor, MinScale, MaxScale);
        SetScaleAround(target, centerX, centerY);
        Mode = ZoomMode.Custom;
    }

    /// <summary>
    /// ホイール1ティックぶん、次（<paramref name="zoomIn"/>=true）／前のズーム段へスナップする
    /// （メインのホイールズーム用）。段は <see cref="ZoomStops"/>（表示倍率 DeviceScale 基準）に
    /// フィット倍率を暫定段として挟んだもの。これにより倍率が round な値に揃い、フィット付近では
    /// フィットに止まる。これ以上段がなければ何もしない。フィット段に着地したときは
    /// <see cref="ZoomMode.Fit"/>（リサイズで再フィット）にする。
    /// </summary>
    public void ZoomToStop(bool zoomIn, double centerX, double centerY)
    {
        if (ImageWidth <= 0 || ImageHeight <= 0 || Scale <= 0) return;

        double current = DeviceScale;
        double fit = FitScale * DpiScale;
        double minDev = MinScale * DpiScale, maxDev = MaxScale * DpiScale;

        // 候補段＝固定段（範囲内のみ）＋フィット段。
        var stops = new List<double>();
        foreach (var s in ZoomStops)
            if (s >= minDev - 1e-9 && s <= maxDev + 1e-9) stops.Add(s);
        if (fit > minDev && fit < maxDev) stops.Add(fit);
        stops.Sort();

        // 現在値の隣の段を選ぶ（相対イプシロンで「同じ段に居る」状態を弾く）。
        double? target = null;
        if (zoomIn)
        {
            foreach (var s in stops)
                if (s > current * (1 + 1e-3)) { target = s; break; }
        }
        else
        {
            for (int i = stops.Count - 1; i >= 0; i--)
                if (stops[i] < current * (1 - 1e-3)) { target = stops[i]; break; }
        }
        if (target is not { } dev) return; // これ以上段がない

        if (Math.Abs(dev - fit) < 1e-9)
        {
            // フィット段に着地。Fit モードへ（中央・リサイズで再フィット）。
            Mode = ZoomMode.Fit;
            ApplyMode();
        }
        else
        {
            SetScaleAround(Math.Clamp(dev / DpiScale, MinScale, MaxScale), centerX, centerY);
            Mode = ZoomMode.Custom;
        }
    }

    /// <summary>描画をドラッグで移動する。</summary>
    public void Pan(double dx, double dy)
    {
        OffsetX += dx;
        OffsetY += dy;
        Clamp();
    }

    private void ApplyMode()
    {
        Scale = Mode switch
        {
            ZoomMode.Fit => FitScale,
            ZoomMode.ActualSize => ActualScale,
            _ => Scale,
        };
        Center();
        Clamp();
    }

    /// <summary>キャンバス座標 (cx,cy) のもとの画像点を固定したまま倍率を変更する。</summary>
    private void SetScaleAround(double newScale, double cx, double cy)
    {
        newScale = Math.Clamp(newScale, MinScale, MaxScale);
        // ズーム中心の画像座標（px）を求め、倍率変更後も同じキャンバス座標に来るようオフセット調整。
        double imgX = (cx - OffsetX) / Scale;
        double imgY = (cy - OffsetY) / Scale;
        Scale = newScale;
        OffsetX = cx - imgX * Scale;
        OffsetY = cy - imgY * Scale;
        Clamp();
    }

    /// <summary>画像をキャンバス中央に配置する。</summary>
    private void Center()
    {
        OffsetX = (CanvasWidth - DrawWidth) / 2;
        OffsetY = (CanvasHeight - DrawHeight) / 2;
    }

    /// <summary>
    /// オフセットをクランプ。描画がキャンバスより小さい軸は中央寄せ、
    /// 大きい軸は端が内側に入り込まないよう範囲内に収める。
    /// </summary>
    private void Clamp()
    {
        OffsetX = ClampAxis(OffsetX, DrawWidth, CanvasWidth);
        OffsetY = ClampAxis(OffsetY, DrawHeight, CanvasHeight);
    }

    private static double ClampAxis(double offset, double drawSize, double canvasSize)
    {
        if (drawSize <= canvasSize)
            return (canvasSize - drawSize) / 2; // 収まるなら中央
        // はみ出すなら [canvasSize - drawSize, 0] に制限
        return Math.Clamp(offset, canvasSize - drawSize, 0);
    }

    /// <summary>
    /// 画像座標（Orientation 補正済み表示空間, 0..ImageWidth/Height）→ キャンバス座標への変換。
    /// AF 枠やグリッド線のオーバーレイ描画に使う。
    /// </summary>
    public (double X, double Y) ImageToCanvas(double imgX, double imgY)
        => (OffsetX + imgX * Scale, OffsetY + imgY * Scale);

    /// <summary>
    /// 現在キャンバスに見えている画像領域（表示空間 px, 0..ImageWidth/Height にクランプ）を返す。
    /// ナビゲーターの「表示領域矩形」描画に使う。見えていなければ幅/高さ 0。
    /// </summary>
    public (double X, double Y, double W, double H) VisibleImageRect()
    {
        if (ImageWidth <= 0 || ImageHeight <= 0 || Scale <= 0) return (0, 0, 0, 0);
        double left = Math.Max(0, (0 - OffsetX) / Scale);
        double top = Math.Max(0, (0 - OffsetY) / Scale);
        double right = Math.Min(ImageWidth, (CanvasWidth - OffsetX) / Scale);
        double bottom = Math.Min(ImageHeight, (CanvasHeight - OffsetY) / Scale);
        if (right <= left || bottom <= top) return (0, 0, 0, 0);
        return (left, top, right - left, bottom - top);
    }

    /// <summary>
    /// 元画像（Orientation 適用前, 幅 bw × 高さ bh）座標を Orientation 補正済み表示空間
    /// （0..DisplayW, 0..DisplayH）へ写すアフィン行列。Win2D の DrawingSession.Transform に
    /// スケール・平行移動を合成して用いる。EXIF Orientation 1..8 に対応。
    /// </summary>
    public static Matrix3x2 OrientationMatrix(int orientation, double bw, double bh)
    {
        float w = (float)bw, h = (float)bh;
        return orientation switch
        {
            2 => new Matrix3x2(-1, 0, 0, 1, w, 0),   // 水平反転
            3 => new Matrix3x2(-1, 0, 0, -1, w, h),  // 180度
            4 => new Matrix3x2(1, 0, 0, -1, 0, h),   // 垂直反転
            5 => new Matrix3x2(0, 1, 1, 0, 0, 0),    // 転置
            6 => new Matrix3x2(0, 1, -1, 0, h, 0),   // 90度 CW
            7 => new Matrix3x2(0, -1, -1, 0, h, w),  // 転地（transverse）
            8 => new Matrix3x2(0, -1, 1, 0, 0, w),   // 270度 CW (90度 CCW)
            _ => Matrix3x2.Identity,                 // 1: 通常
        };
    }

    /// <summary>
    /// 元画像 px から最終キャンバス座標への合成行列（Orientation → Scale → Offset）。
    /// </summary>
    public Matrix3x2 BuildTransform(int orientation, double bw, double bh)
        => OrientationMatrix(orientation, bw, bh)
           * Matrix3x2.CreateScale((float)Scale)
           * Matrix3x2.CreateTranslation((float)OffsetX, (float)OffsetY);
}
