using System.Collections.Generic;
using UnityEngine;

// 原 StatEffect 将天秤天使标为 FOLLOW；对象、位置、动画、攻击周期均独立于玩家。
public sealed class NativeSummon : MonoBehaviour
{
    SkillSystem skills;
    PlayerCtrl owner;
    SkillDef skill;
    SpriteRenderer body;
    Dictionary<string,object> animations;
    string action;
    int frame, facing;
    float frameUntil, attackAt, expires;
    public Vector2 Position => transform.position;

    public static NativeSummon Spawn(SkillSystem skills, PlayerCtrl owner, SkillDef skill)
    {
        var actor = new GameObject("Summon_" + skill.native.Id).AddComponent<NativeSummon>();
        actor.skills = skills; actor.owner = owner; actor.skill = skill;
        actor.transform.position = owner.FeetPos; actor.facing = owner.facing;
        actor.animations = (Dictionary<string,object>)NativeCombatData.Root["summon"];
        actor.body = actor.gameObject.AddComponent<SpriteRenderer>(); actor.body.sortingOrder = owner.SortingOrder - 1;
        actor.expires = Time.unscaledTime + skill.native.Duration;
        actor.Play("summoned");
        actor.attackAt = Time.unscaledTime + actor.Duration("summoned");
        return actor;
    }
    float Duration(string name)
    {
        float duration = 0;
        foreach (Dictionary<string,object> f in (List<object>)animations[name]) duration += Mathf.Max(.001f, Mathf.Abs(WzAssets.AliasNode((string)f["file"]).Int("delay",100))/1000f);
        return duration;
    }
    void Play(string name)
    {
        if (action == name) return;
        action = name; frame = 0; ShowFrame();
    }
    void ShowFrame()
    {
        var frames = (List<object>)animations[action];
        var data = (Dictionary<string,object>)frames[frame];
        var native = WzAssets.AliasNode((string)data["file"]);
        body.sprite = WzAssets.Sprite(native);
        body.flipX = facing > 0;
        frameUntil = Time.unscaledTime + Mathf.Max(.001f, Mathf.Abs(native.Int("delay",100))/1000f);
    }
    void Update()
    {
        if (owner == null || owner.hp <= 0 || !owner.isActiveAndEnabled || skills == null ||
            GameLoop.I == null || GameLoop.I.gameOver || GameLoop.I.state != GameLoop.GameState.Playing || Time.unscaledTime >= expires)
        { gameObject.SetActive(false); Destroy(gameObject); return; }
        if (Time.unscaledTime >= frameUntil)
        {
            frame++;
            if (frame >= ((List<object>)animations[action]).Count)
            {
                if (action == "attack1" || action == "summoned") { action = null; Play("stand"); }
                else { frame = 0; ShowFrame(); }
            }
            else ShowFrame();
        }
        if (action == "summoned" || action == "attack1") return;
        // FOLLOW 的精确 EXE 速度/停距未在服务端定义；本地沿用主人步速，保留独立位置。
        Vector2 goal = owner.FeetPos + Vector2.left * owner.facing * (48f/Art.PPU);
        bool moving = Vector2.Distance(Position,goal) > .1f;
        transform.position = Vector2.MoveTowards(Position,goal,owner.moveSpeed*Time.deltaTime);
        facing = goal.x >= Position.x ? 1 : -1;
        Play(moving ? "fly" : "stand");
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
            attackAt = Time.unscaledTime + Duration("attack1") + Art.Num((Dictionary<string,object>)animations["info"],"after");
        }
        else attackAt = Time.unscaledTime + .1f;
    }
}
