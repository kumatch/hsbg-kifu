namespace HsbgKifu.Config;

/// <summary>
/// Hearthstone のログの設定（<c>%LOCALAPPDATA%\Blizzard\Hearthstone\log.config</c>）で Power.log が出るかを確かめる。読むだけで、書き換えない
/// （設定は HDT が書く。docs/research.md §2）。hsbg-kifu に要るのは <c>[Power]</c> の LogLevel=1・FilePrinting=True・Verbose=True。
/// Hearthstone は起動時に読むので、直したら Hearthstone の再起動が要る。
/// </summary>
public static class LogConfigCheck
{
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Blizzard", "Hearthstone", "log.config");

    /// <summary>問題の一覧（無ければ空）。</summary>
    public static List<string> Problems(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path))
            return new List<string> { $"Hearthstone のログの設定（{path}）がありません。HDT を起動すると作られます" };
        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new List<string> { $"Hearthstone のログの設定を読めません（{e.Message}）" };
        }
        return ProblemsIn(text);
    }

    public static List<string> ProblemsIn(string text)
    {
        var power = Section(text, "Power");
        if (power == null) return new List<string> { "Hearthstone のログの設定に [Power] がありません（Power.log が出ません）" };
        var problems = new List<string>();
        void Expect(string key, string value)
        {
            if (!power.TryGetValue(key, out var v) || !string.Equals(v, value, StringComparison.OrdinalIgnoreCase))
                problems.Add($"Hearthstone のログの設定の [Power] {key} が {value} ではありません（{(v ?? "無し")}）");
        }
        Expect("LogLevel", "1");
        Expect("FilePrinting", "True");
        Expect("Verbose", "True");
        return problems;
    }

    private static Dictionary<string, string>? Section(string text, string name)
    {
        Dictionary<string, string>? current = null, found = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                current = line[1..^1].Trim().Equals(name, StringComparison.OrdinalIgnoreCase) ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : null;
                if (current != null) found = current;
                continue;
            }
            var eq = line.IndexOf('=');
            if (current != null && eq > 0) current[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        return found;
    }
}
