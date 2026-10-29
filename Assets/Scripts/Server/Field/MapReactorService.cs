// 反应堆服务：按 mapId 管理运行时实例，负责受击 / 破坏 / 掉落 / 重生。
// 对齐源端 org.gms.server.maps.MapleMap 的反应堆部分：
//   getReactors()/getReactorByOid()  -> Get(mapId) / Find(mapId, objectId)
//   CustomReactor.hitReactor + OPQ 显式掉落的四击路径（不是完整 WZ 事件状态机）
// 纯 C#，不引用 UnityEngine。
// 时钟沿用 MapItemService.Clock / MapSpawner.Clock 范式：由外部注入毫秒时钟，禁止 UnityEngine.Time。
// 本类不主动阻塞：重生不排线程定时器，而是在 Get/Tick 时按 respawnAt 惰性结算，
// 因此即使宿主没有调用 Tick，查询方（假人/前端）也总能看到正确的 alive 状态。
using System;
using System.Collections.Generic;

namespace Server.Field
{
    public static class MapReactorService
    {
        /// <summary>毫秒时钟，由外部注入（对齐 MapItemService.Clock / BotScheduler.Clock 范式）。</summary>
        public static Func<long> Clock = () => 0;
        public static Dictionary<int,MapReactorTemplate> Templates = new Dictionary<int,MapReactorTemplate>();
        public static Action<MapReactor,int> Act;
        public static Func<string,string> ScriptSource;
        public static Action<int,int,string> ScriptMessage;
        public static Func<int,MapleDash.Js.JsValue> EventInstance;
        public static Action<Exception> ScriptError;
        sealed class ItemActivation { public MapReactor reactor; public MapItem item; public long at; }
        static readonly List<ItemActivation> itemActivations = new List<ItemActivation>();

        // MapleMap.activateItemReactors: one exact-quantity stack, Java rectangle, 5000ms.
        public static void ActivateItemReactors(MapItem item)
        {
            if(item==null || item.item==null || item.pickedUp) return;
            foreach(var reactor in Get(item.mapId))
            {
                var template=reactor.template;
                var events=template?.Events(reactor.state);
                if(!reactor.alive || events==null || events.Count==0 || events[0].type!=100 ||
                    reactor.eventState<0 || reactor.eventState>=events.Count) continue;
                var trigger=events[reactor.eventState];
                if(trigger.item!=item.ItemId || trigger.quantity!=item.Quantity ||
                    item.x<reactor.x+template.left || item.x>=reactor.x+template.right ||
                    item.y<reactor.y+template.top || item.y>=reactor.y+template.bottom) continue;
                itemActivations.Add(new ItemActivation { reactor=reactor,item=item,at=Now()+5000 });
                break;
            }
        }

        static void ResolveItemActivations(long now)
        {
            var due=itemActivations.FindAll(a=>a.at<=now);
            foreach(var activation in due)
            {
                itemActivations.Remove(activation);
                var reactor=activation.reactor;var item=activation.item;
                if(!ReferenceEquals(Find(reactor.mapId,reactor.objectId),reactor) || !reactor.alive ||
                    !reactor.shouldCollect || item.pickedUp || !ReferenceEquals(MapItemService.Find(item.mapId,item.objectId),item)) continue;
                var events=reactor.template?.Events(reactor.state);
                if(events==null || events.Count==0 || events[0].type!=100) continue;
                MapItemService.PickItemDrop(item.mapId,item,0);
                reactor.shouldCollect=false;
                // Match the shipped Reactor.hitReactor, including its second next-state lookup
                // after assigning state. Do not silently replace it with upstream Odin semantics.
                reactor.previousState=reactor.state; reactor.visualRevision++; reactor.lastHitAt=now;
                reactor.state=events[0].next;
                int next=reactor.template.Next(reactor.state,0);
                bool end=next<reactor.state;
                Changed?.Invoke(reactor);
                if(end || next==reactor.state) Act?.Invoke(reactor,item.ownerCharacterId);
                if(!end) reactor.shouldCollect=true;
            }
        }

