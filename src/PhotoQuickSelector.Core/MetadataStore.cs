using System.Data.SQLite;

namespace PhotoQuickSelector.Core;

/// <summary>
/// フォルダ単位の評価データ永続化。対象フォルダ直下の sqlite（既定
/// <c>PhotoQuickSelector.sqlite3</c>）に rating / flag / カラーラベルを保存する。
/// 元の画像ファイルは一切変更しない。
///
/// 各評価項目には「値が最後に変わった時刻」（UTC・Unix epoch 秒）を併せて記録する。
/// 将来の評価エクスポート／インポートで、競合を項目単位に新しい方を採る形で解決するため。
/// </summary>
public sealed class MetadataStore : IDisposable
{
    /// <summary>評価データの既定ファイル名（旧アプリ互換）。</summary>
    public const string DefaultDatabaseFileName = "PhotoQuickSelector.sqlite3";
    private const int CurrentSchemaVersion = 2;

    public const string ColumnRating = "rating";
    public const string ColumnFlagRating = "flag_rating";
    public const string ColumnColorLabelRed = "color_label_red";
    public const string ColumnColorLabelYellow = "color_label_yellow";
    public const string ColumnColorLabelGreen = "color_label_green";
    public const string ColumnColorLabelBlue = "color_label_blue";
    public const string ColumnColorLabelPurple = "color_label_purple";

    /// <summary>行単位の更新時刻列（項目別更新時刻の最大値）。</summary>
    public const string ColumnUpdatedAt = "updated_at";

    private const string UpdatedAtSuffix = "_updated_at";

    /// <summary>
    /// 評価値の列名から、その項目の更新時刻列名を得る（命名規則の唯一の定義点）。
    /// </summary>
    public static string UpdatedAtColumn(string valueColumn) => valueColumn + UpdatedAtSuffix;

    private static readonly IReadOnlyDictionary<ColorLabel, string> ColorLabelColumns =
        new Dictionary<ColorLabel, string>
        {
            [ColorLabel.Red] = ColumnColorLabelRed,
            [ColorLabel.Yellow] = ColumnColorLabelYellow,
            [ColorLabel.Green] = ColumnColorLabelGreen,
            [ColorLabel.Blue] = ColumnColorLabelBlue,
            [ColorLabel.Purple] = ColumnColorLabelPurple,
        };

    // 評価値を持つ列（＝それぞれに対応する更新時刻列がある）。
    private static readonly string[] ValueColumns =
    {
        ColumnRating,
        ColumnFlagRating,
        ColumnColorLabelRed,
        ColumnColorLabelYellow,
        ColumnColorLabelGreen,
        ColumnColorLabelBlue,
        ColumnColorLabelPurple,
    };

    public string FolderPath { get; }

    /// <summary>この store が使う評価データのファイル名（<see cref="FolderPath"/> 直下）。</summary>
    public string DatabaseFileName { get; }

    public string DatabasePath { get; }

