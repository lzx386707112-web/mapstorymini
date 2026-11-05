# TMS282 受選的賽蓮（Chosen Seren）完整逻辑

> 2026-11-05 整理，口径同 `lucid-boss.md`。S=服务端源码、W=wz282、W265=wz265、C=客户端 Data 树。
> 套件内含 RE 注释（research/tms2823/newboss-0930、bossmech3-0930、boss-flow-0930），本文照录其确定结论。
> Seren.java/SerenFight.java 的头注释本身就是高质量 RE 记录（明确区分【client】【data】【推定】【非官方近似】）。

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
  20/60 ⇒ 410002080 召不出任何东西。extreme 实际 P2 战场是 **410002260**（W265 XML 证实
  `first_SerenExtreme2pre`）。normal/hard 的 FIGHT_MAP（410002160/410002060）恰好是 P2 战场所以没炸。
  迁移时 extreme 车道以 Seren.java 路由为准。

## 3. 地图绑定（W265，282 manifest 只改 410002080/180/280 三个尾图）
- 过场1 `seren_direction1`、过场2 `seren_direction2`（Bgm00/Silence）。
- P1 战场：fieldType **270**、fieldScript=`seren`、`onFirstUserEnter=first_Seren{Hard,Normal,Extreme}1pre`、
  Bgm53/AwakeningOfOldGod、lvLimit 260。
- P2 战场：fieldType **271**、`onFirstUserEnter=first_Seren{...}2pre`、
  BGM=`BgmMultiTrack/BossSeren_Phase2`（多轨时钟 BGM，轨切随 time-of-day，见 §5）。
- CustomFieldScripts 另注册 `firstenter_410002160/060/080`（套件脚本体缺失；Seren.spawn 注释自认
  "XML first-enter 与 legacy custom user-enter hook 都可能调它"——**实际全源码无 `Seren.spawn` 调用方**，
  召唤同样靠缺失的外部脚本，282 迁移须自接）。

## 4. 开场与召唤（Seren.java）
- `playIntro`：仅 stage 0 / 40 生效。时长（按 Direction20/bossSeren spine 动画尾帧实测）：
  stage0=**12667ms**（1pahse_spine）、stage40=**9334ms**（2pahse_spine）；`beginBossIntro(duration)` +
  spine `Effect/Direction20.img/bossSeren/<1|2>pahse_spine/skeleton`（arg "animation"、tag serenIntro），
  到时 `warp(field+20)`（每角色回调，不 party warp）。
- `mobId(fieldId)`：base = 0→**8880600**（hard）/1→**8880630**（normal）/2→**8880650**（extreme），
  stage60 时 +2。即 P1=8880600/8880630/8880650，P2 核=8880602/8880632/8880652。
- `spawn`：仅在 stage 20/60 场；`spawnBossMob(id, 0, y)`，y 取**原始 foothold**：P1 地板 y=275、P2 y=305；
  `isBossSpawned` 防重；成功后 stage20→`SerenFight.startPhaseOne`、stage60→`startPhaseTwo`。
- **HP：dat maxHP=2.1e9（占位值）且 spawn 不覆盖 HP** ⇒ 套件里 Seren 血量就是 21 亿（与官方相差数量级）。
  迁移必须自配（同 Will/Gloom/Darknell，官方数值不在套件内）。

## 5. SerenFight 机制（bossmech3-0930，非官方近似已注明）
- **职责边界**：273 的 boss_seren_* 事件脚本只记死亡，机制是 282 套件从客户端+Mob.wz 重构的。官方规则不在客户端。
- **P1（stage20）**：只有太阳计。玩家受伤（Tms282CombatHandlers CP0115）→ `onUserHit` → +gauge：
  基础 `gauge.hit=40`/次，**正午( Noon )×1.5**；每秒衰减 `gauge.decayPerSec=15`，**黎明( DAWN )×3**；
  满 `gauge.max=1000` → fullAction（默认 **stun 3s**，可配 kill/none），然后清零。
- **P2（stage60）**：太阳计 + 时钟。
  - 时钟四段：NOON/SUNSET/MIDNIGHT/DAWN，**每段度数 {108,72,108,72}**（合计 360），周期 `cycleMs` 默认
    **120s**（下限 36s，`tms282.seren.cycleMs`）；指针每 cycleMs/360 一度、段尾停针等服务端 LP08A9；
    time-of-day 切换同时改背景色调 + `BossSeren.img/2PhaseBGM` 轨。
  - **分时身体**：可见身体按 BODY_OFFSET {1,5,7,10} 从 HP 核派生——normal 核 8880632 →
    正午 **8880633**（MobSkill 263）/黄昏 **8880637**（264-2）/午夜 **8880639**/黎明 **8880642**；
    hard 8880602→8880603/07/09/12；extreme 8880652→8880653/57/59/62。
    身体顺序=时刻顺序属【推定】（正午 263 是 P2 客户端 0x8AE、黄昏 264 是 P1 的技，支持此序）。
  - 身体是 `HpLinkMob=核` 的设计（Mob.wz）：伤害经 `getBalrogDamageSinkId` 转发到核
    （8880603/07/09/12→8880602 等，仅核在场时）；核空 stand/隐藏 HP 条。
  - **Nerotas 助手 8880635/36、火焰栅栏、午夜/黎明特殊规则未实现**（SerenFight 头注释明列）。
