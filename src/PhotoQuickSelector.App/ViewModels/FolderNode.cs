using System.Collections.ObjectModel;

namespace PhotoQuickSelector_App.ViewModels;

/// <summary>
/// 左ペインのフォルダツリーの 1 ノード。データバインドモードの TreeView 用。
/// 展開矢印を出すため、未読込のうちはダミーの子（Path 空）を 1 つ持たせる。
///
/// 子の読み込みは <see cref="LoadChildren"/> による「差分同期」。
/// 変化が無ければ一切 collection を変更しないため、再展開や更新を安全に繰り返せる
/// （Clear→全件再追加は TreeView 内部状態を壊し重複表示やクラッシュを招くため使わない）。
/// </summary>
public sealed class FolderNode
{
    public string Name { get; }
    public string Path { get; }
    public ObservableCollection<FolderNode> Children { get; } = new();

    public FolderNode(string name, string path, bool hasChildren)
    {
        Name = name;
        Path = path;
        if (hasChildren) Children.Add(CreatePlaceholder());
    }

    private FolderNode()
    {
        Name = string.Empty;
        Path = string.Empty;
    }

    private static FolderNode CreatePlaceholder() => new();
    private bool IsPlaceholder => Path.Length == 0;

    /// <summary>子フォルダをディスクの現状に差分同期する（追加/削除のみ反映）。</summary>
    public void LoadChildren()
    {
        // 望ましい子フォルダ一覧（パス昇順・隠し/システム除外）
        var desired = new List<(string name, string path, bool hasChildren)>();
        try
        {
            foreach (var dir in Directory.GetDirectories(Path)
                         .OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var attributes = File.GetAttributes(dir);
                    if (attributes.HasFlag(FileAttributes.Hidden) ||
                        attributes.HasFlag(FileAttributes.System))
                        continue;
                }
                catch { continue; }

                desired.Add((System.IO.Path.GetFileName(dir), dir, HasSubDirectory(dir)));
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }

        var desiredPaths = new HashSet<string>(
            desired.Select(d => d.path), StringComparer.OrdinalIgnoreCase);

        // 1) プレースホルダ・消えたフォルダを削除
        for (int i = Children.Count - 1; i >= 0; i--)
        {
            if (Children[i].IsPlaceholder || !desiredPaths.Contains(Children[i].Path))
                Children.RemoveAt(i);
        }

        // 2) 追加されたフォルダを正しい位置に挿入（既存ノードは温存＝展開状態を保持）
        //    既存子・desired ともにパス昇順なのでインデックスで突き合わせできる。
        for (int i = 0; i < desired.Count; i++)
        {
            if (i < Children.Count &&
                string.Equals(Children[i].Path, desired[i].path, StringComparison.OrdinalIgnoreCase))
                continue;

            Children.Insert(i, new FolderNode(desired[i].name, desired[i].path, desired[i].hasChildren));
        }
    }

    /// <summary>今すぐ子フォルダを再同期する（手動更新）。</summary>
    public void Refresh() => LoadChildren();

    private static bool HasSubDirectory(string dir)
    {
        try { return Directory.EnumerateDirectories(dir).Any(); }
        catch { return false; }
    }
}
