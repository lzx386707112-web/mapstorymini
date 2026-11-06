// HEXA 矩阵窗口（282 UI/HexaMatrixUI.img）。几何全部从资源实测，不抄文档：
//   main/backgrnd 862x624 @(0,0)；关闭 (822,10)、帮助 (801,10)、索尔艾尔达兑换 (752,64)；
//   tab:mainTab 两页 176x35 落在 (20,50)/(200,50)，lt(20,46) rb(384,87)；
//   子窗区 main/vector:childwnd_lt(20,87) / childwnd_rb(850,603) → 830x516，
//   两页的槽位坐标都是「相对这个子窗区」的，所以整页用 GrowthUi.Shift(20,87,830,516) 画。
//
// 技能页 hexaSkill：18 槽（skill 6 / mastery 4 / enforce 4 / common 4），
//   右侧详情层 310x364 @(512,142) 共 12 种状态（激活×3、激活带前置×3、
//   强化 couple/单主/单副、强化完成）；底部 activation(2007)/enforcement(2008) 在 (593,455)。
// 属性页 hexaStat：6 槽 = 三档属性核心 × 两套预设；右侧未激活走 activation_wnd(496x506)、
//   已激活走 enforcement_wnd(489x506)，wnd 里两颗 tab:coreSlot 就是「强化中 / 保存中」。
//
// 数据只有一份：等级、气息、碎片、是否准入全来自 HexaResponse；客户端只多算一件
// 事——「这一级要多少气息/碎片」，而且走 NativeHexaMatrixService.UpgradeCost 这同一份表，
// 不在 UI 里再抄一遍常量（282 最招骂的 bug 就是窗口显示 3、服务端扣 4）。
using System;
using System.Collections.Generic;
using System.Globalization;
using MapleDash.Wz;
using UnityEngine;

public static class NativeHexaMatrixWindow
{
    const string Img = "UI/HexaMatrixUI.img/";
    const float W = 862f, H = 624f;
    // 子窗区（两页共同坐标系）
    const float ChildX = 20f, ChildY = 87f, ChildW = 830f, ChildH = 516f;
    // 页签
    const float TabX = 20f, TabY = 50f, TabW = 176f, TabH = 35f, TabSpace = 4f;
    // 详情层
    const float DetailX = 512f, DetailY = 142f, DetailW = 310f, DetailH = 364f;
    // 属性页右侧两个子窗（贴右边界：496/489 宽）
    // 两者都由 hexaStat/vector:childwnd2_lt (332,1) 定位——这是相对 hexaStat 底图的坐标，
    // 之前 activation 写成 326、enforce 写成 333，右侧整块左右抖 1~6px。
    const float ActivationX = 332f, ActivationY = 1f, ActivationW = 496f, ActivationH = 506f;
    const float EnforceX = 332f, EnforceY = 1f, EnforceW = 489f, EnforceH = 506f;

    enum Page { Skill = 0, Stat = 1 }
    enum Dialog { None, Upgrade, Transform, StatActivation, StatChange, SkillResult, StatResult }
    static HexaStatView resultStatBefore, resultStatAfter;
    static bool resultActivation;
    static int resultBefore, resultAfter;
    static readonly string[] StatNames={"爆擊傷害增加","Boss傷害增加","增加無視防禦率","傷害增加","攻擊力提升","魔力增加","主要能力值增加"};
    static readonly int[] choices=new int[3];
    static int dropdown=-1,changeCore,changePreset;

    // 技能页四段的槽位数（282 固定 18 格；我们的职业组只有 2 技能 + 3 共用核心，
    // 剩下的格子留空但照原样画出来，环状布局才是对的）。
    static readonly string[] Sections = { "skill", "mastery", "enforce", "common" };
    // 段 → 服务端的 group 序号（0 mastery / 1 enforce / 2 skill / 3 common）
    static readonly int[] SectionGroup = { 2, 0, 1, 3 };
    static readonly int[] SectionSlots = { 6, 4, 4, 4 };

    static readonly int[] TransformItems = { NativeHexaMatrixService.FaintSolErda, NativeHexaMatrixService.HexaBooster };
    static readonly string[] TransformNames = { "淡灵魂艾尔达", "HEXA Booster" };

    static bool visible, dragging, positioned, miniOpen;
    static Vector2 position, grab;
    static int owner, selectedCore, statCore, preset, transformTab, transformCount = 1;
    static Page page;
    static Dialog dialog;
    static int upgradeTarget;
    static HexaResponse state;
    static string error;

    public static bool Visible => visible;
    public static bool AtPointer => visible && (dragging || Pointer.HitsWindow(ModalRect()));

    static float S => Mathf.Min(BottomBar.S, Mathf.Min((UiKit.W - 24) / W, UiKit.H * .92f / (dialog==Dialog.StatActivation ? 699f : dialog==Dialog.StatChange ? 688f : H)));
    static Vector2 Free
    {
        get
        {
            if(!positioned) return new Vector2((UiKit.W/S-W)/2,(UiKit.H/S-H)/2);
            float extra=(dialog==Dialog.StatActivation ? 699 : dialog==Dialog.StatChange ? 688 : H)-H;
            var offset=new Vector2(0,extra/2);
            return NativeUi.ClampWindowPosition(position-offset,new Vector2(W,H+extra),S)+offset;
        }
    }
    static Rect WindowRect() => new Rect(Free.x * S, Free.y * S, W * S, H * S);

    static Rect ModalRect()
    {
        var r=WindowRect();
        if(dialog==Dialog.StatActivation || dialog==Dialog.StatChange)
        {
            float height=(dialog==Dialog.StatActivation ? 699f : 688f)*S;
            return new Rect(r.x,r.y+(r.height-height)/2,r.width,height);
        }
        return r;
    }

    public static void Escape()
    {
        if (dialog != Dialog.None) { dialog = Dialog.None; return; }
        // 索尔艾尔达迷你窗是主窗内部的一层，Esc 只收它，不连主窗一起关。
        if (miniOpen) { miniOpen = false; return; }
        Close();
    }

    public static void Close()
    {
        visible = dragging = positioned = miniOpen = false;
        state = null; error = null; selectedCore = 0; dialog = Dialog.None;
    }

    public static void Open()
    {
        Close();
        if (!GameSession.InGame) return;
        visible = true; owner = GameSession.CharacterId;
        Load();
    }

    static void Load()
    {
        var response = GameSession.Net.Hexa(new HexaRequest { characterId = owner, operation = HexaOperation.Snapshot });
        if (response == null) return;
        state = response;
        error = response.eligible ? null : response.error;
        if (selectedCore != 0 && CoreOf(selectedCore) == null) selectedCore = 0;
        if (state.stats != null && StatOf(statCore, preset) == null && StatOf(statCore, 1 - preset) != null)
            preset = 1 - preset;
        // 核心等级 → 包装技能（500004xxx）等级：HEXA 强化的 damR_6th 靠这条链生效，
        // 而且刚升完级还没重拉角色数据时也能立刻算进伤害。
        VMatrixLevels.CaptureHexa(response, owner);
    }

