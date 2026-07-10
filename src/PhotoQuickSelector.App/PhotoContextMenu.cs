using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using PhotoQuickSelector.Core;
using PhotoQuickSelector_App.ViewModels;
using Windows.Foundation;

namespace PhotoQuickSelector_App;

/// <summary>
/// グリッド／フィルムストリップの右クリックメニュー（評価・ファイルコピー・外部連携・
/// Reject 移動／リネームコピー・全選択）を組み立てて表示する。グリッド（<see cref="Controls.PhotoGridView"/>）と
/// フィルムストリップ（<see cref="Controls.PreviewControl"/>）の双方から呼び、挙動を共通化する。
///
/// <para><b>対象確定（エクスプローラ流儀）</b>: 右クリックした写真が選択集合のメンバーなら集合全体が対象、
/// 集合外なら「その 1 枚を選び直して」単独対象にする（＝右クリックで選択が移る）。空白域では全選択のみ。</para>
/// </summary>
public static class PhotoContextMenu
{
    // 大量選択時の警告しきい値。エクスプローラの同種警告（15）より写真は重いので低めにしている。
    private const int BulkWarnThreshold = 10;

    /// <summary>
    /// <paramref name="clicked"/>（右クリックされた写真。空白域なら null）に応じてメニューを構築し、
    /// <paramref name="host"/> の <paramref name="position"/> に表示する。
    /// </summary>
    public static void Show(
        FrameworkElement host, Point position, PhotoItemViewModel? clicked,
        MainViewModel vm, XamlRoot xamlRoot)
    {
        var targets = ResolveTargets(vm, clicked, out var primary);
        var flyout = new MenuFlyout();

        // 対象が無い（空白域＋選択も焦点も無い）ときは全選択だけを出す。
        if (targets.Count == 0)
        {
            AddSelectAll(flyout, vm);
            if (flyout.Items.Count > 0)
                flyout.ShowAt(host, new FlyoutShowOptions { Position = position });
            return;
        }

        // 複数対象のときは先頭に「N 枚が対象」の見出し（無効項目）を置いて誤操作を防ぐ。
        if (targets.Count > 1)
        {
            flyout.Items.Add(new MenuFlyoutItem
            {
                Text = Loc.Get("Ctx_TargetCount", targets.Count),
                IsEnabled = false,
            });
            flyout.Items.Add(new MenuFlyoutSeparator());
        }

        // 選択集合全体を対象にする項目には末尾に「(全選択ファイル)」を付けて、1 枚だけの操作
        // （エクスプローラで表示）と見分けられるようにする。単一対象では付けない。
        string suffix = targets.Count > 1 ? " " + Loc.Get("Ctx_AllFilesSuffix") : "";

        // --- C: 評価（レーティング／カラーラベル／フラグ） ---
        AddRatingSub(flyout, vm, targets, suffix);
        AddColorSub(flyout, vm, targets, suffix);
        AddFlagSub(flyout, vm, targets, suffix);

        flyout.Items.Add(new MenuFlyoutSeparator());

        // --- B: ファイルをコピー（表示中のみ／関連ファイルも／リネームしてコピー）＋パスをコピー ---
        var copyFiles = new MenuFlyoutSubItem { Text = Loc.Get("Ctx_CopyFiles") + suffix };
        copyFiles.Items.Add(Item(Loc.Get("Ctx_CopyFiles_DisplayedOnly"),
            () => _ = RunWithBulkWarningAsync(xamlRoot, targets.Count, "BulkWarn_CopyMessage",
                () => _ = PhotoFileClipboard.CopyFilesAsync(targets, includeSiblings: false))));
        copyFiles.Items.Add(Item(Loc.Get("Ctx_CopyFiles_IncludeSiblings"),
            () => _ = RunWithBulkWarningAsync(xamlRoot, targets.Count, "BulkWarn_CopyMessage",
                () => _ = PhotoFileClipboard.CopyFilesAsync(targets, includeSiblings: true))));
        // 関連ファイルの下に区切り線を挟み、その下へ「リネームしてコピー」を配置する。
        copyFiles.Items.Add(new MenuFlyoutSeparator());
        copyFiles.Items.Add(Item(Loc.Get("Ctx_CopyRename"),
            () => _ = BatchFlows.RunCopyRenameAsync(vm, targets, xamlRoot)));
        flyout.Items.Add(copyFiles);

        flyout.Items.Add(Item(Loc.Get("Ctx_CopyPath") + suffix, () => PhotoFileCommands.CopyPaths(targets)));

        flyout.Items.Add(new MenuFlyoutSeparator());

        // --- A: 外部連携 ---
        // エクスプローラ /select は複数不可なので代表 1 枚（右クリックした写真）に限定（接尾辞なし）。
        var explorerTarget = primary;
        flyout.Items.Add(Item(Loc.Get("Ctx_Explorer"),
            () => { if (explorerTarget != null) PhotoFileCommands.OpenInExplorer(explorerTarget); },
            enabled: explorerTarget != null));
        flyout.Items.Add(Item(Loc.Get("Ctx_OpenDefault") + suffix,
            () => _ = RunWithBulkWarningAsync(xamlRoot, targets.Count, "BulkWarn_OpenMessage",
                () => PhotoFileCommands.OpenWithDefault(targets))));
        flyout.Items.Add(Item(Loc.Get("Ctx_Share") + suffix,
            () => _ = RunWithBulkWarningAsync(xamlRoot, targets.Count, "BulkWarn_ShareMessage",
                () => PhotoFileCommands.Share(targets, vm.Settings))));

        flyout.Items.Add(new MenuFlyoutSeparator());

        // --- E: すべて選択 ---
        AddSelectAll(flyout, vm);

        flyout.ShowAt(host, new FlyoutShowOptions { Position = position });
    }

