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

            CheckSplitPacks(root);
            Console.WriteLine("PASS: streaming hash (large/empty), interrupted install recovery, progress phases, corrupt pack rejection, promotion rollback, delta add/remove/reuse, wrong APK/base rejection, archive cleanup, online manifest and version promotion/rollback, unchanged files untouched, journal recovery and commit cleanup, split-part extraction/rejection, split pack cleanup, split patch transaction");
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

    static string ManifestOf(Dictionary<string, byte[]> files)
    {
        var entries = new List<object>();
        foreach (var file in files) entries.Add(new { path = file.Key, sha256 = WzResourceInstaller.Hash(file.Value) });
        return JsonSerializer.Serialize(new { files = entries });
    }

    static string[] PackSplit(string directory, string stem, string manifest,
        Dictionary<string, byte[]> files, int partCount, int firstIndex = 1, bool patch = false)
    {
        Directory.CreateDirectory(directory);
        var buckets = new List<KeyValuePair<string, byte[]>>[partCount];
        for (int i = 0; i < partCount; i++) buckets[i] = new List<KeyValuePair<string, byte[]>>();
        int n = 0;
        foreach (var file in files) buckets[n++ % partCount].Add(file);
        var paths = new List<string>();
        for (int i = 0; i < partCount; i++)
        {
            string path = Path.Combine(directory, stem + ".part" + (i + firstIndex).ToString("00") + ".zip");
            if (File.Exists(path)) File.Delete(path);
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                if (i == 0)
                {
                    using (var writer = new StreamWriter(zip.CreateEntry("resource-manifest.json").Open()))
                        writer.Write(manifest);
                    if (patch) zip.CreateEntry("resource-patch.json");
                }
                string info = "{\"format\":1,\"kind\":\"" + (patch ? "patch" : "full") + "\",\"index\":" +
                    (i + firstIndex) + ",\"count\":" + partCount + ",\"partSize\":2147483648,\"totalFiles\":" +
                    files.Count + "}";
                using (var writer = new StreamWriter(zip.CreateEntry("resource-pack.json").Open())) writer.Write(info);
                foreach (var file in buckets[i])
                {
                    using var output = zip.CreateEntry("Data/" + file.Key).Open();
                    output.Write(file.Value, 0, file.Value.Length);
                }
            }
            paths.Add(path);
        }
        return paths.ToArray();
    }

    // 分包（part01..partNN）：逐包解压、缺包/重号必须在写 Data 之前拒绝、成功才删包。
    static void CheckSplitPacks(string root)
    {
        var files = new Dictionary<string, byte[]>
        {
            ["a.img"] = new byte[] { 1 },
            ["b.img"] = new byte[] { 2, 2 },
            ["c.img"] = new byte[] { 3, 3, 3 },
            ["empty.img"] = Array.Empty<byte>()
        };
        string manifest = ManifestOf(files);

        // 1) 完整分包：全部解压出来，包文件整体删除
        string completeDir = Path.Combine(root, "split-complete");
        PackSplit(completeDir, "MapleDashResources", manifest, files, 3);
        string completeRoot = Path.Combine(root, "split-complete-out");
        var phases = new List<int>();
        string extracted = WzResourceInstaller.ExtractFull(Path.Combine(completeDir, "MapleDashResources.zip"),
            completeRoot, (done, total) => { Require(total == files.Count, "Split progress total is wrong"); phases.Add(done); });
        Require(extracted == manifest, "Split pack returned the wrong manifest");
        foreach (var file in files)
            Require(File.ReadAllBytes(Path.Combine(completeRoot, "Data/" + file.Key)).Length == file.Value.Length,
                "Split extraction lost " + file.Key);
        Require(phases[phases.Count - 1] == files.Count, "Split progress did not reach the file count");
        Require(Directory.GetFiles(completeDir, "*.zip").Length == 0, "Split parts were not deleted after success");

        // 2) 缺第 2 包：在解压任何文件之前失败，Data 目录不产生
        string gapDir = Path.Combine(root, "split-gap");
        PackSplit(gapDir, "MapleDashResources", manifest, files, 3);
        File.Delete(Path.Combine(gapDir, "MapleDashResources.part02.zip"));
        string gapRoot = Path.Combine(root, "split-gap-out");
        ExpectFailure(() => WzResourceInstaller.ExtractFull(Path.Combine(gapDir, "MapleDashResources.zip"), gapRoot));
        Require(!Directory.Exists(Path.Combine(gapRoot, "Data")), "Incomplete split started extracting");
        Require(Directory.GetFiles(gapDir, "*.zip").Length == 2, "Incomplete split deleted its parts");

        // 3) 包号重复（part02 被 part01 覆盖）：拒绝，不要装出缺文件的 Data
        string dupDir = Path.Combine(root, "split-dup");
        PackSplit(dupDir, "MapleDashResources", manifest, files, 3);
        File.Copy(Path.Combine(dupDir, "MapleDashResources.part01.zip"),
            Path.Combine(dupDir, "MapleDashResources.part02.zip"), true);
        ExpectFailure(() => WzResourceInstaller.ExtractFull(Path.Combine(dupDir, "MapleDashResources.zip"),
            Path.Combine(root, "split-dup-out")));

        // 4) 包号不从 1 开始（只传了后面几包）：拒绝
        string shiftDir = Path.Combine(root, "split-shift");
        PackSplit(shiftDir, "MapleDashResources", manifest, files, 2, firstIndex: 2);
        ExpectFailure(() => WzResourceInstaller.ExtractFull(Path.Combine(shiftDir, "MapleDashResources.zip"),
            Path.Combine(root, "split-shift-out")));

        // 5) 分包也要能走校验安装路径：条目分散在不同包里照样逐文件校验
        string installDir = Path.Combine(root, "split-install");
        PackSplit(installDir, "MapleDashResources", manifest, files, 2);
        string installRoot = Path.Combine(root, "split-install-out");
        WzResourceInstaller.Install(Path.Combine(installDir, "MapleDashResources.zip"), installRoot, manifest, null);
        WzResourceInstaller.Verify(installRoot, manifest, null);
        Require(Directory.GetFiles(installDir, "*.zip").Length == 0, "Split install left parts behind");

        // 6) 单包与分包同时存在时按修改时间取新的：旧整包不能被新分包顶掉
        string mixDir = Path.Combine(root, "split-mix");
        PackSplit(mixDir, "MapleDashResources", manifest, new Dictionary<string, byte[]> { ["only.img"] = new byte[] { 7 } }, 2);
        string staleZip = Path.Combine(mixDir, "MapleDashResources.zip");
        Pack(staleZip, manifest, new Dictionary<string, byte[]> { ["stale.img"] = new byte[] { 9 } });
        File.SetLastWriteTimeUtc(staleZip, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        string mixRoot = Path.Combine(root, "split-mix-out");
        WzResourceInstaller.ExtractFull(staleZip, mixRoot);
        Require(File.Exists(Path.Combine(mixRoot, "Data/only.img")), "Newer split pack was ignored");
        Require(!File.Exists(Path.Combine(mixRoot, "Data/stale.img")), "Stale single archive won over split parts");
        Require(Directory.GetFiles(mixDir, "*.zip").Length == 0, "Mixed pack cleanup missed files");

        // 7) 分包补丁走在线事务安装：变更文件分散在多个包里也要逐个校验并提交
        string patchDir = Path.Combine(root, "split-patch");
        var patched = new Dictionary<string, byte[]>(files) { ["added.img"] = new byte[] { 11, 12 } };
        patched.Remove("b.img");
        string patchedManifest = ManifestOf(patched);
        PackSplit(patchDir, "MapleDashResources.patch", patchedManifest,
            new Dictionary<string, byte[]> { ["added.img"] = patched["added.img"] }, 2, patch: true);
        const string version = "{\"versionCode\":7,\"resUrl\":\"https://example.com/p.zip\"}";
        WzResourceInstaller.Install(Path.Combine(patchDir, "MapleDashResources.patch.zip"), installRoot,
            patchedManifest, null, null, version, manifest);
        WzResourceInstaller.Verify(installRoot, patchedManifest, null);
        Require(File.Exists(Path.Combine(installRoot, "Data/added.img")), "Split patch lost its new file");
        Require(!File.Exists(Path.Combine(installRoot, "Data/b.img")), "Split patch kept a deleted file");
        Require(File.ReadAllText(Path.Combine(installRoot, "Data/.resUpdate.json")) == version,
            "Split patch did not carry the resource version");
        Require(Directory.GetFiles(patchDir, "*.zip").Length == 0, "Split patch left parts behind");
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
