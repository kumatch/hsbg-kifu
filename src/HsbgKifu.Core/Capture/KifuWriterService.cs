using System.Globalization;
using HsbgKifu.Cards;
using HsbgKifu.Diagnostics;
using HsbgKifu.Model;
using HsbgKifu.Output;
using HsbgKifu.Watch;

namespace HsbgKifu.Capture;

/// <summary>
/// 書き終えた中継ログから記録を作って出力先に書く（常駐の監視と作り直しのコマンドが使う）。
/// <list type="bullet">
/// <item>カードの情報（CardDefs.base.xml）は最初に使うときに読み、HDT が更新したら読み直す</item>
/// <item>同じゲームの記録が出力先にすでにあれば書かない（プラグインの記録と二重にしない）。ファイル名の開始時刻が 2 分以内で、
///   game_id が違うものを「同じゲームの別の記録」とみなす。自分の記録（同じ game_id）は上書きする</item>
/// <item>MMR は HDT の BgsLastGames.xml から。常駐中（<c>mmrWait</c> を渡したとき）は、後の MMR が XML に入るまで待って記録を書き直す</item>
/// </list>
/// </summary>
public sealed class KifuWriterService : IDisposable
{
    private static readonly TimeSpan MmrPollInterval = TimeSpan.FromSeconds(2);

    /// <summary>後の MMR を待つのは、終わってからこの時間以内のゲームだけ。</summary>
    private static readonly TimeSpan RecentGame = TimeSpan.FromMinutes(5);

    /// <summary>後の MMR を待っている記録。</summary>
    private sealed record PendingMmr(BuildResult Result, List<string> Events, IReadOnlyList<string> SliceGzs, DateTime Deadline);

    /// <summary>再接続でつなぐ中継ログを探す範囲（同じゲーム番号で、開始時刻がこの時間以内）。</summary>
    private static readonly TimeSpan ReconnectWindow = TimeSpan.FromHours(3);
    private static readonly TimeSpan SameGameWindow = TimeSpan.FromMinutes(2);

    private readonly string _outRoot;
    private readonly string _cardDefsPath;
    private readonly RecorderOptions _recorderOptions;
    private readonly KifuOutput.Options _outputOptions;
    private readonly IKifuLog _log;
    private readonly object _gate = new();
    private CardDb? _cards;
    private DateTime _cardsStamp;
    private List<(DateTime Start, string GameId, string Path)>? _existing;
    private readonly BgsLastGames _ratings;
    private readonly Func<DateTimeOffset> _now;
    private readonly TimeSpan? _mmrWait;
    private readonly List<PendingMmr> _pending = new();
    private Timer? _timer;
    private SliceStore? _store;

    /// <summary>
    /// 再接続でつなぐ中継ログを探す置き場（常駐・作り直しで渡す）。渡しておくと、同じゲーム番号の中継ログ
    /// （Hearthstone が落ちて再接続すると 2 つ以上に分かれる）をまとめて 1 つの記録にする。
    /// </summary>
    public void UseStore(SliceStore store) => _store = store;

    /// <summary>この中継ログと同じゲームの中継ログ（同じゲーム番号で、片方が再接続のもの）を開始時刻の順に。無ければこれだけ。</summary>
    private List<(string Gz, SliceMeta Meta)> Chain(SliceMeta meta, string sliceGz)
    {
        var one = new List<(string, SliceMeta)> { (sliceGz, meta) };
        if (_store == null || meta.GameHandle is not { } handle) return one;
        var same = _store.Finished
            .Where(x => x.Meta.GameHandle == handle && (x.Meta.StartTime - meta.StartTime).Duration() <= ReconnectWindow)
            .Select(x => (x.Gz, x.Meta))
            .OrderBy(x => x.Meta.StartTime)
            .ToList();
        // 2 つ目以降が再接続のものでなければ、たまたま同じ番号の別のゲーム。つながない
        if (same.Count < 2 || same.Skip(1).Any(x => x.Meta.Reconnecting != true) || !same.Any(x => x.Meta.Key == meta.Key)) return one;
        return same;
    }
    /// <param name="mmrWait">常駐中: 後の MMR が BgsLastGames.xml に入るまで待つ最長の時間（null なら待たない。作り直しのコマンド）</param>
    /// <param name="ratings">MMR の読み元（テスト用。既定は %APPDATA%\HearthstoneDeckTracker\BgsLastGames.xml）</param>
    /// <param name="now">今の時刻（テスト用）</param>
    public KifuWriterService(string outRoot, string? cardDefsPath = null, RecorderOptions? recorderOptions = null,
        KifuOutput.Options? outputOptions = null, IKifuLog? log = null, TimeSpan? mmrWait = null, BgsLastGames? ratings = null,
        Func<DateTimeOffset>? now = null)
    {
        _now = now ?? (() => DateTimeOffset.Now);
        _outRoot = outRoot;
        _cardDefsPath = cardDefsPath ?? CardDb.DefaultPath;
        _recorderOptions = recorderOptions ?? new RecorderOptions();
        _outputOptions = outputOptions ?? new KifuOutput.Options();
        _log = log ?? NullKifuLog.Instance;
        _mmrWait = mmrWait;
        _ratings = ratings ?? new BgsLastGames();
    }

