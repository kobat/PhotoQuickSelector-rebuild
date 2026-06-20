using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using PhotoQuickSelector.Core;

namespace PhotoQuickSelector_App.ViewModels;

/// <summary>
/// メイン画面のビューモデル。フォルダ読み込み（メタデータ並列抽出＋評価のマージ）と
/// 右ペインのサムネイル一覧、およびアプリ設定（最近/お気に入り）を管理する。
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private MetadataStore? _store;

    /// <summary>アプリ設定（最近フォルダ・お気に入り・左ペイン状態）。</summary>
    public AppSettings Settings { get; } = AppSettings.Load();

    /// <summary>読み込んだ全写真（恒久。サムネイル等の状態を保持する）。</summary>
    public ObservableCollection<PhotoItemViewModel> AllPhotos { get; } = new();

    /// <summary>絞り込み結果（<see cref="AllPhotos"/> から <see cref="Filter"/> を通した表示用ビュー）。</summary>
    public ObservableCollection<PhotoItemViewModel> Photos { get; } = new();

    /// <summary>絞り込み条件（SPEC §3-4）。変更で <see cref="ApplyFilter"/> を再実行する。</summary>
    public FilterViewModel Filter { get; } = new();

    /// <summary>フィルタフライアウトの件数表示 "絞込件数 / 全件数"。</summary>
    public string FilteredCountText => $"{Photos.Count} / {AllPhotos.Count}";

    /// <summary>左ペイン上部「お気に入り」一覧（<see cref="AppSettings.Favorites"/> の投影）。</summary>
    public ObservableCollection<FolderShortcut> Favorites { get; } = new();

    /// <summary>左ペイン上部「最近開いたフォルダ」一覧（<see cref="AppSettings.RecentFolders"/> の投影）。</summary>
    public ObservableCollection<FolderShortcut> RecentFolders { get; } = new();

    public Visibility FavoritesVisibility =>
        Favorites.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RecentVisibility =>
        RecentFolders.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public MainViewModel()
    {
        RebuildShortcuts();
        Filter.Changed += (_, _) => ApplyFilter();
        ShowInfoOverlay = Settings.ShowInfoOverlay;
    }

    /// <summary>
    /// <see cref="AllPhotos"/> を <see cref="Filter"/> に通して <see cref="Photos"/> を作り直す。
    /// 絞り込み後もフォーカス中の写真が結果に残っていれば選択を維持する（SPEC §3-4）。
    /// </summary>
    public void ApplyFilter()
    {
        var previous = SelectedPhoto;

        Photos.Clear();
        foreach (var item in AllPhotos)
            if (Filter.Model.Matches(item.Eval))
                Photos.Add(item);

        OnPropertyChanged(nameof(FilteredCountText));

        // 絞り込みで対象から外れた場合は選択を解除（残っていればそのまま）。
        SelectedPhoto = (previous != null && Photos.Contains(previous)) ? previous : null;
    }

    /// <summary>絞込結果のファイル名一覧テキスト（クリップボード用、SPEC §3-5）。</summary>
    public string BuildFileNameListText() =>
        ClipboardExport.BuildFileNameList(Photos.Select(p => p.FileName));

    /// <summary>採用（絞込結果）写真を移動する .bat スクリプト（クリップボード用、SPEC §3-5）。</summary>
    public string BuildMoveBatchText() =>
        ClipboardExport.BuildMoveBatch(
            CurrentFolder ?? "",
            Photos.Count,
            AllPhotos.Count,
            Filter.Model.DescribeConditions(),
            Photos.Select(p => p.FileName));

    /// <summary>設定の最近/お気に入りから表示用コレクションを作り直す。</summary>
    private void RebuildShortcuts()
    {
        Favorites.Clear();
        foreach (var path in Settings.Favorites)
            Favorites.Add(new FolderShortcut(path));

        RecentFolders.Clear();
        foreach (var path in Settings.RecentFolders)
            RecentFolders.Add(new FolderShortcut(path));

        OnPropertyChanged(nameof(FavoritesVisibility));
        OnPropertyChanged(nameof(RecentVisibility));
    }

    public bool IsFavorite(string path) => Settings.IsFavorite(path);

    /// <summary>お気に入りの登録/解除を切り替え、即時保存する。</summary>
    public void ToggleFavorite(string path)
    {
        if (Settings.IsFavorite(path)) Settings.RemoveFavorite(path);
        else Settings.AddFavorite(path);
        Settings.Save();
        RebuildShortcuts();
    }

    public void RemoveFavorite(string path)
    {
        Settings.RemoveFavorite(path);
        Settings.Save();
        RebuildShortcuts();
    }

    public void RemoveRecentFolder(string path)
    {
        Settings.RecentFolders.RemoveAll(
            p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        Settings.Save();
        RebuildShortcuts();
    }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "左のツリーでフォルダを選び、「読み込み」ボタンで開きます。";

    [ObservableProperty]
    public partial string? CurrentFolder { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial PhotoItemViewModel? SelectedPhoto { get; set; }

    /// <summary>ステータスバーのメタ情報パネル表示（写真選択時のみ）。</summary>
    public Visibility PhotoInfoVisibility =>
        SelectedPhoto != null ? Visibility.Visible : Visibility.Collapsed;

    partial void OnSelectedPhotoChanged(PhotoItemViewModel? value) =>
        OnPropertyChanged(nameof(PhotoInfoVisibility));

    // --- プレビュー画面（右ペインのサムネイル一覧 ⇄ 大画面プレビュー切替） ---

    [ObservableProperty]
    public partial bool IsPreviewMode { get; set; }

    /// <summary>三分割グリッド線オーバーレイの表示（SPEC §3-6 / §3-7 の G キー）。</summary>
    [ObservableProperty]
    public partial bool ShowGrid { get; set; }

    /// <summary>プレビュー左上のメタ情報オーバーレイ（案B / I キーでトグル）。</summary>
    [ObservableProperty]
    public partial bool ShowInfoOverlay { get; set; }

    public Visibility InfoOverlayVisibility =>
        ShowInfoOverlay ? Visibility.Visible : Visibility.Collapsed;

    partial void OnShowInfoOverlayChanged(bool value)
    {
        Settings.ShowInfoOverlay = value;  // in-memory。実保存は終了時の Settings.Save() で一括。
        OnPropertyChanged(nameof(InfoOverlayVisibility));
    }

    public Visibility ThumbnailVisibility => IsPreviewMode ? Visibility.Collapsed : Visibility.Visible;
    public Visibility PreviewVisibility => IsPreviewMode ? Visibility.Visible : Visibility.Collapsed;

    partial void OnIsPreviewModeChanged(bool value)
    {
        OnPropertyChanged(nameof(ThumbnailVisibility));
        OnPropertyChanged(nameof(PreviewVisibility));
        OnPropertyChanged(nameof(ZoomVisibility));
    }

    /// <summary>プレビューの現在倍率（物理px / 画像px ＝ <c>PreviewViewport.DeviceScale</c>）。
    /// プレビュー側がズーム/パン/ロードのたびに更新する。ピクセル等倍で 1.0（＝100%）。</summary>
    [ObservableProperty]
    public partial double ZoomScale { get; set; } = 1.0;

    /// <summary>ステータスバーに出す倍率テキスト（ピクセル等倍＝100%）。</summary>
    public string ZoomText => $"{ZoomScale * 100:0}%";

    /// <summary>倍率表示はプレビュー時のみ。</summary>
    public Visibility ZoomVisibility =>
        IsPreviewMode ? Visibility.Visible : Visibility.Collapsed;

    partial void OnZoomScaleChanged(double value) => OnPropertyChanged(nameof(ZoomText));

    /// <summary>サムネイルをダブルクリック等でプレビュー（大画面）へ遷移する。</summary>
    public void EnterPreview()
    {
        if (SelectedPhoto == null && Photos.Count > 0)
            SelectedPhoto = Photos[0];
        if (SelectedPhoto == null) return;
        IsPreviewMode = true;
    }

    /// <summary>プレビューからサムネイル一覧へ戻る（Esc / 再ダブルクリック）。</summary>
    public void ExitPreview() => IsPreviewMode = false;

    /// <summary>次の写真へ。複数枚あるとき末尾はそのまま。</summary>
    public void MoveNext() => MoveBy(1);

    /// <summary>前の写真へ。先頭はそのまま。</summary>
    public void MovePrevious() => MoveBy(-1);

    private void MoveBy(int delta)
    {
        if (SelectedPhoto == null) return;
        int index = Photos.IndexOf(SelectedPhoto);
        if (index < 0) return;
        int next = Math.Clamp(index + delta, 0, Photos.Count - 1);
        if (next != index) SelectedPhoto = Photos[next];
    }

    /// <summary>
    /// フォルダ内の JPEG を読み込み、メタデータを並列抽出してサムネイル一覧を構築する。
    /// 評価は既存の <see cref="MetadataStore"/>（フォルダ内 sqlite）からマージする。
    /// </summary>
    public async Task LoadFolderAsync(string folderPath)
    {
        if (IsLoading) return;
        IsLoading = true;
        AllPhotos.Clear();
        Photos.Clear();
        OnPropertyChanged(nameof(FilteredCountText));
        CurrentFolder = folderPath;
        StatusText = $"読み込み中: {folderPath}";

        try
        {
            // 1) 対象ファイル列挙（JPEG のみ）
            var paths = Directory.GetFiles(folderPath)
                .Where(MetadataReader.IsSupported)
                .ToArray();

            // 列挙に成功＝有効なフォルダ。最近一覧へ記録して永続化する。
            Settings.AddRecentFolder(folderPath);
            Settings.Save();
            RebuildShortcuts();

            if (paths.Length == 0)
            {
                StatusText = $"JPEG が見つかりません: {folderPath}";
                return;
            }

            // 2) メタデータを並列抽出（CPU バウンドなのでバックグラウンドで）
            var metas = await Task.Run(() =>
            {
                var result = new ImageMetadata?[paths.Length];
                Parallel.For(0, paths.Length, i =>
                {
                    try { result[i] = MetadataReader.Read(paths[i]); }
                    catch { result[i] = null; }
                });
                return result
                    .Where(m => m != null)
                    .Select(m => m!)
                    .OrderBy(m => m.TakenDateTimeOffset == DateTimeOffset.MinValue ? 1 : 0)
                    .ThenBy(m => m.TakenDateTimeOffset)
                    .ThenBy(m => m.FileName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            });

            // 3) 評価ストア（フォルダ内 sqlite）を開き、評価をマージして VM 化
            _store?.Dispose();
            _store = new MetadataStore(folderPath);

            foreach (var meta in metas)
            {
                var eval = _store.LoadEvaluation(meta.FileName, meta.ExifRating);
                AllPhotos.Add(new PhotoItemViewModel(meta, eval, _store));
            }

            ApplyFilter();
            StatusText = $"{AllPhotos.Count} 枚  ({folderPath})";

            // 読み込み直後は大画面プレビューを初期表示にする（先頭写真を選択。空なら従来どおりグリッド）。
            EnterPreview();

            // 4) サムネイル（圧縮バイト）を順次先読み（UI を塞がない）。世代トークンで古い読込を中断。
            //    デコード（BitmapImage 化）は表示中のコンテナ分だけ行うのでここでは軽量。
            _ = LoadThumbnailsAsync(++_loadGeneration);
        }
        catch (Exception ex)
        {
            StatusText = $"読み込みエラー: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private int _loadGeneration;

    /// <summary>背景先読みの中心（最後に画面へ実体化されたサムネイルのインデックス）。</summary>
    private int _prefetchAnchor;

    /// <summary>
    /// グリッドが項目を実体化したときに呼ぶ。背景先読みをこの近傍から進めるためのアンカー。
    /// 中央へジャンプしてもその周辺のバイトが優先的に揃う。
    /// </summary>
    public void NotePrefetchAnchor(int index) => _prefetchAnchor = index;

    private async Task LoadThumbnailsAsync(int generation)
    {
        // 全件分のバイトを先読み（デコードはしない）。別フォルダを開いて世代が進んだら中断。
        // 0..N の固定順ではなく、現在のアンカー（可視範囲）から外側へ広げて読むので、
        // 直後に中央へジャンプしても近傍が優先される。絞り込み中でも全件読む（フィルタ変更で再ロード不要）。
        var items = AllPhotos.ToArray();
        int n = items.Length;
        if (n == 0) return;

        var attempted = new bool[n];
        for (int done = 0; done < n; done++)
        {
            if (generation != _loadGeneration) break;
            int anchor = Math.Clamp(_prefetchAnchor, 0, n - 1);
            int idx = NearestUnattempted(attempted, anchor, n);
            if (idx < 0) break;
            attempted[idx] = true;
            await items[idx].EnsureThumbnailBytesAsync().ConfigureAwait(false);
        }
    }

    /// <summary>アンカーから外側（anchor, +1, -1, +2, -2 …）へ広げ、未試行で最も近い添字を返す。無ければ -1。</summary>
    private static int NearestUnattempted(bool[] attempted, int anchor, int n)
    {
        for (int r = 0; r < n; r++)
        {
            int hi = anchor + r;
            if (hi < n && !attempted[hi]) return hi;
            int lo = anchor - r;
            if (r > 0 && lo >= 0 && !attempted[lo]) return lo;
        }
        return -1;
    }
}
