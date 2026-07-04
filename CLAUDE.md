# CLAUDE.md — PhotoQuickSelector-rebuild

写真を高速に閲覧・選別する Windows デスクトップアプリ（旧 `PhotoQuickSelector` の作り直し）。
詳細仕様は [SPEC.md](SPEC.md) を参照。本ファイルは作業の引き継ぎ用メモ。

## 技術スタック / 構成
- WinUI 3 / .NET（App は `net10.0-windows`、Core は `net8.0`）/ Windows App SDK / CommunityToolkit.Mvvm
- EXIF 解析: 公式 NuGet `MetadataExtractor`（フォーク不使用）
- 永続化: `System.Data.SQLite.Core`（フォルダごとに `PhotoQuickSelector.sqlite3`）
- 構成:
  - `src/PhotoQuickSelector.Core/` … UI 非依存（メタデータ抽出・評価モデル・SQLite 永続化）
  - `src/PhotoQuickSelector.App/` … WinUI アプリ（左右分割UI・サムネイル・キー操作）
  - `tests/PhotoQuickSelector.Core.Tests/` … xUnit（96 件）

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

## 現在の進捗
- **Phase 1 完了**: Core（メタデータ抽出・SQLite 永続化）＋テスト 46 件。実画像・旧DB互換も検証済み。
- **Phase 2 完了**: WinUI アプリ本体（左右分割／フォルダツリー遅延読込／サムネイル／評価編集／キー操作）。
  追加修正: ダブルクリック=展開、初期文言修正、レーティング★の EXIF/ユーザー色分け、
  フォルダ追加削除の差分同期反映（更新ボタン/F5/右クリック更新/再展開）。
- **Phase 2 補完 完了**: アプリ設定（`AppSettings`）を新設し、最近開いたフォルダ／お気に入り、
  左ペイン幅・折りたたみ状態を JSON 永続化。左ペイン上部に「お気に入り」「最近開いたフォルダ」
  の Expander（件数 0 のときは非表示）を追加。お気に入りはツリーノード右クリックで追加/解除、
  最近は読み込み時に自動記録。設定は終了時（幅/折りたたみ）と変更時（最近/お気に入り）に保存。
  - 保存先: `%LOCALAPPDATA%\PhotoQuickSelector\settings.json`（素のファイルパス＝unpackaged でも可）。
    packaged 開発時は実体が `…\Packages\<PFN>\LocalCache\Local\PhotoQuickSelector\settings.json` に
    リダイレクトされる点に注意。
  - トリミング発行対策で JSON は source generator（`AppSettingsJsonContext`）を使用。
    日本語パスは `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` で生 UTF-8 保存。
  - お気に入りの削除: お気に入りリスト各行の **× ボタン**＋右クリック「お気に入りから削除」（`f6cbef4`）。
    ユーザーによる画面目視確認済み（2026-06-14、問題なし）。
- **Phase 3 ステージ A 完了（`4b3aca0`）**: 右ペインに大画面プレビューを追加（Win2D `CanvasControl`）。
  サムネイルのダブルクリックで Thumbnail⇄Preview を相互遷移（`MainViewModel.IsPreviewMode` ＋
  `ThumbnailVisibility`/`PreviewVisibility` で排他表示）。下部に横スクロールのフィルムストリップ。
  ズーム（ホイール／`Z`=フィット⇄100%／`Shift+Z`=等倍）、ドラッグでパン、`←`/`→` で前後移動、
  EXIF Orientation を回転トランスフォームで適用。実機（Sony DSC*.JPG 4000 枚）で目視確認済み。
  - 新規: `Controls/PreviewControl.xaml(.cs)`（Win2D）、`Controls/PreviewViewport.cs`（ズーム/パン/
    Orientation 変換の純ロジック）。`Microsoft.Graphics.Win2D` 1.4.0 を追加。
  - 設計メモ: 写真コレクション/選択は既存 `MainViewModel.Photos` / `SelectedPhoto` を共有。
    前後移動で `SelectedPhoto` を更新し、`MainPage` 側でサムネイルグリッドの選択も同期。
- **Phase 3 ステージ C 完了（`05a7947`）**: 評価キーを `PhotoKeyCommands.TryHandleEvaluation` に
  共通化（`MainPage` のサムネイル側と `PreviewControl` の両方から呼ぶ）。プレビューで
  `0`–`5`/`6`–`9`・`P`/`[ ]`/`Ctrl+↑↓` が効く。`Alt+矢印`=パン、`Shift+Alt+←/→`=フィット/100%。
  `CanvasBitmap` の前後 N 枚先読みキャッシュ（`Dictionary`＋inflight 共有＋範囲外破棄＋デバイス
  再生成で世代無効化）。実機で評価キー・パン・前後移動を確認済み。
- **Phase 3 ステージ B 完了（Core `41476a6` / App `05a7947`）**: AF 枠オーバーレイ／三分割グリッド線（`G`）／
  フォーカス点スクロール（`Alt+F`）。Core 拡張で `ImageMetadata.FocusReferenceSize`（Sony `0x2027` の `[0],[1]`）を
  追加（`MetadataReaderFocusTests` 追加、計 47 件）。右ナビゲーターは今回見送り。
  実機（横構図 DSC*.JPG／縦構図 DSC03334=Orientation8）で AF枠・グリッド・スクロールを目視確認済み。
  - **重大な発見＆修正**: `CanvasBitmap.LoadAsync` は **EXIF Orientation を自動適用**して返す
    （生 8640×5760 → `SizeInPixels` 5760×8640 で正立）。ステージ A は `OrientationMatrix` を
    自前で重ねており、回転画像は**二重回転**で誤表示していた（Orientation=1 のみのテストで見逃し）。
    修正: 画像/グリッドは回転を加えず `SizeInPixels`（DPI 非依存）でスケール＋平行移動のみ。
    AF 枠だけは生センサー座標 `0x2027[2],[3]` を `OrientationMatrix`（生寸法基準）→`ImageToCanvas`
    で表示空間へ写す。`PreviewViewport.BuildTransform` は不使用化（`OrientationMatrix` は AF で利用）。
  - 検証中に既知 DPI バグも修正: グリッドが `_bitmap.Size`（DPI 依存）基準で画像外へはみ出していた。
- **プレビューのキーボード入力フォーカス問題 完了（`f54d9b4`、`origin/main` プッシュ済み）**:
  「画像クリック後にキーが効かない」「フィルムストリップフォーカス時の `Alt+矢印` が画像切替になる」を解消。
  キー入力を **Window 直下の `MainWindow` ルート `Grid`（`x:Name="RootGrid"`）の `PreviewKeyDown`(tunneling)**
  で集約（`RootGrid.PreviewKeyDown += RootGrid_PreviewKeyDown` → `MainPage.HandleGlobalKeyDown` →
  `IsPreviewMode` で `PreviewControl.HandleKeyDown(VirtualKey)` ／サムネイル評価キーへ分岐、処理したら
  `e.Handled=true`）。RootGrid は最上位なのでフォーカス位置に依存せず tunneling の最初に届き、Handled で
  後続 KeyDown を抑止できる＝ListView より先にキーを奪える（bubbling KeyDown では ListView に Alt+矢印を
  先取りされ誤動作した）。`PreviewControl.OnKeyDown`(イベント) は `bool HandleKeyDown(VirtualKey)` へ変更し
  `MainCanvas` の `KeyDown` 配線を削除、到達不能化した `MainPage.PhotoGrid_KeyDown` も削除。実機確認済み。
  経緯と原因の詳細はメモリ `preview-keyboard-focus-investigation` ／ 再利用知見は `winui-keyinput-gotchas`。
- **Phase 3 ステージ B 残（右パネル）完了（`993c7c2`、`origin/main` プッシュ済み）**: プレビューを旧アプリ同様の3面構成に拡張。
  左カラム＝メイン大画面＋フィルムストリップ、右カラム＝**上：ズームプレビュー(100% ルーペ)** ＋
  **下：ナビゲーター**（`GridSplitter` でリサイズ可）。
  - **ナビゲーター**（`NavCanvas`）: 全体縮小画像＋**青枠＝メイン表示領域**（`PreviewViewport.VisibleImageRect`
    で算出、メインのズーム/パンに追従）＋**緑枠＝AF枠**。クリック/ドラッグでメイン表示位置を移動
    （`NavMoveMainTo`：ナビ座標→画像点へ逆変換しメイン中央へパン）。
  - **ズームプレビュー**（`ZoomCanvas`／独立 `_zoomViewport`）: 100% 表示・ロード時に AF 点へ寄せる。
    `Ctrl+Alt+矢印`＝短辺25%スクロール（`ZoomPanByRatio`）、`Ctrl+Alt+F`＝フォーカス点へ、
    ドラッグ/ホイールでも操作可。メインとは独立スクロール。
  - 実装メモ: 3 つの `CanvasControl` は **既定で同一デバイス共有**（`UseSharedDevice=true`）なので、
    メインがロードした単一 `CanvasBitmap` を再デコードせず描画に流用できる（旧アプリと同じ手法）。
    AF 枠描画は `DrawFocusFrame(ds, toCanvas, thickness)` に共通化しメイン/ナビで共用。
  - 実機（Sony 100MSDCF）で右パネル表示・ナビ青枠追従・ナビクリックパン・ルーペ独立スクロール
    （`Ctrl+Alt+↓`/`Ctrl+Alt+F`）・`Z` でのナビ青枠縮小を目視確認済み（2026-06-17）。
  - 変更: `Controls/PreviewControl.xaml(.cs)`、`Controls/PreviewViewport.cs`（`VisibleImageRect` 追加）。
- **フィルムストリップ/グリッドの評価表示 完了（`c53ce4c`）**: フィルムストリップ各セルに
  色枠／旗バッジ／★／色ドット／ファイル名を表示し、サムネイルグリッドも同デザインへ統一。
  - レイアウト: 旗バッジ＝左上、色ドット＝右上、★レーティング＝下部中央、ファイル名＝
    グリッドは下部黒帯／フィルムストリップは画像下の独立行。色ラベルは**枠線(先頭1色)＋ドット(全色)併用**
    （`PhotoEvaluation` は複数色ラベル同時可なので枠は enum 順 Red→…→Purple の先頭色）。
  - 旗／色ドット／★は `#99000000` の角丸黒バッジで統一。**単一要素時は正方形**
    （グリッド `Height=30 MinWidth=30`／フィルムストリップ `Height=20 MinWidth=20`）。
    グリッドの色ドット円は 15px（フィルムストリップは 8px）。
  - `PhotoItemViewModel` に `ColorLabelBorderBrush`（先頭色 or `Transparent`、枠太さは XAML 定数 3px で
    レイアウト固定）／`FlagVisibility`／`RatingVisibility`／`ColorDotsVisibility` を追加し、評価操作
    （`SetRating`/`NotifyFlag`/`ToggleColorLabel`）で変更通知。**Core・コードビハインドは非変更**、
    既存プロパティ（`RatingStars`/`RatingForeground`/`Pick|RejectVisibility`/各 `*Visibility`/`FileName`）を再利用。
  - 変更: `Controls/PreviewControl.xaml`、`MainPage.xaml`、`ViewModels/PhotoItemViewModel.cs`。
    実機（Sony 100MSDCF）でグリッド／プレビュー双方を目視確認済み（2026-06-17）。`dotnet test` 47 件緑。
- **詳細メタ情報パネル（旧 `PhotoInfoControl` の移植）完了**: 旧アプリの「画面上部右の写真情報」を2系統で再現。
  表示項目は旧同等（ファイル名／画像サイズ／ファイルサイズ／撮影日時／焦点距離・絞り・SS・露出補正・ISO／
  カメラ・レンズ名／★・旗・カラーラベル）。**Core は表示用データを既に保持**しており、評価表示と同じく
  `ImageMetadata` の各 `*Description` を再利用（GPS のみ Core を微拡張、後述）。
  - **案B＝プレビューのオーバーレイ**: メイン大画面の左上に半透明で重畳（`IsHitTestVisible=False` で
    ズーム/パンを妨げない）。`I` キーでトグル（`MainViewModel.ShowInfoOverlay`／`InfoOverlayVisibility`、
    `PreviewControl.HandleKeyDown` に `I` 分岐追加）。EXIF は角丸チップ列で表示。
  - **案A＝ステータスバー（右ペイン上部）への埋め込み**: 旧同様の**2段組**（上＝ファイル名・サイズ・MB＋
    EXIFチップ／下＝カメラ・レンズ・評価・**GPSボタン**・撮影日時）を**右寄せで密集**表示。ステータスバー行は
    `Auto` 高のまま中身2段で自然に伸ばす。グリッド/プレビュー共通の行なので、グリッドで選ぶだけでも出る。
  - **GPS 地図ボタン**: `HasGpsLocation` のときだけ表示、クリックで撮影位置を Google マップで開く
    （`MainPage.GpsButton_Click` → `Launcher.LaunchUriAsync`）。十進緯度経度が地図 URL に必要なため
    **Core を拡張**: `ImageMetadata.GpsLatitude/GpsLongitude`（nullable）＋`MetadataReader.ReadGps` が
    `GeoLocation.Latitude/Longitude` も返すよう変更。`PhotoItemViewModel.MapUri/GpsTooltip/GpsVisibility`。
  - **重要（WUI2010 回避）**: 選択写真への束縛はネスト `x:Bind ViewModel.SelectedPhoto.*`（3 セグメント）だと
    起動時 null 中間で**クラッシュ警告**が出る。→ パネル直下の `DataContext="{x:Bind ViewModel.SelectedPhoto}"`
    ＋内部はクラシック `{Binding}`（null セーフ・評価のライブ更新も追従）にして警告ゼロ。
  - 表示用プロパティを `PhotoItemViewModel` に追加（`ImageSizeText`/`FileSizeText`/`TakenDateTimeText`/
    `CameraLensText`/`ExifChips`/`HasGps`/`GpsVisibility`/`GpsTooltip`/`MapUri`）。下段文字は
    `TextFillColorSecondaryBrush`、カメラ/レンズは省略せず全文表示（ユーザー要望）。
  - 変更: `Core/ImageMetadata.cs`・`Core/MetadataReader.cs`、`MainPage.xaml(.cs)`、
    `Controls/PreviewControl.xaml(.cs)`、`ViewModels/MainViewModel.cs`・`PhotoItemViewModel.cs`。
    実機で案A/案B 表示・`I` トグル・GPSボタンで地図オープンを確認済み（2026-06-18）。`dotnet test` 47 件緑。
- **`cffb7c1` まで（Phase 3 A/B/C 含む）／`f54d9b4`（キー集約）／`993c7c2`（右パネル）／`c53ce4c`（評価表示）
  `origin/main` にプッシュ済み。詳細メタ情報パネル（案A/案B＋GPS）は `0c53aa4` でプッシュ済み。**
- **構造化リファクタリング Phase 1 完了**: 肥大化した `Controls/PreviewControl.xaml.cs`（674 行・6 系統混在）を
  **挙動・XAML 配線を一切変えずに** partial class で関心事ごとへ分割。さらに先読みキャッシュを独立クラスへ抽出。
  - 分割（すべて `partial class PreviewControl`、同一 `Controls/` フォルダにフラット配置）:
    `PreviewControl.xaml.cs`（205 行＝骨組み・ViewModel 配線・ライフサイクル・画像ロード統括）／
    `.MainCanvas.cs`（メイン描画・パン/ズーム・ポインタ）／`.Overlays.cs`（三分割グリッド線・AF 枠・フォーカス点幾何）／
    `.Loupe.cs`（右上ルーペ）／`.Navigator.cs`（ナビゲーター）／`.Input.cs`（キー処理 `HandleKeyDown`）。
  - **`PreviewBitmapCache.cs`（新規・独立クラス）**: `LoadAsync`/`LoadCoreAsync`/`ClearCache`/`TrimCache`/
    `PrefetchNeighbors` を移設し `GetAsync`/`Clear`/`Trim`/`Prefetch` API に整理。`ICanvasResourceCreator` のみ依存＝
    UI 非依存で将来 xUnit テスト可能（`PreviewViewport` と同方針）。コントロール側は `WindowPhotos()`→`WindowPaths()`
    （VM 返し→パス返し）に整理し `_cache.GetAsync/Clear/Trim/Prefetch` を呼ぶだけに。
  - partial 分割なので `MainCanvas_Draw` 等のハンドラ名・`PreviewControl.xaml` は不変＝リスクゼロ。
    csproj は Compile グロブ取り込みなので新規 `.cs` は自動コンパイル。`BUILD SUCCEEDED`／`dotnet test` 47 件緑。
    実機目視確認は未実施（挙動不変のため次セッションで前後移動・先読み・ルーペ・ナビを最終確認推奨）。
  - 構造化の続き（次の候補）: Phase 2＝評価バッジ（旗/色ドット/★）とメタ情報パネルを共通 UserControl/リソース化
    （グリッド・フィルムストリップ・案A・案B の 4 重複を解消）／Phase 3＝左ペインを `FolderNavigationView` に切り出し。
- **構造化リファクタリング Phase 2 完了（バッジ部品のみ・最小スコープ）**: 評価バッジの「色ドット 5 個」と
  「旗グリフ対」が 4 箇所（サムネイルグリッド／フィルムストリップ／メタ案A／メタ案B）にコピーされていた重複を、
  2 つの部品 UserControl に集約。**各セル/パネルのレイアウト・配色・ファイル名配置は現状維持**（統合はしない）。
  - 新規: `Controls/ColorLabelDots.xaml(.cs)`（色ドット列。DP `DotSize`/`Spacing`）、
    `Controls/FlagGlyph.xaml(.cs)`（採用=旗/拒否=×。DP `GlyphSize`）。
  - 束縛: 部品の DataContext は呼び出し側から継承した `PhotoItemViewModel`。内部の状態はクラシック
    `{Binding RedVisibility}`/`{Binding PickVisibility}` 等（null セーフ・ライブ更新追従）、サイズ DP は
    部品 XAML から `{x:Bind DotSize}`。**可視性ラッパー（`ColorDotsVisibility`/`FlagVisibility`）は部品に含めず**
    呼び出し側（グリッド/フィルムは黒バッジ `Border`、案A/案B は部品要素自身）に付与＝空バッジ事故を防止。
  - 差し替えサイズ: グリッド `DotSize=15 Spacing=4`/`GlyphSize=16`、フィルム `8/2`/`11`、案A `10/3`/`12`、案B `9/3`/`12`。
    `PreviewControl.xaml` に `xmlns:ctl` を追加。**`PhotoItemViewModel`・Core・コードビハインドは非変更**。
  - 変更: `MainPage.xaml`・`Controls/PreviewControl.xaml`（inline の 5-Ellipse/FontIcon 対が両ファイルから消滅）。
    `BUILD SUCCEEDED`／`dotnet test` 47 件緑。純 XAML リファクタで挙動不変。実機目視（4 箇所のバッジ同一性・
    評価キーでのライブ更新）をユーザー確認済み（2026-06-18）。
  - 構造化の続き（Phase 3 候補）: サムネイルセル（グリッド/フィルム）統合、メタパネル（案A/案B）統合、
    左ペインを `FolderNavigationView` に切り出し。
- **構造化リファクタリング Phase 3-A 完了（左ペイン切り出し）**: 肥大した `MainPage` の左ペイン
  （お気に入り/最近/フォルダツリー）を独立 UserControl `Controls/FolderNavigationView.xaml(.cs)` へ移設。
  `MainPage` は「3カラム配置＋ペイン間調停（左ペイン開閉/幅保存・キー委譲）」に近づいた（XAML 376→185 行、
  cs 284→127 行、計 323 行を移動）。
  - 結合最小化: 左ペインの読み込み/お気に入り操作はすべて共有 `MainViewModel`（`LoadFolderAsync`/
    `ToggleFavorite`/`RemoveFavorite`/`RemoveRecentFolder`/`IsFavorite`）へ直接委譲。`FolderNavigationView` に
    `ViewModel` プロパティを持たせ `MainPage` ctor で注入（`PreviewControl` と同一パターン、setter で `Bindings.Update()`）。
    右ペインは `ViewModel.Photos` 変化を自動観測するのでペイン間イベント配線は不要。
  - 移設: ツリー（`Expanding`/`DoubleTapped`/F5）・更新（差分同期。**WinUI TreeView は Clear→全件追加で壊れるため
    差分同期を維持**）・読み込み・お気に入り/最近の全ハンドラと `RootFolders`・ドライブ列挙（`Loaded` で実行）。
  - `MainPage` 残置（骨組み）: 3カラム `Grid`/`LeftColumn`/`GridSplitter`、左ペイン幅・折りたたみの永続化
    （`RestoreLeftPaneLayout`/`SaveLeftPaneLayout`/`ToggleLeftPaneButton_Click`＝`LeftColumn` を触るため）、
    右ペイン一式、`HandleGlobalKeyDown`、`MainWindow.Closed → SaveLeftPaneLayout` 配線（不変）。
  - **`MainViewModel`・`AppSettings`・`FolderNode`/`FolderShortcut`・Core・`MainWindow` は非変更**。
    `BUILD SUCCEEDED`／`dotnet test` 47 件緑。純構造移動で挙動不変だが実機目視（ツリー展開/読込/更新差分同期/
    お気に入り/最近/左ペイン開閉・幅復元）は確認推奨。
  - 次（Phase 3-B 候補）: 右ペイン（ステータスバー＋サムネイル＋プレビュー）を `PhotoBrowserView` へ。越境は
    トグルボタン（`ToggleLeftPaneRequested` イベント）とキー委譲（`HandleGlobalKeyDown` 委譲）の 2 点。
