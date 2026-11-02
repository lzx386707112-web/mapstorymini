# TMS282 露希妲（Lucid）完整逻辑

> 2026-11-02 整理。服务端源码 `~/Documents/mxd/TMS282.5/source/src/main/java/net/swordie/ms/`（下称 S）；
> 提取 WZ XML `~/Documents/mxd/TMS282.5/data/wz282/`（下称 W）。282 套件内含大量 RE 注释
> （research/tms2823/lucid-re-0930、bossmech1-0930、lucidfly-0930），本文照录其确定结论。
> 265 = 套件内基线版本（wz265/dat265），282 = TMS_282_3 协议。

## 1. 组成
- **普通 boss 副本**：`world/boss/Lucid.java` 状态机 + `world/boss/BossManager`（脚本分发）+ `BossHelper`（击杀/奖励）。
- **變身燃燒（changeburning）活动**：`world/tms282/contents/changeburning/` 的 `LucidDream`/`LucidAbility`/`DreamTrial`，
  玩家变成露希妲，用 `Tms282LucidFlight`（异想之梦 80004201 飞行）。
- 客户端数据：`W/Etc.wz/BossLucid.img.xml`（1466 行，机制参数总表）。

## 0. 迁移决策（2026-11-02 用户拍板，迁移以此为准）
1. **三难度车道全部启用**：入场（NPC/boss UI）时让玩家选难度，normal 走 450004450 车道、hard 走
   450004750 车道。套件侧 6 处改动清单见 §2「【决策已定】三车道全部启用」。
2. **死亡按当前服务端逻辑**：无死亡次数限制（通用复活），非 bug（机制存在但从未接入，见 §2）。
   可选增强：`setupBossDeathCount(10)` 还原真服每人 10 次。
3. **P2 血量按 282 口径 = P1 同血量**（76,563,532/170,141,183/340,282,366），删除 265 硬编码
   （且套件 265 值 easy/normal 本身少两个零），Fury 壳 340,282,366，走 boss.properties 运行时可调。
   依据与 WZ 官方语义冲突说明见 §3「【决策已定】P2 HP」与「【WZ 官方难度段映射】」。

## 2. 入场与流程（副本）
- 入口地图 450004000「惡夢時間塔」（`BossConstants.LUCID_ENTRACE_MAP`）；限时 30 分钟（`LUCID_TIME`）。
- `BossPartyType.LUCID_EASY/NORMAL/HARD`：orderId 19、等级 220、preQuest 34330、每周一次（OnceInSevendays）。
  战斗图全部先给 450004150（`getBossFightingFieldId`），`getBossEntryFieldId` 282 下也回 450004150。
- 入场检查链（ScriptHandler:370-425，viaBossUI 路径）：全员等级 ≥220 且**全员**完成前置任务 34330 →
  每人 `createQuestWithQRValue(102401, "mapR=…;order=19;diff=…")` 记录 + `setLucidMode(难度)` →
  `checkPartyBossAttempt`（周次数）→ `ensureBossEntryItems`（其他栏空间/入场道具补发）→ **仅队长可操作** →
  `warpInstanceIn` + `setInstanceTime(30min, returnField=450004000)`。超时=通用 instance teardown，
  到期传回 returnField。
- **【决策已定】玩家死亡按当前服务端逻辑 = 无死亡次数限制**（走通用复活，死了可以继续打）。
  现状核实：通用死亡计数机制存在（`Char.deathCount` 默认 **-1=关闭**、`sm.setupBossDeathCount(n)`/
  `setPartyDeathCount(n)` 可给全队上次数、死亡时 Char.java:8210/8301 有复活 UI 与扣减链路），但
  **全服务端无任何调用方**，Lucid 入场也没调——不是 bug，是未接入。迁移先按"无限制"实现；
  若要还原真服「每人 10 次」（StrategyWiki：贯穿全场 10 次）属可选增强：入场时对全队
  `setupBossDeathCount(10)` 即可，机制代码现成。
- **入场 buff**：`giveBossPartyBuff` 给每人上 `BossWaitingLinesBuff`，技能 = `BossConstants:357` 的
  **80002232**（x/time 取技能表值）。
- 难度与地图车道：**WZ 地图数据里有三条车道**（easy 450004150/450/750…、normal 450004450…、hard 450004750…，
  结构一致每 +300/450/750），**但这套 282 服务端现状只跑 easy 车道**——`getBossFightingFieldId`/
  `getBossEntryFieldId`（282）对三难度都回 450004150；`enterFirstPhase282` 只在 `field.getId()==450004150`
  生效（Lucid.java:134）；265 路径 spawn 同样限定 450004150（Lucid.java:45）；P2 spawn 只认 450004250
  （Lucid.java:78）。normal/hard 专用图（450004450~900）服务端 spawn 链路是断的（450004450 的
  `Fenter_450004150pre` 在 BossManager 无 case；450004850 的 `Fenter_450004250` 触发后被 field 限制空转），
  `CustomFieldScripts` 也只注册了 450004150/200/250。难度差异本身由 lucidMode 承载（模板 id、HP、
  蝴蝶目标数、技能概率），启用其他车道只是"把场地指过去"，不涉及新机制。
  - easy 图组：450004150 夢幻森林(P1) → 450004200 惡夢時間塔(过场) → 450004250 坍塌的時間塔(P2) → 450004300(奖励) → 450004600
  - normal 450004450→450004500→450004550→450004600；hard 450004750→450004800→450004850→450004900。
  - `BossLucid.img` 只有 450004000/300/600 完整 img，其余为占位复用。
- **【决策已定，2026-11-02】三车道全部启用，入场时让玩家选难度**。套件侧最小改动清单（迁移照此实现）：
  ①`BossConstants` 282 段 `getBossEntryFieldId/getBossFightingFieldId`：LUCID_NORMAL→450004450、
  LUCID_HARD→450004750（easy 保持 450004150）；②`Lucid.enterFirstPhase282` 的 field 判断
  450004150 → 扩为 {450004150, 450004450, 450004750}（265 路径 Lucid.java:45 同理）；③`spawn(SECOND)`
  的 450004250 → 扩为 {450004250, 450004550, 450004850}；④`CustomFieldScripts` 补注册
  450004450/500/550/750/800/850（脚本名复用 `firstenter_450004150/200/250` 同族逻辑或新增同款 case）；
  ⑤`BossManager.handle` 给 `Fenter_450004150pre/450004250pre` 补 case（或让 normal/hard 图也走
  CustomFieldScripts 接管）；⑥难度开关已现成：`config/boss.properties` 的 `boss.enable.LUCID_NORMAL/HARD`
  （默认 true，`BossRuntimeConfig.isDifficultyEnabled`）+ 后台管理台 BossConfigGroup 有开关 UI。
  入场 UI：客户端 boss UI（orderId 19）自带难度选择，`handleBossUIRequest` 解出 difficulty →
  `getByOrderIdAndDifficulty(19, 0/1/2)` → 各自车道；本项目 Unity 侧在入场 NPC 交互做难度选择弹窗
  （传 difficulty 参数走同一条 CHECK_BOSS_PARTY_BY_SCRIPT/BOSS_UI_REQUEST 链）。
  `getBossUITransferFieldId`（ScriptHandler:310）已按 `getBossEntryFieldId` 动态取图，改①后自动适配。