    public string OutRoot => _outRoot;

    /// <summary>
    /// 中継ログ 1 つから記録を作って書く。書いた記録のパス（書かなかったら null）。例外は外に出さず動作記録に書く。
    /// </summary>
    public string? Process(SliceMeta meta, string sliceGz)
    {
        lock (_gate)
        {
            try
            {
                if (meta.GameType != null && !meta.GameType.StartsWith("GT_BATTLEGROUNDS", StringComparison.Ordinal)) return null;
                var chain = Chain(meta, sliceGz);
                if (chain.Count > 1)
                    _log.Info($"{meta.Session} {meta.CreateGameTime}: 再接続した同じゲームの中継ログ {chain.Count} つをまとめます");
                var result = KifuBuilder.Build(chain, Cards(), _recorderOptions, _log);
                var gzs = chain.Select(x => x.Gz).ToList();
                if (result.Log == null)
                {
                    _log.Info($"{meta.Session} {meta.CreateGameTime}: 記録しません（ターン 1 の前に終わった）");
                    return null;
                }
                var start = result.Log.Game.StartedAt?.LocalDateTime ?? meta.StartTime.LocalDateTime;
                var other = Existing().FirstOrDefault(e => e.GameId != result.Log.Game.GameId && (e.Start - start).Duration() <= SameGameWindow);
                if (other.Path != null)
                {
                    _log.Info($"{meta.Session} {meta.CreateGameTime}: 同じゲームの記録がすでにあるので書きません（{Path.GetFileName(other.Path)}）");
                    return null;
                }
                var events = result.Events.ToList();
                // 後の MMR を待つのは、終わったばかりのゲームだけ（起動時の回収で作る古いゲームは、BgsLastGames.xml に無ければもう入らない）
                var ended = result.Log.Game.EndedAt ?? result.Log.Game.StartedAt ?? _now();
                var wait = _mmrWait != null && _now() - ended <= RecentGame;
                var mmrDone = ApplyMmr(result.Log, events, final: !wait);
                result = result with { Events = events };
                var path = KifuOutput.Write(result, _outRoot, gzs, _outputOptions);
                if (path != null)
                {
                    Remember(path);
                    _log.Info($"記録を書きました: {Path.GetFileName(path)}（{result.Log.Status}、{result.Log.Turns.Count} ターン、順位 {result.Log.Result.Placement?.ToString() ?? "不明"}" +
                              $"、MMR {result.Log.Result.MmrBefore?.ToString() ?? "-"} → {result.Log.Result.MmrAfter?.ToString() ?? (mmrDone ? "-" : "待っています")}）");
                    if (!mmrDone && _mmrWait is { } waitFor)
                    {
                        _pending.Add(new PendingMmr(result, events, gzs, DateTime.UtcNow + waitFor));
                        _timer ??= new Timer(_ => CheckPending(), null, MmrPollInterval, MmrPollInterval);
                    }
                }
                return path;
            }
            catch (Exception e)
            {
                _log.Error($"{meta.Session} {meta.CreateGameTime}: 記録を作れませんでした", e);
                return null;
            }
            finally
            {
                // 記録を作るのは 1 ゲームに 1 回。中継ログを読み直した分の使い終わったメモリを、常駐中に抱え続けないよう OS に返す
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            }
        }
    }

    /// <summary>
    /// 書き終えた中継ログのうち、まだ記録が無いもの（同じ game_id の記録が出力先に無いもの）から記録を作る（常駐の起動時）。
    /// 中継ログを書き終えてから記録を書く前に止まった場合などの取りこぼしを拾う。同じゲームの別の記録があるものは <see cref="Process"/> が書かない。
    /// 書いた記録の数を返す。
    /// </summary>
    public int CatchUp(SliceStore store)
    {
        var done = 0;
        foreach (var (meta, gz) in store.Finished)
        {
            var id = GameRecorder.GameIdFor(meta);
            bool known;
            lock (_gate) known = Existing().Any(e => e.GameId == id);
            if (known) continue;
            if (Process(meta, gz) != null) done++;
        }
        if (done > 0) _log.Info($"記録が無かった {done} ゲームの記録を書きました");
        return done;
    }

