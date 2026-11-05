# TMS282 威爾（Will）完整逻辑

> 2026-11-05 整理，结构与口径同 `lucid-boss.md`。服务端源码 `~/Documents/mxd/TMS282.5/source/src/main/java/net/swordie/ms/`（S）；
> 提取 WZ XML `~/Documents/mxd/TMS282.5/data/wz282/`（W）/ `wz265/`（W265）；客户端 Data 树 `~/Documents/mxd/MapleStory/Data/`（C）。
> 套件内含 RE 注释（research/tms2823/will-flow-0930、boss-mech-0930、bossmech2-0930），本文照录其确定结论。

## 1. 组成
- **普通 boss 副本**：`world/boss/Will.java`（开场 spine + 三阶段召唤 + 月光计）+ `Tms282WillPhase1.java`
  （282 P1 双次元门槛 HP 控制，Mob.java:1362 挂伤害钩子）+ `BossHelper`（P1/P2 击杀跳图、P3 通用通关、周次数）。
- **协议**：`WillPacket`（265 名）+ `Tms282BossFieldTranslators`（282 重映射）+ 三个上行 handler（MobHandler 1012-1082）。
- **客户端数据**：`Etc/BossWill.img`（791 行 XML，机制参数总表）+ `Effect/Direction20.img/bossWill`（intro）。

## 2. 注册与入场
- `BossPartyType.WILL_EASY/NORMAL/HARD(23)`：等级 235、preQuest **37871**、OnceInSevendays、限时 `WILL_TIME=30min`。
- 地图（BossConstants）：
  - 入口 `WILL_ENTRACE_MAP=450007240`（W282 实测：Bgm49/TempleInTheMirror、esfera、lvLimit 235、
    `onUserEnter=enter_450007240`、portal pt00 script=`BPReturn_Will`——该名在 282 别名表归 `compatible_boss_exit`）。
  - 战斗图：E/N `450008750/850/950`、Hard `450008150/250/350`（fieldType **182/183/184**，
    `onFirstUserEnter=will_phase1pre/2pre/3pre`、fieldScript=`bosswill`）。
  - 过场（intro）图：E/N `450008700/800/900`、Hard `450008100/200/300`（`onUserEnter=will_direction1/2/3`）。
    **282 的 getBossEntryFieldId：E/N→450008700、Hard→450008100**（入场先进过场图，播完 +50 进战斗图）。
  - `FIGHT_FIELD_TO_ENTRANCE` 覆盖 450008100/200/250/300/350/400 + 450008700/800/850/900/950 → 入口图。
- 入场 buff `80002419`；入场检查链与 Lucid 同一条通用链（ScriptHandler viaBossUI：等级/前置/周次数/仅队长/
  `warpInstanceIn`+`setInstanceTime`，QR 102401 记 mapR/order/diff）。
- **【套件缺口】WILL_EASY 有注册无召唤**：`Will.spawnBoss` 只有 isNormal/isHard 两分支，Easy（diff 0）
  落到 `moonLightTime=1e8` 无召唤。迁移若开放三难度需自补 Easy 车道（WZ 段3 'easy' 8880360 系）。

## 3. 开场（playAnimation，282 路径）
- 仅在过场图（450008000+stage*100 / 450008600+stage*100）生效：`beginBossIntro(8000)` +
  spine `Effect/Direction20.img/bossWill/intro_spine/skeleton`（参数 stage 1..3）+
  音效 `Sound/SoundEff.img/esfera/boss/intro<stage>`（C 客户端 bossWill/intro_1..3 + intro_spine 已确认存在；
  esfera 音效节点未逐一 dump——SoundEff.img 128MB，工具超时，待真机核对），8s 后 `warp(field+50)`。
- 每人一次回调（不重建实例、不 party warp），282 有防重入判断。
- 265 旧路径：lockUI + spine + 7s unlockUI + 8s warpInstanceIn，逻辑同。

