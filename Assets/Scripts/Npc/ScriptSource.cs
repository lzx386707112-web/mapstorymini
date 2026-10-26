using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// 全类别源端脚本取用入口。两条通道的逻辑文本一致（原件见 Data/script-sources）：
//   1) Data/scripts/{kind}/<名字>.js —— 编辑器/桌面直接读文件，改完马上生效，方便对拍源端
//   2) Resources/Scripts/npc_scripts.json —— 打包进包体，Android / WebGL 不需要 /sdcard 上装脚本
// 两份都由 tools/sync_npc_scripts.py 从 BeiDou-Server/gms-server/scripts-zh-CN（缺中文回落
// scripts）逐字节复制过来。脚本必须原样执行，所以同步阶段不做任何改写。
//
// 名字既可能是数字 id（npc/1002000.js、quest/1021.js），也可能是源端自定义名
// （npc/gachapon.js、npc/changeName.js，由 cm.openNpc(id, "名字") 之类触发）。
public static class ScriptSource
{
    const string PackedResource = "Scripts/npc_scripts";

    static readonly Dictionary<string, string> cache = new Dictionary<string, string>();
    static readonly HashSet<string> missing = new HashSet<string>();
    static Dictionary<string, object> packed;
    static bool packedLoaded;

    public static string Root => Path.Combine(WzAssets.DataRoot, "scripts");
    public static string Evidence => Root + " | Resources/" + PackedResource;

    // NPCScriptManager：命名 npc → BeiDouSpecial → 数字 npc，保留子目录名。
    public static string LoadNpc(int npcId, string scriptName, out string key)
    {
        var candidates = string.IsNullOrEmpty(scriptName)
            ? new[] { "npc/" + npcId }
            : new[] { "npc/" + scriptName, "BeiDouSpecial/" + scriptName, "npc/" + npcId };
        foreach (string candidate in candidates)
        {
            var text = LoadKey(candidate);
            if (text != null) { key = candidate; return text; }
        }
        key = candidates[0];
        return null;
    }

    public static string LoadKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.StartsWith("/", StringComparison.Ordinal) || key.IndexOf('\\') >= 0)
            throw new ArgumentException("Invalid script key", nameof(key));
        foreach (string part in key.Split('/'))
            if (part.Length == 0 || part == "." || part == "..") throw new ArgumentException("Invalid script key", nameof(key));
        int slash = key.IndexOf('/');
        return slash < 0 ? Load("", key) : Load(key.Substring(0, slash), key.Substring(slash + 1));
    }

    public static bool Has(string kind, int id) => !string.IsNullOrEmpty(Load(kind, id));

    public static string Load(string kind, int id) => Load(kind, id.ToString());

    public static string Load(string kind, string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        string key = string.IsNullOrEmpty(kind) ? name : kind + "/" + name;
        if (Path.IsPathRooted(key) || key.IndexOf('\\') >= 0) throw new ArgumentException("Invalid script path");
        foreach (string part in key.Split('/'))
            if (part.Length == 0 || part == "." || part == "..") throw new ArgumentException("Invalid script path");
        if (cache.TryGetValue(key, out var text)) return text;
        if (missing.Contains(key)) return null;

        text = LoadFile(kind, name);
        if (string.IsNullOrEmpty(text)) text = LoadPacked(key);

        if (string.IsNullOrEmpty(text))
        {
            missing.Add(key);
            return null;
        }
        cache[key] = text;
        return text;
    }

    static string LoadFile(string kind, string name)
    {
        string path = Path.Combine(Root, kind, name + ".js");
        try
        {
            if (File.Exists(path)) return File.ReadAllText(path, System.Text.Encoding.UTF8);
        }
        catch (Exception e)
        {
            // 只影响"热改脚本"这条便利通道，不阻断包内通道
            Debug.LogWarning("[Script] 读取失败 " + path + ": " + e.Message);
        }
        return null;
    }

    static string LoadPacked(string key)
    {
        var map = Packed;
        if (map == null) return null;
        return map.TryGetValue(key, out var v) ? v as string : null;
    }

    static Dictionary<string, object> Packed
    {
        get
        {
            if (packedLoaded) return packed;
            packedLoaded = true;
            var asset = Resources.Load<TextAsset>(PackedResource);
            if (asset == null)
            {
                Debug.LogWarning("[Script] 缺少 Resources/" + PackedResource
                                 + "，请先运行 tools/sync_npc_scripts.py");
                return null;
            }
            try
            {
                packed = MiniJson.Parse(asset.text) as Dictionary<string, object>;
            }
            catch (Exception e)
            {
                Debug.LogError("[Script] 脚本包解析失败: " + e.Message);
                packed = null;
            }
            return packed;
        }
    }

    // 调试用：改完脚本/重新导入 JSON 后清一次，避免旧内容一直缓存着
    public static void ClearCache()
    {
        cache.Clear();
        missing.Clear();
        packed = null;
        packedLoaded = false;
    }
}
