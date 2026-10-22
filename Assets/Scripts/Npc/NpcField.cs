using System.Collections.Generic;
using UnityEngine;

// 当前地图上的 NPC 交互层。地图 NPC 列表来自 life 节点，绘制仍由 NativeMapView 完成；
// 这里只负责「谁在附近 / 点到了谁 / 谁头顶有任务图标」。
public static class NpcField
{
    static FieldMap map;
    static readonly Dictionary<int, Marker> markers = new Dictionary<int, Marker>();
    static float markerRefresh;

    public struct Marker
    {
        public bool start;       // 可接
        public bool complete;    // 可交
    }

    public static void Bind(FieldMap m)
    {
        map = m;
        markers.Clear();
        markerRefresh = 0f;
    }

    public static void Clear()
    {
        map = null;
        markers.Clear();
    }

    public static FieldMap Map => map;

    static float Px => Art.PPU;

    public static FieldNpc Nearest(Vector2 feetWorld, float maxPx = 110f)
    {
        if (map == null) return null;
        FieldNpc best = null;
        float bestD = maxPx / Px;
        for (int i = 0; i < map.npcs.Count; i++)
        {
            var npc = map.npcs[i];
            if (npc.hide != 0) continue;
            float wx = map.WorldX(npc.x), wy = map.WorldY(npc.y);
            float dx = Mathf.Abs(feetWorld.x - wx);
            float dy = Mathf.Abs(feetWorld.y - wy);
            float d = Mathf.Max(dx, dy * 0.6f);
            if (d < bestD) { bestD = d; best = npc; }
        }
        return best;
    }

    // 鼠标点选：GUI 坐标 → 世界坐标 → 最近的 NPC（以脚点为基准，向上 110px 的身体范围都算）
    public static FieldNpc Pick(Vector2 guiPos)
    {
        if (map == null || Camera.main == null) return null;
        float scale = UiKit.Scale;
        var screen = new Vector3(guiPos.x * scale, Screen.height - guiPos.y * scale, 0f);
        var world = Camera.main.ScreenToWorldPoint(screen);

        FieldNpc best = null;
        float bestD = float.MaxValue;
        for (int i = 0; i < map.npcs.Count; i++)
        {
            var npc = map.npcs[i];
            if (npc.hide != 0) continue;
            float wx = map.WorldX(npc.x), wy = map.WorldY(npc.y);
            float dx = world.x - wx;
            float dy = world.y - wy;
            if (Mathf.Abs(dx) > 34f / Px) continue;
            if (dy < -12f / Px || dy > 120f / Px) continue;
            float d = Mathf.Abs(dx);
            if (d < bestD) { bestD = d; best = npc; }
        }
        return best;
    }

    // 原客户端：NPC 头顶 ! = 可接，? = 可交。优先返回「可交」，与服务端交任务优先一致。
    public static Marker MarkerOf(int npcId)
    {
        if (Time.unscaledTime >= markerRefresh)
        {
            markerRefresh = Time.unscaledTime + 0.4f;
            markers.Clear();
        }
        if (markers.TryGetValue(npcId, out var m)) return m;

        m = new Marker();
        var ids = QuestCatalog.ForNpc(npcId);
        for (int i = 0; i < ids.Count; i++)
        {
            var q = QuestCatalog.Get(ids[i]);
            if (q == null) continue;
            if (QuestLogic.CanComplete(q, npcId)) { m.complete = true; break; }
            if (m.start) continue;
            if (QuestLogic.CanStart(q, npcId)) m.start = true;
        }
        markers[npcId] = m;
        return m;
    }

    public static void InvalidateMarkers() => markers.Clear();

    // 点击 NPC：有可交任务 → 交任务脚本；有可接任务 → 接任务脚本；否则跑 NPC 对白脚本。
    public static void Interact(int npcId)
    {
        var ids = QuestCatalog.ForNpc(npcId);
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                var q = QuestCatalog.Get(ids[i]);
                if (q == null) continue;
                bool wantComplete = pass == 0;
                if (wantComplete && QuestLogic.CanComplete(q, npcId))
                {
                    NpcSession.TalkQuest(q, npcId, true);
                    return;
                }
                if (!wantComplete && QuestLogic.CanStart(q, npcId))
                {
                    NpcSession.TalkQuest(q, npcId, false);
                    return;
                }
            }
        }
        NpcSession.Talk(npcId);
    }

    public static bool TryInteractNearest()
    {
        var player = GameLoop.I != null ? GameLoop.I.player : null;
        if (player == null) return false;
        var npc = Nearest(player.FeetPos);
        if (npc == null) return false;
        Interact(npc.id);
        return true;
    }
}
