using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoQuickSelector_App.ViewModels;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// 絞り込み（SPEC §3-4）とファイル操作（SPEC §3-5。ファイル名/実体のコピー・移動）のフライアウト。
/// 共有 <see cref="MainViewModel"/> を <see cref="MainPage"/> が注入する。条件入力はフライアウト内の
/// クラシック Binding（DataContext=ViewModel.Filter）で <see cref="FilterViewModel"/> を更新し、
/// レーティング値・比較トグル・コピー/移動操作はコードビハインドで処理する。
/// コピー系メニューの中身は右クリックメニューと共有（<see cref="PhotoContextMenu.AddCopyNameItems"/> /
/// <see cref="PhotoContextMenu.AddCopyFileItems"/>）。
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

    // === コピー系 DropDownButton（対象＝絞込結果。中身は PhotoContextMenu と共有） ===
    // フライアウトを開いたまま絞込が変わり得るため、Opening のたびに Items をクリアして再構築する。

    /// <summary>「ファイル名をコピー」メニューを現在の絞込結果で組み直す。</summary>
    private void CopyNamesFlyout_Opening(object sender, object e)
    {
        if (ViewModel is not { } vm) return;
        CopyNamesFlyout.Items.Clear();
        // withAcceleratorText:false — Ctrl+Alt+E の実処理対象は選択集合であり、フィルタバーの対象
        // （絞込結果）と異なるため、誤解を招くショートカット表示はしない。
        PhotoContextMenu.AddCopyNameItems(CopyNamesFlyout.Items, vm.GetCopyTargets(), withAcceleratorText: false);
    }

    /// <summary>「ファイルをコピー」メニューを現在の絞込結果で組み直す。</summary>
    private void CopyFilesFlyout_Opening(object sender, object e)
    {
        if (ViewModel is not { } vm) return;
        CopyFilesFlyout.Items.Clear();
        PhotoContextMenu.AddCopyFileItems(CopyFilesFlyout.Items, vm, vm.GetCopyTargets(), XamlRoot);
    }

    // === 移動系（フロー本体は BatchFlows に集約。ここは対象抽出のみ） ===

    /// <summary>絞込結果を任意の宛先フォルダへ移動する（対象＝絞込結果）。</summary>
    private async void MoveFiles_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        await BatchFlows.RunMoveAsync(vm, vm.GetMoveTargets(), XamlRoot);
    }

    /// <summary>採用フラグなし・未評価の画像を Reject サブフォルダへ移動する（対象＝全件から抽出）。</summary>
    private async void RejectUnrated_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        await BatchFlows.RunRejectAsync(vm, vm.GetRejectTargets(), XamlRoot);
    }
}
