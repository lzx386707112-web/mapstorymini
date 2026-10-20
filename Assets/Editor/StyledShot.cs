using UnityEngine;

// 用一套「已装扮」的档案进战斗并连拍：验证头发/服饰/武器覆盖层 + 技能 + 伤害数字
// 在实机里真的渲染出来了（覆盖层是世界空间 SpriteRenderer，会进 RenderTexture 截图）。
public class StyledShot
{
    public static void Run()
    {
        // 预先装扮：法师 + 蓝发 + 紫衣 + 法杖，金币 320（HUD 用）
        PlayerPrefs.SetString("mxd.cls", "mage");
        PlayerPrefs.SetString("mxd.hair", "blue");
        PlayerPrefs.SetString("mxd.outfit", "purple");
        PlayerPrefs.SetString("mxd.equipWeapon", "w_staff");
        PlayerPrefs.SetString("mxd.owned", "w_sword_basic,w_staff");
        PlayerPrefs.SetInt("mxd.gold", 320);
        PlayerPrefs.Save();

        // 复用 PlaySmokeTest 的进 Play + 截图流程（它会读命令行里的 -smokeShots 等）
        PlaySmokeTest.Run();
    }
}
