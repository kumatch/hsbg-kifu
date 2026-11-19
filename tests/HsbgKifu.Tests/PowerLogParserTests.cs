using HearthDb.Enums;
using HsbgKifu.Parse;
using Xunit;

namespace HsbgKifu.Tests;

public class PowerLogParserTests
{
    private static readonly DateTime T0 = new(2026, 11, 20, 10, 0, 0);
    private const string Me = "Me" + "#" + "1111";       // 架空の BattleTag（check_private.py に掛からないよう組み立てる）
    private const string Bob = "Bobby";
    private const string Someone = "Other" + "#" + "2222";

    private static string P(string body) => $"D 10:00:00.0000000 GameState.DebugPrintPower() - {body}";
    private static string G(string body) => $"D 10:00:00.0000000 GameState.DebugPrintGame() - {body}";
    private static string C(string body) => $"D 10:00:00.0000000 GameState.DebugPrintEntityChoices() - {body}";

    /// <summary>出来事を順に文字列で記録する受け手。</summary>
    private sealed class Recorder : PowerGameListener
    {
        public readonly List<string> Events = new();
        public override void OnGameCreated(PowerGame game) => Events.Add("created");
        public override void OnGameSetupDone(PowerGame game) => Events.Add($"setup mid={game.StartedMidGame} local={game.LocalPlayerId}");
        public override void OnLocalPlayerKnown(PowerGame game, int playerId) => Events.Add($"local {playerId}");
        public override void OnEntityUpdated(PowerGame game, Entity entity, TagChangeKind kind) => Events.Add($"entity {entity.Id} {kind} {entity.CardId}");
        public override void OnTurnChanged(PowerGame game, int turn) => Events.Add($"turn {turn}");
        public override void OnCombatSetupEnded(PowerGame game, int tag) => Events.Add($"combat {tag}");
        public override void OnPredamage(PowerGame game, Entity entity, int value) => Events.Add($"predamage {entity.Id} {value}");
        public override void OnDamage(PowerGame game, Entity entity, int amount) => Events.Add($"damage {entity.Id} {amount}");
        public override void OnChoices(PowerGame game, EntityChoice choice) => Events.Add($"choices {choice.ChoiceType} {string.Join(",", choice.EntityIds)}");
        public override void OnGameEnded(PowerGame game) => Events.Add("ended");
    }

    private static (PowerLogParser Parser, Recorder Rec) Run(IEnumerable<string> lines)
    {
        var rec = new Recorder();
        var parser = new PowerLogParser(rec);
        foreach (var l in lines) parser.Feed(l, T0);
        parser.Finish();
        return (parser, rec);
    }

    /// <summary>ゲームの始まり: GameEntity、自分（PlayerID 2、Entity 5）、Bob 役（PlayerID 10、Entity 6）。</summary>
    private static List<string> Setup(int turn = 0) => new()
    {
        "D 10:00:00.0000000 GameState.DebugPrintPowerList() - Count=44",
        P("CREATE_GAME"),
        P("    GameEntity EntityID=4"),
        P("        tag=CARDTYPE value=GAME"),
        P($"        tag=TURN value={turn}"),
        P("    Player EntityID=5 PlayerID=2 GameAccountId=[hi=1 lo=2]"),
        P("        tag=CARDTYPE value=PLAYER"),
        P("        tag=HERO_ENTITY value=100"),
        P("    Player EntityID=6 PlayerID=10 GameAccountId=[hi=0 lo=0]"),
        P("        tag=CARDTYPE value=PLAYER"),
        P("        tag=BACON_DUMMY_PLAYER value=1"),
        P("FULL_ENTITY - Creating ID=100 CardID=BG36_HERO_105"),
        P("    tag=CARDTYPE value=HERO"),
        P("    tag=CONTROLLER value=2"),
        P("    tag=ZONE value=PLAY"),
        P("    tag=HEALTH value=30"),
        P("    tag=PLAYER_LEADERBOARD_PLACE value=8"),
        P("TAG_CHANGE Entity=4 tag=STATE value=RUNNING "),
        G("BuildNumber=255790"),
        G("GameType=GT_BATTLEGROUNDS"),
        G($"PlayerID=2, PlayerName={Me}"),
        G($"PlayerID=10, PlayerName={Bob}"),
    };

