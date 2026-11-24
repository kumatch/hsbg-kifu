using System.Security.Cryptography;
using System.Text;
using HearthDb.Enums;
using HsbgKifu.Diagnostics;
using HsbgKifu.Logic;
using HsbgKifu.Model;
using HsbgKifu.Output;
using HsbgKifu.Parse;
using HsbgKifu.Watch;

namespace HsbgKifu.Capture;

public sealed class RecorderOptions
{
    /// <summary>ショップ終了時の手札を記録する（turns[].hand_end_of_shop。プラグインの snapshot_hand）。</summary>
    public bool SnapshotHand { get; init; } = true;
}

/// <summary>
/// 1 ゲーム分の中継ログの出来事から、記録（GameLog）と events.jsonl の行を組み立てる。
/// プラグインの Capture/GameSession を、HDT の代わりに <see cref="PowerGame"/> と <see cref="PowerGameListener"/> の出来事で動くように移したもの。
/// <list type="bullet">
/// <item>HDT の OnTurnStart(Player / Opponent) → <see cref="OnTurnChanged"/>（GameEntity の TURN が奇数 / 偶数）</item>
/// <item>OnUpdate の IsBattlegroundsCombatPhase の false → true → <see cref="OnCombatSetupEnded"/></item>
/// <item>OnUpdate（約 100 ms ごと）の差分の観測（ショップの行動・戦闘中の強化と神祇・ヒーロー選択）→ <see cref="OnBatchEnd"/></item>
/// <item>OnEntityWillTakeDamage → <see cref="OnPredamage"/>（時刻は行の時刻）</item>
/// <item>OnGameEnd → <see cref="OnGameEnded"/>。最後まで来なければ <see cref="Finish"/> で未完了として閉じる</item>
/// </list>
/// 時刻はすべて Power.log の行の時刻。再接続のつなぎ（M4）、MMR（M4）、ロビーの種族（M4）、Bob's Buddy（取れない）はまだ無い。
/// </summary>
public sealed class GameRecorder : PowerGameListener
{
    private readonly SliceMeta _meta;
    private readonly KifuCards _cards;
    private readonly RecorderOptions _options;
    private readonly IKifuLog _log;

    private GameLog? _record;
    private bool _active;
    private bool _discarded;
    private PowerGame? _game;
    private readonly List<string> _events = new();
    private readonly Dictionary<int, OpponentRecord> _opponents = new();
    private readonly Dictionary<int, int> _trinketFirstSeenTurn = new();

    // ヒーロー選択: 選択肢の Entity（MULLIGAN）。無料リロールでは同じ Entity が CHANGE_ENTITY で別のヒーローになる
    private List<int>? _offeredEntityIds;
    private int[]? _lastOffered;
    private List<HeroChoice>? _firstOffered;
    private readonly Dictionary<int, int> _choiceArmorByDbfId = new();

    // ショップ中の行動検出。購入・売却・リロール・スペル・悪夢の発見・ヒーローパワーは自分の PLAY ブロック（プレイヤーの操作そのもの）から数える（ShopBlockActions）。
    // それ以外（ティアアップ・発見・悪夢の発見のカード・トリプル・プレイ・ゴールド獲得・ショップの並び）は差分から推定する ShopActionTracker
    private readonly ShopActionTracker _tracker = new();
    private readonly ShopBlockActions _blockActions = new();
    private readonly Stack<ShopBlockActions.Action?> _openBlocks = new();
    private readonly Dictionary<int, string?> _handSourceCache = new();
    private readonly LobbyMinionTypes _lobbyTypes = new();
    private bool _shopPhase;
    private int _currentTurn;
    /// <summary>前に見たヒーローパワー（枠とカード）。変わったら hero_power_changed を書く（schema 1.12.0）。まだ見ていなければ null</summary>
    private List<(string Slot, string? CardId)>? _heldHeroPowers;

    // 戦闘
    private bool _inCombat;
    private int _combatTurn;
    /// <summary>戦闘直前（ショップ終了時 / 戦闘開始時）の自分の HP + アーマー（再接続で見られなかった戦闘の推定に使う）。</summary>
    private int? _combatStartMine;
    /// <summary>結果を見ないまま再接続した戦闘のターン（次のショップ開始時かゲーム終了時に HP + アーマーの差から推定する）。</summary>
    private int _interruptedCombatTurn;
    /// <summary>再接続の CREATE_GAME の下の Entity を当てはめ終えるまで待っている。</summary>
    private bool _resumePending;
    /// <summary>今読んでいる中継ログの付帯情報（再接続でつなぐときは 2 つ目以降のもの）。終わりの時刻に使う。</summary>
    private SliceMeta _currentMeta;
    private DateTime? _opponentBoardRecheckAt;
    private readonly List<HeroDamageEvent> _damageEvents = new();
    /// <summary>この戦闘で自分 / 相手のアイスブロックが発動したか（<see cref="OnTagChanged"/>。_damageEvents と同じ区間で数える）。</summary>
    private bool _iceBlockMine, _iceBlockOpponent;
    /// <summary>出たがまだ選ばれていない選択肢（DebugPrintEntityChoices の id → 記録とターン）</summary>
    private readonly Dictionary<int, (ChoiceRecord Record, int Turn)> _openChoices = new();

    // 神祇と「この対戦中」の強化（戦闘中は区切りごとに読み、戦闘の確定時に書く）
    private readonly DeityTracker _deity = new();
    private Entity? _playerSigil;
    private Entity? _opponentSigil;
    private readonly PlayerBuffChannel[] _buffs = PlayerBuffKind.All.Select(k => new PlayerBuffChannel(k)).ToArray();
    private int _shopEndMaxEntityId;

    public GameRecorder(SliceMeta meta, Cards.CardDb cards, RecorderOptions? options = null, IKifuLog? log = null)
    {
        _meta = meta;
        _currentMeta = meta;
        _cards = new KifuCards(cards);
        _options = options ?? new RecorderOptions();
        _log = log ?? NullKifuLog.Instance;
        PlayerBuffKind.UseCards(cards);
    }

    /// <summary>組み立てた記録。ターン 1 の前に終わったゲーム（捨てる）なら null。</summary>
    public GameLog? Record => _discarded ? null : _record;

    /// <summary>ターン 1 の前に終わったので捨てた（プラグインの DiscardAborted と同じ）。</summary>
    public bool Discarded => _discarded;

    /// <summary>events.jsonl の行（1 行 1 JSON）。</summary>
    public IReadOnlyList<string> EventLines => _events;

    /// <summary>次に読む中継ログ（再接続でつなぐとき、2 つ目以降の中継ログを読む前に呼ぶ）。</summary>
    public void BeginSlice(SliceMeta meta) => _currentMeta = meta;

    // ------------------------------------------------------------------ 出来事

    public override void OnGameCreated(PowerGame game)
    {
        if (_record != null)
        {
            // 2 つ目の CREATE_GAME = Hearthstone が落ちた後の再接続。同じ記録を続ける
            if (_active) Resume(game);
            return;
        }
        _game = game;
        var gameType = _meta.GameType ?? "GT_BATTLEGROUNDS";
        _record = new GameLog
        {
            Status = LogStatus.Incomplete,
            Generator =
            {
                Plugin = "hsbg-kifu",
                PluginVersion = KifuVersion.Current,
                HdtVersion = null,
                HearthstoneBuild = _meta.BuildNumber,
            },
            Game =
            {
                GameId = GameIdFor(_meta),
                Variant = gameType.Contains("DUO", StringComparison.Ordinal) ? "duos" : "solo",
                GameType = gameType,
                StartedAt = new DateTimeOffset(game.StartTime),
                Region = null,
            },
            Result = { MmrBefore = null },
        };
        if (_record.Game.Variant == "duos") Note("duos: not fully supported (MVP targets solo)");
        if (_meta.Reconnecting == true || game.StartedMidGame)
            Note("reconnect: this record starts after a Hearthstone restart (the earlier part of the game was not found)");
        _active = true;
        Emit("game_start", 0, F("game_id", _record.Game.GameId), F("game_type", _record.Game.GameType), F("mmr_before", null));
    }

