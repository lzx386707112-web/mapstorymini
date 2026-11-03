using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

// 在线补丁事务：验证发生在修改之前，只移动受影响文件；启动先恢复未提交事务。
public static class WzResourcePatchInstaller
{
    const string Transaction = "ResourcePatchTransaction";
    static string SafePath(string root, string relative)
    {
        string prefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(prefix, StringComparison.Ordinal) || relative.Contains("\\") ||
            relative.StartsWith("/", StringComparison.Ordinal)) throw new InvalidDataException("资源路径无效: " + relative);
        return path;
    }
    static void DurableWrite(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using (var file = new FileStream(path, FileMode.Create, FileAccess.Write))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            file.Write(bytes, 0, bytes.Length); file.Flush(true);
        }
    }
    static Dictionary<string, string> Files(string manifest)
    {
        var index = (Dictionary<string, object>)MiniJson.Parse(manifest);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Dictionary<string, object> file in (List<object>)index["files"])
        {
            string name = (string)file["path"];
            if (name.StartsWith(".", StringComparison.Ordinal)) throw new InvalidDataException("资源路径保留");
            result.Add(name, (string)file["sha256"]);
        }
        return result;
    }
    // journal 在任何移动前落盘；每项记录“原先存在”。恢复操作可重复执行。
    public static void Recover(string root)
    {
        string transaction = Path.Combine(root, Transaction);
        if (!Directory.Exists(transaction)) return;
        string journal = Path.Combine(transaction, "journal");
        if (!File.Exists(Path.Combine(transaction, "committed")) && File.Exists(journal))
        {
            var lines = File.ReadAllLines(journal);
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string[] entry = lines[i].Split('|');
                if (entry.Length != 2 || (entry[0] != "0" && entry[0] != "1"))
                    throw new InvalidDataException("补丁恢复日志损坏，请联系群主");
                string name = Encoding.UTF8.GetString(Convert.FromBase64String(entry[1]));
                string target = SafePath(root, name), backup = SafePath(Path.Combine(transaction, "backup"), name);
                if (File.Exists(backup))
                {
                    if (File.Exists(target)) File.Delete(target);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Move(backup, target);
                }
                else if (entry[0] == "0" && File.Exists(target)) File.Delete(target);
            }
        }
        // 先分离已恢复/已提交日志，避免清理被杀后丢失 committed 而误回滚。
        string retired = transaction + ".cleanup-" + Guid.NewGuid().ToString("N");
        Directory.Move(transaction, retired);
        // 已脱离活动事务目录，删除只影响过期备份，不必阻塞安装完成/进入游戏。
        Task.Run(() =>
        {
            try { Directory.Delete(retired, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        });
    }
    public static void Install(string zipPath, string root, string manifest, string fallbackManifest,
        string versionJson, Action<int, int> progress, Action<string> phase)
    {
        Recover(root);
        string transaction = Path.Combine(root, Transaction), staging = Path.Combine(transaction, "new");
        string data = Path.Combine(root, "Data");
        string oldManifestPath = Path.Combine(data, ".resource-manifest.json");
        string oldManifest = File.Exists(oldManifestPath) ? File.ReadAllText(oldManifestPath) : fallbackManifest;
        var previous = Files(oldManifest); var target = Files(manifest);
        var changes = new List<string>();
        var deletions = new List<string>();
        Directory.CreateDirectory(staging);
        bool committed = false;
        try
        {
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                if (WzResourceInstaller.ReadArchiveManifest(zipPath) != manifest)
                    throw new InvalidDataException("补丁目标清单不匹配");
                long total = 0;
                foreach (var file in target)
                {
                    SafePath(data, file.Key);
                    var entry = zip.GetEntry("Data/" + file.Key);
                    if (entry != null) { changes.Add(file.Key); total = checked(total + entry.Length); }

                }
                long done = 0;
                var buffer = new byte[128 * 1024];
                foreach (var file in target)
                {
                    var entry = zip.GetEntry("Data/" + file.Key);
                    if (entry == null) continue; // 未变化文件零复制、零哈希。
                    phase?.Invoke("正在解压补丁资源");
                    string destination = SafePath(staging, "Data/" + file.Key);
                    if (entry != null) Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    using (var input = entry != null ? entry.Open() : File.OpenRead(SafePath(data, file.Key)))
                    using (var output = File.Create(destination))
                    {
                        int n;
                        while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            output?.Write(buffer, 0, n);
                            done += n; progress?.Invoke(total > 0 ? (int)(done * 9900 / total) : 9900, 10000);
                        }
                        output?.Flush(true);

                    }
                }
            }
            foreach (var file in previous)
                if (!target.ContainsKey(file.Key)) deletions.Add("Data/" + file.Key);
            var operations = new List<string>();
            foreach (string name in changes) operations.Add("Data/" + name);
            operations.AddRange(deletions);
            DurableWrite(SafePath(staging, "Data/.resource-manifest.json"), manifest);
            operations.Add("Data/.resource-manifest.json");
            if (versionJson != null)
            {
                DurableWrite(SafePath(staging, "Data/.resUpdate.json"), versionJson);
                operations.Add("Data/.resUpdate.json");
            }
            DurableWrite(SafePath(staging, "NativeAssetIndex.sha256"), WzResourceInstaller.Hash(Encoding.UTF8.GetBytes(manifest)) + "\n");
            operations.Add("NativeAssetIndex.sha256");
            var journal = new StringBuilder();
            foreach (string name in operations)
                journal.Append(File.Exists(SafePath(root, name)) ? "1|" : "0|")
                    .Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(name))).Append('\n');
            DurableWrite(Path.Combine(transaction, "journal.pending"), journal.ToString());
            File.Move(Path.Combine(transaction, "journal.pending"), Path.Combine(transaction, "journal"));
            phase?.Invoke("正在应用补丁"); progress?.Invoke(0, 10000);
            int applied = 0;
            foreach (string name in operations)
            {
                string path = SafePath(root, name), backup = SafePath(Path.Combine(transaction, "backup"), name);
                if (File.Exists(path))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)); File.Move(path, backup);
                }
                string replacement = SafePath(staging, name);
                if (File.Exists(replacement))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)); File.Move(replacement, path);
                }
                progress?.Invoke(++applied * 9900 / operations.Count, 10000);
            }
            // 测试/阶段回调抛错也必须在提交前回滚。
            phase?.Invoke("正在完成资源安装");
            DurableWrite(Path.Combine(transaction, "committed.pending"), "ok");
            File.Move(Path.Combine(transaction, "committed.pending"), Path.Combine(transaction, "committed"));
            committed = true;
        }
        finally
        {
            if (!committed) Recover(root);
        }
        // 提交之后清理失败不会回滚，启动时可继续清理。
        try { Recover(root); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        WzResourceInstaller.DeleteInstalledArchive(zipPath);
    }
}
