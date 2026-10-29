using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// 用户报的两个具体问题的取证脚本（只读，不改数据）：
//   1. 时装暖暖 → 时尚点装：每个部位的列表到底渲染出了什么（图标路径能否取到、名字是不是空）
//   2. 创建角色后进图（出生地图 10000）：NPC 任务标记 + 角色头顶灯泡列出哪些任务、点进去是什么
//
//   Unity -batchmode -quit -projectPath <proj> -executeMethod UiIssueChecks.Run -logFile /tmp/ui_issue.log
//   判定口径：
//     PASS  —— 与预期一致
//     FAIL  —— 回归失败（列表少东西 / 图标取不到 / 灯泡列了非本地图的任务 / 脚本报错）
public static class UiIssueChecks
{
    const string Tag = "[UiCheck] ";
    static readonly List<string> Errors = new List<string>();
    static int pass, fail;
    static void Check(bool ok, string what, string detail)
    {
        if (ok) pass++; else fail++;
        Log("  " + (ok ? "✓ " : "✗ FAIL ") + what + (detail.Length > 0 ? "  — " + detail : ""));
    }

    public static void Run()
    {
        string path = Path.Combine(Path.GetTempPath(), "mapledash-ui-" + Guid.NewGuid() + ".json");
        Application.LogCallback hook = (condition, stack, type) =>
        {
            if (type == LogType.Error && condition.StartsWith("[Script] ", StringComparison.Ordinal))
                Errors.Add(condition);
        };
        try
        {
            var db = DbStore.Create(path);
            // 点券挂在账号（accounts.nxCredit）上，没有账号的话 getCash(1) 恒为 0
            int accountId = AccountsDao.AutoRegister(db, "uitest", "pwd");
            var character = new CharRecord { accountid = accountId, name = "UiTest", level = 200,
                meso = 500000000, job = 112, map = 910000000, damageCap = 25000000, gm = 0 };
            int id = (int)CharactersDao.Insert(db, character);
            character.id = id;
            var server = GameServer.StartWith(db);
            var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public;
            foreach (var pair in new Dictionary<string, object> {
                ["Net"] = server.Transport, ["CharacterId"] = id, ["InGame"] = true,
                ["Character"] = new FullCharacter { record = character } })
                typeof(GameSession).GetProperty(pair.Key, flags).GetSetMethod(true).Invoke(null, new[] { pair.Value });
            server.Transport.Cash(new CashRequest { characterId = id, operation = CashOperation.AdjustBalance,
                currency = 1, amount = 99999999 });
            ScriptSource.ClearCache();
            Application.logMessageReceived += hook;

            Closet();
            NewbieBulb();
            NpcNamePlate();

            Debug.Log(Tag + "完成  通过=" + pass + " 失败=" + fail);
        }
        catch (Exception e) { Debug.LogError(Tag + "自检自身异常：" + e); }
        finally
        {
            Application.logMessageReceived -= hook;
            NpcSession.CloseActive();
            GameServer.Stop();
            if (File.Exists(path)) File.Delete(path);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }

    // ───────── 1. 时装暖暖 → 时尚点装 ─────────
    static void Closet()
    {
        Log("════════ 时装暖暖 → 时尚点装 ════════");
        Profile.Data.SetJob(112);
        Profile.Data.level = 200;
        var lines = new List<string>();

        if (!Goto(out var session, 9900009)) return;                    // 潮流前线主菜单
        DumpMenu("9900009 潮流前线", session);
        if (!Reply(session, 1, 0)) return;                              // #L0# 时装暖暖
        session = NpcSession.Active;
        DumpMenu("→ 时装暖暖", session);
        if (!Reply(session, 1, 0)) return;                              // #L0# 时尚点装
        session = NpcSession.Active;
        DumpMenu("→ 时尚点装（主菜单）", session);

        // 8 个部位逐一点开，看列表里到底有没有东西
        string[] parts = { "武器", "上衣", "裤子", "套服", "手套", "鞋子", "饰品", "帽子" };
        for (int p = 0; p < parts.Length; p++)
        {
            var s = ReplayTo(9900009, new[] { 0, 0, p });          // 潮流前线→时装暖暖→时尚点装→部位
            if (s == null) { Check(false, "部位 " + parts[p] + " 打得开", "打开失败"); continue; }
            var menu = DumpMenu("→ 部位 " + p + " " + parts[p], s, sampleRows: 3);
            Check(menu.rows > 20, "部位 " + parts[p] + " 列出了点装",
                menu.rows + " 行 / 图标OK " + menu.iconOk + " 缺失 " + menu.iconMissing);
            Check(menu.iconMissing == 0, "部位 " + parts[p] + " 图标全部取得到",
                "缺失 " + menu.iconMissing + " 个");

            // 再走两步：选第 0 件 → 确认页（#i 图标 + #t 道具名）→ 选「是」看购买结果
            var buy = ReplayTo(9900009, new[] { 0, 0, p, 0 });
            if (buy != null)
            {
                var confirm = DumpMenu("    └ 选第 0 件后的确认页", buy);
                Check(confirm.iconOk > 0 && !confirm.body.Contains("道具 ") && confirm.body.Contains("需要点卷"),
                    "部位 " + parts[p] + " 确认页有图标和道具名", confirm.body);
            }

            var beforeIds = new List<int>(Profile.Data.itemIds);
            var beforeCounts = new List<int>(Profile.Data.itemCounts);
            var done = ReplayTo(9900009, new[] { 0, 0, p, 0, 1 });
            if (done != null)
            {
                var result = DumpMenu("    └ 点「是」之后", done);
                string gained = "";
                for (int i = 0; i < Profile.Data.itemIds.Count; i++)
                {
                    int newId = Profile.Data.itemIds[i];
                    int old = beforeIds.IndexOf(newId);
                    int oldCount = old >= 0 ? beforeCounts[old] : 0;
                    if (Profile.Data.itemCounts[i] != oldCount)
                        gained += " " + newId + "(" + WzNames.Item(newId) + ") " + oldCount + "→" + Profile.Data.itemCounts[i];
                }
                Log("      到手：" + (gained.Length > 0 ? gained.Trim() : "无"));
                Check(result.body.Contains("购买成功") && gained.Length > 0,
                    "部位 " + parts[p] + " 点券够时能买下并进背包", result.body + " / 到手：" + gained.Trim());
            }
        }
    }

    // 从 9900009 起重放到指定 path，返回最后一步的会话
    static NpcSession ReplayTo(int npcId, int[] selections)
    {
        Errors.Clear();
        NpcSession.CloseActive();
        if (npcId == 0) return null;
        var session = NpcSession.Talk(npcId);
        foreach (int sel in selections)
        {
            if (session == null || session.Closed) break;
            session.Reply(1, sel);
            var active = NpcSession.Active;
            if (active != null && active != session) session = active;
            else if (active == null) session = null;
            if (Errors.Count > 0) { Log("    ✗ " + Short(Errors[0])); Errors.Clear(); return null; }
        }
        return session;
    }

    static bool Goto(out NpcSession session, int npcId)
    {
        Errors.Clear();
        NpcSession.CloseActive();
        session = NpcSession.Talk(npcId);
        if (session == null) { Log("  ✗ npc/" + npcId + " 打不开"); return false; }
        if (Errors.Count > 0) { Log("  ✗ npc/" + npcId + " " + Short(Errors[0])); return false; }
        return true;
    }

    static bool Reply(NpcSession session, int mode, int selection)
    {
        if (session == null || session.Closed) { Log("  ✗ 会话已关闭，无法继续"); return false; }
        Errors.Clear();
        session.Reply(mode, selection);
        if (Errors.Count > 0) { Log("  ✗ 点选 " + selection + " 报错：" + Short(Errors[0])); return false; }
        return true;
    }

    sealed class MenuInfo { public int rows, iconOk, iconMissing; public string body = ""; }

    // 把一层菜单的实际内容摊开：正文 + 每个选项（文字 + 图标能否取到）
    static MenuInfo DumpMenu(string title, NpcSession session, int sampleRows = 0)
    {
        var info = new MenuInfo();
        if (session == null) { Log("  " + title + "：（无会话）"); return info; }
        var d = session.Dialog;
        Log("  " + title + "  mode=" + d.Mode + " visible=" + d.Visible);

        var rows = new List<string>();
        var icons = new Dictionary<int, string>();
        int link = -1;
        foreach (var run in d.Content.Runs)
        {
            if (run.Kind == DialogRunKind.Link && run.Link >= 0) { link = run.Link; rows.Add("L" + link + "：「" + Clean(run.Text) + "」"); }
            else if (run.Kind == DialogRunKind.Icon)
            {
                bool ok = !string.IsNullOrEmpty(run.Art) && WzAssets.Node(run.Art) != null;
                if (ok) info.iconOk++; else info.iconMissing++;
                icons[link] = (icons.ContainsKey(link) ? icons[link] + " " : "") + (ok ? "图标OK" : "图标缺失✗(" + run.Art + ")");
            }
        }
        info.rows = rows.Count + d.Content.Selections.Count;
        int limit = sampleRows > 0 ? Mathf.Min(sampleRows, rows.Count) : rows.Count;
        for (int i = 0; i < limit; i++)
            Log("      " + rows[i] + (icons.ContainsKey(i) ? "   " + icons[i] : ""));
        if (limit < rows.Count) Log("      … 共 " + rows.Count + " 行");
        foreach (var sel in d.Content.Selections)
            Log("      [程序列表] " + sel.Index + " " + Clean(sel.Text ?? ""));
        // 没有可选层（是/否、确定页）：正文才是玩家看到的全部内容
        info.body = Clean(d.RawText ?? "");
        if (rows.Count > 0 || d.Content.Selections.Count > 0) return info;
        Log("      正文：" + Head(d.RawText));
        return info;
    }

    // ───────── 2. 创建角色进图后的头顶灯泡 ─────────
    static void NewbieBulb()
    {
        Log("════════ 创建角色 → 出生地图 " + CharFactory.MapMushroomTown + " ════════");
        var data = Profile.Data;
        string snapshot = JsonUtility.ToJson(data);
        var previousMap = NpcField.Map;
        try
        {
            data.level = 1;
            data.SetJob(0);
            data.questStarted.Clear();
            data.questCompleted.Clear();
            data.itemIds.Clear();
            data.itemCounts.Clear();
            FieldCatalog.Ensure();

            var map = FieldCatalog.GetMap(CharFactory.MapMushroomTown);
            NpcField.Bind(map);
            NpcField.InvalidateMarkers();
            Log("  地图 " + map.id + " " + WzNames.Map(map.id) + "  NPC " + map.npcs.Count + " 个");
            for (int i = 0; i < map.npcs.Count; i++)
            {
                var npc = map.npcs[i];
                var marker = NpcField.MarkerOf(npc.id);
                string tasks = "";
                foreach (int qid in QuestCatalog.ForNpc(npc.id))
                {
                    var q = QuestCatalog.Get(qid);
                    if (q == null) continue;
                    tasks += " q" + qid + "(" + Trim(q.name) + " start=" + QuestLogic.CanStart(q, npc.id)
                             + " complete=" + QuestLogic.CanComplete(q, npc.id) + ")";
                }
                Log("    npc/" + npc.id + " " + WzNames.Npc(npc.id)
                    + "  标记[" + (marker.start ? "!" : "") + (marker.complete ? "?" : "") + "]"
                    + tasks);
            }

            var alerts = NpcField.QuestAlerts();
            Log("  ── 角色头顶灯泡列出 " + alerts.Count + " 项 ──");
            int offMap = 0;
            bool hasStarter = false;
            foreach (var a in alerts)
            {
                if (a.welcome) { Log("    截图还原入口：" + a.Title); continue; }
                bool here = map.npcs.Exists(n => n.id == a.npcId);
                if (!a.quest.autoStart || (!a.complete && a.quest.autoAccept)) offMap++;
                if (a.quest.id == 1031) hasStarter = true;
                Log("    q" + a.quest.id + " [" + Trim(a.quest.name) + "]  npc/" + a.npcId + " "
                    + WzNames.Npc(a.npcId) + "  " + (a.complete ? "可完成" : "可开始")
                    + "  autoStart=" + a.quest.autoStart + " autoComplete=" + a.quest.autoComplete
                    + "  NPC在本地图=" + here);
            }
            Check(offMap == 0, "灯泡按原端 autoStart / autoAccept 筛选", "不符合 " + offMap + " 项");
            Check(!hasStarter, "普通 NPC 任务 1031 不混入远程灯泡", "alerts.Count=" + alerts.Count);

            // 出现动画：QuestAlert 有 Appear（一次性冒出）和 Default（常驻呼吸）两段
            var appearFrames = WzAssets.Frames(WzAssets.Node(NpcField.AlertAppear));
            float appearMs = 0f;
            foreach (var f in appearFrames) appearMs += Mathf.Max(1, f.Int("delay", 120));
            var defaultFrames = WzAssets.Frames(WzAssets.Node(NpcField.AlertDefault));
            float defaultMs = 0f;
            foreach (var f in defaultFrames) defaultMs += Mathf.Max(1, f.Int("delay", 150));
            Check(appearFrames.Count > 1, "QuestAlert/Appear 是出现动画（" + appearFrames.Count
                + " 帧 / " + appearMs + "ms），Default 是常驻呼吸（" + defaultFrames.Count + " 帧 / " + defaultMs + "ms）",
                appearFrames.Count == 0 ? "Appear 节点不存在" : "");
            Check(Mathf.Abs(NpcField.AlertAppearSeconds() * 1000f - appearMs) < 1f,
                  "出现动画时长按 WZ 的 delay 累加，不写死", "AlertAppearSeconds=" + NpcField.AlertAppearSeconds());

            float t0 = Time.unscaledTime;
            bool shown = NpcField.AlertAnimAt(t0, out string art0, out float e0);
            Check(shown && art0 == NpcField.AlertAppear && e0 < 1f,
                  "灯泡冒出来的那一刻播 Appear 的第 0 帧（原来直接跳到 Default）", art0 + " elapsed=" + e0);
            NpcField.AlertAnimAt(t0 + appearMs * .5f / 1000f, out string art1, out float e1);
            Check(art1 == NpcField.AlertAppear && e1 > appearMs * .4f,
                  "出现动画播到一半仍在 Appear", art1 + " elapsed=" + e1);
            NpcField.AlertAnimAt(t0 + appearMs / 1000f + .001f, out string art2, out float e2);
            Check(art2 == NpcField.AlertDefault && e2 < 40f,
                  "播完无缝衔接 Default 的第 0 帧，不跳随机相位", art2 + " elapsed=" + e2);
            NpcField.AlertAnimAt(t0 + 5f, out string art3, out float e3);
            // elapsed 是「进入 Default 后累计的毫秒」，循环由绘制时取模完成——验证取模后落在合法帧
            float phase = Mathf.Repeat(e3, defaultMs);
            int idx = 0;
            float acc = 0f;
            foreach (var f in defaultFrames) { acc += Mathf.Max(1, f.Int("delay", 150)); if (phase < acc) break; idx++; }
            Check(art3 == NpcField.AlertDefault && e3 > defaultMs && idx < defaultFrames.Count,
                  "之后进入 Default 循环（取模后落在第 " + idx + " 帧）",
                  art3 + " elapsed=" + e3 + " → 取模 " + phase + "ms / 周期 " + defaultMs + "ms");
            float appearAt = NpcField.AlertAppearStartedAt;
            NpcField.AlertAnimAt(t0 + 10f, out string art4, out float e4);
            Check(art4 == NpcField.AlertDefault && NpcField.AlertAppearStartedAt == appearAt,
                  "同一批任务反复取帧不会重播出现动画（只有新任务冒出来才播）",
                  "起点 " + appearAt + " 未变");
            NpcField.Bind(map);                       // 模拟换图：灯泡重新冒出来
            NpcField.AlertAnimAt(t0 + 20f, out string art5, out float e5);
            Check(art5 == NpcField.AlertAppear && NpcField.AlertAppearStartedAt > t0 + 19f,
                  "换图后重播一次出现动画", art5 + " 起点=" + NpcField.AlertAppearStartedAt);
            NpcField.InvalidateMarkers();

            NpcField.AlertAnim(out string liveArt, out float liveMs);
            Check(liveArt == NpcField.AlertAppear || liveArt == NpcField.AlertDefault,
                  "走真实时钟的 AlertAnim 也返回合法段（batchmode 下时钟可能冻结，两段都算过）",
                  liveArt + " elapsed=" + liveMs);

            // 真点一下灯泡
            Errors.Clear();
            NpcSession.CloseActive();
            NpcField.ShowQuestAlerts();
            var s = NpcSession.Active;
            if (s == null) { Log("  ⚠ 点灯泡没有出现任何对话"); return; }
            Log("  ── 点灯泡后 ──");
            DumpMenu("灯泡对话（npc/" + s.NpcId + " " + WzNames.Npc(s.NpcId) + "）", s);

            // 每一项都点进去看结果
            var snapshotBulb = JsonUtility.ToJson(data);
            for (int i = 0; i < alerts.Count; i++)
            {
                var backup = JsonUtility.ToJson(data);
                Errors.Clear();
                NpcSession.CloseActive();
                NpcField.ShowQuestAlerts();
                var cur = NpcSession.Active;
                if (cur == null) { Log("    ✗ 灯泡打不开"); break; }
                cur.Reply(1, i);
                var next = NpcSession.Active;
                string err = Errors.Count > 0 ? Short(Errors[0]) : null;
                if (err != null) Log("    → 第" + i + "项 ✗ " + err);
                else if (next == null) Log("    → 第" + i + "项：对话直接关闭（无内容）");
                else Log("    → 第" + i + "项 mode=" + next.Dialog.Mode + " 「" + Clean(Head(next.Dialog.RawText)) + "」");
                NpcSession.CloseActive();
                JsonUtility.FromJsonOverwrite(backup, data);
            }
        }
        finally
        {
            NpcSession.CloseActive();
            JsonUtility.FromJsonOverwrite(snapshot, data);
            NpcField.Bind(previousMap);
            NpcField.InvalidateMarkers();
        }
    }

    static string Clean(string s) => (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
    static string Head(string s)
    {
        s = Clean(s);
        return s.Length > 150 ? s.Substring(0, 150) + "…" : s;
    }
    static string Trim(string s) => (s ?? "").Length > 24 ? s.Substring(0, 24) + "…" : (s ?? "");
    // ───────── 3. NPC 名字牌：EXE type=1001 ─────────
    static void NpcNamePlate()
    {
        Log("════════ NPC 名字牌（EXE：文字宽+5，固定高16，无额外边框） ════════");
        Log("  当前：字号 " + NpcDialogUi.NameFontSize + " 逻辑像素，脚点下 " + NpcDialogUi.NameGapPx + "px"
            + "；UiKit.Scale=" + UiKit.Scale.ToString("0.00") + " Screen.height=" + Screen.height);
        // 字号是逻辑像素，不该再被 Screen.height 放大：GUI 已经按 UiKit.Scale 缩放过。
        Check(NpcDialogUi.NameFontSize == 12,
              "名字字号按逻辑像素（旧写法 13*Screen.height/768 在 1080p 上会变成 18）",
              "NameFontSize=" + NpcDialogUi.NameFontSize);

        // 故意传入远高于字体的 Unity 行盒，背景仍须固定16px。
        var textSize = new Vector2(40f, 24f);
        NpcDialogUi.NamePlateRects(new Vector2(100f, 200f), textSize, out var plate, out var text);
        Check(plate.width == 45f && plate.height == 16f,
              "EXE背景尺寸不受Unity行盒高度影响", "牌 " + plate.width + "x" + plate.height);
        Check(plate.x == 78f, "EXE横向原点按整数除2截断", "左边 x=" + plate.x);
        Check(Mathf.Abs(plate.y - (200f + NpcDialogUi.NameGapPx)) < .01f,
              "牌顶边贴在脚点下方 " + NpcDialogUi.NameGapPx + "px", "牌顶 y=" + plate.y.ToString("0.0"));
        Check(text.x == plate.x + 2f && text.y == plate.y && text.width == 40f,
              "EXE文字从(2,0)开始，无额外纵向内边距", "文字 " + text.x + "," + text.y);
    }

    static string Short(string s)
    {
        s = (s ?? "").Replace("\n", " ");
        int cut = s.IndexOf(" :: ", StringComparison.Ordinal);
        if (cut >= 0) s = s.Substring(cut + 4);
        return s.Length > 200 ? s.Substring(0, 200) + "…" : s;
    }
    static void Log(string s) => Debug.Log(Tag + s);
}
