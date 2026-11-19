using System.Text;
using HsbgKifu.Watch;
using Xunit;

namespace HsbgKifu.Tests;

public class LogTailerTests : IDisposable
{
    private readonly TempDir _tmp = new();
    public void Dispose() => _tmp.Dispose();

    private static void Append(string path, string text)
    {
        // Hearthstone と同じく、読み手が開いたままのファイルに追記する
        using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        var b = Encoding.UTF8.GetBytes(text);
        fs.Write(b, 0, b.Length);
    }

    [Fact]
    public void ReadsOnlyCompleteLinesAndContinuesAfterAppend()
    {
        var path = Path.Combine(_tmp.Path, "Power.log");
        File.WriteAllText(path, "a\nb");
        using var t = new LogTailer(path);
        Assert.Equal(new[] { "a" }, t.ReadAvailable().Select(l => l.Text));
        Assert.Empty(t.ReadAvailable());
        Append(path, "c\nd\n");
        Assert.Equal(new[] { new LogLine("bc", 2), new LogLine("d", 5) }, t.ReadAvailable());
    }

    [Fact]
    public void KeepsReadingAfterTheFileIsRenamed()
    {
        var path = Path.Combine(_tmp.Path, "Power.log");
        File.WriteAllText(path, "a\n");
        using var t = new LogTailer(path);
        Assert.Single(t.ReadAvailable());
        Append(path, "b\n");
        File.Move(path, Path.Combine(_tmp.Path, "Power_old.log")); // 開いたままでも名前を変えられる（共有モードに Delete を入れている）
        Assert.False(File.Exists(path));
        Assert.Equal(new[] { "b" }, t.ReadAvailable().Select(l => l.Text));
    }

    [Fact]
    public void DetectsTruncation()
    {
        var path = Path.Combine(_tmp.Path, "Power.log");
        File.WriteAllText(path, "aaaa\nbbbb\n");
        using var t = new LogTailer(path);
        Assert.Equal(2, t.ReadAvailable().Count);
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) fs.SetLength(2);
        Assert.Empty(t.ReadAvailable());
        Assert.True(t.Truncated);
    }
}

public class KifuWatcherTests : IDisposable
{
    private readonly TempDir _tmp = new();
    private DateTime _now = new(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);
    private bool _hsRunning = true;

    public void Dispose() => _tmp.Dispose();

    private string Logs => Path.Combine(_tmp.Path, "Logs");
    private string Slices => Path.Combine(_tmp.Path, "out", ".slices");

    private KifuWatcher NewWatcher() => new(new KifuWatcherOptions
    {
        LogsDirectory = Logs,
        SlicesRoot = Slices,
        UtcNow = () => _now,
        IsHearthstoneRunning = () => _hsRunning,
    });

    private string Session(string name) => _tmp.Sub("Logs", name);

    private static void Append(string path, IEnumerable<string> lines)
    {
        using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        var b = Encoding.UTF8.GetBytes(TestLogs.Join(lines));
        fs.Write(b, 0, b.Length);
    }

    private List<SliceMeta> Metas() => new SliceStore(Slices).All.OrderBy(m => m.StartTime).ToList();

    [Fact]
    public void FollowsLivePowerLogAcrossRenameAndNewSession()
    {
        var s1 = Session("Hearthstone_2026_11_20_10_00_00");
        File.WriteAllText(Path.Combine(s1, "GameNetLogger.log"), TestLogs.NetLine("10:04:55.0000000", 111, false) + "\n");
        var power1 = Path.Combine(s1, "Power.log");
        var g1 = TestLogs.OneGame("10:05");
        Append(power1, g1.Take(5)); // ゲームの途中まで
        var w = NewWatcher();
        w.Tick();
        Assert.Equal("Hearthstone_2026_11_20_10_00_00", w.CurrentSession);
        Assert.DoesNotContain(Metas(), m => m.Status != SliceMeta.StatusRecording); // GameType 待ち or 書いている途中

        Append(power1, g1.Skip(5)); // 残り（画面側の COMPLETE まで）
        w.Tick();
        var m1 = Assert.Single(Metas());
        Assert.Equal(SliceMeta.StatusComplete, m1.Status);
        Assert.Equal(111, m1.GameHandle);

        // Hearthstone を終了: Power.log → Power_old.log
        File.Move(power1, Path.Combine(s1, "Power_old.log"));
        w.Tick();
        Assert.Null(w.CurrentSession);

        // 起動し直した: 新しいフォルダ
        var s2 = Session("Hearthstone_2026_11_20_12_00_00");
        File.WriteAllText(Path.Combine(s2, "GameNetLogger.log"), TestLogs.NetLine("12:04:55.0000000", 222, false) + "\n");
        Append(Path.Combine(s2, "Power.log"), TestLogs.OneGame("12:05"));
        w.Tick();
        Assert.Equal("Hearthstone_2026_11_20_12_00_00", w.CurrentSession);
        Assert.Equal(new long?[] { 111, 222 }, Metas().Select(m => m.GameHandle));
        Assert.All(Metas(), m => Assert.Equal(SliceMeta.StatusComplete, m.Status));
    }

