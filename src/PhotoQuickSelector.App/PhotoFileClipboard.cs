using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PhotoQuickSelector_App.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace PhotoQuickSelector_App;

/// <summary>
/// 写真ファイルの実体をクリップボードへ「コピー」として載せる（エクスプローラの Ctrl+C 相当）。
/// 貼り付け先（エクスプローラ等）ではファイルコピーになる（<see cref="DataPackageOperation.Copy"/>）。
/// テキスト（1 行 1 パス）も併載するので、テキスト欄へも貼り付けられる。
/// パスのみのコピー（<see cref="PhotoFileCommands.CopyPaths(IEnumerable{PhotoItemViewModel})"/>）とは別物。
/// </summary>
public static class PhotoFileClipboard
{
    /// <summary>
    /// <paramref name="items"/> のファイル実体をクリップボードへコピーとして載せる。
    /// <paramref name="includeSiblings"/> が true のときは同一フォルダ内の「同名（拡張子違い）」ファイルも
    /// まとめて対象にする（例: <c>DSC0001.JPG</c> と <c>DSC0001.ARW</c> を一緒にコピー）。
    /// I/O（同名ファイル列挙・<see cref="StorageFile"/> 取得）は UI を塞がないよう待機で回す。
    /// </summary>
    public static async Task CopyFilesAsync(IEnumerable<PhotoItemViewModel> items, bool includeSiblings)
    {
        var basePaths = items.Where(i => i is not null).Select(i => i.Meta.Path).ToList();
        if (basePaths.Count == 0) return;

        // 対象パスの確定（同名別拡張子の展開はディスク列挙なのでバックグラウンドで）。
        var paths = includeSiblings
            ? await Task.Run(() => ExpandSiblings(basePaths))
            : basePaths;

        // StorageFile 取得は 1 件ずつ await（await のたびに制御を返すので大量選択でも UI は固まらない）。
        var files = new List<IStorageItem>();
        foreach (var path in paths)
        {
            try { files.Add(await StorageFile.GetFileFromPathAsync(path)); }
            catch { /* 消失ファイルは飛ばす */ }
        }
        if (files.Count == 0) return;

        try
        {
            var data = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            data.SetStorageItems(files);
            // テキスト欄へ貼れるよう、パス一覧も併載する（要望②-4）。
            data.SetText(string.Join("\r\n", paths));
            Clipboard.SetContent(data);
        }
        catch { /* クリップボード占有等は黙殺 */ }
    }

    /// <summary>
    /// 各パスについて、同一フォルダ内の「拡張子を除いた名前が一致する」ファイルを集めて重複なく返す。
    /// 元の並び順を保ちつつ、各ベースファイルの直後に同名の別拡張子（RAW 等）を差し込む。
    /// 前方一致の誤検出（<c>DSC0001</c> が <c>DSC00010</c> を拾う）を避けるため、名前は完全一致で判定する。
    /// ファイル実体コピー（本クラス）とパスのみコピー（<see cref="PhotoFileCommands.CopyPathsWithSiblingsAsync"/>）で
    /// 「関連ファイルを含める」の対象展開を共有するため internal 公開。ディスク列挙なので呼び出し側で
    /// バックグラウンド実行すること。
    /// </summary>
    internal static List<string> ExpandSiblings(IReadOnlyList<string> basePaths)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        foreach (var path in basePaths)
        {
            if (seen.Add(path)) result.Add(path);

            var dir = Path.GetDirectoryName(path);
            var stem = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(stem)) continue;

            IEnumerable<string> siblings;
            try
            {
                // "stem.*" で粗く絞り、拡張子を除いた名前の完全一致でふるいにかける。
                siblings = Directory.EnumerateFiles(dir, stem + ".*")
                    .Where(f => string.Equals(
                        Path.GetFileNameWithoutExtension(f), stem, System.StringComparison.OrdinalIgnoreCase));
            }
            catch { continue; }

            foreach (var s in siblings)
                if (seen.Add(s)) result.Add(s);
        }
        return result;
    }
}
