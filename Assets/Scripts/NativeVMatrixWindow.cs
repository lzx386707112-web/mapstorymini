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
    // 特殊核心的图标不在 UI/VMatrixUI.img，也不在 skillicon（见 SpecialIcon 注释）。
    const string SpecialIconFile = "Etc/_Canvas/VCore.img";
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
    static int specialScrollStart = -1;   // 触屏拖动的起点行号；-1 = 本段手势不是从槽区起的
    static bool specialBarDrag;           // 是否正按着滚动条滑块拖
    static float specialBarGrab;          // 按下点距滑块顶端的距离（拖的时候保持不跳）
    // 这一段手势是从特殊核心子窗里起的：主窗的格子/按钮整段都看不到指针。
    static bool specialOwnsGesture;
    static int resultBefore, resultAfter;

    // 手上拿着的核心：源端 V 矩阵单击拿起、双击开强化，快捷栏靠「点一下放下」完成绑定
    // （与技能窗 SkillWindow 同一套语义，见 SkillWindow.DropOnSlot 的 DragStart 注释）。
    static bool carrying;
    static int carryCore, carrySkill;
    static int pressSlot = -1;      // 按下位置落在哪个核心槽（拖动时用来判断「拖核心」还是「拖窗口」）

    public static bool Visible => visible;
    public static bool Carrying => visible && carrying;
    public static bool AtPointer => visible && (dragging || Pointer.HitsWindow(WindowRect()));

    // 窗口宽度随子窗扩展：命中区/遮挡区必须盖住子窗伸到主窗之外的那一截（506..661），
    // 否则子窗右半边的点击会直接落到地图/HUD 上。
    static float Width => specialOpen ? SpecialLeft + SpecialW : W;
    static float S => Mathf.Min(BottomBar.S, Mathf.Min((UiKit.W - 24) / Width, UiKit.H * .92f / H));
    // 但**位置基线永远按主窗宽度算**：原来拿 Width 居中，打开子窗那一刻主窗整体左移
    // (661-506)/2≈77px，关掉再弹回来，看起来像「点了特殊核心，整个面板跳了一下」。
    // 现在主窗不动、子窗向右伸出，只有屏幕真的放不下（Clamp 用扩展尺寸）才整体左移。
    static Vector2 Free
    {
        get
        {
            var size = new Vector2(Width, H);
            if (positioned) return NativeUi.ClampWindowPosition(position, size, S);
            var center = new Vector2((UiKit.W / S - W) / 2, (UiKit.H / S - H) / 2);
            return NativeUi.ClampWindowPosition(center, size, S);
        }
    }
    static Rect WindowRect() => new Rect(Free.x * S, Free.y * S, Width * S, H * S);

    // 特殊核心子窗在窗口内的固定矩形（main/vector:specialCore + specialCore/backgrnd）。
    static Rect SpecialRect() => GrowthUi.At(SpecialLeft, SpecialTop, SpecialW, SpecialH);

    public static void Escape()
    {
        if (carrying) { CancelCarry(); return; }
        if (dialog != Dialog.None) { dialog = Dialog.None; return; }
        // 特殊核心子窗是主窗内部的一层（不是独立 Panel），Esc 只能收这一层。
        // 少了这一句，按一次 Esc 会把子窗和整扇 V 矩阵一起关掉。
        if (specialOpen) { specialOpen = false; specialScroll = 0; specialBarDrag = false; specialOwnsGesture = false; return; }
        Close();
    }

    public static void Close()
    {
        visible = dragging = positioned = specialOpen = false;
        state = null; error = null; selected = -1; dialog = Dialog.None;
        specialScroll = 0;
        specialScrollStart = -1;
        specialBarDrag = false;
        specialOwnsGesture = false;
        CancelCarry();
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
        // 核心等级 → 技能等级：刚升完级还没重拉角色数据时也能立刻施放。
        VMatrixLevels.Capture(response, owner);
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
        if (commit) { state = response; GameSession.ReloadAfterCommand(); VMatrixLevels.Capture(response, owner); }
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
        foreach (var core in state.cores)
        {
            if (core.coreId != coreId) continue;
            // VCore.img is the authoritative connection table.  Older character
            // snapshots can contain a stale name/skill payload (the visible
            // cross icon then shows Steel Body in the tooltip and binds the wrong
            // skill).  Normalize the presentation and binding fields by core id
            // before any UI or quick-slot code consumes the view.
            var canonical = VMatrixCatalog.Core(coreId);
            if (canonical != null)
            {
                core.name = canonical.name;
                core.desc = canonical.desc;
                core.skills = canonical.skills;
                core.type = canonical.type;
                core.maxLevel = canonical.maxLevel;
            }
            return core;
        }
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

    // 特殊核心没有 Skill —— VMatrix.json 里 18 条 type==2 的 `skills` 全是空数组
    // （400007xxx 在客户端没有 Skill 定义），所以「skillicon/<id>」对它永远是 null，
    // 子窗、展示位、登记弹窗三处就都是空的。真正的图标在
    // Etc/VCore.img/vSkill/CoreData/<id>/icon 的 _outlink 目标
    // Etc/_Canvas/VCore.img/vSkill/CoreData/<id>/{icon, iconDisabled}（32x32，ARGB4444），
    // 见 docs/tms282-skill/07-job-advancement.md §7.3；画布由
    // tools/ms282/import_special_core_icons.py 转成 GMS 密钥并登记进资源清单。
    // 资源包没更新时 HasFile 为 false：静默不画，不要每帧抛 FileNotFound。
    static readonly Dictionary<int, Texture2D> specialIcons = new Dictionary<int, Texture2D>();

    static Texture2D SpecialIcon(int coreId, bool registered)
    {
        if (coreId <= 0 || !WzAssets.HasFile(SpecialIconFile)) return null;
        int key = (coreId << 1) | (registered ? 1 : 0);
        if (specialIcons.TryGetValue(key, out var cached)) return cached;
        var node = WzAssets.Node(SpecialIconFile + "/vSkill/CoreData/" + coreId +
            (registered ? "/icon" : "/iconDisabled"));
        var texture = node == null ? null : WzAssets.Texture(node);
        specialIcons[key] = texture;
        return texture;
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

        // 特殊核心子窗是同一个 Panel 里的第二层：它晚画（视觉在上），但 IMGUI 是「先画的先吃事件」。
        // 子窗 x414..661 正好压住主窗最右一列核心格（419..487）和右上展示位（414..446 / 172..200），
        // 不遮的话点一下子窗，底下的格子先被选中、双击还会弹强化窗 —— 表现就是
        // 「点特殊核心面板，点击穿透到底下的核心格」。这里只遮子窗矩形内的指针，
        // 主窗左半边照常可点。整段手势在按下那一刻定下来：手指/鼠标移出子窗也不回吐给主窗，
        // 否则拖到一半会突然选中路过的那一格。
        if (Pointer.Pressed) specialOwnsGesture = specialOpen && SpecialRect().Contains(Pointer.Pos);
        // Clicked/DragEnded 在松手帧产生，此时 Down 已为 false；下一空闲帧才能清归属。
        else if (!Pointer.Down && !Pointer.Released) specialOwnsGesture = false;
        bool specialLayer = specialOpen && (specialOwnsGesture || SpecialRect().Contains(Pointer.Pos));

        // 抓手 = 整条标题栏（排除右上关闭/帮助），特殊核心子窗打开时也要能拖；
        // slop 让整块面板都能抓（按下后走 6px 才转成拖动，点核心/按钮不受影响）。
        // 但按住落在核心格上的那一段手势归「把核心拖到快捷格」，不能再兼作窗口拖动。
        if (Pointer.Pressed) pressSlot = dialog == Dialog.None && !specialLayer ? SlotAt(Pointer.Pos) : -1;
        else if (!Pointer.Down) pressSlot = -1;
        if (dialog == Dialog.None && !carrying && pressSlot < 0 && !specialLayer &&
            GrowthUi.Drag(ref dragging, ref positioned, ref position, ref grab, Width, H, WindowRect, 46f, 100f,
                owner: typeof(NativeVMatrixWindow), slop: 6f))
            window = WindowRect();
        GrowthUi.R = window;
        NativeUi.RegisterWindow("NativeVMatrixWindow", window, () => visible);
        if (Pointer.DragStarted && !carrying && pressSlot >= 0 && dialog == Dialog.None)
        {
            var dragged = CoreOf(pressSlot);
            if (dragged != null) PickCore(pressSlot, dragged.coreId, dragged);
        }
        // 手上有核心时点到面板空白处 = 放回，避免一直挂在指针上。
        if (carrying && Pointer.Clicked && pressSlot < 0) CancelCarry();

        MatrixPresentation.Begin();
        bool parentEnabled = GUI.enabled;
        // 弹窗覆盖期间只绘制主面板，底层槽位、页签和按钮不得消费弹窗点击。
        GUI.enabled = parentEnabled && dialog == Dialog.None;
        GrowthUi.Image(Img + "main/", "backgrnd", 0, 0);
        DrawPoints();
        // 主窗的格与按钮在子窗覆盖区里看不到这根指针；子窗自己（DrawSpecialCore）在外面用真实指针，
        // 所以滚动条、格子、开关都照常响应。用 InputMask 而不是 GUI.enabled：后者会把按钮画成灰图。
        // 两段分开包，绘制顺序保持「槽位 → 子窗 → 按钮」不变。
        using (new Pointer.InputMask(specialLayer)) DrawSlots();
        DrawSpecialCore(specialLayer);
        using (new Pointer.InputMask(specialLayer)) DrawButtons();

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

    // —————————————— 核心拿起 / 放下（拖到快捷栏） ——————————————

    static void PickCore(int slot, int coreId, VMatrixCoreView view)
    {
        var definition = VMatrixCatalog.Core(coreId);
        int[] skills = view != null && view.skills != null && view.skills.Length > 0
            ? view.skills : definition != null ? definition.skills : new int[0];
        // 这些是「操作被拒」，按项目的提示分层走弹窗（CUtilDlg Notice3）而不是 Toast：
        // Toast 只停在底部几秒，手指还按在核心上手一松就飘完了，玩家根本不知道刚才没绑上。
        if (skills.Length == 0) { NativeUtilPrompt.Notice("这个核心没有可以绑定的技能。"); return; }
        // 強化核心（type 1）连的是 400004xxx 包装技能：它本身不施放任何东西，只在装上后给
        // psdSkill 里的既有技能加最终伤害（400004008 = damR_5th 2x%，maxLevel 60）。
        // 绑到快捷格上按了没反应，所以这里直接说清楚，不要拿「未收录」搪塞。
        if (definition != null && definition.IsBoost)
        {
            NativeUtilPrompt.Notice("這是強化核心，本身不施放技能，只提升既有技能的傷害，不能綁到快捷欄。");
            return;
        }
        // 走到这里还取不到技能，才是真的缺口：JobTree 与 VMatrixSkills 都没有、IMG 里也没有
        // common/level。正常情况下不会命中（SkillBook.Get 会直读 IMG 兜底）。
        if (SkillBook.Get(skills[0]) == null)
        {
            NativeUtilPrompt.Notice("这个核心连接的技能（" + skills[0] + "）在技能表里找不到，暂时不能绑到快捷栏。");
            return;
        }
        // 被动核心（终极攻击VI 这类追加攻击体）绑到键盘上也按不下去：
        // 先在拿起这一步说清楚，别让玩家拖到一半、放下之后才看到「被动技能无需施放」。
        if (!QuickSlots.CanBindToQuickBar(skills[0], out string reject))
        {
            if (!string.IsNullOrEmpty(reject)) NativeUtilPrompt.Notice(reject);
            return;
        }
        carrying = true;
        carryCore = coreId;
        carrySkill = skills[0];
        selected = slot;
        BottomBar.Toast("点一下快捷栏（或键盘设定里的键位），放下「" + (view?.name ?? definition?.name ?? "核心") + "」");
    }

    static void CancelCarry()
    {
        carrying = false;
        carryCore = 0;
        carrySkill = 0;
    }

    static int SlotAt(Vector2 point)
    {
        if (state == null) return -1;
        for (int slot = 0; slot < 31; slot++)
        {
            var anchor = SlotAnchor(slot);
            if (GrowthUi.At(anchor.x - 34, anchor.y - 39, 68, 76).Contains(point)) return slot;
        }
        return -1;
    }

    /// <summary>
    /// 手上核心找落点。必须排在 BottomBar.Draw 之前：快捷格在那条链里先吃掉点击
    /// （背包把道具拖到快捷格也是同一处先判，见 NativeInventoryActions.CarryStep）。
    /// </summary>
    public static void CarryStep()
    {
        if (!carrying) return;
        if (!visible) { CancelCarry(); return; }
        if (!Pointer.Clicked && !Pointer.DragEnded) return;

        // ① 键盘。技能只走「键盘」这一层：键盘设定窗里的键位（KeyConfigWindow），或底部快捷格
        //    （每个格子本来就挂着 KeyCode，见 TryDropOnQuickSlot）。屏上虚拟按键不作为落点——
        //    直接把技能挂到 VirtualControls 的元素上会绕过键位映射，那个键在键盘设定里看不到、
        //    也改不了，玩家以为是另一套绑定。所以这里认出「落在虚拟按键上」只为给一句说明，
        //    不能让它变成静默取消（静默取消＝玩家分不清是「不支持」还是「没拖准」）。
        bool overKeycap = KeyConfigWindow.Visible && !KeyConfigWindow.Carrying
                          && KeyConfigWindow.HitKey(Pointer.Pos) != KeyCode.None;
        if (overKeycap)
        {
            if (KeyConfigWindow.DropSkill(Pointer.Pos, carrySkill))
            {
                Pointer.ConsumeClick();
                Pointer.Pressed = false;
                CancelCarry();
                return;
            }
        }
        else if (!KeyConfigWindow.Visible &&
                 (VirtualControls.HitButtonAt(Pointer.Pos) || VirtualControls.HitButtonAt(Pointer.PressPosition)))
        {
            NativeUtilPrompt.Notice("技能只能绑到键盘的键位上：在键盘设定里拖到键位，或放进快捷栏后指定按键。");
            CancelCarry();
            return;
        }

        // 收起状态下 SlotRect 一律是 Rect.zero，落点永远判不中——直接说清楚，别静默取消。
        if (BottomBar.QuickCollapsed)
        {
            NativeUtilPrompt.Notice("快捷栏处于收起状态，先展开快捷栏再绑定。");
            CancelCarry();
            return;
        }
        // 落点优先于「窗口自己吃掉这次点击」：矩阵窗口 506 宽居中，右缘会压住快捷栏最左
        // 两格，先判 AtPointer 会让那两格永远绑不上。手上有核心且指针在快捷格上，
        // 玩家要的就是绑定，不必再问这次点击属于哪个窗口。
        // 抬起点和按下点都试一次：鼠标是从核心一路拖到快捷格再松手（落点＝Pos），
        // 手机是「点核心拿起 → 点快捷格放下」（落点＝PressPosition），两条路都要能中。
        if (TryDropOnQuickSlot(Pointer.Pos) || TryDropOnQuickSlot(Pointer.PressPosition))
        {
            Pointer.ConsumeClick();
            Pointer.Pressed = false;
            return;
        }
        // 落在矩阵窗口内：交给 DrawSlots（拿起另一颗 / 点回自己放回）。
        if (Pointer.Clicked && AtPointer) return;
        CancelCarry();
    }

    static bool TryDropOnQuickSlot(Vector2 point)
    {
        for (int i = 0; i < QuickSlots.Count; i++)
        {
            if (!BottomBar.SlotRect(i).Contains(point)) continue;
            // 快捷栏 26 格里默认只有前 8 格有键（Shift/Insert/Home/PgUp/Ctrl/Delete/End/PgDn），
            // 落在后面没配键的格子上，原来直接弹「这一格还没有选择按键」——可玩家眼里那就是
            // 整条键盘，往中间一拖就永远绑不上。改成落到第一个「有键且空着」的格子，并说明去向。
            int target = i;
            if (QuickBarKeys.Of(target) == KeyCode.None)
            {
                target = QuickBarKeys.FreeKeyedSlot();
                if (target < 0)
                {
                    NativeUtilPrompt.Notice("快捷栏里配了按键的技能格都占满了。先在键盘设置里给空着的格子指定按键，再回来绑定。");
                    CancelCarry();
                    return true;
                }
            }
            // 成功要回一句，否则手指一松只看到图标冒出来，不知道绑到了第几格、哪个键。
            if (QuickBarKeys.BindSkill(target, carrySkill))
            {
                string tail = "（" + KeyMap.Label(QuickBarKeys.Of(target)) + "）";
                BottomBar.Toast(target == i
                    ? "已绑定到快捷格 " + (target + 1) + tail
                    : "这一格没有按键，已改绑到快捷格 " + (target + 1) + tail);
            }
            CancelCarry();
            return true;
        }
        return false;
    }

    /// <summary>手上核心的跟随图标。画在 HUD 层（GameWindows 之后），不受面板指针遮罩影响。</summary>
    public static void DrawCarryGhost()
    {
        if (!carrying) return;
        var texture = carryCore != 0 ? MatrixCoreIcons.Texture(carryCore, false) : null;
        if (texture == null && carrySkill != 0) texture = WzAssets.Texture("skillicon/" + carrySkill);
        if (texture == null) return;
        float size = 32f * BottomBar.S;
        GUI.DrawTexture(new Rect(Pointer.Pos.x - size * .5f, Pointer.Pos.y - size * .5f, size, size),
            texture, ScaleMode.ScaleToFit);
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

    // 等级槽在原底图里的实测范围：本地 x 9..48、y 50..58（58x68 槽框，图标槽 12..45/11..44），
    // 中心 (28.5,54)。core/levelNumber 是 5px 高的点阵数字，上沿落在 51.5，取 52 正好居中。
    //
    // 旧实现把 font:levelCount(12px) 用 UpperLeft 画在 core/vector:currentLevelPos 上：
    // 这组矢量是给点阵数字留的左上角，字高 12 比整条槽（9px）还高，而且 GrowthUi 的
    // 字号下限就是 12 —— 必然压到槽框下沿、也偏左，这就是面板上「数字对不上」的来源。
    // 原图自带数字就用原图，别再让字体替它排版。
    const float LevelBarCenterX = 28.5f, LevelBarTopY = 52f, LevelDigitGap = 1f;

    // 点数阵图直接量宽再画：31 个槽每帧各两趟，不建数组/List，避免 UI 重绘时白白产垃圾。
    static Texture2D LevelDigit(char digit)
    {
        var node = WzAssets.Node(Img + "core/levelNumber/" + digit);
        return node == null ? null : WzAssets.Texture(node);
    }

    static void DrawLevelNumber(int level, float left, float top)
    {
        string text = level.ToString(CultureInfo.InvariantCulture);
        float width = 0f;
        for (int i = 0; i < text.Length; i++)
        {
            var texture = LevelDigit(text[i]);
            if (texture == null) return;   // 缺一张就整组不画，别只留半个数字
            width += texture.width + (i > 0 ? LevelDigitGap : 0f);
        }
        float x = left + LevelBarCenterX - width / 2f, y = top + LevelBarTopY;
        for (int i = 0; i < text.Length; i++)
        {
            var texture = LevelDigit(text[i]);
            GUI.DrawTexture(GrowthUi.At(x, y, texture.width, texture.height), texture);
            x += texture.width + LevelDigitGap;
        }
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
                // 核心图标按 coreId 取 282 的 VCore/HexaCore 原图：Skill 树里很多 V 技能的
                // icon 只是 1×1 空桩，直接 skillicon/<skills[0]> 画出来就是一片空格。
                var icon = view == null ? null
                    : view.type == 2 ? SpecialIcon(view.coreId, view.level > 0)
                    : MatrixCoreIcons.Core(view.coreId, false, view.skills);
                if (icon != null)
                    DrawCoreIcon(icon, bx, by, 58f);
                if (view != null && view.level > 0) DrawLevelNumber(view.level, bx, by);
            }
            if (hover && GUI.enabled && view != null)
            {
                var canonical = VMatrixCatalog.Core(view.coreId);
                MatrixPresentation.Skill(view.skills, view.level,
                    canonical != null ? canonical.name : view.name,
                    canonical != null ? canonical.desc : view.desc);
            }
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

            if (!GUI.enabled || !GrowthUi.Repaint || !hover) continue;
            // 双击 = 开强化弹窗（源端 V 矩阵的双击口径）；单击 = 拿起核心，再点快捷格放下。
            if (Pointer.DoubleClicked)
            {
                Pointer.ConsumeClick();
                CancelCarry();
                selected = coreId == 0 ? -1 : slot;
                if (coreId == 0) continue;
                dialogCore = coreId;
                targetLevel = view?.level ?? 0;
                dialog = Dialog.Upgrade;
                continue;
            }
            if (!Pointer.Clicked) continue;
            Pointer.ConsumeClick();
            if (coreId == 0) { CancelCarry(); selected = -1; continue; }
            if (carrying && carryCore == coreId) { CancelCarry(); continue; }   // 点回自己 = 放回原处
            PickCore(slot, coreId, view);
        }
    }

    static void DrawSpecialCore(bool coveredByPanel)
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
            // 装备中时这一格画的是「已登记」状态：66x68 的 special 底框 + 核心图标。
            // 只画图标（原实现）会得到一个悬在深色底板上的 32x32 方块，没有槽框。
            GrowthUi.Image(Img + "core/coreBackground/", "special", coverX, coverY);
            var icon = SpecialIcon(active.coreId, true);
            if (icon != null) DrawCoreIcon(icon, coverX, coverY, 66f);
        }
        else
        {
            // 空位是两帧：常态「+」在 (380,132)，鼠标移上去换成发光版。
            // 发光版 70x72、origin(-378,-130) → 左上角 (378,130)，比常态外扩 2px。
            // 子窗打开时它和子窗左上角有一块 (414..446 / 172..200) 重叠，那块归子窗，不发光。
            bool over = GUI.enabled && !coveredByPanel && GrowthUi.At(coverX, coverY, 66, 68).Contains(Pointer.Pos);
            GrowthUi.Image(Img + "main/",
                over ? "layer:specialCoreUnregisteredMouseOver" : "layer:specialCoreUnregistered",
                over ? coverX - 2f : coverX, over ? coverY - 2f : coverY);
        }
        if (active != null && GUI.enabled && !coveredByPanel
            && GrowthUi.At(coverX, coverY, 66, 68).Contains(Pointer.Pos))
            MatrixPresentation.Details(active.name, active.desc, active.level, false);
        if (!specialOpen) return;

        GrowthUi.Image(Img + "main/specialCore/", "backgrnd", SpecialLeft, SpecialTop);
        // 列表按 CoreData 的原顺序（= VMatrix.json 里 cores 的顺序，也就是 AllCores 的枚举顺序），
        // 已登记的只是换成自己的快照。先列已登记、再把未登记追加到末尾会造成
        // 「登记一个核心，整张网格重排」——原端列表是固定的 18 格。
        var slots = new List<VMatrixCoreView>();
        foreach (var core in VMatrixCatalog.AllCores)
        {
            if (!core.IsSpecial) continue;
            VMatrixCoreView owned = null;
            if (state?.cores != null)
                foreach (var view in state.cores) if (view.coreId == core.id) { owned = view; break; }
            slots.Add(owned ?? new VMatrixCoreView
            {
                coreId = core.id, type = core.type, maxLevel = core.maxLevel,
                name = core.name, desc = core.desc, skills = core.skills,
            });
        }

        var lt = Vector("main/specialCore/vector:slotLT", 10, 47);
        var slotPos = Vector("main/specialCore/slot/slotPos", 36, 39);
        var offset = Vector("main/specialCore/slot/slotOffset", 75, 75);
        var clip = Vector("main/specialCore/slot/mask/clip_rb", 230, 297);
        const int columns = 3, visibleRows = 4;
        int rows = (slots.Count + columns - 1) / columns;
        int maxScroll = Mathf.Max(0, rows - visibleRows);
        var area = GrowthUi.At(SpecialLeft + lt.x, SpecialTop + lt.y, clip.x, clip.y);

        // 滚动条几何全部读原端 scrollBarInfo：lt(222,-1)、轨道 5x292、滑块 5x28。
        // delta(10)/wheelRange(400) 是原端的翻页量与内容总长，本工程按「行」翻页，不用这两个值。
        var barLT = Vector("main/specialCore/slot/scrollBarInfo/lt", 222, -1);
        const float barW = 5f, barH = 292f, thumbH = 28f;
        float barX = SpecialLeft + barLT.x, barY = SpecialTop + barLT.y;
        float barTravel = Mathf.Max(1f, barH - thumbH);
        // 5px 宽在触屏上按不住，命中区向右放宽到 13px（只放宽命中，绘制仍是 5px）。
        // 左边只退 1px：第三列的槽框右边缘在 slot 局部 x=219，再往左会抢掉格子的点击。
        var barHit = GrowthUi.At(barX - 1f, barY, barW + 8f, barH);
        Rect thumbAt(int scroll) => GrowthUi.At(barX,
            barY + barTravel * (maxScroll <= 0 ? 0f : Mathf.Clamp01((float)scroll / maxScroll)), barW, thumbH);

        if (GUI.enabled)
        {
            // 滚轮（桌面/编辑器）
            if (area.Contains(Pointer.Pos) && Mathf.Abs(Input.mouseScrollDelta.y) > .01f)
                specialScroll = Mathf.Clamp(specialScroll - (int)Mathf.Sign(Input.mouseScrollDelta.y), 0, maxScroll);
            // 滚动条：按滑块拖、按轨道翻一行。命中后必须把这一段手势从槽区拖动里摘出去，
            // 否则拖滑块会顺带按 y 位移把列表再翻一次。
            if (maxScroll > 0)
            {
                var thumb = thumbAt(specialScroll);
                if (Pointer.Pressed && barHit.Contains(Pointer.PressPosition))
                {
                    specialScrollStart = -1;
                    if (thumb.Contains(Pointer.PressPosition))
                    {
                        specialBarDrag = true;
                        specialBarGrab = Pointer.PressPosition.y - thumb.y;
                    }
                    else
                        specialScroll = Mathf.Clamp(specialScroll
                            + (Pointer.PressPosition.y < thumb.y ? -1 : 1), 0, maxScroll);
                    Pointer.ConsumeClick();
                }
                else if (specialBarDrag && Pointer.Down)
                {
                    float t = Mathf.Clamp01((Pointer.Pos.y - specialBarGrab - barY) / barTravel);
                    specialScroll = Mathf.Clamp(Mathf.RoundToInt(t * maxScroll), 0, maxScroll);
                }
            }
            if (!Pointer.Down) specialBarDrag = false;
            // 触屏没有滚轮：18 个特殊核心是 6 行、一屏只放 4 行，后两行只能靠拖。
            // 按住槽区上下拖满一行翻一行（位移不足一行时回到按下时的位置，不会半格错位）。
            // specialScrollStart = -1 表示「这一段手势不是从槽区开始的」，不能拿旧值去算。
            if (maxScroll > 0 && Pointer.Pressed && area.Contains(Pointer.PressPosition)
                && !barHit.Contains(Pointer.PressPosition))
                specialScrollStart = specialScroll;
            else if (!Pointer.Down)
                specialScrollStart = -1;
            else if (specialScrollStart >= 0)
            {
                int step = Mathf.Max(1, Mathf.RoundToInt(offset.y * S));
                specialScroll = Mathf.Clamp(specialScrollStart - Mathf.RoundToInt((Pointer.Pos.y - Pointer.PressPosition.y) / step), 0, maxScroll);
            }
        }
        specialScroll = Mathf.Clamp(specialScroll, 0, maxScroll);

        for (int i = 0; i < slots.Count; i++)
        {
            int row = i / columns, col = i % columns;
            if (row < specialScroll || row >= specialScroll + visibleRows) continue;
            float x = SpecialLeft + lt.x + col * offset.x + slotPos.x;
            float y = SpecialTop + lt.y + (row - specialScroll) * offset.y + slotPos.y;
            var view = slots[i];
            // 已登记（level>0）才有彩色图标；未登记的用 282 的 iconDisabled（灰）+ 锁底框。
            bool opened = view.level > 0;
            var frame = WzAssets.Node(Img + "core/coreBackground/" + (opened ? "special" : "specialLocked"));
            var origin = frame == null ? new Vector2(34, 39) : WzAssets.Origin(frame);
            GrowthUi.Image(Img + "core/coreBackground/", opened ? "special" : "specialLocked", x - origin.x, y - origin.y);
            var icon = SpecialIcon(view.coreId, opened);
            if (icon != null)
                DrawCoreIcon(icon, x - origin.x, y - origin.y, 66f);
            bool hover = GrowthUi.At(x - origin.x, y - origin.y, 66, 68).Contains(Pointer.Pos);
            if (hover && GUI.enabled) MatrixPresentation.Details(view.name, view.desc, view.level, false);
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

        // 滚动条：轨道常驻（一屏放得下时用 disabled/base），有可翻的行才画滑块。
        // 原端 disabled 分支就只有 base/prev/next，没有 thumb，所以这里也不画滑块。
        GrowthUi.Image(Img + "main/specialCore/slot/scrollBarInfo/",
            maxScroll > 0 ? "enabled/base" : "disabled/base", barX, barY);
        if (maxScroll > 0)
        {
            var thumb = thumbAt(specialScroll);
            bool over = barHit.Contains(Pointer.Pos);
            string frame = specialBarDrag ? "thumb2" : over ? "thumb1" : "thumb0";
            GrowthUi.Image(Img + "main/specialCore/slot/scrollBarInfo/enabled/", frame, thumb.x, thumb.y);
        }
    }

    static void DrawButtons()
    {
        if (GrowthUi.Button(Img + "main/", "button:close")) { Close(); return; }
        if (GrowthUi.Button(Img + "main/", "button:help"))
            MatrixPresentation.Help(false);
        // 「特殊核心」开关是同一位置的两张图（箭头 → 开 / ← 关），必须按状态二选一。
        // 两个都画就永远看到后画的 ←，看起来像「面板明明关着，按钮却显示已展开」。
        if (GrowthUi.Button(Img + "main/", specialOpen ? "button:specialCoreClose" : "button:specialCoreOpen"))
        { specialOpen = !specialOpen; specialScroll = 0; specialBarDrag = false; specialOwnsGesture = false; }

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
        var icon = core.type == 2 ? SpecialIcon(core.coreId, true)
            : MatrixCoreIcons.Core(core.coreId, false, core.skills);
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
        var icon = core.type == 2 ? SpecialIcon(core.coreId, true)
            : MatrixCoreIcons.Core(core.coreId, false, core.skills);
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
        var icon = SpecialIcon(core.coreId, true);
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
