using System;
using System.Collections.Generic;

// 原服掉落判定的唯一实现（MapleMap.dropItemsFromMonsterOnMap / dropGlobalItemsFromMonsterOnMap）。
// 抽成静态类是为了让编辑器自检能直接跑几十万次抽样，用统计结果核对 SQL 里的 chance/min/max，
// 而不是靠「肉眼看打了几只怪掉了几件」。
//
// 关键规则（与原服源码逐条对应）：
//   1. 先 Collections.shuffle(dropEntry)，再按顺序逐条判定
//   2. Randomizer.nextInt(999999) < chance 才掉（chance=0 永不掉）
//   3. itemId == 0 是金币：nextInt(Max - Min) + Min，即 [Min, Max-1]
//   4. 非装备：quantity = Max(1, Min)，Max > Min 时再加 nextInt(Max-Min+1)，即 [Min, Max]
//   5. 全局掉落的数量用 Randomizer.rand(min, max)（两端都含），且 itemId == 0 的条目不生成金币
//   6. 每次落地的横向偏移由 d 左右交替：d 偶数 → +25*(d+1)/2，奇数 → -25*(d/2)
public static class FieldLoot
{
    /// <summary>落地回调：itemId=0 表示金币；offsetIndex 对应原服 getPos 的 d。</summary>
    public delegate void DropSink(int itemId, int quantity, int offsetIndex);

    /// <summary>原服 getPos 的横向像素偏移（droptype 0/1/2，即普通/队伍/自由拾取）。</summary>
    public static float OffsetPx(int d)
        => (d % 2 == 0) ? 25f * ((d + 1) / 2) : -25f * (d / 2);

    public static void Roll(int mobId, DropSink sink, Random rng, double dropRate = 1)
    {
        if (sink == null || rng == null) return;
        int d = 1;

        // 掉落表以 DB（drop_data）为准，后台改完 DropStore.Invalidate() 即生效；
        // 无库时 DropStore 回退 FieldDrops.json 快照，判定规则不变。
        List<DropEntry> rows = DropStore.ForMob(mobId);
        if (rows.Count > 0)
        {
            var list = new List<DropEntry>(rows);
            Shuffle(list, rng);
            foreach (var row in list)
            {
                if (rng.Next(0, DropStore.ChanceDenominator) >= row.chance * dropRate) continue;
                if (row.item == 0)
                {
                    // nextInt(Max - Min) + Min；Max == Min 时 nextInt(0) 在原服返回 0
                    int mesos = row.max > row.min ? rng.Next(row.min, row.max) : row.min;
                    if (mesos > 0) sink(0, mesos, d++);
                }
                else
                {
                    int qty = Math.Max(1, row.min);
                    if (row.max > row.min) qty += rng.Next(0, row.max - row.min + 1);
                    sink(row.item, qty, d++);
                }
            }
        }

        var glob = new List<DropEntry>(DropStore.Global());
        if (glob.Count == 0) return;
        Shuffle(glob, rng);
        foreach (var row in glob)
        {
            if (rng.Next(0, DropStore.ChanceDenominator) >= row.chance * dropRate) continue;
            if (row.item == 0) continue;                     // 全局掉落不生成金币
            int min = Math.Max(1, row.min);
            int max = Math.Max(min, row.max);
            int qty = max > min ? rng.Next(min, max + 1) : min;   // rand(min, max) 两端都含
            sink(row.item, qty, d++);
        }
    }

    public static void Shuffle<T>(IList<T> list, Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(0, i + 1);
            T t = list[i]; list[i] = list[j]; list[j] = t;
        }
    }
}
