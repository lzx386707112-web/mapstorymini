using UnityEngine;

// Medal catalog/rewards are QuestInfo/Act, never a hardcoded list or free award.
public static class NativeMedalWindow {
    public static bool Visible;
    static Vector2 scroll;
    static int tab;
    public static void Toggle() { Visible = !Visible; }
    public static void Wear(int item, bool unequip) {
        // 勋章也属于「面板上的操作」：穿不上要弹提示框说原因，不能只闪一行字
        if (item / 10000 != 114 || Profile.ItemCount(item) <= 0 && Profile.EquipOfSlot(EquipSlot.Medal) != item) { NativeUtilPrompt.Notice("没有持有这枚勋章"); return; }
        // 门槛按面板最终属性判（与 EquipLoadout.Equip 同口径），否则靠装备加成够了也会被拦。
        string why = EquipCatalog.WhyNot(item, EquipLoadout.PanelAttributes(), Profile.JobId); if (!unequip && why != null) { NativeUtilPrompt.Notice(why); return; }
        var result = GameSession.Net.ChangeInventory(new InventoryChangeRequest { characterId = GameSession.CharacterId, itemId = item, equip = true, equipSlot = -49, quantity = unequip ? 0 : 1 });
        if (!result.ok) { NativeUtilPrompt.Notice(result.error); return; }
        GameSession.Character.inventory = result.inventory;
        GameSession.ApplyInventoryToProfile();
        Profile.Save();
    }
    public static void Draw() {
        if (!Visible) return;
        var r = new Rect(20, 20, Mathf.Min(600, UiKit.W - 40), Mathf.Min(470, UiKit.H - BottomBar.Height - 40)); GUI.Box(r, "");
        var old = GUI.skin.font; GUI.skin.font = UiKit.Font;
        GUILayout.BeginArea(new Rect(r.x + 12, r.y + 10, r.width - 24, r.height - 20));
        GUILayout.BeginHorizontal(); GUILayout.Label("勋章"); if (GUILayout.Button("关闭", GUILayout.Width(60))) Visible = false; GUILayout.EndHorizontal();
        tab = GUILayout.Toolbar(tab, new[] { "全部", "挑战中", "已完成", "已持有" });
        scroll = GUILayout.BeginScrollView(scroll);
        foreach (int id in QuestCatalog.Ids) {
            var q = QuestCatalog.Get(id); if (q.medalItem <= 0) continue;
            bool started = Profile.IsQuestStarted(id), done = Profile.IsQuestCompleted(id), held = Profile.ItemCount(q.medalItem) > 0 || Profile.EquipOfSlot(EquipSlot.Medal) == q.medalItem;
            if (tab == 1 && !started || tab == 2 && !done || tab == 3 && !held) continue;
            GUILayout.Label(q.name + (done ? " · 已完成" : started ? " · 挑战中" : ""));
            GUILayout.Label(WzNames.Item(q.medalItem));
            GUILayout.BeginHorizontal();
            if (held) { bool worn = Profile.EquipOfSlot(EquipSlot.Medal) == q.medalItem; if (GUILayout.Button(worn ? "卸下勋章" : "装备勋章")) Wear(q.medalItem, worn); }
            if (!done && GUILayout.Button(started ? "检查并领取" : "开始挑战")) {
                var req = started ? q.completeReq : q.startReq;
                string reason; bool valid = started ? QuestLogic.CanComplete(q, req.npc, out reason) : QuestLogic.CanStart(q, req.npc, out reason);
                if (!valid) BottomBar.Toast(reason);
                else { NpcField.OpenQuest(new NpcField.QuestChoice { quest = q, npcId = req.npc, complete = started }); Visible = false; }
            }
            if (started && GUILayout.Button("放弃")) { Profile.ResetQuest(id); NpcField.InvalidateMarkers(); }
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView(); GUILayout.EndArea(); GUI.skin.font = old;
    }
}