- 脚本入口（`BossManager.handle`，由 `ScriptManagerImpl:240/251` 调）：
  `Fenter_450004150`→`Lucid.spawn(FIRST)`；`Fenter_450004250`→`spawn(SECOND)`；
  `Fenter_450004300`/`enter_450004300`→`spawn(REWARD)`；`enter_450004200`→`Lucid.cutScene`。
  出口脚本 `out_450004250`/`out_450004300`/`west_450004150` 仅在 check 白名单（真正传送走 Map 传送点/其他逻辑）。
  入场脚本 `firstenter_450004150/200/250`（`CustomFieldScripts`）。
- **282 专属开场**（`Lucid.enterFirstPhase282`，仅 450004150）：每人一次 `beginBossIntro(9167)`（lusi.json 实际 9.1666s）
  + `blind(1,255,0,0,0,0)` + spine 屏 `Map/Effect3.img/BossLucid/Lucid/lusi` + 音效 `Sound/SoundEff.img/ArcaneRiver/lucid_spine`
  （音量 200），9167ms 后解除并 `spawnMob(mobId, 1000, 48)`。mobId 按模式：0→8880155、1→8880151、2→8880153
  （WZ 标注语义见 §3 难度段映射——套件属再诠释，迁移照抄套件）；HP=`BossHpConstants.resolveMaxHp`：
  8880155=76,563,532；8880151=170,141,183；8880153=340,282,366（MOB_HP 与 BOSS_CONTEXT_HP 一致）。
  人走光则 `setBossSpawned(false)` 回收。265 旧路径（非 282）：spine+`phase1` 音效，spawn 8880140+8880166，
  HP 按 lucidMode 固定 600 亿/1200 亿/50.8 万亿。
- 过场（`Lucid.cutScene`，450004200/450004500/450004800）：`beginBossIntro(4000)`+blind，1s 后
  spine `Map/Effect3.img/BossLucid/butterfly2/buterfly`，3.9~4s `warp(field+50)`；282 有每角色
  `LucidCutscenePlayed:<cid>` 防重放。

## 3. P2 / Fury（最后阶段）
- 击杀 P1 body（`BossHelper` :456）：取消蝴蝶 timer、`butterflyAction(Erase)`、清场内其他 mob，
  2s 后 `warp(field+50)`（组队 warpParty）。
- easy(8880155) 击杀：QR 34360 `boss=1`。
- normal(8880151)：start+complete 100089，QR 34360。
- hard(8880153)：16995、36117、100102 完成任务 + QR 35650 `clear=7` + QR 34360。
- P2 body 8880150 击杀：**hard 模式** → `instance.setTimeout(45)`，spawn 8880153 @ (600,-490)，
  `setLastPhaseTypeForHardMode(1)`（Fury 背景型；282 客户端 0 是 no-op，265 发 0）；否则清场 warp+50。
  Fury 壳 HP：套件硬编码 11 万亿（265 官方 P3≈12万亿/10命），**按 282 口径改为 340,282,366**
  （=BossHpConstants 8880153，boss.properties 可调）；失败后 `bossFuryFailResummon=8880151`
  以 16% HP 在 (711,-786) 复活重打（P2=P1 后即 170,141,183×16%）。
- **【决策已定】P2 HP 按 282 口径 = P1 同血量**：76,563,532 / 170,141,183 / 340,282,366（E/N/H），
  **删除 Lucid.java:80-89 的 265 硬编码**，改走 `BossRuntimeConfig.getContextHp`（boss.properties
  `boss.hp.LUCID_*` 可运行时改）。依据：①265 官方设计 P1=P2 同血量（StrategyWiki：easy 6万亿=6万亿、
  normal 12万亿=12万亿、hard P2 54万亿≈P1 50.8万亿，且套件 hard 硬编码 50.8万亿与官方精确一致——
  注意套件 easy/normal 的 600亿/1200亿是官方 6万亿/12万亿**少打两个零的笔误**）；②282 数据无独立 P2 数值
  （WZ maxHP=`??????` 隐藏，BossHpConstants 282 段无 8880150）。迁移时 P2=P1，Fury 复生=P2×16%
  （bossFuryResummonHPR）。normal 车道 spawn 位仍 (532,-490)。
- **【WZ 官方难度段映射，2026-11-02 第四轮发现，颠覆旧口径】** `BossLucid.img/Lucid` 各段
  （`info` 字段）：段0=normal（P1 8880140+8880166 / P2 8880150）、段1=hard（P1 8880141+8880166 /
  P2 8880151 + Fury 8880152→8880153→fail 8880154→resummon 8880151）、段2=**story**（P1 8880190+8880196 /
  P2 8880191）、段3=**easy**（P1 8880142+8880158、hurdle 8880187 / P2 **8880155**、hurdle 8880189）、
  段4=變身燃燒（8950123/8950124...）。⇒ **282 官方语义：8880155=easy P2、8880151=hard P2、
  8880153=Fury 复生壳**；BossHpConstants 三个 282 数值分别绑这三个角色。套件把 8880155/151/153 当
  E/N/H **P1** spawn 是作者的再诠释（数值递增体系自洽，与 WZ 语义冲突但可运行）。迁移二选一：
  a) 照抄套件（P1 三模板=8880155/151/153，P2=8880150，P2 HP=P1）；b) 按 WZ 官方语义重排
  （P1=8880142/8880140/8880141，P2=8880155/8880150/8880151）。**默认按 a（照抄套件）+ P2=P1 血量**；
  渲染上 8880140/141/142 系列与 8880155 都有 `_Canvas` spine，两条路视觉都成立。
