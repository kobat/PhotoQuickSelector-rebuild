using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoQuickSelector_App.Controls;

/// <summary>先読みキャッシュ項目の状態（デバッグオーバーレイの色分け用）。</summary>
internal enum CacheItemState
{
    /// <summary>デコード済み（_cache 在籍）。</summary>
    Cached,
    /// <summary>ゲート取得済みでファイル読み込み＋デコード中。</summary>
    Loading,
    /// <summary>ゲートの順番待ち（まだ読み込みを開始していない）。</summary>
    Waiting,
}

/// <summary>
/// <see cref="PreviewBitmapCache.Snapshot"/> の 1 項目（デバッグオーバーレイ用の詳細情報）。
/// デコード済み（<see cref="CacheItemState.Cached"/>）以外は <see cref="Bytes"/>/<see cref="Width"/>/
/// <see cref="Height"/>/<see cref="LastUse"/> は 0 のダミー値。
/// XAML には出さない（<see cref="PreviewControl"/> が表示用の <c>CacheEntry</c> へ整形する）。
/// </summary>
internal sealed class CacheSnapshotItem
{
    public CacheSnapshotItem(string path, string name, CacheItemState state, long bytes, int width, int height, long lastUse)
    {
        Path = path;
        Name = name;
        State = state;
        Bytes = bytes;
        Width = width;
        Height = height;
        LastUse = lastUse;
    }

    /// <summary>フルパス（窓分類の辞書引きキー）。</summary>
    public string Path { get; }
    /// <summary>ファイル名（表示用）。</summary>
    public string Name { get; }
    public CacheItemState State { get; }
    /// <summary>デコード済みピクセルのバイト数（未デコードは 0）。</summary>
    public long Bytes { get; }
    /// <summary>画像幅（未デコードは 0）。</summary>
    public int Width { get; }
    /// <summary>画像高さ（未デコードは 0）。</summary>
    public int Height { get; }
    /// <summary>最終利用順（単調増分カウンタ。未デコードは 0）。</summary>
    public long LastUse { get; }
}

/// <summary>
/// デコード済みピクセル（BGRA8・Premultiplied・密詰め＝stride なし）＋寸法。
/// <see cref="WicPixelDecoder.Decode"/> が幅×高さ×4 ちょうどの密詰め配列で返すため、そのまま
/// <see cref="Microsoft.Graphics.Canvas.CanvasBitmap.SetPixelBytes(byte[])"/> /
/// <c>CreateFromBytes</c> に渡せる。
/// </summary>
internal sealed class PixelFrame
{
    public PixelFrame(byte[] bytes, int width, int height)
    {
        Bytes = bytes;
        Width = width;
        Height = height;
    }

    public byte[] Bytes { get; }
    public int Width { get; }
    public int Height { get; }
}

/// <summary>
/// プレビューの前後 N 枚先読みキャッシュ（SPEC §4）。キーはファイルパス。
/// <para>
/// デコード結果は <see cref="PixelFrame"/>（BGRA8 の <c>byte[]</c>・メインメモリ常駐）で保持し、
/// GPU（VRAM）を消費しない。表示時は <see cref="Microsoft.Graphics.Canvas.CanvasBitmap.SetPixelBytes(byte[])"/>
/// （同寸再利用）／<c>CreateFromBytes</c>（作り直し）による GPU への転送のみ＝切替時の CPU コピーは 0 回。
/// デバイス非依存なので Win2D のデバイス再生成（デバイスロスト/DPI 変更）でもキャッシュは生き残る。
/// </para>
/// <para>
/// 同一パスの同時要求は 1 つのタスクに集約する（<see cref="GetAsync"/>）。
/// 全破棄（<see cref="Clear"/>）では世代を進め、進行中の読み込みは完了時に世代不一致で自分を破棄する。
/// </para>
/// <para>
/// 重い「バイト読み込み＋デコード」は <see cref="DecodeGate"/> で同時 <see cref="MaxConcurrentDecodes"/>
/// 本に制限する。スロット解放（grant）のたびに待機列全員を <see cref="DecodePriority"/> で再評価し
/// 最優先のキーへ譲渡する（フォーカス＝index 0 が常に最優先。投入順 FIFO は同値のタイブレーク）。
/// ゲート取得時点で <see cref="IsWanted"/>（現在の保持窓内か）を判定し、外れていればバイトを確保せず
/// 即破棄する＝押しっぱなしで通過した写真はメモリを使わず安価に捨てられる。
/// </para>
/// <para>
/// 破棄は <see cref="Trim"/>（現在窓を保護した LastUse 単独 LRU＋バイト予算 <see cref="MaxCacheBytes"/>）。
/// 破棄した <c>byte[]</c> は <see cref="PixelBufferPool"/> へ返して次のデコードで再利用する
/// （マネージドヒープに 200MB 級のゴミを積まないため。詳細は <see cref="PixelBufferPool"/>）。
/// </para>
/// UI 非依存（デコードは <see cref="WicPixelDecoder"/>＝WIC の COM 直接呼び出しで、Win2D デバイスを
/// 必要としない）なので単体テスト可能。
/// </summary>
internal sealed class PreviewBitmapCache
{
    /// <summary>同時に走らせるデコードの上限（構築時に <see cref="_gate"/> のサイズを決める。変更は再構築が必要）。</summary>
    public int MaxConcurrentDecodes { get; }

