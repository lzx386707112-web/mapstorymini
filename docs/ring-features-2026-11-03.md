# 戒指功能盘点（2026-11-03）

数据源：`MapleDashResources/Data/Character/Ring/*.img`（1116 枚）、`Effect/CharacterEff.img`、`String/Eqp.img/Eqp/Ring`。
代码侧：`EquipLoadout.cs`、`NativeEquipmentEffects.cs`、`NativeRingEffect.cs`、`NativeNameTag.cs`、`NativeChatBalloon.cs`、`Profile.cs`。

## 一、字段全景

`info` 下和"功能"有关的字段，按枚数排：

| 字段 | 枚数 | 作用 | 状态 |
|---|---|---|---|
| `inc*`（STR/DEX/INT/LUK/HP/MP/PDD/MDD/ACC/EVA/PAD/MAD/Speed/Jump） | 577 组 | 基础属性 | ✅ 已有（`EquipCatalog.Apply`） |
| `tuc` | 445 | 可砸卷次数 | ✅ 已有（`NativeScrollService`） |
| `chatBalloon` | 183 | 头顶气泡样式 | ✅ 上一轮（`NativeChatBalloon`） |
| `nameTag` | 167 | 名牌样式 | ✅ 上一轮（`NativeNameTag`） |
| `level` | 150 | 可升级戒指（`level/info/<n>/exp`） | ❌ 未做（需服务端持久化戒指经验） |
| `effect` | 113 | 身上特效 | ✅ **本轮新增**（`NativeRingEffect`） |
| `setItemID` | 72 | 套装 | ✅ 已有（`AdminSetItem`） |
| `timeLimited` | 61 | 时限（实例 `expiration` 过期清理） | ✅ 已有（`NativeEquipmentEffects`） |
| `option` + `fixedPotential` | 40 | 固定潜能 | ❌ 数据缺 `ItemOption.img` |
| `addition` | 37 | 附加属性 / 暴击 / BOSS 伤害 / 技能 | ✅ **本轮新增**（`statinc`+`critical`+`boss`） |
| `ringOptionSkill` + `ringOptionSkillLv` | 36 | 戒指赋予技能（60013-60018） | ❌ 数据缺（`Skill.wz` 无 60000.img） |
| `charmEXP` / `charismaEXP` / `willEXP` | 24 / 22 / 18 | 倾向经验 | ❌ 无倾向系统 |
| `incPVPDamage` | 12 | PVP 伤害 | ❌ 无 PVP |
| `reqPOP` | 8 | 人气门槛 | ❌ 无人气值 |
| `bonusExp` | 6 | 经验加成 | ✅ **本轮新增** |
| `attackSpeed` | 5 | 攻速 | 值全为 0，无实际效果 |
| `groupEffectID` | 4 | 情侣/好友同戴特效 | ❌ 需两名真人同时佩戴 |
| `DAexceedLimit` | 4 | 恶魔复仇者 HP 上限 | ❌ 无该职业 |
| `isDonor` / `sample` / `scope` / `trade*` / `only*` | — | 元数据、交易/强化限制 | 无运行时功能 |

## 二、本轮补的三块

### 1. `info/addition`（37 枚）

源端把戒指的"附加效果"放在 `addition` 下，和 `inc*` 是两套字段，互不覆盖。四类：

- `statinc`：额外属性（incSTR/incDEX/incINT/incLUK/incMHP/incMMP/incPDD/incMDD/incACC/incEVA/incPAD/incMAD/incSpeed/incJump）
- `critical`：`prob` = 暴击率 +%，`damage` = 暴击伤害 +%
- `boss`：`prob` = 触发概率（100 = 常驻），`damage` = BOSS 伤害 +%
- `skill`：赋技能。本数据集只引用 90000000 / 90001001，而 `Skill/900.img/skill` 只有 9001000/9001001/9001002/9001006 → **数据缺，跳过**

每段可带 `con` 条件，三种键可同时出现：

| 条件 | 数据形态 | 判据 |
|---|---|---|
| `lv` / `level` | int | 角色等级 ≥ 值 |
| `job` | 具体 jobId 列表（米哈尔 5111/5112、英雄 2111/2112…） | **精确匹配**，不是分支位掩码 |
| `weekDay` | 字符串列表（`saturday` / `sunday`） | 当天星期匹配 |

