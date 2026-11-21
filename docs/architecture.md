# 設計（案）

作成日: 2026-11-19。調査は docs/research.md。決まったら「案」を外す。

## 全体

```
Hearthstone ── Power.log / GameNetLogger.log ──▶ 記録アプリ（常駐、タスクトレイ）
HDT ── BgsLastGames.xml（MMR）、CardDefs（カードのデータ）──▶     │
                                                             ├─ ① 中継ログ: <日時>_<ゲーム番号>.power.log.gz（ゲーム 1 つ分の生の行）+ 付帯情報
                                                             └─ ② 記録: <日時>_<id>.json / .events.jsonl（今のプラグインと同じ形）
                                                                    ▼
                                                             hsbg-kansousen（分析）
```

- 記録アプリは .NET 10 の WinForms（タスクトレイに常駐。M5）。配布はランタイムを同梱した exe の zip（docs/release.md）。Hearthstone と HDT のファイルを**読むだけ**。
  メモリ読み取り・パケット解析・ゲームの改変・外部への送信はしない
- ① は解釈しない生のデータで、正本。② は ① から作る。解析を直したら、① から ② を作り直せる（作り直すコマンドを付ける）
- ② の形は hdt-plugin-hsbg-kansousen の docs/log-schema.md を引き継いだ（M6 で正本をこのリポジトリの docs/log-schema.md に移した）。hsbg-kansousen は今のまま読める

## 層

| 層 | 役目 | 出どころ |
|---|---|---|
| Watch | ログフォルダの監視、Power.log を後ろから読み続ける、ゲームごとの切り出しと中継ログ、起動時の取りこぼしの回収 | 新規 |
| Parse | Power.log の行 → Entity の状態（`GameState` 相当）と出来事（ゲーム開始・ヒーロー選択・ショップ開始・戦闘開始・ダメージ・ゲーム終了・再接続） | 新規（M2）。HDT が受け持っていた範囲。下の「状態と出来事」 |
| Capture | 出来事ごとに状態からスナップショットを取る（盤面・手札・ショップ・ロビー・強化・神）→ 記録（GameLog）と events.jsonl | M3。プラグインの `Capture/GameSession` などを `GameRecorder` に書き直した。下の「記録を作る」 |
| Logic | 純粋ロジック | M3 でプラグインの `Logic/` を移した（ShopActionTracker はまとまりの途中の tick を扱えるようにした） |
| Model / Output | DTO と書き出し | M3 でプラグインの `Model/`・`Output/` を移した。Newtonsoft.Json は NuGet で同梱する（HDT 同梱の版に頼れなくなる） |
| Cards | カードの情報（名前・dbfId・ティア・種族・攻撃力・体力） | M3。HDT が更新し続ける `CardDefs.base.xml` を読む（`CardDb`） |
| Meta | MMR（`BgsLastGames.xml`。`Capture/BgsLastGames`）、ゲーム番号（`GameNetLogger.log`）、ロビーの種族（`Capture/LobbyMinionTypes`） | ゲーム番号は M1。MMR・ロビーの種族・再接続のつなぎは M4 |
| App | タスクトレイ、設定画面、動作記録、サインイン時の起動、log.config の確認 | M5。`src/HsbgKifu.App`（WinForms）。設定は Core の `Config/`（`KifuConfig`・`OutputDirectory`・`LogConfigCheck`）、組み立ては Core の `KifuService`（CLI の watch と共通） |

## 状態と出来事（Parse。M2 で実装）

- `PowerLogParser` が `GameState.DebugPrintPower` / `DebugPrintGame` / `DebugPrintEntityChoices` の行を順に当てはめ、`PowerGame`（1 ゲームの状態）を組み立てる。
  `PowerTaskList.` の行は読まない（docs/research.md §3）
- `PowerGame` / `Entity` の読み方は HDT の `GameV2` / `Entity` に寄せてある（`Entities`、`PlayerEntity`、`GameEntity`、`TurnNumber`（= GetTurnNumber）、
  `GetTag(GameTag)`、`IsMinion`、`IsInPlay`、`IsControlledBy`、`Health`（HEALTH − DAMAGE）など）。タグは HearthDb の `GameTag`（ThirdParty/HearthDb）。
  M3 でプラグインの `Capture/` を、HDT の代わりにこれを読む形で移す