    [Fact]
    public void BuildsStateAndFindsTheLocalPlayer()
    {
        var lines = Setup();
        lines.AddRange(new[]
        {
            C($"id=1 Player={Me} TaskList=7 ChoiceType=MULLIGAN CountMin=1 CountMax=1"),
            C("  Source=GameEntity"),
            C("  Entities[0]=[entityName=Hero A id=100 zone=HAND zonePos=1 cardId=BG36_HERO_105 player=2]"),
            C("  Entities[1]=[entityName=UNKNOWN ENTITY [cardType=INVALID] id=101 zone=HAND zonePos=2 cardId= player=2]"),
            P("TAG_CHANGE Entity=GameEntity tag=TURN value=1"),
        });
        var (parser, rec) = Run(lines);
        var game = parser.Game!;
        Assert.Equal(2, game.LocalPlayerId);
        Assert.Equal(10, game.OpponentPlayerId);
        Assert.Equal(255790, game.BuildNumber);
        Assert.Equal("GT_BATTLEGROUNDS", game.GameType);
        Assert.True(game.IsBattlegrounds);
        Assert.False(game.StartedMidGame);
        var hero = game.PlayerHero!;
        Assert.Equal("BG36_HERO_105", hero.CardId);
        Assert.True(hero.IsHero);
        Assert.True(hero.IsInPlay);
        Assert.True(hero.IsControlledBy(2));
        Assert.Equal(30, hero.Health);
        Assert.Equal(1, game.TurnNumber);
        Assert.Equal(new[] { 100, 101 }, game.Choices.Single().EntityIds);
        Assert.Equal(new[]
        {
            "created", "entity 4 Creation ", "entity 5 Creation ", "entity 6 Creation ", "entity 100 Creation BG36_HERO_105",
            "local 2", "setup mid=False local=2", // Bob 役でない方が自分（ヒーロー選択の前に分かる）
            "choices MULLIGAN 100,101", "turn 1",
        }, rec.Events);
    }

    [Fact]
    public void ResolvesEntityReferences()
    {
        var lines = Setup();
        lines.AddRange(new[]
        {
            P($"TAG_CHANGE Entity={Me} tag=RESOURCES value=3 "),                    // 自分の名前
            P($"TAG_CHANGE Entity={Someone} tag=RESOURCES value=7 "),               // 知らない名前 → Bob 役
            P("TAG_CHANGE Entity=[entityName=Hero id=100 zone=PLAY zonePos=0 cardId=BG36_HERO_105 player=2] tag=ARMOR value=5 DEF CHANGE"),
            P("        TAG_CHANGE Entity=100 tag=DAMAGE value=4 "),                   // インデントされた TAG_CHANGE
            P("TAG_CHANGE Entity=[entityName=UNKNOWN ENTITY [cardType=INVALID] id=100 zone=PLAY zonePos=0 cardId= player=2] tag=3245 value=2 "),
            P("TAG_CHANGE Entity=GameEntity tag=BACON_GLOBAL_OLD_GOD_DBID value=130610 "),
        });
        var (parser, _) = Run(lines);
        var game = parser.Game!;
        Assert.Equal(3, game.PlayerEntity!.GetTag(GameTag.RESOURCES));
        Assert.Equal(7, game.OpponentEntity!.GetTag(GameTag.RESOURCES));
        var hero = game.PlayerHero!;
        Assert.Equal(5, hero.Armor);
        Assert.Equal(26, hero.Health);
        Assert.Equal(2, hero.GetTag(3245));
        Assert.Equal(130610, game.GameEntity!.GetTag(GameTag.BACON_GLOBAL_OLD_GOD_DBID));
        Assert.Equal(0, parser.UnresolvedNameCount);
    }

    [Fact]
    public void RaisesTurnCombatDamageAndEndEvents()
    {
        var lines = Setup();
        lines.AddRange(new[]
        {
            P("TAG_CHANGE Entity=GameEntity tag=TURN value=1"),
            P("TAG_CHANGE Entity=GameEntity tag=TURN value=2"),
            P("TAG_CHANGE Entity=GameEntity tag=3533 value=1 "),
            P("TAG_CHANGE Entity=GameEntity tag=3533 value=0 "),
            P("FULL_ENTITY - Creating ID=200 CardID=BG22_HERO_201_SKIN_C"),
            P("    tag=CARDTYPE value=HERO"),
            P("    tag=CONTROLLER value=10"),
            P("    tag=ZONE value=PLAY"),
            // 1 回の攻撃で PREDAMAGE が 2 回立ち、DAMAGE の META_DATA は 1 回
            P("    TAG_CHANGE Entity=[entityName=x id=200 zone=PLAY zonePos=0 cardId=BG22_HERO_201_SKIN_C player=10] tag=PREDAMAGE value=2 "),
            P("    TAG_CHANGE Entity=200 tag=PREDAMAGE value=0 "),
            P("    TAG_CHANGE Entity=200 tag=PREDAMAGE value=2 "),
            P("    TAG_CHANGE Entity=200 tag=PREDAMAGE value=0 "),
            P("    META_DATA - Meta=DAMAGE Data=2 InfoCount=1"),
            P("                Info[0] = [entityName=x id=200 zone=PLAY zonePos=0 cardId=BG22_HERO_201_SKIN_C player=10]"),
            P("    META_DATA - Meta=SLUSH_TIME Data=1140 InfoCount=1"),
            P("                Info[0] = [entityName=x id=100 zone=PLAY zonePos=0 cardId=BG36_HERO_105 player=2]"),
            P("TAG_CHANGE Entity=GameEntity tag=TURN value=3"),
            P("TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE "),
            P("TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE "),
            "D 10:00:00.0000000 PowerTaskList.DebugPrintPower() -     TAG_CHANGE Entity=GameEntity tag=TURN value=4", // 読まない
        });
        var (parser, rec) = Run(lines);
        Assert.Equal(new[]
        {
            "turn 1", "turn 2", "combat 3533", "entity 200 Creation BG22_HERO_201_SKIN_C",
            "predamage 200 2", "predamage 200 2", "damage 200 2", "turn 3", "ended",
        }, rec.Events.SkipWhile(e => !e.StartsWith("turn")).ToArray());
        Assert.True(parser.Game!.Ended);
        Assert.Equal(2, parser.Game.TurnNumber);
    }

