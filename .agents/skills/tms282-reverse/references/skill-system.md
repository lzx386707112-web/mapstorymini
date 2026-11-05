# 282 技能子系统（五转 / 六转）

**2026-11-04 重迁移前置补核**：见 `docs/tms282-skill/17-project-advanced-remigration.md`。
本次用户范围为当前项目 17 条终转路线；直接核心技能引用 526 个（不是执行依赖闭包）。
旧目录漏了 V subSkill `400011002`，且不完整保留 multiAttackInfo/嵌套特效。
当前 282 服务端另有新版 VMatrix state/service，不能把旧槽位资源直接当当前 UI 行为。
找到的五六转 GM 捷径跳过任务目标与奖励，不构成原版任务脚本证据。
用户要求必须完整还原，任务证据补齐前不实施相关流程。
同日续查见18文档：网上取得公开旧版五转15份脚本（有TODO与跳过试炼，禁止当282完整实现）；
本地核实三处10秒中转地图、七种六转经验石2636387～2636393（exp字段均500000000）；
EXE A8E4E0～A8F1EE读取info/questComplete/questAccept/disallow，是通用装载路径，非专用任务流程。

核实日期：2026-11-03。对象：`~/Documents/mxd/MapleStory/MapleStory.exe`（TMS 282 客户端，
186,193,888 B）+ `~/Documents/mxd/TMS282.5`（服务端）。
**全量正文在 `mxd-android/docs/tms282-skill/`（6 篇 + 3 份数据集）**，本文件只保留
「下次直接开工需要的事实」：二进制地址、容器规格、节点语义、已证伪假设。

复现工具：`MapleDash/tools/ms282/MsUnpack.java`（解 `.ms`）、
`MapleDash/tools/ms282/export_skill_catalog.py`（导出目录）、
`wzpy`（`Beidou-Server/tool/wz-python`，**必须用 `WzKey.for_region('BMS')`**）。

---

## 1. 数据被拆成两层（最重要的一条）

| 层 | 路径 | 内容 |
|---|---|---|
| 定义层 | `Data/Packs/Skill_00000.ms` … `Skill_00007.ms` 内的 `Skill/<job>.img` | `common` 数值公式、`info`、`action`、每帧 `origin/delay/z`、`_outlink` |
| 像素层 | `Data/Skill/_Canvas/<job>.img`（旧 WZ 树） | 只有像素，**`origin/z/delay` 全为空** |

- 定义层里 7953 个技能的 canvas 帧是 **1×1 占位图**，真像素靠 `_outlink` 指到 `_Canvas`。
- **不要**把 `_Canvas` 当元数据源。实测 `11212.img` 的 471 个画布**全部 0 个子节点**。

### 规模

| 项 | 值 |
|---|---|
| Skill 包 | 8 个（含 `FamiliarSkill`、`FieldSkill` 两个非数字条目），合计 **790** 个 `.img` |
| 全量技能定义 | **13,638** 条 |
| 五/六转技能定义 | **1,618** 条 |
| 五/六转主动技能 | **717** 条 |
| 有 V/HEXA 矩阵的职业 | **51** |
| 视频块 | **306** 节点 / 121 技能 / 894.9 MB |

### 命名规则

- 五转：`400011xxx`…`400051xxx`（5 个桶），强化 `400004xxx`，共通 `400001xxx`。
- 六转：惯例 `<4转职业ID+2>.img`（112→114、1112→1114、11212→11214）；
  **唯一例外 `2217 → 2220`**。六转新技能 `xxxx1500` 段；强化连 `500004xxx`，共通 `500001xxx`。
- 矩阵：`Etc/VCore.img`（`vSkill/coreGroup/<job>/{skill,enforce,common}` + `vSkill/CoreData`）、
  `Etc/HexaCore.img`（`hexaSkill/jobCore/<job>/{mastery,enforce,skill,common}` +
  `hexaSkill/coreData` + `solErda`）。
  **大小写不一致**：VCore 用 `CoreData`，HexaCore 用 `coreData`。

---

## 2. `.ms` 容器

Snow2 v2 / ChaCha20 v4 双版本，1024 字节块对齐。
**文件名参与密钥派生**：`charSum % 312 + 30`、`tripleSum % 212 + 33` ——
改名会导致解包失败，`Skill_00000.ms` 里的条目名带 `Skill_` 前缀，读 `img` 名时要剥掉。
Snow2 分支会回退到全零密钥。

---

## 3. 节点结构

### 3.1 两套互斥数值 schema

| schema | 形态 | 用在哪 |
|---|---|---|
| `common/<字段> = 公式字符串` | `"560+112*x"`，`maxLevel` 是唯一 int | **五/六转 1618/1618 全用这个** |
| `level/<n>/<字段> = int` | 纯 int + 向量，每级一套 | 旧技能 2778 条（`000.img`、`800xxx`、部分 1–4 转） |

`common` 下即使常量也是字符串（`"9"`、`"100"`），因为统一走公式求值。

### 3.2 顶层键

1,618 条 5/6 转共 **308 种**顶层键，平均 14 个/技能。高频：
`icon/iconMouseOver/iconDisabled/common` 各 717、`info` 660、`weapon` 634、`action` 585、
`hit` 534、`effect` 509、`req` 426、`processtype` 381、`info2` 297、`skillList` 265、
`psd`/`psdSkill` 266/255、`vSkill` 205、`effect0` 203、`multiAttackInfo` 141、
`special` 136、`screen` 131、`isSequenceOn` 114、`holding` 98、`finalAttack` 93。

`common` 下 137 种键；`info` 下 51 种（`type` 658、`areaAttack` 256、`magicDamage` 42、
`condition` 22、`rapidAttack` 22、`CooltimeCheckSkillID_WZ2` 13、`zoneType` 2）；
`info2` 下 8 种（`ignoreCounter` 277、`mes` 21、`ignoreImmune` 11）。

### 3.3 数值公式 DSL（282 语义）

