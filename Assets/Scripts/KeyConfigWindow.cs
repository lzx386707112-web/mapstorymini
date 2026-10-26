using System.Collections.Generic;
using UnityEngine;

// UI.wz/UIWindow.img/KeyConfig: 编辑窗口，不向角色发送虚拟按键。
public static class KeyConfigWindow
{
    public static bool Visible { get; private set; }
    static Dictionary<GameAction, KeyCode> draft;
    static readonly GameAction[] palette = BuildPalette();
    static GameAction[] BuildPalette()
    {
        var actions = new List<GameAction>();
        foreach (var entry in NativeKeyActions.Entries) actions.Add(entry.action);
        return actions.ToArray();
    }
    static KeyCode[] quickDraft;
    static int quickPage;
    static readonly Dictionary<int, int> skillDraft = new Dictionary<int, int>();
    static Vector2 pos = new Vector2(-1, -1), offset;
    static bool dragging, quickEdit;
    static int quickIndex, confirmMode;
    static GameAction? carried;
    static bool swallow;          // 这次点击用来「取消搬运」，不再触发底部的按钮
    static KeyCode selected;
    static float S => Mathf.Min(BottomBar.S, (UiKit.W - 16f) / 629f, (UiKit.H - 16f) / 373f);
    static Rect R(float x, float y, float w, float h)
        => new Rect(pos.x + x * S, pos.y + y * S, w * S, h * S);
    static Texture2D T(string name) => Art.Tex("ui/keyconfig/" + name + ".bytes");

    static void EnsurePos()
    {
        if (pos.x < 0) pos = new Vector2((UiKit.W - 629 * S) / 2, Mathf.Max(8, (UiKit.H - 373 * S) / 2));
    }

    public static void Toggle()
    {
        if (Visible) { Close(); return; }
        draft = new Dictionary<GameAction, KeyCode>();
        foreach (var kv in KeyMap.All) draft[kv.Key] = kv.Value;
        quickDraft = QuickBarKeys.Copy();
        quickPage = 0;
        confirmMode = 0;
        Visible = true;
        carried = null;
        swallow = false;
        skillDraft.Clear();
        quickEdit = false;
        selected = KeyCode.None;
        EnsurePos();
        InputHub.ReleaseAllVirtual();
        TouchInput.Reset();
    }

    static void Close() { Visible = false; confirmMode = 0; carried = null; swallow = false; dragging = false; skillDraft.Clear(); }

    public static bool DropSkill(Vector2 point, int skillId)
    {
        var key = HitKey(point);
        if (key == KeyCode.None) return false;
        int slot = -1;
        for (int i = 0; i < QuickSlots.Count; i++)
        {
            var action = QuickSlots.Action(i);
            if (draft[action] == key) { slot = i; break; }
        }
        if (slot < 0)
            for (int i = 0; i < QuickSlots.Count; i++)
                if (!skillDraft.ContainsKey(i) && (QuickSlots.Id(i) == 0 || draft[QuickSlots.Action(i)] == KeyCode.None)) { slot = i; break; }
        if (slot < 0) { BottomBar.Toast("快捷技能位已满，请先清空一个位置"); return true; }
        foreach (var a in new List<GameAction>(draft.Keys))
            if (draft[a] == key) draft[a] = KeyCode.None;
        draft[QuickSlots.Action(slot)] = key;
        skillDraft[slot] = skillId;
        return true;
    }

    public static Texture2D Icon(GameAction action)
    {
        if (QuickSlots.Index(action) >= 0)
        {
            var skill = SkillBook.Get(QuickSlots.Id(QuickSlots.Index(action)));
            return skill != null && !string.IsNullOrEmpty(skill.icon) ? Art.Tex(skill.icon) : null;
        }
        var entry = NativeKeyActions.Of(action);
        return entry != null ? T("icon_" + entry.id) : null;
    }

    public static bool InTray(GameAction action)
        => draft != null && (!draft.TryGetValue(action, out var code) || code == KeyCode.None);

