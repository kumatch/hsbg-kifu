using HearthDb.Enums;
using HsbgKifu.Logic;
using HsbgKifu.Parse;

namespace HsbgKifu.Capture;

/// <summary>
/// 選択肢（turns[].choices[] と events.jsonl の discover）が何によるものか。schema 1.11.0。
/// 出したもの（DebugPrintEntityChoices の Source=）と候補のカードの種類で決める。ターン数では決めない
/// （トリンケットの選択は普通 6・9 ターン目だが、ヒーローや効果で 5・8 ターン目に増えたり、上級が 7 ターン目に早まったりする）。
/// </summary>
public static class ChoiceSource
{
    public const string Triple = CardSource.Triple;
    public const string HeroPower = CardSource.HeroPower;
    public const string DarkGift = CardSource.DarkGift;
    /// <summary>候補がすべてトリンケット（トリンケットの選択。出したものが何かは問わない）</summary>
    public const string Trinket = "trinket";
    /// <summary>それ以外のカード（ミニオンの雄叫び、呪文、トリンケットの効果など。出したものは source_card）</summary>
    public const string Card = "card";

    /// <param name="entities">今の Entity（出したものの作成元を引く）</param>
    public static string Of(Entity? source, IReadOnlyList<Entity?> offered, int localPlayerId, IReadOnlyDictionary<int, Entity> entities)
    {
        var cid = source?.OriginalCardId ?? source?.CardId ?? "";
        if (cid == Snapshots.TripleRewardCardId) return Triple;
        if (cid == Snapshots.DarkGiftButtonCardId || cid == Snapshots.DarkGiftDiscoverCardId)
        {
            // 悪夢の発見の発見スペルは、ボタンのほかにヒーローパワーも作る（BG36_HERO_105p など）。作成元が自分のヒーローパワーなら hero_power
            // （0.2.x までの手札の作成元 = DISPLAYED_CREATOR による判定と同じ結果。2026-11-20 00:14 のゲームで確認）
            return CreatedByMyHeroPower(source!, localPlayerId, entities) ? HeroPower : DarkGift;
        }
        if (offered.Count > 0 && offered.All(e => e != null && e.IsBattlegroundsTrinket)) return Trinket;
        if (source != null && source.IsHeroPower && source.IsControlledBy(localPlayerId)) return HeroPower;
        return Card;
    }

    private static bool CreatedByMyHeroPower(Entity e, int localPlayerId, IReadOnlyDictionary<int, Entity> entities)
    {
        foreach (var tag in new[] { GameTag.DISPLAYED_CREATOR, GameTag.CREATOR })
        {
            var id = e.GetTag(tag);
            if (id > 0 && entities.TryGetValue(id, out var creator) && creator.IsHeroPower && creator.IsControlledBy(localPlayerId)) return true;
        }
        return false;
    }
}
