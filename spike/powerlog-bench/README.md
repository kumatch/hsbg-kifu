# powerlog-bench（試作。製品のコードではない）

Power.log を共有モードで 1 行ずつ読み、`CREATE_GAME` でゲームごとに gzip の中継ログへ切り出しながら、
`GameState` の行から全 Entity のタグを組み立てて、時間とメモリを測る。結果は docs/research.md §5。

```powershell
cd spike\powerlog-bench
dotnet build -c Release
$out = New-Item -ItemType Directory -Force "$env:TEMP\powerlog-bench-out"
.\bin\Release\net8.0\bench.exe "C:\Program Files (x86)\Hearthstone\Logs\Hearthstone_<日時>\Power_old.log" $out
```

出力先には Power.log の写し（ロビー全員の BattleTag が入る）ができるので、リポジトリの外に置く。
