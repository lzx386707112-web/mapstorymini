// V 矩阵窗口（282 UI/VMatrixUI.img）。逐条照 docs/tms282-skill/20-v-hexa-panel.md §1 的几何：
//   main/backgrnd 506x719 @(0,0)；关闭 (470,16)、帮助 (449,16)、重置 (327,57)、
//   V 点转换 (407,57)、特殊核心开关 (347,98)；31 个槽位读 main/slotPos/*，
//   四区标题与「V 矩陣 / V POINT」都烘焙在底图里，不重画。
//
// 数据只有一份：窗口不自己算等级/费用，全部来自 VMatrixResponse（服务端快照），
// 预览（commit=false）也只用来显示「点了会变成什么样」。客户端唯一本地算的是
// 「最多能升到几级」——它要遍历 VMatrixCatalog 的费用表，和服务端同一份 JSON，
// 但真正落库前服务端会再校验一次（花不起就拒）。
using System;
using System.Collections.Generic;
using System.Globalization;
using MapleDash.Wz;
using UnityEngine;

public static class NativeVMatrixWindow
{
    const string Img = "UI/VMatrixUI.img/";
    const float W = 506f, H = 719f;
    // 特殊核心子窗：main/vector:specialCore (414,172) + specialCore/backgrnd 247x402。
    const float SpecialLeft = 414f, SpecialTop = 172f, SpecialW = 247f, SpecialH = 402f;
    const float SlotStepX = 78f, SlotStepY = 75f;

    enum Dialog { None, Upgrade, Result, Transform, Gemstone, Register }

    static bool visible, dragging, positioned, specialOpen;
    static Vector2 position, grab;
    static int owner, selected = -1;
    static VMatrixResponse state;
    static string error;

    static Dialog dialog;
    static int dialogCore, targetLevel, transformCount = 1, gemCount = 1;
    static int gemItem, specialScroll;
    static int resultBefore, resultAfter;

    public static bool Visible => visible;
    public static bool AtPointer => visible && (dragging || Pointer.HitsWindow(WindowRect()));

    static float Width => specialOpen ? SpecialLeft + SpecialW : W;
    static float S => Mathf.Min(BottomBar.S, Mathf.Min((UiKit.W - 24) / Width, UiKit.H * .92f / H));
    static Vector2 Free => positioned ? NativeUi.ClampWindowPosition(position, new Vector2(Width, H), S)
        : new Vector2((UiKit.W / S - Width) / 2, (UiKit.H / S - H) / 2);
    static Rect WindowRect() => new Rect(Free.x * S, Free.y * S, Width * S, H * S);

    public static void Escape()
    {
        if (dialog != Dialog.None) { dialog = Dialog.None; return; }
        Close();
    }

    public static void Close()
    {
        visible = dragging = positioned = specialOpen = false;
        state = null; error = null; selected = -1; dialog = Dialog.None;
    }

    public static void Open()
    {
        Close();
        if (!GameSession.InGame) return;
        visible = true; owner = GameSession.CharacterId;
        Load();
    }

    /// <summary>背包里使用核心宝石：直接开这颗宝石的使用窗（源端是 UseItemHandler 分支）。</summary>
    public static void OpenGemstone(int itemId)
    {
        if (!visible) Open();
        if (!visible) return;
        gemItem = itemId; gemCount = 1; dialog = Dialog.Gemstone;
    }

    static void Load()
    {
        var response = GameSession.Net.VMatrix(new VMatrixRequest
        { characterId = owner, operation = VMatrixOperation.Snapshot });
        if (response == null) return;
        state = response;
        error = response.eligible ? null : response.error;
        if (selected >= 0 && CoreOf(selected) == null) selected = -1;
        NativeSpecialCore.Observe(response);
    }

    /// <summary>下发一条操作；commit=false 只拿预览。失败文案留在窗口里，不关窗。</summary>
    static VMatrixResponse Send(VMatrixOperation operation, int core = 0, int target = 0,
        int item = 0, int count = 0, bool commit = true)
    {
        var response = GameSession.Net.VMatrix(new VMatrixRequest
        {
            characterId = owner, operation = operation, coreId = core, targetLevel = target,
            itemId = item, count = count, commit = commit,
        });
        if (response == null) return null;
        if (!response.ok) { error = response.error; BottomBar.Toast(response.error); return response; }
        error = null;
        if (commit) { state = response; GameSession.ReloadAfterCommand(); }
        // 特殊核心是「全局只允许一个」，战斗侧要知道当前那个是谁，这里跟随快照同步。
        NativeSpecialCore.Observe(response);
        return response;
    }

