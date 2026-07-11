using System.Collections.Generic;
using Microsoft.UI.Xaml.Controls;
using PhotoQuickSelector_App.ViewModels;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// 「ファイルを移動」の入力フォーム（モーダル）。<see cref="CopyRenameDialog"/> からリネーム
/// テンプレート・同名ポリシー・プレビューを除いた簡易版で、移動先フォルダのみを入力する
/// （移動は元ファイルをそのまま同名で移動するためテンプレートは不要）。OK（バッチを生成）時に
/// 未入力・移動元と同一を検証し、問題があれば閉じずに警告する。実際の bat 生成・実行は
/// 呼び出し側（<see cref="PhotoQuickSelector_App.BatchFlows"/>）が行う。
/// </summary>
public sealed partial class MoveFilesDialog : ContentDialog
{
    private MainViewModel _viewModel = null!;
    private IReadOnlyList<PhotoItemViewModel> _targets = new List<PhotoItemViewModel>();

    public MoveFilesDialog()
    {
        InitializeComponent();
        // ContentDialog は既定で幅を ContentDialogMaxWidth（≈548）にクランプするため広げる。
        Resources["ContentDialogMaxWidth"] = 560.0;
        PrimaryButtonClick += MoveFilesDialog_PrimaryButtonClick;
    }

    /// <summary>入力済みの移動先フォルダ（絶対パス）。</summary>
    public string DestinationPath => DestinationBox.Text.Trim();

    /// <summary>表示前にビューモデルと対象を注入する。</summary>
    public void Configure(MainViewModel viewModel, IReadOnlyList<PhotoItemViewModel> targets)
    {
        _viewModel = viewModel;
        _targets = targets;
        TargetCountText.Text = Loc.Get("MoveFiles_TargetCount", targets.Count);
        // 移動先の初期値：セッション中に指定済みならそれを再利用、初回は表示中フォルダ（参照ボタンで変更可能）。
        if (string.IsNullOrEmpty(DestinationBox.Text))
            DestinationBox.Text = viewModel.LastMoveDestination ?? viewModel.CurrentFolder ?? "";
        UpdateValidation();
    }

    // 移動先変更で検証を更新。
    private void Input_Changed(object sender, TextChangedEventArgs e) => UpdateValidation();

    private void UpdateValidation()
    {
        if (_viewModel is null) return;

        // 検証（優先度: 未入力 ＞ 移動元と同じ）。問題があれば「バッチを生成」を無効化。
        var empty = string.IsNullOrWhiteSpace(DestinationPath);
        var sameAsSource = DestinationEqualsSource();

        if (empty)
        {
            ValidationWarning.Severity = InfoBarSeverity.Informational;
            ValidationWarning.Title = Loc.Get("MoveFiles_WarnDestEmpty");
            ValidationWarning.IsOpen = true;
        }
        else if (sameAsSource)
        {
            ValidationWarning.Severity = InfoBarSeverity.Warning;
            ValidationWarning.Title = Loc.Get("MoveFiles_WarnSameFolder");
            ValidationWarning.IsOpen = true;
        }
        else
        {
            ValidationWarning.IsOpen = false;
        }

        IsPrimaryButtonEnabled = !empty && !sameAsSource;
    }

    /// <summary>移動先が移動元（表示中フォルダ）と同一かを正規化して判定する。</summary>
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

    private void Browse_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        // 移動先テキスト（既定＝表示中フォルダ）を初期表示してネイティブのフォルダ選択を開く。
        var start = !string.IsNullOrWhiteSpace(DestinationBox.Text)
            ? DestinationBox.Text
            : _viewModel?.CurrentFolder;

        var picked = NativeFolderPicker.PickFolder(App.WindowHandle, start);
        if (picked != null)
        {
            DestinationBox.Text = picked;
            UpdateValidation();
        }
    }

    // 「バッチを生成」押下時の保険ガード。通常は IsPrimaryButtonEnabled で押下不可だが、
    // 念のため未入力・移動元と同じのいずれかなら閉じない。
    private void MoveFilesDialog_PrimaryButtonClick(
        ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(DestinationPath) || DestinationEqualsSource())
        {
            UpdateValidation();   // 該当する警告を表示
            args.Cancel = true;
        }
    }
}