## 4. 三阶段与召唤（Will.spawn / spawnBoss）
- `spawn(stage, chr)` 只认战斗图 450008050+stage*100（450008150/250/350）与 450008650+stage*100（450008750/850/950）。
  **每个进场的人都要 initMoonGauge**（boss 已召出后再进的人也补），然后 synchronized spawnBoss。
- **P1（450008150/750）双次元**：normal 召 7 只——
  HP 载体 8880340（352,-2020，hp 8.4万亿，客户端不打它）、
  下半身 8880343（352,0）、上半身 8880344（352,-2020）同 hp、假人 8880351/8880355（352/252,0）、
  8880352/8880356（-2020 层）；hard 对应 8880300/303/304/321/325/322/326，hp 42万亿。
- **P2（450008250/850）**：normal 8880341（0,215，6.3万亿）+ 假人 8880353/8880357；hard 8880301（31.5万亿）+ 8880323/327。
- **P3（450008350/950）**：先清场内 SpiderWeb 再重铺 **35 张**（SpiderWeb.load 内置 30+26-... 共 64 个预设点，
  pattern 0/1/2 三种、尺寸 100/160/270），normal 8880342（-4,25，10.5万亿）+ 假人 8880354/8880358；hard 8880302（52.5万亿）+ 8880324/328。
- **WZ 段映射**（BossWill.img/Will，`info` 字段）：段0='easy'（8880310/11/12 投射在鏡面上的威爾 + 假人 8880332-38）、
  段1='normal'（8880340/41/42）、段2='hard'（8880300/01/02）、段3='easy'（8880360 系，另一套 easy 变体）。
  套件只用段1/段2。mob dat maxHP 全是 1337 占位，HP 全部走 Will.java 硬编码（上值）。

## 5. P1 门槛机制（Tms282WillPhase1，282 专属）
- 265 原版只认载体死（但载体只被 242/5 打，`onlyFsm` 客户端永不施放，且 265 HP 门拿当前 HP 比较错）——真服 2026-09-30
  复现「下身打死不了、永不进 P2」。**282 由 Tms282WillPhase1 接管**：
  - 门槛表 `[666, 333, 3]`（千分比 = 66.6% / 33.3% / 0.3%）；
  - 每个身体被打时 HP 卡在下一个门槛、门槛处无敌，直到**另一个身体也到同一门槛**才放行（放行时播 242/8 眩晕）；
  - 载体 HP 镜像两个身体的较大值；`WillPacket.setHp(门槛表, field, 载体, +3, +4)` 推送（300ms 节流）；
  - 双身体都到 3‰ → P1 结束：清场、2.5s 后全员 warp `field+50`（进 P2 过场图）。
- 265 profile 不走这条（回退旧逻辑）。

## 6. 月光计（moon gauge）与上行包
- `setMoonGauge(max, min)`：P1 45 / P2 50 / P3 25（min=消耗量）。`addMoonGauge(值)`。
  计时器每 1000ms +2（**hard 2000ms**，按 `BossHpConstants.getCurrentBossParty==WILL_HARD`），上限 100。
- **上行 WILL_USE_MOON_GAUGE**（MobHandler:1013），按图分派：
  - P1（450008150/750）：gauge-45，广播 `WILL_TELEPORT` + portalTeleport(**ptup/ptdown**——按 y<-1000 选目标，
    即上下次元切换) + 技能特效 80002421；
  - P2（450008250/850）：先移除临时状态 80002404，gauge-50，`cooldownMoonGauge(7000)`，7s 后播 80002404 特效；
  - P3（450008350/950）：gauge-5，`setClearSpiderWeb(2)` 持续 5s（期间碰网即清除），cooldown 5000。
- **上行 WILL_BEHOLDER_EYES_HIT**：命中眼球 gauge-10（旧码 `min(0,..)` 会清空/负值，bossmech2 修为 max(0,..)）。
- **上行 WILL_SPIDER_WEB_HIT**：碰蛛网且无 clearSpiderWeb → 扣 10% maxHP，载荷 isSeal=1 时上 Seal(slv40,5s)；
  NotDamaged/已死跳过。