    static HexaResponse Send(HexaOperation operation, int core = 0, int target = 0, int presetIndex = 0,
        int stat1 = 0, int stat2 = 0, int stat3 = 0, int weight = 0, bool commit = true)
    {
        var response = GameSession.Net.Hexa(new HexaRequest
        {
            characterId = owner, operation = operation, coreId = core, level = 0, targetLevel = target,
            stat1 = stat1, stat2 = stat2, stat3 = stat3, weight = weight, preset = presetIndex, commit = commit,
        });
        if (response == null) return null;
        if (!response.ok) { error = response.error; BottomBar.Toast(response.error); return response; }
        error = null;
        if (commit) { state = response; GameSession.ReloadAfterCommand(); VMatrixLevels.CaptureHexa(response, owner); }
        return response;
    }

    // —————————————— 数据小工具 ——————————————

    static HexaCoreView CoreOf(int coreId)
    {
        if (state?.cores == null || coreId == 0) return null;
        foreach (var core in state.cores) if (core.coreId == coreId) return core;
        return null;
    }

    /// <summary>技能页某一段的槽位表（服务端只回本职业组里的核心，空的格子由窗口补）。</summary>
    static List<HexaCoreView> Section(int sectionIndex)
    {
        var list = new List<HexaCoreView>();
        int group = SectionGroup[sectionIndex];
        if (state?.cores != null) foreach (var core in state.cores) if (core.group == group) list.Add(core);
        return list;
    }

    static HexaStatView StatOf(int coreId, int presetIndex)
    {
        if (state?.stats == null || coreId == 0) return null;
        foreach (var stat in state.stats) if (stat.coreId == coreId && stat.preset == presetIndex) return stat;
        return null;
    }

    static HexaStatView CurrentStat() => StatOf(statCore, preset);

    static bool Eligible => state != null && state.eligible;

    static Vector2 Vector(string path, float fallbackX, float fallbackY)
    {
        var node = WzAssets.Node(Img + path);
        return node != null && node.TryVector("", out int x, out int y)
            ? new Vector2(x, y) : new Vector2(fallbackX, fallbackY);
    }

    static List<Vector2> SlotPositions(string section)
    {
        var list = new List<Vector2>();
        for (int i = 0; i < 8; i++)
        {
            var node = WzAssets.Node(Img + "hexaSkill/slotPos/" + section + "/" + i);
            if (node == null || !node.TryVector("", out int x, out int y)) break;
            list.Add(new Vector2(x, y));
        }
        return list;
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
        // 抓手 = 整条标题栏（排除右上关闭/帮助）：HEXA 标题栏到 46px，页签从 50 起；
        // slop 让整块面板都能抓（按下后走 6px 才转成拖动，点槽位/页签不受影响）。
        if (dialog == Dialog.None && GrowthUi.Drag(ref dragging, ref positioned, ref position, ref grab, W, H, WindowRect, 46f, 100f,
                owner: typeof(NativeHexaMatrixWindow), slop: 6f))
            window = WindowRect();
        GrowthUi.R = window;
        NativeUi.RegisterWindow("NativeHexaMatrixWindow", ModalRect(), () => visible);

        MatrixPresentation.Begin();
        bool parentEnabled = GUI.enabled;
        // 弹窗覆盖期间只绘制主面板，底层槽位、页签和按钮不得消费弹窗点击。
        GUI.enabled = parentEnabled && dialog == Dialog.None;
        GrowthUi.Image(Img + "main/", "backgrnd", 0, 0);
        DrawTabs();
        DrawHeaderButtons();

        // 两条资源计数在主窗坐标系：hexaSkill/vector:mainCount(608,92)、subCount(726,92) 都超过
        // 子窗宽度 506，是相对主窗左上角给的（原端把它们和 tab 画在同一层）。
        DrawResources();

        var outer = GrowthUi.Shift(ChildX, ChildY, ChildW, ChildH);
        if (page == Page.Skill) DrawSkillPage(); else DrawStatPage();
        GrowthUi.Restore(outer);

        // 详情层 310x364 @(512,142) 也是主窗坐标（同 vector:descLT(540,196)），
        // 之前跟着子窗 Shift 一起画，整块右下偏了 (20,87)。
        if (page == Page.Skill) DrawSkillDetail();

        if (miniOpen) DrawMini();

        if (dialog != Dialog.None) MatrixPresentation.Begin();
        GUI.enabled = parentEnabled;
        switch (dialog)
        {
            case Dialog.Upgrade: DrawUpgrade(); break;
            case Dialog.SkillResult: DrawSkillResult(); break;
            case Dialog.StatResult: DrawStatResult(); break;
            case Dialog.Transform: DrawTransform(); break;
            case Dialog.StatActivation: DrawStatSelection(false); break;
            case Dialog.StatChange: DrawStatSelection(true); break;
        }

        MatrixPresentation.Draw();
        if (ownsPointer && GrowthUi.Repaint)
        {
            Pointer.ConsumeClick();
            Pointer.Pressed = Pointer.RightPressed = Pointer.RightReleased = false;
        }

        if (!string.IsNullOrEmpty(error))
        {
            GrowthUi.Plate(20, 592, 830, 20, .82f);
            GrowthUi.Text(Img + "main/", error, 24, 594, 820, "", TextAnchor.UpperLeft, 13,
                new Color(1f, .62f, .55f));
        }
    }

    static void DrawTabs()
    {
        for (int i = 0; i < 2; i++)
        {
            bool on = (int)page == i;
            GrowthUi.Image(Img + "main/tab:mainTab/", (on ? "selected/" : "normal/") + i, TabX + i * (TabW + TabSpace), TabY);
            if (!Interactive()) continue;
            if (!GrowthUi.At(TabX + i * (TabW + TabSpace), TabY, TabW, TabH).Contains(Pointer.Pos)) continue;
            if (!Pointer.Clicked) continue;
            Pointer.ConsumeClick();
            page = (Page)i;
            dialog = Dialog.None;
        }
    }

    static void DrawHeaderButtons()
    {
        if (GrowthUi.Button(Img + "main/", "button:close")) { Close(); return; }
        if (GrowthUi.Button(Img + "main/", "button:help"))
            MatrixPresentation.Help(true);
        if (GrowthUi.Button(Img + "main/", "button:solErdaTransform"))
        {
            if (!Eligible) BottomBar.Toast(error ?? "尚未解锁 HEXA 矩阵。");
            else { transformTab = 0; transformCount = 1; dialog = Dialog.Transform; }
        }
    }

    // —————————————— 技能页 ——————————————

    static void DrawSkillPage()
    {
        GrowthUi.Image(Img + "hexaSkill/", "backgrnd", 0, 0);
        // 18 个六边形槽的底盘是独立图层：layer:slot 446x462 origin(-30,-22) → (30,22)。
        // backgrnd 里只有紫色光效，不画这一层就整页空着（之前就是这样）。
        GrowthUi.Image(Img + "hexaSkill/", "layer:slot", 30, 22);
        for (int section = 0; section < Sections.Length; section++)
        {
            var slots = SlotPositions(Sections[section]);
            var cores = Section(section);
            for (int index = 0; index < slots.Count && index < SectionSlots[section]; index++)
            {
                var core = index < cores.Count ? cores[index] : null;
                DrawSkillSlot(section, slots[index], core);
            }
        }
    }

