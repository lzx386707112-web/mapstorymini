using System.Collections.Generic;

// 源端 drop_data 全量导出的解析器（Field/FieldDropTable.txt）。
//
// 文本由 tools/export_field_drops.py 生成，格式：
//   # 注释
//   M|dropperid|itemid|min|max|questid|chance      ← drop_data
//   G|continent|itemid|min|max|questid|chance      ← drop_data_global
//
// 放在 Server/ 是因为它必须能脱离 Unity 跑（tools/drop-check 直接引用本文件所在的
// 程序集验证二转任务道具真的会掉）。纯 C#，不引用 UnityEngine。
public static class DropTableText
{
    public static bool TryParse(string text, out Dictionary<int, List<DropEntry>> mobs, out List<DropEntry> global)
    {
        mobs = new Dictionary<int, List<DropEntry>>();
        global = new List<DropEntry>();
        if (string.IsNullOrEmpty(text)) return false;

        int parsed = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length < 13 || line[0] == '#') continue;
            var f = line.Split('|');
            if (f.Length < 7) continue;
            bool isGlobal = f[0] == "G";
            if (!isGlobal && f[0] != "M") continue;
            // M|dropper|item|min|max|quest|chance 与 G|continent|item|min|max|quest|chance
            // 只有第 1 列含义不同，item 起的 5 列位置一致。
            const int off = 1;
            if (!int.TryParse(f[off + 1], out int item) ||
                !int.TryParse(f[off + 2], out int min) ||
                !int.TryParse(f[off + 3], out int max) ||
                !int.TryParse(f[off + 4], out int quest) ||
                !int.TryParse(f[off + 5], out int chance)) continue;

            var entry = new DropEntry { item = item, min = min, max = max, quest = quest, chance = chance };
            if (isGlobal) { global.Add(entry); parsed++; continue; }
            if (!int.TryParse(f[1], out int dropper)) continue;
            if (!mobs.TryGetValue(dropper, out var list)) mobs[dropper] = list = new List<DropEntry>();
            list.Add(entry);
            parsed++;
        }
        return parsed > 0 && mobs.Count > 0;
    }
}
