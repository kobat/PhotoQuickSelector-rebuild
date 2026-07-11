using System;
using System.Collections.Generic;
using System.Linq;
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

        // --- C: 評価（フラグ／レーティング／カラーラベル。並びは全表示箇所で統一） ---
        AddFlagSub(flyout.Items, vm, targets, suffix);
        AddRatingSub(flyout.Items, vm, targets, suffix);
        AddColorSub(flyout.Items, vm, targets, suffix);

        flyout.Items.Add(new MenuFlyoutSeparator());

        // --- A/B: ファイル関連（ファイルをコピー／パスをコピー／エクスプローラ表示／既定アプリ／共有） ---
        // ハンバーガー「ファイル」（PhotoStatusBar）と実体を共有する。ショートカット表示はメイン画像メニューと
        // 統一するため true（実キー処理は別経路なので表示専用）。
        AddFileItems(flyout.Items, vm, targets, primary, suffix, xamlRoot, withAcceleratorText: true);

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

    /// <summary>
    /// 右クリック以外（ハンバーガーメニュー・キーボードショートカット）から対象を確定する。
    /// 選択集合があれば集合全体、無ければ焦点の1枚（どちらも無ければ空）。選択状態は変更しない
    /// （<see cref="ResolveTargets"/> と異なり <c>clicked</c> が無いので集合の付け替えは起きない）。
    /// </summary>
    internal static IReadOnlyList<PhotoItemViewModel> ResolveCurrentTargets(
        MainViewModel vm, out PhotoItemViewModel? primary)
        => ResolveTargets(vm, clicked: null, out primary);

    // === 各サブメニュー ===
    // 以下 3 メソッドは internal 化して <see cref="Controls.PreviewControl"/> の右クリックメニュー
    // （メイン画像・対象は焦点の1枚のみ）とも共有する。第1引数は挿入先のアイテムコレクション
    // （<see cref="MenuFlyout.Items"/> でも <see cref="MenuFlyoutSubItem.Items"/> でもよい）。

    /// <summary>
    /// レーティングのサブメニューを構築する。<paramref name="targets"/> が単一対象のときだけ現在値を
    /// <see cref="RadioMenuFlyoutItem"/>（排他6値）のチェックで示す。複数対象時は従来どおりチェックなし
    /// （どれが「現在値」か一意に定まらないため）。
    /// </summary>
    internal static void AddRatingSub(
        IList<MenuFlyoutItemBase> items, MainViewModel vm, IReadOnlyList<PhotoItemViewModel> targets, string suffix)
    {
        var sub = new MenuFlyoutSubItem { Text = Loc.Get("Ctx_Rating") + suffix };
        int? current = targets.Count == 1 ? targets[0].Eval.Rating : null;

        if (current.HasValue)
            sub.Items.Add(RadioItem(Loc.Get("Ctx_RatingClear"), "CtxRating", current.Value == 0,
                () => Apply(vm, i => i.SetRating(0), targets)));
        else
            sub.Items.Add(Item(Loc.Get("Ctx_RatingClear"), () => Apply(vm, i => i.SetRating(0), targets)));

        for (int n = 1; n <= 5; n++)
        {
            int value = n; // クロージャ捕捉用
            if (current.HasValue)
                sub.Items.Add(RadioItem(new string('★', value), "CtxRating", current.Value == value,
                    () => Apply(vm, i => i.SetRating(value), targets)));
            else
                sub.Items.Add(Item(new string('★', value),
                    () => Apply(vm, i => i.SetRating(value), targets)));
        }
        items.Add(sub);
    }

    /// <summary>
    /// カラーラベルのサブメニューを構築する。先頭に全色解除の「クリア」を置き、区切り線の下に5色を並べる。
    /// <paramref name="targets"/> が単一対象のときだけ各色を <see cref="ToggleMenuFlyoutItem"/> にして
    /// 現在の付与有無をチェックで示す（クリック時の動作はどちらもトグル維持で変わらない）。
    /// </summary>
    internal static void AddColorSub(
        IList<MenuFlyoutItemBase> items, MainViewModel vm, IReadOnlyList<PhotoItemViewModel> targets, string suffix)
    {
        var sub = new MenuFlyoutSubItem { Text = Loc.Get("Ctx_ColorLabel") + suffix };
        sub.Items.Add(Item(Loc.Get("Ctx_ColorClear"), () => Apply(vm, i => i.ClearColorLabels(), targets)));
        sub.Items.Add(new MenuFlyoutSeparator());
        AddColor(sub, vm, targets, ColorLabel.Red, "Ctx_Color_Red");
        AddColor(sub, vm, targets, ColorLabel.Yellow, "Ctx_Color_Yellow");
        AddColor(sub, vm, targets, ColorLabel.Green, "Ctx_Color_Green");
        AddColor(sub, vm, targets, ColorLabel.Blue, "Ctx_Color_Blue");
        AddColor(sub, vm, targets, ColorLabel.Purple, "Ctx_Color_Purple");
        items.Add(sub);
    }

    private static void AddColor(
        MenuFlyoutSubItem sub, MainViewModel vm, IReadOnlyList<PhotoItemViewModel> targets,
        ColorLabel label, string locKey)
    {
        if (targets.Count == 1)
            sub.Items.Add(ToggleItem(Loc.Get(locKey), targets[0].Eval.HasColorLabel(label),
                () => Apply(vm, i => i.ToggleColorLabel(label), targets)));
        else
            sub.Items.Add(Item(Loc.Get(locKey), () => Apply(vm, i => i.ToggleColorLabel(label), targets)));
    }

    /// <summary>
    /// フラグのサブメニューを構築する。<paramref name="targets"/> が単一対象のときだけ現在値を
    /// <see cref="RadioMenuFlyoutItem"/>（排他3値）のチェックで示す。
    /// </summary>
    internal static void AddFlagSub(
        IList<MenuFlyoutItemBase> items, MainViewModel vm, IReadOnlyList<PhotoItemViewModel> targets, string suffix)
    {
        var sub = new MenuFlyoutSubItem { Text = Loc.Get("Ctx_Flag") + suffix };
        if (targets.Count == 1)
        {
            int flag = targets[0].Eval.FlagRating;
            sub.Items.Add(RadioItem(Loc.Get("Ctx_Flag_Pick"), "CtxFlag", flag > 0,
                () => Apply(vm, i => i.SetFlag(1), targets)));
            sub.Items.Add(RadioItem(Loc.Get("Ctx_Flag_None"), "CtxFlag", flag == 0,
                () => Apply(vm, i => i.SetFlag(0), targets)));
            sub.Items.Add(RadioItem(Loc.Get("Ctx_Flag_Reject"), "CtxFlag", flag < 0,
                () => Apply(vm, i => i.SetFlag(-1), targets)));
        }
        else
        {
            sub.Items.Add(Item(Loc.Get("Ctx_Flag_Pick"), () => Apply(vm, i => i.SetFlag(1), targets)));
            sub.Items.Add(Item(Loc.Get("Ctx_Flag_None"), () => Apply(vm, i => i.SetFlag(0), targets)));
            sub.Items.Add(Item(Loc.Get("Ctx_Flag_Reject"), () => Apply(vm, i => i.SetFlag(-1), targets)));
        }
        items.Add(sub);
    }

    private static void AddSelectAll(MenuFlyout flyout, MainViewModel vm)
    {
        flyout.Items.Add(Item(Loc.Get("Ctx_SelectAll"), vm.SelectAll, enabled: vm.Photos.Count > 0,
            accelText: "Ctrl+A"));
    }

    /// <summary>
    /// ファイル関連項目（ファイルをコピー▶／パスをコピー／エクスプローラーで表示／既定のアプリで開く／共有）を
    /// <paramref name="items"/> へ追加する。右クリックメニュー（<see cref="Show"/>）とハンバーガー「ファイル」
    /// （<see cref="Controls.PhotoStatusBar"/>）の共通実体。大量対象（<see cref="BatchFlows.BulkWarnThreshold"/> 枚以上）の
    /// 「既定のアプリで開く」「共有」「ファイルをコピー」には確認ダイアログを挟む（パスをコピーは軽微なので挟まない）。
    /// </summary>
    /// <param name="withAcceleratorText">
    /// true でパスをコピー/エクスプローラーで表示/既定のアプリで開く/共有へ <c>KeyboardAcceleratorTextOverride</c>
    /// を付ける（ハンバーガーメニュー用。表示専用＝実キー処理は <see cref="PhotoFileCommands.TryHandle"/> 側）。
    /// </param>
    internal static void AddFileItems(
        IList<MenuFlyoutItemBase> items, MainViewModel vm, IReadOnlyList<PhotoItemViewModel> targets,
        PhotoItemViewModel? primary, string suffix, XamlRoot xamlRoot, bool withAcceleratorText)
    {
        string? Accel(string text) => withAcceleratorText ? text : null;

        // --- B: ファイルをコピー（表示中のみ／関連ファイルも／リネームしてコピー）＋パスをコピー ---
        var copyFiles = new MenuFlyoutSubItem { Text = Loc.Get("Ctx_CopyFiles") + suffix };
        copyFiles.Items.Add(Item(Loc.Get("Ctx_CopyFiles_DisplayedOnly"),
            () => _ = BatchFlows.RunWithBulkWarningAsync(xamlRoot, targets.Count, "BulkWarn_CopyMessage",
                () => _ = PhotoFileClipboard.CopyFilesAsync(targets, includeSiblings: false))));
        copyFiles.Items.Add(Item(Loc.Get("Ctx_CopyFiles_IncludeSiblings"),
            () => _ = BatchFlows.RunWithBulkWarningAsync(xamlRoot, targets.Count, "BulkWarn_CopyMessage",
                () => _ = PhotoFileClipboard.CopyFilesAsync(targets, includeSiblings: true))));
        // 関連ファイルの下に区切り線を挟み、その下へ「リネームしてコピー」を配置する。
        copyFiles.Items.Add(new MenuFlyoutSeparator());
        copyFiles.Items.Add(Item(Loc.Get("Ctx_CopyRename"),
            () => _ = BatchFlows.RunCopyRenameAsync(vm, targets, xamlRoot)));
        items.Add(copyFiles);

        items.Add(Item(Loc.Get("Ctx_CopyPath") + suffix, () => PhotoFileCommands.CopyPaths(targets),
            accelText: Accel("Ctrl+Alt+E")));

        items.Add(new MenuFlyoutSeparator());

        // --- A: 外部連携 ---
        // エクスプローラ /select は複数不可なので代表 1 枚（primary）に限定（接尾辞なし）。
        items.Add(Item(Loc.Get("Ctx_Explorer"),
            () => { if (primary != null) PhotoFileCommands.OpenInExplorer(primary); },
            enabled: primary != null, accelText: Accel("Ctrl+E")));
        items.Add(Item(Loc.Get("Ctx_OpenDefault") + suffix,
            () => _ = BatchFlows.RunWithBulkWarningAsync(xamlRoot, targets.Count, "BulkWarn_OpenMessage",
                () => PhotoFileCommands.OpenWithDefault(targets)),
            accelText: Accel("Alt+E")));
        items.Add(Item(Loc.Get("Ctx_Share") + suffix,
            () => _ = BatchFlows.RunWithBulkWarningAsync(xamlRoot, targets.Count, "BulkWarn_ShareMessage",
                () => PhotoFileCommands.Share(targets, vm.Settings)),
            accelText: Accel("Alt+S")));
    }

    // === 共通ヘルパ ===

    /// <summary>評価操作を対象へ適用する（sqlite 未作成なら作成確認を挟む＝ApplyEvaluationAsync 経由）。</summary>
    private static void Apply(
        MainViewModel vm, Action<PhotoItemViewModel> op, IReadOnlyList<PhotoItemViewModel> targets)
        => _ = vm.ApplyEvaluationAsync(op, targets);

    /// <summary>クリックで <paramref name="onClick"/> を実行する <see cref="MenuFlyoutItem"/> を作る。</summary>
    internal static MenuFlyoutItem Item(string text, Action onClick, bool enabled = true, string? accelText = null)
    {
        var item = new MenuFlyoutItem { Text = text, IsEnabled = enabled };
        if (accelText != null) item.KeyboardAcceleratorTextOverride = accelText;
        item.Click += (_, _) => onClick();
        return item;
    }

    /// <summary>
    /// クリックで <paramref name="onClick"/> を実行する <see cref="RadioMenuFlyoutItem"/> を作る
    /// （単一対象時の評価サブメニューの現在値表示用。排他グループは <paramref name="groupName"/>）。
    /// </summary>
    private static RadioMenuFlyoutItem RadioItem(string text, string groupName, bool isChecked, Action onClick)
    {
        var item = new RadioMenuFlyoutItem { Text = text, GroupName = groupName, IsChecked = isChecked };
        item.Click += (_, _) => onClick();
        return item;
    }

    /// <summary>
    /// クリックで <paramref name="onClick"/> を実行する <see cref="ToggleMenuFlyoutItem"/> を作る
    /// （単一対象時のカラーラベル表示用。複数同時保持があるため排他ではなくトグル）。
    /// </summary>
    private static ToggleMenuFlyoutItem ToggleItem(string text, bool isChecked, Action onClick)
    {
        var item = new ToggleMenuFlyoutItem { Text = text, IsChecked = isChecked };
        item.Click += (_, _) => onClick();
        return item;
    }
}
