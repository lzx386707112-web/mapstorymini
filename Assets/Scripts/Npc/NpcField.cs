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
        alertsRefresh = 0f;
        alertSeen.Clear();            // 换图：灯泡重新冒出来，重播一次出现动画
        alertAppearAt = float.NegativeInfinity;
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

    public static void InvalidateMarkers() { markers.Clear(); alertsRefresh = 0f; }

    public sealed class QuestChoice
    {
        public int npcId;
        public QuestDef quest;
        public bool complete;
    }

    public static List<QuestChoice> ChoicesFor(int npcId)
    {
        var choices = new List<QuestChoice>();
        foreach (int id in QuestCatalog.ForNpc(npcId))
        {
            var q = QuestCatalog.Get(id);
            if (q == null) continue;
            if (QuestLogic.CanComplete(q, npcId)) choices.Add(new QuestChoice { npcId = npcId, quest = q, complete = true });
            else if (QuestLogic.CanStart(q, npcId)) choices.Add(new QuestChoice { npcId = npcId, quest = q });
        }
        choices.Sort((a, b) => a.complete == b.complete ? a.quest.id.CompareTo(b.quest.id) : a.complete ? -1 : 1);
        return choices;
    }

    public static string DefaultTalk(int npcId) => WzAssets.FileRoot("String/Npc.img").Get(npcId.ToString())?.Text("d0", "(...)") ?? "(...)";

    static bool IsSilentNpc(int npcId)
    {
        var info = WzAssets.FileRoot("Npc/" + npcId.ToString("D7") + ".img").Get("info");
        if (info == null) return false;
        if (info.Int("MapleTV") == 1) return true;
        var speak = info.Get("speak");
        return speak != null && speak.Children.Count > 0;
    }

    public static void Interact(int npcId)
    {
        if (NpcSession.Blocking) return;
        var choices = ChoicesFor(npcId);
        var scripts = new List<string>();
        var entries = WzAssets.FileRoot("Npc/" + npcId.ToString("D7") + ".img").Get("info/script");
        if (entries != null)
            foreach (var entry in entries.Children)
            {
                string name = entry.Text("script");
                if (!string.IsNullOrEmpty(name)) scripts.Add(name);
            }
        // 这两类 NPC 源端本来就没有对白脚本，点了不该弹「没有对白脚本」：
        //   info/MapleTV = 1  → 自由市场的电视（客户端走 MapleTV 界面，不是对话）
        //   info/speak 有条目 → 只会冒头顶气泡的装饰 NPC（9000069 彪鲁：speak = [n0, n1]）
        if (choices.Count == 0 && scripts.Count == 0 && IsSilentNpc(npcId)) return;
        if (choices.Count == 0 && scripts.Count == 0) { NpcSession.Talk(npcId); return; }
        if (scripts.Count == 0 && ScriptSource.Has("npc", npcId)) scripts.Add("");
        var session = NpcSession.Choose(npcId, DefaultTalk(npcId), index =>
        {
            if (index >= 0 && index < choices.Count) OpenQuest(choices[index]);
            else if (index >= choices.Count && index < choices.Count + scripts.Count)
                NpcSession.Talk(npcId, scripts[index - choices.Count]);
        });
        FillQuestMenu(session.Dialog.Content, choices);
        if (scripts.Count > 0)
        {
            session.Dialog.Content.Selections.Add(new DialogSelection { Index = -1, HeaderArt = "list2" });
            for (int i = 0; i < scripts.Count; i++)
                session.Dialog.Content.Selections.Add(new DialogSelection { Index = choices.Count + i,
                    Text = string.IsNullOrEmpty(scripts[i]) ? "与 " + WzNames.Npc(npcId) + " 对话" : scripts[i] });
        }
    }

    static readonly List<QuestChoice> alerts = new List<QuestChoice>();
    static float alertsRefresh;
    // 角色头顶灯泡列出「当前地图上能做」的任务，点开就是这些 NPC 的任务列表。
    //
    // 口径来自源端两处（之前只按 autoStart/autoComplete 过滤，导致新建 1 级角色一进图就被
    // 12 条别处 NPC 的活动任务刷屏——富翁先生的鞭炮、佳佳的遗物大会、圣诞派对…）：
    //   QuestScriptManager.java:111  end():  !containsNPC(npc) && !isAutoComplete() 直接 dispose
    //   QuestActionHandler.java:55   isNpcNearby(): 非 autoStart/autoComplete 的任务要 NPC 在本地图
    // 反过来说：除了 autoComplete 的远程任务，接/交任务都必须 NPC 在当前地图，灯泡不该列它。
    public static IReadOnlyList<QuestChoice> QuestAlerts()
    {
        if (Time.unscaledTime < alertsRefresh) return alerts;
        alertsRefresh = Time.unscaledTime + .4f;
        alerts.Clear();
        var present = new HashSet<int>();
        if (map != null)
            foreach (var npc in map.npcs)
                if (npc.hide == 0) present.Add(npc.id);
        foreach (int id in QuestCatalog.Ids)
        {
            var q = QuestCatalog.Get(id);
            if (q == null) continue;
            if (QuestLogic.CanComplete(q, q.completeReq.npc) && (present.Contains(q.completeReq.npc) || q.autoComplete))
                alerts.Add(new QuestChoice { npcId = q.completeReq.npc, quest = q, complete = true });
            else if (QuestLogic.CanStart(q, q.startReq.npc) && present.Contains(q.startReq.npc))
                alerts.Add(new QuestChoice { npcId = q.startReq.npc, quest = q });
        }
        return alerts;
    }

    // —————————————— 头顶灯泡的出现动画 ——————————————
    // BasicEff.img/QuestAlert 有两段：Appear（13 帧 ×120ms ≈ 1.56s，灯泡刚冒出来时的一次性光效）
    // 和 Default（6 帧 ×150ms，常驻呼吸）。以前只画 Default，所以灯泡是凭空出现的。
    public const string AlertAppear = "Effect/BasicEff.img/QuestAlert/Appear";
    public const string AlertDefault = "Effect/BasicEff.img/QuestAlert/Default";

    static readonly HashSet<int> alertSeen = new HashSet<int>();
    static readonly HashSet<int> alertLive = new HashSet<int>();
    static readonly List<int> alertGone = new List<int>();
    static float alertAppearAt = float.NegativeInfinity;
    static float alertAppearLength = -1f;

    public static float AlertAppearStartedAt => alertAppearAt;

    // 出现动画时长（秒）按 WZ 的 delay 累加，不写死
    public static float AlertAppearSeconds()
    {
        if (alertAppearLength < 0f)
        {
            float total = 0f;
            foreach (var frame in WzAssets.Frames(WzAssets.Node(AlertAppear)))
                total += Mathf.Max(1, frame.Int("delay", 120)) / 1000f;
            alertAppearLength = total;
        }
        return alertAppearLength;
    }

    // 灯泡当前该画哪一段，以及从这一段起已经过了多少毫秒。
    // 有新任务冒出来（首次进图 / 交完任务解锁了后续）才把 Appear 的起点拨到当前时刻；
    // 任务被接/交掉只是变少，不重播——WZ 里也没有消失动画。
    // now 单独传进来，是为了 Editor 自检能直接推演时间轴，不用真的等 1.5 秒。
    public static bool AlertAnimAt(float now, out string path, out float elapsedMs)
    {
        path = null;
        elapsedMs = 0f;
        var list = QuestAlerts();
        if (list.Count == 0)
        {
            alertSeen.Clear();
            alertAppearAt = float.NegativeInfinity;
            return false;
        }

        // 这几个集合是静态的：本函数在 OnGUI 里每帧要跑两次（Layout + Repaint），不能每次都 new
        alertLive.Clear();
        bool fresh = false;
        foreach (var choice in list)
        {
            alertLive.Add(choice.quest.id);
            if (alertSeen.Add(choice.quest.id)) fresh = true;
        }
        alertGone.Clear();
        foreach (int id in alertSeen)
            if (!alertLive.Contains(id)) alertGone.Add(id);
        foreach (int id in alertGone) alertSeen.Remove(id);
        if (fresh || now < alertAppearAt) alertAppearAt = now;   // 后者挡住时钟回退导致的负进度

        float age = now - alertAppearAt;
        float appear = AlertAppearSeconds();
        if (age < appear) { path = AlertAppear; elapsedMs = age * 1000f; return true; }
        // 播完接着 Default，相位从 Default 的第 0 帧起算，衔接不跳帧
        path = AlertDefault;
        elapsedMs = (age - appear) * 1000f;
        return true;
    }

    public static bool AlertAnim(out string path, out float elapsedMs)
        => AlertAnimAt(Time.unscaledTime, out path, out elapsedMs);

    static void FillQuestMenu(DialogContent content, IReadOnlyList<QuestChoice> choices)
    {
        foreach (bool complete in new[] { true, false })
        {
            bool header = false;
            for (int i = 0; i < choices.Count; i++)
            {
                if (choices[i].complete != complete) continue;
                if (!header) { content.Selections.Add(new DialogSelection { Index = -1, HeaderArt = complete ? "list1" : "list0" }); header = true; }
                content.Selections.Add(new DialogSelection { Index = i, Text = choices[i].quest.name });
            }
        }
    }

    public static void ShowQuestAlerts()
    {
        if (NpcSession.Blocking) return;
        var choices = new List<QuestChoice>(QuestAlerts());
        if (choices.Count == 0) return;
        var session = NpcSession.Choose(choices[0].npcId, "冒险岛上有人要给你传话。", index =>
        {
            if (index >= 0 && index < choices.Count) OpenQuest(choices[index]);
        });
        FillQuestMenu(session.Dialog.Content, choices);
    }

    public static void OpenQuest(QuestChoice choice)
    {
        // QuestActionHandler.isNpcNearby: 1200px horizontally, 800px vertically.
        var player = GameLoop.I != null ? GameLoop.I.player : null;
        if (map != null && player != null && !choice.quest.autoStart && !choice.quest.autoComplete)
        {
            bool nearby = map.npcs.Exists(n => n.id == choice.npcId && n.hide == 0 &&
                Mathf.Abs(player.FeetPos.x - map.WorldX(n.x)) <= 1200f / Px &&
                Mathf.Abs(player.FeetPos.y - map.WorldY(n.y)) <= 800f / Px);
            if (!nearby) { BottomBar.Toast("目标 NPC 距离过远"); return; }
        }
        NpcSession.TalkQuest(choice.quest, choice.npcId, choice.complete);
    }

    public static bool TryInteractNearest()
    {
        if (NpcSession.Blocking || GameWindows.AnyVisible || KeyConfigWindow.Visible || VirtualControlsEditor.Visible) return false;
        var player = GameLoop.I != null ? GameLoop.I.player : null;
        if (player == null) return false;
        var npc = Nearest(player.FeetPos);
        if (npc == null) return false;
        Interact(npc.id);
        return true;
    }
}
