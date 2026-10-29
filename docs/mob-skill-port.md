# 怪物技能与攻击：源端规则梳理与本工程对齐

2026-10-30。本文是 `NativeMobController` / `NativeMobDebuffs` 的规则说明书：
每一条都以源端（`BeiDou-Server/gms-server` Java + `clien/BeiDou.exe` + WZ 数据）为依据，
不做「按字段名猜测」的实现。逆向证据的细节（偏移、反汇编地址、字符串 id）
留在 `.agents/skills/beidou-client-reverse/references/monster-skills.md`，
本文只写**结论**和**落在哪一行代码**。

## 0. 一句话结论

源端**没有**服务端侧的怪物 AI 决策，也**没有**服务端侧的攻击伤害结算：

- 「放哪个技能」由服务端 `MoveLifeHandler:171-184` 挑一个「下一个可用技能」下发，
  客户端按自己的节奏请求，服务端 `Monster.canUseSkill` 再校验一次。
- 「攻击打多少」由客户端算，服务端 `TakeDamageHandler` 只做上限校验、`fixDamR` 覆盖、
  `deadlyAttack`、`mpBurn`、`conMP` 和 `disease`。
- 因此「主动怪 / 被动怪」在源端**没有服务端字段**，只有客户端 AI。`info/firstAttack`
  会被客户端读进模板 `+0x17C`，但消费者未定位，服务端零处读取。

本工程的怪物是 `EnemyUnit` + `NativeMobController`（单机、服务端权威），
所以上面前两条必须落成本地实现：技能选择 = 本地每隔 500ms 试一次（源端无对应代码，
属**本地策略**）；攻击伤害 = 用 `attack<N>/info/PADamage`（客户端本来就用这个值）。

## 1. 数据事实

| 对象 | 事实 |
| --- | --- |
| `Mob/<id>.img/info/skill/<n>` | 行字段 `skill / level / action / effectAfter`；`action` 缺省 `n+1` |
| `Mob/<id>.img/attack<N>/info` | 攻击参数；`attack<N>` 是**根动作容器**，不是 `info` 子节点 |
| `Mob/<id>.img/attack<N>/info/range` | **容器**，`lt`/`rb`（1819 行）**或** `sp`/`r`（399 行）；`info` 里没有 `range` 标量 |
| `Mob/<id>.img/attack<N>/info/type` | 不是元数据：`0/3/4` 一定是矩形、`1/2` 一定是圆；`2` 带 `ball`、`3/4` 带 `areaWarning`（§2.4 交叉表） |
| `Mob/<id>.img/skill<action>` | 技能动作帧；其 `delay` 之和 = 源端「生效延迟 + 动画锁」 |
| `Skill/MobSkill.img/<type>/level/<n>` | 51 个 type、706 个等级行；客户端只读 `hp/conMP/time/interval/prop/x/y/count/direction/elemAttr/lt/rb/effect/hit/mob/affected/tile/summonEffect/limit` + 数字子节点（召唤 ID）；其余节点（`screen`/`info`/`lua`/`fieldScript`…）是死数据 |
| `info/link` | 只继承**攻击动画**（`LifeFactory:504-513` 只 `addAll(attackInfos)`），属性/技能表不继承 |

数据规模（`tools/native_mob_skills.json`）：2907 只怪、2219 条攻击行、1744 条技能行、
505 个不同的 `(type, level)`、44 个 type 被实际引用。

`MobSkillType` 之外的坑（`tools/audit_mob_skills.py` 会断言）：

- `105`（9309201）在 **MobSkill.img 里是真实存在的类型**，但 Java 枚举里没有 →
  源服 `MobSkillType.from(105).orElseThrow()` 抛异常。可见「WZ 查得到」≠「服务端认」。
- `169`（9460022/3/4）两边都没有。
- 另有 14 条引用指向**不存在的等级**（`8810103→132/10`、`8810105→135/5`、
  `8810106→134/5`、`8810106→135/3`、`8890034→128/128`、`9300394→115/2`、
  `9400923→123/36`、`9460022/3/4→169/*`、`9500390/91/92→110/10`）→ 同样抛异常。
- 结论：共 **13 只怪**（`8810103 8810105 8810106 8890034 9300394 9309201 9400923
  9460022 9460023 9460024 9500390 9500391 9500392`）在源服里**根本加载不出 stats**。
  本工程打 `[MobSkill] Missing` 日志后跳过该技能、保留怪物可用，这是**主动偏差**
  （否则整只怪不可用）。

## 2. 服务端权威链

### 2.1 技能选择与门槛