- 消费缺口：**moonGauge 服务端只产出不消费**（无“黑暗”判定），纯 UI 资源；迁移时同样做成表现层即可。

## 7. 技能（MobSkillID.Will=242，MobSkill.java:1129）
模板（8880340/8880300 的 info/skill）：242-5（onlyFsm priority1）、242-15、242-4。slv 分支：
- **1/2/3 蛛雨**：假人/载体模板换算 id（8880321/22-18、8880323/24-22、8880353/54-12、其余-8），
  rand 17..23 段 `(i, 1800*(1+i/6), -650+130*rand(1,9))` 发 `WILL_SPIDER_ATTACK`（0x0881）。
- **4 P1 灭团读条**：播报「攻擊眼球以改變月光方向，並利用它生成月光屏障！」+ `WILL_USE_SPECIAL`；
  全场 mob attackBlock(0..9)；对每人随机 type 1/2（蓝/紫，按队伍人数对半）发 spiderAttack；清 242 AA；
  召两只 8880305（眼球，0,-2020 与 0,159）；30s 后：清眼球，收集 242 本体 AA + 玩家 400031039/400031040 AA
  内的玩家 → 依次穿保护判（圣魔盾/阿修罗/风灵结界/无敌系）后 **die()**；若 `useSpecialSkill` 则按 HP% 重设
  WillHPlist + 发 setHp + 242/8 眩晕；再 11s 后 242/15。
- **5 P2 地面地板**：type=rand(0/1)，`WILL_SPIDER_ATTACK` slv5 内嵌两块地板区域（type0: y -455..160 或 -2378..-2019，
  见 WillPacket.spiderAttack case5 的坐标），3s 后结算：AA（242 本体 + 400031039/400031040）内玩家按 type 落点
  扣 100% maxHP（type0 打 y>-455&&y<300 层，type1 打 y -2500..-1800 层），1s 后按 HP% 触发 242/4（分档 66.6/33.3/0.3）。
- **7 谎言之镜（P2/P3）**：播报「謊言之鏡會反轉攻擊…」+ USE_SPECIAL + attackBlock，5s 后 242/14。
- **8 眩晕（门槛放行）**：播报「機會來了！趁威爾露出破綻時攻擊他！」+ `WILL_STUN`（0x87B），清 242 AA，
  载体时对 +3/+4 身体发 `MOB_FORCED_SKILL_ACTION`(0x6CA) action=3。
- **14/15**：数据驱动（14 由 7 延迟触发、15 由 4 尾段触发）；265 路径 15 参与 WILL_SET_HP 广播。
- dat265/dat282 242-1..17 全量齐。

## 8. 击杀与奖励
- **P1 载体死（8880340/8880300）**：清场 + 全队 warp `+50`（282 +50 / 265 +100，BossHelper:437）。
  ⚠️ 282 下 Tms282WillPhase1 根本不让身体死（onBodyDamage 返回 true 吞伤害），载体死亡路径仅 265 生效。
- **P2 载体死（8880341/8880301）**：同上 warp +50。
- **P3 本体死（8880342/8880302）**：清场 + 清 SpiderWeb + 走通用通关 `completeCompatibleBoss`
  （QR 34360 `boss=1`、清场演出、掉落窗、退出排程）；同时 addPartyBoss：8880342→WILL_NORMAL、8880302→WILL_HARD。
- 掉落：套件 `mob_drops.json` 无 8880300s/8880340s 任何条目（迁移需自配）。
- 任务：preQuest 37871 两层 dat 全有；282 无 P3 专属后续任务写入（对比 Lucid 的 100089/16995 链）。

