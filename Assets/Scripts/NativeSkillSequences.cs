using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ——— 282 的「形态技能序列」（母技能开形态，子技能出伤害）———
//
// 上一轮把这三条记成「需要 buff 期间解锁子技能的新机制」。机制其实一直在 282 数据里：
// 母技能节点上挂着 `psdSkill/<子技能id>` + `isSequenceOn=1`，伤害全在**子技能**的
// common 里，母技能只负责「开形态 + 给次数」。本项目没接线，于是按下母技能等于
// 开个空 buff：
//
//   400031055 連射十字弓砲彈 (type=15) → 400031056 極速射擊
//     形态 60 秒、x=8 个弹匣；再按一次母技能 → 消耗 1 个弹匣 + 150 MP，射 4 支箭
//     各 9 段（v=4 支 / u=9 段，与子技能 multiAttackInfo 4 条、attackCount=9 逐项相等）。
//
//   400011055 極樂之境 (type=33) → 400011056 黃泉十字架
//     形态 30 秒、w=30 次；形态期间**普通攻击**被替换成黄泉十字架
//     （desc「在極樂之境中，可以以#c一般攻擊#使用交叉冥河」），每次 5 斩 × 5 段 × 15 名。
//
//   400041025 黑影切斷 (type=33) → 400041026 黑影切斷（成功版）
//     母技能是主动攻击（自身 common 的 y=675+27x 打 10 名 5 段），另外「如果有傷口
//     減益效果到達最大值的敵人，可在致命暗殺的最終攻擊和音速狂襲中連接使用」→ 连携段
//     瞬移到该敌人背后、7 段 × 5 斩、无视免疫/反伤、攻击后 1.8 秒无敌。
//     伤口由 4241000 / 4241001（致命暗殺）与 400041039（音速狂襲）命中时施加。
//
// 数值一律来自 Assets/Resources/NativeSkillSequences.json
// （tools/ms282/export_skill_sequences.py：子技能 common 公式逐级展开 + 母技能 h 自证），
// C# 不复制任何伤害常量。自证规则：子技能字段与母技能 h 里被引用的字段逐级相等，
// 对不上导出脚本直接失败。
//
// 为什么单独一套结算而不用 BeginCast 递归：子技能是 invisible 攻击体，不在
// SkillBook / 技能窗里（LevelOf=0），走 BeginCast 会被「未学习」挡下，还会把母技能
// 的 MP / 冷却 / 弹药口径重复扣一遍。这里只做「扣子技能 MP → 播动作/特效 →
// 按 multiAttackInfo 的轮数逐轮 HitAt」，其余全部复用既有命中管线。
public partial class SkillSystem
{
    enum SeqTrigger { Recast, Basic, Chain }

    sealed class SkillSequence
    {
        public int Parent, Child, ChildMax;
        public int DurationSec, Charges, Repeat;
        // 每次子技能要不要额外扣 MP（0 = 不扣）。具体数值取当前等级的 childLevels.mpCon，
        // 这里只当开关：黑影切断的连携段已经在按下母技能时扣过 650，不能再扣一次。
        public int ChildMp;
        public SeqTrigger Trigger;
        public string ChildName;
        public int[] AttackTimes;
        public bool Magic, ParentAttack;
        public int SkillType, AreaAttack;
        public Dictionary<string, object> Levels;
    }

    sealed class RiftZone
    {
        public int Parent, Child;
        public Dictionary<string, object> Levels;
    }

    sealed class WoundSpec
    {
        public int Parent, MaxStacks, DurationSec;
        public int[] Sources;
    }

    static Dictionary<string, object> sequenceData;
    static List<SkillSequence> sequences;
    static List<WoundSpec> wounds;
    static readonly Dictionary<int, RiftZone> riftZones = new Dictionary<int, RiftZone>();

    // 裂缝实例（极乐之境）：位置 / 已累积层数 / 到期 / 上次生成时刻。不落盘。
    sealed class Rift
    {
        public Vector2 Position;
        public int Stages;
        public double ExpireAt, SpawnedAt;
    }

    readonly List<Rift> rifts = new List<Rift>();
    Coroutine riftTender;
    double lastRiftSpawn;

