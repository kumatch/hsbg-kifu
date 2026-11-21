# hsbg-kifu

Hearthstone Battlegrounds の 1 ゲームごとの記録を、Hearthstone の Power.log から作ってローカルの JSON に保存する常駐アプリです（開発中）。

HDT（Hearthstone Deck Tracker）が 2026-11-30 にプラグインを廃止するため、HDT プラグイン hdt-plugin-hsbg-kansousen の後継として作ります。
HDT とは別に起動して、Hearthstone のログファイルを読むだけです。
記録は分析ツール hsbg-kansousen で読めます。名前のとおり、このアプリが棋譜（kifu）を取り、hsbg-kansousen がそれを使って感想戦をする、という関係です。
棋譜は hsbg-kansousen 専用ではなく、JSON なのでほかのツールからも読めます。

- 記録の形（ログの仕様）: [docs/log-schema.md](docs/log-schema.md)、[schema/game-log.schema.json](schema/game-log.schema.json)
- 開発の手順（ソースからのビルド、コマンドライン）: [docs/development.md](docs/development.md)
- 調査: [docs/research.md](docs/research.md)
- 設計: [docs/architecture.md](docs/architecture.md)
- 配布とリリース: [docs/release.md](docs/release.md)
- 進み具合: [TASKS.md](TASKS.md)

## hsbg-kifu がしないこと

hsbg-kifu は、Hearthstone が自分で書き出すログファイルを読んで、遊び終えたゲームの記録を作るだけのアプリです。
ゲームに手を加えることも、対戦中のプレイを手助けすることもしません。
Blizzard Entertainment、HearthSim とは関係のない非公式のツールです。

- Hearthstone のメモリを読みません。書き換えもしません
- Hearthstone のゲームファイルやクライアントを書き換えません
- Hearthstone の通信を傍受したり、解析したりしません
- Hearthstone の操作を自動化しません。キー入力やマウスの操作を送ることもしません
- 対戦中に、画面へ情報を重ねて表示したり、プレイを助言したりしません。記録の JSON は、ゲームが終わってから作ります
- 外部のサーバーには何も送りません。インターネットに接続する機能を持っていません
- BattleTag・プレイヤー名・アカウント ID を、記録の JSON に書きません

読むファイルと書くファイルは、次のものだけです。

| | ファイル |
|---|---|
| 読む | Hearthstone の `Logs` フォルダにある `Power.log`（終わった起動の分は `Power_old.log`）と `GameNetLogger.log`、Hearthstone のログの設定 `log.config`、HDT のファイル（下の「HDT と一緒に使ってください」の表） |
| 書く | 記録の出力先（既定は `Documents\HSBG-Logs`）、設定と動作記録の `%APPDATA%\hsbg-kifu`、「サインイン時に起動する」を選んだときだけ、自分のユーザーのレジストリの `Run` の値 1 つ |

Hearthstone が動いているかどうかは、プロセスの名前で確かめるだけです。

記録の出力先には、JSON のほかに、Power.log をゲームごとに切り出した写し（`.power.log.gz` と、`.slices` フォルダの中継ログ）も書きます。
この写しには、同じロビーの全員の BattleTag が入ります。人に渡さないでください。
記録の横の `.power.log.gz` は、設定画面で書かないようにできます（`.slices` の中継ログは、記録を作り直すために常に残します）。

## HDT と一緒に使ってください

hsbg-kifu は、HDT（Hearthstone Deck Tracker）と一緒に動かす前提です。いくつかの値は HDT が書くファイルから読みます。
HDT は、配布元の HSReplay.net のダウンロードページ（<https://hsreplay.net/downloads/>）から入手してください。
HDT が無くても記録は作れますが、そのとき取れない値は空（null や空の配列）になり、記録の `notes` に理由を書きます。
空になっても記録の形（JSON Schema）としては正しく、hsbg-kansousen で読めます。