    static void DrawSkillSlot(int section, Vector2 anchor, HexaCoreView core)
    {
        // 没有核心的格子不画：282 的环（hexaSkill/layer:slot）本身就是空的，
        // 只有本职业组真正存在的核心才有卡框。
        if (core == null) return;
        string sectionName = Sections[section];
        // 68x78 的卡框原点 (34,39)：锚点是卡框中心。
        GrowthUi.Image(Img + "core/coreBackground/", sectionName, anchor.x - 34, anchor.y - 39);
        float left = anchor.x - 34, top = anchor.y - 39;

        var iconPos = Vector("core/vector:iconPos", 18, 27);
        var icon = MatrixCoreIcons.Core(core.coreId, true, core.skills);
        if (icon != null) GUI.DrawTexture(GrowthUi.At(left + iconPos.x, top + iconPos.y, icon.width, icon.height), icon);
        if (core.level > 0)
            GrowthUi.ShadowText(Img + "core/", core.level.ToString(CultureInfo.InvariantCulture),
                left + 8, top + 54, 52, "font:level", TextAnchor.MiddleCenter, 12);

        // 状态层：未激活可激活 → activatable 光环；未解锁 → unreleased 压暗；选中 → selected 框。
        if (core.level == 0 && Eligible)
            MatrixPresentation.Animation(Img + "core/coreState/activatable/", anchor.x, anchor.y);
        else if (!Eligible)
            GrowthUi.Image(Img + "core/coreState/unreleased/", sectionName, left, top);
        if (core.coreId == selectedCore)
            MatrixPresentation.Animation(Img + "core/coreState/selected/", anchor.x, anchor.y);

        if (!Interactive()) return;
        bool hover = GrowthUi.At(left, top, 68, 78).Contains(Pointer.Pos);
        // 与 V 矩阵同口径：核心 id 才是权威，服务端快照里的 name/desc/skills 可能是旧数据。
        // 早期这里直接用 core.skills，tooltip 就可能指向另一条技能（圣十字架显示灵魂蚀日）。
        if (hover)
        {
            var canonical = HexaCatalog.Core(core.coreId);
            MatrixPresentation.Skill(canonical != null ? canonical.skills : core.skills, core.level,
                canonical != null ? canonical.name : core.name,
                canonical != null ? canonical.desc : core.desc);
        }
        if (hover) GrowthUi.Veil(left, top, 68, 78, new Color(1f, 1f, 1f, .14f));
        if (!hover || !Pointer.Clicked) return;
        Pointer.ConsumeClick();
        selectedCore = core.coreId;
        upgradeTarget = 0;
    }

    static void DrawSkillDetail()
    {
        var core = CoreOf(selectedCore);
        if (core == null) { GrowthUi.Image(Img + "hexaSkill/", "layer:default", DetailX, DetailY); return; }
        GrowthUi.Image(Img + "hexaSkill/", DetailLayer(core), DetailX, DetailY);

        // 强化完成后在底部叠一条 283x144 的「已满级」条（原图就在 (525,349)）。
        bool maxed = core.level >= core.maxLevel && core.level > 0;
        if (maxed) GrowthUi.Image(Img + "hexaSkill/", "layer:enforceCompleted", 525, 349);

        var descLT = Vector("hexaSkill/vector:descLT", 540, 196);
        var nameSpace = Vector("hexaSkill/vector:nameSpace", 2, 7);
        GrowthUi.ShadowText(Img + "hexaSkill/", core.name, descLT.x + nameSpace.x, descLT.y + nameSpace.y,
            195, "font:skillName");

        string desc = string.IsNullOrEmpty(core.desc) ? "（該核心沒有說明。）" : core.desc;
        GrowthUi.TextWrapped(Img + "hexaSkill/", desc, descLT.x, descLT.y + 24, 235, 110, "font:skillDesc");

        // 费用：未激活看激活价，已激活看下一级价；couple 核心（main+sub）分两列。
        int solErdaCost, fragmentCost;
        if (core.level == 0) NativeHexaMatrixService.ActivateCost(core.group, out solErdaCost, out fragmentCost);
        else NativeHexaMatrixService.UpgradeCost(core.group, core.level, out solErdaCost, out fragmentCost);
        bool couple = core.skills != null && core.skills.Length > 1;
        var mainCount = couple ? Vector("hexaSkill/vector:coupleIngredientMainCount", 609, 411)
            : Vector("hexaSkill/vector:singleIngredientCount", 667, 411);
        var subCount = Vector("hexaSkill/vector:coupleIngredientSubCount", 725, 411);
        var short2 = (state?.solErda ?? 0) < solErdaCost ? new Color(1f, .55f, .5f) : (Color?)null;
        var short3 = (state?.fragments ?? 0) < fragmentCost ? new Color(1f, .55f, .5f) : (Color?)null;
        GrowthUi.ShadowText(Img + "hexaSkill/", solErdaCost.ToString(CultureInfo.InvariantCulture),
            mainCount.x - 40, mainCount.y - 8, 40, "font:ingredient", TextAnchor.MiddleRight, 12, short2);
        if (couple)
            GrowthUi.ShadowText(Img + "hexaSkill/", fragmentCost.ToString(CultureInfo.InvariantCulture),
                subCount.x - 40, subCount.y - 8, 40, "font:ingredient", TextAnchor.MiddleRight, 12, short3);
        else
            GrowthUi.ShadowText(Img + "hexaSkill/", fragmentCost.ToString(CultureInfo.InvariantCulture),
                mainCount.x + 28, mainCount.y - 8, 40, "font:ingredient", TextAnchor.MiddleRight, 12, short3);

        bool wasEnabled = GUI.enabled;
        if (core.level == 0)
        {
            GUI.enabled = wasEnabled && Eligible;
            if (GrowthUi.Button(Img + "hexaSkill/", "button:activation"))
            {
                var response = Send(HexaOperation.Activate, core: core.coreId);
                if (response != null && response.ok)
                { resultActivation = true; resultBefore = 0; resultAfter = 1; dialog = Dialog.SkillResult; }
            }
        }
        else
        {
            GUI.enabled = wasEnabled && Eligible && !maxed;
            if (GrowthUi.Button(Img + "hexaSkill/", "button:enforcement"))
            {
                upgradeTarget = Mathf.Clamp(core.level + 1, core.level + 1, core.maxLevel);
                dialog = Dialog.Upgrade;
            }
        }
        GUI.enabled = wasEnabled;

        // 索尔艾尔达小窗按钮（2009）在右下角。
        if (GrowthUi.Button(Img + "hexaSkill/", "button:solErdaMini")) miniOpen = !miniOpen;
    }

    /// <summary>右侧详情层 12 种状态里挑一个（282 按「是否激活 / 是否 couple / 是否有前置」分）。</summary>
    static string DetailLayer(HexaCoreView core)
    {
        bool couple = core.skills != null && core.skills.Length > 1;
        bool hasReq = HexaCatalog.Core(core.coreId)?.reqForActivation is int[] req && req.Length > 0;
        if (core.level > 0)
            // 满级不换底图，只在底部叠 layer:enforceCompleted（283x144 的独立画布）。
            return couple ? "layer:enforceCouple" : "layer:enforceSingleMain";
        if (hasReq)
            return couple ? "layer:activateCoupleWithReq" : "layer:activateSingleMainWithReq";
        return couple ? "layer:activateCouple" : "layer:activateSingleMain";
    }

