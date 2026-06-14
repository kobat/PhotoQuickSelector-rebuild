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
        var (focusPoint, focusSize) = ReadSonyFocus(sony);
        var (hasGps, gpsDescription) = ReadGps(gps);

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

            HasGpsLocation = hasGps,
            GpsLocationDescription = gpsDescription,
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
    /// Sony メーカーノートから AF フォーカス点・枠サイズを読む。
    /// 生タグ 0x2027(フォーカス座標) / 0x2037(枠サイズ) を直接参照する。
    /// </summary>
    private static (PointI?, SizeI?) ReadSonyFocus(SonyType1MakernoteDirectory? sony)
    {
        if (sony == null) return (null, null);

        PointI? point = null;
        var locationTag = sony.GetInt32Array(0x2027);
        if (locationTag is { Length: >= 4 })
            point = new PointI(locationTag[2], locationTag[3]);

        SizeI? size = null;
        var frameSizeTag = sony.GetByteArray(0x2037);
        if (frameSizeTag is { Length: >= 4 })
        {
            // Sony はリトルエンディアン前提（手抜きだが Sony 機限定なので可）
            var width = frameSizeTag[1] << 8 | frameSizeTag[0];
            var height = frameSizeTag[3] << 8 | frameSizeTag[2];
            size = new SizeI(width, height);
        }

        return (point, size);
    }

    private static (bool, string) ReadGps(GpsDirectory? gps)
    {
        if (gps != null && gps.TryGetGeoLocation(out var location))
            return (true, location.ToDmsString());
        return (false, "");
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