- **構造化リファクタリング Phase 3-B 完了（右ペインを 2 コントロールへ分割）**: 右ペインを丸ごと 1 つにせず、
  **ステータスバーとサムネイルグリッドを別々に**切り出した（プレビューは既に `PreviewControl` 独立済み）。
  右ペインの `Grid`（Column 2・2 行）はコンテナとして `MainPage` に残り、3 子（`PhotoStatusBar`／`PhotoGridView`／
  `PreviewControl`）を並べる。`MainPage` は XAML 54 行・cs 103 行まで縮み「3カラム＋ペイン間調停」のシェルに。
  - 新規 `Controls/PhotoStatusBar.xaml(.cs)`: 件数＋メタ情報パネル案A＋GPS＋ProgressRing。`ViewModel` 注入。
    開閉ボタンは `LeftColumn`（骨組み）を操作するため **`ToggleLeftPaneRequested` イベント**で `MainPage` へ委譲。
    `GpsButton_Click` は本体へ移設。
  - 新規 `Controls/PhotoGridView.xaml(.cs)`: サムネイル `GridView`＋バッジ付きアイテムテンプレート。`ViewModel` 注入。
    **選択双方向同期を内包**（選択→`ViewModel.SelectedPhoto`／VM の `SelectedPhoto` 変化→`SelectedItem` 反映＝
    旧 `MainPage.ViewModel_PropertyChanged` を移設）。ダブルクリックで `EnterPreview()`。表示/非表示（プレビューとの
    排他）は呼び出し側が `ThumbnailVisibility` で制御。
  - `MainPage` 残置: 3カラム＋右ペイン 2 行の骨組み、左ペイン幅/折りたたみ永続化（`ToggleLeftPane()` にリネーム）、
    `HandleGlobalKeyDown`（`Preview` を直接参照。`PhotoGrid` は参照しないので影響なし）、ViewModel 所有＋各子へ注入。
    `MainPage.xaml` 31 行目のコメントを実態（ステータスバー＋サムネイルグリッド/プレビュー排他）へ修正。
  - **`MainViewModel`・Core・`MainWindow`・既存 `PreviewControl`/バッジ部品は非変更**。未使用化した
    `using Windows.System;` を整理。`BUILD SUCCEEDED`／`dotnet test` 47 件緑。実機目視（件数/メタ情報/GPS/
    左ペイン開閉・グリッド選択⇄プレビュー同期・ダブルクリック入場）をユーザー確認済み（2026-06-18）。
- **×ボタンが1回で閉じない問題 修正完了（2026-06-19）**: 「画像表示・操作後にウィンドウ右上の×が
  1回で閉じないことがある（時々2回必要、稀に複数回）」不具合を解消。`MainWindow` を
  **`ExtendsContentIntoTitleBar=false`（システム標準タイトルバー）** に変更し、カスタムタイトルバー
  （`AppTitleBar`）を撤去。
  - **真因**: カスタムタイトルバー（`ExtendsContentIntoTitleBar=true`）だと×が XAML の非クライアント入力
    経路＝フォーカス機構と同じ土俵で処理され、画像操作後にフォーカスが GridViewItem/CanvasControl へ
    乗った状態だと×の押下がフォーカス後退（→祖先 ScrollViewer）に消費されて閉じない**確率的レース**。
    一時診断ログ（UIスレッドのハートビート／`InputNonClientPointerSource` の×クリック記録／フォーカス遷移）
    で、(a) クリックは届いている (b) UIスレッドのストールではない (c) 失敗は必ずフォーカス後退を伴う、を確認。
    実機実験で `TitleBar` コントロール／旧来 `SetTitleBar` の双方（=true）で再現、**`=false` でのみ100%確実**と
    確定。経緯はメモリ `close-button-titlebar-focus-race`。
  - トレードオフ: Mica 一体型のカスタムタイトルバー意匠は失うが×は確実。診断ログは撤去済み（差分は
    `MainWindow.xaml`/`.xaml.cs` の2ファイルのみ）。`BUILD SUCCEEDED`／`dotnet test` 47 件緑。
    実機で見た目・×の1クリック終了をユーザー確認済み（2026-06-19）。
- **Phase 4-A 完了（`c073853`、`origin/main` プッシュ済み・2026-06-19）**: フィルタ＋クリップボード出力（SPEC §3-4/§3-5）。
  - **Core（UI 非依存・テスト付き）**: `PhotoFilter`（レーティング `≧/＝`＋`NoRating`／フラグ3種／カラー5色 **AND** の
    `Matches`、`@rem` 用 `DescribeConditions`。旧 `GraterEqual`→`GreaterEqual` に改名＝SPEC §6-6）／
    `ClipboardExport`（ファイル名一覧 ／ 移動 `.bat` 生成。`@rem` ヘッダ＋`set FROMDIR=..`＋
    `move %FROMDIR%\<拡張子なし>* %TODIR%`）。**テスト 21 件追加＝計 68 件緑**。
  - **App**: `ViewModels/FilterViewModel`（各入力を Core モデルへ反映し `Changed` 発火。**初期状態＝有効**＝
    条件未指定で全件表示）。`MainViewModel` を **`AllPhotos`（全件・恒久）/`Photos`（絞込ビュー）の二層**化し
    `ApplyFilter()`（選択維持／外れたら解除）・`FilteredCountText`・コピー用テキスト生成を追加。
    **サムネイルは `AllPhotos` 全件を先読み**するのでフィルタ変更で再ロード不要。
  - **UI**: `Controls/FilterBar.xaml(.cs)`＝**ステータスバー内**（開閉ボタンと件数 TextBlock の間）の漏斗ボタン＋
    フライアウト。条件入力は **`DataContext={x:Bind ViewModel.Filter}`＋クラシック `{Binding}`（null セーフ）**、
    コピーは **`DropDownButton` の明示メニュー**（"ファイル名一覧をコピー"／"移動batを生成してコピー"。旧の
    "Ctrl 押下で .bat" 隠し挙動を改善）。`RatingControl` は値型不一致のためコードビハインド（`ValueChanged`/開く時同期）。
    `PhotoStatusBar` は `ViewModel` 注入を内包 `FilterBar` へ転送。
  - **キー**: `Ctrl+L` でフィルタ ON/OFF トグル（フライアウトは開かない。`MainPage.HandleGlobalKeyDown` で両モード共通）。
  - 既知の割り切り: プレビュー中に `Ctrl+L`/条件変更で表示中の写真が絞り込みから外れると選択解除→プレビューが空になる
    （通常はサムネイル時に使う想定。必要なら 4-B で抑止）。ユーザー画面確認済み（2026-06-19）。
    → **後日アンカー方式で改善（2026-06-21、後述「フィルタで選択写真が外れた時の挙動改善」）**。空表示自体は維持しつつ、
    再表示で元ファイル復元／外れ中の前後移動を元位置基準に。
  - 変更/新規: `Core/PhotoFilter.cs`・`Core/ClipboardExport.cs`、`ViewModels/FilterViewModel.cs`・`MainViewModel.cs`、
    `Controls/FilterBar.xaml(.cs)`・`PhotoStatusBar.xaml(.cs)`、`MainPage.xaml(.cs)`、テスト 2 ファイル。
- **フルスクリーン表示 完了（`5a97b7c`、`origin/main` プッシュ済み・2026-06-20）**: ウィンドウ全体の全画面表示を追加。
  WinAppSDK 標準の **`AppWindowPresenterKind.FullScreen`**（枠・タイトルバー・タスクバーごと非表示）を使用。
  - **トグルロジックは `MainWindow` に一元化**（`public void ToggleFullScreen()`）。AppWindow を所有する Window 側で完結。
  - **キー**: `RootGrid_PreviewKeyDown`（キー集約点）で `F11`＝トグル、`Esc`＝**全画面中のときだけ** `Default` へ復帰
    （Esc は SPEC §3-7 で本来「選択リセット」用途のため、全画面でないときは未処理のまま通す）。`PreviewKeyDown`
    (tunneling) はフォーカス管理の Esc 消費より前に届くため、旧 `CanvasControl.KeyDown` 時の Esc 不達問題は回避できる。
    実キーボードで F11/ボタン/Esc 復帰をユーザー確認済み（2026-06-20。`winui-keyinput-gotchas` どおり computer-use の
    合成 Esc では不発のことがある点に注意）。
  - **ステータスバーに全画面ボタン**（`PhotoStatusBar` 右端、`&#xE740;`）。`LeftColumn` 操作の `ToggleLeftPaneRequested`
    と同じパターンで **`ToggleFullScreenRequested` イベント**を発火し、`MainPage` 経由で `(App.Window as MainWindow)`
    `.ToggleFullScreen()` を呼ぶ（AppWindow は MainWindow が所有するため委譲）。全画面中はボタンも消えるが Esc/F11 で復帰可。
  - `Default` 復帰でシステム標準タイトルバー（`ExtendsContentIntoTitleBar=false`＝×フォーカスレース対策）はそのまま維持。
  - **`MainViewModel`・Core は非変更**。変更: `MainWindow.xaml.cs`、`Controls/PhotoStatusBar.xaml(.cs)`、`MainPage.xaml.cs`。
- **ステータスバーのメタ情報パネル（案A）レイアウト微修正 完了（2026-06-20）**: 旧「2段とも右寄せで密集」から
  **両段を `Grid`（`*` + `Auto`）の左右分離**へ変更。上段＝左:ファイル名・画像サイズ・ファイルサイズ／右:EXIFチップ、
  下段＝左:カメラ/レンズ／右:評価(★・旗・色ドット)・GPS・撮影日時。両段同構造なので**どちらが広くても**左グループの
  左端どうし・右グループの右端どうしが揃う（前回の「下段が最広」前提に依存しない）。右グループに左マージン 10 で
  詰まり過ぎ防止。`PhotoStatusBar.xaml` のみ変更、バインディング/配色/`PhotoItemViewModel`・コードビハインドは非変更。
  ユーザー画面確認済み（2026-06-20）。
