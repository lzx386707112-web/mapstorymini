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

    // Character.useCS：setCS 的存值（见 BuildPlayer）
    static bool useCS;

    // 未迁移 API 必须带完整名字报错，不能把返回 null 当成业务适配成功。
    // 已实现系统显式绑定；没有队伍等合法空状态仍由具体 getter 返回 null。
    static readonly HashSet<string> missingApi = new HashSet<string>();

    static void Lenient(JsHostObject host, string owner)
    {
        host.Missing = name =>
        {
            if (missingApi.Add(owner + "." + name))
                Debug.LogError("[Script] 未迁移服务端接口：" + owner + "." + name);
            return JsFunction.FromNative(name, c => throw new JsRuntimeException("Unmigrated server API: " + owner + "." + name));
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
        cm.Set("spawnMonster", Fn(c => SpawnScriptMob(Int(c,0),Int(c,1),Int(c,2))));
        cm.Set("showSkillBackdrop", Fn(c => NativeSkillBackdrop.ShowMap(Str(c, 0), Int(c, 1))));
        cm.Set("clearSkillBackdrop", Fn(c => NativeSkillBackdrop.ShowMap(null, 0)));
        cm.Set("showLocalSkillBackdrop", Fn(c => NativeSkillBackdrop.ShowLocal(Str(c, 0), Int(c, 1))));
        cm.Set("clearLocalSkillBackdrop", Fn(c => NativeSkillBackdrop.ShowLocal(null, 0)));
        cm.Set("getDamageSkinIds", Ret(_ =>
        {
            var ids = new List<JsValue>();
            foreach (int id in NativeDamageSkinCatalog.Ids()) ids.Add(N(id));
            return new JsArray(ids);
        }));
        cm.Set("getDamageSkinName", Ret(c => S(NativeDamageSkinCatalog.Name(Int(c, 0)))));
        cm.Set("getDamageSkinId", Ret(_ => N(NativeDamageSkinCatalog.Selected)));
        cm.Set("setDamageSkin", Ret(c => B(NativeDamageSkinCatalog.Select(Int(c, 0)))));

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
        cm.Set("sendDefault", Fn(c => session.SetDialog(NpcDialogMode.Ok,
            WzAssets.FileRoot("String/Npc.img").Get(npcId.ToString())?.Text("d0", "(...)") ?? "(...)")));
        cm.Set("sendImage", Fn(c => session.SetDialog(NpcDialogMode.Ok, Str(c, 0))));
        cm.Set("sendNoExit", Fn(c => session.SetDialog(NpcDialogMode.Ok, Str(c, 0))));
        cm.Set("sendDirectionInfo", Fn(c => { }));
        cm.Set("sendDimensionalMirror", Fn(c => { }));
        cm.Set("dispose", Fn(c => session.DisposeFromScript()));

        // —— 北斗 nextLevel 回调，路由完全沿用 NPCScriptManager.nextLevel ——
        cm.Set("sendNextLevel", Fn(c => { session.SetDialog(NpcDialogMode.Next, Str(c, 1)); session.Route.Set(NativeScriptRouteType.Next, after: Str(c, 0)); }));
        cm.Set("sendLastLevel", Fn(c => { session.SetDialog(NpcDialogMode.Prev, Str(c, 1)); session.Route.Set(NativeScriptRouteType.Previous, before: Str(c, 0)); }));
        cm.Set("sendLastNextLevel", Fn(c => { session.SetDialog(NpcDialogMode.NextPrev, Str(c, 2)); session.Route.Set(NativeScriptRouteType.PreviousNext, Str(c, 0), Str(c, 1)); }));
        cm.Set("sendOkLevel", Fn(c => { session.SetDialog(NpcDialogMode.Ok, Str(c, 1)); session.Route.Set(NativeScriptRouteType.Ok, after: Str(c, 0)); }));
        cm.Set("sendSelectLevel", Fn(c => {
            bool prefix = c.ArgCount > 1 && c.Arg(1) is JsString;
            session.SetDialog(NpcDialogMode.Simple, Str(c, prefix ? 1 : 0));
            session.Route.Set(NativeScriptRouteType.Select, selectionPrefix: prefix ? Str(c, 0) : "");
        }));
        cm.Set("sendNextSelectLevel", Fn(c => { session.SetDialog(NpcDialogMode.Simple, Str(c, 1)); session.Route.Set(NativeScriptRouteType.NextSelect, after: Str(c, 0)); }));
        cm.Set("getInputTextLevel", Fn(c => { session.SetDialog(NpcDialogMode.GetText, Str(c, 1)); session.Dialog.Input = ""; session.Route.Set(NativeScriptRouteType.Text, after: Str(c, 0)); }));
        cm.Set("getInputNumberLevel", Fn(c => {
            session.SetDialog(NpcDialogMode.GetNumber, Str(c, 1));
            session.Dialog.NumberDef = Int(c, 2); session.Dialog.NumberMin = Int(c, 3); session.Dialog.NumberMax = Int(c, 4);
            session.Dialog.Input = session.Dialog.NumberDef.ToString(); session.Route.Set(NativeScriptRouteType.Number, after: Str(c, 0));
        }));
        cm.Set("sendAcceptDeclineLevel", Fn(c => { session.SetDialog(NpcDialogMode.AcceptDecline, Str(c, 2)); session.Route.Set(NativeScriptRouteType.AcceptDecline, Str(c, 0), Str(c, 1)); }));
        cm.Set("sendYesNoLevel", Fn(c => { session.SetDialog(NpcDialogMode.YesNo, Str(c, 2)); session.Route.Set(NativeScriptRouteType.YesNo, Str(c, 0), Str(c, 1)); }));

        // —— 文本输入 ——
        cm.Set("getText", Ret(_ => S(session.Dialog.Input)));
        cm.Set("setGetText", Fn(c => session.Dialog.Input = Str(c, 0)));

        // —— 金币 / 经验 / 等级 ——
        cm.Set("getMeso", Ret(_ => N(Profile.gold)));
        cm.Set("getNameplateSeries", Ret(_ => N(Math.Max(0, Math.Min(8, ReadCharacterInteger("nameplate_title_series"))))));
        cm.Set("getPqPoints", Ret(_ => N(ReadCharacterInteger("副本积分"))));
        cm.Set("getPqTotalPoints", Ret(_ => N(ReadCharacterInteger("副本总积分"))));
        cm.Set("gainMeso", Fn(c => Profile.AddGold(Int(c, 0))));
        cm.Set("gainExp", Fn(c => Profile.GrantExperience(Int(c, 0))));
        cm.Set("getLevel", Ret(_ => N(Profile.level)));
        cm.Set("getJobId", Ret(_ => N(QuestLogic.PlayerJob)));
        cm.Set("getJob", Ret(_ => BuildJob(QuestLogic.PlayerJob)));
        cm.Set("getGender", Ret(_ => N(GameSession.Character?.record.gender ?? 0)));
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
        // NPCConversationManager.isNotCanHold(type[, count])：源端在背包满时**自己**发 sendOk
        // 再 dispose，返回 true 表示脚本该 return。单机背包不限格数，与 canHold 恒真保持一致。
        cm.Set("isNotCanHold", Ret(_ => B(false)));
        // ItemInformationProvider.getName / itemDataExists 的等价判断：WZ 里有没有这件道具。
        cm.Set("itemExists", Ret(c => B(!string.IsNullOrEmpty(WzNames.Item(Int(c, 0))))));
        cm.Set("hasItemData", Ret(c => B(!string.IsNullOrEmpty(WzNames.Item(Int(c, 0))))));
        cm.Set("getInventory", Ret(c => BuildInventory(Int(c, 0))));
        cm.Set("removeAll", Fn(c => { }));
        cm.Set("removeAllByInventory", Fn(c => RemoveInventory(Int(c, 0))));
        // 单机没有「格位」概念（inventoryitems 一行一道具），拿到 slot 也无法定位，明确不装作成功。
        cm.Set("removeAllByInventorySlot", Fn(c => throw new JsRuntimeException("单机没有背包格位概念：removeAllByInventorySlot")));

        // —— 传送 ——
        // AbstractPlayerInteraction.warp(map[, portal])：第二参可以是门名（String）也可以是
        // WZ portal 下标（int，= 源端 Portal.getId）。FM 脚本 market00 用后者。
        // mapId 非法时抛错（源端 MapleMapFactory.getMap(-1) 返回 null → NPE → 脚本 try/catch 回退），
        // market00.js 的「取不到 savedLocation 就回射手村」分支靠这个触发。
        cm.Set("warp", Fn(c =>
        {
            int target = Int(c, 0);
            if (target <= 0) throw new JsRuntimeException("warp: 无效的地图 id " + target);
            session.RequestWarp(target, WarpName(c, 1), WarpIndex(c, 1));
        }));
        cm.Set("warpParty", Fn(c => session.RequestWarp(Int(c, 0), WarpName(c, 1), WarpIndex(c, 1))));
        cm.Set("getWarpMap", Ret(_ => N(0)));
        // AbstractPlayerInteraction.getMarketPortalId(mapId)（AbstractPlayerInteraction.java:126）：
        // 目标图里第一个 script 含 "market" 的门（MapleMap.findMarketPortal），没有就取随机出生门。
        cm.Set("getMarketPortalId", Ret(c => N(MarketPortalId(Int(c, 0)))));
        // 进门音效在 FieldDirector.StartWarp 里统一播（原端 playPortalSound 也是客户端表现层），这里只对齐 API 存在性。
        cm.Set("playPortalSound", Fn(c => { }));
        BindSavedLocations(cm);

        // —— ExtendUtil：源端中文任务、签到、制作、领取记录的统一持久化 ——
        cm.Set("getCharacterExtendValue", Ret(c => ExtendValue(c, false, false)));
        cm.Set("getAccountExtendValue", Ret(c => ExtendValue(c, true, false)));
        cm.Set("saveOrUpdateCharacterExtendValue", Fn(c => ExtendValue(c, false, true)));
        cm.Set("saveOrUpdateAccountExtendValue", Fn(c => ExtendValue(c, true, true)));
        cm.Set("getCurrentTime", Ret(_ => N(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())));
        // AbstractPlayerInteraction.getOnlineTime → 当日累计在线秒数（见 OnlineTime.cs）
        cm.Set("getOnlineTime", Ret(_ => N(OnlineTime.Seconds)));
        cm.Set("getScriptName", Ret(_ => S(session.ScriptName)));

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
        // 源端这几个 API 最终都是 SERVERMESSAGE(type, text)，由客户端按 type 决定落点：
        //   0 → 聊天窗浅蓝、1 → 居中弹窗、5 → 聊天窗粉色、6 → 聊天窗浅蓝。
        // 以前一律塞进 Toast，落点和颜色都不对（脚本里 143 处 type5、73 处 type6 全被吃掉）。
        cm.Set("dropMessage", Fn(c => DropNotice(c)));
        // Character.message(msg) 在源端等价于 dropMessage(5, msg)
        cm.Set("message", Fn(c => NativeSystemMessage.Drop(5, Str(c, 0))));
        cm.Set("playerMessage", Fn(c => DropNotice(c)));
        // AbstractPlayerInteraction.mapMessage(type, message) → 整图广播 serverNotice(type, message)
        cm.Set("mapMessage", Fn(c => DropNotice(c)));
        cm.Set("topMessage", Fn(c => ToastIfAny(Str(c, 0))));
        cm.Set("numberWithCommas", Ret(c => S(Int(c, 0).ToString("N0"))));
        cm.Set("showInfo", Fn(c => NativeImgAnimation.ShowInfo(Str(c, 0))));
        cm.Set("showIntro", Fn(c => { }));
        cm.Set("showVideo", Fn(c => { }));
        cm.Set("playSound", Fn(c => { }));
        cm.Set("guideHint", Fn(c => { }));
        cm.Set("showQuestCompleteEffect", Fn(c => { }));
        cm.Set("showItemGain", Fn(c => { }));
        cm.Set("openNpc", Fn(c => session.RequestOpenNpc(Int(c, 0), c.ArgCount > 1 ? Str(c, 1, null) : null)));
        cm.Set("openShopNPC", Fn(c => BottomBar.Toast("商店系统尚未接入")));
        cm.Set("getPlayerCount", Ret(_ => N(1)));
        cm.Set("isLeader", Ret(_ => B(IsPartyLeader())));
        cm.Set("isPartyLeader", Ret(_ => B(IsPartyLeader())));
        cm.Set("isEventLeader", Ret(_ => B(false)));
        cm.Set("isUsingOldPqNpcStyle", Ret(_ => B(false)));
        cm.Set("getHourOfDay", Ret(_ => N(DateTime.Now.Hour)));
        cm.Set("canSpawnPlayerNpc", Ret(_ => B(false)));
        // 源端 AbstractPlayerInteraction.java:1350/1376。use_auto_assign_starters_ap 默认 true，
        // 所以 canGetFirstJob 恒真；文案（"力量 35"）是 NPC 对白里直接拼的，不能返回空串，
        // 否则 1022000.js 会显示「你至少得10级，并且」这种断句。
        cm.Set("canGetFirstJob", Ret(c => B(JobSystem.CanGetFirstJob(Int(c, 0)))));
        cm.Set("getFirstJobStatRequirement", Ret(c => S(JobSystem.FirstJobStatRequirement(Int(c, 0)))));

        BindCommonCm(cm, session);

        // 单机没有的系统：返回 null，脚本里的 `if (x != null)` 分支会自然跳过。
        cm.Set("getEventManager", Ret(_ => JsNull.Value));
        cm.Set("getEventInstance", Ret(_ => JsNull.Value));
        cm.Set("getParty", Ret(_ => BuildParty(session)));
        cm.Set("getExpedition", Ret(_ => JsNull.Value));
        cm.Set("getFamilyEntry", Ret(_ => JsNull.Value));
        cm.Set("getEvent", Ret(_ => JsNull.Value));
        cm.Set("getStorage", Ret(_ => JsNull.Value));
        cm.Set("getMTS", Ret(_ => JsNull.Value));
        cm.Set("getCashShop", Ret(_ => GameSession.InGame ? BuildCashShop() : JsNull.Value));

        // 少数脚本用 cm.c.getPlayer()（Client 链）
        cm.Set("c", BuildClient(player));

        if (isQuest)
        {
            cm.Set("getMedalName", Ret(_ => S(WzNames.Item(QuestCatalog.Get(questId)?.medalItem ?? 0))));
            cm.Set("earnTitle", Fn(c => BottomBar.Toast(Str(c, 0))));
            cm.Set("getQuest", Ret(_ => N(questId)));
            cm.Set("isStart", Ret(_ => B(!session.QuestEnd)));
        }
        return cm;
    }

    static int ReadCharacterInteger(string key)
    {
        if (!GameSession.InGame) throw new JsRuntimeException("Script values require an active character");
        string value = GameSession.Net.ScriptValue(new ScriptValueRequest {
            characterId = GameSession.CharacterId, name = key }).value;
        return int.TryParse(value, System.Globalization.NumberStyles.AllowLeadingSign,
            System.Globalization.CultureInfo.InvariantCulture, out int number) ? number : 0;
    }

    static JsValue ExtendValue(JsCall call, bool account, bool write)
    {
        if (!GameSession.InGame) throw new JsRuntimeException("Script values require an active character");
        int index = write ? 2 : 1;
        int period = call.ArgCount > index ? (JsConvert.ToBoolean(call.Arg(index)) ? 1 : 2) : 0;
        var response = GameSession.Net.ScriptValue(new ScriptValueRequest { characterId = GameSession.CharacterId,
            account = account, write = write, period = period, name = Str(call, 0), value = write ? Str(call, 1) : null });
        return response.value == null ? (JsValue)JsNull.Value : S(response.value);
    }

    static void BindSavedLocations(JsHostObject host)
    {
        host.Set("peekSavedLocation", Ret(c => Location(ScriptOperation.PeekLocation, Str(c, 0))));
        host.Set("getSavedLocation", Ret(c => Location(ScriptOperation.GetLocation, Str(c, 0))));
        host.Set("clearSavedLocation", Fn(c => Location(ScriptOperation.ClearLocation, Str(c, 0))));
    }

    // SavedLocationType 全量，顺序与源端枚举一致（server/maps/SavedLocationType.java）。
    static readonly string[] SavedLocationTypes =
    {
        "FREE_MARKET", "WORLDTOUR", "FLORINA", "INTRO", "SUNDAY_MARKET", "MIRROR",
        "EVENT", "BOSSPQ", "HAPPYVILLE", "MONSTER_CARNIVAL", "DEVELOPER", "JAIL"
    };

    // Character.saveLocation(type)：把「当前地图 + 最近的传送门」写进这一格。
    static void SaveLocation(string type)
    {
        if (string.IsNullOrEmpty(type)) return;
        if (!GameSession.InGame) throw new JsRuntimeException("Saved locations require an active character");
        GameSession.Net.Script(new ScriptRequest
        {
            operation = ScriptOperation.SaveLocation, characterId = GameSession.CharacterId,
            location = type, mapId = MapId, portalId = ClosestPortalId()
        });
    }

    // Character.saveLocationOnWarp()：只补还空着的格子，已有记录一律不动
    // （源端循环里只判 savedLocations[i] == null，不是覆盖写）。
    static void SaveLocationOnWarp()
    {
        foreach (string type in SavedLocationTypes)
            if (JsConvert.ToInt32(Location(ScriptOperation.PeekLocation, type)) < 0) SaveLocation(type);
    }

    // MapleMap.findClosestPortal：按 distanceSq 找最近的门，返回它在 WZ portal 列表里的下标（= 源端 Portal.getId）。
    static int ClosestPortalId()
    {
        var field = GameLoop.I != null ? GameLoop.I.field : null;
        var map = field != null ? field.Map : null;
        var pc = Pc;
        if (map == null || pc == null || map.portals.Count == 0) return 0;
        float px = pc.worldX * Art.PPU, py = -pc.FeetPos.y * Art.PPU;
        int best = 0;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < map.portals.Count; i++)
        {
            var portal = map.portals[i];
            float dx = portal.x - px, dy = portal.y - py;
            float distance = dx * dx + dy * dy;
            if (distance < bestDistance) { bestDistance = distance; best = i; }
        }
        return best;
    }

    struct Totals { public int str, dex, intelligence, luk, watk, matk, weaponDefense, magicDefense; }

    // Character.reapplyLocalStats 的等价：基础属性 + 武器 + 全部防具（与能力值窗同一条路）。
    static Totals PlayerTotals()
    {
        var weapon = WeaponDefs.Get(Profile.equipWeapon);
        var combat = Profile.Attributes.WithWeapon(weapon, 0, out int watk, out int matk);
        return new Totals
        {
            str = combat.str, dex = combat.dex, intelligence = combat.intelligence, luk = combat.luk,
            watk = watk, matk = matk, weaponDefense = combat.weaponDefense, magicDefense = combat.magicDefense
        };
    }

    // NPCConversationManager.removeAllByInventory(invType)：1 装备 2 消耗 3 设置 4 其他 5 特殊。
    // 道具 id 的首位数字就是栏位（ItemConstants.getInventoryType），单机用它判定。
    static void RemoveInventory(int type)
    {
        var data = Profile.Data;
        if (type < 1 || type > 5 || data?.itemIds == null) return;
        foreach (int id in new List<int>(data.itemIds))
        {
            if (id / 1000000 != type) continue;
            int count = Profile.ItemCount(id);
            if (count > 0) Profile.RemoveItem(id, count);
        }
    }

    static JsValue Location(ScriptOperation operation, string type)
    {
        if (!GameSession.InGame) throw new JsRuntimeException("Saved locations require an active character");
        return GameSession.Net.Script(new ScriptRequest { operation = operation,
            characterId = GameSession.CharacterId, location = type, mapId = MapId, portalId = 0 }).value;
    }

    // ————————————————————————————————————————————
    // 静态脚本 API 扫描补齐（tools/api_scan_report.json）：cm/qm 宿主上还缺的绑定。
    // 每组都回源端查过消费链；整套未迁移的系统（公会/远征/CPQ/婚礼/宠物/ dojo）按
    // 源端返回类型给空态或显式提示，不再走 Lenient 抛「未迁移接口」中断对话。
    // ————————————————————————————————————————————
    static void BindCommonCm(JsHostObject cm, NpcSession session)
    {
        // —— 职业 / 成长（AbstractPlayerInteraction.changeJob → Character.changeJob）——
        cm.Set("changeJob", Fn(c => ChangeJob(Int(c, 0))));

        // —— 道具（Odin 风格的别名；单机没有格位，removeItem 只按 id+数量）——
        cm.Set("getItemCount", Ret(c => N(Profile.ItemCount(Int(c, 0)))));
        cm.Set("getSpace", Ret(_ => N(24)));   // InventoryType.getCapacity - used；单机恒有空格
        cm.Set("useItem", Ret(c =>
        {
            int id = Int(c, 0);
            if (Profile.ItemCount(id) < 1) return B(false);
            Profile.RemoveItem(id, 1);
            return B(true);
        }));
        cm.Set("removeItem", Ret(c => B(Profile.RemoveItem(Int(c, 0), Math.Max(1, Int(c, 1, 1))))));
        // Odin gainNX(nx) → nxCredit（CashShop.java:72 NX_CREDIT=1）
        cm.Set("gainNX", Fn(c => GameSession.Net.Cash(new CashRequest { characterId = GameSession.CharacterId,
            operation = CashOperation.AdjustBalance, currency = 1, amount = Int(c, 0) })));
        cm.Set("gainItemPeriod", Fn(c =>   // Odin gainItemPeriod(id, qty, period)：带时限发道具，单机按永久处理
        {
            int id = Int(c, 0), count = Int(c, 1, 1);
            if (count > 0) Profile.AddItem(id, count); else if (count < 0) Profile.RemoveItem(id, -count);
        }));

        // —— 公告 / 提示（PacketCreator.serverNotice(type, text)，与 dropMessage 同一路）——
        cm.Set("serverNotice", Fn(c => DropNotice(c)));

        // —— 地图怪物业（MapleMap 语义）——
        cm.Set("countMonster", Ret(c =>
        {
            int id = Int(c, 0), count = 0;
            foreach (var mob in EnemyUnit.All) if (mob.fieldMode && !mob.dead && mob.fieldMobId == id) count++;
            return N(count);
        }));
        cm.Set("mapMobCount", Ret(_ => N(CountFieldMonsters())));
        cm.Set("resetMapObjects", Fn(c => GameLoop.I?.field?.ClearMobs()));   // MapleMap.resetMapObjects：重刷怪（反应堆未迁移）
        cm.Set("spawnMonsterOnGroundBelowIfMissing", Fn(c =>
        {
            int id = Int(c, 0);
            foreach (var mob in EnemyUnit.All) if (mob.fieldMode && !mob.dead && mob.fieldMobId == id) return;
            SpawnScriptMob(id, Int(c, 1), Int(c, 2), true);
        }));
        cm.Set("scheduleMonsterOnGroundBelowIfMissing", Fn(c =>
        {
            int id = Int(c, 0);
            foreach (var mob in EnemyUnit.All) if (mob.fieldMode && !mob.dead && mob.fieldMobId == id) return;
            SpawnScriptMob(id, Int(c, 1), Int(c, 2), true);
        }));
        // 反应堆整系统未迁移：触发类 no-op、查询类空态（与 map.getReactor 返回 null 一致）
        cm.Set("hitReactor", Fn(c => { }));
        cm.Set("forceHitReactor", Fn(c => { }));
        cm.Set("forceStartReactor", Fn(c => { }));
        cm.Set("isAllReactorState", Ret(_ => B(false)));
        cm.Set("getReactorByName", Ret(_ => JsNull.Value));

        // —— 传送门屏蔽（Character.blockPortal(scriptName)，换图清空见 FieldDirector.Enter）——
        cm.Set("blockPortal", Fn(c =>
        {
            string name = Str(c, 0);
            if (!string.IsNullOrEmpty(name)) FieldDirector.BlockedPortalScripts.Add(name);
        }));
        cm.Set("unblockPortal", Fn(c => FieldDirector.BlockedPortalScripts.Remove(Str(c, 0))));

        // —— 地区信息（Character.updateAreaInfo：按 area 存一段脚本串，任务用 contains 判步）——
        cm.Set("updateAreaInfo", Fn(c => ScriptValueStore("areaInfo_" + Int(c, 0), Str(c, 1), write: true)));
        cm.Set("containsAreaInfo", Ret(c =>
        {
            string stored = ScriptValueStore("areaInfo_" + Int(c, 0), null);
            string needle = Str(c, 1);
            return B(!string.IsNullOrEmpty(stored) && !string.IsNullOrEmpty(needle) && stored.Contains(needle));
        }));

        // —— 副本计数（NPCConversationManager.java:KEY_JIFEN + getPQEnteredName）——
        cm.Set("addPqPoints", Fn(c =>
        {
            int num = c.ArgCount > 0 ? Int(c, 0) : 1;
            SavePqExtend("副本积分", ReadPqExtend("副本积分") + num);
            SavePqExtend("副本总积分", ReadPqExtend("副本总积分") + num);
        }));
        cm.Set("getPQEnteredCount", Ret(c => N(c.Arg(0) is JsString
            ? ReadPqExtend(Str(c, 0))
            : ReadPqExtend(PqEnteredName(Int(c, 0))))));
        cm.Set("recordEntryPQ", Fn(c =>
        {
            string key = c.Arg(0) is JsString ? Str(c, 0) : PqEnteredName(Int(c, 0));
            if (string.IsNullOrEmpty(key)) return;
            SavePqExtend(key, ReadPqExtend(key) + 1);
        }));
        // NPCConversationManager.canEnterPQ：count >= max（true = 次数已用完），照搬语义
        cm.Set("canEnterPQ", Ret(c => B((c.Arg(0) is JsString s0 ? ReadPqExtend(s0.Value) : ReadPqExtend(PqEnteredName(Int(c, 0))))
            >= PqEnterMax())));
        cm.Set("getPQEnterMaxCount", Ret(_ => N(PqEnterMax())));
        cm.Set("gainPQExp", Fn(c =>   // NPCConversationManager.gainPQExp：按等级封顶的副本经验
        {
            long rewardExp = Int(c, 0) * 10000L;
            int level = Profile.level, need = ExpTable.Needed(level);
            if (need < rewardExp)
            {
                if (level > 50 && level <= 70) rewardExp = (long)(need * 0.8);
                else if (level > 70 && level <= 100) rewardExp = (long)(need * 0.5);
                else if (level > 100 && level <= 124) rewardExp = (long)(need * 0.3);
                else rewardExp = need;
            }
            Profile.GrantExperience((int)Math.Min(int.MaxValue, rewardExp));
        }));

        // —— 扭蛋（GachaponService.doGachapon，抽取逻辑在 NativeScriptService.GachaDraw）——
        cm.Set("doGachapon", Fn(c => GachaDraw(session, NpcId(session) /* npc 即 gachaponId */)));
        cm.Set("doRemoteGachapon", Fn(c => GachaDraw(session, Int(c, 0))));
        cm.Set("getTotalGachaponCount", Ret(_ => N(0)));   // player_gachapon_stats 统计表未迁移

        // —— UI / 表现层（教程与过场用；无音频/无 WZ 演出系统，落点为提示或 no-op）——
        cm.Set("showEffect", Fn(c => { }));
        cm.Set("mapEffect", Fn(c => { }));
        cm.Set("showWZEffect", Fn(c => { }));
        cm.Set("showInstruction", Fn(c => ToastIfAny(Str(c, 0))));
        cm.Set("showInfoText", Fn(c => ToastIfAny(Str(c, 0))));
        cm.Set("openUI", Fn(c => { }));    // UIType 窗口枚举未迁移
        cm.Set("changeMusic", Fn(c => { }));   // 无 BGM 系统
        cm.Set("lockUI", Fn(c => PlayerCtrl.ScriptUiLock++));
        cm.Set("lockUI2", Fn(c => PlayerCtrl.ScriptUiLock++));
        cm.Set("unlockUI", Fn(c => PlayerCtrl.ScriptUiLock = 0));
        cm.Set("setDirectionStatus", Fn(c => { }));   // 教程方向指示 UI
        cm.Set("spawnGuide", Fn(c => { }));
        cm.Set("removeGuide", Fn(c => { }));
        cm.Set("talkGuide", Fn(c => { }));
        cm.Set("updateInfo", Fn(c => { }));   // 源端无此 API（其他分支遗留），脚本只在过场里调用

        // —— GM / 活动工具 ——
        cm.Set("getDisconnected", Ret(_ => B(false)));
        cm.Set("getConnection", Ret(_ => JsNull.Value));   // DatabaseConnection 管理脚本专用，单机不开放 DB
        cm.Set("divideTeams", Fn(c => { }));
        cm.Set("warpMembers", Fn(c => session.RequestWarp(Int(c, 0))));   // 单机=把自己传过去
        cm.Set("givePartyExp", Fn(c => Profile.GrantExperience(Int(c, 0))));
        cm.Set("givePartyItems", Fn(c => { int id = Int(c, 0), n = Int(c, 1, 1); if (n > 0) Profile.AddItem(id, n); else if (n < 0) Profile.RemoveItem(id, -n); }));
        cm.Set("removePartyItems", Fn(c => Profile.RemoveItem(Int(c, 0), Math.Max(1, Int(c, 1, 1)))));
        cm.Set("hasLevel30Character", Ret(_ => B(false)));
        cm.Set("getPlayerNPCByScriptid", Ret(_ => JsNull.Value));   // PlayerNPC 名人堂未迁移
        cm.Set("getHallOfFameMapid", Ret(_ => N(0)));
        cm.Set("maxMastery", Fn(c => { }));
        cm.Set("spawnPlayerNPC", Fn(c => { }));
        cm.Set("getTest", Ret(_ => BuildJavaList(new JsArray())));   // 源端无此 API（GMS 测试员遗留），给空列表保住 iterator 链
        cm.Set("weakenAreaBoss", Fn(c => { }));   // 封印/降回避 debuff 未迁移，只差广播文本
        cm.Set("getJailTimeLeft", Ret(_ => N(0)));   // 监狱系统未接入 = 无刑期
        cm.Set("getSkillBookInfo", Ret(_ => N(0)));
        cm.Set("getNamesWhoDropsItem", Ret(_ => BuildJavaList(new JsArray())));   // drop_data 反查走后台模块
        cm.Set("getAvailableMasteryBooks", Ret(_ => new JsArray()));
        cm.Set("getAvailableSkillBooks", Ret(_ => new JsArray()));
        cm.Set("getEquipInSlot", Ret(c =>
        {
            int id = Profile.EquipOfSlot(Int(c, 0));
            return id > 0 ? BuildEquipItem(id) : JsNull.Value;
        }));
        cm.Set("eatEquip", Fn(c => { }));   // 源端无此 API（活动 NPC 自定义），无法还原语义
        cm.Set("removeAllByInventorySlot", Fn(c => throw new JsRuntimeException("单机没有背包格位概念：removeAllByInventorySlot")));

        // —— 未迁移整套系统：空态 / 显式提示，不再走 Lenient 中断对话 ——
        cm.Set("getGuild", Ret(_ => JsNull.Value));          // 公会（NativeCommunityService 有 Guild 操作，但脚本面缺成员查询）
        cm.Set("isGuildLeader", Ret(_ => B(false)));
        cm.Set("displayGuildRanks", Fn(c => { }));
        cm.Set("createAlliance", Ret(_ => NotAvailable("联盟")));
        cm.Set("disbandAlliance", Ret(_ => NotAvailable("联盟")));
        cm.Set("upgradeAlliance", Ret(_ => NotAvailable("联盟")));
        cm.Set("getAllianceCapacity", Ret(_ => N(0)));
        cm.Set("canBeUsedAllianceName", Ret(_ => B(false)));
        cm.Set("createExpedition", Ret(_ => NotAvailable("远征队")));
        cm.Set("endExpedition", Ret(_ => NotAvailable("远征队")));
        cm.Set("isLeaderExpedition", Ret(_ => B(false)));
        cm.Set("getExpeditionMemberNames", Ret(_ => BuildJavaList(new JsArray())));
        cm.Set("fieldLobbied", Ret(_ => B(false)));
        cm.Set("fieldLobbied2", Ret(_ => B(false)));
        cm.Set("fieldTaken", Ret(_ => B(false)));
        cm.Set("fieldTaken2", Ret(_ => B(false)));
        cm.Set("cpqLobby", Ret(_ => JsNull.Value));
        cm.Set("cpqLobby2", Ret(_ => JsNull.Value));
        cm.Set("challengeParty", Ret(_ => NotAvailable("怪物嘉年华")));
        cm.Set("challengeParty2", Ret(_ => NotAvailable("怪物嘉年华")));
        cm.Set("sendCPQMapLists", Fn(c => { }));
        cm.Set("sendCPQMapLists2", Fn(c => { }));
        cm.Set("answerCPQChallenge", Fn(c => NotAvailable("怪物嘉年华")));
        cm.Set("cancelCPQLobby", Fn(c => { }));
        cm.Set("isCPQLoserMap", Ret(_ => B(false)));
        cm.Set("isCPQWinnerMap", Ret(_ => B(false)));
        cm.Set("getFestivalPoints", Ret(_ => N(0)));
        cm.Set("partyMembersInMap", Ret(_ => N(HasParty() ? 1 : 0)));
        cm.Set("getUnclaimedMarriageGifts", Ret(_ => N(0)));
        cm.Set("createMarriageWishlist", Ret(_ => NotAvailable("婚礼")));
        cm.Set("sendMarriageGifts", Ret(_ => NotAvailable("婚礼")));
        cm.Set("sendMarriageWishlist", Ret(_ => NotAvailable("婚礼")));
        cm.Set("getOngoingWedding", Ret(_ => JsNull.Value));
        cm.Set("getWeddingReservationTimeLeft", Ret(_ => N(0)));
        cm.Set("hasMerchant", Ret(_ => B(false)));
        cm.Set("hasMerchantItems", Ret(_ => B(false)));
        cm.Set("showFredrick", Fn(c => { }));
        cm.Set("resetDojoEnergy", Fn(c => { }));
        cm.Set("resetPartyDojoEnergy", Fn(c => { }));
        cm.Set("goDojoUp", Fn(c => { }));    // 武陵道场爬塔未迁移
        cm.Set("startAriantBattle", Ret(_ => NotAvailable("阿里安特竞技场")));
        cm.Set("createPyramid", Ret(_ => NotAvailable("金字塔")));
        cm.Set("gainTameness", Fn(c => { }));   // 宠物系统未迁移
        cm.Set("getDriedPets", Ret(_ => BuildJavaList(new JsArray())));
        cm.Set("getPet", Ret(_ => JsNull.Value));
        cm.Set("evolvePet", Ret(_ => JsNull.Value));   // 源端无宠物时同样返回 null + message
    }

    static JsValue NotAvailable(string system)
    {
        BottomBar.Toast(system + "系统未接入");
        return B(false);
    }

    // —— 副本计数存取：与源端同键（extend_value，isDaily=true → period 1 每日）——
    static int ReadPqExtend(string key)
        => int.TryParse(ScriptValueStore(key, null, period: 1), out int v) ? v : 0;

    static void SavePqExtend(string key, int value)
        => ScriptValueStore(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture), write: true, period: 1);

    // 源端 NPCConversationManager.getPQEnteredName 的 pqType 映射
    static string PqEnteredName(int type) => type switch
    {
        1 => "废弃已完成次数",
        2 => "玩具已完成次数",
        3 => "水晶已完成次数",
        _ => "",
    };

    // getPQEnterMaxCount：GameConfig.getServerInt(maxKey)，无配置默认 1；
    // game_config 服务端配置读取未开放给脚本层，按默认值处理。
    static int PqEnterMax() => 1;

    // —— 扭蛋：服务端抽（GachaDraw），客户端发奖 + 公告 ——
    static void GachaDraw(NpcSession session, int gachaponId)
    {
        if (!GameSession.InGame) throw new JsRuntimeException("Gachapon requires an active character");
        var rsp = GameSession.Net.Script(new ScriptRequest { operation = ScriptOperation.GachaDraw,
            characterId = GameSession.CharacterId, gachaponId = gachaponId });
        if (!(rsp.value is JsArray arr) || arr.Items.Count < 2)
        {
            BottomBar.Toast("百宝箱为空，请联系管理员，百宝箱id: " + gachaponId);
            return;
        }
        int itemId = JsConvert.ToInt32(arr.Items[0]), quantity = JsConvert.ToInt32(arr.Items[1]);
        Profile.AddItem(itemId, Math.Max(1, quantity));
        NativeSystemMessage.Drop(0, "你获得了 " + quantity + " 个 " + WzNames.Item(itemId));
    }

    static int NpcId(NpcSession session) => session?.NpcId ?? 0;

    // —— ScriptValue 直存直取（NativeScriptValues：0 永久 / 1 每日 / 2 每周）——
    static string ScriptValueStore(string name, string value, bool write = false, int period = 0)
    {
        if (!GameSession.InGame) throw new JsRuntimeException("Script values require an active character");
        return GameSession.Net.ScriptValue(new ScriptValueRequest
        {
            characterId = GameSession.CharacterId, write = write, period = period,
            name = name, value = value
        }).value;
    }

    // Java List 形态的空/实列表：脚本用 .size()/.get(i)/.iterator() 消费（如 getPartyMembersOnSameMap）
    static JsHostObject BuildJavaList(JsArray items)
    {
        var list = new JsHostObject(items);
        Lenient(list, "list");
        list.Set("size", Ret(_ => N(items.Items.Count)));
        list.Set("get", Ret(c => (Int(c, 0) >= 0 && Int(c, 0) < items.Items.Count) ? items.Items[Int(c, 0)] : JsNull.Value));
        list.Set("isEmpty", Ret(_ => B(items.Items.Count == 0)));
        list.Set("contains", Ret(c => B(items.Items.Contains(c.Arg(0)))));
        list.Set("iterator", Ret(_ =>
        {
            int cursor = 0;
            var it = new JsHostObject(null);
            it.Set("hasNext", Ret(_ => B(cursor < items.Items.Count)));
            it.Set("next", Ret(_ => cursor < items.Items.Count ? items.Items[cursor++] : JsNull.Value));
            return it;
        }));
        return list;
    }

    static JsHostObject BuildEquipItem(int itemId)
    {
        var item = new JsHostObject(itemId);
        item.Set("getItemId", Ret(_ => N(itemId)));
        item.Set("getQuantity", Ret(_ => N(1)));
        return item;
    }

    static bool IsPartyLeader()
    {
        if (!GameSession.InGame) return false;
        var party=GameSession.Net.Social(new SocialRequest { characterId=GameSession.CharacterId });
        return party.party && party.partyLeader==GameSession.CharacterId;
    }

    // warp 第二参的名字形态：cm.warp(910000000, "out00")
    static string WarpName(JsCall c, int i)
        => c.ArgCount > i && c.Arg(i) is JsString s && !string.IsNullOrEmpty(s.Value) ? s.Value : null;

    // warp 第二参的下标形态：cm.warp(toMap, pi.getMarketPortalId(toMap))
    static int WarpIndex(JsCall c, int i)
    {
        if (c.ArgCount <= i || c.Arg(i) is JsString) return -1;
        int id = JsConvert.ToInt32(c.Arg(i));
        return id >= 0 ? id : -1;
    }

    static int MarketPortalId(int mapId)
    {
        var map = FieldCatalog.GetMap(mapId);
        if (map == null || map.portals.Count == 0) return 0;
        for (int i = 0; i < map.portals.Count; i++)
            if (!string.IsNullOrEmpty(map.portals[i].script) && map.portals[i].script.Contains("market")) return i;
        // MapleMap.getRandomPlayerSpawnpoint：第一个 type=0 的门
        for (int i = 0; i < map.portals.Count; i++)
            if (map.portals[i].type == 0) return i;
        return 0;
    }
    static bool HasParty() => GameSession.InGame && GameSession.Net.Social(new SocialRequest
        { characterId = GameSession.CharacterId, operation = SocialOperation.Read }).party;

    static JsValue BuildParty(NpcSession session)
    {
        if (!GameSession.InGame) return JsNull.Value;
        var state=GameSession.Net.Social(new SocialRequest { characterId=GameSession.CharacterId });
        if (!state.party) return JsNull.Value;
        var party=new JsHostObject(null);
        JsValue Member(SocialMember member)
        {
            if (member==null) return JsNull.Value;
            if (member.id==GameSession.CharacterId) return BuildPlayer(session);
            var actor=new JsHostObject(null);
            actor.Set("getId",Ret(_=>N(member.id))); actor.Set("getName",Ret(_=>S(member.name)));
            actor.Set("getLevel",Ret(_=>N(member.level))); actor.Set("getJobId",Ret(_=>N(member.job)));
            actor.Set("getMapId",Ret(_=>N(member.mapId))); actor.Set("getChannel",Ret(_=>N(member.channel)));
            return actor;
        }
        party.Set("getId",Ret(_=>N(state.partyId)));
        party.Set("getLeaderId",Ret(_=>N(state.partyLeader)));
        party.Set("getLeader",Ret(_=>Member(state.partyMembers.Find(m=>m.id==state.partyLeader))));
        party.Set("getMembers",Ret(_=> { var members=new JsArray(); foreach (var member in state.partyMembers) members.Items.Add(Member(member)); return members; }));
        party.Set("getMemberById",Ret(c=>Member(state.partyMembers.Find(m=>m.id==Int(c,0)))));
        return party;
    }

    // Character.getStorage() + Storage.sendStorage（源端 Storage.java:232）。
    // 仓库系统未迁移，但源端的 15 级门槛要照搬：不到 15 级是「15级以后才可以使用仓库服务」，
    // 到了 15 级才轮到「尚未接入」。这样玩家看到的是明确的中文提示，而不是一句「对话脚本出错」。
    static JsHostObject BuildStorage()
    {
        var storage = new JsHostObject(null);
        storage.Set("sendStorage", Fn(c =>
        {
            if (Profile.level < 15) { BottomBar.Toast("15级以后才可以使用仓库服务"); return; }
            BottomBar.Toast("仓库系统尚未接入");
            NpcSession.CloseActive();
        }));
        // 其余 Storage 入口一起兜住，免得别的脚本换了写法又崩在同一个地方
        foreach (string name in new[] { "sendStored", "sendTakenOut", "sendMeso", "arrange", "close", "save", "increaseSlots" })
            storage.Set(name, Fn(_ => { }));
        storage.Set("getSlots", Ret(_ => N(0)));
        storage.Set("getMeso", Ret(_ => N(0)));
        storage.Set("getItems", Ret(_ => new JsArray()));
        storage.Set("isEmpty", Ret(_ => B(true)));
        return storage;
    }

    static JsHostObject BuildCashShop()
    {
        var cash = new JsHostObject(null);
        cash.Set("getCash", Ret(c => {
            var state = GameSession.Net.Cash(new CashRequest { characterId = GameSession.CharacterId });
            int type = Int(c, 0); return N(type == 1 ? state.credit : type == 2 ? state.points : type == 4 ? state.prepaid : 0);
        }));
        cash.Set("gainCash", Fn(c => {
            var state = GameSession.Net.Cash(new CashRequest { characterId = GameSession.CharacterId,
                operation = CashOperation.AdjustBalance, currency = Int(c, 0), amount = Int(c, 1) });
            if (!state.ok) BottomBar.Toast(state.error);
        }));
        return cash;
    }

    static void ToastIfAny(string text)
    {
        if (!string.IsNullOrEmpty(text)) BottomBar.Toast(text);
    }

    // dropMessage / playerMessage / mapMessage 三种签名一致：
    //   (type, message) 两参 → 按 type 落点；(message) 单参 → 源端等价 type 0。
    static void DropNotice(JsCall c)
    {
        if (c.ArgCount > 1) NativeSystemMessage.Drop(Int(c, 0), Str(c, 1));
        else NativeSystemMessage.Drop(0, Str(c, 0));
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
        // Character.isCygnus()（Character.java:6118）→ getJobType()==1，
        // getJobType() = job.getId()/1000（:5190），即 job ∈ [1000,1999] 的骑士团。
        // 缺了它 npc/9900008.js 五转女神在骑士团分支上直接抛「undefined is not a function」
        //（冒险家因为 && 短路躲过去，所以只绑冒险家测不出来）。
        p.Set("isCygnus", Ret(_ => B(QuestLogic.PlayerJob / 1000 == 1)));
        // Character.getFishLevel()（源端 V1.9.9__insert_fishing_items.sql 给 characters 加的 fishLevel 列）
        p.Set("getFishLevel", Ret(_ => N(GameSession.Character?.record.fishLevel ?? 1)));
        p.Set("getGender", Ret(_ => N(GameSession.Character?.record.gender ?? 0)));
        p.Set("getName", Ret(_ => S(Profile.playerName)));
        p.Set("isGM", Ret(_ => B((GameSession.Character?.record.gm ?? 0) > 1)));
        p.Set("getDamageCap", Ret(_ => N(GameSession.Character?.record.damageCap ?? 19999999)));
        // PacketCreator.calculateNameplatePower 就是拿这六个总属性算战力。
        // 之前只绑了 getDamageCap，脚本里的 try/catch 把「未迁移接口」的异常吞掉返回 0，
        // 于是战力只剩「MaxHP/10 + MaxMP/20」那一段，咖位永远停在最低档。
        Totals totals = default;
        bool totalsReady = false;
        Totals Total() { if (!totalsReady) { totals = PlayerTotals(); totalsReady = true; } return totals; }
        p.Set("getTotalStr", Ret(_ => N(Total().str)));
        p.Set("getTotalDex", Ret(_ => N(Total().dex)));
        p.Set("getTotalInt", Ret(_ => N(Total().intelligence)));
        p.Set("getTotalLuk", Ret(_ => N(Total().luk)));
        p.Set("getTotalWatk", Ret(_ => N(Total().watk)));
        p.Set("getTotalMagic", Ret(_ => N(Total().matk)));
        p.Set("getItemQuantity", Ret(c => N(Profile.ItemCount(Int(c, 0)))));
        p.Set("getItemName", Ret(c => S(WzNames.Item(Int(c, 0)))));
        // 源端这两个是服务器喇叭；单机没有别的玩家，落成底部提示才对得上「玩家看到了什么」。
        p.Set("checkoutBroadcast", Ret(_ => B(false)));
        p.Set("sendFullServerBroadcast", Fn(c => ToastIfAny(Str(c, 0))));
        p.Set("sendBroadcast", Fn(c => ToastIfAny(Int(c, 0) == 0 ? Str(c, 2) : Str(c, 1) + "：" + Str(c, 2))));
        BindSavedLocations(p);
        // Character.useCS（Character.java:472「chaos scroll upon crafting item」）：制作时是否消耗
        // 混乱卷轴的标记。时尚点装等脚本开场 setCS(true) 只是源端模板习惯；本机制作系统未迁移，先存值。
        p.Set("setCS", Fn(c => useCS = JsConvert.ToBoolean(c.Arg(0))));
        // Character.saveLocation(type) / saveLocationOnWarp()：warp 前记住「从哪来」（Character.java:8120/8131）
        p.Set("saveLocation", Fn(c => SaveLocation(Str(c, 0))));
        p.Set("saveLocationOnWarp", Fn(_ => SaveLocationOnWarp()));
        p.Set("startPartyGrindCompanions", Ret(_ => B(GameSession.InGame && GameSession.Net.PartyGrind(
            new Server.Bot.PartyGrindRequest { characterId=GameSession.CharacterId, operation=Server.Bot.PartyGrindOperation.Start }).ok)));
        p.Set("isPartyGrindActive", Ret(_ => B(GameSession.InGame && GameSession.Net.PartyGrind(
            new Server.Bot.PartyGrindRequest { characterId=GameSession.CharacterId }).active)));
        p.Set("stopPartyGrindCompanions", Fn(_ => { if (GameSession.InGame) GameSession.Net.PartyGrind(
            new Server.Bot.PartyGrindRequest { characterId=GameSession.CharacterId,operation=Server.Bot.PartyGrindOperation.Stop }); }));
        p.Set("getParty", Ret(_ => BuildParty(session)));
        p.Set("getId", Ret(_ => N(GameSession.InGame ? GameSession.CharacterId : 1)));
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
        p.Set("getCashShop", Ret(_ => GameSession.InGame ? BuildCashShop() : JsNull.Value));
        // Character.getStorage()（源端 Character.java）。仓库整套（存/取/金币/槽位 UI）未迁移，
        // 但必须返回一个对象：npc/9030100.js 是 `cm.getPlayer().getStorage().sendStorage(...)`，
        // 返回 undefined 会让「点仓库 NPC」直接崩成「对话脚本出错」。
        p.Set("getStorage", Ret(_ => BuildStorage()));
        p.Set("getFamilyEntry", Ret(_ => JsNull.Value));
        p.Set("sendPacket", Fn(c => { }));
        p.Set("cancelEffect", Fn(c => { }));
        p.Set("cancelAllBuffs", Fn(c => { }));
        p.Set("respawn", Fn(c => { }));
        p.Set("enableActions", Fn(c => { }));
        p.Set("setStance", Fn(c => { }));
        p.Set("startMapEffect", Fn(c => { }));
        p.Set("haveItemEquipped", Ret(_ => B(false)));
        p.Set("getMarriageItemId", Ret(_ =>
        {
            var stored = GameSession.InGame ? ScriptValueStore("marriageItemId", null) : null;
            return N(int.TryParse(stored, out int v) ? v : 0);
        }));
        BindCommonPlayer(p, session);
        return p;
    }

    // ————————————————————————————————————————————
    // player 宿主补齐（AbstractCharacterObject/Character 语义）：
    // 基础四维、HP/MP 池、AP/SP/等级/转职/转生、技能、蓝泉/仓库等脚本直调面。
    // ————————————————————————————————————————————
    static void BindCommonPlayer(JsHostObject p, NpcSession session)
    {
        // —— 基础四维（AbstractCharacterObject.attrStr 等：脚本 getStr 是裸基础值，不含装备）——
        p.Set("getStr", Ret(_ => N(Profile.Data.attributes.str)));
        p.Set("getDex", Ret(_ => N(Profile.Data.attributes.dex)));
        p.Set("getInt", Ret(_ => N(Profile.Data.attributes.intelligence)));
        p.Set("getLuk", Ret(_ => N(Profile.Data.attributes.luk)));
        // 带武器的攻防（战力系统按这六个值算咖位）
        p.Set("getWatk", Ret(_ => { var t = PlayerCombat(); return N(t.watk); }));
        p.Set("getMatk", Ret(_ => { var t = PlayerCombat(); return N(t.matk); }));
        p.Set("getWdef", Ret(_ => N(PlayerCombat().weaponDefense)));
        p.Set("getMdef", Ret(_ => N(PlayerCombat().magicDefense)));
        p.Set("setStr", Fn(c => SetBaseStat(str: Int(c, 0))));
        p.Set("setDex", Fn(c => SetBaseStat(dex: Int(c, 0))));
        p.Set("setInt", Fn(c => SetBaseStat(intl: Int(c, 0))));
        p.Set("setLuk", Fn(c => SetBaseStat(luk: Int(c, 0))));

        // —— HP/MP 池（getCurrentMaxHp=localMaxHp 含装备；baseMaxHp 即源端 clientMaxHp）——
        p.Set("getCurrentMaxHp", Ret(_ => N(Pc != null ? Pc.hpMax : Profile.Data.baseMaxHp)));
        p.Set("getCurrentMaxMp", Ret(_ => N(Pc != null ? Pc.mpMax : Profile.Data.baseMaxMp)));
        p.Set("getClientMaxHp", Ret(_ => N(Profile.Data.baseMaxHp)));
        p.Set("getClientMaxMp", Ret(_ => N(Profile.Data.baseMaxMp)));
        p.Set("addMaxHP", Fn(c => AdjustMaxHpMp(Int(c, 0), 0)));
        p.Set("addMaxMP", Fn(c => AdjustMaxHpMp(0, Int(c, 0))));
        p.Set("updateHpMaxHp", Fn(c => { AdjustMaxHpMp(Int(c, 1) - BaseMaxHp(), 0); if (Pc != null) Pc.hp = Int(c, 0); }));
        p.Set("updateMpMaxMp", Fn(c => { AdjustMaxHpMp(0, Int(c, 1) - BaseMaxMp()); if (Pc != null) Pc.mp = Int(c, 0); }));
        p.Set("updateMaxHpMaxMp", Fn(c => AdjustMaxHpMp(Int(c, 0) - BaseMaxHp(), Int(c, 1) - BaseMaxMp())));

        // —— AP / SP / 等级 / 转职 / 转生 ——
        p.Set("setRemainingAp", Fn(c => Profile.AddAp(Int(c, 0) - Profile.ap)));
        p.Set("gainSp", Fn(c => Profile.AddSp(Int(c, 0))));
        p.Set("setLevel", Fn(c =>
        {
            int target = Int(c, 0);
            if (target > 0 && target != Profile.level)
            {
                LevelSystem.SetLevel(Profile.Data, target);
                Profile.Save();
                Pc?.ApplyProgression();
            }
        }));
        p.Set("levelUp", Fn(_ =>
        {
            LevelSystem.SetLevel(Profile.Data, Profile.level + 1);
            Profile.Save();
            Pc?.ApplyProgression();
        }));
        p.Set("changeJob", Fn(c => ChangeJob(Int(c, 0))));
        // 转生次数存 extend_value("reborns")（源端存 characters.reborns 列）
        p.Set("getReborns", Ret(_ => N(PqExtendInt("reborns"))));
        p.Set("getRebornsCount", Ret(_ => N(PqExtendInt("reborns"))));
        // Character.executeRebornAs：满级校验后 addReborns → changeJob → setLevel(0) → levelUp
        p.Set("executeRebornAsId", Fn(c =>
        {
            int jobId = Int(c, 0);
            SavePqExtend("reborns", PqExtendInt("reborns") + 1);
            ChangeJob(jobId);
            Profile.Data.level = 1;
            Profile.Data.experience = 0;
            LevelSystem.EnsureBaseline(Profile.Data);
            Profile.Save();
            Pc?.ApplyProgression();
        }));

        // —— 技能 ——
        p.Set("getSkillLevel", Ret(c => N(SkillProgress.LevelOf(SkillIdOf(c, 0)))));
        p.Set("getSkills", Ret(_ =>
        {
            var arr = new JsArray();
            foreach (var kv in SkillProgress.AllLearned())
            {
                var skill = new JsHostObject(kv.Key);
                skill.Set("id", N(kv.Key));
                skill.Set("getId", Ret(_ => N(kv.Key)));
                skill.Set("getLevel", Ret(_ => N(SkillProgress.LevelOf(kv.Key))));
                arr.Items.Add(skill);
            }
            return arr;
        }));
        p.Set("changeSkillLevel", Fn(c =>
        {
            int id = SkillIdOf(c, 0);
            if (id > 0) SkillProgress.SetLevel(id, Int(c, 1, 0));
        }));
        p.Set("removeBySkillId", Ret(c =>
        {
            int id = Int(c, 0);
            if (SkillProgress.LevelOf(id) <= 0) return B(false);
            SkillProgress.SetLevel(id, 0);
            return B(true);
        }));
        p.Set("addSkillToKeyboard", Fn(c => { }));   // 键位在 PlayerPrefs，脚本批量配键场景未迁移

        // —— 装备 / 背包同步 ——
        p.Set("gainEquip", Fn(c => Profile.AddItem(Int(c, 0), Math.Max(1, Int(c, 1, 1)))));
        p.Set("equipChanged", Fn(c => Pc?.ApplyProgression()));   // Character.equipChanged：重算 local stats
        p.Set("forceUpdateItem", Fn(c => { }));   // 单机背包即改即存，无需补发包
        p.Set("setMarriageItemId", Fn(c => ScriptValueStore("marriageItemId", Int(c, 0).ToString(System.Globalization.CultureInfo.InvariantCulture), write: true)));

        // —— 放逐（Character.banishMap/banishSp/banishTime，怪物技能把玩家扔去别的图）——
        p.Set("getLastBanishData", Ret(_ =>
        {
            var data = new JsHostObject(null);
            int map = PqExtendInt("banishMap"), portal = PqExtendInt("banishSp");
            data.Set("left", N(map)); data.Set("getLeft", Ret(_ => N(map)));
            data.Set("right", N(portal)); data.Set("getRight", Ret(_ => N(portal)));
            return data;
        }));
        p.Set("canRecoverLastBanish", Ret(_ => B(PqExtendInt("banishMap") >= 0)));
        p.Set("clearBanishPlayerData", Fn(_ =>
        {
            SavePqExtend("banishMap", -1);
            SavePqExtend("banishSp", -1);
        }));

        // —— 倍率（NativeCommunityService：expRate/dropRate 已含 world×family）——
        p.Set("getExpRate", Ret(_ => N(Rates().expRate)));
        p.Set("getDropRate", Ret(_ => N(Rates().dropRate)));
        p.Set("getFamilyDrop", Ret(_ => N(Rates().familyDropRate)));

        // —— World 引用（益智答题这类脚本只调 broadcastPacket；单机=自己收到）——
        p.Set("getWorldServer", Ret(_ =>
        {
            var world = new JsHostObject(null);
            Lenient(world, "worldServer");
            world.Set("getExpRate", Ret(_ => N(Rates().expRate)));
            world.Set("broadcastPacket", Fn(c =>
            {
                // PackageCreator.serverNotice(type, text) 的结果脚本拿不到，退而求其次：
                // 把最后一个字符串参数按 type6（聊天窗浅蓝）投放。
                for (int i = c.ArgCount - 1; i >= 0; i--)
                    if (c.Arg(i) is JsString s && !string.IsNullOrEmpty(s.Value)) { NativeSystemMessage.Drop(6, s.Value); return; }
            }));
            return world;
        }));

        p.Set("hasLevel30Character", Ret(_ => B(false)));
        p.Set("getSpace", Ret(_ => N(24)));
        p.Set("getDisconnected", Ret(_ => B(false)));
    }

    // —— BindCommonPlayer 的小工具 ——
    static Totals totalsCache;

    static Totals PlayerCombat()
    {
        totalsCache = PlayerTotals();
        return totalsCache;
    }

    static int BaseMaxHp() => Profile.Data.baseMaxHp;
    static int BaseMaxMp() => Profile.Data.baseMaxMp;

    static void SetBaseStat(int str = -1, int dex = -1, int intl = -1, int luk = -1)
    {
        var a = Profile.Data.attributes;
        Profile.SetBaseStats(
            str >= 0 ? str : a.str,
            dex >= 0 ? dex : a.dex,
            intl >= 0 ? intl : a.intelligence,
            luk >= 0 ? luk : a.luk,
            Profile.Data.ap, Profile.Data.sp);
        Pc?.ApplyProgression();
    }

    // maxHp 加减走 baseMaxHp/baseMaxMp（源端 changeHpMpPool 语义：最终 local 值 = 请求值）
    static void AdjustMaxHpMp(int deltaHp, int deltaMp)
    {
        if (deltaHp == 0 && deltaMp == 0) return;
        Profile.Data.baseMaxHp = Math.Max(50, BaseMaxHp() + deltaHp);
        Profile.Data.baseMaxMp = Math.Max(5, BaseMaxMp() + deltaMp);
        Profile.Save();
        Pc?.ApplyProgression();
        if (Pc != null)
        {
            Pc.hp = Mathf.Clamp(Pc.hp, 0, Mathf.RoundToInt(Pc.hpMax));
            Pc.mp = Mathf.Clamp(Pc.mp, 0, Mathf.RoundToInt(Pc.mpMax));
        }
    }

    static int PqExtendInt(string key)
        => int.TryParse(ScriptValueStore(key, null), out int v) ? v : 0;

    static int SkillIdOf(JsCall c, int i)
    {
        if (c.Arg(i) is JsHostObject host)
        {
            var id = host.Get("id");
            if (id is JsNumber) return JsConvert.ToInt32(id);
        }
        return Int(c, i);
    }

    static CommunityResponse Rates()
        => GameSession.InGame
            ? GameSession.Net.Community(new CommunityRequest { characterId = GameSession.CharacterId, operation = CommunityOperation.Read })
            : new CommunityResponse();

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
        m.Set("getMobs", Ret(_ => {
            var result = new JsArray();
            foreach (var mob in EnemyUnit.All) if (mob.fieldMode && !mob.dead) result.Items.Add(BuildMonster(mob));
            return result;
        }));
        m.Set("countMonsters", Ret(_ => N(CountFieldMonsters())));
        m.Set("getSpawnedMonstersOnMap", Ret(_ => N(CountFieldMonsters())));
        m.Set("spawnMonsterOnGroundBelow", Fn(c => {
            int id = c.Arg(0) is JsHostObject host ? JsConvert.ToInt32(host.Get("id")) : Int(c,0);
            int x = Int(c,1), y = Int(c,2);
            if (c.Arg(1) is JsHostObject point) { x = JsConvert.ToInt32(point.Get("x")); y = JsConvert.ToInt32(point.Get("y")); }
            SpawnScriptMob(id,x,y,true);
        }));
        m.Set("killAllMonsters", Fn(c => GameLoop.I?.field?.ClearMobs()));
        m.Set("getReactorById", Ret(_ => JsNull.Value));
        m.Set("getReactor", Ret(_ => JsNull.Value));
        m.Set("getMonsterById", Ret(c => {
            foreach (var mob in EnemyUnit.All) if (mob.fieldMode && !mob.dead && mob.fieldMobId == Int(c,0)) return BuildMonster(mob);
            return JsNull.Value;
        }));
        return m;
    }

    static int CountFieldMonsters()
    {
        int count = 0;
        foreach (var mob in EnemyUnit.All) if (mob.fieldMode && !mob.dead) count++;
        return count;
    }
    static void SpawnScriptMob(int id, int x, int y, bool ground = false)
        => GameLoop.I?.field?.SpawnNativeMob(id,new Vector3(x / Art.PPU,-y / Art.PPU,0),ground);
    public static JsHostObject BuildJava()
    {
        var java = new JsHostObject(null);
        java.Set("type",Ret(c => {
            string name = Str(c,0);
            if (name == "java.awt.Point") return JsFunction.FromNative("Point",args => {
                var point = new JsHostObject(null);
                point.Set("x",N(Int(args,0))); point.Set("y",N(Int(args,1)));
                point.Set("getX",Ret(_ => point.Get("x"))); point.Set("getY",Ret(_ => point.Get("y")));
                return point;
            });
            // 原菜单在顶层导入通知工具，即使当前分支不调用也必须能取得类引用。
            // 尚未迁移的方法保留明确错误；不能将类引用解析等同于方法已实现。
            if (name == "org.gms.server.ItemInformationProvider")
            {
                var provider = new JsHostObject(null);
                Lenient(provider, "ItemInformationProvider");
                provider.Set("getInstance", Ret(_ => provider));
                provider.Set("getName", Ret(args => S(WzNames.Item(Int(args, 0)))));
                return provider;
            }
            if (name == "org.gms.util.I18nUtil" || name == "org.gms.net.server.Server" || name == "org.gms.util.PacketCreator")
            {
                var type = new JsHostObject(null);
                Lenient(type, name);
                if (name == "org.gms.net.server.Server") type.Set("getInstance", Ret(_ => type));
                return type;
            }
            if (name == "org.gms.client.SkillFactory")
            {
                var factory = new JsHostObject(null);
                Lenient(factory, "SkillFactory");
                factory.Set("getSkill", Ret(args => {
                    var data = SkillBook.Get(Int(args, 0));
                    if (data == null) return JsNull.Value;
                    var skill = new JsHostObject(data);
                    Lenient(skill, "skill");
                    skill.Set("getId", Ret(_ => N(data.id)));
                    skill.Set("getMaxLevel", Ret(_ => N(data.maxLevel)));
                    skill.Set("getName", Ret(_ => S(data.name)));
                    return skill;
                }));
                return factory;
            }
            if (name == "org.gms.server.life.LifeFactory")
            {
                var factory = new JsHostObject(null);
                factory.Set("getMonster",Ret(args => {
                    int id = Int(args,0);
                    FieldCatalog.LoadMob(id);
                    var monster = new JsHostObject(null);
                    Lenient(monster,"monster");
                    monster.Set("id",N(id)); monster.Set("getId",Ret(_ => N(id)));
                    return monster;
                }));
                return factory;
            }
            // 脚本里的枚举/工具类引用：值语义对了，逻辑就能照源端原样跑。
            // InventoryType 的数值取「道具 id 首位」（EQUIP=1 USE=2 SETUP=3 ETC=4 CASH=5），
            // 因为脚本是把它当 int 传给 getInventory(type) 的。
            if (name == "org.gms.client.inventory.InventoryType")
            {
                var type = new JsHostObject(null);
                type.Set("EQUIP", N(1)); type.Set("USE", N(2)); type.Set("SETUP", N(3));
                type.Set("ETC", N(4)); type.Set("CASH", N(5)); type.Set("EQUIPPED", N(-1));
                return type;
            }
            // org.gms.client.inventory.manipulator.InventoryManipulator（源端 InventoryManipulator.java）。
            // addById(Client, itemId, quantity) 语义就是往背包加道具，等价于 gainItem，可以照做；
            // removeFromSlot(Client, type, slot, quantity, fromDrop) 依赖背包格位（slot 来自 Item.getPosition()），
            // 单机没有格位概念，按 removeAllByInventorySlot 的既有口径明确报错，不装作成功。
            if (name == "org.gms.client.inventory.manipulator.InventoryManipulator")
            {
                var manipulator = new JsHostObject(null);
                Lenient(manipulator, "InventoryManipulator");
                manipulator.Set("addById", Ret(args =>
                {
                    int id = Int(args, 1), count = Int(args, 2, 1);
                    if (id > 0 && count > 0) Profile.AddItem(id, count);
                    return B(true);
                }));
                manipulator.Set("removeFromSlot", Fn(args =>
                    throw new JsRuntimeException("Unmigrated server API: InventoryManipulator.removeFromSlot（单机没有背包格位概念）")));
                return manipulator;
            }
            // org.gms.constants.id.ItemId（ItemId.java）：npc/9330045.js 用它的 isChair 判断
            // 玩家背包里有没有椅子（决定能不能进钓鱼场）。源端就是 RELAXER=3010000 ~ 3019999。
            if (name == "org.gms.constants.id.ItemId")
            {
                var itemId = new JsHostObject(null);
                Lenient(itemId, "ItemId");
                itemId.Set("RELAXER", N(3010000));
                itemId.Set("isChair", Ret(args => { int id = Int(args, 0); return B(id >= 3010000 && id <= 3019999); }));
                return itemId;
            }
            // org.gms.constants.game.ExpTable：客户端已按原经验表算好，直接查同一张表
            if (name == "org.gms.constants.game.ExpTable")
            {
                var table = new JsHostObject(null);
                Lenient(table, "ExpTable");
                table.Set("getExpNeededForLevel", Ret(args => N(ExpTable.Needed(Int(args, 0)))));
                return table;
            }
            // slf4j 只用于排查日志：落到 Unity 日志即可，不能因为「类不存在」中断整段对话
            if (name == "org.slf4j.LoggerFactory")
            {
                var factory = new JsHostObject(null);
                factory.Set("getLogger", Ret(args => BuildLogger(Str(args, 0))));
                return factory;
            }
            // CashShop 的货币编号常量（CashShop.java:72-75）。脚本只拿它们当 type 传给 getCash/gainCash。
            if (name == "org.gms.server.CashShop")
            {
                var cash = new JsHostObject(null);
                Lenient(cash, "CashShop");
                cash.Set("NX_CREDIT", N(1)); cash.Set("MAPLE_POINT", N(2)); cash.Set("NX_PREPAID", N(4));
                cash.Set("MAX_CASH_INVENTORY_SAFE", N(1000));
                return cash;
            }
            return JsNull.Value;
        }));
        return java;
    }
    // slf4j Logger 的替身：info/debug 落普通日志，warn/error 落警告。
    static JsHostObject BuildLogger(string name)
    {
        var logger = new JsHostObject(null);
        Lenient(logger, "logger");
        foreach (string level in new[] { "trace", "debug", "info", "warn", "error" })
        {
            string tag = level;
            logger.Set(level, Fn(c =>
            {
                var text = new System.Text.StringBuilder();
                for (int i = 0; i < c.ArgCount; i++)
                {
                    if (i > 0) text.Append(' ');
                    text.Append(JsConvert.ToString(c.Arg(i)));
                }
                if (tag == "warn" || tag == "error") Debug.LogWarning("[Script/" + name + "] " + text);
                else Debug.Log("[Script/" + name + "] " + text);
            }));
        }
        logger.Set("isDebugEnabled", Ret(_ => B(false)));
        logger.Set("isInfoEnabled", Ret(_ => B(true)));
        return logger;
    }

    static JsHostObject BuildMonster(EnemyUnit mob)
    {
        var result = new JsHostObject(mob);
        result.Set("id",N(mob.fieldMobId));
        result.Set("getId",Ret(_ => N(mob.fieldMobId)));
        result.Set("getObjectId",Ret(_ => N(mob.GetInstanceID())));
        result.Set("isAlive",Ret(_ => B(mob != null && !mob.dead)));
        result.Set("getHp",Ret(_ => N(mob.hp)));
        result.Set("getMaxHp",Ret(_ => N(mob.hpMax)));
        return result;
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
        c.Set("getChannel", Ret(_ => N(GameSession.Channel)));
        c.Set("getWorld", Ret(_ => N(GameSession.World)));
        c.Set("getChannelServer", Ret(_ => BuildChannelServer(player)));
        return c;
    }

    // 单机只有自己在线：ChannelServer.getPlayerStorage().getAllCharacters() 只装自己一个。
    // 共同富裕 / 冒险炒股 这类「给当前频道所有人发点券」的脚本靠这条链跑通，
    // 语义上等价于「发给自己」。
    static JsHostObject BuildChannelServer(JsHostObject player)
    {
        var server = new JsHostObject(null);
        Lenient(server, "channelServer");
        var storage = new JsHostObject(null);
        Lenient(storage, "playerStorage");
        storage.Set("getAllCharacters", Ret(_ => new JsArray(new JsValue[] { player })));
        storage.Set("getAllCharactersCount", Ret(_ => N(1)));
        storage.Set("getCharacterById", Ret(_ => player));
        server.Set("getPlayerStorage", Ret(_ => storage));
        server.Set("getChannel", Ret(_ => N(GameSession.Channel)));
        server.Set("getWorld", Ret(_ => N(GameSession.World)));
        return server;
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
        // Inventory.iterator()（源端 Inventory.java）：脚本拿它遍历某一栏的道具。
        // npc/9330045.js 的 hasChair() 就是扫 SETUP 栏找椅子，决定能不能进钓鱼场。
        // 栏位 = 道具 id 首位，与上面的 InventoryType 数值（EQUIP=1…SETUP=3…）同一套。
        inv.Set("iterator", Ret(_ => BuildInventoryIterator(type)));
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

    static JsHostObject BuildInventoryIterator(int type)
    {
        var ids = new List<int>();
        var data = Profile.Data;
        if (data.itemIds != null)
            for (int i = 0; i < data.itemIds.Count; i++)
            {
                if (i >= data.itemCounts.Count) break;
                int id = data.itemIds[i];
                if (id / 1000000 == type && data.itemCounts[i] > 0) ids.Add(id);
            }
        int index = 0;
        var it = new JsHostObject(null);
        it.Set("hasNext", Ret(_ => B(index < ids.Count)));
        it.Set("next", Ret(_ =>
        {
            int id = index < ids.Count ? ids[index++] : 0;
            var item = new JsHostObject(id);
            item.Set("getItemId", Ret(__ => N(id)));
            item.Set("getQuantity", Ret(__ => N(Profile.ItemCount(id))));
            return item;
        }));
        return it;
    }
}
