using MetadataExtractor;
using MetadataExtractor.Formats.Xmp;

namespace PhotoQuickSelector.Core;

/// <summary>EXIF 詳細表示用の 1 タグ（名前＋人間可読の値）。</summary>
public sealed class ExifTagEntry
{
    public ExifTagEntry(string name, string description)
    {
        Name = name;
        Description = description;
    }

    public string Name { get; }
    public string Description { get; }
}

/// <summary>EXIF 詳細表示用のディレクトリ（グループ）1 つぶん。</summary>
public sealed class ExifTagGroup
{
    public ExifTagGroup(string directoryName, IReadOnlyList<ExifTagEntry> tags)
    {
        DirectoryName = directoryName;
        Tags = tags;
    }

    public string DirectoryName { get; }
    public IReadOnlyList<ExifTagEntry> Tags { get; }
}

/// <summary>MetadataExtractor の全ディレクトリ・全タグを表示用にダンプする。</summary>
public static class ExifTagReader
{
    /// <summary>1 タグの Description がこの文字数を超える場合は切り詰める（バイナリ系タグの巨大文字列対策）。</summary>
    private const int MaxDescriptionLength = 300;

    /// <summary>
    /// ファイル自体の情報（ファイル名・サイズ・更新日時）を持つディレクトリ名
    /// （MetadataExtractor の <c>FileMetadataDirectory</c> の名前）。並べ替えの基準＝呼び出し側が
    /// 「先頭が File グループか」を判定するためにも使う。
    /// </summary>
    public const string FileDirectoryName = "File";

    /// <summary>
    /// 指定ファイルの全メタデータディレクトリ・全タグを人間可読の形でダンプする。
    /// タグ名/ディレクトリ名は MetadataExtractor の英語名のまま（ローカライズしない）。
    /// <see cref="FileDirectoryName"/> グループだけは先頭へ移し、残りは原順のまま返す。
    /// 例外時（未対応形式・破損ファイル・パス不正など）は throw せず空リストを返す。
    /// </summary>
    public static IReadOnlyList<ExifTagGroup> ReadAllTags(string path)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(path);

            var groups = new List<ExifTagGroup>();
            foreach (var directory in directories)
            {
                var entries = new List<ExifTagEntry>();
                foreach (var tag in directory.Tags)
                    AddEntry(entries, tag.Name, tag.Description);

                // XmpDirectory は Tags に「XMP Value Count」しか持たず、実体（xmp:Rating 等）は
                // XmpMeta 側にある。プロパティのパス→値へ展開しないと中身が表示できない。
                if (directory is XmpDirectory xmp)
                {
                    foreach (var property in xmp.GetXmpProperties()
                                 .OrderBy(p => p.Key, StringComparer.Ordinal))
                        AddEntry(entries, property.Key, property.Value);
                }

                if (entries.Count == 0) continue;
                groups.Add(new ExifTagGroup(directory.Name, entries));
            }

            MoveFileGroupToFront(groups);
            return groups;
        }
        catch
        {
            // 未対応形式・破損ファイル等はすべて「表示するタグなし」として扱う。
            return Array.Empty<ExifTagGroup>();
        }
    }

    /// <summary>
    /// File グループを先頭へ移す（他のグループの相対順は変えない）。
    /// MetadataExtractor は File Type / File を末尾に足すため、既定ではファイル自体の情報が
    /// 一番下に来てしまう。File Type（検出形式・MIME）は情報価値が薄いので末尾に据え置く。
    /// </summary>
    private static void MoveFileGroupToFront(List<ExifTagGroup> groups)
    {
        var index = groups.FindIndex(g => g.DirectoryName == FileDirectoryName);
        if (index <= 0) return; // 見つからない、または既に先頭

        var fileGroup = groups[index];
        groups.RemoveAt(index);
        groups.Insert(0, fileGroup);
    }

    /// <summary>空値をスキップし、長すぎる値を切り詰めてタグを追加する。</summary>
    private static void AddEntry(List<ExifTagEntry> entries, string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return;

        if (description.Length > MaxDescriptionLength)
            description = description[..(MaxDescriptionLength - 1)] + "…";

        entries.Add(new ExifTagEntry(name, description));
    }
}
