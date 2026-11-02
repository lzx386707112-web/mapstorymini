using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// 转职链路自检（不需要进 Play）：
//   Unity -batchmode -quit -projectPath <proj> -executeMethod JobAdvanceChecks.Run -logFile /tmp/job_adv.log
//
// 目的：把「一转 → 四转」的原始 NPC 脚本按真实点击顺序跑一遍，断言最后 jobId 真的变了。
// 这一层能抓到「对话走完了、任务也过了，但职业没变」这种只有实机才能感觉到的断链：
//   - 脚本里的 cm.changeJobById 之前有空接口/异常 → 被 NpcSession.Fail 吞掉
//   - 前置道具（英雄的证明 4031012 / 介绍信 4031008-4031011）没被消耗，脚本提前 dispose
//   - 考验场里的同事 NPC 根本没把证明发出来
// 不代替实机手感验收（对话框位置、点选命中、换图黑场仍要上手点）。
public static class JobAdvanceChecks
{
    static int pass, fail;

    static void Check(bool ok, string what)
    {
        if (ok) { pass++; return; }
        fail++;
        Debug.LogError("[JobAdvanceCheck] 失败：" + what);
    }

    // 转职凭证（介绍信 / 英雄的证明 / 黑珠 / 三转项链与证明）
    static readonly int[] JobItems = { 4031008, 4031009, 4031010, 4031011, 4031012, 4031013, 4031057, 4031058, 4031059 };

    public static void Run()
    {
        var host = new GameObject("JobAdvanceChecks");
        host.SetActive(false);
        var previousLoop = GameLoop.I;
        string snapshot = JsonUtility.ToJson(Profile.Data);
        var saveOverride = typeof(Profile).GetField("debugOverride", BindingFlags.Static | BindingFlags.NonPublic);
        bool previousOverride = (bool)saveOverride.GetValue(null);
        saveOverride.SetValue(null, true);
        try
        {
            var loop = host.AddComponent<GameLoop>();
            loop.player = host.AddComponent<PlayerCtrl>();
            GameLoop.I = loop;
            pass = fail = 0;

            CheckFirstJob();
            CheckSecondJob();
            CheckSecondJobChain();
            CheckSecondJobWithLeftoverLetter();
            CheckThirdJob();
            CheckFourthJob();

            Debug.Log("[JobAdvanceCheck] 通过 " + pass + " 项，失败 " + fail + " 项");
        }
        finally
        {
            NpcSession.CloseActive();
            GameLoop.I = previousLoop;
            UnityEngine.Object.DestroyImmediate(host);
            JsonUtility.FromJsonOverwrite(snapshot, Profile.Data);
            saveOverride.SetValue(null, previousOverride);
        }
        if (fail > 0 && Application.isBatchMode) EditorApplication.Exit(1);
    }

    // ————————————————————————————————————————————————
    // 准备 / 驱动
    // ————————————————————————————————————————————————

    static void Prepare(int job, int level, params int[] items)
    {
        Profile.Data.jobId = job;
        Profile.Data.level = level;
        Profile.Data.sp = 0;
        foreach (int id in JobItems)
        {
            int n = Profile.ItemCount(id);
            if (n > 0) Profile.RemoveItem(id, n);
        }
        foreach (int id in items) Profile.AddItem(id, 1);
    }

    static string Trim(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        text = text.Replace("\r", "").Replace("\n", " / ");
        return text.Length <= 90 ? text : text.Substring(0, 90) + "…";
    }

    // 按真实点击顺序推进：Simple 依次取 picks 里的链接号，其余一律点「下一步 / 是」。
    static NpcSession Drive(string tag, int npcId, params int[] picks)
    {
        var session = NpcSession.Talk(npcId);
        if (session == null) { Debug.Log("[JobAdv] " + tag + " 取不到脚本 npc=" + npcId); return null; }
        int pi = 0;
        for (int step = 0; step < 60; step++)
        {
            if (session.Closed || session.Disposed) break;
            var mode = session.Dialog.Mode;
            Debug.Log("[JobAdv] " + tag + " #" + step + " " + mode + " job=" + Profile.JobId + " :: " + Trim(session.Dialog.RawText));
            if (mode == NpcDialogMode.None) break;
            if (mode == NpcDialogMode.Simple)
            {
                if (pi >= picks.Length) { Debug.LogWarning("[JobAdv] " + tag + " 菜单没给选项：" + Trim(session.Dialog.RawText)); break; }
                session.Reply(1, picks[pi++]);
            }
            else if (mode == NpcDialogMode.GetNumber || mode == NpcDialogMode.GetText || mode == NpcDialogMode.Style) break;
            else session.Reply(1);
        }
        if (session.LastError != null) Debug.LogError("[JobAdv] " + tag + " 脚本报错：" + session.LastError);
        return session;
    }