    // 形态：母技能 id → 到期时刻 / 剩余次数。不落盘，换图或死亡清零。
    readonly Dictionary<int, double> formUntil = new Dictionary<int, double>();
    readonly Dictionary<int, int> formCharges = new Dictionary<int, int>();

    // 伤口：282 的 h 写死「只會對1名敵人留下」，所以是一组单值状态而不是字典。
    Component woundTarget;
    int woundStacks;
    double woundUntil;

    static int SeqInt(Dictionary<string, object> row, string key, int fallback = 0)
        => row != null && row.TryGetValue(key, out var raw) && raw != null ? Convert.ToInt32(raw) : fallback;

    static int[] SeqInts(Dictionary<string, object> row, string key)
    {
        if (row == null || !row.TryGetValue(key, out var raw) || !(raw is List<object> list))
            return new int[0];
        var result = new int[list.Count];
        for (int i = 0; i < list.Count; i++) result[i] = Convert.ToInt32(list[i]);
        return result;
    }

    static void LoadSequences()
    {
        if (sequences != null) return;
        sequences = new List<SkillSequence>();
        wounds = new List<WoundSpec>();
        var text = Resources.Load<TextAsset>("NativeSkillSequences")?.text;
        sequenceData = string.IsNullOrEmpty(text)
            ? new Dictionary<string, object>()
            : (Dictionary<string, object>)MiniJson.Parse(text);
        if (sequenceData.TryGetValue("sequences", out var raw) &&
            raw is Dictionary<string, object> table)
        {
            foreach (var pair in table)
            {
                if (!(pair.Value is Dictionary<string, object> row)) continue;
                var meta = row.TryGetValue("childSkill", out var metaRaw)
                    ? metaRaw as Dictionary<string, object> : null;
                string trigger = row.TryGetValue("trigger", out var trig) ? Convert.ToString(trig) : "";
                sequences.Add(new SkillSequence
                {
                    Parent = SeqInt(row, "parent"),
                    Child = SeqInt(row, "child"),
                    ChildMax = Mathf.Max(1, SeqInt(meta, "maxLevel", 1)),
                    DurationSec = SeqInt(row, "durationSec"),
                    Charges = SeqInt(row, "charges"),
                    Repeat = Mathf.Max(1, SeqInt(row, "repeat", 1)),
                    ChildName = row.TryGetValue("childName", out var name) ? Convert.ToString(name) : "",
                    ChildMp = SeqInt(row, "childMp"),
                    Trigger = trigger == "recast" ? SeqTrigger.Recast
                        : trigger == "basicAttack" ? SeqTrigger.Basic : SeqTrigger.Chain,
                    AttackTimes = SeqInts(row, "attackTimes"),
                    Magic = meta != null && meta.TryGetValue("magic", out var magic) && magic is bool on && on,
                    ParentAttack = row.TryGetValue("parentAttack", out var pa) && pa is bool attackOn && attackOn,
                    SkillType = SeqInt(meta, "skillType"),
                    AreaAttack = SeqInt(meta, "areaAttack"),
                    Levels = row.TryGetValue("childLevels", out var levels)
                        ? levels as Dictionary<string, object> : null,
                });
            }
        }
        if (sequenceData.TryGetValue("wounds", out var woundRaw) &&
            woundRaw is Dictionary<string, object> woundTable)
        {
            foreach (var pair in woundTable)
            {
                if (!(pair.Value is Dictionary<string, object> row)) continue;
                wounds.Add(new WoundSpec
                {
                    Parent = SeqInt(row, "parent"),
                    MaxStacks = Mathf.Max(1, SeqInt(row, "maxStacks", 1)),
                    DurationSec = SeqInt(row, "durationSec"),
                    Sources = SeqInts(row, "sources"),
                });
            }
        }
        // 裂缝：母技能 common 的 damage/attackCount/mobCount 是**爆炸**段的数值，
        // 与子技能那一套分开存，逐级都在 levels 里。
        if (sequenceData.TryGetValue("zones", out var zoneRaw) &&
            zoneRaw is Dictionary<string, object> zoneTable)
        {
            foreach (var pair in zoneTable)
            {
                if (!(pair.Value is Dictionary<string, object> row)) continue;
                riftZones[SeqInt(row, "parent")] = new RiftZone
                {
                    Parent = SeqInt(row, "parent"),
                    Child = SeqInt(row, "child"),
                    Levels = row.TryGetValue("levels", out var levels)
                        ? levels as Dictionary<string, object> : null,
                };
            }
        }
    }

