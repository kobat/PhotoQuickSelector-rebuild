namespace PhotoQuickSelector.Core;

/// <summary>レーティング絞り込みの比較方法（≧ / ＝）。</summary>
public enum RatingCompareMode
{
    /// <summary>指定値以上（≧）。</summary>
    GreaterEqual,

    /// <summary>指定値と一致（＝）。</summary>
    Equal,
}

/// <summary>
/// 写真一覧の絞り込み条件（SPEC §3-4）。UI 非依存の純ロジックとして
/// <see cref="PhotoEvaluation"/> に対する一致判定（<see cref="Matches"/>）を提供する。
///
/// 条件は 3 系統（レーティング / フラグ / カラーラベル）の AND。各系統は「有効な条件が
/// 設定されているときだけ」絞り込みに効く。<see cref="Enabled"/> が false のときは全件通過。
/// </summary>
public sealed class PhotoFilter
{
    /// <summary>絞り込みの有効/無効。false なら <see cref="Matches"/> は常に true。</summary>
    public bool Enabled { get; set; }

    // --- レーティング ---

    /// <summary>レーティングのしきい値（0–5）。0 は「レーティング条件なし」を意味する。</summary>
    public int RatingValue { get; set; }

    /// <summary>レーティングの比較方法（≧ / ＝）。</summary>
    public RatingCompareMode RatingCompareMode { get; set; } = RatingCompareMode.GreaterEqual;

    /// <summary>レーティング 0（未評価）を絞り込みに含めるか。</summary>
    public bool NoRating { get; set; }

    private bool IsRatingActive => RatingValue > 0 || NoRating;

    // --- フラグ（採用 +1 / 中立 0 / 拒否 -1） ---

    public bool FlagAccept { get; set; }
    public bool FlagNeutral { get; set; }
    public bool FlagReject { get; set; }

    private bool IsFlagActive => FlagAccept || FlagNeutral || FlagReject;

    // --- カラーラベル（選択色を AND で要求） ---

    private readonly Dictionary<ColorLabel, bool> _colors = new()
    {
        [ColorLabel.Red] = false,
        [ColorLabel.Yellow] = false,
        [ColorLabel.Green] = false,
        [ColorLabel.Blue] = false,
        [ColorLabel.Purple] = false,
    };

    public bool GetColor(ColorLabel label) => _colors[label];
    public void SetColor(ColorLabel label, bool value) => _colors[label] = value;

    private bool IsColorActive => _colors.Values.Any(v => v);

    /// <summary>
    /// 評価が現在の絞り込み条件に一致するか。<see cref="Enabled"/> が false なら常に true。
    /// </summary>
    public bool Matches(PhotoEvaluation eval)
    {
        if (!Enabled) return true;

        if (IsRatingActive)
        {
            if (eval.Rating == 0)
            {
                if (!NoRating) return false;
            }
            else
            {
                if (RatingValue == 0) return false; // 未評価のみを要求しているのに評価あり
                if (RatingCompareMode == RatingCompareMode.GreaterEqual)
                {
                    if (eval.Rating < RatingValue) return false;
                }
                else
                {
                    if (eval.Rating != RatingValue) return false;
                }
            }
        }

        if (IsFlagActive)
        {
            if (eval.FlagRating > 0 && !FlagAccept) return false;
            if (eval.FlagRating == 0 && !FlagNeutral) return false;
            if (eval.FlagRating < 0 && !FlagReject) return false;
        }

        if (IsColorActive)
        {
            // 選択した色は AND（すべて付いている写真だけ通す）。
            foreach (var (label, selected) in _colors)
                if (selected && !eval.HasColorLabel(label))
                    return false;
        }

        return true;
    }

    /// <summary>
    /// 現在の条件を人間可読の行（"Rating: ≧3" 等）に整形する。移動バッチ（<see cref="FileMove"/> 等）の
    /// <c>@rem</c> コメントで使う。無効時・条件なし時は空。
    /// </summary>
    public IReadOnlyList<string> DescribeConditions()
    {
        var lines = new List<string>();
        if (!Enabled) return lines;

        if (IsRatingActive)
        {
            if (RatingValue > 0)
            {
                var op = RatingCompareMode == RatingCompareMode.GreaterEqual ? "≧" : "＝";
                lines.Add($"Rating: {op}{RatingValue}{(NoRating ? ", NoRating" : "")}");
            }
            else
            {
                lines.Add("Rating: NoRating");
            }
        }

        if (IsFlagActive)
        {
            var flags = new List<string>();
            if (FlagAccept) flags.Add("Accept");
            if (FlagNeutral) flags.Add("Neutral");
            if (FlagReject) flags.Add("Reject");
            lines.Add($"Flag: {string.Join(", ", flags)}");
        }

        if (IsColorActive)
        {
            var colors = ColorOrder.Where(c => _colors[c]).Select(c => c.ToString());
            lines.Add($"ColorLabel: {string.Join(", ", colors)}");
        }

        return lines;
    }

    private static readonly ColorLabel[] ColorOrder =
        { ColorLabel.Red, ColorLabel.Yellow, ColorLabel.Green, ColorLabel.Blue, ColorLabel.Purple };
}
