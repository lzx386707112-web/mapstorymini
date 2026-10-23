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
        CheckNewCharacterMarkers();
        CheckAllMapQuestLinks();

        Debug.Log("[QuestNpcCheck] 通过 " + pass + " 项，失败 " + fail + " 项");
        if (fail > 0 && Application.isBatchMode) EditorApplication.Exit(1);
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
            if (kv.Key.StartsWith("npc/", StringComparison.Ordinal)) npc++; else quest++;
            try { new JsEngine(body, kv.Key); }
            catch (Exception e)
            {
                broken++;
                if (brokenKeys.Count < 5) brokenKeys.Add(kv.Key + " :: " + e.Message);
            }
        }

        Debug.Log("[QuestNpcCheck] 脚本包 npc=" + npc + " quest=" + quest + " 解析失败=" + broken);
        // 源端本身有几条用 Java 互操作 / 正则字面量 / ES6 解构写的脚本，引擎跑不了；
        // 允许少量失败，但一旦超阈值说明是同步或打包通道坏了，不是脚本特例。
        Check(npc + quest > 1100, "脚本条数偏少：npc=" + npc + " quest=" + quest);
        Check(broken <= 20, "无法解析的脚本过多：" + broken + " 条，例如 " + string.Join(" | ", brokenKeys));
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
        Check(c != null && c.Selections.Count == 2, "选项解析数量不是 2");
        Check(c != null && c.Body.Contains("你好"), "正文丢了");
        Check(c != null && !c.Body.Contains("#L"), "#L 标记没有被吃掉");
        Check(c != null && c.Body.Contains("<color="), "#b 没有转成富文本颜色");
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