- `x` / `X` / `y` 都是**技能等级**（同一值）。
- `logN(x)` 在 `TMS_282_3` 分支返回 **`Math.floor(logarithm)`**；其余版本返回浮点。
  例：`2141504.common.bdR = "40+log10(x)*10+log20(x)*10"`，30 级 = `40+10+10 = 60`
  （不 floor 会得 63）。
- `d()` = floor，`u()` = ceil，支持隐式乘法，未知标识符返回 0，末尾 `(int)` 截断。
- 参考实现：`TMS282.5/source/.../skills/info/SkillFormulaEvaluator.java`（315 行递归下降）。
  本项目对应缺口见 `06-migration.md §1`。

### 3.4 特效层 25 族

`hit`/`effect`/`effect0..2`/`special`/`special0`/`screen`/`screen0..12`/`summon`/`mob`/`ball`/
`tile`/`affected`/`repeat`/`keydown`/`keydown0`/`keydownend`/`prepare`/`finish`/`afterimage`。

`summon` 有状态子节点（idle/walk/attack/…），`tile` 有 `pre/loop/end` 三段
（**字典序会给出 `end → loop → pre`，必须显式排序**）。

### 3.5 `multiAttackInfo/`

多段攻击时间轴，条目 `x` 指向**另一个技能 ID**（不总是自己）。
例：`4141503` 第 10–21 拍打到 `4141504`。

### 3.6 标志位 / 迷惑字段

- `processtype`、`isSequenceOn`、`holding`、`notCooltimeReset`、`notIncBuffDuration`、
  `additional_process`、`psd`：值 1 即打开。
- **`common/dummyStr` … `dummyStr19` 是迷惑字段**：真数值塞进无语义槽位。
  EXE 里只有一处 `dummyStr` 字面量（RVA `0x8250A30`，紧邻 `0x824FC09` 的 `6thCount`），
  且 **`lea`/`mov` RIP 引用索引里查不到任何引用点** —— 不能据此断言"按前缀统一读取"。
  迁移时**不要读**，等有真需求再查。

---

## 4. MCV0 视频容器（已解，本项目能播）

头部 48 字节（`0x06` 给 `headerLength`，实测恒 `0x30`）：

| 偏移 | 类型 | 含义 |
|---|---|---|
| `0x00` | u32 | `"MCV0"` = `0x3056434D` |
| `0x08` | u32 | **FourCC ^ 0xA5A5A5A5** → `0x959CF5F3 ^ …` = `"VP90"`（或 `"VP80"`） |
| `0x0C/0x0E` | u16 | width / height |
| `0x10` | u32 | frameCount |
| `0x14` | u8 | flags：bit0=alpha 流、bit1=逐帧 delay、bit2=逐帧 start |
| `0x18` | u64 | unit（ns 换算，`1000000`） |
| `0x20` | u32 | defaultDelay（单位数，非 ms，常见 60） |

表区从 `headerLength` 起，`N×(8 + alpha?8 + delay?4 + start?8)`；`dataStart` = 表区末尾；
实际偏移 = `dataStart + offset[i]`。**带 alpha 是双路视频流**（一路颜色、一路灰度当 alpha），
解码后合成 RGBA。`Duration = max(start[i]+delay[i]) × unit`。

- 只出现在 `screen*`（119/78/33/14/13）等演出层，`effect`/`special` 各 4 个。
- 实现：`MapleDash/Assets/Scripts/McvVideo.cs`（容器）+ `McvPlayback.cs`（解码线程）+
  `MapleDash/Native/Mcv/mcv_decoder.cpp`（libvpx，输出 top-down RGBA）。
- **不要**再用 zlib / LZ4 去试帧载荷，也不要找"自制编码"。

---

## 5. EXE 关键函数（RVA）

| RVA | 作用 |
|---|---|
| `0x13af510` | 按键名读整数 |
| `0x13b1fc0` | 取子节点容器 |
| `0x7936480` | 字符串相等比较（所有字段分派的 helper） |
| `0xdc5c80` | **图层名 → 枚举**，39 支线性 if 链（`pre`=5 … `flash2`=46） |
| `0x589d140` | **`SkillInfo` 属性装载**，120 字段写 `[rdi+off]`（`damR_5th`→+0x40、`damR_6th`→+0x44） |
| `0x111e390` | **技能数值求值**（62 KB 帧，`attackCount`/`damage`/`rushDistance`/`edCon`） |
| `0x11ee2f0` | `createDelayMin/Max` + `attackCountMin/Max` |
| `0x53fd50` | 怪物攻击引用（`attackDuration`…`mobattackref_idx`）—— **不是玩家技能** |
| `0x5945510` | 顶层特效层分派，**只列 7 个**：`summon/tile/shootobj/effect/screen/special/customEffect` |
| `0x6a93920` | WZ2 规格路径 `Skill/MExplorerSkill.img/{}/Skill`（资源侧基本缺失） |
| `0x78fb610` | `__chkstk` 等价（大栈帧入口 `mov eax,size; call`） |

注意 `0x589d140` **不含** `damage`/`attackCount`/`mobCount`/`cooltime`/`mpCon` ——
这些公式字符串由 `0x111e390` 那条路径求值。

节表：`.text` VA `0x1000` / VSize `0x8193AAA`、`.pdata` VA `0xA247000`（**函数边界权威来源**）、
`.themida` VA `0xAA1C000` 但 **RawSize = 0**（壳只保留解包目标区，主代码明文可静态分析）。
`.data` 有 **5,757 个**未混淆 MSVC RTTI 名。

### 5.1 两套框架

- **legacy**（`SkillSystem::` 命名空间）：`CSkillInfo`、`SkillStatement`、`CKeyInputEvent`、
  `CReactionInfo`、`SkillCategoryMan`、`CUserLocal::…(long, SkillStatement&)`。
  五/六转主动技能走这条。
