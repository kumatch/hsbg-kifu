using HearthDb.Enums;
using HsbgKifu.Cards;
using HsbgKifu.Model;
using HsbgKifu.Parse;

namespace HsbgKifu.Capture;

/// <summary>
/// カードの情報から Model の DTO を作る（プラグインの Capture/HdtCards を移したもの。HearthDb の代わりに <see cref="CardDb"/> を使う）。
/// </summary>
internal sealed class KifuCards
{
    private readonly CardDb _db;

    public KifuCards(CardDb db) => _db = db;

    public CardDb Db => _db;

    public CardRef FromCardId(string? cardId)
    {
        var r = new CardRef { CardId = cardId };
        if (_db.ByCardId(cardId) is { } card)
        {
            r.Name = card.Name;
            r.DbfId = card.DbfId;
        }
        return r;
    }

    public CardRef FromDbfId(int? dbfId)
    {
        var r = new CardRef { DbfId = dbfId };
        if (dbfId is > 0 && _db.ByDbfId(dbfId) is { } card)
        {
            r.CardId = card.CardId;
            r.Name = card.Name;
        }
        else if (dbfId is <= 0)
        {
            r.DbfId = null;
        }
        return r;
    }

    public static void Fill(CardRef target, CardRef source)
    {
        target.CardId = source.CardId;
        target.Name = source.Name;
        target.DbfId = source.DbfId;
    }

    /// <summary>
    /// 種族の名前（小文字）。同じ値に名前が 2 つあるとき（20 = BEAST / PET）は宣言順で最初の名前（プラグインが使っていた HearthDb と同じ "beast"）。
    /// </summary>
    public static string RaceName(Race race) =>
        RaceNames.TryGetValue((int)race, out var name) ? name : race.ToString().ToLowerInvariant();

    private static readonly Dictionary<int, string> RaceNames = BuildRaceNames();

    private static Dictionary<int, string> BuildRaceNames()
    {
        var d = new Dictionary<int, string>();
        foreach (var f in typeof(Race).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).OrderBy(f => f.MetadataToken))
            d.TryAdd((int)f.GetValue(null)!, f.Name.ToLowerInvariant());
        return d;
    }

    /// <summary>Entity の今のカード ID（HDT の Info.LatestCardId ?? CardId。CHANGE_ENTITY の後のカード）。</summary>
    public static string? LatestCardId(Entity e) => e.CardId;

    public int? DbfIdOf(string? cardId) => _db.ByCardId(cardId)?.DbfId;

    /// <summary>手札・ショップのカード 1 枚（schema 1.5.0）。ミニオンは現在の攻撃力・体力と金色も持つ。</summary>
    public CardSnapshot ToSnapshot(Entity e)
    {
        var cardId = LatestCardId(e);
        var s = new CardSnapshot { CardId = cardId };
        var card = _db.ByCardId(cardId);
        if (card != null)
        {
            s.Name = card.Name;
            s.DbfId = card.DbfId;
        }
        var tier = e.HasTag(GameTag.TECH_LEVEL) ? e.GetTag(GameTag.TECH_LEVEL) : card?.TechLevel ?? 0;
        s.Tier = tier > 0 ? tier : null;
        if (e.IsMinion)
        {
            s.CardType = "minion";
            s.Attack = e.Attack;
            s.Health = e.Health;
            s.Golden = e.GetTag(GameTag.PREMIUM) > 0;
        }
        else
        {
            s.CardType = e.IsBattlegroundsSpell || e.IsSpell ? "spell" : "other";
        }
        return s;
    }

    public MinionRecord ToMinion(Entity e, int position)
    {
        var card = _db.ByCardId(LatestCardId(e));
        var m = new MinionRecord
        {
            Position = position,
            CardId = LatestCardId(e),
            Name = card?.Name,
            Tier = e.HasTag(GameTag.TECH_LEVEL) ? e.GetTag(GameTag.TECH_LEVEL) : card?.TechLevel ?? 0,
            Attack = e.Attack,
            Health = e.Health,
            Golden = e.GetTag(GameTag.PREMIUM) > 0,
        };
        if (e.GetTag(GameTag.TAUNT) > 0) m.Keywords.Add("taunt");
        // DIVINE_SHIELD は枚数（闇のパラドックスは重ねられる）
        m.DivineShields = Math.Max(0, e.GetTag(GameTag.DIVINE_SHIELD));
        if (m.DivineShields > 0) m.Keywords.Add("divine_shield");
        if (e.GetTag(GameTag.POISONOUS) > 0) m.Keywords.Add("poisonous");
        if (e.GetTag(GameTag.VENOMOUS) > 0) m.Keywords.Add("venomous");
        if (e.GetTag(GameTag.REBORN) > 0) m.Keywords.Add("reborn");
        if (e.GetTag(GameTag.WINDFURY) > 0) m.Keywords.Add("windfury");
        if (e.GetTag(GameTag.STEALTH) > 0) m.Keywords.Add("stealth");
        if (e.GetTag(GameTag.DEATHRATTLE) > 0) m.Keywords.Add("deathrattle");
        if (e.GetTag(GameTag.BACON_RALLY) > 0) m.Keywords.Add("rally");

        var races = new List<Race>();
        if (card != null && card.Race != Race.INVALID) races.Add(card.Race);
        if (card != null && card.SecondaryRace != Race.INVALID) races.Add(card.SecondaryRace);
        if (races.Count == 0 && e.HasTag(GameTag.CARDRACE)) races.Add((Race)e.GetTag(GameTag.CARDRACE));
        m.MinionTypes = races.Distinct().Select(RaceName).ToList();
        return m;
    }
}
