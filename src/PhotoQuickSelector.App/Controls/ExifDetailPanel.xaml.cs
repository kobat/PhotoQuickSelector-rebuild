using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoQuickSelector.Core;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// EXIF 詳細（全ディレクトリ・全タグ）を表示専用で一覧表示するコントロール。
/// タグ総数が数百〜千を超えることがあるため、内部は仮想化対応のグループ化 ListView
/// （<see cref="CollectionViewSource"/>）で描画する。評価編集等の操作は持たない。
/// </summary>
public sealed partial class ExifDetailPanel : UserControl
{
    public ExifDetailPanel() => InitializeComponent();

    /// <summary>タグ一覧を差し替えて表示する（null または空はクリア＝何も表示しない）。</summary>
    public void ShowTags(IReadOnlyList<ExifTagGroup>? groups)
    {
        if (groups is null || groups.Count == 0)
        {
            GroupedTagsSource.Source = null;
            TagListView.Visibility = Visibility.Collapsed;
            MessageText.Visibility = Visibility.Collapsed;
            return;
        }

        // Source の差し替えのみで表示を更新する（x:Bind は使わず OneTime 相当）。
        // ListView は Source 差し替えでスクロール位置が先頭へ戻る（写真切替時の想定挙動）。
        GroupedTagsSource.Source = groups;
        MessageText.Visibility = Visibility.Collapsed;
        TagListView.Visibility = Visibility.Visible;
    }

    /// <summary>一覧をクリアし、中央にメッセージを 1 行表示する（文言は呼び出し側が渡す＝本コントロールは resw を持たない）。</summary>
    public void ShowMessage(string message)
    {
        GroupedTagsSource.Source = null;
        TagListView.Visibility = Visibility.Collapsed;
        MessageText.Text = message;
        MessageText.Visibility = Visibility.Visible;
    }
}