- **WZ2**（引擎层）：`WZ2AbilitySkill(Spec/Def)`、`WZ2SkillAction`、`WZ2AESkill`、
  `WZ2RLSkill`、`ASkillActorBase/Projectile/Zone`、`UWZ2AnimMontage`、
  **`spine::Skeleton/Animation/TrackEntry`**（部分特效/召唤兽是骨骼动画）。
  WZ2 规格资源缺失，不要把它当五/六转数据源。

### 5.2 已证伪（别重复踩）

1. `0x6670b0`（9.5 KB）**不是**技能处理器，是「蓝洞探索」小游戏配置读取
   （`oxygen`/`DecrementTick`/`DetectionRangeL`…）。
2. EXE 里的 `log10` 字符串属于 **Lua 5.x math 库名表**（邻居 `ceil`/`cosh`/`fmod`/`pow`/`sqrt`），
   不是技能公式解析器。
3. **`MCV0` 字面量在 EXE 中 0 命中**（`"MCV0"`、立即数大小端都试过）—— 签名比较不是 4 字节整数。
4. **反汇编别用线性 sweep**：会把 `.rdata` 当指令，造出假的"技能函数"。
   必须用 `.pdata` 定边界 + 递归下降。
5. `wzpy` 不支持像素格式 **4098**（8bpp 索引图，`40001.img` 里 665 个）。
   需 `zlib.decompressobj(15)` 手动解，解出 `w*h` 字节。

---

## 6. 帧元数据与时间模型

- `delay` 以 **60 ms** 为主（13,059），其次 90（4,223）、120（769）、30（531）；
  有 >500ms 的长 delay，是**等待点**，不是帧时长 —— **不能把"所有 delay 之和"当技能时长**。
- `z` 全量 18,547 个样本**全为 0**（不要指望它排序）。
- `origin` 范围 `x ∈ [-526, 1393]`、`y ∈ [-355, 998]`。
- 生命周期：`prepare → keydown → keydownend → finish/end`。
- `icon` 在包里是 **1×1 占位**，不要当帧渲染。

---

## 7. 五转 / 六转任务与技能获取（摘要）

全量正文 `docs/tms282-skill/07-job-advancement.md`，数据 `docs/tms282-skill/data/advancement.json`。

- **五转**：Lv200 + 总星力 100 + 武器星力 10，时间神殿 `270010111` 找**凝視者** `2140001`，
  任务 `1460 → 1481 → 1461 → 1465 →(1478 解 V 矩阵)`。**不直接发技能**。
  星力门槛在任务 `1481`（NPC 神殿管理人 `2140000`，`Check/1/totalStarForce=100` + `weaponStarForce=10`），
  不是脚本；但服务端 grep 不到这两个字段，V 矩阵门禁只查 `level >= 200 && 完成 1465`。
- **六转**：Lv260 + 完成 `1465` + `39722`（[Borderless] 本來的樣貌），
  完成前置主线 `38601(莉莉) → 38602 … 38609 → 38624 → 38625(HEXA 矩阵)`。
  **2026-11-04 纠正**：38612 要求38601进行中，38611要求38612进行中，不能串成旧线性链；
  五转还包含1461进行中的1462/1463/1464三女神并行任务。完整证据及脚本缺失边界见
  `docs/tms282-skill/18-advancement-evidence-search.md`，不能将此摘要视为已恢复的可执行流程。
  `38618`–`38623` 是奥术河 6 个区域的符文回收（索尔艾尔达来源）。
  **282 的 HEXA 包与处理器沿用 265**（`CP0490`/`CP0491`），只多一个 `LP009D` unlock；
  所以 `HexaMatrixConstants.jobAdvanceQuest=1488` / `solErda=1489` 这两个 265 任务 ID
  在 282 客户端**不存在**。
- **技能获取是一条三级链**：核心宝石/掉落 → V 核心 →（练满后）HEXA 核心。
  六转**不是白给**：
  - HEXA 精通核心（type1）前置 = 对应**五转强化核心 `400004xxx` 达 40 级**
  - HEXA 强化核心（type2）前置 = 对应**五转技能核心 `400011xxx` 达 25 级**
  - 六转新技能（type0）与部分共通核心（type3）无前置
  例：英雄 `20000000 狂暴攻擊VI` 需 `400004008=40`；`30000000 燃燒靈魂之劍` 需 `400011001=25`。
- **V 矩阵**：`Etc/VCore.img` + `Etc/VMatrixOption.img`。580 核心 = 技能 255 / 强化 306 /
  特殊 18 / 材料 1；51 职业各 4 技能 + 6 强化 + 19 共通（1 个职业 21）。
  槽位初始 12、上限 30；V 点上限 9999；技能核心 1→30 = 193 点，强化核心 1→60 = 80 点
  （`vPoint/skill|enforce` 是**阈值表**，取第一个 ≥ 目标等级的键）。
- **HEXA 矩阵**：`Etc/HexaCore.img`。563 核心 = 新技能 102 / 精通 204 / 强化 204 / 共通 53；
  51 职业全是 `mastery 4 + enforce 4 + skill 2 + common 3`。
  索尔艾尔达 9 档（10…1000 经验），gauge 上限 1000、单次最多吃 20 个。
  激活/升级消耗**不在客户端 WZ**，由服务端 `HexaMatrixConstants` 下发
  （激活 5/3/4/7 点；1→30 总耗 145/80/119/201 点）。
- **待决策**：职业 ID 三层（4 转 112 / 5 转 113 / 6 转 114）。任务数据写 113，
  技能文件用 `114`（4 转+2），矩阵表 `coreGroup`/`jobCore` 却按 **112** 建索引，
  而服务端 `getJobLevel(113)=0`。迁移前必须选「ID 不变 + jobLevel」或「ID 逐级 +1」。

---

## 8. 与本文档集的关系

