# 実装経緯の詳細（HISTORY）

PhotoQuickSelector-rebuild の機能ごとの実装経緯・設計判断・落とし穴・実測値・コミットハッシュの記録。
CLAUDE.md の旧「現在の進捗」節を移設したもの（並びは時系列・古い順）。

- **既存機能を修正・拡張する時は、着手前に該当機能の節を読むこと**（機能名で Grep すると該当節が拾える）。
- 新しい作業の記録は本ファイルの「進捗記録」末尾に追記する。CLAUDE.md 側は「現在の状態（要約）」を数行更新するだけにとどめる。

## 進捗記録（時系列）
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
    共有2系統をユーザー確認済み（2026-06-26）。`M`（デバッグ GC）は当時 SPEC 通り未実装
    （後日実装＝「デバッグ用の強制 GC（`M` キー）とメモリ現況表示」節）。

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

- **フィルムストリップ/グリッドの複数選択 完了（2026-06-27・実機確認済み 2026-07-06）**: 「焦点（常に1枚）」と「選択集合（0..N枚＝一括評価対象）」を
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

- **構図グリッドの種類追加＋基準切替 完了（2026-06-27・実機確認済み 2026-07-06）**: プレビューの「三分割グリッド線」固定トグルを、
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

- **左ペインの「更新」ボタン除去＋空白右クリック「ドライブ一覧を更新」追加 完了（2026-07-02・実機確認済み 2026-07-06）**:
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

- **英語表示対応（日英ローカライズ）完了（2026-07-04・実機確認済み 2026-07-06）**: UI 全文字列を日英 2 言語化。
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

- **先読みキャッシュの SoftwareBitmap 化（VRAM 削減）完了（2026-07-04・実機確認済み 2026-07-06）**: プレビューの先読みキャッシュが
  `CanvasBitmap`（VRAM ≈200MB/枚×保持窓4枚）で GPU メモリを圧迫していたのを解消。旧アプリの「メインメモリに BGRA8 で
  保持→表示時だけ GPU へ生転送」方式を踏襲。**Core・XAML は非変更**。
  - **方式**: `PreviewBitmapCache` の保持型を `CanvasBitmap`→**`SoftwareBitmap`（BGRA8・Premultiplied・メインメモリ常駐）**へ
    変更。デコードは `BitmapDecoder.CreateAsync`＋`GetSoftwareBitmapAsync`（`ExifOrientationMode.RespectExifOrientation`＝
    正立済みピクセルを取得＝従来の `CanvasBitmap.LoadAsync` の自動回転と同じ結果、`ColorManagementMode.ColorManageToSRgb`）。
    ファイルロック回避（バイトを読み切ってからメモリストリーム経由でデコード）はそのまま維持。
    `PreviewControl.LoadCurrentAsync` が表示確定時のみ `CanvasBitmap.CreateFromSoftwareBitmap(MainCanvas, sb)` で GPU へ
    転送（デコード不要の生コピーなので速い）。**表示中 1 枚の `CanvasBitmap`（`_bitmap`）は `PreviewControl` が所有**し、
    差し替え/入れ替え/クリア時に明示 `Dispose()` する（従来はキャッシュ内参照を借用するだけで Dispose 不要だった）。
  - **副作用**: キャッシュがデバイス非依存になったため `PreviewBitmapCache.Trim` の `current` 保護引数（表示中ビットマップの
    Trim 除外）が不要になり削除（`Trim(IEnumerable<string> keep)` のみに）。`ResetCacheAndReload`（デバイス再生成/DPI変更）は
    **`_cache.Clear()` を呼ばなくなった**（SoftwareBitmap はデバイスに紐付かないので破棄不要）。旧デバイスに属する表示中の
    `CanvasBitmap` だけ破棄し、キャッシュヒットの再転送で即復帰する。ゲート（`SemaphoreSlim` 同時2）／`IsWanted`（窓外バイパス）／
    レート制限（`RequestPreviewLoad`）／`C` オーバーレイの状態表示（cached/loading/waiting）は構造そのまま無変更。
  - **VRAM/メモリ収支**: 表示中 1 枚のみ VRAM 常駐（≈200MB。差し替え中の一瞬だけ新旧2枚）。旧来の保持窓4枚ぶんの VRAM
    （≈800MB）は解消。代わりにメインメモリ側で窓4枚ぶんの `SoftwareBitmap`（同程度のサイズ）を保持するトレードオフ
    （メインメモリは一般に潤沢なため許容。窓幅は `PrefetchForward`/`PrefetchBackward` が調整ノブ）。
  - 変更: `Controls/PreviewBitmapCache.cs`（保持型・デコード方式・`Trim` シグネチャ）、`Controls/PreviewControl.xaml.cs`
    （GPU 転送処理・`_bitmap` の所有/Dispose・`ResetCacheAndReload`）。`BUILD SUCCEEDED`（x64 Release・警告0）／
    `dotnet test` 96 件緑。実機目視（色味の一致・縦構図 DSC03334 の回転/AF枠位置・前後移動の体感・タスクマネージャで
    VRAM が頭打ちになること）はユーザー確認推奨。
  - **同寸なら SetPixelBytes で再利用（追記・2026-07-04）**: 写真切替時、新画像が表示中の `_bitmap` と同一寸法なら
    `CanvasBitmap` を作り直さず `TryUpdateBitmapInPlace`＝`SoftwareBitmap.CopyToBuffer`→再利用 `byte[]`
    （`_transferBuffer`・寸法変化時のみ再確保・プレビュー退場で解放）→`SetPixelBytes` で既存ビットマップへ上書き転送。
    VRAM の確保/解放 churn がゼロになる（連写フォルダではほぼ常に同寸）。stride パディングあり・寸法違い・転送失敗時は
    従来の `CreateFromSoftwareBitmap` へフォールバック。Dispose は `ReferenceEquals` ガードで再利用時の自己破棄を防止。
    トレードオフ＝転送時に 1 回のメインメモリ memcpy（≈200MB・十数ms）が挟まるが、GPU リソースの生成/破棄がなくなる
    ぶん安定。変更: `Controls/PreviewControl.xaml.cs` のみ。`BUILD SUCCEEDED`（x64 Release・警告0）／`dotnet test` 96 件緑。
  - **キャッシュ保持型を byte[]（PixelFrame）へ変更（追記・2026-07-04）**: `SetPixelBytes` 再利用時、`SoftwareBitmap`
    保持だと `CopyToBuffer`→転送バッファの CPU memcpy（≈200MB・UI スレッド）が毎切替に挟まっていた。保持型を
    `PixelFrame`（BGRA8 密詰め `byte[]`＋寸法）へ変更し、キャッシュのバイト列を直接 `SetPixelBytes`/`CreateFromBytes`
    へ渡すことで**切替時 CPU コピー 0 回**に。デコードは `BitmapDecoder.GetPixelDataAsync`（`RespectExifOrientation`/
    `ColorManageToSRgb`＝従来と同じ引数）＋`DetachPixelData`（密詰め保証＝stride 検査・`LockBuffer` フォールバック不要）。
    寸法は `OrientedPixelWidth/Height`。`_transferBuffer`（200MB 常駐）と stride 検査を撤去しコードも簡素化。解放は
    GC 任せ（LOH）になるが保持窓 4 枚で有界＝サムネイルの圧縮バイト常駐と同方針。作り直しは `CreateFromBytes`
    （dpi=96 明示・`B8G8R8A8UIntNormalized`）。変更: `Controls/PreviewBitmapCache.cs`・`Controls/PreviewControl.xaml.cs`。
    `BUILD SUCCEEDED`（x64 Release・警告0）／`dotnet test` 96 件緑。実機目視（連写切替の応答性・メモリ/VRAM）は
    ユーザー確認推奨。

- **先読みキャッシュの保持ポリシー改良（2段階LRU＋容量予算＋選択集合対応の先読み窓）完了（2026-07-05・実機確認済み 2026-07-06）**:
  2 枚の写真を左右キーで行き来すると、往復のたびに保持窓（前1/後2）から外れた 1 枚（≈200MB 分のデコード）が
  `Trim` で捨てられ再デコードされる無駄を解消。方針＝**先読みする条件は従来のまま、解放する条件だけ緩める**
  （メモリ増はユーザー許容済みのトレードオフ）。**Core・XAML は非変更**（`Controls/PreviewBitmapCache.cs`・
  `Controls/PreviewControl.xaml.cs` のみ）。
  - **① 表示実績優先の 2 段階 LRU＋バイト予算（`PreviewBitmapCache`）**: `Trim(keep)` の意味を「keep（現在窓）外は
    即破棄」→「**keep は無条件保護＋合計バイトが `MaxCacheBytes`（2GB・調整ノブ）を超えている間だけ、
    未表示（先読みのみ）の古い順 → 表示済みの古い順で破棄**」へ変更。予算内なら窓外でも残すため往復の再デコードが
    消える。エントリは `CacheEntry`（`PixelFrame`＋`LastUse`＝単調増分カウンタ（時計不使用）＋`WasDisplayed`）で包む。
    `GetAsync(path, forDisplay)` にシグネチャ変更＝表示ロード（`LoadCurrentAsync`）だけが `WasDisplayed` を立て、
    `Prefetch` は立てない。inflight 中に表示要求が重なるケースは `_pendingDisplay`（HashSet）に控えて挿入時に反映
    （世代不一致/`IsWanted` 破棄/例外の全経路で Remove しリークなし）。継続はすべて UI スレッド＝ロック不要は従来どおり。
  - **② 選択集合対応の先読み窓（`PreviewControl.WindowPaths()`）**: 選択集合なし＝従来どおり位置窓 前1/後2
    （`PrefetchForward/Backward`）。選択集合あり＝「**位置窓 前1/後1**（`SelectionPosition*`。Ctrl+←/→ の集合外
    移動に備える）」＋「**メンバー窓 前1/後2**（`SelectionMember*`。`MoveFocusWithinSelection` と同じ
    `Photos.Where(IsInSelection)` の表示順＋modulo 巻き戻し。焦点が集合外なら同メソッドの規則どおり →=先頭/←=末尾
    基準）」の**和集合**（OrdinalIgnoreCase で重複除去）。返す順は 焦点→メンバー窓→位置窓＝`Prefetch` のゲート
    （同時2本）で巡回先メンバーを位置近傍より優先。`Prefetch`／`Trim` 保護／`IsWanted`（窓外バイパス）の 3 箇所
    すべてが `WindowPaths()` 経由なので、変更はこの 1 メソッドに集約（呼び出し側は不変）。
  - **期待動作**: 2 枚往復は最初の 1 往復で両側の窓が揃い**以降デコードゼロ**（キャッシュヒット→`SetPixelBytes`
    転送のみ）。選択集合巡回はメンバー先読みで**到達即表示**。メモリは予算 2GB で頭打ち（メインメモリのみ・
    VRAM への影響なし）。レート制限・settle・同時実行ゲート・`C` オーバーレイは構造無変更。
  - `BUILD SUCCEEDED`（x64 Release・警告0）／`dotnet test` 96 件緑。`PreviewBitmapCache` は WinRT imaging 依存で
    Core.Tests（非 Windows TFM）から参照不可＝LRU ロジックの単体テストなし。実機目視（2 枚往復で `C` オーバーレイの
    エントリが減らない・選択巡回の即表示・大量移動時に予算で頭打ち）はユーザー確認推奨。

- **設定画面の拡張（タブ分割＋ズーム倍率＋先読みキャッシュ設定＋グループ別リセット）完了（2026-07-05・実機確認済み 2026-07-06）**:
  設定ダイアログ（`ContentDialog`）を `Pivot` で **一般／高度な設定** の2タブに分割し、設定項目を追加。各設定グループの
  見出し横に **「既定に戻す」HyperlinkButton**（`new AppSettings()` の初期化子＝既定値を控えとして参照）を付けた。**Core 非変更**。
  - **一般タブ**: 表示言語（既存）／**ズーム倍率**（新規）／共有先（既存）。**ズーム倍率＝パーセントのカンマ区切りテキスト**
    （例 `25, 50, 100, 200`）。内部は倍率（DeviceScale）で保持し、UI 表示は `×100`。解析は寛容（`, 空白 、 ％ %` で分割・
    正値のみ・重複除去・昇順、空なら既定へフォールバック）。
  - **高度な設定タブ**: **キャッシュ容量予算(GB)**／**先読み枚数(後方=次へ・前方=前へ)**／**同時デコード数**／
    **連打抑制（枚数＝RateBudget・時間窓ms＝RateWindow）**。数値入力は `NumberBox`（Min/Max/SpinButton 付き）。
  - **設定の追加（`AppSettings`）**: `List<double> ZoomStops`（＋`static IReadOnlyList<double> DefaultZoomStops`）／
    `double CacheBudgetGB=2.0`／`int PrefetchForward=2`/`PrefetchBackward=1`／`int MaxConcurrentDecodes=2`／
    `int RateBudget=3`/`RateWindowMs=1500`。**source-gen コンテキストは `AppSettings` 登録済みで `List<double>` も追加登録不要**
    （`List<string>` と同様に自動対応。build 警告0 で確認）。
  - **決め打ち値の設定化（挙動は既定値で不変）**: `PreviewViewport.ZoomStops`（静的→インスタンス設定可能プロパティ）＋
    `MaxScale`（const→設定可能プロパティ。**ズーム段の最大に追従**＝`Max(16.0, stops[^1])` で 1600% 超の段も弾かれない）。
    `PreviewBitmapCache.MaxCacheBytes`（const→設定可能プロパティ）＋`MaxConcurrentDecodes`（**ctor 引数**。Semaphore は構築時に
    サイズ決定するため変更は再構築が必要）。`PreviewControl` の `PrefetchForward/Backward`・`RateWindow`・`RateBudget`
    （const/static→インスタンスフィールド `_prefetchForward` 等）。
  - **反映経路**: `PreviewControl.ApplyPreviewSettings(AppSettings)`（新規 public）がズーム段・`MaxScale`・先読み枚数・レート・
    キャッシュ予算を注入（すべて即時反映可・妥当性クランプ付き）。**同時デコード数のみ** `RebuildCacheForConcurrency(int)`
    （新規 private・`_cache` を作り直し＋Changed/IsWanted 再配線）で反映するため**次回起動後**（実行中の作り直しは保持中の
    デコード済み画像を失うので避ける）。呼び出しは 2 経路: ①**ViewModel 注入時**（起動時・`ViewModel` setter で
    `RebuildCacheForConcurrency`→`ApplyPreviewSettings`）②**設定保存時**（`PhotoStatusBar` の `MenuSettings_Click` が
    新規 `SettingsChanged` イベント発火→`MainPage` が `Preview.ApplyPreviewSettings` を呼ぶ＝`ToggleFullScreenRequested` 等と
    同じ委譲パターン）。②では同時デコード数は再構築しない＝再起動待ち。ルーペ（`_zoomViewport`）は注入せず既定のまま。
  - **ローカライズ**: `Strings/{ja-JP,en-US}/Resources.resw` に設定系の x:Uid キーを追加（タブ見出し・各グループ見出し/注記・
    「既定に戻す」共有 x:Uid `Settings_ResetGroup`）。XAML には日本語リテラルをフォールバックとして残置（既存方針）。
  - 変更/新規: `AppSettings.cs`、`Controls/PreviewViewport.cs`、`Controls/PreviewBitmapCache.cs`、`Controls/PreviewControl.xaml.cs`、
    `Controls/SettingsDialog.xaml(.cs)`（Pivot 化・全面改訂）、`Controls/PhotoStatusBar.xaml.cs`（`SettingsChanged`＋値の反映）、
    `MainPage.xaml.cs`（配線）、`Strings/*/Resources.resw`。`BUILD SUCCEEDED`（x64 Release・警告0）／`dotnet test` 96 件緑。
    実機目視（タブ切替・ズーム段の反映＝ホイール/`+`/`-`・各リセット・先読み枚数/予算/レートの反映・同時デコード数の再起動反映・
    英語 UI）はユーザー確認推奨。
  - **微修正（2026-07-05・上記の続き）**: ①先読み枚数のラベル訳を訂正（Forward=**前方（次へ）**／Backward=**後方（前へ）**。
    ja resw と `AppSettings` のコメントを入替。en は元から正しく据え置き）。②`PrefetchBackward` の既定を **1→2**
    （`AppSettings` 既定と `PreviewControl._prefetchBackward` の両方）。③ズーム段の既定リストから **75%・125% を除外**
    （新 `5,8.33,12.5,16.67,25,33.33,50,66.67,100,150,200,300,400,600,800,1200,1600`＝17段。`AppSettings.DefaultZoomStops` と
    `PreviewViewport.DefaultZoomStops` の両方を同一に更新）。④キャッシュ予算の MB 化は**見送り**（GB のまま）。
    `PreviewViewportTests` の段スナップ 2 件（0.75 依存）を新リストに合わせ更新（67%→100% へ）。`BUILD SUCCEEDED`
    （x64 Release・警告0）／`dotnet test` 96 件緑。

- **先読みキャッシュのデバッグオーバーレイ表示拡張 完了（2026-07-05・実機確認済み 2026-07-06）**: `C` オーバーレイの各行に
  「容量(MB)・寸法・窓分類・表示実績」を追加し、ヘッダに「合計容量/予算・直近デコード数/レート予算・表示中の VRAM 目安」を集計表示。
  ラベルは後置き・日本語（デバッグ専用のため resw 追加なしのハードコード。例 `DSC03334.JPG   191MB  5760×8640    フォーカス・表示済み`、
  未完了は `（読込中）`/`（待機中）`）。**Core 非変更**（`PreviewBitmapCache.cs`・`PreviewControl.xaml(.cs)` のみ）。
  - **窓分類**: `WindowPaths()` の本体を分類付き `WindowEntries()`（`WindowSlot { Focus, Member, Position }`）へ移し、
    `WindowPaths()` はその Path 射影＝列挙順・内容は完全一致（Prefetch のゲート優先順に影響なし。3 呼び出し点も無変更）。
    ラベルは フォーカス/選択窓/位置窓/窓外（窓辞書に無い=予算内残留）。
  - **並び順＝Trim の破棄優先度の逆順**（上ほど安全・下ほど次に消える）: 窓内（窓の列挙順）→窓外・表示済み（LastUse 降順）→
    窓外・未表示（LastUse 降順）→読込中→待機中。`Snapshot()` は詳細型 `CacheSnapshotItem`（Path/Name/State/Bytes/寸法/
    WasDisplayed/LastUse）返しに変更し、末尾の状態ソートは撤去（並び順の責務は表示側へ。窓分類がそちらにしかないため）。
  - **更新タイミングの穴ふさぎ**: 両隣が全部キャッシュ済みの移動では `Changed` が発火せず窓ラベルが古くなるため、
    `LoadCurrentAsync` の `Trim` 直後 2 箇所で `RefreshCacheOverlay()` を明示呼び。**非表示中は冒頭ガード
    （`Visibility != Visible` で即 return）で整形コストゼロ**（`Snapshot()` 呼び出しも enqueue 後のラムダ内＝表示中のみ）。
  - XAML: オーバーレイ `StackPanel.MaxWidth` 280→460、ヘッダ直下に集計用 `CacheOverlaySummary`（Consolas）追加。
    実装は Sonnet サブエージェントに委譲し差分レビュー済み。`BUILD SUCCEEDED`（x64 Release・警告0）／`dotnet test` 96 件緑。
    実機目視（`C` 表示・窓ラベルのナビ追従・選択集合時の選択窓表示・合計/予算表示）はユーザー確認推奨。
  - **修正（同日・実機確認で発覚）**: 選択集合なしのとき窓の列挙順が index 順（前 N 枚→フォーカス→後 N 枚）のため
    フォーカス行が中程に出ていた。表示側だけ `window.OrderBy(Slot==Focus ? 0 : 1)`（安定ソート）でフォーカスを
    窓内グループの先頭へ寄せた。`WindowEntries()` 自体＝Prefetch のゲート優先順は非変更。
  - **並び順をフィルムストリップ順へ変更（同日・ユーザー要望）**: 上記「破棄優先度の逆順」を廃止し、
    **全項目（読込中・待機中も同列）を `Photos` の表示順**でソート（キャッシュがフィルム上のどの範囲を
    覆っているかを読む用途）。`Photos` に無い残留キャッシュ（フィルタ変更で絞込外れ等）は末尾にファイル名順。
    窓分類/表示実績ラベル・ヘッダ集計・`WindowEntries()` は非変更。

- **縦型画像の表示・パンが遅い問題の調査＋ナビゲーター描画の縮小キャッシュ化 完了（2026-07-06・実機確認済み）**:
  「縦型（Orientation≠1）だけ表示までの時間がわずかに遅い／ズーム中の Alt+矢印パンが引っかかる」問題を調査し、
  パン側の真因を修正した。調査の実測値・手法はメモリ `portrait-slowness-benchmarks` に詳細あり。
  - **調査結果（実測で確定）**: ①パンの引っかかり＝**毎パン（`InvalidateMain`）でナビゲーターが 50MP 全体を
    HighQualityCubic 縮小描画**していたのが真因（実アプリ内ベンチ＝セッション復元＋`CompositionTarget.Rendering` の
    360 フレーム対角パンで、縦 p95 20.8ms・20ms 超が 44/360 → ナビ描画停止で p95 7.2ms と完全平滑化。
    Radeon 890M iGPU・可視領域のみ描画への変更は効果なし＝巨大 dest 矩形説は棄却）。**イマーシブ（F）で畳んでも
    `NavCanvas_Draw` が不可視サーフェスへ描画し続けるバグ**も発見（Collapsed でも Invalidate で Draw 発火・
    ActualWidth が古いまま）＝「畳んでも遅い」の説明。②初回表示・切替の遅れ＝デコード時の **EXIF 回転が縦のみ
    +80〜90ms**（`GetPixelDataAsync` の RespectExifOrientation）＋ **`ColorManageToSRgb` が縦横共通で
    +550〜640ms/枚（デコード全体の約7割）**。GPU 転送・描画自体は縦横対称（オフスクリーン実測）。
  - **修正（ナビ縮小キャッシュ・表示中1枚だけ保持＋遅延再生成）**: `_navBitmap`（`CanvasRenderTarget`・
    `NavCanvas` の DPI で物理解像度生成）＋`_navBitmapDirty`。`NavCanvas_Draw` は有効ならキャッシュを 1:1 描画
    （sub-ms）、無効（切替/リサイズ直後）なら**暫定フレームとして NearestNeighbor でフル解像度を直描き**
    （ユーザー要望＝前の写真を一瞬でも残さない）し、`DispatcherQueuePriority.Low` で HQC 再生成を 1 回だけ
    遅延スケジュール（切替フレームから HQC を追い出す＝切替も僅かに改善）。青枠/緑枠は従来どおり毎フレーム描画。
  - **付随修正**: `NavCanvas_Draw` に `_immersive` ガード（畳み中は描画しない）＋`SetImmersive(false)` 復帰時に
    `NavCanvas.Invalidate()`。dirty 化は `LoadCurrentAsync`（**同寸切替は `SetPixelBytes` で同一インスタンス
    再利用＝参照比較では検出不可のため無条件フラグ**）／photo==null／`ResetCacheAndReload`（旧デバイスの
    リソースなので破棄）の 3 経路。
  - **効果（同ベンチで検証済み）**: フレーム間隔 縦 p95 20.8→**7.3ms**／横 14.2→**7.4ms**（ナビ表示のまま）。
    nav draw は sub-ms。実装は Sonnet サブエージェントに委譲し差分レビュー済み。
  - 変更: `Controls/PreviewControl.Navigator.cs`・`.xaml.cs`・`.Immersive.cs` の 3 ファイルのみ。**Core 非変更**。
    `BUILD SUCCEEDED`（x64 Release・警告0）／`dotnet test` 96 件緑。実機目視（ナビ画質・切替直後の一瞬 NN→HQC
    置換・ナビリサイズ・イマーシブ復帰・縦型パンの滑らかさ）はユーザー確認推奨。
  - **残り（未着手の改善案）**: ~~修正案2＝sRGB 写真の色管理スキップ／修正案3＝レート制限の inflight 誤課金修正~~
    → **同日実装済み（次項）**。候補4＝未回転保持＋描画時 GPU 回転（縦の +80〜90ms 解消・大工事のため後回し）は未着手。

- **デコードの sRGB 色管理スキップ＋レート制限の inflight 誤課金修正（修正案2・3）完了（2026-07-06・実機確認済み）**:
  縦型調査（前項）で判明した「未キャッシュ切替が遅い」「キャッシュにあるはずなのに遅い」の 2 因を修正。
  実装は Sonnet サブエージェントへ委譲し差分レビュー済み。**Core 非変更**（App の 2 ファイルのみ）。
  - **修正2＝sRGB 画像の色管理スキップ（`PreviewBitmapCache.LoadCoreAsync`）**: デコード前に EXIF ColorSpace
    （0xA001・WIC クエリ `/app1/ifd/exif/{ushort=40961}`）を照会し、**1（sRGB）なら `DoNotColorManage`**、
    それ以外（Adobe RGB/Uncalibrated/タグ無し/照会失敗）は従来どおり `ColorManageToSRgb`（安全側フォールバック）。
    sRGB→sRGB でも WIC の色管理はデコード全体の約7割を占めており、実アプリ検証で **731〜908ms → 179〜334ms/枚**
    （50MP・約 -600ms）を確認。**注意: 厳密には恒等変換ではなく最大 ±3/255 の丸め差**（差分バイト 0.5〜1.4%・
    スタンドアロン実測）が出るが、視覚的に知覚不能＆SoftwareBitmap 化（2026-07-04）以前の
    `CanvasBitmap.LoadAsync`（色管理なし）時代と同じ表示に戻るだけなので許容と判断。
    Sony α1・OM-1 のテスト画像は全て ColorSpace=1（ICC なし）を確認済み＝実運用で常に効く。
  - **修正3＝inflight 相乗りのレート誤課金修正（`PreviewControl.xaml.cs`＋`PreviewBitmapCache.IsInflight` 新設）**:
    `RequestPreviewLoad` で「読み込み進行中（先読みで走行中）」への相乗りは新規デコードを発生させないため、
    **レート課金（`_recentDecodes`）せず常に表示要求を出す**分岐をキャッシュ済み分岐の直後に追加。settle タイマーの
    課金条件にも `!IsInflight` を追加。従来は相乗りまで課金されてレート予算（3枚/1500ms）を浪費し、縦型など
    先読み完了が遅い並びで間引き（settle 150ms＋デコード待ち）に落ちやすかった。相乗り判定と GetAsync の間で
    inflight が完了/破棄されるレースは課金なしの新規デコードになるが、同時実行ゲート（2本）で有界なので許容。
  - 検証: スタンドアロン（scratchpad DecodeBench）で ColorSpace クエリの型/値（ushort 1）・ピクセル差・
    時間短縮を確認 → 実アプリでも一時ログで全デコード DoNotColorManage・179〜334ms を確認（ログ・settings.json は
    復元済み）。変更: `Controls/PreviewBitmapCache.cs`・`Controls/PreviewControl.xaml.cs`。
    `BUILD SUCCEEDED`（x64 Release・警告0）／`dotnet test` 96 件緑。実機目視（未キャッシュ切替の体感高速化・
    **色味に違和感がないこと**・左右キー連打後の復帰・「キャッシュにあるのに遅い」の解消）はユーザー確認推奨。

- **表示要求の割り込み優先ゲート＋近接順先読み（案A＋C）完了（2026-07-06・実機確認済み）**: 「未デコード領域へ移動した
  直後、フォーカス画像のデコードが先に並んだ窓内先読みの後ろで待たされる」体感悪化（先読み枚数を増やすほど悪化）を解消。
  実装は Sonnet サブエージェントへ委譲し差分レビュー済み。**Core・XAML は非変更**。
  - **案A（`Controls/DecodeGate.cs` 新規）**: `SemaphoreSlim` 代替の**キー付き・優先昇格可能な非同期ゲート**
    （`WaitAsync(key)`/`Promote(key)`/`Release()`）。純 C#・**UI スレッド専有（単一スレッド前提・ロックなし）**＝
    `PreviewBitmapCache` は `ConfigureAwait(false)` 不使用で全継続が UI スレッド直列のため排他不要。
    `TaskCompletionSource(RunContinuationsAsynchronously)` で `Release()` スタック内の同期継続（再入）を防止。
    `Release` は待機列があれば先頭へスロット譲渡（`_inUse` 不変）、空なら空きを増やす。キー比較は OrdinalIgnoreCase。
  - **`PreviewBitmapCache`**: `_gate` を `DecodeGate` へ置換。`GetAsync(forDisplay:true)` が**相乗り（inflight あり）・
    新規開始の双方で `Promote(path)`** を呼び、表示待ちの 1 枚を待機列先頭へ割り込ませる（`LoadCoreAsync` は最初の
    await＝`WaitAsync` まで同期実行されるため新規登録直後の Promote が効く）。読込中（ゲート取得済み）への Promote は
    no-op＝進行中デコード（最大 `MaxConcurrentDecodes` 本）の完了待ちは従来どおり残る（最悪 200〜300ms 程度）。
  - **案C（`PreviewControl.WindowEntries()`）**: 選択集合なしの位置窓の列挙順を index 順（後方→焦点→前方）から
    **近接順・前方優先（焦点→+1→-1→+2→-2→…）**へ変更（集合は同一・順序のみ）。`Prefetch` はこの順でゲートに並ぶため
    順送りで次に見る前方 1 枚目が後方 2 枚目より先にデコードされる。`Trim` 保護・`IsPathInWindow` は順序非依存で影響なし。
  - **効果の設計判断（検討の経緯）**: 押しっぱなし継続中は先読みが settle まで走らず表示要求もレート制限されるため
    ゲートはほぼ空き＝案A/B とも効果薄。効くのは「一時停止→窓の先読き走行中にジャンプ」の場面。案B（表示要求の
    スロット借用＝同時実行超過）は「+1 上限だと離した瞬間に借用枠が塞がっていて効かないことがある」とのユーザー指摘が
    妥当で、採用するなら上限なし即時開始形だが、**ユーザー判断で案A＋C のみ採用**（残る最悪待ちは進行中 1 本≈300ms）。
  - **テスト**: `DecodeGate` は純 C# のため `PreviewViewport` と同じソースリンク方式で xUnit 化（`DecodeGateTests.cs`
    6 件＝容量内即時許可/FIFO/Promote 先頭移動/no-op/スロット返却/大小無視）。**計 102 件緑**。
  - 変更/新規: `Controls/DecodeGate.cs`（新規）・`Controls/PreviewBitmapCache.cs`・`Controls/PreviewControl.xaml.cs`、
    `tests/…/PhotoQuickSelector.Core.Tests.csproj`・`DecodeGateTests.cs`（新規）。`BUILD SUCCEEDED`（x64 Release・警告0）／
    `dotnet test` 102 件緑。実機目視（`C` オーバーレイで「一時停止→即ジャンプ」時にフォーカス画像が（待機中）を
    経ずに早く（読込中）へ遷移・順送りの引っかかり軽減）はユーザー確認推奨。

- **ゲート grant 時の窓分類優先度選択（Promote 廃止）完了（2026-07-07）**: 前項の案A（FIFO＋`Promote` の点昇格）を
  一般化し、「スロット解放（grant）のたびに待機列全員の優先度をその時点の窓分類で再評価し、最優先のキーへ譲渡する」
  方式へ変更。投入順で優先度を表現し陳腐化分を Promote で繕う構造から、単一の不変条件（grant 時に最も必要なものを選ぶ）
  へ統合。実装は Sonnet サブエージェントへ委譲し差分レビュー済み。**Core・XAML は非変更**。
  - **`DecodeGate`**: `Promote` を**削除**し、優先度コールバック `GetPriority`（`Func<string,int>?`・小さいほど優先・
    null なら純 FIFO）を追加。`Release()` は待機列を線形走査して**厳密に最小**のエントリへ譲渡（同値は先着＝FIFO
    タイブレーク）。ゲート自身はキー比較をしない（大小同一視等はコールバック側の責務）。
  - **`PreviewBitmapCache`**: `DecodePriority` プロパティでゲートへ中継。`GetAsync` の `Promote` 呼び出し 2 箇所
    （inflight 相乗り時・新規登録直後）を削除（`_pendingDisplay` への追加＝WasDisplayed 反映は維持）。
    「`LoadCoreAsync` は最初の await まで同期実行されるため新規登録直後の Promote が効く」という**タイミング依存の
    前提が不要になった**のが保守性上の収穫。
  - **`PreviewControl`**: `DecodePriorityOf(path)`＝`WindowEntries()` の index（窓外は `int.MaxValue`）を追加し、
    `IsWanted` と同じ 2 箇所（コンストラクタ・`RebuildCacheForConcurrency`）で設定。**`WindowEntries()` の列挙順が
    「Prefetch の投入順」ではなく「優先度の SSOT」になった**＝投入順序は同値タイブレークにすぎず挙動を決めない。
    表示要求（フォーカス）は index 0 なので旧 Promote の役割を完全に包含する。
  - **挙動差**: ①投入後にフォーカスが移動しても待機列の順序が陳腐化しない（旧方式は焦点 1 枚しか救えず、
    settle 先読みの残待機は旧順のままだった）。②窓外エントリは grant が後回しになり「待機中」のまま滞留しやすい
    （従来は早めに grant→`IsWanted` で即破棄）。列は Release ごとに必ず 1 件進むので飢餓はなく、無害な表示差のみ。
    ③レート制限・`IsWanted`・`Trim`・settle タイマーは順序制御と直交する役割（総量抑制/破棄/予算/トリガ）のため維持。
  - grant ごとのコールバック評価コストは待機者数×`WindowEntries()`（従来も grant 直後の `IsWanted`→`IsPathInWindow`
    で同クラスの評価をしており、コストのクラスは不変）。
  - **テスト**: Promote 系 3 件を優先度選択 4 件（最小優先度選択／grant ごとの再評価／同値 FIFO／`int.MaxValue` 沈降）へ
    置き換え。**計 103 件緑**。`BUILD SUCCEEDED`（x64 Release・警告0）。
  - 変更: `Controls/DecodeGate.cs`・`Controls/PreviewBitmapCache.cs`・`Controls/PreviewControl.xaml.cs`・
    `tests/…/DecodeGateTests.cs`。実機目視（`C` オーバーレイで settle 先読み走行中に 1 枚前進→新しい前方側が
    旧後方側より先に（読込中）へ遷移すること）は**ユーザー確認済み（2026-07-07）**。

