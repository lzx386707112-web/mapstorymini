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
            var engine=new JsEngine(text,key);engine.Run(scope=>scope.Declare("rm",rm));
            if(!engine.HasFunction("act"))return false;
            engine.Call("act");return true;
        }
        catch(Exception error) { Error?.Invoke(error);return false; }
    }
}
