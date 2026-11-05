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
  - 故事线图 450009310..360（210/220/230/240/250/260）。
- `getBossFightingFieldId`：GLOOM_NORMAL→450009400、GLOOM_CHAOS→450009450；282 无专用 EntryField 覆盖
  （`getBossEntryFieldId` 落回 fighting field，**不像 Will/Seren 那样先进过场图**——Gloom 没有过场演出）。

## 3. mob 模板（TMS 繁中名：巨大怪獸戴斯克）
- **8644650 = normal 本体**、**8644655 = chaos 本体**（GameConstants 的 intensePowerCrystal 表同号；
  String/Mob.img 名「巨大怪獸戴斯克」）。lv255、boss=1、firstAttack=1、PDRate/MDRate 300、pushed 10亿、wp 300。
- **HP：dat maxHP=1337 占位 + 无服务端覆盖 = 套件实际不可用**。迁移必须自配（官方数值不在套件内）。
- info/skill（两模板同构）：
  - 213-10（action3，`onlyOtherSkill`，afterDelay 7500）——282 新技能 id（MobSkillID 枚举只到 252，213 走 dat）；
  - 186-11（action4，onlyOtherSkill）；
  - **252-1 = MobSkillID.BlackMageGloom**（action4，onlyOtherSkill）——名字是 BlackMage 前缀但 Gloom 本体在用
    （与黑魔法师共享技能 id）。
  - `onlyOtherSkill=1` ⇒ 三个技能都**不由 mob FSM 自发**，全部由服务端/场类逻辑触发——而 282 场类逻辑缺失。
- 配角（dat282 有、wz282 XML 已解出）：8880450/8880453/8880454 是**真希拉系**（名字证实）、8880460 死靈史烏
  ——**不是 Gloom 的 mob**，别混。Gloom 的 companion（如 8644651/8644658/59/60 等 dat282 条目）语义待核。
- 技能 dat：213-10 / 186-11 / 252-1 两层全有（含 247-1 等）。

## 4. 击杀与奖励
- `handleMobKilled`：8644650→`addPartyBoss(GLOOM_NORMAL)`、8644655→`addPartyBoss(GLOOM_CHAOS)`（BossHelper:1047）
  + 在通用通关白名单（:395）→ `completeCompatibleBoss`：QR 34360 `boss=1`、清场、通关演出、
  `BossRunService.onBossCleared`、掉落窗、`scheduleCompatibleBossExit(-1)` 排程退出。
- 掉落：`mob_drops.json` 无 8644650/8644655 条目。
- Gloom 无专属后续任务写入（对比 Lucid 的 100089/16995）。

## 5. 客户端资源（C）
- mob 像素：`Mob/_Canvas/8644652.img / 8644653.img / 8644654.img / 8644658.img` 存在，**本体 8644650/8644655
  无 _Canvas**——同 Will normal 车道的情况，迁移前先 `MsUnpack` 解 `Mob/8644650.img` 看内联帧/outlink 再定论。
- 地图：C `Data/Map/Map/Map4/450009301/400/430/450/480/500.img` 等 23 张全在（含 210..360 故事图）。
- BGM：入口 WaveofEmptiness、战斗 FerociousBattlefield（450009400/450 同）。
- UI：WILL_DUSK_GAUGE 对应的 gauge 图未定位（无 BossGloom.img、UIWindow 系未见 dusk 节点，待查 UI 类 img）。

## 6. 迁移/验证边界（对本项目）
- **迁移 = 自建召唤与阶段**：`firstenter_450009400/450`（CustomFieldScripts 已注册脚本名，但套件无脚本体）
  + 场类逻辑（fieldType 262 的客户端表现按场内对象驱动）。套件内可照抄的只有：击杀计数、通用通关、
  30min 限时、入场检查链。
- **必拍板**：HP 数值（官方口径外查）、mob 渲染层（本体无 _Canvas）、dusk gauge 是否复刻（套件死包，
  客户端 gauge 资源未定位——建议先不做 gauge，只做召唤+战斗+通关）。
- onlyOtherSkill 三技能的触发时机官方脚本已失传，需按客户端 fieldType 262 场类/攻击表现反推或拍板简化。
