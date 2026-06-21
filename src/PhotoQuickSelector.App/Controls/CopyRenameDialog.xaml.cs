using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using PhotoQuickSelector.Core;
using PhotoQuickSelector_App.ViewModels;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// 「リネームしてコピー」の入力フォーム（モーダル）。コピー先・テンプレート・同名時の挙動を
/// 入力し、リネーム後の名前をライブプレビューする。OK（バッチを生成）時に重複・未入力を検証し、
/// 問題があれば閉じずに警告する。実際の bat 生成・実行は呼び出し側（<see cref="FilterBar"/>）が行う。
/// </summary>
public sealed partial class CopyRenameDialog : ContentDialog
{
    private MainViewModel _viewModel = null!;
    private IReadOnlyList<PhotoItemViewModel> _targets = new List<PhotoItemViewModel>();

    public CopyRenameDialog()
    {
        InitializeComponent();
        // ContentDialog は既定で幅を ContentDialogMaxWidth（≈548）にクランプするため広げる。
        Resources["ContentDialogMaxWidth"] = 720.0;
        PrimaryButtonClick += CopyRenameDialog_PrimaryButtonClick;
    }

    /// <summary>入力済みのコピー先フォルダ（絶対パス）。</summary>
    public string DestinationPath => DestinationBox.Text.Trim();

    /// <summary>入力済みのリネームテンプレート。</summary>
    public string RenameTemplate => TemplateBox.Text;

    /// <summary>同名存在時の挙動。</summary>
    public CopyRename.OnExist Policy =>
        PolicyButtons.SelectedIndex == 1 ? CopyRename.OnExist.Skip : CopyRename.OnExist.Overwrite;

    /// <summary>表示前にビューモデルと対象を注入する。</summary>
    public void Configure(MainViewModel viewModel, IReadOnlyList<PhotoItemViewModel> targets)
    {
        _viewModel = viewModel;
        _targets = targets;
        // コピー先の初期値は現在表示中のフォルダ（参照ボタンで変更可能）。
        if (string.IsNullOrEmpty(DestinationBox.Text))
            DestinationBox.Text = viewModel.CurrentFolder ?? "";
        // ファイル名テンプレートの初期値は前回使った内容（保存済みなら復元）。
        var saved = viewModel.Settings.CopyRenameTemplate;
        if (!string.IsNullOrEmpty(saved))
            TemplateBox.Text = saved;
        UpdatePreview();
    }

    // コピー先・テンプレート変更でプレビューと重複警告を更新。
    private void Input_Changed(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        if (_viewModel is null) return;

        var resolved = _viewModel.PreviewCopyNames(RenameTemplate, _targets);
        var dups = _viewModel.FindCopyNameDuplicates(RenameTemplate, _targets);

        // プレビュー（拡張子は元のまま付与）。先頭から最大 50 件表示。
        PreviewList.ItemsSource = resolved
            .Take(50)
            .Select(r =>
            {
                var ext = System.IO.Path.GetExtension(r.SourceName);
                return $"{r.SourceName}  →  {r.NewBase}{ext}";
            })
            .ToList();
        PreviewHeader.Text = $"プレビュー（対象 {resolved.Count} 件）";

        // 検証（優先度: 未入力 ＞ コピー元と同じ ＞ 名前重複）。問題があれば「バッチを生成」を無効化。
        var empty = string.IsNullOrWhiteSpace(DestinationPath);
        var sameAsSource = DestinationEqualsSource();

        if (empty)
        {
            DuplicateWarning.Severity = InfoBarSeverity.Informational;
            DuplicateWarning.Title = "コピー先フォルダを入力してください";
            DuplicateWarning.Message = "";
            DuplicateWarning.IsOpen = true;
        }
        else if (sameAsSource)
        {
            DuplicateWarning.Severity = InfoBarSeverity.Warning;
            DuplicateWarning.Title = "コピー先がコピー元（表示中のフォルダ）と同じです";
            DuplicateWarning.Message = "初期値のまま実行しないよう、別のフォルダを指定してください。";
            DuplicateWarning.IsOpen = true;
        }
        else if (dups.Count > 0)
        {
            var shown = string.Join(", ", dups.Take(10));
            if (dups.Count > 10) shown += $" …他 {dups.Count - 10} 件";
            DuplicateWarning.Severity = InfoBarSeverity.Warning;
            DuplicateWarning.Title = "リネーム後の名前が重複しています";
            DuplicateWarning.Message = $"次の名前が複数の写真に割り当たります（{dups.Count} 件）。テンプレートに連番（{{seq:000}}）等を加えてください: {shown}";
            DuplicateWarning.IsOpen = true;
        }
        else
        {
            DuplicateWarning.IsOpen = false;
        }

        IsPrimaryButtonEnabled = !empty && !sameAsSource && dups.Count == 0;
    }

    /// <summary>コピー先がコピー元（表示中フォルダ）と同一かを正規化して判定する。</summary>
    private bool DestinationEqualsSource()
    {
        var src = _viewModel?.CurrentFolder;
        var dst = DestinationPath;
        if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(dst)) return false;
        try
        {
            var ns = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(src));
            var nd = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(dst));
            return string.Equals(ns, nd, System.StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;   // 不正なパスは比較不能＝同一ではない扱い（生成側で別途検証）。
        }
    }

    // キャレット位置にトークンを挿入。
    private void Token_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string token }) return;
        var pos = TemplateBox.SelectionStart;
        var text = TemplateBox.Text ?? "";
        TemplateBox.Text = text.Insert(pos, token);
        TemplateBox.SelectionStart = pos + token.Length;
        TemplateBox.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
    }

    private void Browse_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        // コピー先テキスト（既定＝表示中フォルダ）を初期表示してネイティブのフォルダ選択を開く。
        var start = !string.IsNullOrWhiteSpace(DestinationBox.Text)
            ? DestinationBox.Text
            : _viewModel?.CurrentFolder;

        var picked = NativeFolderPicker.PickFolder(App.WindowHandle, start);
        if (picked != null)
        {
            DestinationBox.Text = picked;
            UpdatePreview();
        }
    }

    // 「バッチを生成」押下時の保険ガード。通常は IsPrimaryButtonEnabled で押下不可だが、
    // 念のため未入力・コピー元と同じ・名前重複のいずれかなら閉じない。
    private void CopyRenameDialog_PrimaryButtonClick(
        ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(DestinationPath)
            || DestinationEqualsSource()
            || _viewModel.FindCopyNameDuplicates(RenameTemplate, _targets).Count > 0)
        {
            UpdatePreview();   // 該当する警告を表示
            args.Cancel = true;
        }
    }
}
