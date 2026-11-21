# CLAUDE.md — hsbg-kifu

Claude Code がセッション開始時に読む、このリポジトリの規約です。詳細は `docs/`。ここには「毎回守ること」だけを書きます。

## このリポジトリは何か

Hearthstone Battlegrounds の 1 ゲームごとの記録を、Hearthstone の Power.log から作ってローカルの JSON に保存する常駐アプリ。
HDT プラグイン hdt-plugin-hsbg-kansousen（隣のフォルダ `..\hdt-plugin-hsbg-kansousen`）の後継で、HDT が 2026-11-30 にプラグインを止めるために作る。
記録（棋譜）は分析ツール hsbg-kansousen（`..\hsbg-kansousen`）などが読む。依存の向きは hsbg-kifu → 棋譜 → hsbg-kansousen で、このリポジトリは hsbg-kansousen に依存しない。記録の形（ログの仕様）の正本はこのリポジトリの `docs/log-schema.md` と `schema/game-log.schema.json`（2026-11-21 にプラグインから移した。形はプラグインと同じ）。

## 必ず読むドキュメント

- `TASKS.md` — 今やるべきタスク
- `docs/log-schema.md` — ログの仕様（これが hsbg-kansousen との契約。変えたら `schema_version` を上げる）
- `docs/research.md` — Power.log と HDT のファイルについて確認した事実
- `docs/architecture.md` — 設計

## 環境の前提

- OS: Windows 10/11。WSL は使わない。Claude Code は PowerShell から起動される。手順は PowerShell / Windows パスで書く
- .NET 8（常駐アプリは WinForms）。ビルドは `dotnet build`
- Hearthstone のログ: HDT の `%APPDATA%\HearthstoneDeckTracker\config.xml` の `HearthstoneDirectory` 配下の `Logs\Hearthstone_<日時>\Power.log`
  （終わった起動分は `Power_old.log`）。出力の設定は `%LOCALAPPDATA%\Blizzard\Hearthstone\log.config`
