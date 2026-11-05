// HEXA矩阵：依据282数据与UserHandler；明确缺陷按用户授权修复，操作号为项目内部协议。
//
//   type 0  激活核心：扣索尔艾尔达气息 + 碎片，校验职业组与 reqForActivation
//   type 1  强化核心：一次可升多级，费用按「节点类型 + 目标等级」查表累加
//   type 2  属性核心激活：选择三条能力值（stat1..3）
//   type 3  属性核心强化：按 hexaStatUpgrade 表扣碎片
//   type 5  属性核心预设：preset 0/1套用；10完整保存，11变更类型
//   type 7  属性核心重置：清空等级与权重
//   原端 op8 是二级密码，不是重命名；本项目独立定义 SaveStat/ChangeStat。
//
// 门禁（20-v-hexa-panel.md §6.3，已拍板）：
//   level >= 260 && quest(38625) 完成 && job ∈ 38601 的 47 项 + job_TW 4 项
//   任一条不满足 → 拒绝并保持在窗口内（不静默丢包，与 282 的 LP009D unlock 同风格）。
//
// 费用来源：constants/HexaMatrixConstants.java 的四组「升级气息/碎片」表（29 档，lv1..29）
// 与「激活」固定值（mastery 3/50、enforce 4/75、skill 5/100、common 7/125）。
using System;
using System.Collections.Generic;

public enum HexaOperation
{
    Activate = 0, Upgrade = 1, SetStat = 2, EnforceStat = 3,
    Preset = 5, Reset = 7, Rename = 8, SaveStat = 10, ChangeStat = 11,
    // 9 = 本地只读快照。窗口打开时不能借 Activate(0) 取状态，
    // 否则 coreId=0 会掉进「未知错误」。
    Snapshot = 9,
}

public sealed class HexaRequest
{
    public int characterId;
    public HexaOperation operation;
    public int coreId, level, targetLevel;
    // 属性核心：三条能力值 + 权重 + 预设号
    public int stat1, stat2, stat3, weight, preset;
    public string name = "";
    public bool commit;
}

public sealed class HexaCoreView
{
    public int coreId, type, level, maxLevel;
    public bool activated;
    public string name = "", desc = "";
    public int[] skills = new int[0];
    public int group;           // 0 mastery / 1 enforce / 2 skill / 3 common
}

public sealed class HexaStatView
{
    public int coreId, level, maxLevel, reqLevel, preset;
    public int stat1, stat2, stat3, weight, level1, level2, level3;
    public bool applied;
}

public sealed class HexaResponse
{
    public bool ok = true;
    public string error = "";
    public int level, job;
    public bool eligible;
    public long solErda, fragments, solErdaGauge, solErdaGaugeMax;
    public int unlockLevel = HexaCatalog.UnlockLevel, unlockQuest = HexaCatalog.UnlockQuest;
    public List<HexaCoreView> cores = new List<HexaCoreView>();
    public List<HexaStatView> stats = new List<HexaStatView>();
    // 下一次升到 level+1 的费用预览（客户端强化弹窗直接用）。
    public long nextSolErda, nextFragments;
}

public static class NativeHexaMatrixService
{
    // 索尔艾尔达碎片（HexaMatrixConstants.solErdaFragments）
    public static readonly int[] FragmentItems = { 4009548, 4009547, 4009550 };
    public const int FaintSolErda = 2638879;      // 1 气息 → 30 淡灵魂艾尔达
    public const int HexaBooster = 2638603;       // 1 气息 → 1 HEXA Booster

    // 激活费用：mastery / enforce / skill / common（气息, 碎片）
    static readonly int[] ActivateSolErda = { 3, 4, 5, 7 };
    static readonly int[] ActivateFragments = { 50, 75, 100, 125 };

