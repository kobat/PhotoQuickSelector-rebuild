namespace PhotoQuickSelector.Core;

/// <summary>
/// 評価データ 1 行の忠実な写し（各評価値＋その項目を最後に変更した時刻）。
///
/// 表示・編集には <see cref="PhotoEvaluation"/> を使う。こちらは評価のエクスポート／
/// インポートで「どちらが新しいか」を項目単位で突き合わせるための読み取り専用ビューで、
/// EXIF フォールバックのような表示都合の解釈を一切含まない。
/// 更新時刻の null は「不明」（スキーマ v2 より前に付けた評価、または未変更の項目）。
/// </summary>
public sealed class EvaluationRecord
{
    /// <summary>フォルダ相対のファイル名（評価データの主キー）。</summary>
    public string FileName { get; }

    public EvaluationRecord(string fileName) => FileName = fileName;

    /// <summary>永続化されたレーティング（0–5）。未設定なら null。</summary>
    public int? Rating { get; internal set; }

    /// <summary><see cref="Rating"/> を最後に変更した時刻（UTC・秒精度）。不明なら null。</summary>
    public DateTimeOffset? RatingUpdatedAt { get; internal set; }

    /// <summary>永続化されたフラグ（拒否 -1 / 中立 0 / 採用 +1）。未設定なら null。</summary>
    public int? FlagRating { get; internal set; }

    /// <summary><see cref="FlagRating"/> を最後に変更した時刻（UTC・秒精度）。不明なら null。</summary>
    public DateTimeOffset? FlagRatingUpdatedAt { get; internal set; }

    /// <summary>
    /// この行のいずれかの項目を最後に変更した時刻（＝項目別更新時刻の最大値。UTC・秒精度）。
    /// 「前回エクスポート以降に変わった行」を項目 7 列を見ずに拾うための冗長列。
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; internal set; }

    private readonly Dictionary<ColorLabel, int?> _colorLabels = new()
    {
        [ColorLabel.Red] = null,
        [ColorLabel.Yellow] = null,
        [ColorLabel.Green] = null,
        [ColorLabel.Blue] = null,
        [ColorLabel.Purple] = null,
    };

    private readonly Dictionary<ColorLabel, DateTimeOffset?> _colorLabelUpdatedAt = new()
    {
        [ColorLabel.Red] = null,
        [ColorLabel.Yellow] = null,
        [ColorLabel.Green] = null,
        [ColorLabel.Blue] = null,
        [ColorLabel.Purple] = null,
    };

    /// <summary>指定色ラベルの永続化値（0/1）。未設定なら null。</summary>
    public int? GetColorLabel(ColorLabel label) => _colorLabels[label];

    /// <summary>指定色ラベルを最後に変更した時刻（UTC・秒精度）。不明なら null。</summary>
    public DateTimeOffset? GetColorLabelUpdatedAt(ColorLabel label) => _colorLabelUpdatedAt[label];

    internal void SetColorLabel(ColorLabel label, int? value) => _colorLabels[label] = value;

    internal void SetColorLabelUpdatedAt(ColorLabel label, DateTimeOffset? updatedAt)
        => _colorLabelUpdatedAt[label] = updatedAt;
}
