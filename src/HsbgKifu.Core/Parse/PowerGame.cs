using HearthDb.Enums;

namespace HsbgKifu.Parse;

/// <summary>選択肢（ヒーロー選択・発見など）。自分にしか出ない（docs/research.md §3）。</summary>
public sealed record EntityChoice(int Id, string ChoiceType, IReadOnlyList<int> EntityIds, DateTime Time)
{
    /// <summary>選択肢を出したもの（DebugPrintEntityChoices の <c>Source=</c>。トリプル報酬・ヒーローパワー・ミニオンなど）。無ければ null</summary>
    public int? SourceId { get; init; }
    /// <summary>選ばれた Entity（DebugPrintEntitiesChosen の <c>Entities[i]</c>）。<see cref="PowerGameListener.OnChoiceMade"/> の時点で埋まっている</summary>
    public List<int> ChosenIds { get; } = new();
    /// <summary>選び終えた時刻（DebugPrintEntitiesChosen の行）</summary>
    public DateTime? ChosenTime { get; internal set; }
}

/// <summary>
/// 1 ゲーム分の状態。<see cref="PowerLogParser"/> が Power.log の <c>GameState.</c> の行から組み立てる。
/// 読み方は HDT の <c>GameV2</c> に寄せてある（Entities、PlayerEntity、GameEntity、GetTurnNumber など）。
/// プレイヤー名（BattleTag）は名前の解決にだけ使い、外には出さない。
/// </summary>
public sealed class PowerGame
{
    internal readonly Dictionary<int, string> PlayerNames = new();       // PlayerID → 名前（外に出さない）
    internal readonly Dictionary<int, int> PlayerEntityIdByPlayerId = new(); // PlayerID → プレイヤーの Entity

    public PowerGame(DateTime startTime) => StartTime = startTime;

    /// <summary>CREATE_GAME の行の時刻。</summary>
    public DateTime StartTime { get; }

    /// <summary>最後に当てはめた行の時刻。</summary>
    public DateTime LastTime { get; internal set; }

    public Dictionary<int, Entity> Entities { get; } = new();

    public Entity? GameEntity { get; internal set; }

    public int? BuildNumber { get; internal set; }
    public string? GameType { get; internal set; }
    public string? FormatType { get; internal set; }

    /// <summary>自分の PlayerID（ヒーロー選択の MULLIGAN の選択肢、または Bob 役でないプレイヤー）。</summary>
    public int? LocalPlayerId { get; internal set; }

    /// <summary>
    /// CREATE_GAME の時点でゲームが進んでいた（Hearthstone が落ちた後の再接続。GameEntity の TURN が 0 より大きい）。
    /// この場合 CREATE_GAME の下に、その時点の全 Entity が書かれる（docs/research.md §6）。
    /// </summary>
    public bool StartedMidGame { get; internal set; }

    /// <summary>GameEntity の STATE が COMPLETE になった（サーバー側でゲームが終わった）。</summary>
    public bool Ended { get; internal set; }

    public List<EntityChoice> Choices { get; } = new();

    /// <summary>選んでいる途中の選択肢（出てから DebugPrintEntitiesChosen まで）。HDT の <c>Player.OfferedEntityIds</c> に当たる。</summary>
    public EntityChoice? PendingChoice { get; internal set; }

    public IEnumerable<Entity> PlayerEntities =>
        PlayerEntityIdByPlayerId.Values.Select(id => Entities.TryGetValue(id, out var e) ? e : null).OfType<Entity>();

    public Entity? PlayerEntityOf(int playerId) =>
        PlayerEntityIdByPlayerId.TryGetValue(playerId, out var id) && Entities.TryGetValue(id, out var e) ? e : null;

    /// <summary>自分のプレイヤーの Entity。</summary>
    public Entity? PlayerEntity => LocalPlayerId is { } p ? PlayerEntityOf(p) : null;

    /// <summary>相手役のプレイヤー（BG のソロでは Bob 役。戦闘ではその時の相手のヒーローを持つ）の PlayerID。</summary>
    public int? OpponentPlayerId => LocalPlayerId is { } local ? PlayerEntityIdByPlayerId.Keys.Where(p => p != local).Cast<int?>().FirstOrDefault() : null;

    public Entity? OpponentEntity => OpponentPlayerId is { } p ? PlayerEntityOf(p) : null;

    /// <summary>自分のヒーロー（プレイヤー Entity の HERO_ENTITY が指すもの）。</summary>
    public Entity? PlayerHero => EntityAt(PlayerEntity?.GetTag(GameTag.HERO_ENTITY));

    public Entity? EntityAt(int? id) => id is { } i && i > 0 && Entities.TryGetValue(i, out var e) ? e : null;

    /// <summary>GameEntity の TURN（BG では奇数 = ショップ、偶数 = 戦闘）。</summary>
    public int GameEntityTurn => GameEntity?.GetTag(GameTag.TURN) ?? 0;

    /// <summary>BG のターン番号（(TURN + 1) / 2。HDT の GetTurnNumber と同じ）。ヒーロー選択中は 0。</summary>
    public int TurnNumber => (GameEntityTurn + 1) / 2;

    public bool IsBattlegrounds => GameType != null && GameType.StartsWith("GT_BATTLEGROUNDS", StringComparison.Ordinal);
}
