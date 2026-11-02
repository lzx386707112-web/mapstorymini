using System;
using System.Collections.Generic;

// 源端 org.gms.service.LinkSystemService.java（341 行）的单机等价实现。
// Link = 同账号同世界里「比当前角色更早创建」的角色提供的被动加成：
//   阶段（stagePercent/stageHpMp，L201-219）：Lv.150 → 3%/800，Lv.200 → 7%/1500，Lv.255 → 10%/2000；
//   calculateBonus（L182）：每条达阶段 link，全属性/最终伤害/Boss伤害/经验各 +percent，HP/MP 各 +hpMp。
// 脚本面（link_system.js）只消费 getOverview（菜单字符串）与 addLink（selection = 候选角色 cid）。
// 加成生效（源端三处）：伤害乘区 MapleMap.java:1523、HP/MP/四维 Character.updateLocalStats
// L7703-7711、经验 Character.java L4923 —— 本端接入口见 cm.linkSystemOverview 绑定处注释。
public static class NativeLinkService
{
    public const int FirstStageLevel = 150;

    public sealed class Bonus
    {
        public int activeLinks, allStatPercent, finalDamagePercent, bossDamagePercent, expPercent, hp, mp;
    }

    sealed class Slot
    {
        public long id;
        public string name;
        public int level;
        public string createdate;
    }

    // loadAccountCharacters（L284-298）：同账号同世界，按 createdate, id 升序。
    static List<Slot> AccountCharacters(DbStore db, int accountId, int world)
    {
        var result = new List<Slot>();
        foreach (var row in db.Table("characters").Select(DbStore.Eq("accountid", (long)accountId)))
        {
            if (DbStore.Int(row, "world") != world) continue;
            result.Add(new Slot { id = DbStore.Int(row, "id"), name = DbStore.Str(row, "name"),
                level = DbStore.Int(row, "level"), createdate = DbStore.Str(row, "createdate") });
        }
        result.Sort((a, b) =>
        {
            int compared = string.CompareOrdinal(a.createdate, b.createdate);
            return compared != 0 ? compared : a.id.CompareTo(b.id);
        });
        return result;
    }

    static HashSet<long> LinkedSources(DbStore db, long targetCid)
    {
        var sources = new HashSet<long>();
        foreach (var row in db.Table("character_link").Select(DbStore.Eq("target_cid", targetCid)))
            sources.Add(DbStore.Int(row, "source_cid"));
        return sources;
    }

    static int CharacterSlots(DbStore db, int accountId)
    {
        var row = db.Table("accounts").Find(DbStore.Eq("id", (long)accountId));
        return row != null ? DbStore.Int(row, "characterslots") : 3;
    }

    // —— 脚本面：cm.linkSystemOverview() ——
    public static string Overview(DbStore db, long targetCid)
    {
        var current = CharactersDao.FindById(db, (int)targetCid);
        if (current == null) return "当前角色数据不存在，请重新登录后再试。";

        var characters = AccountCharacters(db, current.accountid, current.world);
        var linked = LinkedSources(db, current.id);
        int totalSlots = Math.Max(CharacterSlots(db, current.accountid), characters.Count);
        int currentIndex = characters.FindIndex(c => c.id == current.id);
        var bonus = LoadBonus(db, current.id);

        var text = new System.Text.StringBuilder("#b#eLink系统#n#k\r\n");
        text.Append("当前角色：#b").Append(current.name).Append("#k Lv.").Append(current.level)
            .Append("  槽位 ").Append(currentIndex + 1).Append("/").Append(totalSlots).Append("\r\n");
        text.Append("已连接：#r").Append(linked.Count).Append("/")
            .Append(Math.Max(0, currentIndex)).Append("#k  已生效：#r")
            .Append(bonus.activeLinks).Append("#k\r\n\r\n");
        text.Append("#e当前总加成#n：全属性 +").Append(bonus.allStatPercent).Append("%  最终伤害 +")
            .Append(bonus.finalDamagePercent).Append("%\r\n")
            .Append("Boss伤害 +").Append(bonus.bossDamagePercent).Append("%  经验 +")
            .Append(bonus.expPercent).Append("%  HP/MP +").Append(bonus.hp).Append("\r\n\r\n");
        text.Append("阶段：Lv.150（3% / 800）  Lv.200（7% / 1500）\r\n")
            .Append("　　　Lv.255（10% / 2000）\r\n\r\n")
            .Append("#eLink槽位#n（只能添加当前角色之前创建的角色）\r\n");

        for (int index = 0; index < totalSlots; index++)
        {
            text.Append("#d").Append(index + 1).Append(".#k ");
            if (index >= characters.Count) { text.Append("#b+ 空槽#k\r\n"); continue; }
            var slot = characters[index];
            if (slot.id == current.id)
            {
                text.Append("#r◆ ").Append(slot.name).Append(" Lv.").Append(slot.level)
                    .Append("（当前角色）#k\r\n");
                continue;
            }
            if (index > currentIndex)
            {
                text.Append(slot.name).Append(" Lv.").Append(slot.level).Append("（后序角色）\r\n");
                continue;
            }
            int percent = StagePercent(slot.level);
            int hpMp = StageHpMp(slot.level);
            if (linked.Contains(slot.id))
            {
                if (percent > 0)
                    text.Append("#g◆ ").Append(slot.name).Append(" Lv.").Append(slot.level)
                        .Append(" 已生效（+").Append(percent).Append("% / +").Append(hpMp).Append("）#k\r\n");
                else
                    text.Append("#k◆ ").Append(slot.name).Append(" Lv.").Append(slot.level)
                        .Append(" 已Link，达到Lv.150后生效\r\n");
            }
            else if (percent > 0)
                text.Append(slot.name).Append(" Lv.").Append(slot.level)
                    .Append("  #L").Append(slot.id).Append("##b+ 添加Link#k#l\r\n");
            else
                text.Append(slot.name).Append(" Lv.").Append(slot.level)
                    .Append("（达到Lv.150后可Link）\r\n");
        }
        text.Append("\r\n#L-1##b关闭#k#l");
        return text.ToString();
    }

