# TASKS.md

今どこにいて、次に何をするか。完了したら `[x]` にし、判明したことは docs に追記する。

## 新しいセッションの始め方（Claude Code 向け）

1. `CLAUDE.md` → この `TASKS.md` → docs/research.md（調査）→ docs/architecture.md（設計）の順に読む
2. 移すもの・参考にするものは隣の `..\hdt-plugin-hsbg-kansousen`（以下「プラグイン」）にある。ログの仕様は 2026-11-21 にこちらへ移した（docs/log-schema.md が正本）
3. 作業の区切りで、変更したファイルと推奨するコミットメッセージを報告して止まる。コミットは許可を得てから

## 現在の状態

- 2026-11-19: リポジトリを作った。調査（docs/research.md）、設計の案（docs/architecture.md）、性能の試作（`spike/powerlog-bench/`）、
  M0 の退避スクリプト（`scripts/backup-powerlogs.ps1`）
- 2026-11-20: M1（Power.log を読み続けてゲームごとの中継ログに切り出す。実機確認済み）
- 2026-11-20: M2（Power.log から状態と出来事を作る）。プラグインの記録 236 ゲームと答え合わせ済み
- 2026-11-21: M3（中継ログから記録の JSON を作る。常駐の監視と作り直しのコマンドにつないだ。実機確認済み）
- 2026-11-21: M4（MMR、ロビーの種族、再接続のつなぎ。実機確認済み）。M5 の常駐アプリ（実機確認済み）、M6 の仕様の正本の移動。
  M7 の配布の zip（.NET 10。zip から入れ直して実機確認済み）。GitHub に push した。
- 2026-11-22: v0.1.0 をリリースした（M7 完了）。M3.5 のアイスブロック（schema 1.10.0）を実装し、プラグインの power.log.gz で答え合わせ済み、実機確認済み。v0.2.0 をリリースした（hsbg-kansousen 0.4.0 の後）
- 2026-11-23: v0.2.1（発見の取り逃しの修正）と v0.3.0（M8: すべての選択肢を記録する、schema 1.11.0）をリリースした
- 2026-11-23: M9（一時ログ = 中継ログの整理）を実装し、実機確認済み。v0.4.0 をリリースした（schema 1.11.0 のまま）
- 2026-11-25: 37.2（2026-12-01）の時渡りに備えて、時渡りと 2 つ目のヒーローパワーのログを調べた（docs/research.md §3.5・§3.6）。M10・M11 を足した
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
- [x] プラグインの `Logic/`・`Model/`・`Output/` とテストを移した（net8.0、Newtonsoft.Json は NuGet。`PartialLogJournal` は不要なので移していない。
      `GameLogWriter.WriteAtomic` は置き換え先を一瞬開いているものがあると失敗したので、File.Move の上書き + やり直しにした）
- [x] プラグインの `Capture/` を `GameRecorder`（PowerGameListener）などに書き直した。対応は docs/architecture.md「記録を作る」
- [x] カードの情報を HDT の CardDefs.base.xml から読む（`Cards/CardDb`。HearthDb の `Utils.cs` も写した）
- [x] 中継ログから記録を作り直すコマンド（`hsbg-kifu rebuild`）。常駐の監視（`watch`）は中継ログを書き終えるたびに記録も書く。同じゲームの別の記録があれば書かない
- [x] ショップの行動のうち購入・売却・リロール・スペル・悪夢の発見・ヒーローパワーは、自分の PLAY ブロックから数える（プラグインの推定より正確。docs/research.md §3.2）
- [x] 答え合わせ（`kifu-dev verify-json`。docs/research.md §3.2）: 中継ログ 22 ゲームで、違うのはショップの並び（プラグインは演出の途中を記録していた）と
      ゴールド獲得（どちらも推定）だけ。プラグインの power.log.gz 236 ゲームでは、ほかに古い版のプラグインの書き方・今の CardDefs での種族の違い・プラグインの取りこぼし。
      ショップの行動の回数は 2,848 ターンすべて PLAY ブロックの正解と一致（プラグインは購入 207・売却 494・リロール 276 ターンなどで外れていた）。
      `hsbg validate` で JSON Schema に合格、`hsbg summarize` も作れる
- [x] テスト: GameRecorderTests / KifuWriterServiceTests（合計 138 件）。実際の Power_old.log を追記しながら `watch` で 7 ゲームの記録ができ、作り直したものとバイト単位で一致
- [x] 実機確認（2026-11-21 15:37 のゲーム、ゲーム番号 3900）: `hsbg-kifu watch` を起動したまま 1 ゲーム遊び、ゲームの終わりから 1 秒以内に記録ができた
      （complete、11 ターン、4 位）。プラグインの記録と 4,524 項目中 4,479 項目が一致し、違いはショップの並び 45 項目だけ（ターン 11 の最後に足されたカードを、
      直前のリロール後の並びに含めた）。ショップの行動の回数は全ターン PLAY ブロックの正解と一致（プラグインは購入で 2 ターン外れ）。`hsbg validate` に合格
- [x] 起動時に、書き終えた中継ログのうち記録が無いもの（記録を書く前に止まったなど）から記録を作る（`KifuWriterService.CatchUp`）。
      実機確認で、前回までに切り出した 22 ゲームの記録が作られていないことに気付いて足した

