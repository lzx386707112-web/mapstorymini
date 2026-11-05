# TMS282 格洛姆/戴斯克（Gloom）完整逻辑

> 2026-11-05 整理，口径同 `lucid-boss.md`。S=服务端源码、W=wz282、W265=wz265、C=客户端 Data 树。
> **最重要结论：这套 282 服务端没有 Gloom 的副本状态机/召唤逻辑**——它只有注册项、场地常量、击杀计数和一个
> 死包名；召唤链靠外部脚本（套件不含），与 Lucid normal/hard 车道被 CustomFieldScripts 接管前是同一种"断链"。

## 1. 组成
- **服务端**：无 `Gloom.java`。相关仅四处：`BossPartyType.GLOOM_NORMAL/CHAOS(26)`、
  `BossConstants.GLOOM_ENTRACE_MAP=450009301` + 战斗图/周时（`getBossFightingTime` 归入 30min 组）、
  `BossHelper.handleMobKilled`（击杀计数 + 通用通关）、`ScriptManagerImpl.resolveScriptFile` 的 282 别名
  （`dusk_enter→gloom_accept`）。
- **协议**：`OutHeader.WILL_DUSK_GAUGE`（1483）——名字带 WILL_ 前缀但 "DUSK" 就是 Gloom（内部代号 dusk，
  地图脚本全叫 dusk_*）；**全服务端零发送方 = 死包**（gauge UI 机制未接入）。
- **客户端**：**无专属 Etc img**（没有 BossGloom.img；机制全在 mob 模板 + 地图 fieldType 262 场类）；
  `Effect/Direction20.img` 也没有 bossGloom 演出分支（无开场 cutscene）。

## 2. 注册与入场
- `GLOOM_NORMAL(26,'Gloom',245,Normal,35632,OnceInSevendays)`、`GLOOM_CHAOS(26,245,Chaos,…)`；
  限时 30min；入场 buff **80002645**；入场检查链与 Lucid 同一条通用链。
- 地图（W265，282 manifest 未改 → 265 版即正确内容）：
  - 入口 `450009301`（W282 有改动版：Bgm49/WaveofEmptiness、mapMark moonBridge、lvLimit 245、
    `onUserEnter=enter_450009301`；portal in00 script=**dusk_enter**（282 别名→`gloom_accept` 外部脚本）、
    down00 script=`BPReturn_dusk`（别名→`compatible_boss_exit`））。
  - normal 战斗图 `450009400`（fieldType **262**、`onFirstUserEnter=dusk_onFirstUserEnterpre`、
    `onUserEnter=dusk_timeRecord`、portal pt_BM1_bossOut）；chaos `450009450`（`dusk_timeRecordHard`）。
  - 奖励/出口图 `450009500`（fieldType 202、`onFirstUserEnter=dusk_onFirstUserEnter`、portal out00=`BM1_bossOut`
    ——别名表里 `pt_bm1_bossout/bm1_bossout` 也归 `compatible_boss_exit`）。
  - 故事线/区域图：`450009210..260`（Bgm49/StrangeFog）与 `450009310..360`（Bgm49/WaveofEmptiness）
    两套六张都在，`lvLimit` 统一 245、`onUserEnter=summon_ARCflyingmob`（原文把两套写成"450009310..360
    （210/220/…/260）"，容易误读）。
- `getBossFightingFieldId`：GLOOM_NORMAL→450009400、GLOOM_CHAOS→450009450；282 无专用 EntryField 覆盖
  （`getBossEntryFieldId` 落回 fighting field，**不像 Will/Seren 那样先进过场图**——Gloom 没有过场演出）。

## 3. mob 模板（TMS 繁中名：巨大怪獸戴斯克）
- **8644650 = normal 本体**、**8644655 = chaos 本体**（GameConstants 的 intensePowerCrystal 表同号；
  String/Mob.img 名「巨大怪獸戴斯克」）。lv255、boss=1、firstAttack=1、PDRate/MDRate 300、pushed 10亿、wp 300。
- **HP：不是 1337（2026-11-05 回归修正）**。`GameConstants:1698` 的 `HP:\t1337` 是旧注释；dat282 里
  8644650/8644655 的 maxHP 实际写成 **957,809,713 / 3,064,991,082**，`BossHpConstants` 的 MOB_HP 与
  `GLOOM_NORMAL/CHAOS` 上下文表同值，`Field.spawnMob`（→`resolveBossSpawnHp`）和
  `ScriptManagerImpl.spawnBossMob`（→`resolveMaxHp`）都会按该表落血。**套件里有血可打**，只是这套值是
  "80 难度平滑曲线"生成的合成值、不是官方数值；迁移想用官方血量仍需自配。
- **官方血量（2026-11-05 联网坐实，此前标"官方口径外查"）**：Story **210 亿**、
  **Normal 26,000,000,000,000（26 兆）**、**Chaos 114,600,000,000,000（114.6 兆）**
  （fandom 旧版记 26T / 128T，strategywiki 记 114.6T——Chaos 取 **114.6T** 更可信，128T 是早期估值）。
  ⇒ dat 的 9.58 亿 / 30.65 亿与官方差 **4 个数量级**，套件那套是给"能打过"的量级设计的，别当官方。
