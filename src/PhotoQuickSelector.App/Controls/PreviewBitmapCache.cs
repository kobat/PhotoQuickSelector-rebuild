using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
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
/// プレビューの前後 N 枚先読みキャッシュ（SPEC §4）。キーはファイルパス。
/// <para>
/// デコード結果は <see cref="SoftwareBitmap"/>（BGRA8・メインメモリ常駐）で保持し、GPU（VRAM）を
/// 消費しない。表示時は <see cref="CanvasBitmap.CreateFromSoftwareBitmap"/> による GPU への転送のみ
/// （デコード不要の生コピー）なので速い。デバイス非依存＝Win2D のデバイス再生成（デバイスロスト/
/// DPI 変更）が起きてもこのキャッシュ自体は生き残る。
/// </para>
/// <para>
/// デコードは重いので、表示中とその近傍だけをメモリに保持する。
/// 同一パスの同時要求は 1 つのタスクに集約する（<see cref="GetAsync"/>）。
/// 全破棄（<see cref="Clear"/>）では世代を進め、進行中の読み込みは完了時に世代不一致で自分を破棄する。
/// </para>
/// <para>
/// 【案2: 同時実行ゲート＋窓外バイパス】左右キー押しっぱなしで通過した写真の読み込み（inflight）が
/// 解放されず増え続ける問題への対策として、重い「バイト読み込み＋デコード」を
/// <see cref="SemaphoreSlim"/> で同時 <see cref="MaxConcurrentDecodes"/> 本に制限する。
/// ゲート取得時点で <see cref="IsWanted"/>（現在の保持窓内か）を判定し、外れていれば
/// バイトを確保せず即破棄する。押しっぱなしで通過した写真はゲートの順番が回る頃には
/// 窓外になっているので、メモリを使わず安価に捨てられる（実デコードは着地写真＋近傍のみ）。
/// </para>
/// UI 非依存（WinRT の imaging API のみに依存し Win2D デバイスを必要としない）なので単体テスト可能。
/// </summary>
internal sealed class PreviewBitmapCache
{
    private const int MaxConcurrentDecodes = 2; // 同時に走らせるデコードの上限

    private readonly Dictionary<string, SoftwareBitmap> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<SoftwareBitmap?>> _inflight = new(StringComparer.OrdinalIgnoreCase);
    // ゲートを取得してファイル読み込み＋デコード中のパス（ゲート順番待ちの待機中と区別する）。
    private readonly HashSet<string> _loading = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(MaxConcurrentDecodes, MaxConcurrentDecodes);
    private int _generation; // Clear（全破棄）でキャッシュを無効化する世代

    /// <summary>キャッシュ内容（デコード済み / 読込中）が変化したときに発火する（デバッグオーバーレイ用）。</summary>
    public event Action? Changed;

    /// <summary>
    /// 【案2】そのパスが今も読み込む価値があるか（保持窓内か）を返す述語。
    /// デコードの順番（ゲート取得）が回ってきた時点で評価し、false なら破棄する。
    /// <see cref="PreviewControl"/> が現在の保持窓で設定する。null なら常に読み込む。
    /// </summary>
    public Func<string, bool>? IsWanted { get; set; }

    /// <summary>
    /// 現在キャッシュ中の画像のファイル名と状態の一覧（デバッグオーバーレイ用）。
    /// 状態でグループ化して返す（cached → loading → waiting）。
    /// <see cref="_inflight"/> は <see cref="Dictionary{TKey,TValue}"/> で列挙順が挿入順を保証せず
    /// （削除によるスロット再利用で崩れる）、loading の上に waiting が混ざって見えるため、
    /// 表示用に状態でソートする（状態管理コレクションには手を付けない）。
    /// 色（UI 型）はここでは決めず、状態 enum までに留める（キャッシュは UI 非依存）。
    /// </summary>
    public IReadOnlyList<(string Name, CacheItemState State)> Snapshot()
    {
        var list = _cache.Keys
            .Select(k => (Path.GetFileName(k)!, CacheItemState.Cached))
            .ToList();
        foreach (var path in _inflight.Keys)
            if (!_cache.ContainsKey(path))
                list.Add((Path.GetFileName(path)!,
                          _loading.Contains(path) ? CacheItemState.Loading : CacheItemState.Waiting));

        // 状態でグループ化して表示（enum 宣言順 = cached → loading → waiting）。
        // OrderBy は安定ソートなので各グループ内の相対順はそのまま。
        return list.OrderBy(x => (int)x.Item2).ToList();
    }

