using Microsoft.UI.Xaml;
using Windows.System;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// プレビューのキー処理。ナビ（←/→）/ ズーム（Z）/ スクロール（Alt+矢印）/ ルーペ（Ctrl+Alt+…）/
/// グリッド（G）/ メタ情報（I）/ キャッシュ一覧（C）/ 評価キーを束ねる。Window 直下のルート集約ハンドラから呼ばれる。
/// </summary>
public sealed partial class PreviewControl
{
    /// <summary>Alt+矢印スクロール 1 回あたりの移動量（DIP）。</summary>
    private const double PanStep = 120;

    /// <summary>
    /// プレビューのキー操作（ナビ / ズーム / スクロール / 評価）を処理する。処理したら true。
    /// Window 直下のルート集約ハンドラ（<see cref="MainPage.HandleGlobalKeyDown"/>）から呼ばれる。
    /// 画像クリックでフォーカスがキャンバスから外れていても効くよう、フォーカス非依存で実行する。
    /// </summary>
    public bool HandleKeyDown(VirtualKey key)
    {
        if (_viewModel == null) return false;

        bool alt = KeyboardModifiers.Alt;
        bool ctrl = KeyboardModifiers.Ctrl;

        // Ctrl+Alt+矢印 : 右上ズームプレビュー（ルーペ）をスクロール / Ctrl+Alt+F : 同・フォーカス点へ
        if (ctrl && alt)
        {
            switch (key)
            {
                case VirtualKey.Left: ZoomPanByRatio(0.25, 0); return true;
                case VirtualKey.Right: ZoomPanByRatio(-0.25, 0); return true;
                case VirtualKey.Up: ZoomPanByRatio(0, 0.25); return true;
                case VirtualKey.Down: ZoomPanByRatio(0, -0.25); return true;
                case VirtualKey.F: ScrollZoomToFocus(); return true;
            }
        }

        // Shift+Alt+←/→ : フィット / 100%（SPEC §3-7）
        if (alt && KeyboardModifiers.Shift)
        {
            switch (key)
            {
                case VirtualKey.Left: _viewport.SetFit(); InvalidateMain(); return true;
                case VirtualKey.Right: _viewport.SetActualSize(); InvalidateMain(); return true;
            }
        }

        // Alt+矢印 : ズーム画像をスクロール（パン） / Alt+F : フォーカス点へスクロール
        if (alt && !ctrl)
        {
            switch (key)
            {
                case VirtualKey.Left: _viewport.Pan(PanStep, 0); InvalidateMain(); return true;
                case VirtualKey.Right: _viewport.Pan(-PanStep, 0); InvalidateMain(); return true;
                case VirtualKey.Up: _viewport.Pan(0, PanStep); InvalidateMain(); return true;
                case VirtualKey.Down: _viewport.Pan(0, -PanStep); InvalidateMain(); return true;
                case VirtualKey.F: ScrollToFocus(); return true;
            }
        }

        // 修飾子なしの ←/→ : 前後移動
        if (KeyboardModifiers.None)
        {
            switch (key)
            {
                case VirtualKey.Left: _viewModel.MovePrevious(); return true;
                case VirtualKey.Right: _viewModel.MoveNext(); return true;
            }
        }

        // Esc は KeyboardAccelerator 側で処理（KeyDown には届かないため）。
        // Z : フィット ⇄ 100% トグル / Shift+Z : 100%
        if (key == VirtualKey.Z)
        {
            if (KeyboardModifiers.Shift) _viewport.SetActualSize();
            else _viewport.ToggleZoom();
            InvalidateMain();
            return true;
        }

        // G : 三分割グリッド線トグル（ShowGrid 変更で再描画される）
        if (KeyboardModifiers.None && key == VirtualKey.G)
        {
            _viewModel.ShowGrid = !_viewModel.ShowGrid;
            return true;
        }

        // I : メタ情報オーバーレイ（案B）トグル
        if (KeyboardModifiers.None && key == VirtualKey.I)
        {
            _viewModel.ShowInfoOverlay = !_viewModel.ShowInfoOverlay;
            return true;
        }

        // C : 先読みキャッシュ内容のデバッグオーバーレイ（右上）をトグル
        if (KeyboardModifiers.None && key == VirtualKey.C)
        {
            bool show = CacheOverlay.Visibility == Visibility.Collapsed;
            CacheOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show) RefreshCacheOverlay();
            return true;
        }

        // 評価キー（rating / flag / colorlabel）はサムネイル一覧と共通化（SPEC §3-7）。
        if (_viewModel.SelectedPhoto is { } photo && PhotoKeyCommands.TryHandleEvaluation(key, photo))
            return true;

        return false;
    }
}
