using System;
using System.Collections.Generic;
using MapleDash.Js;
using UnityEngine;

// 一次 NPC / 任务对话会话。对应源端 NPCScriptManager / QuestScriptManager：
//   - cm/qm 只在一段对话内有效
//   - dispose() 只是释放脚本管理器；已经发出的对话框仍然留在屏幕上，
//     玩家点掉最后一个按钮才真正关闭（源端 sendOk + dispose 的常见写法）
public sealed class NpcSession
{
    public static NpcSession Active { get; private set; }

    public readonly int NpcId;
    public readonly int QuestId;
    public readonly bool IsQuest;
    public readonly bool QuestEnd;

    public readonly NpcDialog Dialog = new NpcDialog();
    public bool Disposed;
    public bool Closed;
    public string LastError;

    JsEngine engine;
    JsHostObject cm;
    int pendingWarp = -1;
    int pendingNpc = -1;
    string pendingNpcScript;
    Action<int, int> localResponse;
    bool localKeepsSession;

    public static bool Blocking => Active != null && !Active.Closed && Active.Dialog.Visible;

    public static NpcSession Choose(int npcId, string text, Action<int> choose)
    {
        CloseActive();
        var session = new NpcSession(npcId, 0, false, false);
        session.localResponse = (mode, selection) => { if (mode == 1) choose(selection); };
        session.SetDialog(NpcDialogMode.Simple, text);
        session.Dialog.IsMenu = true;
        Active = session;
        return session;
    }

    NpcSession(int npcId, int questId, bool isQuest, bool questEnd)
    {
        NpcId = npcId;
        QuestId = questId;
        IsQuest = isQuest;
        QuestEnd = questEnd;
    }

    public static NpcSession Talk(int npcId) => Talk(npcId, null);

    // scriptName 对应源端 NPCScriptManager.start(c, npcId, scriptName)：
    // 有名字就按名字取（npc/gachapon.js），没有才按 id 取（npc/1002000.js）。
    public static NpcSession Talk(int npcId, string scriptName)
    {
        CloseActive();
        string source = string.IsNullOrEmpty(scriptName)
            ? ScriptSource.Load("npc", npcId)
            : ScriptSource.Load("npc", scriptName);
        if (string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(scriptName)) source = ScriptSource.Load("npc", npcId);
        if (string.IsNullOrEmpty(source))
        {
            BottomBar.Toast(WzNames.Npc(npcId) + " 没有对白脚本");
            return null;
        }
        var session = new NpcSession(npcId, 0, false, false);
        Active = session;
        session.Begin(source, string.IsNullOrEmpty(scriptName) ? "npc/" + npcId : "npc/" + scriptName);
        session.FinishInvocation();
        return session;
    }

    public static NpcSession TalkQuest(QuestDef quest, int npcId, bool end)
    {
        CloseActive();
        string reason;
        if (!(end ? QuestLogic.CanComplete(quest, npcId, out reason) : QuestLogic.CanStart(quest, npcId, out reason)))
        { BottomBar.Toast(reason); return null; }
        var session = new NpcSession(npcId, quest.id, true, end);
        string source = ScriptSource.Load("quest", quest.id);
        if (string.IsNullOrEmpty(source))
        {
            session.BeginDataQuest(quest, npcId, end);
            Active = session;
            return session;
        }
        Active = session;
        session.Begin(source, "quest/" + quest.id);
        session.FinishInvocation();
        return session;
    }

    static List<string> QuestPages(MapleDash.Wz.ImgNode node)
    {
        var pages = new List<string>();
        for (int i = 0; node != null && node.Get(i.ToString()) != null; i++) pages.Add(node.Text(i.ToString()));
        return pages;
    }

    void BeginDataQuest(QuestDef quest, int npcId, bool end)
    {
        var say = WzAssets.FileRoot("Quest/Say.img").Get(quest.id + "/" + (end ? "1" : "0"));
        var pages = QuestPages(say);
        if (pages.Count == 0) pages.Add(quest.info.Length > 0 ? quest.info[Math.Min(end ? 1 : 0, quest.info.Length - 1)] : quest.name);
        int page = 0;
        bool branch = false;
        Action show = () => SetDialog(branch ? (page == pages.Count - 1 ? NpcDialogMode.Ok : NpcDialogMode.Next) :
            page == pages.Count - 1 ? NpcDialogMode.AcceptDecline : page == 0 ? NpcDialogMode.Next : NpcDialogMode.NextPrev, pages[page]);
        localKeepsSession = true;
        localResponse = (mode, selection) =>
        {
            if (mode < 0) { Close(); return; }
            if (branch)
            {
                if (mode == 1 && ++page < pages.Count) show();
                else Close();
                return;
            }
            if (page < pages.Count - 1)
            {
                page = Math.Max(0, page + (mode == 1 ? 1 : -1));
                show();
                return;
            }
            if (mode == 1)
            {
                string reason;
                bool valid = end ? QuestLogic.CanComplete(quest, npcId, out reason) : QuestLogic.CanStart(quest, npcId, out reason);
                if (!valid) { BottomBar.Toast(reason); Close(); return; }
                if (end) QuestLogic.Complete(quest, npcId, true);
                else QuestLogic.Start(quest, npcId, true);
                NpcField.InvalidateMarkers();
            }
            pages = QuestPages(say?.Get(mode == 1 ? "yes" : "no"));
            page = 0;
            branch = true;
            if (pages.Count > 0) show();
            else Close();
        };
        show();
    }

