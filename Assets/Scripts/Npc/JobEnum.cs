using System.Collections.Generic;
using MapleDash.Js;

// 源端 org.gms.client.Job（BeiDou-Server/gms-server/src/main/java/org/gms/client/Job.java）
// 在本机的对应物。脚本有两种引用方式，两种都必须成立：
//
//   1) 常量比较（quest/2001.js、3414.js、3414_free10rate.js、2034.js、3437.js）
//        stance = qm.getPlayer().getJobStyle();
//        if (stance == Job.WARRIOR || stance == Job.BEGINNER) { ... }
//      源端是 Java enum 的**引用相等**，所以本机必须让 getJobStyle() 和
//      Job.WARRIOR 返回同一个对象实例；换成数字比较会恒不等（脚本会掉进 else 分支）。
//
//   2) 按名字取常量（quest/20101.js~20315.js 骑士团转职脚本）
//        job = Java.type('org.gms.client.Job')[job[jobId] + jobLevel];
//        qm.sendYesNo(`…你想成为#b初级骑士 - ${job.getName()}#k吗?`);
//      索引器在本机就是 JsHostObject 的成员名查找，getName() 返回源端
//      I18nUtil.getMessage("job.name.<id>") 的中文名。
//
// 枚举表（名字|id|中文名）逐条来自源端 Job.java 与 message_zh_CN.properties，
// 不在本机另造一套职业命名。
public static class JobEnum
{
    public sealed class Const
    {
        public readonly string Name;      // 源端枚举名（BEGINNER / WARRIOR / ...）
        public readonly int Id;           // 源端 getId()
        public readonly string Display;    // 源端 i18n job.name.<id>
        internal JsHostObject Js;          // 同一常量必须始终是同一个实例（== 是引用相等）

        public Const(string name, int id, string display)
        {
            Name = name;
            Id = id;
            Display = display;
        }
    }

    static readonly string[] table =
    {
        "BEGINNER|0|新手",
        "WARRIOR|100|战士",
        "FIGHTER|110|剑客",
        "CRUSADER|111|勇士",
        "HERO|112|英雄",
        "PAGE|120|准骑士",
        "WHITEKNIGHT|121|骑士",
        "PALADIN|122|圣骑士",
        "SPEARMAN|130|枪战士",
        "DRAGONKNIGHT|131|龙骑士",
        "DARKKNIGHT|132|黑骑士",
        "MAGICIAN|200|魔法师",
        "FP_WIZARD|210|法师（火毒）",
        "FP_MAGE|211|巫师（火毒）",
        "FP_ARCHMAGE|212|魔导师（火毒）",
        "IL_WIZARD|220|法师（冰雷）",
        "IL_MAGE|221|巫师（冰雷）",
        "IL_ARCHMAGE|222|魔导师（冰雷）",
        "CLERIC|230|牧师",
        "PRIEST|231|祭司",
        "BISHOP|232|主教",
        "BOWMAN|300|弓箭手",
        "HUNTER|310|猎人",
        "RANGER|311|射手",
        "BOWMASTER|312|神射手",
        "CROSSBOWMAN|320|弩弓手",
        "SNIPER|321|游侠",
        "MARKSMAN|322|箭神",
        "THIEF|400|飞侠",
        "ASSASSIN|410|刺客",
        "HERMIT|411|无影人",
        "NIGHTLORD|412|隐士",
        "BANDIT|420|侠客",
        "CHIEFBANDIT|421|独行客",
        "SHADOWER|422|侠盗",
        "PIRATE|500|海盗",
        "BRAWLER|510|拳手",
        "MARAUDER|511|斗士",
        "BUCCANEER|512|冲锋队长",
        "GUNSLINGER|520|火枪手",
        "OUTLAW|521|大副",
        "CORSAIR|522|船长",
        "MAPLELEAF_BRIGADIER|800|巡查员",
        "GM|900|管理员",
        "SUPERGM|910|超级管理员",
        "NOBLESSE|1000|初心者",
        "DAWNWARRIOR1|1100|魂骑士(1转)",
        "DAWNWARRIOR2|1110|魂骑士(2转)",
        "DAWNWARRIOR3|1111|魂骑士(3转)",
        "DAWNWARRIOR4|1112|魂骑士(4转)",
        "BLAZEWIZARD1|1200|炎术士(1转)",
        "BLAZEWIZARD2|1210|炎术士(2转)",
        "BLAZEWIZARD3|1211|炎术士(3转)",
        "BLAZEWIZARD4|1212|炎术士(4转)",
        "WINDARCHER1|1300|风灵使者(1转)",
        "WINDARCHER2|1310|风灵使者(2转)",
        "WINDARCHER3|1311|风灵使者(3转)",
        "WINDARCHER4|1312|风灵使者(4转)",
        "NIGHTWALKER1|1400|夜行者(1转)",
        "NIGHTWALKER2|1410|夜行者(2转)",
        "NIGHTWALKER3|1411|夜行者(3转)",
        "NIGHTWALKER4|1412|夜行者(4转)",
        "THUNDERBREAKER1|1500|奇袭者(1转)",
        "THUNDERBREAKER2|1510|奇袭者(2转)",
        "THUNDERBREAKER3|1511|奇袭者(3转)",
        "THUNDERBREAKER4|1512|奇袭者(4转)",
        "LEGEND|2000|战童",
        "EVAN|2001|龙初心",
        "ARAN1|2100|战神(1转)",
        "ARAN2|2110|战神(2转)",
        "ARAN3|2111|战神(3转)",
        "ARAN4|2112|战神(4转)",
        "EVAN1|2200|龙神1",
        "EVAN2|2210|龙神2",
        "EVAN3|2211|龙神3",
        "EVAN4|2212|龙神4",
        "EVAN5|2213|龙神5",
        "EVAN6|2214|龙神6",
        "EVAN7|2215|龙神7",
        "EVAN8|2216|龙神8",
        "EVAN9|2217|龙神9",
        "EVAN10|2218|龙神10",
    };

