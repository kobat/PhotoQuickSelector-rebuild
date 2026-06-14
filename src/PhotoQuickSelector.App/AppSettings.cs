using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoQuickSelector_App;

/// <summary>
/// アプリ設定（評価データの SQLite とは別物）。最近開いたフォルダ・お気に入り・
/// 左ペインの幅／折りたたみ状態を JSON で永続化する。
///
/// 保存先は <c>%LOCALAPPDATA%\PhotoQuickSelector\settings.json</c>。
/// packaged（MSIX 開発）でも unpackaged（配布形態）でも使える素のファイルパスを用いる
/// （<see cref="Windows.Storage.ApplicationData"/> は unpackaged で例外になるため使わない）。
/// </summary>
public sealed class AppSettings
{
    /// <summary>最近開いたフォルダ（先頭が最新）。</summary>
    public List<string> RecentFolders { get; set; } = new();

    /// <summary>お気に入りフォルダ（登録順）。</summary>
    public List<string> Favorites { get; set; } = new();

    /// <summary>左ペインの幅（px）。折りたたみ前の幅を保持する。</summary>
    public double LeftPaneWidth { get; set; } = 300;

    /// <summary>左ペインが折りたたまれているか。</summary>
    public bool LeftPaneCollapsed { get; set; }

    /// <summary>最近フォルダの保持件数。</summary>
    private const int MaxRecentFolders = 15;

    [JsonIgnore]
    public bool HasRecentFolders => RecentFolders.Count > 0;

    [JsonIgnore]
    public bool HasFavorites => Favorites.Count > 0;

    // --- 操作 ---

    /// <summary>フォルダを最近一覧の先頭へ。重複は除去し、上限で切り詰める。</summary>
    public void AddRecentFolder(string path)
    {
        RecentFolders.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFolders.Insert(0, path);
        if (RecentFolders.Count > MaxRecentFolders)
            RecentFolders.RemoveRange(MaxRecentFolders, RecentFolders.Count - MaxRecentFolders);
    }

    public bool IsFavorite(string path) =>
        Favorites.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));

    public void AddFavorite(string path)
    {
        if (!IsFavorite(path)) Favorites.Add(path);
    }

    public void RemoveFavorite(string path) =>
        Favorites.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));

    // --- 永続化 ---

    // source generator のリゾルバ（トリミング安全）はそのまま、日本語パスを \uXXXX に
    // エスケープせず生の UTF-8 で書き出すよう encoder だけ差し替える。
    private static readonly JsonSerializerOptions JsonOptions =
        new(AppSettingsJsonContext.Default.Options)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

    private static string SettingsPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PhotoQuickSelector");
            return Path.Combine(dir, "settings.json");
        }
    }

    /// <summary>設定を読み込む。ファイルが無い／壊れている場合は既定値を返す。</summary>
    public static AppSettings Load()
    {
        try
        {
            var path = SettingsPath;
            if (!File.Exists(path)) return new AppSettings();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                   ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    /// <summary>設定を保存する（保存失敗は無視）。</summary>
    public void Save()
    {
        try
        {
            var path = SettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch
        {
            // 保存失敗（権限・ディスク等）はアプリ動作を止めない
        }
    }
}

/// <summary>
/// トリミング発行（Release は <c>PublishTrimmed=true</c>）でも安全なように、
/// JSON を source generator で扱う。
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class AppSettingsJsonContext : JsonSerializerContext
{
}
