using System.Runtime.InteropServices.WindowsRuntime;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoQuickSelector.Core;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using Windows.UI;

namespace PhotoQuickSelector_App.ViewModels;

/// <summary>
/// サムネイル 1 枚分のビューモデル。メタデータ（不変）＋評価（可変）＋サムネイル画像を束ね、
/// 評価変更は即時に <see cref="MetadataStore"/> へ永続化する。
/// </summary>
public partial class PhotoItemViewModel : ObservableObject
{
    private readonly MetadataStore _store;

    public ImageMetadata Meta { get; }
    public PhotoEvaluation Eval { get; }

    public PhotoItemViewModel(ImageMetadata meta, PhotoEvaluation eval, MetadataStore store)
    {
        Meta = meta;
        Eval = eval;
        _store = store;
    }

    public string FileName => Meta.FileName;

    public string InfoLine =>
        string.Join("  ", new[]
        {
            Meta.FocalLengthDescription, Meta.ApertureDescription,
            Meta.ExposureTimeDescription, Meta.IsoDescription
        }.Where(s => !string.IsNullOrEmpty(s)));

    // --- メタ情報オーバーレイ（案B）用の表示プロパティ ---

    public string ImageSizeText => Meta.ImageSizeDescription;

    public string FileSizeText => $"{Meta.FileSize / 1024.0 / 1024.0:0.0}MB";

    public string TakenDateTimeText => Meta.TakenDateTimeDescription;