    public override void OnGameSetupDone(PowerGame game)
    {
        if (!_resumePending || !_active || _record == null || game != _game) return;
        _resumePending = false;
        ContinueAfterReconnect(game);
    }

    public override void OnChoices(PowerGame game, EntityChoice choice)
    {
        if (!_active) return;
        if (choice.ChoiceType != "MULLIGAN")
        {
            // 発見の 3 択（トリプル報酬・ヒーローパワー・悪夢の発見など）。選び終わるまで区切りが来ないことが多いので、ここで覚える
            // （悪夢の発見の dark_gift の行は、手札に入ったカードから書く。その offered に使う）
            if (_shopPhase) _tracker.NoteOffered(choice.EntityIds);
            OpenChoice(game, choice);
            return;
        }
        if (_offeredEntityIds != null) return;
        _offeredEntityIds = choice.EntityIds.ToList();
        TrackHeroPick(game);
    }

    public override void OnChoiceMade(PowerGame game, EntityChoice choice)
    {
        if (!_active || choice.ChoiceType == "MULLIGAN" || !_openChoices.Remove(choice.Id, out var open)) return;
        var (rec, turn) = open;
        var chosenId = choice.ChosenIds.Count > 0 ? choice.ChosenIds[0] : 0;
        var index = chosenId > 0 ? choice.EntityIds.ToList().IndexOf(chosenId) : -1;
        rec.ChosenIndex = index >= 0 ? index : null;
        if (rec.Source == ChoiceSource.DarkGift) return; // events.jsonl には dark_gift の行がある（手札に入ったカードから。actions.dark_gifts と同じ数え方）
        var chosen = rec.ChosenIndex is { } i ? rec.Offered[i] : null;
        EmitAt(choice.ChosenTime ?? game.LastTime, "discover", turn, F("card_id", chosen?.CardId), F("name", chosen?.Name),
            F("gold_after", GoldRemaining(game.PlayerEntity)), F("source", rec.Source), F("source_card_id", rec.SourceCard.CardId),
            F("offered", rec.Offered.Select(c => c.CardId).ToList()));
    }

    /// <summary>
    /// 選択肢が出た: 候補をその時点の値で記録する（turns[].choices[]。選んだものは <see cref="OnChoiceMade"/> で埋める）。
    /// 何による選択かは、出したもの（Source=）と候補のカードの種類で決める。ターン数では決めない（トリンケットの選択はヒーローや効果で早まる・増える）
    /// </summary>
    private void OpenChoice(PowerGame game, EntityChoice choice)
    {
        if (_record == null || _currentTurn <= 0 || choice.EntityIds.Count == 0) return;
        var offered = choice.EntityIds.Select(id => game.Entities.TryGetValue(id, out var e) ? e : null).ToList();
        Entity? source = choice.SourceId is { } sid && game.Entities.TryGetValue(sid, out var s) ? s : null;
        var rec = new ChoiceRecord
        {
            Source = ChoiceSource.Of(source, offered, game.LocalPlayerId ?? 0, game.Entities),
            Offered = offered.Select(e => e != null ? _cards.ToSnapshot(e) : new CardSnapshot()).ToList(),
        };
        if (source != null) KifuCards.Fill(rec.SourceCard, _cards.FromCardId(KifuCards.LatestCardId(source)));
        GetOrAddTurn(_currentTurn).Choices.Add(rec);
        _openChoices[choice.Id] = (rec, _currentTurn);
    }

    public override void OnBatchEnd(PowerGame game, bool packetEnd)
    {
        if (!_active || _record == null) return;
        TrackHeroPick(game);
        if (_inCombat)
        {
            if (_opponentBoardRecheckAt is { } at && game.LastTime >= at)
            {
                _opponentBoardRecheckAt = null;
                RecheckOpponentBoard(game);
            }
            ObserveDeity(game);
            ObserveBuffs(game);
        }
        else if (_shopPhase)
        {
            ObserveShop(game, packetEnd);
        }
    }

    public override void OnBlockStart(PowerGame game, string blockType, Entity? source, Entity? target)
    {
        ShopBlockActions.Action? action = null;
        if (_active && _shopPhase && blockType == "PLAY" && source != null && game.LocalPlayerId is { } me && source.IsControlledBy(me))
            action = _blockActions.Record(source, target);
        _openBlocks.Push(action);
    }

    public override void OnBlockEnd(PowerGame game)
    {
        if (_openBlocks.Count == 0) return;
        // 操作のブロックが終わった時点のゴールド（プラグインの gold_after と同じく操作の後）
        if (_openBlocks.Pop() is not { } a || !_active || _record == null) return;
        var fields = new List<KeyValuePair<string, object?>>
        {
            F("card_id", a.CardId), F("name", _cards.FromCardId(a.CardId).Name), F("value", a.Type == ShopAction.Sell ? 1 : null),
            F("gold_after", GoldRemaining(game.PlayerEntity)),
        };
        if (a.Type == ShopAction.HeroPower) fields.Add(F("source_card_id", a.SourceCardId)); // どちらのヒーローパワーを使ったか（schema 1.12.0）
        Emit(a.Type, _currentTurn, fields.ToArray());
    }

    private static int? GoldRemaining(Entity? pe) =>
        pe != null && pe.HasTag(GameTag.RESOURCES) ? pe.GetTag(GameTag.RESOURCES) - pe.GetTag(GameTag.RESOURCES_USED) + pe.GetTag(GameTag.TEMP_RESOURCES) : null;

    public override void OnTurnChanged(PowerGame game, int gameEntityTurn)
    {
        if (!_active || _record == null) return;
        var turn = game.TurnNumber;
        if (turn <= 0) return;
        _currentTurn = turn;
        if (gameEntityTurn % 2 == 1)
        {
            _inCombat = false; // HDT はショップ開始で IsBattlegroundsCombatPhase を false にする
            OnShopStart(game, turn);
        }
        else
        {
            OnShopEnd(game, turn);
        }
    }

    public override void OnCombatSetupEnded(PowerGame game, int tag)
    {
        if (!_active || _record == null || _inCombat) return;
        _inCombat = true;
        var turn = game.TurnNumber;
        if (turn > 0)
        {
            CaptureCombat(game, turn);
            _opponentBoardRecheckAt = game.LastTime.AddMilliseconds(700);
        }
        ObserveDeity(game);
        ObserveBuffs(game);
    }

    public override void OnPredamage(PowerGame game, Entity entity, int value)
    {
        if (!_active || !entity.IsHero || game.LocalPlayerId is not { } local) return;
        _damageEvents.Add(new HeroDamageEvent
        {
            EntityId = entity.Id,
            IsPlayerHero = entity.IsControlledBy(local),
            Value = value,
            At = game.LastTime,
        });
    }

    /// <summary>
    /// アイスブロック（秘策 <see cref="IceBlockCardId"/>）が墓地に移った = 発動した（HP が 0 になるダメージを 1 度だけ無効にする）。
    /// 持ち主はその時の CONTROLLER で決める。相手のアイスブロックは戦闘ごとに複製され、複製の CONTROLLER は相手役のプレイヤー
    /// （発動しなければ墓地を経ずに REMOVEDFROMGAME に移る）。docs/research.md §3.1「ダメージ」
    /// </summary>
    public override void OnTagChanged(PowerGame game, Entity entity, int tag, int previous, int value, TagChangeKind kind)
    {
        if (!_active || tag != (int)GameTag.ZONE || value != (int)Zone.GRAVEYARD || kind != TagChangeKind.TagChange) return;
        if (entity.CardId != IceBlockCardId && entity.OriginalCardId != IceBlockCardId) return;
        if (game.LocalPlayerId is not { } local) return;
        if (entity.IsControlledBy(local)) _iceBlockMine = true;
        else _iceBlockOpponent = true;
    }

