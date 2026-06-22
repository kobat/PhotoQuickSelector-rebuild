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
  - `tests/PhotoQuickSelector.Core.Tests/` … xUnit（46 件）

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

## 残タスク（次の候補）
- ~~プレビューのキーボード入力フォーカス問題~~ → **完了（`f54d9b4`）。** 上の「現在の進捗」参照。
- ~~Phase 3 ステージ B 残: 右ナビゲーター／ズームプレビュー／`Ctrl+Alt+矢印`／`Ctrl+Alt+F`~~ → **完了（未コミット）。**
  残微調整: ルーペのロード時センタリングは初期レイアウト未確定だと AF 点がやや上寄りになる場合あり（軽微）。
  AF 枠の正確な位置（回転画像）はユーザー最終確認推奨。
- ~~Phase 4-A: フィルタ／クリップボード出力~~ → **完了（`c073853`）。** 上の「現在の進捗」参照。
- Phase 4-B: 外部連携（`Ctrl+E`=エクスプローラ `/select`／`Alt+E`=既定アプリ／`Ctrl+Alt+E`=パスをコピー／
  `Alt+S`=共有）＋設定（**共有先パスを `AppSettings.SharePath` に設定化**＝SPEC §6-3。歯車ボタン→設定ダイアログ）。
  実装方針: `PhotoFileCommands.TryHandle(key, modifiers, item, settings)` を共有し `MainPage.HandleGlobalKeyDown`
  （サムネイル）と `PreviewControl.HandleKeyDown`（プレビュー）の両方から呼ぶ。`M`（デバッグ GC）は実装しない（SPEC §3-7）。
- ~~パッケージング: 素の自己完結 EXE の publish 構成を組み込み＋発行確認~~ → **完了（2026-06-20）。** 上の「現在の進捗」参照。
  pubxml 2 系統（フォルダ／単一ファイル）＋`Publish.ps1`。実発行・起動確認済み（未コミット）。

## キー操作（右ペイン・写真選択時）
- `0`–`5` レーティング / `6`–`9`＋`P` カラーラベル（赤橙緑青紫）/ `[` `]` レーティング増減 / `Ctrl+↑/↓` フラグ
- `Ctrl+L` フィルタ ON/OFF トグル（両モード共通、フライアウトは開かない）
- `F11` フルスクリーン表示トグル（ステータスバー右端の全画面ボタンも同じ）/ `Esc` 全画面中なら通常表示へ復帰
  （全画面でない通常時の `Esc` は無反応＝プレビューを抜けない。プレビュー終了はダブルクリック）
- プレビュー中: `←`/`→` 前後移動（移動後フォーカスはフィルムストリップへ移り `PageUp`/`PageDown`/`Home`/`End` が効く）
- プレビュー中: `Z` フィット⇄ズームトグル（ズーム側は**直近のズーム位置=倍率/中心を復元**。初回は等倍＝DPI考慮の
  1画像px=1物理px＝100%）/ `Shift+Z` 等倍 / `Shift+Alt+←/→` フィット/等倍 / ホイール ズーム。倍率はステータスバー
  右端に表示（ピクセル等倍＝100%）。拡大率により補間自動切替（等倍以上＝NearestNeighbor／縮小＝HighQualityCubic）
- プレビュー中: `I` メタ情報オーバーレイ（案B）トグル / `G` 三分割グリッド線 / `C` 先読みキャッシュ一覧オーバーレイ（デバッグ・初期非表示）

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
