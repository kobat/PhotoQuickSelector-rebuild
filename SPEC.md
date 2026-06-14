# PhotoQuickSelector 再構築 仕様書 (SPEC)

写真を高速に閲覧・選別するための Windows デスクトップアプリ。既存実装
（`C:\Users\kobat\Claude\PhotoQuickSelector`）と機能的に同等のものを、
リファクタしつつ新規に作り直す。

---

## 0. 前提・制約（指示の固定事項）

| 論点 | 決定 |
|------|------|
| 技術スタック | **WinUI 3 / .NET 8 / Windows App SDK 1.6 系**。Windows 専用。 |
| EXIF 解析 | **公式 NuGet `MetadataExtractor`（最新版）** を使用。フォークは使わない。Sony AF 点の生タグ読取りはアプリ側に実装。 |
| 再現範囲 | 機能は既存と同等。**既知バグ・デバッグ残骸はリファクタで改善する**（§6）。 |
| 文字コード | ソース・コメントはすべて **UTF-8**。既存の文字化けは持ち込まない。 |
| 言語 | UI 表記は日本語（既存踏襲）。 |

### 主要 NuGet
- `Microsoft.WindowsAppSDK`
- `CommunityToolkit.WinUI.UI` / `.Controls` / `.Controls.DataGrid`（SwitchPresenter, RatingControl, DataGrid）
- `Microsoft.Graphics.Win2D`（CanvasBitmap による高速描画）
- `MetadataExtractor`（EXIF/XMP 解析）
- `System.Data.SQLite.Core`（評価データ永続化）

### 配布・パッケージング
- 配布形態: **(A) 素の自己完結 EXE（unpackaged / MSIX なし）**。
- .NET ランタイム・Windows App SDK ランタイムを**同梱**し、未インストール環境でも起動可能にする。
- アプリ本体プロジェクト（Phase 2 で作成）の `.csproj` に以下を設定:
  ```xml
  <WindowsPackageType>None</WindowsPackageType>            <!-- unpackaged -->
  <SelfContained>true</SelfContained>                       <!-- .NET 同梱 -->
  <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained> <!-- WinAppSDK 同梱 -->
  <PublishSingleFile>true</PublishSingleFile>
  <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <PublishTrimmed>false</PublishTrimmed>                    <!-- WinUI はトリミング不可 -->
  ```
- 発行コマンド:
  ```
  dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true --self-contained
  ```
- 既知の制約（WinUI3 特有）:
  - 完全な単一ファイルにはならない場合がある。ネイティブ依存（WinAppSDK ネイティブ、Win2D、
    `SQLite.Interop.dll`）は exe 内に取り込み**初回起動時に自己展開**。`resources.pri` 等が
    隣に残ることがある → 「**大きな exe 1 個＋数個の付随ファイル**」を前提とする。
  - exe サイズは概ね 150〜250MB、初回起動にひと呼吸。
  - SQLite は `System.Data.SQLite.Core` を維持（`SQLite.Interop.dll` は自己展開対象）。

---

## 1. アプリ概要

ローカルフォルダ内の写真を高速に閲覧し、レーティング・フラグ・カラーラベルを
付けて選別する写真セレクトツール（Lightroom / Photo Mechanic 類似）。
評価データは元ファイルを書き換えず、フォルダごとの SQLite に保存する。
数千枚規模でも快適に動くよう、メタデータ並列読込・サムネイル/ピクセルデータの
先読みキャッシュを行う。

---

## 2. 画面・モード構成

- **2 モード**: `SelectFolder`（フォルダ選択）⇄ `ViewPhoto`（閲覧）
  - 切替は `SwitchPresenter` で行う。
- **ViewPhoto 内の 2 レイアウト**:
  - `Thumbnail`: タイル状グリッド
  - `Preview`: 横スクロールのフィルムストリップ ＋ 大画面プレビュー（左メイン＋右ナビゲーター）
  - ダブルクリックで Thumbnail ⇄ Preview を相互遷移。

---

## 3. 機能仕様

### 3-1. フォルダ読み込み
- フォルダピッカー（起点 = ピクチャライブラリ）。
- 対象拡張子: **`.jpg` / `.jpeg`**（将来 RAW/HEIC 拡張を見据え、拡張子集合を一箇所に集約）。
- 列挙は `Directory.GetFiles`（WinRT の StorageFile 列挙は遅いため回避）。
- メタデータを**並列読込（既定 50 ファイル/タスク）**、進捗をテキスト表示。
- ソート: **撮影日時 昇順 → 同時刻はファイル名 昇順**。日時不明は末尾。