- **onFirstUserEnter 绑定**（wz265 Map4 实测，BossManager 的 Fenter_* 是这条链）：easy P1 `Fenter_450004150pre`、
  normal P1 `Fenter_450004150pre`、hard P1 `Fenter_450004150`；easy/normal P2 `Fenter_450004250pre`、
  hard P2 `Fenter_450004250`；奖励 300/600/900 `Fenter_450004300`；450003840/920/960（故事/试炼图）绑
  `Fenter_450004150pre`/`Fenter_450004250pre`/`Fenter_450004300`。`pre` 变体 BossManager 无 case（265 旧分发遗留）；
  282 下 easy 车道由 `CustomFieldScripts.firstenter_*` 在 `Field.execFirstUserEnterScript`（Field.java:806-811）
  提前接管（"the boss's custom initializer owns its summon and intro"），根本不走 Fenter 脚本。
  boss 门的 portal script 三个车道都叫 `pt00_450004250`（服务端无处理）。
- `BossLucid.img/Lucid/1/phase2`（hard）：bossMob 8880151、bossFuryMob 8880152、bossFuryReviveMob 8880153、
  bossFuryFailMob 8880154、furyLimitTime 45s、furySuccessMoveDelay 5 / fail 10、
  `bossFuryFailResummon=8880151`、`bossFuryResummonHPR=16`、复生位 (711,-786)。

## 4. 奖励阶段（REWARD）
- 450004300 spawn「最後的音樂盒」（9999999 HP，(50,36)）：easy=8880177、normal=8880167、hard=8880156，
  先 1s 后 weatherEffect(BossLucid)「恭喜擊敗簡單/普通/困難模式露希妲！」，2s 后 spawn；`lucidRewardSpawned` 防重。
- 击杀音乐盒 → `addPartyBoss/addPartyBoss(party, LUCID_*)` 记次数（BossHelper:1036/1122），掉落档位 2
  （超难礼 10万-50万，`getGiftTier` :1297）。

## 5. 机制（服务端驱动）
### 5.1 蝴蝶（ButterFly）
- 周期任务：spawn 每 5s（首延 1s）、destroy 每 32.5s（首延 33.5s）。spawn：`ButterFly(rand 0..8, pos)` 逐只
  `LUCID_BUTTERFLY_INIT`（`i32 unk, i32 n, n x (i32 type,i32 x,i32 y)`）+ `butterflyAction(Add)`。
- 位置表：`W/Etc.wz/BossLucid.img/Butterfly/phase1_pos`(40 点)/`phase2_pos`(40 点)，`ButterFly.load()` 从
  BossLucid.img 读。P1 用 `rand(0, size-2)`，P2 用全表。
- destroy（`destroyButterFly(field, mode)`）：计数清零、`lucidStatueGauge=0`，随机抽 `mode*10+20` 个
  队员 cid 发 `butterflyAttack(count, chars)`（mode2=Attack），再 `statueStateChange(false, min(gauge,3), true)`。
- maxButterfly=40；`Butterfly/info` 按 boss HP 段提高频率：hp 90-101→term 5000/create 5；70-89→4500/7；
  50-69→4000/10；20-49→3000/15；1-19→2000/20（客户端表现参数）。
- **喇叭雕像**（horn）：客户端在 hornPos（P1: (1800,25)/(270,35)；P2: (1208,-384)/(78,-695)）读条
  （gaugeTime 2000ms，lt/rb -184,-252..73,10）发 **CP0617 LUCID_ACTIVATE_STATUE_REQUEST**（body `u8 1`）；
  服务端 `UserHandler.handleLucidActivateStatueRequest`：仅在 450004xxx（`fieldId/1000==450004`），
  置 `lucidStatueGauge=0` + `butterflyAction(Erase)` + `statueStateChange(false,0,true)` → 压制蝴蝶。
  另 CP0616 fairy dust 命中（空 body，265 无 handler，客户端自行结算）、CP0618 幻影弹幕结束（空 body，忽略）。
  注册链：`Tms282LoginOverlay → Tms282BossMechHandlers → Tms282LucidHandlers`。

### 5.2 技能（MobSkillID.Lucid=238，MobSkill.java:1036）
**触发链（MobHandler:202）**：mob 控制端玩家请求技能（`mob.getControllerID()==chr`）→ 请求的 skillID/slv
在模板表内且**过冷却**（`hasSkillOffCooldown`）→ 施放；Lucid 特例：`skillID==0` 时也可按
`lucidMode*5+5`%（E5/N10/H15；mode=-1 非 boss 场 100%）概率自发放行。冷却值=dat 的 `interval` 秒
（`mob.putSkillCooldown(now+interval*1000)`，MobHandler:230）；`hp` stat=解锁血量线（boss HP% 低于该值才可用）。
**mobSkill dat 全量解码**（dat265=dat282，格式：大端 `>h id,>h slv,6×[u8 has+i32 x,i32 y] lt/rb/lt2/rb2/lt3/rb3,
>i16 n×[u8 statOrd+UTF str],>i16 n×i32`；stat 枚举序=x,mpCon,interval,time,hp,info,y,zero,limit,broadCastScreenMsg,
w,z,parsing,prop,ignoreResist,targetAggro,fieldScript,elemAttr,delay,rank,HPDeltaR,summonEffect,y2,q,q2,s2,u,u2,v,
z2,w2,skillAfter,x2,script,attackSuccessProp,bossHeal,face,callSkill,level,linkHP,timeLimitedExchange,summonDir,
summonTerm,castingTime,subTime,reduceCasting,additionalTime,notDestroyByCollide,noGravity,rangeGap,sideAttack,
forcex,targetType,force,count,fixDamR）：
- 238-1/2/3 花陷阱：interval 8、x2=1
- 238-4 精靈粉末：v=100 z2=10 s2=240 u=2640 interval=5 z=10 x=40 y=-180 w=3 w2=1 x2=1
- 238-5 雷射雨：interval=80 z=15 hp=95 w=500 x2=4
- 238-6 強制傳送：interval=30 hp=90
- 238-7 龍（Breath）：interval=50 hp=97
- 238-8 突進（Charge）：interval=120
- 238-9 幻影彈幕：无 stat（时长走服务端 lucidState 12s）
- 238-10 精靈粉末：v=250 z2=10 s2=330 u=2640 interval=10 z=10 x=5 y=30 w=6 w2=3 x2=1
- 238-11 強制傳送(Easy)：interval=40 hp=90
- 238-12 傳染：lt(-10000,-2000) rb(10000,2000) count=6 prop=100 interval=90 time=10 ignoreResist=1
- 201-199 召喚蝴蝶：lt(-400,-200) rb(400,20) limit=200 targetType=2 summonEffect=81
  **ints=[8880179,8880178,8880175]**（召唤的蝴蝶 mob 模板）
