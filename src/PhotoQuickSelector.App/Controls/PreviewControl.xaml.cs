using System.ComponentModel;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PhotoQuickSelector_App.ViewModels;
using Windows.Foundation;
using Windows.System;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// 右ペインの大画面プレビュー（Win2D 描画）。メインキャンバス＋下部フィルムストリップ。
/// ズーム/パン・前後ナビ・回転適用を担う。AF枠/グリッド線/ナビゲーターはステージ B 以降で追加。
/// </summary>
public sealed partial class PreviewControl : UserControl
{
    private readonly PreviewViewport _viewport = new();
    private CanvasBitmap? _bitmap;
    private int _currentOrientation = 1;
    private int _loadToken;

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
            _bitmap?.Dispose();
            _bitmap = null;
            MainCanvas.Invalidate();
            return;
        }

        try
        {
            var bmp = await CanvasBitmap.LoadAsync(MainCanvas, photo.Meta.Path);
            if (token != _loadToken)
            {
                bmp.Dispose(); // 新しい読み込みに追い越されたので破棄
                return;
            }
            _bitmap?.Dispose();
            _bitmap = bmp;
            _currentOrientation = photo.Meta.Orientation;

            _viewport.SetCanvasSize(MainCanvas.ActualWidth, MainCanvas.ActualHeight);
            _viewport.SetImage(photo.Meta.Width, photo.Meta.Height);
            MainCanvas.Invalidate();
        }
        catch
        {
            if (token == _loadToken)
            {
                _bitmap = null;
                MainCanvas.Invalidate();
            }
        }
    }

    private void MainCanvas_CreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
    {
        // デバイス生成・ロストからの復帰時に再ロード。
        if (args.Reason == CanvasCreateResourcesReason.NewDevice ||
            args.Reason == CanvasCreateResourcesReason.DpiChanged)
        {
            _bitmap = null;
            LoadCurrentAsync();
        }
    }

    // --- 描画 ---

    private void MainCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_bitmap == null) return;

        var ds = args.DrawingSession;
        var saved = ds.Transform;
        ds.Transform = _viewport.BuildTransform(
            _currentOrientation, _bitmap.Size.Width, _bitmap.Size.Height);
        ds.DrawImage(_bitmap);
        ds.Transform = saved;
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

    // --- キー操作（ステージ A 範囲: ナビ / ズーム / 戻る） ---

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_viewModel == null) return;

        switch (e.Key)
        {
            case VirtualKey.Left:
                _viewModel.MovePrevious();
                e.Handled = true;
                return;
            case VirtualKey.Right:
                _viewModel.MoveNext();
                e.Handled = true;
                return;
            // Esc は KeyboardAccelerator 側で処理（KeyDown には届かないため）。
            case VirtualKey.Z:
                if (KeyboardModifiers.Shift) _viewport.SetActualSize();
                else _viewport.ToggleZoom();
                MainCanvas.Invalidate();
                e.Handled = true;
                return;
        }
    }
}