- info/skill（两模板同构）：
  - 213-10（action3，`onlyOtherSkill`，afterDelay 7500）——**213 就是 `MobSkillID.AreaWarning`**（枚举内，
    MobSkill:657 有实现，生成 AffectedArea），不是"超出枚举的 282 新 id"；
  - 186-11（action4，onlyOtherSkill）——**186 = `MobSkillID.AreaForce`**（枚举内，同一段 AA 实现）；
  - **252-1 = MobSkillID.BlackMageGloom**（action4，onlyOtherSkill）——名字是 BlackMage 前缀但 Gloom 本体在用
    （与黑魔法师共享技能 id）；枚举里有 252，但 `applyEffectToMob` 没有对应 case（走 default 打印 unhandled）。
  - `onlyOtherSkill=1` ⇒ 三个技能都**不由 mob FSM 自发**，全部由服务端/场类逻辑触发——而 282 场类逻辑缺失。
    技能实现本身存在（213/186），缺的是触发者；252 则连实现都没有。
- 配角命名（String/Mob.img 实测）：8880450=**真‧希拉**、8880453=**死靈史烏**、8880454=**死靈戴米安**、
  8880460=死靈史烏——**不是 Gloom 的 mob**，别混。Gloom 侧 8644651=**戴斯克的靈魂**、
  8644658/8644659/8644660=**巨大怪獸戴斯克**（同名的阶段/召唤变体，具体触发待核）。
- 技能 dat：213-10 / 186-11 / 252-1 两层全有（含 247-1 等）。

## 4. 击杀与奖励
- `handleMobKilled`：8644650→`addPartyBoss(GLOOM_NORMAL)`、8644655→`addPartyBoss(GLOOM_CHAOS)`（BossHelper:1047）
  + 在通用通关白名单（:395）→ `completeCompatibleBoss`：QR 34360 `boss=1`、清场、通关演出、
  `BossRunService.onBossCleared`、掉落窗、`scheduleCompatibleBossExit(-1)` 排程退出。
- **掉落：有（2026-11-05 回归修正）**——此前写"无条目"是只查了 base 表。掉落有**两个文件**：
  `data/resources/mob_drops.json`（13,974 条，265 生成表，**8644650/8644655 没有**）vs
  `data/resources/tms282/mob_drops.json`（4,434 条，282 overlay，**Gloom 全在这里**）；
  两者都是 `[{mobId,...}]` **列表**，不是以 mob 号为 key 的字典：
  - 8644650（normal）**9 条**：超級藥水×40、咒文的痕跡×160、大型BOSS名譽勳章、可疑的附加方塊×6、
    藍/紅星星藥水、高級潛在卷、**星耀耳環 1032330 (chance 250)**、首領怪幣×2；
  - 8644655（chaos）**15 条**：藥水×50、痕跡×240、方塊×10，追加驚訝的混沌卷軸60%、太初之精髓、
    微弱靈魂艾爾達氣息×10、**巨大的恐怖 1113306 (40)**、神祕冥界幽靈武器箱 2633914 (100) /
    防具箱 2633915 (200)、首領怪幣×10；
  - 召唤物 8644614/615/616/617/618/619 有零星 1-3 条（生成的"仿同类 265 mob"转抄条目）。
- Gloom 无专属后续任务写入（对比 Lucid 的 100089/16995）。

## 5. 客户端资源（C）
- mob 像素：`Mob/_Canvas/8644652.img / 8644653.img / 8644654.img / 8644658.img` 存在，**本体 8644650/8644655
  在 `_Canvas` 下没有条目**；2026-11-05 已用 `MsUnpack` 确认 `Packs/Mob_00000.ms` 里有完整 `Mob/8644650.img`
  （13.8KB；8644655 的 `info/link=8644650`，共用外观），**不是缺资源**，迁移时从 pack 解包。
- 地图：C `Data/Map/Map/Map4/450009301/400/430/450/480/500.img` 等 23 张全在（含 450009210..260、
  450009310..360 两组故事图）。
- BGM：入口 WaveofEmptiness、战斗 FerociousBattlefield（450009400/450 同）。
- UI：WILL_DUSK_GAUGE 对应的 gauge 图未定位（无 BossGloom.img、UIWindow 系未见 dusk 节点，待查 UI 类 img）。

## 6. 迁移/验证边界（对本项目）
- **迁移 = 自建召唤与阶段**：`firstenter_450009400/450`（CustomFieldScripts 已注册脚本名，但套件无脚本体）
  + 场类逻辑（fieldType 262 的客户端表现按场内对象驱动）。套件内可照抄的只有：击杀计数、通用通关、
  30min 限时、入场检查链。
- **必拍板**：HP 数值（**官方已有：26T / 114.6T**，套件 dat 是 9.58 亿/30.65 亿的合成值，二选一）、
  mob 渲染层（本体无 `_Canvas`，pack 内可解）、dusk gauge 是否复刻（套件死包，
  客户端 gauge 资源未定位——建议先不做 gauge，只做召唤+战斗+通关）。
- onlyOtherSkill 三技能的触发时机官方脚本已失传，需按客户端 fieldType 262 场类/攻击表现反推或拍板简化。
  官方招式可作反推锚点（2026-11-05 联网补充）：**触手盾**（遮眼期只吃 10% 伤害，遮眼时另一触手 65%/99% 突袭）、
  **主触手 X 形夹击**（每 55s，115%/300%，可站触手当平台打眼）、**垂直激光**（主触手后约 20s，
  300% 首击 + 每 0.5s 300% 持续 5s，会把人吸向中心）、**雾暴**（地面小龙卷 35%/55%）、
  **恐惧计量表**（各人独立，满则黑屏幻觉，掉光才恢复；死者留灰雾给队友加计）、
  **思想碎片陨石**（30%，红=致盲/紫=眩晕/品红=僵尸化）、**巨大幻影**（Normal 100 亿 / Chaos 300 亿）。
  ⇒ 套件里 213(AreaWarning)/186(AreaForce) 正好能承载「陨石」「触手」「龙卷」三类地面预警，
  252(BlackMageGloom) 对不上任何官方招式，更像是黑魔法师那边漏过来的共享 id。