    // —————————————— 数据小工具 ——————————————

    static VMatrixCoreView CoreOf(int slot)
    {
        if (state?.slots == null || slot < 0 || slot >= state.slots.Length) return null;
        int id = state.slots[slot];
        if (id == 0) return null;
        return Find(id);
    }

    static VMatrixCoreView Find(int coreId)
    {
        if (state?.cores == null) return null;
        foreach (var core in state.cores) if (core.coreId == coreId) return core;
        return null;
    }

    static Vector2 SlotAnchor(int slot)
    {
        string section = slot < 4 ? "skill" : slot < 10 ? "enforce" : "common";
        int index = slot < 4 ? slot : slot < 10 ? slot - 4 : slot - 10;
        var node = WzAssets.Node(Img + "main/slotPos/" + section + "/" + index);
        if (node != null && node.TryVector("", out int x, out int y)) return new Vector2(x, y);
        // 节点丢失时按文档表兜底，保证不画到窗口外。
        int col = index % 6;
        float fy = section == "skill" ? 172 : section == "enforce" ? 302 : 432 + 75 * (index / 6);
        return new Vector2(63 + col * SlotStepX, fy);
    }

    static Vector2 Vector(string path, float fallbackX, float fallbackY)
    {
        var node = WzAssets.Node(Img + path);
        return node != null && node.TryVector("", out int x, out int y)
            ? new Vector2(x, y) : new Vector2(fallbackX, fallbackY);
    }

    // —————————————— 绘制 ——————————————

    public static void Draw()
    {
        if (!visible) return;
        if (!GameSession.InGame || owner != GameSession.CharacterId) { Close(); return; }
        var window = WindowRect();
        bool ownsPointer = AtPointer;
        GrowthUi.S = S;
        GrowthUi.R = window;
        // 抓手 = 整条标题栏（排除右上关闭/帮助），特殊核心子窗打开时也要能拖；
        // slop 让整块面板都能抓（按下后走 6px 才转成拖动，点核心/按钮不受影响）。
        if (dialog == Dialog.None && GrowthUi.Drag(ref dragging, ref positioned, ref position, ref grab, Width, H, WindowRect, 46f, 100f,
                owner: typeof(NativeVMatrixWindow), slop: 6f))
            window = WindowRect();
        GrowthUi.R = window;
        NativeUi.RegisterWindow("NativeVMatrixWindow", window, () => visible);

        MatrixPresentation.Begin();
        bool parentEnabled = GUI.enabled;
        // 弹窗覆盖期间只绘制主面板，底层槽位、页签和按钮不得消费弹窗点击。
        GUI.enabled = parentEnabled && dialog == Dialog.None;
        GrowthUi.Image(Img + "main/", "backgrnd", 0, 0);
        DrawPoints();
        DrawSlots();
        DrawSpecialCore();
        DrawButtons();

        // 弹窗最后画：它们压在槽位与按钮之上，但仍在窗口矩形内。
        if (dialog != Dialog.None) MatrixPresentation.Begin();
        GUI.enabled = parentEnabled;
        switch (dialog)
        {
            case Dialog.Upgrade: DrawUpgrade(); break;
            case Dialog.Result: DrawResult(); break;
            case Dialog.Transform: DrawTransform(); break;
            case Dialog.Gemstone: DrawGemstone(); break;
            case Dialog.Register: DrawRegister(); break;
        }

        MatrixPresentation.Draw();
        if (ownsPointer && GrowthUi.Repaint)
        {
            Pointer.ConsumeClick();
            Pointer.Pressed = Pointer.RightPressed = Pointer.RightReleased = false;
        }

        if (!string.IsNullOrEmpty(error))
        {
            GrowthUi.Plate(12, 690, 482, 20, .8f);
            GrowthUi.Text(Img + "main/", error, 16, 692, 474, "", TextAnchor.UpperLeft, 13, new Color(1f, .58f, .52f));
        }
    }

