using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

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
    public static void Install(string zipPath, string root, string expectedManifest, Action<int, int> progress)
    {
        string staging = Path.Combine(root, "Installing"), data = Path.Combine(root, "Data");
        string old = Path.Combine(root, "PreviousData"), receipt = Path.Combine(root, "NativeAssetIndex.sha256");
        Directory.CreateDirectory(root);
        // Recover an interrupted directory promotion before starting another install.
        if (!Directory.Exists(data) && Directory.Exists(old)) Directory.Move(old, data);
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
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
                    using (var output = File.Create(target)) input.CopyTo(output);
                    using (var input = File.OpenRead(target))
                    using (var sha = SHA256.Create())
                    {
                        string hash = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
                        if (hash != (string)file["sha256"]) throw new InvalidDataException("资源校验失败: " + relative);
                    }
                    progress?.Invoke(++count, files.Count);
                }
            }
            if (Directory.Exists(old)) Directory.Delete(old, true);
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
            try { if (Directory.Exists(old)) Directory.Delete(old, true); } catch (IOException) { }
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
}
