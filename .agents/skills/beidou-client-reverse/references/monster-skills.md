# 怪物技能、攻击与状态：数据 / 服务端规则 / 客户端执行

2026-10-29。本文按「数据 → 服务端权威规则 → 客户端执行 → 项目实现」四层记录。
**不是「全部怪物技能已迁移」声明。** 每节末尾标注证据等级，未核实项集中放在最后一节。

## 0. 身份与复现

| 对象 | 身份 |
| --- | --- |
| `clien/BeiDou.exe` | SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，ImageBase `0x400000` |
| `Data/Skill/MobSkill.img` | SHA256 `3ace2a53a7b7b34b649061573f4991d293a97c1f7c8b9d08ac9351f08ae06ab3` |
| `Data/Mob/*.img` | 2908 个文件，逐文件 SHA256 存在 `tools/native_mob_skills.json` 的 `mobs.<id>.sha256` |
| 素材根 | `MapleDashResources/Data`（与 `BeiDou-Server/clien/Data` 同源） |

复现（工作目录 `MapleDash/`）：

```sh
rtk proxy python3 tools/export_mob_skills.py            # 重新导出 tools/native_mob_skills.json
rtk proxy /tmp/mapledash-reverse-venv/bin/python tools/inspect_beidou_client.py \
    ../../Beidou-Server/clien/BeiDou.exe --start 0x67D3F8 --end 0x67D54B
rtk proxy python3 tools/imgdump.py Skill/MobSkill.img "176/level/1" --depth 2
rtk proxy python3 tools/imgdump.py Mob/8510000.img "attack1/info" --depth 2
```

`export_mob_skills.py` 只读原始 IMG，不落地像素；输出的 `summary` 段是全量统计，
`mobs.*.sha256` 用于判断源数据是否变过。

---

## 1. 怪物模板：`Mob/<id>.img/info`

### 1.1 客户端实际读取的字段（EXE 已核）

模板加载函数范围 `0x67D000..0x67F400`（单函数，`push <字符串id>` 紧接
`call 0x79E805` 取字符串，与本记录其它模块同一取串入口）。按读取顺序：

```
link(962) → Mob/%s.img(1603) → info(961) → link(962)
chase(3412) jump(5635) move(5640) fly(1040) regen(5649)
species(1606) bodyAttack(1607) boss(1608) hideHP(1609) hideName(5630) hideLevel(1610)
String/Mob.img(2240) name(1604) level(1611) maxHP(1612) maxMP(1613)
speed(1614) flySpeed(1615) chaseSpeed(1616)
PADamage(1617) PDDamage(1618) MADamage(1619) MDDamage(1620) acc(1621) eva(1622) exp(1623)
pushed(1624) elemAttr(1625) hpRecovery(1626) mpRecovery(1627)
undead(5657) pickUp(1631) firstAttack(1632) selfDestruction(2897)
fs(1633) invincible(2898) disable(5626) noFlip(2982) notAttack(3272)
HPgaugeHide(1628) AngerGauge(1629) ChargeCount(1630)
selfDestruction/action(5614) attack(1145) die(1065) hit(5631)
damagedByMob(3773) dropItemPeriod(3774) damagedBySelectedMob(4273)
ban(4028) banType(4029) damagedBySelectedSkill(4274)
```

要点：

- **`link` 解析后仍从原 `info` 读规则字段**（`0x67D1C1` 再取 `info`，`0x67D243` 再取 `link`）。
  与源服 `MapFactory`/`LifeFactory` 的链接怪规则一致，不把整份 stats 从被链接者复制。
- `chase`/`jump`/`move`/`fly`/`regen` 是**根节点下的动画容器**，不是 `info` 子节点。
- `bodyAttack`、`boss`、`hideHP`、`hideName`、`hideLevel` 等布尔写入模板的一个标志集合
  （`0x67D67F` / `0x67D6F2` 之后 `mov [edi+0x60], eax`、`[edi+0x6C]` 是集合计数）。
- `firstAttack` 单独写入模板 `+0x17C`（`0x67E43E..0x67E44B`），**消费者尚未定位**；
  源服 Java 全仓 0 处引用 `firstAttack`（只有 `LifeFactory` 写入 `stats.firstAttack`），
  即「是否主动攻击」由客户端 AI 决定，服务端不参与。
- `notAttack` 写模板 `+0x1A4`/`+0x1AC`（与既有记录一致，`CMobPool::FindHitMob 0x678D47` 消费）。

### 1.2 移动能力分类（EXE 已核）

`0x67D4DB..0x67D54B`：

```
fly 存在            -> movType = 3
否则 jump 且 move   -> movType = 2
否则 move           -> movType = 1
否则                -> movType = 0
结果经 0x4165B1 写入 模板+0x48
```

`0x67D3F8..0x67D43B`：`chase` 容器是否存在 → `模板+0x204 = 1/0`。

`0x66B599..0x66B6AD` 是消费方：读 `模板+0x48` 与 `模板+0x204`，按
`switch(movType)` 与朝向生成移动动作码（低位置 1 表示朝左）。**这就是「怪物会不会走/飞/跳」
的全部判据，不存在 `info/mobType` 之类的分类字段。** 数据侧统计（`native_mob_skills.json`
`summary.mobility`）：无移动容器 1085、只有 `move` 1441、`move+jump` 261、`fly` 121。

`0x664380..0x664397`：`clamp(模板速度 + 运行时修正, 10, 140)` 后转 float。上界常量
`0x8C`=140、下界 `0xA`=10。修正量的来源（`mob+0x228`）尚未追踪，**不能据此声称缓速
技能的数值已对接**。

#### 三个速度字段（2026-10-30 已核）

| 节点 | 模板槽位（wrapper / 缓存值） | 解析点 | 存入 |
| --- | --- | --- | --- |
| `speed` | `+0x94` / `+0x9C` | `0x67DB67` | `0x67DBC4 mov [edi+0x9C]` |
| `flySpeed` | `+0xA0` / `+0xA8` | `0x67DBE9` | `0x67DC41 mov [edi+0xA8]` |
| `chaseSpeed` | `+0xAC` / `+0xB4` | `0x67DC66` | `0x67DCBE mov [edi+0xB4]` |

三者在**加载期**都先加100，再夹到0..140。加100位于 `67DB9A/67DC1C/67DC99`；
`6823F0` 返回两个参数的较大值，下限为0。旧记录遗漏加100与下限，现纠正。

运行期换算（`0x66439A..0x6643A3`）：

```
f = clamp(speed_or_chase_or_fly + mob+0x228, 10, 140) * 0.01
```

乘数常量 `0xAF14F0 = 0.01`（double）。结果当作**动画/移动速度倍率**写进
`[mob+0x50C]` 的各个槽位（`+0x18`、`+0x24`、`+0x54`、`+0x60`、`+0x6C`、`+0x78`、`+0x84`），
`movType != 0` 才算速度，`movType == 0` 直接 `fldz`（0）。
`movType == 3`（fly）走 `[esi+0x224]`（flySpeed）+ 修正，并把 `+0x78` 置 1.0。

**`speed` 与 `chaseSpeed` 的分工**：`0x66432F` 先看模板 `+0x204`（= 有 `chase` 容器），
只有在「有追击目标（`[eax+0x250] != 0`）且 `chase` 存在」时才读 `chaseSpeed`；
否则走另一分支。**没有任何一处把 WZ `speed` 直接当像素/秒用**，
地面 `%`→`px/s` 基准已定位为 `Physics.walkSpeed`，见文末补记；飞行积分尚未迁移。

### 1.3 服务端读取的字段（Java 已核）

`LifeFactory.getMonsterStats`（494 起）读：`maxHP/maxMP/exp/level/removeAfter/boss/
explosiveReward/publicReward/undead/buff/getCP/removeOnMiss/coolDamage/coolDamageProb/
loseItem/selfDestruction/firstAttack/dropItemPeriod/hpTagColor/hpTagBgcolor/elemAttr/
revive/reviveN/skill/attack<N>/ban`。

服务端**不**读：`bodyAttack`、`notAttack`、`disable`、`invincible`、`pushed`、
`hpRecovery`、`mpRecovery`、`speed`、`chaseSpeed`、`flySpeed`、`acc`、`eva`、
`fixedDamage`、`onlyNormalAttack`。这些只能由 EXE 消费，或根本无人消费。
`monsterStats` 里的 `movetype` 是本地为选帧补的（`fly/0` 优先、否则 `stand/0`），
**不是原端 AI 语义**，不要拿它当「会不会飞」的依据。

**`info/link` 只继承攻击动画，不继承属性。** `getMonsterStats`（`LifeFactory:504-513`）在
`linkMid != 0` 时递归拿到 `linkStats`，然后**只** `attackInfos.addAll(linkStats.getRight())`，
`hp/level/exp/PADamage/...` 一个都不复制（各字段的 `getIntConvert(..., 默认值)` 是**字面常量**，
不是从 linkStats 取）。所以只有 `info` 里一个数值都没有的桩怪（如 `9501016`，只写了
`link=2230102`），服务端眼里 `maxHP = 0`。技能表同样读**自己的** `info/skill`，
不从被链接者继承。

**有 13 只怪在源服里根本加载不了**：`info/skill` 指向不存在的 MobSkill 时，
`MobSkillType.from(...).orElseThrow()`（`LifeFactory:596`）或
`MobSkillFactory.getMobSkillOrThrow`（`:606`）直接抛异常，整只怪拿不到 stats。
全量名单（`tools/audit_mob_skills.py` 的 `EXPECTED_UNLOADABLE` 会断言）：
`8810103/8810105/8810106/8890034/9300394/9309201/9400923/9460022/9460023/9460024/
9500390/9500391/9500392`。
注意 `105` 在 **MobSkill.img 里是真实存在的类型**，只是 Java 枚举没写它——所以
「WZ 里查得到」不等于「服务端认」。本工程选择打日志跳过该技能而不是让整只怪不可用，
这是**主动偏差**，已记录在案。

---

## 2. 怪物攻击：`Mob/<id>.img/attack<N>/info`

### 2.1 位置（数据事实，容易搞错）

`attack1..attackN` 是 **`Mob/<id>.img` 根下的动作容器**，`info` 是它们的子节点：

```
Mob/8510000.img/attack1/0,1,2...      ← 动画帧（带 delay）
Mob/8510000.img/attack1/info/...      ← 命中参数
```

`Mob/<id>.img/info/attack` 不存在。旧实现若在 `info` 下找 `attack1` 会永远找不到。

### 2.2 字段与 EXE 解析偏移

`0x67F000..0x681600` 是 `attack<N>/info` 解析函数。已核写入偏移（对象基址 `A`）：

| 节点 | 偏移 | 证据 |
| --- | --- | --- |
| `PADamage` | `A+0x18` | `0x6801DD..0x68021C` |
| `bulletSpeed` | `A+0x68` | `0x68023F..0x68027E` |
| `deadlyAttack` | `A+0x6C` | `0x6802A0..0x6802E5` |
| `tremble` | `A+0x70` | `0x680307..0x68034C` |
| `doFirst` | `A+0x90` | `0x68036E..0x6803B3` |
| `mpBurn` | `A+0x94` | `0x6803D8..0x680417` |
| `range/start` | `+0x28` | `0x68089D..0x6808DC` |
| `range/areaCount` | `+0x2C` | `0x6808FE..0x68093E`，**结果夹到 ≤ 32**（`cmp eax,0x20`） |
| `range/attackCount` | `+0x30` | `0x680960..0x68099F` |
| `ball` 画布 | `+0x7C` | `0x680D7F..0x680DC8` |
| `areaWarning` 画布 | `+0x80` | `0x680DEB..0x680E8C` |
| `attackAfter` | `A+0x8C` | `0x68107B push 0x676` → `0x6810BA mov [eax+0x8C],esi` |
| `type` | `A+0x0C` | `0x67FF0A` → `0x67FF4D` |
| `inactive`（**布尔**） | `A+0x10` | `0x67FF6F` → `0x67FFB4` |
| `conMP` | `A+0x14` | `0x67FFD6` → `0x680015` |
| `magic` | `A+0x1C` | `0x680037` → `0x68007C` |
| `elemAttr` | `A+0x20`，**只在 `magic != 0` 时解析** | `0x680104..0x680163`，先 `cmp [eax+0x1C],0` |
| `jumpAttack` | `A+0x24` | `0x68009E` → `0x6800E3` |
| `knockback`（**布尔**） | `A+0x98` | `0x68043C` → `0x680481`（`neg/sbb/neg` 归一化） |
| `specialAttack`（**布尔**） | `A+0xA0` | `0x6804A6` → `0x6804EB` |
| `attach` | `A+0x84` | `0x680F37` → `0x680F7A` |
| `attachfacing` | `A+0x9C` | `0x680FAD` → `0x680FED` |
| `effectAfter` | `A+0x88` | `0x681017` → `0x681056`（**2026-10-30 修正，之前记为未定位**） |

**这张表怎么来的（可复现）**：不要用线性反汇编。`--fn` 递归下降后，把「`push <串id>; push <dst>;
call 0x79E805`」与「紧随其后的结构写入」配对，即可整表导出：

