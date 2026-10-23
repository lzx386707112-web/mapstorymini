# 职业系统接入分析（BeiDou 源端 → MapleDash 客户端）

> 本文只做**事实描述与差距判定**，所有数值都标了源端文件行号，方便逐条复核。
> 结论在最后一节，实施切分在第 11 节。

---

## 0. 摘要

源端是一套**标准 v83 冒险家体系**，职业成长完全由「等级 + 能力值 + 道具 + 任务 + NPC 脚本」四类条件驱动，**没有任何"达到等级自动转职"的逻辑**
（`Job.changeJobByLevel()` 存在，但唯一调用点是转生 `Character.changeJobAndLevel()`，见 `Character.java:10620`）。

冒险家可玩职业 **28 个 job id**：新手 `0`，五系一转 `100/200/300/400/500`，各自 2/3/4 转共 21 个分支。

源端 `Job` 枚举里还有骑士团（1000/1100~1512）、战神（2000/2100~2112）、龙神（2200~2218）：

- 客户端 `Skill.wz`（`MapleDashResources/Data/Skill`，76 个 img）**包含了**冒险家全 28 job、骑士团 `1000/1100~1512`、战神 `2000/2100~2112`，缺龙神 `2200~2218`；
- 但**转职 NPC 脚本只覆盖冒险家五系**的 1~4 转（`scripts-zh-CN/npc/1022000、1032001、1012100、1052001、1090000、2020008~2020011、2081100~2081500`），全目录 `grep changeJobById(1[1-5]00)` 无命中 → 骑士团/战神的转职入口脚本不在包里；
- 骑士团/战神还需要各自的新手村地图与 5 转流程（`hasCompletedCygnusFifthJob`），成本远高于冒险家。

**本次接入范围建议锁定冒险家五系（job 0 + 21 个 1~4 转分支）**，其余只留 `JobDefs` 数据结构位，不做内容。

---

## 1. 职业数据模型

### 1.1 Job 枚举（`client/Job.java:28-66`）

| id | 名称（message_zh_CN.properties 解码） | 体系 | 层级 |
|---|---|---|---|
| 0 | 新手 | beginner | 0 |
| 100 / 200 / 300 / 400 / 500 | 战士 / 魔法师 / 弓箭手 / 飞侠 / 海盗 | 一转 | 1 |
| 110 / 120 / 130 | 剑客 / 准骑士 / 枪战士 | 战士 | 2 |
| 111 / 121 / 131 | 勇士 / 骑士 / 龙骑士 | 战士 | 3 |
| 112 / 122 / 132 | 英雄 / 圣骑士 / 黑骑士 | 战士 | 4 |
| 210 / 220 / 230 | 法师（火毒）/ 法师（冰雷）/ 牧师 | 法师 | 2 |
| 211 / 221 / 231 | 巫师 / 巫师 / 祭司 | 法师 | 3 |
| 212 / 222 / 232 | 魔导师 / 魔导师 / 主教 | 法师 | 4 |
| 310 / 320 | 猎人 / 弩弓手 | 弓箭手 | 2 |
| 311 / 321 | 射手* / 游侠* | 弓箭手 | 3 |
| 312 / 322 | 神射手 / 箭神* | 弓箭手 | 4 |
| 410 / 420 | 刺客 / 侠客 | 飞侠 | 2 |
| 411 / 421 | 无影人 / 独行客 | 飞侠 | 3 |
| 412 / 422 | 隐士 / 侠盗 | 飞侠 | 4 |
| 510 / 520 | 拳手 / 火枪手 | 海盗 | 2 |
| 511 / 521 | 斗士 / 大副 | 海盗 | 3 |
| 512 / 522 | 冲锋队长 / 船长 | 海盗 | 4 |

\* 源端 `i18n/message_zh_CN.properties` 对 311/321/322 的译名（射手/游侠/箭神）与 `String.wz/Skill.img` 的 `bookName`（射手技能/…）不完全一致，客户端 UI 以 `JobSkills.json.jobNames` 为准时要注意统一。

### 1.2 判定工具（全部在 `Job.java`）

| 方法 | 语义 | 用途 |
|---|---|---|
| `isA(Job base)` (`:111`) | 是否属于该分支且 ≥ 该阶（按 /10 或 /100 前缀比较） | 成长曲线、HP/MP、装备门槛 |
| `isTargetJob(Job)` (`:478`) | 同前缀（/10 或 /100） | `getBaseJobType` 的基础 |
| `getBaseJobType(Job)` (`:428`) | 归一到分支根（如 112 → 111 的父 110 分支 → 返回 FIGHTER） | 转生时重算职业 |
| `getJobBranch(Job)` (`GameConstants.java:452`) | `%1000==0`→0（新手）；`%100==0`→1（一转）；否则 `2+%10`（二/三/四转） | SP 上限、等级上限 |
| `getJobNiche()` (`:116`) | `(id/100)%10` → 0 新手 /1 战士 /2 法师 /3 弓 /4 飞 /5 海盗 | 队伍、掉落、商店分类 |
| `getJobTier()` (`:129`) | 0/1/2/3/4 阶 | UI |
| `getJobMaxLevel(Job)` (`GameConstants.java:464`) | 0→10，1→30，2→70，3→120，其它 255 | 仅当 `use_enforce_job_level_range=true` 生效（默认 **false**） |
| `getSkillBook(job)` (`GameConstants.java:485`) | 只有龙神 2210~2218 有独立技能书 1~9，其余全 0 | SP 分书 |
| `getJobStyleInternal(jobid, opt)` (`:144`) | 归到 WARRIOR/MAGICIAN/BOWMAN/CROSSBOWMAN/THIEF/GUNSLINGER/BRAWLER | 伤害主属性、HP/MP 成长 |
| `getBy5ByteEncoding` (`:94`) | 登录包里 5 字节职业编码 | 仅协议层，单机不需要 |

