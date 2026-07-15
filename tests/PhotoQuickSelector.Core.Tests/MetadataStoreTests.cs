using PhotoQuickSelector.Core;
using Xunit;

namespace PhotoQuickSelector.Core.Tests;

public sealed class MetadataStoreTests : IDisposable
{
    private readonly string _folder;

    public MetadataStoreTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "pqs_test_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(_folder, recursive: true); }
        catch { /* テスト後始末の失敗は無視 */ }
    }

    [Fact]
    public void Constructor_DoesNotCreateDatabaseFile()
    {
        using var store = new MetadataStore(_folder);
        Assert.False(File.Exists(Path.Combine(_folder, MetadataStore.DefaultDatabaseFileName)));
        Assert.False(store.DatabaseExists);
    }

    [Fact]
    public void LoadEvaluation_DoesNotCreateDatabaseFile()
    {
        using var store = new MetadataStore(_folder);
        var e = store.LoadEvaluation("DSC0001.JPG", exifRating: 4);

        // 読み込みだけではファイルを作らない（EXIF レーティングのみで返す）。
        Assert.False(File.Exists(Path.Combine(_folder, MetadataStore.DefaultDatabaseFileName)));
        Assert.Null(e.PersistedRating);
        Assert.Equal(4, e.Rating); // EXIF フォールバック
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithoutFileName_UsesDefault(string? fileName)
    {
        using var store = new MetadataStore(_folder, fileName);

        Assert.Equal(MetadataStore.DefaultDatabaseFileName, store.DatabaseFileName);
        Assert.Equal(Path.Combine(_folder, MetadataStore.DefaultDatabaseFileName), store.DatabasePath);
    }

    [Fact]
    public void Constructor_WithFileName_UsesItAndKeepsDefaultDbUntouched()
    {
        const string custom = "PhotoQuickSelector.Dev.sqlite3";
        using var store = new MetadataStore(_folder, custom);
        Assert.Equal(Path.Combine(_folder, custom), store.DatabasePath);

        store.SaveRating("DSC0001.JPG", 3);

        // 指定名だけが作られ、既定名の DB（＝日常版の評価データ）は一切触られない。
        Assert.True(File.Exists(Path.Combine(_folder, custom)));
        Assert.False(File.Exists(Path.Combine(_folder, MetadataStore.DefaultDatabaseFileName)));
    }

    [Fact]
    public void FirstSave_CreatesDatabaseFile()
    {
        using var store = new MetadataStore(_folder);
        Assert.False(store.DatabaseExists);

        store.SaveRating("DSC0001.JPG", 3); // 初回書き込みでファイル生成

        Assert.True(File.Exists(Path.Combine(_folder, MetadataStore.DefaultDatabaseFileName)));
        Assert.True(store.DatabaseExists);
    }

    [Fact]
    public void SaveAndLoadRating_RoundTrips()
    {
        using var store = new MetadataStore(_folder);
        store.SaveRating("DSC0001.JPG", 5);

        var e = store.LoadEvaluation("DSC0001.JPG", exifRating: 0);
        Assert.Equal(5, e.PersistedRating);
        Assert.Equal(5, e.Rating);
    }

    [Fact]
    public void SaveFlagAndColorLabels_RoundTrip()
    {
        using var store = new MetadataStore(_folder);
        const string file = "DSC0002.JPG";

        store.SaveFlagRating(file, -1);
        store.SaveColorLabel(file, ColorLabel.Red, 1);
        store.SaveColorLabel(file, ColorLabel.Purple, 1);

        var e = store.LoadEvaluation(file, exifRating: 0);
        Assert.Equal(-1, e.FlagRating);
        Assert.True(e.HasColorLabel(ColorLabel.Red));
        Assert.True(e.HasColorLabel(ColorLabel.Purple));
        Assert.False(e.HasColorLabel(ColorLabel.Blue));
    }

    [Fact]
    public void Updates_OverwriteSameRow()
    {
        using var store = new MetadataStore(_folder);
        const string file = "DSC0003.JPG";

        store.SaveRating(file, 2);
        store.SaveRating(file, 4);
        store.SaveColorLabel(file, ColorLabel.Green, 1);

        var e = store.LoadEvaluation(file, exifRating: 0);
        Assert.Equal(4, e.PersistedRating);          // 上書きされている
        Assert.True(e.HasColorLabel(ColorLabel.Green)); // rating 更新で消えていない
    }

    [Fact]
    public void Data_PersistsAcrossReopen()
    {
        const string file = "DSC0004.JPG";

        using (var store = new MetadataStore(_folder))
        {
            store.SaveRating(file, 3);
            store.SaveColorLabel(file, ColorLabel.Blue, 1);
        }

        using (var reopened = new MetadataStore(_folder))
        {
            var e = reopened.LoadEvaluation(file, exifRating: 0);
            Assert.Equal(3, e.PersistedRating);
            Assert.True(e.HasColorLabel(ColorLabel.Blue));
        }
    }

    [Fact]
    public void ClearRating_ToNull_RemovesPersistedValue()
    {
        using var store = new MetadataStore(_folder);
        const string file = "DSC0005.JPG";

        store.SaveRating(file, 5);
        store.SaveRating(file, null);

        var e = store.LoadEvaluation(file, exifRating: 1);
        Assert.Null(e.PersistedRating);
        Assert.Equal(1, e.Rating); // EXIF フォールバックに戻る
    }
}
