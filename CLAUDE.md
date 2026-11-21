# CLAUDE.md — hsbg-kifu

Claude Code がセッション開始時に読む、このリポジトリの規約です。詳細は `docs/`。ここには「毎回守ること」だけを書きます。

## このリポジトリは何か

Hearthstone Battlegrounds の 1 ゲームごとの記録を、Hearthstone の Power.log から作ってローカルの JSON に保存する常駐アプリ。
HDT プラグイン hdt-plugin-hsbg-kansousen（隣のフォルダ `..\hdt-plugin-hsbg-kansousen`）の後継で、HDT が 2026-11-30 にプラグインを止めるために作る。
記録（棋譜）は分析ツール hsbg-kansousen（`..\hsbg-kansousen`）などが読む。依存の向きは hsbg-kifu → 棋譜 → hsbg-kansousen で、このリポジトリは hsbg-kansousen に依存しない。記録の形はプラグインの `docs/log-schema.md` を引き継ぐ（TASKS.md M6 まではプラグイン側が正本）。

## 必ず読むドキュメント

- `TASKS.md` — 今やるべきタスク
- `docs/research.md` — Power.log と HDT のファイルについて確認した事実
- `docs/architecture.md` — 設計

## 環境の前提

- OS: Windows 10/11。WSL は使わない。Claude Code は PowerShell から起動される。手順は PowerShell / Windows パスで書く
- .NET 8（WPF）。ビルドは `dotnet build`
- Hearthstone のログ: HDT の `%APPDATA%\HearthstoneDeckTracker\config.xml` の `HearthstoneDirectory` 配下の `Logs\Hearthstone_<日時>\Power.log`
  （終わった起動分は `Power_old.log`）。出力の設定は `%LOCALAPPDATA%\Blizzard\Hearthstone\log.config`
- HDT のデータ: `%APPDATA%\HearthstoneDeckTracker\`（`BgsLastGames.xml`、`CardDefs\`）。**読むだけ**
- プラグインの記録と power.log.gz（答え合わせに使う）: `%USERPROFILE%\Documents\HSBG-Logs`

## コーディング規約

- 名前空間 `HsbgKifu`。Power.log の解析・純粋ロジックは UI から分け、xUnit でテストする
- 例外は捕捉してログに書く。記録アプリが落ちても Hearthstone と HDT に影響させない
- Hearthstone と HDT のファイルは共有モードで開いて読むだけ。書き換えない
- ファイル書き込みはアトミックに（一時ファイル → 置き換え）
- ログの形を変えたら、docs/log-schema.md・JSON Schema・テストを揃えて `schema_version` を上げ、hsbg-kansousen にも同じ作業で反映する
  （手順はプラグインの CLAUDE.md「ログの形を変えたとき」。M6 でこのリポジトリに移す）
- `.ps1` は BOM 付き UTF-8。ほかのテキストは BOM なし UTF-8、改行 LF（`.gitattributes`）

## 検証コマンド

- `dotnet build HsbgKifu.sln -c Release; dotnet test HsbgKifu.sln -c Release --no-build`（警告ゼロ。警告はエラー扱い）
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
schema/game-log.schema.json  # プラグインのリポジトリの schema/ の写し（テスト用。正本は M6 までプラグイン側。変えるときは両方）
tests/HsbgKifu.Tests/  # xUnit。実データは使わず TestLogs.cs などで短いログを組み立てる
tools/HsbgKifu.DevTools/  # kifu-dev.exe（配布しない）: verify-parse / verify-json（プラグインの記録との答え合わせ）
scripts/          # backup-powerlogs.ps1（M0）
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
