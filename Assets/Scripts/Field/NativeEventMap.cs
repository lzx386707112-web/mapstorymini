using System;
using System.Collections.Generic;
using MapleDash.Js;
using Server.Field;
using UnityEngine;

// Each EIM owns its mutable mobs/spawn scheduler/reactors/drops. Template IDs remain
// visible to original scripts; the negative runtime key never enters a character save.
public sealed class NativeEventMap
{
    static int nextScope=-1000000,nextMob=1500000000;
    // 事件地图是「每实例各一份」，但脚本手里只有 JS 宿主；Compat.move / killMonster 这类
    // 原生入口要靠宿主反查回 Mob。地图数量很少（起源之塔 21 张），线性查找足够。
    static readonly List<NativeEventMap> live=new List<NativeEventMap>();
    public static Mob FindByHost(JsValue host)
    {
        if(host==null)return null;
        foreach(var map in live)
        {
            if(map.disposed)continue;
            foreach(var mob in map.mobs)if(ReferenceEquals(mob.Host,host))return mob;
        }
        return null;
    }
    public static Mob FindByVisible(EnemyUnit unit)
    {
        if(unit==null)return null;
        foreach(var map in live)
        {
            if(map.disposed)continue;
            foreach(var mob in map.mobs)if(ReferenceEquals(mob.Visible,unit))return mob;
        }
        return null;
    }
    public readonly int Id,Scope;
    public readonly NativeEventScriptService.Instance Instance;
    public readonly JsHostObject Host;
    public readonly FieldMap Template;
    public readonly MapRuntime Geometry;
    readonly List<Mob> mobs=new List<Mob>();
    readonly Dictionary<MapReactor,JsHostObject> reactorHosts=new Dictionary<MapReactor,JsHostObject>();
    readonly HashSet<EnemyUnit> expiring=new HashSet<EnemyUnit>();
    MapSpawner spawner;
    FieldDirector renderer;
    bool disposed;
    // MapleMap.allowSummonState 的登记位（源端默认允许）。
    public bool allowSummonState=true;
    static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    static int Int(JsCall c,int index=0)=>JsConvert.ToInt32(c.Arg(index));
    static void Bind(JsHostObject host,string name,Func<JsCall,JsValue> action)
        =>host.Set(name,JsFunction.FromNative(name,action));
    static JsValue Done=>JsUndefined.Value;
    public NativeEventMap(NativeEventScriptService.Instance instance,int id)
    {
        Instance=instance;Id=id;Scope=nextScope--;
        var template=FieldCatalog.GetMap(id) ?? throw new JsRuntimeException("Missing event map: "+id);
        Template=NativeMapView.LoadMap(template);
        var source=GameServer.Local.Bots.GetMap(id);
        Geometry=new MapRuntime {mapId=Scope,templateMapId=Id,returnMap=source.returnMap,fieldLimit=source.fieldLimit,
            left=source.left,right=source.right,top=source.top,bottom=source.bottom,
            swim=source.swim,everlast=source.everlast,footholdSpeed=source.footholdSpeed,
            minX=source.minX,maxX=source.maxX,minY=source.minY,maxY=source.maxY};
        Geometry.footholds.AddRange(source.footholds);Geometry.ropes.AddRange(source.ropes);
        foreach(var npc in source.npcs)if(!npc.dynamic)
            Geometry.npcs.Add(new MapNpcPosition {id=npc.id,x=npc.x,y=npc.y});
        Geometry.reactors.AddRange(source.reactors);
        foreach(var portal in source.portals) Geometry.portals.Add(new MapPortal {id=portal.id,x=portal.x,y=portal.y,
            type=portal.type,targetMap=portal.targetMap,name=portal.name,targetName=portal.targetName,
            script=portal.script,enabled=portal.enabled});
        MapService.Register(Geometry);
        live.Add(this);
        // 源端 getMapInstance 返回的是频道地图：它的刷怪系统自开服起就在跑，玩家踏进
        // 副本时模板 life 的怪早就站在图上了。KerningPQ 的 respawnStages 只调
        // instanceMapRespawn、从不 resetPQ，就是按「图里本来就有怪」写的——之前移植端
        // 要等 resetPQ 才建刷怪点，结果废都 PQ 全程无怪，第一关鳄鱼(9300001)的
        // 通行证(4001007)无从掉落。这里对齐源端：实例图创建即建刷怪点并全部首刷。
        InitialSpawn();
        Host=new JsHostObject(this);
        Host.Missing=key=>throw new JsRuntimeException("Unmigrated event map API: "+key);
        Bind(Host,"getId",c=>new JsNumber(Id));Bind(Host,"getMapId",c=>new JsNumber(Id));
        Bind(Host,"getFieldLimit",c=>new JsNumber(Geometry.fieldLimit));
        Bind(Host,"getMapName",c=>new JsString(WzNames.Map(Id)));
        Bind(Host,"resetPQ",c=>{Reset();return Done;});
        // MapleMap.resetFully()：= resetMapObjects 全重置（清怪/掉落/反应堆 + 重建刷怪点立即首刷），
        // 与 resetPQ 同路径。Puppeteer.js setup 第一句就是 getInstanceMap(eventMap).resetFully()。
        Bind(Host,"resetFully",c=>{Reset();return Done;});
        // MapleMap.allowSummonState(flag)：实例图是否允许召唤物状态保存（Puppeteer 禁、RockSpirit 开）。
        // 本端召唤兽不走实例图持久化路径，登记真实开关即可。
        Bind(Host,"allowSummonState",c=>{allowSummonState=JsConvert.ToBoolean(c.Arg(0));return Done;});
        // MapleMap.warpEveryone(mapId[, portal])：把图上所有人传走（Subway takeoff/arrived 用）。
        // 单机图上只有本地角色一人，语义一致。
        Bind(Host,"warpEveryone",c=>
        {
            int target=Int(c);
            var session=NpcSession.MapContext();
            foreach(var player in PlayerList().Items)
                session.RequestWarp(target,null,c.ArgCount>1?Int(c,1):-1);
            return Done;
        });
        Bind(Host,"getPortal",c=>Portal(c.Arg(0)));
        Bind(Host,"countMonsters",c=>new JsNumber(Count));
        Bind(Host,"getSpawnedMonstersOnMap",c=>new JsNumber(Count));
        Bind(Host,"getAllMonsters",c=>MonsterList());
        Host.Set("getMonsters",Host.Get("getAllMonsters"));Host.Set("getMobs",Host.Get("getAllMonsters"));
        Bind(Host,"getMonsterById",c=>Find(m=>m.Id==Int(c))?.Host ?? (JsValue)JsNull.Value);
        Bind(Host,"getMonsterByOid",c=>Find(m=>m.ObjectId==Int(c))?.Host ?? (JsValue)JsNull.Value);
        Bind(Host,"killAllMonsters",c=>{KillAll();return Done;});
        Bind(Host,"killMonster",c=>
        {
            var mob=mobs.Find(m=>ReferenceEquals(m.Host,c.Arg(0)));
            if(mob==null)return Done;
            Expire(mob,!(c.Arg(1) is JsNull || c.Arg(1) is JsUndefined));return Done;
        });
        Bind(Host,"instanceMapRespawn",c=>{Respawn();return Done;});
        Bind(Host,"spawnMonsterOnGroundBelow",c=>
        {
            var monster=c.Arg(0) as JsHostObject ?? throw new JsRuntimeException("Expected event monster");
            int mobId=JsConvert.ToInt32(monster.Get("id"));
            var point=c.Arg(1) as JsHostObject;
            float x=point!=null?(float)JsConvert.ToNumber(point.Get("x")):Int(c,1);
            float y=point!=null?(float)JsConvert.ToNumber(point.Get("y")):Int(c,2);
            Spawn(mobId,x,MapService.SnapToGround(Scope,x,y),null,monster);return Done;
        });
        Bind(Host,"spawnMonster",c=>
        {
            var monster=c.Arg(0) as JsHostObject ?? throw new JsRuntimeException("Expected event monster");
            // LifeFactory.getMonster 出来的怪在源端 position 默认 (0,0)，没有 setPosition 也照样能刷。
            var point=monster.Get("position") as JsHostObject;
            Spawn(JsConvert.ToInt32(monster.Get("id")),
                point!=null?(float)JsConvert.ToNumber(point.Get("x")):0,
                point!=null?(float)JsConvert.ToNumber(point.Get("y")):0,null,monster);return Done;
        });
        Bind(Host,"broadcastMessage",c=>{Broadcast(c.Arg(c.ArgCount-1));return Done;});
        // MapleMap.dropMessage(type, text)：只发给站在图里的人。
        Bind(Host,"dropMessage",c=>{DropMapMessage(Int(c),JsConvert.ToString(c.Arg(1)));return Done;});
        // MapleMap.broadcastStringMessage(type, text)：dropMessage 的别名，LudiPQ 用它广播过关提示。
        Bind(Host,"broadcastStringMessage",c=>{DropMapMessage(Int(c),JsConvert.ToString(c.Arg(1)));return Done;});
        // MapleMap.getAllPlayers()：实例地图只有本地角色，玩家不在图上就是空表。
        Bind(Host,"getAllPlayers",c=>PlayerList());
        Host.Set("getCharacters",Host.Get("getAllPlayers"));
        // MapleMap.getPlayers()：同一份名单的旧名字（EllinPQ.js:143 的
        // `eim.getMapInstance(930000200).getPlayers().isEmpty()` 用它判断要不要补怪）。
        Host.Set("getPlayers",Host.Get("getAllPlayers"));
        // MapleMap.spawnAllMonsterIdFromMapSpawnList(mobId, difficulty, isPq)：把该模板的
        // 全部刷怪点立刻生成一次（PiratePQ 第二关每 10 秒补一轮波次怪）。
        Bind(Host,"spawnAllMonsterIdFromMapSpawnList",c=>
        {
            if(spawner==null)return Done;
            int mobId=Int(c);
            foreach(var point in spawner.SpawnPointsOf(mobId))
                Spawn(point.MobId,point.Def.x,point.Def.y,point);
            return Done;
        });
        // MapleMap.movePlayer(Character, Point)：图内瞬移（露希妲复活后回出生点用）。
        Bind(Host,"movePlayer",c=>
        {
            var point=c.Arg(1) as JsHostObject;
            if(point==null)return Done;
            MovePlayerTo((float)JsConvert.ToNumber(point.Get("x")),(float)JsConvert.ToNumber(point.Get("y")));
            return Done;
        });
        // MapleMap.getEventInstance()
        Bind(Host,"getEventInstance",c=>(JsValue)Instance.Host);
        // MapleMap.spawnItemDrop(AbstractMapObject dropper, Character owner, Item item, Point pos,
        //                       boolean ffaDrop, boolean playerDrop)。dropType：0 普通 / 2 FFA。
        Bind(Host,"spawnItemDrop",c=>
        {
            var item=(c.Arg(2) as JsHostObject)?.Host as NativeScriptItem;
            if(item==null)throw new JsRuntimeException("spawnItemDrop requires org.gms.client.inventory.Item");
            var point=c.Arg(3) as JsHostObject;
            float x=point!=null?(float)JsConvert.ToNumber(point.Get("x")):0;
            float y=point!=null?(float)JsConvert.ToNumber(point.Get("y")):0;
            int dropper=ObjectIdOf(c.Arg(0));
            int owner=JsConvert.ToInt32((c.Arg(1) as JsHostObject)?.Get("getId") ?? new JsNumber(0));
            var entry=new InventoryEntry {itemId=item.Id,inventoryType=item.Id/1000000,
                quantity=item.Quantity,owner=item.Owner ?? "",giftFrom=item.GiftFrom ?? "",expiration=item.Expiration};
            MapItemService.SpawnItemDrop(Scope,dropper,owner,0,entry,x,y,
                (byte)(JsConvert.ToBoolean(c.Arg(4)) ? 2 : 0),JsConvert.ToBoolean(c.Arg(5)));
            return Done;
        });
        // MapleMap.shuffleReactors()：把图上反应堆的位置互相打乱（源端是收集全部
        // 反应堆坐标后洗牌逐一重设）。EllinPQ 第三关 930000500 的箱子反应堆靠它换位。
        Bind(Host,"shuffleReactors",c=>{ShuffleReactors();return Done;});
        // MapleMap.getReactorByName(name) / getReactors()：第 2 层卡牌石碑、第 18 层椰子树都靠它。
        Bind(Host,"getReactorByName",c=>
        {
            var name=JsConvert.ToString(c.Arg(0));
            var reactor=MapReactorService.Get(Scope).Find(r=>r.name==name);
            return reactor==null ? (JsValue)JsNull.Value : ReactorHost(reactor);
        });
        Bind(Host,"getReactors",c=>
        {
            var list=new JsArray();
            foreach(var reactor in MapReactorService.Get(Scope))list.Items.Add(ReactorHost(reactor));
            BindArray(list);return list;
        });
        Bind(Host,"getFootholds",c=>
        {
            var tree=new JsHostObject(Geometry);
            Bind(tree,"findBelow",call=>
            {
                var point=call.Arg(0) as JsHostObject;
                if(point==null)return JsNull.Value;
                float x=(float)JsConvert.ToNumber(point.Get("x")),y=(float)JsConvert.ToNumber(point.Get("y"));
                FootholdSegment best=null;float height=float.MaxValue;
                foreach(var f in Geometry.footholds)
                {
                    if(f.Wall || x<Math.Min(f.x1,f.x2) || x>Math.Max(f.x1,f.x2))continue;
                    float h=f.y1+(f.y2-f.y1)*(x-f.x1)/(f.x2-f.x1);
                    if(h>=y && h<height){height=h;best=f;}
                }
                if(best==null)return JsNull.Value;
                var result=new JsHostObject(best);Bind(result,"getId",_=>new JsNumber(best.id));return result;
            });return tree;
        });
    }
    public int Count {get {int n=0;foreach(var mob in mobs)if(mob.Alive)n++;return n;}}
    Mob Find(Predicate<Mob> matches)=>mobs.Find(m=>m.Alive && matches(m));
    static readonly System.Random rng=new System.Random();
    // MapleMap.shuffleReactors：收集全部反应堆坐标 → 洗牌 → 逐一重设（bump visualRevision 让视图跟随）。
    void ShuffleReactors()
    {
        var reactors=MapReactorService.Get(Scope);
        var points=new List<(int x,int y)>();
        foreach(var r in reactors)points.Add((r.x,r.y));
        for(int i=points.Count-1;i>0;i--)
        {
            int j=rng.Next(i+1);
            (points[i],points[j])=(points[j],points[i]);
        }
        for(int i=0;i<reactors.Count;i++)
        {
            reactors[i].x=points[i].x;reactors[i].y=points[i].y;reactors[i].visualRevision++;
        }
    }