- 出来事（`PowerGameListener`）は行を当てはめた**後**に呼ぶ。受け手は今の状態を読める

| 出来事 | 元の行 | HDT の対応 |
|---|---|---|
| `OnGameCreated` / `OnGameSetupDone` | `CREATE_GAME` / その下の Entity を当てはめ終えたとき | OnGameStart |
| `OnLocalPlayerKnown` | Bob 役（`BACON_DUMMY_PLAYER`）でない方、またはヒーロー選択の `Player=` | Player.Id |
| `OnTurnChanged` | GameEntity の TURN（奇数 = ショップの始まり、偶数 = ショップの終わり / 戦闘のターン） | OnTurnStart(Player / Opponent) |
| `OnCombatSetupEnded` | タグ 3533 / 2022 の 1 → 0 | IsBattlegroundsCombatPhase = true |
| `OnPredamage` / `OnDamage` | `PREDAMAGE` > 0 / `META_DATA - Meta=DAMAGE` と `Info[i]` | OnEntityWillTakeDamage / （無し） |
| `OnChoices` | `DebugPrintEntityChoices`（ヒーロー選択は MULLIGAN） | BattlegroundsHeroPickState |
| `OnEntityUpdated` / `OnTagChanged` | FULL / SHOW / CHANGE_ENTITY と、その下の `tag=` / TAG_CHANGE / HIDE_ENTITY | Entities の変化 |
| `OnGameEnded` | GameEntity の STATE = COMPLETE | OnGameEnd |

- 再接続: CREATE_GAME の下の GameEntity の TURN が 0 より大きければ `StartedMidGame`。その下にその時点の全 Entity が来る。作られたときのタグでは TURN などの出来事を出さない
- プレイヤー名（BattleTag）は名前の解決にだけ使い、`PowerGame` の外に出さない（`internal`）
- 受け手の例外は捕まえて動作記録に書き、読み取りは続ける
- `SliceReplay` は中継ログ（.power.log.gz）を読んでパーサに流す（M3 の作り直しコマンドの土台）

## 記録を作る（Capture。M3 で実装）

- `KifuBuilder.Build(中継ログ, 付帯情報, CardDb)` が中継ログを頭から読み、`GameRecorder`（PowerGameListener）で記録を組み立てる。
  ゲームが終わってから中継ログ全体を読み直して作るので、ゲーム中に作る場合も作り直しも同じ処理・同じ結果になる（1 ゲーム 0.3〜1 秒）
- 常駐: 中継ログを書き終えたら（`SliceStore.SliceFinished`。起動時の回収でも）、`KifuWriterService` が記録を作って書く。作り直し: `hsbg-kifu rebuild`
- 書き出し: `<出力先>\YYYY\MM\<yyyyMMdd-HHmmss>_<game_id>.json` と、同じ名前の `.events.jsonl`・`.power.log.gz`（プラグインと同じ置き方）。
  `.power.log.gz` は中継ログの `GameState.` の行だけ（プラグインの写しと同じ中身。hsbg-kansousen の補完コマンドが読む）
- 二重記録の防止: 出力先に、開始時刻（ファイル名）が 2 分以内で game_id が違う記録（= 同じゲームのプラグインの記録）があれば書かない。自分の記録は上書きする
- 書き終えるたびに .NET のメモリを回収して OS に返す（常駐中の使用量を抑える。docs/research.md §3.2）

| プラグイン（HDT） | GameRecorder |
|---|---|
| OnGameStart | OnGameCreated（generator、game_id、started_at） |
| OnTurnStart(Player) / OnTurnStart(Opponent) | OnTurnChanged（奇数 = ショップの始まり / 偶数 = ショップの終わり） |
| OnUpdate の IsBattlegroundsCombatPhase の false → true | OnCombatSetupEnded（相手のヒーローと盤面） |
| OnUpdate の差分の観測（約 100 ms ごと） | OnBatchEnd（ショップはまとまりの終わり、戦闘中の強化と神祇はブロックの終わりでも） |
| ショップの行動の差分からの推定 | 購入・売却・リロール・スペル・悪夢の発見・ヒーローパワーは自分の PLAY ブロック（`ShopBlockActions`）、ほかは ShopActionTracker |
| OnEntityWillTakeDamage | OnPredamage（戦闘のダメージ = 戦闘の結果のダメージ。アイスブロックで防がれても数える） |
| BattlegroundsHeroPickState | ヒーロー選択（MULLIGAN）の選択肢の Entity |
| OnGameEnd | OnGameEnded（ended_at = 中継ログの最後）。終わりが無ければ Finish で未完了 |

