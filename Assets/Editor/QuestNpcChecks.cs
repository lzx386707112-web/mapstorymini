using System;
using System.Collections.Generic;
using System.IO;
using MapleDash.Js;
using UnityEditor;
using UnityEngine;

// NPC 脚本 / 任务系统自检（不需要进 Play）：
//   Unity -batchmode -projectPath <proj> -executeMethod QuestNpcChecks.Run -logFile /tmp/quest_npc.log
//
// 这一层验证的是"离线验收器覆盖不到"的部分：
//   - Quest.wz 能不能被客户端解析（服务端逻辑照抄，但读取路径是我们自己的）
//   - 打包通道 Resources/Scripts/npc_scripts.json 在 Unity 里能不能拿到、能不能逐条解析
//   - 脚本 API 装配（cm/qm）在真实 Unity 环境里跑一段对话会不会抛错
// 不代替实机手感验收（对话框位置、点选命中、换图黑场仍要上手点）。
public static class QuestNpcChecks
{
    static int pass, fail;

    static void Check(bool ok, string what)
    {
        if (ok) { pass++; return; }
        fail++;
        Debug.LogError("[QuestNpcCheck] 失败：" + what);
    }

    public static void Run()
    {
        var data = Profile.Data;
        string snapshot = JsonUtility.ToJson(data);
        var saveOverride = typeof(Profile).GetField("debugOverride", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        bool previousOverride = (bool)saveOverride.GetValue(null);
        saveOverride.SetValue(null, true);
        try
        {
            pass = fail = 0;

            CheckScriptPack();
            CheckQuestCatalog();
            CheckScriptLookup();
            CheckDialogText();
            CheckQuestRules();
            CheckBeginnerNpcBindings();
            CheckTrainingQuestProgress();
            CheckDialogProtocol();
            CheckRepeatedTalkPresses();
            CheckBirthRoutes();
            CheckNewCharacterMarkers();
            CheckRogerApplePresentation();
            CheckRestorativeItems();
            CheckAllMapQuestLinks();

            Debug.Log("[QuestNpcCheck] 通过 " + pass + " 项，失败 " + fail + " 项");
        }
        finally
        {
            JsonUtility.FromJsonOverwrite(snapshot, data);
            saveOverride.SetValue(null, previousOverride);
        }
        if (fail > 0 && Application.isBatchMode) EditorApplication.Exit(1);
    }

    // 独立进程、临时数据库；验证真实 NpcApi，而非宽松 mock。
    static void CheckRogerApplePresentation()
    {
        string description = ItemTooltip.FormatDescription(ItemCatalog.Desc(2010007));
        Check(description.Contains("\n\n") && description.Contains("<color=#FF9900>双击") && !description.Contains("#c"),
            "苹果说明须解析原端换行和强调控制码");
        var icon = NativeItemSource.Icon(2010007);
        Check(icon != null && icon.width == 26 && icon.height == 30,
            "罗杰任务苹果须直接解码0201.img/02010007/info/icon");
        var previousLoop = GameLoop.I;
        var data = Profile.Data;
        string snapshot = JsonUtility.ToJson(data);
        var host = new GameObject("RogerApplePresentationCheck");
        host.SetActive(false);
        try
        {
            var loop = host.AddComponent<GameLoop>();
            loop.player = host.AddComponent<PlayerCtrl>();
            GameLoop.I = loop;
            data.questStarted.Remove(1021);
            data.questCompleted.Remove(1021);
            var session = NpcSession.TalkQuest(QuestCatalog.Get(1021), 2000, false);
            for (int i = 0; i < 5 && session != null; i++) session.Reply(1);
            Check(session != null && session.LastError == null && Profile.ItemCount(2010007) > 0,
                "原始1021脚本须实际发放任务苹果");
            var effect = host.GetComponentInChildren<NativeImgAnimation>(true);
            Check(effect != null && effect.name == "UI/tutorial.img/28" && effect.Image.sprite != null,
                "原始1021脚本结束须实际创建新手引导原IMG动画");
            if (effect != null)
            {
                Check(effect.Image.sprite.texture.width == 215 && effect.Image.sprite.texture.height == 283,
                    "引导素材尺寸须保持215×283");
                session?.Close();
                Check(effect != null, "关闭NPC会话不得销毁新手引导");
                effect.Tick(double.MaxValue);
                Check(host.GetComponentInChildren<NativeImgAnimation>(true) == null,
                    "引导动画完整播完后须释放");
            }
        }
        finally
        {
            NpcSession.CloseActive();
            GameLoop.I = previousLoop;
            UnityEngine.Object.DestroyImmediate(host);
            JsonUtility.FromJsonOverwrite(snapshot, data);
            NpcField.InvalidateMarkers();
        }
    }

    static void CheckRestorativeItems()
    {
        var previousLoop = GameLoop.I;
        var data = Profile.Data;
        string snapshot = JsonUtility.ToJson(data);
        var host = new GameObject("RestorativeItemCheck");
        host.SetActive(false);
        try
        {
            var loop = host.AddComponent<GameLoop>();
            var player = host.AddComponent<PlayerCtrl>();
            loop.player = player; GameLoop.I = loop;
            player.hpMax = 100; player.mpMax = 100; player.hp = 25; player.mp = 10;
            data.AddItem(2010007, 2);
            int count = Profile.ItemCount(2010007);
            Check(NpcSession.UseRestorative(2010007) && player.hp == 55 && Profile.ItemCount(2010007) == count - 1,
                "任务苹果双击须恢复原IMG的30HP并仅扣一个");
            player.hp = 99;
            Check(NpcSession.UseRestorative(2010007) && player.hp == 100, "恢复不得超过最大HP");
            player.hp = 0; count = Profile.ItemCount(2010007);
            Check(!NpcSession.UseRestorative(2010007) && Profile.ItemCount(2010007) == count, "死亡不得消耗药品");
            player.hp = 100;
            data.RemoveItem(2010007, Profile.ItemCount(2010007));
            Check(!NpcSession.UseRestorative(2010007), "没有物品不得恢复");
            data.RemoveItem(2000003, Profile.ItemCount(2000003)); data.AddItem(2000003, 1); player.mp = 0;
            Check(NpcSession.UseRestorative(2000003) && player.mp > 0 && Profile.ItemCount(2000003) == 0,
                "普通蓝药须读取MP效果而非仅特判苹果");
        }
        finally
        {
            GameLoop.I = previousLoop;
            UnityEngine.Object.DestroyImmediate(host);
            JsonUtility.FromJsonOverwrite(snapshot, data);
        }
    }

    public static void RunInventory()
    {
        pass = fail = 0;
        string path = Path.Combine(Path.GetTempPath(), "mapledash-inventory-" + Guid.NewGuid() + ".json");
        var data = Profile.Data;
        string snapshot = JsonUtility.ToJson(data);
        var overrideField = typeof(Profile).GetField("debugOverride", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        bool previousOverride = (bool)overrideField.GetValue(null);
        overrideField.SetValue(null, true);
        var previousLoop = GameLoop.I;
        var host = new GameObject("InventoryUseCheck"); host.SetActive(false);
        try
        {
            var db = DbStore.Create(path);
            var character = new CharRecord { accountid = 1, name = "InventoryTest", level = 30, equipslots = 24, useslots = 24, setupslots = 24, etcslots = 24 };
            int id = (int)CharactersDao.Insert(db, character); character.id = id;
            long oldHat = InventoryDao.InsertItem(db, id, 1002000, 1, -1, 1);
            InventoryDao.InsertEquipment(db, oldHat, str: 7);
            long firstHat = InventoryDao.InsertItem(db, id, 1002001, 1, 1, 1);
            InventoryDao.InsertEquipment(db, firstHat, str: 13);
            long secondHat = InventoryDao.InsertItem(db, id, 1002001, 1, 2, 1);
            InventoryDao.InsertEquipment(db, secondHat, str: 19);
            var moved = NativeInventoryService.Handle(db, new InventoryChangeRequest { characterId = id, itemId = 1002001, equip = true, equipSlot = -1, quantity = 1, sourcePosition = 2 });
            Check(moved.ok && moved.inventory.Exists(x => x.itemRowId == secondHat && x.position == -1 && x.str == 19), "双击须穿戴实际格位的强化装备并保留属性");
            Check(moved.inventory.Exists(x => x.itemRowId == firstHat && x.position == 1 && x.str == 13) && moved.inventory.Exists(x => x.itemRowId == oldHat && x.position == 2 && x.str == 7), "同ID另一件装备不得被替换；旧装备回原格");
            var invalid = NativeInventoryService.Handle(db, new InventoryChangeRequest { characterId = id, itemId = 1002001, equip = true, equipSlot = -7, quantity = 1 });
            Check(!invalid.ok, "帽子不能穿入鞋子栏");
            character.equipslots = 2; CharactersDao.Save(db, character);
            InventoryDao.InsertItem(db, id, 1002002, 1, 2, 1);
            var full = NativeInventoryService.Handle(db, new InventoryChangeRequest { characterId = id, itemId = 1002001, equip = true, equipSlot = -1, quantity = 0 });
            Check(!full.ok && CharService.Load(db, id).inventory.Exists(x => x.itemRowId == secondHat && x.position == -1), "背包满时不得卸装或丢失装备");
            foreach (int item in new[] { 2010007, 2050001, 2050002, 2050003, 2050004, 4000000, 3010000, 5000000 }) InventoryDao.InsertItem(db, id, item, item / 1000000, 1, 2);
            db.Save();
            var server = GameServer.StartWith(db);
            foreach (var pair in new Dictionary<string, object> { ["Net"] = server.Transport, ["CharacterId"] = id, ["InGame"] = true, ["Character"] = CharService.Load(db, id) })
                typeof(GameSession).GetProperty(pair.Key).GetSetMethod(true).Invoke(null, new[] { pair.Value });
            GameSession.ApplyInventoryToProfile();
            var loop = host.AddComponent<GameLoop>(); var player = host.AddComponent<PlayerCtrl>(); loop.player = player; GameLoop.I = loop;
            player.hpMax = player.mpMax = 100; player.hp = 25;
            var diseases = NativeMobDebuffs.For(player);
            Check(NativeInventoryActions.TryUse(2010007) == null && player.hp == 55 && Profile.ItemCount(2010007) == 1, "统一双击入口须恢复苹果HP且扣一个");
            diseases.Apply(121, 1, 0, 60000); diseases.Apply(133, 1, 0, 60000);
            Check(NativeInventoryActions.TryUse(2050001) == null && !diseases.Has(121) && diseases.Has(133), "眼药水只解除黑暗，不能解除僵尸");
            diseases.Clear(); diseases.Apply(122, 1, 0, 60000); diseases.Apply(126, 1, 0, 60000);
            Check(NativeInventoryActions.TryUse(2050002) == null && !diseases.Has(122) && !diseases.Has(126), "补药解除虚弱和缓慢");
            diseases.Clear(); diseases.Apply(120, 1, 0, 60000); diseases.Apply(124, 1, 0, 60000);
            Check(NativeInventoryActions.TryUse(2050003) == null && !diseases.Has(120) && !diseases.Has(124), "圣水解除封印和诅咒");
            diseases.Clear(); diseases.Apply(125, 1, 0, 60000); diseases.Apply(133, 1, 0, 60000);
            Check(NativeInventoryActions.TryUse(2050004) == null && !diseases.Has(125) && diseases.Has(133), "万能药不得解除原端不能驱散的僵尸状态");
            foreach (int item in new[] { 4000000, 3010000, 5000000 })
            {
                int count = Profile.ItemCount(item);
                Check(NativeInventoryActions.TryUse(item) != null && Profile.ItemCount(item) == count, "未接入的特殊道具不得误扣：" + item);
            }
            player.hp = 0;
            Check(NativeInventoryActions.TryUse(2010007) != null && Profile.ItemCount(2010007) == 1, "死亡不得使用物品");
            Check(ItemCatalog.Name(5000000) == "褐色小猫" && !string.IsNullOrEmpty(ItemCatalog.Desc(5000000)), "宠物提示须读取String/Pet.img");
        }
        catch (Exception e) { Check(false, e.ToString()); }
        finally
        {
            GameServer.Stop(); GameLoop.I = previousLoop; UnityEngine.Object.DestroyImmediate(host);
            JsonUtility.FromJsonOverwrite(snapshot, data); overrideField.SetValue(null, previousOverride);
            if (File.Exists(path)) File.Delete(path);
        }
        Debug.Log("[InventoryCheck] passed=" + pass + " failed=" + fail);
        if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
    }

    public static void RunAuction()
    {
        pass = fail = 0;
        string path = Path.Combine(Path.GetTempPath(), "mapledash-auction-" + Guid.NewGuid() + ".json");
        try
        {
            var db = DbStore.Create(path);
            var character = new CharRecord { accountid = 1, name = "AuctionTest", level = 30,
                damageCap = 25000000, gm = 0 };
            int id = (int)CharactersDao.Insert(db, character);
            character.id = id;
            var server = GameServer.StartWith(db);
            var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public;
            foreach (var pair in new Dictionary<string, object> {
                ["Net"] = server.Transport, ["CharacterId"] = id, ["InGame"] = true,
                ["Character"] = new FullCharacter { record = character } })
                typeof(GameSession).GetProperty(pair.Key, flags).GetSetMethod(true).Invoke(null, new[] { pair.Value });
            Profile.Data.level = 30;
            server.ScriptValues.Handle(new ScriptValueRequest { characterId = id, name = "副本积分", value = "1234", write = true });
            server.ScriptValues.Handle(new ScriptValueRequest { characterId = id, name = "nameplate_title_series", value = "3", write = true });
            ScriptSource.ClearCache();
            var session = NpcSession.Talk(9900001);
            Check(session != null && session.LastError == null && session.Dialog.Mode == NpcDialogMode.Simple,
                "拍卖主菜单须使用真实服务端接口成功打开：" + session?.LastError);
            Check(session != null && session.Dialog.RawText.Contains("火影") && session.Dialog.RawText.Contains("1234"),
                "拍卖信息栏须读取真实称号系列和副本积分");
            session?.Reply(1, 22);
            Check(session != null && session.LastError == null && session.Dialog.RawText.Contains("50级"),
                "低于50级选择拍卖行须显示源脚本等级限制");
            session?.Close();
        }
        catch (Exception e) { Check(false, e.ToString()); }
        finally { GameServer.Stop(); if (File.Exists(path)) File.Delete(path); }
        Debug.Log("[AuctionCheck] passed=" + pass + " failed=" + fail);
        if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
    }

    // 逐个点开 9900001 主菜单里的每一条 #L，验证「点了真的有反应、且不抛脚本错误」。
    // 需要整套系统才能跑的功能（仓库 / 转生 / 保底抽奖 / MTS …）允许报「未迁移接口」，
    // 但除此之外的任何异常都算回归 —— 之前 自由市场 就是死在 player.saveLocationOnWarp 没绑。
    public static void RunMenu()
    {
        pass = fail = 0;
        string path = Path.Combine(Path.GetTempPath(), "mapledash-menu-" + Guid.NewGuid() + ".json");
        int savedMap = Profile.LastMapId;
        var errors = new List<string>();
        Application.LogCallback hook = (condition, stack, type) =>
        {
            if (type == LogType.Error && condition.StartsWith("[Script] ", StringComparison.Ordinal))
                errors.Add(condition);
        };
        try
        {
            var db = DbStore.Create(path);
            var character = new CharRecord { accountid = 1, name = "MenuTest", level = 200,
                damageCap = 25000000, gm = 0 };
            int id = (int)CharactersDao.Insert(db, character);
            character.id = id;
            var server = GameServer.StartWith(db);
            var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public;
            foreach (var pair in new Dictionary<string, object> {
                ["Net"] = server.Transport, ["CharacterId"] = id, ["InGame"] = true,
                ["Character"] = new FullCharacter { record = character } })
                typeof(GameSession).GetProperty(pair.Key, flags).GetSetMethod(true).Invoke(null, new[] { pair.Value });
            Profile.Data.level = 200;
            ScriptSource.ClearCache();
            Application.logMessageReceived += hook;

            var opener = NpcSession.Talk(9900001);
            Check(opener != null && opener.LastError == null, "拍卖主菜单须能打开：" + opener?.LastError);
            if (opener == null) return;
            var content = opener.Dialog.Content;
            var links = new List<int>();
            foreach (var run in content.Runs)
                if (run.Kind == DialogRunKind.Link && !links.Contains(run.Link)) links.Add(run.Link);
            Check(links.Count >= 25, "主菜单可点选项偏少：" + links.Count);

            int gaps = 0, broken = 0;
            var failed = new Dictionary<int, string>();
            foreach (int link in links)
            {
                NpcSession.CloseActive();
                errors.Clear();
                var session = NpcSession.Talk(9900001);
                if (session == null) { Check(false, "菜单项 " + link + " 重新打开主菜单失败"); continue; }
                string label = LinkLabel(content, link);
                session.Reply(1, link);
                NpcSession.CloseActive();
                string error = errors.Find(e => e.Contains("Unmigrated server API") || e.Contains("class not migrated"))
                    ?? (errors.Count > 0 ? errors[0] : null);
                if (error == null) { Debug.Log("[MenuCheck] OK   " + link + " " + label); continue; }
                // 两种「缺东西」都算已知缺口：缺服务端接口、缺整个 Java 类（仓库/转生/MTS/Boss成长…）。
                // 它们要么等整套系统，要么只能给玩家一句明确的「尚未接入」，不算回归。
                if (error.Contains("Unmigrated server API") || error.Contains("class not migrated"))
                {
                    gaps++;
                    failed[link] = label;
                    Debug.Log("[MenuCheck] GAP  " + link + " " + label + " :: " + error);
                    continue;
                }
                broken++;
                failed[link] = label;
                Check(false, "菜单项 " + link + " " + label + " 脚本异常：" + error);
            }
            // 这两项是纯传送，不依赖任何未迁移系统：一旦退化说明 savedlocations / warp 链路坏了。
            Check(!failed.ContainsKey(0), "自由市场必须可用（saveLocationOnWarp + warp）");
            Check(!failed.ContainsKey(1), "传送系统（万能传送）必须可用");
            Debug.Log("[MenuCheck] 主菜单 " + links.Count + " 项：正常 " + (links.Count - gaps - broken)
                      + "，待接系统 " + gaps + "，异常 " + broken);
            Debug.Log("[QuestNpcCheck] 通过 " + pass + " 项，失败 " + fail + " 项");
        }
        catch (Exception e) { Check(false, e.ToString()); }
        finally
        {
            Application.logMessageReceived -= hook;
            NpcSession.CloseActive();
            GameServer.Stop();
            Profile.LastMapId = savedMap;
            if (File.Exists(path)) File.Delete(path);
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }

    // Link 游标自己就带着可见文字（Kind=Link 的 run 同时是正文的一部分）
    static string LinkLabel(DialogContent content, int link)
    {
        foreach (var run in content.Runs)
            if (run.Kind == DialogRunKind.Link && run.Link == link) return (run.Text ?? "").Trim();
        return "";
    }

    static NpcSession ScriptSession(string source)
    {
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var session = (NpcSession)Activator.CreateInstance(typeof(NpcSession), flags, null,
            new object[] {2101, 0, false, false}, null);
        // Begin 有两个重载（Begin(src,name) 和 Begin(src,name,entry)），GetMethod 会抛 AmbiguousMatchException
        var begin = Array.Find(typeof(NpcSession).GetMethods(flags),
            m => m.Name == "Begin" && m.GetParameters().Length == 2);
        begin.Invoke(session, new object[] {source, "dialog-check"});
        return session;
    }

    static void CheckRepeatedTalkPresses()
    {
        var frame = typeof(NpcDialogUi).GetField("talkKeyFrame",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        int savedFrame = (int)frame.GetValue(null);
        bool savedEnabled = InputHub.Enabled;
        int replies = 0;
        var session = NpcSession.Conversation(2101, null, (current, mode) =>
        {
            replies++;
            if (replies == 3) current.Close();
            else current.SetDialog(replies == 1 ? NpcDialogMode.NextPrev : NpcDialogMode.Ok, "page" + replies);
        });
        session.SetDialog(NpcDialogMode.Next, "first");
        try
        {
            InputHub.Enabled = true;
            InputHub.ReleaseAllVirtual();
            NpcDialogUi.ConsumeTalkKeyFrame();
            Check(!NpcDialogUi.ContinueWithTalkKey() && replies == 0, "打开对话的同一按下不得跳过首页");
            // 编辑器同步自检不推进真实帧；仅将去重戳移到上一帧，模拟下一次采样。
            for (int i = 0; i < 3; i++)
            {
                frame.SetValue(null, Time.frameCount - 1);
                var key = KeyMap.Of(GameAction.NpcTalk);
                if (key != KeyCode.None) InputHub.SetVirtual(key, true);
                else InputHub.SetVirtualAction(GameAction.NpcTalk, true);
                Check(InputHub.Pressed(GameAction.NpcTalk) && NpcDialogUi.ContinueWithTalkKey() && replies == i + 1,
                    "第" + (i + 1) + "次虚拟对话键按下须推进一次（Next/NextPrev/OK）");
                Check(!NpcDialogUi.ContinueWithTalkKey() && replies == i + 1, "Update/GUI同帧不得重复推进");
                InputHub.EndFrame();
                Check(!InputHub.Pressed(GameAction.NpcTalk), "持续按住不得生成新的按下边沿");
                if (key != KeyCode.None) InputHub.SetVirtual(key, false);
                else InputHub.SetVirtualAction(GameAction.NpcTalk, false);
            }
            Check(session.Closed && !NpcSession.Blocking, "最后一次确定须释放对话会话");
        }
        finally
        {
            session.Close();
            InputHub.ReleaseAllVirtual();
            InputHub.Enabled = savedEnabled;
            frame.SetValue(null, savedFrame);
        }
    }

    static void CheckDialogProtocol()
    {
        var session = ScriptSession(@"
            var step = 0;
            function start() { cm.sendNext('next'); }
            function action(mode,type,selection) {
                if(mode != 1) { cm.dispose(); return; }
                step++;
                if(step == 1 && type == 0) cm.sendYesNo('yesno');
                else if(step == 2 && type == 1) cm.sendSimple('#L7#seven#l');
                else if(step == 3 && type == 4 && selection == 7) cm.sendAcceptDecline('accept');
                else if(step == 4 && type == 12) cm.sendGetNumber('number',10,1,100);
                else if(step == 5 && type == 3 && selection == 87) cm.sendGetText('text');
                else if(step == 6 && type == 2 && cm.getText() == '测试') cm.sendOk('done');
                else if(step == 7 && type == 0) cm.dispose();
                else cm.sendOk('WRONG');
            }");
        typeof(NpcSession).GetField("<Active>k__BackingField",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).SetValue(null, session);
        Check(NpcDialogUi.ContinueWithTalkKey(), "NPC 对话键须推进已打开的 Next 页");
        Check(!NpcDialogUi.ContinueWithTalkKey(), "同帧 Update 与 GUI 对话键不得重复回复");
        Check(session.Dialog.Mode == NpcDialogMode.YesNo, "继续按钮消息类型必须为0");
        var talkFrame = typeof(NpcDialogUi).GetField("talkKeyFrame",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        talkFrame.SetValue(null, Time.frameCount - 1);
        Check(NpcDialogUi.ContinueWithTalkKey(), "对话键确认是/否页默认是按钮");
        Check(session.Dialog.Mode == NpcDialogMode.Simple, "是/否消息类型必须为1");
        talkFrame.SetValue(null, Time.frameCount - 1);
        Check(NpcDialogUi.ContinueWithTalkKey(), "对话键确认原始#L选项且保留非零索引7");
        Check(session.Dialog.Mode == NpcDialogMode.AcceptDecline, "选项消息类型必须为4且保留索引");
        talkFrame.SetValue(null, Time.frameCount - 1);
        Check(NpcDialogUi.ContinueWithTalkKey(), "对话键确认接受页默认接受按钮");
        Check(session.Dialog.Mode == NpcDialogMode.GetNumber, "接受/拒绝消息类型必须为12");
        session.Reply(1, 87);
        Check(session.Dialog.Mode == NpcDialogMode.GetText, "数字输入消息类型必须为3且传递输入值");
        session.Dialog.Input = "测试";
        session.Reply(1);
        Check(session.Dialog.RawText == "done", "文本输入消息类型必须为2且cm.getText保留输入");
        session.Reply(1);
        Check(session.Closed && !session.Dialog.Visible, "确定必须继续脚本；dispose不留旧页");

        session = ScriptSession("function start(){cm.sendOk('last');cm.dispose();}");
        Check(session.Disposed && session.Dialog.Visible, "sendOk+dispose必须保留最后一页");
        session.Reply(1);
        Check(session.Closed, "已dispose的最后一页应可关闭");
        session = ScriptSession("function start(){cm.sendNext('a');} function action(m,t,s){cm.dispose();}");
        session.Cancel();
        Check(session.Closed, "取消必须关闭并释放会话");
        int callbacks = 0;
        session = NpcSession.Choose(2101, "#L0#选择#l", index => callbacks++);
        session.Reply(1, 0);
        session.Reply(1, 0);
        Check(callbacks == 1 && NpcSession.Active == null, "一次选择只能执行一次，重复响应不得重复发放动作");
    }

    static void CheckBirthRoutes()
    {
        string path = Path.Combine(Path.GetTempPath(), "mapledash-birth-" + Guid.NewGuid() + ".json");
        try
        {
            var db = DbStore.Create(path);
            int account = AccountsDao.AutoRegister(db, "BirthCheck", "test");
            var transport = GameServer.StartWith(db).Transport;
            for (int group = 0; group <= 2; group++)
            {
                bool aran = group == 2;
                var result = CharFactory.CreateCharacter(db, account, 0, "Birth" + group, group,
                    aran ? 20100 : 20000, 30300, 0, 0, aran ? 1042167 : 1040002,
                    aran ? 1062115 : 1060002, aran ? 1072383 : 1072001, aran ? 1442079 : 1302000, 0);
                Check(result.code == CharFactory.Ok, "真实建号职业群 " + group);
                var full = transport.CharSelect(new CharSelectRequest { accountId = account, characterId = result.characterId });
                int expectedMap = group == 0 ? 130030000 : group == 1 ? 10000 : 914000000;
                int expectedJob = group == 0 ? 1000 : group == 1 ? 0 : 2000;
                int guide = group == 0 ? 4161047 : group == 1 ? 4161001 : 4161048;
                Check(full.ok && full.character.record.map == expectedMap && full.character.record.job == expectedJob &&
                    full.character.inventory.Exists(item => item.itemId == guide), "建号→选角保留源端地图/职业/指南：" + group);
                Debug.Log("[BirthTrace] group=" + group + " map=" + full.character.record.map + " job=" + full.character.record.job + " guide=" + guide);
                if (group == 1) CheckWelcomePersistence(transport, full.character, path);
            }
            db.Table("game_config").Update(row => (string)row["config_code"] == "use_beidou_beginner_map",
                new DbRow { ["config_value"] = "true" });
            int secondAccount = AccountsDao.AutoRegister(db, "BirthCheck2", "test");
            var beidou = CharFactory.CreateCharacter(db, secondAccount, 0, "BirthMap4", 1,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            var selected = transport.CharSelect(new CharSelectRequest { accountId = secondAccount, characterId = beidou.characterId });
            Check(selected.ok && selected.character.record.map == 4, "北斗新手地图配置必须让冒险家出生在4");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    static void CheckWelcomePersistence(ITransport transport, FullCharacter character, string path)
    {
        string snapshot = JsonUtility.ToJson(Profile.Data);
        int savedMap = Profile.LastMapId;
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public;
        var saved = new Dictionary<string, object>();
        try
        {
            foreach (var pair in new Dictionary<string, object> {
                ["Net"] = transport, ["CharacterId"] = (int)character.record.id, ["InGame"] = true,
                ["Character"] = character })
            {
                var property = typeof(GameSession).GetProperty(pair.Key, flags);
                saved[pair.Key] = property.GetValue(null);
                property.GetSetMethod(true).Invoke(null, new[] { pair.Value });
            }
            Profile.Data.level = 1;
            Profile.Data.SetJob(0);
            Profile.LastMapId = 10000;
            Check(WelcomeTutorial.Available, "真实新建角色必须有问候入口");
            WelcomeTutorial.Open();
            var dialog = NpcSession.Active;
            for (int i = 0; i < 4; i++) dialog?.Reply(1);
            dialog?.Reply(1);
            var reopened = new NativeScriptValues(DbStore.Open(path));
            Check(reopened.Handle(new ScriptValueRequest { characterId = (int)character.record.id,
                name = WelcomeTutorial.StateKey }).value == "1", "接受问候状态必须实际写盘并可重载");
            Profile.LastMapId = 4;
            NpcField.Bind(FieldCatalog.GetMap(4));
            NpcField.Interact(2104);
            var deliveryMenu = NpcSession.Active;
            Check(WelcomeTutorial.CanComplete && NpcField.MarkerOf(2104).complete &&
                deliveryMenu != null && deliveryMenu.Dialog.Mode == NpcDialogMode.Simple,
                "截图问候接取后，昨日酣睡必须显示可完成任务提示及混合菜单");
            int welcomeDelivery = NpcField.ChoicesFor(2104).FindIndex(c => c.welcome && c.complete);
            int beforeWelcomeGold = Profile.gold;
            deliveryMenu?.Reply(1, welcomeDelivery);
            Check(NpcSession.Active != null && NpcSession.Active.Dialog.Mode == NpcDialogMode.Ok &&
                NpcSession.Active.Dialog.RawText.Contains("10000") && Profile.LastMapId == 4,
                "交付问候须先显示奖励页，不得直接传送");
            NpcSession.Active?.Reply(1);
            Check(NpcSession.Active == null && !WelcomeTutorial.CanComplete &&
                Profile.gold == beforeWelcomeGold + 10000 && !NpcField.MarkerOf(2104).complete,
                "奖励确定必须发放10000金币、关闭会话并移除完成提示");
            WelcomeTutorial.Complete();
            Check(Profile.gold == beforeWelcomeGold + 10000, "已交付问候不得重复领取金币");
            NpcField.Interact(2104);
            NpcSession.CloseActive();
            Check(new NativeScriptValues(DbStore.Open(path)).Handle(new ScriptValueRequest {
                characterId = (int)character.record.id, name = WelcomeTutorial.StateKey }).value == "2",
                "交付完成状态必须落盘");
            Profile.LastMapId = 10000;
            Check(!WelcomeTutorial.Available, "已接受的同一角色回到10000不得重复出现问候");
        }
        finally
        {
            NpcSession.CloseActive();
            foreach (var pair in saved)
                typeof(GameSession).GetProperty(pair.Key, flags).GetSetMethod(true).Invoke(null, new[] { pair.Value });
            JsonUtility.FromJsonOverwrite(snapshot, Profile.Data);
            Profile.LastMapId = savedMap;
            NpcField.InvalidateMarkers();
        }
    }

    static void CheckNewCharacterMarkers()
    {
        var data = Profile.Data;
        string snapshot = JsonUtility.ToJson(data);
        var previousMap = NpcField.Map;
        int previousMapId = Profile.LastMapId;
        try
        {
            data.level = 1;
            data.SetJob(0);
            data.questStarted.Clear();
            data.questCompleted.Clear();
            data.itemIds.Clear();
            data.itemCounts.Clear();
            data.AddItem(CharFactory.ItemBeginnersGuide, 1);
            Profile.LastMapId = CharFactory.MapMushroomTown;
            FieldCatalog.Ensure();
            var map = FieldCatalog.GetMap(CharFactory.MapMushroomTown);
            NpcField.Bind(map);
            Check(map.npcs.Exists(n => n.id == 2101), "出生地图缺少希娜");
            Check(map.npcs.Exists(n => n.id == 2101 && n.x == 130 && n.y == 305),
                "希娜应落在 fh51 的 y=305，而不是出生 y=293");
            Check(map.npcs.Exists(n => n.id == 2100 && n.y == 125), "已对齐 NPC 不应产生额外偏移");
            Check(FieldCatalog.GetMap(20000).npcs.Exists(n => n.id == 2000 && n.y == 65),
                "罗杰应落在 fh7 的 y=65，而不是出生 y=58");
            Check(NpcField.MarkerOf(2101).start, "1 级新角色应看到希娜的可接任务标记");
            Check(QuestLogic.CanStart(QuestCatalog.Get(1031), 2101), "1 级新角色应可接希娜和莎丽");
            NpcField.Interact(2101);
            Check(NpcSession.Active != null && NpcSession.Active.Dialog.Mode == NpcDialogMode.Simple,
                "点击NPC应显示任务/普通对话选择，而非自动接取第一个任务");
            Check(!data.questStarted.Contains(1031), "打开NPC菜单不得自动接任务");
            NpcSession.CloseActive();
            var confirmation = NpcSession.TalkQuest(QuestCatalog.Get(1031), 2101, false);
            Check(confirmation != null && confirmation.Dialog.Mode == NpcDialogMode.Next && confirmation.Dialog.RawText.Contains("新来的冒险家"),
                "无脚本任务必须读取Say.img的首段对白");
            confirmation?.Reply(1);
            confirmation?.Reply(0);
            Check(confirmation != null && confirmation.Dialog.RawText.Contains("新来的冒险家"), "返回按钮必须回到任务上一页");
            confirmation?.Reply(1);
            confirmation?.Reply(1);
            confirmation?.Reply(1);
            Check(confirmation != null && confirmation.Dialog.Mode == NpcDialogMode.AcceptDecline && !data.questStarted.Contains(1031),
                "必须读完Say主对白并确认后才能接任务");
            confirmation?.Reply(0);
            Check(!data.questStarted.Contains(1031) && confirmation != null && confirmation.Dialog.RawText.Contains("心理准备"),
                "拒绝任务必须显示Say/no且不得改状态");
            confirmation?.Reply(1);
            Check(NpcSession.Active == null, "拒绝对白结束必须释放会话");
            var welcomeAlerts = new List<NpcField.QuestChoice>(NpcField.QuestAlerts());
            int welcomeIndex = welcomeAlerts.FindIndex(c => c.welcome);
            Check(welcomeIndex >= 0 && welcomeAlerts.Count == 1, "一级map10000必须有截图问候灯泡，不能混入过期活动");
            NpcField.ShowQuestAlerts();
            Check(NpcSession.Active != null && NpcSession.Active.SpeakerName == "冒险岛运营员", "灯泡菜单须显示截图的运营员名字");
            NpcSession.Active?.Reply(1, welcomeIndex);
            var welcome = NpcSession.Active;
            Check(welcome != null && welcome.SpeakerName == "昨日小睡" && welcome.Dialog.RawText.Contains("引导者"), "选择问候后须显示截图第一页");
            welcome?.Reply(1);
            Check(welcome != null && welcome.Dialog.Mode == NpcDialogMode.NextPrev && welcome.Dialog.RawText.Contains("命运的骰子"), "问候第二页须可返回");
            welcome?.Reply(0);
            Check(welcome != null && welcome.Dialog.RawText.Contains("引导者"), "问候上一页须返回第一页");
            for (int step = 0; step < 3; step++) welcome?.Reply(1);
            Check(welcome != null && welcome.Dialog.Mode == NpcDialogMode.AcceptDecline && welcome.Dialog.RawText.Contains("青苹果乐园"), "三页后须询问是否返回青苹果乐园");
            welcome?.Reply(0);
            Check(Profile.LastMapId == 10000 && NpcSession.Active == null && WelcomeTutorial.Available, "拒绝问候不得传送或移除入口");
            WelcomeTutorial.Open();
            welcome = NpcSession.Active;
            for (int step = 0; step < 4; step++) welcome?.Reply(1);
            Check(Profile.LastMapId == 4 && welcome != null && welcome.Dialog.Mode == NpcDialogMode.Ok && welcome.Dialog.RawText.Contains("昨日酣睡"),
                "接受后必须到截图Training camp入口map4并保留最后一页提示");
            Check(!WelcomeTutorial.Available, "离开10000后不可再显示问候入口");
            welcome?.Reply(1);
            Check(NpcSession.Active == null, "最终确定必须关闭问候会话");
            Profile.LastMapId = CharFactory.MapMushroomTown;
            data.level = 2;
            NpcField.InvalidateMarkers();
            Check(QuestCatalog.Get(1029).autoStart && QuestCatalog.Get(1030).autoComplete, "必须读取QuestInfo的远程任务标记");
            Check(!QuestLogic.CanStart(QuestCatalog.Get(1047), 2006), "fieldEnter/0限制必须生效，不能显示999999999地图任务");
            Check(NpcField.QuestAlerts().Count == 0, "无物品/进度的出生角色不得显示过期活动灯泡");
            Check(!QuestLogic.NotificationDateAvailable(QuestCatalog.Get(10110).startReq, new DateTime(2026, 10, 28)),
                "灯泡必须过滤已结束的邀请，不能套用服务端历史日期豁免");
            var dateReq = new QuestRequirement { startDate = "2026102812", endDate = "2026102813" };
            Check(!QuestLogic.NotificationDateAvailable(dateReq, new DateTime(2026, 10, 28, 11, 59, 59)) &&
                QuestLogic.NotificationDateAvailable(dateReq, new DateTime(2026, 10, 28, 12, 0, 0)) &&
                !QuestLogic.NotificationDateAvailable(dateReq, new DateTime(2026, 10, 28, 13, 0, 1)), "灯泡日期包含边界比较");
            // 原端远程任务脚本：确认四页后传送蘑菇城堡，且 warp 后继续执行 forceStartQuest。
            data.level = 30;
            data.SetJob(110);
            NpcField.InvalidateMarkers();
            var remote = new List<NpcField.QuestChoice>(NpcField.QuestAlerts()).Find(c => c.quest.id == 2300);
            Check(remote != null, "异地图 NPC 的 autoStart 任务 2300 必须出现在灯泡");
            if (remote != null)
            {
                NpcField.OpenQuest(remote);
                var warpSession = NpcSession.Active;
                for (int step = 0; step < 4 && warpSession != null && !warpSession.Closed; step++) warpSession.Reply(1);
                Check(warpSession != null && warpSession.LastError == null && Profile.LastMapId == 106020000,
                    "源端 quest/2300.js 确认后必须传送到 106020000");
                Check(data.questStarted.Contains(2300), "warp 后脚本仍需执行 forceStartQuest");
                NpcSession.CloseActive();
            }
            // BeginnerCreator 的配置分支：map4/life/0=2104，原始 JS 第二次调用 warp(1)。
            data.SetJob(0);
            data.level = 1;
            Profile.LastMapId = CharFactory.MapBeidouBeginner;
            NpcField.Bind(FieldCatalog.GetMap(CharFactory.MapBeidouBeginner));
            Check(NpcField.Map.npcs.Exists(n => n.id == 2104), "map4 必须引用原端 NPC2104");
            NpcField.Interact(2104);
            var departure = NpcSession.Active;
            Check(departure != null && departure.Dialog.Mode != NpcDialogMode.Simple &&
                !departure.Dialog.RawText.Contains("HL_LADDER"),
                "map4 NPC 点击须直接打开对白，不得插入 HL_LADDER 内部名称菜单");
            Check(departure != null && departure.ScriptName == "2104" && departure.Dialog.RawText.Contains("冒险旅途"),
                "IMG 的 HL_LADDER 无命名脚本时须按源端回退 npc/2104.js 原始对白");
            departure?.Reply(1);
            Check(departure != null && departure.LastError == null && Profile.LastMapId == 1,
                "源端出生图 NPC2104 确认对白必须传送 map1");
            NpcSession.CloseActive();
            var training = FieldCatalog.GetMap(1);
            NpcField.Bind(training);
            Check(training.npcs.Exists(n => n.id == 2100), "map1 必须引用 NPC2100");
            Check(training.fieldType == 82, "青苹果教程地图必须保留原端fieldType82");
            var tutorialIds = WzCharacter.TutorialAppearance(new[] {
                "00002000", "00012000", "00020000", "00030000", "01040002", "01302000" });
            Check(Array.IndexOf(tutorialIds, "01002562") >= 0 && Array.IndexOf(tutorialIds, "01052081") >= 0 &&
                Array.IndexOf(tutorialIds, "01040002") < 0 && tutorialIds[tutorialIds.Length - 1] == "01302000",
                "教程外观必须覆盖帽服并保留武器，不改真实装备");
            var tutorialSprite = WzCharacter.Appearance("quest-check-field82", tutorialIds);
            Check(tutorialSprite != null && tutorialSprite.rect.width > 0 && tutorialSprite.rect.height > 0,
                "青苹果外观须能使用原始IMG实际合成");
            if (tutorialSprite != null) File.WriteAllBytes("/tmp/mapledash-field82-appearance.png", tutorialSprite.texture.EncodeToPNG());
            NpcField.Interact(2100);
            Check(NpcSession.Active != null && NpcSession.Active.Dialog.Content.Selections.FindAll(c => c.Index >= 0).Count == 1,
                "莎丽首次菜单只能有一个任务，不得额外插入普通对白选项");
            var instructorChoices = NpcField.ChoicesFor(2100);
            int trainingQuestIndex = instructorChoices.FindIndex(c => c.quest.id == 30001 && !c.complete);
            Check(trainingQuestIndex >= 0, "map1 NPC2100 须提供原端新手任务30001");
            if (trainingQuestIndex >= 0)
            {
                NpcSession.Active?.Reply(1, trainingQuestIndex);
                var trainingQuest = NpcSession.Active;
                trainingQuest?.Reply(1);
                trainingQuest?.Reply(1);
                Check(trainingQuest != null && trainingQuest.LastError == null && data.questStarted.Contains(30001)
                    && Profile.LastMapId == 1, "原端30001接取后留在map1，指引参观职业并寻找罗杰");
                trainingQuest?.Reply(1);
            }
            NpcSession.CloseActive();
            NpcField.Interact(2100);
            // 若还有其它可接任务，先选择普通对白。
            if (NpcSession.Active != null && NpcSession.Active.Dialog.Mode == NpcDialogMode.Simple)
                NpcSession.Active.Reply(1, NpcField.ChoicesFor(2100).Count);
            var instructor = NpcSession.Active;
            Check(instructor != null && instructor.ScriptName == "2100" && instructor.Dialog.RawText.Contains("训练课程"),
                "有 speak 气泡的 NPC2100 仍须执行原始对白脚本");
            instructor?.Reply(1);
            Check(instructor != null && instructor.LastError == null && instructor.Dialog.RawText.Contains("射手村"),
                "map1 教练继续按钮须进入原端职业说明");
            instructor?.Reply(1);
            Check(Profile.LastMapId == 1 && NpcSession.Active == null,
                "源端 NPC2100 教程结束不传送，必须保留 map1");
            var trainingExit = training.Portal("next00");
            Check(trainingExit != null && trainingExit.tm == 2 && trainingExit.targetName == "front00",
                "map1 next00 必须沿原端地图引用通往 map2/front00");
            NpcField.Bind(map);
            Profile.LastMapId = CharFactory.MapMushroomTown;
            data.SetJob(0);
            data.level = 11;
            NpcField.InvalidateMarkers();
            Check(!QuestLogic.CanStart(QuestCatalog.Get(1031), 2101), "1031 的 lvmax=10 未生效");
            Check(QuestCatalog.Get(2000).startReq.level == 10, "2000 的 lvmin=10 未读取");
            var session = NpcSession.TalkQuest(QuestCatalog.Get(1021), 2000, false);
            Check(session != null && session.LastError == null && session.Dialog.Visible,
                "罗杰任务脚本未产生对话");
            session?.Close();
            Check(WzAssets.Frames(WzAssets.Node("UI/UIWindow.img/QuestIcon/0")).Count > 0,
                "缺少 NPC 原生任务提示动画");
            Check(WzAssets.Frames(WzAssets.Node("Effect/BasicEff.img/QuestAlert/Default")).Count > 0,
                "缺少角色头顶灯泡动画");
        }
        finally
        {
            NpcSession.CloseActive();
            JsonUtility.FromJsonOverwrite(snapshot, data);
            NpcField.Bind(previousMap);
            Profile.LastMapId = previousMapId;
        }
    }

    static void CheckAllMapQuestLinks()
    {
        FieldCatalog.Ensure();
        int maps = 0, npcs = 0, linked = 0, errors = 0, settled = 0, matchesCy = 0;
        foreach (var entry in FieldCatalog.Maps)
        {
            try
            {
                // 只读 life，不生成场景、不读取怪物或贴图。
                var root = NativeMapView.MapRoot(entry.Key);
                maps++;
                var life = root.Get("life");
                if (life == null) continue;
                foreach (var npc in life.Children)
                {
                    if (npc.Text("type") != "n" || npc.Int("hide") != 0) continue;
                    int id = int.Parse(npc.Text("id"));
                    npcs++;
                    float groundY = NativeMapView.NpcGroundY(root, npc);
                    if (Mathf.Abs(groundY - npc.Int("y")) > .01f) settled++;
                    if (Mathf.Abs(groundY - npc.Int("cy")) < .01f) matchesCy++;
                    Check(!float.IsNaN(groundY) && !float.IsInfinity(groundY), "NPC 落脚坐标非法 " + entry.Key + "/" + id);
                    foreach (int qid in QuestCatalog.ForNpc(id))
                    {
                        var quest = QuestCatalog.Get(qid);
                        Check(quest.startReq.npc == id || quest.completeReq.npc == id,
                            "NPC 与任务索引不一致：" + id + "/" + qid);
                    }
                    if (QuestCatalog.ForNpc(id).Count > 0) linked++;
                }
            }
            catch (Exception e)
            {
                if (errors++ < 10) Debug.LogError("[QuestNpcCheck] 地图 " + entry.Key + ": " + e.Message);
            }
        }
        Check(errors == 0, "地图 NPC/任务关联读取失败 " + errors + " 张");
        Debug.Log("[QuestNpcCheck] 全地图扫描 maps=" + maps + " visibleNpcs=" + npcs +
            " questLinkedNpcs=" + linked + " settledNpcs=" + settled + " matchesCy=" + matchesCy + " errors=" + errors);
    }

    // ————— 打包通道 —————
    static void CheckScriptPack()
    {
        var asset = Resources.Load<TextAsset>("Scripts/npc_scripts");
        Check(asset != null, "缺少 Resources/Scripts/npc_scripts.json（先跑 tools/sync_npc_scripts.py）");
        if (asset == null) return;

        Dictionary<string, object> map = null;
        try { map = MiniJson.Parse(asset.text) as Dictionary<string, object>; }
        catch (Exception e) { Check(false, "脚本包 JSON 解析失败：" + e.Message); return; }
        Check(map != null, "脚本包不是 JSON 对象");
        if (map == null) return;

        int npc = 0, quest = 0, broken = 0;
        var brokenKeys = new List<string>();
        foreach (var kv in map)
        {
            string body = kv.Value as string;
            if (string.IsNullOrEmpty(body)) { broken++; if (brokenKeys.Count < 5) brokenKeys.Add(kv.Key + "(空)"); continue; }
            if (kv.Key.StartsWith("npc/", StringComparison.Ordinal)) npc++;
            else if (kv.Key.StartsWith("quest/", StringComparison.Ordinal)) quest++;
            else continue;
            try { new JsEngine(body, kv.Key); }
            catch (Exception e)
            {
                broken++;
                if (brokenKeys.Count < 5) brokenKeys.Add(kv.Key + " :: " + e.Message);
            }
        }

        Debug.Log("[QuestNpcCheck] 脚本包 npc=" + npc + " quest=" + quest + " 解析失败=" + broken);
        Check(npc + quest > 1100, "脚本条数偏少：npc=" + npc + " quest=" + quest);
        Check(broken == 0, "无法解析的脚本过多：" + broken + " 条，例如 " + string.Join(" | ", brokenKeys));
    }

    // ————— Quest.wz —————
    static void CheckQuestCatalog()
    {
        QuestCatalog.Ensure();
        Check(QuestCatalog.Ready, "Quest.wz 未加载");
        if (!QuestCatalog.Ready) return;

        Debug.Log("[QuestNpcCheck] Quest.wz 任务 " + QuestCatalog.QuestCount + " 个");
        Check(QuestCatalog.QuestCount > 3000, "任务数量偏少：" + QuestCatalog.QuestCount);

        var q = QuestCatalog.Get(1021);       // 罗杰和苹果：有开始/结束脚本，用来对拍整条链路
        Check(q != null, "任务 1021 读不到");
        if (q != null)
        {
            Check(!string.IsNullOrEmpty(q.name), "任务 1021 没有名字");
            Check(!string.IsNullOrEmpty(q.startScript), "任务 1021 没有 startscript");
            Debug.Log("[QuestNpcCheck] 1021 " + q.name + " start=" + q.startScript + " end=" + q.endScript
                      + " npc=" + q.startReq.npc + " lv>=" + q.startReq.level);
        }
    }

    static void CheckScriptLookup()
    {
        Check(ScriptSource.Load("npc", 1002000) != null, "npc/1002000 取不到脚本");
        Check(ScriptSource.Load("quest", 1021) != null, "quest/1021 取不到脚本");
        Check(ScriptSource.Load("npc", 99999999) == null, "不存在的 NPC 应该返回 null");

        string s = ScriptSource.Load("npc", 1002000);
        if (s != null) Check(s.Contains("function start"), "npc/1002000 内容异常");
    }

    // ————— 对白标记 —————
    static void CheckDialogText()
    {
        var c = DialogText.Parse("#b你好#k\r\n#L0#选项一#l\r\n#L1#选项二#l");
        var links = c.Runs.FindAll(r => r.Kind == DialogRunKind.Link);
        Check(links.Count == 2 && links[0].Link == 0 && links[1].Link == 1, "行内选项号解析不对");
        Check(c.Body.Contains("你好"), "正文丢了");
        Check(!c.Body.Contains("#L"), "#L 标记没有被吃掉");
        // 选项必须留在原位置（原端是行内链接），不能被抽出来堆到正文末尾
        Check(c.Body.IndexOf("选项一", System.StringComparison.Ordinal) >
              c.Body.IndexOf("你好", System.StringComparison.Ordinal), "选项没有按原位置留在正文流里");
        Check(c.Body.IndexOf("选项一", System.StringComparison.Ordinal) <
              c.Body.IndexOf("选项二", System.StringComparison.Ordinal), "选项顺序乱了");

        var blue = DialogText.Parse("#b蓝#k普通");
        Check(blue.Runs.Count == 2 && blue.Runs[0].Text == "蓝" && blue.Runs[0].Color == DialogText.Blue,
            "#b 颜色状态没有生效");
        Check(blue.Runs.Count == 2 && blue.Runs[1].Text == "普通" && blue.Runs[1].Color == DialogText.Ink,
            "#k 没有回到默认色");

        var bold = DialogText.Parse("#e粗#n常");
        Check(bold.Runs.Count == 2 && bold.Runs[0].Bold && !bold.Runs[1].Bold, "#e/#n 粗体开关不对");

        Check(DialogText.Parse("#h #").Body == Profile.playerName, "角色名标记未解析");
        Check(DialogText.Parse("#c4000000#").Body == Profile.ItemCount(4000000).ToString(), "道具数量标记应为数量而非颜色");

        // #i/#v 是道具图标，不是文字；取不到图才退回名字
        var imageLink = DialogText.Parse("#L7##fUI/UIWindow.img/UtilDlgEx/list0##l");
        Check(imageLink.Runs.Count == 2 && imageLink.Runs[0].Link == 7 && imageLink.Runs[1].Link == 7, "#L 内图片必须保留点击索引");
        Check(DialogFlow.ContentWidth(529f) == 341f && DialogFlow.BodyHeight(50f) == 120f &&
              DialogFlow.BodyHeight(400f) == 240f, "普通对话框必须采用原端测量宽与正文区上下限");
        var icon = DialogText.Parse("#i04000000#");
        Check(icon.Runs.Count == 1 && icon.Runs[0].Kind == DialogRunKind.Icon, "#i 应该产生图标节点");
        Check(!string.IsNullOrEmpty(icon.Runs[0].Art), "#i 的道具图标路径没有解析出来");
    }

    // ————— 任务规则 —————
    static void CheckBeginnerNpcBindings()
    {
        var data = Profile.Data;
        string snapshot = JsonUtility.ToJson(data);
        try
        {
            data.level = 1; data.jobId = 0;
            data.questStarted.Clear(); data.questCompleted.Clear();
            Check(QuestCatalog.ForNpc(2004).Contains(1035), "透德原始任务1035必须进入NPC索引");
            Check(QuestCatalog.ForNpc(12100).Contains(1041), "麦加原始任务1041必须进入NPC索引");
            Check(NpcField.ChoicesFor(2004).Exists(c => c.quest?.id == 1035), "一级初心者透德显示1035");
            NpcField.Interact(2004);
            Check(NpcSession.Active != null && NpcSession.Active.Dialog.Mode == NpcDialogMode.Simple,
                "透德即使speak且无NPC JS也必须打开任务菜单");
            NpcSession.CloseActive();
            data.questStarted.Add(1040);
            Check(NpcField.ChoicesFor(12100).Exists(c => c.quest?.id == 1041), "1040进行中时麦加显示1041");
            NpcField.Interact(12100);
            Check(NpcSession.Active != null && NpcSession.Active.Dialog.Mode == NpcDialogMode.Simple,
                "麦加即使speak且无NPC JS也必须打开任务菜单");
            NpcSession.CloseActive();
            // The screenshot NPCs are different instances, despite sharing the name 透德.
            // Keep the real level/progress fixture separate from the other-map regression above.
            data.level = 2;
            data.questStarted.Clear(); data.questCompleted.Clear();
            data.questStarted.Add(1034);
            data.questCompleted.AddRange(new[] { 30001, 1021, 1032, 1033 });
            Check(QuestCatalog.ForNpc(9101002).Contains(8142), "截图训练图透德索引必须包含原始8142");
            Check(QuestCatalog.Get(8142).startReq.npc == 9101002 &&
                  QuestCatalog.Get(8142).completeReq.npc == 9101003, "训练图8142必须保留原始接取和交付NPC");
            Check(ScriptSource.Load("npc", 9101001) != null, "截图彼特原始训练结束脚本必须存在");
            data.level = 11;
            Check(!NpcField.ChoicesFor(2004).Exists(c => c.quest?.id == 1035), "透德1035遵循lvmax10");
            Check(!NpcField.ChoicesFor(12100).Exists(c => c.quest?.id == 1041), "麦加1041遵循lvmax10");
        }
        finally { NpcSession.CloseActive(); JsonUtility.FromJsonOverwrite(snapshot, data); }
    }

    static void CheckTrainingQuestProgress()
    {
        var data = Profile.Data;
        string snapshot = JsonUtility.ToJson(data);
        try
        {
            data.level = 2; data.jobId = 0;
            data.questStarted.Clear(); data.questCompleted.Clear(); data.questProgress.Clear();
            data.itemIds.Clear(); data.itemCounts.Clear();
            var quest = QuestCatalog.Get(8142);
            Check(!QuestLogic.CanStart(quest, 9101002), "训练图8142不得擅自绕过原始1042003条件");
            data.SetQuestStarted(8142);
            NpcField.InvalidateMarkers();
            Check(NpcField.MarkerOf(9101003).progress, "训练图8142进行中须在交付NPC显示原图1");
            Check(!NpcField.MarkerOf(9101002).progress && !NpcField.MarkerOf(9101001).progress,
                "任务进行中提示不得串到接取NPC或同名脚本NPC");
            var choice = NpcField.ChoicesFor(9101003).Find(c => c.quest?.id == 8142);
            Check(choice != null && choice.progress && !choice.complete, "交付NPC须提供本NPC进行中的任务");
            var session = NpcSession.TalkQuest(quest, 9101003, true);
            Check(session != null && session.Dialog.Mode == NpcDialogMode.Ok &&
                  session.Dialog.Content.Body.Contains("还没完成透德的请求"), "未收齐物品须读原端stop/item对白");
            session.Reply(1);
            Check(Profile.QuestStatus(8142) == PlayerProfileData.QuestStartedState, "未完成对白不能发奖或完成任务");
            data.AddItem(4000300, 1); data.AddItem(4000301, 1);
            session = NpcSession.TalkQuest(quest, 9101003, true);
            Check(session != null && session.Dialog.Content.Body.Contains("先攻击(Ctrl)"),
                "物品齐但击杀不足须读原端stop/mob对白");
            NpcSession.CloseActive();
            QuestLogic.OnMobKilled(9409000);
            QuestLogic.OnMobKilled(9409000);
            Check(Profile.QuestProgressInt(8142, "9409000") == 1, "完成条件里的击杀须计数且按原端封顶");
            Check(!QuestLogic.CanComplete(quest, 9101003), "不能漏掉第二种怪物条件");
            QuestLogic.OnMobKilled(9409001);
            Check(QuestLogic.CanComplete(quest, 9101003) && NpcField.MarkerOf(9101003).complete,
                "真实训练怪击杀完毕须进入可交付状态并立即刷新标记");
            data.questStarted.Remove(8142);
            data.questProgress.Clear();
            QuestLogic.OnMobKilled(9409000);
            Check(Profile.QuestProgressInt(8142, "9409000") == 0, "未接任务不得累计击杀");
        }
        finally { NpcSession.CloseActive(); JsonUtility.FromJsonOverwrite(snapshot, data); NpcField.InvalidateMarkers(); }
    }

    static void CheckQuestRules()
    {
        // JobId 映射必须和服务端 Quest.java 的 Job 枚举一致，否则 level/job 条件全错
        Check(QuestLogic.JobId("beginner") == 0, "JobId(beginner) != 0");
        Check(QuestLogic.JobId("warrior") == 100, "JobId(warrior) != 100");
        Check(QuestLogic.JobId("mage") == 200, "JobId(mage) != 200");
        Check(QuestLogic.JobId("archer") == 300, "JobId(archer) != 300");

        var q = QuestCatalog.Get(1021);
        if (q == null) return;
        string reason;
        // Explicit fixture: the real saved profile may already have accepted Roger's quest.
        bool started = Profile.Data.questStarted.Remove(q.id);
        bool completed = Profile.Data.questCompleted.Remove(q.id);
        try { Check(!QuestLogic.CanComplete(q, q.completeReq.npc, out reason), "未接取的任务却判定可完成"); }
        finally
        {
            if (started) Profile.Data.questStarted.Add(q.id);
            if (completed) Profile.Data.questCompleted.Add(q.id);
        }
    }
}
