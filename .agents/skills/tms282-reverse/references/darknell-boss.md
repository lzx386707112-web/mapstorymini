# TMS282 親衛隊長頓凱爾（Darknell）完整逻辑

> 2026-11-05 整理，口径同 `lucid-boss.md`。S=服务端源码、W=wz282、W265=wz265、C=客户端 Data 树。
> **与 Gloom 同款结构性断链：这套 282 服务端没有 Darknell 的副本状态机/召唤逻辑**；差别是 Darknell 有
> 客户端机制 img（BossDunkel.img）和一处 MobHandler 播报。
> 2026-11-05 二次回归已修正：血量（dat 真有值）、掉落表（真有）、BossDunkel 两层差异、
> `firstenter_450012210/600` 注册状态。可用的部分比原判断多：入场链/血量/掉落/击杀计数全在，
> 真正缺的只有**召唤与阶段**。
> 2026-11-05 三次回归（拆 img + 联网核官方机制）解开了三件原「未坐实」的事：
> **AreaWarning 0..9 = 5 精英 boss × 2 招**（§5.1）、**260-1..4 = 召唤两把剑 Ancestion/Ascendion**（§3）、
> **fieldType 204 = 枚举缺口**（§1.1）。剩下的是精英出场节奏与场地伤害数值。

## 1. 组成
- **服务端**：无 `Darknell.java`。相关仅：`BossPartyType.DARKNELL_NORMAL/HARD(27)`、
  `BossConstants.DARKNELL_ENTRANCE_MAP=450012200` + 战斗图/限时（30min 组）、入场 buff **80002646**、
  `BossHelper` 击杀计数与通用通关、`ScriptManagerImpl` 别名（`dunkel_enter→darknell_accept`）。
- **MobHandler:186**：mob 8645009/8645066 移动 action==30/31 时广播
  `WeatherEffNoticeType.BossDarknell(272)`「來試試看吧！這是連星辰都能劈開的究極之劍！」（3s）——
  唯一一段服务端主动表现。
- **客户端**：`Etc/BossDunkel.img`（265 层完整 / 282 overlay 只剩骨架，详见 §5）= **AreaWarning 0..9**
  十组——2026-11-05 已解出语义：**那不是本体技能的预警，是 5 个精英 Boss × 2 招的登场预警**
  （见 §5.1）；`Effect/Direction20.img` 无 bossDunkel 演出分支（无开场 cutscene）。

## 1.1 fieldType 204 = 服务端枚举缺口（本轮坐实）

两张战斗图 `450012210` / `450012600`（W265 XML，282 层没改）都是：

```
fieldType = 204     fieldLimit = 861180
```

而 `enums/FieldType.java` 的枚举**最大只到 `COUNT_OF_FIELDTYPE(152)`**，之后只有为 TMS282 的
`HOUSING(500)` 追加项（源码注释「appended to preserve every existing enum ordinal」）。
`FieldType.getByVal(204)` 因此恒为 **null**，`Field` 拿不到任何场类分支。

**但 204 不是孤例**——实测各神秘河战斗图（W265 XML）：

| 图 | fieldType | 枚举内？ | 有专属 Java 实现？ |
|---|---|---|---|
| Will `450008750` | 182 | ✗ | ✓ `Will.java` / `Tms282WillPhase1` |
| Gloom `450009450` / `450009500` | 262 / 202 | ✗ / ✗ | ✓ `GloomFight` |
| Lucid `450004750` / `450004000` | 147 / 0 | ✓ / ✓ | ✓ |
| 黑魔法师 `450010300` | 199 | ✗ | ✓ |
| Seren `410002160` / `410002260` | 271 / 271 | ✗ / ✗ | ✓ `SerenFight`（按 `fieldId%100` 路由，不用 fieldType） |
| **Darknell `450012210` / `450012600`** | **204** | **✗** | **✗** |

