using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Fail a build early if its complete raw Data package is missing/stale.
public sealed class NativeAssetBuildGuard : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;
    static bool IsBackup(string path)
    {
        foreach (string part in path.Replace('\\', '/').Split('/'))
        {
            if (part == ".DS_Store" || part == "Thumbs.db" || part.StartsWith("._", StringComparison.Ordinal) || part.EndsWith("~", StringComparison.Ordinal)) return true;
            string[] tokens = part.ToLowerInvariant().Split('.');
            for (int i = 1; i < tokens.Length; i++)
                if (tokens[i].StartsWith("bak", StringComparison.Ordinal) || tokens[i] == "backup" || tokens[i].EndsWith("-bak", StringComparison.Ordinal)) return true;
        }
        return false;
    }
    public void OnPreprocessBuild(BuildReport report)
    {
        var asset = Resources.Load<TextAsset>("NativeAssetIndex");
        if (asset == null) throw new BuildFailedException("NativeAssetIndex missing. Run tools/package_native_assets.py.");
        var index = (Dictionary<string, object>)MiniJson.Parse(asset.text);
        foreach (Dictionary<string, object> file in (List<object>)index["files"])
        {
            string relative = (string)file["path"];
            if (IsBackup(relative)) continue;
            string path = Path.Combine(WzAssets.DataRoot, relative);
            if (!File.Exists(path)) throw new BuildFailedException("Original IMG package missing: " + relative + ". Run tools/package_native_assets.py.");
            using (var input = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                string hash = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
                if (hash != (string)file["sha256"]) throw new BuildFailedException("Original IMG differs from index: " + relative);
            }
        }
    }
}