    static string Why(NpcSession session) =>
        session == null ? "（没跑起来）" : "（最后一条对白：" + Trim(session.Dialog.RawText) + "）";

    // ————————————————————————————————————————————————
    // 1. 一转：五个教官，新手 0 → 100/200/300/400/500
    // ————————————————————————————————————————————————

    static void CheckFirstJob()
    {
        FirstJob("战士", 1022000, 100);
        FirstJob("法师", 1032001, 200);
        FirstJob("弓手", 1012100, 300);
        FirstJob("飞侠", 1052001, 400);
        FirstJob("海盗", 1090000, 500);
    }

    static void FirstJob(string name, int npcId, int expect)
    {
        Prepare(0, 10);
        var session = Drive("一转-" + name, npcId);
        Check(Profile.JobId == expect, "一转-" + name + " 走完对话后 jobId=" + Profile.JobId + "，应为 " + expect + Why(session));
    }

    // ————————————————————————————————————————————————
    // 2. 二转：五个教官，各选第一个方向
    //    海盗不同：源端要先做完 2191（格斗家）/2192（枪手）转职任务才开放选择，
    //    否则只说一句「你还没有接到转职任务！」——这里按「已完成 2191」模拟。
    // ————————————————————————————————————————————————

    static void CheckSecondJob()
    {
        SecondJob("战士", 1022000, 100, 110, 0, 3, 0);
        SecondJob("法师", 1032001, 200, 210, 0, 3, 0);
        SecondJob("弓手", 1012100, 300, 310, 0, 3, 0);
        SecondJob("飞侠", 1052001, 400, 410, 0, 3, 0);
        SecondJob("海盗", 1090000, 500, 510, 2191, 3);
    }

    static void SecondJob(string name, int npcId, int from, int expect, int doneQuest, params int[] picks)
    {
        Prepare(from, 30, 4031012);
        if (doneQuest > 0) Profile.CompleteQuest(doneQuest);
        var session = Drive("二转-" + name, npcId, picks);
        Check(Profile.JobId == expect, "二转-" + name + " 走完对话后 jobId=" + Profile.JobId + "，应为 " + expect + Why(session));
    }

    // ————————————————————————————————————————————————
    // 3. 二转全链路：教官发信 → 考验场入口 → 交 30 黑珠拿证明 → 回教官转职
    // ————————————————————————————————————————————————

    static readonly (string name, int teacher, int gate, int handIn, int from, int to)[] Chains =
    {
        ("战士", 1022000, 1072000, 1072004, 100, 110),
        ("法师", 1032001, 1072001, 1072005, 200, 210),
        ("弓手", 1012100, 1072002, 1072006, 300, 310),
        ("飞侠", 1052001, 1072003, 1072007, 400, 410),
    };

    static void CheckSecondJobChain()
    {
        foreach (var c in Chains)
        {
            Prepare(c.from, 30);
            var teacher = Drive("全链路-" + c.name + "-教官发信", c.teacher, 0);
            bool hasLetter = false;
            foreach (int letter in new[] { 4031008, 4031009, 4031010, 4031011 })
                if (Profile.ItemCount(letter) > 0) hasLetter = true;
            Check(hasLetter, "全链路-" + c.name + "-教官没发出介绍信" + Why(teacher));

            Drive("全链路-" + c.name + "-考验场入口", c.gate);

            // 后台下发 / 打怪攒够 30 个黑珠
            Profile.AddItem(4031013, 30);
            var hand = Drive("全链路-" + c.name + "-交黑珠", c.handIn);
            Check(Profile.ItemCount(4031012) > 0, "全链路-" + c.name + "-交完 30 个黑珠没拿到英雄的证明" + Why(hand));

            var back = Drive("全链路-" + c.name + "-回教官转职", c.teacher, 3, 0);
            Check(Profile.JobId == c.to, "全链路-" + c.name + " 回到教官后 jobId=" + Profile.JobId + "，应为 " + c.to + Why(back));
        }
    }

