using System.Text;
using HsbgKifu.Capture;
using HsbgKifu.Config;
using HsbgKifu.Diagnostics;
using HsbgKifu.Watch;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HsbgKifu.Tests;

/// <summary>中継ログの整理（TASKS.md M9）。</summary>
public class SlicePruneTests : IDisposable
{
    private readonly TempDir _tmp = new();
    public void Dispose() => _tmp.Dispose();

    private static readonly DateTimeOffset Now = new(new DateTime(2026, 11, 20, 12, 0, 0));
    private static readonly TimeSpan Days30 = TimeSpan.FromDays(30);

    private string OutRoot => Path.Combine(_tmp.Path, "out");
    private string SlicesRoot => Path.Combine(OutRoot, ".slices");
    private string None => Path.Combine(_tmp.Path, "none.xml");

    private static string P(string time, string body) => $"D {time} GameState.DebugPrintPower() - {body}";
    private static string G(string time, string body) => $"D {time} GameState.DebugPrintGame() - {body}";

    /// <summary>ターン 1 まで進んで 3 位で終わる（記録になる）ゲーム。</summary>
    private static List<string> RecordedGame(string hh) => new()
    {
        P($"{hh}:00:01.0000000", "CREATE_GAME"),
        P($"{hh}:00:01.0000000", "    GameEntity EntityID=4"),
        P($"{hh}:00:01.0000000", "    Player EntityID=5 PlayerID=2 GameAccountId=[hi=1 lo=2]"),
        P($"{hh}:00:01.0000000", "        tag=HERO_ENTITY value=100"),
        P($"{hh}:00:01.0000000", "    Player EntityID=6 PlayerID=10 GameAccountId=[hi=0 lo=0]"),
        P($"{hh}:00:01.0000000", "        tag=BACON_DUMMY_PLAYER value=1"),
        P($"{hh}:00:01.0000000", "FULL_ENTITY - Creating ID=100 CardID=BG36_HERO_105"),
        P($"{hh}:00:01.0000000", "    tag=CARDTYPE value=HERO"),
        P($"{hh}:00:01.0000000", "    tag=CONTROLLER value=2"),
        P($"{hh}:00:01.0000000", "    tag=ZONE value=PLAY"),
        G($"{hh}:00:01.0000000", "GameType=GT_BATTLEGROUNDS"),
        P($"{hh}:01:00.0000000", "TAG_CHANGE Entity=GameEntity tag=TURN value=1"),
        P($"{hh}:02:00.0000000", "TAG_CHANGE Entity=100 tag=PLAYER_LEADERBOARD_PLACE value=3 "),
        P($"{hh}:02:00.0000000", "TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE "),
    };

    /// <summary>ヒーローを選ぶ前に終わった（記録にならない）ゲーム。</summary>
    private static List<string> ShortGame(string hh) => new()
    {
        P($"{hh}:00:01.0000000", "CREATE_GAME"),
        P($"{hh}:00:01.0000000", "    GameEntity EntityID=4"),
        G($"{hh}:00:01.0000000", "GameType=GT_BATTLEGROUNDS"),
        P($"{hh}:00:30.0000000", "TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE "),
    };

    /// <summary>中継ログを置き場に書く（書き終えたもの。status が recording なら書きかけのまま）。</summary>
    private static SliceMeta AddSlice(SliceStore store, string session, string createTime, IEnumerable<string> lines,
        long? handle = null, bool reconnecting = false, string status = SliceMeta.StatusComplete)
    {
        var start = SessionClock.ParseSessionName(session)!.Value.Date + PowerLogLine.TimeOfDay("D " + createTime + " x")!.Value;
        var meta = new SliceMeta
        {
            Session = session, CreateGameTime = createTime, GameType = "GT_BATTLEGROUNDS", GameHandle = handle, Reconnecting = reconnecting,
            StartTime = new DateTimeOffset(start),
        };
        var w = store.Begin(meta);
        foreach (var l in lines) w.Write(l);
        if (status == SliceMeta.StatusRecording)
        {
            w.Flush();
            w.Dispose();
            return meta;
        }
        w.Finish(status, meta.StartTime.AddMinutes(2));
        return meta;
    }

