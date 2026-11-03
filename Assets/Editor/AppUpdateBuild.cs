using System.IO;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// JSON 是独立的应用更新版本；构建时仅校验，不能用 APK 版本覆盖它。
public sealed class AppUpdateBuild : IPreprocessBuildWithReport
{
    public int callbackOrder => -1000;
    [System.Serializable]
    sealed class Manifest
    {
        public int versionCode;
        public string versionName;
    }
    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != UnityEditor.BuildTarget.Android) return;
        const string path = "Assets/Resources/update.json";
        try
        {
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
            if (manifest == null || manifest.versionCode < 1 || string.IsNullOrWhiteSpace(manifest.versionName))
                throw new System.Exception("versionCode 必须为正整数，versionName 不能为空");
        }
        catch (System.Exception e)
        {
            throw new BuildFailedException("本地更新配置无效：" + path + "：" + e.Message);
        }
    }
}
