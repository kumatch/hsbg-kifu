using System.Text.Json;
using HsbgKifu.Watch;
using Xunit;

namespace HsbgKifu.Tests;

public class GameSlicerTests : IDisposable
{
    private readonly TempDir _tmp = new();
    private static readonly LogSession Session = new("Hearthstone_2026_11_14_16_51_12", "unused", new DateTime(2026, 11, 14, 16, 51, 12));
    private static readonly GameServerEntry[] Servers =
    {
        new(new DateTime(2026, 11, 14, 17, 0, 0) - TimeSpan.FromSeconds(6), 6350, false),
        new(new DateTime(2026, 11, 14, 17, 30, 0) - TimeSpan.FromSeconds(6), 12688, false),
    };

    public void Dispose() => _tmp.Dispose();

    private (SliceStore Store, GameSlicer Slicer) NewSlicer(SliceStore? store = null)
    {
        store ??= new SliceStore(_tmp.Sub(".slices"));
        return (store, new GameSlicer(Session, @"C:\x\Power.log", store, () => Servers));
    }

    private static void FeedAll(GameSlicer slicer, IEnumerable<string> lines)
    {
        foreach (var l in TestLogs.ToLogLines(lines)) slicer.Feed(l);
    }

    private static List<SliceMeta> Metas(SliceStore store) => store.All.OrderBy(m => m.StartTime).ToList();

    private string GzOf(SliceMeta meta) =>
        Directory.GetFiles(_tmp.Path, "*.power.log.gz", SearchOption.AllDirectories)
            .Single(p => JsonSerializer.Deserialize<SliceMeta>(File.ReadAllText(p.Replace(".power.log.gz", ".slice.json")))!.Key == meta.Key);

    [Fact]
    public void SlicesTwoBattlegroundsGamesAndSkipsOtherModes()
    {
        var (store, slicer) = NewSlicer();
        var g1 = TestLogs.OneGame("17:00");
        var other = TestLogs.OneGame("17:10", gameType: "GT_RANKED");
        var g2 = TestLogs.OneGame("17:30");
        FeedAll(slicer, g1.Concat(other).Concat(g2));
        slicer.EndOfSource();

        var metas = Metas(store);
        Assert.Equal(2, metas.Count);
        Assert.Equal(new long?[] { 6350, 12688 }, metas.Select(m => m.GameHandle));
        Assert.All(metas, m =>
        {
            Assert.Equal(SliceMeta.StatusComplete, m.Status);
            Assert.True(m.ServerComplete);
            Assert.True(m.ClientComplete);
            Assert.Equal("GT_BATTLEGROUNDS", m.GameType);
            Assert.Equal(254477, m.BuildNumber);
            Assert.Equal("Power.log", m.SourceFile);
            Assert.False(m.Reconnecting);
        });
        Assert.Equal("17:00:00.0000000", metas[0].CreateGameTime);
        Assert.Equal(new DateTime(2026, 11, 14, 17, 0, 0), metas[0].StartTime.LocalDateTime);
        Assert.Equal(new DateTime(2026, 11, 14, 17, 0, 50), metas[0].EndTime!.Value.LocalDateTime);

        // 中身: Count= の行から画面側の COMPLETE まで。その後の後片付けの行は入れない
        var lines = TestLogs.ReadGz(GzOf(metas[0]));
        Assert.Equal(g1.Take(g1.Count - 2), lines);
        Assert.Equal(lines.Count, metas[0].LineCount);
        Assert.Equal(2, slicer.FinishedCount);
        Assert.Empty(Directory.GetFiles(_tmp.Path, "*.power.log", SearchOption.AllDirectories)); // 平文は消えている
    }

    [Fact]
    public void FileNameUsesStartTimeAndGameHandle()
    {
        var (store, slicer) = NewSlicer();
        FeedAll(slicer, TestLogs.OneGame("17:00"));
        Assert.True(File.Exists(Path.Combine(store.Root, "2026", "11", "20261114-170000_6350.power.log.gz")));
        Assert.True(File.Exists(Path.Combine(store.Root, "2026", "11", "20261114-170000_6350.slice.json")));
    }