    /// <summary>「索尔艾尔达 / 碎片」两条计数（两页共用同一组锚点）。</summary>
    static void DrawResources()
    {
        if (state == null) return;
        var main = Vector("hexaSkill/vector:mainCount", 608, 92);
        var sub = Vector("hexaSkill/vector:subCount", 726, 92);
        // 这两条数字底下原本压着 main/incMainSolErdaEffect 的气泡动画（导入时按特效丢了），
        // 底图这里是空的，所以自己补一块暗底 + 标签，否则纯数字飘在星空上看不清。
        var label = new Color(.72f, .8f, .9f);
        GrowthUi.Plate(main.x - 104, main.y - 13, 104, 18, .55f);
        GrowthUi.Plate(sub.x - 104, sub.y - 13, 104, 18, .55f);
        GrowthUi.Text(Img + "hexaSkill/", "艾爾達", main.x - 100, main.y - 11, 48, "", TextAnchor.UpperLeft, 12, label);
        GrowthUi.Text(Img + "hexaSkill/", "碎片", sub.x - 100, sub.y - 11, 48, "", TextAnchor.UpperLeft, 12, label);
        GrowthUi.ShadowText(Img + "hexaSkill/", state.solErda.ToString("N0", CultureInfo.InvariantCulture),
            main.x - 96, main.y - 11, 90, "font:invenCount", TextAnchor.UpperRight, 12);
        GrowthUi.ShadowText(Img + "hexaSkill/", state.fragments.ToString("N0", CultureInfo.InvariantCulture),
            sub.x - 96, sub.y - 11, 90, "font:invenCount", TextAnchor.UpperRight, 12);
    }

    // —————————————— 索尔艾尔达小窗（158x58） ——————————————

    static void DrawMini()
    {
        // 原图的 popUpPos (858,0) 是把小窗弹到主窗右侧外面（真端屏幕够宽）。
        // 这里贴在索尔艾尔达按钮左下方，保证小屏也看得见，叠在详情层之上。
        const float x = 649f, y = 196f;
        GrowthUi.Image(Img + "solErdaMini/", "backgrnd", x, y);
        GrowthUi.Image(Img + "solErdaMini/", "layer:iconMain", x + 24, y + 3);
        GrowthUi.Image(Img + "solErdaMini/", "layer:iconSub", x + 97, y + 5);
        GrowthUi.ShadowText(Img + "solErdaMini/", (state?.solErda ?? 0).ToString(CultureInfo.InvariantCulture),
            x + 24, y + 30, 32, "", TextAnchor.MiddleCenter, 12);
        GrowthUi.ShadowText(Img + "solErdaMini/", (state?.fragments ?? 0).ToString(CultureInfo.InvariantCulture),
            x + 99, y + 30, 40, "", TextAnchor.MiddleCenter, 12);
        GrowthUi.Image(Img + "solErdaMini/", "layer:gauge", x + 10, y + 40);
        GrowthUi.Image(Img + "solErdaMini/", "layer:gaugeMax", x + 10, y + 40);
        float ratio = state == null || state.solErdaGaugeMax <= 0 ? 0f
            : Mathf.Clamp01(state.solErdaGauge / (float)state.solErdaGaugeMax);
        if (ratio < 1f) GrowthUi.Veil(x + 10 + 65 * ratio, y + 40, 65 * (1 - ratio), 7, new Color(0f, 0f, 0f, .55f));
        GrowthUi.ShadowText(Img + "solErdaMini/", state?.solErdaGauge.ToString(CultureInfo.InvariantCulture) ?? "0",
            x + 8, y + 42, 26, "font:gauge", TextAnchor.MiddleRight, 10);
        GrowthUi.Image(Img + "solErdaMini/", "layer:gaugeSlash", x + 39, y + 40);
        GrowthUi.ShadowText(Img + "solErdaMini/", state?.solErdaGaugeMax.ToString(CultureInfo.InvariantCulture) ?? "0",
            x + 40, y + 42, 26, "font:gauge", TextAnchor.MiddleLeft, 10);
        if (GrowthUi.ButtonAt(Img + "solErdaMini/", "button:close", GrowthUi.At(x + 144, y + 7, 7, 7))) miniOpen = false;
    }

    // —————————————— 属性页 ——————————————

    static void DrawStatPage()
    {
        GrowthUi.Image(Img + "hexaStat/", "backgrnd", 0, 0);

        var stats = HexaCatalog.AllStats;
        for (int slot = 0; slot < 6; slot++)
        {
            var node = WzAssets.Node(Img + "hexaStat/slotPos/" + slot);
            if (node == null || !node.TryVector("", out int sx, out int sy)) break;
            var anchor = new Vector2(sx, sy);
            int index = slot / 2, slotPreset = slot % 2;
            var definition = index < stats.Count ? stats[index] : null;
            var view = definition == null ? null : StatOf(definition.id, slotPreset);
            DrawStatSlot(anchor, definition, view, slotPreset);
        }

        DrawStatBoard(stats);
        if (statCore == 0 && stats.Count > 0) { statCore = stats[0].id; preset = 0; }
        if (HasStatActive(statCore)) DrawEnforcementWindow();
        else DrawActivationWindow();
    }

    static bool IsStatActive(int coreId, int presetIndex)
    {
        var view = StatOf(coreId, presetIndex);
        return view != null;
    }

    static void DrawStatSlot(Vector2 anchor, HexaStatCore definition, HexaStatView view, int slotPreset)
    {
        float left = anchor.x - 34, top = anchor.y - 39;
        GrowthUi.Image(Img + "core/coreBackground/", "stat", left, top);
        if (view != null && view.level > 0)
            GrowthUi.ShadowText(Img + "core/", "Lv" + view.level.ToString(CultureInfo.InvariantCulture),
                left + 4, top + 56, 60, "", TextAnchor.MiddleCenter, 12);
        if (definition == null || !Eligible || definition.reqLevel > (state?.level ?? 0))
            GrowthUi.Image(Img + "core/coreState/unreleased/", "stat", left, top);
        if (definition != null && definition.id == statCore && slotPreset == preset)
            MatrixPresentation.Animation(Img + "core/coreState/selected/", anchor.x, anchor.y);

        if (!Interactive()) return;
        bool hover = GrowthUi.At(left, top, 68, 78).Contains(Pointer.Pos);
        if (hover) GrowthUi.Veil(left, top, 68, 78, new Color(1f, 1f, 1f, .14f));
        if (hover) MatrixPresentation.Hover(StatHint(view));
        if (!hover || !Pointer.Clicked || definition == null) return;
        Pointer.ConsumeClick();
        statCore = definition.id;
        preset = slotPreset;
    }

    /// <summary>statBoard/layer:statBoard 298x182 落在 (15,312)，坐标 vector:stat (20,57) 相对它。</summary>
    static void DrawStatBoard(List<HexaStatCore> stats)
    {
        GrowthUi.Image(Img + "hexaStat/statBoard/", "layer:statBoard", 15, 312);
        int[] totals=new int[7];
        if(state?.stats!=null) foreach(var view in state.stats)
        {
            if(!view.applied || view.level!=view.level1+view.level2+view.level3) continue;
            if(!NativeHexaMatrixService.ValidStats(view.stat1,view.stat2,view.stat3)) continue;
            totals[view.stat1]+=HexaCatalog.StatValue(view.stat1,view.level1,true);
            totals[view.stat2]+=HexaCatalog.StatValue(view.stat2,view.level2,false);
            totals[view.stat3]+=HexaCatalog.StatValue(view.stat3,view.level3,false);
        }
        for(int i=0;i<6;i++)
        {
            int type=i==4 ? (HexaCatalog.MainStat(state.job)=="INT" ? 5 : 4) : i==5 ? 6 : i;
            int amount=type==6 ? HexaCatalog.MainStatValue(totals[type],state.job) : totals[type];
            string value=type<=3 ? (amount/100f).ToString("0.##")+"%" : amount.ToString();
            GrowthUi.Text(Img+"hexaStat/statBoard/",StatNames[type],35,369+i*18,200,"font:stat");
            GrowthUi.Text(Img+"hexaStat/statBoard/","+"+value,235,369+i*18,65,"font:stat",TextAnchor.UpperRight);
        }
    }