| 需要细节时读 | 文件 |
|---|---|
| 资源分层 / 包格式 / 像素格式 / 工具链 | `docs/tms282-skill/01-data-layers.md` |
| 节点字典 / 162 个 common 键 / 公式 DSL / MCV0 | `docs/tms282-skill/02-node-schema.md` |
| 函数地址 / 结构体偏移 / 框架对比 | `docs/tms282-skill/03-exe-logic.md` |
| 图层播放 / 帧序 / 生命周期 / 伪码 | `docs/tms282-skill/04-vfx-playback.md` |
| 51 职业技能清单（2454 行，逐职业表） | `docs/tms282-skill/05-job-inventory.md` |
| 迁到本项目 Unity `NativeSkill*` 的缺口 | `docs/tms282-skill/06-migration.md` |
| 五/六转任务链 + 核心获取与消耗 | `docs/tms282-skill/07-job-advancement.md` |
| 星力：概率/费用/破坏/属性/UI/包协议 | `docs/tms282-skill/08-star-force.md` |
| 1618 条结构化数据 | `docs/tms282-skill/data/skills-5th6th.json` |
| 120 字段 → 结构体偏移 | `docs/tms282-skill/data/skillinfo-fields.tsv` |
| 图层名 → 枚举值 | `docs/tms282-skill/data/effect-layer-enum.tsv` |
| 星力 30+15 档规则表 | `docs/tms282-skill/data/starforce-table.tsv` |

---

## 9. 技能「子状态 id」：同名技能族里的 invisible buff（2026-11-05）

**结论**：282 的六转技能族里有一类 `type=buff / invisible=1` 的 id **不是玩家能按的技能**，
而是母技能施放后由客户端挂上的内部状态。本项目 `check_skill_effects.py` 曾把它们
误算成「buff 但无 stat／按下没反应」（19 条里的 12 条），属于统计口径错误，不是 12 个待做技能。

**四条判据**（都可复现，工具 `tools/ms282/export_skill_states.py`）：
1. 形状：`level/1` 只有 `time` / `subTime` / 单个字母，无 `mpCon` / `damage` / `attackCount`；
   （`1241501` 例外：`common` 里带了母技能冷却的投影 `cooltime=360`，但原始节点
   `Skill/124.img/skill/1241501` 只有 `common/tile/screen`，**没有 info / effect / level**，
   是领域状态对象，不是可施放技能 —— 形状判据要看节点，不能只看字段名）；
2. 文案：`String/Skill.img`（BMS 密钥）里这些 id **只有 desc、没有 h**，同名兄弟 id 有完整 h
   （`2141502`↔`2141500`、`2341012`↔`2341011`、`5141501`↔`5141500`、`14141021`↔`14141017`）；
3. 数值：状态 id 的 `common` 是母技能 `level/1` 的静态投影
   （`2141502.time=20,x=10` == `2141500.lv1`；`14141021.time=7` == `14141017.lv1`；
   `4240017.time=3,x=9` == `4240016.lv1.w=3,v=9`）；
4. 语义：母技能 h 直接写出状态效果 ——
   `11141500`「使用時#w2秒內最終傷害增加#q2%」→ `11141501(time=20,y=q2)`；
   `2341011`「賦予無視HP歸零一次的加持效果」→ `2341012(time=600)`；
   `5141500`「在#time秒內…使用五轉技能時觸發水龍鬥氣」→ `5141501.skillList` 13 条 +
   5 个伤害体 `5141502..6`（damage 589/897/930/992/1136 与母技能 y/w/v/s4/q 逐级对应）；
   `4240016`「隱身術持續#v秒以上時，強化為暗影之刃」「在煙霧彈或暗影霧殺內…立即套用」
   → `4240017(time=w=3)` + 升级伤害体 `4241019`（q2/q/s），常态体是 `4241018`（x/u/y）。

**迁移边界**：`NativeSkillStates.json` 现登记 **13 条** parent/time/role/evidence
（2026-11-05 补入 `1241501 聖域展開`，见 §11）；运行时 `NativeSkillStates` 只做
「母技能施放 → 记时限 → 供查询」。已接真实消费方的 10 条：
`11141501`（FinalDamage 时效增益，alias y→FinalDamage）、`2341012`（致死免疫一次）、
`5141501`（水龙斗气 2s 冷却 / 最多 8 次 / 5 段连击）、`4240014` + `4240017`（暗影之刃升级）、
`2241504` / `3141501` / `4141502` / `14141021`（§10 周期领域）、
`1241501`（§11 圣域 procGate，500ms 冷却 / 不限次数），
以及 §12 补齐的最后 3 条：`2141502`（元素吸收锁满 10 层 → FinalDamage +30）、
`5141001`（超级闪‧连杀技能替换）、`4241005`（黑暗面隐身回避 + 冷却）。
**13 条全部有真实消费方**，不再有 timer-only 状态。

**验证**：`python3 tools/ms282/export_skill_states.py`、`check_skill_states.py`、
`check_skill_effects.py`（无 stat 数 19→7→4→**1**，只剩 `400001004`）、
`check_skill_mechanics.py`；Unity 编译 0/0。

---

## 10. 周期领域：子状态期间按 `subTime` 反复结算同族伤害体（2026-11-05）

**结论**：§9 里「只是计时器」的 4 条其实都能落地，而且数值不需要猜。282 在这些 VI
母技能的 h 里把周期、次数、目标数、追加伤害全写死了，同族的 invisible 攻击体就是
伤害体，其 `damage/attackCount/mobCount` 与母技能的字母字段 **30 级逐行相等**。
子状态的 `subTime` 是毫秒周期，与 h 里的「每 #s2 秒 / 每隔 #s 秒」严格对得上：

