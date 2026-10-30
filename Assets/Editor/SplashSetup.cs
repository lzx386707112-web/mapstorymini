using UnityEditor;
using UnityEngine;

// Unity 启动画面（Made with Unity）配置。
//
// 为什么只能"改造"不能"删掉"：本机许可证是 Unity Personal + 2022.3，
// Show Splash Screen / Show Unity Logo 两个开关在编辑器里是锁死的，改 asset 也会被构建打回。
// Personal 允许改的是：背景色、背景图、自定义 logo、动画模式、Draw Mode；
// 限制是 Unity logo 必须留、Overlay Opacity 最低 0.5。
//
// 这里做成"游戏自己的标题卡"：
//   - 背景用纯色 .055/.07/.09（= WzAndroidLoader 里 bootCam 的底色），
//     Android 12+ 的系统启动窗也吃这个色，点图标到出图之间不会白闪；
//     不用 LoadingBackground 那张插画——它中间是亮色的角色，白色标题压上去读不清；
//   - logo 用源端 UI/Login.img/Title/MSTitle（登录界面上那个标题），文字刷白、红叶保色；
//   - Draw Mode = Unity Logo Below，Unity logo 垫在下面、同一段时间，整体只有一轮，
//     不比现在更久（Sequential 会变成两张卡依次播，反而更慢）。
//
// 用法：
//   菜单 MapleDash/启动画面/应用到 Player Settings
//   命令行 Unity -batchmode -quit -projectPath . -executeMethod SplashSetup.ApplyBatch
public static class SplashSetup
{
    const string LogoPath = "Assets/Branding/SplashLogo.png";

    // 每个 logo 最短 2 秒（Unity 下限）。
    const float LogoSeconds = 2f;

    // 与 WzAndroidLoader 创建 bootCam 时用的底色一致；Android 启动窗也吃这个色。
    static readonly Color BackgroundColor = new Color(0.055f, 0.07f, 0.09f, 1f);

    [MenuItem("MapleDash/启动画面/应用到 Player Settings")]
    public static void ApplyMenu() => Apply();

    /// <summary>命令行入口。</summary>
    public static void ApplyBatch() => Apply();

    public static bool Apply()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ConfigureSpriteImporter(LogoPath);

        var logo = AssetDatabase.LoadAssetAtPath<Sprite>(LogoPath);
        if (logo == null)
        {
            Debug.LogError("[Splash] 找不到 sprite：" + LogoPath + "，启动图未写入");
            return false;
        }

        PlayerSettings.SplashScreen.show = true;
        // 2022.3 的公开 API 没有 animation / blurBackground（只能改 PlayerSettings.asset 的序列化字段），
        // 这里保留默认 Dolly：缓慢推近，观感更像片头；模式不影响总时长。
        PlayerSettings.SplashScreen.unityLogoStyle = PlayerSettings.SplashScreen.UnityLogoStyle.LightOnDark;
        // Personal 下限 0.5，再低会被钳回去。
        PlayerSettings.SplashScreen.overlayOpacity = 0.5f;
        PlayerSettings.SplashScreen.backgroundColor = BackgroundColor;
        PlayerSettings.SplashScreen.background = null;
        PlayerSettings.SplashScreen.backgroundPortrait = null;
        PlayerSettings.SplashScreen.drawMode = PlayerSettings.SplashScreen.DrawMode.UnityLogoBelow;
        PlayerSettings.SplashScreen.logos = new[]
        {
            PlayerSettings.SplashScreenLogo.Create(LogoSeconds, logo),
        };

        AssetDatabase.SaveAssets();
        Debug.Log($"[Splash] 已写入：纯色背景 #{ColorUtility.ToHtmlStringRGB(BackgroundColor)} / " +
                  $"logo {LogoPath} / Overlay 0.5 / UnityLogoBelow / {LogoSeconds}s");
        return true;
    }

    // logo 必须是 Sprite(Single)：整张图当一枚 logo，不切图、不压缩、不生成 mipmap。
    static void ConfigureSpriteImporter(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            Debug.LogWarning("[Splash] 读不到导入设置：" + path);
            return;
        }
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.mipmapEnabled = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.alphaIsTransparency = true;
        importer.sRGBTexture = true;
        importer.maxTextureSize = 1024;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }
}
