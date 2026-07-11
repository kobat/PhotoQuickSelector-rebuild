namespace PhotoQuickSelector_App;

/// <summary>
/// プレビュー左上の情報オーバーレイの種類（I キーで巡回）。
/// 巡回順は <see cref="Badge"/> → <see cref="Full"/> → <see cref="Off"/> → <see cref="Badge"/>。
/// 表示タイミング（常時 / 切替時のみ）は別軸（<see cref="AppSettings.OverlayTransient"/>・Shift+I）。
/// </summary>
public enum InfoOverlayKind
{
    /// <summary>評価バッジ（レーティング/フラグ/カラーラベルのみを簡潔に表示）。</summary>
    Badge,

    /// <summary>詳細情報（ファイル名・画像サイズ・EXIF チップ・カメラ/レンズ・評価・撮影日時）。</summary>
    Full,

    /// <summary>非表示。</summary>
    Off,
}
