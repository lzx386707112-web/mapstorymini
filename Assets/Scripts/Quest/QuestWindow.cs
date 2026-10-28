using System.Collections.Generic;
using UnityEngine;

// 任务日志：左列进行中/已完成，右列任务说明与奖励。
// 使用原版 UIWindow.img/Quest 的两张底图（245x396 + 305x396）。
public static class QuestWindow
{
    public static bool Visible;
    static int tab;                 // 0 进行中 / 1 已完成
    static int selectedId;
    static Vector2 listScroll, detailScroll;
    static GUIStyle title, row, body, small, section;

    const float S = 1.25f;

    public static void Toggle()
    {
        Visible = !Visible;
        if (Visible) selectedId = 0;
    }

    public static void Draw()
    {
        if (!Visible) return;
        EnsureStyles();

        var quests = tab == 0 ? (IReadOnlyList<int>)Profile.StartedQuests : Profile.CompletedQuests;
        if (selectedId == 0 && quests.Count > 0) selectedId = quests[0];

        float w = (245 + 305) * S, h = 396 * S;
        float x = (UiKit.W - w) * 0.5f;
        float y = Mathf.Max(20f, (UiKit.H - BottomBar.Height) * 0.5f - h * 0.5f);

        var left = new Rect(x, y, 245 * S, h);
        var right = new Rect(x + 245 * S, y, 305 * S, h);
        DrawTex("wz://UI/UIWindow.img/Quest/backgrnd", left);
        DrawTex("wz://UI/UIWindow.img/Quest/backgrnd2", right);

        // 标题 + 关闭
        title.normal.textColor = new Color(0.95f, 0.88f, 0.55f);
        GUI.Label(new Rect(left.x + 14, left.y + 8, 160, 22), "任务日志", title);
        var close = new Rect(right.xMax - 40 * S, right.y + 8 * S, 28 * S, 22 * S);
        if (NativeUi.Button(close, "UI/UIWindow.img/Quest/BtOK"))
        {
            Visible = false;
            return;
        }

        // 页签
        Tab(new Rect(left.x + 14, left.y + 34, 100, 24), "进行中", tab == 0, 0);
        Tab(new Rect(left.x + 120, left.y + 34, 100, 24), "已完成", tab == 1, 1);

        // 列表
        var listRect = new Rect(left.x + 12, left.y + 66, left.width - 24, left.height - 90);
        float rowH = 26f;
        float contentH = Mathf.Max(listRect.height, quests.Count * rowH + 4);
        listScroll = GUI.BeginScrollView(listRect, listScroll, new Rect(0, 0, listRect.width - 16, contentH));
        for (int i = 0; i < quests.Count; i++)
        {
            int id = quests[i];
            var def = QuestCatalog.Get(id);
            var r = new Rect(0, i * rowH, listRect.width - 20, rowH - 2);
            bool hot = r.Contains(Pointer.Pos);
            bool on = id == selectedId;
            if (on || hot) UiKit.Fill(r, on ? new Color(0.25f, 0.35f, 0.5f, 0.55f) : new Color(0.2f, 0.25f, 0.35f, 0.35f));
            row.normal.textColor = on ? new Color(1f, 0.92f, 0.6f) : new Color(0.9f, 0.92f, 0.96f);
            GUI.Label(new Rect(r.x + 6, r.y + 2, r.width - 8, r.height), def != null ? def.name : ("任务 " + id), row);
            if (Event.current.type == EventType.Repaint && Pointer.Released && hot) selectedId = id;
        }
        GUI.EndScrollView();

        if (quests.Count == 0)
        {
            small.normal.textColor = new Color(0.72f, 0.74f, 0.8f);
            GUI.Label(new Rect(left.x + 20, left.y + 90, left.width - 40, 60),
                      tab == 0 ? "还没有进行中的任务。\n去和头顶带 ! 的 NPC 对话。" : "还没有已完成的任务。", small);
        }

        DrawDetail(right);
    }

