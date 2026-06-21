using System.Text;
using System.Text.RegularExpressions;

namespace PhotoQuickSelector.Core;

/// <summary>
/// 写真をリネームしながら任意の宛先フォルダへコピーするバッチ（<c>.bat</c>）の組み立て
/// （UI 非依存の純ロジック）。テンプレート（プレースホルダ）から新しいベース名を決め、
/// RAW+JPEG ペアは <c>for</c> ループ＋ワイルドカード（<c>&lt;元名&gt;.*</c>）でまとめてコピーし、
/// 各ファイルの拡張子は元のまま保持する（新ベース名 ＋ 元拡張子）。
/// 実際のフォルダ作成・bat 保存・実行は呼び出し側（App）が行う。
/// <para>
/// プレースホルダ（年月日＝大文字 / 時分秒＝小文字。月 <c>MM</c> と分 <c>mm</c> は大小で区別）:
/// <c>{folder}</c> 元フォルダ名、<c>{name}</c> 元ファイル名（拡張子なし）、<c>{ext}</c> 元拡張子（ドットなし）、
/// <c>{YYYY}</c>/<c>{YY}</c> 撮影年、<c>{MM}</c> 月、<c>{DD}</c> 日、
/// <c>{hh}</c> 時(24h)、<c>{mm}</c> 分、<c>{ss}</c> 秒、
/// <c>{seq}</c>/<c>{seq:000}</c> 連番（1 始まり・桁数はゼロの個数）。
/// </para>
/// </summary>
public static class CopyRename
{
    /// <summary>同名ファイルが宛先に既にある場合の挙動。</summary>
    public enum OnExist
    {
        /// <summary>上書きコピー（<c>copy /y</c>）。</summary>
        Overwrite,
        /// <summary>既存があればコピーしない（<c>if not exist … copy</c>）。</summary>
        Skip,
    }

    /// <summary>1 枚分のリネーム入力。撮影日時が無い場合は <see cref="Taken"/> を null にする。</summary>
    public readonly record struct RenameContext(string OriginalFileName, DateTimeOffset? Taken);

    // {seq} / {seq:000} を取り出す（コロン以降のゼロの個数＝桁数）。
    private static readonly Regex SeqRegex = new(@"\{seq(?::(0+))?\}", RegexOptions.Compiled);

    /// <summary>
    /// テンプレートと 1 枚分のコンテキストから新しいベース名（拡張子なし）を解決する。
    /// 連番は <paramref name="sequence"/>（1 始まり想定）を使う。Windows で使えない文字は
    /// <c>_</c> に置換する。
    /// </summary>
    public static string ResolveName(
        string template, string folderName, RenameContext ctx, int sequence)
    {
        var name = template ?? "";

        name = name
            .Replace("{folder}", folderName ?? "")
            .Replace("{name}", Path.GetFileNameWithoutExtension(ctx.OriginalFileName))
            .Replace("{ext}", Path.GetExtension(ctx.OriginalFileName).TrimStart('.'));

        // 撮影日時系（無い場合は空文字）。
        var t = ctx.Taken;
        name = name
            .Replace("{YYYY}", t?.ToString("yyyy") ?? "")
            .Replace("{YY}", t?.ToString("yy") ?? "")
            .Replace("{MM}", t?.ToString("MM") ?? "")
            .Replace("{DD}", t?.ToString("dd") ?? "")
            .Replace("{hh}", t?.ToString("HH") ?? "")
            .Replace("{mm}", t?.ToString("mm") ?? "")
            .Replace("{ss}", t?.ToString("ss") ?? "");

        // 連番（{seq} / {seq:000}）。
        name = SeqRegex.Replace(name, m =>
        {
            var width = m.Groups[1].Success ? m.Groups[1].Value.Length : 0;
            return sequence.ToString(new string('0', Math.Max(width, 1)));
        });

        return SanitizeFileName(name);
    }

    /// <summary>
    /// 全件の新ベース名を解決する。<paramref name="duplicates"/> には、同一の新ベース名へ
    /// 解決されてしまう（衝突する）ベース名を大小無視で重複なく返す（リネームで起こり得る事故）。
    /// </summary>
    public static IReadOnlyList<(string SourceName, string NewBase)> ResolveAll(
        string template, string folderName, IReadOnlyList<RenameContext> items,
        out IReadOnlyList<string> duplicates)
    {
        var result = new List<(string, string)>(items.Count);
        for (int i = 0; i < items.Count; i++)
            result.Add((items[i].OriginalFileName, ResolveName(template, folderName, items[i], i + 1)));

        duplicates = result
            .GroupBy(r => r.Item2, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        return result;
    }

    /// <summary>
    /// リネームコピーのバッチ本文を生成する。先頭に <c>chcp 65001</c>（UTF-8）と
    /// 生成日時・コピー元/先・テンプレート・件数の <c>@rem</c> コメントを置く。<c>FROMDIR</c> は
    /// コピー元の絶対パス、<c>TODIR=.</c>（＝bat を宛先直下で実行する前提）。各ファイルは
    /// <c>for %%F in ("%FROMDIR%\&lt;元名&gt;.*") do copy "%%F" "%TODIR%\&lt;新名&gt;%%~xF"</c>
    /// で RAW+JPEG をまとめてコピーする（拡張子は元のまま保持）。
    /// </summary>
    /// <param name="sourceDir">コピー元フォルダの絶対パス。</param>
    /// <param name="destDir">コピー先フォルダの絶対パス（<c>@rem</c> 表示用）。</param>
    /// <param name="template">リネームテンプレート。</param>
    /// <param name="policy">同名存在時の挙動。</param>
    /// <param name="generatedAt">生成日時の表示文字列。</param>
    /// <param name="items">コピー対象（順序が連番に対応）。</param>
    public static string BuildBatch(
        string sourceDir,
        string destDir,
        string template,
        OnExist policy,
        string generatedAt,
        IReadOnlyList<RenameContext> items)
    {
        var folderName = GetFolderName(sourceDir);
        var resolved = ResolveAll(template, folderName, items, out _);

        // @echo off は付けない（各 copy コマンドがログにエコーされるようにするため）。
        var lines = new List<string>
        {
            "chcp 65001 > nul",
            $"@rem CopyRename generated {generatedAt}",
            $"@rem From: {sourceDir}",
            $"@rem To: {destDir}",
            $"@rem Template: {template}   OnExist: {policy}   Count: {items.Count}",
            $"set FROMDIR={sourceDir}",
            "set TODIR=.",
        };

        foreach (var (source, newBase) in resolved)
        {
            var stem = Path.GetFileNameWithoutExtension(source);
            var src = $"\"%FROMDIR%\\{stem}.*\"";
            var dst = $"\"%TODIR%\\{newBase}%%~xF\"";
            lines.Add(policy == OnExist.Overwrite
                ? $"for %%F in ({src}) do copy /y \"%%F\" {dst}"
                : $"for %%F in ({src}) do if not exist {dst} copy \"%%F\" {dst}");
        }
        lines.Add("");

        return string.Join("\r\n", lines);
    }

    /// <summary>コピー元フォルダの末尾フォルダ名（ドライブルートなら空にならないようパス末端を返す）。</summary>
    private static string GetFolderName(string dir)
    {
        var name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrEmpty(name) ? dir.TrimEnd(Path.DirectorySeparatorChar, ':') : name;
    }

    /// <summary>Windows のファイル名に使えない文字を <c>_</c> へ置換する。</summary>
    private static string SanitizeFileName(string name)
    {
        var sb = new StringBuilder(name.Length);
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in name)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString();
    }
}