### 1.3 明确否掉的假设

- **不存在"按等级自动转职"**：`changeJobByLevel` 只在 `changeJobAndLevel`（转生/重算）里用（`Character.java:10620`）。
- **不存在"一转前不能升级"**：`use_enforce_job_level_range=false`（`V1.7.0__create_game_config.sql:68`）。
- **不存在"新手拿 SP"**：`levelUpGainSp()` 第一行 `getJobBranch(job)==0` 直接 return（`Character.java:6268`）。

---

## 2. 成长数值链

### 2.1 建号（`Character.getDefault` `Character.java:581-602`）

```
hp = 50, maxHp = 50, mp = 5, maxMp = 5
attrStr = 12, attrDex = 5, attrInt = 4, attrLuk = 4
job = BEGINNER, level = 1
四个背包各 24 格
```

新手出生图：`MapId.MUSHROOM_TOWN (10000)`，`use_beidou_beginner_map=false` 时不走青苹果地图
（`creator/novice/BeginnerCreator.java:46-55`）。出生自带 `ItemId.BEGINNERS_GUIDE` 1 个。

> 客户端对应：`CharacterAttributes` 默认值 `level=1, str=12, dex=5, intelligence=4, luk=4, maxHp=50, maxMp=5`，**已一致**。

### 2.2 新手期自动配点（`Character.java:6291-6311`）

`use_auto_assign_starters_ap=true`，所以 `isBeginner && level < 11` 时：

```
gainAp(5)                                  // 仍然 +5 AP
level < 6  → assignStrDexIntLuk(5,0,0,0)   // 全加 STR
level >= 6 → assignStrDexIntLuk(4,1,0,0)   // 4 STR + 1 DEX
```

点数是"发了立刻花掉"，所以新手身上不攒 AP。到 10 级累计：

| 等级 | STR | DEX | INT | LUK |
|---|---|---|---|---|
| 1 | 12 | 5 | 4 | 4 |
| 6 | 37 | 5 | 4 | 4 |
| 10 | 53 | 9 | 4 | 4 |

一转瞬间 `resetStats()` 会把这 70 点（53+9+4+4）重新分配（见 4.1）。

> 客户端 `LevelSystem.LevelUp` 已实现同一规则（`d.level - 1 < 6 ? 5 : 4`），一致。

### 2.3 升级（`Character.levelUp` `Character.java:6282-6440`）

| 项 | 值 | 依据 |
|---|---|---|
| AP | `level_up_ap_gain = 5` / 级 | `:6313-6329` |
| SP | `level_up_sp_gain = 3` / 级，**仅非新手** | `:6267-6280` |
| HP/MP | 见下表（`Randomizer.rand` 闭区间） | `:6331-6371` |
| 连续升级 | `use_level_up_protect=true` 一次结算只升 1 级 | 配置 |
| 满级 | `mxj_max_level=255`，经验封顶 99% | `:5348-5369` |

HP/MP 成长表（每级）：

| 分支 | HP | MP |
|---|---|---|
| 新手 | 12~16 | 10~12 |
| 战士系 | 24~28 | 4~6 |
| 法师系 | 10~14 | 22~24 |
| 弓/飞 | 20~24 | 14~16 |
| 海盗系 | 22~28 | 18~23 |
| 战神 | 44~48 | 4~8 |

叠加项：
- 战士/海盗的 `IMPROVED_MAXHP`（`Warrior.IMPROVED_MAXHP` / `Brawler.IMPROVE_MAX_HP` / 骑士团对应）每级额外 `getX()` 点 HP；
- 法师的 `IMPROVED_MAX_MP_INCREASE`、勇士（1210000）、魂骑士 2 转额外 MP；
- `use_randomize_hpmp_gain=true`：非法师再加 `INT/10`，法师加 `INT/20`（`:6366-6372`）。

> 客户端 `LevelSystem` 复刻了区间与 INT 加成，但**没做技能加成**（improved maxHP/MP 技能），接职业系统后要补。

### 2.4 转职（`Character.changeJob` `Character.java:1270-1420`）

每次转职：

| 项 | 规则 | 代码 |
|---|---|---|
| SP | 基础 +1；若 `newJob % 10 == 2`（四转）再 +2 → 共 3 | `:1294-1302` |
| AP | `newJob % 100 >= 1` 才 +5 → **一转（100/200/300/400/500）不给 AP**，二/三/四转各 +5 | `:1305-1316` |
| 背包 | 4 个栏各 +4 格 | `:1319-1323` |
| HP/MP | 见下表 | `:1325-1352` |

转职瞬间 HP/MP 加成（`job_ = id % 1000`）：