    static Texture2D DraftIcon(GameAction action)
    {
        int slot = QuickSlots.Index(action);
        if (slot >= 0 && skillDraft.TryGetValue(slot, out var id))
        {
            var skill = SkillBook.Get(id);
            return skill != null && !string.IsNullOrEmpty(skill.icon) ? Art.Tex(skill.icon) : null;
        }
        return Icon(action);
    }

    static bool Button(string name, float x, float y, int w, int h)
    {
        var r = R(x, y, w, h);
        bool hover = r.Contains(Pointer.Pos);
        var t = T(name + "_" + (hover ? Pointer.Down ? "pressed" : "mouseOver" : "normal"));
        if (t != null) GUI.DrawTexture(r, t);
        // 刚用一次点击取消了「手上拿着功能」时，这次抬手不再触发窗口按钮
        return confirmMode == 0 && !swallow && Event.current.type == EventType.Repaint && Pointer.Released && hover && !(quickEdit && QuickRect.Contains(Pointer.Pos));
    }

    public static KeyCode HitKey(Vector2 point)
    {
        if (!Visible || confirmMode != 0 || quickEdit && QuickRect.Contains(point)) return KeyCode.None;
        foreach (var key in NativeKeyLayout.Positions)
            if (R(key.Value.x, key.Value.y, 32, 32).Contains(point)) return NativeKeyLayout.FunctionKey(key.Key);
        return KeyCode.None;
    }

    // —— 功能的搬运：原端只有一种操作 ——
    //   悬停在功能上 → 光标变张开的手；点一下 → 功能跟到光标上；
    //   在目标键位上再点一下 → 落下。全程不需要按住拖动，触屏也不用长按。
    public static bool Carrying => carried.HasValue;
    public static GameAction CarriedAction => carried.HasValue ? carried.Value : default;

    // 这个位置能拿起哪个功能：键位上已绑的功能，或底部托盘里还没上键的功能
    static GameAction? PickTargetAt(Vector2 point)
    {
        if (draft == null || quickEdit) return null;
        var key = HitKey(point);
        if (key != KeyCode.None)
        {
            foreach (var kv in draft)
                if (kv.Value == key && DraftIcon(kv.Key) != null) return kv.Key;
            return null;
        }
        for (int i = 0; i < palette.Length; i++)
        {
            if (!InTray(palette[i]) || DraftIcon(palette[i]) == null) continue;
            var p = NativeKeyLayout.TrayPosition(palette[i]);
            if (R(p.x, p.y, 32, 32).Contains(point)) return palette[i];
        }
        return null;
    }

    public static bool PickableAt(Vector2 point) => PickTargetAt(point).HasValue;

    // 拿起的动作本体（和命中判定分开：命中判定要 IMGUI 坐标，搬运状态机不需要）
    public static bool PickAction(GameAction action)
    {
        if (draft == null || carried.HasValue) return false;
        if (!draft.ContainsKey(action) && !InTray(action)) return false;
        carried = action;
        return true;
    }

    public static bool PickAt(Vector2 point)
    {
        var action = PickTargetAt(point);
        if (!action.HasValue || !PickAction(action.Value)) return false;
        BottomBar.Toast("拿起「" + ActionLabel(action.Value) + "」：点目标键位放下，Esc 取消");
        return true;
    }

    // 落到某个键上：原来占这个键的功能回托盘（原端一键一职）
    public static bool DropOnKey(KeyCode key)
    {
        if (!carried.HasValue || draft == null || key == KeyCode.None) return false;
        var action = carried.Value;
        foreach (var a in new List<GameAction>(draft.Keys))
            if (draft[a] == key) draft[a] = KeyCode.None;
        draft[action] = key;
        carried = null;
        return true;
    }

    public static bool ReturnToTray()
    {
        if (!carried.HasValue || draft == null) return false;
        var action = carried.Value;
        draft[action] = KeyCode.None;
        carried = null;
        return true;
    }

    public static void CancelCarry() => carried = null;

