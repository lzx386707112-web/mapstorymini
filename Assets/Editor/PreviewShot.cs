using System;
using System.Globalization;
using System.IO;
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
            default:
                Finish(0);
                break;
        }
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
                 : name == "SkillWindow" ? typeof(SkillWindow) : null;
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
        // 存档原样写回：这套数据是给截图用的，不能留在玩家的存档里
        try
        {
            if (EditorPrefs.GetBool(K_HAD, false))
                PlayerPrefs.SetString(SnapKey, EditorPrefs.GetString(K_SNAP, ""));
            else
                PlayerPrefs.DeleteKey(SnapKey);
            PlayerPrefs.DeleteKey("autostart");
            PlayerPrefs.Save();
        }
        catch (Exception e) { Debug.LogWarning("[PreviewShot] 存档恢复失败：" + e.Message); }

        Pointer.TestPos = null;
        Pointer.TestClick = false;
        EquipWindow.Visible = false;
        InvenWindow.Visible = false;
        SkillWindow.Visible = false;

        foreach (var k in new[] { K_RUN, K_DIR, K_T0, K_PHASE, K_NEXT, K_SNAP, K_HAD, K_PINW, K_PINX, K_PINY })
            EditorPrefs.DeleteKey(k);
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayMode;
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        Debug.Log("[PreviewShot] 结束，退出码 " + code);
        EditorApplication.Exit(code);
    }
}
