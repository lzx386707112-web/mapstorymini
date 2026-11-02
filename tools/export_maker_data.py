#!/usr/bin/env python3
"""把源端制作（Maker）数据导出成运行期紧凑表 Assets/Resources/Maker/MakerTable.txt。

数据来源与取舍：
  1) 配方本体取客户端 `Etc.wz/ItemMake.img` 原样导出。源端 gms-server 的同名数据在
     `db/migration/V1.0.53__maker_insert_data.sql`，但那是 MapleSkillMakerFetcher 从
     **另一版本**的 ItemMake.img 生成的，已经和本工程客户端对不上（例：4250000 的
     req_meso 在 SQL 里是 110000，客户端 IMG 里是 100000）。Maker 窗口的配方、材料、
     金币都是客户端读自己的 ItemMake.img 画出来的，服务端必须按同一份数据校验，
     否则玩家看到的价与实扣的价不一致。所以这里以 IMG 为准。
  2) `ItemMake.img` 没有强化宝石（reagent）的属性表。源端那张 `makerreagentdata` 是
     MapleSkillMakerReagentIndexer 从 Item.wz 抽出来的 43 行常量，这里直接内嵌。

字段语义（全部照源端 ItemInformationProvider 的读取路径）：
  M: category=客户端分页（0 全职业 / 1 战士 / 2 法师 / 4 弓手 / 8 飞侠 / 16 海盗）
     itemid / reqLevel / reqSkillLevel / itemNum / tuc / meso / catalyst（-1=无）
     reqItem / reqQuest / reqEquip —— 源端 getMakerItemEntry 不读这三列（makercreatedata
     的 req_item/req_equip 也没被读），只保留下来给调试用，制作流程不消费。
  R: makerrecipedata 等价物（IMG 的 recipe[]）：itemid 需要 reqItem x count
  O: makerrewarddata 等价物（IMG 的 randomReward[]）；源端没有任何逻辑读它，仅留档。
  S: makerreagentdata：reagentItemId 的强化词条 stat=value
  C: 怪物结晶换算表（ItemInformationProvider.getMakerCrystalFromLeftover 的等价物）：
     源端是 "SELECT dropperid FROM drop_data WHERE itemid=? ORDER BY dropperid" 取第一条，
     再按怪物等级查结晶档位。这里在导出期用 Field/FieldDropTable.txt（drop_data 全量重放）
     + Field/FieldMobs.json（怪物等级）预先算好，运行期就不用再依赖掉落表与怪物表。

用法：
    python3 tools/export_maker_data.py            # 重新导出
    python3 tools/export_maker_data.py --verify   # 只校验现成产物
"""
from __future__ import annotations

import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
WZPY = os.path.expanduser("~/Documents/mxd/Beidou-Server/tool/wz-python")
DATA = os.path.expanduser("~/Documents/mxd/BeiDou-Server/clien/Data")
IMG = "Etc/ItemMake.img"
TARGET = os.path.join(ROOT, "Assets/Resources/Maker/MakerTable.txt")

HEADER = [
    "# 源端 Etc.wz/ItemMake.img 全量导出（tools/export_maker_data.py 生成，勿手改）",
    "# 分类 0=全职业 1=战士 2=法师 4=弓手 8=飞侠 16=海盗",
    "# M|category|itemid|reqLevel|reqSkillLevel|itemNum|tuc|meso|catalyst|reqItem|reqQuest|reqEquip",
    "# R|itemid|reqItem|count",
    "# O|itemid|rewardItem|quantity|prob",
    "# S|reagentItemId|stat|value",
    "# C|leftoverItemId|crystalItemId",
]

DROP_TABLE = os.path.join(ROOT, "Assets/Resources/Field/FieldDropTable.txt")

# 怪物结晶档位（ItemId.BASIC_MONSTER_CRYSTAL_1 .. ADVANCED_MONSTER_CRYSTAL_3）
CRYSTALS = [4260000, 4260001, 4260002, 4260003, 4260004, 4260005, 4260006, 4260007, 4260008]

# makerreagentdata：源端 V1.0.53__maker_insert_data.sql 里的 43 行常量（索引器产物，IMG 无此表）。
REAGENTS = [
    (4250000, "incPAD", 1), (4250001, "incPAD", 2), (4250002, "incPAD", 3),
    (4250100, "incMAD", 1), (4250101, "incMAD", 2), (4250102, "incMAD", 3),
    (4250200, "incACC", 2), (4250201, "incACC", 3), (4250202, "incACC", 5),
    (4250300, "incEVA", 2), (4250301, "incEVA", 3), (4250302, "incEVA", 5),
    (4250400, "incSpeed", 2), (4250401, "incSpeed", 3), (4250402, "incSpeed", 5),
    (4250500, "incJump", 1), (4250501, "incJump", 2), (4250502, "incJump", 3),
    (4250600, "incMaxHP", 10), (4250601, "incMaxHP", 20), (4250602, "incMaxHP", 30),
    (4250700, "incMaxMP", 10), (4250701, "incMaxMP", 20), (4250702, "incMaxMP", 30),
    (4250800, "incSTR", 2), (4250801, "incSTR", 3), (4250802, "incSTR", 5),
    (4250900, "incINT", 2), (4250901, "incINT", 3), (4250902, "incINT", 5),
    (4251000, "incLUK", 2), (4251001, "incLUK", 3), (4251002, "incLUK", 5),
    (4251100, "incDEX", 2), (4251101, "incDEX", 3), (4251102, "incDEX", 5),
    (4251200, "incReqLevel", -1), (4251201, "incReqLevel", -2), (4251202, "incReqLevel", -3),
    (4251300, "randOption", 1), (4251301, "randOption", 2), (4251302, "randOption", 3),
    (4251400, "randStat", 2), (4251401, "randStat", 3), (4251402, "randStat", 5),
]


