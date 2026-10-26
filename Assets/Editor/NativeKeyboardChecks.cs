using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Runs the actual virtual-button -> InputHub -> character keymap path.
// Does not save player preferences or the user's database.
public static class NativeKeyboardChecks
{
    static int passed, failed;
    static void Check(bool ok, string name)
    {
        if (ok) passed++;
        else { failed++; Debug.LogError("[NativeKeyboardChecks] " + name); }
    }
    public static void Run()
    {
        var previous = KeyMap.ExportCharacter();
        var skills = new int[QuickSlots.Count];
        for (int i = 0; i < skills.Length; i++) skills[i] = QuickSlots.Id(i);
        bool enabled = InputHub.Enabled;
        var send = typeof(VirtualControls).GetMethod("SendButton", BindingFlags.Static | BindingFlags.NonPublic);
        try
        {
            InputHub.Enabled = true;
            var defaults = new List<KeymapEntry>();
            for (int i = 0; i < KeymapDao.DefaultKeys.Length; i++)
                defaults.Add(new KeymapEntry { key = KeymapDao.DefaultKeys[i], type = KeymapDao.DefaultTypes[i], action = KeymapDao.DefaultActions[i] });
            KeyMap.LoadCharacter(defaults);
            foreach (var binding in defaults)
            {
                Check(NativeKeyActions.Find(binding.type, binding.action, out var action), "默认功能ID可识别 " + binding.action);
                InputHub.ReleaseAllVirtual();
                var button = new VCElem { key = NativeKeyLayout.FromScanCode(binding.key), action = GameAction.Jump, skillId = 12345678 };
                send.Invoke(null, new object[] { button, true });
                Check(InputHub.Pressed(action) && InputHub.Held(action), "默认虚拟键触发 " + action);
                if (action != GameAction.Jump) Check(!InputHub.Pressed(GameAction.Jump), "旧动作不串入 " + action);
                send.Invoke(null, new object[] { button, false });
                Check(!InputHub.Held(action), "虚拟键抬起 " + action);
                InputHub.EndFrame();
                Check(!InputHub.Pressed(action), "边沿只持续一帧 " + action);
            }
            var remapped = new List<KeymapEntry> { new KeymapEntry { key = 2, type = 6, action = 101 } };
            KeyMap.LoadCharacter(remapped);
            var one = new VCElem { key = KeyCode.Alpha1, action = GameAction.Jump, skillId = 12345678 };
            InputHub.ReleaseAllVirtual(); send.Invoke(null, new object[] { one, true });
            Check(InputHub.Pressed(GameAction.FaceSmile), "1改绑表情后触发表情");
            send.Invoke(null, new object[] { one, false }); InputHub.EndFrame();
            remapped[0] = new KeymapEntry { key = 2, type = 4, action = 1 };
            KeyMap.LoadCharacter(remapped);
            send.Invoke(null, new object[] { one, true });
            Check(InputHub.Pressed(GameAction.Inven) && !InputHub.Pressed(GameAction.FaceSmile), "不改虚拟按钮，键盘1改绑道具后跟随新功能");
            send.Invoke(null, new object[] { one, false }); InputHub.EndFrame();
            KeyMap.LoadCharacter(new List<KeymapEntry>());
            send.Invoke(null, new object[] { one, true });
            Check(!InputHub.Pressed(GameAction.Jump), "键盘1取消功能后不回退旧动作");

            CheckMouseControlRelease();
            CheckEscapeLayers();
            CheckKeyConfigCarry();
            CheckSkillWindowCarry();

            // ↓+跳跃的门控要读得到原图属性：10000 号图有一整片 forbidFallDown 的段
            bool forbidParsed = false;
            foreach (var f in NativeMapView.LoadMap(new FieldMap { id = 10000 }).footholds)
                if (f.forbidFallDown != 0) { forbidParsed = true; break; }
            Check(forbidParsed, "地图 forbidFallDown 属性没有读进 foothold");

            var map = WzAssets.FileRoot(WzAssets.MapPath(40001)).Get("miniMap");
            Check(map.Int("mag") == 4 && map.Int("centerX") == 318 && map.Int("centerY") == 799, "原小地图坐标参数");
            var canvas = WzAssets.Texture(map.Get("canvas"));
            Check(canvas.width == 129 && canvas.height == 92, "原小地图画布可解码");
            foreach (string file in new[] { "Map/WorldMap/WorldMap.img", "Map/WorldMap/WorldMap000.img" })
            {
                var root = WzAssets.FileRoot(file);
                Check(root.Get("MapList") != null && root.Get("BaseImg/0") != null, "原大地图数据 " + file);
                Check(WzAssets.Texture(root.Get("BaseImg/0")).width > 0, "原大地图画布可解码 " + file);
            }
            Check(WzAssets.FileRoot("Item/Consume/0238.img").Int("02380000/info/mob") == 100100, "卡片与蜗牛的原始对应");
        }
        finally
        {
            InputHub.ReleaseAllVirtual(); InputHub.Enabled = enabled;
            KeyMap.LoadCharacter(previous); QuickSlots.LoadCharacter(skills);
        }
        Debug.Log("[NativeKeyboardChecks] 通过 " + passed + "，失败 " + failed);
        if (failed > 0 && Application.isBatchMode) EditorApplication.Exit(1);
    }

