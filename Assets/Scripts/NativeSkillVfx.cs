using System;
using System.Collections.Generic;
using UnityEngine;
using MapleDash.Wz;

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
    // Presentation identity includes the learned level: some skills store all visual
    // branches below level/N (e.g. each colour of Three Snails).
    public static string Key(int id, int level, string alias = "")
    {
        Ensure();
        string key = id + "@" + level;
        if (skills.ContainsKey(key)) return key;
        // Supplemental audited layers (such as rupture's Map/Effect screen)
        // must not depend on whether an ID or a legacy alias was queried first.
        if (string.IsNullOrEmpty(alias) || !skills.ContainsKey(alias))
            foreach (var entry in (Dictionary<string, object>)WzAssets.Index["skills"])
                if (Convert.ToInt32(entry.Value) == id) { alias = entry.Key; break; }
        var clips = !string.IsNullOrEmpty(alias) && skills.TryGetValue(alias, out var original)
            ? new Dictionary<string, NativeVfxClip>(original) : new Dictionary<string, NativeVfxClip>();
        var meta = !string.IsNullOrEmpty(alias) && metadata.TryGetValue(alias, out var originalMeta)
            ? new Dictionary<string, object>(originalMeta) : new Dictionary<string, object>();
        string path = "Skill/" + (id / 10000).ToString("D3") + ".img/skill/" + id.ToString("D7");
        var root = WzAssets.Node(path);
        var branches = new HashSet<string>();
        var levelNode = root?.Get("level/" + level)?.TryResolve();
        if (root != null) foreach (var child in root.Children) branches.Add(child.Name);
        if (levelNode != null) foreach (var child in levelNode.Children) branches.Add(child.Name);
        foreach (string branch in branches)
        {
            if (branch == "level" || branch.StartsWith("icon", StringComparison.Ordinal) || branch == "action") continue;
            var specific = root?.Get("level/" + level + "/" + branch)?.TryResolve();
            var node = specific ?? root?.Get(branch)?.TryResolve();
            if (node == null) continue;
            // The selected level replaces a branch as a whole; never concatenate
            // levels/variants or render an icon as a frame.
            foreach (string old in new List<string>(clips.Keys))
                if (old == branch || old.StartsWith(branch + "/", StringComparison.Ordinal)) clips.Remove(old);
            Read(node, specific != null ? path + "/level/" + level + "/" + branch : path + "/" + branch,
                branch, clips, meta, new HashSet<ImgNode>());
        }
        skills.Add(key, clips); metadata.Add(key, meta);
        return key;
    }
    static void Read(ImgNode source, string sourcePath, string path,
        Dictionary<string, NativeVfxClip> clips, Dictionary<string, object> metadata, HashSet<ImgNode> ancestors)
    {
        var node = source.TryResolve();
        if (node == null || !ancestors.Add(node)) return;
        var meta = new Dictionary<string, object>();
        foreach (var child in node.Children)
        {
            var value = child.TryResolve();
            if (value != null && value.Value != null && value.Kind != "Canvas") meta[child.Name] = value.Value;
        }
        metadata[path] = meta;
        var frames = new List<NativeVfxFrame>();
        foreach (var frame in WzAssets.Frames(node))
        {
            var canvas = frame.TryResolve();
            if (canvas == null || canvas.Kind != "Canvas") continue;
            frames.Add(new NativeVfxFrame { file = "wz://" + sourcePath + (node.Kind == "Canvas" ? "" : "/" + frame.Name),
                origin = WzAssets.Origin(canvas), delay = Mathf.Max(.001f, Mathf.Abs(canvas.Int("delay", 100)) / 1000f),
                alpha0 = canvas.Int("a0", 255) / 255f, alpha1 = canvas.Int("a1", 255) / 255f,
                z = canvas.Int("z", node.Int("z")) });
        }
        if (frames.Count > 0) clips[path] = new NativeVfxClip { path = path, frames = frames.ToArray(), meta = meta };
        if (node.Kind != "Canvas")
            foreach (var child in node.Children)
            {
                var resolved = child.TryResolve();
                if (resolved != null && resolved.Kind != "Canvas" && resolved.Children.Count > 0)
                    Read(child, sourcePath + "/" + child.Name, path + "/" + child.Name, clips, metadata, ancestors);
            }
        ancestors.Remove(node);
    }
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
                    string source = (string)f["source"];
                    var native = WzAssets.Node(source);
                    var origin = WzAssets.Origin(native);
                    frames.Add(new NativeVfxFrame {file="wz://"+source, origin=origin,
                        delay=Mathf.Max(.001f,Mathf.Abs(native.Int("delay",100))/1000f),alpha0=native.Int("a0",255)/255f,alpha1=native.Int("a1",255)/255f,z=native.Int("z",(int)Art.Num(f,"z"))});
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
