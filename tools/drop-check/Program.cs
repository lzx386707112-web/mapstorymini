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

        // 4) 二转测试图（108000200 法师线）逐只怪统计：27 只怪里应该两种都掉道具
        Console.WriteLine("[DropCheck] 掉落表 " + mobs.Count + " 只怪 / " + globals.Count + " 条全局");
        Console.WriteLine("[DropCheck] 9000001 打 " + trials + " 只：4031013 " + hits + " 个（期望 " +
                          expected.ToString("F0") + "），金币 " + meso + " 次");
        Console.WriteLine("[DropCheck] 通过 " + assertions + " 项断言");
        return 0;
    }
}
