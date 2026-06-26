using PhotoQuickSelector.Core;
using PhotoQuickSelector_App.ViewModels;
using Windows.System;

namespace PhotoQuickSelector_App;

/// <summary>
/// 写真 1 枚への評価キー操作（レーティング / カラーラベル / フラグ）を集約する。
/// サムネイル一覧（<see cref="MainPage"/>）と大画面プレビュー（<see cref="Controls.PreviewControl"/>）
/// の双方から呼び出して挙動を共通化する（SPEC §3-7）。
/// </summary>
public static class PhotoKeyCommands
{
    private const VirtualKey BracketOpen = (VirtualKey)219;  // [
    private const VirtualKey BracketClose = (VirtualKey)221; // ]

    /// <summary>
    /// 現在の修飾子（<see cref="KeyboardModifiers"/>）と <paramref name="key"/> に応じて
    /// <paramref name="item"/> へ適用する評価操作を返す。評価キーでなければ null。
    /// 実行は呼び出し側が <see cref="MainViewModel.ApplyEvaluationAsync"/> 経由で行う
    /// （初回はファイル作成確認ダイアログを挟むため、ここでは実行せず操作だけを返す）。
    /// </summary>
    public static Action? ResolveEvaluation(VirtualKey key, PhotoItemViewModel item)
    {
        // レーティング / カラーラベル / 増減（修飾子なし）
        if (KeyboardModifiers.None)
        {
            switch (key)
            {
                case VirtualKey.Number0: return () => item.SetRating(0);
                case VirtualKey.Number1: return () => item.SetRating(1);
                case VirtualKey.Number2: return () => item.SetRating(2);
                case VirtualKey.Number3: return () => item.SetRating(3);
                case VirtualKey.Number4: return () => item.SetRating(4);
                case VirtualKey.Number5: return () => item.SetRating(5);

                // カラーラベル 6-9 + P（紫）。★旧実装で未割当だった紫に P を割当て。
                case VirtualKey.Number6: return () => item.ToggleColorLabel(ColorLabel.Red);
                case VirtualKey.Number7: return () => item.ToggleColorLabel(ColorLabel.Yellow);
                case VirtualKey.Number8: return () => item.ToggleColorLabel(ColorLabel.Green);
                case VirtualKey.Number9: return () => item.ToggleColorLabel(ColorLabel.Blue);
                case VirtualKey.P: return () => item.ToggleColorLabel(ColorLabel.Purple);

                case BracketOpen: return item.RatingDown;
                case BracketClose: return item.RatingUp;
            }
        }

        // フラグ（Ctrl+上/下）
        if (KeyboardModifiers.Ctrl)
        {
            switch (key)
            {
                case VirtualKey.Up: return item.FlagUp;
                case VirtualKey.Down: return item.FlagDown;
            }
        }

        return null;
    }
}
