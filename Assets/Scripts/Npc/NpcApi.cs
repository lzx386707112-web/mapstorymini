using System;
using System.Collections.Generic;
using MapleDash.Js;
using UnityEngine;

// cm / qm 的 C# 实现。方法名与源端 NPCConversationManager / QuestActionManager 对齐；
// 未迁移接口明确报错；已实现 getter 仅在源端对应的合法空状态返回 null。
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

    static void SetNativeAppearance(string part,int id)
    {
        var record=GameSession.Character?.record;if(record==null) return;
        if(part=="hair") record.hair=id;
        else if(part=="face") record.face=id;
        else record.skincolor=id;
        Pc?.RefreshFieldAppearance();GameSession.SaveToDb();
    }

    static bool CanHold(int[] items,int[] quantities,int removeItem=0,int removeQuantity=0)
    {
        if (GameSession.InGame)
            return GameSession.Net.ChangeInventory(new InventoryChangeRequest {
                characterId=GameSession.CharacterId,checkItems=items,checkQuantities=quantities,
                removeCheckItem=removeItem,removeCheckQuantity=removeQuantity }).ok;
        // 编辑器回归（batchmode）没有服务端角色，旧写法直接 return false：于是
        // 一转脚本里 canHold(1302077) 一律走「背包满了」分支，五个职业的一转/二转回归
        // 全红，把真正的断链埋掉。这里和 headlessHp/HeadlessPosition 同样思路，
        // 按本地存档的分页容量估算（不查询在线背包）。
        return CanHoldLocal(items, quantities, removeItem);
    }

    static bool CanHoldLocal(int[] items, int[] quantities, int removeItem)
    {
        var used = new Dictionary<int, int>();
        for (int tab = 0; tab < 5; tab++) used[tab] = Profile.InventoryItems(tab).Count;
        if (removeItem > 0)
        {
            int tab = removeItem / 1000000 - 1;
            if (tab >= 0 && tab < 5) used[tab] = Math.Max(0, used[tab] - 1);
        }
        for (int i = 0; i < items.Length; i++)
        {
            int id = items[i];
            int qty = quantities != null && i < quantities.Length ? quantities[i] : 1;
            if (id <= 0 || qty == 0) continue;
            int tab = id / 1000000 - 1;
            if (tab < 0 || tab > 4) continue;
            // 可叠加的道具（消耗/设置/其他）已有存货时先填空位，不额外占格
            bool stacks = tab != 0 && Profile.ItemCount(id) > 0;
            if (stacks) continue;
            if (used[tab] >= Profile.InventoryCapacity(tab)) return false;
            used[tab]++;
        }
        return true;
    }

    static PlayerCtrl Pc => GameLoop.I != null ? GameLoop.I.player : null;
    static int MapId => Profile.LastMapId;

    // ——— 没有 PlayerCtrl 时的 HP/MP 镜像 ———
    // 事件脚本靠 player.getHp()/isAlive()/updateHpMp()/addHP() 判断生死与扣血：起源之塔每 500ms
    // 的 tick 先判 isAlive（死了立刻 fail）、第 8 层火柱把 HP/MP 压到 1%、第 13 层青色落石直接
    // addHP(-getHp())。编辑器回归是 batchmode，没有 PlayerCtrl，如果这些接口一律读 Pc 就是 hp=0、
    // isAlive=false，tick 第一拍就把玩家判死。这里保持与 Pc 完全相同的语义，只把「没有真实角色」
    // 这一种情况落到镜像上，上限与 getCurrentMaxHp/getCurrentMaxMp 的无 Pc 分支同源。
    static int headlessHp = -1, headlessMp = -1;
    static FullCharacter headlessOwner;
    // 同上：没有 PlayerCtrl 时的玩家坐标镜像（地图像素坐标、Y 向下）。事件脚本会按坐标判分支
    // （起源之塔第 19 层的护送距离、第 17 层的掉落判定），batchmode 回归需要一个可放置的玩家。
    // 生产环境 Pc 恒在，这一份永远读到 (0,0)，不会影响游戏。
    internal static Vector2 HeadlessPosition;
    static int MaxHpFallback => Math.Max(1, Profile.Data.baseMaxHp);
    static int MaxMpFallback => Math.Max(1, Profile.Data.baseMaxMp);
    static int ClampHpMp(int value, int max) => value < 0 ? 0 : value > max ? max : value;
    static void SyncHeadless()
    {
        var character = GameSession.Character;
        if (Pc != null || ReferenceEquals(headlessOwner, character)) return;
        headlessOwner = character;
        headlessHp = ClampHpMp(character?.record.hp ?? MaxHpFallback, MaxHpFallback);
        headlessMp = ClampHpMp(character?.record.mp ?? MaxMpFallback, MaxMpFallback);
    }
    static int CurrentHp
    {
        get { if (Pc != null) return Mathf.RoundToInt(Pc.hp); SyncHeadless(); return headlessHp; }
        set { if (Pc != null) Pc.hp = value; else { SyncHeadless(); headlessHp = ClampHpMp(value, MaxHpFallback); } }
    }
    static int CurrentMp
    {
        get { if (Pc != null) return Mathf.RoundToInt(Pc.mp); SyncHeadless(); return headlessMp; }
        set { if (Pc != null) Pc.mp = value; else { SyncHeadless(); headlessMp = ClampHpMp(value, MaxMpFallback); } }
    }
    // 服务端控制器的原生入口（LucidBossCompat 的 applyDamage）。语义与脚本 side 的
    // player.getMaxHp()/addHP()/isAlive()/getPosition() 完全同一份，不另立镜像。
    internal static int ScriptHp { get => CurrentHp; set => CurrentHp = value; }
    internal static int ScriptMaxHp => Pc != null ? Mathf.RoundToInt(Pc.hpMax) : MaxHpFallback;
    internal static bool ScriptAlive => Pc != null ? Pc.hp > 0 : CurrentHp > 0;
    internal static int ScriptMapId => MapId;
    internal static Vector2 ScriptPosition => new Vector2(
        Pc != null ? Pc.worldX * Art.PPU : HeadlessPosition.x,
        Pc != null ? -Pc.FeetPos.y * Art.PPU : HeadlessPosition.y);

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

        cm.Set("startMonsterVac", Ret(_ => B(Pc != null && NativeMonsterVac.For(Pc).StartVac())));
        cm.Set("isMonsterVacActive", Ret(_ => B(Pc?.GetComponent<NativeMonsterVac>()?.Active == true)));
        cm.Set("stopMonsterVac", Fn(_ => Pc?.GetComponent<NativeMonsterVac>()?.StopVac()));
        cm.Set("getPlayer", Ret(_ => player));
        cm.Set("getChar", Ret(_ => player));
        cm.Set("getNpc", Ret(_ => N(npcId)));
        cm.Set("getItemId", Ret(_ => N(session.ItemId)));
        cm.Set("isItemScript", Ret(_ => B(session.ItemId>0)));
        cm.Set("useDamageCapBreakthroughStone", Ret(_ =>
        {
            var response=GameSession.Net.ChangeInventory(new InventoryChangeRequest {characterId=GameSession.CharacterId,itemId=session.ItemId,breakthrough=true});
            if(!response.ok) return S(response.error);
            GameSession.Character.record.damageCap=response.damageCap;
            GameSession.Character.inventory=response.inventory; GameSession.ApplyInventoryToProfile(); Profile.Save();
            return S(response.message);
        }));
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
            var ids = new List<int>();
            if(styles!=null) foreach(var value in styles.Items) ids.Add(JsConvert.ToInt32(value));
            session.SetDialog(NpcDialogMode.Style, Str(c,0));
            session.Dialog.Styles=ids.ToArray();
        }));
        cm.Set("sendDefault", Fn(c => session.SetDialog(NpcDialogMode.Ok,
            WzAssets.FileRoot("String/Npc.img").Get(npcId.ToString())?.Text("d0", "(...)") ?? "(...)")));
        cm.Set("sendImage", Fn(c => session.SetDialog(NpcDialogMode.Ok, Str(c, 0))));
        cm.Set("sendNoExit", Fn(c => session.SetDialog(NpcDialogMode.Ok, Str(c, 0))));
        cm.Set("sendDirectionInfo", Fn(c => { }));
        cm.Set("sendDimensionalMirror", Fn(c => { }));
        cm.Set("dispose", Fn(c => { FlushEquip(); session.DisposeFromScript(); }));
        // AbstractPlayerInteraction.enableActions sends only the action-unlock
        // packet. Local item execution has no network action lock to release.
        cm.Set("enableActions", Fn(c => { }));

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
        // Character.setNameplateSeries 会广播 0x17C；单机里「广播」就是刷新脚下那一行。
        cm.Set("setNameplateSeries", Fn(c =>
        {
            int series = Math.Max(0, Math.Min(NativeNameplate.SeriesCount - 1, Int(c, 0)));
            WriteCharacterValue("nameplate_title_series", series.ToString());
            NativeNameplate.SetSeries(series);
        }));
        cm.Set("getPqPoints", Ret(_ => N(ReadCharacterInteger("副本积分"))));
        cm.Set("getPqTotalPoints", Ret(_ => N(ReadCharacterInteger("副本总积分"))));
        cm.Set("gainMeso", Fn(c => Profile.AddGold(Int(c, 0))));
        cm.Set("gainExp", Fn(c => Profile.GrantExperience(Int(c, 0))));
        cm.Set("getLevel", Ret(_ => N(Profile.level)));
        cm.Set("getJobId", Ret(_ => N(QuestLogic.PlayerJob)));
        cm.Set("getJob", Ret(_ => BuildJob(QuestLogic.PlayerJob)));
        cm.Set("getGender", Ret(_ => N(GameSession.Character?.record.gender ?? 0)));
        cm.Set("getName", Ret(_ => S(Profile.playerName)));
        cm.Set("getFame", Ret(_ => N(GameSession.Character?.record.fame ?? 0)));
        cm.Set("gainFame", Fn(c => ChangeFame(ScriptOperation.GainFame,Int(c,0))));

        // —— 道具 ——
        // 转职凭证互斥：源端 1072000/1072001/1072002/1072003 进考验场时必定扣掉介绍信
        // （cm.gainItem(4031008..4031011, -1)），所以「身上同时有英雄的证明 4031012 和介绍信」
        // 在源端不可能出现。但后台发放 / GM 直接给证明会跳过那个 NPC，介绍信就留在身上：
        // 1022000 的 status==2 守卫 `cm.haveItem(4031008)` 立刻 dispose，玩家选完方向
        // 就"没反应"——对话走完、任务也过了，职业却没变。这里按源端语义把过期介绍信视为已失效。
        cm.Set("haveItem", Ret(c => B(HasItem(Int(c, 0), Int(c, 1, 1)))));
        cm.Set("hasItem", Ret(c => B(HasItem(Int(c, 0), Int(c, 1, 1)))));
        // Character.haveItemWithId(itemid, checkEquipped)（Character.java:5153）：背包找到，
        // 或 checkEquipped 时连装备栏（EQUIPPED）一起查——第二参是布尔，与 haveItem 的数量不同。
        cm.Set("haveItemWithId", Ret(c => B(HasItemWithId(Int(c, 0), c.ArgCount > 1 && JsConvert.ToBoolean(c.Arg(1))))));
        cm.Set("itemQuantity", Ret(c => N(ItemQuantity(Int(c, 0)))));
        cm.Set("getItemQuantity", Ret(c => N(ItemQuantity(Int(c, 0)))));
        // 源端 AbstractPlayerInteraction.gainItem（:587-690）：正数发道具后返回**新道具对象**
        // （背包满才返回 null），负数走 removeById、item 保持 null 返回。
        // 脚本靠这个返回值判断成败：quest/3414_free10rate.js 的 `item != null` 决定
        // 「给经验 + 交任务」，BeiDouSpecial 制作脚本的 `if (!newItem)` 决定「制作失败」。
        // 以前一律返回 undefined，于是这两个判断都判反了。
        cm.Set("gainItem", Ret(c =>
        {
            int id = Int(c, 0), count = Int(c, 1, 1);
            if (id <= 0 || count == 0) return JsNull.Value;
            if (count < 0) { Profile.RemoveItem(id, -count); return JsNull.Value; }
            if (!CanHold(new[] {id}, new[] {count}))
            {
                string[] names = { "", "EQUIP", "USE", "SETUP", "ETC", "CASH" };
                int type = id / 1000000;
                NativeSystemMessage.Drop(1, "您的背包已满，请从" + (type >= 1 && type <= 5 ? names[type] : "") + "栏移除一件物品。");
                return JsNull.Value;
            }
            int before = Profile.ItemCount(id);
            Profile.AddItem(id, count);
            return Profile.ItemCount(id) - before == count ? NewItemRef(id, count) : JsNull.Value;
        }));
        cm.Set("canHold", Ret(c => B(CanHold(new[] {Int(c,0)},new[] {Int(c,1,1)},Int(c,2),Int(c,3)))));
        cm.Set("canHoldAll", Ret(c =>
        {
            var ids=c.Arg(0) as JsArray; var amounts=c.Arg(1) as JsArray;
            if(ids==null) return B(false);
            var items=new List<int>(); var quantities=new List<int>();
            for(int i=0;i<ids.Items.Count;i++)
            { items.Add(JsConvert.ToInt32(ids.Items[i])); quantities.Add(amounts!=null && i<amounts.Items.Count ? JsConvert.ToInt32(amounts.Items[i]) : 1); }
            return B(CanHold(items.ToArray(),quantities.ToArray()));
        }));
        cm.Set("canGenerateItem", Ret(c => B(WzNames.HasItem(Int(c, 0)))));
        cm.Set("isNotCanHold", Ret(c =>
        {
            int type=Int(c,0), needed=Int(c,1,2);
            if(type<1 || type>5) return B(true);
            var used=new HashSet<int>();
            if(GameSession.Character?.inventory!=null)
                foreach(var item in GameSession.Character.inventory)
                    if(item.inventoryType==type && item.position>0) used.Add(item.position);
            bool full=Profile.InventoryCapacity(type-1)-used.Count<needed;
            if(full)
            {
                string[] names={"装备","消耗","设置","其他","特殊"};
                session.SetDialog(NpcDialogMode.Ok,"#r背包空间不足，请确保#b"+names[type-1]+"栏#r至少有#b"+needed+"#r个空格！");
                session.DisposeFromScript();
            }
            return B(full);
        }));
        // ItemInformationProvider.getName / itemDataExists 的等价判断：WZ 里有没有这件道具。
        cm.Set("getCosmeticItem", Ret(c =>
        {
            int id = Int(c, 0);
            if (WzNames.HasItem(id)) return N(id);
            int baseId = id < 30000 ? id / 1000 * 1000 + id % 100 : id / 10 * 10;
            return N(id != baseId && WzNames.HasItem(baseId) ? baseId : -1);
        }));
        cm.Set("isCosmeticEquipped", Ret(c =>
        {
            int id = Int(c, 0);
            var record = GameSession.Character?.record;
            return B(record != null && (id < 30000 ? record.face : record.hair) == id);
        }));
        cm.Set("itemExists", Ret(c => B(WzNames.HasItem(Int(c, 0)))));
        cm.Set("hasItemData", Ret(c => B(!string.IsNullOrEmpty(WzNames.Item(Int(c, 0))))));
        cm.Set("getInventory", Ret(c => BuildInventory(Int(c, 0))));
        cm.Set("removeAll", Fn(c =>
        {
            int id=Int(c,0);
            if(!GameSession.InGame) { int count=Profile.ItemCount(id);if(count>0) Profile.RemoveItem(id,count);return; }
            var result=GameSession.Net.ChangeInventory(new InventoryChangeRequest {characterId=GameSession.CharacterId,itemId=id,removeAll=true});
            if(!result.ok) throw new JsRuntimeException(result.error);
            GameSession.Character.inventory=result.inventory;GameSession.ApplyInventoryToProfile();Profile.Save();
        }));
        cm.Set("removeAllByInventory", Fn(c => RemoveInventory(Int(c, 0))));
        // 单机没有「格位」概念（inventoryitems 一行一道具），拿到 slot 也无法定位，明确不装作成功。
        cm.Set("removeAllByInventorySlot", Fn(c => RemoveInventorySlot(Int(c, 0), Int(c, 1))));

        // —— 传送 ——
        // AbstractPlayerInteraction.warp(map[, portal])：第二参可以是门名（String）也可以是
        // WZ portal 下标（int，= 源端 Portal.getId）。FM 脚本 market00 用后者。
        // Character.changeMap(map, pt)（Character.java:1597）在 eim 为空时走 getMap(map, true)：
        // 目标图取不到只发两条系统消息（type5 聊天窗 + type1 弹窗「你无法去到不存在的地图(x)」）
        // 然后 return，脚本照常往下跑，**不会**抛异常。这里必须照做——2101.js（番茄田的希娜，
        // 「结束训练就送你出训练营」）最后就是 cm.warp(0)，而 0 号图是真实存在的地图
        // （Data/Map/Map/Map0/000000000.img，info/onUserEnter = goAdventure 的开场过场舞台）。
        // 只有负数 id 仍抛错：portal/market00.js 靠 try/catch 回退到自由市场（本端保留的兜底）。
        cm.Set("warp", Fn(c =>
        {
            int target = Int(c, 0);
            if (target < 0) throw new JsRuntimeException("warp: 无效的地图 id " + target);
            if (!MapExists(target))
            {
                string msg = "[系统] 你无法去到不存在的\r\n地图(" + target + ")，请联系GM处理。";
                NativeSystemMessage.Drop(5, msg);
                NativeSystemMessage.Drop(1, msg);
                return;
            }
            session.RequestWarp(target, WarpName(c, 1), WarpIndex(c, 1));
        }));
        cm.Set("warpParty", Fn(c => session.RequestWarp(Int(c, 0), WarpName(c, 1), WarpIndex(c, 1))));
        // AbstractPlayerInteraction.getPortal()：portal 脚本靠它取自己踩的是哪道门
        // （portal/seedTower.js 就是拿 getName() 决定 ptOut / go1F / 层内出口）。
        cm.Set("getPortal", Ret(_ =>
        {
            var portal=session?.Portal;
            if(portal==null)return JsNull.Value;
            var host=new JsHostObject(portal);
            host.Set("getPosition",Ret(c=>BuildPoint(portal.x,portal.y)));
            host.Set("getMapId",Ret(c=>N(MapId)));
            host.Set("getName",Ret(c=>S(portal.name)));
            host.Set("getScriptName",Ret(c=>S(portal.script)));
            host.Set("getType",Ret(c=>N(portal.type)));
            host.Set("getId",Ret(c=>N(portal.id)));
            return host;
        }));
        cm.Set("getWarpMap", Ret(c => BuildWarpMap(Int(c, 0))));
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
        cm.Set("getQuestRecord", Ret(c =>
        {
            int id = Int(c, 0);
            var record = new JsHostObject(id);
            Lenient(record, "questRecord");
            JsValue Data(bool write, JsCall args)
            {
                if (!GameSession.InGame) throw new JsRuntimeException("Quest record requires an active character");
                return GameSession.Net.Script(new ScriptRequest { characterId=GameSession.CharacterId, questId=id,
                    operation=write ? ScriptOperation.SetQuestCustomData : ScriptOperation.GetQuestCustomData,
                    customData=args.Arg(0) is JsNull ? null : Str(args,0) }).value;
            }
            record.Set("getCustomData", Ret(args => Data(false, args)));
            record.Set("setCustomData", Fn(args => { Data(true, args); }));
            record.Set("getStatus", Ret(args => N(Profile.QuestStatus(id))));
            return record;
        }));
        cm.Set("getQuestStatus", Ret(c => N(Profile.QuestStatus(Int(c, 0)))));
        cm.Set("getQuestProgressInt", Ret(c => N(Profile.QuestProgressInt(Int(c, 0), Key(c, 1), Int(c, 2, 0)))));
        cm.Set("getQuestProgress", Ret(c => S(Profile.QuestProgress(Int(c, 0), Key(c, 1), ""))));
        cm.Set("setQuestProgress", Fn(c => Profile.SetQuestProgress(Int(c, 0), Key(c, 1), Str(c, 2))));
        // 源端 QuestActionManager（QuestActionManager.java:57-73）：
        //   forceStartQuest() / forceCompleteQuest() 无参 = **当前任务**；
        //   startQuest() / completeQuest() 无参只是老脚本的兼容写法，同样指向当前任务。
        // AbstractPlayerInteraction 只有带 id 的重载，所以这里必须给无参调用兜底，
        // 否则 quest/2001.js、3414.js 交付后写的 qm.completeQuest() 会变成空操作。
        cm.Set("startQuest", Fn(c => StartQuest(Int(c, 0, questId), npcId)));
        cm.Set("completeQuest", Fn(c => CompleteQuest(Int(c, 0, questId), npcId)));
        cm.Set("forceStartQuest", Fn(c => StartQuest(Int(c, 0, questId), npcId)));
        cm.Set("forceCompleteQuest", Fn(c => CompleteQuest(Int(c, 0, questId), npcId)));

        // —— 技能 / 职业 / 外观 ——
        cm.Set("teachSkill", Fn(c =>
        {
            int id = Int(c, 0), level = Int(c, 1, 1), master = Int(c, 2);
            bool force = c.ArgCount > 4 && JsConvert.ToBoolean(c.Arg(4));
            if (!force && level >= 0)
            {
                level = Math.Max(level, SkillProgress.LevelOf(id));
                master = Math.Max(master, SkillBookItems.MasterLevelFor(id));
            }
            SetScriptSkill(id, level, master, c.ArgCount > 3 ? (long)JsConvert.ToNumber(c.Arg(3)) : -1);
        }));
        // 转职走真实 job id：源端脚本 1022000.js 等是 changeJobById(100) 这种硬编码 id，
        // 旧实现把它映射回 6 个玩具职业字符串，四转（112/212…）会被压成「战士」。
        cm.Set("changeJobById", Fn(c => ChangeJob(Int(c, 0))));
        cm.Set("getJobName", Ret(c => S(SkillBook.JobName(Int(c, 0)))));
        cm.Set("setHair", Fn(c => SetNativeAppearance("hair",Int(c,0))));
        cm.Set("setFace", Fn(c => SetNativeAppearance("face",Int(c,0))));
        cm.Set("setSkin", Fn(c => SetNativeAppearance("skin",Int(c,0))));
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
        cm.Set("showIntro", Fn(c => NativeMapIntro.Show(Str(c,0))));
        cm.Set("showVideo", Fn(c => { }));
        cm.Set("playSound", Fn(c => AudioManager.Play(Str(c, 0), pitchVary: false)));
        cm.Set("guideHint", Fn(c => { }));
        cm.Set("showQuestCompleteEffect", Fn(c => QuestNotice.ShowCompleteEffect()));
        cm.Set("showItemGain", Fn(c => { }));
        cm.Set("openNpc", Fn(c => session.RequestOpenNpc(Int(c, 0), c.ArgCount > 1 ? Str(c, 1, null) : null)));
        cm.Set("openShopNPC", Fn(c => NativeNpcShopWindow.OpenShop(Int(c, 0), true)));
        cm.Set("getPlayerCount", Ret(_ => N(1)));
        cm.Set("isLeader", Ret(_ => B(IsPartyLeader())));
        cm.Set("isPartyLeader", Ret(_ => B(IsPartyLeader())));
        cm.Set("isEventLeader", Ret(_ => B(NativeEventScripts.IsEventLeader(GameSession.CharacterId))));
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
        cm.Set("getEventManager", Ret(c => NativeEventScripts.Manager(Str(c,0))));
        cm.Set("getEventInstance", Ret(_ => NativeEventScripts.Instance()));
        cm.Set("getParty", Ret(_ => BuildParty(session)));
        // AbstractPlayerInteraction.java:1304 —— 当前频道的远征队（同类型只有一支）。
        cm.Set("getExpedition", Ret(c => BuildExpedition(
            ExpeditionRegistry.GetExpedition(ExpeditionTypeArgument(c.Arg(0))), session)));
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

    // 写角色扩展值（对应源端 CharacterExtendStore；ExtendType.CHARACTER_EXTEND 那一路）。
    static void WriteCharacterValue(string key, string value)
    {
        if (!GameSession.InGame) throw new JsRuntimeException("Script values require an active character");
        GameSession.Net.ScriptValue(new ScriptValueRequest {
            characterId = GameSession.CharacterId, write = true, name = key, value = value });
    }

    static JsValue ExtendValue(JsCall call, bool account, bool write)
    {
        if (!GameSession.InGame) throw new JsRuntimeException("Script values require an active character");
        int index = write ? 2 : 1;
        int period = call.ArgCount > index ? (JsConvert.ToBoolean(call.Arg(index)) ? 1 : 2) : 0;
        var response = GameSession.Net.ScriptValue(new ScriptValueRequest { characterId = GameSession.CharacterId,
            account = account, write = write, period = period, name = Str(call, 0), value = write ? Str(call, 1) : null });
        if (write && !account && period == 0 && Str(call, 0) == ExplorerAdvancedSkills.CompletionKey)
            ExplorerAdvancedSkills.Invalidate();
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
        if (type < 1 || type > 5 || GameSession.Character == null) return;
        var items = new List<InventoryEntry>(GameSession.Character.inventory);
        foreach (var item in items)
            if (item.inventoryType == type && item.position > 0) RemoveInventorySlot(type, item.position);
    }

    static void RemoveInventorySlot(int type, int slot)
    {
        var item = SlotEntry(type, slot);
        if (item == null) return;
        GameSession.SaveToDb();
        if (!RemoveSlotItem(type, slot, item.quantity)) throw new JsRuntimeException("删除道具失败");
        GameSession.ApplyInventoryToProfile(); Profile.Save();
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
        // —— NPC 动态增删（AbstractPlayerInteraction.removeNpc/spawnNpc + MapleMap.destroyNPC）——
        // cm.removeNPC(579711) 是单参调用（1096005.js；源端 Java 只有 removeNpc(mapid,npcid) 两参，
        // 按 destroyNPC「同图至多一个同名 NPC」的语义落到当前图）；cm.removeNpc(270050100,2141000) 两参。
        cm.Set("removeNpc", Ret(c => { var t = RemoveScriptTarget(c); return RemoveScriptNpc(t.mapId, t.npcId); }));
        cm.Set("removeNPC", cm.Get("removeNpc"));
        cm.Set("spawnNpc", Fn(c => SpawnScriptNpc(c)));
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
            int level = Profile.level; long need = ExpTable.Needed(level);
            if (need < rewardExp)
            {
                if (level > 50 && level <= 70) rewardExp = (long)(need * 0.8);
                else if (level > 70 && level <= 100) rewardExp = (long)(need * 0.5);
                else if (level > 100 && level <= 124) rewardExp = (long)(need * 0.3);
                else rewardExp = need;
            }
            Profile.GrantExperience(rewardExp);
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
        cm.Set("openEnhancement", Fn(c => NativeEnhancementWindow.Open(Int(c,0))));
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
        cm.Set("getConnection", Ret(_ => throw new JsRuntimeException("Unmigrated server API: cm.getConnection")));
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
        // —— Link 系统（LinkSystemService.java；link_system.js）——
        // 语义完整实现见 NativeLinkService；加成生效（伤害乘区/HPMP/经验）的接入点：
        // 源端在 MapleMap.java:1523（伤害）、Character.updateLocalStats（HP/MP/四维）、
        // Character.java:4923（经验）三处消费 loadBonus。单机同一账号只有真人 + 假人
        // 存档角色，未达 150 级的 link 不生效（StagePercent=0），真实玩家将假人角色练到
        // 150 后，这里返回的加成数据就是源端数值；战斗链路的倍率接入在 NativeBuffs/
        // 经验结算处消费 NativeLinkService.LoadBonus（另批接线，先保证脚本面与存档真）。
        cm.Set("linkSystemOverview", Ret(_ => S(NativeLinkService.Overview(GameServer.Local.Db,
            GameSession.CharacterId))));
        cm.Set("linkSystemAdd", Ret(c => S(NativeLinkService.AddLink(GameServer.Local.Db,
            GameSession.CharacterId, Int(c, 0)))));
        // —— 师徒系统（MentorshipService.java；3003107.js 的 16 个入口）——
        // 全部走真实存档（mentorship_* 表）；单机里师徒关系的对象是假人角色存档，
        // 校验链（师傅≥200/徒弟未转生/同家族）与源端一致，不满足就按源端文案拒绝。
        cm.Set("mentorshipOverview", Ret(_ => S(NativeMentorshipService.Overview(GameServer.Local.Db, GameSession.CharacterId))));
        cm.Set("mentorshipCreate", Ret(c => S(NativeMentorshipService.CreateRelation(GameServer.Local.Db, GameSession.CharacterId, Str(c, 0)))));
        cm.Set("mentorshipPartyMasterMenu", Ret(_ => S(NativeMentorshipService.PartyMasterMenu(GameServer.Local.Db, GameSession.CharacterId, PartyMemberIds()))));
        cm.Set("mentorshipHasPartyMasterCandidate", Ret(_ => B(NativeMentorshipService.HasPartyMasterCandidate(GameServer.Local.Db, PartyMemberIds()))));
        cm.Set("mentorshipCreateFromParty", Ret(c => S(NativeMentorshipService.CreateFromParty(GameServer.Local.Db, GameSession.CharacterId, Int(c, 0), PartyMemberIds()))));
        cm.Set("mentorshipCancel", Ret(_ => S(NativeMentorshipService.Cancel(GameServer.Local.Db, GameSession.CharacterId))));
        cm.Set("mentorshipClaimStages", Ret(_ => S(NativeMentorshipService.ClaimStages(GameServer.Local.Db, GameSession.CharacterId))));
        cm.Set("mentorshipClaimWeekly", Ret(_ => S(NativeMentorshipService.ClaimWeekly(GameServer.Local.Db, GameSession.CharacterId))));
        cm.Set("mentorshipStartDuel", Ret(c => S(NativeMentorshipService.StartDuel(GameServer.Local.Db, GameSession.CharacterId, (long)JsConvert.ToNumber(c.Arg(0))))));
        cm.Set("mentorshipJoinDuel", Ret(c => S(NativeMentorshipService.JoinDuel(GameServer.Local.Db, GameSession.CharacterId, (long)JsConvert.ToNumber(c.Arg(0))))));
        cm.Set("mentorshipDuelStatus", Ret(_ => S(NativeMentorshipService.DuelStatus(GameServer.Local.Db, GameSession.CharacterId))));
        cm.Set("mentorshipRanking", Ret(c => S(NativeMentorshipService.Ranking(GameServer.Local.Db, GameSession.CharacterId, Str(c, 0)))));
        cm.Set("mentorshipVirtueCoins", Ret(_ => N(NativeMentorshipService.CoinsOf(GameServer.Local.Db, GameSession.CharacterId, true))));
        cm.Set("mentorshipApprenticeCoins", Ret(_ => N(NativeMentorshipService.CoinsOf(GameServer.Local.Db, GameSession.CharacterId, false))));
        cm.Set("mentorshipSpendVirtue", Ret(c => B(NativeMentorshipService.SpendCoins(GameServer.Local.Db, GameSession.CharacterId, true, Int(c, 0)))));
        cm.Set("mentorshipSpendApprentice", Ret(c => B(NativeMentorshipService.SpendCoins(GameServer.Local.Db, GameSession.CharacterId, false, Int(c, 0)))));
        cm.Set("getAvailableMasteryBooks", Ret(_ => new JsArray()));
        cm.Set("getAvailableSkillBooks", Ret(_ => new JsArray()));
        cm.Set("getEquipInSlot", Ret(c =>
        {
            int id = Profile.EquipOfSlot(Int(c, 0));
            return id > 0 ? BuildEquipItem(id) : JsNull.Value;
        }));
        cm.Set("eatEquip", Fn(c => { }));   // 源端无此 API（活动 NPC 自定义），无法还原语义
        cm.Set("removeAllByInventorySlot", Fn(c => RemoveInventorySlot(Int(c, 0), Int(c, 1))));

        // —— 未迁移整套系统：空态 / 显式提示，不再走 Lenient 中断对话 ——
        cm.Set("getGuild", Ret(_ => JsNull.Value));          // 公会（NativeCommunityService 有 Guild 操作，但脚本面缺成员查询）
        cm.Set("isGuildLeader", Ret(_ => B(false)));
        cm.Set("displayGuildRanks", Fn(c => { }));
        cm.Set("createAlliance", Ret(_ => NotAvailable("联盟")));
        cm.Set("disbandAlliance", Ret(_ => NotAvailable("联盟")));
        cm.Set("upgradeAlliance", Ret(_ => NotAvailable("联盟")));
        cm.Set("getAllianceCapacity", Ret(_ => N(0)));
        cm.Set("canBeUsedAllianceName", Ret(_ => B(false)));
        // AbstractPlayerInteraction.java:1279-1320，远征队。
        cm.Set("createExpedition", Ret(c => N(CreateExpedition(c, session))));
        cm.Set("endExpedition", Fn(c =>
        {
            var exped = (c.Arg(0) as JsHostObject)?.Host as Expedition;
            if (exped == null) { NotAvailable("远征队"); return; }
            exped.Dispose(true);
            ExpeditionRegistry.RemoveExpedition(exped);
        }));
        cm.Set("isLeaderExpedition", Ret(c =>
        {
            var exped = ExpeditionRegistry.GetExpedition(ExpeditionTypeArgument(c.Arg(0)));
            return B(exped != null && exped.IsLeader(GameSession.CharacterId));
        }));
        cm.Set("getExpeditionMemberNames", Ret(c =>
        {
            var exped = ExpeditionRegistry.GetExpedition(ExpeditionTypeArgument(c.Arg(0)));
            if (exped == null) throw new JsRuntimeException("Missing expedition: " + Str(c, 0));
            var text = new System.Text.StringBuilder();
            foreach (var name in exped.GetMembers().Values) text.Append(name).Append(", ");
            return S(text.ToString());
        }));
        cm.Set("fieldLobbied", Ret(_ => B(false)));
        cm.Set("fieldLobbied2", Ret(_ => B(false)));
        cm.Set("fieldTaken", Ret(_ => B(false)));
        cm.Set("fieldTaken2", Ret(_ => B(false)));
        cm.Set("cpqLobby", Ret(c => { NativeCarnivalLobby.Open(Int(c, 0)); return JsNull.Value; }));
        cm.Set("cpqLobby2", Ret(_ => JsNull.Value));
        cm.Set("challengeParty", Ret(_ => NotAvailable("怪物嘉年华")));
        cm.Set("challengeParty2", Ret(_ => NotAvailable("怪物嘉年华")));
        // sendCPQMapLists：源端 native 打开房间列表、选择走独立 CPQ 包（不回脚本 selection）。
        // 单机只有一个空闲 field 组 → 直接开简易选择窗（NativeCarnivalLobby）。
        cm.Set("sendCPQMapLists", Ret(_ => { NativeCarnivalLobby.Open(); return B(true); }));
        cm.Set("sendCPQMapLists2", Ret(_ => { NativeCarnivalLobby.Open(); return B(true); }));
        cm.Set("answerCPQChallenge", Fn(c => NotAvailable("怪物嘉年华")));
        cm.Set("cancelCPQLobby", Fn(c => { }));
        cm.Set("isCPQLoserMap", Ret(_ => B(Profile.LastMapId == 980031400 || Profile.LastMapId == 980032400 || Profile.LastMapId == 980033400 ||
            (Profile.LastMapId >= 980000104 && Profile.LastMapId <= 980000604 && (Profile.LastMapId - 980000104) % 100 == 0))));
        cm.Set("isCPQWinnerMap", Ret(_ => B(Profile.LastMapId == 980031300 || Profile.LastMapId == 980032300 || Profile.LastMapId == 980033300 ||
            (Profile.LastMapId >= 980000103 && Profile.LastMapId <= 980000603 && (Profile.LastMapId - 980000103) % 100 == 0))));
        // 源端 Character.festivalPoints（MonsterCarnival.finish 里 gainFestivalPoints 写入）
        cm.Set("getFestivalPoints", Ret(_ => N(NativeCarnivalService.FestivalPoints)));
        cm.Set("partyMembersInMap", Ret(_ => N(HasParty() ? 1 : 0)));
        // Character.getUnclaimedMarriageGifts()：婚礼礼物登记簿，本端没有未领礼物（婚礼系统未迁），
        // 空列表是真实状态；9201014 用 marriageGifts.isEmpty() 分流，必须是 List 而不是数字。
        cm.Set("getUnclaimedMarriageGifts", Ret(_ => BuildJavaList(new JsArray())));
        cm.Set("createMarriageWishlist", Ret(_ => NotAvailable("婚礼")));
        cm.Set("sendMarriageGifts", Ret(_ => NotAvailable("婚礼")));
        cm.Set("sendMarriageWishlist", Ret(_ => NotAvailable("婚礼")));
        cm.Set("getOngoingWedding", Ret(_ => JsNull.Value));
        cm.Set("getWeddingReservationTimeLeft", Ret(_ => N(0)));
        cm.Set("hasMerchant", Ret(_ => B(false)));
        cm.Set("hasMerchantItems", Ret(_ => B(false)));
        cm.Set("showFredrick", Fn(c => { }));
        cm.Set("resetDojoEnergy", Fn(c => dojoEnergy = 0));
        // 单机没有别的队友可同步，与源端「把全队能量清零」在唯一真人身上等价。
        cm.Set("resetPartyDojoEnergy", Fn(c => dojoEnergy = 0));
        cm.Set("goDojoUp", Fn(c => { }));    // 武陵道场爬塔未迁移
        cm.Set("startAriantBattle", Ret(_ => NotAvailable("阿里安特竞技场")));
        cm.Set("createPyramid", Ret(_ => NotAvailable("金字塔")));
        cm.Set("gainTameness", Fn(c => {
            var response=GameSession.Net.ChangeInventory(new InventoryChangeRequest {characterId=GameSession.CharacterId,
                itemId=5000000,petTameness=Int(c,0)});
            if(response.inventory!=null) {GameSession.Character.inventory=response.inventory;GameSession.ApplyInventoryToProfile();Profile.Save();}
        }));
        cm.Set("getDriedPets", Ret(_ => {
            var list=new JsArray();
            long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if(GameSession.Character!=null) foreach(var pet in GameSession.Character.inventory)
                if(pet.itemId/1000==5000 && pet.position>0 && pet.expiration<now && pet.petData!=null)
                {
                    var view=new JsHostObject(pet);
                    view.Set("getName",Ret(args=>S(pet.petData.TryGetValue("name",out var name) ? name.ToString() : WzNames.Item(pet.itemId))));
                    view.Set("getLevel",Ret(args=>N(Convert.ToInt32(pet.petData["level"]))));
                    view.Set("getTameness",Ret(args=>N(Convert.ToInt32(pet.petData["closeness"]))));
                    view.Set("getPosition",Ret(args=>N(pet.position)));
                    list.Items.Add(view);
                }
            return BuildJavaList(list);
        }));
        cm.Set("getPet", Ret(c => PetView(Int(c,0))));
        cm.Set("evolvePet", Ret(c => {
            var response=GameSession.Net.ChangeInventory(new InventoryChangeRequest {characterId=GameSession.CharacterId,
                itemId=Int(c,1),evolvePetId=Int(c,1),petSlot=Int(c,0)});
            if(!response.ok) {BottomBar.Toast(response.error);return JsNull.Value;}
            GameSession.Character.inventory=response.inventory;GameSession.ApplyInventoryToProfile();Profile.Save();
            // The source method's evolved return variable remains null.
            return JsNull.Value;
        }));
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
        // List.toArray()：事件脚本的 getEligibleParty 是
        //   var partyList = party.toArray(); for (...) var ch = partyList[i];
        // （KerningPQ.js:100、LudiPQ.js / EllinPQ.js / PiratePQ.js 同写法）。底层本来就是
        // JS 数组，直接原样交出去；漏了这个方法会让四个组队任务一开局就抛
        // "Unmigrated server API: list.toArray"。
        list.Set("toArray", Ret(_ => items));
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

    // —— 远征队（Expedition.java / ExpeditionType.java / Channel.java:411-437）——
    // 脚本用法见 scripts/BeiDouSpecial/露希妲远征.js：
    //   Java.type('org.gms.server.expeditions.ExpeditionType').LUCID 是类型令牌，
    //   cm.getExpedition(token) / cm.createExpedition(token) / cm.endExpedition(exped)。
    // 规则（招募倒计时、人数上下限、黑名单、队长转移）都在 Java 侧对象里，C# 只做转发。
    static ExpeditionType ExpeditionTypeArgument(JsValue value)
    {
        if (value is JsHostObject host)
        {
            if (host.Host is ExpeditionType type) return type;
            if (host.Host is string hostName && ExpeditionTypes.TryParse(hostName, out var parsedHost)) return parsedHost;
        }
        string name = value is JsString text ? text.Value : JsConvert.ToString(value);
        if (!string.IsNullOrEmpty(name) && ExpeditionTypes.TryParse(name, out var parsed)) return parsed;
        throw new JsRuntimeException("Missing expedition type: " + name);
    }

    static JsValue CallMember(JsCall call, JsValue target, string name)
        => call.Interp.Call(
            target is JsHostObject host && host.Members.Has(name) ? host.Get(name) : call.Interp.GetMember(target, name),
            target, new JsValue[0]);

    static int ScriptPlayerId(JsCall call, JsValue value)
        => value is JsNumber number ? (int)number.Value : JsConvert.ToInt32(CallMember(call, value, "getId"));

    static string ScriptPlayerName(JsCall call, JsValue value)
        => JsConvert.ToString(CallMember(call, value, "getName"));

    // AbstractPlayerInteraction.java:1283-1297。
    static int CreateExpedition(JsCall call, NpcSession session)
    {
        var type = ExpeditionTypeArgument(call.Arg(0));
        bool silent = call.ArgCount > 1 && JsConvert.ToBoolean(call.Arg(1));
        int minPlayers = call.ArgCount > 2 ? JsConvert.ToInt32(call.Arg(2)) : 0;
        int maxPlayers = call.ArgCount > 3 ? JsConvert.ToInt32(call.Arg(3)) : 0;
        int id = GameSession.InGame ? GameSession.CharacterId : 1;
        var exped = new Expedition(id, Profile.playerName, MapId, type, silent, minPlayers, maxPlayers);
        if (!ExpeditionBossLog.AttemptBoss(id, type, false)) return 1;
        return ExpeditionRegistry.AddExpedition(exped) ? 0 : -1;
    }

    static JsValue BuildExpedition(Expedition exped, NpcSession session)
    {
        if (exped == null) return JsNull.Value;
        var host = new JsHostObject(exped);
        Lenient(host, "expedition");
        // Expedition.java:475-489：getLeader() 返回真实 Character。单机只有本地角色是真实在线的，
        // 远端成员（不会出现）退化成只有 id + 名字的只读替身。
        JsValue MemberOf(int id, string name)
        {
            if (id == GameSession.CharacterId) return BuildPlayer(session);
            var actor = new JsHostObject(id);
            actor.Set("getId", Ret(_ => N(id)));
            actor.Set("getName", Ret(_ => S(name)));
            return actor;
        }
        host.Set("isLeader", Ret(c => B(exped.IsLeader(ScriptPlayerId(c, c.Arg(0))))));
        host.Set("isRegistering", Ret(_ => B(exped.Registering)));
        host.Set("isInProgress", Ret(_ => B(exped.IsInProgress())));
        host.Set("contains", Ret(c => B(exped.Contains(ScriptPlayerId(c, c.Arg(0))))));
        host.Set("getLeader", Ret(_ => MemberOf(exped.LeaderId, exped.LeaderName)));
        host.Set("getName", Ret(_ => S(exped.LeaderName)));
        host.Set("getType", Ret(_ => S(exped.Type.ToString())));
        host.Set("getMinSize", Ret(_ => N(exped.MinSize)));
        host.Set("getMaxSize", Ret(_ => N(exped.MaxSize)));
        host.Set("getRecruitingMapId", Ret(_ => N(exped.StartMapId)));
        host.Set("getStartTime", Ret(_ => N(exped.StartTime)));
        host.Set("getBossLogs", Ret(_ =>
        {
            var logs = new JsArray();
            foreach (string line in exped.BossLogs) logs.Items.Add(S(line));
            return BuildJavaList(logs);
        }));
        host.Set("addMember", Ret(c => S(exped.AddMember(
            ScriptPlayerId(c, c.Arg(0)), ScriptPlayerName(c, c.Arg(0))))));
        host.Set("addMemberInt", Ret(c => N(exped.AddMemberInt(
            ScriptPlayerId(c, c.Arg(0)), ScriptPlayerName(c, c.Arg(0))))));
        host.Set("removeMember", Ret(c => B(exped.RemoveMember(ScriptPlayerId(c, c.Arg(0))))));
        // Expedition.java:310：ban 收的是 getMemberList() 里的 Map.Entry。
        host.Set("ban", Ret(c =>
        {
            exped.Ban(JsConvert.ToInt32(CallMember(c, c.Arg(0), "getKey")));
            return JsNull.Value;
        }));
        // Expedition.java:392-409：队长排第一，元素是 Map.Entry<Integer,String>。
        host.Set("getMemberList", Ret(_ =>
        {
            var entries = new JsArray();
            foreach (var pair in exped.GetMemberList())
            {
                int key = pair.Key;
                string value = pair.Value;
                var entry = new JsHostObject(pair);
                entry.Set("getKey", Ret(_ => N(key)));
                entry.Set("getValue", Ret(_ => S(value)));
                entries.Items.Add(entry);
            }
            return BuildJavaList(entries);
        }));
        host.Set("getMembers", Ret(_ =>
        {
            var snapshot = exped.GetMembers();
            var map = new JsHostObject(null);
            Lenient(map, "expedition.members");
            map.Set("size", Ret(_ => N(snapshot.Count)));
            map.Set("isEmpty", Ret(_ => B(snapshot.Count == 0)));
            map.Set("get", Ret(c => snapshot.TryGetValue(Int(c, 0), out var name) ? (JsValue)S(name) : JsNull.Value));
            map.Set("values", Ret(_ =>
            {
                var values = new JsArray();
                foreach (var name in snapshot.Values) values.Items.Add(S(name));
                return BuildJavaList(values);
            }));
            map.Set("keySet", Ret(_ =>
            {
                var keys = new JsArray();
                foreach (int id in snapshot.Keys) keys.Items.Add(N(id));
                return BuildJavaList(keys);
            }));
            return map;
        }));
        host.Set("getProperty", Ret(c => exped.GetProperty(Str(c, 0)) is string text ? (JsValue)S(text) : JsNull.Value));
        host.Set("setProperty", Fn(c => exped.SetProperty(Str(c, 0), Str(c, 1))));
        host.Set("isExpeditionTeamTogether", Ret(_ => B(exped.IsExpeditionTeamTogether())));
        // Expedition.java:431-465：单机只有自己，warp 等价于自己换图。
        host.Set("warpExpeditionTeam", Fn(c =>
        {
            if (GameSession.InGame) (session ?? NpcSession.MapContext()).RequestWarp(Int(c, c.ArgCount - 1));
        }));
        return host;
    }

    static JsHostObject BuildEquipItem(int itemId)    {
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

    // 目的地图是否存在：FieldCatalog.Maps 是从 WZ 索引里全量登记的（含北斗自定义的 0~4 号图），
    // 只查表、不解析——GetMap 会把整张图的美术解出来，对一次存在性判断太重。
    internal static bool MapExists(int mapId)
    {
        if (mapId < 0) return false;
        FieldCatalog.Ensure();
        return FieldCatalog.Maps.ContainsKey(mapId);
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
            actor.Set("isLeader",Ret(_=>B(member.id==state.partyLeader)));
            // PartyCharacter.getPlayer()：远端成员在本端没有真实 Character，按源端「不在线/取不到角色」返回 null。
            actor.Set("getPlayer",Ret(_=>JsNull.Value));
            return actor;
        }
        // PartyCharacter（PartyCharacter.java）：getPartyMembersOnline 的元素，脚本用
        // getPlayer()/isLeader()/getMapId()/getLevel() 判断谁能进 PQ（9390220 航海、9020000 等）。
        JsValue PartyCharacter(SocialMember member)
        {
            if (member==null) return JsNull.Value;
            var pc=new JsHostObject(null);
            pc.Set("getId",Ret(_=>N(member.id))); pc.Set("getName",Ret(_=>S(member.name)));
            pc.Set("getLevel",Ret(_=>N(member.level))); pc.Set("getJobId",Ret(_=>N(member.job)));
            pc.Set("getMapId",Ret(_=>N(member.mapId))); pc.Set("getChannel",Ret(_=>N(member.channel)));
            pc.Set("isLeader",Ret(_=>B(member.id==state.partyLeader)));
            // 本地队伍服务的成员表就是在线成员（离线成员不在 Members 结果里）。
            pc.Set("isOnline",Ret(_=>B(true)));
            pc.Set("getPlayer",Ret(_=>member.id==GameSession.CharacterId ? BuildPlayer(session) : JsNull.Value));
            return pc;
        }
        JsValue PartyCharacters()
        {
            var list=new JsArray();
            foreach (var member in state.partyMembers) list.Items.Add(PartyCharacter(member));
            return BuildJavaList(list);
        }
        party.Set("getId",Ret(_=>N(state.partyId)));
        party.Set("getLeaderId",Ret(_=>N(state.partyLeader)));
        party.Set("getLeader",Ret(_=>Member(state.partyMembers.Find(m=>m.id==state.partyLeader))));
        party.Set("getMembers",Ret(_=> { var members=new JsArray(); foreach (var member in state.partyMembers) members.Items.Add(Member(member)); return members; }));
        party.Set("getMemberById",Ret(c=>Member(state.partyMembers.Find(m=>m.id==Int(c,0)))));
        party.Set("getPartyMembers",Ret(_=>PartyCharacters()));
        party.Set("getPartyMembersOnline",Ret(_=>PartyCharacters()));
        return party;
    }

    // Character.getStorage() + Storage.sendStorage（源端 Storage.java:232）。
    // 仓库系统未迁移，但源端的 15 级门槛要照搬：不到 15 级是「15级以后才可以使用仓库服务」，
    // 到了 15 级才轮到「尚未接入」。这样玩家看到的是明确的中文提示，而不是一句「对话脚本出错」。
    static JsHostObject BuildStorage()
    {
        var storage = new JsHostObject(null);
        // sendStorage(client, npcId)：源端所有仓库 NPC 脚本的唯一真实入口（Storage.sendStorage）。
        // 15 级门槛在 NativeStorageService.Handle 里也会拦一次，这里提前拦免得先弹了 NPC 对话。
        storage.Set("sendStorage", Fn(c =>
        {
            if (Profile.level < 15) { BottomBar.Toast("15级以后才可以使用仓库服务"); return; }
            NativeStorageWindow.Open(Int(c, 1));
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

    // ——— 转职考验的资格标记（Character.partyquestitems 的等价物）———
    // 生产环境落 characters.dataString（';' 分隔）；编辑器回归没有 GameSession.Character，
    // 落到进程内存，三转链路才能在 batchmode 里跑完整。
    static readonly HashSet<string> headlessPqItems = new HashSet<string>();

    static bool HasPqFlag(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        var character = GameSession.Character;
        if (character == null) return headlessPqItems.Contains(key);
        return PqList(character.record.dataString).Contains(key);
    }

    static void SetPqFlag(string key, bool on)
    {
        if (string.IsNullOrEmpty(key)) return;
        var character = GameSession.Character;
        if (character == null)
        {
            if (on) headlessPqItems.Add(key); else headlessPqItems.Remove(key);
            return;
        }
        var list = PqList(character.record.dataString);
        list.RemoveAll(x => x == key);
        if (on) list.Add(key);
        character.record.dataString = string.Join(";", list);
        GameSession.SaveToDb();
    }

    static List<string> PqList(string raw)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(raw)) return list;
        foreach (var part in raw.Split(';'))
        {
            var key = part.Trim();
            if (key.Length > 0 && !list.Contains(key)) list.Add(key);
        }
        return list;
    }

    // ——— 道具查询（含转职凭证互斥）———
    // 4031008/4031009/4031010/4031011 是一转教官发的介绍信，进二转考验场时源端必扣；
    // 4031012 是考验通过后拿到的英雄的证明。两者同时存在只可能是后台/GM 跳过了 NPC，
    // 这时把介绍信当已失效，否则二转脚本的 haveItem 守卫会把玩家挡在转职之外。
    static bool StaleLetter(int id)
        => id >= 4031008 && id <= 4031011 && Profile.ItemCount(4031012) > 0;

    static int ItemQuantity(int id) => StaleLetter(id) ? 0 : Profile.ItemCount(id);

    static bool HasItem(int id, int count) => ItemQuantity(id) >= Math.Max(1, count);

    // 装备槽是否穿着指定道具：固定槽 -1..-11（PlayerProfileData.EquipOfSlot）+ 动态配饰槽。
    static bool ItemEquipped(int id)
    {
        if (id <= 0) return false;
        var data = Profile.Data;
        for (int slot = -1; slot >= -11; slot--) if (data.EquipOfSlot(slot) == id) return true;
        if (data.accessorySlots != null)
            foreach (int slot in data.accessorySlots) if (data.EquipOfSlot(slot) == id) return true;
        return false;
    }

    // Character.haveItemWithId（Character.java:5153）：背包 findById 命中，
    // 或 checkEquipped 且装备栏（EQUIPPED）命中。9201004 用 false 找未装备的婚戒、
    // 2091005 用 true 让已装备的腰带也算，两路语义都要保真。
    static bool HasItemWithId(int id, bool checkEquipped) => ItemQuantity(id) > 0 || (checkEquipped && ItemEquipped(id));

    // 武陵道场能量（Character.dojoEnergy，源端不落库的内存态，上限 10000）。
    static int dojoEnergy;

    // Party.hashCode() 的单机等价物：队伍宿主的 getId（lookupPartyDojo(cm.getParty()) 的
    // party 参数是 JS 宿主，源端按 hashCode 记账，这里按 partyId 记账，语义等价）。
    static long PartyIdOf(JsCall c, int index)
    {
        if (!(c.Arg(index) is JsHostObject party)) return 0;
        var value = c.Interp.Call(c.Interp.GetMember(party, "getId"), party, new JsValue[0]);
        return value is JsNumber number ? (long)number.Value : 0;
    }

    // 在线队伍成员 cid 表（源端 getPartyMembersOnline；师徒菜单按它列师傅候选）。
    static List<long> PartyMemberIds()
    {
        var ids = new List<long>();
        if (!GameSession.InGame) return ids;
        var state = GameSession.Net.Social(new SocialRequest { characterId = GameSession.CharacterId });
        if (!state.party) return ids;
        foreach (var member in state.partyMembers) ids.Add(member.id);
        return ids;
    }

    // 转职成功后把过期的介绍信清掉，别在背包里留一封指向已完成考验的废信。
    static void ClearStaleLetters()
    {
        for (int id = 4031008; id <= 4031011; id++)
        {
            int n = Profile.ItemCount(id);
            if (n > 0) Profile.RemoveItem(id, n);
        }
    }

    /// <summary>cm.changeJobById(jobId)：源端不做校验，直接执行 Character.changeJob。</summary>
    static void ChangeJob(int jobId)
    {
        if (jobId <= 0) return;
        if (!JobSystem.ChangeJobById(jobId)) return;
        ClearStaleLetters();
        // 源端 changeJob 只更新 maxHp/maxMp，当前血量保持；别用 ApplyProgression（会顺手回满）。
        // 转生（executeRebornAsId）在本调用之后还会自己 ApplyProgression，等级重置回满，不冲突。
        Pc?.RefreshProgressionCaps();
        BottomBar.Toast("转职成功：" + JobDefs.NameOf(jobId));
        Debug.Log("[Script] changeJobById(" + jobId + ") → " + JobDefs.NameOf(jobId));
    }

    static JsValue PetView(int slot)
    {
        if(!GameSession.InGame) return JsNull.Value;
        var pet=GameSession.Net.ChangeInventory(new InventoryChangeRequest {characterId=GameSession.CharacterId,
            itemId=5000000,getPet=true,petSlot=slot}).pet;
        if(pet==null) return JsNull.Value;
        var view=new JsHostObject(pet);
        view.Set("getItemId",Ret(_=>N(pet.itemId)));
        view.Set("getPosition",Ret(_=>N(pet.position)));
        view.Set("getName",Ret(_=>S(pet.petData["name"].ToString())));
        view.Set("getLevel",Ret(_=>N(Convert.ToInt32(pet.petData["level"]))));
        view.Set("getTameness",Ret(_=>N(Convert.ToInt32(pet.petData["closeness"]))));
        view.Set("getCloseness",Ret(_=>N(Convert.ToInt32(pet.petData["closeness"]))));
        view.Set("getFullness",Ret(_=>N(Convert.ToInt32(pet.petData["fullness"]))));
        return view;
    }
    public static JsHostObject BuildPlayer(NpcSession session)
    {
        var p = new JsHostObject(session);
        Lenient(p, "player");
        p.Set("getPet",Ret(c=>PetView(Int(c,0))));
        p.Set("getHp", Ret(_ => N(CurrentHp)));
        p.Set("getMp", Ret(_ => N(CurrentMp)));
        p.Set("getMaxHp", Ret(_ => N(Pc != null ? Pc.hpMax : MaxHpFallback)));
        p.Set("getMaxMp", Ret(_ => N(Pc != null ? Pc.mpMax : MaxMpFallback)));
        p.Set("getLevel", Ret(_ => N(Profile.level)));
        p.Set("getMaxClassLevel", Ret(_ => GameSession.Net.Script(new ScriptRequest {
            operation = ScriptOperation.GetMaxClassLevel, characterId = GameSession.CharacterId }).value));
        p.Set("getWorld", Ret(_ => N(GameSession.World)));
        p.Set("gainExp", Fn(c => Profile.GrantExperience(Int(c,0))));
        p.Set("gainMeso", Fn(c => Profile.AddGold(Int(c,0))));
        p.Set("getJobId", Ret(_ => N(QuestLogic.PlayerJob)));
        p.Set("getJob", Ret(_ => BuildJob(QuestLogic.PlayerJob)));
        // Character.isCygnus()（Character.java:6118）→ getJobType()==1，
        // getJobType() = job.getId()/1000（:5190），即 job ∈ [1000,1999] 的骑士团。
        // 缺了它 npc/9900008.js 五转女神在骑士团分支上直接抛「undefined is not a function」
        //（冒险家因为 && 短路躲过去，所以只绑冒险家测不出来）。
        p.Set("isCygnus", Ret(_ => B(QuestLogic.PlayerJob / 1000 == 1)));
        // Character.getFishLevel()（源端 V1.9.9__insert_fishing_items.sql 给 characters 加的 fishLevel 列）
        p.Set("getFishLevel", Ret(_ => N(GameSession.Character?.record.fishLevel ?? 1)));
        p.Set("getHair", Ret(_ => N(GameSession.Character?.record.hair ?? 0)));
        p.Set("getFace", Ret(_ => N(GameSession.Character?.record.face ?? 0)));
        p.Set("getBuddylist", Ret(_ =>
        {
            var list = new JsHostObject(null);
            Lenient(list, "buddylist");
            list.Set("getCapacity", Ret(c => N(GameSession.Character?.record.buddyCapacity ?? 25)));
            return list;
        }));
        p.Set("setBuddyCapacity", Fn(c =>
        {
            if (!GameSession.InGame) throw new JsRuntimeException("Buddy capacity requires an active character");
            int capacity = Int(c, 0);
            var response = GameSession.Net.Social(new SocialRequest { characterId = GameSession.CharacterId,
                operation = SocialOperation.SetBuddyCapacity, buddyCapacity = capacity });
            if (!response.ok) throw new JsRuntimeException(response.error);
            GameSession.Character.record.buddyCapacity = capacity;
        }));
        p.Set("getMonsterBook", Ret(_ =>
        {
            var book = new JsHostObject(null);
            Lenient(book, "monsterBook");
            book.Set("getTotalCards", Ret(c => N(GameSession.InGame
                ? GameSession.Net.Social(new SocialRequest { characterId = GameSession.CharacterId }).cards.Count : 0)));
            return book;
        }));
        // Character.java:712; loaded from characters.partySearch, not a constant.
        p.Set("isRecvPartySearchInviteEnabled", Ret(_ =>
        {
            if (!GameSession.InGame || GameSession.Character == null)
                throw new JsRuntimeException("Party search preference requires an active character");
            return B(GameSession.Character.record.partySearch != 0);
        }));
        // Character.java:700 toggleRecvPartySearchInvite：翻转「接受组队搜索邀请」，
        // 打开时登记进组队搜索、关闭时退出。四个组队任务的招募 NPC 的第二个选项
        // （「我想开启/关闭组队搜索」）直接调它，漏了会弹「该功能尚未接入」。
        p.Set("toggleRecvPartySearchInvite", Ret(_ =>
        {
            if (!GameSession.InGame || GameSession.Character == null)
                throw new JsRuntimeException("Party search preference requires an active character");
            var record = GameSession.Character.record;
            bool enabled = record.partySearch == 0;
            record.partySearch = enabled ? 1 : 0;
            if (GameSession.Net != null)
            {
                // updatePartySearchAvailability：源端单人也允许登记（isAlone 分支）。
                // 本端的组队搜索要求「自己是队长」，登记不上时只影响能不能被搜到，
                // 不影响开关本身，所以这里不把服务端的错误抛回脚本。
                int level = Profile.level;
                GameSession.Net.Social(new SocialRequest
                {
                    characterId = GameSession.CharacterId,
                    operation = enabled ? SocialOperation.SearchParty : SocialOperation.StopSearch,
                    minLevel = Math.Max(1, level - 30), maxLevel = level + 30
                });
            }
            GameSession.SaveToDb();
            return B(enabled);
        }));
        p.Set("gmLevel", Ret(_ => N(GameSession.Character?.record.gm ?? 0)));
        // ——— 转职考验的「资格标记」（OdinMS Character.partyquestitems）———
        // 三转链路的脚本全靠这三个方法：1022000/1012100/1052001/1090000 的「见过教官 / 已领分身任务 /
        // 已交项链」判定，2020008~2020011 的 JBQ。北斗 Java 侧把这套 partyquestitems 整块删了
        // （grep setPartyQuestItemObtained 在 gms-server 里 0 命中），客户端若不实现，脚本第一句
        // cm.getPlayer().setPartyQuestItemObtained(...) 就抛「未迁移接口」，三转对话直接中断。
        // 落盘位置沿用 characters.dataString，用 ';' 分隔，避免 "JB" 命中 "JB3" 这种前缀误判。
        p.Set("gotPartyQuestItem", Ret(c => B(HasPqFlag(Str(c, 0)))));
        p.Set("setPartyQuestItemObtained", Fn(c => SetPqFlag(Str(c, 0), true)));
        p.Set("removePartyQuestItem", Fn(c => SetPqFlag(Str(c, 0), false)));
        // Character.getRemainingSp()：源端 remainingSp[getSkillBook()]，本项目就是可用 SP。
        // 三转 status==2 用它卡「SP 必须花到 (等级-70)*3+10 以内」，缺了会抛异常导致转不了职。
        p.Set("getRemainingSp", Ret(_ => N(Profile.Sp)));

        p.Set("getGuildId", Ret(_ => N(GameSession.Character?.record.guildid ?? 0)));
        // Character.getGuildRank()：characters.guildrank（1=族长..5=成员），2010007 家族中心按它拦权限。
        p.Set("getGuildRank", Ret(_ => N(GameSession.Character?.record.guildrank ?? 5)));
        // Character.getGuild()：2010007 只要 getCapacity()；家族数据走既有 Community 读路径。
        p.Set("getGuild", Ret(_ =>
        {
            var guild = new JsHostObject(null);
            Lenient(guild, "guild");
            guild.Set("getId", Ret(_ => N(GameSession.Character?.record.guildid ?? 0)));
            guild.Set("getCapacity", Ret(_ => N(NativeCommunityWindow.Request(CommunityOperation.Read).capacity)));
            guild.Set("getName", Ret(_ => S(NativeCommunityWindow.Request(CommunityOperation.Read).guildName)));
            return guild;
        }));
        // Character.genericGuildMessage(code)（Character.java:3414）→ 客户端 GUILD_OPERATION 处理：
        // code 1 = 弹「创建家族」名输入框，确认后客户端发 CREATE_GUILD 请求（家族窗口邀请输入
        // 同一 NativeDlgInput 组件）。创建校验（金币/重名/最少人数）全部在服务端 CreateGuild。
        p.Set("genericGuildMessage", Fn(c =>
        {
            int code = Int(c, 0);
            if (code != 1) throw new JsRuntimeException("Unmigrated guild message code: " + code);
            if (!GameSession.InGame) throw new JsRuntimeException("Guild creation requires an active character");
            NativeDlgInput.Open("请输入您想要创建的家族名称。（3～12 个字）", "", 12, value =>
            {
                NativeCommunityWindow.InputName = value;
                NativeCommunityWindow.Request(CommunityOperation.CreateGuild);
            });
        }));
        p.Set("disbandGuild", Fn(_ => B(NativeCommunityWindow.Request(CommunityOperation.DisbandGuild).ok)));
        p.Set("increaseGuildCapacity", Fn(_ => B(NativeCommunityWindow.Request(CommunityOperation.ExpandGuild).ok)));
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
        // Character.getJobStyle()（Character.java:577）= getJobStyleInternal(jobId, str>dex ? 0x80 : 0x40)。
        // 返回的是 Job 枚举常量本身——源端是 enum 引用相等，脚本写的是
        // `stance == Job.WARRIOR`，所以这里必须和 JobEnum.Js(Job.WARRIOR) 是同一个实例。
        // 少了它 quest/2001.js、3414.js、3414_free10rate.js 的「选卷轴」菜单根本出不来。
        p.Set("getJobStyle", Ret(_ => JobEnum.Js(JobEnum.Style(QuestLogic.PlayerJob, Total().str, Total().dex))));
        p.Set("getItemQuantity", Ret(c => N(Profile.ItemCount(Int(c, 0)))));
        p.Set("getItemName", Ret(c => S(WzNames.Item(Int(c, 0)))));
        // 源端这两个是服务器喇叭；单机没有别的玩家，落成底部提示才对得上「玩家看到了什么」。
        p.Set("checkoutBroadcast", Ret(_ => B(false)));
        p.Set("sendFullServerBroadcast", Fn(c => ToastIfAny(Str(c, 0))));
        p.Set("sendBroadcast", Fn(c => ToastIfAny(Int(c, 0) == 0 ? Str(c, 2) : Str(c, 1) + "：" + Str(c, 2))));
        // Character.java:11004 sendAllWordPQNotice：副本通关的全服公告
        // （message_zh_CN.properties: Character.PQCompleted.globalNotice =
        //  "[副本公告] : 恭喜玩家 {0} 完成{1}。"）。四个组队任务的领奖 NPC
        // （9020002 / 2040035 / 2133001 / 2094001）交完奖励都会喊这一句，
        // 漏了会让「领取奖励」那一步整个对话崩掉。单机只有自己在线，等同给自己一条公告。
        p.Set("sendAllWordPQNotice", Fn(c =>
        {
            if (!GameSession.InGame) return;
            ToastIfAny("[副本公告] : 恭喜玩家 " + Profile.playerName + " 完成" + Str(c, 0) + "。");
        }));
        BindSavedLocations(p);
        // Character.useCS（Character.java:472「chaos scroll upon crafting item」）：制作时是否消耗
        // 混乱卷轴的标记。时尚点装等脚本开场 setCS(true) 只是源端模板习惯；本机制作系统未迁移，先存值。
        p.Set("setCS", Fn(c => useCS = JsConvert.ToBoolean(c.Arg(0))));
        // Character.saveLocation(type) / saveLocationOnWarp()：warp 前记住「从哪来」（Character.java:8120/8131）
        p.Set("saveLocation", Fn(c => SaveLocation(Str(c, 0))));
        p.Set("saveLocationOnWarp", Fn(_ => SaveLocationOnWarp()));
        p.Set("startMonsterVac", Ret(_ => B(GameLoop.I?.player != null && NativeMonsterVac.For(GameLoop.I.player).StartVac())));
        p.Set("isMonsterVacActive", Ret(_ => B(GameLoop.I?.player?.GetComponent<NativeMonsterVac>()?.Active == true)));
        p.Set("stopMonsterVac", Fn(_ => GameLoop.I?.player?.GetComponent<NativeMonsterVac>()?.StopVac()));
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
        p.Set("getEventInstance", Ret(_ => NativeEventScripts.Instance()));
        // Character.java:4630 delegates to the client's interaction, whose lifetime
        // extends beyond NPC disposal (event cleanup still removes quest items).
        p.Set("getAbstractPlayerInteraction", Ret(_ =>
        {
            var context=session ?? NpcSession.MapContext();
            return Build(context,context.NpcId,context.QuestId,context.IsQuest);
        }));
        p.Set("haveItem", Ret(c => B(HasItem(Int(c, 0), Int(c, 1, 1)))));
        p.Set("haveItemWithId", Ret(c => B(HasItemWithId(Int(c, 0), c.ArgCount > 1 && JsConvert.ToBoolean(c.Arg(1))))));
        // 在线战力注入：师徒自动出师用（源端 MentorshipService.calculatePower(Character)，
        // 全属性含装备 Totals + 存档 maxhp/maxmp + accounts 现金三桶）。
        NativeMentorshipService.OnlinePowerOfActiveCharacter = () =>
        {
            var record = GameSession.Character?.record;
            if (record == null) return 0;
            var t = Total();
            long stats = t.str + t.dex + t.intelligence + t.luk;
            return stats * Math.Max(1, Math.Max(t.watk, t.matk))
                + record.maxhp / 10L + record.maxmp / 20L + NativeMentorshipService.CashOfActiveAccount() / 100L;
        };
        p.Set("canHold", Ret(c => B(CanHold(new[] {Int(c,0)},new[] {Int(c,1,1)}))));
        p.Set("getInventory", Ret(c => BuildInventory(Int(c, 0))));
        p.Set("hasEmptySlot",Ret(c=>B(FreeInventorySlots(Int(c,0))>0)));
        p.Set("getQuestStatus", Ret(c => N(Profile.QuestStatus(Int(c, 0)))));
        p.Set("updateHp", Fn(c => CurrentHp = Int(c, 0)));
        p.Set("updateMp", Fn(c => CurrentMp = Int(c, 0)));
        p.Set("addHP", Fn(c => CurrentHp = CurrentHp + Int(c, 0)));
        p.Set("addMP", Fn(c => CurrentMp = CurrentMp + Int(c, 0)));
        // Character.updateHpMp(hp, mp)：起源之塔第 8 层火柱命中后把 HP/MP 同时压到 1%，
        // 是「着火但没死」的原脚本语义，两个值必须一起写。
        p.Set("updateHpMp", Fn(c =>
        {
            CurrentHp = Int(c, 0);
            CurrentMp = Int(c, 1);
        }));
        p.Set("dropMessage", Fn(c => ToastIfAny(c.ArgCount > 1 ? Str(c, 1) : Str(c, 0))));
        p.Set("message", Fn(c => ToastIfAny(Str(c, 0))));
        p.Set("changeMap", Fn(c =>
        {
            int id=(c.Arg(0) as JsHostObject)?.Host is NativeEventMap eventMap?eventMap.Id:Int(c,0);
            int portal=-1;
            if ((c.Arg(1) as JsHostObject)?.Host is Server.Field.MapPortal point)
                portal = eventMapPortalIndex(c.Arg(0),point);
            else if ((c.Arg(1) as JsHostObject)?.Host is FieldPortal fieldPortal)
                portal = fieldPortal.id;   // FieldPortal.id = WZ portal 列表下标（Portal.getId）
            else if (c.ArgCount > 1 && !(c.Arg(1) is JsNull || c.Arg(1) is JsUndefined)) portal = Int(c, 1);
            session.RequestWarp(id,null,portal);
        }));
        p.Set("setMap", Fn(c => { if (NpcSession.Active != null) NpcSession.Active.RequestWarp(Int(c, 0)); }));
        p.Set("isAlive", Ret(_ => B(Pc != null ? Pc.hp > 0 : CurrentHp > 0)));
        p.Set("getEvents", Ret(_ => new JsArray()));
        p.Set("getAreaInfos", Ret(_ =>
        {
            // Character.area_info is Map<Short,String>, shared with updateAreaInfo.
            var areas = new JsHostObject(null);
            string Key(JsCall call) => "areaInfo_" + unchecked((short)Int(call, 0));
            JsValue Read(JsCall call)
            {
                string value = ScriptValueStore(Key(call), null);
                return string.IsNullOrEmpty(value) ? (JsValue)JsNull.Value : S(value);
            }
            areas.Set("get", Ret(Read));
            areas.Set("put", Ret(c =>
            {
                var previous = Read(c);
                ScriptValueStore(Key(c), Str(c, 1), write: true);
                return previous;
            }));
            areas.Set("containsKey", Ret(c => B(!(Read(c) is JsNull))));
            return areas;
        }));
        p.Set("getPartnerId", Ret(_ => N(GameSession.Character?.record.partnerId ?? 0)));
        p.Set("getCashShop", Ret(_ => GameSession.InGame ? BuildCashShop() : JsNull.Value));
        // Character.getStorage()（源端 Character.java）。仓库整套（存/取/金币/槽位 UI）未迁移，
        // 但必须返回一个对象：npc/9030100.js 是 `cm.getPlayer().getStorage().sendStorage(...)`，
        // 返回 undefined 会让「点仓库 NPC」直接崩成「对话脚本出错」。
        p.Set("getStorage", Ret(_ => BuildStorage()));
        p.Set("getExtraStorage",Ret(_=>
        {
            var storage=new JsHostObject(null);Lenient(storage,"ExtraStorage");
            storage.Set("getItemQuantity",Ret(c=>
            {
                var response=GameSession.Net.ChangeInventory(new InventoryChangeRequest {
                    characterId=GameSession.CharacterId,itemId=Int(c,0),extraStorageType=Int(c,1),extraStorage=ExtraStorageOperation.Read });
                if(!response.ok)throw new JsRuntimeException(response.error);
                return N(response.extraStorageQuantity);
            }));
            return storage;
        }));
        foreach(var operation in new[]{ExtraStorageOperation.Store,ExtraStorageOperation.Withdraw})
        {
            var action=operation;
            p.Set(action==ExtraStorageOperation.Store?"storeExtraItem":"takeOutExtraItem",Ret(c=>
            {
                var response=GameSession.Net.ChangeInventory(new InventoryChangeRequest {characterId=GameSession.CharacterId,
                    itemId=Int(c,0),quantity=Int(c,1),extraStorageType=Int(c,2),extraStorage=action});
                if(response.ok){GameSession.Character.inventory=response.inventory;GameSession.ApplyInventoryToProfile();Profile.Save();}
                else if(!string.IsNullOrEmpty(response.error))NativeSystemMessage.Drop(5,response.error);
                return B(response.ok);
            }));
        }
        p.Set("getFamilyEntry", Ret(_ => JsNull.Value));
        p.Set("sendPacket", Fn(c => ApplyScriptPacket(c.Arg(0))));
        p.Set("cancelEffect", Fn(c => { }));
        p.Set("cancelAllBuffs", Fn(c => { }));
        // Character.respawn(EventInstanceManager eim, int map)：源端是「先摘出事件实例、再换图」。
        // 露希妲的 playerRevive 在死亡满 50 次时就是靠这一步退本（随后脚本 disposeIfEmpty 收实例），
        // 原来当空实现会让实例一直挂在 manager 上，露希妲只有 1 个 lobby → 之后再也开不了本。
        p.Set("respawn", Fn(c =>
        {
            var target=(c.Arg(0) as JsHostObject)?.Host as NativeEventScriptService.Instance;
            int mapId=JsConvert.ToInt32(c.Arg(1));
            if(target!=null)target.Unregister(p);
            if(mapId>0)
            {
                var field=GameLoop.I!=null?GameLoop.I.field:null;
                if(field!=null)field.WarpTo(mapId,null,-1);
                else Profile.LastMapId=mapId;
            }
        }));
        p.Set("enableActions", Fn(c => { }));
        p.Set("setStance", Fn(c => { }));
        // Character.resetEnteredScript(mapId)：清掉「这张图的 onUserEnter 已经跑过」的标记。
        // 露希妲每张图进本都要重跑，源端在 playerEntry 里逐个事件图清一遍。
        p.Set("resetEnteredScript", Fn(c => { }));
        // AbstractAnimatedMapObject.getIdleMovement()：复活后广播的原地位移包。
        // 单机没有接收方，给一个可传给 PacketCreator.movePlayer 的占位即可。
        p.Set("getIdleMovement", Ret(_ => new JsHostObject(null)));
        p.Set("startMapEffect", Fn(c => { }));
        p.Set("haveItemEquipped", Ret(c => B(ItemEquipped(Int(c, 0)))));
        // Character.isMarried()（Character.java）：partnerId>0。婚礼全套（预约/仪式/礼物）未迁移，
        // 但 9201004/9201014 的信息与离婚面只依赖这几个判定，先按真实存档语义落地。
        p.Set("isMarried", Ret(_ => B((GameSession.Character?.record.partnerId ?? 0) > 0)));
        // Character.getMarriageRing()（Character.java:5212）：partnerId>0 时返回婚戒道具，
        // 没有就是 null。婚戒 id 存在 characters.marriageItemId（getMarriageItemId 同源）。
        p.Set("getMarriageRing", Ret(_ =>
        {
            int stored = GameSession.Character?.record.marriageItemId ?? 0;
            if (stored <= 0 || (GameSession.Character?.record.partnerId ?? 0) <= 0) return JsNull.Value;
            foreach (var item in GameSession.Character.inventory ?? new List<InventoryEntry>())
                if (item.itemId == stored && item.inventoryType == 1)
                {
                    var ring = new JsHostObject(item);
                    Lenient(ring, "item");
                    ring.Set("getItemId", Ret(_ => N(item.itemId)));
                    ring.Set("getPosition", Ret(_ => N(item.position)));
                    // Item.equipped()：装备栏的已穿戴位在本地存档里就是负 position。
                    ring.Set("equipped", Ret(_ => B(item.position < 0)));
                    return ring;
                }
            return JsNull.Value;
        }));
        // Character.getMarriageInstance()：婚礼是 channel 级事件实例，本端未迁婚礼事件系统；
        // 没进婚礼时源端本来就返回 null，这里不会出现「已进婚礼」的状态。
        p.Set("getMarriageInstance", Ret(_ => JsNull.Value));
        // Character.ariantPoints（characters.ariantPoints 列，Character.java:10775 直接累加）。
        p.Set("getAriantPoints", Ret(_ => N(GameSession.Character?.record.ariantPoints ?? 0)));
        p.Set("gainAriantPoints", Fn(c =>
        {
            var record = GameSession.Character?.record ?? throw new JsRuntimeException("Ariant points require an active character");
            record.ariantPoints += Int(c, 0);
            GameSession.SaveToDb();
        }));
        // Character.getAriantColiseum()：进阿里安特竞技场战斗事件时才有 arena 登记对象。
        // 战斗事件（AriantColiseum）未迁移 → 没有进行中的登记，返回 null 是真实空状态
        // （2101015/2101016 的「我不认识你」分支就是源端对非参赛者的原样表现）。
        p.Set("getAriantColiseum", Ret(_ => JsNull.Value));
        // —— 武陵道场存档字段（characters.dojoPoints / lastDojoStage 列；能量是内存态，≤10000） ——
        // Character.addDojoPointsByMap 的累加入口在打怪链路，脚本只读分值与读写 stage。
        p.Set("getDojoPoints", Ret(_ => N(GameSession.Character?.record.dojoPoints ?? 0)));
        p.Set("getDojoStage", Ret(_ => N(GameSession.Character?.record.lastDojoStage ?? 0)));
        p.Set("setDojoStage", Fn(c =>
        {
            var record = GameSession.Character?.record ?? throw new JsRuntimeException("Dojo stage requires an active character");
            record.lastDojoStage = Int(c, 0);
            GameSession.SaveToDb();
        }));
        p.Set("setDojoEnergy", Fn(c => dojoEnergy = System.Math.Min(Int(c, 0), 10000)));
        p.Set("getDojoEnergy", Ret(_ => N(dojoEnergy)));
        // Character.getBuffSource(BuffStat)：返回该状态当前生效的来源 id（技能 id / 道具 id），
        // 没有该 buff 返回 null——与源端一致，2081005 等脚本用它判「是否处于某变身」。
        p.Set("getBuffSource", Ret(c =>
        {
            string stat = (c.Arg(0) as JsHostObject)?.Host as string ?? Str(c, 0);
            var buffs = Pc != null ? Pc.GetComponent<NativeBuffs>() : null;
            if (buffs == null) return JsNull.Value;
            int source = buffs.SourceOf(stat);
            // 道具 buff 在本端以 -itemId 记账（NativeBuffs.ApplyItem），对外仍是正道具 id。
            return source == 0 ? (JsValue)JsNull.Value : N(source < 0 ? -source : source);
        }));
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
    static JsArray RebirthSettings()
        => (JsArray)GameSession.Net.Script(new ScriptRequest {
            operation = ScriptOperation.GetRebirthSettings, characterId = GameSession.CharacterId }).value;

    static int RebornCount() => JsConvert.ToBoolean(RebirthSettings().Items[0])
        ? GameSession.Character?.record.reborns ?? 0 : 0;

    static void Rebirth(JsCall call)
    {
        if (!GameSession.InGame) throw new JsRuntimeException("Rebirth requires an active character");
        // 一键转生.js uses false,false. Other overloads remain explicit migration gaps.
        if (JsConvert.ToBoolean(call.Arg(0)) || JsConvert.ToBoolean(call.Arg(1)))
            throw new JsRuntimeException("Unmigrated rebirth skill-reset flags");
        var settings = RebirthSettings();
        if (!JsConvert.ToBoolean(settings.Items[0])) throw new JsRuntimeException("重生系统未启用");
        int maximum = JsConvert.ToInt32(GameSession.Net.Script(new ScriptRequest {
            operation = ScriptOperation.GetMaxClassLevel, characterId = GameSession.CharacterId }).value);
        int level = Math.Min(maximum, JsConvert.ToInt32(settings.Items[1]));
        bool completed = NativeRebirthRules.IsCygnusFifth(Profile.JobId) ||
            ScriptValueStore("cygnus_fifth_job_completed", null) == "1";
        int next = NativeRebirthRules.JobAtLevel(Profile.JobId, level, Int(call, 2), completed);
        using (Profile.DeferSaves())
        {
            if (!JobSystem.ChangeJobById(next)) throw new JsRuntimeException("Unknown rebirth job: " + next);
            GameSession.Character.record.reborns++;
            LevelSystem.ResetForRebirth(Profile.Data, level);
            Profile.Save();
        }
        Pc?.ApplyProgression();
    }

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
        // 与源端一样使用角色永久字段，不能用每日副本计数存储。
        p.Set("getReborns", Ret(_ => N(RebornCount())));
        p.Set("getRebornsCount", Ret(_ => N(RebornCount())));
        p.Set("rebirth", Fn(c => Rebirth(c)));
        // Character.executeRebornAs：满级校验后 addReborns → changeJob → setLevel(0) → levelUp
        p.Set("executeRebornAsId", Fn(c =>
        {
            int jobId = Int(c, 0);
            if (GameSession.Character == null) throw new JsRuntimeException("Rebirth requires an active character");
            GameSession.Character.record.reborns++;
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
            if (id > 0) SetScriptSkill(id, Int(c, 1, 0), Int(c, 2), c.ArgCount > 3 ? (long)JsConvert.ToNumber(c.Arg(3)) : -1);
        }));
        p.Set("removeBySkillId", Ret(c =>
        {
            int id = Int(c, 0);
            var bindings = KeyMap.ExportCharacter();
            bool removed = bindings.RemoveAll(e => e.type == 1 && e.action == id) > 0;
            if (removed) { KeyMap.LoadCharacter(bindings); GameSession.SaveToDb(); }
            return B(removed);
        }));
        p.Set("addSkillToKeyboard", Fn(c =>
        {
            int key = Int(c, 0), id = Int(c, 1);
            var bindings = KeyMap.ExportCharacter();
            bindings.RemoveAll(e => e.key == key);
            bindings.Add(new KeymapEntry { key = key, type = 1, action = id });
            KeyMap.LoadCharacter(bindings);
            GameSession.SaveToDb();
        }));

        // —— 装备 / 背包同步 ——
        p.Set("gainEquip", Fn(c => Profile.AddItem(Int(c, 0), Math.Max(1, Int(c, 1, 1)))));
        p.Set("equipChanged", Fn(c => Pc?.ApplyProgression()));   // Character.equipChanged：重算 local stats
        p.Set("forceUpdateItem", Fn(c => FlushEquip()));   // 脚本改完内存装备 → 整行回写 DB 并刷新背包
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

    static void SetScriptSkill(int id, int level, int master, long expiration)
    {
        if (id <= 0 || SkillBook.Get(id) == null) return;
        var skills = GameSession.Character?.skills;
        if (skills != null)
        {
            var entry = skills.Find(s => s.skillId == id);
            if (entry == null) { entry = new SkillEntry { skillId = id }; skills.Add(entry); }
            entry.masterLevel = master;
            entry.expiration = expiration;
        }
        SkillProgress.SetLevel(id, Math.Max(0, level));
        GameSession.SaveToDb();
    }

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

    static JsArray CharactersOnScriptMap(int mapId)
    {
        var result = new JsArray();
        if (GameSession.InGame && MapId == mapId) result.Items.Add(BuildPlayer(NpcSession.MapContext()));
        foreach (var actor in Server.Bot.BotRegistry.OnMap(mapId))
        {
            if (!actor.online || actor.world != GameSession.World || actor.channel != GameSession.Channel) continue;
            var player = new JsHostObject(actor);
            Lenient(player, "mapCharacter");
            player.Set("getId", Ret(_ => N(actor.id)));
            player.Set("getName", Ret(_ => S(actor.name)));
            player.Set("getLevel", Ret(_ => N(actor.level)));
            player.Set("getMapId", Ret(_ => N(actor.mapId)));
            result.Items.Add(player);
        }
        return result;
    }

    // 源端 AbstractPlayerInteraction.gainFame（:700）→ Character.gainFame（Character.java:3005）：
    // 直接改 fame（夹 -30000..30000）并发 updateFame，没有「拿不到响应就抛异常」这条路。
    // 编辑器回归（QuestNpcChecks 等）不开 GameSession/ITransport，以前这里 GameSession.Net 为 null 直接 NRE，
    // 于是 quest/2001 的 gainFame 把整段脚本打断在 completeQuest 之前。现在：
    //   有传输层 → 仍然问服务端拿同一份实现（NativeScriptService 里夹取 + fquest 余数）；
    //   没有传输层或没有角色 → 只跳过这条表现，不中断脚本。
    internal static void ChangeFame(ScriptOperation operation,int delta=0)
    {
        var record=GameSession.Character?.record;
        if(record==null) return;
        var request=new ScriptRequest {characterId=GameSession.CharacterId,operation=operation,variableValue=delta};
        var response=GameSession.Net!=null?GameSession.Net.Script(request)
            :GameSession.CharacterId>0&&GameServer.Local!=null?GameServer.Local.Scripts.Handle(request)
            :null;
        if(response?.value is not JsArray value)throw new JsRuntimeException("Missing fame response");
        record.fame=JsConvert.ToInt32(value.GetIndex(0));
        record.fquest=JsConvert.ToInt32(value.GetIndex(1));
    }

    internal static JsValue BuildWarpMap(int id)
    {
        FieldCatalog.Ensure();
        if (!FieldCatalog.Maps.ContainsKey(id)) return JsNull.Value;
        if (id == MapId) return BuildMap();
        var map = new JsHostObject(id);
        Lenient(map, "map." + id);
        map.Set("getId", Ret(_ => N(id)));
        map.Set("getMapId", Ret(_ => N(id)));
        map.Set("getFieldLimit", Ret(_ => N(FieldCatalog.GetMap(id).fieldLimit)));
        map.Set("getMapName", Ret(_ => S(WzNames.Map(id))));
        map.Set("getCharacters", Ret(_ => CharactersOnScriptMap(id)));
        map.Set("getAllCharacters", map.Get("getCharacters"));
        map.Set("getCharactersSize", Ret(_ => N(CharactersOnScriptMap(id).Items.Count)));
        map.Set("countPlayers", map.Get("getCharactersSize"));
        // ——— 野外 BOSS 事件脚本（event/AreaBoss*.js）要的四个方法 ———
        // 源端这些脚本在玩家根本不在目标图的时候也会跑：mapFactory.getMap(目标图).getMonsterById /
        // spawnMonsterOnGroundBelow / broadcastMessage。移植端没有常驻 MapleMap，改成「登记 + 进图生成」。
        map.Set("getMonsterById", Ret(c => FieldDirector.HasWildBoss(id, Int(c, 0))
            ? RemoteMonster(Int(c, 0)) : JsNull.Value));
        map.Set("getMobs", Ret(_ => new JsArray()));
        map.Set("getMonsters", map.Get("getMobs"));
        map.Set("getAllMonsters", map.Get("getMobs"));
        map.Set("countMonsters", Ret(_ => N(0)));
        map.Set("getSpawnedMonstersOnMap", map.Get("countMonsters"));
        map.Set("spawnMonsterOnGroundBelow", Fn(c => SpawnBossInto(id, c)));
        map.Set("spawnMonster", map.Get("spawnMonsterOnGroundBelow"));
        map.Set("getNPCById", Ret(c => ScriptNpcView(id, Int(c, 0))));
        map.Set("getNpcById", map.Get("getNPCById"));
        map.Set("containsNPC", Ret(c => B(MapHasNpc(id, Int(c, 0)))));
        map.Set("addMonsterSpawn", Fn(c =>
        {
            if (!(c.Arg(0) is JsHostObject mob)) throw new JsRuntimeException("addMonsterSpawn requires a monster");
            var position = mob.Get("position") as JsHostObject;
            if (position == null) throw new JsRuntimeException("addMonsterSpawn requires setPosition");
            FieldDirector.SpawnWildBoss(id, JsConvert.ToInt32(mob.Get("id")),
                (float)JsConvert.ToNumber(position.Get("x")), (float)JsConvert.ToNumber(position.Get("y")));
        }));
        // broadcastMessage 只发给「在该图里的人」，玩家不在目标图就等于没人收到（源端语义）。
        map.Set("broadcastMessage", Fn(c =>
        {
            if (id == MapId) ApplyScriptPacket(c.Arg(c.ArgCount - 1));
        }));
        // MapleMap.getPortal(int id)/getPortal(String name)：KerningTrain.js:34 的
        // changeMap(onRide, onRide.getPortal(0)) 把 portal 宿主交回 player.changeMap，
        // 那边按 FieldPortal.id（WZ 下标 = Portal.getId）解析落点。
        map.Set("getPortal", Ret(c =>
        {
            var portals = FieldCatalog.GetMap(id).portals;
            int index = c.Arg(0) is JsString name
                ? portals.FindIndex(p => p.name == JsConvert.ToString(name))
                : Int(c, 0);
            if (index < 0 || index >= portals.Count) return JsNull.Value;
            var portal = portals[index];
            var view = new JsHostObject(portal);
            Lenient(view, "portal");
            view.Set("getId", Ret(_ => N(index)));
            view.Set("getName", Ret(_ => S(portal.name)));
            view.Set("getPosition", Ret(_ => BuildPoint(portal.x, portal.y)));
            return view;
        }));
        return map;
    }

    // ——— NPC 动态增删/查询（源端 AbstractPlayerInteraction.removeNpc/spawnNpc、
    // MapleMap.destroyNPC/getNPCById/containsNPC；同图同名 NPC 至多一个）———
    static (int mapId, int npcId) RemoveScriptTarget(JsCall c)
        => c.ArgCount >= 2 ? (Int(c, 0), Int(c, 1)) : (MapId, Int(c, 0));

    static JsValue RemoveScriptNpc(int mapId, int npcId)
    {
        var runtime = Server.Field.MapService.Get(mapId);
        if (runtime == null) return B(false);
        bool dynamic = runtime.RemoveNpc(npcId);
        if (mapId == MapId)
        {
            var field = GameLoop.I?.field;
            if (field?.Map != null && field.Map.id == mapId)
            {
                field.Map.npcs.RemoveAll(n => n.id == npcId);
                field.RemoveNpcView(npcId);
            }
        }
        return B(dynamic);
    }

    // AbstractPlayerInteraction.spawnNpc(npcId, pos, map)：fh = findBelow(pos)，客户端按 cy 落地。
    // 脚本常传 Point(x, 0) 这类「只给 x」的点，这里吸附到该 x 的脚下平台；传进来的 map 宿主
    // 就是当前图（事件实例图有自己的 eim.spawnNpc 链路，不走这里）。
    // 编辑器回归（batchmode）没有进图链路：与 FieldDirector.SpawnWildBoss 同思路先登记，
    // 真实进图时 MapService.Register 会用带 foothold 的运行时覆盖这份空壳。
    static void SpawnScriptNpc(JsCall c)
    {
        int npcId = Int(c, 0), x = 0, y = 0;
        if (c.Arg(1) is JsHostObject point) { x = JsConvert.ToInt32(point.Get("x")); y = JsConvert.ToInt32(point.Get("y")); }
        else if (c.ArgCount > 2) { x = Int(c, 1); y = Int(c, 2); }
        var runtime = Server.Field.MapService.Get(MapId);
        if (runtime == null)
        {
            runtime = new Server.Field.MapRuntime { mapId = MapId };
            Server.Field.MapService.Register(runtime);
        }
        float ground = y;
        var below = runtime.PointBelow(x, y - 12f) ?? runtime.PointBelow(x, float.NegativeInfinity);
        if (below != null) ground = below.Value;
        runtime.npcs.Add(new Server.Field.MapNpcPosition { id = npcId, x = x, y = Mathf.RoundToInt(ground), dynamic = true });
    }

    // MapleMap.getNPCById：当前图以 FieldMap.npcs（静态 life + 动态实例）为准；
    // 其它图只认已注册的运行时（本局进过图/假人几何），未加载的图没有 NPC 数据。
    static JsValue ScriptNpcView(int mapId, int npcId)
    {
        Server.Field.MapNpcPosition npc = null;
        var field = GameLoop.I?.field;
        if (mapId == MapId && field?.Map != null)
            foreach (var n in field.Map.npcs)
                if (n.id == npcId) { npc = new Server.Field.MapNpcPosition { id = n.id, x = Mathf.RoundToInt(n.x), y = Mathf.RoundToInt(n.y) }; break; }
        if (npc == null)
        {
            var runtime = Server.Field.MapService.Get(mapId);
            if (runtime != null && !runtime.removedNpcs.Contains(npcId)) npc = runtime.GetNpc(npcId);
        }
        if (npc == null) return JsNull.Value;
        var view = new JsHostObject(npc);
        Lenient(view, "npc");
        view.Set("getId", Ret(_ => N(npc.id)));
        view.Set("getObjectId", Ret(_ => N(npc.id)));
        view.Set("getPosition", Ret(_ => BuildPoint(npc.x, npc.y)));
        return view;
    }

    static bool MapHasNpc(int mapId, int npcId)
    {
        var field = GameLoop.I?.field;
        if (mapId == MapId && field?.Map != null) return field.Map.npcs.Exists(n => n.id == npcId);
        var runtime = Server.Field.MapService.Get(mapId);
        return runtime != null && !runtime.removedNpcs.Contains(npcId) && runtime.GetNpc(npcId) != null;
    }

    // 别的图里的 Boss 对脚本只是「存在与否」的判据（AreaBoss 用 getMonsterById != null 决定要不要重刷）。
    static JsValue RemoteMonster(int mobId)
    {
        var monster = new JsHostObject(null);
        Lenient(monster, "monster");
        monster.Set("id", N(mobId));
        monster.Set("getId", Ret(_ => N(mobId)));
        monster.Set("getName", Ret(_ => S(WzNames.Mob(mobId))));
        return monster;
    }

    // map.spawnMonsterOnGroundBelow(mob, point) / (mob, x, y)
    static void SpawnBossInto(int mapId, JsCall c)
    {
        int id = c.Arg(0) is JsHostObject host && host.Get("id") is JsNumber
            ? JsConvert.ToInt32(host.Get("id")) : Int(c, 0);
        int x, y;
        if (c.Arg(1) is JsHostObject point)
        {
            x = JsConvert.ToInt32(point.Get("x")); y = JsConvert.ToInt32(point.Get("y"));
        }
        else { x = Int(c, 1); y = Int(c, 2); }
        FieldDirector.SpawnWildBoss(mapId, id, x, y);
    }

    static int eventMapPortalIndex(JsValue value,Server.Field.MapPortal portal)
        =>(value as JsHostObject)?.Host is NativeEventMap map?map.Geometry.portals.IndexOf(portal):-1;
    static JsHostObject BuildMap()
    {
        var instanceMap=NativeEventScripts.ResolveMap(MapId);
        if(instanceMap!=null)return instanceMap.Host;
        var m = new JsHostObject(null);
        Lenient(m, "map");
        m.Set("broadcastMessage", Fn(c => ApplyScriptPacket(c.Arg(c.ArgCount - 1))));
        m.Set("getId", Ret(_ => N(MapId)));
        m.Set("getMapId", Ret(_ => N(MapId)));
        m.Set("getFieldLimit", Ret(_ => N(FieldCatalog.GetMap(MapId)?.fieldLimit ?? 0)));
        m.Set("getReturnMap", Ret(_ =>
        {
            var f = GameLoop.I != null ? GameLoop.I.field : null;
            return N(f != null && f.Map != null ? f.Map.returnMap : MapId);
        }));
        m.Set("getCharactersSize", Ret(_ => N(CharactersOnScriptMap(MapId).Items.Count)));
        m.Set("countPlayers", m.Get("getCharactersSize"));
        m.Set("getCharacters", Ret(_ => CharactersOnScriptMap(MapId)));
        m.Set("getAllCharacters", m.Get("getCharacters"));
        m.Set("getPortal", Ret(c =>
        {
            var map=FieldCatalog.GetMap(MapId);
            FieldPortal portal=null;
            int index=-1;
            if(map!=null)
            {
                if(c.Arg(0) is JsString) index=map.portals.FindIndex(x=>x.name==Str(c,0));
                else index=Int(c,0,-1);
                if(index>=0 && index<map.portals.Count) portal=map.portals[index];
            }
            if(portal==null) return JsNull.Value;
            var p = new JsHostObject(null);
            p.Set("getPosition", Ret(_ => BuildPoint(portal.x,portal.y)));
            p.Set("getMapId", Ret(_ => N(MapId)));
            p.Set("getName", Ret(_ => S(portal.name)));
            p.Set("getId",Ret(_=>N(index)));
            return p;
        }));
        m.Set("getMobs", Ret(_ => {
            var result = new JsArray();
            foreach (var mob in EnemyUnit.All) if (mob.fieldMode && !mob.dead) result.Items.Add(BuildMonster(mob));
            return BuildJavaList(result);
        }));
        // MapleMap.getMonsters/getAllMonsters expose the same MONSTER objects.
        m.Set("getMonsters", m.Get("getMobs"));
        m.Set("getAllMonsters", m.Get("getMobs"));
        m.Set("countMonster", Ret(c =>
        {
            int min = Int(c, 0), max = c.ArgCount > 1 ? Int(c, 1) : min, count = 0;
            foreach (var mob in EnemyUnit.All) if (mob.fieldMode && !mob.dead && mob.fieldMobId >= min && mob.fieldMobId <= max) count++;
            return N(count);
        }));
        m.Set("isCPQWinnerMap", Ret(_ => B(MapId == 980031300 || MapId == 980032300 || MapId == 980033300 ||
            (MapId >= 980000103 && MapId <= 980000603 && (MapId - 980000103) % 100 == 0))));
        m.Set("isCPQLoserMap", Ret(_ => B(MapId == 980031400 || MapId == 980032400 || MapId == 980033400 ||
            (MapId >= 980000104 && MapId <= 980000604 && (MapId - 980000104) % 100 == 0))));
        m.Set("getMapName", Ret(_ => S(WzNames.Map(MapId))));
        m.Set("countMonsters", Ret(_ => N(CountFieldMonsters())));
        m.Set("getSpawnedMonstersOnMap", Ret(_ => N(CountFieldMonsters())));
        // MapleMap.spawnMonsterOnGroundBelow：怪物是「进这张图就一直在」的，不是临时特效，
        // 所以走 FieldDirector 的野外 Boss 登记（离开再回来、打死冷却都按源端算）。
        m.Set("spawnMonsterOnGroundBelow", Fn(c => SpawnBossInto(MapId, c)));
        m.Set("addMonsterSpawn", Fn(c => {
            if (!(c.Arg(0) is JsHostObject mob)) throw new JsRuntimeException("addMonsterSpawn requires a monster");
            var position=mob.Get("position") as JsHostObject;
            if (position==null) throw new JsRuntimeException("addMonsterSpawn requires setPosition");
            GameLoop.I?.field?.AddScriptMonsterSpawn(JsConvert.ToInt32(mob.Get("id")),
                JsConvert.ToInt32(position.Get("x")),JsConvert.ToInt32(position.Get("y")),Int(c,1),Int(c,2,-1));
        }));
        m.Set("killAllMonsters", Fn(c => GameLoop.I?.field?.ClearMobs()));
        JsValue Reactor(Func<Server.Field.MapReactor, bool> matches)
        {
            foreach (var reactor in Server.Field.MapReactorService.Get(MapId))
            {
                if (!reactor.alive || !matches(reactor)) continue;
                var result = new JsHostObject(reactor);
                Lenient(result, "reactor");
                result.Set("getId", Ret(c => N(reactor.id)));
                result.Set("getObjectId", Ret(c => N(reactor.objectId)));
                result.Set("getName", Ret(c => S(reactor.name)));
                result.Set("getState", Ret(c => N(reactor.state)));
                return result;
            }
            return JsNull.Value;
        }
        m.Set("getReactorByName", Ret(c => Reactor(reactor => reactor.name == Str(c, 0))));
        m.Set("getReactorById", Ret(c => Reactor(reactor => reactor.id == Int(c, 0))));
        m.Set("getReactorByOid", Ret(c => Reactor(reactor => reactor.objectId == Int(c, 0))));
        m.Set("getMonsterById", Ret(c => {
            foreach (var mob in EnemyUnit.All) if (mob.fieldMode && !mob.dead && mob.fieldMobId == Int(c,0)) return BuildMonster(mob);
            return JsNull.Value;
        }));
        m.Set("getNPCById", Ret(c => ScriptNpcView(MapId, Int(c, 0))));
        m.Set("getNpcById", m.Get("getNPCById"));
        m.Set("containsNPC", Ret(c => B(MapHasNpc(MapId, Int(c, 0)))));
        return m;
    }

    sealed class ScriptSound { public string path; public bool music; }
    sealed class ScriptNotice { public int type; public string text; }
    // PacketCreator.showEffect(String)：图上播一个 Map/Effect.img 动画（quest/party/clear、
    // customSkill/lucid/*VideoLayer 等）。
    sealed class ScriptEffect { public string path; }
    // PacketCreator.damagePlayer(...)：主角身上跳伤害数字。参数照源端签名保留，
    // 客户端只用到 damage。
    sealed class ScriptDamage { public long damage; public int mobId; public int objectId; }
    // PacketCreator.movePlayer(chrId, InPacket, len)：复活时的位移广播，单机只是让角色回到落点。
    sealed class ScriptMovePlayer { }
    sealed class ScriptInventoryUpdate { public int mode; public InventoryEntry item; public short oldPosition; }
    sealed class ScriptInventoryPacket { public readonly List<ScriptInventoryUpdate> updates=new List<ScriptInventoryUpdate>(); }
    // PacketCreator.openRPSNPC()：9000019.js 的唯一真实出口（RPS_GAME mode 8 + npcId）→ 开猜拳窗
    sealed class ScriptRpsOpen { public int npcId; }
    // PacketCreator.getClock(seconds)：地图时钟倒计时（KerningTrain/Subway/活动脚本），
    // 复用事件倒计时 UI（NativeEventClock，源端 CLOCK type 2 表现）。
    sealed class ScriptClock { public int seconds; }
    // PacketCreator.earnTitleMessage(msg)：源端 SCRIPT_PROGRESS_MESSAGE + writeString
    // （PacketCreator.java:7389-7393），KerningTrain.js 的「下一站停靠 XX 站」到站播报。
    // 客户端把该 opcode 正文当普通提示文本展示 → 走聊天窗浅蓝（与 type 6 同色）。
    sealed class ScriptProgressMessage { public string text; }
    internal static void ApplyScriptPacket(JsValue packet)
    {
        if (packet is JsHostObject host && host.Host is ScriptSound sound)
        { if (sound.music) AudioManager.PlayBgm(sound.path); else AudioManager.Play(sound.path, pitchVary: false); return; }
        if(packet is JsHostObject noticeHost && noticeHost.Host is ScriptNotice notice)
        { NativeSystemMessage.Drop(notice.type,notice.text); return; }
        if(packet is JsHostObject effectHost && effectHost.Host is ScriptEffect effect)
        { NativeScriptEffect.Play(effect.path); return; }
        if(packet is JsHostObject damageHost && damageHost.Host is ScriptDamage damage)
        {
            var player=Pc;
            if(player!=null)DamageText.Show((Vector3)player.FeetPos+Vector3.up*0.6f,damage.damage,"red");
            return;
        }
        // movePlayer 在单机没有接收方：角色位置已经由 movePlayer(map) 那一步改好了。
        if(packet is JsHostObject moveHost && moveHost.Host is ScriptMovePlayer)return;
        if(packet is JsHostObject rpsHost && rpsHost.Host is ScriptRpsOpen rps)
        { NativeRpsWindow.Open(); return; }
        if(packet is JsHostObject clockHost && clockHost.Host is ScriptClock clock)
        { NativeEventClock.Show(clock.seconds); return; }
        if(packet is JsHostObject progressHost && progressHost.Host is ScriptProgressMessage progress)
        { BottomBar.AddChat(progress.text, BottomBar.ChatLightBlue); return; }
        if(packet is JsHostObject inventoryHost && inventoryHost.Host is ScriptInventoryPacket)
        { FlushEquip(); GameSession.ApplyToProfile(); return; }
        throw new JsRuntimeException("Unmigrated script packet effect");
    }
    // 只问「这个包是什么」，不投递。事件实例地图用它记录图上收到过哪个包（回归用）。
    internal static string DescribeScriptPacket(JsValue packet)
    {
        var host = packet as JsHostObject;
        if (host?.Host is ScriptEffect effect) return "effect:" + effect.path;
        if (host?.Host is ScriptNotice notice) return "notice:" + notice.text;
        if (host?.Host is ScriptDamage damage) return "damage:" + damage.damage;
        return null;
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
            if(name=="org.gms.client.inventory.ModifyInventory") return JsFunction.FromNative("ModifyInventory",args=>
            {
                if(args.ArgCount<2 || args.ArgCount>3 || !((args.Arg(1) as JsHostObject)?.Host is InventoryEntry equip))
                    throw new JsRuntimeException("ModifyInventory requires an inventory item");
                var update=new ScriptInventoryUpdate {mode=Int(args,0),item=equip.Copy(),oldPosition=unchecked((short)Int(args,2))};
                var view=new JsHostObject(update);Lenient(view,"ModifyInventory");
                view.Set("getMode",Ret(_=>N(update.mode)));
                view.Set("getInventoryType",Ret(_=>N(update.item.itemId/1000000)));
                view.Set("getPosition",Ret(_=>N(update.item.position)));
                view.Set("getOldPosition",Ret(_=>N(update.oldPosition)));
                view.Set("getQuantity",Ret(_=>N(update.item.isEquip?1:update.item.quantity)));
                view.Set("getItem",Ret(_=>update.item==null?(JsValue)JsNull.Value:BuildItemView(update.item,false)));
                view.Set("clear",Fn(_=>update.item=null));return view;
            });
            if(name=="java.util.Collections")
            {
                var collection=new JsHostObject(null);Lenient(collection,"Collections");
                collection.Set("unmodifiableList",Ret(args=>
                {
                    if(!(args.Arg(0) is JsArray array))throw new JsRuntimeException("unmodifiableList requires a list");
                    var view=BuildJavaList(array);
                    foreach(string method in new[]{"add","set","remove","clear","addAll","removeAll","retainAll"})
                        view.Set(method,Ret(_=>throw new JsRuntimeException("UnsupportedOperationException: unmodifiable list")));
                    return view;
                }));return collection;
            }
            // openRPSNPC 的绑定并入下方 PacketCreator 完整分支（此前这里提前 return，
            // 使 playSound/musicChange/getClock 等在第二个分支里的绑定全部不可达）。
            if (name == "soloMapling.ArtificialPlayer.BotTypes.Blackjack.BlackjackDealerBot")
            {
                var dealer = new JsHostObject(null);
                Lenient(dealer, "BlackjackDealerBot");
                dealer.Set("joinNearestTable", Ret(args =>
                {
                    var player = args.Arg(0);
                    int id = JsConvert.ToInt32(args.Interp.Call(args.Interp.GetMember(player, "getId"), player, new JsValue[0]));
                    if (!GameSession.InGame || id != GameSession.CharacterId)
                        throw new JsRuntimeException("Blackjack player is not the active character");
                    var actor = GameServer.Local?.ActiveBotPlayer(id);
                    if (actor == null || !actor.online || actor.mapId != MapId)
                        throw new JsRuntimeException("Blackjack requires the active field player");
                    return S(Server.Bot.Types.Blackjack.BlackjackDealerBot.JoinNearestTable(actor));
                }));
                return dealer;
            }
            if (name == "org.gms.client.command.CommandsExecutor")
            {
                var executor = new JsHostObject(null);
                Lenient(executor, "CommandsExecutor");
                executor.Set("getInstance", Ret(_ => executor));
                executor.Set("getCommandsNameDesc", Ret(_ =>
                {
                    var groups = new JsArray();
                    foreach (var group in AdminCommands.ScriptCommandDescriptions(GameServer.Local.Db))
                    {
                        var names = new JsArray(); var descriptions = new JsArray();
                        foreach (var command in group)
                        {
                            names.Items.Add(S(command.Key)); descriptions.Items.Add(S(command.Value));
                        }
                        var pair = new JsHostObject(null);
                        Lenient(pair, "CommandNameDescription");
                        pair.Set("getLeft", Ret(args => BuildJavaList(names)));
                        pair.Set("getRight", Ret(args => BuildJavaList(descriptions)));
                        groups.Items.Add(pair);
                    }
                    return BuildJavaList(groups);
                }));
                return executor;
            }
            if (name == "org.gms.server.ShopFactory")
            {
                var factory = new JsHostObject(null);
                factory.Set("getInstance", Ret(_ => factory));
                factory.Set("getShop", Ret(args =>
                {
                    int id = Int(args, 0);
                    var shop = new JsHostObject(null);
                    shop.Set("sendShop", Fn(_ => NativeNpcShopWindow.OpenShop(id, false)));
                    shop.Set("sell", Fn(args => NativeNpcShopWindow.SellFromScript(id, Int(args, 1), Int(args, 2), Int(args, 3))));
                    return shop;
                }));
                return factory;
            }
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
                provider.Set("getPrice", Ret(args => N(GameServer.Local.NpcShops.SellPrice(Int(args, 0), Int(args, 1, 1)))));
                provider.Set("isSamePart",Ret(args=>
                {
                    string part=OriginalItemPart(Int(args,0));return B(part!=null && part==OriginalItemPart(Int(args,1)));
                }));
                provider.Set("isCashItem",Ret(c=>
                {
                    var item=NativeItemSource.Node(Int(c,0));
                    return B((item?.Get("info/cash") ?? item?.Get("cash"))?.Int("")==1);
                }));
                provider.Set("isQuestItem",Ret(c=>B(NativeItemSource.Node(Int(c,0))?.Int("info/quest")==1)));
                provider.Set("isPartyQuestItem",Ret(c=>B(NativeItemSource.Node(Int(c,0))?.Int("info/pquest")==1)));
                provider.Set("getEquipStats", Ret(args =>
                {
                    var stats = Server.Bot.BotItemCatalog.Shared.EquipStats(Int(args, 0));
                    if (stats == null) return JsNull.Value;
                    var map = new JsHostObject(stats);
                    map.Set("get", Ret(c => stats.TryGetValue(Str(c, 0), out int value) ? N(value) : (JsValue)JsNull.Value));
                    map.Set("containsKey", Ret(c => B(stats.ContainsKey(Str(c, 0)))));
                    return map;
                }));
                // ItemInformationProvider.getEquipLevelReq：星之力按「需求等级」查星级上限表，
                // 读不到需求等级返回 -1（脚本据此报「无法读取这件武器的需求等级」）。
                provider.Set("getEquipLevelReq", Ret(args =>
                {
                    var info = EquipCatalog.Info(Int(args, 0));
                    return N(info == null ? -1 : info.Int("reqLevel"));
                }));
                return provider;
            }
            if (name == "org.gms.server.BossDamageGrowth")
            {
                var growth=new JsHostObject(null);Lenient(growth,"BossDamageGrowth");
                growth.Set("QUEST_REQUIREMENT",N(1000));growth.Set("BOSS_KILL_REQUIREMENT",N(10));
                growth.Set("MONSTER_CARD_REQUIREMENT",N(300));growth.Set("QUEST_MAX_BONUS_PERCENT",N(30));
                growth.Set("BOSS_MAX_BONUS_PERCENT",N(40));growth.Set("MONSTER_CARD_MAX_BONUS_PERCENT",N(30));
                growth.Set("MAX_BONUS_PERCENT",N(100));growth.Set("MAX_MULTIPLIER",N(2));
                BossResponse ReadGrowth(JsCall call)
                {
                    if(call.Arg(0) is JsNull || call.Arg(0) is JsUndefined)return new BossResponse();
                    int id=JsConvert.ToInt32(call.Interp.Call(call.Interp.GetMember(call.Arg(0),"getId"),call.Arg(0),Array.Empty<JsValue>()));
                    var response=GameSession.Net.Boss(new BossRequest {characterId=id,operation=BossOperation.ReadGrowth});
                    if(!response.ok)throw new JsRuntimeException(response.error);
                    return response;
                }
                growth.Set("getQuestCount",Ret(c=>N(ReadGrowth(c).questCount)));
                growth.Set("getMonsterCardCount",Ret(c=>N(ReadGrowth(c).monsterCardCount)));
                growth.Set("getQuestBonusPercent",Ret(c=>N(ReadGrowth(c).questBonusPercent)));
                growth.Set("getBossBonusPercent",Ret(c=>N(ReadGrowth(c).bossBonusPercent)));
                growth.Set("getMonsterCardBonusPercent",Ret(c=>N(ReadGrowth(c).monsterCardBonusPercent)));
                growth.Set("getBonusPercent",Ret(c=>N(ReadGrowth(c).bonusPercent)));
                growth.Set("getMultiplier",Ret(c=>N(ReadGrowth(c).multiplier)));
                growth.Set("calculateQuestBonusPercent",Ret(c=>N(Math.Min(1,Math.Max(0,Int(c,0))/1000.0)*30)));
                growth.Set("calculateMonsterCardBonusPercent",Ret(c=>N(Math.Min(1,Math.Max(0,Int(c,0))/300.0)*30)));
                growth.Set("calculateBonusPercent",Ret(c=>N(NativeBossService.Bonus(Int(c,0),Int(c,1),Int(c,2)))));
                growth.Set("calculateMultiplier",Ret(c=>N(1+NativeBossService.Bonus(Int(c,0),Int(c,1),Int(c,2))/100)));
                growth.Set("invalidate",Fn(c=>GameServer.Local.Bosses.Invalidate(Int(c,0))));
                return growth;
            }
            if (name == "org.gms.server.EquipmentCubeManager")
            {
                var cubes=new JsHostObject(null);
                Lenient(cubes,"EquipmentCubeManager");
                InventoryEntry Equip(JsCall call,int index=0)=>(call.Arg(index) as JsHostObject)?.Host as InventoryEntry;
                cubes.Set("isCube",Ret(c=>B(NativeEquipmentCube.IsCube(Int(c,0)))));
                cubes.Set("cubeName",Ret(c=>S(NativeEquipmentCube.CubeName(Int(c,0)))));
                cubes.Set("cubeSummary",Ret(c=>S(c.ArgCount==1?NativeEquipmentCube.Summary(Int(c,0)):NativeEquipmentCube.Summary(Equip(c),Int(c,1)))));
                cubes.Set("canRoll",Ret(c=>B(NativeEquipmentCube.CanRoll(Equip(c)))));
                cubes.Set("canUseCube",Ret(c=>B(NativeEquipmentCube.CanUse(Equip(c),Int(c,1)))));
                cubes.Set("describe",Ret(c=>S(NativeEquipmentCube.Describe(Equip(c)))));
                cubes.Set("roll",Ret(c=>
                {
                    var roll=NativeEquipmentCube.CreateRoll(Equip(c),Int(c,1));
                    var result=new JsHostObject(roll);Lenient(result,"cube roll");
                    result.Set("itemId",Ret(_=>N(roll.itemId)));result.Set("cubeItemId",Ret(_=>N(roll.cubeItemId)));
                    result.Set("grade",Ret(_=>N(roll.grade)));result.Set("data",Ret(_=>S(roll.data)));
                    result.Set("description",Ret(_=>S(roll.description)));result.Set("rankedUp",Ret(_=>B(roll.rankedUp)));
                    result.Set("canKeepOld",Ret(_=>B(roll.canKeepOld)));return result;
                }));
                cubes.Set("apply",Fn(c=>
                {
                    var equip=Equip(c);NativeEquipmentCube.Apply(equip,(c.Arg(1) as JsHostObject)?.Host as NativeEquipmentCube.Roll);MarkDirty(equip);
                }));
                cubes.Set("inherit",Fn(c=>
                {
                    var target=Equip(c,1);NativeEquipmentCube.Inherit(Equip(c),target);MarkDirty(target);
                }));
                return cubes;
            }
            if (name == "org.gms.server.maps.FieldLimit")
            {
                // FieldLimit.java: independent bit masks, not ordinal enum values.
                var limits = new JsHostObject(null);
                Lenient(limits, "FieldLimit");
                var names = new[] { "JUMP", "MOVEMENTSKILLS", "SUMMON", "DOOR", "CANNOTMIGRATE",
                    "CANNOTVIPROCK", "CANNOTMINIGAME", "CANNOTUSEMOUNTS", "CANNOTUSEPOTION",
                    "CANNOTJUMPDOWN", "NO_EXP_DECREASE", "DROP_LIMIT" };
                var masks = new[] { 1, 2, 4, 8, 16, 64, 128, 512, 4096, 131072, 524288, 4194304 };
                for (int i = 0; i < names.Length; i++)
                {
                    int mask = masks[i];
                    var limit = new JsHostObject(null);
                    Lenient(limit, "FieldLimit." + names[i]);
                    limit.Set("getValue", Ret(_ => N(mask)));
                    limit.Set("check", Ret(c => B((Int(c, 0) & mask) == mask)));
                    limits.Set(names[i], limit);
                }
                return limits;
            }
            if (name == "org.gms.server.life.MonsterInformationProvider")
            {
                var provider=new JsHostObject(null);
                Lenient(provider,"MonsterInformationProvider");
                provider.Set("getInstance",Ret(_=>provider));
                provider.Set("getMobNameFromId",Ret(args=>S(WzNames.Mob(Int(args,0)) ?? "")));
                provider.Set("isBoss",Ret(c=>
                {
                    // MonsterInformationProvider.java:269 catches nonexistent/invalid monster data.
                    try
                    {
                        int id=Int(c,0);FieldCatalog.LoadMob(id);
                        return B(FieldCatalog.Mobs.TryGetValue(id,out var mob) && Art.Num(mob.stats,"boss")>0);
                    }
                    catch(Exception){return B(false);}
                }));
                provider.Set("retrieveDrop",Ret(args=>
                {
                    FieldCatalog.Ensure();
                    var entries=new JsArray();
                    foreach(var drop in DropStore.ForMob(Int(args,0)))
                    {
                        var entry=new JsHostObject(drop);
                        Lenient(entry,"MonsterDropEntry");
                        entry.Set("itemId",N(drop.item));entry.Set("chance",N(drop.chance));
                        entry.Set("Minimum",N(drop.min));entry.Set("Maximum",N(drop.max));entry.Set("questid",N(drop.quest));
                        entries.Items.Add(entry);
                    }
                    var list=BuildJavaList(entries);
                    entries.Set("size",list.Get("size"));entries.Set("get",list.Get("get"));entries.Set("iterator",list.Get("iterator"));
                    return entries;
                }));
                return provider;
            }
            if (name == "org.gms.util.I18nUtil" || name == "org.gms.net.server.Server" || name == "org.gms.util.PacketCreator")
            {
                var type = new JsHostObject(null);
                Lenient(type, name);
                if (name == "org.gms.util.PacketCreator")
                {
                    type.Set("modifyInventory",Ret(args=>
                    {
                        var list=(args.Arg(1) as JsHostObject)?.Host as JsArray ?? args.Arg(1) as JsArray;
                        if(list==null)throw new JsRuntimeException("modifyInventory requires an update list");
                        var packet=new ScriptInventoryPacket();
                        foreach(var entry in list.Items)
                        {
                            var update=(entry as JsHostObject)?.Host as ScriptInventoryUpdate;
                            if(update?.item==null || update.mode<0 || update.mode>3)throw new JsRuntimeException("Invalid inventory update");
                            packet.updates.Add(update);
                        }
                        return new JsHostObject(packet);
                    }));
                    type.Set("serverNotice", Ret(args => new JsHostObject(new ScriptNotice {type=Int(args,0),text=Str(args,args.ArgCount>=3 ? 2 : 1)})));
                    type.Set("musicChange", Ret(args => new JsHostObject(new ScriptSound { path = Str(args, 0), music = true })));
                    type.Set("playSound", Ret(args => new JsHostObject(new ScriptSound { path = Str(args, 0) })));
                    // getClock(seconds)：地图时钟倒计时（列车/活动脚本 sendPacket 出去）。
                    type.Set("getClock", Ret(args => new JsHostObject(new ScriptClock { seconds = Int(args, 0) })));
                    // earnTitleMessage(msg)：SCRIPT_PROGRESS_MESSAGE 文本提示（列车到站播报等）。
                    type.Set("earnTitleMessage", Ret(args => new JsHostObject(new ScriptProgressMessage { text = Str(args, 0) })));
                    // openRPSNPC()：9000019 猜拳 GM NPC 的唯一真实出口（RPS_GAME mode 8 + npcId）。
                    type.Set("openRPSNPC", Ret(_ => new JsHostObject(new ScriptRpsOpen { npcId = NativeRpsService.RpsAdminNpc })));
                    // showEffect：图上播 Map/Effect.img 动画（露希妲的花朵爆炸 / 各阶段技能特效）。
                    type.Set("showEffect", Ret(args => new JsHostObject(new ScriptEffect { path = Str(args, 0) })));
                    // damagePlayer(type, mobId, chrId, damage, fake, dir, isPg, oid, isPcrit, mobOid, ...)：
                    // 源端 12 个参数，客户端只画伤害数字（数值已在脚本/控制器里扣过血）。
                    type.Set("damagePlayer", Ret(args => new JsHostObject(new ScriptDamage {
                        damage = (long)JsConvert.ToNumber(args.Arg(args.ArgCount >= 4 ? 3 : 0)),
                        mobId = Int(args, 1), objectId = Int(args, 9) })));
                    // movePlayer(chrId, InPacket, len)：复活位移广播，单机没有接收方。
                    type.Set("movePlayer", Ret(_ => new JsHostObject(new ScriptMovePlayer())));
                }
                if (name == "org.gms.net.server.Server")
                {
                    type.Set("getInstance", Ret(_ => type));
                    type.Set("broadcastMessage", Fn(args => ApplyScriptPacket(args.Arg(args.ArgCount-1))));
                }
                return type;
            }
            if (name == "org.gms.constants.id.MapId")
            {
                var type = new JsHostObject(null);
                Lenient(type, "MapId");
                type.Set("isPartyDojo", Ret(args => B(Int(args, 0) >= 925030100 && Int(args, 0) <= 925033804)));
                return type;
            }
            if (name == "org.gms.server.quest.Quest")
            {
                var type = new JsHostObject(null);
                Lenient(type, "Quest");
                type.Set("getInstance", Ret(args =>
                {
                    int id = Int(args, 0);
                    var definition = QuestCatalog.Get(id);
                    var quest = new JsHostObject(id);
                    Lenient(quest, "Quest." + id);
                    void OwnPlayer(JsCall call)
                    {
                        var player = call.Arg(0);
                        int owner = JsConvert.ToInt32(call.Interp.Call(call.Interp.GetMember(player, "getId"), player, new JsValue[0]));
                        if (!GameSession.InGame || owner != GameSession.CharacterId) throw new JsRuntimeException("Quest player is not the active character");
                    }
                    quest.Set("getId", Ret(c => N(id)));
                    quest.Set("canStart", Ret(c => { OwnPlayer(c); return B(definition != null && QuestLogic.CanStart(definition, Int(c, 1))); }));
                    quest.Set("canComplete", Ret(c => { OwnPlayer(c); return B(definition != null && QuestLogic.CanComplete(definition, Int(c, 1))); }));
                    quest.Set("start", Fn(c => { OwnPlayer(c); if (definition != null && QuestLogic.CanStart(definition, Int(c, 1))) QuestLogic.Start(definition, Int(c, 1), true); }));
                    quest.Set("complete", Fn(c => { OwnPlayer(c); if (definition != null && QuestLogic.CanComplete(definition, Int(c, 1))) QuestLogic.Complete(definition, Int(c, 1), true); }));
                    quest.Set("reset", Fn(c => { OwnPlayer(c); Profile.ResetQuest(id); }));
                    return quest;
                }));
                return type;
            }
            if (name == "org.gms.server.expeditions.ExpeditionType")
            {
                var type = new JsHostObject(null);
                Lenient(type, "ExpeditionType");
                // 参数表统一由 ExpeditionTypes 提供（ExpeditionType.java:34-50 的唯一副本），
                // 这里只把它包成脚本能点的枚举令牌；Host 放真枚举值，cm.getExpedition 直接取。
                foreach (var value in ExpeditionTypes.All)
                {
                    var token = new JsHostObject(value);
                    Lenient(token, "ExpeditionType." + value);
                    string enumName = value.ToString();
                    var kind = value;
                    token.Set("name", Ret(_ => S(enumName)));
                    token.Set("toString", Ret(_ => S(enumName)));
                    token.Set("getMinSize", Ret(_ => N(ExpeditionTypes.MinSize(kind))));
                    token.Set("getMaxSize", Ret(_ => N(ExpeditionTypes.MaxSize(kind))));
                    token.Set("getMinLevel", Ret(_ => N(ExpeditionTypes.MinLevel(kind))));
                    token.Set("getMaxLevel", Ret(_ => N(ExpeditionTypes.MaxLevel(kind))));
                    token.Set("getRegistrationMinutes", Ret(_ => N(ExpeditionTypes.RegistrationMinutes(kind))));
                    type.Set(enumName, token);
                }
                return type;
            }
            if (name == "org.gms.server.life.LifeFactory") return ScriptMonster.Resolve(name);
            if (name == "org.gms.server.life.LucidBossCompat") return LucidBossCompat.Resolve(name);
            // java.util.concurrent.ThreadLocalRandom.current().nextInt(n)：露希妲的花朵爆炸变体选择。
            if (name == "java.util.concurrent.ThreadLocalRandom")
            {
                var type = new JsHostObject(null);
                Lenient(type, "ThreadLocalRandom");
                var current = new JsHostObject(null);
                Lenient(current, "ThreadLocalRandom.current()");
                current.Set("nextInt", Ret(args =>
                    new JsNumber(args.ArgCount == 0 ? 0 : UnityEngine.Random.Range(0, Math.Max(1, Int(args, 0))))));
                current.Set("nextLong", Ret(_ => new JsNumber(0)));
                current.Set("nextDouble", Ret(_ => new JsNumber(UnityEngine.Random.value)));
                type.Set("current", Ret(_ => current));
                return type;
            }
            // AbstractAnimatedMapObject.IDLE_MOVEMENT_PACKET_LENGTH（源端 15）：复活时拼位移包用。
            if (name == "org.gms.server.maps.AbstractAnimatedMapObject")
            {
                var type = new JsHostObject(null);
                Lenient(type, "AbstractAnimatedMapObject");
                type.Set("IDLE_MOVEMENT_PACKET_LENGTH", N(15));
                return type;
            }
            if (name == "org.gms.constants.game.GameConstants")
            {
                var type = new JsHostObject(null);
                Lenient(type, "GameConstants");
                int JobId(JsCall args) => args.Arg(0) is JsHostObject job
                    ? JsConvert.ToInt32(args.Interp.Call(job.Get("getId"), job, new JsValue[0])) : Int(args, 0);
                type.Set("isAriantColiseumArena", Ret(args => { int map = Int(args, 0); return B(map / 1000 == 980010 && map % 10 == 1); }));
                type.Set("isCygnus", Ret(args => B(Int(args, 0) / 1000 == 1)));
                type.Set("isAran", Ret(args => { int id = Int(args, 0); return B(id == 2000 || (id >= 2100 && id <= 2112)); }));
                type.Set("getJobBranch", Ret(args => { int id = JobId(args); return N(id % 1000 == 0 ? 0 : id % 100 == 0 ? 1 : 2 + id % 10); }));
                type.Set("getHallOfFameMapid", Ret(args =>
                {
                    int id = JobId(args);
                    if (id / 1000 == 1) return N(130000100);
                    if (id == 2000 || (id >= 2100 && id <= 2112)) return N(140010110);
                    switch (id / 100) { case 1: return N(102000004); case 2: return N(101000004); case 3: return N(100000204); case 4: return N(103000008); case 5: return N(120000105); default: return N(130000110); }
                }));
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
                    skill.Set("id", N(data.id));
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
                    // Monster.getName()：AreaBoss*.js / map/onUserEnter/*.js 都用它取 Boss 名做公告
                    monster.Set("getName",Ret(_ => S(WzNames.Mob(id))));
                    monster.Set("setPosition",Fn(c => monster.Set("position",c.Arg(0))));
                    monster.Set("setFh",Fn(c=>monster.Set("fh",N(Int(c,0)))));
                    monster.Set("getFh",Ret(_=>monster.Get("fh") is JsNumber?monster.Get("fh"):N(0)));
                    return monster;
                }));
                return factory;
            }
            // 脚本里的枚举/工具类引用：值语义对了，逻辑就能照源端原样跑。
            // InventoryType 的数值取「道具 id 首位」（EQUIP=1 USE=2 SETUP=3 ETC=4 CASH=5），
            // 因为脚本是把它当 int 传给 getInventory(type) 的。
            if (name == "org.gms.constants.inventory.ItemConstants")
            {
                var constants = new JsHostObject(null);
                Lenient(constants, "ItemConstants");
                constants.Set("isAccessory", Ret(c => B(Int(c, 0) >= 1110000 && Int(c, 0) < 1140000)));
                constants.Set("isNewHair", Ret(c =>
                {
                    if (newHairBases == null)
                    {
                        var resource = Resources.Load<TextAsset>("Scripts/hair_base_ids");
                        if (resource == null) throw new JsRuntimeException("Missing original Hair ID catalog");
                        newHairBases = new HashSet<int>();
                        foreach (string line in resource.text.Split('\n'))
                            if (int.TryParse(line.Trim(), out int id)) newHairBases.Add(id);
                    }
                    int hair = Int(c, 0), color = hair % 10;
                    return B(color >= 0 && color <= 7 && newHairBases.Contains(hair - color));
                }));
                return constants;
            }
            if (name == "org.gms.client.inventory.Pet")
            {
                var pet = new JsHostObject(null);
                pet.Set("createPet", Ret(args =>
                {
                    var result=GameSession.Net.ChangeInventory(new InventoryChangeRequest {
                        characterId=GameSession.CharacterId,itemId=Int(args,0),createPet=true });
                    if(!result.ok) throw new JsRuntimeException(result.error);
                    return N(result.createdPetId);
                }));
                return pet;
            }
            if (name == "org.gms.client.inventory.InventoryType")
            {
                var type = new JsHostObject(null);
                type.Set("EQUIP", N(1)); type.Set("USE", N(2)); type.Set("SETUP", N(3));
                type.Set("UNDEFINED", N(0)); type.Set("ETC", N(4)); type.Set("CASH", N(5)); type.Set("EQUIPPED", N(-1));
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
                    if (id <= 0 || count <= 0) return B(false);
                    var result=GameSession.Net.ChangeInventory(new InventoryChangeRequest {
                        characterId=GameSession.CharacterId,itemId=id,quantity=count,
                        petId=args.ArgCount>4 ? Int(args,4,-1) : -1,
                        itemExpiration=args.ArgCount>5 ? (long)JsConvert.ToNumber(args.Arg(5)) : -1 });
                    if(!result.ok) return B(false);
                    GameSession.Character.inventory=result.inventory;
                    GameSession.ApplyInventoryToProfile();Profile.Save();
                    return B(true);
                }));
                // removeFromSlot(Client, type, slot, quantity, fromDrop)：按格位删。
                // 星之力强化要删「其他栏第 2 格的防爆卷」和「爆装时删装备栏第 1 格」，
                // 单机 inventoryitems.position 就是格号，可以照源端实现。
                manipulator.Set("removeFromSlot", Ret(args =>
                    B(RemoveSlotItem(Int(args, 1), Int(args, 2), Math.Max(1, Int(args, 3, 1))))));
                return manipulator;
            }
            // org.gms.client.Job（Job.java）：脚本拿它比职业样式（stance == Job.WARRIOR）
            // 或按名字取常量（Java.type('org.gms.client.Job')["DAWNWARRIOR1"]，20101~20315 转职脚本）。
            // 常量必须始终是同一个实例，== 才是源端的 enum 引用相等。
            if (name == "org.gms.client.Job")
            {
                var job = new JsHostObject(null);
                Lenient(job, "Job");
                foreach (var const_ in JobEnum.All) job.Set(const_.Name, JobEnum.Js(const_));
                job.Set("getById",Ret(c=>JobEnum.Js(JobEnum.Of(Int(c,0)) ?? JobEnum.Of(0))));
                // Job.getJobStyleInternal(jobid, opt)：opt=0x80 走拳手，否则火枪手。
                job.Set("getJobStyleInternal", Ret(args =>
                    JobEnum.Js(JobEnum.Of(JobEnum.StyleOf(Int(args, 0), Int(args, 1, 0x40) == 0x80)))));
                return job;
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
            // org.gms.net.server.guild.Guild（Guild.java）：2010007.js 用静态 getIncreaseGuildCost(capacity)
            // 算「增加 5 人上限要多少金币」。费用公式与 NativeCommunityService.ExpandGuild 同一份。
            if (name == "org.gms.net.server.guild.Guild")
            {
                var guild = new JsHostObject(null);
                Lenient(guild, "Guild");
                guild.Set("getIncreaseGuildCost", Ret(args =>
                {
                    var db = GameServer.Local?.Db;
                    return db == null ? N(0) : N(NativeCommunityService.IncreaseGuildCost(db, Int(args, 0)));
                }));
                return guild;
            }
            // org.gms.client.BuffStat（BuffStat.java 枚举）：脚本把它当常量令牌传给
            // player.getBuffSource(...)（2081005/2103000/2111015、quest/3314/3514/3941、
            // portal/ariant_queens 只用到 MORPH 与 HPREC）。源端是 enum 引用相等：
            // 同名常量恒为同一实例；缺的常量走 Lenient 明确报缺，不静默给假对象。
            if (name == "org.gms.client.BuffStat")
            {
                var stat = new JsHostObject(null);
                Lenient(stat, "BuffStat");
                foreach (string constant in new[] { "MORPH", "HPREC" })
                {
                    var token = new JsHostObject(constant);
                    token.Set("name", Ret(_ => S(constant)));
                    token.Set("toString", Ret(_ => S(constant)));
                    stat.Set(constant, token);
                }
                return stat;
            }
            // org.gms.net.server.channel.handlers.RingActionHandler（RingActionHandler.java）：
            // 9201004 离婚走 breakMarriageRing(player, ringItemId)——清 partnerId、发离婚包；
            // 戒指本体由脚本随后的 gainItem(itemId,-1) 删除，这里只做服务端状态面。
            if (name == "org.gms.net.server.channel.handlers.RingActionHandler")
            {
                var handler = new JsHostObject(null);
                Lenient(handler, "RingActionHandler");
                handler.Set("breakMarriageRing", Ret(args =>
                {
                    var record = GameSession.Character?.record
                        ?? throw new JsRuntimeException("Divorce requires an active character");
                    record.partnerId = 0;
                    record.marriageItemId = 0;
                    GameSession.SaveToDb();
                    return JsNull.Value;
                }));
                return handler;
            }
            // soloMapling.ArtificialPlayer.BotBossCombatManager：自由市场聊天拉假人集火 Boss
            // （9000055/9900002 的 handleChatTrigger(player, "假人打boss")）。未命中触发词
            // 返回 false，聊天链照常继续——与源端 GeneralChatHandler 的接法一致。
            if (name == "soloMapling.ArtificialPlayer.BotBossCombatManager")
            {
                var boss = new JsHostObject(null);
                Lenient(boss, "BotBossCombatManager");
                boss.Set("handleChatTrigger", Ret(args =>
                    B(NativeBotBossCombat.HandleChatTrigger(MapId, args.ArgCount > 1 ? Str(args, 1) : Str(args, 0)))));
                return boss;
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
        result.Set("getName",Ret(_ => S(WzNames.Mob(mob.fieldMobId))));
        result.Set("getLevel",Ret(_ => N(NativeCombatData.Int(mob.CombatStats, "level", 1))));
        result.Set("isBoss",Ret(_ => B(mob.IsBoss)));
        result.Set("getBoss",result.Get("isBoss"));
        result.Set("getMaxMp",Ret(_ => N(NativeCombatData.Int(mob.CombatStats, "maxMP"))));
        return result;
    }

    static JsHostObject BuildPosition()
        => BuildPoint(Pc!=null ? Pc.worldX*Art.PPU : HeadlessPosition.x,
            Pc!=null ? -Pc.FeetPos.y*Art.PPU : HeadlessPosition.y);
    internal static JsHostObject BuildPoint(double x,double y)
    {
        var p = new JsHostObject(null);
        p.Set("x",N(x));p.Set("y",N(y));
        p.Set("getX", Ret(_ => N(x)));
        p.Set("getY", Ret(_ => N(y)));
        p.Set("distance",Ret(c=> {
            if(!(c.Arg(0) is JsHostObject other)) return N(double.PositiveInfinity);
            double dx=x-JsConvert.ToNumber(other.Get("x")),dy=y-JsConvert.ToNumber(other.Get("y"));
            return N(Math.Sqrt(dx*dx+dy*dy));
        }));
        return p;
    }

    static JsHostObject BuildClient(JsHostObject player)
    {
        var c = new JsHostObject(player);
        Lenient(c, "client");
        c.Set("getPlayer", Ret(_ => player));
        c.Set("getLogger",Ret(_=>BuildLogger("Client")));
        c.Set("sendPacket", Fn(args => ApplyScriptPacket(args.Arg(0))));
        c.Set("getChannel", Ret(_ => N(GameSession.Channel)));
        c.Set("getWorld", Ret(_ => N(GameSession.World)));
        c.Set("getChannelServer", Ret(_ => BuildChannelServer(player)));
        c.Set("getEventManager", Ret(args => NativeEventScripts.Manager(Str(args,0))));
        return c;
    }

    // 单机只有自己在线：ChannelServer.getPlayerStorage().getAllCharacters() 只装自己一个。
    // 共同富裕 / 冒险炒股 这类「给当前频道所有人发点券」的脚本靠这条链跑通，
    // 语义上等价于「发给自己」。
    internal static JsHostObject BuildChannelServer(JsHostObject player)
    {
        var server = new JsHostObject(null);
        Lenient(server, "channelServer");
        var storage = new JsHostObject(null);
        Lenient(storage, "playerStorage");
        storage.Set("getAllCharacters", Ret(_ => new JsArray(new JsValue[] { player })));
        storage.Set("getAllCharactersCount", Ret(_ => N(1)));
        storage.Set("getCharacterById", Ret(_ => player));
        foreach (string method in new[] { "getStoredVar", "setStoredVar" })
        {
            bool write = method == "setStoredVar";
            server.Set(method, Ret(c =>
            {
                if (!GameSession.InGame) throw new JsRuntimeException("Channel variables require an active character");
                return GameSession.Net.Script(new ScriptRequest { characterId=GameSession.CharacterId,
                    operation=write ? ScriptOperation.SetStoredVar : ScriptOperation.GetStoredVar,
                    world=GameSession.World, channel=GameSession.Channel, variableKey=Int(c,0), variableValue=Int(c,1) }).value;
            }));
        }
        server.Set("getMapFactory", Ret(_ =>
        {
            var factory = new JsHostObject(null);
            Lenient(factory, "mapFactory");
            factory.Set("getMap", Ret(c => BuildWarpMap(Int(c, 0))));
            return factory;
        }));
        server.Set("getPlayerStorage", Ret(_ => storage));
        // —— 武陵道场（Channel.java L494-700；2091005 检票/离场分支）——
        // lookupPartyDojo(party)：party 为 null（源端同样入参）直接 -1。
        server.Set("lookupPartyDojo", Ret(c => N(NativeDojoService.LookupPartyDojo(PartyIdOf(c, 0)))));
        // ingressDojo(isPartyDojo[, party], fromStage)：两参/三参重载都按源端语义落。
        server.Set("ingressDojo", Ret(c => N(NativeDojoService.IngressDojo(
            JsConvert.ToBoolean(c.Arg(0)),
            c.ArgCount > 2 ? PartyIdOf(c, 1) : 0,
            Int(c, c.ArgCount > 2 ? 2 : 1)))));
        server.Set("resetDojoMap", Fn(c => NativeDojoService.ResetDojoMap(Int(c, 0))));
        server.Set("freeDojoSectionIfEmpty", Fn(c => NativeDojoService.FreeDojoSectionIfEmpty(Int(c, 0))));
        server.Set("dismissDojoSchedule", Fn(c => NativeDojoService.DismissDojoSchedule(Int(c, 0), PartyIdOf(c, 1))));
        server.Set("getDojoFinishTime", Ret(c => N(NativeDojoService.GetDojoFinishTime(Int(c, 0)))));
        // Channel.getId()：AreaBoss*.js 的 init() 第一句就是 em.getChannelServer().getId()，
        // 缺了它整个事件脚本 init 会抛异常，野外 Boss 一个都启动不了。
        server.Set("getId", Ret(_ => N(GameSession.Channel)));
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

    // ItemInformationProvider.getItemPathStr: this deliberately follows the source
    // category ranges (accessories share one category), not EquipSlot equality.
    static string OriginalItemPart(int id)
    {
        if((id>=1670000 && id<1680000) || (id>=1010000 && id<1040000) ||
            (id>=1122000 && id<1123000) || (id>=1132000 && id<1133000) ||
            (id>=1142000 && id<1143000) || (id>=1152000 && id<1153000) ||
            (id>=1180000 && id<1200000))return "Accessory";
        if(id>=1000000 && id<1010000)return "Cap";
        if(id>=1102000 && id<1103000)return "Cape";
        if(id>=1040000 && id<1050000)return "Coat";
        if(id/10000==2 || id/10000==5)return "Face";
        if(id>=1080000 && id<1090000)return "Glove";
        if(id/10000==3 || id/10000==4 || id/10000==6)return "Hair";
        if(id>=1050000 && id<1060000)return "Longcoat";
        if(id>=1060000 && id<1070000)return "Pants";
        if(id>=1802000 && id<1842000)return "PetEquip";
        if(id>=1112000 && id<1120000)return "Ring";
        if(id>=1092000 && id<1100000)return "Shield";
        if(id>=1070000 && id<1080000)return "Shoes";
        if(id>=1900000 && id<2000000)return "Taming";
        if(id>=1300000 && id<1800000)return "Weapon";
        if(id>=4000000 && id<5000000)return "Etc";
        return null;
    }

    static HashSet<int> newHairBases;

    static int FreeInventorySlots(int type)
    {
        if(type<1 || type>5)throw new JsRuntimeException("Invalid inventory type: "+type);
        int limit=Profile.InventoryCapacity(type-1);var used=new HashSet<int>();
        if(GameSession.Character!=null)
            foreach(var item in GameSession.Character.inventory)
                if(item.inventoryType==type && item.position>0 && item.position<=limit)used.Add(item.position);
        return Math.Max(0,limit-used.Count);
    }
    static JsHostObject BuildInventory(int type)
    {
        var inv = new JsHostObject(type);
        Lenient(inv, "inventory");
        object inventoryLock=GameServer.Local?.Db ?? throw new JsRuntimeException("Inventory requires an active server");
        inv.Set("lockInventory",Fn(_=>System.Threading.Monitor.Enter(inventoryLock)));
        inv.Set("unlockInventory",Fn(_=>System.Threading.Monitor.Exit(inventoryLock)));
        inv.Set("countById", Ret(c =>
        {
            int id=Int(c,0),count=0;
            if(GameSession.Character?.inventory!=null)
                foreach(var item in GameSession.Character.inventory)
                    if(InScriptInventory(item,type) && item.itemId==id)count+=item.isEquip?1:item.quantity;
            return N(count);
        }));
        inv.Set("getSlotLimit", Ret(_ => N(Profile.InventoryCapacity(type - 1))));
        inv.Set("getNumFreeSlot", Ret(_ => N(FreeInventorySlots(type))));
        // Inventory.getNextFreeSlot（源端 Inventory.java）：最小空格位；满栏返回 -1。
        // 9201014 玛瑙宝箱用 `getNextFreeSlot() >= 0` 判「消耗栏还有位」。
        inv.Set("getNextFreeSlot", Ret(_ =>
        {
            int limit = Profile.InventoryCapacity(type - 1);
            var used = new HashSet<int>();
            if (GameSession.Character?.inventory != null)
                foreach (var item in GameSession.Character.inventory)
                    if (item.inventoryType == type && item.position > 0) used.Add(item.position);
            for (int slot = 1; slot <= limit; slot++) if (!used.Contains(slot)) return N(slot);
            return N(-1);
        }));
        // Inventory.getItem(slot)（源端 Inventory.java）：星之力强化脚本用
        // `getInventory(EQUIP).getItem(1)` 拿「装备栏第 1 格」。
        inv.Set("getItem", Ret(c => BuildItemView(type, Int(c, 0))));
        inv.Set("getItemBySlot", Ret(c => BuildItemView(type, Int(c, 0))));
        // Inventory.iterator()（源端 Inventory.java）：脚本拿它遍历某一栏的道具。
        // npc/9330045.js 的 hasChair() 就是扫 SETUP 栏找椅子，决定能不能进钓鱼场。
        // 栏位 = 道具 id 首位，与上面的 InventoryType 数值（EQUIP=1…SETUP=3…）同一套。
        inv.Set("iterator", Ret(_ => BuildInventoryIterator(type)));
        inv.Set("listById", Ret(c =>
        {
            var arr = new JsArray();
            int id = Int(c, 0);
            if(GameSession.Character?.inventory!=null)
                foreach(var item in GameSession.Character.inventory)
                    if(InScriptInventory(item,type) && item.itemId==id)arr.Items.Add(BuildItemView(type,item.position));
            return arr;
        }));
        return inv;
    }

    static JsHostObject BuildInventoryIterator(int type)
    {
        var slots = new List<int>();
        if(GameSession.Character?.inventory!=null)
            foreach(var item in GameSession.Character.inventory)
                if(InScriptInventory(item,type))slots.Add(item.position);
        slots.Sort();
        int index = 0;
        var it = new JsHostObject(null);
        it.Set("hasNext", Ret(_ => B(index < slots.Count)));
        it.Set("next", Ret(_ =>
        {
            if(index>=slots.Count)throw new JsRuntimeException("Inventory iterator has no next item");
            return BuildItemView(type,slots[index++]);
        }));
        return it;
    }

    // ————————————————————————————————————————————
    // 装备实例的脚本视图（源端 Equip 的可写面）。
    // 单机背包真源在 DB（inventoryitems + inventoryequipment），内存副本挂在
    // GameSession.Character.inventory —— 就是装备窗读的那一份。
    // 脚本 setXxx 改内存副本并登记脏行，forceUpdateItem / dispose 时整行回写。
    // ————————————————————————————————————————————
    static readonly Dictionary<long, InventoryEntry> dirtyEquip = new Dictionary<long, InventoryEntry>();

    static void MarkDirty(InventoryEntry e)
    {
        if (e == null || e.itemRowId <= 0) return;
        dirtyEquip[e.itemRowId] = e;
    }

    // 源端 gainItem 返回的 Item（AbstractPlayerInteraction.gainItem:611-690）。
    // 单机没有「装备实例」这层，只给脚本一个可判定成功/失败的句柄；
    // 未迁移的 Item 方法走 Lenient，明确报「缺少 item.xxx」而不是静默出错。
    static JsValue NewItemRef(int id, int count)
    {
        var item = new JsHostObject(null);
        Lenient(item, "item");
        item.Set("getItemId", Ret(_ => N(id)));
        item.Set("getId", Ret(_ => N(id)));
        item.Set("getQuantity", Ret(_ => N(count)));
        item.Set("getPosition", Ret(_ => N(-1)));
        return item;
    }

    static InventoryEntry SlotEntry(int type, int slot)
    {
        var full = GameSession.Character;
        if (full?.inventory == null) return null;
        foreach (var it in full.inventory)
            if (InScriptInventory(it,type) && it.position == slot) return it;
        return null;
    }

    // Local persistence stores worn gear in inventorytype=1 with negative positions;
    // Java exposes that range as its separate EQUIPPED(-1) inventory.
    static bool InScriptInventory(InventoryEntry item,int type)
        =>type==-1?item.inventoryType==InventoryDao.Equipped && item.position<0:
            item.inventoryType==type && item.position>0;

    static JsValue BuildItemView(int type, int slot)
    {
        return BuildItemView(SlotEntry(type, slot));
    }
    static JsValue BuildItemView(InventoryEntry e,bool live=true)
    {
        if (e == null) return JsNull.Value;
        void Dirty() { if(live)MarkDirty(e); }
        var item = new JsHostObject(e);
        Lenient(item, "item");
        item.Set("getItemId", Ret(_ => N(e.itemId)));
        item.Set("getId", Ret(_ => N(e.itemId)));
        item.Set("copy",Ret(_=>BuildItemView(e.Copy(),false)));
        item.Set("getQuantity", Ret(_ => N(e.isEquip ? 1 : e.quantity)));
        item.Set("getPosition", Ret(_ => N(e.position)));
        // Stable inventory identity also survives the inventory snapshot refresh after cube consumption.
        item.Set("getCashId", Ret(_ => N(e.itemRowId)));
        item.Set("getExpandAttribute4",Ret(_=>S(NativeEquipmentCube.Raw(e))));
        item.Set("setExpandAttribute4",Fn(c=>
        {
            if(e.equipmentData==null)e.equipmentData=new Dictionary<string,object>();
            e.equipmentData["expandAttribute4"]=Str(c,0);Dirty();
        }));
        item.Set("getUpgradeSlots", Ret(_ => N(e.upgradeslots)));
        item.Set("getLevel",Ret(_=>N(e.equipmentData!=null && e.equipmentData.TryGetValue("level",out var level)?Convert.ToInt32(level):0)));
        item.Set("getOwner", Ret(_ => S(e.owner)));
        item.Set("getExpiration",Ret(_=>N(e.expiration)));
        item.Set("setExpiration",Fn(c=>{ e.expiration=(long)JsConvert.ToNumber(c.Arg(0));Dirty(); }));

        // 星之力（源端 Equip.maxStar / starLevel / starCount）
        item.Set("getStarLevel", Ret(_ => N(e.starLevel)));
        item.Set("setStarLevel", Fn(c => { e.starLevel = Math.Max(0, Int(c, 0)); Dirty(); }));
        item.Set("getStarCount", Ret(_ => N(e.starCount)));
        item.Set("setStarCount", Fn(c => { e.starCount = Math.Max(0, Int(c, 0)); Dirty(); }));
        item.Set("getMaxStar", Ret(_ => N(e.maxStar)));
        item.Set("setMaxStar", Fn(c => { e.maxStar = Math.Max(0, Int(c, 0)); Dirty(); }));
        item.Set("setOwner", Fn(c => { e.owner = Str(c, 0); Dirty(); }));

        // 属性读写（脚本 addStarStats/removeStarStats 直接加减）
        item.Set("getStr", Ret(_ => N(e.str)));
        item.Set("setStr", Fn(c => { e.str = Math.Max(0, Int(c, 0)); Dirty(); }));
        item.Set("getDex", Ret(_ => N(e.dex)));
        item.Set("setDex", Fn(c => { e.dex = Math.Max(0, Int(c, 0)); Dirty(); }));
        item.Set("getInt", Ret(_ => N(e.int_)));
        item.Set("setInt", Fn(c => { e.int_ = Math.Max(0, Int(c, 0)); Dirty(); }));
        item.Set("getLuk", Ret(_ => N(e.luk)));
        item.Set("setLuk", Fn(c => { e.luk = Math.Max(0, Int(c, 0)); Dirty(); }));
        item.Set("getHp",Ret(_=>N(e.hp)));item.Set("setHp",Fn(c=>{e.hp=Math.Max(0,Int(c,0));Dirty();}));
        item.Set("getMp",Ret(_=>N(e.mp)));item.Set("setMp",Fn(c=>{e.mp=Math.Max(0,Int(c,0));Dirty();}));
        item.Set("getWatk", Ret(_ => N(e.watk)));
        item.Set("setWatk", Fn(c => { e.watk = Math.Max(0, Int(c, 0)); Dirty(); }));
        item.Set("getMatk", Ret(_ => N(e.matk)));
        item.Set("setMatk", Fn(c => { e.matk = Math.Max(0, Int(c, 0)); Dirty(); }));
        item.Set("getWdef", Ret(_ => N(e.wdef)));
        item.Set("setWdef", Fn(c => { e.wdef = Math.Max(0, Int(c, 0)); Dirty(); }));
        item.Set("getMdef", Ret(_ => N(e.mdef)));
        item.Set("setMdef", Fn(c => { e.mdef = Math.Max(0, Int(c, 0)); Dirty(); }));
        item.Set("getAcc", Ret(_ => N(e.acc)));
        item.Set("setAcc", Fn(c => { e.acc = Math.Max(0, Int(c, 0)); Dirty(); }));
        item.Set("getAvoid", Ret(_ => N(e.avoid)));
        item.Set("setAvoid", Fn(c => { e.avoid = Math.Max(0, Int(c, 0)); Dirty(); }));
        item.Set("getHands", Ret(_ => N(e.hands)));
        item.Set("setHands", Fn(c => { e.hands = Math.Max(0, Int(c, 0)); Dirty(); }));
        item.Set("getSpeed", Ret(_ => N(e.speed)));
        item.Set("setSpeed", Fn(c => { e.speed = Math.Max(0, Int(c, 0)); Dirty(); }));
        item.Set("getJump", Ret(_ => N(e.jump)));
        item.Set("setJump", Fn(c => { e.jump = Math.Max(0, Int(c, 0)); Dirty(); }));
        return item;
    }

    // 把脚本改过的装备行整行回写（源端 Character.forceUpdateItem）：
    // 一次对话里可能改了不止一件，逐件补丁，最后一个快照回填内存背包。
    static void FlushEquip()
    {
        if (dirtyEquip.Count == 0) return;
        int cid = GameSession.CharacterId;
        List<InventoryEntry> latest = null;
        if (cid > 0)
        {
            foreach (var e in dirtyEquip.Values)
            {
                var res = GameSession.Net.UpdateEquip(new EquipPatchRequest
                {
                    characterId = cid,
                    inventoryItemId = e.itemRowId,
                    expiration = e.itemId/1000000==1 ? null : (long?)e.expiration,
                    starLevel = e.starLevel, starCount = e.starCount, maxStar = e.maxStar,
                    owner = e.owner,
                    expandAttribute4 = NativeEquipmentCube.Raw(e),
                    str = e.str, dex = e.dex, int_ = e.int_, luk = e.luk, hp = e.hp, mp = e.mp,
                    watk = e.watk, matk = e.matk, wdef = e.wdef, mdef = e.mdef,
                    acc = e.acc, avoid = e.avoid, hands = e.hands, speed = e.speed, jump = e.jump,
                    upgradeslots = e.upgradeslots,
                });
                if (!res.ok) { Debug.LogError("[Script] 装备回写失败：" + res.error); continue; }
                if (res.inventory != null) latest = res.inventory;
            }
        }
        if (latest != null && GameSession.Character != null) GameSession.Character.inventory = latest;
        dirtyEquip.Clear();
        Pc?.ApplyProgression();
    }

    // InventoryManipulator.removeFromSlot 的单机实现：按 (栏位, 格号) 删。
    static bool RemoveSlotItem(int type, int slot, int quantity)
    {
        int cid = GameSession.CharacterId;
        if (cid <= 0) return false;
        var res = GameSession.Net.RemoveSlot(new SlotRemoveRequest
        {
            characterId = cid, inventoryType = type, slot = slot, quantity = quantity,
        });
        if (!res.ok) { Debug.LogError("[Script] 删除格位失败：" + res.error); return false; }
        if (res.inventory != null && GameSession.Character != null) GameSession.Character.inventory = res.inventory;
        return true;
    }
}
