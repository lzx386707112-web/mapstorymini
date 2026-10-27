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
        readonly Social.BotDispatcher dispatcher;
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
            dispatcher = new Social.BotDispatcher(BotTypeManager.Messages, BotTypeManager.Output, CharactersOnMap,
                (bot, player) => bot.DisplayCommands(player));
            dispatcher.Start();
            BotMovementService.IsObserved = observers.IsActive;
            BotMovementService.Broadcast = PublishMovement;
            BotMovementService.EnterPortal = EnterPortal;
            BotMovementPlayback.Broadcast = PublishMovement;
            BotTypeManager.RealPlayersOnMap = observers.HasRealPlayerNow;
            observers.Start();
        }
        void PublishMovement(BotCharacter actor, byte[] packet) => Movement?.Invoke(actor, packet);
        IEnumerable<BotActor> CharactersOnMap(Social.ChatMessage message)
        {
            var actors = new List<BotActor>();
            foreach (var player in players.Values)
                if (player.mapId == message.mapId && player.world == message.sender.world && player.channel == message.sender.channel) actors.Add(player);
            foreach (var actor in BotRegistry.OnMap(message.mapId))
                if (actor.world == message.sender.world && actor.channel == message.sender.channel) actors.Add(actor);
            actors.Sort((a, b) => a.mapEntryOrder.CompareTo(b.mapEntryOrder));
            return actors;
        }
        // 本地transport适配：只接收已通过聊天校验的全图发言，不把私聊/组队消息送到地图假人。
        public bool ReceiveChat(int playerId, string text)
        {
            if (disposed || string.IsNullOrWhiteSpace(text) || text[0] == '/' || !players.TryGetValue(playerId, out var player)) return false;
            BotTypeManager.Messages.Add(new Social.ChatMessage(player, text), Social.QueueType.PRIMARY);
            BotTypeManager.Events.Publish(Events.EventFactory.ChatGeneral(player, text));
            return true;
        }
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
        public BotActor SynchronizePlayer(FullCharacter character, int mapId, float mapX, float mapY, int channel = 1)
        {
            if (disposed) throw new ObjectDisposedException(nameof(BotRuntime));
            if (character?.record == null) throw new ArgumentNullException(nameof(character));
            var record = character.record; int id = checked((int)record.id);
            bool changedMap = !players.TryGetValue(id, out var actor) || actor.mapId != mapId || actor.channel != channel;
            if (actor == null) { actor = new BotActor { id = id }; players.Add(id, actor); }
            int previousLevel = actor.level;
            bool knownPlayer = actor.character != null;
            actor.character = character; actor.name = record.name; actor.world = record.world; actor.channel = channel;
            actor.level = record.level; actor.job = record.job; actor.gender = record.gender;
            actor.skinColor = record.skincolor; actor.hair = record.hair; actor.face = record.face;
            actor.mapId = mapId; actor.x = mapX; actor.y = mapY; actor.online = true;
            if (changedMap)
            {
                actor.MarkMapEntry();
                GetMap(mapId); observers.MarkObservedNow(mapId); observers.Refresh();
                // BotAutoSpawner.onPlayerEnterMap只确保standalone，不擅自按地图创建假人。
                BotClient.EnsureStandaloneClient(BotClient.GetBotClient()?.db);
                BotTypeManager.Events.Publish(new Events.GameEvent(actor, Events.EventType.MAP_ENTERED));
            }
            if (knownPlayer && actor.level > previousLevel) BotTypeManager.Events.Publish(Events.EventFactory.LevelUp(actor));
            return actor;
        }
        public void RemovePlayer(int id)
        { if (players.TryGetValue(id, out var actor)) actor.online = false; players.Remove(id); observers.Refresh(); }
        public IEnumerable<BotActor> Players => players.Values;
        public void Drive(long nowMs)
        {
            if (disposed) return;
            BotScheduler.Clock = nowMs; BotScheduler.Drive();
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            dispatcher.Dispose(); mapEntryResponder.Dispose(); townSpawner.Dispose(); observers.Dispose();
            foreach (var bot in new List<BotCharacter>(BotRegistry.All().Values)) BotGeneration.RemoveBotFromServer(bot);
            BotMovementService.Shutdown(); BotTypeManager.Market.Stop(); BotTasks.Stop(); BotScheduler.Stop();
            BotTypeManager.RealPlayersOnMap = null;
            BotMovementService.EnterPortal = null;
            BotMovementService.IsObserved = null; BotMovementService.Broadcast = null; BotMovementPlayback.Broadcast = null;
            BotTypeManager.Output.Clear(); players.Clear();
            BotTypeManager.Messages.Clear();
            BotTypeManager.Ambient.Clear();
            foreach (int id in registeredMaps) MapService.Unregister(id);
            registeredMaps.Clear(); Movement = null;
        }
    }
}