```sh
python tools/inspect_beidou_client.py <BeiDou.exe> --fields 0x67FD81 --pool <pool.json>
```

输出确认该函数共 **36 处字符串查找 / 1784 条指令 / 覆盖 `0x67FD81..0x6811A4` 且无空洞**。

解析顺序（同一函数 `0x67FD81`，注意入口与上表同名函数的一致）：
`info` → `type` → `inactive` → `conMP` → `magic` → `jumpAttack` → `elemAttr` →
`PADamage` → `bulletSpeed` → `deadlyAttack` → `tremble` → `doFirst` → `mpBurn` →
`knockback` → `specialAttack` → `range` → **按 `type` 分两支** →
`effect` → `hit` → `ball` → `areaWarning` → `attach` → `attachfacing` →
`effectAfter` → `attackAfter`。

`tremble`/`doFirst`/`knockback`/`specialAttack`/`inactive` 都是**布尔**（数据里取值只有 1），
不是幅度值。`elemAttr` 是字符串（`'L'` 之类），只有 `magic != 0` 才解析成元素位。

攻击结构本体：`0x682449 push 0xA4` 分配，ctor `0x683512`（只 `memset` 前 `0xC` 字节，
vtable `0xAF84CC`），取用入口 `0x529E7B GetAttackInfo(idx)`。谁读哪个字段见 §6.5。
数据侧作者化计数（2219 行）：`jumpAttack` 162、`knockback` 134、`tremble` 39、
`doFirst` 29、`attach` 20、`specialAttack` **1**（`9400633/attack4`）、`inactive` **0**。

### 2.2a `range` 为什么「两种写法打架」——type 分派 + 真 union

上一版把 `+0x28` 一带的冲突记成「反汇编失步，偏移表不可信」。**2026-10-30 用递归下降
重做后否掉了这个结论**：1784 条指令连续无空洞，冲突是真的设计，不是解码错误。

分派点 `0x680593`：

```
00680593  mov eax, [ebp-0x18]     ; eax = A（见下）
00680596  mov eax, [eax+0x0c]     ; eax = A->type
00680599  cmp eax, ebx            ; ebx == 0
0068059b  jne 0x68071b            ; type != 0 走别支
```

- `type == 0` → 读 `lt`/`rb`，调 `0x40FD6E(A+0x28, …)` 写 **4 分量结构**（每分量 12 字节）
- `type != 0` → `0x68071B: cmp eax,3 / je 0x680896; cmp eax,4 / je 0x680896`
  - `type == 3/4` → `start`→`A+0x28`、`areaCount`→`A+0x2C`（夹 ≤32）、
    `attackCount`→`A+0x30`；**真实矩形改写 `A+0x38`**（`0x680B13 add edi,0x38`），
    另外 `A+0x34 = rb.y - lt.y`（`0x680B4A sub / 0x680B4C`）
  - `type == 1/2` → 读 `sp`，经虚调用 `[vtbl+0x8C]` 把 **`sp.x`/`sp.y` 直接写 `A+0x28`/`A+0x2C`**
    （`0x6807E7 add edx,0x2c` / `0x6807F0 add edx,0x28`）；`r` → `A+0x30`

即同一段内存有两种语义，且**互斥**：

| `A` 偏移 | type 0（近身） | type 1/2（圆/发射物） | type 3/4（多波） |
| --- | --- | --- | --- |
| `+0x28` | 分量0 首字段 | `sp.x`（**裸 int**） | `start` |
| `+0x2C` | 分量0 次字段 | `sp.y`（**裸 int**） | `areaCount`（≤32） |
| `+0x30` | 分量0 值 | `r`（**裸 int**） | `attackCount` |
| `+0x34` | 分量1 首字段 | 未写 | `rb.y - lt.y`（高度） |
| `+0x38..0x67` | 分量1..3 | 未写 | **第二组 4 分量矩形** |

`0x38 + 4×12 = 0x68`，正好接上 `bulletSpeed`(`A+0x68`) —— 布局闭合，佐证这个读法。

**分量为什么是 12 字节**：`0x4165B1(in, out)` 不是数值转换，而是**冗余校验**：
`out[0] = h1`、`out[4] = h2`、`out[8] = h1+h2`，其中 `h2 = rotl(in ^ h1, 5)` 且
**`xor 0xBAADF00D`（未初始化堆内存哨兵）** 参与了运算（`0x4165D4`）。
所以 type 0 的矩形是**带校验的 3 元组**，type 1/2/3/4 的分量是明文 int。

**对移植的意义**：校验只是反作弊，还原后的数值就是 `lt.x/lt.y/rb.x/rb.y`、`sp.x/sp.y/r`、
`start/areaCount/attackCount`。本工程读 WZ 原文，天然等价，不需要复刻校验。
`§8-15`（偏移表不可信）与 `§8-16`（`esi` 身份）由本节结案：

- `[ebp-0x18]` 就是 `A`。它由 `0x681EC6`（`[ebp-0x1C]` 的 `Reset()`）通过 `esi`
  **间接**写入 `[esi+4]`，纯字节/字符串匹配看不到，才误判成「只被 0 写过」。
  `0x681E74` 是配套的 `Get()`，返回 `[ecx+4]` = `A`。
- `0x666362` 的 `esi` 是**怪物自身**：`0x666374 mov esi, ecx`（`this`），随后读
  `[esi+0x328]`（队伍 id）、`[esi+0x38C]`、`[esi+0x450]`/`[esi+0x458]`，全是怪物对象的大偏移
  （与 `[mob+0x228]`/`[mob+0x50C]` 同族）。**攻击区对象是它的局部变量**，不是 `esi`。

### 2.2b 两个「格式串存在但没人用」的坑

只扫 `push imm32` 会漏掉 `mov eax, <id>` 形式，所以这里用**全立即数扫描**复核过
（capstone 遍历 `.text`，匹配任意 `X86_OP_IMM == id`）：

| 字符串 | id | 立即数引用数 |
| --- | ---: | ---: |
| `Mob/%07d.img/info/skill/%03d/effect` | 2752 | **0** |
| `Mob/%07d.img/info/skill/%03d/hit` | 2753 | **0** |
| `Mob/%07d.img/info/skill/%03d/mob` | 2754 | **0** |
| `Mob/%07d.img/%s/info/areaAttack` | 1652 | **0** |

结论：

- **`Mob/<id>.img/info/skill/<n>/{effect,hit,mob}` 客户端从不加载。** 怪物放技能时的
  特效只能来自 `Skill/MobSkill.img/<type>/level/<n>/{effect,hit,mob}`（§3.4）。
  `Mob/<id>.img/info/skill/<n>` 下客户端真正读的只有 `skill/level/action/effectAfter`
  这四个标量（模板加载器，§6.2）。
- **`info/areaAttack` 也是死的**（数据里只有 2 处：8880830 attack3、8880832 attack1）。

### 2.3 服务端只消费其中一小部分

`MobAttackInfoFactory`：`deadlyAttack`（**只看存在性**）、`mpBurn`、`disease`、
`level`、`conMP`、`fixDamR`。
`LifeFactory:617-629`：`conMP` = MP 消耗，`info/attackAfter` = 冷却，`attack<N>` 全部
子节点 `delay` 之和 = 动画时长。

`attack<N>/info` 里**没有任何 `lt`/`rb`，也没有 `range` 标量**（2219 条攻击行里 0 条）；
范围是 `info/range` **容器**下的 `lt`/`rb` 两个 Point（2217 条攻击行有该容器）。
`attackAfter` 在 EXE 里被读进 `+0x8C`，但服务端只拿它当 Karing 冷却兜底，见 §2.4。

服务端**完全不读** `attack<N>/info/fixedDamage`（全仓只有 `fixDamR`），数据里也没有
任何一条攻击行写了 `fixedDamage`（2219 条里 0 条）。不要按名字实现「绝对固定伤害」。
`TakeDamageHandler:185-223` 的应用顺序（**权威**）：

1. `deadlyAttack` 存在 → `mpattack = 当前MP - 1`（致命攻击把 MP 打到 1）
2. `mpattack += mpBurn`
3. `fixDamR > 0 && damage > 0` → `damage = max(1, maxHp * fixDamR / 100)`
4. 若 `disease > 0` 且 `damage > 0` → 用 `MobSkill(disease, level)` 走一遍 `applyEffect`
5. `attacker.mp -= conMP`

因此**怪物攻击带的诅咒/眩晕/缓速是通过 attack<N>/info 的 `disease`+`level` 施加的**，
数据里 268 个攻击带 `disease`。这与 MobSkill 的 120~136 走同一条 `applyEffect`。

⚠️ `disease` 指向的**不只是** 120~136：数据里实际用到的 id 是
`120,121,122,123,125,126,127,128,131,132,133,135,136,137,187`（`Counter` 见
`tools/audit_mob_skills.py` 输出）。因为走的是完整 `applyEffect`，127 会真的驱散、
131 会生成雾、187 会播 `customBossSeren/sacredBurst`。
137 **不在 `MobSkillType` 枚举里**（`from(137)` 返回空），这条攻击挂什么都没发生。
`skill=false` 传入，所以永远走单目标分支，不看 `lt/rb`。

### 2.4 `attackAfter` 的真实身份（2026-10-30 复核，结论收窄）

- 服务端（**已核，权威**）：只出现在 `LifeFactory:624-626`，
  `impactDelay = DataTool.getIntConvert("info/attackAfter", monsterAttackData, 0)`
  然后 `coolTime = KaringBossCompat.attackCooldownMillis(mid, i, impactDelay)`。
  也就是说它**是攻击冷却的兜底值，不是命中延迟**。
- 客户端（**已核到读取，未核到消费**）：`0x68107B push 0x676`(=`attackAfter`) 取串后，
  `0x6810BA mov dword ptr [eax+0x8C], esi` 存进攻击信息对象 `+0x8C`。
  读 `+0x8C` 的执行点尚未定位，所以「它是不是命中帧毫秒」**仍然未知**。

结论：**源端没有任何一处「按 attackAfter 延迟结算伤害」的代码**。
服务端排伤害这件事根本不存在——怪物攻击伤害由客户端算完后用 `TakeDamage` 上报。
因此本工程把 `attackAfter` 当命中时刻，只能记为**表现层选择**，
写代码时必须注释清楚它不是源服规则（`NativeMobController.TryAttack` 已如此标注）。

数据里 2193/2219 个攻击有 `attackAfter`，值域从 0 到 2000+，缺节点时默认 0。
攻击动画锁（源端 `Monster.applyAnimationIfRoaming`）用的是
`attack<N>` 全部子节点 `delay` 之和，与 `attackAfter` 无关。

### 2.5 数据统计（`native_mob_skills.json`）

| 攻击容器 | 含该节点的攻击数 |
| --- | ---: |
| `hit` | 2211 |
| `range` | 2217 |
| `effect` | 331 |
| `ball` | 210 |
| `areaWarning` | 131 |
| `effect0` | 51 |
| `effect1` / `effect2` | 6 / 6 |
| `areaAttack` | 2（EXE 不读） |

标量字段：`type` 1649、`attackAfter` 2193、`conMP` 999、`magic` 826、`PADamage` 604、
`effectAfter` 482、`disease`+`level` 268、`knockback` 134、`deadlyAttack` 79、
`tremble` 39、`MADamage` 30、`doFirst` 29、`mpBurn` 10、`fixDamR` 3。

`range` 还可以带 `start`/`areaCount`/`attackCount` 三兄弟，构成**多波次区域攻击**
（例 8150302 attack1：`start=-7, areaCount=15, attackCount=4`；2400040 attack1：
`start=-7, areaCount=15, attackCount=7`）。这类攻击配 `areaWarning` 预警画布，
属于「场景技能」在数据上的形态。**执行语义（几波、间隔、每波判定）尚未核实**，
见 §8。

### 2.6 `range` 容器的全部子节点（2026-10-30 全量扫描）

遍历 2218 个带 `range` 的攻击行，子节点分布：

| 子节点 | 出现数 | 说明 |
| --- | ---: | --- |
| `lt` / `rb` | 1820 / 1820 | 矩形攻击区（Point） |
| `sp` / `r` | 405 / 399 | **Point + 整数半径**，解析器紧挨着 `lt/rb` 读它们（`0x680735 push 0x66A 'sp'`、`0x680830 push 0x66B 'r'` → `0x68086F mov [eax+0x30],esi`） |
| `start` / `areaCount` / `attackCount` | 131 / 131 / 131 | 多波次 |
| `lt2` / `rb2` | 3 / 3 | 第二个矩形（8880000 attack4、8880832 attack3/5） |
| `reverse` | 2 | 8870000 attack5、9900000 attack5 |
| `delay` | 2 | 9300367 attack2、9390804 attack1 |
| 纯数字子节点 | 2 | `36`、`11`，明显是作者笔误节点 |

**`lt/rb` 与 `sp/r` 基本互斥**（1819 + 399 ≈ 2217，两条都有的只有 1 条），
即每类攻击只有一种区域形态。运行期由 `MobAttackArea.TryParse` 先试 `lt+rb`、
再试 `sp+r`，两种都落空才算「无范围」，见 `Assets/Scripts/Field/MobAttackArea.cs`。