    // 放下：压在键帽上就换过去；压在底部托盘区就收回托盘；点窗口别处 = 取消这次搬运。
    public static bool DropAt(Vector2 point)
    {
        if (!carried.HasValue) return false;
        var key = HitKey(point);
        if (key != KeyCode.None) return DropOnKey(key);
        if (R(0, 262, 629, 111).Contains(point))
        {
            var action = carried.Value;
            if (!ReturnToTray()) return false;
            BottomBar.Toast("「" + ActionLabel(action) + "」已放回托盘");
            return true;
        }
        CancelCarry();
        return false;
    }

    // 键帽中心（回归/自检用，和绘制同一个矩形：32×32 的图标格子）
    public static Vector2 KeyCenter(KeyCode key)
    {
        EnsurePos();
        var want = NativeKeyLayout.FunctionKey(key);
        foreach (var kv in NativeKeyLayout.Positions)
            if (NativeKeyLayout.FunctionKey(kv.Key) == want)
                return new Vector2(pos.x + (kv.Value.x + 16f) * S, pos.y + (kv.Value.y + 16f) * S);
        return new Vector2(-1f, -1f);
    }

    public static KeyCode DraftKey(GameAction action)
        => draft != null && draft.TryGetValue(action, out var code) ? code : KeyCode.None;

    // 这个键上现在挂了几个功能（原端一码一行，正常只能是 0 或 1）
    public static int DraftUsers(KeyCode key)
    {
        if (draft == null || key == KeyCode.None) return 0;
        int n = 0;
        foreach (var kv in draft) if (kv.Value == key) n++;
        return n;
    }

    static string ActionLabel(GameAction action)
    {
        int slot = QuickSlots.Index(action);
        if (slot >= 0)
        {
            var skill = SkillBook.Get(skillDraft.TryGetValue(slot, out var id) ? id : QuickSlots.Id(slot));
            if (skill != null) return skill.name;
        }
        var entry = NativeKeyActions.Of(action);
        return entry != null ? entry.name : action.ToString();
    }

    static void SetQuickKey(KeyCode code)
    {
        if (code == KeyCode.None) return;
        code = NativeKeyLayout.FunctionKey(code);
        for (int i = 0; i < quickDraft.Length; i++)
            if (i != quickIndex && quickDraft[i] == code) quickDraft[i] = quickDraft[quickIndex];
        quickDraft[quickIndex] = code;
    }

    static Rect QuickRect => new Rect(
        Mathf.Clamp(pos.x + 360 * S, 0, Mathf.Max(0, UiKit.W - 266 * S)),
        Mathf.Clamp(pos.y + 220 * S, 0, Mathf.Max(0, UiKit.H - 238 * S)), 266 * S, 238 * S);

    static void DrawQuickEdit()
    {
        float x = Mathf.Clamp(pos.x + 360 * S, 0, Mathf.Max(0, UiKit.W - 266 * S));
        float y = Mathf.Clamp(pos.y + 220 * S, 0, Mathf.Max(0, UiKit.H - 238 * S));
        var bg = T("quickslot_backgrnd");
        if (bg != null) GUI.DrawTexture(new Rect(x, y, 266 * S, 238 * S), bg);
        for (int cell = 0; cell < 8; cell++)
        {
            int i = quickPage * 8 + cell;
            if (i >= QuickSlots.Count) break;
            var r = new Rect(x + (64 + cell % 4 * 35) * S, y + (113 + cell / 4 * 34) * S, 32 * S, 30 * S);

            var style = new GUIStyle { fontSize = Mathf.RoundToInt(9 * S), alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = Color.black;
            GUI.Label(r, KeyMap.Label(quickDraft[i]), style);
            if (Event.current.type == EventType.Repaint && Pointer.Pressed && r.Contains(Pointer.Pos)) quickIndex = i;
        }
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode != KeyCode.None && Event.current.keyCode != KeyCode.Escape)
        { SetQuickKey(Event.current.keyCode); Event.current.Use(); }
        var pageLabel = new GUIStyle { fontSize = Mathf.RoundToInt(10 * S), alignment = TextAnchor.MiddleCenter };
        pageLabel.normal.textColor = Color.black;
        GUI.Label(new Rect(x + 20 * S, y + 185 * S, 226 * S, 16 * S), "快捷格 " + (quickPage * 8 + 1) + "–" + Mathf.Min(26, quickPage * 8 + 8) + " / 26", pageLabel);
        if (NativeUi.Button(new Rect(x + 12 * S, y + 207 * S, 18 * S, 18 * S), "UI/Basic.img/BtUP"))
        { quickPage = (quickPage + 3) % 4; quickIndex = quickPage * 8; }
        if (NativeUi.Button(new Rect(x + 236 * S, y + 207 * S, 18 * S, 18 * S), "UI/Basic.img/BtDown"))
        { quickPage = (quickPage + 1) % 4; quickIndex = quickPage * 8; }
        var ok = new Rect(x + 109 * S, y + 207 * S, 47 * S, 19 * S);
        var tex = T("BtOK_normal");
        if (tex != null) GUI.DrawTexture(ok, tex);
        if (Event.current.type == EventType.Repaint && Pointer.Released && ok.Contains(Pointer.Pos)) quickEdit = false;
    }

