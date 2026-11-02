// 制作（Maker）回归：逐条对照源端
//   client/processor/action/MakerProcessor.java（makerAction / getCreateStatus / removeOddMakerReagents /
//                                                getMakerReagentSlots / addBoostedMakerItem）
//   server/MakerItemFactory.java（getItemCreateEntry / trimCost / getMakerStimulantFee / getMakerReagentFee）
//   server/ItemInformationProvider.java（getMakerDisassembledFee / getMakerDisassembledItems / getCrystalForLevel）
// 配方数据来自 Etc.wz/ItemMake.img（tools/export_maker_data.py 导出），断言里用的数值都能在导出表里查到。
using System;
using System.Collections.Generic;
using System.IO;
using Server.Bot;

static class MakerChecks
{
    static string TablePath() => Path.Combine(AppContext.BaseDirectory, "Maker/MakerTable.txt");

    static MakerResponse Craft(DbStore db, int cid, int itemId, bool stimulant = false, int[] reagents = null)
        => NativeMakerService.Handle(db, new MakerRequest
        {
            characterId = cid, operation = MakerOperation.Craft, toCreate = itemId,
            stimulant = stimulant, reagentIds = reagents,
        });

    static int NewChar(DbStore db, string name, int level, int makerLevel, long meso)
    {
        var login = LoginService.Login(db, name.ToLowerInvariant() + "_acc", "pw123456");
        var made = CharFactory.CreateCharacter(db, login.accountId, 0, name, CharFactory.JobGroupAdventurer,
            20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
        if (made.code != 0) throw new InvalidOperationException("建号失败：" + name);
        var record = CharactersDao.FindById(db, made.characterId);
        record.level = level;
        record.meso = meso;
        CharactersDao.Save(db, record);
        if (makerLevel > 0) SkillsDao.Upsert(db, made.characterId, 1007, makerLevel, 3);
        db.Save();
        return made.characterId;
    }

    static void Give(DbStore db, int cid, int itemId, int count)
    {
        NativeInventoryService.AddItems(db, CharactersDao.FindById(db, cid), itemId, count, -1);
        db.Save();
    }

    static void SetMeso(DbStore db, int cid, long meso)
    {
        var record = CharactersDao.FindById(db, cid);
        record.meso = meso;
        CharactersDao.Save(db, record);
        db.Save();
    }

    static int Held(DbStore db, int cid, int itemId) => InventoryDao.CountOf(db, cid, itemId);

    // game_config 在源端是后台可改的配置表，自检里直接写一行改开关。
    static void SetConfig(DbStore db, string code, string value)
    {
        var table = db.Table("game_config");
        var row = table.Find(x => (string)x["config_code"] == code);
        if (row == null)
            table.Insert(new DbRow { ["config_type"] = "server", ["config_code"] = code, ["config_value"] = value });
        else row["config_value"] = value;
        db.Save();
    }

    static int EquipPosition(DbStore db, int cid, int itemId)
    {
        foreach (var row in InventoryDao.ItemsByCharacter(db, cid))
            if (DbStore.Int(row, "inventorytype") == 1 && DbStore.Int(row, "itemid") == itemId && DbStore.Int(row, "position") > 0)
                return DbStore.Int(row, "position");
        return 0;
    }

    public static void Run(Action<bool, string> Check)
    {
        Console.WriteLine("== 制作 Maker（ItemMake.img / makerAction）==");

        string table = TablePath();
        Check(File.Exists(table), "制作数据表随工程导出（Maker/MakerTable.txt）");
        if (!File.Exists(table)) return;
        MakerTable.TextProvider = () => File.Exists(table) ? File.ReadAllText(table) : null;
        MakerTable.Invalidate();
        MakerTable.Load(File.ReadAllText(table));

        TableChecks(Check);
        CostChecks(Check);
        FlowChecks(Check);
        ReagentChecks(Check);
        StimulantChecks(Check);
        DisassembleChecks(Check);
        CrystalChecks(Check);
    }

    // ————————————————— 数据表 —————————————————
    static void TableChecks(Action<bool, string> Check)
    {
        Check(MakerTable.All().Count == 834, "配方表 834 条（ItemMake.img 全量）");

        var gem = MakerTable.Get(4250000);
        Check(gem != null && gem.meso == 100000 && gem.reqLevel == 45 && gem.reqSkillLevel == 1
              && gem.reqItems.Count == 1 && gem.reqItems[0].itemId == 4021007 && gem.reqItems[0].count == 1,
            "4250000 配方：45 级 / 制作 1 级 / 100000 金币 / 蓝色蜗牛壳 x1");

        var cap = MakerTable.Get(1002028);
        Check(cap != null && cap.catalyst == 4130018 && cap.reqItems.Count == 2 && cap.tuc == 1,
            "1002028 配方带催化剂 4130018（刺激剂来源）");

        var pad = MakerTable.Reagent(4250000);
        var rand = MakerTable.Reagent(4251402);
        Check(pad != null && pad.stat == "incPAD" && pad.value == 1
              && rand != null && rand.stat == "randStat" && rand.value == 5,
            "强化宝石词条表（4250000 = incPAD 1 / 4251402 = randStat 5）");

        Check(MakerTable.CrystalFromLeftover(4000004) == 4260000
              && MakerTable.CrystalFromLeftover(4000001) == 4260000
              && MakerTable.CrystalFromLeftover(4000999) == -1,
            "原石→怪物结晶档位（4000004 → 4260000，查不到回 -1）");

        Check(MakerTable.CategoryOf(100) == 1 && MakerTable.CategoryOf(412) == 8
              && MakerTable.CategoryOf(2000) == 0 && MakerTable.CategoryOf(0) == 0,
            "职业位掩码（战士 1 / 飞侠 8 / 英雄 0）");
    }

    // ————————————————— 费用（MakerItemFactory） —————————————————
    static void CostChecks(Action<bool, string> Check)
    {
        string path = Path.Combine(Path.GetTempPath(), "mapledash_maker_cost_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            int cid = NewChar(db, "MakerCost", 120, 3, 100000000);
            int baseCost = NativeMakerService.QuoteCost(db, 4250000, false, null);
            Check(baseCost == 100000, "基础费用 = trimCost(meso)（实际 " + baseCost + "）");
            // 1002028：帽子（CAP）45 级。heuristics 打开 → 刺激剂 5142.9 / 1 级宝石 20757.3
            int stimCost = NativeMakerService.QuoteCost(db, 1002028, true, null);
            int gemCost = NativeMakerService.QuoteCost(db, 1002028, false, new[] { 4250000 });
            int bothCost = NativeMakerService.QuoteCost(db, 1002028, true, new[] { 4250000 });
            Check(stimCost == 56000, "刺激剂附加费按装备的 wearing 等级拟合（1002028 要 50 级，50000+6076.7 → 56000，实际 " + stimCost + "）");
            Check(gemCost == 74000, "宝石附加费（50000+24013.8 → 74000，实际 " + gemCost + "）");
            Check(bothCost == 80000, "刺激剂 + 宝石叠加（80090.5 → 80000，实际 " + bothCost + "）");

            SetConfig(db, "use_maker_fee_heuristics", "false");
            Check(NativeMakerService.QuoteCost(db, 1002028, true, null) == 64000
                  && NativeMakerService.QuoteCost(db, 1002028, false, new[] { 4250000 }) == 58000,
                "关闭 heuristics 用固定值（刺激剂 14000 / 宝石 8000×等级）");
            SetConfig(db, "use_maker_fee_heuristics", "true");

            Check(NativeMakerService.QuoteCost(db, 9999999, false, null) == -1,
                "不在配方表里的道具报价 -1");
        }
        finally { File.Delete(path); }
    }

    // ————————————————— 制作主流程与六个状态码 —————————————————
    static void FlowChecks(Action<bool, string> Check)
    {
        string path = Path.Combine(Path.GetTempPath(), "mapledash_maker_flow_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            int cid = NewChar(db, "MakerFlow", 50, 1, 100000);
            Give(db, cid, 4021007, 1);
            var res = Craft(db, cid, 4250000);
            Check(res.ok && res.status == 0 && Held(db, cid, 4250000) == 1, "制作 4250000 成功并产出一件");
            Check(Held(db, cid, 4021007) == 0, "材料 4021007 扣掉");
            Check(res.meso == 0 && CharactersDao.FindById(db, cid).meso == 0, "金币按报价扣除（100000 → 0）");

            // 缺材料 → 1
            var noItem = Craft(db, cid, 4250000);
            Check(!noItem.ok && noItem.status == NativeMakerService.MissingItems, "缺材料回状态 1");

            // 缺金币 → 2
            Give(db, cid, 4021007, 1);
            SetMeso(db, cid, 99999);
            var noMeso = Craft(db, cid, 4250000);
            Check(!noMeso.ok && noMeso.status == NativeMakerService.MissingMeso, "金币差 1 块也回状态 2");

            // 等级不足 → 3
            SetMeso(db, cid, 1000000);
            var low = CharactersDao.FindById(db, cid);
            low.level = 44;
            CharactersDao.Save(db, low);
            var tooLow = Craft(db, cid, 4250000);
            Check(!tooLow.ok && tooLow.status == NativeMakerService.LowLevel, "等级 44 回状态 3");

            // 制作技能不足 → 4
            low.level = 50;
            CharactersDao.Save(db, low);
            SkillsDao.Upsert(db, cid, 1007, 0, 3);
            var noSkill = Craft(db, cid, 4250000);
            Check(!noSkill.ok && noSkill.status == NativeMakerService.LowMakerLevel, "制作技能 0 级回状态 4");
            SkillsDao.Upsert(db, cid, 1007, 1, 3);

            // 背包满 → 5
            int full = NewChar(db, "MakerFull", 50, 1, 1000000);
            for (int i = 0; i < 24; i++) Give(db, full, 1002028, 1);
            Give(db, full, 4007001, 5);
            Give(db, full, 4260000, 5);
            var fullRes = Craft(db, full, 1002028);
            Check(!fullRes.ok && fullRes.status == NativeMakerService.InventoryFull, "装备栏满回状态 5（材料不扣）");
            Check(Held(db, full, 4007001) == 5 && Held(db, full, 4260000) == 5, "状态 5 时材料原样保留");

            // 不在配方表 → -1
            var invalid = Craft(db, cid, 9999999);
            Check(!invalid.ok && invalid.status == NativeMakerService.InvalidRecipe, "未登记道具回状态 -1");

            // 技能 id 用 (job/1000)*10000000+1007
            Check(NativeMakerService.MakerSkillLevel(db, cid, 0) == 1
                  && NativeMakerService.MakerSkillLevel(db, cid, 1000) == 0,
                "制作技能等级取 1007 / 10001007");
        }
        finally { File.Delete(path); }
    }

    // ————————————————— 强化宝石（removeOddMakerReagents） —————————————————
    static void ReagentChecks(Action<bool, string> Check)
    {
        string path = Path.Combine(Path.GetTempPath(), "mapledash_maker_reagent_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            int cid = NewChar(db, "MakerGem", 120, 3, 100000000);

            // 非武器装备只允许 type >= 42502 的宝石
            Give(db, cid, 4007001, 5);
            Give(db, cid, 4260000, 5);
            Give(db, cid, 4250000, 1);
            var denied = Craft(db, cid, 1002028, false, new[] { 4250000 });
            Check(!denied.ok && denied.error.Contains("武器"), "非武器禁用攻击力强化宝石");
            Check(Held(db, cid, 4250000) == 1 && Held(db, cid, 4007001) == 5, "被拒时宝石与材料都不动");

            // 武器装备：同类只留效果最高的那颗（1302086 是 3 槽武器）
            int weapon = NewChar(db, "MakerWeapon", 120, 3, 100000000);
            foreach (var req in MakerTable.Get(1302086).reqItems) Give(db, weapon, req.itemId, req.count);
            Give(db, weapon, 4250000, 1);   // incPAD 1
            Give(db, weapon, 4250002, 1);   // incPAD 3
            var stats = BotItemCatalog.Shared.EquipStats(1302086);
            int baseWatk = stats != null && stats.TryGetValue("PAD", out int pad0) ? pad0 : 0;
            var forged = Craft(db, weapon, 1302086, false, new[] { 4250000, 4250002 });
            var made = CharService.Load(db, weapon).inventory.Find(x => x.itemId == 1302086);
            Check(forged.ok && made != null && made.watk == baseWatk + 3,
                "同类宝石只吃效果最高的（incPAD 1+3 → 只加 3）");
            Check(Held(db, weapon, 4250002) == 0 && Held(db, weapon, 4250000) == 1,
                "低效那颗原样留在背包");

            // 宝石槽位数：需求等级 <78 → 1、<108 → 2、>=108 → 3
            Check(NativeMakerService.ReagentSlotsFor(1002028) == 1
                  && NativeMakerService.ReagentSlotsFor(1302086) == 3,
                "宝石槽位：45 级 1 槽 / 115 级 3 槽");

            // 超出的宝石不消耗
            int capped = NewChar(db, "MakerCap", 120, 3, 100000000);
            foreach (var req in MakerTable.Get(1302086).reqItems) Give(db, capped, req.itemId, req.count);
            foreach (int gem in new[] { 4250000, 4250100, 4250200, 4250300, 4250400 }) Give(db, capped, gem, 1);
            Craft(db, capped, 1302086, false, new[] { 4250000, 4250100, 4250200, 4250300, 4250400 });
            Check(Held(db, capped, 4250200) == 0 && Held(db, capped, 4250300) == 1 && Held(db, capped, 4250400) == 1,
                "只消耗到槽位数（3 槽 → 后两颗不扣）");

            // 宝石不够时按实际持有量下调
            int short1 = NewChar(db, "MakerShort", 120, 3, 100000000);
            Give(db, short1, 4011000, 1);
            Give(db, short1, 4260000, 20);
            var noGem = Craft(db, short1, 1302010, false, new[] { 4250000 });
            Check(noGem.ok, "没持有宝石时按「不使用」处理，仍能制作");
        }
        finally { File.Delete(path); }
    }

    // ————————————————— 刺激剂（90% 判定） —————————————————
    static void StimulantChecks(Action<bool, string> Check)
    {
        string path = Path.Combine(Path.GetTempPath(), "mapledash_maker_stim_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        var saved = NativeMakerService.Rng;
        try
        {
            int failSeed = -1, passSeed = -1;
            for (int seed = 1; seed < 400 && (failSeed < 0 || passSeed < 0); seed++)
            {
                double v = new Random(seed).NextDouble();
                if (v < 0.1 && failSeed < 0) failSeed = seed;      // 90% 判定的失败侧
                if (v >= 0.1 && passSeed < 0) passSeed = seed;
            }

            int cid = NewChar(db, "MakerStim", 50, 1, 1000000);
            Give(db, cid, 4007001, 5);
            Give(db, cid, 4260000, 5);
            Give(db, cid, 4130018, 1);
            NativeMakerService.Rng = new Random(failSeed);
            var failed = Craft(db, cid, 1002028, true, null);
            Check(failed.ok && !failed.craftSucceeded && Held(db, cid, 1002028) == 0,
                "刺激剂判定失败：不给成品");
            Check(Held(db, cid, 4007001) == 0 && Held(db, cid, 4260000) == 0 && Held(db, cid, 4130018) == 0,
                "刺激剂判定失败：刺激剂与材料照扣");
            Check(failed.meso == 1000000 - 56000, "刺激剂失败金币照扣（含 6076.7 附加费 → 56000）");

            int ok = NewChar(db, "MakerStimOk", 50, 1, 1000000);
            Give(db, ok, 4007001, 5);
            Give(db, ok, 4260000, 5);
            Give(db, ok, 4130018, 1);
            NativeMakerService.Rng = new Random(passSeed);
            var passed = Craft(db, ok, 1002028, true, null);
            var equip = CharService.Load(db, ok).inventory.Find(x => x.itemId == 1002028);
            Check(passed.ok && passed.craftSucceeded && equip != null, "刺激剂判定成功：正常产出");
            Check(Held(db, ok, 4130018) == 0, "刺激剂成功消耗 1 个");
            var plain = BotItemCatalog.Shared.EquipStats(1002028);
            int baseStr = plain != null && plain.TryGetValue("STR", out int s0) ? s0 : 0;
            Check(equip != null && equip.str >= baseStr, "刺激剂随机化只会加属性");
        }
        finally
        {
            NativeMakerService.Rng = saved;
            File.Delete(path);
        }
    }

    // ————————————————— 装备分解（type 4） —————————————————
    static void DisassembleChecks(Action<bool, string> Check)
    {
        string path = Path.Combine(Path.GetTempPath(), "mapledash_maker_dis_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            int cid = NewChar(db, "MakerDis", 120, 3, 1000000);
            Give(db, cid, 1002028, 1);
            int slot = EquipPosition(db, cid, 1002028);
            Check(slot > 0, "装备已进装备栏");
            int before = Held(db, cid, 4260000);
            var res = NativeMakerService.Handle(db, new MakerRequest
            {
                characterId = cid, operation = MakerOperation.Disassemble, toCreate = 1002028, position = slot,
            });
            Check(res.ok && Held(db, cid, 4260000) == before + 2, "分解 1002028 退回配方里一半的结晶（5 → 2）");
            Check(EquipPosition(db, cid, 1002028) == 0, "分解后装备消失");
            Check(res.meso == 1000000, "分解不收费（源端没走 trimCost，getCost 恒为 0）");

            var wrong = NativeMakerService.Handle(db, new MakerRequest
            {
                characterId = cid, operation = MakerOperation.Disassemble, toCreate = 1002028, position = 1,
            });
            Check(!wrong.ok, "格子对不上时拒绝分解");

            // 不在配方表里的装备没有分解费，也不能分解
            Check(MakerTable.Get(1302000) == null && !NativeMakerService.CanDisassemble(1302000),
                "不在配方表里的装备不可分解");
            Give(db, cid, 1302000, 1);
            int plainPos = EquipPosition(db, cid, 1302000);
            var rejected = NativeMakerService.Handle(db, new MakerRequest
            {
                characterId = cid, operation = MakerOperation.Disassemble, toCreate = 1302000, position = plainPos,
            });
            Check(!rejected.ok, "不可分解的装备被拒");
            Check(EquipPosition(db, cid, 1302000) == plainPos, "被拒时装备还在");
            Check(NativeMakerService.CanDisassemble(1002028), "配方点名结晶的装备可以分解");
        }
        finally { File.Delete(path); }
    }

    // ————————————————— 怪物结晶合成（type 3） —————————————————
    static void CrystalChecks(Action<bool, string> Check)
    {
        string path = Path.Combine(Path.GetTempPath(), "mapledash_maker_crystal_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            int cid = NewChar(db, "MakerCrystal", 50, 1, 1000000);
            Give(db, cid, 4000004, 99);
            var short1 = NativeMakerService.Handle(db, new MakerRequest
            {
                characterId = cid, operation = MakerOperation.Crystal, toCreate = 4000004,
            });
            Check(!short1.ok && short1.status == NativeMakerService.MissingItems, "99 份原石不够（要 100 份）");

            Give(db, cid, 4000004, 1);
            var res = NativeMakerService.Handle(db, new MakerRequest
            {
                characterId = cid, operation = MakerOperation.Crystal, toCreate = 4000004,
            });
            Check(res.ok && Held(db, cid, 4260000) == 1 && Held(db, cid, 4000004) == 0,
                "100 份原石换 1 颗怪物结晶，原石清空");
            Check(res.meso == 1000000, "结晶合成不收费");

            var unknown = NativeMakerService.Handle(db, new MakerRequest
            {
                characterId = cid, operation = MakerOperation.Crystal, toCreate = 4000999,
            });
            Check(!unknown.ok, "查不到掉落的原石拒绝转换");
        }
        finally { File.Delete(path); }
    }
}
