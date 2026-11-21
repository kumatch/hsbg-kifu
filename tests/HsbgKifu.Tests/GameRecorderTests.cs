using HsbgKifu.Capture;
using HsbgKifu.Cards;
using HsbgKifu.Model;
using HsbgKifu.Parse;
using HsbgKifu.Watch;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HsbgKifu.Tests;

public class GameRecorderTests
{
    private static readonly SliceMeta Meta = new()
    {
        Session = "Hearthstone_2026_11_20_10_00_00",
        CreateGameTime = "10:00:01.0000000",
        GameType = "GT_BATTLEGROUNDS",
        BuildNumber = 255790,
        StartTime = new DateTimeOffset(new DateTime(2026, 11, 20, 10, 0, 1)),
        EndTime = new DateTimeOffset(new DateTime(2026, 11, 20, 10, 20, 0)),
    };

    private static string P(string time, string body) => $"D {time} GameState.DebugPrintPower() - {body}";
    private static string PL(string time) => $"D {time} GameState.DebugPrintPowerList() - Count=1";
    private static string G(string time, string body) => $"D {time} GameState.DebugPrintGame() - {body}";

    /// <summary>自分（PlayerID 2、Entity 5、ヒーロー 100）と Bob 役（PlayerID 10、Entity 6）。</summary>
    private static List<string> Setup(bool withHero = true)
    {
        var lines = SetupLines();
        if (withHero) return lines;
        // ヒーロー（Entity 100）とその tag= 行を除く
        var i = lines.FindIndex(l => l.Contains("ID=100 CardID="));
        lines.RemoveRange(i, 7);
        return lines.Where(l => !l.Contains("HERO_ENTITY")).ToList();
    }

    private static List<string> SetupLines() => new()
    {
        PL("10:00:01.0000000"),
        P("10:00:01.0000000", "CREATE_GAME"),
        P("10:00:01.0000000", "    GameEntity EntityID=4"),
        P("10:00:01.0000000", "        tag=TURN value=0"),
        P("10:00:01.0000000", "    Player EntityID=5 PlayerID=2 GameAccountId=[hi=1 lo=2]"),
        P("10:00:01.0000000", "        tag=CARDTYPE value=PLAYER"),
        P("10:00:01.0000000", "        tag=HERO_ENTITY value=100"),
        P("10:00:01.0000000", "        tag=RESOURCES value=3"),
        P("10:00:01.0000000", "    Player EntityID=6 PlayerID=10 GameAccountId=[hi=0 lo=0]"),
        P("10:00:01.0000000", "        tag=CARDTYPE value=PLAYER"),
        P("10:00:01.0000000", "        tag=BACON_DUMMY_PLAYER value=1"),
        P("10:00:01.0000000", "FULL_ENTITY - Creating ID=100 CardID=BG36_HERO_105"),
        P("10:00:01.0000000", "    tag=CARDTYPE value=HERO"),
        P("10:00:01.0000000", "    tag=CONTROLLER value=2"),
        P("10:00:01.0000000", "    tag=ZONE value=PLAY"),
        P("10:00:01.0000000", "    tag=HEALTH value=30"),
        P("10:00:01.0000000", "    tag=ARMOR value=6"),
        P("10:00:01.0000000", "    tag=PLAYER_TECH_LEVEL value=1"),
        P("10:00:01.0000000", "FULL_ENTITY - Creating ID=7 CardID=TB_BaconShop_DragBuy"),
        P("10:00:01.0000000", "    tag=CONTROLLER value=2"),
        P("10:00:01.0000000", "    tag=ZONE value=PLAY"),
        P("10:00:01.0000000", "FULL_ENTITY - Creating ID=8 CardID=TB_BaconShop_8p_Reroll_Button"),
        P("10:00:01.0000000", "    tag=CONTROLLER value=2"),
        P("10:00:01.0000000", "    tag=ZONE value=PLAY"),
        P("10:00:01.0000000", "TAG_CHANGE Entity=4 tag=STATE value=RUNNING "),
        G("10:00:01.0000000", "GameType=GT_BATTLEGROUNDS"),
        G("10:00:01.0000000", "PlayerID=2, PlayerName=Me"),
        G("10:00:01.0000000", "PlayerID=10, PlayerName=Bobby"),
    };

