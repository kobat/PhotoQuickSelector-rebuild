namespace PhotoQuickSelector.Core;

/// <summary>
/// bat に埋め込むと cmd が誤動作する文字の事前検査（UI 非依存の純ロジック）。
/// 対象文字は「%」「&amp;」「^」の 3 つ:
/// % は二重引用符の内側でも変数展開されるため常に危険。&amp; と ^ は無引用符の箇所
/// （set 行・@rem コメント行）で行の分断/エスケープを起こす。
/// | &lt; &gt; " はそもそも Windows のファイル名・フォルダ名に使えないため検査不要。
/// ! は遅延展開を使っていないため安全（photo (1).jpg 等の実在しやすい名前を誤って
/// 弾かないよう ( ) ! はブロックしない）。
/// </summary>
public static class BatchSafety
{
    /// <summary>bat 埋め込みで誤動作を起こす文字。</summary>
    public const string UnsafeChars = "%&^";

    private static readonly char[] UnsafeCharArray = UnsafeChars.ToCharArray();

    /// <summary>値に危険文字が含まれるか。null/空は安全扱い。</summary>
    public static bool ContainsUnsafe(string? value)
        => !string.IsNullOrEmpty(value) && value.IndexOfAny(UnsafeCharArray) >= 0;

    /// <summary>危険文字を含む値だけを入力順で返す（重複はそのまま）。</summary>
    public static IReadOnlyList<string> FindUnsafe(IEnumerable<string?> values)
        => values.Where(ContainsUnsafe).Cast<string>().ToList();
}
