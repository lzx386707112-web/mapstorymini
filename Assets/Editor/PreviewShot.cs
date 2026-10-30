using System;
using System.Globalization;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// 真实数据预览截图（2026-10-29 用户要求）：
//   「以后你的产品效果，最好按实际的来，比如装备的预览，刚看是没装备显示的，
//     看不到实际效果，没办法判断是否还存在问题」
// 所以这个脚本不摆 mock、不摆空装备：它用真实存档 + 真实 WZ 数据把角色穿满一套、
// 打开原版窗口、把指针钉在真实格子上，然后抓「含 IMGUI」的画面。
//
// 两条抓图路径，各有用途：
//   Shot（ScreenCapture）—— 含 OnGUI，HUD/悬浮框只能用这条；编辑器里是异步的，
//                            低帧率时会滞后一两秒，所以每个场景都要先摆好再等 3 秒。
//   ShotRt（RenderTexture）—— 不含 IMGUI，但同一帧同步出图，用来抓会动的升级特效。
//
// 命令行（不能加 -batchmode，否则没有图形设备，截出来是黑的）：
//   /Applications/Unity/Unity.app/Contents/MacOS/Unity -projectPath <proj> \
//     -executeMethod PreviewShot.Run -previewShots /tmp/mapledash-preview \
//     -previewWidth 1280 -previewHeight 720 -logFile /tmp/preview_shot.log
//
// 存档在开跑前快照、结束时原样写回，不会污染玩家数据。
public static class PreviewShot
{
    const string K_RUN = "PreviewShot.running";
    const string K_DIR = "PreviewShot.dir";
    const string K_T0 = "PreviewShot.t0";
    const string K_PHASE = "PreviewShot.phase";
    const string K_START = "PreviewShot.start";
    const string K_WAIT = "PreviewShot.waitReady";
    const string K_NEXT = "PreviewShot.next";
    const string K_SNAP = "PreviewShot.snapshot";
    const string K_HAD = "PreviewShot.hadSnapshot";
    const string K_W = "PreviewShot.width";
    const string K_H = "PreviewShot.height";
    const string K_PINW = "PreviewShot.pinWindow";
    const string K_PINX = "PreviewShot.pinX";
    const string K_PINY = "PreviewShot.pinY";
    const string SnapKey = "mxd.profile.v1";

    // 「冒险岛宝石」套装（Admin/SetItemCatalog 内置 id=11000）真实 8 件。
    // 客户端只实现了 帽子/套服/鞋子/手套/披风/武器 六个槽，腰带(1132092)/肩章(1152060)
    // 故意不穿——正好让套装面板出现「未穿」的灰色行。
    const int SetHat = 1003242, SetTop = 1052357, SetShoes = 1072521;
    const int SetGlove = 1082314, SetCape = 1102294, SetWeapon = 1302169;

    // 进 Play 会触发域重载，静态订阅和字段全部被清掉；必须靠它把 Tick / 状态接回来。
    [InitializeOnLoadMethod]
    static void Hook()
    {
        if (!EditorPrefs.GetBool(K_RUN, false)) return;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= OnPlayMode;
        EditorApplication.playModeStateChanged += OnPlayMode;
    }