        /// <summary>源端 AbstractMapObject 的对象编号池；与 MapItemService 的掉落物编号分属两段，避免撞号。</summary>
        static int nextObjectId = 1000000000;

        static readonly Dictionary<int, List<MapReactor>> byMap = new Dictionary<int, List<MapReactor>>();

        /// <summary>前端渲染钩子：反应堆状态变化（含破坏、重生）。</summary>
        public static event Action<MapReactor> Changed;

        public static long Now() => Clock != null ? Clock() : 0;

        // ——————————————————— 查询 / 惰性构建 ———————————————————

        /// <summary>
        /// 取一张图的全部反应堆实例。第一次调用时从 MapRuntime.reactors 生成实例并分配 objectId
        /// （源端 MapleMap 在 loadReactors 时分配，这里的时机等价）。
        /// 地图还没加载时返回空表且**不缓存**，等地图就绪后再来一次会正常构建。
        /// </summary>
        public static List<MapReactor> Get(int mapId)
        {
            if (!byMap.TryGetValue(mapId, out var list))
            {
                var runtime = MapService.Get(mapId);
                if (runtime == null) return new List<MapReactor>();
                list = Build(mapId, runtime);
                byMap[mapId] = list;
            }
            ResolveRespawns(mapId, list, Now());
            return list;
        }

        static List<MapReactor> Build(int mapId, MapRuntime runtime)
        {
            var list = new List<MapReactor>(runtime.reactors.Count);
            foreach (var spawn in runtime.reactors)
            {
                list.Add(new MapReactor
                {
                    template = Templates.TryGetValue(spawn.id,out var template) ? template : null,
                    mapId = mapId,
                    id = spawn.id,
                    objectId = nextObjectId++,
                    x = spawn.x,
                    y = spawn.y,
                    name = spawn.name ?? "",
                    maxState = spawn.maxState > 0 ? spawn.maxState : 4,
                    respawnSeconds = spawn.RespawnSeconds,
                });
            }
            return list;
        }

        /// <summary>源端 getReactorByOid：objectId 查实例，找不到返回 null。</summary>
        public static MapReactor Find(int mapId, int objectId)
        {
            foreach (var reactor in Get(mapId)) if (reactor.objectId == objectId) return reactor;
            return null;
        }

        // CustomReactor.spawnReactor: dynamic instances share the map's authoritative pool.
        public static MapReactor Spawn(int mapId, int id, int x, int y, int maxState = 4)
        {
            var list = Get(mapId);
            if (!byMap.ContainsKey(mapId)) byMap[mapId] = list;
            var reactor = new MapReactor { mapId = mapId, id = id, objectId = nextObjectId++,
                x = x, y = y, maxState = maxState, lastHitAt = Now(),
                template = Templates.TryGetValue(id, out var template) ? template : null };
            list.Add(reactor);
            Changed?.Invoke(reactor);
            return reactor;
        }

        /// <summary>按 WZ 模板 id 统计还活着的数量（OPQ 用它判断云堆/箱子是否清完）。</summary>
        public static int AliveCount(int mapId, int reactorId)
        {
            int count = 0;
            foreach (var reactor in Get(mapId)) if (reactor.id == reactorId && reactor.Hittable) count++;
            return count;
        }

        // ——————————————————— 受击 / 破坏 / 重生 ———————————————————

        // CustomReactor.hitReactor only increments state and broadcasts. In particular,
        // reaching state 4 does not remove the reactor or arrange a respawn.
        public static MapReactor ForceHit(int mapId, int objectId)
        {
            var reactor = Find(mapId, objectId);
            if (reactor == null) return null;
            reactor.previousState = reactor.state;
            reactor.state++;
            reactor.visualRevision++;
            reactor.lastHitAt = Now();
            Changed?.Invoke(reactor);
            return reactor;
        }

