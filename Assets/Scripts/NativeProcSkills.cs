using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ——— 282 的「被动追加攻击」（proc）———
//
// 有一批技能的伤害不是「按下去结算」，而是由**别的攻击命中**触发。导出层把它们判成
// passive（不再假打），但此前没有任何代码去触发，于是这一批技能等于彻底失效。
// 这里补上「主攻击命中 → 判定 → 用伤害体的 damage%/attackCount 结算」这条链。
//
// 判据来自 WZ 字段而不是文案猜测：
//   info/type=53 + info/finalAttack=1 ⇒ 282 的「終極攻擊」结算体。
//   全库（17 个职业定义 img + 五转 4000x/50000）type=53 共 12 条，其中 10 条带
//   finalAttack=1；这 10 条的每一条都能在 String.wz 找到一个母技能，其 h 写
//   「[終極攻擊系列技能] 直接攻擊的技能命中時，有#prop%機率…#x%傷害」，而母技能的
//   x/y/w 与组件的 damage/attackCount **逐级相等**：
//     2341009.x(405)  == 2341010.damage(405)
//     2141012.x(441)  == 2140013.damage(441)
//     2241003.x(400)  == 2241004.damage(400)
//     5141009.y(535)  == 5140010.damage(535)、x(100)==prop、w(2)==attackCount
//   另外两条 type=53：1140005 狂暴劍痕是真技能（有 lt/rb、有 h、无 finalAttack），
//   1240008 终极攻击VI 只是缺 finalAttack 字段（人工登记）。
//   ⇒ 所以有「触发体」和「伤害体」两个 id：触发体学没学决定生效，伤害体提供数值。
//
// 四种触发形态：
//   ① Roll         概率追加攻击 —— 每次主攻击结算掷一次骰（prop%）。
//   ② Charge       命中计数 —— 「命中#x次時」，累计命中段数，达阈值后触发并清零。
//   ③ SummonCharge 命中计数攒召唤物，再概率放出 —— 暗影蝙蝠/飢餓蝙蝠。
//   ④ BuffRoll     Buff 生效期间概率触发 —— 風暴使者VI / 影之槍。
//
// 伤害倍率口径：与项目里既有的追加攻击（HAUNTED_EDGE / 雷霆万钧VI 追击）一致，
// 用主攻击本次掷出的伤害按 damage% 比例换算（NativeSkillReplay.Adapt），
// 不再 roll 一次面板 —— proc 与主攻击共享同一次爆击/最终伤害判定。
//
// 未实现（原因见 docs/tms282-skill/20-v-hexa-panel.md §7.16）：需要召唤物实体
// （1141006 周期剑击）、需要地面区域（2141007 燎原之火）、需要资源系统
// （5141011 海龙石）、以及本身就是强化被动而非追加攻击的 5140004。
public partial class SkillSystem
{
    enum ProcMode
    {
        Roll,          // 每次攻击结算掷一次 prop
        Charge,        // 累计命中段数 → 阈值触发
        SummonCharge,  // 累计命中段数 → 攒召唤物 → 攻击时掷骰消耗
        BuffRoll,      // Buff 生效期间每次攻击掷一次 prop
        MarkPerTarget, // 按敌人个体记标记；再次命中同一敌人时发射飞镖（刺客刻印VI）
    }

    sealed class ProcSpec
    {
        public readonly int Trigger;      // 学习/等级判据
        public readonly int Body;         // 伤害体（0 = 同 Trigger）
        public readonly ProcMode Mode;
        public readonly string ChargeField;   // 阈值字段（x / z）
        public readonly string CapField;      // 存量上限字段（y）
        public readonly string PropField;     // 概率字段（默认 prop；战舰鲸鱼号用 x）
        public readonly int BrandBody;        // 烙印满 → 强化的伤害体（闇靈懲戒）
        public readonly bool Basic;           // 普通攻击也触发的（終極之弓/弩）
        // 状态升级（暗影之隱 4240016 → 暗影之刃 4240017）：
        // h「隱身術持續#v秒以上時，強化為暗影之刃」「#c暗影之刃#：持續#w秒，
        // 以#s%機率發動以#q2%傷害攻擊#q次的致命一擊」。状态在时换伤害体、换概率字段。
        public readonly int AltState, AltBody;
        public readonly string AltPropField;
        // 倍率口径覆盖：非空时用该字段当「主攻击最终伤害的百分之几」，
        // 而不是 body 的 damage%（暗影侍从 400041028 的 x=46..75，自身没有 damage 字段）。
        public readonly string ScaleField;

