using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
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

    /// <summary>
    /// 評価データファイル（sqlite）をまだ作成していないフォルダで、最初の評価操作時に
    /// 作成可否を確認する非同期コールバック（OK=true）。View（<see cref="MainPage"/>）が
    /// ContentDialog を出して登録する。VM は XamlRoot を持たないためコールバックで委譲する。
    /// </summary>
    public Func<Task<bool>>? ConfirmCreateAsync { get; set; }

    /// <summary>アプリ設定（最近フォルダ・お気に入り・左ペイン状態）。</summary>
    public AppSettings Settings { get; } = AppSettings.Load();

    /// <summary>読み込んだ全写真（恒久。サムネイル等の状態を保持する）。</summary>
    public ObservableCollection<PhotoItemViewModel> AllPhotos { get; } = new();

    /// <summary>絞り込み結果（<see cref="AllPhotos"/> から <see cref="Filter"/> を通した表示用ビュー）。</summary>
    public ObservableCollection<PhotoItemViewModel> Photos { get; } = new();

    /// <summary>
    /// 複数選択（一括評価の対象）の集合。焦点（<see cref="FocusedPhoto"/>）とは独立で 0..N 枚を保持する。
    /// 各メンバーは <see cref="PhotoItemViewModel.IsInSelection"/> も true になる。
    /// </summary>
    public ObservableCollection<PhotoItemViewModel> SelectedPhotos { get; } = new();

    /// <summary>Shift+←/→ レンジ選択の起点（軸）。</summary>
    private PhotoItemViewModel? _selectionPivot;

    /// <summary>
    /// 複数選択メソッドが焦点（<see cref="FocusedPhoto"/>）を動かす間 true。立っている間は
    /// <see cref="OnFocusedPhotoChanged(PhotoItemViewModel?, PhotoItemViewModel?)"/> が選択集合を
    /// リセットしないようにする（素の焦点移動・マウス選択だけが集合をクリアする）。
    /// </summary>
    private bool _managingSelection;

    /// <summary>絞り込み条件（SPEC §3-4）。変更で <see cref="ApplyFilter"/> を再実行する。</summary>
    public FilterViewModel Filter { get; } = new();

    /// <summary>フィルタフライアウトの件数表示 "絞込件数 / 全件数"。</summary>
    public string FilteredCountText => $"{Photos.Count} / {AllPhotos.Count}";

    /// <summary>左ペイン上部「お気に入り」一覧（<see cref="AppSettings.Favorites"/> の投影）。</summary>
    public ObservableCollection<FolderShortcut> Favorites { get; } = new();

    /// <summary>左ペイン上部「最近開いたフォルダ」一覧（<see cref="AppSettings.RecentFolders"/> の投影）。</summary>
    public ObservableCollection<FolderShortcut> RecentFolders { get; } = new();

    public MainViewModel()
    {
        RebuildShortcuts();
        Filter.Changed += (_, _) => ApplyFilter();
        ShowInfoOverlay = Settings.ShowInfoOverlay;
        GridKind = Settings.GridKind;
        GridReference = Settings.GridReference;
    }

    /// <summary>
    /// <see cref="AllPhotos"/> を <see cref="Filter"/> に通して <see cref="Photos"/> を作り直す。
    /// 絞り込み後もフォーカス中の写真が結果に残っていれば選択を維持する（SPEC §3-4）。
    /// </summary>
    public void ApplyFilter()
    {
        Photos.Clear();
        foreach (var item in AllPhotos)
            if (Filter.Model.Matches(item.Eval))
                Photos.Add(item);

        OnPropertyChanged(nameof(FilteredCountText));

        // 焦点と選択集合を絞り込み結果に合わせて調停する（集合は素の焦点移動で消えないようガード ON で）。
        _managingSelection = true;
        try
        {
            // アンカー（最後に焦点だった写真）が結果に残っていれば焦点を復元、外れていれば焦点解除。
            // アンカー自体は保持し続け、再び結果に入れば次回ここで復元される。
            FocusedPhoto = (_focusAnchor != null && Photos.Contains(_focusAnchor))
                ? _focusAnchor : null;

            // 絞り込みで結果から外れたメンバーは選択集合からも外す（残ったメンバーは維持）。
            for (int i = SelectedPhotos.Count - 1; i >= 0; i--)
            {
                if (!Photos.Contains(SelectedPhotos[i]))
                {
                    SelectedPhotos[i].IsInSelection = false;
                    SelectedPhotos.RemoveAt(i);
                }
            }
            if (SelectedPhotos.Count == 0) _selectionPivot = null;
        }
        finally { _managingSelection = false; }
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

    // === Reject 移動（採用フラグなし＆未評価をフォルダ配下の Reject サブフォルダへ） ===

    /// <summary>Reject 移動の対象（採用フラグなし＆未評価）。UI フィルタ非依存で全件から抽出する。</summary>
    public IReadOnlyList<PhotoItemViewModel> GetRejectTargets()
        => AllPhotos.Where(p => RejectMove.IsRejectTarget(p.Eval)).ToList();

    /// <summary>Reject サブフォルダの絶対パス（現在フォルダ配下）。</summary>
    public string RejectFolderPath
        => Path.Combine(CurrentFolder ?? "", RejectMove.RejectFolderName);

    /// <summary>
    /// 移動対象のうち、Reject フォルダに既に同名（拡張子込み）ファイルが存在するものを列挙する。
    /// Reject フォルダ未作成なら衝突なし。
    /// </summary>
    public IReadOnlyList<string> FindRejectCollisions(IEnumerable<PhotoItemViewModel> targets)
    {
        var dir = RejectFolderPath;
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        return targets
            .Select(t => t.FileName)
            .Where(name => File.Exists(Path.Combine(dir, name)))
            .ToList();
    }

    /// <summary>移動対象から Reject 移動バッチ本文を生成する。</summary>
    public string BuildRejectBatchText(IReadOnlyList<PhotoItemViewModel> targets, string generatedAt)
        => RejectMove.BuildBatch(
            CurrentFolder ?? "",
            generatedAt,
            targets.Count,
            AllPhotos.Count,
            targets.Select(p => p.FileName));

    /// <summary>Reject 移動の実行結果。</summary>
    public sealed record RejectRunResult(
        bool Success, int ExitCode, string BatPath, string LogPath, int TargetCount);

    /// <summary>
    /// Reject フォルダを作成（既存なら再利用）して bat を保存し、cmd 経由で実行する。
    /// ログは <c>Reject_yyyyMMddHHmmss.log</c> へリダイレクトする。実行後はフォルダを再読込し、
    /// 移動済みの写真を一覧から除く。
    /// </summary>
    public async Task<RejectRunResult> RunRejectBatchAsync(
        string batText, string timestamp, int targetCount)
    {
        var dir = RejectFolderPath;
        Directory.CreateDirectory(dir);   // 冪等（既存フォルダはそのまま利用）

        var batPath = Path.Combine(dir, $"Reject_{timestamp}.bat");
        var logPath = Path.Combine(dir, $"Reject_{timestamp}.log");

        // UTF-8（BOM なし）で保存。bat 先頭の chcp 65001 と整合し、日本語ファイル名にも対応。
        await File.WriteAllTextAsync(batPath, batText, new UTF8Encoding(false));

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            // bat の実行とログ出力。最初の引用符で外側の囲みを剥がす cmd の作法に合わせる。
            Arguments = $"/c \"\"{batPath}\" > \"{logPath}\" 2>&1\"",
            WorkingDirectory = dir,   // bat 内の FROMDIR=.. / TODIR=. の基準
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        int exitCode = -1;
        using (var process = Process.Start(psi))
        {
            if (process != null)
            {
                await process.WaitForExitAsync();
                exitCode = process.ExitCode;
            }
        }

        // 移動でフォルダ内容が変わったので再読込（移動済みは一覧から消える）。
        var folder = CurrentFolder;
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            await LoadFolderAsync(folder);

        return new RejectRunResult(exitCode == 0, exitCode, batPath, logPath, targetCount);
    }

    // === リネームコピー（絞込結果を任意の宛先へリネームしながらコピー） ===

    /// <summary>リネームコピーの対象（絞込結果＝現在表示中の写真）。</summary>
    public IReadOnlyList<PhotoItemViewModel> GetCopyTargets() => Photos.ToList();

    private static IReadOnlyList<CopyRename.RenameContext> ToContexts(
        IEnumerable<PhotoItemViewModel> items)
        => items.Select(p => new CopyRename.RenameContext(
            p.FileName,
            p.Meta.TakenDateTimeOffset == DateTimeOffset.MinValue
                ? null : p.Meta.TakenDateTimeOffset)).ToList();

    /// <summary>
    /// テンプレートを各対象に適用した「元名 → 新ベース名」の一覧。UI のライブプレビュー用。
    /// </summary>
    public IReadOnlyList<(string SourceName, string NewBase)> PreviewCopyNames(
        string template, IReadOnlyList<PhotoItemViewModel> items)
        => CopyRename.ResolveAll(
            template, Path.GetFileName(CurrentFolder ?? ""), ToContexts(items), out _);

    /// <summary>テンプレート適用後に新ベース名が衝突する（同名になる）ものを列挙する。</summary>
    public IReadOnlyList<string> FindCopyNameDuplicates(
        string template, IReadOnlyList<PhotoItemViewModel> items)
    {
        CopyRename.ResolveAll(
            template, Path.GetFileName(CurrentFolder ?? ""), ToContexts(items), out var dups);
        return dups;
    }

    /// <summary>リネームコピーのバッチ本文を生成する。</summary>
    public string BuildCopyRenameBatchText(
        string destDir, string template, CopyRename.OnExist policy,
        IReadOnlyList<PhotoItemViewModel> items, string generatedAt)
        => CopyRename.BuildBatch(
            CurrentFolder ?? "", destDir, template, policy, generatedAt, ToContexts(items));

    /// <summary>リネームコピーの実行結果。</summary>
    public sealed record CopyRunResult(
        bool Success, int ExitCode, string BatPath, string LogPath, int TargetCount);

    /// <summary>
    /// 宛先フォルダを作成（既存なら再利用）して bat を保存し、cmd 経由で実行する。
    /// bat・ログともに宛先フォルダ直下（<c>CopyRename_yyyyMMddHHmmss.bat/.log</c>）。
    /// コピーは元フォルダを変更しないので一覧の再読込は行わない。
    /// </summary>
    public async Task<CopyRunResult> RunCopyRenameBatchAsync(
        string batText, string destDir, string timestamp, int targetCount)
    {
        Directory.CreateDirectory(destDir);   // 冪等（既存フォルダはそのまま利用）

        var batPath = Path.Combine(destDir, $"CopyRename_{timestamp}.bat");
        var logPath = Path.Combine(destDir, $"CopyRename_{timestamp}.log");

        // UTF-8（BOM なし）で保存。bat 先頭の chcp 65001 と整合し、日本語名にも対応。
        await File.WriteAllTextAsync(batPath, batText, new UTF8Encoding(false));

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"\"{batPath}\" > \"{logPath}\" 2>&1\"",
            WorkingDirectory = destDir,   // bat 内の TODIR=. の基準
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        int exitCode = -1;
        using (var process = Process.Start(psi))
        {
            if (process != null)
            {
                await process.WaitForExitAsync();
                exitCode = process.ExitCode;
            }
        }

        return new CopyRunResult(exitCode == 0, exitCode, batPath, logPath, targetCount);
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
    }

    /// <summary>
    /// 現在のセッション（開いていたフォルダ・選択ファイル・表示モード・フィルタ）を
    /// <see cref="Settings"/> へ書き出す（実保存は終了時の <c>Settings.Save()</c> で一括）。
    /// </summary>
    public void CaptureSession()
    {
        Settings.LastSession = new SessionState
        {
            FolderPath = CurrentFolder,
            SelectedFileName = FocusedPhoto?.FileName,
            IsPreviewMode = IsPreviewMode,
            Filter = Filter.CaptureState(),
        };
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

    /// <summary>
    /// 「リネームしてコピー」で最後に指定したコピー先。アプリ起動中だけ保持する（永続化しない＝
    /// 再起動後の初期値は表示中フォルダに戻る）。OK（バッチ生成）時にのみ更新する。
    /// </summary>
    public string? LastCopyDestination { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>
    /// 焦点の写真（常に 1 枚。プレビュー表示・通常評価・ステータスバー・セッション復元を駆動）。
    /// 複数選択（一括評価の対象）の集合は <see cref="SelectedPhotos"/> を参照。
    /// </summary>
    [ObservableProperty]
    public partial PhotoItemViewModel? FocusedPhoto { get; set; }

    /// <summary>
    /// 最後に「実際に焦点だった」写真。絞り込みで <see cref="FocusedPhoto"/> が null になっても保持し、
    /// 再び結果に入れば焦点を復元する／外れている間の前後移動の基準にする。
    /// </summary>
    private PhotoItemViewModel? _focusAnchor;

    /// <summary>ステータスバーのメタ情報パネル表示（写真選択時のみ）。</summary>
    public Visibility PhotoInfoVisibility =>
        FocusedPhoto != null ? Visibility.Visible : Visibility.Collapsed;

    partial void OnFocusedPhotoChanged(PhotoItemViewModel? value) =>
        OnPropertyChanged(nameof(PhotoInfoVisibility));

    // 旧／新セルの焦点フラグを更新（フィルムストリップのディミング＋アクセント外枠の駆動）。
    partial void OnFocusedPhotoChanged(PhotoItemViewModel? oldValue, PhotoItemViewModel? newValue)
    {
        if (oldValue != null) oldValue.IsFocused = false;
        if (newValue != null)
        {
            newValue.IsFocused = true;
            _focusAnchor = newValue;  // 絞り込みで外れても保持する（再表示時の復元／外れ中の移動基準）
        }

        // 素の焦点移動（マウス選択・集合が空の前後移動・入場/読込）は選択集合をリセットする。
        // 複数選択メソッドが焦点を動かすときは _managingSelection を立てるのでここは素通り。
        if (!_managingSelection) ClearSelection();
    }

    // --- プレビュー画面（右ペインのサムネイル一覧 ⇄ 大画面プレビュー切替） ---

    [ObservableProperty]
    public partial bool IsPreviewMode { get; set; }

    /// <summary>構図グリッドの種類（SPEC §3-6 / G キーで巡回）。変更で Settings へ反映し再描画を促す。</summary>
    [ObservableProperty]
    public partial GridOverlayKind GridKind { get; set; }

    partial void OnGridKindChanged(GridOverlayKind value) =>
        Settings.GridKind = value;  // in-memory。実保存は終了時の Settings.Save() で一括。

    /// <summary>構図グリッドを描く基準（画像 / Canvas。Shift+G で切替）。</summary>
    [ObservableProperty]
    public partial GridOverlayReference GridReference { get; set; }

    partial void OnGridReferenceChanged(GridOverlayReference value) =>
        Settings.GridReference = value;

    /// <summary>構図グリッドの種類を巡回する（None→十字→三分割→正方形→None）。G キーとメニューから共用。</summary>
    public void CycleGridKind() =>
        GridKind = GridKind switch
        {
            GridOverlayKind.None => GridOverlayKind.CenterCross,
            GridOverlayKind.CenterCross => GridOverlayKind.RuleOfThirds,
            GridOverlayKind.RuleOfThirds => GridOverlayKind.Square,
            _ => GridOverlayKind.None,
        };

    /// <summary>構図グリッドの基準を切替する（画像 ⇄ Canvas）。Shift+G キーとメニューから共用。</summary>
    public void ToggleGridReference() =>
        GridReference = GridReference == GridOverlayReference.Image
            ? GridOverlayReference.Canvas
            : GridOverlayReference.Image;

    /// <summary>正方形グリッドの短辺分割数 N（設定値。Core/UI 非依存ロジックから参照）。</summary>
    public int GridSquareDivisions => Settings.GridSquareDivisions;

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
        if (FocusedPhoto == null && Photos.Count > 0)
            FocusedPhoto = Photos[0];
        if (FocusedPhoto == null) return;
        IsPreviewMode = true;
    }

    /// <summary>プレビューからサムネイル一覧へ戻る（Esc / 再ダブルクリック）。</summary>
    public void ExitPreview() => IsPreviewMode = false;

    /// <summary>
    /// 次の写真へ。選択集合があるときはメンバー内で焦点を進める／無いときは絞込ビュー内で前後移動。
    /// 複数枚あるとき末尾はそのまま。
    /// </summary>
    public void MoveNext()
    {
        if (SelectedPhotos.Count > 0) MoveFocusWithinSelection(1);
        else MoveFocus(1);
    }

    /// <summary>前の写真へ。選択集合があるときはメンバー内で焦点を戻す。先頭はそのまま。</summary>
    public void MovePrevious()
    {
        if (SelectedPhotos.Count > 0) MoveFocusWithinSelection(-1);
        else MoveFocus(-1);
    }

    /// <summary>素の焦点移動（選択集合なし）。絞込ビュー内 or アンカー基準で前後の可視写真へ。</summary>
    private void MoveFocus(int delta)
    {
        if (Photos.Count == 0) return;

        // 通常: 焦点の写真を基準に絞込ビュー内で前後移動。
        if (FocusedPhoto != null)
        {
            int index = Photos.IndexOf(FocusedPhoto);
            if (index < 0) return;
            int next = Math.Clamp(index + delta, 0, Photos.Count - 1);
            if (next != index) FocusedPhoto = Photos[next];
            return;
        }

        // 絞り込みで焦点が外れている間: もともとの写真（アンカー）の位置を基準に、
        // 絞込結果に含まれる直近の後ろ／前の写真を選ぶ（AllPhotos は表示順で安定）。
        if (_focusAnchor == null) return;
        int aIdx = AllPhotos.IndexOf(_focusAnchor);
        if (aIdx < 0) return;

        if (delta > 0)
        {
            for (int i = aIdx + 1; i < AllPhotos.Count; i++)
                if (Filter.Model.Matches(AllPhotos[i].Eval)) { FocusedPhoto = AllPhotos[i]; return; }
        }
        else
        {
            for (int i = aIdx - 1; i >= 0; i--)
                if (Filter.Model.Matches(AllPhotos[i].Eval)) { FocusedPhoto = AllPhotos[i]; return; }
        }
        // 該当方向に可視写真が無ければ何もしない（端と同じ＝空のまま）。
    }

    // === 複数選択（フィルムストリップ/グリッド共通） ===

    /// <summary>複数選択メソッドが焦点を動かすときに使う（集合をリセットしないようガードする）。</summary>
    private void SetFocusManaged(PhotoItemViewModel photo)
    {
        _managingSelection = true;
        try { FocusedPhoto = photo; }
        finally { _managingSelection = false; }
    }

    /// <summary>Shift+←/→ : レンジ起点から新しい焦点までを連続選択し、焦点も移動する。</summary>
    public void ExtendSelectionTo(int delta)
    {
        if (Photos.Count == 0 || FocusedPhoto == null) return;
        int focusIdx = Photos.IndexOf(FocusedPhoto);
        if (focusIdx < 0) return;

        if (_selectionPivot == null || !Photos.Contains(_selectionPivot))
            _selectionPivot = FocusedPhoto;

        int next = Math.Clamp(focusIdx + delta, 0, Photos.Count - 1);
        SetFocusManaged(Photos[next]);
        SetSelectionRange(_selectionPivot!, Photos[next]);
    }

    /// <summary>Ctrl+←/→ : 選択集合を変えずに焦点だけ移動する（焦点は集合外へも出られる）。</summary>
    public void MoveFocusKeepingSelection(int delta)
    {
        if (Photos.Count == 0 || FocusedPhoto == null) return;
        int idx = Photos.IndexOf(FocusedPhoto);
        if (idx < 0) return;

        // 選択集合が空の状態から動き出すときは、もともと焦点だった1枚を選択メンバーに残す
        // （焦点だけ先へ動いても、見ていた写真は選択状態のまま）。
        if (SelectedPhotos.Count == 0)
        {
            FocusedPhoto.IsInSelection = true;
            SelectedPhotos.Add(FocusedPhoto);
            _selectionPivot = FocusedPhoto;
        }

        int next = Math.Clamp(idx + delta, 0, Photos.Count - 1);
        if (next != idx) SetFocusManaged(Photos[next]);
    }

    /// <summary>Ctrl+Space : 焦点の写真を選択集合へ参加/解除し、レンジ起点を焦点に置き直す。</summary>
    public void ToggleFocusInSelection()
    {
        if (FocusedPhoto is not { } f) return;
        if (f.IsInSelection)
        {
            f.IsInSelection = false;
            SelectedPhotos.Remove(f);
        }
        else
        {
            f.IsInSelection = true;
            SelectedPhotos.Add(f);
        }
        _selectionPivot = f;
    }

    /// <summary>
    /// 選択集合がある状態の素 ←/→ : メンバー（表示順）の中で焦点を巡回する。端ではもう一方の端へ
    /// 巻き戻す（一番右で → なら一番左、一番左で ← なら一番右）。
    /// </summary>
    public void MoveFocusWithinSelection(int delta)
    {
        var ordered = Photos.Where(p => p.IsInSelection).ToList();
        if (ordered.Count == 0) { MoveFocus(delta); return; }

        int cur = FocusedPhoto != null ? ordered.IndexOf(FocusedPhoto) : -1;
        int n = ordered.Count;
        int next = cur < 0
            ? (delta > 0 ? 0 : n - 1)                       // 焦点が集合外なら端のメンバーへ
            : ((cur + delta) % n + n) % n;                  // 端で反対側へ巻き戻し
        SetFocusManaged(ordered[next]);
    }

    /// <summary>Esc : 選択集合を解除する（焦点は据え置き）。</summary>
    public void ClearSelection()
    {
        if (SelectedPhotos.Count > 0)
        {
            foreach (var p in SelectedPhotos) p.IsInSelection = false;
            SelectedPhotos.Clear();
        }
        _selectionPivot = null;
    }

    /// <summary><see cref="Photos"/> 上の a..b を選択集合にし、範囲外の旧メンバーは外す。</summary>
    private void SetSelectionRange(PhotoItemViewModel a, PhotoItemViewModel b)
    {
        int ia = Photos.IndexOf(a), ib = Photos.IndexOf(b);
        if (ia < 0 || ib < 0) return;
        if (ia > ib) (ia, ib) = (ib, ia);

        var inRange = new HashSet<PhotoItemViewModel>();
        for (int i = ia; i <= ib; i++) inRange.Add(Photos[i]);

        // 範囲外になった旧メンバーを外す。
        for (int i = SelectedPhotos.Count - 1; i >= 0; i--)
        {
            if (!inRange.Contains(SelectedPhotos[i]))
            {
                SelectedPhotos[i].IsInSelection = false;
                SelectedPhotos.RemoveAt(i);
            }
        }
        // 範囲内の未メンバーを追加する。
        for (int i = ia; i <= ib; i++)
        {
            var p = Photos[i];
            if (!p.IsInSelection)
            {
                p.IsInSelection = true;
                SelectedPhotos.Add(p);
            }
        }
    }

    /// <summary>
    /// 評価操作を <paramref name="targets"/> の各写真へ適用する（単一＝焦点1枚／一括＝選択集合の全メンバー）。
    /// 対象フォルダの sqlite がまだ無い場合は、実行前に <see cref="ConfirmCreateAsync"/> で作成可否を確認し、
    /// OK のときだけ適用する（＝ファイル生成）。キャンセル時は何もしない（ファイルも作らず評価も変えない）。
    /// 既にファイルが在れば確認なしで即適用する。確認は対象が複数でも一度だけ。
    /// </summary>
    public async Task ApplyEvaluationAsync(Action<PhotoItemViewModel> op, IReadOnlyList<PhotoItemViewModel> targets)
    {
        if (targets.Count == 0) return;
        if (_store is { DatabaseExists: false })
        {
            if (ConfirmCreateAsync == null) return;
            if (!await ConfirmCreateAsync()) return; // キャンセル＝何もしない
        }
        foreach (var t in targets) op(t);
    }

    /// <summary>
    /// フォルダ内の JPEG を読み込み、メタデータを並列抽出してサムネイル一覧を構築する。
    /// 評価は既存の <see cref="MetadataStore"/>（フォルダ内 sqlite）からマージする。
    /// </summary>
    /// <param name="folderPath">読み込むフォルダ。</param>
    /// <param name="restoreSelectedFile">
    /// 復元したい選択ファイル名（フォルダ相対）。絞込結果に在れば選択する。
    /// null（通常のフォルダ読み込み）なら従来どおり先頭を選ぶ。
    /// </param>
    /// <param name="restorePreviewMode">
    /// 復元したい表示モード（true=プレビュー / false=グリッド）。null なら従来どおりプレビューへ。
    /// </param>
    public async Task LoadFolderAsync(
        string folderPath,
        string? restoreSelectedFile = null,
        bool? restorePreviewMode = null)
    {
        if (IsLoading) return;
        IsLoading = true;
        AllPhotos.Clear();
        Photos.Clear();
        SelectedPhotos.Clear();    // 別フォルダの古い複数選択を残さない
        _selectionPivot = null;
        _focusAnchor = null;       // 別フォルダの古い写真を基準に残さない
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

            // 選択の復元: 指定ファイルが絞込結果に在れば選択する（消えた/絞り込みで外れた場合は下のフォールバック）。
            if (restoreSelectedFile != null)
            {
                var target = Photos.FirstOrDefault(p =>
                    string.Equals(p.FileName, restoreSelectedFile, StringComparison.OrdinalIgnoreCase));
                if (target != null) FocusedPhoto = target;
            }

            // 表示モードの復元。既定（null）は従来どおりプレビュー初期表示。
            // プレビュー指定時は EnterPreview() が FocusedPhoto 未設定なら先頭を選ぶ（空なら no-op でグリッドのまま）。
            if (restorePreviewMode ?? true)
                EnterPreview();
            else if (FocusedPhoto == null && Photos.Count > 0)
                FocusedPhoto = Photos[0]; // グリッド復元でも何か選んでおく

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
