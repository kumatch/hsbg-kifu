using System.Text.Json;
using System.Text.RegularExpressions;
using HsbgKifu.Capture;
using HsbgKifu.Cards;
using HsbgKifu.Diagnostics;
using HsbgKifu.Output;
using HsbgKifu.Parse;
using HsbgKifu.Watch;

namespace HsbgKifu.DevTools;

/// <summary>
/// kifu-dev verify-json --slices DIR|plugin --plugin DIR [--out DIR] [--show N]
/// 中継ログから記録の JSON を作り（KifuBuilder）、同じゲームのプラグインの JSON と全項目を比べる（TASKS.md M3）。
/// 取り方が違う・取れなくなった項目（Ignored）は比べない。出力はカード ID と数字だけ。
/// </summary>
internal static class VerifyJson
{
    private static readonly Regex Index = new(@"\[\d+\]", RegexOptions.Compiled);

    /// <summary>比べない項目（docs/research.md §M3）。</summary>
    private static readonly string[] Ignored =
    {
        "generator.", "game.game_id", "game.started_at", "game.ended_at", "game.duration_sec", "game.region", "game.minion_types",
        "result.mmr_before", "result.mmr_after", "result.mmr_delta", "notes", "turns[*].combat.bobs_buddy",
    };

    public static int Run(string slicesDir, string pluginDir, string? outDir, int show)
    {
        var cards = CardDb.Load();
        Console.WriteLine($"CardDefs: {cards.Count} 枚（build {cards.Build}）");
        var plugin = LoadPlugin(pluginDir);
        var kinds = new Dictionary<string, int>();
        var examples = new Dictionary<string, List<string>>();
        int games = 0, matched = 0, withDiff = 0, fields = 0, diffs = 0, skippedOld = 0;
        var actionFields = new[] { "buys", "sells", "rerolls", "tier_ups", "hero_power_uses", "spells_played", "dark_gifts" };
        var truthStats = actionFields.ToDictionary(f => f, _ => new int[3]); // [比べたターン, kifu が違う, plugin が違う]
        var log = new TextKifuLog(TextWriter.Null);
        foreach (var (gz, meta) in VerifyParse.Inputs(slicesDir, pluginDir).OrderBy(x => x.Meta.StartTime))
        {
            games++;
            BuildResult result;
            try { result = KifuBuilder.Build(gz, meta, cards, log: log); }
            catch (Exception e) { Console.WriteLine($"{Path.GetFileName(gz)[..15]}: 例外 {e.GetType().Name}: {e.Message}"); continue; }
            var name = Path.GetFileName(gz)[..15];
            if (result.Log == null) { Console.WriteLine($"{name}: 記録なし（ターン 1 の前に終わった）"); continue; }
            if (outDir != null) KifuOutput.Write(result, outDir, gz, new KifuOutput.Options(WritePowerLog: false));
            var pj = plugin.Where(p => Math.Abs((p.Start - meta.StartTime).TotalSeconds) < 120)
                .OrderBy(p => Math.Abs((p.Start - meta.StartTime).TotalSeconds)).FirstOrDefault();
            if (pj == null) { Console.WriteLine($"{name}: プラグインの記録なし"); continue; }
            matched++;
            // 正解（PLAY ブロック）と、kifu・プラグインの行動の回数を比べる
            var truth = new ActionTruth();
            SliceReplay.Run(gz, meta, new PowerGameListener[] { truth });
            using (var pdoc = JsonDocument.Parse(File.ReadAllText(pj.Path)))
            {
                var pturns = pdoc.RootElement.GetProperty("turns").EnumerateArray().ToDictionary(x => x.GetProperty("turn").GetInt32());
                foreach (var kt in result.Log.Turns)
                {
                    if (kt.Actions == null || !pturns.TryGetValue(kt.Turn, out var pt) || !pt.TryGetProperty("actions", out var pa) || pa.ValueKind != JsonValueKind.Object) continue;
                    var kj = JsonDocument.Parse(GameLogWriter.Serialize(result.Log)).RootElement.GetProperty("turns").EnumerateArray().First(x => x.GetProperty("turn").GetInt32() == kt.Turn).GetProperty("actions");
                    foreach (var f in actionFields)
                    {
                        var tv = truth.Get(kt.Turn, f);
                        var s = truthStats[f];
                        s[0]++;
                        if (kj.TryGetProperty(f, out var kv) && kv.ValueKind == JsonValueKind.Number && kv.GetInt32() != tv) s[1]++;
                        if (pa.TryGetProperty(f, out var pv) && pv.ValueKind == JsonValueKind.Number && pv.GetInt32() != tv) s[2]++;
                    }
                }
            }
            var ours = Flatten(JsonDocument.Parse(GameLogWriter.Serialize(result.Log)).RootElement);
            var theirs = Flatten(JsonDocument.Parse(File.ReadAllText(pj.Path)).RootElement);
            var gameDiffs = 0;
            var pluginKinds = new HashSet<string>(theirs.Keys.Select(k => Index.Replace(k, "[*]")), StringComparer.Ordinal);
            foreach (var key in ours.Keys.Union(theirs.Keys).OrderBy(k => k, StringComparer.Ordinal))
            {
                var kind = Index.Replace(key, "[*]");
                if (Ignored.Any(i => kind.StartsWith(i, StringComparison.Ordinal))) continue;
                // プラグインの記録に無い種類の項目（古い版のプラグインがまだ書いていなかったもの）は比べない
                if (!theirs.ContainsKey(key) && !pluginKinds.Contains(kind)) { skippedOld++; continue; }
                fields++;
                ours.TryGetValue(key, out var a);
                theirs.TryGetValue(key, out var b);
                if (a == b) continue;
                diffs++;
                gameDiffs++;
                kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
                if (!examples.TryGetValue(kind, out var ex)) examples[kind] = ex = new List<string>();
                if (ex.Count < show) ex.Add($"{name} {key}: kifu={a ?? "(無し)"} plugin={b ?? "(無し)"}");
            }
            if (gameDiffs > 0) withDiff++;
            Console.WriteLine($"{name}: {(gameDiffs == 0 ? "一致" : $"違い {gameDiffs} 件")}");
        }
        Console.WriteLine($"--- {games} ゲーム（対応 {matched}）、違いのあるゲーム {withDiff}、項目 {fields} 件中 {diffs} 件の違い（プラグインの記録に無い項目 {skippedOld} 件は比べていない）");
        Console.WriteLine("  行動の回数と正解（自分の PLAY ブロック）の比較: 項目 / 比べたターン / kifu が違う / プラグインが違う");
        foreach (var f in actionFields) Console.WriteLine($"    {f,-16} {truthStats[f][0],5} {truthStats[f][1],5} {truthStats[f][2],5}");
        foreach (var (k, v) in kinds.OrderByDescending(kv => kv.Value))
        {
            Console.WriteLine($"  {v,6}  {k}");
            foreach (var e in examples[k]) Console.WriteLine($"          {e}");
        }
        return 0;
    }