    static void DrawPoints()
    {
        var anchor = Vector("main/vector:vPointCount", 302, 61);
        GrowthUi.ShadowText(Img + "main/", state == null ? "0" : state.points.ToString("N0", CultureInfo.InvariantCulture),
            anchor.x, anchor.y - 6, 180, "font:vPoint", TextAnchor.UpperLeft, 16);
    }

    // 用户授权按原槽框居中。普通/特殊底框的图标槽中心分别是(29,28)/(33,28)，
    // 下方锁与等级区不计入图标槽；保留图标原尺寸，不把位置向量当左上角。
    static void DrawCoreIcon(Texture2D icon, float x, float y, float frameWidth)
    {
        GUI.DrawTexture(GrowthUi.At(x + (frameWidth - icon.width) / 2f,
            y + 28f - icon.height / 2f, icon.width, icon.height), icon);
    }

    static void DrawSlots()
    {
        if (state == null) return;
        for (int slot = 0; slot < 31; slot++)
        {
            var anchor = SlotAnchor(slot);
            int coreId = state.slots != null && slot < state.slots.Length ? state.slots[slot] : 0;
            string section = slot < 4 ? "job" : slot < 10 ? "enforce" : "common";
            var view = CoreOf(slot);
            bool hover = GrowthUi.At(anchor.x - 34, anchor.y - 39, 68, 76).Contains(Pointer.Pos);

            string background = coreId == 0 ? "empty"
                : (view == null || view.level == 0) ? section + "Locked" : section;
            var frame = WzAssets.Node(Img + "core/coreBackground/" + background);
            var origin = frame == null ? new Vector2(34, 39) : WzAssets.Origin(frame);
            GrowthUi.Image(Img + "core/coreBackground/", background, anchor.x - origin.x, anchor.y - origin.y);
            float bx = anchor.x - origin.x, by = anchor.y - origin.y;

            if (coreId != 0)
            {
                var icon = view == null || view.skills == null || view.skills.Length == 0
                    ? null : WzAssets.Texture("skillicon/" + view.skills[0]);
                if (icon != null)
                    DrawCoreIcon(icon, bx, by, 58f);
                if (view != null && view.level > 0)
                {
                    // 等级数字的格子内偏移是 core/vector:currentLevelPos(-14,13)，占位从格中心算。
                    var levelPos = Vector("core/vector:currentLevelPos", -14, 13);
                    GrowthUi.ShadowText(Img + "core/", view.level.ToString(CultureInfo.InvariantCulture),
                        anchor.x + levelPos.x, anchor.y + levelPos.y, 44, "font:levelCount",
                        TextAnchor.UpperLeft, 12);
                }
            }
            if (hover && GUI.enabled && view != null)
                MatrixPresentation.Skill(view.skills, view.level, view.name, view.desc);
            if (hover)
            {
                string hoverState = coreId == 0 ? "0" : "1";
                var hoverNode = WzAssets.Node(Img + "core/coreState/mouseOver/" + hoverState);
                var hoverOrigin = WzAssets.Origin(hoverNode);
                GrowthUi.Image(Img + "core/coreState/mouseOver/", hoverState,
                    anchor.x - hoverOrigin.x, anchor.y - hoverOrigin.y);
            }
            if (slot == selected)
                GrowthUi.Veil(bx, by, 58, 68, new Color(1f, 1f, 1f, .18f));

            if (!GUI.enabled || !GrowthUi.Repaint || !Pointer.Clicked || !hover) continue;
            Pointer.ConsumeClick();
            selected = coreId == 0 ? -1 : slot;
            if (coreId == 0) continue;
            dialogCore = coreId;
            targetLevel = view?.level ?? 0;
            dialog = Dialog.Upgrade;
        }
    }