    // 未激活 → activation_wnd（496x506，只有一颗激活键）
    // 这一页的版面（两张空卡、「沒有儲存的屬性核心」、MAIN/ADDITIONAL STAT 的三条锁胶囊、
    // 「為了強化核心請完成開放」）全部烘焙在 backgrnd 里，客户端只补一条解锁等级和按钮；
    // 之前在这里自造了名称/说明/等级三段文字，正好压在底图的卡片与 MAIN STAT 上，看着就是「字叠字」。
    static void DrawActivationWindow()
    {
        var outer = GrowthUi.Shift(ActivationX, ActivationY, ActivationW, ActivationH);
        GrowthUi.Image(Img + "hexaStat/activation_wnd/", "backgrnd", 0, 0);
        var definition = HexaCatalog.Stat(statCore);
        if (definition != null)
            GrowthUi.ShadowText(Img + "hexaStat/activation_wnd/",
                "Lv." + definition.reqLevel + " 解鎖（目前 " + (state?.level ?? 0) + "）", 0, 429, 496, "",
                TextAnchor.MiddleCenter, 12, new Color(.72f, .8f, .9f));

        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && Eligible && definition != null
                      && (state?.level ?? 0) >= definition.reqLevel
                      && (definition.reqCore == 0 || HasStatActive(definition.reqCore));
        if (GrowthUi.Button(Img + "hexaStat/activation_wnd/", "button:activation"))
        {
            preset=0; choices[0]=choices[1]=choices[2]=-1; dropdown=-1;
            dialog=Dialog.StatActivation;
        }
        GUI.enabled = wasEnabled;
        GrowthUi.Restore(outer);
    }

    static bool HasStatActive(int coreId)
    {
        for (int i = 0; i < 2; i++) if (IsStatActive(coreId, i)) return true;
        return false;
    }

    // 已激活 → enforcement_wnd（489x506：两颗预设卡 + 强化/套用/保存/重置）
    static void DrawEnforcementWindow()
    {
        var outer = GrowthUi.Shift(EnforceX, EnforceY, EnforceW, EnforceH);
        GrowthUi.Image(Img + "hexaStat/enforcement_wnd/", "backgrnd", 0, 0);
        GrowthUi.Image(Img + "hexaStat/enforcement_wnd/", preset == 0 ? "layer:tab0" : "layer:tab1", 69, 10);

        // 两颗 tab:coreSlot（152x146，间隔 6）：左=强化中（预设 0），右=保存中（预设 1）。
        // 卡里的核心图标位置是 vector:enforceCore(132,61) / saveCore(290,61)（68x78 的左上角，
        // 正好落在卡片中心），之前只画了空卡、没画图标，看着像两格空白。
        var cardPos = Vector("hexaStat/enforcement_wnd/vector:enforceCore", 132, 61);
        var savePos = Vector("hexaStat/enforcement_wnd/vector:saveCore", 290, 61);
        for (int i = 0; i < 2; i++)
        {
            // tab:coreSlot/normal 的原点是 (-90,-30) / (-249,-30)，两张卡不等距 158 而是 159。
            float x = i == 0 ? 90f : 249f;
            var view = StatOf(statCore, i);
            GrowthUi.Image(Img + "hexaStat/enforcement_wnd/tab:coreSlot/",
                (i == preset ? "selected/" : "normal/") + i, x, 30);
            var iconAt = i == 0 ? cardPos : savePos;
            GrowthUi.Image(Img + "core/coreBackground/", "stat", iconAt.x, iconAt.y);
            string label = i == 0 ? "強化中" : "保存中";
            GrowthUi.ShadowText(Img + "hexaStat/enforcement_wnd/", label, x + 10, 38, 60, "", TextAnchor.UpperLeft, 12);
            GrowthUi.ShadowText(Img + "hexaStat/enforcement_wnd/",
                view == null ? "—" : "Lv" + view.level, x + 10, 60, 100, "", TextAnchor.UpperLeft, 13);
            if (view != null)
                GrowthUi.Text(Img + "hexaStat/enforcement_wnd/",
                    view.stat1 + " / " + view.stat2 + " / " + view.stat3, x + 10, 82, 132, "",
                    TextAnchor.UpperLeft, 12, new Color(.85f, .9f, .95f));
            if (view != null && view.applied)
                GrowthUi.Text(Img + "hexaStat/enforcement_wnd/", "套用中", x + 10, 110, 100, "",
                    TextAnchor.UpperLeft, 12, new Color(.55f, .95f, .7f));
            if (GUI.enabled && GrowthUi.At(x, 29, 152, 146).Contains(Pointer.Pos)) MatrixPresentation.Hover(StatHint(view));
            if (!Interactive()) continue;
            if (!GrowthUi.At(x, 29, 152, 146).Contains(Pointer.Pos) || !Pointer.Clicked) continue;
            Pointer.ConsumeClick();
            preset = i;
        }

        var current = CurrentStat();
        int cost = current!=null && current.level<20 ? NativeHexaMatrixService.StatFragmentCost(current.level1) : 0;
        GrowthUi.ShadowText(Img + "hexaStat/enforcement_wnd/",
            cost <= 0 ? "強化完成" : "強化需要 " + cost + " 碎片", 90, 196, 300, "",
            TextAnchor.UpperLeft, 12);

        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && preset==0 && Eligible && current != null && current.level < current.maxLevel
                      && (state?.fragments ?? 0) >= cost;
        if (preset==0 && GrowthUi.Button(Img + "hexaStat/enforcement_wnd/", "button:enforcement"))
        {
            var response = Send(HexaOperation.EnforceStat, core: statCore, presetIndex: preset);
            if (response != null && response.ok)
            { resultStatBefore = current; resultStatAfter = CurrentStat(); dialog = Dialog.StatResult; }
        }
        GUI.enabled = wasEnabled && preset==0 && current != null && (current.level>=10 || current.level!=current.level1+current.level2+current.level3);
        if (GrowthUi.Button(Img + "hexaStat/enforcement_wnd/", "button:reset"))
        {
            NativeUtilPrompt.Confirm(current.level!=current.level1+current.level2+current.level3 ? "舊版核心免費初始化，清空等級並保留能力值種類。繼續？" : "初始化清空強化等級，保留能力值種類，消耗1000萬楓幣。繼續？", ok =>
            {
                if (!ok) return;
                var response = Send(HexaOperation.Reset, core: statCore, presetIndex: preset);
                if (response != null && response.ok) BottomBar.Toast("已初始化");
            });
        }
        GUI.enabled = wasEnabled && preset==0 && current != null && current.level==20;
        if (GrowthUi.Button(Img + "hexaStat/enforcement_wnd/", "button:save"))
        {
            var response = Send(HexaOperation.SaveStat, core: statCore);
            if (response != null && response.ok) BottomBar.Toast("已存到保存槽");
        }
        GUI.enabled = wasEnabled && preset==0 && StatOf(statCore, 0) != null && !StatOf(statCore,0).applied;
        if (preset==0 && GrowthUi.Button(Img + "hexaStat/enforcement_wnd/", "button:apply"))
        {
            var response = Send(HexaOperation.Preset, core: statCore, presetIndex: 0);
            if (response != null && response.ok) BottomBar.Toast("已套用強化中的能力值");
        }
        GUI.enabled = wasEnabled && preset==1 && StatOf(statCore, 1) != null && !StatOf(statCore,1).applied;
        if (preset==1 && GrowthUi.Button(Img + "hexaStat/enforcement_wnd/", "button:apply2"))
        {
            var response = Send(HexaOperation.Preset, core: statCore, presetIndex: 1);
            if (response != null && response.ok) BottomBar.Toast("已套用保存中的能力值");
        }
        GUI.enabled = wasEnabled && current!=null;
        if (GrowthUi.Button(Img + "hexaStat/enforcement_wnd/", "button:change"))
        {
            choices[0]=current.stat1; choices[1]=current.stat2; choices[2]=current.stat3;
            dropdown=-1; changeCore=statCore; changePreset=preset; dialog=Dialog.StatChange;
        }
        GUI.enabled=wasEnabled;
        DrawStatLines(current);
        GrowthUi.Restore(outer);
    }

