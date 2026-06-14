using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoQuickSelector.Core;
using Windows.Storage;
using Windows.Storage.FileProperties;
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

    [ObservableProperty]
    public partial BitmapImage? Thumbnail { get; set; }

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

    public Visibility RedVisibility => Vis(ColorLabel.Red);
    public Visibility YellowVisibility => Vis(ColorLabel.Yellow);
    public Visibility GreenVisibility => Vis(ColorLabel.Green);
    public Visibility BlueVisibility => Vis(ColorLabel.Blue);
    public Visibility PurpleVisibility => Vis(ColorLabel.Purple);

    private Visibility Vis(ColorLabel label) =>
        Eval.HasColorLabel(label) ? Visibility.Visible : Visibility.Collapsed;

    // 評価操作（永続化込み）
    public void SetRating(int value)
    {
        Eval.SetRating(value);
        _store.SaveRating(FileName, Eval.PersistedRating);
        OnPropertyChanged(nameof(RatingStars));
        OnPropertyChanged(nameof(RatingForeground)); // EXIF由来→ユーザー変更で色が変わる
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
    }

    public void ToggleColorLabel(ColorLabel label)
    {
        Eval.ToggleColorLabel(label);
        _store.SaveColorLabel(FileName, label, Eval.GetPersistedColorLabel(label));
        OnPropertyChanged($"{label}Visibility");
    }

    /// <summary>OS のシェルサムネイルを非同期取得して <see cref="Thumbnail"/> に設定する。</summary>
    public async Task LoadThumbnailAsync()
    {
        if (Thumbnail != null) return;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(Meta.Path);
            using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.PicturesView, 320);
            if (thumbnail == null) return;

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(thumbnail);
            Thumbnail = bitmap;
        }
        catch
        {
            // サムネイル取得失敗は無視（画像なしで表示）
        }
    }
}
