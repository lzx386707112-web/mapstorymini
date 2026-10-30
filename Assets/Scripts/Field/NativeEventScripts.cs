using System;
using MapleDash.Js;
using UnityEngine;

// Channel event engine outlives individual map/NPC conversations.
public sealed class NativeEventScripts : MonoBehaviour
{
    static NativeEventScripts active;
    NativeEventScriptService service;
    object character;
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
            host.service?.Clear();host.character=GameSession.Character;
            host.service=new NativeEventScriptService(ScriptSource.LoadKey,()=>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),engine=>
            {
                var java=NpcApi.BuildJava();
                engine.Interp.ResolveHostType=type=>GameServer.Local?.Scripts.ResolveHostType(type)
                    ?? engine.Interp.Call(engine.Interp.GetMember(java,"type"),java,new JsValue[]{new JsString(type)});
            });
            host.service.TransportationTime=time=>GameServer.Local.Scripts.TransportationTime(GameSession.World,time);
            host.service.ChannelServer=()=>NpcApi.BuildChannelServer(NpcApi.BuildPlayer(NpcSession.MapContext()));
            host.service.Error=error=>Debug.LogError("[EventScript] "+error.Message);
            if(GameServer.Local!=null)host.service.LobbyDelayMillis=GameConfigDao.GetInt(GameServer.Local.Db,"event_lobby_delay",10)*1000L;
            // Visible event clocks and private map state are not yet represented by
            // the field renderer. Keep those capabilities explicit, not shared-map aliases.
            host.service.Clock=(instance,seconds)=>
            {
                if(seconds.HasValue)Debug.LogWarning("[EventScript] Clock display not migrated: "+instance.Name+"/"+seconds.Value);
            };
        }
        return host;
    }
    public static JsValue Manager(string name) => Current().service.GetManager(name)?.Host ?? (JsValue)JsNull.Value;
    public static JsValue Instance() => active!=null && ReferenceEquals(active.character,GameSession.Character)
        ? active.service?.GetInstance(GameSession.CharacterId)?.Host ?? (JsValue)JsNull.Value : JsNull.Value;
    public static void ChangedMap(int map,bool after)
    {
        if(active==null || !ReferenceEquals(active.character,GameSession.Character))return;
        var instance=active?.service?.GetInstance(GameSession.CharacterId);
        if(instance==null)return;
        try { instance.ChangedMap(NpcApi.BuildPlayer(NpcSession.MapContext()),map,after); }
        catch(Exception error) { Debug.LogError("[EventScript] map "+map+": "+error.Message); }
    }
    void Update()
    {
        if(service==null)return;
        if(!ReferenceEquals(character,GameSession.Character)) { service.Clear();service=null;return; }
        service.Tick();
    }
    void OnDestroy()
    {
        service?.Clear();if(active==this)active=null;
    }
}
