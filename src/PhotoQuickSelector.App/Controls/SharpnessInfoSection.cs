using System.Collections.ObjectModel;
using System.Globalization;
using PhotoQuickSelector.Core;
using PhotoQuickSelector_App.ViewModels;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// 画像情報パネル／プレビューのルーペオーバーレイに出す「鮮鋭度」グループ。行オブジェクトを固定長で
/// 作り置きし内容だけを差し替える設計は <see cref="EvaluationInfoSection"/> と同じ（写真切替・値確定の
/// たびにグループ化 ListView を再構築しない）。行の型も同じ <see cref="EvaluationInfoRow"/> を流用する
/// （画像情報パネルの <c>InfoRowTemplateSelector</c>・<c>EvaluationRowTemplate</c> にそのまま乗る）。
/// <para>
/// Tenengrad の 4 行（AF 窓／最大タイル／全体／異方性）は <see cref="SharpnessMode.Tenengrad"/> 以上で常に
/// 出す。比較 4 手法の行は <see cref="SharpnessMode.All"/> のときだけ追加する。モード変更で行数が
/// 変わるため、モード変更時は呼び出し側（<c>PreviewControl.ExifPanel.cs</c> の <c>RenderExifForFocus</c>）
/// が画像情報パネル側は全体再構築する。<see cref="Rows"/> 自体は <see cref="ObservableCollection{T}"/>
/// なので、値だけの変更（モード不変）ならルーペオーバーレイの <c>ItemsControl</c> もこのコレクションの
/// 変更通知だけで追従する。
/// </para>
/// </summary>
public sealed class SharpnessInfoSection
{
    private const string UnknownValue = "—";

    private readonly EvaluationInfoRow _afWindow = new(Loc.Get("Sharp_AfWindow"));
    private readonly EvaluationInfoRow _maxTile = new(Loc.Get("Sharp_MaxTile"));
    private readonly EvaluationInfoRow _global = new(Loc.Get("Sharp_Global"));
    private readonly EvaluationInfoRow _anisotropy = new(Loc.Get("Sharp_Anisotropy"));
    private readonly EvaluationInfoRow _lapv = new(Loc.Get("Sharp_LapV"));
    private readonly EvaluationInfoRow _brenner = new(Loc.Get("Sharp_Brenner"));
    private readonly EvaluationInfoRow _reblur = new(Loc.Get("Sharp_Reblur"));
    private readonly EvaluationInfoRow _edgeWidth = new(Loc.Get("Sharp_EdgeWidth"));

    /// <summary>グループ見出し。</summary>
    public string GroupName { get; } = Loc.Get("Info_SharpnessGroup");

    /// <summary>
    /// 現在のモードに応じた可視行（None なら空・Tenengrad なら4行・全手法なら8行）。
    /// <see cref="ObservableCollection{T}"/> なので Clear/Add の変更が両表示（画像情報パネル・
    /// ルーペオーバーレイ）へそのまま伝わる。
    /// </summary>
    public ObservableCollection<object> Rows { get; } = new();

    /// <summary>写真の鮮鋭度スコアを行へ反映し、モードに応じて可視行を組み立て直す。</summary>
    public void Update(PhotoItemViewModel photo, SharpnessMode mode)
    {
        Rows.Clear();
        if (mode == SharpnessMode.None) return;

        if (photo.Sharpness is { } s)
        {
            _afWindow.Value = double.IsNaN(s.AfWindow) ? UnknownValue : FormatN0(s.AfWindow);
            _afWindow.UpdatedAtText = "";
            _maxTile.Value = FormatN0(s.MaxTile);
            _maxTile.UpdatedAtText = $"@{s.MaxTileX},{s.MaxTileY}";
            _global.Value = FormatN0(s.Global);
            _global.UpdatedAtText = "";
            double aniso = double.IsNaN(s.AfWindowAnisotropy) ? s.Anisotropy : s.AfWindowAnisotropy;
            _anisotropy.Value = double.IsNaN(aniso) ? UnknownValue : aniso.ToString("F2", CultureInfo.InvariantCulture);
            _anisotropy.UpdatedAtText = "";
        }
        else
        {
            SetComputing(_afWindow, _maxTile, _global, _anisotropy);
        }

        Rows.Add(_afWindow);
        Rows.Add(_maxTile);
        Rows.Add(_global);
        Rows.Add(_anisotropy);

        if (mode != SharpnessMode.All) return;

        if (photo.SharpnessExtras is { } e)
        {
            SetPair(_lapv, e.LaplacianVariance, "N0");
            SetPair(_brenner, e.Brenner, "N0");
            SetPair(_reblur, e.Reblur, "F3");
            SetPair(_edgeWidth, e.EdgeWidth, "F2", "px");
        }
        else
        {
            SetComputing(_lapv, _brenner, _reblur, _edgeWidth);
        }

        Rows.Add(_lapv);
        Rows.Add(_brenner);
        Rows.Add(_reblur);
        Rows.Add(_edgeWidth);
    }

    private static void SetComputing(params EvaluationInfoRow[] rows)
    {
        string computing = Loc.Get("Sharp_Computing");
        foreach (var row in rows)
        {
            row.Value = computing;
            row.UpdatedAtText = "";
        }
    }

    /// <summary>比較手法 1 個ぶんの行を「AF 窓 ／ 最大タイル」の数値ペアで埋める。</summary>
    private static void SetPair(EvaluationInfoRow row, MetricScores scores, string format, string suffix = "")
    {
        string af = double.IsNaN(scores.AfWindow)
            ? UnknownValue : scores.AfWindow.ToString(format, CultureInfo.InvariantCulture) + suffix;
        string tile = double.IsNaN(scores.MaxTile)
            ? UnknownValue : scores.MaxTile.ToString(format, CultureInfo.InvariantCulture) + suffix;
        row.Value = $"{af} ／ {tile}";
        row.UpdatedAtText = "";
    }

    private static string FormatN0(double value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
