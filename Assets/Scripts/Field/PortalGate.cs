using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

// 源端的关卡门从不靠「门自己藏起来」实现隔离：LudiPQ 每张图通往下一关的 next00 是 pt=5
// （隐形，客户端连光洞都不画），闸门写在 portal/<script>.js 的 enter() 里：
//     var avail = eim.getProperty("1stageclear");
//     if (avail == null) { pi.getPlayer().dropMessage(5, "某种封印阻挡着这扇门，暂时无法进入。"); return false; }
//     pi.playPortalSound(); pi.getPlayer().changeMap(target, targetPortal);
// 也就是「关卡没清 → 只回一句封印」；清关（队长点 NPC → clearStage → setProperty）之后才放行。
//
// 移植端多出一条 HUD 提示（GameHUD「按 ↑ / W 进入传送门」），它只看脚下有没有 pt=1/2/4/5/... 的门，
// 于是站在还没清关的隐形门上也会看到这句提示，观感就是「下一关的门一开始就已经出现了」。
// 这里把脚本自己声明的闸门读出来给前端用，让「提示/触发」和「脚本会不会放行」对齐：
//   * 只认「属性存在才算开」这一种写法（getProperty("X") == null → 闸门），LPQ/KPQ 全用这个；
//   * 解析不出来（自定义门、getIntProperty 的复杂条件、脚本本体缺失）一律当作没有闸门，
//     也就是完全退回改动前的行为，绝不因为猜错而把一扇正常的门锁掉。
public static class PortalGate
{
    // script → 必须先存在的实例属性名。
    static readonly Dictionary<string,string> gates = new Dictionary<string,string>(StringComparer.Ordinal);
    // 确认过「没有可识别闸门」的脚本名，避免每帧重复解析。
    static readonly HashSet<string> plain = new HashSet<string>(StringComparer.Ordinal);
    // enter() 里的属性存在性检查。取最左一个：LPQ 第 6 关先判 5stageclear（闸门），
    // 后面那条 6stageclear == null 是「进入即置位」，不能当成闸门。
    static readonly Regex Presence = new Regex("getProperty\\s*\\(\\s*\"([A-Za-z0-9_]+)\"\\s*\\)\\s*==\\s*null", RegexOptions.Compiled);

    /// <summary>这扇门现在算不算开着（没有闸门 = 开着；闸门属性还没置位 = 关着）。</summary>
    public static bool Open(string script)
    {
        string required = Required(script);
        return required == null || NativeEventScripts.InstanceProperty(required) != null;
    }

    /// <summary>闸门要求的实例属性名；这扇门没有可识别闸门时返回 null。</summary>
    public static string Required(string script)
    {
        if (string.IsNullOrEmpty(script)) return null;
        if (gates.TryGetValue(script, out var known)) return known;
        if (plain.Contains(script)) return null;
        string required = Parse(script);
        if (required == null) { plain.Add(script); return null; }
        gates[script] = required;
        return required;
    }

    static string Parse(string script)
    {
        string source;
        try { source = ScriptSource.Load("portal", script); }
        catch (Exception) { return null; }          // 脚本名非法/资源缺失：当作没闸门
        if (string.IsNullOrEmpty(source)) return null;
        int at = source.IndexOf("function enter", StringComparison.Ordinal);
        if (at < 0) return null;
        int end = source.IndexOf("\nfunction ", at + 1, StringComparison.Ordinal);
        string body = end < 0 ? source.Substring(at) : source.Substring(at, end - at);
        var match = Presence.Match(body);
        return match.Success ? match.Groups[1].Value : null;
    }
}