- **停机**：核死（`Seren.onMobDeath`，P1/P2 均判）→ `stop(true)`：P2 发 `timerPause`、清全员 gauge、
  身体按 ANIMATION_DEATH 移除；P1 核死另有 6s 后全员 `warp(field+20)`（对应 273 boss_seren_* 的 6 秒）。
  场空 60s → `stop(false)` 弃局。

## 6. mob 模板
- P1 本体：8880630（normal，lv270，skill 264-1）/ 8880600（hard，lv275，264-1）/ 8880650（extreme，lv275，264-3）；
  P2 核：8880632（lv270）/8880602（lv275）/8880652（lv280），hideHP=1 无技能；
  P2 身体：8880633（skill 263-1）/ 8880637（264-2，skillForbidExpireTime 3000）等。
  全系 boss=1、firstAttack=1、PDRate/MDRate 380、pushed 10亿。
- **264 / 263 是 282 新技能 id**（MobSkillID 枚举无，走 dat；264-1/2/3 两层全有）。
- 配角：8880635/8880636（Nerotas 助手）、8880631/634/638/640/643 等 dat282 均有。

## 7. 协议（SerenPacket / Tms282SerenTranslators）
- 场类：fieldType 270→2F92AB0、271→2F94A80（服务端只在 Seren 场发送）。
- 包：`SEREN_STUN_GAUGE→LP08AA`（太阳计，`i32 max, i32 value`）、`SEREN_TIMER`（TIMER_START/REPLAN 体 20B、
  TIMER_PAUSE 1B、TIMER_HAND 4B——282 逐一重排，见 Tms282SerenTranslators）、`SEREN_TIME_OF_DAY→LP08A9`
  （1 = noon..4 = dawn，同时驱动背景 tint 3DC5940 与 BGM 轨）、LP08AC/08AD（264 区域，未实现）、
  LP08AE（263 物件，未实现）、LP08AF（未实现）。

## 8. 击杀与奖励
- P2 核死（8880632/8880602/8880652）：`addPartyBoss`→SEREN_NORMAL/HARD/EXTREME（BossHelper:1060-1062）
  + 通用通关 `completeCompatibleBoss`（QR 34360 `boss=1`、清场、掉落窗、退出）。
- P1 核死只跳图不计数（`SerenPhaseCleared` 防重）。
- **「P2 无奖励怪」原则**（Seren.java 注释）：P2 保留共享通关/掉落窗，不发明 reward mob（对比 Lucid 的音乐盒）。
- 掉落：`mob_drops.json` 无 88806xx 条目。
- **本项目已挂的星火/灵魂掉落口径**（2026-11-04 已拍板落地）：项目版塞伦 = **8880342/8880344**（強力/永遠/黑色）。
  ⚠️ **该 id 与 282 威爾 P3 本体撞号**（8880342=Will normal P3、8880344=Will normal P1 上半身）——
  本项目当时把 8880342 当「塞伦」自造的模板 id，与 282 官方语义冲突。迁移真塞伦时必须处理这对撞号
  （项目 mob 表是自建命名空间，暂不致命，但文档口径要改：星火挂的是「项目自造塞伦模板」，不是 282 的 8880342）。

## 9. 客户端资源（C）
- `Etc/BossSeren.img`（仅 2PhaseBGM 四轨）、`Effect/Direction20.img/bossSeren/{1,2}pahse_spine + illust`。
- mob 像素：`Mob/_Canvas/8880600..8880615.img` 全在 ✓。
- UI：Seren UI 0x509（太阳计/时钟）——UIWindow 系内具体 img 未逐张定位（迁移 UI 时再挖）。
- BGM：`Sound/Bgm53.img/AwakeningOfOldGod`、`BgmMultiTrack/BossSeren_Phase2`、入口图 Bgm00/Silence。

## 10. 迁移/验证边界（对本项目）
- 套件可整段照抄：车道路由（含 410002080 常量 bug 的修正）、开场时长 12667/9334、召唤 y=275/305、
  太阳计+时钟全套参数（都是 config 可调）、HP 链接（HpLinkMob→核）、死亡停机/跳图。
- **必拍板**：HP（dat 2.1e9 占位）、太阳计规则官方性（套件自认非官方近似）、午夜/黎明特殊规则与
  264 区域/263 物件（LP08AC..AF 未实现——迁移要么不做要么按客户端表现自建）。
- 与本项目撞号问题（§8）迁移前先对齐命名。
