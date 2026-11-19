using HsbgKifu.Diagnostics;

namespace HsbgKifu.Watch;

/// <summary>
/// 1 つの Power.log の行を順に受け取り、ゲームごとの中継ログに振り分ける（docs/architecture.md「Power.log の読み方」）。
/// <list type="bullet">
/// <item>始まり: <c>CREATE_GAME</c>。直前の <c>DebugPrintPowerList() - Count=</c> の行も新しいゲームに入れる</item>
/// <item>終わり: 画面側（PowerTaskList）の <c>STATE=COMPLETE</c>。この後は数行しか続かないので、そこで閉じる。
///   サーバー側の COMPLETE だけで止まったら、次のゲーム・ファイルの終わり・一定時間の無音（<see cref="FinishIfServerComplete"/>）で閉じる</item>
/// <item>BG 以外のゲームは書かない（<c>DebugPrintGame() - GameType=</c> が分かるまで行をメモリに貯めて決める）</item>
/// <item>書き終えたゲーム（同じログフォルダ + CREATE_GAME の時刻）は読み飛ばす。書いている途中で止まったものは書き直す</item>
/// </list>
/// </summary>
public sealed class GameSlicer : IDisposable
{
    /// <summary>GameType が分からないまま貯める行数の上限。超えたら BG とみなして書き始める。</summary>
    internal const int MaxPendingLines = 5000;

    private readonly LogSession _session;
    private readonly string _sourceFile;
    private readonly SliceStore _store;
    private readonly Func<IReadOnlyList<GameServerEntry>> _servers;
    private readonly IKifuLog _log;
    private readonly SessionClock _clock;

    private LogLine? _heldPowerList;
    private SliceMeta? _pendingMeta;
    private readonly List<string> _pendingLines = new();
    private SliceWriter? _writer;
    private bool _skipping;

    public GameSlicer(LogSession session, string sourceFile, SliceStore store, Func<IReadOnlyList<GameServerEntry>> servers, IKifuLog? log = null)
    {
        _session = session;
        _sourceFile = sourceFile;
        _store = store;
        _servers = servers;
        _log = log ?? NullKifuLog.Instance;
        _clock = new SessionClock(session.Start);
    }

    /// <summary>書いている（または GameType を待っている）ゲーム。</summary>
    public SliceMeta? Current => _writer?.Meta ?? _pendingMeta;

    /// <summary>最後に受け取った行の時刻。</summary>
    public DateTime? LastLineTime { get; private set; }

    public int FinishedCount { get; private set; }

    public void Feed(LogLine line)
    {
        if (PowerLogLine.TimeOfDay(line.Text) is { } tod) LastLineTime = _clock.ToDateTime(tod);

        if (PowerLogLine.IsPowerList(line.Text))
        {
            ReleaseHeld();
            _heldPowerList = line;
            return;
        }
        if (PowerLogLine.IsCreateGame(line.Text))
        {
            CloseOpen();
            StartGame(line);
            return;
        }
        ReleaseHeld();
        Route(line.Text);
    }

    public void Flush() => _writer?.Flush();

    /// <summary>このファイルはもう伸びない（Hearthstone が終わった、別のフォルダに移った）。開いているゲームを閉じる。</summary>
    public void EndOfSource()
    {
        ReleaseHeld();
        CloseOpen();
    }

    /// <summary>サーバー側の COMPLETE を見たのに画面側が来ないまま止まった（結果画面の前に抜けたなど）ときに閉じる。</summary>
    public bool FinishIfServerComplete()
    {
        if (_writer is not { Meta.ServerComplete: true }) return false;
        Finish();
        _skipping = true;
        return true;
    }

    public void Dispose() => _writer?.Dispose();

    private void ReleaseHeld()
    {
        if (_heldPowerList is not { } held) return;
        _heldPowerList = null;
        Route(held.Text);
    }

    private void StartGame(LogLine createGame)
    {
        var held = _heldPowerList;
        _heldPowerList = null;
        var timeText = PowerLogLine.TimeText(createGame.Text);
        if (_store.IsDone(SliceMeta.MakeKey(_session.Name, timeText)))
        {
            _skipping = true;
            return;
        }
        var start = LastLineTime ?? _session.Start;
        var server = GameNetLog.FindFor(_servers(), start);
        _pendingMeta = new SliceMeta
        {
            Session = _session.Name,
            CreateGameTime = timeText,
            SourceFile = Path.GetFileName(_sourceFile),
            SourceOffset = createGame.Offset,
            StartTime = new DateTimeOffset(start),
            GameHandle = server?.GameHandle,
            Reconnecting = server?.Reconnecting,
        };
        _pendingLines.Clear();
        if (held is { } h) _pendingLines.Add(h.Text);
        _pendingLines.Add(createGame.Text);
        _skipping = false;
    }

    private void Route(string text)
    {
        if (_skipping) return;
        if (_pendingMeta is { } pending)
        {
            _pendingLines.Add(text);
            pending.GameType ??= PowerLogLine.GameType(text);
            pending.BuildNumber ??= PowerLogLine.BuildNumber(text);
            if (pending.GameType != null || _pendingLines.Count >= MaxPendingLines) Decide();
            return;
        }
        if (_writer == null) return;
        _writer.Write(text);
        var meta = _writer.Meta;
        meta.BuildNumber ??= PowerLogLine.BuildNumber(text);
        if (PowerLogLine.IsServerComplete(text)) meta.ServerComplete = true;
        else if (PowerLogLine.IsClientComplete(text))
        {
            meta.ClientComplete = true;
            Finish();
            _skipping = true;
        }
    }

    private void Decide()
    {
        var meta = _pendingMeta!;
        _pendingMeta = null;
        if (meta.GameType == null)
            _log.Warn($"{meta.Session} {meta.CreateGameTime}: {MaxPendingLines} 行読んでも GameType が出ないので、BG として記録します");
        else if (!PowerLogLine.IsBattlegrounds(meta.GameType))
        {
            _pendingLines.Clear();
            _skipping = true;
            return;
        }
        _writer = _store.Begin(meta);
        _log.Info($"ゲームの記録を始めます: {meta.Session} {meta.CreateGameTime}（ゲーム番号 {meta.GameHandle?.ToString() ?? "不明"}{(meta.Reconnecting == true ? "、再接続" : "")}）");
        var lines = _pendingLines.ToArray();
        _pendingLines.Clear();
        foreach (var l in lines) Route(l);
    }

    private void CloseOpen()
    {
        if (_pendingMeta != null)
        {
            // GameType が出る前に終わった（ほぼ起きない）。BG か分からないので書かない
            _log.Warn($"{_pendingMeta.Session} {_pendingMeta.CreateGameTime}: GameType が出る前に終わったので記録しません");
            _pendingMeta = null;
            _pendingLines.Clear();
        }
        if (_writer != null) Finish();
        _skipping = false;
    }

    private void Finish()
    {
        var writer = _writer!;
        _writer = null;
        var meta = writer.Meta;
        var status = meta.ServerComplete || meta.ClientComplete ? SliceMeta.StatusComplete : SliceMeta.StatusIncomplete;
        writer.Finish(status, LastLineTime is { } t ? new DateTimeOffset(t) : null);
        FinishedCount++;
        _log.Info($"ゲームの記録を終えました: {meta.Session} {meta.CreateGameTime}（{status}、{meta.LineCount:N0} 行）");
    }
}
