using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

// Extract on a worker thread. Validate the whole new pack before replacing installed Data.
public static class WzResourceInstaller
{
    public static string Hash(byte[] bytes)
    {
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
    public static void Verify(string root, string manifest, Action<int, int> progress)
    {
        var index = (Dictionary<string, object>)MiniJson.Parse(manifest);
        var files = (List<object>)index["files"]; int count = 0;
        foreach (Dictionary<string, object> file in files)
        {
            string path = Path.Combine(root, "Data", (string)file["path"]);
            using (var input = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                string hash = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
                if (hash != (string)file["sha256"]) throw new InvalidDataException("资源校验失败: " + file["path"]);
            }
            progress?.Invoke(++count, files.Count);
        }
    }
    // Fast startup check: hash only a spread-out sample instead of the whole Data tree.
    // A matching install receipt + a passing sample means "installed and sane"; a full
    // Verify is only needed when the sample fails (real corruption) or on first install.
    public static void VerifySample(string root, string manifest, int sample, Action<int, int> progress = null)
    {
        var index = (Dictionary<string, object>)MiniJson.Parse(manifest);
        var files = (List<object>)index["files"];
        if (files.Count == 0) return;
        int step = Math.Max(1, files.Count / Math.Max(1, sample));
        int last = -1;
        int count = 0;
        int countTotal = (files.Count - 1) / step + 1;
        if ((files.Count - 1) % step != 0) countTotal++;
        progress?.Invoke(0, countTotal);
        for (int i = 0; i < files.Count; i += step)
        {
            VerifyOne(root, (Dictionary<string, object>)files[i]);
            last = i;
            progress?.Invoke(++count, countTotal);
        }
        // Always include the final entry: the evenly spaced walk may stop just short of it.
        if (last != files.Count - 1)
        {
            VerifyOne(root, (Dictionary<string, object>)files[files.Count - 1]);
            progress?.Invoke(++count, countTotal);
        }
    }
    static void VerifyOne(string root, Dictionary<string, object> file)
    {
        string path = Path.Combine(root, "Data", (string)file["path"]);
        if (!File.Exists(path)) throw new FileNotFoundException("资源缺失: " + file["path"]);
        using (var input = File.OpenRead(path))
        using (var sha = SHA256.Create())
        {
            string hash = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
            if (hash != (string)file["sha256"]) throw new InvalidDataException("资源校验失败: " + file["path"]);
        }
    }
    // Detach obsolete trees before deleting them: shared-storage deletion of tens of
    // thousands of files must not hold up startup, or race a subsequent installation.
    static void RetireDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        string retired = path + ".cleanup-" + Guid.NewGuid().ToString("N");
        Directory.Move(path, retired);
        Task.Run(() =>
        {
            try { Directory.Delete(retired, true); }
            catch (IOException) { } // Safe to leave obsolete files if storage is unavailable.
            catch (UnauthorizedAccessException) { }
        });
    }