    private KifuWriterService Writer(SliceStore store, DateTimeOffset? now = null, bool writePowerLog = true, IKifuLog? log = null)
    {
        var at = now ?? Now;
        var w = new KifuWriterService(OutRoot, cardDefsPath: None, outputOptions: new KifuOutput.Options(true, writePowerLog),
            log: log, ratings: new BgsLastGames(None), now: () => at);
        w.UseStore(store);
        return w;
    }

    private static string? Process(KifuWriterService w, SliceStore store, SliceMeta meta) =>
        w.Process(meta, store.Finished.Single(x => x.Meta.Key == meta.Key).Gz);

    private string[] SliceGzs() => Directory.GetFiles(SlicesRoot, "*.power.log.gz", SearchOption.AllDirectories);

    private sealed class ListLog : IKifuLog
    {
        private readonly List<string> _lines = new();
        public List<string> Lines { get { lock (_lines) return _lines.ToList(); } }
        public void Info(string message) { lock (_lines) _lines.Add(message); }
        public void Warn(string message) { lock (_lines) _lines.Add(message); }
        public void Error(string message, Exception? exception = null) { lock (_lines) _lines.Add(message); }
    }

    [Fact]
    public void PrunesRecordedAndNotRecordedSlicesAfterTheRetentionAndKeepsTheirMeta()
    {
        var store = new SliceStore(SlicesRoot);
        var recorded = AddSlice(store, "Hearthstone_2026_10_01_10_00_00", "10:00:01.0000000", RecordedGame("10"));
        var tooShort = AddSlice(store, "Hearthstone_2026_10_01_10_00_00", "11:00:01.0000000", ShortGame("11"));
        var log = new ListLog();
        var w = Writer(store, log: log);
        var path = Process(w, store, recorded)!;
        Assert.Null(Process(w, store, tooShort));

        // 記録との対応を付帯情報に書いた
        Assert.Equal(SliceMeta.OutcomeRecorded, recorded.Outcome);
        Assert.Equal(Path.GetRelativePath(OutRoot, path)[..^".json".Length].Replace('\\', '/'), recorded.Record);
        Assert.Equal(SliceMeta.OutcomeNotRecorded, tooShort.Outcome);
        Assert.Null(tooShort.Record);

        Assert.Equal(new KifuWriterService.PruneResult(2, SliceGzs().Sum(f => new FileInfo(f).Length)), w.Prune(store, Days30, dryRun: true));
        Assert.Equal(2, SliceGzs().Length); // dryRun では消さない

        var done = w.Prune(store, Days30);
        Assert.Equal(2, done.Count);
        Assert.Empty(SliceGzs());
        Assert.Equal(2, Directory.GetFiles(SlicesRoot, "*.slice.json", SearchOption.AllDirectories).Length); // 付帯情報は残す
        Assert.True(File.Exists(path));
        Assert.True(File.Exists(path.Replace(".json", ".power.log.gz"))); // 記録の横の写しは残る
        Assert.Contains(log.Lines, l => l.StartsWith("一時ログを 2 件整理しました（約 ", StringComparison.Ordinal));

        // 読み直しても整理済み: 作り直しには出さず、切り出し直さない
        var reloaded = new SliceStore(SlicesRoot);
        Assert.Empty(reloaded.Finished);
        Assert.True(reloaded.IsDone(recorded.Key));
        Assert.True(reloaded.IsDone(tooShort.Key));
        var meta = Assert.Single(reloaded.All, m => m.Key == recorded.Key);
        Assert.Equal(Now, meta.PrunedAt);
        Assert.Equal(SliceMeta.CurrentFormat, meta.Format);
        Assert.Equal(recorded.Record, meta.Record);

        // 2 回目は何もしない（動作記録にも書かない）
        var before = log.Lines.Count;
        Assert.Equal(0, Writer(reloaded, log: log).Prune(reloaded, Days30).Count);
        Assert.Equal(before, log.Lines.Count);
    }

