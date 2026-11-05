// 极限属性（Hyper Stat）。权威服务，照 282 UserStatHandler + SkillConstants：
//   - 140 级起每级给 sp = 3 + (level-140)/10，累计到当前等级（SkillConstants.getHyperStatSpByLv）
//   - 单条技能第 lv 级要花 PointCost[lv-1] 点（1,2,4,8,10,15,20,25,30,35,50,65,80,95,110）
//   - 3 套预设，各自独立；洗点 1000 万枫币，切预设 200 万枫币
// 282 服务端把 800004xx 当技能给角色加等级、属性由客户端结算；本项目没有客户端公式引擎，
// 所以属性由 CharacterAttributes 在派生层按 HyperStatCatalog.Value 叠加，数值口径与源端逐级表一致。
using System;
using System.Collections.Generic;

public enum HyperStatOperation { Preview = 0, Apply = 1, Remove = 2, Reset = 3, SwitchPreset = 4, Refund = 5 }

public sealed class HyperStatRequest
{
    public int characterId, preset, skillId, amount;
    public HyperStatOperation operation;
    public bool commit;
}

public sealed class HyperStatLine
{
    public int skillId, slv, value, maxLevel;
    public string name = "", key = "";
}

public sealed class HyperStatResponse
{
    public bool ok = true;
    public string error = "";
    // preset = 这次请求/查看的那一套；activePreset = 身上正在生效的那一套。
    // 两者相等时「套用」没有意义（服务端也会拒），窗口据此置灰按钮。
    public int preset, activePreset, totalSp, spentSp, availableSp, neededSp, level;
    public long meso, resetCost, switchCost;
    public List<HyperStatLine> lines = new List<HyperStatLine>();
}

public static class NativeHyperStatService
{
    static HyperStatResponse Reject(string error) => new HyperStatResponse { ok = false, error = error };

    public static HyperStatResponse Handle(DbStore db, HyperStatRequest request)
    {
        if (db == null || request == null) return Reject("无效极限属性请求");
        lock (db)
        {
            var character = CharactersDao.FindById(db, request.characterId);
            if (character == null) return Reject("角色不存在");
            var state = GrowthDao.Load(db, request.characterId);
            int preset = request.preset >= 0 && request.preset < HyperStatCatalog.Presets ? request.preset : state.hyperPreset;
            var lines = Rows(GrowthDao.HyperStats(db, request.characterId), preset);
            var response = Snapshot(character, state, lines, preset);

            if (request.operation == HyperStatOperation.Preview) return response;
            if (character.level < HyperStatCatalog.UnlockLevel)
                return Reject(HyperStatCatalog.UnlockLevel + " 级才能使用极限属性");

            switch (request.operation)
            {
                case HyperStatOperation.Apply:
                {
                    var info = HyperStatCatalog.Of(request.skillId);
                    if (info == null) return Reject("没有这条极限属性");
                    int amount = request.amount <= 0 ? 1 : request.amount;
                    int current = LevelOf(lines, request.skillId);
                    int target = current + amount;
                    if (target > info.maxLevel) return Reject("已到这条属性的上限");
                    int needed = SpentBetween(current, target);
                    response.neededSp = needed;
                    if (!request.commit) return response;
                    if (response.availableSp < needed) return Reject("极限属性点数不足");
                    Upsert(lines, request.skillId, target);
                    Write(db, request.characterId, preset, lines);
                    return Done(db, request, preset);
                }
                case HyperStatOperation.Remove:
                {
                    var info = HyperStatCatalog.Of(request.skillId);
                    if (info == null) return Reject("没有这条极限属性");
                    int current = LevelOf(lines, request.skillId);
                    int amount = request.amount <= 0 ? 1 : request.amount;
                    if (current <= 0) return Reject("这条属性还没有加点");
                    int target = Math.Max(0, current - amount);
                    response.neededSp = -SpentBetween(target, current);
                    if (!request.commit) return response;
                    Upsert(lines, request.skillId, target);
                    Write(db, request.characterId, preset, lines);
                    return Done(db, request, preset);
                }
                case HyperStatOperation.Reset:
                {
                    response.resetCost = HyperStatCatalog.ResetCost;
                    if (!request.commit) return response;
                    if (character.meso < HyperStatCatalog.ResetCost) return Reject("枫币不足");
                    db.BeginTransaction();
                    try
                    {
                        character.meso -= HyperStatCatalog.ResetCost;
                        CharactersDao.Save(db, character);
                        GrowthDao.SaveHyperStats(db, request.characterId, preset, new HyperStatEntry[0]);
                        db.Save();
                        db.Commit();
                    }
                    catch { db.Rollback(); throw; }
                    return Done(db, request, preset);
                }
                case HyperStatOperation.SwitchPreset:
                {
                    if (preset == state.hyperPreset) return Reject("已经在使用这套预设");
                    response.switchCost = HyperStatCatalog.SwitchCost;
                    if (!request.commit) return response;
                    if (character.meso < HyperStatCatalog.SwitchCost) return Reject("枫币不足");
                    db.BeginTransaction();
                    try
                    {
                        character.meso -= HyperStatCatalog.SwitchCost;
                        CharactersDao.Save(db, character);
                        state.hyperPreset = preset;
                        GrowthDao.Save(db, request.characterId, state);
                        db.Save();
                        db.Commit();
                    }
                    catch { db.Rollback(); throw; }
                    return Done(db, request, preset);
                }
                default:
                    return Reject("未支持的极限属性操作");
            }
        }
    }

