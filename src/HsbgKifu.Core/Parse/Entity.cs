using HearthDb.Enums;

namespace HsbgKifu.Parse;

/// <summary>
/// ゲーム内の 1 つの Entity（カード、ヒーロー、プレイヤー、ゲーム自体など）。Power.log の行を当てはめて作る。
/// 読み方は HDT の <c>Hearthstone_Deck_Tracker.Hearthstone.Entities.Entity</c> に合わせてある（プラグインの Capture を移しやすくするため）。
/// </summary>
public sealed class Entity
{
    private readonly Dictionary<int, int> _tags = new();

    public Entity(int id) => Id = id;

    public int Id { get; }

    /// <summary>
    /// 今の CardID（<c>BG36_849</c> など。CHANGE_ENTITY で別のカードになれば、そのカード）。相手の手札など見えないものは null。
    /// HDT の <c>Entity.Info.LatestCardId ?? Entity.CardId</c> に当たる。
    /// </summary>
    public string? CardId { get; internal set; }

    /// <summary>
    /// HDT の <c>Entity.CardId</c>。FULL_ENTITY で見えた CardID で、SHOW_ENTITY / CHANGE_ENTITY では HDT と同じ条件のときだけ書き換わる
    /// （トリンケットの枠が選んだトリンケットになる、リロール中のヒーローなど。ゴールデンになる・変身するなどでは元のカードのまま）。
    /// </summary>
    public string? OriginalCardId { get; internal set; }

    public IReadOnlyDictionary<int, int> Tags => _tags;

    public int GetTag(GameTag tag) => GetTag((int)tag);
    public int GetTag(int tag) => _tags.TryGetValue(tag, out var v) ? v : 0;

    /// <summary>HDT と同じく、値が 0 より大きければ true。</summary>
    public bool HasTag(GameTag tag) => GetTag(tag) > 0;

    internal int SetTag(int tag, int value)
    {
        var prev = GetTag(tag);
        _tags[tag] = value;
        return prev;
    }

    public Zone Zone => (Zone)GetTag(GameTag.ZONE);
    public bool IsInZone(Zone zone) => Zone == zone;
    public bool IsInPlay => Zone == Zone.PLAY;
    public bool IsInHand => Zone == Zone.HAND;
    public bool IsInSetAside => Zone == Zone.SETASIDE;
    public bool IsInGraveyard => Zone == Zone.GRAVEYARD;
    public bool IsInSecret => Zone == Zone.SECRET;
    public int ZonePosition => GetTag(GameTag.ZONE_POSITION);

    public CardType CardType => (CardType)GetTag(GameTag.CARDTYPE);
    public bool IsMinion => CardType == CardType.MINION;
    public bool IsHero => CardType == CardType.HERO;
    public bool IsHeroPower => CardType == CardType.HERO_POWER;
    public bool IsSpell => CardType == CardType.SPELL;
    public bool IsBattlegroundsSpell => CardType == CardType.BATTLEGROUND_SPELL;
    public bool IsBattlegroundsTrinket => CardType == CardType.BATTLEGROUND_TRINKET;
    public bool IsEnchantment => CardType == CardType.ENCHANTMENT;
    public bool IsPlayer => CardType == CardType.PLAYER;

    public int Controller => GetTag(GameTag.CONTROLLER);
    public bool IsControlledBy(int playerId) => Controller == playerId;

    public int Attack => GetTag(GameTag.ATK);
    /// <summary>今の体力（HEALTH - DAMAGE）。HDT の Entity.Health と同じ。</summary>
    public int Health => GetTag(GameTag.HEALTH) - GetTag(GameTag.DAMAGE);
    public int Armor => GetTag(GameTag.ARMOR);

    public override string ToString() => $"Entity {Id} {CardId ?? "?"} {CardType} {Zone}";
}