    [Fact]
    public void KeepsSlicesWithinTheRetentionAndRecentOnesEvenWhenPruningRegardlessOfTheRetention()
    {
        var store = new SliceStore(SlicesRoot);
        var tenDaysAgo = AddSlice(store, "Hearthstone_2026_11_10_10_00_00", "10:00:01.0000000", RecordedGame("10"));
        var hourAgo = AddSlice(store, "Hearthstone_2026_11_20_10_00_00", "11:00:01.0000000", RecordedGame("11"));
        var w = Writer(store);
        Process(w, store, tenDaysAgo);
        Process(w, store, hourAgo);

        Assert.Equal(0, w.Prune(store, Days30).Count);
        Assert.Equal(0, w.Prune(store, TimeSpan.FromDays(10.1)).Count);
        // 期間に関係なく（設定画面のボタン）: 10 日前のものは消し、始まってから 3 時間以内のもの（再接続でつなぐかもしれない）は残す
        Assert.Equal(1, w.Prune(store, null).Count);
        Assert.NotNull(store.All.Single(m => m.Key == tenDaysAgo.Key).PrunedAt);
        Assert.Null(store.All.Single(m => m.Key == hourAgo.Key).PrunedAt);
        Assert.Single(SliceGzs());
    }

    [Fact]
    public void KeepsSlicesWithoutAPowerLogBesideTheRecordOrWithoutARecord()
    {
        var store = new SliceStore(SlicesRoot);
        var noPowerLog = AddSlice(store, "Hearthstone_2026_10_01_10_00_00", "10:00:01.0000000", RecordedGame("10"));
        var notProcessed = AddSlice(store, "Hearthstone_2026_10_01_10_00_00", "12:00:01.0000000", RecordedGame("12")); // 記録をまだ作っていない・作るのに失敗した
        var recordDeleted = AddSlice(store, "Hearthstone_2026_10_01_10_00_00", "13:00:01.0000000", RecordedGame("13"));
        AddSlice(store, "Hearthstone_2026_10_01_10_00_00", "14:00:01.0000000", RecordedGame("14"), status: SliceMeta.StatusRecording);

        Process(Writer(store, writePowerLog: false), store, noPowerLog);
        var w = Writer(store);
        var deleted = Process(w, store, recordDeleted)!;
        File.Delete(deleted);

        Assert.Equal(0, w.Prune(store, null).Count);
        Assert.Equal(3, SliceGzs().Length);
        Assert.Null(notProcessed.Outcome);
    }

    [Fact]
    public void PrunesASliceOfAnotherRecordOfTheSameGameOnlyWhenThatRecordHasAPowerLog()
    {
        var store = new SliceStore(SlicesRoot);
        var meta = AddSlice(store, "Hearthstone_2026_10_01_10_00_00", "10:00:01.0000000", RecordedGame("10"));
        // プラグインの記録（開始時刻が 1 秒前、game_id が違う）
        var plugin = Path.Combine(_tmp.Sub("out", "2026", "10"), "20261001-100000_11111111-2222-3333-4444-555555555555");
        File.WriteAllText(plugin + ".json", "{}");
        var w = Writer(store);
        Assert.Null(Process(w, store, meta));
        Assert.Equal(SliceMeta.OutcomeDuplicate, meta.Outcome);
        Assert.Equal("2026/10/20261001-100000_11111111-2222-3333-4444-555555555555", meta.Record);

        Assert.Equal(0, w.Prune(store, Days30).Count); // プラグインの記録の横に power.log.gz が無い
        TestLogs.WriteGz(plugin + ".power.log.gz", "x\n");
        Assert.Equal(1, w.Prune(store, Days30).Count);
    }

    [Fact]
    public void UsesTheGameIdForSlicesWithoutTheRecordMark()
    {
        // 前の版で書いた中継ログ: 付帯情報に記録との対応が無い。同じ game_id の記録で判断する
        var store = new SliceStore(SlicesRoot);
        var meta = AddSlice(store, "Hearthstone_2026_10_01_10_00_00", "10:00:01.0000000", RecordedGame("10"));
        var noStore = new KifuWriterService(OutRoot, cardDefsPath: None, ratings: new BgsLastGames(None), now: () => Now); // 付帯情報を書き直さない
        Assert.NotNull(noStore.Process(meta, store.Finished.Single().Gz));

        var reloaded = new SliceStore(SlicesRoot);
        Assert.Null(reloaded.All.Single().Outcome);
        Assert.Equal(1, Writer(reloaded).Prune(reloaded, Days30).Count);
    }

