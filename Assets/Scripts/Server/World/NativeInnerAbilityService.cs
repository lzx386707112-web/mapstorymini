// 内在能力（Inner Ability）。权威服务，照 282：
//   荣誉值洗练 UserHandler.handleUserRequestCharacterPotentialSkillRandSetUi：
//     费用按当前 grade（100/200/1500/8000）叠加锁行（3000 / +5000）；先扣荣誉值，
//     再按 grade 升降概率掷一次，然后重摇「未锁定」的行（同预设内不重复）。
//   循环器 Tms282InnerAbilityHandlers：整三行重摇 + grade 按道具族升降，摇完进待确认状态，
//     由确认窗 accept/decline（本项目落 character_growth.abilityChoice 作 journal）。
//   行解锁：1 行天生、30 级第 2 行、50 级第 3 行。
using System;
using System.Collections.Generic;

public enum AbilityOperation { Preview = 0, Honor = 1, Choose = 2, SwitchPreset = 3, Circulator = 4 }

public sealed class AbilityRequest
{
    public int characterId, preset;
    public AbilityOperation operation;
    public List<int> lockedRows = new List<int>();
    public long circulatorInventoryItemId;
    public bool commit, accept;
}

public sealed class AbilityLine
{
    public int potKey, skillId, slv, grade, value;
    public string name = "", key = "";
}

public sealed class AbilityResponse
{
    public bool ok = true, pending;
    public string error = "";
    // preset = 这次查看的那一套；activePreset = 身上正在生效的那一套。
    public int preset, activePreset, grade, lineCount, cost;
    public long honorExp;
    public List<AbilityLine> lines = new List<AbilityLine>();
    public List<AbilityLine> candidate = new List<AbilityLine>();
}

public static class NativeInnerAbilityService
{
    static AbilityResponse Reject(string error) => new AbilityResponse { ok = false, error = error };

    public static AbilityResponse Handle(DbStore db, AbilityRequest request, Random random = null)
    {
        if (db == null || request == null) return Reject("无效内在能力请求");
        random ??= new Random();
        lock (db)
        {
            var character = CharactersDao.FindById(db, request.characterId);
            if (character == null) return Reject("角色不存在");
            var state = GrowthDao.Load(db, request.characterId);
            int preset = request.preset >= 0 && request.preset < AbilityCatalog.Presets ? request.preset : state.abilityPreset;
            var lines = Rows(GrowthDao.Potentials(db, request.characterId), preset);
            var response = Snapshot(state, lines, preset, LockedCount(request, lines.Count));
            response.lineCount = Math.Min(lines.Count, UnlockedLines(character.level));

            if (request.operation == AbilityOperation.Preview) return response;
            return request.operation switch
            {
                AbilityOperation.Honor => Honor(db, request, character, state, lines, preset, response, random),
                AbilityOperation.Choose => Choose(db, request, state, preset, response),
                AbilityOperation.SwitchPreset => SwitchPreset(db, request, character, state, preset, response),
                AbilityOperation.Circulator => Circulator(db, request, character, state, lines, preset, response, random),
                _ => Reject("未支持的内在能力操作"),
            };
        }
    }

    // 循环器：整三行重摇，摇完进待确认状态（source: Tms282InnerAbilityHandlers.onCirculator）。
    // 5062xxx 只能升档，2702xxx 可升可降；两者的 grade 判定与荣誉洗练共用 RollGrade 的升降概率。
    static AbilityResponse Circulator(DbStore db, AbilityRequest request, CharRecord character, GrowthState state,
        List<PotentialEntry> lines, int preset, AbilityResponse response, Random random)
    {
        if (UnlockedLines(character.level) < AbilityCatalog.Lines) return Reject("需要解锁 3 行内在能力才能使用循环器");
        if (lines.Count < AbilityCatalog.Lines) return Reject("需要 3 行内在能力才能使用循环器");
        if (response.pending) return Reject("请先确认上一次循环器的结果");
        var item = db.Table("inventoryitems").Find(x => DbStore.Int(x, "characterid") == request.characterId
            && DbStore.Long(x, "inventoryitemid") == request.circulatorInventoryItemId);
        if (item == null || DbStore.Int(item, "quantity") < 1 || DbStore.Int(item, "position") <= 0) return Reject("循环器不存在");
        int itemId = DbStore.Int(item, "itemid");
        bool upwardOnly = itemId / 1000 == 5062;
        int grade = response.grade;
        if (upwardOnly && grade < 1) return Reject("该循环器只能用于稀有以上等级的内在能力");
        if (!upwardOnly && grade >= 3) return Reject("该循环器不能用于传说等级的内在能力");

        int newGrade = upwardOnly ? (grade < 3 && random.Next(100) < AbilityCatalog.GradeUpRate(grade) ? grade + 1 : grade)
                                  : RollGrade(grade, random);
        var rolled = RollLines(lines, new HashSet<int>(), newGrade, random);
        response.candidate = Describe(rolled);
        response.pending = true;
        if (!request.commit) return response;

        db.BeginTransaction();
        try
        {
            int quantity = DbStore.Int(item, "quantity");
            if (quantity <= 1) CharService.RemoveItem(db, request.circulatorInventoryItemId);
            else item["quantity"] = (long)quantity - 1;
            state.abilityChoice = Serialize(rolled);
            GrowthDao.Save(db, request.characterId, state);
            db.Save();
            db.Commit();
        }
        catch { db.Rollback(); throw; }
        var done = Snapshot(GrowthDao.Load(db, request.characterId), Rows(GrowthDao.Potentials(db, request.characterId), preset), preset, LockedCount(request, lines.Count));
        done.candidate = response.candidate;
        done.pending = true;
        return done;
    }

