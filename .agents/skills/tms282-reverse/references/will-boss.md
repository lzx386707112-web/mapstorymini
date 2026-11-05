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
    `onUserEnter=enter_450007240`、portal pt00 script=`BPReturn_Will`）。**回归修正**：282 别名表
    （`ScriptManagerImpl.resolveScriptFile`）只有 `bpreturn_dusk/dunkel/slime/seren/kalos/karing/bm`，
    **没有 `bpreturn_will`**——Will 的退出 portal 不会落到 `compatible_boss_exit`，套件又无同名脚本体，
    属退出链缺口（其余神秘河 boss 都有别名）。
  - 战斗图：E/N `450008750/850/950`、Hard `450008150/250/350`（fieldType **182/183/184**，
    `onFirstUserEnter=will_phase1pre/2pre/3pre`、fieldScript=`bosswill`）。
  - 过场（intro）图：E/N `450008700/800/900`、Hard `450008100/200/300`（`onUserEnter=will_direction1/2/3`）。
    **282 的 getBossEntryFieldId：E/N→450008700、Hard→450008100**（入场先进过场图，播完 +50 进战斗图）。
  - `FIGHT_FIELD_TO_ENTRANCE` 覆盖 450008100/200/250/300/350/400 + 450008700/800/850/900/950 → 入口图。
- 入场 buff `80002419`；入场检查链与 Lucid 同一条通用链（ScriptHandler viaBossUI：等级/前置/周次数/仅队长/
  `warpInstanceIn`+`setInstanceTime`，QR 102401 记 mapR/order/diff）。
- **【套件现状】Easy 没有独立车道**：`BossConstants.getBossEntryFieldId(WILL_EASY)=450008700`、
  `getBossFightingFieldId(WILL_EASY)=450008750`，与 Normal **完全相同**；`Will.spawnBoss` 只按地图判断
  （450008750 → `moonLightTime=1000` → `isNormal`），所以 Easy 实际召的是 Normal 车道的 8880340 系、
  8.4T 血，并不是"无召唤"。WZ 的段0/段3 两套 easy（8880310/8880360 系）在套件里完全没有被引用。
  迁移若开放三难度，要么把 Easy 隐藏，要么自补 easy 车道与血量（2026-11-05 回归修正）。
- **Easy 是官方正式难度（2026-11-05 联网坐实），不是套件臆造**：
  Story 210 亿 / **Easy 16.5 兆**（P1 5.4T＝每维度 2.7T、P2 4.1614T、P3 6.94T）/
  Normal 25.2 兆（P1 8.4T＝每维度 4.2T、P2 6.3T、P3 10.5T）/ Hard 126 兆（P1 42T、P2 31.5T、P3 52.5T）。
  ARC 要求 Easy 560 / Normal·Hard 760，入场等级 Easy 235 / 其余 250 —— 与 WZ 完全对得上：
  **8880360 系 lv235 = Easy、8880340 系 lv250 = Normal、8880300 系 lv250 = Hard**，三套各 5 个本体。
  两条硬证：`GameConstants:1869` 把 **8880360 挂成 Will 的強力結晶载体 9210053 / 38,255,000 meso**
  （全 Will 最低价 ⇒ Easy）；`dat282` P3 本体 **8880302 = 913,438,524 > 8880342 = 456,719,262**
  ⇒ 8880300 系血更高 = Hard。
- **⚠️ 套件内部对 8880342 的难度归属自相矛盾**：`GameConstants:1716` 写 `8880342 -> WILL_HARD`，
  而 `BossHelper:1040` 写 `case 8880342 -> addPartyBoss(WILL_NORMAL)`。按上面的血量证据
  **BossHelper 对（8880342=Normal）、GameConstants 标反了**；同理 `GameConstants:1715` 把 `8880302`
  标成 WILL_NORMAL 也是错的（应为 Hard）。这两行还都挂着 `// HP: 1337` 占位注释——同一批不可信位置。


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
- **P3（450008350/950）**：先清场内 SpiderWeb 再重铺 **35 张**（`SpiderWeb.load` 预置 **67** 个点：
  pattern0 30 + pattern1 28 + pattern2 9，`WillPacket.spider` 尺寸 100/160/270；技能 13 的上限也是 67，
  number 0..66），normal 8880342（-4,25，10.5万亿）+ 假人 8880354/8880358（352/252,281）；hard 8880302（52.5万亿）+ 8880324/328。
