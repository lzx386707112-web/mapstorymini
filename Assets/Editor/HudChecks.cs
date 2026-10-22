using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class HudChecks
{
    static int passed, failed;
    static void Check(bool value, string message)
    {
        if (value) passed++;
        else { failed++; Debug.LogError("[HudCheck] " + message); }
    }
    // RenderTexture shots omit IMGUI. A fixed GameView is required for real HUD screenshots.
    public static void SetGameViewSize(int width, int height)
    {
        var assembly = typeof(Editor).Assembly;
        var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
        var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
        var sizes = singleton.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
        var groupType = sizesType.GetProperty("currentGroupType", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).GetValue(sizes);
        var group = sizesType.GetMethod("GetGroup").Invoke(sizes, new[] { groupType });
        var sizeType = assembly.GetType("UnityEditor.GameViewSize");
        var modeType = assembly.GetType("UnityEditor.GameViewSizeType");
        var size = System.Activator.CreateInstance(sizeType, new object[] { System.Enum.Parse(modeType, "FixedResolution"), width, height, "HUD review " + width + "x" + height });
        group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { size });
        int index = (int)group.GetType().GetMethod("GetTotalCount").Invoke(group, null) - 1;
        var view = EditorWindow.GetWindow(assembly.GetType("UnityEditor.GameView"));
        view.GetType().GetProperty("selectedSizeIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).SetValue(view, index);
    }

    public static void Run()
    {
        passed = failed = 0;
        var raw = new Dictionary<string, int?>();
        foreach (GameAction action in System.Enum.GetValues(typeof(GameAction)))
        {
            string key = "mxd.key." + action;
            raw[key] = PlayerPrefs.HasKey(key) ? (int?)PlayerPrefs.GetInt(key) : null;
        }
        for (int i = 0; i < QuickSlots.Count; i++)
            foreach (string prefix in new[] { "mxd.qs.", "mxd.quickDisplay." })
            {
                string key = prefix + i;
                raw[key] = PlayerPrefs.HasKey(key) ? (int?)PlayerPrefs.GetInt(key) : null;
            }
        var saved = new Dictionary<GameAction, KeyCode>();
        foreach (var kv in KeyMap.All) saved[kv.Key] = kv.Value;
        try
        {
            for (char c = 'A'; c <= 'Z'; c++)
                Check(NativeKeyLayout.Positions.ContainsKey((KeyCode)((int)KeyCode.A + c - 'A')), "字母映射 " + c);
            KeyMap.ResetAll();
            Check(KeyMap.Of(GameAction.Jump) == KeyCode.LeftAlt, "DEFAULT_ACTION 53 对应 Alt");
            Check(KeyMap.Of(GameAction.Shortcut) == KeyCode.RightBracket, "DEFAULT_ACTION 15 对应右方括号，Tab是原端固定移动菜单键");
            Check(KeyMap.Of(GameAction.KeySet) == KeyCode.Backslash, "DEFAULT_ACTION 9 对应反斜杠");
            KeyMap.Bind(GameAction.Jump, KeyCode.K);
            Check(KeyMap.Of(GameAction.Skill) == KeyCode.None, "重绑应解除旧动作");
            typeof(KeyMap).GetField("loaded", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, false);
            KeyMap.Ensure();
            Check(KeyMap.Of(GameAction.Skill) == KeyCode.None, "解除绑定后重载不能恢复旧键");
            Check(KeyMap.Of(GameAction.Jump) == KeyCode.K, "新键应持久化");
            KeyMap.Bind(GameAction.Help, KeyCode.None);
            Check(KeyMap.Of(GameAction.Skill) == KeyCode.None, "清空动作不应重置其他未绑定动作");
            KeyMap.Apply(saved);
            KeyMap.Bind(GameAction.Pickup, KeyCode.Z);
            var configured = new Dictionary<GameAction, KeyCode>();
            foreach (var kv in KeyMap.All) configured[kv.Key] = kv.Value;
            var skillsBefore = new int[QuickSlots.Count];
            for (int i = 0; i < QuickSlots.Count; i++) skillsBefore[i] = QuickSlots.Id(i);
            KeyConfigWindow.Toggle();
            Check(!KeyConfigWindow.InTray(GameAction.Pickup), "已绑定的拾取不能同时出现在托盘");
            var draft = (Dictionary<GameAction, KeyCode>)typeof(KeyConfigWindow).GetField("draft", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            draft[GameAction.Pickup] = KeyCode.None;
            Check(KeyConfigWindow.InTray(GameAction.Pickup), "解除拾取后应回到托盘");
            var displayBefore = QuickBarKeys.Copy();
            typeof(KeyConfigWindow).GetMethod("SetQuickKey", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { KeyCode.Q });
            Check(KeyMap.Of(GameAction.Pickup) == configured[GameAction.Pickup], "显示键编辑不能改功能绑定");
            typeof(KeyConfigWindow).GetField("pos", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, Vector2.zero);
            float keyScale = (float)typeof(KeyConfigWindow).GetProperty("S", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            Check(KeyConfigWindow.DropSkill(new Vector2(99 * keyScale,166 * keyScale), 1001004), "技能拖到原端Z图标区域");
            typeof(KeyConfigWindow).GetMethod("ResetDraft", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            draft = (Dictionary<GameAction, KeyCode>)typeof(KeyConfigWindow).GetField("draft", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            Check(draft[GameAction.Jump] == KeyCode.LeftAlt, "BtDefault恢复默认，而不是复制打开前的自定义设置");
            Check(KeyMap.Of(GameAction.Jump) == configured[GameAction.Jump], "恢复默认尚未确定不能改存档");
            var quickDraft = (KeyCode[])typeof(KeyConfigWindow).GetField("quickDraft", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            Check(quickDraft[4] == KeyCode.LeftControl, "恢复默认也恢复快捷显示键");
            typeof(KeyConfigWindow).GetField("confirmMode", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, 1);
            Check(KeyConfigWindow.HitKey(new Vector2(99 * keyScale,166 * keyScale)) == KeyCode.None, "确认框阻止底层拖放命中");
            KeyConfigWindow.Toggle();
            foreach (var kv in configured) Check(KeyMap.Of(kv.Key) == kv.Value, "取消不能修改保存键位 " + kv.Key);
            for (int i = 0; i < QuickSlots.Count; i++) Check(QuickSlots.Id(i) == skillsBefore[i], "取消技能草稿 " + i);
            for (int i = 0; i < QuickSlots.Count; i++) Check(QuickBarKeys.Of(i) == displayBefore[i], "取消必须丢弃显示键草稿 " + i);
            Check(NativeKeyLayout.Positions[KeyCode.Z] == new Vector2(98,165), "Z使用原端图标坐标");
            Check(NativeKeyLayout.Positions[KeyCode.Space] == new Vector2(233,198), "Space图标在长键帽中间");
            Check(NativeKeyLayout.Positions[KeyCode.RightControl] == new Vector2(461,198), "扩展右Ctrl扫描码");
            Check(NativeKeyLayout.TrayPosition(GameAction.Pickup) == new Vector2(349,301), "拾取使用原端托盘功能序号28");
            Check(NativeKeyLayout.TrayPosition(GameAction.Jump) == new Vector2(451,301), "跳跃使用原端托盘功能序号31");
            Check(NativeKeyLayout.Positions[KeyCode.RightShift] == new Vector2(457,165), "原端右Shift图标坐标");
            KeyMap.Bind(GameAction.Attack, KeyCode.RightControl);
            Check(KeyMap.Of(GameAction.Attack) == KeyCode.LeftControl, "左右Ctrl对应同一原端功能");
            Check(NativeKeyLayout.FunctionKey(KeyCode.RightAlt) == KeyCode.LeftAlt, "左右Alt功能一致");
            Check(QuickSlots.Count == 26, "扩展端26个技能绑定");
            for (int i = 0; i < QuickSlots.Count; i++) Check(QuickSlots.Index(QuickSlots.Action(i)) == i, "保存枚举兼容 " + i);
            foreach (var size in new[] { new Vector2(800,600), new Vector2(1024,768), new Vector2(1280,720), new Vector2(1366,768), new Vector2(1920,1080) })
            {
                float scale = BottomBar.ScaleFor(size.x,size.y);
                Check(1281 * scale <= size.x + 0.01f, "小屏整组不越界 " + size);
                Check(373 * scale < size.y, "键位窗口保留可见空间 " + size);
            }
            Check(BottomBar.ScaleFor(1366,768) > 1f, "1366允许放大底栏");
            Check(BottomBar.ScaleFor(1920,1080) > 1.49f, "大屏底栏随视口放大");
            var display = QuickBarKeys.Copy();
            display[0] = KeyCode.Q;
            QuickBarKeys.Save(display);
            KeyMap.Bind(GameAction.Attack, KeyCode.Q);
            Check(QuickBarKeys.Function(QuickBarKeys.Of(0)) == GameAction.Attack, "显示格查询当前功能");
            KeyMap.Bind(GameAction.Attack, KeyCode.R);
            Check(QuickBarKeys.Of(0) == KeyCode.Q && QuickBarKeys.Function(KeyCode.Q) == null, "重绑功能不能改显示键");
            QuickSlots.Bind(25, 1001004);
            KeyMap.Bind(QuickSlots.Action(25), KeyCode.Q);
            Check(QuickBarKeys.SkillSlot(KeyCode.Q) == 25, "第26个技能绑定可解析");
            QuickBarKeys.Save(displayBefore);
            KeyMap.Apply(saved);
            UiKit.RefreshMetrics();
            for (int i = 0; i < QuickSlots.Count; i++)
            {
                var panel = BottomBar.QuickPanelRect();
                var cell = BottomBar.SlotRect(i);
                Check(cell.xMin >= panel.xMin && cell.xMax <= panel.xMax && cell.yMax <= panel.yMax, "快捷格越界 " + i);
            }
            foreach (string path in new[] { "keyconfig/backgrnd", "keyconfig/BtOK_normal", "keyconfig/BtCancel_pressed", "keyconfig/BtDefault_mouseOver", "gamemenu/backgrnd", "shortcut/BtSkill_normal", "bar/backgrnd2_full", "bar/quickSlot26", "keyconfig/confirm_backgrnd", "keyconfig/confirm_yes_normal", "keyconfig/confirm_no_pressed" })
                Check(Art.Tex("ui/" + path + ".bytes") != null, "缺少原图 " + path);
        }
        finally
        {
            if (KeyConfigWindow.Visible) KeyConfigWindow.Toggle();
            foreach (var kv in raw)
                if (kv.Value.HasValue) PlayerPrefs.SetInt(kv.Key, kv.Value.Value);
                else PlayerPrefs.DeleteKey(kv.Key);
            PlayerPrefs.Save();
            typeof(KeyMap).GetField("loaded", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, false);
            typeof(QuickSlots).GetField("loaded", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, false);
        }
        Debug.Log("[HudCheck] " + passed + " passed, " + failed + " failed");
        EditorApplication.Exit(failed == 0 ? 0 : 1);
    }
}
