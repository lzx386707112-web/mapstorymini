using System.Collections.Generic;
using MapleDash.Wz;
using UnityEngine;

// 原始技能各自的召唤素材与攻击数据；对象、位置、动画、攻击周期独立于玩家。
public sealed class NativeSummon : MonoBehaviour
{
    SkillSystem skills;
    PlayerCtrl owner;
    SkillDef skill;
    SpriteRenderer body;
    ImgNode animations;
    List<ImgNode> frames;
    string action;
    int frame, facing;
    float frameUntil, attackAt;
    public Vector2 Position => transform.position;

    public static ImgNode Root(int id, int level)
    {
        var root = WzAssets.FileRoot("Skill/" + (id / 10000).ToString("D3") + ".img")
            .Get("skill/" + id.ToString("D7"));
        return root?.Get("level/" + level + "/summon") ?? root?.Get("summon");
    }
    // StatEffect.getSummonMovementType; stationary puppets must not follow or
    // borrow another summon’s attack. Circle/FOLLOW motion remains local policy.
    public static bool Stationary(int id) => id == 3111002 || id == 3211002 || id == 13111004 ||
        id == 5211001 || id == 5220002 || id == 2321031;
    public static ImgNode AttackInfo(NativeSkillDefinition skill) => Root(skill.Id, skill.Level)?.Get("attack1/info");
    public static Rect? AttackRange(NativeSkillDefinition skill) => NativeMobController.Range(AttackInfo(skill));

    public static NativeSummon Spawn(SkillSystem skills, PlayerCtrl owner, SkillDef skill)
    {
        var actor = new GameObject("Summon_" + skill.native.Id).AddComponent<NativeSummon>();
        actor.skills = skills; actor.owner = owner; actor.skill = skill;
        actor.transform.position = owner.FeetPos; actor.facing = owner.facing;
        actor.animations = Root(skill.native.Id, skill.native.Level);
        actor.body = actor.gameObject.AddComponent<SpriteRenderer>(); actor.body.sortingOrder = owner.SortingOrder - 1;
        actor.Play(actor.Has("summoned") ? "summoned" : "stand");
        actor.attackAt = Time.unscaledTime + actor.Duration("summoned");
        return actor;
    }
    float Duration(string name)
    {
        float duration = 0;
        foreach (var f in WzAssets.Frames(animations?.Get(name))) duration += Mathf.Max(.001f, Mathf.Abs(f.Resolve().Int("delay",100))/1000f);
        return duration;
    }
    bool Has(string name) => WzAssets.Frames(animations?.Get(name)).Count > 0;
    void Play(string name)
    {
        if (action == name) return;
        if (!Has(name)) name = "stand";
        if (!Has(name)) return;
        frames = WzAssets.Frames(animations.Get(name));
        action = name; frame = 0; ShowFrame();
    }
    void ShowFrame()
    {
        var native = frames[frame].Resolve();
        body.sprite = WzAssets.Sprite(native);
        body.flipX = facing > 0;
        frameUntil = Time.unscaledTime + Mathf.Max(.001f, Mathf.Abs(native.Int("delay",100))/1000f);
    }
    void Update()
    {
        if (owner == null || owner.hp <= 0 || !owner.isActiveAndEnabled || skills == null ||
            GameLoop.I == null || GameLoop.I.gameOver || GameLoop.I.state != GameLoop.GameState.Playing || !NativeBuffs.For(owner).HasSource(skill.native.Id))
        { gameObject.SetActive(false); Destroy(gameObject); return; }
        if (Time.unscaledTime >= frameUntil)
        {
            frame++;
            if (frames == null) return;
            if (frame >= frames.Count)
            {
                if (action == "attack1" || action == "summoned") { action = null; Play("stand"); }
                else { frame = 0; ShowFrame(); }
            }
            else ShowFrame();
        }
        if (action == "summoned" || action == "attack1") return;
        // FOLLOW 的精确 EXE 速度/停距未在服务端定义；本地沿用主人步速，保留独立位置。
        if (!Stationary(skill.native.Id))
        {
            Vector2 goal = owner.FeetPos + Vector2.left * owner.facing * (48f/Art.PPU);
            bool moving = Vector2.Distance(Position,goal) > .1f;
            transform.position = Vector2.MoveTowards(Position,goal,owner.moveSpeed*Time.deltaTime);
            facing = goal.x >= Position.x ? 1 : -1;
            Play(moving ? (Has("fly") ? "fly" : "move") : "stand");
        }
        if (!Has("attack1") || !AttackRange(skill.native).HasValue) return;
        if (Time.unscaledTime < attackAt) return;
        // 先在召唤物自己的攻击框内查敌，朝最近敌人；不借主人位置做范围判定。
        Component nearest = null; float distance = float.MaxValue;
        foreach (var e in EnemyUnit.All)
            if (!e.dead && ((Vector2)e.transform.position-Position).sqrMagnitude < distance)
            { nearest = e; distance = ((Vector2)e.transform.position-Position).sqrMagnitude; }
        foreach (var b in FindObjectsOfType<BossUnit>())
            if (!b.dead && ((Vector2)b.transform.position-Position).sqrMagnitude < distance)
            { nearest = b; distance = ((Vector2)b.transform.position-Position).sqrMagnitude; }
        if (nearest != null) facing = nearest.transform.position.x >= Position.x ? 1 : -1;
        if (skills.HitAt(skill,skill.native,Position,facing) > 0)
        {
            Play("attack1");
            attackAt = Time.unscaledTime + Duration("attack1") + AttackInfo(skill.native).Int("attackAfter") / 1000f;
        }
        else attackAt = Time.unscaledTime + .1f;
    }
}
