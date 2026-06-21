using CommunityToolkit.Mvvm.ComponentModel;
using PhotoQuickSelector.Core;

namespace PhotoQuickSelector_App.ViewModels;

/// <summary>
/// 絞り込み条件（SPEC §3-4）のビューモデル。UI の各入力（トグル/チェック/レーティング）を
/// バインドし、変更を Core の <see cref="PhotoFilter"/> へ反映して <see cref="Changed"/> を発火する。
/// 実際の絞り込み（<see cref="MainViewModel.Photos"/> の再構築）は購読側（MainViewModel）が行う。
/// </summary>
public partial class FilterViewModel : ObservableObject
{
    /// <summary>判定に使う Core モデル（テスト済みロジック）。</summary>
    public PhotoFilter Model { get; } = new();

    /// <summary>いずれかの条件が変わったときに発火（購読側が再フィルタ）。</summary>
    public event EventHandler? Changed;

    private void Apply() => Changed?.Invoke(this, EventArgs.Empty);

    public FilterViewModel()
    {
        // 初期状態は有効。条件未指定なので結果的に全件表示になる。
        Enabled = true;
    }

    /// <summary>現在の条件を永続化用スナップショットへ書き出す（終了時）。</summary>
    public FilterState CaptureState() => new()
    {
        Enabled = Enabled,
        RatingValue = RatingValue,
        RatingGreaterEqual = RatingGreaterEqual,
        NoRating = NoRating,
        FlagAccept = FlagAccept,
        FlagNeutral = FlagNeutral,
        FlagReject = FlagReject,
        Red = Red,
        Yellow = Yellow,
        Green = Green,
        Blue = Blue,
        Purple = Purple,
    };

    /// <summary>スナップショットから条件を復元する（起動時）。各 setter 経由で Core モデル・UI にも反映される。</summary>
    public void ApplyState(FilterState s)
    {
        RatingValue = s.RatingValue;
        RatingGreaterEqual = s.RatingGreaterEqual;
        NoRating = s.NoRating;
        FlagAccept = s.FlagAccept;
        FlagNeutral = s.FlagNeutral;
        FlagReject = s.FlagReject;
        Red = s.Red;
        Yellow = s.Yellow;
        Green = s.Green;
        Blue = s.Blue;
        Purple = s.Purple;
        Enabled = s.Enabled; // 最後に設定（Changed 発火で確定）
    }

    [ObservableProperty]
    public partial bool Enabled { get; set; }
    partial void OnEnabledChanged(bool value) { Model.Enabled = value; Apply(); }

    // --- レーティング ---

    [ObservableProperty]
    public partial int RatingValue { get; set; }
    partial void OnRatingValueChanged(int value) { Model.RatingValue = value; Apply(); }

    [ObservableProperty]
    public partial bool NoRating { get; set; }
    partial void OnNoRatingChanged(bool value) { Model.NoRating = value; Apply(); }

    /// <summary>レーティング比較が ≧ か（false なら ＝）。</summary>
    [ObservableProperty]
    public partial bool RatingGreaterEqual { get; set; } = true;
    partial void OnRatingGreaterEqualChanged(bool value)
    {
        Model.RatingCompareMode = value ? RatingCompareMode.GreaterEqual : RatingCompareMode.Equal;
        OnPropertyChanged(nameof(RatingCompareGlyph));
        Apply();
    }

    /// <summary>比較トグルボタンの表示（≧ / ＝）。</summary>
    public string RatingCompareGlyph => RatingGreaterEqual ? "≧" : "＝";

    /// <summary>比較方法（≧ ⇄ ＝）を切り替える。</summary>
    public void ToggleCompareMode() => RatingGreaterEqual = !RatingGreaterEqual;

    // --- フラグ ---

    [ObservableProperty]
    public partial bool FlagAccept { get; set; }
    partial void OnFlagAcceptChanged(bool value) { Model.FlagAccept = value; Apply(); }

    [ObservableProperty]
    public partial bool FlagNeutral { get; set; }
    partial void OnFlagNeutralChanged(bool value) { Model.FlagNeutral = value; Apply(); }

    [ObservableProperty]
    public partial bool FlagReject { get; set; }
    partial void OnFlagRejectChanged(bool value) { Model.FlagReject = value; Apply(); }

    // --- カラーラベル ---

    [ObservableProperty]
    public partial bool Red { get; set; }
    partial void OnRedChanged(bool value) { Model.SetColor(ColorLabel.Red, value); Apply(); }

    [ObservableProperty]
    public partial bool Yellow { get; set; }
    partial void OnYellowChanged(bool value) { Model.SetColor(ColorLabel.Yellow, value); Apply(); }

    [ObservableProperty]
    public partial bool Green { get; set; }
    partial void OnGreenChanged(bool value) { Model.SetColor(ColorLabel.Green, value); Apply(); }

    [ObservableProperty]
    public partial bool Blue { get; set; }
    partial void OnBlueChanged(bool value) { Model.SetColor(ColorLabel.Blue, value); Apply(); }

    [ObservableProperty]
    public partial bool Purple { get; set; }
    partial void OnPurpleChanged(bool value) { Model.SetColor(ColorLabel.Purple, value); Apply(); }
}
