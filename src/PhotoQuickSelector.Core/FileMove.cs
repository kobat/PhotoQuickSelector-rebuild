namespace PhotoQuickSelector.Core;

/// <summary>
/// 絞込結果の写真を任意の移動先フォルダへ移す <c>.bat</c> の組み立て（UI 非依存の純ロジック）。
/// <see cref="RejectMove"/>（Reject 固定サブフォルダ）と旧 ClipboardExport の移動 bat 生成（廃止済み）
/// の合成に相当するが、移動先が任意フォルダのため <c>FROMDIR</c> は表示中フォルダの
/// <b>絶対パス</b>にする（Reject の <c>FROMDIR=..</c> のような相対参照は使えない）。
/// 実際のフォルダ作成・bat 保存・実行は呼び出し側（App）が行う。
/// </summary>
public static class FileMove
{
    /// <summary>
    /// ファイル移動バッチの本文を生成する。先頭に <c>chcp 65001</c>（UTF-8）と
    /// 生成日時・移動元・移動先・件数・フィルタ条件の <c>@rem</c> コメントを置き、本体は各ファイルの
    /// <c>move "%FROMDIR%\&lt;拡張子なし名&gt;*" "%TODIR%"</c>（RAW+JPEG をまとめて移動する意図は
    /// <see cref="RejectMove"/> と同じ）。bat は移動先フォルダ直下に置く前提のため <c>TODIR=.</c>。
    /// 移動元パス・ファイル名に空白が入り得るため move の両引数は二重引用符で囲む
    /// （<c>set</c> 行自体は cmd の仕様上引用符不要）。
    /// </summary>
    /// <param name="sourceDir">移動元（表示中フォルダ）の絶対パス。</param>
    /// <param name="destDescription">移動先の表示名（<c>@rem</c> コメント用）。</param>
    /// <param name="generatedAt">生成日時の表示文字列（<c>@rem</c> コメント用）。</param>
    /// <param name="targetCount">移動対象件数。</param>
    /// <param name="totalCount">フォルダ内全件数。</param>
    /// <param name="filterConditionLines"><see cref="PhotoFilter.DescribeConditions"/> の各行。</param>
    /// <param name="fileNames">移動対象のファイル名（拡張子付き）。</param>
    public static string BuildBatch(
        string sourceDir,
        string destDescription,
        string generatedAt,
        int targetCount,
        int totalCount,
        IReadOnlyList<string> filterConditionLines,
        IEnumerable<string> fileNames)
    {
        var lines = new List<string>
        {
            "chcp 65001 > nul",
            $"@rem File move generated {generatedAt}",
            $"@rem From: {sourceDir}",
            $"@rem To: {destDescription}",
            $"@rem Count: {targetCount}/{totalCount}",
        };
        foreach (var c in filterConditionLines)
            lines.Add($"@rem {c}");

        lines.Add($"set FROMDIR={sourceDir}");
        lines.Add("set TODIR=.");
        foreach (var name in fileNames)
            lines.Add($"move \"%FROMDIR%\\{Path.GetFileNameWithoutExtension(name)}*\" \"%TODIR%\"");
        lines.Add("");

        return string.Join("\r\n", lines);
    }
}