    /// <summary>
    /// 合計バイト予算（デコード済みキャッシュ＋バッファプール）。設定の「キャッシュ容量予算」そのもので、
    /// 常駐量はこの値で頭打ちになる。内訳は <see cref="PoolRatioPercent"/> で分割する
    /// （<see cref="SetBudget"/>）。
    /// </summary>
    public long MaxTotalBytes { get; private set; }

    /// <summary>合計予算のうちバッファプールへ割り当てる割合（%）。残りがデコード済みキャッシュの予算。</summary>
    public int PoolRatioPercent { get; private set; }

    /// <summary>デコード済みキャッシュの予算（＝<see cref="MaxTotalBytes"/> − プール予算）。<see cref="Trim"/> が参照。</summary>
    public long MaxCacheBytes { get; private set; }

    /// <summary>バッファプールの予算。</summary>
    public long MaxPoolBytes => _pool.MaxBytes;

    /// <summary>プールが現在抱えている本数／バイト数／ヒット・ミス回数（デバッグオーバーレイ用）。</summary>
    public int PooledBufferCount => _pool.Count;
    public long PooledBytes => _pool.PooledBytes;
    public long PoolHitCount => _pool.HitCount;
    public long PoolMissCount => _pool.MissCount;

    /// <summary>
    /// キャッシュ＋プールから捨てられてゴミ（LOH 行きの回収待ち <c>byte[]</c>）になった累積バイト数。
    /// <see cref="MemoryJanitor"/> のバックグラウンド GC 閾値判定に使う（<see cref="PreviewControl"/> が
    /// <see cref="Trim"/> のたびに差分を渡す）。
    /// </summary>
    public long DiscardedBytes => _pool.DiscardedBytes + _directDiscardedBytes;

    /// <summary>先読みキャッシュ（デコード済み在籍分）の合計バイト数（メモリ時系列ログ用）。</summary>
    public long CachedBytes
    {
        get
        {
            long total = 0;
            foreach (var entry in _cache.Values) total += entry.Frame.Bytes.Length;
            return total;
        }
    }

    /// <summary>先読みキャッシュの在籍件数（メモリ時系列ログ用）。</summary>
    public int CachedCount => _cache.Count;

    /// <summary>読込中/待機中（inflight）の件数（メモリ時系列ログ用）。</summary>
    public int InflightCount => _inflight.Count;

    /// <summary>
    /// マネージドヒープ上で"生きている"ことが確実なピクセルバッファの合計バイト数
    /// （キャッシュ在籍＋プール在籍＋デコード中に貸出中＝<see cref="_inflightPixelBytes"/>）。
    /// <see cref="MemoryJanitor"/> のブロッキング段が「回収待ちゴミの概算＝GetTotalMemory − この値」を
    /// 出すための分母側（<see cref="PreviewControl"/> が生成時に渡す）。
    /// </summary>
    public long ResidentBytes => CachedBytes + PooledBytes + Interlocked.Read(ref _inflightPixelBytes);

