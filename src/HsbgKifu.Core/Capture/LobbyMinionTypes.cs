using HearthDb.Enums;
using HsbgKifu.Parse;

namespace HsbgKifu.Capture;

/// <summary>
/// ロビーの種族（game.minion_types）を、ショップに並んだミニオンの種族から決める（docs/research.md §M4）。
/// HDT はメモリから読んでいたが、Power.log にも他のログにも出ない（Gameplay / Zone / Net の項目を足しても出なかった）。
/// <list type="bullet">
/// <item>ショップに並んだミニオン（ShopActionTracker と同じ判定: Bob 役の PLAY にいるミニオン）を、1 体 1 回ずつ数える</item>
/// <item>種族は Power.log に出るミニオン自身のタグ（CARDRACE と種族のタグ）から取る。カードの一覧は持たない（カードの更新で直さなくて済む）</item>
/// <item>無種族（種族のタグが無い）と全種族（CARDRACE が ALL、または種族のタグが 3 つ以上）は数えない。どのロビーでも並ぶ</item>
/// <item>種族を 2 つ持つミニオンは両方を数える。ロビー外の種族はまれにしか並ばない（特別な手段で並ぶバディなど）ので、数が十分あれば上位に来ない</item>
/// <item>ロビーの種族は毎ゲーム 5 つ（途中で変わらない）。上位 5 つを取る</item>
/// </list>
/// </summary>
internal sealed class LobbyMinionTypes
{
    public const int LobbySize = 5;

    private readonly HashSet<int> _seen = new();
    private readonly Dictionary<Race, int> _counts = new();

    public int MinionsSeen => _seen.Count;

    /// <summary>今ショップに並んでいるミニオン。初めて見た Entity だけ数える。</summary>
    public void Observe(PowerGame game, IEnumerable<int> shopIds)
    {
        foreach (var id in shopIds)
        {
            if (_seen.Contains(id) || !game.Entities.TryGetValue(id, out var e) || !e.IsMinion) continue;
            _seen.Add(id);
            foreach (var race in RacesOf(e)) _counts[race] = _counts.GetValueOrDefault(race) + 1;
        }
    }

    /// <summary>無種族・全種族なら空。種族を 2 つ持つなら 2 つ。</summary>
    public static IReadOnlyCollection<Race> RacesOf(Entity e)
    {
        var primary = (Race)e.GetTag(GameTag.CARDRACE);
        if (primary == Race.ALL) return Array.Empty<Race>();
        var races = new HashSet<Race>();
        if (primary != Race.INVALID) races.Add(primary);
        foreach (var (tag, value) in e.Tags)
            if (value > 0 && RaceUtils.TagRaceMap.TryGetValue(tag, out var r) && r != Race.INVALID && r != Race.ALL) races.Add(r);
        return races.Count >= 3 ? Array.Empty<Race>() : races;
    }

    /// <summary>
    /// 上位 5 つの種族（名前の順）。5 種類に満たなければ null。5 番目と 6 番目が同数なら <paramref name="tie"/> = true（名前の順で決める）。
    /// </summary>
    public List<string>? Result(out bool tie)
    {
        tie = false;
        var ranked = _counts.OrderByDescending(kv => kv.Value).ThenBy(kv => KifuCards.RaceName(kv.Key), StringComparer.Ordinal).ToList();
        if (ranked.Count < LobbySize) return null;
        tie = ranked.Count > LobbySize && ranked[LobbySize].Value == ranked[LobbySize - 1].Value;
        return ranked.Take(LobbySize).Select(kv => KifuCards.RaceName(kv.Key)).OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    /// <summary>notes 用の数え方の内訳（"elemental 41, demon 38, ..."）。</summary>
    public string Describe() =>
        string.Join(", ", _counts.OrderByDescending(kv => kv.Value).Select(kv => $"{KifuCards.RaceName(kv.Key)} {kv.Value}"));
}