    [Fact]
    public void MarksEverySliceOfAReconnectedGameWithTheSameRecord()
    {
        static List<string> Players(string t, int turn, int heroDamage) => new()
        {
            P(t, "CREATE_GAME"),
            P(t, "    GameEntity EntityID=4"),
            P(t, $"        tag=TURN value={turn}"),
            P(t, "    Player EntityID=5 PlayerID=2 GameAccountId=[hi=1 lo=2]"),
            P(t, "        tag=CARDTYPE value=PLAYER"),
            P(t, "        tag=HERO_ENTITY value=100"),
            P(t, "    Player EntityID=6 PlayerID=10 GameAccountId=[hi=0 lo=0]"),
            P(t, "        tag=CARDTYPE value=PLAYER"),
            P(t, "        tag=BACON_DUMMY_PLAYER value=1"),
            P(t, "FULL_ENTITY - Creating ID=100 CardID=BG36_HERO_105"),
            P(t, "    tag=CARDTYPE value=HERO"),
            P(t, "    tag=CONTROLLER value=2"),
            P(t, "    tag=ZONE value=PLAY"),
            P(t, "    tag=HEALTH value=30"),
            P(t, $"    tag=DAMAGE value={heroDamage}"),
            P(t, "TAG_CHANGE Entity=4 tag=STATE value=RUNNING "),
            G(t, "GameType=GT_BATTLEGROUNDS"),
        };
        var first = Players("10:00:01.0000000", 0, 0);
        first.Add(P("10:01:00.0000000", "TAG_CHANGE Entity=GameEntity tag=TURN value=1"));
        var second = Players("10:03:00.0000000", 3, 5);
        second.Add(P("10:05:00.0000000", "TAG_CHANGE Entity=100 tag=PLAYER_LEADERBOARD_PLACE value=2 "));
        second.Add(P("10:05:00.0000000", "TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE "));

        var store = new SliceStore(SlicesRoot);
        var a = AddSlice(store, "Hearthstone_2026_10_17_10_00_00", "10:00:01.0000000", first, handle: 1936, status: SliceMeta.StatusIncomplete);
        var w = Writer(store);
        Process(w, store, a);
        var b = AddSlice(store, "Hearthstone_2026_10_17_10_02_30", "10:03:00.0000000", second, handle: 1936, reconnecting: true);
        var path = Process(w, store, b)!;

        Assert.Contains(GameRecorder.GameIdFor(a), path); // 最初の中継ログの ID の記録
        var reloaded = new SliceStore(SlicesRoot);
        Assert.All(reloaded.All, m =>
        {
            Assert.Equal(SliceMeta.OutcomeRecorded, m.Outcome);
            Assert.Equal(Path.GetRelativePath(OutRoot, path)[..^".json".Length].Replace('\\', '/'), m.Record);
        });
        Assert.Equal(2, Writer(reloaded).Prune(reloaded, Days30).Count);
    }

    [Fact]
    public void CatchUpDoesNotRebuildSlicesThatAreMarkedAsNotRecordedOrAsAnotherRecord()
    {
        var store = new SliceStore(SlicesRoot);
        var tooShort = AddSlice(store, "Hearthstone_2026_10_01_10_00_00", "11:00:01.0000000", ShortGame("11"));
        var meta = AddSlice(store, "Hearthstone_2026_10_01_10_00_00", "10:00:01.0000000", RecordedGame("10"));
        File.WriteAllText(Path.Combine(_tmp.Sub("out", "2026", "10"), "20261001-100000_11111111-2222-3333-4444-555555555555.json"), "{}");

        var log = new ListLog();
        Assert.Equal(0, Writer(store, log: log).CatchUp(store));
        Assert.Equal(SliceMeta.OutcomeNotRecorded, tooShort.Outcome);
        Assert.Equal(SliceMeta.OutcomeDuplicate, meta.Outcome);
        var before = log.Lines.Count;

        // 次の起動: 付帯情報を見て、もう作らない（動作記録にも書かない）
        var reloaded = new SliceStore(SlicesRoot);
        Assert.Equal(0, Writer(reloaded, log: log).CatchUp(reloaded));
        Assert.Equal(before, log.Lines.Count);
    }