    /// <summary>アイスブロック（Safety Patch `BG35_MagicItem_820` などで得る秘策）。</summary>
    internal const string IceBlockCardId = "TB_Bacon_Secrets_12";

    private void ClearCombatEvents()
    {
        _damageEvents.Clear();
        _iceBlockMine = false;
        _iceBlockOpponent = false;
    }

    public override void OnGameEnded(PowerGame game)
    {
        if (!_active || _record == null) return;
        var log = _record;
        var turn = game.TurnNumber;
        var hero = FindPlayerHero(game);

        if (_shopPhase) FlushShopActions(game, turn);
        var interrupted = _combatTurn > 0 && _combatTurn == _interruptedCombatTurn;
        if (!interrupted) ResolveCombat(game, _combatTurn > 0 ? _combatTurn : turn, hero, crossCheck: false, gameEnd: true);

        FinalizeHero(game, hero, turn);
        UpdateLobby(game, turn, final: true);
        if (interrupted) ResolveInterruptedCombat(_combatTurn, HealthAndArmor(hero));
        FillGameInfo(game);

        var placement = hero?.GetTag(GameTag.PLAYER_LEADERBOARD_PLACE) ?? 0;
        log.Result.Placement = placement > 0 ? placement : null;
        log.Result.TotalTurns = turn;
        log.Result.EliminatedTurn = turn;
        if (AbortedGame.IsBeforeFirstTurn(log))
        {
            _discarded = true;
            _active = false;
            _log.Info($"{_meta.Session} {_meta.CreateGameTime}: ended before turn 1; not recorded");
            return;
        }
        log.Result.FinalTavernTier = hero != null && hero.GetTag(GameTag.PLAYER_TECH_LEVEL) > 0
            ? hero.GetTag(GameTag.PLAYER_TECH_LEVEL)
            : log.Turns.LastOrDefault()?.TavernTier;

        // 終わりの時刻: 画面側の COMPLETE（中継ログの最後。HDT の OnGameEnd と同じころ）。無ければこの行の時刻
        var endedAt = _currentMeta.EndTime ?? new DateTimeOffset(game.LastTime);
        log.Game.EndedAt = endedAt;
        if (log.Game.StartedAt.HasValue)
            log.Game.DurationSec = (int)Math.Round((endedAt - log.Game.StartedAt.Value).TotalSeconds);
        CloseOpponents(log);

        log.Status = log.Result.Placement.HasValue ? LogStatus.Complete : LogStatus.Incomplete;
        if (!log.Result.Placement.HasValue) Note("placement unavailable at game end");
        Emit("game_end", turn, F("placement", log.Result.Placement), F("total_turns", turn), F("status", log.Status));
        _active = false;
        _shopPhase = false;
    }

    /// <summary>中継ログを読み終えた。ゲームの終わりが無かった（Hearthstone が落ちた・ログが切れた）なら未完了として閉じる。</summary>
    public void Finish()
    {
        if (!_active || _record == null) return;
        if (AbortedGame.IsBeforeFirstTurn(_record))
        {
            _discarded = true;
            _active = false;
            return;
        }
        _record.Status = LogStatus.Incomplete;
        Note("the game did not end in the log (Hearthstone closed, crashed or the log was cut)");
        if (_game != null) FillGameInfo(_game);
        CloseOpponents(_record);
        _active = false;
    }

    // ------------------------------------------------------------------ 再接続（プラグインの ResumeAfterReconnect / ContinueAfterReconnect）

    /// <summary>
    /// Hearthstone が落ちて同じゲームに戻った（2 つ目の CREATE_GAME。その下にその時点の全 Entity が来る）。記録はそのまま続け、
    /// Entity に依存する追跡だけをやり直す。続きは CREATE_GAME の下を当てはめ終えてから（<see cref="ContinueAfterReconnect"/>）。
    /// </summary>
    private void Resume(PowerGame game)
    {
        var log = _record!;
        var lastTurn = Math.Max(_currentTurn, _combatTurn);
        var phase = _shopPhase ? "shop" : _inCombat ? "combat" : "shop end";
        if (_shopPhase && _currentTurn > 0)
        {
            // ショップ中に落ちた: このターンの戦闘は見られないものとして構える（同じショップに戻ってショップ終了を見れば OnShopEnd が取り消す）。
            // 直前の値はショップ開始時の HP + アーマー。購入などの回数は PLAY ブロックで数え続ける
            var rec = GetOrAddTurn(_currentTurn);
            rec.Combat ??= new CombatRecord();
            _combatTurn = _currentTurn;
            _combatStartMine = rec.Health.HasValue ? rec.Health.Value + rec.Armor.GetValueOrDefault() : null;
            _interruptedCombatTurn = _currentTurn;
        }
        else
        {
            var combat = _combatTurn > 0 ? log.Turns.FirstOrDefault(t => t.Turn == _combatTurn)?.Combat : null;
            _interruptedCombatTurn = combat != null && combat.Result == null ? _combatTurn : 0;
        }
        _game = game;
        _shopPhase = false;
        _inCombat = false;
        _opponentBoardRecheckAt = null;
        ClearCombatEvents();
        _openChoices.Clear(); // 落ちる前に出た選択肢は、選んだかどうか分からない（chosen_index は null のまま）
        _tracker.Reset();
        _deity.Reset();
        _playerSigil = null;
        _opponentSigil = null;
        ResetBuffs(keepPlayer: false);
        _handSourceCache.Clear();
        _resumePending = true;
        // 戦闘を最後まで見られなかったかは、戻った先で決まる（同じショップに戻れば普通に見られる）。推定したときは ResolveInterruptedCombat が別に書く
        var message = $"reconnect: Hearthstone restarted and resumed the same server game ({_currentMeta.GameHandle?.ToString() ?? "?"}); " +
                      $"the log before the restart ended in turn {lastTurn} ({phase})";
        log.Notes.Add(message);
        _log.Info($"{_currentMeta.Session} {_currentMeta.CreateGameTime}: {message}");
        Emit("reconnect", lastTurn, F("game_handle", _currentMeta.GameHandle),
            F("interrupted_combat_turn", _interruptedCombatTurn > 0 ? _interruptedCombatTurn : null));
    }

