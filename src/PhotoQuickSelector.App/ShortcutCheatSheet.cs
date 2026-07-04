using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Nodes;

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
/// ドキュメント <c>docs/SHORTCUTS.md</c>（日）/<c>SHORTCUTS.en.md</c>（英）も同じ <c>shortcuts.json</c> から
/// <c>tools/gen-shortcuts.ps1</c> で生成する。
/// 各テキストは「文字列（全言語共通）」または「<c>{"ja": …, "en": …}</c> オブジェクト」で、
/// 表示言語（resw の <c>LangCode</c>＝アプリの言語解決に追従）で選択する。
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
                return Fallback(Loc.Get("Shortcuts_ResourceNotFound", ResourceName));

            using (stream)
            {
                var root = JsonNode.Parse(stream);
                // 言語は resw の解決結果に追従（PrimaryLanguageOverride／OS 言語と一貫する）。
                var lang = Loc.Get("LangCode");

                var groups = new List<ShortcutGroup>();
                foreach (var g in root?["groups"] as JsonArray ?? new JsonArray())
                {
                    if (g is null) continue;
                    var items = new List<ShortcutItem>();
                    foreach (var i in g["items"] as JsonArray ?? new JsonArray())
                    {
                        if (i is null) continue;
                        items.Add(new ShortcutItem(Pick(i["keys"], lang), Pick(i["description"], lang)));
                    }
                    groups.Add(new ShortcutGroup(Pick(g["title"], lang), items));
                }
                return groups;
            }
        }
        catch (Exception ex)
        {
            return Fallback(ex.Message);
        }
    }

    /// <summary>
    /// 文字列ならそのまま（全言語共通）、オブジェクトなら <paramref name="lang"/> → <c>ja</c> → 先頭
    /// の順で値を選ぶ（訳が未整備でも空欄にしない）。
    /// </summary>
    private static string Pick(JsonNode? node, string lang)
    {
        switch (node)
        {
            case JsonValue v when v.TryGetValue<string>(out var s):
                return s;
            case JsonObject o:
                if (o.TryGetPropertyValue(lang, out var ln) &&
                    ln is JsonValue lv && lv.TryGetValue<string>(out var ls)) return ls;
                if (o.TryGetPropertyValue("ja", out var jn) &&
                    jn is JsonValue jv && jv.TryGetValue<string>(out var js)) return js;
                foreach (var kv in o)
                    if (kv.Value is JsonValue fv && fv.TryGetValue<string>(out var fs)) return fs;
                return "";
            default:
                return "";
        }
    }

    /// <summary>読み込み失敗時に空欄にしない代替表示（原因を 1 項目で見せる）。</summary>
    private static IReadOnlyList<ShortcutGroup> Fallback(string message) =>
        new[] { new ShortcutGroup(Loc.Get("Shortcuts_LoadErrorGroup"), new[] { new ShortcutItem("-", message) }) };
}