作者的数值形态也印证了这个读法：`sp` 恒为小的躯干偏移（`sp.y` 全库落在
`-243..6`，`sp.x` 落在 `-530..659`），`r` 中位数 280、常见值 200~300；而 `lt/rb`
是真正的四点矩形（`2230108 attack1` 是 `lt(-99,-89) rb(113,42)`）。

**`+0x28` 这一片是 union，按 `type` 分两套布局 —— 2026-10-30 递归下降后定案（§2.2a）。**
之前这里记的「反汇编失步、偏移表不可信」是**错误结论**，已撤销：

- `type 0`：`lt/rb` 经 `0x40FD6E` 写成 **4 分量结构**（每分量 12 字节，
  值为「2 个 hash + 和」，`0x4165B1` 里带 `0xBAADF00D` 未初始化哨兵），占 `A+0x28..0x57`。
- `type 1/2`：`sp.x`/`sp.y` **明文**写 `A+0x28`/`A+0x2C`，`r` 明文写 `A+0x30`。
- `type 3/4`：`start`/`areaCount`/`attackCount` 明文写 `A+0x28`/`+0x2C`/`+0x30`，
  `A+0x34 = rb.y-lt.y`，真实矩形写 **`A+0x38`**（`0x38 + 4×12 = 0x68` 正好接上 `bulletSpeed`）。

所以 133 条「同时有 `lt/rb` 且有多波字段」的攻击**不冲突**：多波分支的矩形落在 `+0x38`，
不会覆盖 `+0x28` 的计数三连。`[ebp-0x18]` 也不是「只被写过 0」——它是 `A`，
由 `0x681EC6` 经 `esi` 间接写入（`0x681EE9 mov [esi+4], eax`），字符串匹配看不到。
入口改记 **`0x67FD81`**（`0x67FE7F` 只是第一次取串的位置）。

### 2.7 `type` 决定几何与表现（2026-10-30 数据交叉验证，结论最硬的一张表）

`type` 不是可忽略的元数据。把 2219 条攻击按「`type` × 区域形态 × 容器」交叉统计，
分布干净到可以直接当规则用（`tools/audit_mob_skills.py` 第 10 组断言会锁住它）：

| `type` | 条数 | 区域 | `ball` | `areaWarning` | 语义 |
| ---: | ---: | --- | --- | --- | --- |
| `0` / 缺省 | 1143 + 555 + 8 | `lt/rb` 矩形 | 无 | 无 | 近身/普通矩形攻击 |
| `1` | 183 | `sp/r` 圆 | 无 | 无 | 圆形范围攻击（无弹道） |
| `2` | 202 | `sp/r` 圆 | **有** | 无 | **发射物**攻击（圆 + `ball` 弹道） |
| `3` | 101 | `lt/rb` 矩形 | 无 | **有** | **多波次区域攻击**（预警 + `start/areaCount/attackCount`） |
| `4` | 10 | `lt/rb` 矩形 | 无 | **有** | 同上 |

- 只有 `type 1/2` 出现「仅 `sp+r`、无 `lt/rb`」（183 + 202 + 13 条缺省，共 398），
  **没有一条 `type 0/3/4` 走圆形**。
- `ball` 容器共 210 条：204 条是 `type 2`，6 条缺省 `type`，**全部落在圆形那一组**。
- 反方向有 2 条例外：`type 2` 却给了 `lt/rb` 矩形。它们不带 `ball`，
  所以「type 2 ⇒ 圆」不是绝对等式，**运行期按节点有无判断（先 lt/rb 再 sp/r）才对**，
  `type` 只用来解释语义、不用来选几何。
- 131 条带 `areaCount` 的攻击里，`attackCount <= areaCount` **恒成立**，
  且 `areaCount` 被解析器夹到 `<= 0x20`（`0x680931 cmp eax,0x20`）。
- `start` 恒为 `-16..-1` 的负小整数。
- **上面两条的语义已在第五轮破译，见 §2.8**（`start` = 最左波次的带号，`attackCount` = 波数上限，
  参数位掩码决定哪几波生成）。这里保留旧记录只为说明「曾按数据拟合出方向、后被代码证实」。

`areaCount` / `attackCount` / `start` 的**消费代码位置**：`0x66DF02..0x66E0F3`（详见 §2.8）。


---

### 2.8 攻击执行的四种形态（EXE 已核，2026-10-29 第五轮）

这是整个怪物攻击系统的执行内核：`0x66D9C0`（646 条指令，递归下降无空洞）
把一条攻击行 `A` 按 `[A+0xC]` 分派成五种去向。**这张表是「攻击怎么打到人」的唯一权威。**

```asm
0066DA2B  call 0x529E7B          ; A = GetAttackInfo(idx)
0066DAAD  mov  ecx, [esi+0xC]    ; A->type
0066DAB0  cmp  ecx, 1            ; ── type 1 圆形
0066DAB3  jne  0x66DCA8
0066DCA8  cmp  ecx, 2            ; ── type 2 发射物
0066DCAB  jne  0x66DE73
0066DE73  cmp  ecx, 0            ; ── type 0 矩形
0066DE75  jne  0x66DF02
0066DF02  cmp  ecx, 3 / 4        ; ── type 3 预警 / type 4 实体
```

#### 2.8.1 攻击区对象（所有形态共用的落点容器）

`0x672E65`（`ebx+0xD8` 池）取一个节点，布局：

| 偏移 | 内容 | 写入点 |
| ---: | --- | --- |
| `+0x00` | **触发时间** = `A+0x8C`(`attackAfter`) + 基准时刻 | `0x66DE88` / `0x66E009` |
| `+0x04` | **模式** = `A+0x0C`(`type`) | `0x66DE96` / `0x66E017` |
| `+0x08..0x17` | **世界矩形**（4 × int，`0x4144D4` 解出） | `0x66DEBC` / `0x66E02D` |
| `+0x18` | **朝向**（±1） | `0x66DE60` / `0x66DEF4` |
| `+0x1C` | 序号 | `0x66DEFA` |
| `+0x24` | 附加标记 | `0x66DE6B` |

生成后统一做两件事：
- 朝向为左（`dir==0`）时镜像翻转矩形（`neg [obj+8]` / `neg [obj+0]` + `0x45730D` 归一化）。
- `[0xBF0454]` 把局部矩形变换到世界坐标（传 mob 坐标）。

这些对象挂在链表 `[mob+0xE4]` 上，`0x666362`（`DoAttack`）里 `0x66718A mov eax,[eax+0xE4]`
取当前节点，然后 `esi+8` 当查询矩形喂 `0x95B734` 收集目标。

#### 2.8.2 type 0 — 矩形，攻击决策 + 动作生成

两处消费：

1. **「现在该不该出近身攻击」的决策循环**（`0x66E636`，117 条指令；循环体 `0x66E6B6`）：
   `i = 0..[MobData+0x218]-1`，逐条 `GetAttackInfo(i)`，`if (A->type != 0) continue`（`0x66E703`），
   用 `RECT(A+0x28)` 做 `[0xBF0454]`（局部→世界）+ `[0xBF04A8]`（与**本地玩家包围盒**相交，
   `0x66E6A7 call 0x45183B` 取玩家盒）判定；一旦相交就 `push 0xC1` 造动作事件并发网络包
   （`0x66E757..0x66E79B`）。**只有 type 0 走这里**——即「近身攻击的触发条件是站进矩形」。
   它**不是**持续伤害：整条链路没有 `0x670C63` 的伤害结算。
2. **动作触发**（`0x66DE73`）：`src = A+0x28`，同样 `0x4144D4` 解码，写进攻击区对象的
   `+0` 触发时刻（`attackAfter + 基准`）/`+4` 模式/`+8` 世界矩形/`+0x18` 朝向/`+0x1C` 序号，
   真正结算留给 `0x666362`（`DoAttack`）。

#### 2.8.3 type 1 — 圆，圆心 `mob + (sp.x×朝向, sp.y)`，半径 `r + 10`，比的是**目标包围盒**

```asm
0066DB60  ; 先取目标包围盒
0066DB68  call 0x7A38D6           ; 实体包围盒 → [ebp-0x2C..-0x20]
0066DB95  call 0x664559           ; 兜底：目标不在实体表时用它的世界包围盒
0066DBA5  mov  eax, [esi+0x28]    ; A+0x28 = sp.x
0066DBAA  neg  eax                ; 仅当朝向为左
0066DBBB  add  eax, mob.x         ; 圆心 X
0066DBCB  add  ecx, [esi+0x2C]    ; 圆心 Y += sp.y（不受朝向影响）
0066DBDC..0066DC1B  ; 把圆心夹进目标包围盒（X 取朝向那一侧的内缩 10px 边，Y 夹到 [top,bottom]）
0066DC1E  sub  eax, [ebp-0x3C]    ; dx = 夹后点 - 圆心
0066DC21  sub  edi, ecx           ; dy
0066DC39  faddp                   ; dx²+dy²
0066DC42  call 0xA622D4           ; sqrt
0066DC5D  mov  eax, [esi+0x30]    ; A+0x30 = r
0066DC60  add  eax, 0xA           ; +10 px 容差
0066DC69  fcomp qword [ebp-0x3C]  ; r+10 ? dist
0066DC6F  jb   0x66E135           ; dist > r+10 → 不命中
```

三条硬结论：
- **圆心 = `mob + (sp.x × dir, sp.y)`**，`sp.y` 不吃朝向。
- **判定半径 = `r + 10`**，`10` 是写死的字面量，不是配置。
- **判定对象是目标包围盒，不是脚点**：`0x7A38D6`/`0x45183B` 取的是目标的世界矩形，
  圆心先被夹进这个矩形（`0x66DBDC..0x66DC1B`），再量「圆心 → 夹后点」的距离。
  夹后点就是矩形上离圆心最近的那一点，所以这等价于**圆 × AABB 相交**（X 侧用朝内缩 10px
  的边、Y 侧夹到上下边）。第五轮把这里读成「点判定」是错的：那轮只看了 `0x66DC1E`
  之后的三行浮点，没往上看 `0x66DB68` 的取盒调用。

#### 2.8.4 type 2 — 发射物：起点用同一套 `sp`，方向 `atan2`

```asm
0066DCA8  cmp  ecx, 2
0066DCB1  lea  eax, [esi+0x7C]   ; A+0x7C = 弹道对象列表
0066DCD7  mov  eax, [esi+0x28]   ; sp.x（同样吃朝向）
0066DCED  add  eax, [ebp+8]      ; 起点 X = mob.x + sp.x
0066DCFD  add  eax, [esi+0x2C]   ; 起点 Y = mob.y + sp.y
0066DD04  movsx edi, word [ebp+0x12]  ; 目标 X（16 位！）
0066DD0B  movsx eax, word [ebp+0x10]  ; 目标 Y
0066DD55  idiv [ebp+0x10]        ; 斜率
0066DD67  call 0xA6203F          ; atan2
```

- **`sp` 在 type 2 里是「弹道发射点偏移」**，和 type 1 的「圆心偏移」复用同一槽位。
- 方向由 `atan2(目标 - 起点)` 得出，目标以 **16 位**打包传入（`movsx word`）。
- 飞行中的命中在 `0x6670D1`：每 tick 用弹道当前位置构造 **1×1** 矩形
  （`[ebp-0x98]=y`、`[ebp-0x94]=x`、`[ebp-0x90]=y+1`、`[ebp-0x8C]=x+1`），再 `0x95B734` 收集。
  **逐点判定，不是扫过线段** → 高速弹道可能穿模，这是源端真实行为，不要"修正"。

#### 2.8.5 type 3 / type 4 — 多波，`start/areaCount/attackCount` 的真面目

```asm
0066DF02  cmp  ecx, 3 / 4
0066DF12  cmp  [esi+0x2C], 0     ; A+0x2C = areaCount
0066DF1B  jle  <跳过>            ; areaCount <= 0 → 整条不生成
0066DF21  loop:
0066DF24  cmp  eax, [esi+0x30]   ; i vs A+0x30 = attackCount
0066DF27  jge  <结束>
0066DF30  eax = 1 << waveIndex   ; 0x66DF2D..30
0066DF35  test [ebp+0x10], eax   ; 参数位掩码
0066DF38  je   <下一波>          ; 这一波的位没置 → 不生成
0066DF4F  ecx  = [esi+0x28]      ; start（负数）
0066DF52  ecx += waveIndex
0066DF57  imul ecx, [A+0x34]     ; × 波间距
0066DF5F  ecx += spacing/2       ; 该波带中心
0066DF6C  imul ecx, dir(±1)      ; 按朝向镜像
0066DF76  centreX += ecx
```

**语义定死：**

| 字段 | 真实含义 |
| --- | --- |
| `start` | **最左那一波的带号**，负数（`-16..-1`）。这就是 `areaCount ≈ 2|start|+1` 的来源 |
| `areaCount` | 总区域数，**只当 `>0` 门槛用**，不参与循环 |
| `attackCount` | **循环上限 = 实际生成的波数**，被夹 `<= 0x20` 是因为 `1<<i` 掩码只有 32 位 |
| 调用参数 | **波次位掩码**，第 i 波是否生成由 `mask & (1<<i)` 决定 |