- **Trim の表示実績（WasDisplayed）優先を撤去し LastUse 単独 LRU へ（2026-07-07）**: 先読みキャッシュの破棄順
  「未表示（先読みのみ）の古い順→表示済みの古い順」（2026-07-05 導入の 2 段階 LRU）を廃止し、**LastUse 昇順のみ**に
  変更。実装は Sonnet サブエージェントへ委譲し差分レビュー済み。**Core・XAML は非変更**（`Controls/PreviewBitmapCache.cs`・
  `Controls/PreviewControl.xaml.cs` のみ）。
  - **症状（ユーザー報告で発覚）**: 右キー連打で進んだ後に「左×2→右×2」を繰り返すと**毎往復で再デコードが走る**。
    着地点の先の先読み分（N+1/N+2）は表示されないまま窓を外れるため、予算超過時に「未表示が先」の規則で
    真っ先に捨てられ、10 枚以上前の表示済みが残留し続けるスラッシング構造だった。未表示エントリは再デコードしても
    未表示のままなので自然収束しない。
  - **前提が崩れていた理由**: ①窓外バイパス（`IsWanted`）＋grant 時の窓分類優先度選択により、キャッシュに入るのは
    「デコード時点で窓内＝フォーカス近傍」のものだけ＝導入時に警戒した投機的なゴミ先読みは上流で排除済みで、
    未表示エントリはむしろ直近近傍の良質な候補。②キャッシュヒットは `Prefetch` 経由でも LastUse を更新するため、
    LastUse 単独で「近傍＝新しい・遠方＝古い」の破棄順を表現できる。③WasDisplayed は減衰なし（一度立つと永久）で、
    セッションが長いほど太古の表示済みが上位ティアを占拠し悪化する。表示済み優先が有利なのは「昔見た写真へ
    大ジャンプで戻る」ときだけで、損失は再デコード 1 回＝高頻度の近傍往復スラッシングと割に合わない。
  - **変更**: `Trim` の `OrderBy(WasDisplayed).ThenBy(LastUse)`→`OrderBy(LastUse)`。付随して `WasDisplayed`／
    `_pendingDisplay`（inflight 中の表示要求控え＋全破棄経路のリーク対策 Remove 4 箇所）／`GetAsync` の
    `forDisplay` 引数を全撤去（案Aで残すとオーバーレイのラベル 1 箇所のためだけの機構になるため案Bを採用）。
    `C` オーバーレイの「表示済み/未表示」ラベルは **`LastUse <生値>`**（単調増分カウンタ・小さいほど次に破棄
    されやすい）へ置き換え＝破棄順のデバッグにはこちらが直接有用。
  - `BUILD SUCCEEDED`（x64 Release・警告0）／`dotnet test` **103 件緑**。実機目視（右連打→左×2→右×2 の繰り返しで
    2 回目以降の再読込が消えること・`C` オーバーレイの LastUse 表示）は**ユーザー確認済み（2026-07-07）**。

- **ソースコメントの整理（経緯語りの削除と方針確立）（2026-07-07・コミット `5b4fe8f`＋DecodeGate 微修正）**:
  コメント肥大化を受け、「残す＝制約・why・単位・座標系・落とし穴／削除＝HISTORY.md 記録済みの実装経緯
  （【案N】ラベル・旧機構への言及・実測値詳細）とコードの言い換え・重複」の基準で全ファイルを整理。
  - **ユーザー合意の基準（重要）**: ①public プロパティ/メソッドの `<summary>` は自明に見えても残す
    （他クラスとのインターフェース文書。`CacheSnapshotItem` のプロパティで一度削除→指摘で復元）。
    ②「連打抑制のレート。」のような**区分ラベル的な短いコメントも残す**（一度削除→指摘で復元）。
    ③削除は HISTORY.md への記録有無を Grep で確認してから（未記録なら先に移設）。
  - **変更**: `PreviewBitmapCache.cs`（コメント 149→113 行。WasDisplayed 撤去・旧 Promote の経緯語り、
    実測値詳細、3 回重複の「byte[] は Dispose 不要」集約等）・`PreviewControl.xaml.cs`（238→223 行。
    【案2】ラベル、Esc アクセラレータ撤去の経緯、`WindowEntries` doc 内の重複説明統合等）・
    `DecodeGate.cs`（「【背景】」「従来どおり」×2 の経緯基準の言い回しを現在形へ）。
  - **変更なしと判断**: `MainViewModel.cs`・`PreviewViewport.cs` は全文精査の結果、全コメントが「残す」
    カテゴリ。リポジトリ横断の経緯語りマーカー検索（【案N】/かつては/従来は/撤去した等)も残りは正当な用途
    （「旧アプリ準拠」＝仕様出典、「案A/案B」＝CLAUDE.md でも使う識別子）のみ。経緯語りの堆積は直近の
    性能改善で集中的に触ったプレビューキャッシュ周りに限られていた。
  - コード非変更（コメントのみ）。ビルド警告 0。基準はメモリ `comment-cleanup-criteria` にも保存済み。

- **先読みキャッシュの解凍爆弾ガード（巨大宣言寸法の画像を弾く）完了（2026-07-07）**: 極端な幅・高さを宣言した
  悪意ある画像（JPEG はヘッダ上 65535×65535＝BGRA 約 17GB まで宣言可能・実ファイルは数十 KB で作れる）を読むと
  `GetPixelDataAsync` が宣言寸法どおり全面確保しにいき、ページファイルの大きい環境ではコミットが通って OS ごと
  スラッシングする懸念（ユーザー指摘）への対策。**`Controls/PreviewBitmapCache.cs` のみ変更・Core・XAML 非変更**。
  - **リスク評価**: `MaxCacheBytes`（2GB 予算）の `Trim` はデコード完了後にしか効かず確保自体は防げない。
    `MaxConcurrentDecodes=2` で巨大確保が同時 2 本走り得る。例外は `catch` で握られるため即クラッシュより
    「確保が成功してしまう」方が危険。サムネイル側（`PhotoItemViewModel`）は `DecodePixelWidth` の縮小デコードで
    WIC がスキャンライン単位処理のため全面確保は起きず対象外（異常に幅広い PNG の理論上の穴のみ・優先度低）。
  - **実装（`LoadCoreAsync` に 2 段ガード）**: ①`File.ReadAllBytesAsync` 前に実ファイルサイズ > 512MB
    （`MaxFileBytes`）で `return null`（非画像の巨大ファイル防御）。②`BitmapDecoder.CreateAsync` 直後に
    `OrientedPixelWidth × OrientedPixelHeight × 4`（**long で計算**。uint 同士の乗算はオーバーフローする）が
    1GB（`MaxPixelBytesPerImage`）超過で `return null`。ヘッダ読みのみでコストほぼゼロ・既存の「null＝表示しない」
    フローにそのまま乗る。1GB ≈ 268MP は実在カメラ最大級（Phase One 150MP ≈ 605MB）に余裕で収まり、.NET の
    `byte[]` 上限（約 2.1GB・超えると `DetachPixelData` がどのみち失敗）より十分下。上限は固定定数（設定項目にしない・
    ユーザー合意）。弾いた画像は単に表示されない（プレースホルダ表示は必要になったら拡張）。
  - `BUILD SUCCEEDED`（x64 Debug・警告0）／`dotnet test` **103 件緑**。悪意ある画像ファイルでの実機再現確認は未実施
    （巨大宣言寸法のテスト画像を用意すれば `C` オーバーレイで「読込中→消滅・キャッシュ非在籍」として観察可能）。

- **ステータスバーのフォルダパス表示、長パス・狭幅で末尾フォルダ名が見切れる問題を修正（2026-07-07）**:
  `PhotoStatusBar.xaml` のパス表示 `TextBlock` は `TextTrimming="CharacterEllipsis"`（末尾省略）のため、
  幅が狭いと一番重要な末尾フォルダ名から消えてしまっていた。
  - **対応（B案＝head/tail 分割で末尾フォルダ名を常時保証＋A案＝ツールチップ全文の併用）**: `MainViewModel` に
    `StatusPathHead`（親ディレクトリ部）／`StatusPathTail`（セパレータ＋末尾フォルダ名）を追加し、
    `SetStatusPath` で `StatusText`（フォールバック表示＋ツールチップ全文用）とともに設定。XAML 側は
    Grid.Column=2 を「メッセージ用 TextBlock」と「head/tail 2 分割の Grid」を重ねた構造にし、
    `StatusPathVisibility`/`StatusMessageVisibility`（`StatusPathTail` の有無で排他）で切り替える。
  - **レイアウトの要点**: 内側 Grid は列定義 `[*][Auto]`＋`HorizontalAlignment="Left"`。head は `*` 列で
    `CharacterEllipsis`、tail は `Auto` 列で常時全表示。これにより幅が十分なときは head・tail が隣接して
    自然な1本のパスに見え、幅が不足するときのみ `*` 列の head 側だけが末尾省略される（tail は常に見える）。
  - **モード切替の仕組み**: `OnStatusTextChanged` で `StatusPathHead`/`StatusPathTail` を毎回 `""` にクリアする
    ため、`Status_Loading`/`Status_NoJpeg`/`Status_LoadError` 等の**メッセージ系 `StatusText` 代入は自動で
    パスモードから抜ける**（個別対応不要）。`SetStatusPath` 内の `StatusText` 代入も同ハンドラを通るが、
    直後に head/tail を再設定するため問題ない。ルートパス（`D:\` 等、`Path.GetFileName` が空を返す）と
    セパレータを含まない裸の名前（スライスが範囲外になるためガード）は従来どおり単一 TextBlock 表示に
    フォールバックする。
  - 変更: `ViewModels/MainViewModel.cs`（`StatusPathHead`/`StatusPathTail`/`SetStatusPath`/
    `OnStatusTextChanged` 追加、`LoadFolderAsync` 成功時の `StatusText = folderPath;` を `SetStatusPath` へ
    置換）・`Controls/PhotoStatusBar.xaml`（Grid.Column=2 を head/tail 構造へ）。`BUILD SUCCEEDED`／
    `dotnet test` **103 件緑**。ユーザー実機確認済み（2026-07-07）。

- **EXIF 詳細パネル（右パネル上段のルーペ⇄EXIF 切替）完了（2026-07-08）**: 全メタデータタグの一覧表示。
  検討時の案1（右パネル上段をルーペと排他切替）を採用。プレビュー限定（グリッド中は従来どおり案A の要約のみ）。
  - **Core**: `ExifTagReader.ReadAllTags(path)` 追加（`ExifTagReader.cs`）。MetadataExtractor の全ディレクトリ・
    全タグを `ExifTagGroup`（DirectoryName＋Tags）／`ExifTagEntry`（Name＋Description）でダンプする純関数。
    空白 Description はスキップ、300 文字超は切り詰め（バイナリ系タグ対策）、例外時は throw せず空リスト。
    **データ型は get-only class**（record／init-only setter は XamlTypeInfo 生成と衝突＝CS8852。`CacheEntry` と同じ制約）。
    `MetadataReader.cs` は非変更。テスト 4 件追加（`ExifTagReaderTests.cs`。実画像フォルダ規約は
    `MetadataReaderFocusTests` を踏襲）＝**107 件緑**。
  - **タグ規模の実測**: グループ 12〜16 個。Sony α1 ＝ `Sony Makernote`(104) 等で計約 200、OM-1 ＝
    `Olympus Camera Settings`(96)／`Olympus Image Processing`(149)／`Olympus Focus Info`(106) 等で計約 500。
    **このため表示は非仮想化 ItemsControl ではなくグループ化 ListView（仮想化）にした**。
  - **App**: `Controls/ExifDetailPanel.xaml(.cs)`＝表示専用 UserControl（`CollectionViewSource`
    `IsSourceGrouped`＋`ItemsPath=Tags`、タグ名/値の 2 列・値は `IsTextSelectionEnabled`、色は ThemeResource のみ。
    API は `ShowTags`/`ShowMessage`、文言は呼び出し側供給＝resw 非依存）。将来グリッド側へ出す拡張も部品差しで可能。
  - **PreviewControl**: 右パネルを 4 行（タブ/コンテンツ/スプリッター/ナビ）へ変更し、上段セルで
    `ZoomCanvas` と `ExifDetailPanel` を Visibility 排他。新 partial `PreviewControl.ExifPanel.cs` に
    切替（`E` キー＋タブ 2 ボタン。`IsTabStop=False` でフォーカスを奪わない）と遅延読み
    （表示中のみ焦点写真の変更に追従・`Task.Run` で都度読み・`_exifLoadToken` で追い越し破棄。先読みキャッシュとは独立）。
    状態は `AppSettings.PreviewExifPanel` へ永続化（既定 false＝ルーペ）。
  - **ルーペ復帰時の再センタリング**: Collapsed 中はキャンバス寸法 0 のままルーペ中心が決まるため、
    ルーペへ戻すとき `DispatcherQueue.TryEnqueue(ScrollZoomToFocus)` で AF 点へ寄せ直す。
  - resw に `Preview_LoupeTab`/`Preview_ExifTab` 追加（ja/en）。shortcuts.json に `E` を追加し
    `tools/gen-shortcuts.ps1` で SHORTCUTS.md（ja/en）再生成。
  - `BUILD SUCCEEDED`／`dotnet test` **107 件緑**。実機確認済み（2026-07-08・Dark・Sony/Olympus 両機）:
    タブ/E 切替・全 12 グループ表示（Sony Makernote／GPS／XMP 含む）・ホイール＋スクロールバー・
    写真送りでの内容追従（スクロールは先頭へ）・ルーペ復帰の AF 枠付き再描画・タブ状態の再起動復元、いずれも OK。
    ※実機確認の初報で「Makernote 以降が出ない／スクロール不能」と出たが、再診断で computer-use の
    ポインタ操作がリストへ届いていなかった操作側の問題と判明（アプリ側は正常）。
  - **EXIF パネル表示中の画像送りがもたつく問題を解消（2026-07-09）**: 初版は焦点変更のたびに無条件で
    `Task.Run(ReadAllTags)` を発火し、勝った結果で `ShowTags`（グループ化 ListView の Source 丸ごと差し替え）
    していた。←/→ 押しっぱなし（キーリピート）で ①全ディレクトリのフルパースがスレッドプールへ殺到して
    画像デコードのワーカーと競合、②毎ステップの ListView 再構築が UI スレッド（＝画像描画と同一）を塞ぐ、
    の 2 重負荷になり、画像切替のスピード感を損ねていた（パネル非表示時は元々 early-return で無影響）。
    対策は 3 点:
    - **誤情報を出さない（案 B）**: 焦点変更時は前写真の内容を残さず `ShowMessage("読込中…")` を即出す
      （`ShowMessage` は可視性トグル＋Source を一度 null にするだけで ListView 再構築を伴わない。
      2 枚目以降は既に null で実質トグルのみ）。resw に `ExifPanel_Loading`（ja/en）追加。
    - **VM 常駐キャッシュで再解析ゼロ**: `PhotoItemViewModel` に `_exifGroups`／`EnsureExifGroupsAsync()`／
      `CachedExifGroups` を追加（`_thumbnailBytes` と同じ「一度だけ取得して常駐」パターン）。解析は
      `Task.Run(ReadAllTags)` でバックグラウンド。フォルダ再読込で `Photos` ごと破棄＝別途 LRU 不要。
      破損・未対応は空リストを常駐させ無限リトライしない。
    - **settle タイマ相乗りで「1 停止 1 回」に統一**: 焦点変更（連打経路）は placeholder のみ即表示し、
      重い `ShowTags` は既存の画像用 settle タイマ（`LoadSettleDelay`=150ms）の Tick で最終焦点の 1 枚だけ実行。
      キャッシュ済み・未解析にかかわらず ListView 再構築は停止後 1 回に集約。連番の既視画像をフライスルー
      しても placeholder のまま＝UI スレッドも解析スレッドも画像切替に専念できる。
    - 実装: `PreviewControl.ExifPanel.cs` を `OnFocusChangedForExif`（連打経路＝placeholder のみ）と
      `RenderExifForFocus`（settle 確定・E キー/タブ・入場から呼ぶ実描画）に分割。トークン（`_exifLoadToken`）で
      追い越し／ルーペ切替後の破棄を担保。`BUILD SUCCEEDED`／`dotnet test` **107 件緑**。
      **実機のフライスルー体感確認はユーザー未実施**（押しっぱなし挙動のため要目視）。
- **サムネイル／フィルムストリップで縦横の向きが判るように（2026-07-09）**: グリッドとフィルムストリップで
  横型・縦型の区別が付かない問題を解消。
  - **真因**: サムネイルバイト取得が `GetThumbnailAsync(ThumbnailMode.PicturesView, 320)` で、PicturesView は
    正方形寄りにクロップ（＋キャッシュ状況で EXIF 回転が反映されないことがある）ため、横も縦も正方形セルを
    埋め切り向きが潰れていた。XAML 側は既に正方形セル＋`Stretch="Uniform"`＋背景帯（`SubtleFillColorSecondaryBrush`）で
    レターボックスの受け皿は完成済みだった。
  - **修正**: `PhotoItemViewModel.LoadBytesCoreAsync` の 1 行を **`ThumbnailMode.SingleItem`** に変更
    （アスペクト比保持＋EXIF Orientation 適用）。既存の Uniform＋正方形セルと噛み合い、横は上下・縦は左右に
    帯が出て向きが即読める。grid／filmstrip は同じバイトを共用するため両方に同時に効く。プレビューは別系統の
    全解像度デコードなので無影響。`BUILD SUCCEEDED`。実機の縦横混在表示はユーザー確認済み（2026-07-09）。

- **複数選択時のフォーカスと選択集合メンバーの見分けを改善（案A・2026-07-09）**: 複数選択中、「焦点の1枚」と
  「選択されただけのメンバー」が見た目で区別しづらい問題を解消（両者とも似た白系の枠で、しかも焦点の方が弱かった）。
  - **真因**: 焦点＝グレー `#E0E0E0` の枠なのに対し、メンバー＝純白 `#FFFFFF`・3px の枠＋白ウォッシュで、
    **メンバーの方が明るく太く目立っていた**。焦点がメンバーの1枚になると上に乗る純白枠に埋もれた。
  - **修正（案A＝視覚チャンネルの分離）**: 焦点＝「くっきりした純白の太枠」、メンバー＝「純白の細枠（1px）＋
    薄い白の塗りウォッシュ」に役割分担。焦点を最も明るく強い縁取り（純白）に統一し、メンバーは細枠＋塗りで
    差別化。カラーラベル（有彩色の内枠）と競合しないよう、区別は色相でなく明度・太さ・塗りで付ける。
  - **落とし穴（ユーザー反復で判明）**: 塗りウォッシュを濃く（`#40FFFFFF`＝25%）すると写真がくすんで「色あせ＝
    削除扱い」に見える。→ 元の薄め（`#33FFFFFF`＝20%）に戻し写真の発色を残す。メンバー細枠も淡い白では
    弱く見えたため純白 `#FFFFFFFF` に。最終: 焦点＝純白3px枠／メンバー＝純白1px枠＋`#33FFFFFF` 塗り。
  - 対象: `PhotoGridView.xaml`（グリッド。焦点は GridView 標準選択ビジュアルのブラシを純白へ）／
    `PreviewControl.xaml`（フィルムストリップ。焦点リング＋メンバーオーバーレイ）。`BUILD SUCCEEDED`・ユーザー確認済み。

- **Olympus / OM の AF フォーカス枠に対応（案A・2026-07-09）**: これまで AF 枠は Sony 機のみだったが、OM 機も
  EXIF に枠情報を持つと判明したため対応。Core の読取りに分岐を1本足すだけで描画・ルーペ・ナビ・`Alt+F` の
  全経路を Sony と共用（`ImageMetadata` の `FocusPoint`/`FocusSize`/`FocusReferenceSize` へ写す既存モデルを再利用）。
  - **タグ**: `Olympus Camera Settings`（`OlympusCameraSettingsMakernoteDirectory`）の **`0x0304` "AF Areas"**。
    生値は `int[64]`（最大 64 枠、0＝未使用）。各非ゼロ int32 が 1 枠を **「画面全体に対する 0..255 の割合」で
    左上・右下の 4 隅**として packing。バイトは上位から left, top, right, bottom。実測 `P2280057.JPG` は
    `-1938386049` = `0x8C76937F` → (140,118)-(147,127)。description も `(140/255,118/255)-(147/255,127/255)` で一致。
    別タグ `0x0305 AF Point Selected`（`(57%,50%)`）は AF 点グリッドの粗い位置なので**枠には 0x0304 を使う**。
  - **案A＝2×255 基準へ無損失マップ**: 中心＝2 隅の平均で 0.5 単位（例 143.5）を生み `PointI`/`SizeI` は int の
    ため、基準を 255→**510**、中心・サイズを **2 倍**して整数のまま格納。`FocusReferenceSize=(510,510)`,
    `FocusPoint=(left+right, top+bottom)`, `FocusSize=(2*(right-left), 2*(bottom-top))`。描画の
    `cx = fp.X*rawW/refW` にそのまま乗り、丸め誤差ゼロ。**描画側（`PreviewControl.Overlays.cs`）は無改修**。
  - **座標系**: Sony と同様「生センサー基準」として扱い、描画側の `OrientationMatrix` で表示空間へ写す。
    実測サンプルは Orientation=1（横位置）なので回転は恒等。**縦位置（Orientation≠1）の OM 実写での枠一致は未検証**
    （ズレたら座標が表示基準＝回転適用済みの可能性。要 OM 縦位置1枚で確認）。
  - **複数枠**: `int[64]` は多点 AF で複数非ゼロになり得るが、既存モデルが単一枠のため**先頭の非ゼロ枠のみ**採用
    （将来拡張として全枠描画＝モデルの list 化があり得る）。
  - 対象: `MetadataReader.cs`（`ReadOlympusFocus` 追加。Sony が枠を返さないときのみ呼ぶフォールバック）。
    テスト `MetadataReaderFocusTests` に既知 1 枚の厳密値検証を追加＝**108 件緑**。実機での縦位置確認は残。

## 右クリックコンテキストメニュー（グリッド／フィルムストリップ）（2026-07-10）

グリッドのサムネイルとプレビュー下部フィルムストリップで右クリック → 操作メニューを出す。評価・ファイル操作・
外部連携・選別（Reject/リネームコピー）・全選択を集約。**評価キーや外部連携キーと実体を共有**し、挙動を揃える。

- **対象確定＝エクスプローラ流儀**（`PhotoContextMenu.ResolveTargets`）: 右クリックした写真が
  選択集合のメンバーなら**集合全体**が対象、集合外なら**その 1 枚を選び直して**単独対象にする
  （`FocusedPhoto` 代入が `OnFocusedPhotoChanged`→`ClearSelection` を誘発＝右クリックで選択が移る）。
  空白域では全選択のみ。複数対象時は先頭に「N 枚が対象」の無効見出しを置いて誤操作を防ぐ。
- **メニュー構成**（`MenuFlyout` をコードビルド。文言は `Loc.Get`＝resw `Ctx_*`）:
  - C 評価: レーティング（0/★1–5）・カラーラベル（赤黄緑青紫トグル）・フラグ（採用/なし/除外）。適用は
    `MainViewModel.ApplyEvaluationAsync(op, targets)` 経由＝**キーと同じく sqlite 未作成なら作成確認を挟む**。
    フラグの一発指定用に `PhotoEvaluation.SetFlag(int)`／`PhotoItemViewModel.SetFlag(int)` を新設
    （段階遷移の `FlagUp/Down` とは別。三値クランプ。`PhotoEvaluationTests.SetFlag_ClampsToTriState`）。
  - B ファイルをコピー（サブメニュー）: `PhotoFileClipboard.CopyFilesAsync`。`DataPackage`＋`SetStorageItems`＋
    `RequestedOperation=Copy` で**エクスプローラへ貼り付け＝ファイルコピー**になる。「表示中のファイルのみ」／
    「関連ファイルも含める（RAW 等）」（後者は同フォルダの**同名別拡張子**を展開。前方一致誤爆を避け拡張子除去名の
    完全一致で判定＝`DSC0001` が `DSC00010` を拾わない）＋区切り線の下に**リネームしてコピー**（下記 D）を同居。
    テキスト（1 行 1 パス）併載。I/O（同名列挙は `Task.Run`／`StorageFile` 取得は 1 件ずつ `await`）で UI を塞がない。
  - A 外部連携: エクスプローラで表示（`/select` は複数不可＝代表 1 枚に限定）・既定アプリで開く（全対象）・
    パスをコピー（`CopyPaths`＝改行区切り）・共有（`ShareHelper.ShareAsync(IReadOnlyList<string>)` を新設。
    設定 exe は各ファイル個別起動／標準共有シートは複数同時載せ）。
  - D リネームしてコピー: **選択集合が対象**。フロー本体は `FilterBar` から `BatchFlows`
    （`RunCopyRenameAsync`／`RunRejectAsync`＋確認/通知ダイアログ）へ切り出し、フィルタバー（絞込結果）と
    右クリック（選択集合）で共用。既存の bat 生成・実行機構は対象リスト引数化済みだったのでそのまま流用。
    ※**Reject 移動は右クリックからは外した**（誤操作懸念・ユーザー要望）。`BatchFlows.RunRejectAsync` は
    フィルタバー側で引き続き使用。
  - E すべて選択: `MainViewModel.SelectAll()`（絞込結果 `Photos` 全件を選択集合へ。`_managingSelection` で
    焦点移動による解除を回避）。
  - **複数選択時の見分け**: 選択集合全体を対象にする項目（評価各サブ／ファイルをコピー／パスをコピー／
    既定アプリ／共有）は末尾に「(全選択ファイル)」（`Ctx_AllFilesSuffix`）を付す。代表 1 枚のみのエクスプローラ表示と
    全選択は付けない。加えて先頭に「N 枚が対象」の無効見出し。
- **配線**: `PhotoGridView`／`PreviewControl(FilmStrip)` の `RightTapped` から `PhotoContextMenu.Show`。
  `e.OriginalSource` の `DataContext` で右クリックされた `PhotoItemViewModel` を取得（空白域は null）。
- 対象: 新規 `PhotoContextMenu.cs`／`PhotoFileClipboard.cs`／`BatchFlows.cs`、`PhotoFileCommands`
  （複数対応 `CopyPaths`/`OpenWithDefault`/`Share`）、`ShareHelper`（複数対応）、`MainViewModel.SelectAll`、
  Core `PhotoEvaluation.SetFlag`、resw（ja/en に `Ctx_*`・`Share_MultipleTitle`）。**`dotnet test` 113 件緑**
  （SetFlag の theory 5 件追加）。**実機での目視動作確認は未了**（要ユーザー確認）。

- **マウスでの複数選択（Ctrl+クリック／Shift+クリック）完了（2026-07-11）**: キー操作（`SelectionKeyCommands`）と同じ
  焦点/選択集合モデルをマウス修飾クリックにも拡張。
  - **`MainViewModel` に2メソッド追加**: `ToggleSelectionAt(photo)`（Ctrl+クリック＝クリック先へ焦点を移し
    `ToggleFocusInSelection` と同じくトグル参加。集合が空の状態から始めるときは元の焦点も集合に残す＝
    `MoveFocusKeepingSelection` と同じ流儀）／`ExtendSelectionTo(photo)`（Shift+クリック＝既存の
    `ExtendSelectionTo(int delta)` のオーバーロード。pivot 未設定/範囲外なら現在の焦点を pivot にして
    `SetSelectionRange` へ）。どちらも `SetFocusManaged` 経由なので集合はリセットされない。
  - **新規 `SelectionMouseCommands.TryHandle(SelectionChangedEventArgs, vm)`**: `SelectionKeyCommands` と対の
    App 層静的ディスパッチャ。Ctrl/Shift の判定は `KeyboardModifiers`（クリック時点の物理キー状態）。
    `AddedItems`（無ければ `RemovedItems`）から対象を取る（Ctrl+クリックで既選択項目を外すと
    `SelectedItem=null`＝`Added` が空になるため）。Ctrl+Shift は Shift 扱い、Alt 併用は対象外（素クリック）。
  - **配線はグリッド/フィルムストリップ共通パターン**: `SelectionChanged` ハンドラで
    ①VM→ビュー反映によるエコー（`SelectedItem == vm.FocusedPhoto`）を弾く→②`SelectionMouseCommands.TryHandle`→
    成功なら `SelectedItem` を `vm.FocusedPhoto` へ復元代入（Ctrl+クリック解除で null になったケースを含む）→
    ③不成立（素のクリック）なら従来どおり `vm.FocusedPhoto = SelectedItem` で集合リセット。
    **エコー判定が要る理由**: これが無いと `Ctrl+←/→`（キーボードでの焦点のみ移動）で `PhotoGrid.SelectedItem`
    へ焦点を反映した際、Ctrl 押下状態のまま `SelectionChanged` が発火し、マウス修飾クリックと誤判定して
    トグルしてしまう（`PhotoGridView.PhotoGrid_SelectionChanged`）。
  - **フィルムストリップは `TwoWay`→`OneWay`+`SelectionChanged` に変更**（`PreviewControl.xaml` の
    `FilmStrip.SelectedItem`）。TwoWay のままだと x:Bind の自動反映がビューモデル側の
    `ToggleSelectionAt`/`ExtendSelectionTo`（＝`SetFocusManaged` 経由の焦点設定）を経由せず
    `FocusedPhoto` を直接書き換えてしまい、修飾クリック判定の余地がなくなるため。OneWay化の副作用
    （Ctrl+クリックでの解除時に `SelectedItem` が null のまま残る）は復元代入でカバー。
  - **副次効果**: `SelectionChanged` 配線により、`GridView`/`ListView` ネイティブの `Shift+Home`/`End`/
    `PageUp`/`PageDown` でのレンジ拡張も `SelectionMouseCommands`（Shift 扱い）経由でレンジ選択になる
    （エクスプローラ相当の挙動。意図した副次効果であり実機確認時に確認推奨）。
  - 新規: `SelectionMouseCommands.cs`。変更: `ViewModels/MainViewModel.cs`（2メソッド追加）、
    `Controls/PhotoGridView.xaml.cs`（`PhotoGrid_SelectionChanged` 改修）、
    `Controls/PreviewControl.xaml`（`FilmStrip` バインド変更）・`.xaml.cs`（`FilmStrip_SelectionChanged` 追加）、
    `CLAUDE.md`（キー操作節）、`shortcuts.json`（複数選択グループへ 2 行追加。`keys` はローカライズ
    オブジェクト形式＝`ShortcutCheatSheet.Pick` 対応済み）＋ `tools/gen-shortcuts.ps1` 再実行で
    `docs/SHORTCUTS.md`/`SHORTCUTS.en.md` を追従。`BUILD SUCCEEDED`（x64 Debug・警告0）／`dotnet test` 113 件緑。
  - **修正（同日・実機確認で発覚）**: 素のクリックが集合メンバー上でも集合を解除していた（`FocusedPhoto` への
    非管理代入一択だったため）。ユーザー要望＝「メンバークリックは集合維持で焦点だけ移動／集合外クリックは
    従来どおり解除」。`MainViewModel.FocusByClick(photo)` を追加（メンバーなら `SetFocusManaged`＝集合維持、
    集合外なら非管理代入＝`OnFocusedPhotoChanged` の `ClearSelection` に到達）し、グリッド/フィルムストリップの
    素クリック経路を差し替え。変更: `ViewModels/MainViewModel.cs`・`Controls/PhotoGridView.xaml.cs`・
    `Controls/PreviewControl.xaml.cs`、`CLAUDE.md`。修正込みで実機目視をユーザー確認済み（2026-07-11）。

- **大量選択時の一括操作に確認ダイアログを追加（2026-07-11）**: 選択枚数が多い（例: 4000枚）状態で右クリックの
  「既定のアプリで開く」「共有」「ファイルをコピー」（表示中のみ／関連ファイルも含める、の2項目）を実行すると、
  被害・時間コストが大きい割に取り消せない（1ファイル=1プロセス起動で止められない／ファイルごとに
  exe 起動／クリップボード準備が長時間化）ため、実行前に続行/キャンセルの確認を挟むようにした。
  - **しきい値は 10 枚**（`PhotoContextMenu.BulkWarnThreshold`）。エクスプローラの同種警告（15枚）より
    低めにしているのは、写真ファイルは1件あたりの処理コスト（デコード・IPC・プロセス起動）が
    一般ファイルより重いため。
  - **`PhotoContextMenu.RunWithBulkWarningAsync(xamlRoot, count, messageKey, run)`**: 対象数がしきい値以上
    なら `BatchFlows.ConfirmAsync` で確認し、キャンセルなら `run` を呼ばない。少数なら即実行。
    対象4項目（開く/共有/コピー×2）のみに適用し、リネームしてコピー・パスをコピー・エクスプローラで表示・
    すべて選択・評価系は変更していない（いずれも被害が軽微、または元から確認ダイアログを持つため）。
  - **`BatchFlows.ConfirmAsync(xamlRoot, title, message)`** を新設（既存 `ShowMessageAsync`/`ConfirmBatchAsync`
    と同スタイルの汎用続行確認。既定ボタンはキャンセル側＝安全側）。
  - キーボードショートカット（`Alt+E`/`Alt+S`）は焦点1枚のみが対象のため対象外（変更なし）。
  - 変更: `BatchFlows.cs`（`ConfirmAsync` 追加）、`PhotoContextMenu.cs`（しきい値定数・
    `RunWithBulkWarningAsync`・対象4箇所の呼び出し差し替え）、resw（ja/en に `BulkWarn_Title`・
    `BulkWarn_OpenMessage`・`BulkWarn_ShareMessage`・`BulkWarn_CopyMessage`・`Msg_Continue`）、
    `CLAUDE.md`（右クリック節に一文追記）。`dotnet test` 113 件緑（Core 非変更のため件数不変）。
    **実機目視は未了**（要ユーザー確認）。