    private sealed record PluginJson(DateTimeOffset Start, string Path);

    private static List<PluginJson> LoadPlugin(string dir)
    {
        var result = new List<PluginJson>();
        foreach (var path in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories))
        {
            if (path.Contains(System.IO.Path.DirectorySeparatorChar + ".")) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("game", out var g) && g.TryGetProperty("started_at", out var st))
                    result.Add(new PluginJson(st.GetDateTimeOffset(), path));
            }
            catch (JsonException) { }
        }
        return result;
    }

    /// <summary>JSON を「パス → 値の文字列」に（配列は [i]）。空の配列・オブジェクトも 1 項目として残す。</summary>
    private static Dictionary<string, string?> Flatten(JsonElement root)
    {
        var d = new Dictionary<string, string?>(StringComparer.Ordinal);
        void Walk(JsonElement e, string path)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    var any = false;
                    foreach (var p in e.EnumerateObject()) { any = true; Walk(p.Value, path.Length == 0 ? p.Name : path + "." + p.Name); }
                    if (!any) d[path] = "{}";
                    break;
                case JsonValueKind.Array:
                    var i = 0;
                    foreach (var x in e.EnumerateArray()) Walk(x, $"{path}[{i++}]");
                    if (i == 0) d[path] = "[]";
                    break;
                case JsonValueKind.Null: d[path] = null; break;
                default: d[path] = e.GetRawText(); break;
            }
        }
        Walk(root, "");
        return d;
    }
}
