namespace PhotoQuickSelector_App;

/// <summary>
/// プレビューの鮮鋭度スコア表示モード（SPEC「鮮鋭度スコア」 / S キーで巡回）。
/// 巡回順は <see cref="None"/> → <see cref="Compact"/> → <see cref="Tenengrad"/> → <see cref="All"/> → <see cref="None"/>。
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

    /// <summary>計算内容は <see cref="Tenengrad"/> と同じ（Tenengrad＋被写体領域）。ルーペ／詳細情報オーバーレイの
    /// 表示だけを簡略化する（見出し・注記なしの4行のみ）。画像情報パネルは簡略化しない＝<see cref="Tenengrad"/>
    /// と同じ6行＋注記を表示する。
    /// <para>
    /// 末尾に追加した理由: <c>AppSettings.SharpnessMode</c> は設定ファイルへ整数値で永続化される
    /// （ソース生成 JSON コンテキスト経由）ため、既存メンバーの数値を変えると旧 settings.json の
    /// 値が別モードとして読まれてしまう。既存 3 メンバーの並びは変えず、新メンバーは必ず末尾へ足すこと。
    /// </para>
    /// </summary>
    Compact,
}
