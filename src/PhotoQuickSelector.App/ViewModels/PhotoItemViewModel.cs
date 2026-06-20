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

    /// <summary>カメラ・レンズ名（"SONY ILCE-1 / FE 50mm…"）。空要素は省く。</summary>
    public string CameraLensText
    {
        get
        {
            var body = string.Join(" ", new[] { Meta.CameraMaker, Meta.CameraModel }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            if (string.IsNullOrWhiteSpace(Meta.LensModel)) return body;
            return string.IsNullOrWhiteSpace(body) ? Meta.LensModel : $"{body} / {Meta.LensModel}";
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
        HasGps ? $"地図で表示（{Meta.GpsLocationDescription}）" : "";

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

    // --- フィルムストリップの選択強調（案1 ディミング ＋ 案2 アクセント外枠） ---

    /// <summary>
    /// このセルが現在の選択写真かどうか。<see cref="MainViewModel.SelectedPhoto"/> 変更時に
    /// 旧／新セルへ設定される。フィルムストリップのテンプレートが不透明度の切替に使う。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectionOpacity))]
    [NotifyPropertyChangedFor(nameof(SelectionFrameOpacity))]
    public partial bool IsSelected { get; set; }

    /// <summary>非選択セルを淡くして選択を際立たせる不透明度（案1）。選択=1.0／非選択=0.9（濃いめ）。</summary>
    public double SelectionOpacity => IsSelected ? 1.0 : 0.9;

    /// <summary>
    /// アクセント外枠の不透明度（案2）。枠はレイアウトを動かさないよう常設し、可視性だけ切替える。
    /// 選択=1.0／非選択=0.0。
    /// </summary>
    public double SelectionFrameOpacity => IsSelected ? 1.0 : 0.0;

    // 評価操作（永続化込み）
    public void SetRating(int value)
    {
        Eval.SetRating(value);
        _store.SaveRating(FileName, Eval.PersistedRating);
        OnPropertyChanged(nameof(RatingStars));
        OnPropertyChanged(nameof(RatingForeground)); // EXIF由来→ユーザー変更で色が変わる
        OnPropertyChanged(nameof(RatingVisibility));
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

    private void NotifyFlag()
    {
        OnPropertyChanged(nameof(PickVisibility));
        OnPropertyChanged(nameof(RejectVisibility));
        OnPropertyChanged(nameof(FlagVisibility));
    }

    public void ToggleColorLabel(ColorLabel label)
    {
        Eval.ToggleColorLabel(label);
        _store.SaveColorLabel(FileName, label, Eval.GetPersistedColorLabel(label));
        OnPropertyChanged($"{label}Visibility");
        OnPropertyChanged(nameof(ColorLabelBorderBrush));
        OnPropertyChanged(nameof(ColorDotsVisibility));
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
                using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.PicturesView, 320)
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