    static void DrawSpecialCore()
    {
        // 主窗右上角的「已启用特殊核心」展示位。
        // 这一格的位置来自 main/layer:specialCoreUnregistered 的 origin(-380,-132) → (380,132)；
        // main/vector:specialCore(414,172) 是「特殊核心子窗」的落点，不是这一格，
        // 之前拿它画未登记层，66x68 的加号整块往右下飘了 (34,40)。
        const float coverX = 380f, coverY = 132f;
        VMatrixCoreView active = null;
        if (state?.cores != null) foreach (var core in state.cores) if (core.type == 2 && core.selected) active = core;
        if (active != null)
        {
            var icon = active.skills.Length > 0 ? WzAssets.Texture("skillicon/" + active.skills[0]) : null;
            if (icon != null)
                DrawCoreIcon(icon, coverX, coverY, 66f);
        }
        else GrowthUi.Image(Img + "main/", "layer:specialCoreUnregistered", coverX, coverY);
        if (active != null && GUI.enabled && GrowthUi.At(coverX, coverY, 66, 68).Contains(Pointer.Pos))
            MatrixPresentation.Details(active.name, active.desc, active.level);
        if (!specialOpen) return;

        GrowthUi.Image(Img + "main/specialCore/", "backgrnd", SpecialLeft, SpecialTop);
        var slots = new List<VMatrixCoreView>();
        if (state?.cores != null) foreach (var core in state.cores) if (core.type == 2) slots.Add(core);
        // 未登记的也列出来：点一下就走 Register 弹窗（源端 register 分支）。
        foreach (var core in VMatrixCatalog.AllCores)
        {
            if (!core.IsSpecial) continue;
            bool listed = false;
            foreach (var view in slots) if (view.coreId == core.id) { listed = true; break; }
            if (!listed) slots.Add(new VMatrixCoreView { coreId = core.id, type = 2, name = core.name, skills = core.skills });
        }

        var lt = Vector("main/specialCore/vector:slotLT", 10, 47);
        var slotPos = Vector("main/specialCore/slot/slotPos", 36, 39);
        var offset = Vector("main/specialCore/slot/slotOffset", 75, 75);
        const int columns = 3, visibleRows = 4;
        int rows = (slots.Count + columns - 1) / columns;
        int maxScroll = Mathf.Max(0, rows - visibleRows);
        if (GUI.enabled && GrowthUi.At(SpecialLeft, SpecialTop, SpecialW, SpecialH).Contains(Pointer.Pos)
            && Mathf.Abs(Input.mouseScrollDelta.y) > .01f)
            specialScroll = Mathf.Clamp(specialScroll - (int)Mathf.Sign(Input.mouseScrollDelta.y), 0, maxScroll);
        specialScroll = Mathf.Clamp(specialScroll, 0, maxScroll);

        for (int i = 0; i < slots.Count; i++)
        {
            int row = i / columns, col = i % columns;
            if (row < specialScroll || row >= specialScroll + visibleRows) continue;
            float x = SpecialLeft + lt.x + col * offset.x + slotPos.x;
            float y = SpecialTop + lt.y + (row - specialScroll) * offset.y + slotPos.y;
            var view = slots[i];
            bool opened = false;
            foreach (var core in state.cores) if (core.coreId == view.coreId && core.level > 0) opened = true;
            var frame = WzAssets.Node(Img + "core/coreBackground/" + (opened ? "special" : "specialLocked"));
            var origin = frame == null ? new Vector2(34, 39) : WzAssets.Origin(frame);
            GrowthUi.Image(Img + "core/coreBackground/", opened ? "special" : "specialLocked", x - origin.x, y - origin.y);
            var icon = view.skills != null && view.skills.Length > 0 ? WzAssets.Texture("skillicon/" + view.skills[0]) : null;
            if (icon != null)
                DrawCoreIcon(icon, x - origin.x, y - origin.y, 66f);
            bool hover = GrowthUi.At(x - origin.x, y - origin.y, 66, 68).Contains(Pointer.Pos);
            if (hover && GUI.enabled) MatrixPresentation.Details(view.name, VMatrixCatalog.Core(view.coreId)?.desc, view.level);
            if (hover) GrowthUi.Veil(x - origin.x, y - origin.y, 66, 68, new Color(1f, 1f, 1f, .16f));
            if (!GUI.enabled || !GrowthUi.Repaint || !Pointer.Clicked || !hover) continue;
            Pointer.ConsumeClick();
            if (!opened)
            {
                dialogCore = view.coreId; targetLevel = 1; dialog = Dialog.Register;
                continue;
            }
            var toggled = Send(view.selected ? VMatrixOperation.UnselectSpecial : VMatrixOperation.SelectSpecial, core: view.coreId);
            if (toggled != null && toggled.ok)
                BottomBar.Toast(view.selected ? "已停用特殊核心" : "已启用特殊核心");
        }
    }

