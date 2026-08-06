using System;
using System.Collections.Generic;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// デコード済みピクセル用 <c>byte[]</c> の再利用プール（<see cref="PreviewBitmapCache"/> 専用）。
/// <para>
/// 目的はマネージドヒープの肥大抑制。1 枚 200MB 級（α1 8640×5760×4）の配列を毎デコードで新規確保すると、
/// 破棄ぶんが LOH のゴミとして積み上がり、gen2 GC が走るまで解放されない＋GC 後の decommit も分割実施
/// されるため、実際の保持量（予算）の 2 倍以上のワーキングセットになる。確保をプールで回せばゴミが
/// 出ないので、マネージドヒープは予算ちょうどで頭打ちになる。
/// </para>
/// <para>
/// キーはバイト長の完全一致。<see cref="PreviewBitmapCache"/> が持つ <c>byte[]</c> は常に
/// 幅×高さ×4 ちょうど（密詰め BGRA8）で、この長さのまま Win2D の
/// <c>SetPixelBytes(byte[])</c>／<c>CreateFromBytes(byte[])</c> へ渡す契約なので、
/// 切り上げ（サイズクラス丸め）はしない。縦位置と横位置は寸法が入れ替わるだけでバイト長は同じ＝
/// 同一クラスとして再利用できる。
/// </para>
/// <para>
/// 上限（<see cref="MaxBytes"/>）を超えたぶんは古い順（LRU）に捨てる。使われないサイズが居座り続けて
/// 予算を食い潰すことがないので、寸法の揃わないフォルダでも「プールが無駄に抱える量」は上限で頭打ちに
/// なる（最悪ケース＝毎回サイズが違う場合は単に <see cref="Rent"/> が外れ続け、プール導入前と同じ挙動に
/// 縮退する）。
/// </para>
/// <para>
/// <see cref="Rent"/> はデコードワーカー（<see cref="WicPixelDecoder"/> を回す Task.Run スレッド）から、
/// <see cref="Return"/>/<see cref="Clear"/> は UI スレッドから呼ばれるため、内部をロックで保護する。
/// 統計プロパティの読み取り（デバッグオーバーレイ）は表示用途なので非ロックのまま。
/// 純 C#（WinUI/WinRT 非依存）なので単体テスト可能。
/// </para>
/// </summary>
internal sealed class PixelBufferPool
{
    private readonly object _lock = new();
    /// <summary>プール在籍中の 1 本。<see cref="Order"/> は単調増分カウンタによる返却順（時計に依存しない）。</summary>
    private sealed class Slot
    {
        public Slot(byte[] bytes, long order)
        {
            Bytes = bytes;
            Order = order;
        }

        public byte[] Bytes { get; }
        public long Order { get; }
    }

    // 在籍本数は予算 ÷ 1枚あたりのサイズ＝通常 1〜数本なので、線形走査で十分（Dictionary にしない）。
    private readonly List<Slot> _slots = new();
    private long _order;

    /// <summary>プールが抱えてよい合計バイト数。超過分は古い順に捨てる。0 ならプールを使わない。</summary>
    public long MaxBytes { get; set; }

    /// <summary>現在プールが抱えている合計バイト数。</summary>
    public long PooledBytes { get; private set; }

    /// <summary>
    /// プールから捨てられ LOH のゴミになった累積バイト数（単調増加。<see cref="TrimToBudget"/> の
    /// 上限超過分・<see cref="Clear"/> の在庫全量が積み上がる）。<see cref="MemoryJanitor"/> の
    /// バックグラウンド GC 閾値判定に使う。
    /// </summary>
    public long DiscardedBytes { get; private set; }

    /// <summary>現在プールが抱えている本数。</summary>
    public int Count => _slots.Count;

    /// <summary><see cref="Rent"/> が再利用できた回数（デバッグオーバーレイ用）。</summary>
    public long HitCount { get; private set; }

    /// <summary><see cref="Rent"/> が新規確保した回数（デバッグオーバーレイ用）。</summary>
    public long MissCount { get; private set; }

    /// <summary>
    /// 指定バイト長の配列を借りる。同じ長さの在庫があればそれを返し（内容は前の画像のままなので
    /// 呼び出し側が全域を上書きすること）、無ければ新規確保する。
    /// </summary>
    /// <param name="length">必要なバイト長（幅×高さ×4）。</param>
    public byte[] Rent(int length) => Rent(length, out _);

    /// <summary>
    /// <see cref="Rent(int)"/> と同じだが、在庫を再利用できたか（ヒット）を <paramref name="reused"/> で返す。
    /// メモリ時系列ログ（<see cref="MemoryLog"/>）がデコードごとの poolHit/miss を記録するために追加した
    /// オーバーロードで、Hit/MissCount の更新ロジック自体は変更していない。
    /// </summary>
    /// <param name="length">必要なバイト長（幅×高さ×4）。</param>
    /// <param name="reused">在庫を再利用できたら true、新規確保したら false。</param>
    public byte[] Rent(int length, out bool reused)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));

        lock (_lock)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].Bytes.Length != length) continue;

                var bytes = _slots[i].Bytes;
                _slots.RemoveAt(i);
                PooledBytes -= length;
                HitCount++;
                reused = true;
                return bytes;
            }

            MissCount++;
        }

        reused = false;
        // 直後に全域を上書きするのでゼロ初期化は不要（200MB のゼロ埋めを省く）。確保はロック外。
        return GC.AllocateUninitializedArray<byte>(length);
    }

    /// <summary>
    /// 使い終わった配列をプールへ返す。返した時点で最も新しい扱いになり、
    /// <see cref="MaxBytes"/> を超えたぶんは古い順に捨てられる（返したもの自身が捨てられることもある）。
    /// </summary>
    public void Return(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Length == 0) return;

        lock (_lock)
        {
            _slots.Add(new Slot(buffer, ++_order));
            PooledBytes += buffer.Length;
            TrimToBudget();
        }
    }

    /// <summary>在庫を全部捨てる（フォルダ切替・全破棄時）。カウンタは診断のため保持する。</summary>
    public void Clear()
    {
        lock (_lock)
        {
            DiscardedBytes += PooledBytes;
            _slots.Clear();
            PooledBytes = 0;
        }
    }

    /// <summary><see cref="MaxBytes"/> 以内へ収まるまで、返却順の古いものから捨てる。</summary>
    private void TrimToBudget()
    {
        while (PooledBytes > MaxBytes && _slots.Count > 0)
        {
            int oldest = 0;
            for (int i = 1; i < _slots.Count; i++)
                if (_slots[i].Order < _slots[oldest].Order) oldest = i;

            PooledBytes -= _slots[oldest].Bytes.Length;
            DiscardedBytes += _slots[oldest].Bytes.Length;
            _slots.RemoveAt(oldest);
        }
    }
}