        public ProcSpec(int trigger, ProcMode mode, int body = 0, string chargeField = "x",
            string capField = "y", string propField = "prop", int brandBody = 0, bool basic = false,
            string scaleField = "", int altState = 0, int altBody = 0, string altPropField = "")
        {
            Trigger = trigger; Body = body == 0 ? trigger : body; Mode = mode;
            ChargeField = chargeField; CapField = capField; PropField = propField;
            BrandBody = brandBody; Basic = basic; ScaleField = scaleField;
            AltState = altState; AltBody = altBody; AltPropField = altPropField;
        }
    }

    static readonly ProcSpec[] procSpecs =
    {
        // ① 六转「終極攻擊」系列：自带 prop/damage，任意直接攻击命中即触发。
        new ProcSpec(1140009, ProcMode.Roll),   // 英雄    h「#prop%機率發動可造成#damage%傷害#attackCount次」
        new ProcSpec(1240008, ProcMode.Roll),   // 聖騎士  h「攻擊命中時，#prop%機率發動…追加攻擊」
        new ProcSpec(1340007, ProcMode.Roll),   // 黑騎士  h「#prop%機率發動可造成#damage%傷害#attackCount次」
        new ProcSpec(3240014, ProcMode.Roll),   // 箭神    h「以#prop%機率發動可造成#damage%傷害的終極攻擊」
        new ProcSpec(3100001, ProcMode.Roll, basic: true),   // 終極之弓（弓）
        new ProcSpec(3200001, ProcMode.Roll, basic: true),   // 終極之弩（弩）
        // 黑騎士 闇靈復仇VI：伤害体 1341005 与母技能数值同源；
        // 烙印满 3 个（u）后发动 1341006 闇靈懲戒（h「造成#s%傷害#s2次」，s/s2 与体逐级相等）。
        new ProcSpec(1340004, ProcMode.Roll, body: 1341005, brandBody: 1341006),
        // 母技能带 prop、组件只提供数值的两组。
        new ProcSpec(2341009, ProcMode.Roll, body: 2341010),   // 天怒VI
        new ProcSpec(2141012, ProcMode.Roll, body: 2140013),   // 火流星VI
        new ProcSpec(2241003, ProcMode.Roll, body: 2241004),   // 暴風雪VI
        new ProcSpec(5141009, ProcMode.Roll, body: 5140010, propField: "x"),  // 戰艦鯨魚號VI（概率在 x=100）
        // 母体合一、自带 cooltimeMS 再发动冷却的两条 V 技能。
        new ProcSpec(500001016, ProcMode.Roll),  // 突擊爆發（劍擊）
        new ProcSpec(500001020, ProcMode.Roll),  // 突擊爆發（黑暗長槍）
        // ② 命中计数。
        new ProcSpec(12140015, ProcMode.Charge, body: 12141016, chargeField: "x"),  // 魔法爆發VI
        new ProcSpec(14141019, ProcMode.Charge, chargeField: "x"),                  // 深淵黑暗
        // ③ 命中计数攒蝙蝠，攻击时概率放出。
        new ProcSpec(14141004, ProcMode.SummonCharge, chargeField: "z", capField: "y"),
        new ProcSpec(14141012, ProcMode.SummonCharge, chargeField: "z", capField: "y"),
        // ④ Buff 期间概率触发。
        new ProcSpec(13141003, ProcMode.BuffRoll),   // 風暴使者VI
        new ProcSpec(400041008, ProcMode.BuffRoll),  // 影之槍
        // ④b Buff 期间按「主攻击最终伤害的 x%」追加打击（自身没有 damage 字段）。
        // h「在#time秒期間召喚影子；暗影飛鏢：使用投擲飛鏢攻擊時，以最終傷害的#x%造成追加打擊」
        new ProcSpec(400041028, ProcMode.BuffRoll, scaleField: "x"),  // 暗影侍從
        // ⑤ 命中触发的「召唤一次性区域」。
        // 燎原之火VI h「施展火焰魔法時，有#prop%的機率生成火之壁。火之壁可維持#x秒，
        // 並且定期對最多#mobCount名敵人造成#damage%傷害#attackCount次，最多攻擊單一敵人#y次」。
        // y 与 attackCount 同为 3 ⇒「最多攻擊單一敵人 3 次」指的就是这 3 段，
        // 所以按一次 Roll 结算 damage×attackCount 即等价，不需要 4 秒的区域实体。
        new ProcSpec(2141007, ProcMode.Roll),
        // ⑥ 状态升级型：暗影之隱的追加攻击（4241018：x% 伤害、u 段、y% 概率）。
        // 隐身术满 v 秒、或在暗影雾杀内首次命中 → 挂 4240017 暗影之刃状态，
        // 期间改用 4241019（q2% 伤害、q 段、s% 概率）。两条伤害体的 damage/
        // attackCount/prop 都与母技能 level 行逐级相等，见 export_skill_states.py。
        new ProcSpec(4240016, ProcMode.Roll, body: 4241018, propField: "y",
            altState: 4240017, altBody: 4241019, altPropField: "s"),
        // ⑦ 刺客刻印VI h「攻擊時以#prop%的機率留下暗殺者的標記，再次攻擊該敵方時，
        // 生成並發射#bulletCount個傷害#dummyStr%的飛鏢」。标记必须按敌人**个体**记，
        // 不能像 TryBrand 那样按技能 id 全局累计。伤害体 4141004.damage 与 dummyStr
        // 30 级逐级相等（317..520），见 check_proc_skills.py。
        new ProcSpec(4141003, ProcMode.MarkPerTarget, body: 4141004),
    };

