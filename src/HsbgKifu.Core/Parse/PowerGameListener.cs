using HearthDb.Enums;

namespace HsbgKifu.Parse;

/// <summary>タグがどの行で変わったか。</summary>
public enum TagChangeKind
{
    /// <summary>FULL_ENTITY / CREATE_GAME（GameEntity・Player）の下の <c>tag=</c> 行（作られたときの値）。</summary>
    Creation,
    /// <summary>SHOW_ENTITY の下（見えるようになった）。</summary>
    Show,
    /// <summary>CHANGE_ENTITY の下（別のカードに変わった）。</summary>
    Change,
    /// <summary>HIDE_ENTITY（見えなくなった。ZONE だけ）。</summary>
    Hide,
    /// <summary>TAG_CHANGE。</summary>
    TagChange,
}

/// <summary>
/// <see cref="PowerLogParser"/> が知らせる出来事。行を状態に当てはめた<b>後</b>に呼ぶので、受け手は <see cref="PowerGame"/> の今の状態を読める。
/// HDT の GameEvents との対応は docs/research.md §3。使うものだけ上書きする。
/// </summary>
public abstract class PowerGameListener
{
    /// <summary>CREATE_GAME（HDT の OnGameStart）。この時点では Entity はまだ無い。</summary>
    public virtual void OnGameCreated(PowerGame game) { }

    /// <summary>CREATE_GAME の下の Entity（GameEntity・プレイヤー・再接続ならその時点の全 Entity）を当てはめ終えた。</summary>
    public virtual void OnGameSetupDone(PowerGame game) { }

    /// <summary>自分の PlayerID が分かった。</summary>
    public virtual void OnLocalPlayerKnown(PowerGame game, int playerId) { }

    /// <summary>FULL_ENTITY（作られた）/ SHOW_ENTITY / CHANGE_ENTITY の行と、その下のタグを当てはめ終えた。</summary>
    public virtual void OnEntityUpdated(PowerGame game, Entity entity, TagChangeKind kind) { }

    /// <summary>タグの値が変わった（同じ値の上書きでは呼ばない）。</summary>
    public virtual void OnTagChanged(PowerGame game, Entity entity, int tag, int previous, int value, TagChangeKind kind) { }

    /// <summary>
    /// GameEntity の TURN が変わった（TAG_CHANGE）。BG では奇数 = ショップの始まり（HDT の OnTurnStart(Player)）、
    /// 偶数 = 戦闘のターンの始まり（OnTurnStart(Opponent)。相手はまだ Bob で、盤面は無い）。
    /// </summary>
    public virtual void OnTurnChanged(PowerGame game, int gameEntityTurn) { }

    /// <summary>
    /// 戦闘が始まった: タグ 3533（combat setup）または 2022（ソロの setup）が 1 → 0（HDT の IsBattlegroundsCombatPhase = true。
    /// HDT はこの 3533 で相手の盤面を保存する）。1 回の戦闘で両方来ることがある。
    /// </summary>
    public virtual void OnCombatSetupEnded(PowerGame game, int tag) { }

    /// <summary>
    /// PREDAMAGE が 0 より大きくなった（HDT の OnEntityWillTakeDamage）。1 回の攻撃で「値 → 0 → 値 → 0」と 2 回立つので、
    /// ダメージを数えるなら <see cref="OnDamage"/> を使う。
    /// </summary>
    public virtual void OnPredamage(PowerGame game, Entity entity, int value) { }

    /// <summary>ダメージが入った（<c>META_DATA - Meta=DAMAGE Data=N</c> と、受けた Entity の <c>Info[i]</c>）。1 回のダメージに 1 回。</summary>
    public virtual void OnDamage(PowerGame game, Entity entity, int amount) { }

    /// <summary>選択肢が出た（ヒーロー選択は ChoiceType=MULLIGAN）。</summary>
    public virtual void OnChoices(PowerGame game, EntityChoice choice) { }

    /// <summary>選択肢を選び終えた（DebugPrintEntitiesChosen）。</summary>
    public virtual void OnChoiceMade(PowerGame game, EntityChoice choice) { }

    /// <summary>
    /// 観測の区切り。プラグインが HDT の OnUpdate（約 100 ms ごと）でしていた差分の観測をここでする。行の途中の、タグが揃っていない状態では呼ばない。
    /// <paramref name="packetEnd"/> = true: サーバーから届いた 1 回分の行のまとまり（DebugPrintPowerList）の終わり。プレイヤーの操作 1 回分に当たる
    /// （ショップの行動はこの区切りで数える。途中で数えると、1 回の購入でゴールドのタグが片方だけ変わった状態を見てしまう）。
    /// false: まとまりの中の各ブロックの終わり（BLOCK_END）。戦闘は 1 回のまとまりで全部届くので、戦闘中の強化や神祇はこの区切りでも読む
    /// （HDT は演出に合わせて Entity を更新していたので、戦闘の途中の値を見ていた）。
    /// </summary>
    public virtual void OnBatchEnd(PowerGame game, bool packetEnd) { }

    /// <summary>
    /// BLOCK_START。<paramref name="target"/> は <c>Target=</c> の Entity（無ければ null）。
    /// ショップで自分が出す PLAY ブロック（購入・売却・リロールのボタン、手札のカード）はプレイヤーの操作そのもので、購入・売却では対象のミニオンが Target に入る。
    /// </summary>
    public virtual void OnBlockStart(PowerGame game, string blockType, Entity? source, Entity? target) { }
    public virtual void OnBlockEnd(PowerGame game) { }

    /// <summary>GameEntity の STATE が COMPLETE になった（HDT の OnGameEnd）。</summary>
    public virtual void OnGameEnded(PowerGame game) { }
}
