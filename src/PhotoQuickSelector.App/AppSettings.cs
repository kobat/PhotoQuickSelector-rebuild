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

    /// <summary>プレビュー右パネル上段の表示（false=ルーペ / true=EXIF 詳細。E キーで切替）。</summary>
    public bool PreviewExifPanel { get; set; }

    /// <summary>プレビューの構図グリッドの種類（G キーで巡回）。</summary>
    public GridOverlayKind GridKind { get; set; } = GridOverlayKind.None;

    /// <summary>構図グリッドを描く基準（画像 / Canvas、Shift+G で切替）。</summary>
    public GridOverlayReference GridReference { get; set; } = GridOverlayReference.Image;

    /// <summary>正方形グリッドの短辺分割数 N（≧2）。セル一辺＝短辺/N。</summary>
    public int GridSquareDivisions { get; set; } = 8;

    /// <summary>「リネームしてコピー」で最後に使ったファイル名テンプレート（次回の初期値）。</summary>
    public string CopyRenameTemplate { get; set; } = "{name}";

    // --- プレビューのズーム段（一般設定） ---

    /// <summary>ズーム段の既定値（表示倍率 DeviceScale ＝ 物理px/画像px、100%=1.0）。</summary>
    public static IReadOnlyList<double> DefaultZoomStops { get; } = new[]
    {
        0.05, 0.0833, 0.125, 0.1667, 0.25, 0.3333, 0.5, 0.6667,
        1.0, 1.5, 2.0, 3.0, 4.0, 6.0, 8.0, 12.0, 16.0,
    };

    /// <summary>
    /// プレビューのホイール/キーボード（+/-）ズーム段（表示倍率＝倍率で保持。100%=1.0）。
    /// 空/無効なら <see cref="DefaultZoomStops"/> にフォールバックする（<see cref="Controls.PreviewControl"/> 側で検証）。
    /// </summary>
    public List<double> ZoomStops { get; set; } = new(DefaultZoomStops);

    // --- 先読みキャッシュ（高度な設定） ---

    /// <summary>先読みキャッシュの合計バイト予算（GB）。超過分は表示実績優先 LRU で破棄する。</summary>
    public double CacheBudgetGB { get; set; } = 2.0;

    /// <summary>先読み枚数（表示中より前方＝次に進む向き）。</summary>
    public int PrefetchForward { get; set; } = 2;

    /// <summary>先読み枚数（表示中より後方＝前に戻る向き）。</summary>
    public int PrefetchBackward { get; set; } = 2;

    /// <summary>同時に走らせるデコード本数。変更は次回起動後に反映される（Semaphore を構築時にサイズ決定するため）。</summary>
    public int MaxConcurrentDecodes { get; set; } = 2;

    /// <summary>連打抑制: 直近 <see cref="RateWindowMs"/> 内で即デコードを許す枚数。</summary>
    public int RateBudget { get; set; } = 3;

    /// <summary>連打抑制: デコード回数を数える時間窓（ミリ秒）。</summary>
    public int RateWindowMs { get; set; } = 1500;

    /// <summary>
    /// 共有（Alt+S）で起動する外部アプリの exe パス。空なら Windows 標準の共有シートにフォールバックする。
    /// 旧アプリは Google Nearby Share の固定パスだったが、本アプリでは設定化する（SPEC §6-3）。
    /// </summary>
    public string SharePath { get; set; } = "";

    /// <summary>
    /// 表示言語。空文字＝自動（OS の表示言語に追従）／"ja"＝日本語／"en"＝英語。
    /// 反映は次回起動時（<see cref="App"/> が起動直後に PrimaryLanguageOverride を設定する。
    /// override は一度設定するとプロセス内で解除できないため、自動のときは一切設定しない）。
    /// </summary>
    public string Language { get; set; } = "";

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
/// JSON を source generator で扱う（リフレクション非依存）。現在は全構成 <c>PublishTrimmed=false</c>
/// （WinUI/Win2D はトリミング不可）だが、将来の構成変更にも安全なように維持する。
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(SessionState))]
[JsonSerializable(typeof(FilterState))]
internal partial class AppSettingsJsonContext : JsonSerializerContext
{
}
