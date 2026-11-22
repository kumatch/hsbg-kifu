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
        Assert.False(t1.Combat.IceBlock);
        Assert.False(t1.Combat.OpponentIceBlock);

        var events = r.EventLines.Select(JObject.Parse).ToList();
        var buy = events.Single(e => (string?)e["type"] == "buy");
        Assert.Equal("BG31_815", (string?)buy["card_id"]);
        Assert.Equal(0, (int?)buy["gold_after"]);
        Assert.Contains(r.EventLines, l => l.Contains("\"type\":\"buy\"") && l.StartsWith("{\"t\":\"2026-11-20T10:01:10")); // 時刻は Power.log の行の時刻
        Assert.Single(events, e => (string?)e["type"] == "reroll");
        Assert.Equal(new[] { "game_start", "hero_selected", "turn_start" }, events.Take(3).Select(e => (string?)e["type"])); // プラグインと同じ順
        Assert.Equal("game_end", (string?)events[^1]["type"]);
    }

    /// <summary>
    /// ターン 1 の戦闘で、自分のヒーロー（HP 30 + アーマー 6）に 40 の PREDAMAGE。自分のアイスブロック（CONTROLLER 2）と、
    /// 戦闘のために複製された相手のアイスブロック（CONTROLLER 10 = 相手役）がある。発動したほうは墓地へ、しなかったほうは墓地を経ずに消える
    /// （docs/research.md §3.1「ダメージ」）。
    /// </summary>
    private static List<string> IceBlockTurn(bool mineTriggers, bool opponentTriggers)
    {
        var lines = OneTurn();
        var combatStart = lines.FindIndex(l => l.Contains("tag=3533 value=0"));
        var target = mineTriggers ? "100" : "300";
        lines.RemoveRange(combatStart + 1, 4); // 相手のヒーローへの 3 を差し替える
        lines.InsertRange(combatStart + 1, new[]
        {
            P("10:01:30.0000000", "    FULL_ENTITY - Creating ID=400 CardID=TB_Bacon_Secrets_12"),
            P("10:01:30.0000000", "        tag=CONTROLLER value=2"),
            P("10:01:30.0000000", "        tag=ZONE value=SECRET"),
            P("10:01:30.0000000", "    FULL_ENTITY - Creating ID=401 CardID=TB_Bacon_Secrets_12"),
            P("10:01:30.0000000", "        tag=CONTROLLER value=2"),
            P("10:01:30.0000000", "        tag=ZONE value=SETASIDE"),
            P("10:01:30.0000000", "    TAG_CHANGE Entity=401 tag=CONTROLLER value=10 "),
            P("10:01:30.0000000", "    TAG_CHANGE Entity=401 tag=ZONE value=SECRET "),
            P("10:01:31.0000000", $"    TAG_CHANGE Entity={target} tag=PREDAMAGE value=40 "),
            P("10:01:31.0000000", $"    TAG_CHANGE Entity={target} tag=PREDAMAGE value=0 "),
            P("10:01:31.0000000", $"    TAG_CHANGE Entity=400 tag=ZONE value={(mineTriggers ? "GRAVEYARD" : "SECRET")} "),
            P("10:01:31.0000000", $"    TAG_CHANGE Entity=401 tag=ZONE value={(opponentTriggers ? "GRAVEYARD" : "REMOVEDFROMGAME")} "),
        });
        return lines;
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void RecordsWhichIceBlockTriggered(bool mine, bool opponent)
    {
        var r = Run(Setup().Concat(IceBlockTurn(mine, opponent)).Concat(End()));
        var combat = r.Record!.Turns.Single(t => t.Turn == 1).Combat!;
        Assert.Equal(mine, combat.IceBlock);
        Assert.Equal(opponent, combat.OpponentIceBlock);
        // 戦闘のダメージは防がれても数える（docs/research.md §3.1）
        Assert.Equal(mine ? 40 : 0, combat.DamageTaken);
        Assert.Equal(opponent ? 40 : 0, combat.DamageDealt);
        var end = r.EventLines.Select(JObject.Parse).Single(e => (string?)e["type"] == "combat_end");
        Assert.Equal(mine, (bool?)end["ice_block"]);
        Assert.Equal(opponent, (bool?)end["opponent_ice_block"]);
    }

    /// <summary>
    /// ターン 1 のショップで、手札のトリプル報酬（Entity 500）を使う。3 択（501〜503、CREATOR = 500）はまとまりの途中に出て、
    /// 次のまとまり（選んだ 503 が手札に入る）より前に選び終わる。間に区切り（ブロックの終わり・次のまとまり）が無い。
    /// 2026-11-22 00:46 のゲームの T13 と同じ並び（Power.log の DebugPrintEntityChoices → DebugPrintEntitiesChosen → DebugPrintPowerList）。
    /// </summary>
    private static List<string> TripleRewardDiscoverTurn()
    {
        var lines = OneTurn();
        var turn2 = lines.FindIndex(l => l.Contains("tag=TURN value=2"));
        var at = turn2 - 1; // ターン 2 の PL の前（ショップの最後）
        lines.InsertRange(at, new[]
        {
            PL("10:01:25.0000000"),
            P("10:01:25.0000000", "FULL_ENTITY - Creating ID=500 CardID=TB_BaconShop_Triples_01"),
            P("10:01:25.0000000", "    tag=CARDTYPE value=SPELL"),
            P("10:01:25.0000000", "    tag=CONTROLLER value=2"),
            P("10:01:25.0000000", "    tag=ZONE value=HAND"),
            PL("10:01:26.0000000"),
            P("10:01:26.0000000", "BLOCK_START BlockType=PLAY Entity=[entityName=x id=500 zone=HAND zonePos=2 cardId=TB_BaconShop_Triples_01 player=2] EffectCardId=System.Collections.Generic.List`1[System.String] EffectIndex=0 Target=0 SubOption=-1 "),
            P("10:01:26.0000000", "    TAG_CHANGE Entity=500 tag=ZONE value=PLAY "),
            P("10:01:26.0000000", "    BLOCK_START BlockType=POWER Entity=[entityName=x id=500 zone=PLAY zonePos=0 cardId=TB_BaconShop_Triples_01 player=2] EffectCardId=System.Collections.Generic.List`1[System.String] EffectIndex=0 Target=0 SubOption=-1 "),
            P("10:01:26.0000000", "        FULL_ENTITY - Creating ID=501 CardID=BG22_403"),
            P("10:01:26.0000000", "            tag=CARDTYPE value=MINION"),
            P("10:01:26.0000000", "            tag=CONTROLLER value=2"),
            P("10:01:26.0000000", "            tag=ZONE value=SETASIDE"),
            P("10:01:26.0000000", "            tag=CREATOR value=500"),
            P("10:01:26.0000000", "        FULL_ENTITY - Creating ID=502 CardID=BG26_354"),
            P("10:01:26.0000000", "            tag=CARDTYPE value=MINION"),
            P("10:01:26.0000000", "            tag=CONTROLLER value=2"),
            P("10:01:26.0000000", "            tag=ZONE value=SETASIDE"),
            P("10:01:26.0000000", "            tag=CREATOR value=500"),
            P("10:01:26.0000000", "        FULL_ENTITY - Creating ID=503 CardID=BG36_515"),
            P("10:01:26.0000000", "            tag=CARDTYPE value=MINION"),
            P("10:01:26.0000000", "            tag=CONTROLLER value=2"),
            P("10:01:26.0000000", "            tag=ZONE value=SETASIDE"),
            P("10:01:26.0000000", "            tag=CREATOR value=500"),
            "D 10:01:26.1000000 GameState.DebugPrintEntityChoices() - id=20 Player=Me TaskList=10045 ChoiceType=GENERAL CountMin=1 CountMax=1",
            "D 10:01:26.1000000 GameState.DebugPrintEntityChoices() -   Source=[entityName=x id=500 zone=PLAY zonePos=0 cardId=TB_BaconShop_Triples_01 player=2]",
            "D 10:01:26.1000000 GameState.DebugPrintEntityChoices() -   Entities[0]=[entityName=x id=501 zone=SETASIDE zonePos=0 cardId=BG22_403 player=2]",
            "D 10:01:26.1000000 GameState.DebugPrintEntityChoices() -   Entities[1]=[entityName=x id=502 zone=SETASIDE zonePos=0 cardId=BG26_354 player=2]",
            "D 10:01:26.1000000 GameState.DebugPrintEntityChoices() -   Entities[2]=[entityName=x id=503 zone=SETASIDE zonePos=0 cardId=BG36_515 player=2]",
            "D 10:01:27.2000000 GameState.SendChoices() - id=20 ChoiceType=GENERAL",
            "D 10:01:27.5000000 GameState.DebugPrintEntitiesChosen() - id=20 Player=Me EntitiesCount=1",
            "D 10:01:27.5000000 GameState.DebugPrintEntitiesChosen() -   Entities[0]=[entityName=x id=503 zone=SETASIDE zonePos=0 cardId=BG36_515 player=2]",
            PL("10:01:27.5100000"),
            P("10:01:27.5100000", "TAG_CHANGE Entity=503 tag=ZONE value=HAND "),
            P("10:01:27.5100000", "TAG_CHANGE Entity=500 tag=ZONE value=GRAVEYARD "),
            P("10:01:27.5100000", "    BLOCK_END"),
            P("10:01:27.5100000", "BLOCK_END"),
        });
        return lines;
    }

    [Fact]
    public void RecordsATripleRewardDiscoverWhoseChoiceEndsBeforeTheNextBatch()
    {
        var r = Run(Setup().Concat(TripleRewardDiscoverTurn()).Concat(End()));
        var discover = r.EventLines.Select(JObject.Parse).Single(e => (string?)e["type"] == "discover");
        Assert.Equal(1, (int?)discover["turn"]);
        Assert.Equal("triple", (string?)discover["source"]);
        Assert.Equal("BG36_515", (string?)discover["card_id"]);
        Assert.Equal(new[] { "BG22_403", "BG26_354", "BG36_515" }, discover["offered"]!.Select(x => (string?)x));
    }

    [Fact]
    public async Task RecordWithoutMmrAndLobbyTypesIsValidAgainstTheSchema()
    {
        // HDT が無い（BgsLastGames.xml・CardDefs が無い）、ショップに 5 種族並ばなかった、といった取れない値があっても、記録の形として正しい
        var log = Run(Setup().Concat(OneTurn()).Concat(End())).Record!;
        Assert.Null(log.Result.MmrBefore);
        Assert.Null(log.Result.MmrAfter);
        Assert.Null(log.Result.MmrDelta);
        Assert.Empty(log.Game.MinionTypes);
        Assert.Null(log.Player.Hero.Name); // カードの情報が無い
        var schema = await NJsonSchema.JsonSchema.FromJsonAsync(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "game-log.schema.json")));
        var errors = schema.Validate(HsbgKifu.Output.GameLogWriter.Serialize(log)).Select(e => $"{e.Path}: {e.Kind}").ToArray();
        Assert.True(errors.Length == 0, string.Join("\n", errors));
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
        var path = new KifuWriterService(outRoot, cardDefsPath: Path.Combine(_tmp.Path, "none.xml"), ratings: new BgsLastGames(Path.Combine(_tmp.Path, "none.xml"))).Process(meta, gz);
        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.True(File.Exists(path!.Replace(".json", ".events.jsonl")));
        Assert.True(File.Exists(path.Replace(".json", ".power.log.gz")));
        Assert.Equal(TestLogs.ReadGz(gz).Count, TestLogs.ReadGz(path.Replace(".json", ".power.log.gz")).Count); // GameState の行だけ（この中継ログは全部）

        // 自分の記録は上書きする（作り直し）
        Assert.Equal(path, new KifuWriterService(outRoot, cardDefsPath: Path.Combine(_tmp.Path, "none.xml"), ratings: new BgsLastGames(Path.Combine(_tmp.Path, "none.xml"))).Process(meta, gz));

        // 同じゲームの別の記録（プラグインの記録。開始時刻が 1 秒前、game_id が違う）があれば書かない
        var other = _tmp.Sub("out2", "2026", "11");
        File.WriteAllText(Path.Combine(other, "20261120-100000_11111111-2222-3333-4444-555555555555.json"), "{}");
        Assert.Null(new KifuWriterService(Path.Combine(_tmp.Path, "out2"), cardDefsPath: Path.Combine(_tmp.Path, "none.xml"), ratings: new BgsLastGames(Path.Combine(_tmp.Path, "none.xml"))).Process(meta, gz));
    }

    private BgsLastGames Ratings(string attrs)
    {
        var path = Path.Combine(_tmp.Path, Guid.NewGuid().ToString("N") + ".xml");
        File.WriteAllText(path, $"<BgsLastGames>\n  <Game Player=\"1_2\" StartTime=\"2026-11-20T10:00:00.3000000+09:00\" {attrs} />\n</BgsLastGames>\n");
        return new BgsLastGames(path);
    }

    [Fact]
    public void FillsMmrFromBgsLastGames()
    {
        var (meta, gz) = Slice();
        var none = Path.Combine(_tmp.Path, "none.xml");
        var path = new KifuWriterService(_tmp.Sub("m1"), cardDefsPath: none, ratings: Ratings("Rating=\"8000\" RatingAfter=\"8050\"")).Process(meta, gz)!;
        var json = JObject.Parse(File.ReadAllText(path));
        Assert.Equal(8000, (int?)json["result"]!["mmr_before"]);
        Assert.Equal(8050, (int?)json["result"]!["mmr_after"]);
        Assert.Equal(50, (int?)json["result"]!["mmr_delta"]);
        Assert.Contains(File.ReadAllLines(path.Replace(".json", ".events.jsonl")), l => l.Contains("\"type\":\"mmr_after\"") && l.Contains("\"mmr_delta\":50"));
    }

    [Fact]
    public void WaitsForTheMmrAfterAndNotesItWhenItNeverComes()
    {
        var (meta, gz) = Slice();
        var none = Path.Combine(_tmp.Path, "none.xml");
        string path;
        // 終わったばかりのゲーム（今 = 終わりの 30 秒後）として、後の MMR を待つ
        var justEnded = new DateTimeOffset(new DateTime(2026, 11, 20, 10, 2, 40));
        using (var w = new KifuWriterService(_tmp.Sub("m2"), cardDefsPath: none, mmrWait: TimeSpan.FromMinutes(5), ratings: Ratings("Rating=\"8000\""), now: () => justEnded))
        {
            path = w.Process(meta, gz)!;
            var waiting = JObject.Parse(File.ReadAllText(path));
            Assert.Equal(8000, (int?)waiting["result"]!["mmr_before"]);
            Assert.DoesNotContain(waiting["notes"]!.Values<string>(), n => n == "mmr_after unavailable"); // まだ待っている
        } // 止めるときに今の値で書き直す
        var final = JObject.Parse(File.ReadAllText(path));
        Assert.Null((int?)final["result"]!["mmr_after"]);
        Assert.Contains(final["notes"]!.Values<string>(), n => n == "mmr_after unavailable");
    }

    [Fact]
    public void DoesNotWaitForTheMmrOfAnOldGame()
    {
        // 起動時の回収で作る古いゲーム: BgsLastGames.xml に無ければもう入らないので、待たずに確定する
        var (meta, gz) = Slice();
        var none = Path.Combine(_tmp.Path, "none.xml");
        using var w = new KifuWriterService(_tmp.Sub("m3"), cardDefsPath: none, mmrWait: TimeSpan.FromMinutes(5), ratings: new BgsLastGames(none));
        var json = JObject.Parse(File.ReadAllText(w.Process(meta, gz)!));
        Assert.Contains(json["notes"]!.Values<string>(), n => n == "mmr_before unavailable");
        Assert.Contains(json["notes"]!.Values<string>(), n => n == "mmr_after unavailable");
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
        Assert.Equal(1, new KifuWriterService(outRoot, cardDefsPath: none, ratings: new BgsLastGames(none)).CatchUp(new SliceStore(root)));
        Assert.Equal(0, new KifuWriterService(outRoot, cardDefsPath: none, ratings: new BgsLastGames(none)).CatchUp(new SliceStore(root))); // もう記録がある
        Assert.Single(Directory.GetFiles(Path.Combine(outRoot, "2026"), "*.json", SearchOption.AllDirectories));
    }
}

