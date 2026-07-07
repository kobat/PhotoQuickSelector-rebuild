using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PhotoQuickSelector_App.Controls;

/// <summary>
/// <see cref="System.Threading.SemaphoreSlim"/> の代替となる、キー付き・優先度コールバック対応の非同期ゲート。
/// <para>
/// <see cref="PreviewBitmapCache"/> は重いデコードの同時実行数を本ゲートで制限している。
/// スロットの空き（<see cref="Release"/> による grant）が出るたびに <see cref="GetPriority"/>
/// コールバックで待機列全員の「その時点の」優先度を再評価し、最も必要とされているキー（フォーカス→
/// 選択窓→位置窓という窓分類順）へスロットを譲渡する。投入順（FIFO）は同値のときのタイブレーク
/// としてのみ残る。
/// </para>
/// <para>
/// 純 C#（WinUI/WinRT 非依存）。<see cref="PreviewBitmapCache"/> と同じく UI スレッド専有
/// （単一スレッド前提・ロックなし）を前提とする。呼び出し元は <c>ConfigureAwait(false)</c> を
/// 使わず全継続が UI スレッドで直列実行されるため、内部状態（<see cref="_inUse"/>／待機列）への
/// 排他制御は行わない。
/// </para>
/// <para>
/// ゲート自身はキー文字列の比較を行わない（大文字小文字の同一視など、キーの意味づけは
/// <see cref="GetPriority"/> コールバック側の責務）。コールバックは grant のたびに待機者ごとに
/// 呼び出されるため、優先度は常に最新の状態を反映する＝投入後にフォーカスが移動しても順序が
/// 陳腐化しない。
/// </para>
/// </summary>
internal sealed class DecodeGate
{
    private readonly int _capacity;
    private int _inUse;
    private readonly LinkedList<(string Key, TaskCompletionSource Tcs)> _waiters = new();

    /// <summary>
    /// grant（<see cref="Release"/>）のたびに待機列内の各キーへ呼ばれる優先度コールバック。
    /// 戻り値が小さいほど優先。窓外相当のキーには <see cref="int.MaxValue"/> を返す想定。
    /// null なら純 FIFO で先頭へ譲渡する。<see cref="PreviewBitmapCache"/> が
    /// <c>PreviewControl.WindowEntries()</c> の index を返すコールバックを設定する。
    /// </summary>
    public Func<string, int>? GetPriority { get; set; }

    /// <param name="capacity">同時に許可する本数（1 以上にクランプ）。</param>
    public DecodeGate(int capacity)
    {
        _capacity = Math.Max(1, capacity);
    }

    /// <summary>
    /// スロットを要求する。空きがあれば即座に完了した Task を返し、無ければ待機列の末尾へ加わり、
    /// <see cref="Release"/>（grant 時に <see cref="GetPriority"/> で選ばれるまで）順番が回って
    /// くるまで完了しない Task を返す。
    /// </summary>
    /// <param name="key">待機列内でこのタスクを識別するキー（<see cref="Release"/> 時の
    /// <see cref="GetPriority"/> 評価で使う）。</param>
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
    /// スロットを 1 つ返却する。待機列があれば、<see cref="GetPriority"/> が設定されていれば
    /// 待機列を線形走査してその時点の優先度が厳密に最小のエントリへ、未設定なら先頭
    /// （FIFO）のエントリへスロットをそのまま譲渡する（<see cref="_inUse"/> は変えない）。
    /// 同値の場合は先に並んだ方（走査で最初に見つかった方）が勝つ＝FIFO がタイブレークになる。
    /// 待機列が空なら空き本数を増やす。
    /// </summary>
    public void Release()
    {
        if (_waiters.Count > 0)
        {
            var target = SelectGrantTarget();
            _waiters.Remove(target);
            target.Value.Tcs.SetResult();
            return;
        }
        _inUse--;
    }

    /// <summary>
    /// grant 対象のノードを選ぶ。<see cref="GetPriority"/> が null なら先頭（FIFO）。
    /// 設定されていれば全ノードを走査し、優先度が厳密に最小のノードを返す
    /// （同値なら先に見つかった＝先に並んだ方を優先＝FIFO タイブレーク）。
    /// </summary>
    private LinkedListNode<(string Key, TaskCompletionSource Tcs)> SelectGrantTarget()
    {
        if (GetPriority == null) return _waiters.First!;

        var best = _waiters.First!;
        int bestPriority = GetPriority(best.Value.Key);
        for (var node = best.Next; node != null; node = node.Next)
        {
            int priority = GetPriority(node.Value.Key);
            if (priority < bestPriority)
            {
                best = node;
                bestPriority = priority;
            }
        }
        return best;
    }
}
