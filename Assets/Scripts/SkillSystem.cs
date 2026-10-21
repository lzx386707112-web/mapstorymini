using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class SkillDef
{
    public readonly string name, icon, art, hint;
    public readonly NativeSkillDefinition native;
    public float cd => native.Cooldown;
    public SkillDef(ClassSkill presentation, NativeSkillDefinition definition)
    {
        name = presentation.name; art = presentation.art;
        icon = string.IsNullOrEmpty(presentation.icon) ? art : presentation.icon;
        native = definition;
        hint = "Lv." + native.Level;
    }
}

// 配置只读，冷却每个角色独立；Fire 不是对外入口，UI 无法绕过施法校验。
public class SkillSystem : MonoBehaviour
{
    public IReadOnlyList<SkillDef> skills => definitions.AsReadOnly();
    readonly List<SkillDef> definitions = new List<SkillDef>();
    Dictionary<int, SkillRuntimeState> states = new Dictionary<int, SkillRuntimeState>();
    PlayerCtrl p;
    float actionUntil;
    NativeSummon summon;

    void Awake()
    {
        p = GetComponent<PlayerCtrl>();
        foreach (var slot in ClassDefs.Get(Profile.cls).skills)
        {
            var native = NativeSkillCatalog.Get(slot.art);
            if (native == null) throw new System.InvalidOperationException("缺少技能等级数据：" + slot.art);
            definitions.Add(new SkillDef(slot, native));
            if (!states.ContainsKey(native.Id)) states.Add(native.Id, new SkillRuntimeState());
        }
    }

    public float Remaining(int slot) => (float)states[definitions[slot].native.Id].Remaining(Time.realtimeSinceStartupAsDouble);
    // 主角表现重建不代表新建角色：大厅往返不能绕过原角色的冷却。
    public void BindCooldowns(Dictionary<int, SkillRuntimeState> characterStates)
    {
        states = characterStates;
        foreach (var definition in definitions)
            if (!states.ContainsKey(definition.native.Id)) states.Add(definition.native.Id, new SkillRuntimeState());
    }
    public string LastFailure { get; private set; }
    bool Reject(string reason) { LastFailure = reason; return false; }

    public bool TryCast(int slot)
    {
        LastFailure = null;
        var game = GameLoop.I;
        if (slot < 0 || slot >= definitions.Count || p == null) return Reject("技能不可用");
        if (p.hp <= 0 || game == null || game.state != GameLoop.GameState.Playing || game.gameOver) return Reject("当前无法释放技能");
        if (p.IsClimbing) return Reject("攀爬时无法释放技能");
        if (Time.unscaledTime < actionUntil) return Reject("正在释放技能");
        var s = definitions[slot];
        if (Remaining(slot) > 0) return Reject("技能冷却中");
        if (!states[s.native.Id].TryStart(Time.realtimeSinceStartupAsDouble, s.cd)) return Reject("技能冷却中");
        // 当前单机触屏模式按用户选择暂不消耗 MP；原 MpCost 保留在只读数据中。
        p.PlaySkill(s.art);
        // 无冷却技能仍受角色动作时长限制，不允许一帧多次施法。
        actionUntil = Time.unscaledTime + p.SkillActionDuration;
        if (s.native.Kind == "Buff") p.AddAttackBonus(s.native.AttackBuff, s.native.Duration);
        else if (s.native.Kind == "Summon")
        {
            if (summon != null) { summon.gameObject.SetActive(false); Destroy(summon.gameObject); }
            summon = NativeSummon.Spawn(this, p, s);
        }
        else if (s.art == "dash") StartCoroutine(Rush(s));
        else StartCoroutine(Cast(s));
        if (!string.IsNullOrEmpty(s.art)) SkillFx.Play(s.art, p.FeetPos, p.facing);
        return true;
    }

    IEnumerator Cast(SkillDef s)
    {
        if (s.art == "rupture") yield return new WaitForSecondsRealtime(1f);
        if (s.art == "illusion")
        {
            // CloseRangeDamageHandler.SWORD_ILLUSION_*_TIMES_MS；子技能使用独立参数。
            int[] times = {1320,1470,1590,1710,1800,1950,2070,2190,2280,2430,2550,2670,2790,2820,2880,2940,3000};
            int previous = 0;
            foreach (int at in times)
            {
                yield return new WaitForSecondsRealtime((at - previous) / 1000f);
                previous = at;
                Hit(s, NativeSkillCatalog.Get(at <= 2670 ? "illusion_slash" : "illusion_explosion"));
            }
        }
        else Hit(s, s.native);
    }