```
候选 = info/skill 顺序（保序，LinkedHashSet）
逐条：type = MobSkillType.from(skill).orElseThrow()      // 抛异常的点
取 Mob/<id>.img/skill<action> 子节点 delay 之和 → animationTime
canUseSkill(skill, false)：非 SEAL_SKILL、非已有反伤、未在 usedSkills、MP 够
hpPercent = (int)(hp / maxHp * 100)；skill.hp >= hpPercent   // 注意方向
```

- `hp` 是**最低 HP 百分比门槛**，HP 越低能用的技能越多；缺省 100；`hp<=0` 被改写为 100。
- `hpPercent` 先转 `int` 再比较（截断，不四舍五入）。
- 冷却按 `MobSkillId(type, level)` 计，**不同等级各自独立**。

### 2.2 生效时机

```java
if (animationTime > 0 && type != BANISH) applyDelayedEffect(..., animationTime);
else                                     applyEffect(...);          // 立即
```

- BANISH(129) **永远立即**，换图发生在 `MoveLifeHandler:244-248` 广播之后。
- `applyDelayedEffect` 只检查「怪还活着」，**没有「被打断就取消」**。
- 同一个 `animationTime` 也是动画锁（`Monster.applyAnimationIfRoaming`）。
- 420/1744 条技能行在自己 img 里**没有** `skill<action>` 根节点（其中 399 条是 link 怪），
  所以这些技能在源端就是 0 延迟立即生效。

### 2.3 `applyEffect` 分派（全部 case）

| type | 效果 |
| --- | --- |
| 100/110/150、101/111/151、102/112/152、103/113/153 | `WEAPON_ATTACK_UP` / `MAGIC_ATTACK_UP` / `WEAPON_DEFENSE_UP` / `MAGIC_DEFENSE_UP` = `x` |
| 114 HEAL_M | 见 §2.5 |
| 120 SEAL / 121 DARKNESS / 122 WEAKEN / 123 STUN / 124 CURSE / 125 POISON / 126 SLOW | 对应 `Disease` |
| 127 DISPEL | `dispelDebuffs()`：只清 CURSE/DARKNESS/POISON/SEAL/WEAKEN/SLOW |
| 128 SEDUCE | `Disease.SEDUCE`；区域分支最多 `count` 人，单目标分支忽略 `count` |
| 129 BANISH | 有 `info/ban` 才生效，`banMap/0/field` 缺省 `-1` = 无目的地 |
| 131 AREA_POISON | 只生成 mob 型雾（`poison=false`），**零伤害** |
| 132 REVERSE_INPUT → CONFUSE、133 UNDEAD → ZOMBIFY | 对应 `Disease` |
| 140/141 | 互斥免疫，`WEAPON_IMMUNITY` / `MAGIC_IMMUNITY` |
| 143/144/145 | `WEAPON_REFLECT` / `MAGIC_REFLECT` / 两者，reflection 记 `x` |
| 154/155/156/157 | `ACC` / `AVOID` / `SPEED` / `SEAL_SKILL` = `x` |
| 170/186/188/189/190/191/200/201/202/203 | 召唤 |
| 174/177 | 纯视觉，空操作 |
| 176 | 森兰丸困难 35%；否则播 `customBoss/akayrum/screenCrack` 并 1100ms 后结算 |
| 183/184/185/187 | 播 `customBoss*/…`，1400ms 后按百分比扣血（184 固定 35%） |
| 214/215 | 空操作（现代状态机） |
| 105/115/134/135/136/142/169/138/146/171/172 | 枚举里有或没有，`switch` 都**没有 case** = 空操作 |

`applyEffect` 第一行是 `if (!makeChanceResult()) return;`——`prop` 判定在
「动作已经播完、冷却已经扣掉」之后，114 个等级行的 `prop != 100`。

### 2.4 范围口径

```java
new Rectangle(lt.x + mob.x, lt.y + mob.y, rb.x - lt.x, rb.y - lt.y)   // 无镜像
```

`calculateBoundingBox` **不做朝向镜像**。治疗、驱散、增益、疾病、放逐全用这个矩形。
只有普通攻击的 `attack<N>/info/range` 是客户端本地判定，才有 `info/noFlip` 翻转
（`AbstractDealDamageHandler:1396-1398`：`noFlip==0 && !facingLeft` 才镜像；239 只怪
`noFlip!=0` 永不翻）。

**攻击范围有两种形状，互斥**（2217 个 `range` 容器全量统计）：

| 形状 | 子节点 | 行数 | 例 |
| --- | --- | ---: | --- |
| 矩形 | `lt` + `rb` | 1819 | 2400040 attack1 |
| 圆心 + 半径 | `sp` + `r` | 399 | 3000000 attack1（`sp=(-14,-32)`、`r=280`） |
| 两者都有 | — | 1 | — |

