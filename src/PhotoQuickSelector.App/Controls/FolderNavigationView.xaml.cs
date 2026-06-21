using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PhotoQuickSelector_App.ViewModels;
using Windows.System;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// 左ペイン: お気に入り / 最近開いたフォルダ / フォルダツリー。
/// 読み込み・お気に入り操作は共有 <see cref="MainViewModel"/> へ直接委譲する（<see cref="MainPage"/> が注入）。
/// 左ペイン幅・折りたたみの永続化は骨組み側（<see cref="MainPage"/>）の責務で、本コントロールは関与しない。
/// </summary>
public sealed partial class FolderNavigationView : UserControl
{
    /// <summary>フォルダツリーのルート（ドライブ）。TreeView の ItemsSource。</summary>
    public ObservableCollection<FolderNode> RootFolders { get; } = new();

    private MainViewModel? _viewModel;

    public FolderNavigationView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>表示対象のビューモデル。<see cref="MainPage"/> が生成後に注入する。</summary>
    public MainViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (ReferenceEquals(_viewModel, value)) return;
            _viewModel = value;
            Bindings.Update();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (RootFolders.Count == 0)
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                RootFolders.Add(new FolderNode(drive.Name, drive.RootDirectory.FullName, hasChildren: true));
            }
        }
        FolderTree.ItemsSource = RootFolders;
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
        if (_viewModel != null && FolderTree.SelectedItem is FolderNode folder)
            await _viewModel.LoadFolderAsync(folder.Path);
    }

    // --- 最近 / お気に入りショートカット ---

    private async void Shortcut_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (_viewModel == null || e.ClickedItem is not FolderShortcut shortcut) return;
        if (!Directory.Exists(shortcut.Path))
        {
            _viewModel.RemoveRecentFolder(shortcut.Path); // 消えたフォルダは一覧から除去
            return;
        }
        await _viewModel.LoadFolderAsync(shortcut.Path);
        await ExpandAndSelectFolderAsync(shortcut.Path); // ツリーも同パスへ展開＆選択
    }

    /// <summary>
    /// フォルダツリーをルート（ドライブ）から <paramref name="targetPath"/> まで 1 階層ずつ
    /// 展開し、末端ノードを選択状態にする。
    /// WinUI の TreeView には「データ項目を指定して展開/選択する」API が無いため手動ウォークする。
    /// 子は <see cref="FolderNode.LoadChildren"/>（差分同期）で実体化し、コンテナは遅延 realize の
    /// ため <see cref="RealizeContainerAsync"/> でレイアウト確定を待ってから取得する。
    /// 起動時のセッション復元（<see cref="MainPage"/>）からも呼ぶため public。
    /// </summary>
    public async Task ExpandAndSelectFolderAsync(string targetPath)
    {
        var node = RootFolders.FirstOrDefault(r =>
            targetPath.StartsWith(r.Path, StringComparison.OrdinalIgnoreCase));
        if (node == null) return;

        while (true)
        {
            var container = await RealizeContainerAsync(node);
            if (container == null) return;

            if (PathEquals(node.Path, targetPath))
            {
                container.IsSelected = true;       // TreeView 選択ビジュアル
                FolderTree.SelectedItem = node;    // SelectedItem も同期（読み込み/更新ボタンが参照）
                container.StartBringIntoView();    // スクロールして可視化
                return;
            }

            node.LoadChildren();                   // 子を実体化（差分同期・既存ノード温存）
            container.IsExpanded = true;           // 展開して子コンテナを realize させる
            FolderTree.UpdateLayout();

            var next = node.Children.FirstOrDefault(c =>
                PathEquals(c.Path, targetPath) ||
                targetPath.StartsWith(c.Path + System.IO.Path.DirectorySeparatorChar,
                                      StringComparison.OrdinalIgnoreCase));
            if (next == null) return;              // 階層が見つからない（権限/隠しフォルダ等）
            node = next;
        }
    }

    /// <summary>
    /// 指定ノードの <see cref="TreeViewItem"/> コンテナが realize されるまで
    /// レイアウトを回して待つ（仮想化により展開直後は <c>null</c> のことがある）。
    /// </summary>
    private async Task<TreeViewItem?> RealizeContainerAsync(FolderNode node)
    {
        for (int i = 0; i < 20; i++)
        {
            if (FolderTree.ContainerFromItem(node) is TreeViewItem container)
                return container;
            FolderTree.UpdateLayout();
            await Task.Delay(16); // 1 フレーム程度待ってリトライ
        }
        return null;
    }

    /// <summary>末尾区切り（ドライブルート <c>D:\</c> 等）を無視した大小無視のパス比較。</summary>
    private static bool PathEquals(string a, string b) =>
        string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    private void RemoveFavoriteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FolderShortcut shortcut)
            _viewModel?.RemoveFavorite(shortcut.Path);
    }

    private void RemoveFavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FolderShortcut shortcut)
            _viewModel?.RemoveFavorite(shortcut.Path);
    }

    private void AddFavoriteFromRecentMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FolderShortcut shortcut)
            _viewModel?.ToggleFavorite(shortcut.Path);
    }

    private void RemoveRecentMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FolderShortcut shortcut)
            _viewModel?.RemoveRecentFolder(shortcut.Path);
    }

    // --- ツリーノードのお気に入り登録/解除 ---

    private void FolderTreeFlyout_Opening(object sender, object e)
    {
        if (sender is not MenuFlyout flyout) return;
        var node = (flyout.Target as FrameworkElement)?.DataContext as FolderNode;
        bool isFavorite = node != null && _viewModel?.IsFavorite(node.Path) == true;

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
            _viewModel?.ToggleFavorite(node.Path);
    }

    private void RemoveFavoriteFromTreeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FolderNode node)
            _viewModel?.RemoveFavorite(node.Path);
    }
}
