using UnityEditor;
using UnityEngine;

// 底栏两个菜单（目录 / 界面）与三个设置窗的无渲染断言。
// 截图脚本要真机出图，batchmode 下不渲染；几何和状态机这两类问题用这里挡。
//   tools/unity-compile-check 同目录先编译，再跑：
//   Unity -batchmode -nographics -projectPath MapleDash \
//     -executeMethod MenuLayoutChecks.Run -logFile /tmp/menu-checks.log
public static class MenuLayoutChecks
{
    static int passed, failed;
    static void Check(bool ok, string message)
    {
        if (ok) passed++;
        else { failed++; Debug.LogError("[MenuCheck] " + message); }
    }

    static bool Near(float a, float b, float eps = 0.6f) { return Mathf.Abs(a - b) <= eps; }

    public static void Run()
    {
        passed = failed = 0;
        int invite = PlayerPrefs.GetInt("client.invite", (1 << ClientSettings.InviteCount) - 1);
        int hp = PlayerPrefs.GetInt("client.hpalarm", 0);
        int mp = PlayerPrefs.GetInt("client.mpalarm", 0);
        int fx = PlayerPrefs.GetInt("client.screenfx", 1);
        try
        {
            CheckGeometry(BottomMenu.Catalog);
            CheckGeometry(BottomMenu.Interface);
            CheckMutualExclusion();
            CheckDisabledItems();
            CheckWindows();
            CheckSettings();
            CheckAudioMute();
        }
        finally
        {
            PlayerPrefs.SetInt("client.invite", invite);
            PlayerPrefs.SetInt("client.hpalarm", hp);
            PlayerPrefs.SetInt("client.mpalarm", mp);
            PlayerPrefs.SetInt("client.screenfx", fx);
            PlayerPrefs.Save();
            BottomMenu.Close();
            GameOptWindow.Close();
            SysOptWindow.Close();
            ChannelWindow.Close();
        }
        Debug.Log("[MenuCheck] 通过 " + passed + " 项，失败 " + failed + " 项");
        if (Application.isBatchMode) EditorApplication.Exit(failed == 0 ? 0 : 1);
    }

    // ——— 几何：全部对源端 IMG 尺寸（面板 93 宽 / 按钮 81×25 / 行距 26 或 21） ———
    static void CheckGeometry(int kind)
    {
        string tag = kind == BottomMenu.Catalog ? "目录" : "界面";
        float s = BottomBar.S;
        Check(s > 0.05f, tag + " 缩放系数有效（S=" + s + "）");

        int count = BottomMenu.ItemCount(kind);
        int height = kind == BottomMenu.Catalog ? 140 : 245;
        int pitch = kind == BottomMenu.Catalog ? 26 : 21;
        Check(count == (kind == BottomMenu.Catalog ? 4 : 11), tag + " 条目数 " + count);

        var panel = BottomMenu.PanelRect(kind);
        Check(Near(panel.width, 93f * s), tag + " 面板宽 93（实际 " + panel.width + "）");
        Check(Near(panel.height, height * s), tag + " 面板高 " + height + "（实际 " + panel.height + "）");

        // 底边贴大按钮顶边、右缘贴大按钮右缘 —— 只要没被夹取就必须严格相等
        var anchor = BottomBar.BigButtonRect(kind == BottomMenu.Catalog ? 2 : 3);
        Check(panel.yMax <= anchor.y + 0.6f, tag + " 面板不压底栏（面板底 " + panel.yMax + " ≤ 按钮顶 " + anchor.y + "）");
        if (anchor.y - height * s > 0f) Check(Near(panel.yMax, anchor.y), tag + " 底边贴按钮顶边");
        Check(panel.xMax <= anchor.xMax + 0.6f, tag + " 面板不超出大按钮右缘");
        if (anchor.xMax - 93f * s > 0f) Check(Near(panel.xMax, anchor.xMax), tag + " 右缘贴大按钮右缘");

        // 面板必须在可视区内（窄屏/竖屏时会被夹回来）
        if (UiKit.W > 0f && UiKit.H > 0f)
        {
            Check(panel.x >= -0.6f && panel.xMax <= UiKit.W + 0.6f, tag + " 面板在可视区横向内");
            Check(panel.y >= -0.6f && panel.yMax <= UiKit.H + 0.6f, tag + " 面板在可视区纵向内");
        }

        // 源端控件坐标：目录首行 y=24（让开底图印死的「主菜单」标题），界面首行 y=5
        var first = BottomMenu.ItemRect(kind, 0);
        Check(Near(first.x, panel.x + 6f * s), tag + " 首列左缩进 6");
        Check(Near(first.y, panel.y + (kind == BottomMenu.Catalog ? 24f : 5f) * s), tag + " 首行上缩进");

        var prev = first;
        for (int i = 0; i < count; i++)
        {
            var r = BottomMenu.ItemRect(kind, i);
            Check(Near(r.width, 81f * s), tag + " 项" + i + " 宽 81（实际 " + r.width + "）");
            Check(Near(r.height, 25f * s), tag + " 项" + i + " 高 25（实际 " + r.height + "）");
            Check(r.x >= panel.x && r.xMax <= panel.xMax + 0.6f, tag + " 项" + i + " 横向在面板内");
            Check(r.y >= panel.y && r.yMax <= panel.yMax + 0.6f, tag + " 项" + i + " 纵向在面板内（底 " + r.yMax + " ≤ " + panel.yMax + "）");
            if (i > 0) Check(Near(r.y - prev.y, pitch * s), tag + " 项" + i + " 行距 " + pitch);
            prev = r;
        }
    }

