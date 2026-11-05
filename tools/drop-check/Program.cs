// 掉落表检查（不依赖 Unity 编辑器，编辑器开着主工程也能跑）。
//
// 背景：二转测试图（108000100/200/300）里的 9000001/9000002… 曾经一行掉落数据都没有
//（运行期只带 FieldDrops.json 的 9 只怪样本），所以任务道具 4031013「黑色大理石」
// 永远不掉。这里引 tools/unity-compile-check 编出来的运行时程序集，跑真实链路：
//   FieldDropTable.txt → DropTableText.TryParse → DropStore → FieldLoot.Roll
//
// 用法（先保证 DLL 是新的）：
//   dotnet build ../unity-compile-check/Check.csproj --no-restore
//   cd tools/drop-check && dotnet run
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public static class DropCheck
{
    static int assertions;

    static void Check(bool ok, string label)
    {
        assertions++;
        if (!ok) throw new Exception("[DropCheck] " + label);
    }

    static string Find(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    public static int Main()
    {
        var path = Find("Assets/Resources/Field/FieldDropTable.txt");
        Check(path != null, "找不到 Assets/Resources/Field/FieldDropTable.txt");

        Check(DropTableText.TryParse(File.ReadAllText(path), out var mobs, out var globals), "掉落表解析失败");
        Check(mobs.Count > 1000, "掉落表只有 " + mobs.Count + " 只怪，源端的 drop_data 应该覆盖上千只怪物");
        Check(globals.Count > 0, "全局掉落为空");

        DropStore.DbProvider = () => null;
        DropStore.JsonDrops = () => mobs;
        DropStore.JsonGlobalDrops = () => globals;
        DropStore.Invalidate();

        // 282 gemstone: ordinary 2435719 is not a mob/global drop. The regional
        // 2435902 roll is 5/10000; Dreamkeeper additionally has its static 50/10000 roll.
        foreach (var pair in DropStore.All())
            Check(!pair.Value.Exists(e => e.item == 2435719), "旧核心宝石掉落仍存在: " + pair.Key);
        Check(!DropStore.Global().Exists(e => e.item == 2435719), "旧核心宝石全局掉落仍存在");
        Check(DropStore.ForMob(8641000).Find(e => e.item == 2435902)?.chance == 500, "282 区域宝石概率不符");
        var dream = DropStore.ForMob(8643012).FindAll(e => e.item == 2435902);
        Check(dream.Count == 2 && dream.Exists(e => e.chance == 500) && dream.Exists(e => e.chance == 5000), "梦境怪物两次独立抽取被合并");
        Check(!DropStore.ForMob(8641010).Exists(e => e.item == 2435902), "范围外怪物被错误追加宝石");

        // 1) 二转三个测试图的怪：都必须掉 4031013（questid=0 → NeedQuestItem 不拦）
        foreach (int mob in new[] { 9000001, 9000002, 9000100, 9000101, 9000200, 9000201, 9000300, 9000301 })
        {
            var rows = DropStore.ForMob(mob);
            var row = rows.Find(r => r.item == 4031013);
            Check(row != null, $"二转测试怪 {mob} 没有 4031013 掉落行");
            Check(row.quest == 0, $"二转测试怪 {mob} 的 4031013 被 questid={row.quest} 限制");
            Check(row.chance == 700000, $"二转测试怪 {mob} 的 4031013 chance={row.chance}，源端是 700000");
        }

        // 2) 实跑 2 万次击杀，确认 4031013 的掉落率贴住 chance/999999（4σ 内）
        const int trials = 20000;
        var rng = new Random(20261101);
        int hits = 0, meso = 0;
        double expected = 0;
        for (int i = 0; i < trials; i++)
            FieldLoot.Roll(9000001, (itemId, qty, d) =>
            {
                if (itemId == 4031013) hits++;
                if (itemId == 0) meso++;
            }, rng, 1.0, (quest, item) => quest == 0);

        var target = DropStore.ForMob(9000001).Find(r => r.item == 4031013);
        expected = (double)target.chance / DropStore.ChanceDenominator * trials;
        double sd = Math.Sqrt(expected * (1 - (double)target.chance / DropStore.ChanceDenominator));
        Check(Math.Abs(hits - expected) <= 4 * sd + 3,
            $"4031013 实测 {hits} 次 / 期望 {expected:F0} 次，超出 4σ");
        // 二转测试怪只掉大理石（源端同一行数据里没有金币/经验，NPC 也说了"不给经验不掉道具除外的东西"）
        var all = DropStore.ForMob(9000001);
        Check(all.Count == 1, "9000001 除了 4031013 还掉了别的东西：" + all.Count + " 行");
        Check(meso == 0, "9000001 掉了金币，源端测试怪没有金币行");

        // 3) 普通怪也要有掉落（以前整张表只有 9 只怪）
        foreach (int mob in new[] { 100100, 1210102, 1110100, 2230101 })
        {
            var rows = DropStore.ForMob(mob);
            Check(rows.Count > 0, $"普通怪 {mob} 没有任何掉落行");
        }

        // 3b) 机器人玩法掉落（content/drop-overrides.tsv 叠在源端基线之上）：
        // 钢铁碎片是任务 900003/900004 的收集材料，銀之心是低档心臟的打怪兜底。
        foreach (int mob in new[] { 9300133, 9300060, 4230111, 4230112, 9400546, 4230400, 8120102, 8120103, 9400545 })
        {
            var parts = DropStore.ForMob(mob).Find(r => r.item == 4000357);
            Check(parts != null && parts.chance > 0, $"机械怪 {mob} 没有钢铁碎片 4000357 掉落");
            var heart = DropStore.ForMob(mob).Find(r => r.item == 1672000);
            Check(heart != null && heart.chance > 0, $"机械怪 {mob} 没有銀之心 1672000 掉落");
        }
        // 机器人本体不能直接掉：282 的史烏/戴米安掉落源在本项目没有美术，
        // 改成起源之塔兑换；万一有人往基线里加了掉落行，这里会拦下来。
        foreach (int mob in new[] { 8950110, 8880111 })
        {
            var body = DropStore.ForMob(mob).Find(r => r.item == 1662053 || r.item == 1662088);
            Check(body == null, $"机器人本体不该出现在 {mob} 的掉落里（改走兑换）");
        }

        // 3c) 282 成长材料掉落（content/drop-overrides.tsv「282 成长材料获取途径」段）：
        // 照 TMS282 mob_drops.json 官方语义移植 —— 星火/強化型靈魂附魔只挂塞伦，
        // 補給型靈魂附魔挂普通怪，主题灵魂石挂对应 BOSS。动了覆盖层这里会拦。
        Check(DropStore.ForMob(8880342).Find(r => r.item == 2048716)?.chance == 999999, "神选者塞伦 8880342 必须掉強力星火 2048716（100%）");
        Check(DropStore.ForMob(8880342).Find(r => r.item == 2048717)?.chance == 999999, "神选者塞伦 8880342 必须掉永遠星火 2048717（100%）");
        Check(DropStore.ForMob(8880342).Find(r => r.item == 2048753)?.chance == 999999, "神选者塞伦 8880342 必须掉黑色星火 2048753（100%）");
        Check(DropStore.ForMob(8880342).Find(r => r.item == 2590006)?.chance == 999999, "神选者塞伦 8880342 必须掉強化型靈魂附魔 2590006（100%）");
        Check(DropStore.ForMob(8880344).Find(r => r.item == 2048716)?.chance == 999999, "神选者塞伦 8880344 必须掉強力星火 2048716（100%）");
        Check(DropStore.ForMob(8880344).Find(r => r.item == 2590006)?.chance == 999999, "神选者塞伦 8880344 必须掉強化型靈魂附魔 2590006（100%）");
        // 補給型靈魂附魔：普通怪池 0.1%/0.01% 两档（282 语义 1000/100）。
        var supplyTop = DropStore.ForMob(9400606).Find(r => r.item == 2590004);
        Check(supplyTop?.chance == 1000, "巨型蛋糕 9400606 应该以 0.1%（chance=1000）掉補給型靈魂附魔 2590004");
        var supplyRest = DropStore.ForMob(8160000).Find(r => r.item == 2590004);
        Check(supplyRest?.chance == 100, "时间门神 8160000 应该以 0.01%（chance=100）掉補給型靈魂附魔 2590004");
        // 主题灵魂石 2%：BOSS → 石头一一对应（282 官方语境：BOSS 掉自己的灵魂石）。
        foreach (var (mob, stone) in new[] { (8800002, 2591161), (8820000, 2591061), (8830007, 2591045),
                     (8840000, 2591071), (8850012, 2591081), (8860000, 2591200), (8870000, 2591231),
                     (8880000, 2591262), (8880141, 2591587), (8880400, 2591657) })
            Check(DropStore.ForMob(mob).Find(r => r.item == stone)?.chance == 19999,
                $"BOSS {mob} 必须以 2% 掉主题灵魂石 {stone}");

        // 4) 二转测试图（108000200 法师线）逐只怪统计：27 只怪里应该两种都掉道具
        Console.WriteLine("[DropCheck] 掉落表 " + mobs.Count + " 只怪 / " + globals.Count + " 条全局");
        Console.WriteLine("[DropCheck] 9000001 打 " + trials + " 只：4031013 " + hits + " 个（期望 " +
                          expected.ToString("F0") + "），金币 " + meso + " 次");
        // 5) 后台「怪物爆率 / 全局爆率」（/drop/v1/*）：列表必须读「基线 + 覆盖」合并视图
        CheckAdminDrops(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path), "..", "..", "..")), mobs, globals);

        Console.WriteLine("[DropCheck] 通过 " + assertions + " 项断言");
        return 0;
    }

    // 后台掉落接口回归。
    //
    // 真机存档的 drop_data / drop_data_global 是 0 行 —— 全量掉落落在只读基线里，DB 只存
    // 后台改过的覆盖行。所以列表接口必须读 DropStore 的合并视图，只 SELECT 表的话两个
    // 爆率页永远空表（2026-11-04 用户报障）。写侧按覆盖层语义落库：改基线行 = 写覆盖行；
    // 删基线行 = 写 chance=0 墓碑（真删会回退基线，怪照样掉）；删纯 DB 行才真删。
    static void CheckAdminDrops(string repoRoot, Dictionary<int, List<DropEntry>> baseMobs, List<DropEntry> baseGlobal)
    {
        string dbPath = Path.Combine(Path.GetTempPath(), "maple-drop-check-" + Guid.NewGuid().ToString("N") + ".db");
        // DbStore 的 Schema 走 PlatformRes：先在 Unity Resources 里找，再按
        // AppContext.BaseDirectory/Server/Db/Schema[.sql] 找。工程目录里的 Schema.sql
        // 不在这两个位置，直接复制一份过去，省得依赖各机器上 csproj 的 CopyToOutputDirectory。
        string binSchema = Path.Combine(AppContext.BaseDirectory, "Server/Db/Schema.sql");
        Directory.CreateDirectory(Path.GetDirectoryName(binSchema));
        File.Copy(Path.Combine(repoRoot, "Assets/Scripts/Server/Db/Schema.sql"), binSchema, true);

        var db = DbStore.Open(dbPath);
        AdminBackend.DbOverride = db;
        DropStore.DbProvider = () => db;
        DropStore.Invalidate();

        var legacy = db.Table("drop_data");
        legacy.Insert(new DbRow { ["dropperid"] = 8641000L, ["itemid"] = 2435719L,
            ["minimum_quantity"] = 1L, ["maximum_quantity"] = 1L, ["questid"] = 0L, ["chance"] = 100000L });
        Check(!DropStore.ForMob(8641000).Exists(e => e.item == 2435719), "旧存档覆盖重新引入了自定义宝石掉落");
        legacy.rows.Clear();
        DropStore.Invalidate();

        var api = new AdminApi();
        AdminDrops.Register(api);
        string token = api.CreateSession(new AdminAccount { Id = 1, Name = "webadmin", WebAdmin = true });

        string Call(string method, string path, string data)
        {
            var req = new AdminHttpRequest { Method = method };
            if (data != null) req.BodyText = "{\"requestId\":\"probe\",\"data\":" + data + "}";
            req.Headers["Authorization"] = "Bearer " + token;
            int q = path.IndexOf('?');
            req.Path = q < 0 ? path : path.Substring(0, q);
            if (q >= 0)
                foreach (var pair in path.Substring(q + 1).Split('&'))
                {
                    int eq = pair.IndexOf('=');
                    if (eq > 0) req.Query[pair.Substring(0, eq)] = Uri.UnescapeDataString(pair.Substring(eq + 1));
                }
            return Encoding.UTF8.GetString(api.Handle(req).Body ?? Array.Empty<byte>());
        }

        // 从列表 JSON 取出某道具那一行的 id（字段顺序固定：id, dbId, source, dropperId, …）
        string RowId(string json, int itemId)
        {
            int at = json.IndexOf("\"itemId\":" + itemId, StringComparison.Ordinal);
            if (at < 0) return "0";
            int idAt = json.LastIndexOf("\"id\":", at, StringComparison.Ordinal);
            int comma = json.IndexOf(',', idAt);
            return json.Substring(idAt + 5, comma - idAt - 5);
        }

        // ① 列表 = 基线（不带过滤）
        var all = Call("POST", "/drop/v1/getDropList", "{\"pageNo\":1,\"pageSize\":5}");
        Check(all.Contains("\"records\"") && all.Contains("\"totalRow\":"), "后台掉落列表分页形状不对: " + all);
        Check(all.Contains("\"source\":\"baseline\""), "后台掉落列表没有列出源端基线: " + all);

        var mob54 = Call("POST", "/drop/v1/getDropList", "{\"dropperId\":54,\"pageNo\":1,\"pageSize\":50}");
        Check(mob54.Contains("\"itemId\":4000900"), "后台查不到 54 号怪的基线掉落 4000900: " + mob54);
        Check(mob54.Contains("\"chance\":600000") && mob54.Contains("\"dbId\":0"),
            "基线行的 chance/dbId 不对: " + mob54);
        Check(RowId(mob54, 4000900).StartsWith("-"), "基线行应该用负数合成行 id: " + mob54);

        var glob = Call("POST", "/drop/v1/getGlobalDropList", "{\"pageNo\":1,\"pageSize\":50}");
        Check(glob.Contains("4031865"), "全局爆率没列出源端基线: " + glob);
        Check(glob.Contains("\"totalRow\":" + baseGlobal.Count), "全局基线行数不对: " + glob);

        // ② 改基线行 → 只落一条覆盖行，游戏内立即生效
        Call("PUT", "/drop/v1/addDropData",
            "{\"id\":-1,\"dropperId\":54,\"itemId\":4000900,\"minimumQuantity\":1,\"maximumQuantity\":1,\"questId\":0,\"chance\":12345}");
        Check(db.Table("drop_data").rows.Count == 1, "改一条基线行应只落一条覆盖行，实际 " + db.Table("drop_data").rows.Count);
        DropStore.Invalidate();
        var eff = DropStore.ForMob(54).Find(r => r.item == 4000900);
        Check(eff != null && eff.chance == 12345, "覆盖行没有立刻生效: " + (eff == null ? "null" : eff.chance.ToString()));

        var after = Call("POST", "/drop/v1/getDropList", "{\"dropperId\":54,\"pageNo\":1,\"pageSize\":50}");
        Check(after.Contains("\"source\":\"override\""), "列表没标出覆盖行: " + after);

        // ③ 删基线行 → chance=0 墓碑
        Call("DELETE", "/drop/v1/deleteDropData/" + RowId(after, 4000900) + "?dropperId=54&itemId=4000900", null);
        DropStore.Invalidate();
        Check(DropStore.ForMob(54).Find(r => r.item == 4000900) == null, "删了基线掉落，游戏内还在掉");
        Check(db.Table("drop_data").rows.Count == 1 &&
              DbStore.Long(db.Table("drop_data").rows[0], "chance") == 0, "删基线掉落应留 chance=0 墓碑");

        // ④ 再保存一次 → 覆盖墓碑，恢复源端概率
        Call("POST", "/drop/v1/updateDropData",
            "{\"dropperId\":54,\"itemId\":4000900,\"minimumQuantity\":1,\"maximumQuantity\":1,\"questId\":0,\"chance\":600000}");
        DropStore.Invalidate();
        var back = DropStore.ForMob(54).Find(r => r.item == 4000900);
        Check(back != null && back.chance == 600000, "覆盖墓碑后没有恢复源端概率");

        // ⑤ 新增的全局掉落不能冲掉源端基线（旧实现是「DB 有任意行就整表替换」）
        int globBefore = DropStore.Global().Count;
        Call("PUT", "/drop/v1/addGlobalDropData",
            "{\"continent\":-1,\"itemId\":4000999,\"minimumQuantity\":1,\"maximumQuantity\":1,\"questId\":0,\"chance\":5000}");
        DropStore.Invalidate();
        Check(DropStore.Global().Count == globBefore + 1,
            "新增全局掉落冲掉了基线: " + DropStore.Global().Count + " / 基线 " + globBefore);

        // ⑥ 删自己新增的（基线没有这道具）→ 真删
        Call("PUT", "/drop/v1/addDropData",
            "{\"dropperId\":54,\"itemId\":4031865,\"minimumQuantity\":1,\"maximumQuantity\":1,\"questId\":0,\"chance\":1000}");
        DropStore.Invalidate();
        Check(DropStore.ForMob(54).Exists(r => r.item == 4031865), "后台新增的掉落没生效");
        var withNew = Call("POST", "/drop/v1/getDropList", "{\"dropperId\":54,\"pageNo\":1,\"pageSize\":50}");
        Call("DELETE", "/drop/v1/deleteDropData/" + RowId(withNew, 4031865) + "?dropperId=54&itemId=4031865", null);
        DropStore.Invalidate();
        Check(!DropStore.ForMob(54).Exists(r => r.item == 4031865), "纯 DB 新增行没有被真删");
        Check(db.Table("drop_data").rows.Count == 1, "删完纯 DB 行后覆盖层应只剩 54/4000900");

        // ⑦ 过滤条件
        var byItem = Call("POST", "/drop/v1/getDropList", "{\"itemId\":4000900,\"pageNo\":1,\"pageSize\":5}");
        Check(byItem.Contains("\"itemId\":4000900") && !byItem.Contains("\"itemId\":4000901"), "按道具过滤不对: " + byItem);
        var byQuest = Call("POST", "/drop/v1/getDropList", "{\"questId\":0,\"pageNo\":1,\"pageSize\":5}");
        Check(byQuest.Contains("\"questId\":0"), "按任务过滤不对: " + byQuest);

        AdminBackend.DbOverride = null;
        DropStore.DbProvider = () => null;
        DropStore.Invalidate();
    }
}