slv 分支服务端逻辑：
- slv 1/2/3 **花陷阱**：`doSkill(slv, pattern=rand3, pos(1000,48), u8 rand50)`。
- slv 4/10 **精灵粉末**（fairy dust）：随机 3~9 粒，`FairyDust(scale rand3, u=2640, speed, angle)`；
  slv4 x=40 s=180/240 v=100/5 w=3/1；slv10 x=5 s=30/330 v=250/100 w=6/3。播报「露希妲即將釋放力量！」。
- slv 5 **激光雨**：15 段 interval=500，startDelay=4500。「露希妲即將使用強力攻擊！」。
  客户端 `LaserRain/info`：collisionDelay 1260、duration 1740、range 1000x450、rangeH2 100、length 1000。
- slv 6/11 **强制传送**：随机 charId（`Randomizer.nextInt(8)`，按 cid 后 3 位取）。
- slv 7 **龙/召唤兽**：P1 场 `createDragon(1, 0,0,0,0, left)`（left 70%）；P2 场
  createPos (-138|1498, -1312|238)，pos 用 mob.y。播报「強大的召喚獸正在接近！」。
  `Dragon/phase1/2 info`：prepare 3000/6000ms、attack 5000、end 3000、createDelay 3000、breathDelay 1650、
  breath 区域 phase1 (-3539,-269)..(-542,152)、phase2 (-4000,-163)..(-156,157)。
- slv 8 **突進 rush**：`doSkill(8)` 空 path=0，客户端走 `Etc/BossLucid.img/RushLucid/path0`（14 段路径
  start(685,-510)，v 15~30；body (-47,-135)..(76,14)，attackIdx 6）。「攻擊那股風會讓夢境變得更強！」。
- slv 9 **幻影彈幕**（welcomeBarrage）：`lucidState=DoSkill` 12s；依次 `barrage(2)`（清）、
  `stainedGlassOnOff(false, STAINED_GLASS)`（11 块彩绘玻璃 tag）、`setFlyingMode(true)`、蝴蝶移到 (600,-500)，
  再三波：barrage(0,180,30) + barrage(3, rate50/100/90, speed 120/100/90, interval 500/500/700, count 3/4/8)
  + barrage(4/5, angle 180, speed 30/70/100, interval 700/1000/700, count 12, diff 30, bulletRate 5/10/0...)。
  「露希妲正在呼喚真正的力量！」。客户端 `Shoot/info`（spiral/bidirection 参数、lucid pos (600,-500)、
  butterfly 四角 (-100,-980)/(1300,-980)/(-100,50)/(1300,50)，loopDelay 12000）。
- P2 场进技能时先 `MobPool.forceChase(mob, true)`（`Lucid.isSecondPhaseField`）。
- 天气播报（weatherType 222）文案在 `BossLucid.img/WeatherMessage`。

### 5.3 彩绘玻璃（StainedGlass）
- tag 表 `BossConstants.STAINED_GLASS`：Bblue1-3、Bred1-3、Mred2-3、Myellow1-3（11 块）。
- `LUCID_STAINED_GLASS_ON_OFF`（`u8 on, i32 n, n x str`）/`BREAK`（`i32 n, n x str`）；282 字符串重编码。
- **责任边界**：服务端只在幻影彈幕开始时关（MobSkill.java:1106 ON_OFF false）、`LucidState→None`
  （12s 结束，Field.java:3163）时广播 `welcomeBarrage(1)`+ON_OFF true+`setFlyingMode(false)` 重开；
  **`stainedGlassBreak` 包在 282 服务端无调用方**——玩家打碎玻璃的判定与表现在客户端场类本地完成。
  迁移时破碎逻辑按客户端表现自行实现（打碎后小怪涌出）。
- `StainedGlass/info`：breakTerm 10000、recoverTerm 3000、summon 按难度小怪 15 只
  （normal 8880170/hard 8880182/story 8880194/easy 8880188，maxSummonMobID 8880171/8880183/8880195/8880189）。

### 5.3a 服务端判定 vs 客户端表现（责任边界总表）
| 机制 | 服务端 | 客户端 |
|---|---|---|
| 蝴蝶生成/自爆 | timer 驱动，INIT/Attack 包 | INIT 建实例、Action 包驱动 add/erase/attack 表现（Effect3/BossLucid 数据） |
| 喇叭雕像 | 收 CP0617 清 gauge+压制 | 读条 UI、发送 CP0617、gauge 表现 |
| 238 技能 | 冷却/血量门/概率判定 + doSkill 分支发包 | 控制端请求时机、各技能视觉（MobSkill/_Canvas/238.img + BossLucid.img 参数节点） |
| 彩绘玻璃 | 仅 ON_OFF（弹幕开始关/结束开） | 破碎判定+表现+小怪涌出（BREAK 包服务端不发） |
| 突進/激光雨/龙/弹幕/粉末 | doSkill 发参数包，伤害按模板 attack 表结算 | BossLucid.img 的路径/区域/时序数据本地驱动 |
| Fury | 45s timeout、spawn/复生判定 | fog/fog2 雾效、LAST_PHASE_HARD_MODE 包（0x0826）驱动背景切换 |
| BombObject/HurdleArea/DragonShadow | 无服务端逻辑（纯场装饰/碰撞） | 场类 update loop（38F1360/38FA960）本地驱动 |

### 5.4 梦境内专属机制（BossLucid.img 其余节点）
- `HurdleArea`（attackIdx 3，mob 原点 (-20,-500)..(20,10)）：hurdleMob（P1 8880161/8880181/8880187，P2 8880171/8880183/8880189）。
- `DragonShadow`（move 100、rangeMin 300、rangeRand 500、appearDelay 3000、pos (958,-102)）。
- `BombObject`：phase1 area1 (750,44)-(1290,45)、area2 两段 (75-270)/(1620-1930)；phase2 三区（y=-850/-864/-275/-150/-60），
  即 P2 六块碎玻璃踩踏区（对应北斗移植 LucidTables 的碎玻璃）。
