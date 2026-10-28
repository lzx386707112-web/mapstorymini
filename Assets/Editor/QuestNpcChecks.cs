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
        pass = fail = 0;

        CheckScriptPack();
        CheckQuestCatalog();
        CheckScriptLookup();
        CheckDialogText();
        CheckQuestRules();
        CheckDialogProtocol();
        CheckNewCharacterMarkers();
        CheckAllMapQuestLinks();

        Debug.Log("[QuestNpcCheck] 通过 " + pass + " 项，失败 " + fail + " 项");
        if (fail > 0 && Application.isBatchMode) EditorApplication.Exit(1);
    }

    // 独立进程、临时数据库；验证真实 NpcApi，而非宽松 mock。
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
        session.Reply(1);
        Check(session.Dialog.Mode == NpcDialogMode.YesNo, "继续按钮消息类型必须为0");
        session.Reply(1);
        Check(session.Dialog.Mode == NpcDialogMode.Simple, "是/否消息类型必须为1");
        session.Reply(1, 7);
        Check(session.Dialog.Mode == NpcDialogMode.AcceptDecline, "选项消息类型必须为4且保留索引");
        session.Reply(1);
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

    static void CheckNewCharacterMarkers()
    {
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
            data.level = 2;
            NpcField.InvalidateMarkers();
            Check(QuestCatalog.Get(1029).autoStart && QuestCatalog.Get(1030).autoComplete, "必须读取QuestInfo的远程任务标记");
            Check(!QuestLogic.CanStart(QuestCatalog.Get(1047), 2006), "fieldEnter/0限制必须生效，不能显示999999999地图任务");
            NpcField.ShowQuestAlerts();
            Check(NpcSession.Active != null && NpcSession.Active.Dialog.Content.Selections.Count > 0,
                "角色灯泡必须打开任务列表");
            NpcSession.CloseActive();
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
        var icon = DialogText.Parse("#i04000000#");
        Check(icon.Runs.Count == 1 && icon.Runs[0].Kind == DialogRunKind.Icon, "#i 应该产生图标节点");
        Check(!string.IsNullOrEmpty(icon.Runs[0].Art), "#i 的道具图标路径没有解析出来");
    }

    // ————— 任务规则 —————
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
        // 不接任务时不该显示"可交付"
        Check(QuestLogic.CanComplete(q, q.completeReq.npc, out reason) == false, "未接取的任务却判定可完成");
    }
}