    static string StatHint(HexaStatView view)
    {
        if(view==null) return "尚未激活能力值核心";
        string text="能力值核心 Lv"+view.level+(view.applied ? "（套用中）" : "");
        int[] types={view.stat1,view.stat2,view.stat3}, levels={view.level1,view.level2,view.level3};
        for(int i=0;i<3;i++)
        {
            if(types[i]<0 || types[i]>=StatNames.Length) continue;
            int value=HexaCatalog.StatValue(types[i],levels[i],i==0);
            string amount=types[i]<=3 ? (value/100f).ToString("0.##")+"%" : (types[i]==6 ? HexaCatalog.MainStatValue(value,state.job) : value).ToString();
            text+="\n"+(i==0 ? "主要：" : "附加：")+StatNames[types[i]]+" +"+amount+"  Lv"+levels[i];
        }
        return text;
    }

    static void DrawStatLines(HexaStatView view)
    {
        if(view==null) return;
        string root=Img+"hexaStat/enforcement_wnd/enchantResult/";
        int[] types={view.stat1,view.stat2,view.stat3}, levels={view.level1,view.level2,view.level3};
        string[] keys={"main","add1","add2"};
        for(int i=0;i<3;i++)
        {
            var anchor=Vector("hexaStat/enforcement_wnd/enchantResult/pos/vector:"+keys[i],19,i==0 ? 199 : i==1 ? 297 : 364);
            string name=types[i]>=0 && types[i]<StatNames.Length ? StatNames[types[i]] : "";
            GrowthUi.Text(root,name,anchor.x+7,anchor.y+5,350,"font/font:"+(i==0 ? "statMain" : "statAdd"));
            int value=HexaCatalog.StatValue(types[i],levels[i],i==0);
            string amount=types[i]<=3 ? (value/100f).ToString("0.##")+"%" : (types[i]==6 ? HexaCatalog.MainStatValue(value,state.job) : value).ToString();
            GrowthUi.Text(root,"+"+amount,anchor.x+4,anchor.y+23,300,"font/font:figure");
            GrowthUi.Image(root+"imgLevel/",Mathf.Clamp(levels[i],0,10).ToString(),anchor.x+416,anchor.y+23);
            for(int n=1;n<=Mathf.Min(levels[i],10);n++)
                GrowthUi.Image(root+(i==0 ? "imgMainGauge/" : "imgAddGauge/"),n.ToString(),anchor.x+8+(n-1)*39,anchor.y+31);
        }
    }

    static void DrawStatSelection(bool change)
    {
        float width=change ? 788 : 450,height=change ? 688 : 699;
        var outer=GrowthUi.Shift((W-width)/2,(H-height)/2,width,height);
        string root=Img+"hexaStatPopUp/"+(change ? "statChange/" : "activationSet/");
        GrowthUi.Image(root,"backgrnd",0,0);
        float[] x=change ? new[]{112f,338f,543f} : new[]{84f,84f,84f};
        float[] y=change ? new[]{88f,88f,88f} : new[]{228f,288f,323f};
        if(change)
        {
            var definitions=HexaCatalog.AllStats;
            for(int row=0;row<6;row++)
            {
                float top=71+70*row;
                GrowthUi.Image(root,"layer:slot"+(row+1),29,top);
                var definition=row/2<definitions.Count ? definitions[row/2] : null;
                var view=definition==null ? null : StatOf(definition.id,row%2);
                if(view==null) { GrowthUi.Image(root,"canvas:disabled",29,top); continue; }
                bool selected=changeCore==view.coreId && changePreset==view.preset;
                int[] types={view.stat1,view.stat2,view.stat3};
                for(int i=0;i<3;i++)
                {
                    if(selected) { x[i]=i==0 ? 112 : i==1 ? 338 : 543; y[i]=top+17; continue; }
                    if(DrawStatCombo(root,i==0 ? 112 : i==1 ? 338 : 543,top+17,true,types[i],i,false) && dropdown>=0)
                    {
                        changeCore=view.coreId; changePreset=view.preset;
                        choices[0]=view.stat1; choices[1]=view.stat2; choices[2]=view.stat3;
                    }
                }
            }
            GrowthUi.Text(root,NativeHexaMatrixService.StatChangeMeso.ToString("N0"),517,549,100,"",TextAnchor.UpperRight);
        }
        else
        {
            GrowthUi.Image(Img+"core/coreBackground/","stat",191,61);
            GrowthUi.Image(root,"layer:coreCover",187,57);
            GrowthUi.Text(root,"5",127,420,40,"font:quantity",TextAnchor.UpperRight);
            GrowthUi.Text(root,"10",242,420,40,"font:quantity",TextAnchor.UpperRight);
            GrowthUi.Text(root,(state?.solErda ?? 0).ToString(),135,495,40,"possession/font:quantity_P",TextAnchor.UpperRight);
            GrowthUi.Text(root,(state?.fragments ?? 0).ToString(),245,495,40,"possession/font:quantity_P",TextAnchor.UpperRight);
        }
        for(int i=0;i<3;i++) DrawStatCombo(root,x[i],y[i],change,choices[i],i,false);
        bool enabled=GUI.enabled;
        GUI.enabled=enabled && dropdown<0 && NativeHexaMatrixService.ValidStats(choices[0],choices[1],choices[2]);
        if(GrowthUi.Button(root,change ? "button:change" : "button:activation"))
        {
            var response=Send(change ? HexaOperation.ChangeStat : HexaOperation.SetStat,core:change ? changeCore : statCore,presetIndex:change ? changePreset : 0,
                stat1:choices[0],stat2:choices[1],stat3:choices[2]);
            if(response!=null && response.ok) dialog=Dialog.None;
        }
        GUI.enabled=enabled && dropdown<0;
        if(GrowthUi.Button(root,"button:cancel")) dialog=Dialog.None;
        GUI.enabled=enabled;
        if(dropdown>=0)
        {
            int line=dropdown;
            for(int i=0;i<6;i++)
            {
                int type=i==4 ? (HexaCatalog.MainStat(state.job)=="INT" ? 5 : 4) : i==5 ? 6 : i;
                if(DrawStatCombo(root,x[line],y[line]+32*(i+1),change,type,line,true))
                { choices[line]=type; dropdown=-1; }
            }
        }
        GrowthUi.Restore(outer);
    }

