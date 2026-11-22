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
  `PowerTaskList.DebugPrintPower`（演出に合わせて処理した時点、ほぼ同数）。**M2 で `GameState` を使うと決めた**:
  2 系統は同じ内容を同じ順に書いたもので、違うのは書かれる時刻だけ。行ごとに状態を当てはめるので、出来事の時点の状態は同じになる。
  サーバー側の行なので、落ちたときにも先まで残り、遅れも少ない。
  **訂正（2026-11-21、M3）**: M2 では「HDT と同じ GameState」と書いたが、HDT が Entity を更新するのは `PowerTaskList` の行
  （`LogReader/LogWatcherManager.cs` の `OnNewLines`。`GameState.` の行は Power.log の写し `Core.Game.PowerLog`・選択肢・ゲーム情報にだけ使う）。
  HDT の出来事やプラグインの 100 ms ごとの観測は、演出の進行（PowerTaskList）の時刻に合わせて起きていた。この違いが効くところは §3.2
- HDT で「同じダメージが 2 回来る」のは 2 系統のせいではなかった。`GameState` の中で PREDAMAGE が 1 回の攻撃につき「値 → 0 → 値 → 0」と 2 回立つ（§3.1）
- 自分の選択肢（`DebugPrintEntityChoices`）には発見（`ChoiceType=GENERAL`）も出る。以前「取れない」とした発見の選択肢は、自分の分なら取れる見込み（**要検証**）
- プレイヤー名（BattleTag）は Power.log に出る。照合に使うだけで、記録の JSON には書かない
- 1 行ずつ処理するので、プラグインの 100 ms ごとの差分検出より正確になる（HDT が書き換え中の状態を読む問題や、終わり際の変化の取りこぼしが無くなる）。
  同じ入力から同じ結果が出るので、ゲーム中に作った記録と、後で中継ログから作り直した記録が一致する

### 3.1 M2 の実装で分かったこと（2026-11-20、`src/HsbgKifu.Core/Parse/`）

**タグの名前と番号**
- `tag=` の名前は 1 ゲームで約 320 種類。名前のあるタグは名前で、無いもの（3533、2022 など約 400 種類）は番号で出る。
  値が名前で出るのは `ZONE`・`CARDTYPE`・`CARDRACE`・`CLASS`・`FACTION`・`MULLIGAN_STATE`・`STEP`・`NEXT_STEP`・`PLAYSTATE`・`RARITY`・`STATE` だけ
- 名前 → 番号は HearthSim/HearthDb の `Enums.cs`（MIT。v37.0.0、2026-10-13）を `src/HsbgKifu.Core/ThirdParty/HearthDb/` に写して使う。
  公式の NuGet パッケージは無い（`dotnet package search HearthDb` は第三者の古いものだけ）。22 ゲームに出た名前と値はすべて載っていた
- **Hearthstone の更新で名前が増えたら**: パーサは知らない名前を数えて警告する（`UnknownTagCount`。その行は当てはめない）。
  HearthDb の新しい `Enums.cs` を写し直し、ファイルの先頭のコミットを書き換え、THIRD_PARTY_NOTICES.md を確かめる

**行の形**
- 行の種類はインデントで見分けられない（ブロックの中では TAG_CHANGE も深くインデントされる）。先頭の空白を除いた中身で見分ける
- TAG_CHANGE の末尾に ` DEF CHANGE` が付くことがある。Entity の指し方は 4 通り: 番号 / `GameEntity` / `[entityName=... id=N zone=...]`
  （名前の中に `UNKNOWN ENTITY [cardType=INVALID]` のような角かっこが入る）/ プレイヤー名
- **プレイヤー名**: 自分は常に DebugPrintGame の名前で指される。相手役のプレイヤー（ソロでは Bob 役。`BACON_DUMMY_PLAYER=1`）は、
  その時の戦闘相手の BattleTag などで指され、名前は毎回変わる（1 ゲームで 9 通り）。DebugPrintGame に無い名前は Bob 役とみなす。
  CREATE_GAME の下の `Player EntityID=5 PlayerID=2 GameAccountId=[hi=... lo=...]` にはアカウント ID が出る（読まない）
- **自分の判定**: Bob 役でない方のプレイヤー（CREATE_GAME の直後に分かる）。ヒーロー選択（`ChoiceType=MULLIGAN` の `Player=`）とも全ゲームで一致した。
  再接続ではヒーロー選択が来ないが、Bob 役の判定で分かる（10/17 のゲームで確認）
