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
        RestoreLeftPaneLayout();

        if (RootFolders.Count > 0) return;
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            RootFolders.Add(new FolderNode(drive.Name, drive.RootDirectory.FullName, hasChildren: true));
        }
        FolderTree.ItemsSource = RootFolders;
    }

    // --- 左ペインの幅／折りたたみ状態の復元・保存（AppSettings） ---

    private void RestoreLeftPaneLayout()
    {
        var s = ViewModel.Settings;
        _lastLeftWidth = s.LeftPaneWidth > 0 ? s.LeftPaneWidth : 300;
        LeftColumn.Width = s.LeftPaneCollapsed
            ? new GridLength(0)
            : new GridLength(_lastLeftWidth);
    }

    /// <summary>左ペインの現在の幅／折りたたみ状態を設定へ書き戻して保存する（ウィンドウ終了時）。</summary>
    public void SaveLeftPaneLayout()
    {
        var s = ViewModel.Settings;
        if (LeftColumn.ActualWidth > 0)
        {
            s.LeftPaneCollapsed = false;
            s.LeftPaneWidth = LeftColumn.ActualWidth;
        }
        else
        {
            s.LeftPaneCollapsed = true;
            s.LeftPaneWidth = _lastLeftWidth > 0 ? _lastLeftWidth : 300;
        }
        s.Save();
    }

    // --- フォルダツリー ---

    private void FolderTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (args.Item is FolderNode folder) folder.LoadChildren();
    }

    // --- 手動更新（更新ボタン / F5 / 右クリック「更新」） ---

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshSelectedOrDrives();

    private void FolderTree_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.F5)
        {
            RefreshSelectedOrDrives();
            e.Handled = true;
        }
    }

    private void RefreshMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FolderNode folder)
            RefreshFolder(folder);
    }

    private void RefreshSelectedOrDrives()
    {
        if (FolderTree.SelectedItem is FolderNode folder)
            RefreshFolder(folder);
        else
            RefreshDrives();
    }

    private void RefreshFolder(FolderNode folder)
    {
        folder.Refresh();
        if (FolderTree.ContainerFromItem(folder) is TreeViewItem container)
            container.IsExpanded = true;
    }

    private void RefreshDrives()
    {
        // ドライブ一覧も差分同期（Clear→全件追加はしない）
        var ready = DriveInfo.GetDrives().Where(d => d.IsReady).ToList();
        var readyPaths = new HashSet<string>(
            ready.Select(d => d.RootDirectory.FullName), StringComparer.OrdinalIgnoreCase);

        for (int i = RootFolders.Count - 1; i >= 0; i--)
            if (!readyPaths.Contains(RootFolders[i].Path))
                RootFolders.RemoveAt(i);

        var existing = new HashSet<string>(
            RootFolders.Select(r => r.Path), StringComparer.OrdinalIgnoreCase);
        foreach (var drive in ready)
        {
            var path = drive.RootDirectory.FullName;
            if (!existing.Contains(path))
                RootFolders.Add(new FolderNode(drive.Name, path, hasChildren: true));
        }
    }

    private void FolderTree_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // ダブルクリックは「下位フォルダの展開/折りたたみ」。読み込みは「読み込み」ボタンのみ。
        if (FolderTree.SelectedItem is not FolderNode folder) return;
        if (FolderTree.ContainerFromItem(folder) is not TreeViewItem container) return;

        if (!container.IsExpanded) folder.LoadChildren(); // 展開前に子を読み込む
        container.IsExpanded = !container.IsExpanded;
    }

    private async void LoadButton_Click(object sender, RoutedEventArgs e)
        => await LoadSelectedFolderAsync();

    private async Task LoadSelectedFolderAsync()
    {
        if (FolderTree.SelectedItem is FolderNode folder)
            await ViewModel.LoadFolderAsync(folder.Path);
    }

    // --- 最近 / お気に入りショートカット ---

    private async void Shortcut_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is FolderShortcut shortcut)
        {
            if (Directory.Exists(shortcut.Path))
                await ViewModel.LoadFolderAsync(shortcut.Path);
            else
                ViewModel.RemoveRecentFolder(shortcut.Path); // 消えたフォルダは一覧から除去
        }
    }

    private void RemoveFavoriteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FolderShortcut shortcut)
            ViewModel.RemoveFavorite(shortcut.Path);
    }

    private void RemoveFavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FolderShortcut shortcut)
            ViewModel.RemoveFavorite(shortcut.Path);
    }

    private void AddFavoriteFromRecentMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FolderShortcut shortcut)
            ViewModel.ToggleFavorite(shortcut.Path);
    }

    private void RemoveRecentMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FolderShortcut shortcut)
            ViewModel.RemoveRecentFolder(shortcut.Path);
    }

    // --- ツリーノードのお気に入り登録/解除 ---

    private void FolderTreeFlyout_Opening(object sender, object e)
    {
        if (sender is not MenuFlyout flyout) return;
        var node = (flyout.Target as FrameworkElement)?.DataContext as FolderNode;
        bool isFavorite = node != null && ViewModel.IsFavorite(node.Path);

        // Items[0]=お気に入りに追加 / Items[1]=お気に入りから削除
        if (flyout.Items.Count >= 2)
        {
            flyout.Items[0].Visibility = isFavorite ? Visibility.Collapsed : Visibility.Visible;
            flyout.Items[1].Visibility = isFavorite ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void AddFavoriteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FolderNode node)
            ViewModel.ToggleFavorite(node.Path);
    }

    private void RemoveFavoriteFromTreeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FolderNode node)
            ViewModel.RemoveFavorite(node.Path);
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
