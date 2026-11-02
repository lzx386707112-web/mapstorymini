using UnityEngine;

// 怪物嘉年华 HUD —— 客户端表现层（对照 NativeArenaHud 同构）。
// 状态权威在 NativeCarnivalService（源端 MonsterCarnival.java）；每秒 Sync 拉状态，
// 击杀 CPQ 怪由 FieldDirector 调 ReportKill（getCP → gainCP）。
// 结算 win/lose → BottomBar 公告 + warpTo 传送（源端 showEffect quest/carnival/win|lose +
// playSound MobCarnival/Win|Lose 的等价呈现：本工程文字公告）。
// 召唤/守护者/debuff 菜单：本数据包地图无 monsterCarnival 配置、Skill 无 MCSkill.img（见服务端文件头注），
// 源端在此数据下同样不可用，故 HUD 不提供操作按钮，仅保留「认输离场」。
public static class NativeCarnivalHud
{
    static readonly int[] Fields = { 980000101, 980000201, 980000301, 980000401, 980000501, 980000601 };

    static CarnivalResponse state;
    static long nextSync;
    static bool warpPending;
    static int warpTarget;
    static bool announced, finishAnnounced;

    public static bool InGame => state != null && state.inCarnival;

    public static bool OnCarnivalMap(int mapId)
    {
        foreach (int f in Fields) if (f == mapId) return true;
        return false;
    }

    static float S => BottomBar.S;

    public static void Reset() { state = null; nextSync = 0; warpPending = false; warpTarget = 0; announced = false; finishAnnounced = false; }

    // FieldDirector 击杀钩子：把本次击杀的 getCP 报给服务端（gainCP）
    public static void ReportKill(int cp)
    {
        if (!InGame || cp <= 0 || GameSession.Character == null) return;
        var res = GameSession.Net.Carnival(new CarnivalRequest { characterId = GameSession.CharacterId, operation = 1, cp = cp });
        if (res != null && res.ok) state = res;
    }

    public static void Draw()
    {
        if (GameLoop.I == null || GameLoop.I.player == null) return;
        int map = Profile.LastMapId;
        if (!OnCarnivalMap(map))
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

        if (warpPending && Profile.LastMapId != warpTarget) return;   // 结算换图进行中
        if (state == null || !state.inCarnival) return;

        var rect = TopRect();
        UiKit.Fill(rect, new Color(0f, 0f, 0f, 0.55f));
        var white = Style(13, Color.white, TextAnchor.UpperLeft);
        var red = Style(13, new Color(1f, 0.5f, 0.4f), TextAnchor.UpperLeft);
        var blue = Style(13, new Color(0.45f, 0.7f, 1f), TextAnchor.UpperLeft);
        var gold = Style(13, new Color(1f, 0.85f, 0.3f), TextAnchor.UpperLeft);
        float x = rect.x + 8 * S, y = rect.y + 6 * S;

        GUI.Label(new Rect(x, y, 140 * S, 18 * S), "红队 CP " + state.redCp, red);
        GUI.Label(new Rect(x + 90 * S, y, 140 * S, 18 * S), FormatLeft(state.secondsLeft), white);
        GUI.Label(new Rect(x + 160 * S, y, 140 * S, 18 * S), "蓝队 CP " + state.blueCp, blue);
        y += 20 * S;
        GUI.Label(new Rect(x, y, rect.width - 16, 18 * S),
            "累计  " + state.redTotal + " : " + state.blueTotal + "  （判定用累计分）", white);
        y += 20 * S;
        if (state.finished || state.warpTo != 0)
        {
            GUI.Label(new Rect(x, y, rect.width - 16, 18 * S),
                state.win ? "胜利！名望分 +" + state.festivalPoints + "，正在传送…" : "战败…正在传送…", gold);
            if (state.warpTo != 0 && !warpPending)
            {
                warpPending = true;
                warpTarget = state.warpTo;
                GameLoop.I?.field?.WarpTo(state.warpTo);
            }
        }
        else if (ActionButton(x, y, "认输离场", 90))
        {
            var res = GameSession.Net.Carnival(new CarnivalRequest { characterId = GameSession.CharacterId, operation = 3 });
            if (res != null && res.ok) state = res;
        }
    }

    static void Sync()
    {
        if (GameSession.Character == null) return;
        var res = GameSession.Net.Carnival(new CarnivalRequest { characterId = GameSession.CharacterId, operation = 2 });
        if (res == null || !res.ok) return;
        if (res.inCarnival && !announced) { BottomBar.AddChat("怪物嘉年华开始！击杀怪物为红队赢取 CP！", BottomBar.ChatYellow); announced = true; }
        if (res.finished && !finishAnnounced)
        {
            finishAnnounced = true;
            BottomBar.AddChat(res.win ? "怪物嘉年华胜利！名望分 " + res.festivalPoints + "（找斯皮格尔曼兑换奖励）"
                                      : "怪物嘉年华战败…累计 CP " + res.redTotal + " : " + res.blueTotal,
                res.win ? BottomBar.ChatYellow : BottomBar.ChatPink);
        }
        state = res;
        if (res.finished && !string.IsNullOrEmpty(res.error)) BottomBar.AddChat(res.error, BottomBar.ChatPink);
    }

    static bool ActionButton(float x, float y, string label, float w)
    {
        var r = new Rect(x, y, w * S, 24 * S);
        bool hover = r.Contains(Pointer.Pos);
        UiKit.Fill(r, hover ? new Color(0.7f, 0.3f, 0.3f, 0.95f) : new Color(0.55f, 0.2f, 0.2f, 0.9f));
        GUI.Label(r, label, Style(13, Color.white, TextAnchor.MiddleCenter));
        bool clicked = hover && Event.current.type == EventType.Repaint && Pointer.Clicked;
        if (clicked) Pointer.ConsumeClick();
        return clicked;
    }

    static Rect TopRect() => new Rect((UiKit.W - 320 * S) / 2f, 44 * S, 320 * S, 96 * S);

    static GUIStyle Style(int size, Color color, TextAnchor anchor)
    {
        var st = new GUIStyle { font = UiKit.Font, fontSize = Mathf.Max(8, Mathf.RoundToInt(size * S)), alignment = anchor };
        st.normal.textColor = color;
        return st;
    }

    static string FormatLeft(int seconds)
    {
        int s = Mathf.Max(0, seconds);
        return (s / 60) + ":" + (s % 60).ToString("D2");
    }
}