- **ハンバーガー「ファイル」とショートカットを右クリックと統一（2026-07-11）**: ステータスバー右端メニューの
  「ファイル」サブメニューとキーボードショートカット（`Ctrl+E`/`Alt+E`/`Ctrl+Alt+E`/`Alt+S`）を、右クリック
  メニューのファイル関連操作と挙動・項目構成を揃えた。従来はどちらも「焦点の1枚」固定だったが、右クリック
  同様「選択集合があれば集合全体、無ければ焦点の1枚」へ対象を拡張。
  - **対象確定の共通化**: `PhotoContextMenu.ResolveCurrentTargets(vm, out primary)` を新設（既存
    `ResolveTargets(vm, clicked: null, out primary)` への委譲）。右クリック以外（ハンバーガー・キー操作）から
    呼び、選択は変更しない。
  - **ファイル項目ビルダーの共有化**: 右クリック `Show()` 内にあったファイル系項目（ファイルをコピー▶／
    パスをコピー／エクスプローラーで表示／既定のアプリで開く／共有）の構築を
    `PhotoContextMenu.AddFileItems(items, vm, targets, primary, suffix, xamlRoot, withAcceleratorText)` へ
    切り出し、`Show()` とハンバーガー `PhotoStatusBar.BuildFileMenu()` の双方から呼ぶ単一ソースにした。
    `withAcceleratorText=true`（ハンバーガー側）ではパスをコピー/エクスプローラー表示/既定アプリ/共有の
    4項目に `KeyboardAcceleratorTextOverride`（Ctrl+Alt+E/Ctrl+E/Alt+E/Alt+S）を付け表示専用のショートカット
    ヒントを出す（実キー処理は別経路）。`PhotoStatusBar.xaml` の `FileSubItem` は静的4項目を削除し
    `Menu_Opening`→`BuildFileMenu()` で毎回 Clear→再構築する形にした（メニューは開くたび使い捨てなので
    既存 `Menu_Opening` と同パターンで問題ない）。
  - **`BulkWarnThreshold`／`RunWithBulkWarningAsync` を `PhotoContextMenu` から `BatchFlows` へ移設**
    （internal）。右クリック・ハンバーガー・キー操作（`PhotoFileCommands.TryHandle`）の3経路で共用する
    ため、右クリック専用クラスに置いたままだと参照できなかった。
  - **キー操作の集合対応**: `PhotoFileCommands.TryHandle` のシグネチャを
    `(VirtualKey key, PhotoItemViewModel item, AppSettings settings)` から
    `(VirtualKey key, MainViewModel vm, XamlRoot xamlRoot)` に変更し、内部で `ResolveCurrentTargets` を呼ぶ形に
    した。`Ctrl+E`（エクスプローラ表示）のみ `/select` が複数不可のため引き続き焦点1枚（`primary`）。
    `Alt+E`/`Alt+S` は集合全体へ適用し `BatchFlows.RunWithBulkWarningAsync` で大量対象（10枚以上）の確認を
    挟むようにした（従来は焦点1枚固定のため確認なし＝右クリックと非対称だった）。`Ctrl+Alt+E`（パスをコピー）
    は軽微なので警告なし。呼び出し側は `MainPage.HandleGlobalKeyDown` と `PreviewControl.HandleKeyDown`
    の2箇所（いずれも `TryHandle(key, ViewModel/_viewModel, XamlRoot)` へ変更。外側の `FocusedPhoto` ガードは
    他の評価キー処理も使うため構造を維持）。
  - **未使用になった単一アイテム版を削除**: `PhotoFileCommands` の `OpenWithDefault(PhotoItemViewModel)`・
    `CopyPath(PhotoItemViewModel)`・`Share(PhotoItemViewModel, AppSettings)`（ハンバーガーの旧クリック
    ハンドラ専用だった）と、私用ヘルパ `CopyPath(string)` を削除。`OpenInExplorer(PhotoItemViewModel)` は
    右クリック／ハンバーガー／`Ctrl+E` の代表1枚表示で使い続けるため残置。
  - 変更: `PhotoContextMenu.cs`（`ResolveCurrentTargets`・`AddFileItems` 新設、`Show()` を委譲形に簡略化、
    `BulkWarnThreshold`/`RunWithBulkWarningAsync` を削除）、`BatchFlows.cs`（同2つを移設）、
    `PhotoFileCommands.cs`（`TryHandle` 全面改修・単一版削除）、`PhotoFileClipboard.cs`（doc comment の
    cref 差し替え）、`Controls/PhotoStatusBar.xaml`（`FileSubItem` の静的4項目を削除）・`.xaml.cs`
    （`BuildFileMenu()` 新設・旧4クリックハンドラ削除）、`MainPage.xaml.cs`・`Controls/PreviewControl.Input.cs`
    （`TryHandle` 呼び出し変更）、resw（ja/en の `Menu_Explorer`/`Menu_OpenDefault`/`Menu_CopyPath`/`Menu_Share`
    を削除。`Menu_FileSub` は残置）、`shortcuts.json`（ファイル連携4行の説明を対象拡張の実態に合わせ更新）＋
    `tools/gen-shortcuts.ps1` 再実行で `docs/SHORTCUTS.md`/`SHORTCUTS.en.md` 追従、`CLAUDE.md`
    （キー操作節に一文追記）。**Core 非変更**。`BUILD SUCCEEDED`（x64 Debug・警告0）／`dotnet test` 113 件緑
    （Core 非変更のため件数不変）。実機目視をユーザー確認済み（2026-07-11）。

- **左ペインのピン留め/フライアウト表示（2026-07-11）**: 左ペインを閉じているときでも内容を使えるよう、
  「ピン留め（既定・従来のドッキング表示）」と「ピン解除（フライアウト表示）」の 2 モードを導入。ピン解除中は
  左カラム常時幅 0・GridSplitter 非表示で、ステータスバーの開閉ボタンをクリックすると左ペインがオーバーレイ
  として右ペインの上に浮かぶ。UI 案は「ホバーで自動表示」と比較して**クリック起点のピン留め方式を採用**
  （写真選別中はマウス移動が多く、ホバー方式は誤発火・フリッカー対策のタイマー処理が必要になるため）。
  - **実装方式＝再ペアレントなしのオーバーレイ化**: `LeftNav`（`FolderNavigationView`）は root Grid の子の
    まま動かさず、`Grid.ColumnSpan=3`／`HorizontalAlignment=Left`／`Width=_lastLeftWidth`／`Canvas.ZIndex`
    の切替だけでフライアウト化する（WinUI TreeView は再ペアレントで内部状態が壊れるリスクがあるため）。
    `SplitView`（`DisplayMode=Inline/Overlay`）は不採用＝GridSplitter による幅調整（`LeftPaneWidth` 永続化済み）
    が失われるため。閉じるとき `Width` を `NaN` で解除し忘れると幅 0 カラムでも描画され続ける点に注意。
  - **フライアウトの部品**: `MainPage.xaml` に `FlyoutDismissLayer`（全 3 カラムを覆う Transparent Grid・
    ZIndex 10。クリックで閉じ、下の写真の誤選択を防ぐため e.Handled=true で消費）と `FlyoutBackdrop`
    （ZIndex 11 の Border。LeftNav 自体は背景を持たないため下に敷いて不透明化。背景
    `SolidBackgroundFillColorBaseBrush`＋右端 1px `CardStrokeColorDefaultBrush`＋`ThemeShadow`/
    `Translation="0,0,32"`）。LeftNav は開時 ZIndex 12。
  - **ピンボタン**: `FolderNavigationView` の root Grid に Row 0 を追加し右寄せ 28×28 ボタン
    （既存 4 行は +1 シフト）。グリフはピン中=`E77A`(UnPin)／解除中=`E718`(Pin)＝「次に起きる操作」を表示
    （`UpdatePinGlyph(bool)`＝`PhotoStatusBar.UpdateLeftPaneGlyph` と同パターンで MainPage が状態変化毎に呼ぶ）。
    状態管理は `TogglePinRequested` イベントで `MainPage.TogglePin()` へ委譲。ピン解除の瞬間はフライアウトを
    即開いてシームレスに引き継ぐ（消えない）／ピン留めの瞬間はフライアウトを閉じて `_lastLeftWidth` でドッキング。
  - **閉じる導線**: ①開閉ボタン再クリック（DismissLayer がステータスバーも覆うため実際はレイヤークリック
    扱い＝1 クリックで閉のみ・再オープンしない）②外側クリック ③Esc ④フォルダ読み込みで自動クローズ
    （`FolderNavigationView.FolderLoaded` イベント＝読み込みボタン・お気に入り/最近クリックの 2 箇所で発火。
    ピン留め時は `_flyoutOpen=false` なので no-op）。
  - **Esc の優先順位**: `MainWindow` の RootGrid `PreviewKeyDown` の Esc 分岐の**最上段**に
    「フライアウト表示中なら閉じる」を追加（段階的 Esc＝フライアウト＞選択解除＞全画面解除）。
  - **完全全画面（Shift+F）との整合**: 入るとき冒頭で `CloseLeftPaneFlyout()`（フライアウト状態は
    スナップショットに含めない＝解除後は閉で復帰）。出るときの `LeftSplitter` 復元を
    `_pinned ? Visible : Collapsed` に変更（ピン解除中にスプリッターが復活するバグを防止）。
  - **永続化**: `AppSettings.LeftPanePinned`（既定 true）。フライアウトの開閉状態は永続化しない
    （起動時は常に閉）。保存は既存 `SaveLeftPaneLayout()`（終了時 1 回）に相乗りで I/O 増なし。
    ピン解除中の保存は既存ロジックのまま collapsed=true/width=`_lastLeftWidth` が書かれるが、
    復元側（`RestoreLeftPaneLayout`）が `!pinned` なら `LeftPaneCollapsed` を無視するため問題ない。
  - **既知の軽微な制限**: ①ピン解除中にお気に入り/最近をクリックすると、フライアウトが閉じた後に
    `ExpandAndSelectFolderAsync` が幅 0 の非表示ツリーへ走るため、コンテナが realize されず展開・選択が
    効かないことがある（`RealizeContainerAsync` が 20 回リトライ後に graceful に諦める設計なので無害。
    次回フライアウトを開いたときツリー選択が追従していない可能性がある程度）。②フライアウト自体の
    幅ドラッグ調整は対象外（ドッキング時の幅 `_lastLeftWidth` を共用）。
  - 変更: `MainPage.xaml`（DismissLayer/Backdrop 追加）・`MainPage.xaml.cs`（`_pinned`/`_flyoutOpen`・
    `IsLeftPaneFlyoutOpen`・`OpenLeftPaneFlyout`/`CloseLeftPaneFlyout`/`TogglePin`・`ToggleLeftPane` 分岐・
    `Restore/SaveLeftPaneLayout`・`ToggleFullImageMode` 改修）、`MainWindow.xaml.cs`（Esc 最優先分岐）、
    `Controls/FolderNavigationView.xaml`（ピンボタン行）・`.xaml.cs`（`TogglePinRequested`/`FolderLoaded`/
    `UpdatePinGlyph`）、`AppSettings.cs`（`LeftPanePinned`）、resw（ja/en に `FolderNav_PinTooltip`/
    `FolderNav_UnpinTooltip`）、`CLAUDE.md`（要約更新）。**Core 非変更**。実装は Sonnet エージェントへ委譲し
    本体がレビュー。`BUILD SUCCEEDED`（x64 Debug）／`dotnet test` 113 件緑（Core 非変更のため件数不変）。
    実機目視をユーザー確認済み（2026-07-11、`70c895a`）。
  - **ピンボタンの移設（2026-07-11・追補）**: 当初はペイン最上部の専用行（Row 0）に単独配置していたが、
    その行はボタン以外に何も無く高さ（28px＋下マージン4px≒32px）がほぼ空白として無駄になっていた。
    → **専用行を廃し、最下部の「読み込み」行（旧 Row 4）にピンを右寄せで同居**させて解消。
    ボタン以外の要素が既にあるこの行なら高さの無駄が出ない。行定義は 5→4 に、お気に入り/最近/ツリーの
    `Grid.Row` を 1 つずつ繰り下げ、下部行は `StackPanel`→`Grid`（左=読み込み `HorizontalAlignment=Left`／
    右=ピン `HorizontalAlignment=Right`）に変更。ピンボタンの `x:Name`（`PinButton`/`PinIcon`）・クリック
    ハンドラ・グリフ切替（`UpdatePinGlyph`）は不変なので**コードビハインドは無変更**。案は「上部にペイン
    見出し＋ピン」「お気に入り Expander ヘッダーへ重ねる」も検討したが、前者は新ラベル＋ローカライズ増、
    後者は Expander シェブロンとのクリック干渉リスクがあり、最小変更で高さ無駄ゼロの本案を採用。
    変更は `Controls/FolderNavigationView.xaml` のみ。`BUILD SUCCEEDED`（x64 Debug）。実機目視をユーザー確認済み。

- **情報オーバーレイの 2 軸化＋評価バッジオーバーレイ 完了（2026-07-11・実機確認済み）**: プレビューの
  情報オーバーレイを「種類（`I` で巡回: 評価バッジ→詳細情報→オフ）」×「表示タイミング（`Shift+I`:
  常時⇄切替時のみ）」の 2 軸に再構成。評価バッジ＝★/旗/色ドットのみの小型オーバーレイ（詳細情報と同じ
  左上・種類は排他なので位置を共用。評価が全部空の写真ではバッジごと非表示＝
  `PhotoItemViewModel.HasAnyEvalVisibility`。内部はクラシック Binding＝WUI2010 回避の既存流儀）。
  - **「切替時のみ」の挙動**: 写真切替（`FocusedPhoto`）・焦点写真の評価変更・種類/タイミング切替・
    プレビュー入場（`IsPreviewMode`）をトリガに Opacity=1 → 1.0 秒保持 → 0.4 秒フェード
    （`QuadraticEase`/EaseIn。線形だと消え際が唐突）。選択中の種類がバッジ/詳細情報どちらでも同じ挙動
    （ユーザー合意済みの仕様）。実装は新規 partial `PreviewControl.OverlayFade.cs`＝単一 Storyboard
    （`DoubleAnimationUsingKeyFrames`）を `Storyboard.SetTarget` で 2 オーバーレイ間で使い回し。連打は
    `Begin()` 再スタートでタイマーリセット。`Begin()` 前に `Opacity=1` を同期代入（初フレーム待ちを排除）。
    フェード後も Visibility は触らず Opacity=0 のまま（`IsHitTestVisible=False` なので無害）。
  - **評価変更の検知**: 焦点写真の `PropertyChanged` を購読（焦点切替のたび付け替え式＝購読漏れ/リーク防止。
    `RatingStars`/`RatingVisibility`/`FlagVisibility`/`ColorDotsVisibility`/`HasAnyEvalVisibility` の名前
    セットでフィルタ）。キー・右クリック・一括評価のどの経路の変更でも拾える。
  - **状態と移行**: `MainViewModel.OverlayKind`（enum `InfoOverlayKind { Off, Badge, Full }` 新設）/
    `OverlayTransient`（bool）→ `AppSettings` へ永続化。旧 `AppSettings.ShowInfoOverlay`（bool）は移行用に
    残置し、`OverlayKind` が null（未設定）のときだけ true→Full / false→Off で引き継ぐ。既定＝詳細情報＋
    常時（旧動作踏襲）。
  - **メニュー**: ハンバーガー「プレビュー」サブ内の旧 `ToggleMenuFlyoutItem`（メタ情報オーバーレイ）を
    `MenuFlyoutSubItem`「情報オーバーレイ」に置換（`RadioMenuFlyoutItem` 2 グループ＝種類 3 択＋タイミング
    2 択、`Menu_Opening` で 5 項目の IsChecked 同期）。右クリックメニューへは追加しない（対象操作メニューに
    表示設定を混ぜない方針・ユーザー合意）。
  - 変更: `InfoOverlayKind.cs`（新規）・`Controls/PreviewControl.OverlayFade.cs`（新規）・`AppSettings.cs`・
    `ViewModels/MainViewModel.cs`・`ViewModels/PhotoItemViewModel.cs`・`Controls/PreviewControl.xaml(.cs)`・
    `Controls/PreviewControl.Input.cs`・`Controls/PhotoStatusBar.xaml(.cs)`・resw（ja/en）・`shortcuts.json`。
    **Core 非変更**。実装は Sonnet エージェントへ委譲し本体がレビュー（イージング追加・en 大文字小文字の
    微修正）。`BUILD SUCCEEDED`（x64 Debug・警告0）／`dotnet test` 113 件緑。実機目視をユーザー確認済み
    （2026-07-11）。

### 情報オーバーレイ「切替時のみ」の保持/フェード時間を設定化（2026-07-11）
- **背景**: 上記で保持1.0秒・フェード0.4秒を `PreviewControl.OverlayFade.cs` に `static readonly` で
  ハードコードしていた。ユーザー要望で保持・フェードとも ms 指定の設定に。既定は保持500ms／フェード400ms
  （フェードは従来値据え置き・保持のみ 1000→500 に短縮）。
- **設定モデル**: `AppSettings.OverlayTransientHoldMs`(=500)／`OverlayTransientFadeMs`(=400) を追加。
  設定ダイアログ「一般」タブに NumberBox 2 本のグループを新設（既存レート制限と同じ体裁・`SmallChange=100`）。
  保存は `PhotoStatusBar.MenuSettings_Click`、流し込み/既定復帰は `SettingsDialog`。
- **反映機構**: `OverlayFade.cs` の `OverlayHold`/`OverlayFade` を `static readonly` → インスタンス
  フィールド `_overlayHold`/`_overlayFade` 化。Storyboard はキーフレームに時間を焼き込む単一キャッシュ
  （`EnsureOverlayFadeStoryboard`）なので、新設 `ApplyOverlayFadeTimings(settings)` で値が変わったら
  キャッシュ済み Storyboard/Animation を null 化して次回トリガで再構築させる。切替時のみ表示中なら即
  再トリガして体感確認可。呼び出しは `PreviewControl.ApplyPreviewSettings` 末尾（起動時・設定保存時の
  両経路をカバー）。クランプ＝保持 0..60000ms／フェード 1..60000ms（フェード0は KeyTime 重複で消えないため下限1）。
- 変更: `AppSettings.cs`・`Controls/SettingsDialog.xaml(.cs)`・`Controls/PhotoStatusBar.xaml.cs`・
  `Controls/PreviewControl.OverlayFade.cs`・`Controls/PreviewControl.xaml.cs`・resw（ja/en）。**Core 非変更**。
  `BUILD SUCCEEDED`（x64 Debug）。

### 情報オーバーレイのタイミング/保持/フェードを種類別化（2026-07-12）
- **背景・要望**: それまでタイミング（常時/切替時）と保持/フェード時間は評価バッジ／詳細情報で**共有**していた。
  用途が異なる（バッジ＝一瞬光ればよい／詳細情報＝しばらく読ませたい）ため、種類ごとに独立して記憶・設定したい。
- **仕様**（ユーザー確定）: タイミング・保持・フェードを種類別に持つ。Shift+I（据え置き）と各メニューの
  「常時/切替時」は**選択中の種類**（＝現 `OverlayKind`）に作用し、**オフ時は無操作／メニューは両項目を無効化**。
  既定＝評価バッジ:切替時のみ・保持0ms/フェード400ms、詳細情報:常時・保持1000ms/フェード400ms。**旧設定からの移行はしない**
  （新既定で開始）。
- **設定モデル**: `AppSettings` に `BadgeTransient`(=true)/`FullTransient`(=false)、`BadgeHoldMs`(=0)/`BadgeFadeMs`(=400)/
  `FullHoldMs`(=1000)/`FullFadeMs`(=400) を新設。旧 `OverlayTransient`/`OverlayTransientHoldMs`/`OverlayTransientFadeMs` は
  旧 JSON 互換のためだけ残置（参照しない）。
- **VM**（`MainViewModel`）: 旧 `OverlayTransient`(共有 bool) を廃し `BadgeTransient`/`FullTransient` の 2 本に。
  派生 `CurrentOverlayTransient`（現 `OverlayKind` の値。Off は false）と `IsOverlayOn` を追加し、`OnOverlayKindChanged`/
  各 `On…TransientChanged` から `CurrentOverlayTransient` 変更通知。`ToggleOverlayTiming()`（Shift+I）と新設
  `SetCurrentOverlayTransient(bool)`（メニュー）は選択中の種類のみ操作（Off は no-op）。
- **フェード機構**（`PreviewControl.OverlayFade.cs`）: 保持/フェードを種類別の生 ms（`_badgeHoldMs` 等）で保持し、
  `CurrentOverlayTimings()` が現 `OverlayKind` の値を選択・クランプ。`RestartOverlayFade` 冒頭で
  `RebakeOverlayTimingsIfChanged()` を呼び、焼き込み済み(`_overlayHold`/`_overlayFade`)と違えば Storyboard キャッシュを
  破棄→次回再構築。これで種類切替＝時間切替も自動追従。`ApplyOverlayFadeTimings` は 4 値を控えるだけ。判定は
  `OverlayTransient` → `CurrentOverlayTransient` に置換。`PreviewControl.xaml.cs` の購読も
  `nameof(MainViewModel.CurrentOverlayTransient)` へ。
- **メニュー**: ハンバーガー（`PhotoStatusBar`）・メイン画像右クリック（`PreviewControl.ContextMenu.cs`）とも、
  タイミング 2 択を `CurrentOverlayTransient` 基準チェック＋`IsOverlayOn` で活性制御。
- **設定ダイアログ**: 「切替時のみ」の保持/フェードを評価バッジ用・詳細情報用の 2 グループ（計 4 NumberBox＝
  `BadgeHoldBox`/`BadgeFadeBox`/`FullHoldBox`/`FullFadeBox`）へ拡張。resw に `Settings_OverlayBadgeLabel`/
  `Settings_OverlayFullLabel` 追加、注記も種類別対応に更新。
- 変更: `AppSettings.cs`・`ViewModels/MainViewModel.cs`・`Controls/PreviewControl.OverlayFade.cs`・
  `Controls/PreviewControl.xaml.cs`・`Controls/PreviewControl.ContextMenu.cs`・`Controls/PhotoStatusBar.xaml.cs`・
  `Controls/SettingsDialog.xaml(.cs)`・`InfoOverlayKind.cs`・resw（ja/en）。**Core 非変更**。Opus が直接実装
  （委譲なし）。`BUILD SUCCEEDED`（x64 Debug）／`dotnet test` 126 件緑。**実機目視は未確認**。

## メイン画像の右クリックメニュー（プレビュー専用）（2026-07-11）

プレビューのメイン大画面（Win2D `MainCanvas`）を右クリック → プレビュー専用メニューを表示する。
フィルムストリップ／グリッドの `PhotoContextMenu` とは**別メニュー**で、**対象は常に焦点の1枚**
（選択集合は参照も変更もしない＝エクスプローラ流儀の対象付け替えを行わない。複数選択中の誤爆防止・ユーザー要望）。

- **メニュー構成**（上から。ユーザー指定の並び）: ズーム ▶（フィット／100%／縮小／拡大／─／倍率を指定 ▶＝
  設定「ズーム段」`AppSettings.ZoomStops` を動的列挙）／─／イマーシブ表示・全画面表示・完全全画面／─／
  EXIF 詳細パネル・情報オーバーレイ ▶（種類3択＋タイミング2択）・構図グリッド ▶（種類4択＋基準2択）／─／
  フラグ ▶・レーティング ▶・カラーラベル ▶（評価バッジの縦3行 旗→★→カラー と同順）／─／
  ファイルをコピー ▶・パスをコピー／─／エクスプローラーで表示・既定のアプリで開く・共有。
- **ズームの中心は右クリック位置**（ホイール/クリックズームと同じ流儀。「ズームしたい場所を右クリックする」
  操作を想定・ユーザー要望）。`RightTapped` 時点の座標を控えて各項目のクロージャへ渡す。
  倍率指定用に `PreviewViewport.SetDeviceScaleAround(deviceScale, cx, cy)` を新設
  （`ZoomToStop` と同じ DeviceScale→Scale 変換。Mode=Custom）。
- **チェック表示**: フィット/100%/各倍率段は `GroupName="PvZoom"` の単一 Radio グループ
  （フィット中は Mode==Fit、倍率は `DeviceScale` の相対誤差 1e-3 未満で一致判定＝`IsAtDeviceScale`。
  段外の中間倍率ではどこにもチェックが付かない）。イマーシブ/全画面/EXIF パネルは Toggle、
  オーバーレイ／グリッドは既存 VM プロパティ（`OverlayKind`/`OverlayTransient`/`GridKind`/`GridReference`）
  への一発代入 Radio（永続化は既存経路）。メニューは開くたび使い捨て構築なので開いた時点の値を代入するだけ。
- **実体の共有（二重化なし）**: 評価3サブは `PhotoContextMenu.AddRatingSub/AddColorSub/AddFlagSub` を
  internal 化し第1引数を `IList<MenuFlyoutItemBase>` に変更して共用（`Item` ヘルパも internal 化）。
  ファイル系は既存 `AddFileItems`（targets=焦点1枚・suffix=""・アクセラレータ表示あり）。
  表示系はキー処理と同じ実体（`ToggleImmersive`/`ToggleExifPanel`/`SetFit` 等）を呼ぶ。
- **全画面系の配線**: `PreviewControl` に `ToggleFullScreenRequested`/`ToggleFullImageRequested` イベント＋
  `IsFullScreenProvider` を追加（`PhotoStatusBar` と同パターン）し、`MainPage` で StatusBar 配線の隣に同経路で配線。
- **パンを左ボタン限定化**: `MainCanvas_PointerPressed` で `IsLeftButtonPressed` のときのみパン開始＋
  ポインタキャプチャ（右クリックはメニュー用のため）。`Focus()` はボタン種別に関わらず従来どおり実行
  （右クリック後のキー操作継続のため）。従来は右ドラッグでもパンした。
- 新規: `Controls/PreviewControl.ContextMenu.cs`。変更: `PhotoContextMenu.cs`（internal 化＋引数型変更）・
  `Controls/PreviewViewport.cs`（`SetDeviceScaleAround`）・`Controls/PreviewControl.xaml`（`RightTapped` 配線）・
  `Controls/PreviewControl.MainCanvas.cs`（左ボタン限定パン）・`MainPage.xaml.cs`（全画面系配線）・
  resw（ja/en に `PvCtx_*` 22 キー）。**Core 非変更**。実装は Sonnet エージェントへ委譲し本体がレビュー。
  `BUILD SUCCEEDED`（x64 Debug・警告0）／`dotnet test` 113 件緑。実機目視をユーザー確認済み（2026-07-11）。

## メニュー統一（ハンバーガー/右クリック×2）（2026-07-11）

グリッド／フィルムストリップの右クリックメニュー（`PhotoContextMenu`）とハンバーガーメニュー
（`PhotoStatusBar`）を、先行実装済みのメイン画像右クリックメニュー（`PreviewControl.ContextMenu`）に
仕様を揃える改修。Core は非変更。

- **評価サブメニューの並び統一**: `PhotoContextMenu.Show()` の呼び出し順を
  `AddRatingSub→AddColorSub→AddFlagSub` から **`AddFlagSub→AddRatingSub→AddColorSub`**（フラグ→★→カラー）へ。
  メイン画像メニューは元々この順だったため対称化。
- **右クリックメニューにもショートカット表示**: `AddFileItems(..., withAcceleratorText: true)` に変更
  （表示専用。実キー処理は既存の `PhotoFileCommands`/`MainWindow` 集約のまま）。
- **単一対象時は評価サブメニューに現在値チェック**: `targets.Count == 1` のときだけ
  フラグ／レーティングを `RadioMenuFlyoutItem`（排他グループ `CtxFlag`/`CtxRating`）に、
  カラーラベル各色を `ToggleMenuFlyoutItem` にして `Eval` から現在値を反映。複数対象時は従来どおり
  チェックなしの `MenuFlyoutItem`（「現在値」が一意に定まらないため）。カラーのクリックは変わらず
  `ToggleColorLabel`（トグル維持・ユーザー要望）。ヘルパ `RadioItem`/`ToggleItem` を `PhotoContextMenu` に追加
  （`PreviewControl.ContextMenu` の同名ヘルパとは別実装＝呼び出しシグネチャが異なるため共用しない）。
- **カラーラベルに「クリア」を追加**: `AddColorSub` の先頭に全色解除の項目（新 resw `Ctx_ColorClear`）＋
  区切り線を追加。実装は `PhotoItemViewModel.ClearColorLabels()`（新規。付与済みの色だけ既存
  `ToggleColorLabel` を回す＝保存・通知ロジックの二重実装を避ける）。
- **ハンバーガーメニュー再構成**:
  - イマーシブ表示を「プレビュー ▶」の外＝トップ項目へ移動。**グリッド表示中でも押せる**ようにし、
    その場合はクリックで `MainViewModel.EnterPreview()` してからイマーシブ ON にする
    （`Shift+F` の `ToggleFullImageMode` と同じ「入りながら適用」流儀）。写真が0枚の間だけ無効化。
  - 「プレビュー ▶」内に **EXIF 詳細パネル**（`ToggleMenuFlyoutItem`）を新設。
    `PreviewControl.ToggleExifPanel()` を `private→public` 化＋`IsExifPanelShown` プロパティを追加して
    メニューから呼べるようにした（`PhotoStatusBar` に `ToggleExifPanelRequested`/`IsExifPanelProvider`
    を追加し `MainPage` で配線。既存 `ToggleImmersiveRequested`/`IsImmersiveProvider` と同パターン）。
  - **構図グリッドを巡回式2項目からラジオ選択式に置き換え**（メイン画像メニューと同構成＝
    種類4択 `GroupName="MenuGridKind"` ＋ 基準2択 `GroupName="MenuGridRef"`）。旧
    `MenuGridKind_Click`（`CycleGridKind`）／`MenuGridRef_Click`（`ToggleGridReference`）は削除し、
    各項目が `MainViewModel.GridKind`/`GridReference` へ一発代入する方式に統一。
  - 新規 resw キー（ja/en）: `Menu_ExifPanel`／`Menu_GridSub`／`Menu_GridNone`／`Menu_GridCross`／
    `Menu_GridThirds`／`Menu_GridSquare`／`Menu_GridRefImage`／`Menu_GridRefCanvas`（文言はメイン画像メニューの
    `PvCtx_Grid*`/`PvCtx_ExifPanel` と同一にコピー）。廃止: `Menu_GridKind`／`Menu_GridRef`。
- 変更ファイル: `PhotoContextMenu.cs`／`ViewModels/PhotoItemViewModel.cs`（`ClearColorLabels`）／
  `Controls/PreviewControl.ExifPanel.cs`（`ToggleExifPanel` public 化＋`IsExifPanelShown`）／
  `Controls/PhotoStatusBar.xaml`・`.xaml.cs`／`MainPage.xaml.cs`（配線2件追加）／
  resw（ja/en、`Menu_*`/`Ctx_ColorClear` 追加、`Menu_GridKind`/`Menu_GridRef` 削除）。
  `BUILD SUCCEEDED`（x64 Debug・警告0）／`dotnet test` 113 件緑（Core 非変更のため件数不変）。
  実機起動確認は未実施（マルチインスタンス増殖のため本エージェントでは起動せず）。

- **フィルタ下部のファイル操作再編＋「ファイルを移動」新設（2026-07-12）**: フィルタフライアウト下部を
  「ファイル名をコピー（DropDown）／ファイルをコピー（DropDown）／ファイルを移動…／区切り／未評価をReject移動（最下段）」
  に再編。誤操作しやすい Reject 移動を区切り線の下・最下段へ隔離した。実装は Sonnet エージェントへ 2 段委譲
  （①バックエンド ②メニュー再編。①と②の間でセッションリミット中断→SendMessage で再開）。
  - **「パスをコピー」→「ファイル名をコピー」（全メニュー同時適用）**: サブメニューを
    「ファイル名のみ：表示中のファイル／ファイル名のみ：関連ファイルを含める（RAW 等）／─／
    フルパス：表示中のファイル（Ctrl+Alt+E 併記）／フルパス：関連ファイルを含める（RAW 等）」の4択に拡張
    （ラベルは全角コロン「：」区切り・ユーザー指定）。実体は `PhotoContextMenu.AddCopyNameItems` /
    `AddCopyFileItems` に切り出して右クリック（グリッド/フィルムストリップ）・メイン画像メニュー・
    ハンバーガー「ファイル」・フィルタバーの全箇所で共有（`AddFileItems` は薄いラッパ化）。
    名前のみコピーは `PhotoFileCommands.CopyNames`/`CopyNamesWithSiblingsAsync`（`ExpandSiblings` 共有＋
    `Path.GetFileName`。重複除去はしない＝フルパス版と件数一致を優先）。resw キーは `Ctx_CopyPath`→`Ctx_CopyNames`
    へリネーム＋子4キー追加（ja/en）。
    **フィルタバー側は `withAcceleratorText:false`**（Ctrl+Alt+E の実処理対象は選択集合であり、フィルタバーの
    対象＝絞込結果と異なるため誤解を招く表示をしない）。DropDown の `MenuFlyout` は Opening のたびに
    `vm.GetCopyTargets()` で再構築（開いたまま絞込が変わり得るため）。
  - **「ファイルを移動…」新設（旧「移動batを生成してコピー」の後継・実行まで面倒を見る）**: 絞込結果を任意の
    宛先へ bat 経由で移動。Core 純関数 `FileMove.BuildBatch`（新規＋xUnit 5件）は Reject 移動と同形だが、
    **`FROMDIR` は移動元の絶対パス**（`..` 相対不可）・**move の両引数を引用符で囲む**（空白入りパス対応）。
    フィルタ条件の `@rem` は旧 `ClipboardExport.BuildMoveBatch` から引き継ぎ。フローは
    `BatchFlows.RunMoveAsync`＝`MoveFilesDialog`（宛先＋参照＝`NativeFolderPicker`・未入力/移動元同一の検証）→
    同名衝突チェック（`FindRejectCollisions` を `FindCollisions(destDir,…)` に一般化）→ bat 確認 →
    宛先へ `Move_yyyyMMddHHmmss.bat/.log` 保存・実行 → 移動元を再読込。宛先は
    `MainViewModel.LastMoveDestination` に**セッション中だけ**記憶（`LastCopyDestination` とは別枠・非永続）。
    bat 保存＋cmd 実行の共通部は 3 系統（Reject/CopyRename/Move）で `SaveAndRunBatchAsync` に集約（挙動不変）。
    ワイルドカード `<拡張子なし名>*` の前方一致（`DSC0001*` が `DSC00010` も拾う）は Reject 移動と同じ既知の癖。
  - **廃止**: Core `ClipboardExport.cs`（ファイル名一覧＝「ファイル名のみ：表示中」に吸収／移動bat生成＝
    「ファイルを移動…」に置換）＋テスト 3 件、`MainViewModel.BuildFileNameListText`/`BuildMoveBatchText`、
    フィルタバーの単独「リネームしてコピー…」ボタン（「ファイルをコピー」配下へ統合＝右クリックと同構成）。
    resw の `Filter_CopyDropDown`/`Filter_CopyFileNamesMenu`/`Filter_CopyMoveBatchMenu`/`Filter_CopyRenameButton` 削除。
  - 変更/新規: Core `FileMove.cs`（新）・`ClipboardExport.cs`（削除）・`RejectMove.cs`/`PhotoFilter.cs`（cref 文言のみ）、
    App `PhotoContextMenu.cs`・`PhotoFileCommands.cs`・`BatchFlows.cs`・`ViewModels/MainViewModel.cs`・
    `Controls/FilterBar.xaml(.cs)`・`Controls/MoveFilesDialog.xaml(.cs)`（新）・resw（ja/en）、
    tests `FileMoveTests.cs`（新）・`ClipboardExportTests.cs`（削除）。SPEC §3-5 と構成図も現行仕様へ更新。
    `dotnet test` **115 件緑**／`BUILD SUCCEEDED`（x64 Debug・警告 0）。実機目視（メニュー表示・移動実行）は未実施。

## Alt キー押下時のシステム ビープ抑止（2026-07-12）

**症状**: プレビューで `Alt+F`（フォーカス点へスクロール）や `Alt+数字`（一括評価）など「Alt+英数字」を
押すたびに Windows 標準の「ポーン」というビープが鳴る。該当キーはコード側で正しく処理され
`e.Handled=true` も立てているのに鳴る。

**原因**: XAML の `KeyRoutedEventArgs.Handled` が効くのは XAML 入力レイヤーのみで、その下の Win32
メッセージ処理までは止められない。Alt を伴うキーは HWND のメニュー活性化経路に入り、`WM_SYSKEYDOWN`→
`WM_SYSCHAR`→メニュー ニーモニック照合へ進む。アプリはメニューバーを持たず `F`/`1` 等に対応する
ニーモニックも無いため、`WM_MENUCHAR` の既定処理（`MNC_IGNORE`）が `MessageBeep` を鳴らす。音はアプリの
コードではなく OS のメニュー処理から出ている。Ctrl 系・修飾子なしはこの経路に入らないので鳴らない。