- **`type 3` 只画预警**：`0x66DF9A` 走 `0xA456F9`，垂直范围 `mob.y ± 0x64`（±100px）。
- **`type 4` 是实际伤害区**（`0x66DFCF` 之后走攻击区生成 + 结算）。
- 所以「3 预警 / 4 落地」不是命名猜测，是分支事实。

#### 2.8.6 一句话总结

| type | 形状 | 触发 | 判定 |
| ---: | --- | --- | --- |
| 0 | `A+0x28` 矩形 | 进矩形即触发攻击动作（`0x66E636` 决策）+ 动作生成区 | 包围盒 vs 矩形相交 |
| 1 | 圆，心 `mob+(sp.x·dir,sp.y)`，半径 `r+10` | 动作生成时一次性 | 圆心 → **目标包围盒最近点** ≤ r+10 |
| 2 | 弹道，起点同上，方向 `atan2` | 动作生成 | 每 tick **1×1** 矩形 vs 目标包围盒 |
| 3 | 多波矩形（`A+0x38` + 带偏移） | 动作生成 | **只画不结算** |
| 4 | 同上 | 动作生成 | 同 type 0 结算 |


---

## 3. `Skill/MobSkill.img`

### 3.1 结构

```
MobSkill.img/<type>/level/<n>/{mpCon,interval,time,prop,x,y,hp,count,limit,
                              direction,summonEffect,lt,rb,elemAttr,0..N(召唤ID),
                              affected,mob,tile,effect,hit,screen,summon}
```

| 字段 | 单位/含义（源服 `MobSkillFactory`） |
| --- | --- |
| `mpCon` | MP 消耗，默认 0 |
| `interval` | **秒**，冷却；`SECONDS.toMillis` |
| `time` | **秒**，状态持续；`SECONDS.toMillis` |
| `prop` | 百分比，默认 100；`prop/100f`，`prop==1.0` 恒成功 |
| `x` / `y` | 各类型的数值（增益幅度、治疗量、反伤百分比、毒伤…） |
| `hp` | **使用门槛**：怪物当前 HP% ≤ 该值才可放；**`<=0` 视为 100** |
| `count` | 目标数（目前只有 SEDUCE 用它限制人数） |
| `limit` | 召唤数量上限，**默认 0 = 不允许召唤** |
| `summonEffect` | 召唤时的 `spawnMonsterWithEffect` 编号 |
| `lt`/`rb` | 相对施法者的矩形；**不按朝向翻转**（见 §4.6） |
| `direction` | **客户端读、服务端不读**；本数据集 706 个等级里 **0 条**写了它 |
| `0..N` | 召唤怪物 ID 列表 |

`x`/`y` 服务端读，`direction` 只有客户端读（`0x762B35 push 0xEC5` → `0x762B68 mov [esi+0x3C]`）。

### 3.2 容器语义（数据事实 + 命名）

| 容器 | 语义 | 出现类型 |
| --- | --- | --- |
| `affected` | 玩家身上的状态图标序列 | 120~136（玩家减益） |
| `mob` | 怪物身上的状态图标序列 | 100~115、140~157（怪物增益/免疫/反伤） |
| `effect` | 施法特效 | 100~115、140/141、157、200 |
| `hit` | 命中特效 | 125、174、176 |
| `tile` | 地面薄雾贴片 | 131 |
| `screen` | 屏幕层特效（25 帧）——**客户端不读**，见 §3.5 | 176（唯一） |
| `summon` | 召唤特效 | 未在 51 类型中出现 |

`176`（阿卡伊勒屏裂）数据实例：`mpCon=1, interval=10, time=0, prop=100,
x=999999, hp=80, count=30`，`screen` 25 帧 + `hit` 6 帧（`pos=3`）。`x=999999`
就是源服 `applyAkayrumScreenCrackDamage` 里的 `damage = max(1, getX())` ——
**伤害逻辑在服务端，画面在 `screen`；但这条 `screen` 客户端从不加载**。

### 3.3 51 个类型，44 个被怪物实际引用

`MobSkill.img` 定义 51 个 type；`Mob/*/info/skill` 实际引用 505 条 `(type, level)`，
覆盖 44 个 type。引用次数前列：`200` 197 条、`114` 37、`128` 18、`123` 16、
`140` 16、`100` 15、`127` 13、`129` 13、`131` 13、`120` 12、`141` 12。

**两个 type 不在 `MobSkillType` 枚举里**：

- `105`：`9309201` 引用，`MobSkill.img/105/level/1` 只有 `effect`。
- `169`：`9460022/9460023/9460024` 引用。

源服 `LifeFactory:596` 是 `MobSkillType.from(skillId).orElseThrow()`，所以这两个怪物
在源服**加载 stats 时就会抛 `NoSuchElementException`**。这是源端数据/枚举不一致，
不是本工程要「顺手补上」的功能。当前运行时必须保持「不崩、记一条警告」。

`115 HASTE_M`、`142 HARD_SKIN`、`134 STOP_POTION`、`135 STOP_MOTION`、`136 FEAR` 在枚举里
但 `MobSkill.applyEffect` 没有对应 `case`，**源服就是空操作**。不要给它们补效果。

### 3.4 客户端加载器（EXE 已核，2026-10-30）

`0x761A76 push 0xAE8 'Skill/MobSkill.img'` 是**根**；单个等级条目由
`0x7627C7..0x7632A8` 这段装载。要点：

- 等级数组元素大小 **`0x5C`**（`0x7627D6 imul eax,eax,0x5c`；`lea esi,[eax+ecx-0x5C]`，
  即 1-based，`level/1` 落在数组首元素）。
- 注册表访问器 `0x7632F2`（`ecx = [0xBE78DC]` 全局 MobSkill 注册表）返回条目，
  **等级数组指针在 `entry+4`**，元素个数在 `[ptr-4]`。
- 客户端等级结构偏移（依据 `mov [esi+disp], eax` 的写入点）：

| 偏移 | 节点 | 写入点 |
| --- | --- | --- |
| `+0x00` | `hp` | `0x76281C` |
| `+0x04` | `conMP` | `0x762878` |
| `+0x08` | `interval` | `0x76293E` |
| `+0x0C` | `time` | `0x7628DB` |
| `+0x10` | `prop` | `0x76299B` |
| `+0x14` | `x` | `0x7629F8` |
| `+0x18` | `y` | `0x762A55` |
| `+0x2C` | `summonEffect` | `0x763103` |
| `+0x30` | `limit` | `0x763160` |
| `+0x34` | `count` | `0x762AB3` |
| `+0x38` | `count != 0`（布尔归一化） | `0x762B0E`（`neg/sbb/neg`） |
| `+0x3C` | `direction` | `0x762B68` |
| `+0x40` | `elemAttr` | `0x762BC8` |
| `+0x58` | 召唤 ID 列表（遍历数字子节点 push） | `0x763215..0x76321F` |

`+0x44` 被 `0x66C215` 当**字符串**读（`mov eax,[ecx+0x44]`，引用计数在 `+8`），
但装载器**没有**写它 → 运行时恒为空。这与数据里 133 条 `level/*/info`（韩文标签，
如 `100/21` 的 `'넥스1/넥스2'`、`200/239` 的 `'贝伦'`）对得上：**`info` 是死数据**。

三处按等级索引的消费点（`imul ...,0x5C`）：`0x66C161`（取等级 + 名字）、
`0x66E892`、`0x66EBB0`。都只是取条目，**没有定位到执行 `applyEffect` 的对应物**。

### 3.5 客户端真正读取的节点（白名单）与死节点

按上述加载器逐个字符串取串顺序，客户端对 `level/<n>` **只读**这 19 个名字：

```
hp conMP time interval prop x y count direction elemAttr lt rb
effect hit mob affected tile summonEffect limit  +  纯数字子节点（召唤 ID 列表）
```

全量扫描 706 个等级的**所有**子节点名后，以下都是「存在但客户端不读」：

| 节点 | 出现数 | 位置 |
| --- | ---: | --- |
| `info`（韩文标签） | 133 | 遍布 100~200 |
| `mob0` | 35 | 仅 110~114（`mob` 之外的第二套图标） |
| `tremble` | 7 | 176/1..6、200/249 |
| `screen` | 6 | **只有 176/1..6** |
| `lua` | 6 | 176/1..6 |
| `summonOnce` | 6 | 176/1..6 |
| `fieldScript` | 6 | 200/239、240、246、247、248、249 |
| `linkHP` | 4 | 200/243、246、247、248 |
| `afterEffect` | 4 | 200/246、247、248、250 |
| `rangeGap` | 2 | 200/241、242 |
| `affectedPhase` / `affected_after` | 1 / 1 | 174/1 |
| `HP` / `head` / `face` / `bossHeal` / `targetType` / `exchangeAttack` | 各 1 | 200/140、184、250、244、236 |
| `inteval`（笔误） | 1 | 132/2 |

**这意味着「全屏技能」在 v83 客户端里没有入口**：`MobSkill.img/176/*/screen`（25 帧的
阿卡伊勒屏裂）虽然有画，但 `screen` 不在读取白名单里。同理 `fieldScript`（贝伦）、
`lua`、`summonOnce`、`linkHP`、`afterEffect` 服务端也一处不读（Java 全仓 grep 0 命中）。
这些只能是**更晚版本的客户端/服务端产物**，本工程不得据其名称编效果。

真正能拿来做「全屏/场景表现」的只有：`effect`（自身）、`hit`（命中）、`mob`（怪身上图标）、
`affected`（玩家身上图标）、`tile`（地面雾）。

---

## 4. 服务端触发链（权威）

### 4.1 谁来「决定放技能」

**客户端决定，服务端校验。** `MoveLifeHandler`：

```
activity 原始字节 -> rawActivity & 0xFF>>1（高位为符号时）
24..41 -> 攻击，castPos=(rawActivity-24)/2      （attack1..9）
42..61 -> 技能，skillActionIndex=(rawActivity-42)/2（skill1..10）
```

技能分支读包里的 `skillId`/`skillLv`，然后
`Monster.resolveCastSkill(skillId, skillLv, skillActionIndex)`：

1. 若 `(skillId, skillLv)` 在 `info/skill` 列表里 → 用它；
2. 否则按 **skillId**（忽略等级）找第一个同 type；
3. 否则按 **action 下标**取 `info/skill` 列表第 N 项；
4. 都不中 → `null`，本次施法被丢弃。

`LifeFactory:589-594` 用 `LinkedHashSet` 保序，**注释明确要求保序**，因为第 3 步依赖下标。
我们的 `NativeMobController` 用 `List<Cast>` 顺序加载，顺序与 `info/skill/0,1,2...` 一致，
等价。

### 4.2 `canUseSkill`

```
toUse == null                     -> false
怪物身上有 SEAL_SKILL(157)        -> false
toUse 是反伤类(143/144/145) 且已有 WEAPON_REFLECT 或 MAGIC_REFLECT -> false
usedSkills 已含该 (type,level)    -> false       // 冷却中
mp < mpCon                        -> false
否则 -> true，并 usedSkill(): mp -= mpCon，登记冷却
```

- 冷却时长 = `KaringBossCompat.skillCooldownMillis` 覆盖后仍是 `MobSkill.interval*1000`。
- 冷却从**施放瞬间**开始，不是从生效瞬间。
- 注意 `SEAL_SKILL` 只封技能，**不封普通攻击**。

### 4.3 服务端「下一个可放技能」提示

`MoveLifeHandler:166-184`：只有当本次不是技能且无后续 nibble 时，服务端才在
`moveMonsterResponse` 里附带 `nextSkillId/nextSkillLevel`。挑选规则：

```
getSkillsInRandomOrder()                      // 洗牌
  + Karing/Lucid 特殊 Boss 把 183..187 提到最前
逐项：
  canUseSkill(candidate, apply=false)
  && candidate.getHP() >= 当前HP%
  && mobMp >= candidate.getMpCon()
```

`getHP()` 是**最高 HP 百分比门槛**：HP 越低，能用的技能越多。
不在列表里的技能，客户端即使请求也会被 `resolveCastSkill` 拒掉。

### 4.4 生效时机

```java
if (animationTime > 0 && type != BANISH)
    toUse.applyDelayedEffect(player, monster, true, animationTime);
else
    toUse.applyEffect(player, monster, true, banished);
```

`animationTime` = `Mob/<id>.img/skill<action>` 下**所有子节点 `delay` 之和**，
`DataTool.getIntConvert("delay", …, 0)`——**默认 0、不取绝对值**。
若为 0 或负数 → 立即生效。BANISH(129) 永远走立即分支，然后在
`MoveLifeHandler:244-248` 广播之后再换图。

`MobSkill.applyDelayedEffect` 只检查 `monster.isAlive()` 就执行；
**没有「施法被打断就取消」的逻辑**。

### 4.5 `applyEffect` 分派表（全部 `case`）