    /// <summary>
    /// 合計予算と内訳の割合を設定する。例: 2GB・25% なら キャッシュ 1.5GB／プール 0.5GB。
    /// 割合 0 ならプールを使わない（デコードのたびに新規確保＝プール導入前の挙動）。
    /// </summary>
    /// <param name="totalBytes">合計予算（キャッシュ＋プール）。</param>
    /// <param name="poolRatioPercent">プールへ割り当てる割合（0〜90 にクランプ）。</param>
    public void SetBudget(long totalBytes, int poolRatioPercent)
    {
        MaxTotalBytes = Math.Max(0, totalBytes);
        PoolRatioPercent = Math.Clamp(poolRatioPercent, 0, 90);
        _pool.MaxBytes = MaxTotalBytes * PoolRatioPercent / 100;
        MaxCacheBytes = MaxTotalBytes - _pool.MaxBytes;
    }

    /// <summary>
    /// デコード後ピクセル（BGRA8＝幅×高さ×4 バイト）の 1 枚あたり上限。解凍爆弾対策：JPEG はヘッダ上
    /// 65535×65535（BGRA 約 17GB）まで宣言できるため、確保前にヘッダ宣言寸法で弾く。
    /// 1GB ≈ 268MP は実在カメラの最大級（Phase One 150MP ≈ 605MB）に余裕で収まり、
    /// .NET の <c>byte[]</c> 上限（約 2.1GB。超えると <c>DetachPixelData</c> がどのみち失敗）より十分下。
    /// </summary>
    private const long MaxPixelBytesPerImage = 1L << 30;

    /// <summary>
    /// 読み込む実ファイルサイズの上限（512MB）。<see cref="File.ReadAllBytesAsync(string, System.Threading.CancellationToken)"/>
    /// が全量をメモリへ読むため、非画像の巨大ファイルを誤って掴んだときの防御。
    /// </summary>
    private const long MaxFileBytes = 512L << 20;

    /// <summary>キャッシュ 1 件分の付随情報（LRU の判定材料）。</summary>
    private sealed class CacheEntry
    {
        public CacheEntry(PixelFrame frame, long lastUse)
        {
            Frame = frame;
            LastUse = lastUse;
        }

        public PixelFrame Frame { get; }
        /// <summary>単調増分カウンタ（<see cref="_useCounter"/>）による最終利用順。時計に依存しない。</summary>
        public long LastUse { get; set; }
    }

    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<PixelFrame?>> _inflight = new(StringComparer.OrdinalIgnoreCase);
    // ゲートを取得してファイル読み込み＋デコード中のパス（ゲート順番待ちの待機中と区別する）。
    private readonly HashSet<string> _loading = new(StringComparer.OrdinalIgnoreCase);
    private readonly DecodeGate _gate;
    private readonly PixelBufferPool _pool = new();
    // Trim が破棄したバッファの返却待ち行列（1 ターン遅延させる理由は FlushPendingReturns の注記）。
    private readonly List<byte[]> _pendingReturns = new();
    private int _generation; // Clear（全破棄）でキャッシュを無効化する世代
    private long _useCounter; // LastUse 採番用の単調増分カウンタ（DateTime は使わない）
    // Clear がキャッシュ本体／_pendingReturns をプールを経由せず直接捨てた累積バイト数
    // （DiscardedBytes の内訳。プール経由分は _pool.DiscardedBytes 側に乗る）。
    private long _directDiscardedBytes;
    // プールから Rent 済みでまだキャッシュ/プールに戻っていない（デコード中に貸出中の）バッファの合計。
    // Rent はワーカースレッド（Task.Run 内）で走るため Interlocked で操作する（ResidentBytes 用）。
    private long _inflightPixelBytes;

    /// <param name="maxConcurrentDecodes">同時に走らせるデコード本数（1 以上にクランプ）。</param>
    public PreviewBitmapCache(int maxConcurrentDecodes = 2)
    {
        MaxConcurrentDecodes = Math.Max(1, maxConcurrentDecodes);
        _gate = new DecodeGate(MaxConcurrentDecodes);
        // 実値は PreviewControl が設定から与える（ここは素の既定＝AppSettings と同値）。
        SetBudget((long)(2.5 * (1L << 30)), 20);
    }

    /// <summary>キャッシュ内容（デコード済み / 読込中）が変化したときに発火する（デバッグオーバーレイ用）。</summary>
    public event Action? Changed;