    // Esc = 系统菜单动作；有面板时必须先关面板，而且从最后打开的那个关起。
    static void CheckEscapeLayers()
    {
        bool stat = StatWindow.Visible, inven = InvenWindow.Visible, equip = EquipWindow.Visible;
        bool menu = GameMenuWindow.Visible, skill = SkillWindow.Visible, quest = QuestWindow.Visible;
        try
        {
            StatWindow.Visible = InvenWindow.Visible = EquipWindow.Visible = false;
            GameMenuWindow.Visible = SkillWindow.Visible = QuestWindow.Visible = false;
            GameWindows.TrackPanels();
            Check(!GameWindows.CloseTop(), "没有面板时 Esc 不关任何东西");

            StatWindow.Visible = true;
            GameWindows.TrackPanels();
            InvenWindow.Visible = true;
            GameWindows.TrackPanels();
            Check(GameWindows.CloseTop() && !InvenWindow.Visible && StatWindow.Visible,
                "Esc 先关最后打开的面板");
            Check(GameWindows.CloseTop() && !StatWindow.Visible, "Esc 继续关剩下的面板");
            Check(!GameWindows.CloseTop(), "面板全关后 Esc 交回系统菜单");

            StatWindow.Visible = true;
            GameWindows.TrackPanels();
            NativeKeyActions.Execute(GameAction.Menu);
            Check(!StatWindow.Visible && !GameMenuWindow.Visible, "Esc 关面板时不再叠开系统菜单");
            // 有角色时才允许 Esc 打开系统菜单（登录/读图阶段不弹），
            // 这条守卫在无角色的批处理环境里表现为「什么都不开」。
            NativeKeyActions.Execute(GameAction.Menu);
            Check(StatWindow.Visible == false, "无角色时 Esc 不凭空开窗");

            SkillWindow.Visible = true;
            GameWindows.TrackPanels();
            EquipWindow.Visible = true;
            GameWindows.TrackPanels();
            Check(GameWindows.CloseTop() && !EquipWindow.Visible && SkillWindow.Visible,
                "叠开的面板按打开顺序关闭");
        }
        finally
        {
            StatWindow.Visible = stat; InvenWindow.Visible = inven; EquipWindow.Visible = equip;
            GameMenuWindow.Visible = menu; SkillWindow.Visible = skill; QuestWindow.Visible = quest;
            GameWindows.TrackPanels();
        }
    }

    // 键盘设定窗搬功能只有一种操作：点一下拿起、再点一下放下。
    // 这条路径不走 Pointer.Dragging，触屏也就不会被「按住 350ms」卡住。
    static void CheckKeyConfigCarry()
    {
        bool wasVisible = KeyConfigWindow.Visible;
        var saved = KeyMap.ExportCharacter();
        try
        {
            // 前面几项检查把键位表清空过，这里先回到源端默认表再验搬运
            var defaults = new List<KeymapEntry>();
            for (int i = 0; i < KeymapDao.DefaultKeys.Length; i++)
                defaults.Add(new KeymapEntry { key = KeymapDao.DefaultKeys[i], type = KeymapDao.DefaultTypes[i], action = KeymapDao.DefaultActions[i] });
            KeyMap.LoadCharacter(defaults);

            if (wasVisible) KeyConfigWindow.Toggle();
            KeyConfigWindow.Toggle();
            Check(KeyConfigWindow.Visible, "键盘设定窗可打开");

            var alt = KeyMap.Of(GameAction.Jump);
            Check(alt == KeyCode.LeftAlt && KeyConfigWindow.DraftKey(GameAction.Jump) == alt, "跳跃默认在 Alt");

            Check(KeyConfigWindow.PickAction(GameAction.Jump) && KeyConfigWindow.Carrying, "点一下拿起功能（不需要按住或长按）");
            Check(KeyConfigWindow.DraftKey(GameAction.Jump) == alt, "拿起时键位还没变");

            Check(KeyConfigWindow.DropOnKey(KeyCode.Alpha1), "再点一下目标键位就落下");
            Check(!KeyConfigWindow.Carrying, "落下后手上清空");
            Check(KeyConfigWindow.DraftKey(GameAction.Jump) == KeyCode.Alpha1, "跳跃落到 1 号键");
            Check(KeyConfigWindow.DraftKey(GameAction.ChatAll) == KeyCode.None, "原占 1 号键的功能被挤回托盘");
            Check(KeyConfigWindow.DraftUsers(KeyCode.Alpha1) == 1, "一个键只留一个功能");
            Check(KeyConfigWindow.DraftKey(GameAction.Attack) == KeyCode.LeftControl, "其他键位不受影响");

            var jump = KeyConfigWindow.DraftKey(GameAction.Jump);
            Check(jump == KeyCode.Alpha1 && KeyConfigWindow.PickAction(GameAction.Jump), "键位上的功能也能再拿起");
            Check(KeyConfigWindow.ReturnToTray() && KeyConfigWindow.DraftKey(GameAction.Jump) == KeyCode.None,
                "点托盘区把功能收回");

            KeyConfigWindow.PickAction(GameAction.Jump);
            Check(KeyConfigWindow.Carrying, "再拿起一次");
            KeyConfigWindow.CancelCarry();
            Check(!KeyConfigWindow.Carrying && KeyConfigWindow.DraftKey(GameAction.Jump) == KeyCode.None,
                "Esc 只取消搬运，不改键位");

            // 抓手光标用的是原图 Basic.img/Cursor/11（张开手）与 /12（握拳）
            Check(WzAssets.Node("UI/Basic.img/Cursor/11") != null, "原图有「可以抓」光标 Cursor/11");
            Check(WzAssets.Node("UI/Basic.img/Cursor/12") != null, "原图有「抓着」光标 Cursor/12");
        }
        finally
        {
            if (KeyConfigWindow.Visible) KeyConfigWindow.Toggle();
            KeyMap.LoadCharacter(saved);
        }
    }