- ロビーの種族: ショップに並んだミニオンの種族の上位 5 つ（無種族・全種族は数えない）。MMR: BgsLastGames.xml を開始時刻で照らす（常駐中は後の MMR を最長 60 秒待って書き直す）。
  再接続: 同じゲーム番号の中継ログ（2 つ目以降が reconnecting）を続けて読んで 1 つの記録にし、見られなかった戦闘は HP + アーマーの差から推定する（docs/research.md §3.3）

取れないもの: Bob's Buddy（null）、地域（null）

## Power.log の読み方

M1 で実装した（`src/HsbgKifu.Core/Watch/`）。

- 最新のフォルダ（フォルダ名の起動日時が最新の `Hearthstone_*`）の `Power.log` を `FileShare.ReadWrite | FileShare.Delete` で開く
  （Delete が無いと、Hearthstone が名前を `_old` に変えるのを妨げる）。250 ms ごとに増えた分を 4 MB ずつ読み、読んだらすぐ処理する
- 書きかけの最後の行は読まずに待つ。ファイルが短くなったら頭から読み直す（書き終えたゲームは読み飛ばすので重複しない）
- 名前が `Power_old.log` に変わったら（Hearthstone の終了）、開いたまま残りを読み切って閉じる。新しいフォルダができたらそちらへ移る。
  最新のフォルダに Power.log がまだ無ければ（起動直後）、できるのを待つ
- 起動時は、最新以外の全フォルダを読み、まだ書き終えていないゲームを切り出す。最新のフォルダの Power.log は頭から読んで後を追う

## 中継ログ

- 置き場: `<出力先>\.slices\YYYY\MM\<yyyyMMdd-HHmmss>_<ゲーム番号>.power.log.gz` と `.slice.json`（付帯情報）。
  ゲーム番号が分からなければ `o<バイト位置>`、再接続なら末尾に `r`。`.slices` はドットで始まるので hsbg-kansousen は読まない
- ゲームの見分け: ログフォルダ名 + CREATE_GAME の行の時刻（"HH:MM:SS.fffffff"）。ファイル名（Power.log / Power_old.log / .gz）にもバイト位置にも依らない
- 始まり: `GameState.DebugPrintPower() - CREATE_GAME`。直前の `GameState.DebugPrintPowerList() - Count=` の行も入れる
- 終わり: 画面側の `PowerTaskList ... TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE`。サーバー側（`GameState`）の COMPLETE の約 40 秒後に来て、
  その後は後片付けの数行しか無いので、そこで閉じる（docs/research.md §5）。サーバー側の COMPLETE だけで止まったら、次のゲーム・ファイルの終わり・
  60 秒の無音で閉じる（complete）。COMPLETE を見ないまま、ファイルの終わり・Hearthstone のプロセスが無いまま 15 秒の無音なら incomplete
- BG 以外: `DebugPrintGame() - GameType=` が `GT_BATTLEGROUNDS` で始まらなければ書かない（分かるまで行をメモリに貯める。最大 5,000 行）
- 書いている間は平文の `.power.log` に追記し、`.slice.json` は `status: "recording"`。終わったら gzip にして平文を消し、`complete` / `incomplete` にする。
  記録アプリを止めたときの書きかけは recording のまま残り、次の起動で Power.log から書き直す
- ゲーム番号と再接続: CREATE_GAME の時刻の直前 5 分以内（後ろへ 5 秒まで）の `GameNetLogger.log` の `GotoGameServe` 行。再接続の 2 つの中継ログをつなぐのは M4
- `.slice.json` に BattleTag・接続先アドレス・spectateKey は入れない（`GameNetLog` は読み捨てる）

## 持つものと書き出し

- メモリに持つのは、今のゲームの Entity の状態とターンごとの記録だけ（docs/research.md §5）
- 中継ログはゲーム中は平文で追記し、終わったら gzip にして平文を消す
- 記録の JSON は一時ファイルに書いてから置き換える（プラグインと同じ）。MMR の後の値は `BgsLastGames.xml` に入りしだい上書きする