    // 升级费用（lv1..lv29，即「从 lv 升到 lv+1」按 level 取下标 level-1）
    static readonly int[] MasterySolErda = { 1, 1, 1, 1, 1, 1, 2, 2, 5, 2, 2, 2, 2, 2, 2, 2, 2, 3, 8, 3, 3, 3, 3, 3, 3, 3, 3, 4, 10 };
    static readonly int[] MasteryFragments = { 15, 18, 20, 23, 25, 28, 30, 33, 100, 40, 45, 50, 55, 60, 65, 70, 75, 80, 175, 85, 90, 95, 100, 105, 110, 115, 120, 125, 250 };
    static readonly int[] EnforceSolErda = { 1, 1, 1, 2, 2, 2, 3, 3, 8, 3, 3, 3, 3, 3, 3, 3, 3, 4, 12, 4, 4, 4, 4, 4, 5, 5, 5, 6, 15 };
    static readonly int[] EnforceFragments = { 23, 27, 30, 34, 38, 42, 45, 49, 150, 60, 68, 75, 83, 90, 98, 105, 113, 120, 263, 128, 135, 143, 150, 158, 165, 173, 180, 188, 375 };
    static readonly int[] SkillSolErda = { 1, 1, 1, 2, 2, 2, 3, 3, 10, 3, 3, 4, 4, 4, 4, 4, 4, 5, 15, 5, 5, 5, 5, 5, 6, 6, 6, 7, 20 };
    static readonly int[] SkillFragments = { 30, 35, 40, 45, 50, 55, 60, 65, 200, 80, 90, 100, 110, 120, 130, 140, 150, 160, 350, 170, 180, 190, 200, 210, 220, 230, 240, 250, 500 };
    static readonly int[] CommonSolErda = { 2, 2, 2, 3, 3, 3, 5, 5, 14, 5, 5, 6, 6, 6, 6, 6, 6, 7, 17, 7, 7, 7, 7, 7, 9, 9, 9, 10, 20 };
    static readonly int[] CommonFragments = { 38, 44, 50, 57, 63, 69, 75, 82, 300, 110, 124, 138, 152, 165, 179, 193, 207, 220, 525, 234, 248, 262, 275, 289, 303, 317, 330, 344, 750 };

    // 属性核心强化碎片：HexaMatrixConstants表，按当前主线等级取下标。
    static readonly int[] StatFragments = { 10, 10, 10, 20, 20, 20, 20, 30, 40, 50, 50 };

    public const long StatResetMeso=10000000;
    public const long StatChangeMeso=1000000;
    public static bool ValidStats(int a,int b,int c) => a>=0 && a<=6 && b>=0 && b<=6 && c>=0 && c<=6 && a!=b && a!=c && b!=c && !(a==4 && (b==5||c==5) || b==4 && (a==5||c==5) || c==4 && (a==5||b==5));
    public static int RollStat(HexaStatView view,Random random)
    {
        double[] weights={.35,.35,.35,.20,.20,.20,.20,.15,.10,.05};
        double main=view.level1>=10 ? 0 : weights[view.level1];
        double roll=random.NextDouble();
        if(roll<main) return 0;
        if(view.level2>=10) return 2;
        if(view.level3>=10) return 1;
        return roll<main+(1-main)/2 ? 1 : 2;
    }

    static HexaResponse Reject(string error) => new HexaResponse { ok = false, error = error };