    static SkillSequence SequenceOf(int parent, SeqTrigger trigger)
    {
        LoadSequences();
        foreach (var seq in sequences)
            if (seq.Parent == parent && seq.Trigger == trigger) return seq;
        return null;
    }

    static SkillSequence SequenceByTrigger(SeqTrigger trigger)
    {
        LoadSequences();
        foreach (var seq in sequences) if (seq.Trigger == trigger) return seq;
        return null;
    }

    /// <summary>母技能施放成功后要开的形态（recast 型或 basic 型）。</summary>
    static SkillSequence FormSequenceOf(int parent)
    {
        LoadSequences();
        foreach (var seq in sequences)
            if (seq.Parent == parent && (seq.Trigger == SeqTrigger.Recast || seq.Trigger == SeqTrigger.Basic))
                return seq;
        return null;
    }

    static WoundSpec WoundOf(int parent)
    {
        LoadSequences();
        foreach (var spec in wounds) if (spec.Parent == parent) return spec;
        return null;
    }

    /// <summary>
    /// 母技能按下去要不要结算自己 common 的伤害段。
    /// 282 的 info/type=33 是「buff + 攻击」复合技能，但同样是 buff 带 damage 的还有
    /// 5 条是**被动/proc 的数值**（剑士意念VI / 海龙石VI / 风暴使者VI / 散式投掷 / 影之枪），
    /// 一律按主动攻击打会凭空多出一发，所以这里由表显式决定，不看 Kind。
    /// </summary>
    static bool SequenceParentAttacks(int id)
    {
        LoadSequences();
        foreach (var seq in sequences) if (seq.Parent == id) return seq.ParentAttack;
        return false;
    }

    static RiftZone RiftOf(int parent)
    {
        LoadSequences();
        return riftZones.TryGetValue(parent, out var zone) ? zone : null;
    }

    // 子技能定义：从表里的 childLevels + childSkill 元信息现场构造，不经过 SkillBook，
    // 所以不污染技能窗 / SP / 矩阵等级。
    NativeSkillDefinition SequenceChildDefinition(SkillSequence seq, int level)
    {
        if (seq == null || seq.Levels == null) return null;
        int lv = Mathf.Clamp(level, 1, seq.ChildMax);
        if (!seq.Levels.TryGetValue(lv.ToString(), out var raw) ||
            !(raw is Dictionary<string, object> row)) return null;
        var skill = new Dictionary<string, object>
        {
            { "id", seq.Child }, { "kind", "Attack" }, { "magic", seq.Magic },
            { "element", "" }, { "summonInterval", 1f },
            { "skillType", seq.SkillType }, { "areaAttack", seq.AreaAttack },
            { "vMatrix", true },
        };
        var data = new Dictionary<string, object>(row) { ["level"] = lv };
        return new NativeSkillDefinition(skill, data);
    }

    // ——— 形态生命周期 ———

    void StartSequenceForm(SkillSequence seq, int level)
    {
        if (seq == null || seq.DurationSec <= 0) return;
        formUntil[seq.Parent] = Time.realtimeSinceStartupAsDouble + seq.DurationSec;
        formCharges[seq.Parent] = seq.Charges;
        // 形态开始就给一次提示：这两条都是「次数型」，不显示剩余量玩家无从判断。
        if (seq.Charges > 0) Report(seq.Charges + " 次「" + seq.ChildName + "」");
    }

    bool FormActive(SkillSequence seq)
        => seq != null && formUntil.TryGetValue(seq.Parent, out double until) &&
           Time.realtimeSinceStartupAsDouble < until;