    static void DrawDetail(Rect right)
    {
        var def = selectedId > 0 ? QuestCatalog.Get(selectedId) : null;
        if (def == null) return;

        float pad = 16 * S;
        title.normal.textColor = new Color(0.95f, 0.88f, 0.55f);
        GUI.Label(new Rect(right.x + pad, right.y + 12 * S, right.width - pad * 2, 26), def.name, title);

        // 正文
        var bodyRect = new Rect(right.x + pad, right.y + 44 * S, right.width - pad * 2, 150 * S);
        string text = def.info.Length >= 1 ? def.info[0] : "";
        float th = body.CalcHeight(new GUIContent(text), bodyRect.width);
        detailScroll = GUI.BeginScrollView(bodyRect, detailScroll, new Rect(0, 0, bodyRect.width - 16, Mathf.Max(bodyRect.height, th)));
        body.normal.textColor = new Color(0.12f, 0.12f, 0.14f);
        GUI.Label(new Rect(0, 0, bodyRect.width - 18, th), text, body);
        GUI.EndScrollView();

        // 条件 / 奖励
        float y = bodyRect.yMax + 6;
        section.normal.textColor = new Color(0.35f, 0.25f, 0.12f);
        GUI.Label(new Rect(right.x + pad, y, 200, 18), "条件", section);
        y += 18;
        small.normal.textColor = new Color(0.18f, 0.18f, 0.2f);
        var req = tab == 0 ? def.completeReq : def.startReq;
        if (req.level > 0) { GUI.Label(new Rect(right.x + pad, y, 240, 16), "等级 " + req.level, small); y += 16; }
        for (int i = 0; i < req.items.Count; i++)
        {
            GUI.Label(new Rect(right.x + pad, y, 260, 16),
                      WzNames.Item(req.items[i].item) + " x" + req.items[i].count +
                      "（" + Profile.ItemCount(req.items[i].item) + "）", small);
            y += 16;
        }
        for (int i = 0; i < req.mobs.Count; i++)
        {
            int have = Profile.QuestProgressInt(def.id, req.mobs[i].item.ToString());
            GUI.Label(new Rect(right.x + pad, y, 260, 16),
                      WzNames.Mob(req.mobs[i].item) + " " + have + "/" + req.mobs[i].count, small);
            y += 16;
        }

        var act = tab == 0 ? def.completeAct : def.startAct;
        y += 4;
        section.normal.textColor = new Color(0.35f, 0.25f, 0.12f);
        GUI.Label(new Rect(right.x + pad, y, 200, 18), "奖励", section);
        y += 18;
        small.normal.textColor = new Color(0.18f, 0.18f, 0.2f);
        if (act.exp > 0) { GUI.Label(new Rect(right.x + pad, y, 240, 16), "经验 " + act.exp, small); y += 16; }
        if (act.money != 0) { GUI.Label(new Rect(right.x + pad, y, 240, 16), "金币 " + act.money, small); y += 16; }
        for (int i = 0; i < act.items.Count; i++)
        {
            if (act.items[i].count <= 0) continue;
            GUI.Label(new Rect(right.x + pad, y, 260, 16), WzNames.Item(act.items[i].item) + " x" + act.items[i].count, small);
            y += 16;
        }

        // 放弃任务
        if (tab == 0 && Profile.IsQuestStarted(def.id))
        {
            var btn = new Rect(right.x + pad, right.yMax - 40 * S, 90, 24);
            if (NativeUi.Button(btn, "UI/UIWindow.img/Quest/BtGiveup"))
            {
                Profile.ResetQuest(def.id);
                BottomBar.Toast("已放弃：" + def.name);
                selectedId = 0;
                NpcField.InvalidateMarkers();
            }
            GUI.Label(new Rect(btn.xMax + 8, btn.y + 2, 120, 20), "放弃任务", small);
        }
    }

    static void Tab(Rect r, string text, bool on, int index)
    {
        bool hot = r.Contains(Pointer.Pos);
        UiKit.Fill(r, on ? new Color(0.32f, 0.34f, 0.42f, 0.95f) : hot ? new Color(0.26f, 0.28f, 0.34f, 0.9f) : new Color(0.18f, 0.19f, 0.23f, 0.85f));
        row.normal.textColor = on ? new Color(1f, 0.9f, 0.55f) : new Color(0.82f, 0.85f, 0.9f);
        GUI.Label(r, text, row);
        if (Event.current.type == EventType.Repaint && Pointer.Released && hot && !on)
        {
            tab = index;
            selectedId = 0;
            listScroll = Vector2.zero;
            detailScroll = Vector2.zero;
        }
    }

    static void DrawTex(string path, Rect r)
    {
        var tex = Art.Tex(path);
        if (tex != null) GUI.DrawTexture(r, tex, ScaleMode.StretchToFill);
        else UiKit.Fill(r, new Color(0.12f, 0.13f, 0.17f, 0.95f));
    }

    static void EnsureStyles()
    {
        if (title != null) return;
        title = new GUIStyle { fontSize = 16, fontStyle = FontStyle.Bold };
        row = new GUIStyle { fontSize = 12, alignment = TextAnchor.MiddleLeft };
        body = new GUIStyle { fontSize = 12, wordWrap = true, richText = true, alignment = TextAnchor.UpperLeft };
        small = new GUIStyle { fontSize = 11, wordWrap = true };
        section = new GUIStyle { fontSize = 12, fontStyle = FontStyle.Bold };
    }
}
