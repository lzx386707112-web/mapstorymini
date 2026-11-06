using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

// 一键出包。命令行和菜单都能用，避免手动点构建时漏设置。
// 用法（命令行）：
//   Unity -batchmode -quit -projectPath <项目> -executeMethod BuildToDevice.BuildMono
//   Unity -batchmode -quit -projectPath <项目> -executeMethod BuildToDevice.SwitchToWindows
//   Unity -batchmode -quit -projectPath <项目> -executeMethod BuildToDevice.BuildWindows
public static class BuildToDevice
{
    const string SceneDir  = "Assets/Scenes";
    const string ScenePath = "Assets/Scenes/Main.unity";
    const string ApkPath   = "Builds/MapleDash.apk";
    const string WindowsDir = "Builds/Windows";
    const string WindowsExe = "Builds/Windows/MapleDash.exe";

    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [MenuItem("MapleDash/构建 APK（本机调试）")]
    public static void BuildMono() => Build(false);

    [MenuItem("MapleDash/构建 APK（IL2CPP 发布）")]
    public static void BuildIl2cpp() => Build(true);

    [MenuItem("MapleDash/切换平台到 Windows")]
    public static void SwitchToWindowsEditor() => SwitchToWindows();

    [MenuItem("MapleDash/构建 Windows（桌面版）")]
    public static void BuildWindowsMenu() => BuildWindows();

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
                  " APK大小=" + (File.Exists(ApkPath) ? (new FileInfo(ApkPath).Length / 1048576f).ToString("0.0") + "MB" : "未生成") +
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
        PlayerSettings.productName = "未来岛";
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

        // 下面这些是跨平台共用字段，Windows 构建会把它们改成「1280×720 窗口 / 后台继续跑」。
        // 手机端显式写回原值，免得「先出过 Windows 包」之后，接着出的 APK 和 ProjectSettings 跟着变。
        PlayerSettings.runInBackground = false;
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
        PlayerSettings.defaultIsNativeResolution = true;
        PlayerSettings.defaultScreenWidth = 1920;
        PlayerSettings.defaultScreenHeight = 1080;
        PlayerSettings.resizableWindow = false;

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

    // ——————————————————————— Windows 桌面版 ———————————————————————
    // 和 Android 一样的「两步」：切平台必须单独一个进程，切完再构建。

    public static void SwitchToWindows()
    {
        if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows64)
        {
            Debug.Log("[Build] 活动平台已是 Windows");
            EditorApplication.Exit(0);
            return;
        }

        bool ok = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
        Debug.Log("[Build] 切换平台=" + ok + " 当前=" + EditorUserBuildSettings.activeBuildTarget);
        AssetDatabase.SaveAssets();
        EditorApplication.Exit(ok ? 0 : 1);
    }

    public static void BuildWindows()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
        {
            Debug.LogWarning("[Build] 活动平台不是 Windows，先执行切换；请再运行一次构建");
            SwitchToWindows();
            return;
        }

        ConfigureWindowsPlayer();
        EnsureScene();

        Directory.CreateDirectory(WindowsDir);
        if (File.Exists(WindowsExe)) File.Delete(WindowsExe);

        var opts = new BuildPlayerOptions
        {
            scenes           = new[] { ScenePath },
            locationPathName = WindowsExe,
            target           = BuildTarget.StandaloneWindows64,
            targetGroup      = BuildTargetGroup.Standalone,
            options          = BuildOptions.None,
        };

        Debug.Log("[Build] 开始构建：Windows x64（Mono）");
        var report = BuildPipeline.BuildPlayer(opts);
        var s = report.summary;

        Debug.Log("[Build] 结果=" + s.result +
                  " 大小=" + (s.totalSize / 1048576f).ToString("0.0") + "MB" +
                  " 耗时=" + s.totalTime.TotalSeconds.ToString("0") + "s" +
                  " 错误=" + s.totalErrors);

        if (s.result != BuildResult.Succeeded || !File.Exists(WindowsExe))
        {
            Debug.LogError("[Build] 失败，详见上方日志");
            EditorApplication.Exit(1);
        }
        Debug.Log("[Build] 产物：" + Path.GetFullPath(WindowsDir));
        EditorApplication.Exit(0);
    }

    static void ConfigureWindowsPlayer()
    {
        PlayerSettings.companyName = "MapleDash";
        PlayerSettings.productName = "未来岛";

        // 只在 macOS 上交叉出 Windows 包时，IL2CPP 变体要走 MSVC 链接，做不出来；
        // Mono 后端可以直接由 macOS 产出，桌面版对启动性能的要求也不像手机那么敏感。
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
        PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Standalone, ManagedStrippingLevel.Disabled);

        // OnGUI 依赖引擎内置 GUISkin，Strip Engine Code 会把它裁掉（GUI.skin 为 null，每帧 NRE）。
        PlayerSettings.stripEngineCode = false;

        // 固定 1280×720 窗口：正好等于 UiKit 的虚拟设计基准（Scale = 720/720 = 1），
        // 所有 UI 1:1 呈现，不会被缩放糊掉。
        // FullScreenWindow 会按桌面原生分辨率铺满、忽略下面两个数字，所以必须切到 Windowed
        // 并关掉 defaultIsNativeResolution，1280×720 才真正生效。
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultIsNativeResolution = false;
        PlayerSettings.defaultScreenWidth = 1280;
        PlayerSettings.defaultScreenHeight = 720;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.allowFullscreenSwitch = true;      // Alt+Enter 仍可临时切全屏

        // 桌面版是「客户端 + 进程内服务端」同进程：切出去再回来不能让世界停摆。
        PlayerSettings.runInBackground = true;

        QualitySettings.antiAliasing = 0;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;

        AssetDatabase.SaveAssets();

        Debug.Log("[Build] 读回：backend=" + PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone) +
                  " 全屏=" + PlayerSettings.fullScreenMode +
                  " 分辨率=" + PlayerSettings.defaultScreenWidth + "x" + PlayerSettings.defaultScreenHeight +
                  " 原生分辨率=" + PlayerSettings.defaultIsNativeResolution +
                  " active=" + EditorUserBuildSettings.activeBuildTarget);
    }
}
