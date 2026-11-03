using System;
using System.Collections.Generic;
using Server.Bot;
using Server.Bot.Environment;
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
        // FieldDirector 每帧都会调 Bind（= 每帧清缓存再重建一个 BotActorLooter）。
        // 现在野外挂机每 400ms 就要用一次，这里按 runtime 变化短路。
        if (ReferenceEquals(runtime, bots)) return;
        runtime = bots;
        looters.Clear();
    }

    BotActorLooter Looter(BotCharacter bot)
    {
        if (bot == null || runtime == null) return null;
        if (looters.TryGetValue(bot.id, out var looter) && looter != null) return looter;
        // 伴侣挂机一开就是半小时，looter 缓存会随下线的假人越积越多，定期清一次。
        TrimLooters();
        looter = new BotActorLooter(bot, runtime.trades, runtime.parties,
            new BotLootHooks { NeedQuestItem = (_, __) => true });
        looters[bot.id] = looter;
        return looter;
    }

    void TrimLooters()
    {
        if (looters.Count < 64) return;
        var stale = new List<int>();
        foreach (var pair in looters)
            if (BotRegistry.Get(pair.Key) == null) stale.Add(pair.Key);
        foreach (int id in stale) looters.Remove(id);
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
        // 副本图每实例各一份，掉落物挂在 Scope 负 id 下；假人的 mapId 是真实图 id。
        int mapId = NativeEventScripts.ResolveMap(bot.mapId)?.Scope ?? bot.mapId;
        List<MapItem> items;
        try { items = MapItemService.ItemsInRange(mapId, bot.x, bot.y, (double)range * range); }
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
        if (picked < max) picked += PickLegacy(bot, looter, itemId, range, max - picked);
        return picked;
    }

    /// <summary>
    /// 老式地面掉落：FieldDirector.DropLoot 是直接 GroundItem.Spawn（MapItem 为 null），
    /// 没有服务端实体，上面的 MapItemService 范围查询看不到。普通怪（含组队任务的怪）
    /// 掉的券全在这条链上，不补这半段，假人打死怪也收不到券。
    /// 只动「没有 bound」的那些，权威掉落仍走 MapItemPickup。
    /// </summary>
    int PickLegacy(BotCharacter bot, BotActorLooter looter, int itemId, float range, int max)
    {
        if (max <= 0) return 0;
        // bot.x/bot.y 与 GroundItem.targetX/targetY 都是地图像素坐标（不是 Unity 单位），直接比。
        int mx = (int)bot.x, my = (int)bot.y;
        long rangeSq = (long)(range * range);
        int picked = 0;
        foreach (var view in GroundItem.All.ToArray())
        {
            if (picked >= max) break;
            if (view == null || view.bound != null || view.itemId != itemId || view.amount <= 0) continue;
            int want = Math.Min(view.amount, max - picked);
            var entry = new InventoryEntry
            {
                itemId = itemId, inventoryType = Math.Max(1, itemId / 1000000), quantity = want,
            };
            if (!looter.HasSpace(entry)) continue;     // 装不下就别从地上拿走
            int got;
            try { got = view.TakeForBot(mx, my, rangeSq, want, () => PickupTarget(bot)); }
            catch (Exception) { got = 0; }
            if (got <= 0) continue;
            entry.quantity = got;
            looter.AddFromDrop(entry);
            picked += got;
        }
        return picked;
    }

    /// <summary>附近一件可捡掉落的落点（地图像素坐标）与距离。</summary>
    public struct LootSpot { public bool found; public int x, y; public float distance; }

    /// <summary>
    /// 找附近最近的可捡掉落（道具 + 金币，权威链与老式链都看）。
    ///
    /// 野外挂机用：假人打完怪只会站着，怪吐出来的东西没人收（以前只有组队任务里
    /// 「按 itemId 找券」那条路，普通挂机完全没有拾取）。这里给一个不限 itemId 的查询。
    /// floorTolerance 只认同层的掉落 —— 掉在别的平台上的东西不追，不然为了捡一件
    /// 会顺着绳子爬到地图另一头，看起来更像穿模。
    /// </summary>
    public LootSpot Nearest(BotCharacter bot, float searchRange = 340f, int floorTolerance = 140)
    {
        var spot = new LootSpot { distance = float.MaxValue };
        if (bot == null || bot.mapId <= 0 || searchRange <= 0) return spot;
        int mx = (int)bot.x, my = (int)bot.y;
        float best = searchRange;
        void Consider(int x, int y)
        {
            int dx = x - mx, dy = y - my;
            if (Math.Abs(dy) > floorTolerance) return;
            float d = (float)Math.Sqrt((double)dx * dx + (double)dy * dy);
            if (d > best) return;
            best = d; spot.found = true; spot.x = x; spot.y = y;
        }
        int mapId = NativeEventScripts.ResolveMap(bot.mapId)?.Scope ?? bot.mapId;
        List<MapItem> items;
        try { items = MapItemService.DroppedItems(mapId); }
        catch (Exception) { items = null; }
        if (items != null)
            foreach (var item in items)
            {
                if (item == null || item.pickedUp) continue;
                if (item.IsMeso ? item.meso <= 0 : item.item == null) continue;
                Consider((int)item.x, (int)item.y);
            }
        foreach (var view in GroundItem.All)
        {
            if (view == null || !view.Ready) continue;
            Consider(view.MapX, view.MapY);
        }
        spot.distance = best;
        return spot;
    }

    /// <summary>
    /// 捡附近任何东西（不限 itemId，金币也捡），返回件数。
    /// 与 PickNearby 的区别：那个是组队任务「只收某一种券、还要卡数量」的专用口径，
    /// 这个就是野外挂机的「把脚下的东西收干净」。
    /// </summary>
    public int PickAny(BotCharacter bot, float range = 60f, int max = 2, bool meso = true)
    {
        if (bot == null || bot.mapId <= 0 || max <= 0 || range <= 0) return 0;
        // 野外伴侣代玩家拾取：动画目标是假人，入包/金币/提示仍复用真人链。
        var leader = runtime?.partyGrind.Leader(bot.id);
        if (leader == null || leader.id != GameSession.CharacterId || leader.mapId != bot.mapId ||
            !runtime.partyGrind.SameParty(bot, leader)) return 0;
        int picked = 0;
        long rangeSq = (long)(range * range);
        foreach (var view in GroundItem.All.ToArray())
        {
            if (picked >= max) break;
            if (view == null || (view.itemId == 0 && !meso)) continue;
            if (view.PickupForCompanion(bot, rangeSq)) picked++;
        }
        return picked;
    }

    internal static Vector2? PickupTarget(BotCharacter bot)
        => bot != null ? GameLoop.I?.field?.GetComponent<BotField>()?.PickupTarget(bot.id) : null;

    /// <summary>
    /// 地上还有这个道具、但不在拾取半径里：朝最近的那件走过去。返回 true = 这一拍在赶路。
    /// 两套掉落都要看：权威掉落（MapItemService，挂 Scope 图 id）与老式掉落（GroundItem，无实体）。
    /// </summary>
    public bool WalkTo(BotCharacter bot, int itemId, float stopRange = 60f, float searchRange = float.MaxValue)
    {
        if (bot == null || itemId <= 0 || bot.mapId <= 0) return false;
        // bot.x/bot.y、MapItem.x/y、GroundItem 的落点都是地图像素坐标，别乘 PPU。
        int mx = (int)bot.x, my = (int)bot.y;
        int bestX = 0, bestY = 0;
        float best = searchRange;
        bool found = false;
        int mapId = NativeEventScripts.ResolveMap(bot.mapId)?.Scope ?? bot.mapId;
        List<MapItem> items;
        try { items = MapItemService.DroppedItems(mapId); }
        catch (Exception) { items = null; }
        if (items != null)
            foreach (var item in items)
            {
                if (item == null || item.pickedUp || item.IsMeso || item.ItemId != itemId) continue;
                float d = Math.Abs(item.x - mx) + Math.Abs(item.y - my) * .6f;
                if (d < best) { found = true; best = d; bestX = (int)item.x; bestY = (int)item.y; }
            }
        foreach (var view in GroundItem.All)
        {
            if (view == null || !view.Ready || view.itemId != itemId) continue;
            int vx = view.MapX, vy = view.MapY;
            float d = Math.Abs(vx - mx) + Math.Abs(vy - my) * .6f;
            if (d < best) { found = true; best = d; bestX = vx; bestY = vy; }
        }
        if (!found || best <= stopRange) return false;
        try
        {
            if (!BotMovementService.PrepareNavigation(bot)) return false;
            int groundY = (int)MapService.SnapToGround(bot.mapId, bestX, bestY);
            if (!BotMovementService.IsMovingToward(bot, bestX, groundY))
                BotMovementService.Move(bot, bestX, groundY);
            return true;
        }
        catch (Exception) { return false; }
    }
}