    // ——— 互斥：同时只能有一个菜单；同一下标再点一次收起 ———
    static void CheckMutualExclusion()
    {
        BottomMenu.Close();
        Check(!BottomMenu.AnyOpen, "初始无菜单打开");

        GameMenuWindow.Toggle();
        Check(BottomMenu.IsOpen(BottomMenu.Catalog) && GameMenuWindow.Visible, "点目录 → 目录打开");
        Check(!ShortcutWindow.Visible, "目录打开时界面关闭（互斥）");

        ShortcutWindow.Toggle();
        Check(BottomMenu.IsOpen(BottomMenu.Interface) && ShortcutWindow.Visible, "目录开着点界面 → 界面打开");
        Check(!GameMenuWindow.Visible, "界面打开时目录自动收起（互斥）");

        GameMenuWindow.Toggle();
        Check(BottomMenu.IsOpen(BottomMenu.Catalog), "界面开着点目录 → 目录打开");
        Check(!ShortcutWindow.Visible, "目录打开时界面收起（互斥）");

        GameMenuWindow.Toggle();
        Check(!BottomMenu.AnyOpen, "再点一次目录 → 收起");
        GameMenuWindow.Toggle();
        BottomMenu.Close();
        Check(!GameMenuWindow.Visible && !ShortcutWindow.Visible, "Close 后两个菜单都关");

        ShortcutWindow.Visible = true;
        Check(BottomBar.InterfaceVisible, "旧字段 InterfaceVisible 跟随界面菜单");
        ShortcutWindow.Visible = false;
        Check(!BottomBar.InterfaceVisible, "旧字段 InterfaceVisible 关闭后为假");
    }

    // ——— 禁灰：只禁源端确实没实现的项 ———
    static void CheckDisabledItems()
    {
        // 目录 4 项源端全可用；「场景变更（BtSkin）」源端没有这一项（死素材），列表里也不该出现
        for (int i = 0; i < BottomMenu.ItemCount(BottomMenu.Catalog); i++)
            Check(BottomMenu.ItemEnabled(BottomMenu.Catalog, i), "目录项" + i + " 可用");
        Check(BottomMenu.NameOf(BottomMenu.Catalog, 0) == "频道变更", "目录首项是频道变更");
        Check(BottomMenu.NameOf(BottomMenu.Catalog, 1) == "游戏设置", "目录第 2 项是游戏设置（源端无场景变更）");
        Check(BottomMenu.NameOf(BottomMenu.Catalog, 3) == "游戏结束", "目录末项是游戏结束");
        Check(!BottomMenu.ItemEnabled(BottomMenu.Interface, 10), "界面「我的排行」禁灰");
        for (int i = 0; i < 10; i++) Check(BottomMenu.ItemEnabled(BottomMenu.Interface, i), "界面项" + i + " 可用");
        for (int i = 0; i < BottomMenu.ItemCount(BottomMenu.Catalog); i++)
            Check(BottomMenu.NameOf(BottomMenu.Catalog, i) != "?", "目录项" + i + " 有名字");
        for (int i = 0; i < BottomMenu.ItemCount(BottomMenu.Interface); i++)
            Check(BottomMenu.NameOf(BottomMenu.Interface, i) != "?", "界面项" + i + " 有名字");
    }