    // 接続は遅延生成（最初の評価書き込み、または既存ファイルからの読み込み時に開く）。
    // ctor で開くと存在しない sqlite を新規作成してしまうため、ここでは開かない。
    private SQLiteConnection? _connection;

    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// 評価ストアを作る（この時点ではファイルを作らない＝<see cref="EnsureConnection"/> 参照）。
    /// </summary>
    /// <param name="folderPath">評価対象のフォルダ。</param>
    /// <param name="databaseFileName">
    /// 評価データのファイル名。null／空なら <see cref="DefaultDatabaseFileName"/>。
    /// 呼び出し側が用途ごとに DB を分けられるようにするための注入点であり、
    /// Core 自身は配布形態（日常版／開発版）を関知しない。
    /// </param>
    /// <param name="timeProvider">
    /// 更新時刻の時計。null なら <see cref="TimeProvider.System"/>。テストで時刻を固定するための注入点。
    /// </param>
    public MetadataStore(string folderPath, string? databaseFileName = null, TimeProvider? timeProvider = null)
    {
        FolderPath = Path.GetFullPath(folderPath);
        DatabaseFileName = string.IsNullOrWhiteSpace(databaseFileName)
            ? DefaultDatabaseFileName
            : databaseFileName;
        DatabasePath = Path.Combine(FolderPath, DatabaseFileName);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// sqlite ファイルが既に存在するか。最初の評価操作の前に作成確認ダイアログを
    /// 出すか（＝まだ無い）の判定に使う。
    /// </summary>
    public bool DatabaseExists => File.Exists(DatabasePath);

    /// <summary>
    /// 接続を遅延生成する。<paramref name="createIfMissing"/> が false でファイルが無ければ
    /// 接続を開かず false を返す（＝読み取り目的でファイルを作らない）。true のとき、
    /// ファイルが無ければ <see cref="SQLiteConnection.Open"/> がここで新規作成する。
    /// </summary>
    private bool EnsureConnection(bool createIfMissing)
    {
        if (_connection != null) return true;
        if (!createIfMissing && !File.Exists(DatabasePath)) return false;

        var connection = new SQLiteConnection(
            new SQLiteConnectionStringBuilder { DataSource = DatabasePath }.ToString());
        connection.Open();
        _connection = connection; // EnsureSchema はこのフィールド経由でコマンドを作る
        try
        {
            EnsureSchema();
        }
        catch
        {
            // スキーマを確認できていない接続は保持しない。保持すると次回以降 early return で
            // 判定を素通りし、未知のスキーマへ書き込んでしまう。破棄しておけば次回は再判定になる
            // （前方互換ガードはファイルの性質なので毎回同じ例外で確実に弾かれる）。
            _connection = null;
            connection.Dispose();
            throw;
        }
        return true;
    }

    /// <summary>
    /// 保存済みの評価を読み込んで <see cref="PhotoEvaluation"/> を構築する。
    /// 行が無い、または invalid_flag が立っている場合は永続化値なし（EXIF レーティングのみ）。
    /// DB ファイルが無ければ作らない（開いただけでは sqlite を生成しない）。
    /// </summary>
    public PhotoEvaluation LoadEvaluation(string fileName, int exifRating)
        => PhotoEvaluation.FromRecord(LoadRecord(fileName), exifRating);

    /// <summary>レーティングを保存し、適用後の更新時刻を返す。</summary>
    public EvaluationSaveResult SaveRating(string fileName, int? rating)
        => UpsertColumn(fileName, ColumnRating, rating);

    /// <summary>フラグを保存し、適用後の更新時刻を返す。</summary>
    public EvaluationSaveResult SaveFlagRating(string fileName, int? flag)
        => UpsertColumn(fileName, ColumnFlagRating, flag);

    /// <summary>カラーラベルを保存し、適用後の更新時刻を返す。</summary>
    public EvaluationSaveResult SaveColorLabel(string fileName, ColorLabel label, int? value)
        => UpsertColumn(fileName, ColorLabelColumns[label], value);

    private EvaluationSaveResult UpsertColumn(string fileName, string column, object? value)
    {
        // 評価書き込み＝ファイル生成点。無ければここで sqlite が新規作成される。
        EnsureConnection(createIfMissing: true);

        var updatedAtColumn = UpdatedAtColumn(column);
        // epoch 秒は 2038 年に int32 を溢れるので long で扱う。
        var now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();

        using var command = _connection!.CreateCommand();
        // file_name は image_file_metadata の主キー。既存行があれば該当列のみ更新。
        //
        // 更新時刻は「値が実際に変わったときだけ」進める。同じ値の押し直し（既に 3 の写真で
        // 3 を押す等）で時刻が進むと、インポート時の新旧判定が実質の変更なしに入れ替わる。
        // ON CONFLICT ... DO UPDATE SET の右辺に現れる修飾なしの列名は「更新前の行の値」を指す
        // ので、{column} IS @val は旧値と新値の比較になる。NULL 同士を等しいと判定させるため
        // = ではなく IS（NULL 安全な等価）を使う。
        //
        // 行単位 updated_at は項目別更新時刻の最大値。時計が巻き戻ったときに後退させないよう、
        // 既存値より新しいときだけ進める。
        //
        // RETURNING で適用後の時刻をそのまま返す。呼び出し側（メモリ常駐の
        // <see cref="EvaluationTimestamps"/>）が上の CASE を再実装せずに済ませるため。
        command.CommandText =
            $"INSERT INTO image_file_metadata (file_name, {column}, {updatedAtColumn}, {ColumnUpdatedAt}) " +
            "VALUES (@fn, @val, @now, @now) " +
            "ON CONFLICT(file_name) DO UPDATE SET " +
            $"{column} = @val, " +
            $"{updatedAtColumn} = CASE WHEN {column} IS @val THEN {updatedAtColumn} ELSE @now END, " +
            $"{ColumnUpdatedAt} = CASE WHEN {column} IS @val THEN {ColumnUpdatedAt} " +
            $"WHEN {ColumnUpdatedAt} IS NULL OR {ColumnUpdatedAt} < @now THEN @now " +
            $"ELSE {ColumnUpdatedAt} END " +
            $"RETURNING {updatedAtColumn}, {ColumnUpdatedAt}";
        command.Parameters.AddWithValue("@fn", fileName);
        command.Parameters.AddWithValue("@val", value ?? DBNull.Value);
        command.Parameters.AddWithValue("@now", now);

        // RETURNING を伴う文は reader で 1 行読む（ExecuteNonQuery だと結果行を取り出せない）。
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return default;
        return new EvaluationSaveResult(ReadNullableTimestamp(reader, 0), ReadNullableTimestamp(reader, 1));
    }

    // --- 忠実な読み取り（エクスポート／インポートの突き合わせ用） ---

    // SELECT する列（file_name → 評価値 7 列 → 更新時刻 7 列 → 行単位 updated_at）。
    private static readonly string[] RecordColumns = BuildRecordColumns();

    private static string[] BuildRecordColumns()
    {
        var columns = new List<string> { "file_name" };
        columns.AddRange(ValueColumns);
        columns.AddRange(ValueColumns.Select(UpdatedAtColumn));
        columns.Add(ColumnUpdatedAt);
        return columns.ToArray();
    }

    // invalid_flag が立った行は LoadEvaluation と同様「無い」ものとして扱い、条件を一致させる。
    private static string RecordSelectSql =>
        "SELECT " + string.Join(", ", RecordColumns) +
        " FROM image_file_metadata WHERE invalid_flag = 0";

    /// <summary>
    /// 1 ファイル分の評価を更新時刻込みで読む。行が無い、DB がまだ無い、または
    /// invalid_flag が立っている場合は null（DB ファイルは作らない）。
    /// </summary>
    public EvaluationRecord? LoadRecord(string fileName)
    {
        if (!EnsureConnection(createIfMissing: false)) return null;

        using var command = _connection!.CreateCommand();
        command.CommandText = RecordSelectSql + " AND file_name = @fn";
        command.Parameters.AddWithValue("@fn", fileName);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadRecord(reader) : null;
    }

    /// <summary>
    /// 保存されている全ファイルの評価を更新時刻込みで読む（ファイル名順）。
    /// DB がまだ無ければ空（DB ファイルは作らない）。
    /// </summary>
    public IReadOnlyList<EvaluationRecord> LoadAllRecords()
    {
        var records = new List<EvaluationRecord>();
        if (!EnsureConnection(createIfMissing: false)) return records;

        using var command = _connection!.CreateCommand();
        command.CommandText = RecordSelectSql + " ORDER BY file_name";

        using var reader = command.ExecuteReader();
        while (reader.Read()) records.Add(ReadRecord(reader));
        return records;
    }

    private static EvaluationRecord ReadRecord(SQLiteDataReader reader)
    {
        var record = new EvaluationRecord(reader.GetString(reader.GetOrdinal("file_name")))
        {
            Rating = ReadNullableInt(reader, ColumnRating),
            RatingUpdatedAt = ReadNullableTimestamp(reader, UpdatedAtColumn(ColumnRating)),
            FlagRating = ReadNullableInt(reader, ColumnFlagRating),
            FlagRatingUpdatedAt = ReadNullableTimestamp(reader, UpdatedAtColumn(ColumnFlagRating)),
            UpdatedAt = ReadNullableTimestamp(reader, ColumnUpdatedAt),
        };
        foreach (var (label, column) in ColorLabelColumns)
        {
            record.SetColorLabel(label, ReadNullableInt(reader, column));
            record.SetColorLabelUpdatedAt(label, ReadNullableTimestamp(reader, UpdatedAtColumn(column)));
        }
        return record;
    }

    private static int? ReadNullableInt(SQLiteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal));

