// V 矩阵（282 五转核心成长）。权威服务，逐条照 Tms282VMatrixService.plan 的语义：
//
//   op 0  V 点 → 限时核心宝石（count 颗，每颗 transformCost 点，有效期 transformMinutes 分钟）
//   op 1  使用核心宝石：首颗若带 openCore 且该核心未开 → 直接开核心、不产点；其余每颗 +points
//   op 2  强化核心到 targetLevel（特殊核心只能到 1 级，扣 registerSpecialCost）
//   op 3  特殊核心延期（本项目决议：特殊核心永久有效，这一支直接拒，不白扣点）
//   op 4  启用特殊核心（全局唯一，启用前清掉其它）
//   op 5  停用特殊核心
//   op 6  重置：扣 resetMeso，退还 type<=1 已投的 V 点，回到 resetFloor（特殊核心不退）
//
// 准入：level >= 200 && 任务 1465 完成 && 职业在 coreGroup 里（源端 eligible 同口径）。
// 约束：任何路径结束后 points <= 9999，超出整体拒绝（不是截断）。
//
// 与源端的一处刻意差异：282 记 expiresAt（fileTime），本项目决议「特殊核心无限时间」，
// 所以 selected 不看期限、op 3 也不再按剩余天数收点。
using System;
using System.Collections.Generic;

public enum VMatrixOperation
{
    Transform = 0, UseGemstone = 1, Upgrade = 2,
    ExtendSpecial = 3, SelectSpecial = 4, UnselectSpecial = 5, Reset = 6,
    // 7 = 只读快照。282 打开窗口时是单独的 LP 包，本项目所有窗口都走同一个 Request，
    // 所以必须有一个不产生任何副作用的 op，否则「空请求」会掉进 Transform 的 count 校验。
    Snapshot = 7,
}

public sealed class VMatrixRequest
{
    public int characterId;
    public VMatrixOperation operation;
    // Upgrade 用 coreId + targetLevel；UseGemstone 用 itemId + count；Transform 用 count。
    public int coreId, targetLevel, itemId, count;
    public bool preview, commit;
}

public sealed class VMatrixCoreView
{
    public int coreId, type, level, maxLevel, protectedLevel;
    public bool selected;
    public string name = "", desc = "";
    public int[] skills = new int[0];
}

public sealed class VMatrixResponse
{
    public bool ok = true;
    public string error = "";
    public long points, meso;
    public int level, job, revision, maxPoints = VMatrixCatalog.MaxPoints;
    public bool eligible;
    // 服务端经济参数：窗口直接用，避免两边各存一份会漂。
    public int resetMeso, transformCost, transformItem, transformMinutes, registerCost;
    public List<VMatrixCoreView> cores = new List<VMatrixCoreView>();
    // 31 格：下标即槽位号（0..3 技能 / 4..9 强化 / 10..30 共通），0 = 空。
    public int[] slots = new int[VMatrixSlots.Count];
}

public static class VMatrixSlots
{
    public const int SkillCount = 4, EnforceCount = 6, CommonCount = 21;
    public const int Count = SkillCount + EnforceCount + CommonCount;    // 31（282 slotPos 坐标数）

    /// <summary>组内核心列表 → 31 格分配。技能 4 / 强化 6 / 共通 19（末两格留白）。</summary>
    public static int[] Assign(VMatrixGroup group)
    {
        var slots = new int[Count];
        if (group == null) return slots;
        Fill(slots, 0, group.skill);
        Fill(slots, SkillCount, group.enforce);
        Fill(slots, SkillCount + EnforceCount, group.common);
        return slots;
    }

    static void Fill(int[] slots, int start, int[] ids)
    {
        if (ids == null) return;
        for (int i = 0; i < ids.Length && start + i < slots.Length; i++) slots[start + i] = ids[i];
    }

    public static int SectionOf(int slot)
        => slot < SkillCount ? 0 : slot < SkillCount + EnforceCount ? 1 : 2;
}

// vmatrix_state + vmatrix_cores 的内存镜像。
public sealed class VMatrixState
{
    public long points;
    public int revision;
    public readonly Dictionary<int, VMatrixCoreView> cores = new Dictionary<int, VMatrixCoreView>();