M3 で分かった、後で決めること:
- ゴールド獲得（売却以外で増えたゴールド）は推定のまま。売却の回数が正確になったので、売却で戻ったゴールド（BACON_SELL_VALUE）を引く形にできるかもしれない
- 過去のゲームを作り直すと、今の CardDefs の種族・名前になる（パッチで変わったカード）。プラグインの記録がある過去のゲームは作り直さない（二重記録の防止で書かない）

### M3.5. アイスブロックで防がれた戦闘を記録する（2026-11-20 の要望。優先度は高くない）
M3 で記録の JSON を作れるようになってから。記録の形が変わる（`schema_version` を上げる）ので、M6（仕様の正本をこちらへ移す）とまとめて hsbg-kansousen に反映する。

- 目的: アイスブロックで「防いだ」「防がれた」戦闘が分かるようにする。それで順位が上がったなら、アイスブロックの価値がとても高かったことになる（分析で大きな情報）
- 分かっていること（docs/research.md §3.1）: アイスブロックは秘策 `TB_Bacon_Secrets_12`（装飾品 Safety Patch `BG35_MagicItem_820` などで得る）。
  発動した戦闘では、ヒーローに PREDAMAGE は立つが META_DATA DAMAGE が出ず、HP が減らない。秘策の Entity がその戦闘で GRAVEYARD に移る。
  236 ゲームで自分の 6 件を確認（6 件とも次のターンの HP が変わらない）
- [x] 要検証だった相手のアイスブロック: 見分けられる（2026-11-22、docs/research.md §3.1「アイスブロックの持ち主」）。相手のアイスブロックは戦闘ごとに複製され、
      複製の `CONTROLLER` タグが相手役のプレイヤーになる。行の `[... player=N]` は作られたときの持ち主のままなので、自分に見えていた
- [x] キー（schema 1.10.0、hsbg-kifu 0.2.0）: `combat.ice_block` と `combat.opponent_ice_block`（真偽値。戦闘を見られなければ null）。
      防いだダメージは damage_taken / damage_dealt にあるので、真偽値にした。events.jsonl の `combat_end` にも。`GameRecorder.OnTagChanged`
- [x] 答え合わせ: プラグインの power.log.gz 242 ゲーム（2,917 戦闘）から作り直し、自分 6 件・相手 1 件がすべて立ち、ほかの戦闘では立たなかった
      （相手の複製があって発動しなかった戦闘は 8 ゲーム）。テスト `GameRecorderTests.RecordsWhichIceBlockTriggered`
- [x] hsbg-kansousen: 仕様の写し、`KNOWN_SCHEMA_VERSION` 1.10.0、combats の列 `ice_block` / `opponent_ice_block`（`CACHE_FORMAT` 13）、
      要約の戦闘の列に「(my Ice Block)」「(opp. Ice Block)」、Key moments、log-caveats.md
- [x] 実機確認（2026-11-22）: 0.2.0 の常駐アプリで 1 ゲーム。記録は schema 1.10.0・plugin_version 0.2.0 で、各ターンの戦闘に結果と
      `ice_block` / `opponent_ice_block` が入った（このゲームではアイスブロックは発動せず false）
- [x] リリース（2026-11-22）: hsbg-kansousen 0.4.0（1.10.0 を読める hsbg）を先に出してから、hsbg-kifu 0.2.0（zip の SHA-256 は `d46f9cf5…ab58`）。
      hsbg-kansousen を先に push したため、その CI の `schema` ジョブが古い hsbg-kifu の main と比べて一度失敗した（再実行で緑）。push の順番を CLAUDE.md に書いた
- [ ] （任意）保存済みのログを power.log から補う `hsbg backfill-*` にアイスブロックを足す

### M4. HDT のファイルから補う値（Meta）
- [x] MMR: `BgsLastGames.xml` を開始時刻で照らす（`Capture/BgsLastGames`）。常駐中は後の値を最長 60 秒待って書き直す。XML に残る 18 ゲームでプラグインの記録と一致。
      直近の 18 ゲームほどしか残らないので、古いゲームの作り直しでは入らない
- [x] ゲーム番号と再接続: 同じゲーム番号の中継ログ（2 つ目以降が reconnecting）を 1 つの記録にまとめ、見られなかった戦闘は HP + アーマーの差から推定する。
      10/17 のゲームを 2 つの中継ログに分けて試し、16 ターンすべてプラグインの記録と一致
- [x] ロビーの種族: ショップに並んだミニオンの種族の上位 5 つ（`Capture/LobbyMinionTypes`。無種族・全種族は数えない。カードの一覧は持たない）。
      Power.log にも、log.config に Gameplay / Zone / Net / Bob を足したログにも出なかった。237 ゲームすべてプラグインの記録（HDT のメモリの値）と一致