    /// <summary>
    /// 再接続した時点の状態が揃った。戦闘中に戻ったならその戦闘を「始まり」として拾わず、結果は後で推定する。
    /// 同じショップに戻ったならショップの観測を再開する。後のショップに戻ったなら（間のショップ開始を見ていないので）ここでショップを始める。
    /// </summary>
    private void ContinueAfterReconnect(PowerGame game)
    {
        var turn = game.TurnNumber;
        if (turn <= 0) return;
        if (game.GameEntityTurn % 2 == 0)
        {
            // 戦闘中に戻った: 結果は次のショップ開始時に推定する。相手がまだ分からなければヒーローだけ読む（盤面は戦闘の途中なので書かない）
            var rec = GetOrAddTurn(turn);
            rec.Combat ??= new CombatRecord();
            if (_combatTurn != turn)
            {
                if (_interruptedCombatTurn > 0 && _interruptedCombatTurn < turn)
                    _record!.Notes.Add($"reconnect: turn {_interruptedCombatTurn} combat result unknown (back in turn {turn})");
                _combatTurn = turn;
                _combatStartMine = null;
            }
            _combatStartMine ??= HealthAndArmor(FindPlayerHero(game));
            _interruptedCombatTurn = turn;
            _inCombat = true;
            _currentTurn = turn;
            if (rec.Combat.OpponentHero.IsEmpty && Snapshots.OpponentHero(Snapshots.SafeEntities(game), game.OpponentPlayerId ?? 0) is { } oppHero)
            {
                KifuCards.Fill(rec.Combat.OpponentHero, _cards.FromCardId(oppHero.OriginalCardId ?? oppHero.CardId));
                rec.Combat.OpponentHealth = oppHero.Health;
                rec.Combat.OpponentArmor = oppHero.GetTag(GameTag.ARMOR);
                var tier = oppHero.GetTag(GameTag.PLAYER_TECH_LEVEL);
                rec.Combat.OpponentTavernTier = tier > 0 ? tier : null;
                var pid = oppHero.GetTag(GameTag.PLAYER_ID);
                if (pid > 0) rec.Combat.OpponentPlayerId = pid;
            }
        }
        else if (turn == _currentTurn)
        {
            // 落ちる前と同じショップに戻った: 観測を続ける（最初の観測はベースラインになる）
            _tracker.Reset();
            _shopPhase = true;
        }
        else
        {
            // 後のショップに戻った: ショップ開始を見ていないので、ここで始める（前の戦闘は推定する）
            _currentTurn = turn;
            OnShopStart(game, turn);
        }
    }

    /// <summary>
    /// 再接続中に終わった戦闘の結果を、戦闘直前と今（次のショップ開始時 / ゲーム終了時）の HP + アーマーから推定する（Logic/Reconnect.InferCombat）。
    /// 推定できなければ結果は null のまま。どちらの場合も notes に書く。
    /// </summary>
    private void ResolveInterruptedCombat(int combatTurn, int? mineAfter)
    {
        _interruptedCombatTurn = 0;
        _deity.Reset();
        _opponentSigil = null;
        ResetBuffs(keepPlayer: true);
        var rec = _record!.Turns.FirstOrDefault(t => t.Turn == combatTurn);
        if (rec?.Combat == null || rec.Combat.Result != null) return;
        var c = rec.Combat;
        int? opponentBefore = c.OpponentHealth.HasValue ? c.OpponentHealth.Value + c.OpponentArmor.GetValueOrDefault() : null;
        int? opponentAfter = null;
        if (c.OpponentPlayerId.HasValue)
        {
            if (!opponentBefore.HasValue && rec.Lobby?.FirstOrDefault(l => l.PlayerId == c.OpponentPlayerId.Value) is { Health: not null } lobby)
                opponentBefore = lobby.Health!.Value + lobby.Armor.GetValueOrDefault(); // 戦闘開始を見る前に落ちた: ショップ開始時のロビーの値
            if (_opponents.TryGetValue(c.OpponentPlayerId.Value, out var o) && o.FinalHealth.HasValue)
                opponentAfter = o.FinalHealth.Value + o.FinalArmor.GetValueOrDefault();
        }
        var outcome = Reconnect.InferCombat(_combatStartMine, mineAfter, opponentBefore, opponentAfter);
        if (outcome != null)
        {
            c.Result = outcome.Result;
            c.DamageDealt = outcome.DamageDealt;
            c.DamageTaken = outcome.DamageTaken;
        }
        var message = $"reconnect: turn {combatTurn} combat ended while disconnected; result {outcome?.Result ?? "unknown"} inferred from health + armor " +
                      $"(mine {Show(_combatStartMine)} -> {Show(mineAfter)}, opponent {Show(opponentBefore)} -> {Show(opponentAfter)})";
        _record.Notes.Add(message);
        Emit("combat_end", combatTurn, F("result", c.Result), F("damage_dealt", c.DamageDealt), F("damage_taken", c.DamageTaken), F("inferred", true));
    }

    private static string Show(int? v) => v.HasValue ? v.Value.ToString() : "?";

    private static int? HealthAndArmor(Entity? hero) => hero != null ? hero.Health + hero.GetTag(GameTag.ARMOR) : null;

    // ------------------------------------------------------------------ ターン

    private void OnShopStart(PowerGame game, int turn)
    {
        var rec = GetOrAddTurn(turn);
        var hero = FindPlayerHero(game);

        // 前ターンの戦闘を確定（再接続で見られなかった戦闘はロビーを読んでから推定する）
        var interrupted = 0;
        if (_combatTurn > 0 && _combatTurn < turn)
        {
            if (_combatTurn == _interruptedCombatTurn) interrupted = _combatTurn;
            else ResolveCombat(game, _combatTurn, hero, crossCheck: true);
        }
        ClearCombatEvents();
        _opponentBoardRecheckAt = null;

        if (hero != null)
        {
            rec.Health = hero.Health;
            rec.Armor = hero.GetTag(GameTag.ARMOR);
            if (turn == 1)
            {
                _record!.Player.StartingHealth = hero.Health;
                _record.Player.StartingArmor = hero.GetTag(GameTag.ARMOR);
            }
        }
        if (turn == 1) FinalizeHero(game, hero, turn);
        UpdateLobby(game, turn, final: false);
        if (interrupted > 0) ResolveInterruptedCombat(interrupted, HealthAndArmor(hero));
        SnapshotLobby(rec);

        _tracker.Reset();
        _blockActions.Reset();
        _shopPhase = true;
        Emit("turn_start", turn, F("tier", hero?.GetTag(GameTag.PLAYER_TECH_LEVEL)), F("health", rec.Health), F("armor", rec.Armor));
    }

    private void OnShopEnd(PowerGame game, int turn)
    {
        var rec = GetOrAddTurn(turn);
        var hero = FindPlayerHero(game);
        var tier = hero?.GetTag(GameTag.PLAYER_TECH_LEVEL) ?? 0;
        if (tier <= 0) tier = game.PlayerEntity?.GetTag(GameTag.PLAYER_TECH_LEVEL) ?? 0;
        rec.TavernTier = tier > 0 ? tier : null;

        var pe = game.PlayerEntity;
        if (pe != null) rec.GoldAvailable = pe.HasTag(GameTag.RESOURCES) ? pe.GetTag(GameTag.RESOURCES) : null;

        FlushShopActions(game, turn);

        var me = game.LocalPlayerId ?? 0;
        var ents = Snapshots.SafeEntities(game);
        rec.BoardEndOfShop = Snapshots.Board(_cards, ents, me);
        rec.HandEndOfShop = _options.SnapshotHand ? Snapshots.Hand(_cards, ents, me) : null;
        rec.HeroPowers = HeroPowers.Held(hero, HeroPowers.InPlay(ents, me))
            .Select(h => ToHeroPowerRecord(h.Slot, h.Power))
            .ToList();

        // 神祇: 戦闘に持ち込む値。相手の Sigil は戦闘ごとに複製されるので戦闘中に探す
        _shopEndMaxEntityId = ents.Count > 0 ? ents.Max(e => e.Id) : 0;
        _playerSigil = DeityReader.PlayerSigil(ents, me);
        var deityReading = DeityReader.Read(_playerSigil);
        rec.Deity = DeityTracker.AtShopEnd(deityReading, DeityCard(game, deityReading));
        _deity.Reset();
        _opponentSigil = null;

        // この対戦中の強化: 戦闘に持ち込む値
        ResetBuffs(keepPlayer: false);
        foreach (var b in _buffs)
        {
            b.PlayerEnchant = b.Kind.PlayerEnchant(ents, me, pe?.Id);
            b.Kind.SetPlayer(rec, BuffStatsTracker.AtShopEnd(b.Kind.ReadPlayer(b.PlayerEnchant, pe)));
        }

        rec.Combat ??= new CombatRecord();
        var next = hero?.GetTag(GameTag.NEXT_OPPONENT_PLAYER_ID) ?? 0;
        if (next > 0) rec.Combat.OpponentPlayerId = next;
        _combatTurn = turn;
        _combatStartMine = HealthAndArmor(hero);
        if (_interruptedCombatTurn == turn) _interruptedCombatTurn = 0; // ショップ中に落ちて同じショップに戻れた。この戦闘は普通に見られる
        ClearCombatEvents();

        TrackTrinkets(game, turn);
        var fields = new List<KeyValuePair<string, object?>>
        {
            F("tier", rec.TavernTier), F("gold_available", rec.GoldAvailable), F("gold_spent", rec.Actions?.GoldSpent),
            F("board", rec.BoardEndOfShop.Select(m => m.CardId).ToList()), F("next_opponent_player_id", next > 0 ? next : null),
        };
        if (rec.Deity != null)
        {
            fields.Add(F("deity_attack", rec.Deity.Attack));
            fields.Add(F("deity_health", rec.Deity.Health));
        }
        foreach (var b in _buffs)
        {
            var r = b.Kind.GetPlayer(rec);
            if (r == null) continue;
            fields.Add(F(b.Kind.Key + "_attack", r.Attack));
            fields.Add(F(b.Kind.Key + "_health", r.Health));
        }
        Emit("shop_end", turn, fields.ToArray());
    }

