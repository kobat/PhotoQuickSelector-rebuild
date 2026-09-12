namespace PhotoQuickSelector_App;

/// <summary>
/// プレビューの鮮鋭度スコア表示モード（SPEC「鮮鋭度スコア」 / S キーで巡回）。
/// 巡回順は <see cref="None"/> → <see cref="Tenengrad"/> → <see cref="All"/> → <see cref="None"/>。
/// </summary>
public enum SharpnessMode
{
    /// <summary>非表示。計算も行わない。</summary>
    None,

    /// <summary>Tenengrad（<see cref="PhotoQuickSelector.Core.SharpnessAnalyzer"/>）のみを焦点写真で計算・表示。</summary>
    Tenengrad,

    /// <summary>Tenengrad に加え、比較用の 4 手法（<see cref="PhotoQuickSelector.Core.SharpnessMetrics"/>）も
    /// 焦点写真で計算・表示する。</summary>
    All,
}