| 条件 | HP | MP |
|---|---|---|
| `job_ == 100`（一转战士） | +200~250 | — |
| `job_ == 200`（一转法师） | — | +100~150 |
| `job_ % 100 == 0`（一转弓/飞/海盗） | +100~150 | +25~50 |
| `0 < job_ < 200`（2~4 转战士） | +300~350 | — |
| `200 < job_ < 300`（2~4 转法师） | — | +450~500 |
| 其余（2~4 转其他） | +300~350 | +150~200 |

转职还会：广播转职特效、重算 `setMasteries`（四转送部分技能到 10 级）、重置坐骑 buff、队伍/公会同步。

### 2.5 SP 上限（仅 `use_enforce_job_sp_range=true` 生效，**默认 false**）

`GameConstants.java:33-34`：

```
jobUpgradeBlob = {1, 20, 60, 110, 190}   // 各阶等级跨度
jobUpgradeSpUp = {0, 1, 2, 3, 6}         // 各阶转职补正

getJobLevelSp(level, job, branch) = 3 * level + spUp[branch]   // 法师 level 再 +2（8 级转职）
getJobMaxSp(job) = getJobLevelSp(blob[branch], job, branch)
```

| 阶 | SP 理论上限 |
|---|---|
| 一转（branch 1） | 3×20+1 = **61** |
| 二转（branch 2） | 3×60+2 = **182** |
| 三转（branch 3） | 3×110+3 = **333** |
| 四转（branch 4） | 3×190+6 = **576** |

`getChangedJobSp`（`:6192`）在转职时按「3×(level-10)+spUp」补差额 —— 因为默认不启用，客户端**不需要**实现，但要知道为什么不能用 `3×(level-1)` 反推 SP。

---

## 3. 四阶转职：条件、脚本、奖励

所有条件都在 NPC 脚本里，服务端只提供 `cm.*` 原语（`AbstractPlayerInteraction.java:1350-1393`）。

### 3.1 一转

| 职业 | NPC | 地图 | 等级 | 能力值 | 脚本 |
|---|---|---|---|---|---|
| 战士 | 1022000 武术教练 | 102000003 战士圣殿 | 10 | STR ≥ 35 | `npc/1022000.js` |
| 魔法师 | 1032001 汉斯 | 101000003 魔法图书馆 | **8** | INT ≥ 20 | `npc/1032001.js` |
| 弓箭手 | 1012100 赫丽娜 | 100000201 弓箭手教室 | 10 | DEX ≥ 25 | `npc/1012100.js` |
| 飞侠 | 1052001 达克鲁 | 103000003 盗贼基地 | 10 | DEX ≥ 25 | `npc/1052001.js` |
| 海盗 | 1090000 卡伊琳 | 120000000 诺特勒斯 | 10 | DEX ≥ 20 | `npc/1090000.js` |

能力值门槛：`canGetFirstJob(jobType)`（`AbstractPlayerInteraction.java:1350`），文案 `getFirstJobStatRequirement`。

流程（以 `1022000.js:126-145` 为例）：

```
level >= 10 && canGetFirstJob(1)
  → changeJobById(100)
  → gainItem(1302077, 1)      // 对应职业的一转武器
  → resetStats()              // 重排能力值 + 发 SP
```

一转赠礼：

| 职业 | 物品 |
|---|---|
| 战士 | 1302077 新手战士之剑 |
| 法师 | 1372043 初级魔法师的杖 |
| 弓手 | 1452051 初级弓手的弓 + 2060000 弓矢 ×1000 |
| 飞侠 | 1472061 初级飞侠拳套 + 1332063 初级飞侠的短剑 + 2070015 初学者标 ×500 |
| 海盗 | 1492000 短枪 + 1482000 指套 + 2330000 子弹 ×1000 |

### 3.2 二转（`Lv ≥ 30` + 已一转 + 英雄证书 `4031012`）

1. 找一转教官 → 若未持证书：发信件（战士 `4031008`、法师 `4031009`、弓手 `4031010`、飞侠 `4031011`），并 `startQuest(100003)`；
2. 去 `1072000`（战士教官，`102020300`；其余职业同构 NPC）→ 进试炼地图 `108000300`；
3. 打怪收集 **30 个 `4031013` 黑珠** → 与地图内同事对话换 `4031012` 英雄证书；
4. 回教官 → `sendSimple` 选分支（战士：剑客 110 / 准骑士 120 / 枪战士 130）→ `changeJobById`，扣证书，`completeQuest(100005)`。

海盗二转额外判断 `isQuestCompleted(2191)` 或 `2192`（`1090000.js:81-86`）。

> 单机可裁剪：试炼地图与 30 个黑珠是可选环节，若要 1:1 还原就必须把 `108000300` 这类副本地图纳入资源。

### 3.3 三转（`Lv ≥ 70` + `job % 10 == 0`）

1. 一二转教官：`gotPartyQuestItem("JB3")` 后给任务，去秘密地图打「教官的分身」；
2. 击败分身掉 `4031059` 黑符 → 回教官 → 换 `4031057` 力气项链；
3. 去三转教官（战士 `2020008`、法师 `2020009`、弓手 `2020010`、飞侠 `2020011`、海盗 `2020013`）→
   再进一次试炼拿 `4031058` 智慧项链 → `changeJobById(jobId + 1)`。