    public VMatrixCoreView Find(int coreId)
        => cores.TryGetValue(coreId, out var core) ? core : null;

    public int LevelOf(int coreId) => Find(coreId)?.level ?? 0;
}

public static class NativeVMatrixService
{
    static VMatrixResponse Reject(string error) => new VMatrixResponse { ok = false, error = error };

    public static VMatrixResponse Handle(DbStore db, VMatrixRequest request)
    {
        if (db == null || request == null) return Reject("无效 V 矩阵请求");
        VMatrixCatalog.Ensure();
        lock (db)
        {
            var character = CharactersDao.FindById(db, request.characterId);
            if (character == null) return Reject("角色不存在");
            var state = Load(db, request.characterId);
            var response = Snapshot(db, character, state);
            if (request.operation == VMatrixOperation.Transform) { /* 转换不要求准入，源端同样只查 IN_FIELD */ }
            else if (request.operation == VMatrixOperation.Snapshot)
            {
                // 快照不拦准入：窗口要能画出来并给出锁定文案（eligible=false + 下面的 lockText）。
                if (!response.eligible) response.error = LockText(db, request.characterId, response.level);
            }
            else if (!response.eligible)
                return Reject(LockText(db, request.characterId, response.level));

            switch (request.operation)
            {
                case VMatrixOperation.Snapshot:
                    return response;

                case VMatrixOperation.Transform:
                {
                    if (request.count <= 0) return Reject("转换数量不正确。");
                    long cost = (long)request.count * VMatrixCatalog.TransformCost;
                    if (state.points < cost) return Reject("V 点数不足，无法转换。");
                    if (!request.commit) { response.points = state.points - cost; return response; }
                    state.points -= cost;
                    GrantGemstones(db, character, VMatrixCatalog.TransformItem, request.count,
                        VMatrixCatalog.TransformMinutes);
                    break;
                }

                case VMatrixOperation.UseGemstone:
                {
                    var gem = VMatrixCatalog.Gemstone(request.itemId);
                    if (gem == null) return Reject("没有这种核心宝石。");
                    if (request.count <= 0) return Reject("转换数量不正确。");
                    if (InventoryDao.CountOf(db, request.characterId, request.itemId) < request.count)
                        return Reject("没有足够的核心宝石。");
                    if (state.points >= VMatrixCatalog.MaxPoints) return Reject("V 点数已达 9999 点上限。");
                    if (gem.reqQuest != 0 && QuestStatusDao.StatusOf(db, request.characterId, gem.reqQuest) != 2)
                        return Reject(string.IsNullOrEmpty(gem.questAlert) ? "尚未完成使用此核心宝石所需的任务。" : gem.questAlert);
                    if (!request.commit)
                    {
                        response.points = state.points + (long)request.count * gem.points;
                        return response;
                    }
                    int pointItems = request.count;
                    if (gem.openCore != 0)
                    {
                        if (!VMatrixCatalog.Allowed((int)character.job, gem.openCore)) return Reject("此职业无法使用所选核心。");
                        var existing = state.Find(gem.openCore);
                        if (existing == null || existing.level == 0)
                        {
                            SetLevel(state, gem.openCore, 1);
                            pointItems--;      // 282：第一颗只开核心，不产 V 点
                        }
                    }
                    InventoryDao.ConsumeItem(db, request.characterId, request.itemId, request.count);
                    long after = state.points + (long)pointItems * gem.points;
                    if (after > VMatrixCatalog.MaxPoints) return Reject("使用后将超过 V 点数 9999 点上限。");
                    state.points = after;
                    break;
                }

                case VMatrixOperation.Upgrade:
                {
                    var core = VMatrixCatalog.Core(request.coreId);
                    if (core == null) return Reject("没有这个核心。");
                    if (!VMatrixCatalog.Allowed((int)character.job, core.id)) return Reject("此职业无法使用所选核心。");
                    int current = state.LevelOf(core.id);
                    if (core.IsSpecial)
                    {
                        if (request.targetLevel != 1 || current > 0) return Reject("特殊核心已经开启。");
                        if (state.points < VMatrixCatalog.RegisterSpecialCost) return Reject("V 点数不足。");
                        if (!request.commit) return response;
                        state.points -= VMatrixCatalog.RegisterSpecialCost;
                        SetLevel(state, core.id, 1);
                        break;
                    }
                    if (core.type > 1) return Reject("核心目标等级不正确。");
                    if (request.targetLevel <= current || request.targetLevel > core.maxLevel)
                        return Reject("核心目标等级不正确。");
                    if (!core.selfOpen && current == 0) return Reject("此核心必须先使用指定道具开启。");
                    long cost = VMatrixCatalog.CostBetween(core.id, current, request.targetLevel);
                    if (cost < 0) return Reject("核心目标等级不正确。");
                    if (state.points < cost) return Reject("V 点数不足。");
                    response.points = state.points - cost;
                    if (!request.commit) return response;
                    state.points -= cost;
                    SetLevel(state, core.id, request.targetLevel);
                    break;
                }

                case VMatrixOperation.ExtendSpecial:
                {
                    var core = VMatrixCatalog.Core(request.coreId);
                    if (core == null || !core.IsSpecial) return Reject("没有这个特殊核心。");
                    // 本项目特殊核心永久有效（17 §7 决议），延期没有意义，直接告知而不是白扣 V 点。
                    return Reject("本服特殊核心永久有效，无需延期。");
                }

                case VMatrixOperation.SelectSpecial:
                case VMatrixOperation.UnselectSpecial:
                {
                    var core = VMatrixCatalog.Core(request.coreId);
                    if (core == null || !core.IsSpecial) return Reject("没有这个特殊核心。");
                    var view = state.Find(core.id);
                    if (view == null || view.level <= 0) return Reject("特殊核心尚未开启或已到期。");
                    bool select = request.operation == VMatrixOperation.SelectSpecial;
                    if (select && view.selected) return Reject("此特殊核心已经启用。");
                    if (!select && !view.selected) return Reject("此特殊核心尚未启用。");
                    if (!request.commit) return response;
                    foreach (var other in state.cores.Values) other.selected = false;   // 全局只允许一个
                    view.selected = select;
                    break;
                }

                case VMatrixOperation.Reset:
                {
                    if (character.meso < VMatrixCatalog.ResetMeso) return Reject("枫币不足，无法重置 V 矩阵。");
                    if (!request.commit)
                    {
                        response.points = state.points + Refundable(state, (int)character.job);
                        response.meso = character.meso - VMatrixCatalog.ResetMeso;
                        return response;
                    }
                    character.meso -= VMatrixCatalog.ResetMeso;
                    foreach (var view in state.cores.Values)
                    {
                        var core = VMatrixCatalog.Core(view.coreId);
                        if (core == null || core.type > 1) continue;   // 特殊核心不退（源端同口径）
                        int floor = Math.Min(view.level,
                            Math.Max(view.protectedLevel, VMatrixCatalog.ResetFloor((int)character.job, view.coreId)));
                        if (view.level > floor) state.points += VMatrixCatalog.CostBetween(view.coreId, floor, view.level);
                        view.level = floor;
                        view.selected = false;
                    }
                    break;
                }

                default: return Reject("不支援的 V 矩阵操作。");
            }

            if (state.points > VMatrixCatalog.MaxPoints) return Reject("操作后将超过 V 点数 9999 点上限。");
            state.revision++;
            db.BeginTransaction();
            try
            {
                Save(db, request.characterId, state);
                CharactersDao.Save(db, character);
                db.Save();
                db.Commit();
            }
            catch { db.Rollback(); throw; }
            return Snapshot(db, character, state);
        }
    }

