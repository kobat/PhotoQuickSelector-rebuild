using System.Collections.Generic;
using Microsoft.UI.Xaml;
using PhotoQuickSelector.Core;
using PhotoQuickSelector_App.ViewModels;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// 右パネル上段の画像情報パネル（ルーペと同一セルで排他表示。E キー／ヘッダのタブで切替）。
/// 表示状態は <see cref="AppSettings.PreviewExifPanel"/> に永続化（実保存は終了時の一括 Save。
/// 設定キー名は「EXIF 詳細パネル」時代のまま＝既存ユーザーの設定を保つため変えていない）。
///
/// グループの並びは File → 評価（このアプリ）→ EXIF 等。File を先頭へ移すのは
/// <see cref="ExifTagReader.ReadAllTags"/> 側で済んでいる。
///
/// 描画コスト対策の要点（画像切替のスピードを損なわないため）:
///   ・全タグ解析（ファイル I/O＋パース）は <see cref="PhotoItemViewModel.EnsureExifGroupsAsync"/> で
///     バックグラウンド実行＋VM 常駐キャッシュ（往復での再解析ゼロ）。
///   ・焦点変更（←/→ 連打経路）では重い <c>ShowInfo</c>（グループ化 ListView の再構築）を行わず、
///     プレースホルダ「読込中…」だけ即表示する。実描画は停止後に settle タイマ経由で 1 回だけ行う
///     （キャッシュ済み・未解析にかかわらず「1 停止 1 回」に統一）。誤った（前の写真の）情報は出さない。
///   ・評価変更では <c>ShowInfo</c> を呼ばず、評価行のプロパティだけを書き換える（再構築なし・
///     一覧のスクロール位置も動かない）。<see cref="EvaluationInfoSection"/> 参照。
/// </summary>
public sealed partial class PreviewControl
{
    private bool _showExifPanel;   // true=画像情報 / false=ルーペ（既定）
    private int _exifLoadToken;    // 高速ナビでの追い越し対策（LoadCurrentAsync の _loadToken と同じ考え方）

    // 評価グループ（行オブジェクトを使い回すので 1 個だけ持つ）。Loc を使うため遅延生成する。
    private EvaluationInfoSection? _evalSection;

    private EvaluationInfoSection EvalSection => _evalSection ??= new EvaluationInfoSection();

    /// <summary>
    /// 右パネル上段をルーペ ⇄ 画像情報で切り替える（E キー・メイン画像右クリックメニュー・
    /// ハンバーガーメニューの「画像情報パネル」から共通で呼ぶ）。
    /// </summary>
    public void ToggleExifPanel() => SetExifPanelVisible(!_showExifPanel);

    /// <summary>現在 画像情報パネルを表示中か（ルーペ表示中なら false。メニューのチェック表示用）。</summary>
    public bool IsExifPanelShown => _showExifPanel;

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
            // 表示へ切り替えた瞬間は連打経路ではないので即描画（キャッシュ済みは即・未解析は解析）。
            RenderExifForFocus();
        }
        else
        {
            // 非表示（Collapsed）中に写真が切り替わるとキャンバス寸法 0 のままルーペ中心が
            // 決まっているため、レイアウト確定後に AF 点へ寄せ直す。
            DispatcherQueue.TryEnqueue(ScrollZoomToFocus);
        }
    }

    /// <summary>
    /// 焦点変更（連打経路）での軽量更新。重い <c>ShowInfo</c> は行わず、プレースホルダだけ即表示する
    /// （<c>ShowMessage</c> は可視性トグル＝ListView の再構築を伴わない）。実描画は停止後の
    /// <see cref="RenderExifForFocus"/>（settle タイマ）が 1 回だけ行う。非表示中・プレビュー外では何もしない。
    /// </summary>
    private void OnFocusChangedForExif()
    {
        if (!_showExifPanel || _viewModel?.IsPreviewMode != true) return;

        _exifLoadToken++; // 進行中の解析結果（前の焦点ぶん）を無効化する
        if (_viewModel.FocusedPhoto == null)
        {
            ExifPanel.ShowInfo(null);
            return;
        }

        // 前の写真の内容を残さない＝誤情報を出さない。中身は settle 後に確定描画する。
        ExifPanel.ShowMessage(Loc.Get("ExifPanel_Loading"));
    }

    /// <summary>
    /// 焦点写真の評価が変わったときの更新。ListView は触らず評価行の内容だけを差し替える
    /// （再構築なし＝スクロール位置も維持）。非表示中は次の描画時に <see cref="RenderExifForFocus"/> が
    /// 同じ更新を行うので何もしない。
    /// </summary>
    private void OnFocusEvaluationChangedForExif()
    {
        if (!_showExifPanel || _viewModel?.IsPreviewMode != true) return;
        if (_viewModel.FocusedPhoto is { } photo) EvalSection.Update(photo);
    }

    /// <summary>
    /// 焦点写真の情報をパネルへ実際に描画する（停止後の settle 確定・E キー/タブ切替・プレビュー入場から呼ぶ）。
    /// キャッシュ済みなら即描画、未解析はバックグラウンド解析を待って反映する。
    /// 追い越し（描画確定前に焦点がさらに動いた）／ルーペへ切替済みはトークンで破棄する。
    /// </summary>
    private async void RenderExifForFocus()
    {
        if (!_showExifPanel || _viewModel?.IsPreviewMode != true) return;

        int token = ++_exifLoadToken;
        var photo = _viewModel.FocusedPhoto;
        if (photo == null)
        {
            ExifPanel.ShowInfo(null);
            return;
        }

        // 常駐キャッシュ済み（＝一度見た画像）は再解析ゼロで即表示。
        if (photo.CachedExifGroups is { } cached)
        {
            ExifPanel.ShowInfo(BuildInfoGroups(photo, cached));
            return;
        }

        // 未解析はバックグラウンドで解析（UI スレッドを塞がない）。待つ間は読込中を出しておく。
        ExifPanel.ShowMessage(Loc.Get("ExifPanel_Loading"));
        var groups = await photo.EnsureExifGroupsAsync();
        if (token != _exifLoadToken || !_showExifPanel) return; // 追い越し/ルーペへ切替済みは捨てる
        ExifPanel.ShowInfo(BuildInfoGroups(photo, groups));
    }

    /// <summary>
    /// 表示するグループ列を組み立てる（File → 評価 → 残りの EXIF 等）。評価行は毎回ここで最新化する
    /// （非表示中に評価が変わっていても次の描画で追いつく）。EXIF が空（未対応・破損ファイル）でも
    /// 評価グループだけは出す。
    /// </summary>
    private IReadOnlyList<InfoGroup> BuildInfoGroups(
        PhotoItemViewModel photo, IReadOnlyList<ExifTagGroup> exifGroups)
    {
        var section = EvalSection;
        section.Update(photo);

        var groups = new List<InfoGroup>(exifGroups.Count + 1);
        int index = 0;

        // File グループは ReadAllTags が先頭へ並べ替えている。あれば評価より上に置く。
        if (exifGroups.Count > 0 && exifGroups[0].DirectoryName == ExifTagReader.FileDirectoryName)
        {
            groups.Add(new InfoGroup(exifGroups[0].DirectoryName, exifGroups[0].Tags));
            index = 1;
        }

        groups.Add(new InfoGroup(section.GroupName, section.Rows));

        for (; index < exifGroups.Count; index++)
            groups.Add(new InfoGroup(exifGroups[index].DirectoryName, exifGroups[index].Tags));

        return groups;
    }
}
