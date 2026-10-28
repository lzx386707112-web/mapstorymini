using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// 自由市场（910000000）脚本逐层自检：
//   把每个 NPC 的每一层菜单都真的点一遍（#L 每个选项、是/否、上一页/下一页、数字框、输入框），
//   用真实 NpcApi + 真实服务端跑，不是 mock。
//
// 判定口径（沿用 QuestNpcChecks.RunMenu）：
//   GAP    —— 「Unmigrated server API / class not migrated」，整套系统还没接（仓库、雇佣商店…）
//   BROKEN —— 其它脚本异常，真回归
//   NOOP   —— 点下去既没有新对话、也没有关闭/传送/跳转，玩家看到的就是「点了没反应」
//
//   Unity -batchmode -quit -projectPath <proj> -executeMethod FreeMarketMenuChecks.Run -logFile /tmp/fm_menu.log
public static class FreeMarketMenuChecks
{
    const int FreeMarketMap = 910000000;
    // 910000000 的 life 里全部 type=n 的 NPC
    static readonly int[] Targets = { 9030000, 9030100, 9063168, 9071003, 9330045, 9900008, 9900009 };
    // 这两个源端就没有 .js：9250025 是电视（info.MapleTV=1），9000069 彪鲁只冒头顶气泡（info/speak）。
    // 走 NpcField.Interact 的「点击」路径验证不会再弹「没有对白脚本」。
    static readonly int[] Silent = { 9250025, 9000069 };
    const int MaxDepth = 4;
    const int MaxNodesPerNpc = 400;
    // 列表页（椅子 440 项 / 伤害皮肤 n 项）全部点一遍没有意义：
    // 只取前 MaxLinksPerPage 项 + 翻页/返回这类控制项（脚本里统一用 >= 1000000 编号）。
    const int MaxLinksPerPage = 3;

    static int okCount, brokenCount, gapCount, noopCount;
    static readonly List<string> Errors = new List<string>();
    static readonly List<string> Lines = new List<string>();
    static readonly HashSet<string> Visited = new HashSet<string>();
    static int nodeBudget;

    struct Step { public int mode, selection; public Step(int m, int s) { mode = m; selection = s; } }

    sealed class Snap
    {
        public bool opened = true;
        public bool visible;
        public bool closed;
        public bool disposed;
        public NpcDialogMode mode = NpcDialogMode.None;
        public List<int> links = new List<int>();
        public string text = "";
        public string error;
        public int redirected;
        public int warped;
    }

