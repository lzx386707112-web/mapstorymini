using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// 悬浮框对照图（2026-10-29 换的第二版预览形式）
//
// 旧做法（PreviewShot 的 01/07 那几张）是「把指针钉在格子上，再抓整屏」：
//   它依赖 BottomBar.S、GameView 尺寸、窗口坐标三者同时正确，只要有一个没落定，
//   指针就落在空格子上，抓出来的图里根本没有悬浮框（11-15 那轮 S=0.067，07 图空白）。
//   而且每张固定等 3 秒异步落盘，一轮 25 个 phase 一分多钟。
//
// 这一版把「绘制」和「指针」解耦：ItemTooltip.PreviewDraw(id, anchor, instance) 直接在
// 指定锚点画一次，所以和鼠标、窗口位置、缩放全无关；预览时铺一层深色底，抓到的图里
// 只有悬浮框本身；再按 ItemTooltip 记下的真实外框裁切放大，最后拼成一张对照长图。
//
// 命令行：
//   Unity -projectPath <proj> -executeMethod TooltipPreview.Run \
//     -tooltipShots /tmp/mapledash-tooltip -previewWidth 1280 -previewHeight 720 \
//     -logFile /tmp/tooltip_preview.log
// 也能在编辑器菜单 Tools/预览/悬浮框对照图 直接点。
public static class TooltipPreview
{
    const string K_RUN = "TooltipPreview.running";
    const string K_DIR = "TooltipPreview.dir";
    const string K_T0 = "TooltipPreview.t0";
    const string K_STEP = "TooltipPreview.step";
    const string K_NEXT = "TooltipPreview.next";
    const string K_SCENE = "TooltipPreview.scene";
    const string K_IDX = "TooltipPreview.index";
    const string K_WAITPATH = "TooltipPreview.waitPath";
    const string K_WAITT0 = "TooltipPreview.waitT0";
    const string K_SNAP = "TooltipPreview.snapshot";
    const string K_HAD = "TooltipPreview.hadSnapshot";
    const string K_W = "TooltipPreview.width";
    const string K_H = "TooltipPreview.height";
    const string K_QUIT = "TooltipPreview.quitAfter";
    const string SnapKey = "mxd.profile.v1";

    const int SetHat = 1003242, SetTop = 1052357, SetShoes = 1072521;
    const int SetGlove = 1082314, SetCape = 1102294, SetWeapon = 1302169;
    const int StarWeapon = 1302081;      // 永恒破甲剑，reqLevel 120 → 15 星上限，incPAD 110
    const int Potion = 2000000;

    sealed class Scene
    {
        public string key, title;
        public int itemId;
        public int forceStars = -1;   // >=0 时把实例星数临时改成该值（只改内存，不落库）
    }

    // 场景表必须是「无状态」的：域重载后静态字段全丢，每帧靠 key 重新解析。
    static List<Scene> Scenes()
    {
        return new List<Scene>
        {
            new Scene { key = "weapon-0star",  title = "武器 · 0★（星条全灰，验证位置）", itemId = StarWeapon, forceStars = 0 },
            new Scene { key = "weapon-12star", title = "武器 · 12★ / 15（金灰对比）", itemId = StarWeapon, forceStars = 12 },
            new Scene { key = "equip-hat-set", title = "帽子 + 套装面板（宝石套 6/8）", itemId = SetHat },
            new Scene { key = "item-potion",   title = "普通物品 · 红色药水", itemId = Potion },
        };
    }

    [InitializeOnLoadMethod]
    static void Hook()
    {
        if (!EditorPrefs.GetBool(K_RUN, false)) return;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= OnPlayMode;
        EditorApplication.playModeStateChanged += OnPlayMode;
    }

    [MenuItem("Tools/预览/悬浮框对照图")]
    public static void RunFromMenu() => Run();