    static void DrawButtons()
    {
        if (GrowthUi.Button(Img + "main/", "button:close")) { Close(); return; }
        if (GrowthUi.Button(Img + "main/", "button:help"))
            MatrixPresentation.Help(false);
        if (GrowthUi.Button(Img + "main/", "button:specialCoreOpen") || GrowthUi.Button(Img + "main/", "button:specialCoreClose"))
        { specialOpen = !specialOpen; specialScroll = 0; }

        if (GrowthUi.Button(Img + "main/", "button:reset"))
        {
            string cost = VMatrixCatalog.ResetMeso.ToString("N0", CultureInfo.InvariantCulture);
            NativeUtilPrompt.Confirm("重置后返还已分配的 V 点数，并扣除 " + cost + " 枫币。继续？", ok =>
            {
                if (!ok) return;
                var response = Send(VMatrixOperation.Reset);
                if (response != null && response.ok) BottomBar.Toast("V 点数已重置");
            });
            return;
        }

        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && state != null && state.points > 0;
        if (GrowthUi.Button(Img + "main/", "button:transform")) { transformCount = 1; dialog = Dialog.Transform; }
        GUI.enabled = wasEnabled;
    }

    // —————————————— 强化弹窗（vSkillUpgradePopUp/enforcementAlert 340x491）——————————————

    static void DrawUpgrade()
    {
        var core = Find(dialogCore);
        if (core == null) { dialog = Dialog.None; return; }
        // 弹窗内部一律用 enforcementAlert 自己的坐标（按钮 origin 就是相对它的）。
        var outer = GrowthUi.Shift((Width - 340) / 2, (H - 491) / 2, 340, 491);
        GrowthUi.Image(Img + "vSkillUpgradePopUp/enforcementAlert/", "backgrnd", 0, 0);
        var icon = core.skills.Length > 0 ? WzAssets.Texture("skillicon/" + core.skills[0]) : null;
        if (icon != null) GUI.DrawTexture(GrowthUi.At(154, 70, icon.width, icon.height), icon);
        GrowthUi.Image(Img + "vSkillUpgradePopUp/enforcementAlert/", "layer:coreCover", 132, 43);

        int current = core.level;
        targetLevel = Mathf.Clamp(targetLevel, current, Mathf.Max(current, core.maxLevel));
        if (core.type == 2) targetLevel = 1;

        // 等级胶囊：左=当前，右=目标（原图胶囊就在 (119..221, 152..173)）。
        GrowthUi.ShadowText(Img + "vSkillUpgradePopUp/enforcementAlert/", current.ToString(CultureInfo.InvariantCulture),
            130, 155, 40, "font:levelCount", TextAnchor.MiddleRight, 14);
        GrowthUi.Text(Img + "vSkillUpgradePopUp/enforcementAlert/", "/", 172, 155, 12, "", TextAnchor.MiddleCenter, 14);
        GrowthUi.ShadowText(Img + "vSkillUpgradePopUp/enforcementAlert/", targetLevel.ToString(CultureInfo.InvariantCulture),
            186, 155, 40, "font:levelCount", TextAnchor.MiddleLeft, 14);

        long cost = CostOf(core, current, targetLevel);
        long have = state?.points ?? 0;
        GrowthUi.ShadowText(Img + "vSkillUpgradePopUp/enforcementAlert/", have.ToString("N0", CultureInfo.InvariantCulture),
            212, 283, 92, "font:levelCount", TextAnchor.MiddleRight, 14);
        var costColor = cost > have ? new Color(1f, .55f, .5f) : (Color?)null;
        GrowthUi.ShadowText(Img + "vSkillUpgradePopUp/enforcementAlert/", cost < 0 ? "—" : cost.ToString("N0", CultureInfo.InvariantCulture),
            212, 316, 92, "font:levelCount", TextAnchor.MiddleRight, 14, costColor);
        if (cost > have)
            GrowthUi.Text(Img + "vSkillUpgradePopUp/enforcementAlert/", "V 点数不足", 40, 350, 260, "",
                TextAnchor.UpperLeft, 13, new Color(1f, .7f, .6f));

        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && targetLevel < core.maxLevel && cost >= 0 && cost <= have;
        if (GrowthUi.Button(Img + "vSkillUpgradePopUp/enforcementAlert/", "button:levelPlus")) targetLevel++;
        GUI.enabled = wasEnabled && MaxAffordable(core) > targetLevel;
        if (GrowthUi.Button(Img + "vSkillUpgradePopUp/enforcementAlert/", "button:selectAll")) targetLevel = MaxAffordable(core);
        GUI.enabled = wasEnabled && targetLevel != current;
        if (GrowthUi.Button(Img + "vSkillUpgradePopUp/enforcementAlert/", "button:initialize")) targetLevel = current;
        GUI.enabled = wasEnabled && targetLevel > current && cost >= 0 && cost <= have;
        if (GrowthUi.Button(Img + "vSkillUpgradePopUp/enforcementAlert/", "button:confirm"))
        {
            var response = Send(VMatrixOperation.Upgrade, core: core.coreId, target: targetLevel);
            if (response != null && response.ok)
            {
                resultBefore = current;
                resultAfter = targetLevel;
                dialog = Dialog.Result;
            }
        }
        GUI.enabled = wasEnabled;
        if (GrowthUi.Button(Img + "vSkillUpgradePopUp/enforcementAlert/", "button:cancel")) dialog = Dialog.None;
        GrowthUi.Restore(outer);
    }

