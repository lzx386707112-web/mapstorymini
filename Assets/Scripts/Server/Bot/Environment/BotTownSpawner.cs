using System;
using System.Collections.Generic;
using Server.Field;
using Point = Server.Bot.Environment.BotNavigationGraph.Point;

namespace Server.Bot.Environment
{
    // EnvironmentManager.spawnTownWanderers/createTownBotWithRetry。
    // 源端阻塞重试转为主线程续接；完成后才启动该城的状态机。
    public sealed class BotTownSpawner : IDisposable
    {
        readonly BotTownPresenceConfig config;
        readonly BotTownPresenceSampler sampler;
        readonly Func<int, MapRuntime> getMap;
        readonly List<BotTasks.Handle> pending = new List<BotTasks.Handle>();
        bool disposed, running;
        public BotTownSpawner(BotTownPresenceConfig config, Func<int, MapRuntime> getMap, BotTownPresenceSampler sampler = null)
        { this.config = config; this.getMap = getMap; this.sampler = sampler ?? new BotTownPresenceSampler(); }
        public void Spawn(Action completed = null)
        {
            if (disposed) throw new ObjectDisposedException(nameof(BotTownSpawner));
            if (running) throw new InvalidOperationException("Town spawning is already running");
            running = true; int townIndex = 0;
            void NextTown()
            {
                if (disposed) return;
                if (townIndex >= config.Towns.Count) { running = false; completed?.Invoke(); return; }
                var town = config.Towns[townIndex++]; int count = Math.Min(2, town.wanderers), mapId = town.MainMapId;
                if (count <= 0 || mapId <= 0) { NextTown(); return; }
                MapRuntime map;
                try { map = getMap(mapId); } catch (ArgumentException) { NextTown(); return; }
                var portal = map?.portals.Find(p => p.id == 0);
                if (portal == null) { NextTown(); return; }
                var anchor = new Point(portal.x, portal.y); var npcs = new List<Point>();
                foreach (var npc in map.npcs) npcs.Add(new Point(npc.x, npc.y));
                var spots = sampler.Sample(map, anchor, count, npcs, config.OverridesFor(mapId));
                var ids = new List<int>(); int index = 0;
                void NextBot()
                {
                    if (disposed) return;
                    if (index >= count) { BotTypeManager.SetAndStartBots(ids, BotType.TOWN_WANDERER_BOT); NextTown(); return; }
                    Point spawn = index < spots.Count ? spots[index] : new Point(anchor.x, (int)MapService.SnapToGround(mapId, anchor.x, anchor.y));
                    index++; int attempt = 0;
                    void TryCreate()
                    {
                        if (disposed) return;
                        attempt++; BotCharacter actor = null;
                        try { actor = BotGeneration.CreateBotPollReadiness(spawn.x, spawn.y, mapId, true); }
                        catch (Exception) { /* 源端记录失败并继续重试。 */ }
                        if (actor != null) { ids.Add(actor.id); NextBot(); }
                        else if (attempt < 5) pending.Add(BotTasks.Schedule(TryCreate, 200 * attempt));
                        else NextBot();
                    }
                    TryCreate();
                }
                NextBot();
            }
            NextTown();
        }
        public void Dispose()
        { disposed = true; running = false; foreach (var task in pending) task.Cancel(); pending.Clear(); }
    }
}