`2020008.js:58-105`：

```
Lv >= 70 && jobBase == jobStyle && jobId % 10 == 0
  → haveItem(4031058) ? changeJobById(jobId+1) : ...
```

### 3.4 四转（`Lv ≥ 120` + 三转 + 任务 6904 完成）

`2081100.js:50-80`（战士；`2081200~2081500` 对应法/弓/飞/海盗）：

```
level < 120 || jobId/100 != 分支号   → 拒绝
!isQuestCompleted(6904)              → 拒绝（必须先过四转考验任务链）
jobId % 100 % 10 != 2               → changeJobById(jobId+1)
                                    → teachSkill(1121001/1120004/1121008, 10)
                                    → gainItem(2280003, 1)   // [技能册]冒险岛勇士
```

四转后还会开 `sendSimple("教我我的职业技能")` 分支，把三转遗留技能补到 10 级（`2081100.js:81-110`）。

### 3.5 任务与状态标记

| 标记 | 用途 |
|---|---|
| `100003` / `100004` / `100005` | 二转流程（当前 WZ 里**没有**这三条，属服务端自定义标记，缺失时 `cm.startQuest` 在客户端应退化为纯标记） |
| quest `6904` | 四转考验（`Quest.wz/Check.img` 中存在） |
| `gotPartyQuestItem("JB3"/"JBP")` | 三转进度（`Character` 上的内存标记，落库在 quest 进度） |
| `hasCompletedCygnusFifthJob` | 骑士团五转（`ExtendValue` 表），冒险家无关 |

---

## 4. 能力值

### 4.1 一转 `resetStats()`：能力值重排（`Character.java:7986-8046`）

```
tap = remainingAp + STR + DEX + INT + LUK      // 全部可用点数
tsp = 1 + (level - 10) * 3                     // 法师是 (level - 8) * 3
目标值：
  100/1100/2100 → STR 35
  200/1200      → INT 20
  300/400/1300/1400 → DEX 25
  500/1500      → DEX 20
tap -= 目标四维之和；tap < 0 则拒绝（日志警告），否则
  updateStrDexIntLukSp(目标四维, tap 剩余 AP, tsp 剩余 SP)
```

以 10 级战士为例：tap = 70，目标 35/4/4/4 = 47 → 转职后 STR 35，**剩 23 AP**、SP 1。

### 4.2 AP 来源汇总

| 来源 | 值 |
|---|---|
| 建号 | 12/5/4/4（非 4/4/4/4，`use_starting_ap_4=false`） |
| 每级 | +5（新手期自动加掉） |
| 二/三/四转 | 各 +5 |
| 一转 | +0（但 resetStats 会把新手期的分配重排） |
| 上限 | `max_ap = 32767` |

> 客户端现状：`Profile.AssignAp` 只做 +1 且没有 `resetStats`，`AutomaticAp` 判定 `cls=="beginner" && level<=10`
> —— 判定条件对，但缺少"转职后重排 + 剩余 AP + 一转 SP"这一整段。

### 4.3 主/副属性与武器倍率（`Character.calculateMaxBaseDamage` `:854-871`）

```
弓 / 弩 / 枪        → 主 DEX，副 STR
拳套 / 短刀(飞侠用) → 主 LUK，副 DEX+STR
其余                → 主 STR，副 DEX
max = ceil((mult * main + secondary) / 100 * watk)
```

`WeaponType` 倍率（`client/inventory/WeaponType.java:24-44`）：

| 类型 | 倍率 | 类型 | 倍率 |
|---|---|---|---|
| 单手剑 SWORD1H | 4.0 | 双手剑 SWORD2H | 4.6 |
| 单手斧/钝 GENERAL1H_SWING | 4.4 | 双手斧/钝 GENERAL2H_SWING | 4.8 |
| 单手刺 GENERAL1H_STAB | 3.2 | 双手刺 GENERAL2H_STAB | 3.4 |
| 弓 BOW | 3.4 | 弩 CROSSBOW | 3.6 |
| 拳套 CLAW | 3.6 | 短刀 DAGGER_THIEVES | 3.6 |
| 火枪 GUN | 3.6 | 指套 KNUCKLE | 4.8 |
| 枪 SPEAR_STAB / 矛 POLE_ARM_SWING | 5.0 | 枪挥 / 矛刺 | 3.0 |
| 杖 STAFF | 3.6 | 短杖 WAND | 3.6 |

魔法上界：`calculateMaxBaseMagicDamage(matk)`（`:889`），总魔攻含 INT。

### 4.4 命中 / 回避

`recalcLocalStats` 中：命中 = `DEX*0.8 + LUK*0.5 + 装备 incACC`；回避 = `DEX*0.25 + LUK*0.5 + incEVA`（客户端 `GameWindows.cs:129-130` 已按此显示）。

---

## 5. 技能

### 5.1 SP 规则

| 项 | 值 |
|---|---|
| 每级 SP | 3（`level_up_sp_gain`），新手不给 |
| 转职 SP | +1；四转 +3 |
| 1 技能点 = | 1 技能等级 |
| 技能书 | 冒险家全 0（共享一池）；只龙神分 9 本 |

