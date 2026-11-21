using System.Xml;
using HearthDb.Enums;

namespace HsbgKifu.Cards;

/// <summary>1 枚のカードの情報（プラグインが HearthDb の Card から読んでいたもの）。</summary>
public sealed record CardInfo(string CardId, int DbfId, string? Name, int TechLevel, Race Race, Race SecondaryRace, int Attack, int Health);

/// <summary>
/// カードの情報。HDT が hearthstonejson から更新し続ける <c>%APPDATA%\HearthstoneDeckTracker\CardDefs\CardDefs.base.xml</c> を読む（HDT を使い続ける前提。docs/research.md §4）。
/// 名前は英語（enUS。プラグインの記録と同じ）。2 つ目の種族は HearthDb の Card.SecondaryRace と同じく、
/// 種族のタグ（RaceUtils.TagRaceMap）のうち最初に出てくる、1 つ目の種族と違うもの。
/// ファイルが無ければ空（名前などは null になる）。
/// </summary>
public sealed class CardDb
{
    private const int CardNameTag = (int)GameTag.CARDNAME;
    private readonly Dictionary<string, CardInfo> _byCardId = new(StringComparer.Ordinal);
    private readonly Dictionary<int, CardInfo> _byDbfId = new();

    public static readonly CardDb Empty = new();

    private CardDb() { }

    public int Count => _byCardId.Count;

    /// <summary>CardDefs.base.xml の作られたビルド（ルートの build 属性）。</summary>
    public int? Build { get; private set; }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HearthstoneDeckTracker", "CardDefs", "CardDefs.base.xml");

    public CardInfo? ByCardId(string? cardId) => cardId != null && _byCardId.TryGetValue(cardId, out var c) ? c : null;

    public CardInfo? ByDbfId(int? dbfId) => dbfId is { } d && _byDbfId.TryGetValue(d, out var c) ? c : null;

    public static CardDb Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path)) return Empty;
        var db = new CardDb();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Ignore });
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element) continue;
            if (reader.Name == "CardDefs" && int.TryParse(reader.GetAttribute("build"), out var build)) db.Build = build;
            else if (reader.Name == "Entity") db.ReadEntity(reader);
        }
        return db;
    }

    private void ReadEntity(XmlReader reader)
    {
        var cardId = reader.GetAttribute("CardID");
        _ = int.TryParse(reader.GetAttribute("ID"), out var dbfId);
        if (string.IsNullOrEmpty(cardId) || reader.IsEmptyElement) return;
        string? name = null;
        int techLevel = 0, race = 0, attack = 0, health = 0;
        var raceTags = new List<Race>();
        var depth = reader.Depth;
        while (reader.Read() && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth))
        {
            if (reader.NodeType != XmlNodeType.Element || reader.Name != "Tag" || reader.Depth != depth + 1) continue;
            if (!int.TryParse(reader.GetAttribute("enumID"), out var tag)) continue;
            if (RaceUtils.TagRaceMap.TryGetValue(tag, out var r)) raceTags.Add(r);
            if (tag == CardNameTag)
            {
                name = ReadEnUs(reader);
                continue;
            }
            _ = int.TryParse(reader.GetAttribute("value"), out var value);
            switch ((GameTag)tag)
            {
                case GameTag.TECH_LEVEL: techLevel = value; break;
                case GameTag.CARDRACE: race = value; break;
                case GameTag.ATK: attack = value; break;
                case GameTag.HEALTH: health = value; break;
            }
        }
        var primary = (Race)race;
        var secondary = raceTags.FirstOrDefault(x => x != primary, Race.INVALID);
        var info = new CardInfo(cardId, dbfId, name, techLevel, primary, secondary, attack, health);
        _byCardId[cardId] = info;
        if (dbfId > 0) _byDbfId[dbfId] = info;
    }

    /// <summary>LocString の Tag の中の &lt;enUS&gt;。</summary>
    private static string? ReadEnUs(XmlReader reader)
    {
        if (reader.IsEmptyElement) return null;
        var depth = reader.Depth;
        string? value = null;
        reader.Read();
        // ReadElementContentAsString は読み終えた要素の次へ進むので、Read と混ぜて </Tag> を読み飛ばさないようにする（</Tag> の上で止まる）
        while (!reader.EOF && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth))
        {
            if (reader.NodeType == XmlNodeType.Element && reader.Name == "enUS") value = reader.ReadElementContentAsString();
            else reader.Read();
        }
        return value;
    }
}