    int FormCharges(SkillSequence seq)
    {
        if (!FormActive(seq)) return 0;
        return formCharges.TryGetValue(seq.Parent, out int left) ? left : 0;
    }

    void EndSequenceForm(int parent)
    {
        formUntil.Remove(parent);
        formCharges.Remove(parent);
    }

    bool SpendFormCharge(SkillSequence seq)
    {
        if (seq == null) return false;
        int left = FormCharges(seq);
        if (left <= 0) { EndSequenceForm(seq.Parent); return false; }
        if (left - 1 <= 0) EndSequenceForm(seq.Parent);
        else formCharges[seq.Parent] = left - 1;
        return true;
    }

    /// <summary>
    /// 再按母技能时的分流。必须在冷却 / MP / 弹药全部之前：
    /// 400031055 的 buff 是 60 秒、冷却 120 秒，形态期间一定处于「技能冷却中」，
    /// 放到后面会被自己的冷却挡死，等于第二发永远打不出去。
    /// 返回 true 表示「按的是形态内的子技能」，cast 说明这一发是否真的打出去了。
    /// </summary>
    bool TrySequenceRecast(SkillDef s, out bool cast)
    {
        cast = false;
        if (p == null || p.hp <= 0 || s?.native == null) return false;
        var seq = SequenceOf(s.native.Id, SeqTrigger.Recast);
        if (!FormActive(seq)) return false;
        cast = CastSequenceChild(seq, s.native.Level, p.FeetPos, p.facing, null, true);
        return true;
    }

    /// <summary>
    /// 普通攻击被形态替换（400011055 極樂之境 → 黃泉十字架）。
    /// 由 PlayerCtrl.RequestAttack 在自动挥砍之前调用；返回 false 表示照常普攻。
    /// </summary>
    public bool TrySequenceBasicAttack()
    {
        if (p == null || p.hp <= 0) return false;
        var game = GameLoop.I;
        if (game == null || game.state != GameLoop.GameState.Playing || game.gameOver) return false;
        if (p.IsClimbing) return false;
        var seq = SequenceByTrigger(SeqTrigger.Basic);
        if (!FormActive(seq)) return false;
        // 原端普攻是动作驱动的：形态期间那一下仍然受施法动作时长约束。
        if (Time.unscaledTime < actionUntil) return false;
        return CastSequenceChild(seq, SkillProgress.LevelOf(seq.Parent), p.FeetPos, p.facing, null, true);
    }

    /// <summary>
    /// 施放子技能：校验/扣 MP（仅 childMp 型）→ 扣次数 → 播动作与特效 → 按轮数逐轮结算。
    /// 消耗与冷却刻意不在这里处理：母技能的冷却由母技能自己开，子技能没有独立冷却。
    /// </summary>
    bool CastSequenceChild(SkillSequence seq, int parentLevel, Vector2 origin, int facing,
        Component onlyTarget, bool consumeCharge)
    {
        var child = SequenceChildDefinition(seq, parentLevel);
        if (child == null || child.DamagePercent <= 0) return false;
        // 扣不扣看表（400041025 的连携段不该再扣一次 650），扣多少看当前等级的 mpCon。
        int mpCost = seq.ChildMp > 0 ? child.MpCost : 0;
        if (mpCost > 0 && p.mp < mpCost) return Reject("魔力不足（需要 " + mpCost + "）");
        if (consumeCharge && !SpendFormCharge(seq)) return Reject("「" + seq.ChildName + "」次数已用尽");
        if (mpCost > 0) p.TrySpendMp(mpCost);
        var def = new SkillDef(new ClassSkill
        {
            name = seq.ChildName, art = seq.Child.ToString(), icon = "",
        }, child);
        p.PlayNativeSkill(seq.Child);
        PlaySequenceEffect(seq, parentLevel, origin, facing);
        LockAction(Mathf.Max(p.SkillActionDuration, .22f), child);
        AudioManager.Play("Skill/" + seq.Child + "/Use", 0.9f);
        StartCoroutine(RunSequenceChild(seq, def, child, origin, facing, onlyTarget));
        return true;
    }