    [Fact]
    public async Task CatchesUpOldSessionsAtStartAndDoesNotDuplicate()
    {
        var old = Session("Hearthstone_2026_11_19_10_00_00");
        File.WriteAllText(Path.Combine(old, "Power_old.log"), TestLogs.Join(TestLogs.OneGame("10:05").Concat(TestLogs.OneGame("10:30"))));
        var live = Session("Hearthstone_2026_11_20_10_00_00");
        Append(Path.Combine(live, "Power.log"), TestLogs.OneGame("10:05"));

        var w = NewWatcher();
        var cts = new CancellationTokenSource();
        var run = w.RunAsync(cts.Token); // 起動時の回収 + 1 回目の Tick
        SpinWait.SpinUntil(() => Metas().Count(m => m.Status == SliceMeta.StatusComplete) == 3, TimeSpan.FromSeconds(10));
        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(3, Metas().Count);

        // もう一度起動しても増えない
        var w2 = NewWatcher();
        w2.Tick();
        Assert.Equal(3, Metas().Count);
    }

    [Fact]
    public async Task WaitsForPowerLogInANewSessionFolder()
    {
        // Hearthstone の起動直後: フォルダはあるが Power.log はまだ無い
        var s = Session("Hearthstone_2026_11_20_10_00_00");
        var w = NewWatcher();
        var cts = new CancellationTokenSource();
        var run = w.RunAsync(cts.Token);
        await Task.Delay(100);
        Append(Path.Combine(s, "Power.log"), TestLogs.OneGame("10:05"));
        SpinWait.SpinUntil(() => Metas().Count(m => m.Status == SliceMeta.StatusComplete) == 1, TimeSpan.FromSeconds(10));
        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(SliceMeta.StatusComplete, Assert.Single(Metas()).Status);
    }

    [Fact]
    public void GameIsClosedAsIncompleteWhenHearthstoneIsGone()
    {
        var s = Session("Hearthstone_2026_11_20_10_00_00");
        Append(Path.Combine(s, "Power.log"), TestLogs.OneGame("10:05", serverComplete: false));
        var w = NewWatcher();
        w.Tick();
        Assert.Equal(SliceMeta.StatusRecording, Assert.Single(Metas()).Status);

        _hsRunning = false; // 落ちた（Power.log は名前が変わらないまま残る）
        _now += TimeSpan.FromSeconds(5);
        w.Tick();
        Assert.Equal(SliceMeta.StatusRecording, Assert.Single(Metas()).Status); // まだ待つ
        _now += TimeSpan.FromSeconds(20);
        w.Tick();
        Assert.Equal(SliceMeta.StatusIncomplete, Assert.Single(Metas()).Status);
    }

    [Fact]
    public void ServerCompleteOnlyIsClosedAfterIdle()
    {
        var s = Session("Hearthstone_2026_11_20_10_00_00");
        Append(Path.Combine(s, "Power.log"), TestLogs.OneGame("10:05", clientComplete: false));
        var w = NewWatcher();
        w.Tick();
        _now += TimeSpan.FromSeconds(30);
        w.Tick();
        Assert.Equal(SliceMeta.StatusRecording, Assert.Single(Metas()).Status);
        _now += TimeSpan.FromSeconds(31);
        w.Tick();
        Assert.Equal(SliceMeta.StatusComplete, Assert.Single(Metas()).Status);
    }

    [Fact]
    public void SlicesBackedUpGzipSessions()
    {
        // M0 の退避先の形: Hearthstone_*\Power_old.log.gz + GameNetLogger.log.gz
        var dir = _tmp.Sub("backup", "Hearthstone_2026_11_10_23_28_14");
        TestLogs.WriteGz(Path.Combine(dir, "Power_old.log.gz"), TestLogs.Join(TestLogs.OneGame("23:30").Concat(TestLogs.OneGame("23:55"))));
        TestLogs.WriteGz(Path.Combine(dir, "GameNetLogger.log.gz"), TestLogs.NetLine("23:29:55.0000000", 7, false) + "\n");
        var store = new SliceStore(Slices);
        Assert.Equal(2, BatchSlicer.SliceAll(Path.Combine(_tmp.Path, "backup"), store, Diagnostics.NullKifuLog.Instance));
        var metas = Metas();
        Assert.Equal("Power_old.log.gz", metas[0].SourceFile);
        Assert.Equal(7, metas[0].GameHandle);
        Assert.Null(metas[1].GameHandle); // 接続から 5 分以上たっている（ゲーム番号の行が無い）
        Assert.Equal(0, BatchSlicer.SliceAll(Path.Combine(_tmp.Path, "backup"), new SliceStore(Slices), Diagnostics.NullKifuLog.Instance));
    }
}
