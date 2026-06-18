using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoQuickSelector_App.ViewModels;
using Windows.System;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// 右ペイン上部のステータスバー（件数＋選択写真のメタ情報パネル案A＋GPS＋読み込み中表示）。
/// 共有 <see cref="MainViewModel"/> を <see cref="MainPage"/> が注入する。
/// 左ペイン開閉ボタンは骨組み側（<see cref="MainPage"/> の LeftColumn）を操作するため、
/// <see cref="ToggleLeftPaneRequested"/> イベントで委譲する。
/// </summary>
public sealed partial class PhotoStatusBar : UserControl
{
    private MainViewModel? _viewModel;

    public PhotoStatusBar() => InitializeComponent();

    /// <summary>表示対象のビューモデル。<see cref="MainPage"/> が生成後に注入する。</summary>
    public MainViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (ReferenceEquals(_viewModel, value)) return;
            _viewModel = value;
            FilterControl.ViewModel = value; // 内包するフィルタボタンへも注入
            Bindings.Update();
        }
    }

    /// <summary>左ペインの表示/非表示ボタンが押されたとき発火する（開閉は <see cref="MainPage"/> が担う）。</summary>
    public event EventHandler? ToggleLeftPaneRequested;

    private void ToggleButton_Click(object sender, RoutedEventArgs e)
        => ToggleLeftPaneRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>GPS 地図ボタン。撮影位置をブラウザの地図で開く（十進緯度経度がある場合）。</summary>
    private async void GpsButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PhotoItemViewModel { MapUri: { } uri })
            await Launcher.LaunchUriAsync(uri);
    }
}