### 3-2. メタデータ抽出（EXIF / XMP）
- 画像基本: 幅・高さ、Orientation（回転を考慮した表示 W/H）、
  撮影日時（**サブ秒・タイムゾーン対応**、無ければ "Unknown"）。
- カメラ: メーカー / 機種 / レンズ（LensModel → 無ければ LensSpecification）。
- 撮影設定: 焦点距離（**35mm 換算込み。Olympus 機は FocalPlaneDiagonal から逆算**）、
  絞り、シャッター速度、ISO、露出補正。
- AF: **フォーカス点・枠サイズ（Sony メーカーノート 生タグ `0x2027` / `0x2037`）**。
- GPS: 緯度経度（DMS 文字列）。
- レーティング: **XMP `xmp:Rating`**。

### 3-3. 評価データと永続化
- 評価項目:
  - `rating`（0–5）
  - `flag`（拒否 -1 / 中立 0 / 採用 +1）
  - カラーラベル **6 色**: 赤・橙(黄)・緑・青・紫
- 保存先: **対象フォルダ直下の `PhotoQuickSelector.sqlite3`**。
- テーブル `image_file_metadata`（PK = `file_name`）。
- 値変更時に即時 UPDATE（トランザクション）。
- **スキーマバージョン管理（マイグレーション機構）**を持つ（`schema_info` テーブル）。
- 表示用 rating の優先順位: **永続化値があればそれを、無ければ EXIF/XMP 値**。

#### スキーマ（v1）
```sql
CREATE TABLE schema_info (key TEXT PRIMARY KEY, value TEXT);

CREATE TABLE image_file_metadata (
    file_name           TEXT PRIMARY KEY,
    rating              INTEGER,
    flag_rating         INTEGER,
    color_label_red     INTEGER,
    color_label_yellow  INTEGER,
    color_label_green   INTEGER,
    color_label_blue    INTEGER,
    color_label_purple  INTEGER,
    invalid_flag        INTEGER NOT NULL DEFAULT 0
);
```

### 3-4. フィルタ
- 有効/無効トグル。
- 条件: `rating`（**≧ / ＝ 切替** ＋ `NoRating`）、フラグ（採用/中立/拒否）、カラーラベル各色。
- 件数表示 `絞込件数/全件数`。
- フィルタ後もフォーカス中の写真位置を可能な限り維持。

### 3-5. クリップボード出力
- 通常: 絞込結果のファイル名一覧をコピー。
- **Ctrl 押下時**: 採用写真を移動する **`.bat` スクリプトを生成**。
  - フォルダ名・件数・フィルタ条件を `@rem` コメントで埋め込む。
  - 本体は `set FROMDIR=..` / `set TODIR=.` ＋ 各ファイルの
    `move %FROMDIR%\<名前(拡張子なし)>* %TODIR%`（RAW+JPEG をまとめて移動する意図）。

### 3-6. プレビュー表示（描画の中核）
- 縮小表示 / ズーム表示の切替、ドラッグでパン、ズーム倍率変更（フィット / 100% / DPI 考慮）。
- **AF フォーカス点へスクロール**。
- **グリッド線オーバーレイ**（三分割）トグル。
- **右側ナビゲーター**: 全体縮小画像 ＋ 現在表示領域の矩形 ＋ AF フォーカス枠。
- 描画は Win2D `CanvasBitmap` ＋ `SetPixelBytes`。表示 W/H 計算用のカスタムコントロール
  （ResizeImage / OriginalSizeImage 相当：`DrawOffsetX/Y`, `DrawWidth/Height` を公開）。

### 3-7. キーボードショートカット（Preview 時）

| キー | 動作 |
|------|------|
| ← / → | 前後移動（複数選択時は選択内を巡回） |
| 0–5 | レーティング 0–5 |
| 6 / 7 / 8 / 9 | カラーラベル 赤 / 橙 / 緑 / 青 をトグル |
| **改善:** 紫ラベル | 既存は未割当。**新規でキーを割り当てる**（候補: `P` もしくは `0` 列の拡張） |
| `[` / `]` | レーティング −1 / +1 |
| Ctrl+↑ / Ctrl+↓ | フラグを採用方向 / 拒否方向へ |
| Esc | 選択をフォーカス位置にリセット |
| Z | ズーム ⇄ 縮小、**Shift+Z: 等倍（新規で実装する）** |
| Alt+矢印 | ズーム画像をスクロール |
| Ctrl+Alt+矢印 | 右プレビューをスクロール |
| Shift+Alt+← / → | フィット / 100% |
| Alt+F / Ctrl+Alt+F | フォーカス点へスクロール（左 / 右） |
| G | グリッド線トグル |
| Ctrl+L | フィルタ ON/OFF |
| Ctrl+E / Alt+E / Ctrl+Alt+E | エクスプローラで表示 / 既定アプリで開く / パスをコピー |
| Alt+S | 共有（Nearby Share 等。パスは**設定化**する） |
| M | （デバッグ用 GC。リリースでは無効化または削除） |