    public static HexaResponse Handle(DbStore db, HexaRequest request, Random random = null)
    {
        if (db == null || request == null) return Reject("无效 HEXA 矩阵请求");
        HexaCatalog.Ensure();
        lock (db)
        {
            var character = CharactersDao.FindById(db, request.characterId);
            if (character == null) return Reject("角色不存在");
            int level = character.level, job = (int)character.job;
            var state = Load(db, request.characterId);
            // GM 豁免等级 + 38625 任务门禁（2026-11-04 拍板，与 V 矩阵同口径）；
            // 职业白名单仍必须命中——HEXA 核心按职业分发，没命中就没有可操作内容。
            bool eligible = HexaCatalog.JobAllowed(job)
                            && (character.gm > 0
                                || (level >= HexaCatalog.UnlockLevel
                                    && QuestStatusDao.StatusOf(db, request.characterId, HexaCatalog.UnlockQuest) == 2));
            var response = Snapshot(db, character, state, eligible);
            if (request.operation == HexaOperation.Snapshot)
            {
                // 快照不拦门禁：窗口要能画出来并显示锁（eligible=false + error 文案）。
                if (!eligible) response.error = LockText(db, request.characterId, level);
            }
            else if (!eligible)
                return Reject(LockText(db, request.characterId, level));

            if ((request.operation == HexaOperation.SetStat || request.operation == HexaOperation.EnforceStat || request.operation == HexaOperation.Reset)
                && request.preset != 0) return Reject("保存中的核心只供套用，不能直接修改。");
            switch (request.operation)
            {
                case HexaOperation.Snapshot:
                    return response;

                case HexaOperation.Activate:
                {
                    var core = HexaCatalog.Core(request.coreId);
                    if (core == null) return Reject("未知错误。");
                    var group = HexaCatalog.Group(job);
                    if (group == null || !group.Contains(core.id))
                        return Reject("你的职业无法解锁该技能：" + core.name + "。");
                    var learned = NativeMatrixSkills.Levels(db,request.characterId);
                    // 琳等核心还要求普通职业技能；矩阵管辖技能仍以核心表为准。
                    foreach(var row in SkillsDao.ByCharacter(db,request.characterId))
                    {
                        int id=DbStore.Int(row,"skillid");
                        if(!learned.ContainsKey(id)) learned[id]=DbStore.Int(row,"skilllevel");
                    }
                    foreach (var req in core.requiredSkills)
                        if (!learned.TryGetValue(req.Key,out int requiredLevel) || requiredLevel < req.Value)
                            return Reject("前置技能 " + req.Key + " 需要 " + req.Value + " 级。");
                    int slot = GroupOf(group, core.id);
                    int solErda = ActivateSolErda[slot], fragments = ActivateFragments[slot];
                    long have = Fragments(db, request.characterId);
                    if (state.solErda < solErda)
                        return Reject("灵魂艾尔达不足，无法解锁该技能。（需要：" + state.solErda + "/" + solErda + "）");
                    if (have < fragments)
                        return Reject("灵魂艾尔达碎片不足，无法解锁该技能。（需要：" + have + "/" + fragments + "）");
                    if (state.skills.ContainsKey(core.id) && state.skills[core.id] > 0) return Reject("该核心已激活。");
                    if (!request.commit) return response;
                    db.BeginTransaction();
                    try
                    {
                        state.solErda -= solErda;
                        ConsumeFragments(db, request.characterId, fragments);
                        state.skills[core.id] = 1;
                        Save(db, request.characterId, state);
                        db.Save();
                        db.Commit();
                    }
                    catch { db.Rollback(); throw; }
                    return Snapshot(db, character, Load(db, request.characterId),
                        IsEligible(db, character));
                }

                case HexaOperation.Upgrade:
                {
                    var core = HexaCatalog.Core(request.coreId);
                    if (core == null) return Reject("未知错误。");
                    var group = HexaCatalog.Group(job);
                    if (group == null || !group.Contains(core.id)) return Reject("你的职业无法强化该核心。");
                    int current = state.skills.TryGetValue(core.id, out int lv) ? lv : 0;
                    if (current <= 0) return Reject("该核心尚未激活。");
                    int target = request.targetLevel > 0 ? request.targetLevel : current + 1;
                    if (target <= current) return Reject("目标等级不正确。");
                    if (target > core.maxLevel) return Reject(target >= core.maxLevel ? "已经是最高等级。" : "目标等级不正确。");
                    int slot = GroupOf(group, core.id);
                    int solErdaCost = 0, fragmentCost = 0;
                    for (int level2 = current; level2 < target; level2++)
                    {
                        solErdaCost += SolErdaUpgrade(slot, level2);
                        fragmentCost += FragmentUpgrade(slot, level2);
                    }
                    long have = Fragments(db, request.characterId);
                    if (state.solErda < solErdaCost)
                        return Reject("灵魂艾尔达不足。（需要：" + state.solErda + "/" + solErdaCost + "）");
                    if (have < fragmentCost)
                        return Reject("灵魂艾尔达碎片不足。（需要：" + have + "/" + fragmentCost + "）");
                    if (!request.commit)
                    {
                        response.nextSolErda = solErdaCost;
                        response.nextFragments = fragmentCost;
                        return response;
                    }
                    db.BeginTransaction();
                    try
                    {
                        state.solErda -= solErdaCost;
                        ConsumeFragments(db, request.characterId, fragmentCost);
                        state.skills[core.id] = target;
                        Save(db, request.characterId, state);
                        db.Save();
                        db.Commit();
                    }
                    catch { db.Rollback(); throw; }
                    return Snapshot(db, character, Load(db, request.characterId), IsEligible(db, character));
                }

                case HexaOperation.SetStat:
                case HexaOperation.ChangeStat:
                {
                    var core = HexaCatalog.Stat(request.coreId);
                    if (core == null) return Reject("没有这个属性核心。");
                    if (level < core.reqLevel) return Reject(core.reqLevel + " 级才能启用这个属性核心。");
                    if (core.reqCore != 0 && !HasStat(state, core.reqCore)) return Reject("需要先激活前置属性核心。");
                    if (!ValidStats(request.stat1,request.stat2,request.stat3)) return Reject("请选择三条不同的有效能力值。");
                    if(request.preset<0 || request.preset>1) return Reject("预设号不正确。");
                    bool exists = state.stats.TryGetValue(StatKey(core.id,request.preset),out var view);
                    bool change = request.operation == HexaOperation.ChangeStat;
                    if (change && !exists) return Reject("该属性核心尚未激活。");
                    if (!change && exists) return Reject("该属性核心已激活，请使用变更能力值。");
                    if (change && character.meso < StatChangeMeso) return Reject("变更能力值需要100万枫币。");
                    if (!change && (state.solErda < 5 || Fragments(db,request.characterId)<10))
                        return Reject("启用属性核心需要5气息和10碎片。");
                    if (!request.commit) return response;
                    db.BeginTransaction();
                    try
                    {
                        if (change) db.Table("characters").Update(DbStore.Eq("id",(long)request.characterId),new DbRow { ["meso"]=character.meso-StatChangeMeso });
                        else { state.solErda-=5; ConsumeFragments(db,request.characterId,10); }
                        view = StatView(state,core.id,request.preset);
                        view.stat1=request.stat1; view.stat2=request.stat2; view.stat3=request.stat3;
                        if (!change) view.applied=true;
                        Save(db,request.characterId,state); SaveStats(db,request.characterId,state);
                        db.Save(); db.Commit();
                    }
                    catch { db.Rollback(); throw; }
                    return Snapshot(db,character,Load(db,request.characterId),IsEligible(db,character));
                }

                case HexaOperation.EnforceStat:
                {
                    var core = HexaCatalog.Stat(request.coreId);
                    if (core == null) return Reject("没有这个属性核心。");
                    if (!HasStat(state, core.id)) return Reject("该属性核心尚未激活。");
                    var view = StatView(state, core.id, request.preset);
                    if (view == null) return Reject("预设号不正确。");
                    if (view.level != view.level1+view.level2+view.level3)
                        return Reject("旧版核心缺少三条等级记录，请先初始化后强化。");
                    if (view.level >= core.maxLevel) return Reject("强化完成。初始化后可再次强化。");
                    int cost = StatFragmentCost(view.level1);
                    long have = Fragments(db, request.characterId);
                    if (have < cost)
                        return Reject("强化材料不足。（需要：" + have + "/" + cost + "）");
                    if (!request.commit) { response.nextFragments = cost; return response; }
                    db.BeginTransaction();
                    try
                    {
                        ConsumeFragments(db, request.characterId, cost);
                        int index = RollStat(view,random ?? new Random());
                        if(index==0) view.level1++; else if(index==1) view.level2++; else view.level3++;
                        view.level=view.level1+view.level2+view.level3;
                        SaveStats(db, request.characterId, state);
                        db.Save();
                        db.Commit();
                    }
                    catch { db.Rollback(); throw; }
                    return Snapshot(db, character, Load(db, request.characterId), IsEligible(db, character));
                }

                case HexaOperation.Preset:
                {
                    if (request.preset < 0 || request.preset > 1) return Reject("预设号不正确。");
                    if (!HasStat(state, request.coreId)) return Reject("该属性核心尚未激活。");
                    if (!request.commit) return response;
                    // 套用：先清全部 applied，再标记本次预设（源端同样只允许一套生效）。
                    foreach (var stat in state.stats.Values) if(stat.coreId==request.coreId) stat.applied = false;
                    if(!state.stats.TryGetValue(StatKey(request.coreId,request.preset),out var view))
                        return Reject("该预设尚未保存。");
                    view.applied = true;
                    db.BeginTransaction();
                    try
                    {
                        SaveStats(db, request.characterId, state);
                        db.Save();
                        db.Commit();
                    }
                    catch { db.Rollback(); throw; }
                    return Snapshot(db, character, Load(db, request.characterId), IsEligible(db, character));
                }

                case HexaOperation.Reset:
                {
                    if (!state.stats.TryGetValue(StatKey(request.coreId, request.preset), out var target))
                        return Reject("该属性核心尚未激活。");
                    if (target.level <= 0) return Reject("已经是初始状态。");
                    bool legacy=target.level!=target.level1+target.level2+target.level3;
                    if(!legacy && target.level<10) return Reject("核心等级10以上才能初始化。");
                    if(!legacy && character.meso<StatResetMeso) return Reject("初始化需要1000万枫币。");
                    if (!request.commit) return response;
                    db.BeginTransaction();
                    try
                    {
                        db.Table("characters").Update(DbStore.Eq("id",(long)request.characterId),new DbRow { ["meso"]=character.meso-(legacy ? 0 : StatResetMeso) });
                        target.level1=target.level2=target.level3=0;
                        target.level = 0;
                        target.weight = 0;
                        SaveStats(db, request.characterId, state);
                        db.Save();
                        db.Commit();
                    }
                    catch { db.Rollback(); throw; }
                    return Snapshot(db, character, Load(db, request.characterId), IsEligible(db, character));
                }

                case HexaOperation.SaveStat:
                {
                    if(!state.stats.TryGetValue(StatKey(request.coreId,0),out var current) || current.level!=20 || current.level!=current.level1+current.level2+current.level3)
                        return Reject("属性核心20级才能保存。");
                    if(!request.commit) return response;
                    db.BeginTransaction();
                    try
                    {
                        bool applied=state.stats.TryGetValue(StatKey(request.coreId,1),out var old) && old.applied;
                        var copy=StatView(state,request.coreId,1);
                        copy.stat1=current.stat1; copy.stat2=current.stat2; copy.stat3=current.stat3;
                        copy.level1=current.level1; copy.level2=current.level2; copy.level3=current.level3;
                        copy.level=current.level; copy.weight=current.weight; copy.applied=applied;
                        SaveStats(db,request.characterId,state); db.Save(); db.Commit();
                    }
                    catch {db.Rollback();throw;}
                    return Snapshot(db,character,Load(db,request.characterId),IsEligible(db,character));
                }
                case HexaOperation.Rename:
                    return Reject("原端操作8为二级密码，不是核心重命名。");

                default: return Reject("不支援的 HEXA 矩阵操作。");
            }
        }
    }