    public static void Run()
    {
        string dir = "/tmp/mapledash-preview";
        int width = 1280, height = 720, startPhase = 0;
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-previewShots") dir = args[i + 1];
            if (args[i] == "-previewWidth") int.TryParse(args[i + 1], out width);
            if (args[i] == "-previewHeight") int.TryParse(args[i + 1], out height);
            if (args[i] == "-previewStart") int.TryParse(args[i + 1], out startPhase);
        }
        width = Mathf.Max(800, width);
        height = Mathf.Max(600, height);

        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "report.txt"),
            "MapleDash 真实数据预览（PreviewShot）\n" +
            "时间 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n" +
            "角色穿真实套装 + 打开原版窗口 + 指针停在真实格子上，ScreenCapture 抓含 IMGUI 画面。\n\n");

        EditorPrefs.SetBool(K_HAD, PlayerPrefs.HasKey(SnapKey));
        EditorPrefs.SetString(K_SNAP, PlayerPrefs.GetString(SnapKey, ""));
        SnapshotSettings();
        EditorPrefs.SetString(K_DIR, dir);
        EditorPrefs.SetInt(K_W, width);
        EditorPrefs.SetInt(K_H, height);
        EditorPrefs.SetBool(K_RUN, true);
        EditorPrefs.SetInt(K_START, startPhase);
        EditorPrefs.SetBool(K_WAIT, startPhase > 0);
        EditorPrefs.SetInt(K_PHASE, startPhase);
        EditorPrefs.SetString(K_T0, "0");
        EditorPrefs.SetString(K_NEXT, "0");
        EditorPrefs.SetString(K_PINW, "");

        HudChecks.SetGameViewSize(width, height);

        PlayerPrefs.SetInt("autostart", 1);       // 跳过大厅直接出战
        PlayerPrefs.SetInt("hide_tuning", 1);     // 调参面板别入镜
        PlayerPrefs.SetInt("debug_god", 1);       // 别死在截图途中
        PlayerPrefs.Save();

        Note("GameView " + width + "x" + height);
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayMode;
        EditorApplication.EnterPlaymode();
    }

    static void OnPlayMode(PlayModeStateChange st)
    {
        if (st != PlayModeStateChange.EnteredPlayMode) return;
        if (!EditorPrefs.GetBool(K_RUN, false)) return;
        EditorPrefs.SetString(K_T0, EditorApplication.timeSinceStartup.ToString("R", CultureInfo.InvariantCulture));
        EditorPrefs.SetString(K_NEXT, "0");
        EditorPrefs.SetInt(K_PHASE, EditorPrefs.GetInt(K_START, 0));
    }

    static void Tick()
    {
        if (!EditorPrefs.GetBool(K_RUN, false)) return;

        double t0 = Parse(EditorPrefs.GetString(K_T0, "0"));
        if (!EditorApplication.isPlaying)
        {
            if (t0 > 0 && EditorApplication.timeSinceStartup - t0 > 150) { Note("未进入播放模式，退出"); Finish(1); }
            return;
        }
        if (t0 <= 0) return;

        Application.runInBackground = true;
        Repin();      // 每帧重算指针：BottomBar.S 在首帧还没准备好（曾算出 S=0.067）

        double now = EditorApplication.timeSinceStartup;
        double next = Parse(EditorPrefs.GetString(K_NEXT, "0"));
        int phase = EditorPrefs.GetInt(K_PHASE, 0);
        // 从中间 phase 起跑（-previewStart）时不会有 phase 0 的就绪门禁，这里补一道；
        // 只等一次，之后各 phase 期间 GameLoop 状态可能被窗口改掉，不能再卡。
        if (EditorPrefs.GetBool(K_WAIT, false))
        {
            if (!Ready()) { if (now - t0 > 120) { Note("等待游戏就绪超时"); Finish(1); } return; }
            EditorPrefs.SetBool(K_WAIT, false);
        }
        if (phase > 0 && now < next) return;

        switch (phase)
        {
            case 0:
                if (!Ready()) { if (now - t0 > 120) { Note("等待游戏就绪超时"); Finish(1); } return; }
                SetupEquip();
                Step(now + 3.0, 1);
                break;
            case 1:
                Shot("01-equip-tooltip.png");
                Step(now + 1.0, 2);
                break;
            case 2:
                SetupItem();
                Step(now + 3.0, 3);
                break;
            case 3:
                Shot("02-item-tooltip.png");
                Step(now + 1.0, 4);
                break;
            case 4:
                SetupSkill();
                Step(now + 3.0, 5);
                break;
            case 5:
                Shot("03-skill-tooltip.png");
                Step(now + 1.0, 6);
                break;
            case 6:
                SetupLevelUp();
                Step(now + 0.15, 7);
                break;
            case 7: case 8: case 9: case 10: case 11: case 12: case 13: case 14:
                ShotRt("04-levelup-" + (phase - 7) + ".png");
                Step(now + 0.12, phase + 1);
                break;
            case 15:
                ShotRt("04-levelup-8.png");
                Step(now + 0.5, 16);
                break;
            case 16:
                ShotRt("04-levelup-end.png");
                Step(now + 0.4, 17);
                break;
            case 17:
                SetupStar();
                Step(now + 3.0, 18);
                break;
            case 18:
                Shot("05-star-dialog.png");
                Step(now + 1.0, 19);
                break;
            case 19:
                StarEnhance();
                Step(now + 2.5, 20);
                break;
            case 20:
                Shot("06-star-result.png");
                Step(now + 1.0, 21);
                break;
            case 21:
                SetupStarTooltip();
                Step(now + 3.0, 22);
                break;
            case 22:
                Shot("07-star-tooltip.png");
                Step(now + 2.0, 23);      // ScreenCapture 是异步的，收尾前必须留时间落盘
                break;
            case 23:
                SetupNoNpcDialog();
                Step(now + 3.0, 24);
                break;
            case 24:
                Shot("08-no-npc-dialog.png");
                Step(now + 1.0, 25);
                break;
            case 25:
                SetupOptionMenu();
                Step(now + 3.0, 26);
                break;
            case 26:
                Note("拍图前当前项=" + (NpcSession.Active == null ? -99 : NpcSession.Active.Dialog.SelectedLink));
                Shot("09-option-menu-idle.png");   // 未选中：全部浅灰点
                Step(now + 3.0, 27);               // ScreenCapture 异步落盘，期间不能改状态
                break;
            case 27:
                SelectOption(2);
                Step(now + 3.0, 28);
                break;
            case 28:
                Note("拍图前当前项=" + (NpcSession.Active == null ? -99 : NpcSession.Active.Dialog.SelectedLink));
                Shot("10-option-menu-selected.png");   // 只有这一行是红三角
                Step(now + 2.5, 29);
                break;
            case 29:
                Finish(0);
                break;

            // ── 底部菜单 / 设置窗（-previewStart 30） ──
            case 30:
                if (!Ready()) { if (now - t0 > 120) { Note("等待游戏就绪超时"); Finish(1); } return; }
                SetupCatalog();
                Step(now + 2.5, 31);
                break;
            case 31:
                PinMenu(0, 2);
                Step(now + 1.2, 32);
                break;
            case 32:
                Shot("11-menu-catalog.png");
                Step(now + 3.0, 33);
                break;
            case 33:
                OpenInterface();
                Step(now + 2.0, 34);
                break;
            case 34:
                PinMenu(1, 4);
                Step(now + 1.2, 35);
                break;
            case 35:
                Shot("12-menu-interface.png");
                Step(now + 3.0, 36);
                break;
            case 36:
                SetupGameOpt();
                Step(now + 2.5, 37);
                break;
            case 37:
                Shot("13-game-options.png");
                Step(now + 3.0, 38);
                break;
            case 38:
                SetupSysOpt();
                Step(now + 2.5, 39);
                break;
            case 39:
                Shot("14-system-options.png");
                Step(now + 3.0, 40);
                break;
            case 40:
                SetupChannel();
                Step(now + 2.5, 41);
                break;
            case 41:
                Shot("15-channel.png");
                Step(now + 3.0, 42);
                break;
            case 42:
                Note("菜单截图收尾：Open=" + BottomMenu.Open + "（-1 表示已收起），设置窗 " +
                     GameOptWindow.Visible + "/" + SysOptWindow.Visible + "/" + ChannelWindow.Visible);
                Finish(0);
                break;

            // ── 怪物手册（-previewStart 43 单独跑这一段） ──
            case 43:
                if (!Ready()) { if (now - t0 > 120) { Note("等待游戏就绪超时"); Finish(1); } return; }
                SetupBook();
                Step(now + 3.0, 44);
                break;
            case 44:
                Shot("16-book-info.png");            // 开窗默认页 = 手册信息（infoPage）
                Step(now + 3.0, 45);
                break;
            case 45:
                SetupBookCards();
                Step(now + 2.5, 46);
                break;
            case 46:
                Shot("17-book-cards.png");           // 25 格卡槽 + 两对箭头 + 顶部搜索框
                Step(now + 2.5, 47);
                break;
            case 47:
                SetupBookDrops();
                Step(now + 2.5, 48);
                break;
            case 48:
                Shot("18-book-drops.png");           // 右页 4x5 战利品网格
                Step(now + 2.5, 49);
                break;
            case 49:
                Finish(0);
                break;

            // ── 家族面板（-previewStart 50 单独跑这一段） ──
            case 50:
                if (!Ready()) { if (now - t0 > 120) { Note("等待游戏就绪超时"); Finish(1); } return; }
                SetupGuild();
                Step(now + 3.0, 51);
                break;
            case 51:
                Shot("19-guild-empty.png");          // 未入家族：源端字符串 350「没有参加的家族」居中
                Step(now + 2.0, 52);
                break;
            case 52:
                SeedGuild();
                Step(now + 2.5, 53);
                break;
            case 53:
                Shot("20-guild-list.png");           // 组标题 / 列头 / 名字|职业|等级|地位 / 选中行
                Step(now + 2.5, 54);
                break;
            case 54:
                GuildWhere();
                Step(now + 2.5, 55);
                break;
            case 55:
                Shot("21-guild-where.png");          // 聊天窗多一行「XXX 目前在 <地图名>」
                Step(now + 1.5, 56);
                break;
            case 56:
                NativeGuildGradeWindow.Toggle();
                Step(now + 3.0, 57);
                break;
            case 57:
                Shot("22-guild-grade.png");          // BtInfo 的资料子窗（233×304）
                Step(now + 2.0, 58);
                break;
            case 58:
                Finish(0);
                break;

            // ── 能力值洗点窗 5050000（-previewStart 60 单独跑这一段） ──
            // 三页都要拍：来源页(backgrnd/▼) → 目标页(backgrnd2/▲) → 确认页(backgrnd3)。
            case 60:
                if (!Ready()) { if (now - t0 > 120) { Note("等待游戏就绪超时"); Finish(1); } return; }
                if (Mathf.Abs(UiKit.W - 1280f) > 8f)
                {
                    if (now - t0 > 120) { Note("等待画布尺寸超时：UiKit=" + UiKit.W + "x" + UiKit.H); Finish(1); }
                    else Step(now + 0.5, 60);
                    return;
                }
                ClearWindows();
                Profile.Data.AddItem(5050000, 1);
                Note("种子 5050000 数量=" + Profile.ItemCount(5050000));
                NativeResetWindow.Open(5050000, 0);
                Step(now + 0.8, 61);
                break;
            case 61:
                // 指针压在「力量」行的下箭头（x=153..165、y=116..128）：验证 BtDown 的 mouseOver 皮肤
                Pin(typeof(NativeResetWindow), 159f, 122f);
                Shot("36-reset-ap-from.png");
                Step(now + 3.0, 62);
                break;
            case 62:
                SetResetStage(2, 64, 0);
                Pin(typeof(NativeResetWindow), 159f, 140f);   // 敏捷行（y=134）的上箭头
                Shot("37-reset-ap-to.png");
                Step(now + 3.0, 63);
                break;
            case 63:
                SetResetStage(3, 64, 128);
                Shot("38-reset-ap-confirm.png");
                Step(now + 3.0, 64);
                break;
            case 64:
            {
                // 真实结算：STR 64 → DEX 128 各一点；AP 总数不变
                var before = Profile.Attributes;
                int apBefore = Profile.ap, beforeItems = Profile.ItemCount(5050000);
                string error = Profile.ResetAp(64, 128);
                var after = Profile.Attributes;
                Note("洗点结算 error=" + (error ?? "null") + " STR " + before.str + "→" + after.str +
                     " DEX " + before.dex + "→" + after.dex + " AP " + apBefore + "→" + Profile.ap +
                     " 5050000 " + beforeItems + "→" + Profile.ItemCount(5050000));
                Step(now + 1.0, 65);
                break;
            }
            case 65:
                Finish(0);
                break;

            // ── 底栏菜单第二轮：把控件调到「非默认值」再拍（-previewStart 90） ──
            //    默认值看不出问题：邀请全是「同意」、滑块全在中位、静音全关。
            case 90:
                if (!Ready()) { if (now - t0 > 120) { Note("等待游戏就绪超时"); Finish(1); } return; }
                // 等 GameView 真正切到 1280×720 再摆场景：早几帧画布还是旧尺寸（曾出现 693×720），
                // 那时算出来的菜单几何和最终画面差一倍，指针会钉到面板外。
                if (Mathf.Abs(UiKit.W - 1280f) > 8f)
                {
                    if (now - t0 > 120) { Note("等待画布尺寸超时：UiKit=" + UiKit.W + "x" + UiKit.H); Finish(1); }
                    else Step(now + 0.5, 90);
                    return;
                }
                ClearWindows();
                BottomMenu.Show(BottomMenu.Catalog);
                Step(now + 2.0, 91);
                break;
            case 91:
                Step(now + 1.2, 92);
                break;
            case 92:
                // 钉指针放到拍照同一步：Tick 与 OnGUI 拿到的度量不一定同步，
                // 隔两秒再拍会让 hover 落空（上一轮就钉偏到了面板外）。
                PinMenu(0, 1);                        // 1 = 游戏设置（正常项，验证 mouseOver 皮肤）
                Shot("34-menu-item-hover.png");
                Step(now + 2.0, 93);
                break;
            case 93:
                // 目录里已经没有「场景变更」了（源端没这一项，是死素材）；禁灰项只剩界面→我的排行，
                // 拍这张证「指针压在禁用项上也保持灰」。
                BottomMenu.Show(BottomMenu.Interface);
                PinMenu(1, 10);
                Shot("35-menu-disabled-hover.png");
                Step(now + 2.0, 94);
                break;
            case 94:
                SetupGameOptMixed();
                Step(now + 2.5, 95);
                break;
            case 95:
                Shot("31-game-options-mixed.png");    // 同意/拒绝 交替，验证抹字对齐
                Step(now + 2.0, 96);
                break;
            case 96:
                SetupSysOptAdjusted();
                Step(now + 2.5, 97);
                break;
            case 97:
                Shot("32-system-options-adjusted.png");   // 滑块拉偏 + 静音勾选 + 警报 30/20
                Step(now + 2.0, 98);
                break;
            case 98:
                SetupChannel();
                Step(now + 2.5, 99);
                break;
            case 99:
                Shot("33-channel-row.png");           // 列表区 CH.1 条目 + 变更恒灰
                Step(now + 2.0, 100);
                break;
            case 100:
                RestoreSettings();
                Note("设置存档已还原（audio.bgm/sfx/mute、client.invite/hpalarm/mpalarm/screenfx）");
                Finish(0);
                break;

            default:
                Finish(0);
                break;
        }
    }

    // ───────── 公共：把所有窗关掉（各场景起手都先回到干净状态） ─────────
    static void ClearWindows()
    {
        NpcSession.CloseActive();
        EquipWindow.Visible = false;
        InvenWindow.Visible = false;
        SkillWindow.Visible = false;
        GameOptWindow.Close();
        SysOptWindow.Close();
        ChannelWindow.Close();
    }

    // ───────── 场景 11/12：底部菜单「目录」「界面」 ─────────
    static void SetupCatalog()
    {
        ClearWindows();
        GameMenuWindow.Toggle();
        Pointer.TestPos = new Vector2(1100f, 40f);
        Note("目录菜单：Open=" + BottomMenu.Open + "（0=目录），项数=" + BottomMenu.ItemCount(0) +
             "，S=" + BottomBar.S.ToString("0.000"));
        DumpMenu(0);
    }

    static void OpenInterface()
    {
        ShortcutWindow.Toggle();
        Pointer.TestPos = new Vector2(1100f, 40f);
        Note("互斥验证：点「界面」后 Open=" + BottomMenu.Open + "（1=界面），目录=" + BottomMenu.IsOpen(0) +
             " 界面=" + BottomMenu.IsOpen(1));
        DumpMenu(1);
    }

    static void DumpMenu(int kind)
    {
        var panel = BottomMenu.PanelRect(kind);
        Note("  面板 x=" + panel.x.ToString("0.0") + " y=" + panel.y.ToString("0.0") + " " +
             panel.width.ToString("0.0") + "x" + panel.height.ToString("0.0") +
             "，可视区 " + UiKit.W + "x" + UiKit.H);
        for (int i = 0; i < BottomMenu.ItemCount(kind); i++)
        {
            var r = BottomMenu.ItemRect(kind, i);
            Note("  项" + i + " " + BottomMenu.NameOf(kind, i) + " x=" + r.x.ToString("0.0") +
                 " y=" + r.y.ToString("0.0") + " " + r.width.ToString("0.0") + "x" + r.height.ToString("0.0") +
                 " 可用=" + BottomMenu.ItemEnabled(kind, i));
        }
    }

    static void PinMenu(int kind, int index)
    {
        // UiKit 的度量只在 OnGUI 里刷新；本方法跑在 editor update 里，不刷就会拿到上一帧的
        // Scale，BottomBar.S 随之偏掉（实测偏 8%），指针钉到面板外还以为是 hover 没生效。
        UiKit.RefreshMetrics();
        var r = BottomMenu.ItemRect(kind, index);
        ClearPin();
        Pointer.TestPos = new Vector2(r.center.x, r.center.y);
        // Screen/UiKit 在 OnGUI 之外读到的值不一定和绘制时一致，把原始数一起打出来，
        // 钉歪时能一眼看出是哪一项（曾经因此把指针钉到面板外，误以为 hover 皮肤没问题）。
        Note("指针钉在「" + BottomMenu.NameOf(kind, index) + "」中心 " + Pointer.TestPos +
             "（S=" + BottomBar.S.ToString("0.000") + " Screen=" + Screen.width + "x" + Screen.height +
             " UiKit=" + UiKit.W.ToString("0") + "x" + UiKit.H.ToString("0") +
             " scale=" + UiKit.Scale.ToString("0.000") + "）");
    }

    // ───────── 场景 13/14/15：游戏设置 / 系统设置 / 频道 ─────────
    static void SetupGameOpt()
    {
        ClearWindows();
        BottomMenu.Close();
        GameOptWindow.Visible = true;
        Pointer.TestPos = new Vector2(1100f, 40f);
        var on = new System.Text.StringBuilder();
        for (int i = 0; i < 12; i++) on.Append(ClientSettings.Invite(i) ? '1' : '0');
        Note("游戏设置窗 158x284：邀请权限 12 位=" + on);
    }

    static void SetupSysOpt()
    {
        ClearWindows();
        BottomMenu.Close();
        SysOptWindow.Visible = true;
        Pointer.TestPos = new Vector2(1100f, 40f);
        Note("系统设置窗 299x365：BGM " + AudioManager.BgmVolume.ToString("0.00") + "（静音 " + AudioManager.BgmMuted +
             "），音效 " + AudioManager.SfxVolume.ToString("0.00") + "（静音 " + AudioManager.SfxMuted +
             "），HP 警报 " + ClientSettings.HpAlarm + "，MP 警报 " + ClientSettings.MpAlarm +
             "，画面效果 " + ClientSettings.ScreenEffects);
    }

    static void SetupChannel()
    {
        ClearWindows();
        BottomMenu.Close();
        ChannelWindow.Visible = true;
        Pointer.TestPos = new Vector2(1100f, 40f);
        Note("频道窗 436x143（t 67 / c 4×10 / s 36），本地单频道 → 变更按钮恒 disabled");
    }

    // ───────── 第二轮：非默认值（默认值看不出抹字/滑块/勾选的对齐问题） ─────────
    static void SetupGameOptMixed()
    {
        ClearWindows();
        BottomMenu.Close();
        // 隔一个翻成「拒绝」，让 12 行出现交替状态：能直接看出抹字有没有抹干净、
        // 状态字是不是压在底图印死的旧字上。
        for (int i = 0; i < ClientSettings.InviteCount; i++)
            ClientSettings.SetInvite(i, i % 2 == 0);
        GameOptWindow.Visible = true;
        Pointer.TestPos = new Vector2(1100f, 40f);
        var on = new System.Text.StringBuilder();
        for (int i = 0; i < ClientSettings.InviteCount; i++) on.Append(ClientSettings.Invite(i) ? '1' : '0');
        Note("游戏设置（交替）：邀请权限 12 位=" + on);
    }

    static void SetupSysOptAdjusted()
    {
        ClearWindows();
        BottomMenu.Close();
        AudioManager.BgmVolume = 0.25f;
        AudioManager.SfxVolume = 0.85f;
        AudioManager.BgmMuted = true;
        AudioManager.SfxMuted = false;
        ClientSettings.HpAlarm = 30;
        ClientSettings.MpAlarm = 20;
        ClientSettings.ScreenEffects = false;
        SysOptWindow.Visible = true;
        Pointer.TestPos = new Vector2(1100f, 40f);
        Note("系统设置（非默认）：BGM 0.25 静音、音效 0.85、HP 30%、MP 20%、画面效果关");
    }

    // 实拍会改「游戏设置 / 系统设置 / 音量」的存档；开跑前记一份，收尾原样写回。
    // 全是 PlayerPrefs 键，但类型不统一（音量 float、其余 int），所以按类型逐个记。
    static readonly string[] FloatKeys = { "audio.bgm", "audio.sfx" };
    static readonly string[] IntKeys =
    {
        "audio.bgm.mute", "audio.sfx.mute",
        "client.invite", "client.hpalarm", "client.mpalarm", "client.screenfx",
    };
    const string K_SETTINGS = "PreviewShot.settings";

    static void SnapshotSettings()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var k in FloatKeys)
            sb.Append("F\u0001").Append(k).Append('\u0001')
              .Append(PlayerPrefs.HasKey(k) ? PlayerPrefs.GetFloat(k).ToString("R") : "\u0002").Append('\n');
        foreach (var k in IntKeys)
            sb.Append("I\u0001").Append(k).Append('\u0001')
              .Append(PlayerPrefs.HasKey(k) ? PlayerPrefs.GetInt(k).ToString() : "\u0002").Append('\n');
        EditorPrefs.SetString(K_SETTINGS, sb.ToString());
    }

    static void RestoreSettings()
    {
        string raw = EditorPrefs.GetString(K_SETTINGS, "");
        if (string.IsNullOrEmpty(raw)) return;
        foreach (var line in raw.Split('\n'))
        {
            var p = line.Split('\u0001');
            if (p.Length < 3) continue;
            string key = p[1], val = p[2];
            if (val == "\u0002") { PlayerPrefs.DeleteKey(key); continue; }   // 原来没有这个键
            if (p[0] == "F")
                PlayerPrefs.SetFloat(key, float.Parse(val, CultureInfo.InvariantCulture));
            else
                PlayerPrefs.SetInt(key, int.Parse(val, CultureInfo.InvariantCulture));
        }
        PlayerPrefs.Save();
        EditorPrefs.SetString(K_SETTINGS, "");
    }

    // ───────── 场景 16/17/18：怪物手册 ─────────
    // 手册的 ids / state / page / rightTab / selected 都是私有静态字段，实拍用反射直取（不改产品代码）
    static object BookField(string name)
        => typeof(NativeMonsterBook).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);

    static void BookSet(string name, object value)
        => typeof(NativeMonsterBook).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, value);

    static void SetupBook()
    {
        ClearWindows();
        BottomMenu.Close();
        if (!NativeMonsterBook.Visible) NativeMonsterBook.Toggle();

        var ids = BookField("ids") as List<int>;
        int added = 0;
        if (ids != null) for (int i = 0; i < ids.Count && i < 40; i++)
            if (NativeMonsterBook.Collect(ids[i], 1 + i % 5)) added++;
        Note("手册卡表 " + (ids?.Count ?? -1) + " 张，本次登记 " + added + " 张");

        // 重开一次让 page 回到 9（手册信息页，原端开窗默认页）
        NativeMonsterBook.Visible = false;
        NativeMonsterBook.Toggle();
        BookSet("rightTab", 0);
        Pointer.TestPos = new Vector2(1180f, 40f);

        var state = BookField("state") as SocialResponse;
        Note("手册信息页：page=" + BookField("page") + " 等级=" + (state?.bookLevel ?? -1) +
             " 已收录=" + (state?.cards.Count ?? -1) + " 封面=" + (state?.bookCover ?? -1) +
             " S=" + BottomBar.S.ToString("0.000"));
    }

    static void SetupBookCards()
    {
        var state = BookField("state") as SocialResponse;
        var ids = BookField("ids") as List<int>;
        if (state != null && ids != null)
            for (int i = 0; i < ids.Count; i++)
                if (state.cards.ContainsKey(ids[i])) { BookSet("selected", ids[i]); BookSet("page", i / 25); break; }
        BookSet("rightTab", 0);
        Pointer.TestPos = new Vector2(1180f, 40f);
        Note("卡槽页：page=" + BookField("page") + " selected=" + BookField("selected"));
    }

    static void SetupBookDrops()
    {
        BookSet("rightTab", 2);          // 0 基本情报 / 1 怪物情报 / 2 战利品 / 3 出现地图
        Pointer.TestPos = new Vector2(1180f, 40f);
        Note("右页战利品：rightTab=" + BookField("rightTab"));
    }

    static bool Ready() => GameLoop.I != null && GameLoop.I.player != null && GameLoop.I.state == GameLoop.GameState.Playing;

    // ───────── 场景 1：装备悬浮框（真实套装） ─────────
    static void SetupEquip()
    {
        var d = Profile.Data;
        d.SetEquipSlot(EquipSlot.Hat, SetHat);
        d.SetEquipSlot(EquipSlot.Top, SetTop);
        d.SetEquipSlot(EquipSlot.Shoes, SetShoes);
        d.SetEquipSlot(EquipSlot.Glove, SetGlove);
        d.SetEquipSlot(EquipSlot.Cape, SetCape);
        d.SetEquipSlot(EquipSlot.Weapon, SetWeapon);

        var player = GameLoop.I.player;
        try { typeof(PlayerCtrl).GetMethod("ApplyLoadout", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player, null); }
        catch (Exception e) { Note("ApplyLoadout 反射失败：" + e.Message); }
        player.ApplyProgression();

        Note("角色 Lv." + Profile.level + " 职业 " + Profile.JobId + "（" + Profile.cls + "），HP " +
             player.hp + "/" + player.hpMax);
        Note("穿上的装备：" + SetHat + " " + ItemCatalog.Name(SetHat) + " / " +
             SetTop + " " + ItemCatalog.Name(SetTop) + " / " +
             SetShoes + " " + ItemCatalog.Name(SetShoes) + " / " +
             SetGlove + " " + ItemCatalog.Name(SetGlove) + " / " +
             SetCape + " " + ItemCatalog.Name(SetCape) + " / " +
             SetWeapon + " " + ItemCatalog.Name(SetWeapon));
        foreach (int id in new[] { SetHat, SetTop, SetShoes, SetGlove, SetCape, SetWeapon })
            Note("  存在性 " + id + " info=" + (EquipCatalog.Info(id) != null) +
                 " 图标=" + (EquipWindow.ArmorIcon(id) != null ? "有" : "无（武器在 Weapon 目录）"));

        var worn = new System.Collections.Generic.List<int> { SetHat, SetTop, SetShoes, SetGlove, SetCape, SetWeapon };
        foreach (var p in AdminSetItem.PanelsFor(SetHat, worn))
        {
            Note("套装面板：" + p.Name + " 已穿 " + p.EquippedCount + "/" + p.CompleteCount +
                 "，槽位 " + p.Slots.Count + "，档位 " + p.Tiers.Count);
            foreach (var s in p.Slots)
                Note("  槽 " + (s.Alts.Count > 0 ? s.Alts[0].SlotName : "?") + "：" +
                     string.Join(" | ", s.Alts.ConvertAll(a => a.ItemId + " " + a.Name + (a.Equipped ? "(已穿)" : "")).ToArray()));
            foreach (var t in p.Tiers) Note("  档 " + t.Required + " 件：" + Join(t));
        }

        SetWindowPos(typeof(EquipWindow), 40f, 40f);
        EquipWindow.Visible = true;
        InvenWindow.Visible = false;
        SkillWindow.Visible = false;

        // 帽子格：SmallCells 行 0 列 1 → 左 5+33=38、上 35、格 32 → 中心 (54,51)
        Pin(typeof(EquipWindow), 54f, 51f);
    }

    // ───────── 场景 2：背包物品悬浮框 ─────────
    static void SetupItem()
    {
        EquipWindow.Visible = false;
        var d = Profile.Data;
        d.AddItem(2000000, 50);      // 红色药水（消耗页）
        d.AddItem(2000001, 50);      // 橙色药水

        SetWindowPos(typeof(InvenWindow), 40f, 60f);
        // 默认停在「装备」页，不加这一句会拍到空背包（用户上次就是被空背包误导的）
        var tabField = typeof(InvenWindow).GetField("tab", BindingFlags.NonPublic | BindingFlags.Static);
        if (tabField != null) tabField.SetValue(null, 1);
        InvenWindow.Visible = true;
        SkillWindow.Visible = false;

        // 小窗第 1 格：起 (8,50)，格 32 → 中心 (24,66)
        Pin(typeof(InvenWindow), 24f, 66f);
        Note("背包消耗页物品：" + string.Join(" | ", Profile.InventoryItems(1).ConvertAll(i => i.ToString()).ToArray()));
    }

    // ───────── 场景 3：技能详情悬浮框 ─────────
    static void SetupSkill()
    {
        InvenWindow.Visible = false;
        SetWindowPos(typeof(SkillWindow), 40f, 70f);
        SkillWindow.Visible = true;

        // 第一行图标：RowX+IconOffX=8、RowY+IconOffY=100，格 32 → 中心 (24,116)
        Pin(typeof(SkillWindow), 24f, 116f);
        Note("技能窗已打开，职业=" + Profile.JobId + "，指针钉在列表第 1 行图标上");
    }

    // ───────── 场景 4：升级（源端 BasicEff/LevelUp 特效） ─────────
    static void SetupLevelUp()
    {
        SkillWindow.Visible = false;
        Pointer.TestPos = null;
        ClearPin();
        int before = Profile.Data.level;
        int gained = Profile.GrantExperience(Mathf.Max(1, Profile.ExpNeeded));
        Note("升级触发：Lv." + before + " → Lv." + Profile.Data.level +
             "（GrantExperience 返回 " + gained + "；真实链路 LevelSystem → Profile.LeveledUp → PlayerCtrl 播特效）");
    }

    // ───────── 场景 5：星之力强化（跑真实脚本 BeiDouSpecial/装备制作/星级强化.js） ─────────
    // 选 1302081（单手剑，reqLevel 120，WZ incPAD 110、tuc 7）：
    // 需求等级 120 → 脚本 getMaxStars 给 15 星上限，预置 11★ + 连败 5 次（下一次必成），
    // 强化成功后 12★，星条上能看到 12 颗金 + 3 颗灰。
    const int StarWeapon = 1302081;

    static void SetupStar()
    {
        Pointer.TestPos = null;
        ClearPin();
        EquipWindow.Visible = false;
        InvenWindow.Visible = false;
        SkillWindow.Visible = false;
        NpcSession.CloseActive();

        int cid = GameSession.CharacterId;
        if (cid <= 0) { Note("星力场景：不在游戏内，跳过"); return; }
        Profile.AddGold(20000000);   // 强化要花金币（脚本自己扣）

        // 反复实拍会累积同一把武器：先清干净，保证「装备栏第 1 格」就是这次新发的那把
        var db0 = GameSession.Server.Db;
        foreach (var old in db0.Table("inventoryitems").Select(x =>
            DbStore.Int(x, "characterid") == cid && DbStore.Int(x, "itemid") == StarWeapon))
        {
            long oid = DbStore.Long(old, "inventoryitemid");
            db0.Table("inventoryitems").Delete(x => DbStore.Long(x, "inventoryitemid") == oid);
            db0.Table("inventoryequipment").Delete(x => DbStore.Long(x, "inventoryitemid") == oid);
        }
        db0.Save();
        GameSession.Character.inventory = CharService.Load(db0, cid).inventory;

        // 1. 发一把真实武器
        var added = GameSession.Net.ChangeInventory(new InventoryChangeRequest
        {
            characterId = cid, itemId = StarWeapon, quantity = 1,
        });
        if (!added.ok) { Note("星力场景：发武器失败 " + added.error); return; }
        GameSession.Character.inventory = added.inventory;
        var w = GameSession.Character.inventory.Find(x => x.itemId == StarWeapon);
        if (w == null) { Note("星力场景：找不到刚发的武器"); return; }

        // 2. 挪到装备栏第 1 格（脚本硬编码 getInventory(EQUIP).getItem(1)）
        var db = GameSession.Server.Db;
        var row = db.Table("inventoryitems").Find(x => DbStore.Long(x, "inventoryitemid") == w.itemRowId);
        if (row == null) { Note("星力场景：DB 里找不到这行道具"); return; }
        row["position"] = 1L;
        row["inventorytype"] = 1L;
        db.Save();

        // 3. 按源端语义把装备行种成 WZ 基础值（源端 Equip 从 ItemInformationProvider 种属性），
        //    再预置 11★ / 连败 5 次 —— 下一次强化脚本判「必定成功」，出图可复现。
        var info = EquipCatalog.Info(StarWeapon);
        var patch = new EquipPatchRequest
        {
            characterId = cid, inventoryItemId = w.itemRowId,
            upgradeslots = 0,                  // validateEquip：必须用完卷轴次数
            starLevel = 11, starCount = 5, maxStar = 15,
            owner = w.owner,
            str = info.Int("incSTR"), dex = info.Int("incDEX"),
            int_ = info.Int("incINT"), luk = info.Int("incLUK"),
            hp = info.Int("incMHP"), mp = info.Int("incMMP"),
            watk = info.Int("incPAD"), matk = info.Int("incMAD"),
            wdef = info.Int("incPDD"), mdef = info.Int("incMDD"),
            acc = info.Int("incACC"), avoid = info.Int("incEVA"),
            hands = info.Int("incCraft"), speed = info.Int("incSpeed"), jump = info.Int("incJump"),
        };
        var res = GameSession.Net.UpdateEquip(patch);
        if (!res.ok) { Note("星力场景：种装备失败 " + res.error); return; }
        GameSession.Character.inventory = res.inventory;

        var seeded = GameSession.Character.inventory.Find(x => x.itemId == StarWeapon);
        Note("星力场景：武器 " + StarWeapon + " " + ItemCatalog.Name(StarWeapon) +
             " 已在装备栏第 1 格；需求等级 " + info.Int("reqLevel") + "，WZ incPAD " + info.Int("incPAD") +
             "，tuc " + info.Int("tuc") + " → 强化前 " + seeded.starLevel + "★/上限 " + seeded.maxStar +
             "，连败 " + seeded.starCount + "，watk " + seeded.watk + "，可升级次数 " + seeded.upgradeslots);
        Note("金币 " + Profile.gold);

        // 4. 跑真实 NPC 脚本。真实入口是「昨日小睡」(9900001) → npc/9900001.js 的「装备中心」
        //    → 装备中心.js 里的 cm.openNpc(9900001, "装备制作/星级强化")，npcId 一路带 9900001。
        //    这里如果传 0，会话就没有 NPC，对话框左侧不会出立绘/名字条，拍出来的不是游戏里的样子。
        var s = NpcSession.Talk(9900001, "装备制作/星级强化");
        if (s == null) { Note("星力场景：脚本没跑起来"); return; }
        Note("脚本 key = " + s.ScriptName + "，对话类型 = " + s.Dialog.MessageType);
        Note("对话内容：" + (s.Dialog.RawText ?? "").Replace("\r\n", " ⏎ "));
    }

    static void StarEnhance()
    {
        var s = NpcSession.Active;
        if (s == null || !s.Dialog.Visible) { Note("星力场景：没有可回应的对话"); return; }
        s.Respond(1, s.Dialog.MessageType, 0);      // selection 0 = 进行强化
        var after = NpcSession.Active;
        Note("强化结果对话：" + ((after == null ? "" : after.Dialog.RawText) ?? "").Replace("\r\n", " ⏎ "));
        var w = GameSession.Character.inventory.Find(x => x.itemId == StarWeapon);
        if (w != null)
            Note("强化后实例：starLevel=" + w.starLevel + " starCount=" + w.starCount +
                 " maxStar=" + w.maxStar + " watk=" + w.watk + " str=" + w.str +
                 " tuc=" + w.upgradeslots + " owner=" + w.owner);
    }

    // 对照：没有 NPC 的脚本对话（物品脚本 / portal 脚本 / Talk(0)）左侧要有「冒险岛运营员」兜底形象。
    static void SetupNoNpcDialog()
    {
        NpcSession.CloseActive();
        EquipWindow.Visible = false;
        InvenWindow.Visible = false;
        SkillWindow.Visible = false;
        Pointer.TestPos = null;
        var s = NpcSession.Talk(0, "装备制作/星级强化");
        Note("无 NPC 对照场景：Talk(0) 会话 NpcId=" + (s == null ? -1 : s.NpcId) +
             "，左侧应回落「冒险岛运营员」(" + NpcDialogUi.DefaultSpeaker + ") 立绘与名字条");
    }

    // ───────── 场景 5：选项菜单的「当前项」标记 ─────────
    // 原端只有当前选中项的行首是红三角（dot1），其余是 dot0 的浅灰点；上下键移动选中项
    // （0x9AC104/0x9AC347，两端夹取不循环）。这里用真实的「装备中心」菜单（多列 + 分区标题）。
    static void SetupOptionMenu()
    {
        NpcSession.CloseActive();
        EquipWindow.Visible = false;
        InvenWindow.Visible = false;
        SkillWindow.Visible = false;
        // 指针钉到窗口外的空白处：否则鼠标真身停在哪一行，那一行就会被悬停点亮，
        // 拍不出「默认只有首项是红三角」的初始态。
        Pointer.TestPos = new Vector2(1100f, 40f);
        var session = NpcSession.Talk(9900001, "装备中心");
        if (session == null) { Note("装备中心菜单打开失败"); return; }
        Note("选项菜单：Mode=" + session.Dialog.Mode + "，可选项=" + DialogFlow.Links(session.Dialog.Content).Count +
             "，当前项=" + session.Dialog.SelectedLink + "（默认选中首项，只有它是红三角）");
    }

    static void SelectOption(int steps)
    {
        var before = NpcSession.Active == null ? null : NpcSession.Active.Dialog;
        string pre = before == null ? "无会话" : before.SelectedLink + " / links[" +
            string.Join(",", DialogFlow.Links(before.Content).ConvertAll(x => x.ToString()).ToArray()) + "]";
        for (int i = 0; i < steps; i++) NpcDialogUi.MoveSelection(1);
        var session = NpcSession.Active;
        Note("移动前=" + pre + "，按 " + steps + " 次下键后当前项=" + (session == null ? -1 : session.Dialog.SelectedLink));
    }

    // 悬浮框要看的是「强化后的星条」：回背包装备页，指针钉第 1 格。
    static void SetupStarTooltip()
    {
        NpcSession.CloseActive();
        SetWindowPos(typeof(InvenWindow), 40f, 60f);
        var tabField = typeof(InvenWindow).GetField("tab", BindingFlags.NonPublic | BindingFlags.Static);
        if (tabField != null) tabField.SetValue(null, 0);
        InvenWindow.Visible = true;
        EquipWindow.Visible = false;
        SkillWindow.Visible = false;
        Pin(typeof(InvenWindow), 24f, 66f);
        Note("星力场景：背包装备页第 1 格应是刚才那把武器；指针已钉在格上");
    }

    // ───────── 场景 19~22：家族面板（UserList 主窗 tab2） ─────────
    // 种进存档的家族必须在结束时回滚，否则下轮的「未入家族」截图里还挂着上次的家族。
    const string K_SEEDGUILD = "PreviewShot.seedGuild";

    static void SetupGuild()
    {
        ClearWindows();
        BottomMenu.Close();
        NativeGuildGradeWindow.Reset();
        // 上一轮（或修复前的旧版本）留下的种子家族：名字/族长都符合就当自己的清掉
        int cid = GameSession.CharacterId;
        var db = cid > 0 ? GameSession.Server.Db : null;
        var stale = db?.Table("guilds").Find(r =>
            DbStore.Str(r, "name") == SeedGuildName && DbStore.Int(r, "leader") == cid);
        if (stale != null)
        {
            EditorPrefs.SetInt(K_SEEDGUILD, DbStore.Int(stale, "guildid"));
            RevertSeededGuild();
            Note("清掉上一轮遗留的种子家族");
        }
        if (!NativeGuildWindow.Visible) NativeGuildWindow.Toggle();
        Pointer.TestPos = new Vector2(1100f, 40f);
        var st = NativeCommunityWindow.State;
        Note("家族窗已开：Visible=" + NativeGuildWindow.Visible + " guildId=" + (st?.guildId ?? -1) +
             " 成员=" + (st?.guild?.Count ?? -1));
    }

    // 种子的家族名；同时是旧数据自查的标记（族长必须是当前角色）
    const string SeedGuildName = "未来岛";

    // 把本脚本插进存档的家族整个撤回：成员归属清 0、guilds 行删掉。
    static void RevertSeededGuild()
    {
        int gid = EditorPrefs.GetInt(K_SEEDGUILD, 0);
        if (gid <= 0) return;
        try
        {
            var db = GameSession.Server.Db;
            foreach (var row in db.Table("characters").Select(x => DbStore.Int(x, "guildid") == gid))
            {
                row["guildid"] = 0L;
                row["guildrank"] = 5L;
            }
            db.Table("guilds").Delete(x => DbStore.Int(x, "guildid") == gid);
            db.Save();
            if (GameSession.CharacterId > 0)
                GameSession.Character.inventory = CharService.Load(db, GameSession.CharacterId).inventory;
            Note("已回滚种子家族 guildid=" + gid);
        }
        catch (Exception e) { Note("回滚种子家族失败：" + e.Message); }
        EditorPrefs.DeleteKey(K_SEEDGUILD);
    }

    // 走和游戏内一模一样的落库路径（guilds 一行 + characters.guildid/guildrank），
    // 然后再走一次真实的 Read，面板看到的每个字节都来自 DB，不摆 mock。
    static void SeedGuild()
    {
        int cid = GameSession.CharacterId;
        if (cid <= 0) { Note("不在游戏内，无法建家族"); return; }
        var db = GameSession.Server.Db;
        var me = db.Table("characters").Find(r => DbStore.Int(r, "id", 0) == cid);
        if (me == null) { Note("存档里找不到角色 " + cid); return; }

        int gid = DbStore.Int(me, "guildid", 0);
        if (gid <= 0)
        {
            gid = (int)db.Table("guilds").Insert(new DbRow {
                ["leader"] = (long)cid,
                ["GP"] = 12000L,
                ["name"] = "未来岛",
                ["rank1title"] = "族长", ["rank2title"] = "副族长", ["rank3title"] = "优秀族员",
                ["rank4title"] = "正式族员", ["rank5title"] = "实习生",
                ["capacity"] = 30L,
                ["logo"] = 1L, ["logoColor"] = 3L, ["logoBG"] = 2L, ["logoBGColor"] = 5L,
                ["notice"] = "欢迎加入未来岛，每日晚 8 点组队打 BOSS。",
                ["signature"] = 1L,
            });
            me["guildid"] = (long)gid;
            me["guildrank"] = 1L;
            db.Save();
            EditorPrefs.SetInt(K_SEEDGUILD, gid);      // Finish 时按这个 id 回滚
            Note("落库：guilds 新增 guildid=" + gid + "，角色 " + cid + " 设为 rank1");
        }

        var st = NativeCommunityWindow.Request(CommunityOperation.Read);
        Note("家族：guildId=" + (st?.guildId ?? -1) + " 名=" + (st?.guildName ?? "") +
             " 成员=" + (st?.guild?.Count ?? -1) + " GP=" + (st?.gp ?? -1) + " 上限=" + (st?.capacity ?? -1));
        if (st?.guild != null && st.guild.Count > 0)
        {
            int id = st.guild[0].id;
            typeof(NativeGuildWindow).GetField("selectedId", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, id);
            var m = st.guild[0];
            Note("选中成员：" + m.name + "（id " + m.id + "，rank " + m.rank + "，job " + m.job +
                 " lv " + m.level + "，map " + m.map + " " + NativeMapNames.Of(m.map) + "）");
        }
    }

    // BtWhere（按钮表下标 2）：源端是纯本地行为 —— 拿成员所在地图拼一句话打到聊天窗，不发包。
    static void GuildWhere()
    {
        var st = NativeCommunityWindow.State;
        var f = typeof(NativeGuildWindow);
        int sel = (int)(f.GetField("selectedId", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) ?? 0);
        CommunityMember target = st?.guild?.Find(m => m.id == sel);
        if (target == null) { Note("没有选中成员，跳过成员位置"); return; }
        f.GetMethod("Act", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, new object[] { 2, st, target });
        Note("BtWhere → " + target.name + " 目前在 " + NativeMapNames.Of(target.map) + "（map " + target.map + "）");
    }

    static string Join(AdminSetItem.SetPanelTier t)    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (var kv in t.Stats) parts.Add(kv.Key + "+" + kv.Value);
        return string.Join(" ", parts.ToArray());
    }

    // 含 IMGUI 的整屏截图。编辑器里是异步的：低帧率下会滞后，所以调用方要留足等待。
    static void Shot(string name)
    {
        string path = Path.Combine(EditorPrefs.GetString(K_DIR, "/tmp/mapledash-preview"), name);
        try
        {
            ScreenCapture.CaptureScreenshot(path, 1);
            Note("截图（含 UI）" + name + "，本帧 tooltip 挂起=" + ItemTooltip.HasHover +
                 "（hoverId " + ItemTooltip.HoverId + "），装备窗=" + EquipWindow.Visible +
                 " 背包=" + InvenWindow.Visible + " 技能窗=" + SkillWindow.Visible);
        }
        catch (Exception e) { Note("截图失败 " + name + "：" + e.Message); }
    }

    // 同帧同步出图（不含 IMGUI）：会动的特效只能靠它。
    static void ShotRt(string name)
    {
        var cam = Camera.main;
        if (cam == null) { Note("没有主相机，跳过 " + name); return; }
        int W = EditorPrefs.GetInt(K_W, 1280), H = EditorPrefs.GetInt(K_H, 720);
        Rect viewport = cam.rect;
        float aspect = cam.aspect;
        RenderTexture rt = null, prev = RenderTexture.active;
        Texture2D tex = null;
        try
        {
            rt = new RenderTexture(W, H, 24);
            cam.targetTexture = rt;
            cam.rect = new Rect(0f, 0f, 1f, 1f);
            cam.aspect = (float)W / H;
            foreach (var view in UnityEngine.Object.FindObjectsOfType<NativeMapView>()) view.SendMessage("LateUpdate");
            cam.Render();
            RenderTexture.active = rt;
            tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(EditorPrefs.GetString(K_DIR, "/tmp/mapledash-preview"), name), tex.EncodeToPNG());
            int anim = 0;
            foreach (var a in UnityEngine.Object.FindObjectsOfType<NativeImgAnimation>()) if (a != null) anim++;
            Note("截图（特效）" + name + "，场上 IMG 动画对象 " + anim + " 个");
        }
        catch (Exception e) { Note("截图失败 " + name + "：" + e.Message); }
        finally
        {
            if (cam != null) { cam.targetTexture = null; cam.rect = viewport; cam.aspect = aspect; }
            RenderTexture.active = prev;
            if (rt != null) UnityEngine.Object.Destroy(rt);
            if (tex != null) UnityEngine.Object.Destroy(tex);
        }
    }

    static void SetWindowPos(Type window, float x, float y)
    {
        var f = window.GetField("pos", BindingFlags.NonPublic | BindingFlags.Static);
        if (f == null) { Note(window.Name + " 没有 pos 字段"); return; }
        f.SetValue(null, new Vector2(x, y));
    }

    // 指针钉在「窗口内逻辑坐标」上（窗口 rect = pos*S，格子也是 (pos+逻辑)*S）
    // 洗点窗的分页状态是私有的：截第 2/3 页时按原端字段（stage/from/to）直接拨过去。
    static void SetResetStage(int stage, int from, int to)
    {
        var t = typeof(NativeResetWindow);
        const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Static;
        t.GetField("stage", F)?.SetValue(null, stage);
        t.GetField("from", F)?.SetValue(null, from);
        t.GetField("to", F)?.SetValue(null, to);
        Note("洗点窗 stage=" + stage + " from=" + from + " to=" + to);
    }

    static void Pin(Type window, float lx, float ly)
    {
        EditorPrefs.SetString(K_PINW, window.Name);
        EditorPrefs.SetFloat(K_PINX, lx);
        EditorPrefs.SetFloat(K_PINY, ly);
        Repin();
        Note(window.Name + " pos=" + WindowPos(window) + " 指针=" + Pointer.TestPos +
             "（S=" + BottomBar.S.ToString("0.000") + "，屏幕 " + UiKit.W + "x" + UiKit.H + "）");
    }

    static void ClearPin() { EditorPrefs.SetString(K_PINW, ""); Pointer.TestPos = null; }

    // 每帧重算：EnterPlaymode 后头几帧 Screen 尺寸还没落定，只算一次的指针会偏到屏幕角落
    static void Repin()
    {
        string name = EditorPrefs.GetString(K_PINW, "");
        if (string.IsNullOrEmpty(name)) return;
        var type = name == "EquipWindow" ? typeof(EquipWindow)
                 : name == "InvenWindow" ? typeof(InvenWindow)
                 : name == "SkillWindow" ? typeof(SkillWindow)
                 : name == "NativeResetWindow" ? typeof(NativeResetWindow) : null;
        if (type == null) return;
        float S = BottomBar.S;
        if (S < 0.3f) return;                 // 尺寸没准备好，等下一帧
        // GameView 尺寸还没落到目标值时算出来的指针会偏（曾出现 S=0.541 与 1.082 打架，
        // 指针钉在空格子上、悬浮框根本不出现），所以尺寸不对就等。
        if (Screen.width != EditorPrefs.GetInt(K_W, Screen.width) ||
            Screen.height != EditorPrefs.GetInt(K_H, Screen.height)) return;
        var pos = WindowPos(type);
        Pointer.TestPos = new Vector2((pos.x + EditorPrefs.GetFloat(K_PINX, 0f)) * S,
                                      (pos.y + EditorPrefs.GetFloat(K_PINY, 0f)) * S);
    }

    static Vector2 WindowPos(Type window)
    {
        var f = window.GetField("pos", BindingFlags.NonPublic | BindingFlags.Static);
        return f != null ? (Vector2)f.GetValue(null) : Vector2.zero;
    }

    static void Step(double next, int phase)
    {
        EditorPrefs.SetString(K_NEXT, next.ToString("R", CultureInfo.InvariantCulture));
        EditorPrefs.SetInt(K_PHASE, phase);
    }

    static void Note(string s)
    {
        Debug.Log("[PreviewShot] " + s);
        try
        {
            string dir = EditorPrefs.GetString(K_DIR, "/tmp/mapledash-preview");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "report.txt"), s + "\n");
        }
        catch { }
    }

    static double Parse(string s)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;

    static void Finish(int code)
    {
        // 种子家族要撤回：这套数据是给截图造的，不能留在玩家存档里
        try { RevertSeededGuild(); }
        catch (Exception e) { Debug.LogWarning("[PreviewShot] 种子家族回滚失败：" + e.Message); }

        // 存档原样写回：这套数据是给截图用的，不能留在玩家的存档里
        try
        {
            if (EditorPrefs.GetBool(K_HAD, false))
                PlayerPrefs.SetString(SnapKey, EditorPrefs.GetString(K_SNAP, ""));
            else
                PlayerPrefs.DeleteKey(SnapKey);
            PlayerPrefs.DeleteKey("autostart");
            RestoreSettings();          // 设置窗实拍改过的音量/邀请位/警报值也一起还原
            PlayerPrefs.Save();
        }
        catch (Exception e) { Debug.LogWarning("[PreviewShot] 存档恢复失败：" + e.Message); }

        Pointer.TestPos = null;
        Pointer.TestClick = false;
        EquipWindow.Visible = false;
        InvenWindow.Visible = false;
        SkillWindow.Visible = false;

        foreach (var k in new[] { K_RUN, K_DIR, K_T0, K_PHASE, K_NEXT, K_SNAP, K_HAD, K_PINW, K_PINX, K_PINY, K_SETTINGS })
            EditorPrefs.DeleteKey(k);
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayMode;
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        Debug.Log("[PreviewShot] 结束，退出码 " + code);
        EditorApplication.Exit(code);
    }
}
