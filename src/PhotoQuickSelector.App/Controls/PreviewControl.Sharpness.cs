using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using PhotoQuickSelector.Core;
using PhotoQuickSelector_App.ViewModels;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// 鮮鋭度スコア（<see cref="MainViewModel.SharpnessMode"/> ＝ None/Tenengrad/全手法。S キーで巡回）の計算・表示。
/// <para>
/// 2 系統の計算経路がある:
/// <list type="bullet">
///   <item>
///   <b>先読み便乗（<see cref="OnFrameDecoded"/>）</b>: <see cref="PreviewBitmapCache"/> がデコードした
///   フレームすべて（先読み分も含む）に対し、Tenengrad のみをデコードのワーカースレッド上で即座に計算する。
///   モード ON の間フォルダ内の写真が広く埋まっていく（追加コストはデコードのおまけ程度）。
///   </item>
///   <item>
///   <b>焦点写真の確定計算（<see cref="EnsureFocusedSharpness"/>）</b>: 停止後（settle）に焦点写真だけを
///   対象に、未計算なら Tenengrad を、全手法モードならさらに比較 4 手法（<see cref="SharpnessMetrics"/>）
///   も計算する。先読み便乗が既に埋めていれば何もしない。デコード済みバッファは
///   <see cref="PreviewBitmapCache.TryLease"/> で借りて使う（Trim によるプール返却からの保護）。
///   </item>
/// </list>
/// いずれも UI スレッドの <see cref="MainViewModel"/>/<see cref="PhotoItemViewModel"/> への書き込みは
/// <see cref="Microsoft.UI.Dispatching.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueueHandler)"/>
/// 経由でのみ行う（ワーカースレッドから直接代入しない）。
/// </para>
/// </summary>
public sealed partial class PreviewControl
{
    /// <summary>Tenengrad 計算に使う既定オプション（タイル 256px・閾値 32。CLAUDE.md の SharpnessAnalyzer 既定と同じ）。</summary>
    private static readonly SharpnessOptions DefaultSharpnessOptions = new();

    /// <summary>
    /// 現在の鮮鋭度表示モード。<see cref="OnFrameDecoded"/> はワーカースレッドから呼ばれるため、
    /// <see cref="MainViewModel.SharpnessMode"/>（UI スレッド専用の観測可能プロパティ）へ直接触れず、
    /// この volatile フィールドを UI スレッド側で都度ミラーして読む。
    /// </summary>
    private volatile SharpnessMode _sharpnessModeSnapshot;

    /// <summary>鮮鋭度の値変更を監視中の写真（焦点写真に追従。<c>PreviewControl.OverlayFade.cs</c> の
    /// <c>_overlayWatchedPhoto</c> と同じ「付け替え式」で購読漏れ/リークを防ぐ）。</summary>
    private PhotoItemViewModel? _sharpnessWatchedPhoto;

    /// <summary>
    /// 画像情報パネル／ルーペオーバーレイの「鮮鋭度」グループ（行オブジェクトを使い回す）。
    /// XAML（<c>SharpnessLoupeOverlay</c> の <c>x:Bind</c>）から参照するため public にする
    /// （<see cref="CachedFileNames"/> と同じ理由）。
    /// </summary>
    private SharpnessInfoSection? _sharpnessSection;

    public SharpnessInfoSection SharpnessSection => _sharpnessSection ??= new SharpnessInfoSection();

    /// <summary>焦点写真の確定計算ジョブ（1 本のみ。新しいジョブ開始・焦点/モード変更・アンロードで取り消す）。</summary>
    private CancellationTokenSource? _sharpnessCts;

    /// <summary>ViewModel 注入時（起動時）の初期状態合わせ。<see cref="ViewModel"/> セッターから呼ぶ。</summary>
    private void InitializeSharpnessForViewModel()
    {
        if (_viewModel == null) return;
        _sharpnessModeSnapshot = _viewModel.SharpnessMode;
        SubscribeSharpnessWatchedPhoto(_viewModel.FocusedPhoto);
        UpdateSharpnessLoupeOverlayVisibility();
    }