    public static bool IsEligible(DbStore db, CharRecord character)
        => character != null
           && HexaCatalog.JobAllowed((int)character.job)
           && (character.gm > 0
               || (character.level >= HexaCatalog.UnlockLevel
                   && QuestStatusDao.StatusOf(db, (int)character.id, HexaCatalog.UnlockQuest) == 2));

    /// <summary>未准入时窗口显示的锁定文案。</summary>
    static string LockText(DbStore db, int characterId, int level)
        => level < HexaCatalog.UnlockLevel
            ? "260 级才能使用 HEXA 矩阵"
            : QuestStatusDao.StatusOf(db, characterId, HexaCatalog.UnlockQuest) != 2
                ? "尚未完成 [6转] HEXA 矩阵 任务"
                : "当前职业无法使用 HEXA 矩阵";

    /// <summary>0 mastery / 1 enforce / 2 skill / 3 common（与 jobCore 分区顺序一致）。</summary>
    static int GroupOf(HexaGroup group, int coreId)
    {
        if (Array.IndexOf(group.mastery, coreId) >= 0) return 0;
        if (Array.IndexOf(group.enforce, coreId) >= 0) return 1;
        if (Array.IndexOf(group.skill, coreId) >= 0) return 2;
        return 3;
    }

    static int SolErdaUpgrade(int group, int level)
    {
        var table = group == 0 ? MasterySolErda : group == 1 ? EnforceSolErda : group == 2 ? SkillSolErda : CommonSolErda;
        return level >= 1 && level <= table.Length ? table[level - 1] : 20;
    }

