using HsbgKifu.Capture;
using HsbgKifu.Config;
using HsbgKifu.Diagnostics;
using HsbgKifu.Model;
using HsbgKifu.Output;
using HsbgKifu.Watch;

namespace HsbgKifu;

/// <summary>
/// 常駐の本体: 設定から Power.log の監視（KifuWatcher）と記録の書き込み（KifuWriterService）を組み立てて、別のスレッドで動かす。
/// CLI の watch と常駐アプリ（タスクトレイ）が使う。設定を変えたら止めて作り直す。
/// </summary>
public sealed class KifuService : IAsyncDisposable
{
    private readonly KifuConfig _config;
    private readonly IKifuLog _log;
    private CancellationTokenSource? _cancel;
    private Task? _run;
    private KifuWriterService? _writer;

    public KifuService(KifuConfig config, IKifuLog log)
    {
        _config = config;
        _log = log;
        OutRoot = config.ResolveOutputDir();
        LogsDirectory = Path.Combine(LogFiles.FindHearthstoneDirectory(config.ResolveHearthstoneDir()), "Logs");
    }

    public string OutRoot { get; }
    public string LogsDirectory { get; }
    public string SlicesRoot => Path.Combine(OutRoot, ".slices");

    /// <summary>記録を書いた（後の MMR を入れて書き直したときも）。別のスレッドから呼ばれる。</summary>
    public event Action<GameLog, string>? RecordWritten;

    public bool Running => _run is { IsCompleted: false };

    public void Start()
    {
        if (Running) return;
        _cancel = new CancellationTokenSource();
        var token = _cancel.Token;
        _run = Task.Run(async () =>
        {
            try
            {
                var watcher = new KifuWatcher(new KifuWatcherOptions { LogsDirectory = LogsDirectory, SlicesRoot = SlicesRoot }, _log);
                _writer = new KifuWriterService(OutRoot, log: _log,
                    recorderOptions: new RecorderOptions { SnapshotHand = _config.SnapshotHand },
                    outputOptions: new KifuOutput.Options(_config.WriteEventsJsonl, _config.WritePowerLog),
                    mmrWait: TimeSpan.FromSeconds(Math.Max(0, _config.MmrAfterWaitSec)));
                _writer.Written += (log, path) => RecordWritten?.Invoke(log, path);
                _writer.UseStore(watcher.Store);
                watcher.Store.SliceFinished += (meta, gz) => _writer.Process(meta, gz);
                _log.Info($"記録の置き場: {OutRoot}（中継ログは {SlicesRoot}）");
                foreach (var p in LogConfigCheck.Problems()) _log.Warn(p);
                _writer.CatchUp(watcher.Store); // 中継ログはあるのに記録が無いゲーム（記録を書く前に止まったなど）
                await watcher.RunAsync(token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                // 常駐を落とさない。動作記録に書いて止まる（アプリは Running で分かる）
                _log.Error("監視が止まりました", e);
            }
            finally
            {
                _writer?.Dispose(); // 後の MMR を待っている記録を今の値で書き直す
                _writer = null;
            }
        });
    }

    public async Task StopAsync()
    {
        if (_cancel == null || _run == null) return;
        _cancel.Cancel();
        try { await _run.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _cancel.Dispose();
        _cancel = null;
        _run = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