- [x] 未完了のゲーム（Hearthstone が落ちて戻らなかった）を `status: "incomplete"` で書く（M3 の `GameRecorder.Finish`）
- [x] テスト: LobbyAndMmrTests、ReconnectRecorderTests、MMR を待つ・入れるテスト（合計 146 件）
- [x] 実機確認（2026-11-21 18:05 のゲーム、ゲーム番号 4026）: 記録に順位 5・MMR 8334 → 8320（ゲームの画面と一致）・ロビーの種族が入り、
      プラグインの記録と順位・MMR（前・後・差）・ロビーの種族が一致。HDT はゲームの終わりと同時に後の MMR を書いていて、待たずに入った。
      ほかの違いはショップの並び 50 項目だけ。ショップの行動の回数は全ターン PLAY ブロックの正解と一致（プラグインは購入・リロールで 1 ターンずつ外れ）
- [x] 後の MMR を待つのは、終わってから 5 分以内のゲームだけにした。実機確認の前の起動で、起動時の回収が作った古いゲーム（BgsLastGames.xml に無い）まで
      60 秒待ち、値が無いのに「MMR を記録に入れました（- → -）」と出ていた（記録の中身は null と notes で正しかった）。取れなかったときは「取れませんでした」と出す
- [x] HDT と一緒に使う前提を README・CLAUDE.md に書いた（MMR は HDT の BgsLastGames.xml にしか無い）。HDT が無くて MMR・カードの情報・ロビーの種族が
      取れない記録も JSON Schema に合格することをテストにした

### M5. 常駐アプリ（App）
- [x] タスクトレイ常駐（`src/HsbgKifu.App`、WinForms の NotifyIcon。exe は `HsbgKifu.exe`。二重起動しない）。
      メニュー: 状態・最後の記録・記録のフォルダを開く・動作記録を開く・設定・終了。通知は問題があるときだけ。
      アイコンは起動時に描く濃い青と金の「BG」（Hearthstone の雰囲気の配色だけ。ロゴの形や絵は使わない。2026-11-21 のユーザーの感想で「棋」から変えた）
- [x] 設定（`%APPDATA%\hsbg-kifu\config.json`、`Config/KifuConfig`）と設定画面（`SettingsForm`）。出力先の検証はプラグインの `Config/OutputDirectory` を移した。
      保存すると監視を作り直す。監視と書き込みの組み立ては `KifuService`（CLI の watch も使う）
- [x] 動作記録 `%APPDATA%\hsbg-kifu\kifu.log`（5 MB を超えたら `.old` に移す）
- [x] Windows へのサインイン時に起動する設定（設定画面のチェック。既定 OFF。`HKCU\...\Run` の値だけで、config.json には入れない）
- [x] log.config で Power.log が有効か確かめ（`Config/LogConfigCheck`。読むだけ）、無効なら起動時に通知する
- [x] 実機確認（2026-11-21、2 ゲーム）: CPU・メモリに問題なし。設定の保存で監視をやり直し、終了・起動も正常。動作記録にエラー・警告なし。
      2 ゲームとも順位・MMR（前・後）・ロビーの種族がプラグインの記録と一致し、ショップの行動の回数は 24 ターンすべて PLAY ブロックの正解と一致。
      最初の起動は既定の出力先（Documents\HSBG-Logs）で、残っていたログフォルダの 9 ゲームを .slices に切り出した（記録は二重記録の防止で書かなかった）。
      プラグインと並べて試す間だけ起きることなので、既定の出力先は今のまま（M7 の切り替え後は正しい置き場になる）
- [ ] Hearthstone をゲームの途中で終了したときの扱いの確認（次の実機確認のついでに）

### M6. ログの仕様と hsbg-kansousen
- [x] ログの仕様（`docs/log-schema.md`、`schema/game-log.schema.json`）の正本をプラグインからこちらへ移した（2026-11-21）。
      何で作ったかは既存の `generator.plugin`（"hsbg-kifu"）で分かるので、キーは足さず schema 1.9.0 のまま。
      仕様に「記録を書いたアプリ」の節を足し、プラグインの記録との違い（game_id・shop_offers・行動の回数・ロビーの種族・MMR の出どころ）を書いた。
      `scripts/check_cli_sync.py` をプラグインから移し、CLAUDE.md に「ログの形を変えたとき」の手順を移した
- [x] hsbg-kansousen の参照先をこのリポジトリに変えた: 仕様の写し、CI の `schema` ジョブの比較先（`kumatch/hsbg-kifu` の main。リモートが無い間は警告だけ）、
      CLAUDE.md・`docs/release.md`・README の「記録アプリと hsbg の版」、分析の注意（`log-caveats.md` に hsbg-kifu の記録の違い）、
      「新しいプラグイン」の表示を「新しい記録アプリ」に
- [x] プラグインのリポジトリ: 仕様は schema 1.9.0 時点の写しとして凍結（先頭に注記）、`check_cli_sync.py` を消し、CLAUDE.md・docs・CI を合わせた

### M7. 配布と切り替え
- [x] 配布の形を決めた（2026-11-21、docs/release.md）: .NET ランタイムを同梱した 1 つの exe（self-contained、single-file）を zip で配る。
      インストーラと電子署名は見送り、SmartScreen の警告は README で説明する
- [x] .NET 8（2026-11-10 にサポート終了）から .NET 10（LTS）に移した。`global.json` で SDK 10.0.x。テスト 165 件に加え、
      中継ログ 27 ゲームの答え合わせ（verify-json）の出力が .NET 8 のときと 1 文字も違わないことを確かめた
