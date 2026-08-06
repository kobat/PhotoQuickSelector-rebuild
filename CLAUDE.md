# CLAUDE.md — PhotoQuickSelector-rebuild

写真を高速に閲覧・選別する Windows デスクトップアプリ（旧 `PhotoQuickSelector` の作り直し）。
詳細仕様は [SPEC.md](SPEC.md) を参照。本ファイルは作業の引き継ぎ用メモ。
過去の実装経緯・設計判断の詳細は [docs/HISTORY.md](docs/HISTORY.md) を参照（既存機能の修正前に該当節を読むこと）。

## 技術スタック / 構成
- WinUI 3 / .NET（App は `net10.0-windows`、Core は `net8.0`）/ Windows App SDK / CommunityToolkit.Mvvm
- EXIF 解析: 公式 NuGet `MetadataExtractor`（フォーク不使用）
- 永続化: `System.Data.SQLite.Core`（フォルダごとに `PhotoQuickSelector.sqlite3`。ファイル名は
  `MetadataStore` の ctor 引数で注入。既定＝`MetadataStore.DefaultDatabaseFileName`／App は
  `AppSettings.DatabaseFileName` を渡す＝Debug は別名。「既知の注意点」参照）
- 構成:
  - `src/PhotoQuickSelector.Core/` … UI 非依存（メタデータ抽出・評価モデル・SQLite 永続化）
  - `src/PhotoQuickSelector.App/` … WinUI アプリ（左右分割UI・サムネイル・キー操作）
  - `tests/PhotoQuickSelector.Core.Tests/` … xUnit（150 件）

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
- ただし Debug ビルドの書き込み先は `PhotoQuickSelector.Dev.sqlite3` なので、旧アプリ由来の
  `PhotoQuickSelector.sqlite3` は開発中の操作では汚れない（＝旧DB互換の確認をしたい時は
  手動でコピー＆リネームして使う）。

## 主要な決定事項
- **配布形態 = 素の自己完結 EXE（unpackaged）**。.NET/WinAppSDK 同梱。ただし開発は packaged、
  unpackaged 単一ファイル発行は **publish 時の構成**として後で組み込む（SPEC §0/配布節）。
- UI は旧「2モード切替」を廃止し**左右分割の単一画面**（左=フォルダツリー、右=閲覧）。
- 「同等」のゴール = 機能同等＋既知バグ改善（紫ラベルにキー割当、ハードコードパス排除、UTF-8 等）。
- ソースは UTF-8。コミットは原則ユーザー依頼時。コミット末尾に Co-Authored-By を付与。
- **バージョン表記**（2026-07-31）: 開発中は csproj `<Version>` を `0.2.2-dev` のようにプレリリース表記にし、
  **リリースのコミットで `-dev` を外す**（`Package.appxmanifest` の `Version` 上げと同じコミットで行う。
  こちらは MSIX 仕様で数値4組のみ＝英字不可）。`-dev` が載るのは `AssemblyInformationalVersion` だけで、
  バージョン情報ダイアログはこの属性を読む。詳細は HISTORY.md「開発中バージョンのプレリリース表記」節。
- **コードコメントの方針**（2026-07-07 整理・基準ユーザー合意）: コードには「制約・why・単位・座標系・
  落とし穴」を書く。**public メンバーの `<summary>`（自明でも）と処理の区分ラベル的な短いコメントは残す**。
  実装経緯（案番号・旧機構との比較・実測値詳細）は HISTORY.md へ書き、コードには残さない。

## 現在の状態（要約）

**Phase 1〜4 すべて完了**。旧アプリの機能同等＋既知バグ改善を達成し、**v0.2.1** として公開向け発行済み
（v0.1.0＝2026-07-02 初回公開／v0.2.0＝2026-07-12。以降の画像情報パネル〔File→評価→EXIF・更新日時・XMP展開〕／
評価のリセット／グリッド空状態表示／シャッター速度表示修正／評価データのスキーマ v2 等をまとめて v0.2.1＝2026-07-30）。
`dotnet test` **229 件緑**（Core＋リンク参照の `PreviewViewport`/`DecodeGate`/`PixelBufferPool`/`MemoryJanitor`/
`MemoryLog`/`HeapHardLimitPolicy`）。

