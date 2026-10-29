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
    public string ScriptName { get; private set; }
    public string SpeakerName;

    int scriptSessionId;
    int scriptCharacterId;
    bool hasStart, hasAction, hasEnd;
    ITransport scriptTransport;
    NativeScriptService offlineScripts;

    ScriptResponse Dispatch(ScriptRequest request)
    {
        request.characterId = scriptCharacterId;
        request.sessionId = scriptSessionId;
        return scriptTransport != null ? scriptTransport.Script(request) : offlineScripts.Handle(request);
    }

    void CallScript(string function, params JsValue[] arguments)
        => Dispatch(new ScriptRequest { operation = ScriptOperation.Call, function = function, arguments = arguments });

    void ReleaseScript()
    {
        if (scriptSessionId == 0) return;
        Dispatch(new ScriptRequest { operation = ScriptOperation.Close });
        scriptSessionId = 0;
    }
    public readonly NativeScriptRoute Route = new NativeScriptRoute();
    JsHostObject cm;
    int pendingWarp = -1;
    string pendingWarpPortal;
    int pendingWarpPortalId = -1;
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

    // 截图还原等本地数据对白复用同一窗口及回复分派，不伪造 Quest.wz 任务ID。
    public static NpcSession Conversation(int npcId, string speakerName, Action<NpcSession, int> reply)
    {
        CloseActive();
        var session = new NpcSession(npcId, 0, false, false) { SpeakerName = speakerName };
        session.localKeepsSession = true;
        session.localResponse = (mode, selection) => reply(session, mode);
        Active = session;
        return session;
    }

    internal static NpcSession MapContext() => new NpcSession(0,0,false,false);

    NpcSession(int npcId, int questId, bool isQuest, bool questEnd)
    {
        NpcId = npcId;
        QuestId = questId;
        IsQuest = isQuest;
        QuestEnd = questEnd;
    }

    // ItemScriptManager entry: execute the original item/consume_<id>.js unchanged.
    public static NpcSession UseItem(int itemId)
    {
        if (!GameSession.InGame || Profile.ItemCount(itemId)<=0 || Blocking) return null;
        string img="Item/Consume/"+(itemId/10000).ToString("D4")+".img/"+itemId.ToString("D8");
        var spec=WzAssets.Node(img+"/spec");
        string script=spec?.Text("script");
        if (string.IsNullOrEmpty(script)) { UseRestorative(itemId); return null; }
        string key="item/"+script;
        string source=ScriptSource.LoadKey(key);
        if (string.IsNullOrEmpty(source)) return null;
        CloseActive();
        var session=new NpcSession(spec.Int("npc"),0,false,false);
        Active=session; session.Begin(source,key); session.FinishInvocation(); return session;
    }

    // UseItemHandler -> StatEffect.calcHP/MPChange: ordinary restoration is not an item script.
    public static bool UseRestorative(int itemId)
    {
        var player = GameLoop.I?.player;
        if (itemId / 1000000 != 2 || player == null || player.hp <= 0 || Blocking || Profile.ItemCount(itemId) <= 0) return false;
        var spec = NativeItemSource.Node(itemId)?.Get("spec");
        if (spec == null || !string.IsNullOrEmpty(spec.Text("script")) || spec.Int("time") != 0) return false;
        // Do not partially apply buff/special items whose other effects are not handled here.
        foreach (var field in spec.Children)
            if (field.Name != "hp" && field.Name != "mp" && field.Name != "hpR" && field.Name != "mpR") return false;
        int hp = (short)spec.Int("hp"), mp = (short)spec.Int("mp");
        int hpRate = spec.Int("hpR"), mpRate = spec.Int("mpR");
        if (hp == 0 && mp == 0 && hpRate == 0 && mpRate == 0) return false;
        int job = Profile.JobId;
        int alchemist = job / 10 == 41 ? 4110000 : job / 10 == 141 ? 14110003 : 0;
        int level = alchemist == 0 ? 0 : SkillProgress.LevelOf(alchemist);
        if (level > 0)
        {
            var effect = WzAssets.Node("Skill/" + (alchemist / 10000).ToString("D3") + ".img/skill/" + alchemist + "/level/" + level);
            if (effect != null) { hp = (int)(hp * (effect.Int("x") / 100.0)); mp = (int)(mp * (effect.Int("x") / 100.0)); }
        }
        int percentHp = (int)(player.hpMax * (hpRate / 100.0));
        if (player.GetComponent<NativeMobDebuffs>()?.Has(133) == true) { hp /= 2; percentHp /= 2; }
        // Source removes one even when already at full HP/MP; never apply an effect if removal fails.
        if (!Profile.RemoveItem(itemId, 1)) return false;
        player.hp = Mathf.Clamp(player.hp + hp + percentHp, 0, player.hpMax);
        player.mp = Mathf.Clamp(player.mp + mp + (int)(player.mpMax * (mpRate / 100.0)), 0, player.mpMax);
        GameSession.SaveToDb();
        return true;
    }

    public static NpcSession Talk(int npcId) => Talk(npcId, null);

    // scriptName 对应源端 NPCScriptManager.start(c, npcId, scriptName)：
    // 有名字就按名字取（npc/gachapon.js），没有才按 id 取（npc/1002000.js）。
    public static NpcSession Talk(int npcId, string scriptName)
    {
        CloseActive();
        string scriptKey;
        string source = ScriptSource.LoadNpc(npcId, scriptName, out scriptKey);
        if (string.IsNullOrEmpty(source))
        {
            BottomBar.Toast(WzNames.Npc(npcId) + " 没有对白脚本");
            return null;
        }
        var session = new NpcSession(npcId, 0, false, false);
        Active = session;
        session.Begin(source, scriptKey);
        session.FinishInvocation();
        return session;
    }

    // PortalScriptManager.executePortalScript：跑 portal/<script>.js 的 enter(pi)。
    // 脚本不存在或没有 enter 函数时返回 false，FieldDirector 回退到 WZ 的 tm/tn 直传
    // （对应源端 GenericPortal.enterPortal 的 else 分支）。
    public static bool RunPortal(FieldPortal portal)
    {
        if (portal == null || string.IsNullOrEmpty(portal.script)) return false;
        string source = ScriptSource.Load("portal", portal.script);
        if (string.IsNullOrEmpty(source) || !source.Contains("function enter")) return false;
        CloseActive();
        var session = new NpcSession(0, 0, false, false);
        Active = session;
        session.Begin(source, "portal/" + portal.script, "enter");
        session.FinishInvocation();
        return true;
    }

    public static NpcSession TalkQuest(QuestDef quest, int npcId, bool end)
    {
        CloseActive();
        string reason = null;
        if (!(end ? QuestLogic.InProgressAt(quest, npcId) || QuestLogic.CanComplete(quest, npcId, out reason) : QuestLogic.CanStart(quest, npcId, out reason)))
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
        // 交付对话开始前先按原端 restoreLostItem 补一次「接取时自己发过的任务道具」，
        // 否则道具丢失后 Check/1 永远不满足，任务卡死再也交不了。
        if (end) QuestLogic.RestoreLostItems(quest);
        bool branch = end && !QuestLogic.CanComplete(quest, npcId);
        if (branch)
        {
            var stopPages = QuestPages(say?.Get("stop/" + QuestLogic.IncompleteStop(quest)));
            // Some IMG quests omit stop text; retain their original progress description.
            pages = stopPages.Count > 0 ? stopPages : new List<string> { quest.info.Length > 1 ? quest.info[1] : quest.name };
        }
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
                // 任务链：原端 NextQuestAction 发 updateQuestFinish(questId, npc, nextQuest)，
                // 客户端当场把后继任务的接取对话接在同一个 NPC 上（不是静默改状态）。
                // 所以这里关掉当前对话、直接开下一条的接取，Act/0 的道具/经验在接取时才发放。
                var next = end ? QuestLogic.ChainedNext(quest.completeAct, npcId) : null;
                if (next != null)
                {
                    Close();
                    TalkQuest(next, npcId, false);
                    return;
                }
            }
            pages = QuestPages(say?.Get(mode == 1 ? "yes" : "no"));
            page = 0;
            branch = true;
            if (pages.Count > 0) show();
            else Close();
        };
        show();
    }

    void Begin(string source, string name) => Begin(source, name, "start");

    // entry：NPC/任务脚本是 start()，portal 脚本是 enter(pi)（pi 传 cm 本体，与全局 pi 绑定同一个对象）
    void Begin(string source, string name, string entry)
    {
        try
        {
            int slash = name.IndexOf('/');
            ScriptName = slash < 0 ? name : name.Substring(slash + 1);
            scriptTransport = GameSession.Net;
            scriptCharacterId = GameSession.CharacterId;
            if (scriptTransport == null) offlineScripts = new NativeScriptService(); // 编辑器离线检查
            cm = NpcApi.Build(this, NpcId, QuestId, IsQuest);
            var bindings = new Dictionary<string, JsValue>
            {
                [IsQuest ? "qm" : "cm"] = cm, ["pi"] = cm, ["im"] = cm,
                ["player"] = NpcApi.BuildPlayer(this), ["Java"] = NpcApi.BuildJava()
            };
            var opened = Dispatch(new ScriptRequest { operation = ScriptOperation.Open, name = name, source = source, bindings = bindings });
            scriptSessionId = opened.sessionId;
            hasStart = opened.hasStart; hasAction = opened.hasAction; hasEnd = opened.hasEnd;

            if (IsQuest)
            {
                if ((QuestEnd ? hasEnd : hasStart))
                    CallScript(QuestEnd ? "end" : "start", new JsNumber(1), new JsNumber(0), new JsNumber(0));
                else
                    Disposed = true;
            }
            else if (entry != "start")
            {
                CallScript(entry, cm);
            }
            else
            {
                if (!hasStart) { Disposed = true; return; }
                CallScript("start");
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
            if (Route.type != NativeScriptRouteType.None)
            {
                if (!Route.Resolve(mode, selection, Dialog.Input, out var function, out var arguments)) { Close(); return; }
                CallScript(function, arguments);
            }
            else if (IsQuest) CallScript(QuestEnd ? "end" : "start", new JsNumber(mode), new JsNumber(type), new JsNumber(selection));
            else if (hasAction) CallScript("action", new JsNumber(mode), new JsNumber(type), new JsNumber(selection));
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
        if (Disposed) ReleaseScript();
        if (!Closed && Disposed && !Dialog.Visible) Close();
    }

    // ————— 由 cm API 调用 —————
    public void SetDialog(NpcDialogMode mode, string text)
    {
        if (Closed) return;
        Dialog.Set(mode, text);
    }

    public void DisposeFromScript() => Disposed = true;

    // cm.warp(mapId) / warp(mapId, 名字) / warp(mapId, portalId)：portalId 是 WZ portal 列表下标
    //（= 源端 Portal.getId），进图后由 FieldDirector 解析成落点。
    public void RequestWarp(int mapId, string portalName = null, int portalId = -1)
    {
        pendingWarp = mapId;
        pendingWarpPortal = portalName;
        pendingWarpPortalId = portalId;
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
        ReleaseScript();
        Closed = true;
        Disposed = true;
        Dialog.Hide();
        if (Active == this) Active = null;
        ApplyPending();
    }

    void ApplyPending()
    {
        int warp = pendingWarp; pendingWarp = -1;
        string warpPortal = pendingWarpPortal; pendingWarpPortal = null;
        int warpPortalId = pendingWarpPortalId; pendingWarpPortalId = -1;
        int npc = pendingNpc; pendingNpc = -1;
        string npcScript = pendingNpcScript; pendingNpcScript = null;

        if (warp >= 0)
        {
            var field = GameLoop.I != null ? GameLoop.I.field : null;
            if (field != null) field.WarpTo(warp, warpPortal, warpPortalId);
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
        // 点到的功能只是「客户端还没接这个服务端接口」时，把缺的东西直接报出来。
        // 原来一律显示「对话脚本出错」，玩家和看日志的人都不知道到底缺什么。
        const string apiPrefix = "Unmigrated server API: ";
        const string classPrefix = "Java interop: class not migrated: ";
        string api = null, missing = null;
        if (e.Message != null && e.Message.StartsWith(apiPrefix, StringComparison.Ordinal))
            api = e.Message.Substring(apiPrefix.Length);
        else if (e.Message != null && e.Message.StartsWith(classPrefix, StringComparison.Ordinal))
            missing = e.Message.Substring(classPrefix.Length);
        BottomBar.Toast(api != null ? "该功能尚未接入（缺少 " + api + "）"
            : missing != null ? "该功能需要未接入的服务端类 " + missing
            : "对话脚本出错（详见日志）");
        Dialog.Hide();
        CloseImmediate();
    }
}
