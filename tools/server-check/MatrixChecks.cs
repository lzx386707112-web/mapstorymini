// 282 V 矩阵 / HEXA 矩阵的离线自检：目录表口径 + 协议 7 个 op 的扣费与落盘。
// 运行：cd tools/server-check && dotnet run -- --matrix
//
// 盯三件容易悄悄跑偏的事：
//   1. 费用表是「首个 ≥ 目标等级的阈值」（ceiling），不是 floor 查表 —— 差一格就多退少扣。
//   2. V 点 9999 上限是「整体拒绝」不是截断；特殊核心不退点、不参与重置。
//   3. HEXA 门禁三件套按**六转编码原样比对**（113 在白名单，112 不在），不做 -1 归一化。
using System;
using System.IO;

static class MatrixChecks
{
    public static void Run(Action<bool, string> check)
    {
        checkSchema(check);
        checkVMatrixData(check);
        checkVMatrixService(check);
        checkHexaData(check);
        checkHexaService(check);
    }

    // 空值安全的属性核心读取：失配时返回空视图，断言会看到 level=0 而不是 NRE。
    static HexaStatView StatOf(HexaResponse r, int coreId, int preset = -1)
        => r.stats.Find(s => s.coreId == coreId && (preset < 0 || s.preset == preset)) ?? new HexaStatView();

    // —————————————————— 1. Schema ——————————————————

