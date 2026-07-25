using Microsoft.UI.Xaml.Controls;
using PhotoQuickSelector_App.ViewModels;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// サムネイルグリッドに写真を出せないときの空状態（アイコン＋1 行の説明）。
/// 表示するかどうか・アイコン・文言は共有 <see cref="MainViewModel"/> の EmptyState* が決める。
/// グリッドへ重ねて置く前提で、ヒットテストは透過する（XAML 側 IsHitTestVisible=False）。
/// </summary>
public sealed partial class EmptyStateView : UserControl
{
    private MainViewModel? _viewModel;

    public EmptyStateView() => InitializeComponent();

    /// <summary>表示対象のビューモデル。親コントロールが生成後に注入する。</summary>
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
}
