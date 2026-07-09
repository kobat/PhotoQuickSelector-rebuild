using System.Globalization;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Exif.Makernotes;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.Xmp;
using Directory = MetadataExtractor.Directory;

namespace PhotoQuickSelector.Core;

/// <summary>
/// MetadataExtractor を用いて画像ファイルから <see cref="ImageMetadata"/> を抽出する。
/// 公式 NuGet パッケージのみに依存（フォーク不要）。
/// </summary>
public static class MetadataReader
{
    /// <summary>対応拡張子（小文字、ドット付き）。</summary>
    public static readonly IReadOnlySet<string> SupportedExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg" };

    public static bool IsSupported(string path)
        => SupportedExtensions.Contains(System.IO.Path.GetExtension(path));

    public static ImageMetadata Read(string path)
    {
        var directories = ImageMetadataReader.ReadMetadata(path);

        var jpeg = directories.OfType<JpegDirectory>().FirstOrDefault();
        var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        var sony = directories.OfType<SonyType1MakernoteDirectory>().FirstOrDefault();
        var olympus = directories.OfType<OlympusEquipmentMakernoteDirectory>().FirstOrDefault();
        var olympusCameraSettings = directories.OfType<OlympusCameraSettingsMakernoteDirectory>().FirstOrDefault();
        var gps = directories.OfType<GpsDirectory>().FirstOrDefault();
        var xmp = directories.OfType<XmpDirectory>().FirstOrDefault();

        var fileInfo = new FileInfo(path);

        var focalLength = GetDouble(subIfd, ExifDirectoryBase.TagFocalLength);
        var focalLength35 = MetadataCalc.Compute35mmEquivalent(
            focalLength,
            GetDouble(subIfd, ExifDirectoryBase.Tag35MMFilmEquivFocalLength),
            GetDouble(olympus, OlympusEquipmentMakernoteDirectory.TagFocalPlaneDiagonal));

        var aperture = GetDouble(subIfd, ExifDirectoryBase.TagFNumber);
        var iso = GetInt32(subIfd, ExifDirectoryBase.TagIsoEquivalent);
        var orientation = GetInt32(ifd0, ExifDirectoryBase.TagOrientation);

        var (takenOffset, takenDescription) = ReadTakenDateTime(subIfd);
        var (focusPoint, focusSize, focusReferenceSize) = ReadSonyFocus(sony);
        if (focusPoint == null)
            (focusPoint, focusSize, focusReferenceSize) = ReadOlympusFocus(olympusCameraSettings);
        var (hasGps, gpsDescription, gpsLat, gpsLon) = ReadGps(gps);

        return new ImageMetadata
        {
            Path = path,
            FileName = System.IO.Path.GetFileName(path),
            DirectoryName = System.IO.Path.GetDirectoryName(path) ?? "",
            FileSize = fileInfo.Exists ? fileInfo.Length : 0,

            OriginalWidth = GetInt32(jpeg, JpegDirectory.TagImageWidth),
            OriginalHeight = GetInt32(jpeg, JpegDirectory.TagImageHeight),
            Orientation = orientation,
            OrientationDescription = GetDescription(ifd0, ExifDirectoryBase.TagOrientation),

            TakenDateTimeOffset = takenOffset,
            TakenDateTimeDescription = takenDescription,

            ExifRating = ReadXmpRating(xmp),

            CameraMaker = GetString(ifd0, ExifDirectoryBase.TagMake),
            CameraModel = GetString(ifd0, ExifDirectoryBase.TagModel),
            LensMake = GetString(subIfd, ExifDirectoryBase.TagLensMake),
            LensModel = ReadLensModel(subIfd),

            FocalLength = focalLength,
            FocalLength35 = focalLength35,
            FocalLengthDescription = MetadataCalc.FocalLengthDescription(focalLength, focalLength35),
            Aperture = aperture,
            ApertureDescription = MetadataCalc.ApertureDescription(aperture),
            ExposureTimeDescription = ReadExposureTimeDescription(subIfd),
            Iso = iso,
            IsoDescription = MetadataCalc.IsoDescription(iso),
            ExposureBias = GetDouble(subIfd, ExifDirectoryBase.TagExposureBias),
            ExposureBiasDescription = ReadExposureBiasDescription(subIfd),

            FocusPoint = focusPoint,
            FocusSize = focusSize,
            FocusReferenceSize = focusReferenceSize,

            HasGpsLocation = hasGps,
            GpsLocationDescription = gpsDescription,
            GpsLatitude = gpsLat,
            GpsLongitude = gpsLon,
        };
    }

