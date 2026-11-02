using System;
using System.Collections.Generic;
using MapleDash.Js;
using Server.Bot;
using UnityEngine;

// 「假人以队员身份跑 NPC 脚本」用的最小 cm / player 宿主。
//
// 为什么需要它：源端组队任务的阶段 NPC 把队员分支写在脚本里（废都 9020001 的
// gridCheck 领题 → itemQuantity 判券数 → gainItem 换 1 张通行证）。这段逻辑只在
// 「非队长」身份下才会走到，而 NpcApi.Build 造出来的 cm 一律绑死本地真实角色，
// 所以以前假人只能当观众，队员分支永远跑不到。
//
// 边界（重要）：
//   · 这里只绑定队员分支真正会用到的那一组接口，且全部作用在**假人自己的背包**上；
//   · 未绑定的成员返回一个记录日志的空操作函数（Missing），绝不抛错——假人只是陪跑，
//     任何没建模的脚本调用都不能把真人那一侧的副本流程带崩；
//   · 不写库、不改 Profile：假人背包是运行期内存（BotCharacter.inventory）。
public static class BotNpcApi
{
    static JsNumber N(double v) => new JsNumber(v);
    static JsString S(string v) => new JsString(v ?? "");
    static JsBoolean B(bool v) => JsBoolean.Of(v);
    static JsFunction Fn(Action<JsCall> body)
        => JsFunction.FromNative("", c => { body(c); return JsUndefined.Value; });
    static JsFunction Ret(Func<JsCall, JsValue> body) => JsFunction.FromNative("", body);
    static int Int(JsCall c, int i, int def = 0) => i < c.ArgCount ? JsConvert.ToInt32(c.Arg(i)) : def;
    static string Str(JsCall c, int i, string def = "") => i < c.ArgCount ? JsConvert.ToString(c.Arg(i)) : def;

    static readonly HashSet<string> reported = new HashSet<string>();

    // ——— 假人背包（源端 InventoryManipulator 的加减，只落在 BotCharacter.inventory）———

    public static int Quantity(BotCharacter bot, int itemId)
    {
        if (bot == null || itemId <= 0) return 0;
        int total = 0;
        foreach (var entry in bot.inventory)
            if (entry != null && entry.itemId == itemId) total += entry.quantity;
        return total;
    }

    /// <summary>返回操作后的持有数量。负数 = 扣除，扣不干净时按实际扣到的算（源端 gainItem 不吞负库存）。</summary>
    public static int Gain(BotCharacter bot, int itemId, int count)
    {
        if (bot == null || itemId <= 0 || count == 0) return Quantity(bot, itemId);
        if (count < 0)
        {
            int left = -count;
            for (int i = bot.inventory.Count - 1; i >= 0 && left > 0; i--)
            {
                var entry = bot.inventory[i];
                if (entry == null || entry.itemId != itemId) continue;
                int take = Math.Min(entry.quantity, left);
                entry.quantity -= take; left -= take;
                if (entry.quantity <= 0) bot.inventory.RemoveAt(i);
            }
            return Quantity(bot, itemId);
        }
        foreach (var entry in bot.inventory)
            if (entry != null && entry.itemId == itemId)
            {
                // 可叠加道具（消耗/其他）直接累加；装备类每条一份，源端也是这么落的。
                if (itemId / 1000000 != 1) { entry.quantity += count; return Quantity(bot, itemId); }
                break;
            }
        bot.inventory.Add(new InventoryEntry
        {
            itemId = itemId, inventoryType = Math.Max(1, itemId / 1000000), quantity = count,
        });
        return Quantity(bot, itemId);
    }

    /// <summary>源端 AbstractPlayerInteraction.canHold：假人背包不设格位上限，恒真。</summary>
    public static bool CanHold(int itemId, int count) => itemId > 0 && count != 0;

    // ——— cm ———