    private void FlushShopActions(PowerGame game, int turn)
    {
        if (!_shopPhase) return;
        // ショップの終わりと同じまとまりの中の行動（終わる直前に使った呪文など）を拾ってから閉じる
        ObserveShop(game, packetEnd: true);
        _shopPhase = false;
        var rec = GetOrAddTurn(turn);
        var pe = game.PlayerEntity;
        int? goldSpent = pe != null && pe.HasTag(GameTag.RESOURCES) ? pe.GetTag(GameTag.RESOURCES_USED) : null;
        foreach (var a in _tracker.FlushPending())
            Emit(a.Type, turn, F("card_id", null), F("name", null), F("value", a.Value), F("gold_after", null));
        if (_tracker.Ticks >= 2)
        {
            rec.Actions = _tracker.ToActions(goldSpent);
        }
        else
        {
            rec.Actions ??= new TurnActions();
            rec.Actions.GoldSpent = goldSpent;
        }
        _blockActions.ApplyTo(rec.Actions);
    }

    private void ObserveShop(PowerGame game, bool packetEnd)
    {
        var ents = Snapshots.SafeEntities(game);
        var pid = game.LocalPlayerId ?? 0;
        var opp = game.OpponentPlayerId ?? 0;
        var pe = game.PlayerEntity;
        var heroId = pe?.GetTag(GameTag.HERO_ENTITY) ?? 0;
        Entity? hero = null;
        var heroPowers = new List<Entity>();
        foreach (var e in ents)
        {
            if (e.Id == heroId) hero = e;
            else if (e.IsHeroPower && e.IsInPlay && e.IsControlledBy(pid)) heroPowers.Add(e);
        }
        var held = HeroPowers.Held(hero, heroPowers);
        var heroPower = held.Count > 0 && held[0].Slot == HeroPowers.Main ? held[0].Power : null;
        TrackHeroPowers(held);
        // 発見（悪夢の発見 / ヒーローパワー / トリプル報酬）の 3 択。選んでいる途中だけ
        var offered = game.PendingChoice is { } c && c.ChoiceType != "MULLIGAN" && c.EntityIds.Count > 0 ? c.EntityIds.ToList() : null;

        var tick = Snapshots.Tick(ents, pid, opp, pe, hero, heroPower, offered, _handSourceCache);
        _lobbyTypes.Observe(game, tick.ShopIds);
        var actions = _tracker.Observe(tick, packetEnd);
        if (actions.Count == 0) return;
        foreach (var a in actions)
        {
            // 発見（トリプル報酬・ヒーローパワー）の行は、選択の出来事から書く（OnChoiceMade。schema 1.11.0）。手札の差からは書かない
            // （ShopActionTracker はヒーローパワーで発見したカードをヒーローパワーの行に載せるが、ヒーローパワーの行は PLAY ブロックから書く）
            if (a.Type == ShopAction.Discover) continue;
            if (ShopBlockActions.CountedFromBlocks(a.Type)) continue; // events.jsonl には PLAY ブロックから書く
            if (a.Type == ShopAction.Shop)
            {
                RecordShopOffer(ents, a);
                continue;
            }
            var ent = a.EntityId.HasValue ? ents.FirstOrDefault(e => e.Id == a.EntityId.Value) : null;
            var cardId = ent != null ? KifuCards.LatestCardId(ent) : null;
            var name = _cards.FromCardId(cardId).Name;
            if (a.Source == null && a.OfferedIds == null)
            {
                Emit(a.Type, _currentTurn, F("card_id", cardId), F("name", name), F("value", a.Value), F("gold_after", tick.GoldRemaining));
                continue;
            }
            var offeredCards = a.OfferedIds?
                .Select(id => ents.FirstOrDefault(e => e.Id == id))
                .Where(e => e != null)
                .Select(e => KifuCards.LatestCardId(e!))
                .Where(x => x != null)
                .ToList();
            Emit(a.Type, _currentTurn, F("card_id", cardId), F("name", name), F("value", a.Value), F("gold_after", tick.GoldRemaining),
                F("source", a.Source), F("offered", offeredCards));
        }
    }

    private HeroPowerRecord ToHeroPowerRecord(string slot, Entity power)
    {
        var rec = new HeroPowerRecord { Slot = slot };
        KifuCards.Fill(rec, _cards.FromCardId(KifuCards.LatestCardId(power)));
        return rec;
    }

    /// <summary>
    /// 持っているヒーローパワーが前に見たときから変わったら、枠ごとに hero_power_changed を書く（増えた・入れ替わった・なくなった。schema 1.12.0）。
    /// 最初に見たヒーローパワーは書かない（player.hero_power にある）。カードで比べるので、再接続で Entity が作り直されても書かない。
    /// ヒーローパワーがトリンケットやクエストに変わってなくなる（マリンなど。CHANGE_ENTITY）と、card_id が null の行になる
    /// </summary>
    private void TrackHeroPowers(List<(string Slot, Entity Power)> held)
    {
        var now = held.Select(h => (h.Slot, CardId: KifuCards.LatestCardId(h.Power))).ToList();
        var before = _heldHeroPowers;
        if (before == null)
        {
            // 最初に見たときの基準。メインが見えるまで待つ（ヒーローパワーが作られる前のまとまり）
            if (held.Count > 0 && held[0].Slot == HeroPowers.Main) _heldHeroPowers = now;
            return;
        }
        _heldHeroPowers = now;
        if (_currentTurn <= 0) return;
        foreach (var slot in new[] { HeroPowers.Main, HeroPowers.Additional })
        {
            var a = before.Where(h => h.Slot == slot).Select(h => h.CardId).ToList();
            var b = now.Where(h => h.Slot == slot).Select(h => h.CardId).ToList();
            for (var i = 0; i < Math.Max(a.Count, b.Count); i++)
            {
                var previous = i < a.Count ? a[i] : null;
                var current = i < b.Count ? b[i] : null;
                if (previous == current) continue;
                Emit("hero_power_changed", _currentTurn, F("slot", slot), F("card_id", current), F("name", _cards.FromCardId(current).Name),
                    F("previous_card_id", previous));
            }
        }
    }