- **パッケージング（unpackaged 自己完結 EXE 発行）完了（2026-06-20）**: SPEC §0 の配布形態 (A)＝MSIX なしの素の EXE
  （.NET / Windows App SDK 同梱）を発行できるよう publish 構成を組み込み、実発行＋起動確認済み。**MSIX 化はしない**
  （専用スキル `winui-packaging` は MSIX 前提なので今回は不使用。活きた知見は「WinUI/Win2D はトリミング不可」のみ）。
  - **設計の肝**: `WindowsPackageType=None`／`WindowsAppSDKSelfContained=true` は **csproj にグローバル設定しない**。
    開発は packaged（MSIX）で動かしているため、これらを全構成に置くと `dotnet run`／`BuildAndRun.ps1` が壊れる。
    → **publish 時だけ効く `.pubxml` 側に配置**し、csproj には全構成で安全な `PublishTrimmed=false` だけ入れた。
  - **csproj**: 旧 `PublishTrimmed`（Debug=false/それ以外=true）を **常時 `false`** へ（WinUI/Win2D はトリミング不可）。
    `PublishReadyToRun`（Release=true）は据え置き＝起動高速化（ユーザー選択。サイズ増は許容）。
  - **pubxml 2 系統**（`Properties/PublishProfiles/`）: 共通で `WindowsPackageType=None`＋`WindowsAppSDKSelfContained=true`
    ＋`SelfContained=true`＋`PublishReadyToRun=true`＋`PublishTrimmed=false`。
    - `win-x64.pubxml`（既存を更新／x86・arm64 も同様に更新）＝**フォルダ配布**（`PublishSingleFile=false`）。
    - `win-x64-singlefile.pubxml`（新規）＝**単一ファイル EXE**（`PublishSingleFile=true`＋
      `IncludeNativeLibrariesForSelfExtract=true`。出力は `…\publish-singlefile\`）。
  - **発行ヘルパー**: `src\PhotoQuickSelector.App\Publish.ps1`（`-SingleFile` で単一ファイル、`-Runtime` で RID 切替）。
    手動なら `dotnet publish -c Release -p:Platform=x64 -p:PublishProfile=win-x64[-singlefile]`。
  - **実発行で検証済み（win-x64）**: フォルダ版＝525 ファイル/273MB（`coreclr.dll`＋`Microsoft.ui.xaml.dll`＋
    `Microsoft.WindowsAppRuntime.dll`/Bootstrap＋`SQLite.Interop.dll`＋Win2D を同梱）。単一ファイル版＝**ルート直下 exe 1 個
    (290MB)＋pdb 2 個**（`resources.pri` も exe 内に埋め込まれた）。**両版とも unpackaged exe を直接起動して常駐を確認**
    （bootstrap が同梱ランタイムを解決＝未インストール環境前提でも起動可）。`dotnet test` 68 件緑。
  - 残: 配布前にユーザー環境での実機目視（実際にフォルダ読込→評価→保存まで）を推奨。pdb は配布から外したい場合
    Release で `DebugType=none` を検討（クラッシュ解析性とのトレードオフ）。**Core/App のコードは非変更**（ビルド構成のみ）。
- **先読みキャッシュのデバッグオーバーレイ 完了（2026-06-20）**: 先読みキャッシュ（`PreviewBitmapCache`）の挙動確認用に、
  キャッシュ中の画像ファイル名一覧をプレビュー右上に重畳表示。**`C` キーでトグル（初期非表示）**。案B（`I`／左上）と被らない右上に配置。
  - `PreviewBitmapCache` に `event Action? Changed`（`_cache`/`_inflight` 変化の4箇所＝`GetAsync` 読込開始／`LoadCoreAsync`
    完了 `finally`／`Trim` 削除あり時／`Clear` で発火）＋`SnapshotFileNames()`（デコード済みをファイル名で列挙、読込中は
    末尾に `(loading)` 付与）を追加。
  - `PreviewControl`: `ObservableCollection<string> CachedFileNames`（x:Bind 用）＋`RefreshCacheOverlay()`（オーバーレイ表示中の
    ときだけ `DispatcherQueue` 経由で再構築）。コンストラクタで `_cache.Changed` を購読。`Input.cs` に `C` トグル分岐。
    XAML に `CacheOverlay` Border（`Visibility=Collapsed`／`IsHitTestVisible=False`／等幅フォント）。
  - 移動（←/→）に合わせ `Prefetch`→`Trim` の結果をライブ反映。保持窓は前2/後1なので通常1〜4行。**Core 非変更**（App のみ）。
    `BUILD SUCCEEDED`／`dotnet test` 68 件緑。実機目視は次セッションで推奨。
- **サムネイルのメモリ削減 完了（2026-06-20）**: 1 万枚超フォルダでも破綻しないよう、サムネイルの保持方式を
  「デコード済み BitmapImage の全件常駐」から「**圧縮 JPEG バイトの全件常駐＋可視コンテナ分だけデコード**」へ変更。
  - **背景（実測）**: `GetThumbnailAsync(PicturesView,320)` が返すのは **JPEG（~30KB、448×307px）**。
    だが `BitmapImage.SetSourceAsync` で **~537KB/枚の非圧縮 BGRA8** に展開され全件常駐していた（1 万枚 ≈5.2GB で破綻）。
    犯人はソース形式でなく「全件をデコード済みのまま常駐」。応答性（＝全件常駐でスクロール時 I/O ゼロ）は維持しつつ
    常駐を小さい圧縮バイトに、重い非圧縮サーフェスは画面に見えるコンテナ分だけに。
  - **`PhotoItemViewModel`**: `[ObservableProperty] BitmapImage? Thumbnail` を廃止 → `byte[]? _thumbnailBytes`（常駐）。
    `EnsureThumbnailBytesAsync()`（シェル JPEG を `DataReader` でバイト読み出し・一度だけ）＋
    `CreateThumbnailImageAsync(int decodePixelWidth)`（バイト→`InMemoryRandomAccessStream`→`DecodePixelWidth` 指定の
    `BitmapImage`）を追加。
  - **`MainViewModel.LoadThumbnailsAsync`**: 先読みを `EnsureThumbnailBytesAsync()` に変更（BitmapImage 化しない＝軽量・高速）。
    世代トークンと中断ロジックは不変。
  - **可視分デコード**: 新規 `Controls/ThumbnailContainerLoader.cs`（当初は静的ヘルパ。後述のフリーズ対策でインスタンス化）。`ContainerContentChanging` で
    `InRecycleQueue`→`Image.Source=null`（解放）／実体化時→`img.Tag=vm` トークン付きで `CreateThumbnailImageAsync` を
    await し、Tag が一致する時だけ Source 設定（リサイクル先取り対策）。グリッド（`PhotoGridView`、幅 200）と
    フィルムストリップ（`PreviewControl`、幅 90）の両方から呼ぶ。両 XAML の `<Image>` は **Source バインドを外し
    `x:Name`（`ThumbImage`/`FilmThumbImage`）付与**、バッジの `x:Bind` は不変・`args.Handled` は立てない（phase 処理と共存）。
  - メモリ概算（1 万枚）: 常駐 ≈300MB（圧縮バイト）＋ 可視コンテナ数十枚分の小さな非圧縮サーフェスのみ。
  - **Core・選択同期・評価バッジ部品は非変更**。`BUILD SUCCEEDED`／`dotnet test` 68 件緑。
    実機（テストフォルダ 71 枚・x64 ビルド）で グリッド表示／スクロール再デコード／プレビュー入場／フィルムストリップ表示を
    目視確認済み（2026-06-20）。大量枚数フォルダでのメモリ頭打ちはユーザー確認推奨。
- **サムネイル・スクロール時の一瞬フリーズ対策 完了（2026-06-20）**: 上記メモリ削減後、「フォルダ選択直後にスクロール／
  スクロールバーで中央へジャンプすると一瞬固まる」現象を解消。原因＝オンデマンドデコード経路の **I/O＋デコードが UI スレッドに集中**
  （特に冷えたシェルキャッシュの高解像度デコードが重い）＋再デコードの無駄＋多重ロード。A〜D で対処:
  - **A. `ConfigureAwait(false)`**: `EnsureThumbnailBytesAsync` を `LoadBytesCoreAsync`（private）へ分離し、シェル呼び出し
    （`GetFileFromPathAsync`/`GetThumbnailAsync`/`reader.LoadAsync` を `.AsTask().ConfigureAwait(false)`）とバイトコピーを UI
    スレッドから外す。先読みループ（`MainViewModel.LoadThumbnailsAsync`）の await も `.ConfigureAwait(false)` でバックグラウンド化。
    **`CreateThumbnailImageAsync` は据え置き**（UI 起点・await 後 UI 復帰＝`BitmapImage`/`SetSourceAsync` は UI スレッドで実行）。
  - **B. in-flight 共有**: `PhotoItemViewModel._loadBytesTask` で同一写真の同時取得を 1 本に集約（`finally` で null＝失敗時再試行可）。
  - **C. デコード済み BitmapImage の容量上限つき LRU**: `ThumbnailContainerLoader` を **静的→インスタンス化**（デコード幅を保持）。
    実体化時に LRU ヒットなら即表示（再デコードなし）、ミスなら async デコード→LRU 登録。リサイクル時は `Image.Source=null`（参照解放）だが
    **LRU には残す**＝戻りスクロールで即表示。容量固定（グリッド150≈16MB／フィルム60≈1.3MB）でメモリは枚数非依存。
    `PhotoGridView`/`PreviewControl` が各 1 個生成し、`Photos` の `CollectionChanged(Reset)` 購読で `Clear()`（フォルダ切替で解放）。
  - **D. ビューポート近傍からの先読み**: グリッド実体化時に `MainViewModel.NotePrefetchAnchor(args.ItemIndex)` でアンカー通知。
    `LoadThumbnailsAsync` は 0..N 固定順をやめ、アンカーから外側（anchor,+1,-1,+2,-2…）へ未ロード項目を選ぶ（`NearestUnattempted`）。
    中央ジャンプでも近傍バイトが優先的に揃う。可視範囲自体はオンデマンド経路が従来どおり最優先。
  - **E.（重要・クラッシュ修正）シェルサムネイル抽出を直列化**: A で先読みループをバックグラウンド化した結果、
    `GetThumbnailAsync`（Windows シェル/WIC のサムネイル抽出）が **先読み（スレッドプール）とオンデマンド（UI）から同時に**
    呼ばれ、imaging 層が `Microsoft.UI.Xaml.dll` 経由で **fail-fast（`0xc000027b`/E_UNEXPECTED）してアプリが異常終了**した
    （4000 枚フォルダで「お気に入り選択→グリッド表示中」に再現）。`PhotoItemViewModel` に **静的 `SemaphoreSlim(1,1)` `_shellGate`** を
    追加し `LoadBytesCoreAsync` のシェル抽出を1本に直列化＝同時アクセスを防止。I/O はバックグラウンドのままなので UI は固まらない。
    （デコード `SetSourceAsync` は in-memory ストリームからで shell 非経由のため対象外。）
  - **Core・選択同期・評価バッジ・XAML は非変更**。`BUILD SUCCEEDED`／`dotnet test` 68 件緑。
    **検証経緯（重要な落とし穴）**: `winapp run` の **AppX ステージング（`…\win-x64\AppX`）はインクリメンタルビルドで更新されず**、
    `winapp` は古いステージ（さらに二重 `AppX\AppX`）を実行するため、当初の「実機確認」は古いバイナリを動かしていた。
    フォルダ自動読込＋ファイルログを仕込み **`dotnet run`** で再現させて初めて E の真因を特定・修正を確認
    （4000 枚で 325 デコード・無クラッシュ＝修正前は ~131 で異常終了）。実機目視の最終確認はユーザー推奨。
  - 教訓: 検証は `dotnet run`（毎回フレッシュにビルド）が確実。`winapp run` 経由は AppX ステージングの鮮度に注意。
- **フィルムストリップの高さ調節 完了（2026-06-20）**: プレビュー下部フィルムストリップの高さをスプリッターでドラッグ調節可能に。
  方式＝**GridSplitter ドラッグ＋セルの高さ追従＋永続化**（操作手段は右パネル/ナビの既存スプリッターと同パターン）。
  - **① GridSplitter**: `PreviewControl.xaml` 外側 Grid を 2 行→3 行化（`*` / スプリッター `Auto` / `FilmStripRow`
    可変・`MinHeight=80` `MaxHeight=220`）。上段とストリップの境界に横 `controls:GridSplitter`（既存と同設定
    `BasedOnAlignment`/`Auto`）。ListView は固定 `Height="118"` を撤去し行いっぱいに `Stretch`。
  - **② セル追従**: **`DataTemplate` 内からはコントロール側プロパティへ `x:Bind` できない**（テンプレ DataType＝
    `PhotoItemViewModel`）ため、観測可能な小クラス **`Controls/FilmStripMetrics.cs`（新規・`Edge`/`ItemWidth`）** を
    `UserControl.Resources` に置き、各セルは `{Binding Edge, Source={StaticResource FilmMetrics}}` で参照（Source 指定＝
    DataContext/namescope 非依存）。`FilmStrip.SizeChanged` で `Edge = ListView高 − 内訳分36px`（Padding8＋Margin4＋
    枠線6＋ファイル名行≒18）を再計算し全セル一括追従。デコード幅は `90→140` に引き上げ（拡大時のボケ対策。`MaxHeight=220`
    内なら概ね鮮明。メモリ増は数MB で無視可）。
  - **③ 永続化**: `AppSettings.FilmStripHeight`（既定 118）を追加。共有 `ViewModel.Settings` 経由で**左ペイン幅と同じく
    ウィンドウ終了時の `Settings.Save()` で一緒に保存**。高さ変更時に in-memory へ控え、ViewModel 注入時に
    `RestoreFilmStripHeight()`（行の `GridLength` を直接設定→`SizeChanged` で再計算）で復元。
  - **Core・選択同期・評価バッジ部品は非変更**。変更/新規: `AppSettings.cs`、`Controls/FilmStripMetrics.cs`（新規）、
    `Controls/PreviewControl.xaml(.cs)`。`BUILD SUCCEEDED`／`dotnet test` 68 件緑。実機目視（ドラッグでサムネイル連動拡縮・
    再起動で高さ復元・拡大時のボケ）はユーザー確認推奨。
- **右パネル幅／ナビゲーター高さ／メタ情報オーバーレイ表示状態の永続化 完了（`e5c10ab`・2026-06-20）**:
  `AppSettings.RightPanelWidth`（既定 260）／`NavigatorHeight`（既定 220）／`ShowInfoOverlay`（既定 true）を追加。
  フィルムストリップ高さと同方式＝変更時に in-memory へ控え、終了時 `Settings.Save()` で一括保存。ViewModel 注入時に
  `RestoreRightPanelLayout()` で復元（右パネル列幅とナビ行高の `GridLength` を直接設定）。変更: `AppSettings.cs`、
  `Controls/PreviewControl.xaml(.cs)`・`.Navigator.cs`、`ViewModels/MainViewModel.cs`。
  ※ドキュメント突き合わせ（2026-07-04）で記載漏れが判明し追記。
- **プレビューの DPI 考慮ズーム＋補間ポリシー＋倍率表示 完了（`c174232`＋`ae5f1a3`、`origin/main` プッシュ済み・2026-06-21）**:
  プレビューのズーム表示を DPI 対応化し、拡大率に応じて補間を切り替え、現在倍率をステータスバーへ表示。ユーザー実機確認済み。
  - **① DPI 考慮の等倍（`c174232`）**: 旧「等倍＝`Scale=1.0`（画像1px→1 DIP）」は高DPI（例150%）で画像1pxが1.5物理pxに
    拡大されていた。`PreviewViewport.DpiScale`（物理px/DIP＝`Dpi/96`）と `ActualScale`（`1.0/DpiScale`）を追加し、
    等倍を **`Scale=96/Dpi`** に変更＝**1 画像px = 1 物理px の真の等倍**に。`SetActualSize`/`ApplyMode` の `ActualSize` 分岐が
    対象（`Z`/`Shift+Z`/`Shift+Alt+→`/ルーペ100% が一括対応）。`PreviewControl.UpdateDpiScale()` が `MainCanvas.Dpi/96` を
    両ビューポートへ供給（`LoadCurrentAsync` 時＋DPI変更は既存 `CreateResources(DpiChanged)→ResetCacheAndReload` 経路で追従）。
  - **② 拡大率で補間切替（`c174232`＋`ae5f1a3`）**: `DrawImage` は dest/source 矩形オーバーロードに変更し補間を明示指定。
    判定は `PreviewViewport.DeviceScale`（=`Scale × DpiScale`＝物理px/画像px）。**`DeviceScale ≥ 1`（ピクセル等倍以上）＝
    `NearestNeighbor`（くっきり・補間なし）／`< 1`（縮小）＝`HighQualityCubic`（高品質縮小）**。補間選択は
    `PreviewControl.MainCanvas.cs` の `PickInterpolation(deviceScale)` に共通化、描画は `DrawScaledBitmap`（ジオメトリ明示
    本体＋ビューポート用オーバーロード）に集約。**メイン/ルーペ/ナビ 3 キャンバスすべて**で適用（ナビは縮小なので実質常に
    HighQualityCubic。Transform 方式をやめ `using System.Numerics;` を Loupe/Navigator から撤去）。ジオメトリは従来と同一。
    - **重要**: 既存の `Transform×DrawImage(bitmap)` は「画像1px→1 DIP」で動いていた（Fit が画面いっぱいに収まる関係＝裏付け）。
      dest 矩形は `DrawWidth/Height = SizeInPixels × Scale`（DIP）、source は `_bitmap.Bounds`（DPI 非依存全域）で同一ジオメトリ。
  - **③ 倍率をステータスバー表示（`c174232`）**: `MainViewModel.ZoomScale`（=`DeviceScale`）＋`ZoomText`（`{ZoomScale*100:0}%`）
    ＋`ZoomVisibility`（`IsPreviewMode` 連動＝プレビュー時のみ）。`PreviewControl.UpdateZoomDisplay()` を `InvalidateMain()`/
    `InvalidateAll()` 末尾で呼び、ズーム/パン/ロード/ナビ移動すべてに追従。**ピクセル等倍＝100%**（DPI 考慮済みなので高DPIでも正しい）。
    `PhotoStatusBar.xaml` の全画面ボタン直前に `Auto` 列を足して右端に表示。
  - 変更: `Controls/PreviewViewport.cs`、`Controls/PreviewControl.{xaml.cs,MainCanvas.cs,Loupe.cs,Navigator.cs}`、
    `ViewModels/MainViewModel.cs`、`Controls/PhotoStatusBar.xaml`。**Core は非変更**。`BUILD SUCCEEDED`／`dotnet test` 68 件緑。
    実機（高DPI モニタ）で等倍=100%・拡大くっきり・縮小高品質・倍率追従をユーザー確認済み。
- **写真切替時のズーム状態維持 完了（2026-06-21）**: プレビューで前後移動（`←`/`→`）したとき、従来は毎回
  フィット表示に戻っていたのを、**ズームモード/倍率/中心を引き継いで「ズーム表示のまま」**切り替わるようにした。
  切替前後で画像サイズが異なる場合の方針はユーザー選択（中心＝**相対位置で保持**／Custom 倍率＝**フィット比を保持**）。
  - **`PreviewViewport.SetImagePreservingView(imgW, imgH, canvasW, canvasH)` を新設**: 変更前に「キャンバス中心が指す
    画像上の相対位置(0..1)」と「フィット比(`Scale/FitScale`、Custom のみ)」をキャプチャ → 新サイズへ差し替え →
    倍率を再決定（**Fit=再フィット／ActualSize=DPI基準の100%維持／Custom=フィット比維持**）→ 中心を相対位置で復元 → `Clamp`。
    **`SetCanvasSize` を呼ぶと `ApplyMode→Center` が Custom でも再センタリングしてパンが消える罠**を避けるため、canvas
    サイズも引数で受け取り（相対中心/フィット比キャプチャ後に）一括更新する。
  - **動作**: 同サイズ画像（連写など最頻ケース）では相対=絶対でピクセル単位完全一致。サイズ違いでは同じ構図位置を中心に
    維持し、Custom は見える構図範囲が一定（100% は 100% 固定で見える範囲は変わる）。
  - **`PreviewControl.LoadCurrentAsync(bool preserveView)` 化**: `SelectedPhoto` 変更（写真切替）=`true`／`IsPreviewMode`
    入場=`false`（従来どおりフィットから）／`ResetCacheAndReload`（デバイス再生成・DPI変更）=`true`（ズーム維持）。
    保持パスは `_viewport.ImageWidth > 0` ガード付き（初回/空は安全にフィットへフォールバック）。
  - **テスト**: `PreviewViewport` は UI 非依存（`System.Numerics` のみ）なので、WinUI な App を参照せず **ソースをリンク参照**
    （`tests` csproj に `<Compile Include=... Link=...>`）して xUnit 化。`PreviewViewportTests.cs`（5 件）＝同サイズ完全一致／
    サイズ違いでのフィット比・相対中心維持／100%維持／Fit 再フィット／空からのフォールバック。
  - ルーペ・ナビは現状維持（ルーペは設計どおり毎回 100%＋AF 点へ）。変更: `Controls/PreviewViewport.cs`・
    `Controls/PreviewControl.xaml.cs`、`tests/…/PhotoQuickSelector.Core.Tests.csproj`＋`PreviewViewportTests.cs`（新規）。
    `BUILD SUCCEEDED`（App x64 Release・警告0）／`dotnet test` **73 件緑**（68→+5）。実機目視（前後移動でのズーム維持・
    サイズ違いフォルダでの中心追従）はユーザー確認推奨。
- **デザイン微修正（AF枠の表示場所＋左ペイン配置）完了（2026-06-21）**: ユーザー依頼の 3 点を挙動の根幹を変えずに調整。
  - **① AF フォーカス枠をメイン → ルーペへ**: メイン大画面（`MainCanvas`）は AF 枠が邪魔なので非表示にし、**右上ルーペ
    （`ZoomCanvas`）に表示**。`MainCanvas_Draw` から `DrawFocusFrame(ds)` を削除（グリッド線描画は残置）、`ZoomCanvas_Draw` に
    `DrawFocusFrame(ds, _zoomViewport.ImageToCanvas, 2f)` を追加。汎用化済みの `DrawFocusFrame(ds, toCanvas, thickness)` を
    ルーペ用ビューポートの `ImageToCanvas` で呼ぶだけ（生センサー座標→Orientation→キャンバスの既存パイプラインを流用）。
    **ナビゲーター（`NavCanvas`）の緑枠は従来どおり**。変更: `Controls/PreviewControl.{MainCanvas.cs,Loupe.cs}`。
  - **② 左ペインの「読み込み」「更新」ボタンを最下部へ**: `FolderNavigationView.xaml` の `LeftPane` Grid を
    お気に入り(Row0 Auto)／最近(Row1 Auto)／**TreeView(Row2 `*`)**／**ボタン StackPanel(Row3 Auto)** に組み替え。
    TreeView 行を `*` にして残り高さを占めさせ、ボタンをペイン最下部に固定（最初の組み替えで `Grid.Row` だけ付け替え
    `RowDefinitions` の `*` 位置を直し忘れ、ボタンが星サイズ行の上端で「浮く」不具合を出したのを修正＝**行の高さ定義も併せて入替える**）。
  - **③ お気に入り/最近を空でも常時表示**: 2 つの `Expander` から `Visibility="{x:Bind ViewModel.*Visibility}"` を削除（既定 Visible）。
    未使用化した `MainViewModel.FavoritesVisibility`/`RecentVisibility`（宣言＋`RebuildShortcuts` の自分への `OnPropertyChanged`）も削除
    （`using Microsoft.UI.Xaml;` は他プロパティで使用中のため残置）。
  - **Core・コードビハインド（`FolderNavigationView.xaml.cs`）は非変更**。`BUILD SUCCEEDED`。実機目視はユーザー確認推奨。
- **フィルムストリップの選択強調 完了（2026-06-21）**: 「選択中の画像が分かりづらい」を解消。フィルムストリップは
  既定の `ListViewItem` 選択ビジュアル任せで、画像がセル全面を覆い細いアクセント縁しか見えなかった。**案1（非選択を
  ディミング）＋案2（アクセント外枠）の併用**で対処（モックアップで 4 案比較しユーザー選択）。
  - **`PhotoItemViewModel`**: `[ObservableProperty] bool IsSelected`＋派生 `SelectionOpacity`（選択 1.0／非選択 0.9＝
    濃いめ。ユーザー調整で 0.45→0.7→0.9 と上げ最終 0.9）と `SelectionFrameOpacity`（選択 1.0／非選択 0.0）。
    `[NotifyPropertyChangedFor]` で派生を通知。
  - **`MainViewModel`**: `OnSelectedPhotoChanged(old,new)`（2 引数 partial）で旧セル `IsSelected=false`／新セル `=true`。
    既存 1 引数版（`PhotoInfoVisibility` 通知）はそのまま残置（両方呼ばれる）。
  - **テンプレート**（`PreviewControl.xaml` フィルムストリップ）: 案1＝ルート `StackPanel.Opacity` を `SelectionOpacity` に束縛。
    案2＝セル本体（カラーラベル枠＋画像＋バッジ）を `Grid` で包み、その上に**子を持たない空のアクセントリング** `Border`
    （`CornerRadius=6`/`BorderThickness=3`/枠色 `#FF333333`＝濃いグレー固定/`IsHitTestVisible=False`）を重ね、`Opacity` を
    `SelectionFrameOpacity` に束縛。カラーラベル枠に `Margin=3` を付け最外周をアクセント枠ぶん空ける（同心配置）。
    **枠は常設で Opacity だけ切替＝レイアウト無変動**。枠色は当初 `AccentFillColorDefaultBrush`（青）だったが
    **カラーラベルの青と紛らわしい**ためユーザー要望で濃いグレー固定へ変更。
    - **重要な落とし穴（実機で発覚→修正）**: 当初はアクセント枠で画像コンテンツを**内包**し `Opacity` を切替えたが、
      WinUI の `Opacity` は子へ乗算されるため非選択時（`Opacity=0`）に**画像ごと不可視**になった。→ 枠を「内包」から
      「上に重ねる空リング」へ変更して解決（枠だけ消え画像は残る）。
  - **寸法調整**: アクセント枠ぶん上下左右 各 3px を反映。`FilmStripMetrics.ItemWidth` を `Edge+4`→`Edge+12`、
    `PreviewControl.FilmChromeHeight` を `36`→`42`。
  - **グリッド（`PhotoGridView`）は非変更**（既定の選択ビジュアルで十分に見えるため。`IsSelected` は VM に立つが
    グリッドテンプレートは参照しない）。**Core は非変更**。`BUILD SUCCEEDED`／`dotnet test` 73 件緑。実機目視はユーザー確認推奨。
