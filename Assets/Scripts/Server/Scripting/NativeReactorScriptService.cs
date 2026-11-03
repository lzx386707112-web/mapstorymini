using System;
using System.Collections.Generic;
using MapleDash.Js;
using Server.Field;

// ReactorScriptManager selects numeric reactor/<id>.js, not IMG action text.
// Run the original act() body; host methods retain map/position context.
//
// 反应堆脚本是副本流程的一部分，不是装饰：
//   reactor/2202003.js  rm.dropItems()            玩具塔 2/5 关的通行证全在箱子里
//   reactor/2201001.js  rm.spawnMonster(9300007)  玩具塔 3 关：砸 8 个箱子刷出 24 只怪，凑够 32 张
//   reactor/2201002.js  rm.spawnMonster(9300010,1, -211)  玩具塔 7 关：砸箱子才出 3 只战甲鱼
//   reactor/2201003.js  rm.spawnMonster(...)      玩具塔 9 关：祭坛召出阿丽莎乐
//   reactor/3002001.js  rm.getEventInstance().showClearEffect(rm.getMap().getId()); rm.dropItems()
//   reactor/3001000.js  rm.playerMessage(...) + rm.spawnMonster(9300180, 1)  毒雾森林：召出毒液石头人
//   reactor/2519000~2519003.js  rm.getReactor().getMap()  海盗船四个动力门
//
// 绑定面按 ReactorActionManager 的公开方法逐个对齐（dropItems/sprayItems 的各层重载、
// spawnMonster/spawnNpc/mapMessage/playerMessage/getReactor/getMap/getPlayer/
// getEventInstance/getPosition），未迁移的成员一律记日志后忽略——反应堆脚本出问题
// 不能把玩家的地图流程带崩。
public sealed class NativeReactorScriptService : IDisposable
{
    readonly Func<string,string> source;
    readonly List<(MapRuntime map,MapNpcPosition npc)> spawned=new List<(MapRuntime,MapNpcPosition)>();
    public void Dispose()
    {
        foreach(var entry in spawned)entry.map.npcs.Remove(entry.npc);
        spawned.Clear();
    }
    public Action<int,int,string> Message;
    public Func<int,JsValue> EventInstance;
    // ReactorActionManager.getPlayer(int owner)。起源之塔第 18 层 reactor/9922003.js 就是
    // rm.getEventInstance() + rm.getPlayer() + rm.getReactor() 三件套转给事件脚本。
    public Func<int,JsValue> Player;
    // ReactorActionManager.getMap()：事件实例地图的脚本宿主（只有实例图才拿得到）。
    // 毒雾森林 3002001/3009000、海盗船四个动力门都靠 rm.getMap().getId() 收尾。
    public Func<int,JsValue> Map;
    // ReactorActionManager.spawnMonster(id[,qty][,x,y])：刷进实例地图（NativeEventMap.SpawnAt）。
    // 组队任务的「砸箱子出怪」全走这里，接线缺失只影响出怪、不影响掉券。
    public Func<int,int,float,float,int,bool> SpawnMonster;
    public Action<Exception> Error;
    public NativeReactorScriptService(Func<string,string> source) { this.source=source; }
    public bool Act(MapReactor reactor,int owner)
    {
        if (reactor == null) return false;
        string key="reactor/"+reactor.id;
        string text=source?.Invoke(key);
        if(string.IsNullOrEmpty(text))return false;
        try
        {
            var rm=new JsObject();
            Bind(rm,"dropItems",c=>{ Drop(reactor,owner,c); return JsUndefined.Value; });
            Bind(rm,"sprayItems",c=>{ Drop(reactor,owner,c); return JsUndefined.Value; });
            Bind(rm,"mapMessage",c=>
            { Message?.Invoke(reactor.mapId,JsConvert.ToInt32(c.Arg(0)),JsConvert.ToString(c.Arg(1)));return JsUndefined.Value; });
            // playerMessage(type, text)：单机只有一个真实角色，落到系统提示上。
            Bind(rm,"playerMessage",c=>
            { Message?.Invoke(reactor.mapId,JsConvert.ToInt32(c.Arg(0)),JsConvert.ToString(c.Arg(1)));return JsUndefined.Value; });
            Bind(rm,"spawnMonster",c=>{ Spawn(reactor,c); return JsUndefined.Value; });
            Bind(rm,"spawnNpc",c=>
            {
                var map=MapService.Get(reactor.mapId);
                if(map==null)throw new JsRuntimeException("Reactor map is no longer loaded");
                var npc=new MapNpcPosition { id=JsConvert.ToInt32(c.Arg(0)),x=reactor.x,y=reactor.y,dynamic=true };
                map.npcs.Add(npc);spawned.Add((map,npc));
                return JsUndefined.Value;
            });
            Bind(rm,"getEventInstance",c=>EventInstance?.Invoke(reactor.mapId) ?? JsNull.Value);
            // ReactorActionManager.getPlayer()：谁打的就是谁（源端 hitReactor 传进来的 player）。
            Bind(rm,"getPlayer",c=>Player?.Invoke(owner) ?? JsNull.Value);
            Bind(rm,"getMap",c=>MapOf(reactor));
            // 源端 react.getPosition() 是反应堆坐标上移 10px（掉落物从箱子上沿弹出来）。
            Bind(rm,"getPosition",c=>Point(reactor.x,reactor.y-10));
            // ReactorActionManager.getReactor()：脚本面只需要读状态/坐标这几项（Reactors 名表里
            // 30 个脚本用 getReactor，其中就包括起源之塔的 seedCoconut 三个调用点）。
            var reactorHost=new JsHostObject(reactor);
            reactorHost.Missing=name=>throw new JsRuntimeException("Unmigrated reactor API: "+name);
            void BindReactor(string name,Func<JsCall,JsValue> call)=>reactorHost.Set(name,JsFunction.FromNative(name,call));
            BindReactor("getId",c=>new JsNumber(reactor.id));
            BindReactor("getObjectId",c=>new JsNumber(reactor.objectId));
            BindReactor("getState",c=>new JsNumber(reactor.state));
            BindReactor("getName",c=>new JsString(reactor.name ?? ""));
            BindReactor("isAlive",c=>JsBoolean.Of(reactor.alive));
            BindReactor("getPosition",c=>Point(reactor.x,reactor.y));
            // 2519000~2519003 是 rm.getReactor().getMap() 的写法，跟 rm.getMap() 同一个宿主。
            BindReactor("getMap",c=>MapOf(reactor));
            rm.Set("getReactor",JsFunction.FromNative("getReactor",c=>reactorHost));
            var engine=new JsEngine(text,key);engine.Run(scope=>scope.Declare("rm",rm));
            if(!engine.HasFunction("act"))return false;
            engine.Call("act");return true;
        }
        catch(Exception error) { Error?.Invoke(error);return false; }
    }

