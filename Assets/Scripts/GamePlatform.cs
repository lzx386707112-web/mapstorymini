using System;
using System.IO;
using UnityEngine;

// 平台差异的唯一出口。
//
// 现在有三处会按平台分叉，全放在这里，业务代码不要再自己写 #if UNITY_STANDALONE 判断：
//   1) 资源根目录：Android 是 /sdcard/mapstory，Windows 桌面版是 D:\mapstory，编辑器是工程外的 MapleDashResources；
//   2) 屏上虚拟按键：只有触屏（Android）需要，桌面版是纯键鼠，整层不画也不吃点击；
//   3) MCV 视频：只有 Windows 缺解码库（Native/Mcv/build.py 目前只出 dylib / so）。
public static class GamePlatform
{
    // Windows 端约定固定的资源目录：安装在别的盘符时不做自动探测，避免「客户端找不到自己刚放的包」。
    public const string WindowsResourceRoot = @"D:\mapstory";

    // 桌面独立播放器（Windows / macOS / Linux）：键鼠操作。
    // 编辑器不算 —— 虚拟按键编辑器要在编辑器里摆位置、截图验收，不能跟着一起关掉。
    public static bool DesktopPlayer
    {
        get
        {
#if UNITY_EDITOR
            return false;
#elif UNITY_STANDALONE
            return true;
#else
            return false;
#endif
        }
    }

    // 屏上虚拟按键（摇杆/按键层/布局编辑器）是否可用。
    public static bool TouchControls => !DesktopPlayer;

    // MCV 视频解码是否可用。Windows 版没有 mapledash_mcv.dll，调用会抛 DllNotFoundException，
    // 所以在装载入口直接跳过，让「Boss 登场/技能视频」退化成没有视频，而不是每次施法打一堆异常。
    public static bool MoviePlayback
    {
        get
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            return false;
#else
            return true;
#endif
        }
    }

    static string resourceRoot;

    /// <summary>资源包根目录（其下有 Data/）。</summary>
    public static string ResourceRoot
    {
        get
        {
            if (resourceRoot == null) resourceRoot = ResolveResourceRoot();
            return resourceRoot;
        }
    }

    /// <summary>原始 IMG 树根目录（ResourceRoot/Data）。</summary>
    public static string DataRoot => Path.Combine(ResourceRoot, "Data");

    static string ResolveResourceRoot()
    {
        // 联调/排查用：指到任意一套 Data 上跑（Windows 上没必要为了换资源去搬盘符）。
        string overridden = Environment.GetEnvironmentVariable("MAPLEDASH_RESOURCE_ROOT");
        if (!string.IsNullOrEmpty(overridden)) return Path.GetFullPath(overridden);
#if UNITY_ANDROID && !UNITY_EDITOR
        return "/sdcard/mapstory";
#elif UNITY_STANDALONE && !UNITY_EDITOR
        return ResolveStandaloneRoot();
#else
        // 编辑器：原始 IMG 树在工程之外（MapleDash/../MapleDashResources/Data）
        return Path.GetFullPath(Path.Combine(Application.dataPath, "../../MapleDashResources"));
#endif
    }

#if UNITY_STANDALONE && !UNITY_EDITOR
    // 约定路径是 D:\mapstory；客户机没有 D 盘时退回「exe 同级的 mapstory」并打一条警告，
    // 不让整台机器因为盘符不同直接起不来（加载页会把实际路径显示出来）。
    static string ResolveStandaloneRoot()
    {
        if (HasDrive("D")) return WindowsResourceRoot;
        string exeDir = Path.GetDirectoryName(Application.dataPath);
        string fallback = Path.Combine(string.IsNullOrEmpty(exeDir) ? "." : exeDir, "mapstory");
        Debug.LogWarning("[资源] 未找到 D 盘，资源目录改用 " + fallback + "（约定路径为 " + WindowsResourceRoot + "）");
        return fallback;
    }

    static bool HasDrive(string letter)
    {
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                // 光驱/无根目录的盘符不能当资源盘（客户机上 D: 是光驱的情况不少见）
                if (drive.DriveType == DriveType.CDRom || drive.DriveType == DriveType.NoRootDirectory) continue;
                if (string.Equals(drive.Name, letter + ":" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(drive.Name, letter + ":", StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        catch (Exception) { }   // 极少数受限环境拿不到盘符列表，按「没有」处理，走同级目录兜底
        return false;
    }
#endif
}