⇒ 282 的场类 id 已远超 Swordie 枚举上限，`getByVal` 对 182/199/202/204/262/271 **全返回 null**。
所以「套件里 Darknell 没状态机」的准确表述是：**别家都写了专属 Java 类绕过 fieldType 分派，
只有 Darknell 两者皆无——既无枚举项，也无专属类**；fieldType 204 本身并不比其他家更特殊。



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
- `getBossFightingFieldId`：NORMAL→450012210、HARD→450012600；无专用 EntryField（无过场图）——
  `getBossEntryFieldId` 的 282 覆盖名单里没有 DARKNELL，直接落到 `getBossWaitingFieldId`=450012200。
- **282 overlay 实际改了 16 张 450012xxx**：020/030/040/100/110/120/130/200/250/350/360/370/430/450/500/650。
  改动集中在入口图、地图名牌、故事图与尾图；**两张战斗图 210/600 只在 265 层**（282 没碰），
  000/010/300..340/400/410/420/440/460/470 也同样只在 265 层。

## 3. mob 模板（TMS 繁中名：親衛隊長頓凱爾）
- **8645009 = normal 本体**、**8645066 = hard 本体**（String/Mob.img 证实；GameConstants intensePowerCrystal
  同号）。lv265、boss=1、firstAttack=1、PDRate/MDRate 300、pad 22000 / mad 24000、pushed 10亿、wp 300。
- **HP：可用，不需要自配（此前「dat=1337 占位」是误读）**：
  - dat282 头偏移 0x14 的 long 实测 **8645009=1,225,996,433 / 8645066=2,451,992,866**（lv265 同步证实
    读的是本文件，不是兜底）；
  - `BossHpConstants.BOSS_CONTEXT_HP[DARKNELL_NORMAL/HARD]` 配了**同值**（`BossHpConstants:222/227`）；
  - 但两者都**不在 `MOB_HP`** ⇒ `getMaxHp(8645009)` = 0，所以只有「带 DARKNELL 组队上下文」的
    `resolveMaxHp` 或 `Field.spawnMob(hp=0)` 的 `isBoss()` 兜底才落血，最终都是 dat 值。
  - 「1337」的真正出处是 `GameConstants.getPartyBossByTemplateID` 的**注释占位**（HP 未知时写 1337，
    Gloom/Will/黑魔法师同一批），它和 mob 血量无关。数值本身是套件合成曲线，非官方血量。
- info/skill（normal/hard 同构，仅 260 等级不同；**wz265 的 8645009/8645066 完全没有 `skill` 节点，
  这 5 条全部只存在于 wz282 overlay**——282 加技的直接证据）：
  - 110-16（action1，skillAfter 1350）——旧通用技能；
  - 170-63（action2，effectAfter 0，skillForbid 5000，afterAttack 1/count 1）、170-77（action4，skillForbid 5000）
    ——170 是「必杀打击系」通用技；
  - **260-1/3（normal）/ 260-2/4（hard）**（action3，skillAfter 900）——282 新技能 id（`MobSkillID` 枚举最大
    只到 252)，走 dat，difficulty 差异仅体现在 260 的 level（1/3 vs 2/4）。
    **2026-11-05 已解出：这两招 = 召唤 Darknell 的两把剑。** `dat282/mobSkills/260-<lv>.dat` 是
    `>H skillId + >H level + WZ 属性体`，体里第一个 string 就是被召唤的 mob 模板：

    | skill/level | 召唤 mob | 名字（String/Mob.img） | 归属 |
    |---|---|---|---|
    | 260-1 | 8645010 | **Ancestion / 安薩斯迪溫** | normal 剑 A |
    | 260-3 | 8645011 | **Ascendion / 奧賽西翁** | normal 剑 B |
    | 260-2 | 8645066 | 親衛隊長頓凱爾（hard 本体） | hard 剑 A 的载体 |
    | 260-4 | 8645067 | 頓凱爾（hard 分身，`notDamaged=1`/`summonType=12`/`hideHP=1`） | hard 剑 B 的载体 |

    两把剑是**真 mob**（8645010：lv260、maxHP 633,547,200、`exp=680638`、`summonType=1`、PDRate 10）；
    客户端有独立像素 `Mob/_Canvas/8645010.img` 1.4 MB / `8645011.img` 2.4 MB。
    `skill/3` 带 `<imgdir name="cooltimeOnSkill"><int name="0" value="4"/>`、`skill/4` 反向指 `3`
    ⇒ 两招互为冷却（用一招同时禁另一招），所以实战里**两把剑不会同时在场**——这一条是从
    `cooltimeOnSkill` 的对称引用推出的，没有实测录像佐证。
    两把剑**没有 `skill` 节点**（`8645010/8645011.xml` 只有 attack/hit/move/stand/range，
    8645010 连 dieF 都没有）⇒ 它们是「会平A的召唤物」，不自己放技。


