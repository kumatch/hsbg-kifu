using HsbgKifu.Parse;

namespace HsbgKifu.DevTools;

/// <summary>
/// ショップの行動の「正解」: 自分がショップ中に出した PLAY ブロック（購入・売却・リロール・ティアアップのボタン、ヒーローパワー、手札のカード）を数える。
/// プレイヤーの操作がそのまま残る行なので、差分から推定する ShopActionTracker（プラグインと hsbg-kifu）の答え合わせに使う。
/// </summary>
internal sealed class ActionTruth : PowerGameListener
{
    public sealed class Counts
    {
        public int Buys, Sells, Rerolls, TierUps, HeroPowerUses, SpellsPlayed, DarkGifts, Plays;
    }

    public readonly Dictionary<int, Counts> ByTurn = new();

    public override void OnBlockStart(PowerGame game, string blockType, Entity? source, Entity? target)
    {
        if (blockType != "PLAY" || source == null || game.LocalPlayerId is not { } me || !source.IsControlledBy(me)) return;
        if (game.GameEntityTurn % 2 != 1) return; // ショップ中だけ
        var turn = game.TurnNumber;
        if (!ByTurn.TryGetValue(turn, out var c)) ByTurn[turn] = c = new Counts();
        var card = source.OriginalCardId ?? source.CardId ?? "";
        if (card is "TB_BaconShop_DragBuy" or "TB_BaconShop_DragBuy_Spell") c.Buys++;
        else if (card == "TB_BaconShop_DragSell") c.Sells++;
        else if (card == "TB_BaconShop_8p_Reroll_Button") c.Rerolls++;
        else if (card.StartsWith("TB_BaconShopTechUp", StringComparison.Ordinal)) c.TierUps++;
        else if (card == "BG36_Button_DarkGift") c.DarkGifts++;
        else if (source.IsHeroPower) c.HeroPowerUses++;
        else if (source.IsSpell || source.IsBattlegroundsSpell) c.SpellsPlayed++;
        else if (source.IsMinion) c.Plays++;
    }

    public int? Get(int turn, string field)
    {
        if (!ByTurn.TryGetValue(turn, out var c)) return 0;
        return field switch
        {
            "buys" => c.Buys,
            "sells" => c.Sells,
            "rerolls" => c.Rerolls,
            "tier_ups" => c.TierUps,
            "hero_power_uses" => c.HeroPowerUses,
            "spells_played" => c.SpellsPlayed,
            "dark_gifts" => c.DarkGifts,
            _ => null,
        };
    }
}
