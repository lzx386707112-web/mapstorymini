# TMS282 親衛隊長頓凱爾（Darknell）完整逻辑

> 2026-11-05 整理，口径同 `lucid-boss.md`。S=服务端源码、W=wz282、W265=wz265、C=客户端 Data 树。
> **与 Gloom 同款结构性断链：这套 282 服务端没有 Darknell 的副本状态机/召唤逻辑**；差别是 Darknell 有
> 客户端机制 img（BossDunkel.img）和一处 MobHandler 播报。

## 1. 组成
- **服务端**：无 `Darknell.java`。相关仅：`BossPartyType.DARKNELL_NORMAL/HARD(27)`、
  `BossConstants.DARKNELL_ENTRANCE_MAP=450012200` + 战斗图/限时（30min 组）、入场 buff **80002646**、
  `BossHelper` 击杀计数与通用通关、`ScriptManagerImpl` 别名（`dunkel_enter→darknell_accept`）。
- **MobHandler:186**：mob 8645009/8645066 移动 action==30/31 时广播
  `WeatherEffNoticeType.BossDarknell(272)`「來試試看吧！這是連星辰都能劈開的究極之劍！」（3s）——
  唯一一段服务端主动表现。
- **客户端**：`Etc/BossDunkel.img`（两层 WZ 同有）= **AreaWarning 0..9** 十组攻击预警表
  （每组 `areaWarning` + 可选 `hit/ball/effect/sound`）——Darknell 的范围技预警贴图/音效参数总表；
  `Effect/Direction20.img` 无 bossDunkel 演出分支（无开场 cutscene）。

## 2. 注册与入场
- `DARKNELL_NORMAL(27,'Darknell',255,Normal,35815,OnceInSevendays)`、`DARKNELL_HARD(27,255,Hard,…)`；
  限时 30min；入场检查链与 Lucid 同一条通用链；**与黑魔法师共享 preQuest 35815**（同一入场券任务，
  Black Mage 是 35815 的后续挑战）。
- 地图（W265，282 只改了外围）：
  - 入口 `450012200`（W282 有改动版：Bgm50/SubterminalPoint、mapMark Limen、lvLimit 255、
    `onUserEnter=enter_450012200`；portal east00=`east_450012200`、in00=**dunkel_enter**（别名→darknell_accept）、
    west00=`BPReturn_dunkel`（别名→compatible_boss_exit））。
  - normal 战斗图 `450012210`、hard `450012600`：fieldType **204**、fieldScript=`dunkel`、
    `onFirstUserEnter=dunkel_bosspre`、`onUserEnter=dunkel_timeRecord`、portal pt_GC_out
    （=pt_gc_out，别名表归 `compatible_boss_exit`；GC=Guard Captain）。
  - 450012650（W282 改动版）：bgm Bgm50/BlackFury、`onFirstUserEnter=enter_450012650`——观战/后日谈图。
  - 450012500 = 黑魔法师入口（同一张图在 282 manifest 也有改动，别与 Darknell 混）。
- `getBossFightingFieldId`：NORMAL→450012210、HARD→450012600；无专用 EntryField（无过场图）。

## 3. mob 模板（TMS 繁中名：親衛隊長頓凱爾）
- **8645009 = normal 本体**、**8645066 = hard 本体**（String/Mob.img 证实；GameConstants intensePowerCrystal
  同号）。lv265、boss=1、firstAttack=1、PDRate/MDRate 300、pushed 10亿、wp 300。
- **HP：dat maxHP=1337 占位 + 无服务端覆盖 = 套件实际不可用**，迁移必须自配。
- info/skill（normal/hard 同构，仅 260 等级不同）：
  - 110-16（action1，skillAfter 1350）——旧通用技能；
  - 170-63（action2，effectAfter 0，skillForbid 5000，afterAttack 1/count 1）、170-77（action4，skillForbid 5000）
    ——170 是「必杀打击系」通用技；
  - **260-1/3（normal）/ 260-2/4（hard）**（action3，skillAfter 900）——282 新技能 id（MobSkillID 枚举无，
    走 dat），difficulty 差异体现在 260 的等级。
- dat：110-16 / 170-63 / 170-77 / 260-1..4 两层全有。
- 配角：dat282 8645013/8645039/8645044..49/8645064/65/67/68 及 8645058（赫力泰）等——Darknell 战斗的
  召唤物/亲卫队语义待核（客户端 _Canvas 8645000-8645067 大量存在）。

## 4. 击杀与奖励
- `handleMobKilled`：8645009→`addPartyBoss(DARKNELL_NORMAL)`、8645066→`addPartyBoss(DARKNELL_HARD)`（:1050）
  + 通用通关白名单（:396）→ `completeCompatibleBoss`（QR 34360 `boss=1`、清场演出、掉落窗、退出排程）。
- 掉落：`mob_drops.json` 无 8645009/8645066 条目。
- 无专属后续任务写入。

## 5. 客户端资源（C）
- 机制表：`Etc/BossDunkel.img`（两层同内容，套件 XML 265=282）：AreaWarning 0..9。
- mob 像素：`Mob/_Canvas/8645009.img` 本体存在 ✓（另有 8645000-03/10-12/15-17/22-24/26/43/48/49/51/52/57/58/
  60/64/67 等配角系）。
- 地图：C `Data/Map/Map/Map4/450012000..650.img` 全在（000/010/020/030/040/100/110/120/130/200/210/250/300..380/
  400..470/500/600/650）。
- BGM：入口/战斗 SubterminalPoint，450012650 BlackFury。
- 演出：Direction20 无 bossDunkel；黑魔法师开场（bossBlackMage）是独立分支。

## 6. 迁移/验证边界（对本项目）
- **迁移 = 自建召唤与阶段**（fieldType 204 场类逻辑 + `firstenter_450012210/600`，脚本名已注册、脚本体缺失）。
  可照抄：击杀计数、通用通关、30min、入场链、action 30/31 播报。
- **必拍板**：HP、阶段划分（fieldType 204 的场类行为未逆向——套件无 RE 记录，这与 Will/Seren 不同）、
  260-1..4 的表现（dat 有数值无服务端分支）、AreaWarning 0..9 与实际技的对应。
- BossDunkel.img 是唯一确定的客户端机制数据，迁移表现层时按 areaWarning/hit/ball/effect/sound 五类落。
