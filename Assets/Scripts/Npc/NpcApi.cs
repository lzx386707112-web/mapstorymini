using System;
using System.Collections.Generic;
using MapleDash.Js;
using UnityEngine;

// cm / qm 的 C# 实现。方法名与源端 NPCConversationManager / QuestActionManager 对齐；
// 没有对应系统的调用（家族、远征队、事件副本）返回 null/0，让脚本按原端分支自然收尾。
public static class NpcApi
{
    static JsValue U => JsUndefined.Value;
    static JsNumber N(double v) => new JsNumber(v);
    static JsString S(string v) => new JsString(v ?? "");
    static JsBoolean B(bool v) => JsBoolean.Of(v);

    static JsFunction Fn(Action<JsCall> body)
        => JsFunction.FromNative("", c => { body(c); return JsUndefined.Value; });
    static JsFunction Ret(Func<JsCall, JsValue> body) => JsFunction.FromNative("", body);

    static int Int(JsCall c, int i, int def = 0) => i < c.ArgCount ? JsConvert.ToInt32(c.Arg(i)) : def;
    static string Str(JsCall c, int i, string def = "") => i < c.ArgCount ? JsConvert.ToString(c.Arg(i)) : def;

    static PlayerCtrl Pc => GameLoop.I != null ? GameLoop.I.player : null;
    static int MapId => Profile.LastMapId;

    // 单机里没有对应实现的源端 API（组队/远征队/公会/仓库/宠物/拍卖/活动…）。
    // 统一兜成"返回 null 的函数"：脚本拿到 null 后会走进源端「你没有队伍」「没有可复活宠物」
    // 这类分支，而不是在第一步就抛 "xxx is not a function" 把整段对话打断。
    // 每个名字只记一次日志，方便回查到底缺哪块系统。
    static readonly HashSet<string> missingApi = new HashSet<string>();

    static void Lenient(JsHostObject host, string owner)
    {
        host.Missing = name =>
        {
            if (missingApi.Add(owner + "." + name))
                Debug.Log("[Script] " + owner + "." + name + " 未实现（单机无对应系统），按 null 处理");
            return JsFunction.FromNative(name, c => JsNull.Value);
        };
    }

