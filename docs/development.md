# 開発の手順

ソースからビルドして動かす手順と、開発・確認用のコマンドラインツールの使い方です。
利用者向けの説明は [README](../README.md)、配布の zip の作り方は [release.md](release.md)、検証コマンドは [CLAUDE.md](../CLAUDE.md) にあります。

## ビルドと常駐アプリの起動

.NET 10 SDK が要ります（`global.json`）。

```powershell
dotnet build HsbgKifu.sln -c Release
& ".\src\HsbgKifu.App\bin\Release\net10.0-windows\HsbgKifu.exe"
```

配布する zip と同じ常駐アプリが起動します。設定（`%APPDATA%\hsbg-kifu\config.json`）も配布物と同じものを使います。

## コマンドライン（hsbg-kifu.exe）

常駐アプリ（M5）ができるまでの確認用に作ったコマンドです。今も、中継ログの切り出しや記録の作り直しに使います。配布の zip には入れていません。

Power.log をゲームごとの中継ログ（`.slices\` の `.power.log.gz` と `.slice.json`）に切り出し、ゲームが終わるたびに記録（棋譜）の JSON を作ります。
MMR は HDT の `BgsLastGames.xml` から読み（`watch` はゲームの後に最長 60 秒待って書き直します）、ロビーの種族はショップに並んだミニオンから決めます。

```powershell
dotnet build HsbgKifu.sln -c Release
$kifu = ".\src\HsbgKifu.Cli\bin\Release\net10.0\hsbg-kifu.exe"

# 常駐して Power.log を読み続け、ゲームが終わるたびに記録を書く（Ctrl+C で止める）
& $kifu watch --out "$env:USERPROFILE\Documents\HSBG-Kifu-Test" --log "$env:USERPROFILE\Documents\HSBG-Kifu-Test\kifu.log"

# 終わったログフォルダをまとめて切り出す（Hearthstone の Logs や、scripts\backup-powerlogs.ps1 の退避先。記録は作らない）
& $kifu slice --logs "$env:USERPROFILE\Documents\HSBG-PowerLogs" --out "$env:USERPROFILE\Documents\HSBG-Kifu-Test"

# 中継ログから記録を作り直す
& $kifu rebuild --out "$env:USERPROFILE\Documents\HSBG-Kifu-Test"
```

- 記録は `<出力先>\YYYY\MM\` に、`.json`・`.events.jsonl`・`.power.log.gz` を書きます（プラグインと同じ置き方）
- `--out` の既定は、プラグインの記録と同じ `Documents\HSBG-Logs` です。試すときは、上の例のように別のフォルダを指定してください。
  `.slices` はドットで始まるので、hsbg-kansousen は読みません
- 出力先に同じゲームの記録（プラグインの記録など）がすでにあれば、書きません。ただし、確かめるのは起動したときにあった記録と自分が書いた記録だけです。
  プラグインと同時に動かすと、同じゲームの記録が 2 つになることがあります
- `watch` は起動時に、中継ログはあるのに記録が無いゲームの記録も作ります
- 中継ログは、利用者向けの表記（設定画面・README・動作記録）では「一時ログ」と呼んでいます（docs/architecture.md「中継ログ」）
- `watch` も常駐アプリと同じく、始まってから 30 日を過ぎたゲームの中継ログを整理します（`.power.log.gz` を消して `.slice.json` に `pruned_at` を付ける。docs/architecture.md「中継ログ」）。
  `rebuild` は整理済みの中継ログを飛ばし、その件数を出します
- `--hs` で Hearthstone のフォルダを指定できます（既定は HDT の設定から探します）
- 中継ログと `.power.log.gz` には、同じロビーの全員の BattleTag が入ります。人に渡さないでください

## 答え合わせ（kifu-dev.exe）

`tools/HsbgKifu.DevTools` の `kifu-dev.exe` で、作った記録をプラグインの記録と比べられます（配布しません）。
コマンドは CLAUDE.md の「検証コマンド」、今の結果は [research.md](research.md) §3.1 にあります。