- ヒーロー選択で選んだものは `GameState.DebugPrintEntitiesChosen() - ... Entities[0]=[... id=N ...]` に出る

**ダメージ**
- 1 回の攻撃で PREDAMAGE が「値 → 0 → 値 → 0」と 2 回立ち、実際に入ったところで `META_DATA - Meta=DAMAGE Data=<量> InfoCount=1` と
  `Info[0] = <受けた Entity>` が 1 回出る。パーサは前者を `OnPredamage`、後者を `OnDamage` として知らせる
- PREDAMAGE が立っても META_DATA DAMAGE が出ない場合がある（236 ゲームで 7 ターン分）:
  - 自分のヒーロー（6 件）: どれも次のターンの HP が変わっていない。**アイスブロック**（装飾品 Safety Patch `BG35_MagicItem_820` で得る秘策
    `TB_Bacon_Secrets_12`。HP が 0 になるダメージを 1 度だけ無効にして消える）が、6 件ともそのターンの戦闘で墓地（GRAVEYARD）に移っていた
  - 相手のヒーロー（10/18 12:09 の T11）: 戦闘用のヒーローに PREDAMAGE 23、ダメージは相手のリーダーボード側のヒーロー（SETASIDE）の `DAMAGE` に直接書かれた。
    これも相手のアイスブロックで、相手の HP は次のターンも 13 のまま（下の「アイスブロックの持ち主」）
- **アイスブロックの持ち主**（2026-11-22、M3.5）: 自分のアイスブロックは、自分が CONTROLLER の SECRET にあり、発動すると戦闘中に GRAVEYARD → REMOVEDFROMGAME に移る。
  相手のアイスブロックは戦闘のたびに複製される。複製は自分の PlayerID で SETASIDE に作られ、戦闘の始まりに `CONTROLLER` が相手役のプレイヤー（Bob 役の PlayerID）に、
  `ZONE` が SECRET に変わる。発動すれば GRAVEYARD に移り、発動しなければ戦闘の後に墓地を経ずに REMOVEDFROMGAME に移る
  （行の `[... player=N]` は作られたときの持ち主のままなので、持ち主は `CONTROLLER` のタグで見る。10/18 の T11 で「controller が自分」に見えたのはこのため）。
  2026-11-22 時点のプラグインの power.log.gz（`kifu-dev verify-json --slices plugin` の 242 ゲーム、2,917 戦闘）で、戦闘中に GRAVEYARD に移ったアイスブロックは
  自分 6 件・相手 1 件で、上の 7 ターンと一致した。相手の複製があって発動しなかった戦闘は 8 ゲームにあり、どれも誤って立たなかった（`combat.ice_block` / `opponent_ice_block`）
- **ショップ中のダメージ**: HP で払う購入（ミニオン・酒場の呪文）と、プレイすると HP が減るミニオンがある。ソウル・リワインダーのように
  ショップ中のダメージを巻き戻すミニオンと組み合わせることが多い（巻き戻せば HP は戻る）。戦闘のダメージとは別物で、
  戦闘開始（3533 / 2022 の 1 → 0）から次のショップの始まり（奇数の TURN）の間だけを数えれば混ざらない（混ぜると 22 ゲーム中 8 ゲームで違いが出た）
- **戦闘のダメージの定義（2026-11-20、ユーザーの説明による）**: damage_dealt / damage_taken は「戦闘の結果として生じたダメージ」。
  アイスブロックで無効になって HP が減らなくても、戦闘の結果のダメージ（例: 15）を記録する。これはプラグインの数え方（PREDAMAGE から。HDT の OnEntityWillTakeDamage）と同じ。
  PREDAMAGE の 2 回目（同じ Entity・同じ値で、間に META_DATA DAMAGE が無い）は数えない。META_DATA DAMAGE が無かった戦闘は「防がれた（または別の Entity に書かれた）」と見分けられる

**プラグインの記録との一致（`kifu-dev verify-parse`）**
- 比べた項目: ヒーロー、順位、ターン数、各ターンのティア（ショップの終わり）・HP とアーマー（ショップの始まり）・与えた / 受けたダメージ
  （上の定義。戦闘開始から次のショップまでの PREDAMAGE）
- 中継ログ 22 ゲーム（1,421 項目）: 違い 0 件（アイスブロックの戦闘 2 件を含む）
- プラグインの power.log.gz 236 ゲーム（14,949 項目）: 違いは 1 ゲーム（10/11 13:43）の 3 項目だけ。
  その power.log.gz はゲームの途中（13:59:12）で切れていて終わりの行が無い（プラグインの写しの欠け。中継ログでは起きない）
