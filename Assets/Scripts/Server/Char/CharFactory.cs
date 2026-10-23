// 建号服务。逐条对齐源端：
//   CreateCharHandler.java:41-115     包体与职业群(0骑士团/1冒险家/2战神)
//   ItemConstants.isNewCharDefault*   默认外观白名单（脸/发/发色/肤/衣/裤/鞋/武器）
//   CharacterFactory.java:42-108      槽位/合法性/装备位置(-5/-6/-7/-11)/校验
//   Character.getDefault (581-623)    初始 HP50/MP5/四维12/5/4/4/背包各24格
//   BeginnerCreator / NoblesseCreator 初始地图与新手指南
//   Character.insertNewChar (8136)    characters+keymap+quickslot+背包+技能 同一事务
using System;
using System.Collections.Generic;

public static class CharFactory
{
    public const int JobGroupCygnus = 0;
    public const int JobGroupAdventurer = 1;
    public const int JobGroupAran = 2;

    public const int MapMushroomTown = 10000;      // MapId.MUSHROOM_TOWN
    public const int MapStartingNoblesse = 130030000; // MapId.STARTING_MAP_NOBLESSE
    public const int ItemBeginnersGuide = 4161001; // ItemId.BEGINNERS_GUIDE
    public const int ItemNoblesseGuide = 4161047;  // ItemId.NOBLESSE_GUIDE

    public const int EquipPosTop = -5;
    public const int EquipPosBottom = -6;
    public const int EquipPosShoes = -7;
    public const int EquipPosWeapon = -11;

    // 建号结果码（对齐源端 deleteCharResponse 语义：0 成功，负数失败）
    public const int Ok = 0;
    public const int ErrNoSlot = -3;
    public const int ErrBadName = -1;
    public const int ErrInvalid = -2;
    public const int ErrJobDisabled = -4;

    public sealed class Result
    {
        public int code;
        public int characterId;
        public string error;
    }

    // 默认外观白名单（源端 ItemConstants.java:373-459，job 群 0/1 通用）
    public static bool IsDefaultFace(int job, int gender, int face)
    {
        if (job == JobGroupAran)
            return gender == 0 ? (face == 20100 || face == 20401 || face == 20402)
                               : (face == 21700 || face == 21201 || face == 21002);
        return gender == 0 ? (face == 20000 || face == 20001 || face == 20002)
                           : (face == 21000 || face == 21001 || face == 21002);
    }

    public static bool IsDefaultHair(int gender, int hair)
        => gender == 0 ? (hair == 30290 || hair == 30300 || hair == 30310)
                       : (hair == 31000 || hair == 31040 || hair == 31050);

    public static bool IsDefaultHairColor(int hairColor)
        => hairColor == 0 || hairColor == 2 || hairColor == 3 || hairColor == 7;

    public static bool IsDefaultSkinColor(int skinColor) => skinColor >= 0 && skinColor < 4;

    public static bool IsDefaultTop(int job, int gender, int top)
    {
        if (job == JobGroupAran) return top == 1042167;
        return gender == 0 ? (top == 1040002 || top == 1040006 || top == 1040010)
                           : (top == 1041002 || top == 1041006 || top == 1041010 || top == 1041011);
    }

    public static bool IsDefaultBottom(int job, int gender, int bottom)
    {
        if (job == JobGroupAran) return bottom == 1062115;
        return gender == 0 ? (bottom == 1060002 || bottom == 1060006)
                           : (bottom == 1061002 || bottom == 1061008);
    }

    public static bool IsDefaultShoes(int job, int shoes)
    {
        if (job == JobGroupAran) return shoes == 1072383;
        return shoes == 1072001 || shoes == 1072005 || shoes == 1072037 || shoes == 1072038;
    }

    public static bool IsDefaultWeapon(int job, int weapon)
    {
        if (job == JobGroupAran) return weapon == 1442079;
        return weapon == 1302000 || weapon == 1322005 || weapon == 1312004;
    }

