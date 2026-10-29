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
        public readonly Trade.BotTradeService trades;
        public readonly Trade.BotTradeProtocol tradeProtocol;
        public readonly BotPartyService parties;
        public readonly BotPartyGrindService partyGrind;
        public readonly MarketShopService shops;
        public readonly MarketShopProtocol marketProtocol;
        // 移动事件：**只有真的有人订阅时才把包发出来**。单机宿主没有网络出口，
        // 没人订阅时整条「构包 → 转发」链都是白做（每秒几千次分配），
        // 所以这里用自定义访问器把「有没有消费者」同步给下游，而不是无条件接上。
        Action<BotCharacter, byte[]> movement;
        public event Action<BotCharacter, byte[]> Movement
        {
            add { movement += value; SyncMovementSink(); }
            remove { movement -= value; SyncMovementSink(); }
        }
        void SyncMovementSink()
        {
            Action<BotCharacter, byte[]> sink = movement != null ? (Action<BotCharacter, byte[]>)PublishMovement : null;
            BotMovementService.Broadcast = sink;
            BotMovementPlayback.Broadcast = sink;
        }
        bool disposed;
        readonly BotTasks.Handle invitationWatch;
        readonly NativeReactorScriptService reactorScripts;
        public BotRuntime(DbStore db, Social.BotMessenger messenger = null, BotPartyService parties = null)
        {
            var perf = BotPerf.Timer("BotRuntime.Ctor");
            BotClient.Init(db);
            BotTypeManager.Messenger = messenger ?? new Social.BotMessenger();
            this.parties = parties ?? new BotPartyService(db);
            invitationWatch=BotTasks.ScheduleFixedRate(()=> { this.parties.TickInvitationTimeouts(); BotTypeManager.Messenger.TickInvitationTimeouts(); trades?.TickInvitationTimeouts(); },30000,30000);
            trades = new Trade.BotTradeService(db,BotItemCatalog.Shared);
            BotTypeManager.Parties = this.parties;
            BotTypeManager.TradeService = trades;
            tradeProtocol = new Trade.BotTradeProtocol(trades, id => players.TryGetValue(id,out var player) ? player : null);
            trades.ChatOutput = (actor,text) => BotTypeManager.Output.Send(Social.BotOutput.Kind.TradeChat,actor,text,
                targetId: actor.IsBot() ? actor.trade?.partner?.actor.id ?? 0 : actor.id);
            maps = BotNavigationMapLoader.Load();
            shops = new MarketShopService(db, trades, GetMap);
            marketProtocol = new MarketShopProtocol(shops, id => players.TryGetValue(id, out var player) ? player : null);
            BotEnvironment.PopulateMarketRooms = shops.PopulateRooms;
            partyGrind = new BotPartyGrindService(this.parties,GetMap,null,db);
            BotTypeManager.PartyGrind = partyGrind;
            observers = new BotObserverTracker(() => players.Values, GetMap, BotWorldGraph.Load());
            townSpawner = new BotTownSpawner(new BotTownPresenceConfig(BotResources.Load()), TryGetMap);
            // EnvironmentManager（市场环境启动编排）的地图加载/城镇游荡钩子
            BotEnvironment.GetMap = GetMap;
            BotEnvironment.TryGetMap = TryGetMap;
            BotEnvironment.TownWanderers = () => townSpawner.Spawn();
            mapEntryResponder = new BotMapEntryResponder(BotTypeManager.Events);
            mapEntryResponder.Register();
            dispatcher = new Social.BotDispatcher(BotTypeManager.Messages, BotTypeManager.Output, CharactersOnMap,
                (bot, player) => bot.DisplayCommands(player));
            dispatcher.Start();
            BotMovementService.IsObserved = observers.IsActive;
            SyncMovementSink();                    // 没有订阅者时 Broadcast=null，下游据此跳过构包
            BotMovementService.EnterPortal = EnterPortal;
            BotMovementService.ResolveProfile = (actor,map) => BotMovementProfile.FromCharacter(actor,
                (map.fieldLimit & 2) != 0,this.parties.Members(actor.id),BotMovementService.HasSnowShoes(actor));
            BotTypeManager.RealPlayersOnMap = observers.HasRealPlayerNow;
            BotLogic.OnlinePlayers = () => players.Values;
            MapItemService.PartyOf = id => this.parties.For(id)?.id ?? -1;
            MapItemService.IsPartyMember = (ownerId, characterId) => this.parties.For(ownerId)?.members.Contains(characterId) ?? false;
            // 地面掉落与假人调度共用同一毫秒时钟（源端 TimerManager / System.currentTimeMillis 同源）
            MapItemService.Clock = () => BotScheduler.Clock;
            MapReactorService.Clock = () => BotScheduler.Clock;
            MapReactorService.Templates = MapReactorTemplate.Parse(BotWarmup.Root("Bot/ReactorTemplates"));
            MapItemService.Spawned += MapReactorService.ActivateItemReactors;
            reactorScripts = new NativeReactorScriptService(key=>MapReactorService.ScriptSource?.Invoke(key));
            reactorScripts.Message = (map,type,text)=>MapReactorService.ScriptMessage?.Invoke(map,type,text);
            reactorScripts.EventInstance = map=>MapReactorService.EventInstance?.Invoke(map);
            reactorScripts.Error = error=>MapReactorService.ScriptError?.Invoke(error);
            MapReactorService.Act = (reactor,owner)=>reactorScripts.Act(reactor,owner);
            Types.Opq.OpqOrchestrator.LeaderMapOf = id =>
            {
                var member = this.parties.Get(id);
                var party = this.parties.For(id);
                var leader = party == null ? null : this.parties.Get(party.leader);
                return member != null && leader != null && leader.online &&
                    leader.world == member.world && leader.channel == member.channel ? leader.mapId : -1;
            };
            observers.Start();
            // ≡ 源端经 BotControlController→BotControlService.requestMarketStartup 后台触发；
            //   本端口增加游戏内自动启动（solo_mapling_market_auto_start，默认 true），
            //   状态机幂等（already/running/started），失败可重试。进游戏即拉起三波环境，
            //   玩家走到自由市场时假人已在场。
            if (BotConfig.MarketAutoStartEnabled(db)) BotEnvironment.RequestMarketStartup();
            perf.Dispose();
        }
        void PublishMovement(BotCharacter actor, byte[] packet) => movement?.Invoke(actor, packet);
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
        // 非阻塞版（分帧加载用）：分片已在后台解析好就登记并返回 Ready；
        // 还在解析返回 Pending，调用方下一帧再问，主线程一帧都不堵；包里没这张图返回 Missing。
        public BotMapState TryGetMap(int id, out MapRuntime map, bool allowBlocking = false)
        {
            map = MapService.Get(id);
            if (map != null) return BotMapState.Ready;
            if (allowBlocking)
            {
                try { map = GetMap(id); } catch (ArgumentException) { map = null; }
                return map != null ? BotMapState.Ready : BotMapState.Missing;
            }
            bool ready;
            try { ready = maps.TryGeometry(id, out map); }
            catch (ArgumentException) { map = null; return BotMapState.Missing; }
            if (!ready || map == null) { map = null; return BotMapState.Pending; }
            MapService.Register(map); registeredMaps.Add(id);
            return BotMapState.Ready;
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
            bool changedMap = !players.TryGetValue(id, out var actor) || actor.mapId != mapId || actor.channel != channel || actor.world != record.world;
            if (changedMap) { actor?.trade?.Decline(); shops.Leave(id); }
            if (actor == null) { actor = new BotActor { id = id }; players.Add(id, actor); }
            int previousLevel = actor.level;
            bool knownPlayer = actor.character != null;
            if (actor.character!=null && character.tradeRevision<actor.character.tradeRevision)
            {
                var persisted=CharService.Load(BotClient.GetBotClient().db,id);
                character.inventory=persisted.inventory; record.meso=persisted.record.meso;
                character.tradeRevision=actor.character.tradeRevision;
            }
            actor.character = character; actor.name = record.name; actor.world = record.world; actor.channel = channel;
            actor.level = record.level; actor.job = record.job; actor.gender = record.gender;
            actor.skinColor = record.skincolor; actor.hair = record.hair; actor.face = record.face;
            actor.mapId = mapId; actor.x = mapX; actor.y = mapY; actor.online = true;
            parties.Bind(actor);
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
        { partyGrind.Stop(id); shops.Leave(id); if (players.TryGetValue(id, out var actor)) { actor.trade?.Decline(); actor.online = false; } parties.EndSession(id); players.Remove(id); BotTypeManager.Messenger.Leave(id); observers.Refresh(); }
        public BotActor Player(int id) => players.TryGetValue(id,out var actor) ? actor : null;
        public IEnumerable<BotActor> Players => players.Values;
        public void Drive(long nowMs)
        {
            if (disposed) return;
            BotScheduler.Clock = nowMs; BotScheduler.Drive();
            MapItemService.Tick(nowMs);
            MapReactorService.Tick(nowMs);
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            foreach (var player in new List<BotActor>(players.Values)) player.trade?.Decline();
            invitationWatch.Cancel();
            shops.Dispose(); BotEnvironment.PopulateMarketRooms = null;
            partyGrind.Dispose(); BotTypeManager.PartyGrind = null;
            parties.Clear();
            BotTypeManager.Parties = null; BotTypeManager.TradeService = null;
            dispatcher.Dispose(); mapEntryResponder.Dispose(); townSpawner.Dispose(); observers.Dispose();
            foreach (var bot in new List<BotCharacter>(BotRegistry.All().Values)) BotGeneration.RemoveBotFromServer(bot);
            BotMovementService.Shutdown(); BotTypeManager.Market.Stop(); BotTasks.Stop(); BotScheduler.Stop();
            BotTypeManager.RealPlayersOnMap = null;
            BotLogic.OnlinePlayers = null;
            MapItemService.PartyOf = null; MapItemService.IsPartyMember = null;
            MapItemService.Clock = () => 0;
            MapItemService.ClearAll();
            MapItemService.Spawned -= MapReactorService.ActivateItemReactors;
            MapReactorService.Clock = () => 0;
            MapReactorService.ClearAll();
            MapReactorService.Act = null;
            reactorScripts.Dispose();
            Types.Opq.OpqOrchestrator.LeaderMapOf = null;
            Types.Opq.OpqOrchestrator.ShutdownRun();
            BotMovementService.EnterPortal = null; BotMovementService.ResolveProfile = null;
            BotMovementService.IsObserved = null; BotMovementService.Broadcast = null; BotMovementPlayback.Broadcast = null;
            BotTypeManager.Output.Clear(); players.Clear();
            BotTypeManager.Messages.Clear();
            BotTypeManager.Ambient.Clear();
            BotTypeManager.Messenger.Clear();
            foreach (int id in registeredMaps) MapService.Unregister(id);
            registeredMaps.Clear(); movement = null; SyncMovementSink();
            // 档案册的运行期占用状态跟着会话走；档案本身（bot_profile 表）留在库里不动。
            BotDirectory.Reset();
            BotEnvironment.GetMap = null; BotEnvironment.TryGetMap = null; BotEnvironment.TownWanderers = null;
            BotEnvironment.Reset();
        }
    }
}
