using System.IO.Compression;
using System.Text;
using HsbgKifu.Watch;

namespace HsbgKifu.Tests;

/// <summary>テスト用の短い Power.log / GameNetLogger.log を組み立てる（実際のログの形に合わせる。BattleTag は入れない）。</summary>
internal static class TestLogs
{
    public static string Gs(string time, string body) => $"D {time} GameState.DebugPrintPower() - {body}";
    public static string Ptl(string time, string body) => $"D {time} PowerTaskList.DebugPrintPower() - {body}";
    public static string Game(string time, string body) => $"D {time} GameState.DebugPrintGame() - {body}";
    public static string PowerList(string time) => $"D {time} GameState.DebugPrintPowerList() - Count=44";

    /// <summary>
    /// 1 ゲーム分の行。<paramref name="clientComplete"/> が false なら画面側の COMPLETE を書かない。
    /// <paramref name="serverComplete"/> が false なら終わりの行を書かない（落ちた）。
    /// </summary>
    public static List<string> OneGame(string hhmm, string gameType = "GT_BATTLEGROUNDS", bool serverComplete = true, bool clientComplete = true, int filler = 3)
    {
        var t = $"{hhmm}:00.0000000";
        var lines = new List<string>
        {
            PowerList(t),
            Gs(t, "CREATE_GAME"),
            Gs(t, "    GameEntity EntityID=1"),
            Gs(t, "        tag=CARDTYPE value=GAME"),
            Gs(t, "TAG_CHANGE Entity=1 tag=STATE value=RUNNING "),
            Game(t, "BuildNumber=254477"),
            Game(t, $"GameType={gameType}"),
            Game(t, "FormatType=FT_WILD"),
        };
        for (var i = 0; i < filler; i++)
        {
            lines.Add(Gs($"{hhmm}:1{i}.0000000", $"TAG_CHANGE Entity=GameEntity tag=TURN value={i + 1} "));
            lines.Add(Ptl($"{hhmm}:1{i}.5000000", $"    TAG_CHANGE Entity=GameEntity tag=TURN value={i + 1} "));
        }
        if (!serverComplete) return lines;
        lines.Add(Gs($"{hhmm}:30.0000000", "TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE "));
        lines.Add(Ptl($"{hhmm}:40.0000000", "    TAG_CHANGE Entity=GameEntity tag=TURN value=99 "));
        if (!clientComplete) return lines;
        lines.Add(Ptl($"{hhmm}:50.0000000", "    TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE "));
        lines.Add($"D {hhmm}:50.0000000 PowerTaskList.DebugDump() - Block End=(null)");
        lines.Add($"D {hhmm}:55.0000000 PowerProcessor.EndCurrentTaskList() - m_currentTaskList=5011");
        return lines;
    }

    public static string NetLine(string time, long game, bool reconnecting) =>
        $"I {time} Network.GotoGameServe() - address= 192.0.2.1:1119, game={game}, client=123, spectateKey=abc reconnecting={(reconnecting ? "True" : "False")}";

    public static List<LogLine> ToLogLines(IEnumerable<string> lines)
    {
        var result = new List<LogLine>();
        long offset = 0;
        foreach (var l in lines)
        {
            result.Add(new LogLine(l, offset));
            offset += Encoding.UTF8.GetByteCount(l) + 1;
        }
        return result;
    }

    public static string Join(IEnumerable<string> lines) => string.Join("\n", lines) + "\n";

    public static List<string> ReadGz(string path)
    {
        using var s = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var r = new StreamReader(s);
        var lines = new List<string>();
        while (r.ReadLine() is { } l) lines.Add(l);
        return lines;
    }

    public static void WriteGz(string path, string text)
    {
        using var s = new GZipStream(File.Create(path), CompressionLevel.Fastest);
        var bytes = Encoding.UTF8.GetBytes(text);
        s.Write(bytes, 0, bytes.Length);
    }
}

/// <summary>テストごとの一時フォルダ。</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hsbg-kifu-tests", Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string Sub(params string[] parts)
    {
        var p = System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());
        Directory.CreateDirectory(p);
        return p;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