    // 命中计数 / 蝙蝠存量 / 烙印数（按触发技能 id）。不落盘：换图或死亡后清零。
    readonly Dictionary<int, int> procCounter = new Dictionary<int, int>();
    readonly Dictionary<int, int> procBrands = new Dictionary<int, int>();
    readonly Dictionary<int, int> procBrandTicks = new Dictionary<int, int>();
    // 再发动冷却（common/cooltimeMS）与 BuffRoll 的到期时刻。
    readonly Dictionary<int, double> procNext = new Dictionary<int, double>();
    readonly Dictionary<int, double> procUntil = new Dictionary<int, double>();
    // 海龙霸拳「水龙斗气」：状态期间按 dummyStr 冷却、最多 dummyStr2 次，换状态重新计数。
    readonly Dictionary<int, double> chainNext = new Dictionary<int, double>();
    readonly Dictionary<int, int> chainUses = new Dictionary<int, int>();
    readonly Dictionary<int, bool> chainWasActive = new Dictionary<int, bool>();
    // 刺客刻印VI：按敌人个体记的「暗殺者的標記」到期时刻，key 是 EnemyUnit / BossUnit。
    readonly Dictionary<Component, double> assassinMarks = new Dictionary<Component, double>();

    void ResetProcState()
    {
        assassinMarks.Clear();
        procCounter.Clear();
        procBrands.Clear();
        procBrandTicks.Clear();
        procNext.Clear();
        procUntil.Clear();
        chainNext.Clear();
        chainUses.Clear();
        chainWasActive.Clear();
    }

    // BeginCast 里调用：BuffRoll 类技能按下后开始计时。持续秒数取 common/time。
    void StartProcBuff(SkillDef s)
    {
        if (s.native == null) return;
        var spec = ProcOf(s.native.Id);
        if (spec == null || spec.Mode != ProcMode.BuffRoll) return;
        var row = SkillBook.Get(s.native.Id)?.Level(s.native.Level);
        float seconds = Art.Num(row, "time");
        if (seconds <= 0) return;
        procUntil[s.native.Id] = Time.realtimeSinceStartupAsDouble + seconds;
    }

    static ProcSpec ProcOf(int id)
    {
        foreach (var spec in procSpecs) if (spec.Trigger == id) return spec;
        return null;
    }

    bool ProcBuffActive(int id)
        => procUntil.TryGetValue(id, out var until) && Time.realtimeSinceStartupAsDouble < until;

