using System;
using System.Collections.Generic;
using UnityEngine;

// 施法结算体检：把「按下去之后到底会不会打到怪」这条链上的每一个判据都打出来。
//
// 起因：400011072 圣十字架「只播效果、打不了怪」。
// 它的施法走的是 SkillSystem 里最绕的一条：TryCastBoundSkill → rapid → StartHeld
// → Update() 每 100ms BeginCast(continuousShot:true) → ProjectileCast / HitAt。
// 只要中间任何一环判 false，表现就是「特效照放、伤害为零」，而且不报错。
//
// 命令行：
//   Unity -batchmode -projectPath <proj> -executeMethod SkillCombatDump.Run -quit -logFile /tmp/dump.log
//   Unity ... -executeMethod SkillCombatDump.Run -dumpIds 400011072,400011003
public static class SkillCombatDump
{
    static readonly int[] Default = { 400011072, 400011003, 400011052, 400011131 };

    public static void Run()
    {
        var ids = new List<int>();
        var args = Environment.GetCommandLineArgs();
        bool tree = false;
        bool uncastable = false;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-dumpIds")
                foreach (string part in args[i + 1].Split(','))
                    if (int.TryParse(part.Trim(), out int parsed)) ids.Add(parsed);
            if (args[i] == "-dumpTree") tree = args[i + 1] != "0";
            if (args[i] == "-dumpUncastable") uncastable = args[i + 1] != "0";
        }
        if (ids.Count == 0) ids.AddRange(Default);

        if (uncastable)
        {
            string audit = AuditUncastable();
            Debug.Log("[CombatDump]\n" + audit);
            System.IO.File.WriteAllText("/tmp/skill_uncastable.txt", audit);
            UnityEditor.EditorApplication.Exit(0);
            return;
        }

        var lines = new List<string>();
        foreach (int id in ids)
        {
            lines.Add("");
            lines.Add("=== " + id + " ===");
            if (tree) DumpTree(lines, id);
            else DumpDefinition(lines, id);
        }