    /// <summary>
    /// 指定パスが既にデコード済み（キャッシュ在籍）かどうか。true なら <see cref="GetAsync"/> は
    /// 追加デコードなし（VRAM 生成なし）で即返せる＝throttle せず即表示してよい、の判定に使う。
    /// </summary>
    public bool IsCached(string path) => _cache.ContainsKey(path);

    /// <summary>キャッシュ優先で <see cref="SoftwareBitmap"/> を取得する。読み込み中なら同一タスクを共有。</summary>
    public Task<SoftwareBitmap?> GetAsync(string path)
    {
        if (_cache.TryGetValue(path, out var cached)) return Task.FromResult<SoftwareBitmap?>(cached);
        if (_inflight.TryGetValue(path, out var running)) return running;

        var task = LoadCoreAsync(path, _generation);
        _inflight[path] = task;
        Changed?.Invoke(); // 読込中エントリが増えた
        return task;
    }

    private async Task<SoftwareBitmap?> LoadCoreAsync(string path, int generation)
    {
        try
        {
            // 同時実行ゲート。順番が来るまで待つ（待機中はバイトを確保しないので軽量）。
            await _gate.WaitAsync();
            try
            {
                // ゲート取得までに保持窓を外れた / 全破棄（Clear）されたら、読み込まず破棄する。
                // 押しっぱなしで通過した写真はここで安価に捨てられる（メモリを使わない）。
                if (generation != _generation) return null;
                if (IsWanted != null && !IsWanted(path)) return null;

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

                var decoder = await BitmapDecoder.CreateAsync(stream);
                var sb = await decoder.GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    new BitmapTransform(),
                    ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.ColorManageToSRgb);
                if (generation != _generation)
                {
                    sb.Dispose(); // Clear で世代が進んだ（全破棄要求後）
                    return null;
                }
                _cache[path] = sb;
                return sb;
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
    /// <paramref name="keep"/> に含まれないキャッシュを破棄する。
    /// 表示中の 1 枚は呼び出し側（<see cref="PreviewControl"/>）が独立した
    /// <see cref="CanvasBitmap"/> として GPU へ転送・所有するため、このキャッシュ（<see cref="SoftwareBitmap"/>）
    /// 側では保護不要（旧 current 保護引数は撤去）。
    /// </summary>
    public void Trim(IEnumerable<string> keep)
    {
        var keepSet = new HashSet<string>(keep, StringComparer.OrdinalIgnoreCase);
        bool removed = false;
        foreach (var key in _cache.Keys.ToList())
        {
            if (keepSet.Contains(key)) continue;
            _cache[key].Dispose();
            _cache.Remove(key);
            removed = true;
        }
        if (removed) Changed?.Invoke();
    }

    /// <summary>
    /// 全破棄し世代を進める。進行中の読み込みは完了時に世代不一致で自分を破棄する。
    /// キャッシュはデバイス非依存（SoftwareBitmap）になったため、Win2D のデバイス再生成では
    /// 呼ぶ必要がない（呼び出し元が無くなったが、全無効化用 API として維持）。
    /// </summary>
    public void Clear()
    {
        _generation++;
        foreach (var bmp in _cache.Values)
            bmp.Dispose();
        _cache.Clear();
        Changed?.Invoke();
    }
}