    /// <summary>ターン 1: 買って、リロールして、ショップの終わり。戦闘で相手のヒーローに 3（PREDAMAGE が 2 回立つ）。ターン 2 の始まりで確定。</summary>
    private static List<string> OneTurn() => new()
    {
        PL("10:01:00.0000000"),
        P("10:01:00.0000000", "TAG_CHANGE Entity=GameEntity tag=TURN value=1"),
        P("10:01:00.0000000", "FULL_ENTITY - Creating ID=200 CardID=BG31_815"),
        P("10:01:00.0000000", "    tag=CARDTYPE value=MINION"),
        P("10:01:00.0000000", "    tag=CONTROLLER value=10"),
        P("10:01:00.0000000", "    tag=ZONE value=PLAY"),
        P("10:01:00.0000000", "    tag=ATK value=3"),
        P("10:01:00.0000000", "    tag=HEALTH value=3"),
        P("10:01:00.0000000", "    tag=TECH_LEVEL value=1"),
        PL("10:01:10.0000000"),
        P("10:01:10.0000000", "BLOCK_START BlockType=PLAY Entity=[entityName=x id=7 zone=PLAY zonePos=0 cardId=TB_BaconShop_DragBuy player=2] EffectCardId=System.Collections.Generic.List`1[System.String] EffectIndex=0 Target=[entityName=y id=200 zone=PLAY zonePos=1 cardId=BG31_815 player=10] SubOption=-1 "),
        P("10:01:10.0000000", "    TAG_CHANGE Entity=Me tag=RESOURCES_USED value=3 "),
        P("10:01:10.0000000", "    TAG_CHANGE Entity=200 tag=CONTROLLER value=2 "),
        P("10:01:10.0000000", "    TAG_CHANGE Entity=200 tag=ZONE value=HAND "),
        P("10:01:10.0000000", "BLOCK_END"),
        PL("10:01:20.0000000"),
        P("10:01:20.0000000", "BLOCK_START BlockType=PLAY Entity=[entityName=x id=8 zone=PLAY zonePos=0 cardId=TB_BaconShop_8p_Reroll_Button player=2] EffectCardId=System.Collections.Generic.List`1[System.String] EffectIndex=0 Target=0 SubOption=-1 "),
        P("10:01:20.0000000", "BLOCK_END"),
        PL("10:01:30.0000000"),
        P("10:01:30.0000000", "TAG_CHANGE Entity=GameEntity tag=TURN value=2"),
        P("10:01:30.0000000", "FULL_ENTITY - Creating ID=300 CardID=BG22_HERO_201_SKIN_C"),
        P("10:01:30.0000000", "    tag=CARDTYPE value=HERO"),
        P("10:01:30.0000000", "    tag=CONTROLLER value=10"),
        P("10:01:30.0000000", "    tag=ZONE value=PLAY"),
        P("10:01:30.0000000", "    tag=HEALTH value=30"),
        P("10:01:30.0000000", "    tag=PLAYER_ID value=4"),
        P("10:01:30.0000000", "TAG_CHANGE Entity=GameEntity tag=3533 value=1 "),
        P("10:01:30.0000000", "TAG_CHANGE Entity=GameEntity tag=3533 value=0 "),
        P("10:01:30.0000000", "    TAG_CHANGE Entity=300 tag=PREDAMAGE value=3 "),
        P("10:01:30.0000000", "    TAG_CHANGE Entity=300 tag=PREDAMAGE value=0 "),
        P("10:01:30.0000000", "    TAG_CHANGE Entity=300 tag=PREDAMAGE value=3 "),
        P("10:01:30.0000000", "    TAG_CHANGE Entity=300 tag=PREDAMAGE value=0 "),
        PL("10:02:00.0000000"),
        P("10:02:00.0000000", "TAG_CHANGE Entity=GameEntity tag=TURN value=3"),
    };

    private static List<string> End() => new()
    {
        P("10:19:00.0000000", "TAG_CHANGE Entity=100 tag=PLAYER_LEADERBOARD_PLACE value=1 "),
        P("10:19:00.0000000", "TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE "),
    };

    private static GameRecorder Run(IEnumerable<string> lines)
    {
        var recorder = new GameRecorder(Meta, CardDb.Empty);
        var clock = new SessionClock(SessionClock.ParseSessionName(Meta.Session)!.Value);
        var parser = new PowerLogParser(recorder);
        foreach (var l in lines) parser.Feed(l, clock.ToDateTime(PowerLogLine.TimeOfDay(l)!.Value));
        parser.Finish();
        recorder.Finish();
        return recorder;
    }