    // —— 脚本面：cm.linkSystemAdd(sourceCid)（校验链 validateLink L251-271） ——
    public static string AddLink(DbStore db, long targetCid, int sourceCid)
    {
        if (sourceCid <= 0) return "Link角色不存在。";
        var target = CharactersDao.FindById(db, (int)targetCid);
        var source = CharactersDao.FindById(db, sourceCid);
        if (target == null) return "当前角色数据不存在，请重新登录后再试。";
        if (source == null) return "选择的Link角色不存在。";
        if (target.id == source.id) return "不能Link当前角色自己。";
        if (target.accountid != source.accountid || target.world != source.world)
            return "只能Link同一账号、同一世界的角色。";
        if (!IsEarlier(source.createdate, source.id, target.createdate, target.id))
            return "只能添加比当前角色更早创建的角色。";
        if (source.level < FirstStageLevel)
            return "该角色需要达到150级后才能添加Link。";

        var links = db.Table("character_link");
        if (links.Find(r => DbStore.Int(r, "target_cid") == target.id && DbStore.Int(r, "source_cid") == source.id) != null)
            return "该角色已经添加过Link。";
        links.Insert(new DbRow { ["target_cid"] = (long)target.id, ["source_cid"] = (long)source.id,
            ["created_at"] = DbStore.NowText() });

        var bonus = LoadBonus(db, target.id);
        return "已将 #b" + source.name + "#k 添加到Link。\r\n当前总加成：全属性/最终伤害/Boss伤害/经验 +"
            + bonus.allStatPercent + "% ，HP/MP +" + bonus.hp + "。";
    }

    // loadBonus（L227-249）：只统计通过同账号/同世界/更早创建校验的 link（脏数据不生效）。
    public static Bonus LoadBonus(DbStore db, long targetCid)
    {
        var target = CharactersDao.FindById(db, (int)targetCid);
        if (target == null) return new Bonus();
        var levels = new List<int>();
        foreach (var row in db.Table("character_link").Select(DbStore.Eq("target_cid", targetCid)))
        {
            var source = CharactersDao.FindById(db, DbStore.Int(row, "source_cid"));
            if (source == null || source.accountid != target.accountid || source.world != target.world) continue;
            if (!IsEarlier(source.createdate, source.id, target.createdate, target.id)) continue;
            levels.Add(source.level);
        }
        var bonus = new Bonus();
        foreach (int level in levels)
        {
            int percent = StagePercent(level);
            if (percent == 0) continue;
            bonus.activeLinks++;
            bonus.allStatPercent += percent;
            bonus.finalDamagePercent += percent;
            bonus.bossDamagePercent += percent;
            bonus.expPercent += percent;
            bonus.hp += StageHpMp(level);
            bonus.mp += StageHpMp(level);
        }
        return bonus;
    }

    public static int StagePercent(int level)
    {
        if (level >= 255) return 10;
        if (level >= 200) return 7;
        return level >= FirstStageLevel ? 3 : 0;
    }

    public static int StageHpMp(int level)
    {
        if (level >= 255) return 2000;
        if (level >= 200) return 1500;
        return level >= FirstStageLevel ? 800 : 0;
    }

    // isEarlier（L221-225）：createdate 相同时按 id 决胜。
    static bool IsEarlier(string sourceCreated, long sourceId, string targetCreated, long targetId)
    {
        int compared = string.CompareOrdinal(sourceCreated ?? "", targetCreated ?? "");
        return compared < 0 || (compared == 0 && sourceId < targetId);
    }
}
