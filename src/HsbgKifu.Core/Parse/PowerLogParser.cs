using System.Text.RegularExpressions;
using HearthDb.Enums;
using HsbgKifu.Diagnostics;

namespace HsbgKifu.Parse;

/// <summary>
/// Power.log の <c>GameState.</c> の行から、ゲームの状態（<see cref="PowerGame"/>）を組み立て、出来事を <see cref="PowerGameListener"/> に知らせる。
/// HDT が使っているのと同じ <c>GameState.</c> の行だけを読む（<c>PowerTaskList.</c> は同じ内容を演出に合わせて後から書いたもの）。docs/research.md §3。
/// 行の種類はインデントではなく中身で見分ける（ブロックの中では TAG_CHANGE も深くインデントされる）。
/// </summary>
public sealed class PowerLogParser
{
    private const string PowerSource = "GameState.DebugPrintPower() - ";
    private const string GameSource = "GameState.DebugPrintGame() - ";
    private const string ChoicesSource = "GameState.DebugPrintEntityChoices() - ";
    private const string ChosenSource = "GameState.DebugPrintEntitiesChosen() - ";
    private const string PowerListSource = "GameState.DebugPrintPowerList() - ";

    private static readonly Regex BracketId = new(@" id=(\d+) zone=", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly PowerGameListener[] _listeners;
    private readonly IKifuLog _log;

    private Entity? _current;              // 直後の tag= 行を当てはめる Entity
    private TagChangeKind _currentKind;
    private bool _pendingUpdate;           // _current について OnEntityUpdated をまだ出していない
    private bool _inCreateGame;            // CREATE_GAME の下（GameEntity・Player・再接続の全 Entity）
    private EntityChoiceBuilder? _choice;
    private int _blockDepth;
    private int? _metaDamage;              // 直前の META_DATA - Meta=DAMAGE の Data（続く Info[i] の Entity が受けた）
    private readonly HashSet<string> _warned = new();

    public PowerLogParser(IEnumerable<PowerGameListener> listeners, IKifuLog? log = null)
    {
        _listeners = listeners.ToArray();
        _log = log ?? NullKifuLog.Instance;
    }

    public PowerLogParser(params PowerGameListener[] listeners) : this((IEnumerable<PowerGameListener>)listeners) { }

    /// <summary>今のゲーム（CREATE_GAME を見るまでは null）。</summary>
    public PowerGame? Game { get; private set; }

    /// <summary>名前で指されたのに、どのプレイヤーか決められなかった TAG_CHANGE の数。</summary>
    public int UnresolvedNameCount { get; private set; }

    /// <summary>知らないタグ名（HearthDb に無い）の TAG_CHANGE の数。</summary>
    public int UnknownTagCount { get; private set; }

    /// <summary>1 行を当てはめる。<paramref name="time"/> は日付つきの行の時刻（<see cref="Watch.SessionClock"/>）。</summary>
    public void Feed(string line, DateTime time)
    {
        if (line.Length < 20) return;
        var src = line.AsSpan(19);
        if (_choice != null && !src.StartsWith(ChoicesSource, StringComparison.Ordinal)) FlushChoice();
        if (src.StartsWith(PowerSource, StringComparison.Ordinal))
        {
            if (Game != null) Game.LastTime = time;
            Power(src[PowerSource.Length..].ToString(), time);
        }
        else if (src.StartsWith(GameSource, StringComparison.Ordinal)) GameInfo(src[GameSource.Length..].Trim().ToString());
        else if (src.StartsWith(ChoicesSource, StringComparison.Ordinal)) Choices(src[ChoicesSource.Length..].ToString(), time);
        else if (src.StartsWith(ChosenSource, StringComparison.Ordinal)) Chosen(src[ChosenSource.Length..].TrimStart().ToString());
        else if (src.StartsWith(PowerListSource, StringComparison.Ordinal))
        {
            // 次のまとまりが始まる = 前のまとまりの終わり
            FlushEntity();
            if (Game != null) Raise(l => l.OnBatchEnd(Game, packetEnd: true));
        }
    }

    /// <summary>入力の終わり（書き出しの前に呼ぶ）。保留している出来事を出す。</summary>
    public void Finish()
    {
        FlushEntity();
        FlushChoice();
        EndCreateGame();
        if (Game != null) Raise(l => l.OnBatchEnd(Game, packetEnd: true));
    }

    // ---------------------------------------------------------------- DebugPrintPower

    private void Power(string body, DateTime time)
    {
        var t = body.TrimStart();
        if (t.StartsWith("tag=", StringComparison.Ordinal))
        {
            if (_current != null && Game != null) InitialTag(_current, t);
            return;
        }
        FlushEntity();
        if (t.StartsWith("Info[", StringComparison.Ordinal))
        {
            // Info[0] = [entityName=... id=N ...]
            if (_metaDamage is { } amount && Game != null)
            {
                var eq = t.IndexOf("] = ", StringComparison.Ordinal);
                if (eq > 0 && Resolve(t[(eq + 4)..]) is { } target) Raise(l => l.OnDamage(Game, target, amount));
            }
            return;
        }
        _metaDamage = null;

        if (t == "CREATE_GAME")
        {
            FlushChoice();
            EndCreateGame();
            Game = new PowerGame(time) { LastTime = time };
            _inCreateGame = true;
            _blockDepth = 0;
            Raise(l => l.OnGameCreated(Game));
            return;
        }
        if (Game == null) return;

        if (t.StartsWith("GameEntity EntityID=", StringComparison.Ordinal))
        {
            var e = GetOrCreate(IntAfter(t, "EntityID="));
            Game.GameEntity = e;
            Begin(e, TagChangeKind.Creation);
            return;
        }
        if (t.StartsWith("Player EntityID=", StringComparison.Ordinal))
        {
            // Player EntityID=5 PlayerID=2 GameAccountId=[...]（アカウント ID は読まない）
            var e = GetOrCreate(IntAfter(t, "EntityID="));
            Game.PlayerEntityIdByPlayerId[IntAfter(t, "PlayerID=")] = e.Id;
            Begin(e, TagChangeKind.Creation);
            return;
        }
        // CREATE_GAME の下の Entity が終わった（最初の TAG_CHANGE / BLOCK など）。再接続のときは FULL_ENTITY が続くので、それも含める
        if (_inCreateGame && !t.StartsWith("FULL_ENTITY", StringComparison.Ordinal)) EndCreateGame();

        if (t.StartsWith("FULL_ENTITY - ", StringComparison.Ordinal))
        {
            // FULL_ENTITY - Creating ID=27 CardID=X / FULL_ENTITY - Updating [entityName=... id=27 ...] CardID=X
            var e = t.StartsWith("FULL_ENTITY - Creating", StringComparison.Ordinal)
                ? GetOrCreate(IntAfter(t, "ID="))
                : Resolve(Between(t, "FULL_ENTITY - Updating ", " CardID="));
            if (e == null) return;
            SetCardId(e, After(t, " CardID="));
            Begin(e, TagChangeKind.Creation);
            return;
        }
        if (t.StartsWith("SHOW_ENTITY - Updating Entity=", StringComparison.Ordinal) || t.StartsWith("CHANGE_ENTITY - Updating Entity=", StringComparison.Ordinal))
        {
            var kind = t[0] == 'S' ? TagChangeKind.Show : TagChangeKind.Change;
            var e = Resolve(Between(t, "Updating Entity=", " CardID="));
            if (e == null) return;
            SetCardId(e, After(t, " CardID="), update: true);
            Begin(e, kind);
            return;
        }
        if (t.StartsWith("HIDE_ENTITY - Entity=", StringComparison.Ordinal))
        {
            var (entityText, tag, value) = SplitTagChange(t["HIDE_ENTITY - Entity=".Length..]);
            if (Resolve(entityText) is { } e) Apply(e, tag, value, TagChangeKind.Hide);
            return;
        }
        if (t.StartsWith("TAG_CHANGE Entity=", StringComparison.Ordinal))
        {
            var (entityText, tag, value) = SplitTagChange(t["TAG_CHANGE Entity=".Length..]);
            var e = Resolve(entityText);
            if (e != null) Apply(e, tag, value, TagChangeKind.TagChange);
            return;
        }
        if (t.StartsWith("BLOCK_START ", StringComparison.Ordinal))
        {
            _blockDepth++;
            var blockType = Between(t, "BlockType=", " ") ?? "";
            var source = Resolve(Between(t, " Entity=", " EffectCardId="));
            var targetText = Between(t, " Target=", " SubOption=");
            var target = targetText is null or "0" ? null : Resolve(targetText);
            Raise(l => l.OnBlockStart(Game, blockType, source, target));
            return;
        }
        if (t == "BLOCK_END")
        {
            if (_blockDepth > 0) _blockDepth--;
            Raise(l => l.OnBlockEnd(Game));
            Raise(l => l.OnBatchEnd(Game, packetEnd: false));
        }
        if (t.StartsWith("META_DATA - Meta=DAMAGE ", StringComparison.Ordinal))
        {
            _metaDamage = IntAfter(t, "Data=");
            return;
        }
        // ほかの META_DATA / SUB_SPELL_START / Targets[] などは今は使わない
    }

    private void Begin(Entity e, TagChangeKind kind)
    {
        _current = e;
        _currentKind = kind;
        _pendingUpdate = true;
    }

    private void FlushEntity()
    {
        var e = _current;
        _current = null;
        if (e == null || !_pendingUpdate || Game == null) return;
        _pendingUpdate = false;
        var kind = _currentKind;
        Raise(l => l.OnEntityUpdated(Game, e, kind));
    }

    private void EndCreateGame()
    {
        if (!_inCreateGame || Game == null) return;
        _inCreateGame = false;
        if (Game.GameEntityTurn > 0) Game.StartedMidGame = true;
        DetectLocalPlayerFromDummy();
        Raise(l => l.OnGameSetupDone(Game));
    }

    private void InitialTag(Entity e, string t)
    {
        // tag=ZONE value=PLAY
        var (_, tagText, valueText) = SplitTagChange("x " + t);
        Apply(e, tagText, valueText, _currentKind);
    }

    private void Apply(Entity e, string tagText, string valueText, TagChangeKind kind)
    {
        var game = Game!;
        if (TagNames.Tag(tagText) is not { } tag)
        {
            UnknownTagCount++;
            WarnOnce("tag:" + tagText, $"HearthDb に無いタグ名です: {tagText}（ThirdParty/HearthDb/Enums.cs を更新してください）");
            return;
        }
        if (TagNames.Value(tag, valueText) is not { } value)
        {
            WarnOnce($"value:{tagText}={valueText}", $"HearthDb に無い値の名前です: {tagText}={valueText}");
            return;
        }
        var prev = e.SetTag(tag, value);
        if (prev == value) return;
        Raise(l => l.OnTagChanged(game, e, tag, prev, value, kind));
        if (kind != TagChangeKind.TagChange) return;

        if (e == game.GameEntity)
        {
            if (tag == (int)GameTag.TURN) Raise(l => l.OnTurnChanged(game, value));
            else if (tag == (int)GameTag.STATE && value == (int)State.COMPLETE && !game.Ended)
            {
                game.Ended = true;
                Raise(l => l.OnGameEnded(game));
            }
        }
        if ((tag == 3533 || tag == 2022) && prev == 1 && value == 0) Raise(l => l.OnCombatSetupEnded(game, tag));
        else if (tag == (int)GameTag.PREDAMAGE && value > 0) Raise(l => l.OnPredamage(game, e, value));
    }

    /// <summary>
    /// CardID を当てはめる。<see cref="Entity.CardId"/>（今のカード）はいつも書き換える。<see cref="Entity.OriginalCardId"/>（HDT の Entity.CardId）は
    /// HDT の PowerHandler と同じ規則: FULL_ENTITY では書き、SHOW_ENTITY / CHANGE_ENTITY では、まだ無いか、その時点で
    /// BACON_IS_MAGIC_ITEM_DISCOVER（トリンケットの枠など）/ BACON_TRINKET / BACON_HERO_CAN_BE_DRAFTED / BACON_SKIN（リロール中のヒーロー）を持つか、
    /// <c>CREATED_BY_</c> で始まるときだけ書き換える（ゴールデンになる・変身するなどでは元のカードのまま）。
    /// </summary>
    private static void SetCardId(Entity e, string? cardId, bool update = false)
    {
        if (string.IsNullOrEmpty(cardId)) return;
        e.CardId = cardId;
        if (!update
            || string.IsNullOrEmpty(e.OriginalCardId)
            || e.HasTag(GameTag.BACON_IS_MAGIC_ITEM_DISCOVER)
            || e.HasTag(GameTag.BACON_TRINKET)
            || e.HasTag(GameTag.BACON_HERO_CAN_BE_DRAFTED)
            || e.HasTag(GameTag.BACON_SKIN)
            || e.OriginalCardId.StartsWith("CREATED_BY_", StringComparison.Ordinal))
            e.OriginalCardId = cardId;
    }

    // ---------------------------------------------------------------- 名前の解決

    private Entity GetOrCreate(int id)
    {
        var game = Game!;
        if (!game.Entities.TryGetValue(id, out var e)) game.Entities[id] = e = new Entity(id);
        return e;
    }

    /// <summary>
    /// Entity の指し方: 番号 / <c>GameEntity</c> / <c>[entityName=... id=N zone=...]</c> / プレイヤー名。
    /// プレイヤー名は、DebugPrintGame に載っている名前ならそのプレイヤー、載っていなければ Bob 役（BACON_DUMMY_PLAYER）のプレイヤー
    /// （相手役のプレイヤーは、その時の戦闘相手の名前などで指される。docs/research.md §3）。
    /// </summary>
    private Entity? Resolve(string? text)
    {
        if (string.IsNullOrEmpty(text) || Game == null) return null;
        if (text.StartsWith("UNKNOWN ENTITY ", StringComparison.Ordinal)) text = text["UNKNOWN ENTITY ".Length..];
        if (text[0] == '[')
        {
            var m = BracketId.Match(text);
            return m.Success ? GetOrCreate(int.Parse(m.Groups[1].Value)) : null;
        }
        if (char.IsDigit(text[0]) && int.TryParse(text, out var id)) return GetOrCreate(id);
        if (text == "GameEntity") return Game.GameEntity;

        foreach (var (playerId, name) in Game.PlayerNames)
            if (name == text && Game.PlayerEntityOf(playerId) is { } named) return named;
        if (Game.PlayerNames.Count == 0)
        {
            // DebugPrintGame の前に名前で指された（自分か相手か決められない）
            UnresolvedNameCount++;
            return null;
        }
        var dummy = Game.PlayerEntities.FirstOrDefault(p => p.HasTag(GameTag.BACON_DUMMY_PLAYER));
        if (dummy != null) return dummy;
        if (Game.OpponentEntity is { } other) return other;
        UnresolvedNameCount++;
        return null;
    }

    private void DetectLocalPlayerFromDummy()
    {
        // MULLIGAN の選択肢が来ない場合（再接続）: Bob 役でない方が自分（ソロのみ。デュオは要検証）
        var game = Game!;
        if (game.LocalPlayerId != null) return;
        var players = game.PlayerEntityIdByPlayerId.Where(kv => game.Entities.TryGetValue(kv.Value, out var e) && !e.HasTag(GameTag.BACON_DUMMY_PLAYER)).ToList();
        if (players.Count == 1 && game.PlayerEntityIdByPlayerId.Count == 2) SetLocalPlayer(players[0].Key);
    }

    private void SetLocalPlayer(int playerId)
    {
        var game = Game!;
        if (game.LocalPlayerId == playerId) return;
        if (game.LocalPlayerId is { } old)
        {
            WarnOnce("local-mismatch", $"自分の PlayerID の判定が食い違いました（{old} → {playerId}）。ヒーロー選択の方を使います");
        }
        game.LocalPlayerId = playerId;
        Raise(l => l.OnLocalPlayerKnown(game, playerId));
    }

    // ---------------------------------------------------------------- DebugPrintGame / 選択肢

    private void GameInfo(string body)
    {
        if (Game == null) return;
        // PlayerID=7, PlayerName=<BattleTag>（名前は外に出さない）
        if (body.StartsWith("PlayerID=", StringComparison.Ordinal))
        {
            var comma = body.IndexOf(", PlayerName=", StringComparison.Ordinal);
            if (comma > 0 && int.TryParse(body.AsSpan(9, comma - 9), out var pid))
                Game.PlayerNames[pid] = body[(comma + ", PlayerName=".Length)..];
            return;
        }
        if (body.StartsWith("BuildNumber=", StringComparison.Ordinal) && int.TryParse(body.AsSpan(12), out var build)) Game.BuildNumber = build;
        else if (body.StartsWith("GameType=", StringComparison.Ordinal)) Game.GameType = body[9..];
        else if (body.StartsWith("FormatType=", StringComparison.Ordinal)) Game.FormatType = body[11..];
    }

    private void Choices(string body, DateTime time)
    {
        if (Game == null) return;
        var t = body.TrimStart();
        if (t.StartsWith("id=", StringComparison.Ordinal))
        {
            // id=1 Player=<名前> TaskList=7 ChoiceType=MULLIGAN CountMin=1 CountMax=1
            FlushChoice();
            var playerName = Between(t, " Player=", " TaskList=");
            var choiceType = Between(t, "ChoiceType=", " ") ?? "";
            _choice = new EntityChoiceBuilder(IntAfter(t, "id="), choiceType, time);
            if (choiceType == "MULLIGAN" && playerName != null)
            {
                foreach (var (pid, name) in Game.PlayerNames)
                    if (name == playerName) SetLocalPlayer(pid);
            }
            return;
        }
        if (_choice != null && t.StartsWith("Entities[", StringComparison.Ordinal))
        {
            var eq = t.IndexOf("]=", StringComparison.Ordinal);
            if (eq > 0 && Resolve(t[(eq + 2)..]) is { } e) _choice.EntityIds.Add(e.Id);
        }
    }

    private void FlushChoice()
    {
        var c = _choice;
        _choice = null;
        if (c == null || Game == null) return;
        var choice = new EntityChoice(c.Id, c.ChoiceType, c.EntityIds, c.Time);
        Game.Choices.Add(choice);
        Game.PendingChoice = choice;
        Raise(l => l.OnChoices(Game, choice));
    }

    private void Chosen(string body)
    {
        // id=1 Player=<名前> EntitiesCount=1（続く Entities[i] は読まない）
        FlushChoice();
        if (Game == null || !body.StartsWith("id=", StringComparison.Ordinal)) return;
        var id = IntAfter(body, "id=");
        if (Game.PendingChoice is { } pending && pending.Id == id)
        {
            Game.PendingChoice = null;
            Raise(l => l.OnChoiceMade(Game, pending));
        }
    }

    private sealed class EntityChoiceBuilder(int id, string choiceType, DateTime time)
    {
        public int Id { get; } = id;
        public string ChoiceType { get; } = choiceType;
        public DateTime Time { get; } = time;
        public List<int> EntityIds { get; } = new();
    }

    // ---------------------------------------------------------------- 文字列

    /// <summary><c>&lt;entity&gt; tag=T value=V[ DEF CHANGE]</c> を分ける。entity の中に " tag=" が無い前提で、最後の " tag=" で切る。</summary>
    private static (string Entity, string Tag, string Value) SplitTagChange(string s)
    {
        var ti = s.LastIndexOf(" tag=", StringComparison.Ordinal);
        if (ti < 0) return ("", "", "");
        var vi = s.IndexOf(" value=", ti, StringComparison.Ordinal);
        if (vi < 0) return ("", "", "");
        var tag = s.Substring(ti + 5, vi - ti - 5);
        var vStart = vi + 7;
        var vEnd = s.IndexOf(' ', vStart);
        var value = vEnd < 0 ? s[vStart..] : s[vStart..vEnd];
        return (s[..ti], tag, value);
    }

    private static string? Between(string s, string start, string end)
    {
        var i = s.IndexOf(start, StringComparison.Ordinal);
        if (i < 0) return null;
        i += start.Length;
        var j = s.IndexOf(end, i, StringComparison.Ordinal);
        return j < 0 ? s[i..] : s[i..j];
    }

    private static string? After(string s, string start)
    {
        var i = s.LastIndexOf(start, StringComparison.Ordinal);
        if (i < 0) return null;
        var rest = s[(i + start.Length)..];
        var sp = rest.IndexOf(' ');
        return sp < 0 ? rest : rest[..sp];
    }

    private static int IntAfter(string s, string key)
    {
        var i = s.IndexOf(key, StringComparison.Ordinal);
        if (i < 0) return 0;
        i += key.Length;
        var j = i;
        while (j < s.Length && char.IsDigit(s[j])) j++;
        return int.TryParse(s.AsSpan(i, j - i), out var v) ? v : 0;
    }

    private void Raise(Action<PowerGameListener> action)
    {
        foreach (var l in _listeners)
        {
            try { action(l); }
            catch (Exception e)
            {
                // 受け手の例外で読み取りを止めない（記録アプリが落ちて Hearthstone に影響しないように）
                _log.Error($"{l.GetType().Name} で例外", e);
            }
        }
    }

    private void WarnOnce(string key, string message)
    {
        if (_warned.Add(key)) _log.Warn(message);
    }
}
