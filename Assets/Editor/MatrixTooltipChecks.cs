using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;

// V / HEXA 矩阵 tooltip 的数据链路守卫：
//   coreId → VMatrixCatalog.Core / HexaCatalog.Core → SkillBook.Get(skills[0]) → SkillData.ForTooltip
// 断言「最终画到屏幕上的名字」一定是这个 coreId 的权威核心名，不是别的核心/别的技能的。
//
// 事故背景：聖十字架（10010027 → 400011072）悬停显示「靈魂蝕日」
// （10010030 → 400011088，另一个职业组）。数据表本身没错，所以守卫必须从
// coreId 一路走到 tooltip 文本，不能只比 JSON。
//
// 命令行：
//   Unity -batchmode -projectPath <proj> -executeMethod MatrixTooltipChecks.Run -quit -logFile /tmp/matrix_tooltip.log
public static class MatrixTooltipChecks
{
    static readonly int[] Jobs = { 112, 122, 132, 1112, 1212, 1312, 2112, 212, 222, 232 };

    static void Require(bool ok, string message)
    {
        if (!ok) throw new Exception("[MatrixTooltip] " + message);
    }

    // 繁简 / 空格 / 标点差异不算错，只比中文字与字母数字。
    static string Norm(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
            if ((c >= 0x4E00 && c <= 0x9FFF) || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                sb.Append(c);
        return sb.ToString();
    }

    static string First(string text, int width)
    {
        if (string.IsNullOrEmpty(text)) return "";
        text = text.Replace("\\n", " ").Replace("\n", " ").Replace("\r", " ");
        return text.Length <= width ? text : text.Substring(0, width) + "…";
    }

    static List<int> Keys(Type type, string field)
    {
        var info = type.GetField(field, BindingFlags.Static | BindingFlags.NonPublic);
        var result = new List<int>();
        if (info?.GetValue(null) is IDictionary table)
            foreach (object key in table.Keys)
                if (key is int id) result.Add(id);
        result.Sort();
        return result;
    }

    // 项目里不是每个 Skill/<分片>.img 都在（五转核心会引用没导入的职业分片），
    // Get() 在那种分片上会直接抛 FileNotFoundException，这里统一收成「取不到定义」。
    static SkillData SafeGet(int id)
    {
        try { return SkillBook.Get(id); }
        catch (Exception e) { Debug.LogWarning("[MatrixTooltip] Get(" + id + ") 失败：" + e.GetType().Name + " " + e.Message); return null; }
    }

    // Get() 的四个来源逐层打印：byId（JobTree / VMatrixSkills 已收录）→ 灵魂武器 →
    // IMG 直读 String/Skill.img → matrixNames 兜底。名字对不上时看这一段就知道是哪层串的。
    static void Dump(int id)
    {
        const BindingFlags F = BindingFlags.Static | BindingFlags.NonPublic;
        var byId = typeof(SkillBook).GetField("byId", F)?.GetValue(null) as IDictionary;
        var names = typeof(SkillBook).GetField("matrixNames", F)?.GetValue(null) as IDictionary;
        var descs = typeof(SkillBook).GetField("matrixDescs", F)?.GetValue(null) as IDictionary;
        bool inById = byId != null && byId.Contains(id);
        bool inNames = names != null && names.Contains(id);
        var asset = Resources.Load<TextAsset>("UI/VMatrixSkills");
        bool hasNode = false;
        string imgName = "";
        try
        {
            var node = WzAssets.FileRoot("String/Skill.img")?.Get(id.ToString("D7"));
            hasNode = node != null;
            imgName = node == null ? "" : node.Text("name");
        }
        catch (Exception e) { imgName = "<" + e.GetType().Name + ">"; }
        var data = SafeGet(id);
        Debug.Log("[MatrixTooltip][Dump] id=" + id +
                  " byId=" + inById + (inById && byId[id] is SkillData d0 ? "「" + d0.name + "」" : "") +
                  " matrixNames=" + inNames + (inNames ? "「" + names[id] + "」" : "") +
                  " String/Skill.img 有节点=" + hasNode + " name=「" + imgName + "」" +
                  " VMatrixSkills.json=" + (asset != null ? "已加载 " + asset.text.Length + " 字符" : "缺失") +
                  " Get()=" + (data == null ? "null" : "「" + data.name + "」type=" + data.type + " levels=" + data.levels.Count));
    }

    public static void Run()
    {
        VMatrixCatalog.Ensure();
        HexaCatalog.Ensure();
        SkillBook.Ensure();
        foreach (int id in new[] { 400011003, 400011052, 400011072, 400011088, 400011131 }) Dump(id);

        int cores = 0, descBad = 0, missing = 0, noSkill = 0;
        var report = new List<string>();
        var byName = new Dictionary<string, List<int>>();
        var bySkill = new Dictionary<int, List<int>>();

        report.Add("======== V 矩阵：职业组技能槽 ========");
        foreach (int job in Jobs)
        {
            var group = VMatrixCatalog.Group(job);
            if (group == null) { report.Add("job " + job + "：无 V 组"); continue; }
            report.Add("-- job " + job + "  skill 槽 " + group.skill.Length + " 个");
            for (int slot = 0; slot < group.skill.Length; slot++)
            {
                int coreId = group.skill[slot];
                var core = VMatrixCatalog.Core(coreId);
                if (core == null) { missing++; report.Add("   slot" + slot + " core " + coreId + " 不在 cores 表"); continue; }
                cores++;
                string skillName = "—", tipName = "—";
                if (core.skills == null || core.skills.Length == 0) noSkill++;
                else
                {
                    var data = SafeGet(core.skills[0]);
                    if (data == null) { missing++; report.Add("   slot" + slot + " " + coreId + " skill " + core.skills[0] + " 取不到定义"); continue; }
                    skillName = data.name;
                    var tip = data.ForTooltip(core.name, core.desc);
                    Require(tip != null, coreId + " ForTooltip 返回 null");
                    tipName = tip.name;
                    Require(Norm(tipName) == Norm(core.name),
                        "core " + coreId + "「" + core.name + "」tooltip 标题变成了「" + tipName + "」");
                    if (!string.IsNullOrEmpty(core.desc) && Norm(tip.desc) != Norm(core.desc))
                    {
                        descBad++;
                        report.Add("   ! desc 不符 core " + coreId + "「" + core.name + "」");
                    }
                }
                report.Add("   slot" + slot + " core=" + coreId + "「" + core.name + "」skill=" +
                           (core.skills.Length > 0 ? core.skills[0].ToString() : "无") +
                           " 技能名=「" + skillName + "」tooltip=「" + tipName + "」");
                report.Add("          " + First(core.desc, 44));
            }
        }

        report.Add("");
        report.Add("======== HEXA 矩阵：全部核心 ========");
        foreach (int coreId in Keys(typeof(HexaCatalog), "cores"))
        {
            var core = HexaCatalog.Core(coreId);
            if (core == null) continue;
            if (core.type == 2) continue;
            if (core.skills == null || core.skills.Length == 0) { noSkill++; continue; }
            var data = SafeGet(core.skills[0]);
            if (data == null) { missing++; report.Add("   HEXA core " + coreId + "「" + core.name + "」skill " + core.skills[0] + " 取不到定义"); continue; }
            cores++;
            var tip = data.ForTooltip(core.name, core.desc);
            Require(tip != null, "HEXA " + coreId + " ForTooltip null");
            Require(Norm(tip.name) == Norm(core.name),
                "HEXA core " + coreId + "「" + core.name + "」tooltip 标题变成了「" + tip.name + "」");
            Track(byName, bySkill, coreId, core.name, core.skills);
        }

        // ——— 全局串位扫描 ———
        foreach (int coreId in Keys(typeof(VMatrixCatalog), "cores"))
        {
            var core = VMatrixCatalog.Core(coreId);
            if (core == null || core.type == 2) continue;
            Track(byName, bySkill, coreId, core.name, core.skills);
        }
        int dupName = 0, dupSkill = 0;
        foreach (var pair in byName)
            if (pair.Value.Count > 1) { dupName++; report.Add("重名核心「" + pair.Key + "」→ " + string.Join(",", pair.Value)); }
        foreach (var pair in bySkill)
            if (pair.Value.Count > 1) { dupSkill++; report.Add("同技能多核心 " + pair.Key + " → " + string.Join(",", pair.Value)); }

        report.Add("");
        report.Add("合计检查核心 " + cores + "；desc 不符 " + descBad + "；取不到定义 " + missing +
                   "；无连接技能 " + noSkill + "；重名 " + dupName + "；同技能多核心 " + dupSkill);

        // 圣骑士事故复现点单独硬断言
        foreach (int coreId in new[] { 10010002, 10010014, 10010027, 10010042 })
        {
            var core = VMatrixCatalog.Core(coreId);
            Require(core != null, "圣骑士核心 " + coreId + " 缺失");
            report.Add("圣骑士 core " + coreId + " = " + core.name + " → skill " +
                       (core.skills.Length > 0 ? core.skills[0].ToString() : "无"));
        }
        // 表里是繁体（聖十字架 / 靈魂蝕日），这里不比繁简，只比关键字，避免守卫自己误报。
        var cross = VMatrixCatalog.Core(10010027);
        Require(cross != null && cross.name.Contains("十字"), "10010027 不是圣十字架：" + (cross == null ? "缺失" : cross.name));
        Require(cross != null && cross.skills.Length > 0 && cross.skills[0] == 400011072, "10010027 没连到 400011072");
        var eclipse = VMatrixCatalog.Core(10010030);
        if (eclipse != null)
        {
            Require(eclipse.name.Contains("蝕日") || eclipse.name.Contains("蚀日"), "10010030 不是灵魂蚀日：" + eclipse.name);
            Require(eclipse.skills.Length > 0 && eclipse.skills[0] == 400011088, "10010030 没连到 400011088");
        }

        string text = string.Join("\n", report.ToArray());
        System.IO.File.WriteAllText("/tmp/matrix_tooltip_report.txt", text);
        Debug.Log("[MatrixTooltip] 报告已写入 /tmp/matrix_tooltip_report.txt（" + report.Count + " 行）");
        EditorApplication.Exit(0);
    }

    static void Track(Dictionary<string, List<int>> byName, Dictionary<int, List<int>> bySkill,
                      int coreId, string name, int[] skills)
    {
        string key = Norm(name);
        if (key.Length > 0)
        {
            if (!byName.TryGetValue(key, out var list)) byName[key] = list = new List<int>();
            if (!list.Contains(coreId)) list.Add(coreId);
        }
        if (skills == null || skills.Length == 0) return;
        if (!bySkill.TryGetValue(skills[0], out var owners)) bySkill[skills[0]] = owners = new List<int>();
        if (!owners.Contains(coreId)) owners.Add(coreId);
    }
}