| 状态 / 母技能 | 周期 | 持续 | 伤害体 | 逐级自证 |
|---|---|---|---|---|
| `2241504` / `2241500` 殛凍領域 | `subTime=2000ms` = `s2(2)×1000` | `time=30` | `2241501` | `damage=q2` `attackCount=q3` `mobCount=s` |
| `3141501` / `3141500` 箭影紛飛 | `subTime=3000ms` = `s(3)×1000` | `time=30`，最多 `q2=7` 次 | `3141502` | `damage=x` `attackCount=y` `mobCount=z` |
| `4141502` / `4141500` 禁咒之符 | `subTime=1900ms` | `time=60` | `4141501` | `damage=y` `attackCount=v` `mobCount=u` `bulletCount=z(5)` |
| `14141021` / `14141017` 闇黑天魔VI | 召唤体 `14141020.subTime=1470ms` | 召唤体 `time=60`（状态 `time=7` 只是触发窗口） | `14141020` | `damage=w2` `attackCount=w3` `mobCount=w` |

h 原文（判断触发条件的依据）：
- `2241500`「#time秒內出現流動閃電的冰寒地帶／每#s2秒…#c魔力迸發#：最多對#s名敵人以#q2%的傷害攻擊#q3次」；
- `3141500`「最後一次箭矢洗禮後，#time秒內**攻擊命中時**，每隔#s秒發動…#q2次」⇒ 不是自动周期，要命中才启动，且总次数上限 7；
- `4141500`「生成#z個在#time秒內經過隨機路徑、以#y%的傷害對最多#u名敵人攻擊#v次的巨型飛鏢」，
  desc「永劫時空中形成的飛標會**優先追蹤最大HP最高的BOSS怪物**」⇒ 复用 `SeekingTargets` 的 boss/maxHP 排序；
- `14141017`「套用#c暗影蝙蝠期間# #s秒內，每#t2秒召喚…弱化蝙蝠群」⇒ 状态窗口内**后续技能命中**才召唤。

**落盘**：`Assets/Resources/NativeFieldZones.json`（4 条），
`tools/ms282/export_field_zones.py` 只登记「字段名映射」，具体数值从 282 表逐级读并自校验；
`check_field_zones.py` 五项守卫（表结构 / 数值回算 / C# 逐键消费 / 反向孤儿 / 接线）。
运行时 `Assets/Scripts/NativeFieldZones.cs`（`SkillSystem` 的 partial）：
- `StartFieldZones` 接在 `NativeSkillStates.Activate` 之后（onCast 两条）；
- `TriggerFieldZonesOnHit` 接在 `TriggerReactiveSkills`（onHit 一条、状态窗口内技能命中一条）；
- 协程复用 `FireProc` 的换算管线：母技能 damage% 面板行 → 伤害体 damage%，
  范围用母技能 `lt/rb`，换图/死亡/重复施放用 generation 中止；`ClearFieldZones` 挂换图清理。

**简化口径（与原版的已知偏差）**：`area` 模式每周期按母技能范围取 `mobCount` 个目标各打一次伤害体；
`boss` 模式（`4141502`）每周期让 5 个飞镖各追一个目标（取 boss/maxHP 排序前 5），
未实现单个飞镖的「最多 15 名溅射」与随机路径动画。特效仍走 `PlayNativeReplayEffect(伤害体 id)`。

**后记（同日续做，见 §12）**：当时列在这里的 `2141502` 元素吸收、`4241005` 隐身回避、
`5141001` 技能替换，以及 4 条真缺口里的 `5141011` / `400041001` / `400041061`
都已落地成真实机制；`4240014` 暗影雾杀仍由暗影之刃 proc 的 `AltState` 消费。
**唯一未实现的是 `400001004` 實用的戰鬥命令**（等级 +1，需改 `SkillBook` 等级解析），
按用户要求本轮不做。

**验证**：`export_field_zones.py` / `check_field_zones.py --table` 4 条 0 不一致；
Unity 编译 0/0、server-check 2246/0、面板 5 守卫 + 4 个技能守卫全 PASS。

---

## 11. 剩余缺口的定性：圣域子状态 / 刺客刻印 / 强化核心（2026-11-05）

把 §9–§10 之后「buff 仍无 stat」的 7 条逐条查 282 原文，其中 **3 条不是待做功能**：

### ① `1241501 聖域展開` 是第 13 条子状态（口径纠正，不是缺口）
`Skill/124.img/skill/1241501` 只有 `common/tile/screen/variableNumList`，
**没有 `info` / `effect` / `level`**；`String/Skill.img` 里 h 为空，只有 desc。
母技能 `1241500` 有完整 h：
「連續#u秒內形成聖域，聖騎士在聖域內進行攻擊時，發動最多對#v名的敵人以#y%的傷害攻擊#z次的審判之光，
冷卻時間是#t秒」。`1241501.time=30 == u`；`common.cooltime=360` 只是母技能冷却的投影
（所以形状判据不能只看字段名，见 §9 ①）。

审判之光伤害体 `1241502`：`skillList` 15 条触发名单、`cooltimeMS=500`、
`damage=600+20*x`（620..1200）== 母 `y`、`attackCount=13` == `z`、`mobCount=5` == `v`，
**30 级逐级相等**。母技能 `t=0` 是占位，冷却必须取伤害体的 `cooltimeMS`，
把 500 当秒会变成 8 分钟一次。
落 `NativeSkillStates.json`（`role=procGate`），复用海龙霸拳的 `TriggerStateChains`：
扩展支持 `cooldownMs` 与 `maxActivations=0`（不限次数，由状态 30 秒收口）。

### ② `4141003 刺客刻印VI` 是两段式 proc，标记按**敌人个体**记
h「攻擊時以#prop%的機率留下暗殺者的標記，再次攻擊該敵方時，生成並發射#bulletCount個
傷害#dummyStr%的飛鏢」；desc「飛鏢會優先攻擊最大HP最高的敵方」。
伤害体 `4141004.damage` 与 `dummyStr` 30 级逐级相等（317..520）。
新增 `ProcMode.MarkPerTarget`：第一次按 `prop=65%` 给该敌人打标记（`dotTime=20` 秒寿命），
再次命中同一敌人时消耗标记，在 `range=500` 内按 boss/maxHP 降序取 `bulletCount=3`
（不超过 `mobCount=4`）个目标结算飞镖。**不能**复用 `TryBrand` 的按技能 id 全局计数。

