using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

namespace PhotoQuickSelector_App;

/// <summary>
/// 現在のキー修飾子（Ctrl / Shift / Alt）を取得するヘルパ。
/// 旧プロジェクトの AppUtils.GetKeyDownModifiers を移植。
/// </summary>
public static class KeyboardModifiers
{
    public static bool IsDown(VirtualKey key)
        => InputKeyboardSource.GetKeyStateForCurrentThread(key)
            .HasFlag(CoreVirtualKeyStates.Down);

    public static bool Ctrl => IsDown(VirtualKey.Control) || IsDown(VirtualKey.RightControl);
    public static bool Shift => IsDown(VirtualKey.Shift) || IsDown(VirtualKey.RightShift);
    public static bool Alt => IsDown(VirtualKey.Menu) || IsDown(VirtualKey.RightMenu);

    /// <summary>修飾子が一切押されていない（素のキー）か。</summary>
    public static bool None => !Ctrl && !Shift && !Alt;
}
