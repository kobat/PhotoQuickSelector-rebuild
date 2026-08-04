using System;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// 2段構成のメモリ掃除係。<see cref="PixelBufferPool"/>／<see cref="PreviewBitmapCache"/> はバイト長
/// 完全一致でしか在庫を再利用できないため、画像サイズ混在フォルダでは再利用ミスが連発し、破棄された
/// 200MB 級 <c>byte[]</c> が LOH のゴミとして gen2 GC まで残留する（decommit も遅延し、ワーキングセットが
/// 予算の 2 倍近くまで膨らむ）。
/// <para>
/// 対策として役割を 2 段に分ける:
/// <list type="bullet">
///   <item>
///   バックグラウンド gen2 GC（<see cref="NoteDiscarded"/>）: 捨てバイトの累計が閾値を超えたら発行する。
///   ブロッキングしない（<c>blocking: false</c>）ので連打中でも UI を止めず、ゴミが際限なく積み上がるのを
///   防ぐ。ConserveMemory=7 設定済みのため、GC さえ走れば空きリージョンは OS へ返る。
///   </item>
///   <item>
///   アイドル時の完全 GC（<see cref="Tick"/>）: 操作が止まってから <see cref="_idleDelay"/> 経っても
///   まだ掃除が必要（dirty）なら、Ctrl+M 相当のブロッキング完全 GC（ファイナライザ待ち込み・~300ms）で
///   基準値まで戻す。操作中に当たるとカクつくので、活動が途絶えたときだけ発行する。
///   </item>
/// </list>
/// </para>
/// <para>
/// すべて UI スレッドから呼ばれる前提でロックは持たない（ホスト側のタイマー／イベントハンドラはいずれも
/// UI スレッドで実行されるため）。純 C#（WinUI/WinRT 非依存）なので単体テスト可能。
/// </para>
/// </summary>
internal sealed class MemoryJanitor
{
    private readonly long _backgroundGcThresholdBytes;
    private readonly TimeSpan _idleDelay;
    private readonly Action _requestBackgroundGc;
    private readonly Action _requestFullGc;
    private readonly Func<DateTime> _clock;

    private long _discardedSinceBgc;
    private DateTime _lastActivity;

    /// <param name="backgroundGcThresholdBytes">この量の捨てバイトが溜まるたびにバックグラウンド GC を1回発行する。</param>
    /// <param name="idleDelay">最終活動からこの時間ノー操作が続いたら、dirty ならアイドル完全 GC を発行する。</param>
    /// <param name="requestBackgroundGc">バックグラウンド gen2 GC を要求するコールバック。</param>
    /// <param name="requestFullGc">ブロッキングの完全 GC を要求するコールバック。</param>
    /// <param name="clock">現在時刻の取得元。省略時は <see cref="DateTime.UtcNow"/>（テストでは差し替えて時間経過を制御する）。</param>
    public MemoryJanitor(
        long backgroundGcThresholdBytes,
        TimeSpan idleDelay,
        Action requestBackgroundGc,
        Action requestFullGc,
        Func<DateTime>? clock = null)
    {
        _backgroundGcThresholdBytes = backgroundGcThresholdBytes;
        _idleDelay = idleDelay;
        _requestBackgroundGc = requestBackgroundGc;
        _requestFullGc = requestFullGc;
        _clock = clock ?? (() => DateTime.UtcNow);
        _lastActivity = _clock();
    }

    /// <summary>アイドル完全 GC（<see cref="Tick"/>）がまだ必要かどうか。ホストがタイマーを回すかの判定に使う。</summary>
    public bool IsDirty { get; private set; }

    /// <summary>ユーザー操作があったことを記録する（最終活動時刻を更新し、アイドル判定を先送りする）。</summary>
    public void NoteActivity() => _lastActivity = _clock();

    /// <summary>
    /// プールミス等でゴミになったバイト数を記録する。0 以下は無視。累積が閾値
    /// （<see cref="_backgroundGcThresholdBytes"/>）に達したらバックグラウンド GC を1回発行して累積を0に戻す
    /// （<see cref="IsDirty"/> は立てたまま＝アイドル完全 GC の対象としては残す）。
    /// </summary>
    public void NoteDiscarded(long bytes)
    {
        if (bytes <= 0) return;

        _discardedSinceBgc += bytes;
        IsDirty = true;

        if (_discardedSinceBgc >= _backgroundGcThresholdBytes)
        {
            _requestBackgroundGc();
            _discardedSinceBgc = 0;
        }
    }

    /// <summary>
    /// ホストの周期タイマーから呼ぶ。dirty かつ最終活動から <see cref="_idleDelay"/> 以上経っていれば
    /// 完全 GC を1回発行し、dirty を解除する。条件を満たさなければ何もしない。
    /// </summary>
    public void Tick()
    {
        if (!IsDirty) return;
        if (_clock() - _lastActivity < _idleDelay) return;

        _requestFullGc();
        IsDirty = false;
        _discardedSinceBgc = 0;
    }
}
