using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 全量族层审计：把「运行时取层规则」和「数据里真实存在的族」逐一对齐，
// 找出**取层函数会整层丢掉**的一级分支。
//
// 背景：282 的一级分支大量成族（effect / effect0..6、special / special0..3、
// affected0、finish0、hit2、screen1..12）。SkillVisuals 里
//   Layers(stem) —— 枚举「主干 + 纯数字后缀」，族层全吃
//   Parts(name)  —— 只匹配该名字本身，族层里带数字的一个都吃不到
// 两者混用就会漏层，而漏掉的那层在画面上的表现就是「和导出基准图不一样」。
// 已经踩过两次：affected0（3 个技能）、finish0（1 个技能）。
// 本审计把剩下的隐患一次性列清楚，避免再靠肉眼逐个技能试。
//
// 命令行：
//   Unity -batchmode -projectPath <proj> -executeMethod SkillVfxFamilyAudit.Run -quit -logFile /tmp/family.log
//   可选 -auditIds 400011003,400011072  只查指定技能
public static class SkillVfxFamilyAudit
{
    // 目前仍用 Parts() 直取（只认一级名）的层。
    // 改 SkillVisuals 里对应的那一行后，要从这里删掉，审计才会闭口。
    static readonly string[] PartsOnly =
    {
        "afterimage",           // Afterimage() 逐段错开，只有一级名
        "tile",                 // TileField() 逐帧砸地，只有一级名
        "hit", "hit2",          // 命中层：落在被打到的目标身上
        "ball",                 // 弹道本体
        "number", "number2",    // 数字图集，按 Icons() 顺序取
        "mob",                  // 怪物侧表现
    };

    // 已被 Layers() 覆盖的族主干（改 SkillVisuals 时同步）。
    static readonly string[] FamilyStems =
    {
        "prepare", "keydown", "keydownend", "effect", "special", "specialAffected",
        "affected", "effect_ple", "finish", "start", "repeat", "end", "screen",
        "rushEffect",   // CastVisuals 按 effect 同口径播（400011104 突进残影）
        "mask",         // 400011028 的 mask 是纯黑 stencil（1368x164, RGB=0, alpha 全 255），
                        // 不能当精灵画（会盖出黑条），CastVisuals 故意不取；这里只防误报
    };

    // 不在 NativeSkillVfx 这条链上取的分支：由别的系统按 id 直接读 IMG，
    // 出现在这里不代表漏播，只代表「不归 CastVisuals 管」。
    static readonly string[] ExternalStems =
    {
        "summon",       // NativeSummon / 召唤物实体
        "shootobj",     // NativeShootObj（Actor 发射物）
        "customEffect", // PlayCustomEffect（start/end 两段）
        "screen",       // IsScreenLayer 用 StartsWith 判定，screen_back / screen_over 也算
        "background",
        "EffectAnimSet", "ActionAnimSet",  // 动画集引用，不是画布层
        "cDoor", "mDoor", "Frame",         // 容器节点
        "number", "number2",               // ShowComboNumber 成对取
        "hit", "hit2",                     // Impact 按 HitCount 交替取，成对处理
    };

    public static void Run()
    {
        var only = new List<int>();
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "-auditIds")
                foreach (string part in args[i + 1].Split(','))
                    if (int.TryParse(part.Trim(), out int id)) only.Add(id);

        var ids = new List<int>();
        if (only.Count > 0) ids.AddRange(only);
        else
        {
            // 全部五转核心（含强化核心），六转另算
            VMatrixCatalog.Ensure();
            foreach (var core in VMatrixCatalog.AllCores)
                if (core.skills != null)
                    foreach (int skill in core.skills)
                        if (skill > 0 && !ids.Contains(skill)) ids.Add(skill);
            ids.Sort();
        }

        var drop = new List<string>();
        var unhandled = new List<string>();
        var external = new List<string>();
        var slots = new List<string>();
        int scanned = 0, noKey = 0;