    /// <summary>
    /// そのパスが今も読み込む価値があるか（保持窓内か）を返す述語。
    /// デコードの順番（ゲート取得）が回ってきた時点で評価し、false なら破棄する。
    /// <see cref="PreviewControl"/> が現在の保持窓で設定する。null なら常に読み込む。
    /// </summary>
    public Func<string, bool>? IsWanted { get; set; }

    /// <summary>
    /// <see cref="DecodeGate"/> が grant（スロット解放）のたびに待機列の各キーを評価する優先度
    /// （小さいほど優先・窓外は <see cref="int.MaxValue"/>）。<see cref="PreviewControl"/> が
    /// <c>WindowEntries()</c> の index（フォーカス→選択窓→位置窓の順）で設定する。null なら
    /// <see cref="DecodeGate"/> は純 FIFO で grant する。
    /// </summary>
    public Func<string, int>? DecodePriority { get => _gate.GetPriority; set => _gate.GetPriority = value; }

    /// <summary>
    /// 現在キャッシュ中の画像の詳細一覧（デバッグオーバーレイ用）。デコード済み（<see cref="_cache"/> 在籍）は
    /// 容量・寸法・最終利用順まで含めて返し、inflight（読込中/待機中）は名前・パス・状態のみ埋める。
    /// 並び順の責務は持たない（窓分類は <see cref="PreviewControl"/> 側にしかないため、そちらでソートする）。
    /// 色（UI 型）はここでは決めず、状態 enum までに留める（キャッシュは UI 非依存）。
    /// </summary>
    public IReadOnlyList<CacheSnapshotItem> Snapshot()
    {
        var list = new List<CacheSnapshotItem>(_cache.Count + _inflight.Count);
        foreach (var kv in _cache)
        {
            list.Add(new CacheSnapshotItem(
                path: kv.Key,
                name: Path.GetFileName(kv.Key)!,
                state: CacheItemState.Cached,
                bytes: kv.Value.Frame.Bytes.Length,
                width: kv.Value.Frame.Width,
                height: kv.Value.Frame.Height,
                lastUse: kv.Value.LastUse));
        }
        foreach (var path in _inflight.Keys)
        {
            if (_cache.ContainsKey(path)) continue;
            list.Add(new CacheSnapshotItem(
                path: path,
                name: Path.GetFileName(path)!,
                state: _loading.Contains(path) ? CacheItemState.Loading : CacheItemState.Waiting,
                bytes: 0,
                width: 0,
                height: 0,
                lastUse: 0));
        }
        return list;
    }

    /// <summary>
    /// 指定パスが既にデコード済み（キャッシュ在籍）かどうか。true なら <see cref="GetAsync"/> は
    /// 追加デコードなし（VRAM 生成なし）で即返せる＝throttle せず即表示してよい、の判定に使う。
    /// </summary>
    public bool IsCached(string path) => _cache.ContainsKey(path);

    /// <summary>
    /// 指定パスが読み込み進行中（inflight＝待機中/読込中）かどうか。走行中タスクへの相乗りは
    /// 新規デコードを発生させないため、呼び出し側（<see cref="PreviewControl"/>）のレート制限で
    /// デコード 1 回として課金しない判定に使う。
    /// </summary>
    public bool IsInflight(string path) => _inflight.ContainsKey(path);

    /// <summary>
    /// キャッシュ優先で <see cref="PixelFrame"/> を取得する。読み込み中なら同一タスクを共有（inflight 相乗り）。
    /// ゲートへの割り込みは行わない（順番は grant のたびに <see cref="DecodePriority"/> で再評価される）。
    /// </summary>
    public Task<PixelFrame?> GetAsync(string path)
    {
        if (_cache.TryGetValue(path, out var entry))
        {
            entry.LastUse = ++_useCounter;
            return Task.FromResult<PixelFrame?>(entry.Frame);
        }
        if (_inflight.TryGetValue(path, out var running))
        {
            return running;
        }

        var task = LoadCoreAsync(path, _generation);
        _inflight[path] = task;
        Changed?.Invoke(); // 読込中エントリが増えた
        return task;
    }