    static void ResetDraft()
    {
        draft = KeyMap.Defaults();
        quickDraft = QuickBarKeys.Defaults();
        skillDraft.Clear();
    }

    static void DrawConfirmation()
    {
        var rect = R((629 - 264) / 2f, (373 - 132) / 2f, 264, 132);
        var bg = T("confirm_backgrnd");
        if (bg != null) GUI.DrawTexture(rect, bg);
        var style = new GUIStyle { fontSize = Mathf.RoundToInt(12 * S), alignment = TextAnchor.MiddleCenter, wordWrap = true };
        style.normal.textColor = Color.black;
        GUI.Label(new Rect(rect.x + 16 * S, rect.y + 32 * S, 232 * S, 48 * S),
            confirmMode == 1 ? "要恢复默认键位和快捷键设置吗？" : "要清除全部可配置键位吗？", style);
        for (int i = 0; i < 2; i++)
        {
            var button = new Rect(rect.x + (55 + i * 82) * S, rect.y + 90 * S, 65 * S, 24 * S);
            bool hover = button.Contains(Pointer.Pos);
            var texture = T("confirm_" + (i == 0 ? "yes_" : "no_") + (hover ? Pointer.Down ? "pressed" : "mouseOver" : "normal"));
            if (texture != null) GUI.DrawTexture(button, texture);
            if (Event.current.type != EventType.Repaint || !Pointer.Released || !hover) continue;
            if (i == 0)
            {
                if (confirmMode == 1) ResetDraft();
                else foreach (var action in new List<GameAction>(draft.Keys))
                    if (action > GameAction.Down && action != GameAction.Menu) draft[action] = KeyCode.None;
            }
            confirmMode = 0;
        }
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        { confirmMode = 0; Event.current.Use(); }
    }

    // 一次按下只做一件事：手上有功能就放下，没有就拿起。
    // 拾取/放下都走 Pointer.Pressed，所以「按住拖动」在这条路径上不再参与——
    // 触屏也就不会被 350ms 长按门槛挡住。
    static void PointerStep()
    {
        if (!Pointer.Pressed) return;
        if (carried.HasValue)
        {
            if (!DropAt(Pointer.Pos)) swallow = true;     // 点空白 = 取消，别顺手按到底部按钮
            return;
        }
        if (quickEdit) return;                            // 快捷格子窗自己的按下在 DrawQuickEdit 里处理
        PickAt(Pointer.Pos);
    }

    // 光标：悬停在能拿的功能上 = 张开手（Cursor/11），手上有东西 = 握拳（Cursor/12）
    static void UpdateCursor()
    {
        if (confirmMode != 0) return;
        if (carried.HasValue) { NativeCursor.Request(NativeCursor.StateHold); return; }
        if (Pointer.Down || Pointer.RightDown) return;    // 按下时让默认的「按下手」生效
        if (PickableAt(Pointer.Pos)) NativeCursor.Request(NativeCursor.StateGrab);
    }

