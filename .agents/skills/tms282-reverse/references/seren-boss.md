# TMS282 受選的賽蓮（Chosen Seren）完整逻辑

> 2026-11-05 整理，口径同 `lucid-boss.md`。S=服务端源码、W=wz282、W265=wz265、C=客户端 Data 树。
> 套件内含 RE 注释（research/tms2823/newboss-0930、bossmech3-0930、boss-flow-0930），本文照录其确定结论。
> Seren.java/SerenFight.java 的头注释本身就是高质量 RE 记录（明确区分【client】【data】【推定】【非官方近似】）。
> 2026-11-05 二次回归修正：HP 分 P1/P2 口径、`firstenter_4100021x0` 抢走 XML first-enter 的机制、
> `410002080` 实为 fieldType 0 的 hard 尾图、掉落表与強力結晶。
> 2026-11-05 三次回归（联网核官方机制）：太阳计官方 6 条规则、官方 7 分钟时钟配比、
> 263/264/260 的形态归属、260 跨 boss 共用 id、奈洛塔双 mob。

## 1. 组成
- **服务端**：`world/boss/Seren.java`（车道路由 + 开场 spine + 召唤 + P1 死亡跳图）+
  `SerenFight.java`（日晷时钟 + 太阳计 + 分时身体，`-Dtms282.seren.mechanics.enabled=false` 可整体关闭）+
  `SerenPacket` / `Tms282SerenTranslators`（协议）+ `BossHpConstants.getBalrogDamageSinkId` 的 Seren 段
  （P2 身体→HP 核转发）+ `Tms282CombatHandlers` CP0115 挂太阳计 + `BossHelper:364` 挂死亡回调。
- **客户端数据**：`Etc/BossSeren.img`（**只有 2PhaseBGM**：noon/twilight/midnight/dawn 四轨——时长表不在客户端）+
  `Effect/Direction20.img/bossSeren/{1pahse_spine,2pahse_spine,1pahse_illust,2pahse_illust}`（客户端拼写 pahse）+
  `UI 0x509`（Seren UI，2F92A00：太阳计 + P2 时钟，全局 9EE15D8/508E230）。

## 2. 注册与车道
- `CHOSEN_SEREN_NORMAL/HARD/EXTREME(28,'Chosen Seren',260,39921,OnceInSevendays)`；限时 30min；buff **80002643**
  （与黑魔法师/Kalos/Kaling/Limbo 共用）。
- **车道 = 地图百分位**（Seren.stage：`(fieldId-410002000)%100`，%20==0 才有效）+ **千位 = 难度**
  （`/100`：0=hard、1=normal、2=extreme）：
  - hard：410002000（过场1）→ 410002020（P1 战）→ 410002040（过场2）→ 410002060（P2 战）→ 410002080（尾图）；
  - normal：410002100 → 410002120 → 410002140 → 410002160 → 410002180；
  - extreme：410002200 → 410002220 → 410002240 → 410002260 → 410002280。
- BossConstants：入口 `SEREN_ENTRANCE_MAP=410000670`；`getBossEntryFieldId(282)`：N→410002100、H→410002000、
  E→410002200（=各自过场1，与 Will 同款"先过场后战斗"）。
- **【套件常量 bug】`SEREN_EXTREME_FIGHT_MAP=410002080`**：410002080 的 stage=80，`Seren.mobId` 只认 stage
  20/60 ⇒ 召不出任何东西；W265 更直接——**410002080 的 `info/fieldType=0`、`onFirstUserEnter=first_SerenHard3`**，
  它是 **hard 的尾图**（不是战斗场）。extreme 实际 P2 战场是 **410002260**（fieldType **271**、
  `first_SerenExtreme2pre`）。normal/hard 的 FIGHT_MAP（410002160/410002060）恰好是 P2 战场所以没炸。
  连带后果：`CustomFieldScripts.firstenter_410002080` 也因此绑在 hard 尾图上。
  迁移时 extreme 车道以 `Seren.java` 路由为准，且要补 `firstenter_410002260` 注册。

## 3. 地图绑定（W265，282 manifest 只改 410002080/180/280 三个尾图）
- 过场1 `seren_direction1`、过场2 `seren_direction2`（Bgm00/Silence）。
- P1 战场：fieldType **270**、fieldScript=`seren`、`onFirstUserEnter=first_Seren{Hard,Normal,Extreme}1pre`、
  Bgm53/AwakeningOfOldGod、lvLimit 260。
- P2 战场：fieldType **271**、`onFirstUserEnter=first_Seren{...}2pre`、
  BGM=`BgmMultiTrack/BossSeren_Phase2`（多轨时钟 BGM，轨切随 time-of-day，见 §5）。
