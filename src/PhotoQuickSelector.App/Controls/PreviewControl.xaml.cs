using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PhotoQuickSelector.Core;
using PhotoQuickSelector_App.ViewModels;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// 右ペインの大画面プレビュー（Win2D 描画）。メインキャンバス＋下部フィルムストリップ。
/// ズーム/パン・前後ナビ・回転適用を担う。AF枠/グリッド線/ナビゲーターはステージ B 以降で追加。
/// </summary>
public sealed partial class PreviewControl : UserControl
{
    private readonly PreviewViewport _viewport = new();
    private CanvasBitmap? _bitmap;          // 現在表示中（_cache 内の参照。直接 Dispose しない）
    private ImageMetadata? _currentMeta;    // 表示中ビットマップに対応するメタデータ（AF枠描画用）
    private int _currentOrientation = 1;
    private int _loadToken;                 // 現在表示ロードの世代（高速ナビでの追い越し対策）

    // 前後 N 枚先読みキャッシュ（SPEC §4）。キーはファイルパス。
    private const int PrefetchForward = 2;
    private const int PrefetchBackward = 1;
    private readonly Dictionary<string, CanvasBitmap> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<CanvasBitmap?>> _inflight = new(StringComparer.OrdinalIgnoreCase);
    private int _cacheGeneration;           // デバイス再生成でキャッシュを無効化する世代

    private bool _isPanning;
    private Point _lastPointer;

    private MainViewModel? _viewModel;

    public PreviewControl()
    {
        InitializeComponent();

        // Esc は WinUI のフォーカス管理に先取りされ KeyDown へ届かないため、
        // フォーカスを持つキャンバスにアクセラレータを付けてサムネイル一覧へ戻す。
        // （ヒントの自動ツールチップは非表示にする）
        MainCanvas.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        var escape = new KeyboardAccelerator { Key = VirtualKey.Escape };
        escape.Invoked += (_, args) =>
        {
            _viewModel?.ExitPreview();
            args.Handled = true;
        };
        MainCanvas.KeyboardAccelerators.Add(escape);
    }

