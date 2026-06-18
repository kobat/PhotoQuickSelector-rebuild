namespace PhotoQuickSelector.Core;

/// <summary>
/// クリップボード出力テキストの生成（SPEC §3-5）。UI 非依存の純関数として、
/// 絞込結果のファイル名一覧、または採用写真を移動する <c>.bat</c> スクリプトを組み立てる。
/// 実際のクリップボード設定（<c>DataPackage</c>）は呼び出し側（App）が行う。
/// </summary>
public static class ClipboardExport
{
    /// <summary>絞込結果のファイル名一覧（1 行 1 ファイル、末尾に空行）。</summary>
    public static string BuildFileNameList(IEnumerable<string> fileNames)
        => Join(fileNames.Append(""));

    /// <summary>
    /// 採用（絞込結果）写真を移動する <c>.bat</c> スクリプトを生成する。
    /// 先頭にフォルダ・件数・フィルタ条件を <c>@rem</c> コメントで埋め込み、本体は
    /// <c>set FROMDIR=..</c> / <c>set TODIR=.</c> ＋ 各ファイルの
    /// <c>move %FROMDIR%\&lt;拡張子なし名&gt;* %TODIR%</c>（RAW+JPEG をまとめて移動する意図）。
    /// </summary>
    /// <param name="folderDescription">対象フォルダの表示名（パス）。</param>
    /// <param name="filteredCount">絞込件数。</param>
    /// <param name="totalCount">全件数。</param>
    /// <param name="filterConditionLines"><see cref="PhotoFilter.DescribeConditions"/> の各行。</param>
    /// <param name="fileNames">移動対象のファイル名（拡張子付き）。</param>
    public static string BuildMoveBatch(
        string folderDescription,
        int filteredCount,
        int totalCount,
        IReadOnlyList<string> filterConditionLines,
        IEnumerable<string> fileNames)
    {
        var lines = new List<string>
        {
            $"@rem Folder: {folderDescription}",
            $"@rem Count: {filteredCount}/{totalCount}",
        };
        foreach (var c in filterConditionLines)
            lines.Add($"@rem {c}");

        lines.Add("set FROMDIR=..");
        lines.Add("set TODIR=.");
        foreach (var name in fileNames)
            lines.Add($"move %FROMDIR%\\{Path.GetFileNameWithoutExtension(name)}* %TODIR%");
        lines.Add("");

        return Join(lines);
    }

    private static string Join(IEnumerable<string> lines) => string.Join("\r\n", lines);
}