    // === 対象確定（エクスプローラ流儀） ===

    /// <summary>
    /// 右クリック対象を確定する。<paramref name="primary"/> は外部連携など「1 枚だけ」を要する操作の代表。
    /// 集合外を右クリックしたときは、その写真を焦点にして集合を解除する（＝選択が移る）。
    /// </summary>
    private static IReadOnlyList<PhotoItemViewModel> ResolveTargets(
        MainViewModel vm, PhotoItemViewModel? clicked, out PhotoItemViewModel? primary)
    {
        if (clicked == null)
        {
            // 空白域: 既存の選択集合／焦点があればそれを対象にする（無ければ空）。
            primary = vm.FocusedPhoto;
            if (vm.SelectedPhotos.Count > 0) return vm.SelectedPhotos.ToList();
            return vm.FocusedPhoto != null
                ? new List<PhotoItemViewModel> { vm.FocusedPhoto }
                : Array.Empty<PhotoItemViewModel>();
        }

        // 集合メンバーを右クリック → 集合全体が対象（選択は保つ）。
        if (clicked.IsInSelection && vm.SelectedPhotos.Count > 0)
        {
            primary = clicked;
            return vm.SelectedPhotos.ToList();
        }

        // 集合外を右クリック → その 1 枚を選び直す（FocusedPhoto 代入が ClearSelection を誘発）。
        vm.FocusedPhoto = clicked;
        primary = clicked;
        return new List<PhotoItemViewModel> { clicked };
    }

    // === 各サブメニュー ===

    private static void AddRatingSub(
        MenuFlyout flyout, MainViewModel vm, IReadOnlyList<PhotoItemViewModel> targets, string suffix)
    {
        var sub = new MenuFlyoutSubItem { Text = Loc.Get("Ctx_Rating") + suffix };
        sub.Items.Add(Item(Loc.Get("Ctx_RatingClear"),
            () => Apply(vm, i => i.SetRating(0), targets)));
        for (int n = 1; n <= 5; n++)
        {
            int value = n; // クロージャ捕捉用
            sub.Items.Add(Item(new string('★', value),
                () => Apply(vm, i => i.SetRating(value), targets)));
        }
        flyout.Items.Add(sub);
    }