    [Fact]
    public void RecordsAShortGame()
    {
        var r = Run(Setup().Concat(OneTurn()).Concat(End()));
        var log = r.Record!;
        Assert.Equal(LogStatus.Complete, log.Status);
        Assert.Equal("hsbg-kifu", log.Generator.Plugin);
        Assert.Null(log.Generator.HdtVersion);
        Assert.Equal(255790, log.Generator.HearthstoneBuild);
        Assert.Equal(GameRecorder.GameIdFor(Meta), log.Game.GameId);
        Assert.Equal(new DateTimeOffset(new DateTime(2026, 11, 20, 10, 0, 1)), log.Game.StartedAt);
        Assert.Equal(1199, log.Game.DurationSec); // 終わり = 中継ログの最後（画面側の COMPLETE）
        Assert.Equal("BG36_HERO_105", log.Player.Hero.CardId);
        Assert.Equal(30, log.Player.StartingHealth);
        Assert.Equal(6, log.Player.StartingArmor);
        Assert.Equal(1, log.Result.Placement);
        Assert.Equal(2, log.Result.TotalTurns);

        var t1 = log.Turns.Single(t => t.Turn == 1);
        Assert.Equal(1, t1.Actions!.Buys);
        Assert.Equal(1, t1.Actions.Rerolls);
        Assert.Equal(3, t1.Actions.GoldSpent);
        Assert.Equal(1, t1.TavernTier);
        Assert.Equal("BG31_815", Assert.Single(t1.HandEndOfShop!).CardId);
        Assert.Equal("turn_start", t1.ShopOffers[0].Trigger);
        Assert.Equal("BG22_HERO_201_SKIN_C", t1.Combat!.OpponentHero.CardId);
        Assert.Equal(4, t1.Combat.OpponentPlayerId);
        Assert.Equal("win", t1.Combat.Result);
        Assert.Equal(3, t1.Combat.DamageDealt); // PREDAMAGE の 2 回目は数えない
        Assert.Equal(0, t1.Combat.DamageTaken);
        Assert.Null(t1.Combat.BobsBuddy);

        var events = r.EventLines.Select(JObject.Parse).ToList();
        var buy = events.Single(e => (string?)e["type"] == "buy");
        Assert.Equal("BG31_815", (string?)buy["card_id"]);
        Assert.Equal(0, (int?)buy["gold_after"]);
        Assert.Contains(r.EventLines, l => l.Contains("\"type\":\"buy\"") && l.StartsWith("{\"t\":\"2026-11-20T10:01:10")); // 時刻は Power.log の行の時刻
        Assert.Single(events, e => (string?)e["type"] == "reroll");
        Assert.Equal(new[] { "game_start", "hero_selected", "turn_start" }, events.Take(3).Select(e => (string?)e["type"])); // プラグインと同じ順
        Assert.Equal("game_end", (string?)events[^1]["type"]);
    }

    [Fact]
    public void GameThatEndsBeforeTurnOneIsDiscarded()
    {
        // ヒーロー選択中に外れた: ヒーローも順位も無いまま終わる（プラグインの AbortedGame と同じ判定）
        var lines = Setup(withHero: false).Append(P("10:00:30.0000000", "TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE "));
        var r = Run(lines);
        Assert.True(r.Discarded);
        Assert.Null(r.Record);
    }

    [Fact]
    public void GameWithoutAnEndIsIncomplete()
    {
        var r = Run(Setup().Concat(OneTurn()));
        Assert.Equal(LogStatus.Incomplete, r.Record!.Status);
        Assert.Contains(r.Record.Notes, n => n.Contains("did not end"));
    }

    [Fact]
    public void GameIdIsStableAndLooksLikeAUuid()
    {
        var a = GameRecorder.GameIdFor(Meta);
        Assert.Equal(a, GameRecorder.GameIdFor(new SliceMeta { Session = Meta.Session, CreateGameTime = Meta.CreateGameTime }));
        Assert.NotEqual(a, GameRecorder.GameIdFor(new SliceMeta { Session = Meta.Session, CreateGameTime = "10:30:00.0000000" }));
        Assert.True(Guid.TryParse(a, out _));
        Assert.Equal('5', a[14]);
    }
}

public class KifuWriterServiceTests : IDisposable
{
    private readonly TempDir _tmp = new();
    public void Dispose() => _tmp.Dispose();

