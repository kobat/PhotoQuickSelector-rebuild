# CLAUDE.md — PhotoQuickSelector-rebuild

写真を高速に閲覧・選別する Windows デスクトップアプリ（旧 `PhotoQuickSelector` の作り直し）。
詳細仕様は [SPEC.md](SPEC.md) を参照。本ファイルは作業の引き継ぎ用メモ。
過去の実装経緯・設計判断の詳細は [docs/HISTORY.md](docs/HISTORY.md) を参照（既存機能の修正前に該当節を読むこと）。

## 技術スタック / 構成
- WinUI 3 / .NET（App は `net10.0-windows`、Core は `net8.0`）/ Windows App SDK / CommunityToolkit.Mvvm
- EXIF 解析: 公式 NuGet `MetadataExtractor`（フォーク不使用）
- 永続化: `System.Data.SQLite.Core`（フォルダごとに `PhotoQuickSelector.sqlite3`）
- 構成:
  - `src/PhotoQuickSelector.Core/` … UI 非依存（メタデータ抽出・評価モデル・SQLite 永続化）
  - `src/PhotoQuickSelector.App/` … WinUI アプリ（左右分割UI・サムネイル・キー操作）
  - `tests/PhotoQuickSelector.Core.Tests/` … xUnit（108 件）

## ビルド / 起動（重要）
- **packaged（MSIX 開発）構成**で開発している。**exe を直接ダブルクリックしない**（無音終了する）。
- 前提: Developer Mode 有効、`winapp` CLI、WinUI テンプレート（導入済み）。
- ビルド＆起動（推奨。skill `winui:winui-dev-workflow` の `BuildAndRun.ps1`）:
  ```powershell
  cd src\PhotoQuickSelector.App
  & "C:\Users\kobat\.claude\plugins\cache\win-dev-skills\winui\0.3.0\skills\winui-dev-workflow\BuildAndRun.ps1"
  # ビルドのみ: -SkipRun
  ```
- もしくは `cd src\PhotoQuickSelector.App; dotnet run`。
- テスト: `dotnet test`。

## テストデータ
- `D:\Users\kobat\tmp_ClaudeCode用\20260228`（Sony α1 の DSC*.JPG＋Olympus OM-1 の P22*.JPG、71 枚）
- 同フォルダに旧アプリの `PhotoQuickSelector.sqlite3`（評価データ）あり。新アプリと互換（確認済み）。
- 注意: 評価操作は対象フォルダの sqlite に即保存される。検証は控えのあるフォルダで。

## 主要な決定事項
- **配布形態 = 素の自己完結 EXE（unpackaged）**。.NET/WinAppSDK 同梱。ただし開発は packaged、
  unpackaged 単一ファイル発行は **publish 時の構成**として後で組み込む（SPEC §0/配布節）。
- UI は旧「2モード切替」を廃止し**左右分割の単一画面**（左=フォルダツリー、右=閲覧）。
- 「同等」のゴール = 機能同等＋既知バグ改善（紫ラベルにキー割当、ハードコードパス排除、UTF-8 等）。
- ソースは UTF-8。コミットは原則ユーザー依頼時。コミット末尾に Co-Authored-By を付与。
- **コードコメントの方針**（2026-07-07 整理・基準ユーザー合意）: コードには「制約・why・単位・座標系・
  落とし穴」を書く。**public メンバーの `<summary>`（自明でも）と処理の区分ラベル的な短いコメントは残す**。
  実装経緯（案番号・旧機構との比較・実測値詳細）は HISTORY.md へ書き、コードには残さない。

## 現在の状態（要約）

**Phase 1〜4 すべて完了**。旧アプリの機能同等＋既知バグ改善を達成し、**v0.1.0** として公開向け発行済み。
`dotnet test` **113 件緑**（Core＋リンク参照の `PreviewViewport`/`DecodeGate`）。

- **Core**: メタデータ抽出（EXIF／AF点／GPS／LensMake）・SQLite 永続化（旧DB互換・遅延作成＋作成確認）・
  フィルタ・クリップボード出力・Reject移動・リネームコピー（いずれも純関数＋xUnit）
- **App（主要機能）**: 左右分割UI（フォルダツリー／お気に入り／最近。左ペイン下部の「読み込み」行右端のピンボタンで
  ピン留め解除するとフライアウト表示＝`AppSettings.LeftPanePinned`。Esc/外側クリック/読み込みで閉）／サムネイルグリッド／プレビュー
  （Win2D 3面構成＝メイン＋ルーペ＋ナビゲーター・フィルムストリップ・AF枠・構図グリッド・DPI考慮の
  段ズーム・ズーム状態維持・EXIF 詳細パネル＝右パネル上段をルーペと `E`/タブで切替）／評価編集（単一＋複数選択の一括評価）／
  右クリックメニュー（グリッド／フィルムストリップ共通・エクスプローラ流儀の対象確定）／フィルタ＋bat 書き出し／外部連携／
  セッション復元／全画面・イマーシブ・完全全画面（Shift+F）／Dark テーマ／日英ローカライズ
  （resw＋shortcuts.json SSOT・F1 チートシート）／設定ダイアログ（一般／高度な設定の 2 タブ）