- `Fury/info`：fogTime 45；Fury 包 `LUCID_LAST_PHASE_HARD_MODE`（0x0826，type 1=背景/2=fail/3=fog）。

## 6. 282 客户端 RE（照录 Tms282LucidTranslators 头注释，research/tms2823/lucid-re-0930）
- 265 的 10 个 LUCID_* Out（2023..2033）在 282.3/282.4 客户端重映射为 **0x081C..0x0826**：
  BUTTERFLY_INIT 0x081C(38E8180)、BUTTERFLY_ACTION 0x081D(38E7F20)、DRAGON_INIT 0x081F(38EC660,21B)、
  DO_SKILL 0x0820、STAINED_GLASS_ON_OFF 0x0821(38FAE60)、BREAK 0x0822(38FB2B0)、
  STATUE_STATE_CHANGE 0x0823(3D843D0)、SET_FLYING_MODE 0x0824、WELCOME_BARRAGE 0x0825(3904F40)、
  LAST_PHASE_HARD_MODE 0x0826(3902AF0 "Fury")。
- 两个场类：**38F13D0**（vtable 86417B8，P1：0x081C/081D/081F/0820/0823，其余走 CField::OnPacket 3725AF0）
  与 **38FAA00**（vtable 8642BF8，P2/last：0x081C..0x0826、0x082A）。ctor 38F0662/38F07A3/38F7D39、38F9A57/38F9BD7/3901A3E。
- DO_SKILL 0x0820 只认 skillId=238；slv 12（38FEA40）为 282-only、265 服务器不发。barrage type 6 会回 CP0618。
- 入包 CP0616/0617/0618（SendPacket 346A070 无尾 u32，谓词 347ECF0）；fairy dust 计数函数 576BC40；
  P1/P2 update loop 38F1360 / 38FA960；CP0617 sender 3D82380/3D83860。
- 服务端 translator：`Tms282LucidTranslators`（`-Dtms282.lucid.enabled=false` 可关）。

## 7. 變身燃燒（LucidDream，独立玩法）
- 进度：QR 600384（lv/exp）、通行证 QR 102930 passCnt、计数 QR 102883、Boss 奖励 QR 102963/600379。
- 杀怪给露希妲经验（仅变身中）：食夢者 9834752=40、惡夢的走狗 9834754=25、夢幻的根源 9834753=400
  （可 `-Dtms282.lucid.exp.<id>` 覆盖）；主角色经验 = 角色升级需求 × exp/10,000,000。
- LucidBoss 任务映射：8800102→102931、8930000→102932、**8880153→102933（露希妲困難）**、
  9834755→102934、9834770→102961；Lv70 任务 102941。
- 技能：异想之梦 80004201（type 10、no time、isFieldMovingLimit）= 飞行开关，承载 CTS NewFlying(bit 558)；
  客户端证据：5A79D00 起飞、5EFEC60 移动重算、4442C70 技能 on 判定、36D44F0 取消。
- 能力值：RPEventStat（CTS bit 978，19×i32）0..18 = LEVEL/EXP/MAX_HP/MAX_MP/ATK/ATK_SPD/DEF/HP_RECOVERY/
  MP_RECOVERY/CRI/...；StatBlock(LP02CC, reader 1015020, 20×i32) 顺序 maxHp,maxHpR,maxMp,atk,atkR,atkSpd,def,
  hpRecovery,mpRecovery,cri,criDamR,moveSpd,jump,stance,fs,maxExp,mastery,reduceCoolTime,atkMinRange,atkMaxRange。
- 夢幻的試煉副本（993290100/993290300）复用 P1/P2 机制：`Lucid.setExtraFields()` 由 `DreamTrialService`
  按 JSON 注册（tms282.lucid.dreamTrial 开关），`NORMAL_FIRST_PHASE_FIELDS={450004150,450004450,450004750,450003840}`。

## 8. 客户端 Data 树与全部资源路径（迁移清单）
> 282 客户端 `~/Documents/mxd/MapleStory/Data/` 是**解包后的 WZ 目录树**（二进制 .img，新版高版本编码），
> 这是迁移素材的真正源头；`TMS282.5/data/wz282/` 只是套件抽取的 XML 子集。

### 8.1 Etc/BossLucid.img（机制总表，187,420B，必迁）
客户端 `Data/Etc/BossLucid.img`；服务端套件同内容 XML：`TMS282.5/data/wz282/Etc.wz/BossLucid.img.xml`（1466 行，
**`ButterFly.load()` 在服务端按 `%s/Etc.wz/BossLucid.img.xml` 路径直读**）。顶层节点：
`Lucid/{0,1}`（0=P1、1=P2/last）下 `phase1`/`phase2`：
- `phase`/`maxButterfly(40)`/`bossMob(8880140|8880151)`/`hurdleMob(8880161|8880171)`/`bossMob2(8880166)`/
  `bossFuryMob(8880152)`/`bossFuryReviveMob(8880153)`/`bossFuryFailMob(8880154)`/`bossFuryFailResummon(8880151)`/
  `bossFuryResummonHPR(16)`/`furyLimitTime(45)`/`furySuccessMoveDelay(5)`/`furyFailMoveDelay(10)`/
  `bossFuryRevivePos(711,-786)`；`portal` pt00..pt03（P2 portal 名，与地图传送点 pn 对应）；
- `horn`：hornPos P1 (1800,25)/(270,35)、P2 (1208,-384)/(78,-695)；info lt(-184,-252) rb(73,10)、gaugeTime 2000；
  stand/ready/use/effect 贴图子节点（雕像/喇叭读条 UI，同 img 内）；
- 其他顶层：`Butterfly`（phase1_pos/phase2_pos 各 40 点、info 按 boss HP% 的 create/term 频率表）、
  `Dragon/phase1|phase2/info`（prepare 3000|6000、attack 5000、end 3000、createDelay 3000、breathDelay 1650、
  breath 区域 P1 (-3539,-269)..(-542,152)、P2 (-4000,-163)..(-156,157)）、`LaserRain`（collisionDelay 1260、
  duration 1740、range 1000x450、rangeH2 100、length 1000）、`Shoot`（弹幕参数、lucid pos (600,-500)、
  butterfly 四角 (-100,-980)/(1300,-980)/(-100,50)/(1300,50)、loopDelay 12000）、`StainedGlass`（breakTerm 10000、
  recoverTerm 3000、按难度 summon 15 只：normal 8880170/hard 8880182/story 8880194/easy 8880188，maxSummonMobID
  8880171/8880183/8880195/8880189）、`RushLucid/path0/info`（14 段路径 start(685,-510) v15..30，body
  (-47,-135)..(76,14)，attackIdx 6）、`BombObject`（P1 area1 (750,44)-(1290,45)+area2 (75-270)/(1620-1930)，
  P2 三区 y=-850/-864/-275/-150/-60）、`HurdleArea`（attackIdx3，(-20,-500)..(20,10)）、`DragonShadow`
  （move 100、rangeMin 300、rangeRand 500、appearDelay 3000、pos (958,-102)）、`Fury/info`（fogTime 45）、
  `WeatherMessage`（weatherType 222 文案）。