    [Fact]
    public void PrunedSlicesAreNotSlicedAgainFromTheLogFolder()
    {
        var session = new LogSession("Hearthstone_2026_10_01_10_00_00", "unused", new DateTime(2026, 10, 1, 10, 0, 0));
        var lines = RecordedGame("10");
        lines.Add("D 10:02:10.0000000 PowerTaskList.DebugPrintPower() -     TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE ");
        void SliceFromTheLogFolder()
        {
            using var slicer = new GameSlicer(session, "Power.log", new SliceStore(SlicesRoot), () => Array.Empty<GameServerEntry>());
            foreach (var l in TestLogs.ToLogLines(lines)) slicer.Feed(l);
        }
        SliceFromTheLogFolder();
        var store = new SliceStore(SlicesRoot);
        var w = Writer(store);
        Assert.Equal(1, w.CatchUp(store));
        Assert.Equal(1, w.Prune(store, Days30).Count);

        SliceFromTheLogFolder(); // 起動時の回収（Logs にフォルダが残っている）
        Assert.Empty(SliceGzs());
    }

    [Fact]
    public void FormatsSizes()
    {
        Assert.Equal("約 1 KB", KifuWriterService.FormatSize(10));
        Assert.Equal("約 2 KB", KifuWriterService.FormatSize(1025));
        Assert.Equal("約 2.5 MB", KifuWriterService.FormatSize(5L << 19));
        Assert.Equal("約 1.5 GB", KifuWriterService.FormatSize(3L << 29));
    }
}

/// <summary>常駐（KifuService）で整理が動くとき: 起動時の回収の後と、記録を書いた後。</summary>
public class KifuServicePruneTests : IDisposable
{
    private readonly TempDir _tmp = new();
    public void Dispose() => _tmp.Dispose();

    private string OutRoot => Path.Combine(_tmp.Path, "out");
    private string SlicesRoot => Path.Combine(OutRoot, ".slices");
    private string None => Path.Combine(_tmp.Path, "none.xml");

    private static List<string> Game(string hh)
    {
        string P(string t, string b) => $"D {hh}:{t} GameState.DebugPrintPower() - {b}";
        return new List<string>
        {
            P("00:01.0000000", "CREATE_GAME"),
            P("00:01.0000000", "    GameEntity EntityID=4"),
            P("00:01.0000000", "    Player EntityID=5 PlayerID=2 GameAccountId=[hi=1 lo=2]"),
            P("00:01.0000000", "        tag=HERO_ENTITY value=100"),
            P("00:01.0000000", "    Player EntityID=6 PlayerID=10 GameAccountId=[hi=0 lo=0]"),
            P("00:01.0000000", "        tag=BACON_DUMMY_PLAYER value=1"),
            P("00:01.0000000", "FULL_ENTITY - Creating ID=100 CardID=BG36_HERO_105"),
            P("00:01.0000000", "    tag=CARDTYPE value=HERO"),
            P("00:01.0000000", "    tag=CONTROLLER value=2"),
            P("00:01.0000000", "    tag=ZONE value=PLAY"),
            $"D {hh}:00:01.0000000 GameState.DebugPrintGame() - GameType=GT_BATTLEGROUNDS",
            P("01:00.0000000", "TAG_CHANGE Entity=GameEntity tag=TURN value=1"),
            P("02:00.0000000", "TAG_CHANGE Entity=100 tag=PLAYER_LEADERBOARD_PLACE value=3 "),
            P("02:00.0000000", "TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE "),
            $"D {hh}:02:10.0000000 PowerTaskList.DebugPrintPower() -     TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE ",
        };
    }

