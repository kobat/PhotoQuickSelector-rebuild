using System.Collections.Generic;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// 画像情報パネルの 1 グループ（見出し＋行）。File／評価／EXIF 各ディレクトリを同じ形で並べるための器。
///
/// 行の型はグループによって違う（EXIF＝<see cref="PhotoQuickSelector.Core.ExifTagEntry"/>／
/// 評価＝<see cref="EvaluationInfoRow"/>）ため <c>object</c> で受ける。描画側のテンプレートは
/// <see cref="InfoRowTemplateSelector"/> が型で振り分ける。<c>IReadOnlyList&lt;T&gt;</c> は共変なので
/// Core 側のリストをそのまま渡してもコピーは発生しない。
/// </summary>
public sealed class InfoGroup
{
    public InfoGroup(string name, IReadOnlyList<object> rows)
    {
        Name = name;
        Rows = rows;
    }

    /// <summary>グループ見出し（EXIF ディレクトリ名、または「評価」等のローカライズ済み名）。</summary>
    public string Name { get; }

    /// <summary>このグループの行。<c>CollectionViewSource.ItemsPath</c> がこの名前を指す。</summary>
    public IReadOnlyList<object> Rows { get; }
}
