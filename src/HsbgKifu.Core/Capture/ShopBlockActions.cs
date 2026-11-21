using HsbgKifu.Logic;
using HsbgKifu.Model;
using HsbgKifu.Parse;

namespace HsbgKifu.Capture;

/// <summary>
/// ショップの行動のうち、自分の PLAY ブロック（プレイヤーの操作そのもの）から正確に数えられるもの: 購入・売却・リロール・スペル・悪夢の発見・ヒーローパワー。
/// 購入（TB_BaconShop_DragBuy / _Spell）と売却（TB_BaconShop_DragSell）は対象のミニオンが Target に入る。
/// 差分から推定する ShopActionTracker（プラグインの数え方）は、3 枚目を買ってすぐトリプルになった購入や、リロールでショップが一度空になったときに外れる
/// （22 ゲーム 271 ターンで、推定は購入 18〜49 ターン・リロール 13〜21 ターンで外れ、この数え方が正解。docs/research.md §M3）。
/// </summary>
internal sealed class ShopBlockActions
{
    public sealed record Action(string Type, string? CardId);

    public int Buys { get; private set; }
    public int Sells { get; private set; }
    public int Rerolls { get; private set; }
    public int SpellsPlayed { get; private set; }
    public int DarkGifts { get; private set; }
    public int HeroPowerUses { get; private set; }

    /// <summary>ブロックから数える種類（ShopActionTracker の同じ種類の events.jsonl は書かない）。</summary>
    public static bool CountedFromBlocks(string type) =>
        type is ShopAction.Buy or ShopAction.Sell or ShopAction.Reroll or ShopAction.Spell or ShopAction.HeroPower;

    public void Reset() => Buys = Sells = Rerolls = SpellsPlayed = DarkGifts = HeroPowerUses = 0;

    /// <summary>自分がショップ中に出した PLAY ブロック 1 つ。events.jsonl に書く行動なら返す。</summary>
    public Action? Record(Entity source, Entity? target)
    {
        var card = source.OriginalCardId ?? source.CardId ?? "";
        switch (card)
        {
            case "TB_BaconShop_DragBuy":
            case "TB_BaconShop_DragBuy_Spell":
                Buys++;
                return new Action(ShopAction.Buy, target != null ? KifuCards.LatestCardId(target) : null);
            case "TB_BaconShop_DragSell":
                Sells++;
                return new Action(ShopAction.Sell, target != null ? KifuCards.LatestCardId(target) : null);
            case "TB_BaconShop_8p_Reroll_Button":
                Rerolls++;
                return new Action(ShopAction.Reroll, null);
            case "BG36_Button_DarkGift":
                // ボタンを押した回数。選んだカードは ShopActionTracker が dark_gift として書く
                DarkGifts++;
                return null;
        }
        if (source.IsHeroPower)
        {
            // EXHAUSTED の変化から数えると、1 ターンに何度も使える・使っても EXHAUSTED にならないヒーローパワーで外れる（236 ゲームで 113 ターン）
            HeroPowerUses++;
            // card_id は書かない（プラグインと同じ。発見したカードは後のまとまりで手札に来るので、ShopActionTracker が discover として書く）
            return new Action(ShopAction.HeroPower, null);
        }
        if (source.IsSpell || source.IsBattlegroundsSpell)
        {
            SpellsPlayed++;
            return new Action(ShopAction.Spell, KifuCards.LatestCardId(source));
        }
        return null;
    }

    public void ApplyTo(TurnActions actions)
    {
        actions.Buys = Buys;
        actions.Sells = Sells;
        actions.Rerolls = Rerolls;
        actions.SpellsPlayed = SpellsPlayed;
        actions.DarkGifts = DarkGifts;
        actions.HeroPowerUses = HeroPowerUses;
    }
}