- [x] `scripts/package.ps1`: テスト → exe（約 52 MB）→ 手元のパス（ユーザー名）が DLL に残っていないかの確認（`Directory.Build.props` の PathMap）→
      zip（約 45 MB。README.txt・LICENSE・THIRD_PARTY_NOTICES・.NET と Newtonsoft.Json のライセンス文）と SHA-256
- [x] zip から入れ直す実機確認（2026-11-21）: 別のフォルダに展開して起動し、動作記録がこれまでどおり出ること、
      1 ゲームの MMR とメニューの「最後の記録」がゲームの数値と一致することを確かめた
- [x] GitHub にリポジトリを作って push した（2026-11-21、public の `kumatch/hsbg-kifu`。作成と push はユーザー）。
      hsbg-kansousen の CI の `schema` ジョブは、これでこのリポジトリの main と比べられる
- [x] hsbg-kansousen の README の「はじめかた」（プラグインを入れる手順）を hsbg-kifu に書き換えた（冒頭・しくみの図・動作環境も。
      プラグインの記録も読めることと、2026-11-30 以降は動かないことを添えた）。分析のスキルの「tracker plugin」も直した
- [x] 切り替えの手順を README に書いた（「HDT プラグインから切り替える」）: プラグインを外してから使う。
      両方が同じフォルダに書くと、hsbg-kifu が先に書いた記録の後にプラグインも書き、同じゲームが 2 つになる
      （hsbg-kifu の二重記録の防止は、すでにある記録を見て自分が書かないだけ）
- [x] README を利用者向けに組み直した（2026-11-21）: 「入れ方と使い方」（zip、SmartScreen、更新と削除）、`.power.log.gz` に BattleTag が入ること、
      非公式であること。ソースからのビルドとコマンドラインの手順は `docs/development.md` に移した
- [x] 最初のリリース v0.1.0（2026-11-22。タグ `v0.1.0`、zip の SHA-256 は `06a268eb…4251`。Releases のページはユーザーが作成）。
      プラグインの README の冒頭に「保守を終えた。2026-11-30 以降の HDT では動かない。hsbg-kifu へ」と書いた

### M7.1. 発見の 3 択の取り逃しを直す（2026-11-22、hsbg-kansousen-workspace の分析で指摘。0.2.1）
- [x] 原因: 3 択が出てから選び終わるまで区切りが来ないと、3 択が見えず、手札に入ったカードを発見と判断できなかった（docs/research.md §3.4）。
      トリプル報酬・ヒーローパワーの `discover` と、悪夢の発見の `offered` が抜けていた
- [x] 修正: 3 択が出た時点で候補を覚える（`GameRecorder.OnChoices` → `ShopActionTracker.NoteOffered`）。ログの形は変えない（schema 1.10.0 のまま）。
      テスト `GameRecorderTests.RecordsATripleRewardDiscoverWhoseChoiceEndsBeforeTheNextBatch`（直す前は失敗することを確認）
- [x] 照合: `HSBG-Kifu-Test` の 28 戦を作り直し、トリプル報酬 109 / 110 件、ヒーローパワー 12 / 12 件がプラグインの記録と一致（残る 1 件は §3.4）
- [x] 0.2.0 で書いた 2026-11-22 00:23 のゲームをスクラッチに作り直して比べた: 主ログの JSON は同じ、events.jsonl に T13 の `discover`（トリプル報酬）が増え、
      T10 の `dark_gift` に `offered` が入った。置き換えるかはユーザーが決める
- [x] 実機確認（2026-11-22 22:56 のゲーム）: トリプル報酬 4 回の 3 択がどれも区切りの無い並びで、4 件とも `discover` に入った。`dark_gift` の `offered` も入った
- [x] 0.2.1 のリリース（2026-11-22。schema は 1.10.0 のままなので hsbg-kansousen は出さない）。0.2.0 で書いたログの置き換えは、ユーザーの判断で行わない

### M8. すべての発見の 3 択を記録する（2026-11-22 の要望）
- 目的: 何が提示されて何を選んだかを、トリプル報酬・ヒーローパワー以外（ミニオンの雄叫び、呪文、トリンケット、クエストなど）も含めて残す。
  理想は 3 択が妥当だったかを分析できること（提示されたカードのティア・種族・攻撃力・体力）
- [x] 設計（2026-11-22。形はユーザーから任された）: 選択の出来事（3 択の `Source` と、選ばれた Entity）から直接記録する。主ログに `turns[].choices[]`（source、source_card、
      offered = 候補のティア・攻撃力・体力、chosen_index）、events.jsonl の `discover` に `source_card_id` と `source` の `trinket` / `card`。schema 1.11.0、hsbg-kifu 0.3.0。
      トリンケットの選択はターン数ではなく候補のカードの種類で決める（ユーザーの指摘: 5・8 ターン目に増えるヒーロー、7 ターン目に早まる上級がある）
