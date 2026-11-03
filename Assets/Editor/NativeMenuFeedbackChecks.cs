using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class NativeMenuFeedbackChecks
{
    static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        Debug.Log("[MenuFeedback] PASS " + message);
    }
    static void Set(string name, object value) => typeof(GameSession).GetProperty(name).GetSetMethod(true).Invoke(null, new[] { value });
    public static void Run()
    {
        string path = Path.Combine(Path.GetTempPath(), "menu-feedback-" + Guid.NewGuid().ToString("N") + ".json");
        string snapshot = JsonUtility.ToJson(Profile.Data);
        var field = typeof(Profile).GetField("debugOverride", BindingFlags.Static | BindingFlags.NonPublic);
        bool previous = (bool)field.GetValue(null);
        field.SetValue(null, true);
        int exit = 0;
        try
        {
            var db = DbStore.Create(path);
            int account = LoginService.Login(db, "menu-feedback-check", "pw").accountId;
            var record = new CharRecord { accountid = account, name = "MenuFeedback", level = 255, job = 412,
                reborns = 9, str = 4, dex = 4, int_ = 4, luk = 1000, maxhp = 10000, maxmp = 5000,
                equipslots = 96, useslots = 96, setupslots = 96, etcslots = 96 };
            int id = (int)CharactersDao.Insert(db, record); record.id = id;
            InventoryDao.InsertItem(db, id, 4039020, 4, 1, 30);
            InventoryDao.InsertItem(db, id, 2049115, 2, 1, 10);
            var server = GameServer.StartWith(db);
            Set("Server", server); Set("Net", server.Transport); Set("CharacterId", id);
            Set("Character", CharService.Load(db, id)); Set("InGame", true);
            GameSession.Net.Cash(new CashRequest { characterId = id, operation = CashOperation.AdjustBalance,
                currency = 1, amount = 100000 });
            Profile.Data.level = 255; Profile.Data.jobId = 412; Profile.Data.experience = 123;
            Profile.Data.baseMaxHp = 10000; Profile.Data.baseMaxMp = 5000;
            Profile.Data.attributes = new CharacterAttributes { str = 4, dex = 4, intelligence = 4, luk = 1000, level = 255 };
            GameSession.ApplyInventoryToProfile();

            var session = NpcSession.Talk(9900001, "任务/血衣合成");
            Check(session != null && session.Dialog.RawText.Contains("选择"), "blood menu opens original script");
            session.Reply(1, 4);
            Check(session.Dialog.RawText.Contains("300个 #t4000313#") && session.Dialog.RawText.Contains("50个 #t4000152#") &&
                session.Dialog.RawText.Contains("1个 #t1012190#"), "last recipe shows seven individual materials");

            session = NpcSession.Talk(9900001, "一键出售");
            Check(session != null && session.Dialog.RawText.Contains("快捷出售"), "batch sell menu opens");
            session.Reply(1, 1); session.Reply(1);
            Check(session.Dialog.RawText.Contains("成功"), "batch sale completes through original Shop.sell");
            Check(Profile.ItemCount(2049115) == 0 && Profile.Data.gold > 0, "sale removes real slot and grants meso");

            Profile.AddItem(4000000, 2); GameSession.SaveToDb();
            session = NpcSession.Talk(9900001, "删除道具"); session.Reply(1, 4); session.Reply(1, 2);
            var slot = GameSession.Character.inventory.Find(v => v.itemId == 4000000);
            Check(slot != null, "delete fixture actual etc slot exists");
            session.Reply(1, slot.position);
            Check(session.Dialog.RawText.Contains("清除完毕") && Profile.ItemCount(4000000) == 0, "delete original script removes actual slot");

            // Fill each real USE slot with one nonreward item, then execute the original canHold branch.
            GameSession.Character.record.useslots = 1;
            Profile.AddItem(2000000, 1); GameSession.SaveToDb();
            session = NpcSession.Talk(9900001, "每日签到"); session.Reply(1, 0);
            Check(session.Dialog.Visible && session.Dialog.RawText.Contains("背包空间不足"), "daily sign-in full bag keeps original error dialog visible");
            // A live field object must survive the Java-list/Map deduplication chain.
            var enemyObject = new GameObject("MenuFeedbackMob");
            var mob = enemyObject.AddComponent<EnemyUnit>();
            if (!EnemyUnit.All.Contains(mob)) EnemyUnit.All.Add(mob);
            mob.fieldMode = true; mob.fieldMobId = 100100; mob.hp = mob.hpMax = 50;
            session = NpcSession.Talk(9900001, "爆率一览");
            Debug.Log("[MenuFeedback] DROP " + session.Dialog.RawText);
            Check(session.Dialog.RawText.Contains("当前地图怪物") && session.Dialog.RawText.Contains("#L100100#"), "drop query finds real live field monster");
            session.Reply(1, 100100);
            Check(session.Dialog.RawText.Contains("掉落列表") && !session.Dialog.RawText.Contains("查询失败"), "original drop query opens actual mob drop table");
            UnityEngine.Object.DestroyImmediate(enemyObject);
            var parsed = DialogText.Parse("#L1##金币兑换：金币兑换各种道具#l");
            Check(parsed.Body.Contains("金币兑换各种道具"), "unknown hash preserves exchange first option text");
            NativeSystemMessage.Drop(1, "背包空间不足");
            Check(NativeUtilPrompt.Visible, "type1 inventory failure opens persistent original Notice dialog");
            NativeUtilPrompt.Clear();
            Debug.Log("[MenuFeedback] ALL PASS");
        }
        catch (Exception e) { Debug.LogException(e); exit = 1; }
        finally
        {
            NpcSession.CloseActive(); Set("InGame", false); Set("Character", null); Set("CharacterId", 0);
            JsonUtility.FromJsonOverwrite(snapshot, Profile.Data); field.SetValue(null, previous);
            if (File.Exists(path)) File.Delete(path);
        }
        if (Application.isBatchMode) EditorApplication.Exit(exit);
    }
}