客户端在同一个函数里解析这两种（`0x6805A9 'lt'`、`0x680621 'rb'`、`0x680735 'sp'`、
`0x680830 'r'`）。`sp` 是相对怪脚点的小偏移（全库 `sp.y ∈ -243..6`、`sp.x ∈ -530..659`），
`r` 中位数 280、常见 200~300，所以「圆心+半径」是唯一自洽读法。

**第三轮追加：客户端确实是按 `type` 分两支解析的，不是猜的。** 分派点在 `0x680593`：

```
mov eax, [A+0x0C]      ; type
cmp eax, 0 ; jne 0x68071B      ; type==0 -> lt/rb
cmp eax,3 ; je 0x680896        ; 3/4 -> start/areaCount/attackCount
cmp eax,4 ; je 0x680896
                               ; 否则 1/2 -> sp/r
```

布局也核清了（`A` = `attack<N>` 结构，`0xA4` 字节）：

| `A` 偏移 | type 0 | type 1/2 | type 3/4 |
| --- | --- | --- | --- |
| `+0x28` | 矩形分量0（校验三元组） | `sp.x`（明文 int） | `start` |
| `+0x2C` | 分量0 次字段 | `sp.y`（明文 int） | `areaCount`（≤32） |
| `+0x30` | 分量0 值 | `r`（明文 int） | `attackCount` |
| `+0x34` | 分量1 | — | `rb.y - lt.y` |
| `+0x38..0x67` | 分量1..3 | — | 第二组矩形（`0x38 + 4×12 = 0x68` 接上 `bulletSpeed`） |

`0x40FD6E` 的那 12 字节分量是**冗余校验**（`0x4165B1`，含 `xor 0xBAADF00D` 未初始化哨兵），
不是数值编码 —— 还原出来就是 `lt.x/lt.y/rb.x/rb.y`。本工程读 WZ 原文，不需要复刻。

**`type` 和形状是一一对应的**（2219 行交叉统计，`audit_mob_skills.py` 第 10 组断言锁住）：

| `type` | 行数 | 区域 | 附带容器 | 含义 |
| ---: | ---: | --- | --- | --- |
| 0 / 缺省 | 1706 | 矩形 | — | 近身/普通攻击 |
| 1 | 183 | 圆 | — | 圆形范围 |
| 2 | 202 | 圆 | `ball` | 发射物（`ball` 共 210 条，其中 204 条是 type 2、6 条缺省 type） |
| 3 | 101 | 矩形 | `areaWarning` | 多波次预警攻击 |
| 4 | 10 | 矩形 | `areaWarning` | 同上 |

**没有一条 `type 0/3/4` 走圆形**，也没有一条 `type 1/2` 带 `areaWarning`。
反方向有 2 条例外（`type 2` 却给 `lt/rb`、不带 `ball`），所以**几何仍按节点有无判断**
（先 `lt/rb` 再 `sp/r`），`type` 只用来解释语义。
131 条多波攻击里 `attackCount <= areaCount` 恒成立，`areaCount` 被解析器夹 `<=0x20`
（`0x680931 cmp eax,0x20`）。`areaWarning` 不是逐帧动画，是**一组带独立 `origin` 的落点贴图**
（2400040/attack1：15 个节点、`delay` 全 120、`origin` 从 `(24,409)` 到 `(80,412)`），
所以语义方向是「在 N 个落点各打一个框」。但节点数与 `areaCount` 没有稳定换算
（15/15 ✓、6/9 ✗、23/9 ✗），`DoAttack`（`0x666362`）里也找不到计数循环，
三个收集点都只处理单个攻击区 → **本工程不做多波**。

**命中形态不是点判定**：`0x95B734` 遍历对象 → `0x7A38D6` 取包围盒 →
`[0xBF04A8](包围盒, 攻击矩形)` 相交才收进结果数组，然后逐目标 `0x670C63` 结算。
所以只要身体碰到攻击区就算命中（见 §4.2）。

### 2.5 治疗与召唤

```
HEAL_M：区域分支 hps = (xInt / 1000) * (int)(950 + 1050 * rand)，逐只 heal(hps, y)
        单目标分支 monster.heal(x, y)
召唤  ：map mobs < 80；mapId 在 925020000..925033804 时 limit 无限
        summonLimit = min(ids, limit - 已召唤数)，shuffle 后取前 N
        8500003 y=-590；8500004 x±500；8510100 1/5 概率 y=78/x=小值，否则 x±500
        地图 220080001/230040420 越界时**重新随机**回界内（不是 clamp）
```

