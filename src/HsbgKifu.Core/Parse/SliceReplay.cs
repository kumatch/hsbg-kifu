using HsbgKifu.Diagnostics;
using HsbgKifu.Watch;

namespace HsbgKifu.Parse;

/// <summary>中継ログ（<c>.power.log.gz</c>）を頭から読み、パーサに流す。M3 の「中継ログから記録を作り直す」の土台。</summary>
public static class SliceReplay
{
    /// <summary>
    /// <paramref name="gzPath"/> を読んで <paramref name="listeners"/> に出来事を知らせる。
    /// 行の日付は付帯情報のログフォルダ名（起動日時）から補う。
    /// </summary>
    public static PowerLogParser Run(string gzPath, SliceMeta meta, IEnumerable<PowerGameListener> listeners, IKifuLog? log = null)
    {
        var start = SessionClock.ParseSessionName(meta.Session) ?? meta.StartTime.LocalDateTime;
        var clock = new SessionClock(start);
        var parser = new PowerLogParser(listeners, log);
        using var reader = new StreamReader(LogFiles.OpenRead(gzPath));
        while (reader.ReadLine() is { } line)
        {
            var time = PowerLogLine.TimeOfDay(line) is { } tod ? clock.ToDateTime(tod) : parser.Game?.LastTime ?? start;
            parser.Feed(line, time);
        }
        parser.Finish();
        return parser;
    }
}
