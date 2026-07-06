using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

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
/// <see cref="Height"/>/<see cref="WasDisplayed"/>/<see cref="LastUse"/> は 0/false のダミー値。
/// XAML には出さない（<see cref="PreviewControl"/> が表示用の <c>CacheEntry</c> へ整形する）。
/// </summary>
internal sealed class CacheSnapshotItem
{
    public CacheSnapshotItem(string path, string name, CacheItemState state, long bytes, int width, int height, bool wasDisplayed, long lastUse)
    {
        Path = path;
        Name = name;
        State = state;
        Bytes = bytes;
        Width = width;
        Height = height;
        WasDisplayed = wasDisplayed;
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
    /// <summary>一度でも表示目的で取得されたか（未デコードは false）。</summary>
    public bool WasDisplayed { get; }
    /// <summary>最終利用順（単調増分カウンタ。未デコードは 0）。</summary>
    public long LastUse { get; }
}

/// <summary>
/// デコード済みピクセル（BGRA8・Premultiplied・密詰め＝stride なし）＋寸法。
/// <see cref="BitmapDecoder.GetPixelDataAsync"/> の <c>DetachPixelData()</c> は
/// 幅×高さ×4 ちょうどの密詰め配列を返すため、そのまま
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
/// （同寸再利用）／<c>CreateFromBytes</c>（作り直し）による GPU への転送のみ。キャッシュのバイト列を
/// 直接転送 API へ渡すため、切替時の CPU コピーは 0 回。デバイス非依存＝Win2D のデバイス再生成
/// （デバイスロスト/DPI 変更）が起きてもこのキャッシュ自体は生き残る。
/// </para>
/// <para>
/// デコードは重いので、表示中とその近傍だけをメモリに保持する。
/// 同一パスの同時要求は 1 つのタスクに集約する（<see cref="GetAsync"/>）。
/// 全破棄（<see cref="Clear"/>）では世代を進め、進行中の読み込みは完了時に世代不一致で自分を破棄する。
/// </para>
/// <para>
/// 【案2: 同時実行ゲート＋窓外バイパス】左右キー押しっぱなしで通過した写真の読み込み（inflight）が
/// 解放されず増え続ける問題への対策として、重い「バイト読み込み＋デコード」を
/// <see cref="DecodeGate"/> で同時 <see cref="MaxConcurrentDecodes"/> 本に制限する。
/// ゲート取得時点で <see cref="IsWanted"/>（現在の保持窓内か）を判定し、外れていれば
/// バイトを確保せず即破棄する。押しっぱなしで通過した写真はゲートの順番が回る頃には
/// 窓外になっているので、メモリを使わず安価に捨てられる（実デコードは着地写真＋近傍のみ）。
/// </para>
/// <para>
/// 【表示要求の割り込み優先】<see cref="DecodeGate"/> は FIFO の待機列内でキーを指定して先頭へ
/// 割り込ませる <see cref="DecodeGate.Promote"/> を持つ。表示目的の取得（<c>forDisplay: true</c>）が
/// 既に inflight（待機中/読込中）のタスクへ相乗りしたとき・新規に待機列へ並んだときの双方で
/// <see cref="DecodeGate.Promote"/> を呼び、先読み（Prefetch）専用の inflight より優先してゲートの
/// 順番を回す。読込中（既にゲート取得済み）のキーへの Promote は待機列に居ないため no-op になる。
/// </para>
/// <para>
/// 【表示実績優先の2段階 LRU＋バイト予算】先読み（Prefetch）対象＝Trim で残す対象を分離した。
/// 従来は「窓外は即破棄」だったため、2枚を左右キーで往復するたびに窓から外れた側（≈200MB）が
/// 毎回捨てられ再デコードされる無駄があった。<see cref="Trim"/> は呼び出し側が渡す現在窓
/// （<c>keep</c>）を無条件保護しつつ、それ以外は合計バイト数が <see cref="MaxCacheBytes"/> を
/// 超えている間だけ「未表示（先読みのみ）の古い順 → 表示済みの古い順」で破棄する。予算内なら
/// 窓外でも残すため、往復の再デコードが減る。表示目的の取得（<see cref="GetAsync"/> の
/// <c>forDisplay: true</c>）だけが <see cref="CacheEntry.WasDisplayed"/> を立てる（<see cref="Prefetch"/>
/// 経由の先読みだけの取得では立てない）。
/// </para>
/// UI 非依存（WinRT の imaging API のみに依存し Win2D デバイスを必要としない）なので単体テスト可能。
/// </summary>
internal sealed class PreviewBitmapCache
{
    /// <summary>同時に走らせるデコードの上限（構築時に <see cref="_gate"/> のサイズを決める。変更は再構築が必要）。</summary>
    public int MaxConcurrentDecodes { get; }