## 3. 玩家侧结算

| 规则 | 依据 | 行为 |
| --- | --- | --- |
| 免疫 | `AbstractDealDamageHandler:394-402` | 命中免疫时伤害填 **1**（不是 0，不显示 Miss） |
| 反伤 | 同文件 `665-682` | **只认 145**：物理扣 `x`、魔法扣 `y`；单独 143/144 只免疫不反弹 |
| 反伤互斥 | `Monster.canUseSkill:1572-1576` | 已有 reflect 状态时不能再放 143/144/145 |
| `deadlyAttack` | `TakeDamageHandler:188-192` | `mpattack = 当前MP - 1`，最终 MP = 1 |
| `fixDamR` | 同文件 `194-196` | `damage = max(1, maxHp * fixDamR / 100)` |
| `mpBurn` / `conMP` | 同文件 `192/204` | MP 伤害累加 / 消耗怪物 MP |
| 攻击带 `disease` | 同文件 `198-202` | 用 `(disease, level)` 走**完整** `applyEffect`，`skill=false` 单目标 |
| 疾病叠加 | `Character.giveDebuff:2823-2834` | 同种不刷新、最多 2 个、圣盾挡除 SEDUCE/STUN |
| ZOMBIFY | `StatEffect.calcHPChange:1493-1513` | 药水 `/= 2`；治疗技能取负 |
| CURSE | `Character.gainExp` | 经验减半 |
| POISON 跳伤 | **服务端零处计算** | 客户端算；本工程每秒扣 `x`、非致死，属本地实现 |

`attack<N>/info/disease` 实际取值是
`120,121,122,123,125,126,127,128,131,132,133,135,136,137,187`——不只有 120~136。
因为走完整 `applyEffect`，127 会真的驱散、131 会生成雾、187 会播赛莲的爆发；
137 不在枚举里，这条攻击挂不上任何东西。

## 4. 本轮对齐的改动（`Field/NativeMobController.cs`）

| 改动 | 依据 |
| --- | --- |
| 新增 `ServerMillis()`：`skill<action>`/`attack<N>` 子节点 `delay` 直接求和、缺省 0、不取绝对值；`AnimationMillis()` 只留给出死亡动作播放 | `LifeFactory:567-575 / 618-621` |
| 技能生效延迟改用 `ServerMillis(root.Get("skill"+action))`，BANISH 强制 0 | `MoveLifeHandler:135-141` |
| 技能/攻击的 `busyUntil` 改用同一个 `ServerMillis`（源端动画锁） | `Monster:335-358` |
| 攻击命中延迟注明为**表现层选择**（源服无「命中毫秒」概念） | `LifeFactory:624-626` |
| 删除 `attack<N>/info/fixedDamage` 分支（服务端不读、数据里 0 条） | `MobAttackInfoFactory` |
| HP 门槛改成 `(int)(hp/maxHp*100)` 截断比较 | `MoveLifeHandler:172-177` |
| HEAL_M 区域分支改 `x/1000*(950+1050*rand)` | `MobSkill.applyHealEffect:303-313` |
| AREA_POISON 去掉每秒毒伤，只留「生成雾」的事实 | `MapleMap.spawnMist(...,false,false,false)` |
| 疾病分支：区域分支对 SEDUCE 检查 `count`，单目标忽略 | `MobSkill.applyDisease:521-539` |
| BANISH 目的地判定 `-1` 为哨兵，`0` 是合法地图 | `LifeFactory:633`（缺省 -1） |
| 补 138/146/171/172 显式空操作 | `MobSkill.applyEffect` 无 case |
| 召唤坐标按源端：8510100 的 1/5 分支、越界重随机、道场上限 `925033804` | `MobSkill.summonMonsters:437-509`、`MapId.DOJO_MAX` |

`NativeMobDebuffs.cs` 复核后**无需改动**：`giveDebuff` 的两条前置条件、
`dispelDebuffs` 的 6 项清单、ZOMBIFY 的「药水减半 / 技能反转」都与源端逐行对上。

### 4.1 第二轮（2026-10-30，攻击范围两种形状）

| 改动 | 依据 |
| --- | --- |
| 新增 `Field/MobAttackArea.cs`（不含 UnityEngine 类型，可被 `tools/boss-check` 直接跑）：矩形与圆两种判定 | `0x680735 'sp'`、`0x680830 'r'` 与 `lt/rb` 在同一个解析函数里 |
| `NativeMobController.Area()` 取代攻击路径上的 `Range()`；`AttackInArea()` 支持两种形状 | 数据里 1819 矩形 / 399 圆、重叠 1 |
| `Range()` 保留给 MobSkill（`lt/rb`）与召唤物，语义未变 | `MobSkill.calculateBoundingBox` 只有矩形 |

