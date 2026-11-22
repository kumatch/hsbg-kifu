using System.Collections.Generic;

namespace HsbgKifu.Model
{
    /// <summary>
    /// 手札やショップに見えたカード 1 枚。ミニオンは攻撃力・体力・金色も持つ（手札のミニオンは強化されていることがある）。
    /// schema 1.5.0 で追加（1.4.x 以前の hand_end_of_shop は card_id / name / dbf_id だけ）
    /// </summary>
    public sealed class CardSnapshot : CardRef
    {
        /// <summary>"minion" | "spell" | "other"</summary>
        public string? CardType { get; set; }
        /// <summary>TECH_LEVEL（酒場の呪文にもある）。無ければ null</summary>
        public int? Tier { get; set; }
        /// <summary>ミニオンのみ。それ以外は null</summary>
        public int? Attack { get; set; }
        public int? Health { get; set; }
        public bool? Golden { get; set; }
    }

    /// <summary>
    /// ショップ（Bob の酒場）に並んだカード。ターン開始時・リロールごとに全体、それ以外で増えたカードは増えた分だけ。
    /// schema 1.5.0 で追加
    /// </summary>
    public sealed class ShopOffer
    {
        /// <summary>
        /// "turn_start"（ターンで最初に見えた酒場）| "reroll"（丸ごと入れ替わった）|
        /// "refill"（買い切って空になった後に並んだ）| "added"（効果などで途中から増えたカードだけ）
        /// </summary>
        public string Trigger { get; set; } = "";
        public List<CardSnapshot> Cards { get; set; } = new List<CardSnapshot>();
    }

    /// <summary>
    /// 選択肢 1 回分（発見・トリンケットの選択・ヒーローパワーの選択など。ヒーロー選択は含まない）。
    /// Power.log の DebugPrintEntityChoices（出したものと候補）と DebugPrintEntitiesChosen（選んだもの）から作る。schema 1.11.0 で追加
    /// </summary>
    public sealed class ChoiceRecord
    {
        /// <summary>
        /// 何による選択か: "triple"（トリプル報酬）| "hero_power"（自分のヒーローパワー）| "dark_gift"（悪夢の発見）|
        /// "trinket"（候補がすべてトリンケット）| "card"（それ以外のカード: ミニオンの雄叫び、呪文、トリンケットの効果など）
        /// </summary>
        public string Source { get; set; } = "";
        /// <summary>選択肢を出したカード（Source= の Entity の今のカード）。取れなければ中身が null</summary>
        public CardRef SourceCard { get; set; } = new CardRef();
        /// <summary>候補（画面に出た順）。攻撃力・体力・金色は候補が出た時点の値</summary>
        public List<CardSnapshot> Offered { get; set; } = new List<CardSnapshot>();
        /// <summary>選んだ候補の位置（offered の添字、0 始まり）。選び終える前にゲームが終わった・見られなかったなら null</summary>
        public int? ChosenIndex { get; set; }
    }

    /// <summary>ショップ開始時のロビー（リーダーボードに出ている他プレイヤーのティアと HP）。schema 1.5.0 で追加</summary>
    public sealed class LobbyEntry
    {
        public int PlayerId { get; set; }
        public int? TavernTier { get; set; }
        public int? Health { get; set; }
        public int? Armor { get; set; }
        /// <summary>PLAYER_LEADERBOARD_PLACE</summary>
        public int? LeaderboardPlace { get; set; }
        public bool Eliminated { get; set; }
    }
}
