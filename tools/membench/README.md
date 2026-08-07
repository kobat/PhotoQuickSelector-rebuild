# membench — キャッシュ/メモリ設定の自動ベンチマーク

設定（キャッシュ予算/プール比率/緊急GC閾値/ハードリミット）を条件スイープしながら、
アプリを自動操作（写真送り連打⇄アイドル）してメモリ使用量とカクつきを TSV に記録・集計する。
仕組みの詳細と過去の測定結果は [docs/HISTORY.md](../../docs/HISTORY.md) の
「メモリベンチマークモード（membench）」節を参照。

## 前提（PC ごとに1回）

1. 開発環境: Developer Mode 有効・`winapp` CLI・.NET SDK（CLAUDE.md「ビルド/起動」参照）。
2. **一度アプリを手動起動して閉じる**（Debug 設定フォルダと settings.json を作らせ、
   MSIX パッケージを登録させるため。無いと Run-MemBench.ps1 が案内メッセージ付きで停止する）:
   ```powershell
   cd src\PhotoQuickSelector.App
   dotnet run
   ```

## テストデータ

- **合成データ（PC 間で完全に同一・推奨）**: 固定シードなのでどの PC でも同じファイルができる。
  ```powershell
  cd tools\membench
  .\New-MemBenchTestData.ps1 -OutputFolder 'D:\bench\membench_uniq' -Mode unique   # 全60枚が異寸法（最悪ケース）
  .\New-MemBenchTestData.ps1 -OutputFolder 'D:\bench\membench_same' -Mode same     # 全60枚が同寸法（最良ケース）
  ```
- **実データ**: 実写真フォルダをコピーして使う（評価 DB には書き込まない＝矢印キー相当のみ。
  Debug ビルドは `PhotoQuickSelector.Dev.sqlite3` 分離なので既存 DB も汚れない）。
  PC 間で結果を比較するなら同じフォルダをコピーすること。

## 実行

```powershell
cd tools\membench
# conditions.json の全条件（1条件 約3.2分。settings.json は自動バックアップ→終了時復元）
.\Run-MemBench.ps1 -FolderPath 'D:\bench\membench_uniq' -StartFile 'UNIQ0001.JPG'
# 条件ファイル・対象タグを絞る場合
.\Run-MemBench.ps1 -ConditionsPath .\conditions-uniq.json -FolderPath 'D:\bench\membench_uniq' -StartFile 'UNIQ0001.JPG'
.\Run-MemBench.ps1 -Tags baseline,pool50
# 集計（results\*.tsv → results\summary.md）
.\Analyze-MemBench.ps1
```

- `-StartFile` は開始写真のファイル名（実データで特定区間を通したいとき用）。**空文字は
  `powershell -File` 経由だと引数エラーになる**ので、先頭から始めたい場合も先頭ファイル名を明示する。
- `-NavIntervalMs` 既定 500ms＝連打抑制設定からの逆算値（これ未満だと描画スキップが起きて
  実操作と乖離する。docs/HISTORY.md「ナビ間隔の是正」参照）。
- 実行中はアプリのウィンドウが自動で開閉する（キーボード/マウスは奪わない）。
  中断したら `Stop-Process -Name PhotoQuickSelector.App -Force` で残骸を掃除。

## PC 間の結果比較

- `results/` は gitignore（マシン依存データ）。比較するときは `summary.md`／`*.tsv` を
  ホスト名付きにリネームして持ち寄る（例: `summary-desktop.md`）。
- RAM の少ない PC では `HeapHardLimitGB`／`CacheBudgetGB` を下げた条件ファイルを作ること
  （物理 RAM に対して WS ピーク 4GB 超は他プロセスと競合し、測定がスワップに歪められる）。
