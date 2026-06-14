using System.Collections.ObjectModel;

namespace PhotoQuickSelector_App.ViewModels;

/// <summary>
/// 左ペインのフォルダツリーの 1 ノード。データバインドモードの TreeView 用。
/// 展開時に <see cref="LoadChildren"/> で子フォルダを遅延読み込みする。
/// 展開矢印を出すため、未読込のうちはダミーの子を 1 つ持たせる。
/// </summary>
public sealed class FolderNode
{
    public string Name { get; }
    public string Path { get; }
    public ObservableCollection<FolderNode> Children { get; } = new();

    public bool IsLoaded { get; private set; }

    /// <summary>通常ノード。subdirがある想定ならプレースホルダを入れて展開矢印を出す。</summary>
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

    /// <summary>子フォルダを実際に列挙して読み込む（初回展開時に呼ぶ）。</summary>
    public void LoadChildren()
    {
        if (IsLoaded) return;
        IsLoaded = true;
        Children.Clear();

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

                Children.Add(new FolderNode(
                    System.IO.Path.GetFileName(dir), dir, HasSubDirectory(dir)));
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    private static bool HasSubDirectory(string dir)
    {
        try { return Directory.EnumerateDirectories(dir).Any(); }
        catch { return false; }
    }
}
