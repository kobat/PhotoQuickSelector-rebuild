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
  - **可視分デコード**: 新規 `Controls/ThumbnailContainerLoader.cs`（静的ヘルパ）。`ContainerContentChanging` で
    `InRecycleQueue`→`Image.Source=null`（解放）／実体化時→`img.Tag=vm` トークン付きで `CreateThumbnailImageAsync` を
    await し、Tag が一致する時だけ Source 設定（リサイクル先取り対策）。グリッド（`PhotoGridView`、幅 200）と
    フィルムストリップ（`PreviewControl`、幅 90）の両方から呼ぶ。両 XAML の `<Image>` は **Source バインドを外し
    `x:Name`（`ThumbImage`/`FilmThumbImage`）付与**、バッジの `x:Bind` は不変・`args.Handled` は立てない（phase 処理と共存）。
  - メモリ概算（1 万枚）: 常駐 ≈300MB（圧縮バイト）＋ 可視コンテナ数十枚分の小さな非圧縮サーフェスのみ。
  - **Core・選択同期・評価バッジ部品は非変更**。`BUILD SUCCEEDED`／`dotnet test` 68 件緑。
    実機（テストフォルダ 71 枚・x64 ビルド）で グリッド表示／スクロール再デコード／プレビュー入場／フィルムストリップ表示を
    目視確認済み（2026-06-20）。大量枚数フォルダでのメモリ頭打ちはユーザー確認推奨。

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