实现：`EquipCatalog.ApplyAddition(itemId, result, ref bossDamage)`；`boss` 伤害并进 `setItemBonus["BossDamage"]`（复制 `ComputeBonus` 的返回值再改，避免污染缓存），由 `AdminSetItem.ApplyDamage` 消费。

典型样本：

- `1112427 冒险家的残酷之戒`：Lv.30 起暴击率 +5%
- `1113068 探险之暴击戒指`：Lv.70 起暴击率 +12%、暴击伤害 +1%
- `1113025 周末特殊戒指<第4阶段>`：仅周六/日生效，+5% BOSS 伤害 + 四维/攻击
- `1112723 米哈尔 戒指`：仅 5111/5112 生效

### 2. `info/effect`（113 枚，94 枚有素材）

同一个字段两种模式，靠字段有无区分：

| 模式 | 判据字段 | 行为 | 样本 |
|---|---|---|---|
| 固定循环 | `fixed` / `animate` / `pos` | 挂在角色身上的循环帧动画，`pos=1` 抬到头顶 | 灵魂戒指 1118000-、寂寞单身戒指 1112916 |
| 粒子发射 | `follow` + `left/top/right/bottom` + `dx/dy` + `interval` + `delay` | 按 `interval` 在角色周围区域撒点，`delay` 是单颗粒子寿命 | 音符环绕 1112900、闪星环绕 1112901、极光 1112404 |

- 区域是 WZ 屏幕坐标（y 向下为正），世界坐标要取反。
- `genOnMove` 的（极光等 10 枚）只在角色移动时发射。
- 素材直读 `Effect/CharacterEff.img/<path 末段>`，走 `NativeImgAnimation`（保留 origin / 每帧 delay / alpha）。
- 排序：`z=-1` → `SortingOrder-1`（角色身后）。
- 19 枚（1112500/1112907/1112915/1112924-）指向的节点源端也没有 → 静默跳过。

### 3. `info/bonusExp`（6 枚）

`bonusExp/<段>/incExpR` 是百分比。多段（`termStart` 0/1/2）是新人/回归礼包按周递增；本工程没有账号龄基准，取第 0 段（最保守）。多枚戒指同戴取最大值。

实现：`NativeEquipmentEffects.RingExpPercent()` / `RingExpBonus(gain)`，接在 `Profile.GrantExperience` 的吊坠加成之后（先除后乘，long 不溢出）。

## 三、未实现的四处及原因

| 项 | 原因 | 能否补 |
|---|---|---|
| 潜能 `option`/`fixedPotential`（40 枚） | 项目数据里没有 `ItemOption.img`（GMS 里在 `Item.wz` 根），`option` 只存 code+level，没有属性值可算 | 补数据文件后可做 |
| 可升级戒指 `level`（150 枚） | `level/info/<n>/exp` 是戒指自身经验表，需要服务端在 `inventoryitems` 上持久化「戒指经验」并接打怪结算 | 工程量大 |
| 戒指技能 `ringOptionSkill`（36 枚） | `Skill.wz` 里没有 60000.img，60013-60018 无法解析；`addition.skill` 的 90000000/90001001 同样缺 | 补数据后可做 |
| 倾向经验 / PVP / 人气 / 恶魔复仇者 / 情侣特效 | 对应系统整体不存在（无倾向、无 PVP、无人气值、无 3100 职业、无第二个真人） | 需先有系统 |

## 四、验证

- `tools/unity-compile-check`：0 error / 0 warning
- `tools/server-check`：1412 通过 / 0 失败
- `MenuFeedbackChecks` 新增 6 组断言：`con.lv` 门槛（29 不生效 / 30 生效）、暴击率与暴伤、周末戒指按星期生效、`con/job` 精确匹配、`bonusExp` 取值、`effect/path` 解析
- 真机排查：`adb shell setprop debug_ringfx 1` 前先 `PlayerPrefs` 写入 `debug_ringfx=1`（一次有效），日志会打
  `[Ring/Fx] ring=<id> path=<路径> node=<true|false> groups=<n> mode=<fixed|emitter|none>`
