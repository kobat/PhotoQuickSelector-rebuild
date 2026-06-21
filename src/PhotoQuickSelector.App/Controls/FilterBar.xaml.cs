using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PhotoQuickSelector_App.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// 絞り込み（SPEC §3-4）とクリップボード出力（SPEC §3-5）のフライアウト。共有
/// <see cref="MainViewModel"/> を <see cref="MainPage"/> が注入する。条件入力はフライアウト内の
/// クラシック Binding（DataContext=ViewModel.Filter）で <see cref="FilterViewModel"/> を更新し、
/// レーティング値・比較トグル・コピー操作はコードビハインドで処理する。
/// </summary>
public sealed partial class FilterBar : UserControl
{
    private MainViewModel? _viewModel;

    public FilterBar() => InitializeComponent();

    /// <summary>表示対象のビューモデル。<see cref="MainPage"/> が生成後に注入する。</summary>
    public MainViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (ReferenceEquals(_viewModel, value)) return;
            _viewModel = value;
            Bindings.Update();
        }
    }

    // 開くたびにレーティングピッカーを現在値へ同期（RatingControl は値型不一致で直接束縛できない）。
    private void FilterFlyout_Opening(object sender, object e)
    {
        if (ViewModel is { } vm)
            RatingPicker.Value = vm.Filter.RatingValue > 0 ? vm.Filter.RatingValue : -1;
    }

    private void RatingPicker_ValueChanged(RatingControl sender, object args)
    {
        if (ViewModel is { } vm)
            vm.Filter.RatingValue = sender.Value > 0 ? (int)sender.Value : 0;
    }

    private void CompareButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => ViewModel?.Filter.ToggleCompareMode();

    private void CopyFileNames_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => SetClipboardText(ViewModel?.BuildFileNameListText());

    private void CopyMoveBatch_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => SetClipboardText(ViewModel?.BuildMoveBatchText());

    private static void SetClipboardText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    // === Reject 移動（採用フラグなし・未評価をフォルダ配下の Reject へ） ===

    /// <summary>
    /// 採用フラグなし・未評価の画像を Reject サブフォルダへ移動する一連のフロー。
    /// 対象抽出 → 同名衝突チェック（あれば中断）→ bat 内容の確認ダイアログ →
    /// フォルダ作成＋bat 保存＋実行（ログ出力）→ 完了通知。
    /// </summary>
    private async void RejectUnrated_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;

        if (string.IsNullOrEmpty(vm.CurrentFolder))
        {
            await ShowMessageAsync("Reject 移動", "フォルダが読み込まれていません。");
            return;
        }

        var targets = vm.GetRejectTargets();
        if (targets.Count == 0)
        {
            await ShowMessageAsync("Reject 移動", "採用フラグなし・未評価の画像はありません。");
            return;
        }

        // 1) 同名衝突チェック（Reject に同名ファイルが既にあれば中断）。
        var collisions = vm.FindRejectCollisions(targets);
        if (collisions.Count > 0)
        {
            var shown = string.Join("\n", collisions.Take(20));
            if (collisions.Count > 20) shown += $"\n…他 {collisions.Count - 20} 件";
            await ShowMessageAsync(
                "Reject 移動を中断しました",
                $"Reject フォルダに同名のファイルが既に存在します（{collisions.Count} 件）。\n\n{shown}");
            return;
        }

        // 2) bat をメモリ生成 → 内容を確認ダイアログで表示。
        var now = DateTime.Now;
        var timestamp = now.ToString("yyyyMMddHHmmss");
        var batText = vm.BuildRejectBatchText(targets, now.ToString("yyyy-MM-dd HH:mm:ss"));

        var intro = $"{targets.Count} 件を Reject フォルダへ移動します。Reject フォルダに次のバッチを保存して実行します。";
        if (!await ConfirmBatchAsync("Reject 移動の確認", intro, batText))
            return;

        // 3) Reject 作成（既存は再利用）＋bat 保存＋実行（ログ出力）。
        var result = await vm.RunRejectBatchAsync(batText, timestamp, targets.Count);

        var message = result.Success
            ? $"{result.TargetCount} 件の移動を実行しました。\n\nログ: {result.LogPath}"
            : $"移動を実行しましたが、エラーの可能性があります（終了コード {result.ExitCode}）。\n\nログ: {result.LogPath}";
        await ShowMessageAsync("Reject 移動が完了しました", message);
    }

    // === リネームコピー（絞込結果を任意の宛先へリネームしながらコピー） ===

    /// <summary>
    /// 絞込結果をリネームしながら別フォルダへコピーする一連のフロー。
    /// 入力ダイアログ（宛先・テンプレート・上書き/無視）→ bat 内容の確認ダイアログ →
    /// 宛先フォルダ作成＋bat 保存＋実行（ログ出力）→ 完了通知。
    /// </summary>
    private async void CopyRename_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;

        if (string.IsNullOrEmpty(vm.CurrentFolder))
        {
            await ShowMessageAsync("リネームしてコピー", "フォルダが読み込まれていません。");
            return;
        }

        var targets = vm.GetCopyTargets();
        if (targets.Count == 0)
        {
            await ShowMessageAsync("リネームしてコピー", "コピー対象の画像（絞込結果）がありません。");
            return;
        }

        // 1) 入力ダイアログ（宛先・テンプレート・同名時の挙動）。
        var dialog = new CopyRenameDialog { XamlRoot = XamlRoot };
        dialog.Configure(vm, targets);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        // 2) bat をメモリ生成 → 内容を確認ダイアログで表示。
        var now = DateTime.Now;
        var timestamp = now.ToString("yyyyMMddHHmmss");
        var batText = vm.BuildCopyRenameBatchText(
            dialog.DestinationPath, dialog.RenameTemplate, dialog.Policy,
            targets, now.ToString("yyyy-MM-dd HH:mm:ss"));

        var intro = $"{targets.Count} 件を「{dialog.DestinationPath}」へコピーします。" +
                    "コピー先に次のバッチを保存して実行します。";
        if (!await ConfirmBatchAsync("リネームしてコピーの確認", intro, batText))
            return;

        // 3) 宛先作成（既存は再利用）＋bat 保存＋実行（ログ出力）。
        var result = await vm.RunCopyRenameBatchAsync(
            batText, dialog.DestinationPath, timestamp, targets.Count);

        var message = result.Success
            ? $"{result.TargetCount} 件のコピーを実行しました。\n\nログ: {result.LogPath}"
            : $"コピーを実行しましたが、エラーの可能性があります（終了コード {result.ExitCode}）。\n\nログ: {result.LogPath}";
        await ShowMessageAsync("リネームしてコピーが完了しました", message);
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "OK",
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private async Task<bool> ConfirmBatchAsync(string title, string intro, string batText)
    {
        var box = new TextBox
        {
            // AcceptsReturn は Text より先に true にする。既定（false）のまま改行入りの文字列を
            // 代入すると TextBox が 1 行目で切り捨てるため（初期化子は記述順に代入される）。
            AcceptsReturn = true,
            IsReadOnly = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas"),
            Height = 320,
            Text = batText,
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(box, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);

        var panel = new StackPanel { Spacing = 8, Width = 700 };
        panel.Children.Add(new TextBlock
        {
            Text = intro,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(box);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = "実行",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        // ContentDialog は既定で ContentDialogMaxWidth（≈548）に幅をクランプするため、
        // 内容の Width を広げても頭打ちになる。リソースを上書きしてクランプを外す。
        dialog.Resources["ContentDialogMaxWidth"] = 760.0;
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
