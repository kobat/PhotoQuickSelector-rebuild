using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// フィルムストリップのサムネイル寸法を保持する小さな観測対象。
/// 高さ調節（<see cref="PreviewControl"/> のスプリッター）に合わせてセルを拡縮するため、
/// <see cref="Edge"/> を変更すると各セルが追従する。
/// <para>
/// ListView の <c>DataTemplate</c> 内からはコントロール側プロパティへ <c>x:Bind</c> できない
/// （テンプレートの DataType は <c>PhotoItemViewModel</c>）。そこで本オブジェクトをリソースに置き、
/// 各セルは <c>{Binding Edge, Source={StaticResource FilmMetrics}}</c> のように Source 指定で参照する。
/// </para>
/// </summary>
public sealed class FilmStripMetrics : INotifyPropertyChanged
{
    private double _edge = 84;

    /// <summary>サムネイル画像セル（正方形）の一辺(px)。</summary>
    public double Edge
    {
        get => _edge;
        set
        {
            if (_edge == value) return;
            _edge = value;
            OnChanged();
            OnChanged(nameof(ItemWidth));
        }
    }

    /// <summary>セル外側（ファイル名行）の幅。アクセント外枠＋カラーラベル枠(各 3px×左右=12)ぶんを足す。</summary>
    public double ItemWidth => _edge + 12;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
