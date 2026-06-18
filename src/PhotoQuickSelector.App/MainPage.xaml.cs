using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PhotoQuickSelector_App.ViewModels;
using Windows.System;

namespace PhotoQuickSelector_App;

/// <summary>
/// メイン画面。左=フォルダツリー / 右=サムネイル一覧の左右分割レイアウト。
/// </summary>
public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel { get; } = new();

    private double _lastLeftWidth = 300;

    public MainPage()
    {
        InitializeComponent();
        Loaded += MainPage_Loaded;

        // 左ペイン（フォルダナビ）とプレビュー（大画面）に共有ビューモデルを注入する。
        LeftNav.ViewModel = ViewModel;
        Preview.ViewModel = ViewModel;
        // 選択変更でサムネイル側の選択も同期する。
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.SelectedPhoto)) return;
        // プレビューでの前後移動・フィルムストリップ選択をサムネイルグリッドへ反映。
        if (!ReferenceEquals(PhotoGrid.SelectedItem, ViewModel.SelectedPhoto))
            PhotoGrid.SelectedItem = ViewModel.SelectedPhoto;
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        RestoreLeftPaneLayout();
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

    /// <summary>GPS 地図ボタン。撮影位置をブラウザの地図で開く（十進緯度経度がある場合）。</summary>
    private async void GpsButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PhotoItemViewModel { MapUri: { } uri })
            await Launcher.LaunchUriAsync(uri);
    }

    // --- 右ペイン: 選択とキー操作 ---

    private void PhotoGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.SelectedPhoto = PhotoGrid.SelectedItem as PhotoItemViewModel;
    }

    private void PhotoGrid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // サムネイルのダブルクリックで大画面プレビューへ（SPEC §2）。
        ViewModel.EnterPreview();
    }

    /// <summary>
    /// Window 直下のルート要素（<see cref="MainWindow"/> の RootGrid）の <c>PreviewKeyDown</c> から
    /// 委譲されるキー処理。tunneling で子より先に呼ばれるフォーカス非依存の集約点なので、画像クリックで
    /// フォーカスが祖先 ScrollViewer へ移っても、フィルムストリップ（ListView）にフォーカスがあっても、
    /// 評価/ナビキーを ListView のナビゲーションより先に処理できる。処理したら <paramref name="e"/> を
    /// Handled にして後続の bubbling KeyDown（子コントロールのナビ等）を抑止する。
    /// プレビュー時は <see cref="Controls.PreviewControl.HandleKeyDown"/> へ委譲し、サムネイル一覧時は
    /// 評価キーのみ処理する（素の矢印は未処理のまま通して GridView の通常ナビへ流す）。
    /// </summary>
    public void HandleGlobalKeyDown(KeyRoutedEventArgs e)
    {
        if (ViewModel.IsPreviewMode)
        {
            if (Preview.HandleKeyDown(e.Key))
                e.Handled = true;
            return;
        }

        if (ViewModel.SelectedPhoto is { } item && PhotoKeyCommands.TryHandleEvaluation(e.Key, item))
            e.Handled = true;
    }
}
