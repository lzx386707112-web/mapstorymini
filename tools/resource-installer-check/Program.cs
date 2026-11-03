using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

static class Program
{
    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static void Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "resource-installer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var payload = new byte[300001]; // Multiple hashing buffers, including a partial final block.
            new Random(42).NextBytes(payload);
            var files = new Dictionary<string, byte[]>
            {
                ["nested/large.img"] = payload,
                ["empty.img"] = Array.Empty<byte>()
            };
            var entries = new List<object>();
            foreach (var file in files)
                entries.Add(new { path = file.Key, sha256 = WzResourceInstaller.Hash(file.Value) });
            string manifest = JsonSerializer.Serialize(new { files = entries });
            string zipPath = Path.Combine(root, "resources.zip");
            Pack(zipPath, manifest, files);
            Directory.CreateDirectory(Path.Combine(root, "Installing"));
            File.WriteAllText(Path.Combine(root, "Installing", "interrupted"), "partial");
            Directory.CreateDirectory(Path.Combine(root, "PreviousData"));
            File.WriteAllText(Path.Combine(root, "PreviousData", "old"), "recover me");
            var phases = new List<string>();
            int completed = 0;
            WzResourceInstaller.Install(zipPath, root, manifest,
                (n, total) => { Require(total == files.Count, "Wrong progress total"); completed = n; }, phases.Add);
            WzResourceInstaller.Verify(root, manifest, null);
            Require(completed == files.Count, "Incomplete progress");
            Require(phases[phases.Count - 1] == "正在完成资源安装", "Missing finalization phase");
            Require(File.ReadAllText(Path.Combine(root, "NativeAssetIndex.sha256")).Trim() ==
                WzResourceInstaller.Hash(Encoding.UTF8.GetBytes(manifest)), "Missing receipt");

            Require(!File.Exists(zipPath), "Successful archive was not deleted");

            // A bad pack must leave the last working installation intact.
            files["nested/large.img"] = new byte[] { 1, 2, 3 };
            Pack(zipPath, manifest, files);
            ExpectFailure(() => WzResourceInstaller.Install(zipPath, root, manifest, null));
            WzResourceInstaller.Verify(root, manifest, null);
            Require(File.Exists(zipPath), "Failed archive must remain for retry");

            // Failure after promotion must restore the previous installation.
            files["nested/large.img"] = payload;
            Pack(zipPath, manifest, files);
            Directory.CreateDirectory(Path.Combine(root, "NativeAssetIndex.sha256.pending"));
            ExpectFailure(() => WzResourceInstaller.Install(zipPath, root, manifest, null));
            WzResourceInstaller.Verify(root, manifest, null);
            Require(!Directory.Exists(Path.Combine(root, "Installing")), "Staging was not cleaned up");
            Directory.Delete(Path.Combine(root, "NativeAssetIndex.sha256.pending"));

            // Delta: reuse a large file, add one, remove one; validate target before promotion.
            var target = new Dictionary<string, byte[]>
            {
                ["nested/large.img"] = payload,
                ["new.img"] = new byte[] { 9, 8, 7 }
            };
            entries.Clear();
            foreach (var file in target)
                entries.Add(new { path = file.Key, sha256 = WzResourceInstaller.Hash(file.Value) });
            string patchedManifest = JsonSerializer.Serialize(new { files = entries });
            Pack(zipPath, patchedManifest, new Dictionary<string, byte[]> { ["new.img"] = target["new.img"] });
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update))
                zip.CreateEntry("resource-patch.json");
            // Wrong APK manifest rejects the patch and keeps both archive and Data.
            ExpectFailure(() => WzResourceInstaller.Install(zipPath, root, manifest, null));
            Require(File.Exists(zipPath), "Rejected patch was deleted");
            WzResourceInstaller.Verify(root, manifest, null);
            // Missing or corrupt unchanged files must also reject the patch.
            File.WriteAllBytes(Path.Combine(root, "Data/nested/large.img"), new byte[] { 0 });
            ExpectFailure(() => WzResourceInstaller.Install(zipPath, root, patchedManifest, null));
            Require(File.Exists(zipPath), "Failed patch was deleted");
            Require(File.Exists(Path.Combine(root, "Data/empty.img")), "Failed patch changed Data");
            File.WriteAllBytes(Path.Combine(root, "Data/nested/large.img"), payload);
            WzResourceInstaller.Install(zipPath, root, patchedManifest, null);
            WzResourceInstaller.Verify(root, patchedManifest, null);
            Require(!File.Exists(Path.Combine(root, "Data/empty.img")), "Deleted entry survived patch");
            Require(!File.Exists(zipPath), "Successful patch was not deleted");
            Console.WriteLine("PASS: streaming hash (large/empty), interrupted install recovery, progress phases, corrupt pack rejection, promotion rollback, delta add/remove/reuse, wrong APK/base rejection, archive cleanup");
        }
        finally
        {
            // Background retirement may still own a tree; leave it to finish if so.
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    static void ExpectFailure(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }
        throw new Exception("Expected installation to fail");
    }

    static void Pack(string path, string manifest, Dictionary<string, byte[]> files)
    {
        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(zip.CreateEntry("resource-manifest.json").Open()))
            writer.Write(manifest);
        foreach (var file in files)
        {
            using var output = zip.CreateEntry("Data/" + file.Key).Open();
            output.Write(file.Value, 0, file.Value.Length);
        }
    }
}