    /// <summary>
    /// デコード完了フック（<see cref="PreviewBitmapCache.FrameDecoded"/>）。**ワーカースレッド**から呼ばれる
    /// （デコードを実行している Task.Run の中＝DecodeGate のスロットを握ったまま）ため、軽量な Tenengrad
    /// 計算のみをここで行い、UI への反映は <c>DispatcherQueue.TryEnqueue</c> に委ねる。
    /// 先読み分も含め、フォルダ内のデコード済み写真を広く埋める役目（比較 4 手法はここでは計算しない＝
    /// 焦点写真だけの <see cref="EnsureFocusedSharpness"/> が担当）。
    /// </summary>
    private void OnFrameDecoded(string path, PixelFrame frame)
    {
        if (_sharpnessModeSnapshot == SharpnessMode.None) return;
        var vm = _viewModel;
        if (vm == null) return;
        if (!vm.TryGetPhotoByPath(path, out var photo)) return;
        if (photo.Sharpness != null) return; // 既に計算済み（焦点計算・別デコードいずれか経由）

        try
        {
            var afWindow = SharpnessAnalyzer.AfWindowFor(photo.Meta, frame.Width, frame.Height);
            var score = SharpnessAnalyzer.Analyze(
                frame.Bytes, frame.Width, frame.Height, frame.Width * 4, DefaultSharpnessOptions, afWindow);
            DispatcherQueue.TryEnqueue(() => photo.Sharpness = score);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OnFrameDecoded sharpness calc failed: {ex}");
        }
    }

    /// <summary>
    /// 進行中の確定計算ジョブを取り消す（焦点変更・モード変更・アンロード・新規ジョブ開始のいずれでも呼ぶ）。
    /// </summary>
    private void CancelSharpnessJob()
    {
        _sharpnessCts?.Cancel();
        _sharpnessCts = null;
    }

    /// <summary>
    /// 焦点写真の鮮鋭度を確定計算する。呼び出し元は (a) 停止後の settle タイマ（<see cref="RenderExifForFocus"/>
    /// と同じ箇所）(b) モード変更 (c) プレビュー入場。モード None・写真なしなら計算せず取り消すだけ。
    /// 未計算な分だけ計算する（Tenengrad が既にあれば飛ばす／全手法モードで比較 4 手法が既にあれば飛ばす）。
    /// 焦点写真の frame がまだキャッシュに無ければ何もしない（settle 経路がロード完了後に再度呼ぶため
    /// 取りこぼさない）。
    /// </summary>
    private void EnsureFocusedSharpness()
    {
        CancelSharpnessJob();

        if (_viewModel?.IsPreviewMode != true) return;
        var mode = _viewModel.SharpnessMode;
        if (mode == SharpnessMode.None) return;

        var photo = _viewModel.FocusedPhoto;
        if (photo == null) return;

        bool needTenengrad = photo.Sharpness == null;
        bool needExtras = mode == SharpnessMode.All && photo.SharpnessExtras == null;
        if (!needTenengrad && !needExtras) return;

        if (!_cache.TryLease(photo.Meta.Path, out var lease)) return; // 未デコード。settle 再訪で拾う。

        var cts = new CancellationTokenSource();
        _sharpnessCts = cts;
        _ = ComputeFocusedSharpnessAsync(photo, lease, needTenengrad, needExtras, cts.Token);
    }