| HDT から読むもの | 使い道 | HDT が無いとき |
|---|---|---|
| `%APPDATA%\HearthstoneDeckTracker\BgsLastGames.xml` | MMR（前・後）。Hearthstone のログには出ないので、HDT がゲームのメモリから読んで書いた値を使う。直近の 18 ゲームほどしか残らない | `mmr_before` / `mmr_after` / `mmr_delta` が null（notes に "mmr_before unavailable" など） |
| `%APPDATA%\HearthstoneDeckTracker\CardDefs\CardDefs.base.xml` | カードの名前・dbfId・ティア・2 つ目の種族（HDT が更新し続ける） | 残っているファイルを使う（更新されない）。無ければ名前などが null。カード ID・攻撃力・体力・ロビーの種族は Power.log から取るので変わらない |
| `%LOCALAPPDATA%\Blizzard\Hearthstone\log.config` | Hearthstone に Power.log を出させる設定（HDT が書く） | 一度設定されれば残る。設定が無いと Power.log が出ず、記録できない |
| `%APPDATA%\HearthstoneDeckTracker\config.xml` | Hearthstone のフォルダの場所 | 既定の場所（`C:\Program Files (x86)\Hearthstone`）。設定画面の「Hearthstone のフォルダ」で指定できる |

どれも読むだけで、HDT のファイルを書き換えることはありません。

## 使い方

Windows 10 / 11（64 ビット）で動きます。.NET のランタイムは exe に入っているので、別に入れる必要はありません。

1. [Releases](https://github.com/kumatch/hsbg-kifu/releases) から `hsbg-kifu-<版>-win-x64.zip` をダウンロードし、好きな場所に展開します
2. `HsbgKifu.exe` を起動します。タスクトレイに濃い青と金の「BG」のアイコンが出ます
3. いつもどおり HDT と Hearthstone で BG を遊ぶと、ゲームが終わるたびに記録が `Documents\HSBG-Logs\YYYY\MM\` に保存されます

exe には電子署名が無いので、初めて起動するときに「Windows によって PC が保護されました」と表示されることがあります。
入手先がこのリポジトリの Releases であることを確かめてから、「詳細情報」→「実行」を押してください。
zip の SHA-256 は Releases のページに書いてあり、PowerShell の `Get-FileHash .\hsbg-kifu-<版>-win-x64.zip -Algorithm SHA256` で確かめられます。

- アイコンの右クリックのメニューで、記録のフォルダと動作記録を開いたり、設定を変えたりできます。ダブルクリックで記録のフォルダを開きます
- 設定は `%APPDATA%\hsbg-kifu\config.json`（メニューの「設定...」で変えられます）。動作記録は `%APPDATA%\hsbg-kifu\kifu.log`
- 設定画面の「Windows へのサインイン時に起動する」をチェックすると、サインインのたびに起動します（既定は OFF）。
  展開したフォルダを後で動かしたときは、チェックを付け直してください
- 起動時に Hearthstone のログの設定（log.config）を確かめ、Power.log が出ない設定なら通知します
- 二重には起動しません。終了はメニューの「終了」から行います。ゲームの後の MMR を待っている記録は、終了した時点の値で書きます

更新するときは、hsbg-kifu を終了してから、新しい zip の中身で上書きします。設定は残ります。
削除するときは、設定画面で「サインイン時に起動する」を外してから終了し、展開したフォルダを消します。
`%APPDATA%\hsbg-kifu` の設定と動作記録は、要らなければ消してください。記録（`Documents\HSBG-Logs`）は消えません。

## HDT プラグインから切り替える

記録の形はプラグインと同じで、置き場も同じ `Documents\HSBG-Logs` です。プラグインの記録と hsbg-kifu の記録は、hsbg-kansousen でまとめて読めます。

HDT のプラグイン（hdt-plugin-hsbg-kansousen）を外してから、hsbg-kifu を使ってください。
hsbg-kifu は、出力先に同じゲームの記録がすでにあれば書きませんが、プラグインは hsbg-kifu の記録を確かめずに書きます。
両方を同時に動かすと、同じゲームの記録が 2 つになることがあります。

## ライセンス

MIT（[LICENSE](LICENSE)）。同梱しているソフトウェアのライセンスは [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) にあります。
