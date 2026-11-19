using HearthDb.Enums;

namespace HsbgKifu.Parse;

/// <summary>
/// Power.log に出るタグと値の名前を番号にする（HearthDb の列挙型。ThirdParty/HearthDb/Enums.cs）。
/// クライアントが名前を知らないタグは番号で出る（<c>tag=3533</c>）。値が名前で出るのは下の 11 種類のタグだけ（docs/research.md §3）。
/// </summary>
public static class TagNames
{
    private static readonly Dictionary<string, int> Tags = NamesOf<GameTag>();

    private static readonly Dictionary<int, Dictionary<string, int>> Values = new()
    {
        [(int)GameTag.ZONE] = NamesOf<Zone>(),
        [(int)GameTag.CARDTYPE] = NamesOf<CardType>(),
        [(int)GameTag.CARDRACE] = NamesOf<Race>(),
        [(int)GameTag.CLASS] = NamesOf<CardClass>(),
        [(int)GameTag.FACTION] = NamesOf<Faction>(),
        [(int)GameTag.MULLIGAN_STATE] = NamesOf<Mulligan>(),
        [(int)GameTag.STEP] = NamesOf<Step>(),
        [(int)GameTag.NEXT_STEP] = NamesOf<Step>(),
        [(int)GameTag.PLAYSTATE] = NamesOf<PlayState>(),
        [(int)GameTag.RARITY] = NamesOf<Rarity>(),
        [(int)GameTag.STATE] = NamesOf<State>(),
    };

    /// <summary><c>ZONE</c> → 49、<c>3533</c> → 3533。知らない名前なら null。</summary>
    public static int? Tag(string text)
    {
        if (TryInt(text, out var n)) return n;
        return Tags.TryGetValue(text, out var v) ? v : null;
    }

    /// <summary><c>value=PLAY</c>（ZONE）→ 1、<c>value=12</c> → 12。知らない名前なら null。</summary>
    public static int? Value(int tag, string text)
    {
        if (TryInt(text, out var n)) return n;
        return Values.TryGetValue(tag, out var names) && names.TryGetValue(text, out var v) ? v : null;
    }

    private static bool TryInt(string s, out int value) =>
        int.TryParse(s, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out value);

    private static Dictionary<string, int> NamesOf<T>() where T : struct, Enum
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in Enum.GetNames<T>()) d.TryAdd(name, Convert.ToInt32(Enum.Parse<T>(name)));
        return d;
    }
}
