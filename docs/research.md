# 調査: HDT を使わずに Power.log からゲームを記録する

作成日: 2026-11-19（hdt-plugin-hsbg-kansousen のリポジトリで調べた内容を移した）
**確認済み**と**要検証**を分けて書く。確認に使った環境: Windows 11、Hearthstone のログは `C:\Program Files (x86)\Hearthstone\Logs\`、
HDT 1.59.x、プラグイン 0.14.0 の記録 754 ゲーム（うち power.log.gz があるのは 235 ゲーム）。

## 1. きっかけ: HDT がプラグインを廃止する

- お知らせ: https://help.hearthsim.net/en/articles/18470784-what-s-changing-about-plugins-in-hdt
- "From November 30th 2026 onwards Hearthstone Deck Tracker will no longer support plugins."
  新しいプラグインは入れられず、入っているプラグインは "will no longer be active" になる。審査や署名の制度は無く、例外の記述も無い
- 理由: HDT はゲームを書き換えない "Passive Tool" で、最近は LLM で作った侵襲的な mod がプラグインとして配られている。
  プラグインを審査する "App Store" は現実的でない
- HDT に組み込まれる機能（11/30 まで）: ロビーの BG レーティング、各プレイヤーの酒場ランクと種族、APM、パックの天井、配信者向けデッキ表示。
  **ゲームの記録を書き出す機能は無い**
- 方針（2026-11-19 に決定）: HDT は最新のまま使い続け、記録は HDT と別に常駐するアプリで Power.log を読んで作る。
  古い HDT に留まる案と、HDT への機能要望に頼る案は採らない

## 2. Power.log の出力と置き場（確認済み）

- 出力の設定は `%LOCALAPPDATA%\Blizzard\Hearthstone\log.config` の `[Power] LogLevel=1 FilePrinting=True Verbose=True`。
  HDT が書き換えて有効にしている。HDT は自分の追跡に Power.log を使うので、HDT を使い続ける限りこの設定は保たれる。
  記録アプリは log.config を**読むだけ**にする（足りないときは利用者に知らせる。書き換えるかは要検討）
- 置き場は `<Hearthstone のフォルダ>\Logs\Hearthstone_YYYY_MM_DD_HH_MM_SS\`（起動時刻ごとのフォルダ）。
  Hearthstone のフォルダは HDT の `%APPDATA%\HearthstoneDeckTracker\config.xml` の `HearthstoneDirectory` で分かる
- 起動中は `Power.log` に追記され、**Hearthstone の終了時に `Power_old.log` に名前が変わる**
  （2026-11-19 に確認: 23:06 の起動を終了した後、次の起動の前にフォルダの Power.log が Power_old.log になっていた）。
  `GameNetLogger.log` は名前が変わらないこともある（同じ起動で `GameNetLogger.log` のまま）
- 残っているフォルダは直近の 6 起動分だった（2026-11-19 時点で 11/10〜11/15 の 6 フォルダ）。
  **2026-11-20 に確認**: 11/19 に 2 回起動した後、11/10 と 11/12 のフォルダが消え、残りは 11/14〜11/19 の 6 フォルダだった
  （新しく起動するたびに古いものから消え、6 フォルダを保つと見られる）。11/19 21:08 のフォルダは Power.log が無いまま終わっていた
  （ゲームをせずに終えた起動。こうしたフォルダも 6 つのうちに数えられる）
- 起動直後のフォルダには、まだ Power.log が無いことがある（M1 の実装で、できるのを待つようにした）
- 1 起動分の Power.log は 170〜340 MB。1 ゲームは 28〜124 MB（gzip で 3〜5 MB）
- **欠落の確認**: 残っている 6 起動分すべてで、`Power_old.log` の `CREATE_GAME` の数がプラグインの記録数と一致した（5/6/4/7/5/4、計 31 ゲーム）。
  340 MB まで途中で別ファイルに切り替わった形跡は無い。ただし容量の上限で切り替わらない保証は無いので、常駐して読み続ける

## 3. HDT のイベントを Power.log で置き換えられるか（確認済み）

HDT のイベントは HDT が Power.log から作っている。プラグインが使っていたものと、その元の行:

| プラグインが使っていたもの | Power.log の行 | 11/15 の 4 ゲームでの出現 |
|---|---|---|
| `OnGameStart` | `GameState.DebugPrintPower() - CREATE_GAME` | 各 1 回 |
| `OnTurnStart(Player / Opponent)` | GameEntity の `TAG_CHANGE ... tag=TURN`（奇数 = ショップ、偶数 = 戦闘） | 40〜64 回 |
| `IsBattlegroundsCombatPhase`（戦闘開始） | タグ `3533`（combat setup）と `2022`（ソロの setup）の 1 → 0。HDT の `TagChangeActions` と同じ判定 | 戦闘ごと |
| `OnEntityWillTakeDamage` | `tag=PREDAMAGE` | 612〜1,252 回 |
| 前の戦闘の勝敗 | `BACON_WON_LAST_COMBAT` | あり |
| `OnGameEnd` | `TAG_CHANGE ... tag=STATE value=COMPLETE` | 各 1 回 |
| `Player.Id`（自分はどのプレイヤーか） | ヒーロー選択 `DebugPrintEntityChoices() - id=1 Player=<名前> ... ChoiceType=MULLIGAN` は自分にしか出ない。名前を `DebugPrintGame() - PlayerID=<n>, PlayerName=<名前>` に照らす | あり |
| `Core.Game.Entities`（盤面・手札・ショップ・ゴールドなど） | `FULL_ENTITY` / `SHOW_ENTITY` / `CHANGE_ENTITY` とその下の `tag=` 行、`TAG_CHANGE` を順に当てはめる | 試作で組み立てられた（§5） |

- Power.log には同じ内容が 2 回出る: `GameState.DebugPrintPower`（サーバーから受け取った時点、約 68 万行）と
  `PowerTaskList.DebugPrintPower`（演出に合わせて処理した時点、ほぼ同数）。片方だけ読めば処理は半分で済む。
  HDT で「同じダメージが 2 回来る」のはこの 2 系統が原因と思われる（**要検証**）。どちらを使うかは実装で決める
- 自分の選択肢（`DebugPrintEntityChoices`）には発見（`ChoiceType=GENERAL`）も出る。以前「取れない」とした発見の選択肢は、自分の分なら取れる見込み（**要検証**）
- プレイヤー名（BattleTag）は Power.log に出る。照合に使うだけで、記録の JSON には書かない
- 1 行ずつ処理するので、プラグインの 100 ms ごとの差分検出より正確になる（HDT が書き換え中の状態を読む問題や、終わり際の変化の取りこぼしが無くなる）。
  同じ入力から同じ結果が出るので、ゲーム中に作った記録と、後で中継ログから作り直した記録が一致する

## 4. HDT に頼っていた値（確認済み）

| 値 | プラグインの取り方 | HDT なしで |
|---|---|---|
| MMR（前・後） | `GameStats.BattlegroundsRating` / `RatingAfter`（HDT がメモリから読む） | Power.log と他のテキストログには無い（11/10 のゲームの 8178 / 8176 で検索して確認）。HDT の `%APPDATA%\HearthstoneDeckTracker\BgsLastGames.xml` の `<Game StartTime= Rating= RatingAfter= Placemenent= ...>` を開始時刻で照らす。`Player` 属性はアカウント ID なので記録しない。11/30 以降も HDT が書き続けるかは**要検証** |
| ロビーの種族 | `BattlegroundsUtils.GetAvailableRaces()` | HDT はメモリから読んでいる（master の `Hearthstone/BattlegroundsUtils.cs` の `ReadAvailableRacesFromMemory`）。Power.log には無い。ショップに出たミニオンの種族から推定するか null |
| Bob's Buddy | HDT 内部をリフレクションで読む | 取れない。null にする（schema は null を許している） |
| カードのデータ（名前・ティア・種族） | HDT の `Cards` / HearthDb | HDT の `%APPDATA%\HearthstoneDeckTracker\CardDefs\CardDefs.*.xml` を読む（hsbg-kansousen の `cards.py` と同じ）。HDT を使い続けるので更新される |
| サーバー側のゲーム番号 | `MetaData.ServerInfo.GameHandle`（HearthMirror） | `GameNetLogger.log` の `Network.GotoGameServe() - ... game=<n> ... reconnecting=<bool>`。同じ行にある接続先アドレスと `spectateKey` は記録しない |
| Power.log の写し | `Core.Game.PowerLog` | 自分で切り出す。HDT の写しで起きていた日本語のカード名の文字化けも無くなる |

## 5. 性能（確認済み。試作: `spike/powerlog-bench/`）

試作は本番と同じ流れで処理する。共有モード（`FileShare.ReadWrite | FileShare.Delete`）で開いて 1 行ずつ読み、
`CREATE_GAME` で区切って gzip の中継ログに書き出し、`GameState` の行から全 Entity のタグをメモリに組み立てる。.NET 8、Release ビルド、Ryzen 7 9800X3D で測った。

| 入力 | ゲーム数 | 処理時間（CPU） | 最大のワーキングセット | 1 ゲームの最大 Entity 数 |
|---|---|---|---|---|
| 11/15 の起動分 233 MB（168 万行） | 4 | 1.4 秒（1.5 秒） | 95 MB | 7,184 個（タグ 13.9 万件） |
| 11/14 16:51 の起動分 340 MB（246 万行） | 7 | 2.1 秒（2.2 秒） | 91 MB | 3,777 個（タグ 7.5 万件） |

- 遊びながら読む量は 1 秒あたり約 30 KB（340 MB / 約 3 時間）。CPU の負荷はほぼ無い
- メモリは .NET の分を含めて約 90 MB。Entity の状態はゲームが変わると捨てるので増え続けない。WPF の常駐アプリにした分は**未測定**（150〜200 MB の見込み）
- ゲームが終わるまで持つもの: 今のゲームの Entity の状態（数十 MB）とターンごとの記録（数百 KB）。Power.log の行は読んだらすぐ中継ログに書く
- 中継ログはゲーム中は gzip しないテキストで追記し、終わってから圧縮する（gzip のまま書くと、落ちたときに壊れる）。一時的なディスクの使用量は最大のゲームで約 124 MB

### M1 の実装での測定（2026-11-20。切り出しだけで、Entity の状態はまだ組み立てない）

| 場面 | 入力 | 結果 |
|---|---|---|
| まとめて切り出す（`hsbg-kifu slice`） | 今の Logs（5 起動分、21 ゲーム） | 5.4 秒、最大のワーキングセット 104 MB |
| 同上 | M0 の退避先（7 起動分の .gz） | 4.0 秒。書き終えた 21 ゲームは読み飛ばし、11 ゲームを追加 |
| 追記しながら追う（`hsbg-kifu watch`） | 11/14 の Power_old.log 340 MB を大小ばらばらの塊で 11 秒かけて追記（実際の約 1,000 倍の速さ） | CPU 2.3 秒、最大 94 MB |
| 起動直後の読み直し（同上） | 同じ 340 MB がすでにある Power.log | CPU 2.1 秒、最大 131 MB（並行 GC を止めて。止めないと 163 MB） |

- 中継ログの中身は、追いながら書いたもの・読み直したもの・まとめて切り出したもので**バイト単位で一致**した
- 中継ログの `GameState.` の行は、プラグインの power.log.gz（HDT の `Core.Game.PowerLog`）と行末の空白を除いて同じだった（11/15 12:34 のゲーム、99,058 行）。
  HDT は `GameState.` の行だけを持っていて、行末の空白を削っている。中継ログはそれに `PowerTaskList.` などの行が加わる（同じゲームで 206,940 行）
- 終わり方: 11/15 12:34 のゲームは、サーバー側の COMPLETE が 12:51:59、画面側の COMPLETE が 12:52:39（40 秒後）。画面側の後に続くのは 3 秒後の TAG_CHANGE が数行だけで、
  次の CREATE_GAME（12:53:28）の直前に `DebugPrintPowerList() - Count=44` がある
- 32 ゲームのうち、ゲーム番号が取れなかったものは無い。11/10・11/12 の分は M0 で退避していたから残った（§2: Hearthstone が消していた）

## 6. 落ちたとき

| 起きたこと | ログ | 扱い |
|---|---|---|
| Hearthstone が落ちて同じゲームに戻った | 新しいフォルダの Power.log が、その時点の全状態を書いた `CREATE_GAME` から始まる（確認済み: 2026-10-17 のゲームの power.log.gz に 18:05:57 の 2 回目の `CREATE_GAME`、`tag=STATE value=RUNNING` 付き）。それまでの行は前のフォルダの `Power_old.log` に残る（hdt-plugin-hsbg-kansousen の docs/hdt-research.md §3.19） | `GameNetLogger.log` の `game=<n> reconnecting=True` で同じゲームとつなぐ。落ちていた間の戦闘は HP + アーマーの差から推定（プラグインの `Logic/Reconnect` と同じ） |
| Hearthstone が落ちて戻らなかった | `STATE=COMPLETE` 無しで終わる | Hearthstone のプロセスの終了・新しいフォルダ・別のゲームの開始のどれかで `status: "incomplete"` にする（`Logic/AbortedGame` と同じ） |
| 記録アプリが落ちた・途中で起動した | Power.log は Hearthstone がディスクに書き続けている | 起動時に今の Power.log を頭から読み直す（1 起動分で約 2 秒）。プラグインの部分ログジャーナルは要らない |
| PC ごと落ちた | 中継ログはディスクに書いたところまで残る | 次の起動時に残っているログフォルダから補う |

## 7. 要検証の一覧

- `GameState` と `PowerTaskList` のどちらを使うか（重複・落ちたときにどこまで残るか・遅れ）
- Hearthstone がログを書くまでの遅れ（HDT がほぼ即時に動いているので問題ない見込み）
- ログフォルダを何回分残すか。容量の上限での切り替えがあるか（`Power_old.log` になるのは終了時と確認済み、§2）
- `BgsLastGames.xml` に MMR が入るまでの時間。11/30 以降も書かれ続けるか
- ロビーの種族を推定したときの精度
- 自分の発見の選択肢が取れるか（記録するかは別に決める）
- WPF 常駐アプリにしたときのメモリ