**修掉的实际 bug**：`AttackInRange` 遇到 `range == null` 时返回 `false`（宁可不打也不造假），
而 399 条只有 `sp/r` 的攻击行在旧代码里被判成「无范围」→ 这些 Boss 攻击**永远不会触发**。
现在它们按 `圆心+半径` 正常命中，只有 1 条同时写两种形状的仍走矩形。

### 4.2 第三轮（2026-10-30，命中形态 + `type` 交叉验证）

| 改动 | 依据 |
| --- | --- |
| `MobAttackArea.Overlaps(minX,minY,maxX,maxY)`：圆走「圆心到包围盒最近点的距离」，矩形走 AABB 相交 | `0x95B734` 收集目标时用的是包围盒 × 攻击矩形相交；圆用同一套 `[0xBF04A8]` |
| `AttackInArea` 从「脚点是否在区域内」改成「玩家包围盒是否与区域相交」 | 同上；玩家盒取 `±14 × 48`，是**近似值**（真实包围盒来自角色身帧，未追） |
| `tools/boss-check` 新增 9 条 `Overlaps` 断言（矩形边缘/整体在外/圆相切/圆外） | 同上 |
| `export_mob_skills.py` 增加 `inactive`/`specialAttack`/`jumpAttack`/`attach`/`attachfacing` 与 `rangeValues` | 解析器 `0x67FF6F`/`0x6804A6`/`0x68009E`/`0x680F37`/`0x680FAD` |
| `audit_mob_skills.py` 第 10 组：圆形只允许出现在 `type 1/2`、多波必须带 `areaWarning` 且必须有矩形、`attackCount<=areaCount`、`inactive` 必须 0 条 | §2.4 的交叉表 |

**没有改的（查到了但不敢按推断落地）**：

- `inactive`：客户端确实会跳过（`0x66A7A2 cmp [A+0x10],0`），但**全库 0 条作者化**，所以不写分支。
- `specialAttack`：`0x667600` 用本行 `attackAfter` 覆盖怪物冷却 `[mob+0x9C]`，全库只有
  `9400633/attack4` 一条打开。影响可忽略，先不加特例。
- `tremble`（39 条，屏幕震动）、`knockback`（134 条，读点未定位）、`doFirst`
  （门控已核但 `[mob+0x174]` 语义未定）都留空。
- **同目标 2000ms 内只吃一次**（`0x670C63`：`[target+0x46C]` + 常量 `0x7D0`）：
  这是本轮新发现的一条真规则，但本工程已有 `invulnUntil`，两者时长不同，
  接这条会明显改变手感，先记在 §5 不动。

### 4.3 第四轮（递归下降重做：撤销「偏移表不可信」，工具补 `--fn`/`--fields`）

**这一轮没改游戏代码，改的是「能不能查得动」。** 前一轮把 `+0x28` 的冲突判成「反汇编失步、
偏移表只能对照」，这个结论是错的，本轮推翻并补齐证据。

| 改动 | 说明 |
| --- | --- |
| `tools/inspect_beidou_client.py` 新增 `--fn <entry>` | 按真实控制流递归下降（跟随 `jcc`/`jmp`/跳转表），替掉会漂的线性 sweep。`0x67FD81` 得到 1784 条指令、`0x67FD81..0x6811A4` 连续无空洞 |
| 新增 `--fields <entry> --pool <json>` | 自动把「`push <串id>; push <dst>; call 0x79E805`」与紧随其后的结构写入配对，一次导出 36 个字段的落点 |
| 线性 `--start/--end` 加强制提示 | 打印「boundaries drift after a switch table; use --fn」 |
| `monster-skills.md` 重写 §2.2 / 新增 §2.2a | union 布局表、`type` 分派点、`0x4165B1` 校验语义；撤销 §8-15/§8-16 |

**关键纠正**：`[ebp-0x18]` 不是「只被写过 0」——它就是 `A`，由 `0x681EC6` 通过 `esi`
间接写入（`0x681EE9`），纯字符串/字节匹配看不见。因此 `+0x28` 的两种写法不是解码错误，
而是**按 `type` 互斥的 union**（表见 §2.4）。`effectAfter` 也定位到 `A+0x88`。

### 4.4 第五轮（2026-10-29，改从 Compat DLL 反推 exe，攻击执行模型定型）

前四轮都在 exe 里硬找。这一轮换了入口：**客户端自带的 Compat DLL 是字节补丁层**，
hook 体里写着被拦函数的原地址（`push <原地址>; ret` 蹦床），等于一份现成的导览图。

