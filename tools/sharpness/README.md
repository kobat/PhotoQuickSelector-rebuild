# SharpnessBench

`SharpnessAnalyzer`（`src/PhotoQuickSelector.Core/SharpnessAnalyzer.cs`＝Tenengrad）と、比較用に追加した
4手法（`src/PhotoQuickSelector.Core/SharpnessMetrics.cs`）、および被写体領域解析
`SubjectRegionAnalyzer`（`src/PhotoQuickSelector.Core/SubjectRegionAnalyzer.cs`）を実写真フォルダにかけて、
鮮鋭度スコアと各処理ステップの所要時間を TSV に記録する開発用の計測ツール。App 本体には組み込まない。

`SubjectRegionAnalyzer` は、Tenengrad の「最大タイル」が一方向ブレの被写体では
「ブレ方向に平行な、たまたま残った鋭いエッジ」を拾ってしまう問題に対応するもの。エッジ密度の高い
タイル群（＝被写体領域）を特定し、その中で勾配方向ビンごとにエッジ幅（px）の中央値を測る。
最もエッジ幅が広い方向（=最もボケている方向）と最も狭い方向の比（`subj_w_ratio`）が大きいほど、
方向依存性の強いボケ（＝被写体ブレ）を示唆する。

**フィット表示相当（`fit_*`/`ratio_*` 列）**：ここまでの指標はすべて 1:1 等倍で計算するが、
実際のアプリのフィット表示（ウィンドウに収まるよう長辺を固定 px に縮小する表示）では、
等倍で見えるボケが縮小により目立たなくなることがある。`BgraDownscaler`
（`src/PhotoQuickSelector.Core/BgraDownscaler.cs`）で面積平均（OpenCV の `INTER_AREA` 相当）縮小した
コピーを別途作り、同じ手法群（Tenengrad の global/af/maxtile、被写体領域解析）を縮小後の画像に対して
再計算する。タイルサイズの既定を `--tile`（256）より小さい 64 にしているのは、256 のままだと
長辺 1600px の縮小画像でタイルが 6 個ほどしか取れず（256×6≒1536）粒度が粗すぎるため。
`ratio_global`/`ratio_subj` は等倍値÷フィット値で、値が大きいほど「等倍では見えるボケが、
フィット表示では相対的に目立たなくなる」度合いが強いことを示す。

比較用4手法（`SharpnessMetric` enum）：

- `lapv`＝LaplacianVariance（ラプラシアン分散。値が大きいほど鮮鋭）
- `bren`＝Brenner勾配（2px離れた画素との差の二乗和。値が大きいほど鮮鋭）
- `reblur`＝Reblur（Crete-Roffet の no-reference ブラー推定。0..1、値が大きいほど鮮鋭）
- `edgew`＝EdgeWidth（Marziliano 式のエッジ幅推定。px、**値が小さいほど鮮鋭**＝他3手法と大小の向きが逆）

## 実行方法