    // 技能窗搬技能同样是「点一下拿起、再点一下放下」（BeiDou.exe 0x8ABA6B / 0x8A98FE 在
    // WM_LBUTTONDOWN 里发 DragStart，两个窗口都没有 WM_LBUTTONUP 分支）。
    static void CheckSkillWindowCarry()
    {
        var savedSlots = new int[QuickSlots.Count];
        for (int i = 0; i < savedSlots.Length; i++) savedSlots[i] = QuickSlots.Id(i);
        var savedKeys = QuickBarKeys.Copy();
        try
        {
            int id = 0;
            foreach (var tab in SkillBook.Tabs(SkillBook.LineFor(Profile.cls)))
            {
                if (!tab.TryGetValue("skills", out var raw)) continue;
                foreach (var o in (List<object>)raw) { id = int.Parse(o.ToString()); break; }
                if (id != 0) break;
            }
            Check(id > 0 && SkillBook.Get(id) != null, "技能树里能取到一个技能用于回归");

            SkillWindow.PickForTest(id);
            Check(SkillWindow.Dragging && SkillWindow.DragSkillId == id, "点一下拿起技能（不等位移、不等松手）");

            var slot = BottomBar.SlotRect(0);
            Check(SkillWindow.DropAt(new Vector2(slot.center.x, slot.center.y)), "再点一下快捷格就放下");
            Check(!SkillWindow.Dragging, "放下后手上清空");
            bool bound = false;
            for (int i = 0; i < QuickSlots.Count; i++) if (QuickSlots.Id(i) == id) bound = true;
            Check(bound, "技能真的进了快捷格");

            SkillWindow.PickForTest(id);
            Check(!SkillWindow.DropAt(new Vector2(4f, 4f)), "点空白不算放下");
            Check(!SkillWindow.Dragging, "点空白后手上清空（取消这次搬运）");

            SkillWindow.CancelSkillDrag();
            Check(!SkillWindow.Dragging, "右键/关窗能把技能收回去");
        }
        finally
        {
            SkillWindow.CancelSkillDrag();
            for (int i = 0; i < savedSlots.Length; i++) QuickSlots.Bind(i, savedSlots[i]);
            QuickBarKeys.Save(savedKeys);
        }
    }

    static void CheckMouseControlRelease()
    {
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var loaded = typeof(VirtualControls).GetField("loaded", flags);
        var anyHeld = typeof(VirtualControls).GetMethod("AnyHeld", flags);
        var oldElements = VirtualControls.elems;
        bool wasLoaded = (bool)loaded.GetValue(null);
        try
        {
            // -2 is the real desktop pointer ID; -1 alone means unoccupied.
            loaded.SetValue(null, true);
            var stick = new VCElem { type = VCElemType.Stick, finger = -2, held = true, axis = Vector2.right };
            VirtualControls.elems = new List<VCElem> { stick };
            InputHub.SetVirtualAxis(1, 0);
            Check((bool)anyHeld.Invoke(null, null), "鼠标摇杆占用被识别");
            VirtualControls.Tick(false);
            Check(stick.finger == -1 && stick.axis == Vector2.zero && !stick.held, "输入关闭释放鼠标摇杆");
            Check(!(bool)anyHeld.Invoke(null, null) && !VirtualControls.BlockingPointer, "鼠标释放后不残留指针阻塞");
            var button = new VCElem { action = GameAction.Jump, finger = -2, held = true };
            VirtualControls.elems = new List<VCElem> { button };
            typeof(VirtualControls).GetMethod("SendButton", flags).Invoke(null, new object[] { button, true });
            Check(InputHub.Held(GameAction.Jump), "鼠标虚拟键实际注入跳跃");
            VirtualControls.Tick(false);
            Check(button.finger == -1 && !button.held && !InputHub.Held(GameAction.Jump), "输入关闭释放鼠标虚拟键");
        }
        finally
        {
            VirtualControls.elems = oldElements;
            loaded.SetValue(null, wasLoaded);
            InputHub.ReleaseAllVirtual();
        }
    }
}
