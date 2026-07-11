using PhotoQuickSelector_App.ViewModels;
using Windows.System;

namespace PhotoQuickSelector_App;

/// <summary>
/// フィルムストリップ/グリッドの複数選択キー操作を集約する。評価キー（<see cref="PhotoKeyCommands"/>）と
/// 同じく、サムネイル一覧（<see cref="MainPage"/>）と大画面プレビュー（<see cref="Controls.PreviewControl"/>）の
/// 双方から呼び出して挙動を共通化する。
/// <list type="bullet">
///   <item><c>Shift+←/→</c>: レンジ起点から焦点までを連続選択（焦点も移動）</item>
///   <item><c>Ctrl+←/→</c>: 選択集合を変えずに焦点だけ移動（集合外へも可）</item>
///   <item><c>Ctrl+Space</c>: 焦点の写真を選択集合へ参加/解除</item>
///   <item><c>Ctrl+A</c>: 絞込結果（<c>Photos</c>）の全件を選択集合にする（Windows 標準の全選択）</item>
///   <item><c>Esc</c>: 選択集合を解除（集合があるときのみ消費）</item>
/// </list>
/// </summary>
public static class SelectionKeyCommands
{
    /// <summary>キーを処理したら true。フォーカス非依存の集約点から呼ばれる。</summary>
    public static bool TryHandle(VirtualKey key, MainViewModel vm)
    {
        bool ctrl = KeyboardModifiers.Ctrl;
        bool shift = KeyboardModifiers.Shift;
        bool alt = KeyboardModifiers.Alt;

        // Shift+←/→ : レンジ選択（Shift+Alt は別機能なので alt は除外）
        if (shift && !ctrl && !alt)
        {
            switch (key)
            {
                case VirtualKey.Left: vm.ExtendSelectionTo(-1); return true;
                case VirtualKey.Right: vm.ExtendSelectionTo(1); return true;
            }
        }

        // Ctrl+←/→ : 焦点のみ移動（集合不変） / Ctrl+Space : 焦点をトグル参加 / Ctrl+A : 全選択
        // ※ Ctrl+↑/↓ はフラグ評価、Ctrl+L はフィルタなので衝突しない。
        // Ctrl+A（Windows 標準の全選択）は絞込結果 Photos の全件を選択集合にする（右クリック「すべて選択」と同一）。
        if (ctrl && !shift && !alt)
        {
            switch (key)
            {
                case VirtualKey.Left: vm.MoveFocusKeepingSelection(-1); return true;
                case VirtualKey.Right: vm.MoveFocusKeepingSelection(1); return true;
                case VirtualKey.Space: vm.ToggleFocusInSelection(); return true;
                case VirtualKey.A: vm.SelectAll(); return true;
            }
        }

        // Esc : 選択集合の解除（集合があるときだけ消費。無ければ未処理で通す）
        if (key == VirtualKey.Escape && !ctrl && !shift && !alt && vm.SelectedPhotos.Count > 0)
        {
            vm.ClearSelection();
            return true;
        }

        return false;
    }
}
