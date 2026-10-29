using System;
using System.Collections.Generic;
using MapleDash.Js;

// MapScriptManager.runMapScript: cached global scope, start(ms), first-entry per character/map/path.
// Independent of NPC dialog sessions. The caller supplies original source and explicit host objects.
public sealed class NativeMapScriptService
{
    readonly Dictionary<string,JsEngine> scripts=new Dictionary<string,JsEngine>();
    readonly Dictionary<int,HashSet<string>> entered=new Dictionary<int,HashSet<string>>();
    public bool Run(int character,int map,string path,bool firstUser,Func<string,string> source,JsValue methods,
        Action<JsEngine> bind=null)
    {
        if (firstUser)
        {
            if (!entered.TryGetValue(character,out var seen)) entered[character]=seen=new HashSet<string>();
            // Source records entry before loading (including a missing or broken script).
            if (!seen.Add(map+":"+path)) return false;
        }
        if (scripts.TryGetValue(path,out var cached))
        {
            try { cached.Call("start",methods);return true; }
            catch (JsRuntimeException) { scripts.Remove(path); } // Original retries with a fresh engine.
        }
        string text=source("map/"+path);
        if (string.IsNullOrEmpty(text)) return false;
        var script=new JsEngine(text,"map/"+path);
        bind?.Invoke(script);
        script.Run();
        scripts[path]=script;
        script.Call("start",methods);
        return true;
    }
    public void EndCharacter(int character) => entered.Remove(character);
    public void Reload() => scripts.Clear();
}
