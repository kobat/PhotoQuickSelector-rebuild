namespace PhotoQuickSelector_App.ViewModels;

/// <summary>
/// 左ペイン上部の「最近開いたフォルダ」「お気に入り」に表示する 1 項目。
/// 表示はフォルダ名、ツールチップはフルパス。
/// </summary>
public sealed class FolderShortcut
{
    public string Path { get; }

    /// <summary>表示名。フォルダ名（末尾要素）。ドライブ直下等は元のパス。</summary>
    public string Name { get; }

    public FolderShortcut(string path)
    {
        Path = path;
        var name = System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar));
        Name = string.IsNullOrEmpty(name) ? path : name;
    }
}