public class ReconnectRecorderTests : IDisposable
{
    private readonly TempDir _tmp = new();
    public void Dispose() => _tmp.Dispose();

    private static string P(string time, string body) => $"D {time} GameState.DebugPrintPower() - {body}";
    private static string G(string time, string body) => $"D {time} GameState.DebugPrintGame() - {body}";

    private static List<string> Players(string t, int turn, int heroDamage) => new()
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
        P(t, "    tag=ARMOR value=6"),
        P(t, "TAG_CHANGE Entity=4 tag=STATE value=RUNNING "),
        G(t, "GameType=GT_BATTLEGROUNDS"),
        G(t, "PlayerID=2, PlayerName=Me"),
    };

    private (string Gz, SliceMeta Meta) Slice(string name, string session, string createTime, bool reconnecting, List<string> lines)
    {
        var gz = Path.Combine(_tmp.Path, name + ".power.log.gz");
        TestLogs.WriteGz(gz, TestLogs.Join(lines));
        var start = SessionClock.ParseSessionName(session)!.Value.Date + PowerLogLine.TimeOfDay("D " + createTime + " x")!.Value;
        return (gz, new SliceMeta
        {
            Session = session, CreateGameTime = createTime, GameType = "GT_BATTLEGROUNDS", GameHandle = 1936, Reconnecting = reconnecting,
            StartTime = new DateTimeOffset(start), Status = reconnecting ? SliceMeta.StatusComplete : SliceMeta.StatusIncomplete,
        });
    }

    [Fact]
    public void JoinsTheSlicesAndInfersTheCombatThatWasNotSeen()
    {
        // 1 つ目: ターン 1 のショップと戦闘の始まりまで（ここで Hearthstone が落ちた）
        var first = Players("10:00:01.0000000", 0, 0);
        first.AddRange(new[]
        {
            P("10:01:00.0000000", "TAG_CHANGE Entity=GameEntity tag=TURN value=1"),
            P("10:01:30.0000000", "TAG_CHANGE Entity=GameEntity tag=TURN value=2"),
            P("10:01:31.0000000", "TAG_CHANGE Entity=GameEntity tag=3533 value=1 "),
            P("10:01:31.0000000", "TAG_CHANGE Entity=GameEntity tag=3533 value=0 "),
        });
        // 2 つ目: 再起動後、ターン 2 のショップ（TURN 3）の状態から。戦闘で 5 受けていた
        var second = Players("10:03:00.0000000", 3, 5);
        second.Add(P("10:05:00.0000000", "TAG_CHANGE Entity=100 tag=PLAYER_LEADERBOARD_PLACE value=2 "));
        second.Add(P("10:05:00.0000000", "TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE "));

        var a = Slice("a", "Hearthstone_2026_10_17_10_00_00", "10:00:01.0000000", false, first);
        var b = Slice("b", "Hearthstone_2026_10_17_10_02_30", "10:03:00.0000000", true, second);
        var result = KifuBuilder.Build(new[] { a, b }, CardDb.Empty);
        var log = result.Log!;

        Assert.Equal(GameRecorder.GameIdFor(a.Meta), log.Game.GameId); // 最初の中継ログの ID
        Assert.Equal(LogStatus.Complete, log.Status);
        Assert.Equal(2, log.Result.Placement);
        Assert.Equal(new[] { 1, 2 }, log.Turns.Select(t => t.Turn));
        var t1 = log.Turns[0].Combat!;
        Assert.Equal("loss", t1.Result);     // 36 → 31 から推定
        Assert.Equal(5, t1.DamageTaken);
        Assert.Null(t1.IceBlock);              // 戦闘を見ていないので分からない
        Assert.Null(t1.OpponentIceBlock);
        Assert.Equal(25, log.Turns[1].Health); // ターン 2 のショップは再接続した時点から始めた
        Assert.Contains(log.Notes, n => n.StartsWith("reconnect: Hearthstone restarted"));
        Assert.Contains(log.Notes, n => n.Contains("turn 1 combat ended while disconnected; result loss inferred"));
        Assert.Contains(result.Events, e => e.Contains("\"type\":\"reconnect\""));
        Assert.Contains(result.Events, e => e.Contains("\"type\":\"combat_end\"") && e.Contains("\"inferred\":true"));
    }
}
