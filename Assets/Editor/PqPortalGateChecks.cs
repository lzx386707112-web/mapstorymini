using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 组队任务「下一关的门」闸门回归。
// 源端把关卡隔离写在 portal/<script>.js 的 enter() 里（LPQ/KPQ 都是 eim.getProperty("Nstageclear") == null
// 就回一句「某种封印阻挡着这扇门」并 return false），移植端靠 PortalGate 把这句判据读出来，
// 让「脚下这扇门算不算开着」和脚本自己的放行条件一致——否则未清关的隐形门也会弹
// 「按 ↑ / W 进入传送门」，看起来就是「通关门一开始就出现了」。
//
// 运行：Unity -batchmode -quit -projectPath MapleDash -executeMethod PqPortalGateChecks.Run
public static class PqPortalGateChecks
{
    // 源端脚本实读：lpq0..lpq7 分别判 1,2,3,4,5,5,7,8 stageclear（第 6 关塔顶的 lpq5 复判 5stageclear，
    // 进入时顺带把 6stageclear 置位，所以那一关的下一关门不看 6stageclear）。
    static readonly (string script, string required)[] Expected =
    {
        ("lpq0", "1stageclear"), ("lpq1", "2stageclear"), ("lpq2", "3stageclear"), ("lpq3", "4stageclear"),
        ("lpq4", "5stageclear"), ("lpq5", "5stageclear"), ("lpq6", "7stageclear"), ("lpq7", "8stageclear"),
        ("kpq0", "1stageclear"), ("kpq1", "2stageclear"), ("kpq2", "3stageclear"), ("kpq3", "4stageclear"),
        ("kpq4", "5stageclear"),
    };
    // 解析不出来的门必须「当它开着」（= 完全退回改动前的行为），不能因为猜错把正常门锁掉。
    static readonly string[] Ungated = { "glpqPortal0", "glpqPortal5", "lpq99", "" };

    public static void Run()
    {
        try
        {
            int cases = 0;
            var wrong = new List<string>();
            foreach (var (script, required) in Expected)
            {
                string actual = PortalGate.Required(script);
                if (actual == required) cases++;
                else wrong.Add(script + " → " + (actual ?? "null") + "（源端 " + required + "）");
            }
            foreach (string script in Ungated)
            {
                if (PortalGate.Required(script) == null) cases++;
                else wrong.Add(script + " 不该被识别成闸门门");
            }
            // 没进事件实例时属性必然读不到：LPQ 的门必须是「关着」，GPQ 这类认不出闸门的门必须照常放行。
            if (!PortalGate.Open("lpq0")) cases++; else wrong.Add("lpq0 在无实例时竟然判为可进入");
            if (PortalGate.Open("glpqPortal0")) cases++; else wrong.Add("认不出闸门的门被误锁");

            if (wrong.Count > 0) throw new Exception(string.Join(" | ", wrong));
            Debug.Log("[PqPortalGateChecks] PASS " + cases + " 项：LPQ/KPQ 各关闸门属性与源端一致；无实例时未清关的门不开放；无法识别的门照常放行");
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    [MenuItem("Tools/检查/组队任务传送门闸门")]
    static void RunFromMenu() => Run();
}
