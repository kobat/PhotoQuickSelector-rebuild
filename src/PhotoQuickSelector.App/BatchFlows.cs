using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PhotoQuickSelector_App.Controls;
using PhotoQuickSelector_App.ViewModels;

namespace PhotoQuickSelector_App;

/// <summary>
/// Reject 移動・リネームコピー・ファイル移動の一連のフロー（対象抽出は呼び出し側／衝突チェック →
/// 確認ダイアログ → フォルダ作成＋bat 保存＋実行 → 完了通知）を、対象リストを引数に取る形で集約する。
/// フィルタバー（絞込結果／未評価全件が対象）と右クリックメニュー（選択集合が対象）の双方から呼ぶ。
/// ダイアログ表示のため <see cref="XamlRoot"/> を受け取る（VM は XamlRoot を持たない）。
/// </summary>
public static class BatchFlows
{
    // 大量選択時の一括操作（既定アプリで開く／共有／ファイルコピー）に確認を挟むしきい値。
    // エクスプローラの同種警告（15）より写真は重い（1件あたりデコード・IPC・プロセス起動コストが大きい）ため低めにしている。
    internal const int BulkWarnThreshold = 10;

    /// <summary>
    /// <paramref name="targets"/> を Reject サブフォルダへ移動する。
    /// 同名衝突があれば中断し、確認ダイアログで bat 内容を見せてから実行する。
    /// </summary>
    public static async Task RunRejectAsync(
        MainViewModel vm, IReadOnlyList<PhotoItemViewModel> targets, XamlRoot xamlRoot)
    {
        if (string.IsNullOrEmpty(vm.CurrentFolder))
        {
            await ShowMessageAsync(xamlRoot, Loc.Get("RejectMove_Title"), Loc.Get("Msg_NoFolderLoaded"));
            return;
        }
        if (targets.Count == 0)
        {
            await ShowMessageAsync(xamlRoot, Loc.Get("RejectMove_Title"), Loc.Get("RejectMove_NoTargets"));
            return;
        }

        // 1) 同名衝突チェック（Reject に同名ファイルが既にあれば中断）。
        var collisions = vm.FindRejectCollisions(targets);
        if (collisions.Count > 0)
        {
            var shown = string.Join("\n", collisions.Take(20));
            if (collisions.Count > 20) shown += "\n" + Loc.Get("Msg_MoreItemsSuffix", collisions.Count - 20);
            await ShowMessageAsync(
                xamlRoot,
                Loc.Get("RejectMove_AbortedTitle"),
                Loc.Get("RejectMove_Collisions", collisions.Count, shown));
            return;
        }

        // 2) bat をメモリ生成 → 内容を確認ダイアログで表示。
        var now = DateTime.Now;
        var timestamp = now.ToString("yyyyMMddHHmmss");
        var batText = vm.BuildRejectBatchText(targets, now.ToString("yyyy-MM-dd HH:mm:ss"));

        var intro = Loc.Get("RejectMove_ConfirmIntro", targets.Count);
        if (!await ConfirmBatchAsync(xamlRoot, Loc.Get("RejectMove_ConfirmTitle"), intro, batText))
            return;

        // 3) Reject 作成（既存は再利用）＋bat 保存＋実行（ログ出力）。
        var result = await vm.RunRejectBatchAsync(batText, timestamp, targets.Count);

        var message = result.Success
            ? Loc.Get("RejectMove_Done", result.TargetCount, result.LogPath)
            : Loc.Get("RejectMove_DoneWithError", result.ExitCode, result.LogPath);
        await ShowMessageAsync(xamlRoot, Loc.Get("RejectMove_DoneTitle"), message);
    }

    /// <summary>
    /// <paramref name="targets"/> をリネームしながら別フォルダへコピーする。
    /// 入力ダイアログ（宛先・テンプレート・上書き/無視）→ bat 確認 → 実行。
    /// </summary>
    public static async Task RunCopyRenameAsync(
        MainViewModel vm, IReadOnlyList<PhotoItemViewModel> targets, XamlRoot xamlRoot)
    {
        if (string.IsNullOrEmpty(vm.CurrentFolder))
        {
            await ShowMessageAsync(xamlRoot, Loc.Get("CopyRename_MsgTitle"), Loc.Get("Msg_NoFolderLoaded"));
            return;
        }
        if (targets.Count == 0)
        {
            await ShowMessageAsync(xamlRoot, Loc.Get("CopyRename_MsgTitle"), Loc.Get("CopyRename_NoTargets"));
            return;
        }

        // 1) 入力ダイアログ（宛先・テンプレート・同名時の挙動）。
        var dialog = new CopyRenameDialog { XamlRoot = xamlRoot };
        dialog.Configure(vm, targets);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        // 使ったテンプレートを次回の初期値として保存。
        vm.Settings.CopyRenameTemplate = dialog.RenameTemplate;
        vm.Settings.Save();

        // 指定したコピー先をセッション中だけ記憶（永続化しない）。
        vm.LastCopyDestination = dialog.DestinationPath;

        // 2) bat をメモリ生成 → 内容を確認ダイアログで表示。
        var now = DateTime.Now;
        var timestamp = now.ToString("yyyyMMddHHmmss");
        var batText = vm.BuildCopyRenameBatchText(
            dialog.DestinationPath, dialog.RenameTemplate, dialog.Policy,
            targets, now.ToString("yyyy-MM-dd HH:mm:ss"));

        var intro = Loc.Get("CopyRename_ConfirmIntro", targets.Count, dialog.DestinationPath);
        if (!await ConfirmBatchAsync(xamlRoot, Loc.Get("CopyRename_ConfirmTitle"), intro, batText))
            return;

        // 3) 宛先作成（既存は再利用）＋bat 保存＋実行（ログ出力）。
        var result = await vm.RunCopyRenameBatchAsync(
            batText, dialog.DestinationPath, timestamp, targets.Count);

        var message = result.Success
            ? Loc.Get("CopyRename_Done", result.TargetCount, result.LogPath)
            : Loc.Get("CopyRename_DoneWithError", result.ExitCode, result.LogPath);
        await ShowMessageAsync(xamlRoot, Loc.Get("CopyRename_DoneTitle"), message);
    }