    IEnumerator RunSequenceChild(SkillSequence seq, SkillDef def, NativeSkillDefinition child,
        Vector2 origin, int facing, Component onlyTarget)
    {
        // 单目标多轮（極速射擊 4 支箭 / 黑影切斷 5 斩）是「每轮换一个目标」，
        // 多目标多轮（黃泉十字架 15 名 × 5 斩）是「同一批目标被打 N 次」。
        // 前者不能靠 HitAt 的 struck —— 那里 count 会按 struck.Count 减掉，第二发会变成 0 段；
        // 所以自己记已打过的目标，每轮挑一个没打过的。
        bool spread = child.TargetCount <= 1 && seq.Repeat > 1 && onlyTarget == null;
        var spreadDone = spread ? new List<Component>() : null;
        for (int round = 0; round < seq.Repeat; round++)
        {
            int waitMs = seq.AttackTimes != null && seq.AttackTimes.Length > 0
                ? seq.AttackTimes[Mathf.Min(round, seq.AttackTimes.Length - 1)] : 0;
            if (round > 0 && waitMs > 0) yield return new WaitForSeconds(waitMs / 1000f);
            if (p == null || p.hp <= 0) yield break;
            if (GameLoop.I == null || GameLoop.I.gameOver ||
                GameLoop.I.state != GameLoop.GameState.Playing) yield break;
            // 连携段锁死目标（瞬移到背后打的那一个），其余按子技能自己的 lt/rb 选。
            Vector2 point = onlyTarget != null ? p.FeetPos : origin;
            Component single = onlyTarget;
            if (spread)
            {
                single = null;
                foreach (var candidate in SelectTargets(def, child, point, p.facing, null, null))
                {
                    if (candidate == null || spreadDone.Contains(candidate)) continue;
                    single = candidate;
                    break;
                }
                if (single == null) yield break;      // 目标打完了，剩下的轮数没有意义
                spreadDone.Add(single);
            }
            HitAt(def, child, point, p.facing, null, single);
            // 极乐之境：交叉冥河命中处裂开空间，留下裂缝（h「以交叉冥河攻擊時斬裂空間」）。
            var rift = RiftOf(seq.Parent);
            if (rift != null)
                TouchRifts(rift, SelectTargets(def, child, point, p.facing, null, single));
        }
    }

    // ——— 裂缝（400011055 極樂之境 的第二段）———
    //
    // h：「留下持續 y 秒的裂縫；持續以交叉冥河攻擊裂縫會使裂縫逐漸擴大，最終發動 q 次爆炸，
    //     以 damage% 的傷害攻擊最多 mobCount 名敵人 attackCount 次；裂縫在生成後 z 秒可再次
    //     生成，最多 u3 個；持續時間結束後消失並立即爆炸，但傷害降低為 u4%」。
    // 所以裂缝是「被同一技能反复命中才长大」的区域实体，不是每发都炸。

    void TouchRifts(RiftZone zone, List<Component> targets)
    {
        if (zone == null || targets == null || targets.Count == 0) return;
        int level = Mathf.Max(1, SkillProgress.LevelOf(zone.Parent));
        var row = RiftRow(zone, level);
        if (row == null) return;
        int maxZones = Mathf.Max(1, Art.Int(row, "maxZones", 1));
        int stages = Mathf.Max(1, Art.Int(row, "stages", 1));
        float life = Mathf.Max(1f, Art.Num(row, "lifeSec", 1));
        float regen = Art.Num(row, "regenSec");
        double now = Time.realtimeSinceStartupAsDouble;
        foreach (var target in targets)
        {
            if (target == null) continue;
            Vector2 point = target.transform.position;
            Rift near = null;
            foreach (var rift in rifts)
                if ((rift.Position - point).sqrMagnitude <= 2.25f) { near = rift; break; }
            if (near != null)
            {
                near.Stages++;
                if (near.Stages >= stages)
                {
                    ExplodeRift(zone, near, 100);
                    rifts.Remove(near);
                }
                continue;
            }
            if (rifts.Count >= maxZones) continue;
            if (regen > 0 && now - lastRiftSpawn < regen) continue;
            rifts.Add(new Rift { Position = point, Stages = 1, ExpireAt = now + life, SpawnedAt = now });
            lastRiftSpawn = now;
        }
        if (rifts.Count > 0 && riftTender == null) riftTender = StartCoroutine(TendRifts(zone));
    }

