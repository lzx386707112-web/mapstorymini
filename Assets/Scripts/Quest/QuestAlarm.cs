using System.Collections.Generic;
using UnityEngine;

// CUIQuestAlarm: 885165 controls, 885EB8 rows (18px), up to five tracked quests.
public static class QuestAlarm
{
    static readonly List<int> tracked = new List<int>();
    static readonly Dictionary<int, float> updated = new Dictionary<int, float>();
    static bool automatic = true, visible = true, expanded;
    static Vector2 position = new Vector2(float.NaN, 35), grab;
    static bool dragging;
    public static IReadOnlyList<int> Tracked => tracked;
    public static bool CanTrack(QuestDef quest) => quest != null && tracked.Count < 5 && !tracked.Contains(quest.id) &&
        (quest.completeReq.mobs.Count > 0 || quest.completeReq.items.Count > 0 || quest.completeReq.quests.Count > 0 || quest.completeReq.meso > 0);
    public static void Track(int id)
    {
        if (!Profile.IsQuestStarted(id)) return;
        if (!tracked.Contains(id)) { if (!CanTrack(QuestCatalog.Get(id))) return; tracked.Add(id); }
        updated[id] = Time.unscaledTime; visible = true;
    }
    public static void Progress(int id)
    {
        if (automatic || tracked.Contains(id)) Track(id);
    }
    public static void Remove(int id) { tracked.Remove(id); updated.Remove(id); }
    public static void Draw()
    {
        for (int i = tracked.Count - 1; i >= 0; i--)
        {
            int id = tracked[i];
            if (!Profile.IsQuestStarted(id) || automatic && Time.unscaledTime - updated[id] >= 600) Remove(id);
        }
        if (!visible) return;
        float scale = BottomBar.S;
        if (float.IsNaN(position.x)) position.x = UiKit.W / scale - 233;
        position = NativeUi.ClampWindowPosition(position, new Vector2(223, 25), scale);
        var header = new Rect((position.x + 25) * scale, position.y * scale, 114 * scale, 20 * scale);
        if (Event.current.type == EventType.Repaint && Pointer.Pressed && header.Contains(Pointer.Pos))
        { dragging = true; grab = Pointer.Pos / scale - position; Pointer.ConsumeClick(); }
        if (dragging && Pointer.Down)
            position = NativeUi.ClampWindowPosition(Pointer.Pos / scale - grab, new Vector2(223, 25), scale);
        if (dragging && !Pointer.Down) { dragging = false; Pointer.ConsumeClick(); }
        QuestUi.Begin(position * scale, scale);
        QuestUi.Image("QuestAlarm/" + (expanded ? "backgrndmax" : "backgrndmin"), 0, 0);
        QuestUi.Label(new Rect(25, 2, 114, 18), "任务告知(" + tracked.Count + "/5)", Color.black);
        if (QuestUi.Button("UI/UIWindow.img/QuestAlarm/BtQ", 5, 4)) QuestWindow.Toggle();
        if (QuestUi.Button("UI/UIWindow.img/QuestAlarm/BtAuto", 140, 4)) automatic = !automatic;
        if (QuestUi.Button("UI/Basic.img/" + (expanded ? "BtMin" : "BtMax"), 190, 4)) expanded = !expanded;
        if (QuestUi.Button("UI/Basic.img/BtClose", 203, 4)) visible = false;
        if (!expanded || tracked.Count == 0) return;
        var lines = new List<KeyValuePair<int, string>>();
        foreach (int id in tracked)
        {
            var q = QuestCatalog.Get(id); if (q == null) continue;
            lines.Add(new KeyValuePair<int, string>(id, q.name));
            foreach (var need in q.completeReq.mobs)
                lines.Add(new KeyValuePair<int, string>(0, WzNames.Mob(need.item) + " " + Profile.QuestProgressInt(id, need.item.ToString()) + "/" + need.count));
            foreach (var need in q.completeReq.items)
                lines.Add(new KeyValuePair<int, string>(0, WzNames.Item(need.item) + " " + Profile.ItemCount(need.item) + "/" + need.count));
        }
        float y = 25;
        foreach (var line in lines)
        {
            QuestUi.Image("QuestAlarm/backgrndcenter", 0, y);
            QuestUi.Label(new Rect(10, y, 185, 18), line.Value, Color.black);
            if (line.Key > 0 && QuestUi.Click(QuestUi.Rect(10, y, 185, 18))) QuestWindow.Select(line.Key);
            if (line.Key > 0 && QuestUi.Button("UI/Basic.img/BtClose2", 203, y - 2)) { Remove(line.Key); break; }
            y += 18;
        }
        QuestUi.Image("QuestAlarm/backgrndbottom", 0, y);
    }
}
