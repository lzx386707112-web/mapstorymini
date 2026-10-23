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
}