    static void Bind(JsObject host,string name,Func<JsCall,JsValue> action)
        => host.Set(name,JsFunction.FromNative(name,action));

    static JsValue Point(float x,float y)
    {
        var point=new JsHostObject(null);
        point.Set("x",new JsNumber(x));point.Set("y",new JsNumber(y));
        point.Set("getX",JsFunction.FromNative("getX",_=>new JsNumber(x)));
        point.Set("getY",JsFunction.FromNative("getY",_=>new JsNumber(y)));
        return point;
    }

    // rm.getMap()：优先用实例地图宿主；没有实例（普通野外图的反应堆）时退回一个只认
    // getId() 的最小宿主——脚本里 `rm.getMap().getId()` 是最常见的一句，不能因为没接实例就崩。
    JsValue MapOf(MapReactor reactor)
    {
        var host=Map?.Invoke(reactor.mapId);
        if (host != null && host is not JsNull && host is not JsUndefined) return host;
        var fallback=new JsHostObject(null);
        fallback.Missing=name=>throw new JsRuntimeException("Unmigrated map API: "+name);
        fallback.Set("getId",JsFunction.FromNative("getId",_=>new JsNumber(reactor.mapId)));
        fallback.Set("getMapId",JsFunction.FromNative("getMapId",_=>new JsNumber(reactor.mapId)));
        return fallback;
    }

    // ReactorActionManager.dropItems / sprayItems 的各层重载：
    //   ()                                          -> 不掉钱，只按 reactordrops 表掷
    //   (meso, mesoChance, minMeso, maxMeso)        -> 同上 + 掉钱
    //   (meso, mesoChance, minMeso, maxMeso, minItems)
    //   (posX, posY, meso, mesoChance, minMeso, maxMeso[, minItems])
    // 统一折到 NativeReactorDrops.Drop（源端也是最终都走这一个入口）。
    void Drop(MapReactor reactor,int owner,JsCall call)
    {
        bool meso=false;
        int mesoChance=1,minMeso=0,maxMeso=0,minItems=0;
        // 从第 0 个参数开始的是 4/5 参那两档；从第 2 个参数开始的是带 posX/posY 的 6/7 参档。
        int at=call.ArgCount>=6?2:call.ArgCount>=4?0:-1;
        if(at>=0)
        {
            meso=JsConvert.ToBoolean(call.Arg(at));
            mesoChance=Math.Max(1,JsConvert.ToInt32(call.Arg(at+1)));
            minMeso=JsConvert.ToInt32(call.Arg(at+2));
            maxMeso=JsConvert.ToInt32(call.Arg(at+3));
            if(call.ArgCount>at+4)minItems=Math.Max(0,JsConvert.ToInt32(call.Arg(at+4)));
        }
        try { NativeReactorDrops.Drop(reactor,owner,meso,mesoChance,minMeso,maxMeso,minItems); }
        catch(Exception error) { Error?.Invoke(error); }
    }

    // ReactorActionManager.spawnMonster 的四层重载：
    //   (id) / (id, qty) / (id, x, y) / (id, qty, x, y)
    // 源端是 spawnMonsterOnGroundBelow（落在脚下）。接线缺失时只记日志：出怪失败不该
    // 连带把同一段脚本里的 dropItems 也吞掉。
    void Spawn(MapReactor reactor,JsCall call)
    {
        int id=JsConvert.ToInt32(call.Arg(0));
        int qty=1;
        float x=reactor.x,y=reactor.y;
        if(call.ArgCount==2)qty=Math.Max(1,JsConvert.ToInt32(call.Arg(1)));
        else if(call.ArgCount==3)
        { x=(float)JsConvert.ToNumber(call.Arg(1));y=(float)JsConvert.ToNumber(call.Arg(2)); }
        else if(call.ArgCount>=4)
        {
            qty=Math.Max(1,JsConvert.ToInt32(call.Arg(1)));
            x=(float)JsConvert.ToNumber(call.Arg(2));
            y=(float)JsConvert.ToNumber(call.Arg(3));
        }
        if(SpawnMonster==null)
        {
            Error?.Invoke(new InvalidOperationException(
                "Reactor spawnMonster is not wired: reactor "+reactor.id+" -> mob "+id));
            return;
        }
        if(!SpawnMonster(reactor.mapId,id,x,y,qty))
            Error?.Invoke(new InvalidOperationException(
                "Reactor spawnMonster failed: map "+reactor.mapId+" mob "+id));
    }
}