### 8.2 地图（16 张全在客户端，服务端 XML 只有 3 张）
- 客户端 `Data/Map/Map/Map4/450004000.img` … `450004900.img`（0/50/100/150/200/250/300/400/450/500/550/600/700/750/800/850/900 全 16 张，含 `Map4/_Canvas/450004000.img`、`450004850.img`）。
- 服务端套件 XML：`TMS282.5/data/wz282/Map.wz/Map/Map4/450004{000,300,600}.img.xml`（282 改动版）；
  **其余 13 张（450004100/150/200/250/400/450/500/550/700/750/800/850/900）直接用 wz265 的
  `data/wz265/Map.wz/Map/Map4/*.img.xml`**（282 manifest 无改动记录，265 版即正确内容；dat265/fields 16 张全有）。
- 450004000 关键字段：`bgm=Bgm46/ClockTowerofNightmare`、`mapMark=Lacheln`、`lvLimit=220`、`qrLimit=34330`（入场通行证 QR）、`returnMap/forcedReturn=450004000`、VR (-1000,-800)..(748,179)、`onUserEnter=enter_450004000`、portal `sp`(出生点, -106,19)、`pt02`、`out00` script=`BPReturn_Lucid`。
- 450004300：`onUserEnter=enter_450004300`、portal `out00` script=`out_450004300`、bgm 同上、fieldLimit 34349816。其余 13 张图的 portal/life/VR 从 `wz265/Map.wz/Map/Map4/*.img.xml` 读（无需碰客户端二进制 img）。地图相关脚本名：`enter_450004xxx`（onUserEnter）、`Fenter_450004xxx`（进 boss 门）、`out_450004250/300`、`west_450004150`、`firstenter_450004150/200/250`、`BPReturn_Lucid`。
- 背景资源：`Data/Map/Back/lucidDream.img`、`2023LucidDream.img`、`2023LucidDream_SG.img`（各有 `_Canvas` 版）；`Data/Map/Obj/` 同名 obj；`Data/Map/MapHelper.img`（梯子/工具）。

### 8.3 Mob（服务端模板 vs 客户端渲染）
- **282 boss 视觉全部走 spine/Canvas**：P1/P2 本体、Fury、石巨人等在 `Data/Mob/_Canvas/88801xx.img`
  （8880100/8880101/8880102/8880103/8880121/8880140/8880150/8880151/8880152/8880153/8880154/8880156/8880157/
  8880158/8880160/8880161/8880162/8880163/8880165/8880170/8880171/8880175）。小怪可复用 canvas。
- **客户端 Data 树缺**：8880155、8880166、8880167、8880177、8880190、8880191、8880194、8880196 的 img
  （easy P1、双 P1 伴随、easy/normal 音乐盒、story 模式mob）——这些模板 mob 本体不可见或由 spine/Effect3
  渲染，mob img 只当不可见碰撞盒用（8880155 的 WZ XML `fly/hit1` 等节点全为空即此意）。
- 服务端套件 XML：`TMS282.5/data/wz282/Mob.wz/8880{140,141,142,150,151,153,155,181,183,184,185,190,191}.img.xml`；
  dat：`dat282/mobs/` 有 8880140/141/142/150/151/153/155/181/183/184/185/190/191，**缺** 8880152/154/156/167/177/161/171/175/194/196
  ——不构成运行缺口：dat265/mobs（base 层）这 10 个全有，overlay 查找会回退（见 9.1/9.2）；282 XML 已解出补进 wz282。
- 8880155.img.xml 关键值（easy P1，normal 8880151 同构仅 info 韩文名不同；8880153 只有 89 行=纯 Fury 复生壳，
  skill 段是 114-82 占位；8880190=story 模式）：level 230、exp 30000000、charismaEXP 336、PADamage 22000 /
  MADamage 24000、PDRate/MDRate 300、acc 9999 / eva 750、boss=1、firstAttack=1、hideHP=1（HP 条显示「無法測量」）、
  elemAttr P2H2F2I2S2L2D2、mobType 3N、summonType 1、hpTagColor 1/hpTagBgcolor 5、pushed 10 亿、wp 300。
  - `info/skill`（skill/level/action）：0=**238-10 精靈粉末**(action1, effectAfter0, fixDamR10)、1=**238-7 龍**
    (action2)、2=**238-5 雷射雨痕**(action5, stopByBind, skillForbid 19050)、3=**201-199 召喚蝴蝶**(action3,
    skillAfter 1530)、4=**238-9 幻影彈幕**(action4, onlyOtherSkill, skillForbid 23360)、5=**238-12 傳染**
    (action3, skillAfter 2040)、6=**238-8 突進**(action6, skillAfter 960, skillForbid 13680)。
  - `info/attack`（击中结算）：0 虛擬 fixDamR30 cooltime 1e8；1 精靈粉末實際傷害 fixDamR30；2 龍之氣息
    fixDamR100 tremble；3 召喚惡夢石巨人(第2次更新不使用，虛擬用途) disease 134 lv9 fixDamR70；
    4 雷射雨 fixDamR34 cooltime 1000；5 幻影彈幕 fixDamR33；6 突進 fixDamR51。
  - `info/flyingMove` key=`lucid`、range 150、moveLimit (-80,-950)..(1260,-20)，11 个飞行航点
    (670,-700)(385,-400)(-1000,-400)(810,-731)(440,-280)(1000,-200)(1050,-600)(500,-500)(990,-250)(370,-620)(770,-420)，v=30、ImpactInterval 600、impactRot 45/-45、destAttack 1。
  - 帧节点 `fly`(空=靠 canvas)、`hit1/die1` uol 引用、`attack1..6` range（attack1 精靈 (-260,-190)..(258,0)，
    其余 (-15,-15)..(15,15)，onlyFsm=1）；`skill5` 的 uol 46..61 引用 fly/0..14。
