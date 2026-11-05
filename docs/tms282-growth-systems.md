> 徽章窗口现行证据与修正见 [第28篇](../../docs/tms282-skill/28-gemstones-symbol-panel.md)。本文旧 UIAuthenticForce 的坐标描述已废止，不能用于推断 EXE。


## 迁移落地记录（2026-11-04，第二轮）

### 已完成
| 层 | 文件 | 内容 |
|---|---|---|
| 数据表 | `Assets/Scripts/Server/World/Tms282GrowthData.cs` | 徽章/极限属性/内在能力/星火/灵魂五套静态表 + 公式求值（u=向上、d=向下） |
| 存档 | `GrowthDao.cs` + `Schema.sql`（3 张新表 + 10 个新列） | `character_growth` / `hyperstats` / `characterpotentials` |
| 服务 | `Native{Symbol,HyperStat,InnerAbility,Flame,Soul}Service.cs` | 五个权威服务，全部走事务 + 预览/提交两段 |
| 协议 | `Protocol.cs` | `ITransport` 5 个方法；徽章/星火/灵魂带交易检查，成长三表走 `RefreshGrowthMirror` |
| 结算 | `NativeGrowthStats.cs` | 五套属性并进 `AdminSetItem` 差量字典（(base+flat)×(100+pct)/100 语义复用） |
| 展示 | `ItemTooltip.cs` | 徽章等级/经验/ARC/AUT、星火每行、灵魂插槽与属性 |
| 自检 | `tools/server-check/GrowthChecks.cs` | 180 项（`--growth`），全量 1844 项通过 |

### 关键口径（照 282）
- **282 服务端不算属性**：极限属性只给 `800004xx` 加技能等级，内在能力只存 `CharacterPotential`，
  星火只打包 `exGradeOption`，灵魂只写 `soulSocketId/soulOptionId`。属性由客户端 `7000xxx` 公式结算；
  本项目在派生层用同一份公式表补，避免两套数值。
- **星火可摇 21 类**：`applyFlameLines` 传 `length-1`，而 `getRandom(int)` 是闭区间 → r ∈ [0,20]。
- **灵魂属性靠名字推导**：`Tms282SoulOptions.optionFor` 先查 GMS 265 英文名，查不到走 265 的
  `soulPotList` 随机兜底。离线复刻成 `Enhancement/SoulOptions.json`（472 条）。
- **徽章槽段**：模板无 islot，源端另存 `getArcaneSymbols/getSacredSymbols`；本项目接成
  `-201..-222`（ARC -201.. / AUT -211.. / Grand -221..），并在 `EquipmentSlotOf` 里前置拦截。

### 已知缺口 / 待决策
1. ~~**灵魂道具不在基线**~~ **已解决**：`Item/Consume/0259.img`（注意是 Consume 不是 Etc）已由
   `tools/ms282/import_soul_items.py` 导入——847 条（2590 卷轴 26 / 2591 灵魂石 821），
   名称 282 中文 769 条 + 265 英文兜底，覆盖 100%。资源连带导入 `_Canvas/0259.img` 与
   `_Canvas/0243.img`（13 条卷轴 icon outlink 到 02432220，6MB 整文件）。
2. **SoulWeaponWz2 新养成线**：282 客户端有 `Etc/SoulWeaponWz2.img`（LevelInfo 99 / GradeInfo 49 /
   BossInfo 357 / TransmitInfo 58）与 `SoulWeaponWz2Option.img`，但 **282 服务端零实现**。
3. **神之子专属**：镶魂分支里 `zeroWeapon` 同步、`getSoulSkillFromSoulID` 授技能、`initSoulMP` 都只在
   Zero 生效，本项目无神之子，未实现。

## 第三轮：UI 落地与回归（2026-11-04）