    public static void Draw()
    {
        if (!Visible) return;
        EnsurePos();
        pos.x = Mathf.Clamp(pos.x, 0, Mathf.Max(0, UiKit.W - 629 * S));
        pos.y = Mathf.Clamp(pos.y, 0, Mathf.Max(0, UiKit.H - 373 * S));
        bool tick = confirmMode == 0 && Event.current.type == EventType.Repaint && !(quickEdit && QuickRect.Contains(Pointer.Pos));
        if (tick && Input.GetKeyDown(KeyCode.Escape))
        {
            // 手上还拿着功能时，Esc 只放下（取消这次搬运），不关窗
            if (carried.HasValue) { carried = null; return; }
            Close();
            return;
        }
        if (tick && Pointer.Pressed && !carried.HasValue && R(0, 0, 590, 22).Contains(Pointer.Pos))
        { dragging = true; offset = Pointer.Pos - pos; }
        if (dragging && Pointer.Down)
        {
            pos = Pointer.Pos - offset;
            pos.x = Mathf.Clamp(pos.x, 0, Mathf.Max(0, UiKit.W - 629 * S));
            pos.y = Mathf.Clamp(pos.y, 0, Mathf.Max(0, UiKit.H - 373 * S - BottomBar.Height));
        }
        if (!Pointer.Down) dragging = false;
        if (tick) PointerStep();
        var bg = T("backgrnd");
        if (bg != null) GUI.DrawTexture(R(0, 0, 629, 373), bg);
        foreach (var key in NativeKeyLayout.Positions)
        {
            var code = NativeKeyLayout.FunctionKey(key.Key);
            var r = R(key.Value.x, key.Value.y, 32, 32);
            foreach (var kv in draft)
            {
                if (kv.Value != code || code == KeyCode.None) continue;
                if (carried.HasValue && kv.Key == carried.Value) continue;    // 拿在手上的先离开原位
                Texture2D icon;
                int index = QuickSlots.Index(kv.Key);
                if (skillDraft.TryGetValue(index, out var skillId))
                {
                    var skill = SkillBook.Get(skillId);
                    icon = skill != null ? Art.Tex(skill.icon) : null;
                }
                else icon = Icon(kv.Key);
                if (icon != null) GUI.DrawTexture(r, icon, ScaleMode.ScaleToFit);
            }

            if (tick && quickEdit && Pointer.Pressed && r.Contains(Pointer.Pos))
            {
                selected = code;
                SetQuickKey(code);
            }
        }
        for (int i = 0; i < palette.Length; i++)
        {
            if (!InTray(palette[i])) continue;
            if (carried.HasValue && carried.Value == palette[i]) continue;    // 同理：托盘里也先空出来
            var point = NativeKeyLayout.TrayPosition(palette[i]);
            var r = R(point.x, point.y, 32, 32);
            var icon = DraftIcon(palette[i]);
            if (icon == null) continue;
            GUI.DrawTexture(r, icon, ScaleMode.ScaleToFit);
        }
        if (Button("BtClose", 609, 6, 12, 12) || Button("BtCancel", 58, 236, 47, 19)) { Close(); return; }
        if (Button("BtOK", 8, 236, 47, 19))
        {
            QuickBarKeys.Save(quickDraft);
            KeyMap.Apply(draft);
            foreach (var kv in skillDraft) QuickSlots.Bind(kv.Key, kv.Value);
            if (GameSession.InGame) GameSession.SaveToDb();
            Close(); return;
        }
        if (Button("BtDefault", 112, 236, 65, 19)) { confirmMode = 1; carried = null; }
        if (Button("BtDelete", 177, 236, 71, 19)) { confirmMode = 2; carried = null; }
        if (Button("BtQuickSlot", 260, 236, 96, 18)) { quickEdit = !quickEdit; carried = null; }
        if (quickEdit) DrawQuickEdit();
        if (confirmMode != 0) DrawConfirmation();
        // 手上的功能跟着光标走（松手也还在手上，直到下一次点击放下）
        if (carried.HasValue)
        {
            var icon = DraftIcon(carried.Value);
            if (icon != null) GUI.DrawTexture(new Rect(Pointer.Pos.x - 16 * S, Pointer.Pos.y - 16 * S, 32 * S, 32 * S), icon);
        }
        UpdateCursor();
        if (Pointer.Released) swallow = false;
    }
}
