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
    // 282 用「首帧 delay 明显长于后续帧」表示整层晚出现：effect2 首帧 450ms、
    // 后续 60ms；effect4 首帧 780ms、effect3 首帧 1020ms。这不是普通帧时长，
    // 而是这一层的起始偏移，直接同帧播放会把后段特效提前叠上来。
    public float StartDelay
    {
        get
        {
            if (frames.Length < 2) return 0;
            float rest = 0;
            for (int i = 1; i < frames.Length; i++) rest += frames[i].delay;
            rest /= frames.Length - 1;
            float first = frames[0].delay;
            return first > .15f && first > rest * 2f ? first : 0;
        }
    }
}
// 282 全屏技能视频层（Canvas#Video）：载荷是完整 MCV0，交给 NativeSkillVideo 播。
// z 决定前后景：负值画在施法者之后，正值画在前（与普通帧层同一口径）。
public sealed class NativeVfxVideo
{
    public string path;
    public ImgNode node;
    public int z;
    public float delay;
    public bool loop;
}
public static class NativeSkillVfx
{
    static readonly Dictionary<string,Dictionary<string,NativeVfxClip>> skills = new Dictionary<string,Dictionary<string,NativeVfxClip>>();
    static readonly Dictionary<string,Dictionary<string,object>> metadata = new Dictionary<string,Dictionary<string,object>>();
    static readonly Dictionary<string,List<NativeVfxVideo>> videos = new Dictionary<string,List<NativeVfxVideo>>();
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
        var layerVideos = !string.IsNullOrEmpty(alias) && videos.TryGetValue(alias, out var originalVideos)
            ? new List<NativeVfxVideo>(originalVideos) : new List<NativeVfxVideo>();
        string path = "Skill/" + (NativeSoulWeapon.IsSkill(id)?800128:id / 10000).ToString("D3") + ".img/skill/" + id.ToString("D7");
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
            layerVideos.RemoveAll(v => v.path == branch || v.path.StartsWith(branch + "/", StringComparison.Ordinal));
            string nodePath = specific != null ? path + "/level/" + level + "/" + branch : path + "/" + branch;
            // number / number2 不是动画：282 用宽字符常量
            // "Skill/%03d.img/skill/%07d/number/icon%d" 按索引取图，index 是运行时连击/层数。
            // 子节点 icon1..icon99 是「同一位置的 N 个数字」，交给 ReadIcons 单独收。
            if (branch == "number" || branch == "number2")
            {
                ReadIcons(node, nodePath, branch, clips);
                continue;
            }
            Read(node, nodePath, branch, clips, meta, layerVideos, new HashSet<ImgNode>());
        }
        // 96C052..96C166: movement effects live outside Skill.wz.
        string movementEffect = id == 4111006 ? "Flying" : id == 14101004 ? "Flying1" :
            id == 11101005 ? "SoulRush" : null;
        if (movementEffect != null)
        {
            string effectPath = "Effect/BasicEff.img/" + movementEffect;
            var effectNode = WzAssets.Node(effectPath);
            if (effectNode != null) Read(effectNode, effectPath, "movement", clips, meta, layerVideos, new HashSet<ImgNode>());
        }
        skills.Add(key, clips); metadata.Add(key, meta); videos.Add(key, layerVideos);
        return key;
    }
    /// <summary>
    /// 帧元数据（origin/delay/z/a0/a1）该从哪个节点读。
    /// 282 分体画布：占位节点带全部元数据、outlink 目标是只有像素的光板，
    /// 所以优先占位节点；内嵌像素的老资源两者同一节点，退回 canvas 即可。
    /// </summary>
    static ImgNode MetaNode(ImgNode frame, ImgNode canvas) =>
        frame != null && frame.Kind == "Canvas" ? frame : canvas;

    static void Read(ImgNode source, string sourcePath, string path,
        Dictionary<string, NativeVfxClip> clips, Dictionary<string, object> metadata,
        List<NativeVfxVideo> layerVideos, HashSet<ImgNode> ancestors)
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
            // 282 的 `Skill/<file>.img` 只留 1x1 占位 + `_outlink`，真画布在 `Skill/_Canvas/...`；
            // **origin / delay / z / a0 / a1 全写在占位节点上，outlink 目标是光板**。
            // 之前从 canvas 读，这些字段全退化成 origin=(0,0) / delay=100ms / 无渐隐，
            // 表现就是特效整体偏移半个画布、帧率变慢（钢铁之躯 effect 实际 60ms 被当成 100ms）。
            var frameMeta = MetaNode(frame, canvas);
            frames.Add(new NativeVfxFrame { file = "wz://" + sourcePath + (node.Kind == "Canvas" ? "" : "/" + frame.Name),
                origin = WzAssets.Origin(frameMeta), delay = Mathf.Max(.001f, Mathf.Abs(frameMeta.Int("delay", 100)) / 1000f),
                alpha0 = frameMeta.Int("a0", 255) / 255f, alpha1 = frameMeta.Int("a1", 255) / 255f,
                z = frameMeta.Int("z", node.Int("z")) });
        }
        if (frames.Count > 0) clips[path] = new NativeVfxClip { path = path, frames = frames.ToArray(), meta = meta };
        if (node.Kind != "Canvas")
            foreach (var child in node.Children)
            {
                var resolved = child.TryResolve();
                if (resolved == null) continue;
                // 282 全屏技能视频：video 节点没子节点，不会被帧分支收走，单独登记成层。
                if (resolved.Kind == "Canvas#Video")
                {
                    layerVideos.Add(new NativeVfxVideo
                    {
                        path = path,
                        node = resolved,
                        z = node.Int("z"),
                        delay = Mathf.Max(0, node.Int("delay")) / 1000f,
                        loop = path.EndsWith("/loop", StringComparison.Ordinal),
                    });
                    continue;
                }
                if (resolved.Kind != "Canvas" && resolved.Children.Count > 0)
                    Read(child, sourcePath + "/" + child.Name, path + "/" + child.Name, clips, metadata, layerVideos, ancestors);
            }
        ancestors.Remove(node);
    }
    // number/icon%d：每张 icon 是同一位置上的一个数字，不是动画帧。
    // 声明成单帧 clip（delay=1s，播完即停），按 icon 序号排序后由调用方按层数取用。
    static void ReadIcons(ImgNode source, string sourcePath, string branch,
        Dictionary<string,NativeVfxClip> clips)
    {
        var node = source.TryResolve();
        if (node == null) return;
        var ordered = new List<KeyValuePair<int, ImgNode>>();
        foreach (var child in node.Children)
        {
            if (!child.Name.StartsWith("icon", StringComparison.Ordinal)) continue;
            if (!int.TryParse(child.Name.Substring(4), out int index) || index <= 0) continue;
            ordered.Add(new KeyValuePair<int, ImgNode>(index, child));
        }
        ordered.Sort((a, b) => a.Key.CompareTo(b.Key));
        foreach (var pair in ordered)
        {
            var canvas = pair.Value.TryResolve();
            if (canvas == null || canvas.Kind != "Canvas") continue;
            var meta = MetaNode(pair.Value, canvas);
            string clipPath = branch + "/" + pair.Value.Name;
            clips[clipPath] = new NativeVfxClip
            {
                path = clipPath,
                frames = new[]
                {
                    new NativeVfxFrame
                    {
                        file = "wz://" + sourcePath + "/" + pair.Value.Name,
                        origin = WzAssets.Origin(meta),
                        delay = 1f,
                        alpha0 = 1f,
                        alpha1 = 1f,
                        z = meta.Int("z", node.Int("z")),
                    },
                },
                meta = new Dictionary<string, object> { { "index", pair.Key } },
            };
        }
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
    // 一级分支下再一层子分支名（frameEffect/0、frameEffect/1…）。
    // 三段层常带这一层壳，枚举不了就只能播到第 0 个变体。
    public static List<string> SubBranches(string key,string root)
    {
        Ensure();
        var result=new List<string>();
        var seen=new HashSet<string>(StringComparer.Ordinal);
        if(skills.TryGetValue(key,out var clips))
            foreach(var path in clips.Keys)
            {
                if(!path.StartsWith(root+"/",StringComparison.Ordinal))continue;
                string rest=path.Substring(root.Length+1);
                int slash=rest.IndexOf('/');
                if(slash<=0)continue;
                string sub=root+"/"+rest.Substring(0,slash);
                if(seen.Add(sub))result.Add(sub);
            }
        result.Sort(StringComparer.Ordinal);
        return result;
    }
    // number / number2：按 icon 序号排好的数字图集，调用方按层数取对应一张。
    public static List<NativeVfxClip> Icons(string key,string branch)
    {
        Ensure();
        var result=new List<NativeVfxClip>();
        if (skills.TryGetValue(key,out var clips))
            foreach(var pair in clips)
                if (pair.Key.StartsWith(branch+"/",StringComparison.Ordinal)) result.Add(pair.Value);
        result.Sort((a,b)=>IconIndex(a).CompareTo(IconIndex(b)));
        return result;
    }
    static int IconIndex(NativeVfxClip clip) =>
        clip.meta!=null && clip.meta.TryGetValue("index",out var value) ? Convert.ToInt32(value) : 0;
    // 三段层的段名：pre/start 播一次、loop/repeat 循环、end 收尾。
    // effect_ple、frameEffect/N、customEffect、召唤物 start|repeat|end 都是这个形态。
    public static List<NativeVfxClip> Segment(string key,string branch,string stage)
        => Parts(key,branch+"/"+stage);
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
    public static List<NativeVfxVideo> VideoLayers(string key)
    {
        Ensure();
        return videos.TryGetValue(key, out var list) ? list : new List<NativeVfxVideo>();
    }
    public static List<NativeVfxVideo> VideoParts(string key,string branch)
    {
        var result=new List<NativeVfxVideo>();
        foreach(var layer in VideoLayers(key))
            if(layer.path==branch || layer.path.StartsWith(branch+"/",StringComparison.Ordinal)) result.Add(layer);
        return result;
    }
    public static bool HasVideo(string key,string branch) => VideoParts(key,branch).Count>0;
    // 282 同一技能的屏幕层是 screen / screen1..screen12 这种编号族，
    // 按「一级分支名」枚举才能不漏层。
    public static List<string> Roots(string key)
    {
        Ensure();
        var result=new List<string>();
        var seen=new HashSet<string>(StringComparer.Ordinal);
        if(skills.TryGetValue(key,out var clips))
            foreach(var path in clips.Keys)
            {
                string root=path.Split('/')[0];
                if(seen.Add(root)) result.Add(root);
            }
        foreach(var layer in VideoLayers(key))
        {
            string root=layer.path.Split('/')[0];
            if(seen.Add(root)) result.Add(root);
        }
        return result;
    }
    public static string Alias(string part)
    {
        if(part=="cast") return "effect";
        if(part=="cast2") return "effect0";
        return part;
    }
    // 282 的一级分支大量成族出现：effect / effect0 / effect1..6、special /
    // special0..3、prepare / prepare0、keydown / keydown0、hit / hit2。
    // 运行时只认写死的两个名字必然漏层（五六转 93 处），统一按「主干 + 纯数字后缀」
    // 枚举，名字里含字母的（effectFlash / effect_ple / effectSelfCloneList）
    // 是有独立语义的另一种层，不并入。
    public static bool Numbered(string name,string stem)
    {
        if(name==null || !name.StartsWith(stem,StringComparison.Ordinal)) return false;
        for(int i=stem.Length;i<name.Length;i++)
            if(name[i]<'0' || name[i]>'9') return false;
        return true;
    }
    public static List<string> Layers(string key,string stem)
    {
        Ensure();
        var result=new List<string>();
        foreach(string root in Roots(key))
            if(Numbered(root,stem)) result.Add(root);
        result.Sort(StringComparer.Ordinal);
        return result;
    }
    public static bool HasLayer(string key,string stem) => Layers(key,stem).Count>0;
    // 282 用 info/rapidAttack 标记「按住连射」（暴风神射VI / 箭座VI / 瞬迅雷VI /
    // 天空之歌VI…）。旧的 Continuous 表只有三只四转技能，五六转同名技能会被
    // 当成一次性施法，这里直接读源端标记。蓄力型（雷霆万钧VI / 元素之炎VI）
    // 没有这个标记，释放结算未逆向，保持单次施法。
    public static bool RapidAttack(int id)
    {
        var root=WzAssets.Node("Skill/"+(id/10000).ToString("D3")+".img/skill/"+id.ToString("D7"));
        if(root==null) return false;
        if(root.Int("info/rapidAttack")!=0) return true;
        // 有一批「按住连续攻击」不写 info/rapidAttack，改用 keydown/keydownend +
        // attackDelay/attackCount 三件套表达。400011068 槍刺旋風就是这种：
        // info 只有 areaAttack/bodyAttack/invincible/movingAttack（type=11），
        // common 却是 attackDelay=140、attackCount=8，h 写「最多#t秒內以#damage%傷害
        // 連續攻擊前方#mobCount名敵人共#attackCount次」。
        // 只看 rapidAttack 会把它当蓄力型（见 SkillSystem.BeginCast 的 charge 分支）：
        // 按住只播 keydown 循环、伤害要松手才出一次 —— 和圣十字架「只播特效打不了怪」
        // 是同一类症状。判据全部来自源端字段，不按 id 硬编码。
        if(root.Get("keydown")==null || root.Get("keydownend")==null) return false;
        return SafeInt(root,"common/attackDelay")>0 && SafeInt(root,"common/attackCount")>0;
    }
    // common 里可能是公式串（"675+27*x"），Convert.ToInt32 会抛，这里按「读不到」处理。
    static int SafeInt(ImgNode node,string path)
    {
        var value=node?.Get(path)?.TryResolve()?.Value;
        if(value==null) return 0;
        try { return Convert.ToInt32(value); } catch { return 0; }
    }
}
