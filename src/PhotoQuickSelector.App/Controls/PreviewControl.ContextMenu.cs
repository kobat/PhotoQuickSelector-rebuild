using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using PhotoQuickSelector_App.ViewModels;
using Windows.Foundation;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// メイン大画面（<c>MainCanvas</c>）の右クリックメニュー（ズーム／表示切替／評価／ファイル連携）。
/// <b>対象は常に焦点の1枚（<see cref="MainViewModel.FocusedPhoto"/>）に固定する</b>。フィルムストリップ／
/// グリッドの右クリックメニュー（<see cref="PhotoContextMenu"/>）と異なり、選択集合
/// （<see cref="MainViewModel.SelectedPhotos"/>）は参照も変更もしない（エクスプローラ流儀の対象付け替えは行わない）。
/// メニューは開くたび使い捨てで組み立てる（既存 <see cref="PhotoContextMenu"/> と同じ流儀）。
/// </summary>
public sealed partial class PreviewControl
{
    /// <summary>全画面表示の切替要求（<see cref="MainWindow.ToggleFullScreen"/> を呼ぶ）。</summary>
    public event EventHandler? ToggleFullScreenRequested;

    /// <summary>完全全画面モードの切替要求（<see cref="MainPage.ToggleFullImageMode"/> を呼ぶ）。</summary>
    public event EventHandler? ToggleFullImageRequested;

    /// <summary>メニューを開いた時点の全画面状態の照会（<see cref="MainPage"/> が <see cref="MainWindow"/> から供給）。</summary>
    public Func<bool>? IsFullScreenProvider { get; set; }

    private void MainCanvas_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (_viewModel == null) return;