    // ————————————————————————————————————————————————
    // 4. 介绍信没被消耗时（后台直接发证明 / 交付 NPC 漏扣信）
    // ————————————————————————————————————————————————

    static void CheckSecondJobWithLeftoverLetter()
    {
        // 源端 1072000/1072001/1072002/1072003 进考验场时才扣介绍信；
        // 若玩家靠后台发道具跳过这一步，信还留在身上，1022000 的 status==2 会直接 dispose，
        // 表现就是「选完方向没反应」。凭证互斥（NpcApi.StaleLetter）必须把这种信判成已失效。
        Prepare(100, 30, 4031012, 4031008);
        var session = Drive("二转-战士(带介绍信)", 1022000, 3, 0);
        Check(Profile.JobId == 110,
            "二转-战士(身上残留 4031008 介绍信) 走完对话后 jobId=" + Profile.JobId + "，应为 110 —— 玩家会卡在「选完方向没反应」" + Why(session));
    }

    // ————————————————————————————————————————————————
    // 5. 三转：2020008~2020013，持有智慧项链 4031058 走 Mental 分支
    // ————————————————————————————————————————————————

    static void CheckThirdJob()
    {
        ThirdJobPrelude();
        ThirdJob("战士", 2020008, 110, 111);
        ThirdJob("法师", 2020009, 210, 211);
        ThirdJob("弓手", 2020010, 310, 311);
        ThirdJob("飞侠", 2020011, 410, 411);
        ThirdJob("海盗", 2020013, 510, 511);
    }

    // 三转的前半段（体力部分）：2020008 申请 → 1022000 派分身 → 交项链拿 4031057。
    // 这一段全靠 Character.partyquestitems 的 JB3/JBP 标记串起来，标记读写不通就会
    // 一直重复「去找教官」，表现是「三转接了但永远进行不下去」。
    static void ThirdJobPrelude()
    {
        Prepare(110, 70);
        var ask = Drive("三转-战士-申请", 2020008, 0);
        Check(ask != null && ask.LastError == null, "三转-战士-申请 脚本报错：" + (ask?.LastError ?? "没跑起来"));

        var dispatch = Drive("三转-战士-找教官", 1022000);
        Check(dispatch != null && (dispatch.Dialog.RawText ?? "").Contains("分身"),
            "三转申请后 1022000 应进入分身考验分支，实际：" + Trim(dispatch?.Dialog.RawText));

        Profile.AddItem(4031059, 1);
        var hand = Drive("三转-战士-交项链", 1022000);
        Check(Profile.ItemCount(4031057) > 0,
            "交回 4031059 后应拿到 4031057，实际 " + Profile.ItemCount(4031057) + Why(hand));

        var half = Drive("三转-战士-交体力证明", 2020008, 0);
        Check(Profile.ItemCount(4031057) == 0 && half != null && half.LastError == null,
            "把 4031057 交给 2020008 后应被收走并转入答题环节" + Why(half));
    }

    static void ThirdJob(string name, int npcId, int from, int expect)
    {
        Prepare(from, 70, 4031058);
        var session = Drive("三转-" + name, npcId, 0);
        Check(Profile.JobId == expect, "三转-" + name + " 走完对话后 jobId=" + Profile.JobId + "，应为 " + expect + Why(session));
    }

    // ————————————————————————————————————————————————
    // 6. 四转：2081100~2081500，前置是考验任务 6904 已完成 + Lv.120
    // ————————————————————————————————————————————————

    static void CheckFourthJob()
    {
        // 每系的四转考验任务不同：战 6904 / 法 6914 / 弓 6924 / 侠 6934 / 海 6944
        FourthJob("战士", 2081100, 111, 112, 6904);
        FourthJob("法师", 2081200, 211, 212, 6914);
        FourthJob("弓手", 2081300, 311, 312, 6924);
        FourthJob("飞侠", 2081400, 411, 412, 6934);
        FourthJob("海盗", 2081500, 511, 512, 6944);
    }

    static void FourthJob(string name, int npcId, int from, int expect, int trialQuest)
    {
        Prepare(from, 120);
        Profile.CompleteQuest(trialQuest);
        var session = Drive("四转-" + name, npcId);
        Check(Profile.JobId == expect, "四转-" + name + " 走完对话后 jobId=" + Profile.JobId + "，应为 " + expect + Why(session));
    }
}