    private static void AddColorSub(
        MenuFlyout flyout, MainViewModel vm, IReadOnlyList<PhotoItemViewModel> targets, string suffix)
    {
        var sub = new MenuFlyoutSubItem { Text = Loc.Get("Ctx_ColorLabel") + suffix };
        AddColor(sub, vm, targets, ColorLabel.Red, "Ctx_Color_Red");
        AddColor(sub, vm, targets, ColorLabel.Yellow, "Ctx_Color_Yellow");
        AddColor(sub, vm, targets, ColorLabel.Green, "Ctx_Color_Green");
        AddColor(sub, vm, targets, ColorLabel.Blue, "Ctx_Color_Blue");
        AddColor(sub, vm, targets, ColorLabel.Purple, "Ctx_Color_Purple");
        flyout.Items.Add(sub);
    }

    private static void AddColor(
        MenuFlyoutSubItem sub, MainViewModel vm, IReadOnlyList<PhotoItemViewModel> targets,
        ColorLabel label, string locKey)
    {
        sub.Items.Add(Item(Loc.Get(locKey), () => Apply(vm, i => i.ToggleColorLabel(label), targets)));
    }

    private static void AddFlagSub(
        MenuFlyout flyout, MainViewModel vm, IReadOnlyList<PhotoItemViewModel> targets, string suffix)
    {
        var sub = new MenuFlyoutSubItem { Text = Loc.Get("Ctx_Flag") + suffix };
        sub.Items.Add(Item(Loc.Get("Ctx_Flag_Pick"), () => Apply(vm, i => i.SetFlag(1), targets)));
        sub.Items.Add(Item(Loc.Get("Ctx_Flag_None"), () => Apply(vm, i => i.SetFlag(0), targets)));
        sub.Items.Add(Item(Loc.Get("Ctx_Flag_Reject"), () => Apply(vm, i => i.SetFlag(-1), targets)));
        flyout.Items.Add(sub);
    }

    private static void AddSelectAll(MenuFlyout flyout, MainViewModel vm)
    {
        flyout.Items.Add(Item(Loc.Get("Ctx_SelectAll"), vm.SelectAll, enabled: vm.Photos.Count > 0));
    }

    // === 共通ヘルパ ===

    /// <summary>
    /// 対象が <see cref="BulkWarnThreshold"/> 枚以上なら確認ダイアログを挟んでから <paramref name="run"/> を実行する。
    /// キャンセル時は何もしない。少数なら即実行。
    /// </summary>
    private static async Task RunWithBulkWarningAsync(
        XamlRoot xamlRoot, int count, string messageKey, Action run)
    {
        if (count >= BulkWarnThreshold &&
            !await BatchFlows.ConfirmAsync(
                xamlRoot, Loc.Get("BulkWarn_Title"), Loc.Get(messageKey, count)))
            return;
        run();
    }

    /// <summary>評価操作を対象へ適用する（sqlite 未作成なら作成確認を挟む＝ApplyEvaluationAsync 経由）。</summary>
    private static void Apply(
        MainViewModel vm, Action<PhotoItemViewModel> op, IReadOnlyList<PhotoItemViewModel> targets)
        => _ = vm.ApplyEvaluationAsync(op, targets);

    /// <summary>クリックで <paramref name="onClick"/> を実行する <see cref="MenuFlyoutItem"/> を作る。</summary>
    private static MenuFlyoutItem Item(string text, Action onClick, bool enabled = true)
    {
        var item = new MenuFlyoutItem { Text = text, IsEnabled = enabled };
        item.Click += (_, _) => onClick();
        return item;
    }
}
