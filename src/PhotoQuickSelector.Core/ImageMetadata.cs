namespace PhotoQuickSelector.Core;

/// <summary>整数座標。AF フォーカス点に使用。</summary>
public readonly record struct PointI(int X, int Y);

/// <summary>整数サイズ。AF フォーカス枠に使用。</summary>
public readonly record struct SizeI(int Width, int Height);

/// <summary>
/// 1 枚の画像から抽出した不変メタデータ。<see cref="MetadataReader"/> が生成する。
/// 評価（rating 等）は含まない（それは <see cref="PhotoEvaluation"/>）。
/// </summary>
public sealed class ImageMetadata
{
    // ファイル情報
    public required string Path { get; init; }
    public required string FileName { get; init; }
    public required string DirectoryName { get; init; }
    public long FileSize { get; init; }

    // 画像基本情報
    public int OriginalWidth { get; init; }
    public int OriginalHeight { get; init; }
    public int Orientation { get; init; }
    public string OrientationDescription { get; init; } = "";

    /// <summary>Orientation を考慮した表示上の幅。</summary>
    public int Width => MetadataCalc.DisplayWidth(Orientation, OriginalWidth, OriginalHeight);

    /// <summary>Orientation を考慮した表示上の高さ。</summary>
    public int Height => MetadataCalc.DisplayHeight(Orientation, OriginalWidth, OriginalHeight);

    public string ImageSizeDescription => (Width > 0 && Height > 0) ? $"{Width}x{Height}" : "-";

    // 撮影日時
    public DateTimeOffset TakenDateTimeOffset { get; init; } = DateTimeOffset.MinValue;
    public string TakenDateTimeDescription { get; init; } = "Unknown";

    // レーティング（EXIF/XMP 由来）
    public int ExifRating { get; init; }

    // カメラ
    public string CameraMaker { get; init; } = "";
    public string CameraModel { get; init; } = "";
    public string LensModel { get; init; } = "";

    // 撮影設定
    public double FocalLength { get; init; }
    public double FocalLength35 { get; init; }
    public string FocalLengthDescription { get; init; } = "";
    public double Aperture { get; init; }
    public string ApertureDescription { get; init; } = "";
    public string ExposureTimeDescription { get; init; } = "";
    public int Iso { get; init; }
    public string IsoDescription { get; init; } = "";
    public double ExposureBias { get; init; }
    public string ExposureBiasDescription { get; init; } = "";

    // AF フォーカス（Sony）。FocusPoint が null のときは情報なし。
    public PointI? FocusPoint { get; init; }
    public SizeI? FocusSize { get; init; }

    /// <summary>
    /// AF フォーカス点が測られている基準画像サイズ（Sony tag 0x2027 の [0],[1]）。
    /// フォーカス点を実画像ピクセルへ正規化する際の分母。通常 <see cref="OriginalWidth"/>/
    /// <see cref="OriginalHeight"/> とほぼ一致する。null のときは元画像サイズで代用する。
    /// </summary>
    public SizeI? FocusReferenceSize { get; init; }

    // GPS
    public bool HasGpsLocation { get; init; }
    public string GpsLocationDescription { get; init; } = "";
}