        string text = string.Join("\n", lines.ToArray());
        Debug.Log("[CombatDump]\n" + text);
        System.IO.File.WriteAllText("/tmp/skill_combat_dump.txt", text);
        UnityEditor.EditorApplication.Exit(0);
    }

    // 源端节点树（= 项目里那份 282 的 Skill/<job>.img）。「对比 282」在这里就是逐节点看
    // 它到底把伤害/形态写在哪：common 的公式、level/N 的字段、还是 psdSkill 指名的子技能。
    static void DumpTree(List<string> lines, int id)
    {
        string basePath = "Skill/" + (id / 10000).ToString("D3") + ".img/skill/" + id.ToString("D7");
        var node = WzNode(basePath);
        if (node == null) { lines.Add("  <项目里没有这个技能节点：" + basePath + ">"); return; }

        var roots = new List<string>();
        foreach (var child in node.Children) roots.Add(child.Name);
        roots.Sort(StringComparer.Ordinal);
        lines.Add("  顶层子节点：" + string.Join("  ", roots.ToArray()));

        foreach (string key in new[] { "info", "info2", "common", "psdSkill", "skillList",
                                       "type4UI", "req", "summon", "action", "weapon" })
        {
            var sub = node.Get(key);
            if (sub == null) continue;
            lines.Add("  " + key + " = " + Flatten(sub, 2));
        }

        var level = node.Get("level");
        if (level == null) lines.Add("  level: 无（只有 common）");
        else
        {
            var rows = new List<string>();
            foreach (var row in level.Children) rows.Add(row.Name);
            rows.Sort(StringComparer.Ordinal);
            lines.Add("  level 级数=" + rows.Count + "（" + string.Join(",", rows.ToArray()) + "）");
            foreach (string pick in new[] { "1", "30" })
            {
                var row = level.Get(pick);
                if (row == null) continue;
                lines.Add("  level/" + pick + " = " + Flatten(row, 1));
            }
        }
    }

    // 把子节点拼成 k=v，子属性递归一层（够看 lt/rb 这种 vector 与嵌套）
    static string Flatten(MapleDash.Wz.ImgNode node, int depth)
    {
        var items = new List<string>();
        foreach (var child in node.Children)
        {
            if (child.Kind == "Canvas") { items.Add(child.Name + "=<canvas " + child.Width + "x" + child.Height + ">"); continue; }
            if (child.Children.Count > 0 && depth > 0)
            {
                items.Add(child.Name + "={" + Flatten(child, depth - 1) + "}");
                continue;
            }
            items.Add(child.Name + "=" + Format(child.Value));
        }
        items.Sort(StringComparer.Ordinal);
        return items.Count == 0 ? "{}" : "{" + string.Join("  ", items.ToArray()) + "}";
    }

    static void DumpDefinition(List<string> lines, int id)
    {
        var data = SafeGet(id);
        if (data == null) { lines.Add("  SkillBook.Get() 返回 null"); return; }
        {
            lines.Add("  名字=" + data.name + "  type=" + data.type + "  passive=" + data.IsPassive +
                      "  SpecialKind=" + (SkillBook.SpecialKind(id) ?? "null") +
                      "  InstantSelf=" + SkillSystem.InstantSelfSkill(id) +
                      "  maxLevel=" + data.maxLevel + "  masterLevel=" + data.masterLevel);

            var row = data.Level(30);
            lines.Add("  —— 原始行（level 30）有值的键 ——");
            if (row == null) lines.Add("    （没有 level/30 行）");
            else
            {
                var keys = new List<string>(row.Keys);
                keys.Sort(StringComparer.Ordinal);
                var kept = new List<string>();
                foreach (string key in keys)
                {
                    object value = row[key];
                    if (value == null) continue;
                    string asText = value as string;
                    if (asText != null) { kept.Add(key + "=" + asText); continue; }
                    kept.Add(key + "=" + Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
                }
                lines.Add("    " + string.Join("  ", kept.ToArray()));
            }

            NativeSkillDefinition native = null;
            try { native = SkillBook.Native(id, 30); }
            catch (Exception e) { lines.Add("  Native(30) 抛异常：" + e.GetType().Name + " " + e.Message); }
            if (native == null) { lines.Add("  Native(30) 为 null ⇒ 施展时会以「技能等级数据缺失」拒绝"); }
            else
            {
                lines.Add("  —— 结算定义 ——");
                lines.Add("    Kind=" + native.Kind + "  Damage%=" + native.DamagePercent +
                          "  段数=" + native.HitCount + "  目标数=" + native.TargetCount);
                lines.Add("    攻击框 Min=" + native.Min + " Max=" + native.Max +
                          "  HasBounds=" + native.HasBounds +
                          "  FullScreen=" + native.FullScreen + "  AreaAttack=" + native.AreaAttack);
                lines.Add("    冷却=" + native.Cooldown + "s  MP=" + native.MpCost +
                          "  Duration=" + native.Duration);
            }

            lines.Add("  —— 施法路径判据 ——");
            lines.Add("    NativeHeldSkill.Continuous=" + NativeHeldSkill.Continuous(id) +
                      "  KeyDown=" + NativeHeldSkill.KeyDown(id));
            lines.Add("    NativeSkillVfx.RapidAttack=" + SafeBool(() => NativeSkillVfx.RapidAttack(id)));
            lines.Add("    HasLayer(keydown)=" + SafeBool(() => NativeSkillVfx.HasLayer(Key(id), "keydown")) +
                      "  HasLayer(keydownend)=" + SafeBool(() => NativeSkillVfx.HasLayer(Key(id), "keydownend")));
            lines.Add("    NativeShootObj.Has=" + SafeBool(() => NativeShootObj.Has(id)));
            lines.Add("    Has(ball)=" + SafeBool(() => NativeSkillVfx.Has(Key(id), "ball")));
            // UsesProjectile 是 SkillVisuals 的私有静态方法，规则照抄一份用于诊断
            bool usesProjectile = data.GetType() != null &&
                (SafeBool(() => NativeSkillVfx.Has(Key(id), "ball")) || SafeBool(() => NativeShootObj.Has(id)));
            lines.Add("    ⇒ 判据推演：rapid 需要「绑了快捷键且按住」；按住期间每 100ms 走一次" +
                      (usesProjectile ? " ProjectCast" : " HitAt"));

            // info 节点原样打出来：判断「这个技能到底该不该能放」只能看源端字段，
            // 不能看 SkillBook 分类（极冰风暴 type=passive 但有 cooltime/mpCon/79 帧施法特效）。
            lines.Add("  —— 源端 info ——");
            var info = WzNode("Skill/" + (id / 10000).ToString("D3") + ".img/skill/" +
                              id.ToString("D7") + "/info");
            if (info == null) lines.Add("    （没有 info 节点）");
            else
            {
                var parts = new List<string>();
                foreach (var child in info.Children)
                    parts.Add(child.Name + "=" + Format(child.Value));
                parts.Sort(StringComparer.Ordinal);
                lines.Add("    " + (parts.Count == 0 ? "（空）" : string.Join("  ", parts.ToArray())));
            }
        }

        string text = string.Join("\n", lines.ToArray());
        Debug.Log("[CombatDump]\n" + text);
        System.IO.File.WriteAllText("/tmp/skill_combat_dump.txt", text);
        UnityEditor.EditorApplication.Exit(0);
    }

    // 全量「装了核心却按不动」清单。
    // 起因：400011121 极冰风暴 装备核心 10010046 却只回「被动技能无需施放」。
    // 根因是 SkillBook.Get() 的 IMG 兜底按字段猜类型，猜不出来就留在 passive；
    // tools/check_matrix_skill_coverage.py 只验「能解析」所以一路报 OK。
    // 这里按核心反查真实可施放性，把同类问题一次性列全。
    static string AuditUncastable()
    {
        VMatrixCatalog.Ensure();
        var lines = new List<string>();
        var seen = new HashSet<int>();
        int total = 0, missing = 0, passive = 0, ok = 0;
        foreach (var core in VMatrixCatalog.AllCores)
        {
            if (core.type != 0 || core.skills == null) continue;
            foreach (int id in core.skills)
            {
                if (id <= 0 || !seen.Add(id)) continue;
                total++;
                var data = SafeGet(id);
                if (data == null) { missing++; lines.Add("  [无定义] " + id + "  core=" + core.id); continue; }
                if (!data.IsPassive) { ok++; continue; }
                passive++;
                lines.Add("  [按不动] " + id + "「" + data.name + "」 core=" + core.id +
                          " type=" + data.type +
                          "  源端info=" + InfoSummary(id));
            }
        }
        var report = new List<string>();
        report.Add("技能核心引用技能 " + total + " 个：可施放 " + ok +
                   "，被判被动 " + passive + "，无定义 " + missing);
        report.Add("");
        report.AddRange(lines);
        return string.Join("\n", report.ToArray());
    }

    static string InfoSummary(int id)
    {
        var info = WzNode("Skill/" + (id / 10000).ToString("D3") + ".img/skill/" + id.ToString("D7") + "/info");
        if (info == null) return "<无 info>";
        var parts = new List<string>();
        foreach (var child in info.Children) parts.Add(child.Name + "=" + Format(child.Value));
        parts.Sort(StringComparer.Ordinal);
        return parts.Count == 0 ? "<空>" : string.Join(" ", parts.ToArray());
    }

    static string Key(int id)
    {
        try { return NativeSkillVfx.Key(id, 30); } catch { return null; }
    }

    static MapleDash.Wz.ImgNode WzNode(string path)
    {
        try { return WzAssets.Node(path); } catch { return null; }
    }

    static string Format(object value)
    {
        if (value == null) return "null";
        var text = value as string;
        if (text != null) return text;
        if (value is float || value is double)
            return Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture)
                .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    static SkillData SafeGet(int id)
    {
        try { return SkillBook.Get(id); }
        catch (Exception e)
        {
            Debug.LogWarning("[CombatDump] Get(" + id + ") 失败：" + e.GetType().Name + " " + e.Message);
            return null;
        }
    }

    static bool SafeBool(Func<bool> f)
    {
        try { return f(); } catch { return false; }
    }
}