| 来源 | 给出的 exe 地址 | 价值 |
| --- | --- | --- |
| `IndexedDamageNumberCompat.dll`（4.6KB） | `0x97251A`/`0x972626` 近身动作分派；`0x66C6E9` 伤害怪物；`0x980477`/`0x9804BB`/`0x9803EB` 本地攻击/移动；`[0xBEBF98]` 本地玩家 | 第一次确认 `0x97xxxx`/`0x98xxxx` 也属于怪物/角色动作层 |
| `BeiDouSkillCompatCore.dll` | `HookBishopMagicAoeClassifier` → `0x955D19`(单体) / **`0x956372`(AOE)**；`HookSummonAttackClassifier` → `0x7AD4EA`/`0x7AD51B` | 证明**客户端 AOE 一律是矩形**（`0x4144D4` + 常量 75/50/100），圆形只属于怪物 `type 1/2` |

在主函数 `0x66D9C0`（646 条指令）里按 `A->type` 分派出五种去向，**四种形态全部定型**：

| type | 形状 | 触发 | 判定 | 关键常量 |
| ---: | --- | --- | --- | --- |
| 0 | `A+0x28` 矩形 | **每刻持续检测**（`0x66E6B6` 循环只认 type 0）+ 动作生成区 | 包围盒 × 矩形相交 | — |
| 1 | 圆，心 `mob+(sp.x·dir, sp.y)` | 动作生成一次性 | **点**到圆心距离 | **半径 = `r+10`**（`0x66DC63` 字面量） |
| 2 | 弹道，起点同上 | 动作生成 | 每 tick **1×1** 矩形 | 方向 `atan2`（`0xA6203F`） |
| 3 | 多波矩形 | 动作生成 | **只画预警**（`mob.y±100`） | `start/spacing/dir` |
| 4 | 同上 | 动作生成 | 同 type 0 结算 | 同上 |

**多波公式**（`0x66DF4F..0x66DF76`）：
`centreX += ((start + waveIndex) * spacing + spacing/2) * dir`。
`areaCount` 只是 `>0` 门槛，`attackCount` 才是循环上限，哪几波生成由调用者的**位掩码**决定
（`0x66DF35 test [ebp+0x10], 1<<i`）——这也解释了为什么解析器把 `attackCount` 夹到 32。

代码改动：

| 文件 | 改动 |
| --- | --- |
| `Field/MobAttackArea.cs` | 新增 `HitSlack = 10f`（圆形判定用 `r+10`）；新增 `WaveOffsetX(start,waveIndex,spacing,dir)` 纯函数 |
| `Field/NativeMobController.cs` | `AttackInArea` 按形态分流：圆形走点判定（`Contains`）、矩形走包围盒相交（`Overlaps`）。~~之前圆形被当包围盒，等于把圆心命中放宽成一个盒子~~ → **第六轮已推翻，两种形状都改回包围盒（§4.5）** |
| `tools/boss-check/Program.cs` | 新增 8 条断言：`r+10` 边界（290 命中 / 291 拒）、`WaveOffsetX` 四组、位掩码门控 |

**这一条的结论在第六轮被推翻**：源端的圆形判定也是「目标包围盒」，见 §4.5。

### 4.5 第六轮（2026-10-29，按「已实现 vs 源端」逐条核对）

这一轮不找新入口，而是把 `monster-skills.md` 的每条结论拿去验代码。改动如下。