    private void RecordShopOffer(List<Entity> ents, ShopAction a)
    {
        if (_record == null || _currentTurn <= 0 || a.ShopIds == null) return;
        var cards = Snapshots.ShopCards(_cards, ents, a.ShopIds);
        if (cards.Count == 0) return;
        GetOrAddTurn(_currentTurn).ShopOffers.Add(new ShopOffer { Trigger = a.Trigger ?? "", Cards = cards });
        Emit(ShopAction.Shop, _currentTurn, F("trigger", a.Trigger), F("cards", cards.Select(c => c.CardId).ToList()));
    }

    private void SnapshotLobby(TurnRecord rec)
    {
        if (_opponents.Count == 0) return;
        rec.Lobby = _opponents.Values
            .OrderBy(o => o.PlayerId)
            .Select(o => new LobbyEntry
            {
                PlayerId = o.PlayerId,
                TavernTier = o.FinalTavernTier,
                Health = o.FinalHealth,
                Armor = o.FinalArmor,
                LeaderboardPlace = o.EstimatedPlacement,
                Eliminated = o.EliminatedTurn.HasValue,
            })
            .ToList();
    }

    private TurnRecord GetOrAddTurn(int turn)
    {
        var rec = _record!.Turns.FirstOrDefault(t => t.Turn == turn);
        if (rec == null)
        {
            rec = new TurnRecord { Turn = turn };
            _record.Turns.Add(rec);
            _record.Turns.Sort((a, b) => a.Turn.CompareTo(b.Turn));
        }
        return rec;
    }

    // ------------------------------------------------------------------ 戦闘

    private void ObserveBuffs(PowerGame game)
    {
        var pe = game.PlayerEntity;
        foreach (var b in _buffs)
        {
            b.Tracker.ObservePlayer(b.Kind.ReadPlayer(b.PlayerEnchant, pe));
            if (b.OpponentSource != null)
                b.Tracker.ObserveOpponent(b.Kind.ReadOpponent(b.OpponentSource));
        }
        if (!_buffs.Any(b => b.NeedsScan)) return;
        // 相手の値の入れ物は戦闘ごとに作られる。見つかるまで区切りごとに探す（相手にその強化が無ければ作られない）
        var ents = Snapshots.SafeEntities(game);
        foreach (var b in _buffs)
        {
            b.PlayerEnchant ??= b.Kind.PlayerEnchant(ents, game.LocalPlayerId ?? 0, pe?.Id);
            if (b.OpponentSource == null)
            {
                b.OpponentSource = b.Kind.OpponentSource(ents, game.OpponentPlayerId ?? 0, _shopEndMaxEntityId);
                if (b.OpponentSource != null) b.Tracker.ObserveOpponent(b.Kind.ReadOpponent(b.OpponentSource));
            }
        }
    }

    private void CompleteBuffs(PowerGame game, TurnRecord rec, List<KeyValuePair<string, object?>> fields, bool gameEnd)
    {
        try
        {
            List<Entity>? ents = null;
            foreach (var b in _buffs)
            {
                var enchant = b.PlayerEnchant;
                if (enchant == null && b.Kind.EnchantCardId != null)
                    enchant = b.Kind.PlayerEnchant(ents ??= Snapshots.SafeEntities(game), game.LocalPlayerId ?? 0, game.PlayerEntity?.Id);
                var current = b.Kind.ReadPlayer(enchant, game.PlayerEntity);
                if (gameEnd)
                {
                    b.Tracker.ObservePlayer(current);
                    if (b.OpponentSource != null)
                        b.Tracker.ObserveOpponent(b.Kind.ReadOpponent(b.OpponentSource));
                }
                var mine = b.Tracker.CompletePlayer(b.Kind.GetPlayer(rec), current);
                b.Kind.SetPlayer(rec, mine);
                if (mine != null)
                {
                    fields.Add(F(b.Kind.Key + "_attack_after", mine.AttackAfterCombat));
                    fields.Add(F(b.Kind.Key + "_health_after", mine.HealthAfterCombat));
                }
                var opp = b.Tracker.OpponentRecord();
                b.Kind.SetOpponent(rec.Combat!, opp);
                if (opp != null)
                {
                    fields.Add(F("opponent_" + b.Kind.Key + "_attack", opp.Attack));
                    fields.Add(F("opponent_" + b.Kind.Key + "_health", opp.Health));
                    fields.Add(F("opponent_" + b.Kind.Key + "_attack_after", opp.AttackAfterCombat));
                    fields.Add(F("opponent_" + b.Kind.Key + "_health_after", opp.HealthAfterCombat));
                }
            }
        }
        finally
        {
            ResetBuffs(keepPlayer: true);
        }
    }

    private void ResetBuffs(bool keepPlayer)
    {
        foreach (var b in _buffs) b.Reset(keepPlayer);
    }

    private void ObserveDeity(PowerGame game)
    {
        // 神祇の無いシーズンでは自分の Sigil も無い。そのときは相手の Sigil を探さない
        if (_playerSigil == null) return;
        _deity.ObservePlayer(DeityReader.Read(_playerSigil));
        if (_deity.OpponentFirst == null && (_opponentSigil == null || DeityReader.Read(_opponentSigil)?.IsEmpty != false))
            _opponentSigil = DeityReader.OpponentSigil(Snapshots.SafeEntities(game), game.OpponentPlayerId ?? 0, 0);
        if (_opponentSigil != null)
            _deity.ObserveOpponent(DeityReader.Read(_opponentSigil));
    }

    private void CompleteDeity(PowerGame game, TurnRecord rec, List<KeyValuePair<string, object?>> fields)
    {
        try
        {
            var ents = Snapshots.SafeEntities(game);
            var (spawned, sigilIds) = DeityReader.CombatSpawns(ents, _shopEndMaxEntityId);
            var awakened = DeityTracker.AwakenedControllers(spawned, sigilIds);
            var golden = DeityTracker.GoldenControllers(spawned, sigilIds);
            var playerId = game.LocalPlayerId ?? 0;
            if (rec.Deity != null)
            {
                _deity.CompletePlayer(rec.Deity, DeityReader.Read(_playerSigil ?? DeityReader.PlayerSigil(ents, playerId)),
                    awakened.Contains(playerId), awakenedGolden: golden.Contains(playerId));
                fields.Add(F("deity_attack_after", rec.Deity.AttackAfterCombat));
                fields.Add(F("deity_health_after", rec.Deity.HealthAfterCombat));
                fields.Add(F("deity_awakened", rec.Deity.Awakened));
                fields.Add(F("deity_golden", rec.Deity.Golden));
            }
            var opp = rec.Combat!.OpponentDeity = _deity.OpponentRecord(DeityCard(game, _deity.OpponentFirst),
                awakened.Any(c => c != playerId), awakenedGolden: golden.Any(c => c != playerId));
            if (opp != null)
            {
                fields.Add(F("opponent_deity_attack", opp.Attack));
                fields.Add(F("opponent_deity_health", opp.Health));
                fields.Add(F("opponent_deity_awakened", opp.Awakened));
                fields.Add(F("opponent_deity_golden", opp.Golden));
            }
        }
        finally
        {
            _deity.Reset();
            _opponentSigil = null;
        }
    }

    private CardRef DeityCard(PowerGame game, DeityReading? reading)
    {
        var dbf = reading?.DbfId ?? 0;
        if (dbf <= 0) dbf = game.GameEntity?.GetTag(GameTag.BACON_GLOBAL_OLD_GOD_DBID) ?? 0;
        return _cards.FromDbfId(dbf);
    }