    static int FragmentUpgrade(int group, int level)
    {
        var table = group == 0 ? MasteryFragments : group == 1 ? EnforceFragments : group == 2 ? SkillFragments : CommonFragments;
        return level >= 1 && level <= table.Length ? table[level - 1] : 20;
    }

    // —————————— 窗口侧的费用预览（同一份常量，别在 UI 里再抄一遍） ——————————
    // 282 里最招骂的 bug 就是「窗口显示 3 个气息、点下去扣 4 个」。窗口只调这几个入口，
    // 费用表永远只有 Handle 与这里两处读，改表时一处生效。
    public static void ActivateCost(int group, out int solErda, out int fragments)
    {
        int slot = group < 0 ? 0 : group > 3 ? 3 : group;
        solErda = ActivateSolErda[slot];
        fragments = ActivateFragments[slot];
    }

    /// <summary>从 level 升到 level+1 的费用；level 超出 1..29 时返回 0（表尾以外不再涨价）。</summary>
    public static void UpgradeCost(int group, int level, out int solErda, out int fragments)
    {
        if (level < 1 || level > 29) { solErda = 0; fragments = 0; return; }
        solErda = SolErdaUpgrade(group, level);
        fragments = FragmentUpgrade(group, level);
    }

    /// <summary>属性核心从 level 升到 level+1 要多少碎片（level 0..10）。</summary>
    public static int StatFragmentCost(int level)
        => level >= 0 && level < StatFragments.Length ? StatFragments[level] : 0;

