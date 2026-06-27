using System.Linq;
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

    // ズームイン/アウトのメイン段キー（OEM_PLUS/OEM_MINUS）。
    // VK_OEM_PLUS/MINUS は「いかなる国/地域でも『＋』『−』キー」と定義された配列非依存の OEM ペアで、
    // US（=/-）でも JIS（;/-）でも印字どおりの +/- 物理キーに対応する（ブラケット系と違い安全）。
    private const VirtualKey OemPlus = (VirtualKey)187;   // メイン段 + キー
    private const VirtualKey OemMinus = (VirtualKey)189;  // メイン段 - キー

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
            // 選択集合があるときは Ctrl+Alt+↑/↓ を一括フラグへ振り分ける（無いときは従来のルーペ縦スクロール）。
            if (_viewModel.SelectedPhotos.Count > 0 &&
                (key == VirtualKey.Up || key == VirtualKey.Down) &&
                PhotoKeyCommands.ResolveBulkEvaluation(key) is { } flagOp)
            {
                _ = _viewModel.ApplyEvaluationAsync(flagOp, _viewModel.SelectedPhotos.ToList());
                return true;
            }
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

        // 複数選択キー（Shift+←/→ / Ctrl+←/→ / Ctrl+Space / Esc）。Alt 併用は上で処理済み。
        if (SelectionKeyCommands.TryHandle(key, _viewModel))
        {
            FocusFilmStripSelected();
            return true;
        }

        // 修飾子なしの ←/→ : 前後移動（選択集合があればメンバー内で巡回）。移動後はフォーカスを
        // フィルムストリップへ移し、PageUp/PageDown/Home/End 等の ListView キー操作が効くようにする。
        if (KeyboardModifiers.None)
        {
            switch (key)
            {
                case VirtualKey.Left: _viewModel.MovePrevious(); FocusFilmStripSelected(); return true;
                case VirtualKey.Right: _viewModel.MoveNext(); FocusFilmStripSelected(); return true;
            }
        }

        // +/- : 段ズーム（イン/アウト）。テンキー（Add/Subtract）とメイン段（OEM_PLUS/MINUS）の両方を受ける。
        // Shift は不問（US の素押し = / JIS の素押し ; でもズームイン）。Ctrl/Alt 併用時は他機能優先で対象外。
        if (!ctrl && !alt)
        {
            switch (key)
            {
                case VirtualKey.Add:
                case OemPlus:
                    ZoomStepFromCenter(zoomIn: true);
                    return true;
                case VirtualKey.Subtract:
                case OemMinus:
                    ZoomStepFromCenter(zoomIn: false);
                    return true;
            }
        }

        // Esc ではプレビューを抜けない（ユーザー要望。終了はダブルクリック）。全画面中の Esc は MainWindow 側。
        // Z : フィット ⇄ 直近ズーム位置トグル / Shift+Z : 100%
        if (key == VirtualKey.Z)
        {
            if (KeyboardModifiers.Shift) _viewport.SetActualSize();
            else _viewport.ToggleZoom();
            InvalidateMain();
            return true;
        }

        // F : イマーシブ表示トグル（右パネル＋フィルムストリップを畳んでメインを全域表示）。
        // Alt+F / Ctrl+Alt+F（フォーカス点へスクロール）は上で処理済みなので、ここは修飾子なしのみ。
        if (KeyboardModifiers.None && key == VirtualKey.F)
        {
            ToggleImmersive();
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

        // 一括評価（Alt+数字）: 選択集合の全メンバーへ。集合が無ければ Alt+数字 は無効（消費のみ）。
        if (PhotoKeyCommands.ResolveBulkEvaluation(key) is { } bulkOp)
        {
            if (_viewModel.SelectedPhotos.Count > 0)
                _ = _viewModel.ApplyEvaluationAsync(bulkOp, _viewModel.SelectedPhotos.ToList());
            return true;
        }

        // 外部連携（Ctrl+E / Alt+E / Ctrl+Alt+E / Alt+S）／単一評価（焦点1枚。rating / flag / colorlabel）は
        // サムネイル一覧と共通化（SPEC §3-7 / §3-8）。
        if (_viewModel.FocusedPhoto is { } photo)
        {
            if (PhotoFileCommands.TryHandle(key, photo, _viewModel.Settings))
                return true;
            if (PhotoKeyCommands.ResolveEvaluation(key) is { } op)
            {
                // 初回はファイル作成確認ダイアログを挟むため非同期 gate 経由（待たない）。
                _ = _viewModel.ApplyEvaluationAsync(op, new[] { photo });
                return true;
            }
        }

        return false;
    }

    /// <summary>キャンバス中心を基準に段ズーム（キーボードの +/- 用。ホイールと同じ段ラダーを共有）。</summary>
    private void ZoomStepFromCenter(bool zoomIn)
    {
        _viewport.ZoomToStop(zoomIn, _viewport.CanvasWidth / 2, _viewport.CanvasHeight / 2);
        InvalidateMain();
    }
}
