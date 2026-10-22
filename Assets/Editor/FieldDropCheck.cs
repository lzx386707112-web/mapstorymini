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
//   3. 掉出来的 itemId 必须都在 FieldDrops.json 导出的物品表里
//   4. 全局掉落不得生成金币（原服 dropGlobalItems 对 itemId=0 直接跳过）
public static class FieldDropCheck
{
    const int Trials = 50000;

    public static void Run()
    {
        FieldCatalog.Ensure();
        if (!FieldCatalog.Ready) { Debug.LogError("[DropCheck] FieldCatalog 未加载"); EditorApplication.Exit(1); return; }

        int pass = 0, fail = 0;
        var hits = new Dictionary<int, int>();     // 物品 id → 命中次数（便于汇总打印）
        var rng = new System.Random(20251021);

        foreach (var kv in FieldCatalog.Drops)
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
                double expCount = p * Trials;                              // 期望命中次数
                double sd = System.Math.Sqrt(expCount * (1 - p));
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

                    if (!FieldCatalog.Items.ContainsKey(row.item))
                    {
                        fail++;
                        Debug.LogError("[DropCheck] " + name + "(" + mobId + ") item=" + row.item + " 不在物品表里");
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
}
