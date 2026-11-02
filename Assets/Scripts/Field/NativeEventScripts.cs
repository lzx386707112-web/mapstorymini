using System;
using MapleDash.Js;
using UnityEngine;

// Channel event engine outlives individual map/NPC conversations.
public sealed class NativeEventScripts : MonoBehaviour
{
    static NativeEventScripts active;
    NativeEventScriptService service;
    object character;
    // 事件计时/延迟任务的时钟。生产走真实 UTC；编辑器回归换成可控时钟后用 TickNow() 手动推进
    // （batchmode 不跑 Update，否则 10 分钟副本超时、2 秒换波这些分支根本测不到）。
    internal static Func<long> Clock = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    readonly System.Collections.Generic.Dictionary<NativeEventScriptService.Instance,System.Collections.Generic.Dictionary<int,NativeEventMap>> instanceMaps=
        new System.Collections.Generic.Dictionary<NativeEventScriptService.Instance,System.Collections.Generic.Dictionary<int,NativeEventMap>>();
    // 源端 Channel 构造时把 event/ 下所有脚本都塞进 EventScriptManager 并 init()（Channel.getEvents +
    // EventScriptManager.init），AreaBoss*.js 的 init() 里就 em.schedule("start", 0) 自己排上了。
    // 移植端漏了这一步，于是「野外 BOSS」那批 Boss 一个都不会刷（它们的地图 life 里本来就没有 Boss）。
    // 这里补同一条启动链，但只对 AreaBoss* 做，且一帧一个，别把登录后的第一帧堵死；
    // 其余事件脚本仍然由 NPC 脚本的 cm.getEventManager 按需懒加载。
    readonly System.Collections.Generic.List<string> pendingBossEvents =
        new System.Collections.Generic.List<string>();
    bool bossEventsQueued;
    static NativeEventScripts Current()
    {
        // EventManager belongs to the channel, not a rendered map. NPC scripts
        // may query managers before entering an instance; map capabilities stay explicit.
        var host=active;
        if(host==null)
        {
            var owner=new GameObject("[NativeEventScripts]");
            host=owner.AddComponent<NativeEventScripts>();
            if(Application.isPlaying) DontDestroyOnLoad(owner);
            active=host;
        }
        if(host.service==null || !ReferenceEquals(host.character,GameSession.Character))
        {
            host.service?.Clear();host.ReleaseAllMaps();host.character=GameSession.Character;
            host.bossEventsQueued=false;host.pendingBossEvents.Clear();
            host.service=new NativeEventScriptService(ScriptSource.LoadKey,()=>Clock(),engine=>
            {
                var java=NpcApi.BuildJava();
                engine.Interp.ResolveHostType=type=>GameServer.Local?.Scripts.ResolveHostType(type)
                    ?? SeedTowerCompat.Resolve(type)
                    ?? engine.Interp.Call(engine.Interp.GetMember(java,"type"),java,new JsValue[]{new JsString(type)});
            });
            host.service.TransportationTime=time=>GameServer.Local.Scripts.TransportationTime(GameSession.World,time);
            // Reactor.hitReactor:384 的起源之塔闸门（SeedTowerCompat 在 Field 层，Server 不能直接引用）。
            Server.Field.MapReactorService.AllowHit=SeedTowerCompat.AllowReactorHit;
            // 队伍版 startInstance 的队长判定（源端用 party.getLeader().getPlayer()）。
            host.service.LocalPlayerId=()=>GameSession.CharacterId;
            // EventManager.getBossTime（EventManager.java:1271）：刷新间隔 × boss_respawn_mob_time_rate。
            // 源端同键默认 1.0；这里把非法值（<=0/>1）按 MapFactory.java:128 的口径当 1 处理，
            // 否则 0 会让 em.schedule(name,0) 变成每帧重入。
            host.service.BossTime=value=>
            {
                float rate=GameServer.Local==null ? 1f : (float)GameConfigDao.GetFloat(GameServer.Local.Db,"boss_respawn_mob_time_rate",1);
                if(rate<=0f || rate>1f) rate=1f;
                return (long)System.Math.Round(value*(double)rate);
            };
            host.service.ChannelServer=()=>NpcApi.BuildChannelServer(NpcApi.BuildPlayer(NpcSession.MapContext()));
            // EventInstanceManager.java:1293-1329：showEffect("quest/party/clear") + 音效。
            // 音效系统未迁移，动画按原素材播（NativeScreenEffect）。
            host.service.ClearEffect=(instance,mapId,gate)=>
            {
                NativeScreenEffect.Play("Map/Effect.img/quest/party/clear");
            };
            host.service.Error=error=>Debug.LogError("[EventScript] "+error.Message);
            // EventManager.getMonster(mobId)：LifeFactory.getMonster 的实例侧入口，
            // 脚本拿到模板怪后自己 registerMonster + map.spawnMonsterOnGroundBelow。
            host.service.MonsterFactory=id=>new ScriptMonster(id).Host();
            // eim.getMapFactory().getMap(id)：warp 目标图的脚本对象（客户端 Npc 层的 BuildWarpMap）。
            host.service.WarpMapFactory=id=>NpcApi.BuildWarpMap(id);
            // 组队任务（KPQ/LudiPQ/EllinPQ/PiratePQ）：warpEventTeam 把本地角色送到目标图。
            // 假人不走这条（它们由 Server.Bot 侧自己 WarpTo），这里只处理真实角色。
            host.service.WarpPlayer=(instance,mapId,player)=>
            {
                var field=GameLoop.I!=null?GameLoop.I.field:null;
                if(field!=null)field.WarpTo(mapId,null,-1); else Profile.LastMapId=mapId;
            };
            // showWrongEffect：EventInstanceManager.java:1289 播 quest/party/wrong_kor + Party1/Failed。
            host.service.WrongEffect=(instance,mapId)=>
                NativeScreenEffect.Play("Map/Effect.img/quest/party/wrong_kor");
            host.service.InstanceMap=(instance,id)=>
            {
                if(!host.instanceMaps.TryGetValue(instance,out var maps))
                    host.instanceMaps.Add(instance,maps=new System.Collections.Generic.Dictionary<int,NativeEventMap>());
                if(!maps.TryGetValue(id,out var map))maps.Add(id,map=new NativeEventMap(instance,id));
                return map.Host;
            };
            host.service.ReleaseMaps=instance=>
            {
                if(!host.instanceMaps.TryGetValue(instance,out var maps))return;
                foreach(var map in maps.Values)map.Dispose();host.instanceMaps.Remove(instance);
            };
            host.service.SpawnNpc=(instance,id,position,target)=>
            {
                var map=(target as JsHostObject)?.Host as NativeEventMap;
                var point=position as JsHostObject;
                if(map==null || map.Instance!=instance || point==null)throw new JsRuntimeException("Invalid event NPC map/position");
                int x=JsConvert.ToInt32(point.Get("x")),y=JsConvert.ToInt32(point.Get("y"));
                map.Geometry.npcs.Add(new Server.Field.MapNpcPosition {id=id,x=x,
                    y=(int)Server.Field.MapService.SnapToGround(map.Scope,x,y),dynamic=true});
            };
            host.service.AwardClearQuestPoints=player=>
            {
                if(!((player as JsHostObject)?.Get("getId") is JsFunction getId) ||
                    JsConvert.ToInt32(getId.Native(new JsCall {This=player,Args=Array.Empty<JsValue>()}))!=GameSession.CharacterId)
                    throw new JsRuntimeException("Event clear player is not the local character");
                NpcApi.ChangeFame(ScriptOperation.AwardEventQuestPoints);
            };
            if(GameServer.Local!=null)host.service.LobbyDelayMillis=GameConfigDao.GetInt(GameServer.Local.Db,"event_lobby_delay",10)*1000L;
            if(GameServer.Local!=null)host.service.MaxEventLevels=GameConfigDao.GetInt(GameServer.Local.Db,"max_event_levels",8);
            // 源端 startEventTimer/dismissEventTimer 发的就是 CLOCK / STOP_CLOCK 两个包，
            // 客户端侧按 Map/Obj/etc.img/clock/fontTime 画倒计时（见 NativeEventClock）。
            host.service.Clock=(instance,seconds)=>
            {
                if(seconds.HasValue) NativeEventClock.Show(seconds.Value);
                else NativeEventClock.Hide();
            };
        }
        return host;
    }
    public static JsValue Manager(string name) => Current().service.GetManager(name)?.Host ?? (JsValue)JsNull.Value;
    // 进图后调一次：把 AreaBoss* 事件脚本按源端启动顺序逐个 init()（每帧一个）。
    public static void QueueWildBossEvents()
    {
        if(!GameSession.InGame) return;
        var host=Current();
        if(host.bossEventsQueued) return;
        host.bossEventsQueued=true;
        foreach(string name in ScriptSource.Keys("event"))
            if(name.StartsWith("AreaBoss",StringComparison.Ordinal)) host.pendingBossEvents.Add(name);
    }
    void TickBossEvents()
    {
        if(pendingBossEvents.Count==0) return;
        string name=pendingBossEvents[0];
        pendingBossEvents.RemoveAt(0);
        try { service.GetManager(name); }
        catch(Exception error)
        {
            // 单个事件脚本坏了不能拖累其它 Boss；源端 EventScriptManager.init 也是逐个 try/catch。
            Debug.LogError("[EventScript] 启动事件脚本失败 "+name+"："+error.Message);
        }
    }
    public static JsValue Instance() => active!=null && ReferenceEquals(active.character,GameSession.Character)
        ? active.service?.GetInstance(GameSession.CharacterId)?.Host ?? (JsValue)JsNull.Value : JsNull.Value;
    // AbstractPlayerInteraction.isEventLeader()：有实例时按实例队长判定。
    // 源端这是「队长才能推进副本阶段」的唯一判据，恒 false 会让四个组队任务的 NPC
    // 永远走「请让队长来跟我谈」分支。
    public static bool IsEventLeader(int characterId)
    {
        var instance=CurrentInstance();
        return instance!=null && instance.Leader==characterId;
    }
    // 原生宿主（SeedTowerCompat 等）要读实例属性/玩家表，需要拿到 Instance 本体而不是 JS 宿主。
    public static NativeEventScriptService.Instance CurrentInstance()
    {
        if(active==null || !ReferenceEquals(active.character,GameSession.Character))return null;
        return active.service?.GetInstance(GameSession.CharacterId);
    }
    public static NativeEventScriptService.Instance InstanceById(int characterId)
        => active?.service?.GetInstance(characterId);
    public static NativeEventMap ResolveMap(int id)
    {
        if(active==null || !ReferenceEquals(active.character,GameSession.Character))return null;
        var instance=active.service?.GetInstance(GameSession.CharacterId);
        return instance!=null && active.instanceMaps.TryGetValue(instance,out var maps) && maps.TryGetValue(id,out var map)?map:null;
    }
    public static void ChangedMap(int map,bool after)
    {
        if(active==null || !ReferenceEquals(active.character,GameSession.Character))return;
        var instance=active?.service?.GetInstance(GameSession.CharacterId);
        if(instance==null)return;
        try { instance.ChangedMap(NpcApi.BuildPlayer(NpcSession.MapContext()),map,after); }
        catch(Exception error) { Debug.LogError("[EventScript] map "+map+": "+error.Message); }
    }
    // 源端 Client 断开连接时 Channel 调 eim.playerDisconnected（Client.java:969），只负责调脚本回调，
    // 摘人/清场由脚本自己的 playerDisconnected 收尾。移植端单机只能在结束会话时补这一次；
    // 必须在 GameSession.Character 置空之前调用，否则实例已经反查不到了。
    public static void PlayerDisconnected()
    {
        if(active==null || !ReferenceEquals(active.character,GameSession.Character))return;
        var instance=active.service?.GetInstance(GameSession.CharacterId);
        if(instance==null)return;
        try { instance.PlayerDisconnected(NpcApi.BuildPlayer(NpcSession.MapContext())); }
        catch(Exception error) { Debug.LogError("[EventScript] playerDisconnected: "+error.Message); }
    }
    void Update()
    {
        if(service==null)return;
        if(!ReferenceEquals(character,GameSession.Character)) { service.Clear();ReleaseAllMaps();service=null;return; }
        TickBossEvents();
        service.Tick();
        // 远征队招募倒计时（源端是 TimerManager 定时任务）。频道级状态，跟着游戏主循环走。
        ExpeditionRegistry.Tick(Clock());
        // LucidBossCompat 的 250ms 控制器（源端 TimerManager.register）。
        LucidBossCompat.Tick(Clock());
    }
    void OnDestroy()
    {
        service?.Clear();ReleaseAllMaps();if(active==this)active=null;
    }
    void ReleaseAllMaps()
    {
        foreach(var maps in instanceMaps.Values)foreach(var map in maps.Values)map.Dispose();
        instanceMaps.Clear();
    }
    // ——— 编辑器回归用的测试缝（不参与运行时逻辑） ———
    // 与 Update() 同一顺序：先推事件延迟任务，再推 LucidBossCompat 的控制器。
    internal void TickNow()
    {
        service?.Tick();
        LucidBossCompat.Tick(Clock());
    }}
