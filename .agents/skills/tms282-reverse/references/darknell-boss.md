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
> 2026-11-05 四次回归：按「客户端优先」原则（§0）把上面两项也定案了——精英出场档位来自官方、
> 场地伤害与堕落勇士已用 **dat 血量反查**锁死（§3），**全部「待拍板」已清零**，只剩可实现的落地口径（§6）。

## 0. 冲突裁决原则（2026-11-05 拍板，适用全部神秘河 boss）

四层证据，从上往下取第一条能定案的；**冲突时上面覆盖下面**：

1. **客户端表现层**——`MapleStory/Data` 的 img/pack/`_Canvas` 像素。动作、站位、帧延迟以它为准。
2. **客户端数据层**——`TMS282.5/data` 的 `wz265` 底 + `wz282` overlay、`dat282/*.dat`。
   数值、mob id、技能归属、召唤目标以它为准（这是"客户端优先"最有用的一层，因为它可解析）。
3. **官方机制**——wiki/namu/官方攻略。只用来补客户端里没有的**可玩性规则**（伤害百分比、节奏、CD、失败条件）。
4. **自建**——前三层都没有时自己定一套，但必须在本文件写明常量名，代码里同名。

套件里的注释与合成值（`// HP: 1337`、`2.1e9`、`SEGMENT_DEGREES{108,72,108,72}`、
`SEREN_EXTREME_FIGHT_MAP=410002080`）全部属于**要被覆盖的一方**，不是证据。

已生效的三条裁决：
- **Will Easy**：套件把 Easy 映射到 Normal 的图与 mob；客户端有独立 8880360 系（lv235）⇒ 走 8880360。
- **Seren 时钟**：套件 120s / 108-72-108-72 是自选占位；官方 420s ⇒ 走 120/130/40/130。
- **8880342 难度归属**：`GameConstants` 标 HARD、`BossHelper` 标 NORMAL；dat 8880302(913M) > 8880342(456M)
  ⇒ 判 **NORMAL**，`GameConstants` 标注错误。

**血量是唯一要解释的例外**：`dat282` 的 `maxHP` 是「服务端没下发时的兜底显示值」，服务端 spawn 会带自己的
hp/maxHp 覆盖它。所以"客户端优先"在血量上的含义是**血条要能画出完整数值**，不是"拿 dat 的 12 亿去打"——
真实战斗血量取官方值（Darknell 26T/160T、Gloom 26T/114.6T、Seren 208T/483T）。

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

| 图 | fieldType | 枚举内？ | 有专属 Java 类？ |
|---|---|---|---|
| Will `450008750` | 182 | ✗ | ✓ `Will.java` + `Tms282WillPhase1` |
| Lucid `450004750` / `450004000` | 147 / 0 | ✗（147 是空号）/ ✓ | ✓ `Lucid.java` |
| Gloom `450009450` / `450009500` | 262 / 202 | ✗ / ✗ | **✗ 无** |
| 黑魔法师 `450010300` | 199 | ✗ | **✗ 无**（全源码只在 `GameConstants` 出现 8880532） |
| Seren `410002160` / `410002260` | 271 / 271 | ✗ / ✗ | ✓ `SerenFight`（按 `fieldId%100` 路由，不用 fieldType） |
| **Darknell `450012210` / `450012600`** | **204** | **✗** | **✗ 无** |

`world/boss/` 下只有 `Will/Seren/SerenFight/Lucid/Tms282WillPhase1` 这五个与神秘河相关，
**没有 `GloomFight`、没有 `BlackMage`、没有 `Darknell`**（实测 `ls world/boss/` 核实过）。

