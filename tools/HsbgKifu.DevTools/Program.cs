using System.Text.Json;
using System.Text.RegularExpressions;
using HearthDb.Enums;
using HsbgKifu.Diagnostics;
using HsbgKifu.Parse;
using HsbgKifu.Watch;

namespace HsbgKifu.DevTools;

/// <summary>
/// 開発用の道具（配布しない）。
/// <code>
/// kifu-dev verify-parse --slices DIR --plugin DIR [--verbose true]
///     中継ログを PowerLogParser で読み、出来事と状態から計算した値を、同じゲームのプラグインの記録（JSON）と比べる（TASKS.md M2）
/// </code>
/// 出力にはカード ID と数字だけを出す（BattleTag は出さない）。
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        var opts = new Dictionary<string, string>();
        for (var i = 1; i + 1 < args.Length; i += 2) opts[args[i].TrimStart('-')] = args[i + 1];
        switch (args.Length > 0 ? args[0] : "")
        {
            case "verify-parse": return VerifyParse.Run(opts["slices"], opts["plugin"], opts.GetValueOrDefault("verbose") == "true");
            case "verify-json": return VerifyJson.Run(opts["slices"], opts["plugin"], opts.GetValueOrDefault("out"), int.Parse(opts.GetValueOrDefault("show") ?? "3"));
            default:
                Console.Error.WriteLine("kifu-dev verify-parse --slices DIR|plugin --plugin DIR [--verbose true]");
                Console.Error.WriteLine("kifu-dev verify-json  --slices DIR|plugin --plugin DIR [--out DIR] [--show N]");
                return 2;
        }
    }
}

/// <summary>出来事と状態から、プラグインの記録と比べられる値を集める。</summary>
internal sealed class Facts : PowerGameListener
{
    public sealed class Turn
    {
        public int Number;
        public int? Health, Armor, TavernTier;
        public int DamageDealt, DamageTaken;
        public bool CombatStarted;
    }

    public readonly SortedDictionary<int, Turn> Turns = new();
    public string? HeroCardId;
    public int? Placement;
    public int TotalTurns;
    public int LocalPlayerId;
    public bool LocalFromMulligan, LocalFromDummyAgrees = true;
    public int TurnChanges, CombatStarts, Predamages;

    private Turn T(int n) => Turns.TryGetValue(n, out var t) ? t : Turns[n] = new Turn { Number = n };

    public override void OnLocalPlayerKnown(PowerGame game, int playerId)
    {
        LocalPlayerId = playerId;
        LocalFromMulligan = game.Choices.Count > 0 || true;
        var dummy = game.PlayerEntityOf(playerId)?.HasTag(GameTag.BACON_DUMMY_PLAYER) ?? false;
        if (dummy) LocalFromDummyAgrees = false;
    }

    public override void OnGameSetupDone(PowerGame game)
    {
        if (Debug) Console.WriteLine($"  setup: startedMidGame={game.StartedMidGame} turn={game.GameEntityTurn} local={game.LocalPlayerId} players={game.PlayerEntities.Count()} entities={game.Entities.Count}");
    }

    public override void OnTurnChanged(PowerGame game, int gameEntityTurn)
    {
        TurnChanges++;
        var hero = game.PlayerHero;
        if (hero == null) return;
        var n = game.TurnNumber;
        if (gameEntityTurn % 2 == 1) _inCombat = false;
        if (gameEntityTurn % 2 == 1)
        {
            // ショップの始まり（HDT の OnTurnStart(Player)）: 前の戦闘の結果を反映した HP / アーマー
            T(n).Health = hero.Health;
            T(n).Armor = hero.Armor;
        }
        else
        {
            // ショップの終わり（OnTurnStart(Opponent)）: そのターンのティア
            var tier = hero.GetTag(GameTag.PLAYER_TECH_LEVEL);
            if (tier == 0) tier = game.PlayerEntity?.GetTag(GameTag.PLAYER_TECH_LEVEL) ?? 0;
            T(n).TavernTier = tier == 0 ? 1 : tier;
        }
    }

    private bool _inCombat;

    public override void OnCombatSetupEnded(PowerGame game, int tag)
    {
        _inCombat = true;
        CombatStarts++;
        T(game.TurnNumber).CombatStarted = true;
    }

    // 戦闘のダメージ = 戦闘の結果として生じたダメージ（PREDAMAGE）。アイスブロックで防がれて HP が減らなくても数える（docs/research.md §3.1）。
    // ショップ中のダメージ（HP で払う購入・プレイ）は戦闘のダメージではないので、戦闘開始から次のショップの始まりまでだけ数える。
    // PREDAMAGE は 1 回の攻撃で「値 → 0 → 値 → 0」と 2 回立つので、2 回目（同じ Entity・同じ値で、間に META_DATA DAMAGE が無い）は数えない。
    // META_DATA DAMAGE（実際に入ったダメージ）は、防がれたかどうかを見分けるのにだけ使う
    private readonly Dictionary<int, int> _lastPredamage = new();
    private readonly HashSet<(int Turn, bool Taken)> _metaSeen = new();
    private readonly HashSet<(int Turn, bool Taken)> _predamageSeen = new();
    public int Prevented;
    public static bool Debug;

