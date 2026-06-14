using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PhotoQuickSelector.Core;
using PhotoQuickSelector_App.ViewModels;
using Windows.System;

namespace PhotoQuickSelector_App;

/// <summary>
/// メイン画面。左=フォルダツリー / 右=サムネイル一覧の左右分割レイアウト。
/// </summary>
public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel { get; } = new();

    /// <summary>フォルダツリーのルート（ドライブ）。TreeView の ItemsSource。</summary>
    public ObservableCollection<FolderNode> RootFolders { get; } = new();

    private double _lastLeftWidth = 300;

    public MainPage()
    {
        InitializeComponent();
        Loaded += MainPage_Loaded;
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (RootFolders.Count > 0) return;
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            RootFolders.Add(new FolderNode(drive.Name, drive.RootDirectory.FullName, hasChildren: true));
        }
        FolderTree.ItemsSource = RootFolders;
    }

    // --- フォルダツリー ---

    private void FolderTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (args.Item is FolderNode folder) folder.LoadChildren();
    }

    private async void FolderTree_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        => await LoadSelectedFolderAsync();

    private async void LoadButton_Click(object sender, RoutedEventArgs e)
        => await LoadSelectedFolderAsync();

    private async Task LoadSelectedFolderAsync()
    {
        if (FolderTree.SelectedItem is FolderNode folder)
            await ViewModel.LoadFolderAsync(folder.Path);
    }

    // --- 左ペインの折りたたみ ---

    private void ToggleLeftPaneButton_Click(object sender, RoutedEventArgs e)
    {
        if (LeftColumn.ActualWidth > 0)
        {
            _lastLeftWidth = LeftColumn.ActualWidth;
            LeftColumn.Width = new GridLength(0);
        }
        else
        {
            LeftColumn.Width = new GridLength(_lastLeftWidth <= 0 ? 300 : _lastLeftWidth);
        }
    }

    // --- 右ペイン: 選択とキー操作 ---

    private void PhotoGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.SelectedPhoto = PhotoGrid.SelectedItem as PhotoItemViewModel;
    }

    private void PhotoGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var item = ViewModel.SelectedPhoto;
        if (item == null) return;

        // レーティング 0-5（修飾子なし）
        if (KeyboardModifiers.None)
        {
            switch (e.Key)
            {
                case VirtualKey.Number0: item.SetRating(0); e.Handled = true; return;
                case VirtualKey.Number1: item.SetRating(1); e.Handled = true; return;
                case VirtualKey.Number2: item.SetRating(2); e.Handled = true; return;
                case VirtualKey.Number3: item.SetRating(3); e.Handled = true; return;
                case VirtualKey.Number4: item.SetRating(4); e.Handled = true; return;
                case VirtualKey.Number5: item.SetRating(5); e.Handled = true; return;

                // カラーラベル 6-9 + P（紫）。★旧実装で未割当だった紫に P を割当て。
                case VirtualKey.Number6: item.ToggleColorLabel(ColorLabel.Red); e.Handled = true; return;
                case VirtualKey.Number7: item.ToggleColorLabel(ColorLabel.Yellow); e.Handled = true; return;
                case VirtualKey.Number8: item.ToggleColorLabel(ColorLabel.Green); e.Handled = true; return;
                case VirtualKey.Number9: item.ToggleColorLabel(ColorLabel.Blue); e.Handled = true; return;
                case VirtualKey.P: item.ToggleColorLabel(ColorLabel.Purple); e.Handled = true; return;

                case (VirtualKey)219: item.RatingDown(); e.Handled = true; return; // [
                case (VirtualKey)221: item.RatingUp(); e.Handled = true; return;   // ]
            }
        }

        // フラグ（Ctrl+上/下）
        if (KeyboardModifiers.Ctrl)
        {
            switch (e.Key)
            {
                case VirtualKey.Up: item.FlagUp(); e.Handled = true; return;
                case VirtualKey.Down: item.FlagDown(); e.Handled = true; return;
            }
        }
    }
}
