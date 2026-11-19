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

- 最新のフォルダ（作成時刻が最新の `Hearthstone_*`）の `Power.log` を `FileShare.ReadWrite | FileShare.Delete` で開く
  （Delete が無いと、Hearthstone が名前を `_old` に変えるのを妨げる）
- 書きかけの最後の行は読まずに待つ。ファイルが小さくなったり差し替えられたりしたら、別のファイルとして読み直す
- 新しいフォルダができたらそちらへ移る
- ゲームの区切りは `CREATE_GAME`、終わりは `STATE=COMPLETE`。再接続は `GameNetLogger.log` の `reconnecting=True` とゲーム番号で見分ける
- 起動時は、残っている全フォルダを読み、まだ中継ログが無いゲームを切り出す（同じゲームかどうかは開始時刻とゲーム番号で判定する）

## 持つものと書き出し

- メモリに持つのは、今のゲームの Entity の状態とターンごとの記録だけ（docs/research.md §5）
- 中継ログはゲーム中は平文で追記し、終わったら gzip にして平文を消す
- 記録の JSON は一時ファイルに書いてから置き換える（プラグインと同じ）。MMR の後の値は `BgsLastGames.xml` に入りしだい上書きする
