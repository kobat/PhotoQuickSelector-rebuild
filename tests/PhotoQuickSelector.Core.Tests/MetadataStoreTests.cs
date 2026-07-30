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

    // --- 更新時刻の記録（スキーマ v2） ---

    /// <summary>時刻を明示的に進められる時計。更新時刻の前後関係を決定的に検証するため。</summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public FakeTimeProvider(DateTimeOffset start) => _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;

        public void Set(DateTimeOffset now) => _now = now;
    }

    private static readonly DateTimeOffset T0 =
        new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private MetadataStore CreateStore(FakeTimeProvider clock)
        => new(_folder, databaseFileName: null, timeProvider: clock);

    [Fact]
    public void Save_RecordsUpdatedAtForThatFieldAndRow()
    {
        var clock = new FakeTimeProvider(T0);
        using var store = CreateStore(clock);
        const string file = "DSC0100.JPG";

        store.SaveRating(file, 3);

        var r = store.LoadRecord(file);
        Assert.NotNull(r);
        Assert.Equal(3, r!.Rating);
        Assert.Equal(T0, r.RatingUpdatedAt);
        Assert.Equal(T0, r.UpdatedAt);
    }

    [Fact]
    public void Save_LeavesUntouchedFieldsWithoutUpdatedAt()
    {
        var clock = new FakeTimeProvider(T0);
        using var store = CreateStore(clock);
        const string file = "DSC0101.JPG";

        store.SaveRating(file, 3);

        var r = store.LoadRecord(file)!;
        Assert.Null(r.FlagRating);
        Assert.Null(r.FlagRatingUpdatedAt);
        foreach (var label in Enum.GetValues<ColorLabel>())
        {
            Assert.Null(r.GetColorLabel(label));
            Assert.Null(r.GetColorLabelUpdatedAt(label));
        }
    }

    [Fact]
    public void SaveSameValue_DoesNotAdvanceUpdatedAt()
    {
        var clock = new FakeTimeProvider(T0);
        using var store = CreateStore(clock);
        const string file = "DSC0102.JPG";

        store.SaveRating(file, 3);
        clock.Advance(TimeSpan.FromHours(1));
        store.SaveRating(file, 3); // 同じ値の押し直し＝実質の変更なし

        var r = store.LoadRecord(file)!;
        Assert.Equal(T0, r.RatingUpdatedAt);
        Assert.Equal(T0, r.UpdatedAt);
    }

    [Fact]
    public void SaveDifferentValue_AdvancesUpdatedAt()
    {
        var clock = new FakeTimeProvider(T0);
        using var store = CreateStore(clock);
        const string file = "DSC0103.JPG";

        store.SaveRating(file, 3);
        clock.Advance(TimeSpan.FromHours(1));
        store.SaveRating(file, 4);

        var r = store.LoadRecord(file)!;
        Assert.Equal(T0.AddHours(1), r.RatingUpdatedAt);
        Assert.Equal(T0.AddHours(1), r.UpdatedAt);
    }

    [Fact]
    public void ClearToNull_CountsAsChangeAndAdvancesUpdatedAt()
    {
        var clock = new FakeTimeProvider(T0);
        using var store = CreateStore(clock);
        const string file = "DSC0104.JPG";

        store.SaveRating(file, 5);
        clock.Advance(TimeSpan.FromHours(1));
        store.SaveRating(file, null); // 解除も「変更」として時刻を進める

        var r = store.LoadRecord(file)!;
        Assert.Null(r.Rating);
        Assert.Equal(T0.AddHours(1), r.RatingUpdatedAt);
    }

    [Fact]
    public void SaveNullOverNull_DoesNotAdvanceUpdatedAt()
    {
        var clock = new FakeTimeProvider(T0);
        using var store = CreateStore(clock);
        const string file = "DSC0105.JPG";

        store.SaveRating(file, 3); // 行を作る（flag は NULL のまま）
        var flagUpdatedAtBefore = store.LoadRecord(file)!.FlagRatingUpdatedAt;
        clock.Advance(TimeSpan.FromHours(1));
        store.SaveFlagRating(file, null); // NULL → NULL は IS で等しいと判定される

        var r = store.LoadRecord(file)!;
        Assert.Null(flagUpdatedAtBefore);
        Assert.Null(r.FlagRatingUpdatedAt);
        Assert.Equal(T0, r.UpdatedAt); // 行単位も進まない
    }

    [Fact]
    public void FieldUpdatedAt_AreIndependentPerField()
    {
        var clock = new FakeTimeProvider(T0);
        using var store = CreateStore(clock);
        const string file = "DSC0106.JPG";

        store.SaveRating(file, 2);
        clock.Advance(TimeSpan.FromMinutes(10));
        store.SaveFlagRating(file, 1);
        clock.Advance(TimeSpan.FromMinutes(10));
        store.SaveColorLabel(file, ColorLabel.Green, 1);

        var r = store.LoadRecord(file)!;
        Assert.Equal(T0, r.RatingUpdatedAt);
        Assert.Equal(T0.AddMinutes(10), r.FlagRatingUpdatedAt);
        Assert.Equal(T0.AddMinutes(20), r.GetColorLabelUpdatedAt(ColorLabel.Green));
        Assert.Null(r.GetColorLabelUpdatedAt(ColorLabel.Red));
        // 行単位は項目別の最大値。
        Assert.Equal(T0.AddMinutes(20), r.UpdatedAt);
    }

    [Fact]
    public void RowUpdatedAt_DoesNotGoBackwardsWhenClockRollsBack()
    {
        var clock = new FakeTimeProvider(T0);
        using var store = CreateStore(clock);
        const string file = "DSC0107.JPG";

        store.SaveRating(file, 2);
        clock.Set(T0.AddHours(-1)); // 時計の巻き戻し
        store.SaveFlagRating(file, 1);

        var r = store.LoadRecord(file)!;
        Assert.Equal(T0.AddHours(-1), r.FlagRatingUpdatedAt); // 項目別は実際の時刻を素直に記録
        Assert.Equal(T0, r.UpdatedAt);                        // 行単位は後退しない
    }

    [Fact]
    public void UpdatedAt_IsUtcWithSecondPrecision()
    {
        // ミリ秒とローカルオフセットを持つ時刻を与えても、UTC・秒精度で往復する。
        var start = new DateTimeOffset(2026, 7, 30, 21, 34, 56, 789, TimeSpan.FromHours(9));
        var clock = new FakeTimeProvider(start);
        using var store = CreateStore(clock);
        const string file = "DSC0108.JPG";

        store.SaveRating(file, 1);

        var actual = store.LoadRecord(file)!.RatingUpdatedAt;
        Assert.NotNull(actual);
        Assert.Equal(TimeSpan.Zero, actual!.Value.Offset);
        Assert.Equal(0, actual.Value.Millisecond);
        Assert.Equal(start.ToUnixTimeSeconds(), actual.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public void LoadRecord_ReturnsNull_WhenNoDatabaseOrNoRow()
    {
        using var store = new MetadataStore(_folder);

        Assert.Null(store.LoadRecord("DSC0109.JPG")); // DB がまだ無い
        Assert.False(store.DatabaseExists);           // 読み取りで作らない

        store.SaveRating("DSC0110.JPG", 1);
        Assert.Null(store.LoadRecord("DSC0109.JPG")); // 行が無い
    }

    [Fact]
    public void LoadAllRecords_ReturnsAllRowsOrderedByFileName()
    {
        using var store = new MetadataStore(_folder);
        Assert.Empty(store.LoadAllRecords()); // DB がまだ無い

        store.SaveRating("DSC0202.JPG", 2);
        store.SaveRating("DSC0201.JPG", 1);

        var all = store.LoadAllRecords();
        Assert.Equal(new[] { "DSC0201.JPG", "DSC0202.JPG" }, all.Select(r => r.FileName));
        Assert.Equal(1, all[0].Rating);
        Assert.Equal(2, all[1].Rating);
    }

    // --- 保存の戻り値（メモリ常駐の更新時刻へ反映するため） ---

    [Fact]
    public void Save_ReturnsAppliedTimestamps()
    {
        var clock = new FakeTimeProvider(T0);
        using var store = CreateStore(clock);
        const string file = "DSC0300.JPG";

        var saved = store.SaveRating(file, 3);

        Assert.Equal(T0, saved.ItemUpdatedAt);
        Assert.Equal(T0, saved.RowUpdatedAt);
        // DB を読み直した値と一致する（＝メモリ側が SQL の判定を再実装せずに済む）。
        var r = store.LoadRecord(file)!;
        Assert.Equal(r.RatingUpdatedAt, saved.ItemUpdatedAt);
        Assert.Equal(r.UpdatedAt, saved.RowUpdatedAt);
    }

    [Fact]
    public void Save_SameValue_ReturnsUnchangedTimestamps()
    {
        var clock = new FakeTimeProvider(T0);
        using var store = CreateStore(clock);
        const string file = "DSC0301.JPG";

        store.SaveRating(file, 3);
        clock.Advance(TimeSpan.FromHours(1));
        var saved = store.SaveRating(file, 3); // 同値の押し直しは時刻を進めない

        Assert.Equal(T0, saved.ItemUpdatedAt);
        Assert.Equal(T0, saved.RowUpdatedAt);
    }

    [Fact]
    public void Save_OtherFieldsKeepTheirOwnTimestamps()
    {
        var clock = new FakeTimeProvider(T0);
        using var store = CreateStore(clock);
        const string file = "DSC0302.JPG";

        store.SaveRating(file, 1);
        clock.Advance(TimeSpan.FromMinutes(5));
        var saved = store.SaveColorLabel(file, ColorLabel.Blue, 1);

        Assert.Equal(T0.AddMinutes(5), saved.ItemUpdatedAt); // 保存した項目の時刻
        Assert.Equal(T0.AddMinutes(5), saved.RowUpdatedAt);  // 行単位は最大値
        Assert.Equal(T0, store.LoadRecord(file)!.RatingUpdatedAt); // 触っていない項目は不変
    }

    [Fact]
    public void EvaluationTimestamps_FromRecord_CarriesAllFields()
    {
        var clock = new FakeTimeProvider(T0);
        using var store = CreateStore(clock);
        const string file = "DSC0303.JPG";

        store.SaveRating(file, 4);
        clock.Advance(TimeSpan.FromMinutes(1));
        store.SaveFlagRating(file, -1);
        clock.Advance(TimeSpan.FromMinutes(1));
        store.SaveColorLabel(file, ColorLabel.Purple, 1);

        var timestamps = EvaluationTimestamps.FromRecord(store.LoadRecord(file));

        Assert.Equal(T0, timestamps.Rating);
        Assert.Equal(T0.AddMinutes(1), timestamps.FlagRating);
        Assert.Equal(T0.AddMinutes(2), timestamps.GetColorLabel(ColorLabel.Purple));
        Assert.Null(timestamps.GetColorLabel(ColorLabel.Red)); // 未変更の項目は不明
        Assert.Equal(T0.AddMinutes(2), timestamps.UpdatedAt);
    }

    [Fact]
    public void EvaluationTimestamps_FromNullRecord_IsAllUnknown()
    {
        var timestamps = EvaluationTimestamps.FromRecord(null);

        Assert.Null(timestamps.Rating);
        Assert.Null(timestamps.FlagRating);
        Assert.Null(timestamps.UpdatedAt);
        foreach (var label in Enum.GetValues<ColorLabel>())
            Assert.Null(timestamps.GetColorLabel(label));
    }

    [Fact]
    public void EvaluationTimestamps_TakeSaveResults()
    {
        var clock = new FakeTimeProvider(T0);
        using var store = CreateStore(clock);
        const string file = "DSC0304.JPG";
        var timestamps = EvaluationTimestamps.FromRecord(store.LoadRecord(file)); // 行なし＝全不明

        timestamps.SetRating(store.SaveRating(file, 2));
        clock.Advance(TimeSpan.FromMinutes(3));
        timestamps.SetColorLabel(ColorLabel.Green, store.SaveColorLabel(file, ColorLabel.Green, 1));

        Assert.Equal(T0, timestamps.Rating);
        Assert.Equal(T0.AddMinutes(3), timestamps.GetColorLabel(ColorLabel.Green));
        Assert.Equal(T0.AddMinutes(3), timestamps.UpdatedAt);
        Assert.Null(timestamps.FlagRating);
    }

    [Fact]
    public void PhotoEvaluation_FromRecord_UsesPersistedValuesOverExifRating()
    {
        var clock = new FakeTimeProvider(T0);
        using var store = CreateStore(clock);
        const string file = "DSC0305.JPG";

        store.SaveRating(file, 5);
        store.SaveColorLabel(file, ColorLabel.Yellow, 1);

        var e = PhotoEvaluation.FromRecord(store.LoadRecord(file), exifRating: 2);
        Assert.Equal(5, e.Rating);
        Assert.True(e.HasColorLabel(ColorLabel.Yellow));

        // 行が無ければ EXIF フォールバックのみ。
        var none = PhotoEvaluation.FromRecord(null, exifRating: 2);
        Assert.Null(none.PersistedRating);
        Assert.Equal(2, none.Rating);
    }

    // --- スキーマ移行 / 前方互換 ---

    /// <summary>
    /// v1（更新時刻列なし）の DB を手で作る。移行と旧データの扱いを検証するため。
    /// </summary>
    private void CreateLegacyV1Database(string fileName, string schemaVersion = "1")
    {
        var path = Path.Combine(_folder, fileName);
        using var connection = new System.Data.SQLite.SQLiteConnection(
            new System.Data.SQLite.SQLiteConnectionStringBuilder { DataSource = path }.ToString());
        connection.Open();

        void Exec(string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        Exec("CREATE TABLE schema_info (key TEXT PRIMARY KEY, value TEXT)");
        Exec($"INSERT INTO schema_info (key, value) VALUES ('schema_version', '{schemaVersion}')");
        Exec(@"
            CREATE TABLE image_file_metadata (
                file_name           TEXT PRIMARY KEY,
                rating              INTEGER,
                flag_rating         INTEGER,
                color_label_red     INTEGER,
                color_label_yellow  INTEGER,
                color_label_green   INTEGER,
                color_label_blue    INTEGER,
                color_label_purple  INTEGER,
                invalid_flag        INTEGER NOT NULL DEFAULT 0
            )");
        Exec("INSERT INTO image_file_metadata (file_name, rating, color_label_blue) " +
             "VALUES ('OLD0001.JPG', 4, 1)");
    }

    [Fact]
    public void Migration_FromV1_KeepsValuesAndLeavesUpdatedAtNull()
    {
        CreateLegacyV1Database(MetadataStore.DefaultDatabaseFileName);

        using var store = new MetadataStore(_folder);
        var r = store.LoadRecord("OLD0001.JPG");

        Assert.NotNull(r);
        Assert.Equal(4, r!.Rating);                             // 既存の評価は保たれる
        Assert.Equal(1, r.GetColorLabel(ColorLabel.Blue));
        Assert.Null(r.RatingUpdatedAt);                         // いつ評価したかは不明＝NULL
        Assert.Null(r.GetColorLabelUpdatedAt(ColorLabel.Blue));
        Assert.Null(r.UpdatedAt);
    }

    [Fact]
    public void Migration_FromV1_ThenEditRecordsUpdatedAtForThatFieldOnly()
    {
        CreateLegacyV1Database(MetadataStore.DefaultDatabaseFileName);

        var clock = new FakeTimeProvider(T0);
        using var store = CreateStore(clock);
        store.SaveRating("OLD0001.JPG", 5);

        var r = store.LoadRecord("OLD0001.JPG")!;
        Assert.Equal(T0, r.RatingUpdatedAt);
        Assert.Equal(T0, r.UpdatedAt);
        Assert.Null(r.GetColorLabelUpdatedAt(ColorLabel.Blue)); // 触っていない項目は不明のまま
    }

    [Fact]
    public void Open_FutureSchemaVersion_ThrowsAndKeepsThrowing()
    {
        // 将来版が書いた DB。知らない列を無視して書き込むとデータが壊れるので開かせない。
        CreateLegacyV1Database(MetadataStore.DefaultDatabaseFileName, schemaVersion: "99");

        using var store = new MetadataStore(_folder);

        Assert.Throws<NotSupportedException>(() => store.LoadEvaluation("OLD0001.JPG", 0));
        // 接続を保持していないので 2 回目も素通りせず同じように弾かれる。
        Assert.Throws<NotSupportedException>(() => store.LoadRecord("OLD0001.JPG"));
        Assert.Throws<NotSupportedException>(() => store.SaveRating("OLD0001.JPG", 1));
    }

    // --- 評価のリセット（行削除） ---

    [Fact]
    public void ClearEvaluation_WithoutDatabase_DoesNothingAndDoesNotCreateFile()
    {
        using var store = new MetadataStore(_folder);

        Assert.False(store.ClearEvaluation("DSC0200.JPG"));
        Assert.False(File.Exists(Path.Combine(_folder, MetadataStore.DefaultDatabaseFileName)));
    }

    [Fact]
    public void ClearEvaluation_RemovesRowAndFallsBackToExifRating()
    {
        using var store = new MetadataStore(_folder);
        const string file = "DSC0201.JPG";
        store.SaveRating(file, 2);
        store.SaveFlagRating(file, -1);
        store.SaveColorLabel(file, ColorLabel.Green, 1);

        Assert.True(store.ClearEvaluation(file));

        // 行ごと消えるので「一度も評価していない」状態と同じ（更新時刻も残らない）。
        Assert.Null(store.LoadRecord(file));
        var e = store.LoadEvaluation(file, exifRating: 4);
        Assert.Null(e.PersistedRating);
        Assert.Null(e.PersistedFlagRating);
        Assert.False(e.HasColorLabel(ColorLabel.Green));
        Assert.Equal(4, e.Rating); // EXIF フォールバックが復活する
    }

    [Fact]
    public void ClearEvaluation_LeavesOtherFilesUntouched()
    {
        using var store = new MetadataStore(_folder);
        store.SaveRating("DSC0202.JPG", 3);
        store.SaveRating("DSC0203.JPG", 5);

        store.ClearEvaluation("DSC0202.JPG");

        Assert.Single(store.LoadAllRecords());
        Assert.Equal(5, store.LoadRecord("DSC0203.JPG")!.Rating);
    }

    [Fact]
    public void ClearEvaluation_WithoutRow_ReturnsFalse()
    {
        using var store = new MetadataStore(_folder);
        store.SaveRating("DSC0204.JPG", 3); // DB は作られるが対象ファイルの行は無い

        Assert.False(store.ClearEvaluation("DSC0205.JPG"));
    }

    [Fact]
    public void EvaluationTimestamps_Reset_ClearsAllTimes()
    {
        var clock = new FakeTimeProvider(T0);
        using var store = CreateStore(clock);
        const string file = "DSC0207.JPG";
        store.SaveRating(file, 3);
        store.SaveFlagRating(file, 1);
        store.SaveColorLabel(file, ColorLabel.Red, 1);

        var timestamps = EvaluationTimestamps.FromRecord(store.LoadRecord(file));
        Assert.Equal(T0, timestamps.Rating);

        timestamps.Reset();

        Assert.Null(timestamps.Rating);
        Assert.Null(timestamps.FlagRating);
        Assert.Null(timestamps.UpdatedAt);
        foreach (var label in Enum.GetValues<ColorLabel>())
            Assert.Null(timestamps.GetColorLabel(label));
    }

    [Fact]
    public void SaveAfterClearEvaluation_StartsFreshTimestamps()
    {
        var clock = new FakeTimeProvider(T0);
        using var store = CreateStore(clock);
        const string file = "DSC0206.JPG";
        store.SaveRating(file, 3);

        store.ClearEvaluation(file);
        clock.Advance(TimeSpan.FromHours(1));
        store.SaveRating(file, 3); // リセット前と同じ値。行が無いので「変更なし」にはならない

        var r = store.LoadRecord(file)!;
        Assert.Equal(3, r.Rating);
        Assert.Equal(T0.AddHours(1), r.RatingUpdatedAt); // 旧行の時刻は引き継がない
        Assert.Equal(T0.AddHours(1), r.UpdatedAt);
    }
}
