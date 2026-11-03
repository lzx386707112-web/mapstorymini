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
            // Online updates use the target manifest from the archive, independently of the APK.
            string downloadedManifest = WzResourceInstaller.ReadArchiveManifest(zipPath);
            Require(downloadedManifest == patchedManifest, "Wrong downloaded target manifest");
            const string resourceVersion = "{\"versionCode\":2,\"resUrl\":\"https://example.com/patch.zip\"}";
            phases.Clear();
            string unchanged = Path.Combine(root, "Data/nested/large.img");
            DateTime originalWriteTime = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(unchanged, originalWriteTime);
            // Failure after all moves must restore files, deletions and metadata.
            ExpectFailure(() => WzResourceInstaller.Install(zipPath, root, downloadedManifest, null,
                phase => { if (phase == "正在完成资源安装") throw new IOException("Injected pre-commit failure"); },
                resourceVersion, manifest));
            WzResourceInstaller.Verify(root, manifest, null);
            Require(!File.Exists(Path.Combine(root, "Data/new.img")), "Rollback left new file");
            Require(!File.Exists(Path.Combine(root, "Data/.resUpdate.json")), "Rollback advanced version");
            int maximumProgress = 0;
            WzResourceInstaller.Install(zipPath, root, downloadedManifest,
                (done, total) => maximumProgress = Math.Max(maximumProgress, done * 100 / total),
                phases.Add, resourceVersion, manifest);
            Require(maximumProgress < 100, "Progress reached 100 before installer returned");
            Require(File.GetLastWriteTimeUtc(unchanged) == originalWriteTime, "Unchanged resource was rewritten");
            Require(phases.Contains("正在解压补丁资源"), "Patch resource did not use hash validation");
            Require(!phases.Contains("正在校验旧资源基线"), "Verified unchanged resource was rehashed");
            WzResourceInstaller.Verify(root, patchedManifest, null);
            Require(File.ReadAllText(Path.Combine(root, "Data/.resource-manifest.json")) == patchedManifest,
                "Target manifest did not travel with promoted Data");
            Require(File.ReadAllText(Path.Combine(root, "Data/.resUpdate.json")) == resourceVersion,
                "Resource version did not travel with promoted Data");
            // A failed later update must preserve both the installed files and version receipt.
            Pack(zipPath, manifest, new Dictionary<string, byte[]> { ["empty.img"] = Array.Empty<byte>() });
            ExpectFailure(() => WzResourceInstaller.Install(zipPath, root, manifest, null, null,
                "{\"versionCode\":3,\"resUrl\":\"https://example.com/bad.zip\"}"));
            Require(File.ReadAllText(Path.Combine(root, "Data/.resUpdate.json")) == resourceVersion,
                "Failed update advanced resource version");
            WzResourceInstaller.Verify(root, patchedManifest, null);
            File.Delete(zipPath);
            Require(!File.Exists(Path.Combine(root, "Data/empty.img")), "Deleted entry survived patch");
            Require(!File.Exists(zipPath), "Successful patch was not deleted");
            // Simulate process death after backup/move but before commit.
            string tx = Path.Combine(root, "ResourcePatchTransaction");
            Directory.CreateDirectory(Path.Combine(tx, "backup/Data"));
            string victim = Path.Combine(root, "Data/new.img");
            byte[] original = File.ReadAllBytes(victim);
            File.Move(victim, Path.Combine(tx, "backup/Data/new.img"));
            File.WriteAllBytes(victim, new byte[] { 0 });
            File.WriteAllText(Path.Combine(root, "Data/added.img"), "partial");
            File.WriteAllText(Path.Combine(tx, "journal"),
                "1|" + Convert.ToBase64String(Encoding.UTF8.GetBytes("Data/new.img")) + "\n" +
                "0|" + Convert.ToBase64String(Encoding.UTF8.GetBytes("Data/added.img")) + "\n");
            WzResourcePatchInstaller.Recover(root);
            WzResourcePatchInstaller.Recover(root); // idempotent
            Require(Convert.ToBase64String(File.ReadAllBytes(victim)) == Convert.ToBase64String(original), "Recovery lost backup");
            Require(!File.Exists(Path.Combine(root, "Data/added.img")), "Recovery left added file");
            // Committed transactions must only clean backups, never roll back.
            Directory.CreateDirectory(Path.Combine(tx, "backup/Data"));
            File.WriteAllText(Path.Combine(tx, "backup/Data/new.img"), "old");
            File.WriteAllText(Path.Combine(tx, "journal"), "1|" + Convert.ToBase64String(Encoding.UTF8.GetBytes("Data/new.img")) + "\n");
            File.WriteAllText(Path.Combine(tx, "committed"), "ok");
            WzResourcePatchInstaller.Recover(root);
            Require(Convert.ToBase64String(File.ReadAllBytes(victim)) == Convert.ToBase64String(original), "Committed patch rolled back");
            // Direct full extraction ignores APK baseline and content hashes.
            string directRoot = Path.Combine(root, "direct");
            string directZip = Path.Combine(root, "direct.zip");
            Pack(directZip, manifest, new Dictionary<string, byte[]> { ["fresh.img"] = new byte[] { 4, 5 } });
            WzResourceInstaller.ExtractFull(directZip, directRoot);
            Require(File.Exists(Path.Combine(directRoot, "Data/fresh.img")), "Direct extraction failed");
            Require(!File.Exists(directZip), "Direct archive not cleaned up");
            Console.WriteLine("PASS: streaming hash (large/empty), interrupted install recovery, progress phases, corrupt pack rejection, promotion rollback, delta add/remove/reuse, wrong APK/base rejection, archive cleanup, online manifest and version promotion/rollback, unchanged files untouched, journal recovery and commit cleanup");
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