    // 主攻击结算后调用（HitAt 与 BeginReplay 都经过 TriggerReactiveSkills）。
    void TriggerProcSkills(SkillDef cast, List<CapturedReplayHit> captured, Vector2 origin, int facing)
    {
        if (cast.native == null || cast.native.Kind != "Attack") return;
        if (captured == null || captured.Count == 0) return;
        if (cast.native.DamagePercent <= 0) return;   // 无伤害基准，无法换算 proc 倍率
        int segments = 0;
        foreach (var hit in captured)
            if (hit.Hits != null) foreach (var line in hit.Hits) if (line.Amount > 0) segments++;
        if (segments == 0) return;

        double now = Time.realtimeSinceStartupAsDouble;
        // 子状态驱动的两条链：暗影之刃的升级条件、海龙霸拳的水龙斗气。
        UpdateShadowBladeState();
        TriggerStateChains(cast, captured, origin, facing);
        foreach (var spec in procSpecs)
        {
            int level = SkillProgress.LevelOf(spec.Trigger);
            if (level <= 0) continue;                       // 没学就不参与
            if (!ProcWeaponOk(spec.Trigger)) continue;
            if (procNext.TryGetValue(spec.Trigger, out double next) && now < next) continue;
            var row = SkillBook.Get(spec.Trigger)?.Level(level);
            var body = BodyDefinition(spec, level);
            if (row == null || body == null) continue;
            // 刺客刻印VI：标记挂在敌人个体上，不能走「一个 spec 一次 fire」的通用路径。
            if (spec.Mode == ProcMode.MarkPerTarget)
            {
                TryAssassinMark(spec, row, body, cast, captured, origin, facing);
                continue;
            }
            // 暗影之隐：状态在时换成「暗影之刃」的伤害体与概率字段。
            var states = p != null ? NativeSkillStates.For(p) : null;
            bool upgraded = spec.AltState > 0 && states != null && states.Has(spec.AltState);
            if (upgraded && spec.AltBody > 0)
            {
                int max = SkillBook.MaxLevelOf(spec.AltBody);
                if (max > 0)
                {
                    var alt = SkillBook.Native(spec.AltBody, Mathf.Clamp(level, 1, max));
                    if (alt != null && alt.DamagePercent > 0) body = alt;
                    else upgraded = false;
                }
            }
            // 倍率口径：默认是伤害体自己的 damage%；登记了 ScaleField 的（暗影侍从的
            // x = 「最终伤害的 x%」）改用「主攻击伤害 × x%」—— 那种技能没有 damage 字段。
            // 学了强化核心 500004073 后这个 x 被核心的 x 覆盖（46..75 → 76..105）。
            double scale = spec.ScaleField.Length > 0
                ? NativeSkillOverrides.Scale(spec.Trigger, Art.Int(row, spec.ScaleField))
                : body.DamagePercent;
            if (scale <= 0) continue;
            double fromPercent = spec.ScaleField.Length > 0 ? 100.0 : cast.native.DamagePercent;
            string propField = upgraded && spec.AltPropField.Length > 0 ? spec.AltPropField : spec.PropField;
            int prop = Mathf.Clamp(Art.Int(row, propField, 100), 0, 100);

            bool fire;
            switch (spec.Mode)
            {
                case ProcMode.BuffRoll:
                    if (!ProcBuffActive(spec.Trigger)) continue;
                    fire = UnityEngine.Random.Range(0, 100) < prop;
                    break;
                case ProcMode.Charge:
                case ProcMode.SummonCharge:
                {
                    int cap = spec.Mode == ProcMode.SummonCharge
                        ? Mathf.Max(1, Art.Int(row, spec.CapField, 1))
                        : int.MaxValue;
                    procCounter.TryGetValue(spec.Trigger, out int count);
                    count = Mathf.Min(cap, count + segments);
                    if (spec.Mode == ProcMode.Charge)
                    {
                        int threshold = Mathf.Max(1, Art.Int(row, spec.ChargeField, 1));
                        fire = count >= threshold;
                        if (fire) count -= threshold;
                    }
                    else
                    {
                        fire = count > 0 && UnityEngine.Random.Range(0, 100) < prop;
                        if (fire) count--;
                    }
                    procCounter[spec.Trigger] = Mathf.Clamp(count, 0, cap);
                    break;
                }
                default:
                    fire = UnityEngine.Random.Range(0, 100) < prop;
                    break;
            }
            if (!fire) continue;

            var targets = ProcTargets(captured, Mathf.Max(1, body.TargetCount), origin);
            if (targets.Count == 0) continue;
            PlayNativeReplayEffect(upgraded ? spec.AltBody : spec.Trigger, origin, facing);
            foreach (var target in targets)
                FireProc(cast, body, ProcSource(captured, target, cast.native), fromPercent,
                    target, origin, facing, scale);
            int cooldownMs = Art.Int(row, "cooltimeMS");
            if (cooldownMs > 0) procNext[spec.Trigger] = now + cooldownMs / 1000.0;
            // 烙印：每 u2 次追加攻击盖 1 个，集满 u 个消耗全部并发动强化版（闇靈懲戒）。
            if (spec.BrandBody > 0) TryBrand(spec, row, cast, captured, origin, facing);
        }
    }