- **Core**: メタデータ抽出（EXIF／AF点／GPS／LensMake。全タグダンプは File グループを先頭へ並べ替え）・
  SQLite 永続化（旧DB互換・遅延作成＋作成確認・評価の更新時刻記録＝スキーマ v2。保存は適用後の時刻を
  返し〔`RETURNING`〕App が `EvaluationTimestamps` としてメモリ常駐。リセットは行削除＝
  `ClearEvaluation`）・
  フィルタ・Reject移動・リネームコピー・ファイル移動（いずれも純関数＋xUnit。旧 `ClipboardExport` は廃止）
- **App（主要機能）**: 左右分割UI（フォルダツリー／お気に入り／最近。左ペイン下部の「読み込み」行右端のピンボタンで
  ピン留め解除するとフライアウト表示＝`AppSettings.LeftPanePinned`。Esc/外側クリック/読み込みで閉）／サムネイルグリッド／プレビュー
  （Win2D 3面構成＝メイン＋ルーペ＋ナビゲーター・フィルムストリップ・AF枠・構図グリッド・DPI考慮の
  段ズーム・ズーム状態維持・画像情報パネル＝右パネル上段をルーペと `E`/タブで切替）／評価編集（単一＋複数選択の一括評価）／
  右クリックメニュー（グリッド／フィルムストリップ共通・エクスプローラ流儀の対象確定）／フィルタ＋ファイル操作
  （名前/実体コピー・ファイル移動・Reject移動＝いずれも bat 確認式。フィルタボタンは絞り込み状態を文字色で表示＝
  白/アクセント/注意色）／グリッドの空状態表示（フォルダ未選択・JPEGなし・絞り込み0件をアイコン＋1行で明示）／外部連携／
  セッション復元／全画面・イマーシブ・完全全画面（Shift+F）／Dark テーマ／日英ローカライズ
  （resw＋shortcuts.json SSOT・F1 チートシート）／設定ダイアログ（一般／高度な設定の 2 タブ）