- mobSkills：**238-1..12 全 12 级 dat 两层都齐**（dat265/dat282 各 12 个，此前"仅 8 个"是抽查遗漏）；
  201（召喚蝴蝶）1..334 级 dat265 全量。客户端数据 img `Skill/MobSkill/238.img`/`201.img` 在
  `Data/Packs/Skill_00008.ms`（条目 `Skill/MobSkill/238.img`，迁移时解包）；特效 canvas
  `Data/Skill/MobSkill/_Canvas/238.img`。
- **各模板实际技能表（WZ info/skill，迁移的"实际会放什么"以此为准）**：
  - 8880155（easy P1）/ 8880151（normal P1）：同构 7 条 → 238-10 精靈粉末(action1)、238-7 龍(action2)、
    238-5 雷射雨(action5, stopByBind, skillForbid 19050)、201-199 召喚蝴蝶(action3, skillAfter 1530)、
    238-9 幻影彈幕(action4, onlyOtherSkill, skillForbid 23360)、238-12 傳染(action3, skillAfter 2040)、
    238-8 突進(action6, skillAfter 960, skillForbid 13680)；attack1..6 命中框两模板一致。
  - 8880150（P2 本体）：仅 1 条 238-10——P2 的其余伤害走 Lucid.java 场机制 + attack 表，不走技能表。
  - 8880153（Fury 复生壳）：仅 114-82 占位。
  - slv 1/2/3（花陷阱）、4/6/11（粉末/传送）：doSkill 有分支、dat 存在，但 282 模板表未引用
    （265 路径 8880140 遗留或备用），迁移时实现分支但不主动触发。

### 8.4 音效 / BGM / spine
- BGM：`Data/Sound/Bgm46.img`（450004xxx 全部 `Bgm46/ClockTowerofNightmare`）。
- 音效：开场 `Sound/SoundEff.img/ArcaneRiver/lucid_spine`（音量 200）；265 旧开场 `ArcaneRiver/phase1`。
  （已核实：`ArcaneRiver` 下 lucid_spine/phase1/phase2/nightmare 等 33 个子节点确实存在，见 9.2。）
- spine：开场 `Map/Effect3.img/BossLucid/Lucid/lusi`（`Data/Map/Effect3.img` 内 `lusi` 字符串 7 处证实）；
  过场 `Map/Effect3.img/BossLucid/butterfly2/buterfly`。这两条是 **spine 动画**（不是 MCV 视频），
  与北斗 `custom-dll-systems.md` 的 Canvas boss 视频（Map/Effect.img/<FIELD_EFFECT>/0，Lucid 签名
  112244/556677/8899AA/BBCCEE）是两套东西，迁移时勿混。
- 變身燃燒技能：`Data/Skill/` + `TMS282.5/data/wz282/Skill.wz/Skill/800042.img.xml`（80004200/80004201 系列）。

### 8.5 任务 / 掉落 / 次数
- 任务 dat：`dat282/quests/{34330,100089,16995,36117,100102,102931,102932,102933,102934,102961}.dat`（變身燃燒
  5 个 LucidBoss 首杀任务 102931-934/102961 **dat282 全有**，dat265 没有属正常——overlay 覆盖）；
  **34360/102401/102930/600384/102883/102963 是 QR 值型任务，两层都无 dat**（`createQuestWithQRValue` 运行时现建，正常）。
- 掉落：`TMS282.5/data/resources/mob_drops.json` 含 8880156（音乐盒 hard）条目。
- 客户端任务表现资源：`Data/Quest/QuestData/`（25033 个）。

## 9. 迁移/验证边界（对本项目）
- 北斗端已有移植：`event/LucidBattle.js` + `Assets/Scripts/Field/LucidBossCompat.cs`/`LucidTables.cs`
  （见 beidou-client-reverse/references/map-intro-and-events.md:240）；282 与 265 的差异主要是
  模板 id（8880155/151/153 vs 8880140/150）、HP 值、hard Fury 二段、282 开场 spine 时长 9167ms、
  skillId 238 的 slv 9 弹幕参数、CP0617 雕像压制。
- 模板语义（第四轮已查清，见 §3）：WZ 官方语义 8880155=easy P2、8880151=hard P2、8880153=Fury 复生壳；
  套件把三者当 E/N/H P1 spawn 是作者再诠释。迁移默认照抄套件（方案 a）+ P2 HP=P1；改动时再评估方案 b。
  （8880152/8880154 dat 缺失已证伪，见 9.2。）
- 客户端 mob 技能表（dat282/mobs/8880155.dat）：精灵粉末/龍/雷射雨痕/召喚蝴蝶/幻影彈幕/傳染/突進/虛擬
  + 各「實際傷害控制用」影子技能，slv 与 MobSkill.java 分支一一对应。
- 客户端 Data 缺 8880155/8880166/8880167/8880177/8880190/8880191 的渲染 img（不可见碰撞盒设计，
  勿当素材缺失误判）。

### 9.1 数据分层（先读这节，否则会误判"缺口"）
282 服务器查找数据文件**不是单目录**，而是两层叠加（`loaders/overlay/DataOverlays.java`）：
- **base 层**：`ServerConstants.DATA_VERSION=265` → `data/dat265` + `data/wz265`（套件随附，全量）。
- **overlay 层**：仅当 `protocol.version=tms282.3`（`ProtocolConfig`，即 282 profile）时启用
  `data/dat282` + `data/wz282` + `data/resources/tms282`。
- **查找顺序**：`DirectoryDataOverlay` 先查 overlay 文件，存在则用 overlay；不存在**回退 base**
  （`datFile`/`wzLayers`，overlay 是"按文件覆盖"不是整目录替换）。
- `QuestShortcut.enabled()` 与大量 TMS282 特性都以 `ProtocolVersion.TMS_282_3` 为开关。
- 推论：**dat282 里缺、dat265 里有的文件不构成运行缺口**（base 兜底）；两层都缺才是真缺口。
  （2026-11-02 早先"服务器运行读 dat265 不是 dat282"的说法不完整，以此节为准。）

### 9.2 缺口总表（2026-11-02 第三轮全量核实，逐项查证后的最终状态）

