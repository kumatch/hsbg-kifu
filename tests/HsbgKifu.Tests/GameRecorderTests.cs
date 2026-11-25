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
        Assert.Equal("TB_BaconShop_Triples_01", (string?)discover["source_card_id"]);
        Assert.Contains(r.EventLines, l => l.Contains("\"type\":\"discover\"") && l.StartsWith("{\"t\":\"2026-11-20T10:01:27.5")); // 選び終えた行の時刻

        var choice = Assert.Single(r.Record!.Turns.Single(t => t.Turn == 1).Choices);
        Assert.Equal("triple", choice.Source);
        Assert.Equal("TB_BaconShop_Triples_01", choice.SourceCard.CardId);
        Assert.Equal(new[] { "BG22_403", "BG26_354", "BG36_515" }, choice.Offered.Select(c => c.CardId));
        Assert.Equal("minion", choice.Offered[0].CardType);
        Assert.Equal(2, choice.ChosenIndex);
    }

    /// <summary>
    /// ターン 1 のショップで、Entity 600（<paramref name="sourceCardId"/>、種類 <paramref name="sourceType"/>）が 3 択（601〜603、種類 <paramref name="offeredType"/>）を出し、602 を選ぶ。
    /// <paramref name="leavesHandAtOnce"/> なら、選んだカードは手札に入ったまとまりの中ですぐ手札から消える（すぐトリプルになった）。
    /// </summary>
    private static List<string> ChoiceTurn(string sourceCardId, string sourceType, string offeredType, bool leavesHandAtOnce = false, bool createdByHeroPower = false)
    {
        var lines = OneTurn();
        var at = lines.FindIndex(l => l.Contains("tag=TURN value=2")) - 1;
        var add = new List<string>
        {
            PL("10:01:25.0000000"),
            P("10:01:25.0000000", $"FULL_ENTITY - Creating ID=600 CardID={sourceCardId}"),
            P("10:01:25.0000000", $"    tag=CARDTYPE value={sourceType}"),
            P("10:01:25.0000000", "    tag=CONTROLLER value=2"),
            P("10:01:25.0000000", "    tag=ZONE value=PLAY"),
            P("10:01:25.0000000", $"    tag=CREATOR value={(createdByHeroPower ? 650 : 0)}"),
            P("10:01:25.0000000", "FULL_ENTITY - Creating ID=650 CardID=BG36_HERO_105p"),
            P("10:01:25.0000000", "    tag=CARDTYPE value=HERO_POWER"),
            P("10:01:25.0000000", "    tag=CONTROLLER value=2"),
            P("10:01:25.0000000", "    tag=ZONE value=PLAY"),
            P("10:01:26.0000000", "BLOCK_START BlockType=POWER Entity=[entityName=x id=600 zone=PLAY zonePos=1 cardId=" + sourceCardId + " player=2] EffectCardId=System.Collections.Generic.List`1[System.String] EffectIndex=0 Target=0 SubOption=-1 "),
        };
        foreach (var (id, card) in new[] { (601, "BG_A"), (602, "BG_B"), (603, "BG_C") })
        {
            add.Add(P("10:01:26.0000000", $"    FULL_ENTITY - Creating ID={id} CardID={card}"));
            add.Add(P("10:01:26.0000000", $"        tag=CARDTYPE value={offeredType}"));
            add.Add(P("10:01:26.0000000", "        tag=CONTROLLER value=2"));
            add.Add(P("10:01:26.0000000", "        tag=ZONE value=SETASIDE"));
            add.Add(P("10:01:26.0000000", "        tag=CREATOR value=600"));
            add.Add(P("10:01:26.0000000", $"        tag=ATK value={id - 600}"));
            add.Add(P("10:01:26.0000000", $"        tag=HEALTH value={id - 590}"));
        }
        add.AddRange(new[]
        {
            "D 10:01:26.1000000 GameState.DebugPrintEntityChoices() - id=21 Player=Me TaskList=10046 ChoiceType=GENERAL CountMin=1 CountMax=1",
            $"D 10:01:26.1000000 GameState.DebugPrintEntityChoices() -   Source=[entityName=x id=600 zone=PLAY zonePos=1 cardId={sourceCardId} player=2]",
            "D 10:01:26.1000000 GameState.DebugPrintEntityChoices() -   Entities[0]=[entityName=x id=601 zone=SETASIDE zonePos=0 cardId=BG_A player=2]",
            "D 10:01:26.1000000 GameState.DebugPrintEntityChoices() -   Entities[1]=[entityName=x id=602 zone=SETASIDE zonePos=0 cardId=BG_B player=2]",
            "D 10:01:26.1000000 GameState.DebugPrintEntityChoices() -   Entities[2]=[entityName=x id=603 zone=SETASIDE zonePos=0 cardId=BG_C player=2]",
            "D 10:01:27.5000000 GameState.DebugPrintEntitiesChosen() - id=21 Player=Me EntitiesCount=1",
            "D 10:01:27.5000000 GameState.DebugPrintEntitiesChosen() -   Entities[0]=[entityName=x id=602 zone=SETASIDE zonePos=0 cardId=BG_B player=2]",
            PL("10:01:27.5100000"),
            P("10:01:27.5100000", "TAG_CHANGE Entity=602 tag=ZONE value=HAND "),
        });
        if (leavesHandAtOnce) add.Add(P("10:01:27.5100000", "TAG_CHANGE Entity=602 tag=ZONE value=SETASIDE "));
        add.Add(P("10:01:27.5100000", "BLOCK_END"));
        lines.InsertRange(at, add);
        return lines;
    }

    [Theory]
    [InlineData("BG31_890", "MINION", "MINION", "card")]                 // ミニオンの雄叫びの発見
    [InlineData("BG30_Trinket_1st", "SPELL", "BATTLEGROUND_TRINKET", "trinket")] // トリンケットの選択（ターン数では決めない）
    [InlineData("BG30_MagicItem_703t", "BATTLEGROUND_TRINKET", "BATTLEGROUND_TRINKET", "trinket")]
    [InlineData("BG36_MidGameEffect_010", "SPELL", "MINION", "dark_gift")]
    [InlineData("BG36_MidGameEffect_010", "SPELL", "MINION", "hero_power", true)] // ヒーローパワーが作った悪夢の発見の発見スペル（BG36_HERO_105p）
    [InlineData("BG36_HERO_105p", "HERO_POWER", "MINION", "hero_power")]
    public void RecordsEveryChoiceWithItsSource(string sourceCardId, string sourceType, string offeredType, string expected, bool createdByHeroPower = false)
    {
        var r = Run(Setup().Concat(ChoiceTurn(sourceCardId, sourceType, offeredType, createdByHeroPower: createdByHeroPower)).Concat(End()));
        var choice = Assert.Single(r.Record!.Turns.Single(t => t.Turn == 1).Choices);
        Assert.Equal(expected, choice.Source);
        Assert.Equal(sourceCardId, choice.SourceCard.CardId);
        Assert.Equal(new[] { "BG_A", "BG_B", "BG_C" }, choice.Offered.Select(c => c.CardId));
        Assert.Equal(1, choice.ChosenIndex);
        var discovers = r.EventLines.Select(JObject.Parse).Where(e => (string?)e["type"] == "discover").ToList();
        if (expected == "dark_gift")
        {
            Assert.Empty(discovers); // 悪夢の発見は dark_gift の行（手札に入ったカードから）
            return;
        }
        var d = Assert.Single(discovers);
        Assert.Equal(expected, (string?)d["source"]);
        Assert.Equal("BG_B", (string?)d["card_id"]);
        Assert.Equal(sourceCardId, (string?)d["source_card_id"]);
        if (offeredType == "MINION") Assert.Equal(2, choice.Offered[1].Attack); // 候補が出た時点の値
    }

    [Fact]
    public void RecordsAChoiceWhosePickLeavesTheHandInTheSameBatch()
    {
        // 選んだカードが手札に入ったまとまりの中で、すぐトリプルになって手札から消えた（手札の差では見えない。11/14 00:47 の T8）
        var r = Run(Setup().Concat(ChoiceTurn("TB_BaconShop_Triples_01", "SPELL", "MINION", leavesHandAtOnce: true)).Concat(End()));
        var d = Assert.Single(r.EventLines.Select(JObject.Parse), e => (string?)e["type"] == "discover");
        Assert.Equal("triple", (string?)d["source"]);
        Assert.Equal("BG_B", (string?)d["card_id"]);
    }

    [Fact]
    public void AChoiceThatWasNeverMadeHasNoChosenIndex()
    {
        var lines = ChoiceTurn("BG31_890", "MINION", "MINION");
        lines.RemoveAll(l => l.Contains("DebugPrintEntitiesChosen"));
        var r = Run(Setup().Concat(lines).Concat(End()));
        Assert.Null(Assert.Single(r.Record!.Turns.Single(t => t.Turn == 1).Choices).ChosenIndex);
        Assert.DoesNotContain(r.EventLines, l => l.Contains("\"type\":\"discover\""));
    }

    /// <summary>
    /// 自分のヒーロー（Entity 100）にヒーローパワー Entity 120（<paramref name="cardId"/>、PLAY）を持たせる。<paramref name="withHeroPowerEntity"/> なら
    /// ヒーローの HERO_POWER_ENTITY も付ける（2026-09 のログには無い）。
    /// </summary>
    private static List<string> SetupWithHeroPower(string cardId = "BG35_HERO_001p", bool withHeroPowerEntity = true)
    {
        var lines = Setup();
        var at = lines.FindIndex(l => l.Contains("tag=STATE value=RUNNING"));
        var add = new List<string>
        {
            P("10:00:01.0000000", $"FULL_ENTITY - Creating ID=120 CardID={cardId}"),
            P("10:00:01.0000000", "    tag=CARDTYPE value=HERO_POWER"),
            P("10:00:01.0000000", "    tag=CONTROLLER value=2"),
            P("10:00:01.0000000", "    tag=ZONE value=PLAY"),
        };
        if (withHeroPowerEntity) add.Add(P("10:00:01.0000000", "TAG_CHANGE Entity=100 tag=HERO_POWER_ENTITY value=120 "));
        lines.InsertRange(at, add);
        return lines;
    }

    /// <summary>
    /// ターン 1 のショップで、ヒーローパワーが増える（Entity 701、ADDITIONAL_HERO_POWER_INDEX=1、ヒーローの ADDITIONAL_HERO_POWER_ENTITY_1）。
    /// <paramref name="replaceMain"/> なら次のまとまりでメインも入れ替わる（Entity 702。元のメイン 120 は SETASIDE、ヒーローの HERO_POWER_ENTITY が 702）。
    /// その後、使う型のヒーローパワー（<paramref name="usedId"/>）を使う。
    /// ウォーゲンの王ゲン（2026-10-11 00:17 のゲーム）と時渡りの「テロンの力」（2026-11-02 14:26）と同じ並び（docs/research.md §3.6）。
    /// </summary>
    private static List<string> SecondHeroPowerTurn(bool replaceMain, int usedId)
    {
        var lines = OneTurn();
        var at = lines.FindIndex(l => l.Contains("tag=TURN value=2")) - 1;
        var add = new List<string>
        {
            PL("10:01:25.0000000"),
            P("10:01:25.0000000", "FULL_ENTITY - Creating ID=701 CardID=BG20_HERO_280p5"),
            P("10:01:25.0000000", "    tag=CARDTYPE value=HERO_POWER"),
            P("10:01:25.0000000", "    tag=CONTROLLER value=2"),
            P("10:01:25.0000000", "    tag=ZONE value=PLAY"),
            P("10:01:25.0000000", "    tag=ADDITIONAL_HERO_POWER_INDEX value=1"),
            P("10:01:25.0000000", "TAG_CHANGE Entity=100 tag=ADDITIONAL_HERO_POWER_ENTITY_1 value=701 "),
        };
        if (replaceMain)
        {
            add.AddRange(new[]
            {
                PL("10:01:26.0000000"),
                P("10:01:26.0000000", "FULL_ENTITY - Creating ID=702 CardID=TB_BaconShop_HP_020"),
                P("10:01:26.0000000", "    tag=CARDTYPE value=HERO_POWER"),
                P("10:01:26.0000000", "    tag=CONTROLLER value=2"),
                P("10:01:26.0000000", "    tag=ZONE value=PLAY"),
                P("10:01:26.0000000", "TAG_CHANGE Entity=120 tag=ZONE value=SETASIDE "),
                P("10:01:26.0000000", "TAG_CHANGE Entity=100 tag=HERO_POWER_ENTITY value=702 "),
            });
        }
        add.AddRange(new[]
        {
            PL("10:01:27.0000000"),
            P("10:01:27.0000000", $"BLOCK_START BlockType=PLAY Entity=[entityName=x id={usedId} zone=PLAY zonePos=0 cardId=x player=2] EffectCardId=System.Collections.Generic.List`1[System.String] EffectIndex=0 Target=0 SubOption=-1 "),
            P("10:01:27.0000000", $"    TAG_CHANGE Entity={usedId} tag=EXHAUSTED value=1 "),
            P("10:01:27.0000000", "BLOCK_END"),
        });
        lines.InsertRange(at, add);
        return lines;
    }

    [Theory]
    [InlineData(true)]  // ウォーゲンの王ゲン: 1 回目が追加の枠、2 回目がメインと入れ替わる
    [InlineData(false)] // 時渡りの「○○の力」: 追加の枠だけ
    public async Task RecordsTheSecondHeroPower(bool replaceMain)
    {
        var used = replaceMain ? 702 : 120;
        var r = Run(SetupWithHeroPower().Concat(SecondHeroPowerTurn(replaceMain, used)).Concat(End()));
        var log = r.Record!;
        Assert.Equal("BG35_HERO_001p", log.Player.HeroPower.CardId); // 最初のヒーローパワーのまま

        var held = log.Turns.Single(t => t.Turn == 1).HeroPowers;
        var main = replaceMain ? "TB_BaconShop_HP_020" : "BG35_HERO_001p";
        Assert.Equal(new[] { ("main", main), ("additional", "BG20_HERO_280p5") }, held.Select(h => (h.Slot, h.CardId!)));

        var events = r.EventLines.Select(JObject.Parse).ToList();
        var changes = events.Where(e => (string?)e["type"] == "hero_power_changed").ToList();
        Assert.Equal(replaceMain ? 2 : 1, changes.Count);
        Assert.Equal("additional", (string?)changes[0]["slot"]);
        Assert.Equal("BG20_HERO_280p5", (string?)changes[0]["card_id"]);
        Assert.Null((string?)changes[0]["previous_card_id"]);
        Assert.Equal(1, (int?)changes[0]["turn"]);
        if (replaceMain)
        {
            Assert.Equal("main", (string?)changes[1]["slot"]);
            Assert.Equal("TB_BaconShop_HP_020", (string?)changes[1]["card_id"]);
            Assert.Equal("BG35_HERO_001p", (string?)changes[1]["previous_card_id"]);
        }

        // どちらのヒーローパワーを使ったか
        var use = events.Single(e => (string?)e["type"] == "hero_power");
        Assert.Equal(replaceMain ? "TB_BaconShop_HP_020" : "BG35_HERO_001p", (string?)use["source_card_id"]);
        Assert.Equal(1, log.Turns.Single(t => t.Turn == 1).Actions!.HeroPowerUses);

        var schema = await NJsonSchema.JsonSchema.FromJsonAsync(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "game-log.schema.json")));
        var errors = schema.Validate(HsbgKifu.Output.GameLogWriter.Serialize(log)).Select(e => $"{e.Path}: {e.Kind}").ToArray();
        Assert.True(errors.Length == 0, string.Join("\n", errors));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)] // HERO_POWER_ENTITY の無いログ（2026-09 のゲーム）: PLAY のヒーローパワーから決める
    public void RecordsOneHeroPowerWithoutChanges(bool withHeroPowerEntity)
    {
        var r = Run(SetupWithHeroPower("TB_BaconShop_HP_035", withHeroPowerEntity).Concat(OneTurn()).Concat(End()));
        Assert.Equal("TB_BaconShop_HP_035", r.Record!.Player.HeroPower.CardId);
        var held = Assert.Single(r.Record.Turns.Single(t => t.Turn == 1).HeroPowers);
        Assert.Equal(("main", "TB_BaconShop_HP_035"), (held.Slot, held.CardId!));
        Assert.DoesNotContain(r.EventLines, l => l.Contains("\"type\":\"hero_power_changed\""));
    }

    [Fact]
    public void RecordsAHeroPowerThatTurnsIntoATrinket()
    {
        // マリンのヒーローパワーは、トリンケットに変わってなくなる（CHANGE_ENTITY。2026-09-26 19:39 のゲームの T5）
        var lines = OneTurn();
        var at = lines.FindIndex(l => l.Contains("tag=TURN value=2")) - 1;
        lines.InsertRange(at, new[]
        {
            PL("10:01:25.0000000"),
            P("10:01:25.0000000", "CHANGE_ENTITY - Updating Entity=[entityName=x id=120 zone=PLAY zonePos=0 cardId=BG30_HERO_304p player=2] CardID=BG30_MagicItem_423"),
            P("10:01:25.0000000", "    tag=CARDTYPE value=BATTLEGROUND_TRINKET"),
        });
        var r = Run(SetupWithHeroPower("BG30_HERO_304p").Concat(lines).Concat(End()));
        Assert.Equal("BG30_HERO_304p", r.Record!.Player.HeroPower.CardId);
        Assert.Empty(r.Record.Turns.Single(t => t.Turn == 1).HeroPowers);
        var change = Assert.Single(r.EventLines.Select(JObject.Parse), e => (string?)e["type"] == "hero_power_changed");
        Assert.Equal("main", (string?)change["slot"]);
        Assert.Null((string?)change["card_id"]);
        Assert.Equal("BG30_HERO_304p", (string?)change["previous_card_id"]);
    }

    /// <summary>
    /// ターン 1 の始まりに、ヒーローパワー（Entity 120）で時渡りの酒場に行く。クロナム 2、候補 3 枚（801: ミニオン 1 クロナム、802: ミニオン 2 クロナム、
    /// 803: 呪文 1 クロナム。ミニオンのティアは <paramref name="tier"/>）。801 を買うと、残り 1 クロナムで買える 803 だけの選択肢が出て、803 を買って終わる。
    /// 時渡りの間は RESOURCES がクロナムで、終わると通常のゴールド（3）に戻る。2026-11-02 14:26 のミロクーのゲームの T5 と同じ並び（docs/research.md §3.5）。
    /// </summary>
    private static List<string> TimewarpTurn(int tier)
    {
        var lines = OneTurn();
        var at = lines.FindIndex(l => l.Contains("CardID=BG31_815")); // ターン 1 の酒場が並ぶ前
        var add = new List<string>
        {
            P("10:01:00.0000000", "TAG_CHANGE Entity=GameEntity tag=BACON_ALT_TAVERN_IN_PROGRESS value=1 "),
            P("10:01:00.0000000", "TAG_CHANGE Entity=Me tag=RESOURCES value=0 "),
            P("10:01:00.0000000", "TAG_CHANGE Entity=Me tag=RESOURCES value=2 "),
            P("10:01:00.0000000", "TAG_CHANGE Entity=Me tag=BACON_ALT_TAVERN_COIN value=2 "),
        };
        foreach (var (id, card, type, cost) in new[] { (801, "BG34_Giant_580", "MINION", 1), (802, "BG34_Giant_582", "MINION", 2), (803, "BG34_Treasure_607", "BATTLEGROUND_SPELL", 1) })
        {
            add.Add(P("10:01:00.0000000", $"FULL_ENTITY - Creating ID={id} CardID={card}"));
            add.Add(P("10:01:00.0000000", $"    tag=CARDTYPE value={type}"));
            add.Add(P("10:01:00.0000000", "    tag=CONTROLLER value=2"));
            add.Add(P("10:01:00.0000000", "    tag=ZONE value=SETASIDE"));
            add.Add(P("10:01:00.0000000", $"    tag=TECH_LEVEL value={(type == "MINION" ? tier : 3)}"));
            add.Add(P("10:01:00.0000000", $"    tag=COST value={cost}"));
            add.Add(P("10:01:00.0000000", "    tag=CREATOR value=120"));
            add.Add(P("10:01:00.0000000", "    tag=BACON_TIMEWARPED value=1"));
            add.Add(P("10:01:00.0000000", $"TAG_CHANGE Entity={id} tag=BACON_OVERRIDE_BG_COST value={cost} "));
            add.Add(P("10:01:00.0000000", $"TAG_CHANGE Entity={id} tag=IS_A_SHOP_CHOICE_CARD value=1 "));
        }
        add.AddRange(new[]
        {
            "D 10:01:00.1000000 GameState.DebugPrintEntityChoices() - id=30 Player=Me TaskList=10050 ChoiceType=GENERAL CountMin=1 CountMax=1",
            "D 10:01:00.1000000 GameState.DebugPrintEntityChoices() -   Source=[entityName=x id=120 zone=PLAY zonePos=0 cardId=BG34_HERO_004p player=2]",
            "D 10:01:00.1000000 GameState.DebugPrintEntityChoices() -   Entities[0]=[entityName=x id=801 zone=SETASIDE zonePos=0 cardId=BG34_Giant_580 player=2]",
            "D 10:01:00.1000000 GameState.DebugPrintEntityChoices() -   Entities[1]=[entityName=x id=802 zone=SETASIDE zonePos=0 cardId=BG34_Giant_582 player=2]",
            "D 10:01:00.1000000 GameState.DebugPrintEntityChoices() -   Entities[2]=[entityName=x id=803 zone=SETASIDE zonePos=0 cardId=BG34_Treasure_607 player=2]",
            "D 10:01:02.0000000 GameState.DebugPrintEntitiesChosen() - id=30 Player=Me EntitiesCount=1",
            "D 10:01:02.0000000 GameState.DebugPrintEntitiesChosen() -   Entities[0]=[entityName=x id=801 zone=SETASIDE zonePos=0 cardId=BG34_Giant_580 player=2]",
            PL("10:01:02.1000000"),
            P("10:01:02.1000000", "TAG_CHANGE Entity=801 tag=ZONE value=HAND "),
            P("10:01:02.1000000", "TAG_CHANGE Entity=Me tag=RESOURCES_USED value=1 "),
            "D 10:01:02.2000000 GameState.DebugPrintEntityChoices() - id=31 Player=Me TaskList=10051 ChoiceType=GENERAL CountMin=1 CountMax=1",
            "D 10:01:02.2000000 GameState.DebugPrintEntityChoices() -   Source=[entityName=x id=120 zone=PLAY zonePos=0 cardId=BG34_HERO_004p player=2]",
            "D 10:01:02.2000000 GameState.DebugPrintEntityChoices() -   Entities[0]=[entityName=x id=803 zone=SETASIDE zonePos=0 cardId=BG34_Treasure_607 player=2]",
            "D 10:01:03.0000000 GameState.DebugPrintEntitiesChosen() - id=31 Player=Me EntitiesCount=1",
            "D 10:01:03.0000000 GameState.DebugPrintEntitiesChosen() -   Entities[0]=[entityName=x id=803 zone=SETASIDE zonePos=0 cardId=BG34_Treasure_607 player=2]",
            PL("10:01:03.1000000"),
            P("10:01:03.1000000", "TAG_CHANGE Entity=803 tag=ZONE value=HAND "),
            P("10:01:03.1000000", "TAG_CHANGE Entity=Me tag=RESOURCES_USED value=2 "),
            P("10:01:03.1000000", "TAG_CHANGE Entity=GameEntity tag=BACON_ALT_TAVERN_IN_PROGRESS value=0 "),
            P("10:01:03.1000000", "TAG_CHANGE Entity=Me tag=RESOURCES value=0 "),
            P("10:01:03.1000000", "TAG_CHANGE Entity=Me tag=RESOURCES_USED value=0 "),
            P("10:01:03.1000000", "TAG_CHANGE Entity=Me tag=RESOURCES value=3 "),
        });
        lines.InsertRange(at, add);
        return lines;
    }

    [Theory]
    [InlineData(3, "minor")]
    [InlineData(5, "major")]
    public async Task RecordsATimewarpVisit(int tier, string kind)
    {
        var r = Run(SetupWithHeroPower("BG34_HERO_004p").Concat(TimewarpTurn(tier)).Concat(End()));
        var log = r.Record!;
        var t1 = log.Turns.Single(t => t.Turn == 1);
        var tw = t1.Timewarp!;
        Assert.Equal(kind, tw.Kind);
        Assert.Equal("BG34_HERO_004p", tw.SourceCard!.CardId);
        Assert.Equal(2, tw.Chronum);
        Assert.Equal(new[] { "BG34_Giant_580", "BG34_Giant_582", "BG34_Treasure_607" }, tw.Offered.Select(o => o.CardId));
        Assert.Equal(new int?[] { 1, 2, 1 }, tw.Offered.Select(o => o.Cost));
        Assert.Equal("spell", tw.Offered[2].CardType);
        Assert.Equal(new[] { (0, 1), (2, 1) }, tw.Bought.Select(b => (b.Index!.Value, b.Cost!.Value)));
        Assert.Equal(0, tw.ChronumLeft);

        // 選択肢は 2 回とも timewarp（2 回目は買える候補だけ）
        Assert.Equal(new[] { "timewarp", "timewarp" }, t1.Choices.Select(c => c.Source));
        Assert.Equal(new[] { 3, 1 }, t1.Choices.Select(c => c.Offered.Count));

        // 通常の酒場の記録には混ざらない（クロナムの増減を売却やゴールド獲得と数えない）
        Assert.Equal(1, t1.Actions!.Buys);
        Assert.Equal(0, t1.Actions.Sells);
        Assert.Equal(0, t1.Actions.GoldGained ?? 0);
        Assert.Equal(3, t1.Actions.GoldSpent);
        Assert.Equal(3, t1.GoldAvailable);

        var events = r.EventLines.Select(JObject.Parse).ToList();
        Assert.DoesNotContain(events, e => (string?)e["type"] == "discover");
        var visit = events.Single(e => (string?)e["type"] == "timewarp");
        Assert.Equal(kind, (string?)visit["kind"]);
        Assert.Equal(2, (int?)visit["chronum"]);
        Assert.Equal(new[] { 1, 2, 1 }, visit["costs"]!.Select(x => (int)x));
        var buys = events.Where(e => (string?)e["type"] == "timewarp_buy").ToList();
        Assert.Equal(new[] { "BG34_Giant_580", "BG34_Treasure_607" }, buys.Select(e => (string?)e["card_id"]));
        Assert.Equal(new[] { 1, 0 }, buys.Select(e => (int)e["chronum_after"]!));
        Assert.Equal(new[] { 0, 2 }, buys.Select(e => (int)e["index"]!));

        var schema = await NJsonSchema.JsonSchema.FromJsonAsync(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "game-log.schema.json")));
        var errors = schema.Validate(HsbgKifu.Output.GameLogWriter.Serialize(log)).Select(e => $"{e.Path}: {e.Kind}").ToArray();
        Assert.True(errors.Length == 0, string.Join("\n", errors));
    }

    [Fact]
    public void ATurnWithoutTimewarpHasNoTimewarpRecord()
    {
        var r = Run(SetupWithHeroPower().Concat(OneTurn()).Concat(End()));
        Assert.All(r.Record!.Turns, t => Assert.Null(t.Timewarp));
        Assert.DoesNotContain(r.EventLines, l => l.Contains("\"type\":\"timewarp"));
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
