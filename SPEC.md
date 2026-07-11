# PhotoQuickSelector 再構築 仕様書 (SPEC)

写真を高速に閲覧・選別するための Windows デスクトップアプリ。既存実装
（`C:\Users\kobat\Claude\PhotoQuickSelector`）と機能的に同等のものを、
リファクタしつつ新規に作り直す。

---

## 0. 前提・制約（指示の固定事項）

| 論点 | 決定 |
|------|------|
| 技術スタック | **WinUI 3 / .NET（App は `net10.0-windows`、Core は `net8.0`）/ Windows App SDK 2.2 系**。Windows 専用。 |
| EXIF 解析 | **公式 NuGet `MetadataExtractor`（最新版）** を使用。フォークは使わない。Sony AF 点の生タグ読取りはアプリ側に実装。 |
| 再現範囲 | 機能は既存と同等。**既知バグ・デバッグ残骸はリファクタで改善する**（§6）。 |
| 文字コード | ソース・コメントはすべて **UTF-8**。既存の文字化けは持ち込まない。 |
| 言語 | UI 表記は日本語（既存踏襲）。 |

### 主要 NuGet
- `Microsoft.WindowsAppSDK`
- `CommunityToolkit.Mvvm`（`ObservableObject` / `[ObservableProperty]`）
- `CommunityToolkit.WinUI.Controls.Sizers`（GridSplitter）
- `Microsoft.Graphics.Win2D`（CanvasBitmap による高速描画）
- `MetadataExtractor`（EXIF/XMP 解析）
- `System.Data.SQLite.Core`（評価データ永続化）

### 配布・パッケージング
- 配布形態: **(A) 素の自己完結 EXE（unpackaged / MSIX なし）**。
- .NET ランタイム・Windows App SDK ランタイムを**同梱**し、未インストール環境でも起動可能にする。
- 開発は packaged（MSIX 開発）構成のまま行い、unpackaged 化の設定は **publish プロファイル
  （`.pubxml`）側にのみ**置く（csproj のグローバル設定にすると `dotnet run` / 開発時起動が壊れるため。
  csproj には全構成で安全な `PublishTrimmed=false` のみ）:
  - `Properties/PublishProfiles/win-x64.pubxml` … フォルダ配布（`PublishSingleFile=false`）
  - `Properties/PublishProfiles/win-x64-singlefile.pubxml` … 単一ファイル EXE
  - 共通設定: `WindowsPackageType=None` / `SelfContained=true` / `WindowsAppSDKSelfContained=true` /
    `PublishReadyToRun=true` / `PublishTrimmed=false`（WinUI/Win2D はトリミング不可）
- 発行コマンド:
  ```
  dotnet publish -c Release -p:Platform=x64 -p:PublishProfile=win-x64[-singlefile]
  ```
- 実発行の結果（win-x64・実測）:
  - 単一ファイル版: ルート直下 exe 1 個（約 290MB）＋ pdb（`resources.pri` も exe 内に埋め込み）。
  - フォルダ版: 約 525 ファイル / 273MB。
  - SQLite は `System.Data.SQLite.Core` を維持（`SQLite.Interop.dll` は自己展開対象）。

---

## 1. アプリ概要

ローカルフォルダ内の写真を高速に閲覧し、レーティング・フラグ・カラーラベルを
付けて選別する写真セレクトツール（Lightroom / Photo Mechanic 類似）。
評価データは元ファイルを書き換えず、フォルダごとの SQLite に保存する。
数千枚規模でも快適に動くよう、メタデータ並列読込・サムネイル/ピクセルデータの
先読みキャッシュを行う。

---

## 2. 画面構成（★旧プロジェクトからの変更点）

旧プロジェクトの「フォルダ選択モード ⇄ 閲覧モード」の 2 モード切替（`SwitchPresenter`）は**廃止**し、
**単一ウィンドウを左右に分割**した常時表示レイアウトにする。

- **左ペイン: フォルダ選択**
  - **フォルダツリー**（ドライブ→フォルダの階層をクリックでたどる、エクスプローラ風）。
  - 上部に **最近開いたフォルダ** と **お気に入り** を表示（フォルダノードを右クリック等で
    お気に入り登録／解除）。最近・お気に入りはアプリ設定として永続化（後述 §5）。
  - **読み込みは明示操作**：ツリーでフォルダを選んでも自動ロードはしない。
    左ペインの「**読み込み**」ボタン（またはお気に入り／最近開いたフォルダのクリック）で右ペインに反映する。
    フォルダノードの**ダブルクリック**は下位フォルダの展開／折りたたみ（読み込みはしない）。
- **右ペイン: 閲覧**（旧 ViewPhoto 相当。内部に 2 レイアウト）
  - `Thumbnail`: タイル状グリッド
  - `Preview`: 横スクロールのフィルムストリップ ＋ 大画面プレビュー（メイン＋ナビゲーター）
  - サムネイルのダブルクリックで Preview へ、**フィルムストリップのダブルクリック**で Thumbnail へ戻る
    （メイン大画面のダブルクリックは 100% 表示に割当てているため終了導線ではない）。