- `CustomFieldScripts` 注册了 **`firstenter_410002160/060/080`**（枚举 :68-70，值直接取
  `BossConstants.SEREN_NORMAL/HARD/EXTREME_FIGHT_MAP`），但**脚本体全缺**（`data/scripts` 是空的）。
  要命的是这层注册会**抢走 XML 的 first-enter**：`Field.execFirstUserEnterScript` 在 282 下只要满足
  「`getScriptForField` 非空 + 该图能映射到 boss 入口图」就直接 `return`；而 410002000..280 每 20 一张
  全被 `BossConstants:668` 映射到 `SEREN_ENTRANCE_MAP` ⇒ 注册过的图里
  `onFirstUserEnter=first_Seren*2pre` **永远不会执行**，一律交给不存在的 `firstenter_4100020xx` ⇒ 空场。
  P1 场（410002020/120/220）不在枚举里，走 XML 的 `first_Seren*1pre`——同样缺体。
- **`Seren.spawn` 全源码零调用方**（`Will.spawn` 同款）：注释自认 "XML first-enter 与 legacy custom
  user-enter hook 都可能调它"，实际上只能由脚本体调用，而脚本体不存在 ⇒ 现成套件里塞伦一只都刷不出来。
  迁移必须自接（改 `execFirstUserEnterScript`、或让场类在 `onFieldEnter` 直接调 `Seren.spawn`）。

## 4. 开场与召唤（Seren.java）
- `playIntro`：仅 stage 0 / 40 生效。时长（按 Direction20/bossSeren spine 动画尾帧实测）：
  stage0=**12667ms**（1pahse_spine）、stage40=**9334ms**（2pahse_spine）；`beginBossIntro(duration)` +
  spine `Effect/Direction20.img/bossSeren/<1|2>pahse_spine/skeleton`（arg "animation"、tag serenIntro），
  到时 `warp(field+20)`（每角色回调，不 party warp）。
- `mobId(fieldId)`：base = 0→**8880600**（hard）/1→**8880630**（normal）/2→**8880650**（extreme），
  stage60 时 +2。即 P1=8880600/8880630/8880650，P2 核=8880602/8880632/8880652。
- `spawn`：仅在 stage 20/60 场；`spawnBossMob(id, 0, y)`，y 取**原始 foothold**：P1 地板 y=275、P2 y=305；
  `isBossSpawned` 防重；成功后 stage20→`SerenFight.startPhaseOne`、stage60→`startPhaseTwo`。
- **HP：不是"只有 2.1e9 占位"，要分 P1 / P2 看**：
  - dat282 里 8880600/8880602/8880632/8880652 的 maxHP **全是 2,100,000,000**（0x7D2B7500，明显占位）；
  - 但 `spawn` 走的是 `spawnBossMob` → `BossHpConstants.resolveMaxHp`，**P2 核会被覆盖**：
    `MOB_HP` 与 `BOSS_CONTEXT_HP[CHOSEN_SEREN_*]` 都是 **normal 8880632=1,569,275,434 /
    hard 8880602=3,138,550,868 / extreme 8880652=8,631,014,887**（`BossHpConstants:98-100/186/191/196`）；
  - **P1 本体 8880600/630/650 两边都没有**（`MOB_HP` 里没有、上下文表也没有）⇒ P1 就真的是 **21 亿**，
    这是套件里唯一实打实的"占位血量"。迁移时 P1 必须自配；
  - 结论：官方数值仍不在套件内，但 P2 有可用合成血量（P1 没有）。`Seren.spawn` 既然没人调，
    这些覆盖目前也只是"将来接上就生效"的预备值。

## 5. SerenFight 机制（bossmech3-0930，非官方近似已注明）
- **职责边界**：273 的 boss_seren_* 事件脚本只记死亡，机制是 282 套件从客户端+Mob.wz 重构的。官方规则不在客户端。
- **P1（stage20）**：只有太阳计。玩家受伤（Tms282CombatHandlers CP0115）→ `onUserHit` → +gauge：
  基础 `gauge.hit=40`/次，**正午( Noon )×1.5**；每秒衰减 `gauge.decayPerSec=15`，**黎明( DAWN )×3**；
  满 `gauge.max=1000` → fullAction（默认 **stun 3s**，可配 kill/none），然后清零。