- **パフォーマンス**: サムネイル＝圧縮バイト常駐＋可視分デコード（容量固定 LRU）／プレビュー先読み
  キャッシュ＝BGRA8 `byte[]`（PixelFrame）保持・LRU（LastUse 単独）＋容量予算・DecodeGate（grant 時の窓分類
  優先度選択＝フォーカス→選択窓→位置窓）・レート制限・sRGB 色管理スキップ・解凍爆弾ガード（宣言寸法 1GB／
  実ファイル 512MB 超はデコードせずスキップ）・**デコードは WIC の COM 直接呼び出し**
  （`WicPixelDecoder`＝全ネイティブ確定解放・GC 非依存・WinRT 経路比約 2 倍速。2026-08-03）／
  ナビゲーター縮小ビットマップキャッシュ／
  **バッファ再利用プール**（`PixelBufferPool`。予算＝キャッシュ＋プールの合計で、内訳は
  `AppSettings.CachePoolRatioPercent`。既定＝予算 2.5GB・プール 20%＝キャッシュ 2GB／プール 0.5GB。
  200MB 級 `byte[]` の毎回確保をなくしてマネージドヒープ肥大＝WS 過大を解消。
  詳細は HISTORY.md「先読みキャッシュのバッファプール化」節）／
  **GC 設定 `System.GC.ConserveMemory`=7**（csproj の `RuntimeHostConfigurationOption`。プールで確保を減らした
  うえで、GC に圧縮・OS へのセグメント返却を促して WS の高止まりを解消。値は実機比較で選定＝5 は効果不足・
  9 は停止が体感。詳細は HISTORY.md「GC の `System.GC.ConserveMemory` 設定」節）／
  **マネージドヒープのハードリミット `System.GC.HeapHardLimit`＝既定 3.5GiB**（`AppSettings.HeapHardLimitGB`。
  2026-08-06。上限接近でランタイムがブロッキング圧縮 GC を強制＝
  マネージドコミットの絶対上限・OOM 的肥大の保険。自発 gen2 は OS への返却をしないため
  **WS は下げない＝掃除係の置換ではなく併用**。設定＞高度な設定から変更可＝`GC.RefreshMemoryLimit` で
  起動時＋保存時に適用〔`MemoryDiagnostics.TryApplyHeapHardLimit`〕。
  **`0` で無効＝上限なし**（2026-08-07。メモリ潤沢な PC 向け。csproj の初期値は撤去＝上限なし起動→
  実行中新設も、0 指定での実行中撤去も `RefreshMemoryLimit` で可能と最小コンソール検証済み。
  掃除係の `BlockingGcThresholdMB`=0 と組み合わせるとハードリミット導入前と同等になる）。
  **下限クランプ「キャッシュ予算＋1GB・絶対下限 2GB」＝`HeapHardLimitPolicy`**（0 以下は無効として素通し）で
  OOM 必至の組み合わせを設定画面から作れない。実測 3 本比較で**上限は大きいほど良いではなく逆**＝
  窮屈な方が自発 GC が勤勉になり掃除係 agr の停止が短く少ない〔3.5GiB: max 235ms／4GiB: max 336ms・
  停止密度ほぼ倍〕→既定 3.5 据え置き。詳細は HISTORY.md「マネージドヒープのハードリミット」節）／
  **メモリ計測の強化**（`Ctrl+M` を `GCCollectionMode.Aggressive` 化＋`M` オーバーレイに「ネイティブ」行。
  これで測ったところ **WS 肥大の主因は LOH ではなくネイティブ**＝確定解放できない `BitmapDecoder` 等が
  ファイナライザ待ちで抱える分と判明〔GC 1 回でネイティブ −1,030MB／マネージドは −182MB のみ〕。
  詳細は HISTORY.md「メモリ肥大の原因調査」節。※後日この「ネイティブ」は算式の見かけ込みと判明し
  2026-08-04 に補正＝オーバーレイの「ネイティブ (GC直後が正)」行）／
  **メモリ掃除係**（`Controls/MemoryJanitor`＝2026-08-04・3 段構成化 2026-08-05〜06。①プールミスで
  捨てた累積 512MB で背景 gen2 GC ②回収待ち概算＝**ゴミ**〔`GetTotalMemory` − キャッシュ・プール・
  デコード中貸出＝`PreviewBitmapCache.ResidentBytes`〕＋**在庫**〔直近 GC の `TotalCommittedBytes` −
  `GetTotalMemory`＝回収済み・OS 未返却〕の合計が閾値〔`AppSettings.BlockingGcThresholdMB`＝既定 512・
  0 で無効・設定＞高度な設定〕を超えたら＝背景 GC の速度負け時のみ**ブロッキング gen2＝常に
  Aggressive**〔OS への返却込み・kind=`agr`・停止 ~140-300ms。再武装ガード＝捨てバイト閾値半分・
  Tick 5 秒周期でも判定。素の Forced 段は返却予定ページが計測に写らない盲点があり廃止〕③最終操作から
  20 秒のアイドルで `Ctrl+M` 相当の完全 GC〔結果はオーバーレイに「アイドル GC」表示〕。連打中は
  「予算＋閾値」水準で頭打ちに・アイドルで基準値へ戻す。※当初の gen2 完了カウント判定は実機で不発火と
  判明し実測判定へ作り替え。プール割合の既定は据え置き〔サイズ混在では完全一致プールの拡大は構造的に
  効かないため〕。詳細は HISTORY.md「メモリ掃除係」「メモリ掃除係のブロッキング段」節）／
  **メモリ時系列ログ**（`MemoryLog`＝2026-08-05。`Ctrl+Shift+M` で操作イベント＋250ms周期のメモリ
  サンプルを TSV へ記録〔設定フォルダ配下 `logs\`〕。オーバーレイの目視・スクショに代わり推移と操作の
  因果を後から機械的に追える。詳細は HISTORY.md「メモリ時系列ログ」節）
- **配布**: unpackaged 自己完結 EXE（フォルダ／単一ファイルの pubxml 2 系統）・LICENSE／
  THIRD-PARTY-NOTICES 同梱・アプリアイコン・README（日英）。
  **GitHub Release へ添付する既定は単一ファイル版**（`win-x64-singlefile`＝exe＋LICENSE＋THIRD-PARTY のみ・
  `.pdb` は除外。v0.2.0 で確定。2026-07-12）

**実機確認が未了の項目**:
- 先読みキャッシュの解凍爆弾ガード（2026-07-07。巨大宣言寸法のテスト画像での再現確認は
  未実施＝作れば `C` オーバーレイで観察可能。通常画像への無影響はビルド＋テスト 103 件緑で確認済み）。
（メモリ掃除係のブロッキング段〔常時 Aggressive 確定版〕は実機確認⑤で 2026-08-06 に**合格**＝
4 確認ポイントとも良好〔WS 3.3〜3.9GB 中心・滞留再発なし・停止 116〜269ms・静穏時過発火なし〕。
同日 `System.GC.HeapHardLimit`=3.5GiB を導入し、掃除係②無効での切り分けログにより
「ハードリミット＝マネージドコミットの保険／掃除係＝WS 削減の実働」の役割分担を確定＝併用。
HISTORY.md「メモリ掃除係のブロッキング段」「マネージドヒープのハードリミット」節。）
（案 K の実機確認⑤は 2026-08-04 に**合格**。連打中に残った「ネイティブ」行の積み上がりは
案 K の取りこぼしではなく**プールミス時の LOH 変動＋オーバーレイ算式の見かけ**と切り分けで確定＝
HISTORY.md「実機確認⑤と『ネイティブ』積み上がりの正体」節。対策＝メモリ掃除係〔同日実装〕。
案 K 自体の未検証項目（非 sRGB 色変換・ミラー系 orientation）は据え置き＝HISTORY.md「対策③＝案 K」節。）
（バッファ再利用プール〔2026-07-31〕はユーザーが実機確認済み。ただし単体ではまだ WS が多めだったため、
GC 設定 `System.GC.ConserveMemory`=7 を併用〔2026-08-01〕。さらに計測の結果 WS 肥大の主因は LOH ではなく
ネイティブと判明〔同日〕。）

**次の候補（未着手）**:
- **使っていない間のキャッシュ解放**（`PreviewBitmapCache.Clear()` は実装済みだが**呼び出し元が無い**。
  プレビュー退出・フォルダ切替・ウィンドウ非アクティブでも 2GB が常駐し続け、別フォルダへ移っても
  前フォルダのぶんが残る。`Clear()` はプールも空にする実装済みなので、呼ぶ箇所を決めるだけ。
  バッファプール化〔2026-07-31〕と対で「使わないときは軽い」が完成する）。
- **評価のエクスポート／インポート**（更新時刻の記録＝スキーマ v2 は 2026-07-30 に実装済み＝下準備完了。
  インポート時の競合解決は「項目単位で更新時刻の新しい方を採る／更新時刻 NULL は上書き可」。
  着手前に HISTORY.md「評価データの更新時刻の記録」節の**フェーズ B への申し送り**を読むこと。
  加えて更新時刻は App 側でメモリ常駐（`PhotoItemViewModel.EvalTimestamps`）になったので、
  一括で DB を書く実装はメモリ側も更新するかフォルダ再読込が必要＝HISTORY「画像情報パネル」節。
  **評価のリセットは行削除＝tombstone が残らない**ので、リセット済みの写真は古いエクスポートの
  取り込みで評価が復活する＝許容済み）。
- 縦型画像の未回転保持＋描画時 GPU 回転（縦のデコード +80〜90ms 解消。大工事のため後回し。
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
- ステータスバー右端の**ハンバーガーメニュー**（`&#xE700;`）から上記トグル/外部連携/設定をクリック実行も可（ショートカット併記）。
  イマーシブ表示はプレビュー▶の外＝トップ項目（グリッド表示中でもクリック可＝プレビューへ入ってON）。プレビュー▶配下に
  画像情報パネル（トグル）と構図グリッド（種類4択/基準2択ともラジオ選択。旧・巡回式2項目は廃止）を集約。