    // ——— 原生宿主（LucidBossCompat 等）用到的地图能力，语义与 MapleMap 同名方法一致 ———
    public List<Mob> AllMobs => new List<Mob>(mobs);
    public Mob FindAliveById(int mobId) => Find(m=>m.Id==mobId);
    public Mob FindByOid(int objectId) => Find(m=>m.ObjectId==objectId);
    public void Kill(Mob mob) => Expire(mob,false);
    public void SpawnOnGround(int mobId,float x,float y)
        => Spawn(mobId,x,MapService.SnapToGround(Scope,x,y),null);
    public Mob SpawnAt(int mobId,float x,float y,bool ground)
        => Spawn(mobId,x,ground?MapService.SnapToGround(Scope,x,y):y,null);
    // 脚本发 PacketCreator.showEffect：源端是广播给图上所有人，单机就是本地角色。
    public void BroadcastEffect(string path)
    {
        LastPacket="effect:"+path;LastEffect=path;RecordEffect(path);
        if(renderer!=null)NativeScriptEffect.Play(path);
    }
    public void Broadcast(JsValue packet)
    {
        string description=NpcApi.DescribeScriptPacket(packet);
        if(description!=null){LastPacket=description;if(description.StartsWith("effect:"))RecordEffect(description.Substring(7));}
        if(renderer!=null)NpcApi.ApplyScriptPacket(packet);
    }
    // 这张图上播过的全部特效路径（按时间顺序）。回归断言用：同一拍里可能连播两个特效，
    // 只比 LastPacket 会误判「后来的覆盖了先前的」。
    readonly List<string> effectLog=new List<string>();
    public IReadOnlyList<string> EffectLog => effectLog;
    void RecordEffect(string path)
    {
        if(effectLog.Count<256)effectLog.Add(path);
    }
    public string LastPacket {get;private set;}
    public void DropMapMessage(int type,string text)
    {
        LastMessage=text;
        if(renderer!=null)NativeSystemMessage.Drop(type,text);
    }
    // PacketCreator.damagePlayer：主角身上跳一个伤害数字（源端是广播伤害包）。
    public void DamagePlayerPacket(long damage,int mobId,int objectId)
    {
        if(renderer==null)return;
        var player=GameLoop.I!=null?GameLoop.I.player:null;
        if(player==null)return;
        DamageText.Show((Vector3)player.FeetPos+Vector3.up*0.6f,damage,"red");
    }
    void MovePlayerTo(float x,float y)
    {
        if(renderer==null)return;
        renderer.TeleportLocalPlayer(x,y);
    }
    JsArray PlayerList()
    {
        var result=new JsArray();
        if(renderer!=null)result.Items.Add(NpcApi.BuildPlayer(NpcSession.MapContext()));
        BindArray(result);return result;
    }
    // 最近一次发给这张图的特效包 / 提示文本（源端广播内容）。只做记录，不参与逻辑；
    // batchmode 回归没有 FieldDirector，靠它断言「脚本确实让地图播了什么」。
    public string LastEffect {get;private set;}
    public string LastMessage {get;private set;}