- dat：`110-16 / 170-63 / 170-77 / 260-1..4` 都在。dat282 的 260 系列有 **`260-1..7`**（1..4 属顿凯尔，
  5..7 属其他 282 boss），dat265 有 `260-1..8`。
- 配角：dat282 有 8645013/8645039/8645044..49/8645064/65/67/68 及 8645058（赫力泰）等——Darknell 战斗的
  召唤物/亲卫队语义待核。客户端 `_Canvas` 只覆盖其中一部分：8645000/001/002/003、010/011/012、
  015/016/017、022/023/024、026、043、048/049、051/052、057/058、060、064、067 有像素；
  013/039/044..047/065/066/068 只有 dat 没有 `_Canvas`（纯逻辑/无独立外观，走 link 或共用本体）。

## 4. 击杀与奖励
- `handleMobKilled`：8645009→`addPartyBoss(DARKNELL_NORMAL)`、8645066→`addPartyBoss(DARKNELL_HARD)`
  （BossHelper:1050/1051，另 :1136/1137 是个人计数分支）
  + 通用通关白名单（:396）→ `completeCompatibleBoss`（QR 34360 `boss=1`、清场演出、掉落窗、退出排程）。
- **掉落：完整奖励表在 `data/resources/tms282/mob_drops.json`**（此前「无条目」是只查了 base 表
  `data/resources/mob_drops.json`，13,974 条里确实没有 8645xxx）——文件是 `[{mobId,...}]` **列表**，
  按 `mobId` 过滤，不是以 mob 号为 key 的字典：
  - Normal 8645009 **11 条**、Hard 8645066 **17 条**，格式 `{mobId,itemId,itemName,chance,minQuant,maxQuant,rule}`；
  - 共有：超級藥水 40/50、咒文的痕跡 160/240、大型BOSS名譽勳章、可疑的附加方塊 6/10、
    藍/紅星星藥水、高級潛在卷、扭曲的時間精髓、頓凱爾的靈魂碎片 2439568、**星耀耳環 1032330 (chance 250)**；
  - Hard 追加：驚訝的混沌卷軸60%、太初之精髓、微弱靈魂艾爾達氣息×12、**指揮官力量耳環 1032316 (40)**、
    **神祕冥界幽靈武器箱 2633914 (100) / 防具箱 2633915 (200)**；
  - 两者都有 **首領怪幣 4009495（Normal ×2 / Hard ×5，chance 10000）**——是项目侧 2026-09-29 的全局拍板。
- 无专属后续任务写入。

## 5. 客户端资源（C）

### 5.1 AreaWarning 0..9 的真实语义（2026-11-05 解开）

**不是 Darknell 本体技能的预警，是 5 个精英 Boss（黑王）各 2 招的登场预警。** 三重证据交叉：

1. **音效名**：`0/1/2/3/4/6/8/9` 用 `eliteBossN`，而 **`5` 和 `7` 用 `eliteBossAir`**（空中版）。
2. **帧组成**：只有 **`1`、`2` 带 `ball`**（飞行投射物），只有 **`4/5/6/7` 带 `effect`**（爆发），
   `0/3/8/9` 只有 `areaWarning`（纯近战/无附特效）。