    [Fact]
    public void ShowChangeAndHideEntity()
    {
        var lines = Setup();
        lines.AddRange(new[]
        {
            P("FULL_ENTITY - Creating ID=300"),
            P("    tag=ZONE value=HAND"),
            P("    tag=CONTROLLER value=2"),
            P("SHOW_ENTITY - Updating Entity=300 CardID=BG36_849"),
            P("    tag=CARDTYPE value=MINION"),
            P("    tag=ATK value=5"),
            P("    tag=HEALTH value=4"),
            P("    tag=CARDRACE value=QUILBOAR"),
            P("CHANGE_ENTITY - Updating Entity=[entityName=x id=300 zone=HAND zonePos=1 cardId=BG36_849 player=2] CardID=BG36_849_G"),
            P("    tag=PREMIUM value=1"),
            P("HIDE_ENTITY - Entity=[entityName=x id=300 zone=HAND zonePos=1 cardId=BG36_849_G player=2] tag=ZONE value=GRAVEYARD"),
        });
        var (parser, rec) = Run(lines);
        var e = parser.Game!.Entities[300];
        Assert.Equal("BG36_849_G", e.CardId);
        Assert.True(e.IsMinion);
        Assert.Equal(5, e.Attack);
        Assert.Equal((int)Race.QUILBOAR, e.GetTag(GameTag.CARDRACE));
        Assert.Equal(1, e.GetTag(GameTag.PREMIUM));
        Assert.True(e.IsInGraveyard);
        Assert.Contains("entity 300 Creation ", rec.Events);
        Assert.Contains("entity 300 Show BG36_849", rec.Events);
        Assert.Contains("entity 300 Change BG36_849_G", rec.Events);
    }

    [Fact]
    public void ReconnectSnapshotStartsMidGame()
    {
        // 再接続: CREATE_GAME の下に、その時点の状態（TURN 23）。ヒーロー選択は来ないので Bob 役でない方が自分
        var (parser, rec) = Run(Setup(turn: 23));
        Assert.True(parser.Game!.StartedMidGame);
        Assert.Equal(2, parser.Game.LocalPlayerId);
        Assert.Equal(12, parser.Game.TurnNumber);
        Assert.Contains("setup mid=True local=2", rec.Events);
        Assert.DoesNotContain(rec.Events, e => e.StartsWith("turn")); // 作られたときの TURN では出来事を出さない
    }

    [Fact]
    public void UnknownNamesAreCountedNotFatal()
    {
        var lines = Setup();
        lines.Add(P("TAG_CHANGE Entity=100 tag=SOME_FUTURE_TAG value=1 "));
        lines.Add(P("TAG_CHANGE Entity=100 tag=ZONE value=SOME_FUTURE_ZONE "));
        lines.Add(P("TAG_CHANGE Entity=100 tag=ARMOR value=3 "));
        var (parser, _) = Run(lines);
        Assert.Equal(1, parser.UnknownTagCount);
        Assert.True(parser.Game!.PlayerHero!.IsInPlay); // 知らない値は当てはめない
        Assert.Equal(3, parser.Game.PlayerHero.Armor);
    }

    [Fact]
    public void NameBeforeDebugPrintGameIsUnresolved()
    {
        var lines = Setup().Take(18).ToList(); // DebugPrintGame の前まで
        lines.Add(P($"TAG_CHANGE Entity={Me} tag=RESOURCES value=3 "));
        var (parser, _) = Run(lines);
        Assert.Equal(1, parser.UnresolvedNameCount);
    }

    [Fact]
    public void ListenerExceptionDoesNotStopParsing()
    {
        var rec = new Recorder();
        var parser = new PowerLogParser(new Throwing(), rec);
        foreach (var l in Setup().Append(P("TAG_CHANGE Entity=GameEntity tag=TURN value=1"))) parser.Feed(l, T0);
        Assert.Contains("turn 1", rec.Events);
    }

    private sealed class Throwing : PowerGameListener
    {
        public override void OnTagChanged(PowerGame game, Entity entity, int tag, int previous, int value, TagChangeKind kind) =>
            throw new InvalidOperationException("boom");
    }
}
