# hsbg-kifu

Hearthstone Battlegrounds の 1 ゲームごとの記録を、Hearthstone の Power.log から作ってローカルの JSON に保存する常駐アプリです（開発中）。

HDT（Hearthstone Deck Tracker）が 2026-11-30 にプラグインを廃止するため、HDT プラグイン hdt-plugin-hsbg-kansousen の後継として作ります。
HDT とは別に起動して、Hearthstone のログファイルを読むだけです。ゲームのメモリは読まず、外部にも送信しません。
記録は分析ツール hsbg-kansousen で読めます。名前のとおり、このアプリが棋譜（kifu）を取り、hsbg-kansousen がそれを使って感想戦をする、という関係です。
棋譜は hsbg-kansousen 専用ではなく、JSON なのでほかのツールからも読めます。

- 調査: [docs/research.md](docs/research.md)
- 設計（案）: [docs/architecture.md](docs/architecture.md)
- 進み具合: [TASKS.md](TASKS.md)

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

## ライセンス

MIT（[LICENSE](LICENSE)）
