using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;
using Windows.Storage.Streams;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// プレビューの前後 N 枚先読みキャッシュ（SPEC §4）。キーはファイルパス。
/// <para>
/// <see cref="CanvasBitmap"/> のデコードは重いので、表示中とその近傍だけをメモリに保持する。
/// 同一パスの同時要求は 1 つのタスクに集約する（<see cref="GetAsync"/>）。
/// デバイス再生成（CreateResources の NewDevice/DpiChanged）では <see cref="Clear"/> で
/// 世代を進め、進行中の読み込みは完了時に世代不一致で自分を破棄する。
/// </para>
/// UI 非依存（<see cref="ICanvasResourceCreator"/> のみに依存）なので単体テスト可能。
/// </summary>
internal sealed class PreviewBitmapCache
{
    private readonly ICanvasResourceCreator _device;
    private readonly Dictionary<string, CanvasBitmap> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<CanvasBitmap?>> _inflight = new(StringComparer.OrdinalIgnoreCase);
    private int _generation; // デバイス再生成でキャッシュを無効化する世代

    /// <summary>キャッシュ内容（デコード済み / 読込中）が変化したときに発火する（デバッグオーバーレイ用）。</summary>
    public event Action? Changed;

    public PreviewBitmapCache(ICanvasResourceCreator device) => _device = device;

    /// <summary>
    /// 現在キャッシュ中の画像のファイル名一覧（デバッグオーバーレイ用）。
    /// デコード済みに続けて、読込中（inflight）のものを <c>(loading)</c> 付きで列挙する。
    /// </summary>
    public IReadOnlyList<string> SnapshotFileNames()
    {
        var list = _cache.Keys.Select(Path.GetFileName).ToList();
        foreach (var path in _inflight.Keys)
            if (!_cache.ContainsKey(path))
                list.Add(Path.GetFileName(path) + " (loading)");
        return list!;
    }

    /// <summary>キャッシュ優先で <see cref="CanvasBitmap"/> を取得する。読み込み中なら同一タスクを共有。</summary>
    public Task<CanvasBitmap?> GetAsync(string path)
    {
        if (_cache.TryGetValue(path, out var cached)) return Task.FromResult<CanvasBitmap?>(cached);
        if (_inflight.TryGetValue(path, out var running)) return running;

        var task = LoadCoreAsync(path, _generation);
        _inflight[path] = task;
        Changed?.Invoke(); // 読込中エントリが増えた
        return task;
    }

    private async Task<CanvasBitmap?> LoadCoreAsync(string path, int generation)
    {
        try
        {
            // ファイルパスを直接 CanvasBitmap.LoadAsync に渡すと、生成された CanvasBitmap が
            // 生きている間ずっと元ファイルをロックし続ける（Win2D の既知挙動）。すると Reject 移動
            // などの move がキャッシュ中のファイルだけ「使用中」で失敗する。バイトを読み切って
            // メモリストリームからデコードし、元ファイルのハンドルは即座に閉じる。
            // EXIF Orientation は WIC が適用するため、ストリーム経由でも自動回転は維持される。
            var bytes = await File.ReadAllBytesAsync(path);
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);

            var bmp = await CanvasBitmap.LoadAsync(_device, stream);
            if (generation != _generation)
            {
                bmp.Dispose(); // デバイス再生成でキャッシュが無効化された
                return null;
            }
            _cache[path] = bmp;
            return bmp;
        }
        catch
        {
            return null;
        }
        finally
        {
            _inflight.Remove(path);
            Changed?.Invoke(); // 読込完了（成功=cached へ昇格 / 失敗=消滅）
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
    /// 表示中の <paramref name="current"/> は keep 外でも残す（描画中の解放を防ぐ）。
    /// </summary>
    public void Trim(IEnumerable<string> keep, CanvasBitmap? current)
    {
        var keepSet = new HashSet<string>(keep, StringComparer.OrdinalIgnoreCase);
        bool removed = false;
        foreach (var key in _cache.Keys.ToList())
        {
            if (keepSet.Contains(key)) continue;
            if (ReferenceEquals(_cache[key], current)) continue;
            _cache[key].Dispose();
            _cache.Remove(key);
            removed = true;
        }
        if (removed) Changed?.Invoke();
    }

    /// <summary>全破棄し世代を進める。進行中の読み込みは完了時に世代不一致で自分を破棄する。</summary>
    public void Clear()
    {
        _generation++;
        foreach (var bmp in _cache.Values)
            bmp.Dispose();
        _cache.Clear();
        Changed?.Invoke();
    }
}