- **パフォーマンス**: サムネイル＝圧縮バイト常駐＋可視分デコード（容量固定 LRU）／プレビュー先読み
  キャッシュ＝BGRA8 `byte[]`（PixelFrame）保持・LRU（LastUse 単独）＋容量予算・DecodeGate（grant 時の窓分類
  優先度選択＝フォーカス→選択窓→位置窓）・レート制限・sRGB 色管理スキップ・解凍爆弾ガード（宣言寸法 1GB／
  実ファイル 512MB 超はデコードせずスキップ）／ナビゲーター縮小ビットマップキャッシュ
- **配布**: unpackaged 自己完結 EXE（フォルダ／単一ファイルの pubxml 2 系統）・LICENSE／
  THIRD-PARTY-NOTICES 同梱・アプリアイコン・README（日英）

**実機確認が未了の項目**: 先読みキャッシュの解凍爆弾ガード（2026-07-07。巨大宣言寸法のテスト画像での再現確認は
未実施＝作れば `C` オーバーレイで観察可能。通常画像への無影響はビルド＋テスト 103 件緑で確認済み）。

**次の候補（未着手）**: 縦型画像の未回転保持＋描画時 GPU 回転（縦のデコード +80〜90ms 解消。大工事のため後回し。
実測値はメモリ `portrait-slowness-benchmarks` と HISTORY.md「縦型画像の表示・パンが遅い問題」節）。

## 実装経緯の詳細（docs/HISTORY.md）

機能ごとの実装経緯・設計判断・落とし穴・実測値・コミットハッシュは [docs/HISTORY.md](docs/HISTORY.md) に
機能単位の見出しで記録している（旧 CLAUDE.md「現在の進捗」の移設先）。

- **既存機能を修正・拡張する時は、着手前に HISTORY.md の該当節を必ず読むこと**（機能名で Grep）。
- 今後の作業記録は HISTORY.md 末尾へ追記し、本ファイルは「現在の状態（要約）」の数行更新にとどめる
  （CLAUDE.md の再肥大化防止）。

## キー操作（右ペイン・写真選択時）
- `0`–`5` レーティング / `6`–`9`＋`P` カラーラベル（赤黄緑青紫。7=`ColorLabel.Yellow`＝黄 `#FDD835`）/ `[` `]` レーティング増減 / `Ctrl+↑/↓` フラグ
  （複数選択中でも**通常評価は焦点の1枚のみ**に反映）
- 複数選択（両モード共通。焦点＝常に1枚／選択集合＝0..N枚で別概念）:
  `Shift+←/→` レンジ選択（起点から焦点までを連続選択）/ `Ctrl+←/→` 焦点のみ移動（集合は不変）/
  `Ctrl+Space` 焦点を選択集合へ参加/解除 / `Ctrl+A` すべて選択（絞込結果 `Photos` 全件を選択集合に。焦点据え置き。
  グリッド/プレビュー共通・右クリック「すべて選択」と同一） / 選択集合中の `←/→` はメンバー内で焦点巡回 / `Esc` 選択集合を解除 /
  マウス: `Ctrl+クリック`＝トグル参加／`Shift+クリック`＝レンジ選択（グリッド/フィルムストリップ共通。
  素のクリックはメンバー上なら集合維持で焦点移動／集合外なら集合リセット）
- `Alt+0`–`5`／`Alt+6`–`9`／`Alt+P` 一括評価（選択集合の全メンバーへ。レーティング/カラーラベル）
- `Ctrl+Alt+↑/↓` 一括フラグ（選択集合の全メンバーへ。単一フラグ `Ctrl+↑↓` の対称形）。**プレビューでは選択集合がある
  ときのみ一括フラグ／無いときは従来のルーペ縦スクロール**。集合が無ければ一括系は無効
- `Ctrl+L` フィルタ ON/OFF トグル（両モード共通、フライアウトは開かない）
- `Ctrl+E` エクスプローラで表示 / `Alt+E` 既定アプリで開く / `Ctrl+Alt+E` パスをコピー / `Alt+S` 共有
  （両モード共通。共有は `AppSettings.SharePath` 設定時はその exe 起動、未設定なら Windows 標準共有シート。設定はステータスバー右端のメニュー＞設定…から）。
  `Alt+E`/`Ctrl+Alt+E`/`Alt+S` は選択集合があれば全メンバーが対象（10枚以上は確認ダイアログ）。`Ctrl+E` は
  `/select` が複数不可のため焦点の1枚のみ