- **右クリック**（グリッド／フィルムストリップ共通・`PhotoContextMenu`）: 評価（フラグ/レーティング/カラーラベル。
  並びは旗→★→カラーで全表示箇所と統一。ショートカット表示も付す）・ファイル名をコピー（「ファイル名のみ／フルパス」×
  「表示中のみ／関連ファイルも」の4択。旧「パスをコピー」を改称・拡張）・ファイルをコピー（表示中のみ/同名別拡張子も＝
  エクスプローラ貼り付けでファイルコピー／配下にリネームしてコピー）・外部連携・すべて選択。
  **評価をリセット**（評価3サブメニューの直下。DB の行ごと削除＝未評価へ戻す＝レーティングは EXIF 値へ復帰。
  キー割当なし・10枚以上は確認ダイアログ。メイン画像の右クリックにも同項目。詳細は HISTORY.md「評価のリセット」節）・
  **単一対象時は評価サブメニューに現在値をチェック表示**（フラグ/レーティングは排他ラジオ、カラーは色ごとトグル。
  複数対象時は従来どおりチェックなし）。カラーラベルには全解除の「クリア」を追加（`PhotoItemViewModel.ClearColorLabels`）。
  **対象確定はエクスプローラ流儀**（集合メンバー右クリック＝集合全体／
  集合外右クリック＝その1枚を選び直して単独対象）。複数選択時は集合対象の項目に「(全選択ファイル)」を付す。
  Reject 移動は誤操作懸念のため右クリックには入れていない（フィルタバー側のみ）。10枚以上の一括対象では
  「既定のアプリで開く／共有／ファイルをコピー」に確認ダイアログを挟む。詳細は HISTORY.md「右クリックコンテキストメニュー」
  「メニュー統一（ハンバーガー/右クリック×2）」節
