namespace PhotoQuickSelector.Core;

/// <summary>
/// 評価 1 項目を保存した結果（適用後の更新時刻）。
///
/// 「更新時刻を進めるか否か」の判定（同値の押し直しでは進めない・行単位の時刻は後退させない）は
/// SQL 側に一本化してあるため、保存を呼んだ側は結果を受け取るだけにする。C# 側で同じ判定を
/// 書くと二重実装になり必ず乖離する。
/// </summary>
public readonly struct EvaluationSaveResult
{
    public EvaluationSaveResult(DateTimeOffset? itemUpdatedAt, DateTimeOffset? rowUpdatedAt)
    {
        ItemUpdatedAt = itemUpdatedAt;
        RowUpdatedAt = rowUpdatedAt;
    }

    /// <summary>保存した項目の更新時刻（UTC・秒精度）。不明なら null。</summary>
    public DateTimeOffset? ItemUpdatedAt { get; }

    /// <summary>その行のいずれかの項目を最後に変更した時刻（UTC・秒精度）。不明なら null。</summary>
    public DateTimeOffset? RowUpdatedAt { get; }
}

/// <summary>
/// 1 枚の写真の「各評価項目を最後に変更した時刻」だけを持つ常駐用の器。
///
/// 評価値（<see cref="PhotoEvaluation"/>）と同じくメモリ常駐＋書き込み時に反映（write-through）で
/// 扱う。表示のたびに DB を引かずに済み、将来の「評価時刻でのソート/絞り込み」も全件メモリ前提で作れる。
/// <see cref="EvaluationRecord"/> は同じ情報を持つがエクスポート用の一時オブジェクト（値＋辞書 2 本）で、
/// 数千〜1 万枚を常駐させる用途には重い。こちらは epoch 秒（UTC）の固定フィールドだけを持つ。
///
/// 時刻の null は「不明」（スキーマ v2 より前に付けた評価、または未変更の項目）。
/// 他インスタンスが同じ DB を書いた分は反映されない（評価値の常駐と同じ性質）。
/// </summary>
public sealed class EvaluationTimestamps
{
    /// <summary>不明を表す内部値。epoch 秒 0（1970-01-01）は評価の更新時刻として現実に現れない。</summary>
    private const long Unknown = 0;

    private long _rating = Unknown;
    private long _flagRating = Unknown;

    // index = (int)ColorLabel。既定値 0 ＝ Unknown。
    private readonly long[] _colorLabels = new long[Enum.GetValues<ColorLabel>().Length];

    private long _updatedAt = Unknown;

    /// <summary>レーティングを最後に変更した時刻。不明なら null。</summary>
    public DateTimeOffset? Rating => ToTime(_rating);

    /// <summary>フラグを最後に変更した時刻。不明なら null。</summary>
    public DateTimeOffset? FlagRating => ToTime(_flagRating);

    /// <summary>指定色ラベルを最後に変更した時刻。不明なら null。</summary>
    public DateTimeOffset? GetColorLabel(ColorLabel label) => ToTime(_colorLabels[(int)label]);

    /// <summary>いずれかの項目を最後に変更した時刻（＝項目別の最大値）。不明なら null。</summary>
    public DateTimeOffset? UpdatedAt => ToTime(_updatedAt);

    /// <summary>レーティング保存の結果を取り込む。</summary>
    public void SetRating(EvaluationSaveResult saved)
    {
        _rating = ToEpoch(saved.ItemUpdatedAt);
        _updatedAt = ToEpoch(saved.RowUpdatedAt);
    }

    /// <summary>フラグ保存の結果を取り込む。</summary>
    public void SetFlagRating(EvaluationSaveResult saved)
    {
        _flagRating = ToEpoch(saved.ItemUpdatedAt);
        _updatedAt = ToEpoch(saved.RowUpdatedAt);
    }

    /// <summary>カラーラベル保存の結果を取り込む。</summary>
    public void SetColorLabel(ColorLabel label, EvaluationSaveResult saved)
    {
        _colorLabels[(int)label] = ToEpoch(saved.ItemUpdatedAt);
        _updatedAt = ToEpoch(saved.RowUpdatedAt);
    }

    /// <summary>
    /// DB から読んだ 1 行（null＝行なし／DB なし）から更新時刻を取り出す。
    /// フォルダ読み込み時に写真ごとに 1 つ作る。
    /// </summary>
    public static EvaluationTimestamps FromRecord(EvaluationRecord? record)
    {
        var timestamps = new EvaluationTimestamps();
        if (record == null) return timestamps;

        timestamps._rating = ToEpoch(record.RatingUpdatedAt);
        timestamps._flagRating = ToEpoch(record.FlagRatingUpdatedAt);
        timestamps._updatedAt = ToEpoch(record.UpdatedAt);
        foreach (var label in Enum.GetValues<ColorLabel>())
            timestamps._colorLabels[(int)label] = ToEpoch(record.GetColorLabelUpdatedAt(label));
        return timestamps;
    }

    private static DateTimeOffset? ToTime(long epochSeconds) =>
        epochSeconds == Unknown ? null : DateTimeOffset.FromUnixTimeSeconds(epochSeconds);

    private static long ToEpoch(DateTimeOffset? time) => time?.ToUnixTimeSeconds() ?? Unknown;
}
