using PhotoQuickSelector.Core;

namespace PhotoQuickSelector_App.ViewModels;

/// <summary>
/// 「全手法」表示モード（<see cref="SharpnessMode.All"/>）で焦点写真に計算する、Tenengrad 以外の
/// 4 手法（<see cref="SharpnessMetrics"/>）の結果一式。4 手法すべて完了して初めて
/// <see cref="PhotoItemViewModel.SharpnessExtras"/> へセットする（部分結果は保持しない）。
/// </summary>
public sealed record SharpnessExtraScores(
    MetricScores LaplacianVariance,
    MetricScores Brenner,
    MetricScores Reblur,
    MetricScores EdgeWidth);
