using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class NativeRebirthChecks
{
    static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        Debug.Log("[RebirthCheck] PASS " + message);
    }
    static void Set(string name, object value) => typeof(GameSession).GetProperty(name).GetSetMethod(true).Invoke(null, new[] { value });
    public static void Run()
    {
        string path = Path.Combine(Path.GetTempPath(), "rebirth-" + Guid.NewGuid().ToString("N") + ".json");
        string snapshot = JsonUtility.ToJson(Profile.Data);
        var field = typeof(Profile).GetField("debugOverride", BindingFlags.Static | BindingFlags.NonPublic);
        bool previous = (bool)field.GetValue(null);
        field.SetValue(null, true);
        int exit = 0;
        try
        {
            var db = DbStore.Create(path);
            int account = LoginService.Login(db, "rebirth-check", "pw").accountId;
            var record = new CharRecord { accountid = account, name = "RebirthCheck", level = 255, job = 412,
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

            var session = NpcSession.Talk(9900001, "一键转生");
            Check(session != null && session.Dialog.RawText.Contains("小于10次"), "原脚本显示10次上限");
            session.Reply(1); Check(session.Dialog.RawText.Contains("最大10次"), "原脚本进入涅槃确认页");
            session.Reply(1);
            Check(session.Dialog.RawText.Contains("涅槃成功"), "真实原脚本完成涅槃");
            Check(Profile.level == 1 && Profile.JobId == 0 && Profile.Data.experience == 0, "按源端等级/职业逻辑回到1级初心者并清经验");
            Check(GameSession.Character.record.reborns == 10, "第9次成功增加至10次");
            Check(Profile.ItemCount(4039020) == 0 && Profile.ItemCount(2049115) == 0, "扣除30材料和10卷轴");
            Check(Profile.ItemCount(4032170) == 50 && Profile.ItemCount(4032171) == 50, "发放两种宝石各50");
            Check(GameSession.Net.Cash(new CashRequest { characterId = id }).credit == 50000, "仅扣除5万点券");
            var values = new NativeScriptValues(db);
            Check(values.Handle(new ScriptValueRequest { characterId = id, name = "偷学技能点" }).value == "1", "永久偷学技能点增加1");
            GameSession.SaveToDb(); db.Save();
            Check(CharactersDao.FindById(DbStore.Open(path), id).reborns == 10, "涅槃次数写入characters并可重新读档");

            Profile.Data.level = 255;
            Profile.AddItem(4039020, 30); Profile.AddItem(2049115, 10);
            session = NpcSession.Talk(9900001, "一键转生"); session.Reply(1); session.Reply(1);
            Check(session.Dialog.RawText.Contains("已达到最大涅槃次数10次"), "达到10次后由原脚本拒绝第11次");
            Check(GameSession.Character.record.reborns == 10 && Profile.ItemCount(4039020) == 30 &&
                Profile.ItemCount(2049115) == 10 && GameSession.Net.Cash(new CashRequest { characterId = id }).credit == 50000,
                "上限拒绝不扣材料/点券且不增加次数");
            Debug.Log("[RebirthCheck] ALL PASS");
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
