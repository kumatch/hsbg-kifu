# hsbg-kifu

Hearthstone Battlegrounds の 1 ゲームごとの記録を、Hearthstone の Power.log から作ってローカルの JSON に保存する常駐アプリです（開発中）。

HDT（Hearthstone Deck Tracker）が 2026-11-30 にプラグインを廃止するため、HDT プラグイン hdt-plugin-hsbg-kansousen の後継として作ります。
HDT とは別に起動して、Hearthstone のログファイルを読むだけです。ゲームのメモリは読まず、外部にも送信しません。
記録は分析ツール hsbg-kansousen で読めます。名前のとおり、このアプリが棋譜（kifu）を取り、hsbg-kansousen がそれを使って感想戦をする、という関係です。
棋譜は hsbg-kansousen 専用ではなく、JSON なのでほかのツールからも読めます。

- 調査: [docs/research.md](docs/research.md)
- 設計（案）: [docs/architecture.md](docs/architecture.md)
- 進み具合: [TASKS.md](TASKS.md)

## HDT と一緒に使ってください

hsbg-kifu は、HDT（Hearthstone Deck Tracker）と一緒に動かす前提です。いくつかの値は HDT が書くファイルから読みます。
HDT が無くても記録は作れますが、そのとき取れない値は空（null や空の配列）になり、記録の `notes` に理由を書きます。
空になっても記録の形（JSON Schema）としては正しく、hsbg-kansousen で読めます。

| HDT から読むもの | 使い道 | HDT が無いとき |
|---|---|---|
| `%APPDATA%\HearthstoneDeckTracker\BgsLastGames.xml` | MMR（前・後）。Hearthstone のログには出ないので、HDT がゲームのメモリから読んで書いた値を使う。直近の 18 ゲームほどしか残らない | `mmr_before` / `mmr_after` / `mmr_delta` が null（notes に "mmr_before unavailable" など） |
| `%APPDATA%\HearthstoneDeckTracker\CardDefs\CardDefs.base.xml` | カードの名前・dbfId・ティア・2 つ目の種族（HDT が更新し続ける） | 残っているファイルを使う（更新されない）。無ければ名前などが null。カード ID・攻撃力・体力・ロビーの種族は Power.log から取るので変わらない |
| `%LOCALAPPDATA%\Blizzard\Hearthstone\log.config` | Hearthstone に Power.log を出させる設定（HDT が書く） | 一度設定されれば残る。設定が無いと Power.log が出ず、記録できない |
| `%APPDATA%\HearthstoneDeckTracker\config.xml` | Hearthstone のフォルダの場所 | 既定の場所（`C:\Program Files (x86)\Hearthstone`）。`--hs` で指定できる |

どれも読むだけで、HDT のファイルを書き換えることはありません。

## 記録アプリができるまでの退避

Hearthstone のログフォルダ（`<Hearthstone のフォルダ>\Logs\Hearthstone_<日時>\`）は 6 起動分ほどで消えます。
記録アプリができるまでは、遊んだ後に次のスクリプトで Power.log を退避しておくと、後で記録を作れます。

```powershell
.\scripts\backup-powerlogs.ps1                                 # 退避先: Documents\HSBG-PowerLogs
.\scripts\backup-powerlogs.ps1 -Destination D:\HSBG-PowerLogs  # 退避先を変える
```

- 写すのは `Power*.log` と `GameNetLogger*.log` を gzip にしたものだけです。Hearthstone のファイルは変えません。一度写したファイルは、変わっていなければ飛ばします
- 退避したファイルには、同じロビーの全員の BattleTag が入ります。人に渡さないでください
- 実行ポリシーで止まるときは `powershell -ExecutionPolicy Bypass -File .\scripts\backup-powerlogs.ps1` で実行します

## 常駐アプリ（M5、開発中）

```powershell
dotnet build HsbgKifu.sln -c Release
& ".\src\HsbgKifu.Appin\Release
et8.0-windows\HsbgKifu.exe"
```

- タスクトレイに濃い青と金の「BG」のアイコンが出ます。右クリックのメニューで、記録のフォルダと動作記録を開いたり、設定を変えたりできます。ダブルクリックで記録のフォルダを開きます
- 設定は `%APPDATA%\hsbg-kifu\config.json`（メニューの「設定...」で変えられます）。動作記録は `%APPDATA%\hsbg-kifu\kifu.log`
- 設定画面の「Windows へのサインイン時に起動する」をチェックすると、サインインのたびに起動します（既定は OFF）
- 起動時に Hearthstone のログの設定（log.config）を確かめ、Power.log が出ない設定なら通知します
- 二重には起動しません。終了はメニューの「終了」から（後の MMR を待っている記録は、その時点の値で書きます）

## 開発中の使い方（コマンドライン）

Power.log をゲームごとの中継ログ（`.slices\` の `.power.log.gz` と `.slice.json`）に切り出し、ゲームが終わるたびに記録（棋譜）の JSON を作ります。
記録の形はプラグインと同じで、hsbg-kansousen でそのまま読めます。MMR は HDT の `BgsLastGames.xml` から読み（`watch` はゲームの後に最長 60 秒待って書き直します）、
ロビーの種族はショップに並んだミニオンから決めます。.NET 8 SDK が要ります。

```powershell
dotnet build HsbgKifu.sln -c Release
$kifu = ".\src\HsbgKifu.Cli\bin\Release\net8.0\hsbg-kifu.exe"

# 常駐して Power.log を読み続け、ゲームが終わるたびに記録を書く（Ctrl+C で止める）
& $kifu watch --out "$env:USERPROFILE\Documents\HSBG-Kifu-Test" --log "$env:USERPROFILE\Documents\HSBG-Kifu-Test\kifu.log"

# 終わったログフォルダをまとめて切り出す（Hearthstone の Logs や、上の退避先。記録は作らない）
& $kifu slice --logs "$env:USERPROFILE\Documents\HSBG-PowerLogs" --out "$env:USERPROFILE\Documents\HSBG-Kifu-Test"

# 中継ログから記録を作り直す
& $kifu rebuild --out "$env:USERPROFILE\Documents\HSBG-Kifu-Test"
```

- 記録は `<出力先>\YYYY\MM\` に、`.json`・`.events.jsonl`・`.power.log.gz` を書きます（プラグインと同じ置き方）
- `--out` の既定は `Documents\HSBG-Logs` です（プラグインの記録と同じ場所。`.slices` はドットで始まるので hsbg-kansousen は読みません）。
  同じゲームの記録（プラグインの記録など）がすでにあれば書かないので、二重にはなりません。
  `watch` は起動時に、中継ログはあるのに記録が無いゲームの記録も作ります
- `--hs` で Hearthstone のフォルダを指定できます（既定は HDT の設定から探します）
- 中継ログと `.power.log.gz` には、同じロビーの全員の BattleTag が入ります。人に渡さないでください

## ライセンス

MIT（[LICENSE](LICENSE)）