    static bool DrawStatCombo(string root,float x,float y,bool change,int type,int line,bool option)
    {
        string art=root+"comboBoxPad/combo:main/normal/";
        GrowthUi.Image(art,"0",x,y);
        GrowthUi.Image(art,"1",x+2,y);
        GrowthUi.Image(art,"2",x+2+(change ? 173 : 248),y);
        GrowthUi.Text(root,type<0 ? "不選擇" : StatNames[type],x+8,y+7,change ? 166 : 240,"",TextAnchor.UpperLeft,12);
        bool clicked=Hot() && (option || dropdown<0) && Pointer.Clicked && GrowthUi.At(x,y,change ? 203 : 283,32).Contains(Pointer.Pos);
        if(clicked) { Pointer.ConsumeClick(); if(!option) dropdown=line; }
        return clicked;
    }

    // —————————————— 强化弹窗（hexaSkillPopUp/enforcementAlert 348x514） ——————————————

    static void DrawUpgrade()
    {
        var core = CoreOf(selectedCore);
        if (core == null) { dialog = Dialog.None; return; }
        var outer = GrowthUi.Shift((W - 348) / 2, (H - 514) / 2, 348, 514);
        GrowthUi.Image(Img + "hexaSkillPopUp/enforcementAlert/", "backgrnd", 0, 0);

        var corePos = Vector("hexaSkillPopUp/enforcementAlert/vector:corePos", 140, 61);
        var icon = MatrixCoreIcons.Core(core.coreId, true, core.skills);
        if (icon != null)
            GUI.DrawTexture(GrowthUi.At(corePos.x - 20, corePos.y - 10, icon.width, icon.height), icon);
        GrowthUi.Image(Img + "hexaSkillPopUp/enforcementAlert/", "layer:coreCover", 136, 57);

        upgradeTarget = Mathf.Clamp(upgradeTarget, core.level + 1, core.maxLevel);
        var before = Vector("hexaSkillPopUp/enforcementAlert/vector:levelBefore", 127, 206);
        var after = Vector("hexaSkillPopUp/enforcementAlert/vector:levelAfter", 226, 206);
        GrowthUi.Text(Img + "hexaSkillPopUp/enforcementAlert/", core.level.ToString(CultureInfo.InvariantCulture),
            before.x - 24, before.y - 10, 48, "font:levelBefore", TextAnchor.MiddleCenter, 18);
        GrowthUi.Text(Img + "hexaSkillPopUp/enforcementAlert/", upgradeTarget.ToString(CultureInfo.InvariantCulture),
            after.x - 24, after.y - 10, 48, "font:levelAfter", TextAnchor.MiddleCenter, 18);

        // 多级一次强化：累加 level..target-1 的费用。
        long solErdaCost = 0, fragmentCost = 0;
        for (int level = core.level; level < upgradeTarget; level++)
        {
            NativeHexaMatrixService.UpgradeCost(core.group, level, out int one, out int two);
            solErdaCost += one; fragmentCost += two;
        }
        bool couple = core.skills != null && core.skills.Length > 1;
        var single = Vector("hexaSkillPopUp/enforcementAlert/vector:singleIngredientCount", 174, 320);
        var mainCount = couple ? Vector("hexaSkillPopUp/enforcementAlert/vector:coupleIngredientMainCount", 116, 320) : single;
        var subCount = Vector("hexaSkillPopUp/enforcementAlert/vector:coupleIngredientSubCount", 232, 320);
        var solColor = (state?.solErda ?? 0) < solErdaCost ? new Color(1f, .55f, .5f) : (Color?)null;
        var fragColor = (state?.fragments ?? 0) < fragmentCost ? new Color(1f, .55f, .5f) : (Color?)null;
        GrowthUi.ShadowText(Img + "hexaSkillPopUp/enforcementAlert/", solErdaCost.ToString(CultureInfo.InvariantCulture),
            mainCount.x - 40, mainCount.y - 8, 40, "font:enforceIngredient", TextAnchor.MiddleRight, 12, solColor);
        GrowthUi.ShadowText(Img + "hexaSkillPopUp/enforcementAlert/", fragmentCost.ToString(CultureInfo.InvariantCulture),
            (couple ? subCount.x - 40 : single.x + 28), single.y - 8, 40, "font:enforceIngredient",
            TextAnchor.MiddleRight, 12, fragColor);

        bool enough = (state?.solErda ?? 0) >= solErdaCost && (state?.fragments ?? 0) >= fragmentCost;
        if (!enough)
        {
            GrowthUi.Image(Img + "hexaSkillPopUp/enforcementAlert/", "layer:deficiencyAlert", 49, 283);
            GrowthUi.Text(Img + "hexaSkillPopUp/enforcementAlert/", "氣息或碎片不足", 49, 291, 250, "",
                TextAnchor.MiddleCenter, 13, new Color(1f, .7f, .62f));
        }

        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && upgradeTarget < core.maxLevel && enough;
        if (GrowthUi.Button(Img + "hexaSkillPopUp/enforcementAlert/", "button:levelPlus")) upgradeTarget++;
        GUI.enabled = wasEnabled && upgradeTarget < core.maxLevel;
        if (GrowthUi.Button(Img + "hexaSkillPopUp/enforcementAlert/", "button:selectAll")) upgradeTarget = MaxAffordable(core);
        GUI.enabled = wasEnabled && upgradeTarget != core.level + 1;
        if (GrowthUi.Button(Img + "hexaSkillPopUp/enforcementAlert/", "button:initialize")) upgradeTarget = core.level + 1;
        GUI.enabled = wasEnabled && upgradeTarget > core.level && enough;
        if (GrowthUi.Button(Img + "hexaSkillPopUp/enforcementAlert/", "button:confirm"))
        {
            int beforeLevel = core.level;
            var response = Send(HexaOperation.Upgrade, core: core.coreId, target: upgradeTarget);
            if (response != null && response.ok)
            { resultActivation = false; resultBefore = beforeLevel; resultAfter = upgradeTarget; dialog = Dialog.SkillResult; }
        }
        GUI.enabled = wasEnabled;
        if (GrowthUi.Button(Img + "hexaSkillPopUp/enforcementAlert/", "button:cancel")) dialog = Dialog.None;
        GrowthUi.Restore(outer);
    }

    static void DrawStatResult()
    {
        if (resultStatBefore == null || resultStatAfter == null) { dialog = Dialog.None; return; }
        const string branch = "hexaStatPopUp/enforceResult/";
        string root = Img + branch;
        var outer = GrowthUi.Shift((W-528)/2, (H-489)/2, 528, 489);
        GrowthUi.Image(root,"backgrnd",0,0);
        var core = Vector(branch+"vector:core",230,61);
        GrowthUi.Image(Img+"core/coreBackground/","stat",core.x,core.y);
        GrowthUi.Image(root,"layer:coreCover",225,56);
        bool main = resultStatAfter.level1 > resultStatBefore.level1;
        GrowthUi.Image(root,main ? "canvas:mainSuccess" : "canvas:success",main ? 167 : 212,153);
        for (int side=0;side<2;side++)
        {
            var view = side==0 ? resultStatBefore : resultStatAfter;
            var anchor = Vector(branch+(side==0 ? "vector:before" : "vector:after"),side==0 ? 17 : 274,221);
            int[] types={view.stat1,view.stat2,view.stat3}, levels={view.level1,view.level2,view.level3};
            string[] keys={"mainStat","addStat1","addStat2"};
            for(int i=0;i<3;i++)
            {
                var line=Vector(branch+"vector:"+keys[i],119,i==0 ? 52 : i==1 ? 112 : 147);
                int value=HexaCatalog.StatValue(types[i],levels[i],i==0);
                string amount=types[i]<=3 ? (value/100f).ToString("0.##")+"%" : (types[i]==6 ? HexaCatalog.MainStatValue(value,state.job) : value).ToString();
                string name=types[i]>=0 && types[i]<StatNames.Length ? StatNames[types[i]] : "";
                GrowthUi.Text(root,name+" +"+amount+"  Lv"+levels[i],anchor.x+line.x-115,anchor.y+line.y-9,230,
                    i==0 ? "font:mainStat" : "font:addStat",TextAnchor.MiddleCenter,18);
            }
        }
        if(GrowthUi.Button(root,"button:confirm")) dialog=Dialog.None;
        GrowthUi.Restore(outer);
    }