- **左右の境界**
  - `GridSplitter` で**幅をリサイズ可能**。さらに**左ペインを折りたたみ/展開**できる
    トグルボタンを用意し、閲覧領域を最大化できる。
- **キー入力の集約**
  - 旧実装はモードでキー入力を振り分けていたが、新実装は Window ルートの `PreviewKeyDown`
    （tunneling）で集約し、§3-7 のレーティング等ショートカットは**フォーカス位置に依存せず**効く
    （ダイアログ／フライアウト表示中は集約を停止し、入力をそちらへ委ねる）。

---

## 3. 機能仕様

### 3-1. フォルダ読み込み
- 左ペインのフォルダツリーで選択し、**明示操作（「読み込み」ボタン／お気に入り・最近のクリック）でロード**
  （§2 参照。旧来のモーダルなフォルダピッカーは使わない）。
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
  - カラーラベル **5 色**: 赤・黄・緑・青・紫（DB カラムも `color_label_yellow`＝黄）
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

### 3-5. ファイル操作（フィルタフライアウト下部。対象＝絞込結果）
- **ファイル名をコピー**（DropDown）: 「ファイル名のみ／フルパス」×「表示中のみ／関連ファイルも（RAW 等）」の
  4 択で 1 行 1 件のテキストをコピー（右クリックメニューの同名サブメニューと実体共有）。
- **ファイルをコピー**（DropDown）: 実体コピー（表示中のみ／関連ファイルも）＋リネームしてコピー
  （右クリックメニューと実体共有）。
- **ファイルを移動…**: 移動先フォルダを入力（セッション中は前回値を記憶）し、**`.bat` を生成して確認後に実行**
  （旧「移動 bat を生成してコピー」の後継。旧実装の「Ctrl 押下で切替」の隠し挙動も廃止済み）。
  - 生成日時・移動元/先・件数・フィルタ条件を `@rem` コメントで埋め込む。
  - 本体は `set FROMDIR=<移動元絶対パス>` / `set TODIR=.` ＋ 各ファイルの
    `move "%FROMDIR%\<名前(拡張子なし)>*" "%TODIR%"`（RAW+JPEG をまとめて移動する意図）。
  - 移動先の同名衝突チェック → bat 内容の確認ダイアログ → 移動先へ bat/ログ保存＋実行 → 一覧再読込。

### 3-6. プレビュー表示（描画の中核）
- 縮小表示 / ズーム表示の切替、ドラッグでパン、ズーム倍率変更（フィット / 100% / DPI 考慮）。
- **AF フォーカス点へスクロール**。
- **構図グリッド線オーバーレイ**: 種類（中央十字／三分割／正方形）×基準（画像／Canvas）を切替。
- **右側ナビゲーター**: 全体縮小画像 ＋ 現在表示領域の矩形 ＋ AF フォーカス枠。
- 描画は Win2D `CanvasBitmap`（ストリーム経由 `LoadAsync`＝元ファイルをロックしない）。
  表示ジオメトリ（ズーム/パン/フィット、`OffsetX/Y`・`DrawWidth/Height`）は UI 非依存の
  `PreviewViewport` が担う（旧 ResizeImage / OriginalSizeImage 相当）。

### 3-7. キーボードショートカット（Preview 時）

| キー | 動作 |
|------|------|
| ← / → | 前後移動（複数選択時は選択内を巡回）。**移動後フォーカスをフィルムストリップへ移し PageUp/PageDown/Home/End を有効化** |
| PageUp / PageDown / Home / End | フィルムストリップ上の ListView ナビ（← / → 移動でフォーカスが移った後に有効） |
| 0–5 | レーティング 0–5 |
| 6 / 7 / 8 / 9 | カラーラベル 赤 / 黄 / 緑 / 青 をトグル |
| P | カラーラベル 紫 をトグル（**改善:** 既存は未割当 → `P` に割当済み） |
| `[` / `]` | レーティング −1 / +1 |
| Ctrl+↑ / Ctrl+↓ | フラグを採用方向 / 拒否方向へ |
| Esc | **プレビューでは無反応（プレビューを抜けない）。終了はダブルクリック**。全画面中のみ通常表示へ復帰 |
| Z | フィット ⇄ ズームトグル（ズーム側は**直近のズーム位置=倍率/中心を復元**、初回は等倍）、**Shift+Z: 等倍** |
| Alt+矢印 | ズーム画像をスクロール |
| Ctrl+Alt+矢印 | 右プレビューをスクロール |
| Shift+Alt+← / → | フィット / 100% |
| Alt+F / Ctrl+Alt+F | フォーカス点へスクロール（左 / 右） |
| G / Shift+G | 構図グリッドの種類を巡回 / 基準（画像⇄Canvas）を切替 |
| Ctrl+L | フィルタ ON/OFF |
| Ctrl+E / Alt+E / Ctrl+Alt+E | エクスプローラで表示 / 既定アプリで開く / パスをコピー |
| Alt+S | 共有（Nearby Share 等。パスは**設定化**する） |
| M | （デバッグ用 GC）**未実装**（意図した割り切り。必要になれば追加） |

