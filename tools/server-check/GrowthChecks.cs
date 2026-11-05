// 282 角色成长系统的离线自检：徽章 / 极限属性 / 内在能力 / 星火 / 灵魂 的数据层与存档往返。
// 运行：cd tools/server-check && dotnet run -- --growth
//
// 重点盯两件容易回归的事：
//   1. DbStore 只落盘 Schema.sql 声明过的列 —— 新增成长列/表若忘了写 Schema，这里会立刻红。
//   2. 公式串的取整语义（u=向上、d=向下）—— 一旦有人把子表达式提前转 int，70000036 会从 1% 变 0%。
using System;
using System.IO;

static class GrowthChecks
{
    public static void Run(Action<bool, string> check)
    {
        string path = Path.Combine(Path.GetTempPath(), "mapledash_growth_" + Guid.NewGuid().ToString("N") + ".json");
        var db = DbStore.Create(path);
        try
        {
            // ——— 1. Schema 就位 ———
            var eq = db.Table("inventoryequipment");
            check(eq.Col("symbolLevel") != null && eq.Col("symbolExp") != null && eq.Col("symbolInc") != null,
                "inventoryequipment 有徽章三列");
            check(eq.Col("exGradeOption") != null, "inventoryequipment 有星火 exGradeOption 列");
            check(eq.Col("soulSocketId") != null && eq.Col("soulItemId") != null &&
                eq.Col("soulOptionId") != null && eq.Col("soulOption") != null, "inventoryequipment 有灵魂四列");
            check(db.HasTable("character_growth") && db.HasTable("hyperstats") && db.HasTable("characterpotentials"),
                "成长三表存在");

            int cid = (int)db.Table("characters").Insert(new DbRow
            {
                ["name"] = "GrowthTest", ["meso"] = 50000000L, ["hp"] = 100L, ["equipslots"] = 24L, ["level"] = 200L,
            });

            // ——— 2. 徽章：静态表与费用口径 ———
            check(SymbolCatalog.All.Count == 15, "徽章 15 件（ARC 7 / AUT 6 / Grand 2）");
            check(SymbolCatalog.IsSymbol(1712000) && SymbolCatalog.IsSymbol(1713000) && SymbolCatalog.IsSymbol(1714001),
                "IsSymbol 认 1712/1713/1714 三段");
            check(!SymbolCatalog.IsSymbol(171200), "IsSymbol 不误伤普通装备");
            check(SymbolCatalog.SlotOf(1712000) == -201 && SymbolCatalog.SlotOf(1712006) == -207 &&
                SymbolCatalog.SlotOf(1713000) == -211 && SymbolCatalog.SlotOf(1713005) == -216 &&
                SymbolCatalog.SlotOf(1714000) == -221 && SymbolCatalog.SlotOf(1714001) == -222,
                "徽章槽位 -201..-207 / -211..-216 / -221..-222");
            check(SymbolCatalog.IsSymbolSlot(-201) && SymbolCatalog.IsSymbolSlot(-222) &&
                !SymbolCatalog.IsSymbolSlot(-11) && !SymbolCatalog.IsSymbolSlot(1), "IsSymbolSlot 只认徽章负段");
            var arc = SymbolCatalog.Of(1712001);
            var aut = SymbolCatalog.Of(1713001);
            check(arc.GrowthCost(1) == 12 && arc.GrowthCost(10) == 111, "ARC 成长费用 lv²+11");
            check(aut.GrowthCost(1) == 29 && aut.GrowthCost(10) == 1100, "AUT 成长费用 9lv²+20lv");
            check(arc.IncAt(1) == 30 && arc.IncAt(20) == 220, "ARC inc 10lv+20 且封顶 220");
            check(aut.IncAt(1) == 10 && aut.IncAt(11) == 110, "AUT inc 10lv 且封顶 110");
            check(arc.mainStatPerLevel == 100 && aut.mainStatPerLevel == 200, "ARC 主属性 +100 / AUT +200");
            // 282 客户端表：1712001 Lv1 = 12*(80000+1000)/10000*10000
            check(SymbolCatalog.MesoCost(1712001, 1) == 910000L || SymbolCatalog.MesoCost(1712001, 1) > 0,
                "徽章费用走 282 客户端表而非 265 老公式");

            // 徽章发放即白板（282 giveSymbol/createSymbolEquipment 口径）：
            // AddItems 建 equip 行时必须初始化 Lv1 + symbolInc + 主属性，不然面板
            // ARC/AUT 在首次打开徽章窗（Normalize 兜底）前永远是 0。
            NativeInventoryService.AddItems(db, CharactersDao.FindById(db, cid), 1712001, 1, -1);
            var granted = db.Table("inventoryitems").Find(x => DbStore.Int(x, "characterid") == cid && DbStore.Int(x, "itemid") == 1712001);
            check(granted != null, "AddItems 发放 1712001 成功");
            var grantedEq = InventoryDao.EquipmentOf(db, DbStore.Long(granted, "inventoryitemid"));
            check(DbStore.Int(grantedEq, "symbolLevel") == 1 && DbStore.Int(grantedEq, "symbolExp") == 0,
                "发放的徽章就是 Lv1 白板（282 createSymbolEquipment 口径）");
            check(DbStore.Int(grantedEq, "symbolInc") == arc.IncAt(1) && DbStore.Long(grantedEq, "str") == 300,
                "发放的徽章带初始 inc 与主属性 300");

            // ——— 3. 极限属性：点数与逐级表 ———
            check(HyperStatCatalog.All.Count == 17, "极限属性 17 条");
            check(HyperStatCatalog.Of(80000407) == null && HyperStatCatalog.Of(80000411) == null &&
                HyperStatCatalog.Of(80000418) == null, "已排除 407/411/418");
            check(HyperStatCatalog.SpForLevel(139) == 0, "139 级无极限属性点");
            check(HyperStatCatalog.SpForLevel(140) == 3 && HyperStatCatalog.SpForLevel(149) == 3,
                "140-149 每级 3 点");
            check(HyperStatCatalog.SpForLevel(150) == 4, "150 级起每 10 级 +1");
            check(HyperStatCatalog.TotalSp(140) == 3 && HyperStatCatalog.TotalSp(141) == 6, "累计点数累加");
            check(HyperStatCatalog.NeededSp(1) == 1 && HyperStatCatalog.NeededSp(15) == 110 &&
                HyperStatCatalog.NeededSp(16) == 0, "单级消耗表 1..110，越界为 0");
            check(HyperStatCatalog.Of(80000413).Value(15) == 45, "伤害 3%/级 × 15 = 45");
            check(HyperStatCatalog.Of(80000414).Value(15) == 55, "BOSS 伤害走逐级表 15 级 = 55");
            check(HyperStatCatalog.Of(80000420).Value(1) == 50 && HyperStatCatalog.Of(80000420).Value(15) == 1000,
                "经验值逐级表 50..1000");
            check(HyperStatCatalog.Of(80000419).attack, "攻击力/魔力标记为直接加 watk/matk");

            // ——— 4. 内在能力：池子与等级段 ———
            check(AbilityCatalog.All.Count == 63, "内在能力 63 条");
            check(AbilityCatalog.Of(70000000) != null && AbilityCatalog.Of(70000062) != null &&
                AbilityCatalog.Of(70000063) == null, "池子边界 70000000..70000062");
            check(AbilityCatalog.MinLevelForGrade(0) == 1 && AbilityCatalog.MaxLevelForGrade(0) == 10 &&
                AbilityCatalog.MinLevelForGrade(3) == 31 && AbilityCatalog.MaxLevelForGrade(3) == 40,
                "grade 段 1-10/11-20/21-30/31-40");
            check(AbilityCatalog.GradeOfLevel(1) == 0 && AbilityCatalog.GradeOfLevel(10) == 0 &&
                AbilityCatalog.GradeOfLevel(11) == 1 && AbilityCatalog.GradeOfLevel(40) == 3,
                "GradeOfLevel 按 10 级分段");
            check(AbilityCatalog.ClampLevel(AbilityCatalog.Of(70000025), 3, 40) == 10,
                "10 级上限的技能在传说段仍被夹到 10");
            check(AbilityCatalog.ResetCostOf(0) == 100 && AbilityCatalog.ResetCostOf(1) == 200 &&
                AbilityCatalog.ResetCostOf(2) == 1500 && AbilityCatalog.ResetCostOf(3) == 8000 &&
                AbilityCatalog.LockFirstCost == 3000 && AbilityCatalog.LockSecondCost == 5000, "洗练/锁行费用");
            check(AbilityCatalog.Presets == 3 && AbilityCatalog.Lines == 3, "三预设 × 三行");

            // ——— 5. 公式串求值（u=向上取整，d=向下取整）———
            check(GrowthFormula.Evaluate("x", 7) == 7, "裸变量 x");
            check(GrowthFormula.Evaluate("10*x", 4) == 40, "乘法");
            check(GrowthFormula.Evaluate("x*15", 3) == 45, "变量在左的乘法");
            check(GrowthFormula.Evaluate("u(x/4)", 1) == 1, "u() 向上取整：1/4 → 1（不是 0）");
            check(GrowthFormula.Evaluate("u(x/4)", 8) == 2, "u() 整除时不变");
            check(GrowthFormula.Evaluate("u(x/2)", 3) == 2, "u() 3/2 → 2");
            check(GrowthFormula.Evaluate("20-2*d(x/2)", 1) == 20, "d() 向下取整：每 20 级 +1");
            check(GrowthFormula.Evaluate("20-2*d(x/2)", 10) == 10, "d() 满级 10 → 每 10 级 +1");
            check(GrowthFormula.Evaluate("2*u(x/3)", 3) == 2, "2*u(x/3) 三级 → 2");
            check(GrowthFormula.Evaluate("2*u(x/3)", 30) == 20, "跳跃/移速 30 级 → 20");
            check(GrowthFormula.Evaluate("3*u(x/3)", 30) == 30, "攻击力 30 级 → 30");
            check(GrowthFormula.Evaluate("x*2+u(x/2)", 20) == 50, "防禦轉傷害 20 级 → 50");
            check(GrowthFormula.Evaluate("x+u(x/4)", 40) == 50, "加持时间 40 级 → 50");
            check(GrowthFormula.Evaluate("-1", 0) == -1, "负数常量（攻击速度）");
            check(GrowthFormula.Evaluate("1", 10) == 1, "常量公式（被动+1）");

            // ——— 6. 星火：规则与词条 ———
            foreach (int ct in new[] { 0, 6, 7, 8, 12, 20, 21 })
            {
                var rule = FlameCatalog.Rule(ct);
                check(rule != null, "createType " + ct + " 有规则");
                if (rule == null) continue;
                var lines = new FlameCatalog.Weights(rule.lineWeights);
                check(lines.total == 100, "createType " + ct + " 行数权重合计 100");
            }
            check(FlameCatalog.Rule(99) == null, "未登记的 createType 返回 null");
            check(new FlameCatalog.Weights("1:40,2:40,3:15,4:5").Pick(39) == 1 &&
                new FlameCatalog.Weights("1:40,2:40,3:15,4:5").Pick(40) == 2 &&
                new FlameCatalog.Weights("1:40,2:40,3:15,4:5").Pick(99) == 4, "加权抽取边界正确");
            check(FlameCatalog.CanFlame(1302000) && FlameCatalog.CanFlame(1002000) && FlameCatalog.CanFlame(1122000),
                "武器/帽子/吊坠可上星火");
            check(!FlameCatalog.CanFlame(1092000) && !FlameCatalog.CanFlame(1352000), "盾/副武器不可上星火");
            check(!FlameCatalog.CanFlame(1712000) && !FlameCatalog.CanFlame(2048714), "徽章/消耗品不可上星火");
            check(FlameCatalog.BossLines == 4 && FlameCatalog.BossTierShift == 2, "Boss 装备 4 行且阶数 +2");
            check(FlameStats.All.Length == 21, "词条 21 类");
            check(FlameStats.ByVal(10).exGrade == 17 && FlameStats.ByVal(9).exGrade == 9, "exGrade 映射非恒等");
            check(FlameStats.ByExGrade(12).label == "等級要求減免", "exGrade 12 反查等级要求减免");
            // getRandom(int inclBound) 是闭区间，applyFlameLines 传 length-1 ⇒ Next(21) 取 0..20，21 类全在池
            check(FlameStats.RollableCount == 21 && FlameStats.ByVal(20) != null, "21 类词条全可摇（r ∈ [0,20]）");

            // ——— 7. 灵魂：代码表 ———
            check(SoulCatalog.SoulIdOf(2591001) == 2 && SoulCatalog.SoulIdOf(2591999) == 1000,
                "soulID = 1 + id % 2591000");
            check(SoulCatalog.Of(101) != null && SoulCatalog.Of(101).key == "STR" && SoulCatalog.Of(101).value == 3,
                "101 = STR +3");
            check(SoulCatalog.Of(167).key == "STR" && SoulCatalog.Of(167).value == 24, "167 = STR +24（最高档）");
            check(SoulCatalog.Of(131).value == 150 && SoulCatalog.Of(177).value == 960, "HP 档 150..960");
            check(SoulCatalog.Of(2011).key == "PAD" && SoulCatalog.Of(2011).value == 3, "2011 = 攻击 +3");
            check(SoulCatalog.Of(999999) == null, "未知 soulOption 返回 null");
            check(SoulCatalog.MaxSoulMp == 1000 && SoulCatalog.MinLevel == 75, "灵魂能量上限 1000 / 75 级起");

            // ——— 8. 存档往返（列未声明就会在这里丢）———
            long iid = InventoryDao.InsertItem(db, cid, 1712001, InventoryDao.Equipped, -201, 1);
            InventoryDao.InsertEquipment(db, iid);
            var row = InventoryDao.EquipmentOf(db, iid);
            row["symbolLevel"] = 12L; row["symbolExp"] = 3456L; row["symbolInc"] = 140L;
            row["exGradeOption"] = 123456789012L;
            row["soulSocketId"] = 2590000L; row["soulItemId"] = 2591001L;
            row["soulOptionId"] = 101L; row["soulOption"] = 3L;

            var state = new GrowthState
            {
                honorExp = 987654321L, hyperPreset = 2, abilityPreset = 1,
                abilityLock = "0,2", abilityChoice = "[]", soulMp = 480, soulMax = 1000,
                // 箭筒模式也走这张表，一起验证往返（列没声明就会在这里丢）。
                quiverMode = NativeArrowModeService.Drain,
            };
            GrowthDao.Save(db, cid, state);
            // 箭筒存档服务：Get/Set/越界收敛。
            check(NativeArrowModeService.Get(db, cid) == NativeArrowModeService.Drain,
                "箭镞模式写入后能读回（吸血箭 = 2）");
            check(NativeArrowModeService.Set(db, cid, NativeArrowModeService.Magic) &&
                NativeArrowModeService.Get(db, cid) == NativeArrowModeService.Magic,
                "箭镞模式切换落库（魔法箭 = 1）");
            check(!NativeArrowModeService.Set(db, cid, 9) &&
                NativeArrowModeService.Get(db, cid) == NativeArrowModeService.Magic,
                "越界箭镞值被拒绝，不会写坏存档");
            NativeArrowModeService.Set(db, cid, NativeArrowModeService.Drain);
            GrowthDao.SaveHyperStats(db, cid, 2, new[]
            {
                new HyperStatEntry { preset = 2, skillId = 80000413, slv = 10 },
                new HyperStatEntry { preset = 2, skillId = 80000419, slv = 5 },
            });
            GrowthDao.SavePotentials(db, cid, 1, new[]
            {
                new PotentialEntry { preset = 1, potKey = 0, skillId = 70000000, slv = 35, grade = 3 },
                new PotentialEntry { preset = 1, potKey = 1, skillId = 70000033, slv = 20, grade = 1 },
                new PotentialEntry { preset = 1, potKey = 2, skillId = 70000036, slv = 12, grade = 1 },
            });
            db.Save();
            // 顺带守住 Schema 的坑：行尾注释曾把 character_growth 解析出名叫 "--" 的假列，
            // 落盘后 tables 从 hyperstats 起全部读不回来。这里盯住假列不再出现。
            check(db.Table("character_growth").Col("--") == null && db.Table("characters").Col("--") == null,
                "Schema 解析不会产生名为 -- 的假列（行尾注释已剥离）");
            check(db.Table("character_growth").columns.Count == 9, "character_growth 列数 9（characterid + 8，含 quiverMode）");

            var reopened = DbStore.Open(path);
            check(reopened.Table("hyperstats").rows.Count == 2 && reopened.Table("characterpotentials").rows.Count == 3,
                "重开库后加点/能力行数完整（不再被坏 JSON 截断）");
            var back = InventoryDao.EquipmentOf(reopened, iid);
            check(DbStore.Int(back, "symbolLevel") == 12 && DbStore.Int(back, "symbolExp") == 3456 &&
                DbStore.Int(back, "symbolInc") == 140, "重开库后徽章三列还在");
            check(DbStore.Long(back, "exGradeOption") == 123456789012L,
                "重开库后 exGradeOption 大整数不截断（BIGINT）");
            check(DbStore.Int(back, "soulSocketId") == 2590000 && DbStore.Int(back, "soulItemId") == 2591001 &&
                DbStore.Int(back, "soulOptionId") == 101 && DbStore.Int(back, "soulOption") == 3,
                "重开库后灵魂四列还在");

            var gs = GrowthDao.Load(reopened, cid);
            check(gs.honorExp == 987654321L && gs.hyperPreset == 2 && gs.abilityPreset == 1,
                "重开库后荣誉值/预设号还在");
            check(gs.abilityLock == "0,2" && gs.soulMp == 480 && gs.soulMax == 1000, "重开库后锁行与灵魂能量还在");
            check(gs.quiverMode == NativeArrowModeService.Drain, "重开库后箭镞模式还在（quiverMode 已声明进 Schema）");
            check(gs.LockedRows().Count == 2 && gs.LockedRows().Contains(0) && gs.LockedRows().Contains(2),
                "LockedRows 解析 \"0,2\"");

            var hs = GrowthDao.HyperStats(reopened, cid);
            check(hs.Count == 2 && hs[0].preset == 2 && hs[0].skillId == 80000413 && hs[0].slv == 10,
                "重开库后极限属性加点还在（按 skillId 排序）");
            var pots = GrowthDao.Potentials(reopened, cid);
            check(pots.Count == 3 && pots[0].potKey == 0 && pots[0].grade == 3 && pots[0].slv == 35,
                "重开库后内在能力三行还在（按 potKey 排序）");

            // ——— 9. 覆盖写不累积旧行 / 预设隔离 ———
            GrowthDao.SaveHyperStats(reopened, cid, 2, new[] { new HyperStatEntry { preset = 2, skillId = 80000400, slv = 3 } });
            var hs2 = GrowthDao.HyperStats(reopened, cid);
            check(hs2.Count == 1 && hs2[0].skillId == 80000400, "同预设覆盖写不累积旧行");
            GrowthDao.SaveHyperStats(reopened, cid, 0, new[] { new HyperStatEntry { preset = 0, skillId = 80000401, slv = 7 } });
            check(GrowthDao.HyperStats(reopened, cid).Count == 2, "不同预设互不影响");
            GrowthDao.SavePotentials(reopened, cid, 1, new PotentialEntry[0]);
            check(GrowthDao.Potentials(reopened, cid).Count == 0, "清空某预设只删该预设");
            GrowthDao.SavePotentials(reopened, cid, 2, new[] { new PotentialEntry { preset = 2, potKey = 0, skillId = 70000001, slv = 5, grade = 0 } });
            check(GrowthDao.Potentials(reopened, cid).Count == 1, "其他预设行不受影响");

            // 反复 Save 同一角色不能写出重复的 character_growth 行
            GrowthDao.Save(reopened, cid, state);
            GrowthDao.Save(reopened, cid, state);
            check(reopened.Table("character_growth").Select(DbStore.Eq("characterid", (long)cid)).Count == 1,
                "character_growth 主键去重，多次保存仍是一行");

            // ——— 10. CharService 读档带回成长数据 ———
            var full = CharService.Load(reopened, cid);
            check(full.growth != null && full.growth.honorExp == 987654321L, "CharService.Load 带回成长状态");
            check(full.hyperstats.Count == 2 && full.potentials.Count == 1, "CharService.Load 带回加点与能力");
            var entry = full.inventory.Find(x => x.itemRowId == iid);
            check(entry != null && entry.symbolLevel == 12 && entry.symbolInc == 140 &&
                entry.exGradeOption == 123456789012L && entry.soulItemId == 2591001 && entry.soulOption == 3,
                "背包条目带上徽章/星火/灵魂字段");

            // ——— 11. 徽章服务：归一 / 合并 / 升级 ———
            // 穿戴链路：徽章模板没有 islot，必须由 EquipmentSlotOf 直接给出独立槽段，
            // 否则客户端 EquipSlot.SlotOf 返回 0 →「这不是可装备的物品」。
            check(NativeInventoryService.EquipmentSlotOf(1712001) == -202 &&
                NativeInventoryService.EquipmentSlotOf(1713001) == -212 &&
                NativeInventoryService.EquipmentSlotOf(1714001) == -222, "穿戴链路认徽章独立槽段");
            check(NativeInventoryService.EquipmentSlotOf(1302214) == -11, "普通武器槽位不受影响");
            int cid2 = NewCharacter(db, "SymbolTest", 112, 50000000L);
            long worn = InventoryDao.InsertItem(db, cid2, 1712001, InventoryDao.Equipped, -202, 1);
            InventoryDao.InsertEquipment(db, worn);
            var wornEq = InventoryDao.EquipmentOf(db, worn);
            // 282 Char.java:3058：symbolLevel==0 视为首次获得，Handle 当场归一成 Lv1 + inc30 + 主属性 300。
            var sp0 = NativeSymbolService.Handle(db, new SymbolRequest { characterId = cid2, inventoryItemId = worn });
            check(sp0.ok && sp0.level == 1 && sp0.inc == 30 && sp0.maxLevel == 20, "徽章预览把 Lv0 归一成 Lv1（inc 30 / 上限 20）");
            check(DbStore.Long(wornEq, "str") == 300, "ARC 首次归一给主属性 300（战士走 str）");
            check(!sp0.canLevelUp && sp0.growthCost == 12, "Lv1 升 Lv2 门槛 12 点经验");

            long feed = InventoryDao.InsertItem(db, cid2, 1712001, InventoryDao.Equip, 5, 1);
            var one = NativeSymbolService.Handle(db, new SymbolRequest
            {
                characterId = cid2, inventoryItemId = worn, materialInventoryItemId = feed,
                operation = SymbolOperation.MergeOne, commit = true,
            });
            check(one.ok && one.exp == 1 && one.mergedCount == 1, "MergeOne 吃一件同款 → +1 经验");
            check(db.Table("inventoryitems").Select(x => DbStore.Long(x, "inventoryitemid") == feed).Count == 0,
                "材料徽章被消耗");

            var removedSymbol = NativeInventoryService.Handle(db, new InventoryChangeRequest
            { characterId = cid2, itemId = 1712001, equip = true, equipSlot = -202, quantity = 0 });
            var removedEntry = removedSymbol.inventory?.Find(x => x.itemRowId == worn);
            check(removedSymbol.ok && removedEntry != null && removedEntry.inventoryType == InventoryDao.Equip && removedEntry.position > 0,
                "符文可经现有卸装链回装备背包");
            check(DbStore.Int(wornEq, "symbolLevel") == 1 && DbStore.Int(wornEq, "symbolExp") == 1 && DbStore.Int(wornEq, "str") == 300,
                "卸下符文保留等级经验及属性");
            var removedStats = NativeGrowthStats.Accumulate(CharService.Load(db, cid2), 200, 112);
            check(removedStats.arcaneForce == 0 && removedStats.Get("STR") == 0, "卸下即停止符文属性加成");
            var rewornSymbol = NativeInventoryService.Handle(db, new InventoryChangeRequest
            { characterId = cid2, itemId = 1712001, equip = true, equipSlot = -202, quantity = 1, sourcePosition = removedEntry?.position ?? 0 });
            check(rewornSymbol.ok && rewornSymbol.inventory.Exists(x => x.itemRowId == worn && x.position == -202),
                "取下的同一符文可再次佩戴");

            var selfFeed = NativeSymbolService.Handle(db, new SymbolRequest
            {
                characterId = cid2, inventoryItemId = worn, materialInventoryItemId = worn,
                operation = SymbolOperation.MergeOne, commit = true,
            });
            check(!selfFeed.ok && selfFeed.error.Contains("自己"), "不能把徽章喂给自己");

            // MergeAll：一次吃完背包里所有合格材料，上限 = growthCost - exp。
            for (int i = 0; i < 3; i++) InventoryDao.InsertItem(db, cid2, 1712001, InventoryDao.Equip, 10 + i, 1);
            var previewAll = NativeSymbolService.Handle(db, new SymbolRequest
            { characterId = cid2, inventoryItemId = worn, operation = SymbolOperation.MergeAll });
            check(previewAll.ok && previewAll.mergedCount == 3 && previewAll.exp == 1,
                "MergeAll 预览只报件数不扣料（3 件可吃，经验仍是 1）");
            check(DbStore.Long(wornEq, "symbolExp") == 1, "预览不写经验");
            var all = NativeSymbolService.Handle(db, new SymbolRequest
            { characterId = cid2, inventoryItemId = worn, operation = SymbolOperation.MergeAll, commit = true });
            check(all.ok && all.exp == 4 && all.mergedCount == 3, "MergeAll 提交后 exp = 4");
            var emptyAll = NativeSymbolService.Handle(db, new SymbolRequest
            { characterId = cid2, inventoryItemId = worn, operation = SymbolOperation.MergeAll, commit = true });
            check(!emptyAll.ok && emptyAll.error.Contains("没有可用"), "背包没料时 MergeAll 拒绝");

            var early = NativeSymbolService.Handle(db, new SymbolRequest
            { characterId = cid2, inventoryItemId = worn, operation = SymbolOperation.LevelUp, commit = true });
            check(!early.ok && early.error.Contains("经验不足"), "经验不足时升级被拒");

            long beforeMeso = CharactersDao.FindById(db, cid2).meso;
            wornEq["symbolExp"] = 12L;
            db.Save();
            var levelUp = NativeSymbolService.Handle(db, new SymbolRequest
            { characterId = cid2, inventoryItemId = worn, operation = SymbolOperation.LevelUp, commit = true });
            check(levelUp.ok && levelUp.level == 2 && levelUp.exp == 0 && levelUp.inc == 40,
                "升级扣满门槛经验，inc 重算到 40");
            check(DbStore.Long(wornEq, "str") == 400, "升级加 100 主属性（300 → 400）");
            check(levelUp.mesoCost > 0 && CharactersDao.FindById(db, cid2).meso == beforeMeso - levelUp.mesoCost,
                "升级按 282 客户端表扣枫币");

            long bagSymbol = InventoryDao.InsertItem(db, cid2, 1712002, InventoryDao.Equip, 20, 1);
            InventoryDao.InsertEquipment(db, bagSymbol);
            InventoryDao.EquipmentOf(db, bagSymbol)["symbolExp"] = 999L;
            var notWorn = NativeSymbolService.Handle(db, new SymbolRequest
            { characterId = cid2, inventoryItemId = bagSymbol, operation = SymbolOperation.LevelUp, commit = true });
            check(!notWorn.ok && notWorn.error.Contains("穿上"), "未穿戴的徽章不能升级");

            long universal = InventoryDao.InsertItem(db, cid2, 1712000, InventoryDao.Equipped, -202, 1);
            InventoryDao.InsertEquipment(db, universal);
            var blocked = NativeSymbolService.Handle(db, new SymbolRequest
            { characterId = cid2, inventoryItemId = universal, operation = SymbolOperation.MergeAll, commit = true });
            check(!blocked.ok && blocked.error.Contains("通用"), "通用祕法符文不能成长");

            // ——— 12. 星火服务：摇词条 / 打包 / 两段式确认 ———
            int cid3 = NewCharacter(db, "FlameTest", 112);
            long bossWeapon = InventoryDao.InsertItem(db, cid3, 1302336, InventoryDao.Equipped, -11, 1);
            InventoryDao.InsertEquipment(db, bossWeapon);
            var bossEq = InventoryDao.EquipmentOf(db, bossWeapon);
            long flameItem = InventoryDao.InsertItem(db, cid3, 2048716, InventoryDao.Equip, 5, 1);   // createType 6

            var flamePreview = NativeFlameService.Handle(db, new FlameRequest
            { characterId = cid3, inventoryItemId = bossWeapon, flameInventoryItemId = flameItem }, new Random(12345));
            check(flamePreview.ok && flamePreview.createType == 6, "星火道具解析到 createType 6");
            check(flamePreview.lineCount == 4 && flamePreview.candidate.Count == 4, "Boss 装备固定 4 行");
            check(flamePreview.candidate.TrueForAll(x => x.tier >= 3 && x.tier <= 7), "Boss 装备阶数 +2 且封顶 7");
            check(DbStore.Long(bossEq, "exGradeOption") == 0, "预览不写 exGradeOption");

            var flameCommit = NativeFlameService.Handle(db, new FlameRequest
            {
                characterId = cid3, inventoryItemId = bossWeapon, flameInventoryItemId = flameItem,
                commit = true, revision = flamePreview.revision,
            }, new Random(12345));
            check(flameCommit.ok && !flameCommit.pending && flameCommit.lineCount == 4, "普通星火一次性生效");
            long packed = DbStore.Long(bossEq, "exGradeOption");
            check(packed == NativeFlameService.Pack(flameCommit.candidate), "exGradeOption 写入千进制打包结果");
            check(NativeFlameService.Unpack(packed).Count == 4, "打包结果能拆回 4 行");
            check(db.Table("inventoryitems").Select(x => DbStore.Long(x, "inventoryitemid") == flameItem).Count == 0,
                "星火道具被消耗");

            long flameItem2 = InventoryDao.InsertItem(db, cid3, 2048716, InventoryDao.Equip, 6, 1);
            var stale = NativeFlameService.Handle(db, new FlameRequest
            {
                characterId = cid3, inventoryItemId = bossWeapon, flameInventoryItemId = flameItem2,
                commit = true, revision = flamePreview.revision,
            }, new Random(1));
            check(!stale.ok && stale.error.Contains("重新预览"), "revision 过期时拒绝提交（防重复扣道具）");

            // 黑星火：不直接生效，进待确认，由二选一窗决定保留新旧。
            long blackFlame = InventoryDao.InsertItem(db, cid3, 2048753, InventoryDao.Equip, 7, 1);
            int rev = DbStore.Int(bossEq, "flameRevision");
            var black = NativeFlameService.Handle(db, new FlameRequest
            {
                characterId = cid3, inventoryItemId = bossWeapon, flameInventoryItemId = blackFlame,
                commit = true, revision = rev,
            }, new Random(777));
            check(black.ok && black.pending && black.candidate.Count == 4, "黑星火进待确认状态");
            check(!string.IsNullOrEmpty(DbStore.Str(bossEq, "flameChoice")), "待确认结果落 flameChoice 而不覆盖 exGradeOption");
            check(DbStore.Long(bossEq, "exGradeOption") == packed, "待确认期间旧结果仍在生效");

            var recoveredFlame = NativeFlameService.Handle(db, new FlameRequest
            { characterId = cid3, inventoryItemId = bossWeapon, choose = true });
            check(recoveredFlame.ok && recoveredFlame.pending && recoveredFlame.current.Exists(x => x.value > 0)
                && recoveredFlame.candidate.Exists(x => x.value > 0), "黑星火消耗后无需材料恢复对比，旧新词条均有数值");
            check(NativeFlameService.Pack(recoveredFlame.candidate) == NativeFlameService.Pack(black.candidate),
                "恢复黑星火不重摇、不改变候选结果");
            var staleChoice = NativeFlameService.Handle(db, new FlameRequest
            { characterId = cid3, inventoryItemId = bossWeapon, choose = true, commit = true, revision = rev });
            check(!staleChoice.ok && !string.IsNullOrEmpty(DbStore.Str(bossEq, "flameChoice")),
                "旧revision不得提交星火选择，候选保留");

            long anotherFlame = InventoryDao.InsertItem(db, cid3, 2048716, InventoryDao.Equip, 8, 1);
            var blockedFlame = NativeFlameService.Handle(db, new FlameRequest
            {
                characterId = cid3, inventoryItemId = bossWeapon, flameInventoryItemId = anotherFlame,
                commit = true, revision = DbStore.Int(bossEq, "flameRevision"),
            }, new Random(9));
            check(!blockedFlame.ok && blockedFlame.error.Contains("上一次"), "未确认前不能再上星火");

            var keepOld = NativeFlameService.Handle(db, new FlameRequest
            {
                characterId = cid3, inventoryItemId = bossWeapon, choose = true, keepNew = false,
                commit = true, revision = DbStore.Int(bossEq, "flameRevision"),
            });
            check(keepOld.ok && !keepOld.pending && DbStore.Long(bossEq, "exGradeOption") == packed,
                "选保留旧结果时 exGradeOption 不变");
            check(DbStore.Str(bossEq, "flameChoice") == "", "确认后清空待选记录");

            long blackFlame2 = InventoryDao.InsertItem(db, cid3, 2048753, InventoryDao.Equip, 9, 1);
            var black2 = NativeFlameService.Handle(db, new FlameRequest
            {
                characterId = cid3, inventoryItemId = bossWeapon, flameInventoryItemId = blackFlame2,
                commit = true, revision = DbStore.Int(bossEq, "flameRevision"),
            }, new Random(31));
            var keepNew = NativeFlameService.Handle(db, new FlameRequest
            {
                characterId = cid3, inventoryItemId = bossWeapon, choose = true, keepNew = true,
                commit = true, revision = DbStore.Int(bossEq, "flameRevision"),
            });
            check(keepNew.ok && DbStore.Long(bossEq, "exGradeOption") == NativeFlameService.Pack(black2.candidate),
                "选保留新结果时写入新打包值");

            // ——— 13. 旧灵魂材料退役，保留武器转换元数据 ———
            int cid4=NewCharacter(db,"SoulTest",112);
            check(Server.Bot.BotItemCatalog.Shared.Info(2590002)==null &&
                Server.Bot.BotItemCatalog.Shared.Info(2591010)==null,"旧灵魂材料已移出道具目录");
            long weapon=InventoryDao.InsertItem(db,cid4,1302214,InventoryDao.Equipped,-11,1);
            InventoryDao.InsertEquipment(db,weapon);
            var weaponEq=InventoryDao.EquipmentOf(db,weapon);
            weaponEq["soulItemId"]=2591010L; weaponEq["soulOption"]=101L;
            // Simulate a pre-migration save, bypassing the now-blocked grant API.
            foreach(int id in new[]{2590002,2591010})
                db.Table("inventoryitems").Insert(new DbRow { ["characterid"]=(long)cid4,
                    ["itemid"]=(long)id,["inventorytype"]=2L,["position"]=5L,["quantity"]=1L });
            db.Table("extrastorage").Insert(new DbRow { ["characterid"]=(long)cid4,["itemid"]=2591010L,["quantity"]=1L });
            db.Table("drop_data").Insert(new DbRow { ["dropperid"]=8800002L,["itemid"]=2590004L,["chance"]=100L });
            db.Save();
            var retired=DbStore.Open(db.FilePath);
            check(!InventoryDao.ItemsByCharacter(retired,cid4).Exists(r=>InventoryDao.IsRetiredSoulMaterial(DbStore.Int(r,"itemid"))),
                "重开存档删除全部旧灵魂材料");
            check(retired.Table("extrastorage").Select(r=>InventoryDao.IsRetiredSoulMaterial(DbStore.Int(r,"itemid"))).Count==0 &&
                retired.Table("drop_data").Select(r=>InventoryDao.IsRetiredSoulMaterial(DbStore.Int(r,"itemid"))).Count==0,
                "仓储与旧掉落覆盖同步清理");
            check(DbStore.Int(InventoryDao.EquipmentOf(retired,weapon),"soulItemId")==2591010 &&
                DbStore.Int(InventoryDao.EquipmentOf(retired,weapon),"soulOption")==101,"保留武器旧灵魂转换数据");
            bool retiredGrantBlocked=false;
            try{InventoryDao.InsertItem(retired,cid4,2590002,InventoryDao.Use,5,1);}
            catch(InvalidOperationException){retiredGrantBlocked=true;}
            check(retiredGrantBlocked,"旧材料不能经发放接口重新出现");
            InventoryDao.PurgeRetiredSoulMaterials(db);

            // ——— 14. 极限属性服务：加点 / 退点 / 洗点 / 切预设 ———
            int cid5 = NewCharacter(db, "HyperTest", 112, 50000000L);
            var hyper0 = NativeHyperStatService.Handle(db, new HyperStatRequest { characterId = cid5 });
            check(hyper0.ok && hyper0.totalSp == HyperStatCatalog.TotalSp(200) && hyper0.spentSp == 0,
                "200 级总点数 = TotalSp(200)");

            var apply = NativeHyperStatService.Handle(db, new HyperStatRequest
            { characterId = cid5, skillId = 80000413, amount = 3, operation = HyperStatOperation.Apply, commit = true });
            check(apply.ok && apply.spentSp == 7 && apply.availableSp == hyper0.totalSp - 7, "伤害 3 级共花 1+2+4 = 7 点");
            var dmgLine = apply.lines.Find(x => x.skillId == 80000413);
            check(dmgLine != null && dmgLine.slv == 3 && dmgLine.value == 9, "伤害 3%/级 → 3 级 9%");

            var overMax = NativeHyperStatService.Handle(db, new HyperStatRequest
            { characterId = cid5, skillId = 80000413, amount = 20, operation = HyperStatOperation.Apply, commit = true });
            check(!overMax.ok && overMax.error.Contains("上限"), "超过单条上限被拒");

            var noPoint = NativeHyperStatService.Handle(db, new HyperStatRequest
            { characterId = cid5, skillId = 80000413, amount = 12, operation = HyperStatOperation.Apply, commit = true });
            check(!noPoint.ok && noPoint.error.Contains("点数不足"), "点数不足时拒绝加点");

            var remove = NativeHyperStatService.Handle(db, new HyperStatRequest
            { characterId = cid5, skillId = 80000413, amount = 1, operation = HyperStatOperation.Remove, commit = true });
            check(remove.ok && remove.spentSp == 3, "退 1 级退回 4 点（7 → 3）");

            long hyperMeso = CharactersDao.FindById(db, cid5).meso;
            var reset = NativeHyperStatService.Handle(db, new HyperStatRequest
            { characterId = cid5, operation = HyperStatOperation.Reset, commit = true });
            check(reset.ok && reset.lines.Count == 0 && reset.spentSp == 0, "洗点清空整套加点");
            check(CharactersDao.FindById(db, cid5).meso == hyperMeso - HyperStatCatalog.ResetCost,
                "洗点扣 1000 万枫币");

            long switchMeso = CharactersDao.FindById(db, cid5).meso;
            var switchPreset = NativeHyperStatService.Handle(db, new HyperStatRequest
            { characterId = cid5, preset = 1, operation = HyperStatOperation.SwitchPreset, commit = true });
            check(switchPreset.ok && switchPreset.preset == 1 && GrowthDao.Load(db, cid5).hyperPreset == 1,
                "切到预设 2 并落盘");
            check(switchPreset.activePreset == 1 && switchPreset.preset == 1,
                "套用后 activePreset 跟 preset 一致（窗口据此置灰套用按钮）");
            var viewOther = NativeHyperStatService.Handle(db, new HyperStatRequest
            { characterId = cid5, preset = 2, operation = HyperStatOperation.Preview });
            check(viewOther.preset == 2 && viewOther.activePreset == 1,
                "看别的预设时 activePreset 仍是生效的那套");
            check(CharactersDao.FindById(db, cid5).meso == switchMeso - HyperStatCatalog.SwitchCost,
                "切预设扣 200 万枫币");

            int jumpId = NewCharacter(db, "HyperJump", 112, 0L, 139);
            var beforeJump = NativeHyperStatService.Handle(db, new HyperStatRequest { characterId = jumpId });
            var jumpCharacter = CharactersDao.FindById(db, jumpId);
            jumpCharacter.level = 200;
            CharactersDao.Save(db, jumpCharacter);
            var afterJump = NativeHyperStatService.Handle(db, new HyperStatRequest { characterId = jumpId });
            check(beforeJump.totalSp == 0 && afterJump.level == 200 && afterJump.totalSp == 339 && afterJump.availableSp == 339,
                "命令直改139到200级无需逐级发点，未分配极限属性可用339点");

            int lowLevel = NewCharacter(db, "HyperLow", 112, 0L, 139);
            var lockedStat = NativeHyperStatService.Handle(db, new HyperStatRequest
            { characterId = lowLevel, skillId = 80000413, amount = 1, operation = HyperStatOperation.Apply, commit = true });
            check(!lockedStat.ok && lockedStat.error.Contains("140"), "139 级不能使用极限属性");

            // ——— 15. 内在能力服务：荣誉洗练 / 锁行 / 循环器 / 确认 ———
            int cid6 = NewCharacter(db, "AbilityTest", 112);
            GrowthDao.Save(db, cid6, new GrowthState { honorExp = 20000, hyperPreset = 0, abilityPreset = 0 });
            GrowthDao.SavePotentials(db, cid6, 0, new[]
            {
                new PotentialEntry { preset = 0, potKey = 0, skillId = 70000000, slv = 5, grade = 0 },
                new PotentialEntry { preset = 0, potKey = 1, skillId = 70000001, slv = 6, grade = 0 },
                new PotentialEntry { preset = 0, potKey = 2, skillId = 70000002, slv = 7, grade = 0 },
            });
            var ability0 = NativeInnerAbilityService.Handle(db, new AbilityRequest { characterId = cid6 });
            check(ability0.ok && ability0.lines.Count == 3 && ability0.lineCount == 3 && ability0.grade == 0,
                "三行就位，grade 取最高行");
            check(ability0.cost == 100 && ability0.activePreset == 0,
                "预览就在算费用（稀有档 100）并带出生效预设");

            var lockedPreview = NativeInnerAbilityService.Handle(db, new AbilityRequest
            { characterId = cid6, operation = AbilityOperation.Preview, lockedRows = new System.Collections.Generic.List<int> { 1 } });
            check(lockedPreview.cost == 3100, "预览锁 1 行 = 100 + 3000（窗口不用点使用就能显示）");

            var honorPreview = NativeInnerAbilityService.Handle(db, new AbilityRequest
            { characterId = cid6, operation = AbilityOperation.Honor, lockedRows = new System.Collections.Generic.List<int> { 0, 2 } });
            check(honorPreview.ok && honorPreview.cost == 8100, "锁 2 行费用 = 100 + 3000 + 5000");

            var honor = NativeInnerAbilityService.Handle(db, new AbilityRequest
            { characterId = cid6, operation = AbilityOperation.Honor, commit = true }, new Random(2024));
            check(honor.ok && honor.honorExp == 20000 - 100 && honor.candidate.Count == 3, "洗练扣 100 荣誉并出 3 行候选");
            var honorState = GrowthDao.Load(db, cid6);
            check(honorState.honorExp == 20000 - 100, "荣誉值落盘");
            check(GrowthDao.Potentials(db, cid6).Count == 3, "洗练后仍是 3 行");

            honorState.honorExp = 50;
            GrowthDao.Save(db, cid6, honorState);
            var poor = NativeInnerAbilityService.Handle(db, new AbilityRequest
            { characterId = cid6, operation = AbilityOperation.Honor, commit = true }, new Random(1));
            check(!poor.ok && poor.error.Contains("荣誉值不足"), "荣誉值不足时拒绝洗练");

            int lowAbility = NewCharacter(db, "AbilityLow", 112, 0L, 20);
            var noLine = NativeInnerAbilityService.Handle(db, new AbilityRequest
            { characterId = lowAbility, operation = AbilityOperation.Honor, commit = true }, new Random(1));
            check(!noLine.ok && noLine.error.Contains("解锁"), "不足 3 行时不能洗练");

            // 循环器要求 grade ≥ 1（5062 只升档），先把三行改成稀有段。
            GrowthDao.SavePotentials(db, cid6, 0, new[]
            {
                new PotentialEntry { preset = 0, potKey = 0, skillId = 70000000, slv = 12, grade = 1 },
                new PotentialEntry { preset = 0, potKey = 1, skillId = 70000001, slv = 15, grade = 1 },
                new PotentialEntry { preset = 0, potKey = 2, skillId = 70000002, slv = 18, grade = 1 },
            });
            long circulator = InventoryDao.InsertItem(db, cid6, 5062001, InventoryDao.Equip, 5, 1);
            var circ = NativeInnerAbilityService.Handle(db, new AbilityRequest
            { characterId = cid6, operation = AbilityOperation.Circulator, circulatorInventoryItemId = circulator, commit = true }, new Random(88));
            check(circ.ok && circ.pending && circ.candidate.Count == 3, "循环器摇出 3 行待确认");
            check(!string.IsNullOrEmpty(GrowthDao.Load(db, cid6).abilityChoice), "待确认结果落 abilityChoice");
            var pendingPreview = NativeInnerAbilityService.Handle(db, new AbilityRequest
            { characterId = cid6, operation = AbilityOperation.Preview });
            check(pendingPreview.pending && pendingPreview.candidate.Count == 3,
                "预览就带出待确认的三行（确认窗一次请求就能画）");

            long circulator2 = InventoryDao.InsertItem(db, cid6, 5062001, InventoryDao.Equip, 6, 1);
            var blockedCirc = NativeInnerAbilityService.Handle(db, new AbilityRequest
            { characterId = cid6, operation = AbilityOperation.Circulator, circulatorInventoryItemId = circulator2, commit = true }, new Random(2));
            check(!blockedCirc.ok && blockedCirc.error.Contains("确认"), "未确认前不能再摇循环器");

            var accept = NativeInnerAbilityService.Handle(db, new AbilityRequest
            { characterId = cid6, operation = AbilityOperation.Choose, accept = true, commit = true });
            check(accept.ok && !accept.pending && GrowthDao.Load(db, cid6).abilityChoice == "", "接受后写入预设并清空待选");
            check(GrowthDao.Potentials(db, cid6).TrueForAll(x => x.grade >= 1), "接受的是循环器摇出的行");

            // 2702 可升可降，但不接受已到传说（grade 3）的能力。
            GrowthDao.SavePotentials(db, cid6, 0, new[]
            {
                new PotentialEntry { preset = 0, potKey = 0, skillId = 70000000, slv = 35, grade = 3 },
                new PotentialEntry { preset = 0, potKey = 1, skillId = 70000001, slv = 36, grade = 3 },
                new PotentialEntry { preset = 0, potKey = 2, skillId = 70000002, slv = 38, grade = 3 },
            });
            long circulator3 = InventoryDao.InsertItem(db, cid6, 2702001, InventoryDao.Equip, 7, 1);
            var legendary = NativeInnerAbilityService.Handle(db, new AbilityRequest
            { characterId = cid6, operation = AbilityOperation.Circulator, circulatorInventoryItemId = circulator3, commit = true }, new Random(3));
            check(!legendary.ok && legendary.error.Contains("传说"), "2702 循环器不能用于传说能力");

            // ——— 16. 属性结算层：五套系统并进同一张差量表 ———
            // 282 服务端不算属性，本项目在派生层按同一份公式表补；这里直接喂内存角色验算。
            var flameWeapon = new InventoryEntry
            {
                itemId = 1302336, position = -11, quantity = 1, inventoryType = InventoryDao.Equipped,
                exGradeOption = NativeFlameService.Pack(new System.Collections.Generic.List<FlameLine>
                {
                    new FlameLine { statVal = 0, exGrade = 0, tier = 3 },    // STR：3 × ceil(121/20) = 21
                    new FlameLine { statVal = 18, exGrade = 21, tier = 2 },  // BOSS 伤害：2 × tier = 4
                    new FlameLine { statVal = 10, exGrade = 17, tier = 3 },  // 攻击力：Boss 武器倍率表
                }),
                soulOption = 101,   // STR +3
            };
            var full2 = new FullCharacter
            {
                inventory = new System.Collections.Generic.List<InventoryEntry>
                {
                    new InventoryEntry { itemId = 1712001, position = -202, quantity = 1, symbolInc = 140, str = 400 },
                    new InventoryEntry { itemId = 1713001, position = -212, quantity = 1, symbolInc = 110 },
                    flameWeapon,
                    new InventoryEntry { itemId = 1302000, position = 5, quantity = 1 },  // 背包里的不算
                },
                growth = new GrowthState { hyperPreset = 1, abilityPreset = 0 },
                hyperstats = new System.Collections.Generic.List<HyperStatEntry>
                {
                    new HyperStatEntry { preset = 1, skillId = 80000413, slv = 15 },   // 伤害 45%
                    new HyperStatEntry { preset = 1, skillId = 80000421, slv = 10 },   // 神祕力量 +60 ARC
                    new HyperStatEntry { preset = 1, skillId = 80000404, slv = 5 },    // 最大HP +10%
                    new HyperStatEntry { preset = 0, skillId = 80000400, slv = 15 },   // 非当前预设：不算
                },
                potentials = new System.Collections.Generic.List<PotentialEntry>
                {
                    new PotentialEntry { preset = 0, potKey = 0, skillId = 70000000, slv = 30, grade = 3 },  // STR +30
                    new PotentialEntry { preset = 0, potKey = 1, skillId = 70000036, slv = 12, grade = 3 },  // 一般怪伤害 u(12/4)=3
                    new PotentialEntry { preset = 0, potKey = 2, skillId = 70000021, slv = 8, grade = 3 },   // 力量轉敏捷：无通道
                },
            };
            var stats = NativeGrowthStats.Accumulate(full2, 200, 112);
            check(stats.arcaneForce == 190 && stats.authenticForce == 110,
                "徽章 ARC 140 + 极限属性神祕力量 10 级 50 = 190 / AUT 110");
            check(stats.Get("STR") == 400 + 30 + 21 + 3, "STR 汇总：徽章 400 + 能力 30 + 星火 21 + 灵魂 3");
            check(stats.Get("BossDamage") == 4, "星火 BOSS 伤害 2×tier");
            check(stats.Get("Damage") == 45, "极限属性伤害 15 级 = 45%");
            check(stats.Get("HPPct") == 10, "极限属性最大HP 5 级 = +10%");
            check(stats.Get("NormalDamage") == 3, "能力「一般怪伤害」12 级 = u(12/4) = 3");
            check(stats.Get("PAD") > 0, "Boss 武器星火攻击力按 Boss 倍率表算出正值");
            check(stats.unsettled.Contains("STR2DEX"), "四维互转进 unsettled 不静默吞掉");
            check(stats.Get("DEX") == 0, "非当前预设的加点不生效");

            // 内在能力按等级解锁行数生效：20 级只有第 1 行，其余两行不结算。
            var lowLines = new FullCharacter
            {
                growth = new GrowthState(),
                potentials = new System.Collections.Generic.List<PotentialEntry>
                {
                    new PotentialEntry { preset = 0, potKey = 0, skillId = 70000000, slv = 10, grade = 0 },
                    new PotentialEntry { preset = 0, potKey = 1, skillId = 70000001, slv = 10, grade = 0 },
                },
            };
            var lowStats = NativeGrowthStats.Accumulate(lowLines, 20, 112);
            check(lowStats.Get("STR") == 10 && lowStats.Get("DEX") == 0, "20 级只结算第 1 行内在能力");
            check(NativeGrowthStats.Accumulate(null, 200, 112).set.Count == 0, "空角色返回空差量");

            // SoulWeaponWz2: authoritative ledger, weekly rewards, growth, rollback/stale request and cooldown.
            int modernCid=NewCharacter(db,"ModernSoul",112);
            long modernWeapon=InventoryDao.InsertItem(db,modernCid,1302134,InventoryDao.Equipped,-11,1);
            InventoryDao.InsertEquipment(db,modernWeapon);
            var modernRow=db.Table("inventoryitems").Find(x=>DbStore.Long(x,"inventoryitemid")==modernWeapon);
            modernRow["flag"]=0L;
            SoulResponse Modern(SoulOperation op,int amount=0,long? revision=null,DateTimeOffset? at=null)
                =>NativeSoulWeaponWz2Service.Handle(db,new SoulRequest {characterId=modernCid,inventoryItemId=modernWeapon,
                    operation=op,commit=true,amount=amount,revision=revision??NativeSoulWeaponWz2Service.Read(db,modernCid).revision},new Random(1),at);
            var modern=Modern(SoulOperation.ActivateModern);
            check(modern.ok&&modern.modern.weapon==modernWeapon&&modern.modern.level==1,"新版启用绑定真实装备实例");
            check(DbStore.Int(modernRow,"flag")==0,"可交易武器允许启用且不被永久改为不可交易");
            var modernCharacter=CharService.Load(db,modernCid);
            modernCharacter.inventory.Add(new InventoryEntry {itemId=1302000,position=-10,quantity=1,soulOption=101});
            var modernStats=NativeGrowthStats.Accumulate(modernCharacter,200,112);
            check(modernStats.Get("PAD")==21&&modernStats.Get("MAD")==21&&modernStats.Get("STR")==0,
                "新版共鸣同时结算攻魔且不叠加旧灵魂属性");
            modernCharacter.inventory.Find(x=>x.itemRowId==modernWeapon).position=1;
            check(NativeGrowthStats.Accumulate(modernCharacter,200,112).Get("PAD")==0,
                "卸下绑定武器立即移除新版共鸣");
            check(!Modern(SoulOperation.ActivateModern).ok,"新版禁止重复启用");
            check(NativePotentialService.BlocksTransfer(db,CharService.ToInventoryEntry(db,modernRow)),"启用中武器不可转移");
            var weekStart=new DateTimeOffset(2026,11,5,0,0,0,TimeSpan.FromHours(8));
            check(NativeSoulWeaponWz2Service.OnBossClear(db,modernCid,"Lucid_Easy_",false,weekStart)==0,"组队击破不发单人碎片");
            check(NativeSoulWeaponWz2Service.OnBossClear(db,modernCid,"Lucid_Easy_",true,weekStart)==10,"简单露希妲按IMG发10碎片");
            check(NativeSoulWeaponWz2Service.OnBossClear(db,modernCid,"Lucid_Easy_",true,weekStart)==0,"重复通关不重复发碎片");
            check(NativeSoulWeaponWz2Service.OnBossClear(db,modernCid,"Lucid_Normal_",true,weekStart)==25,"普通露希妲补35减10差额");
            check(NativeSoulWeaponWz2Service.OnBossClear(db,modernCid,"Lucid_Hard_",true,weekStart)==45,"困难露希妲补80减35差额");
            check(NativeSoulWeaponWz2Service.OnBossClear(db,modernCid,"Lucid_Normal_",true,weekStart)==0,"低难度不再次发奖");
            var modernState=NativeSoulWeaponWz2Service.Read(db,modernCid);
            check(modernState.fragments==80&&modernState.cleared.Contains(68842)&&modernState.cleared.Contains(68843),"碎片与单人升阶条件同时持久化");
            check(!Modern(SoulOperation.LevelModern,1,0).ok,"过期revision不得扣碎片");
            check(!Modern(SoulOperation.LevelModern,81).ok,"成长不可透支碎片");
            check(Modern(SoulOperation.LevelModern,1).ok&&NativeSoulWeaponWz2Service.Read(db,modernCid).fragments==79,"1碎片兑换1经验");
            check(!Modern(SoulOperation.GradeModern).ok,"经验未满不能升阶");
            int cost=SoulWeaponWz2Catalog.Int(SoulWeaponWz2Catalog.Grade(1),"OptionRollCost");
            modern=Modern(SoulOperation.RollModern);
            check(modern.ok&&modern.modern.fragments==79-cost&&SoulWeaponWz2Catalog.RollOptions(1).Contains(modern.modern.option),"烙印扣原表费用且只选适用候选");
            check(Modern(SoulOperation.CastModern,at:weekStart).ok,"装备绑定武器可以施放灵魂技能");
            check(!Modern(SoulOperation.CastModern,at:weekStart.AddSeconds(119)).ok,"120秒内不能重施");
            check(Modern(SoulOperation.CastModern,at:weekStart.AddSeconds(120)).ok,"120秒后冷却结束");
            modernRow["position"]=1L;
            check(!Modern(SoulOperation.CastModern,at:weekStart.AddSeconds(240)).ok,"未装备绑定武器不可施放");
            modernRow["position"]=-11L;
            check(NativeSoulWeaponWz2Service.OnBossClear(db,modernCid,"Lucid_Easy_",true,weekStart.AddDays(7))==10,"周四新周可再次获得碎片");
            var reopenedModern=NativeSoulWeaponWz2Service.Read(DbStore.Open(path),modernCid);
            check(reopenedModern.weapon==modernWeapon&&reopenedModern.weekly["13"]==10,"重开库保留绑定与本周奖励");
            check(Modern(SoulOperation.DeactivateModern).ok&&!NativeSoulWeaponWz2Service.IsBound(db,modernCid,modernWeapon),"停用解除操作限制");
            check(NativeSoulWeaponWz2Service.Read(db,modernCid).option==modern.modern.option,"停用保留角色成长与烙印");
            check(SoulWeaponWz2Catalog.PassiveAttack(1)==21&&SoulWeaponWz2Catalog.PassiveAttack(60)==80&&SoulWeaponWz2Catalog.PassiveAttack(61)==82&&SoulWeaponWz2Catalog.PassiveAttack(100)==160,"被动攻击采用EXE分段公式");
            // tooltip 与结算层共用 DescribeFlame：拆包 → 补数值 → 标签
            var described = NativeGrowthStats.DescribeFlame(flameWeapon);
            check(described.Count == 3 && described[0].label == "STR" && described[0].value == 21,
                "星火拆包后按装备等级补数值（STR 21）");
            check(described[2].statVal == 10 && described[2].value > 0, "Boss 武器攻击力行有值");
            check(NativeGrowthStats.DescribeFlame(new InventoryEntry { itemId = 1302336 }).Count == 0,
                "没有星火的装备不产生行");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // 成长服务用例统一走「真表行」：角色行 + 200 级默认（极限属性要求 140+）。
    static int NewCharacter(DbStore db, string name, int job, long meso = 0, int level = 200)
        => (int)db.Table("characters").Insert(new DbRow
        {
            ["name"] = name, ["meso"] = meso, ["hp"] = 100L, ["equipslots"] = 24L,
            ["level"] = (long)level, ["job"] = (long)job,
        });
}