    private static string ReadLensModel(ExifSubIfdDirectory? subIfd)
    {
        var lens = GetString(subIfd, ExifDirectoryBase.TagLensModel);
        if (lens.Length > 0) return lens;
        return GetDescription(subIfd, ExifDirectoryBase.TagLensSpecification).Trim();
    }

    private static string ReadExposureTimeDescription(ExifSubIfdDirectory? subIfd)
    {
        if (subIfd == null || !subIfd.ContainsTag(ExifDirectoryBase.TagExposureTime)) return "";
        var rational = subIfd.GetRational(ExifDirectoryBase.TagExposureTime);
        return rational.IsZero ? "" : rational.ToString();
    }

    private static string ReadExposureBiasDescription(ExifSubIfdDirectory? subIfd)
    {
        if (subIfd == null || !subIfd.ContainsTag(ExifDirectoryBase.TagExposureBias)) return "";
        return MetadataCalc.ExposureBiasDescription(
            subIfd.GetDouble(ExifDirectoryBase.TagExposureBias));
    }

    private static (DateTimeOffset, string) ReadTakenDateTime(ExifSubIfdDirectory? subIfd)
    {
        if (subIfd == null || !subIfd.ContainsTag(ExifDirectoryBase.TagDateTimeOriginal))
            return (DateTimeOffset.MinValue, "Unknown");

        var hasSubsecond = subIfd.ContainsTag(ExifDirectoryBase.TagSubsecondTimeOriginal);
        double subsecondMs = hasSubsecond
            ? subIfd.GetDouble(ExifDirectoryBase.TagSubsecondTimeOriginal)
            : 0;

        if (subIfd.ContainsTag(ExifDirectoryBase.TagTimeZoneOriginal))
        {
            // タイムゾーンあり
            DateTimeOffset.TryParseExact(
                subIfd.GetDescription(ExifDirectoryBase.TagDateTimeOriginal) + " " +
                subIfd.GetDescription(ExifDirectoryBase.TagTimeZoneOriginal),
                "yyyy:MM:dd HH:mm:ss zzz", null, DateTimeStyles.None, out var withZone);

            var value = withZone.AddMilliseconds(subsecondMs);
            var format = hasSubsecond ? "yyyy-MM-dd HH:mm:ss.fff zzz" : "yyyy-MM-dd HH:mm:ss zzz";
            return (value, value.ToString(format));
        }
        else
        {
            // タイムゾーンなし（ローカル時刻として扱う）
            var dateTime = subIfd.GetDateTime(ExifDirectoryBase.TagDateTimeOriginal);
            var value = new DateTimeOffset(dateTime).AddMilliseconds(subsecondMs);
            var format = hasSubsecond ? "yyyy-MM-dd HH:mm:ss.fff" : "yyyy-MM-dd HH:mm:ss";
            return (value, value.ToString(format));
        }
    }