    // ——— 子状态驱动的两条链 ———
    //
    // 4240016 暗影之隱 h：「隱身術持續#v秒以上時，強化為暗影之刃」
    //                  「#c暗影之刃#：持續#w秒…#s%機率發動#q2%傷害#q次」
    //                  「在煙霧彈或暗影霧殺內，當隱身術中直接攻擊的技能首次命中時，
    //                    立即套用暗影之刃」
    // 4240017 是子状态（time=w=3），4241019 是升级后的伤害体。两条路都只负责
    // 「把状态挂上」，数值仍然从 4240016 的 level 行读，不在 C# 里复制常量。
    void UpdateShadowBladeState()
    {
        if (p == null) return;
        int level = SkillProgress.LevelOf(4240016);
        if (level <= 0) return;
        var row = SkillBook.Get(4240016)?.Level(level);
        if (row == null) return;
        var states = NativeSkillStates.For(p);
        var buffs = NativeBuffs.For(p);
        bool dark = false;
        double elapsed = 0.0;
        foreach (var effect in buffs.Visible)
            if (effect.Stats.ContainsKey("DARKSIGHT"))
            { dark = true; elapsed = NativeBuffs.Now - effect.Started; break; }
        // 4241005 黑暗面是 6 转隐身术的状态体，同样算「正在隐身」。
        if (states.Has(4241005)) { dark = true; elapsed = Math.Max(elapsed, states.Since(4241005)); }
        if (!dark) return;
        bool instant = states.Has(4240014);       // 暗影雾杀VI / 烟雾弹区域
        bool longEnough = elapsed >= Art.Int(row, "v", 9);
        if (!instant && !longEnough) return;
        states.ActivateState(4240017, level, Art.Int(row, "w", 3));
    }

    // 5141500 海龍霸拳 h：「在#time秒內，除格鬥技能與海龍螺旋外，使用五轉技能時觸發
    // #c水龍鬥氣#。冷卻時間#dummyStr秒，最多可發動#dummyStr2次攻擊。第1擊…第5擊」
    // 5141501 是海龙状态，skillList 是触发名单，5141502..6 是五段伤害体 ——
    // 全部来自 NativeSkillStates.json（tools/ms282/export_skill_states.py），不写死 id。
    void TriggerStateChains(SkillDef cast, List<CapturedReplayHit> captured, Vector2 origin, int facing)
    {
        if (p == null || cast.native == null || captured == null) return;
        var states = NativeSkillStates.For(p);
        double now = Time.realtimeSinceStartupAsDouble;
        foreach (int stateId in NativeSkillStates.AllIds())
        {
            if (NativeSkillStates.Ints(stateId, "bodies").Length == 0) continue;
            if (!states.Has(stateId))
            { chainWasActive[stateId] = false; chainUses[stateId] = 0; continue; }
            if (!chainWasActive.TryGetValue(stateId, out bool was) || !was)
            { chainUses[stateId] = 0; chainWasActive[stateId] = true; }
            bool listed = false;
            foreach (int trigger in NativeSkillStates.Ints(stateId, "triggers"))
                if (trigger == cast.native.Id) { listed = true; break; }
            if (!listed) continue;
            if (chainNext.TryGetValue(stateId, out double next) && now < next) continue;
            chainUses.TryGetValue(stateId, out int used);
            // 海龙霸拳最多 8 次；圣域展开不设次数（0），由状态自己的 30 秒收口。
            int cap = NativeSkillStates.Int(stateId, "maxActivations", 8);
            if (cap > 0 && used >= cap) continue;
            // 1241501 的再发动冷却是 1241502.common/cooltimeMS=500 毫秒；
            // 5141501 写的是 cooldownSec=2 秒。两种单位都从表读，别把 500 当成秒。
            int cooldownMs = NativeSkillStates.Int(stateId, "cooldownMs");
            double cooldown = cooldownMs > 0 ? cooldownMs / 1000.0
                : NativeSkillStates.Int(stateId, "cooldownSec", 2);
            chainNext[stateId] = now + System.Math.Max(0.01, cooldown);
            chainUses[stateId] = used + 1;
            StartCoroutine(FireStateChain(stateId, cast, captured,
                NativeSkillStates.Ints(stateId, "bodies"), origin, facing));
        }
    }

