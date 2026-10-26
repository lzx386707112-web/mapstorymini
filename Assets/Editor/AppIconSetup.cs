using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// APK 桌面图标：把 Assets/Branding/AppIcon.png 铺到 Android 的四类图标槽位
// （旧版方形 / 圆形 / 8.0+ 自适应前景），自适应背景由 AppIconBackground.png 兜底。
//
// 两张图必须是「方形全出血」：白底、外圈圆角、生成器水印都要在入库前处理掉。
// 启动器自己会套圆角/圆形遮罩，图里再留一圈圆角就会变成「圆角套圆角」。
//
// 用法：
//   菜单 MapleDash/应用图标/应用到 Android
//   命令行 Unity -batchmode -quit -projectPath . -executeMethod AppIconSetup.ApplyBatch
public static class AppIconSetup
{
    const string IconPath = "Assets/Branding/AppIcon.png";
    const string BgPath   = "Assets/Branding/AppIconBackground.png";

    [MenuItem("MapleDash/应用图标/应用到 Android")]
    public static void ApplyMenu() => Apply(true);

    // 命令行入口：Unity -batchmode -quit -projectPath . -executeMethod AppIconSetup.ApplyBatch
    public static void ApplyBatch() => Apply(true);

    /// <summary>把图标写进 PlayerSettings，返回是否连自适应图标也一起写成功。</summary>
    public static bool Apply(bool verbose)
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ConfigureImporter(IconPath, 1024);
        ConfigureImporter(BgPath, 512);

        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
        var bg = AssetDatabase.LoadAssetAtPath<Texture2D>(BgPath);
        if (icon == null)
        {
            Debug.LogError("[AppIcon] 找不到 " + IconPath + "，图标未应用");
            return false;
        }
        if (bg == null)
            Debug.LogWarning("[AppIcon] 找不到 " + BgPath + "，自适应背景会退回 Unity 默认灰色");

        // 自适应图标的槽位种类（Legacy/Round/Adaptive）定义在 Android 模块的
        // UnityEditor.Android.Extensions.dll 里。这里反射取，Editor 脚本就不会对
        // 该 DLL 产生硬引用——tools/unity-compile-check 只引 UnityEditor.dll，硬引用会直接编不过。
        var kindType = FindAndroidIconKindType();
        if (kindType == null)
        {
            Debug.LogError("[AppIcon] 没找到 Android 模块的 AndroidPlatformIconKind，图标未应用" +
                           "（Unity 安装时需勾选 Android Build Support）");
            return false;
        }

        int legacy = ApplyKind(kindType, "Legacy", icon, null);
        int round = ApplyKind(kindType, "Round", icon, null);
        int adaptive = ApplyKind(kindType, "Adaptive", icon, bg);

        AssetDatabase.SaveAssets();
        if (verbose)
        {
            Debug.Log($"[AppIcon] Android 图标已写入：方形 {legacy} 档 / 圆形 {round} 档 / " +
                      $"自适应 {adaptive} 档（背景 {(bg != null ? "AppIconBackground.png" : "缺失")}）");
        }
        return adaptive > 0;
    }

    static int ApplyKind(Type kindType, string kindName, Texture2D foreground, Texture2D background)
    {
        var kind = GetIconKind(kindType, kindName);
        if (kind == null)
        {
            Debug.LogWarning("[AppIcon] 取不到图标种类 " + kindName);
            return 0;
        }

        var icons = PlayerSettings.GetPlatformIcons(BuildTargetGroup.Android, kind);
        if (icons == null || icons.Length == 0)
        {
            Debug.LogWarning("[AppIcon] " + kindName + " 没有可写入的尺寸档位");
            return 0;
        }

        int filled = 0;
        foreach (var slot in icons)
        {
            // 自适应图标是两层：0 层背景、1 层前景。SetTextures 要求一次性把 2 层都传进去，
            // 单层赋值会抛「requires atleast 2 layers」。两层都给满屏图，启动器遮罩只裁掉四角。
            if (background != null && slot.maxLayerCount >= 2)
                slot.SetTextures(new[] { background, foreground });
            else
                slot.SetTextures(new[] { foreground });
            filled++;
        }
        PlayerSettings.SetPlatformIcons(BuildTargetGroup.Android, kind, icons);
        return filled;
    }

    // AndroidPlatformIconKind.Legacy/Round/Adaptive 在 2022 里是静态只读字段（早期版本曾用属性），
    // 两种都试一遍，免得升 Unity 换个版本就取不到。
    static PlatformIconKind GetIconKind(Type kindType, string kindName)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        var field = kindType.GetField(kindName, flags);
        if (field != null) return field.GetValue(null) as PlatformIconKind;
        var prop = kindType.GetProperty(kindName, flags);
        return prop?.GetValue(null) as PlatformIconKind;
    }

    static Type FindAndroidIconKindType()
    {
        const string fullName = "UnityEditor.Android.AndroidPlatformIconKind";
        var t = Type.GetType(fullName + ", UnityEditor.Android.Extensions");
        if (t != null) return t;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            t = asm.GetType(fullName);
            if (t != null) return t;
        }
        return null;
    }

    // 图标要按「原图直出」对待：不缩放成 2 的幂、不生成 mipmap、不压缩，
    // 否则小尺寸档位会出黑边或糊掉。Unity 构建时会自己按各档位重采样。
    static void ConfigureImporter(string path, int maxSize)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            Debug.LogWarning("[AppIcon] 读不到导入设置：" + path);
            return;
        }
        importer.textureType = TextureImporterType.Default;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.alphaIsTransparency = true;
        importer.sRGBTexture = true;
        importer.maxTextureSize = maxSize;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.isReadable = false;
        importer.SaveAndReimport();
    }
}