```powershell
dotnet run --project tools\sharpness\SharpnessBench -c Release -- <folder> [--out <file.tsv>] [--tile 256] [--threshold 32] [--group-seconds 3] [--fit-long 1600] [--fit-tile 64]
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
- `--fit-long`: フィット表示相当の長辺 px（既定 1600）。`BgraDownscaler.FitSize` に渡す
  （元画像の長辺がこれ以下なら縮小しない＝拡大はしない）。
- `--fit-tile`: フィット画像側の `TileSize`（既定 64）。長辺 1600px 前後の縮小画像では
  `--tile` の既定 256 だとタイルが数個しか取れず粒度が粗すぎるため、独立した既定値にしている。

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
| `subj_tiles` | 被写体タイル数（`SubjectRegionScore.TileCount`）。0 なら以降の `subj_*` 実数値列は空欄（タイル判定は相対基準 `DensityRatio` に加え絶対下限 `MinTileEdgeFraction`＝タイル面積比・既定0.5% があり、画像全体がほぼ平坦なら0になる） |
| `subj_x`/`subj_y`/`subj_w`/`subj_h` | 被写体タイル群の外接矩形（`Bounds`。px・タイル格子基準） |
| `subj_ten`/`subj_edge_density`/`subj_per_edge` | 被写体タイル内の Tenengrad 平均・エッジ密度・エッジ画素のみの平均 mag²（`Tenengrad`/`EdgeDensity`/`PerEdgeMag2`） |
| `subj_aniso`/`subj_dir_deg` | 構造テンソルの異方性比・支配的勾配方向（度。`AnisotropyRatio`/`DominantGradientDegrees`） |
| `subj_w_worst`/`subj_w_best` | 方向ビン別エッジ幅中央値のうち最大/最小（`MinEdgesPerBin` 以上のサンプルを持つビンのみが対象。`WorstWidth`/`BestWidth`） |
| `subj_w_worst_deg` | 最大幅ビンの中心角度（度。`SubjectRegionScore.BinCenterDegrees`） |
| `subj_w_ratio` | `subj_w_worst`/`subj_w_best`（大きいほど方向依存性の強いボケ＝被写体ブレを示唆） |
| `subj_w_rel_extent` | `subj_w_worst` ÷ max(`subj_w`,`subj_h`) × 1000（被写体外接矩形の長辺に対する千分率。矩形サイズに対する相対的なボケ幅） |
| `subj_bins` | 方向ビン（既定8＝22.5°刻み）ごとのエッジ幅中央値を `/` 区切りで並べたもの（`BinMedianWidths`。NaN は `-`） |
| `af_edges`/`af_edge_density` | AF 窓内（クリップ後）のエッジ画素数・エッジ密度（`AfWindowEdgeCount`/`AfWindowEdgeCount÷AfWindowPixelCount`） |
| `rel_subj_w`/`rel_subj_extent` | 同一グループ内の相対値（%）。`edgew` と同じ向き＝グループ最小/値×100（`subj_w_worst`/`subj_w_rel_extent` それぞれに適用。値が小さいほど鮮鋭なため） |
| `subj_ms` | `SubjectRegionAnalyzer.Analyze` の所要時間（ms） |
| `fit_w`/`fit_h` | `BgraDownscaler.FitSize` が返したフィット画像の寸法（px。`--fit-long` 適用後） |
| `fit_global`/`fit_af`/`fit_maxtile` | フィット画像に対する `SharpnessAnalyzer.Analyze`（`--fit-tile`・`--threshold` 適用）のスコア。等倍の `global`/`af_window`/`max_tile` に対応 |
| `fit_maxtile_x`/`fit_maxtile_y` | フィット画像上の最鋭タイル位置（fit px） |
| `fit_aniso` | フィット画像全体の異方性比（等倍の `anisotropy` に対応） |
| `fit_subj_ten` | 等倍側で見つかった被写体領域（`subj_x/y/w/h`）を fit 座標へ縮尺した矩形を「AF窓」として渡し、フィット画像上で測った Tenengrad（被写体タイル探索のやり直しではない）。等倍側に被写体タイルが無ければ（`subj_tiles`=0）空欄 |
| `fit_subj_tiles`/`fit_subj_x`/`fit_subj_y`/`fit_subj_w`/`fit_subj_h` | フィット画像そのものに対する独立の `SubjectRegionAnalyzer.Analyze`（`--fit-tile` 適用）が見つけた被写体タイル数・外接矩形。タイルサイズが等倍側と異なるスケールのため、`subj_x/y/w/h` とは境界が一致するとは限らない |
| `fit_subj_w_worst`/`fit_subj_w_best`/`fit_subj_w_ratio` | 上記フィット側 `SubjectRegionAnalyzer` の `WorstWidth`/`BestWidth`/`WidthRatio`（fit px。等倍の `subj_w_worst` 等に対応） |
| `fit_subj_aniso` | 上記フィット側 `SubjectRegionAnalyzer` の `AnisotropyRatio`（等倍の `subj_aniso` に対応） |
| `ratio_global` | `global ÷ fit_global`。値が大きいほど「等倍では見えるボケが、フィット表示（縮小）では相対的に目立たなくなる」度合いが強い。どちらかが NaN、または `fit_global` が 0 なら空欄 |
| `ratio_subj` | `subj_ten ÷ fit_subj_ten`。同上の被写体領域版 |
| `rel_fit_maxtile`/`rel_fit_subj` | 同一グループ内の相対値（%）。Tenengrad系＝値が大きいほど鮮鋭なので「値/グループ最大×100」（`fit_maxtile`/`fit_subj_ten` にそれぞれ適用） |
| `rel_fit_subj_w` | 同一グループ内の相対値（%）。`edgew`/`rel_subj_w` と同じ向き＝グループ最小/値×100（`fit_subj_w_worst` に適用。値が小さいほど鮮鋭なため） |
| `fit_downscale_ms` | `BgraDownscaler.AreaAverage` の所要時間（ms） |
| `fit_ms` | フィット画像側の解析3手法（`SharpnessAnalyzer.Analyze`×2＋`SubjectRegionAnalyzer.Analyze`×1）の合計所要時間（ms）。ダウンスケール自体（`fit_downscale_ms`）は含まない |

数値は小数点表記（不変カルチャ）。スコア系は 3 桁、時間系は 1 桁。値が無い（NaN・null）場合は空欄。