- **P2（stage60）**：太阳计 + 时钟。
  - 时钟四段：NOON/SUNSET/MIDNIGHT/DAWN，套件用**每段度数 {108,72,108,72}**（合计 360，即 30/20/30/20%），
    周期 `cycleMs` 默认 **120s**（下限 36s，`tms282.seren.cycleMs`）⇒ 实际每段 **36s / 24s / 36s / 24s**。
    指针每 cycleMs/360 一度、段尾停针等服务端 LP08A9；time-of-day 切换同时改背景色调 +
    `BossSeren.img/2PhaseBGM` 轨。
  - **⚠️ 这与官方不符（2026-11-05 用攻略/wiki 坐实）**。官方是七分钟一个周期、四段不等长：

    | 形态 | 官方时长 | 官方占比 | 官方度数 | 套件 108/72 体系 |
    |---|---|---|---|---|
    | 正午 noon | 120s | 28.6% | 102.9° | 108° → 36s |
    | 夕阳 sunset | 120s | 28.6% | 102.9° | 72° → 24s |
    | **午夜 midnight** | **60s** | **14.3%** | **51.4°** | 108° → 36s |
    | 黎明 dawn | 120s | 28.6% | 102.9° | 72° → 24s |

    ⇒ 套件的两处都错：**总周期 120s vs 官方 420s**，**午夜被给成最大段之一（36s）而官方最短（60s，
    但占比 14.3%）**。要贴官方应配 `cycleMs=420000` + `{103,103,51,103}`（int 取整后仍合计 360）。
    SerenFight 头注释自认「Segment sizes ... and a 120 s cycle are server choices」，所以这不是实现 bug，
    是**故意留的占位值，该改**。
  - 官方太阳计（诅咒计）规则 vs 套件实现：

    | 规则 | 官方 | 套件 |
    |---|---|---|
    | 计满惩罚 | **眩晕 5s** + 午夜段 −1 步 + 当前段 +1 步 | 眩晕 **3s** + 清零（`fullAction` 可选 stun/kill/none） |
    | 午夜段耗尽 | **全队强制失败**（无视各人剩余死亡数，显示「只要太陽沒有西下，任何人都無法對抗我。」） | **未实现** |
    | 整周期无人计满 | 午夜段 **+1** 步（最多回补） | **未实现** |
    | 午夜段最大被扣 | **35 次** | **未实现** |
    | 午夜形态攻击 | **不增加太阳计** | **未屏蔽**（`onUserHit` 只看 `timeOfDay==NOON` 时 ×1.5，MIDNIGHT 照加） |
    | 残量继承 | P1 的计量表继承进 P2 中心区 | 同一 `SerenFight` 实例跨阶段，gauge 保留 ✓ |

    这 6 条里套件只对上「残量继承」；其余 5 条是**迁移时真正要补的东西**，比 LP08AC..AF 更影响手感。
  - **分时身体**：可见身体按 BODY_OFFSET {1,5,7,10} 从 HP 核派生——normal 核 8880632 →
    正午 **8880633**（MobSkill 263）/黄昏 **8880637**（264）/午夜 **8880639**（**260**）/黎明 **8880642**（无技能）；
    hard 8880602→8880603/07/09/12；extreme 8880652→8880653/57/59/62。
    身体顺序=时刻顺序原为【推定】，**2026-11-05 已由 skill 节点坐实**：8880633 挂 263、8880637 挂 264、
    8880639 挂 260、8880642 无 skill ⇒ 四段形态各自专技，不复用。
  - 身体是 `HpLinkMob=核` 的设计（Mob.wz）：伤害经 `getBalrogDamageSinkId` 转发到核
    （8880603/07/09/12→8880602 等，仅核在场时）；核空 stand/隐藏 HP 条。
  - **Nerotas（奈洛塔）双助手 8880635「奈洛塔現身」/ 8880636「奈洛塔的力量」、火焰栅栏、
    午夜/黎明特殊规则未实现**（SerenFight 头注释明列）。官方侧「Norum/奈洛塔」是低血量出现的
    常驻无敌分身（击退 + 加太阳计），套件只有 mob 模板，没有任何行为。

- **停机**：核死（`Seren.onMobDeath`，P1/P2 均判）→ `stop(true)`：P2 发 `timerPause`、清全员 gauge、
  身体按 ANIMATION_DEATH 移除；P1 核死另有 6s 后全员 `warp(field+20)`（对应 273 boss_seren_* 的 6 秒）。
  场空 60s → `stop(false)` 弃局。

## 6. mob 模板
- P1 本体：8880630（normal，lv270）/ 8880600（hard，lv275）/ 8880650（extreme，lv275）；
  P2 核：8880632（lv270）/8880602（lv275）/8880652（lv280），hideHP=1 无技能；
  P2 身体：8880633 / 8880637 / 8880639 / 8880642（normal 四形态）。
  全系 boss=1、firstAttack=1、PDRate/MDRate 380、pushed 10亿。
