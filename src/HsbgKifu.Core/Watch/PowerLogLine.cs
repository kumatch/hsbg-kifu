namespace HsbgKifu.Watch;

/// <summary>
/// Power.log の行の見分け。行の形: <c>D 12:34:51.2930731 GameState.DebugPrintPower() - CREATE_GAME</c>
/// （レベル 1 文字、時刻 16 文字、出どころ、" - "、本文）。docs/research.md §3。
/// </summary>
public static class PowerLogLine
{
    private const string GameStatePower = "GameState.DebugPrintPower() - ";
    private const string TaskListPower = "PowerTaskList.DebugPrintPower() - ";
    private const string GameStateGame = "GameState.DebugPrintGame() - ";
    private const string GameStatePowerList = "GameState.DebugPrintPowerList() - ";
    private const string GameComplete = "TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE";

    /// <summary>行頭の時刻（"HH:MM:SS.fffffff"）。形が違えば null。</summary>
    public static TimeSpan? TimeOfDay(string line)
    {
        if (line.Length < 18 || line[1] != ' ' || line[4] != ':' || line[7] != ':' || line[10] != '.') return null;
        if (!TwoDigits(line, 2, out var h) || !TwoDigits(line, 5, out var m) || !TwoDigits(line, 8, out var s)) return null;
        long frac = 0;
        for (var i = 11; i < 18; i++)
        {
            var c = line[i];
            if (c < '0' || c > '9') return null;
            frac = frac * 10 + (c - '0');
        }
        return new TimeSpan(h, m, s) + TimeSpan.FromTicks(frac);
    }

    /// <summary>行頭の時刻の文字列（"HH:MM:SS.fffffff"）。ゲームの見分けに使う。</summary>
    public static string TimeText(string line) => line.Length >= 18 ? line.Substring(2, 16) : "";

    public static bool IsCreateGame(string line) => Body(line, GameStatePower).Trim() is "CREATE_GAME";

    public static bool IsPowerList(string line) => Source(line).StartsWith(GameStatePowerList, StringComparison.Ordinal);

    /// <summary>サーバー側（GameState）でゲームが終わった。</summary>
    public static bool IsServerComplete(string line) => Body(line, GameStatePower).TrimStart().StartsWith(GameComplete, StringComparison.Ordinal);

    /// <summary>画面側（PowerTaskList）でゲームが終わった。サーバー側の数十秒後に来る。この後は数行しか続かない。</summary>
    public static bool IsClientComplete(string line) => Body(line, TaskListPower).TrimStart().StartsWith(GameComplete, StringComparison.Ordinal);

    /// <summary><c>DebugPrintGame() - GameType=GT_BATTLEGROUNDS</c> の値。</summary>
    public static string? GameType(string line) => GameField(line, "GameType=");

    /// <summary><c>DebugPrintGame() - BuildNumber=254477</c> の値。</summary>
    public static int? BuildNumber(string line) => int.TryParse(GameField(line, "BuildNumber="), out var n) ? n : null;

    public static bool IsBattlegrounds(string? gameType) => gameType != null && gameType.StartsWith("GT_BATTLEGROUNDS", StringComparison.Ordinal);

    private static string? GameField(string line, string key)
    {
        var body = Body(line, GameStateGame);
        return body.StartsWith(key, StringComparison.Ordinal) ? body[key.Length..].ToString().Trim() : null;
    }

    private static ReadOnlySpan<char> Source(string line) => line.Length > 19 ? line.AsSpan(19) : ReadOnlySpan<char>.Empty;

    private static ReadOnlySpan<char> Body(string line, string source)
    {
        var s = Source(line);
        return s.StartsWith(source, StringComparison.Ordinal) ? s[source.Length..] : ReadOnlySpan<char>.Empty;
    }

    private static bool TwoDigits(string s, int i, out int value)
    {
        value = 0;
        char a = s[i], b = s[i + 1];
        if (a < '0' || a > '9' || b < '0' || b > '9') return false;
        value = (a - '0') * 10 + (b - '0');
        return true;
    }
}
