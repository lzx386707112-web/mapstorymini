using System;
using System.Collections.Generic;

// 组队任务「通往下一关的门」什么时候算开着。
//
// 源端从不靠「门自己藏起来」做关卡隔离：LudiPQ 每张图通往下一关的 next00 是 pt=5（隐形，客户端
// 连光洞都不画），闸门写在 portal/<script>.js 的 enter() 里：
//     var avail = eim.getProperty("1stageclear");
//     if (avail == null) { pi.getPlayer().dropMessage(5, "某种封印阻挡着这扇门，暂时无法进入。"); return false; }
//     pi.playPortalSound(); pi.getPlayer().changeMap(target, targetPortal);
// 也就是「关卡没清 → 只回一句封印」，队长点 NPC 交券清关（clearStage → setProperty）之后才放行。
//
// 移植端多出一条 HUD 提示（GameHUD「按 ↑ / W 进入传送门」），它只看脚下有没有 pt=1/2/4/5/... 的门，
// 于是站在还没清关的隐形门上也会看到这句提示，观感就是「下一关的门一开始就已经出现了」。
// 这里给出每扇门要求的实例属性，让「提示/触发」与「脚本会不会放行」一致。
//
// 为什么是手抄表而不是解析脚本：LPQ 把 getProperty 的返回值存进局部变量（avail）再判 null，
// GPQ 用的是 getIntProperty 的复合条件，静态解析抓不准——第一版按正则解析时 8 个 LPQ 门只认出
// 了 lpq5 里的 6stageclear（还是「进图置位」那一条，不是闸门）。表逐条对照源端脚本抄下来，
// 由 PqPortalGateChecks 钉住；改源端脚本时先读 portal/<script>.js 再改这里。
// 表里没有的门一律当作「没有闸门」（= 完全退回改动前的行为），不会因为漏登记把正常的门锁掉。
public static class PortalGate
{
    // script → 必须先存在的实例属性名。
    static readonly Dictionary<string,string> gates = new Dictionary<string,string>(StringComparer.Ordinal)
    {
        // scripts/portal/lpq0..7.js：第 1..8 段 stageclear。第 6 关塔顶（922010600）的 lpq5 判的是
        // 5stageclear（进图时顺带把 6stageclear 置位），所以那扇门不看 6stageclear。
        ["lpq0"] = "1stageclear", ["lpq1"] = "2stageclear", ["lpq2"] = "3stageclear", ["lpq3"] = "4stageclear",
        ["lpq4"] = "5stageclear", ["lpq5"] = "5stageclear", ["lpq6"] = "7stageclear", ["lpq7"] = "8stageclear",
        // scripts/portal/kpq0..4.js：废弃都市 PQ 同一套写法。
        ["kpq0"] = "1stageclear", ["kpq1"] = "2stageclear", ["kpq2"] = "3stageclear", ["kpq3"] = "4stageclear",
        ["kpq4"] = "5stageclear",
    };

    /// <summary>这扇门现在算不算开着（没有闸门 = 开着；闸门属性还没置位 = 关着）。</summary>
    public static bool Open(string script)
    {
        string required = Required(script);
        return required == null || NativeEventScripts.InstanceProperty(required) != null;
    }

    /// <summary>闸门要求的实例属性名；这扇门没有可识别闸门时返回 null。</summary>
    public static string Required(string script)
        => script != null && gates.TryGetValue(script, out var required) ? required : null;
}
