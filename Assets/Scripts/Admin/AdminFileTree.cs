// 服务端脚本文件树（源端 FileController + FileTreeService，URL 与 DTO 对齐）。
//
// 基目录 = WzAssets.DataRoot/scripts（ScriptSource.Root 同源）：桌面/编辑器改完 .js
// 立即生效（ScriptSource 直接读磁盘）；Android 打包后脚本走 npc_scripts.json，
// 这里的写操作只影响 /sdcard 上的副本（真机需重跑 tools/sync_npc_scripts.py 回填包体）。
//
// key 语义照抄源端：根目录的子项 key 是自己的下标，子目录的 key = 父 key + "-" + 下标；
// 解析 key 时逐级 listFiles()[i]，天然防路径逃逸，再补一道 startsWith 基目录校验。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public static class AdminFileTree
{
    static string baseDir;

    public static void Register(AdminApi api)
    {
        api.Post("/file/v1/tree", Tree);
        api.Post("/file/v1/tree/read", Read);
        api.Post("/file/v1/tree/write", Write);
    }

    static string BaseDir()
    {
        if (baseDir != null) return baseDir;
        string root = WzAssets.DataRoot;
        string dir = Path.Combine(root, "scripts");
        if (!Directory.Exists(dir)) throw new AdminApiException("脚本目录不存在: " + dir);
        baseDir = Path.GetFullPath(dir);
        return baseDir;
    }

    // 源端 FileController 的排序：纯数字标题（去掉 .js）按数值倒序在前，其余忽略大小写字典序
    static int CompareNodes(FileInfo a, FileInfo b)
    {
        string t1 = a.Name.Replace(".js", "");
        string t2 = b.Name.Replace(".js", "");
        bool n1 = IsNum(t1), n2 = IsNum(t2);
        if (n1 && !n2) return -1;
        if (!n1 && n2) return 1;
        if (n1 && n2) return long.Parse(t2).CompareTo(long.Parse(t1));
        return string.Compare(t1, t2, StringComparison.OrdinalIgnoreCase);
    }

    static bool IsNum(string s)
    {
        if (s.Length == 0) return false;
        foreach (var c in s) if (c < '0' || c > '9') return false;
        return true;
    }

    static object Tree(AdminCtx ctx)
    {
        string currentKey = AdminJson.GetString(AdminJson.AsDict(ctx.Body), "currentKey");
        return AdminMainThread.Run(() =>
        {
            var current = Resolve(currentKey);
            if (!current.Exists || (current.Attributes & FileAttributes.Directory) == 0)
                throw new AdminApiException("请输入文件夹");

            var dirs = new List<FileInfo>();
            var files = new List<FileInfo>();
            foreach (var path in Directory.GetFileSystemEntries(current.FullName))
            {
                var info = new FileInfo(path);
                if ((info.Attributes & FileAttributes.Directory) != 0) dirs.Add(info);
                else files.Add(info);
            }
            // 目录在前（系统习惯，源端 listFiles 本身目录优先），组内按源端排序
            dirs.Sort(CompareNodes);
            files.Sort(CompareNodes);

            var nodes = new List<object>();
            int i = 0;
            foreach (var d in dirs) nodes.Add(Node(d, currentKey, i++));
            foreach (var f in files) nodes.Add(Node(f, currentKey, i++));
            return nodes;
        });
    }

    static object Node(FileInfo info, string parentKey, int index)
    {
        string key = string.IsNullOrEmpty(parentKey) ? index.ToString() : parentKey + "-" + index;
        bool isDir = (info.Attributes & FileAttributes.Directory) != 0;
        return AdminJson.Obj(
            "title", info.Name,
            "key", key,
            "isLeaf", !isDir,
            "children", isDir ? new List<object>() : null);
    }

    // 逐级按下标下钻（等价源端 resolveByTreeKey；越界/非数字都算「不存在」）
    static FileInfo Resolve(string currentKey)
    {
        var current = new FileInfo(BaseDir());
        if (!string.IsNullOrEmpty(currentKey))
        {
            foreach (var part in currentKey.Split('-'))
            {
                if (!int.TryParse(part, out int index))
                    throw new AdminApiException("文件/文件夹不存在");
                var entries = Directory.GetFileSystemEntries(current.FullName);
                if (index < 0 || index >= entries.Length)
                    throw new AdminApiException("文件/文件夹不存在");
                current = new FileInfo(entries[index]);
            }
        }

        // 防路径逃逸：必须仍在 scripts 基目录内
        string full = Path.GetFullPath(current.FullName);
        if (!full.StartsWith(BaseDir(), StringComparison.Ordinal))
            throw new AdminApiException("检测到路径逃逸尝试");
        return current;
    }

    static object Read(AdminCtx ctx)
    {
        var body = AdminJson.AsDict(ctx.Body);
        string currentKey = AdminJson.GetString(body, "currentKey");
        string title = AdminJson.GetString(body, "title");
        return AdminMainThread.Run(() =>
        {
            var file = Resolve(currentKey);
            if (file.Name != title) throw new AdminApiException("文件目录发生变动，请重新读取");
            // 源端 UTF-8 失败回落 ISO-8859-1；本工程脚本同步时就是 utf-8(-sig)，直接按 UTF-8 读
            return AdminJson.Obj("content", File.ReadAllText(file.FullName, new UTF8Encoding(false)));
        });
    }

    static object Write(AdminCtx ctx)
    {
        var body = AdminJson.AsDict(ctx.Body);
        string currentKey = AdminJson.GetString(body, "currentKey");
        string title = AdminJson.GetString(body, "title");
        string content = AdminJson.GetString(body, "content");
        return AdminMainThread.Run(() =>
        {
            var file = Resolve(currentKey);
            if (file.Name != title) throw new AdminApiException("文件目录发生变动，请重新写入");
            if ((file.Attributes & FileAttributes.Directory) != 0) throw new AdminApiException("请选择文件");
            File.WriteAllText(file.FullName, content ?? "", new UTF8Encoding(false));
            // NPC/任务脚本直接改的是磁盘文件（Data/scripts），ScriptSource 有进程内缓存 +
            // missing 标记集，写完必须清掉，否则下一次对话还读旧内容（源端每次执行前重新读盘）。
            ScriptSource.ClearCache();
            return AdminJson.Ok();
        });
    }
}