- **WZ 段映射**（BossWill.img/Will，`info` 字段）：段0='easy'（8880310/11/12 投射在鏡面上的威爾 + 假人 8880332-38）、
  段1='normal'（8880340/41/42）、段2='hard'（8880300/01/02）、段3='easy'（8880360 系，另一套 easy 变体）。
  套件只用段1/段2。mob dat 的 maxHP 对 P1/P2 是 1337 占位（P3 8880342/8880302 的 dat 已写成
  456,719,262/913,438,524），实际 HP 全部由 Will.java 显式传入（上值）。

## 5. P1 门槛机制（Tms282WillPhase1，282 专属）
- 265 原版只认载体死（但载体只被 242/5 打，`onlyFsm` 客户端永不施放，且 265 HP 门拿当前 HP 比较错）——真服 2026-09-30
  复现「下身打死不了、永不进 P2」。**282 由 Tms282WillPhase1 接管**：
  - 门槛表 `[666, 333, 3]`（千分比 = 66.6% / 33.3% / 0.3%）；
  - 每个身体被打时 HP 卡在下一个门槛、门槛处无敌，直到**另一个身体也到同一门槛**才放行（放行时播 242/8 眩晕）；
  - 载体 HP 镜像两个身体的较大值；`WillPacket.setHp(门槛表, field, 载体, +3, +4)` 推送（300ms 节流）；
  - 双身体都到 3‰ → P1 结束：清场、2.5s 后全员 warp `field+50`（进 P2 过场图）。
- 265 profile 不走这条（回退旧逻辑）。
- **P2 另有 265 遗留门槛（282 未接管，现行有效）**：`Mob.damage` 对 8880301/8880341 判 **50% / 0.3%**
  （`WillHPlist=[500,3]`，由 `Field.spawnMob` 初始化）：到 50% 卡血并放 242/7（`useSpecialSkill` 置 true，
  120s 内不重复）；到 0.3% 再放一次 242/7。这段没有 Tms282 版本，迁移要一并复刻。

## 6. 月光计（moon gauge）与上行包
- `setMoonGauge(max, min)`：P1 45 / P2 50 / P3 25（min=消耗量）。`addMoonGauge(值)`。
  计时器每 1000ms +2（**hard 2000ms**，按 `BossHpConstants.getCurrentBossParty==WILL_HARD`），上限 100。
- **上行 WILL_USE_MOON_GAUGE**（MobHandler:1013），按图分派：
  - P1（450008150/750）：gauge-45，广播 `WILL_TELEPORT` + portalTeleport（**y > -1000 → ptup，否则 ptdown**，
    即上下次元切换）+ 技能特效 80002421；
  - P2（450008250/850）：先移除临时状态 80002404，gauge-50，`cooldownMoonGauge(7000)`，7s 后播 80002404 特效；
  - P3（450008350/950）：gauge-5，`setClearSpiderWeb(2)` 持续 5s（期间碰网即清除），cooldown 5000。
- **上行 WILL_BEHOLDER_EYES_HIT**：命中眼球 gauge-10（旧码 `min(0,..)` 会清空/负值，bossmech2 修为 max(0,..)）。
- **上行 WILL_SPIDER_WEB_HIT**：碰蛛网且无 clearSpiderWeb → 扣 10% maxHP，载荷 isSeal=1 时上 Seal(slv40,5s)；
  NotDamaged/已死跳过。
- **消耗**：月光技能本身会扣 45/50/5（见上），除此之外服务端没有"黑暗"判定或其他消费点；
  迁移按 45/50/5 实现即可，不要臆造黑暗机制。（原文写"只产出不消费"是错的。）
- **回归补充**：`WILL_USE_MOON_GAUGE` 不校验 gauge 是否够扣（直接 `max(0, …)`），客户端是否置灰由 UI 决定。

## 7. 技能（MobSkillID.Will=242，MobSkill.java:1129）
模板（8880340/8880300 的 info/skill）：242-5（onlyFsm priority1）、242-15、242-4。slv 分支：
- **1/2/3 蛛雨**：假人/载体模板换算 id（8880321/22-18、8880323/24-22、8880353/54-12、其余-8），
  rand 17..23 段 `(i, 1800*(1+i/6), -650+130*rand(1,9))` 发 `WILL_SPIDER_ATTACK`（0x0881）。
