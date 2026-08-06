using System;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// 回収待ちメモリの概算（メモリ掃除係のブロッキング段の判定材料。ホストが計測して渡す）。
/// </summary>
/// <param name="GarbageBytes">未回収の不要マネージドメモリ（<c>GC.GetTotalMemory(false) − 生きていることが
/// 確実な常駐分</c>）。gen2 GC が確保のバーストに速度負けしているときに増える。</param>
/// <param name="InventoryBytes">回収済みだが OS へ未返却の分（<c>GC.GetGCMemoryInfo().TotalCommittedBytes −
/// GC.GetTotalMemory(false)</c>）。gen2 GC は走ったが decommit（OS への返却）が追いついていないときに増える。</param>
internal readonly record struct PendingMemoryEstimate(long GarbageBytes, long InventoryBytes)
{
    /// <summary>判定に使う合計（ゴミ＋在庫）。</summary>
    public long TotalBytes => GarbageBytes + InventoryBytes;
}

/// <summary>
/// 3段構成のメモリ掃除係。<see cref="PixelBufferPool"/>／<see cref="PreviewBitmapCache"/> はバイト長
/// 完全一致でしか在庫を再利用できないため、画像サイズ混在フォルダでは再利用ミスが連発し、破棄された
/// 200MB 級 <c>byte[]</c> が LOH のゴミとして gen2 GC まで残留する（decommit も遅延し、ワーキングセットが
/// 予算の 2 倍近くまで膨らむ）。
/// <para>
/// 対策として役割を 3 段に分ける:
/// <list type="bullet">
///   <item>
///   バックグラウンド gen2 GC（<see cref="NoteDiscarded"/>）: 捨てバイトの累計が閾値を超えたら発行する。
///   ブロッキングしない（<c>blocking: false</c>）ので連打中でも UI を止めず、ゴミが際限なく積み上がるのを
///   防ぐ。ConserveMemory=7 設定済みのため、GC さえ走れば空きリージョンは OS へ返る。
///   </item>
///   <item>
///   ブロッキング gen2 GC（<see cref="NoteDiscarded"/>／<see cref="Tick"/>）: 上の背景 GC が確保のバーストに
///   速度負けしたときだけ発行する保険段。判定は<b>ゴミ＋在庫の合計</b>（<see cref="_pendingBytes"/> が返す
///   <see cref="PendingMemoryEstimate.TotalBytes"/>）が閾値（<see cref="BlockingGcThresholdBytes"/>）以上か。
///   単純な「ゴミ量」だけで判定すると、実際に解放される量（ゴミの回収＋在庫の返却）と発動条件が乖離して
///   直感的でない（実測: 閾値 512MB 設定で 1.2GB 解放される、等）ため合算にした。
///   発行する GC は<b>常に Aggressive</b>（ホストが配線。OS への返却まで同期・停止 ~140〜300ms 実測）。
///   軽量な素の Forced を先に試す 2 段方式は採らない: ブロッキング Forced の完了後、返却予定のページは
///   <c>TotalCommittedBytes</c> から同期的に除外される一方で実返却は数秒遅れ、その間どの計測（Mng/Comm）
///   にも写らない＝在庫として検出できず、WS 高止まりを取りこぼすことが実測で確定したため
///   （経緯は HISTORY.md「メモリ掃除係のブロッキング段」節）。
///   落とし穴: 在庫（<see cref="PendingMemoryEstimate.InventoryBytes"/>）の元になる
///   <c>GC.GetGCMemoryInfo().TotalCommittedBytes</c> は直近 GC 完了時点のスナップショットで、GC が走らない
///   間は更新されない＝実態より高止まりして見えることがある。過大判定は再武装ガードと閾値側の余裕で吸収する
///   前提。
///   発火判定は捨てが起きた瞬間（<see cref="NoteDiscarded"/>）だけでなく 5 秒周期の <see cref="Tick"/> でも
///   試みる（バーストが止まった直後、誰も新たな捨てバイトを記録しないまま残債が数秒滞留するのを掃くため）。
///   再武装ガード（<see cref="_discardedSinceBlockingGc"/> が閾値の半分たまるまで再発火しない）は、
///   概算に乗る推定誤差（サムネイル等の他ライブ分の過大評価。実測 ~100-300MB）だけで発火し続けるのを
///   防ぐため＝実チャーン中は閾値の半分ごとの再発火がキャップとして機能する。
///   <see cref="BlockingGcThresholdBytes"/> が 0 以下なら無効。
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
    private readonly Action _requestBlockingGc;
    private readonly Action _requestFullGc;
    private readonly Func<PendingMemoryEstimate> _pendingBytes;
    private readonly Func<DateTime> _clock;

    private long _discardedSinceBgc;
    // ブロッキング段の再武装ガード起算（前回発火 or 生成からの捨てバイト累計）。
    private long _discardedSinceBlockingGc;
    private DateTime _lastActivity;

    /// <param name="backgroundGcThresholdBytes">この量の捨てバイトが溜まるたびにバックグラウンド GC を1回発行する。</param>
    /// <param name="idleDelay">最終活動からこの時間ノー操作が続いたら、dirty ならアイドル完全 GC を発行する。</param>
    /// <param name="requestBackgroundGc">バックグラウンド gen2 GC を要求するコールバック。</param>
    /// <param name="requestBlockingGc">OS への返却まで行うブロッキング gen2 GC（Aggressive）を
    /// 要求するコールバック。</param>
    /// <param name="requestFullGc">ブロッキングの完全 GC を要求するコールバック。</param>
    /// <param name="pendingBytes">回収待ちメモリ（ゴミ＋在庫）の概算の取得元。ブロッキング段の判定にのみ使い、
    /// 捨てバイト条件（<see cref="_discardedSinceBlockingGc"/> が閾値の半分以上）が成立したときだけ呼ぶ
    /// （毎回 GetTotalMemory 等を呼ばないための順序）。</param>
    /// <param name="clock">現在時刻の取得元。省略時は <see cref="DateTime.UtcNow"/>（テストでは差し替えて時間経過を制御する）。</param>
    public MemoryJanitor(
        long backgroundGcThresholdBytes,
        TimeSpan idleDelay,
        Action requestBackgroundGc,
        Action requestBlockingGc,
        Action requestFullGc,
        Func<PendingMemoryEstimate> pendingBytes,
        Func<DateTime>? clock = null)
    {
        _backgroundGcThresholdBytes = backgroundGcThresholdBytes;
        _idleDelay = idleDelay;
        _requestBackgroundGc = requestBackgroundGc;
        _requestBlockingGc = requestBlockingGc;
        _requestFullGc = requestFullGc;
        _pendingBytes = pendingBytes;
        _clock = clock ?? (() => DateTime.UtcNow);
        _lastActivity = _clock();
    }

    /// <summary>アイドル完全 GC（<see cref="Tick"/>）がまだ必要かどうか。ホストがタイマーを回すかの判定に使う。</summary>
    public bool IsDirty { get; private set; }

    /// <summary>
    /// ブロッキング gen2 GC を発行する、回収待ちメモリ（ゴミ＋在庫の合計・概算）のバイト閾値。既定 0＝無効。
    /// 捨てバイト累計が閾値の半分に達し（再武装ガード）、かつそのときの実測合計
    /// （<see cref="_pendingBytes"/> の <see cref="PendingMemoryEstimate.TotalBytes"/>）がこの値以上なら
    /// 発行する。詳細はクラス <see cref="MemoryJanitor"/> の <summary> 参照。
    /// </summary>
    public long BlockingGcThresholdBytes { get; set; }

    /// <summary>ユーザー操作があったことを記録する（最終活動時刻を更新し、アイドル判定を先送りする）。</summary>
    public void NoteActivity() => _lastActivity = _clock();

    /// <summary>
    /// プールミス等でゴミになったバイト数を記録する。0 以下は無視。まず <see cref="TryBlockingGc"/> を試み、
    /// 発火したらその回の段1（背景 GC）判定はスキップする。発火しなければ従来どおり捨てバイト累計が
    /// 閾値（<see cref="_backgroundGcThresholdBytes"/>）に達したときだけバックグラウンド GC を1回発行する
    /// （いずれも <see cref="IsDirty"/> は立てたまま＝アイドル完全 GC の対象としては残す）。
    /// </summary>
    public void NoteDiscarded(long bytes)
    {
        if (bytes <= 0) return;

        _discardedSinceBgc += bytes;
        _discardedSinceBlockingGc += bytes;
        IsDirty = true;

        if (TryBlockingGc()) return;

        if (_discardedSinceBgc >= _backgroundGcThresholdBytes)
        {
            _requestBackgroundGc();
            _discardedSinceBgc = 0;
        }
    }

    /// <summary>
    /// ブロッキング gen2 GC の発火条件を判定し、満たしていれば発行する。閾値無効・再武装ガード未達なら
    /// <see cref="_pendingBytes"/> を呼ばずに false を返す（早期 return の順序で「捨てバイト条件成立時だけ
    /// 実測を取る」を保証する）。
    /// </summary>
    private bool TryBlockingGc()
    {
        if (BlockingGcThresholdBytes <= 0) return false;
        if (_discardedSinceBlockingGc < BlockingGcThresholdBytes / 2) return false;
        if (_pendingBytes().TotalBytes < BlockingGcThresholdBytes) return false;

        _requestBlockingGc();
        _discardedSinceBlockingGc = 0;
        _discardedSinceBgc = 0;
        return true;
    }

    /// <summary>
    /// ホストの周期タイマーから呼ぶ。dirty かつ最終活動から <see cref="_idleDelay"/> 以上経っていれば
    /// 完全 GC を1回発行し dirty を解除する。そうでなければ（dirty のまま操作中でない状態が続いている
    /// だけでも）<see cref="TryBlockingGc"/> を試みる＝バースト直後に捨てが止まり誰も新たな
    /// <see cref="NoteDiscarded"/> を呼ばないまま残債が滞留するのをこの周期で掃く。
    /// </summary>
    public void Tick()
    {
        if (!IsDirty) return;

        if (_clock() - _lastActivity >= _idleDelay)
        {
            _requestFullGc();
            IsDirty = false;
            _discardedSinceBgc = 0;
            _discardedSinceBlockingGc = 0;
            return;
        }

        TryBlockingGc();
    }
}