客户端当前 `SkillProgress.TotalSp = 3 * (level-1)`（`SkillBook.cs:163`）**与源端不符**：
源端是"进入职业分支后每级 +3，转职时 +1/+3"的累计值。正确做法是把它改成**累计字段**（与 AP 同构），
而不是用等级反推。校验值：1 转后在 30 级时 SP 应为 `3*(30-10)+1 = 61`，正好等于 `getJobMaxSp(branch 1)`。

### 5.2 新手虚拟 SP（`AssignSPProcessor.java:71-80`）

新手技能（`id % 10000000 ∈ [1000, 1002]`，即 `1000/1001/1002`）不消耗真实 SP，而是用

```
remainingSp = min(level - 1, 6) - (1000 级 + 1001 级 + 1002 级 之和)
```

即新手最多 6 点，只够点这 3 个技能。客户端已用 `Clamp(level-1, 0, 6)` 近似，接职业系统后要精确到"只算这三个技能"。

### 5.3 技能归属与前置（`GameConstants.isInJobTree` `:529-549`）

- 技能 id / 10000 = 所属 job；
- 只能加**当前 job 树**内的技能（`isInJobTree`），且要求 `skillJob <= jobId`（不能提前点高阶技能）；
- `isSpecialSkills`（二段跳/瞬移/神圣之光/…）例外，允许跨职业保存到键盘。

### 5.4 技能上限与四转 masterLevel

- 普通技能：`Skill.wz/<job>.img/skill/<id>/common/maxLevel`；
- 四转技能（`isFourthJob`，即 `id/100%10==2`）：加点受 `masterLevel` 限制，默认 10，靠技能书（`228xxxx`）提升到 20/30；
- `changeJob` 后 `setMasteries(jobId)`（`Character.java:1146-1258`）会把核心四转技能直接给到 10 级。

> 客户端 `SkillBook` 已读 `maxLevel` 与 `req`（前置），**没有 masterLevel 概念**，四转技能会被一次性点到 30。

### 5.5 技能数据现状

`Assets/Resources/UI/JobSkills.json`（210 技能）覆盖：

```
job 0: 4      100: 6     110: 8     111: 9     112: 21
              130: 8     131: 9     132: 20
              200: 6     210: 6     211: 7     212: 22
              230: 6     231: 7     232: 28
              300: 6     310: 6     311: 7     312: 24
```

**缺**：`120/121/122`（准骑士线）、`220/221/222`（冰雷线）、`320/321/322`（弩弓手线）、`400/410/411/412/420/421/422`（飞侠线）、`500/510/511/512/520/521/522`（海盗线）。
注意 `Skill.wz` 里这些目录**都存在**，只是导出器当初只导了 6 条玩具职业线。
`jobNames` 也只有 0~322，缺 400~522 的职业名（可从 `message_zh_CN.properties` 的 `job.name.*` 补齐）。

`NativeAssetIndex.json` 的 `skillicon/*` 别名只有 **205** 条，与新职业不匹配 —— 需要重新生成索引。

---

## 6. 装备与武器

### 6.1 reqJob 位掩码

`Character.wz/<类别>.img/<id>/info/reqJob` 是位掩码：

| 值 | 含义 |
|---|---|
| 0 | 全职业 |
| 1 | 战士 |
| 2 | 法师 |
| 4 | 弓箭手 |
| 8 | 飞侠 |
| 16 | 海盗 |
| 组合（如 3） | 多职业可穿 |

客户端 `WeaponDefs.CanEquip`（`WeaponDefs.cs:74-82`）只映射了 `beginner→0 / mage,priest→2 / archer→4 / 其它→1`，
**飞侠(8)、海盗(16) 全部错判成战士**，这是接职业系统必须一起改的。

同时缺 `reqLevel` / `reqSTR` / `reqDEX` / `reqINT` / `reqLUK` 之外的装备门槛（`reqJob` 对防具同样生效）。

### 6.2 装备属性叠加

`CharacterAttributes.WithWeapon`（`NativeCombatData.cs:31-45`）已按 `incSTR/incDEX/incINT/incLUK/incMHP/incMMP/incPDD/incMDD/incACC/incEVA/incPAD/incMAD` 叠加，
但**只对武器生效**。防具（帽/衣/裤/鞋/手套/披风/盾/戒指/项链）在客户端还没有装备槽位模型
（存档只有 `equipWeapon` 一个字段 + `equipmentSlots` 数量）。

---

## 7. 相关物品清单

| id | 名称 | 用途 | 来源 |
|---|---|---|---|
| 4031008 | 武术教练的信件 | 战士二转引荐信 | 1022000 |
| 4031009 | 汉斯的信件 | 法师二转引荐信 | 1032001 |
| 4031010 | 赫丽娜的信件 | 弓手二转引荐信 | 1012100 |
| 4031011 | 达克鲁的信件 | 飞侠二转引荐信 | 1052001 |
| 4031012 | 英雄证书 | **二转必需** | 二转试炼 NPC |
| 4031013 | 黑珠 | 二转试炼收集 30 个 | 试炼地图怪物 |
| 4031057 | 力气项链 | 三转第一阶段凭证 | 一二转教官 |
| 4031058 | 智慧项链 | **三转必需** | 三转教官 |
| 4031059 | 黑符 | 击败教官分身掉落 | 分身体 |
| 2280003 | [技能册]冒险岛勇士 | 四转奖励 | 四转教官 |
| 1302077 | 新手战士之剑 | 一转赠礼 | 1022000 |
| 1372043 | 初级魔法师的杖 | 一转赠礼 | 1032001 |
| 1452051 / 2060000 | 初级弓手的弓 / 弓矢×1000 | 一转赠礼 | 1012100 |
| 1472061 / 1332063 / 2070015 | 初级飞侠拳套 / 初级飞侠的短剑 / 初学者标×500 | 一转赠礼 | 1052001 |
| 1492000 / 1482000 / 2330000 | 短枪 / 指套 / 子弹×1000 | 一转赠礼 | 1090000 |