    public override void OnPredamage(PowerGame game, Entity entity, int value)
    {
        if (!entity.IsHero) return;
        Predamages++;
        if (!_inCombat) return;
        if (_lastPredamage.Remove(entity.Id, out var last) && last == value) return; // 2 回目
        _lastPredamage[entity.Id] = value;
        if (Side(game, entity) is not { } taken) return;
        var n = game.TurnNumber;
        _predamageSeen.Add((n, taken));
        if (taken) T(n).DamageTaken += value; else T(n).DamageDealt += value;
    }

    public override void OnDamage(PowerGame game, Entity entity, int value)
    {
        if (Debug && entity.IsHero)
            Console.WriteLine($"  dmg turn={game.GameEntityTurn} combat={_inCombat} entity={entity.Id} {entity.CardId} ctrl={entity.Controller} local={game.LocalPlayerId} opp={game.OpponentPlayerId} v={value}");
        if (!entity.IsHero || !_inCombat) return;
        _lastPredamage.Remove(entity.Id);
        if (Side(game, entity) is { } side) _metaSeen.Add((game.TurnNumber, side));
    }

    private static bool? Side(PowerGame game, Entity entity) =>
        game.LocalPlayerId is { } local && entity.IsControlledBy(local) ? true
        : game.OpponentPlayerId is { } opp && entity.IsControlledBy(opp) ? false : null;

    /// <summary>PREDAMAGE はあったのに META_DATA DAMAGE が無かった戦闘（アイスブロックで防がれた、または別の Entity に直接書かれた）の数。</summary>
    public void CountPrevented() => Prevented = _predamageSeen.Count(k => !_metaSeen.Contains(k));

    public override void OnGameEnded(PowerGame game)
    {
        var hero = game.PlayerHero;
        HeroCardId = hero?.CardId;
        Placement = hero?.GetTag(GameTag.PLAYER_LEADERBOARD_PLACE);
        TotalTurns = game.TurnNumber;
    }
}

internal static class VerifyParse
{
    private static readonly Regex Skin = new(@"_SKIN_[A-Z0-9]+$", RegexOptions.Compiled);

    /// <summary>
    /// 比べる入力: <paramref name="slicesDir"/> の中継ログ。"plugin" を渡すと、プラグインが残した power.log.gz（HDT の写し。GameState の行だけ）を使う。
    /// </summary>
    internal static IEnumerable<(string Gz, SliceMeta Meta)> Inputs(string slicesDir, string pluginDir)
    {
        if (slicesDir != "plugin")
        {
            foreach (var p in Directory.GetFiles(slicesDir, "*.slice.json", SearchOption.AllDirectories))
            {
                var m = JsonSerializer.Deserialize<SliceMeta>(File.ReadAllText(p))!;
                if (m.Status == SliceMeta.StatusComplete) yield return (p.Replace(".slice.json", ".power.log.gz"), m);
            }
            yield break;
        }
        foreach (var gz in Directory.GetFiles(pluginDir, "*.power.log.gz", SearchOption.AllDirectories).Order())
        {
            if (gz.Contains(Path.DirectorySeparatorChar + ".")) continue;
            var json = gz.Replace(".power.log.gz", ".json");
            if (!File.Exists(json)) continue;
            using var doc = JsonDocument.Parse(File.ReadAllText(json));
            var start = doc.RootElement.GetProperty("game").GetProperty("started_at").GetDateTimeOffset();
            // 日付を補うための仮のログフォルダ名（ゲーム開始の 1 分前に起動したことにする）
            var session = "Hearthstone_" + start.LocalDateTime.AddMinutes(-1).ToString("yyyy_MM_dd_HH_mm_ss");
            yield return (gz, new SliceMeta { Session = session, StartTime = start, Status = SliceMeta.StatusComplete });
        }
    }