- HDT のデータ: `%APPDATA%\HearthstoneDeckTracker\`（`BgsLastGames.xml`、`CardDefs\`）。**読むだけ**
- **HDT と一緒に使う前提**（2026-11-21 にユーザーと決めた。README「HDT と一緒に使ってください」）: MMR は HDT の BgsLastGames.xml にしか無い
  （Hearthstone のログに出ない。メモリ読み取りはしない）。カードの情報（CardDefs）と Power.log の設定（log.config）も HDT が用意する。
  HDT が無くても記録は作れるようにし、取れない値は null / 空の配列にして notes に理由を書く。その記録も JSON Schema に合格させる
  （`GameRecorderTests.RecordWithoutMmrAndLobbyTypesIsValidAgainstTheSchema`）。HDT のファイルに頼る値を増やすときは、無いときの挙動とテストも一緒に作る
- プラグインの記録と power.log.gz（答え合わせに使う）: `%USERPROFILE%\Documents\HSBG-Logs`

## コーディング規約

- 名前空間 `HsbgKifu`。Power.log の解析・純粋ロジックは UI から分け、xUnit でテストする
- 例外は捕捉してログに書く。記録アプリが落ちても Hearthstone と HDT に影響させない
- Hearthstone と HDT のファイルは共有モードで開いて読むだけ。書き換えない
- ファイル書き込みはアトミックに（一時ファイル → 置き換え）
- ログの形を変えたら、下の「ログの形を変えたとき」の手順で、hsbg-kansousen にも同じ作業で反映する
- `.ps1` は BOM 付き UTF-8。ほかのテキストは BOM なし UTF-8、改行 LF（`.gitattributes`）

## ログの形を変えたとき（hsbg-kansousen にも同じ作業で反映する）

ログ（主ログ JSON の DTO・`schema/game-log.schema.json`・events.jsonl の `type` やフィールド）を変えたら、分析ツール hsbg-kansousen
（隣のフォルダ `..\hsbg-kansousen`。別の場所なら環境変数 `HSBG_KANSOUSEN_DIR`）にも **同じ作業の中で** 反映する。片方だけで止めない。

1. このリポジトリ: `docs/log-schema.md`（改訂履歴と該当の節）、`schema/game-log.schema.json`、`GameLog.CurrentSchemaVersion`、テスト
2. hsbg-kansousen: `hsbg/game-log.schema.json` と `docs/log-schema.md` にこのリポジトリのものをそのままコピーする
3. hsbg-kansousen: 読み込みを合わせる。`hsbg/loader.py` の `KNOWN_SCHEMA_VERSION` を新しい `schema_version` にし、新しいキーは `normalize.py` の列とテスト、
   列を変えたら `cache.py` の `CACHE_FORMAT` を上げる。分析で注意が要るものは `hsbg/templates/skills/hsbg-analysis/log-caveats.md`。キーの意味が変わるなら互換処理
4. 両方の検証コマンドを緑にする（hsbg-kansousen は その `CLAUDE.md` の検証コマンド）。`python scripts\check_cli_sync.py --strict` も緑にする
5. 作業の区切りの報告では、**両方のリポジトリ**について変更したファイルと推奨するコミットメッセージを出す

hsbg-kansousen の CI も、このリポジトリの main の schema / `docs/log-schema.md` とコピーが違えば失敗する（push 後の最後の確認）。
リリースでは、ログの版が前のリリースから変わっていれば **hsbg-kansousen を先に** リリースする。

## 検証コマンド

- `dotnet build HsbgKifu.sln -c Release; dotnet test HsbgKifu.sln -c Release --no-build`（警告ゼロ。警告はエラー扱い）
- `python scripts\check_cli_sync.py --strict`（hsbg-kansousen のコピーが最新か。`schema_version` の改訂履歴があるか。hsbg-kansousen が無ければ `--strict` を外す）
- 公開前: `python ..\hdt-plugin-hsbg-kansousen\scripts\check_private.py README.md CLAUDE.md TASKS.md docs spike scripts src tests tools`（個人の情報が入っていないか）
- パーサを変えたら、実データで答え合わせをする（どちらも違いの一覧が出る。今の結果は docs/research.md §3.1）:
  `.\tools\HsbgKifu.DevTools\bin\Release\net8.0\kifu-dev.exe verify-parse --slices <中継ログの .slices> --plugin "$env:USERPROFILE\Documents\HSBG-Logs"`、
  `--slices plugin` ならプラグインの power.log.gz（236 ゲーム）で比べる
- 実データで試すときは、出力先をスクラッチ（一時フォルダ）にし、終わったら消す（中継ログには BattleTag が入る）。
  実データの行を表示するときは BattleTag を伏せる（`Entity=<名前>`・`PlayerName=`・`Player=`）
- 実機確認は人間に依頼する。手順を PowerShell / GUI 操作として書いて止まる

## 構成

```
HsbgKifu.sln, Directory.Build.props（共通の設定と <Version>）
src/HsbgKifu.Core/
├── Watch/        # Power.log の監視と切り出し: LineSplitter, LogTailer, PowerLogLine, SessionClock, LogSessions, GameNetLog,
│                 #   GameSlicer, SliceStore（中継ログと .slice.json）, KifuWatcher（常駐の本体）, BatchSlicer
├── Parse/        # Power.log → 状態と出来事: PowerLogParser, PowerGame, Entity, PowerGameListener, TagNames, SliceReplay
├── Capture/      # 状態と出来事 → 記録: GameRecorder（プラグインの GameSession）, ShopBlockActions, Snapshots, KifuCards, PlayerBuffs, DeityReader,
│                 #   KifuBuilder / KifuOutput（中継ログ → JSON・events.jsonl・power.log.gz）, KifuWriterService（常駐・作り直しから使う。二重記録の防止）
├── Cards/        # CardDb（HDT の CardDefs.base.xml）
├── Logic/ Model/ Output/  # プラグインから移した純粋ロジック・DTO・書き出し（HDT 非依存。プラグインのテストも tests/ に移した）
├── ThirdParty/HearthDb/   # HearthDb の Enums.cs / Utils.cs の写し（MIT。THIRD_PARTY_NOTICES.md。手で直さない）
└── Diagnostics/  # IKifuLog
src/HsbgKifu.Cli/ # hsbg-kifu.exe（watch / slice / rebuild）。常駐アプリ（M5）ができるまでの確認用
docs/log-schema.md, schema/game-log.schema.json  # ログの仕様の正本（hsbg-kansousen が写しを持つ）
tests/HsbgKifu.Tests/  # xUnit。実データは使わず TestLogs.cs などで短いログを組み立てる
tools/HsbgKifu.DevTools/  # kifu-dev.exe（配布しない）: verify-parse / verify-json（プラグインの記録との答え合わせ）
scripts/          # backup-powerlogs.ps1（M0）, check_cli_sync.py（hsbg-kansousen の写しとの照合）
spike/            # 試作（製品のコードではない）
```

## 作業の進め方

- `TASKS.md` の順に進める。コミットはユーザーの許可を得てから。push やリモートの作成も頼まれたときだけ
- 「要検証」とされた事項は推測で実装せず、実際のログで確かめて docs に追記する

## やらないこと

- Hearthstone のメモリ読み取り、パケット解析、ゲームクライアントの改変（利用規約違反のリスク）
- 外部サーバへの送信（ローカルのファイルにだけ書く）
- BattleTag・プレイヤー名・アカウント ID（`BgsLastGames.xml` の `Player`）・接続先アドレス・`spectateKey` を記録の JSON に書くこと
  （中継ログ = Power.log の写しにはロビー全員の BattleTag が入る。人に渡さない）
- Hearthstone や HDT のバイナリ・ソースをリポジトリ・配布物に含めること（HearthDb は HDT とは別の MIT のプロジェクトで、列挙型だけを写している）
