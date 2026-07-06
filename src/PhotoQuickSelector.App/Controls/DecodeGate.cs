using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// <see cref="System.Threading.SemaphoreSlim"/> の代替となる、キー付き・優先昇格可能な非同期ゲート。
/// <para>
/// 【背景】<see cref="PreviewBitmapCache"/> は重いデコードの同時実行数を本ゲートで制限している。
/// 表示目的の取得（<c>GetAsync(path, forDisplay: true)</c>）が、既に待機列に並んでいる先読み
/// （Prefetch）用の inflight タスクへ相乗りしても、従来の FIFO キューでは順番が変わらず、
/// 先に並んだ窓内先読みのデコード完了を待たされてしまう。本ゲートは待機列内でキーを指定して
/// 先頭へ割り込ませる <see cref="Promote"/> を持ち、表示要求を優先させられるようにする。
/// </para>
/// <para>
/// 純 C#（WinUI/WinRT 非依存）。<see cref="PreviewBitmapCache"/> と同じく UI スレッド専有
/// （単一スレッド前提・ロックなし）を前提とする。呼び出し元は <c>ConfigureAwait(false)</c> を
/// 使わず全継続が UI スレッドで直列実行されるため、内部状態（<see cref="_inUse"/>／待機列）への
/// 排他制御は行わない。
/// </para>
/// </summary>
internal sealed class DecodeGate
{
    private readonly int _capacity;
    private int _inUse;
    private readonly LinkedList<(string Key, TaskCompletionSource Tcs)> _waiters = new();

    /// <param name="capacity">同時に許可する本数（1 以上にクランプ）。</param>
    public DecodeGate(int capacity)
    {
        _capacity = Math.Max(1, capacity);
    }

    /// <summary>
    /// スロットを要求する。空きがあれば即座に完了した Task を返し、無ければ待機列の末尾へ加わり、
    /// <see cref="Release"/>（または <see cref="Promote"/> 後の <see cref="Release"/>）で順番が回って
    /// くるまで完了しない Task を返す。
    /// </summary>
    /// <param name="key">待機列内でこのタスクを識別するキー（<see cref="Promote"/> で使う）。</param>
    public Task WaitAsync(string key)
    {
        if (_inUse < _capacity)
        {
            _inUse++;
            return Task.CompletedTask;
        }

        // RunContinuationsAsynchronously は必須: 指定しないと Release() の呼び出しスタック内で
        // SetResult() が待機側の継続を同期実行してしまい、Release() の呼び出し元（LoadCoreAsync の
        // finally 節等）の途中で再入的にコードが走る事故につながる。継続は必ず非同期にディスパッチする。
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiters.AddLast((key, tcs));
        return tcs.Task;
    }

    /// <summary>
    /// 待機列にある <paramref name="key"/> のエントリを先頭へ移動する（既に先頭・見つからない
    /// ＝待機列に居ない＝既にゲート取得済みで読込中、または存在しないキーの場合は no-op）。
    /// </summary>
    public void Promote(string key)
    {
        for (var node = _waiters.First; node != null; node = node.Next)
        {
            if (!string.Equals(node.Value.Key, key, StringComparison.OrdinalIgnoreCase)) continue;
            if (node != _waiters.First)
            {
                _waiters.Remove(node);
                _waiters.AddFirst(node);
            }
            return;
        }
        // 見つからなければ no-op（読込中 or 未登録のいずれか。呼び出し側で判別不要）。
    }

    /// <summary>
    /// スロットを 1 つ返却する。待機列があれば先頭のエントリへスロットをそのまま譲渡する
    /// （<see cref="_inUse"/> は変えない）。待機列が空なら空き本数を増やす。
    /// </summary>
    public void Release()
    {
        if (_waiters.Count > 0)
        {
            var first = _waiters.First!.Value;
            _waiters.RemoveFirst();
            first.Tcs.SetResult();
            return;
        }
        _inUse--;
    }
}