| type | 效果 |
| --- | --- |
| 100/110/150 `ATTACK_UP*`/`PAD` | `WEAPON_ATTACK_UP = x` |
| 101/111/151 | `MAGIC_ATTACK_UP = x` |
| 102/112/152 | `WEAPON_DEFENSE_UP = x` |
| 103/113/153 | `MAGIC_DEFENSE_UP = x` |
| 114 `HEAL_M` | 见 §4.7 |
| 120 SEAL / 121 DARKNESS / 122 WEAKEN / 123 STUN / 124 CURSE / 125 POISON / 126 SLOW | `Disease.*` |
| 127 DISPEL | 目标 `dispel()`：取消所有 `isSkill()` 的 buff |
| 128 SEDUCE | `Disease.SEDUCE`；区域内最多 `count` 人 |
| 129 BANISH | 有 `info/ban` 才生效；收集目标，广播后换图 |
| 131 AREA_POISON | **只生成一个 mob 型雾，poison=false**，见 §4.8 |
| 132 REVERSE_INPUT | `Disease.CONFUSE`（反向操作） |
| 133 UNDEAD | `Disease.ZOMBIFY` |
| 140 PHYSICAL_IMMUNE | 若无 `MAGIC_IMMUNITY` → `WEAPON_IMMUNITY = x` |
| 141 MAGIC_IMMUNE | 若无 `WEAPON_IMMUNITY` → `MAGIC_IMMUNITY = x` |
| 143 PHYSICAL_COUNTER | `WEAPON_REFLECT=10, WEAPON_IMMUNITY=10`，reflection 记 `x` |
| 144 MAGIC_COUNTER | `MAGIC_REFLECT=10, MAGIC_IMMUNITY=10`，reflection 记 `x` |
| 145 PHYS+AND_MAGIC_COUNTER | 四个状态都设，reflection 记 `x` |
| 154 ACC / 155 EVA / 156 SPEED / 157 SEAL_SKILL | `ACC/AVOID/SPEED/SEAL_SKILL = x` |
| 174 / 177 | 纯视觉，空操作 |
| 176 AKAYRUM_SCREEN_CRACK | 森兰丸困难 → 35% 伤害；否则播 `customBoss/akayrum/screenCrack` 并 1100ms 后结算 |
| 183 / 184 / 185 / 187 | 播 `customBoss*/…`，1400ms 后按百分比扣血（184 固定 35%） |
| 188/201/…/170/(200,202,203) | 召唤 |
| 214 / 215 | 空操作（现代状态机，老客户端只有视觉） |
| 105/115/134/135/136/142/169 | **源服无 case = 空操作**（105/169 连枚举都没有） |
| 138/146/171/172 `CYGNUS_UNSUPPORTED_*` | 枚举里有、`switch` 里**没有 case**，同样空操作 |

**`applyEffect` 的第一行是 `if (!makeChanceResult()) return;`**，即 `prop` 判定在
动作已经播完之后，失败就什么都不发生（连冷却都已经扣了）。

### 4.6 范围矩形不翻转（重要）

`MobSkill.calculateBoundingBox`：

```java
new Point(lt.x + pos.x, lt.y + pos.y) .. (rb.x + pos.x, rb.y + pos.y)
```

**没有任何朝向镜像**。数据里 `125` 的 `lt=(-150,-90) rb=(0,0)`，意味着一只朝右站立的怪
放毒，只毒**它左边**那片矩形。这是源端既有行为，不能「修正」成翻转后的版本。
普通攻击的 `attack<N>/info/range` 是客户端本地判定，才有朝向翻转（`info/noFlip`）。

`getPlayersInRange` / `getObjectsInRange` 用同一个矩形，所以治疗、驱散、增益、
疾病、放逐的范围口径完全一致。

### 4.7 治疗 `HEAL_M`(114)

```java
if (lt != null && rb != null && skill)
    hps = (getX() / 1000) * (int)(950 + 1050 * Math.random());   // 整数除法先做
    范围内每只怪 heal(hps, getY())
else
    monster.heal(getX(), getY())
```

`x/1000` 是整数除法；`950+1050*rand` 取整后落在 950..1999。`y` 是回蓝量。

### 4.8 `AREA_POISON`(131) 不产生毒伤

```java
Mist mist = new Mist(box, monster, this);      // isMobMist=true, isPoisonMist=true
int mistDuration = x * 100;                    // x=800 -> 80 秒
monster.getMap().spawnMist(mist, mistDuration, false, false, false);
//                                            ^poison ^fake  ^recovery 全是 false
```

`MapleMap.spawnMist` 只有 `poison==true` 才注册 `poisonTask`（2500ms 周期给范围内怪物上
中毒状态）。**mob 雾传的是 `false`，所以服务端只有「雾存在 80 秒」这一个事实，
没有任何毒伤结算。** 谁都不扣血。

### 4.9 召唤

```
map.getSpawnedMonstersOnMap() < 80
skillLimit = limit（MapId.isDojo 时 Integer.MAX_VALUE）
summonLimit = min(召唤ID数量, skillLimit - 该怪已召唤数)
若 summonLimit >= 1：Collections.shuffle(ids) 后取前 summonLimit 个
8500003(HIGH_DARKSTAR)：fh = ceil(rand*19)，ypos = -590
8500004(LOW_DARKSTAR)：xpos = mobX + rand(1000) - 500
8510100(BLOODY_BOOM)：1/5 概率 → ypos = 78、xpos = rand(5) + (1/2 概率再 +180)；否则同上随机 ±500
地图 220080001 / 230040420：越界时**重新随机回边界内**（不是 clamp）
spawnMonsterWithEffect(toSpawn, spawnEffect, pos)
8500004 走 spawnFakeMonster
```

`limit` 缺省 0 意味着「没有 `limit` 节点的召唤技能什么也召不出来」——数据里 197 个怪
引用了 type 200，靠的都是各自 `level` 下的 `limit`。

道场上限是 `MapId.DOJO_MAX = 925033804`（`DOJO_MIN = DOJO_SOLO_BASE`），
不是「925040000 之前都算道场」。

---

## 5. 玩家侧结算

### 5.1 免疫 → 全部伤害变 1

`AbstractDealDamageHandler:394-402`：魔法攻击查 `MAGIC_IMMUNITY`，物理查
`WEAPON_IMMUNITY`，命中则 `Collections.fill(onedList, 1)`——**是 1 不是 0**，
飘字仍显示 1，不显示 Miss。

### 5.2 反伤只认 145

`AbstractDealDamageHandler:665-682`：

```
WEAPON_REFLECT 且 非魔法攻击 且 非 Hero.MONSTER_MAGNET
    -> 遍历怪物 info/skill 里 type==145 的条目 -> player.addHP(-x)
MAGIC_REFLECT 且 魔法攻击 且 非 MONSTER_MAGNET
    -> 同样只找 145 -> player.addHP(-y)
```

关键两点：

1. **只看 145 的 `x`/`y`**；143/144 自己设的 `x` 不参与扣血。
2. **遍历的是怪物配置的技能列表**，不是「当前生效的状态」。一只只有 143 没有 145 的怪，
   即使挂上了 `WEAPON_REFLECT` 也不会反伤。
3. 反伤**不减免本次伤害**；减免来自同一技能附带的 `WEAPON_IMMUNITY`（伤害变 1）。

### 5.3 玩家自带的反弹

- `TakeDamageHandler:243-250`：`damagefrom == -1`（接触伤害）且玩家有 `POWERGUARD`
  → `bounce = damage * pg / (boss ? 200 : 100)`，上限 `怪 maxHP/10`，
  **先从玩家伤害里扣掉 bounce 再打到怪身上**。
- 魔法反射 `MANA_REFLECTION` 只在 `damagefrom != -1 && != -2`（即怪的攻击技能）时触发，
  职业 212/222/232，`bounce = damage * x / 100`，上限 `怪 maxHP/5`，**不扣玩家伤害**，
  且 `!attacker.isBoss()`。

### 5.4 疾病

`Character.giveDebuff`：

- 已有同名疾病 → 忽略（不刷新时长）。
- 同时最多 **2 个**疾病。
- 圣盾（`Bishop.HOLY_SHIELD`）挡掉除 **SEDUCE/STUN** 以外的全部疾病。
  注意 `MobSkill.applyDisease` 的区域分支又自己挡了一次圣盾，且**连 SEDUCE/STUN 一起挡**；
  两处口径不一致是源端既有的，不要统一。
- 时长 = `MobSkill.time * 1000`；下发给客户端的数值是 `x`。
- `SEDUCE` 会强制取消坐椅子（`sitChair(-1)`）。

各疾病的服务端可见效果：

| 疾病 | 服务端事实 |
| --- | --- |
| 124 CURSE | `gainExp`: `gain *= 0.5`（个人经验与队伍经验都减半） |
| 133 ZOMBIFY | `applyHpMpChange` 里允许 HP 扣到 ≤0（即允许被治疗技能打死） |
| 120/121/122/123/125/126/128/132 | 服务端只记录状态、下发时长；**具体表现由客户端算** |

### 5.5 怪物 MP

`MapleMap.mobMpRecovery`：每 `respawn_interval`（SQL 默认 10000ms）给存活怪恢复
`level` 点 MP，封顶 `maxMP`。源 Java 不读 `info/mpRecovery`。

---

## 6. 客户端表现链路

### 6.1 怪物放技能时客户端查的路径

**只有 `Skill/MobSkill.img/...` 这一组是真的**（`0x762DC8/0x762E80/0x762F38` 三次取串）：

```
Skill/MobSkill.img/%03d/level/%d/effect      // 技能特效
Skill/MobSkill.img/%03d/level/%d/hit         // 命中特效
Skill/MobSkill.img/%03d/level/%d/mob         // 怪物身上状态图标
```

`affected` / `tile` 走**节点名**直取（`0x762FA2`、`0x763037`），不拼路径。

按**怪物**取的那一组格式串（2752..2754）**是死的**，全立即数扫描 0 引用，见 §2.2b：

```
Mob/%07d.img/info/skill/%03d/effect          // 0 引用
Mob/%07d.img/info/skill/%03d/hit             // 0 引用
Mob/%07d.img/info/skill/%03d/mob             // 0 引用
```

按**动作**取的那一组是活的（`0x680xxx`，`%s` = `attack1..N`）：

```
Mob/%07d.img/%s/info/effect                  // 活
Mob/%07d.img/%s/info/hit                     // 活
Mob/%07d.img/%s/info/ball                    // 活
Mob/%07d.img/%s/info/areaWarning             // 活
Mob/%07d.img/%s/info/areaAttack              // 0 引用（死）
```

`attack1`..`attack8`/`attackF`、`skill1`..`skill16`/`skillF` 两组动作名都在字符串池里
（id 1056..1064、2735..2751），由 `0x4A6EE5`/`0x4A7101` 附近的动作名表构建。

### 6.2 动作

`info/skill/<n>/action` 指定用 `Mob/<id>.img/skill<action>` 播。缺省 = 行号+1。
`LifeFactory` 用同一个 `skill<action>` 求服务端生效延迟，两份必须一致。

### 6.3 全屏 / 场景技能（结论：数据有、链路没有）

- **状态图标**：`affected` 挂在玩家头顶，`mob` 挂在怪物头顶 —— 这两个是**活**的。
- **地面雾**：`131/tile` —— 活。
- **`screen`（全屏覆盖）**：`176/1..6` 各有 25 帧，但**客户端加载器不读 `screen`**
  （§3.5 白名单），服务端 Java 也不读。**当前这条链路是断的**，不要实现成「阿卡伊勒屏裂」。
- **多波区域**：`attack<N>/info/range/{start,areaCount,attackCount}` + `areaWarning` 画布
  —— `areaWarning` 是活的，三兄弟的执行语义未核（§8）。
- **Boss 视频**：不走 MobSkill.img，走 `Map/Effect.img/<FIELD_EFFECT>/0` 的 7×5 像素标记，
  由 `KaringSceneCompat.dll` 映射成 MCV 编号（详见 [custom-dll-systems](custom-dll-systems.md)）。
  这是「mcv 技能」的真实入口，不是 MobSkill 字段。

### 6.4 远程弹道

`attack<N>/info/ball` 是弹道画布（210 个攻击带）。**生成点已核、飞行/命中未核**：

- 生成器 `0x670F41`（从动作派发表 `0x67936D` 的 `0xFA` 分支进来）：
  先读 `[A+0x78]` 的容器（`mov eax,[eax]; test; mov eax,[eax]; mov eax,[eax-4]; shr eax,1`
  → 空则整段 return，即「没有弹道画布就不发射」）；再用 `0x664559(obj,1,&rect)` 取宿主
  矩形，`rand() % (right-left) + left`、`% (bottom-top) + top` 取**矩形内的随机点**作为
  起点（`0x670D17..0x670D5A`）。所以弹道起点带随机散布，不是一个固定像素。
- 命中判定走的是 `0x95B734` 的「弹道点转 1×1 矩形」查询（`0x6670D1`：
  `[ebp-0x98]=x; [ebp-0x90]=x+1; [ebp-0x94]=y; [ebp-0x8c]=y+1`），
  **即弹道是逐点命中，不是扫过线段**。速度 `bulletSpeed`（`A+0x68`）的读点还没定位。

### 6.5 攻击执行的消费点（2026-10-30 新核，全部带地址）