- **お気に入り/最近クリックでツリー展開＆選択 完了（2026-06-21）**: 左ペインの「お気に入り」「最近開いたフォルダ」を
  クリックしたとき、従来は写真読込のみでツリー側は無反応だったのを、**フォルダツリーを同パスまで展開して末端ノードを
  選択状態にする**よう拡張。`FolderNavigationView.xaml.cs` のみ変更（Core・XAML・ViewModel 非変更）。
  - **`Shortcut_ItemClick`**: 存在チェック→`LoadFolderAsync`（写真読込）の後に新規 `ExpandAndSelectFolderAsync(path)` を呼ぶ。
    お気に入り/最近は同一ハンドラ経由なので一括適用。消えたフォルダは従来どおり `RemoveRecentFolder` で除去し早期 return。
  - **`ExpandAndSelectFolderAsync`**（新規）: WinUI の `TreeView` には「データ項目を指定して展開/選択する」API が無いため
    **ドライブルートから目的パスへ 1 階層ずつ手動ウォーク**＝「コンテナ realize 待ち→`LoadChildren()`(差分同期)→
    `IsExpanded=true`→`UpdateLayout()`→次の子を探索」を繰り返し、末端で `IsSelected=true`＋`FolderTree.SelectedItem` 同期＋
    `StartBringIntoView()`。**既存の差分同期 `LoadChildren()` をそのまま使う**ので「TreeView は Clear→全件追加で壊れる」罠と
    展開状態の喪失を回避。`SelectedItem` 同期で後続の「読み込み」「更新」「F5」も選択ノードへ正しく作用。
  - **`RealizeContainerAsync`**（新規）: 仮想化で展開直後は `ContainerFromItem` が `null` のことがあるため、`null` の間
    `UpdateLayout()`＋`Task.Delay(16)`（1 フレーム待ち）でリトライ（最大 20 回）。
  - **`PathEquals`**（新規）: 末尾 `\`（ドライブルート `D:\` 等）を `TrimEnd('\\')` で無視した大小無視のパス比較。
  - `BUILD SUCCEEDED`。実機でお気に入り/最近クリック→ツリー展開・選択・スクロールをユーザー確認済み（2026-06-21）。
- **セッション復元（終了時の状態を再起動時に復元）完了（2026-06-21）**: アプリ終了時に「開いていたフォルダ／選択ファイル／
  表示モード（プレビュー⇄グリッド）／フィルタ条件」を保存し、再起動時に復元する。フォルダ/ファイル消失等のイレギュラーは
  防御的に処理。**Core は非変更**（App のみ）。
  - **保存先**: 既存 `AppSettings`（`settings.json`）に `SessionState`（`FolderPath`／`SelectedFileName`＝**ファイル名のみ**＝
    リネーム/移動に頑健／`IsPreviewMode`／`Filter`）＋`FilterState`（`FilterViewModel` のスナップショット）を追加。
    source-gen コンテキスト（`AppSettingsJsonContext`）に両型を登録（トリミング安全）。
  - **保存タイミング**: `MainWindow_Closed` で `ViewModel.CaptureSession()`（VM 状態→`Settings.LastSession`）→`SaveLeftPaneLayout()`
    （末尾の `Settings.Save()` で左ペイン状態と一括保存）。**保存 I/O は増やさない**（既存の終了時 1 回に相乗り）。
  - **復元**（`MainPage.RestoreSessionAsync`／`MainPage_Loaded` から fire-and-forget）: ① `FolderPath` 空→何もしない
    ② `Directory.Exists` 偽→中止＋「前回のフォルダが見つかりません」表示（最近一覧は残す）③ `Filter.ApplyState()` で
    **フィルタを先に**復元（`LoadFolderAsync` 末尾の `ApplyFilter` が反映するため選択復元より前）④ `LoadFolderAsync(folder,
    selectedFile, previewMode)` ⑤ `LeftNav.ExpandAndSelectFolderAsync(folder)` でツリーも展開＆選択（お気に入りクリックと同経路。
    そのため同メソッドを private→**public** 化）。
  - **`LoadFolderAsync` をオプション引数化**（`restoreSelectedFile`/`restorePreviewMode`、ともに既定 null）: **既定値は現行挙動と
    完全一致**（通常のフォルダクリック＝末尾で必ず `EnterPreview()`）。復元時のみ、保存ファイルが**絞込結果 `Photos` 内に在れば**
    選択し、表示モードを復元。見つからない/絞り込みで外れたら「プレビュー指定＝先頭選択／グリッド指定＝先頭 or 未選択」へフォールバック。
  - **イレギュラー対応**: フォルダ消失・ファイル消失/リネーム・フィルタ除外・JPEG 0 枚・I/O 例外（`LoadFolderAsync` の `try/catch`）・
    `settings.json` 破損（既存 `Load()` が既定値復帰）のいずれもクラッシュせず安全に既定状態へ。
  - 変更/新規: `AppSettings.cs`（`SessionState`/`FilterState` 追加）、`ViewModels/FilterViewModel.cs`（`CaptureState`/`ApplyState`）、
    `ViewModels/MainViewModel.cs`（`CaptureSession`＋`LoadFolderAsync` 引数）、`MainPage.xaml.cs`（`RestoreSessionAsync`）、
    `MainWindow.xaml.cs`（`Closed` で `CaptureSession`）、`Controls/FolderNavigationView.xaml.cs`（`ExpandAndSelectFolderAsync` を public 化）。
    `BUILD SUCCEEDED`（警告0）／`dotnet test` 73 件緑。実機目視（開く→選択→プレビュー→終了→再起動で復元）はユーザー確認推奨。
- **フィルタで選択写真が外れた時の挙動改善 完了（2026-06-21）**: 選択中の写真がフィルタ条件変更で絞込結果から外れたときの
  挙動を、従来の「選択を完全に忘れる」から「**最後に実際に選んでいた写真をアンカーとして覚えておく**」方式へ変更。
  3 挙動を実現（`MainViewModel.cs` のみ変更・Core/XAML/テスト非変更）。
  - **新フィールド `_selectionAnchor`**: `SelectedPhoto` とは別に「最後に実際に選んでいた写真」を保持。
    `OnSelectedPhotoChanged(old,new)` で **非 null 選択時のみ更新**（`SelectedPhoto=null`＝外れた時は保持）。
    `LoadFolderAsync` の `AllPhotos.Clear()` で `null` リセット（別フォルダの古い写真を基準に残さない）。
  - **挙動①（外れたら空表示・維持）**: `ApplyFilter` はアンカーが結果に残れば選択復元・外れれば `SelectedPhoto=null`
    （プレビューは空）だが**アンカーは保持**。
  - **挙動②（再表示で元ファイル選択）**: `ApplyFilter` の選択判定を旧 `previous`（＝今の選択）から `_selectionAnchor` へ置換。
    再びフィルタを広げてアンカーが結果に入れば自動再選択。
  - **挙動③（外れ中の前後移動は元位置基準）**: `MoveBy` を 2 分岐化。`SelectedPhoto != null` は従来どおり絞込ビュー内移動。
    `null`（外れ中）はアンカーの `AllPhotos` 位置から直近の前/後ろの**可視（`Filter.Model.Matches`）写真**を選ぶ。
  - 設計判断: アンカー＝「最後に**表示できていた**選択」。外れて空の後に ←/→ で見える別写真へ動けば、その時点でアンカーは
    そちらへ更新される（元写真へは戻らない＝ブラウズ中の選択ワープを防止）。挙動②③はいずれも「まだ空のまま」の状態で作用。
  - 変更: `ViewModels/MainViewModel.cs`（5 箇所）。`BUILD SUCCEEDED`（警告0）／`dotnet test` 73 件緑。
    `MainViewModel` は WinUI 依存（`Visibility`）で Core.Tests から参照不可のため検証は実機目視（外れる→空／広げて復帰→元選択／
    空のまま ←/→ で前後の可視写真へ）をユーザー確認推奨。

- **未評価画像の Reject フォルダ移動 完了（2026-06-21）**: 「採用フラグなし＆未評価」の画像を、フォルダ配下の
  `Reject` サブフォルダへ bat 経由で移動する機能。フィルタのフライアウト内に専用ボタン「未評価をRejectフォルダへ移動…」。
  - **Core（純関数・テスト付き）**: 新規 `Core/RejectMove.cs`＝`IsRejectTarget(eval)`（`FlagRating<=0 && Rating==0`＝
    拒否フラグ付き未評価も対象）／`BuildBatch(...)`（`@echo off`＋`chcp 65001`＋`@rem` ヘッダ＋既存と同じ
    `move %FROMDIR%\<拡張子なし名>* %TODIR%`＝RAW+JPEG まとめ移動。bat を Reject 直下に置く前提で FROMDIR=../TODIR=.）。
    テスト 6 件追加＝**計 79 件緑**。
  - **App（`MainViewModel`）**: `GetRejectTargets()`（**フィルタ非依存で `AllPhotos` 全件**から抽出）／`RejectFolderPath`／
    `FindRejectCollisions()`（Reject に同名=拡張子込みが既存なら列挙）／`BuildRejectBatchText()`／`RunRejectBatchAsync()`
    （`Directory.CreateDirectory`＝冪等→ `Reject_yyyyMMddHHmmss.bat` を UTF-8(BOM なし)保存→
    `cmd /c ""bat" > "Reject_….log" 2>&1"`（WorkingDirectory=Reject）で実行＋`WaitForExitAsync`→ フォルダ再読込で移動済みを一覧から除去）。
  - **UI フロー（`Controls/FilterBar.xaml(.cs)`）**: ボタン→ 対象抽出（0 件なら通知）→ **同名衝突チェック（あれば中断ダイアログ）**→
    bat 内容を `ContentDialog`（読み取り専用 `TextBox`・Consolas・実行/キャンセル）で確認 → OK で `RunRejectBatchAsync` → 完了ダイアログ
    （ログパス表示）。`ContentDialog` の `XamlRoot` はコントロールのものを使用。
  - 設計判断（ユーザー確定）: 対象＝拒否フラグ付き未評価も含む／RAW+JPEG はワイルドカードでまとめ移動／フィルタ非依存で全件／
    入口はフィルタのフライアウト内。**パッケージ版でも子 cmd はサンドボックス外で動くのでファイル移動可**。
  - **確認ダイアログ TextBox の落とし穴 2 件（実機確認で修正）**: ①初期化子は記述順に代入されるため
    `AcceptsReturn=true` を `Text` より**先に**設定する（`AcceptsReturn=false` のまま改行入り文字列を代入すると 1 行目で切り捨て。
    メモリ `winui-textbox-acceptsreturn-order`）。②`ContentDialog` は既定で `ContentDialogMaxWidth`(≈548) に幅をクランプするため、
    内容 `StackPanel.Width=700` ＋ `dialog.Resources["ContentDialogMaxWidth"]=760.0` の**2点セット**で広げる。
  - 変更/新規: `Core/RejectMove.cs`（新規）・`tests/…/RejectMoveTests.cs`（新規）、`ViewModels/MainViewModel.cs`、
    `Controls/FilterBar.xaml(.cs)`。`BUILD SUCCEEDED`（App x64 Release・警告0）／`dotnet test` 79 件緑。
    実機で対象抽出・bat 全行表示・移動実行・完了通知をユーザー確認済み（2026-06-21）。

- **リネームしてコピー 完了（2026-06-21）**: 絞込結果（フィルタ後の表示中写真）を、ファイル名を置換ルールで
  リネームしながら任意の宛先フォルダへ bat 経由でコピーする機能。フィルタのフライアウト内に専用ボタン「リネームしてコピー…」。
  Reject 移動と同じ骨格（メモリ生成→内容確認→宛先で保存・実行・ログ格納）。
  - **Core（純関数・テスト付き）**: 新規 `Core/CopyRename.cs`＝`ResolveName`（テンプレート展開・不正文字は `_` にサニタイズ）／
    `ResolveAll`（全件展開＋**重複名検出**＝リネーム特有の同名衝突）／`BuildBatch`（RAW+JPEG は
    `for %%F in ("%FROMDIR%\<元名>.*") do copy "%%F" "%TODIR%\<新名>%%~xF"` で**まとめてコピー＋拡張子保持**。
    上書き＝`copy /y`／無視＝`if not exist … copy`）。テスト 8 件追加＝**計 87 件緑**。
  - **プレースホルダ（ユーザー指定: 年月日＝大文字 / 時分秒＝小文字。月 `MM` と分 `mm` が大小で区別）**:
    `{folder}` `{name}` `{ext}` ／ `{YYYY}` `{YY}` `{MM}` `{DD}` ／ `{hh}`(24h) `{mm}` `{ss}` ／ `{seq}` `{seq:000}`（連番・桁数はゼロの個数）。
    拡張子はテンプレートに含めず自動付与。撮影日時(`Meta.TakenDateTimeOffset`)が無い写真は日時トークンが空。
  - **App（`MainViewModel`）**: `GetCopyTargets()`（**=絞込結果 `Photos`**）／`PreviewCopyNames`／`FindCopyNameDuplicates`／
    `BuildCopyRenameBatchText`／`RunCopyRenameBatchAsync`（宛先を `Directory.CreateDirectory`＝冪等→
    `CopyRename_yyyyMMddHHmmss.bat` を UTF-8(BOM なし)保存→ `cmd /c` で実行・`CopyRename_….log` へリダイレクト。
    WorkingDirectory=宛先。コピーは元フォルダ不変なので再読込しない）。
  - **UI（`Controls/CopyRenameDialog.xaml(.cs)` 新規＝モーダル）**: コピー先入力＋**参照ボタン（`FolderPicker`＋`App.WindowHandle`）**／
    テンプレート入力＋プレースホルダ挿入ボタン（キャレット位置へ挿入）／上書き⇔無視 `RadioButtons`／**リネーム結果ライブプレビュー**／
    重複警告 `InfoBar`。`FilterBar.CopyRename_Click` が 入力→bat 確認ダイアログ（Reject と共用化した `ConfirmBatchAsync(title,intro,bat)`）→
    実行→完了通知 を駆動。コピー先初期値＝表示中フォルダ（`CurrentFolder`）。
  - **誤実行防止（ユーザー要望）**: コピー先がコピー元（表示中フォルダ）と同じ／未入力／名前重複のいずれかなら
    **「バッチを生成」ボタンを `IsPrimaryButtonEnabled=false` で無効化**＋InfoBar で理由表示（優先度: 未入力＞同一＞重複）。
    パス比較は `Path.GetFullPath`＋`TrimEndingDirectorySeparator` の大小無視（不正パスは例外握りつぶし）。`PrimaryButtonClick` でも同条件を保険ガード。
  - **バッチ先頭の `@echo off` を廃止（ユーザー要望・`RejectMove` にも適用）**: 各 copy/move コマンドを実行ログにエコーさせるため。
    先頭は `chcp 65001 > nul` から。`ClipboardExport.BuildMoveBatch` は元々 `@echo off` 無しのため変更なし。テストの行構成も更新。
  - 設計判断（ユーザー確定）: 対象＝絞込結果（フィルタ後）／RAW+JPEG はまとめてコピー／プレースホルダの年月日＝大文字・時分秒＝小文字。
  - 変更/新規: `Core/CopyRename.cs`（新規）・`tests/…/CopyRenameTests.cs`（新規）、`Core/RejectMove.cs`（`@echo off` 撤去）、
    `ViewModels/MainViewModel.cs`、`Controls/CopyRenameDialog.xaml(.cs)`（新規）、`Controls/FilterBar.xaml(.cs)`。
    `BUILD SUCCEEDED`（App x64・警告0）／`dotnet test` 87 件緑。実機で参照・プレビュー・同一フォルダ時のボタン無効・コピー実行をユーザー確認済み（2026-06-21）。
  - **テンプレート永続化（追記・2026-06-21）**: 「リネームしてコピー」のファイル名テンプレートを `AppSettings.CopyRenameTemplate`
    （**既定 `{name}`**）に保存し、次回起動以降の初期値に。保存は **バッチ生成（OK）時に `Settings.Save()`**（最近/お気に入りと同じ
    変更時保存。キャンセルした打ちかけは記録しない）。`CopyRenameDialog.Configure` が開く際に保存値を `TemplateBox` へ復元
    （非空のときのみ）。string プロパティ追加のみで source-gen コンテキストは変更不要。変更: `AppSettings.cs`、
    `Controls/CopyRenameDialog.xaml(.cs)`（XAML 既定値も `{name}`）、`Controls/FilterBar.xaml.cs`。
  - **参照ボタンをコピー先フォルダで開く（追記・2026-06-21）**: 「参照…」のフォルダ選択を、現在のコピー先
    （既定＝表示中フォルダ）を初期表示して開くようにした。**WinRT `FolderPicker` は任意パスから開けない**
    （`SuggestedStartLocation` は `PickerLocationId` 列挙のみ＝WinUI 3 既知制約）ため、Win32 `IFileOpenDialog`
    （`FOS_PICKFOLDERS`＋`SetFolder`）を `[ComImport]` 相互運用で呼ぶ新規ヘルパー `Controls/NativeFolderPicker.cs` を導入。
    `SHCreateItemFromParsingName` で開始パスの `IShellItem` 化 → `SetFolder` → `Show` → `GetResult`/`GetDisplayName(FILESYSPATH)`。
    開始パスが無ければ**直近の存在する親へ遡り**、キャンセル/例外は握りつぶして null。発行時もトリミング無効なので従来型
    `[ComImport]` で安全。`Browse_Click` は WinRT → ネイティブに差し替え（同期化、`using Windows.Storage.Pickers;` 撤去）。
    変更/新規: `Controls/NativeFolderPicker.cs`（新規）、`Controls/CopyRenameDialog.xaml.cs`。Core/ViewModel/XAML 非変更。
  - **コピー先のセッション記憶＋同名時の既定変更（追記・2026-06-27）**: ①「リネームしてコピー」のコピー先フォルダを
    **アプリ起動中だけ記憶**（前回指定先を次回ダイアログの初期値に。**永続化しない**＝再起動後は表示中フォルダに戻る。
    テンプレート永続化とは別扱い）。`MainViewModel.LastCopyDestination`（インメモリ・非永続）を追加し、`CopyRenameDialog.Configure`
    の初期値を `LastCopyDestination ?? CurrentFolder` に。記憶の更新は **OK（バッチ生成）押下時のみ**（`FilterBar.CopyRename_Click`
    でテンプレート保存と同箇所。キャンセルは記憶しない）。②同名存在時の既定を「上書きする」→**「コピーしない（無視）」**へ
    （`CopyRenameDialog.xaml` の `PolicyButtons` を `SelectedIndex="0"`→`"1"`。`Policy` ゲッターは既に index 1→`Skip` 対応済みで
    コード変更不要）。変更: `ViewModels/MainViewModel.cs`、`Controls/CopyRenameDialog.xaml(.cs)`、`Controls/FilterBar.xaml.cs`。
    **Core 非変更**。`BUILD SUCCEEDED`（x64 Release・警告0）。ユーザー画面確認済み（2026-06-27）。
  - **モーダル表示中にグローバルキーが効かない問題を修正（追記・2026-06-21）**: ダイアログ（`ContentDialog`）の TextBox に
    `z`・`←`・`→`・数字等を入力しようとしても、ルート集約 `RootGrid_PreviewKeyDown`(tunneling) が先取りして
    「ズーム/前後移動/評価」として消費し入力できなかった。`RootGrid_PreviewKeyDown` 冒頭に
    **`VisualTreeHelper.GetOpenPopupsForXamlRoot(RootGrid.XamlRoot).Count > 0` なら早期 return**（ポップアップ
    ＝ダイアログ/フライアウト/メニュー/ComboBox ドロップダウン等が開いている間はグローバルキー集約を停止）を追加。
    呼び出し側の変更不要で全ダイアログに自動適用。プレビューのオーバーレイ（`I`/`G`/`C`）はポップアップでないため非影響。
    フライアウト/メニュー中もナビ/ズームが止まるが、その時はフォーカスがポップアップ側にあるのでむしろ正しい挙動。
    変更: `MainWindow.xaml.cs` のみ。詳細はメモリ `winui-keyinput-gotchas`。

- **プレビュー操作の挙動変更3点 完了（2026-06-23）**: ユーザー依頼でプレビューのキー/フォーカス挙動を調整。
  - **① ズーム位置の記憶**: 「ズーム→スクロール→`Z`で全体表示→再び`Z`でズーム」したとき、以前と同じズーム位置
    （倍率・中心）に戻るようにした。`PreviewViewport` に直近ズーム状態（倍率/モード/**キャンバス中心が指す相対位置
    0..1**）を保持するフィールドを追加。`SetFit()` でフィットへ戻る直前に `RememberCurrentView()` で記憶、
    `ToggleZoom()` のフィット→ズーム側で `RestoreZoomView()` で復元（記憶が無ければ従来どおり等倍）。新画像/入場の
    `SetImage()` で記憶をリセット。相対位置保持なので同サイズ連写では完全一致、サイズ違いでも同じ構図位置が中心。
  - **② ←/→ 移動でフィルムストリップへフォーカス移動**: 前後移動後に `FocusFilmStripSelected()` で選択セルのコンテナへ
    `Focus()`。これで `PageUp`/`PageDown`/`Home`/`End` 等の ListView キー操作がフィルムストリップ上で効く。←/→自体は
    従来どおりルート集約 `RootGrid_PreviewKeyDown`(tunneling) が先取りするのでフォーカス位置によらず誤動作しない。
    コンテナ未実体化時は ListView 自体へフォーカス。
  - **③ Esc でプレビューを抜けない**: コンストラクタの `Esc→ExitPreview` `KeyboardAccelerator` を撤去。Esc を押しても
    プレビュー表示のまま維持。プレビュー終了は従来どおりダブルクリック（`MainCanvas_DoubleTapped`、SPEC §2）。
    全画面中の Esc（通常表示へ復帰）は `MainWindow` 側で処理するため非影響。
  - 変更: `Controls/PreviewViewport.cs`・`Controls/PreviewControl.xaml.cs`・`Controls/PreviewControl.Input.cs`。
    **Core・XAML は非変更**。`BUILD SUCCEEDED`（警告0）／`dotnet test` 87 件緑。実機目視（記憶ズーム復元・
    フォーカス移動後の PageUp/PageDown・Esc 無反応）はユーザー確認推奨。

- **Reject 移動でキャッシュ中ファイルが「使用中」で移動失敗する不具合 修正完了（2026-06-23）**: 「未評価を Reject フォルダへ移動」
  実行時に**一部のファイルだけ移動に失敗**する不具合を解消。真因＝**プレビュー先読みキャッシュ（`PreviewBitmapCache`）が
  対象ファイルをロックしていた**こと。
  - **真因**: `PreviewBitmapCache` は `CanvasBitmap.LoadAsync(_device, path)`（**ファイルパス指定オーバーロード**）でデコード
    していた。Win2D のこの方式は、生成された `CanvasBitmap` が生きている間ずっと元ファイルを開いたままロックする（[Win2D #291]）。
    Reject 移動はアプリ起動中に子 `cmd` の `move` で実行するため、**自プロセスがキャッシュ経由でロック中のファイルだけ**
    `move` に失敗する。キャッシュ保持窓は「表示中±前後数枚」なので、移動対象（フォルダ全件の未評価）のうち**直前に
    プレビューで見た付近の数枚だけ失敗**＝「一部のファイルだけ失敗」の症状と一致。サムネイル（`PhotoItemViewModel`）は
    全バイトを読み切り `using` で閉じておりロック源ではない。
  - **修正（ストリーム経由デコードへ＝根治）**: `PreviewBitmapCache.LoadCoreAsync` を **`File.ReadAllBytesAsync` で読み切り →
    `InMemoryRandomAccessStream` → `CanvasBitmap.LoadAsync(_device, stream)`** に変更。元ファイルのハンドルは読み切った時点で
    閉じるため、`CanvasBitmap` がキャッシュに残っていてもファイルはロックされない。**EXIF Orientation は WIC のデコード段で
    適用されるためストリーム経由でも自動回転は維持**（「二重回転しない」前提は不変）。デコード中だけ一時的に `byte[]`（JPEG 生
    サイズ）が増えるが直後に GC 対象（保持窓は数枚なので影響軽微）。
  - 設計の波及（将来）: この根治により、Copy/リネーム/エクスプローラ操作など他のファイル操作でも同種のロック問題を予防できる。
  - **Core・XAML・他コードビハインドは非変更**。変更: `Controls/PreviewBitmapCache.cs` のみ（usings に
    `System.Runtime.InteropServices.WindowsRuntime`／`Windows.Storage.Streams` を追加）。`BUILD SUCCEEDED`（App x64 Release・
    警告0）／`dotnet test` 87 件緑。実機で「プレビューで数枚見た直後に未評価を Reject へ移動→全件移動成功」をユーザー確認済み（2026-06-23）。

  [Win2D #291]: https://github.com/Microsoft/Win2D/issues/291

- **プレビューのイマーシブ表示（メイン全域表示）完了（2026-06-25）**: プレビュー中に **`F` キー**（修飾子なし）で
  右パネル（ルーペ＋ナビ）とフィルムストリップを畳み、`MainCanvas` を `PreviewControl` の全域へ広げるトグルを追加。
  既存の **F11 全画面＋左ペイン非表示**と組み合わせると画像が画面一杯に表示される（ユーザー選択＝3 つは独立操作で合成）。
  - **トグル本体（新規 `Controls/PreviewControl.Immersive.cs`／partial）**: `ToggleImmersive()` が `RightPanelColumn` 列と
    `FilmStripRow` 行の `Width/Height`＋`MinWidth/MinHeight` を退避→0 にし、右パネル/フィルムストリップと**両スプリッターを
    `Collapsed`**。戻す時は退避値へ復元＋`Visible`。`MinWidth/MinHeight` も 0 にしないと `Width/Height=0` が効かない点に注意。
    `MainCanvas` の再フィットは既存 `MainCanvas_SizeChanged`→`SetCanvasSize` が自動処理（追加描画ロジック不要）。
  - **キー**: `Controls/PreviewControl.Input.cs` に `KeyboardModifiers.None && F` 分岐。`Alt+F`/`Ctrl+Alt+F`（フォーカス点へ
    スクロール）は上流で処理済みなので競合なし。ルート集約経路（`RootGrid_PreviewKeyDown→HandleGlobalKeyDown→
    Preview.HandleKeyDown`）に乗るので配線追加不要。
  - **XAML**: 畳む対象の 2 本のスプリッターに `x:Name`（`RightSplitter`/`FilmSplitter`）を付与しただけ。
  - 決め事（ユーザー確定）: トグルキー＝`F`／**永続化なし**（再起動で通常の 3 パネルに戻る・セッション中は状態維持）／
    `PreviewControl` 内のみ（F11・左ペイン非表示は従来どおり独立）。
  - **Core・MainPage・MainWindow は非変更**。変更/新規: `Controls/PreviewControl.xaml`・`.Input.cs`・`.Immersive.cs`（新規）。
    `BUILD SUCCEEDED`。実機目視（`F` で畳む/戻す・全画面＋左ペイン非表示との合成で画面一杯・畳んだ状態でのズーム/パン/前後移動）は
    ユーザー確認推奨。

- **プレビューの完全全画面モード（Shift+F）完了（2026-06-25）**: 1 操作で **ウィンドウ全画面＋左ペイン非表示
  （スプリッター含む）＋ステータスバー非表示＋イマーシブ（右パネル/フィルム畳む）＋右ペイン余白0** を切り替え、
  `MainCanvas` を物理画面いっぱいに表示する統合モード。`F`（イマーシブ単体）とは別。
  - **挙動（ユーザー確定）**: グリッド表示中に `Shift+F` → **プレビューに入ってから**完全全画面化／ステータスバー
    （件数・メタ情報・倍率）も隠す／解除は **`Shift+F` または `Esc`**。**入る前の状態をスナップショットして正確に復元**
    （元がグリッドならグリッドへ戻す）。
  - **コーディネータ（`MainPage.ToggleFullImageMode()`）**: 複数コンポーネントにまたがるため `MainPage` に集約。
    入りで退避（`LeftColumn` 幅/MinWidth・`StatusBar.Visibility`・`RightPaneRoot.Padding`・直前の `IsPreviewMode`）→
    `EnterPreview()`（グリッド時）→ 左ペイン幅0＋`LeftSplitter` 隠す／`StatusBar` 隠す／余白0／`Preview.SetImmersive(true)`／
    `MainWindow.SetFullScreen(true)`。出は逆順で復元（元がグリッドなら `ExitPreview()`）。`IsFullImageMode` を公開。
  - **イマーシブ共用（`PreviewControl`）**: `ToggleImmersive()` を **冪等な `public SetImmersive(bool)`** へリファクタ。
    `F` キーはトグル、完全全画面は強制 ON/OFF で本メソッドを共用（スナップショット方式で `F`/`F11`/左ペイン手動トグルと衝突しない）。
  - **キー配線（`MainWindow.RootGrid_PreviewKeyDown`）**: `Shift+F`（`KeyboardModifiers.Shift && F`）→ `MainPage.ToggleFullImageMode()`
    （F11 と同じフォーカス非依存の集約点なのでグリッド時も拾える）。`Esc` 分岐を拡張＝**完全全画面モード中なら優先解除**、
    そうでなく素のフルスクリーン中なら従来どおり `Default` 復帰。`public SetFullScreen(bool)` を追加（既存 `ToggleFullScreen` は据え置き）。
  - 変更/新規: `MainPage.xaml`（右ペイン Grid に `x:Name="RightPaneRoot"`／左スプリッターに `x:Name="LeftSplitter"`）・
    `MainPage.xaml.cs`（コーディネータ＋退避フィールド）、`MainWindow.xaml.cs`（`Shift+F`/`Esc` 分岐＋`SetFullScreen`）、
    `Controls/PreviewControl.Immersive.cs`（`SetImmersive`/`IsImmersive`）。**Core 非変更**。`BUILD SUCCEEDED`。
    実機で `Shift+F` 完全全画面化／`Esc`・`Shift+F` 解除／グリッド入退場でのモード復元／左スプリッター非表示をユーザー確認済み（2026-06-25）。

- **Phase 4-B 完了（外部連携＋設定・2026-06-26）**: SPEC §3-8/§6-3 の外部連携キーと共有先パスの設定化を実装。
  評価キー（`PhotoKeyCommands`）と同じ「App 層の静的ディスパッチャ＋2 呼び出し点」パターンで横展開。**Core 非変更**。
  - **`PhotoFileCommands`（App層・新規）**: `TryHandle(key, item, settings)` が修飾子で分岐＝
    `Ctrl+E`=エクスプローラ `/select`（`explorer.exe "/select,\"<path>\""`）／`Alt+E`=既定アプリ（`ProcessStartInfo`
    `UseShellExecute=true`）／`Ctrl+Alt+E`=パスを `Clipboard` へコピー／`Alt+S`=共有（`ShareHelper` へ委譲）。
    各操作は try/catch でファイル消失等を黙殺。`Meta.Path` がフルパス。
  - **`ShareHelper`（App層・新規）**: `ShareAsync(path, settings)`。**SharePath 設定済み（かつ存在）→ その exe を
    `Process.Start`（パスを引数）／未設定（or exe 消失）→ Windows 標準の共有シート**。共有シートは WinUI 3 では
    `DataTransferManager.ShowShareUI()` を直接呼べず **`IDataTransferManagerInterop`（`GetForWindow`/`ShowShareUIForWindow`）に
    `App.WindowHandle`(HWND) を渡す相互運用**が必要（`DataTransferManager.As<…>()`＋`MarshalInterface<…>.FromAbi`、
    `DataRequested` は `GetDeferral` で `StorageFile` を非同期セット）。
  - **設定（`AppSettings.SharePath`／既定 ""）**: string 追加のみ（source-gen コンテキスト変更不要）。歯車ボタンは
    **ステータスバー右端**（`PhotoStatusBar.xaml` の列追加＋`&#xE713;`）。`SettingsButton_Click` が新規モーダル
    `Controls/SettingsDialog.xaml(.cs)` を開き、保存（Primary）で `Settings.SharePath` 反映＋`Settings.Save()`。
    exe 参照は WinRT `FileOpenPicker`（`.exe` フィルタ＋`InitializeWithWindow(App.WindowHandle)`）＋クリアボタン。
  - **2 呼び出し点**（評価キーと同列）: サムネイル＝`MainPage.HandleGlobalKeyDown`／プレビュー＝
    `PreviewControl.Input.cs` の `HandleKeyDown`。いずれも `PhotoFileCommands.TryHandle(...)` を評価キーより先に判定。
    プレビュー既存キー（Alt+矢印/Alt+F 等）と `Alt+E`/`Alt+S` は競合なし。
  - 決め事（ユーザー確定）: 共有＝**両対応**（SharePath あれば exe／無ければ標準シート）／設定入口＝**ステータスバー右端**。
  - 変更/新規: `PhotoFileCommands.cs`（新規）・`ShareHelper.cs`（新規）・`Controls/SettingsDialog.xaml(.cs)`（新規）、
    `AppSettings.cs`、`Controls/PhotoStatusBar.xaml(.cs)`、`MainPage.xaml.cs`、`Controls/PreviewControl.Input.cs`。
    `BUILD SUCCEEDED`（x64・警告0）／`dotnet test` 87 件緑。実機で Ctrl+E/Alt+E/Ctrl+Alt+E/Alt+S・設定保存・
    共有2系統をユーザー確認済み（2026-06-26）。`M`（デバッグ GC）は SPEC 通り未実装。

