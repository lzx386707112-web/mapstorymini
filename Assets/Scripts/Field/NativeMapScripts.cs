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
            host.scripts.Reload();
            host.timers.Clear();
            host.character=GameSession.Character; host.characterId=GameSession.CharacterId;
        }
        host.Run(ScriptName(field.Map.id,field.Map.onFirstUserEnter),true);
        host.Run(ScriptName(field.Map.id,field.Map.onUserEnter),false);
    }
    public void Reload() { scripts.Reload(); timers.Clear(); }

    // 源端 onUserEnter/onFirstUserEnter 的默认值是「地图 id 字符串」而不是空串
    // （MapFactory.java:193-197：取不到就 String.valueOf(mapid)），所以 MapleMap.addPlayer 对
    // **每一张**图都会尝试 map/onUserEnter/<mapid>.js（文件不存在时 runMapScript 返回 false）。
    // 移植端原来只在 WZ 里写了 onUserEnter 才跑，于是 map/onUserEnter/677000003.js 这类
    // 「按图 id 命名」的脚本（野外 Boss 现身、过场）全都成了死代码。
    static string ScriptName(int mapId,string declared)
    {
        if(!string.IsNullOrEmpty(declared)) return declared;
        // 这 3 张图的 onUserEnter 只写在服务端 wz 里，客户端 IMG 的 info 没有；
        // 全量对比 1518 张带进入脚本的地图，只有这 3 张不一致（都是枫城的野外 Boss）。
        if(ServerOnlyEnterScripts.TryGetValue(mapId,out string name)) return name;
        return mapId.ToString();
    }
    static readonly System.Collections.Generic.Dictionary<int,string> ServerOnlyEnterScripts=
        new System.Collections.Generic.Dictionary<int,string>
        {
            {800040208,"armoredSamuraiBoss"},   // 甲胄武士
            {800040401,"emperorToadBoss"},      // 枫城天皇蟾蜍（天守阁 2 阶）
            {800040410,"emperorToadBoss"},      // 同上（最上层）
        };

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
