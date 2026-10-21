using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class NativeVfxFrame
{
    public string file;
    public Vector2 origin;
    public float delay, alpha0, alpha1;
    public int z;
}
public sealed class NativeVfxClip
{
    public string path;
    public NativeVfxFrame[] frames;
    public Dictionary<string,object> meta;
    public float Duration { get { float sum=0; foreach(var f in frames) sum+=Mathf.Max(.001f,f.delay); return sum; } }
}
public static class NativeSkillVfx
{
    static readonly Dictionary<string,Dictionary<string,NativeVfxClip>> skills = new Dictionary<string,Dictionary<string,NativeVfxClip>>();
    static readonly Dictionary<string,Dictionary<string,object>> metadata = new Dictionary<string,Dictionary<string,object>>();
    static void Ensure()
    {
        if (skills.Count>0) return;
        var root=(Dictionary<string,object>)MiniJson.Parse(Resources.Load<TextAsset>("GameplaySkillVfx").text);
        foreach(Dictionary<string,object> skill in (List<object>)root["skills"])
        {
            string key=(string)skill["key"];
            var clips=new Dictionary<string,NativeVfxClip>();
            foreach(var pair in (Dictionary<string,object>)skill["clips"])
            {
                var entry=(Dictionary<string,object>)pair.Value;
                var frames=new List<NativeVfxFrame>();
                foreach(Dictionary<string,object> f in (List<object>)entry["frames"])
                {
                    var origin=(List<object>)f["origin"];
                    frames.Add(new NativeVfxFrame {file=(string)f["file"], origin=new Vector2(Convert.ToSingle(origin[0]),Convert.ToSingle(origin[1])),
                        delay=Mathf.Max(.001f,Art.Num(f,"delay",.1f)),alpha0=Art.Num(f,"a0",255)/255,alpha1=Art.Num(f,"a1",255)/255,z=(int)Art.Num(f,"z")});
                }
                clips.Add(pair.Key,new NativeVfxClip {path=pair.Key,frames=frames.ToArray(),meta=(Dictionary<string,object>)entry["meta"]});
            }
            skills.Add(key,clips); metadata.Add(key,(Dictionary<string,object>)skill["metadata"]);
        }
    }
    public static NativeVfxClip Get(string key,string path)
    {
        Ensure();
        if (!skills.TryGetValue(key,out var clips)) return null;
        return clips.TryGetValue(path,out var clip) ? clip : null;
    }
    public static List<NativeVfxClip> Parts(string key,string branch)
    {
        Ensure(); var result=new List<NativeVfxClip>();
        if (skills.TryGetValue(key,out var clips))
            foreach(var pair in clips)
                if (pair.Key==branch || pair.Key.StartsWith(branch+"/",StringComparison.Ordinal)) result.Add(pair.Value);
        result.Sort((a,b)=>string.CompareOrdinal(a.path,b.path));
        return result;
    }
    public static Dictionary<string,object> Meta(string key,string path)
    {
        Ensure();
        return metadata.TryGetValue(key,out var entries) && entries.TryGetValue(path,out var value) ? (Dictionary<string,object>)value : null;
    }
    public static float Number(string key,string path,string field,float fallback=0)
    {
        var meta=Meta(key,path);return meta==null ? fallback : Art.Num(meta,field,fallback);
    }
    public static bool Has(string key,string branch) => Parts(key,branch).Count>0;
    public static string Alias(string part)
    {
        if(part=="cast") return "effect";
        if(part=="cast2") return "effect0";
        return part;
    }
}
