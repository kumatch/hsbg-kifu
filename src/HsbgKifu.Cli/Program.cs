using HsbgKifu.Capture;
using HsbgKifu.Diagnostics;
using HsbgKifu.Watch;

namespace HsbgKifu.Cli;

/// <summary>
/// 開発・確認用のコマンド（配布しない。docs/development.md）。
/// <code>
/// hsbg-kifu watch --out DIR [--hs DIR] [--log FILE]   常駐して Power.log を読み続け、ゲームごとに切り出して記録を作る（Ctrl+C で止める）
/// hsbg-kifu slice --logs DIR --out DIR                 終わったログフォルダ（Logs や M0 の退避先）をまとめて切り出す（記録は作らない）
/// hsbg-kifu rebuild --out DIR                          中継ログ（&lt;out&gt;\.slices）から記録を作り直す
/// </code>
/// 中継ログは &lt;out&gt;\.slices\ に、記録は &lt;out&gt;\YYYY\MM\ に書く。
/// --out は必須（既定値を置くと、試しただけで常駐アプリやプラグインの記録のフォルダに書いてしまうため）。
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

        var outDir = opts.GetValueOrDefault("out");
        if (string.IsNullOrWhiteSpace(outDir))
        {
            Console.Error.WriteLine("--out で出力先を指定してください（試すときは、常駐アプリの記録とは別の空のフォルダにします）");
            Usage();
            return 2;
        }
        var slicesRoot = Path.Combine(outDir, ".slices");
        var log = new TextKifuLog(Console.Out, opts.GetValueOrDefault("log"));

        switch (args[0])
        {
            case "watch":
            {
                var config = new Config.KifuConfig { OutputDir = outDir, HearthstoneDir = opts.GetValueOrDefault("hs") };
                await using var service = new KifuService(config, log);
                using var stop = new SemaphoreSlim(0);
                Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Release(); };
                service.Start();
                log.Info("Ctrl+C で止めます");
                await stop.WaitAsync();
                await service.StopAsync();
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
            case "rebuild":
            {
                var store = new SliceStore(slicesRoot);
                var writer = new KifuWriterService(outDir, log: log);
                writer.UseStore(store);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int done = 0, total = 0;
                foreach (var (meta, gz) in store.Finished)
                {
                    total++;
                    if (writer.Process(meta, gz) != null) done++;
                }
                log.Info($"{total} ゲームのうち {done} ゲームの記録を書きました（{sw.Elapsed.TotalSeconds:F1} 秒）。置き場: {outDir}");
                var pruned = store.All.Count(m => m.PrunedAt != null);
                if (pruned > 0) log.Info($"整理済みの一時ログ {pruned} 件は飛ばしました（.power.log.gz を消したので作り直せません）");
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
              hsbg-kifu watch --out DIR [--hs DIR] [--log FILE]
                  常駐して Power.log を読み続け、ゲームごとの中継ログと記録を書く（Ctrl+C で止める）。
                  起動時に、中継ログはあるのに記録が無いゲームの記録も作る。始まってから 30 日を過ぎたゲームの中継ログは整理する
              hsbg-kifu slice --logs DIR --out DIR
                  終わったログフォルダ（Hearthstone の Logs、または scripts\backup-powerlogs.ps1 の退避先）をまとめて切り出す（記録は作らない）
              hsbg-kifu rebuild --out DIR
                  中継ログ（<out>\.slices）から記録を作り直す。整理済みの中継ログ（.power.log.gz を消したもの）は飛ばす

              --out  出力先（必須）。中継ログは <out>\.slices\、記録は <out>\YYYY\MM\ に書く。
                     試すときは、常駐アプリの記録のフォルダ（Documents\HSBG-Logs）とは別の空のフォルダにする
              --hs   Hearthstone のフォルダ（既定: HDT の config.xml の HearthstoneDirectory）
              --log  動作記録を書くファイル

            注意:
              - 開発・確認用のコマンドです。配布の zip には入っていません
              - 出力先に同じゲームの記録があれば書きません。ただし確かめるのは、起動したときにあった記録と自分が書いた記録だけです。
                常駐アプリと同じ出力先で同時に動かすと、同じゲームの記録が 2 つになることがあります
              - 中継ログ（.slices）と .power.log.gz には、同じロビーの全員の BattleTag が入ります。人に渡さないでください
            """);
    }
}
