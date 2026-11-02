# 制作 Maker（ItemMake 配方 / makerAction 三种操作）

源端：`client/processor/action/MakerProcessor.java`、`server/MakerItemFactory.java`、
`server/ItemInformationProvider.java`、`net/server/channel/handlers/MakerSkillHandler.java`。
本项目：`Assets/Scripts/Server/World/MakerTable.cs`（数据）、`NativeMakerService.cs`（规则）、
`Assets/Scripts/NativeMakerWindow.cs`（窗口）、`tools/export_maker_data.py`（导出）、
`tools/server-check/MakerChecks.cs`（50 项回归）。

## 数据源：为什么不用源端 SQL

`makercreatedata` / `makerrecipedata` 是 `MapleSkillMakerFetcher` 从 `Etc.wz/ItemMake.img`
生成的，源端那份 SQL 与客户端 IMG **不是同一版本**：

- 4250000 的 `req_meso`：SQL `110000`，客户端 IMG `100000`。
- Maker 窗口的配方、材料、金币全由客户端读自己的 ItemMake.img 绘制，服务端按 SQL 校验
  就会出现「界面标 10 万、实扣 11 万」。

所以配方以 `Etc/ItemMake.img` 为准，导出成 `Assets/Resources/Maker/MakerTable.txt`。
`makerreagentdata`（43 行宝石词条）是索引器产物、IMG 里没有，直接内嵌进同一张表；
`makerrewarddata`（IMG 的 `randomReward[]`）源端**没有任何逻辑消费**，只留档。

## IMG 结构（`Etc/ItemMake.img`）

顶层 6 个分类，是职业位掩码：`0` 全职业 / `1` 战士 / `2` 法师 / `4` 弓手 / `8` 飞侠 / `16` 海盗。
每个分类下是 8 位定长 itemid，节点字段：

| 字段 | 含义 | 对应 SQL |
| --- | --- | --- |
| `reqLevel` | 制作要求的角色等级 | `makercreatedata.req_level` |
| `reqSkillLevel` | 制作技能（锻造）等级 | `req_maker_level` |
| `itemNum` | 产出数量 | `quantity` |
| `tuc` | 产出的升级次数 | `tuc` |
| `meso` | 基础费用 | `req_meso` |
| `catalyst` | 刺激剂 id（只有装备有） | `catalyst` |
| `recipe[]` | `item` / `count` 材料表 | `makerrecipedata` |
| `randomReward[]` | `item` / `itemNum` / `prob` | `makerrewarddata`（无人读） |
| `reqItem` / `reqQuest` / `reqEquip` | 仅分类 0 有 | `req_item` / `req_equip`（同样无人读） |

全量 834 条配方、1926 条材料行、98 条随机奖励。

## makerAction 的三种操作

包里的 `type`（源端只认这三种，其它走默认分支当制作处理）：

- `type == 0` 制作装备/道具
- `type == 3` 怪物结晶合成：`toCreate` 是**原石 id**，查表换成结晶 id，配方变成
  「原石 x100 → 结晶 x1」
- `type == 4` 装备分解：读取装备格子 `pos`，只认「该格装备 id 与 toCreate 相同」

### 源端的两个「反直觉但属实」的点

1. **分解与结晶合成都免费**。`generateLeftoverCrystalEntry` / `generateDisassemblyCrystalEntry`
   只给 `cost`（double）赋值，**从不调用 `trimCost()`**，而 `getCost()` 读的是 `reqCost`，
   所以恒为 0。`getMakerDisassembledFee` 算出来的 13.6363% 手续费只出现在配方构造里，
   玩家一分钱不掏。这是既有行为，不要「顺手修好」。
2. **刺激剂 90% 判定失败时材料与金币照扣、成品不给**。`addBoostedMakerItem` 在扣完材料、
   扣完金币**之后**才 roll，失败只返回 false。

## 校验顺序（getCreateStatus）

| 码 | 含义 | 判据 |
| --- | --- | --- |
| `-1` | 配方不存在 | `reqLevel < 0`（IMG 里查不到该 itemid） |
| `1` | 缺材料 | 逐条 `countById < count` |
| `2` | 缺金币 | `meso < reqCost` |
| `3` | 等级不足 | `level < reqLevel` |
| `4` | 制作技能不足 | `getMakerSkillLevel < reqSkillLevel` |
| `5` | 背包满 | `canHoldAllAfterRemoving(add, rmv)` 失败 |
| `0` | 通过 | —— |

制作技能 id：`(job / 1000) * 10000000 + 1007`。冒险家（job 100/200/300/400/500、以及
110/412 这类）都算 0 → 用 `1007`；骑士团 `10001007`；英雄 `20001007`。技能 1007 在
`String/Skill.img` 里叫**锻造**；别和 1003「匠人之魂」（可给已穿戴装备上卷）搞混。

