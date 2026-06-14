namespace PhotoQuickSelector.Core;

/// <summary>カラーラベルの色。</summary>
public enum ColorLabel
{
    Red,
    Yellow,
    Green,
    Blue,
    Purple,
}

/// <summary>
/// 1 枚の写真に対する評価（レーティング・フラグ・カラーラベル）。
///
/// 永続化値は「未設定(null)」を区別する。実効レーティングは
/// 「永続化値があればそれ、無ければ EXIF/XMP 値」で決まる。
/// 状態遷移は純粋メソッドとして提供し、永続化は呼び出し側（MetadataStore）が担う。
/// </summary>
public sealed class PhotoEvaluation
{
    /// <summary>EXIF/XMP から読み取ったレーティング（永続化値が無いときのフォールバック）。</summary>
    public int ExifRating { get; init; }

    public int? PersistedRating { get; set; }
    public int? PersistedFlagRating { get; set; }

    private readonly Dictionary<ColorLabel, int?> _persistedColorLabels = new()
    {
        [ColorLabel.Red] = null,
        [ColorLabel.Yellow] = null,
        [ColorLabel.Green] = null,
        [ColorLabel.Blue] = null,
        [ColorLabel.Purple] = null,
    };

    /// <summary>実効レーティング（0–5）。永続化値を優先し、無ければ EXIF 値。</summary>
    public int Rating => PersistedRating ?? ExifRating;

    /// <summary>実効フラグ（拒否 -1 / 中立 0 / 採用 +1）。</summary>
    public int FlagRating => PersistedFlagRating ?? 0;

    public int? GetPersistedColorLabel(ColorLabel label) => _persistedColorLabels[label];
    public void SetPersistedColorLabel(ColorLabel label, int? value) => _persistedColorLabels[label] = value;

    /// <summary>指定色ラベルが付いているか。</summary>
    public bool HasColorLabel(ColorLabel label) => (_persistedColorLabels[label] ?? 0) > 0;

    // --- 状態遷移（新しい永続化値を返す。null を返すことはない） ---

    public const int MinRating = 0;
    public const int MaxRating = 5;

    /// <summary>レーティングを 1 上げる（最大 5）。</summary>
    public int RatingUp() => SetRating(Rating + 1);

    /// <summary>レーティングを 1 下げる（最小 0）。</summary>
    public int RatingDown() => SetRating(Rating - 1);

    /// <summary>レーティングを直接設定（0–5 にクランプ）。</summary>
    public int SetRating(int value)
    {
        var clamped = Math.Clamp(value, MinRating, MaxRating);
        PersistedRating = clamped;
        return clamped;
    }

    /// <summary>
    /// フラグを採用方向へ 1 段階。拒否(-1)→中立(0)→採用(+1)。採用のままなら変化なし。
    /// </summary>
    public int FlagUp()
    {
        if (FlagRating < 0) PersistedFlagRating = 0;
        else if (FlagRating == 0) PersistedFlagRating = 1;
        return FlagRating;
    }

    /// <summary>
    /// フラグを拒否方向へ 1 段階。採用(+1)→中立(0)→拒否(-1)。拒否のままなら変化なし。
    /// </summary>
    public int FlagDown()
    {
        if (FlagRating > 0) PersistedFlagRating = 0;
        else if (FlagRating == 0) PersistedFlagRating = -1;
        return FlagRating;
    }

    /// <summary>カラーラベルのオン/オフをトグルし、新しい値(0 or 1)を返す。</summary>
    public int ToggleColorLabel(ColorLabel label)
    {
        var next = HasColorLabel(label) ? 0 : 1;
        _persistedColorLabels[label] = next;
        return next;
    }
}
