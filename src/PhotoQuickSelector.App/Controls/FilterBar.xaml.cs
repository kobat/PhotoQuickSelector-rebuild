using Microsoft.UI.Xaml.Controls;
using PhotoQuickSelector_App.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// 絞り込み（SPEC §3-4）とクリップボード出力（SPEC §3-5）のフライアウト。共有
/// <see cref="MainViewModel"/> を <see cref="MainPage"/> が注入する。条件入力はフライアウト内の
/// クラシック Binding（DataContext=ViewModel.Filter）で <see cref="FilterViewModel"/> を更新し、
/// レーティング値・比較トグル・コピー操作はコードビハインドで処理する。
/// </summary>
public sealed partial class FilterBar : UserControl
{
    private MainViewModel? _viewModel;

    public FilterBar() => InitializeComponent();

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

    // 開くたびにレーティングピッカーを現在値へ同期（RatingControl は値型不一致で直接束縛できない）。
    private void FilterFlyout_Opening(object sender, object e)
    {
        if (ViewModel is { } vm)
            RatingPicker.Value = vm.Filter.RatingValue > 0 ? vm.Filter.RatingValue : -1;
    }

    private void RatingPicker_ValueChanged(RatingControl sender, object args)
    {
        if (ViewModel is { } vm)
            vm.Filter.RatingValue = sender.Value > 0 ? (int)sender.Value : 0;
    }

    private void CompareButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => ViewModel?.Filter.ToggleCompareMode();

    private void CopyFileNames_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => SetClipboardText(ViewModel?.BuildFileNameListText());

    private void CopyMoveBatch_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => SetClipboardText(ViewModel?.BuildMoveBatchText());

    private static void SetClipboardText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }
}
