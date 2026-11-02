using System;
using System.Collections.Generic;
using Server.Bot;
using Server.Field;
using UnityEngine;

// 假人自己去捡地面掉落。
//
// 为什么需要：组队任务里「队员捡优惠券 / 通行证」是关卡脚本判定的一部分
// （废都第一关：队员捡满 N 张 4001007 才换得到 1 张 4001008 交给队长）。
// 以前移植端只给本地角色接了 GroundItemBridge，假人打死怪只能让掉落物躺在地上，
// 于是队员的「攒券 → 换证」永远凑不出材料，只能靠 PqDirector.Give 凭空发。
//
// 这里复用真人那条完全相同的结算链：MapItemService 的范围查询 + MapItemPickup 的
// 决策树 + BotActorLooter 的入包（TradeItemRules.Receive），只是把 looter 换成假人。
public sealed class BotGroundPickup
{
    BotRuntime runtime;
    readonly Dictionary<int, BotActorLooter> looters = new Dictionary<int, BotActorLooter>();

    public void Bind(BotRuntime bots)
    {
        runtime = bots;
        looters.Clear();
    }

    BotActorLooter Looter(BotCharacter bot)
    {
        if (bot == null || runtime == null) return null;
        if (looters.TryGetValue(bot.id, out var looter) && looter != null) return looter;
        looter = new BotActorLooter(bot, runtime.trades, runtime.parties,
            new BotLootHooks { NeedQuestItem = (_, __) => true });
        looters[bot.id] = looter;
        return looter;
    }

    /// <summary>
    /// 捡脚下这个道具：范围内每捡到一件返回 +1（捡不到返回 0）。
    /// range 是像素半径（内部按源端 getMapObjectsInRange 的平方距离比较）。
    /// max 是本次最多捡几件——组队任务里 NPC 用 `itemQuantity == answer` 严格相等判定，
    /// 多捡一张反而交不掉，所以调用方会把「还差几张」传进来。
    /// </summary>
    public int PickNearby(BotCharacter bot, int itemId, float range = 90f, int max = int.MaxValue)
    {
        if (bot == null || itemId <= 0 || bot.mapId <= 0 || max <= 0) return 0;
        var looter = Looter(bot);
        if (looter == null) return 0;
        List<MapItem> items;
        try { items = MapItemService.ItemsInRange(bot.mapId, bot.x, bot.y, (double)range * range); }
        catch (Exception) { return 0; }
        int picked = 0;
        long now = (long)(Time.realtimeSinceStartup * 1000);
        foreach (var item in items)
        {
            if (picked >= max) break;
            if (item == null || item.pickedUp || item.IsMeso || item.ItemId != itemId) continue;
            bool ok;
            try { ok = MapItemPickup.PickupItem(item, looter, now); }
            catch (Exception) { ok = false; }
            if (ok) picked++;
        }
        return picked;
    }
}