### ③ `500004073 強化暗影侍從/暗影瞬移` 是字段覆盖，不产出 stat
h「暗影侍從延伸的最終傷害反映率#x%，暗影瞬移持續時間增加為#time秒」：
`x=76..105` 覆盖 `400041028.x=46..75`（暗影飞镖倍率），
`time=46..55` 覆盖 `400041095.time=30..45`（暗影瞬移时长）。
两条都是**替换不是相加**（级差与 400041028.x 完全相同，整体抬高 30）。
落 `NativeSkillOverrides.json` + `export_skill_overrides.py` / `check_skill_overrides.py`；
`NativeSkillOverrides.Scale/DurationSeconds/IsDurationTarget` 分别接到
`NativeProcSkills` 的倍率换算、`NativeBuffs.SkillDuration`、`ApplySkill` 的 summon 放行。
顺带修了暗影瞬移本体：`info` 没有 effect/skillType，`ApplySkill` 原会因为
`OverTime()` 与 `stats.Count==0` 双重拒绝 ⇒ `NativeSummon.HasSource` 为假、影子立刻消失；
现在用 `SUMMON_DURATION` 注册标记放行（`shared` 里已声明）。

### 剩余真缺口（已在 §12 收敛到 1 条）
`5141011` 海龙石、`400041001` 散式投擲、`400041061` 飛閃起爆符以及 3 条 timer-only
状态都在 §12 落地。**只剩 `400001004` 實用的戰鬥命令**（`x=1` 全技能等级 +1，
按 desc 只限 4 转及以下且排除 5/6 转/超技能，需动 `SkillBook` 等级解析，影响面最大），
本轮按用户要求明确跳过。

**验证**：`export_skill_states.py` 13 条 0 不一致、`export_skill_overrides.py` 1 条 0 不一致；
6 个技能守卫 + 面板 5 守卫全 PASS；Unity 编译 0/0、server-check 2246/0。

---

## 12. 最后 6 条：资源 / 追加方向 / 符咒 / 技能替换 / 元素吸收 / 隐身回避（2026-11-05）

把 §9–§11 之后剩下的 3 条「buff 无 stat」真缺口（`5141011` / `400041001` /
`400041061`）与 3 条 timer-only 子状态（`2141502` / `5141001` / `4241005`）全部落地。
**它们都不是属性 buff**，所以 `NativeBuffs.SkillStats` 永远返回空、按下去没有反应；
每条需要自己的实体。数值全部来自 `Assets/Resources/NativeSkillMechanics.json`
（`tools/ms282/export_skill_mechanics.py` 逐级自证 + h 原文证据），C# 不复制伤害常量。

### ① `5141011 海龍石VI` — 资源计数 + 突击状态 + 被动加伤
h「消耗`#u`個海龍石，`#time`秒內可發動突擊技能」「每次發動海龍技能時可獲得1個海龍石，
最多可持有`#u`個」「`[被動效果：海龍之怒VI傷害增加#damPlus%p]`」。
`u=5 / time=15 / w3=10`。触发名单来自 282 原始节点的两份 `skillList`：
`5141013`（海龙突击VI）= 5101012 / 5111002 / 5121016 / 5141015，
`5141014`（海龙怒击VI）= 5121007 / 5141000 / 5141002。
两个伤害体的 `damage/attackCount/mobCount` 与母技能 `damage/attackCount/mobCount`、
`z/w2/w` **30 级逐行相等**（母技能 common 里就写着 `465+8*x` / `463+13*x` 的投影）。
运行时：命中名单内技能 +1 石（上限 5）；攒满才能按键（`MechanicCastGate` 在扣 MP / 物品
之前拒绝，不会空放）；15 秒状态内每次命中结算两个伤害体，最多 `w3=10` 次
（h「對同一名敵人發動`#w3`次攻擊時消失」）。
**简化口径**：`damPlus` 乘在同族突击伤害体的倍率上 —— 原版指名的 `5140004/5141006/5141008`
是 passive / invisible 体，本工程没有独立施放链，直接照抄会变成死代码；
`FireDragonBonus` 仍保留，若那两个体将来作为 `cast` 出现会另外结算。

### ② `400041001 散式投擲` — 追加方向
h「`#time`秒期間使用投擲類技能時一次向多個方向投擲／三飛閃：追加4方向，
追加方向以`#u`%的傷害攻擊最多`#dummyStr`名敵人／四飛閃：追加3方向，
追加方向以`#damage`%的傷害攻擊最多`#dummyStr2`名敵人」。
`Skill/40004.img` 里 `400041017.common.damage=290 == 母技能 u`、
`400041018.common.damage=379 == 母技能 damage`；追加方向 4/3 与这两个体的
`bulletCount` 3/4 互补（合计 7）。触发名单取 `400041061.skillList` 的 13 条投掷技能。
运行时：20..30 秒窗口内投掷技能命中 → 按三/四飞闪分组结算 `directions` 次，
每次对 `dummyStr/dummyStr2=4` 名目标打 `u/damage`%。未实现方向动画，
用 `ProcTargets` 的目标池代替「多个方向」，属于已知偏差。

### ③ `400041061 飛閃起爆符` — 符咒计数，主/被动互斥
h「投擲手裏劍的攻擊技能命中敵人時…最多`#v`名敵人…`#z`%的傷害攻擊`#q`次的爆炸」
「每`#u`秒會準備1個爆炸符咒」「`#time`秒內可使用`#x`個爆炸符咒」「主動效果和被動效果
不會同時發動」「最少`#s`個到最多`#w`個符咒」。
`x=46 / time=60 / u=10 / subTime=10000ms == u×1000 / s=2 / w=4 / q=5 / v=6`。
爆炸体 `400041062.common.damage=475+19*x` 与母技能 `z`（494..1045）**30 级逐行相等**，
`attackCount=q=5`、`mobCount=v=6`。
运行时：主动施放给 46 个符咒并关闭被动累积；窗口外每 10 秒攒 1 个；
投掷技能命中消耗 `min(可用, w)` 个（不足 `s=2` 不触发），每个符咒结算一次爆炸。
爆炸体不在 UI 技能表里，倍率直接读母技能 `z`，登记为 `bodyFromParentField`。