    JsArray MonsterList()
    {
        var result=new JsArray();foreach(var mob in mobs)if(mob.Alive)result.Items.Add(mob.Host);
        BindArray(result);return result;
    }
    static void BindArray(JsArray list)
    {
        list.Set("size",JsFunction.FromNative("size",c=>new JsNumber(list.Length)));
        list.Set("get",JsFunction.FromNative("get",c=>list.GetIndex(Int(c))));
    }
    // 掉落归属按源端 AbstractMapObject.getObjectId()：怪物 oid / 反应堆 oid / 角色 id。
    static int ObjectIdOf(JsValue value)
    {
        var host=value as JsHostObject;
        switch(host?.Host)
        {
            case Mob mob: return mob.ObjectId;
            case MapReactor reactor: return reactor.objectId;
        }
        if(host!=null && host.Get("getObjectId") is JsFunction getter)
            return JsConvert.ToInt32(getter.Native(new JsCall {This=value,Args=Array.Empty<JsValue>()}));
        return JsConvert.ToInt32(value);
    }
    JsHostObject ReactorHost(MapReactor reactor)
    {
        if(reactorHosts.TryGetValue(reactor,out var cached))return cached;
        var host=new JsHostObject(reactor);
        host.Missing=key=>throw new JsRuntimeException("Unmigrated event reactor API: "+key);
        Bind(host,"getId",c=>new JsNumber(reactor.id));
        Bind(host,"getObjectId",c=>new JsNumber(reactor.objectId));
        Bind(host,"getState",c=>new JsNumber(reactor.state));
        Bind(host,"getPosition",c=>NpcApi.BuildPoint(reactor.x,reactor.y));
        Bind(host,"getName",c=>new JsString(reactor.name));
        Bind(host,"resetReactorActions",c=>{MapReactorService.ResetActions(Scope,reactor,Int(c));return Done;});
        reactorHosts[reactor]=host;return host;
    }
    public JsHostObject ReactorFor(MapReactor reactor) => reactor==null?null:ReactorHost(reactor);
    public JsValue Portal(JsValue key)
    {
        int index=key is JsString?Geometry.portals.FindIndex(p=>p.name==JsConvert.ToString(key)):JsConvert.ToInt32(key);
        if(index<0 || index>=Geometry.portals.Count)return JsNull.Value;
        var portal=Geometry.portals[index];var host=new JsHostObject(portal);
        Bind(host,"getId",c=>new JsNumber(index));Bind(host,"getName",c=>new JsString(portal.name));
        Bind(host,"getPosition",c=>NpcApi.BuildPoint(portal.x,portal.y));return host;
    }
    public void Reset()
    {
        if(disposed)throw new JsRuntimeException("Disposed event map: "+Id);
        KillAll();MapItemService.ClearMap(Scope);
        // MapleMap.resetReactors resets existing objects, retaining their OIDs and
        // script-created reactors. Rebuilding the pool would invalidate handles.
        foreach(var reactor in MapReactorService.Get(Scope))MapReactorService.Respawn(Scope,reactor);
        spawner=new MapSpawner {Clock=()=>Now,FullRespawn=true};
        spawner.SetMobInterval(Template.createMobInterval);
        spawner.SetRandomRefresh(true);
        foreach(var s in Template.spawns)
        {
            FieldCatalog.LoadMob(s.mob);
            var point=spawner.AddMonsterSpawn(new SpawnPointDef {mob=s.mob,mobTime=s.mobTime,fh=s.fh,
                f=s.dir,team=s.team,x=s.x,y=MapService.SnapToGround(Scope,s.x,s.y),rx0=s.rx0,rx1=s.rx1,cy=(int)s.cy,
                isBoss=FieldCatalog.Mobs.TryGetValue(s.mob,out var def) && Art.Num(def.stats,"boss",0)>0});
            // MapleMap.instanceMapFirstSpawn: only the single-spawn points are immediate.
            if(s.mobTime==-1)Spawn(point.MobId,point.Def.x,point.Def.y,point);
        }
    }
    // 构造期的「频道地图已开服跑了一段时间」口径：Reset 后把 mobTime>=0 的
    // 刷怪点也全部首刷（SpawnPoint.shouldForceSpawn 即源端实例强制补怪判定）。
    void InitialSpawn()
    {
        Reset();
        foreach(var point in spawner.AllSpawnPoints)
            if(point.ShouldForceSpawn())
                Spawn(point.MobId,point.Def.x,point.Def.y,point);
    }
    Mob Spawn(int id,float x,float y,SpawnPoint spawn,JsHostObject host=null)
    {
        FieldCatalog.LoadMob(id);
        if(!FieldCatalog.Mobs.TryGetValue(id,out var data))throw new JsRuntimeException("Missing event monster: "+id);
        var mob=new Mob(this,id,nextMob++,x,y,NativeCombatData.Long(data.stats,"maxHP",1),spawn,host);
        if(host?.Host is ScriptMonster template)mob.ApplyTemplate(template);
        mobs.Add(mob);spawn?.MarkSpawned(Now);
        // 源端 EventInstanceManager.mobs 只由脚本登记（eim.registerMonster / eim.spawnMonster）；
        // 模板 life 刷出来的怪不进集合，否则脚本等 allMonstersDead 时会被模板怪拖住
        //（PiratePQ 波次怪、HenesysPQ/TreasurePQ 的 allMonstersDead 都按脚本登记口径写）。
        if(host!=null)Instance.RegisterMonster(mob.Host,mob.Friendly);
        if(renderer!=null)mob.Render(renderer);
        return mob;
    }
    public EnemyUnit SpawnDynamic(int id,Vector3 position,bool ground)
    {
        float x=position.x*Art.PPU,y=-position.y*Art.PPU;
        return Spawn(id,x,ground?MapService.SnapToGround(Scope,x,y):y,null).Visible;
    }
    public void Respawn()
    {
        if(disposed || spawner==null)return;
        foreach(var point in spawner.Respawn(Instance.PlayerCount))Spawn(point.MobId,point.Def.x,point.Def.y,point);
    }
    public void Attach(FieldDirector field)
    {
        renderer=field;foreach(var mob in mobs)if(mob.Alive)mob.Render(field);
    }
    public void Detach()
    {
        foreach(var mob in mobs)mob.Detach();renderer=null;
    }
    public void Killed(EnemyUnit enemy,bool hasKiller)
    {
        if(expiring.Contains(enemy))return;
        var mob=mobs.Find(m=>m.Visible==enemy);if(mob!=null)Remove(mob,hasKiller);
    }
    void Expire(Mob mob,bool hasKiller)
    {
        var visible=mob.Visible;
        if(visible!=null)
        {
            expiring.Add(visible);
            try {visible.ExpireNative(1);}
            finally {expiring.Remove(visible);}
        }
        Remove(mob,hasKiller);
    }
    void Remove(Mob mob,bool hasKiller)
    {
        if(!mobs.Remove(mob))return;
        mob.Hp=0;mob.Spawn?.OnKilled(mob.Visible?.NativeController?.ServerDeathMillis ?? 0,Now);
        // EventInstanceManager.monsterKilled(Character,Monster)：发完经验后按脚本 monsterValue 记分。
        // 源端两个 monsterKilled 入口是分开的，这里的实例地图只有本地角色会真的给经验，
        // 所以 hasKiller 成立时记账对象就是本地玩家。
        if(hasKiller)Instance.MonsterKilledBy(NpcApi.BuildPlayer(NpcSession.MapContext()),mob.Id,WzNames.Mob(mob.Id));
        Instance.MonsterKilled(mob.Host,hasKiller,mob.Friendly);
    }
    public void KillAll()
    {
        spawner?.CloseSpawnPoints();
        foreach(var mob in new List<Mob>(mobs))
            Expire(mob,false);
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        // 先摘控制器：它的延迟任务还会引用 Mob，图销毁后必须停。
        LucidBossCompat.Stop(this);
        Detach();mobs.Clear();live.Remove(this);
        MapItemService.ClearMap(Scope);MapReactorService.Clear(Scope);MapService.Unregister(Scope);
    }
    public sealed class Mob
    {
        readonly NativeEventMap map;
        public readonly int Id,ObjectId;
        public readonly SpawnPoint Spawn;
        public readonly JsHostObject Host;
        public EnemyUnit Visible;
        public float X,Y;
        public long Hp,MaxHp;
        public bool Friendly;
        public bool Alive=>Visible!=null?!Visible.dead:Hp>0;
        public Mob(NativeEventMap map,int id,int oid,float x,float y,long hp,SpawnPoint spawn,JsHostObject host=null)
        {
            this.map=map;Id=id;ObjectId=oid;X=x;Y=y;Hp=MaxHp=Math.Max(1,hp);Spawn=spawn;
            Friendly=Art.Num(FieldCatalog.Mobs[id].stats,"friendly",0)>0;
            Host=host ?? new JsHostObject(this);Host.Set("id",new JsNumber(Id));
            Host.Missing=key=>throw new JsRuntimeException("Unmigrated event monster API: "+key);
            Bind(Host,"getId",c=>new JsNumber(Id));Bind(Host,"getObjectId",c=>new JsNumber(ObjectId));
            Bind(Host,"getMap",c=>map.Host);Bind(Host,"getName",c=>new JsString(WzNames.Mob(Id)));
            Bind(Host,"isAlive",c=>JsBoolean.Of(Alive));
            Bind(Host,"getHp",c=>new JsNumber(Visible!=null?Visible.NativeController?.CurrentHp ?? (long)Visible.hp:Hp));
            Bind(Host,"getMaxHp",c=>new JsNumber(MaxHp));
            Bind(Host,"heal",c=>
            {
                long amount=(long)JsConvert.ToNumber(c.Arg(0));
                if(Visible!=null)Visible.NativeController?.Heal(amount);
                else Hp=Math.Max(0,Math.Min(MaxHp,Hp+amount));return Done;
            });
            Bind(Host,"getPosition",c=>Visible!=null?NpcApi.BuildPoint(Visible.transform.position.x*Art.PPU,-Visible.transform.position.y*Art.PPU):NpcApi.BuildPoint(X,Y));
            // Monster.setFh / getFh（暴怒失败时把 8880154 放在原 BOSS 的浮台上）。
            Bind(Host,"getFh",c=>new JsNumber(Fh));
            Bind(Host,"setFh",c=>{Fh=Int(c);return Done;});
            // Monster.setDamageBlockedUntil（Monster.java:1701）：绝对毫秒时间戳，之前免疫伤害。
            Bind(Host,"setDamageBlockedUntil",c=>{BlockDamageUntil((long)JsConvert.ToNumber(c.Arg(0)));return Done;});
            Bind(Host,"setHp",c=>{SetHp((long)JsConvert.ToNumber(c.Arg(0)));return Done;});
            Bind(Host,"addHp",c=>{SetHp(CurrentHp+(long)JsConvert.ToNumber(c.Arg(0)));return Done;});
            // Monster.setPosition(Point)：登记生成前的坐标（LifeFactory.getMonster 之后、spawnMonster 之前）。
            Bind(Host,"setPosition",c=>
            {
                var point=c.Arg(0) as JsHostObject;
                if(point==null)return Done;
                X=(float)JsConvert.ToNumber(point.Get("x"));Y=(float)JsConvert.ToNumber(point.Get("y"));
                Host.Set("position",point);
                if(Visible!=null)Visible.ScriptMoveTo(X,Y,false);
                return Done;
            });
            // Monster.changeDifficulty(difficulty, pqMob)：PiratePQ 按队伍难度调怪。
            // 源端 MonsterStats.setChange 全工程无调用点，changeable 恒 false，实际总是 no-op。
            Bind(Host,"changeDifficulty",c=>{ChangeDifficulty(Int(c),JsConvert.ToBoolean(c.Arg(1)));return Done;});
        }
        public long? ExpOverride;
        // 公式与 mobHpVal 表在 ScriptMonster.ChangeableStats（模板怪与图内怪共用同一份源端口径）。
        void ChangeDifficulty(int difficulty,bool pqMob)
        {
            var diff=ScriptMonster.ChangeableStats(Id,difficulty,pqMob);
            if(!diff.HasValue)return;
            MaxHp=Math.Max(1,diff.Value.hp);SetHp(MaxHp);
            ExpOverride=diff.Value.exp;
            if(Visible!=null){Visible.expOverride=diff.Value.exp;Visible.NativeController?.RestoreFieldHp(MaxHp);}
        }
        public int Fh;
        long damageBlockedUntil;
        public NativeEventMap Map=>map;
        // 源端 Monster.getHp()：活着读实体血量，没渲染时才用镜像值。
        public long CurrentHp=>Visible!=null?Visible.NativeController?.CurrentHp ?? (long)Visible.hp:Hp;
        // 地图像素坐标（Y 向下），与脚本 side 的 Point 同口径。
        public Vector2 Position=>Visible!=null?new Vector2(Visible.transform.position.x*Art.PPU,-Visible.transform.position.y*Art.PPU):new Vector2(X,Y);
        public void SetHp(long value)
        {
            value=Math.Max(0,Math.Min(MaxHp,value));
            Hp=value;
            Visible?.NativeController?.RestoreFieldHp(value);
        }
        public void BlockDamageUntil(long timestamp)
        {
            damageBlockedUntil=timestamp;
            long remaining=timestamp-NativeEventScripts.Clock();
            if(remaining>0)Visible?.NativeController?.BlockFor((int)remaining);
        }
        // LifeFactory.getMonster 之后、进图之前写在模板上的字段（setFh / setDamageBlockedUntil / addHp）。
        internal void ApplyTemplate(ScriptMonster template)
        {
            if(template.FhSet)Fh=template.Fh;
            damageBlockedUntil=template.DamageBlockedUntil;
            if(template.HpOverride>=0)Hp=Math.Max(0,Math.Min(MaxHp,template.HpOverride));
            // changeDifficulty 的结果：源端 changeLevel 同时改 hp 与 maxHp（ostats.hp），
            // 所以这里不走 clamp，直接重设 MaxHp。
            if(template.DifficultyStats.HasValue)
            {
                MaxHp=Math.Max(1,template.DifficultyStats.Value.hp);Hp=MaxHp;ExpOverride=template.DifficultyStats.Value.exp;
            }
        }
        // SeedTowerCompat.controls：脚本在 spawn 之后写 seedControlled.<mapId>.<oid>，
        // 所以这里必须在生成之后惰性读取（EnemyUnit 每 0.5s 问一次），不能只在 Spawn 时判一次。
        public bool Controlled => map.Instance.Property("seedControlled."+map.Id+"."+ObjectId)=="1";
        // SeedTowerCompat.move 的落点：脚本改服务端坐标，同时让本地 AI 不再自己走。
        public void ScriptMove(float x,float y,bool flying)
        {
            X=x;Y=y;
            if(Visible!=null)Visible.ScriptMoveTo(x,y,flying);
        }
        public void Render(FieldDirector field)
        {
            if(Visible!=null)return;
            Visible=field.SpawnNativeMob(Id,new Vector3(X/Art.PPU,-Y/Art.PPU,0),false,eventOwned:true);
            if(Visible==null)throw new JsRuntimeException("Cannot render event monster: "+Id);
            Visible.ScriptControlProbe=()=>Controlled;
            Visible.NativeController?.RestoreFieldHp(Hp);
            if(ExpOverride.HasValue)Visible.expOverride=ExpOverride.Value;
            // 进图前登记的免疫窗口（LucidBossCompat 的暴怒失败怪 8880154）。
            long remaining=damageBlockedUntil-NativeEventScripts.Clock();
            if(remaining>0)Visible.NativeController?.BlockFor((int)remaining);
        }
        public void Detach()
        {
            if(Visible==null)return;
            Hp=Visible.dead?0:Visible.NativeController?.CurrentHp ?? (long)Visible.hp;
            X=Visible.transform.position.x*Art.PPU;Y=-Visible.transform.position.y*Art.PPU;Visible=null;
        }
    }
}
