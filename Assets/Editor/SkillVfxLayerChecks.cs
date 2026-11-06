using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 按 SkillVisuals.CastVisuals / Impact 的**真实取层函数**（NativeSkillVfx.Layers / Parts）
// 列出每个技能运行时到底会播哪些一级分支、每层几段几帧。
//
// 存在的理由：光看 JSON 会漏掉「Parts 只认一级名、不认 effect0/affected0」这种规则差，
// 而基准图（tools/ms282/export_warrior_v_core_vfx.py）是把同一族的层全叠上去的。
// 两边一对照，「游戏里和导出图不一样」到底缺了哪一层就能直接看出来。
//
// 命令行：
//   Unity -batchmode -projectPath <proj> -executeMethod SkillVfxLayerChecks.Run -quit -logFile /tmp/vfx_layers.log
public static class SkillVfxLayerChecks
{
    // 圣骑士四个五转核心（事故复现范围）
    static readonly int[] Default = { 400011003, 400011052, 400011072, 400011131 };

    public static void Run()
    {
        string ids = "";
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "-vfxIds") ids = args[i + 1];
        var list = new List<int>();
        if (!string.IsNullOrEmpty(ids))
            foreach (string part in ids.Split(','))
                if (int.TryParse(part.Trim(), out int id)) list.Add(id);
        if (list.Count == 0) list.AddRange(Default);

        var report = new List<string>();
        foreach (int id in list)
        {
            foreach (int level in new[] { 1, 30 })
            {
                string key;
                try { key = NativeSkillVfx.Key(id, level); }
                catch (Exception e) { report.Add(id + "@" + level + " Key() 失败：" + e.GetType().Name); continue; }
                if (string.IsNullOrEmpty(key)) { report.Add(id + "@" + level + " 没有 vfx key"); continue; }

                var roots = NativeSkillVfx.Roots(key);
                int totalClips = 0, totalFrames = 0, picked = 0;
                var detail = new List<string>();
                foreach (string root in roots)
                {
                    var parts = NativeSkillVfx.Parts(key, root);
                    if (parts.Count == 0) continue;
                    int frames = 0;
                    foreach (var clip in parts) frames += clip.frames.Length;
                    if (frames == 0) continue;
                    bool cast = CastPicks(root);
                    if (cast) { picked++; totalClips += parts.Count; totalFrames += frames; }
                    detail.Add("      " + (cast ? "CAST" : " -- ") + " " + root +
                               "  clips=" + parts.Count + " frames=" + frames);
                }
                report.Add("[" + id + "@" + level + "] key=" + key + "  一级分支 " + roots.Count +
                           "，施法侧取 " + picked + " 层 / " + totalClips + " 段 / " + totalFrames + " 帧");
                report.AddRange(detail);
            }
        }

        string text = string.Join("\n", report.ToArray());
        Debug.Log("[VfxLayer]\n" + text);
        System.IO.File.WriteAllText("/tmp/vfx_layers.txt", text);
        EditorApplication.Exit(0);
    }

    // SkillVisuals.CastVisuals 真正会去取的层。规则照源文件抄，不做"看起来合理"的推测：
    //   prepare / effect / special / specialAffected / effect_ple / start·repeat·end
    //   → Layers()（自动带 effect0、special0… 编号兄弟）
    //   affected / finish / afterimage / tile / hit / hit2
    //   → 原本是 Parts()，只认一级名；affected 与 finish 已改成 Layers()。
    //   screen / frameEffect / customEffect 走各自专用分支。
    static bool CastPicks(string root)
    {
        string stem = Stem(root);
        switch (stem)
        {
            case "prepare": case "keydown": case "keydownend":
            case "effect": case "special": case "specialAffected": case "effect_ple":
            case "start": case "repeat": case "end":
            case "affected":            // 已改为 Layers
            case "finish":              // 已改为 Layers
            case "screen": case "frameEffect": case "customEffect":
                return true;
            case "afterimage": case "tile": case "hit": case "hit2":
                return true;
            default:
                return false;
        }
    }

    static string Stem(string name)
    {
        int end = name.Length;
        while (end > 0 && name[end - 1] >= '0' && name[end - 1] <= '9') end--;
        return name.Substring(0, end);
    }
}