    [Fact]
    public void GameThatNeverEndsIsIncompleteAtEndOfSource()
    {
        var (store, slicer) = NewSlicer();
        FeedAll(slicer, TestLogs.OneGame("17:00", serverComplete: false));
        Assert.Equal(SliceMeta.StatusRecording, slicer.Current?.Status);
        Assert.True(File.Exists(Path.Combine(store.Root, "2026", "11", "20261114-170000_6350.power.log"))); // 書いている途中は平文
        slicer.EndOfSource();
        var meta = Assert.Single(Metas(store));
        Assert.Equal(SliceMeta.StatusIncomplete, meta.Status);
        Assert.False(meta.ServerComplete);
    }

    [Fact]
    public void ServerCompleteWithoutClientCompleteIsClosedOnRequestAndAtNextGame()
    {
        var (store, slicer) = NewSlicer();
        FeedAll(slicer, TestLogs.OneGame("17:00", clientComplete: false));
        Assert.True(slicer.FinishIfServerComplete());
        Assert.False(slicer.FinishIfServerComplete());
        Assert.Equal(SliceMeta.StatusComplete, Assert.Single(Metas(store)).Status);

        var (store2, slicer2) = NewSlicer(new SliceStore(_tmp.Sub("other")));
        FeedAll(slicer2, TestLogs.OneGame("17:00", clientComplete: false).Concat(TestLogs.OneGame("17:30")));
        Assert.Equal(new[] { SliceMeta.StatusComplete, SliceMeta.StatusComplete }, Metas(store2).Select(m => m.Status));
        Assert.False(Metas(store2)[0].ClientComplete);
    }

    [Fact]
    public void DoneGamesAreSkippedAndRecordingOnesAreRewritten()
    {
        var root = _tmp.Sub(".slices");
        var (_, first) = NewSlicer(new SliceStore(root));
        FeedAll(first, TestLogs.OneGame("17:00").Concat(TestLogs.OneGame("17:30", serverComplete: false)));
        first.Flush();
        first.Dispose(); // 2 ゲーム目は書いている途中で止まった（recording のまま）

        var store = new SliceStore(root);
        var key1 = SliceMeta.MakeKey(Session.Name, "17:00:00.0000000");
        var key2 = SliceMeta.MakeKey(Session.Name, "17:30:00.0000000");
        Assert.True(store.IsDone(key1));
        Assert.False(store.IsDone(key2));

        var (_, second) = NewSlicer(store);
        FeedAll(second, TestLogs.OneGame("17:00").Concat(TestLogs.OneGame("17:30")));
        second.EndOfSource();
        Assert.Equal(1, second.FinishedCount); // 1 ゲーム目は読み飛ばした
        Assert.Equal(new[] { SliceMeta.StatusComplete, SliceMeta.StatusComplete }, Metas(new SliceStore(root)).Select(m => m.Status));
        Assert.Equal(2, Directory.GetFiles(root, "*.slice.json", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void UnknownGameTypeIsTreatedAsBattlegroundsAfterTheLimit()
    {
        var (store, slicer) = NewSlicer();
        var lines = new List<string> { TestLogs.Gs("17:00:00.0000000", "CREATE_GAME") };
        for (var i = 0; i < GameSlicer.MaxPendingLines + 10; i++) lines.Add(TestLogs.Gs("17:00:01.0000000", $"TAG_CHANGE Entity=1 tag=X value={i}"));
        FeedAll(slicer, lines);
        slicer.EndOfSource();
        var meta = Assert.Single(Metas(store));
        Assert.Null(meta.GameType);
        Assert.Equal(lines.Count, meta.LineCount);
    }

    [Fact]
    public void MetaHasNoPersonalOrNetworkData()
    {
        var (store, slicer) = NewSlicer();
        var g = TestLogs.OneGame("17:00");
        var fakeTag = "Someone" + "#" + "1234"; // 架空の BattleTag（check_private.py に掛からないよう組み立てる）
        g.Insert(8, TestLogs.Game("17:00:00.0000000", $"PlayerID=7, PlayerName={fakeTag}"));
        FeedAll(slicer, g);
        var json = File.ReadAllText(Directory.GetFiles(store.Root, "*.slice.json", SearchOption.AllDirectories).Single());
        Assert.DoesNotContain("Someone", json);
        Assert.DoesNotContain("192.0.2.1", json);
        Assert.DoesNotContain("spectate", json);
    }
}