    IEnumerator FireStateChain(int stateId, SkillDef cast, List<CapturedReplayHit> captured,
        int[] bodies, Vector2 origin, int facing)
    {
        int parent = NativeSkillStates.Int(stateId, "parent");
        int level = Mathf.Max(1, SkillProgress.LevelOf(parent));
        foreach (int bodyId in bodies)
        {
            yield return new WaitForSeconds(0.12f);
            if (p == null || p.hp <= 0) yield break;
            int max = SkillBook.MaxLevelOf(bodyId);
            var body = SkillBook.Native(bodyId, max > 0 ? Mathf.Clamp(level, 1, max) : 1);
            if (body == null || body.DamagePercent <= 0) continue;
            var targets = ProcTargets(captured, Mathf.Max(1, body.TargetCount), origin);
            if (targets.Count == 0) continue;
            PlayNativeReplayEffect(bodyId, origin, facing);
            foreach (var target in targets)
                FireProc(cast, body, ProcSource(captured, target, cast.native), cast.native.DamagePercent,
                    target, origin, facing, body.DamagePercent);
        }
    }

    // 刺客刻印VI 4141003：h「攻擊時以#prop%的機率留下暗殺者的標記，再次攻擊該敵方時，
    // 生成並發射#bulletCount個傷害#dummyStr%的飛鏢」；desc「飛鏢會優先攻擊最大HP最高的敵方」。
    // 标记必须按敌人**个体**存（assassinMarks），与 TryBrand 的按技能全局计数不同。
    // dotTime=20 是标记寿命（取字段形状；h 没写持续秒数）。
    void TryAssassinMark(ProcSpec spec, Dictionary<string, object> row, NativeSkillDefinition body,
        SkillDef cast, List<CapturedReplayHit> captured, Vector2 origin, int facing)
    {
        if (body == null || body.DamagePercent <= 0 || cast.native == null) return;
        double now = Time.realtimeSinceStartupAsDouble;
        int prop = Mathf.Clamp(Art.Int(row, spec.PropField, 100), 0, 100);
        int bullets = Mathf.Max(1, Art.Int(row, "bulletCount", 1));
        int mobs = Mathf.Max(1, Art.Int(row, "mobCount", 1));
        int range = Mathf.Max(1, Art.Int(row, "range", 500));
        float lifetime = Mathf.Max(1f, Art.Int(row, "dotTime", 20));
        foreach (var hit in captured)
        {
            var target = hit.Target;
            if (target == null || !TargetAlive(target)) continue;
            bool marked = assassinMarks.TryGetValue(target, out double until) && now < until;
            if (!marked)
            {
                if (UnityEngine.Random.Range(0, 100) < prop) assassinMarks[target] = now + lifetime;
                continue;
            }
            assassinMarks.Remove(target);
            var pool = MarkTargets(target, range);
            if (pool.Count == 0) continue;
            PlayNativeReplayEffect(spec.Body, origin, facing);
            int shots = Mathf.Min(bullets, Mathf.Min(mobs, pool.Count));
            for (int i = 0; i < shots; i++)
            {
                var victim = pool[i];
                FireProc(cast, body, ProcSource(captured, victim, cast.native),
                    cast.native.DamagePercent, victim, origin, facing, body.DamagePercent);
            }
        }
    }