⇒ 结论要这么写：`FieldType.getByVal` 对 182/199/202/204/262/271 **全返回 null**（只有 `0`=DEFAULT 命中），
**按 fieldType 分派在神秘河整体失效**；Will/Lucid/Seren 各写了专属类绕过它，
**Gloom/黑魔法师/Darknell 三者既无枚举项也无专属类**，只剩 `BossHelper` 的击杀计数 + 通用通关。
所以 Darknell 并不比 Gloom 更残缺，它多出来的只有 `BossDunkel.img` 与 MobHandler 的 action 30/31 播报。



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
    **这两招 = 召唤 Darknell 的「剑」。** `dat282/mobSkills/<id>-<lv>.dat` 的二进制布局（按
    `SkillData.loadMobSkillFromFile` 逐字段核对）：

    ```
    >H id | >H level | 6 × ( bool hasPos ? >ii : 空 ) | >H n | n × ( u8 statOrdinal | >H len | utf8 value )
    | >H m | m × >i
    ```

    第一个 stat 的 ordinal 是 **28 = `MobSkillStat.v`**（枚举里的通用兜底槽），它装的正是 mob 模板 id。
    **决定性交叉验证**：263（正午）/264（夕阳）的 `v` 槽**全部为空**，只有 260 有值 ⇒
    **260 是「召唤/换形」型技能**，263/264 不是。所以这个 id 的含义是确定的，不是猜的。

    | skill/level | `v` = 召唤 mob | 名字 | 实测 |
    |---|---|---|---|
    | 260-1 | 8645010 | **安薩斯迪溫**（Ancestion） | lv260、maxHP **633,547,200**、`exp=680638` |
    | 260-3 | 8645011 | **奧賽西翁**（Ascendion） | lv262、maxHP **655,470,200**、`exp=700242` |
    | 260-2 | 8645066 | 親衛隊長頓凱爾（hard 本体，`link=8645009`） | lv265、`boss=1` |
    | 260-4 | 8645067 | 頓凱爾（隐形，`summonType=12`/`notDamaged=1`/`hideHP=1`） | lv275 |

    **hard 不是"没做剑"，是"用本体外观当剑"**：260-2 召本体模板、260-4 召一个不可见的判定分身。
    以前把它记成「dat 偷懒/语义待录像是错的」——`v` 槽这套解析规则在 Seren 的 260-5/6/7 上同时成立
    （`v` = 8880609/8880639/8880659，正好是午夜身体），两套 boss 用同一格式，不可能一边是笔误。
    另有一个**未被 282 引用的强化剑** `8645070 強力的奧賽西翁`（lv262、maxHP **1,310,940,400**（=8645011 的 2 倍）、
    `exp=840290`）——想要 hard 用真剑而不是本体外观时，这是唯一有数值支撑的替代品。
    两把剑**没有 `skill` 节点**（8645010 连 dieF 都没有）⇒ 纯平A召唤物。
    `skill/3` 的 `<cooltimeOnSkill><int name="0" value="4"/>` 与 `skill/4` 的反向引用互指 ⇒
    两招互为冷却，**实战里不会同时在场**（这条由 WZ 引用对称推出，无录像佐证，但可直接照抄实现）。


- **堕落勇士（Corrupted Warrior）= 8645003（N）/ 8645069（H）**——用 dat 血量反查锁死，与攻略完全吻合：

  | mob | lv | maxHP | 依据 |
  |---|---|---|---|
  | 8645003 墮落勇士 | 265 | **10,000,000,000** | 攻略「Normal 100 亿」 |
  | 8645069 墮落勇士 | 265 | **30,000,000,000** | 攻略「Hard 300 亿」，`link=8645003` 共用外观 |
  | 8645033 墮落勇士 | 262 | 655,470,200 | 故事模式档位（与 8645039 弱化本体同档） |

  **它没有挂在任何 `skill` 节点上**（Darknell 的 skill 只有 110/170/260）⇒ 只能由 **fieldType 204 的场类
  定时召唤**：每批 4 只、出生 4–5s 无敌、touch 5%（`maxHP` 写 `10^10`/`3·10^10`，无敌时长官方 wiki=4s、
  dexless=5s，按 **5s** 实现即可）。这是"场类逻辑"里最实的一块，不是黑盒。
- `8645070 強力的奧賽西翁`（lv262、maxHP 1,310,940,400、`exp=840290`）= 强化剑，282 的 260 **没有引用它**，
  属于可选的 hard 真剑替代（见上）。
- dat：`110-16 / 170-63 / 170-77 / 260-1..4` 都在。dat282 的 260 系列有 **`260-1..7`**（1..4 属顿凯尔，
  5..7 属 Seren 午夜），dat265 有 `260-1..8`。
- **配角清单要拆开看——原先把黑魔法师/Black Heaven 的 mob 混进来了**（否则会照它们写召唤逻辑）：
  - Darknell 系：`8645039 弱化的親衛隊長頓凱爾`（lv262、`link=8645009`、PDRate 50）= **故事模式本体**；
    `8645067 / 8645068 頓凱爾`（lv275、`summonType=12`、`notDamaged=1`、`hideHP=1`）= 隐形判定分身；
    `8645014 / 8645030 / 8645032 / 8645040` 同名「安薩斯迪溫/奧賽西翁」= 旧版剑的重复模板（可忽略）。
  - **不是 Darknell 的**：`8645013 / 8645044 光之巨人`（Black Heaven）、`8645045..049`+`8645064/65`
    「弱化的創造的伊恩 / 破壞的雅達巴特 / 黑魔法師」（黑魔法师 P4 战）、`8645058 赫力泰`。
    它们和 Darknell 共用 450012xxx 图组（450012500 就是黑魔法师入口），**别当召唤物用**。

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
同色配对唯一）。