**修正**: `MainWindow` の HWND を comctl32 の `SetWindowSubclass` でサブクラス化し、`WM_MENUCHAR`(0x0120) を
横取りして `MNC_CLOSE`（HIWORD=1）を返す＝メニューモードを無音で閉じる。`Alt+F4`/`Alt+Space` は
`WM_SYSCOMMAND` 経路なので影響しない。実装上の注意:
- comctl32 のサブクラス API は**名前ではなく序数でのみエクスポート**される（SetWindowSubclass=410／
  RemoveWindowSubclass=412／DefSubclassProc=413）。`DllImport` は `EntryPoint="#410"` 等の序数指定にする
  （名前指定は `EntryPointNotFoundException`）。
- サブクラス デリゲートは GC 回収されないようインスタンス フィールド（`_menuBeepSubclassProc`）で保持し、
  `MainWindow_Closed` で `RemoveWindowSubclass` して解除する。
- 変更ファイル: `src/PhotoQuickSelector.App/MainWindow.xaml.cs` のみ。`BUILD SUCCEEDED`（x64 Debug）。
  ビープ消失は音を伴うため**実機での聴取確認が未了**（computer-use の合成 Alt キーは不安定＝メモリ
  `winui-keyinput-gotchas` 参照）。

## bat 生成・実行の空白/特殊文字対策（2026-07-12）

bat 経由のファイル操作 3 系統（Reject 移動／リネームコピー／ファイル移動）における、空白・cmd 特殊文字
入りの名前の扱いを調査し、2 件対応した（実装は Sonnet エージェントへ委譲・本体レビュー済み）。

- **調査結果（対応前の挙動）**: 空白入りファイル名は Reject 移動のみ失敗（move 引数が無引用符のため
  トークン分解＝「`DSC001 - コピー.JPG`」等で移動漏れ。途中行の失敗は exit code 0 のまま成功扱いになる）。
  FileMove/CopyRename は引用符済みで空白は問題なし。特殊文字は全系統に穴: `%` は**引用符内でも**変数展開で
  行が化ける／`&` `^` は無引用符箇所（`set FROMDIR=` 行・パスやテンプレートが生で入る `@rem` 行＝`rem` も
  `&` で分断され後続がコマンド実行される）で危険。`| < > "` はファイル/フォルダ名に使えないため考慮不要。
  `!` は遅延展開不使用で安全、`( )` も現構造では安全（`photo (1).jpg` は実在しやすいのでブロックしない）。
- **対応 1: Reject 移動の move 行を引用符付きに**（`RejectMove.cs`）: `move "%FROMDIR%\<名>*" "%TODIR%"`＝
  FileMove と同形式。空白入り名で正常動作。既存動作は不変（引用符は move に透過）。
- **対応 2: 危険文字 `% & ^` を含むと実行拒否**（エッジケースにつきエスケープはせず一律ブロック）:
  - Core 新規 `BatchSafety.cs`＝`UnsafeChars="%&^"`／`ContainsUnsafe`／`FindUnsafe`（純関数＋xUnit 10 件）。
  - App `BatchFlows.EnsureBatchSafeAsync`（private）＝該当があれば通知ダイアログ（新 resw キー
    `Msg_UnsafeBatchChars`・列挙は衝突表示と同じ 20 件打ち切り）で中断。3 フローの bat 生成前に挿入。
    検査対象＝対象ファイル名＋bat に埋まるパス（`CurrentFolder`・移動/コピー先）＋リネームテンプレート
    （**解決後の新名は検査不要**＝テンプレート・フォルダ名・元名・日付数字のみから合成されるため入力側で網羅）。
- 変更/新規: Core `RejectMove.cs`・`BatchSafety.cs`（新）、App `BatchFlows.cs`・resw（ja/en）、
  tests `RejectMoveTests.cs`（期待値 3 箇所更新＋空白名 1 件追加）・`BatchSafetyTests.cs`（新）。
  `dotnet test` **126 件緑**／`BUILD SUCCEEDED`。実機での拒否ダイアログ目視は未実施。

## v0.2.0 リリース（2026-07-12）

v0.1.0（2026-07-02・初回公開／Pre-release）以降の 48 コミットをまとめて **v0.2.0** として公開。
GitHub Release に Pre-release で添付。csproj `<Version>` を 0.2.0 に更新。

- **配布形態を単一ファイル版へ変更（2026-07-12・ユーザー合意）**: 当初 v0.1.0 を踏襲して `win-x64` フォルダ発行
  （527 ファイルの zip）を添付したが、「展開版ではなく単一 exe 版が良い」との要望で `win-x64-singlefile` に差し替え。
  - 発行 `dotnet publish -c Release -p:Platform=x64 -p:PublishProfile=win-x64-singlefile`。出力は 5 ファイル
    （`PhotoQuickSelector.App.exe` 約292MB＋`LICENSE`＋`THIRD-PARTY-NOTICES.txt`＋`.pdb`×2）。`resources.pri` は残らない。
  - zip には **exe＋LICENSE＋THIRD-PARTY の3点のみ**同梱（`.pdb` は配布不要のため除外）。`PhotoQuickSelector-0.2.0-win-x64.zip`
    ＝約104MB。`gh release upload v0.2.0 … --clobber` で同名添付を置換。exe 埋め込み `FileVersion`＝0.2.0.0。
  - 制約: 完全な単一ファイルではない（LICENSE 等が並ぶため zip 配布は維持）。初回起動でネイティブ DLL を
    自己展開するためひと呼吸の遅延あり。**以後の Release 添付は単一ファイル版を既定**とする（CLAUDE.md 配布節）。
  - 未実施: 発行済み単一ファイル exe の実起動スモークテスト（localization=resources.pri 非残置でも動くかの目視は
    ユーザー確認推奨。過去に単一ファイル発行は検証済み＝メモリ `winui-seticon-singlefile` 参照）。

- **v0.1.0 → v0.2.0 の主な変更**（利用者視点）:
  - 複数選択・一括評価: マウス `Ctrl`/`Shift` クリック、`Ctrl+A` すべて選択、選択集合への一括評価/一括フラグ
  - 左ペインのピン留め/フライアウト表示（`AppSettings.LeftPanePinned`）
  - 情報オーバーレイの 2 軸化（評価バッジ／詳細情報・`I` 種類巡回／`Shift+I` 常時⇄切替時のみ・
    保持/フェード時間を種類別に設定化）
  - メイン画像の右クリックメニュー（プレビュー専用・対象は焦点の 1 枚固定）
  - メニュー統一（右クリック／ハンバーガー・評価順を旗→★→カラーに統一・現在値チェック表示）、
    「ファイル名をコピー」4 択化、「ファイルを移動」新設、「パスをコピー」サブメニュー化
  - EXIF 詳細パネル（`E`／タブ切替・全ディレクトリ全タグ・キーボードスクロール対応）
  - bat 生成の空白/特殊文字対策（Reject 移動の引用符付与・危険文字 `% & ^` は実行拒否）
  - `Alt`+英数字押下時のシステムビープ抑止、`Alt+F` フォーカス点移動でのズーム倍率維持
- リリース作業: バージョン更新コミット → `main` へ push → `v0.2.0` タグ → `gh release create`（Pre-release・
  `PhotoQuickSelector-0.2.0-win-x64.zip` 添付）。

## EXIF 詳細パネルの XMP タグ展開（2026-07-16）

**症状**: EXIF 詳細パネルの XMP グループに `XMP Value Count` の 1 行しか出ず、中身（`xmp:Rating` 等）が見えない。

**原因**: MetadataExtractor の `XmpDirectory` だけは他のディレクトリと構造が違い、`Tags` に持つのは
`XMP Value Count`（プロパティ数）1 件のみ。実体は `XmpMeta` 側にあり `GetXmpProperties()`
（パス→値の辞書）経由でしか取れない。`ExifTagReader.ReadAllTags` は全ディレクトリを `directory.Tags` で
一律に舐めていたため、XMP はカウントだけが表示されていた。

**修正**（Core のみ・App と VM のキャッシュは無変更）:
- `ExifTagReader.ReadAllTags` に `directory is XmpDirectory` の分岐を追加し、`GetXmpProperties()` の
  各エントリを `ExifTagEntry(パス, 値)` として展開（タグ名は `xmp:Rating` 形式のプレフィックス付きパスを
  そのまま表示＝「タグ名は MetadataExtractor の英語名のまま」の方針と一貫）。
- 並びはパスの序数ソート。辞書の順序は保証されず、ソートすると `crs:` / `dc:` / `xmp:` とプレフィックス単位で
  まとまって読みやすい。
- 空値スキップと 300 文字切り詰めは通常タグと共有するため `AddEntry` ヘルパへ切り出した（挙動不変）。
- `XMP Value Count` の行は残す（展開結果との突き合わせができるため）。
- テスト 1 件追加（Sony 画像の XMP グループに `xmp:Rating` があること）＝`dotnet test` **131 件緑**。

**実データの中身**: カメラ出し JPEG（Sony α1／Olympus OM-1 とも）の XMP は `xmp:Rating` のみで、展開しても
増えるのは Rating 程度（実ファイルの XMP パケットを直接ダンプして確認）。Lightroom／Bridge を通した
ファイルでは `dc:`（タイトル・キーワード）・`photoshop:`・`crs:`（現像設定）が数百件入り得るが、パネルは
既に Olympus の約 500 タグを仮想化 ListView で捌いているため規模は問題にならない。

**検証**: Core を直接叩く使い捨てコンソールで XMP グループをダンプし、`XMP Value Count = 1` ＋
`xmp:Rating = 0` の 2 行が出ることを両機の実画像で確認（アプリ画面での目視は未実施）。

## バージョン情報ダイアログにデータの保存先を表示（2026-07-25）

**きっかけ**: 日常版（Release）/開発版（Debug）で設定フォルダと評価データのファイル名を分離した結果
（CLAUDE.md「既知の注意点」）、「いま動いているアプリがどのファイルを読み書きしているのか」を画面から
確認できなかった。設定ダイアログ／バージョン情報／ハンバーガーメニューの 3 案を比較し、
**バージョン情報ダイアログ（About）に載せる案**をユーザーが選択。

**要点＝表示するパスは読み書きに使うパスと違う**: `AppSettings` は packaged/unpackaged 双方で使える素の
`%LOCALAPPDATA%\{フォルダ名}\settings.json` を読み書きに使うが、**packaged 実行では MSIX のファイルシステム
仮想化で実体が `…\Packages\{PFN}\LocalCache\Local\` 配下へリダイレクトされる**。この文字列をそのまま表示すると
エクスプローラで開けないパスになるため、表示用に実体を解決するプロパティを別に設けた。

- `AppSettings.SettingsFileDisplayPath`（新規・public）: `Windows.Storage.ApplicationData.Current.LocalCacheFolder.Path`
  を try/catch で取得し、取れたら（＝packaged）`<LocalCache>\Local\{フォルダ名}\settings.json` を返す。
  例外（＝unpackaged）なら読み書き用の `SettingsPath` をそのまま返す。読み書き経路は従来どおり無変更。
- **`dotnet run` は packaged 起動**（csproj の `Microsoft.Windows.SDK.BuildTools.WinApp` がデバッグ ID を登録して
  AUMID 起動する）。従来コメント/CLAUDE.md の「`dotnet run` 等 unpackaged の開発起動」は誤りだったので修正。
  実測での開発時の実体は `%LOCALAPPDATA%\Packages\60348750-46D8-401F-865F-CB8EC131C5A0_1z32rh13vfry6\LocalCache\Local\PhotoQuickSelector.Dev\settings.json`。
- **About の内容**: 既存の横並び（アイコン＋アプリ名/バージョン/著作権/GitHub）を縦 `StackPanel` で包み、区切り線の下に
  「設定ファイル」＝パス（`IsTextSelectionEnabled`・折り返し）＋ボタン 2 つ（エクスプローラで表示／パスをコピー）と、
  「評価データ: {ファイル名}（写真フォルダごとに作成されます）」の 1 行（`AppSettings.DatabaseFileName`）。
  幅は 560／`ContentDialogMaxWidth` を 700 に拡張（既定 ≈548 だと長いパスが細かく折り返す）。
- **エクスプローラで表示**: ファイルがあれば `/select` 表示、無ければ**実在する直近の親フォルダ**を開く
  （表示のためにフォルダを作りはしない＝副作用を持たせない）。`PhotoFileCommands` の `ShowInExplorer` /
  `SetClipboardText` を private → public 化して再利用（`/select` の引数作法を 2 箇所に散らさないため）。
- 変更/新規: `AppSettings.cs`（`SettingsFileDisplayPath` 追加・`SettingsFileName` 定数化・コメント修正）、
  `PhotoFileCommands.cs`（2 メソッド公開）、`Controls/AboutDialog.xaml(.cs)`、resw（ja/en 各 4 キー：
  `About_SettingsFileHeader.Text`／`About_ShowSettingsFileButton.Content`／`About_CopySettingsPathButton.Content`／
  `About_DatabaseFileFormat`）。`BUILD SUCCEEDED`（x64 Debug）／`dotnet test` **131 件緑**。
  実機目視（About のレイアウト・2 ボタンの動作）はユーザー確認推奨。

## グリッドの空状態表示＋フィルタボタンの状態色（2026-07-25）

**きっかけ＝「フォルダ内容が正常に表示されない」の調査**。ユーザーの開発版 settings.json で起動すると
`D:\…\2026_日常系\20260718`（JPG 1281＋ARW 1281）が何も表示されなかった。原因は**復元された絞り込み条件**で、
`LastSession.Filter` が `Enabled=true / RatingValue=2 / ≧` の一方、当該フォルダには評価 DB が存在せず
（`PhotoQuickSelector.sqlite3` も `.Dev.sqlite3` も無し）評価は XMP の `xmp:Rating` のみ。実測分布は
rating=0 が 1274 枚・rating=1 が 7 枚で **★2 以上が 0 枚**＝`Photos` が空になり、グリッドが無地になっていた。

**「読み取りが遅い」説は実測で否定**（同フォルダ・実データ）:

| 処理 | 実測 |
|---|---|
| `Directory.GetFiles` ＋拡張子選別 | 8 ms（2562 エントリ→JPEG 1281） |
| `MetadataReader.Read` × 1281（`Parallel.For`） | 527 ms（逐次 1.5 s／中央値 1.0 ms・最大 4.3 ms） |
| ソート | 3 ms |
| `MetadataStore.LoadEvaluation` × 1281 | ≒0（DB 不在時は `EnsureConnection(false)` が即 false） |
| `LoadThumbnailsAsync`（シェルサムネイル 1281 枚・`_shellGate` で直列） | **14.8 s**（中央値 11.2 ms・p99 15.4 ms・常駐 38.3 MB） |

`LoadFolderAsync` は 1 秒未満で返る。裏で 15 秒回り続けるのは先読みだけで、表示が出ない直接原因ではない。
なお `ProgressRing` は `IsLoading`（＝`LoadFolderAsync` 中）にしか連動しないので、先読み中は回らない。

**対策＝案A「グリッド領域の空状態」**（他に InfoBar 常設・ステータス文言のみ・フィルタボタン強調を比較）。
視線が最初に行く「本来サムネイルが並ぶ場所」に理由を出すのが本質という判断。ユーザー指示で
**アイコン＋1 行のみの最小構成**とし、件数・絞り込み条件・復帰ボタンは**フィルタボタンと重複するため載せない**。

- `EmptyStateKind`（新規 enum）＝`None`/`NoFolder`/`NoPhotos`/`FilteredOut`。`MainViewModel.EmptyState` が
  `IsLoading` → `CurrentFolder` → `AllPhotos.Count` → `Photos.Count` の順で判定する。
  **読み込み中は `None`**（確定前に出すとちらつくため）。
- `Controls/EmptyStateView`（新規）: 48px の `FontIcon`＋`SubtitleTextBlockStyle` の 1 行。二次色・非太字
  （エラーではなく状態なので落ち着かせる）。`IsHitTestVisible=False` でクリックはグリッドへ透過。
  グリフは Folder(`E8B7`)／Photo(`E91B`)／Filter(`E71C`)。**Segoe Fluent Icons に斜線入りのグリフが無い**ため、
  「〜が無い」ケース（`NoPhotos`/`FilteredOut`）だけ `Path`（`M 7,41 L 41,7`）を重ねて斜線を描く。
  `NoFolder` は不在ではなく次操作の案内なので斜線なし。
- 置き場所は `PhotoGridView.xaml`（`GridView` を `Grid` で包んで重ねる）。プレビュー中は `Photos` が空だと
  `EnterPreview()` が no-op で入れないため、**フィルムストリップ側には出さない**。
- **フィルタボタンの状態色**（同時実施）: アイコン＋件数の文字色を `MainViewModel.FilterIndicatorBrush` で
  白＝絞り込みなし／`#99E4FF`＝絞り込み中／`#FCE100`＝絞り込み中かつ 0 件、の 3 段に
  （絞り込み中は当初 `#60CDFF`＝Dark のアクセント文字色にしたが、実機目視で暗いというユーザー指摘があり
  同系のまま明度を上げた）。**ボタンの枠は既定のまま**
  （枠まで塗るとステータスバーが騒がしく、既定の 2 トーン枠も失われる）。テーマは Dark 固定（`MainWindow` の
  `RootGrid`）なので `ThemeResource` は VM から引けず、`PhotoItemViewModel` の評価色と同じくリテラル指定。
- Core に `PhotoFilter.HasConditions`（`Enabled` を見ず「条件が設定されているか」だけを返す）を追加。
  `Enabled=true` でも条件ゼロなら全件通過するため、状態色を出し分けるのに必要。
- 更新の起点は `MainViewModel.NotifyPhotoCountChanged()` に集約（件数・空状態 4 プロパティ・状態色）。
  呼び出し元は `ApplyFilter` / `LoadFolderAsync` 冒頭 / `OnCurrentFolderChanged` / `OnIsLoadingChanged`。
- 変更/新規: `Core/PhotoFilter.cs`、`EmptyStateKind.cs`、`Controls/EmptyStateView.xaml(.cs)`、
  `Controls/PhotoGridView.xaml(.cs)`、`Controls/FilterBar.xaml`、`ViewModels/MainViewModel.cs`、
  resw（ja/en 各 3 キー: `Empty_NoFolder`／`Empty_NoPhotos`／`Empty_FilteredOut`）、
  `tests/PhotoFilterTests.cs`（`HasConditions` 6 件）。`BUILD SUCCEEDED`（x64 Debug）／`dotnet test` **137 件緑**。

**実機目視はユーザー確認済み**（2026-07-25）。この作業環境からは `winapp run` が `CoreMessagingXP.dll` で
fail-fast（`0xc0000602`）して即終了し起動確認ができなかったため、画面確認はユーザーが実施
（変更前の時点で 5 回再現＝環境要因。イベントログ上、過去 14 日で通常利用時のクラッシュは無し）。

**副次の知見**: 読み込みエラー（`LoadFolderAsync` の catch）でも `AllPhotos` が空なので空状態は
「JPEG がありません」を出す。実際の理由はステータスバーのエラー文言側に出る。専用の状態は設けていない。

## シャッター速度の表示（1 秒以上を秒表記に）（2026-07-27）

**症状**: EXIF 撮影設定チップのシャッター速度が、1 秒以上でも「1/1」「4/1」のような分数表示になる。

**原因**: `MetadataReader.ReadExposureTimeDescription` が `Rational.ToString()`（＝分子/分母をそのまま
連結）を返していた。EXIF は 1 秒を `1/1`、4 秒を `4/1`、1.3 秒を `13/10` で持つため、そのまま出ていた。
他の `*Description`（焦点距離・絞り・ISO・露出補正）は `MetadataCalc` の純関数なのに、SS だけ Reader 内に
ベタ書きだったという不揃いも同時に解消。

**修正**（Core のみ。App は無変更＝`ExifChips`／`InfoLine` が Core の文字列をそのまま流すため、
プレビュー／グリッドのオーバーレイ／ステータスバーへ自動反映）:
- `MetadataCalc.ExposureTimeDescription(double seconds)` を新設。0 以下・NaN・∞ は空文字／1 秒以上は
  小数第1位・ゼロ落ちで `1s` `1.3s` `30s`／1 秒未満は逆数が整数に近いときだけ `1/250` `1/3`。
- `ReadExposureTimeDescription` は `rational.ToDouble()` を渡すだけに縮小（分母 0 は `ToDouble` 経由で
  0／NaN になりガードに吸収されるので、旧 `IsZero` チェックは不要）。

**落とし穴**: 1 秒未満を一律 `1/round(1/s)` にすると壊れる。0.8 秒（EXIF `8/10`）は逆数 1.25 で、丸めると
「1/1」という別物になる。カメラ背面と同じく「1/3 秒より遅い側は小数秒」で表示させたいので、
**逆数と整数の相対誤差 2% 以内のときだけ分数化**し、外れたら秒の小数表記へ落とす判定にした
（`1/3`＝逆数ちょうど 3 は分数のまま、`0.8` は `0.8s`）。

**表記の決定**（ユーザー合意）: 分数側に単位 `s` は付けない（`1/250` のまま）。混在するが、カメラ・現像
ソフトの慣習に沿っており、既存チップの見た目も変わらない。1 秒未満の中途半端な値の修正も同時に実施。

- 変更: `Core/MetadataCalc.cs`、`Core/MetadataReader.cs`、`tests/MetadataCalcTests.cs`（13 件追加）。
  `dotnet test` **150 件緑**／`BUILD SUCCEEDED`（x64 Debug）。実画像での目視確認は未実施
  （長秒露光のテストデータが手元テストフォルダに無いため）。

## デバッグ用の強制 GC（`M` キー）とメモリ現況表示（2026-07-28）

**目的**: 「毎秒 4〜5 回のペースで ←/→ を押すとメモリ使用量が微増するが、放っておくと減っている」という
観察に対し、**回収が追いついていないだけなのか、本当に増え続けるのか**を切り分ける。任意のタイミングで
フル GC を打って落ちるなら前者、という検証を可能にする。旧アプリにあった `M` キーの移植でもある
（SPEC §3-7 では「未実装（意図した割り切り。必要になれば追加）」としていた項目を、必要が出たので実装）。

**実装**:
- `MemoryDiagnostics`（新規・App 直下）… 診断専用の静的ヘルパ。
  - `Snapshot()` … マネージドヒープ（`GC.GetTotalMemory(false)`）／GC コミット済み容量
    （`GetGCMemoryInfo().TotalCommittedBytes`）／ワーキングセット（`Process.WorkingSet64`）の 3 値。
    Win2D/WIC のアンマネージド確保はマネージド側に出ないので WS を併記しないと判断を誤る。
  - `ForceFullCollect()` … `LargeObjectHeapCompactionMode.CompactOnce` → 全世代ブロッキング＋compacting
    collect → `WaitForPendingFinalizers()` → **もう一度 collect** の 2 段。ファイナライザ経由で解放される
    COM ラッパ（`CanvasBitmap` 等）は 1 回では回収し切れないため。プレビューのピクセルバッファは LOH 行きの
    大きな `byte[]` なので LOH 圧縮も入れる。
- `M`（修飾子なし）: プレビュー時は `PreviewControl.ForceGarbageCollect()`（`PreviewControl.Input.cs`）＝
  GC 実行後に前後の値を `C` オーバーレイへ出す。オーバーレイが閉じていれば**自動で開く**（結果が見えないため）。
  グリッド表示時は `MainPage.HandleGlobalKeyDown` で GC のみ実行（結果の表示先が無いのでタスクマネージャー等で観察）。
- `C` オーバーレイのサマリに**メモリ現況行を常設**（`RefreshCacheOverlay`）。この Refresh は写真切替のたびに
  走るので、←/→ 連打中の増減をそのまま読める。直近の GC 結果行は次の `M` まで残す。

**先行する知見との関係**: 「連写切替でメモリ/VRAM が増え続ける」問題（本ファイル該当節）では、`Trim` に
`GC.Collect()`＋`WaitForPendingFinalizers()` を一時挿入した診断で**押しっぱなし中の増加は止まらず**、
finalizer 保持ではなく**生成レート飽和**と確定している。今回の対象は毎秒 4〜5 回とレートが 1 桁低く、
かつその後キャッシュは `byte[]`（LOH）常駐方式に変わっているため、同じ結論とは限らない＝再検証の価値がある。

- 変更/新規: `src/PhotoQuickSelector.App/MemoryDiagnostics.cs`（新規）、`Controls/PreviewControl.Input.cs`、
  `Controls/PreviewControl.xaml.cs`、`MainPage.xaml.cs`、`shortcuts.json`（＋`tools/gen-shortcuts.ps1` で
  `docs/SHORTCUTS.md`/`SHORTCUTS.en.md` 再生成）、`SPEC.md`。`BUILD SUCCEEDED`（x64 Debug・警告0）／
  `dotnet test` 150 件緑。**実機での目視確認と、肝心の「M で減るか」の検証はユーザー実施待ち**。

## メモリオーバーレイの分離（`M` / `Ctrl+M`）（2026-07-28）

**症状**: 上記でキャッシュ一覧オーバーレイ（`C`）にメモリ現況行を足したところ、**オーバーレイ表示中は
←/→ の連続移動が目に見えて遅くなった**。

**原因は 2 つ**:
1. `Process.WorkingSet64` が高すぎる。.NET の Windows 実装では `Process` のメモリ系プロパティは
   `NtQuerySystemInformation(SystemProcessInformation)` で**マシン上の全プロセス・全スレッド**の
   スナップショットを取ってから自分の分を拾うため、1 回で数〜数十 ms かかる。これを UI スレッドで払っていた。
2. しかも 1 回の写真切替で複数回走っていた。`RefreshCacheOverlay` は `_cache.Changed`（読込開始／登録／Trim）
   からも呼ばれ、切替側からも明示的に呼ばれる。`DispatcherQueue.TryEnqueue` は**同一処理を合体しない**ので、
   ナビ 1 回あたり 3〜5 回フル再構築＝全プロセス列挙が走っていた。

**対処**:
- **計測を psapi の `GetProcessMemoryInfo` へ**（`MemoryDiagnostics`）。自プロセスのみのクエリで数十 µs。
  ついでに `PrivateUsage`（タスクマネージャーの「コミット サイズ」）も取得して表示に加えた。
- **オーバーレイを分離**（`Controls/MemoryOverlay.xaml(.cs)` 新規）。更新はコントロール内の
  `DispatcherQueueTimer`（500ms・表示中のみ稼働）で完結し、**写真切替の処理経路には一切載せない**。
  非表示中はタイマーを止めるのでコストゼロ。
  - 置き場所は **`MainPage` の右ペイン右下**。メモリはプレビュー固有の情報ではないので、グリッド表示でも
    見えるべきという判断（前版で「グリッドでは `M` の結果を出す場所が無い」と積み残していた件も解消）。
    右上はキャッシュ一覧が使うため右下。
  - キー割り当て（ユーザー確定）: **`M`＝オーバーレイのトグル / `Ctrl+M`＝強制 GC**。`M` 単独が GC だと
    誤爆したときに数百 ms 固まるため、破壊力のある方に修飾子を付けた。キー処理は
    `MainPage.HandleGlobalKeyDown`（プレビューへ委譲する前）に置いて両モード共通にしている。
- **`RefreshCacheOverlay` の多重投入を合体**（`_cacheOverlayRefreshQueued` フラグ）。メモリ表示とは独立に、
  キャッシュ一覧単体の負荷も 3〜5 分の 1 になる。

**表示の落とし穴（桁揃え）**: 当初はヘッダ行を等幅（Consolas）1 枚の TextBlock にして
`{value,10}` の空白パディングで 2 列に見せていたが、**右列（WS）の数値が左にずれた**。Consolas に日本語
グリフが無くフォールバックが起き、その全角幅が ASCII 2 文字ぶんとは限らないため、文字数で詰めても
表示幅は揃わない。**Grid の列で組み直し**、数値セルだけ Consolas＋右寄せ＋`MinWidth` 固定にして解決
（桁数が変わっても右端が動かない）。日本語ラベルを含む等幅レイアウトでは同じ罠を踏むので注意。
ヘッダには実行方法として `(Ctrl+MでGC)` を併記した。

**副次の効果**: 一定周期サンプリングになったことで**観測が対象を乱さなくなった**。旧実装は「見ていること自体が
ナビを遅くし、文字列生成でゴミも増やす」構造で、メモリ検証の道具としては筋が悪かった。時間軸で等間隔に
読めるので推移も追いやすい。

- 変更/新規: `Controls/MemoryOverlay.xaml(.cs)`（新規）、`MemoryDiagnostics.cs`、`MainPage.xaml(.cs)`、
  `Controls/PreviewControl.xaml.cs`、`Controls/PreviewControl.Input.cs`、`shortcuts.json`（＋生成物）、
  `SPEC.md`。`BUILD SUCCEEDED`（x64 Debug・警告0）／`dotnet test` 150 件緑。**実機確認はユーザー実施待ち**。

## 評価データの更新時刻の記録（スキーマ v2）（2026-07-30）

**背景**: 将来「評価のエクスポート／インポート」を作りたい。別マシン間で持ち込んだときの競合を
「後から評価した方を採る」で解けるようにするため、先に**変更時刻を記録するだけ**を入れておく
（インポート機能はフェーズ B）。

**方針（ユーザー確定）**:
- **粒度は項目別 7 列**（rating / flag_rating / カラーラベル 5 色）**＋行単位 `updated_at`**。
  項目別にしたのは、既存の書き込み口 `UpsertColumn(fileName, column, value)` がもともと項目単位で、
  「ローカルでレーティング、リモートでカラーラベル」を変更した競合でも取りこぼさずマージできるから。
  行単位 `updated_at` は項目別の最大値を持つ冗長列で、「前回エクスポート以降に変わった行」を
  7 列見ずに拾うため。
- **値の形式は Unix epoch 秒（UTC）の INTEGER**。DB ビューアで直接覗く用途がないので数値型。
  同時に複数箇所から操作されるアプリではないので、同一項目がミリ秒レベルで競合することはない。
- **既存行は NULL のまま**（＝いつ評価したか不明）。インポート時は「更新時刻が NULL の項目は上書き可」
  で整理する。未変更の項目も NULL になるので、規則が 1 本で済む。
- UI 表示は含めない（記録だけ）。旧アプリとの関係は文書化しない（利用者が作者のみのため）。

**実装（Core のみ。App は無変更で通った）**:
- `CurrentSchemaVersion` を 2 に。`Migrate_2()` で `ALTER TABLE ADD COLUMN ... INTEGER` を 8 本。
  ALTER ADD COLUMN はテーブル書き換えを伴わないので既存 DB でも一瞬。既存行は NULL のまま残る。
- 列名は `{値の列名}_updated_at`。命名規則の定義点は `MetadataStore.UpdatedAtColumn(string)` の 1 箇所。
- 読み取りは新設の `EvaluationRecord`（全列忠実・読み取り専用ビュー）＋`LoadRecord` / `LoadAllRecords`。
  **`PhotoEvaluation` / `LoadEvaluation` は一切触っていない**。UI に出さない 7 個の日時で表示用の
  ドメインモデルを膨らませないため。エクスポート実装でもこの record をそのまま使える。
- 時計は `TimeProvider`（ctor 第 3 引数・既定 `TimeProvider.System`）で注入。テストで前後関係を固定するため。
  epoch 秒は 2038 年に int32 を溢れるので C# 側も `long`（既存の `ReadNullableInt` は流用不可）。

**要点 1: 更新時刻は「値が実際に変わったときだけ」進める**。現状のコードは既に 3 の写真で `3` を押しても
無条件に書き込む（`FlagUp` が上限で変化なしのケースも同じ）。ここで時刻を打つと、**見ただけの押し直しが
「最新の評価」に化けて競合判定を反転させる**。upsert 1 文で条件付き更新にした:

```sql
INSERT INTO image_file_metadata (file_name, rating, rating_updated_at, updated_at)
VALUES (@fn, @val, @now, @now)
ON CONFLICT(file_name) DO UPDATE SET
    rating            = @val,
    rating_updated_at = CASE WHEN rating IS @val THEN rating_updated_at ELSE @now END,
    updated_at        = CASE WHEN rating IS @val THEN updated_at
                             WHEN updated_at IS NULL OR updated_at < @now THEN @now
                             ELSE updated_at END
```

- `DO UPDATE SET` の右辺に現れる**修飾なしの列名は「更新前の行の値」**を指す。だから `rating IS @val` が
  旧値と新値の比較になる（新値側は `excluded.rating`）。この挙動に依存しているのでテストで固定した。
- `=` ではなく **`IS`**（NULL 安全な等価）。`=` だと NULL 同士の比較が NULL になり、未評価→未評価が
  「変更あり」と判定されてしまう。
- 行単位 `updated_at` は**時計が巻き戻っても後退させない**（既存値より新しいときだけ進める）。
- 解除（値→NULL、カラーの 0）は実値の変化なので時刻が進む。これは意図どおりで、
  「明示的に外した」がインポートで古いデータに勝てる。

**要点 2: 前方互換ガードを追加**。`EnsureSchema` は `while (version < Current)` だけだったので、
**将来版が書いた DB を古い版が黙って開き、知らない列を放置したまま値だけ書き換える**余地があった
（v1 の版が v2 の DB を触ると時刻が嘘になるのが実例）。`version > CurrentSchemaVersion` で
`NotSupportedException` にした。あわせて `EnsureConnection` を、**スキーマ確認に失敗した接続は
保持せず破棄する**形に変更。保持すると次回以降 `_connection != null` の early return で判定を素通りし、
1 回目だけ弾いて 2 回目から書き込んでしまう（テストで固定済み）。App 側は `LoadFolderAsync` の
try/catch が拾ってステータスバーに出るのでクラッシュしない。

**フェーズ B（インポート実装時）への申し送り**:
- **`max(a,b,...)`（複数引数のスカラー版）は引数に 1 つでも NULL があると NULL を返す**（集約 `MAX()` が
  NULL を無視するのとは挙動が違う）。項目別 7 列から最大を取るときに素朴に書くと未評価項目の NULL で
  全行が漏れる。行単位 `updated_at` を持たせたのはこれを避ける意味もある。
- 「更新時刻 NULL は上書き可」の帰結として、**初回インポートでは既存の全評価がインポート側に負ける**
  （古いバックアップを取り込むと現行評価が潰れる）。逃げ道が必要なら
  インポート UI に「更新時刻が不明な項目も上書きする」チェックを 1 つ置く。両側 NULL も同じ規則でリモート勝ち。
- 同一性キーが `file_name` である脆さ（リネーム・別フォルダ間の衝突）。撮影日時・機種・寸法での照合は
  画像から都度読めるので DB には持たせていない。
- ~~行削除も NULL 書き戻しも起きない（解除は 0/NULL という実値の変更として記録される）ので、
  **tombstone の設計は不要**で項目単位 LWW だけで足りる。~~
  → **2026-07-30 に前提が変わった**。「評価をリセット」（下記の節）が**行を削除する**ので、消した事実は
  どこにも残らない。項目単位 LWW は従来どおり成立するが、**リセットした写真は古いエクスポートを
  取り込むと評価が復活する**（ユーザー了解済みの割り切り）。
- 一括評価は 1 枚ずつ別の時刻になるが、同一秒に並ぶのは**別ファイルの行**なので競合判定
  （同一ファイルの同一項目）には影響しない。時刻を揃える対応は入れていない。