    public static JsHostObject Build(NpcSession session, int npcId, int questId, bool isQuest)
    {
        var cm = new JsHostObject(session);
        Lenient(cm, "cm");
        var player = BuildPlayer(session);
        var map = BuildMap();

        cm.Set("getPlayer", Ret(_ => player));
        cm.Set("getChar", Ret(_ => player));
        cm.Set("getNpc", Ret(_ => N(npcId)));
        cm.Set("getNpcObjectId", Ret(_ => N(npcId)));
        cm.Set("getMap", Ret(_ => map));
        cm.Set("getMapId", Ret(_ => N(MapId)));
        cm.Set("getClient", Ret(_ => BuildClient(player)));

        // —— 对话 ——
        cm.Set("sendNext", Fn(c => session.SetDialog(NpcDialogMode.Next, Str(c, 0))));
        cm.Set("sendPrev", Fn(c => session.SetDialog(NpcDialogMode.Prev, Str(c, 0))));
        cm.Set("sendNextPrev", Fn(c => session.SetDialog(NpcDialogMode.NextPrev, Str(c, 0))));
        cm.Set("sendOk", Fn(c => session.SetDialog(NpcDialogMode.Ok, Str(c, 0))));
        cm.Set("sendYesNo", Fn(c => session.SetDialog(NpcDialogMode.YesNo, Str(c, 0))));
        cm.Set("sendAcceptDecline", Fn(c => session.SetDialog(NpcDialogMode.AcceptDecline, Str(c, 0))));
        cm.Set("sendSimple", Fn(c => session.SetDialog(NpcDialogMode.Simple, Str(c, 0))));
        cm.Set("sendGetText", Fn(c =>
        {
            session.SetDialog(NpcDialogMode.GetText, Str(c, 0));
            session.Dialog.Input = "";
        }));
        cm.Set("sendGetNumber", Fn(c =>
        {
            session.SetDialog(NpcDialogMode.GetNumber, Str(c, 0));
            session.Dialog.NumberDef = Int(c, 1, 0);
            session.Dialog.NumberMin = Int(c, 2, 0);
            session.Dialog.NumberMax = Int(c, 3, 999999999);
            session.Dialog.Input = session.Dialog.NumberDef.ToString();
        }));
        cm.Set("sendStyle", Fn(c =>
        {
            var styles = c.Arg(1) as JsArray;
            var text = Str(c, 0);
            for (int i = 0; i < (styles?.Items.Count ?? 0); i++)
                text += "\r\n#L" + i + "#" + HairStyles.NameOf(HairStyles.KeyOf(i)) + "#l";
            session.SetDialog(NpcDialogMode.Simple, text);
        }));
        cm.Set("sendDefault", Fn(c => session.SetDialog(NpcDialogMode.Ok, "")));
        cm.Set("sendImage", Fn(c => session.SetDialog(NpcDialogMode.Ok, Str(c, 0))));
        cm.Set("sendNoExit", Fn(c => session.SetDialog(NpcDialogMode.Ok, Str(c, 0))));
        cm.Set("sendDirectionInfo", Fn(c => { }));
        cm.Set("sendDimensionalMirror", Fn(c => { }));
        cm.Set("dispose", Fn(c => session.DisposeFromScript()));

        // —— 文本输入 ——
        cm.Set("getText", Ret(_ => S(session.Dialog.Input)));
        cm.Set("setGetText", Fn(c => session.Dialog.Input = Str(c, 0)));

        // —— 金币 / 经验 / 等级 ——
        cm.Set("getMeso", Ret(_ => N(Profile.gold)));
        cm.Set("gainMeso", Fn(c => Profile.AddGold(Int(c, 0))));
        cm.Set("gainExp", Fn(c => Profile.GrantExperience(Int(c, 0))));
        cm.Set("getLevel", Ret(_ => N(Profile.level)));
        cm.Set("getJobId", Ret(_ => N(QuestLogic.PlayerJob)));
        cm.Set("getJob", Ret(_ => BuildJob(QuestLogic.PlayerJob)));
        cm.Set("getGender", Ret(_ => N(0)));
        cm.Set("getName", Ret(_ => S(Profile.playerName)));
        cm.Set("getFame", Ret(_ => N(0)));
        cm.Set("gainFame", Fn(c => { }));

        // —— 道具 ——
        cm.Set("haveItem", Ret(c => B(Profile.ItemCount(Int(c, 0)) >= Math.Max(1, Int(c, 1, 1)))));
        cm.Set("hasItem", Ret(c => B(Profile.ItemCount(Int(c, 0)) >= Math.Max(1, Int(c, 1, 1)))));
        cm.Set("haveItemWithId", Ret(c => B(Profile.ItemCount(Int(c, 0)) >= Math.Max(1, Int(c, 1, 1)))));
        cm.Set("itemQuantity", Ret(c => N(Profile.ItemCount(Int(c, 0)))));
        cm.Set("getItemQuantity", Ret(c => N(Profile.ItemCount(Int(c, 0)))));
        cm.Set("gainItem", Fn(c =>
        {
            int id = Int(c, 0), count = Int(c, 1, 1);
            if (id <= 0 || count == 0) return;
            if (count > 0) Profile.AddItem(id, count);
            else Profile.RemoveItem(id, -count);
        }));
        cm.Set("canHold", Ret(_ => B(true)));
        cm.Set("canHoldAll", Ret(_ => B(true)));
        cm.Set("canGenerateItem", Ret(_ => B(true)));
        cm.Set("getInventory", Ret(c => BuildInventory(Int(c, 0))));
        cm.Set("removeAll", Fn(c => { }));

        // —— 传送 ——
        cm.Set("warp", Fn(c => session.RequestWarp(Int(c, 0))));
        cm.Set("warpParty", Fn(c => session.RequestWarp(Int(c, 0))));
        cm.Set("getWarpMap", Ret(_ => N(0)));

        // —— 任务状态 ——
        cm.Set("isQuestStarted", Ret(c => B(Profile.IsQuestStarted(Int(c, 0)))));
        cm.Set("isQuestCompleted", Ret(c => B(Profile.IsQuestCompleted(Int(c, 0)))));
        cm.Set("isQuestActive", Ret(c => B(Profile.IsQuestStarted(Int(c, 0)))));
        cm.Set("getQuestStatus", Ret(c => N(Profile.QuestStatus(Int(c, 0)))));
        cm.Set("getQuestProgressInt", Ret(c => N(Profile.QuestProgressInt(Int(c, 0), Key(c, 1), Int(c, 2, 0)))));
        cm.Set("getQuestProgress", Ret(c => S(Profile.QuestProgress(Int(c, 0), Key(c, 1), ""))));
        cm.Set("setQuestProgress", Fn(c => Profile.SetQuestProgress(Int(c, 0), Key(c, 1), Str(c, 2))));
        cm.Set("startQuest", Fn(c => StartQuest(Int(c, 0), npcId)));
        cm.Set("completeQuest", Fn(c => CompleteQuest(Int(c, 0), npcId)));
        cm.Set("forceStartQuest", Fn(c => StartQuest(Int(c, 0, questId), npcId)));
        cm.Set("forceCompleteQuest", Fn(c => CompleteQuest(Int(c, 0, questId), npcId)));

        // —— 技能 / 职业 / 外观 ——
        cm.Set("teachSkill", Fn(c =>
        {
            int id = Int(c, 0), level = Int(c, 1, 1);
            if (id > 0 && level > 0) SkillProgress.SetLevel(id, level);
        }));
        // 转职走真实 job id：源端脚本 1022000.js 等是 changeJobById(100) 这种硬编码 id，
        // 旧实现把它映射回 6 个玩具职业字符串，四转（112/212…）会被压成「战士」。
        cm.Set("changeJobById", Fn(c => ChangeJob(Int(c, 0))));
        cm.Set("getJobName", Ret(c => S(SkillBook.JobName(Int(c, 0)))));
        cm.Set("setHair", Fn(c => Profile.SetHair(HairStyles.KeyOf(Math.Abs(Int(c, 0)) % HairStyles.Count))));
        cm.Set("setFace", Fn(c => { }));
        cm.Set("setSkin", Fn(c => { }));
        // 一转后脚本紧跟 resetStats()：源端 Character.resetStats 按「当前职业」重排四维
        // 并把新手期自动配的点数退回 AP，同时按等级补算 SP。
        cm.Set("resetStats", Fn(c => JobSystem.ResetStats(Profile.JobId)));

        // —— 提示 / 其它 ——
        cm.Set("dropMessage", Fn(c =>
        {
            string text = c.ArgCount > 1 ? Str(c, 1) : Str(c, 0);
            if (!string.IsNullOrEmpty(text)) BottomBar.Toast(text);
        }));
        cm.Set("message", Fn(c => ToastIfAny(Str(c, 0))));
        cm.Set("playerMessage", Fn(c => ToastIfAny(c.ArgCount > 1 ? Str(c, 1) : Str(c, 0))));
        cm.Set("mapMessage", Fn(c => ToastIfAny(Str(c, 0))));
        cm.Set("topMessage", Fn(c => ToastIfAny(Str(c, 0))));
        cm.Set("numberWithCommas", Ret(c => S(Int(c, 0).ToString("N0"))));
        cm.Set("showInfo", Fn(c => { }));
        cm.Set("showIntro", Fn(c => { }));
        cm.Set("showVideo", Fn(c => { }));
        cm.Set("playSound", Fn(c => { }));
        cm.Set("guideHint", Fn(c => { }));
        cm.Set("showQuestCompleteEffect", Fn(c => { }));
        cm.Set("showItemGain", Fn(c => { }));
        cm.Set("openNpc", Fn(c => session.RequestOpenNpc(Int(c, 0), c.ArgCount > 1 ? Str(c, 1, null) : null)));
        cm.Set("openShopNPC", Fn(c => BottomBar.Toast("商店系统尚未接入")));
        cm.Set("getPlayerCount", Ret(_ => N(1)));
        cm.Set("isLeader", Ret(_ => B(true)));
        cm.Set("isEventLeader", Ret(_ => B(true)));
        cm.Set("isUsingOldPqNpcStyle", Ret(_ => B(false)));
        cm.Set("getHourOfDay", Ret(_ => N(DateTime.Now.Hour)));
        cm.Set("canSpawnPlayerNpc", Ret(_ => B(false)));
        // 源端 AbstractPlayerInteraction.java:1350/1376。use_auto_assign_starters_ap 默认 true，
        // 所以 canGetFirstJob 恒真；文案（"力量 35"）是 NPC 对白里直接拼的，不能返回空串，
        // 否则 1022000.js 会显示「你至少得10级，并且」这种断句。
        cm.Set("canGetFirstJob", Ret(c => B(JobSystem.CanGetFirstJob(Int(c, 0)))));
        cm.Set("getFirstJobStatRequirement", Ret(c => S(JobSystem.FirstJobStatRequirement(Int(c, 0)))));

        // 单机没有的系统：返回 null，脚本里的 `if (x != null)` 分支会自然跳过。
        cm.Set("getEventManager", Ret(_ => JsNull.Value));
        cm.Set("getEventInstance", Ret(_ => JsNull.Value));
        cm.Set("getParty", Ret(_ => JsNull.Value));
        cm.Set("getExpedition", Ret(_ => JsNull.Value));
        cm.Set("getFamilyEntry", Ret(_ => JsNull.Value));
        cm.Set("getEvent", Ret(_ => JsNull.Value));
        cm.Set("getStorage", Ret(_ => JsNull.Value));
        cm.Set("getMTS", Ret(_ => JsNull.Value));
        cm.Set("getCashShop", Ret(_ => JsNull.Value));

        // 少数脚本用 cm.c.getPlayer()（Client 链）
        cm.Set("c", BuildClient(player));

        if (isQuest)
        {
            cm.Set("getQuest", Ret(_ => N(questId)));
            cm.Set("isStart", Ret(_ => B(!session.QuestEnd)));
            cm.Set("getMedalName", Ret(_ => S("")));
        }
        return cm;
    }