    public static void Run()
    {
        string dir = "/tmp/mapledash-tooltip";
        int width = 1280, height = 720;
        var args = Environment.GetCommandLineArgs();
        bool fromCmd = false;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-tooltipShots") dir = args[i + 1];
            if (args[i] == "-previewWidth") int.TryParse(args[i + 1], out width);
            if (args[i] == "-previewHeight") int.TryParse(args[i + 1], out height);
        }
        foreach (var a in args) if (a == "-executeMethod") fromCmd = true;
        width = Mathf.Max(800, width);
        height = Mathf.Max(600, height);

        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "meta.jsonl"), "");
        File.WriteAllText(Path.Combine(dir, "report.txt"),
            "MapleDash 悬浮框对照图（TooltipPreview）\n" +
            "时间 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n" +
            "真实存档装备 + 真实 WZ 素材，PreviewDraw 定点绘制，按真实外框裁切。\n\n");

        EditorPrefs.SetBool(K_HAD, PlayerPrefs.HasKey(SnapKey));
        EditorPrefs.SetString(K_SNAP, PlayerPrefs.GetString(SnapKey, ""));
        EditorPrefs.SetString(K_DIR, dir);
        EditorPrefs.SetInt(K_W, width);
        EditorPrefs.SetInt(K_H, height);
        EditorPrefs.SetBool(K_QUIT, fromCmd);
        EditorPrefs.SetBool(K_RUN, true);
        EditorPrefs.SetInt(K_STEP, 0);
        EditorPrefs.SetInt(K_IDX, 0);
        EditorPrefs.SetString(K_T0, "0");
        EditorPrefs.SetString(K_NEXT, "0");
        EditorPrefs.SetString(K_SCENE, "");
        EditorPrefs.SetString(K_WAITPATH, "");

        PlayerPrefs.SetInt("autostart", 1);
        PlayerPrefs.SetInt("hide_tuning", 1);
        PlayerPrefs.SetInt("debug_god", 1);
        PlayerPrefs.Save();

        HudChecks.SetGameViewSize(width, height);
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
        EditorPrefs.SetInt(K_STEP, 0);
        EditorPrefs.SetInt(K_IDX, 0);
        EditorPrefs.SetString(K_NEXT, "0");
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

        double now = EditorApplication.timeSinceStartup;
        if (now < Parse(EditorPrefs.GetString(K_NEXT, "0"))) return;

        // 每帧重挂：进 Play 会域重载，静态委托和实例都会丢，重建比存状态可靠。
        EnsureHook();

        // 虚拟画布稳定性：编辑器里 UiKit.W/H 会随 GameView 抖动，
        // 画面帧和记录外框的那一帧必须同尺寸，否则裁切会偏。
        if (Mathf.Abs(UiKit.W - stableW) > 0.5f || Mathf.Abs(UiKit.H - stableH) > 0.5f)
        { stableW = UiKit.W; stableH = UiKit.H; stableCount = 0; }
        else stableCount++;

        int step = EditorPrefs.GetInt(K_STEP, 0);
        switch (step)
        {
            case 0:
                if (!Ready()) { if (now - t0 > 150) { Note("等待游戏就绪超时"); Finish(1); } return; }
                Prepare();
                Step(now + 0.4, 1);
                break;

            case 1:   // 摆好场景，等两帧让 PreviewDraw 真正画一次
            {
                int idx = EditorPrefs.GetInt(K_IDX, 0);
                var scenes = Scenes();
                if (idx >= scenes.Count) { Step(now + 0.1, 4); return; }
                EditorPrefs.SetString(K_SCENE, scenes[idx].key);
                normReady = false;
                normSet = null;
                Step(now + 0.3, 2);
                break;
            }

            case 2:   // 抓图（要等虚拟画布连续几帧不变，否则画面帧和外框帧对不上）
            {
                if (stableCount < 3) { Step(now + 0.05, 2); return; }
                int idx = EditorPrefs.GetInt(K_IDX, 0);
                var scenes = Scenes();
                string key = idx < scenes.Count ? scenes[idx].key : "x";
                string file = Path.Combine(EditorPrefs.GetString(K_DIR, "/tmp/mapledash-tooltip"),
                    string.Format("{0:00}-{1}.png", idx + 1, key));
                if (File.Exists(file)) File.Delete(file);
                ScreenCapture.CaptureScreenshot(file, 1);
                EditorPrefs.SetString(K_WAITPATH, file);
                EditorPrefs.SetString(K_WAITT0, now.ToString("R", CultureInfo.InvariantCulture));
                Step(now + 0.1, 3);
                break;
            }

            case 3:   // 等落盘（通常一两帧），超时也往下走，别卡死整轮
            {
                string path = EditorPrefs.GetString(K_WAITPATH, "");
                bool ok = File.Exists(path) && new FileInfo(path).Length > 0;
                double waited = now - Parse(EditorPrefs.GetString(K_WAITT0, "0"));
                if (!ok && waited < 6) return;
                if (ok) WriteMeta(path);
                else Note("截图超时 " + Path.GetFileName(path));
                EditorPrefs.SetInt(K_IDX, EditorPrefs.GetInt(K_IDX, 0) + 1);
                EditorPrefs.SetString(K_SCENE, "");
                EditorPrefs.SetString(K_WAITPATH, "");
                Step(now + 0.1, 1);
                break;
            }

            default:
                Finish(0);
                break;
        }
    }

    // 归一化外框（0..1，相对 UiKit 虚拟画布）。
    // 必须用 UiKit.W/H 而不是 Screen.width/height：编辑器里 Screen 会随 GameView 窗口抖动
    // （实测录到 1206x20 / 2412x40），而 GUI 画的是虚拟画布，两者不成比例。
    static Rect normEquip;
    static Rect? normSet;
    static bool normReady;
    static float stableW = -1, stableH = -1;
    static int stableCount;

    static void EnsureHook()
    {
        string key = EditorPrefs.GetString(K_SCENE, "");
        if (string.IsNullOrEmpty(key)) { UiPreviewHook.Draw = null; UiPreviewHook.SolidBackdrop = false; return; }
        Scene scene = null;
        foreach (var s in Scenes()) if (s.key == key) { scene = s; break; }
        if (scene == null) { UiPreviewHook.Draw = null; return; }
        var inst = FindInstance(scene.itemId);
        // 只改内存实例（不落库、不调服务端），用来对比同一件装备不同星数的星条。
        if (inst != null && scene.forceStars >= 0 && inst.starLevel != scene.forceStars)
            inst.starLevel = scene.forceStars;
        int id = scene.itemId;
        // 锚点固定：横向居中、靠上，保证高框也不会被 clamp 到底部，裁切位置可预期。
        Vector2 anchor = new Vector2(UiKit.W * 0.5f, UiKit.H * 0.06f);
        UiPreviewHook.SolidBackdrop = true;
        UiPreviewHook.Draw = () =>
        {
            ItemTooltip.PreviewDraw(id, anchor, inst);
            float w = Mathf.Max(1f, UiKit.W), h = Mathf.Max(1f, UiKit.H);
            var r = ItemTooltip.LastEquipRect;
            normEquip = new Rect(r.x / w, r.y / h, r.width / w, r.height / h);
            var set = ItemTooltip.LastSetRect;
            normSet = set.HasValue
                ? new Rect(set.Value.x / w, set.Value.y / h, set.Value.width / w, set.Value.height / h)
                : (Rect?)null;
            normReady = r.width > 1f;
        };
    }

    static InventoryEntry FindInstance(int id)
    {
        var ch = GameSession.Character;
        if (ch == null || ch.inventory == null) return null;
        InventoryEntry worn = null, bag = null;
        foreach (var it in ch.inventory)
        {
            if (it == null || it.itemId != id) continue;
            if (it.position < 0) worn = worn ?? it;
            else bag = bag ?? it;
        }
        return worn ?? bag;
    }

    static bool Ready() => GameLoop.I != null && GameLoop.I.player != null && GameLoop.I.state == GameLoop.GameState.Playing;

    // ───────── 一次把数据准备好：穿套装 + 发一把 12★ 武器 + 一瓶药 ─────────
    static void Prepare()
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
        Note("角色 Lv." + Profile.level + " 职业 " + Profile.JobId + "，已穿 6 件宝石套");

        int cid = GameSession.CharacterId;
        if (cid <= 0) { Note("不在游戏内（CharacterId=0），武器场景只有 WZ 基础值"); d.AddItem(Potion, 50); return; }

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

        var added = GameSession.Net.ChangeInventory(new InventoryChangeRequest
        { characterId = cid, itemId = StarWeapon, quantity = 1 });
        if (!added.ok) { Note("发武器失败 " + added.error); d.AddItem(Potion, 50); return; }
        GameSession.Character.inventory = added.inventory;
        var w = GameSession.Character.inventory.Find(x => x.itemId == StarWeapon);
        if (w == null) { Note("找不到刚发的武器"); d.AddItem(Potion, 50); return; }

        var row = GameSession.Server.Db.Table("inventoryitems")
            .Find(x => DbStore.Long(x, "inventoryitemid") == w.itemRowId);
        if (row != null) { row["position"] = 1L; row["inventorytype"] = 1L; GameSession.Server.Db.Save(); }

        var info = EquipCatalog.Info(StarWeapon);
        var res = GameSession.Net.UpdateEquip(new EquipPatchRequest
        {
            characterId = cid, inventoryItemId = w.itemRowId,
            upgradeslots = 0, starLevel = 12, starCount = 0, maxStar = 15, owner = w.owner,
            str = info.Int("incSTR"), dex = info.Int("incDEX"),
            int_ = info.Int("incINT"), luk = info.Int("incLUK"),
            hp = info.Int("incMHP"), mp = info.Int("incMMP"),
            watk = info.Int("incPAD"), matk = info.Int("incMAD"),
            wdef = info.Int("incPDD"), mdef = info.Int("incMDD"),
            acc = info.Int("incACC"), avoid = info.Int("incEVA"),
            hands = info.Int("incCraft"), speed = info.Int("incSpeed"), jump = info.Int("incJump"),
        });
        if (!res.ok) Note("种星级失败 " + res.error);
        else
        {
            GameSession.Character.inventory = res.inventory;
            var s2 = GameSession.Character.inventory.Find(x => x.itemId == StarWeapon);
            Note("武器 " + ItemCatalog.Name(StarWeapon) + " 实拍实例：starLevel=" + s2.starLevel +
                 " maxStar=" + s2.maxStar + " watk=" + s2.watk + " tuc=" + s2.upgradeslots);
        }
        d.AddItem(Potion, 50);
    }

    static void WriteMeta(string file)
    {
        string key = EditorPrefs.GetString(K_SCENE, "");
        string title = key;
        foreach (var s in Scenes()) if (s.key == key) { title = s.title; break; }
        if (!normReady)
        {
            Note("出图 " + Path.GetFileName(file) + "，但这一帧没画出悬浮框（normReady=false）");
            return;
        }
        string json = "{\"key\":\"" + Esc(key) + "\",\"title\":\"" + Esc(title) + "\",\"file\":\"" +
            Esc(Path.GetFileName(file)) + "\",\"unit\":\"norm\",\"rect\":[" +
            N(normEquip.x) + "," + N(normEquip.y) + "," + N(normEquip.width) + "," + N(normEquip.height) + "]";
        if (normSet.HasValue)
        {
            var v = normSet.Value;
            json += ",\"setRect\":[" + N(v.x) + "," + N(v.y) + "," + N(v.width) + "," + N(v.height) + "]";
        }
        json += "}";
        File.AppendAllText(Path.Combine(EditorPrefs.GetString(K_DIR, "/tmp/mapledash-tooltip"), "meta.jsonl"), json + "\n");
        Note("出图 " + Path.GetFileName(file) + "，悬浮框 " + R(normEquip.width * UiKit.W) + "x" +
             R(normEquip.height * UiKit.H) + "（虚拟单位），套装面板 " +
             (normSet.HasValue ? "有" : "无"));
    }

    static string N(float v) => v.ToString("0.00000", CultureInfo.InvariantCulture);
    static string R(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);
    static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

    static void Step(double next, int step)
    {
        EditorPrefs.SetString(K_NEXT, next.ToString("R", CultureInfo.InvariantCulture));
        EditorPrefs.SetInt(K_STEP, step);
    }

    static void Note(string s)
    {
        Debug.Log("[TooltipPreview] " + s);
        try
        {
            string dir = EditorPrefs.GetString(K_DIR, "/tmp/mapledash-tooltip");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "report.txt"), s + "\n");
        }
        catch { }
    }

    static double Parse(string s)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;

    static void Finish(int code)
    {
        UiPreviewHook.Draw = null;
        UiPreviewHook.SolidBackdrop = false;
        try
        {
            if (EditorPrefs.GetBool(K_HAD, false)) PlayerPrefs.SetString(SnapKey, EditorPrefs.GetString(K_SNAP, ""));
            else PlayerPrefs.DeleteKey(SnapKey);
            PlayerPrefs.DeleteKey("autostart");
            PlayerPrefs.Save();
        }
        catch (Exception e) { Debug.LogWarning("[TooltipPreview] 存档恢复失败：" + e.Message); }

        int count = 0;
        try
        {
            string meta = Path.Combine(EditorPrefs.GetString(K_DIR, "/tmp/mapledash-tooltip"), "meta.jsonl");
            if (File.Exists(meta)) foreach (var line in File.ReadAllLines(meta)) if (line.Trim().Length > 0) count++;
        }
        catch { }
        bool quitAfter = EditorPrefs.GetBool(K_QUIT, false);
        foreach (var k in new[] { K_RUN, K_DIR, K_T0, K_STEP, K_NEXT, K_SCENE, K_IDX, K_WAITPATH, K_WAITT0, K_SNAP, K_HAD, K_QUIT })
            EditorPrefs.DeleteKey(k);
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayMode;
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        Note("结束，退出码 " + code + "，共 " + count + " 张");
        // 只有命令行跑才退编辑器；菜单点的时候退了会把用户正在编辑的工程一起关掉。
        if (quitAfter) EditorApplication.Exit(code);
    }
}
