using HearthDb.Enums;
using HsbgKifu.Capture;
using HsbgKifu.Parse;
using HsbgKifu.Watch;
using Xunit;

namespace HsbgKifu.Tests;

public class LobbyMinionTypesTests
{
    /// <summary>ショップのミニオンを、Power.log の行から作る（種族はタグで付ける）。</summary>
    private static PowerGame Shop(params (int Id, string Race, int[] RaceTags)[] minions)
    {
        var lines = new List<string> { "D 10:00:00.0000000 GameState.DebugPrintPower() - CREATE_GAME" };
        foreach (var (id, race, tags) in minions)
        {
            lines.Add($"D 10:00:00.0000000 GameState.DebugPrintPower() - FULL_ENTITY - Creating ID={id} CardID=X{id}");
            lines.Add("D 10:00:00.0000000 GameState.DebugPrintPower() -     tag=CARDTYPE value=MINION");
            if (race != "") lines.Add($"D 10:00:00.0000000 GameState.DebugPrintPower() -     tag=CARDRACE value={race}");
            foreach (var t in tags) lines.Add($"D 10:00:00.0000000 GameState.DebugPrintPower() -     tag={t} value=1");
        }
        var parser = new PowerLogParser();
        foreach (var l in lines) parser.Feed(l, new DateTime(2026, 11, 21, 10, 0, 0));
        parser.Finish();
        return parser.Game!;
    }

    [Fact]
    public void IgnoresNeutralAndAllTypesAndCountsBothTypesOfDualMinions()
    {
        var all = RaceUtils.TagRaceMap.Where(kv => kv.Value is Race.MURLOC or Race.DEMON or Race.BEAST).Select(kv => kv.Key).ToArray();
        var naga = RaceUtils.TagRaceMap.First(kv => kv.Value == Race.NAGA).Key;
        var game = Shop((1, "", Array.Empty<int>()), (2, "ALL", Array.Empty<int>()), (3, "", all), (4, "DEMON", new[] { naga }), (5, "PIRATE", Array.Empty<int>()));
        Assert.Empty(LobbyMinionTypes.RacesOf(game.Entities[1])); // 無種族
        Assert.Empty(LobbyMinionTypes.RacesOf(game.Entities[2])); // 全種族（CARDRACE=ALL）
        Assert.Empty(LobbyMinionTypes.RacesOf(game.Entities[3])); // 全種族（種族のタグが 3 つ以上）
        Assert.Equal(new[] { Race.DEMON, Race.NAGA }.OrderBy(x => x), LobbyMinionTypes.RacesOf(game.Entities[4]).OrderBy(x => x));
        Assert.Equal(new[] { Race.PIRATE }, LobbyMinionTypes.RacesOf(game.Entities[5]));
    }

    [Fact]
    public void TakesTheFiveMostCommonTypes()
    {
        var naga = RaceUtils.TagRaceMap.First(kv => kv.Value == Race.NAGA).Key;
        var minions = new List<(int, string, int[])>();
        var id = 1;
        foreach (var race in new[] { "DEMON", "DRAGON", "ELEMENTAL", "MECHANICAL", "PIRATE" })
            for (var i = 0; i < 6; i++) minions.Add((id++, race, Array.Empty<int>()));
        minions.Add((id++, "DEMON", new[] { naga })); // ロビー外の種族（ナーガ）が 2 つ目の種族として少しだけ
        minions.Add((id++, "BEAST", Array.Empty<int>()));
        var game = Shop(minions.ToArray());
        var lobby = new LobbyMinionTypes();
        lobby.Observe(game, game.Entities.Keys);
        lobby.Observe(game, game.Entities.Keys); // 同じミニオンは 1 回だけ数える
        Assert.Equal(new[] { "demon", "dragon", "elemental", "mechanical", "pirate" }, lobby.Result(out var tie));
        Assert.False(tie);
        Assert.Equal(game.Entities.Count, lobby.MinionsSeen);
    }

    [Fact]
    public void ReportsTiesAndTooFewTypes()
    {
        var game = Shop((1, "DEMON", Array.Empty<int>()), (2, "DRAGON", Array.Empty<int>()), (3, "PIRATE", Array.Empty<int>()),
            (4, "MURLOC", Array.Empty<int>()), (5, "BEAST", Array.Empty<int>()), (6, "UNDEAD", Array.Empty<int>()));
        var lobby = new LobbyMinionTypes();
        lobby.Observe(game, game.Entities.Keys);
        Assert.NotNull(lobby.Result(out var tie));
        Assert.True(tie);
        var few = new LobbyMinionTypes();
        few.Observe(game, new[] { 1, 2 });
        Assert.Null(few.Result(out _));
    }
}

public class BgsLastGamesTests : IDisposable
{
    private readonly TempDir _tmp = new();
    public void Dispose() => _tmp.Dispose();

    private string Xml(string games)
    {
        var path = Path.Combine(_tmp.Path, "BgsLastGames.xml");
        File.WriteAllText(path, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<BgsLastGames>\n" + games + "</BgsLastGames>\n");
        return path;
    }

    [Fact]
    public void FindsTheNearestGameWithinTwoMinutes()
    {
        var path = Xml(
            "  <Game Player=\"1_2\" StartTime=\"2026-11-21T10:00:00.1000000+09:00\" Rating=\"8000\" RatingAfter=\"8050\" Placemenent=\"3\" />\n" +
            "  <Game Player=\"1_2\" StartTime=\"2026-11-21T10:30:00.0000000+09:00\" Rating=\"8050\" Placemenent=\"7\" />\n");
        var r = new BgsLastGames(path);
        var a = r.Find(new DateTimeOffset(2026, 11, 21, 10, 0, 1, TimeSpan.FromHours(9)))!;
        Assert.Equal(8000, a.Rating);
        Assert.Equal(8050, a.RatingAfter);
        var b = r.Find(new DateTimeOffset(2026, 11, 21, 10, 30, 1, TimeSpan.FromHours(9)))!;
        Assert.Null(b.RatingAfter); // 後の MMR がまだ書かれていない
        Assert.Null(r.Find(new DateTimeOffset(2026, 11, 21, 11, 0, 0, TimeSpan.FromHours(9))));
        Assert.Null(new BgsLastGames(Path.Combine(_tmp.Path, "none.xml")).Find(DateTimeOffset.Now));
    }
}