    /// <summary>キャッシュの合計バイト予算（既定 2GB。<see cref="Trim"/> が参照。実行中に変更可）。</summary>
    public long MaxCacheBytes { get; set; } = 2L << 30;

    /// <summary>キャッシュ 1 件分の付随情報（表示実績優先 LRU の判定材料）。</summary>
    private sealed class CacheEntry
    {
        public CacheEntry(PixelFrame frame, long lastUse, bool wasDisplayed)
        {
            Frame = frame;
            LastUse = lastUse;
            WasDisplayed = wasDisplayed;
        }

        public PixelFrame Frame { get; }
        /// <summary>単調増分カウンタ（<see cref="_useCounter"/>）による最終利用順。時計に依存しない。</summary>
        public long LastUse { get; set; }
        /// <summary>一度でも「表示目的」（<see cref="GetAsync"/> の forDisplay: true）で取得されたか。</summary>
        public bool WasDisplayed { get; set; }
    }

    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<PixelFrame?>> _inflight = new(StringComparer.OrdinalIgnoreCase);
    // ゲートを取得してファイル読み込み＋デコード中のパス（ゲート順番待ちの待機中と区別する）。
    private readonly HashSet<string> _loading = new(StringComparer.OrdinalIgnoreCase);
    // inflight（読込中/待機中）に対する forDisplay 要求を記録する。完了時にキャッシュ挿入する
    // CacheEntry.WasDisplayed へ反映するため（先読み開始後に表示目的の取得が重なるケースに対応）。
    private readonly HashSet<string> _pendingDisplay = new(StringComparer.OrdinalIgnoreCase);
    private readonly DecodeGate _gate;
    private int _generation; // Clear（全破棄）でキャッシュを無効化する世代
    private long _useCounter; // LastUse 採番用の単調増分カウンタ（DateTime は使わない）

    /// <param name="maxConcurrentDecodes">同時に走らせるデコード本数（1 以上にクランプ）。</param>
    public PreviewBitmapCache(int maxConcurrentDecodes = 2)
    {
        MaxConcurrentDecodes = Math.Max(1, maxConcurrentDecodes);
        _gate = new DecodeGate(MaxConcurrentDecodes);
    }

    /// <summary>キャッシュ内容（デコード済み / 読込中）が変化したときに発火する（デバッグオーバーレイ用）。</summary>
    public event Action? Changed;

    /// <summary>
    /// 【案2】そのパスが今も読み込む価値があるか（保持窓内か）を返す述語。
    /// デコードの順番（ゲート取得）が回ってきた時点で評価し、false なら破棄する。
    /// <see cref="PreviewControl"/> が現在の保持窓で設定する。null なら常に読み込む。
    /// </summary>
    public Func<string, bool>? IsWanted { get; set; }

    /// <summary>
    /// 現在キャッシュ中の画像の詳細一覧（デバッグオーバーレイ用）。デコード済み（<see cref="_cache"/> 在籍）は
    /// 容量・寸法・表示実績・最終利用順まで含めて返し、inflight（読込中/待機中）は名前・パス・状態のみ埋める。
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
                wasDisplayed: kv.Value.WasDisplayed,
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
                wasDisplayed: false,
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
    /// キャッシュ優先で <see cref="PixelFrame"/> を取得する。読み込み中なら同一タスクを共有。
    /// <paramref name="forDisplay"/>=true（表示目的の取得。<see cref="PreviewControl"/> の
    /// 実表示ロード）は該当エントリの <see cref="CacheEntry.WasDisplayed"/> を立てる（<see cref="Trim"/>
    /// の 2 段階 LRU で優先的に残す対象になる）。<see cref="Prefetch"/> はこれを false で呼ぶ。
    /// <para>
    /// 表示目的の取得は、既存の inflight（待機中/読込中）への相乗り・新規の待機列追加のいずれでも
    /// <see cref="DecodeGate.Promote"/> でゲートの待機列先頭へ割り込ませる。これにより、表示待ちの
    /// 1 枚が先読み専用の inflight より先にデコードされる。読込中（既にゲート取得済み）のキーへの
    /// Promote は待機列に居ないため no-op（進行中デコードの完了を待つのみ＝従来と同じ）。
    /// </para>
    /// </summary>
    public Task<PixelFrame?> GetAsync(string path, bool forDisplay)
    {
        if (_cache.TryGetValue(path, out var entry))
        {
            entry.LastUse = ++_useCounter;
            if (forDisplay) entry.WasDisplayed = true;
            return Task.FromResult<PixelFrame?>(entry.Frame);
        }
        if (_inflight.TryGetValue(path, out var running))
        {
            if (forDisplay)
            {
                _pendingDisplay.Add(path);
                _gate.Promote(path);
            }
            return running;
        }

        if (forDisplay) _pendingDisplay.Add(path);
        var task = LoadCoreAsync(path, _generation);
        _inflight[path] = task;
        // LoadCoreAsync は最初の await（_gate.WaitAsync）まで同期実行されるため、ここに来た時点で
        // 待機列に並び終わっている（または即取得済み）。並んでいれば Promote が効く。
        if (forDisplay) _gate.Promote(path);
        Changed?.Invoke(); // 読込中エントリが増えた
        return task;
    }

