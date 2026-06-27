using Microsoft.UI.Xaml.Controls;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// ショートカット一覧（チートシート）ダイアログ（モーダル）。<see cref="ShortcutCheatSheet.Groups"/> を
/// カテゴリ別にデータ駆動で表示する。ハンバーガーメニュー「ショートカット一覧…」と F1 キーから開く。
/// </summary>
public sealed partial class ShortcutsDialog : ContentDialog
{
    public ShortcutsDialog()
    {
        InitializeComponent();
        // ContentDialog は既定で幅を ContentDialogMaxWidth（≈548）にクランプするため広げる。
        Resources["ContentDialogMaxWidth"] = 720.0;
        GroupsRepeater.ItemsSource = ShortcutCheatSheet.Groups;
    }
}