    /// <summary>未准入时窗口显示的锁定文案（与 282 的三条拒绝文案同源）。</summary>
    static string LockText(DbStore db, int characterId, int level)
        => level < VMatrixCatalog.UnlockLevel
            ? "200 级才能使用 V 矩阵"
            : QuestStatusDao.StatusOf(db, characterId, VMatrixCatalog.FifthJobQuest) != 2
                ? "尚未完成五转任务，无法使用 V 矩阵"
                : "当前职业无法使用 V 矩阵";

    static long Refundable(VMatrixState state, int job)    {
        long total = 0;
        foreach (var view in state.cores.Values)
        {
            var core = VMatrixCatalog.Core(view.coreId);
            if (core == null || core.type > 1) continue;
            int floor = Math.Min(view.level, Math.Max(view.protectedLevel, VMatrixCatalog.ResetFloor(job, view.coreId)));
            if (view.level > floor) total += VMatrixCatalog.CostBetween(view.coreId, floor, view.level);
        }
        return total;
    }

    static void SetLevel(VMatrixState state, int coreId, int level)
    {
        if (!state.cores.TryGetValue(coreId, out var view))
        {
            view = new VMatrixCoreView { coreId = coreId };
            state.cores[coreId] = view;
        }
        view.level = level;
    }