    private (SliceMeta Meta, string Gz) Slice()
    {
        var lines = new List<string>
        {
            "D 10:00:01.0000000 GameState.DebugPrintPower() - CREATE_GAME",
            "D 10:00:01.0000000 GameState.DebugPrintPower() -     GameEntity EntityID=4",
            "D 10:00:01.0000000 GameState.DebugPrintPower() -     Player EntityID=5 PlayerID=2 GameAccountId=[hi=1 lo=2]",
            "D 10:00:01.0000000 GameState.DebugPrintPower() -         tag=HERO_ENTITY value=100",
            "D 10:00:01.0000000 GameState.DebugPrintPower() -     Player EntityID=6 PlayerID=10 GameAccountId=[hi=0 lo=0]",
            "D 10:00:01.0000000 GameState.DebugPrintPower() -         tag=BACON_DUMMY_PLAYER value=1",
            "D 10:00:01.0000000 GameState.DebugPrintPower() - FULL_ENTITY - Creating ID=100 CardID=BG36_HERO_105",
            "D 10:00:01.0000000 GameState.DebugPrintPower() -     tag=CARDTYPE value=HERO",
            "D 10:00:01.0000000 GameState.DebugPrintPower() -     tag=CONTROLLER value=2",
            "D 10:00:01.0000000 GameState.DebugPrintPower() -     tag=ZONE value=PLAY",
            "D 10:01:00.0000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=GameEntity tag=TURN value=1",
            "D 10:02:00.0000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=100 tag=PLAYER_LEADERBOARD_PLACE value=3 ",
            "D 10:02:00.0000000 GameState.DebugPrintPower() - TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE ",
        };
        var gz = Path.Combine(_tmp.Sub("slices"), "s.power.log.gz");
        TestLogs.WriteGz(gz, TestLogs.Join(lines));
        var meta = new SliceMeta
        {
            Session = "Hearthstone_2026_11_20_10_00_00", CreateGameTime = "10:00:01.0000000", GameType = "GT_BATTLEGROUNDS",
            StartTime = new DateTimeOffset(new DateTime(2026, 11, 20, 10, 0, 1)), Status = SliceMeta.StatusComplete,
        };
        return (meta, gz);
    }

    [Fact]
    public void WritesTheRecordAndOverwritesItsOwnButNotAnotherRecordOfTheSameGame()
    {
        var (meta, gz) = Slice();
        var outRoot = _tmp.Sub("out");
        var path = new KifuWriterService(outRoot, cardDefsPath: Path.Combine(_tmp.Path, "none.xml")).Process(meta, gz);
        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.True(File.Exists(path!.Replace(".json", ".events.jsonl")));
        Assert.True(File.Exists(path.Replace(".json", ".power.log.gz")));
        Assert.Equal(TestLogs.ReadGz(gz).Count, TestLogs.ReadGz(path.Replace(".json", ".power.log.gz")).Count); // GameState の行だけ（この中継ログは全部）

        // 自分の記録は上書きする（作り直し）
        Assert.Equal(path, new KifuWriterService(outRoot, cardDefsPath: Path.Combine(_tmp.Path, "none.xml")).Process(meta, gz));

        // 同じゲームの別の記録（プラグインの記録。開始時刻が 1 秒前、game_id が違う）があれば書かない
        var other = _tmp.Sub("out2", "2026", "11");
        File.WriteAllText(Path.Combine(other, "20261120-100000_11111111-2222-3333-4444-555555555555.json"), "{}");
        Assert.Null(new KifuWriterService(Path.Combine(_tmp.Path, "out2"), cardDefsPath: Path.Combine(_tmp.Path, "none.xml")).Process(meta, gz));
    }

    [Fact]
    public void CatchUpWritesRecordsOnlyForSlicesWithoutOne()
    {
        // 書き終えた中継ログを GameSlicer で作る
        var root = _tmp.Sub("out3", ".slices");
        var session = new LogSession("Hearthstone_2026_11_20_10_00_00", "unused", new DateTime(2026, 11, 20, 10, 0, 0));
        var (_, gz) = Slice();
        var lines = TestLogs.ReadGz(gz);
        lines.Insert(1, "D 10:00:01.0000000 GameState.DebugPrintGame() - GameType=GT_BATTLEGROUNDS");
        lines.Add("D 10:02:10.0000000 PowerTaskList.DebugPrintPower() -     TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE ");
        using (var slicer = new GameSlicer(session, "Power.log", new SliceStore(root), () => Array.Empty<GameServerEntry>()))
            foreach (var l in TestLogs.ToLogLines(lines)) slicer.Feed(l);

        var outRoot = Path.Combine(_tmp.Path, "out3");
        var none = Path.Combine(_tmp.Path, "none.xml");
        Assert.Equal(1, new KifuWriterService(outRoot, cardDefsPath: none).CatchUp(new SliceStore(root)));
        Assert.Equal(0, new KifuWriterService(outRoot, cardDefsPath: none).CatchUp(new SliceStore(root))); // もう記録がある
        Assert.Single(Directory.GetFiles(Path.Combine(outRoot, "2026"), "*.json", SearchOption.AllDirectories));
    }
}
