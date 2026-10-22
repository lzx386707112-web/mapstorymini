using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

// 一键出 WebGL（浏览器调试用）。
// 用法（命令行，分两步，和 Android 一样：切平台要单独一个进程）：
//   Unity -batchmode -quit -projectPath <项目> -executeMethod BuildWebGL.SwitchToWebGL
//   Unity -batchmode -quit -projectPath <项目> -executeMethod BuildWebGL.Build
// 产物：Builds/WebGL/index.html（起个静态服务器就能开）
public static class BuildWebGL
{
    const string ScenePath = "Assets/Scenes/Main.unity";
    const string OutDir    = "Builds/WebGL";

    [MenuItem("MapleDash/切换平台到 WebGL")]
    public static void SwitchToWebGL()
    {
        if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL)
        {
            Debug.Log("[WebGL] 活动平台已是 WebGL");
            EditorApplication.Exit(0);
            return;
        }
        bool ok = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);
        Debug.Log("[WebGL] 切换平台=" + ok + " 当前=" + EditorUserBuildSettings.activeBuildTarget);
        AssetDatabase.SaveAssets();
        EditorApplication.Exit(ok ? 0 : 1);
    }

    [MenuItem("MapleDash/构建 WebGL（浏览器调试）")]
    public static void Build()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
        {
            Debug.LogWarning("[WebGL] 活动平台不是 WebGL，先执行 SwitchToWebGL 再来一次");
            SwitchToWebGL();
            return;
        }

        Configure();
        EnsureScene();

        Directory.CreateDirectory(OutDir);
        var opts = new BuildPlayerOptions
        {
            scenes           = new[] { ScenePath },
            locationPathName = OutDir,
            target           = BuildTarget.WebGL,
            targetGroup      = BuildTargetGroup.WebGL,
            options          = BuildOptions.None,
        };

        Debug.Log("[WebGL] 开始构建 → " + OutDir);
        var report = BuildPipeline.BuildPlayer(opts);
        var s = report.summary;
        Debug.Log("[WebGL] 结果=" + s.result +
                  " 大小=" + (s.totalSize / 1048576f).ToString("0.0") + "MB" +
                  " 耗时=" + s.totalTime.TotalSeconds.ToString("0") + "s" +
                  " 错误=" + s.totalErrors);

        if (s.result != BuildResult.Succeeded)
        {
            Debug.LogError("[WebGL] 失败，详见上方日志");
            EditorApplication.Exit(1);
        }
        // Publish external raw resources beside the web build, outside Unity's data archive.
        var index = (System.Collections.Generic.Dictionary<string, object>)MiniJson.Parse(Resources.Load<TextAsset>("NativeAssetIndex").text);
        foreach (System.Collections.Generic.Dictionary<string, object> file in (System.Collections.Generic.List<object>)index["files"])
        {
            string relative = (string)file["path"];
            string target = Path.Combine(OutDir, "StreamingAssets/Wz/Data", relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(Path.Combine(WzAssets.DataRoot, relative), target, true);
        }
        EditorApplication.Exit(0);
    }

    static void Configure()
    {
        PlayerSettings.companyName = "MapleDash";
        PlayerSettings.productName = "枫叶闯关";

        // WebGL 只有 IL2CPP
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.WebGL, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetIl2CppCompilerConfiguration(BuildTargetGroup.WebGL, Il2CppCompilerConfiguration.Release);

        // OnGUI 依赖引擎内置 GUISkin，裁剪会把它裁掉（安卓上已踩过：GUI.skin 为 null，每帧 NRE）
        PlayerSettings.stripEngineCode = false;
        PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.WebGL, ManagedStrippingLevel.Disabled);

        // 浏览器里异常直接打在控制台，方便 F12 看栈
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.FullWithoutStacktrace;

        // Brotli + 内嵌解压：任意静态服务器（含 python -m http.server）都能直接跑，
        // 不需要配 Content-Encoding: br。体积比 Disabled 小，代价是加载时多一次内存解压。
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        PlayerSettings.WebGL.decompressionFallback = true;

        // 贴图量大（Resources/Art ≈ 115MB），给足堆；开增长模式，不会因为初始值不够直接 OOM
        PlayerSettings.WebGL.memoryGrowthMode = WebGLMemoryGrowthMode.Geometric;
        PlayerSettings.WebGL.initialMemorySize = 512;
        PlayerSettings.WebGL.maximumMemorySize = 4096;
        PlayerSettings.WebGL.dataCaching = true;

        // 自定义模板：16:9 等比铺满窗口、去掉 Unity 页脚、带地图 id 跳转框（见 Assets/WebGLTemplates/MapleDash）
        PlayerSettings.WebGL.template = "PROJECT:MapleDash";
        PlayerSettings.defaultWebScreenWidth = 1280;
        PlayerSettings.defaultWebScreenHeight = 720;
        // 注意：runInBackground / defaultInterfaceOrientation 是跨平台共用字段，
        // WebGL 用不着，改了会连累 APK 流程，所以这里不碰；横屏由模板的 16:9 画布保证。

        QualitySettings.antiAliasing = 0;
        Application.targetFrameRate = 60;

        AssetDatabase.SaveAssets();
        Debug.Log("[WebGL] 读回：backend=" + PlayerSettings.GetScriptingBackend(BuildTargetGroup.WebGL) +
                  " 压缩=" + PlayerSettings.WebGL.compressionFormat +
                  " 回退解压=" + PlayerSettings.WebGL.decompressionFallback +
                  " 初始堆=" + PlayerSettings.WebGL.initialMemorySize + "MB" +
                  " 上限=" + PlayerSettings.WebGL.maximumMemorySize + "MB");
    }

    static void EnsureScene()
    {
        if (!File.Exists(ScenePath))
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[WebGL] 已创建空场景 " + ScenePath);
        }
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
    }
}