    // 宝石必须按背包格位落库（position>0）：直接 InsertItem(position=0) 会写进「非背包格」，
    // InventoryDao.CountOf 只统计 IsBagPosition，于是刚转出来的宝石用不了。
    // 统一走 NativeInventoryService.AddItems（源端 InventoryManipulator.addFromDrop 同一套放包规则）。
    static void GrantGemstones(DbStore db, CharRecord character, int itemId, int count, int minutes)
    {
        long expires = minutes <= 0 ? -1
            : DateTimeOffset.UtcNow.AddMinutes(minutes).ToUnixTimeMilliseconds();
        NativeInventoryService.AddItems(db, character, itemId, count, expires);
    }

    static VMatrixState Load(DbStore db, int characterId)
    {
        var state = new VMatrixState();
        var row = db.Table("vmatrix_state").Find(DbStore.Eq("characterid", (long)characterId));
        if (row != null)
        {
            state.points = DbStore.Long(row, "points");
            state.revision = DbStore.Int(row, "revision");
        }
        foreach (var core in db.Table("vmatrix_cores").Select(DbStore.Eq("characterid", (long)characterId)))
        {
            int coreId = DbStore.Int(core, "coreid");
            state.cores[coreId] = new VMatrixCoreView
            {
                coreId = coreId,
                level = DbStore.Int(core, "level"),
                selected = DbStore.Int(core, "selected") != 0,
                protectedLevel = DbStore.Int(core, "protectedlevel"),
            };
        }
        return state;
    }

    static void Save(DbStore db, int characterId, VMatrixState state)
    {
        var table = db.Table("vmatrix_state");
        var values = new DbRow { ["points"] = state.points, ["revision"] = (long)state.revision };
        if (table.Update(DbStore.Eq("characterid", (long)characterId), values) == 0)
        {
            values["characterid"] = (long)characterId;
            table.Insert(values);
        }
        var cores = db.Table("vmatrix_cores");
        cores.Delete(DbStore.Eq("characterid", (long)characterId));
        foreach (var view in state.cores.Values)
        {
            if (view.level <= 0 && !view.selected) continue;
            cores.Insert(new DbRow
            {
                ["characterid"] = (long)characterId,
                ["coreid"] = (long)view.coreId,
                ["level"] = (long)view.level,
                ["selected"] = view.selected ? 1L : 0L,
                ["expiresat"] = 0L,
                ["protectedlevel"] = (long)view.protectedLevel,
            });
        }
        NativeMatrixSkills.Sync(db, characterId);
    }

