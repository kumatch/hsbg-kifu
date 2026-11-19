using System.Text.RegularExpressions;

namespace HsbgKifu.Watch;

/// <summary>サーバー側のゲーム番号。再接続でも変わらない（docs/research.md §4・§6）。</summary>
public readonly record struct GameServerEntry(DateTime Time, long GameHandle, bool Reconnecting);

/// <summary>
/// GameNetLogger.log の <c>Network.GotoGameServe() - address=..., game=6350, client=..., spectateKey=... reconnecting=False</c> を読む。
/// 使うのはゲーム番号と再接続かどうかだけ。接続先アドレスと spectateKey は読み捨てる（記録に書かない）。
/// </summary>
public static class GameNetLog
{
    private static readonly Regex GotoGameServe = new(@"GotoGameServe\(\) - .*\bgame=(\d+).*\breconnecting=(True|False)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static List<GameServerEntry> Parse(IEnumerable<string> lines, DateTime sessionStart)
    {
        var clock = new SessionClock(sessionStart);
        var result = new List<GameServerEntry>();
        foreach (var line in lines)
        {
            var tod = PowerLogLine.TimeOfDay(line);
            if (tod == null) continue;
            var time = clock.ToDateTime(tod.Value);
            var m = GotoGameServe.Match(line);
            if (!m.Success) continue;
            result.Add(new GameServerEntry(time, long.Parse(m.Groups[1].Value), m.Groups[2].Value == "True"));
        }
        return result;
    }

    /// <summary>
    /// ゲーム開始（CREATE_GAME）の直前に接続したゲームサーバー（実測では 6 秒前）。後ろへ 5 秒の誤差を許す。
    /// 5 分より前の接続しか無ければ、別のゲームのものなので null。
    /// </summary>
    public static GameServerEntry? FindFor(IReadOnlyList<GameServerEntry> entries, DateTime gameStart)
    {
        GameServerEntry? found = null;
        foreach (var e in entries)
        {
            if (e.Time <= gameStart + TimeSpan.FromSeconds(5)) found = e;
            else break;
        }
        return found is { } f && f.Time >= gameStart - TimeSpan.FromMinutes(5) ? f : null;
    }
}