    /// <summary>
    /// <paramref name="targets"/>（絞込結果）を任意の宛先フォルダへ移動する。
    /// 入力ダイアログ（宛先）→ 同名衝突チェック → bat 確認 → 実行、の順で
    /// <see cref="RunRejectAsync"/> と <see cref="RunCopyRenameAsync"/> を合成した流れ。
    /// </summary>
    public static async Task RunMoveAsync(
        MainViewModel vm, IReadOnlyList<PhotoItemViewModel> targets, XamlRoot xamlRoot)
    {
        if (string.IsNullOrEmpty(vm.CurrentFolder))
        {
            await ShowMessageAsync(xamlRoot, Loc.Get("MoveFiles_Title"), Loc.Get("Msg_NoFolderLoaded"));
            return;
        }
        if (targets.Count == 0)
        {
            await ShowMessageAsync(xamlRoot, Loc.Get("MoveFiles_Title"), Loc.Get("MoveFiles_NoTargets"));
            return;
        }

        // 1) 入力ダイアログ（移動先）。
        var dialog = new MoveFilesDialog { XamlRoot = xamlRoot };
        dialog.Configure(vm, targets);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        // 指定した移動先をセッション中だけ記憶（永続化しない）。
        vm.LastMoveDestination = dialog.DestinationPath;

        // 2) 同名衝突チェック（移動先に同名ファイルが既にあれば中断）。
        var collisions = vm.FindCollisions(dialog.DestinationPath, targets);
        if (collisions.Count > 0)
        {
            var shown = string.Join("\n", collisions.Take(20));
            if (collisions.Count > 20) shown += "\n" + Loc.Get("Msg_MoreItemsSuffix", collisions.Count - 20);
            await ShowMessageAsync(
                xamlRoot,
                Loc.Get("MoveFiles_AbortedTitle"),
                Loc.Get("MoveFiles_Collisions", collisions.Count, shown));
            return;
        }

        // 3) bat をメモリ生成 → 内容を確認ダイアログで表示。
        var now = DateTime.Now;
        var timestamp = now.ToString("yyyyMMddHHmmss");
        var batText = vm.BuildFileMoveBatchText(
            dialog.DestinationPath, targets, now.ToString("yyyy-MM-dd HH:mm:ss"));

        var intro = Loc.Get("MoveFiles_ConfirmIntro", targets.Count, dialog.DestinationPath);
        if (!await ConfirmBatchAsync(xamlRoot, Loc.Get("MoveFiles_ConfirmTitle"), intro, batText))
            return;

        // 4) 宛先作成（既存は再利用）＋bat 保存＋実行（ログ出力）。
        var result = await vm.RunFileMoveBatchAsync(
            batText, dialog.DestinationPath, timestamp, targets.Count);

        var message = result.Success
            ? Loc.Get("MoveFiles_Done", result.TargetCount, result.LogPath)
            : Loc.Get("MoveFiles_DoneWithError", result.ExitCode, result.LogPath);
        await ShowMessageAsync(xamlRoot, Loc.Get("MoveFiles_DoneTitle"), message);
    }

    /// <summary>単純な通知ダイアログ（OK のみ）。</summary>
    public static async Task ShowMessageAsync(XamlRoot xamlRoot, string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "OK",
            XamlRoot = xamlRoot,
        };
        await dialog.ShowAsync();
    }

    /// <summary>続行/キャンセルを問う汎用確認ダイアログ。続行なら true。既定ボタンはキャンセル（安全側）。</summary>
    public static async Task<bool> ConfirmAsync(XamlRoot xamlRoot, string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = Loc.Get("Msg_Continue"),
            CloseButtonText = Loc.Get("Msg_Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>
    /// 対象が <see cref="BulkWarnThreshold"/> 枚以上なら確認ダイアログを挟んでから <paramref name="run"/> を実行する。
    /// キャンセル時は何もしない。少数なら即実行。右クリックメニュー・ハンバーガーメニュー・キー操作（Alt+E/Alt+S）の
    /// 大量対象操作（既定アプリで開く／共有／ファイルコピー）で共通に使う単一ソース。
    /// </summary>
    internal static async Task RunWithBulkWarningAsync(XamlRoot xamlRoot, int count, string messageKey, Action run)
    {
        if (count >= BulkWarnThreshold &&
            !await ConfirmAsync(xamlRoot, Loc.Get("BulkWarn_Title"), Loc.Get(messageKey, count)))
            return;
        run();
    }

    /// <summary>bat 内容を読み取り専用で見せ、実行/キャンセルを問う確認ダイアログ。実行なら true。</summary>
    public static async Task<bool> ConfirmBatchAsync(XamlRoot xamlRoot, string title, string intro, string batText)
    {
        var box = new TextBox
        {
            // AcceptsReturn は Text より先に true にする（既定 false のまま改行入りを代入すると 1 行目で切れる）。
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
        panel.Children.Add(new TextBlock { Text = intro, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(box);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = Loc.Get("Msg_Run"),
            CloseButtonText = Loc.Get("Msg_Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot,
        };
        // ContentDialog の既定幅クランプ（≈548）を外す。
        dialog.Resources["ContentDialogMaxWidth"] = 760.0;
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