转职相关地图：

| 地图 | 用途 |
|---|---|
| 102000003 / 101000003 / 100000201 / 103000003 / 120000000 | 五系一转教官所在地 |
| 102020300 等 | 二转考验 NPC（1072000 等）所在地 |
| 108000300 等 | 二转试炼副本 |
| 三转教官所在地 | 2020008~2020013 |
| 四转教官所在地 | 2081100~2081500 |

**客户端当前 `FieldMaps.json` 只有 37 张图，全部是枫之岛新手村（0~60001、1000000~2000001），
上述城镇/副本一张都没有** —— 这是"转职走地图找 NPC"路线最大的资源缺口。

---

## 8. 客户端现状（文件级）

| 文件 | 已有能力 | 与职业系统的关系 |
|---|---|---|
| `ClassDefs.cs` | 6 个玩具职业（beginner/knight/warrior/mage/archer/priest），每职业 4 个写死技能 | **要整体替换**为 28 个真实 job |
| `Profile.cs` | `cls` 字符串、AP 加减、`SetClass` | 要改成 `jobId`（int）+ 累计 `sp` + `resetStats` |
| `PlayerProfileData.cs` | 等级/HP/MP/AP/物品/任务/武器 | 要加 `jobId`、`sp`、`skillLevels`、装备槽 |
| `LevelSystem.cs` | 升级 AP/HP/MP、新手自动配点 | 要加 SP 累计、转职时 HP/MP/SP/AP |
| `SkillBook.cs` | 按 `cls` 取"职业线"，页签=职业阶段；`SkillProgress` 用 `PlayerPrefs` 存等级 | 要改成按 `jobId` 取当前+历史页签；`masterLevel`；SP 累计 |
| `SkillWindow.cs` | 完整原版技能窗（页签/书名/拖拽） | 页签过滤要按已转职阶段 |
| `GameWindows.cs` (StatWindow) | 能力值窗 + 详情窗，职业图标用 `job/100` | 职业图标与职业名已能走 job id |
| `WeaponDefs.cs` | 武器目录（`GameplayCombat.json`）+ `CanEquip` | 位掩码要按真实职业改 |
| `Npc/NpcApi.cs` | 跑源端 NPC 脚本的 `cm` 原语；**`canGetFirstJob=false`、`resetStats` 空实现、`ChangeJob` 只映射到 6 个玩具职业** | 转职流程的主要堵点 |
| `Quest/QuestLogic.cs` | `JobId(cls)` 硬映射 5 个职业 | 改成直接读 `Profile.jobId` |
| `Npc/NpcField.cs` | NPC 交互/任务标记 | 可直接用 |
| `tools/sync_npc_scripts.py` | 已把 `scripts-zh-CN` 的 npc/quest 脚本同步进 Resources | 转职脚本**已经在包里** |

已具备的关键基础设施（这次接入的有利条件）：

1. **NPC JS 引擎**（`Scripting/JsEngine` + JParser）能直接跑源端脚本，转职脚本无需重写；
2. **Quest.wz 运行时读取**，`6904` 等任务的 Check/Act 可直接用；
3. **String.wz 名字解析**（`WzNames.Item/Npc/Map`），新增道具/地图无需预导出名字；
4. **技能图标别名 → Skill.wz canvas**，新增技能只要补 `NativeAssetIndex.json` 别名。

---

## 9. 差距矩阵

| 能力 | 源端 | 客户端 | 差距 |
|---|---|---|---|
| 职业 id 模型 | 35 job | 6 字符串职业 | ❌ 重建 |
| 新手→一转→四转 | NPC 脚本 | 大厅直接选职业 | ❌ 重建 |
| 一转能力值门槛 | STR35/INT20/DEX25/20 | 无 | ❌ |
| 一转 resetStats | 重排+剩余AP+SP | 无 | ❌ |
| 升级 SP | 3/级（非新手） | 3×(level-1) 反推 | ⚠️ 公式错 |
| 转职 AP/HP/MP/格子 | 有 | 无 | ❌ |
| 技能树数据 | 全部 | 只 6 条线（210 技能） | ⚠️ 缺 22 个 job |
| 技能前置 | req | 已有 | ✅ |
| masterLevel / 技能书 | 有 | 无 | ❌ |
| 新手虚拟 SP | min(lv-1,6)-3技能 | 近似 | ⚠️ |
| 装备 reqJob 位掩码 | 1/2/4/8/16 | 只 0/1/2/4 | ⚠️ 飞侠海盗错 |
| 防具槽位 | 10+ 槽 | 只有武器 | ❌ |
| 转职道具链 | 4031008~4031059 | 无 | ❌ |
| 转职 NPC/城镇地图 | 有 | 无（37 张新手岛图） | ❌ |
| 转职任务 100003/6904 | 脚本+WZ | 脚本有、100003 无 WZ | ⚠️ |
| 伤害主副属性 | 按武器类型 | 按武器类型 | ✅ |
| 技能成长加成（improved HP/MP） | 有 | 无 | ❌ |