### ④ `5141001 超級閃‧連殺` — 技能替换
`5141000` h「發動海龍怒擊時，`#x`秒內可使用超級閃‧連殺」，`x=10 == 5141001.time=10`；
desc「海龍怒擊發動時，一定時間內閃．連殺VI變更為超級閃．連殺」。
`5141014.skillList` 是「发动海龙怒击」的触发名单；替换体 `5141002`（本体 3 段）+ 冲击波
`5141003`（10 段，`damage == 5141000.w2`、`attackCount == q2`、`mobCount == v2` 逐级相等）。
运行时 `ResolveMechanicReplacement` 在 `BeginCast` 最开头把 `SkillDef.native` 换成
`5141002`，命中后 `FireReplacementShock` 追加 `5141003`。这是**真替换**，不是追加伤害。

### ⑤ `2141502 劇毒煉獄` — 元素吸收上限
母技能 `2141500` h「`#time`秒內元素吸收的上限擴充至`#x`堆疊數」，
desc「讓元素吸收的上限提升，**固定為最大堆疊數**」，`x=10`。
元素吸收本体 `Skill/210.img/skill/2100009.common = {x:3, y:5}`，
h「按照自己的技能來給的持續性傷害，增加最終傷害`#x`%。最多適用`#y`個」
⇒ 每层 +3% FinalDamage、默认上限 5 层；状态期间锁满 10 层 = **+30%**。
落 `NativeSkillStates.json` 的 `timedEffect` + `effectKey/effectPct/elementAbsorbCap`，
`NativeSkillMechanics.AddTimedEffect` 注入 `NativeBuffs.SkillStats`，走既有 `shared` 的
`FinalDamage` 与 `setItemBonus` 管线，没有新开乘区。
**简化口径**：本工程没有「按自己 DOT 层数」的实体，按 desc 直接锁满层数，
没有实现逐层累积过程。

### ⑥ `4241005 黑暗面` — 隐身回避 + 解除后冷却
母技能 `4001003` h「迴避依照最大HP的比率造成傷害的攻擊時，會等隱身術解除後
依照迴避的次數套用冷卻時間`#cooltime`秒，最多可重疊到`#y`次」；
`4001003.common = {cooltime:3, y:5, w2:20}`，状态 `4241005 = {time:6, w2:20, subTime:4000}`。
本工程 `TakeBossDamage` 是 Boss 百分比/固定伤害的唯一通路（`NativeMobController` 的
`fixDamR` / `deadlyAttack` 都走这里），所以挂在这里最贴合「按最大HP 比率的攻击」。
运行时：隐身状态期间整段回避并计数（单窗口上限 `w2=20`）；状态结束或再次按隐身术键
（desc「再次使用隱身術技能鍵即可解除」）时按 `min(次数, y=5) × 3 秒` 给隐身术叠冷却，
`MechanicCastGate` 在施放前拦住。物理接触伤害不受影响。

### 落盘与接线
- 新增 `NativeSkillMechanics.json` + meta、`Assets/Scripts/NativeSkillMechanics.cs` + meta
  （`public static class NativeSkillMechanics` 读表 + `SkillSystem` partial 跑实体）、
  `export_skill_mechanics.py` / `check_skill_mechanics.py`（五项守卫）。
- `SkillSystem.BeginCast`：`ResolveMechanicReplacement` → `MechanicCastGate`（**所有消耗之前**）
  → `OnMechanicCast`；`CancelActions` 加 `ClearMechanics`。
- `NativeSkillFollowups.TriggerReactiveSkills` 加 `OnMechanicHit`（命中后结算，复用
  `ProcTargets` / `ProcSource` / `NativeSkillReplay.Adapt` / `ApplyReplayHit`）。
- `PlayerCtrl.TakeBossDamage` 加 `TryEvadeHpRatioDamage`；
  `NativeBuffs.SkillStats` 的 timedEffect 分支加 `AddTimedEffect`。
- `check_skill_effects.py` 的 special 加入 6 条 ⇒ 「buff 无 stat」 **4→1**，
  只剩当时按用户要求跳过的 `400001004`（§13 已补完，现为 0）。

**验证**：`export_skill_mechanics.py` 7 条 0 不一致（§12 的 6 条 + §13 的 `400001004`）；`check_skill_mechanics.py`、
`check_skill_states.py`（13 条）、`check_skill_effects.py`、`check_proc_skills.py`、
`check_shields.py`、`check_field_zones.py`、`check_skill_overrides.py` 全 0 不一致；
Unity 编译 0 Warning / 0 Error、server-check 2246/0、面板 5 守卫 PASS。

## 13. 实用的战斗命令 `400001004`：技能等级 +1（2026-11-05）

§9–§12 之后「buff 无 stat」只剩这一条，用户最后要求补掉。它不是属性 buff，
**时效侧连一条 stat 都产不出来**，所以 `NativeBuffs.ApplySkill` 过去直接
`return false`：按下去没图标、没效果、没任何提示。

### 原始节点（`Skill/40000.img/skill/400001004`）
```
info    = { pvp:0, type:10 }                       // 10 = buff
common  = { x:"1", time:"180+3*x", asrR:"u(x/5)",
            lt:(-400,-300), rb:(400,300),
            cooltime:"180", hpCon:"100", maxLevel:30 }
skillType / effect 节点：有 effect、无 skillType、无 hit/ball ⇒ OverTime() 判 true
```
h：「消耗HP `#hpCon`，在`#time`秒内所有技能等级+1／冷却时间 `#cooltime`秒／
`[被动效果：状态异常抗性 #asrR增加]`」。
所以这一条技能其实有**两个互不相干的产物**：