    static readonly Dictionary<string, Const> byName = new Dictionary<string, Const>();
    static readonly Dictionary<int, Const> byId = new Dictionary<int, Const>();

    static JobEnum()
    {
        foreach (var row in table)
        {
            var parts = row.Split('|');
            var c = new Const(parts[0], int.Parse(parts[1]), parts[2]);
            byName[c.Name] = c;
            byId[c.Id] = c;
        }
    }

    public static IEnumerable<Const> All
    {
        get
        {
            foreach (var row in table)
            {
                var parts = row.Split('|');
                yield return byName[parts[0]];
            }
        }
    }

    public static Const Of(string name) => byName.TryGetValue(name ?? "", out var c) ? c : null;
    public static Const Of(int id) => byId.TryGetValue(id, out var c) ? c : null;

    // Job.getJobStyleInternal(int jobid, byte opt)（Job.java:144）
    // 按 jobid/100 归到源端的六个基础职业样式；海盗按 opt 分拳手/火枪手。
    public static int StyleOf(int jobId, bool strOverDex)
    {
        int jobtype = jobId / 100;
        if (jobtype == 1 || jobtype == 11 || jobtype == 21) return 100;                 // WARRIOR / DAWNWARRIOR1 / ARAN1
        if (jobtype == 2 || jobtype == 12 || jobtype == 22) return 200;                 // MAGICIAN / BLAZEWIZARD1 / EVAN1
        if (jobtype == 3 || jobtype == 13) return jobId / 10 == 32 ? 320 : 300;         // CROSSBOWMAN / BOWMAN（WINDARCHER1 同理）
        if (jobtype == 4 || jobtype == 14) return 400;                                 // THIEF / NIGHTWALKER1
        if (jobtype == 5 || jobtype == 15) return strOverDex ? 510 : 520;              // BRAWLER / GUNSLINGER
        return 0;                                                                      // BEGINNER
    }

    // Character.getJobStyle()（Character.java:577）：opt = str > dex ? 0x80 : 0x40
    public static Const Style(int jobId, int str, int dex)
        => Of(StyleOf(jobId, str > dex));

    // 枚举常量在 JS 里就是一个带 getId/getName 的宿主对象；同一常量永远返回同一实例。
    public static JsHostObject Js(Const c)
    {
        if (c == null) return null;
        if (c.Js == null)
        {
            var o = new JsHostObject(c);
            o.Set("getId", JsFunction.FromNative("getId", _ => new JsNumber(c.Id)));
            o.Set("getName", JsFunction.FromNative("getName", _ => new JsString(c.Display)));
            o.Set("name", new JsString(c.Name));
            o.Set("id", new JsNumber(c.Id));
            c.Js = o;
        }
        return c.Js;
    }
}