- `invalid_flag` はアプリが書いておらず、`LoadRecord` / `LoadAllRecords` も `LoadEvaluation` と同様に
  立っている行を「無い」ものとして除外している。

- 変更/新規: `Core/EvaluationRecord.cs`（新規）、`Core/MetadataStore.cs`、
  `tests/MetadataStoreTests.cs`。`dotnet test` **164 件緑**（+14）／`BUILD SUCCEEDED`（x64 Debug）。
  App は署名互換（`timeProvider` は省略可）で無変更。

## 画像情報パネル（旧 EXIF 詳細パネル）＋評価と更新時刻の表示（2026-07-30）

「EXIF 詳細パネル」を**画像情報パネル**へ位置づけ直し、グループ並びを **File → 評価（このアプリ）→
EXIF 等**にした。評価は項目ごとの**更新日時**も併記する（スキーマ v2 で記録済みの時刻の初の可視化）。

### 決めたこと（ユーザー合意）
- 評価変更への追従は「固定行＋OneWay の差分更新」（検討時の案C）。
- `File Type` グループ（検出形式・MIME）は情報価値が薄いので**末尾据え置き**。上げるのは `File` だけ。
- File グループに**フルパスは足さない**（表示が長くなる）。MetadataExtractor の 3 行（File Name／
  File Size／File Modified Date）そのまま。
- カラーラベルの未設定表示は**「未設定」**（レーティングと表記を揃える）。設定済みは「あり」／解除済みは「なし」。
- 内部識別子（`AppSettings.PreviewExifPanel`／クラス名 `ExifDetailPanel`／partial `PreviewControl.ExifPanel.cs`）は
  **改名しない**。設定キーを変えると既存ユーザーの設定が飛ぶため。ユーザー向け文字列のみ差し替えた。

### 更新時刻をメモリ常駐にした（表示のたびに DB を引かない）
初案は描画時に `MetadataStore.LoadRecord` を引く形だったが、**評価値と同じく write-through で
メモリに持つ**方式へ変更した（ユーザー提案）。
- `Core/EvaluationTimestamps.cs`（新規）＝**epoch 秒の固定フィールドのみ**を持つ軽量な器。
  `EvaluationRecord` は同じ情報を持つがエクスポート用の一時オブジェクト（値＋`Dictionary` 2 本）で、
  数千〜1 万枚を常駐させる用途には重い。1 万枚でも 1MB 未満に収まる表現を選んだ。
- **「時刻を進めるか」の判定は SQL に一本化**したまま。`UpsertColumn` に `RETURNING` を付けて
  適用後の（項目別・行単位の）時刻を返し、`EvaluationSaveResult` として呼び出し側が受け取る。
  C# 側で同値判定・時計巻き戻しガードを再実装すると必ず乖離するため。
  **`System.Data.SQLite.Core` 1.0.119 で `RETURNING` は動く**（ただし `ExecuteNonQuery` では結果行を
  取り出せないので `ExecuteReader` で 1 行読む）。
- 書き込み経路は `PhotoItemViewModel` の 3 メソッド（`SetRating`／`SaveFlag`／`ToggleColorLabel`）に
  閉じているので、同期漏れの穴は小さい。
- 副産物として**フォルダ読み込みのクエリが N→1 に減った**。従来は写真ごとに `LoadEvaluation`
  （1 枚 1 クエリ）だったのを `LoadAllRecords()` 1 回＋辞書引きへ。辞書のキー比較は
  **`StringComparer.Ordinal` 固定**（SQLite 既定の BINARY 比較と一致。`OrdinalIgnoreCase` にすると
  大文字小文字だけ違う旧行を拾い始めて挙動が変わる）。
- `LoadEvaluation` は `LoadRecord` ＋ `PhotoEvaluation.FromRecord` の薄いラッパへ寄せ、
  列マッピングの二重実装をなくした。

### 評価変更で ListView を再構築しない（案C の要点）
`ShowInfo`（旧 `ShowTags`）は `CollectionViewSource.Source` の差し替えで、グループ化 ListView の
全再構築＋**スクロール位置の先頭復帰**を伴う。評価編集はこのアプリの主ループなので、キー入力ごとに
これを走らせると 2026-07-09 に潰したもたつきが再発する。
- `Controls/EvaluationInfoSection.cs`（新規）＝評価グループの行を**固定長（フラグ／レーティング／
  カラー5色／最終更新＝8 行）**で作り置きし、内容だけ書き換える。行は `EvaluationInfoRow`
  （`ObservableObject`＋`[ObservableProperty]`）で、テンプレートは OneWay バインド。
  行数が変わらないのでコレクション変更通知も不要。
- 評価変更の購読は既存の「焦点写真の評価変更」監視（`SubscribeOverlayWatchedPhoto` ／
  `OverlayEvalTriggerProperties`）に相乗りし、`OnFocusEvaluationChangedForExif()` を呼ぶだけ。
  1 回の評価操作で複数プロパティが飛ぶが、`SetProperty` の同値スキップで実描画は 1 回。
- 行の型が EXIF 行（`ExifTagEntry`・OneTime・2 列）と評価行（3 列）で違うため
  `Controls/InfoRowTemplateSelector.cs`（`DataTemplateSelector`）で振り分ける。
  **WinUI は WPF の暗黙 DataTemplate（`DataType` 指定）を持たない**のでセレクタが必要。
- グループの器は `Controls/InfoGroup.cs`（見出し＋`IReadOnlyList<object>` の行）。
  `IReadOnlyList<T>` は共変なので Core 側の `IReadOnlyList<ExifTagEntry>` をそのまま渡せる（コピーなし）。
  `CollectionViewSource.ItemsPath` は `Tags` から `Rows` へ変更。

### 表示仕様
- 時刻は **ローカル時刻・固定書式 `yyyy-MM-dd HH:mm:ss`**（ja/en 共通。一覧で前後関係が読めるように）。
  不明（v2 より前に付けた評価／未変更の項目／DB なし）は `—`。
- レーティングは**永続化値をそのまま**出す（EXIF フォールバックの実効値ではない＝「このアプリの評価」）。
  未設定は「未設定」。**EXIF（`xmp:Rating`）にも値があるときは改行して `(Exif: 2)` を併記**する
  （`TextBlock` は文字列中の改行をそのまま行分割として描く。更新日時列は 1 行目に揃うよう上寄せ）
  （＝永続化値が無いときの実効値の出所が判る。`ExifRating` は未記録でも 0 になり明示的な 0 と
  区別できないので 1 以上のときだけ出す。resw `Info_ExifRating`）。
- 行の並びは全表示箇所と同じ「旗 → ★ → カラー」。最後に行単位の「最終更新」。
- EXIF が空（未対応・破損ファイル）でも評価グループは出る（従来は真っ白だった）。
- 連打経路（←/→）は従来どおり placeholder のみで、実描画は settle 後の 1 回。

### 落とし穴・申し送り
- **`MetadataStore` は単一接続でスレッド安全でない**。時刻をメモリに持つ主目的の一つがこれで、
  描画時に DB を引く設計だと UI スレッド固定の制約が付きまとう（`ExifTagReader` のように
  `Task.Run` へ載せてはいけない）。
- パネルを開いただけでは **sqlite を作らない**（読みは全てメモリ／`LoadAllRecords` は
  `createIfMissing: false`）。この性質はテストでも担保している。
- 多重起動で同じフォルダを開くと時刻は自インスタンスの認識のまま古くなる。**評価値も元から同じ性質**
  なので新しい欠陥ではない。
- **評価エクスポート／インポート（フェーズ B）への申し送り**: 一括で DB を書く実装は、
  メモリ側（`PhotoItemViewModel.EvalTimestamps` と `Eval`）も更新するか、フォルダを読み直すこと。
  DB だけ書くと画像情報パネルの表示が古い時刻のまま残る。
- 文字列は resw に `Info_EvalGroup`／`Info_LastUpdated`／`Info_Unset`／`Info_ColorOn`／`Info_ColorOff` を追加し、
  項目名・フラグ値・色名は既存の右クリックメニュー用キー（`Ctx_Flag`／`Ctx_Rating`／`Ctx_Flag_*`／
  `Ctx_Color_*`）を再利用してメニューと表記を揃えた。`Preview_ExifTab` は「EXIF」→「情報」/"Info"。
- 変更/新規: Core＝`ExifTagReader.cs`（File 先頭へ並べ替え＋`FileDirectoryName`）、
  `EvaluationTimestamps.cs`（新規）、`PhotoEvaluation.cs`（`FromRecord`）、`MetadataStore.cs`。
  App＝`Controls/InfoGroup.cs`／`EvaluationInfoSection.cs`／`InfoRowTemplateSelector.cs`（いずれも新規）、
  `ExifDetailPanel.xaml(.cs)`、`PreviewControl.ExifPanel.cs`、`PreviewControl.OverlayFade.cs`、
  `PhotoItemViewModel.cs`、`MainViewModel.cs`、resw（ja/en）、`shortcuts.json`＋SHORTCUTS.md 再生成。
  `dotnet test` **173 件緑**（+9）／`BUILD SUCCEEDED`（x64 Debug）。
- **実機確認済み（2026-07-30・ユーザー目視）**。仕上げの微修正: レーティングの Exif 併記は
  `(EXIF: 2)`→`(Exif: 2)` のキャメルケースにし、同じ行で**改行して 2 行目に置く**形に変更
  （更新日時列は `VerticalAlignment=Top`）。

## 評価のリセット（行削除）（2026-07-30）

このアプリで付けた評価を「付ける前」へ戻す手段が無かったので、右クリックメニューに
**「評価をリセット」**を追加した。

### 決めたこと（ユーザー確定）
- **対象は焦点の 1 枚／選択集合まで**。フォルダ全体の一括リセットは**作らない**
  （必要なら sqlite を手動で消せば済むため）。
- **DB は行ごと削除**（`DELETE FROM image_file_metadata WHERE file_name=@fn`）。値を NULL で
  上書きする案と比較して選定。
- **キー割当なし**。誤打鍵で評価が飛ぶ事故を避け、メニューからの明示操作に限る。
- 既存の「レーティング＞なし (0)」「カラーラベル＞クリア」は**据え置き**（0 を書く従来動作のまま）。
  リセットは別項目として並べる。

### なぜ NULL 書き戻しではなく行削除か
- 全項目をまとめて解除する操作なので、**行が無い＝一度も評価していない状態**と完全に同じにできる。
  NULL 書き戻しだと「値は未設定なのに `updated_at` だけ残る」行になり、画像情報パネルに
  「未設定＋更新日時あり」という説明の要る表示が出る。
- **DB を作らないガードが自然に書ける**。`UpsertColumn` は `createIfMissing: true`（＝評価書き込みが
  ファイル生成点）なので NULL 書き戻し案では別途ガードが要るが、`DELETE` なら
  `EnsureConnection(createIfMissing: false)` の early return がそのまま「DB が無ければ何もしない」になる。
- 更新時刻の CASE 判定（`UpsertColumn`）に一切触らずに済む。リセット後の再評価は行の新規作成なので、
  **リセット前と同じ値を押しても時刻は新しく打たれる**（テストで固定）。
- 代償は tombstone が残らないこと＝上の「評価データの更新時刻の記録」節の申し送りを訂正済み。

### 実装
- Core: `MetadataStore.ClearEvaluation(fileName)`（削除した行があれば true）、
  `PhotoEvaluation.Reset()`（永続化値を全て null＝実効レーティングは EXIF へ戻る）、
  `EvaluationTimestamps.Reset()`（全項目を「不明」へ）。
- App: `PhotoItemViewModel.ResetEvaluation()` が 3 つを呼び、評価系プロパティを一括通知する。
  通知先が既存の `OverlayEvalTriggerProperties` を含むため、**情報オーバーレイと画像情報パネルの
  評価行は既存の購読経路で自動追従**する（新しい配線は不要だった）。
  `MainViewModel.ResetEvaluations(targets)` は `ApplyEvaluationAsync` を通さない
  （消すだけの操作＝sqlite 作成確認を出す意味がない）。
- UI: `PhotoContextMenu.AddResetItem` を評価 3 サブメニューの直下に置き、グリッド／フィルムストリップの
  右クリックとメイン画像の右クリック（対象は焦点 1 枚）で共有。**10 枚以上は
  `BatchFlows.RunWithBulkWarningAsync`（`BulkWarn_ResetMessage`）で確認**を挟む。
- resw（ja/en）に `Ctx_ResetEval`／`BulkWarn_ResetMessage` を追加。

### 落とし穴・申し送り
- `invalid_flag` が立った旧アプリ由来の行も同じ `file_name` なら消える（読み取り側は元から
  「無い」扱いなので実害なし）。
- **リセット後は絞り込みを掛け直さない**（既存の評価変更と同じ挙動。`Photos` は再構築されないので、
  絞込条件から外れた写真もその場では一覧に残る）。
- レーティングだけを未設定へ戻す（項目単位の NULL 化）は**この実装では出来ない**。必要になったら
  `UpsertColumn(..., null)` を呼ぶ別口を足すことになる。
- 変更/新規: Core＝`MetadataStore.cs`／`PhotoEvaluation.cs`／`EvaluationTimestamps.cs`、
  App＝`ViewModels/PhotoItemViewModel.cs`／`ViewModels/MainViewModel.cs`／`PhotoContextMenu.cs`／
  `Controls/PreviewControl.ContextMenu.cs`／resw（ja/en）、
  tests＝`MetadataStoreTests.cs`／`PhotoEvaluationTests.cs`。
  `dotnet test` **180 件緑**（+7）／`BUILD SUCCEEDED`（x64 Debug）。

## フォルダツリーのドラッグ並べ替えを無効化（2026-07-30）

左ペインのフォルダをドラッグすると「移動」と表示される、という指摘の調査と対処。

**調査結果＝実装コードは存在しない**。リポジトリ全体で `AllowDrop` / `CanDragItems` /
`CanReorderItems` / `DragOver` / `DragItemsStarting` / `Drop` ハンドラ／`Directory.Move` はいずれも 0 件。
`FolderNavigationView.xaml` の `TreeView` は `SelectionMode` / `Expanding` / `DoubleTapped` / `KeyDown`
しか指定していない。**「移動」表示の正体は WinUI `TreeView` の既定挙動**（`CanDragItems`・
`CanReorderItems` はフレームワーク既定が `true`、既定テンプレートの内部 `TreeViewList` が `AllowDrop`）で、
ツリーの**ノード並べ替え**用。TreeView が導入された `89bbe07`（2026-06-14, Phase 2）から
ずっとこの状態だった＝意図して付けた機能ではない。

**ディスク上のフォルダは動かない**。起こりうるのは `FolderNode.Children` 内でのノード移動＝表示の乱れだけで、
順序は永続化されず `LoadChildren()` の差分同期（パス昇順）で戻る。ただし CLAUDE.md「既知の注意点」のとおり
WinUI TreeView は内部状態が壊れやすいので、誤操作の余地を残す意味がない。

**対処**: `TreeView` に `CanDragItems="False"` `CanReorderItems="False"` を明示。
理由は XAML 側にコメントで残した（既定 true を打ち消す指定は、無いと「なぜ書いてあるか」が読めないため）。
変更: `Controls/FolderNavigationView.xaml` のみ。`BUILD SUCCEEDED`（x64 Debug）。

## v0.2.1 リリース（2026-07-30）

v0.2.0（2026-07-12）以降の 13 コミットをまとめて **v0.2.1** として公開（GitHub Release・Pre-release 継続）。
csproj `<Version>` を 0.2.1 に更新。発行・添付は v0.2.0 で確定した既定どおり
**単一ファイル版**（`win-x64-singlefile`・zip には exe＋LICENSE＋THIRD-PARTY の3点のみ・`.pdb` 除外）。

- **v0.2.0 → v0.2.1 の主な変更**（利用者視点）:
  - 画像情報パネル（旧 EXIF 詳細パネル）: **File → 評価（このアプリ）→ EXIF** の並びに整理し、
    評価の項目別の**更新日時**を表示。XMP タグ（`xmp:Rating` 等）も展開表示
  - 評価をリセット（右クリック・DB の行ごと削除＝未評価へ戻す）
  - グリッドの空状態表示（フォルダ未選択／JPEG なし／絞り込み 0 件）＋フィルタボタンの状態色
  - バージョン情報ダイアログにデータの保存先（settings.json・評価 DB ファイル名）を表示
  - シャッター速度の表示を修正（1 秒以上を秒表記に）
  - フォルダツリーのドラッグ並べ替えを無効化（誤操作防止）
  - メモリ使用量オーバーレイ `M` ／強制フル GC `Ctrl+M`（デバッグ用）
  - **評価データのスキーマが v2 に**（項目別＋行単位の更新時刻列）。開いた DB は自動移行される
- **開発向け（配布物には非影響）**: アプリ設定フォルダと評価 DB ファイル名を Release／Debug で分離
  （`PhotoQuickSelector` ／ `PhotoQuickSelector.Dev`）。
- **リリースノートに明記した注意**: v0.2.1 で開いた DB を **v0.2.0（スキーマ v1 認識）で触ると値だけ変わって
  更新時刻が古いまま残る**ため、同じフォルダで両版を混在利用しないこと。
- リリース作業: バージョン更新コミット（csproj＋`Package.appxmanifest` の `Version`＝0.2.1.0 も同期・README の
  インストール手順を zip 前提へ修正）→ `main` へ push → `v0.2.1` タグ → `gh release create`（Pre-release・
  `PhotoQuickSelector-0.2.1-win-x64.zip` 添付）。`dotnet test` 180 件緑。

## 開発中バージョンのプレリリース表記（`-dev`）（2026-07-31）

**動機**: 開発中のビルドを、リリース済みの版と見分けたい（v0.2.1 リリース後に 0.2.2 へ向けて作業中、
という状態をバージョン表示で表したい）。

**バージョン文字列に英字が使えるか**の切り分け:

| 箇所 | 英字 | 備考 |
|---|---|---|
| csproj `<Version>` | 可 | SemVer のプレリリース表記。`0.2.2-dev` でビルドできる（MSIX ツーリング下でも `BUILD SUCCEEDED`） |
| `AssemblyVersion` / `AssemblyFileVersion` | 不可 | `<Version>` の数値部（`VersionPrefix`）だけを採る＝`0.2.2.0`。`-dev` は落ちる |
| `AssemblyInformationalVersion` | 可 | `0.2.2-dev` がそのまま載る。**`-dev` が残るのはここだけ** |
| `Package.appxmanifest` の `Version` | 不可 | MSIX 仕様で数値4組固定（第4フィールドは Store 予約＝0）。開発中でも数値のみ上げる |

**採った方式（案A）**: csproj `<Version>` を `0.2.2-dev` とし、**リリースのコミットで `-dev` を外す**。
SemVer の順序も `0.2.2-dev < 0.2.2` で正しい。ビルド構成（Debug/Release）に紐づける案Bも検討したが、
「未リリースの 0.2.2 を Release ビルドした exe」が本物のリリース版と同じ表示になってしまうため見送り。

**変更点**:
- `PhotoQuickSelector.App.csproj`: `<Version>0.2.2-dev</Version>`＋
  `<IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>`。
  後者が無いと InformationalVersion が `0.2.2-dev+<git sha>` になり、そのまま画面に出せない。
- `Controls/AboutDialog.xaml.cs`: `GetVersionText()` を `Assembly.GetName().Version`（＝数値のみ）から
  `AssemblyInformationalVersionAttribute` 優先の読み取りへ。`+` 以降は捨て、属性が無ければ従来の数値表示へフォールバック。
- `Package.appxmanifest`: `Version="0.2.2.0"`（数値のみ。ここは開発版と区別できない）。

**リリース時の手順への追加**: csproj の `-dev` を外す（appxmanifest の版上げと同じコミットで行う）。

## 先読みキャッシュのバッファプール化（ワーキングセット削減）（2026-07-31）

「メモリオーバーレイ（`M`）でキャッシュ予算 2GB に対しマネージドが 4〜5GB・WS が 5〜6GB まで増える。
`Ctrl+M` を 1 回押しても WS が 0.2〜0.3GB しか減らず、連打してようやく 2.5〜3GB まで下がる」という
報告への対応。実使用はキャッシュ 2GB＋その他 1GB 程度なので、OS から見た使用量が必要以上に大きく、
メモリの少ない環境で他アプリを圧迫する懸念があった。

**原因**: 保持型が `byte[]`（LOH 行きの 200MB 級＝α1 8640×5760×4）で、**毎デコードで新規確保していた**こと。
① `Trim` が捨てたぶんがゴミとして積み上がり、gen2/LOH の GC 予算が生存量（2GB）に比例して大きいので
なかなか回収されない、② 回収されても decommit は GC 1 回あたりの予算で分割実施されるため WS が即座には
減らない（`Ctrl+M` 連打で階段状に下がるのはこのため＝ランタイム仕様であり回収漏れではない）、
③ ピクセルは全バイト書き込むので触れたページは全部 dirty＝コミット＝WS 入り。加えて
`GetPixelDataAsync`＋`DetachPixelData` は WinRT の native バッファから managed 配列へマーシャリングするため、
デコード中は**同じ 200MB が一時的に 2 面**存在していた（同時デコード 2 本で最大 800MB のピーク）。

**方針**: 保持型は `byte[]` のまま、**確保をプールで回してゴミを出さない**。保持型を動かせない理由は
過去 2 件の実装経緯（本ファイル「先読みキャッシュの SoftwareBitmap 化」節）にある：
`SoftwareBitmap` 保持は切替のたびに `CopyToBuffer` の 200MB memcpy が UI スレッドに乗るため 2026-07-04 に
撤回済み、それを避けて毎回 `CreateFromSoftwareBitmap` する形にすると今度は 2026-07-01 に潰した
**フル解像度 `CanvasBitmap` の生成レート飽和による VRAM 肥大**が再発する。**守るべき不変条件＝
①写真切替時の CPU コピー 0 回、②`CanvasBitmap` の生成レートを上げない（同寸 `SetPixelBytes` 再利用の維持）**。
プール方式はどちらも壊さない（`PreviewControl` の表示経路は 1 行も変えていない）。

**予算の分割**: 設定の「キャッシュ容量予算」の意味を「デコード済みキャッシュの合計」から
**「キャッシュ＋プールの合計」**へ変更し、割合で内訳を決める。固定パーティションにしたのは、
共有予算で「超過するプールへの返却を拒否」
とすると**プールが常に空になって機能しない**ため（`Trim` はキャッシュを予算いっぱいまで太らせるので、
プールに回る余りが構造的にゼロになる）。割合は**設定項目**にした（予算を大きくすると必要な割合は下がる＝
プールに要る本数は同時デコード数に比例し予算には比例しないため。ユーザー判断で調整できるようにする）。

**変更点**:
- `Controls/PixelBufferPool.cs`（新規・純 C#）: バイト長**完全一致**キーの再利用プール。
  `Rent`（在庫があれば再利用・無ければ `GC.AllocateUninitializedArray`＝200MB のゼロ埋めも省く）／
  `Return`（無条件に受け取り、上限超過ぶんを返却順の古い順＝LRU で捨てる）／`Clear`／ヒット・ミス数。
  **サイズクラス丸めはしない**（`byte[]` を厳密長で `SetPixelBytes`/`CreateFromBytes` へ渡す契約のため）。
  縦位置と横位置は寸法が入れ替わるだけでバイト長は同じ＝同一クラスとして再利用できる。
  寸法がバラバラなフォルダでは `Rent` が外れ続けて**プール導入前と同じ挙動に縮退する**だけで、
  抱え込む無駄は上限で頭打ち（LRU で使われないサイズが自然に落ちる）。
- `Controls/PreviewBitmapCache.cs`:
  - デコードを `GetSoftwareBitmapAsync` へ戻し、`LockBuffer` の `Stride` 検査で密詰めを確認してから
    プールの配列へ `CopyToBuffer`。**行パディングがあるときだけ**従来の `GetPixelDataAsync`＋
    `DetachPixelData`（密詰め保証・プール不使用）へフォールバックする。BGRA8 は 4 バイト境界に自然に
    揃うのでこの分岐は実際には踏まれない（2026-07-04 の `fb9bcd1` も同じ前提で出荷している）。
    コピー量・実行スレッドは `DetachPixelData` のマーシャリングと同じなので切替コストは不変、
    一時的な 2 面確保だけが消える。
  - `SetBudget(totalBytes, poolRatioPercent)` を追加し `MaxCacheBytes` は算出値（＝合計−プール）へ。
  - `Trim` が捨てた配列は `_pendingReturns` へ積み、**次の `Trim` の先頭でプールへ返す（1 ターン遅延）**。
    即返却だと、`GetAsync` が返した `PixelFrame` を `PreviewControl` が GPU へ転送し終わる前に
    `Rent` で中身を上書きされて表示が化ける恐れがある。UI スレッドのターンをまたげば同期利用は必ず
    終わっている。なお表示中の 1 枚は常に保持窓内で `Trim` の保護対象なので、これは取りこぼしへの保険。
  - `Clear()` はバッファを**プールへ返さず捨てる**＋プールの在庫も空にする（返すとキャッシュから
    プールへ移動するだけで 1 バイトも解放されないため）。
- `AppSettings.cs`: `CachePoolRatioPercent`（既定 25）。`CacheBudgetGB` の doc を「合計」へ改訂。
- `Controls/SettingsDialog.xaml(.cs)`・`Controls/PhotoStatusBar.xaml.cs`・resw（ja/en）: 割合の設定 UI。
  「既定に戻す」はキャッシュ予算と同じグループ。
- `Controls/PreviewControl.xaml.cs`: 予算の適用（`SetBudget`）・同時デコード数変更時の引き継ぎ・
  `C` オーバーレイに「プール N本 xMB / yMB 再利用 h/r（p%）」行を追加。**表示経路（`TryUpdateBitmapInPlace`・
  `CreateFromBytes`）は無変更**。
- `tests/…/PixelBufferPoolTests.cs`＋csproj のソースリンク（`PreviewViewport`/`DecodeGate` と同方式）。

`dotnet test` **190 件緑**（+10）／`BUILD SUCCEEDED`（x64 Debug・Release とも警告 0）。

**期待値と実機確認（未了）**: マネージド 4〜5GB → 予算前後で頭打ち、WS 5〜6GB → 2.5〜2.8GB、`Ctrl+M` 不要。
`M` オーバーレイで押しっぱなしナビ中の managed/WS が頭打ちになること、`C` オーバーレイでプールの
再利用率が高いこと（同一機種フォルダならほぼ 100%）、連写切替の体感が変わっていないことの目視確認が必要。

**既定値（実機確認後に確定・2026-07-31）**: 実測でメモリ使用量が十分下がったため、**予算 2.5GB・
プール 20%＝キャッシュ 2GB／プール 0.5GB** を既定にした（`AppSettings.CacheBudgetGB`＝2.5・
`CachePoolRatioPercent`＝20。`PreviewBitmapCache` の ctor 既定も同値に揃えてある）。
これで**デコード済みキャッシュは従来と同じ 2GB＝α1 で 10 枚**を保ちつつ、プールぶんを足しても
常駐は 2.5GB で頭打ちになり、変更前（WS 5〜6GB）より小さい。既存ユーザーの `settings.json` には
旧値が保存済みなので、**既定値の変更が効くのは新規インストールと「既定に戻す」を押したときだけ**。

**申し送り**: 割合が過大／過小なら設定で調整する（プールに要る本数は同時デコード数に比例し予算には
比例しないので、予算を増やすほど必要な割合は下がる）。予算が小さいとき（1GB 以下・α1 級）はプールが 1 本も持てず
縮退しうるが、**下限は設けていない**（合計 ≤ 予算 の不変条件を優先。実測してから再検討する）。
なお `Clear()` は現在も呼び出し元が無く、**プレビュー退出・フォルダ切替後もキャッシュは常駐したまま**
（別フォルダへ移っても前フォルダのぶんが残る）。ここを解放する対応は別件として未着手。

## GC の `System.GC.ConserveMemory` 設定（2026-08-01）

バッファプール化（前節）でマネージドヒープの肥大は止まったが、**実機ではまだワーキングセットが多め**
だったため、GC 側の設定を併用した。`RuntimeHostConfigurationOption` で
`System.GC.ConserveMemory` を **7** に設定（`src/PhotoQuickSelector.App/PhotoQuickSelector.App.csproj`）。

- **なぜ効くか**: プールは「毎回の確保」をなくすがヒープの**保持**は減らさない。ConserveMemory は
  GC に圧縮と OS へのセグメント返却を積極的に行わせるので、200MB 級 `byte[]` を扱う本アプリでは
  managed heap より **WS の高止まり**に効く。プールと役割が違うので併用が効く。
- **値の選定（実機比較・ユーザー確認）**: **5＝削減が不足／9＝GC の停止が体感できる／7＝効果と挙動の
  バランスが良い**。よって 7 を採用。
- 設定前に別ブランチで 5 を試した時点ではカクつきは皆無だった＝**カクつきが出るのは 9 側**という感触。
- `runtimeconfig.json` の `configProperties` に出力される（ビルドで確認）。single-file 発行では
  exe に埋め込まれるので、**配布版・開発版とも同じ値**が効く（構成による出し分けはしていない）。

**申し送り**: 将来 `PreviewBitmapCache.Clear()` の呼び出し（プレビュー退出・フォルダ切替でのキャッシュ解放）
を入れると解放の主因がそちらへ移るので、そのときに 7 が過剰でないか再評価する余地がある。

## メモリ肥大の原因調査（`Ctrl+M` の Aggressive 化／ネイティブ行の追加）（2026-08-01）

`ConserveMemory=7`（前節）まで入れてもワーキングセットが 5GB 級まで伸びるという報告への対応。
当初の見立ては「LOH にゴミが溜まっている」だったが、**計測したら原因が違った**。本節はその計測と、
計測のために入れた 2 つの変更の記録（**対応そのものは未着手＝申し送り参照**）。

### 計測（`Ctrl+M` の前後・実機）

まず切り分けのために 2 つ足した:

- `Ctrl+M` の 2 段目を `GCCollectionMode.Forced` → **`Aggressive`** に（`MemoryDiagnostics.ForceFullCollect`）。
  `Aggressive` は free list を捨てて空きリージョン/セグメントを積極的に OS へ返す。
  **generation＝`MaxGeneration` / `blocking` / `compacting` の 3 つがすべて必須**（1 つでも欠けると
  `ArgumentException`）で、既存コードは元からこの形だったのでモードの置換だけで通った。
  1 段目は「ファイナライザ対象を浮かせる」のが目的なので `Forced` のまま（両方 `Aggressive` にしても
  1 段目の decommit は 2 段目で上書きされ、所要時間が伸びるだけ）。
- `M` オーバーレイに **「ネイティブ」行**（`MemorySnapshot.NativeBytes` ＝プライベート − GC コミット）を追加。
  GC 管轄外がどれだけあるかを常時見えるようにした。GC 前後の表示にも同じ行を足してある。

結果（悪めの状態で `Ctrl+M` を 1 回）:

| 項目 | GC 前 | GC 後 | 差 |
|---|---|---|---|
| マネージド | 2,931MB | 2,749MB | **−182MB** |
| ネイティブ | 1,961MB | 931MB | **−1,030MB** |
| WS | 4,528MB | 3,468MB | −1,060MB |

所要 245ms。

### 分かったこと

- **LOH にゴミは溜まっていない。** マネージドが 182MB しか減らず、GC 後の 2,749MB は
  キャッシュ＋プールの予算 2,560MB とほぼ一致する＝**LOH は予算どおり全部ライブ**。
  バッファプール化（前々節）が効いている裏付けでもある。したがって「LOH を解放する」ための
  GC 強化は無意味で、LOH を減らしたければ予算を下げるか `PreviewBitmapCache.Clear()` しかない。
- **WS 肥大の主因はネイティブ側で、しかも GC で 1GB 返る。** GC で返るということは、
  デコード経路の WinRT/COM オブジェクトが**ファイナライザ待ちのままアンマネージド メモリを
  抱えていた**ということ。`SoftwareBitmap`／`BitmapBuffer`／`InMemoryRandomAccessStream` は
  `using` で確定解放しているが、**`BitmapDecoder` は `IDisposable`/`IClosable` を持たない**ため
  確定解放できず、GC ＋ファイナライザでしか WIC 側のバッファが返らない。α1 級（8640×5760）を
  2 本並列で回し続けるので、これが 1〜2GB まで積み上がる。

`BUILD SUCCEEDED` ／ `dotnet test` 190 件緑（テストの増減なし）。

### 申し送り（対応方法は検討中）

**対策そのものはまだ入れていない**。判明した事実から、選択肢は 2 つある。

1. **アイドル時の自動 GC（対症療法）**。確定解放の手段が無い以上、静かになったタイミングで
   明示的に GC を回すのが直接の解。一度実装しかけた際の設計は以下（実装は取り消してある）:
   - 判定を純関数（`IdlePolicy`）に切り出してタイマー（`IdleMonitor`）から分離する。境界条件
     （残り時間・ビジー時の再試行）を xUnit で押さえられ、`DecodeGate`/`PixelBufferPool` と同じ
     ソースリンク方式に乗る。
   - **操作通知はフィールド書き込み 1 回**で終わる形にする。`PointerMoved` から呼ぶので、操作のたびに
     タイマーを張り直す実装にはできない。期限判定は発火時に「最後の操作からの経過時間」を見て行い、
     足りなければ残りぶんだけ張り直す。
   - **アイドル期間ごとに 1 回**だけ実行してタイマーを止める（アイドル中の周期実行は CPU/バッテリーの無駄）。
   - デコード進行中（`PreviewBitmapCache` の inflight）は見送って再判定。ここで GC を挟むと
     目的（回収）も体感（デコード遅延）も両方損なう。
   - 操作の集約点は `MainWindow`。キーは既存の `RootGrid_PreviewKeyDown` の先頭（`Handled` 済みや
     ダイアログ中の入力も「操作」なので早期 return より前）、ポインタは `AddHandler(..., true)` で
     Pressed/Moved/WheelChanged（グリッドのクリックはほぼ全部 `Handled` になるため `handledEventsToo` が要る）。
     `Activated` で非アクティブ化も拾える。
   - しきい値の案＝アクティブ 60 秒／非アクティブ 5 秒／ビジー再試行 10 秒。60 秒は「写真を眺めて考える間
     （数十秒）に割り込まない」ため（GC 自体は 245ms だが、再開の瞬間に重なると待たされて見える）。
   - 結果表示は `MemoryOverlay` の GC 前後ブロックを流用しつつ、**オーバーレイを勝手に出さない**
     （ユーザーは何も操作していないので画面が突然変わる方が邪魔）。
   - 入れる場合は `MemoryDiagnostics` のクラス doc「通常動作では一切呼ばれない」の改訂が必要。
     ただし**製品ロジックの正しさは GC に依存しない**（最適化であって、動かなくても表示や評価は
     変わらない）という前提は維持できる。
2. **`BitmapDecoder` を抱え込まない（本筋）**。確定解放できないオブジェクトの生存期間を短くする
   （デコード直後に参照を落とす等）方が、GC を回すより筋が良い。ネイティブが GC 直後でも 931MB 残る点も
   含めて、まず内訳を詰める価値がある。

