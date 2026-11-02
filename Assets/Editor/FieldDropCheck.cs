using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 掉落统计自检（不需要进 Play）：
//   Unity -batchmode -projectPath <proj> -executeMethod FieldDropCheck.Run -logFile /tmp/drop_checks.log
//
// 目的：用大样本证明「掉落概率、金币区间、物品数量区间」与 drop_data SQL 的语义一致，
// 而不是靠打几只怪肉眼判断。判定标准：
//   1. 每条掉落行的实测命中率与 chance/999999 的偏差必须在 4σ 内（σ = sqrt(p(1-p)/n)）
//   2. 金币必须落在 [Min, Max-1]，物品数量必须落在 [Min, Max]
//   3. 二转测试图的黑色大理石（4031013）必须在掉落表里（这条曾经整张表只有 9 只怪，
//      二转地图什么也不掉）
//   4. 全局掉落不得生成金币（原服 dropGlobalItems 对 itemId=0 直接跳过）
public static class FieldDropCheck
{
    const int Trials = 50000;
    // 全量掉落表现在有两万多行，逐只怪跑 5 万次会跑到天荒地老，按固定步长抽样。
    const int SampleMobs = 24;

    public static void Run()
    {
        FieldCatalog.Ensure();
        if (!FieldCatalog.Ready) { Debug.LogError("[DropCheck] FieldCatalog 未加载"); EditorApplication.Exit(1); return; }

        int pass = 0, fail = 0;
        var hits = new Dictionary<int, int>();     // 物品 id → 命中次数（便于汇总打印）
        var rng = new System.Random(20251021);

        // 关键任务道具先断言：二转测试图（108000100/200/300）的怪必须掉 4031013
        foreach (int testMob in new[] { 9000001, 9000002, 9000100, 9000200, 9000300 })
        {
            var rows0 = FieldCatalog.Drops.TryGetValue(testMob, out var r0) ? r0 : null;
            bool found = rows0 != null && rows0.Exists(e => e.item == 4031013 && e.chance > 0);
            if (found) pass++;
            else { fail++; Debug.LogError("[DropCheck] 二转测试怪 " + testMob + " 没有 4031013 掉落行"); }
        }

        foreach (var kv in SampleForCheck())
        {
            int mobId = kv.Key;
            var rows = kv.Value;

            // 每条 row 的实测命中数
            var rowHits = new int[rows.Count];
            var mesoMin = new int[rows.Count];
            var mesoMax = new int[rows.Count];
            var qtyMin = new int[rows.Count];
            var qtyMax = new int[rows.Count];
            for (int i = 0; i < rows.Count; i++)
            {
                mesoMin[i] = int.MaxValue; mesoMax[i] = int.MinValue;
                qtyMin[i] = int.MaxValue; qtyMax[i] = int.MinValue;
            }

            // 影子表：用 (itemId, min, max) 反查行号。游戏中同 (mob,item) 只有首条生效，
            // 用 itemId 定位足够，因为导出时已按 INSERT IGNORE 语义去重。
            var rowOf = new Dictionary<int, int>();
            for (int i = 0; i < rows.Count; i++) if (!rowOf.ContainsKey(rows[i].item)) rowOf[rows[i].item] = i;

            for (int t = 0; t < Trials; t++)
            {
                FieldLoot.Roll(mobId, (itemId, qty, d) =>
                {
                    int idx;
                    if (!rowOf.TryGetValue(itemId, out idx)) idx = -1;   // 来自全局掉落
                    if (idx >= 0)
                    {
                        var row = rows[idx];
                        rowHits[idx]++;
                        if (itemId == 0) { mesoMin[idx] = Mathf.Min(mesoMin[idx], qty); mesoMax[idx] = Mathf.Max(mesoMax[idx], qty); }
                        else { qtyMin[idx] = Mathf.Min(qtyMin[idx], qty); qtyMax[idx] = Mathf.Max(qtyMax[idx], qty); }
                    }
                    int seen;
                    hits.TryGetValue(itemId, out seen);
                    hits[itemId] = seen + 1;
                }, rng);
            }

            string name = FieldCatalog.Mobs.ContainsKey(mobId) ? FieldCatalog.Mobs[mobId].name : "?";
            double totalExpected = 0, totalActual = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.chance == 0) continue;      // 永不掉落，不参与统计

                double p = (double)row.chance / FieldCatalog.ChanceDenominator;
                // 同一个 itemId 也可能出现在全局掉落里（例如 4001006），实测命中是两者之和
                double globalP = 0;
                foreach (var g in FieldCatalog.GlobalDrops) if (g.item == row.item) globalP += (double)g.chance / FieldCatalog.ChanceDenominator;
                double pTotal = p + globalP;
                double expCount = pTotal * Trials;                          // 期望命中次数
                double sd = System.Math.Sqrt(expCount * (1 - pTotal));
                // 小概率行（期望次数很少）用正态近似会误判，标准差之外再放宽 3 次吸收离散
                double tolCount = 4 * sd + 3;
                double obsCount = rowHits[i];
                double actual = obsCount / Trials;

                totalExpected += p;
                totalActual += actual;

                if (System.Math.Abs(obsCount - expCount) > tolCount)
                {
                    fail++;
                    Debug.LogError("[DropCheck] " + name + "(" + mobId + ") item=" + row.item +
                                   " 命中 " + obsCount + " 次 / 期望 " + expCount.ToString("F1") +
                                   "，超出容差 ±" + tolCount.ToString("F1") + "（4σ）");
                }
                else pass++;

                if (row.item == 0)
                {
                    int lo = row.min, hi = row.max > row.min ? row.max - 1 : row.min;
                    if (rowHits[i] > 0 && (mesoMin[i] < lo || mesoMax[i] > hi))
                    {
                        fail++;
                        Debug.LogError("[DropCheck] " + name + "(" + mobId + ") 金币区间 [" + mesoMin[i] + "," + mesoMax[i] +
                                       "] 超出原服 [Min,Max-1]=[" + lo + "," + hi + "]");
                    }
                    else pass++;
                }
                else
                {
                    if (rowHits[i] > 0 && (qtyMin[i] < Mathf.Max(1, row.min) || qtyMax[i] > row.max))
                    {
                        fail++;
                        Debug.LogError("[DropCheck] " + name + "(" + mobId + ") item=" + row.item + " 数量区间 [" +
                                       qtyMin[i] + "," + qtyMax[i] + "] 超出原服 [" + Mathf.Max(1, row.min) + "," + row.max + "]");
                    }
                    else pass++;
                }
            }

