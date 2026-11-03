using System;
using System.IO;
using MapleDash.Js;

public static class RebirthChecks
{
    public static void Run(Action<bool, string> check)
    {
        string path = Path.Combine(Path.GetTempPath(), "rebirth-config-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var db = DbStore.Create(path);
            check(GameConfigDao.GetInt(db, "max_reborn_count") == 10, "新存档涅槃上限为10");
            db.Table("game_config").Delete(r => DbStore.Str(r, "config_code") == "max_reborn_count");
            db.Save();
            db = DbStore.Open(path);
            check(GameConfigDao.GetInt(db, "max_reborn_count") == 10, "旧存档缺失涅槃上限自动补为10");

            var scripts = new NativeScriptService(db);
            var session = scripts.Handle(new ScriptRequest { operation = ScriptOperation.Open,
                source = "function start(){return Java.type('org.gms.config.GameConfig').getServerInt('max_reborn_count');}" });
            var result = scripts.Handle(new ScriptRequest { operation = ScriptOperation.Call,
                sessionId = session.sessionId, function = "start" });
            check(JsConvert.ToInt32(result.value) == 10, "真实JS作用域配置接口读取上限10");

            var cap = db.Table("game_config").Find(r => DbStore.Str(r, "config_code") == "max_reborn_count");
            cap["config_value"] = "7";
            db.Save();
            db = DbStore.Open(path);
            check(GameConfigDao.GetInt(db, "max_reborn_count") == 7, "补键不覆盖已有自定义涅槃上限");
            db.Save();
            check(GameConfigDao.GetInt(DbStore.Open(path), "max_reborn_count") == 7, "涅槃上限再次读档保持不变");
            check(GameConfigDao.GetBool(db, "use_rebirth_system"), "本地涅槃启用键补齐");
            var enabled = db.Table("game_config").Find(r => DbStore.Str(r, "config_code") == "use_rebirth_system");
            enabled["config_value"] = "false"; GameConfigDao.EnsureLevelCaps(db);
            check(!GameConfigDao.GetBool(db, "use_rebirth_system"), "保留明确关闭涅槃的配置");
            check(NativeRebirthRules.JobAtLevel(412, 1, 0, false) == 0, "源端飞侠回1级变初心者");
            check(NativeRebirthRules.JobAtLevel(222, 30, 0, false) == 200 && NativeRebirthRules.JobAtLevel(222, 31, 0, false) == 220,
                "源端冒险家30/31级职业边界");
            check(NativeRebirthRules.JobAtLevel(1111, 120, 0, false) == 1111 && NativeRebirthRules.JobAtLevel(1111, 120, 0, true) == 1112,
                "源端骑士团五转完成门控");
            check(NativeRebirthRules.JobAtLevel(512, 1, 0, false) == 512 && NativeRebirthRules.JobAtLevel(2112, 1, 0, false) == 2112,
                "保留源端海盗/战神未回退职业分支");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