## 9. 282 客户端 RE（照录 Tms282BossFieldTranslators 头注释，boss-mech-0930）
- fieldType 182/183/184 → CField Will 场类 3A42220 / 3A49940 / 3A4D040（共享基类 3A346F0，另有 cooldown UI 4BAEAA0）。
- 282 opcode：`0x086C SET_MOONGAUGE(8B)`、`0x086D MOONGAUGE(4)`、`0x086E COOLTIME_MOONGAUGE(4)`、
  `0x086F CREATE_BULLETEYE`、`0x0870 SPIDER`（create=265 体 7×i32；**remove 只读 `i32 4, i32 objectId`**，3A4C5E2）、
  `0x0871 THIRD_ONE(5)`、`0x0872/73 POISON(_2)`、`0x0874 POISON_REMOVE`、`0x0875 POISON_ATTACK(12)`、
  `0x0877 TELEPORT(4)`、`0x0879 SET_HP`、`0x087A SET_HP2`、`0x087B STUN(0)`、`0x087C USE_SPECIAL(0)`、
  `0x0881 SPIDER_ATTACK`；`MOB_FORCED_SKILL_ACTION 0x6CA`（`i32 action, u8`；skill action=index+0x1E 变换同 0x6CD）。
- 0x0881 在 Dunkel 场是另一含义（按 fieldType 分派，服务端只在本 boss 场发送）。
- `-Dtms282.bossMech.enabled=false` 关整组翻译。

## 10. 客户端资源（C=MapleStory/Data）
- 机制表：`Etc/BossWill.img`（187KB 级）：`Beholder/Config/{0,1}`（Phase1/Phase2 的 Gen 布点 x/ry +
  Straight 射速 shootAngleRate 300/shootSpeed 100/shootCount 3/shootInterval 1800 + loopDelay 4000 +
  attackIndex 5(P1)/8(P2) + decLunarPower 3）、`Beholder/Image`、`NarrowWeb/Config`、`Web/Image`、
  `ObstacleTrigger/0..3`、`Infection/{Repeat,Attack,AfterHostDead}`、`Will/{0..3}`（段表见 §4）。
- 演出：`Effect/Direction20.img/bossWill/{intro_1,intro_2,intro_3,intro_spine}`（intro_spine 走
  `Effect/_Canvas/Direction20.img` 像素）。
- 音效：`Sound/SoundEff.img/esfera/boss/intro1..3`（服务端引用；客户端节点待真机/后续 dump 核对）。
- BGM：过场 Bgm49/TempleInTheMirror；P1 Bgm49/Diffraction；P2 Bgm49/MirrorCage；P3 Bgm49/BloodCage。
- mob 像素：`Mob/_Canvas/` 有 **8880300/301/302/303/305/306/315/325/332**（hard 系+眼球+镜子）；**8880340s
  （normal 系）与 8880310s 无 _Canvas**——normal 车道 mob 渲染层待迁（可能是 pack 内联画布或复用，勿当缺失误判，
  迁移前用 MsUnpack 解 `Mob/8880340.img` 确认）。
- 任务表现：`Quest/QuestData/37871.img`（入场券任务）。

## 11. 迁移/验证边界（对本项目）
- 本项目已有 P1 蛛网/假人等先例可复用（Lucid 蝴蝶/弹幕管线）；Will 需要的新机制：月光计 UI、P1 双身体门槛 HP、
  上下次元传送（ptup/ptdown）、P2 80002404 状态清除、P3 蛛网 35 张 + 清网读条、242 的 5/7/8 表现。
- **必补**：Easy 车道召唤分支、normal 车道 mob 渲染层确认、掉落表、（若做周次数）P3 击杀 addPartyBoss 已在
  BossHelper 有，但本项目按自身 boss 次数体系落。
- HP 口径：套件硬编码 P1 8.4T/42T、P2 6.3T/31.5T、P3 10.5T/52.5T（N/H），dat 是 1337 占位——迁移照抄套件数值
  并放 boss.properties 可调。