その他:

- `ConserveMemory=7` の再評価余地（前節の申し送り）は据え置き。
- `PreviewBitmapCache.Clear()` は**依然として呼び出し元が無い**。LOH（＝キャッシュ 2GB）を返したい
  場合の唯一の手段なので、プレビュー退出・フォルダ切替と併せて別件のまま。

## ネイティブメモリ肥大：実験①＝デコーダの COM 参照の確定解放（2026-08-02）

前節（メモリ肥大の原因調査）の申し送りにある「まず内訳を詰める」の実行。主因候補
「ファイナライザ待ちの `BitmapDecoder` が WIC の内部フレームキャッシュ（α1 級で ~190MB/枚）を
抱える」を確定させるため、**デコード完了直後にデコーダの COM 参照を強制解放する実験コード**を
`PreviewBitmapCache.LoadCoreAsync` に入れた（`ReleaseWinRtNative`。【実験 2026-08-02】コメント付き）。

### 実装のポイント

- **`IWinRTObject.NativeObject` の Dispose だけでは解放されない。** CsWinRT のプロジェクション生成
  クラス（`BitmapDecoder` 等）は、インターフェイスごとの `IObjectReference` フィールドを遅延生成し、
  それぞれが独立の COM 参照（AddRef）を持つ。1 本でも残ると参照カウントが 0 にならず WIC 側は
  解放されないため、**リフレクションで全フィールド（QI キャッシュ辞書も含む）を走査して全部
  Dispose する**実装にした。実験用途なのでリフレクションのコスト・行儀は許容。
- 解放位置は「最後の利用の直後」＝ピクセル取り出し（`CopyToBuffer`／`DetachPixelData`）の後、
  `PixelFrame` 生成の前。ヘルパー内は包括 try/catch で、解放に失敗してもデコード結果の利用は
  損なわない。
- ビルド成功・`dotnet test` 190 件緑（テスト増減なし。`PreviewBitmapCache` はテスト対象外のため）。

### 既知のリスク（実験として許容）

解放後も RCW 本体は GC まで CsWinRT の RCW キャッシュ（ネイティブポインタ→管理オブジェクトの
同一性キャッシュ）に残る。ネイティブ側で**同じアドレスに新しい WIC オブジェクトが確保される**と、
稀に解放済み RCW が新オブジェクトの顔をして返され、次の利用で `ObjectDisposedException` になりうる。
`LoadCoreAsync` の包括 catch で null（＝その写真が一時的に表示されない）に落ちるだけでクラッシュは
しないが、**ナビ中に稀に写真が出ないことがあれば、この既知アーティファクトの可能性が高い**
（次の再試行でデコードし直される）。

### 確認手順（実機・ユーザー）

1. アプリを起動し、大きめのフォルダ（岩国_100MSDCF 等）でプレビューを開く。
2. `M` でメモリオーバーレイを出し、押しっぱなしナビを数十枚〜数百枚行う。
3. **「ネイティブ」行**を観察する。期待値＝従来の 2GB 級への高止まりが消え、
   「進行中デコード 2 本ぶん（~400MB）＋固定費（前節の計測で GC 後 931MB）」程度で頭打ちになる。
   `Ctrl+M` を押してもネイティブがほぼ減らなくなれば（＝ファイナライザ待ちの滞留が消えた）、
   主因確定。

### 実験①の結果＝効果なし。真因は `InMemoryRandomAccessStream` のバッファと特定（同日）

ユーザー実機確認の結果、**実験①は効果なし**（ナビ中にネイティブ ~2.0GB へ肥大・`Ctrl+M` で −1.1GB
＝実験前と同じパターン）。切り分けのため、アプリと同じデコード経路を再現するコンソールプローブ
（scratchpad の `DecoderNativeProbe`。α1 実画像・各シナリオ 8〜21 枚・3 ラウンド）で
各段階のネイティブ量（private − GC commit）を実測した。

**分かったこと（仮説の棄却と真因）**:

- 解放コード自体は機能していた（デコーダの `IObjectReference` 3 本＝`_inner`／`IBitmapFrame`／
  `IBitmapFrameWithSoftwareBitmap` を正しく Dispose）。しかし**生きているデコーダ 8 個を全解放しても
  ネイティブは 1MB も減らない**＝「`BitmapDecoder` が WIC フレームキャッシュ ~190MB を抱える」
  仮説は**誤り**。デコーダ本体のネイティブ保持はほぼゼロ。
- `SoftwareBitmap`（190MB 級）は `Dispose` で**即返る**（段階別計測で確認）＝従来コードで問題なし。
- **真因＝`InMemoryRandomAccessStream` のネイティブバッファ**。`WriteAsync` 時点でファイルサイズの
  約 2 倍のネイティブを確保し、**`Dispose`（Close）してもバッファは返らない**。WIC デコーダ側が
  ストリームへの参照（クローン）を持つため、RCW のファイナライズが揃うまで生存する。
  デコーダ RCW・ストリーム RCW・`IAsyncOperation` の Close をどう組み合わせても確定解放できなかった
  （参照連鎖のどこかが必ず GC 待ちで残る）。1 デコードあたり ~13〜16MB（6〜7MB ファイル時）が滞留し、
  GC 間隔が長いと数百デコードで 1〜2GB に達する＝実機の観察と整合。

**対策候補の実測**（21 枚 × 3 ラウンド。デコード時間と GC なしでのネイティブ滞留）:

| 方式 | 時間/枚 | ネイティブ滞留 | 備考 |
|---|---|---|---|
| B: 現行（毎回 `InMemoryRandomAccessStream`） | 88–92ms | **~330–400MB** | 従来挙動 |
| G: `MemoryStream.AsRandomAccessStream()` | 113–114ms | **±0MB** | マネージドのまま渡す＝ネイティブコピー自体が消える。**+25ms/枚（約 +28%）** |
| H: 使い回し（`Size=0` リセット） | 88ms | 120–150MB | truncate がバッファを切り離し効果半減 |
| H2: 使い回し（truncate なし上書き） | 88–89ms | **12–57MB** | 速度据え置き・滞留は上限固定。ただし旧ファイル残骸が末尾に残る（JPEG は EOI で読み終えるため実害はない想定）＋クローンのバッファ共有という未文書化挙動に依存＋同時デコード 2 本ぶんのストリーム管理が必要 |

**申し送り**:

- 恒久対応は G（確実・単純・保守安全。ただし +25ms/枚）か H2（速度据え置き。ただし未文書化挙動依存）
  の二択。採用決定後、**実験①のコード（`ReleaseWinRtNative` と呼び出し）は効果がないので削除する**こと。
- サムネイル側（`PhotoItemViewModel.CreateThumbnailImageAsync`）も同じ
  `InMemoryRandomAccessStream` パターンだが、量的に軽微（320px・数十KB 級）。恒久対応と合わせて
  揃えるかは任意。
- プローブ一式は scratchpad（セッション限り）のため、再検証したい場合は本節の記録から再構築する。

## ネイティブメモリ肥大の恒久対応＝マネージドストリーム化（G 案・**差し戻し済み**）（2026-08-02）

前節（実験①）で特定した真因への恒久対応として、ユーザー選択の G 案を一度実装した:

- `PreviewBitmapCache.LoadCoreAsync` の WinRT ストリームを
  `InMemoryRandomAccessStream`＋`WriteAsync` から `MemoryStream.AsRandomAccessStream()` に置換
  （圧縮バイトをマネージドのまま WIC へ渡し、滞留していたネイティブコピー自体を消す）。
- 効果のなかった実験①のコード（`ReleaseWinRtNative` と呼び出し）は削除。

**しかしユーザー実機確認で「依然としてネイティブ行が高くなることがある」**ため、再現方法を
確立してから再調査する方針とし、**コードは HEAD（`InMemoryRandomAccessStream` 版・実験①も無し）へ
差し戻した**（未コミットのまま破棄。ビルド＋テスト 190 件緑で復元確認済み）。

### 再調査への申し送り（重要）

プローブの実測（前節）自体は有効＝「ストリームのネイティブバッファが GC 待ちで滞留する」のは事実。
ただし G でも解消しきれない**別の要因**があるということ。候補と確認観点:

1. **`M` オーバーレイの「ネイティブ」行の測定誤差**。ネイティブ＝プライベート − GC コミットだが、
   GC コミット（`GC.GetGCMemoryInfo().TotalCommittedBytes`）は**直近 GC 時点のスナップショット**で、
   GC の合間にマネージドヒープが伸びた分（キャッシュ充填期の新規 `byte[]` 確保など）は次の GC まで
   「ネイティブ」に**見かけ上**上乗せされる（プローブでも `ReadAllBytes` 直後にマネージド確保が
   ネイティブ行へ出る現象を確認済み）。再現時は `Ctrl+M` 後の「実際に減った量」で実滞留か見かけかを
   切り分けること。
2. **プレビュー以外のネイティブ消費**。サムネイル経路（`GetThumbnailAsync`＝シェル COM／
   `BitmapImage` の XAML デコードサーフェス。フォルダ読込・グリッドスクロールで発生）、
   Win2D 側（`CreateFromBytes` の作り直し・ナビゲーター縮小ビットマップ・D3D ドライバ側バッファ）は
   プローブ対象外だった。
3. 再現手順の記録観点: 直前の操作（フォルダ切替？グリッドスクロール？縦横混在ナビ？フィルタ？）、
   `M` の各行の値、`C` オーバーレイのキャッシュ/プール状態、`Ctrl+M` での減少量（マネージド／
   ネイティブ別）。

再現方法が判明したら、本節と「実験①」節の実測を踏まえて原因を絞り込むこと。

## ネイティブメモリ肥大：再現手順の確立と原因特定（2026-08-03）

ユーザーが再現方法を確立した（岩国_100MSDCF〔α1 8640×5760・6〜7MB JPG × 4000 枚〕を開き、
DSC03220 あたりから**秒間 3〜4 回で右キー連打**を続けると、DSC03290〜03310 を通るあたりで
`M` オーバーレイの「ネイティブ」が 2GB 前後に達し WS も 5GB 近くになる）。実測スクリーンショット＝
マネージド 2,476MB／GC コミット 2,536MB／プライベート 4,663MB／WS 4,363MB／ネイティブ 2,127MB、
`C` オーバーレイ＝キャッシュ 11 枚 2088MB・プール 1 本 80MB・再利用 501/717（70%）。

### 調査方法＝定常状態を模すコンソールプローブ

前回（実験①）のプローブは「デコードごとの滞留量」の単発計測だったため、今回は**連打ナビの
定常状態**（キャッシュ 11 枚満杯＋プール再利用でマネージド新規確保がほぼゼロ）を模して
連続 120 デコードし、10 デコードごとにネイティブ（private − GC committed）と GC 回数を記録した
（scratchpad `NativeSteadyProbe`。実画像 DSC03220 以降を使用・`ConserveMemory=7` も本体と同一。
セッション限りなので再構築時は本節の設計で作り直す）。

### 実測結果（120 デコード・95〜111ms/枚）

| シナリオ | ms/枚 | GC 回数 (gen0/gen2) | ネイティブ推移 | フル GC＋ファイナライザ後 |
|---|---|---|---|---|
| b-pool（現行＝ネイティブストリーム＋プール） | 95 | **13 / 13** | GC 間で **+10〜20MB/枚** 単調増・ピーク 968MB | **62MB** |
| b-nopool（プール導入前＝毎回 new 190MB） | 96 | 117 / 117 | **250MB 以下で頭打ち**（毎デコード GC） | 32MB |
| g-pool（G 案＝マネージドストリーム＋プール） | 111 | 104 / 13 | **+5〜8MB/枚** の滞留が gen1 回収と均衡し **~550MB で高止まり** | 47MB |

### 結論＝原因は「デコードごとのネイティブ滞留 × プール化による GC 静穏化」の複合

1. **滞留はすべて GC＋ファイナライザで全回収できる**（フル GC 後 32〜62MB）＝リークではなく
   「ファイナライザ待ちの純ゴミ」。実機の 2,127MB も同じ性質（`Ctrl+M` で戻ることと整合）。
2. 1 デコードあたりの滞留は現行 ~20MB/枚＝**ストリームバッファ ~13MB**（実験①で特定済み・
   ファイルサイズ約 2 倍）＋**デコーダ系 ~6.5MB**（≈ファイルサイズ 1 倍。G でストリームバッファを
   消しても残る成分。正体の特定は未実施＝WIC が圧縮データを内部バッファする分と推定）。
3. **プール化（2026-07-31）が引き金**: プール再利用で 190MB 級のマネージド確保が消えた結果、
   GC 頻度が 1/10 以下（117 回→13 回）になり、従来は毎デコードの GC で掃除されていた滞留が
   数十〜数百デコードぶん積み上がるようになった。プール導入前は「大量のマネージドゴミが
   GC を強制する」ことで偶然ネイティブも掃除されていた（＝LOH 肥大と引き換え）。
4. **再現手順が DSC03290〜03310 で顕在化する理由**: 開始（03220）から十数枚はプールミスの
   190MB 確保が GC を誘発して掃除が効くが、キャッシュ＋プールが温まる（~03235）と定常状態に
   入り、以降 ~20MB/枚 × 55〜90 枚 ≈ 1.1〜1.8GB が GC 固定費（~931MB）に上乗せされる＝
   実測 2,127MB と整合。
5. **G 案差し戻し時の「G でも高くなることがある」の説明がついた**: G はストリームバッファ
   （~13MB/枚）を消すが**デコーダ系 ~5〜8MB/枚は残る**ため、GC が静かなら GB 級まで積み上がり
   うる（プローブでは gen0/gen1 が高頻度に回って ~550MB で均衡したが、実機はデコードペースが
   遅く GC 間隔が延びるため上限はもっと高い）。実験①の「G: ±0MB」は計測パターンの違い
   （ラウンドごとの GC で掃除されていた）による過大評価だった。

### 対策候補（未着手・ユーザー判断待ち）

- **案 J＝能動的な後始末**: ナビが静止したアイドル時（`PreviewControl` の settle timer が流用可能）
  や N デコードごとに GC＋`WaitForPendingFinalizers` を軽く回す。ストリーム・デコーダ両成分に効く
  唯一のクラス横断策で、実装が最小（既存 `MemoryDiagnostics` 流用）。コストは GC 停止時間
  （アイドル時なら体感なし）。
- **案 H2（既記録）**: ストリーム使い回しで最大成分 ~13MB/枚 の確保自体をなくす。速度据え置き。
  未文書化挙動依存の懸念は前節のとおり。残るデコーダ系 ~6.5MB/枚 は案 J との併用が必要。
- **案 G（既記録）**: +16ms/枚（今回実測 95→111ms）で最大成分を消すが、単独では不十分
  （上記 5 のとおり）。
- 補足: 意図して保持しているメモリ（キャッシュ 2GB＋プール 0.5GB）は予算どおりで問題なし。
  問題は再利用されない純ゴミの滞留だけなので、案 J 単独でも「連打中の上限」を数百 MB 程度に
  制御できる見込み。

## ネイティブメモリ肥大の対策＝案 J（WinRT デコードゴミの能動回収）（2026-08-03）

前節で特定した原因への対策として案 J を実装した。**方式をプローブで先に検証してから**
実装している（`NativeSteadyProbe` にシナリオ追加）。

### プローブでの方式検証（実装前）

- **gen1 掃除（16 デコードごと・ブロッキング）**: 掃除直後 100ms 待ちでもネイティブ ~300〜400MB が
  残る。→ **バックグラウンド gen2 GC と重なって走っていたデコードの RCW が gen2 へ昇格**し、
  gen1 掃除では届かない成分があると判明。ただし連打中のピークは 968→716MB に抑制。
- **gen2 バックグラウンド掃除（16 デコードごと・非ブロッキング）**: 掃除直後は 144〜387MB と
  ばらつく（BGC 完了待ちの揺れ）。デコード時間 +11ms/枚 の常時コストがかかる。ピーク 698MB。
- **アイドル掃除（gen2 バックグラウンド 1 発＋1 秒放置）**: **532→64MB**。ブロッキングも
  `WaitForPendingFinalizers` も不要で、フル GC（after-GC 43MB）とほぼ同水準まで戻る。

→ 採用構成＝**連打中は gen1（軽い・数 ms）で上限を抑え、アイドルで gen2 バックグラウンド（停止なし）
まで回収**。周期掃除まで gen2 にする案は常時コストに見合わないため不採用。

### 実装（`PreviewBitmapCache`）

- `NoteDecodeGarbage()`: `LoadCoreAsync` の outer finally から、WinRT ストリームを作ったデコード
  （成否・世代破棄を問わず）ごとに呼ぶ。プレビュー・先読みの全デコードがここを通る。
  - `SweepEveryDecodes`（=16）回ごとに `GC.Collect(1, Forced)`＝エフェメラル世代のみの
    ブロッキング GC（数 ms。キャッシュの大きな byte[] は gen2/LOH で走査対象外）。
  - 毎回 one-shot の `System.Threading.Timer` を `IdleSweepDelay`（=2 秒）でリスタート。
- `IdleSweep()`（スレッドプール）: `GC.Collect(MaxGeneration, Forced, blocking: false)`＝
  バックグラウンド gen2。UI 停止なし。ファイナライザは背景任せ（回収開始後、数百 ms で OS へ返る）。
- キャッシュは UI 非依存のまま（`DispatcherQueueTimer` ではなく `System.Threading.Timer`）。
  カウンタは `Interlocked`（呼び出しは UI スレッド、タイマは TP スレッドのため）。
- `MemoryDiagnostics.ForceFullCollect`（Ctrl+M）は流用しない＝Aggressive／LOH 圧縮／
  `WaitForPendingFinalizers` は停止が長く、この用途には過剰。
- 期待値: 連打中のネイティブ上限 ~700MB（従来 2.1GB 超）、ナビ停止 2〜3 秒後に ~60MB 水準へ復帰。
- ビルド成功・`dotnet test` 190 件緑（`PreviewBitmapCache` はテスト対象外のため増減なし）。

### 実機確認①＝gen1 周期掃除は実アプリでは効かず、gen2 バックグラウンドへ変更（同日）

ユーザー実機確認の結果、**アイドル掃除は想定どおり**（停止後 gen2 が 1 回だけ増えて
ネイティブ 2,291→607MB。607MB は GC 後固定費の範囲＝先読みゴミとは別物）だが、
**連打中の gen1 周期掃除はほぼ無効**（ネイティブ 2GB 超まで積み上がった）。

原因はオーバーレイの GC 回数から特定できた: **gen1 GC が 176 回走っているのにネイティブが
減っていない＝ゴミが gen0/gen1 に存在しない**。実アプリは UI 由来のアロケーションで
ephemeral GC が高頻度に走るため（gen0 238／gen1 176）、デコード中（寿命 ~250ms）に生きている
RCW が昇格を重ね、大半が **gen2** に入る。素のコンソールプローブは GC が稀（120 デコードで
19 回）でゴミが gen0 に留まっていたため gen1 掃除が効いて見えた＝**検証環境が実アプリの
GC 頻度を再現できていなかった**（教訓: GC 世代に依存する検証はプローブでは代表できない）。

→ **周期掃除も gen2 バックグラウンド（非ブロッキング）へ変更**（`Sweep()` に一本化。
アイドル掃除と同じ呼び方＝`GC.Collect(MaxGeneration, Forced, blocking: false)`）。
プローブ実測ではこの方式で連打中ピーク ~700MB・+11ms/枚（並行 GC の CPU 分。実機は
デコードペースが遅いので影響はより小さい想定）。バックグラウンド GC 実行中の重複要求は
ランタイムが無視するだけなので多重起動の考慮は不要。ビルド＋テスト 190 件緑。

### 実機確認②＝改善するも最悪 2GB は残存 → H2 併用へ（同日）

ユーザー実機確認②の結果、gen2 バックグラウンド掃除で「GC のたびにネイティブ・WS が減る」ことは
確認できたが、**最悪ケースはなおネイティブ 2GB／WS 4.5GB**。オーバーレイの gen2 回数（182 回）から、
ボトルネックは GC の起動頻度ではなく**回収の遅延**（BGC は 2.5GB 級ヒープの並行処理に時間がかかり、
実行中に発生したゴミは次回まで回収不能・実行中の追加要求は無視される・ファイナライザ遅延も別枠）に
あると判断。掃除間隔の短縮（16→8）は掃除間の素の蓄積分にしか効かないため、**発生源を断つ H2
（ストリーム使い回し）の併用**を選択（ユーザー合意）。

## ネイティブメモリ肥大の対策②＝H2（WinRT ストリームの使い回し）（2026-08-03）

`InMemoryRandomAccessStream` を毎デコード作らず、同時デコード本数（ゲート上限＝2）ぶんを
使い回して最大成分 ~13MB/枚 の発生自体をなくす。実装前にプローブで再検証した。

### プローブでの再検証（定常状態 120 デコード）

- **H2 単独（掃除なし）**: 滞留 ~3.4MB/枚 に減り速度も据え置き（96ms/枚）だが、**重大な発見**＝
  溜まった残留 405MB が **BGC 耐性化**する（BGC＋5 秒でも、ファイナライザ待ち単独でも、2 回目の
  BGC でも回収されず、ブロッキングフル GC でのみ 76MB へ戻る。機構は未特定＝ストリームを生かした
  まま累積させた場合に限って起きる老化現象）。
- **H2＋周期 BGC 掃除（16 デコードごと）**: 掃除で新鮮なうちに回収すれば BGC 耐性化は起きず、
  連打中ピーク 968→**502MB**・掃除直後 33〜402MB・**アイドル掃除後 79MB**（フル GC 75MB と同水準）。
  → **周期掃除は H2 導入後も必須**（外すと BGC 耐性ゴミが溜まる）。

### 実装（`PreviewBitmapCache`）

- `_streamPool`（`Stack<InMemoryRandomAccessStream>`・UI スレッド専用でロック不要）＋
  `RentStream`/`ReturnStream`。返却はゲート解放と同じ inner finally（デコード完了後＝もう読まれない）。
  プール上限は `MaxConcurrentDecodes`（超過は捨てる保険）。
- **上書きは truncate（Size=0）せず先頭から書くだけ**（truncate はバッファを切り離して効果半減）。
  旧内容の残骸が末尾に残るが JPEG は EOI で読み終えるため実害なし（壊れた JPEG ではエラーでなく
  異常表示になりうるが許容）。「クローン生存中の上書きでもバッファ共有」という未文書化挙動に依存
  （プローブで成立確認済み。将来の Windows 更新で変われば滞留が復活するだけで壊れはしない）。
- **巨大ファイル（`ReusableStreamMaxBytes`=64MB 超）は使い捨て**＝使い回しバッファをそのサイズの
  まま常駐させない。`Clear()` はストリームプールも破棄する。
- ビルド＋テスト 190 件緑。

### 実機確認③＝H2 は実機で効果なし → 差し戻し（同日）

ユーザー実機確認③の結果、**連打中の最悪値は ~2GB のまま変化なし・停止後の床はむしろ悪化傾向**
（gen2 GC 1 回後 775MB→1,062MB）。セッションのデコード数 209 回 × 期待 churn 6.5MB/枚 ≈ 1.4GB では
ピーク 2GB と整合しない＝**H2 の削減効果が実機では出ていない**。推定＝「クローン生存中の上書きでも
バッファ共有」の未文書化挙動が、実機条件（ファイナライザ遅延で未解放クローンが長く残る）では
**書き込み時にバッファを複製する側に倒れる**（COW）。床の悪化は「H2 残留の BGC 耐性化（老化）」が
実機の BGC 遅延で実際に起きたためと推定。プローブは 2 回とも実機と乖離＝**GC 挙動に依存する対策は
プローブでは代表できない**（教訓・再確認）。

→ **H2 を差し戻し**（毎デコード使い捨てに復帰＝ゴミが BGC で素直に回収できる状態へ）、
併せて `SweepEveryDecodes` を **16→8** に短縮（ユーザー選択。掃除間の素の蓄積分 320→160MB のみ
半減する限定的な改善＝1〜2 割台の見込みと合意済み。BGC 実行中の掃除要求は無視されるだけなので
短縮の害はない）。ビルド＋テスト 190 件緑。

### 申し送り

- **実機確認④が未了**（期待値＝連打中の最悪 1.5〜1.7GB 前後〔16 のときの 2GB から 1〜2 割減〕・
  停止後は ~600〜775MB 水準へ復帰・カクつき増なし）。
- これで不足なら残る手は**案 K＝WIC の COM 直接呼び出し化**のみ（`IWICImagingFactory`→
  `CreateDecoderFromStream`→`IWICFormatConverter`→`CopyPixels` を interop で直接使い、全ネイティブを
  `Release()` で確定解放＝GC・ファイナライザ非依存でデコードごとの滞留が原理的にゼロ。
  EXIF 回転・sRGB 色管理スキップ・解凍爆弾ガードの移植が必要な 200〜400 行規模の中〜大工事）。

### 実機確認④＝周期 8 でも改善せず（むしろ悪化）→ GC 起動系の対策は打ち止め（同日）

ユーザー実機確認④の結果（デコード 406 回のセッション）:

- **連打中の最悪ネイティブ 2,453MB**（周期 16 時の ~2GB より悪化。マネージド 2,488MB ≒
  GC コミット 2,487MB で計測値は正確）。**gen2 GC が 315 回も走った上での数値**であり、
  起動頻度はもはやボトルネックではない。
- **停止後の床も 1,178MB へ上昇**。セッション累計デコード数と床の推移（607→775→1,062→1,178MB）が
  相関しており、「ブロッキングフル GC でしか回収できない残留」が通常経路でも累積する老化現象と
  整合する。推定機構＝BGC が常時回っていると、デコードゴミが常に「BGC 実行中の新規確保」となって
  gen2 送り＋取り残しが常態化する（H2 の節で見た現象の一般形）。
- 結論（ユーザー確認）: **「GC では減らせない」。gen2 BGC の高頻度化では連打中のピークを抑えられず、
  GC 起動系の対策はここで打ち止め。** 残る手段は案 K（WIC COM 直接化＝確定解放）か、
  ブロッキング GC の定期実行（カクつきとのトレードで目的に反するため非推奨）のみ。

## ネイティブメモリ肥大の対策③＝案 K（WIC の COM 直接呼び出し化）（2026-08-03）

プレビューデコードから WinRT（`BitmapDecoder`/`InMemoryRandomAccessStream`）を排除し、WIC を
COM interop で直接呼んで**全ネイティブ資源を `Marshal.FinalReleaseComObject` で確定解放**する。
デコードごとのネイティブ滞留（~20MB/枚 × GC 待ち）が原理的に消える＝GC・ファイナライザ非依存。

### 実装（新規 `Controls/WicPixelDecoder.cs`＋`PreviewBitmapCache` 改修）

- **interop 宣言**は必要スロットだけ正確に書き、呼ばないスロットは引数なしプレースホルダで位置合わせ
  （vtable 順序・GUID・enum 値は Windows SDK 10.0.26100.0 の wincodec.h と突き合わせ済み）。
  継承は使わずフラット宣言＋キャストで QI させる。
- **パイプライン**: `IWICStream.InitializeFromMemory`（ピン止めした managed 配列を直接参照＝
  圧縮データのネイティブコピー自体が無い）→ `CreateDecoderFromStream` → frame → メタデータ
  （Orientation 274／EXIF ColorSpace 40961。クエリは WinRT 時代と同じ app1 経路）→
  解凍爆弾ガード（宣言寸法を確保前に判定＝従来同等）→ 非 sRGB のみ色変換（コンテキスト無しや
  初期化失敗は変換なしで続行＝安全側）→ orientation≠1 は `CacheOnLoad` の IWICBitmap を挟んで
  `FlipRotator`（JPEG デコーダは行を上から順にしか出せず、直結すると帯ごとの再デコードが起きる
  WIC の既知の落とし穴を回避）→ `FormatConverter`(32bppPBGRA) → `CopyPixels` で**プールから借りた
  byte[] へ直接書き出し**。
- `LoadCoreAsync` は `Task.Run` でワーカー実行（WIC は同期 API）。従来 UI スレッドで払っていた
  `CopyToBuffer` の 190MB コピーも消えた。`PixelBufferPool` は Rent がワーカーから呼ばれるように
  なったためロックを追加。**案 J の GC 掃除機構（周期／アイドル gen2 BGC）は発生源が消えたので撤去**。
- **落とし穴（重要）**: `CopyPixels` を `[Out] byte[]` 宣言で呼ぶとマーシャリングでアクセス違反＝
  **プロセスごと落ちる**（実測）。出力バッファは自前で `GCHandle` ピン止めして `IntPtr` で渡すこと。

### 検証（scratchpad `WicParityProbe`＝アプリの実装ファイルをリンクして検証・セッション限り）

- **パリティ**: 岩国_100MSDCF 12 件＋20260228 20 件＝計 32 件で、従来の WinRT 経路（旧 LoadCoreAsync と
  同一条件）と**全件 byte 単位で完全一致**（orientation=8 の縦位置回転、α1/OM-1/旧機種の複数サイズ込み）。
- **確定解放**: 同一サイズ 60 連続デコード（プール再利用・GC ほぼ無し）でネイティブ横ばい
  （ドリフトは GC コミットのスナップショット遅れと整合）。
- **速度**: 41ms/枚（WinRT 経路は同条件 95ms/枚）。中間 SoftwareBitmap の実体化（190MB）と
  取り出しコピーが消えたぶん**約 2 倍速**。
- **未検証**: 非 sRGB（AdobeRGB）の色変換パス（手元の実写が全部 sRGB。失敗時は変換なしの安全側）と
  ミラー系 orientation 2/4/5/7（実写でほぼ出現せず。誤りでも鏡像表示になるだけ）。

### 申し送り

- ~~**実機確認⑤が未了**~~ → **合格（2026-08-04）**。ただし「ネイティブ行が積み上がらない」という
  期待値は誤りだった（積み上がって見えた分はプールミスの LOH 変動＝別物。次節
  「実機確認⑤と『ネイティブ』積み上がりの正体」参照）。
- 表示側（Win2D）・サムネイル側（シェル抽出＋BitmapImage）は WinRT のままだが量的に軽微
  （2026-08-04 の切り分けで両方シロと実機確認済み＝次節）。

## 実機確認⑤と「ネイティブ」積み上がりの正体＝プールミスの LOH 変動（2026-08-04）

案 K の実機確認⑤（岩国_100MSDCF で右キー連打）で、連打中の「ネイティブ」行の積み上がり
（最悪 ~2GB・`Ctrl+M` で ~800MB へ復帰）が**まだ残る**ことが判明。切り分けの結果、
**案 K は目的（WinRT デコーダのネイティブ滞留の排除）を達成しており、残っていたのは別物**と確定した。

### 切り分けの経過（すべて実機・M/C オーバーレイ観察）

1. **キャッシュ済み 11 枚内だけで ←/→ 連打** → ネイティブ横ばい（~846MB）。
   毎ステップ 190MB の GPU 転送（`SetPixelBytes`）が走っているのに増えない＝**表示転送側はシロ**。
   積み上がりは「新規デコードが走る時だけ」に限定された。
2. **通過済み範囲（サムネイル温存・プレビューキャッシュのみ失効）の再連打** → 発生。
   ＝**サムネイル経路（シェル抽出＋BitmapImage）もシロ**。残る相関は「デコード＝バッファ確保」のみ。
   このときユーザーが「画像サイズ混在地点（8640×5760 と 5616×3744）で発生しやすい」
   「プールが再利用されない時に生じている可能性」を観察・指摘（これが決め手になった）。
3. **プール割合 0%**（毎デコード新規確保） → **どこでも再現**。
   **プール割合 75%** → 積み上がりが明確に減少（WS 3.3〜3.5GB で頭打ち）。＝確定。

### 結論（二層構造だった）

- 元々の症状は「WinRT デコーダの本物のネイティブ滞留（~20MB/枚・ファイナライザ待ち）」と
  「**プールミス時に新規確保される 190MB/84MB 級 `byte[]` の LOH ゴミ＋GC の decommit 遅延**」の
  **混合**だった。案 K は前者だけを正しく消した。後者が混在サイズ地点（プールはバイト長**完全一致**
  でしか再利用しない＝`PixelBufferPool` の設計どおりの縮退）で顕著に残った。
- それが「ネイティブ」に見えたのは **M オーバーレイの算式**のせい。ネイティブ行＝
  プライベート（現在値）− GC コミット（**直前 GC 時点のスナップショット**。`GC.GetGCMemoryInfo` の仕様）
  なので、直前 GC 以降の新規 LOH 確保と decommit 未了の空き領域が全部「ネイティブ」に計上される
  （`MemorySnapshot.NativeBytes` の doc コメントに書いてあった既知の弱点そのもの）。
  実際、悪い状態ではマネージド現在値が生存分（キャッシュ＋プール＋固定費）より数百 MB 大きい
  ＝未回収ゴミの存在が読み取れた。
- 整合する傍証: `Ctrl+M` 後の基準値は毎回 ~800MB で増えない（＝リークではない）／連打停止後に
  190MB 単位で階段状に自然減（＝GC の decommit 分割実施）／案 J（GC 起動系）が効き切らなかった
  （発生源の半分が GC 管轄内のゴミで、起動しても decommit が遅延するため見かけが残る）。

### 実機確認⑤の判定

**合格**（2026-08-04）。WinRT デコーダ由来の滞留は消えた。カクつきなし・画の見た目従来同一。
「ネイティブ行が積み上がらない」という期待値の書き方が誤りで、積み上がって見えた分は上記の別物。

### 教訓

- **M オーバーレイの「ネイティブ」行は GC 直後の値どうしでしか比較できない**。連打中の現在値は
  LOH 変動を含む「見かけ」。診断時はまず `Ctrl+M` 後の基準値を比べること。
- 再現手順が複数の発生源を同時に駆動する場合（連打＝デコード＋GPU 転送＋サムネイル＋確保）、
  1 変数ずつ止める切り分け（キャッシュ内連打／通過済み再連打／プール 0%）が有効だった。

### 残課題（未着手・対策検討中）

- プール割合/予算の既定見直し（既定 20%＝512MB は混在サイズ（190MB×2＋84MB×2 級）を同居させるには
  不足。75% は効くがキャッシュ予算が保持窓を割る副作用あり）。サイズ別在庫上限などの設計変更も候補。
- M オーバーレイのネイティブ行の算式/注記の改善（今回の誤誘導の再発防止）。

## メモリ掃除係（捨てバイト閾値の背景 GC＋アイドル完全 GC）（2026-08-04）

前節で確定した「プールミスの LOH 変動で WS が予算の 2 倍近くまで膨らむ」への対策。
プール割合の既定引き上げは**採らない**（ユーザー判断: プールはバイト長完全一致でしか再利用できず、
サイズ混在フォルダでは割合を上げても構造的に効かない。極端な話、全画像のサイズが異なれば効果ゼロ。
プールはほどほどの容量で十分）。代わりに **GC を適切なタイミングで明示起動して回収・decommit を
早める** 2 段構成にした。

### 設計（新規 `Controls/MemoryJanitor.cs`＋配線）