    /// <summary>
    /// <see cref="EnsureFocusedSharpness"/> の本体（ワーカースレッドで計算・UI スレッドへ反映）。
    /// リース（<paramref name="lease"/>）は完了/キャンセル/例外いずれの経路でも必ず破棄する。
    /// </summary>
    private async Task ComputeFocusedSharpnessAsync(
        PhotoItemViewModel photo, FrameLease lease, bool computeTenengrad, bool computeExtras,
        CancellationToken token)
    {
        try
        {
            var frame = lease.Frame;
            var meta = photo.Meta;
            var afWindow = SharpnessAnalyzer.AfWindowFor(meta, frame.Width, frame.Height);

            if (computeTenengrad)
            {
                var score = await Task.Run(
                    () => SharpnessAnalyzer.Analyze(
                        frame.Bytes, frame.Width, frame.Height, frame.Width * 4,
                        DefaultSharpnessOptions, afWindow, token),
                    token);
                token.ThrowIfCancellationRequested();
                DispatcherQueue.TryEnqueue(() => photo.Sharpness = score);
            }

            if (computeExtras)
            {
                var extras = await Task.Run(() =>
                {
                    // 全 4 手法完了して初めて 1 個のレコードにまとめる（部分結果は保持しない仕様）。
                    var lapv = SharpnessMetrics.Compute(
                        SharpnessMetric.LaplacianVariance, frame.Bytes, frame.Width, frame.Height,
                        frame.Width * 4, DefaultSharpnessOptions.TileSize, afWindow,
                        cancellationToken: token);
                    token.ThrowIfCancellationRequested();

                    var brenner = SharpnessMetrics.Compute(
                        SharpnessMetric.Brenner, frame.Bytes, frame.Width, frame.Height,
                        frame.Width * 4, DefaultSharpnessOptions.TileSize, afWindow,
                        cancellationToken: token);
                    token.ThrowIfCancellationRequested();

                    var reblur = SharpnessMetrics.Compute(
                        SharpnessMetric.Reblur, frame.Bytes, frame.Width, frame.Height,
                        frame.Width * 4, DefaultSharpnessOptions.TileSize, afWindow,
                        cancellationToken: token);
                    token.ThrowIfCancellationRequested();

                    var edgeWidth = SharpnessMetrics.Compute(
                        SharpnessMetric.EdgeWidth, frame.Bytes, frame.Width, frame.Height,
                        frame.Width * 4, DefaultSharpnessOptions.TileSize, afWindow,
                        threshold: DefaultSharpnessOptions.Threshold, cancellationToken: token);

                    return new SharpnessExtraScores(lapv, brenner, reblur, edgeWidth);
                }, token);
                token.ThrowIfCancellationRequested();
                DispatcherQueue.TryEnqueue(() => photo.SharpnessExtras = extras);
            }
        }
        catch (OperationCanceledException)
        {
            // 焦点変更・モード変更・アンロードによる取り消しは正常系（何もしない）。
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"EnsureFocusedSharpness calc failed: {ex}");
        }
        finally
        {
            lease.Dispose();
        }
    }

    /// <summary>
    /// 鮮鋭度の値変更を監視する対象を付け替える。焦点写真が変わるたびに
    /// <see cref="OnViewModelPropertyChanged"/>（FocusedPhoto ケース）から呼ぶ。
    /// </summary>
    private void SubscribeSharpnessWatchedPhoto(PhotoItemViewModel? photo)
    {
        if (ReferenceEquals(_sharpnessWatchedPhoto, photo)) return;
        if (_sharpnessWatchedPhoto != null)
            _sharpnessWatchedPhoto.PropertyChanged -= OnSharpnessWatchedPhotoPropertyChanged;
        _sharpnessWatchedPhoto = photo;
        if (_sharpnessWatchedPhoto != null)
            _sharpnessWatchedPhoto.PropertyChanged += OnSharpnessWatchedPhotoPropertyChanged;
    }

    /// <summary>
    /// 焦点写真が変わった直後に、ルーペオーバーレイ／画像情報パネルの行を新しい写真の値で書き直す。
    /// 先読み便乗で既に計算済みの写真は切替後に値変更イベントが起きない（＝<see cref="OnSharpnessWatchedPhotoPropertyChanged"/>
    /// が発火しない）ため、この明示更新が無いと前の写真の値が残り続ける。未計算なら「計算中…」になり、
    /// 計算完了のイベントで値へ置き換わる。
    /// </summary>
    private void RefreshSharpnessRowsForFocus()
    {
        if (_viewModel?.SharpnessMode is not { } mode || mode == SharpnessMode.None) return;
        if (_viewModel.FocusedPhoto is { } photo) SharpnessSection.Update(photo, mode);
    }

    /// <summary>
    /// 焦点写真の <see cref="PhotoItemViewModel.Sharpness"/>/<see cref="PhotoItemViewModel.SharpnessExtras"/>
    /// が変わったら、画像情報パネル／ルーペオーバーレイの行を差分更新する（ListView 再構築なし＝
    /// <see cref="EvaluationInfoSection"/> と同じ流儀）。
    /// </summary>
    private void OnSharpnessWatchedPhotoPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PhotoItemViewModel.Sharpness) &&
            e.PropertyName != nameof(PhotoItemViewModel.SharpnessExtras))
            return;
        if (_viewModel?.SharpnessMode is not { } mode || mode == SharpnessMode.None) return;
        if (sender is not PhotoItemViewModel photo || !ReferenceEquals(photo, _viewModel.FocusedPhoto)) return;

        SharpnessSection.Update(photo, mode);
    }

    /// <summary>
    /// ルーペオーバーレイ（<c>SharpnessLoupeOverlay</c>）の表示可否を更新する。ルーペ表示中
    /// （画像情報パネルでない）かつモード None でないときのみ表示。<see cref="SetExifPanelVisible"/>・
    /// モード変更・ViewModel 注入・プレビュー入場から呼ぶ。
    /// </summary>
    private void UpdateSharpnessLoupeOverlayVisibility()
    {
        bool show = !_showExifPanel && _sharpnessModeSnapshot != SharpnessMode.None;
        SharpnessLoupeOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show && _viewModel?.FocusedPhoto is { } photo)
            SharpnessSection.Update(photo, _sharpnessModeSnapshot);
    }

    /// <summary>コントロールのアンロードで進行中の確定計算ジョブを取り消す（<c>PreviewControl.xaml</c> の
    /// <c>Unloaded</c> から呼ばれる）。</summary>
    private void PreviewControl_Unloaded(object sender, RoutedEventArgs e) => CancelSharpnessJob();
}