    // Resume cleanup if Android killed the process before its background delete finished.
    public static void CleanupRetired(string root)
    {
        Task.Run(() =>
        {
            try
            {
                foreach (string prefix in new[] { "Installing.cleanup-", "PreviousData.cleanup-", "ResourcePatchTransaction.cleanup-" })
                    foreach (string path in Directory.GetDirectories(root, prefix + "*"))
                    {
                        try { Directory.Delete(path, true); }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        });
    }

    // Keep failed archives for retry; deletion failure must not undo a valid install.
    public static void DeleteInstalledArchive(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // 用户选择直接解压模式：不匹配 APK 清单，不计算资源哈希。
    public static string ExtractFull(string zipPath, string root, Action<int, int> progress = null)
    {
        string data = Path.Combine(root, "Data");
        Directory.CreateDirectory(data);
        string prefix = Path.GetFullPath(data) + Path.DirectorySeparatorChar;
        string manifest = null;
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            if (zip.GetEntry("resource-patch.json") != null)
                throw new InvalidDataException("这里需要全量资源 ZIP，不能使用增量补丁");
            int count = 0;
            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.StartsWith("Data/", StringComparison.Ordinal) || entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
                string target = Path.GetFullPath(Path.Combine(data, entry.FullName.Substring(5)));
                if (!target.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("资源路径无效");
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using (var input = entry.Open())
                using (var output = File.Create(target)) input.CopyTo(output, 128 * 1024);
                progress?.Invoke(++count, zip.Entries.Count);
            }
            var index = zip.GetEntry("resource-manifest.json");
            if (index != null)
            {
                using (var reader = new StreamReader(index.Open(), Encoding.UTF8)) manifest = reader.ReadToEnd();
                File.WriteAllText(Path.Combine(data, ".resource-manifest.json"), manifest);
            }
        }
        DeleteInstalledArchive(zipPath);
        return manifest;
    }

    public static string ReadArchiveManifest(string zipPath)
    {
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            var entry = zip.GetEntry("resource-manifest.json");
            if (entry == null || entry.Length > 64 * 1024 * 1024)
                throw new InvalidDataException("资源包缺少有效清单");
            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
            {
                string text = reader.ReadToEnd();
                var index = (Dictionary<string, object>)MiniJson.Parse(text);
                if (!(index["files"] is List<object> files) || files.Count == 0)
                    throw new InvalidDataException("资源清单没有文件");
                return text;
            }
        }
    }

    // 仅信任和安装回执匹配的历史清单；回执无效时仍逐文件完整校验。
    static Dictionary<string, string> VerifiedFiles(string root, string fallbackManifest)
    {
        if (fallbackManifest == null) return null;
        try
        {
            string installed = Path.Combine(root, "Data/.resource-manifest.json");
            string text = File.Exists(installed) ? File.ReadAllText(installed) : fallbackManifest;
            string receipt = File.ReadAllText(Path.Combine(root, "NativeAssetIndex.sha256")).Trim();
            if (receipt != Hash(Encoding.UTF8.GetBytes(text))) return null;
            var index = (Dictionary<string, object>)MiniJson.Parse(text);
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Dictionary<string, object> file in (List<object>)index["files"])
                result.Add((string)file["path"], (string)file["sha256"]);
            return result;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (ArgumentException) { return null; }
        catch (InvalidCastException) { return null; }
        catch (KeyNotFoundException) { return null; }
    }

    public static void Install(string zipPath, string root, string expectedManifest, Action<int, int> progress,
        Action<string> phase = null, string resourceVersionJson = null,
        string verifiedBaseManifest = null)
    {
        string staging = Path.Combine(root, "Installing"), data = Path.Combine(root, "Data");
        string old = Path.Combine(root, "PreviousData"), receipt = Path.Combine(root, "NativeAssetIndex.sha256");
        Directory.CreateDirectory(root);
        WzResourcePatchInstaller.Recover(root);
        if (verifiedBaseManifest != null)
        {
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                if (archive.GetEntry("resource-patch.json") != null)
                {
                    WzResourcePatchInstaller.Install(zipPath, root, expectedManifest, verifiedBaseManifest,
                        resourceVersionJson, progress, phase);
                    return;
                }
            }
            phase?.Invoke("正在解压资源");
            string extracted = ExtractFull(zipPath, root, progress);
            if (resourceVersionJson != null)
                File.WriteAllText(Path.Combine(data, ".resUpdate.json"), resourceVersionJson);
            // 仅保存清单摘要供旧代码读取，不做内容校验。
            if (extracted != null)
                File.WriteAllText(receipt, Hash(Encoding.UTF8.GetBytes(extracted)) + "\n");
            return;
        }
        // Recover an interrupted directory promotion before starting another install.
        if (!Directory.Exists(data) && Directory.Exists(old)) Directory.Move(old, data);
        phase?.Invoke("正在准备资源安装");
        RetireDirectory(staging);
        Directory.CreateDirectory(staging);
        try
        {
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                var manifest = zip.GetEntry("resource-manifest.json");
                if (manifest == null) throw new InvalidDataException("资源包缺少清单");
                string text;
                using (var reader = new StreamReader(manifest.Open(), Encoding.UTF8)) text = reader.ReadToEnd();
                if (Hash(Encoding.UTF8.GetBytes(text)) != Hash(Encoding.UTF8.GetBytes(expectedManifest)))
                    throw new InvalidDataException("资源包版本与当前游戏不匹配");
                bool patch = zip.GetEntry("resource-patch.json") != null;
                var index = (Dictionary<string, object>)MiniJson.Parse(text);
                var files = (List<object>)index["files"];
                var verified = patch ? VerifiedFiles(root, verifiedBaseManifest) : null;
                int count = 0;
                var buffer = new byte[128 * 1024];
                phase?.Invoke(patch ? "正在应用资源补丁并校验" : "首次安装：解压并校验资源");
                progress?.Invoke(0, files.Count);
                foreach (Dictionary<string, object> file in files)
                {
                    string relative = (string)file["path"];
                    string target = Path.GetFullPath(Path.Combine(staging, relative));
                    if (!target.StartsWith(Path.GetFullPath(staging) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                        throw new InvalidDataException("资源路径无效");
                    var entry = zip.GetEntry("Data/" + relative);
                    if (entry == null && !patch) throw new InvalidDataException("资源包缺少文件: " + relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    // 在线补丁可复用已确认的旧文件；新增/修改文件始终计算 SHA-256。
                    if (entry == null && verified != null &&
                        verified.TryGetValue(relative, out string previousHash) && previousHash == (string)file["sha256"])
                    {
                        phase?.Invoke("正在复制未变化资源（无需重复校验）");
                        File.Copy(Path.Combine(data, relative), target);
                        progress?.Invoke(++count, files.Count);
                        continue;
                    }
                    phase?.Invoke(verified != null ? "正在安装并校验新增/修改资源" : "正在安装并完整校验资源");
                    using (var input = entry != null ? entry.Open() : File.OpenRead(Path.Combine(data, relative)))
                    using (var output = File.Create(target))
                    using (var sha = SHA256.Create())
                    {
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            output.Write(buffer, 0, read);
                            sha.TransformBlock(buffer, 0, read, buffer, 0);
                        }
                        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                        string hash = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
                        if (hash != (string)file["sha256"]) throw new InvalidDataException("资源校验失败: " + relative);
                    }
                    progress?.Invoke(++count, files.Count);
                }
            }
            // 清单与版本回执随 Data 一起切换，进程中断不会出现新版本配旧资源。
            File.WriteAllText(Path.Combine(staging, ".resource-manifest.json"), expectedManifest);
            if (resourceVersionJson != null)
                File.WriteAllText(Path.Combine(staging, ".resUpdate.json"), resourceVersionJson);
            phase?.Invoke("正在完成资源安装");
            RetireDirectory(old);
            if (Directory.Exists(data)) Directory.Move(data, old);
            try
            {
                Directory.Move(staging, data);
                File.WriteAllText(receipt + ".pending", Hash(Encoding.UTF8.GetBytes(expectedManifest)) + "\n");
                if (File.Exists(receipt)) File.Delete(receipt);
                File.Move(receipt + ".pending", receipt);
            }
            catch
            {
                if (Directory.Exists(data)) Directory.Delete(data, true);
                if (Directory.Exists(old)) Directory.Move(old, data);
                throw;
            }
            DeleteInstalledArchive(zipPath);
            // A cleanup failure must not invalidate an already verified installation.
            try { RetireDirectory(old); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
}