    // —————————————— 强化结果（vSkillUpgradePopUp/enforcementResult 340x351）——————————————
    // 282 的强化流程是「设等级 → 确认 → 结果窗里只有取消键」，没有二次确认。
    static void DrawResult()
    {
        var core = Find(dialogCore);
        if (core == null) { dialog = Dialog.None; return; }
        var outer = GrowthUi.Shift((Width - 340) / 2, (H - 351) / 2, 340, 351);
        GrowthUi.Image(Img + "vSkillUpgradePopUp/enforcementResult/", "backgrnd", 0, 0);
        var pos = Vector("vSkillUpgradePopUp/enforcementResult/vector:corePos", 175, 92);
        var icon = core.skills.Length > 0 ? WzAssets.Texture("skillicon/" + core.skills[0]) : null;
        if (icon != null)
            GUI.DrawTexture(GrowthUi.At(pos.x - icon.width / 2f, pos.y - icon.height / 2f, icon.width, icon.height), icon);
        GrowthUi.Image(Img + "vSkillUpgradePopUp/enforcementResult/", "layer:coreCover", 132, 43);
        var before = Vector("vSkillUpgradePopUp/enforcementResult/vector:levelBefore", 93, 229);
        var after = Vector("vSkillUpgradePopUp/enforcementResult/vector:levelAfter", 237, 229);
        GrowthUi.Text(Img + "vSkillUpgradePopUp/enforcementResult/", resultBefore.ToString(CultureInfo.InvariantCulture),
            before.x - 24, before.y - 9, 48, "font:levelBefore", TextAnchor.MiddleCenter, 18);
        GrowthUi.Text(Img + "vSkillUpgradePopUp/enforcementResult/", resultAfter.ToString(CultureInfo.InvariantCulture),
            after.x - 24, after.y - 9, 48, "font:levelAfter", TextAnchor.MiddleCenter, 18);
        if (GrowthUi.Button(Img + "vSkillUpgradePopUp/enforcementResult/", "button:cancel"))
        {
            selected = -1;
            dialog = Dialog.None;
            Load();
        }
        GrowthUi.Restore(outer);
    }

    static long CostOf(VMatrixCoreView core, int from, int to)
    {
        if (to <= from) return 0;
        if (core.type == 2) return VMatrixCatalog.RegisterSpecialCost;
        return VMatrixCatalog.CostBetween(core.coreId, from, to);
    }

    /// <summary>按当前 V 点能一口气升到几级（客户端预算，服务端仍复核）。</summary>
    static int MaxAffordable(VMatrixCoreView core)
    {
        if (core.type == 2) return 1;
        long points = state?.points ?? 0;
        int level = core.level;
        while (level < core.maxLevel)
        {
            int step = VMatrixCatalog.CostForLevel(core.coreId, level + 1);
            if (step < 0 || step > points) break;
            points -= step; level++;
        }
        return level;
    }

