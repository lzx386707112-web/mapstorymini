using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// 关卡数据自检：验证 Resources 路径、JSON 解析、bossId/theme 的引用是否都能对上。
// 菜单：Tools/关卡数据自检
// 命令行：Unity -batchmode -quit -projectPath <proj> -executeMethod StageSmokeTest.RunFromCLI
//
// 存在的意义：数据是手写的 JSON，字段名写错（比如 bossId 写成 boss）不会报错，
// 只会在游戏里静默退化成"打了第一个 Boss"，非常难查。这个自检就是防这个。
public static class StageSmokeTest
{
    static StringBuilder sb;
    static int errors;

    static void Line(string s) { sb.AppendLine(s); Debug.Log(s); }
    static void Err(string s)  { errors++; sb.AppendLine("  ✗ " + s); Debug.LogError(s); }

    [MenuItem("Tools/关卡数据自检")]
    public static void Run()
    {
        DoRun();
        EditorUtility.DisplayDialog("关卡数据自检",
            errors == 0 ? "全部通过 ✔\n\n" + Summary() : "发现 " + errors + " 个问题\n\n" + Summary(), "好");
    }

    public static void RunFromCLI()
    {
        DoRun();
        var path = Path.Combine(Directory.GetCurrentDirectory(), "stage_smoke.log");
        File.WriteAllText(path, sb.ToString());
        Debug.Log("[Smoke] 结果写入 " + path + "，错误数 " + errors);
        if (errors > 0) EditorApplication.Exit(1);
        EditorApplication.Exit(0);
    }

    static string Summary()
    {
        var t = sb.ToString();
        return t.Length > 1600 ? t.Substring(t.Length - 1600) : t;
    }

    static void DoRun()
    {
        sb = new StringBuilder();
        errors = 0;
        Line("=== 关卡数据自检 ===");

        var ch = StageChapter.Load("Stage/chapter1");
        if (ch == null) { Err("加载失败：Resources/Stage/chapter1.json 找不到"); return; }

        Line("章节：" + ch.chapterId + " " + ch.name + " — " + ch.subtitle);
        Line("主题 " + ch.themes.Count + " 个，Boss " + ch.bosses.Count + " 个，关卡 " + ch.levels.Count + " 关");

        if (ch.levels.Count == 0) { Err("没有任何关卡"); return; }

        foreach (var kv in ch.bosses)
            Line("  Boss [" + kv.Key + "] " + kv.Value.name + " HP " + kv.Value.hp +
                 " 红圈 " + kv.Value.slam.telegraph + "s 硬直 " + kv.Value.slam.recover + "s " +
                 "半径 " + kv.Value.slam.radius + "px 阶段 " + kv.Value.phases.Count);

        int totalSeg = 0, totalSpawn = 0, bossSeg = 0;

        foreach (var lv in ch.levels)
        {
            Line("— " + lv.id + " " + lv.name + "（主题 " + lv.theme + "，段 " + lv.segments.Count + "）");

            if (!ch.themes.ContainsKey(lv.theme)) Err(lv.id + " 引用了不存在的主题：" + lv.theme);
            if (lv.segments.Count == 0) Err(lv.id + " 没有任何段");
            if (string.IsNullOrEmpty(lv.goal)) Err(lv.id + " 缺 goal");

            for (int i = 0; i < lv.segments.Count; i++)
            {
                var s = lv.segments[i];
                totalSeg++;

                switch (s.kind)
                {
                    case "walk":
                        if (s.duration <= 0f) Err(lv.id + " 段" + i + " walk 没有 duration");
                        break;

                    case "rest":
                        if (s.duration <= 0f) Line("    （提示）" + lv.id + " 段" + i + " rest 未写 duration，将用默认 3s");
                        break;

                    case "combat":
                        if (s.spawns.Count == 0) Err(lv.id + " 段" + i + " combat 没有 spawns");
                        foreach (var sp in s.spawns)
                        {
                            totalSpawn += sp.count;
                            if (sp.count <= 0) Err(lv.id + " 段" + i + " spawn count <= 0");
                            if (sp.gap <= 0f) Err(lv.id + " 段" + i + " spawn gap <= 0");
                            if (sp.side != "left" && sp.side != "right" && sp.side != "both")
                                Err(lv.id + " 段" + i + " side 非法：" + sp.side);
                        }
                        break;

                    case "boss":
                        bossSeg++;
                        if (string.IsNullOrEmpty(s.boss))
                            Err(lv.id + " 段" + i + " boss 段没有 bossId（这是最容易写错的字段）");
                        else if (!ch.bosses.ContainsKey(s.boss))
                            Err(lv.id + " 段" + i + " 找不到 Boss：" + s.boss);
                        else
                            Line("    Boss 段 → " + ch.bosses[s.boss].name + "（" + s.boss + "）");
                        break;

                    default:
                        Err(lv.id + " 段" + i + " 未知 kind：" + s.kind);
                        break;
                }

                if (s.kind != "boss" && s.kind != "walk" && s.kind != "rest" && s.kind != "combat")
                    Err(lv.id + " 段" + i + " 类型无法处理");
            }
        }

        Line("合计：段 " + totalSeg + "，杂兵 " + totalSpawn + " 只，Boss 段 " + bossSeg + " 个");
        Line(errors == 0 ? "结果：全部通过 ✔" : "结果：发现 " + errors + " 个问题 ✗");
    }
}
