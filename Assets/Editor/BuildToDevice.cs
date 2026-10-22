using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

// 一键出 APK。命令行和菜单都能用，避免手动点构建时漏设置。
// 用法（命令行）：
//   Unity -batchmode -quit -projectPath <项目> -executeMethod BuildToDevice.BuildMono
public static class BuildToDevice
{
    const string SceneDir  = "Assets/Scenes";
    const string ScenePath = "Assets/Scenes/Main.unity";
    const string ApkPath   = "Builds/MapleDash.apk";

    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [MenuItem("MapleDash/构建 APK（本机调试）")]
    public static void BuildMono() => Build(false);

    [MenuItem("MapleDash/构建 APK（IL2CPP 发布）")]
    public static void BuildIl2cpp() => Build(true);

    // 只读诊断：不修改任何设置，把当前工程真实状态打出来
    public static void Dump()
    {
        Debug.Log("[Dump] active=" + EditorUserBuildSettings.activeBuildTarget +
                  " arch=" + PlayerSettings.Android.targetArchitectures +
                  " backend=" + PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android) +
                  " minSdk=" + (int)PlayerSettings.Android.minSdkVersion +
                  " sdk=" + EditorPrefs.GetString("AndroidSdkRoot") +
                  " jdk=" + EditorPrefs.GetString("JdkPath"));
        EditorApplication.Exit(0);
    }

    // 单独一步：把活动平台切到 Android。
    // batchmode 下 SwitchActiveBuildTarget 需要重载程序集，和随后的 BuildPlayer 放在同一次运行里会失效
    // （表现为 BuildPlayer 读到的 targetArchitectures 是 None），所以拆成两个进程跑。
    public static void SwitchToAndroid()
    {
        if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
        {
            Debug.Log("[Build] 活动平台已是 Android");
            EditorApplication.Exit(0);
            return;
        }

        bool ok = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        Debug.Log("[Build] 切换平台=" + ok + " 当前=" + EditorUserBuildSettings.activeBuildTarget);
        AssetDatabase.SaveAssets();
        EditorApplication.Exit(ok ? 0 : 1);
    }

    static void Build(bool il2cpp)
    {
        // 同一次进程里切平台会让 BuildPlayer 读不到架构（报 Target architecture not specified）
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            Debug.LogWarning("[Build] 活动平台不是 Android，先执行切换；请再运行一次构建");
            SwitchToAndroid();
            return;
        }

        ConfigurePaths();
        ConfigurePlayer();
        EnsureScene();

        Directory.CreateDirectory("Builds");
        if (File.Exists(ApkPath)) File.Delete(ApkPath);

        var opts = new BuildPlayerOptions
        {
            scenes           = new[] { ScenePath },
            locationPathName = ApkPath,
            target           = BuildTarget.Android,
            targetGroup      = BuildTargetGroup.Android,
            options          = BuildOptions.None,
        };

        Debug.Log("[Build] 开始构建：backend=" + (il2cpp ? "IL2CPP" : "Mono") + " arch=ARM64");
        var report = BuildPipeline.BuildPlayer(opts);
        var s = report.summary;

        Debug.Log("[Build] 结果=" + s.result +
                  " 大小=" + (s.totalSize / 1048576f).ToString("0.0") + "MB" +
                  " 耗时=" + s.totalTime.TotalSeconds.ToString("0") + "s" +
                  " 错误=" + s.totalErrors);

        if (s.result != BuildResult.Succeeded)
        {
            Debug.LogError("[Build] 失败，详见上方日志");
            EditorApplication.Exit(1);
        }
        EditorApplication.Exit(0);
    }

    // SDK/NDK/JDK 路径。
    // 用户机器上已有完整 Android SDK（含 34 平台与 build-tools），
    // Unity 自带的 Android 支持模块只装了运行库、没带 SDK，所以必须指过去。
    static void ConfigurePaths()
    {
        string sdk = Path.Combine(Home, "Library/Android/sdk");
        // 机器上原本的 sdkmanager 是 19.0（Java17 字节码），JDK 11 跑不了；
        // 已在 cmdline-tools/6.0 放了一份官方 6.0，兼容 JDK 11。Gradle 仍用 11 最稳。
        string jdk = "/Library/Java/JavaVirtualMachines/jdk-11.jdk/Contents/Home";
        // Unity 2022.3 只认 NDK r23b（23.1.7779620）。目录是从 r26d 克隆的、只改了版本号，
        // 工具链向后兼容，实测能编译；真要发布建议换成官方 r23b。
        // 批量构建时可用环境变量 MAPLEDASH_NDK 指到别的 NDK 做对照排查。
        string ndkEnv = Environment.GetEnvironmentVariable("MAPLEDASH_NDK");
        string ndk = !string.IsNullOrEmpty(ndkEnv) ? ndkEnv : Path.Combine(sdk, "ndk/23.1.7779620");

        if (!Directory.Exists(sdk)) Debug.LogError("[Build] 找不到 SDK：" + sdk);
        if (!Directory.Exists(jdk)) Debug.LogError("[Build] 找不到 JDK：" + jdk);
        if (!Directory.Exists(ndk)) Debug.LogWarning("[Build] 找不到 NDK（Mono 后端不需要）：" + ndk);

        EditorPrefs.SetString("AndroidSdkRoot", sdk);
        EditorPrefs.SetString("JdkPath", jdk);
        EditorPrefs.SetString("AndroidNDKRoot", ndk);   // 旧版键名（2021.1 及以前有效）
        EditorPrefs.SetString("AndroidNdkRoot", ndk);   // 兼容写法
        EditorPrefs.SetString("AndroidNdkRootR23", ndk);
        EditorPrefs.SetBool("AndroidNDKRootInstalledWithUnity", false);
        EditorPrefs.SetBool("AndroidUseEmbeddedSdk", false);

        // 2021.2 起「外部工具」搬进了 UnityEditor.Android.AndroidExternalToolsSettings，
        // 打包用的 NDK 校验读的是它，只写 EditorPrefs 会一直报 "Android NDK not found or invalid"。
        // 该类型在 Android 支持模块里，没装模块时不存在，所以用反射，不硬引用。
        SetAndroidTool("ndkRootPath", ndk);
        SetAndroidTool("sdkRootPath", sdk);
        SetAndroidTool("jdkRootPath", jdk);

        Debug.Log("[Build] SDK=" + sdk + "\nJDK=" + jdk + "\nNDK=" + ndk);
    }

    static void SetAndroidTool(string prop, string value)
    {
        var t = Type.GetType("UnityEditor.Android.AndroidExternalToolsSettings, UnityEditor.Android.Extensions");
        var p = t?.GetProperty(prop, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        if (p == null)
        {
            Debug.LogWarning("[Build] 无法设置 " + prop + "（找不到 AndroidExternalToolsSettings）");
            return;
        }
        p.SetValue(null, value);
    }

    static void ConfigurePlayer()
    {
        PlayerSettings.companyName = "MapleDash";
        PlayerSettings.productName = "枫叶闯关";
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.mapledash.game");

        // Unity 2022.3 的 Android Mono 后端不支持 ARM64（设了也会被清空，报 Target architecture not specified），
        // 只能 IL2CPP。首次构建要编译 il2cpp 运行时，10–20 分钟；之后增量会快很多。
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetIl2CppCompilerConfiguration(BuildTargetGroup.Android, Il2CppCompilerConfiguration.Release);

        // OnGUI 依赖引擎内置的 GUISkin 资源，Strip Engine Code 会把它裁掉，
        // 真机上表现为 OnGUI 里 GUI.skin 为 null、每帧 NullReferenceException。
        PlayerSettings.stripEngineCode = false;
        PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Android, ManagedStrippingLevel.Disabled);

        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

        // 横屏
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

        PlayerSettings.useHDRDisplay = false;
        QualitySettings.antiAliasing = 0;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;

        PlayerSettings.Android.startInFullscreen = true;
        PlayerSettings.Android.renderOutsideSafeArea = false;

        // 国内上架走 APK；改成 true 就是 AAB
        EditorUserBuildSettings.buildAppBundle = false;

        AssetDatabase.SaveAssets();

        Debug.Log("[Build] 读回：arch=" + PlayerSettings.Android.targetArchitectures +
                  " backend=" + PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android) +
                  " minSdk=" + (int)PlayerSettings.Android.minSdkVersion +
                  " active=" + EditorUserBuildSettings.activeBuildTarget);
    }

    // 工程原本没有任何 .unity 文件（GameLoop 靠 RuntimeInitializeOnLoadMethod 自举），
    // 但 BuildPlayer 必须有至少一个场景，否则直接报 "No scenes in build"。
    static void EnsureScene()
    {
        if (!Directory.Exists(SceneDir)) Directory.CreateDirectory(SceneDir);

        if (!File.Exists(ScenePath))
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[Build] 已创建空场景 " + ScenePath);
        }

        AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        Debug.Log("[Build] 构建场景列表已设置：" + ScenePath);
    }
}
