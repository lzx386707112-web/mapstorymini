using System;
using System.Collections.Generic;
using Server.Field;
using Point = Server.Bot.Environment.BotNavigationGraph.Point;

namespace Server.Bot.Environment
{
    // EnvironmentManager.spawnTownWanderers/createTownBotWithRetry。
    // 源端在后台线程里阻塞重试，主线程不卡；本端口在主线程跑，所以按「一个城镇一步、
    // 一个假人一步」把整段游荡生成拆进 BotEnvironment 的分帧泵里（BotEnvironment.Post）。
    // 每个城镇要先拿地图几何——分片没解完就把这一步重新排队，绝不在主线程解 1MB JSON。
    public sealed class BotTownSpawner : IDisposable
    {
        readonly BotTownPresenceConfig config;
        readonly BotTownPresenceSampler sampler;
        readonly BotMapLoad tryMap;
        readonly List<BotTasks.Handle> pending = new List<BotTasks.Handle>();
        bool disposed, running;
        public BotTownSpawner(BotTownPresenceConfig config, BotMapLoad tryMap, BotTownPresenceSampler sampler = null)
        { this.config = config; this.tryMap = tryMap; this.sampler = sampler ?? new BotTownPresenceSampler(); }
        // 同步加载版（离线自检用）：拿不到地图就当「包里没有」，不再等后台。
        public BotTownSpawner(BotTownPresenceConfig config, Func<int, MapRuntime> getMap, BotTownPresenceSampler sampler = null)
            : this(config, (int id, out MapRuntime map, bool allowBlocking) =>
            {
                map = null;
                if (getMap == null) return BotMapState.Missing;
                try { map = getMap(id); } catch (ArgumentException) { return BotMapState.Missing; }
                return map != null ? BotMapState.Ready : BotMapState.Missing;
            }, sampler)
        { }

        public bool IsRunning => running;

        public void Spawn(Action completed = null)
        {
            if (disposed) throw new ObjectDisposedException(nameof(BotTownSpawner));
            if (running) throw new InvalidOperationException("Town spawning is already running");
            running = true;
            int townIndex = 0;

            // 一步：处理一个城镇的「家庭作业」——拿地图、采样落脚点，然后把每个假人的生成排进队尾。
            void NextTown()
            {
                if (disposed) { running = false; return; }
                if (townIndex >= config.Towns.Count) { running = false; completed?.Invoke(); return; }
                var town = config.Towns[townIndex++];
                int count = Math.Min(2, town.wanderers), mapId = town.MainMapId;
                if (count <= 0 || mapId <= 0) { BotEnvironment.Post(NextTown); return; }

                WhenMapReady(mapId, map =>
                {
                    var portal = map?.portals.Find(p => p.id == 0);
                    if (portal == null) { BotEnvironment.Post(NextTown); return; }
                    var anchor = new Point(portal.x, portal.y);
                    var npcs = new List<Point>();
                    foreach (var npc in map.npcs) npcs.Add(new Point(npc.x, npc.y));
                    var spots = sampler.Sample(map, anchor, count, npcs, config.OverridesFor(mapId));
                    var ids = new List<int>();
                    int index = 0;

                    void NextBot()
                    {
                        if (disposed) return;
                        if (index >= count) { BotTypeManager.SetAndStartBots(ids, BotType.TOWN_WANDERER_BOT); BotEnvironment.Post(NextTown); return; }
                        Point spawn = index < spots.Count ? spots[index] : new Point(anchor.x, (int)MapService.SnapToGround(mapId, anchor.x, anchor.y));
                        index++; int attempt = 0;

                        void TryCreate()
                        {
                            if (disposed) return;
                            attempt++; BotCharacter actor = null;
                            try { actor = BotGeneration.CreateBotPollReadiness(spawn.x, spawn.y, mapId, true); }
                            catch (Exception) { /* 源端记录失败并继续重试。 */ }
                            if (actor != null) { ids.Add(actor.id); BotEnvironment.Post(NextBot); }
                            else if (attempt < 5) pending.Add(BotTasks.Schedule(TryCreate, 200 * attempt));
                            else BotEnvironment.Post(NextBot);
                        }
                        TryCreate();
                    }
                    NextBot();
                });
            }
            BotEnvironment.Post(NextTown);
        }

        // 地图几何就绪才继续；分片还在后台解析就先让出这一帧。
        // 包里没有这张图 → 直接跳过（原端同样跳过）；等够 MapWaitFrames 才允许一次同步加载兜底。
        void WhenMapReady(int mapId, Action<MapRuntime> next, int attempt = 0)
        {
            if (disposed) return;
            var map = MapService.Get(mapId);
            BotMapState state = map != null ? BotMapState.Ready : BotMapState.Missing;
            if (map == null && tryMap != null)
            {
                try { state = tryMap(mapId, out var loaded, attempt >= BotEnvironment.MapWaitFrames); if (state == BotMapState.Ready) map = loaded; }
                catch (ArgumentException) { state = BotMapState.Missing; }
            }
            if (map != null) { next(map); return; }
            if (state == BotMapState.Missing) { next(null); return; }
            BotEnvironment.PostNextFrame(() => WhenMapReady(mapId, next, attempt + 1));
        }

        public void Dispose()
        { disposed = true; running = false; foreach (var task in pending) task.Cancel(); pending.Clear(); }
    }
}