    static void ToastIfAny(string text)
    {
        if (!string.IsNullOrEmpty(text)) BottomBar.Toast(text);
    }

    static string Key(JsCall c, int i)
    {
        if (i >= c.ArgCount) return "";
        var v = c.Arg(i);
        if (v is JsString s) return s.Value;
        return JsConvert.ToString(v);
    }

    static void StartQuest(int id, int npcId)
    {
        if (id <= 0) return;
        if (!QuestCatalog.Exists(id)) { Profile.StartQuest(id); return; }
        QuestLogic.Start(QuestCatalog.Get(id), npcId, false);   // 脚本自己发奖励
    }

    static void CompleteQuest(int id, int npcId)
    {
        if (id <= 0) return;
        if (!QuestCatalog.Exists(id)) { Profile.CompleteQuest(id); return; }
        QuestLogic.Complete(QuestCatalog.Get(id), npcId, false);
    }

    /// <summary>cm.changeJobById(jobId)：源端不做校验，直接执行 Character.changeJob。</summary>
    static void ChangeJob(int jobId)
    {
        if (jobId <= 0) return;
        if (!JobSystem.ChangeJobById(jobId)) return;
        BottomBar.Toast("转职成功：" + JobDefs.NameOf(jobId));
        Debug.Log("[Script] changeJobById(" + jobId + ") → " + JobDefs.NameOf(jobId));
    }