    public static long Fragments(DbStore db, int characterId)
    {
        long total = 0;
        foreach (int id in FragmentItems) total += InventoryDao.CountOf(db, characterId, id);
        return total;
    }

    static void ConsumeFragments(DbStore db, int characterId, int need)
    {
        foreach (int id in FragmentItems)
        {
            if (need <= 0) break;
            int have = InventoryDao.CountOf(db, characterId, id);
            int take = Math.Min(have, need);
            if (take > 0) InventoryDao.ConsumeItem(db, characterId, id, take);
            need -= take;
        }
    }

    sealed class HexaState
    {
        public long solErda;
        public readonly Dictionary<int, int> skills = new Dictionary<int, int>();
        public readonly Dictionary<int, HexaStatView> stats = new Dictionary<int, HexaStatView>();
    }

    static HexaState Load(DbStore db, int characterId)
    {
        var state = new HexaState();
        var row = db.Table("hexa_state").Find(DbStore.Eq("characterid", (long)characterId));
        if (row != null) state.solErda = DbStore.Long(row, "solerda");
        foreach (var r in db.Table("hexa_skills").Select(DbStore.Eq("characterid", (long)characterId)))
            state.skills[DbStore.Int(r, "coreid")] = DbStore.Int(r, "level");
        foreach (var r in db.Table("hexa_stats").Select(DbStore.Eq("characterid", (long)characterId)))
        {
            int coreId = DbStore.Int(r, "coreid");
            int preset = DbStore.Int(r, "preset");
            state.stats[StatKey(coreId, preset)] = new HexaStatView
            {
                coreId = coreId,
                preset = preset,
                level = DbStore.Int(r, "level"),
                stat1 = DbStore.Int(r, "stat1"),
                stat2 = DbStore.Int(r, "stat2"),
                stat3 = DbStore.Int(r, "stat3"),
                weight = DbStore.Int(r, "weight"),
                level1=DbStore.Int(r,"level1"), level2=DbStore.Int(r,"level2"), level3=DbStore.Int(r,"level3"),
                applied = DbStore.Int(r, "applied") != 0,
            };
        }
        return state;
    }