- `F11` フルスクリーン表示トグル（ステータスバー右端の全画面ボタンも同じ）/ `Esc` 全画面中なら通常表示へ復帰
  （全画面でない通常時の `Esc` は無反応＝プレビューを抜けない。プレビュー終了はフィルムストリップのダブルクリック）
- プレビュー中（マウス）: メイン大画面の**シングルクリック**＝フィット⇄ズーム切替（`Z` と同一倍率）/ **ダブルクリック**＝100%
  （`Shift+Z` と同一）。いずれもズーム中心は**クリック位置基準**（ホイールと同様）。ドラッグ＝パン（**左ボタン限定**）。
  **メイン画像の右クリック**＝プレビュー専用メニュー（`PreviewControl.ContextMenu`。**対象は常に焦点の1枚**＝選択集合は
  不問・不変。ズーム〔フィット/100%/縮小/拡大/倍率を指定＝設定のズーム段。中心は右クリック位置〕・イマーシブ/全画面/
  完全全画面・画像情報パネル・情報オーバーレイ・構図グリッド・フラグ/レーティング/カラーラベル・ファイル系。
  詳細は HISTORY.md「メイン画像の右クリックメニュー」節）。
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
- プレビュー中: `I` 情報オーバーレイの種類を巡回（評価バッジ→詳細情報→オフ。`AppSettings.OverlayKind` に永続化）/
  `Shift+I` 表示タイミング切替（常時⇄切替時のみ＝写真切替・焦点写真の評価変更・プレビュー入場のたびに保持時間だけ表示→
  フェードアウト）。**タイミングと保持/フェード時間は種類（評価バッジ／詳細情報）ごとに独立して記憶**し、
  Shift+I／メニューは**選択中の種類**に作用する（オフ時は無操作・メニューは無効化）。既定＝評価バッジ:切替時のみ・
  保持0ms/フェード400ms（`AppSettings.BadgeTransient`/`BadgeHoldMs`/`BadgeFadeMs`）、詳細情報:常時・保持1000ms/
  フェード400ms（`AppSettings.FullTransient`/`FullHoldMs`/`FullFadeMs`）。保持/フェードは設定＞一般で種類別に ms 指定 /
  `C` 先読みキャッシュ一覧オーバーレイ（デバッグ・初期非表示）
- `M` メモリ使用量オーバーレイの切替 / `Ctrl+M` 強制フル GC / `Ctrl+Shift+M` メモリ時系列ログの記録
  開始/停止（いずれも両モード共通。`Controls/MemoryOverlay`＋`MemoryDiagnostics`。GC は `Forced`→
  ファイナライザ待ち→`Aggressive` の 2 段＋LOH `CompactOnce`。**Ctrl 系はオーバーレイ表示中のみ有効**＝
  誤爆防止ゲート・非表示中は何もしない。記録中に `M` で隠すと記録も停止＝「見えないまま記録」を作らない。
  2026-08-06）。
  オーバーレイは MainPage 右下・500ms 周期の自己更新（キャッシュ一覧とは分離。理由は
  HISTORY「メモリオーバーレイの分離」節）。表示は マネージド／GCコミット／プライベート／WS／**ネイティブ**
  （＝プライベート − max(GCコミット, マネージド現在値)＝GC 管轄外。GC の合間はマネージド伸長分の
  見かけが乗るため GC 直後の値どうしで比較する＝ラベルに「(GC直後が正)」注記）＋
  直近の GC 前後値（`Ctrl+M` とアイドル GC で共用）。
  `Ctrl+Shift+M` は `MemoryLog`（TSV 出力・詳細は HISTORY「メモリ時系列ログ」節）を開始/停止し、
  記録中はオーバーレイに「● REC」を表示
