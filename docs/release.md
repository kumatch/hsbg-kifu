# 配布とリリースの手順

## 配布の形（2026-11-21 に決めた）

- **.NET ランタイムを同梱した 1 つの exe**（self-contained、single-file、win-x64）を、説明とライセンス文と一緒に **zip** にして GitHub Releases に置く。
  利用者は展開して `HsbgKifu.exe` を起動するだけでよい（.NET のインストールは要らない）。HDT が使うのは .NET Framework 4.7.2 なので、
  HDT の利用者に新しい .NET のランタイムが入っているとは限らないため
- 大きさ: exe 約 52 MB、zip 約 45 MB（hsbg-kifu 0.1.0、.NET 10.0.15）。ランタイムを同梱しない形なら約 1.3 MB だが、利用者に .NET Desktop Runtime を入れてもらう必要がある
- **.NET 10**（LTS。2028-11 までサポート）。.NET 8 のサポートは 2026-11-10 に終わったので、最初のリリースの前に移した。SDK は `global.json` で 10.0.100 以降の 10.0.x
- .NET のランタイムと Newtonsoft.Json は MIT ライセンスなので、同梱して配布してよい。条件の著作権表示とライセンス文は zip の `licenses\` と `THIRD_PARTY_NOTICES.md` に入れる
- 同梱したランタイムは、hsbg-kifu を出し直さないと更新されない。.NET のセキュリティ更新（毎月の 10.0.x）で、hsbg-kifu に関わるもの（WinForms、ファイル・XML の読み書き）が出たら出し直す
- **インストーラと電子署名は見送った**。署名が無いので、利用者が初めて起動すると SmartScreen の「Windows によって PC が保護されました」が出る
  （「詳細情報」→「実行」）。zip の README.txt と README に書き、zip の SHA-256 をリリースノートに載せる
- 設定と動作記録は `%APPDATA%\hsbg-kifu`、記録は出力先にあるので、更新は zip の中身の上書きだけでよい

## 版の付け方

- `Directory.Build.props` の `<Version>` が正本。タグは `v<version>`（例 `v0.1.0`）で、`<Version>` を上げたコミットに後から打つ
  （タグから版を決める MinVer などは使わない。タグが先だと、package と起動の確認で問題が出たときにタグを打ち直すことになり、
  記録の `generator.plugin_version` も git の状態に左右されるため。2026-11-23）
- アプリは自分の版を `KifuVersion` で知る。`Current`（x.y.z）は記録の `generator.plugin_version` と `.slice.json` の `kifu_version` に書き、
  `Display`（例 `0.3.0（8a7ce60）`）は設定画面の下と動作記録の起動の行に出す。括弧の中はビルドしたコミットで、
  .NET SDK が AssemblyInformationalVersion の `+` の後ろに入れるハッシュを使う（リリース後の開発ビルドと区別できる）。トレイメニューの先頭には `Current` を出す
- `scripts/package.ps1` は、コミットしていない変更があるとき（`-AllowDirty` で試しに作れる。配布には使わない）と、
  タグ `v<version>` がもう別のコミットを指しているとき（`<Version>` の上げ忘れ）に止まる。exe の版が `<version>+<HEAD のコミット>` になっているかも確かめる
- ログの版（`GameLog.CurrentSchemaVersion`）とは別。番号は hsbg-kansousen ともそろえない（対応はログの版で決まる。CLAUDE.md「ログの形を変えたとき」）

## 手順

0. ログの版が前のリリースから変わったなら、先にそのログの版を読める hsbg-kansousen をリリースしておく（hsbg-kansousen の `docs/release.md`）
1. `<Version>` を上げ、検証コマンド（CLAUDE.md）を緑にしてコミットする
2. `.\scripts\package.ps1` で `dist\hsbg-kifu-<version>-win-x64.zip` と `.sha256` を作る（変更が残っていないか・タグの確認 → テスト → exe → exe の版と手元のパスの確認 → zip）
3. zip を別のフォルダに展開して起動し、タスクトレイのアイコン・メニューの先頭の版・設定画面（下の版とコミット）・1 ゲームの記録を確かめる（人間に依頼）
4. `git tag -a v<version> -m "hsbg-kifu <version>（schema <a.b.c>）"; git push origin main v<version>`
5. GitHub の Releases でタグを選び、zip を添付して、リリースノートに次を書く: 変更点、書くログの版（schema）、読める hsbg-kansousen の版、
   zip の SHA-256、初めて起動するときの SmartScreen の説明、HDT と一緒に使うこと