    public static int Run(string slicesDir, string pluginDir, bool verbose)
    {
        var plugin = LoadPlugin(pluginDir);
        int games = 0, matched = 0, gamesWithDiff = 0, fieldDiffs = 0, fieldChecks = 0;
        long unresolved = 0, unknownTags = 0;
        var diffKinds = new Dictionary<string, int>();
        foreach (var (gz, meta) in Inputs(slicesDir, pluginDir).OrderBy(x => x.Meta.StartTime))
        {
            games++;
            var facts = new Facts();
            Facts.Debug = Environment.GetEnvironmentVariable("KIFU_DEBUG_GAME") is { } dg && Path.GetFileName(gz).StartsWith(dg);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            PowerLogParser parser;
            try { parser = SliceReplay.Run(gz, meta, new PowerGameListener[] { facts }, new TextKifuLog(Console.Out)); }
            catch (InvalidDataException e) { Console.WriteLine($"{Path.GetFileName(gz)}: 読めない（{e.Message}）"); continue; }
            facts.CountPrevented();
            unresolved += parser.UnresolvedNameCount;
            unknownTags += parser.UnknownTagCount;
            var name = Path.GetFileName(gz)[..15];
            var pj = plugin.Where(p => Math.Abs((p.Start - meta.StartTime).TotalSeconds) < 120).OrderBy(p => Math.Abs((p.Start - meta.StartTime).TotalSeconds)).FirstOrDefault();
            if (pj == null) { Console.WriteLine($"{name}: プラグインの記録なし"); continue; }
            matched++;
            var diffs = new List<string>();
            void Check(string field, object? ours, object? theirs)
            {
                fieldChecks++;
                if (Equals(ours?.ToString(), theirs?.ToString())) return;
                fieldDiffs++;
                var kind = Regex.Replace(field, @"T\d+\.", "T*.");
                diffKinds[kind] = diffKinds.GetValueOrDefault(kind) + 1;
                diffs.Add($"{field}: kifu={ours ?? "null"} plugin={theirs ?? "null"}");
            }
            Check("hero", facts.HeroCardId is { } h ? Skin.Replace(h, "") : null, pj.Hero is { } ph ? Skin.Replace(ph, "") : null);
            Check("placement", facts.Placement, pj.Placement);
            Check("total_turns", facts.TotalTurns, pj.TotalTurns);
            foreach (var pt in pj.Turns)
            {
                facts.Turns.TryGetValue(pt.Number, out var ft);
                Check($"T{pt.Number}.tavern_tier", ft?.TavernTier, pt.TavernTier);
                Check($"T{pt.Number}.health", ft?.Health, pt.Health);
                Check($"T{pt.Number}.armor", ft?.Armor, pt.Armor);
                if (pt.HasCombat)
                {
                    Check($"T{pt.Number}.damage_dealt", ft?.DamageDealt, pt.DamageDealt);
                    Check($"T{pt.Number}.damage_taken", ft?.DamageTaken, pt.DamageTaken);
                }
            }
            if (diffs.Count > 0) gamesWithDiff++;
            Console.WriteLine($"{name}: {(diffs.Count == 0 ? "一致" : $"違い {diffs.Count} 件")}（{sw.ElapsedMilliseconds} ms、TURN 変化 {facts.TurnChanges}、戦闘開始 {facts.CombatStarts}、PREDAMAGE {facts.Predamages}、" +
                              $"防がれた戦闘 {facts.Prevented}、名前未解決 {parser.UnresolvedNameCount}、自分の判定 {(facts.LocalFromDummyAgrees ? "OK" : "Bob 役と食い違い")}）");
            if (verbose || diffs.Count <= 6) foreach (var d in diffs) Console.WriteLine("    " + d);
            else foreach (var d in diffs.Take(6)) Console.WriteLine("    " + d);
        }
        Console.WriteLine($"--- {games} ゲーム（プラグインの記録と対応 {matched}）、違いのあるゲーム {gamesWithDiff}、項目 {fieldChecks} 件中 {fieldDiffs} 件の違い、名前未解決 {unresolved}、未知のタグ {unknownTags}");
        foreach (var (k, v) in diffKinds.OrderByDescending(kv => kv.Value)) Console.WriteLine($"    {k}: {v}");
        return 0;
    }

    private sealed record PluginTurn(int Number, int? TavernTier, int? Health, int? Armor, bool HasCombat, int? DamageDealt, int? DamageTaken);
    private sealed record PluginGame(DateTimeOffset Start, string? Hero, int? Placement, int? TotalTurns, List<PluginTurn> Turns);

    private static List<PluginGame> LoadPlugin(string dir)
    {
        var result = new List<PluginGame>();
        foreach (var path in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories))
        {
            if (path.Contains(Path.DirectorySeparatorChar + ".")) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var r = doc.RootElement;
                if (!r.TryGetProperty("game", out var g) || !g.TryGetProperty("started_at", out var st)) continue;
                var turns = new List<PluginTurn>();
                if (r.TryGetProperty("turns", out var ts) && ts.ValueKind == JsonValueKind.Array)
                    foreach (var t in ts.EnumerateArray())
                    {
                        var combat = t.TryGetProperty("combat", out var c) && c.ValueKind == JsonValueKind.Object ? c : (JsonElement?)null;
                        turns.Add(new PluginTurn(t.GetProperty("turn").GetInt32(), Int(t, "tavern_tier"), Int(t, "health"), Int(t, "armor"),
                            combat != null, combat is { } cc ? Int(cc, "damage_dealt") : null, combat is { } cd ? Int(cd, "damage_taken") : null));
                    }
                var hero = r.TryGetProperty("player", out var p) && p.TryGetProperty("hero", out var h) && h.ValueKind == JsonValueKind.Object && h.TryGetProperty("card_id", out var hc) ? hc.GetString() : null;
                var res = r.TryGetProperty("result", out var rr) ? rr : default;
                result.Add(new PluginGame(st.GetDateTimeOffset(), hero, res.ValueKind == JsonValueKind.Object ? Int(res, "placement") : null,
                    res.ValueKind == JsonValueKind.Object ? Int(res, "total_turns") : null, turns));
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { }
        }
        return result;
    }

    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
}