| 文件 | 改动 | 依据 |
| --- | --- | --- |
| `Field/MobAttackArea.cs` | 圆形不再走点判定：`Overlaps`（圆心 → 包围盒最近点 ≤ `r+10`）被确认为**攻击判定本身**；`Contains` 降级为纯点探针（只给自检用） | `0x66DB68 call 0x7A38D6` 取目标包围盒 → `0x66DBDC..0x66DC1B` 把圆心夹进盒子 → `0x66DC1E/21` 量「夹后点 − 圆心」 → `0x66DC63` `r+10` |
| `Field/NativeMobController.cs` | `AttackInArea` 两种形状统一走 `Overlaps`（源端两条通路都以目标身体为准） | 同上 + `0x95B734` |
| 同上 | 攻击结算前加**同目标 2000ms** 闸门，时间戳写在伤害计算之前 | `0x670C99..0x670CB2`（`[esi+0x46C]`，`0x7D0`） |
| 同上 | `specialAttack` 非 0 时把本行 `attackAfter` 记成怪物整体攻击间隔 | `0x667600`：`mov [mob+0x9C], A+0x8C`，配合 `0x670A3F` 读 `[mob+0xA0]+[mob+0x9C]` |
| 同上 | `tremble` 非 0 时震屏（常量 30.0，按像素 ÷ `Art.PPU` 换成世界单位） | `0x6671D3` + `0xAF46E0`=30.0 + `0x43DA05` |
| 同上 | 攻击执行播 `attack<N>/info/effect`（怪身上）、命中播 `.../hit`（被打者身上） | §6.1 的活路径；数据 331 / 2211 条 |
| 同上 | 技能生效播 `Skill/MobSkill.img/<id>/level/<n>/effect`；131 额外把 `tile` 按 `x*100 ms` 落地 | §3.5 白名单 + `spawnMist(mist, x*100, …)` |
| 同上 | BANISH 换图前先发 `info/ban/banMsg`（消息类型 5） | `LifeFactory:633` + `Character.changeMapBanish:1563` `dropMessage(5, msg)` |
| `Field/NativeMobDebuffs.cs` | `Apply` 增加 `region` 参数：区域分支的圣盾**连 SEDUCE/STUN 一起挡**，单目标分支仍放过 | `MobSkill.applyDisease:525` vs `Character.giveDebuff:2823`（源端两套口径，不合并） |
| `PlayerCtrl.cs` | 新增 `lastMobAttackMs`（= `[target+0x46C]`） | `0x670C99` |
| `Server/Combat/NativeBossRules.cs` | 新增 `MobAttackGapMillis` / `MobAttackAllowed`（可无 Unity 测试） | 同上 |
| `HitFeel.cs` | 抽出 `Shake(amp, seconds)`，`Punch` 复用它（原来衰减窗口写死 0.14s） | 震屏与顿帧解耦 |
| `NativeImgAnimation.cs` | 新增 `SpawnAt(ImgNode, …)`，可直接播放已解析的画布节点 | 攻击/技能画布都是手里已有的 `ImgNode` |

被纠正的两条旧结论：

1. **圆形是点判定** → 错。第六轮补看 `0x66DB68` 才发现在算距离之前先取了目标包围盒。
   旧实现让「圆心 → 脚点」当判定，身高方向的射程少算最多 48px。
2. **`0x664559` 是「攻击允许矩形」** → 错。它是「取实体世界包围盒」：
   `0x6645AC [0xBF0454]` 用 `obj+0x510/+0x514` 把 `obj+0x40C` 的局部矩形换成世界坐标，
   `flag!=0` 时再并上 `+0x41C` 那一份。`0x670D17` 拿它给伤害数字取随机落点就是明证。

还补了一条**证据**（不改行为）：`attackAfter` 确实是命中延迟 —— `0x66DE88` 把它写进
攻击区对象的 `+0` 触发时刻。之前记成「表现层选择」是缺证据，不是结论相反。



**源端根本不读、所以不实现的（结论是「没有链路」，不是「还没查」）**：

- `Mob/<id>.img/info/skill/<n>/{effect,hit,mob}`：三个格式串在 `.text` 里**全立即数扫描 0 引用**。
- `Mob/<id>.img/<action>/info/areaAttack`：同样 0 引用。
- `MobSkill.img` 的 `screen`(仅 176/1..6)、`info`(133 条韩文标签)、`lua`、`summonOnce`、
  `fieldScript`、`linkHP`、`afterEffect`、`mob0`、`tremble`：客户端加载器
  （`0x7627C7..0x7632A8`）的读取白名单里没有它们；Java 服务端也 0 命中。
  **「全屏技能」在这套客户端上没有任何入口。**
- `MobSkill.img` 用 `mpCon`，客户端却去读 `conMP`（字符串 5620，攻击 info 的同名字段）
  → 客户端永远拿不到怪技能的 MP 消耗。服务端照常扣（`MobSkillFactory` 读 `mpCon`）。

**有解析偏移、没有执行语义，不实现**：

- `range/start`、`areaCount`、`attackCount`、`lt2/rb2`、`reverse`、`knockback`。
- `effectAfter` 的**存储位置已定位 = `A+0x88`**（`0x681056`），但没有任何分支消费它。
  仍然不要凭名字实现「延迟播特效」。
- ~~`attackAfter` 的「命中帧毫秒」用法仍是表现层选择~~：**第六轮补证，它确实是命中延迟**
  （`0x66DE88` 把 `A+0x8C` 写进攻击区对象的 `+0` 触发时刻）。服务端那边仍只有
  `LifeFactory:624-626` 的冷却兜底。
- ~~`tremble`/`specialAttack` 只记录不实现~~：**第六轮已实现**（§4.5），
  `inactive` 全库 0 条仍然只记录。