    static VMatrixResponse Snapshot(DbStore db, CharRecord character, VMatrixState state)
    {
        int job = (int)character.job;
        var response = new VMatrixResponse
        {
            points = state.points,
            meso = character.meso,
            level = character.level,
            job = job,
            revision = state.revision,
            resetMeso = VMatrixCatalog.ResetMeso,
            transformCost = VMatrixCatalog.TransformCost,
            transformItem = VMatrixCatalog.TransformItem,
            transformMinutes = VMatrixCatalog.TransformMinutes,
            registerCost = VMatrixCatalog.RegisterSpecialCost,
            // 准入：等级 + 五转任务 + 职业核心组（源端 eligible 同口径）。
            // GM 豁免前两档（2026-11-04 拍板）：GM 从主菜单入口点开就要能直接操作，
            // 不用先练到 200 级/跑完 1465。职业组仍必须存在——核心是按职业分发的，
            // 没有组就没有可操作的格子，那不是权限问题。
            eligible = VMatrixCatalog.Group(job) != null
                        && (character.gm > 0
                            || (character.level >= VMatrixCatalog.UnlockLevel
                                && QuestStatusDao.StatusOf(db, (int)character.id, VMatrixCatalog.FifthJobQuest) == 2)),
        };
        var group = VMatrixCatalog.Group(job);
        response.slots = VMatrixSlots.Assign(group);
        foreach (var coreId in response.slots)
        {
            if (coreId == 0) continue;
            var core = VMatrixCatalog.Core(coreId);
            if (core == null) continue;
            var view = state.Find(coreId);
            response.cores.Add(View(core, view));
        }
        // 特殊核心不进槽位（282 是独立的 specialCore 子窗），单独附在末尾。
        foreach (var core in VMatrixCatalog.AllCores)
        {
            if (!core.IsSpecial) continue;
            var view = state.Find(core.id);
            if (view == null) continue;
            response.cores.Add(View(core, view));
        }
        return response;
    }

    static VMatrixCoreView View(VMatrixCore core, VMatrixCoreView view) => new VMatrixCoreView
    {
        coreId = core.id,
        type = core.type,
        maxLevel = core.maxLevel,
        name = core.name,
        desc = core.desc,
        skills = core.skills,
        level = view?.level ?? 0,
        selected = view?.selected ?? false,
        protectedLevel = view?.protectedLevel ?? 0,
    };
}


// 核心表是唯一权威；skills是给现有技能系统消费的投影，不能被旧角色快照回写。
public static class NativeMatrixSkills
{
    public static Dictionary<int,int> Levels(DbStore db, int characterId)
    {
        var result = new Dictionary<int,int>();
        var chr = CharactersDao.FindById(db, characterId);
        if (chr == null) return result;
        bool hasV = db.Table("vmatrix_state").Find(DbStore.Eq("characterid", (long)characterId)) != null;
        bool hasH = db.Table("hexa_state").Find(DbStore.Eq("characterid", (long)characterId)) != null;
        var vg = VMatrixCatalog.Group(chr.job);
        var hg = HexaCatalog.Group(chr.job);
        if (hasV && vg != null)
        {
            foreach (int id in VMatrixSlots.Assign(vg))
            {
                var core = VMatrixCatalog.Core(id);
                if (core == null) continue;
                var row = db.Table("vmatrix_cores").Find(r => DbStore.Int(r,"characterid")==characterId && DbStore.Int(r,"coreid")==id);
                int level = Math.Min(core.maxLevel, Math.Max(0, DbStore.Int(row,"level")));
                Merge(result,core.skills,level); Merge(result,core.subSkills,level);
            }
        }
        if (hasH && hg != null)
            foreach (int id in hg.All)
            {
                var core = HexaCatalog.Core(id);
                if (core == null) continue;
                var row = db.Table("hexa_skills").Find(r => DbStore.Int(r,"characterid")==characterId && DbStore.Int(r,"coreid")==id);
                int level = Math.Min(core.maxLevel, Math.Max(0, DbStore.Int(row,"level")));
                Merge(result,core.skills,level); Merge(result,core.subSkills,level);
            }
        return result;
    }

    static void Merge(Dictionary<int,int> result, int[] skills, int level)
    {
        foreach (int skill in skills)
            result[skill] = Math.Max(result.TryGetValue(skill,out int old) ? old : 0,level);
    }

    public static bool Sync(DbStore db, int characterId)
    {
        bool changed = false;
        var rows = SkillsDao.ByCharacter(db,characterId);
        foreach (var pair in Levels(db,characterId))
        {
            var row = rows.Find(r=>DbStore.Int(r,"skillid")==pair.Key);
            if (row == null && pair.Value == 0) continue;
            if (row != null && DbStore.Int(row,"skilllevel")==pair.Value && DbStore.Int(row,"masterlevel")>=pair.Value) continue;
            SkillsDao.Upsert(db,characterId,pair.Key,pair.Value,Math.Max(pair.Value,DbStore.Int(row,"masterlevel")));
            changed = true;
        }
        return changed;
    }
}