    private static int? ReadNullableInt(SQLiteDataReader reader, string column)
        => ReadNullableInt(reader, reader.GetOrdinal(column));

    private static DateTimeOffset? ReadNullableTimestamp(SQLiteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal)
            ? null
            : DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(reader.GetValue(ordinal)));

    private static DateTimeOffset? ReadNullableTimestamp(SQLiteDataReader reader, string column)
        => ReadNullableTimestamp(reader, reader.GetOrdinal(column));

    // --- スキーマ管理 ---

    private void EnsureSchema()
    {
        if (!TableExists("schema_info"))
            CreateSchemaInfoTable();

        var version = GetSchemaVersion();

        // 前方互換ガード。新しい版が書いた DB を古い版で開くと、知らない列を更新せずに
        // 値だけ書き換えて整合を壊す（例＝更新時刻を持つ DB を v1 の版が触ると時刻が嘘になる）。
        // 読み書きさせず、ここで明示的に失敗させる。
        if (version > CurrentSchemaVersion)
            throw new NotSupportedException(
                $"評価データのスキーマバージョン {version} はこのバージョンのアプリでは扱えません" +
                $"（対応は {CurrentSchemaVersion} まで）: {DatabasePath}");

        while (version < CurrentSchemaVersion)
        {
            version++;
            ApplyMigration(version);
        }
    }

    private bool TableExists(string tableName)
    {
        using var command = _connection!.CreateCommand();
        command.CommandText =
            "SELECT count(*) FROM sqlite_master WHERE type='table' AND tbl_name=@name";
        command.Parameters.AddWithValue("@name", tableName);
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    private int GetSchemaVersion()
    {
        using var command = _connection!.CreateCommand();
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
            case 2:
                Migrate_2();
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

    // 評価項目ごとの更新時刻＋行単位の更新時刻を追加する。値は Unix epoch 秒（UTC）の INTEGER。
    // 秒精度なのは、同一項目が別マシンで同じ 1 秒内に評価される確率が実質ゼロなため。
    // ALTER TABLE ADD COLUMN はテーブル書き換えを伴わないので既存 DB でも一瞬で終わり、
    // 既存行の更新時刻は NULL（＝いつ評価したか不明）のまま残る。
    private void Migrate_2() => RunInTransaction(() =>
    {
        foreach (var column in ValueColumns)
            ExecuteNonQuery(
                $"ALTER TABLE image_file_metadata ADD COLUMN {UpdatedAtColumn(column)} INTEGER");
        ExecuteNonQuery($"ALTER TABLE image_file_metadata ADD COLUMN {ColumnUpdatedAt} INTEGER");
        ExecuteNonQuery("UPDATE schema_info SET value='2' WHERE key='schema_version'");
    });

    private void RunInTransaction(Action action)
    {
        using var transaction = _connection!.BeginTransaction();
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
        using var command = _connection!.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose() => _connection?.Dispose();
}
