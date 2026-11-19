using System.Text;
using HsbgKifu.Watch;
using Xunit;

namespace HsbgKifu.Tests;

public class LineSplitterTests
{
    [Fact]
    public void SplitsLinesAcrossChunksWithOffsets()
    {
        var bytes = Encoding.UTF8.GetBytes("abc\r\nde\nfgh");
        var s = new LineSplitter();
        var lines = new List<LogLine>();
        s.Append(bytes.AsSpan(0, 2), lines);   // "ab"
        Assert.Empty(lines);
        s.Append(bytes.AsSpan(2, 6), lines);   // "c\r\nde\n"
        Assert.Equal(new[] { new LogLine("abc", 0), new LogLine("de", 5) }, lines);
        lines.Clear();
        s.Append(bytes.AsSpan(8), lines);      // "fgh"（改行なし）
        Assert.Empty(lines);
        Assert.Equal(3, s.PendingLength);
        Assert.Equal(new LogLine("fgh", 8), s.FlushPending());
        Assert.Equal(11, s.Position);
    }

    [Fact]
    public void MultibyteCharacterSplitBetweenChunksIsNotGarbled()
    {
        var bytes = Encoding.UTF8.GetBytes("スニード\n次\n");
        var s = new LineSplitter(100);
        var lines = new List<LogLine>();
        for (var i = 0; i < bytes.Length; i++) s.Append(bytes.AsSpan(i, 1), lines); // 1 バイトずつ
        Assert.Equal(new[] { new LogLine("スニード", 100), new LogLine("次", 100 + 13) }, lines);
    }
}

public class PowerLogLineTests
{
    [Fact]
    public void RecognizesLines()
    {
        Assert.True(PowerLogLine.IsCreateGame("D 12:34:51.2930731 GameState.DebugPrintPower() - CREATE_GAME"));
        Assert.False(PowerLogLine.IsCreateGame("D 12:34:51.2930731 PowerTaskList.DebugPrintPower() - CREATE_GAME"));
        Assert.True(PowerLogLine.IsPowerList("D 12:34:51.2930731 GameState.DebugPrintPowerList() - Count=44"));
        Assert.True(PowerLogLine.IsServerComplete("D 12:51:59.5435810 GameState.DebugPrintPower() - TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE "));
        Assert.False(PowerLogLine.IsClientComplete("D 12:51:59.5435810 GameState.DebugPrintPower() - TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE "));
        Assert.True(PowerLogLine.IsClientComplete("D 12:52:39.7497715 PowerTaskList.DebugPrintPower() -     TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE "));
        Assert.False(PowerLogLine.IsServerComplete("D 12:52:39.7497715 GameState.DebugPrintPower() - TAG_CHANGE Entity=16 tag=STATE value=RUNNING "));
        Assert.Equal("GT_BATTLEGROUNDS", PowerLogLine.GameType("D 17:45:37.9516013 GameState.DebugPrintGame() - GameType=GT_BATTLEGROUNDS"));
        Assert.Equal(254477, PowerLogLine.BuildNumber("D 17:45:37.9516013 GameState.DebugPrintGame() - BuildNumber=254477"));
        Assert.Null(PowerLogLine.GameType("D 17:45:37.9516013 GameState.DebugPrintGame() - FormatType=FT_WILD"));
        Assert.True(PowerLogLine.IsBattlegrounds("GT_BATTLEGROUNDS_FRIENDLY"));
        Assert.False(PowerLogLine.IsBattlegrounds("GT_RANKED"));
    }

    [Fact]
    public void ParsesTimeOfDay()
    {
        Assert.Equal(new TimeSpan(0, 12, 34, 51) + TimeSpan.FromTicks(2930731), PowerLogLine.TimeOfDay("D 12:34:51.2930731 GameState.DebugPrintPower() - CREATE_GAME"));
        Assert.Equal("12:34:51.2930731", PowerLogLine.TimeText("D 12:34:51.2930731 GameState.DebugPrintPower() - CREATE_GAME"));
        Assert.Null(PowerLogLine.TimeOfDay("garbage"));
    }
}

public class SessionClockTests
{
    [Fact]
    public void ParsesFolderNameAndRollsOverMidnight()
    {
        var start = SessionClock.ParseSessionName("Hearthstone_2026_11_14_22_33_10");
        Assert.Equal(new DateTime(2026, 11, 14, 22, 33, 10), start);
        Assert.Null(SessionClock.ParseSessionName("Other_2026"));
        var clock = new SessionClock(start!.Value);
        Assert.Equal(new DateTime(2026, 11, 14, 23, 58, 9), clock.ToDateTime(new TimeSpan(23, 58, 9)));
        Assert.Equal(new DateTime(2026, 11, 15, 0, 5, 0), clock.ToDateTime(new TimeSpan(0, 5, 0)));
        Assert.Equal(new DateTime(2026, 11, 15, 0, 4, 59), clock.ToDateTime(new TimeSpan(0, 4, 59))); // 少し戻るのは同じ日
    }

    [Fact]
    public void FirstLineAfterMidnightOfAStartBeforeMidnight()
    {
        var clock = new SessionClock(new DateTime(2026, 11, 14, 23, 59, 50));
        Assert.Equal(new DateTime(2026, 11, 15, 0, 0, 20), clock.ToDateTime(new TimeSpan(0, 0, 20)));
    }
}

public class GameNetLogTests
{
    private static readonly DateTime Start = new(2026, 11, 14, 16, 51, 12);

    [Fact]
    public void ParsesGotoGameServeAndFindsTheGame()
    {
        var entries = GameNetLog.Parse(new[]
        {
            "I 16:52:08.5752382 Network.GotoGameServe() - gameConnectionDisconnected True, IsRestoringGameState: False",
            TestLogs.NetLine("16:52:08.5752382", 6350, false),
            TestLogs.NetLine("17:21:02.2421219", 12688, false),
            TestLogs.NetLine("18:05:51.0000000", 12688, true),
        }, Start);
        Assert.Equal(3, entries.Count);
        Assert.Equal(new GameServerEntry(new DateTime(2026, 11, 14, 16, 52, 8).AddTicks(5752382), 6350, false), entries[0]);

        Assert.Equal(6350, GameNetLog.FindFor(entries, new DateTime(2026, 11, 14, 16, 52, 14))?.GameHandle);
        Assert.Equal(12688, GameNetLog.FindFor(entries, new DateTime(2026, 11, 14, 17, 21, 13))?.GameHandle);
        var reconnect = GameNetLog.FindFor(entries, new DateTime(2026, 11, 14, 18, 5, 57));
        Assert.True(reconnect?.Reconnecting);
        Assert.Null(GameNetLog.FindFor(entries, new DateTime(2026, 11, 14, 16, 50, 0)));   // 接続より前
        Assert.Null(GameNetLog.FindFor(entries, new DateTime(2026, 11, 14, 16, 59, 0)));   // 5 分より前の接続しか無い
    }
}
