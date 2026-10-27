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
//
// 特效分工（和原版 Skill.wz 的节点一一对应）：
//   cast / prepare  在角色身上播（SkillFx）
//   ball            弹道本体，由 SkillVisuals 推进到目标
//   hit / mob       落在被打到的目标身上（以前只有一个通用黄色爆点，那是占位）
//   special / tile  分波次推进、砸地后停留的追加部件
public partial class SkillSystem : MonoBehaviour
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
    // 快捷格/技能窗按技能 id 查冷却
    public float RemainingById(int id)
        => states.TryGetValue(id, out var st) ? (float)st.Remaining(Time.realtimeSinceStartupAsDouble) : 0f;
    public float CooldownOf(int id)
    {
        var data = SkillBook.Get(id);
        if (data == null) return 0f;
        int lv = Mathf.Max(1, SkillProgress.LevelOf(id));
        var native = SkillBook.Native(id, lv);
        return native != null ? native.Cooldown : 0f;
    }
    // 主角表现重建不代表新建角色：大厅往返不能绕过原角色的冷却。
    public void BindCooldowns(Dictionary<int, SkillRuntimeState> characterStates)
    {
        states = characterStates;
        foreach (var definition in definitions)
            if (!states.ContainsKey(definition.native.Id)) states.Add(definition.native.Id, new SkillRuntimeState());
    }
    public string LastFailure { get; private set; }
    bool Reject(string reason) { LastFailure = reason; return false; }

    // 面板/快捷格的即时提示，HUD 顶部读 ActiveNotice 显示。
    string notice;
    float noticeUntil;
    public void Report(string text) { notice = text; noticeUntil = Time.unscaledTime + 1.8f; }
    public string ActiveNotice => Time.unscaledTime < noticeUntil ? notice : null;

    SkillRuntimeState State(int id)
    {
        if (!states.TryGetValue(id, out var st)) { st = new SkillRuntimeState(); states[id] = st; }
        return st;
    }

    // 四格旧入口（大厅预览、自动化测试仍然用它）
    public bool TryCast(int slot)
    {
        LastFailure = null;
        if (slot < 0 || slot >= definitions.Count || p == null) return Reject("技能不可用");
        return BeginCast(definitions[slot]);
    }

    // 技能窗按「原版技能 id」施放：等级取角色已学等级，数值取 WZ 该等级。
    public bool TryCastSkill(int skillId)
    {
        LastFailure = null;
        var data = SkillBook.Get(skillId);
        if (data == null) return Reject("缺少技能数据");
        int level = SkillProgress.LevelOf(skillId);
        if (level <= 0) return Reject("技能尚未学习（技能窗里加点）");
        if (data.IsPassive) return Reject("被动技能无需施放");
        var native = SkillBook.Native(skillId, level);
        if (native == null) return Reject("技能等级数据缺失");
        var def = new SkillDef(new ClassSkill { name = data.name, art = "", icon = "" }, native);
        return BeginCast(def);
    }

    // 原版释放流程：校验 → 扣蓝 → 播放施法帧/特效 → 按 lt/rb 判定 → 结算伤害。
    // 没有 WZ 特效部件的技能（206 个里大部分）走同一条判定，只是不播特效。
    bool BeginCast(SkillDef s)
    {
        LastFailure = null;
        var game = GameLoop.I;
        if (p == null || p.hp <= 0 || game == null || game.state != GameLoop.GameState.Playing || game.gameOver)
            return Reject("当前无法释放技能");
        if (p.IsClimbing) return Reject("攀爬时无法释放技能");
        if (p.GetComponent<NativeMobDebuffs>()?.BlocksSkill == true) return Reject("异常状态下无法释放技能");
        if (Time.unscaledTime < actionUntil) return Reject("正在释放技能");
        if (s.native == null) return Reject("技能等级数据缺失");
        if (s.native.Kind == "Passive") return Reject("被动技能无需施放");
        bool frenzy=s.native.Id==4121023&&fourSeasonsFrenzy;
        if (!frenzy && State(s.native.Id).Remaining(Time.realtimeSinceStartupAsDouble) > 0) return Reject("技能冷却中");
        // 原端 TryUseSkill：MP 不够直接拒绝，扣蓝在施法成功之后。
        // mpCon 来自 Skill.wz 对应等级的 mpCon（见 NativeSkillDefinition.MpCost），不是本地估值。
        if (p.mp < s.native.MpCost) return Reject("魔力不足（需要 " + s.native.MpCost + "）");
        if (!frenzy && !State(s.native.Id).TryStart(Time.realtimeSinceStartupAsDouble, s.cd)) return Reject("技能冷却中");
        p.TrySpendMp(s.native.MpCost);
        NativeSkillVideo.PlaySkill(s.native.Id, p);

        bool hasArt = !string.IsNullOrEmpty(s.art);
        float preparation = hasArt ? Preparation(s) : 0f;
        if (hasArt)
        {
            p.PlaySkill(preparation > 0 ? s.art + "_prepare" : s.art, preparation);
            // 无冷却技能仍受角色动作时长限制，不允许一帧多次施法。
            actionUntil = Time.unscaledTime + Mathf.Max(p.SkillActionDuration, preparation + p.GetSkillActionDuration(s.art));
            BeginVisuals(s);
        }
        else
        {
            // 没有特效部件的技能：给一段挥动时长，避免连点一帧打十下
            actionUntil = Time.unscaledTime + Mathf.Max(p.SkillActionDuration, 0.32f);
        }

        bool replay = BeginReplay(s, p.FeetPos, p.facing);
        if (s.native.Kind == "Buff") p.AddAttackBonus(s.native.AttackBuff, s.native.Duration);
        else if (s.native.Kind == "Summon")
        {
            if (summon != null) { summon.gameObject.SetActive(false); Destroy(summon.gameObject); }
            summon = NativeSummon.Spawn(this, p, s);
        }

        // 施法音效（源端 Sound/Skill.img/<技能ID>/Use；原端没配音的技能不发声）
        AudioManager.Play("Skill/" + s.native.Id + "/Use", 0.9f);

        if (replay) return true;
        if (s.art == "dash") StartCoroutine(Rush(s));
        else StartCoroutine(Cast(s,p.FeetPos,p.facing));
        return true;
    }

    // 特效由原资源目录驱动；判定仍保留原端剑影子技能时间轴。
    IEnumerator Cast(SkillDef s,Vector2 origin,int facing)
    {
        float preparation = Preparation(s);
        if (preparation > 0) yield return new WaitForSeconds(preparation);
        if (s.native.Kind == "Buff" || s.native.Kind == "Summon") yield break;
        if (UsesProjectile(s))
        {
            yield return ProjectileCast(s,origin,facing);
            yield break;
        }
        float delay = s.art == "doom" ? NativeSkillVfx.Get(s.art,"effect").Duration*.5f :
            s.art == "firebolt" ? .45f : s.art == "arrowrain" ? .5f : s.art == "starfall" ? NativeSkillVfx.Number(s.art,"hit/0","hitAfter",1800)/1000 :
            (SkillFxPlan.Get(s.art)?.impact ?? 0);
        if (delay > 0) yield return new WaitForSeconds(delay);
        HitAt(s,s.native,origin,facing);
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

    internal int HitAt(SkillDef s, NativeSkillDefinition d, Vector2 origin, int facing,
                       HashSet<int> struck = null, Component onlyTarget = null)
    {
        if (d == null || p == null || p.hp <= 0 || GameLoop.I == null || GameLoop.I.gameOver ||
            GameLoop.I.state != GameLoop.GameState.Playing) return 0;
        if (p.GetComponent<NativeMobDebuffs>()?.BlocksSkill == true) return 0;
        var targets = new List<Component>();
        foreach (var b in FindObjectsOfType<BossUnit>()) if (!b.dead && (onlyTarget == b || (onlyTarget == null && InRange(b.transform.position, d, origin, facing)))) targets.Add(b);
        foreach (var e in EnemyUnit.All) if (!e.dead && (onlyTarget == e || (onlyTarget == null && InRange(e.transform.position, d, origin, facing)))) targets.Add(e);
        targets.Sort((a, b) =>
        {
            if (s.art == "illusion" && (a is BossUnit) != (b is BossUnit)) return a is BossUnit ? -1 : 1;
            return (a.transform.position - (Vector3)origin).sqrMagnitude.CompareTo((b.transform.position - (Vector3)origin).sqrMagnitude);
        });
        targets.RemoveAll(t => struck != null && struck.Contains(t.GetInstanceID()));
        int count = d.Kind == "Summon" ? NativeCombatData.Int((Dictionary<string,object>)((Dictionary<string,object>)NativeCombatData.Root["summon"])["info"],"count",1) : d.TargetCount;
        if (struck != null) count = Mathf.Max(0,count-struck.Count);
        int hits = Mathf.Min(count, targets.Count);
        var captured=new List<CapturedReplayHit>();
        for (int i = 0; i < hits; i++)
        {
            var e = targets[i] as EnemyUnit;
            var b = targets[i] as BossUnit;
            int direction = onlyTarget != null ? facing : targets[i].transform.position.x >= origin.x ? 1 : -1;
            struck?.Add(targets[i].GetInstanceID());
            if (d.Kind == "Dispel")
            {
                if (e != null && Random.value < d.Probability)
                {
                    e.SealSkills(d.Duration);
                    foreach (var clip in NativeSkillVfx.Parts(s.art,"mob")) Visual(s.art,clip.path,TargetAnchor(e,NativeSkillVfx.Number(s.art,clip.path,"pos")),direction,e.transform);
                }
                continue;
            }
            if (d.Kind == "Pull")
            {
                if (e != null && Random.value < d.Probability) e.PullTowards(p.worldX);
                continue;
            }
            bool landed = false;
            var rawHits=new List<DamageResult>();
            for (int hit = 0; hit < d.HitCount; hit++)
            {
                var result = p.RollAttack(e != null ? e.CombatStats : b.CombatStats, d);
                rawHits.Add(result);
                landed |= result.Amount > 0;
                // 技能自带原版 hit 部件时，关掉占位的黄色柔光爆点
                if (e != null) e.TakeHit(result, direction, true, d.Magic);
                else if (b != null) b.TakeHit(result, direction, true);
            }
            Impact(s,d,targets[i],direction,landed);
            captured.Add(new CapturedReplayHit{Target=targets[i],Hits=rawHits.ToArray()});
            if (e != null)
            {
                if ((s.art == "roar" || s.art == "chain") && Random.value < d.Probability)
                {
                    float duration = d.Duration * (s.art == "chain" ? 2 : 1);
                    e.Stun(duration);
                    foreach (var clip in NativeSkillVfx.Parts(s.art,"mob"))
                    {
                        var status = Visual(s.art,clip.path,TargetAnchor(e,NativeSkillVfx.Number(s.art,clip.path,"pos")),direction,e.transform,
                            NativeSkillVfx.Number(s.art,clip.path,"repeat")>0);
                        if (status != null && NativeSkillVfx.Number(s.art,clip.path,"repeat")>0) Destroy(status.gameObject,duration);
                    }
                }
                if (s.art == "dragon" || s.art == "dash") e.Push(direction, d);
            }
        }
        if(d.Id==s.native.Id && d.Kind!="Summon")TriggerReactiveSkills(s,captured,origin,facing);
        return hits;
    }

    // 换地图/死亡取消飞行、延迟命中和召唤；冷却保留在角色状态中，不写回配置。
    public void CancelActions()
    {
        StopAllCoroutines();
        spearCombo.Reset();
        ClearNativeReplayEffects();
        NativeSkillVideo.Stop(NativeSkillVideo.Channel.PlayerSkill);
        ClearVisuals();
        if (summon != null) { summon.gameObject.SetActive(false); Destroy(summon.gameObject); }
        summon = null; actionUntil = 0;
        if (p != null) p.CancelRush();
    }
    void OnDisable() => CancelActions();
}
