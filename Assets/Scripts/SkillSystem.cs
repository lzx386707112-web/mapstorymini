using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class SkillDef
{
    public readonly string name, icon, art, hint, action;
    string visualKey;
    public string vfx => visualKey ?? (visualKey = NativeSkillVfx.Key(native.Id, native.Level, art));
    public readonly NativeSkillDefinition native;
    public float cd => native.Cooldown;
    public SkillDef(ClassSkill presentation, NativeSkillDefinition definition)
    {
        name = presentation.name; art = presentation.art;
        icon = string.IsNullOrEmpty(presentation.icon) ? art : presentation.icon;
        native = definition;
        action = native.Id + "@" + native.Level;
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
    readonly Dictionary<int,NativeSummon> summons = new Dictionary<int,NativeSummon>();

    void Awake()
    {
        p = GetComponent<PlayerCtrl>();
        NativeBuffs.For(p).Removed += RemoveBuffSummon;
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
    // 特殊核心的「剩余冷却时间减少 N%」（回归 30% / 没有结束的攻击 20% / 忍耐II 20% / 耐性I 25%）。
    // 只压剩余量，已经转好的技能不动；源端同样是 undefined cooldown 的技能不参与。
    public void ReduceCooldowns(int percent)
    {
        if (percent <= 0) return;
        double now = Time.realtimeSinceStartupAsDouble;
        foreach (var state in states.Values) state.Reduce(now, percent);
    }
    public float CooldownOf(int id)    {
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
        if(NativeSoulWeapon.IsSkill(skillId)){string failure=NativeSoulWeapon.Cast(skillId);return failure==null || Reject(failure);}
        var data = SkillBook.Get(skillId);
        if (data == null) return Reject("缺少技能数据");
        int level = SkillProgress.LevelOf(skillId);
        if (level <= 0) return Reject("技能尚未学习（技能窗里加点）");
        if (NativeMount.IsSkill(skillId))
        {
            if(p==null || p.hp<=0 || GameLoop.I?.state!=GameLoop.GameState.Playing || GameLoop.I.gameOver)
                return Reject("当前无法骑乘");
            string failure=NativeMount.Toggle(p,skillId,level);
            return failure==null || Reject(failure);
        }
        if(skillId==1016)
        {
            if(p==null || p.hp<=0 || GameLoop.I?.field==null) return Reject("当前无法放置轮回碑石。");
            if(Profile.EquipOfSlot(-53)!=1189999) return Reject("请先装备轮回碑石。");
            if(State(1016).Remaining(Time.realtimeSinceStartupAsDouble)>0) return Reject("技能冷却中");
            string failure=GameLoop.I.field.ActivateFrenzy(p);
            if(failure!=null) return Reject(failure);
            State(1016).TryStart(Time.realtimeSinceStartupAsDouble,1200);
            p.PlayNativeSkill(1016); return true;
        }
        // 魔幻箭筒VI（3141005）是 passive 形态的「箭镞切换」开关：
        // 使用一次切到下一支箭镞并播对应 mode/N，不施法、不耗蓝、无冷却。
        if (NativeArrowMode.IsToggle(skillId))
        {
            if (p == null || p.hp <= 0 || GameLoop.I?.field == null ||
                GameLoop.I.state != GameLoop.GameState.Playing || GameLoop.I.gameOver)
                return Reject("当前无法切换箭镞");
            if (!NativeArrowMode.CooledDown) return Reject("箭镞切换中");
            string label = NativeArrowMode.Switch(p);
            Report(label);
            return true;
        }
        if (data.IsPassive && !InstantSelfSkill(skillId)) return Reject("被动技能无需施放");
        var native = SkillBook.Native(skillId, level);
        if (native == null) return Reject("技能等级数据缺失");
        // Preserve the audited presentation key when entering through an ID binding.
        string art = skillId.ToString();
        foreach (var pair in (Dictionary<string, object>)WzAssets.Index["skills"])
            if (System.Convert.ToInt32(pair.Value) == skillId) { art = pair.Key; break; }
        var def = new SkillDef(new ClassSkill { name = data.name, art = art, icon = data.icon }, native);
        return BeginCast(def);
    }

    // 原版释放流程：校验 → 扣蓝 → 播放施法帧/特效 → 按 lt/rb 判定 → 结算伤害。
    // 视觉分支按技能 ID/等级读取原 IMG；资源为空时仍走同一条施法校验。
    bool BeginCast(SkillDef s,bool continuousShot=false)
    {
        LastFailure = null;
        var game = GameLoop.I;
        if (p == null || p.hp <= 0 || game == null || game.state != GameLoop.GameState.Playing || game.gameOver)
            return Reject("当前无法释放技能");
        if (p.IsClimbing) return Reject("攀爬时无法释放技能");
        if (p.GetComponent<NativeMobDebuffs>()?.BlocksSkill == true && !NativeBuffs.IsHerosWill(s.native?.Id ?? 0)) return Reject("异常状态下无法释放技能");
        if (heldSkill!=null && !continuousShot) return Reject("正在持续释放技能");
        if (!continuousShot && Time.unscaledTime < actionUntil) return Reject("正在释放技能");
        if (s.native == null) return Reject("技能等级数据缺失");
        if (NativeMount.Item(p)>0) return Reject("请先解除骑乘状态");
        if (s.native.Kind == "Passive" && !InstantSelfSkill(s.native.Id)) return Reject("被动技能无需施放");
        if (s.native.Kind=="FlashJump" && !p.CanUseFlashJump(s.native.Id)) return Reject("当前无法使用二段跳");
        Vector2 teleportDestination = default;
        // 原端找不到落点就整条请求不发出：不扣 MP/冷却，也不弹提示。
        // 场景就是竖井图最低平台继续按↓（玩具塔101第2关），表现应当只是「原地不动」。
        if (s.native.Kind=="Teleport" && !p.TeleportDestination(s.native,out teleportDestination))
        { LastFailure = null; return false; }
        bool frenzy=s.native.Id==4121023&&fourSeasonsFrenzy;
        if (!frenzy && State(s.native.Id).Remaining(Time.realtimeSinceStartupAsDouble) > 0) return Reject("技能冷却中");
        // 原端 TryUseSkill：MP 不够直接拒绝，扣蓝在施法成功之后。
        // mpCon 来自 Skill.wz 对应等级的 mpCon（见 NativeSkillDefinition.MpCost），不是本地估值。
        var buffs=NativeBuffs.For(p);
        if(buffs.Value("MORPH")>0 && buffs.Value("MORPH")<100 && (s.native.Kind=="Attack" || s.native.Kind=="Pull")) return Reject("变身状态下无法攻击");
        int mpCost=s.native.MpCost;
        int amplification=Profile.JobId==211 || Profile.JobId==212 ? 2110001 : Profile.JobId==221 || Profile.JobId==222 ? 2210001 :
            Profile.JobId==1211 || Profile.JobId==1212 ? 12110001 : Profile.JobId>=2215 && Profile.JobId<=2218 ? 22150000 : 0;
        int amplificationLevel=SkillProgress.LevelOf(amplification);
        if (amplificationLevel>0) mpCost=(int)(mpCost*(Art.Int(SkillBook.Get(amplification).Level(amplificationLevel),"x")/100.0));
        if (buffs.HasStat("INFINITY")) mpCost=0;
        else mpCost -= (int)(mpCost*(buffs.Value("CONCENTRATE")/100.0));
        if (p.mp < mpCost) return Reject("魔力不足（需要 " + s.native.MpCost + "）");
        // StatEffect.calcHPChange + Character.applyHpMpChange: HP costs
        // cannot reduce the caster to zero; reject before MP/cooldown mutation.
        if (s.native.HpCost > 0 && p.hp <= s.native.HpCost) return Reject("生命不足（需要大于 " + s.native.HpCost + "）");
        var costRow=SkillBook.Get(s.native.Id)?.Level(s.native.Level);
        // 射程口径（IsRangedSkill）与弹药消耗口径（ConsumesAmmo）必须分开：
        // 282 有 69 个远程 VI 技能没有任何发射物层，不该吃箭。
        bool ranged=s.native.Kind=="Attack" && !s.native.Magic && NativeProjectiles.ConsumesAmmo(s.native.Id);
        var projectile=ranged ? NativeProjectiles.For(p,s.native.Id,s.native.Level) : default;
        if (ranged && !projectile.Allowed) return Reject("缺少可用弹药");
        int itemId=Art.Int(costRow,"itemCon"),itemCount=Art.Int(costRow,"itemConNo");
        if (itemCount>0 && Profile.ItemCount(itemId)<itemCount) return Reject("技能所需物品不足");
        int starId=0,starPosition=0,starCount=s.native.Id==4121006 ? Art.Int(costRow,"bulletConsume") : 0;
        if (starCount>0)
        {
            if (GameSession.InGame)
            {
                foreach (var item in GameSession.Character.inventory)
                    if (item.inventoryType==2 && item.position>0 && item.itemId/10000==207 && item.quantity>=starCount &&
                        (starPosition==0 || item.position<starPosition)) { starId=item.itemId; starPosition=item.position; }
            }
            else foreach (int id in Profile.InventoryItems(1))
                if (id/10000==207 && Profile.ItemCount(id)>=starCount) { starId=id; break; }
            if (starId==0) return Reject("技能所需飞镖不足");
        }
        // SkillEffectHandler only broadcasts the key-down start; costs belong to each attack packet.
        // 连射判定：旧表三只四转技能 + 源端 info/rapidAttack（五六转同名技能全靠它）。
        bool rapid = !continuousShot && boundHeldAction.HasValue &&
            (NativeHeldSkill.Continuous(s.native.Id) || NativeSkillVfx.RapidAttack(s.native.Id));
        if (rapid)
        {
            StartHeld(s,boundHeldAction.Value);
            return true;
        }
        // 蓄力型（雷霆万钧VI / 元素之炎VI）：有 keydown 循环层与 keydownend 收手段，
        // 但没有 rapidAttack。按住只累积表现、不逐发结算；松手才走一次正常施法。
        // 挨个塞进连射路径会把蓄力变成每 100ms 打一发，伤害与 MP 全错。
        bool charge = !continuousShot && boundHeldAction.HasValue && !rapid &&
            NativeSkillVfx.HasLayer(s.vfx,"keydown") &&
            NativeSkillVfx.HasLayer(s.vfx,"keydownend");
        if (charge)
        {
            StartHeld(s,boundHeldAction.Value,true);
            return true;
        }
        // StatEffect.applyTo: primary caster consumes itemCon/itemConNo only.
        // All local rejection gates precede the authoritative inventory mutation.
        if (itemCount>0 && !Profile.RemoveItem(itemId,itemCount)) return Reject("技能所需物品扣除失败");
        if (starCount>0 && !Profile.ConsumeProjectile(starId,starCount,starPosition)) return Reject("技能所需飞镖扣除失败");
        if (ranged && !NativeProjectiles.Spend(projectile)) return Reject("弹药扣除失败");
        if (!frenzy && !State(s.native.Id).TryStart(Time.realtimeSinceStartupAsDouble, s.cd)) return Reject("技能冷却中");
        p.TrySpendMp(mpCost);
        float previousHp=p.hp;
        p.hp -= s.native.HpCost;
        NativePetAutopot.OnLoss(p,previousHp,p.mp);
        if (s.native.Id==4111004)
        {
            int money=Art.Int(costRow,"moneyCon");
            if (money>0)
                Profile.Spend(System.Math.Min(Profile.gold,(long)money+Random.Range(0,money/2)));
        }
        if (continuousShot)
        {
            if (UsesProjectile(s)) StartCoroutine(ProjectileCast(s,p.FeetPos,p.facing,true));
            else HitAt(s,s.native,p.FeetPos,p.facing);
            return true;
        }
        ApplyInstantSelf(s.native);
        NativeSkillVideo.PlaySkill(s.native.Id, p);

        if (s.native.Kind=="Teleport")
        {
            NativeImgAnimation.Spawn("Effect/BasicEff.img/Teleport",null,p.FeetPos,p.SortingOrder+1,p.facing>0);
            p.PlaceAt(teleportDestination.x,teleportDestination.y);
            actionUntil=Time.unscaledTime+.12f; // 9580A8: native teleport deadline +120ms.
            AudioManager.Play("Skill/"+s.native.Id+"/Use",0.9f);
            return true;
        }

        if (s.native.Kind=="FlashJump")
        {
            p.ApplyFlashJump(s.native.Level,s.native.Id);
            BeginVisuals(s);
            foreach (var clip in NativeSkillVfx.Parts(s.vfx,"movement"))
                Visual(s.vfx,clip.path,p.FeetPos,p.facing);
            AudioManager.Play("Skill/"+s.native.Id+"/Use",0.9f);
            return true;
        }

        bool hasArt = !string.IsNullOrEmpty(s.art);
        float preparation = hasArt ? Preparation(s) : 0f;
        if (hasArt)
        {
            p.PlaySkill(preparation > 0 ? s.action + "_prepare" : s.action, preparation);
            // 无冷却技能仍受角色动作时长限制，不允许一帧多次施法。
            actionUntil = Time.unscaledTime + (NativeWarriorMelee(s.native.Id) ? p.SkillActionDuration :
                Mathf.Max(p.SkillActionDuration, preparation + p.GetSkillActionDuration(s.action)));
            BeginVisuals(s);
        }
        else
        {
            p.PlayNativeSkill(s.native.Id);
            PlayNativeReplayEffect(s.native.Id, p.FeetPos, p.facing);
            actionUntil = Time.unscaledTime + Mathf.Max(p.SkillActionDuration, 0.32f);
        }

        bool replay = BeginReplay(s, p.FeetPos, p.facing);
        NativeBuffs.For(p).ApplySkill(s.native.Id, s.native.Level, NativeBuffs.Now);
        // 3141009 追加魔幻箭筒：common 只有 time/cooltime，没有任何属性 stat，
        // NativeBuffs.ApplySkill 会因为 stats 为空直接返回 false，所以持续时间自己管。
        if (NativeQuiverBurst.IsSkill(s.native.Id)) NativeQuiverBurst.Activate(s.native.Level);
        // 特殊核心里 cooltime 型（魔王杀手II / 破坏防具II / 自动恢复 / 一击必杀I）靠施法触发。
        NativeSpecialCore.OnSkillUse(p);
        if (s.native.Kind == "Summon")
        {
            RemoveBuffSummon(s.native.Id);
            summons[s.native.Id] = NativeSummon.Spawn(this, p, s);
        }

        // 施法音效（源端 Sound/Skill.img/<技能ID>/Use；原端没配音的技能不发声）
        AudioManager.Play("Skill/" + s.native.Id + "/Use", 0.9f);

        if (replay) return true;
        if (InstantSelfSkill(s.native.Id)) return true;
        if (s.art == "dash") StartCoroutine(Rush(s));
        else StartCoroutine(Cast(s,p.FeetPos,p.facing));
        return true;
    }

    // StatEffect.calcHPChange/calcMPChange and SpecialMoveHandler.MP_RECOVERY.
    // 3141005 魔幻箭筒VI 虽然源端标 passive，但它是可主动按的箭镞切换，不能按被动藏起来。
    public static bool InstantSelfSkill(int id) => id==2301002 || id==5101005 || id==4211001 || id==2311001 || id==5121010 ||
        id==9101000 || NativeBuffs.IsHerosWill(id) || NativeArrowMode.IsToggle(id);
    void ApplyInstantSelf(NativeSkillDefinition d)
    {
        var row=SkillBook.Get(d.Id)?.Level(d.Level);
        if (row==null) return;
        int heal=Art.Int(row,"hp");
        if (heal!=0) p.Heal((int)(p.hpMax*heal/100f),true);
        int mana=Art.Int(row,"mp");
        if (mana!=0) p.mp=Mathf.Min(p.mpMax,p.mp+mana);
        if (d.Id==4211001)
        {
            double rate=Art.Int(row,"y")/100.0;
            int lower=(int)(p.Attributes.luk*2.3*rate),upper=(int)(p.Attributes.luk*3.5*rate);
            p.hp=Mathf.Min(p.hpMax,p.hp+lower+Mathf.FloorToInt(UnityEngine.Random.value*(upper-lower+1)));
        }
        if (d.Id==5101005)
        {
            int divisor=Art.Int(row,"x");
            int loss=divisor>0 ? Mathf.Min((int)p.hp-1,(int)p.hpMax/divisor) : 0;
            p.hp-=loss; p.mp=Mathf.Min(p.mpMax,p.mp+(int)(loss*(Art.Int(row,"y")/100.0)));
        }
        if ((d.Id==2311001 || d.Id==9101000) && UnityEngine.Random.value<d.Probability)
            p.GetComponent<NativeMobDebuffs>()?.Dispel();
        else if (NativeBuffs.IsHerosWill(d.Id)) p.GetComponent<NativeMobDebuffs>()?.Purge();
        if (d.Id==9101000) { p.hp=p.hpMax; p.mp=p.mpMax; }
        if (d.Id==5121010) foreach (var pair in states) if (pair.Key!=5121010) pair.Value.Reset();
        if (d.Id==2311001)
        {
            int affected=0;
            foreach (var enemy in EnemyUnit.All)
            {
                if (enemy.dead || !InRange(enemy.transform.position,d,p.FeetPos,p.facing)) continue;
                enemy.NativeController?.Dispel(d.Id);
                if (++affected>=d.TargetCount) break;
            }
        }
    }

    // 特效由原资源目录驱动；判定仍保留原端剑影子技能时间轴。
    IEnumerator Cast(SkillDef s,Vector2 origin,int facing)
    {
        if (NativeWarriorMelee(s.native.Id))
        {
            // Original close attack captures targets before scheduling each mob's
            // hit, so walking out of the rectangle during windup cannot retarget it.
            var targets = SelectTargets(s, s.native, origin, facing, null, null);
            int count = Mathf.Min(s.native.TargetCount, targets.Count);
            for (int i = 0; i < count; i++)
            {
                int distance = Mathf.RoundToInt(Mathf.Abs(targets[i].transform.position.x - origin.x) * Art.PPU);
                int impactDelay = p.MeleeSkillImpactMs + (int)System.Math.Round(distance * 1.5);
                StartCoroutine(MeleeTarget(s, targets[i], origin, facing, impactDelay / 1000f));
            }
            yield break;
        }
        float preparation = Preparation(s);
        if (preparation > 0) yield return new WaitForSeconds(preparation);
        if (s.native.Kind == "Buff" || s.native.Kind == "Summon") yield break;
        if (UsesProjectile(s))
        {
            yield return ProjectileCast(s,origin,facing);
            yield break;
        }
        float delay = s.art == "doom" ? NativeSkillVfx.Get(s.vfx,"effect").Duration*.5f :
            s.art == "firebolt" ? .45f : s.art == "arrowrain" ? .5f : s.art == "starfall" ? NativeSkillVfx.Number(s.vfx,"hit/0","hitAfter",1800)/1000 :
            (SkillFxPlan.Get(s.art)?.impact ?? 0);
        if (delay > 0) yield return new WaitForSeconds(delay);
        HitAt(s,s.native,origin,facing);
    }

    static bool NativeWarriorMelee(int id) => id == 1001004 || id == 1001005;
    IEnumerator MeleeTarget(SkillDef skill, Component target, Vector2 origin, int facing, float delay)
    {
        if (delay > 0) yield return new WaitForSeconds(delay);
        if (target != null) HitAt(skill, skill.native, origin, facing, null, target);
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
        // 全屏技能（skillType=3：天怒/暴风雪/火流星/终极攻击…）源端没有 lt/rb，
        // 按相机可见宽度判定，否则会退回身前 200px、只能打到贴脸的一只怪。
        if (d.FullScreen)
        {
            var camera = Camera.main;
            if (camera == null) return true;
            float half = camera.orthographicSize * camera.aspect + 1f;
            return Mathf.Abs(position.x - camera.transform.position.x) <= half;
        }
        var delta = (Vector2)position - origin;
        float x = delta.x * facing;
        // WZ 左朝向攻击框，世界坐标 Y 向上。
        Vector2 min = d.Min, max = d.Max;
        if (d.Kind == "Summon")
        {
            var range = NativeSummon.AttackRange(d);
            if (!range.HasValue) return false;
            min = range.Value.min / Art.PPU;
            max = range.Value.max / Art.PPU;
        }
        return x >= -max.x && x <= -min.x && delta.y >= -max.y && delta.y <= -min.y;
    }

    bool InRange(Component target,NativeSkillDefinition d,Vector2 origin,int facing)
    {
        if (InRange(target.transform.position,d,origin,facing)) return true;
        if (d.Kind=="Summon" || !d.HasBounds || !(target is EnemyUnit enemy) || !enemy.SelectionBounds.HasValue) return false;
        float left=facing<0 ? d.Min.x : -d.Max.x;
        float right=facing<0 ? d.Max.x : -d.Min.x;
        var attack=Rect.MinMaxRect(origin.x+left,origin.y-d.Max.y,origin.x+right,origin.y-d.Min.y);
        return attack.Overlaps(enemy.SelectionBounds.Value);
    }

    List<Component> SelectTargets(SkillDef s, NativeSkillDefinition d, Vector2 origin, int facing,
        HashSet<int> struck, Component onlyTarget)
    {
        var targets = new List<Component>();
        foreach (var b in FindObjectsOfType<BossUnit>()) if (!b.dead && (onlyTarget == b || (onlyTarget == null && InRange(b, d, origin, facing)))) targets.Add(b);
        foreach (var e in EnemyUnit.All) if (!e.dead && (onlyTarget == e || (onlyTarget == null && InRange(e, d, origin, facing)))) targets.Add(e);
        targets.Sort((a, b) =>
        {
            if (s.art == "illusion" && (a is BossUnit) != (b is BossUnit)) return a is BossUnit ? -1 : 1;
            return (a.transform.position - (Vector3)origin).sqrMagnitude.CompareTo((b.transform.position - (Vector3)origin).sqrMagnitude);
        });
        targets.RemoveAll(t => struck != null && struck.Contains(t.GetInstanceID()));
        return targets;
    }

    internal int HitAt(SkillDef s, NativeSkillDefinition d, Vector2 origin, int facing,
                       HashSet<int> struck = null, Component onlyTarget = null)
    {
        if (d == null || p == null || p.hp <= 0 || GameLoop.I == null || GameLoop.I.gameOver ||
            GameLoop.I.state != GameLoop.GameState.Playing) return 0;
        if (p.GetComponent<NativeMobDebuffs>()?.BlocksSkill == true) return 0;
        using var profileSaves=Profile.DeferSaves();
        using var databaseSaves=GameServer.Local?.Db.DeferSaves();
        var targets = SelectTargets(s, d, origin, facing, struck, onlyTarget);
        // 起源之塔第 14 层只允许普通攻击：技能包同样要走 handleAttack 才会回「本层只能使用普通攻击」。
        if (SeedTowerCompat.HandleAttack(d.Id, targets.Count > 0 ? targets[0] as EnemyUnit : null)) return 0;
        int count = d.Kind == "Summon" ? NativeSummon.AttackInfo(d)?.Int("mobCount", 1) ?? 1 : d.TargetCount;
        if (struck != null) count = Mathf.Max(0,count-struck.Count);
        int hits = Mathf.Min(count, targets.Count);
        long totalDamage=0;
        var captured=new List<CapturedReplayHit>();
        for (int i = 0; i < hits; i++)
        {
            var e = targets[i] as EnemyUnit;
            var b = targets[i] as BossUnit;
            int direction = onlyTarget != null ? facing : targets[i].transform.position.x >= origin.x ? 1 : -1;
            struck?.Add(targets[i].GetInstanceID());
            // AbstractDealDamageHandler.parseDamage 1042..1044 removes defense
            // buffs for Shadow Meso before resolving this target's damage.
            if (d.Id==4111004) e?.NativeController?.Dispel(d.Id);
            if (d.Kind == "Dispel")
            {
                if (e != null && Random.value < d.Probability)
                {
                    e.SealSkills(d.Duration);
                    e.NativeController?.Dispel(d.Id);
                    foreach (var clip in NativeSkillVfx.Parts(s.vfx,"mob")) Visual(s.vfx,clip.path,TargetAnchor(e,NativeSkillVfx.Number(s.vfx,clip.path,"pos")),direction,e.transform);
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
            }
            // RangedAttackHandler:1375 and AbstractDealDamageHandler:636-640:
            // Taunt is an attack status, not an applyMonsterBuff skill.
            if (e!=null && landed && (d.Id==4121003 || d.Id==4221003) && Random.value<d.Probability)
                e.NativeController?.ApplyShowdown(d.Id,Art.Int(SkillBook.Get(d.Id)?.Level(d.Level),"x"),
                    (long)(d.Duration*1000),e.IsBoss);
            // Settle all original attackCount lines together so lethal first hits
            // cannot discard later lines; each line keeps its own index and Miss.
            if (e != null)
            {
                totalDamage+=e.TakeReplayHit(rawHits.ToArray(), direction, d.Magic, false, 120, false, d.Id);
                int heal=DrainRecovery(d.Id,totalDamage,e.NativeController?.MaximumHp ?? (long)e.hpMax,(int)p.hpMax,
                    Art.Int(SkillBook.Get(d.Id)?.Level(d.Level),"x"));
                // AbstractDealDamageHandler uses addHP, independent of ZOMBIFY/heal skill modifiers.
                if (heal>0) p.hp=Mathf.Min(p.hpMax,p.hp+heal);
                // MagicDamageHandler applies MP Eater to surviving map objects after the attack.
                if (d.Magic && d.Kind!="Summon" && !e.dead && e.NativeController!=null)
                {
                    int eater=(Profile.JobId-Profile.JobId%10)*10000;
                    int level=SkillProgress.LevelOf(eater);
                    if (level>0 && (eater==2100000 || eater==2200000 || eater==2300000))
                    {
                        var row=SkillBook.Get(eater)?.Level(level);
                        if (Random.value<Art.Int(row,"prop",100)/100f)
                            p.mp=Mathf.Min(p.mpMax,p.mp+e.NativeController.AbsorbMp(Art.Int(row,"x")));
                    }
                }
            }
            else if (b != null) b.TakeReplayHit(rawHits.ToArray(), direction, false, 120, false, d.Id);
            Impact(s,d,targets[i],direction,landed);
            captured.Add(new CapturedReplayHit{Target=targets[i],Hits=rawHits.ToArray()});
            if (e != null)
            {
                if ((s.art == "roar" || s.art == "chain") && Random.value < d.Probability)
                {
                    float duration = d.Duration * (s.art == "chain" ? 2 : 1);
                    e.Stun(duration);
                    foreach (var clip in NativeSkillVfx.Parts(s.vfx,"mob"))
                    {
                        var status = Visual(s.vfx,clip.path,TargetAnchor(e,NativeSkillVfx.Number(s.vfx,clip.path,"pos")),direction,e.transform,
                            NativeSkillVfx.Number(s.vfx,clip.path,"repeat")>0);
                        if (status != null && NativeSkillVfx.Number(s.vfx,clip.path,"repeat")>0) Destroy(status.gameObject,duration);
                    }
                }
                if (s.art == "dragon" || s.art == "dash") e.Push(direction, d);
            }
        }
        // 魔幻箭筒（3141005）的箭镞：直接攻击命中后按 u%/w% 掷一次骰，追加魔法箭伤害或吸血回复。
        // 只对主技能生效（子技能/召唤不重复触发），且每次施法只掷一次，不随目标数放大。
        if (d.Id==s.native.Id && d.Kind=="Attack" && hits>0 && p.UsesRangedWeapon)
            NativeArrowMode.OnHit(p, targets[0], targets[0].transform.position.x >= origin.x ? 1 : -1);
        // 特殊核心的攻击型计数：源端按「技能的每次攻击」算 1 次，
        // 所以一次结算要计 attackCount × 命中目标数，而不是只计 1。
        if (d.Id==s.native.Id && d.Kind=="Attack" && hits>0)
        {
            int ticks = Mathf.Max(1, d.HitCount) * hits;
            int targetId = targets[0] != null ? targets[0].GetInstanceID() : 0;
            for (int i=0;i<ticks;i++) NativeSpecialCore.OnAttack(p, targetId);
        }
        // The client captures damage while hidden; the server cancels the state
        // on receiving the attack, so all lines must use the pre-cancellation stats.
        if (d.Kind=="Attack" && !d.Magic)
        {
            if (p.UsesRangedWeapon) p.GetComponent<NativeBuffs>()?.OnRangedAttack(hits);
            else p.GetComponent<NativeBuffs>()?.OnCloseAttack();
        }
        if(d.Id==s.native.Id && d.Kind!="Summon")TriggerReactiveSkills(s,captured,origin,facing);
        return hits;
    }
    public static int DrainRecovery(int skillId,long cumulativeDamage,long monsterMaxHp,int playerMaxHp,int percent)
    {
        if (skillId!=4101005 && skillId!=5111004 && skillId!=15111001 && skillId!=14101006) return 0;
        return (int)System.Math.Max(0,System.Math.Min(monsterMaxHp,System.Math.Min(
            (int)System.Math.Min(int.MaxValue,cumulativeDamage*(percent/100.0)),playerMaxHp/2)));
    }

    // 换地图/死亡取消飞行、延迟命中和召唤；冷却保留在角色状态中，不写回配置。
    public void CancelActions()
    {
        StopHeld(false);
        StopAllCoroutines();
        spearCombo.Reset();
        ClearNativeReplayEffects();
        NativeSkillVideo.Stop(NativeSkillVideo.Channel.PlayerSkill);
        ClearVisuals();
        foreach (var summon in summons.Values)
            if (summon != null) { summon.gameObject.SetActive(false); Destroy(summon.gameObject); }
        summons.Clear(); actionUntil = 0;
        if (p != null) p.CancelRush();
    }
    void RemoveBuffSummon(int id)
    {
        if (!summons.TryGetValue(id,out var actor)) return;
        summons.Remove(id);
        if (actor != null) { actor.gameObject.SetActive(false); Destroy(actor.gameObject); }
    }
    public void RestoreBuffSummons()
    {
        var buffs=GetComponent<NativeBuffs>();
        if (buffs==null) return;
        var effects=new List<NativeBuffs.Effect>(buffs.Visible);
        foreach (var effect in effects)
        {
            if (NativeSummon.Root(effect.Id,effect.Level)==null) continue;
            if (NativeSummon.Stationary(effect.Id)) { buffs.Cancel(effect.Id); continue; }
            var data=SkillBook.Get(effect.Id);
            var skill=new SkillDef(new ClassSkill { name=data.name,art=effect.Id.ToString(),icon=data.icon },SkillBook.Native(effect.Id,effect.Level));
            RemoveBuffSummon(effect.Id);
            summons[effect.Id]=NativeSummon.Spawn(this,p,skill);
        }
    }
    void OnDestroy()
    {
        var buffs=p != null ? p.GetComponent<NativeBuffs>() : null;
        if (buffs != null) buffs.Removed -= RemoveBuffSummon;
    }
    void OnDisable() => CancelActions();
}