        foreach (int id in ids)
        {
            string key = null;
            foreach (int level in new[] { 30, 1 })
            {
                try { key = NativeSkillVfx.Key(id, level); } catch { key = null; }
                if (!string.IsNullOrEmpty(key)) break;
            }
            if (string.IsNullOrEmpty(key)) { noKey++; continue; }
            scanned++;

            var roots = NativeSkillVfx.Roots(key);
            var seenStem = new HashSet<string>(StringComparer.Ordinal);
            foreach (string root in roots)
            {
                string stem = Stem(root);
                if (!seenStem.Add(stem)) continue;

                // 1) 需要按族取、但代码还在用 Parts 的
                bool pairwise = false;
                foreach (string ext in ExternalStems) if (stem == ext) { pairwise = true; break; }
                if (!pairwise)
                foreach (string bad in PartsOnly)
                {
                    if (stem != bad) continue;
                    var family = NativeSkillVfx.Layers(key, bad);
                    if (family.Count > 1)
                        drop.Add("  " + id + "  " + bad + " 族=" + string.Join(",", family.ToArray()));
                }

                // 2) 认识但走别的系统：只列出来，不算漏
                bool externalHit = false;
                foreach (string ext in ExternalStems)
                    if (stem == ext) { external.Add("  " + id + "  " + root); externalHit = true; break; }

                // 3) 完全不认识的层：不报错不播，属于静默丢弃
                // screen 走 StartsWith 判定（IsScreenLayer），按相等比会误报 screen_back/screen_over。
                bool known = externalHit || stem.StartsWith("screen", StringComparison.Ordinal);
                foreach (string good in FamilyStems) if (stem == good) { known = true; break; }
                if (!known) foreach (string good in PartsOnly) if (stem == good) { known = true; break; }
                if (!externalHit && !known) unhandled.Add("  " + id + "  " + root);

                // 3) specialAffected 多段同 origin = 羁绊对象槽（队员各一份）
                if (stem == "specialAffected")
                {
                    foreach (string branch in NativeSkillVfx.Layers(key, "specialAffected"))
                    {
                        var clips = NativeSkillVfx.Parts(key, branch);
                        if (clips.Count < 2) continue;
                        Vector2 first = clips[0].frames.Length > 0 ? clips[0].frames[0].origin : Vector2.zero;
                        bool same = true;
                        foreach (var clip in clips)
                            if (clip.frames.Length == 0 || clip.frames[0].origin != first) { same = false; break; }
                        if (same)
                            slots.Add("  [对象槽] " + id + "  " + branch + "  段数=" + clips.Count +
                                      "  origin=" + first);
                    }
                }
            }
        }

        var report = new List<string>();
        report.Add("扫描技能 " + scanned + " 个（无 vfx key " + noKey + " 个）");
        report.Add("");
        report.Add("## 一、仍会丢的族层（代码用 Parts，数据成族）：" + drop.Count + " 处");
        report.AddRange(drop);
        report.Add("");
        report.Add("## 二、specialAffected 里的羁绊对象槽：" + slots.Count + " 处");
        report.AddRange(slots);
        report.Add("");
        report.Add("## 三、真正没人在播的一级分支：" + unhandled.Count + " 处（这些才是漏播）");
        report.AddRange(unhandled);
        report.Add("");
        report.Add("## 四、由其它系统处理、不计漏播：" + external.Count + " 处");
        report.AddRange(external);

        string text = string.Join("\n", report.ToArray());
        Debug.Log("[FamilyAudit]\n" + text);
        System.IO.File.WriteAllText("/tmp/vfx_family_audit.txt", text);
        EditorApplication.Exit(0);
    }

    static string Stem(string name)
    {
        int end = name.Length;
        while (end > 0 && name[end - 1] >= '0' && name[end - 1] <= '9') end--;
        return name.Substring(0, end);
    }
}