    static void checkSchema(Action<bool, string> check)
    {
        string path = Path.Combine(Path.GetTempPath(), "mapledash_matrix_" + Guid.NewGuid().ToString("N") + ".json");
        var db = DbStore.Create(path);
        try
        {
            check(db.HasTable("vmatrix_state") && db.HasTable("vmatrix_cores") &&
                  db.HasTable("hexa_state") && db.HasTable("hexa_skills") && db.HasTable("hexa_stats"),
                "V/HEXA 五张表存在");
            check(db.Table("vmatrix_state").Col("points") != null && db.Table("vmatrix_state").Col("revision") != null,
                "vmatrix_state 有 points/revision");
            check(db.Table("vmatrix_cores").Col("coreid") != null && db.Table("vmatrix_cores").Col("selected") != null &&
                  db.Table("vmatrix_cores").Col("expiresat") != null && db.Table("vmatrix_cores").Col("protectedlevel") != null,
                "vmatrix_cores 有 coreid/selected/expiresat/protectedlevel");
            check(db.Table("hexa_state").Col("solerda") != null && db.Table("hexa_state").Col("fragments") != null &&
                  db.Table("hexa_state").Col("solerdagauge") != null, "hexa_state 三列");
            check(db.Table("hexa_stats").Col("preset") != null && db.Table("hexa_stats").Col("weight") != null &&
                  db.Table("hexa_stats").Col("applied") != null && db.Table("hexa_stats").Col("stat3") != null,
                "hexa_stats 有 preset/weight/applied/stat1..3");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // —————————————————— 2. V 矩阵目录表 ——————————————————

    static void checkVMatrixData(Action<bool, string> check)
    {
        VMatrixCatalog.Ensure();

        check(VMatrixCatalog.CoreCount == 580, "V 核心 580 个");
        check(VMatrixCatalog.ResetMeso == 10000000 && VMatrixCatalog.RegisterSpecialCost == 14 &&
              VMatrixCatalog.ExtendSpecialCost == 50 && VMatrixCatalog.TransformCost == 2 &&
              VMatrixCatalog.TransformItem == 2439869 && VMatrixCatalog.TransformMinutes == 10080,
            "info 六个经济参数（重置 1000 万 / 特殊开启 14 / 延期 50 / 转换 2 点→2439869 / 10080 分钟）");

        var g112 = VMatrixCatalog.Group(112);
        check(g112 != null && g112.skill.Length == 4 && g112.enforce.Length == 6 && g112.common.Length == 19,
            "职业组 112 = 技能 4 / 强化 6 / 共通 19");
        check(ReferenceEquals(VMatrixCatalog.Group(113), g112), "六转编码 113 回退到四转组 112");
        check(VMatrixCatalog.Group(999) == null, "未知职业没有组");

        // 31 格三分区
        var slots = VMatrixSlots.Assign(g112);
        check(slots.Length == 31 && slots[0] == g112.skill[0] && slots[3] == g112.skill[3] &&
              slots[4] == g112.enforce[0] && slots[9] == g112.enforce[5] &&
              slots[10] == g112.common[0] && slots[28] == g112.common[18] &&
              slots[29] == 0 && slots[30] == 0, "31 格分配：4 技能 + 6 强化 + 19 共通，末两格留白");
        check(VMatrixSlots.SectionOf(0) == 0 && VMatrixSlots.SectionOf(3) == 0 &&
              VMatrixSlots.SectionOf(4) == 1 && VMatrixSlots.SectionOf(9) == 1 &&
              VMatrixSlots.SectionOf(10) == 2 && VMatrixSlots.SectionOf(30) == 2, "SectionOf 三段边界");

        // 费用表：ceiling 语义
        check(VMatrixCatalog.CostForLevel(10010001, 1) == 7, "技能核心 1 级 = 7 点");
        check(VMatrixCatalog.CostForLevel(10010001, 2) == 4 && VMatrixCatalog.CostForLevel(10010001, 10) == 4,
            "2..10 级按 ceiling 取 4（不是 7）");
        check(VMatrixCatalog.CostForLevel(10010001, 11) == 6 && VMatrixCatalog.CostForLevel(10010001, 20) == 6,
            "11..20 级按 ceiling 取 6");
        check(VMatrixCatalog.CostForLevel(10010001, 21) == 9 && VMatrixCatalog.CostForLevel(10010001, 30) == 9,
            "21..30 级按 ceiling 取 9");
        check(VMatrixCatalog.CostForLevel(10010001, 31) < 0, "超过 maxLevel 无费用");
        check(VMatrixCatalog.CostForLevel(30000000, 1) < 0, "特殊核心不走等级费用");
        check(VMatrixCatalog.CostBetween(10010001, 0, 30) == 7 + 9 * 4 + 10 * 6 + 10 * 9,
            "0→30 累计 193 点（7 + 36 + 60 + 90）");
        check(VMatrixCatalog.CostForLevel(20010000, 40) == 1 && VMatrixCatalog.CostForLevel(20010000, 41) == 2 &&
              VMatrixCatalog.CostForLevel(20010000, 60) == 2, "强化核心 1..40 = 1 点 / 41..60 = 2 点");
        check(VMatrixCatalog.CostBetween(20010000, 0, 60) == 80, "0→60 累计 80 点（40×1 + 20×2）");

        // allowed / resetFloor
        check(VMatrixCatalog.Allowed(112, 10010001), "本职业技能核心可用");
        check(!VMatrixCatalog.Allowed(112, 20020000), "他职业强化核心不可用");
        check(VMatrixCatalog.Allowed(112, 30000000), "特殊核心任意职业可用");
        check(!VMatrixCatalog.Allowed(112, 40000000), "type 3（不可用）核心不放行");
        check(!VMatrixCatalog.Allowed(999, 30000000), "没有职业组的职业连特殊核心也不放行");
        check(VMatrixCatalog.ResetFloor(112, 10010001) == 1, "技能组核心重置保底 1 级");
        check(VMatrixCatalog.ResetFloor(112, 10000000) == 0, "可自开的共通核心重置归零");
        check(VMatrixCatalog.ResetFloor(112, 20010000) == 0, "强化核心重置归零");

        // 宝石
        var gem = VMatrixCatalog.Gemstone(2436037);
        check(gem != null && gem.points == 2 && gem.openCore == 10000000 && gem.reqQuest == 0,
            "宝石 2436037 = 2 点 + 开启连接绳索");
        var gated = VMatrixCatalog.Gemstone(2438411);
        check(gated != null && gated.points == 5 && gated.reqQuest == 37871 && gated.questAlert.Length > 0,
            "宝石 2438411 挂任务 37871 与提示文案");
        check(VMatrixCatalog.Gemstone(2439869) != null, "转换产出的 2439869 自己是宝石（1 点）");
        check(VMatrixCatalog.Gemstone(1111111) == null, "未知道具不是宝石");
    }

    // —————————————————— 3. V 矩阵 7 个 op ——————————————————

    static void checkVMatrixService(Action<bool, string> check)
    {
        string path = Path.Combine(Path.GetTempPath(), "mapledash_vmatrix_" + Guid.NewGuid().ToString("N") + ".json");
        var db = DbStore.Create(path);
        try
        {
            // 六转编码 113：服务端要能回退到四转组 112
            int cid = (int)db.Table("characters").Insert(new DbRow
            {
                ["name"] = "VHero", ["job"] = 113L, ["level"] = 200L,
                ["meso"] = 50000000L, ["hp"] = 100L, ["maxhp"] = 100L,
                ["equipslots"] = 24L, ["useslots"] = 24L, ["setupslots"] = 24L, ["etcslots"] = 24L,
            });

            VMatrixRequest Req(VMatrixOperation op, int core = 0, int target = 0, int item = 0, int count = 0, bool commit = true)
                => new VMatrixRequest
                {
                    characterId = cid, operation = op, coreId = core, targetLevel = target,
                    itemId = item, count = count, commit = commit,
                };

            // 准入三件套
            db.Table("characters").Update(DbStore.Eq("id", (long)cid), new DbRow { ["level"] = 199L });
            var r = NativeVMatrixService.Handle(db, Req(VMatrixOperation.Upgrade, core: 10010001, target: 30));
            check(!r.ok && r.error.Contains("200 级"), "199 级拒绝使用 V 矩阵");
            // 快照不拦准入：窗口仍能画出来，只带锁定文案
            r = NativeVMatrixService.Handle(db, new VMatrixRequest { characterId = cid, operation = VMatrixOperation.Snapshot });
            check(r.ok && !r.eligible && r.error.Contains("200 级") && r.slots.Length == 31,
                "未准入时快照仍下发 31 格并带锁定文案");
            db.Table("characters").Update(DbStore.Eq("id", (long)cid), new DbRow { ["level"] = 200L });
            r = NativeVMatrixService.Handle(db, Req(VMatrixOperation.Upgrade, core: 10010001, target: 30));
            check(!r.ok && r.error.Contains("五转任务"), "200 级但未完成五转任务拒绝");
            QuestStatusDao.Upsert(db, cid, 1465, 2, 1);
            r = NativeVMatrixService.Handle(db, new VMatrixRequest { characterId = cid, operation = VMatrixOperation.Snapshot });
            check(r.ok && r.eligible && r.maxPoints == 9999, "五转任务完成后准入，上限 9999");
            check(r.slots.Length == 31 && r.cores.Count >= 29, "快照下发 31 格与 29 个职业核心");
            check(r.resetMeso == 10000000 && r.transformCost == 2 && r.transformItem == 2439869 && r.registerCost == 14,
                "快照带全经济参数给窗口");
            check(r.cores.TrueForAll(c => c.name.Length > 0), "每个核心都带名字");

            // 没有 V 点时转换要拒
            r = NativeVMatrixService.Handle(db, Req(VMatrixOperation.Transform, count: 10));
            check(!r.ok && r.error.Contains("V 点数不足"), "0 点时拒绝转换");

            db.Table("vmatrix_state").Insert(new DbRow { ["characterid"] = (long)cid, ["points"] = 2000L });
            r = NativeVMatrixService.Handle(db, Req(VMatrixOperation.Transform, count: 10));
            check(r.ok && r.points == 1980, "转换 10 颗扣 20 点");
            check(InventoryDao.CountOf(db, cid, 2439869) == 10, "生成 10 颗限时宝石");
            var gemRow = db.Table("inventoryitems").Find(DbStore.Eq("characterid", (long)cid));
            check(gemRow != null && DbStore.Long(gemRow, "expiration") > 0, "限时宝石带到期时间戳");

            // 使用宝石（1 点/颗）
            r = NativeVMatrixService.Handle(db, Req(VMatrixOperation.UseGemstone, item: 2439869, count: 5));
            check(r.ok && r.points == 1985, "用 5 颗 1 点宝石 → 1985");
            check(InventoryDao.CountOf(db, cid, 2439869) == 5, "宝石已消耗 5 颗");

            // 带 openCore 的宝石：首颗只开核心不产点
            NativeInventoryService.AddItems(db, CharactersDao.FindById(db, cid), 2436037, 2, -1);
            r = NativeVMatrixService.Handle(db, Req(VMatrixOperation.UseGemstone, item: 2436037, count: 2));
            check(r.ok && r.points == 1987, "带 openCore 的宝石首颗只开核心（2 颗只 +2 点）");
            var opened = r.cores.Find(c => c.coreId == 10000000);
            check(opened != null && opened.level == 1, "连接绳索 10000000 被第一颗宝石开启");

            // 任务门禁宝石
            NativeInventoryService.AddItems(db, CharactersDao.FindById(db, cid), 2438411, 1, -1);
            r = NativeVMatrixService.Handle(db, Req(VMatrixOperation.UseGemstone, item: 2438411, count: 1));
            check(!r.ok && r.error == VMatrixCatalog.Gemstone(2438411).questAlert, "未完成 37871 的宝石拒绝并回源端提示");
            check(InventoryDao.CountOf(db, cid, 2438411) == 1, "被拒的宝石不消耗");

            // 强化：技能核心 0→30 = 193 点
            r = NativeVMatrixService.Handle(db, Req(VMatrixOperation.Upgrade, core: 10010001, target: 30));
            check(r.ok && r.points == 1794, "技能核心 0→30 扣 193 点");
            check(r.cores.Find(c => c.coreId == 10010001).level == 30, "等级写回 30");

            // 预览不落盘
            r = NativeVMatrixService.Handle(db, Req(VMatrixOperation.Upgrade, core: 20010000, target: 60, commit: false));
            check(r.ok && r.points == 1714, "预览返回扣费后点数（1794-80）");
            var still = NativeVMatrixService.Handle(db, new VMatrixRequest { characterId = cid, operation = VMatrixOperation.Snapshot });
            check(still.points == 1794, "预览不改状态");

            r = NativeVMatrixService.Handle(db, Req(VMatrixOperation.Upgrade, core: 20010000, target: 60));
            check(r.ok && r.points == 1714, "强化核心 0→60 扣 80 点");

            // 跨职业 / 越界
            r = NativeVMatrixService.Handle(db, Req(VMatrixOperation.Upgrade, core: 20020000, target: 1));
            check(!r.ok && r.error.Contains("职业"), "他职业核心拒绝强化");
            r = NativeVMatrixService.Handle(db, Req(VMatrixOperation.Upgrade, core: 10010001, target: 31));
            check(!r.ok && r.error.Contains("目标等级"), "超过 30 级拒绝");
            r = NativeVMatrixService.Handle(db, Req(VMatrixOperation.Upgrade, core: 10010001, target: 30));
            check(!r.ok, "目标等级不高于当前级拒绝");

            // 特殊核心：开 → 启用 → 互斥 → 停用
            r = NativeVMatrixService.Handle(db, Req(VMatrixOperation.Upgrade, core: 30000000, target: 1));
            check(r.ok && r.points == 1700, "特殊核心开启扣 registerSpecialCost 14");
            r = NativeVMatrixService.Handle(db, Req(VMatrixOperation.Upgrade, core: 30000000, target: 1));
            check(!r.ok && r.error.Contains("已经开启"), "特殊核心不能重复开启");
            r = NativeVMatrixService.Handle(db, Req(VMatrixOperation.SelectSpecial, core: 30000000));
            check(r.ok && r.cores.Find(c => c.coreId == 30000000).selected, "特殊核心启用");
            r = NativeVMatrixService.Handle(db, Req(VMatrixOperation.SelectSpecial, core: 30000000));
            check(!r.ok && r.error.Contains("已经启用"), "重复启用拒绝");

            r = NativeVMatrixService.Handle(db, Req(VMatrixOperation.Upgrade, core: 30000001, target: 1));
            check(r.ok && r.points == 1686, "第二个特殊核心开启扣 14");
            r = NativeVMatrixService.Handle(db, Req(VMatrixOperation.SelectSpecial, core: 30000001));
            check(r.ok && r.cores.Find(c => c.coreId == 30000001).selected &&
                  !r.cores.Find(c => c.coreId == 30000000).selected, "全局只允许一个特殊核心生效");
            r = NativeVMatrixService.Handle(db, Req(VMatrixOperation.UnselectSpecial, core: 30000001));
            check(r.ok && !r.cores.Find(c => c.coreId == 30000001).selected, "停用特殊核心");

            // 延期：本项目决议永久有效 → 明确拒绝，不白扣点
            r = NativeVMatrixService.Handle(db, Req(VMatrixOperation.ExtendSpecial, core: 30000000));
            check(!r.ok && r.error.Contains("永久有效"), "延期特殊核心直接拒绝（本项目永久有效）");
            check(NativeVMatrixService.Handle(db, new VMatrixRequest { characterId = cid, operation = VMatrixOperation.Snapshot }).points == 1686,
                "被拒的延期不扣点");

            // 上限：>= 9999 时禁止再吃宝石
            db.Table("vmatrix_state").Update(DbStore.Eq("characterid", (long)cid), new DbRow { ["points"] = 9999L });
            NativeInventoryService.AddItems(db, CharactersDao.FindById(db, cid), 2439869, 1, -1);
            r = NativeVMatrixService.Handle(db, Req(VMatrixOperation.UseGemstone, item: 2439869, count: 1));
            check(!r.ok && r.error.Contains("9999"), "满 9999 点拒绝再吃宝石");
            db.Table("vmatrix_state").Update(DbStore.Eq("characterid", (long)cid), new DbRow { ["points"] = 1686L });

            // 重置：扣 1000 万，退 1 级以上的点（特殊核心不退）
            var charRow = CharactersDao.FindById(db, cid);
            check(charRow.meso == 50000000, "重置前枫币 5000 万");
            r = NativeVMatrixService.Handle(db, Req(VMatrixOperation.Reset));
            check(r.ok && r.points == 1686 + 7 + 186 + 80, "重置退 273 点（7+186+80）");
            check(r.meso == 40000000, "重置扣 1000 万枫币");
            check(r.cores.Find(c => c.coreId == 10010001).level == 1, "技能组核心重置保底 1 级");
            check(r.cores.Find(c => c.coreId == 10000000) == null ||
                  r.cores.Find(c => c.coreId == 10000000).level == 0, "可自开共通核心归零");
            check(r.cores.Find(c => c.coreId == 30000000) != null &&
                  r.cores.Find(c => c.coreId == 30000000).level == 1, "特殊核心不参与重置");

            // 枫币不足
            db.Table("characters").Update(DbStore.Eq("id", (long)cid), new DbRow { ["meso"] = 100L });
            r = NativeVMatrixService.Handle(db, Req(VMatrixOperation.Reset));
            check(!r.ok && r.error.Contains("枫币"), "枫币不足拒绝重置");

            // 落盘往返
            db.Save();
            var reopened = DbStore.Open(path);
            var back = NativeVMatrixService.Handle(reopened, new VMatrixRequest { characterId = cid, operation = VMatrixOperation.Snapshot });
            check(back.ok && back.cores.Find(c => c.coreId == 10010001).level == 1, "重置结果落盘");
            check(back.cores.Find(c => c.coreId == 30000001).level == 1, "特殊核心等级落盘");

            check(!NativeVMatrixService.Handle(db, new VMatrixRequest { characterId = 999999, operation = VMatrixOperation.Snapshot }).ok,
                "不存在的角色拒绝");
            check(!NativeVMatrixService.Handle(db, null).ok, "空请求拒绝");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // —————————————————— 4. HEXA 目录表 ——————————————————

    static void checkHexaData(Action<bool, string> check)
    {
        HexaCatalog.Ensure();

        check(HexaCatalog.Core(20000000) != null && HexaCatalog.Core(20000000).maxLevel == 30,
            "HEXA 核心 20000000（狂暴攻击VI）最高 30 级");
        var g112 = HexaCatalog.Group(112);
        check(g112 != null && g112.mastery.Length == 4 && g112.enforce.Length == 4 &&
              g112.skill.Length == 2 && g112.common.Length == 3,
            "HEXA 职业组 112 = mastery 4 / enforce 4 / skill 2 / common 3（13 核心）");
        check(ReferenceEquals(HexaCatalog.Group(113), g112), "六转编码 113 回退到四转组 112");
        check(!HexaCatalog.Allowed(112, 20000003), "他职业 mastery 核心不放行");
        check(HexaCatalog.Allowed(112, 20000000), "本职业 mastery 核心放行");

        check(HexaCatalog.Stat(50000000) != null && HexaCatalog.Stat(50000000).reqLevel == 260 &&
              HexaCatalog.Stat(50000001).reqLevel == 265 && HexaCatalog.Stat(50000001).reqCore == 50000000,
            "属性核心三档 260/265/270 与前置链");
        check(HexaCatalog.Tiers.Count == 9, "索尔艾尔达 9 档");
        check(HexaCatalog.GaugeMax == 1000 && HexaCatalog.SlotMax == 20, "气息槽上限 1000 / 碎片槽 20");
        check(HexaCatalog.SolErdaExp(2637338) == 1000 && HexaCatalog.SolErdaExp(2636420) == 10 &&
              HexaCatalog.SolErdaExp(1111111) == 0, "道具 → 气息经验（未知道具 0）");

        check(HexaCatalog.UnlockLevel == 260 && HexaCatalog.UnlockQuest == 38625, "门禁 = 260 级 + 任务 38625");
        check(HexaCatalog.JobWhitelist.Length == 51, "白名单 51 项（38601 的 47 + job_TW 4）");
        check(HexaCatalog.JobAllowed(113) && HexaCatalog.JobAllowed(1113) && HexaCatalog.JobAllowed(17513),
            "六转编码 113/1113/17513 都在白名单");
        check(HexaCatalog.JobAllowed(112) && HexaCatalog.JobAllowed(1112),
            "四转编码 112/1112 借 +1 归到六转 113/1113（本项目没有 jobLevel，六转不回写 job）");
        check(!HexaCatalog.JobAllowed(0) && !HexaCatalog.JobAllowed(900) && !HexaCatalog.JobAllowed(500),
            "新手 / GM / 海盗不在白名单（六转 1 / 901 / 501 都不在表内）");
        check(NativeHexaMatrixService.FragmentItems.Length == 3 &&
              Array.IndexOf(NativeHexaMatrixService.FragmentItems, 4009548) >= 0 &&
              Array.IndexOf(NativeHexaMatrixService.FragmentItems, 4009547) >= 0 &&
              Array.IndexOf(NativeHexaMatrixService.FragmentItems, 4009550) >= 0,
            "碎片三类道具 4009548/4009547/4009550");
    }

    // —————————————————— 5. HEXA 7 个 type ——————————————————

    static void checkHexaService(Action<bool, string> check)
    {
        string path = Path.Combine(Path.GetTempPath(), "mapledash_hexa_" + Guid.NewGuid().ToString("N") + ".json");
        var db = DbStore.Create(path);
        try
        {
            int cid = (int)db.Table("characters").Insert(new DbRow
            {
                ["name"] = "HexaHero", ["job"] = 113L, ["level"] = 260L,
                ["meso"] = 100000000L, ["hp"] = 100L, ["maxhp"] = 100L,
                ["equipslots"] = 24L, ["useslots"] = 24L, ["setupslots"] = 24L, ["etcslots"] = 24L,
            });

            HexaRequest Req(HexaOperation op, int core = 0, int target = 0, bool commit = true)
                => new HexaRequest { characterId = cid, operation = op, coreId = core, targetLevel = target, commit = commit };

            // 门禁三件套
            db.Table("characters").Update(DbStore.Eq("id", (long)cid), new DbRow { ["level"] = 259L });
            var r = NativeHexaMatrixService.Handle(db, Req(HexaOperation.Activate, 20000000));
            check(!r.ok && r.error.Contains("260"), "259 级拒绝 HEXA");
            db.Table("characters").Update(DbStore.Eq("id", (long)cid), new DbRow { ["level"] = 260L });
            r = NativeHexaMatrixService.Handle(db, Req(HexaOperation.Activate, 20000000));
            check(!r.ok && r.error.Contains("6转") && r.error.Contains("HEXA"), "未完成 38625 拒绝（提示带任务名）");
            db.Table("characters").Update(DbStore.Eq("id", (long)cid), new DbRow { ["job"] = 112L });
            QuestStatusDao.Upsert(db, cid, 38625, 2, 1);
            r = NativeHexaMatrixService.Handle(db, new HexaRequest { characterId = cid, operation = HexaOperation.Snapshot });
            check(r.ok && r.eligible, "四转编码 112 同样准入（白名单 113 由其 +1 命中）");
            db.Table("characters").Update(DbStore.Eq("id", (long)cid), new DbRow { ["job"] = 900L });
            r = NativeHexaMatrixService.Handle(db, Req(HexaOperation.Activate, 20000000));
            check(!r.ok && r.error.Contains("职业"), "GM 编码 900 不在白名单，按职业拒绝");
            db.Table("characters").Update(DbStore.Eq("id", (long)cid), new DbRow { ["job"] = 113L });
            r = NativeHexaMatrixService.Handle(db, new HexaRequest { characterId = cid, operation = HexaOperation.Snapshot });
            check(r.ok && r.eligible && r.unlockLevel == 260 && r.unlockQuest == 38625, "260 级 + 38625 + 113 准入");
            check(r.cores.Count == 13, "快照下发 13 个职业核心");
            check(r.cores.TrueForAll(c => c.group >= 0 && c.group <= 3), "每核心带分组 0..3");

            // 前置校验必须先于扣费；V 核心等级投影也是实际技能的唯一权威。
            r=NativeHexaMatrixService.Handle(db,Req(HexaOperation.Activate,20000000));
            check(!r.ok && r.error.Contains("前置"),"缺少五转前置技能拒绝激活");
            db.Table("vmatrix_state").Insert(new DbRow { ["characterid"]=(long)cid });
            db.Table("vmatrix_cores").Insert(new DbRow { ["characterid"]=(long)cid,["coreid"]=20010000L,["level"]=40L });
            db.Table("vmatrix_cores").Insert(new DbRow { ["characterid"]=(long)cid,["coreid"]=10010001L,["level"]=25L });
            NativeMatrixSkills.Sync(db,cid);
            check(NativeMatrixSkills.Levels(db,cid).TryGetValue(400004008,out int vl) && vl==40,"五转强化核心同步实际技能40级");
            check(NativeMatrixSkills.Levels(db,cid).TryGetValue(400011002,out int sub) && sub==25,"五转关联子技能同步25级");

            // 气息与碎片不足
            r = NativeHexaMatrixService.Handle(db, Req(HexaOperation.Activate, 20000000));
            check(!r.ok && r.error.Contains("灵魂艾尔达不足"), "无气息无法激活");

            db.Table("hexa_state").Insert(new DbRow { ["characterid"] = (long)cid, ["solerda"] = 100L });
            r = NativeHexaMatrixService.Handle(db, Req(HexaOperation.Activate, 20000000));
            check(!r.ok && r.error.Contains("碎片不足"), "有气息无碎片仍拒绝");

            NativeInventoryService.AddItems(db, CharactersDao.FindById(db, cid), 4009548, 200, -1);
            r = NativeHexaMatrixService.Handle(db, Req(HexaOperation.Activate, 20000000));
            check(r.ok && r.solErda == 97, "mastery 激活扣 3 气息");
            check(r.fragments == 150, "mastery 激活扣 50 碎片");
            check(r.cores.Find(c => c.coreId == 20000000).level == 1 &&
                  r.cores.Find(c => c.coreId == 20000000).activated, "核心激活到 1 级");

            r = NativeHexaMatrixService.Handle(db, Req(HexaOperation.Activate, 20000000));
            check(!r.ok && r.error.Contains("已激活"), "重复激活拒绝");

            // 职业组外
            var outside = HexaCatalog.Group(212).mastery[0];
            r = NativeHexaMatrixService.Handle(db, Req(HexaOperation.Activate, outside));
            check(!r.ok && r.error.Contains("职业"), "他职业核心拒绝激活");

            // 强化：按「节点类型 + 目标等级」累加
            r = NativeHexaMatrixService.Handle(db, Req(HexaOperation.Upgrade, 20000000, 3));
            check(r.ok && r.solErda == 95, "mastery 1→3 扣 2 气息（1+1）");
            check(r.fragments == 117, "mastery 1→3 扣 33 碎片（15+18）");
            r = NativeHexaMatrixService.Handle(db, Req(HexaOperation.Upgrade, 20000000, 3));
            check(!r.ok && r.error.Contains("目标等级"), "目标低于当前级拒绝");
            r = NativeHexaMatrixService.Handle(db, Req(HexaOperation.Upgrade, 20000000, 31));
            check(!r.ok && r.error.Contains("最高等级"), "超过 30 级提示已满");
            r = NativeHexaMatrixService.Handle(db, Req(HexaOperation.Upgrade, 20000050, 2));
            check(!r.ok && r.error.Contains("尚未激活"), "未激活不能强化");

            // 预览
            r = NativeHexaMatrixService.Handle(db, Req(HexaOperation.Upgrade, 20000000, 4, false));
            check(r.ok && r.nextSolErda == 1 && r.nextFragments == 20, "强化预览给出下一级费用");
            check(NativeHexaMatrixService.Handle(db, new HexaRequest { characterId = cid, operation = HexaOperation.Snapshot }).cores
                .Find(c => c.coreId == 20000000).level == 3, "预览不改等级");

            // enforce 组：激活 4 气息 + 75 碎片
            r = NativeHexaMatrixService.Handle(db, Req(HexaOperation.Activate, 30000000));
            check(r.ok && r.solErda == 91 && r.fragments == 42, "enforce 激活扣 4 气息 / 75 碎片");

            // 属性核心
            var set = new HexaRequest
            { characterId = cid, operation = HexaOperation.SetStat, coreId = 50000001, stat1 = 1, stat2 = 2, stat3 = 3, commit = true };
            r = NativeHexaMatrixService.Handle(db, set);
            check(!r.ok && r.error.Contains("265"), "265 级才能启用属性核心Ⅱ");
            set.coreId = 50000000;
            r = NativeHexaMatrixService.Handle(db, set);
            check(r.ok && StatOf(r, 50000000, 0).stat1 == 1,
                "属性核心Ⅰ写入三条能力值");

            var enforce = new HexaRequest
            { characterId = cid, operation = HexaOperation.EnforceStat, coreId = 50000000, commit = true };
            r = NativeHexaMatrixService.Handle(db, enforce);
            check(r.ok && StatOf(r, 50000000).level == 1, "属性核心强化到 1 级");
            check(r.fragments == 22, "属性核心启用与强化各扣10碎片");
            check(StatOf(r,50000000).level1+StatOf(r,50000000).level2+StatOf(r,50000000).level3==1,"强化恰好提升一条等级");

            r = NativeHexaMatrixService.Handle(db, new HexaRequest
            { characterId = cid, operation = HexaOperation.Preset, coreId = 50000000, preset = 0, commit = true });
            check(r.ok && StatOf(r, 50000000, 0).applied, "预设 0 标记为生效");
            r = NativeHexaMatrixService.Handle(db, new HexaRequest
            { characterId = cid, operation = HexaOperation.Preset, coreId = 50000000, preset = 5, commit = true });
            check(!r.ok, "非法预设号拒绝");
            r = NativeHexaMatrixService.Handle(db, new HexaRequest
            { characterId = cid, operation = HexaOperation.Rename, coreId = 50000000, name = "输出", commit = true });
            check(!r.ok, "原端二级密码操作不误当成核心命名");
            db.Table("hexa_stats").Update(row=>DbStore.Int(row,"characterid")==cid,new DbRow {["level"]=10L,["level1"]=4L,["level2"]=3L,["level3"]=3L});
            r = NativeHexaMatrixService.Handle(db, new HexaRequest
            { characterId = cid, operation = HexaOperation.Reset, coreId = 50000000, commit = true });
            check(r.ok && StatOf(r, 50000000).level == 0, "属性核心重置归零");
            r = NativeHexaMatrixService.Handle(db, new HexaRequest
            { characterId = cid, operation = HexaOperation.Reset, coreId = 50000000, commit = true });
            check(!r.ok && r.error.Contains("初始"), "已是初始状态拒绝重复重置");

            // 无效类型/预设不得收费；保存必须完整复制三条等级。
            long mesoBefore=CharactersDao.FindById(db,cid).meso;
            r=NativeHexaMatrixService.Handle(db,new HexaRequest {characterId=cid,operation=HexaOperation.ChangeStat,coreId=50000000,stat1=4,stat2=5,stat3=6,commit=true});
            check(!r.ok && CharactersDao.FindById(db,cid).meso==mesoBefore,"物理/魔法攻击互斥，拒绝且不收费");
            r=NativeHexaMatrixService.Handle(db,new HexaRequest {characterId=cid,operation=HexaOperation.ChangeStat,coreId=50000000,stat1=0,stat2=4,stat3=6,commit=true});
            check(r.ok && CharactersDao.FindById(db,cid).meso==mesoBefore-1000000,"能力值变更由服务端固定收100万");
            db.Table("hexa_stats").Update(row=>DbStore.Int(row,"characterid")==cid,new DbRow {["level"]=20L,["level1"]=8L,["level2"]=7L,["level3"]=5L});
            r=NativeHexaMatrixService.Handle(db,Req(HexaOperation.SaveStat,50000000));
            var saved=StatOf(r,50000000,1);
            check(r.ok && saved.level==20 && saved.level1==8 && saved.level2==7 && saved.level3==5 && saved.stat1==0,"保存包含三条类型和等级而非仅复制类型");
            r=NativeHexaMatrixService.Handle(db,new HexaRequest {characterId=cid,operation=HexaOperation.Preset,coreId=50000000,preset=1,commit=true});
            check(r.ok && StatOf(r,50000000,1).applied && !StatOf(r,50000000,0).applied,"套用保存核心只生效一个预设");
            r=NativeHexaMatrixService.Handle(db,new HexaRequest {characterId=cid,operation=HexaOperation.EnforceStat,coreId=50000000,preset=1,commit=true});
            check(!r.ok,"保存核心不能直接强化");
            mesoBefore=CharactersDao.FindById(db,cid).meso;
            db.Table("hexa_stats").Update(row=>DbStore.Int(row,"characterid")==cid && DbStore.Int(row,"preset")==0,new DbRow {["level"]=12L,["level1"]=0L,["level2"]=0L,["level3"]=0L});
            r=NativeHexaMatrixService.Handle(db,Req(HexaOperation.EnforceStat,50000000));
            check(!r.ok && r.error.Contains("旧版"),"旧总等级不伪造历史分配");
            r=NativeHexaMatrixService.Handle(db,Req(HexaOperation.Reset,50000000));
            check(r.ok && StatOf(r,50000000).level==0 && CharactersDao.FindById(db,cid).meso==mesoBefore,"旧核心免费初始化且保留保存核心");
            var rollView=new HexaStatView(); var rng=new Random(731);
            for(int n=0;n<20;n++) {int pick=NativeHexaMatrixService.RollStat(rollView,rng);if(pick==0)rollView.level1++;else if(pick==1)rollView.level2++;else rollView.level3++;}
            check(rollView.level1+rollView.level2+rollView.level3==20 && rollView.level1<=10 && rollView.level2<=10 && rollView.level3<=10,"20次强化总等级20且单条不超过10");
            check(HexaCatalog.StatValue(0,1,true)==35 && HexaCatalog.StatValue(3,1,true)==75,"IMG保留0.35%暴伤及0.75%伤害精度");
            check(HexaCatalog.StatValue(4,10,true)==100 && HexaCatalog.StatValue(6,10,true)==20,"IMG主线攻击/主属性权重按表读取");
            NativeMatrixSkills.Sync(db,cid);
            var actual=SkillsDao.ByCharacter(db,cid);
            int hexaSkill=HexaCatalog.Core(20000000).skills[0];
            check(actual.Exists(row=>DbStore.Int(row,"skillid")==hexaSkill && DbStore.Int(row,"skilllevel")==3),"HEXA升级同步实际技能3级");

            var full=new FullCharacter();
            full.hexaStats.Add(new HexaStatView { coreId=50000000,applied=true,level=3,level1=1,level2=1,level3=1,stat1=0,stat2=3,stat3=6 });
            var delta=NativeGrowthStats.Accumulate(full,260,113);
            check(Math.Abs(delta.criticalBonus-.0035f)<.000001 && delta.hexaDamagePercent>0,"实际属性结算保留小数百分比");
            check(delta.Get("STR")==30,"主属性权重1实际增加30力量");
            delta=NativeGrowthStats.Accumulate(full,260,3613);
            check(delta.Get("STR")==40 && delta.Get("DEX")==40 && delta.Get("LUK")==40,"杰诺三属性按批准倍率实际生效");
            delta=NativeGrowthStats.Accumulate(full,260,3123);
            check(delta.Get("HP")==630,"恶魔复仇者按批准倍率加生命值");
            full.hexaStats[0].applied=false;
            check(NativeGrowthStats.Accumulate(full,260,3123).Get("HP")==0,"取消套用即移除属性，不受缓存污染");
            check(HexaCatalog.MainStat(17513)=="DEX" && HexaCatalog.MainStat(17213)=="INT" && HexaCatalog.MainStat(15413)=="LUK","特殊职业主属性映射按源职业枚举");

            var loaded=CharService.Load(db,cid);
            check(loaded.hexaStats.Exists(v=>v.preset==1 && v.level1==8 && v.applied),"角色读档含保存核心完整等级与套用状态");
            loaded.skills.Find(v=>v.skillId==hexaSkill).skillLevel=1;
            CharService.Save(db,loaded);
            check(SkillsDao.ByCharacter(db,cid).Exists(row=>DbStore.Int(row,"skillid")==hexaSkill && DbStore.Int(row,"skilllevel")==3),"旧角色技能快照不会覆盖权威核心等级");

            // 索尔艾尔达兑换
            var conv = NativeHexaMatrixService.Convert(db, cid, NativeHexaMatrixService.FaintSolErda, 2);
            check(conv.ok && conv.solErda == 84, "兑换扣 2 气息");
            check(InventoryDao.CountOf(db, cid, NativeHexaMatrixService.FaintSolErda) == 60, "1 气息 → 30 淡灵魂艾尔达");
            conv = NativeHexaMatrixService.Convert(db, cid, NativeHexaMatrixService.HexaBooster, 1);
            check(conv.ok && conv.solErda == 83 && InventoryDao.CountOf(db, cid, NativeHexaMatrixService.HexaBooster) == 1,
                "1 气息 → 1 HEXA Booster");
            conv = NativeHexaMatrixService.Convert(db, cid, 2439869, 1);
            check(!conv.ok && conv.error.Contains("没有这种兑换"), "非法道具拒绝兑换");
            conv = NativeHexaMatrixService.Convert(db, cid, NativeHexaMatrixService.FaintSolErda, 9999);
            check(!conv.ok && conv.error.Contains("不足"), "气息不足拒绝兑换");

            // 落盘往返
            db.Save();
            var reopened = DbStore.Open(path);
            var back = NativeHexaMatrixService.Handle(reopened, new HexaRequest { characterId = cid, operation = HexaOperation.Snapshot });
            check(back.ok && back.cores.Find(c => c.coreId == 20000000).level == 3 &&
                  back.cores.Find(c => c.coreId == 30000000).level == 1, "核心等级落盘");
            check(back.stats.Exists(s => s.coreId == 50000000 && s.stat2 == 4), "属性核心三条能力值落盘");
            check(back.solErda == 83, "气息余额落盘");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