- **三种 282 新技能的分工（2026-11-05 由 wz282 的 `skill` 节点坐实）**：

  | 身体 | skill | level | 补充字段 |
  |---|---|---|---|
  | P1 本体 8880630 / 8880600 / 8880650 | **264** | 1 / 1 / 3 | 难度只差 level |
  | 正午 8880633（normal）/ 8880603（hard） | **263** | 1 | — |
  | 夕阳 8880637（normal） | **264** | 2 | `skillForbidExpireTime 3000` |
  | **午夜** 8880639（N）/ 8880609（H）/ 8880659（E） | **260** | 6 / 5 / 7 | `effectAfter 480`、`skillForbidExpireTime 6000` |
  | 黎明 8880642 | **无** | — | 黎明形态不出招，只有场地 + 盾 |

  （8880607/8880657 是 hard/extreme 的夕阳身体，未逐个核，按同构推定为 264-2/264-4。）

  ⇒ 结论修正：**263 是「正午专属」、264 是「P1 主技 + 夕阳复用」、260 是「午夜专属」**，
  不是原先记的「264=P1、263=P2」那么简单——263 与 260 才是四个形态里唯一互斥的两段。
- **260 是跨 boss 共用 id**：`dat282/mobSkills/` 有 `260-1..7`，Darknell 占 1..4（召唤两把剑）、
  Seren 占 5..7（午夜形态）；`dat265` 另有 `260-1..8`（265 时代已被别处用过）。
  **同一个 skillId 靠 level 区分宿主**，`MobSkill` 里写 `case 260` 时必须先看 `mob.getTemplateId()`
  才能分流，否则会把 Darknell 的剑召唤到塞伦身上。
- **260 / 263 / 264 全部不在 `MobSkillID` 枚举内**（枚举最大只到 252），`MobSkill.applyEffectToMob`
  没有对应分支 ⇒ 服务端只会广播动作、不结算任何效果，只能靠 dat 的数值做表现。
- **它们在 wz265 层全部不存在**：8880630/600/650/632/602/652/633/637/639/642 的 `wz265` XML
  **完全没有 `skill` 节点**，263/264/260 只出现在 `wz282` overlay ⇒ 这是「282 给塞伦加技」的直接证据。
- 配角：8880635「奈洛塔現身」/ 8880636「奈洛塔的力量」、8880631/634/638/640/643 等 dat282 均有。


## 7. 协议（SerenPacket / Tms282SerenTranslators）
- 场类：fieldType 270→2F92AB0、271→2F94A80（服务端只在 Seren 场发送）。
- 包：`SEREN_STUN_GAUGE→LP08AA`（太阳计，`i32 max, i32 value`）、`SEREN_TIMER`（TIMER_START/REPLAN 体 20B、
  TIMER_PAUSE 1B、TIMER_HAND 4B——282 逐一重排，见 Tms282SerenTranslators）、`SEREN_TIME_OF_DAY→LP08A9`
  （1 = noon..4 = dawn，同时驱动背景 tint 3DC5940 与 BGM 轨）、LP08AC/08AD（264 区域，未实现）、
  LP08AE（263 物件，未实现）、LP08AF（**语义未定**，连客户端处理位置都没定位到）。
  配合 §6 的形态归属，这四个包现在能对上人：**AC/AD = 夕阳形态（264）的扇形/区域攻击、
  AE = 正午形态（263）的召唤物、AF = 未知**。注意 P1 主技也是 264，但 P1 场只发太阳计
  （`SerenFight.sync` 里 `stunGauge` 无条件发、`timerStart`/`timeOfDay` 只在 `phaseTwo` 发），
  所以 P1 的 264 走 mob 自身动画，不走 LP08AC/08AD。

## 8. 击杀与奖励
- P2 核死（8880632/8880602/8880652）：`addPartyBoss`→SEREN_NORMAL/HARD/EXTREME（BossHelper:1060-1062）
  + 通用通关 `completeCompatibleBoss`（QR 34360 `boss=1`、清场、掉落窗、退出）。