这一节把「`attack<N>/info` 的字段到底被谁读」串起来了，可以替代之前的多条「未定位」。

| 环节 | 地址 | 行为 |
| --- | --- | --- |
| 取攻击结构 | `0x529E7B` | `GetAttackInfo(idx)`：`idx<0 \|\| idx>=[MobData+0x218]` 返回 0；懒解析后缓存在 `[MobData+0x238+idx*8]`（`0x234` 是 holder 基址） |
| 结构大小 | `0x682449 push 0xA4` | `MobAttackInfo` = **0xA4 字节**，ctor `0x683512`（`memset(…,0xC)`，vtable `0xAF84CC`） |
| 攻击执行总入口 | `0x666362` | 唯一调用者 `0x6685C8`（`mob->DoAttack(idx)`，前面 0x666205 先起手） |
| 门控 | `0x66A7A2` | `if (A->doFirst==0 && mob->[+0x174]!=0) return; if (A->inactive!=0) return;` |
| 目标收集 | `0x95B734` | 遍历对象，`0x7A38D6(obj,&rect)` 取包围盒，`[0xBF04A8](rect, queryRect)` 真则收进结果数组 |
| 命中判定 | 同上 | **矩形 × 矩形相交**，不是点判定；`0x66720A` 把攻击的世界坐标矩形当 `queryRect` |
| 单目标结算 | `0x670C63` | 同队跳过（`[target+0x328] == [mob+0x328]`）→ **同目标 2000ms 内只能被打一次**（`[target+0x46C]`，`0x7D0`）→ 按 `A->magic` 选 `0x792C01`(魔法) / `0x792AE1`(物理) → 伤害数字位置取目标包围盒内随机点 |
| `specialAttack` | `0x667600` | `if (A->[+0xA0]) mob->[+0x9C] = A->attackAfter;` —— **`specialAttack` 是「用这条攻击的 `attackAfter` 覆盖怪物冷却」的开关**，全库只有 `9400633/attack4` 一条打开 |
| `tremble` | `0x6671D3` | `if (A->[+0x70])` 用常量 `[0xAF46E0]` 的 double 做一次屏幕震动调用（`0x43DA05`） |
| `knockback` | 未定位 | 只有解析写入 `A+0x98`，没找到读点 |
| `doFirst` | `0x66A7A2` | 与 `mob->[+0x174]` 组成门控；`[mob+0x174]` 的写入点未定位 |
| `inactive` | `0x66A7A2` | 置 1 则这条攻击整条作废；**全库 0 条作者化**，等于永远不开 |

`0x666362` 的三个分支用的是同一套矩形：
`[esi+4]==0` 时走 `[esi+8]` 的矩形（世界坐标），否则走另一条（`0x667352`）；
每条分支都先 `[0xBF04A8]` 判是否与视口/自身相交，再 `0x95B734` 收目标，
最后按目标逐个 `0x670C63`。
`esi` 在函数内会被 `[ebp-0x18]` 覆盖，**它到底是「攻击区对象」还是怪物自身还没定死**
（树里的 `[esi+4]` 既像 vtable 又像模式标记），见 §8。

---

## 7. 当前实现对照

实现集中在 `Field/NativeMobController.cs`（技能/攻击/召唤/反伤）、
`Field/NativeMobDebuffs.cs`（疾病）、`Server/Combat/NativeBossRules.cs`（冷却/优先级）。

| 规则 | 源端 | 当前 | 判定 |
| --- | --- | --- | --- |
| skill 生效延迟 | `skill<action>` 全部子节点 `delay` 求和，默认 0、不取绝对值 | `ServerMillis(root.Get("skill"+action))`，读**自己**文件 | 一致 |
| skill 动画锁 | 同一个 `animationTime`（`Monster:349-351`） | `busyUntil = Now + serverDelay` | 一致 |
| attack 动画锁 | `attack<N>` 子节点 `delay` 求和 | `ServerMillis(attack)` | 一致 |
| attack 命中时刻 | `A+0x8C` 写进攻击区对象的 `+0` 触发时刻（`0x66DE88`/`0x66E009`） | `attackAfter` | 一致（第六轮补证，之前误记为「表现层选择」） |
| `attack<N>/info/fixedDamage` | 不读，数据里也没有 | 已删除 | 一致 |
| 攻击冷却兜底 | `attackAfter`（`LifeFactory:624-626`） | `NativeBossRules.AttackCooldown(..., attackAfter)` | 一致 |
| 13x `prop` | `applyEffect` 首行 | `Apply()` 首行 | 一致 |
| `hp` 门槛 | `(int)((float)hp/maxHp*100) <= getHP()` | `NativeMobCastState.HpAllows` | 已纠正旧实现的反向比较；验证见下文 |
| `hp<=0 → 100` | `MobSkillFactory:97-99` | 同 | 一致 |
| 冷却起点 | 施放瞬间 | 同 | 一致 |
| `SEAL_SKILL` | 只封技能 | 同 | 一致 |
| 反伤互斥 | 已有 reflect 时不能再放 143/144/145 | 同 | 一致 |
| `140`/`141` 互斥 | 同 | 同 | 一致 |
| 免疫伤害 | 变 1 | `new DamageResult(1,false)` | 一致 |
| 反伤扣血 | 只认 145 的 x/y | 同 | 一致 |
| 范围不翻转 | `calculateBoundingBox` 无镜像 | `InRange` 不翻 | 一致 |
| 攻击范围翻转 | `noFlip==0` 且朝右才镜像 | 同（239 只怪 `noFlip!=0` 永不翻） | 一致 |
| 攻击范围节点 | `info/range` 容器下的 `lt`/`rb`（无标量） | `Range()` 先取 `range` 再取 `lt/rb` | 一致 |
| 圆形攻击区 `sp`/`r` | 398 条攻击只有 `sp`+`r`，`sp` 是小偏移、`r` 200~300 | `MobAttackArea.TryParse` → 圆心+半径 | 一致 |
| 攻击命中形态 | 矩形：`0x95B734` 目标包围盒 × 矩形相交；圆形：圆心 → 目标包围盒最近点 ≤ `r+10`（`0x66DB68`+`0x66DC1E`） | 两种形状都走 `MobAttackArea.Overlaps(delta±14/-48..0)` | 一致；玩家盒尺寸仍是近似值 |
| `type` 与区域形态 | 圆形只出现在 type 1/2；多波只出现在 type 3/4 | 不按 `type` 分支，只按节点有无 | 结果等价（数据 0 反例），但语义未利用 |
| `inactive` | 客户端门控，数据 0 条 | 不读 | 一致（都不触发） |
| `specialAttack` | `0x667600`：`[mob+0x9C] = attackAfter`（怪物整体攻击间隔），仅 1 条 | `attackGapUntil = Now + attackAfter` | 一致 |
| `lt2`/`rb2`、`reverse` | 3 + 2 条 | 不认 | 缺口（样本极小） |
| HEAL_M | `(x/1000)*(950..1999)`，区域分支逐只结算 | 同 | 一致 |
| AREA_POISON | 只生成雾、无伤害，寿命 `x*100` ms | 不结算；`131/tile` 画布按同一寿命落地 | 一致 |
| 召唤 limit 缺省 0 | 召不出 | 同 | 一致 |
| 召唤坐标 | 8500003/8500004/8510100 特例 + 越界重随机 | 同 | 一致 |
| 道场上限 | `925020000..925033804` | 同 | 一致 |
| 攻击带 `disease` | 完整 `applyEffect`，单目标分支 | 走 `Apply()`，`skill=false` 语义 | 一致 |
| 疾病叠加 | 同种不刷新、最多 2 个；**区域分支**圣盾连 SEDUCE/STUN 一起挡，单目标分支放过 | `NativeMobDebuffs.Apply(..., region)` | 一致（两套口径按源码分开实现） |
| 驱散范围 | `dispelDebuffs` 只清 CURSE/DARKNESS/POISON/SEAL/WEAKEN/SLOW | `Dispel()` 同 6 项 | 一致 |
| ZOMBIFY 治疗 | 药水 `/2`、治疗技能取负（`StatEffect:1493-1513`） | `Healing(amount, skill)` | 一致 |
| CURSE 经验减半 | 是 | `Profile.GrantExperience` 前 /2 | 一致 |
| 主动/被动仇恨 | 客户端 AI | 本地 `aggroRange=8` + `dy<=1.8` | **未经核实** |
| POISON 每秒伤害 | 服务端不算，客户端算 | 每秒扣 `x`（非致死） | **本地实现，公式未核** |
| 移动速度 | `clamp(speed|chase|fly + 修正, 10, 140) * 0.01` 作倍率（§1.2） | 固定 160ms/10px，不用 WZ | **缺口**（倍率结构已核，px 基准未核） |
| 同目标 2000ms 内只吃一次 | `0x670C99..0x670CB2`：`[target+0x46C]` + `0x7D0`，先写时间戳再算伤害 | `PlayerCtrl.lastMobAttackMs` + `NativeBossRules.MobAttackAllowed` | 一致 |
| `knockback` | `A+0x98` 只有解析写入，13 个真实消费点全 0 命中 | 不读 | 一致（客户端不消费） |
| `tremble` | `0x6671D3` 常量 `0xAF46E0`=30.0 调一次震屏，39 条 | `HitFeel.Shake(30/PPU, .25s)` | 一致（幅度单位按像素解释） |
| `doFirst` | `0x66A7A2` 与 `mob->[+0x174]` 联合门控 | 不读 | 缺口（`[mob+0x174]` 语义未定） |
| `magic` / `elemAttr` | `magic` 决定 `elemAttr` 是否解析、并选魔法/物理伤害函数 | 只读 `magic` | 一致 |
| `areaWarning` 多波 | 131 条，`areaCount`≥`attackCount`，执行语义未核 | 无 | 缺口 |
| `ball` 弹道 | 起点=宿主矩形内随机点（已核）；速度/命中时刻未核 | 无 | 缺口 |
| `effect`/`tile`（MobSkill） | `Skill/MobSkill.img/.../{effect,tile}` 活（100 / 16 条） | `PlaySkillCanvas` → `NativeImgAnimation`（effect 跟怪、tile 落地、时长 `x*100`ms） | 一致 |
| `effect`/`hit`（按怪物） | `Mob/<id>.img/info/skill/*/{effect,hit,mob}` **0 引用** | 不画 | 一致（都不该画） |
| `effect`/`hit`（按攻击行） | `Mob/<id>.img/attack<N>/info/{effect,hit}` 活（331 / 2211 条） | `PlayCanvas`：effect 在怪身上、hit 在被打者身上 | 一致 |
| 状态图标 `affected`/`mob` | 活（MobSkill.img） | 不画 | 缺口 |
| `screen`（全屏） | **客户端不读**，服务端不读 | 不画 | 一致（都不该画） |
| `info`/`lua`/`fieldScript`/`summonOnce`/`linkHP`/`afterEffect`/`mob0` | 两边都不读 | 不读 | 一致 |
| `direction` | 客户端读、数据 0 条 | 不读 | 一致（无数据） |
| 技能失败即跳过的怪 | 源服整只怪不可用 | 打日志继续 | **主动偏差** |

验证入口（工作目录 `MapleDash/`）：

```sh
rtk dotnet build tools/unity-compile-check/Check.csproj -t:Rebuild   # 编译
rtk python3 tools/audit_mob_skills.py                                 # 数据不变量
cd tools/boss-check && rtk dotnet run -c Release                      # 几何/规则断言
rtk python3 tools/export_mob_skills.py                                # 重新导出（改了 WZ 才需要）
```

---

## 8. 待核实（不得写成实现要求）

### 8.0 本轮已结案（2026-10-30 第二轮）

- **`Mob/%07d.img/info/skill/%03d/{effect,hit,mob}`**：全立即数扫描 **0 引用** → 客户端
  从不加载（§2.2b）。不是「没找到调用点」，是**不存在**。
- **`info/areaAttack`**：同样 0 引用（§2.2b）。
- **`lt/rb` 之外的范围节点**：`range` 子节点已全量枚举（§2.6）。
- **`MobSkill.img` 的 `screen`/`fieldScript`/`lua`/`summonOnce`/`linkHP`/`afterEffect`/
  `mob0`/`info`**：客户端读取白名单里没有（§3.5），服务端 Java 也 0 命中 → 死数据。
- **`type` 的语义**：不再是「未知枚举」，交叉表在 §2.7，且 `audit_mob_skills.py` 第 10 组
  断言会锁住「圆形只出现在 type 1/2」。
- **命中测试的形态**：`0x95B734` + `[0xBF04A8]` = **矩形 × 矩形相交**，已照此实现
  （`MobAttackArea.Overlaps`），不再是点判定。
- **`specialAttack`(+0xA0)**：读点 `0x667600`，作用是「用本行 `attackAfter` 覆盖怪物冷却
  `[mob+0x9C]`」，全库 1 条（`9400633/attack4`）。
- **`tremble`(+0x70)**：读点 `0x6671D3`，屏幕震动，39 条。
- **`attackAfter`(+0x8C) 的消费点**：`0x667600`（被 `specialAttack` 门控）+ 服务端
  `LifeFactory:624-626`。「命中帧毫秒」这个用法**仍然是本地表现层选择**，没有源端证据。
