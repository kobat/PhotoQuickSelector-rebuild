using PhotoQuickSelector.Core;
using PhotoQuickSelector_App.ViewModels;
using Windows.System;

namespace PhotoQuickSelector_App;

/// <summary>
/// 写真への評価キー操作（レーティング / カラーラベル / フラグ）を集約する。
/// サムネイル一覧（<see cref="MainPage"/>）と大画面プレビュー（<see cref="Controls.PreviewControl"/>）
/// の双方から呼び出して挙動を共通化する（SPEC §3-7）。
/// 返すのは適用先非依存の <see cref="Action{PhotoItemViewModel}"/> で、単一（焦点1枚）にも
/// 一括（選択集合の全メンバー）にも同じ操作を流用できる。
/// </summary>
public static class PhotoKeyCommands
{
    private const VirtualKey BracketOpen = (VirtualKey)219;  // [
    private const VirtualKey BracketClose = (VirtualKey)221; // ]

    /// <summary>
    /// 現在の修飾子（<see cref="KeyboardModifiers"/>）と <paramref name="key"/> に応じて
    /// 適用する評価操作を返す。評価キーでなければ null。実行は呼び出し側が
    /// <see cref="MainViewModel.ApplyEvaluationAsync"/> 経由で行う（初回はファイル作成確認ダイアログを
    /// 挟むため、ここでは実行せず操作だけを返す）。
    /// </summary>
    public static Action<PhotoItemViewModel>? ResolveEvaluation(VirtualKey key)
    {
        // レーティング / カラーラベル / 増減（修飾子なし）
        if (KeyboardModifiers.None)
        {
            switch (key)
            {
                case VirtualKey.Number0: return i => i.SetRating(0);
                case VirtualKey.Number1: return i => i.SetRating(1);
                case VirtualKey.Number2: return i => i.SetRating(2);
                case VirtualKey.Number3: return i => i.SetRating(3);
                case VirtualKey.Number4: return i => i.SetRating(4);
                case VirtualKey.Number5: return i => i.SetRating(5);

                // カラーラベル 6-9 + P（紫）。★旧実装で未割当だった紫に P を割当て。
                case VirtualKey.Number6: return i => i.ToggleColorLabel(ColorLabel.Red);
                case VirtualKey.Number7: return i => i.ToggleColorLabel(ColorLabel.Yellow);
                case VirtualKey.Number8: return i => i.ToggleColorLabel(ColorLabel.Green);
                case VirtualKey.Number9: return i => i.ToggleColorLabel(ColorLabel.Blue);
                case VirtualKey.P: return i => i.ToggleColorLabel(ColorLabel.Purple);

                case BracketOpen: return i => i.RatingDown();
                case BracketClose: return i => i.RatingUp();
            }
        }

        // フラグ（Ctrl+上/下）
        if (KeyboardModifiers.Ctrl)
        {
            switch (key)
            {
                case VirtualKey.Up: return i => i.FlagUp();
                case VirtualKey.Down: return i => i.FlagDown();
            }
        }

        return null;
    }

    /// <summary>
    /// 一括評価キーに応じた操作を返す。評価キーでなければ null。単一評価と同じマッピングを、
    /// 選択集合の全メンバーへ流用する。
    /// <list type="bullet">
    ///   <item><c>Alt+0–5</c>: レーティング ／ <c>Alt+6–9</c>・<c>Alt+P</c>: カラーラベル（Alt 単独。Ctrl 併用は対象外）</item>
    ///   <item><c>Ctrl+Alt+↑/↓</c>: フラグ増減（単一フラグ <c>Ctrl+↑/↓</c> に Alt を足した対称形）</item>
    /// </list>
    /// </summary>
    public static Action<PhotoItemViewModel>? ResolveBulkEvaluation(VirtualKey key)
    {
        bool alt = KeyboardModifiers.Alt;
        bool ctrl = KeyboardModifiers.Ctrl;

        // Alt+数字: レーティング / カラーラベル（Ctrl 併用は対象外）
        if (alt && !ctrl)
        {
            switch (key)
            {
                case VirtualKey.Number0: return i => i.SetRating(0);
                case VirtualKey.Number1: return i => i.SetRating(1);
                case VirtualKey.Number2: return i => i.SetRating(2);
                case VirtualKey.Number3: return i => i.SetRating(3);
                case VirtualKey.Number4: return i => i.SetRating(4);
                case VirtualKey.Number5: return i => i.SetRating(5);

                case VirtualKey.Number6: return i => i.ToggleColorLabel(ColorLabel.Red);
                case VirtualKey.Number7: return i => i.ToggleColorLabel(ColorLabel.Yellow);
                case VirtualKey.Number8: return i => i.ToggleColorLabel(ColorLabel.Green);
                case VirtualKey.Number9: return i => i.ToggleColorLabel(ColorLabel.Blue);
                case VirtualKey.P: return i => i.ToggleColorLabel(ColorLabel.Purple);
            }
        }

        // Ctrl+Alt+↑/↓: フラグ増減
        if (ctrl && alt)
        {
            switch (key)
            {
                case VirtualKey.Up: return i => i.FlagUp();
                case VirtualKey.Down: return i => i.FlagDown();
            }
        }

        return null;
    }
}