### 3-8. 外部連携
- エクスプローラ `/select`、既定アプリ起動、共有送信、クリップボード。
- **共有先のパスはハードコードせず設定可能にする**（既存は Google Nearby Share を固定パス参照）。

---

## 4. 非機能要件
- 数千枚規模で UI が固まらない（メタデータ並列読込・専用スレッドキュー）。
- サムネイル / フルピクセルデータの**前後 N 枚先読みキャッシュ ＋ 範囲外破棄**
  （forward/backward それぞれ create / hold 件数をパラメータ化）。
- リサイクル描画・メモリ肥大対策。
- 評価操作は即時に SQLite へ反映され、再起動後も保持される。

---

## 5. アーキテクチャ / プロジェクト構成

```
PhotoQuickSelector.sln
├── src/
│   ├── PhotoQuickSelector.Core/        … UI 非依存のコア（メタデータ・永続化・キャッシュ方針）
│   │     ├── ImageMetadata             … 1 枚分の不変メタデータ（EXIF/XMP 抽出結果）
│   │     ├── MetadataReader            … MetadataExtractor を用いた抽出
│   │     ├── ImageRating               … rating/flag/colorlabel のドメインモデル
│   │     └── MetadataStore             … SQLite 永続化（スキーマ移行込み）
│   └── PhotoQuickSelector.App/         … WinUI 3 アプリ（画面・描画・入力）
└── tests/
      └── PhotoQuickSelector.Core.Tests … xUnit。抽出ロジックと永続化を検証
```

- MVVM 寄りに整理（既存の `AppState` 相当を ViewModel 化、INotifyPropertyChanged は
  `CommunityToolkit.Mvvm` の `ObservableObject` / `[ObservableProperty]` を検討）。
- `[Windows.UI.Xaml.Data.Bindable]`（UWP 旧名前空間）は使わず、必要なら
  `[Microsoft.UI.Xaml.Data.Bindable]` を用いる。

---

## 6. 既存からの改善点（本再構築で必ず直す）

1. **紫カラーラベルにキーを割り当てる**（既存は 6–9 で赤橙緑青のみ）。
2. **デバッグ用ハードコードパスを完全排除**（`O:\…`, `P:\…` 等）。
3. **共有先パスのハードコード廃止** → 設定化。
4. **UWP 旧名前空間 `Windows.UI.Xaml.*` を排除**。
5. **Shift+Z の等倍表示を実装**（既存はコメントアウト）。
6. **文字化けコメントの解消**（UTF-8 統一）、命名ミス修正（`GraterEqual`→`GreaterEqual` 等）。
7. **死蔵コード（大量のコメントアウト）を持ち込まない**。
8. 先読みスレッド内の `.Result` / `.Wait()` 同期待ちを、可能な範囲で `async` 化または整理。
9. EXIF 解析を**公式 NuGet 化**（フォーク依存を解消）。

---

## 7. 実装の進め方（段階分割）

各ステップでビルド＆動作確認を挟む。

1. **Phase 1（本着手範囲）**: `PhotoQuickSelector.Core` ＝ メタデータ抽出 ＋ SQLite 永続化。
   UI なしで単体テストまで通す。
2. **Phase 2**: WinUI アプリ枠 ＋ フォルダ選択 ＋ Thumbnail レイアウト ＋ 評価付与 ＋ キー操作。
3. **Phase 3**: Preview レイアウト（ズーム/パン/ナビゲーター/フォーカス表示、Win2D 描画）。
4. **Phase 4**: フィルタ ＋ クリップボード出力 ＋ 外部連携 ＋ 設定。

### 受け入れ基準（例）
- Sony 機画像で AF 枠が表示される。
- XMP レーティング付き画像の初期 rating が反映される。
- 評価を付けて再起動しても保持される（SQLite）。
- 3000 枚規模で初回一覧表示が実用的な時間で完了する。