- **`ball` 生成点**：`0x670F41`，宿主矩形内随机点，见 §6.4。

### 8.0b 第三轮结案（2026-10-29，递归下降重做）

- **`0x67FD81` 不再失步**：`--fn` 递归下降 = 1784 条指令、`0x67FD81..0x6811A4` **0 空洞**。
  「偏移表不可信」结论撤销，`§8-15` 结案。
- **`type` 是解析分支条件本身**：`0x680593 mov eax,[A+0xC]; cmp eax,0; jne 0x68071B`，
  0 走 `lt/rb`，3/4 跳 `0x680896`（多波计数），1/2 走 `sp/r`。见 §2.2a。
- **`+0x28` 的 union 布局全解**（表在 §2.2a），`0x4165B1` = 带 `0xBAADF00D` 哨兵的
  冗余校验（每分量 12 字节 = 2 hash + 和），**不是数值换算**。
- **`effectAfter` = `A+0x88`**（`0x681056`），`§8-6` 的「未定位」部分结案；
  「有没有消费」仍未定位。
- **`attach`(+0x84) 的消费点找到了**：`0x670D82`（单目标结算 `0x670C63` 内）
  `cmp [A+0x84],0` → 非 0 走「`0x432C10` + `0x66A4F2` + `0x663FE2`」分支，
  结果 `neg/sbb/inc` 归一化成 0/1。同函数还读了 `A+0x1C`(magic)、`A+0x14`(conMP)。
- **`0x666362 DoAttack` 不直接读 `A` 的矩形**：全函数 1601 条指令里没有任何
  `[reg+0x28..0x48]` 读取。攻击区是**先构造进「攻击区对象」**（`+4` 模式、`+8` 世界矩形、
  `+0x18` 朝向、`+0x1C` 序号），再喂给 `0x95B734`。三个收集点对应三条分支：
  ① `0x6670F9` 用 **1×1 点矩形**（弹道点 `inc` 成 1×1）；② `0x66728A` 与 ③ `0x6674B1`
  直接传**攻击区对象的 `+8` 世界矩形**。
- **`areaWarning` 的数据形状**（WZ 事实，非代码结论）：它是**落点贴图组**，不是逐帧动画
  （2400040/attack1：15 节点、`delay` 全 120、`origin` 从 `(24,409)` 跳到 `(80,412)`）。
  第五轮已确认它与波次定位无关（定位由 `start/spacing/dir` 算，见 §2.8.5），
  它只是资源；「贴图怎么对应到第 i 波」仍未核。
- **`0x666362` 的 `esi` = 怪物自身**（`mov esi,ecx`，读 `+0x328`/`+0x38C`/`+0x450`），
  `§8-16` 结案。→ **第五轮修正**：只对函数序言成立，三个收集点处 `esi` 已是攻击区对象，
  见 §8.0c。

### 8.0c 第五轮结案（2026-10-29，改从 DLL 反推 exe）

这一轮的入口不是 exe，而是客户端自带的 **Compat DLL**：它们是「字节补丁 + `push <原地址>; ret` 蹦床」，
所以 hook 体里直接写明了 exe 里被拦的函数地址。`IndexedDamageNumberCompat.dll`
（4.6KB，全 `.text` 仅 0x3DC 字节）一次给出了 5 个从未发现的入口：

| DLL 字符串 | 蹦床目标 | 含义 |
| --- | --- | --- |
| `CLOSE_RANGE_ATTACK lookup bytes` | `0x97251A` / `0x972626` | 近身攻击动作分派（`jmp [eax*4+0x97262B]`，action `0xBA..0xCC`） |
| `DAMAGE_MONSTER hook bytes` | `0x66C6E9` | 伤害怪物数字入口（`0x4065F3` 取属性、`0x406629` 取对象、`0x6691D3` 造数字） |
| `HookSkipLocalAttackByte` | `0x9803EB` | 本地攻击：`mov [esi+0x2AE0], al` 写动作状态字节 |
| `HookSkipLocalMoveAction` | `0x980477` / `0x9804BB` | 按 `[ebp-0x18] == [0xBEBF98]`（本地玩家）分流；非本地走 `0x980477`，本地跳 `0x9804BB` |

（上表的映射用 `tools/pe_module.py <dll>` 自动导出，别手抄——第一次手抄就把后两条搞反了。）

`BeiDouSkillCompatCore.dll` 另外给出 `HookBishopMagicAoeClassifier` 的两个目标
（非 AOE `0x955D19` / **AOE `0x956372`**）与 `HookSummonAttackClassifier`（`0x7AD4EA` / `0x7AD51B`）。
AOE 分支 `0x956372` 是**玩家**技能，但它揭示了客户端的范围构造惯例：
`0x4144D4` 生成 4 个 int 的局部 RECT，常量 `-0x4B`(75) / `0x32`(50) / `-0x64`(100) 是半宽半高
——**客户端的 AOE 一律是矩形，没有圆形**，这正是圆形只能出现在怪物 `type 1/2` 的原因。

结案清单：

- **`range/start`**（§8-2）：= 最左波次的带号（负数）。公式
  `centreX += ((start+i)*spacing + spacing/2) * dir`，`0x66DF4F..0x66DF76`。**不是帧号、不是延迟。**
- **`areaCount`**（§8-3）：只是 `>0` 门槛（`0x66DF12 jle <跳过>`），**不参与循环**。
- **`attackCount`**（§8-3）：**循环上限 = 实际生成波数**；夹 `<=0x20` 是因为
  掩码用 `1<<i`（`0x66DF30 shl eax,cl`）。
- **多波怎么选**（§8-3）：调用参数是**波次位掩码**，`0x66DF35 test [ebp+0x10], eax` 决定该波是否生成。
- **`type 3` vs `type 4`**：3 = 只画预警（`0xA456F9`，`mob.y ± 100`），4 = 实际伤害区。
- **`sp/r` 圆形命中几何**（§8-12）：**圆心 = `mob + (sp.x×朝向, sp.y)`，判定半径 = `r + 10`**，
  比的是「圆心 → **目标包围盒最近点**」的距离。`sp.y` 不吃朝向。
  ~~圆心先 clamp 进「攻击允许矩形」~~ → 第六轮结案：那其实是目标包围盒（§8.0e）。
- **`ball` 起点与方向**（§8-4）：起点 `mob + (sp.x×朝向, sp.y)`（与圆形同一套 `sp`），
  方向 `atan2(目标-起点)`（`0xA6203F`）；命中是每 tick **1×1** 矩形，**逐点不扫线**。
- **`knockback`(+0x98)**（§8-7）：在 13 个 `GetAttackInfo` 真实消费点 ±0x120 内**一次都没有出现**
  （同批 `tremble`/`specialAttack`/`doFirst`/`attach`/`conMP`/`magic`/`elemAttr` 全部命中）。
  这条比第三轮的「93 处粗扫」强得多，可以判定 **客户端不消费 `knockback`**。
- **`0x666362` 的 `esi`**（§8-16，第三轮结论需修正）：在三个收集点
  （`0x6670A0`/`0x6670C8`/`0x66719B`）之前 `esi` 已被重写成**攻击区对象**
  （`0x66718A mov eax,[eax+0xE4]`）。「`esi`=怪物自身」只对函数序言成立，**不能推广到全函数**。

新发现、仍未实现的一条规则：**圆形攻击的半径容差 `+10px` 是写死的字面量**（`0x66DC63`），
不是配置也不是缩放。

### 8.0d 遗留（本轮之后仍然没有代码证据）

- **`[mob+0x174]`**：`doFirst` 的门控依赖它（`0x66A7A2`），谁写、代表什么状态未定位。
- **`effectAfter`(+0x88) 的消费**：13 个消费点里只有 `0x66A7A2` / `0x66DA2B` 取地址
  （`lea ecx,[..+0x88]`），没有任何一处按值判断。仍按「存储有意义、消费无证据」处理。
- ~~`0x664559` 的「攻击允许矩形」~~：**已结案（§8.0e）——它就是「取实体的世界包围盒」，
  不是任何"允许攻击区域"。**
- **波次位掩码是谁传的**：调用 `0x66D9C0` 的上层在哪一步决定 `mask`。
- **`attackCount` 与 `areaWarning` 的节点数不等**：既然 `attackCount` 是波数，
  那 `areaWarning` 的 `origin` 数组只是贴图资源，不承担定位职责（本轮结论），
  但「贴图怎么对应到第 i 波」仍未核。

### 8.0e 第六轮结案（2026-10-29，对着「已实现 vs 源端」逐条核对时补的）

这一轮不是找新入口，而是把 §2.8 的结论**逐条拿去验代码**，顺手把读错的两处纠正。

- **`0x664559(obj, outRect, flag)` = 取实体的世界包围盒**（60 条指令）：
  `0x6645ac [0xBF0454]` 用 `obj+0x510/+0x514` 把局部矩形（`obj+0x40C`，或 `+0x41C` 另一份）
  变换到世界坐标；`flag!=0` 时再用 `+0x518/+0x51C` 变换第二份并 `0xBF0408` 求并。
  `0x670D17` 用它给伤害数字在目标身上取随机落点，就是最直接的证据。
  → **不存在「攻击允许矩形」这个概念**，§2.8.3 旧读法作废。
- **type 1 圆形是「圆 × 目标包围盒」**，不是点判定（§2.8.3 重写，证据 `0x66DB68`）。
- **type 0 的 `0x66E6B6` 循环不是持续伤害**，是「玩家站进矩形 → 发近身攻击动作」的决策
  （`0x66E636`，117 条指令，末尾 `push 0xC1` + `0x49637B` 发包）。§2.8.2 已改。
- **`attackAfter` 确实是命中延迟**：`0x66DE88` 把 `A+0x8C` 写进攻击区对象的 `+0` 触发时刻。
  第五轮记的「只是表现层选择」作废（服务端那边仍然只有冷却兜底这一个用途）。
- **`tremble` 的常量** = `0xAF46E0` 处的 double **30.0**，交给 `0x43DA05` 做一次震屏；
  它在收集目标之前调用，所以打空也会震。
- **`specialAttack` 的落点**：`0x667600` 段是
  `cmp [A+0xA0],0 / mov eax,[A+0x8C] / mov [mob+0x9C],eax / call 0x670A3F`，
  而 `0x670A3F` 读的是 `[mob+0x9C]`(冷却时长) + `[mob+0xA0]`(下次可用时刻)，
  所以它改的是**怪物整体攻击间隔**，不是单条攻击行的冷却。
- **`info/ban` 的真实结构**：`info/ban/{banMsg, banMap/0/{field,portal}}`
  （`LifeFactory:633` 三个字段全读），`banMsg` 由 `Character.changeMapBanish:1563`
  以 `dropMessage(5, msg)` 形式发给玩家。
- **`MobSkill.applyDisease:521-539` 的两套口径**已按源码坐实：区域分支 `:525`
  先挡圣盾（连 SEDUCE/STUN 一起），单目标分支直接 `giveDebuff`（让 123/128 过）。


- **`0x67FD81` 不再失步**：`--fn` 递归下降 = 1784 条指令、`0x67FD81..0x6811A4` **0 空洞**。
  「偏移表不可信」结论撤销，`§8-15` 结案。
- **`type` 是解析分支条件本身**：`0x680593 mov eax,[A+0xC]; cmp eax,0; jne 0x68071B`，
  0 走 `lt/rb`，3/4 跳 `0x680896`（多波计数），1/2 走 `sp/r`。见 §2.2a。
- **`+0x28` 的 union 布局全解**（表在 §2.2a），`0x4165B1` = 带 `0xBAADF00D` 哨兵的
  冗余校验（每分量 12 字节 = 2 hash + 和），**不是数值换算**。
- **`effectAfter` = `A+0x88`**（`0x681056`），`§8-6` 的「未定位」部分结案；
  「有没有消费」仍未定位。
- **`0x666362` 的 `esi` = 怪物自身**（`mov esi,ecx`，读 `+0x328`/`+0x38C`/`+0x450`），
  `§8-16` 结案。
- **`sp/r` 圆形几何**：`sp.x/sp.y/r` 是**明文裸 int** 写 `A+0x28/0x2C/0x30`，
  与矩形走不同的包装路径 → 「圆心+半径」有布局证据（§8-12 收窄）。
- **`knockback`(+0x98) 仍无读点**：怪物区段（`0x660000..0x780000`）93 处 `+0x98` 引用逐一核对，
  没有一处落在 `GetAttackInfo` 的调用上下文里；同批的 `tremble`/`specialAttack`/`doFirst`
  都找到了。**倾向「客户端不消费」，但未证伪**，仍留 §8-7。
- **`attach`(+0x84) 的消费点找到了**：`0x670D82`（单目标结算 `0x670C63` 内）
  `cmp [A+0x84],0` → 非 0 走「`0x432C10` + `0x66A4F2` + `0x663FE2`」分支，
  结果 `neg/sbb/inc` 归一化成 0/1。同函数还读了 `A+0x1C`(magic)、`A+0x14`(conMP)。
