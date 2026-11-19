using System.Diagnostics;
using HsbgKifu.Diagnostics;

namespace HsbgKifu.Watch;

public sealed class KifuWatcherOptions
{
    /// <summary><c>&lt;Hearthstone のフォルダ&gt;\Logs</c>。</summary>
    public required string LogsDirectory { get; init; }
    public required string SlicesRoot { get; init; }
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(250);
    /// <summary>サーバー側の COMPLETE の後、画面側の COMPLETE が来ないまま無音が続いたら閉じるまでの時間。</summary>
    public TimeSpan ServerCompleteIdle { get; init; } = TimeSpan.FromSeconds(60);
    /// <summary>Hearthstone のプロセスが無く、ログも伸びないまま、この時間がたったら開いているゲームを未完了で閉じる。</summary>
    public TimeSpan HearthstoneGoneGrace { get; init; } = TimeSpan.FromSeconds(15);
    public Func<bool> IsHearthstoneRunning { get; init; } = () => Process.GetProcessesByName("Hearthstone").Length > 0;
    public Func<DateTime> UtcNow { get; init; } = () => DateTime.UtcNow;
}

/// <summary>終わったログフォルダをまとめて切り出す（起動時の回収、M0 の退避先の取り込み）。</summary>
public static class BatchSlicer
{
    public static int SliceSession(LogSession session, SliceStore store, IKifuLog log)
    {
        var servers = session.ReadGameServers();
        var finished = 0;
        foreach (var file in session.PowerLogFiles())
        {
            using var slicer = new GameSlicer(session, file, store, () => servers, log);
            var splitter = new LineSplitter();
            var lines = new List<LogLine>();
            var buffer = new byte[1 << 20];
            using (var stream = LogFiles.OpenRead(file))
            {
                int n;
                while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    splitter.Append(buffer.AsSpan(0, n), lines);
                    foreach (var l in lines) slicer.Feed(l);
                    lines.Clear();
                }
            }
            if (splitter.FlushPending() is { } last) slicer.Feed(last);
            slicer.EndOfSource();
            finished += slicer.FinishedCount;
        }
        return finished;
    }

    public static int SliceAll(string logsDirectory, SliceStore store, IKifuLog log)
    {
        var total = 0;
        foreach (var s in LogSession.List(logsDirectory))
        {
            try { total += SliceSession(s, store, log); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                log.Error($"{s.Name} を読めませんでした", e);
            }
        }
        return total;
    }
}

/// <summary>
/// 常駐して Power.log を読み続け、ゲームごとの中継ログを書く（TASKS.md M1）。
/// 起動時は、残っているログフォルダのうち書いていないゲームを回収し、いちばん新しいフォルダの Power.log を頭から読んで後を追う。
/// 止めたときに書きかけのゲームは recording のまま残し、次の起動で Power.log から書き直す。
/// </summary>
public sealed class KifuWatcher
{
    private static readonly TimeSpan ProcessCheckInterval = TimeSpan.FromSeconds(5);

    private readonly KifuWatcherOptions _options;
    private readonly IKifuLog _log;
    private readonly SliceStore _store;
    private readonly HashSet<string> _finishedSessions = new();

    private LogSession? _session;
    private LogTailer? _tailer;
    private GameSlicer? _slicer;
    private DateTime _lastData;
    private DateTime _lastProcessCheck = DateTime.MinValue;
    private bool _hearthstoneRunning = true;

    public KifuWatcher(KifuWatcherOptions options, IKifuLog? log = null)
    {
        _options = options;
        _log = log ?? NullKifuLog.Instance;
        _store = new SliceStore(options.SlicesRoot);
    }

    public SliceStore Store => _store;

    /// <summary>今追っているログフォルダ（無ければ null）。</summary>
    public string? CurrentSession => _session?.Name;