        // 右クリック位置をズームの中心として各項目のクロージャへ渡す（ホイール/ダブルクリックと同じ流儀）。
        var pos = e.GetPosition(MainCanvas);
        var flyout = BuildContextMenu(pos);
        flyout.ShowAt(MainCanvas, new FlyoutShowOptions { Position = pos });
        e.Handled = true;
    }

    /// <summary>メニュー本体を構築する。<paramref name="pos"/> はキャンバス座標（ズーム系項目の中心基準）。</summary>
    private MenuFlyout BuildContextMenu(Point pos)
    {
        var vm = _viewModel!;
        var flyout = new MenuFlyout();

        // --- ズーム ---
        var zoomSub = new MenuFlyoutSubItem { Text = Loc.Get("PvCtx_Zoom") };
        zoomSub.Items.Add(RadioItem(Loc.Get("PvCtx_ZoomFit"), "PvZoom",
            _viewport.Mode == ZoomMode.Fit, "Shift+Alt+←",
            () => { _viewport.SetFit(); InvalidateMain(); }));
        zoomSub.Items.Add(RadioItem("100%", "PvZoom",
            IsAtDeviceScale(1.0), "Shift+Z",
            () => { _viewport.SetActualSizeAround(pos.X, pos.Y); InvalidateMain(); }));
        zoomSub.Items.Add(PhotoContextMenu.Item(Loc.Get("PvCtx_ZoomOut"),
            () => { _viewport.ZoomToStop(false, pos.X, pos.Y); InvalidateMain(); },
            accelText: "-"));
        zoomSub.Items.Add(PhotoContextMenu.Item(Loc.Get("PvCtx_ZoomIn"),
            () => { _viewport.ZoomToStop(true, pos.X, pos.Y); InvalidateMain(); },
            accelText: "+"));
        zoomSub.Items.Add(new MenuFlyoutSeparator());

        var zoomStopsSub = new MenuFlyoutSubItem { Text = Loc.Get("PvCtx_ZoomStops") };
        foreach (var s in _viewport.ZoomStops)
        {
            double stop = s; // クロージャ捕捉用
            zoomStopsSub.Items.Add(RadioItem($"{stop * 100:0.#}%", "PvZoom",
                IsAtDeviceScale(stop), accelText: null,
                onClick: () =>
                {
                    if (Math.Abs(stop - 1.0) < 1e-9) _viewport.SetActualSizeAround(pos.X, pos.Y);
                    else _viewport.SetDeviceScaleAround(stop, pos.X, pos.Y);
                    InvalidateMain();
                }));
        }
        zoomSub.Items.Add(zoomStopsSub);
        flyout.Items.Add(zoomSub);

        flyout.Items.Add(new MenuFlyoutSeparator());

        // --- 表示切替（イマーシブ／全画面／完全全画面） ---
        flyout.Items.Add(ToggleItem(Loc.Get("PvCtx_Immersive"), IsImmersive, "F", ToggleImmersive));
        flyout.Items.Add(ToggleItem(Loc.Get("PvCtx_FullScreen"), IsFullScreenProvider?.Invoke() ?? false, "F11",
            () => ToggleFullScreenRequested?.Invoke(this, EventArgs.Empty)));
        flyout.Items.Add(PhotoContextMenu.Item(Loc.Get("PvCtx_FullImage"),
            () => ToggleFullImageRequested?.Invoke(this, EventArgs.Empty), accelText: "Shift+F"));

        flyout.Items.Add(new MenuFlyoutSeparator());

        // --- EXIF 詳細パネル ---
        flyout.Items.Add(ToggleItem(Loc.Get("PvCtx_ExifPanel"), _showExifPanel, "E", ToggleExifPanel));

        // --- 情報オーバーレイ（種類×表示タイミングの2軸） ---
        var overlaySub = new MenuFlyoutSubItem { Text = Loc.Get("PvCtx_Overlay") };
        overlaySub.Items.Add(RadioItem(Loc.Get("PvCtx_OverlayBadge"), "PvOverlayKind",
            vm.OverlayKind == InfoOverlayKind.Badge, "I", () => vm.OverlayKind = InfoOverlayKind.Badge));
        overlaySub.Items.Add(RadioItem(Loc.Get("PvCtx_OverlayFull"), "PvOverlayKind",
            vm.OverlayKind == InfoOverlayKind.Full, "I", () => vm.OverlayKind = InfoOverlayKind.Full));
        overlaySub.Items.Add(RadioItem(Loc.Get("PvCtx_OverlayOff"), "PvOverlayKind",
            vm.OverlayKind == InfoOverlayKind.Off, "I", () => vm.OverlayKind = InfoOverlayKind.Off));
        overlaySub.Items.Add(new MenuFlyoutSeparator());
        overlaySub.Items.Add(RadioItem(Loc.Get("PvCtx_OverlayAlways"), "PvOverlayTiming",
            !vm.OverlayTransient, "Shift+I", () => vm.OverlayTransient = false));
        overlaySub.Items.Add(RadioItem(Loc.Get("PvCtx_OverlayTransient"), "PvOverlayTiming",
            vm.OverlayTransient, "Shift+I", () => vm.OverlayTransient = true));
        flyout.Items.Add(overlaySub);

        // --- 構図グリッド（種類×基準の2軸） ---
        var gridSub = new MenuFlyoutSubItem { Text = Loc.Get("PvCtx_Grid") };
        gridSub.Items.Add(RadioItem(Loc.Get("PvCtx_GridNone"), "PvGridKind",
            vm.GridKind == GridOverlayKind.None, "G", () => vm.GridKind = GridOverlayKind.None));
        gridSub.Items.Add(RadioItem(Loc.Get("PvCtx_GridCross"), "PvGridKind",
            vm.GridKind == GridOverlayKind.CenterCross, "G", () => vm.GridKind = GridOverlayKind.CenterCross));
        gridSub.Items.Add(RadioItem(Loc.Get("PvCtx_GridThirds"), "PvGridKind",
            vm.GridKind == GridOverlayKind.RuleOfThirds, "G", () => vm.GridKind = GridOverlayKind.RuleOfThirds));
        gridSub.Items.Add(RadioItem(Loc.Get("PvCtx_GridSquare"), "PvGridKind",
            vm.GridKind == GridOverlayKind.Square, "G", () => vm.GridKind = GridOverlayKind.Square));
        gridSub.Items.Add(new MenuFlyoutSeparator());
        gridSub.Items.Add(RadioItem(Loc.Get("PvCtx_GridRefImage"), "PvGridRef",
            vm.GridReference == GridOverlayReference.Image, "Shift+G",
            () => vm.GridReference = GridOverlayReference.Image));
        gridSub.Items.Add(RadioItem(Loc.Get("PvCtx_GridRefCanvas"), "PvGridRef",
            vm.GridReference == GridOverlayReference.Canvas, "Shift+G",
            () => vm.GridReference = GridOverlayReference.Canvas));
        flyout.Items.Add(gridSub);

        // --- 評価／ファイル連携（焦点の1枚のみが対象。選択集合は不問） ---
        if (vm.FocusedPhoto is { } photo)
        {
            flyout.Items.Add(new MenuFlyoutSeparator());

            var targets = new List<PhotoItemViewModel> { photo };
            PhotoContextMenu.AddFlagSub(flyout.Items, vm, targets, suffix: "");
            PhotoContextMenu.AddRatingSub(flyout.Items, vm, targets, suffix: "");
            PhotoContextMenu.AddColorSub(flyout.Items, vm, targets, suffix: "");

            flyout.Items.Add(new MenuFlyoutSeparator());

            PhotoContextMenu.AddFileItems(flyout.Items, vm, targets, primary: photo, suffix: "",
                XamlRoot, withAcceleratorText: true);
        }

        return flyout;
    }

    /// <summary>
    /// 表示倍率（<see cref="PreviewViewport.DeviceScale"/>）が指定段 <paramref name="deviceScale"/> に
    /// 一致しているか（Radio 項目の IsChecked 判定用）。フィット中は「どの固定段でもない」ので常に false。
    /// 相対誤差 1e-3 未満を「一致」とみなす（浮動小数の丸め誤差を吸収）。
    /// </summary>
    private bool IsAtDeviceScale(double deviceScale) =>
        _viewport.Mode != ZoomMode.Fit &&
        Math.Abs(_viewport.DeviceScale - deviceScale) < deviceScale * 1e-3;

    /// <summary>
    /// クリックで <paramref name="onClick"/> を実行する <see cref="RadioMenuFlyoutItem"/> を作る。
    /// <paramref name="accelText"/> は表示専用ヒント（<c>KeyboardAcceleratorTextOverride</c>）。
    /// </summary>
    private static RadioMenuFlyoutItem RadioItem(
        string text, string groupName, bool isChecked, string? accelText, Action onClick)
    {
        var item = new RadioMenuFlyoutItem { Text = text, GroupName = groupName, IsChecked = isChecked };
        if (accelText != null) item.KeyboardAcceleratorTextOverride = accelText;
        item.Click += (_, _) => onClick();
        return item;
    }

    /// <summary>クリックで <paramref name="onClick"/> を実行する <see cref="ToggleMenuFlyoutItem"/> を作る。</summary>
    private static ToggleMenuFlyoutItem ToggleItem(
        string text, bool isChecked, string accelText, Action onClick)
    {
        var item = new ToggleMenuFlyoutItem
        {
            Text = text, IsChecked = isChecked, KeyboardAcceleratorTextOverride = accelText,
        };
        item.Click += (_, _) => onClick();
        return item;
    }
}