3. **像素内容**：把 `Etc/_Canvas/BossDunkel.img` 解出来看（BMS key），十组画的都是 **Darknell 剪影**
   （举剑角度不同 + 5 种颜色：金/蓝/绿/紫/红，第二排 5..9 与第一排 0..4 一一同色）。

官方招式表（五家各两招）与推断映射：

| 组 | 音效 | 组成 | 预警时长 | 推断 |
|---|---|---|---|---|
| 0 | eliteBoss0 | 仅 warning | 36f ×90ms = **3240ms** | 金 · 黑騎士 **Raging Blow**（4 连斩 34%+眩晕） |
| 5 | **eliteBossAir** | warning + effect | 32f = **2880ms** | 金 · 黑騎士 **Blue Chains（空中锁链）** 6×10%=60% |
| 1 | eliteBoss1 | warning + hit + ball | 36f = **3240ms** | 蓝 · 瘋狂法師 **Blue Sphere**（飞行蓝球 15/25%） |
| 6 | eliteBoss6 | warning + effect | 36f = **3240ms** | 蓝 · 瘋狂法師 **Ground Blue Chains**（4×9.5%=38%+缓速） |
| 2 | eliteBoss2 | warning + hit + ball | 39f = **3510ms** | 绿 · 兇惡獵手 **Snipe**（箭 40/60%+禁跳） |
| 7 | **eliteBossAir** | warning + effect（0 帧） | 32f = **2880ms** | 绿 · 兇惡獵手 **Blue Chains（空中）** |
| 3 | eliteBoss3 | 仅 warning | 43f = **3870ms** | 紫 · 壞打手（两招之一） |
| 8 | eliteBoss8 | 仅 warning | 32f = **2880ms** | 紫 · 壞打手（另一招） |
| 4 | eliteBoss4 | warning + effect | 38f = **3420ms** | 红 · 狂暴賽博格 **Final Cut**（冲刺 10/19%+致盲） |
| 9 | eliteBoss9 | 仅 warning | 31f = **2790ms** | 红 · 狂暴賽博格 **Phantom Blow**（6×7%=42%） |

可信度分层：**(0,5)/(1,6)/(2,7)/(4,9) 四组是三重证据互锁**（air 音效只落 5/7、ball 只落 1/2、
同色配对唯一）；**只有 3/8（壞打手）无法定序**——Brain Scrambler 的「地面标靶延迟射击」按理会带 effect，
但这两组都是纯 warning，可能标靶走的是独立的场地对象而非 BossDunkel：
- 预警提前量 **2.79–3.87 秒**（最短 9 组 2790ms，最长 3 组 3870ms），与攻略「光柱约 2 秒、斩击 2.5–3 秒、
  剑气约 3 秒」量级一致；Darknell 本体招（大击退/斩击/剑气）另走 mob attack，不在这张表里。
- 精英 Boss 出现数量随血量：Normal >66% 出 1 只、33–66% 出 2 只、<33% 出 3 只；Hard >50% 出 2 只、
  <50% 出 3 只（dexless/wiki 一致）——套件**完全没有这段逻辑**。

### 5.2 img 本体
- 机制表：`Etc/BossDunkel.img`——**两层不是同内容，别再说"同内容"**：
  - wz265 全量 **2915 行**：AreaWarning 0..9 十组、共 **463 张 canvas**，每张只有 1×1 头和
    `_outlink → Etc/_Canvas/BossDunkel.img/AreaWarning/<g>/areaWarning|hit|ball/effect/<n>`，外加
    `origin/z/delay`；`sound` 只有 9 个名字（`eliteBoss0/1/2/3/4/6/8/9` + 两处 `eliteBossAir`，
    **没有 eliteBoss5/7**，其 5、7 组复用 air 音）；
  - wz282 overlay **只有 140 行骨架**：`areaWarning/hit/ball/effect` 全是空 imgdir，只保留 `sound`
    字符串和 `uol` 重定向（如 `4/effect/11→9`、`7/effect/*→../../5/effect/*`）。服务端侧没有任何
    Java 读它（`EtcData` 也没载入），所以 282 profile 下它只是「音效名表」。
  - 像素在客户端：`Etc/BossDunkel.img` 57 KB + **`Etc/_Canvas/BossDunkel.img` 10.6 MB**（outlink 的落点，
    服务端导出树里没有）。