### 新增文件
| 层 | 文件 | 内容 |
|---|---|---|
| 资源 | `tools/ms282/import_growth_ui.py` | 导入 4 扇窗（结构 + `_Canvas` 像素），BMS→GMS 整文件转码 |
| 资源 | `MapleDashResources/Data/UI/{Enchant282,UIArcaneForce,UIAuthenticForce,UIWindow4}.img` + `_Canvas/` | 共 311 MiB |
| 探查 | `tools/ms282/dump_window.py` | 紧凑导出任意 UI 分支的坐标（跟随 `_outlink` 取真实宽高原点） |
| 窗口 | `Assets/Scripts/NativeGrowthWindows.cs` | `GrowthUi` 底座 + 徽章 / 极限属性 / 内在能力三扇独立窗 |
| 页签 | `NativeEnhancementWindow.cs` | 4 → 6 页签，新增「轮回星火」「灵魂武器」 |
| 入口 | `content/script-overrides/npc/9900001.js` | 角色成长分区新增 5 个入口（36..40） |
| 入口 | `NpcApi.cs` | `cm.openFlame/openSoul/openSymbol/openHyperStat/openInnerAbility` |
| 入口 | `NativeCommandCatalog.cs` + `NativeGmCommandHost.cs` | `!symbol` / `!hyperstat` / `!innerability` / `!flame` / `!soul`（等级 3） |

### Enchant 为什么是两个文件
282 的 `Enchant.img` 是项目旧版的**严格超集**（starForce/potential/traceTransfer 节点完全一致），
但像素挪到了 `_Canvas/Enchant.img`，且 `effect/*`、`fullScreen_*` 有 1734 张是 **format 2050/4098**，
`ImgFile.Rgba` 只认 ARGB4444/RGB565/ARGB1555。原地替换会让现有星力/潜能特效抛
`NotSupportedException`，所以 282 版落地为 `UI/Enchant282.img`，两个文件并存：
前四个页签读旧的（内嵌像素），星火/灵魂读新的（outlink 到 `_Canvas`，bonusStat 86 张 /
soulWeapon 25 张全是 RGB565，可解码）。

### 窗口坐标（实测，可直接抄）
- **极限属性** `UIWindow4.img/HyperStat/Window`（185x318）：`backgrnd`(0,0)、`backgrnd2`(−6,−7)、
  `backgrnd3`(−13,−41)；`BtReduce`(−11,−288)、`BtReset`(−123,−288)、`BtLVup` 12x12；
  预设 1/2/3 各 17x16 @(60|80|100,267)，套用 46x16 @(126,267)。17 条属性名是
  `statList/800004xx` 位图（19x10 左右）。
- **内在能力** `UIWindow4.img/AbilitySetting`（248x388）：`AbilityGrade` 230x28 @(9,162)、
  `BtAbility` 230x18 @(9,194)、`button:Use` 228x36 @(10,343)、预设 12x12 @(177|197|217,263)、
  `vector:OptionPos1..3`=(14,197)/(14,218)/(14,239)、`costPos`(227,321)。
- **徽章** `UIAuthenticForce.img/AutEquip`：`backgrndExtend` 189x330 + `slotPosExtend`
  6 槽 (24,121)(74,141)(124,121)(24,209)(74,229)(124,209)，卡片 `box` 42x83。
- **星火** `Enchant282.img/bonusStat`（418x720）：`itemLT`(9,32)/`itemRB`(418,251)、
  `summaryBox`(67,242) 287x26、`statBox`(10,271) 398x140、`atkPowBox`(10,415)、
  材料格 `costLT`(10,490) 起 38x38 步长 41（9 列 2 行）、`button:confirm`(147,675)。
- **灵魂** `Enchant282.img/soulWeapon`（418x720）：`summaryBox`(66,242) 286x37、
  `detailBox`(10,282) 398x130、`costInvenBox`(11,420)、材料格 (20,444) 起、
  `button:confirmEnchanter` / `button:confirmSoul` 各 129x35 @(147,675)。

### 本轮修掉的回归问题
1. **ARC/AUT 读错来源**：徽章窗原写 `Profile.Attributes.arcaneForce`——那是「基础属性 + AP 分配」的
   副本，不含任何装备/成长加成，永远是 0。改取 `GameLoop.I.player.Attributes`（`EquipLoadout.Apply` 的最终面板）。
