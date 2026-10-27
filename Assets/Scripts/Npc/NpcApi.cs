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
        cm.Set("warp", Fn(c => session.RequestWarp(Int(c, 0))));
        cm.Set("warpParty", Fn(c => session.RequestWarp(Int(c, 0))));
        cm.Set("getWarpMap", Ret(_ => N(0)));
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
        cm.Set("isLeader", Ret(_ => B(HasParty())));
        cm.Set("isPartyLeader", Ret(_ => B(HasParty())));
        cm.Set("isEventLeader", Ret(_ => B(false)));
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

    struct Totals { public int str, dex, intelligence, luk, watk, matk; }

    // Character.reapplyLocalStats 的等价：基础属性 + 武器 + 全部防具（与能力值窗同一条路）。
    static Totals PlayerTotals()
    {
        var weapon = WeaponDefs.Get(Profile.equipWeapon);
        var combat = Profile.Attributes.WithWeapon(weapon, 0, out int watk, out int matk);
        return new Totals
        {
            str = combat.str, dex = combat.dex, intelligence = combat.intelligence, luk = combat.luk,
            watk = watk, matk = matk
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

    static bool HasParty() => GameSession.InGame && GameSession.Net.Social(new SocialRequest
        { characterId = GameSession.CharacterId, operation = SocialOperation.Read }).party;

    static JsValue BuildParty(NpcSession session)
    {
        if (!HasParty()) return JsNull.Value;
        var party = new JsHostObject(null);
        var self = BuildPlayer(session);
        party.Set("getId", Ret(_ => N(GameSession.CharacterId)));
        party.Set("getLeaderId", Ret(_ => N(GameSession.CharacterId)));
        party.Set("getLeader", Ret(_ => self));
        party.Set("getMembers", Ret(_ => new JsArray(new JsValue[] { self })));
        party.Set("getMemberById", Ret(c => Int(c, 0) == GameSession.CharacterId ? (JsValue)self : JsNull.Value));
        return party;
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
        // Character.saveLocation(type) / saveLocationOnWarp()：warp 前记住「从哪来」（Character.java:8120/8131）
        p.Set("saveLocation", Fn(c => SaveLocation(Str(c, 0))));
        p.Set("saveLocationOnWarp", Fn(_ => SaveLocationOnWarp()));
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