    public static void Run()
    {
        string path = Path.Combine(Path.GetTempPath(), "mapledash-fm-" + Guid.NewGuid() + ".json");
        int savedMap = Profile.LastMapId;
        Application.LogCallback hook = (condition, stack, type) =>
        {
            if (type == LogType.Error && condition.StartsWith("[Script] ", StringComparison.Ordinal))
                Errors.Add(condition);
        };
        try
        {
            var db = DbStore.Create(path);
            var character = new CharRecord { accountid = 1, name = "FmTest", level = 200,
                meso = 500000000, job = 112, map = FreeMarketMap, damageCap = 25000000, gm = 0 };
            int id = (int)CharactersDao.Insert(db, character);
            character.id = id;
            var server = GameServer.StartWith(db);
            var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public;
            foreach (var pair in new Dictionary<string, object> {
                ["Net"] = server.Transport, ["CharacterId"] = id, ["InGame"] = true,
                ["Character"] = new FullCharacter { record = character } })
                typeof(GameSession).GetProperty(pair.Key, flags).GetSetMethod(true).Invoke(null, new[] { pair.Value });
            Profile.Data.level = 200;
            Profile.Data.gold = 500000000;
            Profile.Data.SetJob(112);              // 冒险家四转英雄：五转女神走的是冒险家分支
            // 椅子商店/逼王戒走点券（getCashShop().getCash(1)），不给余额就只能测到「点券不足」一条路
            server.Transport.Cash(new CashRequest { characterId = id, operation = CashOperation.AdjustBalance,
                currency = 1, amount = 99999999 });
            ScriptSource.ClearCache();
            Application.logMessageReceived += hook;

            foreach (int npc in Targets)
            {
                Visited.Clear();
                nodeBudget = MaxNodesPerNpc;
                Lines.Add("──────── npc/" + npc + " " + WzNames.Npc(npc));
                Probe(npc, new List<Step>());
            }
            // 骑士团分支单独再跑一遍：五转女神的第二条路 isCygnus() 只在非冒险家时才会被调用
            Profile.Data.SetJob(11);
            Visited.Clear();
            nodeBudget = MaxNodesPerNpc;
            Lines.Add("──────── npc/9900008 五转女神（骑士团 job=11）");
            Probe(9900008, new List<Step>());

            Lines.Add("──────── 无脚本 NPC（点击路径 NpcField.Interact）");
            foreach (int npc in Silent)
            {
                NpcSession.CloseActive();
                NpcField.Interact(npc);
                bool quiet = NpcSession.Active == null;
                Lines.Add("  npc/" + npc + " " + WzNames.Npc(npc) + "  "
                          + (quiet ? "· 静默（不再弹「没有对白脚本」）" : "✗ 仍然弹出了对话/提示"));
                if (quiet) okCount++; else brokenCount++;
                NpcSession.CloseActive();
            }

            Debug.Log("[FmCheck] 节点 OK=" + okCount + " BROKEN=" + brokenCount
                      + " GAP=" + gapCount + " NOOP=" + noopCount);
            foreach (var l in Lines) Debug.Log("[FmCheck] " + l);
        }
        catch (Exception e) { Debug.LogError("[FmCheck] 自检自身异常：" + e); }
        finally
        {
            Application.logMessageReceived -= hook;
            NpcSession.CloseActive();
            GameServer.Stop();
            Profile.LastMapId = savedMap;
            if (File.Exists(path)) File.Delete(path);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }

    static void Probe(int npcId, List<Step> path)
    {
        if (path.Count > MaxDepth || nodeBudget-- <= 0) return;
        string sig = npcId + ":" + string.Join("/", path.ConvertAll(s => s.mode + "." + s.selection));
        if (!Visited.Add(sig)) return;

        var snap = Replay(npcId, path);
        string label = Label(npcId, path);

        if (!snap.opened)
        {
            Lines.Add("  " + label + "  ✗ 打不开（没有脚本 / 脚本为空）");
            brokenCount++;
            return;
        }
        if (snap.error != null)
        {
            Classify(label + "  ✗ " + ClassOf(snap.error), snap.error);
            return;
        }
        if (snap.warped > 0)
        {
            Lines.Add("  " + label + "  → 传送到 " + snap.warped);
            okCount++;
            return;
        }
        if (snap.redirected > 0)
        {
            Lines.Add("  " + label + "  → 跳转 npc/" + snap.redirected
                      + (snap.visible ? "（已打开：" + Head(snap.text) + "）" : "（跳转后无对话！）"));
            if (snap.visible) okCount++; else { noopCount++; Lines.Add("      ↳ NOOP 跳转后没有对话"); }
            return;
        }
        if (snap.closed && !snap.visible)
        {
            Lines.Add("  " + label + "  · 对话结束");
            okCount++;
            return;
        }
        if (!snap.visible)
        {
            noopCount++;
            Lines.Add("  " + label + "  ✗ NOOP 点了没有任何新对话（玩家视角：没反应）");
            return;
        }

        okCount++;
        Lines.Add("  " + label + "  [" + snap.mode + "] " + Head(snap.text));
        foreach (Step branch in BranchesOf(snap, path))
        {
            var next = new List<Step>(path) { branch };
            Probe(npcId, next);
        }
    }

    // 重放整条路径，返回最后一步执行完的状态；途中任何一步报错就停在那一步。
    static Snap Replay(int npcId, List<Step> path)
    {
        Errors.Clear();
        NpcSession.CloseActive();
        Profile.LastMapId = FreeMarketMap;
        var session = NpcSession.Talk(npcId);
        var snap = new Snap();
        if (session == null)
        {
            // 源端有些 NPC（9063168）是「dispose + openNpc」中转脚本：自己不留对话，
            // 直接把玩家交给另一个脚本。Talk 返回的原会话此时已经关掉，Active 换成了目标 NPC。
            var relayed = NpcSession.Active;
            if (relayed != null && relayed.NpcId != npcId)
            {
                snap.redirected = relayed.NpcId;
                Fill(snap, relayed);
                snap.error = FirstError();
                return snap;
            }
            snap.opened = false;
            return snap;
        }
        snap.error = FirstError();
        if (snap.error != null) return snap;
        // 会话没关但已经被换掉（openNpc 在 dispose 之后、结束之前生效）
        var swapped = NpcSession.Active;
        if (swapped != null && swapped != session)
        {
            snap.redirected = swapped.NpcId;
            session = swapped;
        }

        foreach (Step step in path)
        {
            if (session.Closed) { snap.closed = true; return snap; }
            Errors.Clear();
            if (session.Dialog.Mode == NpcDialogMode.GetText) session.Dialog.Input = "1";
            session.Reply(step.mode, step.selection);

            var active = NpcSession.Active;
            if (active != null && active != session)      // openNpc 把会话换成了另一个 NPC
            {
                snap.redirected = active.NpcId;
                session = active;
            }
            else if (active == null)                      // 会话关了：可能是 warp / dispose 收尾
            {
                session = null;
            }
            if (Profile.LastMapId != FreeMarketMap) snap.warped = Profile.LastMapId;

            string error = FirstError();
            if (error != null) { snap.error = error; return snap; }
            if (session == null)
            {
                snap.closed = true;
                return snap;
            }
        }
        Fill(snap, session);
        return snap;
    }

    static void Fill(Snap snap, NpcSession session)
    {
        snap.visible = session.Dialog.Visible;
        snap.closed = session.Closed;
        snap.disposed = session.Disposed;
        snap.mode = session.Dialog.Mode;
        snap.text = session.Dialog.RawText ?? "";
        var links = new List<int>();
        foreach (var run in session.Dialog.Content.Runs)
            if (run.Kind == DialogRunKind.Link && !links.Contains(run.Link)) links.Add(run.Link);
        snap.links = links;
    }

    static List<Step> BranchesOf(Snap snap, List<Step> path)
    {
        var steps = new List<Step>();
        switch (snap.mode)
        {
            case NpcDialogMode.Simple:
            case NpcDialogMode.AcceptDecline:
                int taken = 0;
                foreach (int link in snap.links)
                {
                    // 主菜单（第一层）必须全测；深层列表（椅子 440 项）才抽样
                    bool control = link >= 1000000;             // 上一页 / 下一页 / 返回主菜单
                    if (path.Count > 0 && !control && taken++ >= MaxLinksPerPage) continue;
                    steps.Add(new Step(1, link));
                }
                break;
            case NpcDialogMode.YesNo:
            case NpcDialogMode.NextPrev:
                steps.Add(new Step(1, -1));
                steps.Add(new Step(0, -1));
                break;
            case NpcDialogMode.Next:
            case NpcDialogMode.Ok:
            case NpcDialogMode.Prev:
                steps.Add(new Step(1, -1));
                break;
            case NpcDialogMode.GetNumber:
                steps.Add(new Step(1, 1));
                break;
            case NpcDialogMode.GetText:
                steps.Add(new Step(1, -1));
                break;
        }
        return steps;
    }

    static string FirstError() => Errors.Count > 0 ? Errors[0] : null;

    static string ClassOf(string error)
        => error.Contains("Unmigrated server API") ? "GAP 未接入的服务端接口： " + Short(error)
         : error.Contains("class not migrated") ? "GAP 未迁移的 Java 类： " + Short(error)
         : "BROKEN " + Short(error);

    static void Classify(string line, string error)
    {
        Lines.Add("  " + line);
        if (error.Contains("Unmigrated server API") || error.Contains("class not migrated")) gapCount++;
        else brokenCount++;
    }

    static string Short(string s)
    {
        s = (s ?? "").Replace("\n", " ");
        int cut = s.IndexOf(" :: ", StringComparison.Ordinal);
        if (cut >= 0) s = s.Substring(cut + 4);
        return s.Length > 160 ? s.Substring(0, 160) + "…" : s;
    }

    static string Head(string s)
    {
        s = (s ?? "").Replace("\r", " ").Replace("\n", " ").Replace("#k", "").Replace("#b", "")
             .Replace("#e", "").Replace("#n", "").Replace("#r", "").Replace("#L", "[").Replace("#l", "]");
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        s = s.Trim();
        return s.Length > 90 ? s.Substring(0, 90) + "…" : s;
    }

    static string Label(int npcId, List<Step> path)
    {
        if (path.Count == 0) return "打开";
        var sb = new System.Text.StringBuilder();
        foreach (Step s in path) sb.Append(s.mode == 1 ? "→" : "←").Append(s.selection).Append(' ');
        return sb.ToString().TrimEnd();
    }
}