    private void CaptureCombat(PowerGame game, int turn)
    {
        var rec = GetOrAddTurn(turn);
        rec.Combat ??= new CombatRecord();
        _combatTurn = turn;
        _combatStartMine = HealthAndArmor(FindPlayerHero(game)) ?? _combatStartMine;
        var ents = Snapshots.SafeEntities(game);
        var oppId = game.OpponentPlayerId ?? 0;
        var oppHero = Snapshots.OpponentHero(ents, oppId);
        if (oppHero != null)
        {
            KifuCards.Fill(rec.Combat.OpponentHero, _cards.FromCardId(oppHero.OriginalCardId ?? oppHero.CardId));
            rec.Combat.OpponentHealth = oppHero.Health;
            rec.Combat.OpponentArmor = oppHero.GetTag(GameTag.ARMOR);
            var oppTier = oppHero.GetTag(GameTag.PLAYER_TECH_LEVEL);
            rec.Combat.OpponentTavernTier = oppTier > 0 ? oppTier : null;
            var pid = oppHero.GetTag(GameTag.PLAYER_ID);
            if (pid > 0)
            {
                if (rec.Combat.OpponentPlayerId.HasValue && rec.Combat.OpponentPlayerId.Value != pid)
                    Note($"turn {turn}: NEXT_OPPONENT_PLAYER_ID={rec.Combat.OpponentPlayerId} but combat opponent PLAYER_ID={pid}");
                rec.Combat.OpponentPlayerId = pid;
            }
        }
        else
        {
            Note($"turn {turn}: opponent hero not found at combat start");
        }
        rec.Combat.OpponentBoard = Snapshots.Board(_cards, ents, oppId);
        if (rec.BoardEndOfShop.Count == 0)
            rec.BoardEndOfShop = Snapshots.Board(_cards, ents, game.LocalPlayerId ?? 0);
        Emit("combat_start", turn, F("opponent_player_id", rec.Combat.OpponentPlayerId), F("opponent_hero_card_id", rec.Combat.OpponentHero.CardId),
            F("opponent_health", rec.Combat.OpponentHealth), F("opponent_armor", rec.Combat.OpponentArmor), F("opponent_tavern_tier", rec.Combat.OpponentTavernTier),
            F("opponent_board", rec.Combat.OpponentBoard.Select(m => m.CardId).ToList()));
    }

    private void RecheckOpponentBoard(PowerGame game)
    {
        if (_combatTurn <= 0) return;
        var rec = _record!.Turns.FirstOrDefault(t => t.Turn == _combatTurn);
        if (rec?.Combat == null) return;
        var board = Snapshots.Board(_cards, Snapshots.SafeEntities(game), game.OpponentPlayerId ?? 0);
        if (board.Count > rec.Combat.OpponentBoard.Count) rec.Combat.OpponentBoard = board;
    }

    private void ResolveCombat(PowerGame game, int combatTurn, Entity? hero, bool crossCheck, bool gameEnd = false)
    {
        if (combatTurn <= 0) return;
        var rec = _record!.Turns.FirstOrDefault(t => t.Turn == combatTurn);
        if (rec?.Combat == null || rec.Combat.Result != null) return;
        var outcome = CombatResolver.Resolve(_damageEvents);
        rec.Combat.Result = outcome.Result;
        rec.Combat.DamageDealt = outcome.DamageDealt;
        rec.Combat.DamageTaken = outcome.DamageTaken;
        rec.Combat.IceBlock = _iceBlockMine;
        rec.Combat.OpponentIceBlock = _iceBlockOpponent;
        var fields = new List<KeyValuePair<string, object?>>
        {
            F("result", outcome.Result), F("damage_dealt", outcome.DamageDealt), F("damage_taken", outcome.DamageTaken),
            F("ice_block", _iceBlockMine), F("opponent_ice_block", _iceBlockOpponent),
        };
        CompleteDeity(game, rec, fields);
        CompleteBuffs(game, rec, fields, gameEnd);
        Emit("combat_end", combatTurn, fields.ToArray());

        if (crossCheck && hero != null && hero.HasTag(GameTag.BACON_WON_LAST_COMBAT) && outcome.Result != "tie")
        {
            var won = hero.GetTag(GameTag.BACON_WON_LAST_COMBAT) == 1;
            if (won != (outcome.Result == "win"))
                Note($"turn {combatTurn}: combat result '{outcome.Result}' disagrees with BACON_WON_LAST_COMBAT={hero.GetTag(GameTag.BACON_WON_LAST_COMBAT)}");
        }
    }

    // ------------------------------------------------------------------ ヒーロー / ロビー

    private static Entity? FindPlayerHero(PowerGame game)
    {
        if (game.PlayerHero is { } hero) return hero;
        // 代わり: 自分の PLAY 中のヒーロー（ゲーム終了時は複製に注意）
        var me = game.LocalPlayerId ?? 0;
        return game.Entities.Values.FirstOrDefault(x => x.IsHero && x.IsInPlay && x.IsControlledBy(me));
    }

    private void FinalizeHero(PowerGame game, Entity? hero, int turn)
    {
        var log = _record!;
        // HDT の CardId（無料リロールで CHANGE_ENTITY されたヒーローは変わった後のヒーロー。プラグインの記録と同じ）
        var heroCard = hero?.OriginalCardId ?? hero?.CardId;
        if (heroCard != null && log.Player.Hero.IsEmpty)
        {
            KifuCards.Fill(log.Player.Hero, _cards.FromCardId(heroCard));
            Emit("hero_selected", turn, F("card_id", log.Player.Hero.CardId), F("name", log.Player.Hero.Name), F("dbf_id", log.Player.Hero.DbfId));
        }
        if (log.Player.HeroPower.IsEmpty)
        {
            // メイン（HERO_POWER_ENTITY）。ヒーローパワーが 2 つあっても取り違えない
            var held = HeroPowers.Held(hero, HeroPowers.InPlay(game.Entities.Values, game.LocalPlayerId ?? 0));
            var hp = held.Count > 0 && held[0].Slot == HeroPowers.Main ? held[0].Power : null;
            var hpCard = hp?.OriginalCardId ?? hp?.CardId;
            if (hpCard != null) KifuCards.Fill(log.Player.HeroPower, _cards.FromCardId(hpCard));
        }
        var offered = Offered(game);
        if (offered != null && offered.Length > 0)
            log.Player.HeroChoices = offered.Select(id => ToHeroChoice(game, id)).ToList();
        if (_firstOffered != null && offered != null && !_firstOffered.Select(c => c.DbfId).SequenceEqual(offered.Cast<int?>()))
            log.Player.HeroChoicesBeforeReroll = _firstOffered;
        TrackTrinkets(game, turn);
    }

    /// <summary>ヒーローの候補の dbfId（選択肢の Entity の今のカード。並びは ZONE_POSITION）。</summary>
    private int[]? Offered(PowerGame game)
    {
        if (_offeredEntityIds == null) return null;
        var heroes = _offeredEntityIds
            .Select(id => game.Entities.TryGetValue(id, out var e) ? e : null)
            .OfType<Entity>()
            .OrderBy(e => e.ZonePosition)
            .ToList();
        var dbfs = heroes.Select(e => _cards.DbfIdOf(e.CardId) ?? 0).ToArray();
        return dbfs.Length > 0 && dbfs.All(d => d > 0) ? dbfs : null;
    }

    private void TrackHeroPick(PowerGame game)
    {
        if (game.TurnNumber != 0 || _offeredEntityIds == null) return;
        var offered = Offered(game);
        if (offered == null || offered.Length == 0) return;
        RememberChoiceArmor(game);
        if (_lastOffered == null)
        {
            _lastOffered = offered;
            _firstOffered = offered.Select(id => ToHeroChoice(game, id)).ToList();
            Emit("hero_choice", 0, F("card_ids", _firstOffered.Select(c => c.CardId).ToList()), F("dbf_ids", offered));
        }
        else if (!_lastOffered.SequenceEqual(offered))
        {
            _lastOffered = offered;
            var choices = offered.Select(id => ToHeroChoice(game, id)).ToList();
            Emit("hero_reroll", 0, F("card_ids", choices.Select(c => c.CardId).ToList()), F("dbf_ids", offered));
        }
    }

