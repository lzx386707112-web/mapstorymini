using System;
using System.Collections.Generic;
using UnityEngine;

// 排行榜窗口。素材只有 UI/UIWindow.img/Ranking：backgrnd 295x335（顶部印着 MY RANKING 的
// 白条 + 一条蓝色标签带 + 红线），Tab/enabled|disabled 各只有 0 号一张 53x14 的页签板（无文字）。
// 所以页签用真素材当底板、文字另叠，列表与「我的名次」都排在红线以下的空白区。
//
// 数据来自 NativeRankingService：榜单口径 = getWorldRankPlayers（level↓/exp↓/lastExpGainTime↑，
// 前 50），名次口径 = RankingLoginTask 维护的 rank/rankMove/jobRank/jobRankMove。
public static class NativeRankingWindow
{
    const string Root = "UI/UIWindow.img/Ranking/";
    const float W = 295, H = 335;
    const float RowH = 14f;
    const int RowsVisible = 20;
    const float ListX = 10, ListY = 46, ListW = 268, ListH = H - ListY - 8;

    static bool visible;
    static int tab;                 // 0 综合 / 1 职业
    static float scroll;
    static List<RankingEntry> entries = new List<RankingEntry>();
    static RankingResponse info;
    static string hint = "";
    static Vector2 position = new Vector2(250, 50), grab;
    static bool dragging;

    public static bool Visible => visible;

    public static void Reset()
    {
        visible = false; tab = 0; scroll = 0;
        entries = new List<RankingEntry>(); info = null; hint = "";
    }

    public static void Close() => Reset();
    public static void Toggle() { if (visible) Close(); else Open(); }

    public static void Open()
    {
        if (!GameSession.InGame) return;
        Reset();
        if (!Fetch()) return;
        visible = true;
    }

    static bool Fetch()
    {
        if (!GameSession.InGame) return false;
        var job = GameSession.Character != null ? GameSession.Character.record.job : 0;
        int jobGroup = tab == 1 ? job / 100 : -1;
        var res = GameSession.Net.Ranking(new RankingRequest {
            characterId = GameSession.CharacterId, operation = RankingOperation.Top, jobGroup = jobGroup });
        if (!res.ok) { BottomBar.Toast(res.error); return false; }
        info = res;
        entries = res.entries ?? new List<RankingEntry>();
        hint = entries.Count == 0 ? "暂无排行数据" : "";
        scroll = 0;
        return true;
    }

    // ————————————————— 绘制 —————————————————

    static GUIStyle Label(bool right = false, int size = 10)
    {
        var style = new GUIStyle { font = UiKit.Font, fontSize = size, padding = new RectOffset(), clipping = TextClipping.Clip,
            alignment = right ? TextAnchor.UpperRight : TextAnchor.UpperLeft, wordWrap = false };
        style.normal.textColor = Color.black; return style;
    }

    static GUIStyle TabLabel()
    {
        var style = new GUIStyle { font = UiKit.Font, fontSize = 10, padding = new RectOffset(), clipping = TextClipping.Clip,
            alignment = TextAnchor.MiddleCenter, wordWrap = false };
        style.normal.textColor = Color.black; return style;
    }

    static void Image(string node, float x, float y)
    {
        var texture = Art.Tex("wz://" + node);
        if (texture != null) GUI.DrawTexture(new Rect(x, y, texture.width, texture.height), texture);
    }

    static string Move(int move)
    {
        if (move > 0) return " ▲" + move;
        if (move < 0) return " ▼" + (-move);
        return "";
    }

    static void Tabs(Vector2 p)
    {
        string[] names = { "综合", "职业" };
        for (int i = 0; i < names.Length; i++)
        {
            var rect = new Rect(12 + i * 56, 23, 53, 14);
            // Tab/enabled 只有一个 0 号板：选中的用 enabled，未选中的用 disabled。
            Image(Root + "Tab/" + (tab == i ? "enabled" : "disabled") + "/0", rect.x, rect.y);
            GUI.Label(rect, names[i], TabLabel());
            if (GUI.enabled && Event.current.type == EventType.Repaint && Pointer.Released && rect.Contains(p))
            {
                Pointer.ConsumeClick();
                if (tab != i) { tab = i; Fetch(); }
                return;
            }
        }
    }

    static void DrawList(Vector2 p)
    {
        int max = Math.Max(0, entries.Count - RowsVisible);
        scroll = NativeUi.Scroll4(new Rect(ListX + ListW - 2, ListY, 12, ListH), new Rect(ListX, ListY, ListW, ListH), scroll, max, 1, p);
        for (int i = 0; i < RowsVisible; i++)
        {
            int index = i + (int)scroll;
            if (index >= entries.Count) break;
            var entry = entries[index];
            float y = ListY + i * RowH;
            if (entry.name == (info != null && GameSession.Character != null ? GameSession.Character.record.name : null))
                GUI.Box(new Rect(ListX, y - 1, ListW - 12, RowH), "");
            GUI.Label(new Rect(ListX, y, 26, 13), entry.rank.ToString(), Label(true));
            GUI.Label(new Rect(ListX + 30, y, 118, 13), entry.name, Label());
            GUI.Label(new Rect(ListX + 150, y, 32, 13), entry.level.ToString(), Label(true));
            GUI.Label(new Rect(ListX + 186, y, 68, 13), JobDefs.NameOf(entry.job), Label());
            GUI.Label(new Rect(ListX + 186 + 56, y, 26, 13), Move(entry.rankMove), Label(true));
        }
        if (!string.IsNullOrEmpty(hint)) GUI.Label(new Rect(ListX, ListY + 4, ListW, 13), hint, Label());
    }

    public static void Draw()
    {
        if (!visible) return;
        if (!GameSession.InGame) { Reset(); return; }

        float scale = BottomBar.S;
        position = NativeUi.ClampWindowPosition(position, new Vector2(W, H), scale);
        NativeUi.RegisterWindow("NativeRankingWindow", new Rect(position.x * scale, position.y * scale, W * scale, H * scale), () => Visible);
        var pointer = Pointer.Pos / scale; var matrix = GUI.matrix;
        GUI.matrix = matrix * Matrix4x4.Scale(new Vector3(scale, scale, 1));
        try
        {
            if (GUI.enabled && Event.current.type == EventType.Repaint && Pointer.Pressed && new Rect(position.x, position.y, W, 16).Contains(pointer))
            { dragging = true; grab = pointer - position; }
            if (dragging && Pointer.Down) position = NativeUi.ClampWindowPosition(pointer - grab, new Vector2(W, H), scale);
            if (!Pointer.Down) dragging = false;
            GUI.BeginGroup(new Rect(position.x, position.y, W, H));
            try
            {
                var p = pointer - position;
                Image(Root + "backgrnd", 0, 0);
                GUI.Label(new Rect(12, 7, 120, 13), info != null && info.wholeServer ? "全服排行" : "我的排行", Label());
                if (info != null)
                    GUI.Label(new Rect(140, 7, W - 152, 13),
                        "综合 #" + info.myRank + Move(info.myRankMove) + "   职业 #" + info.myJobRank + Move(info.myJobRankMove),
                        Label(true));
                if (NativeUi.Button(new Rect(W - 22, 4, 16, 16), "UI/UIWindow.img/BtUIClose2", p)) { Close(); return; }
                Tabs(p);
                DrawList(p);
            }
            finally { GUI.EndGroup(); }
        }
        finally { GUI.matrix = matrix; }
    }
}