- **公開向けライセンス整備 完了（2026-06-26）**: GitHub 公開済みソースに対し、本アプリのライセンス明示と、同梱する
  第三者ライブラリの許諾義務への対応を実施。
  - **本アプリ＝MIT License**（`LICENSE`／`Copyright (c) 2026 KOBAT`）。使用ライブラリは全て許諾型（MIT/Apache/BSD/
    パブリックドメイン）でコピーレフトが無いため、アプリ自体を MIT にして矛盾・追加義務なし。
  - **`THIRD-PARTY-NOTICES.txt`（新規・リポジトリ直下）**: **配布物（自己完結EXE）に同梱されるコンポーネントのみ**
    のライセンス全文を集約。対象＝.NET ランタイム(MIT)／Windows App SDK 2.2.0(Microsoft Software License Terms・全文)／
    Win2D 1.4.0(MIT)／CommunityToolkit.Mvvm 8.4.2・WinUI.Controls.Sizers 8.2(MIT)／MetadataExtractor 2.9.3(Apache-2.0・全文)／
    XmpCore 6.1.10.1(BSD・Adobe XMP SDK 由来／MetadataExtractor の依存)／System.Data.SQLite.Core 1.0.119・SQLite(パブリック
    ドメイン)。**ビルド専用（SDK.BuildTools/WinApp）・テスト専用（xUnit/coverlet/Test.Sdk）は配布物に含まれず義務なしのため記載せず**。
  - **検証**: 各 NuGet パッケージの実体（`~/.nuget/packages`）の nuspec/license ファイルで著作権者・許諾を確認。Win2D の
    現行ライセンスは nuspec の旧 EULA URL ではなく **MIT**（GitHub microsoft/Win2D で確認）。XmpCore は LICENSE ファイル無し・
    README が「Adobe の XMP SDK と同じ BSD」と明記。WindowsAppSDK は `license.txt §3` で自己完結配布の再頒布を明示許可。
  - **発行物への自動同梱**: App csproj に `Content Include="..\..\LICENSE"`／`THIRD-PARTY-NOTICES.txt`（`Link`＋
    `CopyToOutputDirectory=PreserveNewest`）を追加。ビルド/publish 出力ルートへコピーされることを確認（`bin/x64/Release/.../win-x64/`）。
  - 変更/新規: `LICENSE`（新規）・`THIRD-PARTY-NOTICES.txt`（新規）、`PhotoQuickSelector.App.csproj`（Content 2 件）。
    `BUILD SUCCEEDED`（x64 Release・警告0）。**任意の次候補**: アプリ内「バージョン情報／ライセンス」画面（必須ではない）。
  - 注: これは法的助言ではないが、許諾型ライセンスの一般的な遵守要件（著作権表示＋ライセンス文の同梱）は満たしている。

- **評価データファイルの遅延作成＋作成確認ダイアログ 完了（2026-06-27）**: 「フォルダを開いただけで `PhotoQuickSelector.sqlite3`
  が即生成される」挙動を改め、**最初の評価操作時に初めて作成**するように変更。さらに作成直前に確認ダイアログを出し、OK の時だけ作る。
  - **① Core を遅延作成化（`MetadataStore`）**: コンストラクタで接続を開かない（＝ファイルを作らない）よう変更し、
    パス記録のみに。`DatabaseExists`（`File.Exists` 判定）を公開。`EnsureConnection(createIfMissing)` で遅延 Open＝
    読み取り（`false`）はファイルが無ければ開かず、**書き込み（`true`）で初めて Open＝ファイル生成点**。`LoadEvaluation` は
    ファイルが無ければ作らず `ExifRating` のみで返す（**EXIF の★表示は維持**）。`UpsertColumn`（`SaveRating`/`SaveFlagRating`/
    `SaveColorLabel`）が `EnsureConnection(true)` で生成。`Dispose` は null セーフ化。スキーマ系ヘルパは `_connection!`。
  - **② 作成前の確認ダイアログ（App・案A＝Action 解決→非同期 gate）**: `PhotoKeyCommands.TryHandleEvaluation`（即実行）を
    **`ResolveEvaluation`（実行せず `Action?` を返す）** へ改修（評価キー判定の同期 bool は維持）。`MainViewModel.ApplyEvaluationAsync(Action)`
    が、`_store` のファイル未作成時のみ `ConfirmCreateAsync` で確認し **OK のときだけ `op()` 実行（＝生成＋保存）／キャンセルは何もしない**
    （評価も変えない）。既存ファイルがあれば確認なしで即実行。ダイアログ（`ContentDialog`）は `MainPage.ConfirmCreateStoreAsync` が
    表示（VM は XamlRoot を持たないため View にコールバック登録。多重表示ガード付き）。
  - **2 呼び出し点**（評価キーと同列）: サムネイル＝`MainPage.HandleGlobalKeyDown`／プレビュー＝`PreviewControl.Input.cs`。
    いずれも `ResolveEvaluation → ApplyEvaluationAsync`（fire-and-forget。キーハンドラは await 不可）。`ContentDialog` 表示中は
    既存の `GetOpenPopupsForXamlRoot` 早期 return でグローバルキーが抑止されるため連打多重も防止。
  - 決め事（ユーザー確定）: キャンセル＝**何もしない**／ダイアログは**そのフォルダで最初の評価時に1回**（キャンセル後の再操作で再度尋ねる。
    OK 後はファイルが在るので以後無言）／文言＝「このフォルダに評価データファイル（PhotoQuickSelector.sqlite3）を作成します。よろしいですか？」。
  - 変更: `Core/MetadataStore.cs`、`PhotoKeyCommands.cs`、`ViewModels/MainViewModel.cs`、`MainPage.xaml.cs`、`Controls/PreviewControl.Input.cs`、
    `tests/…/MetadataStoreTests.cs`（`Constructor_DoesNotCreateDatabaseFile`/`LoadEvaluation_DoesNotCreateDatabaseFile`/`FirstSave_CreatesDatabaseFile` に更新）。
    `BUILD SUCCEEDED`（x64・警告0）／`dotnet test` 88 件緑。実機でフォルダを開いただけでは未生成・初回評価で確認→作成/キャンセルをユーザー確認済み（2026-06-27）。

- **UI 微修正3点（フォント統一＋フィルムストリップ整形）完了（2026-06-27）**: ユーザー指摘の見た目の違和感を調整。挙動・機能は不変。
  - **① メタ情報パネル上段の EXIF チップのフォント統一**: チップだけ `FontSize="12"` で隣（ファイル名・画像/ファイルサイズ＝既定14px）
    より小さかったため属性を削除し既定 14px へ。`Controls/PhotoStatusBar.xaml`（チップの `TextBlock`）。
  - **② フィルムストリップの選択セルに出る矩形枠を除去**: フォーカスが当たると `ListViewItem` の**システムフォーカス枠**
    （二重線の矩形）が選択強調のアクセント外枠とは別に描画されていた。`ItemContainerStyle` に
    `<Setter Property="UseSystemFocusVisuals" Value="False" />` を追加して無効化（選択強調はアクセント外枠＝`#FF333333` リングで継続）。
  - **③ フィルムストリップ上下の余分な余白を削減**: ListView の縦 `Padding` を `4`→**`4,0,4,2`（上0・下2）**、項目 `Margin` を
    `2`→`2,0`（縦の項目間隔0・横は維持）。下 2px は横スクロールバー（`HorizontalScrollBarVisibility=Auto`／オーバーレイ）の逃げとして残置。
    **`FilmChromeHeight` を `42`→`32` に連動**して下げ、削った余白ぶんサムネイル画像を拡大＝隙間が再発しないよう整合
    （行高は `AllPhotos` 由来でなく `FilmStrip.ActualHeight - FilmChromeHeight` でセル一辺を決めるため、Padding/Margin を減らしたら
    同じだけ定数も減らすのが要点）。アクセント外枠 `Margin=3`／カラーラベル枠 `BorderThickness=3`／ファイル名行は機能上必要なので維持。
  - 変更: `Controls/PhotoStatusBar.xaml`、`Controls/PreviewControl.xaml`、`Controls/PreviewControl.xaml.cs`（`FilmChromeHeight`）。
    **Core・ViewModel は非変更**。ユーザー画面確認済み（2026-06-27）。

- **プレビューのズーム改善（ホイール段スナップ＋キーボードズーム）完了（2026-06-27）**: ホイールズームの倍率が中途半端に
  なる問題を「round なズーム段へのスナップ」で解消し、さらに `+`/`-` でのキーボードズームを追加。**Core 非変更**（`PreviewViewport` は App 側）。
  - **① ホイール段スナップ（`PreviewViewport.ZoomToStop`）**: 旧「`Scale` を毎ティック ×1.15／÷1.15」（フィット起点の等比で
    87%・134% 等の半端な倍率になる）をやめ、**round な段ラダー** `ZoomStops`（表示倍率 DeviceScale 基準＝
    5/8/12/17/25/33/50/67/75/100/125/150/200/300/400/600/800/1200/1600%）の隣の段へスナップ。**フィット倍率を暫定段として
    動的に挿入**（ユーザー選択）するので、フィット付近では必ずフィットに止まり、そこからさらに動かせば下/上の段へ抜ける。
    フィット段着地時のみ `ZoomMode.Fit`（リサイズ再フィット）、他は `Custom`。**段は表示倍率基準なので高DPIでも 100% 等に正しく止まる**
    （内部 `Scale = 目標DeviceScale / DpiScale`）。マウス位置中心は既存 `SetScaleAround` を流用。`MainCanvas_PointerWheelChanged` を
    `ZoomBy`→`ZoomToStop(delta>0,…)` に差し替え。**ルーペ（`ZoomCanvas`）は 100% 精査用なので `ZoomBy`（連続）のまま据え置き**。
  - **② キーボードズーム（`+`/`-`）**: プレビュー中に **`+`=ズームイン／`-`=ズームアウト**。`ZoomToStop` を共有するので
    ホイールと同じ段ラダー（フィット段込み）、中心はキャンバス中心。`Z`/`Shift+Z`（フィット⇄ズーム/100%）は従来どおり併存。
    実装は `PreviewControl.Input.cs` の `HandleKeyDown` のみ（`ZoomStepFromCenter` ヘルパ追加）。
    - **JIS/US 配列対応（ユーザー要望）**: テンキー `VirtualKey.Add`(107)/`Subtract`(109)＝配列完全非依存 ＋ メイン段
      `(VirtualKey)187`(OEM_PLUS)/`(VirtualKey)189`(OEM_MINUS) の2経路を受ける。**`VK_OEM_PLUS`/`VK_OEM_MINUS` は
      「いかなる国/地域でも『＋』『−』キー」と定義された配列非依存の OEM ペア**で、US(`=`/`-`)・JIS(`;`/`-`)どちらも
      印字どおりの物理キーで効く。**Shift 不問**（US 素押し `=`／JIS 素押し `;` でもズームイン）。`Ctrl`/`Alt` 併用時は
      他機能優先で対象外（Alt+矢印パン等と非衝突）。
    - **判明した既存の落とし穴**: `[`/`]`（レーティング増減）は配列依存の `VK_OEM_4`(219)/`VK_OEM_6`(221) を使っており、
      JIS 配列では印字どおりに効かない可能性がある（今回の対象外。OEM_PLUS/MINUS と違い `[`/`]` は country 非依存ではない）。
  - **テスト（+4 件＝計92件緑）**: `PreviewViewportTests` に段スナップ／フィット段通過（Fitモード化）／高DPIでの%基準スナップ／
    最大段で停止 を追加（`PreviewViewport` はリンク参照で単体テスト）。
  - 変更: `Controls/PreviewViewport.cs`・`Controls/PreviewControl.MainCanvas.cs`・`Controls/PreviewControl.Input.cs`、
    `tests/…/PreviewViewportTests.cs`。`BUILD SUCCEEDED`（App x64 Release・警告0）／`dotnet test` 92 件緑。実機でホイール段スナップ・
    `+`/`-` キーボードズームをユーザー確認済み（2026-06-27）。

- **フィルムストリップ/グリッドの複数選択 完了（2026-06-27・要実機確認）**: 「焦点（常に1枚）」と「選択集合（0..N枚＝一括評価対象）」を
  概念分離し、複数選択と集合一括評価を追加。**概念整理に合わせて既存名もリネーム**（挙動不変の純リネーム＋新機能の二段）。
  - **リネーム（焦点系。挙動不変）**: `MainViewModel.SelectedPhoto`→**`FocusedPhoto`**／per-item `PhotoItemViewModel.IsSelected`→**`IsFocused`**／
    視覚 `SelectionOpacity`→**`CellOpacity`**（焦点 **or メンバー**で 1.0／他 0.9）・`SelectionFrameOpacity`→**`FocusFrameOpacity`**／
    焦点復元アンカー `_selectionAnchor`→**`_focusAnchor`**／`OnSelectedPhotoChanged`→**`OnFocusedPhotoChanged`**／`MoveBy`→**`MoveFocus`**。
    XAML の `x:Bind`（`PreviewControl.xaml`・`PhotoStatusBar.xaml`）と `nameof` 参照も追従。
  - **新概念（選択集合）**: `MainViewModel.SelectedPhotos`（`ObservableCollection`）＋ per-item **`IsInSelection`** ＋視覚
    **`SelectionHighlightOpacity`**（メンバー＝薄い青の外枠 `#FF66B2FF`＋背景ウォッシュ `#4066B2FF`・焦点リング(グレー)とは別レイヤで併存）。
    レンジ起点 `_selectionPivot`。
  - **焦点 vs 集合の調停（肝）**: 「素の焦点移動（マウス選択・集合空の前後移動・入場/読込）は集合をリセット」する一方、複数選択メソッドが
    焦点を動かす時は消さない。これを `OnFocusedPhotoChanged(old,new)` 内の `if(!_managingSelection) ClearSelection();` ＋
    複数選択メソッドが `SetFocusManaged()`（`_managingSelection` ガード付きで焦点設定）で焦点を動かす再入ガードで実現。マウス選択は
    TwoWay バインド経由で焦点が変わるのでガード無し＝集合リセット。`ApplyFilter` は絞り込みで結果外のメンバーを集合から外す（ガード ON で調停）。
  - **複数選択メソッド（`MainViewModel`）**: `ExtendSelectionTo(±1)`（Shift+←/→＝起点 pivot..焦点の連続レンジ・`SetSelectionRange`）／
    `MoveFocusKeepingSelection(±1)`（Ctrl+←/→＝焦点のみ移動・集合外可）／`ToggleFocusInSelection()`（Ctrl+Space＝焦点をトグル参加・pivot=焦点）／
    `MoveFocusWithinSelection(±1)`（集合ありの素 ←/→＝メンバー表示順で焦点巡回。焦点が集合外なら端メンバーへ）／`ClearSelection()`（Esc）。
    `MoveNext`/`MovePrevious` を「集合あり=巡回／無し=`MoveFocus`」へ分岐。
  - **一括評価（Alt+数字）**: `PhotoKeyCommands.ResolveEvaluation` を **`Action<PhotoItemViewModel>` 返し**へリファクタ（item 非依存化）＋
    新規 `ResolveBulkEvaluation`（**Alt+0–5＝レーティング／Alt+6–9＝赤黄緑青／Alt+P＝紫**。フラグ・増減は対象外）。
    `ApplyEvaluationAsync(op, targets)` へバッチ化（sqlite 作成確認は対象複数でも1回）。単一=焦点1枚／一括=`SelectedPhotos` 全員。
    **通常評価（Alt なし）は焦点1枚のみ＝集合不変**（要件どおり）。
  - **キー集約（新規 `SelectionKeyCommands.TryHandle(key, vm)`）**: 評価キーと同じ「App 層の静的ディスパッチャ＋2 呼び出し点」パターン。
    `MainPage.HandleGlobalKeyDown`（グリッド）／`PreviewControl.Input.cs`（プレビュー）の両方から、Alt 系ナビ（Alt+矢印パン等）の**後**に判定。
    グリッドの素 ←/→ は集合ありのとき横取りして巡回（空なら GridView 通常ナビへ）。Esc は集合があるときだけ消費（無ければ素通し＝
    全画面解除は `MainWindow` 側が先、プレビュー終了はしない）。Ctrl+L(フィルタ)/Ctrl+↑↓(フラグ)とは非衝突。
  - **マウス選択＝集合リセット**（ユーザー確定の決定2）／**集合中の通常数字は焦点1枚のみ**（決定1）。グリッドにもメンバー外枠を表示（焦点は GridView 標準）。
  - 変更/新規: `PhotoKeyCommands.cs`・`SelectionKeyCommands.cs`（新規）、`ViewModels/MainViewModel.cs`・`PhotoItemViewModel.cs`、
    `MainPage.xaml.cs`、`Controls/PreviewControl.Input.cs`・`.xaml.cs`・`.xaml`、`Controls/PhotoGridView.xaml(.cs)`、`Controls/PhotoStatusBar.xaml`。
    `BUILD SUCCEEDED`（App x64 Release・警告0）／`dotnet test` 92 件緑。**`MainViewModel` は WinUI 依存（`Visibility`）で Core.Tests から
    参照不可＝選択ロジックは単体テスト未実施**。実機目視（実キーボードで Shift/Ctrl+←/→・Ctrl+Space・Alt+数字一括・Esc 解除・
    マウス選択でのリセット）をユーザー確認済み（2026-06-27）。
  - **一括フラグ（`Ctrl+Alt+↑/↓`）追記（2026-06-27）**: 単一フラグ `Ctrl+↑↓` の対称形として一括フラグを追加。
    `PhotoKeyCommands.ResolveBulkEvaluation` に `Ctrl+Alt+↑/↓→FlagUp/FlagDown` 分岐を足し（既存 `Alt+数字` と同居）、
    グリッドは既存の一括評価ブロックが自動対応。**プレビューは `Ctrl+Alt+矢印` がルーペ縦スクロールと衝突**するため、
    `PreviewControl.Input.cs` の `Ctrl+Alt` ブロック先頭で **選択集合があるとき `↑/↓` だけ一括フラグへ振り分け／無いときは
    従来のルーペ縦スクロールへ素通し**（横スクロール `←/→`・`Ctrl+Alt+F` は常にルーペのまま）。`Ctrl+↑↓`(単一)とは別キーで非干渉。
    変更: `PhotoKeyCommands.cs`・`Controls/PreviewControl.Input.cs`。`BUILD SUCCEEDED`（x64 Release・警告0）。ユーザー確認済み（2026-06-27）。
  - **微修正3点（2026-06-27）**: ①メンバー強調色をアンバー→**薄い青（外枠 `#FF66B2FF`＋背景ウォッシュ `#4066B2FF`）**に変更
    （フィルムストリップ/グリッド両テンプレート）。②`MoveFocusKeepingSelection`（Ctrl+←/→）で**集合が空の状態から動き出すとき、
    もともと焦点だった1枚を選択メンバーに残す**（焦点だけ先へ動く）。③`MoveFocusWithinSelection`（集合中の素 ←/→）を
    clamp→**巻き戻し（modulo）**に変更（一番右で→は一番左へ／一番左で←は一番右へ）。
    変更: `ViewModels/MainViewModel.cs`、`Controls/PreviewControl.xaml`・`PhotoGridView.xaml`。`BUILD SUCCEEDED`（x64 Release・警告0）。
    ユーザー確認済み（2026-06-27）。