上表は代表的なもの。実装済みの全ショートカットは **`shortcuts.json`（唯一の情報源）**／
生成物 `docs/SHORTCUTS.md`／アプリ内 `F1` を参照。

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
PhotoQuickSelector.slnx
├── src/
│   ├── PhotoQuickSelector.Core/        … UI 非依存のコア（メタデータ・評価・永続化・出力生成）
│   │     ├── ImageMetadata             … 1 枚分の不変メタデータ（EXIF/XMP 抽出結果）
│   │     ├── MetadataReader            … MetadataExtractor を用いた抽出
│   │     ├── PhotoEvaluation           … rating/flag/colorlabel のドメインモデル
│   │     ├── MetadataStore             … SQLite 永続化（スキーマ移行込み・初回書き込みまで遅延作成）
│   │     └── PhotoFilter / RejectMove / CopyRename / FileMove … 絞込・bat 出力の純ロジック
│   └── PhotoQuickSelector.App/         … WinUI 3 アプリ（画面・描画・入力）
│         ├── MainWindow / MainPage     … キー入力の集約と 3 カラム骨組み（GridSplitter + 折りたたみ）
│         ├── Controls/FolderNavigationView … 左ペイン: フォルダツリー＋最近/お気に入り
│         ├── Controls/PhotoStatusBar / PhotoGridView / PreviewControl
│         │                             … 右ペイン: ステータスバー＋ Thumbnail / Preview（旧 ViewPhoto 相当）
│         └── AppSettings               … 最近フォルダ・お気に入り・ペイン幅 等の永続化
└── tests/
      └── PhotoQuickSelector.Core.Tests … xUnit。抽出・永続化・絞込・bat 生成・ビューポートを検証
```

- 単一ウィンドウの**左右分割**（§2）。`SwitchPresenter` によるモード切替は廃止。
- MVVM 寄りに整理（既存の `AppState` 相当を ViewModel 化、INotifyPropertyChanged は
  `CommunityToolkit.Mvvm` の `ObservableObject` / `[ObservableProperty]` を採用）。
- **アプリ設定の永続化**: 最近開いたフォルダ・お気に入り・左ペイン幅/折りたたみ状態を、
  ローカルアプリデータの JSON 等に保存（評価データの SQLite とは別物）。
- `[Windows.UI.Xaml.Data.Bindable]`（UWP 旧名前空間）は使わず、必要なら
  `[Microsoft.UI.Xaml.Data.Bindable]` を用いる。

---

## 6. 既存からの改善点（本再構築で必ず直す）

1. **紫カラーラベルにキーを割り当てる**（既存は 6–9 で赤黄緑青のみ）→ `P` に割当済み。
2. **デバッグ用ハードコードパスを完全排除**（`O:\…`, `P:\…` 等）。
3. **共有先パスのハードコード廃止** → 設定化。
4. **UWP 旧名前空間 `Windows.UI.Xaml.*` を排除**。
5. **Shift+Z の等倍表示を実装**（既存はコメントアウト）。
6. **文字化けコメントの解消**（UTF-8 統一）、命名ミス修正（`GraterEqual`→`GreaterEqual` 等）。
7. **死蔵コード（大量のコメントアウト）を持ち込まない**。
8. 先読みスレッド内の `.Result` / `.Wait()` 同期待ちを、可能な範囲で `async` 化または整理。
9. EXIF 解析を**公式 NuGet 化**（フォーク依存を解消）。
10. **UI を 2 モード切替から左右分割の単一画面へ再設計**（§2）。左=フォルダツリー＋最近/
    お気に入り、右=閲覧。明示ロード、リサイズ＋折りたたみ可能なスプリッター。

---

## 7. 実装の進め方（段階分割）

各ステップでビルド＆動作確認を挟む。

1. **Phase 1（本着手範囲）**: `PhotoQuickSelector.Core` ＝ メタデータ抽出 ＋ SQLite 永続化。
   UI なしで単体テストまで通す。
2. **Phase 2**: WinUI アプリ枠（**左右分割レイアウト**）＋ 左ペイン（フォルダツリー＋最近/
   お気に入り、明示ロード）＋ 右ペイン Thumbnail レイアウト ＋ 評価付与 ＋ キー操作。
3. **Phase 3**: Preview レイアウト（ズーム/パン/ナビゲーター/フォーカス表示、Win2D 描画）。
4. **Phase 4**: フィルタ ＋ クリップボード出力 ＋ 外部連携 ＋ 設定。

### 受け入れ基準（例）
- Sony 機画像で AF 枠が表示される。
- XMP レーティング付き画像の初期 rating が反映される。
- 評価を付けて再起動しても保持される（SQLite）。
- 3000 枚規模で初回一覧表示が実用的な時間で完了する。