    public async Task RunAsync(CancellationToken cancel)
    {
        _log.Info($"Power.log の監視を始めます: {_options.LogsDirectory}");
        CatchUp();
        while (!cancel.IsCancellationRequested)
        {
            try { Tick(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _log.Error("Power.log の読み取りで失敗しました（続けます）", e);
            }
            try { await Task.Delay(_options.PollInterval, cancel).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
        _slicer?.Flush();
        _slicer?.Dispose();
        _tailer?.Dispose();
        _log.Info("Power.log の監視を止めました");
    }

    /// <summary>1 回分の処理（テストから直接呼ぶ）。</summary>
    internal void Tick()
    {
        var sessions = LogSession.List(_options.LogsDirectory);
        var newest = sessions.Count > 0 ? sessions[^1] : null;

        // 新しいログフォルダができた（Hearthstone を起動し直した）
        if (_session != null && newest != null && newest.Name != _session.Name) CloseSession(drain: true);

        if (_session == null && newest != null && !_finishedSessions.Contains(newest.Name))
        {
            // 間のフォルダ（ポーリングの間に起動・終了したもの）を回収
            foreach (var s in sessions)
                if (s.Name != newest.Name && !_finishedSessions.Contains(s.Name)) BatchSession(s);
            if (newest.LivePowerLog() is { } live) Open(newest, live);
            else if (newest.PowerLogFiles().Count > 0) BatchSession(newest);
        }

        if (_tailer == null || _slicer == null) return;

        var now = _options.UtcNow();
        var got = 0;
        do
        {
            var lines = _tailer.ReadAvailable();
            foreach (var l in lines) _slicer.Feed(l);
            got += lines.Count;
        } while (_tailer.HasMore);
        _slicer.Flush();
        if (got > 0) _lastData = now;

        if (_tailer.Truncated)
        {
            _log.Warn($"{_tailer.Path} が短くなったので、頭から読み直します");
            var session = _session!;
            CloseSession(drain: false);
            _finishedSessions.Remove(session.Name);
            return;
        }
        if (!File.Exists(_tailer.Path))
        {
            // 終了時に Power.log → Power_old.log に名前が変わった。開いたままなので残りを読み切って閉じる
            CloseSession(drain: true);
            return;
        }
        if (got > 0 || _slicer.Current == null) return;

        if (now - _lastData >= _options.ServerCompleteIdle && _slicer.FinishIfServerComplete()) return;
        if (now - _lastProcessCheck >= ProcessCheckInterval)
        {
            _lastProcessCheck = now;
            _hearthstoneRunning = _options.IsHearthstoneRunning();
        }
        if (!_hearthstoneRunning && now - _lastData >= _options.HearthstoneGoneGrace)
        {
            _log.Warn("Hearthstone が終わったのにゲームが閉じていないので、ここまでを記録します（落ちた可能性）");
            _slicer.EndOfSource();
        }
    }

    private void CatchUp()
    {
        var sessions = LogSession.List(_options.LogsDirectory);
        for (var i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            var isNewest = i == sessions.Count - 1;
            // いちばん新しいフォルダは Tick に任せる: 起動中なら後を追い、Power.log がまだ無ければ（起動直後）できるのを待つ
            if (isNewest && (s.LivePowerLog() != null || s.PowerLogFiles().Count == 0)) continue;
            BatchSession(s);
        }
    }

    private void BatchSession(LogSession s)
    {
        try
        {
            var n = BatchSlicer.SliceSession(s, _store, _log);
            if (n > 0) _log.Info($"{s.Name} から {n} ゲームを回収しました");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _log.Error($"{s.Name} を読めませんでした", e);
        }
        _finishedSessions.Add(s.Name);
    }

    private void Open(LogSession session, string powerLog)
    {
        _session = session;
        _tailer = new LogTailer(powerLog);
        // ゲーム番号は CREATE_GAME の直前に GameNetLogger.log に書かれるので、ゲームが始まるたびに読み直す
        _slicer = new GameSlicer(session, powerLog, _store, () => session.ReadGameServers(), _log);
        _lastData = _options.UtcNow();
        _log.Info($"{session.Name}\\Power.log の後を追います");
    }

    private void CloseSession(bool drain)
    {
        if (_tailer != null && _slicer != null && drain)
        {
            List<LogLine> lines;
            do
            {
                lines = _tailer.ReadAvailable();
                foreach (var l in lines) _slicer.Feed(l);
            } while (lines.Count > 0 || _tailer.HasMore);
            if (_tailer.FlushPending() is { } last) _slicer.Feed(last);
            _slicer.EndOfSource();
        }
        else
        {
            _slicer?.Flush();
        }
        _slicer?.Dispose();
        _tailer?.Dispose();
        if (_session != null && drain) _finishedSessions.Add(_session.Name);
        _session = null;
        _slicer = null;
        _tailer = null;
    }
}
