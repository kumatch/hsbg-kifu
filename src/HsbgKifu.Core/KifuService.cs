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
/// 中継ログの整理（設定の保持期間）は、起動時の回収の後と、中継ログから記録を作った後に動かす。
/// </summary>
public sealed class KifuService : IAsyncDisposable
{
    private readonly KifuConfig _config;
    private readonly IKifuLog _log;
    private CancellationTokenSource? _cancel;
    private Task? _run;
    private volatile KifuWriterService? _writer;
    private volatile SliceStore? _store;

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

    /// <summary>テスト用: カードの情報と MMR の読み元（既定は HDT のファイル）。</summary>
    internal string? CardDefsPath { get; init; }
    internal BgsLastGames? Ratings { get; init; }

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
                var writer = new KifuWriterService(OutRoot, cardDefsPath: CardDefsPath, ratings: Ratings, log: _log,
                    recorderOptions: new RecorderOptions { SnapshotHand = _config.SnapshotHand },
                    outputOptions: new KifuOutput.Options(_config.WriteEventsJsonl, _config.WritePowerLog),
                    mmrWait: TimeSpan.FromSeconds(Math.Max(0, _config.MmrAfterWaitSec)));
                writer.Written += (log, path) => RecordWritten?.Invoke(log, path);
                writer.UseStore(watcher.Store);
                _writer = writer;
                _store = watcher.Store;
                var caughtUp = false;
                watcher.Store.SliceFinished += (meta, gz) =>
                {
                    writer.Process(meta, gz);
                    if (caughtUp) AutoPrune(writer, watcher.Store); // 起動時の回収の途中では、終わってからまとめて 1 回
                };
                watcher.CaughtUp += () =>
                {
                    caughtUp = true;
                    AutoPrune(writer, watcher.Store);
                };
                _log.Info($"記録の置き場: {OutRoot}（一時ログは {SlicesRoot}）");
                foreach (var p in LogConfigCheck.Problems()) _log.Warn(p);
                writer.CatchUp(watcher.Store); // 中継ログはあるのに記録が無いゲーム（記録を書く前に止まったなど）
                await watcher.RunAsync(token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                // 常駐を落とさない。動作記録に書いて止まる（アプリは Running で分かる）
                _log.Error("監視が止まりました", e);
            }
            finally
            {
                var writer = _writer;
                _writer = null;
                _store = null;
                writer?.Dispose(); // 後の MMR を待っている記録を今の値で書き直す
            }
        });
    }

    /// <summary>設定の保持期間で中継ログを整理する（消さない設定なら何もしない）。失敗しても監視は止めない。</summary>
    private void AutoPrune(KifuWriterService writer, SliceStore store)
    {
        if (_config.SlicesRetention is not { } keep) return;
        try { writer.Prune(store, keep); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.Error("一時ログを整理できませんでした", e);
        }
    }

    /// <summary>
    /// 期間に関係なく、消してよい中継ログを整理する（設定画面のボタン）。<paramref name="dryRun"/> なら消さずに件数と大きさだけ。
    /// 監視が動いていれば、記録の書き込みとぶつからないよう同じ KifuWriterService で行う。
    /// </summary>
    public Task<KifuWriterService.PruneResult> PruneSlicesAsync(bool dryRun)
    {
        var writer = _writer;
        var store = _store;
        if (writer == null || store == null) return Task.Run(() => PruneSlices(OutRoot, _log, dryRun));
        return Task.Run(() => writer.Prune(store, keepFor: null, dryRun));
    }

    /// <summary>監視が動いていないときの整理（期間に関係なく）。</summary>
    public static KifuWriterService.PruneResult PruneSlices(string outRoot, IKifuLog log, bool dryRun)
    {
        var store = new SliceStore(Path.Combine(outRoot, ".slices"));
        using var writer = new KifuWriterService(outRoot, log: log);
        writer.UseStore(store);
        return writer.Prune(store, keepFor: null, dryRun);
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
