using System;
using System.Collections.Generic;
using Server.Bot.Environment;
using Server.Field;

namespace Server.Bot
{
    // 本地服务端会话接线；真人位置由FieldDirector同步，AI保持纯后端。
    public sealed class BotRuntime : IDisposable
    {
        readonly BotNavigationMapLoader maps;
        readonly Dictionary<int, BotActor> players = new Dictionary<int, BotActor>();
        readonly HashSet<int> registeredMaps = new HashSet<int>();
        public readonly BotObserverTracker observers;
        public readonly BotTownSpawner townSpawner;
        public readonly BotMapEntryResponder mapEntryResponder;
        public event Action<BotCharacter, byte[]> Movement;
        bool disposed;
        public BotRuntime(DbStore db)
        {
            BotClient.Init(db);
            maps = BotNavigationMapLoader.Load();
            observers = new BotObserverTracker(() => players.Values, GetMap, BotWorldGraph.Load());
            townSpawner = new BotTownSpawner(new BotTownPresenceConfig(BotResources.Load()), GetMap);
            mapEntryResponder = new BotMapEntryResponder(BotTypeManager.Events);
            mapEntryResponder.Register();
            BotMovementService.IsObserved = observers.IsActive;
            BotMovementService.Broadcast = PublishMovement;
            BotMovementService.EnterPortal = EnterPortal;
            BotMovementPlayback.Broadcast = PublishMovement;
            BotTypeManager.RealPlayersOnMap = observers.HasRealPlayerNow;
            observers.Start();
        }
        void PublishMovement(BotCharacter actor, byte[] packet) => Movement?.Invoke(actor, packet);
        public MapRuntime GetMap(int id)
        {
            var map = MapService.Get(id);
            if (map != null) return map;
            map = maps.Geometry(id); MapService.Register(map); registeredMaps.Add(id); return map;
        }
        // GCPortals.enter按目标门名查找，找不到回退portal0；不自行执行额外脚本传送。
        public bool EnterPortal(BotCharacter actor, MapPortal portal)
        {
            if (actor == null || portal == null || !portal.enabled) return false;
            try
            {
                var destination = GetMap(portal.targetMap);
                var exit = destination.portals.Find(p => p.name == portal.targetName) ?? destination.portals.Find(p => p.id == 0);
                if (exit == null) return false;
                actor.x = exit.x; actor.y = exit.y;
                BotRegistry.EnterMap(actor, destination.mapId);
                return true;
            }
            catch (ArgumentException) { return false; }
        }
        public BotActor SynchronizePlayer(FullCharacter character, int mapId, float mapX, float mapY)
        {
            if (disposed) throw new ObjectDisposedException(nameof(BotRuntime));
            if (character?.record == null) throw new ArgumentNullException(nameof(character));
            var record = character.record; int id = checked((int)record.id);
            bool changedMap = !players.TryGetValue(id, out var actor) || actor.mapId != mapId;
            if (actor == null) { actor = new BotActor { id = id }; players.Add(id, actor); }
            int previousLevel = actor.level;
            bool knownPlayer = actor.character != null;
            actor.character = character; actor.name = record.name; actor.world = record.world;
            actor.level = record.level; actor.job = record.job; actor.gender = record.gender;
            actor.skinColor = record.skincolor; actor.hair = record.hair; actor.face = record.face;
            actor.mapId = mapId; actor.x = mapX; actor.y = mapY; actor.online = true;
            if (changedMap)
            {
                GetMap(mapId); observers.MarkObservedNow(mapId); observers.Refresh();
                // BotAutoSpawner.onPlayerEnterMap只确保standalone，不擅自按地图创建假人。
                BotClient.EnsureStandaloneClient(BotClient.GetBotClient()?.db);
                BotTypeManager.Events.Publish(new Events.GameEvent(actor, Events.EventType.MAP_ENTERED));
            }
            if (knownPlayer && actor.level > previousLevel) BotTypeManager.Events.Publish(Events.EventFactory.LevelUp(actor));
            return actor;
        }
        public void RemovePlayer(int id) { players.Remove(id); observers.Refresh(); }
        public IEnumerable<BotActor> Players => players.Values;
        public void Drive(long nowMs)
        {
            if (disposed) return;
            BotScheduler.Clock = nowMs; BotScheduler.Drive();
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            mapEntryResponder.Dispose(); townSpawner.Dispose(); observers.Dispose();
            foreach (var bot in new List<BotCharacter>(BotRegistry.All().Values)) BotGeneration.RemoveBotFromServer(bot);
            BotMovementService.Shutdown(); BotTypeManager.Market.Stop(); BotTasks.Stop(); BotScheduler.Stop();
            BotTypeManager.RealPlayersOnMap = null;
            BotMovementService.EnterPortal = null;
            BotMovementService.IsObserved = null; BotMovementService.Broadcast = null; BotMovementPlayback.Broadcast = null;
            BotTypeManager.Output.Clear(); players.Clear();
            foreach (int id in registeredMaps) MapService.Unregister(id);
            registeredMaps.Clear(); Movement = null;
        }
    }
}
