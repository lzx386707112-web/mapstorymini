using System;
using MapleDash.Js;
using UnityEngine;

// Real-player field entry only; bot entry must never execute map scripts.
public sealed class NativeMapScripts : MonoBehaviour
{
    readonly NativeMapScriptService scripts=new NativeMapScriptService();
    readonly NativeBossTimeline timers=new NativeBossTimeline();
    object character;
    int characterId;
    FieldDirector field;
    public static void Enter(FieldDirector field)
    {
        NativeMapIntro.Enter(field);
        var host=field.GetComponent<NativeMapScripts>() ?? field.gameObject.AddComponent<NativeMapScripts>();
        host.field=field;
        if (!ReferenceEquals(host.character,GameSession.Character))
        {
            host.scripts.EndCharacter(host.characterId);
            host.timers.Clear();
            host.character=GameSession.Character; host.characterId=GameSession.CharacterId;
        }
        host.Run(field.Map.onFirstUserEnter,true);
        host.Run(field.Map.onUserEnter,false);
    }
    void Run(string name,bool first)
    {
        if (string.IsNullOrEmpty(name)) return;
        int uiLock=PlayerCtrl.ScriptUiLock;
        try
        {
            var context=NpcSession.MapContext();
            var ms=NpcApi.Build(context,0,0,false);
            // Reject still-empty presentation methods rather than reporting a successful event.
            foreach (string method in new[]{"showEffect","mapEffect","showWZEffect","openUI",
                "setDirectionStatus","spawnGuide","removeGuide","talkGuide","resetDojoEnergy",
                "displayCygnusIntro","displayAranIntro","startExplorerExperience"})
            {
                string missing=method;
                ms.Set(method,JsFunction.FromNative(method,_=>throw new JsRuntimeException("Unmigrated map API: "+missing)));
            }
            ms.Set("goAdventure",JsFunction.FromNative("goAdventure",c=>
            {
                NativeMapIntro.Show("Effect/Direction3.img/goAdventure/Scene"+(GameSession.Character?.record.gender ?? 0),true);
                return JsUndefined.Value;
            }));
            ms.Set("goLith",JsFunction.FromNative("goLith",c=>
            {
                NativeMapIntro.Show("Effect/Direction3.img/goLith/Scene"+(GameSession.Character?.record.gender ?? 0),true);
                return JsUndefined.Value;
            }));
            ms.Set("playSound",JsFunction.FromNative("playSound",c=>{AudioManager.Play(JsConvert.ToString(c.Arg(0)));return JsUndefined.Value;}));
            ms.Set("changeMusic",JsFunction.FromNative("changeMusic",c=>{AudioManager.PlayBgm(JsConvert.ToString(c.Arg(0)));return JsUndefined.Value;}));
            var mapGetter=ms.Get("getMap");
            var interpreter=new JsInterpreter();
            ms.Set("getMap",JsFunction.FromNative("getMap",c=>
            {
                if(c.ArgCount>0 && JsConvert.ToInt32(c.Arg(0))!=field.Map.id)
                    throw new JsRuntimeException("Unmigrated map API: getMap(remote field)");
                return interpreter.Call(mapGetter,ms,Array.Empty<JsValue>());
            }));
            ms.Set("scheduleMapWarp",JsFunction.FromNative("scheduleMapWarp",c=>
            {
                int expected=JsConvert.ToInt32(c.Arg(0)),target=JsConvert.ToInt32(c.Arg(1));
                long delay=(long)JsConvert.ToNumber(c.Arg(2));
                timers.Schedule(Now,delay,()=>field!=null && field.Map?.id==expected,
                    ()=>field.WarpTo(target,null,0));
                return JsUndefined.Value;
            }));
            scripts.Run(characterId,field.Map.id,(first?"onFirstUserEnter/":"onUserEnter/")+name,first,
                ScriptSource.LoadKey,ms,engine=>
                {
                    var java=NpcApi.BuildJava();
                    engine.Interp.ResolveHostType=type=>engine.Interp.Call(engine.Interp.GetMember(java,"type"),java,new JsValue[]{new JsString(type)});
                });
        }
        catch(Exception error)
        {
            PlayerCtrl.ScriptUiLock=uiLock;
            Debug.LogError("[MapScript] "+field.Map.id+"/"+name+": "+error.Message);
        }
    }
    static long Now=>(long)(Time.realtimeSinceStartupAsDouble*1000);
    void Update()
    {
        if(!ReferenceEquals(character,GameSession.Character)) { timers.Clear();return; }
        timers.Tick(Now);
    }
}
