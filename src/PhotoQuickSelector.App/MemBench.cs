#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PhotoQuickSelector_App;

/// <summary>
/// <see cref="MemBenchConfig.Steps"/> の 1 ステップ。<c>kind="sweep"</c>＝写真送り連打、
/// <c>kind="idle"</c>＝無操作待機（メモリ掃除係のアイドル GC 挙動の観測用）。
/// </summary>
public sealed class MemBenchStep
{
    /// <summary>"sweep" または "idle"。大小文字は区別しない。</summary>
    public string Kind { get; set; } = "";

    /// <summary>ステップの総時間（ミリ秒）。</summary>
    public int DurationMs { get; set; }

    /// <summary>sweep 時の写真送り間隔（ミリ秒）。idle では無視する。</summary>
    public int IntervalMs { get; set; } = 120;
}

/// <summary>
/// キャッシュ/メモリ設定の最適値探索のための自動ベンチマークモード（<b>membench</b>）のトリガー設定。
/// <b>Debug ビルド限定</b>。設定フォルダ（<see cref="AppSettings.SettingsFolder"/>＝<c>settings.json</c>
/// と同じフォルダ）に置かれた <c>membench.json</c> を起動時に読み取り、見つかれば通常のセッション復元の
/// 代わりに自動ベンチマークを実行する（<c>tools/membench/Run-MemBench.ps1</c> ハーネス専用の内部機構。
/// 実装経緯・スキーマ詳細は docs/HISTORY.md「メモリベンチマークモード（membench）」節を参照）。
/// </summary>
public sealed class MemBenchConfig
{
    /// <summary>読み込むフォルダの絶対パス。</summary>
    public string FolderPath { get; set; } = "";

    /// <summary>結果を識別するタグ（<c>MemoryLog</c> の bench NOTE 行にも記録される）。</summary>
    public string Tag { get; set; } = "";

    /// <summary>
    /// 開始時に焦点を当てるファイル名（フォルダ相対・拡張子込み。例 "DSC03220.JPG"）。
    /// <see cref="ViewModels.MainViewModel.LoadFolderAsync"/> の <c>restoreSelectedFile</c> へそのまま渡す。
    /// null/空なら従来どおり先頭写真から開始する。特定のファイル範囲（例: 実機で増加傾向が確認された
    /// 連番区間）を毎回同じ起点から辿るための再現性用パラメータ。
    /// </summary>
    public string? StartFile { get; set; }

    /// <summary>
    /// ベンチ完了時に <see cref="MemoryLog.CurrentFilePath"/> のコピー先。null/空ならコピーしない
    /// （呼び出し元でディレクトリを作成してからコピーする）。
    /// </summary>
    public string? OutputPath { get; set; }

    /// <summary>フォルダ読み込み完了から計測開始までの落ち着き待ち（ミリ秒。初期デコードの影響を除くため）。</summary>
    public int SettleMs { get; set; } = 5000;

    /// <summary>順に実行するステップ。</summary>
    public List<MemBenchStep> Steps { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// <paramref name="settingsFolder"/> 直下の <c>membench.json</c> を読み取って返す（**消費式**＝
    /// 読み取り次第すぐ削除するため、通常起動時に前回のトリガーが混入することはない）。ファイルが
    /// 無ければ null。パース失敗時は例外内容を同フォルダの <c>membench.error.txt</c> へ書いて null を
    /// 返す（通常起動を継続させる＝ハーネスの設定ミスでアプリが起動不能にならないようにする）。
    /// </summary>
    public static MemBenchConfig? TryConsume(string settingsFolder)
    {
        var path = Path.Combine(settingsFolder, "membench.json");
        if (!File.Exists(path)) return null;

        try
        {
            var json = File.ReadAllText(path);
            File.Delete(path);
            return JsonSerializer.Deserialize<MemBenchConfig>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            try
            {
                File.WriteAllText(Path.Combine(settingsFolder, "membench.error.txt"), ex.ToString());
            }
            catch
            {
                // ここでも書けない（権限等）なら諦める。通常起動は継続させる。
            }
            return null;
        }
    }
}
#endif