    private void RememberChoiceArmor(PowerGame game)
    {
        if (_offeredEntityIds == null) return;
        foreach (var id in _offeredEntityIds)
        {
            if (!game.Entities.TryGetValue(id, out var e) || _cards.DbfIdOf(e.CardId) is not { } dbf) continue;
            _choiceArmorByDbfId[dbf] = e.GetTag(GameTag.ARMOR);
        }
    }

    private HeroChoice ToHeroChoice(PowerGame game, int dbfId)
    {
        var c = _cards.FromDbfId(dbfId);
        var choice = new HeroChoice { CardId = c.CardId, Name = c.Name, DbfId = c.DbfId };
        if (!_choiceArmorByDbfId.ContainsKey(dbfId)) RememberChoiceArmor(game);
        if (_choiceArmorByDbfId.TryGetValue(dbfId, out var armor)) choice.StartingArmor = armor;
        return choice;
    }

    private void TrackTrinkets(PowerGame game, int turn)
    {
        var me = game.LocalPlayerId ?? 0;
        // HDT の Player.Trinkets: 自分の PLAY 中の Entity のうち BACON_TRINKET を持つもの（ターン 1 からある枠 BG30_Trinket_1st / 2nd は持たない）
        foreach (var t in game.Entities.Values.Where(x => x.IsInPlay && x.HasTag(GameTag.BACON_TRINKET) && x.IsControlledBy(me)).ToList())
        {
            var cardId = t.OriginalCardId ?? t.CardId;
            if (cardId == null || _trinketFirstSeenTurn.ContainsKey(t.Id)) continue;
            _trinketFirstSeenTurn[t.Id] = turn;
            var c = _cards.FromCardId(cardId);
            _record!.Player.Trinkets.Add(new TrinketRecord { CardId = c.CardId, Name = c.Name, DbfId = c.DbfId, Turn = turn });
            Emit("trinket", turn, F("card_id", c.CardId), F("name", c.Name), F("dbf_id", c.DbfId));
        }
    }

    private void UpdateLobby(PowerGame game, int turn, bool final)
    {
        var myId = game.LocalPlayerId ?? 0;
        var heroes = game.Entities.Values
            .Where(x => x.IsHero && x.HasTag(GameTag.PLAYER_ID) && x.GetTag(GameTag.PLAYER_ID) != myId && x.IsInSetAside)
            .ToList();
        foreach (var h in heroes)
        {
            var pid = h.GetTag(GameTag.PLAYER_ID);
            if (!_opponents.TryGetValue(pid, out var rec))
            {
                rec = new OpponentRecord { PlayerId = pid };
                _opponents[pid] = rec;
            }
            var heroCard = h.OriginalCardId ?? h.CardId;
            if (rec.Hero.IsEmpty && heroCard != null)
                KifuCards.Fill(rec.Hero, _cards.FromCardId(heroCard));
            rec.FinalHealth = h.Health;
            rec.FinalArmor = h.GetTag(GameTag.ARMOR);
            var tier = h.GetTag(GameTag.PLAYER_TECH_LEVEL);
            if (tier > 0) rec.FinalTavernTier = tier;
            var place = h.GetTag(GameTag.PLAYER_LEADERBOARD_PLACE);
            if (place > 0) rec.EstimatedPlacement = place;
            if (h.Health <= 0 && rec.EliminatedTurn == null)
            {
                rec.EliminatedTurn = final ? turn : Math.Max(1, turn - 1);
                Emit("player_eliminated", turn, F("player_id", pid), F("hero_card_id", rec.Hero.CardId), F("eliminated_turn", rec.EliminatedTurn), F("place", place > 0 ? place : null));
            }
            foreach (var dbf in new[] { h.GetTag(GameTag.BACON_FIRST_TRINKET_DATABASE_ID), h.GetTag(GameTag.BACON_SECOND_TRINKET_DATABASE_ID) })
            {
                if (dbf > 0 && rec.Trinkets.All(t => t.DbfId != dbf))
                    rec.Trinkets.Add(_cards.FromDbfId(dbf));
            }
            if (final)
                rec.Placement = rec.EliminatedTurn.HasValue && place > 0 ? place : null;
        }
    }

    private void CloseOpponents(GameLog log)
    {
        log.Game.LobbySize = _opponents.Count > 0 ? _opponents.Count + 1 : null;
        log.Opponents = _opponents.Values.OrderBy(o => o.PlayerId).ToList();
    }

    private void FillGameInfo(PowerGame game)
    {
        var log = _record!;
        var ge = game.GameEntity;
        var anomaly = ge?.GetTag(GameTag.BACON_GLOBAL_ANOMALY_DBID) ?? 0;
        if (anomaly > 0) KifuCards.Fill(log.Game.Anomaly, _cards.FromDbfId(anomaly));
        var oldGod = ge?.GetTag(GameTag.BACON_GLOBAL_OLD_GOD_DBID) ?? 0;
        if (oldGod > 0) KifuCards.Fill(log.Game.GlobalOldGod, _cards.FromDbfId(oldGod));
        if (ge != null && ge.GetTag(GameTag.BACON_COMBAT_DAMAGE_CAP_ENABLED) > 0)
            log.Game.CombatDamageCap = ge.GetTag(GameTag.BACON_COMBAT_DAMAGE_CAP);
        // ロビーの種族: HDT はメモリから読んでいた。Power.log には無いので、ショップに並んだミニオンの種族の上位 5 つ（LobbyMinionTypes）
        var types = _lobbyTypes.Result(out var tie);
        if (types != null)
        {
            log.Game.MinionTypes = types;
            if (tie) Note($"minion_types: the 5th and 6th types were seen equally often in the shop ({_lobbyTypes.Describe()})");
        }
        else if (log.Turns.Count > 0)
        {
            Note($"minion_types unavailable: fewer than {LobbyMinionTypes.LobbySize} minion types were seen in the shop ({_lobbyTypes.Describe()})");
        }
    }

    // ------------------------------------------------------------------ events.jsonl / notes

    private static KeyValuePair<string, object?> F(string key, object? value) => new(key, value);

    private void Emit(string type, int turn, params KeyValuePair<string, object?>[] fields)
    {
        var at = _game != null ? new DateTimeOffset(_game.LastTime) : DateTimeOffset.Now;
        _events.Add(EventsJsonlWriter.Line(at, turn, type, fields));
    }

    /// <summary>行の時刻を指定して書く（選択は DebugPrintEntitiesChosen の行の時刻。PowerGame.LastTime は DebugPrintPower の行でしか進まない）。</summary>
    private void EmitAt(DateTime at, string type, int turn, params KeyValuePair<string, object?>[] fields)
        => _events.Add(EventsJsonlWriter.Line(new DateTimeOffset(at), turn, type, fields));

    private void Note(string message)
    {
        _record?.Notes.Add(message);
        Emit("warning", _currentTurn, F("message", message));
        _log.Warn($"{_meta.Session} {_meta.CreateGameTime}: {message}");
    }

    /// <summary>
    /// game_id: 中継ログ（ログフォルダ名 + CREATE_GAME の時刻）から決まる UUID。作り直しても同じ ID・同じファイル名になる。
    /// </summary>
    public static string GameIdFor(SliceMeta meta)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("hsbg-kifu|" + meta.Key));
        var bytes = hash.AsSpan(0, 16).ToArray();
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50); // version 5 風（名前から作った ID の目印）
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes).ToString();
    }
}