- P1 核死只跳图不计数（`SerenPhaseCleared` 防重）。
- **「P2 无奖励怪」原则**（Seren.java 注释）：P2 保留共享通关/掉落窗，不发明 reward mob（对比 Lucid 的音乐盒）。
- **掉落：有，按 P2 核分档**（此前「`mob_drops.json` 无 88806xx 条目」是只查了 base 表）：
  `data/resources/mob_drops.json` 13,974 条（265 生成表）里没有塞伦；
  **`data/resources/tms282/mob_drops.json` 4,434 条（282 overlay）里三种核全有**。
  该文件是 `{mobId,...}` 的**列表**，按 `mobId` 过滤：
  - normal 核 8880632 **8 条**、hard 核 8880602 **12 条**、extreme 核 8880652 **12 条**；
  - 共有：超級藥水×50、咒文的痕跡×240、大型BOSS名譽勳章、可疑的附加方塊 10/11、
    微弱靈魂艾爾達氣息、**破曉墜飾 1122443 (chance 250)**、**賽蓮機器人 1662152 (100)**、首領怪幣 ×2/×5/×10；
  - hard/extreme 追加：驚訝的混沌卷軸60%、米特拉的核心寶石 2632972、米特拉的憤怒選擇箱 2633927、
    hard 另给灵魂艾尔达气息 2636955、extreme 给浓厚版 2636956。
- **強力結晶按 P1 本体计**（`GameConstants.intensePowerCrystal`，注意不是 P2 核）：
  8880600 (hard) → 9210050 / 151,250,000 meso；8880630 (normal) → 9210054 / 133,687,500；
  8880650 (extreme) → 9210058 / 847,000,000。
- **本项目已挂的星火/灵魂掉落口径**（2026-11-04 已拍板落地）：项目版塞伦 = **8880342/8880344**（強力/永遠/黑色）。
  ⚠️ **该 id 与 282 威爾 P3 本体撞号**（8880342=Will normal P3、8880344=Will normal P1 上半身）——
  本项目当时把 8880342 当「塞伦」自造的模板 id，与 282 官方语义冲突。迁移真塞伦时必须处理这对撞号
  （项目 mob 表是自建命名空间，暂不致命，但文档口径要改：星火挂的是「项目自造塞伦模板」，不是 282 的 8880342）。

## 9. 客户端资源（C）
- `Etc/BossSeren.img`（仅 2PhaseBGM 四轨）、`Effect/Direction20.img/bossSeren/{1,2}pahse_spine + illust`。
- mob 像素：`Mob/_Canvas/8880600..8880615.img` **全部 16 个都在** ✓——注意是**客户端 C** 的
  `Data/Mob/_Canvas/`（8880600.img 10.9 MB）；服务端 `wz265/Mob.wz/` 根本没有 `_Canvas` 目录。
- UI：Seren UI 0x509（太阳计/时钟）——UIWindow 系内具体 img 未逐张定位（迁移 UI 时再挖）。
- BGM：`Sound/Bgm53.img/AwakeningOfOldGod`、`BgmMultiTrack/BossSeren_Phase2`、入口图 Bgm00/Silence。

## 10. 迁移/验证边界（对本项目）
- 套件可整段照抄：车道路由（含 410002080 常量 bug 的修正）、开场时长 12667/9334、召唤 y=275/305、
  太阳计+时钟全套参数（都是 config 可调）、HP 链接（HpLinkMob→核）、死亡停机/跳图、P2 核血量与掉落表。
- **本轮已坐实（2026-11-05 三次回归，原先标"未坐实"的项）**：
  - 太阳计官方规则（§5 对照表 6 条，套件只对上「残量继承」）；
  - 时钟官方配比 **420s / 103-103-51-103 度**（套件 120s / 108-72-108-72 是自选值）；
  - 四形态专属技 **263=正午 / 264=P1 主技+夕阳 / 260=午夜 / 黎明无技**（wz282 skill 节点坐实）；
  - **260 跨 boss 共用**（Darknell lv1–4 / Seren lv5–7），实现时须按 `templateId` 分流。
- **必拍板**：
  1. HP（**P1 本体 8880600/630/650 就是 dat 的 21 亿占位，必须自配**；P2 核有合成值
     15.7 / 31.4 / 86.3 亿可直接用）；
  2. 太阳计 5 条缺失规则补不补（午夜 −1 / 耗竭全队强制失败 / 整周期回补 +1 / 35 次上限 / 午夜形态不计增）
     ——这五条决定「午夜段是不是一个资源」，不补的话整个 P2 只是换了皮的分时战；
  3. 264 区域 / 263 物件（LP08AC..AF 四个包全未实现，其中 AF 连语义都没定）。

- **必须自己接的一环**：召唤入口。`Seren.spawn` 零调用方 + `firstenter_4100021x0` 脚本体缺失 +
  注册项会压掉 XML first-enter，三者叠起来就是"进场空场"；`firstenter_410002260` 还要新增。
- 与本项目撞号问题（§8）迁移前先对齐命名。