    void Begin(string source, string name)
    {
        try
        {
            engine = new JsEngine(source, name);
            cm = NpcApi.Build(this, NpcId, QuestId, IsQuest);
            engine.Bind(IsQuest ? "qm" : "cm", cm);
            engine.Bind("pi", cm);                    // 少数脚本用 pi
            engine.Bind("player", NpcApi.BuildPlayer(this));
            engine.Run();

            if (IsQuest)
            {
                if (engine.HasFunction(QuestEnd ? "end" : "start"))
                    engine.Call(QuestEnd ? "end" : "start", new JsNumber(1), new JsNumber(0), new JsNumber(0));
                else
                    Disposed = true;
            }
            else
            {
                if (!engine.HasFunction("start")) { Disposed = true; return; }
                engine.Call("start");
            }
        }
        catch (JsRuntimeException e)
        {
            Fail(e);
        }
        catch (Exception e)
        {
            Fail(e);
        }
    }

    public void Respond(int mode, int type, int selection)
    {
        if (Closed) return;
        if (Disposed)
        {
            // 脚本已经 dispose：这次点击只是关掉残留对话
            Close();
            return;
        }
        try
        {
            if (localResponse != null)
            {
                var reply = localResponse;
                if (localKeepsSession) Dialog.Mode = NpcDialogMode.None;
                else Close();
                reply(mode, selection);
                return;
            }
            // Consume the old page. dispose() without a new send must not leave it stuck.
            Dialog.Mode = NpcDialogMode.None;
            if (IsQuest) engine.Call(QuestEnd ? "end" : "start", new JsNumber(mode), new JsNumber(type), new JsNumber(selection));
            else if (engine.HasFunction("action")) engine.Call("action", new JsNumber(mode), new JsNumber(type), new JsNumber(selection));
            else Close();
            FinishInvocation();
        }
        catch (JsRuntimeException e) { Fail(e); }
        catch (Exception e) { Fail(e); }
    }

    public void Reply(int mode, int selection = -1) => Respond(mode, Dialog.MessageType, selection);

    public void Cancel()
    {
        // Text-entry cancellation is disposed directly by NPCMoreTalkHandler.
        if (Dialog.Mode == NpcDialogMode.GetText) { Close(); return; }
        Reply(-1);
        Close();
    }

    void FinishInvocation()
    {
        if (!Closed && Disposed && !Dialog.Visible) Close();
    }

    // ————— 由 cm API 调用 —————
    public void SetDialog(NpcDialogMode mode, string text)
    {
        if (Closed) return;
        Dialog.Set(mode, text);
    }

    public void DisposeFromScript() => Disposed = true;

    public void RequestWarp(int mapId)
    {
        pendingWarp = mapId;
        CloseImmediate();
    }

    public void RequestOpenNpc(int npcId, string scriptName = null)
    {
        if (scriptName == "undefined" || scriptName == "null") scriptName = null;   // JS 里显式传 undefined
        if (string.IsNullOrEmpty(scriptName)) scriptName = null;
        pendingNpc = npcId;
        pendingNpcScript = scriptName;
        CloseImmediate();
    }

    public void Close()
    {
        if (Closed) return;
        CloseImmediate();
    }

    void CloseImmediate()
    {
        Closed = true;
        Disposed = true;
        Dialog.Hide();
        if (Active == this) Active = null;
        ApplyPending();
    }

    void ApplyPending()
    {
        int warp = pendingWarp; pendingWarp = -1;
        int npc = pendingNpc; pendingNpc = -1;
        string npcScript = pendingNpcScript; pendingNpcScript = null;

        if (warp >= 0)
        {
            var field = GameLoop.I != null ? GameLoop.I.field : null;
            if (field != null) field.WarpTo(warp);
            else Profile.LastMapId = warp;
        }
        if (npc > 0) Talk(npc, npcScript);
    }

    // 每帧由 HUD 调用：处理脚本里 warp/openNpc 造成的延迟动作
    public static void Tick()
    {
        if (Active == null) return;
        if (Active.pendingWarp >= 0 || Active.pendingNpc > 0) Active.ApplyPending();
    }

    public static void CloseActive()
    {
        if (Active != null) Active.Close();
        Active = null;
    }

    void Fail(Exception e)
    {
        LastError = e.Message;
        Debug.LogError("[Script] " + (IsQuest ? "quest/" + QuestId : "npc/" + NpcId) + " :: " + e.Message);
        BottomBar.Toast("对话脚本出错（详见日志）");
        Dialog.Hide();
        CloseImmediate();
    }
}