- **`0x666362 DoAttack` 不直接读 `A` 的矩形**：全函数 1601 条指令里没有任何
  `[reg+0x28..0x48]` 读取。攻击区是**先构造进「攻击区对象」**（`+4` 模式、`+8` 世界矩形、
  `+0x18` 朝向、`+0x1C` 序号），再喂给 `0x95B734`。三个收集点对应三条分支：
  ① `0x6670F9` 用 **1×1 点矩形**（`[ebp-0x48]/[ebp-0x44]` 弹道点，`inc` 成 1×1）；
  ② `0x66728A` 与 ③ `0x6674B1` 直接传**攻击区对象的 `+8` 世界矩形**。
- **`areaWarning` 的数据形状**（这不是代码结论，是 WZ 事实）：它是一个**落点贴图组**，
  不是逐帧动画 —— 每个子节点 `0..N` 有自己的 `origin`
  （如 2400040/attack1：15 个节点、`delay` 全 120、`origin` 从 `(24,409)` 一路跳到 `(80,412)`；
  7220005/attack1 有 23 个节点，`origin` 的 y 在 `frame16..20` 突跳到 `578`）。
  所以 `type 3/4` 的多波次落点很可能就来自这些 `origin`，而 `A+0x38` 的第二组矩形是**相对落点**
  的判定框 —— 但 `start`/`areaCount`/`attackCount` 与节点数**没有稳定换算关系**
  （2400040：15 节点/areaCount 15 ✓；8143000：6 节点/areaCount 9 ✗；7220005：23 节点/areaCount 9 ✗），
  所以「谁决定打哪几个落点」仍未定死。

1. **主动/被动判定**：`firstAttack` 写到模板 `+0x17C` 后没有找到读取点；
   `chase` 只影响移动动作码。源服不消费 `firstAttack`。在找到 EXE 读取点之前，
   本地 `aggroRange`/高度差阈值只能记为「本地策略」。
2. ~~**`range/start`**~~：**已结案（§2.8.5）**。`start` = 最左波次的带号（负数），
   消费公式 `centreX += ((start+i)*spacing + spacing/2) * dir`（`0x66DF4F..0x66DF76`）。
   `areaCount = 2|start|+1` 只是这个「中心对称展开」在多数样本上的副产物，
   `8143000`（`-5→9`）之所以破式子，是因为它的 `attackCount` 更小、实际生成波数不足。
3. ~~**`areaCount` / `attackCount` 执行语义**~~：**已结案（§2.8.5）**。
   `areaCount` 只当 `>0` 门槛（`0x66DF12 jle`），`attackCount` 才是循环上限；
   哪几波生成由调用者的**波次位掩码**决定（`0x66DF35 test`），夹 `<=0x20` 是因为 `1<<i` 只有 32 位。
   `areaWarning` 的 `origin` 数组只是贴图资源，不承担定位职责。
4. ~~**`ball` 弹道**~~：**起点与方向已结案（§2.8.4）**。起点 `mob+(sp.x×dir, sp.y)`，
   方向 `atan2(目标-起点)`（`0xA6203F`），目标以 16 位打包传入；命中是每 tick **1×1** 矩形（逐点不扫线）。
   **仍未核**：`bulletSpeed`(+0x68) 的读点（数据里 0 条，实际不可达）、
   弹道寿命与撞墙判定（`[0xBF0484]` 未解）。
5. **同目标 2000ms 内只吃一次（`0x670C63`）**：`[target+0x46C]` + 常量 `0x7D0`。
   这是**新发现且未实现**的规则；本工程用 `invulnUntil`，两者时长不同。
   要不要接这条需要先确认 `[target+0x46C]` 确实是「上次被怪打的时间」。
6. **`effectAfter`**：**存储位置已定位 = `A+0x88`**（`0x681017 push 0x677(effectAfter)` →
   `0x681056 mov [eax+0x88], …`，2026-10-30 递归下降复核）。`info/skill/<n>/effectAfter`
   由行解析器读进结构（`0x6813DF`），**仍没有任何分支消费它**。
   不要凭名字实现「延迟播特效」。
7. **~~`knockback`(+0x98)~~ / `doFirst`(+0x90)**：
   `knockback` **可判定为客户端不消费**——13 个 `GetAttackInfo` 真实消费点 ±0x120 内
   一次都没出现（§8.0c），同批 `tremble`/`specialAttack`/`doFirst`/`attach`/`conMP`/
   `magic`/`elemAttr` 全部命中。这是「精准上下文扫描」而非「全区粗扫」，结论等级更高。
   `doFirst` 的**门控位置已核**（`0x66A7A2`），但 `[mob+0x174]` 谁写、代表什么状态仍未定位，
   所以「doFirst 是首击特权」只能记为推断。
   `tremble` 与 `specialAttack` 已结案（§8.0）。`bulletSpeed`(+0x68) 数据里 0 条，不可达。
8. **POISON(125) 每秒伤害**：服务端零处计算，`PacketCreator` 只把 `x` 发下去
   （`giveDebuff` 里 POISON 额外写一个 short，见 `Character:2833`）。
   当前运行时的「每秒扣 x、非致死」是本地实现，不是已核实公式。
9. **SEDUCE(128) 的 `x` 语义**：数据里 `x ∈ {1,2,3,4,6,7}`。本工程把它当「强制动作模式」
   （1 往左 / 2 往右 / 3 跳），4/6/7 未覆盖。语义未核。
10. **DARKNESS / WEAKEN 的数值影响**：服务端不消费，需要 EXE 的命中率/跳跃计算。
11. **移动速度**：`clamp(...,10,140) * 0.01` 已核（§1.2），但 `%`→`px/s` 的换算基准未核，
    `mob+0x228` 修正量的来源未核（扫了 `[reg+0x228]` 的全部写入点，落在 `0x52xxxx`
    的通用 setter 里，不是怪物专属）。SLOW(126) 的 `x` 只有 60/80 两个值，
    是「设为 60」还是「减到 60%」没有证据。
12. ~~**`range/sp` + `range/r` 的命中几何**~~：**已结案（§2.8.3 + §8.0e，第六轮定稿）**。
    圆形有独立的命中代码，就在 `0x66DAB0` 的 `cmp ecx,1` 分支：
    圆心 `= mob + (sp.x×朝向, sp.y)`（`0x66DBBB`/`0x66DBCB`），判定半径 `= r + 10`
    （`0x66DC63 add eax,0xA`），`sqrt(dx²+dy²) <= r+10` 才命中（`0x66DC69`）。
    **但 dx/dy 是「夹进目标包围盒之后的点 − 圆心」**（`0x66DB68 call 0x7A38D6` 先取盒），
    所以是圆 × AABB，不是点判定。本工程按 `MobAttackArea.Overlaps` 实现。
13. **`lt2`/`rb2`（3 条）与 `reverse`（2 条）**：`8880000`/`8880832` 的双矩形、
    `8870000`/`9900000` 的 `reverse=1` 都没有消费证据。样本只有 5 条，先不做。
    第五轮补：`0x666362`/`0x66D9C0` 两条主通路里都没有出现 `+0x58..0x67`（`lt2/rb2` 落点）
    或 `reverse` 的读取，倾向「客户端不消费」。
14. **`MobSkill` 等级结构 `+0x44`**：被 `0x66C215` 当字符串读（引用计数在 `+8`），
    但装载器不写它。数据里 `level/*/info` 是韩文标签，两者是否同一字段没有证据。
15. **~~`attack<N>/info` 字段偏移表的可靠性~~**：**已结案（2026-10-30）**。
    之前的「失步」结论是**错的**：线性 + skipdata 在带 switch 表的大函数里会漂；
    改用 `--fn` 递归下降后 `0x67FD81` 是 **1784 条指令、`0x67FD81..0x6811A4` 连续无空洞**。
    `+0x28` 一带的「冲突」是真 union，见 §2.2a。偏移表恢复为**可信**（运行期仍以 WZ 名字为准）。
16. ~~**`0x666362` 的 `esi` 身份**~~：**已结案并修正（§8.0c）**。
    序言处 `esi = this = 怪物`，但三个收集点之前（`0x66718A mov eax,[eax+0xE4]`、
    `0x66719B mov esi,eax`）`esi` 已被换成**攻击区对象**（`+8` 世界矩形 / `+0x18` 朝向 /
    `+0x1C` 序号）。**不要按「全函数 `esi` 都是怪物」去读那段代码。**

**仍未定位（真卡点，别写实现）**——第五轮之后只剩这些：

- `doFirst` 依赖的 `[mob+0x174]` 来源（第 7 条）。
- `ball` 的 `bulletSpeed` 读点与撞墙/寿命判定（第 4 条；`bulletSpeed` 数据 0 条，实际不可达）。
- `effectAfter`(+0x88) 的消费（第 6 条；只被取地址，没有按值分支）。
- `lt2/rb2`/`reverse`（第 13 条，5 条样本，两条主通路均不读）。
- `MobSkill` 等级 `+0x44`（第 14 条）。
- 服务端零覆盖的疾病数值语义：POISON 每秒伤害、SEDUCE 的 `x`（4/6/7）、
  DARKNESS/WEAKEN 的命中率影响、SLOW 的 `x` 是「设为」还是「减到」（第 8~11 条）。
- 移动速度 `%`→`px/s` 的基准与 `mob+0x228` 来源（第 11 条）。
- 本轮新增：`0x664559` 填的「攻击允许矩形」是什么边界；波次位掩码的上层调用者；
  `areaWarning` 贴图与第 i 波的对应关系。



### 2026-10-29：纠正施法 HP 门槛方向

原服 `MoveLifeHandler.java:172–177` 的确切条件是 `candidate.getHP() >= (int)(((float)hp/maxHp)*100)`；
`MobSkillFactory.java:95–99` 将配置 hp<=0 归为100。此前字段表的“当前HP≥门槛”及审计“一致”结论错误，现纠正。
当前 `NativeMobController.Update` 改用 `NativeMobCastState.HpAllows`，使用 float 除法和整数截断：
50.9% 在门槛50时允许，51%拒绝，默认100在掉血后仍允许。
这是服务端源码规则，不依赖 EXE 地址，也不能据此宣称 firstAttack/追击/飞行移动已搬完。
回归入口：`NativeGameplayNodeChecks.Run` 的HP边界断言，以及 `NativeBuffChecks.RunAll`
内 `CheckMobLifecycle` 将2220000降到50%HP、恢复MP后调用实际控制器Update，要求扣MP并进入施法忙状态。

本次实测：隔离Unity `NativeGameplayNodeChecks.Run` (`/tmp/mapledash-priority-ai-nodes.log`) 退出0，
`NativeBuffChecks.RunAll` (`/tmp/mapledash-priority-ai-runtime.log`) 退出0，包含上述实际半血施法断言及现有生命周期/辅助技能HUD回归。


### 2026-10-29：地面追击速度补齐与飞行边界

身份沿用本文件 BeiDou.exe SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`、base0x400000。
`67DB95→67DB9A`、`67DC17→67DC1C`、`67DC94→67DC99` 在读取 speed/flySpeed/chaseSpeed 后加100；
随后 `6823F0` 取max(value,0)，再夹140。`664380..6643A3` 对模板+修正夹10..140再乘.01。
`9B2187` 的 `9B219D..9B21BA` 读取 CLifeMoveParam+24，乘foothold属性+0C与Physics对象+8；
后者为 Map/Physics.img/walkSpeed，地面入口 `9B23F2→9B2657→9B2187`。
复现用 `tools/inspect_beidou_client.py <EXE> --fn 0x6642B0` 与 `--fn 0x9B2187`。

运行时 NativeMobMotion 保留先模板后运行时的两次限幅；NativeMobController.GroundMoveSpeed 使用 own-file info，
有根chase动画且有目标才选chaseSpeed，地面巡逻选speed；EnemyUnit改用该速度与chase动画。
NativeGameplayNodeChecks 包含负数、极值、修正在模板限幅之后的断言。
仍不是完整AI：本地选敌/恒速/平台寻路未还原原端加速度与决策，firstAttack及mob+228修正来源未解决。
`6643FB..66449F` 确认飞行把fly倍率写入CLifeMoveParam+84/+6C/+60，+78/+54置1；
飞行力、阻力、限速与目标决策的后续消费未核，当前飞行怪仍不移动，不能按地面125px/s硬套。
此前 §8“地面基准未定位”条目由本补记取代，疾病修正/飞行基准仍是缺口。


回归稳定性纠正：2220000 的候选包含 MobSkill200/level78（hp90、缺mpCon即0），
以及126/7、112/1、113/1（各mpCon5）。随机选择原召唤时MP不下降是合法行为；
半血施法断言应要求进入Busy，而不能把“MP必须下降”当成所有候选通用合同。
复现：wzmap读取 Mob/2220000.img/info/skill 与 Skill/MobSkill.img/<id>/level/<level>。

最终验证：NativeGameplayNodeChecks.Run（/tmp/mapledash-gameplay-final2.log）退出0；
NativeBuffChecks.RunAll（/tmp/mapledash-priority-four-runtime2.log）退出0，包含真实控制器的speed/chaseSpeed选择、
无chase容器回退与半血施法。compile-check 0 warning/0 error。持久摘要 docs/skill-regression/priority-four-checks.txt。