    /// <summary>被标记敌人 range 内的目标，boss / maxHP 降序（desc「優先攻擊最大HP最高的敵方」）。</summary>
    List<Component> MarkTargets(Component marked, int range)
    {
        var list = new List<Component>();
        Vector2 center = marked.transform.position;
        float radius = range / Art.PPU;
        float r2 = radius * radius;
        foreach (var boss in FindObjectsOfType<BossUnit>())
            if (!boss.dead && ((Vector2)boss.transform.position - center).sqrMagnitude <= r2)
                list.Add(boss);
        foreach (var enemy in EnemyUnit.All)
            if (!enemy.dead && ((Vector2)enemy.transform.position - center).sqrMagnitude <= r2)
                list.Add(enemy);
        list.Sort((a, b) =>
        {
            bool ab = IsBossTarget(a), bb = IsBossTarget(b);
            if (ab != bb) return ab ? -1 : 1;
            int hp = TargetMaxHp(b).CompareTo(TargetMaxHp(a));
            return hp != 0 ? hp : a.GetInstanceID().CompareTo(b.GetInstanceID());
        });
        return list;
    }

    // 黑騎士 闇靈復仇VI：h「攻擊命中#u2次時，將對敵人烙印復仇的烙印；集滿3個時，消耗所有烙印並發動闇靈懲戒」。
    void TryBrand(ProcSpec spec, Dictionary<string, object> row, SkillDef cast,
        List<CapturedReplayHit> captured, Vector2 origin, int facing)
    {
        int interval = Mathf.Max(1, Art.Int(row, "u2", 1));
        int cap = Mathf.Max(1, Art.Int(row, "u", 1));
        procBrandTicks.TryGetValue(spec.Trigger, out int ticks);
        ticks++;
        if (ticks < interval) { procBrandTicks[spec.Trigger] = ticks; return; }
        procBrandTicks[spec.Trigger] = 0;
        procBrands.TryGetValue(spec.Trigger, out int brands);
        brands++;
        if (brands < cap) { procBrands[spec.Trigger] = brands; return; }
        procBrands[spec.Trigger] = 0;
        int level = SkillProgress.LevelOf(spec.Trigger);
        var punish = SkillBook.Native(spec.BrandBody, level);
        if (punish == null || punish.DamagePercent <= 0) return;
        var targets = ProcTargets(captured, Mathf.Max(1, punish.TargetCount), origin);
        if (targets.Count == 0) return;
        PlayNativeReplayEffect(spec.BrandBody, origin, facing);
        foreach (var target in targets)
            FireProc(cast, punish, ProcSource(captured, target, cast.native), cast.native.DamagePercent,
                target, origin, facing, punish.DamagePercent);
    }

    // 普通攻击命中也触发的 proc（終極之弓 3100001 / 終極之弩 3200001）。
    // 普攻没有 damage%，源端面板口径就是 100%，所以 fromPercent 固定 100。
    public void NativeBasicProcHit(Component target, DamageResult hit, int facing)
    {
        if (target == null || hit.Amount <= 0 || p == null) return;
        if (GameLoop.I == null || GameLoop.I.gameOver || GameLoop.I.state != GameLoop.GameState.Playing) return;
        double now = Time.realtimeSinceStartupAsDouble;
        foreach (var spec in procSpecs)
        {
            if (!spec.Basic || !ProcWeaponOk(spec.Trigger)) continue;
            if (procNext.TryGetValue(spec.Trigger, out double next) && now < next) continue;
            int level = SkillProgress.LevelOf(spec.Trigger);
            if (level <= 0) continue;
            var row = SkillBook.Get(spec.Trigger)?.Level(level);
            var body = BodyDefinition(spec, level);
            if (row == null || body == null || body.DamagePercent <= 0) continue;
            if (UnityEngine.Random.Range(0, 100) >= Mathf.Clamp(Art.Int(row, spec.PropField, 100), 0, 100)) continue;
            PlayNativeReplayEffect(spec.Trigger, p.FeetPos, facing);
            FireProc(null, body, new[] { hit }, 100.0, target, p.FeetPos, facing, body.DamagePercent);
            int cooldownMs = Art.Int(row, "cooltimeMS");
            if (cooldownMs > 0) procNext[spec.Trigger] = now + cooldownMs / 1000.0;
        }
    }