    private async Task<PixelFrame?> LoadCoreAsync(string path, int generation)
    {
        try
        {
            // 同時実行ゲート。順番が来るまで待つ（待機中はバイトを確保しないので軽量）。
            await _gate.WaitAsync(path);
            try
            {
                // ゲート取得までに保持窓を外れた / 全破棄（Clear）されたら、読み込まず破棄する。
                if (generation != _generation) { return null; }
                if (IsWanted != null && !IsWanted(path)) { return null; }

                // 待機中 → 読み込み中（ファイル読み込み＋デコード）へ遷移。
                // _inflight の増減を伴わない遷移なので、ここで Changed を発火してオーバーレイを更新する。
                _loading.Add(path);
                Changed?.Invoke();

                // ファイルパスを直接デコーダに渡すと元ファイルがデコード中ロックされ、Reject 移動
                // などの move が「使用中」で失敗しうる。バイトを読み切ってメモリからデコードし、
                // 元ファイルのハンドルは即座に閉じる。
                // 実ファイルが異常に大きい場合は全量読み込みを行わない（null＝表示なしで既存フローに乗る）。
                if (new FileInfo(path).Length > MaxFileBytes) { return null; }

                // メモリ時系列ログ（MemoryLog）の DECODE 行用計測。File.ReadAllBytesAsync 直前から
                // Decode 完了までを測ることで、遅さの原因が I/O かデコードかを後から切り分けられる。
                var decodeSw = Stopwatch.StartNew();
                var bytes = await File.ReadAllBytesAsync(path);

                // デコードは WIC の COM 直接呼び出し（WicPixelDecoder）。WinRT の
                // BitmapDecoder/InMemoryRandomAccessStream はデコードごとに ~20MB のネイティブを
                // ファイナライザ待ちで滞留させ、連続ナビで GB 級に積み上がるため使わない
                // （GC の起動側では抑えられないことを実機で確認済み。経緯は HISTORY.md）。
                // EXIF Orientation 適用（正立化）・sRGB の色管理スキップ・解凍爆弾ガード
                // （宣言寸法で確保前に弾く）は WicPixelDecoder 側で従来同等に行う。
                // 同期 API のためワーカーで実行（同時実行数はゲートで既に絞られている）。
                // ピクセルの書き出し先はプールから借りるので、200MB 級の LOH ゴミも出ない。
                // Rent が返した瞬間から「貸出中」とみなし _inflightPixelBytes へ計上する（ResidentBytes の
                // 一部＝ゴミ量概算の分母側）。Rent はワーカースレッド（Task.Run 内）で走るため Interlocked。
                // rentedBytes はこの呼び出しローカルの変数なので、同時デコード（ゲートで最大 2 本）間で
                // 混線しない。
                long rentedBytes = 0;
                bool poolHit = false;
                PixelFrame? frame;
                try
                {
                    frame = await Task.Run(() =>
                        WicPixelDecoder.Decode(bytes, MaxPixelBytesPerImage, len =>
                        {
                            var buf = _pool.Rent(len, out poolHit);
                            rentedBytes = buf.Length;
                            Interlocked.Add(ref _inflightPixelBytes, rentedBytes);
                            return buf;
                        }));
                    decodeSw.Stop();
                    if (frame == null) { return null; }
                    MemoryLog.Current.DecodeDone(Path.GetFileName(path), frame.Bytes.Length, decodeSw.Elapsed.TotalMilliseconds, poolHit, bytes.Length);

                    // 破棄が決まったバッファはプールへ戻す（まだ誰にも渡していないので即返却でよい）。
                    if (generation != _generation) { _pool.Return(frame.Bytes); return null; }
                    _cache[path] = new CacheEntry(frame, ++_useCounter);
                    return frame;
                }
                finally
                {
                    // 結末（キャッシュ入り／プール返却／frame==null／例外＝Decode 内 COMException で
                    // rentBuffer 済みのまま失敗）のいずれでも貸出を解消する。キャッシュ入りの場合は
                    // 代入直後にここへ来るため CachedBytes と一瞬二重計上になるが、ゴミ量概算を安全側
                    // （過小評価しない）に倒すため許容する。
                    if (rentedBytes > 0) Interlocked.Add(ref _inflightPixelBytes, -rentedBytes);
                }
            }
            finally
            {
                _loading.Remove(path); // 読み込み終了（成功/失敗/破棄いずれも）
                _gate.Release();
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            _inflight.Remove(path);
            Changed?.Invoke(); // 読込完了（成功=cached へ昇格 / 失敗・破棄=消滅）
        }
    }

    /// <summary>指定パス群をキャッシュへ先読みする（fire-and-forget）。</summary>
    public void Prefetch(IEnumerable<string> paths)
    {
        foreach (var path in paths)
            _ = GetAsync(path);
    }

    /// <summary>
    /// <paramref name="keep"/>（呼び出し側が渡す現在の保持窓）は無条件で保護する。それ以外は、
    /// キャッシュ合計バイト数が <see cref="MaxCacheBytes"/> を超えている間だけ LastUse の古い順で破棄する。
    /// 予算内なら窓外でも残す＝2枚往復のような「一度外れてまた戻る」ケースの再デコードを避けられる。
    /// 表示中の 1 枚は呼び出し側（<see cref="PreviewControl"/>）が独立した
    /// <see cref="Microsoft.Graphics.Canvas.CanvasBitmap"/> として GPU へ転送・所有するため、
    /// このキャッシュ（<see cref="PixelFrame"/>）側では別途の保護不要。
    /// </summary>
    public void Trim(IEnumerable<string> keep)
    {
        FlushPendingReturns();

        var keepSet = new HashSet<string>(keep, StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in _cache.Values) total += entry.Frame.Bytes.Length;

        bool removed = false;
        if (total > MaxCacheBytes)
        {
            var candidates = _cache
                .Where(kv => !keepSet.Contains(kv.Key))
                .OrderBy(kv => kv.Value.LastUse)
                .ToList();

            foreach (var kv in candidates)
            {
                if (total <= MaxCacheBytes) break;
                total -= kv.Value.Frame.Bytes.Length;
                _cache.Remove(kv.Key);
                _pendingReturns.Add(kv.Value.Frame.Bytes);
                removed = true;
            }
        }
        if (removed) Changed?.Invoke();
    }

    /// <summary>
    /// 前回の <see cref="Trim"/> が破棄したバッファをプールへ実際に返す。
    /// <para>
    /// 破棄した直後に返さないのは、<see cref="GetAsync"/> が返した <see cref="PixelFrame"/> を
    /// 呼び出し側（<see cref="PreviewControl"/>）が GPU へ転送し終わる前にプールへ戻すと、
    /// 次の <see cref="PixelBufferPool.Rent"/> で中身を上書きされて表示が化けるため。返却を
    /// 「次に <see cref="Trim"/> が呼ばれるまで」＝UI スレッドの 1 ターンぶん遅らせれば、
    /// 同期的な利用は必ず終わっている。
    /// </para>
    /// <para>
    /// なお表示中の 1 枚は常に保持窓（<c>keep</c>）内で <see cref="Trim"/> の保護対象なので、
    /// そもそも破棄候補にならない。この遅延は「追い越されたロードの継続がまだ走っている」等の
    /// 取りこぼしに対する保険である。
    /// </para>
    /// </summary>
    private void FlushPendingReturns()
    {
        if (_pendingReturns.Count == 0) return;
        foreach (var bytes in _pendingReturns) _pool.Return(bytes);
        _pendingReturns.Clear();
    }

    /// <summary>
    /// 全破棄し世代を進める。進行中の読み込みは完了時に世代不一致で自分を破棄する。
    /// キャッシュはデバイス非依存（PixelFrame＝byte[]）のため Win2D のデバイス再生成では呼ぶ必要がない
    /// （現在呼び出し元は無いが、全無効化用 API として維持）。
    /// <para>
    /// 全破棄はメモリを手放すのが目的なので、バッファは**プールへ返さず捨てる**（返すと
    /// キャッシュからプールへ移動するだけで 1 バイトも解放されない）。プールの在庫も同時に空にする。
    /// </para>
    /// </summary>
    public void Clear()
    {
        _generation++;
        foreach (var entry in _cache.Values) _directDiscardedBytes += entry.Frame.Bytes.Length;
        foreach (var bytes in _pendingReturns) _directDiscardedBytes += bytes.Length;
        _cache.Clear();
        _pendingReturns.Clear();
        _pool.Clear();
        Changed?.Invoke();
    }
}
