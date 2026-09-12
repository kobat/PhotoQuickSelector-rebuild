# SharpnessBench

`SharpnessAnalyzer`（`src/PhotoQuickSelector.Core/SharpnessAnalyzer.cs`＝Tenengrad）と、比較用に追加した
4手法（`src/PhotoQuickSelector.Core/SharpnessMetrics.cs`）を実写真フォルダにかけて、鮮鋭度スコアと
各処理ステップの所要時間を TSV に記録する開発用の計測ツール。App 本体には組み込まない。

比較用4手法（`SharpnessMetric` enum）：

- `lapv`＝LaplacianVariance（ラプラシアン分散。値が大きいほど鮮鋭）
- `bren`＝Brenner勾配（2px離れた画素との差の二乗和。値が大きいほど鮮鋭）
- `reblur`＝Reblur（Crete-Roffet の no-reference ブラー推定。0..1、値が大きいほど鮮鋭）
- `edgew`＝EdgeWidth（Marziliano 式のエッジ幅推定。px、**値が小さいほど鮮鋭**＝他3手法と大小の向きが逆）

## 実行方法

```powershell
dotnet run --project tools\sharpness\SharpnessBench -c Release -- <folder> [--out <file.tsv>] [--tile 256] [--threshold 32] [--group-seconds 3]
```

- `<folder>`: 対象フォルダ（非再帰）。`.jpg`/`.jpeg` のみ処理（`MetadataReader.IsSupported` 準拠）。
  RAW（`.ARW`/`.ORF` 等）・動画は自動でスキップし、件数をサマリに表示する。
- `--out`: 出力 TSV パス省略時はリポジトリ直下（`CLAUDE.md` のあるフォルダ）基準の
  `tools/sharpness/results/<フォルダ名>-<yyyyMMdd-HHmmss>.tsv`。
- `--tile`: `SharpnessOptions.TileSize`（既定 256）。
- `--threshold`: `SharpnessOptions.Threshold`（既定 32）。この閾値と、比較用に常に `Threshold=0`
  でも解析し直す（`*_t0` 列・`analyze0_ms`）。
- `--group-seconds`: 連写グループの分割秒数（既定 3）。カメラ機種が変わる／直前ファイルとの
  撮影時刻差がこの秒数を超える／撮影時刻が取れないファイルを境界としてグループを区切る。

ビルドのみ: `dotnet build tools\sharpness\SharpnessBench -c Release`

## 実行中の挙動

- 1 ファイルずつ**逐次**処理する（並列化しない＝ファイルごとの計測値を汚さないため）。
- 各行は計算でき次第すぐ `<out>.partial.tsv` へ書き込み・フラッシュする（クラッシュしても
  途中経過が残る）。全件処理後にグループ内相対値（`rel_*`）を確定させ、完全な内容を `<out>` へ
  書き直して `.partial.tsv` を削除する。
- 初回のデコード成功フレームは、計測を始める前に 1 回 `Analyze` を空打ちして JIT ウォームアップする
  （先頭行だけ `analyze_ms` が跳ね上がるのを防ぐ）。比較用4手法も手法ごとに同様（`SharpnessMetric` の
  最初の呼び出し時に1回だけ空打ち）。
- デコード失敗（解凍爆弾ガード・非対応・破損）はスコア列を空のまま行を出力し、処理を続行する。

## TSV 列

| 列 | 内容 |
| --- | --- |
| `file` | ファイル名 |
| `group` | 連写グループ番号（1始まり） |
| `camera` | `CameraModel`（無ければ `CameraMaker`） |
| `width`/`height` | 表示上の寸法（Orientation 適用後、EXIF 由来） |
| `orientation` | EXIF Orientation |
| `iso`/`exposure`/`focal_mm`/`aperture` | 撮影設定 |
| `taken` | 撮影日時（ISO 8601・ローカル時刻。不明は空） |
| `af_x`/`af_y`/`af_w`/`af_h` | `SharpnessAnalyzer.AfWindowFor` が返す AF 窓（表示px・クリップ前）。無ければ空 |
| `global`/`af_window`/`max_tile` | `--threshold` 適用時のスコア（`SharpnessScore` の同名プロパティ） |
| `max_tile_x`/`max_tile_y` | 最鋭タイルの位置 |
| `anisotropy`/`af_anisotropy` | 方向依存性（`--threshold` 適用時） |
| `global_t0`/`af_window_t0`/`max_tile_t0` | 同じタイルサイズ・`Threshold=0` で再解析した場合のスコア |
| `rel_global`/`rel_af`/`rel_maxtile` | 同一グループ内でその値を最大値比の百分率にしたもの（グループ最大が0またはNaNなら空） |
| `read_ms`/`meta_ms`/`decode_ms`/`analyze_ms`/`analyze0_ms` | 各ステップの所要時間（ms） |
| `total_ms` | `read_ms + meta_ms + decode_ms + analyze_ms`（`analyze0_ms` は含めない） |
| `<p>_global`/`<p>_af`/`<p>_maxtile` | 比較用4手法（`<p>`=`lapv`/`bren`/`reblur`/`edgew`）の `--threshold`（`edgew` のみ使用）・`--tile` 適用時のスコア。`SharpnessMetrics.MetricScores` の同名プロパティ |
| `<p>_maxtile_x`/`<p>_maxtile_y` | 各手法の最良タイル位置（`edgew` は「エッジ幅が最小＝最鋭」なタイル。100 エッジ未満のタイルは対象外） |
| `rel_<p>_af`/`rel_<p>_maxtile` | 同一グループ内の相対値（%）。`lapv`/`bren`/`reblur` は「値/グループ最大×100」（Tenengrad の `rel_af`/`rel_maxtile` と同じ向き）、`edgew` のみ値が小さいほど鮮鋭なので「グループ最小/値×100」（どちらも 100=グループ内最鋭。値がNaN、またはグループの最大/最小が0かNaNなら空） |
| `<p>_ms` | 各手法の `SharpnessMetrics.Compute` 所要時間（ms） |

数値は小数点表記（不変カルチャ）。スコア系は 3 桁、時間系は 1 桁。値が無い（NaN・null）場合は空欄。
