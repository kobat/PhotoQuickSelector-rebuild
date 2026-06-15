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
    /// <paramref name="item"/> の評価を変更する。処理したら true。
    /// </summary>
    public static bool TryHandleEvaluation(VirtualKey key, PhotoItemViewModel item)
    {
        // レーティング / カラーラベル / 増減（修飾子なし）
        if (KeyboardModifiers.None)
        {
            switch (key)
            {
                case VirtualKey.Number0: item.SetRating(0); return true;
                case VirtualKey.Number1: item.SetRating(1); return true;
                case VirtualKey.Number2: item.SetRating(2); return true;
                case VirtualKey.Number3: item.SetRating(3); return true;
                case VirtualKey.Number4: item.SetRating(4); return true;
                case VirtualKey.Number5: item.SetRating(5); return true;

                // カラーラベル 6-9 + P（紫）。★旧実装で未割当だった紫に P を割当て。
                case VirtualKey.Number6: item.ToggleColorLabel(ColorLabel.Red); return true;
                case VirtualKey.Number7: item.ToggleColorLabel(ColorLabel.Yellow); return true;
                case VirtualKey.Number8: item.ToggleColorLabel(ColorLabel.Green); return true;
                case VirtualKey.Number9: item.ToggleColorLabel(ColorLabel.Blue); return true;
                case VirtualKey.P: item.ToggleColorLabel(ColorLabel.Purple); return true;

                case BracketOpen: item.RatingDown(); return true;
                case BracketClose: item.RatingUp(); return true;
            }
        }

        // フラグ（Ctrl+上/下）
        if (KeyboardModifiers.Ctrl)
        {
            switch (key)
            {
                case VirtualKey.Up: item.FlagUp(); return true;
                case VirtualKey.Down: item.FlagDown(); return true;
            }
        }

        return false;
    }
}