| 产物 | 字段 | 性质 | 通道 |
|---|---|---|---|
| 所有技能等级 +1 | `x=1`，持续 `time`（183..270 秒） | 时效（自己不能产出 stat） | `SKILL_ORDER` 标记 + `NativeCombatOrders` |
| 状态异常抗性 1..6% | `asrR = u(x/5)` | 括号内 = **永久被动** | `NativeSkillEffects` 的 `ASR` 键 → `NativeMobDebuffs` |

### 谁能被提升（判据来自 desc 原文）
desc：「一定时间内提升自身的所有技能等级。**限于4转技能可以增加到最大等级以上**，
其他技能只能提高到最大等级为止。部分例外的特别技能和初心者技能、战斗命令、
实用的战斗命令、超技能、5转、6转技能的等级是无法提高技能等级。无法与战斗命令重复使用。」

判据落在 `Assets/Scripts/NativeCombatOrders.cs`：
- `id / 10000 % 10 <= 2` ⇒ 一/二/三/四转可提升。四转 = 个位 2（含骑士团的 1100/1110/1111/1112）。
- `id / 10000 % 10 >= 3` ⇒ 五转觉醒（四转+1）、六转（四转+2）与超技能编码，全部不动。
- `id >= 400000000` ⇒ 五转 V 核心 / 六转 V 编码 / 强化核心 `400004xxx`，不动。
- `id / 10000 % 1000 == 0` ⇒ 初心者与职业群共通（1000、2000…，精灵的祝福这类），不动。
- `autoLearn`（精灵的祝福）与 `hexa`（`1141000` 这类六转）另外再挡一层。

**为什么不用 282 的 `combatOrders` 节点**：282 的每条技能确实带一个 `combatOrders`
（int，全库只有值 `1` 的 750 条、其余无节点），但它只写在 **282 自己保留的那批技能**上 ——
`JobTree` 里 476 条「282 包里也有」的技能只有 43 条带这个标记，其余四转技能一条都没有
（`Skill_112.img` 全包只有 24 个技能节点，`1121001 磁石` 根本不在 282 里）。
拿它当唯一判据会让绝大多数四转技能失效，比不判更糟；北斗时代的 WZ 则完全没有这个节点。
判决记在代码注释里，后续不要再拿节点当判据。

### 上限
- 四转：`MasterCapOf + bonus`（技能册的 `masterLevel=10` 可以被越过）。
- 其他：`MasterCapOf`（非四转时它本来就等于 `maxLevel`），即「只到最大等级为止」。
- 两侧最后都夹到 WZ 的 `maxLevel`：282 的技能表只有 `1..maxLevel` 行，31 级没有数值可读。
  ⇒ 本工程里**满 30 级的四转技能提升后数值不变**（客户端原版存在 31 级行，这是数据缺口）。
  按当前 JobTree 统计：可提升技能 414 条，其中满加点时会真的 +1 的是 37 条
  （全是 `masterLevel=10` 的四转技能，10 → 11）。

### 运行时接线
- `NativeBuffs.SkillStats`：`kind == "levelBoost"` 时写 `SKILL_ORDER = x` 进 stats
  （`shared`，否则 `Register` 的 singleton 分支会顶掉别的 buff），值就是加成级数；
  生效期与 buff 图标由这一条 effect 承载，过期照旧走 `Tick`。
- `NativeBuffs.SkillLevelBoost`：只读 `winners["SKILL_ORDER"]` + 判 `Until`，**刻意不 Tick**
  —— `SkillProgress.LevelOf` 在每次伤害结算里都会走这条路，加 Tick 会顺手重建 buff 表
  并触发 `RefreshBuffAttributes`，既慢又容易递归。
- `SkillProgress.LevelOf` = `BaseLevelOf` + 加成（夹到 `NativeCombatOrders.Cap`）。
  **加点 / 洗点 / 技能册 / SP 统计一律用 `BaseLevelOf`**（`Raise` / `WhyNot` / `SpentSp` /
  `BeginnerSpent` / `Reset` / `Profile.ResetSp` / `NativeSpResetWindow` /
  `NativeItemUseEffects`）：拿加成后的等级去比上限会把「+1 之后还能不能加点」判反，
  `Raise` 还会白扣 1 点 SP。
- 状态异常抗性：`NativeMobDebuffs.ResistancePercent` = `setItemBonus["ASR"]`（永久被动）
  + `NativeBuffs.Value("ASR")`（时效，`400011066 钢铁之躯` 的 `indieAsrR`），
  在 `Apply` 最前面按 `Random < 抗性%` 抵抗 —— 被抵抗掉的状态不占「最多两种病」的名额。
  这条通道原先不存在（`asrR` 一直躺在 `export_skill_effects.py` 的 `UNCONSUMED` 里），
  现在两个字段（`asrR` / `indieAsrR`）都映射到 `ASR`，`UNCONSUMED` 已清空。

### 已知偏差
- 「无法与战斗命令重复使用」：282 里另一条 `战斗命令`（`1211011`）不在本工程技能表，
  没有可叠加的对象，未做互斥判定。
- 满 30 级四转技能提升后数值不变（见「上限」）。
- `use_buff_everlasting` 开启时 `SkillDuration` 会把已学 buff 的时长拉到极大，
  这条 V 技能同样受影响 —— 那是北斗服务端配置的既有口径，不是本机制的判断。

**验证**：`export_skill_mechanics.py` 7 条 0 不一致（`time=180+3*x`、`asrR=u(x/5)`、
`x≡1` 逐级自证 + `NativeSkillEffects` 被动表交叉比对）；`check_skill_mechanics.py`
七项守卫 0 不一致；`check_skill_effects.py` 的「buff 仍无 stat」**1 → 0**；
Unity 编译 0 Warning / 0 Error；server-check 2246/0；面板 113 项 PASS。