    /// <summary>
    /// Sony メーカーノートから AF フォーカス点・枠サイズ・基準画像サイズを読む。
    /// 生タグ 0x2027(フォーカス座標) / 0x2037(枠サイズ) を直接参照する。
    /// 0x2027 は [基準幅, 基準高さ, フォーカス X, フォーカス Y] の順（exiftool 仕様）。
    /// </summary>
    private static (PointI?, SizeI?, SizeI?) ReadSonyFocus(SonyType1MakernoteDirectory? sony)
    {
        if (sony == null) return (null, null, null);

        PointI? point = null;
        SizeI? referenceSize = null;
        var locationTag = sony.GetInt32Array(0x2027);
        if (locationTag is { Length: >= 4 })
        {
            referenceSize = new SizeI(locationTag[0], locationTag[1]);
            point = new PointI(locationTag[2], locationTag[3]);
        }

        SizeI? size = null;
        var frameSizeTag = sony.GetByteArray(0x2037);
        if (frameSizeTag is { Length: >= 4 })
        {
            // Sony はリトルエンディアン前提（手抜きだが Sony 機限定なので可）
            var width = frameSizeTag[1] << 8 | frameSizeTag[0];
            var height = frameSizeTag[3] << 8 | frameSizeTag[2];
            size = new SizeI(width, height);
        }

        return (point, size, referenceSize);
    }

    /// <summary>
    /// Olympus / OM メーカーノートの AF 枠（Camera Settings tag 0x0304 "AF Areas"）を読む。
    /// 各要素は 1 枠を「画面全体に対する 0..255 の割合」で左上・右下の 4 隅として詰めた int32。
    /// バイトは上位から left, top, right, bottom（例 0x8C76937F = (140,118)-(147,127)）。0 は未使用枠。
    /// Sony と同じ「中心＋枠サイズ＋基準サイズ」表現へ写す（描画・ルーペ・ナビ経路を共用するため）。
    /// 中心は 2 隅の平均で 0.5 単位を生むので、基準を 255→510・中心/サイズを 2 倍し整数のまま無損失に格納する
    /// （<see cref="PointI"/>/<see cref="SizeI"/> は int）。座標は生センサー基準として扱い、描画側の
    /// OrientationMatrix で表示空間へ写す（Orientation=1 の横位置では恒等）。
    /// </summary>
    private static (PointI?, SizeI?, SizeI?) ReadOlympusFocus(OlympusCameraSettingsMakernoteDirectory? camera)
    {
        var areas = camera?.GetInt32Array(0x0304);
        if (areas == null) return (null, null, null);

        foreach (var packed in areas)
        {
            if (packed == 0) continue; // 未使用枠 (0,0)-(0,0)
            var v = (uint)packed;
            int left = (int)((v >> 24) & 0xFF);
            int top = (int)((v >> 16) & 0xFF);
            int right = (int)((v >> 8) & 0xFF);
            int bottom = (int)(v & 0xFF);

            // 2×255 基準（中心が 0.5 単位になるため 2 倍して整数化＝無損失）。
            var referenceSize = new SizeI(510, 510);
            var point = new PointI(left + right, top + bottom);
            var size = new SizeI(2 * (right - left), 2 * (bottom - top));
            return (point, size, referenceSize);
        }
        return (null, null, null);
    }

    private static (bool HasGps, string Description, double? Latitude, double? Longitude) ReadGps(GpsDirectory? gps)
    {
        if (gps != null && gps.TryGetGeoLocation(out var location))
            return (true, location.ToDmsString(), location.Latitude, location.Longitude);
        return (false, "", null, null);
    }

    private static int ReadXmpRating(XmpDirectory? xmp)
    {
        var properties = xmp?.GetXmpProperties();
        if (properties == null || !properties.TryGetValue("xmp:Rating", out var value))
            return 0;
        return int.TryParse(value, out var rating) ? rating : 0;
    }

    // --- Directory 取得ヘルパ（タグが無ければ既定値） ---

    private static string GetDescription(Directory? directory, int tag)
        => directory != null && directory.ContainsTag(tag) ? directory.GetDescription(tag) ?? "" : "";

    private static string GetString(Directory? directory, int tag)
        => directory != null && directory.ContainsTag(tag) ? (directory.GetString(tag) ?? "").Trim() : "";

    private static int GetInt32(Directory? directory, int tag)
        => directory != null && directory.ContainsTag(tag) ? directory.GetInt32(tag) : 0;

    private static double GetDouble(Directory? directory, int tag)
        => directory != null && directory.ContainsTag(tag) ? directory.GetDouble(tag) : 0;
}