## 费用（use_maker_fee_heuristics 默认 true）

基础费用 = `ItemMake.img` 的 `meso`，再叠加：

- 刺激剂附加费 `StimulantFee`；
- 每颗宝石 `ReagentFee(itemid, reagentId % 10 + 1) × 份数`；
- 最后 `trimCost()`：`(int)(cost / 1000) * 1000`（向下截到千位）。

两条拟合曲线按 `EquipType.getEquipTypeById` 分支：`itemid/100000` 为 13/14 时按千位
（武器 1302/1402…），否则按万位（100 帽 / 104 上衣 / 105 套服 / 106 裤 / 107 鞋 /
108 手套 / 109 盾），其余走武器那组系数。

**注意 `getEquipLevelReq` 读的是装备的 `info/reqLevel`（穿戴等级），不是配方的 `reqLevel`。**
1002028 配方只要 45 级，穿戴要 50 级，附加费按 50 级算（50000+6076.7 → 56000）。
写成配方等级会得到 55000，差一个千位档。

关掉 heuristics（`use_maker_fee_heuristics=false`）时用固定值：刺激剂 14000、宝石 8000×等级。

## 强化宝石规则（removeOddMakerReagents）

- `isMakerReagent` = `id / 10000 == 425`。
- 宝石类型 = `id / 100`；`type < 42502`（攻/魔强化）**只能上武器**
  （`isWeapon` = `1302000 <= id < 1493000`，或配置 `use_maker_permissive_atk_up=true`），
  否则整个制作被拒，提示「攻击力/魔力强化宝石只能用在武器装备上」。
- 同类型只留 **id 最大**（效果最高）的那颗，其余丢弃。
- 去重后每种的份数一律压成 1：Maker 技能每种宝石只吃一颗。
- 槽位数 `getMakerReagentSlots`：装备 `info/reqLevel` `< 78` → 1、`< 108` → 2、否则 3。
  客户端传多了直接 `Math.min` 截断，**不报错**。
- 传入的宝石若持有数量不够，按实际数量下调；为 0 则整颗去掉（不报错）。

## 成品属性（addBoostedMakerItem）

只有走「刺激剂或宝石」这条路才会用这个函数；纯制作走普通 `gainItem`（干净属性）。

1. 有刺激剂先 roll 90%（受 `scroll_chance_rolls` 影响），失败直接返回 false。
2. 取 IMG 基础属性；饰品（`1110000 <= id < 1140000`）升级次数 `<= 0` 时补 3 次。
3. `use_enhanced_crafting`（默认 **false**）开启时：升级次数 +1（GM 且 `use_perfect_gm_scroll`
   除外），再滚一次混沌卷 60 级。
4. 宝石：`incXXX` 直接加属性（`incReqLevel` 跳过、`incMaxHP/MP` → `MHP/MMP`）；
   `randStat` / `randOption` 走 `scrollOptionEquipWithChaos`（前者滚状态类、后者滚攻防速类，
   未开 `use_enhanced_chaos_scroll` 时负值截 0）。
5. 有刺激剂再 `randomizeUpgradeStats`：状态类 +0..2、防/HP/MP +0..5（只加不减）。

## 怪物结晶档位

`getCrystalForLevel(level)`：`range = (level - 1) / 10`，档位 = `clamp(range - 4, 0, 8)`，
对应 `4260000`..`4260008`。

`getMakerCrystalFromLeftover` 在源端是「`SELECT dropperid FROM drop_data WHERE itemid=? ORDER BY
dropperid` 取第一条 → 那只怪的等级 → 查档位」。本项目在**导出期**就把这张表算好（`C|` 行，
1059 条），运行期不再依赖掉落表；怪物等级取 `Mob.wz/<7 位 id>.img` 的 `info/level`，
不要用 `Field/FieldMobs.json`（那只是已导出地图的小样本，覆盖不到全服原石）。

## 窗口

`UI/UIWindow.img/Maker`：`backgrnd` 312x393，另有 `BtStart` / `BtCancel` / `ComboBox1,2` /
`GaugeBar` / `randomMake` / `randomRecipe`，**按钮图没有文字**，窗口路径也是
`"UI/UIWindow.img/%s/backgrnd"` 拼出来的（EXE 字符串池里没有字面量），坐标无反汇编依据。
当前实现：只取底图，控件在空区重排，按钮用中文文字；三个页签「制作 / 分解 / 结晶」
对应上面三种 `type`。

入口：技能窗双击锻造（`id % 10000 == 1007`）。源端是 `MakerSkillHandler` 接 `MAKER_SKILL`
包，不走施法流程。