    // ——— 三个设置窗接入统一的可见性 / Esc 关闭栈 ———
    static void CheckWindows()
    {
        GameOptWindow.Visible = true;
        Check(GameOptWindow.Visible && GameWindows.AnyVisible, "游戏设置窗打开后计入 AnyVisible");
        GameWindows.TrackPanels();
        Check(GameWindows.CloseTop(), "Esc 能关掉游戏设置窗");
        Check(!GameOptWindow.Visible, "游戏设置窗已关闭");

        SysOptWindow.Visible = true;
        Check(SysOptWindow.Visible && GameWindows.AnyVisible, "系统设置窗打开后计入 AnyVisible");
        GameWindows.TrackPanels();
        GameWindows.CloseTop();
        Check(!SysOptWindow.Visible, "系统设置窗已关闭");

        ChannelWindow.Visible = true;
        Check(ChannelWindow.Visible && GameWindows.AnyVisible, "频道窗打开后计入 AnyVisible");
        GameWindows.TrackPanels();
        GameWindows.CloseTop();
        Check(!ChannelWindow.Visible, "频道窗已关闭");

        // 后开的先关
        GameOptWindow.Visible = true;
        ChannelWindow.Visible = true;
        GameWindows.TrackPanels();
        Check(GameWindows.CloseTop() && !ChannelWindow.Visible && GameOptWindow.Visible, "Esc 先关后开的频道窗");
        GameWindows.CloseTop();
        Check(!GameOptWindow.Visible, "再按 Esc 关掉游戏设置窗");
    }

    // ——— 设置存档：12 位邀请位图 + 警报阈值夹取 ———
    static void CheckSettings()
    {
        Check(ClientSettings.InviteCount == 12, "邀请项 12 行");
        Check(ClientSettings.InviteNames.Length == 12, "邀请项文案 12 条");

        ClientSettings.SetInvite(3, false);
        Check(!ClientSettings.Invite(3), "关闭第 4 项生效");
        Check(ClientSettings.Invite(0) && ClientSettings.Invite(11), "其它位不受影响");
        ClientSettings.SetInvite(3, true);
        Check(ClientSettings.Invite(3), "再打开第 4 项生效");

        ClientSettings.HpAlarm = 150;
        Check(ClientSettings.HpAlarm == 100, "HP 警报上限夹到 100");
        ClientSettings.HpAlarm = -5;
        Check(ClientSettings.HpAlarm == 0, "HP 警报下限夹到 0");
        ClientSettings.MpAlarm = 30;
        Check(ClientSettings.MpAlarm == 30, "MP 警报保存");

        ClientSettings.ScreenEffects = false;
        Check(!ClientSettings.ScreenEffects, "画面效果关掉后读回 false");
        ClientSettings.ScreenEffects = true;
        Check(ClientSettings.ScreenEffects, "画面效果打开后读回 true");
    }

    // ——— 音频：静音是乘 0，不能把用户设的音量冲掉 ———
    static void CheckAudioMute()
    {
        float bgm = AudioManager.BgmVolume, sfx = AudioManager.SfxVolume;
        bool bgmMute = AudioManager.BgmMuted, sfxMute = AudioManager.SfxMuted;
        try
        {
            AudioManager.BgmVolume = 0.42f;
            AudioManager.BgmMuted = true;
            Check(AudioManager.BgmMuted, "BGM 静音状态读回");
            Check(Near(AudioManager.BgmVolume, 0.42f, 0.02f), "静音不改写 BGM 音量设定");
            AudioManager.BgmMuted = false;
            Check(Near(AudioManager.BgmVolume, 0.42f, 0.02f), "取消静音回到原音量");

            AudioManager.SfxMuted = true;
            Check(AudioManager.SfxMuted, "音效静音状态读回");
            AudioManager.SfxMuted = false;
            Check(!AudioManager.SfxMuted, "取消静音生效");

            AudioManager.BgmVolume = 3f;
            Check(Near(AudioManager.BgmVolume, 1f, 0.02f), "BGM 音量夹取到 1");
            AudioManager.SfxVolume = -1f;
            Check(Near(AudioManager.SfxVolume, 0f, 0.02f), "音效音量夹取到 0");
        }
        finally
        {
            AudioManager.BgmVolume = bgm;
            AudioManager.SfxVolume = sfx;
            AudioManager.BgmMuted = bgmMute;
            AudioManager.SfxMuted = sfxMute;
            PlayerPrefs.Save();
        }
    }
}
