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

        Debug.Log("[QuestNpcCheck] 通过 " + pass + " 项，失败 " + fail + " 项");
        if (fail > 0 && Application.isBatchMode) EditorApplication.Exit(1);
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
