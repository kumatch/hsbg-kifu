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

- 記録アプリは .NET 8 の WPF（タスクトレイに常駐）。Hearthstone と HDT のファイルを**読むだけ**。
  メモリ読み取り・パケット解析・ゲームの改変・外部への送信はしない
- ① は解釈しない生のデータで、正本。② は ① から作る。解析を直したら、① から ② を作り直せる（作り直すコマンドを付ける）
- ② の形は hdt-plugin-hsbg-kansousen の docs/log-schema.md を引き継ぐ。hsbg-kansousen は今のまま読める

## 層

| 層 | 役目 | 出どころ |
|---|---|---|
| Watch | ログフォルダの監視、Power.log を後ろから読み続ける、ゲームごとの切り出しと中継ログ、起動時の取りこぼしの回収 | 新規 |
| Parse | Power.log の行 → Entity の状態（`GameState` 相当）と出来事（ゲーム開始・ヒーロー選択・ショップ開始・戦闘開始・ダメージ・ゲーム終了・再接続） | 新規。HDT の `Capture/` が受け持っていた範囲 |
| Capture | 出来事ごとに状態からスナップショットを取る（盤面・手札・ショップ・ロビー・強化・神） | プラグインの `Capture/GameSession` などを、HDT の型を使わない形に書き直す |
| Logic | 純粋ロジック | プラグインの `Logic/` をそのまま移す |
| Model / Output | DTO と書き出し | プラグインの `Model/`・`Output/` を移す。Newtonsoft.Json は NuGet で同梱する（HDT 同梱の版に頼れなくなる） |
| Meta | MMR（`BgsLastGames.xml`）、ゲーム番号（`GameNetLogger.log`）、カードのデータ（CardDefs） | 新規 |
| App | タスクトレイ、設定画面、ログ出力、自動起動 | プラグインの `UI/`・`Config/`・`Diagnostics/` を移す |

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