- 再接続（10/17 17:45）: 2 回目の CREATE_GAME を「途中から（TURN 23）」と判定し、473 Entity を当てはめ、全項目が一致
- 名前で指されて決められなかった行: 0。知らないタグ名: 0

### 3.2 M3 の実装で分かったこと（2026-11-21、`src/HsbgKifu.Core/Capture/`）

**観測の区切り**（プラグインの OnUpdate の約 100 ms ごとの差分の観測の代わり。`PowerGameListener.OnBatchEnd`）
- GameState の行は、サーバーから届いた 1 回分のまとまり（`DebugPrintPowerList() - Count=N` で始まる）で届く。プレイヤーの操作 1 回分がおおむね 1 まとまり。
  ターン開始のショップの入れ替えは、ターンの切り替わりの後の大きなまとまり（Count=370 など）の中の、いくつものブロックで作られる。戦闘は 1 回のまとまりで全部届く
- ショップの観測はまとまりの終わりで、戦闘中の強化と神祇はブロックの終わり（BLOCK_END）でも読む。ブロックの終わりごとにショップを観測すると、
  1 回の購入でゴールドのタグが片方だけ変わった状態を見て、ゴールド獲得を多く数える（22 ゲームで 101 ターン）
- ショップの終わり（偶数の TURN）と同じまとまりの中の行動（終わる直前に使った呪文など）は、ショップを閉じる前にもう 1 回観測して拾う

**HDT の Entity.CardId**（`Entity.OriginalCardId`。プラグインは場所によって CardId と Info.LatestCardId を使い分けていた）
- HDT の `PowerHandler` は、SHOW_ENTITY / CHANGE_ENTITY で `CardId` を書き換えるのは、まだ無いか、その時点で `BACON_IS_MAGIC_ITEM_DISCOVER`・`BACON_TRINKET`・
  `BACON_HERO_CAN_BE_DRAFTED`・`BACON_SKIN` のどれかを持つか、`CREATED_BY_` で始まるときだけ。`Info.LatestCardId` はいつも書き換える
- 例: トリンケットの枠（`BG30_Trinket_1st`、`BACON_IS_MAGIC_ITEM_DISCOVER`）は選んだトリンケットに CHANGE_ENTITY される。無料リロールのヒーローも CHANGE_ENTITY

**ほかに合わせたこと**
- 種族の名前: 新しい HearthDb では値 20 に `BEAST` と `PET` がある。.NET の ToString は PET を返すので、宣言順で最初の名前（プラグインと同じ "beast"）を使う
- トリンケット: HDT の `Player.Trinkets` は CARDTYPE ではなく `BACON_TRINKET` を持つ Entity（ターン 1 からある枠は持たない）
- ヒーローの候補: HDT の `BattlegroundsHeroPickState` の代わりに、ヒーロー選択（MULLIGAN）の選択肢の Entity を ZONE_POSITION の順に。無料リロールは同じ Entity が CHANGE_ENTITY される

**ショップの行動の回数**（`kifu-dev verify-json` の正解との比較。正解 = 自分がショップ中に出した PLAY ブロック）
- 購入（`TB_BaconShop_DragBuy` / `_Spell`。対象のミニオンが `Target=` に入る）・売却（`TB_BaconShop_DragSell`。同）・リロール（`TB_BaconShop_8p_Reroll_Button`）・
  ティアアップ（`TB_BaconShopTechUp0N_Button`）・悪夢の発見（`BG36_Button_DarkGift`）・ヒーローパワー（ヒーローパワーの Entity）・スペル（手札のスペル）は、
  プレイヤーの操作そのものとして PLAY ブロックに残る
- 差分から推定する数え方（プラグイン）は外れがある。プラグインの power.log.gz の 2,848 ターンで、購入 207・売却 494・リロール 276・スペル 438・悪夢の発見 51・
  ヒーローパワー 125 ターンが正解と違った（3 枚目を買ってすぐトリプルになった購入、リロールでショップが一度空になった、1 ターンに何度も使えるヒーローパワーなど）
- **hsbg-kifu は購入・売却・リロール・スペル・悪夢の発見・ヒーローパワーを PLAY ブロックから数える**（2,848 ターンすべて正解と一致）。
  ティアアップ・発見・トリプル・プレイ・ゴールド獲得・ショップの並びは、プラグインと同じく差分から推定する（ShopActionTracker）
