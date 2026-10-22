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
        if (string.IsNullOrEmpty(source))
        {
            BottomBar.Toast(WzNames.Npc(npcId) + " 没有对白脚本");
            return null;
        }
        var session = new NpcSession(npcId, 0, false, false);
        session.Begin(source, string.IsNullOrEmpty(scriptName) ? "npc/" + npcId : "npc/" + scriptName);
        Active = session;
        return session;
    }

    public static NpcSession TalkQuest(QuestDef quest, int npcId, bool end)
    {
        CloseActive();
        var session = new NpcSession(npcId, quest.id, true, end);
        string source = ScriptSource.Load("quest", quest.id);
        if (string.IsNullOrEmpty(source))
        {
            // 没有任务脚本：按 Check/Act 直接接取/完成（原端 uncoded quest 走数据动作）
            if (!end)
            {
                string reason;
                if (!QuestLogic.CanStart(quest, npcId, out reason)) { BottomBar.Toast(reason); return null; }
                QuestLogic.Start(quest, npcId, true);
                BottomBar.Toast("接取任务：" + quest.name);
            }
            else
            {
                string reason;
                if (!QuestLogic.CanComplete(quest, npcId, out reason)) { BottomBar.Toast(reason); return null; }
                QuestLogic.Complete(quest, npcId, true);
            }
            return null;
        }
        session.Begin(source, "quest/" + quest.id);
        Active = session;
        return session;
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
            if (IsQuest) engine.Call(QuestEnd ? "end" : "start", new JsNumber(mode), new JsNumber(type), new JsNumber(selection));
            else if (engine.HasFunction("action")) engine.Call("action", new JsNumber(mode), new JsNumber(type), new JsNumber(selection));
            else Close();
        }
        catch (JsRuntimeException e) { Fail(e); }
        catch (Exception e) { Fail(e); }
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

        if (warp > 0)
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
        if (Active.pendingWarp > 0 || Active.pendingNpc > 0) Active.ApplyPending();
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