    /// <summary>
    /// BgsLastGames.xml から MMR を入れる。後の MMR まで入ったら true。<paramref name="final"/> なら（もう待たない）取れなかった値を notes に書く。
    /// </summary>
    private bool ApplyMmr(GameLog log, List<string> events, bool final)
    {
        var start = log.Game.StartedAt ?? DateTimeOffset.Now;
        var r = _ratings.Find(start);
        if (r?.Rating is { } before) log.Result.MmrBefore = before;
        if (r?.RatingAfter is { } after && log.Result.MmrAfter == null)
        {
            log.Result.MmrAfter = after;
            if (log.Result.MmrBefore is { } b) log.Result.MmrDelta = after - b;
            var at = log.Game.EndedAt ?? DateTimeOffset.Now;
            events.Add(EventsJsonlWriter.Line(at, log.Result.TotalTurns ?? 0, "mmr_after",
                new[] { new KeyValuePair<string, object?>("mmr_after", after), new KeyValuePair<string, object?>("mmr_delta", log.Result.MmrDelta) }));
        }
        var done = log.Result.MmrAfter != null;
        if (final)
        {
            if (log.Result.MmrBefore == null) log.Notes.Add("mmr_before unavailable");
            if (log.Result.MmrAfter == null) log.Notes.Add("mmr_after unavailable");
        }
        return done || final;
    }

    /// <summary>後の MMR を待っている記録を見直す（タイマーから）。入ったか、待つ時間を過ぎたら書き直す。</summary>
    private void CheckPending()
    {
        lock (_gate)
        {
            foreach (var p in _pending.ToList())
            {
                try
                {
                    var log = p.Result.Log!;
                    var final = DateTime.UtcNow >= p.Deadline;
                    if (!ApplyMmr(log, p.Events, final)) continue;
                    _pending.Remove(p);
                    KifuOutput.Write(p.Result with { Events = p.Events }, _outRoot, p.SliceGzs, _outputOptions with { WritePowerLog = false });
                    _log.Info(log.Result.MmrAfter != null
                        ? $"MMR を記録に入れました: {log.Game.GameId}（{log.Result.MmrBefore?.ToString() ?? "-"} → {log.Result.MmrAfter}）"
                        : $"後の MMR は取れませんでした（BgsLastGames.xml に入らなかった）: {log.Game.GameId}（{log.Result.MmrBefore?.ToString() ?? "-"} → -）");
                }
                catch (Exception e)
                {
                    _pending.Remove(p);
                    _log.Error("MMR を記録に入れられませんでした", e);
                }
            }
            if (_pending.Count == 0)
            {
                _timer?.Dispose();
                _timer = null;
            }
        }
    }

    /// <summary>止める前に、待っている記録を今の値で書き直す（取れなかった MMR は notes に書く）。</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            // 待つ時間を過ぎたことにして、最後にもう 1 度だけ XML を見る
            for (var i = 0; i < _pending.Count; i++) _pending[i] = _pending[i] with { Deadline = DateTime.MinValue };
        }
        CheckPending();
        _timer?.Dispose();
        _timer = null;
    }

    private CardDb Cards()
    {
        var stamp = File.Exists(_cardDefsPath) ? File.GetLastWriteTimeUtc(_cardDefsPath) : DateTime.MinValue;
        if (_cards == null || stamp != _cardsStamp)
        {
            _cards = CardDb.Load(_cardDefsPath);
            _cardsStamp = stamp;
            if (_cards.Count == 0) _log.Warn($"カードの情報がありません（{_cardDefsPath}）。カード名などは空になります");
            else _log.Info($"カードの情報を読みました: {_cards.Count} 枚（build {_cards.Build}）");
        }
        return _cards;
    }

    /// <summary>出力先の記録（ファイル名 yyyyMMdd-HHmmss_&lt;game_id&gt;.json）。ドットで始まるフォルダ（.slices など）は見ない。</summary>
    private List<(DateTime Start, string GameId, string Path)> Existing()
    {
        if (_existing != null) return _existing;
        _existing = new();
        if (!Directory.Exists(_outRoot)) return _existing;
        var stack = new Stack<string>();
        stack.Push(_outRoot);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            foreach (var sub in Directory.EnumerateDirectories(dir))
                if (!Path.GetFileName(sub).StartsWith('.')) stack.Push(sub);
            foreach (var f in Directory.EnumerateFiles(dir, "*.json")) Remember(f);
        }
        return _existing;
    }

    private void Remember(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (name.Length < 17 || name[15] != '_') return;
        if (!DateTime.TryParseExact(name[..15], "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var start)) return;
        _existing ??= new();
        var gameId = name[16..];
        _existing.RemoveAll(e => e.Path == path);
        _existing.Add((start, gameId, path));
    }
}