- ゴールド獲得（売却以外で増えたゴールド）は正解が無い。プラグインとは 22 ゲームで 28 ターン違う（どちらも推定）

**ショップの並び（turns[].shop_offers）**
- プラグインは演出の途中を覗いていたので、ターン開始時のショップを、カードが揃う前・酒場のバフが乗る前（3/3 など）で記録していた（どこで覗けるかは演出の速さ次第）。
  hsbg-kifu はまとまりの終わりで記録するので、ターン開始の効果で足されたカードとバフを含めた、実際に並んだ状態になる（毎回同じ結果）。
  そのため shop_offers の中身・攻撃力と体力・trigger（refill / reroll / added）はプラグインの記録と違うことがある

**プラグインの記録との一致**（`kifu-dev verify-json`。比べない項目: ゲーム ID、generator、開始・終了の時刻と所要時間、地域、ロビーの種族、MMR、Bob's Buddy、notes）
- 中継ログ 22 ゲーム（109,678 項目）: 違うのは上のショップの並びとゴールド獲得だけ。盤面・手札・ロビー・戦闘・神祇・強化・相手・トリンケット・ヒーローの候補はすべて一致
- プラグインの power.log.gz 236 ゲーム: プラグインの記録にある種類の項目だけ比べると、ほかに違うのは、古い版のプラグインの書き方（0.9.4 より前は盤面のカードが元のカード）、
  今の CardDefs で過去のゲームを作り直したための種族の違い（パッチでカードの種族が変わる）、プラグインの取りこぼし（相手の盤面が無い 1 ゲームなど）
- hsbg-kansousen の `hsbg validate` で 22 ゲームすべて JSON Schema に合格し、`hsbg summarize` も作れた

**時刻と generator**
- started_at = CREATE_GAME の行の時刻（HDT の StartTime より 1 秒ほど遅い）。ended_at = 中継ログの最後（画面側の COMPLETE。HDT の OnGameEnd と同じころ）
- events.jsonl の `t` は Power.log の行の時刻（プラグインは HDT が処理した時刻）
- generator: `plugin` = "hsbg-kifu"、`plugin_version` = hsbg-kifu の版、`hdt_version` = null、`hearthstone_build` = Power.log の `BuildNumber`（プラグインは HDT の HearthstoneBuild で、別の番号）
- game_id = 中継ログ（ログフォルダ名 + CREATE_GAME の時刻）から作る UUID。作り直しても同じ ID・同じファイル名

**メモリ**（`hsbg-kifu watch`）
- 起動時に 7 ゲームを回収して記録を作る間の最大 135 MB。記録を作り終えるたびに .NET にメモリを回収させて OS に返すと、何もしていない間は 71 MB（専用メモリ 29 MB）。
  返さないと 142 MB（同 100 MB）のまま。カードの情報（CardDefs.base.xml、35,000 枚）の読み込みが最大の大半

### 3.3 M4 の実装で分かったこと（2026-11-21）

**ロビーの種族（game.minion_types）**
- ゲームの仕様（ユーザーから）: 種族はマッチング直後のヒーロー選択の画面で 5 つ示され、毎ゲーム必ず 5 つ、途中で変わらない。
  無種族のミニオンと全種族のミニオンは、どのロビーでも並ぶ。種族を 2 つ持つミニオンもある。
  ロビー外の種族のミニオンがショップに並ぶのはまれ（「驚異の教育球」でバディが並ぶ、他プレイヤーのバディを「テス・グレイメイン」が写すなど）
- **Power.log には無い**: ゲームの始まりの GameEntity・プレイヤーのタグ、ヒーロー選択中（TURN 0）の TAG_CHANGE に、種族を表すものは無い（237 ゲームで、
  ゲームごとに値が変わるのは乱数の種・バーテンダー・神・ヒーローなどだけ）。`BACON_SUBSET_*` はトリンケットなどにも付き、`IS_BACON_POOL_MINION` のカードにはロビー外の種族もある
- **他のログにも無い**: log.config に `[Gameplay]`・`[Zone]`・`[Net]`・`[Bob]` を足して 1 ゲーム遊んで調べた（2026-11-21）。Gameplay.log はエンチャントの付け外しとカードのゾーン移動、
  Zone.log（1 起動 86 MB）はゾーン移動の処理、Net.log は接続と受け取ったデータの名前だけ（**Battle.net の認証に関わる文字列が書かれる**）、Bob.log はできなかった。
  調べた後、log.config は元に戻した
