using System.Linq;
using Microsoft.UI.Xaml.Controls;
using PhotoQuickSelector_App.ViewModels;

namespace PhotoQuickSelector_App;

/// <summary>
/// グリッド/フィルムストリップのマウス修飾クリック（Ctrl+クリック＝トグル参加／Shift+クリック＝レンジ選択）を
/// 集約する。両ビューの SelectionChanged から呼ばれ、処理したら true（呼び出し側は選択をVMの焦点へ復元する）。
/// Ctrl+Shift は Shift（レンジ）として扱い、Alt 併用は対象外（素のクリック扱い）。
/// </summary>
public static class SelectionMouseCommands
{
    public static bool TryHandle(SelectionChangedEventArgs e, MainViewModel vm)
    {
        bool ctrl = KeyboardModifiers.Ctrl;
        bool shift = KeyboardModifiers.Shift;
        if (KeyboardModifiers.Alt || (!ctrl && !shift)) return false;

        // Ctrl+クリックで既選択項目を外すと Added が空になる（SelectedItem=null）ので Removed も見る
        var clicked = (e.AddedItems.FirstOrDefault() ?? e.RemovedItems.FirstOrDefault()) as PhotoItemViewModel;
        // フォルダ切替等の ItemsSource リセットでも SelectionChanged が来る。現行 Photos に居ない項目は対象外
        if (clicked == null || !vm.Photos.Contains(clicked)) return false;

        if (shift) vm.ExtendSelectionTo(clicked);
        else vm.ToggleSelectionAt(clicked);
        return true;
    }
}