    private async Task<PixelFrame?> LoadCoreAsync(string path, int generation)
    {
        try
        {
            // 同時実行ゲート。順番が来るまで待つ（待機中はバイトを確保しないので軽量）。
            // 表示目的の取得が後から相乗り/新規登録されると Promote で待機列の先頭へ割り込む。
            await _gate.WaitAsync(path);
            try
            {
                // ゲート取得までに保持窓を外れた / 全破棄（Clear）されたら、読み込まず破棄する。
                // 押しっぱなしで通過した写真はここで安価に捨てられる（メモリを使わない）。
                // 破棄する経路では _pendingDisplay に積んだままだとリークするので必ず取り除く。
                if (generation != _generation) { _pendingDisplay.Remove(path); return null; }
                if (IsWanted != null && !IsWanted(path)) { _pendingDisplay.Remove(path); return null; }

                // 待機中 → 読み込み中（ファイル読み込み＋デコード）へ遷移。
                // _inflight の増減を伴わない遷移なので、ここで Changed を発火してオーバーレイを更新する。
                _loading.Add(path);
                Changed?.Invoke();

                // ファイルパスを直接 CanvasBitmap.LoadAsync に渡すと、生成された CanvasBitmap が
                // 生きている間ずっと元ファイルをロックし続ける（Win2D の既知挙動）。すると Reject 移動
                // などの move がキャッシュ中のファイルだけ「使用中」で失敗する。バイトを読み切って
                // メモリストリームからデコードし、元ファイルのハンドルは即座に閉じる。
                // EXIF Orientation は WIC が適用するため、ストリーム経由でも自動回転は維持される
                // （RespectExifOrientation で正立済みピクセルを得る＝従来の CanvasBitmap.LoadAsync の
                // 自動回転と同じ結果）。
                var bytes = await File.ReadAllBytesAsync(path);
                using var stream = new InMemoryRandomAccessStream();
                await stream.WriteAsync(bytes.AsBuffer());
                stream.Seek(0);

                // GetPixelDataAsync + DetachPixelData は「幅×高さ×4」ちょうどの密詰め配列を返す
                // （stride パディングなし）。SetPixelBytes / CreateFromBytes へそのまま渡せる。
                var decoder = await BitmapDecoder.CreateAsync(stream);

                // EXIF ColorSpace（0xA001）が 1（sRGB）の画像は色管理をスキップする。sRGB→sRGB でも WIC の
                // 色管理はデコード全体の約7割（実測 +550〜640ms/枚・50MP）を占める。厳密には恒等変換ではなく
                // 最大 ±3/255 程度の丸め差が出る（実測: 差分バイト 0.5〜1.4%）が、視覚的に知覚不能であり、
                // SoftwareBitmap 化以前の CanvasBitmap.LoadAsync（色管理なし）時代と同じ表示に戻るだけなので許容。
                // Adobe RGB（値 2 / 0xFFFF=Uncalibrated）やタグ無し・照会失敗は従来どおり ColorManageToSRgb（安全側）。
                var colorMode = ColorManagementMode.ColorManageToSRgb;
                try
                {
                    const string exifColorSpaceQuery = "/app1/ifd/exif/{ushort=40961}";
                    var props = await decoder.BitmapProperties.GetPropertiesAsync(new[] { exifColorSpaceQuery });
                    if (props.TryGetValue(exifColorSpaceQuery, out var v) && v.Value is ushort cs && cs == 1)
                        colorMode = ColorManagementMode.DoNotColorManage;
                }
                catch
                {
                    // タグ無し（WINCODEC_ERR_PROPERTYNOTFOUND）等。色管理あり（従来動作）のままにする。
                }

                var pixels = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    new BitmapTransform(),
                    ExifOrientationMode.RespectExifOrientation,
                    colorMode);
                var frame = new PixelFrame(
                    pixels.DetachPixelData(),
                    (int)decoder.OrientedPixelWidth,
                    (int)decoder.OrientedPixelHeight);
                if (generation != _generation) { _pendingDisplay.Remove(path); return null; } // byte[] は GC 管理なので Dispose 不要
                // 挿入時に LastUse を採番し、先読み中に表示目的の取得が重なっていれば WasDisplayed へ反映する。
                bool wasDisplayed = _pendingDisplay.Remove(path);
                _cache[path] = new CacheEntry(frame, ++_useCounter, wasDisplayed);
                return frame;
            }
            finally
            {
                _loading.Remove(path); // 読み込み終了（成功/失敗/破棄いずれも）
                _gate.Release();
            }
        }
        catch
        {
            _pendingDisplay.Remove(path); // 読み込み/デコード失敗でもリークさせない
            return null;
        }
        finally
        {
            _inflight.Remove(path);
            Changed?.Invoke(); // 読込完了（成功=cached へ昇格 / 失敗・破棄=消滅）
        }
    }