- **決め方**（`Capture/LobbyMinionTypes`）: ショップに並んだミニオン（ShopActionTracker と同じ判定）を 1 体 1 回ずつ数え、上位 5 種族。
  種族は Power.log に出るミニオン自身のタグ（CARDRACE と種族のタグ）から取り、カードの一覧は持たない（カードの更新で直さなくて済む）。
  無種族（タグ無し）と全種族（CARDRACE=ALL、または種族のタグが 3 つ以上）は数えない。種族を 2 つ持つミニオンは両方を数える
- **結果**: プラグインの記録（HDT がメモリから読んだ値）と 237 ゲームすべて一致。5 番目と 6 番目が同数のゲームは無かった（同数なら notes に書く）。
  調べる途中の簡単なスクリプトはショップのミニオンを 2 割ほどしか拾えず（1 ゲーム 53 体。記録の shop_offers では 247 体）、2 つ目の種族のナーガで順位が入れ替わって 7 ゲーム外れていた

**MMR**（`Capture/BgsLastGames`）
- HDT の `%APPDATA%\HearthstoneDeckTracker\BgsLastGames.xml` の `<Game StartTime= Rating= RatingAfter= ...>`。**直近の 18 ゲームほどしか残らない**（2026-11-21 時点で最も古いのは 11/14 17:44）。
  過去のゲームを作り直すと MMR は入らない（notes に "mmr_before unavailable" / "mmr_after unavailable"）
- StartTime は HDT の開始時刻（プラグインの started_at と同じ値）。hsbg-kifu の開始時刻（CREATE_GAME）より 1 秒ほど早いので、前後 2 分以内で最も近いものを取る。
  XML に残る 18 ゲームで、前・後・差ともプラグインの記録と一致
- 常駐中は、記録を書いた後に XML を 2 秒ごとに最長 60 秒見直し、後の MMR が入ったら記録を書き直して events.jsonl に `mmr_after` を足す。作り直しでは待たない
- `Player` 属性（アカウント ID）は読まない。11/30 以降も HDT がこのファイルを書き続けるかは**要検証**

**再接続のつなぎ**（`KifuWriterService.Chain`、`GameRecorder.Resume`）
- 同じゲーム番号の中継ログで、2 つ目以降が `reconnecting=true` のものを開始時刻の順に続けて読み、1 つの記録にする（game_id・ファイル名は最初の中継ログのもの）。
  落ちた時点で最初の中継ログから未完了の記録を書き、戻って終わったときに上書きする。どの順で処理しても、同じ番号の中継ログが揃えば同じ記録になる
- 2 つ目の CREATE_GAME（GameEntity の TURN が 0 より大きい）で記録を続け、Entity に依存する追跡だけやり直す。最後まで見られなかった戦闘は、
  次のショップ開始時かゲーム終了時に HP + アーマーの差から推定する（`Logic/Reconnect.InferCombat`。notes と events.jsonl の `combat_end` に inferred）
- 確認: 10/17 17:45 のゲーム（プラグインの power.log.gz を 2 つ目の CREATE_GAME で 2 つに分けて中継ログの組にした）で、16 ターンの HP・アーマー・ティア・
  戦闘の結果とダメージ・購入の回数がプラグインの記録（手でつないだもの）とすべて一致。2 つ目の中継ログはターン 12 のショップから始まり、同じショップに戻ったと判断した

### 3.4 発見の 3 択の取り逃し（2026-11-22、0.2.1 で修正）

- 発見の 3 択（`DebugPrintEntityChoices`）は、カードを使った操作のまとまりの **途中** に出て、プレイヤーが選び終わる（`DebugPrintEntitiesChosen`）まで
  次の区切り（ブロックの終わり・次の `DebugPrintPowerList`）が来ないことが多い。選んだカードは、選び終わった後の次のまとまりで手札に入る
- 0.2.0 までは、区切りの時点の 3 択（`PowerGame.PendingChoice`）を見て覚えていたので、区切りの無い 3 択は一度も見えず、手札に入ったカードを発見と判断できなかった。
  プラグインは HDT の定期的な読み取り（約 0.1 秒ごと）で選んでいる最中の 3 択を見ていたので、取り逃さなかった