- **4 P1 灭团读条**：播报「攻擊眼球以改變月光方向，並利用它生成月光屏障！」+ `WILL_USE_SPECIAL`；
  意图是全场 mob attackBlock（列表是 0..8，不是 0..9）；**源码 bug**：`for (Mob m : field.getMobs())` 里广播的
  是 `MobPool.mobAttackBlock(mob, …)`，编码的是施法者 objectId（MobPool:609），实际只反复封锁载体自己。
  对每人随机 type 1/2（蓝/紫，按队伍人数对半）发 spiderAttack；清 242 AA；
  召两只 8880305（眼球，0,-2020 与 0,159）；30s 后：清眼球，收集 242 本体 AA + 玩家 400031039/400031040 AA
  内的玩家 → 依次穿保护判（圣魔盾/阿修罗/风灵结界/无敌系）后 **die()**；若 `useSpecialSkill` 则按 HP% 重设
  WillHPlist + 发 setHp + 242/8 眩晕；再 11s 后 242/15（即 4 之后约 41s）。
- **5 P2 地面地板**：type=rand(0/1)，`WILL_SPIDER_ATTACK` slv5 内嵌两块地板区域（type0: y -455..160 或 -2378..-2019，
  见 WillPacket.spiderAttack case5 的坐标），3s 后结算：先把载体与 +3/+4 身体的 HP 同步成两者较大值（仅载体为
  8880300/8880340 时），再对 AA（242 本体 + 400031039/400031040）内玩家按 type 落点扣 100% maxHP
  （实际公式 `heal(-maxHP*(100-IndieDamReduceR)/100)`，type0 打 y>-455&&y<300 层，type1 打 y -2500..-1800 层），
  1s 后按 HP% 触发 242/4（分档 66.6/33.3/0.3）。
- **7 谎言之镜（P2/P3）**：播报「謊言之鏡會反轉攻擊…」+ USE_SPECIAL + attackBlock，5s 后 242/14。
- **8 眩晕（门槛放行）**：播报「機會來了！趁威爾露出破綻時攻擊他！」+ `WILL_STUN`（0x87B），清 242 AA，
  载体时对 +3/+4 身体各发 `MOB_FORCED_SKILL_ACTION`(0x6CA) action=3；非载体（下半身/上半身自己放）改为
  对自己发 action=2。10s 后全场解除 attackBlock。
- **9 传染炸弹**：随机一名玩家上 `BossWill_Infection`（tOption 7s），广播 `WILL_POISON_2`，7s 后
  `WILL_POISON_REMOVE`。
- **10/11**：`WILL_SPIDER_ATTACK` slv10/11、type=1（无坐标表），P2/P3 的蛛腿攻击。
- **12 眼球/弹幕**：按假人模板换算本体 id（325/326→y<0?304:303 或 344:343；327/328→301/341 或 302/342；
  355/356→343/344），再按 y 层与随机分支发 `WILL_CREATE_BULLETEYE`（0x86F：type0 布点 + type1 直线/扇形参数）。
- **13 补网**：按 number 0..66 找第一个空缺补一张 SpiderWeb，总数上限 67。
- **14（P2 专属，7 延迟 5s 触发）**：`specialPattern=true`；`doSpider` 对最大伤害玩家发 18 段蛛腿
  （slv14，type 随机 0/1），11s 后反向 type 再发一轮（solo 追加 9 段，共 27）；30s 后若 specialPattern 仍在
  → 242/8 + 按 HP% 重设 `WillHPlist`（50%/0.3%，SET_HP2），否则解除 attackBlock。
- **15（4 的尾段）**：3s 后对每人 50% `ptup` / 50% `ptdown` 传送 + `WILL_SPIDER_ATTACK` slv15 随机 type。
- dat265/dat282 242-1..17 全量齐。

## 8. 击杀与奖励
- **P1 载体死（8880340/8880300）**：清场 + 全队 warp `+50`（282 +50 / 265 +100，BossHelper:437）。
  ⚠️ 282 下 Tms282WillPhase1 根本不让身体死（onBodyDamage 返回 true 吞伤害），载体死亡路径仅 265 生效。
- **P2 载体死（8880341/8880301）**：同上 warp +50。
- **P3 本体死（8880342/8880302）**：清场 + 清 SpiderWeb + 走通用通关 `completeCompatibleBoss`
  （QR 34360 `boss=1`、清场演出、掉落窗、退出排程）；同时 addPartyBoss：8880342→WILL_NORMAL、8880302→WILL_HARD。