- **アプリアイコン作成 完了（2026-06-27）**: 旧テンプレート（VS 既定の WinUI ロゴ）から自前デザインへ差し替え。
  デザイン＝**青の角丸タイル ＋ 白い写真フレーム（太陽＋山）＋ 右下にゴールドの星**（「写真を選別＝レーティング」を表現）。
  星色はアプリの★と同じ `#FFD700`（`Colors.Gold`／`PhotoItemViewModel.NormalRatingBrush`）。星位置はユーザー選定で
  フレーム右下角から **-8px 上**（「案1」）。山の下に白い余白を残し外枠（青）と内枠（写真）を区別。
  - **テキスト形式のソース（再生成可能）**: `Assets/AppIcon.svg`（200×200 座標系のマスター。ジオメトリの単一情報源）と
    `tools/generate-app-icon.ps1`（GDI+ で SVG と同一ジオメトリを各サイズへラスタライズ）。デザイン変更は両者の座標
    （特に星の頂点）を直して PS スクリプトを再実行＝全 PNG/.ico を一括再生成。
  - **生成物**: `Assets/AppIcon.ico`（16/24/32/48/64/128/256px の **PNG 埋め込み複数解像度**。ICONDIR を手書きで構築）＋
    パッケージ用 PNG 一式を新デザインで上書き（StoreLogo 50／Square44x44 scale-200=88・targetsize 24/48／Square150x150
    scale-200=300／LockScreenLogo 48／Wide310x150 scale-200=620×300／SplashScreen scale-200=1240×600）。正方は全面、
    ワイド/スプラッシュは中央配置＋左右透明。
  - **組み込み**: csproj に `<ApplicationIcon>Assets\AppIcon.ico</ApplicationIcon>` を追加（unpackaged EXE 用）。
    `Package.appxmanifest` は既存のロゴ参照のまま PNG を差し替え＝packaged 開発時のタスクバー/タイルも更新。
  - **落とし穴**: 生成スクリプトに日本語コメントを入れると Windows PowerShell 5.1 が UTF-8(BOMなし)を ANSI 誤読し
    文字化け→パースエラー。**PS スクリプトは ASCII のみ**で書く（SVG の日本語コメントは UTF-8 で読まれるので可）。
  - **検証**: 生成 PNG を目視・`.ico` 構造を検査（7 解像度・全 PNG）・`dotnet build -c Release -p:Platform=x64` 成功（警告0）・
    ビルド済み EXE からアイコン抽出成功（リソース埋め込み確認）。実機タスクバー/エクスプローラの最終見た目はユーザー確認推奨。
  - 変更/新規: `Assets/AppIcon.svg`（新規）・`tools/generate-app-icon.ps1`（新規）・`Assets/*.png`/`AppIcon.ico`（再生成）・
    `PhotoQuickSelector.App.csproj`（`ApplicationIcon`）。**Core・アプリコードは非変更**。
  - **タイルをチャコール化（Dark テーマ対応・2026-06-28）**: アプリ全体の黒系化に合わせ、アイコンの**タイル（枠）色を
    青 `#3478F6` → チャコール `#2A2A2A`** に変更。**山は元の青 `#3478F6` のまま残しアクセントに**（白フレーム・ゴールドの
    太陽/星も維持）。`AppIcon.svg`（タイルの `fill`）と `generate-app-icon.ps1`（`$charcoal` を追加しタイルへ適用・山は `$bBlue`
    継続）を更新し PS スクリプト再実行で PNG/.ico を一括再生成。`dotnet build -c Release -p:Platform=x64` 成功（警告0）。
    実機タスクバー/タイルの見た目はユーザー確認推奨。変更: `Assets/AppIcon.svg`・`tools/generate-app-icon.ps1`・
    `Assets/*.png`/`AppIcon.ico`（再生成）。**Core・アプリコードは非変更**。

- **構図グリッドの種類追加＋基準切替 完了（2026-06-27・要実機確認）**: プレビューの「三分割グリッド線」固定トグルを、
  **種類（中央十字／三分割／正方形）×基準（画像／Canvas）の2軸**に拡張。
  - **状態モデル**: 旧 `MainViewModel.ShowGrid`(bool) を廃し、**enum 2軸** `GridOverlayKind { None, CenterCross,
    RuleOfThirds, Square }`／`GridOverlayReference { Image, Canvas }`（新規 `GridOverlayKind.cs`・ルート名前空間）。
    `MainViewModel` の `GridKind`/`GridReference`（`[ObservableProperty]`）は ctor で `Settings` から初期化、`OnChanged` で
    `Settings` へ反映（**保存は終了時 `Settings.Save()` に相乗り**＝`ShowInfoOverlay` と同方式）。`GridSquareDivisions` は
    `Settings` 直読み（既定8）。
  - **永続化（`AppSettings`）**: `GridKind`/`GridReference`/`GridSquareDivisions` を追加。**enum/int プロパティは source-gen
    コンテキスト（`AppSettingsJsonContext`）の追加登録不要**（`AppSettings` 登録済みなのでプロパティとして自動対応・既定は数値）。
  - **キー（`PreviewControl.Input.cs`）**: `G`＝種類を巡回（**None→中央十字→三分割→正方形→None**＝徐々に細かく）／
    `Shift+G`＝基準トグル（画像⇄Canvas）。既存キーと非衝突（`Shift+Z`は別、`Shift+G`は空き）。
  - **描画（`PreviewControl.Overlays.cs` の `DrawGrid` 再構成）**: 「基準で領域矩形（画像基準＝`OffsetX/DrawWidth` の表示中
    画像矩形・ズーム/パン追従／Canvas基準＝コントロール全面）→ 種類別に線」。線は「領域∩キャンバス」にクリップ（ローカル関数
    `V(x)`/`H(y)`）。三分割は分割点を全画像矩形基準で計算し線をクリップ（旧挙動踏襲）。正方形は新ヘルパ `DrawSquareGrid`＝
    **cell＝短辺/N の正方セルを領域中心から対称配置**（中心オフセット集合：偶数N=`{0,±cell,±2cell…}`＝中央に線／
    奇数N=`{±cell/2,±3cell/2…}`＝中央線なし。長辺方向も同位相で延長）。cell が領域比例なのでズーム非依存で線数は概ね一定。
  - **再描画**: `PreviewControl.xaml.cs` の property 監視を `ShowGrid`→`GridKind`/`GridReference` に差し替え（変更で
    `MainCanvas.Invalidate()`）。描画条件（`MainCanvas.cs`）も `GridKind != None` に。**スコープはメインキャンバスのみ**
    （ルーペ/ナビは非対象）。N 調整 UI は未実装（設定値で効く）。
  - 変更/新規: `GridOverlayKind.cs`（新規）、`AppSettings.cs`、`ViewModels/MainViewModel.cs`、`Controls/PreviewControl.Input.cs`・
    `.MainCanvas.cs`・`.Overlays.cs`・`.xaml.cs`。**Core・XAML は非変更**。`BUILD SUCCEEDED`（x64 Release・警告0）／
    `dotnet test` 92 件緑。実機目視（G 巡回・Shift+G 基準切替・正方形の対称性/偶奇・永続化）はユーザー確認推奨。

- **プレビューのマウスクリック挙動変更＋マウス位置基準ズーム 完了（2026-06-27）**: プレビュー画面のクリック操作を刷新。
  ユーザー確認済み。
  - **挙動（ユーザー確定）**: ①メイン大画面の**シングルクリック＝フィット⇄ズーム切替**（`Z` キーと同一倍率）／
    ②**ダブルクリック＝100%表示**（`Shift+Z` と同一）／③**フィルムストリップのダブルクリック＝グリッドビューへ戻る**。
    さらにシングル/ダブルとも**ズーム中心はクリック位置基準**（ホイールズームと同様にカーソル下の画像点を固定）。
  - **メイン画像のプレビュー終了を撤去→フィルムストリップへ移設**: 旧 `MainCanvas_DoubleTapped`＝`ExitPreview()` を
    100%表示へ差し替え、マウスでの終了導線は新 `FilmStrip_DoubleTapped`＝`ExitPreview()` に集約。マウスでの終了は
    フィルムストリップのダブルクリックのみになる（`Esc` は元々終了しない仕様）。**イマーシブ表示（`F`）中はフィルムストリップが
    畳まれるため、その状態ではマウス終了導線が無い**（`F`/`Shift+F`/`Esc` 系で対応）。
  - **シングル⇄ダブルの競合**: WinUI は `Tapped`/`DoubleTapped` を独立発火し、ダブル時は `Tapped`（トグル）が先に発火するが
    最終状態は 100% に確定（ちらつき許容＝ユーザー選択・即時反応優先。タイマー遅延判定は不採用）。**`Tapped` はドラッグ
    （しきい値超えの移動）では発火しない**ので、既存の `PointerPressed/Moved/Released`（パン）と自然に分離＝パン操作は不変。
  - **マウス位置基準ズーム**: `PreviewViewport` に `ToggleZoomAround(cx,cy)`／`SetActualSizeAround(cx,cy)` を追加。どちらも
    既存 `SetScaleAround`（カーソル下の画像点を固定したまま倍率変更）を流用。倍率は `Z`/`Shift+Z` と同じ（`ToggleZoomAround` は
    記憶倍率/モード、無ければ等倍）だが**中心は記憶の相対位置ではなくカーソル位置**。**キーボードの `Z`/`Shift+Z`
    （`ToggleZoom()`/`SetActualSize()`）は非変更**＝従来どおり中央/記憶位置基準を維持。
  - **テスト**: `PreviewViewportTests` に +4 件（クリック点の画像点がズーム後も同一キャンバス座標に固定＝マウス基準／記憶倍率の
    流用＋新中心／ズーム中→フィット復帰／100%のマウス基準）。`PreviewViewport` は UI 非依存でリンク参照の単体テスト。
  - 変更: `Controls/PreviewViewport.cs`（2メソッド追加）・`Controls/PreviewControl.MainCanvas.cs`（`Tapped`/`DoubleTapped` 配線）・
    `Controls/PreviewControl.xaml`（`MainCanvas` に `Tapped`、`FilmStrip` に `DoubleTapped`）・`Controls/PreviewControl.xaml.cs`
    （`FilmStrip_DoubleTapped`）、`tests/…/PreviewViewportTests.cs`。**Core・キー処理（Input.cs）は非変更**。
    `BUILD SUCCEEDED`（x64 Release・警告0）／`dotnet test` 96 件緑。

- **ハンバーガーメニュー追加（案A＝MenuFlyout・クリック実行可）完了（2026-06-27）**: ステータスバー右端に
  ハンバーガー（`&#xE700;`）ボタンを追加し、既存ショートカットのうち「トグル/単発アクション」系をメニュー化。
  各項目は**クリックで実行**＋ショートカットを `KeyboardAcceleratorTextOverride` で右側にグレー表示（**表示専用**＝
  実キー処理は従来の `MainWindow` ルート集約のまま＝二重ハンドリングなし）。**歯車ボタンは廃止し「設定…」をメニューへ集約**。
  - **メニュー構成**: フィルタ ON/OFF(`Ctrl+L`)・全画面表示(`F11`)・完全全画面(`Shift+F`)／プレビュー▶(プレビュー時のみ有効)＝
    イマーシブ(`F`)・メタ情報(`I`)・構図グリッド種類(`G`)/基準(`Shift+G`)／ファイル▶(写真選択時のみ有効)＝エクスプローラ(`Ctrl+E`)・
    既定アプリ(`Alt+E`)・パスをコピー(`Ctrl+Alt+E`)・共有(`Alt+S`)／設定…。評価キー・前後移動・ズーム・複数選択・一括評価は
    「高速反復操作」なので**メニューに載せない**（ユーザー確定スコープ）。リネームコピー/Reject 移動はショートカット未割当のため
    対象外（フィルタのフライアウト内に維持）。
  - **状態同期**: `ToggleMenuFlyoutItem.IsChecked` はバインドせず `MenuFlyout.Opening` で毎回 VM 状態から再設定（メニューは短命。
    既存 `FilterFlyout_Opening` と同パターン）。同 `Opening` で `PreviewSubItem.IsEnabled=IsPreviewMode`／
    `FileSubItem.IsEnabled=FocusedPhoto!=null` も更新。「完全全画面」はメニュー到達時は常に「入る」操作（ON 時はステータスバー
    非表示で到達不能）なのでトグルではなく通常項目。
  - **配線（単一ソース維持）**: 設定/フィルタ/メタ情報は VM 直接。全画面/完全全画面/イマーシブは `PhotoStatusBar` のイベント
    （既存 `ToggleFullScreenRequested`＋新規 `ToggleFullImageRequested`/`ToggleImmersiveRequested`）で `MainPage` へ委譲。
    チェック表示用に `IsImmersiveProvider`/`IsFullScreenProvider`（`Func<bool>`）を `MainPage` が Preview/MainWindow から供給
    （`MainWindow.IsFullScreen` 追加）。構図グリッドは `MainViewModel.CycleGridKind()`/`ToggleGridReference()` を新設し
    `G`/`Shift+G` キー（`PreviewControl.Input.cs`）とメニューで共用。ファイル連携は `PhotoFileCommands` に
    `OpenInExplorer/OpenWithDefault/CopyPath/Share`(写真受け取り版)を抽出し `TryHandle`(キー)と共用。
  - 変更: `Controls/PhotoStatusBar.xaml(.cs)`・`Controls/FilterBar.xaml`、`MainPage.xaml.cs`、`MainWindow.xaml.cs`、
    `PhotoFileCommands.cs`、`ViewModels/MainViewModel.cs`、`Controls/PreviewControl.Input.cs`。**Core 非変更**。
    `BUILD SUCCEEDED`（x64 Release・警告0）／`dotnet test` 96 件緑。ユーザー画面確認済み（2026-06-27）。

- **左ペイン開閉ボタンのグリフを開閉状態へ追従＋向き修正＋ボタン寸法統一 完了（2026-06-27）**:
  - **グリフ追従（案2＝幅変化を唯一の起点に一元更新）**: 旧・固定グリフをやめ、左ペインの開閉状態に追従。
    `PhotoStatusBar.UpdateLeftPaneGlyph(bool open)`（`FontIcon`＋ツールチップ切替）を、`MainPage` が `LeftNav.SizeChanged` を
    観測して `ActualWidth>0` で呼ぶ。開閉のきっかけ（ボタン/スプリッタードラッグで幅0/完全全画面/起動復元）すべてを 1 経路でカバー
    （各経路への個別更新コード不要）。`MainPage_Loaded` の復元直後に初期同期。
  - **重要（向きの確定）**: グリフをフォント（Segoe Fluent Icons）で実レンダリングして確認＝**`U+E89F`(ClosePane)＝右矢印→／
    `U+E8A0`(OpenPane)＝左矢印←**。左ペインは左側にあるので「開＝左へ畳む＝左矢印`E8A0`／閉＝右へ展開＝右矢印`E89F`」が正解。
    最初の実装は MDL2 の名前を額面どおり対応させて**逆**だった（ユーザー指摘で実測→修正）。
  - **ボタン寸法統一**: アイコンのみのボタンはテキスト持ちのフィルタボタンより低かった（`FontIcon` の行ボックス＜テキスト行）。
    左ペイン/全画面/メニューに `Height="32" Width="32" Padding="0"`＝**32×32 正方形**、フィルタボタンは `Height="32"`（幅は件数テキストで可変）。
    32 は WinUI 標準のボタン高さ。実機で正方形・アイコン中央・はみ出しなしを確認。
  - 変更: `Controls/PhotoStatusBar.xaml(.cs)`・`Controls/FilterBar.xaml`、`MainPage.xaml.cs`。**Core・ViewModel は非変更**。
    `BUILD SUCCEEDED`。`dotnet run` で実機確認・拡大目視（グリフ向き／開閉追従／4ボタンの高さ・3ボタンの幅統一）済み（2026-06-27）。

- **バージョン情報画面＋ライセンス画面 完了（2026-06-28）**: ステータスバー右端のハンバーガーメニューに「バージョン情報…」を追加し、
  About → ライセンス の2画面を実装。**Core・`MainViewModel`・`MainWindow` は非変更**（App 層のダイアログ追加のみ）。
  - **メニュー（`Controls/PhotoStatusBar.xaml(.cs)`）**: 「設定…」直下に「バージョン情報…」。`MenuAbout_Click` で `AboutDialog` を表示し、
    **Primary（「ライセンス情報」）が押されたら About を閉じてから `LicenseDialog` を開く連鎖**（`ContentDialog` の入れ子表示は
    クラッシュ要因のため必ず閉じてから次を開く）。
  - **`Controls/AboutDialog.xaml(.cs)`（新規）**: アプリアイコン（`ms-appx:///Assets/Square150x150Logo.scale-200.png`）＋アプリ名＋
    バージョン＋`Copyright © 2026 KOBAT`＋MIT＋GitHub リンク。**バージョンはハードコードせず**
    `Assembly.GetExecutingAssembly().GetName().Version` を `x.y.z` 整形して表示。
  - **`Controls/LicenseDialog.xaml(.cs)`（新規）**: `Pivot`（「本アプリ (MIT)」／「ライブラリ」）で全文をスクロール表示
    （`ContentDialogMaxWidth` を 780 へ拡張）。全文は **アセンブリ埋め込みリソース**（`GetManifestResourceStream("LICENSE")`／
    `"THIRD-PARTY-NOTICES.txt"`）から読む＝single-file 発行でも exe 隣のファイル有無に依存せず確実に表示できる。
  - **バージョンの単一情報源（`csproj`）**: `<Version>1.0.0</Version>` を追加（About がリフレクション参照）。
  - **ライセンス文の二重持ち（`csproj`）**: 既存の `Content`/`Link`（配布物への同梱＝法的要件）はそのまま残し、別途
    `EmbeddedResource`（`LogicalName="LICENSE"` / `"THIRD-PARTY-NOTICES.txt"`）でアセンブリにも埋め込む（アプリ内表示用）。
    ビルド済みアセンブリで埋め込み2件を `GetManifestResourceNames()` で確認済み。
  - 変更/新規: `PhotoQuickSelector.App.csproj`（`<Version>`＋`EmbeddedResource` 2件）、`Controls/PhotoStatusBar.xaml(.cs)`、
    `Controls/AboutDialog.xaml(.cs)`（新規）、`Controls/LicenseDialog.xaml(.cs)`（新規）。`BUILD SUCCEEDED`（x64 Release・警告0）。
    実機目視（メニュー項目・About 表示・ライセンス全文スクロール・GitHub リンク）はユーザー確認推奨。

- **ショートカット一覧（チートシート）画面 完了（2026-06-28）**: キー操作の一覧を **モーダル `ContentDialog`** で表示。
  入口は **ハンバーガーメニュー「ショートカット一覧…」＋ `F1` キー**。**Core・既存のキー処理ロジックは非変更**（App 層の追加のみ）。
  - **データ（`ShortcutCheatSheet.cs`・新規／App ルート名前空間）**: 表示専用の静的定義。`record ShortcutItem(Keys, Description)`
    ＋`record ShortcutGroup(Title, Items)`。グループ順は **全般→表示→移動→評価→複数選択→ファイル連携**（ユーザー指定）。
    内容は CLAUDE.md「キー操作」節が元ネタ。**項目追加はこのリストへ 1 行足すだけ**。
  - **設計判断（二重持ち）**: 実キー処理は 5 箇所（`PhotoKeyCommands`/`SelectionKeyCommands`/`PhotoFileCommands`/
    `PreviewControl.Input`/`MainWindow.RootGrid_PreviewKeyDown`）に分散。今回は SSOT 化せず**表示専用の独立データ**にした
    （処理との二重持ち＝低リスク優先。SSOT 化は将来課題としてコメントに明記）。
  - **ダイアログ（`Controls/ShortcutsDialog.xaml(.cs)`・新規）**: `LicenseDialog` 同型（`ContentDialogMaxWidth=720` へ拡張、
    `ScrollViewer` で縦スクロール）。`ItemsControl`（グループ）＋ネスト `ItemsControl`（項目）でデータ駆動。各項目は
    キー列（角丸チップ＝EXIF チップと同デザイン・幅 210px 固定で全行そろえ）｜説明（ラップ）の 2 カラム。`ItemsSource` は
    コードビハインドで `ShortcutCheatSheet.Groups` を流し込み（DataTemplate はクラシック `{Binding}`）。
  - **入口**: `PhotoStatusBar.xaml(.cs)` のメニューに「設定…」直下へ「ショートカット一覧…」（`F1` 併記・`MenuShortcuts_Click`）。
    `MainWindow.RootGrid_PreviewKeyDown` に **`F1`** 分岐（`ShowShortcutsAsync` を fire-and-forget）。ポップアップ開時は
    既存の `GetOpenPopupsForXamlRoot` 早期 return で弾かれ二重表示しない。
  - 変更/新規: `ShortcutCheatSheet.cs`（新規）・`Controls/ShortcutsDialog.xaml(.cs)`（新規）、`Controls/PhotoStatusBar.xaml(.cs)`、
    `MainWindow.xaml.cs`。`BUILD SUCCEEDED`（x64 Release・警告0）。ユーザー画面確認済み（2026-06-28）。