    IEnumerator TendRifts(RiftZone zone)
    {
        while (true)
        {
            yield return new WaitForSeconds(.15f);
            if (p == null || p.hp <= 0 || GameLoop.I == null || GameLoop.I.gameOver)
            { rifts.Clear(); riftTender = null; yield break; }
            int level = Mathf.Max(1, SkillProgress.LevelOf(zone.Parent));
            var row = RiftRow(zone, level);
            int expirePercent = row != null ? Mathf.Max(0, Art.Int(row, "expirePercent", 100)) : 100;
            double now = Time.realtimeSinceStartupAsDouble;
            for (int i = rifts.Count - 1; i >= 0; i--)
            {
                if (now < rifts[i].ExpireAt) continue;
                ExplodeRift(zone, rifts[i], expirePercent);
                rifts.RemoveAt(i);
            }
            if (rifts.Count == 0) { riftTender = null; yield break; }
        }
    }

    Dictionary<string, object> RiftRow(RiftZone zone, int level)
    {
        if (zone?.Levels == null) return null;
        int max = zone.Levels.Count;
        int lv = Mathf.Clamp(level, 1, Mathf.Max(1, max));
        return zone.Levels.TryGetValue(lv.ToString(), out var raw)
            ? raw as Dictionary<string, object> : null;
    }

    // 爆炸段的伤害体是母技能自己（damage/attackCount/mobCount 就是 h 里的爆炸数值），
    // 但母技能是 buff 类、按 200px 口径判范围，原端是 15 名敌人的无框攻击 ⇒
    // 这里按 vMatrix 攻击体重新构造一个，走全屏可见范围。
    void ExplodeRift(RiftZone zone, Rift rift, int percent)
    {
        var row = RiftRow(zone, Mathf.Max(1, SkillProgress.LevelOf(zone.Parent)));
        if (row == null) return;
        var data = new Dictionary<string, object>(row)
        {
            ["damage"] = Art.Num(row, "damage") * Mathf.Max(0, percent) / 100.0,
        };
        var skill = new Dictionary<string, object>
        {
            { "id", zone.Parent }, { "kind", "Attack" }, { "magic", false },
            { "element", "" }, { "summonInterval", 1f },
            { "skillType", 0 }, { "areaAttack", 1 }, { "vMatrix", true },
        };
        var body = new NativeSkillDefinition(skill, data);
        if (body.DamagePercent <= 0) return;
        var def = new SkillDef(new ClassSkill { name = "", art = zone.Parent.ToString(), icon = "" }, body);
        PlayNativeReplayEffect(zone.Parent, rift.Position, p != null ? p.facing : 1);
        HitAt(def, body, rift.Position, p != null ? p.facing : 1);
    }

    // 特效取子技能自己的 effect 层；SkillProgress.LevelOf(子技能) 恒为 0，
    // 所以等级要显式传母技能的等级，不能走 PlayNativeReplayEffect(id) 的默认取法。
    void PlaySequenceEffect(SkillSequence seq, int level, Vector2 origin, int facing)
    {
        string key = NativeSkillVfx.Key(seq.Child, Mathf.Max(1, level));
        foreach (var clip in NativeSkillVfx.Parts(key, "effect"))
            Visual(key, clip.path, origin, facing,
                NativeSkillVfx.Number(key, clip.path, "fix") != 0 ? null : p.transform,
                false, p.SortingOrder + (NativeSkillVfx.Number(key, clip.path, "z") < 0 ? -1 : 1));
    }

    // ——— 伤口（400041025 的被动段）———