---

## 10. 关键结论（给实施用）

1. **职业主键必须是 job id（int），不是字符串**。现有 `cls` 字符串只能靠一层映射表兼容旧存档。
2. **SP 必须改成累计值**，不能再用 `3*(level-1)` 反推，否则四转满技能时点数必然算错。
3. **转职脚本已经在包里**，缺的是 `cm` 原语（`canGetFirstJob` / `getFirstJobStatRequirement` / `resetStats` / `changeJobById` 的真实实现）。
4. **地图是真缺口**：不补城镇图，就只能走"简化转职入口"（见 11.4 方案 B）。
5. **技能数据要重新导出**：22 个 job 缺失、图标别名 205 vs 需要 ~600。

---

## 11. 实施方案（分阶段，每阶段可独立验收）

### 阶段 1：数据层（不动玩法）

- 新增导出器 `tools/export_jobs.py`：
  - 从 `Skill.wz` 导出**全 28 job** 的技能（name/desc/maxLevel/req/levels/icon），合并 `message_zh_CN.properties` 的 `job.name.*`；
  - 输出 `Assets/Resources/UI/JobSkills.json`（新结构：`jobs: [{job, name, book, skills[]}]`）+ `JobNames.json`；
  - 同时补 `NativeAssetIndex.json` 的 `skillicon/*` 别名。
- 新增 `JobDefs.cs`：`JobId → 名称 / 分支 / 阶 / skillBook / 装备掩码 / 一转目标四维` 常量表。
- 验收：`FullDataChecks` 校验 28 job、每 job 技能数、图标别名非空，技能总数 ≥ 600。

### 阶段 2：规则层（存档 + 成长）

- `PlayerProfileData`：`jobId`、`sp`、`skillLevels`、`ap` 语义修正；旧存档用 `cls` 反向映射迁移。
- `JobSystem.cs`（新）：`CanAdvance1/2/3/4`、`Advance(jobId)`、`ResetStats()`、`ChangeJobById()`；
  逐条对齐 `changeJob`（SP +1/+3、AP +5 二~四转、背包 +4×4、HP/MP 表）与 `resetStats`。
- `LevelSystem`：升级时按 `jobBranch != 0` 累计 SP；战士/海盗技能加成 HP。
- 验收：编辑器自检跑「Lv1 新手 → Lv10 一转 → Lv30 二转 → Lv70 三转 → Lv120 四转」，
  断言每阶段 AP/SP/HP/MP/格子与源端公式一致（写成 `JobChecks.cs`）。

### 阶段 3：技能窗 / 能力值窗

- `SkillBook` 按 `jobId` 取页签：只显示当前职业线已到达的阶段；
- `SkillProgress` 改为读写存档 `skillLevels` + 累计 `sp`；四转技能按 `masterLevel` 限制；
- 新手 3 技能用虚拟 SP 池；
- 能力值窗：`AutomaticAp` 判定改为 `jobId==0 && level<=10`，转职后显示剩余 AP，`BtAuto` 接真实自动分配。
- 验收：`SkillWindowChecks` + 截图（`docs/skill-window-*.png` 系列回归）。

### 阶段 4：转职入口（两条路线，二选一）

**A. 原版路线（推荐，但工作量大）**
- 把五系一转教官所在地 + 二/三/四转教官所在地 + 试炼地图补进 `FieldMaps.json`（需要新的地图导出脚本，参考 `wzmap.py`）；
- `NpcApi` 补齐 `canGetFirstJob / getFirstJobStatRequirement / resetStats / changeJobById / teachSkill(带masterLevel)`；
- 直接把 `scripts-zh-CN/npc/1022000.js` 等原样跑起来。

**B. 简化路线（先跑通闭环）**
- 在大厅/城镇放一个"转职教官"入口，用同一套 `JobSystem` 规则做 1~4 转（等级+能力值+道具条件照抄源端）；
- 不做试炼地图，二转证明改为"等级 30 + 交付 4031013×30"或直接给；
- 后续再补地图换成 A。

### 阶段 5：装备与物品

- `WeaponDefs.CanEquip` 改位掩码（1/2/4/8/16），按 job 分支判定；
- 装备槽模型（至少武器 + 防具 6 槽），`CharacterAttributes.WithEquip` 统一叠加；
- 转职道具/赠礼进背包，`WzNames.Item` 已有名字解析。

---

## 12. 待决策（已据此实现，2026-10-23 补齐）

> 以下四项已在本轮缺口补齐中落地，记录实际选择，便于后续回溯。

