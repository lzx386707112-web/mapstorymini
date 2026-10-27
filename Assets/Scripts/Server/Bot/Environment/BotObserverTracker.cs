using System;
using System.Collections.Generic;
using Server.Field;

namespace Server.Bot.Environment
{
    public sealed class BotObserverTracker : IDisposable
    {
        readonly Func<IEnumerable<BotActor>> players;
        readonly Func<int, MapRuntime> maps;
        readonly BotWorldGraph graph;
        readonly Dictionary<int, long> stickyUntil = new Dictionary<int, long>(), forced = new Dictionary<int, long>();
        HashSet<int> full = new HashSet<int>(), halo = new HashSet<int>(), active = new HashSet<int>();
        BotTasks.Handle task;
        public BotObserverTracker(Func<IEnumerable<BotActor>> players, Func<int, MapRuntime> maps, BotWorldGraph graph)
        { this.players = players; this.maps = maps; this.graph = graph; }
        public bool Started => task != null && !task.Cancelled;
        public void Start() { if (!Started) task = BotTasks.ScheduleFixedRate(SafeRefresh, 0, 2000); }
        public void Dispose() { task?.Cancel(); task = null; }
        public void MarkObservedNow(int id) => forced[id] = BotScheduler.Now() + 2000;
        bool Forced(int id)
        {
            if (!forced.TryGetValue(id, out long until)) return false;
            if (until < BotScheduler.Now()) { forced.Remove(id); return false; }
            return true;
        }
        public bool IsFull(int id) => full.Contains(id) || Forced(id);
        public bool IsHalo(int id) => halo.Contains(id);
        public bool IsActive(int id) => active.Contains(id) || Forced(id);
        public int FullCount => full.Count;
        public int HaloCount => halo.Count;
        public int ActiveCount => active.Count;
        public bool HasRealPlayerNow(int id)
        { foreach (var player in players()) if (player != null && !BotHelpers.IsBot(player) && player.mapId == id) return true; return false; }
        void SafeRefresh() { try { Refresh(); } catch (Exception) { } }
        public void Refresh()
        {
            var newFull = new HashSet<int>(); var real = new List<BotActor>();
            foreach (var player in players())
                if (player != null && !BotHelpers.IsBot(player)) { newFull.Add(player.mapId); real.Add(player); }
            var newHalo = new HashSet<int>();
            if (graph != null) foreach (var player in real)
            {
                var map = maps(player.mapId); if (map == null) continue;
                var walkable = new HashSet<int>(graph.PortalNeighbors(player.mapId));
                foreach (var portal in map.portals)
                {
                    int target = portal.targetMap;
                    double dx = player.x - portal.x, dy = player.y - portal.y;
                    if (target > 0 && !newFull.Contains(target) && walkable.Contains(target) && dx * dx + dy * dy <= 122500)
                        newHalo.Add(target);
                }
            }
            long now = BotScheduler.Now(), until = now + 4000;
            foreach (int id in newFull) stickyUntil[id] = until;
            foreach (int id in newHalo) stickyUntil[id] = until;
            foreach (int id in new List<int>(stickyUntil.Keys)) if (stickyUntil[id] <= now) stickyUntil.Remove(id);
            active = new HashSet<int>(stickyUntil.Keys); full = newFull; halo = newHalo;
        }
    }
}