    // —————————————— 特殊核心登记（specialCorePopUp/register 342x361）——————————————

    static void DrawRegister()
    {
        var core = Find(dialogCore);
        if (core == null)
        {
            var definition = VMatrixCatalog.Core(dialogCore);
            if (definition == null) { dialog = Dialog.None; return; }
            core = new VMatrixCoreView
            { coreId = definition.id, type = definition.type, maxLevel = definition.maxLevel, name = definition.name, skills = definition.skills };
        }
        var outer = GrowthUi.Shift((Width - 342) / 2, (H - 361) / 2, 342, 361);
        GrowthUi.Image(Img + "specialCorePopUp/register/", "backgrnd", 0, 0);
        var icon = core.skills.Length > 0 ? WzAssets.Texture("skillicon/" + core.skills[0]) : null;
        if (icon != null) GUI.DrawTexture(GrowthUi.At(155, 96, icon.width, icon.height), icon);
        GrowthUi.ShadowText(Img + "specialCorePopUp/register/", core.name, 40, 150, 262, "",
            TextAnchor.MiddleCenter, 14);
        GrowthUi.ShadowText(Img + "specialCorePopUp/register/", VMatrixCatalog.RegisterSpecialCost.ToString(CultureInfo.InvariantCulture),
            200, 250, 90, "", TextAnchor.MiddleRight, 14);

        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && (state?.points ?? 0) >= VMatrixCatalog.RegisterSpecialCost;
        if (GrowthUi.Button(Img + "specialCorePopUp/register/", "button:apply"))
        {
            var response = Send(VMatrixOperation.Upgrade, core: core.coreId, target: 1);
            if (response != null && response.ok) { dialog = Dialog.None; BottomBar.Toast("特殊核心已登记"); }
        }
        GUI.enabled = wasEnabled;
        if (GrowthUi.Button(Img + "specialCorePopUp/register/", "button:cancel")) dialog = Dialog.None;
        GrowthUi.Restore(outer);
    }

    // —————————————— V 点转换（vPointTransform 374x446）——————————————

    static void DrawTransform()
    {
        var outer = GrowthUi.Shift((Width - 374) / 2, (H - 446) / 2, 374, 446);
        GrowthUi.Image(Img + "vPointTransform/", "backgrnd", 0, 0);
        if (state != null && state.transformItem != 0)
        {
            var item = WzAssets.Texture("itemicon/" + state.transformItem);
            if (item != null) GUI.DrawTexture(GrowthUi.At(243, 211, item.width, item.height), item);
        }
        int maxCount = MaxTransform();
        transformCount = Mathf.Clamp(transformCount, 1, Mathf.Max(1, maxCount));
        GrowthUi.ShadowText(Img + "vPointTransform/", (state?.points ?? 0).ToString("N0", CultureInfo.InvariantCulture),
            200, 150, 95, "font:myVPoint", TextAnchor.MiddleRight, 14);
        GrowthUi.ShadowText(Img + "vPointTransform/", (transformCount * (state?.transformCost ?? 0)).ToString(CultureInfo.InvariantCulture),
            84, 250, 60, "font:EA", TextAnchor.MiddleRight, 14);
        GrowthUi.ShadowText(Img + "vPointTransform/", transformCount.ToString(CultureInfo.InvariantCulture),
            228, 250, 60, "font:EA", TextAnchor.MiddleRight, 14);
        GrowthUi.Text(Img + "vPointTransform/", transformCount.ToString(CultureInfo.InvariantCulture),
            84, 296, 120, "", TextAnchor.MiddleCenter, 14);

        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && transformCount > 1;
        if (GrowthUi.Button(Img + "vPointTransform/", "button:transformCountDown")) transformCount--;
        GUI.enabled = wasEnabled && transformCount < maxCount;
        if (GrowthUi.Button(Img + "vPointTransform/", "button:transformCountUP")) transformCount++;
        if (GrowthUi.Button(Img + "vPointTransform/", "button:transformCountMax")) transformCount = Mathf.Max(1, maxCount);
        GUI.enabled = wasEnabled && maxCount > 0;
        if (GrowthUi.Button(Img + "vPointTransform/", "button:confirm"))
        {
            var response = Send(VMatrixOperation.Transform, count: transformCount);
            if (response != null && response.ok)
            {
                dialog = Dialog.None;
                BottomBar.Toast("已消耗 " + (transformCount * state.transformCost) + " V 点，兑换 " + transformCount + " 颗核心宝石");
            }
        }
        GUI.enabled = wasEnabled;
        if (GrowthUi.Button(Img + "vPointTransform/", "button:close")) dialog = Dialog.None;
        GrowthUi.Restore(outer);
    }

