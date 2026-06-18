using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;

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

    public PreviewBitmapCache(ICanvasResourceCreator device) => _device = device;

    /// <summary>キャッシュ優先で <see cref="CanvasBitmap"/> を取得する。読み込み中なら同一タスクを共有。</summary>
    public Task<CanvasBitmap?> GetAsync(string path)
    {
        if (_cache.TryGetValue(path, out var cached)) return Task.FromResult<CanvasBitmap?>(cached);
        if (_inflight.TryGetValue(path, out var running)) return running;

        var task = LoadCoreAsync(path, _generation);
        _inflight[path] = task;
        return task;
    }

    private async Task<CanvasBitmap?> LoadCoreAsync(string path, int generation)
    {
        try
        {
            var bmp = await CanvasBitmap.LoadAsync(_device, path);
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
        foreach (var key in _cache.Keys.ToList())
        {
            if (keepSet.Contains(key)) continue;
            if (ReferenceEquals(_cache[key], current)) continue;
            _cache[key].Dispose();
            _cache.Remove(key);
        }
    }

    /// <summary>全破棄し世代を進める。進行中の読み込みは完了時に世代不一致で自分を破棄する。</summary>
    public void Clear()
    {
        _generation++;
        foreach (var bmp in _cache.Values)
            bmp.Dispose();
        _cache.Clear();
    }
}