def val(node, default=0):
    """WzIntProperty -> int；缺失或非数值回退 default。"""
    if node is None:
        return default
    v = getattr(node, "value", None)
    return v if isinstance(v, int) else default


def crystal_for_level(level):
    """ItemInformationProvider.getCrystalForLevel：range=(level-1)/10，档位 = clamp(range-4,0,8)。"""
    rng = (level - 1) // 10
    return CRYSTALS[max(0, min(8, rng - 4))]


def leftovers():
    """掉落表里出现过的所有 etc 物品 → 结晶档位（按最小 dropperid 那只怪的等级）。

    怪物等级取 `Mob.wz/<7 位 id>.img` 的 info/level（源端 LifeFactory.getMonsterLevel 同源）。
    不用 Field/FieldMobs.json：那只是当前已导出地图的小样本，覆盖不到全服掉落的原石。
    """
    first = {}
    with open(DROP_TABLE, encoding="utf-8") as fh:
        for line in fh:
            if not line.startswith("M|"):
                continue
            parts = line.split("|")
            dropper, itemid = int(parts[1]), int(parts[2])
            if itemid // 1000000 != 4:
                continue
            # 源端 ORDER BY dropperid 取第一条
            if itemid not in first or dropper < first[itemid]:
                first[itemid] = dropper

    sys.path.insert(0, WZPY)
    from wzpy import WzImage, WzKey  # noqa: E402

    levels = {}
    for dropper in set(first.values()):
        path = os.path.join(DATA, "Mob", "%07d.img" % dropper)
        if not os.path.exists(path):
            continue
        img = WzImage.from_bytes(open(path, "rb").read(), key=WzKey.for_region("GMS"), name=path)
        img.parse()
        info = img.root.get("info")
        level = val(info.get("level") if info is not None else None, 0)
        if level > 0:
            levels[dropper] = level

    out = []
    for itemid in sorted(first):
        level = levels.get(first[itemid])
        if level is None:
            continue
        out.append("C|%d|%d" % (itemid, crystal_for_level(level)))
    return out


def build():
    sys.path.insert(0, WZPY)
    from wzpy import WzImage, WzKey  # noqa: E402

    raw = open(os.path.join(DATA, IMG), "rb").read()
    img = WzImage.from_bytes(raw, key=WzKey.for_region("GMS"), name=IMG)
    img.parse()
    if img.truncated or img.parse_warnings:
        raise SystemExit("ItemMake.img 解析异常：truncated=%s %s" % (img.truncated, img.parse_warnings))

    head, recipes, rewards = [], [], []
    for cat in img.root.children():
        category = int(cat.name)
        for node in cat.children():
            itemid = int(node.name)
            catalyst = val(node.get("catalyst"), -1)
            head.append("M|%d|%d|%d|%d|%d|%d|%d|%d|%d|%d|%d" % (
                category, itemid,
                val(node.get("reqLevel")), val(node.get("reqSkillLevel")),
                val(node.get("itemNum"), 1), val(node.get("tuc")),
                val(node.get("meso")), catalyst,
                val(node.get("reqItem")), val(node.get("reqQuest")), val(node.get("reqEquip"))))
            recipe = node.get("recipe")
            if recipe is not None:
                for entry in recipe.children():
                    recipes.append("R|%d|%d|%d" % (itemid, val(entry.get("item")), val(entry.get("count"))))
            reward = node.get("randomReward")
            if reward is not None:
                for entry in reward.children():
                    rewards.append("O|%d|%d|%d|%d" % (
                        itemid, val(entry.get("item")), val(entry.get("itemNum"), 1), val(entry.get("prob"))))

    stats = ["S|%d|%s|%d" % row for row in REAGENTS]
    return HEADER + head + recipes + rewards + stats + leftovers()


def verify(path):
    lines = open(path, encoding="utf-8").read().splitlines()
    kinds = {}
    for line in lines:
        if line.startswith("#") or not line:
            continue
        kinds[line[0]] = kinds.get(line[0], 0) + 1
    return kinds


def main():
    if "--verify" in sys.argv:
        kinds = verify(TARGET)
        print("verify", TARGET, kinds)
        return 0
    lines = build()
    os.makedirs(os.path.dirname(TARGET), exist_ok=True)
    with open(TARGET, "w", encoding="utf-8") as fh:
        fh.write("\n".join(lines) + "\n")
    kinds = verify(TARGET)
    print("wrote", TARGET, len(lines), "lines", kinds)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