- **段1: 捨てバイト閾値の背景 gen2 GC**。プール／キャッシュが捨てた累積バイト数
  （`PixelBufferPool.DiscardedBytes`＋`PreviewBitmapCache` の Clear 直接破棄分）を `Trim` のたびに
  差分報告し、累計 512MB（190MB 級 2〜3 本）で `GC.Collect(2, Forced, blocking:false)` を 1 回発行。
  連打中でも停止は数 ms 級で、膨らみが「予算＋閾値」で頭打ちになる。Optimized にしないのは
  ゴミの存在が閾値到達で確定しているため（GC 側の見送り判断は不要）。
- **段2: アイドル完全 GC**。最終操作から 20 秒ノー操作が続き、まだ dirty（捨てが残っている）なら
  `MemoryDiagnostics.ForceFullCollect()`（`Ctrl+M` 相当＝Forced→ファイナライザ待ち→Aggressive＋
  LOH CompactOnce・~300ms）で基準値まで戻す。背景 GC が待たないファイナライザ絡みの取り残しも掃ける。
  結果はオーバーレイの GC 結果パネルへ「アイドル GC（xxx ms）」として共用表示（勝手には開かない）。
- **操作の検知**: プレビューのナビ（`RequestPreviewLoad`）に加え、グリッド表示中も効くよう
  MainPage のグローバルキー（`HandleGlobalKeyDown`）とページ全域のホイール/クリック
  （`AddHandler` handledEventsToo）を中継。操作中にブロッキング GC が当たるのを避ける。
- **タイマーは dirty の間だけ回す**（5 秒周期・掃除が済んだら自己停止。非表示中は止める
  `MemoryOverlay` と同じ流儀）。
- `MemoryJanitor` は純 C#（clock 注入可能）でテスト 7 件、`PixelBufferPool.DiscardedBytes` に 3 件を追加
  ＝**テスト計 200 件緑**。

### 案 J との関係（再評価）

案 J（連打中の周期 BGC）は「効かない」と判定して撤去したが、当時の判定材料は**補正前のネイティブ行**
だった。相手の半分（WinRT 滞留）には実際効かず、もう半分（LOH 変動）には効いていた可能性がある。
今回は相手が GC 管轄内のゴミと確定しており、かつ発生量連動（時間周期ではなく閾値）・アイドル完全 GC
との 2 段構成である点が異なる。

### 実機確認（未了）

確認ポイント: ①サイズ混在地点の連打で WS/プライベートが「予算＋閾値」水準で頭打ちになるか
（補正済みオーバーレイの GC 直後基準で判定）②連打中のカクつきが増えないか（背景 GC の CPU 競合）
③操作をやめて ~25 秒でオーバーレイに「アイドル GC」が出て基準値（ネイティブ ~800MB 台）へ戻るか
④グリッドスクロール中にアイドル完全 GC が当たってカクつかないか。

## 残タスク（記録・すべて完了済み）
- ~~プレビューのキーボード入力フォーカス問題~~ → **完了（`f54d9b4`）。** 上の「現在の進捗」参照。
- ~~Phase 3 ステージ B 残: 右ナビゲーター／ズームプレビュー／`Ctrl+Alt+矢印`／`Ctrl+Alt+F`~~ → **完了（`993c7c2` プッシュ済み）。**
  残微調整: ルーペのロード時センタリングは初期レイアウト未確定だと AF 点がやや上寄りになる場合あり（軽微）。
  AF 枠の正確な位置（回転画像）はユーザー最終確認推奨。
- ~~Phase 4-A: フィルタ／クリップボード出力~~ → **完了（`c073853`）。** 上の「現在の進捗」参照。
- ~~Phase 4-B: 外部連携（`Ctrl+E`／`Alt+E`／`Ctrl+Alt+E`／`Alt+S`）＋設定（`AppSettings.SharePath` 設定化・歯車→設定ダイアログ）~~
  → **完了（2026-06-26）。** 上の「現在の進捗」参照。`M`（デバッグ GC）は 2026-07-28 に実装済み。
- ~~パッケージング: 素の自己完結 EXE の publish 構成を組み込み＋発行確認~~ → **完了（2026-06-20）。** 上の「現在の進捗」参照。
  pubxml 2 系統（フォルダ／単一ファイル）＋`Publish.ps1`。実発行・起動確認済み（コミット済み）。

## メモリ時系列ログ（2026-08-05）

### 目的

メモリオーバーレイ（`M`）は目視・スクショでしか推移を追えず、「いつ・何をしたら・どれだけ増減したか」の
因果が残らない。連続ナビ中の LOH 変動やアイドル GC の効き具合を後から機械的に分析できるよう、操作イベント
＋ 250ms 周期のメモリサンプルを TSV に時系列記録するデバッグ用ロガー `MemoryLog`（`Ctrl+Shift+M`）を追加した。

### 使い方

- プレビュー中・グリッド表示中どちらでも `Ctrl+Shift+M` で記録を開始/停止する（トグル）。
- 開始すると `AppSettings.LogsFolder`（設定フォルダ配下の `logs\`。Debug/Release で親フォルダが分かれる
  既存の分離＝`PhotoQuickSelector.Dev`/`PhotoQuickSelector` にそのまま乗る）に
  `memlog_yyyyMMdd_HHmmss.tsv` が作られる。
- 開始と同時にメモリオーバーレイを表示状態にし（非表示なら開く。表示中は何もしない）、
  オーバーレイ上部に「● REC \<ファイル名\>」を出して記録中であることを分かるようにする。
- 停止すると記録終了。`MemoryLog.CurrentFilePath` は停止後も直近ファイルを指したままなので、
  次に `M` でオーバーレイを開けば場所を確認できる（UI 側では現状ファイルパスの表示はしていない。
  必要ならエクスプローラで `logs\` を直接開く）。

### 設計・実装

- `src/PhotoQuickSelector.App/MemoryLog.cs`（`MemoryDiagnostics.cs` と同階層・同 namespace）に
  **WinUI/WinRT 非依存の純 C#** で実装（`DispatcherQueue` は使わない）。250ms 周期のサンプリングは
  ホスト（`MainPage`）が `DispatcherQueueTimer` で駆動し `MemoryLog.Sample` を呼ぶだけで、
  `MemoryLog` 自身はタイマーを持たない＝xUnit からソースリンクしてテストできる
  （`PreviewViewport`/`DecodeGate`/`PixelBufferPool`/`MemoryJanitor` と同じ方式。
  `MemorySnapshot` 定義元の `MemoryDiagnostics.cs` も併せてリンク）。
- インスタンスクラス＋アプリ共用の `MemoryLog.Current`（テストは `new MemoryLog()` で独立させる）。
- 各記録メソッド（`Sample`/`Nav`/`DecodeDone`/`Discarded`/`Gc`/`Note`）は非記録中は即 return の no-op で、
  行文字列を組んで `ConcurrentQueue<string>` へ積むだけ（ロックなし・任意スレッドから呼んでよい）。
  書き出しは 500ms 周期の `System.Threading.Timer` がキューを drain して `StreamWriter` へ書く
  （ライター本体の開閉／書き込みだけ最小限のロックで保護）。行フォーマットは `FormatSample` 等の
  static な純関数に分離してあり、テストはこれを直接呼んで検証する。
- 全行の1列目＝`Start()` からの経過 ms（`Stopwatch` 起点）。2列目＝レコード種別。ヘッダはアプリバージョン
  （`AboutDialog` と同じ取得方法＝`AssemblyInformationalVersionAttribute`）・ビルド構成・開始時刻・
  サンプル周期を `#` 始まりの1行で書く。

### TSV スキーマ（タブ区切り。数値はバイト単位の生値）

- `SAMPLE`: managed, committed, private, ws, native, gen0, gen1, gen2, lohSize, lohFrag, cacheBytes,
  cacheCount, inflightCount, poolBytes, poolCount, poolHit, poolMiss, discardedBytes, janitorDirty(1/0),
  allocatedBytes, pohSize（末尾 2 列は 2026-08-05 の後付け＝「列追加」追記参照）
  - `lohSize`/`lohFrag` は `GC.GetGCMemoryInfo().GenerationInfo[3]` の `SizeAfterBytes`/`FragmentationAfterBytes`
    ＝**直前の GC 完了時点のスナップショット**（`GCMemoryInfo` の仕様。現在値ではない＝GC が走らない間は
    更新されない）。プールミス時の LOH 変動仮説を検証するための列で、この前提込みで読むこと。
    `pohSize`（POH＝GenerationInfo[4] の `SizeAfterBytes`）も同じく直前 GC 時点のスナップショット。
  - `allocatedBytes` は `GC.GetTotalAllocatedBytes(precise: false)`＝プロセス起動からの**累積**確保バイト
    （現在値・単調増加。回収されても減らない）。サンプル間の差分がアロケーションレートになり、
    「GC 間の WS/private 増加＝新規確保の積み上がり（回収待ちゴミ）か・真のネイティブ増か」を
    機械的に切り分けられる。
  - `cacheBytes`/`cacheCount`/`inflightCount`/`poolBytes`/`poolCount`/`poolHit`/`poolMiss`/`discardedBytes`/
    `janitorDirty` は `PreviewControl.CollectMemoryLogExtras()`（`MemoryLogExtras`）が
    `PreviewBitmapCache`/`PixelBufferPool`/`MemoryJanitor` から集める。
- `NAV`: fileName, width, height（`FocusedPhoto` 変更時。寸法は EXIF Orientation 適用後の表示寸法＝
  `ImageMetadata.Width`/`Height`）
- `DECODE`: fileName, bytes, elapsedMs, poolHit(1/0), fileBytes（`File.ReadAllBytesAsync` 直前から
  `WicPixelDecoder.Decode` 完了まで。I/O 込みで測ることで遅さの原因が I/O かデコードかを切り分けられる。
  `fileBytes`＝読み込んだ圧縮ファイルのバイト数（2026-08-05 後付け）。毎デコードの読み捨て byte[] が
  生む LOH 負荷を定量するための列で、ピクセルの `bytes` とは別枠）
- `DISCARD`: deltaBytes（キャッシュ/プールからの破棄＝LOH ゴミ化。`PreviewControl.ReportDiscards` の差分）
- `GC`: kind, elapsedMs, beforeManaged, beforeNative, beforeWs, afterManaged, afterNative, afterWs
  （`kind` は `ctrlm`＝Ctrl+M の強制フル GC／`idle`＝メモリ掃除係のアイドル完全 GC／`bgc`＝メモリ掃除係の
  背景 gen2 GC／`blk`＝メモリ掃除係のブロッキング gen2 GC（2026-08-05 追加＝「ブロッキング段」節参照）。
  `bgc` は非ブロッキング発行で前後値を取らないため after 側 3 列は空文字）
- `NOTE`: kind, detail（`rec-start`/`rec-stop`/`folder`/`preview-enter`/`preview-exit` 等）

### フック地点

- `MainPage.HandleGlobalKeyDown`: `Ctrl+Shift+M` トグル（`M`/`Ctrl+M` と同じキーで修飾子違い。
  3 つの割り当てを厳密に判定＝`Ctrl+Alt+M` 等では発火しない）。250ms `DispatcherQueueTimer` の
  起動/停止・`MemoryLog.Current.Start`/`Stop` 呼び出し・開始時のオーバーレイ表示（`EnsureShown`）。
- `PreviewBitmapCache.LoadCoreAsync`: デコード計測＋ `DecodeDone`。`PixelBufferPool.Rent` に
  `out bool reused` オーバーロードを追加（既存 `Rent(int)` はそちらへ委譲・Hit/MissCount のロジックは不変）し、
  poolHit をクロージャで拾う。
- `PreviewBitmapCache`: `CachedBytes`/`CachedCount`/`InflightCount` プロパティを追加
  （`CollectMemoryLogExtras` 用）。
- `PreviewControl.ReportDiscards`: 破棄差分を `Discarded` へ。
- `PreviewControl` コンストラクタ（`MemoryJanitor` の `requestBackgroundGc`/`requestFullGc`）:
  `Gc("bgc", ...)`/`Gc("idle", ...)`。
- `PreviewControl.OnViewModelPropertyChanged`（`FocusedPhoto` case）: `Nav(...)`。
- `PreviewControl.CollectMemoryLogExtras()`: 新規追加（`MainPage` の 250ms タイマーから呼ぶ）。
- `MemoryOverlay.ForceGarbageCollect`: `Gc("ctrlm", ...)`。`MemoryOverlay.Update`: REC インジケーター表示。
  `EnsureShown()` を新規追加（非表示なら表示、表示中は何もしない＝`Toggle()` と使い分け）。
- `MainViewModel.LoadFolderAsync`: フォルダ読み込み完了直後（`SetStatusPath` の次）で `Note("folder", ...)`。
- `MainViewModel.EnterPreview`/`ExitPreview`: `Note("preview-enter")`/`Note("preview-exit")`。
- `AppSettings.LogsFolder`: 新規追加（`internal static`。`SettingsPath` と同じ素の
  `%LOCALAPPDATA%\{SettingsFolderName}\logs`。packaged 実行では settings.json と同様に MSIX 仮想化で
  リダイレクトされる）。
- テスト: `PhotoQuickSelector.Core.Tests/MemoryLogTests.cs`。行フォーマット純関数（各レコード種別・
  タブ区切り・`bgc` の空フィールド）＋ Start/Stop ライフサイクル（ヘッダ・`rec-start`/`rec-stop`・
  多重 Start の no-op・Stop 後 no-op・Start 前 no-op）を検証。テスト計 212 件緑。

### 初回ログ解析と列追加（2026-08-05）

初回の実機ログ（`memlog_20260805_223744.tsv`＝左押しっぱなし→右連打・60 秒・NAV 415／DECODE 204）を
解析した結果:

- **サイズ混在地帯（プールミス・DISCARD が出る区間）ではメモリ掃除係が想定どおり機能**し、
  WS は 3.4〜4.0GB（≒予算 2.5GB＋ネイティブ基礎 ~0.9GB＋捨てバイト閾値ぶん）で頭打ちになる。
- 問題は**単一サイズ地帯で全プールヒットになる区間**。DISCARD が止まる＝掃除係の発火契機（捨てバイト
  閾値）が消え、gen2 GC が自然発生（4〜8 秒間隔）のみになり、その間に回収待ちの使い捨てアロケーションが
  積もって **WS が 4.5GB までピーク**した（自然 gen2 のたびに −300〜600MB 回復＝リークではない）。
- 積もる正体の最有力は `PreviewBitmapCache.LoadCoreAsync` の `File.ReadAllBytesAsync`＝**毎デコードで
  圧縮 JPEG 全体（α1 で 10〜30MB 級）を新規 byte[] へ読み捨て**る分（4.4 デコード/s で 50〜100MB/s の
  LOH ゴミ）。これは `DiscardedBytes` に算入されず掃除係から不可視。ただしこの推定はログからの
  状況証拠のみだったため、確証を取る目的で列を 2 つ後付けした（**既存パーサ互換のためいずれも行末尾へ追加**）:
  - `SAMPLE` 末尾に `allocatedBytes`（`GC.GetTotalAllocatedBytes` 累積）と `pohSize`（POH サイズ）
  - `DECODE` 末尾に `fileBytes`（読み込んだ圧縮ファイルのバイト数）
- 検証手順: 同じ操作（単一サイズ地帯での連打）を再記録し、全ヒット区間の
  「`allocatedBytes` の増加レート ≒ Σ`fileBytes` レート」かつ「WS 増加が allocatedBytes 増加に随伴し
  gen2 で回復」なら読み捨て仮説が確定。対策候補は①圧縮バイト読み込みのプール化
  （`RandomAccess.Read`＋rent。`IWICStream.InitializeFromMemory` は長さ指定なのでオーバーサイズ
  バッファ可）②掃除係の閾値カウントへ `fileBytes` を算入、の 2 案（未着手）。
- なお Ctrl+M 後の床は WS 3.17GB（キャッシュ 2.28GB＋マネージドその他＋ネイティブ基礎 ~0.9GB。
  フル GC でもネイティブは 1183→1181MB とほぼ不動＝案 K 後の真のネイティブ滞留は無し）。予算に対する
  床の高さは構造的なもので、下げるには未着手の「使っていない間のキャッシュ解放」が必要。

### 再記録ログでの検証結果＝読み捨て仮説は主因として棄却（2026-08-05）

列追加後に同一操作を再記録（`memlog_20260805_225510.tsv`＝65 秒・NAV 323・DECODE 167・
うちプールミス 59＝35%）。`allocatedBytes` により総アロケーション 8,519MB が完全に分解できた:

| 発生源 | 量 | 割合 |
|---|---|---|
| プールミス時のピクセル新規確保（190MB/80MB 級） | **7,254MB** | 85% |
| 圧縮ファイルの読み捨て（Σ`fileBytes`） | 989MB | 12% |
| その他すべて（UI・サムネイル等） | 276MB | 3% |

- **読み捨てファイル仮説は主因としては棄却**。実測 `fileBytes` は平均 5.9MB/デコード
  （前節の 10〜30MB 級という想定が過大だった。このフォルダの JPEG は小さい）。
  対策候補②（掃除係へ `fileBytes` 算入）・①（読み込みのプール化）は効果が小さく**棚上げ**。
- **WS ピーク（4,659MB＠25.8s）の正体＝プールミスの連発バースト**。ミスが 54→62 に増える
  2.6 秒間に dAlloc 199MB/250ms 級が連続（最大 ~800MB/s）し WS が 3,709→4,659 に急騰。
  この間メモリ掃除係の bgc は 3 回発火していたが、**背景 gen2 は非ブロッキングで回収がバーストに
  追いつかず**、回収待ちゴミが 1GB 超積もった（掃除係の設計どおりの場面で速度負け）。
- 全ヒット地帯（miss 固定区間）では dAlloc ≒ dFileBytes となりアロケーションはほぼファイル読みのみ。
  この区間の WS はむしろ緩やかに下降し、**初回ログで見えた「全ヒット地帯での積み上がり」は再現せず**
  （直前バーストの残債＋native 算式の見かけだった可能性が高い）。POH はほぼゼロ＝ピン止め影響なし。
  操作終了後は床 2,957MB（キャッシュ 2,088＋プール 80＋ネイティブ基礎 721MB）まで復帰＝リークなし再確認。
- **今後の本命はプールミスの削減**（総ゴミの 85%）。ミスはサイズ混在地帯で 190MB と 80MB が
  完全一致プールを相互に追い出し合うのが原因なので、**サイズクラス化**（実測上サイズは 2 種だけ
  なので、サイズごとに別枠で保持する程度の小改修）でミスの大半を潰せる見込み（未着手）。
  代替/補完＝バースト時だけブロッキング gen2 に切り替える案はカクつきリスクがあるため、
  サイズクラス化で足りるならやらない。なお瞬間ピークは数秒で掃除係が戻すことも確認できたので
  「一瞬 4.6GB は許容」という判断もあり得る。
  ※この優先順位は直後のユーザー判断で逆転した＝次節「メモリ掃除係のブロッキング段」参照。

## メモリ掃除係のブロッキング段（背景 GC の速度負け対策）（2026-08-05）

前節で本命としたプールのサイズクラス化は**採らない**（ユーザー判断）。理由＝プールの効果はフォルダ内の
画像サイズ構成に依存し、全サイズ相異なら効果ゼロ・サイズ混在はよくある状況なのに、サイズ別枠は
もう出てこないサイズのバッファがプール予算を占有し続けるデメリットを持つ。「メモリを一定量に抑える」
というゴールに対する**上限の保証**にはならない。代わりに、どんなサイズ構成でも効く**掃除の強化**＝
メモリ掃除係に「ブロッキング gen2 GC の段」を追加した（2段→3段構成）。

### 設計（`Controls/MemoryJanitor` の段追加）

- **発火条件＝「背景 GC の速度負け」の検出**: gen2 GC の完了カウント（`GC.CollectionCount(2)`）が
  1 つも進まないまま捨てバイト累計が高閾値（既定 1GB）に達したら、`GC.Collect(2, Forced,
  blocking: true, compacting: false)` を発行する。gen2 が完了していれば背景 GC が追いついている
  ＝起算をリセットしてブロッキングは打たない。**捨てバイト量だけを閾値にすると背景 GC が
  勝っている時にも無駄にカクつかせてしまう**ため、完了カウント連動にした。
- 発行時は背景 GC 段の累計もリセットし、その回の背景 GC 判定はスキップ（直後に重ねて打たない）。
  アイドル完全 GC（Tick）でも起算をリセット。Aggressive／LOH 圧縮はやらない（アイドル完全 GC の役目）。
- **閾値は設定項目**: `AppSettings.BlockingGcThresholdMB`（既定 1024。0 以下で無効）。
  設定＞高度な設定＞キャッシュ容量予算グループに NumberBox（`Settings_BlockingGcNote`・ja/en resw）。
  `MemoryJanitor.BlockingGcThresholdBytes` は可変プロパティで、`PreviewControl.ApplySettings` が適用。
- メモリログには `GC` 行 kind=`blk` で前後値＋所要 ms を記録（ブロッキングなので after が取れる）。
- カクつきコストの見積り（再記録ログの実測より）: フル GC（Aggressive＋LOH 圧縮込み）が 236〜253ms、
  ミス時の α1 デコードが平均 149ms。blk は圧縮なしなのでフル GC より短い見込みで、発火するのは
  ミス連発中＝もともとナビが遅い時間帯に限られるため体感への影響は小さいと判断。
- 期待効果: WS はおおむね「キャッシュ容量予算＋閾値＋ネイティブ基礎」で頭打ち
  （既定値なら ~4.2GB 水準。厳しくしたければ閾値を 512MB 等へ下げる）。
- テスト: `MemoryJanitorTests` に 5 件追加（無効時は発火しない／gen2 無完了で高閾値到達→発火＋
  背景段の累計リセット／途中で gen2 完了→起算リセットで発火しない／発火後の再発火／
  アイドル完全 GC で起算リセット）＝**テスト計 217 件緑**。gen2Count はコンストラクタ注入で模擬。

### 実機確認①＝不発火（2026-08-05）→ 判定を実測ゴミ量へ作り替え（2026-08-06）

実機ログ（`memlog_20260805_234033.tsv`）で **blk は一度も発火せず、WS ピークは 4,662MB＝対策前と
同水準**だった。原因＝上記の gen2 完了カウント判定の前提の誤り:

- 背景 gen2 は掃除係自身が**捨てバイト 512MB ごとに発行**しているため、バースト中でも 0.5〜1 秒ごとに
  「完了」してカウントが進む＝発行も完了も捨て量に連動し、**完了カウントは回収の追いつきの情報を
  持たない**。完了間の捨て量は最大 ~840MB で閾値 1,024MB に構造的に届かなかった。
- 一方で回収は追いついていない（gen2 は**開始時点に存在したゴミしか回収できず**、~800MB/s のバースト
  では 1 回 ~600MB 回収 vs 新規 800MB/s でスループット負け。例: 完了でマネージド −657MB でも WS −72MB）。

副次観測（列追加の成果）: ①マネージドの回収待ちゴミはピークでも **~950MB**（Mng 3,345 − キャッシュ
1,898 − プール 481）②WS 超過の残りは **decommit 遅れ**（ctrlm でマネージド −13MB しか減らないのに
WS は即時 −490MB＋2 秒でさらに −350MB）。よって完璧なタイミングで blk を打っても現実的キャップは
「床 ~3.4GB＋ゴミ閾値＋decommit 遅れ」＝ **4.0〜4.2GB 前後**が期待値。

**Aggressive の検討**（ユーザー質問への回答）: Aggressive なら decommit 遅れの大半も解放できる
（ctrlm の実測が証拠）が、それでも ~200〜350MB の尻尾は数秒残り、停止は最重量（実測 236〜263ms）・
ライブ 2.3GB の LOH 圧縮コピー・バースト継続中は decommit→再 commit の往復コストを払う。
→ **blk は素の Forced（blocking・compacting なし）とし、Aggressive はアイドル GC 専用に残す**。
実機で「blk 後も WS が落ち切らない」と確認されたら格上げは 1 行変更で試せる。

**作り替え（2026-08-06 実装）**:

- 発火判定＝**実測ゴミ概算** `GC.GetTotalMemory(false) − ResidentBytes` ≥ 閾値
  （`ResidentBytes`＝キャッシュ在籍＋プール在籍＋デコード中貸出。`PreviewBitmapCache` に
  `_inflightPixelBytes`〔Interlocked・rentBuffer で加算・結末の finally で減算〕を追加）。
  gen2 カウント注入は撤去。ゴミ量が閾値超え＝「背景 GC が追いついていない」事実そのものを見る。
- **再武装ガード**: 前回 blk 以降の捨てバイトが閾値の半分に達するまで再発火しない
  （概算の推定誤差〔サムネイル等の他ライブ分 ~100-300MB〕だけで連打されるのを防ぐ。
  ガード成立時のみ `GetTotalMemory` を呼ぶ短絡順序）。
- **Tick（5 秒周期・dirty 中）でも判定**: バースト直後に捨てが止まり、誰も GC を起動しないまま
  残債が数秒滞留する現象（実機ログで WS 4.4GB が 3 秒残留）を掃く。
- 既定閾値 **1024→512MB**（ゴミ実測が ~950MB 止まりのため 1GB では発火しない。設定説明文も
  「回収待ちの不要メモリがこの量を超えると…」へ更新）。テスト計 219 件緑。

### 実機確認②＝マネージドキャップは成立・WS ピークは decommit 遅れで不変（2026-08-06）

実機ログ（`memlog_20260806_000933.tsv`・前回と同一操作）の結果:

- **成立**: blk はバースト中に 8 回発火（静穏期の乱発なし＝再武装ガード機能）。停止は **26〜58ms
  （平均 ~37ms）**で懸念の 100〜250ms を大幅に下回り、α1 デコード 1 枚（~150ms）より短い。
  各 blk でマネージド −600〜700MB を同期回収し、マネージドヒープは瞬間最大 3.5GB → 即 2.8GB へ
  戻る＝**バーストがどれだけ続いてもゴミが閾値近傍で頭打ちになる上限保証が成立**。床 3,161MB へ復帰。
- **不変**: WS ピークは 4,603MB（前回 4,662MB）とほぼ同じ。原因＝ blk の前後値が証拠で、
  例: マネージド 3,499→2,860（−639MB）でも WS 4,470→4,509 と不動。**回収済み空き領域の decommit が
  数秒遅れる**（free だが committed のページが ~400〜500MB・数秒残って一括解放される。26.3〜29.1s は
  ゴミ ~300MB まで回収済みなのに WS 4.3GB が滞留→29.4s に −473MB）。素の Forced gen2 では制御不能。
  Tick 掃きが発火しなかったのはゴミが閾値未満で正しい判定（滞留の正体が回収待ちではないため）。
- 判断: 実質目標（上限保証・小停止・数秒でピーク解消・床復帰）は達成。残るはタスクマネージャー上の
  瞬間値 ~4.6GB が数秒見えることのみ→ **blk の Aggressive 格上げ（decommit 同期化）の効果と
  カクつきを実測して判断する**ことにした（次項）。

### 実機確認③＝Aggressive 格上げの実測 → 2 段判定へ（2026-08-06）

blk を Aggressive（blocking・compacting）にした実験ビルドの実測（`memlog_20260806_002050.tsv`・同一操作）:

| 指標 | Forced 版 | Aggressive 版 |
|---|---|---|
| blk 発火回数 | 8 回 | 3 回 |
| blk の停止時間 | 26〜58ms | 203〜305ms |
| blk 直後の WS | ほぼ不動（数秒遅れ） | **同期で −625〜−1,241MB** |
| WS ピーク（250ms サンプル） | 4,603MB | 4,455MB |
| バースト中の WS 水準 | 4.3〜4.6GB が数秒継続 | **3.3〜3.9GB 中心**・4.3〜4.5GB は瞬間のみ |

- 瞬間最大値そのもの（blk 直前の一瞬）は両版とも ~4.8〜5.0GB に触れる。差が出るのは
  「4GB 超えの継続時間」（数秒→サブ秒）と「バースト中の平均 WS」（~0.5GB 低下）。
- ユーザー判断: **カクつきは許容範囲**。ただし「閾値 512MB なのに 1.2GB 解放される」＝発動条件と
  解放量の乖離が直感的に分かりづらい。解放量の内訳は ①判定の離散性によるオーバーシュート
  （バースト中はチェックひと跨ぎで 600〜700MB）②過去の GC が回収済みだが **OS 未返却の在庫**
  （free だが committed。27.4s の blk は在庫 628MB が上乗せされ −1,241MB）③断片化の圧縮分。
- → **判定を「ゴミ＋在庫」の合計に統一し、モードを在庫量で選ぶ 2 段判定へ**（同日実装）:
  - ゴミ＝`GetTotalMemory − ResidentBytes`（従来）／在庫＝`GCMemoryInfo.TotalCommittedBytes −
    GetTotalMemory`（直近 GC 完了時点のスナップショット。背景 gen2 完了直後は未 decommit の空きを
    含むので在庫が写る。**GCKind 不問＝gen0/gen1 の記録にも総コミットが入る**ため、割り当てが続く
    バースト中は 0.5〜2 秒ごとに更新され鮮度は実用十分。GC が走らない静穏時は高止まりに見える
    欠点があり、再武装ガードで過発火を抑える）。
  - 発動: 合計 ≥ 閾値（既定 512MB・再武装ガード従来どおり）。設定の意味が「回収待ちの合計が
    この量を超えたら緊急回収」になり、解放量と発動条件が一致して直感的に。
  - モード: **在庫 ≥ 閾値の半分 → Aggressive**（`agr`。返却まで必要）／未満 → **素の Forced**
    （`blk`。回収だけで足りる・26〜58ms）。Forced の decommit 遅れは在庫として次回判定に現れる
    ので、溜まれば自動的に Aggressive へ格上げされる自己修正型。
  - メモリログの GC 行 kind に `agr` を追加（`blk`＝Forced 段と区別）。テスト計 221 件緑。

### 実機確認④＝2 段判定に盲点が判明 → 常時 Aggressive へ回帰（2026-08-06）

2 段判定の実機ログ（`memlog_20260806_004757.tsv`）で、blk（Forced）連発後に **WS 4.3GB が
~2 秒滞留**し agr へ格上げされない事象を確認。原因＝**Forced ブロッキング gen2 の後、返却予定の
ページは `TotalCommittedBytes` から同期的に除外される一方で実返却は数秒遅れ、その間 Mng にも
Comm にも写らない**＝在庫の式（Comm − Mng）が ~100〜270MB にしか見えず（実態 ~800MB）、
合計が閾値に届かず死角になる。背景 gen2 完了後は freed 領域が Comm に残るので在庫が写る
（20.8s の agr は正しく発火）のと非対称。

- ユーザー提案「合計 ≥ 閾値でまずノンブロッキング GC → 取り直して再判定 → 超過なら Aggressive」も
  検討したが不成立と評価: ①ステップ 1 は既存の段 1（捨てバイト 512MB ごとの bgc）と重複＝合計が
  閾値に達した時点でそれが追いつかなかったことは確定済み ②ノンブロッキングは発行直後に結果を
  取り直せず（完了は 0.5〜2 秒後）、即再判定なら常に Aggressive 行き・完了待ちなら ~800MB/s の
  バースト中に待ち時間ぶん積み増しでピークが悪化 ③Forced 後の盲点も残る。
- → **発動条件（ゴミ＋在庫 ≥ 閾値）は維持し、モード選択を廃止して常に Aggressive**（kind=`agr`・
  ユーザー判断）。Aggressive は毎回完全に掃くため発火回数自体が減り（実測③: 3 回/39s）、総停止
  時間は 2 段構成と同水準。テスト計 220 件緑。
- 期待値の注記: 瞬間 ~4.3〜4.5GB のひげは常時 Aggressive でも出る（閾値 512MB＋再武装 256MB の
  設計上、~800MB/s バーストでは次の発火までの積み増しだけで数百 MB 上振れするため）。ひげの高さを
  さらに下げる実効的なつまみは閾値を下げること（512→256MB＝発火頻度と引き換え。設定項目済み）。
- **WS 値そのものを発動条件にする案**も検討し不採用（ユーザー質問への回答）: WS は OS が他プロセスの
  メモリ圧で勝手にトリムする値で、アプリの状態ではなくシステム状況で変わる（見るなら private＝
  コミットの方）。またプロセス実測にはネイティブ基礎（D3D/WIC/UI。フォルダ・GPU で変動）が含まれ、
  固定閾値では「GC で回収できないネイティブ増」でも発火する誤爆を持ち込む。GC で回収できる量とは
  無関係に発火し得るため、停止を払って何も減らない空振りが構造的に避けられない。詳細は本節参照。

### 実機確認⑤＝常時 Aggressive 確定版・合格（2026-08-06）

確認ポイント: ①バーストで `agr` が発火し WS 水準が実測③（3.3〜3.9GB 中心・4GB 超はサブ秒）と
同等か ②blk 連発後の滞留（4.3GB×2 秒）が再発しないか ③停止時間の分布（~140〜300ms）
④静穏時の過発火が無いか。

結果＝**4 点とも良好**（`memlog_20260806_024730.tsv`＝56.5 秒・デコード 199 回・プールミス 56 回・
捨て累計 5.3GB。※同日導入の `System.GC.HeapHardLimit`＝3.5GiB 併用構成＝次節）:

- ① WS 3.3〜3.9GB 中心・最大 4.155GB・3.9GB 超は 1 サンプル（サブ秒）＝実測③と同等。
- ② 滞留なし。最悪バースト（20〜25s 区間・WS 4.16GB）も ~0.4 秒で回収。
- ③ `agr` 6 回・停止 116〜269ms（avg 205ms）＝従来分布どおり。
- ④ 静穏区間（50s 以降）の発火なし。

## マネージドヒープのハードリミット（System.GC.HeapHardLimit）

（2026-08-06）「GC の挙動調整（ConserveMemory・掃除係）でメモリを分かりやすく抑えるのは難しい」
というユーザーの実感から、**ハードリミット**を検討・導入した。

### 3 案の比較

- **案1（採用）: `System.GC.HeapHardLimit`** — マネージドヒープ（SOH+LOH+POH）のコミット量の
  絶対上限。上限接近でランタイムがブロッキング圧縮 GC を強制し、GC しても収まらない確保は
  `OutOfMemoryException`＝クラッシュ。csproj 1 行で導入でき、WS ピークの主因（プールミス時の
  LOH バースト）がちょうど管轄内。
- **案2（不採用）: Job Object**（`JOB_OBJECT_LIMIT_PROCESS_MEMORY`）— ネイティブ込みで
  プロセス全体のコミットを制限できる唯一の手段だが、超過時は任意の場所（WIC・D3D・XAML）の
  確保が失敗し、回復可能な設計がほぼ不可能。
- **案3（不採用）: `SetProcessWorkingSetSizeEx`（HARDWS_MAX）** — WS の数字そのものに上限を
  かけるが、コミットは減らず OS がページをスタンバイへ強制トリムするだけ＝半分見かけの対策。
  連打中は「トリム→即フォールト」の空回りで性能を削る。

### 導入と検証

- csproj の `RuntimeHostConfigurationOption` に `System.GC.HeapHardLimit`=3,758,096,384（3.5GiB）。
  値の根拠＝キャッシュ＋プール予算 2.5GB＋同時デコード 2 本×約 200MB＋サムネイル圧縮バイト常駐＋余裕。
  **`AppSettings.CacheBudgetGB` を上げる場合はこの値も連動して見直すこと**（csproj コメントにも明記）。