- ステータスバー右端の**ハンバーガーメニュー**（`&#xE700;`）から上記トグル/外部連携/設定をクリック実行も可（ショートカット併記）
- **右クリック**（グリッド／フィルムストリップ共通・`PhotoContextMenu`）: 評価（レーティング/カラーラベル/フラグ）・
  ファイルをコピー（表示中のみ/同名別拡張子も＝エクスプローラ貼り付けでファイルコピー／配下にリネームしてコピー）・
  パスをコピー・外部連携・すべて選択。**対象確定はエクスプローラ流儀**（集合メンバー右クリック＝集合全体／
  集合外右クリック＝その1枚を選び直して単独対象）。複数選択時は集合対象の項目に「(全選択ファイル)」を付す。
  Reject 移動は誤操作懸念のため右クリックには入れていない（フィルタバー側のみ）。10枚以上の一括対象では
  「既定のアプリで開く／共有／ファイルをコピー」に確認ダイアログを挟む。詳細は HISTORY.md「右クリックコンテキストメニュー」節
- `F11` フルスクリーン表示トグル（ステータスバー右端の全画面ボタンも同じ）/ `Esc` 全画面中なら通常表示へ復帰
  （全画面でない通常時の `Esc` は無反応＝プレビューを抜けない。プレビュー終了はフィルムストリップのダブルクリック）
- プレビュー中（マウス）: メイン大画面の**シングルクリック**＝フィット⇄ズーム切替（`Z` と同一倍率）/ **ダブルクリック**＝100%
  （`Shift+Z` と同一）。いずれもズーム中心は**クリック位置基準**（ホイールと同様）。ドラッグ＝パン。
  **フィルムストリップのダブルクリック**＝グリッドビューへ戻る（マウスでのプレビュー終了導線）
- プレビュー中: `←`/`→` 前後移動（移動後フォーカスはフィルムストリップへ移り `PageUp`/`PageDown`/`Home`/`End` が効く）
- プレビュー中: `Z` フィット⇄ズームトグル（ズーム側は**直近のズーム位置=倍率/中心を復元**。初回は等倍＝DPI考慮の
  1画像px=1物理px＝100%）/ `Shift+Z` 等倍 / `Shift+Alt+←/→` フィット/等倍 / ホイール ズーム。倍率はステータスバー
  右端に表示（ピクセル等倍＝100%）。拡大率により補間自動切替（等倍以上＝NearestNeighbor／縮小＝HighQualityCubic）
- プレビュー中: `+`/`-` 段ズーム（イン/アウト）。ホイールと同じ round な段ラダー（フィット段挟み込み込み）にスナップ。
  テンキー・メイン段（JIS/US どちらも `+`/`-` 物理キー、Shift 不問）の両対応。ホイールも段スナップ式（中途半端な倍率にならない）
- プレビュー中: `F` イマーシブ表示トグル（右パネル＋フィルムストリップを畳んでメインを全域表示。F11＋左ペイン非表示と合成で画面一杯）
- `Shift+F` 完全全画面モード（ウィンドウ全画面＋左ペイン/ステータスバー非表示＋イマーシブ＋余白0 を一括）。グリッド時は
  プレビューに入って全画面化。解除は `Shift+F` または `Esc`（入る前の状態へ正確復元）
- プレビュー中: `I` メタ情報オーバーレイ（案B）トグル / `C` 先読みキャッシュ一覧オーバーレイ（デバッグ・初期非表示）
- プレビュー中: `E` 右パネル上段をルーペ⇄EXIF 詳細で切替（上段のタブクリックでも可。状態は
  `AppSettings.PreviewExifPanel` に永続化。全ディレクトリ・全タグ＝Core `ExifTagReader.ReadAllTags`／
  UI `Controls/ExifDetailPanel`・グループ化 ListView 仮想化）
- プレビュー中: `G` 構図グリッド種類を巡回（None→中央十字→三分割→正方形→None）/ `Shift+G` グリッド基準を切替
  （画像⇄Canvas）。正方形は短辺を N 等分した正方セルを画像中央から対称配置（N＝`AppSettings.GridSquareDivisions`・既定8。
  偶数Nは中央に線・奇数Nは中央線なし）。種類/基準は `AppSettings` に永続化（次回起動で復元）

