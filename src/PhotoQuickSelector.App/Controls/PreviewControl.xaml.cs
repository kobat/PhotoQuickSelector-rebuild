using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PhotoQuickSelector.Core;
using PhotoQuickSelector_App.ViewModels;
using Windows.Foundation;
using Windows.System;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// 右ペインの大画面プレビュー（Win2D 描画）。メイン大画面＋フィルムストリップ＋右パネル
/// （ズームルーペ／ナビゲーター）の 3 面構成。ズーム/パン・前後ナビ・AF枠/グリッド線を担う。
/// <para>
/// 実装は関心事ごとに partial で分割している:
/// <list type="bullet">
///   <item><c>PreviewControl.xaml.cs</c> … 骨組み・ViewModel 配線・ライフサイクル（本ファイル）</item>
///   <item><c>PreviewControl.MainCanvas.cs</c> … メイン描画・パン/ズーム・ポインタ</item>
///   <item><c>PreviewControl.Overlays.cs</c> … 三分割グリッド線・AF フォーカス枠</item>
///   <item><c>PreviewControl.Loupe.cs</c> … 右上ズームプレビュー（100% ルーペ）</item>
///   <item><c>PreviewControl.Navigator.cs</c> … ナビゲーター（全体縮小＋表示領域矩形）</item>
///   <item><c>PreviewControl.Input.cs</c> … キー処理（<see cref="HandleKeyDown"/>）</item>
/// </list>
/// 先読みキャッシュは <see cref="PreviewBitmapCache"/> に分離。
/// </para>
/// </summary>
public sealed partial class PreviewControl : UserControl
{
    private readonly PreviewViewport _viewport = new();
    private readonly PreviewViewport _zoomViewport = new();  // 右上ズームプレビュー（100% ルーペ）の独立ビューポート
    private readonly PreviewBitmapCache _cache;              // 前後 N 枚先読みキャッシュ（SPEC §4）
    private CanvasBitmap? _bitmap;          // 現在表示中（_cache 内の参照。直接 Dispose しない）
    private ImageMetadata? _currentMeta;    // 表示中ビットマップに対応するメタデータ（AF枠描画用）
    private int _currentOrientation = 1;
    private int _loadToken;                 // 現在表示ロードの世代（高速ナビでの追い越し対策）

    // 先読みキャッシュの保持窓（現在位置の前後 N 枚）。
    private const int PrefetchForward = 2;
    private const int PrefetchBackward = 1;

    private bool _isPanning;
    private Point _lastPointer;

    private bool _isZoomPanning;            // 右上ズームプレビューのドラッグ中
    private Point _zoomLastPointer;
    private bool _isNavPanning;             // ナビゲーターのドラッグ中

    private MainViewModel? _viewModel;

    /// <summary>キャッシュ中の画像ファイル名一覧（デバッグオーバーレイ用。C キーでトグル）。</summary>
    public ObservableCollection<string> CachedFileNames { get; } = new();

    public PreviewControl()
    {
        InitializeComponent();
        _cache = new PreviewBitmapCache(MainCanvas);
        _cache.Changed += RefreshCacheOverlay;

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

    // フィルムストリップも可視コンテナの分だけサムネイルをデコード/破棄（メモリは枚数に依存しない）。
    private void FilmStrip_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        => ThumbnailContainerLoader.Handle(args, "FilmThumbImage", decodePixelWidth: 90);

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
            InvalidateAll();
            return;
        }

        var bmp = await _cache.GetAsync(photo.Meta.Path);
        if (token != _loadToken) return; // 新しい読み込みに追い越された

        _bitmap = bmp;
        _currentMeta = bmp != null ? photo.Meta : null;
        if (bmp != null)
        {
            _currentOrientation = photo.Meta.Orientation;
            double w = bmp.SizeInPixels.Width, h = bmp.SizeInPixels.Height;
            // CanvasBitmap.LoadAsync は EXIF Orientation を適用済みのビットマップを返す（既に正立）。
            // よって SizeInPixels がそのまま表示サイズになる（回転は描画側で加えない）。
            // Size は DPI 依存（高 DPI で縮む）ため、寸法基準は SizeInPixels に統一する。
            _viewport.SetCanvasSize(MainCanvas.ActualWidth, MainCanvas.ActualHeight);
            _viewport.SetImage(w, h);

            // 右上ズームプレビューは 100% 表示で AF フォーカス点へ寄せる（旧アプリ準拠）。
            _zoomViewport.SetCanvasSize(ZoomCanvas.ActualWidth, ZoomCanvas.ActualHeight);
            _zoomViewport.SetImage(w, h);
            _zoomViewport.SetActualSize();
            ScrollZoomToFocus();
        }
        InvalidateAll();

        _cache.Prefetch(WindowPaths());
        _cache.Trim(WindowPaths(), _bitmap);
    }

    /// <summary>メイン・ズームプレビュー・ナビゲーターの 3 キャンバスを再描画する。</summary>
    private void InvalidateAll()
    {
        MainCanvas.Invalidate();
        ZoomCanvas.Invalidate();
        NavCanvas.Invalidate();
    }

    /// <summary>メイン操作（ズーム/パン）後に呼ぶ。メインとナビ（表示領域矩形が追従）を再描画する。</summary>
    private void InvalidateMain()
    {
        MainCanvas.Invalidate();
        NavCanvas.Invalidate();
    }

    /// <summary>デバイス再生成・ロスト復帰時はキャッシュを破棄して再ロードする。</summary>
    private void ResetCacheAndReload()
    {
        _cache.Clear();
        _bitmap = null;
        _currentMeta = null;
        LoadCurrentAsync();
    }

    /// <summary>
    /// キャッシュ内容のデバッグオーバーレイ（<see cref="CacheOverlay"/>）を最新化する。
    /// キャッシュ変更通知（<see cref="PreviewBitmapCache.Changed"/>）から呼ばれ、表示中のときだけ更新する。
    /// 通知は非同期ロード継続（UI スレッド）から来るが、安全のため UI スレッドへ束ねる。
    /// </summary>
    private void RefreshCacheOverlay()
    {
        if (CacheOverlay.Visibility != Visibility.Visible) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            CachedFileNames.Clear();
            foreach (var name in _cache.SnapshotFileNames())
                CachedFileNames.Add(name);
        });
    }

    /// <summary>現在位置を中心とした保持窓 [index-backward, index+forward] のファイルパス。</summary>
    private IEnumerable<string> WindowPaths()
    {
        var vm = _viewModel;
        if (vm?.SelectedPhoto == null) yield break;
        int index = vm.Photos.IndexOf(vm.SelectedPhoto);
        if (index < 0) yield break;

        int start = Math.Max(0, index - PrefetchBackward);
        int end = Math.Min(vm.Photos.Count - 1, index + PrefetchForward);
        for (int i = start; i <= end; i++)
            yield return vm.Photos[i].Meta.Path;
    }
}
