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
        var actions = new List<GameAction> { GameAction.Equip, GameAction.Inven, GameAction.Stat,
            GameAction.Skill, GameAction.KeySet, GameAction.Shortcut, GameAction.Pickup,
            GameAction.Attack, GameAction.Jump };
        return actions.ToArray();
    }
    static KeyCode[] quickDraft;
    static int quickPage;
    static readonly Dictionary<int, int> skillDraft = new Dictionary<int, int>();
    static Vector2 pos = new Vector2(-1, -1), offset;
    static bool dragging, quickEdit;
    static int quickIndex, confirmMode;
    static GameAction? carried;
    static KeyCode selected;
    static float S => Mathf.Min(BottomBar.S, (UiKit.W - 16f) / 629f, (UiKit.H - 16f) / 373f);
    static Rect R(float x, float y, float w, float h)
        => new Rect(pos.x + x * S, pos.y + y * S, w * S, h * S);
    static Texture2D T(string name) => Art.Tex("ui/keyconfig/" + name + ".bytes");

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
        skillDraft.Clear();
        quickEdit = false;
        selected = KeyCode.None;
        InputHub.ReleaseAllVirtual();
        TouchInput.Reset();
    }

    static void Close() { Visible = false; confirmMode = 0; carried = null; dragging = false; skillDraft.Clear(); }

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
        int id;
        switch (action)
        {
            case GameAction.Equip: id = 0; break;
            case GameAction.Inven: id = 1; break;
            case GameAction.Stat: id = 2; break;
            case GameAction.Skill: id = 3; break;
            case GameAction.KeySet: id = 9; break;
            case GameAction.Menu: return null;
            case GameAction.Shortcut: id = 15; break;
            case GameAction.Help: return null;
            case GameAction.Pickup: id = 50; break;
            case GameAction.Attack: id = 52; break;
            case GameAction.Jump: id = 53; break;
            default: return null;
        }
        return T("icon_" + id);
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
        return confirmMode == 0 && Event.current.type == EventType.Repaint && Pointer.Released && hover && !(quickEdit && QuickRect.Contains(Pointer.Pos));
    }

    public static KeyCode HitKey(Vector2 point)
    {
        if (!Visible || confirmMode != 0 || quickEdit && QuickRect.Contains(point)) return KeyCode.None;
        foreach (var key in NativeKeyLayout.Positions)
            if (R(key.Value.x, key.Value.y, 32, 32).Contains(point)) return NativeKeyLayout.FunctionKey(key.Key);
        return KeyCode.None;
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
            if (quickIndex == i) UiKit.Fill(r, new Color(0.3f, 0.6f, 1, 0.3f));
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
        var pageStyle = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(10 * S) };
        if (GUI.Button(new Rect(x + 12 * S, y + 207 * S, 80 * S, 19 * S), "‹ 上一页", pageStyle))
        { quickPage = (quickPage + 3) % 4; quickIndex = quickPage * 8; }
        if (GUI.Button(new Rect(x + 174 * S, y + 207 * S, 80 * S, 19 * S), "下一页 ›", pageStyle))
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

    public static void Draw()
    {
        if (!Visible) return;
        if (pos.x < 0) pos = new Vector2((UiKit.W - 629 * S) / 2, Mathf.Max(8, (UiKit.H - 373 * S) / 2));
        pos.x = Mathf.Clamp(pos.x, 0, Mathf.Max(0, UiKit.W - 629 * S));
        pos.y = Mathf.Clamp(pos.y, 0, Mathf.Max(0, UiKit.H - 373 * S));
        bool tick = confirmMode == 0 && Event.current.type == EventType.Repaint && !(quickEdit && QuickRect.Contains(Pointer.Pos));
        if (tick && Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
        if (tick && Pointer.Pressed && R(0, 0, 590, 22).Contains(Pointer.Pos))
        { dragging = true; offset = Pointer.Pos - pos; }
        if (dragging && Pointer.Down)
        {
            pos = Pointer.Pos - offset;
            pos.x = Mathf.Clamp(pos.x, 0, Mathf.Max(0, UiKit.W - 629 * S));
            pos.y = Mathf.Clamp(pos.y, 0, Mathf.Max(0, UiKit.H - 373 * S - BottomBar.Height));
        }
        if (!Pointer.Down) dragging = false;
        var bg = T("backgrnd");
        if (bg != null) GUI.DrawTexture(R(0, 0, 629, 373), bg);
        foreach (var key in NativeKeyLayout.Positions)
        {
            var code = NativeKeyLayout.FunctionKey(key.Key);
            var r = R(key.Value.x, key.Value.y, 32, 32);
            foreach (var kv in draft)
            {
                if (kv.Value != code || code == KeyCode.None) continue;
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
            if (selected == code) UiKit.Fill(r, new Color(0.2f, 0.5f, 1f, 0.24f));
            if (tick && Pointer.Pressed && r.Contains(Pointer.Pos))
            {
                selected = code;
                if (quickEdit) { SetQuickKey(code); continue; }
                foreach (var kv in draft)
                    if (kv.Value == code && DraftIcon(kv.Key) != null) { carried = kv.Key; break; }
            }
        }
        for (int i = 0; i < palette.Length; i++)
        {
            if (!InTray(palette[i])) continue;
            var point = NativeKeyLayout.TrayPosition(palette[i]);
            var r = R(point.x, point.y, 32, 32);
            var icon = DraftIcon(palette[i]);
            if (icon == null) continue;
            GUI.DrawTexture(r, icon, ScaleMode.ScaleToFit);
            if (tick && Pointer.Pressed && r.Contains(Pointer.Pos)) carried = palette[i];
        }
        if (carried.HasValue && tick && Pointer.Released)
        {
            var key = HitKey(Pointer.Pos);
            if (key != KeyCode.None)
            {
                foreach (var a in new List<GameAction>(draft.Keys))
                    if (draft[a] == key) draft[a] = KeyCode.None;
                draft[carried.Value] = key;
            }
            else if (R(0, 262, 629, 111).Contains(Pointer.Pos)) draft[carried.Value] = KeyCode.None;
            carried = null;
        }
        if (Button("BtClose", 609, 6, 12, 12) || Button("BtCancel", 58, 236, 47, 19)) { Close(); return; }
        if (Button("BtOK", 8, 236, 47, 19))
        {
            QuickBarKeys.Save(quickDraft);
            KeyMap.Apply(draft);
            foreach (var kv in skillDraft) QuickSlots.Bind(kv.Key, kv.Value);
            Close(); return;
        }
        if (Button("BtDefault", 112, 236, 65, 19)) { confirmMode = 1; carried = null; }
        if (Button("BtDelete", 177, 236, 71, 19)) { confirmMode = 2; carried = null; }
        if (Button("BtQuickSlot", 260, 236, 96, 18)) { quickEdit = !quickEdit; carried = null; }
        if (quickEdit) DrawQuickEdit();
        if (confirmMode != 0) DrawConfirmation();
        if (carried.HasValue && Pointer.Down)
        {
            var icon = DraftIcon(carried.Value);
            if (icon != null) GUI.DrawTexture(new Rect(Pointer.Pos.x - 16 * S, Pointer.Pos.y - 16 * S, 32 * S, 32 * S), icon);
        }
    }
}