2. **成长结算每帧重建字典**：能力值详情窗每帧 `WithWeapon` → `NativeGrowthStats.Accumulate` 会
   遍历背包 + `new Dictionary` + 查道具表。改为按**内容指纹**缓存（O(n) 整数运算、零分配），
   任一来源变化自动失效，不需要 40 个改 `inventory` 的调用点都手动打脏标记。
3. **星火 tooltip 每帧建 List**：`DescribeFlame` 原先每次 `new List<FlameLine>` + `Unpack` 再 new 一次，
   悬停时每帧两次分配。改为单件缓存（key = 实例 + `exGradeOption` + `iIncReq`）。
4. **星火待确认时仍可切页签**：会把未落地的候选结果藏起来，玩家再也点不到「保留/替换」。
   页签切换守卫补 `flame?.pending!=true`。
5. **`NativeGrowthWindows.cs` 首个类被嵌进上一个类**（插入锚点选在文件末尾的 `}` 之前），
   编译能过但外部访问不到 `NativeSymbolWindow`。已按顶层结构重建。

### 口径确认：ARC/AUT 不结算伤害
282 服务端全库没有 `arc`/`aut` 消费（`Mob.java` 无该字段，伤害在客户端算），
本项目 `GameplayCombat.json` 的 7 个怪物也都没有 ARC/AUT 需求字段。所以照 282：
**ARC/AUT 只做数值展示（徽章窗 / 能力值），不参与伤害公式**，不引入本项目独有的衰减规则。

### 待办
- `_Canvas/Enchant.img`（215 MiB）与 `_Canvas/UIWindow4.img`（108 MiB）里绝大部分画布本项目不用
  （effect / fullScreen_* / Stat / battleStats），可用 wzpy.writer 裁成子集，体积能压到 10 MiB 量级。
- SoulWeaponWz2 新养成线仍未开工（用户已拍板「按客户端数据自己设计」）。

## 能力值来源联查（2026-11-05）

核对内在能力（当前预设、等级解锁行）、极限属性（当前预设）、符文、星火、灵魂、主附潜能与称号试炼：常规四维/HP/MP/攻魔/防御均进入 EquipLoadout.Apply，伤害/首领/无视防御等由 setItemBonus 进入 CombatRules。称号仅当前佩戴系列的已点亮档生效，非所有系列叠加。

StatWindow 之前四维取基础 Profile.Attributes，故所有上述四维加成都可能不显示。首次修复取 WithWeapon 后，续查进一步发现临时 WATK/MATK、冒险岛勇士、防御与命中增益、特殊核心只在 PlayerCtrl.RefreshAttributes 中合并。现新增 ReadCombatAttributes 只刷新并返回该最终结果及攻魔，StatWindow 四维/详情统一使用，HP/MP上限也从同一结果读取，不再另算静态面板。未修改原IMG布局、AP加点、成长规则或称号设计。

边界：既有成长实现仍将 STR2DEX 等转换、攻速、加持时间、无冷却、被动等级等放入 unsettled，未实现对应机制；不能把常规结算检查通过等同于全部内在能力有效。本次未猜测补写这些规则。旧能力值九行版式没有暴击/首领/无视等独立显示行；这些有战斗结算的字段不能仅据未显示判断不生效。速度/跳跃显示仍为旧硬编码100，需原端显示口径与当前移动限制进一步比对，未擅自添加布局。

## 命令直升级后的极限点数（2026-11-05）

服务端 Snapshot 按 character.level 调用 TotalSp，每级从140含起累计 `3+(lv-140)/10`；200级总计339，无需逐级发点。GM LevelCommand 已 SaveToDb，但 NativeHyperStatWindow 只在打开/操作/切页时读取，面板开着改等级仍缓存旧 totalSp。修复 Draw 检查响应 level 与 Profile.level，不同则重读当前查看预设；Load 在角色镜像等级落后时先 SaveToDb，覆盖普通升级后尚未保存的情况。保留已消费点数，不额外重复发点。增加真实DAO139直接改200的服务端回归，断言可用339点；该检查不能代替Android显示验收。