    /// <summary>
    /// 表示対象のビューモデル。<see cref="MainPage"/> が生成後に注入する。
    /// 設定時に x:Bind を更新し、選択写真の変更を購読する。
    /// </summary>
    public MainViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (ReferenceEquals(_viewModel, value)) return;
            if (_viewModel != null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = value;
            if (_viewModel != null) _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            Bindings.Update();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.SelectedPhoto):
                LoadCurrentAsync();
                ScrollSelectedIntoView();
                break;
            case nameof(MainViewModel.IsPreviewMode):
                if (_viewModel?.IsPreviewMode == true)
                {
                    LoadCurrentAsync();
                    FocusForKeys();
                    ScrollSelectedIntoView();
                }
                break;
            case nameof(MainViewModel.ShowGrid):
                MainCanvas.Invalidate();
                break;
        }
    }

    private void FocusForKeys()
    {
        // 可視化直後はレイアウト未確定なのでディスパッチしてキャンバスへフォーカスを取る。
        // （UserControl 自体は Focus が通らないことがあるため、フォーカス可能な CanvasControl を対象にする）
        DispatcherQueue.TryEnqueue(() => MainCanvas.Focus(FocusState.Programmatic));
    }

    private void ScrollSelectedIntoView()
    {
        if (_viewModel?.SelectedPhoto is { } photo)
            DispatcherQueue.TryEnqueue(() => FilmStrip.ScrollIntoView(photo));
    }

    // --- 画像ロード ---

    private async void LoadCurrentAsync()
    {
        if (_viewModel?.IsPreviewMode != true) return;

        var photo = _viewModel.SelectedPhoto;
        int token = ++_loadToken;

        if (photo == null)
        {
            _bitmap = null;
            _currentMeta = null;
            MainCanvas.Invalidate();
            return;
        }

        var bmp = await LoadAsync(photo.Meta.Path);
        if (token != _loadToken) return; // 新しい読み込みに追い越された

        _bitmap = bmp;
        _currentMeta = bmp != null ? photo.Meta : null;
        if (bmp != null)
        {
            _currentOrientation = photo.Meta.Orientation;
            _viewport.SetCanvasSize(MainCanvas.ActualWidth, MainCanvas.ActualHeight);
            // CanvasBitmap.LoadAsync は EXIF Orientation を適用済みのビットマップを返す（既に正立）。
            // よって SizeInPixels がそのまま表示サイズになる（回転は描画側で加えない）。
            // Size は DPI 依存（高 DPI で縮む）ため、寸法基準は SizeInPixels に統一する。
            _viewport.SetImage(bmp.SizeInPixels.Width, bmp.SizeInPixels.Height);
        }
        MainCanvas.Invalidate();

        PrefetchNeighbors();
        TrimCache();
    }

    /// <summary>キャッシュ優先で <see cref="CanvasBitmap"/> を取得する。読み込み中なら同一タスクを共有。</summary>
    private Task<CanvasBitmap?> LoadAsync(string path)
    {
        if (_cache.TryGetValue(path, out var cached)) return Task.FromResult<CanvasBitmap?>(cached);
        if (_inflight.TryGetValue(path, out var running)) return running;

        var task = LoadCoreAsync(path, _cacheGeneration);
        _inflight[path] = task;
        return task;
    }

    private async Task<CanvasBitmap?> LoadCoreAsync(string path, int generation)
    {
        try
        {
            var bmp = await CanvasBitmap.LoadAsync(MainCanvas, path);
            if (generation != _cacheGeneration)
            {
                bmp.Dispose(); // デバイス再生成でキャッシュが無効化された
                return null;
            }
            _cache[path] = bmp;
            return bmp;
        }
        catch
        {
            return null;
        }
        finally
        {
            _inflight.Remove(path);
        }
    }

    /// <summary>現在位置の前後 N 枚をキャッシュへ先読みする（fire-and-forget）。</summary>
    private void PrefetchNeighbors()
    {
        foreach (var photo in WindowPhotos())
            _ = LoadAsync(photo.Meta.Path);
    }

    /// <summary>保持窓（前後 N 枚）の外にあるキャッシュを破棄する。</summary>
    private void TrimCache()
    {
        var keep = new HashSet<string>(
            WindowPhotos().Select(p => p.Meta.Path), StringComparer.OrdinalIgnoreCase);

        foreach (var key in _cache.Keys.ToList())
        {
            if (keep.Contains(key)) continue;
            if (ReferenceEquals(_cache[key], _bitmap)) continue; // 表示中は破棄しない
            _cache[key].Dispose();
            _cache.Remove(key);
        }
    }

    /// <summary>現在位置を中心とした保持窓 [index-backward, index+forward] の写真。</summary>
    private IEnumerable<PhotoItemViewModel> WindowPhotos()
    {
        var vm = _viewModel;
        if (vm?.SelectedPhoto == null) yield break;
        int index = vm.Photos.IndexOf(vm.SelectedPhoto);
        if (index < 0) yield break;

        int start = Math.Max(0, index - PrefetchBackward);
        int end = Math.Min(vm.Photos.Count - 1, index + PrefetchForward);
        for (int i = start; i <= end; i++)
            yield return vm.Photos[i];
    }

    private void ClearCache()
    {
        _cacheGeneration++; // 進行中の読み込みは完了時に gen 不一致で自分を破棄する
        foreach (var bmp in _cache.Values)
            bmp.Dispose();
        _cache.Clear();
        _bitmap = null;
        _currentMeta = null;
    }

    private void MainCanvas_CreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
    {
        // デバイス生成・ロストからの復帰時はキャッシュを破棄して再ロード。
        if (args.Reason == CanvasCreateResourcesReason.NewDevice ||
            args.Reason == CanvasCreateResourcesReason.DpiChanged)
        {
            ClearCache();
            LoadCurrentAsync();
        }
    }

    // --- 描画 ---

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

    /// <summary>
    /// Sony AF フォーカス枠を描く。フォーカス点・枠は「生センサー px（Orientation 適用前）」の値なので、
    /// <see cref="PreviewViewport.OrientationMatrix"/> で表示空間（正立ビットマップ座標）へ写してから
    /// <see cref="PreviewViewport.ImageToCanvas"/> でキャンバスへ変換する。線幅はキャンバス空間で固定。
    /// </summary>
    private void DrawFocusFrame(CanvasDrawingSession ds)
    {
        if (_currentMeta is not { } meta || meta.FocusPoint is not { } fp || _bitmap == null) return;

        double rawW = meta.OriginalWidth, rawH = meta.OriginalHeight; // 生センサー寸法（Orientation 前）
        if (rawW <= 0 || rawH <= 0) return;
        double refW = meta.FocusReferenceSize is { Width: > 0 } rs ? rs.Width : rawW;
        double refH = meta.FocusReferenceSize is { Height: > 0 } rs2 ? rs2.Height : rawH;

        var om = PreviewViewport.OrientationMatrix(_currentOrientation, rawW, rawH);
        double cx = fp.X * rawW / refW;
        double cy = fp.Y * rawH / refH;

        var color = Color.FromArgb(0xEE, 0x66, 0xFF, 0x66); // 半透明グリーン

        if (meta.FocusSize is { Width: > 0, Height: > 0 } fs)
        {
            double fw = fs.Width * rawW / refW;
            double fh = fs.Height * rawH / refH;
            // 生 px の矩形 4 隅 → 表示空間 → キャンバス（90/270 度回転で軸が入替わっても矩形は軸平行のまま）。
            var o0 = Vector2.Transform(new Vector2((float)(cx - fw / 2), (float)(cy - fh / 2)), om);
            var o1 = Vector2.Transform(new Vector2((float)(cx + fw / 2), (float)(cy + fh / 2)), om);
            var (x0, y0) = _viewport.ImageToCanvas(o0.X, o0.Y);
            var (x1, y1) = _viewport.ImageToCanvas(o1.X, o1.Y);
            float x = (float)Math.Min(x0, x1), y = (float)Math.Min(y0, y1);
            ds.DrawRectangle(x, y, (float)Math.Abs(x1 - x0), (float)Math.Abs(y1 - y0), color, 2f);
        }
        else
        {
            var o = Vector2.Transform(new Vector2((float)cx, (float)cy), om);
            var (px, py) = _viewport.ImageToCanvas(o.X, o.Y);
            const float radius = 8f;
            ds.DrawRectangle((float)px - radius, (float)py - radius, 2 * radius, 2 * radius, color, 2f);
        }
    }

    /// <summary>AF フォーカス点（無ければ画像中心）が画面中央へ来るよう 100% 表示でスクロールする。</summary>
    private void ScrollToFocus()
    {
        if (_bitmap == null) return;

        _viewport.SetActualSize(); // スクロールできるよう等倍にする

        double dispX, dispY;
        if (_currentMeta is { FocusPoint: { } fp } meta && meta.OriginalWidth > 0 && meta.OriginalHeight > 0)
        {
            double rawW = meta.OriginalWidth, rawH = meta.OriginalHeight;
            double refW = meta.FocusReferenceSize is { Width: > 0 } rs ? rs.Width : rawW;
            double refH = meta.FocusReferenceSize is { Height: > 0 } rs2 ? rs2.Height : rawH;
            var disp = Vector2.Transform(
                new Vector2((float)(fp.X * rawW / refW), (float)(fp.Y * rawH / refH)),
                PreviewViewport.OrientationMatrix(_currentOrientation, rawW, rawH));
            dispX = disp.X;
            dispY = disp.Y;
        }
        else
        {
            dispX = _viewport.ImageWidth / 2;
            dispY = _viewport.ImageHeight / 2;
        }

        var (curX, curY) = _viewport.ImageToCanvas(dispX, dispY);
        _viewport.Pan(MainCanvas.ActualWidth / 2 - curX, MainCanvas.ActualHeight / 2 - curY);
        MainCanvas.Invalidate();
    }

    private void MainCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _viewport.SetCanvasSize(MainCanvas.ActualWidth, MainCanvas.ActualHeight);
        MainCanvas.Invalidate();
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
        MainCanvas.Invalidate();
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
        MainCanvas.Invalidate();
        e.Handled = true;
    }

    private void MainCanvas_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // 大画面のダブルクリックでサムネイル一覧へ戻る（SPEC §2）。
        _viewModel?.ExitPreview();
        e.Handled = true;
    }

    // --- キー操作（ナビ / ズーム / スクロール / 評価） ---

    /// <summary>Alt+矢印スクロール 1 回あたりの移動量（DIP）。</summary>
    private const double PanStep = 120;

    /// <summary>
    /// プレビューのキー操作（ナビ / ズーム / スクロール / 評価）を処理する。処理したら true。
    /// Window 直下のルート集約ハンドラ（<see cref="MainPage.HandleGlobalKeyDown"/>）から呼ばれる。
    /// 画像クリックでフォーカスがキャンバスから外れていても効くよう、フォーカス非依存で実行する。
    /// </summary>
    public bool HandleKeyDown(VirtualKey key)
    {
        if (_viewModel == null) return false;

        bool alt = KeyboardModifiers.Alt;

        // Shift+Alt+←/→ : フィット / 100%（SPEC §3-7）
        if (alt && KeyboardModifiers.Shift)
        {
            switch (key)
            {
                case VirtualKey.Left: _viewport.SetFit(); MainCanvas.Invalidate(); return true;
                case VirtualKey.Right: _viewport.SetActualSize(); MainCanvas.Invalidate(); return true;
            }
        }

        // Alt+矢印 : ズーム画像をスクロール（パン） / Alt+F : フォーカス点へスクロール
        if (alt)
        {
            switch (key)
            {
                case VirtualKey.Left: _viewport.Pan(PanStep, 0); MainCanvas.Invalidate(); return true;
                case VirtualKey.Right: _viewport.Pan(-PanStep, 0); MainCanvas.Invalidate(); return true;
                case VirtualKey.Up: _viewport.Pan(0, PanStep); MainCanvas.Invalidate(); return true;
                case VirtualKey.Down: _viewport.Pan(0, -PanStep); MainCanvas.Invalidate(); return true;
                case VirtualKey.F: ScrollToFocus(); return true;
            }
        }

        // 修飾子なしの ←/→ : 前後移動
        if (KeyboardModifiers.None)
        {
            switch (key)
            {
                case VirtualKey.Left: _viewModel.MovePrevious(); return true;
                case VirtualKey.Right: _viewModel.MoveNext(); return true;
            }
        }

        // Esc は KeyboardAccelerator 側で処理（KeyDown には届かないため）。
        // Z : フィット ⇄ 100% トグル / Shift+Z : 100%
        if (key == VirtualKey.Z)
        {
            if (KeyboardModifiers.Shift) _viewport.SetActualSize();
            else _viewport.ToggleZoom();
            MainCanvas.Invalidate();
            return true;
        }

        // G : 三分割グリッド線トグル（ShowGrid 変更で再描画される）
        if (KeyboardModifiers.None && key == VirtualKey.G)
        {
            _viewModel.ShowGrid = !_viewModel.ShowGrid;
            return true;
        }

        // 評価キー（rating / flag / colorlabel）はサムネイル一覧と共通化（SPEC §3-7）。
        if (_viewModel.SelectedPhoto is { } photo && PhotoKeyCommands.TryHandleEvaluation(key, photo))
            return true;

        return false;
    }
}