    // 伤害体的定义：组件技能（如 2341010 天怒VI 的追加體）不属于任何职业的技能线，
    // 用触发体的等级去取它的行；取不到或没有伤害就退回触发体自己。
    NativeSkillDefinition BodyDefinition(ProcSpec spec, int level)
    {
        if (spec.Body != spec.Trigger)
        {
            int max = SkillBook.MaxLevelOf(spec.Body);
            if (max > 0)
            {
                var body = SkillBook.Native(spec.Body, Mathf.Clamp(level, 1, max));
                if (body != null && body.DamagePercent > 0) return body;
            }
        }
        return SkillBook.Native(spec.Trigger, level);
    }

    void FireProc(SkillDef cast, NativeSkillDefinition proc, DamageResult[] source, double fromPercent,
        Component target, Vector2 origin, int facing, double percent)
    {
        if (target == null || source.Length == 0) return;
        var lines = NativeSkillReplay.Adapt(source, proc.HitCount, fromPercent, percent,
            GameSession.Character?.record.damageCap ?? CombatRules.InitialDamageCap);
        if (cast == null)
            cast = new SkillDef(new ClassSkill { name = proc.Id.ToString(), art = proc.Id.ToString(), icon = "" }, proc);
        ApplyReplayHit(cast, proc, target, lines, origin, facing,
            new NativeReplayStage { Total = true, Interval = 120 });
    }

    // 終極之弓要求弓箭、終極之弩要求弩弓（String.wz 的 desc 写死了这两个条件）；
    // 暗影侍从 h「使用投擲飛鏢攻擊時」，所以只在飞镖（claw）下生效。
    bool ProcWeaponOk(int id)
    {
        if (id == 3100001) return p != null && p.WeaponType == WeaponKind.bow;
        if (id == 3200001) return p != null && p.WeaponType == WeaponKind.crossbow;
        if (id == 400041028) return p != null && p.WeaponType == WeaponKind.claw;
        return true;
    }

    // 282 的追加攻击优先打「最大 HP 最高的 Boss」：先 Boss、再按 maxHP 降序、同 HP 按距离。
    List<Component> ProcTargets(List<CapturedReplayHit> captured, int limit, Vector2 origin)
    {
        var list = new List<Component>();
        foreach (var hit in captured)
        {
            var target = hit.Target;
            if (target == null) continue;
            if (target is EnemyUnit e && e.dead) continue;
            if (target is BossUnit b && b.dead) continue;
            if (!list.Contains(target)) list.Add(target);
        }
        list.Sort((a, b) =>
        {
            bool ab = IsBossTarget(a), bb = IsBossTarget(b);
            if (ab != bb) return ab ? -1 : 1;
            int hp = TargetMaxHp(b).CompareTo(TargetMaxHp(a));
            if (hp != 0) return hp;
            float da = ((Vector2)a.transform.position - origin).sqrMagnitude;
            float db = ((Vector2)b.transform.position - origin).sqrMagnitude;
            return da.CompareTo(db);
        });
        if (list.Count > limit) list.RemoveRange(limit, list.Count - limit);
        return list;
    }

    static bool IsBossTarget(Component target)
        => target is BossUnit || (target is EnemyUnit enemy && NativeCombatData.Int(enemy.CombatStats, "boss") != 0);

    static long TargetMaxHp(Component target)
    {
        if (target is EnemyUnit enemy) return NativeCombatData.Long(enemy.CombatStats, "maxHP");
        if (target is BossUnit boss) return NativeCombatData.Long(boss.CombatStats, "maxHP");
        return 0;
    }

    // 取该目标本次主攻击掷出的那几段伤害，作为 proc 倍率换算的基准。
    // 全 miss 的目标没有正伤害可缩放，退回按面板重掷一次（与既有 replay 兜底同口径）。
    DamageResult[] ProcSource(List<CapturedReplayHit> captured, Component target, NativeSkillDefinition main)
    {
        foreach (var hit in captured)
            if (ReferenceEquals(hit.Target, target) && hit.Hits != null && hit.Hits.Length > 0)
            {
                bool positive = false;
                foreach (var line in hit.Hits) positive |= line.Amount > 0;
                if (positive) return hit.Hits;
                break;
            }
        return p != null ? p.ReplayFallback(main, Mathf.Clamp(main.HitCount, 1, 15), true) : Array.Empty<DamageResult>();
    }
}
