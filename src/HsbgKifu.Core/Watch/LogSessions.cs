using System.IO.Compression;
using System.Xml;

namespace HsbgKifu.Watch;

/// <summary>
/// Hearthstone の起動 1 回分のログフォルダ（<c>Logs\Hearthstone_YYYY_MM_DD_HH_MM_SS\</c>）。
/// M0 の退避先（<c>HSBG-PowerLogs\Hearthstone_*\Power_old.log.gz</c>）も同じ形で扱う。
/// </summary>
public sealed record LogSession(string Name, string Directory, DateTime Start)
{
    /// <summary>
    /// 読む Power.log。終わった起動分は Power_old.log（終了時に名前が変わる。docs/research.md §2）。
    /// 両方あれば Power_old.log → Power.log の順。退避した .gz も含む。
    /// </summary>
    public IReadOnlyList<string> PowerLogFiles() => Find("Power");

    public IReadOnlyList<string> GameNetLogFiles() => Find("GameNetLogger");

    /// <summary>起動中の Power.log（名前が変わる前）。無ければ null。</summary>
    public string? LivePowerLog()
    {
        var p = Path.Combine(Directory, "Power.log");
        return File.Exists(p) ? p : null;
    }

    public List<GameServerEntry> ReadGameServers()
    {
        var lines = new List<string>();
        foreach (var f in GameNetLogFiles())
        {
            try { lines.AddRange(LogFiles.ReadAllLines(f)); }
            catch (IOException) { /* 読めなければゲーム番号なしで続ける */ }
        }
        var entries = GameNetLog.Parse(lines, Start);
        entries.Sort((a, b) => a.Time.CompareTo(b.Time));
        return entries;
    }

    private IReadOnlyList<string> Find(string stem)
    {
        var result = new List<string>();
        foreach (var name in new[] { $"{stem}_old.log", $"{stem}.log" })
        {
            var plain = Path.Combine(Directory, name);
            if (File.Exists(plain)) result.Add(plain);
            else if (File.Exists(plain + ".gz")) result.Add(plain + ".gz");
        }
        return result;
    }

    public static IReadOnlyList<LogSession> List(string logsDirectory)
    {
        if (!System.IO.Directory.Exists(logsDirectory)) return Array.Empty<LogSession>();
        var result = new List<LogSession>();
        foreach (var dir in System.IO.Directory.GetDirectories(logsDirectory, "Hearthstone_*"))
        {
            var name = Path.GetFileName(dir);
            if (SessionClock.ParseSessionName(name) is { } start) result.Add(new LogSession(name, dir, start));
        }
        result.Sort((a, b) => a.Start.CompareTo(b.Start));
        return result;
    }
}

public static class LogFiles
{
    public const FileShare ShareWithHearthstone = FileShare.ReadWrite | FileShare.Delete;

    /// <summary>Hearthstone が書き込み中・名前を変える途中でも邪魔をしない共有モードで開く。.gz は展開しながら読む。</summary>
    public static Stream OpenRead(string path)
    {
        Stream fs = new FileStream(path, FileMode.Open, FileAccess.Read, ShareWithHearthstone, 1 << 16);
        return path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? new GZipStream(fs, CompressionMode.Decompress) : fs;
    }

    public static List<string> ReadAllLines(string path)
    {
        using var reader = new StreamReader(OpenRead(path));
        var lines = new List<string>();
        while (reader.ReadLine() is { } line) lines.Add(line);
        return lines;
    }

    /// <summary>
    /// Hearthstone のフォルダ: 指定 → HDT の config.xml の HearthstoneDirectory → 既定の場所。
    /// </summary>
    public static string FindHearthstoneDirectory(string? overridePath = null)
    {
        if (!string.IsNullOrEmpty(overridePath)) return overridePath;
        try
        {
            var cfg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HearthstoneDeckTracker", "config.xml");
            if (File.Exists(cfg))
            {
                var doc = new XmlDocument();
                using (var s = new FileStream(cfg, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    doc.Load(s);
                var dir = doc.SelectSingleNode("/Config/HearthstoneDirectory")?.InnerText;
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
            }
        }
        catch (Exception e) when (e is IOException or XmlException or UnauthorizedAccessException)
        {
            // 読めなければ既定の場所
        }
        return @"C:\Program Files (x86)\Hearthstone";
    }
}