- `Effect/Direction20.img` 无 bossDunkel 演出分支（无开场 cutscene）。
- mob 像素：**`_Canvas` 只存在于客户端 C**——`Data/Mob/_Canvas/8645009.img` 26 MB ✓（hard 共用）；
  服务端 `wz265/Mob.wz/` 根本没有 `_Canvas` 目录，按「WZ 里有 _Canvas」去找会一律落空。
  另有 8645000-03/10-12/15-17/22-24/26/43/48/49/51/52/57/58/60/64/67 等配角系。
- 地图：C `Data/Map/Map/Map4/450012000..650.img` 全在（000/010/020/030/040/100/110/120/130/200/210/250/300..380/
  400..470/500/600/650）。
- BGM：入口/战斗 SubterminalPoint，450012650 BlackFury。

## 6. 迁移/验证边界（对本项目）
- **迁移 = 自建召唤与阶段**（fieldType 204 场类逻辑 + `firstenter_450012210/600`）。
  ⚠️ 修正常见误写：`firstenter_450012210/600` **没有注册**——`CustomFieldScripts` 的枚举与 282 switch 里
  都没有任何 4500 段条目（有 401060300=Easy Magnus，别把 401060300 看成 410002060 那一类）。
  因此 282 下 `execFirstUserEnterScript` 会正常派发 XML 的 `onFirstUserEnter=dunkel_bosspre`，
  而那个脚本同样缺体 ⇒ **进场不会刷任何 mob**。要跑起来只能自己补：`dunkel_bosspre` 或
  `firstenter_450012210/600`（后者还得先进 `CustomFieldScripts` 枚举，否则永远不派发）。
- **可以照抄**：击杀计数、通用通关、30min 限时、入场链、action 30/31 播报、dat 血量。
- **本轮已收敛（不再是未知）**：
  - 260-1..4 = 召唤 Ancestion / Ascendion 两把剑（§3），`MobSkillID` 无 260 ⇒ 套件里只会广播动作、
    不结算召唤，要落地必须在 `MobSkill` 补 `case DarknellSummonSwords`；
  - AreaWarning 0..9 = 5 精英 × 2 招（§5.1），预警 2790–3870ms；
  - fieldType 204 = **枚举缺口**，不是「场类行为未逆向」（§1.1）。
- **仍需拍板**：
  1. 精英 Boss 的出场节奏与数量分档（Normal >66% 1 只 / 33–66% 2 只 / <33% 3 只；Hard >50% 2 只 / <50% 3 只）
     ——套件零实现，数据只能来自攻略，客户端 img 里没有；
  2. 「两把剑」在 normal/hard 的语义不对称：normal 召 **8645010/8645011**（真剑，带 exp），
     hard 的 260-2/260-4 却指向 **8645066（本体）/8645067（隐形分身 `notDamaged=1`）**——
     需录像确认 hard 是「剑换皮成同 id」还是「确实不召唤剑」；
  3. 场地伤害：光柱 35/40%、陨石 30/40%（三色分别眩晕/致盲/封印）、堕落勇士 5%（4–5s 无敌盾）
     ——全部只在攻略/wiki 层，WZ 里没有伤害数值，要做得自己配。
- 官方血量参照（dat 的合成值远低于官方，**迁移前必须决定用哪套**）：
  Normal **26,000,000,000,000**（26 兆）/ Hard **160,000,000,000,000**（160 兆）；
  dat282 只有 1.23e9 / 2.45e9，差 4 个数量级。
- BossDunkel.img（265 层 + `Etc/_Canvas`）是唯一确定的客户端机制数据，迁移表现层时按
  areaWarning/hit/ball/effect/sound 五类落；282 overlay 那份骨架只能用来对音效名。

