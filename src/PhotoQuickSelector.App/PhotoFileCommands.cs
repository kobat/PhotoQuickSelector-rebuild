using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using PhotoQuickSelector_App.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace PhotoQuickSelector_App;

/// <summary>
/// 写真 1 枚への外部連携キー操作（エクスプローラ表示 / 既定アプリ起動 / パスのコピー / 共有）を集約する。
/// 評価キー（<see cref="PhotoKeyCommands"/>）と同様に、サムネイル一覧（<see cref="MainPage"/>）と
/// 大画面プレビュー（<see cref="Controls.PreviewControl"/>）の双方から呼んで挙動を共通化する（SPEC §3-8）。
/// 実体は Win32 / WinUI 依存（<see cref="Process"/>・<see cref="Clipboard"/> 等）のため Core ではなく App 層に置く。
/// </summary>
public static class PhotoFileCommands
{
    /// <summary>
    /// 現在の修飾子（<see cref="KeyboardModifiers"/>）と <paramref name="key"/> に応じて
    /// <paramref name="item"/> に対する外部連携を実行する。処理したら true。
    /// Alt+S（共有）は <paramref name="settings"/> の <see cref="AppSettings.SharePath"/> を参照する。
    /// </summary>
    public static bool TryHandle(VirtualKey key, PhotoItemViewModel item, AppSettings settings)
    {
        if (item is null) return false;

        bool alt = KeyboardModifiers.Alt;
        bool ctrl = KeyboardModifiers.Ctrl;
        var path = item.Meta.Path;

        if (key == VirtualKey.E)
        {
            // Ctrl+Alt+E : パスをクリップボードへコピー
            if (ctrl && alt) { CopyPath(path); return true; }
            // Ctrl+E : エクスプローラで /select 表示
            if (ctrl && !alt) { ShowInExplorer(path); return true; }
            // Alt+E : 既定アプリで開く
            if (alt && !ctrl) { OpenWithDefault(path); return true; }
        }

        // Alt+S : 共有（設定済み exe を起動／未設定なら Windows 標準の共有シート）
        if (alt && !ctrl && key == VirtualKey.S)
        {
            _ = ShareHelper.ShareAsync(path, settings);
            return true;
        }

        return false;
    }

    // === メニューからの直接呼び出し用（キー処理 TryHandle と同じ実体を共有する） ===

    /// <summary>エクスプローラで対象を選択表示する（メニュー「エクスプローラーで表示」）。</summary>
    public static void OpenInExplorer(PhotoItemViewModel item)
    {
        if (item is not null) ShowInExplorer(item.Meta.Path);
    }

    /// <summary>既定アプリで開く（メニュー「既定のアプリで開く」）。</summary>
    public static void OpenWithDefault(PhotoItemViewModel item)
    {
        if (item is not null) OpenWithDefault(item.Meta.Path);
    }

    /// <summary>パスをクリップボードへコピー（メニュー「パスをコピー」）。</summary>
    public static void CopyPath(PhotoItemViewModel item)
    {
        if (item is not null) CopyPath(item.Meta.Path);
    }

    /// <summary>共有（メニュー「共有」。設定済み exe ／未設定なら Windows 標準共有シート）。</summary>
    public static void Share(PhotoItemViewModel item, AppSettings settings)
    {
        if (item is not null) _ = ShareHelper.ShareAsync(item.Meta.Path, settings);
    }

    // === 複数選択（右クリックメニュー）対応 ===

    /// <summary>選択集合の全メンバーを既定アプリで開く（メニュー「既定のアプリで開く」の一括版）。</summary>
    public static void OpenWithDefault(IEnumerable<PhotoItemViewModel> items)
    {
        foreach (var item in items)
            if (item is not null) OpenWithDefault(item.Meta.Path);
    }

    /// <summary>選択集合の全メンバーのフルパスを 1 行 1 パスでクリップボードへコピーする。</summary>
    public static void CopyPaths(IEnumerable<PhotoItemViewModel> items)
    {
        var paths = items.Where(i => i is not null).Select(i => i.Meta.Path).ToList();
        if (paths.Count == 0) return;
        try
        {
            var data = new DataPackage();
            data.SetText(string.Join("\r\n", paths));
            Clipboard.SetContent(data);
        }
        catch { /* クリップボード占有等は黙殺 */ }
    }

    /// <summary>選択集合の全メンバーを共有する（設定済み exe は各ファイルを引数起動／共有シートは複数同時）。</summary>
    public static void Share(IReadOnlyList<PhotoItemViewModel> items, AppSettings settings)
    {
        var paths = items.Where(i => i is not null).Select(i => i.Meta.Path).ToList();
        if (paths.Count == 0) return;
        _ = ShareHelper.ShareAsync(paths, settings);
    }

    /// <summary>エクスプローラで対象ファイルを選択状態にして表示する（<c>/select,</c>）。</summary>
    private static void ShowInExplorer(string path)
    {
        try
        {
            // 引数はパスを二重引用符で囲む。/select の直後にカンマ＋パスを置くのが explorer の作法。
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
            {
                UseShellExecute = true,
            });
        }
        catch { /* ファイル消失等は黙殺（クラッシュさせない） */ }
    }

    /// <summary>既定アプリで対象ファイルを開く（シェル実行）。</summary>
    private static void OpenWithDefault(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch { /* 関連付けなし / ファイル消失等は黙殺 */ }
    }

    /// <summary>対象ファイルのフルパスをクリップボードへコピーする。</summary>
    private static void CopyPath(string path)
    {
        try
        {
            var data = new DataPackage();
            data.SetText(path);
            Clipboard.SetContent(data);
        }
        catch { /* クリップボード占有等は黙殺 */ }
    }
}
