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

    /// <summary>フィルムストリップ（プレビュー下部）の高さ(px)。</summary>
    public double FilmStripHeight { get; set; } = 118;

    /// <summary>プレビュー右パネル（ズームルーペ/ナビゲーター）の幅(px)。</summary>
    public double RightPanelWidth { get; set; } = 260;

    /// <summary>右パネル内ナビゲーターの高さ(px)。ズームルーペはこの残り(*)。</summary>
    public double NavigatorHeight { get; set; } = 220;

    /// <summary>プレビューのメタ情報オーバーレイ（案B / I キー）を表示するか。</summary>
    public bool ShowInfoOverlay { get; set; } = true;

    /// <summary>「リネームしてコピー」で最後に使ったファイル名テンプレート（次回の初期値）。</summary>
    public string CopyRenameTemplate { get; set; } = "{name}";

    /// <summary>前回終了時のセッション（開いていたフォルダ・選択・表示モード・フィルタ）。</summary>
    public SessionState LastSession { get; set; } = new();

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
/// 前回終了時のセッション状態。再起動時に「開いていたフォルダ／選択ファイル／表示モード／
/// フィルタ条件」を復元するためのスナップショット。フォルダ/ファイル消失等のイレギュラーは
/// 復元側（<see cref="MainViewModel"/>）が防御的に処理する。
/// </summary>
public sealed class SessionState
{
    /// <summary>開いていたフォルダ（絶対パス）。未設定なら復元しない。</summary>
    public string? FolderPath { get; set; }

    /// <summary>選択していたファイル（フォルダ相対の名前のみ。パスは持たない）。</summary>
    public string? SelectedFileName { get; set; }

    /// <summary>true=大画面プレビュー / false=サムネイルグリッド。</summary>
    public bool IsPreviewMode { get; set; }

    /// <summary>絞り込み条件のスナップショット。</summary>
    public FilterState Filter { get; set; } = new();
}

/// <summary>
/// 絞り込み条件（<see cref="ViewModels.FilterViewModel"/>）の永続化用スナップショット。
/// </summary>
public sealed class FilterState
{
    public bool Enabled { get; set; } = true;
    public int RatingValue { get; set; }
    public bool RatingGreaterEqual { get; set; } = true;
    public bool NoRating { get; set; }
    public bool FlagAccept { get; set; }
    public bool FlagNeutral { get; set; }
    public bool FlagReject { get; set; }
    public bool Red { get; set; }
    public bool Yellow { get; set; }
    public bool Green { get; set; }
    public bool Blue { get; set; }
    public bool Purple { get; set; }
}

/// <summary>
/// トリミング発行（Release は <c>PublishTrimmed=true</c>）でも安全なように、
/// JSON を source generator で扱う。
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(SessionState))]
[JsonSerializable(typeof(FilterState))]
internal partial class AppSettingsJsonContext : JsonSerializerContext
{
}