    static void DrawSkillResult()
    {
        var core = CoreOf(selectedCore);
        if (core == null) { dialog = Dialog.None; return; }
        string branch = "hexaSkillPopUp/" + (resultActivation ? "activationResult/" : "enforcementResult/");
        string root = Img + branch;
        var outer = GrowthUi.Shift((W - 348) / 2, (H - 414) / 2, 348, 414);
        GrowthUi.Image(root, "backgrnd", 0, 0);
        var pos = Vector(branch + "vector:corePos", 140, 61);
        var icon = MatrixCoreIcons.Core(core.coreId, true, core.skills);
        if (icon != null) GUI.DrawTexture(GrowthUi.At(pos.x - 20, pos.y - 10, icon.width, icon.height), icon);
        GrowthUi.Image(root, "layer:coreCover", 135, 56);
        if (resultActivation)
        {
            var name = Vector(branch + "vector:skillName", 174, 254);
            GrowthUi.Text(root, core.name, name.x - 145, name.y - 10, 290, "font:skillName", TextAnchor.MiddleCenter, 20);
        }
        else
        {
            var before = Vector(branch + "vector:levelBefore", 125, 254);
            var after = Vector(branch + "vector:levelAfter", 227, 254);
            GrowthUi.Text(root, resultBefore.ToString(CultureInfo.InvariantCulture), before.x - 24, before.y - 10, 48,
                "font:levelBefore", TextAnchor.MiddleCenter, 20);
            GrowthUi.Text(root, resultAfter.ToString(CultureInfo.InvariantCulture), after.x - 24, after.y - 10, 48,
                "font:levelAfter", TextAnchor.MiddleCenter, 20);
        }
        if (GrowthUi.Button(root, resultActivation ? "button:confirm" : "button:cancel")) dialog = Dialog.None;
        GrowthUi.Restore(outer);
    }

    /// <summary>按当前气息与碎片能一口气升到几级（客户端预算，服务端复核）。</summary>
    static int MaxAffordable(HexaCoreView core)
    {
        long solErda = state?.solErda ?? 0, fragments = state?.fragments ?? 0;
        int level = core.level;
        while (level < core.maxLevel)
        {
            NativeHexaMatrixService.UpgradeCost(core.group, level, out int one, out int two);
            if (one > solErda || two > fragments) break;
            solErda -= one; fragments -= two; level++;
        }
        return Mathf.Max(level + 1, core.level + 1);
    }

    // —————————————— 索尔艾尔达兑换（solErdaTransform 408x492） ——————————————

    static void DrawTransform()
    {
        var outer = GrowthUi.Shift((W - 408) / 2, (H - 492) / 2, 408, 492);
        GrowthUi.Image(Img + "solErdaTransform/", "backgrnd", 0, 0);

        for (int i = 0; i < TransformItems.Length; i++)
        {
            float x = 26 + i * 180;
            GrowthUi.Image(Img + "solErdaTransform/tab:item/", (i == transformTab ? "selected/" : "normal/") + (i + 1), x, 185);
            if (!Hot()) continue;
            if (!GrowthUi.At(x, 185, 176, 35).Contains(Pointer.Pos) || !Pointer.Clicked) continue;
            Pointer.ConsumeClick();
            transformTab = i;
        }

        int itemId = TransformItems[transformTab];
        int perItem = itemId == NativeHexaMatrixService.FaintSolErda ? 30 : 1;
        int max = (int)Math.Min(9999, Math.Max(0, state?.solErda ?? 0));
        transformCount = Mathf.Clamp(transformCount, 1, Mathf.Max(1, max));

        GrowthUi.Image(Img + "solErdaTransform/detailLayer/", "layer:" + transformTab, 25, 220);
        GrowthUi.ShadowText(Img + "solErdaTransform/", (state?.solErda ?? 0).ToString("N0", CultureInfo.InvariantCulture),
            204, 126, 110, "font:EA", TextAnchor.MiddleRight, 14);
        GrowthUi.ShadowText(Img + "solErdaTransform/", transformCount.ToString(CultureInfo.InvariantCulture),
            136, 274, 60, "font:EA", TextAnchor.MiddleRight, 14);
        GrowthUi.ShadowText(Img + "solErdaTransform/", (transformCount * perItem).ToString(CultureInfo.InvariantCulture),
            268, 274, 60, "font:EA", TextAnchor.MiddleRight, 14);
        GrowthUi.Text(Img + "solErdaTransform/", transformCount.ToString(CultureInfo.InvariantCulture),
            139, 321, 120, "", TextAnchor.MiddleCenter, 14);
        GrowthUi.Text(Img + "solErdaTransform/", "1 氣息 → " + perItem + " 個" + TransformNames[transformTab],
            25, 350, 358, "", TextAnchor.MiddleCenter, 12, new Color(.78f, .84f, .9f));

        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && transformCount > 1;
        if (GrowthUi.Button(Img + "solErdaTransform/", "button:transformCountDown")) transformCount--;
        GUI.enabled = wasEnabled && transformCount < max;
        if (GrowthUi.Button(Img + "solErdaTransform/", "button:transformCountUP")) transformCount++;
        GUI.enabled = wasEnabled && transformCount != max;
        if (GrowthUi.Button(Img + "solErdaTransform/", "button:transformCountMax")) transformCount = Mathf.Max(1, max);
        GUI.enabled = wasEnabled && max > 0;
        if (GrowthUi.Button(Img + "solErdaTransform/", "button:confirm"))
        {
            var response = GameSession.Net.HexaConvert(new HexaConvertRequest
            { characterId = owner, itemId = itemId, count = transformCount });
            if (response == null || !response.ok) { error = response?.error ?? "兑换失败"; BottomBar.Toast(error); }
            else
            {
                BottomBar.Toast("轉換了 " + (transformCount * perItem) + " 個" + TransformNames[transformTab]);
                dialog = Dialog.None;
                GameSession.ReloadAfterCommand();
                Load();
            }
        }
        GUI.enabled = wasEnabled;
        if (GrowthUi.Button(Img + "solErdaTransform/", "button:close")) dialog = Dialog.None;
        GrowthUi.Restore(outer);
    }

    // 鼠标热区统一口径：弹窗打开时页面/页签一律不吃点击（弹窗是模态的）。
    static bool Hot() => GUI.enabled && GrowthUi.Repaint && !NativeUtilPrompt.Visible;
    static bool Interactive() => Hot() && dialog == Dialog.None;
}