    static int StatKey(int coreId, int preset) => coreId * 4 + (preset & 3);

    /// <summary>
    /// stats 的键是 StatKey(coreId, preset) = coreId*4 + preset，
    /// 所以「这个属性核心激活了没有」不能直接 ContainsKey(coreId)——那样永远查不到，
    /// 会让 EnforceStat/Reset/Rename/Preset 全部误报「尚未激活」。
    /// </summary>
    static bool HasStat(HexaState state, int coreId)
    {
        for (int preset = 0; preset < 4; preset++)
            if (state.stats.ContainsKey(StatKey(coreId, preset))) return true;
        return false;
    }

    static HexaStatView StatView(HexaState state, int coreId, int preset)
    {
        int key = StatKey(coreId, preset);
        if (!state.stats.TryGetValue(key, out var view))
        {
            view = new HexaStatView { coreId = coreId, preset = preset };
            state.stats[key] = view;
        }
        return view;
    }

    static void Save(DbStore db, int characterId, HexaState state)
    {
        var table = db.Table("hexa_state");
        var values = new DbRow { ["solerda"] = state.solErda };
        if (table.Update(DbStore.Eq("characterid", (long)characterId), values) == 0)
        {
            values["characterid"] = (long)characterId;
            table.Insert(values);
        }
        var skills = db.Table("hexa_skills");
        skills.Delete(DbStore.Eq("characterid", (long)characterId));
        foreach (var pair in state.skills)
        {
            if (pair.Value <= 0) continue;
            skills.Insert(new DbRow
            {
                ["characterid"] = (long)characterId,
                ["coreid"] = (long)pair.Key,
                ["level"] = (long)pair.Value,
            });
        }
        NativeMatrixSkills.Sync(db, characterId);
    }

    static void SaveStats(DbStore db, int characterId, HexaState state)
    {
        var table = db.Table("hexa_stats");
        table.Delete(DbStore.Eq("characterid", (long)characterId));
        foreach (var view in state.stats.Values)
        {
            table.Insert(new DbRow
            {
                ["characterid"] = (long)characterId,
                ["coreid"] = (long)view.coreId,
                ["preset"] = (long)view.preset,
                ["level"] = (long)view.level,
                ["stat1"] = (long)view.stat1,
                ["stat2"] = (long)view.stat2,
                ["stat3"] = (long)view.stat3,
                ["weight"] = (long)view.weight,
                ["level1"]=(long)view.level1, ["level2"]=(long)view.level2, ["level3"]=(long)view.level3,
                ["applied"] = view.applied ? 1L : 0L,
            });
        }
    }

    public static List<HexaStatView> Stats(DbStore db,int characterId)
        => new List<HexaStatView>(Load(db,characterId).stats.Values);

