using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PhotoQuickSelector_App.ViewModels;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// 右ペインのサムネイルグリッド。共有 <see cref="MainViewModel"/> を <see cref="MainPage"/> が注入する。
/// 焦点は <c>ViewModel.FocusedPhoto</c> と双方向同期: グリッド選択→VM、VM の変更（プレビュー前後移動・
/// フィルムストリップ選択）→グリッドの選択へ反映する。ダブルクリックで大画面プレビューへ遷移。
/// </summary>
public sealed partial class PhotoGridView : UserControl
{
    private MainViewModel? _viewModel;

    // サムネイルのデコード/破棄＋デコード済み LRU。アンカーは背景先読みの中心として VM へ通知。
    private readonly ThumbnailContainerLoader _loader;

    public PhotoGridView()
    {
        InitializeComponent();
        _loader = new ThumbnailContainerLoader(
            "ThumbImage", decodePixelWidth: 200, capacity: 150,
            onAnchor: i => _viewModel?.NotePrefetchAnchor(i));
    }

    /// <summary>表示対象のビューモデル。<see cref="MainPage"/> が生成後に注入する。</summary>
    public MainViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (ReferenceEquals(_viewModel, value)) return;
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                ((INotifyCollectionChanged)_viewModel.Photos).CollectionChanged -= OnPhotosChanged;
            }
            _viewModel = value;
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
                ((INotifyCollectionChanged)_viewModel.Photos).CollectionChanged += OnPhotosChanged;
            }
            Bindings.Update();
        }
    }

    // フォルダ再読込（Photos リセット）で旧フォルダのデコード済み画像を解放。
    private void OnPhotosChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset) _loader.Clear();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.FocusedPhoto)) return;
        // プレビューでの前後移動・フィルムストリップ選択をサムネイルグリッドへ反映。
        if (!ReferenceEquals(PhotoGrid.SelectedItem, _viewModel?.FocusedPhoto))
            PhotoGrid.SelectedItem = _viewModel?.FocusedPhoto;
    }

    private void PhotoGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null) return;
        // VM→グリッド反映（OnViewModelPropertyChanged や下の復元代入）で発火したエコーは無視。
        // これが無いと、Ctrl+←/→（キーボード焦点移動）の反映時に Ctrl 押下状態の SelectionChanged が
        // 発火し、マウス修飾クリックと誤判定してトグルしてしまう。
        if (ReferenceEquals(PhotoGrid.SelectedItem, _viewModel.FocusedPhoto)) return;
        if (SelectionMouseCommands.TryHandle(e, _viewModel))
        {
            // Ctrl+クリックの解除で SelectedItem=null になるケースを含め、グリッド選択を焦点へ復元。
            // この代入で再発火する SelectionChanged は上のエコー判定で止まる。
            PhotoGrid.SelectedItem = _viewModel.FocusedPhoto;
            return;
        }
        // 素のクリック＝焦点移動（メンバー上なら集合維持、集合外なら集合リセット＝決定2）
        _viewModel.FocusByClick(PhotoGrid.SelectedItem as PhotoItemViewModel);
    }

    private void PhotoGrid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // サムネイルのダブルクリックで大画面プレビューへ（SPEC §2）。
        _viewModel?.EnterPreview();
    }

    // サムネイルの右クリックで操作メニューを表示（対象確定はエクスプローラ流儀＝PhotoContextMenu）。
    private void PhotoGrid_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (_viewModel == null) return;
        var clicked = (e.OriginalSource as FrameworkElement)?.DataContext as PhotoItemViewModel;
        PhotoContextMenu.Show(PhotoGrid, e.GetPosition(PhotoGrid), clicked, _viewModel, XamlRoot);
        e.Handled = true;
    }

    // 可視コンテナの分だけサムネイルをデコード/破棄（メモリは枚数に依存しない）。
    private void PhotoGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        => _loader.Handle(args);
}