    private KifuService Service(int? retentionDays) =>
        new(new KifuConfig { OutputDir = OutRoot, HearthstoneDir = _tmp.Path, MmrAfterWaitSec = 0, SlicesRetentionDays = retentionDays }, NullKifuLog.Instance)
        {
            CardDefsPath = None,
            Ratings = new BgsLastGames(None),
        };

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++) await Task.Delay(50);
        Assert.True(condition());
    }

    private bool AllPruned() =>
        new SliceStore(SlicesRoot).All is { Count: > 0 } all && all.All(m => m.PrunedAt != null);

    [Fact]
    public async Task PrunesOldSlicesAfterCatchingUpAtStart()
    {
        // 終わったログフォルダ（2020 年 = 保持期間を過ぎた）。起動時の回収で切り出して記録を作り、その後で整理する
        var old = _tmp.Sub("Logs", "Hearthstone_2020_10_01_10_00_00");
        File.WriteAllText(Path.Combine(old, "Power_old.log"), TestLogs.Join(Game("10")));
        await using var service = Service(30);
        service.Start();
        await WaitUntil(AllPruned);
        Assert.Single(Directory.GetFiles(Path.Combine(OutRoot, "2020"), "*.power.log.gz", SearchOption.AllDirectories)); // 記録の横の写しは残る
        await service.StopAsync();
    }

    [Fact]
    public async Task PrunesAfterWritingARecordWhileFollowingThePowerLog()
    {
        var live = _tmp.Sub("Logs", "Hearthstone_2020_10_01_10_00_00");
        var power = Path.Combine(live, "Power.log");
        File.WriteAllText(power, "");
        await using var service = Service(30);
        service.Start();
        await WaitUntil(() => Directory.Exists(SlicesRoot)); // 監視が始まった
        using (var fs = new FileStream(power, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            var b = Encoding.UTF8.GetBytes(TestLogs.Join(Game("10")));
            fs.Write(b, 0, b.Length);
        }
        await WaitUntil(AllPruned);
        await service.StopAsync();
    }

    [Fact]
    public async Task DoesNotPruneWhenTheRetentionIsOff()
    {
        var old = _tmp.Sub("Logs", "Hearthstone_2020_10_01_10_00_00");
        File.WriteAllText(Path.Combine(old, "Power_old.log"), TestLogs.Join(Game("10")));
        await using var service = Service(null);
        service.Start();
        await WaitUntil(() => Directory.Exists(Path.Combine(OutRoot, "2020")) &&
                              Directory.GetFiles(Path.Combine(OutRoot, "2020"), "*.power.log.gz", SearchOption.AllDirectories).Length == 1);
        await service.StopAsync();
        Assert.Single(Directory.GetFiles(SlicesRoot, "*.power.log.gz", SearchOption.AllDirectories));

        // 設定画面のボタン（期間に関係なく）: 止まっていても今の出力先で整理できる
        var plan = KifuService.PruneSlices(OutRoot, NullKifuLog.Instance, dryRun: true);
        Assert.Equal(1, plan.Count);
        Assert.Equal(1, KifuService.PruneSlices(OutRoot, NullKifuLog.Instance, dryRun: false).Count);
        Assert.Empty(Directory.GetFiles(SlicesRoot, "*.power.log.gz", SearchOption.AllDirectories));
    }
}

public class SlicesRetentionConfigTests : IDisposable
{
    private readonly TempDir _tmp = new();
    public void Dispose() => _tmp.Dispose();

    [Fact]
    public void DefaultsTo30DaysAndKeepsNullAsKeepForever()
    {
        var path = Path.Combine(_tmp.Path, "config.json");
        var created = KifuConfig.Load(path, out _);
        Assert.Equal(30, created.SlicesRetentionDays);
        Assert.Equal(TimeSpan.FromDays(30), created.SlicesRetention);
        Assert.Equal(30, (int?)JObject.Parse(File.ReadAllText(path))["slices_retention_days"]);

        created.SlicesRetentionDays = null;
        created.Save(path);
        var loaded = KifuConfig.Load(path, out var problem);
        Assert.Null(problem);
        Assert.Null(loaded.SlicesRetentionDays);
        Assert.Null(loaded.SlicesRetention);

        // 前の版の config.json（キーが無い）は既定の 30 日
        File.WriteAllText(path, "{\"output_dir\": \"%USERPROFILE%\\\\Documents\\\\HSBG-Logs\"}");
        Assert.Equal(30, KifuConfig.Load(path, out _).SlicesRetentionDays);

        Assert.Null(new KifuConfig { SlicesRetentionDays = 0 }.SlicesRetention);
        Assert.Equal(TimeSpan.FromDays(90), new KifuConfig { SlicesRetentionDays = 90 }.SlicesRetention);
        Assert.Equal(new[] { 30, 60, 90 }, KifuConfig.SlicesRetentionChoices);
    }
}
