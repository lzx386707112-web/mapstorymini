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
                foreach (string prefix in new[] { "Installing.cleanup-", "PreviousData.cleanup-" })
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

    public static void Install(string zipPath, string root, string expectedManifest, Action<int, int> progress,
        Action<string> phase = null)
    {
        string staging = Path.Combine(root, "Installing"), data = Path.Combine(root, "Data");
        string old = Path.Combine(root, "PreviousData"), receipt = Path.Combine(root, "NativeAssetIndex.sha256");
        Directory.CreateDirectory(root);
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
                var index = (Dictionary<string, object>)MiniJson.Parse(text);
                var files = (List<object>)index["files"];
                int count = 0;
                var buffer = new byte[128 * 1024];
                phase?.Invoke("首次安装：解压并校验资源");
                progress?.Invoke(0, files.Count);
                foreach (Dictionary<string, object> file in files)
                {
                    string relative = (string)file["path"];
                    string target = Path.GetFullPath(Path.Combine(staging, relative));
                    if (!target.StartsWith(Path.GetFullPath(staging) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                        throw new InvalidDataException("资源路径无效");
                    var entry = zip.GetEntry("Data/" + relative);
                    if (entry == null) throw new InvalidDataException("资源包缺少文件: " + relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (var input = entry.Open())
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
            // A cleanup failure must not invalidate an already verified installation.
            try { RetireDirectory(old); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
}
