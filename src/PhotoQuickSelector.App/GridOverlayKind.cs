namespace PhotoQuickSelector_App;

/// <summary>
/// プレビューに重ねる構図グリッドの種類（SPEC §3-6 / G キーで巡回）。
/// 巡回順は <see cref="None"/> → <see cref="CenterCross"/> → <see cref="RuleOfThirds"/> →
/// <see cref="Square"/> → <see cref="None"/>（徐々に細かくなる順）。
/// </summary>
public enum GridOverlayKind
{
    /// <summary>非表示。</summary>
    None,

    /// <summary>中央十字（領域中心の縦横各1本）。</summary>
    CenterCross,

    /// <summary>三分割（rule of thirds。縦2本・横2本）。</summary>
    RuleOfThirds,

    /// <summary>正方形グリッド（短辺を N 等分した正方セルを敷き詰め。N は設定で調整可）。</summary>
    Square,
}

/// <summary>
/// グリッドを描く基準（Shift+G で切替）。
/// </summary>
public enum GridOverlayReference
{
    /// <summary>表示中の画像領域に対して描く（ズーム/パン追従）。</summary>
    Image,

    /// <summary>CanvasControl 全面に対して描く（画面固定・画像に追従しない）。</summary>
    Canvas,
}
