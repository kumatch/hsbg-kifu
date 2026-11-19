# TASKS.md

今どこにいて、次に何をするか。完了したら `[x]` にし、判明したことは docs に追記する。

## 新しいセッションの始め方（Claude Code 向け）

1. `CLAUDE.md` → この `TASKS.md` → docs/research.md（調査）→ docs/architecture.md（設計）の順に読む
2. 移すもの・参考にするものは隣の `..\hdt-plugin-hsbg-kansousen`（以下「プラグイン」）にある。ログの仕様はプラグインの docs/log-schema.md が正本（M6 でこちらへ移す）
3. 作業の区切りで、変更したファイルと推奨するコミットメッセージを報告して止まる。コミットは許可を得てから

## 現在の状態

- 2026-11-19: リポジトリを作った。調査（docs/research.md）、設計の案（docs/architecture.md）、性能の試作（`spike/powerlog-bench/`）、
  M0 の退避スクリプト（`scripts/backup-powerlogs.ps1`）
- 2026-11-20: M1（Power.log を読み続けてゲームごとの中継ログに切り出す。実機確認済み）
- 2026-11-20: M2（Power.log から状態と出来事を作る）。プラグインの記録 236 ゲームと答え合わせ済み。次は M3
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
- [x] ソリューションの骨組み（`HsbgKifu.sln`: `src/HsbgKifu.Core`（ライブラリ）、`src/HsbgKifu.Cli`（`hsbg-kifu.exe`、開発・確認用のコマンド）、
      `tests/HsbgKifu.Tests`（xUnit）。.NET 8、警告はエラー扱い）。検証コマンドは CLAUDE.md
- [x] Power.log を後ろから読み続ける（`Watch/LogTailer`: 共有モード、書きかけの行、名前の変更・短くなったとき。`Watch/KifuWatcher`: 新しいフォルダへの移動、
      起動直後で Power.log がまだ無いフォルダを待つ）
- [x] ゲームごとに切り出し（`Watch/GameSlicer`）、中継ログ（平文で追記 → 終わったら gzip）と付帯情報 `.slice.json`
      （日付つきの開始時刻、ゲーム番号と再接続かどうか、ビルド番号、complete / incomplete）を書く（`Watch/SliceStore`）。
      境目と置き場は docs/architecture.md「中継ログ」
- [x] 起動時に残っているログフォルダを読み、まだ切り出していないゲームを回収する（同じゲームの判定: ログフォルダ名 + CREATE_GAME の時刻）。
      `hsbg-kifu slice --logs <dir>` で M0 の退避先（.gz）も取り込める
- [x] テスト: 実データの代わりに、テストの中で組み立てた短いログ（`tests/HsbgKifu.Tests/TestLogs.cs`）で、切り出し・BG 以外・途中で切れたログ・
      サーバー側だけの COMPLETE・書きかけからの再開・名前の変更・新しいフォルダ・Hearthstone が落ちたとき・退避した .gz を試す（23 件）
- [x] 実データでの確認（2026-11-20、docs/research.md §5「M1 の実装での測定」）: 今の Logs（5 起動分）から 21 ゲーム、M0 の退避先から残りの 11 ゲーム
      （重複なし）でプラグインの記録 31 件 + 11/19 の 1 件と一致。中継ログの `GameState.` の行は、プラグインの power.log.gz と行末の空白を除いて同じ。
      実際の Power_old.log を追記しながら `watch` で追った結果と、起動直後に読み直した結果が、まとめて切り出した結果とバイト単位で一致
- [x] 実機確認: `hsbg-kifu watch` を起動したまま Hearthstone で 1 ゲーム遊び、終了する（2026-11-20 00:14 のゲーム、ゲーム番号 2017）。
      Hearthstone の動作に問題なし。起動時に残っていた 5 フォルダから 21 ゲームを回収し、起動した Hearthstone の Power.log ができて 0.1 秒以内に後を追い始め、
      画面側の COMPLETE（00:41:35.28）から 0.5 秒以内に complete で閉じた（367,949 行）。終了後の Power_old.log からまとめて切り出した結果とバイト単位で一致、
      `GameState.` の行はプラグインの power.log.gz（183,423 行）と行末の空白を除いて同じ

M3 で決めること: 中継ログ（`.slices\`）と記録の JSON の置き方。hsbg-kansousen の `powerlog.power_log_for` は JSON と同じ名前の `.power.log.gz` を探す

### M2. Power.log から状態と出来事を作る（Parse）
- [x] Entity の状態の組み立て（`Parse/PowerLogParser`・`PowerGame`・`Entity`。`FULL_ENTITY` / `SHOW_ENTITY` / `CHANGE_ENTITY` / `HIDE_ENTITY` / `TAG_CHANGE`、
      プレイヤー名 → `PlayerID`）。タグの名前と番号は HearthDb の `Enums.cs`（MIT）を写した（`ThirdParty/HearthDb/`、THIRD_PARTY_NOTICES.md）
- [x] 自分のプレイヤーの判定（Bob 役 = `BACON_DUMMY_PLAYER` でない方。ヒーロー選択の `ChoiceType=MULLIGAN` の `Player=` とも一致。再接続でも分かる）
- [x] 出来事（`PowerGameListener`）: ゲーム開始・ヒーロー選択（選択肢）・ターン（ショップの始まり / 終わり）・戦闘開始（`3533` / `2022`）・
      ダメージ（`PREDAMAGE` と `META_DATA DAMAGE`）・ゲーム終了・再接続（`StartedMidGame`）
- [x] `GameState` と `PowerTaskList` のどちらを使うか: `GameState`（HDT と同じ。docs/research.md §3）
- [x] テスト 8 件（`PowerLogParserTests`、合計 31 件）。実データの答え合わせ `kifu-dev verify-parse`（`tools/HsbgKifu.DevTools`）:
      中継ログ 22 ゲーム・プラグインの power.log.gz 236 ゲームで、ヒーロー・順位・ターン数・各ターンのティア / HP / アーマー / ダメージが一致
      （違いは、プラグインの power.log.gz が途中で切れていた 1 ゲームだけ。docs/research.md §3.1）

M2 で分かったこと（M3 で使う）:
- damage_dealt / damage_taken は「戦闘の結果として生じたダメージ」= PREDAMAGE（2 回目を除く）を、戦闘開始から次のショップの始まりまで数える。
  アイスブロックで無効になっても数える（プラグインと同じ。記録の形は変えない）。ショップ中のダメージ（HP で払う購入・プレイ）は含めない
- 記録の候補（今は記録しない。記録するなら schema_version を上げる）: 戦闘のダメージが防がれたこと（アイスブロック。META_DATA DAMAGE が無い戦闘で見分けられる）、
  ショップ中に HP で払ったダメージとその巻き戻し（ソウル・リワインダーなど）

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
