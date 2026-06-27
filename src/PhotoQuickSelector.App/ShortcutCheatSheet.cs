using System.Collections.Generic;

namespace PhotoQuickSelector_App;

/// <summary>ショートカット一覧（チートシート）の 1 行＝キー表記とその説明。</summary>
public sealed record ShortcutItem(string Keys, string Description);

/// <summary>ショートカット一覧のグループ（カテゴリ）。<see cref="Items"/> をその見出しでまとめる。</summary>
public sealed record ShortcutGroup(string Title, IReadOnlyList<ShortcutItem> Items);

/// <summary>
/// ショートカット一覧（<see cref="Controls.ShortcutsDialog"/>）の表示専用データ。CLAUDE.md「キー操作」節を
/// 元にした静的定義で、実キー処理（<see cref="PhotoKeyCommands"/> / <see cref="SelectionKeyCommands"/> /
/// <see cref="PhotoFileCommands"/> / <c>PreviewControl.Input</c> / <c>MainWindow.RootGrid_PreviewKeyDown</c>）
/// とは独立している（表示と処理の二重持ち。SSOT 化は将来課題）。項目追加はこのリストへ 1 行足すだけ。
/// </summary>
public static class ShortcutCheatSheet
{
    /// <summary>表示順に並べたグループ一覧（全般→表示→移動→評価→複数選択→ファイル連携）。</summary>
    public static IReadOnlyList<ShortcutGroup> Groups { get; } = new ShortcutGroup[]
    {
        new("全般", new ShortcutItem[]
        {
            new("F1", "ショートカット一覧を表示"),
            new("Ctrl+L", "フィルタ ON/OFF を切替（フライアウトは開かない）"),
            new("F11", "全画面表示の切替"),
            new("Shift+F", "完全全画面モード（全画面＋左ペイン/ステータスバー非表示＋イマーシブ）の切替"),
            new("Esc", "全画面/完全全画面を解除（選択集合があれば解除）"),
        }),
        new("表示", new ShortcutItem[]
        {
            new("クリック", "メイン画像をフィット⇄ズーム切替（クリック位置が中心）"),
            new("ダブルクリック", "メイン画像を 100% 表示（クリック位置が中心）"),
            new("ドラッグ", "パン（表示位置を移動）"),
            new("ホイール", "段ズーム（カーソル位置が中心）"),
            new("+ / -", "段ズーム イン/アウト"),
            new("Z", "フィット⇄ズーム切替（ズーム側は直近の倍率/位置を復元）"),
            new("Shift+Z", "等倍 100% 表示"),
            new("Shift+Alt+← / →", "フィット / 等倍"),
            new("F", "イマーシブ表示（右パネル/フィルムストリップを畳む）の切替"),
            new("I", "メタ情報オーバーレイの切替"),
            new("G", "構図グリッドの種類を巡回（なし→中央十字→三分割→正方形）"),
            new("Shift+G", "構図グリッドの基準を切替（画像⇄Canvas）"),
            new("C", "先読みキャッシュ一覧の切替（デバッグ）"),
        }),
        new("移動", new ShortcutItem[]
        {
            new("← / →", "前後の写真へ移動（移動後フォーカスはフィルムストリップへ）"),
            new("ダブルクリック", "フィルムストリップ：グリッド表示へ戻る"),
            new("PageUp / PageDown / Home / End", "フィルムストリップ内を移動"),
            new("Alt+← / → / ↑ / ↓", "パン（画像を上下左右へ移動）"),
            new("Alt+F", "フォーカス点へスクロール"),
            new("Ctrl+Alt+← / →", "ルーペを横スクロール"),
            new("Ctrl+Alt+↑ / ↓", "ルーペを縦スクロール（選択集合があるときは一括フラグ）"),
            new("Ctrl+Alt+F", "ルーペをフォーカス点へ"),
        }),
        new("評価", new ShortcutItem[]
        {
            new("0 – 5", "レーティング（通常評価は焦点の 1 枚のみに反映）"),
            new("6 / 7 / 8 / 9", "カラーラベル（赤 / 橙 / 緑 / 青）"),
            new("P", "カラーラベル（紫）"),
            new("[ / ]", "レーティングを増減"),
            new("Ctrl+↑ / ↓", "フラグ（採用 / 拒否）"),
        }),
        new("複数選択", new ShortcutItem[]
        {
            new("Shift+← / →", "レンジ選択（起点から焦点までを連続選択）"),
            new("Ctrl+← / →", "焦点のみ移動（選択集合は不変）"),
            new("Ctrl+Space", "焦点を選択集合へ参加 / 解除"),
            new("← / →（選択集合中）", "メンバー内で焦点を巡回"),
            new("Esc", "選択集合を解除"),
            new("Alt+0 – 5", "一括レーティング（選択集合の全メンバーへ）"),
            new("Alt+6 – 9 / Alt+P", "一括カラーラベル（選択集合の全メンバーへ）"),
            new("Ctrl+Alt+↑ / ↓", "一括フラグ（選択集合の全メンバーへ）"),
        }),
        new("ファイル連携", new ShortcutItem[]
        {
            new("Ctrl+E", "エクスプローラーで表示"),
            new("Alt+E", "既定のアプリで開く"),
            new("Ctrl+Alt+E", "パスをコピー"),
            new("Alt+S", "共有（設定の共有先 exe、未設定なら標準の共有シート）"),
        }),
    };
}
