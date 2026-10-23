using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Schema.sql 是「表结构一比一复刻源端」的唯一真源，但 Unity 只把 .txt/.json/.bytes
// 等已知扩展名当 TextAsset 收录，.sql 不会进包；而它又放在 Assets/Scripts 下，
// Resources.Load 根本找不到。真机表现为启动后：
//   InvalidOperationException: Schema.sql 缺失
//   at DbStore.LoadSchema → GameServer.Start → GameSession.Boot → LoginFlow.Start
// 离线 tools/server-check 不暴露该问题（csproj 把 .sql 拷到输出目录，走文件系统读到了）。
//
// 这里做三件事：
//   1. Schema.sql 变更时自动同步出一份 Assets/Resources/Server/Db/Schema.txt；
//   2. 打包前强制同步并校验 Resources 里真的能读到，读不到直接让构建失败；
//   3. 菜单 MapleDash/同步 Schema 到 Resources 可手动触发。
internal static class SchemaSync
{
    internal const string Source = "Assets/Scripts/Server/Db/Schema.sql";
    internal const string TargetDir = "Assets/Resources/Server/Db";
    internal const string Target = TargetDir + "/Schema.txt";
    // Resources.Load 用的路径（不含扩展名）
    internal const string ResourcePath = "Server/Db/Schema";

    [MenuItem("MapleDash/同步 Schema 到 Resources")]
    internal static void SyncMenu() => Sync(true);

    internal static void Sync(bool verbose)
    {
        if (!File.Exists(Source))
        {
            if (verbose) Debug.LogError("[SchemaSync] 找不到源文件 " + Source);
            return;
        }
        string text = File.ReadAllText(Source);
        if (string.IsNullOrWhiteSpace(text))
        {
            if (verbose) Debug.LogError("[SchemaSync] " + Source + " 是空文件");
            return;
        }

        // 已一致就不写，避免无谓的重新导入
        if (File.Exists(Target) && File.ReadAllText(Target) == text)
        {
            if (verbose) Debug.Log("[SchemaSync] 已是最新：" + Target);
            return;
        }

        Directory.CreateDirectory(TargetDir);
        File.WriteAllText(Target, text);
        AssetDatabase.ImportAsset(Target, ImportAssetOptions.ForceUpdate);
        Debug.Log("[SchemaSync] 已同步 " + Source + " → " + Target + "（" + text.Length + " 字符）");
    }
}

// Schema.sql 被改动时自动跟随同步（编辑器内 Play 也就不会用旧 schema）
sealed class SchemaPostprocessor : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        for (int i = 0; i < imported.Length; i++)
        {
            if (imported[i].Replace('\\', '/') != SchemaSync.Source) continue;
            SchemaSync.Sync(false);
            return;
        }
    }
}

// 打 APK 前兜底：同步 + 校验 Resources 真能读到，读不到就让构建失败，
// 而不是装到手机启动后才抛「Schema.sql 缺失」。
sealed class SchemaBuildGuard : IPreprocessBuildWithReport
{
    public int callbackOrder => -100; // 早于 NativeAssetBuildGuard 之类的校验

    public void OnPreprocessBuild(BuildReport report)
    {
        SchemaSync.Sync(true);
        if (Resources.Load<TextAsset>(SchemaSync.ResourcePath) == null)
            throw new BuildFailedException(
                "Resources/" + SchemaSync.ResourcePath + " 不可读：Schema.sql 没能同步进 Resources，"
                + "真机会抛「Schema.sql 缺失」。检查 " + SchemaSync.Target + " 是否存在且非空。");
    }
}