- プレビュー中: `E` 右パネル上段をルーペ⇄**画像情報**で切替（上段のタブクリックでも可。状態は
  `AppSettings.PreviewExifPanel` に永続化＝キー名は旧称のまま）。並びは **File → 評価（このアプリ）→ EXIF 等**。
  評価は項目ごとの値＋**更新日時**（不明は `—`／未設定は「未設定」）を表示し、評価変更では ListView を
  再構築せず該当行だけ差分更新（スクロール位置維持）＝`Controls/EvaluationInfoSection`。
  全ディレクトリ・全タグ＝Core `ExifTagReader.ReadAllTags`（File グループのみ先頭へ並べ替え・File Type は末尾据え置き）／
  UI `Controls/ExifDetailPanel`（＝画像情報パネル。クラス名は旧称のまま）・グループ化 ListView 仮想化・
  行テンプレートは `InfoRowTemplateSelector` で型別。XMP は `XmpDirectory.GetXmpProperties()` で
  `xmp:Rating` 等のプロパティへ展開＝HISTORY「EXIF 詳細パネルの XMP タグ展開」「画像情報パネル」節）
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
- **アプリ設定は日常版と開発版でフォルダを分離**（`AppSettings.SettingsFolderName` をビルド構成で切替。2026-07-12）。
  Release=`%LOCALAPPDATA%\PhotoQuickSelector\`（配布＝日常利用。既存設定を継承）／Debug=`%LOCALAPPDATA%\PhotoQuickSelector.Dev\`。
  開発版に日常版の設定を引き継ぎたければ日常版の `settings.json` を `PhotoQuickSelector.Dev\` へ手動コピー。
  **`dotnet run` は packaged 起動**（`Microsoft.Windows.SDK.BuildTools.WinApp` がデバッグ ID を登録して AUMID 起動）
  なので、実体は MSIX リダイレクト先の
  `%LOCALAPPDATA%\Packages\<PFN>\LocalCache\Local\PhotoQuickSelector.Dev\settings.json`。
  実体パスはアプリの**バージョン情報ダイアログ**に表示される（`AppSettings.SettingsFileDisplayPath`。
  評価データのファイル名も併記。2026-07-25）。
- **評価データ（フォルダ内 sqlite）のファイル名も日常版と開発版で分離**（`AppSettings.DatabaseFileName` を
  ビルド構成で切替。2026-07-16）。Release=`PhotoQuickSelector.sqlite3`（旧アプリ互換の既定名＝挙動据え置き）／
  Debug=`PhotoQuickSelector.Dev.sqlite3`。**Core は配布形態を関知しない**設計＝`MetadataStore` の ctor 第2引数で
  ファイル名を注入し（null/空なら `DefaultDatabaseFileName`）、日常版/開発版の判断は App 側の1箇所
  （`MainViewModel` のストア生成）に閉じている。Debug の DB は空から始まるので、実データで確認したい時は
  `PhotoQuickSelector.sqlite3` をコピーして `.Dev.sqlite3` にリネームする。
  開発版を実写真フォルダに向けると `PhotoQuickSelector.Dev.sqlite3` がそのフォルダに残る点に注意。
  作成確認ダイアログの文言（`Msg_ConfirmCreateStoreContent`）はファイル名を `{0}` で受ける。
- **評価データのスキーマは v2**（2026-07-30。項目別＋行単位の更新時刻列を追加）。開いた DB は自動で
  v2 へ移行され、**配布済み v0.2.0（v1 認識）で同じ DB を触ると値だけ変わって更新時刻が古いまま残る**
  ので混在利用しないこと。逆向き（v2 の版が将来の v3 を開く）は前方互換ガードで
  `NotSupportedException` になる。詳細は HISTORY.md「評価データの更新時刻の記録」節。
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