1. **职业范围**：冒险家五系（0→1~4 转）+ 骑士团五系（1000→1100~1500 2/3/4 转）均已覆盖；技能树来自 `Skill.wz` + `String.wz`，规则来自 `JobSystem.cs`。
2. **转职入口**：原版"城镇找 NPC"已接通——`NpcApi.changeJobById/resetStats/canGetFirstJob/getFirstJobStatRequirement` 直连 `JobSystem`，源端 1183 个 `.js` 由内嵌 ES5 解释器直接跑。
3. **转职任务还原度**：条件校验（等级/凭证/任务/SP 余量）全做；赠品与凭证消耗在 `JobSystem.Advance` / `ChangeJobById` 内完成。
4. **防具**：已做全套装备槽（帽/衣/裤/鞋/手套/披风/盾/武器），见 §13。

---

## 13. 缺口补齐实现（装备槽 / 技能窗 / 技能名）

### 13.1 防具槽位模型（直读 IMG，不落导出表）
- `EquipSlot`：槽位常量 + `SlotOf(itemId)`（按 `id/10000` 分目录）+ `Worn`（8 个穿戴位）。
- `EquipCatalog`：按 `Character/<目录>/<8位id>.img/info` 直读 `reqJob/reqLevel/reqSTR…/inc*`；
  `CanEquip` 复刻源端 `EquipmentCatalogService.matchesJob` 的 **reqJob 位掩码**（1 战士/2 法师/4 弓/8 飞侠/16 海盗，经 `JobDefs.JobBit(jobId)`）。
- `EquipLoadout.Apply`：基础属性 + 武器（走 `WeaponDefs`）+ 全部防具 `inc*` 叠加，是 `PlayerCtrl.ApplyLoadout` 与 `StatWindow` 共用的唯一集成点。
- `PlayerProfileData` / `Profile`：`EquipOfSlot(slot)` / `SetEquipSlot(slot,id)` 持久化 8 个槽位（源端 `inventoryitems` 负位置，本地 JSON 同构）。进游戏时 `GameSession.ApplyToProfile` 把 DB 的 EQUIPPED 行（衣/裤/鞋/武器由 `CharFactory` 建号写入）回填进 Profile。

### 13.2 UI 接线（GameWindows.cs）
- `EquipWindow`：原来写死 `Coat/01040002` 等 4 件，现改为按 8 个 `EquipSlot.Worn` 读 `Profile.EquipOfSlot`，点击非武器槽可脱下。
- `InvenWindow` 装备页：武器走 `Profile.EquipWeapon`；其余防具走 `EquipLoadout.Equip`（含 `reqJob`/`reqLevel`/`reqSTR…` 校验与提示），图标按 `EquipCatalog.DirOf` 取 `Character/<目录>/<id>.img/info/icon`。

### 13.3 技能窗：masterLevel 上限 + 新手虚拟 SP
- `SkillBook.MasterCapOf`：复刻 `AssignSPProcessor`——四转技能（`id/10000%10==2`）上限取 `min(maxLevel, masterLevel)`，再被技能册 `SkillBookItems.MasterLevelFor` 抬升。
- `SkillProgress`：`BeginnerSp = clamp(level-1,0,6)`（旧客户端公式，源端新手本无 SP）；`AvailableSp` 新手走虚拟总量减三新手技已点等级，其余直接读存档累计 SP；`Raise` 受 `MasterCapOf` 与 `CanRaise`（req 前置、自动习得技不可手动点）约束。

### 13.4 缺名技能处理
- `SkillBook` 加载时 `noName = string.IsNullOrEmpty(name)`，缺名回退显示 id。
- 12 个在 `String.wz`/`Skill.wz`/`技能书` 均无名字的技能（jobs 222/232/312 四转 + 1 个贵族 1000，属五六转自定义技能）已在 `Assets/Resources/UI/JobTree.json` 写入 **`职业·id` 占位名**（如 `冰雷·2221011`）。
- 占位名可直接在 `JobTree.json` 改为真名，**无需重跑 `export_jobs.py`**；若重跑导出，因 `String.wz` 仍无对应名，会被重置回 id，届时需再次手动替换。


---

## 附录：引用索引

| 主题 | 位置 |
|---|---|
| Job 枚举与判定 | `gms-server/src/main/java/org/gms/client/Job.java` |
| 转职 SP/AP/HP/MP/格子 | `org/gms/client/Character.java:1270-1420` |
| 升级 AP/HP/MP/SP | `org/gms/client/Character.java:6267-6440` |
| 一转重置能力值 | `org/gms/client/Character.java:7986-8046` |
| SP 上限表 | `org/gms/constants/game/GameConstants.java:33-34, 326-332, 452-490` |
| 伤害公式 | `org/gms/client/Character.java:854-900`；`org/gms/client/inventory/WeaponType.java` |
| 一转门槛原语 | `org/gms/scripting/AbstractPlayerInteraction.java:1350-1393` |
| 新手虚拟 SP / 技能加点 | `org/gms/client/processor/stat/AssignSPProcessor.java:71-100` |
| 建号默认值 | `org/gms/client/Character.java:581-602`；`creator/novice/BeginnerCreator.java` |
| 转职脚本 | `gms-server/scripts-zh-CN/npc/{1022000,1032001,1012100,1052001,1090000,1072000,2020008..2020011,2081100..2081500}.js` |
| 服务端配置默认值 | `src/main/resources/db/migration/V1.7.0__create_game_config.sql` |
| 职业中文名 | `src/main/resources/i18n/message_zh_CN.properties`（`job.name.*`） |