    static int MaxTransform()
    {
        if (state == null || state.transformCost <= 0) return 0;
        return (int)Math.Min(9999, state.points / state.transformCost);
    }

    // —————————————— 核心宝石使用（coreGemStoneOpen/open 285x470）——————————————

    static void DrawGemstone()
    {
        var outer = GrowthUi.Shift((Width - 285) / 2, (H - 470) / 2, 285, 470);
        GrowthUi.Image(Img + "coreGemStoneOpen/open/", "backgrnd", 0, 0);
        var gem = VMatrixCatalog.Gemstone(gemItem);
        int have = CountGemstone(gemItem);
        gemCount = Mathf.Clamp(gemCount, 1, Mathf.Max(1, have));
        if (gem != null)
        {
            GrowthUi.ShadowText(Img + "coreGemStoneOpen/open/", gemCount.ToString(CultureInfo.InvariantCulture),
                92, 130, 62, "font:coreCountInfo", TextAnchor.MiddleRight, 14);
            GrowthUi.ShadowText(Img + "coreGemStoneOpen/open/", (state?.points ?? 0).ToString("N0", CultureInfo.InvariantCulture),
                150, 301, 80, "font:vPointInfo", TextAnchor.MiddleRight, 14);
            // 首颗如果开核心就不产点，预览按「首颗开核心」口径算。
            int pointGems = gemCount;
            bool opensCore = gem.openCore != 0 && Find(gem.openCore) == null;
            if (opensCore && pointGems > 0) pointGems--;
            GrowthUi.ShadowText(Img + "coreGemStoneOpen/open/", (pointGems * gem.points).ToString(CultureInfo.InvariantCulture),
                151, 332, 80, "font:corevPointInfo", TextAnchor.MiddleRight, 14);
            GrowthUi.Text(Img + "coreGemStoneOpen/open/", gemCount.ToString(CultureInfo.InvariantCulture),
                139, 198, 48, "", TextAnchor.MiddleCenter, 14);
        }
        else
        {
            GrowthUi.Text(Img + "coreGemStoneOpen/open/", "这不是核心宝石", 40, 180, 200, "",
                TextAnchor.MiddleCenter, 14, new Color(1f, .6f, .55f));
        }

        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && gemCount > 1;
        if (GrowthUi.Button(Img + "coreGemStoneOpen/open/", "button:Down")) gemCount--;
        GUI.enabled = wasEnabled && gemCount < have;
        if (GrowthUi.Button(Img + "coreGemStoneOpen/open/", "button:UP")) gemCount++;
        if (GrowthUi.Button(Img + "coreGemStoneOpen/open/", "button:All")) gemCount = Mathf.Max(1, have);
        GUI.enabled = wasEnabled && gem != null && have > 0;
        if (GrowthUi.Button(Img + "coreGemStoneOpen/open/", "button:Confirm"))
        {
            var response = Send(VMatrixOperation.UseGemstone, item: gemItem, count: gemCount);
            if (response != null && response.ok) { dialog = Dialog.None; BottomBar.Toast("核心宝石已使用"); }
        }
        GUI.enabled = wasEnabled;
        if (GrowthUi.Button(Img + "coreGemStoneOpen/open/", "button:Cancel")) dialog = Dialog.None;
        GrowthUi.Restore(outer);
    }

    static int CountGemstone(int itemId)
    {
        if (itemId == 0) return 0;
        int total = 0;
        var inventory = GameSession.Character?.inventory;
        if (inventory == null) return 0;
        foreach (var item in inventory)
            if (item.itemId == itemId && item.position > 0) total += item.quantity;
        return total;
    }
}