| 项 | dat265(base) | dat282(overlay) | 客户端 Data | 结论 / 迁移动作 |
|---|---|---|---|---|
| boss mob 8880140/141/150/151/153/155/190/191 等 13 个 | ✅ | ✅ | ✅ | 双层齐全；8880152 maxHP 已交叉验证一致 |
| Fury/Fail/音乐盒等 10 个 mob（8880152/154/156/161/167/171/175/177/194/196） | ✅ 全有 | ❌ 缺 | ✅（_Canvas + 已解出 XML） | **运行无缺口**（base 兜底）；282 层已补 XML 进 `wz282/Mob.wz/`，dat 待 JDK22 导出（可选） |
| 地图 450004000/300/600（282 改动版） | ✅ | ✅ | ✅ | 用 282 层（overlay 生效） |
| 地图 450004100~900 其余 13 张 | ✅ 16 张全有 | ❌ 缺 | ✅ | **运行无缺口**：282 manifest 证明未改动 → base 的 265 版本就是正确内容 |
| 任务 34330/100089/16995/36117/100102（副本入场/通关） | ✅ | ✅ | ✅ | 无缺口 |
| 任务 102931/932/933/934/961（變身燃燒 LucidBoss 首杀） | ❌ 缺 | ✅ **全有** | ✅ `Data/Quest/QuestData/1029xx.img` | **无缺口**（overlay 覆盖；曾误报 265 缺，但 282 层有即可） |
| QR 值型任务 34360/102401/102930/600384/102883/102963 | ❌ | ❌ | ❌ | **设计如此**：`createQuestWithQRValue` 运行时现建，无 dat 正常 |
| mobSkills 238-1/5/7/8/9/10/11/12 | ✅ 8 个全有 | ✅ 8 个全有 | ✅ `Skill/MobSkill/_Canvas/238.img` | 无缺口 |
| 音效 `SoundEff.img/ArcaneRiver/lucid_spine` | — | — | ✅ 已 wzpy 解析证实（ArcaneRiver 下 33 节点） | 无缺口（旧"strings 未命中"是加密 WZ 假象） |
| 开场/过场 spine `Effect3.img/BossLucid/…` | — | — | ✅ lusi(11帧)/buterfly/fog/fog2 全证实 | 无缺口 |
| 8880155/166/167/177/190/191 客户端渲染 img | — | — | ❌ 无渲染 img | **设计**：不可见碰撞盒（8880155 WZ `fly/hit1` 全空），勿当素材缺失 |
| dat282 全量重建 | — | — | — | 需 JDK22（源码含 Java22 字符串模板，JDK17 编不过）；仅"282 数据层完全自洽"时才需要，迁移非必需 |

**唯一遗留动作（可选）**：给 dat282/mobs 补上述 10 个 mob 的 dat——装 JDK 22 后 `mvn compile`
再跑 `loaders/MobData.main()`（从 wz282 XML 全量重建）。不做也不影响运行与迁移（base 已兜底，
且 8880152 的 dat265 值与 282 解包 XML 已数值一致）。

### 9.3 数据层补充事实与工具（第二轮复核产物，结论已并入 9.2 表）
- **base 层 mob dat 全量可用**：`dat265/mobs` 里 10 个"缺口" mob（8880152/154/156/161/167/171/175/177/194/196）
  全有 dat，overlay 回退即命中。dat 字段**大端序**：
  `>i templateId, >i level, >i firstAttack, >i bodyAttack, >q maxHP, >q maxMP, >i pad,pdr,mad,mdr...`。
  验证一致：dat265/8880152 maxHP=100000000 == 282 img 解包值；8880155/150/153 dat 里 maxHP=1337
  是占位，运行时由 BossHpConstants.resolveMaxHp 覆盖。
- **.ms 解包器已落地**：`MapleDash/tools/ms282/`（MsUnpack.java + SnowTables.java + out/ 已编译 +
  ms282_img2xml.py + mob_raw/ + mob_xml/）。282 客户端 `Data/Packs/*.ms`（Mob 4 卷 + Skill 9 卷，
  Packs.ini 记录卷数）是 ChaCha20-v4 格式（Snow2 v2 兜底），盐= `alDFd@e1DFSG0wl950mc#^`（随机）。
  用法：`java -cp out ms282.MsUnpack <file.ms> list [substr] | extract <outDir> <name>...`，
  条目名形如 `Mob/8880152.img`；解出的 img 是 BMS（零 IV）WZ，wzpy 用
  `WzKey.for_region('BMS')` 直接解析。全部 8880 mob 在 Mob_00000.ms（7135 条目）。
- 10 个 mob 的 282 XML 已解出并落入 `TMS282.5/data/wz282/Mob.wz/`（8880152 FuryMob：selfDestruction
  action6、revive→8880153、maxHP 1亿 string 型、flySpeed 5；8880154 Fail：maxHP 100万；8880156/167/177
  音乐盒族：mobType 14N、fixedDamage 1000、hideName、explosiveReward；8880194 revive→8880195 maxHP
  1.71874亿；8880196/8880175 maxHP 32374800；8880161 maxHP 1亿；8880171 maxHP 5亿）。
- **地图缺口关闭**：450004100/150/200/250/450/500/550/750/800/850/900 在 282 manifest 无改动记录
  （282 只改了 450004000/300/600）→ 直接复用 `TMS282.5/data/wz265/Map.wz/Map/Map4/*.img.xml`。
- **SoundEff 节点证实**：`Data/Sound/SoundEff.img`（128MB，BMS 头）wzpy 解析成功，
  `ArcaneRiver` 下确有 `lucid_spine/phase1/phase2/nightmare/arrow*/butterfly*` 等 33 个子节点。
  同理可解任意 `Data/**/*.img`（都是 BMS raw img）。
- **dat282 重建口径**：套件 `MobData.main()`（loaders/MobData.java:1367）从 WZ_DIR XML 全量重建
  dat；源码用 Java 22 字符串模板（STR."..."，ServerConstants.java:26-29），本机 JDK 17 编不了，
  需 `brew install openjdk@22` 后 `mvn compile -Dmaven.compiler.release=22` 再跑（仅 282 数据层自洽时需要，见 9.2）。
- **Effect3.img 全验证**：`Data/Map/Effect3.img/BossLucid/` 下实际节点：`Lucid/lusi`（lusi.atlas +
  lusi.json + lusi*.png 共 11 帧，开场 spine）、`butterfly2/buterfly`（buterfly.atlas + spine，过场）、
  `fog/`（002.atlas）与 `fog2/mist_003`（Fury 雾，对应服务端 Fury fogTime 45）、`butterfly/005`、
  `Lucid2..5`（帧序列）。之前"BossLucid 字符串 0 命中"是 strings 对加密 WZ 失效的假象。
