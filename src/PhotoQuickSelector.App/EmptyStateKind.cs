namespace PhotoQuickSelector_App;

/// <summary>
/// サムネイルグリッドに写真を出せないときの理由。
/// <see cref="Controls.EmptyStateView"/> がアイコン＋1 行の説明で示す。
/// </summary>
public enum EmptyStateKind
{
    /// <summary>表示できる写真がある（空状態を出さない）。読み込み中もこれ。</summary>
    None,

    /// <summary>まだフォルダを開いていない（起動直後）。</summary>
    NoFolder,

    /// <summary>フォルダは開いたが JPEG が 1 枚も無い。</summary>
    NoPhotos,

    /// <summary>JPEG はあるが絞り込みで全件が外れた。</summary>
    FilteredOut,
}