- **レンズ名の前にレンズメーカーを表示（案A＝EXIF `LensMake` タグ）完了（2026-06-28）**: 詳細メタ情報パネルの
  「カメラ・レンズ名」（`CameraLensText`）で、レンズ名の前に EXIF のレンズメーカー（`LensMake` タグ 0xA433）を付ける。
  - **Core**: `ImageMetadata.LensMake` を追加し、`MetadataReader` で `GetString(subIfd, ExifDirectoryBase.TagLensMake)` を読む
    （`LensModel` と対のタグ）。
  - **App（`PhotoItemViewModel.CameraLensText`）**: レンズ部を `{LensMake} {LensModel}`（メーカー空なら従来どおりモデルのみ）に変更。
    例 `SONY ILCE-1 / SONY FE 50mm…`。**メーカーがカメラ側と重複してもそのまま出す**（ユーザー確定）。空要素は従来どおり省略。
  - **重要な検証結果（割り切り）**: テストデータ（Sony α1＋OM-1）は **`LensMake` タグを書き込んでおらず全て空**だった
    （実機材ではメーカー表示は出ず、従来どおりレンズ名のみ）。多くのカメラがこのタグを省略する（exiftool 等はタグでなく内蔵
    レンズ DB 照合で表示）。実データの SIGMA/`OM 90mm` 等はボディ `Make`(SONY) と別物のため「空ならカメラメーカー代用」は誤りになる。
    レンズ名からのメーカー推定（ヒューリスティック補完）も検討したが**ユーザー選択で案A（タグ依存）のまま据え置き**＝
    タグを書く機材/レンズでのみ有効・無い機材では無害なフォールバック（レンズ名のみ）。
  - 変更: `Core/ImageMetadata.cs`・`Core/MetadataReader.cs`、`ViewModels/PhotoItemViewModel.cs`。`BUILD SUCCEEDED`／
    `dotnet test` 96 件緑。表示箇所はメタ情報パネル案A（ステータスバー）／案B（プレビュー左上オーバーレイ）の `CameraLensText` 両方。

- **アプリ全体を黒系（Dark テーマ）化 完了（2026-06-28）**: 「写真選別アプリを黒背景にしたい」要望に対応。Dark テーマ固定＋
  タイトルバー暗色化＋選択/焦点強調色の調整を実施。ユーザー画面確認済み。**Core・ViewModel は非変更**（App の表示のみ）。
  - **① Dark テーマ固定（方法1）**: `MainWindow.xaml` のルート `Grid`（`RootGrid`）に `RequestedTheme="Dark"` を付与。
    背景は全て `ThemeResource`（`SolidBackgroundFillColorBaseBrush`/`SubtleFillColorSecondaryBrush`/`CardBackgroundFillColorDefaultBrush`
    等）経由なので一括で暗化。Win2D の `ClearColor="Transparent"` も親ブラシ追従で暗くなる。色味は Fluent の暗グレー（純黒ではない）。
    - メモ: `Application` レベルでなく `RootGrid`（子要素）に付けたため、**ポップアップ/フライアウト/ダイアログは継承しない**
      （別ビジュアルツリー）。今回それらは問題にならなかったが、将来ポップアップも暗くするなら `App.xaml` の `<Application RequestedTheme="Dark">` へ移すのが本筋。
  - **② タイトルバーの暗色化（案A＝DWM イマーシブ ダークモード）**: 標準タイトルバー（`ExtendsContentIntoTitleBar=false`）は OS が描く
    ため Dark テーマでも白いまま。`MainWindow` ctor に `EnableDarkTitleBar()` を追加し、HWND（`WinRT.Interop.WindowNative.GetWindowHandle`）へ
    `dwmapi.dll` の `DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE=20, 1, 4)` を P/Invoke。標準タイトルバーを Windows 標準の
    暗色（濃いグレー＋明るいグリフ）で描かせる。**`ExtendsContentIntoTitleBar=false` のままなので ×ボタンのフォーカスレース
    （`close-button-titlebar-focus-race`）は再発しない**。属性は HWND に残るので全画面⇄復帰でも維持。色は Windows 標準ダーク色固定
    （任意の真っ黒指定は不可。それには `AppWindow.TitleBar` 色指定＝`ExtendsContentIntoTitleBar=true` が要りレース再発のため不採用）。
  - **③ フィルムストリップの焦点リング色**: 暗背景で `#FF333333`（濃いグレー）が溶けて見えないため `#FFE0E0E0`（薄グレー）へ
    （`PreviewControl.xaml`・焦点 overlay の `BorderBrush`）。
  - **④ 複数選択（選択集合）メンバーの強調を青→白系へ**: 暗背景＋焦点リング変更で青がちぐはぐになったため、メンバー強調を
    枠 `#FF66B2FF`→`#FFFFFFFF`／背景ウォッシュ `#4066B2FF`→`#33FFFFFF`（半透明白）に。**フィルムストリップ（`PreviewControl.xaml`）と
    グリッド（`PhotoGridView.xaml`）の両方**で統一。焦点（薄グレーの外周リング・塗りなし）とメンバー（白枠＋塗りウォッシュ）は
    「塗りの有無」で見分ける。
  - **⑤ グリッドの焦点（=選択）枠を白系へ＋外周の白フォーカス矩形を除去**: グリッドは焦点＝選択（`SelectionMode=Single`＋
    `FocusedPhoto`↔`SelectedItem` 同期）なので、GridViewItem 既定の**選択枠**（`SelectedBorderBrush`＝既定アクセント青＝水色）が
    焦点インジケータ。これを白系へ。`PhotoGridView.xaml` の `GridView.Resources` で `GridViewItemSelectedBorderBrush`／
    `…SelectedPointerOverBorderBrush`／`…SelectedPressedBorderBrush` を `#FFE0E0E0` に上書き。加えて GridViewItem 既定の
    **キーボードフォーカス枠（外周の白い矩形）**を `ItemContainerStyle` の `UseSystemFocusVisuals="False"` で無効化。
    - **重要な落とし穴（実機で発覚→修正）**: `ItemContainerStyle` を **`BasedOn` なし**で書くと既定スタイル
      （`DefaultGridViewItemStyle`）を丸ごと置き換え、**選択枠を描く `ListViewItemPresenter` の配線（`SelectedBorderBrush`/
      `SelectedBorderThickness` を持つ Template）が失われて焦点セルの枠が一切出なくなる**（中身はフォールバック Template で表示される
      ので画像は見える＝症状が分かりにくい）。SDK の `generic.xaml` で確認: 選択枠は `DefaultGridViewItemStyle` の Template 内に
      `SelectedBorderBrush="{ThemeResource GridViewItemSelectedBorderBrush}"` として配線（暗黙スタイルも `BasedOn DefaultGridViewItemStyle`）。
      → **`<Style TargetType="GridViewItem" BasedOn="{StaticResource DefaultGridViewItemStyle}">` 必須**。フィルムストリップ（ListView）は
      独自の焦点リング overlay で焦点を示すため BasedOn なしでも顕在化しなかった（システム選択枠に非依存）。複数選択メンバーの白枠は
      `DataTemplate` 内の独自 overlay `Border` なのでコンテナスタイル置き換えの影響を受けない（だから「メンバーだけ白枠」状態になった）。
  - 変更: `MainWindow.xaml`（`RequestedTheme="Dark"`）・`MainWindow.xaml.cs`（`EnableDarkTitleBar`＋P/Invoke）、
    `Controls/PreviewControl.xaml`（焦点リング/メンバー強調色）、`Controls/PhotoGridView.xaml`（メンバー強調色＋`GridView.Resources`
    選択枠色上書き＋`ItemContainerStyle` BasedOn＋`UseSystemFocusVisuals=False`）。`BUILD SUCCEEDED`（x64 Release・警告0）。

- **単一ファイル発行で窓/タスクバーのアイコンが反映されない不具合 修正完了（2026-06-30）**: `dotnet publish` の
  単一ファイル（`PublishSingleFile=true`）で起動後のアイコンが既定に戻る不具合を解消。**Core 非変更**（`MainWindow.xaml.cs` のみ）。
  - **真因（アイコンは2系統）**: ①エクスプローラ上の **exe ファイルアイコン**（`<ApplicationIcon>` による exe 埋め込み＝
    `RT_GROUP_ICON`）は単一ファイルでも正しく入る（実バイナリ解析で確認：1 グループ・7 解像度・PNG エントリがソース
    `AppIcon.ico` と一致）。壊れていたのは ②**実行時に設定する窓/タスクバーのアイコン**で、旧コードは
    `AppWindow.SetIcon("Assets/AppIcon.ico")`＝**ディスク上の .ico ファイルを相対パスで読む** API だった。単一ファイル発行では
    `Assets\AppIcon.ico` も `resources.pri` も exe 内へ埋め込まれて**ディスクに残らない**（出力フォルダに Assets なし）ため
    `SetIcon` が黙って失敗し既定アイコンへ。フォルダ発行/packaged では loose ファイルが残るので顕在化しなかった。
  - **修正（埋め込みアイコンを HICON でロード＝ファイルパス非依存）**: 新ヘルパー `SetWindowIconFromEmbedded()`＝
    `GetModuleHandleW(null)`（自 exe）→ `EnumResourceNamesW(h, RT_GROUP_ICON=14, …)` で**最初の**グループアイコン ID を
    列挙取得（ID 決め打ちせず堅牢。apphost では実測 id=32512）→ `LoadImageW(…, IMAGE_ICON, LR_DEFAULTSIZE|LR_SHARED)` で
    HICON → `Microsoft.UI.Win32Interop.GetIconIdFromIcon(hIcon)` → `AppWindow.SetIcon(iconId)`。packaged/フォルダ/単一ファイル
    すべてで効く。`EnableDarkTitleBar` と同じ P/Invoke 区画に declarations を追加。
  - **検証**: `BUILD SUCCEEDED`（x64 Release・警告0）。発行済み単一ファイル exe に同じ P/Invoke 経路（`LoadLibraryExW(...,0x20)`
    →Enum→`LoadImageW`）を当て、最初の `RT_GROUP_ICON` id が解決し `LoadImage` が有効な HICON を返すことを確認。再発行
    （`-p:PublishProfile=win-x64-singlefile`）後の実機タスクバーアイコンの最終目視はユーザー推奨。再利用知見はメモリ `winui-seticon-singlefile`。
  - 変更: `MainWindow.xaml.cs` のみ（`SetIcon(path)` → `SetWindowIconFromEmbedded()` ＋ P/Invoke 3 件）。

- **左右キー押しっぱなしで先読みキャッシュが膨張するメモリリーク 修正完了（2026-06-30）**: 「←/→を押し続けるとメモリが
  増え続け、離すと元に戻る」不具合を解消。先読みキャッシュ一覧オーバーレイ（`C`）で「押している間はキャッシュが増え続け
  解放されない」ことを観測。**真因＝`PreviewControl.LoadCurrentAsync` の追い越し検出 `if (token != _loadToken) return;` が
  `_cache.Trim()`（保持窓外の破棄）まで一緒にスキップしていた**こと。
  - **機序**: ←/→ 押しっぱなしで `FocusedPhoto` が次々変わるたび `LoadCurrentAsync` が `_loadToken` をインクリメントし
    `await _cache.GetAsync(path)` で中断。1枚のデコードはキーのオートリピート間隔より長いので、await 復帰前に次の呼び出しが
    `_loadToken` を進める → 中断側は復帰時に必ず `token != _loadToken` で**早期 return**。`Trim` 呼び出しはコード全体でこの
    関数末尾の 1 箇所のみのため、**押している間は一度も Trim が走らない**。一方 `GetAsync` は通り過ぎた中間写真も
    `LoadCoreAsync` 完了時に `_cache[path]=bmp` で追加し続ける（世代はナビ中不変＝破棄もされない）→ 増え続ける。離すと
    最後の 1 回だけ `token==_loadToken` で末尾に到達し `Trim` が走り保持窓（前1/後2）外を一括破棄＝元に戻る。
  - **修正**: 追い越された場合でも `return` の前に `_cache.Trim(WindowPaths(), _bitmap)` を通す。`WindowPaths()` は現在の
    `FocusedPhoto` 基準なので追い越し側から呼んでも常に最新窓へ収束。`Trim` は冪等・軽量なのでキャッシュは「窓＋進行中の
    数枚」に収まる。表示の確定は従来どおり最新側（token 一致）の呼び出しに任せる。
  - 変更: `Controls/PreviewControl.xaml.cs` のみ（早期 return 直前に Trim を追加）。**Core・`PreviewBitmapCache`・XAML は非変更**。
    `BUILD SUCCEEDED`（x64 Release・警告0）。実機目視（`C` オーバーレイでキャッシュ件数が頭打ち・メモリが増え続けない）はユーザー確認推奨。

- **左右キー押しっぱなしで「読み込み中（loading）」が膨張する続発不具合 修正完了（2026-06-30・案2採用）**: 上の Trim 漏れ修正で
  「デコード済みキャッシュ」は頭打ちになったが、続いて **`C` オーバーレイで「読み込み中（loading）の写真」が増え続ける**ことを観測。
  **真因＝`PreviewBitmapCache` の進行中ロード（`_inflight`）に同時実行数・本数の上限が無く、`Trim` は完了済み `_cache` のみ破棄して
  `_inflight` には手を付けない**こと。押しっぱなしで通過した写真ごとに `GetAsync`→`LoadCoreAsync` が起動し、多数のデコードが
  並行して各々ファイル全バイトの `byte[]`（JPEG 数MB）＋デコード面を抱える＝loading とメモリが増え続けた。
  - **対策2案を別ブランチで実装し比較（ユーザーが案2を採用）**:
    - 案1 `feature/inflight-cancel`（`d027b8a`・プッシュ済み）＝各 inflight に `CancellationTokenSource` を紐付け、`Trim` で保持窓外を
      `Cancel`（`File.ReadAllBytesAsync(ct)`/`CanvasBitmap.LoadAsync(...).AsTask(ct)` へ伝播）。走り出したロードを途中で打ち切る。
    - **案2 `feature/inflight-gate`（採用・main へマージ）＝同時実行ゲート＋窓外バイパス**。重い「バイト読み込み＋デコード」を
      `SemaphoreSlim(MaxConcurrentDecodes=2)` で同時2本に制限。ゲート取得時点で `IsWanted`（現在の保持窓内か）を評価し、外れていれば
      **`byte[]` を確保せず即破棄**。押しっぱなしで通過した写真はゲート順が回る頃には窓外になっているので、メモリを使わず安価に捨てられる
      （実デコードは着地写真＋近傍のみ＝同時 `byte[]` は最大2本に固定）。`IsWanted` は `PreviewControl` が `IsPathInWindow`（`WindowPaths()`
      メンバー判定）で設定。
  - **案2 採用の理由**: 「そもそもデコードを始めない」ぶん無駄CPUが少なく、同時 `byte[]` 上限が固定でメモリのピークが読みやすい。
    案1 はデコード終盤でのキャンセルだと読み込み済みバイトが無駄になり、キャンセル伝播が効くまで `byte[]` が一瞬残りうる。
  - **オーバーレイで waiting/loading を区別（`494a0cd`）**: ゲート取得済み＝実読み込み中のパスを `_loading`（`HashSet`）で追跡し、
    `SnapshotFileNames` で **ゲート取得済み=`(loading)`／ゲート順番待ち=`(waiting)`** と表示し分け。待機中→読み込み中の遷移は
    `_inflight` の増減を伴わないため、遷移時に `Changed` を発火してオーバーレイを更新する（変数名は従来表現の踏襲＋ファイル読み込みも
    兼ねるため `_loading`）。辞書/集合操作はすべて UI スレッド上の継続（`ConfigureAwait(false)` 不使用）なのでロック不要。
  - 変更（案2）: `Controls/PreviewBitmapCache.cs`（ゲート＋`IsWanted`＋`_loading`）・`Controls/PreviewControl.xaml.cs`（`IsWanted` 設定＋
    `IsPathInWindow`）。**Core・XAML は非変更**。`BUILD SUCCEEDED`（x64 Release・警告0）。`C` オーバーレイで「`(waiting)` が一瞬並ぶ→
    ゲートを通った最大2枚が `(loading)`→着地後にデコード済みへ昇格」をユーザー確認済み（2026-06-30）。案1 ブランチは比較用に残置。
  - **オーバーレイの状態色分け（main・2026-06-30）**: `C` オーバーレイを状態別の文字色に。**cached=白 `#E8FFFFFF`／
    loading=緑系 `#FF7CE38B`／waiting=灰系 `#FF9AA0A6`**。
    実装＝`Snapshot()` が `(string Name, CacheItemState State)`（enum＝`Cached`/`Loading`/`Waiting`、**色は持たず UI 非依存**）を
    返し、`PreviewControl` 側で状態→（接尾辞＋ブラシ）にマッピングして表示項目 `CacheEntry`（`Text`＋`Brush Foreground`）を構築。
    XAML の DataTemplate を `x:String`→`ctl:CacheEntry`（`Foreground="{x:Bind Foreground}"`）に。
    - **落とし穴**: 表示項目をポジショナル `record` にすると `init` 専用プロパティになり、XAML 生成（`XamlTypeInfo.g.cs` の
      setter 代入）と衝突して **CS8852** でビルド失敗。→ **get-only クラス**にして読み取り専用 OneWay バインドへ揃える。
    変更: `Controls/PreviewBitmapCache.cs`（`SnapshotFileNames`→`Snapshot()`＋`CacheItemState`）・`Controls/PreviewControl.xaml(.cs)`。
  - **オーバーレイを状態でグループ化（main・2026-06-30）**: 「loading の上に waiting が混ざる」現象を整理。**真因＝表示順が
    `_inflight`（`Dictionary`）の列挙順で、Dictionary は挿入順を保証しない**（追加/削除churnでフリーリストのスロット再利用が起き
    崩れる）こと。ゲート（`SemaphoreSlim`）の取得自体は概ね FIFO で問題なし＝**表示順がそれを反映していないだけ**だった。
    対策＝`Snapshot()` の**最終段で状態ソート**（`OrderBy((int)State)`＝enum 宣言順 `cached→loading→waiting`。安定ソートで
    グループ内の相対順は保持）。**`_inflight`/`_loading` 等の状態管理コレクションは無変更**（並べ替えは表示用スナップショットのみ）。
    変更: `Controls/PreviewBitmapCache.cs`（`Snapshot()` 末尾の `OrderBy` 1行）。

