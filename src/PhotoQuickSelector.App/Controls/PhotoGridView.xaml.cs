using System.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PhotoQuickSelector_App.ViewModels;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// 右ペインのサムネイルグリッド。共有 <see cref="MainViewModel"/> を <see cref="MainPage"/> が注入する。
/// 選択は <c>ViewModel.SelectedPhoto</c> と双方向同期: グリッド選択→VM、VM の変更（プレビュー前後移動・
/// フィルムストリップ選択）→グリッドの選択へ反映する。ダブルクリックで大画面プレビューへ遷移。
/// </summary>
public sealed partial class PhotoGridView : UserControl
{
    private MainViewModel? _viewModel;

    public PhotoGridView() => InitializeComponent();

    /// <summary>表示対象のビューモデル。<see cref="MainPage"/> が生成後に注入する。</summary>
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
        if (e.PropertyName != nameof(MainViewModel.SelectedPhoto)) return;
        // プレビューでの前後移動・フィルムストリップ選択をサムネイルグリッドへ反映。
        if (!ReferenceEquals(PhotoGrid.SelectedItem, _viewModel?.SelectedPhoto))
            PhotoGrid.SelectedItem = _viewModel?.SelectedPhoto;
    }

    private void PhotoGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.SelectedPhoto = PhotoGrid.SelectedItem as PhotoItemViewModel;
    }

    private void PhotoGrid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // サムネイルのダブルクリックで大画面プレビューへ（SPEC §2）。
        _viewModel?.EnterPreview();
    }
}
