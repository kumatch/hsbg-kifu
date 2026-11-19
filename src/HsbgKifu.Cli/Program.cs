using HsbgKifu.Diagnostics;
using HsbgKifu.Watch;

namespace HsbgKifu.Cli;

/// <summary>
/// 開発・確認用のコマンド。常駐アプリ（M5）ができるまでは、これで Power.log を監視する。
/// <code>
/// hsbg-kifu watch [--out DIR] [--hs DIR] [--log FILE]   常駐して Power.log を読み続け、ゲームごとに切り出す（Ctrl+C で止める）
/// hsbg-kifu slice --logs DIR [--out DIR]                 終わったログフォルダ（Logs や M0 の退避先）をまとめて切り出す
/// </code>
/// 中継ログは &lt;out&gt;\.slices\ に書く。&lt;out&gt; の既定は %USERPROFILE%\Documents\HSBG-Logs。
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Usage();
            return args.Length == 0 ? 2 : 0;
        }
        var opts = ParseOptions(args.Skip(1).ToArray());
        if (opts == null) { Usage(); return 2; }

        var outDir = opts.GetValueOrDefault("out") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HSBG-Logs");
        var slicesRoot = Path.Combine(outDir, ".slices");
        var log = new TextKifuLog(Console.Out, opts.GetValueOrDefault("log"));

        switch (args[0])
        {
            case "watch":
            {
                var hsDir = LogFiles.FindHearthstoneDirectory(opts.GetValueOrDefault("hs"));
                var logsDir = Path.Combine(hsDir, "Logs");
                using var cancel = new CancellationTokenSource();
                Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
                var watcher = new KifuWatcher(new KifuWatcherOptions { LogsDirectory = logsDir, SlicesRoot = slicesRoot }, log);
                log.Info($"中継ログの置き場: {slicesRoot}（Ctrl+C で止めます）");
                await watcher.RunAsync(cancel.Token);
                return 0;
            }
            case "slice":
            {
                var logsDir = opts.GetValueOrDefault("logs");
                if (logsDir == null) { Usage(); return 2; }
                var store = new SliceStore(slicesRoot);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var n = BatchSlicer.SliceAll(logsDir, store, log);
                log.Info($"{n} ゲームを切り出しました（{sw.Elapsed.TotalSeconds:F1} 秒）。置き場: {slicesRoot}");
                return 0;
            }
            default:
                Usage();
                return 2;
        }
    }

    private static Dictionary<string, string>? ParseOptions(string[] args)
    {
        var result = new Dictionary<string, string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length) return null;
            result[args[i][2..]] = args[++i];
        }
        return result;
    }

    private static void Usage()
    {
        Console.Error.WriteLine("""
            使い方:
              hsbg-kifu watch [--out DIR] [--hs DIR] [--log FILE]
                  常駐して Power.log を読み続け、ゲームごとの中継ログを書く（Ctrl+C で止める）
              hsbg-kifu slice --logs DIR [--out DIR]
                  終わったログフォルダ（Hearthstone の Logs、または backup-powerlogs.ps1 の退避先）をまとめて切り出す

              --out  出力先（既定: Documents\HSBG-Logs）。中継ログは <out>\.slices\ に書く
              --hs   Hearthstone のフォルダ（既定: HDT の config.xml の HearthstoneDirectory）
              --log  動作記録を書くファイル
            """);
    }
}