    static HexaResponse Snapshot(DbStore db, CharRecord character, HexaState state, bool eligible)
    {
        int job = (int)character.job;
        var response = new HexaResponse
        {
            level = character.level,
            job = job,
            eligible = eligible,
            solErda = state.solErda,
            fragments = Fragments(db, (int)character.id),
            solErdaGaugeMax = HexaCatalog.GaugeMax,
            solErdaGauge = Math.Min(HexaCatalog.GaugeMax, state.solErda),
        };
        var group = HexaCatalog.Group(job);
        if (group != null)
            foreach (int coreId in group.All)
            {
                var core = HexaCatalog.Core(coreId);
                if (core == null) continue;
                int slot = GroupOf(group, coreId);
                int level = state.skills.TryGetValue(coreId, out int lv) ? lv : 0;
                response.cores.Add(new HexaCoreView
                {
                    coreId = coreId,
                    type = core.type,
                    level = level,
                    maxLevel = core.maxLevel,
                    activated = level > 0,
                    name = core.name,
                    desc = core.desc,
                    skills = core.skills,
                    group = slot,
                });
                if (level > 0 && level < core.maxLevel)
                {
                    response.nextSolErda = SolErdaUpgrade(slot, level);
                    response.nextFragments = FragmentUpgrade(slot, level);
                }
            }
        foreach (var view in state.stats.Values)
        {
            var core = HexaCatalog.Stat(view.coreId);
            response.stats.Add(new HexaStatView
            {
                coreId = view.coreId,
                level = view.level,
                maxLevel = core?.maxLevel ?? 0,
                reqLevel = core?.reqLevel ?? 0,
                preset = view.preset,
                stat1 = view.stat1,
                stat2 = view.stat2,
                stat3 = view.stat3,
                weight = view.weight,
                level1=view.level1, level2=view.level2, level3=view.level3,
                applied = view.applied,
            });
        }
        return response;
    }

    // —————————— 索尔艾尔达兑换（CP0491） ——————————

    public sealed class ErdaConversion { public bool ok = true; public string error = ""; public long solErda; }

    /// <summary>1 索尔艾尔达气息 → 30 淡灵魂艾尔达 / 1 HEXA Booster（HexaMatrixConstants.erdaConversions）。</summary>
    public static ErdaConversion Convert(DbStore db, int characterId, int itemId, int count)
    {
        var result = new ErdaConversion();
        if (db == null) { result.ok = false; result.error = "无效转换请求"; return result; }
        lock (db)
        {
            int reward = itemId == FaintSolErda ? 30 : itemId == HexaBooster ? 1 : 0;
            if (reward == 0) { result.ok = false; result.error = "没有这种兑换。"; return result; }
            if (count <= 0) { result.ok = false; result.error = "转换数量不正确。"; return result; }
            var row = db.Table("hexa_state").Find(DbStore.Eq("characterid", (long)characterId));
            long have = row == null ? 0 : DbStore.Long(row, "solerda");
            if (have < count) { result.ok = false; result.error = "持有的灵魂艾尔达不足，无法进行转换。"; return result; }
            var character = CharactersDao.FindById(db, characterId);
            if (character == null) { result.ok = false; result.error = "角色不存在"; return result; }
            db.BeginTransaction();
            try
            {
                var table = db.Table("hexa_state");
                var values = new DbRow { ["solerda"] = have - count };
                if (table.Update(DbStore.Eq("characterid", (long)characterId), values) == 0)
                {
                    values["characterid"] = (long)characterId;
                    table.Insert(values);
                }
                // 必须走 AddItems（背包格位 position>0）：position=0 不算背包格，
                // CountOf 读不到，兑换出来的道具会「看不见也用不了」。
                NativeInventoryService.AddItems(db, character, itemId, reward * count, -1);
                db.Save();
                db.Commit();
            }
            catch { db.Rollback(); throw; }
            result.solErda = have - count;
            return result;
        }
    }
}
