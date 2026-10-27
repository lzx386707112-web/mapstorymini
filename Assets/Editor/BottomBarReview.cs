using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;

// 底栏视觉复核：进 Play → 等场景就绪 → 按状态各截一张，再用工具裁底栏对比原端合成图。
// Unity -batchmode -projectPath . -executeMethod BottomBarReview.Run -logFile /tmp/bar_review.log
// 产物落在 Library/SbPreview/（沙箱里不写 /tmp）。
public static class BottomBarReview
{
    public static void Run()
    {
        SessionState.SetBool("BottomBarReview", true);
        HudChecks.SetGameViewSize(1280, 720);
        PlayerPrefs.SetInt("autostart", 1);
        PlayerPrefs.SetInt("hide_tuning", 1);
        PlayerPrefs.Save();
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    static void Hook() { EditorApplication.update += Tick; }

    static void Tick()
    {
        if (!SessionState.GetBool("BottomBarReview", false) || !EditorApplication.isPlaying ||
            GameLoop.I == null || GameLoop.I.player == null) return;
        SessionState.SetBool("BottomBarReview", false);
        new GameObject("BottomBar review").AddComponent<BottomBarReviewProbe>();
    }
}

public class BottomBarReviewProbe : MonoBehaviour
{
    static string OutDir
    {
        get
        {
            var dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library/SbPreview");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    static void Shot(string name) { ScreenCapture.CaptureScreenshot(Path.Combine(OutDir, name)); }

    IEnumerator Start()
    {
        Application.runInBackground = true;
        yield return new WaitForSecondsRealtime(2f);
        if (GameLoop.I.state != GameLoop.GameState.Playing)
        {
            Debug.LogError("[BottomBarReview] boot failed");
            EditorApplication.Exit(1);
            yield break;
        }

        // 1) 收起态：聊天条压深色底、显示最后一条记录；两个通知白槽常驻；
        //    快捷栏展开（QuickSlotD = 关闭快捷键，向下箭头）
        BottomBar.ResetChat();
        BottomBar.SubmitChat("客户端上次闪退可能是地图不存在或异常，现已将你转移。", NativeChatChannel.All);
        BottomBar.QuickCollapsed = false;
        Pointer.TestPos = null;
        yield return new WaitForSecondsRealtime(0.5f);
        Shot("bar_1_collapsed.png");
        yield return new WaitForSecondsRealtime(0.6f);

        // 2) 输入态：频道按钮 base/chatTarget + 白色输入行
        BottomBar.FocusChat();
        yield return new WaitForSecondsRealtime(0.5f);
        Shot("bar_2_input.png");
        yield return new WaitForSecondsRealtime(0.6f);

        // 3) 悬停第 6 个小按钮（QuickSlotD）：核对 mouseOver 用的是按钮皮肤，不是键名字形
        BottomBar.ResetChat();
        Pointer.TestPos = BottomBar.SmallButtonRect(5).center;
        yield return new WaitForSecondsRealtime(0.5f);
        Shot("bar_3_hover_quickslot.png");
        yield return new WaitForSecondsRealtime(0.6f);

        // 4) 快捷栏收起态：同一按钮换成 QuickSlot（打开快捷键，向上箭头）
        Pointer.TestPos = null;
        BottomBar.QuickCollapsed = true;
        yield return new WaitForSecondsRealtime(0.5f);
        Shot("bar_4_quickslot_collapsed.png");
        yield return new WaitForSecondsRealtime(0.6f);

        // 5) 聊天窗多类型混排：白=玩家、粉=type5、浅蓝=type6，核对行距与上色
        BottomBar.ResetChat();
        BottomBar.QuickCollapsed = false;
        BottomBar.AddChat("[所有人] " + Profile.playerName + " : 有人在射手村练级吗", BottomBar.ChatWhite);
        NativeSystemMessage.Drop(6, "需要传送卡才能启动这个传送门。");
        BottomBar.AddChat("[组队] " + Profile.playerName + " : 等我一下", BottomBar.ChatWhite);
        NativeSystemMessage.Drop(5, "你不能再往前了。");
        NativeSystemMessage.Drop(5, "[系统] 客户端上次闪退可能是地图【小蘑菇】不存在或异常，现已将你转移到【小蘑菇】。");
        yield return new WaitForSecondsRealtime(0.5f);
        Shot("bar_5_sysmsg.png");
        yield return new WaitForSecondsRealtime(0.6f);

        Debug.Log("[BottomBarReview] PASS");
        EditorApplication.Exit(0);
    }
}