- [x] 実装: パーサが `Source=` と選ばれた Entity を読む（`EntityChoice.SourceId` / `ChosenIds`）、`GameRecorder.OpenChoice` / `OnChoiceMade`、`Capture/ChoiceSource`。
      手札の差による `discover` の行はやめた。テスト（トリプル報酬・カード・トリンケット 2 種・悪夢の発見・ヒーローパワーが作った悪夢の発見・すぐ手札から消える・選ばれない）
- [x] 照合: `HSBG-Kifu-Test` の 28 戦で選択肢 454 回（中継ログと一致）、トリプル報酬 110 件（プラグインと一致）、0.2.1 の `discover` の行はすべて残る（docs/research.md §3.4）
- [ ] hsbg-kansousen の対応（仕様の写し、`KNOWN_SCHEMA_VERSION` 1.11.0、`choices` / `choice_offers` の表、要約、log-caveats）
- [x] 実機確認（2026-11-22 23:52 のゲーム、ゲイルウィング）: 選択肢 12 回（ヒーローパワーの行き先の選択 4・行き先に着いたときの発見 2・悪夢の発見 3・トリプル報酬 2・
      トリンケット 1）に候補と選んだものが入った。行き先の発見は `source` が `hero_power`、`source_card` が行き先のカード（Eastern Plaguelands）で見分けられる
      （hsbg の要約の書き方で対応。ログの形は変えない）
- [x] リリース（2026-11-23）: hsbg-kifu の main を先に push し、hsbg-kansousen 0.5.0 → hsbg-kifu 0.3.0 の順に出した（zip の SHA-256 は `b678a202…c489`）。
      hsbg-kansousen の CI は最初の実行で緑

### M9. 中継ログ（`.slices`）を整理する（2026-11-23 の要望）
- 背景（2026-11-23 に確認）: 中継ログの `.power.log.gz` は 1 ゲーム約 2.6 MB（`PowerTaskList.` の行も含む）で、消す仕組みが無い（README・設定画面で「常に残す」と書いている）。
  記録の横の `.power.log.gz`（約 1.2 MB。`GameState.` の行だけ）と中身が重なり、既定の設定で 1 ゲーム約 3.8 MB になる。
  プラグインは中継ログを持たず、記録の横の写し（約 1.2 MB、これも消さない）だけだった。動作記録 `kifu.log` は 5 MB + `.old` で上限があるので何もしない
- 消してよい理由: 記録づくり（`PowerLogParser`）が読むのは `GameState.` の行だけで、記録の横の `.power.log.gz` にそろっている。
  hsbg-kansousen の分析・補完も記録の横の `.power.log.gz` を読む。`.slice.json` を残せば、記録の横の `.power.log.gz` から作り直す機能も後から作れる（今は作らない）
- 決めたこと（2026-11-23、ユーザーと）:
  - 自動の整理: 設定に保持期間を足す。選択肢は 30 日（既定）・60 日・90 日・消さない。期間はゲームの開始時刻（`SliceMeta.StartTime`）から数える
  - 設定画面に「中継ログを整理する」ボタン: **期間に関係なく**、消してよい中継ログを全部消す。押したら件数と空く容量を見せ、確認してから消す
  - 消してよい中継ログ:
    (a) 対応する記録の横に `.power.log.gz` があるもの（再接続で分かれた中継ログは 1 つの記録に対応させる。
        二重記録の防止で書かなかったゲームは、そのときに見つけたほかの記録（プラグインの記録など）の横の `.power.log.gz` を見る）
    (b) 記録にならなかったゲーム（ターン 1 の前に終わったなど。ユーザーの判断で、ほぼ意味の無いデータとして一緒に消す）
  - 消さないもの: 書いている途中（recording）、始まってから 3 時間以内のもの（実装で「今追っているログフォルダのもの」から変えた。下の設計）、
    記録をまだ作っていないもの・作るのに失敗したもの、
    記録の横に `.power.log.gz` が無いもの（`write_power_log` が OFF のときに書いた記録など）
