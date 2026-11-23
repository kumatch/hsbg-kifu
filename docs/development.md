# 開発の手順

ソースからビルドして動かす手順です。
利用者向けの説明は [README](../README.md)、配布の zip の作り方は [release.md](release.md)、テストなどの検証コマンドは [CLAUDE.md](../CLAUDE.md) の「検証コマンド」にあります。

## ビルドと常駐アプリの起動

.NET 10 SDK が要ります（`global.json`）。

```powershell
dotnet build HsbgKifu.sln -c Release
dotnet test HsbgKifu.sln -c Release --no-build
& ".\src\HsbgKifu.App\bin\Release\net10.0-windows\HsbgKifu.exe"
```

配布する zip と同じ常駐アプリが起動します。設定（`%APPDATA%\hsbg-kifu\config.json`）も記録の出力先も、配布物と同じものを使います。

## 開発用の道具

次の 2 つは開発用です。配布の zip には入れず、サポートもしません。使い方は、それぞれ引数なしで実行すると出ます。

| 道具 | 置き場 | 用途 |
|---|---|---|
| `hsbg-kifu.exe` | `src\HsbgKifu.Cli` | 常駐アプリと同じ処理をコンソールで動かす（`watch`）。終わった Power.log の切り出し（`slice`）、記録の作り直し（`rebuild`） |
| `kifu-dev.exe` | `tools\HsbgKifu.DevTools` | 作った記録を、前身の HDT プラグインの記録と比べる |

`hsbg-kifu.exe` は出力先（`--out`）を必ず指定します。試すときは、常駐アプリの記録のフォルダとは別の、空のフォルダにしてください。
出力先の `.slices` には Power.log の写しが入り、同じロビーの全員の BattleTag が含まれます。人に渡さないでください。
