namespace PhotoQuickSelector.Core;

/// <summary>
/// 「採用フラグなし＆未評価」の写真をフォルダ配下の <c>Reject</c> サブフォルダへ移動する
/// バッチ（<c>.bat</c>）の組み立て（UI 非依存の純ロジック）。実際のフォルダ作成・bat 保存・
/// 実行は呼び出し側（App）が行う。<see cref="ClipboardExport.BuildMoveBatch"/> と同じく
/// 「bat を移動先（Reject）フォルダ直下に置く」前提で <c>FROMDIR=..</c> / <c>TODIR=.</c> を使い、
/// <c>move %FROMDIR%\&lt;拡張子なし名&gt;* %TODIR%</c> で RAW+JPEG をまとめて移動する。
/// </summary>
public static class RejectMove
{
    /// <summary>移動先サブフォルダ名。</summary>
    public const string RejectFolderName = "Reject";

    /// <summary>
    /// 移動対象か（採用フラグが付いておらず、レーティングも付いていない）。
    /// 拒否フラグ付き（FlagRating &lt; 0）の未評価も対象に含む。
    /// </summary>
    public static bool IsRejectTarget(PhotoEvaluation eval)
        => eval.FlagRating <= 0 && eval.Rating == 0;

    /// <summary>
    /// Reject 移動バッチの本文を生成する。先頭に <c>@echo off</c> ＋ <c>chcp 65001</c>（UTF-8）と
    /// 生成日時・フォルダ・件数の <c>@rem</c> コメントを置き、本体は各ファイルの
    /// <c>move %FROMDIR%\&lt;拡張子なし名&gt;* %TODIR%</c>。ログ出力は呼び出し側で
    /// <c>cmd /c "bat" &gt; "log" 2&gt;&amp;1</c> のリダイレクトにより行う想定（bat 本文には含めない）。
    /// </summary>
    /// <param name="folderDescription">対象フォルダの表示名（パス）。</param>
    /// <param name="generatedAt">生成日時の表示文字列（<c>@rem</c> コメント用）。</param>
    /// <param name="targetCount">移動対象件数。</param>
    /// <param name="totalCount">フォルダ内全件数。</param>
    /// <param name="fileNames">移動対象のファイル名（拡張子付き）。</param>
    public static string BuildBatch(
        string folderDescription,
        string generatedAt,
        int targetCount,
        int totalCount,
        IEnumerable<string> fileNames)
    {
        var lines = new List<string>
        {
            "@echo off",
            "chcp 65001 > nul",
            $"@rem Reject move generated {generatedAt}",
            $"@rem Folder: {folderDescription}",
            $"@rem Count: {targetCount}/{totalCount} (no pick flag, no rating)",
            "set FROMDIR=..",
            "set TODIR=.",
        };
        foreach (var name in fileNames)
            lines.Add($"move %FROMDIR%\\{Path.GetFileNameWithoutExtension(name)}* %TODIR%");
        lines.Add("");

        return string.Join("\r\n", lines);
    }
}