        /// <summary>
        /// 源端 OPQ CustomReactor 四击路径：状态 +1；到上限后按显式传入表投放。
        /// 掉落表由调用方给（源端反应堆掉落来自 WZ reward 表，本工程没有导出，
        /// 所以这里不编造物品——OPQ 传 4001063 / 4001056-4001062）。
        /// 返回受击后的实例（已破坏也能拿到 alive=false 的结果）；找不到实例返回 null。
        /// </summary>
        public static MapReactor Hit(int mapId, int objectId, IList<InventoryEntry> drops = null,
            int ownerId = 0, int partyId = -1, bool playerDrop = false)
        {
            var reactor = Find(mapId, objectId);
            if (reactor == null) return null;
            // 已破坏 / 已到上限：源端会直接忽略（重复命中不能再吐一次掉落）。
            if (!reactor.Hittable) return reactor;

            long now = Now();
            reactor.previousState = reactor.state; reactor.visualRevision++;
            reactor.state++;
            reactor.lastHitAt = now;
            if (reactor.state >= reactor.maxState)
                Destroy(mapId, reactor, drops, ownerId, partyId, playerDrop);
            else
                Changed?.Invoke(reactor);
            return reactor;
        }

        /// <summary>源端 setState(4) + 掉落 + 排重生：把实例置为已破坏并投放 drops。</summary>
        public static void Destroy(int mapId, MapReactor reactor, IList<InventoryEntry> drops = null,
            int ownerId = 0, int partyId = -1, bool playerDrop = false)
        {
            if (reactor == null || !reactor.alive) return;
            reactor.alive = false;
            reactor.state = reactor.maxState;
            if (drops != null)
            {
                foreach (var item in drops)
                {
                    if (item == null) continue;
                    // 源端反应堆掉落走 spawnItemDrop(dropper=reactor, owner, item, pos, ...)
                    MapItemService.SpawnItemDrop(mapId, reactor.objectId, ownerId, partyId, item,
                        reactor.x, reactor.y, false, playerDrop);
                }
            }
            reactor.respawnAt = reactor.respawnSeconds > 0
                ? Now() + reactor.respawnSeconds * 1000L
                : 0L;
            Changed?.Invoke(reactor);
        }

        /// <summary>源端 Reactor.forceRespawn / setState(0)：状态归零、重新可打。</summary>
        public static void Respawn(int mapId, MapReactor reactor)
        {
            if (reactor == null) return;
            reactor.alive = true;
            reactor.state = 0;
            reactor.previousState = -1; reactor.visualRevision++;
            reactor.lastHitAt = Now();
            reactor.respawnAt = 0;
            Changed?.Invoke(reactor);
        }

        /// <summary>宿主可选的统一驱动入口（与 MapItemService.Tick 同一范式）；不调用也不会漏重生。</summary>
        public static void Tick(long now)
        {
            foreach (var pair in byMap) ResolveRespawns(pair.Key, pair.Value, now);
            ResolveItemActivations(now);
        }

        static void ResolveRespawns(int mapId, List<MapReactor> list, long now)
        {
            if (now <= 0) return;
            for (int i = 0; i < list.Count; i++)
            {
                var reactor = list[i];
                if (reactor.alive || reactor.respawnAt <= 0 || now < reactor.respawnAt) continue;
                Respawn(mapId, reactor);
            }
        }

        // ——————————————————— 生命周期 ———————————————————

        /// <summary>地图卸载/换图：丢弃该图实例（下次访问会按当前 MapRuntime 重建）。</summary>
        public static void Clear(int mapId)
        { byMap.Remove(mapId);itemActivations.RemoveAll(a=>a.reactor.mapId==mapId); }

        public static void ClearAll() { byMap.Clear();itemActivations.Clear(); }
    }
}