    /// <summary>
    /// 创建角色。字段顺序与源端 CREATE_CHAR 包体一致：
    /// name, job(群), face, hair, hairColor, skinColor, top, bottom, shoes, weapon, gender。
    /// </summary>
    public static Result CreateCharacter(DbStore db, int accountId, int world, string name, int job,
        int face, int hair, int hairColor, int skinColor, int top, int bottom, int shoes, int weapon, int gender)
    {
        // 1) 职业群开关（源端 CreateCharHandler:101-115）
        if (job == JobGroupCygnus && !GameConfigDao.GetBool(db, "enable_knights_of_cygnus", true))
            return Fail(ErrJobDisabled, "骑士团创建已关闭");
        if (job == JobGroupAdventurer && !GameConfigDao.GetBool(db, "enable_adventurers", true))
            return Fail(ErrJobDisabled, "冒险家创建已关闭");
        if (job != JobGroupCygnus && job != JobGroupAdventurer && job != JobGroupAran)
            return Fail(ErrInvalid, "未知职业群");

        // 2) 默认外观白名单
        if (!IsDefaultFace(job, gender, face)) return Fail(ErrInvalid, "非默认脸型");
        if (!IsDefaultHair(gender, hair)) return Fail(ErrInvalid, "非默认发型");
        if (!IsDefaultHairColor(hairColor)) return Fail(ErrInvalid, "非默认发色");
        if (!IsDefaultSkinColor(skinColor)) return Fail(ErrInvalid, "非默认肤色");
        if (!IsDefaultTop(job, gender, top)) return Fail(ErrInvalid, "非默认上衣");
        if (!IsDefaultBottom(job, gender, bottom)) return Fail(ErrInvalid, "非默认下装");
        if (!IsDefaultShoes(job, shoes)) return Fail(ErrInvalid, "非默认鞋子");
        if (!IsDefaultWeapon(job, weapon)) return Fail(ErrInvalid, "非默认武器");

        // 3) 槽位与名字
        if (CharactersDao.ByAccount(db, accountId, world).Count >= AccountsDao.CharacterSlots(db, accountId))
            return Fail(ErrNoSlot, "角色位已满");
        if (!CharactersDao.CanCreateChar(name)) return Fail(ErrBadName, "名字需 4-13 位英文数字");
        if (CharactersDao.NameTaken(db, name)) return Fail(ErrBadName, "名字已被使用");

        // 4) 初始数值（Character.getDefault）
        bool cygnus = job == JobGroupCygnus;
        var rec = new CharRecord
        {
            accountid = accountId,
            world = world,
            name = name,
            level = 1,
            exp = 0,
            str = 12, dex = 5, int_ = 4, luk = 4,
            hp = 50, maxhp = 50, mp = 5, maxmp = 5,
            job = cygnus ? 1000 : 0,             // NOBLESSE / BEGINNER
            skincolor = skinColor,
            gender = gender,
            hair = hair + hairColor,             // 源端把发色加进发型 id
            face = face,
            ap = 0,
            map = cygnus ? MapStartingNoblesse : MapMushroomTown,
            spawnpoint = 0,
            equipslots = 24, useslots = 24, setupslots = 24, etcslots = 24,
            fishLevel = 1,
        };

        // 5) 事务：characters + keymap + quickslot + 背包 + 技能（源端 insertNewChar）
        db.BeginTransaction();
        try
        {
            long id = CharactersDao.Insert(db, rec);
            KeymapDao.SeedDefaults(db, (int)id);
            QuickslotDao.Save(db, accountId, 0);

            // 装备（源端位置 -5/-6/-7/-11，EQUIPPED）
            InventoryDao.InsertItem(db, (int)id, top, InventoryDao.Equipped, EquipPosTop, 1);
            InventoryDao.InsertItem(db, (int)id, bottom, InventoryDao.Equipped, EquipPosBottom, 1);
            InventoryDao.InsertItem(db, (int)id, shoes, InventoryDao.Equipped, EquipPosShoes, 1);
            InventoryDao.InsertItem(db, (int)id, weapon, InventoryDao.Equipped, EquipPosWeapon, 1);
            // 新手指南（ETC 背包第一位）
            InventoryDao.InsertItem(db, (int)id, cygnus ? ItemNoblesseGuide : ItemBeginnersGuide, InventoryDao.Etc, 1, 1);

            db.Commit();
            return new Result { code = Ok, characterId = (int)id };
        }
        catch (Exception e)
        {
            db.Rollback();
            return Fail(ErrInvalid, e.Message);
        }
    }

    static Result Fail(int code, string error) => new Result { code = code, error = error };
}