- `HSBG-Kifu-Test` の 28 戦の中継ログ: トリプル報酬の 3 択 110 回のうち、出てから選ぶまでに区切りがあったのは 39 回で、0.1.0 が記録した発見 39 件と一致した。
  プラグインの記録は 110 件（28 戦すべてで中継ログの 3 択の回数と一致。二重の記録は無い）
- 直し方: 3 択が出た時点（`OnChoices`）で候補を `ShopActionTracker.NoteOffered` に渡す。直した版で作り直すと、トリプル報酬 109 件（110 件中）、
  ヒーローパワー 12 件（プラグインの `discover` と、選んだカードを載せた `hero_power` の行の合計 12 件と全件一致）。悪夢の発見の `offered` も入るようになった
- 残る 1 件（11/14 00:47 の T8）: 選んだカードが手札に入った同じまとまりの中で、すぐトリプルになって手札から消えた。区切りごとに手札を比べる方式では見えない。
  選択の出来事（選ばれた Entity）から直接記録すれば取れる（TASKS の M8）
- M8（0.3.0、schema 1.11.0）: 選択の出来事から記録するようにした。`DebugPrintEntityChoices` の `Source=`（出したもの）と `Entities[i]`（候補。画面の順）、
  `DebugPrintEntitiesChosen` の `Entities[0]`（選んだもの）を読む。保存済みの 255 ゲームでは、ヒーロー選択以外の選択肢 4,048 回がすべて `GENERAL`・`CountMin=1 CountMax=1`・
  選んだものは 1 つで、候補の数は 1〜5（3 が 3,016 回、4 が 562 回、2 が 467 回）。選ばれないまま終わったのは 1 回
- トリンケットの選択の `Source=` は `BG30_Trinket_1st` / `BG30_Trinket_2nd` が多いが、ヒーローパワー（`BG30_HERO_304p` など）やカード（`BG30_MagicItem_703t` など）もトリンケットを選ばせる。
  そのため、候補がすべてトリンケット（CARDTYPE が BATTLEGROUND_TRINKET）なら `trinket` とし、ターン数では決めない
- 悪夢の発見の発見スペル `BG36_MidGameEffect_010` は、ボタンのほかにヒーローパワー（`BG36_HERO_105p`）も作る（スペルの CREATOR / DISPLAYED_CREATOR がヒーローパワー）。
  0.2.x の手札による判定（選んだカードの DISPLAYED_CREATOR）はこれを `hero_power` にしていたので、同じにした（2026-11-20 00:14 のゲームの 3 回）
- `HSBG-Kifu-Test` の 28 戦を作り直した結果: 選択肢 454 回（中継ログの数と一致）。`discover` の行はトリプル報酬 110（プラグインと一致。上の残り 1 件も取れた）、
  ヒーローパワー 65、カード 135、トリンケット 58。0.2.1 で書いていた `discover` の行はすべて残った。悪夢の発見は選択肢 86 回に対し `dark_gift` の行 82（手札の差から書くので、
  選んだカードがすぐ手札から消えたものは行が無い。`turns[].choices[]` にはある）

## 4. HDT に頼っていた値（確認済み）

| 値 | プラグインの取り方 | HDT なしで |
|---|---|---|
| MMR（前・後） | `GameStats.BattlegroundsRating` / `RatingAfter`（HDT がメモリから読む） | Power.log と他のテキストログには無い（11/10 のゲームの 8178 / 8176 で検索して確認）。HDT の `%APPDATA%\HearthstoneDeckTracker\BgsLastGames.xml` を開始時刻で照らす（M4 で実装。§3.3）。`Player` 属性はアカウント ID なので記録しない。11/30 以降も HDT が書き続けるかは**要検証** |
| ロビーの種族 | `BattlegroundsUtils.GetAvailableRaces()` | HDT はメモリから読んでいる（master の `Hearthstone/BattlegroundsUtils.cs` の `ReadAvailableRacesFromMemory`）。Power.log にも他のログにも無い。ショップに並んだミニオンの種族の上位 5 つ（M4 で実装。§3.3。237 ゲームすべて一致） |
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

- Hearthstone がログを書くまでの遅れ（HDT がほぼ即時に動いているので問題ない見込み）
- ログフォルダを何回分残すか。容量の上限での切り替えがあるか（`Power_old.log` になるのは終了時と確認済み、§2）
- `BgsLastGames.xml` に MMR が入るまでの時間。11/30 以降も書かれ続けるか
- 自分の発見の選択肢が取れるか（記録するかは別に決める）
- WPF 常駐アプリにしたときのメモリ