    // 荣誉值洗练：扣费 → 掷 grade 升降 → 重摇未锁定的行。
    static AbilityResponse Honor(DbStore db, AbilityRequest request, CharRecord character, GrowthState state,
        List<PotentialEntry> lines, int preset, AbilityResponse response, Random random)
    {
        int unlocked = UnlockedLines(character.level);
        if (lines.Count < unlocked) return Reject("请先解锁 " + unlocked + " 行内在能力");
        var locked = new HashSet<int>();
        foreach (int row in request.lockedRows) if (row >= 0 && row < lines.Count) locked.Add(row);
        if (locked.Count >= lines.Count) return Reject("至少要留一行可以洗练");

        int grade = response.grade;
        int cost = AbilityCatalog.CostOf(grade, locked.Count);
        response.cost = cost;
        if (!request.commit) return response;
        if (state.honorExp < cost) return Reject("荣誉值不足");

        int newGrade = RollGrade(grade, random);
        var rolled = RollLines(lines, locked, newGrade, random);
        response.candidate = Describe(rolled);

        db.BeginTransaction();
        try
        {
            state.honorExp -= cost;
            GrowthDao.Save(db, request.characterId, state);
            GrowthDao.SavePotentials(db, request.characterId, preset, rolled);
            db.Save();
            db.Commit();
        }
        catch { db.Rollback(); throw; }

        var saved = GrowthDao.Potentials(db, request.characterId);
        var current = Rows(saved, preset);
        var done = Snapshot(GrowthDao.Load(db, request.characterId), current, preset, LockedCount(request, lines.Count));
        done.cost = cost;
        done.candidate = response.candidate;
        return done;
    }

    // 循环器确认窗：accept 才写进预设，decline 什么都不改（源端同为两段式）。
    static AbilityResponse Choose(DbStore db, AbilityRequest request, GrowthState state, int preset, AbilityResponse response)
    {
        if (string.IsNullOrEmpty(state.abilityChoice)) return Reject("没有待确认的内在能力结果");
        var pending = Parse(state.abilityChoice);
        response.pending = true;
        response.candidate = Describe(pending);
        if (!request.commit) return response;
        db.BeginTransaction();
        try
        {
            if (request.accept) GrowthDao.SavePotentials(db, request.characterId, preset, pending);
            state.abilityChoice = "";
            GrowthDao.Save(db, request.characterId, state);
            db.Save();
            db.Commit();
        }
        catch { db.Rollback(); throw; }
        var done = Snapshot(GrowthDao.Load(db, request.characterId), Rows(GrowthDao.Potentials(db, request.characterId), preset), preset, LockedCount(request, pending.Count));
        done.pending = false;
        return done;
    }

    static AbilityResponse SwitchPreset(DbStore db, AbilityRequest request, CharRecord character, GrowthState state,
        int preset, AbilityResponse response)
    {
        if (preset == state.abilityPreset) return Reject("已经在使用这套预设");
        if (!request.commit) return response;
        db.BeginTransaction();
        try
        {
            state.abilityPreset = preset;
            GrowthDao.Save(db, request.characterId, state);
            db.Save();
            db.Commit();
        }
        catch { db.Rollback(); throw; }
        return Snapshot(GrowthDao.Load(db, request.characterId), Rows(GrowthDao.Potentials(db, request.characterId), preset), preset, LockedCount(request, response.lines.Count));
    }

    // 行解锁：1 行天生、30 级第 2 行、50 级第 3 行（任务 12394/12395/12396 的等价物）。
    public static int UnlockedLines(int level)
        => level >= AbilityCatalog.Line3Level ? 3 : level >= AbilityCatalog.Line2Level ? 2 : 1;

    // grade 升降：先掷升，再掷降（与 265 同序，顺序反了概率就不等价）。
    public static int RollGrade(int grade, Random random)
    {
        if (grade < 3 && random.Next(100) < AbilityCatalog.GradeUpRate(grade)) return grade + 1;
        if (grade > 0 && random.Next(100) < AbilityCatalog.GradeDownRate(grade)) return grade - 1;
        return grade;
    }

