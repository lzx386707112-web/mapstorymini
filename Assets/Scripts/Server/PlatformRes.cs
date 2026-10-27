// Server 层的资源读取：优先 Unity Resources（运行时），退到文件系统（tools 离线单测）。
// 保持 Server 程序集不静态依赖 UnityEngine。
using System;
using System.IO;
using System.Reflection;

public static class PlatformRes
{
    public static string LoadText(string resPath)
    {
        var viaUnity = TryLoadTextAsset(resPath);
        if (viaUnity != null) return viaUnity;
        foreach (var root in new[] { AppContext.BaseDirectory, ".", "Assets/Resources", "../Assets/Resources", "../../Assets/Resources", "Assets/Scripts/Server/Db", "../Assets/Scripts/Server/Db" })
        {
            foreach (var suffix in new[] { "", ".json", ".sql" })
            {
                var p = Path.Combine(root, resPath + suffix);
                if (File.Exists(p)) return File.ReadAllText(p);
            }
        }
        return null;
    }

    static string TryLoadTextAsset(string resPath)
    {
        try
        {
            var asm = Type.GetType("UnityEngine.Resources, UnityEngine");
            if (asm == null) return null;
            var m = asm.GetMethod("Load", new[] { typeof(string), typeof(Type) });
            if (m == null) return null;
            var ta = m.Invoke(null, new object[] { resPath, asm.Assembly.GetType("UnityEngine.TextAsset") });
            if (ta == null) return null;
            var textProp = ta.GetType().GetProperty("text");
            return textProp != null ? (string)textProp.GetValue(ta, null) : null;
        }
        catch { return null; }
    }

    // Unity 持久化目录（存档位置）；离线单测退到当前目录。
    public static string PersistentDir()
    {
        try
        {
            var t = Type.GetType("UnityEngine.Application, UnityEngine");
            if (t != null)
            {
                var p = t.GetProperty("persistentDataPath", BindingFlags.Public | BindingFlags.Static);
                if (p != null) return (string)p.GetValue(null, null);
            }
        }
        catch { }
        return ".";
    }
}
