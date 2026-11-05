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