    // 重摇：锁定行原样保留，其余行在同预设内不重复地抽（源端 used 集合口径）。
    static List<PotentialEntry> RollLines(List<PotentialEntry> lines, HashSet<int> locked, int grade, Random random)
    {
        var used = new HashSet<int>();
        foreach (var line in lines) used.Add(line.skillId);
        var result = new List<PotentialEntry>();
        for (int row = 0; row < lines.Count; row++)
        {
            if (locked.Contains(row)) { result.Add(lines[row]); continue; }
            int skillId = 0;
            int guard = 0;
            do
            {
                skillId = AbilityCatalog.BaseId + random.Next(AbilityCatalog.MaxId - AbilityCatalog.BaseId + 1);
            } while (used.Contains(skillId) && ++guard < 500);
            used.Add(skillId);
            var info = AbilityCatalog.Of(skillId);
            int slv = 1 + random.Next(grade * 10, grade * 10 + 10);   // base+1 .. base+10（上界闭区间）
            if (info != null) slv = AbilityCatalog.ClampLevel(info, grade, slv);
            // grade 一律以本次掷出的为准：源端把三行写成同一个 grade，而不是按行推导。
            result.Add(new PotentialEntry { potKey = row, skillId = skillId, slv = slv, grade = grade });
        }
        return result;
    }

    static List<PotentialEntry> Rows(List<PotentialEntry> all, int preset)
    {
        var result = new List<PotentialEntry>();
        foreach (var line in all) if (line.preset == preset) result.Add(line);
        result.Sort((a, b) => a.potKey.CompareTo(b.potKey));
        return result;
    }

    // 锁行数夹到「最多锁到只剩一行」：锁满整张表时洗练没有意义，服务端会直接拒。
    static int LockedCount(AbilityRequest request, int lineCount)
    {
        int locked = request?.lockedRows?.Count ?? 0;
        return Math.Max(0, Math.Min(locked, Math.Max(0, lineCount - 1)));
    }

    // lockedCount 由调用方给：锁行是客户端的选择，服务端只在本次请求里看得到。
    // Preview 也传进来，窗口才能在点「使用」之前就显示这次要花多少名聲值。
    static AbilityResponse Snapshot(GrowthState state, List<PotentialEntry> lines, int preset, int lockedCount = 0)
    {
        int grade = 0;
        foreach (var line in lines) grade = Math.Max(grade, line.grade);
        bool pending = !string.IsNullOrEmpty(state.abilityChoice);
        var response = new AbilityResponse
        {
            preset = preset, activePreset = state.abilityPreset, grade = grade, honorExp = state.honorExp,
            lineCount = lines.Count, pending = pending,
            // 锁满全部行时「使用」会直接被拒，预览按最贵的那档给，不误导成免费。
            cost = AbilityCatalog.CostOf(grade, Math.Min(Math.Max(0, lockedCount), Math.Max(0, lines.Count - 1))),
        };
        response.lines = Describe(lines);
        // 待确认的结果也要一起给，否则窗口得再发一次 Choose 预览才知道要显示什么。
        if (pending) response.candidate = Describe(Parse(state.abilityChoice));
        return response;
    }

    static List<AbilityLine> Describe(List<PotentialEntry> rows)
    {
        var result = new List<AbilityLine>();
        foreach (var row in rows)
        {
            var info = AbilityCatalog.Of(row.skillId);
            var line = new AbilityLine { potKey = row.potKey, skillId = row.skillId, slv = row.slv, grade = row.grade };
            if (info != null)
            {
                line.name = info.name;
                // 多键技能（所有能力值 +x）取第一个键作为展示键，数值逐键在聚合层算。
                if (info.keys != null && info.keys.Length > 0 && info.formulas != null && info.formulas.Length > 0)
                {
                    line.key = info.keys[0];
                    line.value = GrowthFormula.Evaluate(info.formulas[0], row.slv);
                }
            }
            result.Add(line);
        }
        return result;
    }

    // 待确认结果落 journal：potkey:skillId:slv:grade 四条一组，逗号分隔。
    public static string Serialize(List<PotentialEntry> rows)
    {
        var parts = new List<string>();
        foreach (var row in rows) parts.Add(row.potKey + ":" + row.skillId + ":" + row.slv + ":" + row.grade);
        return string.Join(",", parts);
    }

    public static List<PotentialEntry> Parse(string packed)
    {
        var result = new List<PotentialEntry>();
        if (string.IsNullOrEmpty(packed)) return result;
        foreach (var part in packed.Split(','))
        {
            var cells = part.Split(':');
            if (cells.Length != 4) continue;
            if (!int.TryParse(cells[0], out int potKey) || !int.TryParse(cells[1], out int skillId)
                || !int.TryParse(cells[2], out int slv) || !int.TryParse(cells[3], out int grade)) continue;
            result.Add(new PotentialEntry { potKey = potKey, skillId = skillId, slv = slv, grade = grade });
        }
        return result;
    }
}
