// 技能宏回归：逐条对照源端
//   net/server/channel/handlers/SkillMacroHandler.java（num>5 丢弃；name>12 改包拒绝；position=包内序号）
//   client/Character.java:349（SkillMacro[5]）、8492-8505（存档 DELETE+INSERT 非空宏）、
//   client/Character.java:8739（每次进角色重发宏包）、9517（updateMacros）
//   service/CharacterService.java:504-508（按 position 升序装载）
//   net/server/channel/handlers/UseCashItemHandler.java:196-227（SP 归零清理宏引用）
using System;
using System.Collections.Generic;

static class SkillMacroChecks
{
    static MacroEntry M(int position, string name, int shout, int s1, int s2, int s3)
        => new MacroEntry { position = position, name = name, shout = shout, skill1 = s1, skill2 = s2, skill3 = s3 };

    static MacroResponse Handle(DbStore db, int cid, params MacroEntry[] macros)
        => NativeSkillMacroService.Handle(db, new MacroRequest { characterId = cid, macros = new List<MacroEntry>(macros) });

    static List<DbRow> Rows(DbStore db, int cid)
    {
        var rows = db.Table("skillmacros").Select(DbStore.Eq("characterid", cid));
        rows.Sort((a, b) => Convert.ToInt64(a["position"]).CompareTo(Convert.ToInt64(b["position"])));
        return rows;
    }

    public static void Run(Action<bool, string> Check)
    {
        Console.WriteLine("== 技能宏 SkillMacro（SkillMacroHandler / skillmacros 表）==");
        HandlerChecks(Check);
        PersistChecks(Check);
    }

    // ————————————————— handler 校验与落库 —————————————————
    static void HandlerChecks(Action<bool, string> Check)
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mapledash_macro_h_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            var login = LoginService.Login(db, "macro_acc", "pw123456");
            var made = CharFactory.CreateCharacter(db, login.accountId, 0, "MacroH", CharFactory.JobGroupAdventurer,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            int cid = made.characterId;

            // 5 条宏整包上送：position = 包内序号，非空才落库
            var res = Handle(db, cid,
                M(0, "打怪", 1, 1001005, 1001004, 0),
                M(1, "", 0, 0, 0, 0),                       // 空宏（只有名字算空）
                M(2, "BOSS", 1, 4111002, 0, 0),
                M(3, "", 0, 0, 0, 0),
                M(4, "buff", 0, 1001003, 2311002, 0));
            Check(res.ok, "合法整包（5 条含 2 条空宏）接受");
            var rows = Rows(db, cid);
            Check(rows.Count == 3, "空宏不落库，只存 3 条非空宏（实际 " + rows.Count + "）");
            Check(rows[0]["position"] is long p0 && p0 == 0 && (string)rows[0]["name"] == "打怪",
                "position 保槽位号：宏 0 名「打怪」shout=1");
            Check(rows[1]["position"] is long p1 && p1 == 2, "中间空宏跳过，宏 2 仍落 position=2");

            // 超过 5 条整包拒绝（源端 num>5 直接 return）
            var over = new List<MacroEntry>();
            for (int i = 0; i < 6; i++) over.Add(M(i, "m" + i, 0, 0, 0, 0));
            res = NativeSkillMacroService.Handle(db, new MacroRequest { characterId = cid, macros = over });
            Check(!res.ok && Rows(db, cid).Count == 3, "num>6 拒绝且旧数据不动");

            // 名字 >12 字符整包拒绝（源端 AutobanFactory.PACKET_EDIT + 断线）
            res = Handle(db, cid, M(0, "一二三四五六七八九十甲乙丙", 0, 0, 0, 0));
            Check(!res.ok && Rows(db, cid).Count == 3, "宏名 13 字拒绝且旧数据不动");
            res = Handle(db, cid, M(0, "一二三四五六七八九十甲乙", 0, 0, 0, 0));
            Check(res.ok, "宏名 12 字正好可接受");

            // 覆盖写：第二次提交重写全部行
            res = Handle(db, cid, M(0, "新宏", 1, 5001005, 0, 0));
            Check(res.ok && Rows(db, cid).Count == 1 && (string)Rows(db, cid)[0]["name"] == "新宏",
                "覆盖写：旧 3 条被替换成新 1 条");

            // 全空整包 = 清空宏
            res = Handle(db, cid, M(0, "", 0, 0, 0, 0), M(1, "", 0, 0, 0, 0));
            Check(res.ok && Rows(db, cid).Count == 0, "全空整包 = 清空 skillmacros");
        }
        finally { db.Save(); System.IO.File.Delete(path); }
    }

    // ————————————————— CharService 装载 / 存档闭环 —————————————————
    static void PersistChecks(Action<bool, string> Check)
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mapledash_macro_p_" + Guid.NewGuid().ToString("N") + ".db");
        var db = DbStore.Create(path);
        try
        {
            var login = LoginService.Login(db, "macro_acc2", "pw123456");
            var made = CharFactory.CreateCharacter(db, login.accountId, 0, "MacroP", CharFactory.JobGroupAdventurer,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            int cid = made.characterId;

            Handle(db, cid,
                M(0, "练级", 1, 1001005, 1001004, 1001003),
                M(1, "", 0, 0, 0, 0),
                M(2, "BOSS", 0, 4111002, 4211002, 0));

            // 装载：按 position 升序（CharacterService.loadCharWithSkillMacros）
            var full = CharService.Load(db, cid);
            Check(full.macros.Count == 2, "Load 装载 2 条非空宏（实际 " + full.macros.Count + "）");
            Check(full.macros[0].name == "练级" && full.macros[0].shout == 1
                && full.macros[0].skill1 == 1001005 && full.macros[0].skill2 == 1001004 && full.macros[0].skill3 == 1001003,
                "宏 0 三技能 + shout 完整还原");
            Check(full.macros[1].position == 2 && full.macros[1].name == "BOSS", "宏 2 槽位号与名字还原");

            // 存档闭环：Save 是 DELETE+INSERT，重存不重复、不丢
            full.macros[1].skill2 = 5001005;
            CharService.Save(db, full);
            var again = CharService.Load(db, cid);
            Check(again.macros.Count == 2, "Save 后重 Load 不重复（DELETE+INSERT 幂等）");
            Check(again.macros[1].skill2 == 5001005, "改动后的宏经存档闭环保留");

            // 客户端镜像上的空宏不阻塞存档
            again.macros.Add(new MacroEntry { position = 4, name = "", skill1 = 0, skill2 = 0, skill3 = 0 });
            CharService.Save(db, again);
            Check(CharService.Load(db, cid).macros.Count == 2, "镜像里的空宏在存档时被跳过");

            // 未设置宏的角色：Load 得空表，不报错
            var made2 = CharFactory.CreateCharacter(db, login.accountId, 0, "MacroQ", CharFactory.JobGroupAdventurer,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            var fresh = CharService.Load(db, made2.characterId);
            Check(fresh.macros.Count == 0, "新角色宏表为空");
        }
        finally { db.Save(); System.IO.File.Delete(path); }
    }
}