**3/8（壞打手）的定序（2026-11-05 结案，客户端优先 + 自建）**：
- **3 = Brain Scrambler**——预警 **3870ms**，全表最长。「地面出标靶 → 延迟 → 射击」是三段式，必须给最长窗口；
- **8 = Octopunch**——**2880ms**，最短档。9 段纯近战连打，只在起手瞬间给预警。
- 判据不只是时长：这两组都是**纯 `areaWarning`、没有 `hit/ball/effect` 帧**。这恰好解释了为什么
  Brain Scrambler 的标靶/子弹看不到——它们是**独立场地对象**，不在 `BossDunkel.img` 里；Octopunch 则是
  本体贴图连打。两组特效都缺席，所以只能靠「预警时长 + 招式性质」定序。**这是自建结论，实现时要写进注释。**

- 预警提前量 **2.79–3.87 秒**（最短 9 组 2790ms，最长 3 组 3870ms），与攻略「光柱约 2 秒、斩击 2.5–3 秒、
  剑气约 3 秒」同量级；Darknell 本体招（大击退/斩击/剑气）另走 mob attack，不在这张表里。
- **精英 Boss 出场档位**（dexless/wiki 一致，套件零实现）：**每掉 1/3 血加一只**。

  | 难度 | 1 只 | 2 只 | 3 只 |
  |---|---|---|---|
  | Normal | HP > 66% | 33–66% | < 33% |
  | Hard | —（开局即 2 只） | 51–100% | < 50% |

  revamp 后它们**不是 mob、不需要服务端实体**：只是「出现 → 放一招 → 消失」的演出，服务端在该时刻
  广播对应 group 的动作即可（组号↔招式见上表）。刷新间隔官方无秒数（dexless 仅 "every few seconds"）⇒
  **自建 20s**，且同屏多只不得复用同一 group（否则同色同招重叠）。

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
- **迁移 = 自建 `DarknellFight` 场类**（召唤 + 堕落勇士 + 精英触发 + 通关），**不要走 first-enter 脚本链**。
  ⚠️ 修正常见误写：`firstenter_450012210/600` **没有注册**——`CustomFieldScripts` 的枚举与 282 switch 里
  都没有任何 4500 段条目（有 401060300=Easy Magnus，别把 401060300 看成 410002060 那一类）。
  所以 282 下 `execFirstUserEnterScript` 会正常派发 XML 的 `onFirstUserEnter=dunkel_bosspre`，
  而那个脚本同样缺体 ⇒ **进场不会刷任何 mob**。补 `dunkel_bosspre` 也行，但脚本层在 282 里处处漏底，
  场类直连更稳（Seren 那边就是被脚本链坑死的）。
- **可以照抄**：击杀计数、通用通关、30min 限时、入场链、action 30/31 播报。
- **本轮已收敛**：260-1..4 的召唤目标（§3）、AreaWarning 0..9 的语义（§5.1）、
  fieldType 204 的性质（§1.1）、堕落勇士模板与血量（§3）。
- **落地口径（2026-11-05 定案，本文档不再有待拍板项）**：

  | 项 | 结论 | 落点 |
  |---|---|---|
  | 召唤入口 | `firstenter_450012210/600` 未注册、XML `dunkel_bosspre` 缺体 | 自建 `DarknellFight`，**在 `onFieldEnter` 直接调 spawn**，别走 first-enter 脚本链（这条链被 `execFirstUserEnterScript` 的 return 逻辑掐死了） |
  | 本体 | 8645009(N) / 8645066(H)，y 用图内 foothold | `spawnBossMob` |
  | 血量 | **官方 26 兆 / 160 兆**为默认；dat 1.23e9/2.45e9 是 282 客户端的展示占位，`BossHpConstants` 同值 | `spawnBossMob(id, hp)` 显式传值（不传就只有 12 亿，玩家秒掉） |
  | 堕落勇士 | 8645003(N)/8645069(H)，每批 4 只、5s 无敌、touch 5% | 场类定时召唤（不走 mob skill，本体没有这条技） |
  | 剑 | N：260-1/3→8645010/8645011；H：260-2/4→8645066/8645067 | `MobSkill` 补 `case 260`，先按 `mob.getTemplateId()` 分流（Seren 也用 260） |
  | 精英 Boss | 无 mob、纯演出，档位 §5.1 | HP 百分比触发，广播对应 `AreaWarning/<g>` |
  | 场地伤害 | 光柱 35%/40%、陨石 30%/40%、堕落勇士 5% | 自建常量（客户端无伤害数值） |
  | 精英间隔 | 官方只有 "every few seconds" | 自建 **20s**，同屏不复用 group |

- **血量口径说明**：`dat282` 的 maxHP 会随 mob 模板进客户端，用来画血条上限；它同时也是服务端
  `resolveMaxHp` 的兜底。官方 26 兆与 dat 的 12 亿差 4 个数量级，**保留 dat 值 = 一箭秒杀**，
  所以默认走官方值并在 spawn 时覆盖。若本项目要做轻量档，就显式保留 dat 值，别两头混。
- BossDunkel.img（265 层 + `Etc/_Canvas`）是唯一确定的客户端机制数据，迁移表现层时按
  areaWarning/hit/ball/effect/sound 五类落；282 overlay 那份骨架只能用来对音效名。

