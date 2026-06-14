using System.Data.SQLite;

namespace PhotoQuickSelector.Core;

/// <summary>
/// フォルダ単位の評価データ永続化。対象フォルダ直下の
/// <c>PhotoQuickSelector.sqlite3</c> に rating / flag / カラーラベルを保存する。
/// 元の画像ファイルは一切変更しない。
/// </summary>
public sealed class MetadataStore : IDisposable
{
    public const string DatabaseFileName = "PhotoQuickSelector.sqlite3";
    private const int CurrentSchemaVersion = 1;

    public const string ColumnRating = "rating";
    public const string ColumnFlagRating = "flag_rating";
    public const string ColumnColorLabelRed = "color_label_red";
    public const string ColumnColorLabelYellow = "color_label_yellow";
    public const string ColumnColorLabelGreen = "color_label_green";
    public const string ColumnColorLabelBlue = "color_label_blue";
    public const string ColumnColorLabelPurple = "color_label_purple";

    private static readonly IReadOnlyDictionary<ColorLabel, string> ColorLabelColumns =
        new Dictionary<ColorLabel, string>
        {
            [ColorLabel.Red] = ColumnColorLabelRed,
            [ColorLabel.Yellow] = ColumnColorLabelYellow,
            [ColorLabel.Green] = ColumnColorLabelGreen,
            [ColorLabel.Blue] = ColumnColorLabelBlue,
            [ColorLabel.Purple] = ColumnColorLabelPurple,
        };

    public string FolderPath { get; }
    public string DatabasePath { get; }

    private readonly SQLiteConnection _connection;

    public MetadataStore(string folderPath)
    {
        FolderPath = Path.GetFullPath(folderPath);
        DatabasePath = Path.Combine(FolderPath, DatabaseFileName);

        _connection = new SQLiteConnection(
            new SQLiteConnectionStringBuilder { DataSource = DatabasePath }.ToString());
        _connection.Open();

        EnsureSchema();
    }

    /// <summary>
    /// 保存済みの評価を読み込んで <see cref="PhotoEvaluation"/> を構築する。
    /// 行が無い、または invalid_flag が立っている場合は永続化値なし（EXIF レーティングのみ）。
    /// </summary>
    public PhotoEvaluation LoadEvaluation(string fileName, int exifRating)
    {
        var evaluation = new PhotoEvaluation { ExifRating = exifRating };

        using var command = _connection.CreateCommand();
        command.CommandText =
            $"SELECT {ColumnRating}, {ColumnFlagRating}, " +
            $"{ColumnColorLabelRed}, {ColumnColorLabelYellow}, {ColumnColorLabelGreen}, " +
            $"{ColumnColorLabelBlue}, {ColumnColorLabelPurple} " +
            "FROM image_file_metadata WHERE file_name = @fn AND invalid_flag = 0";
        command.Parameters.AddWithValue("@fn", fileName);

        using var reader = command.ExecuteReader();
        if (!reader.Read()) return evaluation;

        evaluation.PersistedRating = ReadNullableInt(reader, 0);
        evaluation.PersistedFlagRating = ReadNullableInt(reader, 1);
        evaluation.SetPersistedColorLabel(ColorLabel.Red, ReadNullableInt(reader, 2));
        evaluation.SetPersistedColorLabel(ColorLabel.Yellow, ReadNullableInt(reader, 3));
        evaluation.SetPersistedColorLabel(ColorLabel.Green, ReadNullableInt(reader, 4));
        evaluation.SetPersistedColorLabel(ColorLabel.Blue, ReadNullableInt(reader, 5));
        evaluation.SetPersistedColorLabel(ColorLabel.Purple, ReadNullableInt(reader, 6));
        return evaluation;
    }

    public void SaveRating(string fileName, int? rating) => UpsertColumn(fileName, ColumnRating, rating);

    public void SaveFlagRating(string fileName, int? flag) => UpsertColumn(fileName, ColumnFlagRating, flag);

    public void SaveColorLabel(string fileName, ColorLabel label, int? value)
        => UpsertColumn(fileName, ColorLabelColumns[label], value);

    private void UpsertColumn(string fileName, string column, object? value)
    {
        using var command = _connection.CreateCommand();
        // file_name は image_file_metadata の主キー。既存行があれば該当列のみ更新。
        command.CommandText =
            $"INSERT INTO image_file_metadata (file_name, {column}) VALUES (@fn, @val) " +
            $"ON CONFLICT(file_name) DO UPDATE SET {column} = @val";
        command.Parameters.AddWithValue("@fn", fileName);
        command.Parameters.AddWithValue("@val", value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static int? ReadNullableInt(SQLiteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal));

    // --- スキーマ管理 ---

    private void EnsureSchema()
    {
        if (!TableExists("schema_info"))
            CreateSchemaInfoTable();

        var version = GetSchemaVersion();
        while (version < CurrentSchemaVersion)
        {
            version++;
            ApplyMigration(version);
        }
    }

    private bool TableExists(string tableName)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            "SELECT count(*) FROM sqlite_master WHERE type='table' AND tbl_name=@name";
        command.Parameters.AddWithValue("@name", tableName);
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    private int GetSchemaVersion()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT value FROM schema_info WHERE key='schema_version'";
        return int.Parse((string)command.ExecuteScalar()!);
    }

    private void CreateSchemaInfoTable() => RunInTransaction(() =>
    {
        ExecuteNonQuery(
            "CREATE TABLE schema_info (key TEXT PRIMARY KEY, value TEXT)");
        ExecuteNonQuery(
            "INSERT INTO schema_info (key, value) VALUES ('schema_version', '0')");
    });

    private void ApplyMigration(int version)
    {
        switch (version)
        {
            case 1:
                Migrate_1();
                break;
            default:
                throw new InvalidOperationException($"未知のスキーマバージョン: {version}");
        }
    }

    private void Migrate_1() => RunInTransaction(() =>
    {
        ExecuteNonQuery(@"
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
        ExecuteNonQuery("UPDATE schema_info SET value='1' WHERE key='schema_version'");
    });

    private void RunInTransaction(Action action)
    {
        using var transaction = _connection.BeginTransaction();
        try
        {
            action();
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private void ExecuteNonQuery(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();
}