- **掉落：有（2026-11-05 回归修正）**——掉落有**两个文件**，282 boss 只在 overlay 那个里：
  `data/resources/mob_drops.json`（13,974 条，265 生成表，**8880300s/8880340s 一个都没有**）vs
  `data/resources/tms282/mob_drops.json`（4,434 条，282 overlay）。两者都是
  `[{mobId,itemId,itemName,chance,minQuant,maxQuant,rule}]` 的**列表**，不是以 mob 号为 key 的字典
  （按 `"8880342"` 这种字符串 key 查字典也只会一无所获）。`DropData.load` 的合并语义是**按 mob 整体替换**：
  overlay 里出现过的 mobId，base 的同 mob 行会被整批删掉（不是追加）⇒ 这四个 88803xx 只有 overlay 那批生效。
  - **8880342（normal P3）13 条**：超級藥水×40、咒文的痕跡×160、大型BOSS名譽勳章、可疑的附加方塊×5、
    藍/紅星星藥水、高級潛在卷、混沌之碎片、扭曲的時間精髓、威爾之魂碎片 2438396、克伯威水滴石 4001890、
    **暮光印記 1012757 (chance 250)**、首領怪幣×2；
  - **8880302（hard P3）20 条**：同上但量更大（藥水×50、痕跡×240、方塊×9），hard 追加
    驚訝的混沌卷軸60%、微弱靈魂艾爾達氣息×5、鏡子世界的核心寶石 2438412、
    **受詛咒的魔導書選擇箱 2633926 (40)**、神祕冥界幽靈武器箱 2633914 (100) / 防具箱 2633915 (200)、
    **威爾機器人 1662148 (100)**、首領怪幣×5；
  - P1/P2 载体（8880300/301/340/341）确实没有条目，只有 P3 本体有。
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
- 机制表：`Etc/BossWill.img`（282 overlay 791 行；265 全量 1965 行，多出的是 `_Canvas` outlink 节点）：
  `Beholder/Config/{0,1}`（每个 Config 下都有 Phase1/2/3 的 Gen 布点 x/ry + Straight 参数：
  shootAngleRate 300、shootSpeed 100、shootInterval 1800、loopDelay 4000、decLunarPower 3；
  shootCount 与 attackIndex 随段变化——P1/P2 为 3 发 + attackIndex 5/8，P3 与 Config1 部分段为 5 发）、
  `Beholder/Image`、`NarrowWeb/Config`、`Web/Image`、`ObstacleTrigger/0..3`、
  `Infection/{Repeat,Attack,AfterHostDead}`、`Will/{0..3}`（段表见 §4）。
- 演出：`Effect/Direction20.img/bossWill/{intro_1,intro_2,intro_3,intro_spine}`（intro_spine 走
  `Effect/_Canvas/Direction20.img` 像素）。
- 音效：`Sound/SoundEff.img/esfera/boss/intro1..3`（服务端引用；客户端节点待真机/后续 dump 核对）。
- BGM：过场 Bgm49/TempleInTheMirror；P1 Bgm49/Diffraction；P2 Bgm49/MirrorCage；P3 Bgm49/BloodCage。
- mob 像素：`Mob/_Canvas/` 有 **8880300/301/302/303/305/306/315/325/332**（hard 系+眼球+镜子）；
  **8880340s（normal 系）与 8880310-313 在 `_Canvas` 下没有独立条目**，但 2026-11-05 已用 `MsUnpack` 确认
  `Packs/Mob_00000.ms` 里都有完整 `Mob/888xxxx.img`（8880340 仅 1.4KB 载体桩，8880341 53KB、8880342 44KB、
  8880343/344 各 31.6KB、8880351 1.9KB），**不是缺资源**，迁移时从 pack 解包即可（8880315 另有 _Canvas）。
- 任务表现：`Quest/QuestData/37871.img`（入场券任务）。

## 11. 迁移/验证边界（对本项目）
- 本项目已有 P1 蛛网/假人等先例可复用（Lucid 蝴蝶/弹幕管线）；Will 需要的新机制：月光计 UI、P1 双身体门槛 HP、
  上下次元传送（ptup/ptdown）、P2 80002404 状态清除、P3 蛛网 35 张 + 清网读条、242 的 5/7/8/9/12/13/14/15 表现。
- **必补**：`bpreturn_will` 别名/退出脚本（Easy 那条已从「待定」降级为「已知」：官方有 Easy，
  血量 16.5 兆、mob 用 8880360 系，套件没接——要做就照 §2 的三套归属补车道与 `Will.spawnBoss` 的
  easy 分支）、（若做周次数）P3 击杀 addPartyBoss 已在 BossHelper 有，但本项目按自身 boss 次数体系落。
  掉落表**不用补**（`data/resources/tms282/mob_drops.json` 已有 P3 两档，见 §8）。
- HP 口径：套件硬编码 P1 8.4T/42T、P2 6.3T/31.5T、P3 10.5T/52.5T（N/H），P1/P2 dat 是 1337 占位、
  P3 dat 是 456,719,262/913,438,524——迁移照抄 Will.java 的显式数值并放 boss.properties 可调。