- [x] 設計（docs/architecture.md「中継ログ」）:
  - 消すのは中継ログの `.power.log.gz` だけにし、`.slice.json` は「整理済み」の印（`pruned_at`）を付けて残す（`SliceMeta.CurrentFormat` を 2 にした）。
    全部消すと、起動時の回収（`KifuWatcher.CatchUp`）が Hearthstone の Logs に残っているフォルダ（6 起動分ほど）から切り出し直してしまう。
    `SliceStore.IsDone` は整理済みも済みとして扱い、`Finished` には含めない
  - (b) を「まだ作っていない・失敗した」と見分けるため、`KifuWriterService` が記録を作ったら `.slice.json` に結果を書く
    （`outcome`: `recorded` / `duplicate` / `not_recorded` と、`record`: 記録の出力先からの相対パス）。印の無い前の版の中継ログは、同じ game_id の記録で判断する。
    起動時の記録の回収（`KifuWriterService.CatchUp`）は、`not_recorded` と、`record` の記録があるものをもう作らない
    （これまでは起動のたびに作り直して「同じゲームの記録がすでにある」「記録しません」を動作記録に書いていた）
  - 「今追っているログフォルダのもの」は消さない、の代わりに「始まってから 3 時間以内（再接続でつなぐ範囲 `ReconnectWindow`）のもの」は消さないにした。
    ボタンで消した直後に Hearthstone が落ちて再接続すると、前の中継ログが無くて 1 つの記録にまとめられないため。
    今追っているログフォルダのゲームを消しても、整理済みは済みとして扱うので切り出し直しはしない
  - 整理は監視と同じところで動かす。ボタンも動いている `KifuService` に頼み、記録の書き込み・後の MMR の書き直しとぶつからないようにする
  - 自動の整理が動くとき（2026-11-23、ユーザーと確認）:
    1. 監視を始めたとき（起動時の回収と `KifuWriterService.CatchUp` の後。`KifuWatcher.CaughtUp`）。常駐アプリの起動（サインイン時の起動を含む）と、設定の保存で監視をやり直したとき。
       保持期間を変えて保存すると、その場で新しい期間で整理される
    2. ゲームの記録を書いた後（消えるのは今のゲームではなく、開始から保持期間を過ぎたゲーム）。起動時の回収の途中では整理せず、終わってから 1 回
    期間を過ぎた瞬間には消さず、次の 1・2 で消す（日ごとのタイマーは足さない。遊べば記録のたびに整理され、遅れても数日分が残るだけ）
  - 消したときは動作記録に「中継ログを N 件整理しました（約 X MB）」を 1 行書く。消すものが無いときは書かない（動作記録を増やさない）
  - `hsbg-kifu rebuild` は整理済みの中継ログを飛ばし、その件数を出す
  - ログの形（schema）は変わらないので、hsbg-kansousen の作業は無い
- [x] 実装（0.4.0）: `Config/KifuConfig`（`slices_retention_days`）、`Watch/SliceStore`（記録との対応・整理済み。監視と設定画面の両方から触るので鍵をかけた）、
      `KifuWriterService.Prune`（記録の一覧を持つ `KifuWriterService` の鍵の中で判断して消す）、`KifuWatcher.CaughtUp`、`KifuService`（自動の整理と `PruneSlicesAsync`）、
      `SettingsForm`（「中継ログを残す期間」と「中継ログを整理する...」）、Cli の `rebuild`
- [x] テスト（`SlicePruneTests.cs` の 13 件、合計 189 件）: (a)・(b) を消す、期間の内と外、消さないもの（recording・3 時間以内・記録が無い・横の `.power.log.gz` が無い・記録を消した）、
      再接続の中継ログ、二重記録の防止で書かなかったゲーム、印の無い前の版の中継ログ、整理済みを起動時の回収で切り出し直さない・作り直しに出さない、
      `CatchUp` が印のあるものを作り直さない、設定の読み書き（既定 30 日・消さない・前の版の config.json）、
      `KifuService` で監視を始めたときと記録を書いた後に整理が動く・消さない設定では動かない・止まっていてもボタンの整理ができる、動作記録の 1 行（消したときだけ）
- [x] 実データ（2026-11-23。`Documents\HSBG-Logs` の中継ログ 15 ゲームと 11 月の記録 78 件をスクラッチに写して試し、消した）: 印の無い状態では自分の記録がある 3 件が消せる。
      起動時の記録の回収で 12 件に `duplicate`（プラグインの記録）が付き、2 回目の回収は何も作らない。30 日の整理は 0 件（どれも新しい）。
      ボタンで 15 件（約 38 MB）を消し、記録の横の `.power.log.gz` 78 件は残った。その後、Hearthstone の Logs（6 フォルダ）を起動時の回収と同じく切り出しても、切り出し直したゲームは 0
- [x] 文書: README（中継ログを消すこと。書くファイルの表は「記録の出力先」の中なので変えず、「自分で消すのは中継ログだけ」と書いた）、
      README と配布の README.txt の「更新」に、0.3.x 以前から更新して最初の起動で 30 日より古い中継ログがまとめて消えることと、残す方法（config.json に 1 行）。
      設定画面の説明文、docs/architecture.md「中継ログ」、docs/development.md（`watch` も整理する・`rebuild` が整理済みを飛ばす）
- [x] 利用者に見せる名前を「中継ログ」から「一時ログ」に変えた（2026-11-23 のユーザーの提案。設定画面・README・配布の README.txt・動作記録の文言。
      ずっと残す必要が無いと分かるように）。docs・TASKS・コードの名前とコメントは「中継ログ」のまま（docs/architecture.md「中継ログ」に対応を書いた）
- [x] 実機確認（人間に依頼）: 設定画面で期間を選んで保存できる。ボタンで件数と容量が出て、消した後も記録・分析（hsbg）に問題が無い。
      常駐を起動し直しても整理済みのゲームが切り出し直されない
      - 2026-11-23（「中継ログ」の表記の版）: ボタンで 15 件（約 38.0 MB）を消し、動作記録に 1 行。直後の 1 ゲーム（14:41、ゲーム番号 6229）は記録・events.jsonl・
        記録の横の `.power.log.gz` ができ、中継ログは `outcome: recorded` で残った。この起動で 12 件に `duplicate` の印が付いた
        15:16 に起動し直すと、「同じゲームの記録がすでにある」も切り出し直しも出なかった
      - 「一時ログ」の表記の版（0.4.0）: 設定画面の文言・期間の保存・版の表示を確かめた。zip を展開した exe でもアイコン・メニューの版・設定画面・1 ゲームの記録を確かめた