- **左右キー連打でビデオメモリ（VRAM）が増え続けるリーク 修正完了（2026-07-01）**: 「先読みキャッシュの件数は頭打ちなのに、
  ←/→ を押し続けると VRAM が毎秒約2GB のペースで増え続け、離すと解放される（メインメモリは横ばい）」不具合を解消。
  **真因＝フル解像度 `CanvasBitmap` の生成レート飽和**（Dispose しても回収がレートに追いつかず、ドライバがコミット済み VRAM を溜め込む）。
  発生源そのものを断つ throttle で対処。
  - **切り分けの経緯（重要）**: ①キャッシュは `Trim`/`Clear` で `Dispose()` 済み＝件数は頭打ち。②規模で犯人を特定＝
    **2GB/秒 ÷ 約200MB/枚（α1 8640×5760×4＝BGRA8）≒ 毎秒10枚**でフル解像度デコード相当。サムネイル（140px≒52KB/枚）では
    毎秒4万枚必要＝4桁足りず**サムネイルは原因になり得ない**。③診断で `Trim` に `GC.Collect()`＋`WaitForPendingFinalizers()` を
    一時挿入 → **連打中の増加は止まらず**＝finalizer 保持ではなく**生成レート飽和**（GC では解決不可）と確定。診断コードは撤去済み。
  - **なぜ churn するか**: キーのオートリピート（毎秒20〜30）が毎回 `FocusedPhoto` を変え → `LoadCurrentAsync`→`GetAsync`で
    200MB デコード。着地ごとの `Prefetch(WindowPaths())`（前1後2）が要求を約4倍に増幅。`SemaphoreSlim` ゲート（同時2）は
    **同時実行数を絞るだけで累積レートは絞らない**ため、パイプラインが飽和し続ける。加えて `LoadCoreAsync` は
    `ConfigureAwait(false)` を持たず（サムネイル側とは非対称）、継続・`Trim`/`Dispose`・描画が UI スレッドに戻るため、
    生成が回収を追い越す。
  - **対策＝発生源を断つ throttle（`PreviewControl.RequestPreviewLoad`）**: `FocusedPhoto` 変更を直接ロードせず throttle 経由に。
    リーディング（先頭即時＝遅延なし）＋周期（連打継続中は数秒に1回だけデコードして中間フィードバック。既定
    `LoadThrottleInterval=2000ms`）＋トレーリング（停止後 `LoadSettleDelay=150ms` に最終位置を確定デコード＋近傍先読み。
    `DispatcherQueueTimer`）。連打中メインは直前の画像のまま（選択位置はフィルムストリップのハイライトで分かる）。
    先読み（`Prefetch`）は連打中の間引きロードでは走らせず（`LoadCurrentAsync(prefetch:false)`）、settle の確定ロードでのみ実行。
    これで連打中のフル解像度デコードは「先頭1＋2秒に1＋末尾1」に激減＝生成レート約100MB/秒（従来の1/20）で VRAM は増えなくなる。
  - **通常の連続切替が遅延する回帰への追修正（同日・重要）**: 上記だけだと throttle が**キャッシュ済み（＝デコード不要）の写真まで
    待たせて**しまい、「短時間に連続で ←/→ を叩くと2枚目以降が150ms 遅延」という快適度低下が出た。→ **キャッシュ済みは throttle を
    無視して即表示**に修正（`PreviewBitmapCache.IsCached(path)` を追加し、`RequestPreviewLoad` 冒頭でヒットなら即
    `LoadCurrentAsync(prefetch:false)`＋settle 張り直しで return）。デコード不要＝VRAM を生成しないので待たせる理由がない。
    通常のゆっくりした前後移動は settle 先読みで近傍が温まっているため2枚目以降も遅延なく出て、連打（窓が追いつかず未キャッシュ連発）
    だけが従来どおり抑制される。
  - **入場/デバイス再生成は throttle をバイパス**（`IsPreviewMode`入場・`ResetCacheAndReload` は `_settleTimer?.Stop()`＋
    `_lastFullLoadUtc=MinValue` で即時ロード＝入場後の最初のナビも必ず先頭＝即デコード）。
  - 調整ノブ: `LoadThrottleInterval`（連打中の周期）／`LoadSettleDelay`（停止後の確定待ち）。効かない/遅い場合は先読み窓
    `PrefetchForward`/`PrefetchBackward` を広げる手もある。
  - 変更: `Controls/PreviewControl.xaml.cs`（throttle 一式・`LoadCurrentAsync` に `prefetch` 引数）・
    `Controls/PreviewBitmapCache.cs`（`IsCached` 追加）。**Core・XAML は非変更**。`BUILD SUCCEEDED`（x64 Release・警告0）。
    実機で「連打中 VRAM 非増加」「通常の連続切替が遅延なし」「先頭即時・連打中は数秒周期・停止後に最終位置」をユーザー確認済み（2026-07-01）。
  - **時間ベース → レートベースへ改良（`2d2c48c` の後・2026-07-02）**: 上記 throttle は「前回デコードからの経過時間
    （`LoadThrottleInterval=2s`）」で判定していたため、**Ctrl+Space の複数選択などで場所が離れた（＝キャッシュにない）ファイルへ
    左右キーで数枚ジャンプすると、2枚目以降が throttle に引っかかって遅延**する不満が残った（連発ではないのに抑制される）。
    → 未キャッシュ画像の読み込み判定を**「直近 `RateWindow`（1500ms）内のデコード回数」で絞るレート制限**に変更:
    窓内のデコード回数が `RateBudget`（3枚）未満なら即デコード、超過（押しっぱなしの大量連発）なら間引き。
    `Queue<DateTime> _recentDecodes`＋`PrunedDecodeCount(now)`（窓外を捨てて残数を返す）で実装。旧
    `_lastFullLoadUtc`/`LoadThrottleInterval` は撤去。周期デコード（数秒に1回）はレート窓が中間フィードバックを兼ねるため不要に。
    キャッシュ済み即表示・停止後 settle 確定＋先読み・入場/デバイス再生成のバイパスは踏襲（バイパスは `_recentDecodes.Clear()` に変更）。
    settle の確定デコードも未キャッシュ時はレート窓に計上。
    - **利点**: ①離れたファイルへ数枚ジャンプ／通常の連続切替は遅延なく通る ②経過時間でなく**回数**で絞るので
      **キーリピート速度の OS 設定に依存しない**（時間間隔で「連打か」を当てる方式は遅いリピート設定で誤判定し得る）。
    - **トレードオフ**: 純レート制限なので持続レート上限 ≒ `RateBudget ÷ RateWindow`（許容バーストと連動）。VRAM がまだ増えるなら
      `RateBudget` を下げる／`RateWindow` を延ばす。数枚ジャンプで遅延を感じるなら逆に緩める（調整ノブ）。
    - 変更: `Controls/PreviewControl.xaml.cs` のみ。**Core・XAML・`PreviewBitmapCache` は非変更**。`BUILD SUCCEEDED`（x64 Release・警告0）。
      実機で「離れたファイルへの数枚ジャンプが遅延なし」「連打中 VRAM 非増加」「通常連続切替が快適」をユーザー確認済み（2026-07-02）。

- **publish 出力に LICENSE／THIRD-PARTY-NOTICES.txt が出ない不具合 修正完了（2026-07-02）**: `dotnet publish`（単一ファイル・
  フォルダ両プロファイル）の出力フォルダに `LICENSE`/`THIRD-PARTY-NOTICES.txt` が配置されていなかった問題を解消。
  - **真因**: 既存の `Content`（`CopyToOutputDirectory=PreserveNewest`）は `dotnet build` 出力（`bin\…\win-x64\` 直下）へは
    コピーされるが、本 csproj は `EnableMsixTooling=true`（WinAppSDK/MSIX の publish パイプライン）が有効なため、**publish では
    標準の `CopyToPublishDirectory` によるファイル解決が効かず**両ライセンス文が publish 出力に入っていなかった（実発行で再現確認。
    `CopyToPublishDirectory` を Content に明示追加しても改善せず）。＝アプリ内表示用の `EmbeddedResource` 埋め込みは効いていたが、
    **配布物同梱＝法的要件のほうの loose ファイルが欠落**していた。
  - **修正（パイプライン非依存の `AfterTargets="Publish"` Target）**: csproj に
    `<Target Name="CopyLicenseFilesToPublishDir" AfterTargets="Publish"><Copy SourceFiles="..\..\LICENSE;..\..\THIRD-PARTY-NOTICES.txt"
    DestinationFolder="$(PublishDir)" SkipUnchangedFiles="true" /></Target>` を追加。`Publish` 完了後に `$(PublishDir)` へ直接コピー
    するのでフォルダ配布／単一ファイルのどちらでも確実に配置される。既存の Content（`CopyToOutputDirectory`＝build 出力用・実害なし）と
    `EmbeddedResource`（アプリ内表示用）はそのまま残置。`CopyToPublishDirectory` も対で残したが効果はなし。
  - **検証**: `win-x64-singlefile`／`win-x64` 両プロファイルを実発行し、各 publish フォルダ（`…\win-x64\publish-singlefile\`／
    `…\win-x64\publish\`）に両ファイルが出力されることを確認済み。変更: `PhotoQuickSelector.App.csproj` のみ（`CopyToPublishDirectory`
    2 行＋Copy Target）。**Core・アプリコードは非変更**。

- **左ペインの「更新」ボタン除去＋空白右クリック「ドライブ一覧を更新」追加 完了（2026-07-02・要実機確認）**:
  「フォルダ単位の更新は右クリック『更新』で十分」というユーザー判断で `FolderNavigationView` 下部の更新ボタンを除去。
  ボタン除去で唯一到達不能になる「ドライブ一覧の再列挙（USB 抜き差し等）」への導線として、**`TreeView` 自体に
  `ContextFlyout`（空白部分の右クリック→「ドライブ一覧を更新」→ `RefreshDrives()`）** を追加。ノード上の右クリックは
  内側（DataTemplate の `StackPanel`）の ContextFlyout が優先されるため従来のノードメニューのまま。
  **F5（`FolderTree_KeyDown`→`RefreshSelectedOrDrives`）は残置**＝選択ノード更新／選択なしならドライブ一覧更新。
  変更: `Controls/FolderNavigationView.xaml(.cs)` のみ（`RefreshButton`/`RefreshButton_Click` 撤去・
  `RefreshDrivesMenuItem_Click` 追加）。**Core・ViewModel は非変更**。`BUILD SUCCEEDED`（x64 Release・警告0）。
  実機目視（空白右クリックでメニュー表示・ノード右クリックが従来どおり・F5 動作）はユーザー確認推奨。

- **バージョンを 0.1.0 に設定＋初回公開向け発行 完了（2026-07-02）**: 公開（GitHub 等）に向けてバージョンを **1.0.0 → 0.1.0**
  へ変更。バージョン情報ダイアログ（About）がリフレクションで参照する単一情報源 `csproj` の `<Version>` と、packaged 開発用
  マニフェスト `Package.appxmanifest` の `Version`（`0.1.0.0`）を両方更新。配布形態は SPEC §0 の unpackaged 自己完結 EXE のため、
  実配布物は `dotnet publish -c Release -p:Platform=x64 -p:PublishProfile=win-x64-singlefile`（単一ファイル）で発行。
  - **落とし穴（再確認）**: `Publish.ps1` は日本語コメント入り UTF-8(BOMなし)のため **Windows PowerShell 5.1 が ANSI 誤読して
    パースエラー**になる（`generate-app-icon.ps1` と同じ現象）。発行はスクリプトを介さず `dotnet publish` を直接叩くのが確実。
  - 変更: `PhotoQuickSelector.App.csproj`（`<Version>0.1.0</Version>`）、`Package.appxmanifest`（`Version="0.1.0.0"`）。
    **Core・アプリコードは非変更**。

- **ショートカット一覧を JSON へ SSOT 化 完了（2026-07-02）**: 従来 `ShortcutCheatSheet.cs` にハードコードしていたキー一覧を、
  リポジトリ直下 `shortcuts.json`（唯一の情報源）へ移設。**アプリ内 F1 は埋め込み JSON を実行時パースして表示／ドキュメント
  `docs/SHORTCUTS.md` は同 JSON から生成**。README とショートカット一覧を人が編集する運用に合わせ、編集点を `shortcuts.json` 1 つに集約。
  - **JSON 形（ルート＝オブジェクト）**: `title`/`keysLabel`/`descriptionLabel`（＝生成 md 用のメタ）＋`groups[]`（各 `title`＋`items[]`＝
    `keys`/`description`）。**グループ表示順は配列順**（全般→表示→移動→評価→複数選択→ファイル連携を維持）。
  - **埋め込み＆読込**: csproj に `<EmbeddedResource Include="..\..\shortcuts.json" LogicalName="shortcuts.json" />`（LICENSE 等と同方式）。
    `ShortcutCheatSheet.Groups` を静的ハードコード → `Assembly.GetManifestResourceStream("shortcuts.json")`＋
    `JsonSerializer.Deserialize<CheatSheetData>`（`PropertyNameCaseInsensitive=true`）へ。**型 `ShortcutGroup`/`ShortcutItem`・
    `Groups` プロパティ名・`ShortcutsDialog.xaml`（`{Binding Title/Items/Keys/Description}`）は不変**＝ダイアログ無変更・低リスク。
    トリミング無効（`PublishTrimmed=false`）なのでリフレクション逆シリアライズは安全。読み込み失敗時は 1 項目の代替表示（`Fallback`）でクラッシュ回避。
  - **md 生成（`tools/gen-shortcuts.ps1`・新規）**: `shortcuts.json`（UTF-8）→ `docs/SHORTCUTS.md`（UTF-8 no BOM）を生成。**編集したら手動実行**
    （`generate-app-icon.ps1` と同じ流儀）。**スクリプトは ASCII のみ**＝日本語は全て JSON から実行時に読む（`Get-Content -Raw -Encoding UTF8`）。
    これで PS 5.1 の「UTF-8 no BOM を ANSI 誤読してパースエラー」を回避（`generate-app-icon.ps1`/`Publish.ps1` の教訓）。
  - **検証**: `dotnet build`（x64 Release・警告0）／ビルド済みアセンブリの `GetManifestResourceNames()` に `shortcuts.json` を確認／
    生成 md の日本語表示を確認。実機 F1 の目視はユーザー確認推奨。**Core 非変更**。
  - 変更/新規: `shortcuts.json`（新規・リポジトリ直下）、`tools/gen-shortcuts.ps1`（新規）、`docs/SHORTCUTS.md`（生成物）、
    `ShortcutCheatSheet.cs`（ローダー化）、`PhotoQuickSelector.App.csproj`（EmbeddedResource）。

- **README（公開向け）＋スクリーンショット 完了（2026-07-03）**: GitHub 公開に向け `README.md`（日本語）を新規作成。構成＝一言紹介＋
  スクショ／特長／動作環境／インストール・起動（Releases＋SmartScreen 注意）／使い方（開く→評価→プレビュー→絞込→書き出し）／
  ショートカット（抜粋表＋アプリ内 `F1`・`docs/SHORTCUTS.md` へ誘導）／ビルド（`dotnet run`/`dotnet test`/publish）／ライセンス。
  ショートカット一覧は README で完結させず **`shortcuts.json` を唯一の正**とし、README は抜粋のみ・全量は `F1`／`docs/SHORTCUTS.md` へ誘導。
  - **スクリーンショット**: `docs/images/` に 2 枚。`screenshot-preview.png`（プレビュー：左ツリー＋大画面＋ルーペ/ナビ＋フィルムストリップ＋
    メタ情報バー）／`screenshot-grid.png`（グリッド：★/旗/カラードット/採用拒否/選択強調）。撮影は computer-use で実アプリを最大化 →
    PowerShell の `Screen.PrimaryScreen.WorkingArea` を `CopyFromScreen`（＝タスクバー除外・フル解像度）で `docs/images` へ直接保存。
    **ユーザーが後日、任意のスクショへ差し替え済み**（README の参照はそのまま `docs/images/screenshot-preview.png` / `-grid.png`）。
  - **README は人が編集する前提**（ショートカット一覧同様）。文言・画像はユーザーが随時修正。Core/アプリコードは非変更（ドキュメントのみ）。
  - 変更/新規: `README.md`（新規）、`docs/images/*.png`（新規）。

- **英語表示対応（日英ローカライズ）完了（2026-07-04・要実機確認）**: UI 全文字列を日英 2 言語化。
  方式＝**WinUI 標準の MRT Core resw**（`Strings/ja-JP|en-US/Resources.resw`）＋ XAML `x:Uid`＋コード側 `Loc` ヘルパ。
  - **スパイクで確定した重要知見**: resw／x:Uid／`Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride`
    は **packaged・単一ファイル発行の両形態で動作**（resources.pri 埋め込みでも解決）。override は
    **プロセス単位（再起動で消える・永続化なし）**、プロセス内切替は可能だが**一度設定すると解除不可**
    （`""`/`null` 代入は 0x80070057 例外）。→ 設計:「自動」＝override を一切触らない（OS 言語追従）／
    「ja」「en」＝`App` ctor（`InitializeComponent` より前）で設定＝**反映は再起動後**。詳細はメモリ `winui-mrtcore-localization`。
  - **設定**: `AppSettings.Language`（`""`=自動/`"ja"`/`"en"`）。設定ダイアログ先頭に言語コンボ
    （自動/日本語/English＋「再起動後に反映」注記）。保存はメニュー＞設定…の Primary（`PhotoStatusBar`）。
  - **XAML（9 ファイル・約 88 箇所）**: 命名規約 `<Control>_<Element>.<Property>`（例 `Filter_Header.Text`）。
    **日本語リテラルは残置**（デザイナ用フォールバック。実行時は resw が上書き）。ToolTip は resw 側で
    `Uid.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip` 構文。ContentDialog はルート x:Uid で
    `.Title`/`.PrimaryButtonText`/`.CloseButtonText`。同一文言は x:Uid 共有可（例 `Common_BrowseButton`／
    お気に入り追加/削除メニュー）。**`RadioButtons` の `x:String` 項目は x:Uid 不可**→ `RadioButton` 要素へ置換
    （`CopyRenameDialog`。`SelectedIndex` ベースの Policy 判定は不変）。
  - **コード側**: 新規 `Loc.cs`＝`Loc.Get(key[, args])`（`ResourceLoader` ラップ。未解決はキー自身を返す＝空欄防止）。
    対象: `MainViewModel`（StatusText 系）、`MainPage`（sqlite 作成確認・前回フォルダ不明）、`FilterBar`
    （Reject 移動/リネームコピーの全ダイアログ）、`CopyRenameDialog`（InfoBar/プレビュー件数）、`AboutDialog`
    （バージョン表記）、`LicenseDialog`（読込失敗）、`PhotoStatusBar`（左ペイン開閉ツールチップ）、
    `PhotoItemViewModel`（GPS ツールチップ）。フォーマットは resw に `{0}` で保持（literal `{}` は `{{}}` エスケープ）。
    x:Uid キーをコードから読む場合はパス区切り `/`（例 `Loc.Get("CopyRename_DuplicateWarning/Title")`）。
  - **shortcuts.json（SSOT 維持のまま多言語化）**: 各テキストを「文字列（全言語共通）or `{"ja":…,"en":…}`」に拡張。
    `ShortcutCheatSheet` は `JsonNode` パースへ変更し、**言語判定は resw の `LangCode` キー**（ja-JP=`ja`/en-US=`en`）＝
    resw の解決結果と常に一致。`tools/gen-shortcuts.ps1` は **`docs/SHORTCUTS.md`（日・既存名維持）＋
    `docs/SHORTCUTS.en.md`（英）の 2 枚生成**に更新（実行済み・ASCII-only 維持）。
  - **README**: `README.en.md` 新規（英語版・`SHORTCUTS.en.md` へ誘導）＋日英相互リンク＋日本語版の特長に言語対応 1 行。
  - **検証**: `BUILD SUCCEEDED`（x64 Release・警告0）／`dotnet test` **96 件緑**／packaged（`dotnet run`）と
    単一ファイル発行の両方で `Language="en"` 起動 15 秒生存（クラッシュなし＝x:Uid/attached-property 構文 OK）。
    設定 json は検証後に復元済み。**実機目視（英語 UI の見た目・各ダイアログ・F1 の英語表示・設定コンボでの切替→
    再起動反映）はユーザー確認推奨**。
  - 変更/新規: `Strings/ja-JP|en-US/Resources.resw`（新規）、`Loc.cs`（新規）、`README.en.md`（新規）、
    `docs/SHORTCUTS.en.md`（生成物・新規）、`App.xaml.cs`、`AppSettings.cs`、`ShortcutCheatSheet.cs`、`shortcuts.json`、
    `tools/gen-shortcuts.ps1`、`README.md`、XAML 9 ファイル＋対応コードビハインド、`ViewModels/MainViewModel.cs`・
    `PhotoItemViewModel.cs`、`MainPage.xaml.cs`。**Core は非変更**。

## 残タスク（次の候補）
- ~~プレビューのキーボード入力フォーカス問題~~ → **完了（`f54d9b4`）。** 上の「現在の進捗」参照。
- ~~Phase 3 ステージ B 残: 右ナビゲーター／ズームプレビュー／`Ctrl+Alt+矢印`／`Ctrl+Alt+F`~~ → **完了（`993c7c2` プッシュ済み）。**
  残微調整: ルーペのロード時センタリングは初期レイアウト未確定だと AF 点がやや上寄りになる場合あり（軽微）。
  AF 枠の正確な位置（回転画像）はユーザー最終確認推奨。
- ~~Phase 4-A: フィルタ／クリップボード出力~~ → **完了（`c073853`）。** 上の「現在の進捗」参照。
- ~~Phase 4-B: 外部連携（`Ctrl+E`／`Alt+E`／`Ctrl+Alt+E`／`Alt+S`）＋設定（`AppSettings.SharePath` 設定化・歯車→設定ダイアログ）~~
  → **完了（2026-06-26）。** 上の「現在の進捗」参照。`M`（デバッグ GC）は SPEC 通り未実装。
- ~~パッケージング: 素の自己完結 EXE の publish 構成を組み込み＋発行確認~~ → **完了（2026-06-20）。** 上の「現在の進捗」参照。
  pubxml 2 系統（フォルダ／単一ファイル）＋`Publish.ps1`。実発行・起動確認済み（コミット済み）。

## キー操作（右ペイン・写真選択時）
- `0`–`5` レーティング / `6`–`9`＋`P` カラーラベル（赤黄緑青紫。7=`ColorLabel.Yellow`＝黄 `#FDD835`）/ `[` `]` レーティング増減 / `Ctrl+↑/↓` フラグ
  （複数選択中でも**通常評価は焦点の1枚のみ**に反映）
- 複数選択（両モード共通。焦点＝常に1枚／選択集合＝0..N枚で別概念）:
  `Shift+←/→` レンジ選択（起点から焦点までを連続選択）/ `Ctrl+←/→` 焦点のみ移動（集合は不変）/
  `Ctrl+Space` 焦点を選択集合へ参加/解除 / 選択集合中の `←/→` はメンバー内で焦点巡回 / `Esc` 選択集合を解除
- `Alt+0`–`5`／`Alt+6`–`9`／`Alt+P` 一括評価（選択集合の全メンバーへ。レーティング/カラーラベル）
- `Ctrl+Alt+↑/↓` 一括フラグ（選択集合の全メンバーへ。単一フラグ `Ctrl+↑↓` の対称形）。**プレビューでは選択集合がある
  ときのみ一括フラグ／無いときは従来のルーペ縦スクロール**。集合が無ければ一括系は無効
- `Ctrl+L` フィルタ ON/OFF トグル（両モード共通、フライアウトは開かない）
- `Ctrl+E` エクスプローラで表示 / `Alt+E` 既定アプリで開く / `Ctrl+Alt+E` パスをコピー / `Alt+S` 共有
  （両モード共通。共有は `AppSettings.SharePath` 設定時はその exe 起動、未設定なら Windows 標準共有シート。設定はステータスバー右端のメニュー＞設定…から）
- ステータスバー右端の**ハンバーガーメニュー**（`&#xE700;`）から上記トグル/外部連携/設定をクリック実行も可（ショートカット併記）
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
- プレビュー中: `G` 構図グリッド種類を巡回（None→中央十字→三分割→正方形→None）/ `Shift+G` グリッド基準を切替
  （画像⇄Canvas）。正方形は短辺を N 等分した正方セルを画像中央から対称配置（N＝`AppSettings.GridSquareDivisions`・既定8。
  偶数Nは中央に線・奇数Nは中央線なし）。種類/基準は `AppSettings` に永続化（次回起動で復元）

## 既知の注意点
- 検証で `DSC09432.JPG` の rating が null→0 に変わっている（実効値は同じ）。
- コミット時の `LF→CRLF` 警告は無害（Windows の改行正規化）。
- WinUI TreeView は子コレクションの `Clear()`→全件再追加で内部状態が壊れる。**差分同期で更新する**こと。

### 開発フローのハマりどころ（本セッションで判明）
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
