using UnityEngine;

// 活动竞技场 HUD（雪球大战 / 椰子树）—— 客户端表现层。
// 状态权威在 NativeEventArenaService（源端 Snowball/Coconut 事件类）；
// 这里每秒 Sync 一次拉状态，打击按钮发 Hit（雪球 what 0..3 / 椰子 id=-1）。
// 结算 warpTo → field.WarpTo（源端 warpOutByTeam 的客户端等价物）。
// 表现说明：源端雪球推进/椰子掉落是专用包驱动的地图对象动画，这里用 HUD 进度条 +
// 聊天公告等价呈现（状态机与判定忠实，见服务端文件头注）。
public static class NativeArenaHud
{
    public static bool Visible => state != null && state.kind != 0;

    static ArenaResponse state;
    static long nextSync;
    static bool warpPending;
    static int warpTarget;

    static float S => BottomBar.S;

    const int SnowballMap = NativeEventArenaService.SnowballMap;
    const int CoconutMap = NativeEventArenaService.CoconutMap;

    public static void Reset()
    {
        state = null;
        nextSync = 0;
        warpPending = false;
        warpTarget = 0;
    }

    public static void Draw()
    {
        if (GameLoop.I == null || GameLoop.I.player == null) return;
        int map = Profile.LastMapId;
        bool onArena = map == SnowballMap || map == CoconutMap;
        if (!onArena)
        {
            if (state != null) Reset();
            return;
        }

        long now = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (now >= nextSync)
        {
            nextSync = now + 1000;
            Sync();
        }

        if (warpPending && Profile.LastMapId != warpTarget)
        {
            // 已收到结算换图，等 Sync 拿到新地图状态后再清 HUD
            return;
        }

        if (state == null || state.kind == 0) return;
        ApplyNotices(state);

        var rect = TopRect();
        UiKit.Fill(rect, new Color(0f, 0f, 0f, 0.55f));
        var white = Style(13, Color.white, TextAnchor.UpperLeft);
        var gold = Style(13, new Color(1f, 0.85f, 0.3f), TextAnchor.UpperLeft);
        float x = rect.x + 8 * S, y = rect.y + 6 * S;

        if (state.kind == 1)
        {
            GUI.Label(new Rect(x, y, rect.width - 16, 18 * S),
                "雪球大战  " + FormatLeft(state.timeLeftMs) + "   我方(队" + state.myTeam + ") "
                + state.myPosition + " : " + state.otherPosition + " 对方", white);
            y += 20 * S;
            DrawBar(x, y, rect.width - 16 * S, 8 * S, state.myPosition / 620f, new Color(0.35f, 0.65f, 1f));
            DrawBar(x, y + 10 * S, rect.width - 16 * S, 8 * S, state.otherPosition / 620f, new Color(1f, 0.5f, 0.4f));
            y += 24 * S;
            GUI.Label(new Rect(x, y, rect.width - 16, 18 * S),
                "我方雪人 HP " + state.mySnowmanHp, state.mySnowmanHp > 500 ? white : gold);
            y += 20 * S;
            if (!state.ended && ActionButton(x, y, "推雪球", 90)) Hit(state.myTeam == 0 ? 0 : 1);
            if (!state.ended && ActionButton(x + 100 * S, y, "打雪人", 90)) Hit(state.myTeam == 0 ? 2 : 3);
        }
        else
        {
            GUI.Label(new Rect(x, y, rect.width - 16, 18 * S),
                "椰子树大战  " + FormatLeft(state.timeLeftMs)
                + "   Maple " + state.mapleScore + " : " + state.storyScore + " Story", white);
            y += 20 * S;
            GUI.Label(new Rect(x, y, rect.width - 16, 18 * S),
                "可击落 " + state.falling + "  炸弹 " + state.bombings + "  哑弹 " + state.stopped, white);
            y += 20 * S;
            if (!state.ended && ActionButton(x, y, "敲椰子", 90)) Hit(-1);
        }

        if (state.ended)
        {
            y += 24 * S;
            GUI.Label(new Rect(x, y, rect.width - 16, 18 * S), state.warpTo != 0 ? "比赛结束，正在传送…" : "比赛结束", gold);
            if (state.warpTo != 0 && !warpPending)
            {
                warpPending = true;
                warpTarget = state.warpTo;
                GameLoop.I?.field?.WarpTo(state.warpTo);
            }
        }
    }

    static void DrawBar(float x, float y, float w, float h, float t, Color c)
    {
        t = Mathf.Clamp01(t);
        UiKit.Fill(new Rect(x, y, w, h), new Color(1f, 1f, 1f, 0.25f));
        if (t > 0.01f) UiKit.Fill(new Rect(x, y, w * t, h), c);
    }

    static bool ActionButton(float x, float y, string label, float w)
    {
        var r = new Rect(x, y, w * S, 24 * S);
        bool hover = r.Contains(Pointer.Pos);
        UiKit.Fill(r, hover ? new Color(0.3f, 0.5f, 0.9f, 0.95f) : new Color(0.2f, 0.35f, 0.7f, 0.9f));
        GUI.Label(r, label, Style(13, Color.white, TextAnchor.MiddleCenter));
        bool clicked = hover && Event.current.type == EventType.Repaint && Pointer.Clicked;
        if (clicked) Pointer.ConsumeClick();
        return clicked;
    }

    static Rect TopRect() => new Rect((UiKit.W - 300 * S) / 2f, 44 * S, 300 * S, 108 * S);

    static GUIStyle Style(int size, Color color, TextAnchor anchor)
    {
        var st = new GUIStyle
        {
            font = UiKit.Font,
            fontSize = Mathf.Max(8, Mathf.RoundToInt(size * S)),
            alignment = anchor,
        };
        st.normal.textColor = color;
        return st;
    }

    static string FormatLeft(long ms)
    {
        int s = Mathf.Max(0, Mathf.CeilToInt(ms / 1000f));
        return (s / 60) + ":" + (s % 60).ToString("D2");
    }

    static void Sync()
    {
        if (GameSession.Character == null) return;
        var res = GameSession.Net.Arena(new ArenaRequest { characterId = GameSession.CharacterId, operation = ArenaOperation.Sync });
        if (res == null || !res.ok) return;
        if (res.kind == 0) { if (state != null) Reset(); return; }
        if (state == null || state.kind != res.kind) BottomBar.AddChat(res.kind == 1 ? "雪球大战开始！" : "椰子树大战开始！", BottomBar.ChatYellow);
        state = res;
        ApplyNotices(res);
        if (res.warpTo != 0 && !warpPending && Profile.LastMapId == (res.kind == 1 ? SnowballMap : CoconutMap))
        {
            // 结算后延迟到下一个 Sync（结算窗口 10/12 秒）再换图由上面 ended 分支处理
        }
    }

    static void Hit(int param)
    {
        if (GameSession.Character == null) return;
        var res = GameSession.Net.Arena(new ArenaRequest { characterId = GameSession.CharacterId, operation = ArenaOperation.Hit, param = param });
        if (res == null) return;
        if (!res.ok && !string.IsNullOrEmpty(res.error)) BottomBar.AddChat(res.error, BottomBar.ChatPink);
        else { state = res; ApplyNotices(res); }
    }

    static void ApplyNotices(ArenaResponse res)
    {
        if (string.IsNullOrEmpty(res.notice)) return;
        foreach (var line in res.notice.Split('\n'))
            if (!string.IsNullOrEmpty(line)) BottomBar.AddChat(line, BottomBar.ChatYellow);
    }
}
