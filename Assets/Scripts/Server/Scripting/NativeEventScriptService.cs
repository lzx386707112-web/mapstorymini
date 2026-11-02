using System;
using System.Collections.Generic;
using MapleDash.Js;

// EventManager/EventInstanceManager: original scripts share one engine per manager.
// Map creation and packet delivery belong to the caller, never a fake shared field.
public sealed class NativeEventScriptService
{
    readonly Func<string,string> source;
    readonly Func<long> clock;
    readonly Action<JsEngine> configure;
    readonly Dictionary<string,Manager> managers=new Dictionary<string,Manager>();
    readonly Dictionary<int,Instance> membership=new Dictionary<int,Instance>();
    readonly List<Task> tasks=new List<Task>();
    public Func<Instance,int,JsValue> InstanceMap;
    public Func<JsValue> ChannelServer;
    public Func<int, int> TransportationTime;
    // EventManager.getBossTime(BossTime)（EventManager.java:1271）= 入参 × boss_respawn_mob_time_rate。
    // 默认恒等，由前端按 game_config 注入实际倍率（后端不直接读库）。
    public Func<long, long> BossTime = value => value;
    public Action<Instance,int?> Clock;
    public Action<Instance> ReleaseMaps;
    // 组队任务：把实例里的某个玩家送到目标图（前端接 GameLoop.I.field.WarpTo）。
    public Action<Instance,int,JsValue> WarpPlayer;
    // linkToNextStage 设置「下一阶段门脚本名」（前端接 FieldDirector 的 portal script）。
    public Action<Instance,string,int> LinkPortal;
    // showWrongEffect（答错特效）。
    public Action<Instance,int> WrongEffect;
    public Action<JsValue> AwardClearQuestPoints;
    public Action<Instance,int,JsValue,JsValue> SpawnNpc;
    // EventInstanceManager.showClearEffect：源端是「clear 特效 + Party1/Clear 音效」，
    // 有 gate 时再加 environmentChange。移植端由客户端宿主接成可见的完成提示。
    public Action<Instance,int,bool> ClearEffect;
    public Action<Exception> Error;
    // 队伍版 startInstance 要判断「队长是不是本地真实角色」（源端 EventManager.java:738 用
    // party.getLeader().getPlayer() 当 leader；移植端只有本地角色有真实 Character）。未注入时
    // 不做该判断，保持纯引擎可测。
    public Func<int> LocalPlayerId;
    // V1.7.0__create_game_config.sql: event_lobby_delay is 10 seconds.
    public long LobbyDelayMillis=10000;
    public int MaxEventLevels=8;
    public Func<int,int> RewardIndex=count=>new Random().Next(count);
    // EventManager.getMonster(mobId) = LifeFactory.getMonster：返回一个「还没进图」的怪物对象。
    // ScriptMonster 在客户端 Field 层，Server 侧不能直接引用，由宿主注入（NativeEventScripts）。
    public Func<int,JsValue> MonsterFactory;
    // eim.getMapFactory().getMap(id)：warp 目标图的脚本对象（NpcApi.BuildWarpMap 在客户端
    // Npc 层，Server 侧不能直接引用），由宿主注入；未注入时返回 null（离线自检不触发该路径）。
    public Func<int,JsValue> WarpMapFactory;
    public NativeEventScriptService(Func<string,string> source,Func<long> clock,Action<JsEngine> configure=null)
    { this.source=source;this.clock=clock;this.configure=configure; }
    public Manager GetManager(string name)
    {
        if (managers.TryGetValue(name,out var found)) return found;
        string text=source("event/"+name);
        if (string.IsNullOrEmpty(text)) return null;
        var manager=new Manager(this,name,text);
        managers.Add(name,manager);
        try { manager.Initialize();return manager; }
        catch { manager.Cancel();managers.Remove(name);throw; }
    }
    public Instance GetInstance(int character) => membership.TryGetValue(character,out var instance) ? instance : null;
    public void Tick()
    {
        // A callback may enqueue another delay-0 task. Execute that on the next tick,
        // rather than looping forever inside one frame.
        long now=clock();var due=tasks.FindAll(t=>!t.Cancelled && t.At<=now);
        due.Sort((a,b)=>a.At.CompareTo(b.At));
        foreach(var task in due)
        {
            tasks.Remove(task);
            if(task.Cancelled || task.Manager.Cancelled || task.Owner?.Disposed==true) continue;
            task.Cancelled=true;
            try { task.Action(); } catch(Exception error) { if(Error==null)throw;Error(error); }
        }
        tasks.RemoveAll(t=>t.Cancelled || t.Manager.Cancelled || t.Owner?.Disposed==true);
    }
    public void Clear()
    {
        foreach(var manager in new List<Manager>(managers.Values)) manager.Cancel();
        managers.Clear();membership.Clear();tasks.Clear();
    }
    Task Schedule(Manager manager,Instance owner,long delay,Action action)
    {
        var task=new Task {Manager=manager,Owner=owner,At=clock()+Math.Max(0,delay),Action=action};
        tasks.Add(task);return task;
    }
    sealed class Task
    {
        public Manager Manager;public Instance Owner;public long At;public Action Action;public bool Cancelled;
        public JsValue Host()
        {
            var host=new JsHostObject(this);
            host.Set("cancel",JsFunction.FromNative("cancel",c=>{bool pending=!Cancelled;Cancelled=true;return JsBoolean.Of(pending);}));
            host.Set("isCancelled",JsFunction.FromNative("isCancelled",c=>JsBoolean.Of(Cancelled)));
            return host;
        }
    }
    static JsValue Void=JsUndefined.Value;
    static void Bind(JsHostObject host,string name,Func<JsCall,JsValue> call) => host.Set(name,JsFunction.FromNative(name,call));
    static string Text(JsCall c,int index=0) => JsConvert.ToString(c.Arg(index));
    static int Number(JsCall c,int index=0) => JsConvert.ToInt32(c.Arg(index));
    static long Long(JsCall c,int index=0) => (long)JsConvert.ToNumber(c.Arg(index));
    static JsHostObject Strict(object value,string kind)
    {
        var host=new JsHostObject(value);
        host.Missing=key=>throw new JsRuntimeException("Unmigrated "+kind+" API: "+key);
        return host;
    }
    static void Properties(JsHostObject host,Dictionary<string,string> properties)
    {
        Bind(host,"setProperty",c=>{properties[Text(c)]=Text(c,1);return Void;});
        Bind(host,"setIntProperty",c=>{properties[Text(c)]=Number(c,1).ToString(System.Globalization.CultureInfo.InvariantCulture);return Void;});
        Bind(host,"getProperty",c=>properties.TryGetValue(Text(c),out var value) ? (JsValue)new JsString(value) : JsNull.Value);
        // EventInstanceManager.java:793 —— 缺省返回 0（props.getProperty(key) 为 null 时按 "0" 解析），
        // 不是抛异常。露希妲 event/LucidBattle.js 的 playerEntry 第一句就是
        // `getIntProperty("eliminated_"+id)`，键还没建；返回 0 才是源端行为。
        Bind(host,"getIntProperty",c=>properties.TryGetValue(Text(c),out var value) && int.TryParse(value,out int number)
            ? (JsValue)new JsNumber(number) : (JsValue)new JsNumber(0));
    }
    static int PlayerId(JsValue player,JsEngine engine)
    {
        var method=engine.Interp.GetMember(player,"getId");
        return JsConvert.ToInt32(engine.Interp.Call(method,player,Array.Empty<JsValue>()));
    }
    static JsValue Players(IEnumerable<JsValue> players)
    {
        var array=new JsArray(players);
        array.Set("get",JsFunction.FromNative("get",c=>array.GetIndex(Number(c))));
        array.Set("size",JsFunction.FromNative("size",c=>new JsNumber(array.Length)));
        array.Set("isEmpty",JsFunction.FromNative("isEmpty",c=>JsBoolean.Of(array.Length==0)));
        return array;
    }
    public sealed class Manager
    {
        readonly NativeEventScriptService service;
        readonly JsEngine engine;
        readonly Dictionary<string,Instance> instances=new Dictionary<string,Instance>();
        readonly Dictionary<string,string> properties=new Dictionary<string,string>();
        public readonly string Name;
        public readonly JsHostObject Host;
        public bool Cancelled {get;private set;}
        public Manager(NativeEventScriptService service,string name,string text)
        {
            this.service=service;Name=name;engine=new JsEngine(text,"event/"+name);
            Host=Strict(this,"event manager");Properties(Host,properties);
            Bind(Host,"getName",c=>new JsString(Name));
            Bind(Host,"getTransportationTime",c=>new JsNumber(service.TransportationTime?.Invoke(JsConvert.ToInt32(c.Arg(0))) ?? throw new JsRuntimeException("Event transportation time is not configured")));
            // 野外 BOSS 脚本每次都调 em.getBossTime(BossTime*60*1000) 再 em.schedule 回去
            Bind(Host,"getBossTime",c=>new JsNumber(service.BossTime(Long(c,0))));
            Bind(Host,"getChannelServer",c=>service.ChannelServer?.Invoke() ?? throw new JsRuntimeException("Event channel server is not configured"));
            Bind(Host,"newInstance",c=>NewInstance(Text(c)).Host);
            Bind(Host,"getInstance",c=>instances.TryGetValue(Text(c),out var eim) ? (JsValue)eim.Host : JsNull.Value);
            Bind(Host,"getInstances",c=>{var values=new List<JsValue>();foreach(var eim in instances.Values)values.Add(eim.Host);return Players(values);});
            Bind(Host,"disposeInstance",c=>{if(instances.TryGetValue(Text(c),out var eim))Remove(eim);return Void;});
            Bind(Host,"schedule",c=>
            {
                var owner=c.ArgCount>2 ? (c.Arg(1) as JsHostObject)?.Host as Instance : null;
                return service.Schedule(this,owner,Long(c,c.ArgCount>2?2:1),()=>Call(Text(c),owner?.Host ?? (JsValue)JsNull.Value)).Host();
            });
            Bind(Host,"scheduleAtTimestamp",c=>service.Schedule(this,null,Long(c,1)-service.clock(),()=>Call(Text(c),JsNull.Value)).Host());
            Bind(Host,"startInstance",c=>
            {
                if(c.ArgCount==1)
                {
                    // EventManager.java:531 startInstance(Expedition)：远征队版。
                    if(c.Arg(0) is JsHostObject single && single.Host is Expedition)
                        return JsBoolean.Of(StartExpedition(c.Arg(0))!=null);
                    // 单参对象没有 getId 又挂着 getLeader（Party 的成员）就走队伍版；其余明确报缺失，
                    // 不能当玩家去取 id。
                    if(c.Arg(0) is JsHostObject unknown && !unknown.Members.Has("getId"))
                        throw new JsRuntimeException("Unmigrated event startInstance overload: "+Name);
                    return JsBoolean.Of(Start(c.Arg(0))!=null);
                }
                // EventManager.java:715/726/808 —— 队伍版：startInstance([lobby,] party, map[, difficulty])。
                // 怪物公园 mPark_in00.js 与多数 PQ 的组队分支走这条，不是 (lobby, player) 那种数字重载。
                if(IsParty(c.Arg(0)))
                    return JsBoolean.Of(StartParty(c.Arg(0),c.Arg(1),c.ArgCount>2?Number(c,2):1)!=null);
                // EventManager.java:634/646: explicit lobby, character, leader, difficulty.
                if(c.ArgCount==2 && c.Arg(0) is JsNumber)
                    return JsBoolean.Of(Start(c.Arg(1),Number(c,0))!=null);
                // EventManager.java:541/552 startInstance(lobby, expedition[, leader])。
                if(c.ArgCount>=2 && c.Arg(0) is JsNumber && c.Arg(1) is JsHostObject expeditionHost && expeditionHost.Host is Expedition)
                    return JsBoolean.Of(StartExpedition(c.Arg(1),Number(c,0),c.ArgCount>2?c.Arg(2):null)!=null);
                if(c.ArgCount==4 && c.Arg(0) is JsNumber && IsParty(c.Arg(1)))
                    return JsBoolean.Of(StartParty(c.Arg(1),c.Arg(2),Number(c,3),Number(c,0))!=null);
                if(c.ArgCount==4 && c.Arg(0) is JsNumber)
                    return JsBoolean.Of(Start(c.Arg(1),Number(c,0),c.Arg(2),Number(c,3))!=null);
                throw new JsRuntimeException("Unmigrated event startInstance overload");
            });
            // EventManager.java:994 getEligibleParty(Party)：资格判定不在 Java 侧，而是把
            // party.getPartyMembersOnline()（List<PartyCharacter>）交给事件脚本自己的
            // getEligibleParty(party) 算等级/人数，结果写回 party 后再返回给 NPC。
            // 四个组队任务的招募 NPC（9020000 Lakelis / 2040034 / 2133000 / 2094000）第一句
            // 就是 `em.getEligibleParty(cm.getParty())`；漏绑定会抛
            // "Unmigrated event manager API: getEligibleParty"，玩家看到的正是
            // 「对话脚本出错（详见日志）」。
            Bind(Host,"getEligibleParty",c=>
            {
                var party=c.Arg(0);
                if(party is JsNull || party is JsUndefined)return new JsArray();
                JsValue members;
                try { members=CallMember(party,"getPartyMembersOnline"); }
                catch(Exception) { return new JsArray(); }
                try
                {
                    // 脚本里 return Java.to(eligible, PartyCharacter[])；移植端集合就是 JS 数组，
                    // Java.to 是恒等转换，所以这里拿到的就是判好的名单。
                    var eligible=Call("getEligibleParty",members);
                    return eligible is JsArray ? eligible : (JsValue)new JsArray();
                }
                catch(Exception error)
                {
                    // EventManager.java:1006 也是只记日志不抛：脚本这次算不出来就按「没有合格
                    // 队员」处理，让 NPC 走「人数不足/条件不符」提示，而不是整个对话崩掉。
                    service.Error?.Invoke(error);
                    return new JsArray();
                }
            });
            // EventManager.getMonster(mobId)：LifeFactory.getMonster 的实例侧入口。
            // PiratePQ 的波次怪是「em.getMonster(id) → eim.registerMonster → map 刷出来」。
            Bind(Host,"getMonster",c=>service.MonsterFactory!=null
                ? service.MonsterFactory(Number(c))
                : throw new JsRuntimeException("Event monster factory is not configured"));
        }
        internal void Initialize()
        {
            service.configure?.Invoke(engine);engine.Bind("em",Host);engine.Run();Call("init");
        }
        internal JsValue Call(string method,params JsValue[] args)
            => !Cancelled && engine.HasFunction(method) ? engine.Call(method,args) : JsNull.Value;
        public Instance NewInstance(string name)
        {
            if(Cancelled)throw new JsRuntimeException("Disposed event manager: "+Name);
            if(instances.ContainsKey(name))throw new JsRuntimeException("Event instance already exists: "+name);
            var instance=new Instance(this,service,name);instances.Add(name,instance);return instance;
        }
        // EventManager.java:634/646 的 lobby 选择：同一事件内 lobby 号唯一，requestedLobby<0 表示自动挑。
        int PickLobby(int requestedLobby)
        {
            int max=engine.HasFunction("getMaxLobbies") ? JsConvert.ToInt32(Call("getMaxLobbies")) : 8;
            int limit=Math.Min(8,max);
            int lobby=requestedLobby<0?0:requestedLobby;
            while(lobby<limit)
            {
                bool used=false;foreach(var instance in instances.Values)if(instance.Lobby==lobby)used=true;
                if(!used)break;
                if(requestedLobby>=0)return -1;
                lobby++;
            }
            return lobby>=limit ? -1 : lobby;
        }
        public Instance Start(JsValue player,int requestedLobby=-1,JsValue leader=null,int difficulty=1)
        {
            if(Cancelled)return null;
            if(requestedLobby < -1)return null;
            int lobby=PickLobby(requestedLobby);
            if(lobby<0)return null;
            var before=new HashSet<string>(instances.Keys);
            try
            {
                var result=Call("setup",new JsNumber(difficulty),new JsNumber(lobby));
                var instance=(result as JsHostObject)?.Host as Instance;
                if(instance==null || instance.manager!=this)throw new JsRuntimeException("Event setup did not return its instance: "+Name);
                instance.Lobby=lobby;instance.Leader=PlayerId(leader ?? player,engine);
                instance.Register(player);instance.Start();return instance;
            }
            catch
            {
                foreach(var entry in new List<Instance>(instances.Values))if(!before.Contains(entry.Name))entry.Dispose();
                throw;
            }
        }
        // EventManager.java:531-605 startInstance(Expedition) → startInstance(-1, exped)
        // → startInstance(-1, exped, exped.getLeader())，顺序是：
        //   选 lobby → setup → setLeader → exped.start() → registerExpedition → startEvent(afterSetup)
        // registerExpedition（EventInstanceManager.java:401-414）只登记「还在招募图上」的成员；
        // exped.start() 必须在 afterSetup 之前，否则脚本里 isInProgress() 会读到招募中。
        public Instance StartExpedition(JsValue expedition,int requestedLobby=-1,JsValue leader=null)
        {
            if(Cancelled || !((expedition as JsHostObject)?.Host is Expedition exped))return null;
            int lobby=PickLobby(requestedLobby);
            if(lobby<0)return null;
            var leaderValue=leader ?? CallMember(expedition,"getLeader");
            var before=new HashSet<string>(instances.Keys);
            try
            {
                var result=Call("setup",new JsNumber(1),new JsNumber(lobby));
                var instance=(result as JsHostObject)?.Host as Instance;
                if(instance==null || instance.manager!=this)throw new JsRuntimeException("Event setup did not return its instance: "+Name);
                instance.Lobby=lobby;instance.Leader=exped.LeaderId;
                exped.Start();
                instance.Exped=exped;
                if(leaderValue!=null && !(leaderValue is JsNull) && !(leaderValue is JsUndefined)
                    && JsConvert.ToInt32(CallMember(leaderValue,"getMapId"))==exped.StartMapId)
                    instance.Register(leaderValue);
                instance.Start();
                return instance;
            }
            catch
            {
                foreach(var entry in new List<Instance>(instances.Values))if(!before.Contains(entry.Name))entry.Dispose();
                throw;
            }
        }
        // EventManager.java:715/738 startInstance(Party, MapleMap)：队长必须是真实角色，
        // 然后 registerParty(party, map) 只登记「还在招募图上」的合格成员。
        // 移植端唯一的真实角色是本地玩家，所以队长不是本地角色时没有可注册的人，返回 false
        // 让脚本按源端的「没有空闲房间」提示收尾，而不是伪造一支远端队伍。
        public Instance StartParty(JsValue party,JsValue map,int difficulty,int requestedLobby=-1)
        {
            if(Cancelled || party is JsNull || party is JsUndefined || map is JsNull)return null;
            var leader=CallMember(party,"getLeader");
            if(leader is JsNull || leader is JsUndefined)return null;
            int leaderId=PlayerId(leader,engine);
            if(service.LocalPlayerId!=null && service.LocalPlayerId()!=leaderId)return null;
            int recruit=JsConvert.ToInt32(CallMember(map,"getId"));
            if(JsConvert.ToInt32(CallMember(leader,"getMapId"))!=recruit)return null;
            return Start(leader,requestedLobby,leader,difficulty);
        }
        public void Cancel()
        {
            if(Cancelled)return;
            try { Call("cancelSchedule",JsNull.Value); }
            finally
            {
                foreach(var instance in new List<Instance>(instances.Values))instance.Dispose();
                Cancelled=true;properties.Clear();
            }
        }
        internal int Id(JsValue player) => PlayerId(player,engine);
        // 实例侧要给玩家发经验/传送，需要同一个解释器句柄（与 manager.CallMember 同源）。
        internal JsInterpreter InterpOf => engine.Interp;
        // 队伍对象与角色对象都是 JS 宿主：靠 Party.getLeaderId 区分（EventManager 的重载签名不同）。
        // 必须只看宿主自己显式绑定的成员：未迁移成员的兜底会返回一个「调用才抛」的函数，
        // 用 GetMember 探测会把任何角色都误判成队伍（9390220 的 startInstance(-1,player,player,1)）。
        bool IsParty(JsValue value)
            => value is JsHostObject host && host.Members.Has("getLeaderId");
        internal JsValue CallMember(JsValue value,string name,params JsValue[] args)
            =>engine.Interp.Call(engine.Interp.GetMember(value,name),value,args);
        // 源端 EventManager.disposeInstance 只是把实例从事件表里摘掉，eim 与它的 MapleMap 交给 GC；
        // 移植端的实例地图是全局注册的运行态资源（MapService/反应堆/掉落池），必须显式回收，
        // 否则打一场副本就漏六张图。只有实例里已经没人了才回收，免得把还站在图里的玩家送到
        // 一张被注销的地图上。
        internal void Remove(Instance instance) => service.Schedule(this,null,service.LobbyDelayMillis,()=>
        {
            if(instances.TryGetValue(instance.Name,out var current) && current==instance)instances.Remove(instance.Name);
            if(!instance.Disposed && instance.PlayerCount==0)instance.Dispose();
        });
    }
    public sealed class Instance
    {
        internal readonly Manager manager;
        readonly NativeEventScriptService service;
        readonly Dictionary<int,JsValue> players=new Dictionary<int,JsValue>();
        readonly Dictionary<string,string> properties=new Dictionary<string,string>();
        readonly Dictionary<int,JsValue> maps=new Dictionary<int,JsValue>();
        readonly HashSet<JsValue> monsters=new HashSet<JsValue>();
        // EventInstanceManager.java:77 `killCount`：脚本 `monsterValue(mobId)` 返回的积分按角色累加，
        // 由 `getKillCount(chr)` 读回（TreasurePQ / AreaBoss* / 各 PQ 拿它算奖励档位）。
        readonly Dictionary<int,int> killCount=new Dictionary<int,int>();
        // EventInstanceManager.playerGrid：KPQ 第一关用它记住「这个队员拿到的是第几道题」。
        readonly Dictionary<int,int> playerGrid=new Dictionary<int,int>();
        // EventInstanceManager.onMapClearExp / onMapClearMeso：setEventClearStageExp 写入，
        // getClearStageBonus 按「阶段号从 1 开始」读回（KPQ / LudiPQ 的阶段经验）。
        readonly List<int> onMapClearExp=new List<int>();
        readonly List<int> onMapClearMeso=new List<int>();
        readonly List<int> exclusiveItems=new List<int>();
        sealed class Reward {public List<int> Items,Quantities;public int Exp;}
        readonly Dictionary<int,Reward> rewards=new Dictionary<int,Reward>();
        Task timer;
        long timerStarted,eventTime;
        bool disposing;
        public readonly string Name;
        public readonly JsHostObject Host;
        public bool Disposed {get;private set;}
        public bool Started {get;private set;}
        public bool Cleared {get;private set;}
        public int Leader {get;internal set;}
        public int Lobby {get;internal set;}=-1;
        // EventInstanceManager.java:92 `expedition`：registerExpedition 挂上的远征队，
        // monsterKilled 时用它记 BOSS 战绩，dispose 时替它摘频道表。
        public Expedition Exped {get;internal set;}
        public int PlayerCount=>players.Count;
        // 原生宿主（SeedTowerCompat 等）读实例属性用：源端 eim.getProperty 的直读版。
        public string Property(string key) => properties.TryGetValue(key,out var value) ? value : null;
        // 原生宿主写实例属性用（LucidBossCompat 暴怒失败复活 P2 后把 phase 改回 2）。
        public void SetIntProperty(string key,int value)
            => properties[key] = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        internal Instance(Manager manager,NativeEventScriptService service,string name)
        {
            this.manager=manager;this.service=service;Name=name;
            Host=Strict(this,"event instance");Properties(Host,properties);
            Bind(Host,"getName",c=>new JsString(Name));
            Bind(Host,"getChannelServer",c=>service.ChannelServer?.Invoke() ?? throw new JsRuntimeException("Event channel server is not configured"));
            Bind(Host,"getEm",c=>manager.Host);
            Bind(Host,"getEventManager",c=>manager.Host);
            Bind(Host,"spawnNpc",c=>
            {
                if(service.SpawnNpc==null)throw new JsRuntimeException("Event NPC spawning is not configured");
                service.SpawnNpc(this,Number(c),c.Arg(1),c.Arg(2));return Void;
            });
            Bind(Host,"invokeScriptFunction",c=>
            {
                var args=new JsValue[Math.Max(0,c.ArgCount-1)];for(int i=0;i<args.Length;i++)args[i]=c.Arg(i+1);
                return Invoke(Text(c),args);
            });
            Bind(Host,"setExclusiveItems",c=>{exclusiveItems.AddRange(Integers(c.Arg(0)));return Void;});
            Bind(Host,"setEventRewards",c=>
            {
                bool levelFirst=c.Arg(0) is JsNumber;int level=levelFirst?Number(c):1,offset=levelFirst?1:0;
                if(level<1 || level>service.MaxEventLevels)return Void;
                rewards[level]=new Reward {Items=Integers(c.Arg(offset)),Quantities=Integers(c.Arg(offset+1)),
                    Exp=c.ArgCount>offset+2?Number(c,offset+2):0};return Void;
            });
            Bind(Host,"giveEventReward",c=>JsBoolean.Of(GiveReward(c.Arg(0),c.ArgCount>1?Number(c,1):1)));
            Bind(Host,"getPlayers",c=>Players(players.Values));
            Bind(Host,"getPlayerCount",c=>new JsNumber(players.Count));
            Bind(Host,"getPlayerById",c=>players.TryGetValue(Number(c),out var p)?p:JsNull.Value);
            Bind(Host,"getLeaderId",c=>new JsNumber(Leader));
            // EventInstanceManager.java:628 getKillCount(Character)。
            Bind(Host,"getKillCount",c=>new JsNumber(killCount.TryGetValue(manager.Id(c.Arg(0)),out int killed) ? killed : 0));
            Bind(Host,"isLeader",c=>JsBoolean.Of(manager.Id(c.Arg(0))==Leader));
            Bind(Host,"registerPlayer",c=>{Register(c.Arg(0),c.ArgCount<2 || JsConvert.ToBoolean(c.Arg(1)));return Void;});
            Bind(Host,"unregisterPlayer",c=>{Unregister(c.Arg(0));return Void;});
            Bind(Host,"exitPlayer",c=>{Unregister(c.Arg(0));Invoke("playerExit",Host,c.Arg(0));return Void;});
            Bind(Host,"dispose",c=>{Dispose();return Void;});
            Bind(Host,"isDisposed",c=>JsBoolean.Of(Disposed));
            Bind(Host,"isEventDisposed",c=>JsBoolean.Of(Disposed));
            Bind(Host,"isEventCleared",c=>JsBoolean.Of(Cleared));
            Bind(Host,"setEventCleared",c=>{SetCleared();return Void;});
            // EventInstanceManager.java:1293-1329：四个重载最终都是
            // broadcastMessage(showEffect("quest/party/clear") + playSound("Party1/Clear"))，
            // hasGate 时再 environmentChange(mapObj,newState)。地图侧只保留 mapId 语义。
            Bind(Host,"showClearEffect",c=>
            {
                bool gate=false;int mapId=int.MinValue;
                foreach(var arg in c.Args)
                    if(arg is JsBoolean flag)gate=flag.Value;
                    else if(arg is JsNumber number)mapId=(int)number.Value;
                if(mapId==int.MinValue)mapId=Leader!=0 && players.TryGetValue(Leader,out var leader)
                    ? JsConvert.ToInt32(manager.CallMember(leader,"getMapId")) : int.MinValue;
                service.ClearEffect?.Invoke(this,mapId,gate);return Void;
            });
            // EventInstanceManager.java:1185-1194：清关后只看「离场且剩 ≤1 人」；
            // 未清关时「离场的是队长」直接算缺人，否则按 minPlayers 比。
            Bind(Host,"isEventTeamLackingNow",c=>JsBoolean.Of(Cleared
                ? JsConvert.ToBoolean(c.Arg(0)) && PlayerCount<=1
                : (JsConvert.ToBoolean(c.Arg(0)) && manager.Id(c.Arg(2))==Leader) || PlayerCount<=Number(c,1)));
            // EventInstanceManager.java:1176-1183：远征队不看队长也不看 minPlayers
            // （源码注释原话：expeditions don't need to have neither the leader nor meet
            // the minimum requirement inside the event），两个分支都只判 ≤1 人。
            Bind(Host,"isExpeditionTeamLackingNow",c=>JsBoolean.Of(Cleared
                ? JsConvert.ToBoolean(c.Arg(0)) && PlayerCount<=1
                : PlayerCount<=1));
            // EventInstanceManager.java:1165-1174。isEventTeamLeaderOn() 是「队长还在实例里」。
            Bind(Host,"checkEventTeamLacking",c=>JsBoolean.Of(Cleared && PlayerCount>1
                ? false
                : (!Cleared && JsConvert.ToBoolean(c.Arg(0)) && !LeaderOnline()) || PlayerCount<Number(c,1)));
            Bind(Host,"isEventTeamTogether",c=>JsBoolean.Of(TeamTogether()));
            Bind(Host,"dropMessage",c=>
            {
                // Use a snapshot: a player callback may leave/dispose the instance.
                foreach(var player in new List<JsValue>(players.Values))
                    manager.CallMember(player,"dropMessage",c.Arg(0),c.Arg(1));
                return Void;
            });
            Bind(Host,"startEvent",c=>{Start();return Void;});
            Bind(Host,"schedule",c=>{if(!Disposed)service.Schedule(manager,this,Long(c,1),()=>Invoke(Text(c),Host));return Void;});
            Bind(Host,"startEventTimer",c=>{StartTimer(Long(c));return Void;});
            Bind(Host,"restartEventTimer",c=>{StopTimer();StartTimer(Long(c));return Void;});
            Bind(Host,"stopEventTimer",c=>{StopTimer();return Void;});
            Bind(Host,"getTimeLeft",c=>new JsNumber(eventTime-(service.clock()-timerStarted)));
            Bind(Host,"isTimerStarted",c=>JsBoolean.Of(eventTime>0 && timerStarted>0));
            Bind(Host,"getInstanceMap",c=>GetMap(Number(c)));
            Bind(Host,"getMapInstance",c=>GetMap(Number(c)));
            // eim.getEm()：EventInstanceManager.getEm() 返回创建它的 EventManager
            // （KerningTrain.js:31 `eim.getEm().getChannelServer().getMapFactory().getMap(...)`）。
            Bind(Host,"getEm",c=>manager.Host);
            // eim.getMapFactory()：源端委托 channel.getMapFactory()，与
            // em.getChannelServer().getMapFactory() 是同一个全局地图工厂（KerningTrain.js:32-33）。
            Bind(Host,"getMapFactory",c=>
            {
                var factory=new JsHostObject(null);
                factory.Set("getMap",JsFunction.FromNative("getMap",c=>service.WarpMapFactory!=null ? service.WarpMapFactory(Number(c)) : JsNull.Value));
                return factory;
            });
            // ——— 组队任务（KPQ / LudiPQ / EllinPQ / PiratePQ）用到的实例接口 ———
            // 这些接口此前没迁移，四个副本的阶段 NPC 一调用就抛「Unmigrated」，
            // 表现为「点 NPC 提示对话脚本出错」。语义逐条对齐 EventInstanceManager.java。
            // isEventLeader(Character)：EventInstanceManager.java:847 `chr.getId() == getLeaderId()`。
            Bind(Host,"isEventLeader",c=>JsBoolean.Of(IsPlayer(c.Arg(0)) && manager.Id(c.Arg(0))==Leader));
            // clearPQ()：EventInstanceManager.java:827 只是把脚本的 clearPQ(eim) 调起来，
            // 真正的停表/置通关/传送都在事件脚本里做。
            Bind(Host,"clearPQ",c=>{Invoke("clearPQ",Host);return Void;});
            // warpEventTeam([warpFrom,] warpTo)：EventInstanceManager.java:1220/1230。
            Bind(Host,"warpEventTeam",c=>
            {
                int from=c.ArgCount>1?Number(c,0):int.MinValue;
                int to=c.ArgCount>1?Number(c,1):Number(c,0);
                WarpTeam(from,to);return Void;
            });
            // 到点刷新的落点版本（EventInstanceManager.java:1238/1248）：两位参数都是「目标图 + sp」。
            Bind(Host,"warpEventTeamToMapSpawnPoint",c=>
            {
                int from=c.ArgCount>2?Number(c,0):int.MinValue;
                int to=c.ArgCount>2?Number(c,1):Number(c,0);
                WarpTeam(from,to);return Void;
            });
            // linkToNextStage(stage,eventFamily,mapId)：EventInstanceManager.java:1354
            // = 先发本阶段经验/金币，再把这张图 next00 门的脚本名改成 eventFamily+(stage-1)。
            // 移植端的阶段推进由 PqRun 直接传送（见 Server/Bot/Types/Pq/PqRun.cs），
            // 所以这里保留奖励语义即可；门脚本名的设置通过回调交给前端地图。
            Bind(Host,"linkToNextStage",c=>
            {
                GiveStageReward(Number(c,0));
                service.LinkPortal?.Invoke(this,Text(c,1)+(Number(c,0)-1),Number(c,2));
                return Void;
            });
            Bind(Host,"linkPortalToScript",c=>
            {
                GiveStageReward(Number(c,0));
                service.LinkPortal?.Invoke(this,Text(c,2),Number(c,3));return Void;
            });
            // grid*：KPQ 第一关的队员题目表（EventInstanceManager.java:1377/1397/1416）。
            Bind(Host,"gridInsert",c=>{playerGrid[manager.Id(c.Arg(0))]=Number(c,1);return Void;});
            Bind(Host,"gridCheck",c=>new JsNumber(playerGrid.TryGetValue(manager.Id(c.Arg(0)),out int status)?status:-1));
            Bind(Host,"gridRemove",c=>{playerGrid.Remove(manager.Id(c.Arg(0)));return Void;});
            Bind(Host,"gridSize",c=>new JsNumber(playerGrid.Count));
            Bind(Host,"gridClear",c=>{playerGrid.Clear();return Void;});
            // 阶段奖励表（EventInstanceManager.java:939/963/1348）。
            Bind(Host,"setEventClearStageExp",c=>{onMapClearExp.Clear();onMapClearExp.AddRange(Integers(c.Arg(0)));return Void;});
            Bind(Host,"setEventClearStageMeso",c=>{onMapClearMeso.Clear();onMapClearMeso.AddRange(Integers(c.Arg(0)));return Void;});
            Bind(Host,"getClearStageExp",c=>new JsNumber(StageBonus(onMapClearExp,Number(c))));
            Bind(Host,"getClearStageMeso",c=>new JsNumber(StageBonus(onMapClearMeso,Number(c))));
            Bind(Host,"getClearStageBonus",c=>
            {
                var bonus=new JsArray(new JsValue[]{
                    new JsNumber(StageBonus(onMapClearExp,Number(c))),
                    new JsNumber(StageBonus(onMapClearMeso,Number(c)))});
                bonus.Set("get",JsFunction.FromNative("get",call=>bonus.GetIndex(Number(call))));
                bonus.Set("size",JsFunction.FromNative("size",_=>new JsNumber(bonus.Length)));
                return bonus;
            });
            Bind(Host,"giveEventPlayersStageReward",c=>{GiveStageReward(Number(c));return Void;});
            Bind(Host,"giveEventPlayersExp",c=>{GiveAll("gainExp",Number(c,0),c.ArgCount>1?Number(c,1):-1);return Void;});
            Bind(Host,"giveEventPlayersMeso",c=>{GiveAll("gainMeso",Number(c,0),c.ArgCount>1?Number(c,1):-1);return Void;});
            // showWrongEffect：EventInstanceManager.java:1283，队长图上播「答错」特效。
            Bind(Host,"showWrongEffect",c=>{service.WrongEffect?.Invoke(this,c.ArgCount>0?Number(c):-1);return Void;});
            // dropAllExclusiveItems：EventInstanceManager.java:979，进本时把独占物品从背包清掉。
            Bind(Host,"dropAllExclusiveItems",c=>{DropExclusiveItems();return Void;});
            // registerMonster：EventInstanceManager.java:475，脚本自己刷的怪登记进实例。
            Bind(Host,"registerMonster",c=>{if(c.Arg(0) is JsHostObject mob)monsters.Add(mob);return Void;});
            // activatedAllReactorsOnMap(map,min,max)：EventInstanceManager.java:1429，
            // 指定 id 区间的反应堆「全部被激活过」才算 true（PiratePQ 的大门判定）。
            Bind(Host,"activatedAllReactorsOnMap",c=>
            {
                int mapId=JsConvert.ToInt32(manager.CallMember(c.Arg(0),"getId"));
                return JsBoolean.Of(AllReactorsActivated(mapId,Number(c,1),Number(c,2)));
            });
        }
        List<JsValue> PlayerSnapshot()
        {
            var list=new List<JsValue>();
            foreach(var entry in players)if(!Disposed)list.Add(entry.Value);
            return list;
        }
        bool IsPlayer(JsValue player)
        {
            if(player is JsNull || player is JsUndefined)return false;
            var id=engine_getId(player);return id!=0;
        }
        int engine_getId(JsValue player)
        {
            try { return manager.Id(player); } catch(Exception) { return 0; }
        }
        int MapOf(JsValue player)
        {
            try { return JsConvert.ToInt32(manager.CallMember(player,"getMapId")); }
            catch(Exception) { return -1; }
        }
        void WarpTeam(int from,int to)
        {
            if(to<=0)return;
            foreach(var player in PlayerSnapshot())
            {
                if(from!=int.MinValue && MapOf(player)!=from)continue;
                if(service.WarpPlayer!=null) { service.WarpPlayer(this,to,player); continue; }
                try { manager.CallMember(player,"changeMap",GetMap(to)); }
                catch(Exception error) { service.Error?.Invoke(error); }
            }
        }
        void GiveAll(string method,long amount,int mapId)
        {
            if(amount==0)return;
            foreach(var player in PlayerSnapshot())
            {
                if(mapId!=-1 && MapOf(player)!=mapId)continue;
                try
                {
                    double rate=1;
                    if(method=="gainExp")
                    {
                        var getRate=engine_interp?.GetMember(player,"getExpRate");
                        if(getRate is JsFunction)rate=JsConvert.ToNumber(engine_interp.Call(getRate,player,Array.Empty<JsValue>()));
                    }
                    manager.CallMember(player,method,new JsNumber(Math.Round(amount*rate)));
                }
                catch(Exception error) { service.Error?.Invoke(error); }
            }
        }
        JsInterpreter engine_interp => manager.InterpOf;
        void GiveStageReward(int stage)
        {
            GiveAll("gainExp",StageBonus(onMapClearExp,stage),-1);
            GiveAll("gainMeso",StageBonus(onMapClearMeso,stage),-1);
        }
        // EventInstanceManager.java:949 —— stage 从 1 开始，越界返回 0。
        static int StageBonus(List<int> table,int stage)
            => stage>=1 && stage<=table.Count ? table[stage-1] : 0;
        void DropExclusiveItems()
        {
            if(exclusiveItems.Count==0)return;
            foreach(var player in PlayerSnapshot())
                foreach(int item in exclusiveItems)
                    try { manager.CallMember(player,"removeAll",new JsNumber(item)); }
                    catch(Exception) { /* 玩家没有这个物品就当已经清掉了 */ }
        }
        bool AllReactorsActivated(int mapId,int min,int max)
        {
            foreach(var reactor in Server.Field.MapReactorService.Get(mapId))
                if(reactor.id>=min && reactor.id<=max && reactor.state<=0)return false;
            return true;
        }
        JsValue GetMap(int id)
        {
            if(Disposed)throw new JsRuntimeException("Disposed event instance: "+Name);
            if(maps.TryGetValue(id,out var map))return map;
            if(service.InstanceMap==null)throw new JsRuntimeException("Unmigrated event instance map isolation: "+id);
            map=service.InstanceMap(this,id);
            if(map==null || map is JsNull)throw new JsRuntimeException("Missing event instance map: "+id);
            maps.Add(id,map);return map;
        }
        public JsValue Invoke(string method,params JsValue[] args) => Disposed ? JsNull.Value : manager.Call(method,args);
        static List<int> Integers(JsValue value)
        {
            if(!(value is JsArray array))throw new JsRuntimeException("Event item list must be an array");
            var result=new List<int>();foreach(var item in array.Items)result.Add(JsConvert.ToInt32(item));return result;
        }
        bool GiveReward(JsValue player,int level)
        {
            if(!rewards.TryGetValue(level,out var reward))return true;
            if(reward.Items.Count>0)
            {
                var types=new HashSet<int>();foreach(int item in reward.Items)types.Add(item/1000000);
                foreach(int type in types)if(!JsConvert.ToBoolean(manager.CallMember(player,"hasEmptySlot",new JsNumber(type))))return false;
                int index=service.RewardIndex(reward.Items.Count);
                if(index>=reward.Quantities.Count)throw new JsRuntimeException("Missing event reward quantity");
                var api=manager.CallMember(player,"getAbstractPlayerInteraction");
                manager.CallMember(api,"gainItem",new JsNumber(reward.Items[index]),new JsNumber(unchecked((short)reward.Quantities[index])));
            }
            if(reward.Exp>0)manager.CallMember(player,"gainExp",new JsNumber(reward.Exp));
            return true;
        }
        // EventInstanceManager.java:1155-1163 —— 队长是否还在实例里。
        bool LeaderOnline()
        {
            foreach(var player in players.Values)
                if(manager.Id(player)==Leader)return true;
            return false;
        }
        // EventInstanceManager.java:1196-1218 —— 全员在同一张图（≤1 人恒为 true）。
        bool TeamTogether()
        {
            if(players.Count<=1)return true;
            int mapId=int.MinValue;
            foreach(var player in players.Values)
            {
                int current=JsConvert.ToInt32(manager.CallMember(player,"getMapId"));
                if(mapId==int.MinValue)mapId=current;
                else if(current!=mapId)return false;
            }
            return true;
        }
        public void SetCleared()
        {
            if(Disposed)return;
            Cleared=true;
            // EventInstanceManager.setEventCleared awards quest points before releasing
            // the lobby. Disposing the instance here would cancel finishVoyage/end.
            foreach(var player in new List<JsValue>(players.Values))
            {
                if(service.AwardClearQuestPoints==null)
                    throw new JsRuntimeException("Event clear quest points are not configured");
                service.AwardClearQuestPoints(player);
            }
            manager.Remove(this);
        }
        public void RegisterMonster(JsValue monster,bool friendly=false)
        { if(!Disposed && !friendly)monsters.Add(monster); }
        public void MonsterKilled(JsValue monster,bool hasKiller,bool friendly=false)
        {
            if(Disposed)return;
            if(friendly) { Invoke("friendlyKilled",monster,Host,JsBoolean.Of(hasKiller));return; }
            monsters.Remove(monster);
            if(!Started)return;
            // Capture the empty state before the callback: scripts may spawn a new wave.
            bool empty=monsters.Count==0;
            Invoke("monsterKilled",monster,Host,JsBoolean.Of(hasKiller));
            if(empty)Invoke("allMonstersDead",Host,JsBoolean.Of(hasKiller));
        }
        // EventInstanceManager.java:607-626 monsterKilled(Character, Monster)：只问脚本
        // `monsterValue(mobId)` 要积分并把它记到击杀者头上，再通知远征队（未迁移）。
        // 和上面那个 monsterKilled(Monster, hasKiller) 是两条独立入口，源端分别在
        // 「发完经验」和「killBy」里调用，不能合并成一次。
        // EventInstanceManager.java:607-626 monsterKilled(Character,Monster)：
        //   inc = invokeScriptFunction("monsterValue", eim, mob.getId())
        // 脚本签名统一是 monsterValue(eim, mobId)，第一个参数必须是实例宿主，不能省。
        public void MonsterKilledBy(JsValue killer,int mobId,string mobName=null)
        {
            if(Disposed || mobId<=0 || killer is JsNull || killer is JsUndefined)return;
            var value=Invoke("monsterValue",Host,new JsNumber(mobId));
            // 大多数脚本的 monsterValue 只对特定怪 return 1，其余路径返回 undefined。
            if(!(value is JsNumber number))return;
            int inc=(int)number.Value;
            if(inc==0)return;
            int id=manager.Id(killer);
            killCount.TryGetValue(id,out int current);
            killCount[id]=unchecked(current+inc);
            // 源端同一分支里顺带让远征队记 BOSS 战绩。
            Exped?.MonsterKilled(mobId,mobName ?? ("mob " + mobId));
        }
        public int GetKillCount(JsValue player) => killCount.TryGetValue(manager.Id(player),out int value) ? value : 0;
        // EventInstanceManager.java:597 playerDisconnected：只调用脚本回调，不在这里摘玩家
        // （脚本自己 unregisterPlayer / dispose），源端同样如此。
        public void PlayerDisconnected(JsValue player)
        {
            if(Disposed)return;
            Invoke("playerDisconnected",Host,player);
        }
        // EventInstanceManager.java:577 reviveMonster → 脚本 monsterRevive(eim, mob)。
        // 源端 MapleMap.reviveMonster 在整个 Java 里没有调用点（`monsterRevive` 在源端是死代码），
        // 但移植端客户端真的会按 Mob.wz/info/revives 刷后继形态（露希妲 8880141 → 8880152），
        // 所以这里补上回调：不补的话露希妲第三阶段的控制器永远不会启动。
        public void ReviveMonster(JsValue monster)
        {
            if(Disposed || monster is JsNull || monster is JsUndefined)return;
            Invoke("monsterRevive",Host,monster);
        }
        public void Register(JsValue player,bool entry=true)
        {
            if(Disposed || player is JsNull || player is JsUndefined)return;
            int id=manager.Id(player);if(players.ContainsKey(id))return;
            players.Add(id,player);service.membership[id]=this;
            if(entry)Invoke("playerEntry",Host,player);
        }
        public void Unregister(JsValue player)
        {
            if(Disposed)return;
            int id=manager.Id(player);
            Invoke("playerUnregistered",Host,player);
            players.Remove(id);
            service.membership.Remove(id);
            if(exclusiveItems.Count>0)
            {
                var api=manager.CallMember(player,"getAbstractPlayerInteraction");
                foreach(int item in exclusiveItems)manager.CallMember(api,"removeAll",new JsNumber(item));
            }
        }
        public void Start() { if(Disposed)return;Started=true;Invoke("afterSetup",Host); }
        public void ChangedMap(JsValue player,int map,bool after=false)
            => Invoke(after?"afterChangedMap":"changedMap",Host,player,new JsNumber(map));
        public void StartTimer(long duration)
        {
            if(Disposed)return;
            timerStarted=service.clock();eventTime=duration;service.Clock?.Invoke(this,(int)(duration/1000));
            timer=service.Schedule(manager,this,duration,()=>{DismissTimer();Invoke("scheduledTimeout",Host);});
        }
        void DismissTimer() { service.Clock?.Invoke(this,null);timer=null;timerStarted=0;eventTime=0; }
        public void StopTimer() { if(timer!=null)timer.Cancelled=true;DismissTimer(); }
        public void Dispose()
        {
            if(Disposed || disposing)return;
            disposing=true;
            try { Invoke("dispose",Host); }
            finally
            {
                Disposed=true;
                foreach(int id in players.Keys)if(service.GetInstance(id)==this)service.membership.Remove(id);
                players.Clear();if(timer!=null)timer.Cancelled=true;timer=null;
                foreach(var task in service.tasks)if(task.Owner==this)task.Cancelled=true;
                properties.Clear();maps.Clear();monsters.Clear();killCount.Clear();manager.Remove(this);service.ReleaseMaps?.Invoke(this);
                // EventInstanceManager.java:1107-1116：先按清关状态写战绩，再从频道摘掉远征队。
                if(Exped!=null){Exped.Dispose(Cleared);ExpeditionRegistry.RemoveExpedition(Exped);Exped=null;}
                disposing=false;
            }
        }
    }
}
