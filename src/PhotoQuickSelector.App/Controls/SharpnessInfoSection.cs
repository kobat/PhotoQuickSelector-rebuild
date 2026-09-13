using System;
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
/// 行は 3 系統のコレクションで公開する。<see cref="Rows"/> は画像情報パネル向けの結合コレクション
/// （<c>PreviewControl.ExifPanel.cs</c> がこれを 1 グループとして使う）。
/// <see cref="TenengradRows"/>（最大タイル→ブレ幅→被写体領域→AF窓→全体→異方性の順・6行固定）と
/// <see cref="ExtraRows"/>（比較4手法・<see cref="SharpnessMode.All"/> のみ4行、それ以外は空）は
/// ルーペオーバーレイ（<c>PreviewControl.xaml</c> の <c>SharpnessLoupeOverlay</c>）が見出しを分けて
/// 表示するために分離した参照。3 つとも同じ <see cref="EvaluationInfoRow"/> インスタンスを指すので、
/// <see cref="Update"/> の値書き換えは全コレクションの <c>x:Bind</c> へそのまま伝わる。
/// </para>
/// <para>
/// ブレ幅／被写体領域の2行は <see cref="SubjectRegionAnalyzer"/>（被写体領域＝高エッジ密度タイル群の
/// 方向別エッジ幅解析）の結果を表示する。最大タイルが一方向ブレの被写体で「たまたま残った鋭いエッジ」を
/// 誤検出しうる問題への補完情報として、モードを問わず（Tenengrad のみ／全手法のいずれでも）表示する。
/// AF 窓行は、被写体領域側のエッジ画素数が閾値（<see cref="AfNoEdgeThreshold"/>）未満（空振り＝AF窓が
/// 背景に外れた等）なら「エッジなし」を注記に追加する。
/// </para>
/// <para>
/// Tenengrad の 6 行は <see cref="SharpnessMode.Tenengrad"/> 以上で常に出す。比較 4 手法の行は
/// <see cref="SharpnessMode.All"/> のときだけ追加する。モード変更で <see cref="Rows"/> の行数が
/// 変わるため、モード変更時は呼び出し側（<c>PreviewControl.ExifPanel.cs</c> の <c>RenderExifForFocus</c>）
/// が画像情報パネル側は全体再構築する。<see cref="ObservableCollection{T}"/> なので、値だけの変更
/// （モード不変）ならルーペオーバーレイの <c>ItemsControl</c> もこのコレクションの変更通知だけで追従する。
/// </para>
/// </summary>
public sealed class SharpnessInfoSection
{
    private const string UnknownValue = "—";

    /// <summary>AF 窓のエッジ画素数がこれ未満なら「エッジなし」（空振り＝AF窓が背景に外れた等）とみなす
    /// 閾値。ヒューリスティックな値。<see cref="PhotoItemViewModel.SubjectRegionText"/> と共有するため
    /// ここに集約する（数値の重複を避ける）。</summary>
    public const long AfNoEdgeThreshold = 200;

    private readonly EvaluationInfoRow _afWindow = new(Loc.Get("Sharp_AfWindow"));
    private readonly EvaluationInfoRow _maxTile = new(Loc.Get("Sharp_MaxTile"));
    private readonly EvaluationInfoRow _subjectBlur = new(Loc.Get("Sharp_SubjectBlur"));
    private readonly EvaluationInfoRow _subjectRegion = new(Loc.Get("Sharp_SubjectRegion"));
    private readonly EvaluationInfoRow _global = new(Loc.Get("Sharp_Global"));
    private readonly EvaluationInfoRow _anisotropy = new(Loc.Get("Sharp_Anisotropy"));
    private readonly EvaluationInfoRow _lapv = new(Loc.Get("Sharp_LapV"));
    private readonly EvaluationInfoRow _brenner = new(Loc.Get("Sharp_Brenner"));
    private readonly EvaluationInfoRow _reblur = new(Loc.Get("Sharp_Reblur"));
    private readonly EvaluationInfoRow _edgeWidth = new(Loc.Get("Sharp_EdgeWidth"));

    /// <summary>グループ見出し。</summary>
    public string GroupName { get; } = Loc.Get("Info_SharpnessGroup");

    /// <summary>
    /// 現在のモードに応じた可視行（None なら空・Tenengrad なら4行・全手法なら8行）。並び順は
    /// 最大タイル→AF窓→全体→異方性→（全手法のみ）比較4手法。
    /// <see cref="ObservableCollection{T}"/> なので Clear/Add の変更が両表示（画像情報パネル・
    /// ルーペオーバーレイ）へそのまま伝わる。
    /// </summary>
    public ObservableCollection<object> Rows { get; } = new();

