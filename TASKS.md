# TASKS.md

今どこにいて、次に何をするか。完了したら `[x]` にし、判明したことは docs に追記する。

## 新しいセッションの始め方（Claude Code 向け）

1. `CLAUDE.md` → この `TASKS.md` → docs/research.md（調査）→ docs/architecture.md（設計）の順に読む
2. 移すもの・参考にするものは隣の `..\hdt-plugin-hsbg-kansousen`（以下「プラグイン」）にある。ログの仕様はプラグインの docs/log-schema.md が正本（M6 でこちらへ移す）
3. 作業の区切りで、変更したファイルと推奨するコミットメッセージを報告して止まる。コミットは許可を得てから

## 現在の状態

- 2026-11-19: リポジトリを作った。調査（docs/research.md）、設計の案（docs/architecture.md）、性能の試作（`spike/powerlog-bench/`）、
  M0 の退避スクリプト（`scripts/backup-powerlogs.ps1`）。次は M1
- 期限: HDT は 2026-11-30 にプラグインを止める。それまではプラグインで記録を続け、その記録を答え合わせに使う

## タスク

### M0. 当面の取りこぼし対策（11/30 までに）
- [x] 記録アプリができるまでの間、Hearthstone の `Logs\Hearthstone_*\Power*.log` を gzip して退避するスクリプト
      （`scripts/backup-powerlogs.ps1`。`GameNetLogger*.log` も退避する。既定の退避先は `Documents\HSBG-PowerLogs`。
      ログフォルダは 6 起動分ほどしか残らない。退避したファイルは後で M3 の作り直しコマンドに通す）。
      2026-11-19 に確認: 6 起動分 1.47 GB → 72 MB（約 10 秒）、展開した中身のハッシュが元と一致、2 回目は全件を飛ばす、
      試験用のフォルダで「起動中の Power.log → 追記 → `_old` に名前が変わる」を再現して古い写しが置き換わる。pwsh と powershell.exe の両方で動く
- [x] 実機確認: Hearthstone の起動中にスクリプトを実行しても、Hearthstone に影響しないこと。
      その後の終了で Power.log が Power_old.log になり、次の実行で置き換わること
      （2026-11-19 23:06 の起動で 1 ゲーム。起動中の実行で Hearthstone に影響なし。終了後の実行で `Power_old.log.gz` だけが残った）

### M1. 読み続けて切り出す（Watch）
- [ ] ソリューションの骨組み（.NET 8。`src/` に記録アプリとライブラリ、`tests/` に xUnit）。検証コマンドを決めて CLAUDE.md に書く
- [ ] Power.log を後ろから読み続ける（共有モード、書きかけの行、名前の変更・差し替え・新しいフォルダへの移動）
- [ ] `CREATE_GAME` / `STATE=COMPLETE` でゲームごとに切り出し、中継ログ（平文で追記 → 終わったら gzip）と付帯情報
      （日付つきの開始時刻、ゲーム番号、Hearthstone のビルド番号）を書く
- [ ] 起動時に残っているログフォルダを読み、まだ切り出していないゲームを回収する（同じゲームの判定: 開始時刻 + ゲーム番号）
- [ ] テスト用の短い Power.log（匿名化したもの。プラグインの `scripts/anonymize_log.py` を参考にする）で、切り出し・再接続・途中で切れたログを試す

### M2. Power.log から状態と出来事を作る（Parse）
- [ ] Entity の状態の組み立て（`FULL_ENTITY` / `SHOW_ENTITY` / `CHANGE_ENTITY` / `TAG_CHANGE`、プレイヤー名 → `PlayerID`）
- [ ] 自分のプレイヤーの判定（ヒーロー選択の `ChoiceType=MULLIGAN` の `Player=`）
- [ ] 出来事: ゲーム開始・ヒーロー選択・ショップ開始・戦闘開始（`3533` / `2022`）・ダメージ（`PREDAMAGE`）・ゲーム終了・再接続
- [ ] `GameState` と `PowerTaskList` のどちらを使うか決める（docs/research.md §7）

### M3. 記録を作る（Capture / Logic / Model / Output）と答え合わせ
- [ ] プラグインの `Logic/`・`Model/`・`Output/` とテストを移す（net472 → net8.0、Newtonsoft.Json は NuGet）
- [ ] プラグインの `Capture/`（`GameSession`、`EntitySnapshotter`、`PlayerBuffs`、`DeityReader`、`HdtCards`）を、M2 の状態と出来事を使う形に書き直す
- [ ] カードのデータを HDT の CardDefs から読む
- [ ] 中継ログから記録を作り直すコマンド
- [ ] 答え合わせ: プラグインの power.log.gz がある 235 ゲーム（`Documents\HSBG-Logs`）で、作った JSON とプラグインの JSON を比べ、違いを一覧にする。
      違ってよいのは Bob's Buddy（null）、ロビーの種族、プラグインの既知の取りこぼしだけ。違いはここに理由を書く

### M4. HDT のファイルから補う値（Meta）
- [ ] MMR: `BgsLastGames.xml` を開始時刻で照らす（前の値と後の値。後の値が入るまで待つ）
- [ ] ゲーム番号と再接続: `GameNetLogger.log`
- [ ] ロビーの種族: ショップに出たミニオンから推定するか、null にするかを決める
- [ ] 未完了のゲーム（Hearthstone が落ちて戻らなかった）を `status: "incomplete"` で書く

### M5. 常駐アプリ（App）
- [ ] タスクトレイ常駐、設定画面（出力先など。プラグインの `UI/`・`Config/` を移す）、ログ出力（プラグインの `Diagnostics/`）
- [ ] Windows へのログオン時に起動する設定（既定 OFF）
- [ ] log.config で Power.log が有効か確かめ、無効なら知らせる
- [ ] 実機確認（人間に依頼）: 数ゲーム遊んで、記録・メモリ・CPU・Hearthstone を落としたときの扱いを確かめる

### M6. ログの仕様と hsbg-kansousen
- [ ] ログの仕様（`docs/log-schema.md`、`schema/game-log.schema.json`）の正本をプラグインからこちらへ移す。
      何で作ったかを示すキー（例: `recorder`）を足すなら `schema_version` を上げる
- [ ] hsbg-kansousen の参照先（コピー元・`check_cli_sync.py`・CI・CLAUDE.md・README の「プラグインと hsbg の版」）をこのリポジトリに変える

### M7. 配布と切り替え
- [ ] 配布の形（framework-dependent か self-contained か、zip かインストーラか）を決めて、リリースの手順を docs に書く
- [ ] 最初のリリース。プラグインの README に「2026-11-30 以降の HDT では動かない。記録アプリへ」と書き、プラグインのリポジトリは保守を止める

## 判明したこと・決めたこと

- 2026-11-19: 名前を hsbg-kansousen-recorder から hsbg-kifu に変えた（棋譜を取るアプリと、感想戦をする hsbg-kansousen という関係を名前に出す。
  hsbg-kansousen 専用に見える接頭辞をやめた）。C# の名前空間は `HsbgKifu`
- 2026-11-19: 新しい記録アプリは、このリポジトリで作って管理する。プラグインのリポジトリは資産として読み、必要なものを移す
- 2026-11-19: 記録は HDT とは別の常駐アプリで、Power.log から作る（docs/research.md §1）。記録の JSON はこのアプリが作り、形は今のプラグインと同じ（方針 A）