- [x] リリース 0.4.0（2026-11-23。0.3.0 は M9 の前にリリース済みだったので版を上げた。schema は 1.11.0 のままなので hsbg-kansousen は出さない）:
      作業ブランチを main に載せ直して取り込み（fast-forward）、main で zip を作った（SHA-256 は `46ad3179…9469`）。タグ `v0.4.0`、Releases のページはユーザーが作成

### M10. 2 つ目のヒーローパワーを記録する（2026-11-25 の要望）
- 背景: ウォーゲンの王ゲンは 4 ターン目にヒーローパワーを 2 つ発見し、以後 2 つを持って戦う。37.2 の時渡りの「○○の力」でも 2 つ目のヒーローパワーが手に入る。
  0.4.0 の記録では、手に入れたヒーローパワーも、どちらを使ったかも分からない（docs/research.md §3.6）。今のバージョンで起きることなので、M11 より先にする
- 決めたこと（2026-11-25、ユーザーと）:
  - `turns[].hero_powers[]`（新しいキー）: ショップを終えた時点で持っていたヒーローパワー。`{slot, card_id, name, dbf_id}`、`slot` は `main` / `additional`。
    ヒーローの `HERO_POWER_ENTITY` と `ADDITIONAL_HERO_POWER_ENTITY_1` から読む。理由を問わず（ゲン・時渡り・育つヒーローパワーなど）変化が分かる
  - `player.hero_power` は今までどおり最初のヒーローパワー（意味を変えない）
  - events.jsonl: `hero_power` の行に `source_card_id`（使ったヒーローパワー）を足す（`card_id` は 1.10.x 以前に「発見したカード」の意味で使っていたので使わない）。
    増えた・入れ替わったときに `hero_power_changed`（slot, card_id, 前の card_id）
  - メインのヒーローパワーは「PLAY にある最初の自分のヒーローパワー」ではなく `HERO_POWER_ENTITY` から引く（`GameRecorder` の 2 か所）
- [x] 実装（schema 1.12.0）: `Capture/HeroPowers`（メインと追加の枠を読む）、`turns[].hero_powers[]`（`Model/TurnRecord` の `HeroPowerRecord`）、
      events.jsonl の `hero_power_changed` と `hero_power` の `source_card_id`、`player.hero_power` と `Snapshots.Tick` に渡すメインを `HERO_POWER_ENTITY` から。
      ヒーローパワーがトリンケット・クエストに変わってなくなったとき（マリンなど）も `hero_power_changed`（card_id が null）を書く。
      `HERO_POWER_ENTITY` の無いログ（2026-09 のゲーム）は PLAY のヒーローパワーから決める
- [x] テスト（`GameRecorderTests` の 5 件、合計 200 件）: ゲンの流れ（1 回目が追加の枠、2 回目がメインと入れ替わる）、テロンの力の流れ（追加の枠だけ）、
      どちらを使ったか（`source_card_id`）、スキーマに合格、ヒーローパワーが 1 つのゲーム（`HERO_POWER_ENTITY` の有無）、トリンケットに変わってなくなる
- [x] 実データ（2026-11-25。プラグインの power.log.gz 257 ゲームを作り直した。出力はスクラッチ、終わって消した）:
      ゲンは T4 に追加の枠 `BG20_HERO_280p5` → メインが `TB_BaconShop_HP_020` に入れ替わり、使ったのは 3 回とも `TB_BaconShop_HP_020`。
      ミロクーは T5 に追加の枠 `BG25_HERO_103p`。`hero_power_changed` は 583 行（グエン 317、インゲ 114、ゲイルウィング 57 など毎ターン変わるヒーロー）。
      なくなる行はマリン・ボタンズ・`BG24_HERO_100p` のヒーローだけで、一瞬見えなくなるような余計な行は無い。2 つ持っていたターンは 19（バロフが効果で得た 1 回を含む）。
      `verify-json` のプラグインとの違いは変更前と同じ 80,257 件、新しいキー以外の記録と events.jsonl は 257 ゲームすべてで変更前と同じ
- [x] hsbg-kansousen（ブランチ `kumatch/pre-timewarp-support`）: schema と docs のコピー、`KNOWN_SCHEMA_VERSION` 1.12.0、`turns` の列（`CACHE_FORMAT` 15）、
      要約の「## Hero powers」、選択肢の「Hero power (X)」の判定、log-caveats、テスト
- [ ] 実機確認（人間に依頼。M11 と一緒に）

### M11. 時渡り（Timewarp）の買い物を記録する（2026-11-25 の要望。37.2 = 2026-12-01 から）
- 背景: 37.2 で時渡りが戻る（6 ターン目にミノール、9 ターン目にメジャー。クロナムで特別なカードを買う）。トリンケットはなくなるが、`trinkets` が空になるだけで形は変えない。
  0.4.0 は時渡りの買い物をヒーローパワーの発見として記録し、クロナムの額・値段・ミノールかメジャーかが残らない（docs/research.md §3.5。ミロクーのゲームで確認）
