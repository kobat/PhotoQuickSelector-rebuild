using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace PhotoQuickSelector_App;

/// <summary>ショートカット一覧（チートシート）の 1 行＝キー表記とその説明。</summary>
public sealed record ShortcutItem(string Keys, string Description);

/// <summary>ショートカット一覧のグループ（カテゴリ）。<see cref="Items"/> をその見出しでまとめる。</summary>
public sealed record ShortcutGroup(string Title, IReadOnlyList<ShortcutItem> Items);

/// <summary>
/// ショートカット一覧（<see cref="Controls.ShortcutsDialog"/>）の表示専用データ。唯一の情報源（SSOT）は
/// リポジトリ直下の <c>shortcuts.json</c>（csproj の <c>EmbeddedResource</c>＝<c>LogicalName="shortcuts.json"</c>
/// でアセンブリへ埋め込み）で、実行時に <see cref="Assembly.GetManifestResourceStream(string)"/> で読み出す。
/// single-file 発行でも exe 隣のファイル有無に依存しない（<see cref="Controls.LicenseDialog"/> と同方式）。
/// ドキュメント <c>docs/SHORTCUTS.md</c> も同じ <c>shortcuts.json</c> から <c>tools/gen-shortcuts.ps1</c> で生成する。
/// 項目の追加/修正は <c>shortcuts.json</c> を編集するだけ（実キー処理とは独立した表示専用の二重持ち。SSOT 化は別課題）。
/// </summary>
public static class ShortcutCheatSheet
{
    private const string ResourceName = "shortcuts.json";

    /// <summary>表示順に並べたグループ一覧（<c>shortcuts.json</c> の配列順をそのまま維持）。</summary>
    public static IReadOnlyList<ShortcutGroup> Groups { get; } = Load();

    private static IReadOnlyList<ShortcutGroup> Load()
    {
        try
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream is null)
                return Fallback($"{ResourceName} が見つかりません");

            using (stream)
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var data = JsonSerializer.Deserialize<CheatSheetData>(stream, options);
                return data?.Groups ?? new List<ShortcutGroup>();
            }
        }
        catch (Exception ex)
        {
            return Fallback(ex.Message);
        }
    }

    /// <summary>読み込み失敗時に空欄にしない代替表示（原因を 1 項目で見せる）。</summary>
    private static IReadOnlyList<ShortcutGroup> Fallback(string message) =>
        new[] { new ShortcutGroup("（読み込みエラー）", new[] { new ShortcutItem("-", message) }) };

    /// <summary>
    /// <c>shortcuts.json</c> のルート。<c>Title</c>/<c>*Label</c> は生成する <c>SHORTCUTS.md</c> 用のメタ情報で、
    /// アプリ側は <see cref="Groups"/> のみ使用する。
    /// </summary>
    private sealed record CheatSheetData(
        string? Title,
        string? KeysLabel,
        string? DescriptionLabel,
        IReadOnlyList<ShortcutGroup> Groups);
}