    /// <summary>Tenengrad の4行（最大タイル→AF窓→全体→異方性）。モード None なら空。</summary>
    public ObservableCollection<object> TenengradRows { get; } = new();

    /// <summary>比較4手法の行。<see cref="SharpnessMode.All"/> のときだけ中身が入る（それ以外は空）。</summary>
    public ObservableCollection<object> ExtraRows { get; } = new();

    /// <summary>写真の鮮鋭度スコアを行へ反映し、モードに応じて可視行を組み立て直す。</summary>
    public void Update(PhotoItemViewModel photo, SharpnessMode mode)
    {
        Rows.Clear();
        TenengradRows.Clear();
        ExtraRows.Clear();
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

        if (photo.SubjectRegion is { } r)
        {
            if (r.TileCount == 0)
            {
                _subjectBlur.Value = Loc.Get("Sharp_NoSubject");
                _subjectBlur.UpdatedAtText = "";
                _subjectRegion.Value = Loc.Get("Sharp_NoSubject");
                _subjectRegion.UpdatedAtText = "";
            }
            else
            {
                _subjectBlur.Value = double.IsNaN(r.WorstWidth)
                    ? UnknownValue : r.WorstWidth.ToString("F1", CultureInfo.InvariantCulture) + " px";
                _subjectBlur.UpdatedAtText = r.WorstBin >= 0 && !double.IsNaN(r.WorstWidth) && !double.IsNaN(r.WidthRatio)
                    ? string.Format(CultureInfo.InvariantCulture, Loc.Get("Sharp_SubjectDirFormat"),
                        (int)Math.Round(SubjectRegionScore.BinCenterDegrees(r.WorstBin, r.BinMedianWidths.Count)),
                        r.WidthRatio.ToString("F2", CultureInfo.InvariantCulture))
                    : "";
                _subjectRegion.Value = string.Format(CultureInfo.InvariantCulture, Loc.Get("Sharp_SubjectTilesFormat"), r.TileCount);
                _subjectRegion.UpdatedAtText = $"@{r.Bounds.X},{r.Bounds.Y} {r.Bounds.Width}×{r.Bounds.Height}";
            }

            // AF 窓の空振り注記は被写体タイルの有無と無関係（AfWindowEdgeCount/PixelCount は Empty でも保持される）。
            _afWindow.UpdatedAtText = r.AfWindowPixelCount > 0 && r.AfWindowEdgeCount < AfNoEdgeThreshold
                ? Loc.Get("Sharp_AfNoEdges")
                : "";
        }
        else
        {
            SetComputing(_subjectBlur, _subjectRegion);
        }

        // 並びは 最大タイル→ブレ幅→被写体領域→AF窓→全体→異方性（最大タイルを先頭に＝画面上のオレンジ枠、
        // 被写体領域をその直後に＝画面上のシアン枠と対応させる）。
        Rows.Add(_maxTile);
        Rows.Add(_subjectBlur);
        Rows.Add(_subjectRegion);
        Rows.Add(_afWindow);
        Rows.Add(_global);
        Rows.Add(_anisotropy);
        TenengradRows.Add(_maxTile);
        TenengradRows.Add(_subjectBlur);
        TenengradRows.Add(_subjectRegion);
        TenengradRows.Add(_afWindow);
        TenengradRows.Add(_global);
        TenengradRows.Add(_anisotropy);

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
        ExtraRows.Add(_lapv);
        ExtraRows.Add(_brenner);
        ExtraRows.Add(_reblur);
        ExtraRows.Add(_edgeWidth);
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

    /// <summary>比較手法 1 個ぶんの行を「最大タイル ／ AF 窓」の数値ペアで埋める（最大タイルを左に＝
    /// Tenengrad 4行と同じ並び順に揃える）。</summary>
    private static void SetPair(EvaluationInfoRow row, MetricScores scores, string format, string suffix = "")
    {
        string tile = double.IsNaN(scores.MaxTile)
            ? UnknownValue : scores.MaxTile.ToString(format, CultureInfo.InvariantCulture) + suffix;
        string af = double.IsNaN(scores.AfWindow)
            ? UnknownValue : scores.AfWindow.ToString(format, CultureInfo.InvariantCulture) + suffix;
        row.Value = $"{tile} ／ {af}";
        row.UpdatedAtText = "";
    }

    private static string FormatN0(double value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
