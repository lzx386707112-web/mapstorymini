using System;
using System.Collections.Generic;
using MapleDash.Js;
using Server.Field;

// ReactorScriptManager selects numeric reactor/<id>.js, not IMG action text.
// Run the original act() body; host methods retain map/position context.
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
    public Action<Exception> Error;
    public NativeReactorScriptService(Func<string,string> source) { this.source=source; }
    public bool Act(MapReactor reactor,int owner)
    {
        string key="reactor/"+reactor.id;
        string text=source?.Invoke(key);
        if(string.IsNullOrEmpty(text))return false;
        try
        {
            var rm=new JsObject();
            rm.Set("dropItems",JsFunction.FromNative("dropItems",c=>
            {
                if (c.ArgCount!=5) throw new JsRuntimeException("Reactor dropItems overload is not migrated: "+c.ArgCount);
                NativeReactorDrops.Drop(reactor,owner,JsConvert.ToBoolean(c.Arg(0)),
                    JsConvert.ToInt32(c.Arg(1)),JsConvert.ToInt32(c.Arg(2)),
                    JsConvert.ToInt32(c.Arg(3)),JsConvert.ToInt32(c.Arg(4)));
                return JsUndefined.Value;
            }));
            rm.Set("mapMessage",JsFunction.FromNative("mapMessage",c=>
            { Message?.Invoke(reactor.mapId,JsConvert.ToInt32(c.Arg(0)),JsConvert.ToString(c.Arg(1)));return JsUndefined.Value; }));
            rm.Set("spawnNpc",JsFunction.FromNative("spawnNpc",c=>
            {
                var map=MapService.Get(reactor.mapId);
                if(map==null)throw new JsRuntimeException("Reactor map is no longer loaded");
                var npc=new MapNpcPosition { id=JsConvert.ToInt32(c.Arg(0)),x=reactor.x,y=reactor.y,dynamic=true };
                map.npcs.Add(npc);spawned.Add((map,npc));
                return JsUndefined.Value;
            }));
            rm.Set("getEventInstance",JsFunction.FromNative("getEventInstance",c=>EventInstance?.Invoke(reactor.mapId) ?? JsNull.Value));
            // ReactorActionManager.getPlayer()：谁打的就是谁（源端 hitReactor 传进来的 player）。
            rm.Set("getPlayer",JsFunction.FromNative("getPlayer",c=>Player?.Invoke(owner) ?? JsNull.Value));
            // ReactorActionManager.getReactor()：脚本面只需要读状态/坐标这几项（Reactors 名表里
            // 30 个脚本用 getReactor，其中就包括起源之塔的 seedCoconut 三个调用点）。
            var reactorHost=new JsHostObject(reactor);
            reactorHost.Missing=name=>throw new JsRuntimeException("Unmigrated reactor API: "+name);
            void Bind(string name,Func<JsCall,JsValue> call)=>reactorHost.Set(name,JsFunction.FromNative(name,call));
            Bind("getId",c=>new JsNumber(reactor.id));
            Bind("getObjectId",c=>new JsNumber(reactor.objectId));
            Bind("getState",c=>new JsNumber(reactor.state));
            Bind("getName",c=>new JsString(reactor.name ?? ""));
            Bind("isAlive",c=>JsBoolean.Of(reactor.alive));
            Bind("getPosition",c=>
            {
                var point=new JsHostObject(null);
                point.Set("x",new JsNumber(reactor.x));point.Set("y",new JsNumber(reactor.y));
                point.Set("getX",JsFunction.FromNative("getX",_=>new JsNumber(reactor.x)));
                point.Set("getY",JsFunction.FromNative("getY",_=>new JsNumber(reactor.y)));
                return point;
            });
            rm.Set("getReactor",JsFunction.FromNative("getReactor",c=>reactorHost));
            var engine=new JsEngine(text,key);engine.Run(scope=>scope.Declare("rm",rm));
            if(!engine.HasFunction("act"))return false;
            engine.Call("act");return true;
        }
        catch(Exception error) { Error?.Invoke(error);return false; }
    }
}