    IEnumerator Rush(SkillDef s)
    {
        p.BeginRush(s.native, p.SkillActionDuration);
        var struck = new HashSet<int>();
        HitAt(s,s.native,p.FeetPos,p.facing,struck);
        while (p.IsRushing)
        {
            yield return null;
            HitAt(s,s.native,p.FeetPos,p.facing,struck);
        }
    }

    bool InRange(Vector3 position, NativeSkillDefinition d, Vector2 origin, int facing)
    {
        var delta = (Vector2)position - origin;
        float x = delta.x * facing;
        // WZ 左朝向攻击框，世界坐标 Y 向上。
        Vector2 min = d.Min, max = d.Max;
        if (d.Kind == "Summon")
        {
            var info = (Dictionary<string,object>)((Dictionary<string,object>)NativeCombatData.Root["summon"])["info"];
            var lt = (List<object>)info["lt"]; var rb = (List<object>)info["rb"];
            min = new Vector2(System.Convert.ToSingle(lt[0]),System.Convert.ToSingle(lt[1])) / Art.PPU;
            max = new Vector2(System.Convert.ToSingle(rb[0]),System.Convert.ToSingle(rb[1])) / Art.PPU;
        }
        return x >= -max.x && x <= -min.x && delta.y >= -max.y && delta.y <= -min.y;
    }

    void Hit(SkillDef s, NativeSkillDefinition d) => HitAt(s,d,p.FeetPos,p.facing);

    internal int HitAt(SkillDef s, NativeSkillDefinition d, Vector2 origin, int facing, HashSet<int> struck = null)
    {
        if (d == null || p == null || p.hp <= 0 || GameLoop.I == null || GameLoop.I.gameOver ||
            GameLoop.I.state != GameLoop.GameState.Playing) return 0;
        var targets = new List<Component>();
        foreach (var b in FindObjectsOfType<BossUnit>()) if (!b.dead && InRange(b.transform.position, d, origin, facing)) targets.Add(b);
        foreach (var e in EnemyUnit.All) if (!e.dead && InRange(e.transform.position, d, origin, facing)) targets.Add(e);
        targets.Sort((a, b) =>
        {
            if (s.art == "illusion" && (a is BossUnit) != (b is BossUnit)) return a is BossUnit ? -1 : 1;
            return (a.transform.position - (Vector3)origin).sqrMagnitude.CompareTo((b.transform.position - (Vector3)origin).sqrMagnitude);
        });
        targets.RemoveAll(t => struck != null && struck.Contains(t.GetInstanceID()));
        int count = d.Kind == "Summon" ? NativeCombatData.Int((Dictionary<string,object>)((Dictionary<string,object>)NativeCombatData.Root["summon"])["info"],"count",1) : d.TargetCount;
        if (struck != null) count = Mathf.Max(0,count-struck.Count);
        int hits = Mathf.Min(count, targets.Count);
        for (int i = 0; i < hits; i++)
        {
            var e = targets[i] as EnemyUnit;
            var b = targets[i] as BossUnit;
            int direction = targets[i].transform.position.x >= origin.x ? 1 : -1;
            struck?.Add(targets[i].GetInstanceID());
            if (d.Kind == "Dispel")
            {
                if (e != null && Random.value < d.Probability) e.SealSkills(d.Duration);
                continue;
            }
            if (d.Kind == "Pull")
            {
                if (e != null && Random.value < d.Probability) e.PullTowards(p.worldX);
                continue;
            }
            for (int hit = 0; hit < d.HitCount; hit++)
            {
                var result = p.RollAttack(e != null ? e.CombatStats : b.CombatStats, d);
                if (e != null) e.TakeHit(result, direction);
                else if (b != null) b.TakeHit(result, direction);
            }
            if (e != null)
            {
                if ((s.art == "roar" || s.art == "chain") && Random.value < d.Probability)
                    e.Stun(d.Duration * (s.art == "chain" ? 2 : 1));
                if (s.art == "dragon" || s.art == "dash") e.Push(direction, d);
            }
        }
        return hits;
    }

    // 换地图/死亡取消飞行、延迟命中和召唤；冷却保留在角色状态中，不写回配置。
    public void CancelActions()
    {
        StopAllCoroutines();
        if (summon != null) { summon.gameObject.SetActive(false); Destroy(summon.gameObject); }
        summon = null; actionUntil = 0;
        if (p != null) p.CancelRush();
    }
    void OnDisable() => CancelActions();
}
