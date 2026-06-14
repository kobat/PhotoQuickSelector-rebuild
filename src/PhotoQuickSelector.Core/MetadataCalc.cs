namespace PhotoQuickSelector.Core;

/// <summary>
/// EXIF 値から表示用の値を導出する純粋関数群。副作用を持たないため単体テストの主対象。
/// </summary>
public static class MetadataCalc
{
    /// <summary>
    /// EXIF Orientation が 5 以上（90/270 度回転）のとき幅と高さが入れ替わる。
    /// </summary>
    public static bool IsRotated(int orientation) => orientation >= 5;

    public static int DisplayWidth(int orientation, int originalWidth, int originalHeight)
        => IsRotated(orientation) ? originalHeight : originalWidth;

    public static int DisplayHeight(int orientation, int originalWidth, int originalHeight)
        => IsRotated(orientation) ? originalWidth : originalHeight;

    /// <summary>
    /// 35mm 換算焦点距離を求める。
    /// EXIF に換算値があればそれを使う。無い場合、Olympus(マイクロフォーサーズ)機は
    /// FocalPlaneDiagonal から逆算する。求められなければ 0 を返す。
    /// </summary>
    /// <param name="focalLength">実焦点距離(mm)</param>
    /// <param name="exifFocalLength35">EXIF の 35mm 換算焦点距離(mm)。無ければ 0。</param>
    /// <param name="olympusFocalPlaneDiagonal">Olympus メーカーノートの撮像面対角長(mm)。無ければ 0。</param>
    public static double Compute35mmEquivalent(
        double focalLength, double exifFocalLength35, double olympusFocalPlaneDiagonal)
    {
        if (exifFocalLength35 != 0) return exifFocalLength35;
        if (focalLength == 0) return 0;

        // マイクロフォーサーズ(対角約21.6mm)はちょうど2倍
        if (Math.Round(olympusFocalPlaneDiagonal, 1) == 21.6) return focalLength * 2;

        // それ以外は 35mm 判の対角 43.27mm との比から換算
        if (olympusFocalPlaneDiagonal > 0)
            return Math.Round(focalLength * 43.27 / olympusFocalPlaneDiagonal);

        return 0;
    }

    /// <summary>
    /// 焦点距離の表示文字列。例: "35mm (52.5mm)"。実焦点距離が 0 なら空文字。
    /// </summary>
    public static string FocalLengthDescription(double focalLength, double focalLength35)
    {
        if (focalLength == 0) return "";
        if (focalLength35 == 0) return $"{Math.Round(focalLength, 1)}mm";
        return $"{Math.Round(focalLength, 1)}mm ({Math.Round(focalLength35, 1)}mm)";
    }

    public static string ApertureDescription(double aperture)
        => aperture == 0 ? "" : $"F{Math.Round(aperture, 1)}";

    public static string IsoDescription(int iso)
        => iso == 0 ? "" : $"ISO{iso}";

    /// <summary>
    /// 露出補正の表示文字列。タグが存在する場合のみ呼ぶ。例: "+0.7EV", "±0EV", "-1.0EV"。
    /// </summary>
    public static string ExposureBiasDescription(double exposureBias)
    {
        if (exposureBias == 0) return "±0EV";
        return exposureBias > 0 ? $"+{exposureBias:0.0}EV" : $"{exposureBias:0.0}EV";
    }
}
