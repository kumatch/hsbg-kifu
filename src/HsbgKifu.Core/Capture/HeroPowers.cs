using HearthDb.Enums;
using HsbgKifu.Parse;

namespace HsbgKifu.Capture;

/// <summary>
/// 自分が持っているヒーローパワー（メインと追加の枠）。schema 1.12.0（turns[].hero_powers[] と events.jsonl の hero_power_changed）。
/// メインはヒーローの <c>HERO_POWER_ENTITY</c>、追加の枠は <c>ADDITIONAL_HERO_POWER_ENTITY_1</c>（その Entity は <c>ADDITIONAL_HERO_POWER_INDEX=1</c>）。
/// ウォーゲンの王ゲンの 2 つの発見や、時渡りの「○○の力」で 2 つになる（docs/research.md §3.6）。
/// </summary>
public static class HeroPowers
{
    public const string Main = "main";
    public const string Additional = "additional";

    /// <summary>
    /// 持っているヒーローパワー。メインが先、追加の枠は <c>ADDITIONAL_HERO_POWER_INDEX</c> の順。
    /// </summary>
    /// <param name="hero">自分のヒーロー（見つからなければ null。そのときは PLAY のヒーローパワーから決める）</param>
    /// <param name="inPlay">PLAY にある自分のヒーローパワー</param>
    public static List<(string Slot, Entity Power)> Held(Entity? hero, IReadOnlyList<Entity> inPlay)
    {
        var result = new List<(string, Entity)>();
        if (inPlay.Count == 0) return result;
        var mainId = hero?.GetTag(GameTag.HERO_POWER_ENTITY) ?? 0;
        var additionalId = hero?.GetTag(GameTag.ADDITIONAL_HERO_POWER_ENTITY_1) ?? 0;

        // HERO_POWER_ENTITY が無い（古いログ・ヒーローが見えない）なら、追加の枠でない最初のもの（0.4.0 までの決め方）
        var main = inPlay.FirstOrDefault(e => e.Id == mainId)
                   ?? inPlay.FirstOrDefault(e => e.Id != additionalId && e.GetTag(GameTag.ADDITIONAL_HERO_POWER_INDEX) == 0);
        if (main != null) result.Add((Main, main));

        // 追加の枠が 2 つ以上になる例はまだ見ていない（docs/research.md §7）。INDEX が付いた PLAY のヒーローパワーも拾う
        var additional = inPlay
            .Where(e => e != main && (e.Id == additionalId || e.GetTag(GameTag.ADDITIONAL_HERO_POWER_INDEX) > 0))
            .OrderBy(e => e.Id == additionalId ? 1 : e.GetTag(GameTag.ADDITIONAL_HERO_POWER_INDEX))
            .ThenBy(e => e.Id);
        foreach (var e in additional) result.Add((Additional, e));
        return result;
    }

    /// <summary>PLAY にある <paramref name="playerId"/> のヒーローパワー。</summary>
    public static List<Entity> InPlay(IEnumerable<Entity> entities, int playerId) =>
        entities.Where(e => e.IsHeroPower && e.IsInPlay && e.IsControlledBy(playerId)).ToList();
}