    /// <summary>指定パス群をキャッシュへ先読みする（fire-and-forget）。表示目的ではないので WasDisplayed は立てない。</summary>
    public void Prefetch(IEnumerable<string> paths)
    {
        foreach (var path in paths)
            _ = GetAsync(path, forDisplay: false);
    }

    /// <summary>
    /// <paramref name="keep"/>（呼び出し側が渡す現在の保持窓）は無条件で保護する。それ以外は、
    /// キャッシュ合計バイト数が <see cref="MaxCacheBytes"/> を超えている間だけ、
    /// 「未表示（先読みのみ）の古い順 → 表示済みの古い順」（<see cref="CacheEntry.WasDisplayed"/> 昇順→
    /// <see cref="CacheEntry.LastUse"/> 昇順）で破棄する。予算内なら窓外でも残す＝2枚往復のような
    /// 「一度外れてまた戻る」ケースの再デコードを避けられる。表示中の 1 枚は呼び出し側
    /// （<see cref="PreviewControl"/>）が独立した <see cref="Microsoft.Graphics.Canvas.CanvasBitmap"/> として
    /// GPU へ転送・所有するため、このキャッシュ（<see cref="PixelFrame"/>）側では別途の保護不要。
    /// <c>byte[]</c> は GC 管理のため Dispose 不要で Remove するだけでよい。
    /// </summary>
    public void Trim(IEnumerable<string> keep)
    {
        var keepSet = new HashSet<string>(keep, StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in _cache.Values) total += entry.Frame.Bytes.Length;

        bool removed = false;
        if (total > MaxCacheBytes)
        {
            // 予算超過分だけ、保護対象外を「未表示優先→古い順」で破棄する。
            var candidates = _cache
                .Where(kv => !keepSet.Contains(kv.Key))
                .OrderBy(kv => kv.Value.WasDisplayed)   // false（未表示）が先
                .ThenBy(kv => kv.Value.LastUse)          // 古い順
                .ToList();

            foreach (var kv in candidates)
            {
                if (total <= MaxCacheBytes) break;
                total -= kv.Value.Frame.Bytes.Length;
                _cache.Remove(kv.Key);
                removed = true;
            }
        }
        if (removed) Changed?.Invoke();
    }

    /// <summary>
    /// 全破棄し世代を進める。進行中の読み込みは完了時に世代不一致で自分を破棄する。
    /// キャッシュはデバイス非依存（PixelFrame＝byte[]）になったため、Win2D のデバイス再生成では
    /// 呼ぶ必要がない（呼び出し元が無くなったが、全無効化用 API として維持）。byte[] は GC 管理のため
    /// Dispose 不要。
    /// </summary>
    public void Clear()
    {
        _generation++;
        _cache.Clear();
        Changed?.Invoke();
    }
}
