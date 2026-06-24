using Microsoft.UI.Xaml;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// イマーシブ表示（メイン全域表示）の切替。右パネル（ルーペ＋ナビ）とフィルムストリップを畳み、
/// <c>MainCanvas</c> を Grid 全域へ広げる。F キー（修飾子なし）でトグルする。
/// 全画面（F11）・左ペイン非表示とは独立した PreviewControl 内のみの操作。
/// 状態はセッション中保持・永続化しない（再起動で通常の 3 パネルに戻る）。
/// </summary>
public sealed partial class PreviewControl
{
    private bool _immersive;

    // 畳む直前の寸法を退避（戻すときに復元）。
    private GridLength _savedRightPanelWidth = new(260);
    private double _savedRightPanelMinWidth = 160;
    private GridLength _savedFilmStripHeight = new(118);
    private double _savedFilmStripMinHeight = 80;

    /// <summary>
    /// 右パネル＋フィルムストリップの表示／非表示をトグルする。畳むと <c>MainCanvas</c> が
    /// Grid 全域を占め、<see cref="MainCanvas_SizeChanged"/>→<c>SetCanvasSize</c> で自動的に再フィットする。
    /// </summary>
    private void ToggleImmersive()
    {
        if (_immersive)
        {
            // 戻す: 退避していた寸法を復元し、右パネル/フィルムストリップとスプリッターを再表示。
            RightPanelColumn.MinWidth = _savedRightPanelMinWidth;
            RightPanelColumn.Width = _savedRightPanelWidth;
            RightSplitter.Visibility = Visibility.Visible;
            RightPanel.Visibility = Visibility.Visible;

            FilmStripRow.MinHeight = _savedFilmStripMinHeight;
            FilmStripRow.Height = _savedFilmStripHeight;
            FilmSplitter.Visibility = Visibility.Visible;
            FilmStrip.Visibility = Visibility.Visible;
        }
        else
        {
            // 畳む: 現在の寸法を退避してから 0 にし、右パネル/フィルムストリップとスプリッターを隠す。
            // MinWidth/MinHeight も 0 にしないと Width/Height=0 が効かない。
            _savedRightPanelWidth = RightPanelColumn.Width;
            _savedRightPanelMinWidth = RightPanelColumn.MinWidth;
            _savedFilmStripHeight = FilmStripRow.Height;
            _savedFilmStripMinHeight = FilmStripRow.MinHeight;

            RightPanelColumn.MinWidth = 0;
            RightPanelColumn.Width = new GridLength(0);
            RightSplitter.Visibility = Visibility.Collapsed;
            RightPanel.Visibility = Visibility.Collapsed;

            FilmStripRow.MinHeight = 0;
            FilmStripRow.Height = new GridLength(0);
            FilmSplitter.Visibility = Visibility.Collapsed;
            FilmStrip.Visibility = Visibility.Collapsed;
        }

        _immersive = !_immersive;
    }
}