## 既知の注意点
- 検証で `DSC09432.JPG` の rating が null→0 に変わっている（実効値は同じ）。
- コミット時の `LF→CRLF` 警告は無害（Windows の改行正規化）。
- WinUI TreeView は子コレクションの `Clear()`→全件再追加で内部状態が壊れる。**差分同期で更新する**こと。

### 開発フローのハマりどころ
- アプリは**マルチインスタンス**。`winapp run`／computer-use の `open_application` を呼ぶたびに
  ウィンドウが増える。後始末は PowerShell `Stop-Process -Name PhotoQuickSelector.App -Force`。
- `BuildAndRun.ps1` は **csproj ディレクトリ（`src\PhotoQuickSelector.App`）から実行**する。
  リポジトリ直下から実行すると `No .csproj file found in current directory` で失敗。
- **`winapp run` が「multiple .exe files were found / placeholder」で起動失敗**することがある（2026-06-20 遭遇）。
  原因＝自己完結ランタイム由来の `createdump.exe`／`RestartAgent.exe` がビルド出力（`…\win-x64` と `…\win-x64\AppX`）に
  並び、マニフェストの exe プレースホルダを解決できないため。ビルド自体は `BUILD SUCCEEDED`。回避策＝
  両フォルダから上記 2 つの exe を消してから `winapp run "<…\win-x64\AppX>" --detach --json` で AppX を直接指定して起動
  （登録 AUMID は `…!App`）。リビルドで再生成されるので恒久対策が要るなら別途検討。
- packaged 開発時、ウィンドウの実体プロセスは `photoquickselector.app.exe`（ワーカー）。
  computer-use のスクリーンショットは AUMID 付与だけだと中身がマスクされるので、
  `request_access` に `photoquickselector.app.exe` を渡すと表示される。
- `settings.json` の実体は packaged 時
  `…\Packages\<PFN>\LocalCache\Local\PhotoQuickSelector\settings.json` にリダイレクトされる。
- × ボタン（`f6cbef4`）はビルド成功・`RemoveFavorite` ロジック検証済み。ユーザーが画面目視確認済み
  （2026-06-14、問題なし）。

### Win2D プレビューのキー入力（ステージ A で判明）
- **`UserControl.Focus()` は効かないことがある**。キー入力を受けたい場合はフォーカス可能な子
  `Control`（ここでは `IsTabStop=True` の `CanvasControl`）に `Focus()` する。これで `←`/`→`/`Z`/
  ホイール/ドラッグはすべて動作（目視確認済み）。
- **`Esc` は WinUI のフォーカス管理に先取りされ `KeyDown` に届かない**。`KeyboardAccelerator`
  （`CanvasControl.KeyboardAccelerators` に追加、ツールチップは `KeyboardAcceleratorPlacementMode.Hidden`）
  で処理する実装にしてある。ただし **computer-use の合成 `Esc` 注入では発火しない**（`←`/`Z` 等は注入で
  動く）。Esc でのプレビュー終了は**実キーボードでユーザー確認済み（2026-06-15、動作 OK）**。プレビュー終了の
  正規手段はダブルクリック（SPEC §2、動作確認済み）。SPEC §3-7 の `Esc` は本来「選択リセット」用途。
- **computer-use の合成キーはプレビュー入場直後（`FocusForKeys` 後）なら `Z`/`G`/`Alt+矢印`/数字キー等が
  通る**が、キャンバスへ別途クリックした後などはフォーカスが外れて通らないことがある。検証は
  「ダブルクリックで入場 → 直後にキー」の順で行うと安定。

### Win2D の Orientation / DPI（ステージ B で判明・重要）
- **`CanvasBitmap.LoadAsync` は EXIF Orientation を自動適用して返す**（WIC 経由）。生 8640×5760・
  Orientation=8 の画像は `SizeInPixels` が 5760×8640（正立）になる。**自前で `OrientationMatrix` を
  かけると二重回転**になり、横→縦が横のまま等の誤表示になる（ステージ A は Orientation=1 画像しか
  無く見逃していた）。→ 画像/グリッド描画は回転を加えず、`SizeInPixels` 基準でスケール＋平行移動のみ。
- **AF フォーカス点 `0x2027[2],[3]` は生センサー座標（Orientation 適用前）**。正立ビットマップへ重ねるには
  `PreviewViewport.OrientationMatrix(orientation, 生W, 生H)`（生寸法＝`ImageMetadata.OriginalWidth/Height`）で
  表示空間へ写し、`ImageToCanvas` でキャンバスへ。基準寸法は `FocusReferenceSize`(=`0x2027[0],[1]`)。
- **`CanvasBitmap.Size` は DPI 依存**（高 DPI スケールで縮む）。寸法計算は必ず `SizeInPixels` を使う。