- 決めたこと（2026-11-25、ユーザーと）:
  - `turns[].timewarp`（新しいキー。時渡りに行ったターンだけ）: `kind`（`minor` / `major`）、`source_card`（連れて行ったヒーローパワー。全員が行く時渡りなら null）、
    `chronum`（`BACON_ALT_TAVERN_COIN`）、`offered[]`（候補 5 枚。`choices[].offered` の形に `cost` = `BACON_OVERRIDE_BG_COST`）、`bought[]`（買った順に `{index, cost}`）、`chronum_left`
  - `choices[].source` に `timewarp`: 出したものではなく、選択肢が出たときに `GameEntity` の `BACON_ALT_TAVERN_IN_PROGRESS=1` かで決める
    （ミロクーの記録は `hero_power` から変わる。前の版で記録したのは 1 ゲームだけ）
  - events.jsonl: 時渡りの選択では `discover` を出さず、`timewarp_buy`（card_id, name, cost, chronum_after）。始まりに `timewarp`（kind, chronum, offered[]）
  - 2 つ目のヒーローパワー（「○○の力」）は M10 の `turns[].hero_powers[]` に出る
- [x] ミロクーのゲームで確かめた範囲の実装（schema は M10 と同じ 1.12.0。2026-11-25 にユーザーと決めた: M10 と M11 は一緒にリリースする）:
      `turns[].timewarp`（`Model/TurnRecord` の `TimewarpRecord` / `TimewarpOffer` / `TimewarpBuy`。`CardSnapshot` は継承できるようにした）、
      `choices[].source` の `timewarp`（`ChoiceSource.InTimewarp`）、events.jsonl の `timewarp` / `timewarp_buy`（時渡りの選択では `discover` を書かない）。
      時渡りは最初の選択肢で始め（クロナム = `BACON_ALT_TAVERN_COIN`。そのころには付いている）、`BACON_ALT_TAVERN_IN_PROGRESS` が 0 になったときの
      `RESOURCES - RESOURCES_USED` を使い残しにする。`chronum_after` は持っていた分から値段を引いて出す（選んだ時点では `RESOURCES_USED` がまだ増えていない）。
      `kind` は候補のミニオンのティアの最大（5 以上ならメジャー）。37.2 のゲームで確かめるまでは目安
- [x] テスト（`GameRecorderTests` の 3 件、合計 203 件）: ミロクーの流れ（3 枚 → 買える候補だけの 2 回目 → クロナム 0 で終わる。ティア 3 でミノール、5 でメジャー）、
      通常のショップの記録（`gold_available`・`gold_spent`・`buys`・`sells`・`gold_gained`）に混ざらない、`discover` を書かない、スキーマに合格、時渡りの無いターン
- [x] 実データ（2026-11-26。257 ゲームを作り直し、M10 の版と比べた。出力はスクラッチ、終わって消した）: ミロクーの T5 は kind `minor`、クロナム 2、
      候補 5 枚の値段 2・2・1・1・1、買ったのはテロンの力（index 4）→ 時渡の訓練生（index 3）、使い残し 0。変わったのはこのゲームだけ
      （選択肢 2 件の source と、`discover` 2 行が `timewarp` 1 行と `timewarp_buy` 2 行に）。ほかの 256 ゲームは記録も events.jsonl も同じ
- [x] hsbg-kansousen: schema と docs のコピー、`timewarp_offers` の表（`CACHE_FORMAT` 16）、要約の「## Timewarp」、選択肢の kind `buy`、log-caveats
- [ ] 実機確認（人間に依頼。M10 と一緒に。37.2 の時渡りのゲームで）
- [ ] 37.2 のゲームで確かめる（docs/research.md §7 の時渡りの項目）。違っていれば直し、docs/research.md §3.5 に追記する

## 判明したこと・決めたこと

- 2026-11-19: 名前を hsbg-kansousen-recorder から hsbg-kifu に変えた（棋譜を取るアプリと、感想戦をする hsbg-kansousen という関係を名前に出す。
  hsbg-kansousen 専用に見える接頭辞をやめた）。C# の名前空間は `HsbgKifu`
- 2026-11-19: 新しい記録アプリは、このリポジトリで作って管理する。プラグインのリポジトリは資産として読み、必要なものを移す
- 2026-11-19: 記録は HDT とは別の常駐アプリで、Power.log から作る（docs/research.md §1）。記録の JSON はこのアプリが作り、形は今のプラグインと同じ（方針 A）
- 2026-11-23: 中継ログ（`.slices`）は「常に残す」をやめ、記録の横に `.power.log.gz` ができたものは保持期間（既定 30 日）の後に消す（M9）。
  古いゲームを作り直すことはあまり無く、分析は記録の横の `.power.log.gz` でできるため。動作記録 `kifu.log` は上限があるので今のまま
- 2026-11-23: docs/development.md は公開を前提に、ビルドの手順と開発用の道具の紹介だけにした。Cli と DevTools の使い方・注意は、引数なしで実行したときの表示に移した。
  Cli の `--out` は必須にした（既定の `Documents\HSBG-Logs` だと、試しただけで常駐アプリの記録のフォルダに書いてしまうため）