- `knockback`（134 条）：怪物区段（`0x660000..0x780000`）93 处 `+0x98` 引用逐条核对过，
  没有一处落在 `GetAttackInfo` 的调用上下文里；同批的 `tremble`/`specialAttack`/`doFirst`
  都找到了。**倾向「客户端不消费」，但没有证伪**，所以「击退多少像素」依然无依据。

~~**已定位但没有实现的一条真规则**：同目标 2000ms~~ → **第六轮已实现**（§4.5）。

**客户端行为，未核**：

- POISON 每秒伤害、SEDUCE 的 `x` 语义（数据里 1/2/3/4/6/7）、SLOW 的 `x`（60/80）。
- 移动速度只核到 `clamp(speed|chase|fly + 修正, 10, 140) * 0.01` 是**倍率**，
  `%`→`px/s` 的基准与 `mob+0x228` 的来源未核，仍未用 WZ `info/speed`。
- ~~圆形攻击（`sp+r`）转换点没定位~~：**已结案（§4.4/§4.5）**。圆形走 `0x66DAB0` 分支，
  但量的是「圆心 → **目标包围盒最近点**」`<= r+10`，不是点到圆心距离。

**表现缺口**：状态图标 `affected`(227)/`mob`(174)、`MobSkill` 的 `hit`(12)、
`areaWarning` 预警画布仍未画；`effect`/`hit`（攻击行）与 `effect`/`tile`（技能）第六轮已接。

**本地策略**：「主动/被动仇恨」（`aggroRange` + 高度差）无源端依据；
本地 AI 每 500ms 试一次技能，源端是客户端驱动，节奏不可能完全一致。
**多波（type 3/4）仍未实现**：公式已核（§4.4），但本工程一次动作只生成一个攻击区，
且 `type 3` 的预警画布没画，只做结算会让「无预警直接挨打」比源端更恶劣。

## 6. 验证

```sh
cd MapleDash
rtk dotnet build tools/unity-compile-check/Check.csproj -t:Rebuild   # 0 error / 0 warning
rtk python3 tools/audit_mob_skills.py                                # 数据不变量
cd tools/boss-check && rtk dotnet run -- <MapleDashResources>/Data    # 规则 + 真实 IMG 区域形状

# 逆向侧：查攻击结构的字段落点（不要用线性 sweep，会漂）
python tools/inspect_beidou_client.py <BeiDou.exe> --fields 0x67FD81 --pool <pool.json>
python tools/inspect_beidou_client.py <BeiDou.exe> --fn 0x67FD81 --start 0x680593 --end 0x6805b0
```

`audit_mob_skills.py` 断言的是**数据侧**不变量（枚举覆盖、等级存在性、`hp`/`prop` 分布、
`lt+rb` 分支比例、`skill<action>` 缺失数、`maxHP<=0`、攻击 `disease` 取值、
`fixedDamage` 必须为空、MobSkill 等级节点白名单、`direction` 必须不存在、
`screen` 只能在 176/1..6、矩形与圆必须基本互斥、圆形只出现在 `type 1/2`、
多波必须带 `areaWarning` 且必须有矩形、`attackCount<=areaCount`、`inactive` 必须 0 条），
退出码非 0 即数据或假设变了。

`boss-check` 里 `AttackAreaGeometry()` 是无数据也能跑的纯几何断言（圆不是它的外接矩形、
半径从 `sp` 量、`r+10` 容差边界 290/291、`Overlaps` 的矩形边缘/整体在外/圆相切/圆外、
`WaveOffsetX` 四组与位掩码门控）；第六轮另加三条**点探针 vs 身体探针必须分叉**的断言
（脚点 140px 在 100px 圆外、但同姿势的身体进圈）与四条 2000ms 窗口断言
（首次命中 / 1999 拒 / 2000 收 / 哨兵 0）。
带 Data 参数时再对 6 个真实攻击行验形状
（3000000/3210800/3220000 = 圆，2400040/8880000/8870000 = 矩形）。

2026-10-29 第六轮跑完的实测数字：

```
Build succeeded. 0 Warning(s) 0 Error(s)
OK: mob skill data invariants hold (mobs=2907, types=51)
  note: attack areas: rect 1819, point+radius 399, both 1, sp without r 6
  note: circle-only attacks by type: {'1': 183, '2': 202, 'None': 13}
  note: attack rows with specialAttack: ['9400633/attack4']
Boss checks: 66 passed        (无数据参数；上一轮 61)
Boss checks: 147 passed       (带 Data；上一轮 142)  → Attack area shapes: 3 rect, 3 circle
```
它不验证运行时表现——运行时结论仍以 `.agents/.../monster-skills.md` 的证据等级为准。
