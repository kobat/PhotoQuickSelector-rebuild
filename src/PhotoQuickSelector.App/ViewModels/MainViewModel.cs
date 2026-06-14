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

    public ObservableCollection<PhotoItemViewModel> Photos { get; } = new();

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
    }

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

    /// <summary>
    /// フォルダ内の JPEG を読み込み、メタデータを並列抽出してサムネイル一覧を構築する。
    /// 評価は既存の <see cref="MetadataStore"/>（フォルダ内 sqlite）からマージする。
    /// </summary>
    public async Task LoadFolderAsync(string folderPath)
    {
        if (IsLoading) return;
        IsLoading = true;
        Photos.Clear();
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
                Photos.Add(new PhotoItemViewModel(meta, eval, _store));
            }

            StatusText = $"{Photos.Count} 枚  ({folderPath})";

            // 4) サムネイルを順次読み込み（UI を塞がない）。世代トークンで古い読込を中断。
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

    private async Task LoadThumbnailsAsync(int generation)
    {
        // スナップショットを順次ロード。別フォルダを開いて世代が進んだら中断。
        foreach (var item in Photos.ToArray())
        {
            if (generation != _loadGeneration) break;
            await item.LoadThumbnailAsync();
        }
    }
}
