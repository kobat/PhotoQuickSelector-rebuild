using System;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using PhotoQuickSelector_App.ViewModels;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// 情報オーバーレイ（<see cref="MainViewModel.OverlayKind"/> ＝評価バッジ／詳細情報）の
/// 「切替時のみ」表示（<see cref="MainViewModel.CurrentOverlayTransient"/>）を担う。
/// 常時表示のときは両オーバーレイの Opacity を常に 1 に保つ（Storyboard は起動しない）。
/// 切替時のみのときは、写真切替・評価変更・種類/タイミング切替・プレビュー入場のたびに
/// アクティブなオーバーレイを Opacity=1 で見せ、選択中の種類の保持時間だけ保持後
/// フェード時間をかけてフェードアウトする（連打時はタイマー再スタート。保持/フェード時間は種類別）。
/// フェード完了後も Visibility は触らない（IsHitTestVisible=False の要素なので Opacity=0 のままで無害）。
/// </summary>
public sealed partial class PreviewControl
{
    // 保持時間・フェード時間は「情報オーバーレイの種類ごと」に独立（評価バッジ／詳細情報）。
    // 生の設定値（ms）を種類別に保持し、Storyboard には現在の種類の値だけを焼き込む。
    // 既定値は AppSettings の初期値と一致させる（設定注入前にトリガされても不自然にならないよう）。
    private int _badgeHoldMs;          // 既定 0（即フェード開始）
    private int _badgeFadeMs = 400;
    private int _fullHoldMs = 1000;
    private int _fullFadeMs = 400;

    // Storyboard に現在焼き込まれている保持/フェード時間（種類切替・設定変更で作り直す判定に使う）。
    private TimeSpan _overlayHold = TimeSpan.FromMilliseconds(0);
    private TimeSpan _overlayFade = TimeSpan.FromMilliseconds(400);

    /// <summary>評価変更で「切替時のみ」表示を再トリガすべきプロパティ名。</summary>
    private static readonly HashSet<string> OverlayEvalTriggerProperties = new(StringComparer.Ordinal)
    {
        nameof(PhotoItemViewModel.RatingStars),
        nameof(PhotoItemViewModel.RatingVisibility),
        nameof(PhotoItemViewModel.FlagVisibility),
        nameof(PhotoItemViewModel.ColorDotsVisibility),
        nameof(PhotoItemViewModel.HasAnyEvalVisibility),
    };

    private Storyboard? _overlayFadeStoryboard;
    private DoubleAnimationUsingKeyFrames? _overlayFadeAnimation;

    /// <summary>評価変更を監視中の写真（焦点の写真に追従。付け替え式で購読漏れ/リークを防ぐ）。</summary>
    private PhotoItemViewModel? _overlayWatchedPhoto;

    /// <summary>現在の <see cref="MainViewModel.OverlayKind"/> に対応する表示要素（Off なら null）。</summary>
    private FrameworkElement? ActiveOverlayElement =>
        _viewModel?.OverlayKind switch
        {
            InfoOverlayKind.Badge => RatingBadge,
            InfoOverlayKind.Full => InfoOverlay,
            _ => null,
        };

    /// <summary>
    /// 評価変更の監視対象を付け替える。焦点写真が変わるたびに <see cref="PreviewControl.OnViewModelPropertyChanged"/>
    /// （FocusedPhoto ケース）から呼ばれる。旧写真の購読解除→新写真の購読を必ずセットで行う。
    /// </summary>
    private void SubscribeOverlayWatchedPhoto(PhotoItemViewModel? photo)
    {
        if (ReferenceEquals(_overlayWatchedPhoto, photo)) return;
        if (_overlayWatchedPhoto != null)
            _overlayWatchedPhoto.PropertyChanged -= OnOverlayWatchedPhotoPropertyChanged;
        _overlayWatchedPhoto = photo;
        if (_overlayWatchedPhoto != null)
            _overlayWatchedPhoto.PropertyChanged += OnOverlayWatchedPhotoPropertyChanged;
    }

    private void OnOverlayWatchedPhotoPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != null && OverlayEvalTriggerProperties.Contains(e.PropertyName))
            RestartOverlayFade();
    }

    /// <summary>
    /// <see cref="MainViewModel.OverlayKind"/>/<see cref="MainViewModel.CurrentOverlayTransient"/> 変更時に呼ぶ。
    /// 常時表示へ切り替わったときは Opacity=1 に戻して Storyboard を止め、切替時のみへ切り替わった
    /// （または種類が変わった）ときはフェードを再スタートする。
    /// </summary>
    private void ApplyOverlayTiming()
    {
        if (_viewModel?.CurrentOverlayTransient == true)
        {
            RestartOverlayFade();
        }
        else
        {
            _overlayFadeStoryboard?.Stop();
            InfoOverlay.Opacity = 1;
            RatingBadge.Opacity = 1;
        }
    }

    /// <summary>
    /// アクティブなオーバーレイを Opacity=1 で見せ、保持→フェードのアニメを再スタートする。
    /// 選択中の種類が常時表示（<see cref="MainViewModel.CurrentOverlayTransient"/>=false）または
    /// アクティブ要素なし（Off）なら何もしない。保持/フェード時間は選択中の種類の値を焼き込む。
    /// </summary>
    private void RestartOverlayFade()
    {
        if (_viewModel?.CurrentOverlayTransient != true) return;

        var target = ActiveOverlayElement;
        if (target == null)
        {
            _overlayFadeStoryboard?.Stop();
            return;
        }

        RebakeOverlayTimingsIfChanged(); // 選択中の種類の保持/フェード時間へ Storyboard を追従させる
        EnsureOverlayFadeStoryboard();
        _overlayFadeStoryboard!.Stop();
        target.Opacity = 1; // Storyboard の反映を待たず即座に見せる
        Storyboard.SetTarget(_overlayFadeAnimation!, target);
        _overlayFadeStoryboard.Begin();
    }

    /// <summary>現在選択中のオーバーレイ種類に応じた保持/フェード時間（クランプ済み）を返す。</summary>
    private (TimeSpan hold, TimeSpan fade) CurrentOverlayTimings()
    {
        var (holdMs, fadeMs) = _viewModel?.OverlayKind switch
        {
            InfoOverlayKind.Badge => (_badgeHoldMs, _badgeFadeMs),
            InfoOverlayKind.Full => (_fullHoldMs, _fullFadeMs),
            _ => (_badgeHoldMs, _badgeFadeMs), // Off: フェードは走らないのでどちらでも可
        };
        // 過大値でも UI が固まらないようクランプ（保持は 0＝即フェード開始も許す）。
        var hold = TimeSpan.FromMilliseconds(Math.Clamp(holdMs, 0, 60000));
        // フェードは最低 1ms（0 だと Discrete/Easing の KeyTime が重なり消えないため）。
        var fade = TimeSpan.FromMilliseconds(Math.Clamp(fadeMs, 1, 60000));
        return (hold, fade);
    }

    /// <summary>選択中の種類の保持/フェード時間が Storyboard の焼き込み値と異なれば、キャッシュを破棄して次回再構築させる。</summary>
    private void RebakeOverlayTimingsIfChanged()
    {
        var (hold, fade) = CurrentOverlayTimings();
        if (hold == _overlayHold && fade == _overlayFade) return;

        _overlayHold = hold;
        _overlayFade = fade;
        _overlayFadeStoryboard?.Stop();
        _overlayFadeStoryboard = null; // 次回 EnsureOverlayFadeStoryboard で新しい時間で再構築
        _overlayFadeAnimation = null;
    }

    /// <summary>
    /// 種類ごとの保持時間／フェード時間を設定から反映する（<see cref="ApplyPreviewSettings"/> から呼ぶ）。
    /// 生の ms 値を控えるだけで Storyboard は作り直さない（実際の焼き込みは <see cref="RebakeOverlayTimingsIfChanged"/>
    /// が選択中の種類に応じて行う）。切替時のみ表示中なら即座に再トリガして体感変化を確認できるようにする。
    /// </summary>
    private void ApplyOverlayFadeTimings(AppSettings s)
    {
        _badgeHoldMs = s.BadgeHoldMs;
        _badgeFadeMs = s.BadgeFadeMs;
        _fullHoldMs = s.FullHoldMs;
        _fullFadeMs = s.FullFadeMs;
        if (_viewModel?.CurrentOverlayTransient == true) RestartOverlayFade();
    }

    private void EnsureOverlayFadeStoryboard()
    {
        if (_overlayFadeStoryboard != null) return;

        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 1.0 });
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = _overlayHold, Value = 1.0 });
        animation.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = _overlayHold + _overlayFade,
            Value = 0.0,
            // 線形だと消え際が唐突。EaseIn（出だしゆっくり）で「ふわっと」消す。
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
        });
        Storyboard.SetTargetProperty(animation, "Opacity");

        _overlayFadeAnimation = animation;
        _overlayFadeStoryboard = new Storyboard();
        _overlayFadeStoryboard.Children.Add(animation);
    }
}