    public static JsHostObject BuildPlayer(NpcSession session)
    {
        var p = new JsHostObject(session);
        Lenient(p, "player");
        p.Set("getHp", Ret(_ => N(Pc != null ? Pc.hp : 0)));
        p.Set("getMp", Ret(_ => N(Pc != null ? Pc.mp : 0)));
        p.Set("getMaxHp", Ret(_ => N(Pc != null ? Pc.hpMax : 0)));
        p.Set("getMaxMp", Ret(_ => N(Pc != null ? Pc.mpMax : 0)));
        p.Set("getLevel", Ret(_ => N(Profile.level)));
        p.Set("getJobId", Ret(_ => N(QuestLogic.PlayerJob)));
        p.Set("getJob", Ret(_ => BuildJob(QuestLogic.PlayerJob)));
        p.Set("getGender", Ret(_ => N(0)));
        p.Set("getName", Ret(_ => S(Profile.playerName)));
        p.Set("getId", Ret(_ => N(1)));
        p.Set("getMeso", Ret(_ => N(Profile.gold)));
        p.Set("getMapId", Ret(_ => N(MapId)));
        p.Set("getMap", Ret(_ => BuildMap()));
        p.Set("getPosition", Ret(_ => BuildPosition()));
        p.Set("getClient", Ret(_ => BuildClient(p)));
        p.Set("getAbstractPlayerInteraction", Ret(_ => NpcSession.Active != null
            ? Build(NpcSession.Active, session.NpcId, session.QuestId, session.IsQuest) : JsNull.Value));
        p.Set("haveItem", Ret(c => B(Profile.ItemCount(Int(c, 0)) >= Math.Max(1, Int(c, 1, 1)))));
        p.Set("canHold", Ret(_ => B(true)));
        p.Set("getInventory", Ret(c => BuildInventory(Int(c, 0))));
        p.Set("getQuestStatus", Ret(c => N(Profile.QuestStatus(Int(c, 0)))));
        p.Set("updateHp", Fn(c =>
        {
            if (Pc == null) return;
            Pc.hp = Mathf.Clamp(Int(c, 0), 0, Mathf.RoundToInt(Pc.hpMax));
        }));
        p.Set("updateMp", Fn(c =>
        {
            if (Pc == null) return;
            Pc.mp = Mathf.Clamp(Int(c, 0), 0, Mathf.RoundToInt(Pc.mpMax));
        }));
        p.Set("addHP", Fn(c => { if (Pc != null) Pc.hp = Mathf.Clamp(Pc.hp + Int(c, 0), 0, Pc.hpMax); }));
        p.Set("addMP", Fn(c => { if (Pc != null) Pc.mp = Mathf.Clamp(Pc.mp + Int(c, 0), 0, Pc.mpMax); }));
        p.Set("dropMessage", Fn(c => ToastIfAny(c.ArgCount > 1 ? Str(c, 1) : Str(c, 0))));
        p.Set("changeMap", Fn(c => { if (NpcSession.Active != null) NpcSession.Active.RequestWarp(Int(c, 0)); }));
        p.Set("setMap", Fn(c => { if (NpcSession.Active != null) NpcSession.Active.RequestWarp(Int(c, 0)); }));
        p.Set("isAlive", Ret(_ => B(Pc != null && Pc.hp > 0)));
        p.Set("getEvents", Ret(_ => new JsArray()));
        p.Set("getAreaInfos", Ret(_ => new JsArray()));
        p.Set("getPartnerId", Ret(_ => N(0)));
        p.Set("getFamilyEntry", Ret(_ => JsNull.Value));
        p.Set("sendPacket", Fn(c => { }));
        p.Set("cancelEffect", Fn(c => { }));
        p.Set("cancelAllBuffs", Fn(c => { }));
        p.Set("respawn", Fn(c => { }));
        p.Set("enableActions", Fn(c => { }));
        p.Set("setStance", Fn(c => { }));
        p.Set("startMapEffect", Fn(c => { }));
        p.Set("haveItemEquipped", Ret(_ => B(false)));
        p.Set("getMarriageItemId", Ret(_ => N(0)));
        return p;
    }