    /// <summary>
    /// 命中后调用（HitAt 与 BeginReplay 共用 TriggerReactiveSkills）。
    /// 只有 h 点名的来源技能会留下伤口：致命暗殺（4241000 / 4241001）与音速狂襲（400041039）。
    /// </summary>
    void NotifySequenceHit(SkillDef cast, List<CapturedReplayHit> captured)
    {
        LoadSequences();
        if (cast?.native == null || captured == null || captured.Count == 0) return;
        WoundSpec spec = null;
        foreach (var candidate in wounds)
            foreach (int source in candidate.Sources)
                if (source == cast.native.Id) { spec = candidate; break; }
        if (spec == null) return;
        // h「只會對1名敵人留下」+「優先留下給最大HP最高的敵人」→ 取本批命中的最大 HP 目标。
        Component best = null;
        long bestHp = -1;
        foreach (var hit in captured)
        {
            var target = hit.Target;
            if (target == null) continue;
            if (target is EnemyUnit e && e.dead) continue;
            if (target is BossUnit b && b.dead) continue;
            long hp = TargetMaxHp(target);
            if (hp > bestHp) { bestHp = hp; best = target; }
        }
        if (best == null) return;
        double now = Time.realtimeSinceStartupAsDouble;
        // 换了敌人 ⇒ 前一个伤口立即消失（h 原文），不是并存。
        if (!ReferenceEquals(best, woundTarget)) { woundStacks = 0; woundTarget = best; }
        woundStacks = Mathf.Min(spec.MaxStacks, woundStacks + 1);
        woundUntil = now + spec.DurationSec;
    }

    bool WoundReady(WoundSpec spec)
    {
        // Component 的 == null 对已销毁对象为 true（Unity 的 fake null），必须先判这个。
        if (spec == null || woundTarget == null) return false;
        if (Time.realtimeSinceStartupAsDouble >= woundUntil) { ClearWound(); return false; }
        if (woundTarget is EnemyUnit e && e.dead) { ClearWound(); return false; }
        if (woundTarget is BossUnit b && b.dead) { ClearWound(); return false; }
        return woundStacks >= spec.MaxStacks;
    }

    void ClearWound()
    {
        woundTarget = null;
        woundStacks = 0;
        woundUntil = 0;
    }

    /// <summary>
    /// 连携段：伤口满层时，母技能施放之后追加 400041026 —— 瞬移到该敌人背后，
    /// 7 段 × 5 斩，无视免疫/反伤（info2/ignoreImmune + ignoreCounter），攻击后 1.8 秒无敌（t）。
    /// </summary>
    IEnumerator RunSequenceChain(SkillSequence seq, int parentLevel)
    {
        var spec = WoundOf(seq.Parent);
        var victim = woundTarget;
        if (!WoundReady(spec) || victim == null) yield break;
        ClearWound();
        yield return new WaitForSeconds(.12f);
        if (p == null || p.hp <= 0 || victim == null) yield break;
        if (victim is EnemyUnit e && e.dead) yield break;
        if (victim is BossUnit b && b.dead) yield break;
        // casterMove=1：站到目标背面（原端「移動到該名敵人的背後」）。
        Vector3 target = victim.transform.position;
        int dir = target.x >= p.FeetPos.x ? 1 : -1;
        float behind = target.x - dir * (60f / Art.PPU);
        float ground = PlayerCtrl.GroundAt(behind, p.FeetPos.y);
        p.PlaceAt(behind, ground);
        p.facing = (target.x >= behind ? 1 : -1);
        // 无敌时长取母技能 common/t（1.8 秒），与 h「攻擊後#t秒內無敵」一致。
        var row = SkillBook.Get(seq.Parent)?.Level(parentLevel);
        float invuln = Art.Num(row, "t");
        // PlayerCtrl 只有 invulnUntil 这一个无敌口径（受击/起身都用它），不新开字段。
        if (invuln > 0) p.invulnUntil = Mathf.Max(p.invulnUntil, Time.unscaledTime + invuln);
        PlaySequenceEffect(seq, parentLevel, p.FeetPos, p.facing);
        CastSequenceChild(seq, parentLevel, p.FeetPos, p.facing, victim, false);
    }

    /// <summary>换图 / 死亡：形态、次数、伤口、裂缝全部失效，不能带进下一张图。</summary>
    void ClearSequences()
    {
        formUntil.Clear();
        formCharges.Clear();
        ClearWound();
        rifts.Clear();
        riftTender = null;
        lastRiftSpawn = 0;
    }
}