    // 该预设里某条技能的等级。
    public static int LevelOf(List<HyperStatEntry> lines, int skillId)
    {
        foreach (var line in lines) if (line.skillId == skillId) return line.slv;
        return 0;
    }

    static List<HyperStatEntry> Rows(List<HyperStatEntry> all, int preset)
    {
        var result = new List<HyperStatEntry>();
        foreach (var line in all) if (line.preset == preset) result.Add(new HyperStatEntry { preset = preset, skillId = line.skillId, slv = line.slv });
        return result;
    }

    // 从 from 级加到 to 级要花多少点（不含 from 那一级本身）。
    public static int SpentBetween(int from, int to)
    {
        int sum = 0;
        for (int level = from + 1; level <= to; level++) sum += HyperStatCatalog.NeededSp(level);
        return sum;
    }

    // 一个预设已花掉的点数 = 每条技能从 1 级到当前级的累计消耗。
    public static int SpentSp(List<HyperStatEntry> lines)
    {
        int sum = 0;
        foreach (var line in lines) sum += SpentBetween(0, line.slv);
        return sum;
    }

    static void Upsert(List<HyperStatEntry> lines, int skillId, int slv)
    {
        foreach (var line in lines) if (line.skillId == skillId) { line.slv = slv; return; }
        lines.Add(new HyperStatEntry { skillId = skillId, slv = slv });
    }

    static void Write(DbStore db, int characterId, int preset, List<HyperStatEntry> lines)
    {
        db.BeginTransaction();
        try
        {
            GrowthDao.SaveHyperStats(db, characterId, preset, lines);
            db.Save();
            db.Commit();
        }
        catch { db.Rollback(); throw; }
    }

    static HyperStatResponse Done(DbStore db, HyperStatRequest request, int preset)
    {
        var character = CharactersDao.FindById(db, request.characterId);
        var state = GrowthDao.Load(db, request.characterId);
        var lines = Rows(GrowthDao.HyperStats(db, request.characterId), preset);
        return Snapshot(character, state, lines, preset);
    }

    static HyperStatResponse Snapshot(CharRecord character, GrowthState state, List<HyperStatEntry> lines, int preset)
    {
        int total = HyperStatCatalog.TotalSp(character.level);
        int spent = SpentSp(lines);
        var response = new HyperStatResponse
        {
            preset = preset, activePreset = state.hyperPreset, level = character.level,
            totalSp = total, spentSp = spent,
            availableSp = Math.Max(0, total - spent), meso = character.meso,
            resetCost = HyperStatCatalog.ResetCost, switchCost = HyperStatCatalog.SwitchCost,
        };
        foreach (var line in lines)
        {
            var info = HyperStatCatalog.Of(line.skillId);
            if (info == null) continue;
            response.lines.Add(new HyperStatLine
            {
                skillId = info.skillId, name = info.name, key = info.key, slv = line.slv,
                maxLevel = info.maxLevel, value = info.Value(line.slv),
            });
        }
        return response;
    }
}