    static JsHostObject BuildMap()
    {
        var m = new JsHostObject(null);
        Lenient(m, "map");
        m.Set("getId", Ret(_ => N(MapId)));
        m.Set("getMapId", Ret(_ => N(MapId)));
        m.Set("getReturnMap", Ret(_ =>
        {
            var f = GameLoop.I != null ? GameLoop.I.field : null;
            return N(f != null && f.Map != null ? f.Map.returnMap : MapId);
        }));
        m.Set("getCharactersSize", Ret(_ => N(1)));
        m.Set("getCharacters", Ret(_ => new JsArray()));
        m.Set("getAllCharacters", Ret(_ => new JsArray()));
        m.Set("getPortal", Ret(c =>
        {
            var p = new JsHostObject(null);
            p.Set("getPosition", Ret(_ => BuildPosition()));
            p.Set("getMapId", Ret(_ => N(MapId)));
            p.Set("getName", Ret(_ => S(Str(c, 0))));
            return p;
        }));
        m.Set("getMobs", Ret(_ => new JsArray()));
        m.Set("getReactorById", Ret(_ => JsNull.Value));
        m.Set("getReactor", Ret(_ => JsNull.Value));
        m.Set("getMonsterById", Ret(_ => JsNull.Value));
        return m;
    }

    static JsHostObject BuildPosition()
    {
        var p = new JsHostObject(null);
        p.Set("getX", Ret(_ => N(Pc != null ? Pc.worldX * Art.PPU : 0)));
        p.Set("getY", Ret(_ => N(Pc != null ? -Pc.FeetPos.y * Art.PPU : 0)));
        return p;
    }

    static JsHostObject BuildClient(JsHostObject player)
    {
        var c = new JsHostObject(player);
        Lenient(c, "client");
        c.Set("getPlayer", Ret(_ => player));
        c.Set("getChannel", Ret(_ => N(0)));
        c.Set("getWorld", Ret(_ => N(0)));
        return c;
    }

    static JsHostObject BuildJob(int jobId)
    {
        var j = new JsHostObject(jobId);
        j.Set("getId", Ret(_ => N(jobId)));
        j.Set("getName", Ret(_ => S(SkillBook.JobName(jobId))));
        return j;
    }

    static JsHostObject BuildInventory(int type)
    {
        var inv = new JsHostObject(type);
        Lenient(inv, "inventory");
        inv.Set("countById", Ret(c => N(Profile.ItemCount(Int(c, 0)))));
        inv.Set("getNumFreeSlot", Ret(_ => N(24)));
        inv.Set("listById", Ret(c =>
        {
            var arr = new JsArray();
            int id = Int(c, 0), count = Profile.ItemCount(id);
            if (count > 0)
            {
                var item = new JsHostObject(id);
                item.Set("getItemId", Ret(_ => N(id)));
                item.Set("getQuantity", Ret(_ => N(count)));
                arr.Items.Add(item);
            }
            return arr;
        }));
        return inv;
    }
}
