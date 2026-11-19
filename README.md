# hsbg-kansousen-recorder

Hearthstone Battlegrounds の 1 ゲームごとの記録を、Hearthstone の Power.log から作ってローカルの JSON に保存する常駐アプリです（開発中）。

HDT（Hearthstone Deck Tracker）が 2026-11-30 にプラグインを廃止するため、HDT プラグイン hdt-plugin-hsbg-kansousen の後継として作ります。
HDT とは別に起動して、Hearthstone のログファイルを読むだけです。ゲームのメモリは読まず、外部にも送信しません。
記録は分析ツール hsbg-kansousen で読めます。

- 調査: [docs/research.md](docs/research.md)
- 設計（案）: [docs/architecture.md](docs/architecture.md)
- 進み具合: [TASKS.md](TASKS.md)

## ライセンス

MIT（[LICENSE](LICENSE)）
