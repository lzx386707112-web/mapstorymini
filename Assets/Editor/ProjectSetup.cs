using System.IO;
using UnityEditor;
using UnityEngine;

// 把"横屏 + ARM64 + IL2CPP + 最低 API 26"这些必须手点的设置，做成一键菜单。
// 少点一次就少一个坑：这些选项里任何一个错了，真机上要么黑屏要么装不上。
public static class ProjectSetup
{
    [MenuItem("MapleDash/一键配置 Android 导出")]
    public static void ConfigureAndroid()
    {
        // 1. 切平台
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

        // 2. 包名与产品名（包名一旦上架就不能改，先定死）
        PlayerSettings.companyName = "MapleDash";
        PlayerSettings.productName = "未来岛";
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.mapledash.game");

        // 3. 脚本后端：ARM64 只能用 IL2CPP，Mono 不支持
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

        // 4. 最低版本 26（Android 8.0）：覆盖面足够，且省掉大量旧系统兼容代码
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

        // 5. 横屏：横版游戏必须横屏，竖屏只够看 2–3 个身位
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

        // 6. 渲染：2D 不需要 HDR 和抗锯齿，手机上是纯浪费
        PlayerSettings.useHDRDisplay = false;
        QualitySettings.antiAliasing = 0;
        QualitySettings.vSyncCount = 0;          // 手机上 vSync 无效，靠 targetFrameRate
        Application.targetFrameRate = 60;

        // 7. 全屏
        PlayerSettings.Android.startInFullscreen = true;
        PlayerSettings.Android.renderOutsideSafeArea = false;

        // 8. 默认出 APK（国内渠道要 APK；上 Google Play 时再勾 Build App Bundle）
        EditorUserBuildSettings.buildAppBundle = false;

        AssetDatabase.SaveAssets();
        Debug.Log("[MapleDash] Android 导出配置完成：ARM64 / IL2CPP / API26 / 横屏 / 60fps");
        EditorUtility.DisplayDialog("配置完成",
            "已设置：\n\n• 横屏（左右自动旋转）\n• ARM64 + IL2CPP\n• 最低 API 26\n• 60fps，关闭抗锯齿\n\n" +
            "接下来：File → Build Settings → Build And Run\n（手机用 USB 连上并开启「开发者选项 → USB 调试」）",
            "好");
    }

    [MenuItem("MapleDash/检查环境")]
    public static void CheckEnv()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Build Target: " + EditorUserBuildSettings.activeBuildTarget);
        sb.AppendLine("Backend: " + PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android));
        sb.AppendLine("Arch: " + PlayerSettings.Android.targetArchitectures);
        sb.AppendLine("Min SDK: " + PlayerSettings.Android.minSdkVersion);
        sb.AppendLine("Bundle ID: " + PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android));
        sb.AppendLine();

        // 用常见路径探测，避免依赖 Android 模块的 Editor API
        string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal);
        string[] sdkCandidates =
        {
            Path.Combine(home, "Library/Android/sdk"),
            "/usr/local/share/android-sdk",
            "/opt/homebrew/share/android-sdk",
        };
        string found = "未找到 —— 去 Preferences → External Tools 手动指定";
        foreach (var p in sdkCandidates)
            if (Directory.Exists(p)) { found = p; break; }
        sb.AppendLine("Android SDK: " + found);

        string javaHome = System.Environment.GetEnvironmentVariable("JAVA_HOME") ?? "";
        sb.AppendLine("JAVA_HOME: " + (string.IsNullOrEmpty(javaHome) ? "未设置" : javaHome));
        sb.AppendLine();
        sb.AppendLine($"项目版本: {Application.unityVersion}");

        Debug.Log("[MapleDash] 环境检查\n" + sb);
        EditorUtility.DisplayDialog("环境检查", sb.ToString(), "好");
    }
}