- **落とし穴＝int32 超の値は runtimeconfig.json に文字列で出力される**（`"3758096384"`。
  ConserveMemory=7 は数値のまま）。文字列形式でも適用されるかを最小コンソール（net10.0・同じ
  `RuntimeHostConfigurationOption`）で検証し、`GC.GetGCMemoryInfo().TotalAvailableMemoryBytes` が
  上限値そのものを返すことを確認＝適用 OK。
- ハードリミット設定時は `TotalAvailableMemoryBytes` の意味が「物理メモリ量」→「上限値」に
  変わるが、この API の参照箇所はソース中ゼロを確認（副作用なし）。

### 実機確認＝ログ 2 本で役割分担を切り分け（2026-08-06）

1. **掃除係②あり**（`memlog_20260806_024730.tsv`＝上記実機確認⑤と同一ログ）: 共存に問題なし。
   コミット最大 3.289GB（上限まで余裕 0.47GB・上限超えサンプル 0）・OOM なし・停止分布も従来どおり。
   ただし掃除係②が先に掃除するため**ハードリミット自体は未発動**。
2. **掃除係②なし**（`BlockingGcThresholdMB`=0・`memlog_20260806_025348.tsv`＝49.8 秒・デコード
   156 回・プールミス 61 回・捨て累計 6.1GB）: **OOM なし・コミット最大 3.249GB で頭打ち**。
   ランタイム自発の gen2（50 秒で約 21 回）が上限手前の 3.0〜3.25GB 水準で抑え込み、暴走しない。
   **ただし WS は悪化**: 最大 4.252GB・3.9GB 超が累計約 5 秒・4GB 超が累計約 1.25 秒
   （掃除係②ありでは 3.9GB 超が 1 サンプルのみ）。20〜30s 区間は 4.2GB 前後に張り付き。

### 結論＝「保険」と「掃除」の役割分担（併用で確定）

- **ハードリミット＝保険**: マネージドコミットの絶対上限を確保パスで保証（OOM 的肥大の阻止）。
  ただしランタイム自発の gen2 は OS へのページ返却まではしないため **WS は下げない**。
- **掃除係②＝WS 削減の実働**: Aggressive GC（返却込み）が WS ピークを実際に削る。こちらは
  ベストエフォートで保証はない。
- 片方がもう片方を置換できないことがログ 2 本で確定。**併用で確定**し、切り分けに使った
  `BlockingGcThresholdMB` は既定 512 へ戻す（ユーザー操作）。
- 申し送り: 唯一の鋭い縁は「GC しても収まらない確保＝OOM クラッシュ」。現状は実測ピークに対し
  余裕 0.47GB。キャッシュ予算・同時デコード数の既定を変える変更では必ず上限値も見直すこと
  （→同日、この見直しを設定画面のクランプで機械化＝次節）。

### 設定画面からの変更＝GC.RefreshMemoryLimit（2026-08-06）

ハードリミットを設定＞高度な設定から変更可能にした。鍵は **.NET 8+ の
`GC.RefreshMemoryLimit()`**（コンテナのメモリ上限変更追従用の API）:
`AppContext.SetData("GCHeapHardLimit", (ulong)bytes)` → `GC.RefreshMemoryLimit()` で
**実行中に上限を変更できる**＝再起動不要。最小コンソール（net10.0・csproj 3.5GiB 起動）で事前検証:

- 上げ方向（3.5→5GiB）は即時有効。**旧上限を超える ~3.9GiB の確保が実際に通る**ことまで確認。
- 下げ方向は現在のヒープコミットが新上限を超えていると `InvalidOperationException`。
  解放＋フル GC 後なら成功し、**下げた上限は実際に OOM で強制される**（2GiB 下で 3GiB 確保→OOM）。

実装（コード作成は Sonnet エージェントへ委譲・設計/レビューは本体。テスト計 226 件緑）:

- **`HeapHardLimitPolicy`**（新規・純ロジック・リンク参照でテスト 6 件）: `ClampGB` ＝
  `max(入力, キャッシュ予算+1GB, 絶対下限 2GB)`。上限が「予算＋デコード中バッファ＋サムネイル常駐」を
  下回ると OOM クラッシュ必至のため、**設定画面からその組み合わせを作れないよう機械化**
  （旧・csproj コメントの「予算を上げたら上限も見直し」という人間頼みの申し送りを置換）。
  NaN は既定値扱いに正規化（`Math.Max` の NaN 伝播対策）。
- **`MemoryDiagnostics.TryApplyHeapHardLimit`**: 適用に失敗（下げ方向）したらフル GC を 1 回
  打って再試行、それでも失敗なら false＝保存済みの値が次回起動時に効く。
- **`AppSettings.HeapHardLimitGB`**（既定 3.5）＋起動時適用（`App.ApplyLanguageOverride` 内＝
  ヒープがまだ小さく失敗しないタイミング）＋設定ダイアログ NumberBox（2〜64・0.5 刻み。
  クランプ後の値を保存するので開き直すと引き上げ結果が見える）＋resw 日英。
- 既知の限界: 保存時適用は `SettingsChanged`（キャッシュ予算の即時反映）より前に走るため、
  **予算と上限を同時に大きく下げる**とキャッシュ縮小前で失敗し「次回起動時に反映」になる
  （UI の説明文でカバー。上げ方向は常に即時）。

### 実機 3 本の比較＝「上限は大きいほど良い」ではない（2026-08-06）

ユーザーが上限 3.5GB/4GB を実機比較したところ「**4GB の方がカクつく**」という直感に反する
体感が出て、ログでも確定した:

| | 3.5GB・普通連打 | 4GB・速い連打 | 3.5GB・最速連打 |
|---|---|---|---|
| ログ | `…_223111.tsv` | `…_223306.tsv` | `…_224409.tsv` |
| NAV 速度 | 3.1/秒 | 4.8/秒 | **5.6/秒** |
| agr 頻度 | 8.4 秒に 1 回 | **4.9 秒に 1 回** | 6.8 秒に 1 回 |
| 停止 avg / max | 209 / 259ms | 240 / **336ms** | 212 / **235ms** |
| 停止密度 | 2.5% | **4.9%** | 3.1% |
| agr 直前のマネージド | ~2.9–3.1GB | **3.2–3.3GB** | ~2.8–3.0GB |

- **機構**: ハードリミット下の GC は**上限への近さで勤勉さが決まる**。3.5GiB だと常駐 2.5GB に
  対し余裕が薄く、ランタイム自発の背景 gen2 が早め早めに回ってマネージドの天井が ~3.0GB に
  保たれ、掃除係 agr の 1 回あたりの仕事量が小さい。4GiB だと自発 GC が遅れてヒープが
  3.2〜3.3GB まで育ってから agr が発火＝圧縮の仕事量が増えて停止が伸び（max 336ms）、
  発火もクラスタ化（約 1 秒間隔で連発）して体感が悪化。
- 閲覧速度の交絡は 3 本目（**最速の連打なのに停止指標が 3 本中最良**）で棄却。
- → **既定 3.5 据え置きで確定**（＝クランプ下限「予算 2.5＋1GB」ちょうど）。ハードリミットは
  「大きいほど安心」ではなく、**常駐量に対して適度に窮屈な方が滑らか**。予算を変えない限り
  上限も動かさないのが正解。


## メモリデバッグキーの誤爆防止ゲート（2026-08-06）

`Ctrl+M`（強制フル GC）・`Ctrl+Shift+M`（メモリ時系列ログの記録開始/停止）を**メモリオーバーレイ
表示中のみ有効**にした。ユーザー提案（採用時に `Ctrl+M` も対象に含めることで合意）。

- **動機**: 通常操作中の誤爆。`Ctrl+M` は 100〜300ms 級の GC 停止＋オーバーレイが勝手に開き、
  `Ctrl+Shift+M` は気づかないまま TSV 記録が走る。特に後者には「記録開始でオーバーレイは開くが、
  その後 `M` で隠すと ● REC が見えないまま記録が続く」という既存の穴があった。
- **設計**: 「`M`＝デバッグモードの入口。Ctrl 系はその中でのみ有効」のメンタルモデルに統一。
  - オーバーレイ非表示中の `Ctrl+M`/`Ctrl+Shift+M` は何もしない（キーは消費＝`e.Handled` を立てて
    他ハンドラへ漏らさない。将来プレビュー側に M 系の割当が増えても挙動が変わらないように）。
  - 記録中に `M` でオーバーレイを隠すと**記録も同時に停止**（上記の穴を塞ぐ）。
- **実装**: `MainPage.HandleGlobalKeyDown` の M 分岐 1 箇所に閉じる（`MemoryOverlay.IsShown` で判定。
  停止はタイマーを持つ MainPage 側の `ToggleMemoryLogRecording()` を呼ぶだけ）。記録開始時の
  `EnsureShown()` はゲート下では冗長だが保険として残置。
- **検討して捨てた案**: 確認ダイアログ（計測を汚す・煩わしい）／押しにくいキーへの変更（「気づかず
  記録継続」を解決しない）／設定でデバッグキー ON/OFF（重い。誤爆が残った場合の次の一手として温存）／
  記録の自動停止タイマー（「隠したら停止」で野放し経路がほぼ塞がるため当面不要）。
- 付随更新: `shortcuts.json`（Ctrl 系 2 項目に「オーバーレイ表示中のみ」注記）→ `gen-shortcuts.ps1` で
  SHORTCUTS.md 日英を再生成。


## ハードリミットの無効指定（0）と csproj 初期値の撤去（2026-08-07）

メモリに余裕がある PC では「メモリ使用量の上限＝無効」＋「緊急メモリ回収のしきい値
（`BlockingGcThresholdMB`）＝0」の組み合わせで**ハードリミット導入（2026-08-06）前と同等の状態**に
できるようにしたい、というユーザー要望。`HeapHardLimitGB = 0` を「無効（上限なし）」の特別値にした
（しきい値の「0 で無効」と同じ流儀）。

### 事前検証＝csproj 初期値は不要（最小コンソール・net10.0・実機）

当初案は「無効＝物理メモリ全量を上限として適用」（csproj の 3.5GiB が起動時から効いているため、
無効にするには能動的に引き上げる必要がある）だったが、ユーザーの「csproj の 3.5GiB 設定を除去すると
問題はあるか？」という着眼を受けて 2 点を検証し、**除去して良い＝小細工不要**と確定した:

1. **上限なし起動→実行中の新設**: csproj 未設定のプロセスで `AppContext.SetData("GCHeapHardLimit",
   2GiB)`→`GC.RefreshMemoryLimit()` が成功。`TotalAvailableMemoryBytes` が物理メモリ量（約 93.6GiB）→
   2GiB に変わり、**実際に約 2GB 確保で OOM＝本物として強制される**。
2. **0 指定での実行中撤去**: `SetData("GCHeapHardLimit", 0UL)`→`RefreshMemoryLimit()` で
   `TotalAvailableMemoryBytes` が物理メモリ量へ戻り、旧上限超の 3GB 確保が通る。
   （ドキュメントには撤去可否の明記がなく、実機検証で確定させた）

### 実装（コード作成は Sonnet エージェントへ委譲・設計/レビューは本体。テスト 229 件緑）

- **csproj**: `System.GC.HeapHardLimit` の `RuntimeHostConfigurationOption` を削除
  （`ConserveMemory`=7 は据え置き）。上限は起動時に `AppSettings.HeapHardLimitGB` からのみ適用。
- **`HeapHardLimitPolicy.ClampGB`**: 0 以下は無効として素通しで 0 を返す（負値も 0 に正規化）。
  クランプの目的（上限＜キャッシュ予算の OOM 組み合わせ防止）は上限が無ければ該当しないため。
  NaN→3.5 の正規化は 0 判定より**先**（NaN は無効ではなく想定外入力＝既定値扱い）。テスト 3 件追加。
- **起動時**（`App.ApplyLanguageOverride`）: クランプ結果が 0 なら適用スキップ（csproj にも設定が
  無いので素の GC のまま）。**設定保存時**（`PhotoStatusBar`）: 0 でも無条件で
  `TryApplyHeapHardLimit` を呼ぶ＝実行中の有効→無効切替（撤去適用）が成立。
  `TryApplyHeapHardLimit` 自体はコード変更なし（`ToBytes(0)`=0UL の SetData がそのまま撤去になる）。
- **設定 UI**: NumberBox の Minimum 2→0。説明文（resw 日英＋XAML フォールバック）に
  「0 で無効（上限なし）」を追記。0 と 2 の間の値は従来どおりクランプで引き上げ。

### 除去に伴う挙動変化（許容と判断）

- 起動直後〜設定適用までの短い窓が上限なしになる（適用は起動最初期・ヒープ極小のため実害なし）。
- 起動時適用が万一失敗した場合の csproj フォールバックが消える（起動時は新設方向＝検証 1 のとおり
  失敗要因が実質なく、リスク小と判断）。
- settings.json が無い初回起動でも既定 3.5 が適用されるため**既定の挙動は不変**。

申し送り: 無効＋しきい値 0 でも掃除係①（プールミス累積の背景 GC）・③（アイドル GC）・
`ConserveMemory`=7・バッファプールは残る（いずれもハードリミットより前からある機構）。
「メモリ対策を全部無効化」まで求める場合は別途検討。

## メモリベンチマークモード（membench）（2026-08-07）

キャッシュ/メモリ設定（`CacheBudgetGB`／`CachePoolRatioPercent`／`BlockingGcThresholdMB`／
`HeapHardLimitGB`）の最適値探索を、手動での「設定変更→アプリ再起動→写真送り連打→
`Ctrl+Shift+M` で記録→目視比較」という手順から解放するため、**Debug ビルド限定の自動ベンチマーク
モード（membench）**をアプリに内蔵し、条件スイープ用ハーネス＋分析スクリプトを
`tools/membench/` に追加した。計測基盤は既存の `MemoryLog`（TSV 時系列ログ）をそのまま再利用する。

### 全体の流れ

1. `tools/membench/Run-MemBench.ps1` が `conditions.json` の条件ごとに Debug 設定フォルダの
   `settings.json` へキャッシュ/メモリ 4 項目を書き込み、`membench.json`（トリガーファイル）を
   同フォルダへ置いて `dotnet run --no-build` を起動する。
2. アプリ側（`MainPage.MainPage_Loaded`。Debug 限定）が起動時に `MemBenchConfig.TryConsume` で
   `membench.json` を読み取り（**消費式**＝読み取り次第すぐ削除）、見つかれば通常のセッション復元
   （`RestoreSessionAsync`）の代わりに `RunMemBenchAsync` を実行する。
3. `RunMemBenchAsync` は指定フォルダを読み込み（`restoreSelectedFile` に `startFile` を渡して
   任意のファイルから焦点を開始できる）、落ち着き待ち（`settleMs`）の後にメモリログ記録を開始し、
   `steps`（`sweep`＝写真送り連打／`idle`＝無操作待機）を順に実行、記録を停止してログを
   `outputPath` へコピーし、アプリを終了する（`Application.Current.Exit()`）。
4. `Run-MemBench.ps1` は `dotnet run --no-build` の終了を待って次の条件へ進む（`dotnet run` は
   `Microsoft.Windows.SDK.BuildTools.WinApp` 経由でパッケージ起動し、アプリが自発的に終了するまで
   ブロックする＝`winapp run` の既定動作と同じ。`BuildAndRun.ps1` スキルの async 推奨の注記が
   これの裏付け）。
5. 全条件終了後、`tools/membench/Analyze-MemBench.ps1` が `results/*.tsv` を集計し
   `results/summary.md` へ比較表（WS ピーク／sweep 中 WS 平均／終了時の床／カクつき／GC 内訳／
   デコード統計／プールヒット率／DISCARD 合計）を書き出す。

### membench.json スキーマ

設定フォルダ（`AppSettings.SettingsFolder`＝`settings.json` と同じフォルダ）直下に置く。

```json
{
  "folderPath": "D:\\Users\\kobat\\tmp_ClaudeCode用\\岩国_100MSDCF",
  "tag": "baseline",
  "startFile": "DSC03220.JPG",
  "outputPath": "C:\\...\\tools\\membench\\results\\baseline.tsv",
  "settleMs": 5000,
  "steps": [
    { "kind": "sweep", "durationMs": 60000, "intervalMs": 120 },
    { "kind": "idle",  "durationMs": 30000 }
  ]
}
```

- `startFile`（任意）: 開始時に焦点を当てるファイル名（フォルダ相対）。
  `LoadFolderAsync` の `restoreSelectedFile` へそのまま渡す。null/空なら先頭写真から開始する。
  既定のハーネス設定はユーザーが実機でメモリ増加傾向を確認した実データ・実手順
  （`岩国_100MSDCF` フォルダの `DSC03220.JPG` から右送りで `DSC03290`〜`03310` 付近を通過）に合わせて
  `-FolderPath`/`-StartFile` の既定値をそれへ変更してある（旧既定の `20260228` フォルダ・先頭開始より
  再現性が高いとの判断）。
- `steps[].kind`＝`"sweep"`（`ViewModel.MoveNext`/`MovePrevious`＝矢印キーと同じ実処理を
  `intervalMs` 間隔で呼ぶ。端に着いて `FocusedPhoto` が動かなくなったら方向反転して折り返す）または
  `"idle"`（`durationMs` 待機するだけ）。

### アプリ側の実装

- `src/PhotoQuickSelector.App/MemBench.cs`（新規・全体を `#if DEBUG` で囲む）: `MemBenchStep`・
  `MemBenchConfig`（`System.Text.Json`・`PropertyNameCaseInsensitive=true`。トリミング無効
  ＝`PublishTrimmed=false` なのでリフレクション経由の Deserialize で問題ない）・
  静的 `TryConsume(settingsFolder)`。パース失敗時は `membench.error.txt` へ例外を書いて null を返し
  通常起動を継続させる（ハーネスの設定ミスでアプリが起動不能にならないように）。
- `MainPage.xaml.cs`: `MainPage_Loaded` で `RestoreSessionAsync` の前に `MemBenchConfig.TryConsume`
  を試す（Debug 限定）。ヒットしたら `RunMemBenchAsync` へ分岐し、通常のセッション復元は呼ばない。
  `RunMemBenchAsync`/`RunMemBenchSweepAsync` も `#if DEBUG` ブロック内に追加。例外発生時も
  `membench.error.txt` へ書いてから必ず `Application.Current.Exit()` する
  （ハーネスを無限待ちにしないため。8 分タイムアウト＋プロセス Kill はハーネス側の保険）。
- `MemoryLog` 記録の開始/停止を `ToggleMemoryLogRecording()` から `StartMemoryLogRecording()`/
  `StopMemoryLogRecording()` の 2 ヘルパーへ分割（既存の `Ctrl+Shift+M` トグルは 2 つを呼ぶだけに
  リファクタ）。`RunMemBenchAsync` は `StartMemoryLogRecording()`/`StopMemoryLogRecording()` を
  直接呼ぶ（オーバーレイの `EnsureShown()`・250ms サンプリングタイマーの挙動は変えない）。
- **セッション保存の抑止**: `MainViewModel.SuppressSessionCapture`（既定 false）を追加し、
  `CaptureSession()` の先頭で true なら即 return。`RunMemBenchAsync` は開始時に立てる。
  目的＝ベンチ実行がユーザーの「前回開いていたフォルダ」設定（`AppSettings.LastSession`）を
  ベンチ用フォルダで上書きしないため。Debug 専用の用途だが `#if` を散らかすほどでもないため
  常設プロパティにしてある。
- `AppSettings.SettingsFolder`（新規・internal static）: `settings.json` の親フォルダを返す。
  既存の `LogsFolder`（`logs\` 配下）はこれを使うようリファクタ（DRY）。`MemBench.TryConsume` の
  探索元にも使う。

### ハーネス（`tools/membench/`）

- `conditions.json`: 1 因子ずつのスイープ（既定は現行既定 `2.5GB/20%/512MB/3.5GiB` を基準に
  budget・pool・thr・hhl を 1 項目ずつ振る。`budget3.5` は `HeapHardLimitGB` を道連れで 4.5 にする
  ＝`HeapHardLimitPolicy` のクランプ「予算＋1GB」対策。`baseline2` は再現性確認用）。
- `Run-MemBench.ps1`: 残骸プロセス掃除→ Debug パッケージの設定フォルダを `Package.appxmanifest`
  の Identity Name（GUID）から `Get-AppxPackage` で解決（失敗時は `%LOCALAPPDATA%\Packages` の
  glob フォールバック）→ `settings.json` をバックアップ（既存バックアップは上書きしない）→
  `dotnet build` → 条件ごとに `settings.json` の該当 4 項目だけ上書き＋`membench.json` 生成→
  `dotnet run --no-build` を同期実行（8 分タイムアウト＋強制終了の保険）→ 結果 tsv の有無で
  OK/NG 判定。終了時（`finally`）に `settings.json` を必ず復元する。
  Windows PowerShell 5.1 構文限定（`&&`/`??`/三項演算子は不使用）。
- `Analyze-MemBench.ps1`: `results/*.tsv` を集計し `results/summary.md` へ Markdown 表を出力
  （WS ピーク／sweep 中 WS 平均／終了時の床／カクつき回数・最大ギャップ／GC kind 別回数・
  最大/合計停止時間／DECODE 件数・平均／プールヒット率／DISCARD 合計）。条件（tag/budget/pool/
  thr/hhl）は NOTE `bench` 行（`RunMemBenchAsync` が書く `tag=... budget=... pool=... thr=... hhl=...`
  形式）から読み取る。
- **PS 5.1 の `Sort-Object` はタイ・ブレークで挿入順を保証しない**（実機で確認済み＝
  同一 `ElapsedMs` の `NOTE` 行が並べ替え後に入れ替わり、sweep 区間の境界計算が壊れるバグを
  Analyze-MemBench.ps1 の開発時に実際に踏んだ）。`MemoryLog` の TSV は各記録メソッドが
  `ConcurrentQueue` を FIFO で drain して書くため、**ファイル出現順＝発生順はそのまま信頼できる**
  （`bench`/`bench-step`/`bench-end` はいずれも `RunMemBenchAsync` の単一スレッド `await` チェーンから
  逐次発生する）。そのため `Analyze-MemBench.ps1` は SAMPLE/NOTE の並べ替えを行わず、
  ファイル出現順をそのまま区間境界・時系列として使う。
- 両スクリプトとも UTF-8 BOM 付きで保存する必要がある（PowerShell 5.1 はスクリプトファイルに
  BOM が無いとシステムコードページ〔日本語 Windows では CP932〕でソースを読み、日本語コメント/
  文字列が文字化けしてパースエラーになる）。同じ理由で、`settings.json`/`membench.json`/TSV
  （いずれも .NET 側は BOM なし UTF-8 で読み書き）を読む `Get-Content` 呼び出しには
  `-Encoding UTF8` を明示している（省略すると日本語フォルダパス等が文字化けする）。

### 実行方法

```powershell
cd tools\membench
.\Run-MemBench.ps1                              # conditions.json の全件
.\Run-MemBench.ps1 -Tags baseline,budget1.5      # 一部のタグのみ
.\Analyze-MemBench.ps1                           # results\*.tsv を集計して summary.md を生成
```

### 初回スイープの実測結果（2026-08-07・同日実施）

実装同日に 3 ラウンド計 20 ラン実施（台本＝sweep60s→idle30s→sweep60s→idle25s・interval 120ms・
`岩国_100MSDCF` の `DSC03220.JPG` 起点。カクつき閾値は実測に合わせ 400→350ms に調整＝通常時の
SAMPLE ギャップは 300ms 未満で、~250ms の GC 停止でもギャップは ~324ms 程度にしか伸びないため）。
生データと比較表は `tools/membench/results/`（gitignore 対象・ローカルのみ）。
測定ノイズは baseline×2 の差＝WS ピーク ±80MB・agr 合計 ±190ms 程度。

- **支配的因子はプール比率**（`CachePoolRatioPercent`）で、10→50% で単調に改善:
  agr（ブロッキング gen2）24回/計4.9s → 6回/計0.6〜0.8s（20%＝現既定）→ 2回/計0.2s（40%）→
  0〜1回/計0.1s 未満（50%）。DISCARD 12.2GB → ~3.3GB → ~1.4GB → 0.7〜0.9GB。
  プールヒット率 72→85→89→92%。**60% はほぼ頭打ち**（DISCARD 241MB・ヒット 93.6%・WS ピークは
  50% と同等かやや上）＝**膝は 40〜50%**。DECODE 件数は全比率で ~250〜320 とほぼ不変＝
  プール拡大でキャッシュ枠が減っても（2.5GB×50%＝キャッシュ 1.25GB）このシナリオでは
  再デコード増は出ない。
  ※「メモリ掃除係」節の「サイズ混在では完全一致プールの拡大は構造的に効かない」という当時の推定は
  **20% 止まりの範囲でのみ妥当**だった。40〜50%（1.0〜1.25GB）まで広げると 190MB/80MB の
  2 サイズ級を同時に複数本ずつ抱えられるようになり、相互追い出しが実測で解消する。
- **予算**（`CacheBudgetGB`）: 1.5GB は WS −700MB だが agr 28回/計4.3s・カクつき 8 回と大幅悪化。
  ただし予算スイープはプール比 20% 固定＝プール絶対量も 0.3GB へ縮む交絡があり、悪化の主因は
  プール縮小側の可能性が高い（budget2.0×pool40〜50 が良好なことが傍証）。3.5GB はカクつき 0 だが
  常時 +1GB で伸びしろ小＝「上限は大きいほど良いではない」の既知見と整合。
- **掃除係閾値**（`BlockingGcThresholdMB`）: 256 でピーク −190MB（agr 8回/計0.75s）、0/1024 で
  agr 消滅（ピーク +260MB）＝ピーク ~300MB ↔ 停止 ~0.7s のトレード。**プール比 40〜50% では
  そもそも agr がほぼ発火しなくなる**ので、どの閾値でも実質差なし＝512 のまま保険として維持で良い。
- **ハードリミット**（`HeapHardLimitGB`）: 0/3.5/4.5 いずれも差はノイズ内＝このシナリオでは
  発動せず。「マネージドコミットの保険」という役割どおり。
- **有望構成**: `2.5GB/50%/512/3.5`（WS ピーク 3.75〜3.84GB・カクつき 0・agr ≦1回。pool50×2 本で
  再現確認済み）／省メモリなら `2.0GB/40〜50%/512/3.5`（sweep 中平均 2.9GB・カクつき 0）。
  **既定値（20%）の変更は未実施**＝一方向スイープ主体の台本のため、行き来の多い実操作での
  キャッシュ縮小影響（2.5GB×50% だと 190MB 級 ~6 枚分）は未検証。既定変更するならその検証後。

### ナビ間隔の是正（120ms→500ms）とサイズばらつき 3 水準の検証（2026-08-08）

初回スイープの interval 120ms は**描画スキップを起こしていた**（ユーザーが目視で指摘。ログでも
NAV 940 に対し DECODE 274＝レート制限 2 枚/秒に張り付き）。原因＝120ms は `LoadSettleDelay`
（150ms）未満で settle→先読みが一度も走らず、即デコード経路（`RateBudget`3/`RateWindowMs`1500）で
間引かれるため。**逆算での是正値＝500ms**（持続デコード上限 3÷1500ms＝2 枚/秒の逆数。かつ 150ms 超
なので毎ナビ後に settle→先読みが走り、次のナビはキャッシュ済み即表示になる）。ハーネスの
`-NavIntervalMs` パラメータ化（既定 500）。実測で **NAV＝DECODE が完全一致**（230/230 等）し
スキップ解消を確認。旧 120ms の結果 TSV は `results/pace120/` へ退避（デコードレートは両ペースとも
~2 枚/秒で頭打ちだったため結論は変わらなかったが、混同を避けるため分離）。

併せて「プール比率の効果は画像サイズのばらつき依存」（ユーザー指摘）を検証するため、
`New-MemBenchTestData.ps1`（System.Drawing で合成 JPEG 生成）を追加し、ばらつき 3 水準
×プール比 0/20/50% の 9 ラン実施（いずれも 500ms・予算 2.5GB・thr512・hhl3.5）:

- テストデータ: `membench_uniq`＝60 枚全部異寸法（8640x5760 から 1 枚ごとに幅 −16/高さ −8＝
  完全一致プールが構造的にヒットできない最悪ケース）／`membench_same`＝60 枚全部 8640x5760
  （最良ケース）／実データ `岩国_100MSDCF`＝α1/OM-1 の 2 サイズ混在。
- **プール 0%（無効）は 3 水準すべてで最悪**: agr（ブロッキング gen2）50〜58 回/計 10.6〜14.0 秒・
  カクつき 12〜25 回・DISCARD 31〜35GB。同一サイズのフォルダですら毎デコード 190MB 新規確保で
  掃除係が回りっぱなしになる。
- **2 サイズ混在（実データ）**: 20%→50% で agr 2 回/289ms→**0 回**・DISCARD 3.0GB→241MB・
  WS ピーク 4270→3694MB。旧ペースの結論（膝 40〜50%）を実操作ペースでも確認。
- **全同一サイズ**: 20% で既に完璧（GC 発生ゼロ・DISCARD 0・ヒット 96.6%）。50% も同様で、
  キャッシュ枠が減るぶん **WS ピークはむしろ 2935→2181MB に低下**（常駐デコード済みが減るため）。
- **全異寸法（最悪ケース）**: プールはほぼ無力（ヒット 0〜9%）で 0/20/50% とも agr ~9〜11 秒・
  カクつき 14〜20 回と同水準＝**プールを大きくしても悪化はしない**（死蔵在庫でキャッシュ枠が
  減っても DECODE 件数 +12% 程度・ピークはノイズ範囲）。被害の頭打ちは掃除係（thr512）が担う。
- **結論: プール比 40〜50% はばらつきの全水準で最適〜無害**。「大きすぎると損」のケースは
  実測では存在しなかった。既定 20%→40〜50% への引き上げは安全side（ユーザー判断待ち）。
  ユーザー実機の設定（プール 0%）は全水準最悪の構成なので変更推奨。

### ユーザー指定構成の検証＝「プール0・掃除係無効・ハードリミット頼み」（2026-08-08）

ユーザー指定の「予算 2GB・プール 0%・閾値 0・上限 3.5GiB」を岩国×500ms で実測（`b2p0t0h35`）。
同予算の対照 `b2p50t512h35`（プール 50%・閾値 512）との比較:

| 指標 | b2p0t0h35（指定構成） | b2p50t512h35（対照） |
|---|---|---|
| WS ピーク / sweep 平均 | 4350 / 3580MB | 3304 / 2674MB |
| カクつき | 2 回（max 352ms） | 0 回 |
| GC | bgc 61 回＋gen2 計 101 回 | bgc1＋idle1 のみ |
| DISCARD | 35.4GB | 0.7GB |
| GC コミット最大 | **3503MB＝HHL 3584MB の 98% に張り付き** | 2332MB（余裕大） |

- 指定構成は**カクつきはほぼ出ない**（ブロッキング段オフのため。プール0×閾値512 の
  カクつき 25 回は agr＝掃除係ブロッキング段そのものが源だったと確定）。ゴミの頭打ちは
  ハードリミット由来の GC 圧が実質担い、背景 gen2 が回りっぱなし（101 回/175 秒・捨て 35GB）に
  なることで成立している＝CPU コストと平均 WS ~900MB 高で買う静けさ。コミットが上限の 98% に
  常時張り付くため余裕がなく、より大きい画像や他の確保が重なると
  ブロッキング化・アロケ停止に転びやすい構成でもある。
- 同予算のプール 50% は WS ピーク −1046MB・平均 −906MB・GC 実質ゼロ・カクつき 0 で全面的に優位。

### 未実施・申し送り

- 台本は一方向スイープ主体のまま（500ms 化で毎ナビ描画になったが、短周期の「戻り連打」＝
  数枚範囲の往復でのキャッシュ命中挙動は専用台本が要る。プール比を上げるとキャッシュ枠が
  同額減る点の残リスクはここだけ）。
- 合成 JPEG（membench_uniq/same）は EXIF 無し・デコード 40〜100ms と実写より軽い。
  サイズ起因のアロケーション挙動の比較には十分だが、絶対値は実データと比較しないこと。

### 別 PC（KOBAPOCKET4）での再現検証（2026-08-08）

RAM 23.6GB のノート PC（KOBAPOCKET4）で同一検証を再実行した（合成データは固定シードで同一・
岩国実データは `D:\Users\kobat\Pictures\tmp\ClaudeCode用\岩国_100MSDCF`＝**4000 枚**入りフォルダ。
サイズばらつき 3 水準×プール比 0/20/50% の 9 ラン＋ユーザー指定構成の 2 ラン。結果は
`results/summary-kobapocket4.md`＝gitignore・ローカルのみ）。

- **結論はそのまま再現**: プール 0% は全水準で最悪（agr 計 8.8〜19.2 秒・DISCARD 18〜34GB）、
  20→50% で単調改善し岩国 50% は agr 0 回・カクつき 0・DISCARD 401MB・ヒット 92.3%。
  全同一寸法は 20% で既に完璧＝50% で WS ピークがさらに低下（2801→2030MB）、全異寸法は
  プール無力（ヒット 0〜11%）だが**大きくしても悪化しない**——序列・膝の位置とも前回 PC と
  定性的に完全一致。既定 20%→40〜50% への引き上げ推奨を裏付け。
- **新知見＝フォルダ枚数が HHL の余裕を食う**: 4000 枚フォルダでは常駐分（サムネイル圧縮バイト・
  メタデータ等）の上乗せで、予算 2.5GB の岩国 3 ランすべて GC コミット最大が 3,527〜3,549MB＝
  **HHL 3.5GiB（3,584MB）の 98〜99% に張り付き**（プール比を変えても不変。60 枚の合成データは
  uniq ~3.1GB・same 1.4〜2.9GB で余裕あり）。初回実行では岩国 p0/p50 の 2 本が
  `System.OutOfMemoryException` でクラッシュし、再試行では成功＝確率的に転ぶ際どさ。
  README の「RAM の少ない PC では条件を下げる」とは別軸の注意点で、数千枚フォルダ常用なら
  予算 2.0GB へ下げるか HHL 4.5GiB／0（無効）の検討余地。
- **ユーザー指定構成（`b2p0t0h35`＝プール 0・閾値 0・HHL 頼み）はこの PC では劣化が拡大**:
  メモリ面は前回 PC とほぼ同一（bgc 60 回・DISCARD 34.6GB・コミット HHL の 94% 張り付き）だが、
  前回 PC で「ほぼ出ない」だったカクつきが **22 回（max 552ms）発生**＝ブロッキング段オフでも
  HHL 近傍の背景 gen2 常時回転による CPU 競合・確保停止を、この PC の性能＋4000 枚常駐では
  吸収しきれない。デコード平均も 348ms vs 313ms と GC 圧分の悪化。同予算の対照
  `b2p50t512h35` は WS ピーク −1,103MB・GC 実質ゼロ・カクつき 1 回で全面優位＝
  前回 PC と同方向で差はむしろ拡大。