            Debug.Log("[DropCheck] " + name + "(" + mobId + ") 行 " + rows.Count +
                      "，每次击杀期望掉落 " + totalExpected.ToString("F3") + " 件，实测 " + totalActual.ToString("F3") + " 件");
        }

        // 全局掉落不生成金币
        int zeroItem = FieldCatalog.GlobalDrops.FindIndex(g => g.item == 0);
        if (zeroItem < 0) pass++;
        else { fail++; Debug.LogError("[DropCheck] 全局掉落里出现 itemId=0（原服会跳过，不该导出为金币）"); }

        int coins = 0;
        if (hits.TryGetValue(0, out coins))
            Debug.Log("[DropCheck] 金币掉落总次数 " + coins + "（" + Trials + " 次击杀 × " + FieldCatalog.Drops.Count + " 种怪物）");

        Debug.Log("[DropCheck] 通过 " + pass + " 项，失败 " + fail + " 项，样本 " + Trials + " 次/怪");
        EditorApplication.Exit(fail == 0 ? 0 : 1);
    }

    // 全量表两万多行，等距抽样若干只怪（外加新手区与二转测试怪）跑统计。
    static Dictionary<int, List<FieldDropEntry>> SampleForCheck()
    {
        var ids = new List<int>(FieldCatalog.Drops.Keys);
        ids.Sort();
        var picked = new List<int>();
        foreach (int must in new[] { 100100, 1210102, 9000001, 9000002, 9000100, 9000200, 9000300 })
            if (FieldCatalog.Drops.ContainsKey(must)) picked.Add(must);
        if (ids.Count > 0)
        {
            int stride = System.Math.Max(1, ids.Count / SampleMobs);
            for (int i = 0; i < ids.Count && picked.Count < SampleMobs + 7; i += stride)
                if (!picked.Contains(ids[i])) picked.Add(ids[i]);
        }
        var map = new Dictionary<int, List<FieldDropEntry>>();
        foreach (int id in picked) map[id] = FieldCatalog.Drops[id];
        return map;
    }
}
