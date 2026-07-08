using MetadataExtractor;

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
    /// 指定ファイルの全メタデータディレクトリ・全タグを人間可読の形でダンプする。
    /// タグ名/ディレクトリ名は MetadataExtractor の英語名のまま（ローカライズしない）。
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
                {
                    var description = tag.Description;
                    if (string.IsNullOrWhiteSpace(description)) continue;

                    if (description.Length > MaxDescriptionLength)
                        description = description[..(MaxDescriptionLength - 1)] + "…";

                    entries.Add(new ExifTagEntry(tag.Name, description));
                }

                if (entries.Count == 0) continue;
                groups.Add(new ExifTagGroup(directory.Name, entries));
            }

            return groups;
        }
        catch
        {
            // 未対応形式・破損ファイル等はすべて「表示するタグなし」として扱う。
            return Array.Empty<ExifTagGroup>();
        }
    }
}