    public static JsHostObject Build(NpcSession session, BotCharacter bot)
    {
        var cm = new JsHostObject(bot);
        // 未建模的接口：记一次日志后当空操作，不让假人这一侧把脚本带崩。
        cm.Missing = key =>
        {
            if (reported.Add(key))
                Debug.LogWarning("[BotNpc] " + (bot != null ? bot.name : "?") + " 调用了未建模的 cm." + key + "（按空操作处理）");
            return JsFunction.FromNative(key, _ => JsUndefined.Value);
        };

        cm.Set("getPlayer", Ret(_ => Player(session, bot)));
        cm.Set("getMapId", Ret(_ => N(bot != null ? bot.mapId : 0)));
        cm.Set("getNpc", Ret(_ => N(session != null ? session.NpcId : 0)));
        // 假人永远不是队长：isEventLeader 在源端就是「角色 id == eim 队长 id」。
        cm.Set("isEventLeader", Ret(_ => B(false)));
        cm.Set("isLeader", Ret(_ => B(false)));
        cm.Set("isPartyLeader", Ret(_ => B(false)));
        cm.Set("getPlayerCount", Ret(_ => N(1)));
        cm.Set("getEventInstance", Ret(_ => NativeEventScripts.Instance()));
        cm.Set("getEventManager", Ret(_ => JsNull.Value));
        cm.Set("getParty", Ret(_ => JsNull.Value));

        // —— 道具（源端 itemQuantity / haveItem / gainItem）——
        cm.Set("itemQuantity", Ret(c => N(Quantity(bot, Int(c, 0)))));
        cm.Set("getItemQuantity", Ret(c => N(Quantity(bot, Int(c, 0)))));
        cm.Set("hasItem", Ret(c => B(Quantity(bot, Int(c, 0)) >= Math.Max(1, Int(c, 1, 1)))));
        cm.Set("haveItem", Ret(c => B(Quantity(bot, Int(c, 0)) >= Math.Max(1, Int(c, 1, 1)))));
        cm.Set("canHold", Ret(c => B(CanHold(Int(c, 0), Int(c, 1, 1)))));
        cm.Set("gainItem", Fn(c => Gain(bot, Int(c, 0), Int(c, 1, 1))));
        cm.Set("getItemCount", Ret(c => N(Quantity(bot, Int(c, 0)))));

        // —— 对话：只登记「脚本发了一页」，真正的推进由 PqBotNpcDriver 同步回话 ——
        JsFunction Say(NpcDialogMode mode) => Fn(c =>
        {
            if (session == null) return;
            session.LastSay = Str(c, 0);
            session.SetDialog(mode, Str(c, 0));
        });
        cm.Set("sendNext", Say(NpcDialogMode.Next));
        cm.Set("sendPrev", Say(NpcDialogMode.Prev));
        cm.Set("sendNextPrev", Say(NpcDialogMode.NextPrev));
        cm.Set("sendOk", Say(NpcDialogMode.Ok));
        cm.Set("sendYesNo", Say(NpcDialogMode.YesNo));
        cm.Set("sendSimple", Say(NpcDialogMode.Simple));
        cm.Set("sendGetNumber", Say(NpcDialogMode.GetNumber));
        cm.Set("sendStyle", Say(NpcDialogMode.Style));
        cm.Set("dispose", Fn(_ => session?.DisposeFromScript()));
        cm.Set("dropMessage", Fn(_ => { }));
        cm.Set("message", Fn(_ => { }));
        cm.Set("playPortalSound", Fn(_ => { }));
        return cm;
    }

    // ——— player ———

    public static JsValue Player(NpcSession session, BotCharacter bot)
    {
        var player = new JsHostObject(bot);
        player.Missing = key =>
        {
            if (reported.Add("player." + key))
                Debug.LogWarning("[BotNpc] " + (bot != null ? bot.name : "?") + " 调用了未建模的 player." + key + "（按空操作处理）");
            return JsFunction.FromNative(key, _ => JsUndefined.Value);
        };
        player.Set("getId", Ret(_ => N(bot != null ? bot.id : 0)));
        player.Set("getName", Ret(_ => S(bot != null ? bot.name : "")));
        player.Set("getMapId", Ret(_ => N(bot != null ? bot.mapId : 0)));
        player.Set("getLevel", Ret(_ => N(bot != null ? bot.level : 1)));
        player.Set("getJob", Ret(_ => N(bot != null ? bot.job : 0)));
        player.Set("getJobId", Ret(_ => N(bot != null ? bot.job : 0)));
        player.Set("isLeader", Ret(_ => B(false)));
        player.Set("isLoggedInWorld", Ret(_ => B(bot != null && bot.online)));
        player.Set("getEventInstance", Ret(_ => NativeEventScripts.Instance()));
        player.Set("getPosition", Ret(_ =>
        {
            var point = new JsHostObject(null);
            point.Set("getX", Ret(_ => N(bot != null ? bot.x : 0)));
            point.Set("getY", Ret(_ => N(bot != null ? bot.y : 0)));
            point.Set("x", N(bot != null ? bot.x : 0));
            point.Set("y", N(bot != null ? bot.y : 0));
            return point;
        }));
        player.Set("dropMessage", Fn(_ => { }));
        player.Set("message", Fn(_ => { }));
        player.Set("changeMap", Fn(_ => { }));   // 假人换图由 PqRun.WarpBot 负责
        player.Set("warp", Fn(_ => { }));
        player.Set("getAbstractPlayerInteraction", Ret(_ =>
            session != null && session.CmHost != null ? session.CmHost : (JsValue)FallbackCm(bot)));
        return player;
    }

    // 没有会话时的 cm 兜底（实例登记用的常驻 player 宿主会走到这里）：
    // 源端 eim.unregisterPlayer 会拿 getAbstractPlayerInteraction 去清独占道具
    // （KerningPQ.setEventExclusives([4001007,4001008])），返回 null 会让收尾抛错。
    static readonly Dictionary<int, JsHostObject> fallbackCm = new Dictionary<int, JsHostObject>();
    static JsHostObject FallbackCm(BotCharacter bot)
    {
        if (bot == null) return null;
        if (fallbackCm.TryGetValue(bot.id, out var cm) && cm != null) return cm;
        fallbackCm[bot.id] = cm = Build(null, bot);
        cm.Set("removeAll", Fn(c => Gain(bot, Int(c, 0), -Quantity(bot, Int(c, 0)))));
        return cm;
    }
}
