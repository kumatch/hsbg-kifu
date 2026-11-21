using System.Globalization;
using HsbgKifu.Cards;
using HsbgKifu.Diagnostics;
using HsbgKifu.Watch;

namespace HsbgKifu.Capture;

/// <summary>
/// 書き終えた中継ログから記録を作って出力先に書く（常駐の監視と作り直しのコマンドが使う）。
/// <list type="bullet">
/// <item>カードの情報（CardDefs.base.xml）は最初に使うときに読み、HDT が更新したら読み直す</item>
/// <item>同じゲームの記録が出力先にすでにあれば書かない（プラグインの記録と二重にしない）。ファイル名の開始時刻が 2 分以内で、
///   game_id が違うものを「同じゲームの別の記録」とみなす。自分の記録（同じ game_id）は上書きする</item>
/// </list>
/// </summary>
public sealed class KifuWriterService
{
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

    public KifuWriterService(string outRoot, string? cardDefsPath = null, RecorderOptions? recorderOptions = null,
        KifuOutput.Options? outputOptions = null, IKifuLog? log = null)
    {
        _outRoot = outRoot;
        _cardDefsPath = cardDefsPath ?? CardDb.DefaultPath;
        _recorderOptions = recorderOptions ?? new RecorderOptions();
        _outputOptions = outputOptions ?? new KifuOutput.Options();
        _log = log ?? NullKifuLog.Instance;
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
                var result = KifuBuilder.Build(sliceGz, meta, Cards(), _recorderOptions, _log);
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
                var path = KifuOutput.Write(result, _outRoot, sliceGz, _outputOptions);
                if (path != null)
                {
                    Remember(path);
                    _log.Info($"記録を書きました: {Path.GetFileName(path)}（{result.Log.Status}、{result.Log.Turns.Count} ターン、順位 {result.Log.Result.Placement?.ToString() ?? "不明"}）");
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