    /// <summary>カメラ・レンズ名（"SONY ILCE-1 / SONY FE 50mm…"）。空要素は省く。
    /// レンズはメーカー名（<see cref="ImageMetadata.LensMake"/>）をモデル名の前に付ける。</summary>
    public string CameraLensText
    {
        get
        {
            var body = string.Join(" ", new[] { Meta.CameraMaker, Meta.CameraModel }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            var lens = string.Join(" ", new[] { Meta.LensMake, Meta.LensModel }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            if (string.IsNullOrWhiteSpace(lens)) return body;
            return string.IsNullOrWhiteSpace(body) ? lens : $"{body} / {lens}";
        }
    }

    /// <summary>EXIF 撮影設定チップ（焦点距離/絞り/SS/露出補正/ISO、空は除外）。</summary>
    public IReadOnlyList<string> ExifChips =>
        new[]
        {
            Meta.FocalLengthDescription, Meta.ApertureDescription,
            Meta.ExposureTimeDescription, Meta.ExposureBiasDescription, Meta.IsoDescription
        }.Where(s => !string.IsNullOrEmpty(s)).ToList();

    // --- GPS（撮影位置）。地図ボタンの表示出し分けとツールチップ／地図リンクに使う ---

    /// <summary>GPS 位置情報を持つか。地図ボタンの表示可否。</summary>
    public bool HasGps => Meta.HasGpsLocation;

    public Visibility GpsVisibility => HasGps ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>地図ボタンのツールチップ（DMS 座標）。</summary>
    public string GpsTooltip =>
        HasGps ? Loc.Get("Gps_Tooltip", Meta.GpsLocationDescription) : "";

    /// <summary>撮影位置を開く地図 URL。十進緯度経度が無ければ null。</summary>
    public Uri? MapUri =>
        Meta is { GpsLatitude: { } lat, GpsLongitude: { } lon }
            ? new Uri(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "https://www.google.com/maps/search/?api=1&query={0},{1}", lat, lon))
            : null;

    /// <summary>
    /// 全件常駐させる圧縮サムネイル（シェルの JPEG バイトをそのまま保持＝1 枚 ~30KB）。
    /// 重い非圧縮 <see cref="BitmapImage"/> は画面に見えているコンテナ分だけ
    /// <see cref="CreateThumbnailImageAsync"/> で都度生成し、リサイクル時に破棄する。
    /// </summary>
    private byte[]? _thumbnailBytes;

    /// <summary>バイト取得中の共有タスク（in-flight 重複排除）。完了後は <see cref="_thumbnailBytes"/> がガード。</summary>
    private Task? _loadBytesTask;

    /// <summary>
    /// EXIF 詳細パネル用の全タグ（全ディレクトリ）。一度解析したら常駐させ、往復での再解析をゼロにする
    /// （<see cref="_thumbnailBytes"/> と同じ「一度だけ取得して持ち続ける」パターン）。フォルダ再読込で
    /// Photos ごと破棄されるため別途 LRU は不要。破損・未対応ファイルは空リストが入る（無限リトライしない）。
    /// </summary>
    private IReadOnlyList<ExifTagGroup>? _exifGroups;

    /// <summary>EXIF 解析中の共有タスク（in-flight 重複排除）。完了後は <see cref="_exifGroups"/> がガード。</summary>
    private Task<IReadOnlyList<ExifTagGroup>>? _exifLoadTask;

    /// <summary>常駐済みの EXIF 全タグ。未解析なら null（呼び出し側が同期で「即表示できるか」を判定する）。</summary>
    public IReadOnlyList<ExifTagGroup>? CachedExifGroups => _exifGroups;

    /// <summary>
    /// EXIF 全タグを一度だけ解析して常駐させる。以後の呼び出しはキャッシュを返す（再解析ゼロ）。
    /// 解析（ファイル I/O＋パース）は <see cref="ExifTagReader.ReadAllTags"/> ＝バックグラウンドスレッドで行い、
    /// UI スレッドを塞がない。同一写真への同時呼び出しは 1 本の解析タスクを共有する（in-flight 重複排除）。
    /// 呼び出しは UI スレッドからのみ（プレビューの焦点変更）なのでロック不要。
    /// </summary>
    public Task<IReadOnlyList<ExifTagGroup>> EnsureExifGroupsAsync()
    {
        if (_exifGroups != null) return Task.FromResult(_exifGroups);
        return _exifLoadTask ??= LoadExifCoreAsync();
    }

    private async Task<IReadOnlyList<ExifTagGroup>> LoadExifCoreAsync()
    {
        string path = Meta.Path;
        try
        {
            // ReadAllTags は throw せず、破損・未対応時は空リストを返す（＝空でも常駐＝再試行しない）。
            var groups = await Task.Run(() => ExifTagReader.ReadAllTags(path)).ConfigureAwait(false);
            _exifGroups = groups;
            return groups;
        }
        finally
        {
            // 成功時は _exifGroups が以後のガード。タスク参照は解放しておく。
            _exifLoadTask = null;
        }
    }

    // レーティング★のブラシ（不透明のまま RGB で濃淡を表現）
    private static readonly Brush NormalRatingBrush = new SolidColorBrush(Colors.Gold);            // #FFD700
    private static readonly Brush ExifRatingBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xF2, 0xE2, 0xA8)); // 淡い金

    // 評価の表示用プロパティ（★はレーティング数）
    public string RatingStars => Eval.Rating > 0 ? new string('★', Eval.Rating) : "";

    /// <summary>レーティングが EXIF(xmp:Rating) 由来で、まだユーザー変更されていないか。</summary>
    public bool IsRatingFromExif => Eval.PersistedRating == null && Eval.Rating > 0;

    /// <summary>EXIF 由来は薄め色、ユーザー変更済みは通常の金色。</summary>
    public Brush RatingForeground => IsRatingFromExif ? ExifRatingBrush : NormalRatingBrush;

    // フラグは状態を可視性で表現（採用=旗 / 拒否=×）
    public Visibility PickVisibility => Eval.FlagRating > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RejectVisibility => Eval.FlagRating < 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>フラグ（採用/拒否）バッジ全体の表示可否。中立(0)なら隠す。</summary>
    public Visibility FlagVisibility => Eval.FlagRating != 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RedVisibility => Vis(ColorLabel.Red);
    public Visibility YellowVisibility => Vis(ColorLabel.Yellow);
    public Visibility GreenVisibility => Vis(ColorLabel.Green);
    public Visibility BlueVisibility => Vis(ColorLabel.Blue);
    public Visibility PurpleVisibility => Vis(ColorLabel.Purple);

    private Visibility Vis(ColorLabel label) =>
        Eval.HasColorLabel(label) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>レーティングがあるとき、下部中央の★を表示する。</summary>
    public Visibility RatingVisibility =>
        Eval.Rating > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>いずれかのカラーラベルがあるとき、右上の色ドットを表示する。</summary>
    public Visibility ColorDotsVisibility =>
        HasAnyColorLabel ? Visibility.Visible : Visibility.Collapsed;

    private bool HasAnyColorLabel =>
        ColorLabelOrder.Any(Eval.HasColorLabel);

    /// <summary>
    /// 評価バッジオーバーレイ（<see cref="Controls.PreviewControl"/> の RatingBadge）の表示可否。
    /// レーティング・フラグ・カラーラベルのいずれも無ければバッジごと隠す。
    /// </summary>
    public Visibility HasAnyEvalVisibility =>
        (Eval.Rating > 0 || Eval.FlagRating != 0 || HasAnyColorLabel)
            ? Visibility.Visible : Visibility.Collapsed;

    // カラーラベルの色（XAML の楕円 Fill と一致）。枠線色の決定にも使う。
    private static readonly ColorLabel[] ColorLabelOrder =
        { ColorLabel.Red, ColorLabel.Yellow, ColorLabel.Green, ColorLabel.Blue, ColorLabel.Purple };

    private static readonly Brush TransparentBrush = new SolidColorBrush(Colors.Transparent);
    private static readonly IReadOnlyDictionary<ColorLabel, Brush> ColorLabelBrushes =
        new Dictionary<ColorLabel, Brush>
        {
            [ColorLabel.Red] = new SolidColorBrush(Color.FromArgb(0xFF, 0xE5, 0x39, 0x35)),
            [ColorLabel.Yellow] = new SolidColorBrush(Color.FromArgb(0xFF, 0xFD, 0xD8, 0x35)),
            [ColorLabel.Green] = new SolidColorBrush(Color.FromArgb(0xFF, 0x43, 0xA0, 0x47)),
            [ColorLabel.Blue] = new SolidColorBrush(Color.FromArgb(0xFF, 0x1E, 0x88, 0xE5)),
            [ColorLabel.Purple] = new SolidColorBrush(Color.FromArgb(0xFF, 0x8E, 0x24, 0xAA)),
        };

    /// <summary>
    /// 枠線の色。付与されたカラーラベルのうち enum 順で最初のもの。無ければ透明（枠スペースは確保）。
    /// 複数ラベル時は下部の色ドットで全色を補完表示する。
    /// </summary>
    public Brush ColorLabelBorderBrush
    {
        get
        {
            foreach (var label in ColorLabelOrder)
                if (Eval.HasColorLabel(label))
                    return ColorLabelBrushes[label];
            return TransparentBrush;
        }
    }

    // --- フィルムストリップ/グリッドの強調（焦点＝1枚 ／ 選択集合＝0..N枚は別概念） ---

    /// <summary>
    /// このセルが現在の焦点（プレビュー表示・通常評価の対象＝常に1枚）かどうか。
    /// <see cref="MainViewModel.FocusedPhoto"/> 変更時に旧／新セルへ設定される。フィルムストリップの
    /// テンプレートが不透明度・アクセント外枠の切替に使う。一括評価の対象となる
    /// <see cref="IsInSelection"/>（選択集合のメンバー）とは独立。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CellOpacity))]
    [NotifyPropertyChangedFor(nameof(FocusFrameOpacity))]
    public partial bool IsFocused { get; set; }

    /// <summary>
    /// このセルが複数選択（一括評価の対象）のメンバーかどうか。<see cref="MainViewModel.SelectedPhotos"/>
    /// の出入りで設定される。焦点（<see cref="IsFocused"/>）とは独立で、焦点はメンバーの中の1枚になる
    /// （ただし Ctrl+矢印で焦点だけ集合外へ動く瞬間はメンバー外になり得る）。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CellOpacity))]
    [NotifyPropertyChangedFor(nameof(SelectionHighlightOpacity))]
    public partial bool IsInSelection { get; set; }

    /// <summary>
    /// セル全体の不透明度。焦点またはメンバーは全不透明（1.0）、いずれでもないセルは淡く（0.9）して
    /// 焦点/選択を際立たせる。
    /// </summary>
    public double CellOpacity => IsFocused || IsInSelection ? 1.0 : 0.9;

    /// <summary>
    /// 焦点のアクセント外枠（グレー）の不透明度。枠はレイアウトを動かさないよう常設し可視性だけ切替える。
    /// 焦点=1.0／非焦点=0.0。
    /// </summary>
    public double FocusFrameOpacity => IsFocused ? 1.0 : 0.0;

    /// <summary>
    /// 選択集合メンバーのハイライト外枠（アンバー）の不透明度。焦点リングとは別レイヤで併存させる。
    /// メンバー=1.0／非メンバー=0.0。
    /// </summary>
    public double SelectionHighlightOpacity => IsInSelection ? 1.0 : 0.0;

    // 評価操作（永続化込み）
    public void SetRating(int value)
    {
        Eval.SetRating(value);
        _store.SaveRating(FileName, Eval.PersistedRating);
        OnPropertyChanged(nameof(RatingStars));
        OnPropertyChanged(nameof(RatingForeground)); // EXIF由来→ユーザー変更で色が変わる
        OnPropertyChanged(nameof(RatingVisibility));
        OnPropertyChanged(nameof(HasAnyEvalVisibility));
    }

    public void RatingUp() => SetRating(Eval.Rating + 1);
    public void RatingDown() => SetRating(Eval.Rating - 1);

    public void FlagUp()
    {
        Eval.FlagUp();
        _store.SaveFlagRating(FileName, Eval.PersistedFlagRating);
        NotifyFlag();
    }

    public void FlagDown()
    {
        Eval.FlagDown();
        _store.SaveFlagRating(FileName, Eval.PersistedFlagRating);
        NotifyFlag();
    }

    /// <summary>フラグを直接設定（拒否 -1 / 中立 0 / 採用 +1）。右クリックメニューからの一発指定用。</summary>
    public void SetFlag(int value)
    {
        Eval.SetFlag(value);
        _store.SaveFlagRating(FileName, Eval.PersistedFlagRating);
        NotifyFlag();
    }

    private void NotifyFlag()
    {
        OnPropertyChanged(nameof(PickVisibility));
        OnPropertyChanged(nameof(RejectVisibility));
        OnPropertyChanged(nameof(FlagVisibility));
        OnPropertyChanged(nameof(HasAnyEvalVisibility));
    }

    public void ToggleColorLabel(ColorLabel label)
    {
        Eval.ToggleColorLabel(label);
        _store.SaveColorLabel(FileName, label, Eval.GetPersistedColorLabel(label));
        OnPropertyChanged($"{label}Visibility");
        OnPropertyChanged(nameof(ColorLabelBorderBrush));
        OnPropertyChanged(nameof(ColorDotsVisibility));
        OnPropertyChanged(nameof(HasAnyEvalVisibility));
    }

    /// <summary>
    /// 付与されているカラーラベルをすべて解除する（右クリックメニュー「クリア」用）。
    /// 保存・通知は既存の <see cref="ToggleColorLabel"/> を色ごとに呼んで委ねる（重複実装を避ける）。
    /// </summary>
    public void ClearColorLabels()
    {
        foreach (var label in ColorLabelOrder)
            if (Eval.HasColorLabel(label))
                ToggleColorLabel(label);
    }

    /// <summary>
    /// OS のシェルサムネイル（JPEG）を一度だけ取得し、圧縮バイトのまま常駐させる。
    /// デコード（BitmapImage 化）はしないので軽量。再呼び出しは何もしない。
    /// 同一写真への同時呼び出しは 1 本の取得タスクを共有する（in-flight 重複排除）。
    /// </summary>
    public Task EnsureThumbnailBytesAsync()
    {
        if (_thumbnailBytes != null) return Task.CompletedTask;
        // 呼び出しは UI スレッドからのみ（先読みループ／コンテナ実体化）なのでロック不要。
        return _loadBytesTask ??= LoadBytesCoreAsync();
    }

    /// <summary>
    /// シェルサムネイル抽出（<see cref="StorageFile.GetThumbnailAsync"/>）を **アプリ全体で直列化**するゲート。
    /// この抽出は内部で Windows シェル/WIC を使うが、UI スレッドとバックグラウンド（先読み）から
    /// **同時に**呼ぶと imaging 層が `Microsoft.UI.Xaml.dll` 経由で fail-fast する（実機で確認）。
    /// 1 本に直列化して同時アクセスを防ぐ。I/O 自体はバックグラウンドのままなので UI は固まらない。
    /// </summary>
    private static readonly System.Threading.SemaphoreSlim _shellGate = new(1, 1);

    private async Task LoadBytesCoreAsync()
    {
        try
        {
            await _shellGate.WaitAsync().ConfigureAwait(false);
            try
            {
                // I/O（特に冷えたシェルキャッシュの高解像度デコード）は UI スレッドへ戻さない。
                var file = await StorageFile.GetFileFromPathAsync(Meta.Path).AsTask().ConfigureAwait(false);
                // SingleItem＝アスペクト比保持＋EXIF Orientation 適用。正方形セル＋Uniform で横は上下・縦は
                // 左右に帯が出て向きが判る。PicturesView は正方形寄りにクロップされ向きが潰れるため使わない。
                using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 320)
                    .AsTask().ConfigureAwait(false);
                if (thumbnail == null || thumbnail.Size == 0) return;

                var size = (uint)thumbnail.Size;
                using var reader = new DataReader(thumbnail.GetInputStreamAt(0));
                await reader.LoadAsync(size).AsTask().ConfigureAwait(false);
                var bytes = new byte[size];
                reader.ReadBytes(bytes);
                _thumbnailBytes = bytes;
            }
            finally { _shellGate.Release(); }
        }
        catch
        {
            // サムネイル取得失敗は無視（画像なしで表示）
        }
        finally
        {
            // 成功時は _thumbnailBytes が以後のガード。失敗時は次回再試行できるよう解放。
            _loadBytesTask = null;
        }
    }

    /// <summary>
    /// 常駐している圧縮バイトから表示用 <see cref="BitmapImage"/> を生成する。
    /// 可視コンテナの分だけ呼ばれ、コンテナのリサイクル時に破棄される想定。
    /// <paramref name="decodePixelWidth"/> で非圧縮サーフェスを表示サイズへ抑える。
    /// </summary>
    public async Task<BitmapImage?> CreateThumbnailImageAsync(int decodePixelWidth)
    {
        await EnsureThumbnailBytesAsync();
        var bytes = _thumbnailBytes;
        if (bytes == null) return null;
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);

            var bitmap = new BitmapImage { DecodePixelWidth = decodePixelWidth };
            await bitmap.SetSourceAsync(stream);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
