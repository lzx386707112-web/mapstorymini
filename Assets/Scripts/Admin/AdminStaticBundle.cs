// 后台前端静态资源包：把 gms-ui 的 dist/ 打成一个 .bytes 放进 Resources。
//
// 为什么不直接放 StreamingAssets：Android 上 StreamingAssets 在 APK 的 jar 里，
// File.ReadAllBytes 读不到，要异步走 UnityWebRequest；而本工程已经有「资源打包进
// Resources 的 .bytes」这条成熟链路（NativeAssetIndex / npc_scripts.json），沿用即可。
//
// 包格式（小端）：
//   magic  "MDUIB1"  (6 字节)
//   u32    entryCount
//   重复 entryCount 次：
//     u16  nameLen, name(UTF-8)
//     u32  dataLen, data
//
// 纯 C#，不引用 UnityEngine。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public sealed class AdminStaticFile
{
    public string Name;
    public byte[] Data;
    public string ContentType;
}

public sealed class AdminStaticBundle
{
    public const string Magic = "MDUIB1";

    readonly Dictionary<string, AdminStaticFile> files =
        new Dictionary<string, AdminStaticFile>(StringComparer.OrdinalIgnoreCase);

    public int Count => files.Count;
    public IEnumerable<string> Names => files.Keys;

    public static AdminStaticBundle Load(byte[] raw)
    {
        if (raw == null || raw.Length < 10) throw new InvalidDataException("后台前端资源包为空");
        var bundle = new AdminStaticBundle();
        using (var ms = new MemoryStream(raw, false))
        using (var br = new BinaryReader(ms, Encoding.UTF8))
        {
            var magic = Encoding.ASCII.GetString(br.ReadBytes(Magic.Length));
            if (magic != Magic) throw new InvalidDataException("后台前端资源包标识不匹配: " + magic);
            int count = br.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                int nameLen = br.ReadUInt16();
                string name = Encoding.UTF8.GetString(br.ReadBytes(nameLen));
                int dataLen = br.ReadInt32();
                var data = br.ReadBytes(dataLen);
                var key = Normalize(name);
                bundle.files[key] = new AdminStaticFile
                {
                    Name = key,
                    Data = data,
                    ContentType = ContentTypeOf(key),
                };
            }
        }
        return bundle;
    }

    // 请求路径 → 包内键。统一去掉前导 '/'，'/' 落到 index.html。
    public static string Normalize(string path)
    {
        if (string.IsNullOrEmpty(path)) return "index.html";
        var p = path.Replace('\\', '/');
        while (p.StartsWith("/", StringComparison.Ordinal)) p = p.Substring(1);
        if (p.Length == 0) return "index.html";
        return p;
    }

    public bool TryGet(string path, out AdminStaticFile file)
    {
        var key = Normalize(path);
        if (files.TryGetValue(key, out file)) return true;
        // SPA 回退：前端用 history 路由，/login、/game/config 这类地址没有实体文件，
        // 一律回 index.html 交给 vue-router。带扩展名的（.js/.css/...）不回退，避免把
        // 404 的脚本换成 HTML 导致浏览器报 MIME 错误。
        if (!key.Contains(".") && files.TryGetValue("index.html", out file)) return true;
        file = null;
        return false;
    }

    // 只认实体文件，不做 SPA 回退。路由层先用它判断「这是不是静态资源」，
    // 再决定要不要把 /assets/item-icons 这类接口当静态文件兜底。
    public bool TryGetExact(string path, out AdminStaticFile file)
        => files.TryGetValue(Normalize(path), out file);

    public static string ContentTypeOf(string name)
    {
        int dot = name.LastIndexOf('.');
        string ext = dot < 0 ? "" : name.Substring(dot).ToLowerInvariant();
        switch (ext)
        {
            case ".html": return "text/html; charset=utf-8";
            case ".js":
            case ".mjs": return "application/javascript; charset=utf-8";
            case ".css": return "text/css; charset=utf-8";
            case ".json": return "application/json; charset=utf-8";
            case ".svg": return "image/svg+xml";
            case ".png": return "image/png";
            case ".jpg":
            case ".jpeg": return "image/jpeg";
            case ".gif": return "image/gif";
            case ".webp": return "image/webp";
            case ".ico": return "image/x-icon";
            case ".woff": return "font/woff";
            case ".woff2": return "font/woff2";
            case ".ttf": return "font/ttf";
            case ".eot": return "application/vnd.ms-fontobject";
            case ".map": return "application/json; charset=utf-8";
            case ".txt": return "text/plain; charset=utf-8";
            default: return "application/octet-stream";
        }
    }
}
