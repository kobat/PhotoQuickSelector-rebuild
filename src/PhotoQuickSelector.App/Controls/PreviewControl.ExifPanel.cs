using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using PhotoQuickSelector.Core;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// 右パネル上段の EXIF 詳細パネル（ルーペと同一セルで排他表示。E キー／ヘッダのタブで切替）。
/// 全タグの読取り（ファイル I/O＋パース）はバックグラウンドで行い、焦点写真の変更に追従する。
/// 表示状態は <see cref="AppSettings.PreviewExifPanel"/> に永続化（実保存は終了時の一括 Save）。
/// </summary>
public sealed partial class PreviewControl
{
    private bool _showExifPanel;   // true=EXIF 詳細 / false=ルーペ（既定）
    private int _exifLoadToken;    // 高速ナビでの追い越し対策（LoadCurrentAsync の _loadToken と同じ考え方）

    /// <summary>右パネル上段をルーペ ⇄ EXIF 詳細で切り替える（E キー用）。</summary>
    private void ToggleExifPanel() => SetExifPanelVisible(!_showExifPanel);

    private void LoupeTabButton_Click(object sender, RoutedEventArgs e) => SetExifPanelVisible(false);

    private void ExifTabButton_Click(object sender, RoutedEventArgs e) => SetExifPanelVisible(true);

    /// <summary>
    /// 上段の表示状態を反映する（タブの見た目・設定への控え・表示直後のコンテンツ更新まで）。
    /// ViewModel 注入時の復元と、タブ/E キーの切替の両方から呼ばれる。
    /// </summary>
    private void SetExifPanelVisible(bool showExif)
    {
        _showExifPanel = showExif;
        LoupeTabButton.IsChecked = !showExif;
        ExifTabButton.IsChecked = showExif;
        ZoomCanvas.Visibility = showExif ? Visibility.Collapsed : Visibility.Visible;
        ExifPanel.Visibility = showExif ? Visibility.Visible : Visibility.Collapsed;
        if (_viewModel != null) _viewModel.Settings.PreviewExifPanel = showExif;

        if (showExif)
        {
            RefreshExifPanel();
        }
        else
        {
            // 非表示（Collapsed）中に写真が切り替わるとキャンバス寸法 0 のままルーペ中心が
            // 決まっているため、レイアウト確定後に AF 点へ寄せ直す。
            DispatcherQueue.TryEnqueue(ScrollZoomToFocus);
        }
    }

    /// <summary>
    /// 焦点写真の全タグを読み直してパネルへ反映する。非表示中・プレビュー外では何もしない
    /// （表示に切り替えた瞬間に読み直すので取りこぼさない）。
    /// </summary>
    private async void RefreshExifPanel()
    {
        if (!_showExifPanel || _viewModel?.IsPreviewMode != true) return;

        int token = ++_exifLoadToken;
        var photo = _viewModel.FocusedPhoto;
        if (photo == null)
        {
            ExifPanel.ShowTags(null);
            return;
        }

        // 読取りは 1 ファイル数ms〜十数ms だが UI スレッドから外す。先読みキャッシュとは独立
        // （ピクセルではなくメタデータのみで軽量なため、都度読みで足りる）。
        string path = photo.Meta.Path;
        var groups = await Task.Run(() => ExifTagReader.ReadAllTags(path));
        if (token != _exifLoadToken || !_showExifPanel) return; // 追い越し/ルーペへ切替済みは捨てる
        ExifPanel.ShowTags(groups);
    }
}
