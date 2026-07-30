using System;
using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PhotoQuickSelector.Core;
using PhotoQuickSelector_App.ViewModels;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// 画像情報パネルの「評価」グループの 1 行（項目名＋値＋更新日時）。
/// 値と日時は写真切替・評価変更で書き換わるため OneWay バインド前提の可変プロパティにする。
/// </summary>
public sealed partial class EvaluationInfoRow : ObservableObject
{
    public EvaluationInfoRow(string name) => Name = name;

    /// <summary>項目名（生成後不変）。</summary>
    public string Name { get; }

    [ObservableProperty]
    public partial string Value { get; set; }

    [ObservableProperty]
    public partial string UpdatedAtText { get; set; }
}

/// <summary>
/// 画像情報パネルの「評価」グループ。行オブジェクトを固定長で作り置きし、内容だけを差し替える。
///
/// これは意図的な設計で、評価を変えるたびにグループ化 ListView の Source を差し替えると
/// (a) 数百行の再構築で UI スレッドが塞がり、(b) 一覧のスクロール位置が先頭へ戻ってしまう。
/// 行数を固定（フラグ／レーティング／カラー5色／最終更新）にすれば、評価変更は
/// <see cref="Update"/> でのプロパティ更新だけで済み、コレクション変更通知も不要になる。
///
/// 更新時刻の出所は <see cref="PhotoItemViewModel.EvalTimestamps"/>（メモリ常駐）。
/// 表示のたびに DB を引かない。
/// </summary>
public sealed class EvaluationInfoSection
{
    /// <summary>時刻の表示書式。ja/en 共通の固定書式（一覧で前後関係を読みやすくするため）。</summary>
    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>更新時刻が不明（スキーマ v2 より前に付けた評価／未変更の項目）のときの表示。</summary>
    private const string UnknownTimestamp = "—";

    // 表示順は他の評価表示（バッジ・メニュー）と共通の定義を使う。
    private static ColorLabel[] ColorLabelOrder => PhotoItemViewModel.ColorLabelOrder;

    private static readonly IReadOnlyDictionary<ColorLabel, string> ColorLabelResourceKeys =
        new Dictionary<ColorLabel, string>
        {
            [ColorLabel.Red] = "Ctx_Color_Red",
            [ColorLabel.Yellow] = "Ctx_Color_Yellow",
            [ColorLabel.Green] = "Ctx_Color_Green",
            [ColorLabel.Blue] = "Ctx_Color_Blue",
            [ColorLabel.Purple] = "Ctx_Color_Purple",
        };

    private readonly EvaluationInfoRow _flag = new(Loc.Get("Ctx_Flag"));
    private readonly EvaluationInfoRow _rating = new(Loc.Get("Ctx_Rating"));
    private readonly Dictionary<ColorLabel, EvaluationInfoRow> _colorLabels;
    private readonly EvaluationInfoRow _lastUpdated = new(Loc.Get("Info_LastUpdated"));

    private readonly List<object> _rows = new();

    public EvaluationInfoSection()
    {
        _colorLabels = new Dictionary<ColorLabel, EvaluationInfoRow>();
        foreach (var label in ColorLabelOrder)
            _colorLabels[label] = new EvaluationInfoRow(Loc.Get(ColorLabelResourceKeys[label]));

        // 並びは全表示箇所と同じ「旗 → ★ → カラー」。最後に行単位の最終更新を置く。
        _rows.Add(_flag);
        _rows.Add(_rating);
        foreach (var label in ColorLabelOrder) _rows.Add(_colorLabels[label]);
        _rows.Add(_lastUpdated);
    }

    /// <summary>グループ見出し。</summary>
    public string GroupName { get; } = Loc.Get("Info_EvalGroup");

    /// <summary>行（固定長・同一インスタンスを使い回す）。</summary>
    public IReadOnlyList<object> Rows => _rows;

    /// <summary>指定写真の評価内容を行へ反映する（写真切替・評価変更の両方から呼ぶ）。</summary>
    public void Update(PhotoItemViewModel photo)
    {
        var eval = photo.Eval;
        var timestamps = photo.EvalTimestamps;

        _flag.Value = FlagText(eval.PersistedFlagRating);
        _flag.UpdatedAtText = FormatTimestamp(timestamps.FlagRating);

        // レーティングは DB の永続化値をそのまま出す（EXIF 由来の実効値ではない＝このアプリで付けた評価）。
        // EXIF（xmp:Rating）側にも値があれば改行して併記する＝永続化値が無いときの実効値の出所が判る
        // （TextBlock は文字列中の改行をそのまま行分割として描く）。
        // ExifRating は未記録でも 0 になり「明示的な 0」と区別できないため、1 以上のときだけ出す。
        _rating.Value = eval.PersistedRating?.ToString(CultureInfo.InvariantCulture) ?? UnsetText;
        if (eval.ExifRating > 0)
            _rating.Value += "\n" + Loc.Get("Info_ExifRating", eval.ExifRating);
        _rating.UpdatedAtText = FormatTimestamp(timestamps.Rating);

        foreach (var label in ColorLabelOrder)
        {
            var row = _colorLabels[label];
            row.Value = ColorLabelText(eval.GetPersistedColorLabel(label));
            row.UpdatedAtText = FormatTimestamp(timestamps.GetColorLabel(label));
        }

        _lastUpdated.Value = string.Empty; // 値を持たない行（時刻だけ）
        _lastUpdated.UpdatedAtText = FormatTimestamp(timestamps.UpdatedAt);
    }

    private static string UnsetText => Loc.Get("Info_Unset");

    private static string FlagText(int? flag) => flag switch
    {
        null => UnsetText,
        > 0 => Loc.Get("Ctx_Flag_Pick"),
        < 0 => Loc.Get("Ctx_Flag_Reject"),
        _ => Loc.Get("Ctx_Flag_None"),
    };

    private static string ColorLabelText(int? value) => value switch
    {
        null => UnsetText,
        > 0 => Loc.Get("Info_ColorOn"),
        _ => Loc.Get("Info_ColorOff"),
    };

    /// <summary>UTC の更新時刻をローカル時刻の固定書式で表示する。null は「不明」。</summary>
    private static string FormatTimestamp(DateTimeOffset? updatedAt) =>
        updatedAt?.ToLocalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture) ?? UnknownTimestamp;
}
