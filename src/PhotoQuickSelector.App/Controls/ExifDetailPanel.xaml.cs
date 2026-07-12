using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PhotoQuickSelector.Core;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// EXIF 詳細（全ディレクトリ・全タグ）を表示専用で一覧表示するコントロール。
/// タグ総数が数百〜千を超えることがあるため、内部は仮想化対応のグループ化 ListView
/// （<see cref="CollectionViewSource"/>）で描画する。評価編集等の操作は持たない。
/// </summary>
public sealed partial class ExifDetailPanel : UserControl
{
    /// <summary>Ctrl+Alt+↑/↓ の 1 回あたりの縦スクロール量（DIP）。行高より少し大きく＝数行送り。</summary>
    private const double LineScrollStep = 54;

    /// <summary>ページ送り時に前後で残す重なり（DIP）。文脈を 1〜2 行残すため viewport から引く。</summary>
    private const double PageOverlap = 40;

    // ListView テンプレート内の ScrollViewer は生成後不変なので、初回に見つけたら使い回す。
    private ScrollViewer? _scrollViewer;

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

    /// <summary>タグ一覧を数行ぶん縦スクロールする（Ctrl+Alt+↑/↓）。メッセージ表示中・未実現時は何もしない。</summary>
    public void ScrollByLine(bool down)
        => ScrollByOffset(down ? LineScrollStep : -LineScrollStep);

    /// <summary>タグ一覧をページ送りする（Ctrl+Alt+←＝上／→＝下）。メッセージ表示中・未実現時は何もしない。</summary>
    public void ScrollByPage(bool down)
    {
        var sv = GetScrollViewer();
        if (sv == null) return;
        // viewport 高から重なり分を引いた量を 1 ページとする（下限は 1 行送り）。
        double page = Math.Max(sv.ViewportHeight - PageOverlap, LineScrollStep);
        ScrollByOffset(down ? page : -page, sv);
    }

    /// <summary>内部 ScrollViewer を delta（DIP）だけ縦移動する。範囲外は ChangeView がクランプする。</summary>
    private void ScrollByOffset(double delta, ScrollViewer? sv = null)
    {
        sv ??= GetScrollViewer();
        if (sv == null) return;
        sv.ChangeView(null, sv.VerticalOffset + delta, null, disableAnimation: true);
    }

    /// <summary>ListView テンプレート内の ScrollViewer を取得（初回のみ探索してキャッシュ）。一覧非表示中は null。</summary>
    private ScrollViewer? GetScrollViewer()
    {
        if (TagListView.Visibility != Visibility.Visible) return null;
        return _scrollViewer ??= FindDescendant<ScrollViewer>(TagListView);
    }

    /// <summary>ビジュアルツリーを深さ優先で辿り、最初に見つかった型 T の子孫を返す。</summary>
    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) return typed;
            if (FindDescendant<T>(child) is { } found) return found;
        }
        return null;
    }
}
