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

### 6.1 五转核心面板与圣骑士技能复核（2026-11-05）

- `VCore.img/coreGroup/122` 的四个圣骑士职业核心已核实为 `10010002 神聖團結 → 400011003`、`10010014 祝福之鎚 → 400011052`、`10010027 聖十字架 → 400011072`、`10010042 雷神戰鎚 → 400011131`；`VMatrix.json` 与 `VMatrixSkills.json` 均存在，`VMatrixSlots.Assign` 职业段容量为 4，故聖十字架不应缺席职业核心格。`400011052` 的 h/common 字段逐级导出为 `time=30`、`damage=546`、`mobCount=8`、`attackCount=4`、`attackDelay=1800`，类型为 `attack`，不是被动。
- 282 原端特效层证据：400011003 有 `special/special0` 及 `specialAffected*`，400011131 有 `effect/effect0`、`special/0` 与 `special/front`；运行时须枚举分支和子分支并读取占位帧的 origin/delay/outlink。项目 `NativeSkillVfx` 已按该规则读取并在 `SkillVisuals` 播放 effect/special/video/分段层；`check_skill_vfx.py --vs282` 全量结果为项目无“源端有像素而项目整层为空”的条目，剩余 97 处与源端同为空、51 处为源端无对应分支，不能据此宣称所有演出已完成。
- 特殊核心穿透复现及修复：`NativeVMatrixWindow.Draw` 的 `specialOwnsGesture` 在松手帧不能提前清零；现在保持到 `Released` 处理结束，并以子窗矩形 hover 继续屏蔽底层。`tools/check_matrix_pointer.py` 增加 7 项 overlay 手势检查，连同原有 Pointer mask/DrawAll 检查通过。
- 用户模拟器复核补充：用户确认当前实际看到的“聖十字架”效果对应 `鋼鐵之軀(400011066)`，而 `神聖團結(400011003)` 只播到前半段；这与 VCore 核心表的连接 ID 不一致，属于运行时入口/按键绑定或旧角色快照映射问题，不能改动 VCore 静态连接表猜测修正。离线预览显示 400011003 的完整后续层为 `specialAffected/specialAffected1/specialAffected2`，此前 `SkillVisuals` 只播 `affected`，已补齐 `Layers(s.vfx,"specialAffected")`。钢铁之躯的 400011066 原端预览只有 `effect`，因此蓝色十字架不来自该技能资源本身，需在 Unity 模拟器用实际绑定复现入口映射后继续定位。
- 截图复核：截图中十字图标与“鋼鐵之軀”悬浮说明同时出现，证明需要同时记录 coreId、connectSkill、SkillBook id、tooltip 数据名和 VFX key；不能只按图标外观判断技能。TMS282 客户端 `VCore.img` 与 `vmatrix-data.json` 均核实 `coreGroup/122` 第三职业槽为 `10010027 → 400011072 聖十字架`，`鋼鐵之軀` 是共通核心 `10010025 → 400011066`，两者静态连接没有互换。

#### 复现与边界

- 二进制身份：TMS282 客户端 `MapleStory.exe` SHA256 `def89cf3c51b5d196b90776d64a68898d722c26f8ce14f97f8c3930e9c4cfc27`，函数边界按 `.pdata`；本轮仅复核技能 ID 字符串引用及 IMG 节点，未把调用点推断成完整技能处理器。
- 资源检查：`tools/ms282/check_skill_vfx.py --vs282`；技能数据检查：`tools/ms282/export_vhexa_skills.py --check`（526 核心技能，0 缺伤害等级、0 attackInfo 覆盖缺口）；面板检查：`tools/check_vmatrix_layout.py`、`tools/check_matrix_pointer.py`。

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


## 14. 五六转全量回归：三条「看着有、其实没接」的通道（2026-11-05）

把 710 条五六转技能（V 376 + 五转觉醒/超技能 334）按「运行时通道」逐条打标后，
回归出三类**守卫看不见**的缺口 —— 它们都能通过「表结构自洽」检查，因为出问题的地方
在**数据形状**上，而不是在某张表里漏键。

### ① `5241009 轟炸風暴VI`：数值全在 `dummyStr` 系列

原始节点只有 `info/type=33` 与 `common/{dummyStr=6, dummyStr2=643+12*x, dummyStr3=9, dummyStr4=2}`，
一个 `damage` 都没有。h 才是字典：

```
攻擊命中時對目標進行轟炸，以#dummyStr2%的傷害攻擊最多#dummyStr名敵人#dummyStr3次
砲擊再發動冷卻時間 #dummyStr4秒
```

⇒ `dummyStr2=damage`、`dummyStr=mobCount`、`dummyStr3=attackCount`，
`dummyStr4` 是**秒**（不是毫秒），所以不进 levels，由 `ProcSpec.CooldownField` +
`CooldownUnitMs=1000` 直读原字段。

落地：
- `export_advanced_skills.COMMON_ALIAS` 加这条映射（导出期摊平，运行时零改动）。
  注意 `dummyStr4` **不能**摊进 levels —— 摊进去会被当成毫秒。
- `PROC_SKILLS` 显式登记为 passive（它本来靠兜底也会判 passive，登记是为了留取证）。
- `NativeProcSkills` 加 `new ProcSpec(5241009, Roll, cooldownField:"dummyStr4", cooldownUnitMs:1000)`。
  h 没写概率 ⇒ prop 缺省 100，每次命中触发，只受 2 秒再发动冷却限制。

### ② 12 条 `damPlus` 被动：强化的是**另一条技能**

五六转技能的被动段普遍长这样：

```
[被动效果：狂暴攻擊VI的傷害增加#damPlus%p]
[被动效果：閃‧連殺VI傷害增加#u%p、超級閃‧連殺傷害增加#damPlus%p、
           超級閃‧連殺衝擊波傷害增加#v%p]
```

`damPlus` / `u` / `v` 既不是属性 stat（进不了 `setItemBonus` 的结算），也不产生伤害体
（不是 proc），所以过去**零消费** —— 12 条技能的被动强化等于没做。

落地：`NativeSkillBoosts.json` + `NativeSkillBoosts.Pct(target)`，
在 `SkillBook.Native()` 出口把 `damage × (100+Σpct)/100`。
放这个位置是因为 proc / 召唤 / 普通施法三条线都从 `Native()` 取定义，
且 `data` 每次都是新建副本，不会重复放大。

**目标 id 的定法**（表在 `export_skill_boosts.py`，逐条带 h 片段自证）：
1. h 里点名的技能名必须出现在 `h` 原文里；
2. 目标要么在来源的 `psdSkill` 里（数据层引用），要么名字确实出现在 h 里；
3. 名字重名时取**实际结算伤害的那一个**：
   - `4141008 → 4141004`（4141003 只是 proc 触发体，伤害在 body 上）
   - `5241015 → 5241009`（proc 的 body 自己）
   - `5141015 → 5141003`（5141002 的追加冲击波体）

全套 12 条来源 / 14 条目标；`check_skill_boosts.py` 守卫五项 + 跨技能取数值一项。

### ③ `500001005 靈魂赫卡忒`：伤害在 `psdSkill` 引用的子技能上

它 `info/type=33` + 有 `summon` 节点，但 `common` 只有召唤参数（lt/rb/attackDelay/speed/x=50…），
`damage` 与 `attackCount` **一个都没有** —— 这两项在 `psdSkill` 的 `500001010` 上：
`damage=660+22*x`、`bulletCount=4`、`attackCount=2`，与 h 的三个占位符逐项相等。
不补的话 `NativeSummon` 拿到的 `DamagePercent` 恒为 0，**召唤出来站着不打**。

落地：`export_vhexa_skills.LINKED_ATTACK` 在导出期补字段（运行时零改动），
导出条目带 `linkedAttack:{from,fields}` 便于反查。

### 查实后**不是**问题的几类（别再重复排查）

| 现象 | 实际 |
|---|---|
| `400001036/39/59` attack 无 damage | 走 `common/attackInfo/<四转ID>`，`SkillBook.JobAttackRow` 按 `Profile.JobId` 取行（id/10 落回同支线） |
| 12 条 invisible passive 零引用 | 内部标记/动作体（`invisible` + common 无伤害无时长），不需要独立消费 |
| 14 条 attack 无 attackCount、28 条无 mobCount | 282 省略即默认 1 / 单发，不是缺字段 |
| 6 条 buff 无 time | proc 触发体（刺客刻印）或瞬发型（连接绳索/瞬移/满载骰子） |
| 174 条 `req` 指向不在技能表的前置 | 前置是 282 重做的四转技能（`1121052 愤怒爆发` 等），本工程走 JobTree 那套。`req` 只在 `SkillBook.WhyNot` 的 SP 加点路径生效，而 hexa 技能在它之前就 `return`，不影响玩法 |

### 仍未做（需要新机制，别按「补字段」去修）

- 已在本轮补齐，见 §15：`400031055 連射十字弓彈`、`400041025 黑影切斷`、`400011055 極樂之境`。
  它们不是「buff 期间解锁子技能」这种需要凭空造的系统 —— 机制本来就在数据里
  （`psdSkill` 指向 invisible 攻击体 + `isSequenceOn`），只是本项目没接线。

**验证**：`check_skill_boosts.py`（12/14，0 不一致）、`check_proc_skills.py`（24 条）、
7 个技能守卫 + 8 个矩阵/面板守卫全 PASS；Unity 编译 0 Warning / 0 Error；
server-check 2246/0。

## 15. 形态技能：`psdSkill` 子技能 + 三种消费入口（2026-11-05）

§14 的「仍未做」把这三条记成需要新机制。查原始节点后结论相反：**机制一直在数据里**，
只是伤害不在母技能身上 —— 母技能是「开形态 + 给次数」，真正的攻击体是 `psdSkill`
指名的 invisible 子技能。过去没接线，于是按下母技能等于开一个空 buff。

### 判据：`psdSkill` 指名 + 子技能是 invisible 攻击体

```
Skill/40003.img/skill/400031055
  info/type = 15            ← 「連射/形态」类
  psdSkill/400031056
  isSequenceOn = 1
  common/{mpCon=750, cooltime=120, time=60, x=8, y=150, w=500+20*x, u=9, v=4, cr=100}

Skill/40003.img/skill/400031056      ← invisible=1，不在技能窗、不能加点
  common/{maxLevel=30, mpCon=150, damage=500+20*x, attackCount=9, mobCount=1,
          lt=[-780,-190], rb=[10,100]}
  multiAttackInfo/0..3（attackTime 0/120/120/120）  ← 4 轮 = 母技能 v「發射 4 支」
```

**自证链**（导出脚本期强制，逐级比对，对不上直接失败）：

| 母技能 h | 母字段 | 子技能字段 |
|---|---|---|
| `#c極速射擊#：消耗MP #y` | `y`=150 | `mpCon`=150 |
| `發射#v支以#w%的傷害攻擊#u次` | `w`=500+20x / `u`=9 / `v`=4 | `damage` / `attackCount` / `multiAttackInfo` 条数 |

`magic` 一律**继承母技能**，不看子节点：`400031056` 挂着 `info/magicDamage=1`，
但它是弩手的物理箭，按它结算会拿魔法力去打物理。

三条技能的形状：

| 母技能 | 子技能 | 触发 | 形态 | 子技能数值（lv1..lv30） |
|---|---|---|---|---|
| `400031055` 連射十字弓砲彈 (type=15) | `400031056` 極速射擊 | **再按母技能** | 60s / `x`=8 个弹匣 | 520..1100% × 9 段 × 1 目标 × 4 轮，150 MP |
| `400011055` 極樂之境 (type=33) | `400011056` 黃泉十字架 | **普通攻击被替换** | 30s / `w`=30 次 | 344..750% × 5 段 × 15 目标 × 5 轮，0 MP |
| `400041025` 黑影切斷 (type=33) | `400041026` 黑影切斷（成功版） | **伤口满层连携** | 无（靠伤口门） | 858..1815% × 7 段 × 1 目标 × 5 轮，连携不再扣 MP |

触发方式用 h/desc 原文定性，不看 `isSequenceOn`（`2241007 闪电球VI` 也带它，
但那是 summon 的召唤体，不是形态）：

- `400031055` desc「**再次使用技能**即可使用能利用箭筒的全速射擊」
- `400011055` desc「在極樂之境中，可以以#c**一般攻擊**#使用交叉冥河」
- `400041025` h「如果有傷口減益效果到達最大值的敵人，可在致命暗殺的最終攻擊和音速狂襲中
  **連接使用**，且此時會移動到該名敵人的背後」

`400041025` 还有原端的连携窗口字段：`4241000.addAttack/{skill=400041025, reqCount=1,
pattern=151, skipActionFrame=3}`、`400041039.cancelableSkillID` 含 `400041026`。
`pattern=151` 是帧级输入窗口，本工程没做帧精确输入，改用「伤口仍在」等价表达
（伤口只由这两个技能造成、10 秒，语义一致）。

### 母技能按下去要不要打一段：**由表决定，不看 Kind**

`info/type=33` 是「buff + 攻击」的复合标记，但同样是 buff 带 `damage` 的还有 5 条
**是 proc/被动的数值**，一律照打会凭空多出一发：

```
1141006 剑士意念VI / 5141011 海龙石VI / 13141003 风暴使者VI / 400041001 散式投掷 / 400041008 影之枪
```

所以 `NativeSkillSequences.json` 每条带 `parentAttack`，`SkillSystem.Cast` 的短路改成：

```csharp
if (s.native.Kind == "Summon") yield break;
if (s.native.Kind == "Buff" && !SequenceParentAttacks(s.native.Id)) yield break;
```

- `400041025` = true（h「#主動效果#:消耗MP#mpCon，最多向#v名敵人以#y%的傷害攻擊#q次」）
- `400011055` = false（母 common 的 `damage=320+13x` 是**裂缝爆炸**，不是按下去那一下）

### 伤口（`400041025` 的被动段）

h「向被致命暗殺的最後攻擊和音速狂襲命中的敵人留下#time秒間最多重疊3次的傷口減益效果。
傷口減益效果只會對**1名敵人**留下，如果對新的敵人留下減益效果，前一個則會消失」。

- 来源：`4241000` / `4241001`（致命暗殺兩段）+ `400041039`（音速狂襲），由
  `TriggerReactiveSkills` → `NotifySequenceHit` 挂接（`HitAt` 与 `BeginReplay` 共用）。
- 目标：本批命中里 maxHP 最高的那一只（desc「優先…最大HP最高的敵方」）。
- 层数 3 只写在文案里（common 没有对应字段），登记为常量并在表里注明出处。
- 换敌人 ⇒ 旧伤口立刻清零（h 原文如此），不是并存。

### 裂缝（`400011055` 的第二段）

h「以交叉冥河攻擊時斬裂空間，留下持續#y秒的裂縫…持續以交叉冥河攻擊裂縫會使裂縫逐漸擴大，
最終發動#q次爆炸，以#damage%的傷害攻擊最多#mobCount名敵人#attackCount次…裂縫在生成後#z秒
可再次生成，最多生成#u3個，裂縫持續時間結束後會消失並立即爆炸，但傷害降低為#u4%」。

所以母技能 common 的 `damage/attackCount/mobCount` 是**爆炸**的数值（`333% × 6 段 × 15 名`），
`y=10 / z=1 / q=5 / u3=10` 是常量，`u4=150+6x` 是逐级百分比。
裂缝是「被同一技能反复命中才长大」的区域实体，不是每发都炸：命中处无裂缝 → 建一个（有 `z` 秒
再生间隔、上限 `u3` 个）；已有 → 层数 +1；满 `q` 层 → 立即爆炸；到期未满 → 爆炸但按 `u4%`
折算。爆炸体按 `vMatrix` 攻击体重构造，走全屏可见范围（母技能是 buff 类，按 200px 口径
只能打到贴脸的一只）。

### 运行时为什么**不**走 `BeginCast` 递归

子技能是 invisible 攻击体，不在 `SkillBook` / 技能窗里（`LevelOf` 恒 0），走 `BeginCast`
会被「技能未学习」挡下，还会把母技能的 MP / 冷却 / 弹药口径重复扣一遍。
`NativeSkillSequences.CastSequenceChild` 只做「校验并扣子技能 MP → 扣次数 → 播动作/特效 →
按 `multiAttackInfo` 的轮数逐轮 `HitAt`」，命中管线（`RollAttack` / `TakeReplayHit` /
`TriggerReactiveSkills` / 特殊核心计数）全部复用。

**再按键分流必须早于冷却检查**：`400031055` 的 buff 是 60 秒、冷却 120 秒，形态期间一定
处于「技能冷却中」，放在冷却之后第二发会被自己挡死。`check_skill_sequences.py` 会比对
`TrySequenceRecast(` 与冷却判断在源码里的先后位置。

### 单目标多轮 ≠ 多目标多轮

`400031056` 是「4 支箭各打一个目标」，`400011056` 是「15 个目标各被打 5 次」。
不能用 `HitAt` 的 `struck` 做跨轮去重 —— 那里 `count = max(0, count - struck.Count)`，
第二发会变成 0 段。实现按 `TargetCount` 分流：`<=1` 时自己记已打目标、每轮挑一个没打过的；
`>1` 时每轮重选同一批。

### 落地与守卫

| 文件 | 作用 |
|---|---|
| `Assets/Resources/NativeSkillSequences.json` | 序列 / 伤口 / 裂缝三张表 |
| `Assets/Scripts/NativeSkillSequences.cs` | 形态、次数、三种触发、伤口、裂缝 |
| `tools/ms282/export_skill_sequences.py` | 从 282 展开 + 逐级自证，失败不写文件 |
| `tools/ms282/check_skill_sequences.py` | 六项：表结构 / 有出处 / 有消费 / 接线 / 口径 / 资源 |

守卫的「有出处」会重跑导出脚本并逐字节比对表，手改 JSON 会立刻被抓
（负向验证：把 `charges` 从 8 改成 9 ⇒ `FAIL sequences.400031055`）。
「口径」还查三件事：子技能不在任何技能表（否则会出现在技能窗被加点）、
`invisible==1`、子技能 IMG 在 `NativeAssetIndex` 里（否则动作/特效全空）。

### 2026-11-05 V 核心连接技能一致性修复

`Etc/VCore.img` / `VMatrix.json` 的 core id 才是连接关系的权威来源：圣骑士 `10010027 → 400011072 聖十字架`，共通核心 `10010025 → 400011066 鋼鐵之軀`。旧角色快照可能携带过期的 name/skills 字段，造成十字架图标、钢铁之躯提示、释放仍走十字架的三链分裂。

运行时在 `NativeVMatrixWindow.Find` 和 `VMatrixLevels.Capture` 按 core id 回填 canonical name/desc/type/maxLevel/skills；`SkillBook.LoadMatrixSkills` 对 `>=400000000` 的 V/HEXA 定义改为覆盖旧缓存条目，保证 type、icon、主动/被动判断与 `VMatrixSkills.json` 同源。验证边界：服务端仍以 `VMatrixCatalog.Core` 校验，客户端只修复旧快照展示与技能等级镜像，不改变源端连接表。

同日补充：`400011072 聖十字架` 的 `attackDelay/subTime` 表示按住期间重复命中，但 IMG 未设置 `info/rapidAttack`，旧客户端因此误判为蓄力技能，只在松手时命中；现由 `NativeSkillVfx.RapidAttack` 显式纳入连续攻击。持续攻击期间切换其它技能时先结束当前 held 状态，避免 `heldSkill` 全局锁死快捷栏。

### 已知口径简化

1. `4241000.addAttack.pattern=151` 的帧级输入窗口未做，用「伤口仍在」等价；
2. `400041026` 的 `casterMove` 按「目标背面 60px + 面向目标」实现，不做逐帧位移曲线；
3. 子技能角色动作走 `PlayNativeSkill(child)`，角色 WZ 无对应 clip 时回退 `atk`
   （与项目里其它 V 技能同一条路径）；
4. 形态时长取母技能 `time` 原值，不吃 `use_buff_everlasting` / 增益时长加成。

## 16. 按住型技能「只播特效、打不了怪」（2026-11-06）

### 16.1 症状

400011072 圣十字架绑到快捷键、按住：光束特效照播，怪只疼一下（或一下都不疼），
松手立刻没。用户原话「之前是只播效果，但是打不了怪」。

### 16.2 根因：连射的「每一发」被当成一次新施法

施法链是 `TryCastBoundSkill → rapid → StartHeld → Update() 每 attackDelay 一次
BeginCast(continuousShot:true)`。问题在 `BeginCast` 里这几条判据**对每一发都重新跑、重新扣**：

```csharp
if (!frenzy && State(s.native.Id).Remaining(...) > 0) return Reject("技能冷却中");
...
if (!frenzy && !State(s.native.Id).TryStart(now, s.cd)) return Reject("技能冷却中");
p.TrySpendMp(mpCost);
```

400011072 的 `cooltime=120`、`mpCon=1000`。于是：

1. 按下 → `StartHeld`（**不扣蓝、不起冷却**，因为 rapid 分支当时在扣费之前 return）
2. +0ms 第 1 发：冷却为空 → 扣 1000 MP → `TryStart(120s)` → `HitAt` ✔
3. +150ms 第 2 发：`Remaining(120s) > 0` → **Reject("技能冷却中")**
4. `Update()` 拿到 false → `Report` + `StopHeld(true)` → 光束拆掉、播 keydownend

表现就是「特效闪一下、怪掉一点血、然后什么都没有」。冷却 120 秒的技能尤其明显；
冷却短的连射技能（暴风神射这类）因为 `cd` 很小，症状轻，一直被掩盖。

### 16.3 修复（`SkillSystem.BeginCast` / `SkillHeldInput`）

1. **`payCost = !continuousShot`**：冷却、MP、HP、弹药、物品这五类判据与扣减
   全部只在「真正发起一次施法」时执行。后续每一发只结算伤害。
2. **`rapid` 分支挪到扣费之后**。放在之前的话按下不扣蓝、每发各扣一次，
   1000 MP 的圣十字架第 2 发就「魔力不足」（换一个闸门挡死，症状一样）。
3. **`charge`（蓄力型：雷霆万钧VI / 元素之炎VI）保持在扣费之前**：
   它是松手才结算，按下扣一次、松手再扣一次就是双重扣蓝。
4. **`AttackDelay` 读源端 `attackDelay`**（400011072 = 150ms），
   不再写死 100ms —— 写死会让 150ms 的技能多打 50%、90ms 的少打。
5. **保底一发**：`HeldVisuals` 要等 `prepare` 播完（圣十字架约 0.9s）才开火，
   玩家点一下就松手会一发都打不出去 ⇒ `StopHeld(release)` 里若 `!heldFired`
   就补一次 `BeginCast(previous,true)`。
6. `heldReady` 闸门：`prepare` 没播完之前 `Update` 不开火，避免「怪先掉血、光束后到」。

### 16.4 验证（`Assets/Editor/SkillDamagePlayChecks.cs`）

走真实输入链路，不塞测试后门：

```text
QuickSlots.Bind(0, id) → InputHub.SetVirtualAction(action, down)
→ QuickSlots.Cast(0) → TryCastBoundSkill → rapid → StartHeld
→ 按住 N 秒（Update 逐发）→ SetVirtualAction(up) → StopHeld(true)
```

结果（按住 3.0s）：

```text
400011072「圣十字架」 → 打到怪：伤害包 9 帧 / 总伤害 8071，击杀 18，按住态保持完好
```

三条都过：**9 个伤害包**（≈200ms 一包，含起手 0.9s）、**按住态没被拆**、
伤害不为 0。修复前只会有 1 包且按住态在 +150ms 就归零。

### 16.5 量伤害时踩的坑（下次别再走）

- **40001 的怪只有 8 血**，一发就死，死了 `EnemyUnit.All.Remove(this)`，
  之后所有发包都打空气 —— 「1 包」和「20 包」看起来都是 1 次击杀。
  正确做法：每帧检查目标是否死了，死了就**立刻换最近的一只并贴上去**，
  这样 27 只怪能连续量出 9 包。只盯一只怪 / 只调 `hpMax` 都测不出来。
- **别改 `hpMax`**：`EnemyUnit` 的刷新会把 `hpMax` 覆盖回原生值，
  一帧从 5000 万掉到 0，看起来像「一刀 5000 万」，其实是重置。
- **别改 `attackDamage`**：每帧由 `Attributes` 重算，写了也没用。
- **`mp` 读数不可靠**：会被 `maxMp` 夹回去，不能拿 MP 差当「扣了几次」的证据。

## 17. 对照 282 源端：极冰风暴的分类、枪刺旋风的按住判定（2026-11-06）

### 17.1 400011121 極冰風暴：不是分类 bug，是没实现的「詛咒 + 冰塊」proc

282 `h` 原文（`String/Skill.img`）：

> 消耗 #mpCon MP，施展時增加 #z 鬥氣，對最多 #mobCount 名敵人施加**鑽石狼的詛咒**
> 隨後 #q 秒內，定期發動能對最多 #w 名敵人施加詛咒的光芒
> #c冰塊#：對最多 #u 名敵人造成 **#u2%** 傷害 **#u3 次**，冷卻時間為 #t 秒

节点事实（`Skill/40001.img/skill/400011121`）：

```text
info    = {type=1}
common  = {cooltime=60 maxLevel=30 mobCount=10 mpCon=1200 q=20 q2=20 t=0.4
           u=15 u2=640+25*x u3=4 v=1 w=5 x=15 y=6 z=200}
action  = 5thComboTempest
顶层    = action additional_process common effect icon* info isSequenceOn
          notCooltimeReset notIncBuffDuration preloadEff processtype special vSkill weapon
```

`common` 里**没有 `damage`、没有 `attackCount`、没有 `psdSkill`**，伤害全部藏在
`u2`（冰塊伤害%）与 `u3`（次数）上，且「冰塊」是**命中带詛咒的敌人时生成**的追加攻击，
不是施展时的一次伤害。

`SkillBook.Get()` 的兜底分类（`SkillBook.cs:271` 先落 `type="passive"`，296-298 再按字段升级：
`summon 节点 → summon` / `damage|fixdamage|mad|attackCount → attack` / `time|prop → buff`）
三条全不命中 ⇒ 留在 `passive` ⇒ `TryCastSkill` 以「被动技能无需施放」拒绝。

**结论：别为了「能按」把它改成 attack。** 它的 `DamagePercent` 是 0，
放行之后就是「79 帧特效照播、怪一点血不掉」——正是我们要修的那个症状换了个技能。
要做就补齐两条：①施展时施加詛咒（debuff 表）②直接攻击命中带詛咒目标 → 生成冰塊追加攻击
（`u2%` × `u3` 次，冷却 `t` 秒）。属新功能，不是修 bug。

### 17.2 400011068 槍刺旋風：`info/rapidAttack` 不是唯一判据

282 节点：

```text
info   = {areaAttack=1 bodyAttack=1 invincible=1 movingAttack=1 rectBasedOnTarget=1 type=11}
common = {attackCount=8 attackDelay=140 cooltime=120 damage=253+11*x mobCount=12 mpCon=100
          s=564+23*x s5=50 t=4 updatableTime=4000 w=1 w2=10 x=100 y=12 z=8}
psdSkill = {400011068, 400011069}
action   = 5thPierceCycloneLoop
```

`h`：「最多 #t 秒內以 #damage% 傷害**連續攻擊**前方 #mobCount 名敵人共 #attackCount 次…
按鍵持續結束時…造成 #s% 傷害並攻擊 #q 次的**衝擊波** #z 次作為收尾攻擊」。

它 **没有** `info/rapidAttack`，但有 `attackDelay=140` + `attackCount=8`。
旧判据只看 `info/rapidAttack` ⇒ 落到 `charge` 分支 ⇒ 按住只播 keydown 循环、
伤害要松手才出一次。和圣十字架「只播特效打不了怪」是同一类症状。

新判据（`NativeSkillVfx.RapidAttack`，全部来自源端字段、不按 id 硬编码）：

```text
info/rapidAttack != 0                                   → rapid
否则 keydown 与 keydownend 都存在
     且 common/attackDelay > 0
     且 common/attackCount > 0                          → rapid
```

顺带删掉 `if (id == 400011072) return true;` 那条硬编码：282 的
`400011072 聖十字架` 与 `400011028 火神重擊` **本身就写了 `info/rapidAttack=1`**，
硬编码是多余的；真正漏掉这一位的是枪刺旋风。

实测（`SkillDamagePlayChecks`，按住 3s）：

```text
400011068「枪刺旋风」→ 伤害包 6 帧 / 总伤害 8047 / 击杀 27 / 按住态保持完好
```

**边界**：蓄力型（`charge` 分支里的雷霆万钧VI / 元素之炎VI）不带 `attackDelay` ——
`attackDelay` 的语义就是「连续攻击的每发间隔」，只有按住型才有。所以新判据不会把蓄力型误判成连射。
三个按住型技能一起实测（同一轮、同一张图）：

```text
400011068 枪刺旋风  → 伤害包  6 帧 / 击杀 27 / 按住态保持完好
400011072 圣十字架  → 伤害包 11 帧 / 击杀 21 / 按住态保持完好
400011028 火神重击  → 伤害包 18 帧 / 击杀 21 / 按住态保持完好
```

**仍未做**：`psdSkill 400011069` 是松手那一下的冲击波，不在 `NativeSkillSequences.json` 里，
所以「收尾冲击波」这一层还是缺的（伤害没缺，缺的是那一段表现 + 额外 `s%` × `q` 次）。

## 18. 「装了核心却按不动」全量清单与单字母别名（2026-11-06）

`tools/check_matrix_skill_coverage.py` 只验「技能能被 `SkillBook` 解析」，
**解析成功 ≠ 能施放**。真跑一遍（`SkillCombatDump -dumpUncastable 1`，
按 `VMatrix.json` 的 type=0 核心反查）得到：

```text
技能核心引用技能 264 个：可施放 247，被判被动 13，无定义 4      ← 修前
技能核心引用技能 264 个：可施放 252，被判被动  8，无定义 4      ← 修后
```

### 18.1 根因：兜底分类器读不到单字母字段

`SkillBook.Get()` 对不在 `VMatrixSkills.json` 里的技能走 IMG 兜底，类型是**按字段猜**的：

```text
summon 节点                             → summon
damage / fixdamage / mad / attackCount  → attack
time / prop                             → buff
都不命中                                 → 留在 "passive"
```

282 有一批技能把「伤害 / 段数 / 目标数」写在 `q`、`z`、`dummyStr2` 这类占位字段上，
真实含义由 `h` 模板指定。导出链（`export_advanced_skills.COMMON_ALIAS`）只覆盖
被导进 `VMatrixSkills.json` 的那批，其余走到兜底就一个字段都读不到 ⇒ 判成被动 ⇒
玩家装备核心、按下去只回「被动技能无需施放」。

修：`SkillBook.FieldAliases` + `ApplyFieldAliases()`，在分类**之前**补别名，
判据一律抄 h 原文：

| 技能 | h 里的原文 | 别名 |
| --- | --- | --- |
| 400011039 協力攻擊 | 1打：#q%的傷害攻擊#q2次 | q→damage, q2→attackCount |
| 400021078 黑翼破天 | #dummyStr2%傷害…#dummyStr3次…#dummyStr名敵人 | dummyStr2→damage, dummyStr→mobCount, dummyStr3→attackCount |
| 400021155 絕魂毒牙 | 同上句式 | 同上 |
| 400041075 幽靈之刃 | 修羅：最多對#y名敵人以#z%傷害攻擊#u次 | z→damage, u→attackCount, y→mobCount |
| 400051024 ICBM | 以#q%的傷害攻擊最多#u名敵人#q2次 | q→damage, q2→attackCount, u→mobCount |

只在自身没有目标字段时写入，不覆盖已有定义（同 python `apply_alias` 的 setdefault 口径）。
多段技能只取第一打当主段（与落雷凝聚「只取主段」同一口径）。

### 18.2 剩下 8 个被判被动的，逐个定性

| 技能 | 源端 info | 定性 |
| --- | --- | --- |
| 400000005 实用的进阶祝福 | `pvp=0` | 真被动 ✔ |
| 400000006 实用的最终极速 | `pvp=0` | 真被动 ✔ |
| 400040000 爆破飞毒杀 | `type=51 dot=1` | 被动 proc ✔ |
| 400001091 轮回之环 / 400001092 命运之力 | `type=12` | 需输入数字键交互，`400001092` 是 `MainSkillID_WZ2` 子技能 |
| 400031078 风暴注入 | `type=12` | 属性切换开关 |
| 400001024 普力特的祝福 | `type=10` | **可施放的 buff**（6 层叠加，h 有 w/z/q/u/v），缺的是效果表不是类型 |
| 400011121 极冰风暴 | `type=1` | 詛咒 + 冰塊 proc（见 §17.1），强行放行 = 0 伤害 |

### 18.3 4 个「无定义」是源端数据缺失

`400021008 / 400021048 / 400021074 / 400021096`：`VMatrix.json` 的核心
`connectSkill/0` 确实指向它们，但 **282 的 `Skill/40002.img` 里没有这些节点**
（`load_img` 确认 282 源端也没有）。属源端数据缺失，不是本项目漏导。

### 18.4 守卫

```bash
Unity -batchmode -projectPath . -executeMethod SkillCombatDump.Run -dumpUncastable 1 -quit
# 产物 /tmp/skill_uncastable.txt
```

建议把这条接进回归：**核心引用的技能里，被判 passive 的数量只允许是那 8 个已知项**，
多出来的就是新的分类漏判。

### 18.5 另外补的一处：判定框只认 `lt/rb`，漏了 `lt2/rb2`

ICBM `400051024` 的 `common` 里**没有 `lt/rb`**，只有：

```text
lt2 = [-800, -400]      rb2 = [-250, 80]      （爆炸落点）
range = 1（秒）  q = 800+32*x（伤害%）  q2 = 5（段数）  u = 15（目标数）
```

`NativeSkillCatalog` 原来写死 `HasBounds = level.ContainsKey("lt") && level.ContainsKey("rb")`，
于是 `HasBounds=false`，`InRange` 退回 0 宽的默认框 ⇒ 技能放得出来、怪一点血不掉。

修：按 `lt/rb → lt2/rb2 → lt3/rb3 → lt4/rb4` 逐级回退，只认第一条存在的。
修后 `ICBM` 的判定框是 `Min=(-20,-10) Max=(-6.25,2)`（世界单位，即身前 6.25~20 单位），
与 `lt2/rb2 ÷ 40` 完全一致。

顺带说明为什么它之前「测不出来」：贴脸 1.2 单位站位根本不在这个框里，
探针加了 `-dmgStandoff <世界单位>` 才量得到。远程/落点型技能都要用它。

## 19. 两处「看着像缺效果」的实测结论（2026-11-06 下午）

### 19.1 雷霆戰槌 `400011131` 没有 `ball`，锤子体是 `special/front`

282 pack 的顶层子节点里根本没有 `ball`：

```text
action common effect(17帧) effect0(23帧,z=-1) extraSkillInfo hit number(99)
psd psdSkill showStackCooltimeNum skillList special vSkill weapon*
```

`special` 下挂 `0` 与 `front` 两个子段（锤子本体 + 前方拖影）。
所以「缺 ball」不是漏导，是本来就没有这一层。

真正的问题在 `SkillVisuals.SpecialLayer`：它把 `special` 一律当成「按 count 多次生成」，
沿 `x`（默认 200px）对称铺开 —— 于是 `special/0` 落在人物左边 100px、
`special/front` 落在右边 100px，**且不分朝向**。表现就是「锤子没飞出去／左右各一个」。

修：`special` 有两种形态，数据能区分：

```text
分支上写了 count        → 多次生成（400011003 神聖團結的 special 才是分波次），按份数铺开
分支上没有 count        → 层级容器（锤子本体 + 前方拖影），叠在同一处；
                          子段名以 /front 结尾 → 放到朝向那一侧，/back → 反侧
```

### 19.2 圣十字架「没伤害」：命中框是真的窄，先看 `[Hit]` 日志再判断

加了开关（菜单 `Tools/技能诊断/伤害日志 开/关`，键 `PlayerPrefs "debug_hitlog"`），
打开后每次结算打印一行：

```text
[Hit] id=400011072 lv=30 kind=Attack dmg%=515 段=12 目标上限=12
      框=(-5.25, -2.50)~(5.25, 0.75) hasBounds=True ⇒ 选中=1 命中=1 总伤害=381
```

小怪一发就死，**只看死没死判断不出问题**：伤害是 1 还是 1 万都叫「打死了」。
这一行才能区分三种情况：`选中=0`（打空）、`命中>0 总伤害=0`（算出 0）、`总伤害` 太小（数值不够）。

实测得到的关键事实：

- 命中框 `±210px` 是**真实 282 数据**（pack 里 `lt=[-210,-100] rb=[210,30]`），
  而光束视觉长度是 `w=1080`。**看得见 ≠ 打得到**，这是源端口径。
- ⚠️ ~~项目 IMG 与 282 pack 的 `lt/rb` 不一致~~ **此结论已作废（2026-11-06 复核）**。
  用 BMS 密钥解出商业端 `Skill_00005.ms → Skill/40001.img`，与项目
  `MapleDashResources/Data/Skill/40001.img`（GMS 等长 XOR 版）逐字段比对：
  `400011072` 两份都是 `lt=[-210,-100] rb=[210,30]` 且都有 `lt2/rb2/lt3/rb3/lt4/rb4`。
  真正写 `lt/rb=null` 的是 **`400011052` 祝福之鎚母技能**（商业端本来就没有框，
  `VMatrixSkills.json` 里的 ±210 是按 `boundsFrom` 从子技能 `400011053` 继承的合成值）。
  旧结论把「导出表丢字段」误记成了「源端两份数据不同」，详见 §21。
- 按住期间后续发包 `选中=0` 多数是**目标已经不在框内**（框只有 ±210px），
  不是冷却/结算被挡。

### 19.3 圣十字架的光束长度：`w` 要接到判定框上

`common` 同时给了两个互相矛盾的数：

```text
lt = [-210, -100]   rb = [210, 30]    ← 每发判定框，前向只有 210px
w  = 1080                             ← 光束的视觉长度（px）
```

只按 `lt/rb` 结算，画面上一道光束贯穿半个屏幕、实际只有贴身的怪掉血。
`w` 是源端给的光束长度，按住期间应该沿朝向把前向接到 `w` 上。

修（`NativeSkillDefinition` 构造末尾，**只对按住连续攻击生效**）：

```text
Kind == "Attack" 且 NativeSkillVfx.RapidAttack(id) 且 common/w > 0
⇒ 若 w/PPU > -Min.x，则 Min.x = -w/PPU
```

不动后向（`Max.x`）与高度（`Min.y/Max.y`），也不碰非按住型技能 ——
普通技能的 `w` 语义不是光束长度（雷神战槌 `w=60`，神聖團結 `w=1000` 都不是光束）。

修后 `圣十字架` 的框从 `Min(-5.25,-2.50)` 变成 `Min(-27.00,-2.50)`，即身前 1080px。

## 20. 周期攻击（祝福之鎚）：母技能开状态、子技能出每一发

用户原话「祝福之锤有连续攻击效果，但是后续的攻击没看到特效」。先看 282 节点再改代码：

- `400011052`：只有 `common` 的 `mpCon/cooltime/x/y/w/q/z/s/v`，
  `info/type=11 + areaAttack=1`，层只有 `special`(8) 与 `ball/0..2`。
  **没有 damage / time / attackDelay**，这是「召唤/开状态」的壳。
- `400011053`（`invisible=1`）：`damage=525+21*x`、`attackCount=4`、`mobCount=8`、
  `time=30`、`attackDelay=1800`、`subTime=300`、`lt=[-210,-245] rb=[210,175]`，
  层有 `special`(8)、`ball/0..2`、`effect`(7)、`effect0`(8, z=-1)、`hit/0`、
  `action/0=5thBlessedHammer`。**每一发的数值与表现都在这里。**
- `VMatrixSkills.json` 把子技能数值合并进了母技能行（所以 C# 侧从母技能行能读到
  `time/attackDelay/damage/lt/rb`）。这不是母技能 IMG 的字段，别拿 `400011052` 的 vfx
  去播后续发次 —— 它只有召唤动画。

修复（`NativeSkillFollowups.cs` / `SkillVisuals.cs` / `SkillSystem.cs`）：

1. `SkillDef` 加 `visualKeyOverride`：`native` 仍用母技能（判定框/伤害/proc 的 id 口径不变），
   `vfx` 换成 `NativeSkillVfx.Key(400011053, level)`。`BeginVisuals` 的
   `effect/effect0/special` 与 `Impact` 的 `hit/0` 因此全部走子技能。
2. `RepeatAttack` 每发 = `BeginVisuals(body,true)` + `RepeatingHammer` + `HitAt`；
   另加 `[Repeat] tick=N` 调试标记（`debug_hitlog=1` 时）。
3. `RepeatingHammer` 轮换 `ball/0..2`、至少 0.45s：普通 `Fly` 只有 ~70ms，肉眼看不见，
   这是「没特效」的第二个根因。
4. `Cast` 协程对 `IsRepeatingSkill(id)` 跳过 `ProjectileCast`。母技能带 `ball` 会被
   `UsesProjectile` 误判成弹道，而其 `launch=p.SkillActionDuration*500` 是 **6 秒**，
   表现就是「周期里该出的没有，6 秒后莫名多飞出一颗」。
5. 排程用真实时间 deadline，不用 `WaitForSeconds`（后者受 timeScale 影响，与
   `Time.realtimeSinceStartupAsDouble` 混用会漂移）。

验证用 `Assets/Editor/SkillRepeatPlayChecks.cs`：Play Mode 运行时协程采样 +
`Application.logMessageReceived` 按日志顺序归因（`[Repeat] tick=N` → `[FxPlay]`/`[Hit]`）。
**batchmode 的 `EditorApplication.update` 会被节流、帧时间戳会跳变，按时间分窗必错。**
修前三个周期窗口新特效层全 0；修后 9 个 tick 每发 4~6 个新层 + 1 次结算，
6 次真实命中 1483 伤害，截图能看到金色锤环与爆点。

## 21. 商业端 vs 服务端：逐项复核「导错了」还是「没找到」（2026-11-06）

用户指出「282 是商业端，技能没问题；服务端不是商业端，会有问题」，据此把上一轮
登记成「源端数据缺失」的条目全部重查。**先分清三份数据**，否则任何"源端没有"都是假结论：

| 身份 | 路径 | 读法 |
|---|---|---|
| **商业端客户端** | `~/Documents/mxd/MapleStory/Data/Packs/*.ms` | ChaCha20-v4 加密容器，`java -cp tools/ms282/out ms282.MsUnpack <file.ms> list/extract` 解包；解出的 `Skill_*.img` 要 **BMS 密钥**（GMS 读会静默得到 0 节点） |
| 商业端散落资源 | `MapleStory/Data/Etc/{VCore,HexaCore}.img`、`String/Skill.img` | 同样 BMS 密钥 |
| **服务端套件（非商业端）** | `TMS282.5/data/wz282/*.img.xml`、`data/protocol/tms282.3/vmatrix-data.json`、`data/dat282/skills` | XML 是导出快照，缺画布/部分字段，不能拿来断言商业端 |
| 项目运行资源 | `MapleDashResources/Data/Skill/*.img` | 商业端 IMG 的 **GMS 等长 XOR 改写版**，字段与商业端逐字一致 |

复核结论：

1. **技能定义数据源没导错**：`/tmp/packs/Skill_40001.img`、`40002.img` 与从
   `Skill_00005.ms` 现解的 IMG **MD5 完全相同**。`export_vhexa_skills.py` 用的就是商业端。
2. **核心表没导错**：商业端 `VCore.img/vSkill/CoreData` 580 个核心与
   `Assets/Resources/UI/VMatrix.json` 逐条比对 → id / name / connectSkill **0 差异**
   （只有强化核心的 `jobs` 是项目从职业组补的，商业端 coreData 里为空，属正常补全）。
   注意 `VMatrix.json` 的 `source.file` 指向服务端协议 `vmatrix-data.json`，但内容与商业端一致。
3. ✅ **真正「导错了」**：`tools/ms282/export_advanced_skills.py:362 level_rows()` 把
   vector 字段白名单写死成 `("lt","rb")`，商业端的
   `lt2/rb2 / lt3/rb3 / lt4/rb4 / pos` 全被丢弃。
   例：`400011072 聖十字架` 商业端 `common` 有 **四组框**
   (`[-210,-100]..[210,30]`、`[-65,-245]..[65,175]`、`[-300,-160]..[300,90]`、
   `[-125,-335]..[125,265]`)，导出表只剩第一组 ⇒ 之前只能拿 `w=1080` 硬凑前向框，
   ICBM `400051024` 也只有 `lt2/rb2`，得在 C# 侧另打回退补丁。
   **已修**：`level_rows()` 改为保留所有 `(list,tuple)` 字段并重新导出；
   diff = 526 技能不变、**已有字段数值 0 变化**、补回 1800 个字段
   (`lt2/rb2` 各 660、`lt3/rb3` 各 180、`lt4/rb4` 各 30、`pos` 60)。
4. ✅ 配套修 `NativeSkillCatalog.cs`：282 的 `ltN/rbN` 不全是判定框，
   `400021092 火蜥蜴的恶作剧` 的 `lt2/rb2=[65,-42]..[67,-40]` 只有 2×2px（真框在
   `lt3/rb3=±1300×±750`），`400001008/400041038` 的 lt2 是 0 宽竖线。
   补全字段后若不过滤，这些技能会从「可见范围」退化成几像素框 ⇒ 放得出来打不到怪。
   现在备用框宽/高 <20px 就跳过，主 `lt/rb` 口径不变。
5. ✅ 「项目 IMG 无框 / pack 有框」是**错误记忆**：两份都有 `±210`。
   真正无框的是 `400011052 祝福之鎚` 母技能，`VMatrixSkills.json` 的 `±210` 是
   `boundsFrom` 从子技能 `400011053` 继承的**合成值**。
6. ✅ 「两套口径」的真相：`VMatrixSkills.json` 只收录 526 个技能（`JOBS` 17 个职业 +
   核心连接技能），而核心表共引用 857 个 id。其余走 `SkillBook.Get()` 的 IMG 兜底
   （按字段猜类型）。所以差异来自**导出覆盖**，不是商业端有两份数据。

### 逐项分类（上一轮被误记为「源端缺」的条目）

| 条目 | 商业端证据 | 定性 |
|---|---|---|
| `400011069` 枪刺旋风收尾冲击波 | `400011068/keydownFinish=400011069`、`psdSkill` 里也有；400011069 自己有 `damage=564+23*x / attackCount / lt/rb / multiAttackInfo` | **没接**（`StopHeld` 松手路径未分发），不是源端缺 |
| `400011121 極冰風暴` | 有 `lt/rb=±500/-400..250`、`additional_process`、`effect/special` 79 帧；`h` 写明 `u2`=冰塊伤害%、`u3`=次数、`u`=敌人数、`t`=冰块冷却 | **没接机制**（诅咒状态 + 命中生成冰塊 proc）；按 `damage/attackCount` 猜类型才判成 passive，不能简单补别名当 AOE（会凭空多一发） |
| `400001024 普力特祝福` | `h` 完整写出 6 层：`w`=CD 缩短、`z`=异常耐性、`q`=全属性、`u`=攻/魔、`v`=Boss 伤、`x`=最大叠加 | **没导效果表**（数值都在 common），不是源端缺 |
| `400011028/mask`、`400011104/rushEffect` | 商业端技能节点里确实有这两层 | **没接取层**（`CastVisuals` 层名白名单没覆盖） |
| `400021008 / 048 / 074 / 096` | 商业端 `VCore.img` 有 4 个核心（`job=14212` 超能力者）指向它们；但全 6 个商业端 Skill IMG **节点名 0、整数引用 0**，`String/Skill.img` 97 个 4000210xx 名字里也没有，`HexaCore.img` 无对应核心 | **商业端自身不同步**：核心表先登记、技能包未打包（且导出脚本 `JOBS` 不含 14212）。既不是我们导错，也不是没找到；不要再用服务端 XML 去"证明源端有" |

复验（Unity Play Mode，重导数据后）：编译 0/0、三份日志 0 异常；
圣十字架 6 包 / 8054 伤害 / 击杀 27，枪刺旋风 10 包 / 8086 / 21，
祝福之鎚 9 个 tick 每发都有新层+结算，雷神战槌 `effect0/effect/special/0` 正常。
`400021092` 是 `type=summon`（`time=40 / dot=15`），`Cast` 在 Summon 分支直接
`yield break`，伤害探针测不到 `HitAt` 属探针不适用，特效 `effect/effect0` 正常。

## 22. 四条「商业端有、我们没接」的机制全部接上（2026-11-06 下午）

上一轮 §21 把四条定性为「没接」。这一轮全部接完，并在 Play Mode 里逐条验过。
共同前提：这四条技能在 `SkillBook` 里都会**掉进 passive**（`common` 没有
`damage/attackCount/time/prop`），按下去只回「被动技能无需施放」——
所以每条都要先在 `SkillBook.Get()` 的分类之后显式登记类型。

### 22.1 `keydownFinish` 松手收尾子技能
- 商业端节点：`Skill/<img>.img/skill/<id>/keydownFinish`，**全库只有 4 条**
  （`400011068→400011069` 槍刺旋風 / `400041059→400041060` 暗影投擲 /
  `400051046→400051097` 小萌新吉祥物 / `400051078→400051079` 破碎連拳）。
  子技能一律 `invisible=1`，`common` 自带 `damage/attackCount/mobCount/lt/rb`，
  `String/Skill.img` 里只有名字、没有 `h` ⇒ 数值与表现全在子技能。
- 导出：`tools/ms282/export_skill_finishers.py` → `Assets/Resources/NativeSkillFinishers.json`
  （母技能 `common` + `h` 给「几波 / 间隔多少」，波数字段名逐条不同：`z`/`u2`/`v`）。
- 运行时：`Assets/Scripts/NativeSkillFinishers.cs`，入口 `SkillHeldInput.StopHeld(true)`；
  蓄力型不叠加（它自己松手才结算）。伤害/判定框全走子技能的 `SkillBook.Native(childId, level)`，
  本文件不写任何伤害常量。
- 守卫：`tools/ms282/check_skill_finishers.py`。

### 22.2 `mask` / `rushEffect` 取层
- `400011104 [神速]奔馳的閃光` 的 `rushEffect/0` 是 13 帧突进残影，跟着角色走，
  `SkillVisuals.CastVisuals` 已按 `effect` 同一口径取层（`z` 先读 clip 再回退分支，`z<0` 画在施法者之后）。
- ⚠️ `400011028 火神重擊` 的 `mask` **不是可直绘图层**：商业端节点只是占位
  （`origin=967,116 / z=0 / delay=90 / _outlink=Skill/_Canvas/40001/400011028.img/...`），
  解出来的画布是 **1368x164、R=G=B=0、alpha 99.9% 不透明** 的纯黑 stencil。
  第一版把它当精灵播，Play Mode 截图里就是屏幕正中一条黑条，把角色整个人盖掉。
  282 是拿它做遮罩/混合用的，**EXE 侧混合方式未逆向 ⇒ 一个像素都不要画**。
  审计白名单里保留 `mask` 只为不误报，取层代码里已显式排除（注释写明原因）。

### 22.3 極冰風暴 400011121：诅咒 + 冰塊 proc
- 整条链是**三个技能**：`400011121` 母技能（`info/type=1`，`lt/rb=-500,-400..500,250`，
  `effect` 23 帧 + `special` 4 组 = 那 79 帧）/ `400011123` 光芒状态
  （`invisible=1`、`time=20 / subTime=5000 / mobCount=5`、自带 `lt/rb=-550,-350..550,200`）/
  `400011122` 冰塊伤害体（`invisible=1`、`damage=640+25*x / attackCount=4 / mobCount=15 /
  cooltimeMS=400 / lt/rb=±750..400`）。
- `h` 的字母语义：`u2`=冰塊伤害%、`u3`=段数、`u`=敌人数、`t`=冰塊冷却(秒)、
  `q/q2`=持续与刷新上限、`y`=叠层上限、`v`=每层加的攻击次数。
- 导出 `tools/ms282/export_skill_curses.py` → `NativeSkillCurses.json`；运行时
  `NativeSkillCurses.cs`：`StartCurse` 按母技能框上诅咒 + 起 `CurseLights`（每 5s 补一层，
  **用 400011123 自己的框选人**，不是母技能的）；`TriggerCurseOnHit` 在每次真实命中后，
  若命中的是「被诅咒目标」且过了 400ms 冷却，就生成冰塊（段数按叠层加）。
- **不要给它补 `u2→damage` 别名**：那会把母技能变成一发凭空的 1390% AOE。
- 守卫 `tools/ms282/check_skill_curses.py`。

### 22.4 普力特的祝福 400001024：六层叠加
- `h` 逐层写死：1 层 `w`=其余技能 CD 缩短%、2 层 `z`=异常耐性、3 层 `q`=全属性、
  4 层 `u`=攻/魔、5 层 `v`=Boss 伤害%、6 层无敌；`x`=最多 6 层、`s`=持续 30s、
  `cooltime=25`，若到期或叠满则冷却 `y=240s`。
- 导出 `tools/ms282/export_skill_stacks.py` → `NativeSkillStacks.json`（按 1..maxLevel 展开）；
  运行时 `NativeSkillStacks.cs`：每次施放 `stackCount+1` 并**重写同 skillId 的 Effect**
  （`NativeBuffs.ApplyStack` 内部 `Register` 会先移除旧条目），所以「叠一层 = 保留前面所有层
  + 新增一层」正好是逐层累加的 stat 集合；`EffectiveCooldown` 读 `CooldownReducePercent`
  对所有技能生效；到期协程清层并把该技能锁进 240s。
- 第 3 层的 `STR/DEX/INT/LUK` 是**flat**（不是 %），`PlayerCtrl.RefreshBuffAttributes` 里
  单独加，不能和 `MAPLE_WARRIOR` 的百分比合并。
- 守卫 `tools/ms282/check_skill_stacks.py`。

### 22.5 验证探针 `Assets/Editor/SkillFeaturePlayChecks.cs`
```text
Unity -batchmode -projectPath . -executeMethod SkillFeaturePlayChecks.Run \
      -featureHold 2.0 -featureShots /tmp/feature -logFile /tmp/feature.log
```
四条纪律，违反任何一条都会得到假结论：
1. **每发之前 `Prep`**（补 `mpMax/mp`、清 `actionUntil/actionBlocking/actionSkillId`、清 `states`）。
   阶段内连放两个技能时，前一个的 `actionUntil` 会把后一个拒成「正在释放技能」
   （`400011131` 就这么假失败过一次）。
2. **收尾冲击波单独成段放最后**：8 波 × `intervalMs` 在 batchmode 节流下真实耗时会膨胀好几倍，
   放中间会被后面的阶段切断统计；这一段等「8 波全到或超时」再收尾。
3. **收尾的 `StartFinisher` 必须在松手之后判**：松手在前、等待在后，顺序反了就永远等不到。
4. **放 500px 框的技能前要贴到最近怪身前**（`StandNearMob`）：测试场小怪常站在框外，
   否则 `[Curse] apply targets=0`，看着像机制没接上，其实是没框到人。

实测（`/tmp/feature4` 那一轮）：① 收尾 8/8 波、`400011069` 命中 8 次；
② `mask` / `rushEffect/0` 都出层；③ 诅咒 `applied=4`、冰塊 `body=400011122 stacks=3 hits=6 targets=3`；
④ 六层逐层 `cdReduce=10 → +ASR → +STR..LUK → +PAD/MAD → +BossDamage → +INVINCIBLE`。
编译 0/0、日志 0 异常。

## 23. 全职业技能回归（2026-11-06）：覆盖面与唯一实质缺口

一次性扫 1343 个技能（JobTree ∪ VMatrix 核心连接，去重去 invisible），
探针 `Assets/Editor/SkillFullSweepChecks.cs`：

```text
Unity -batchmode -projectPath . -executeMethod SkillFullSweepChecks.Run \
      -sweepFrom 0 -sweepCount 450 -sweepHold 0.18 -logFile /tmp/sweep_0.log
```

### 23.1 结果
- **节点解析**：`SkillVfxFamilyAudit` 族层丢层 0、没人在播的视觉分支 0；
  `check_skill_vfx.py` 的 148 处「整族没像素」全是源端 1×1 空占位（无 outlink），不是解析漏。
- **施放**：676 被动 / 624 可施放 / 43 被拒。被拒原因全可解释：`hpCon` 18、物品 8、
  弹药 4（+复测 21）、二段跳 2、轮回碑石/飞镖/海龙石 3、**`400021008/048/074/096` 缺技能数据 4**
  （商业端核心表登记、技能包没打包，再次确认）。
- **命中**：攻击型 414 → 324 直接命中、58 长窗口命中（投射物飞行）、
  21 缺弹药未测、11 仍 0 命中（`400031053` 是源端点框 20×0px；其余多为探针归因窗口太短）。
- **效果解释**：唯一实质缺口是**强化核心四件套没结算**，已修（见 23.2）；
  其余未接字段（`damR_6th` 65、`indie*`、`dotInterval`、`updatableTime`、`forceCon`、
  `ballDelay*`）只是没接，不是读不到。

### 23.2 强化核心（VMatrix type=1）的加成
- 结构：核心连 `400004xxx` 包装技能（`info/type=50`），包装技能 `psdSkill` 列出被强化的
  既有技能，`common` 只有 `damR_5th`（最终伤害%）、`targetPlus_5th`（目标数）、
  `ignoreMobpdpR`（无视防御%）、`bdR`（Boss 伤害%）。306 核 / 1156 条连线。
- 导出 `tools/ms282/export_enhance_cores.py` → `NativeSkillEnhance.json`（每级压成
  `[damR,targetPlus,ignorePdp,boss]` 数组，对象写法会到 1.7MB）；
  运行时 `NativeSkillEnhance.cs`，等级读 `VMatrixLevels.TryGet(包装技能id)`。
- 结算点：`CombatRules.Attack` 里 damage% 之后乘 `1+damR/100`、无视防御并入
  `ignoreDefense`、Boss% 并入 `ApplyDamage` 的额外百分比；`SkillSystem.HitAt` 目标数
  `+ targetPlus`。未装核心时四项恒 0，既有口径不变。
- 守卫 `check_enhance_cores.py`；验证 `SkillEnhanceChecks.Run`（静态）与
  `SkillEnhanceChecks.RunDamage`（伤害通道：78 → 172，倍率 2.205）。
- ⚠️ **踩到的老坑**：第一版用 `Art.Int(core,"w")` 取包装技能 id，400004008 被 float 舍成
  400004000 ⇒ 加成恒 0。id 一律 `Art.Id`（IRON-RULES §7.1），守卫里已加这条断言。

## 24. 全职业回归扫出来的第二、三、四类缺口（2026-11-06 下午）

§23 修掉「强化核心装了等于没装」之后，继续按同一份扫测结果往下清。这一轮补的四条
都是「字段/机制在商业端写得很清楚、项目里一条都没读」的类型，不是猜的。

### 24.1 强化核心的等级链路本身是断的（比 damR_5th 更根本）
- `400004xxx`（V 强化核心包装）与 `500004xxx`（HEXA 强化包装）都是 `info/type=50`，
  **不在 JobTree 里** ⇒ `SkillBook.Get()` 返回 null ⇒ `SkillProgress.LevelOf(wrapper)` 恒 0。
  所以这类强化只能靠 `VMatrixLevels` 这张等级镜像表。
- 这张表以前只有**打开面板**才会写（`NativeVMatrixWindow.Load` / `NativeHexaMatrixWindow.Load`）。
  不打开面板的玩家 ⇒ 等级 0 ⇒ 306 个 V 强化核心 + 65 个 HEXA 强化全都不生效。
  已修：`GameSession.SelectChar` 里 `RefreshMatrixLevels()` 主动拉一次 V / HEXA 快照。
- 更隐蔽的一条：`Capture` 与 `CaptureHexa` 都是「整份重建」语义，共用一张表时
  **打开一次 V 面板就会把 HEXA 等级清掉**。已拆成 `matrixLevels` / `hexaLevels` 两张表，
  `TryGet` 先查 V 再查 HEXA。
- 守卫 `check_enhance_cores.py` 现在断言：`RefreshMatrixLevels()` 存在、`CaptureHexa` 被调用、
  两张表分开、运行时不用 `SkillProgress.LevelOf` 取包装技能等级。

### 24.2 六转强化 `damR_6th`（65 条 / 138 个被强化技能）
- 结构与 V 强化核心同构，只是列只有「最终伤害」一项：
  `500004006 強化聖十字架 psdSkill=[400011072] common{damR_6th=10+x+5*log10(x)+5*log20(x)+10*log30(x)}`
  ⇒ Lv1=11%、Lv30=60%。
- 导出并入 `NativeSkillEnhance.json`（`s` 字段区分等级来源：0=V、1=HEXA），
  运行时同一套 `Bonus()` 结算 —— 不需要第二套乘区。
- 实测：`400011072` 无 HEXA 143 → Lv30 230，**倍率 1.608**（+60% 完全对上）。

### 24.3 持续伤害 `dot` / `dotTime` / `dotInterval`（30 条可结算）
- 三字段成组：`dot`=每跳伤害%（与 `damage` 同口径）、`dotInterval`=间隔秒、`dotTime`=总时长秒。
  例 `1121015 藍焰恐懼` Lv30：`dot=165% / 2s / 60s` = 30 跳。
- 项目此前**一条都没结算**（全代码搜不到 `dotInterval`）：这些技能只出首段 `damage`，
  后续几十跳全丢，血条是「一下掉一段」而不是持续下滑。
- 导出 `tools/ms282/export_skill_dots.py` → `NativeSkillDots.json`（按级展开 `[dmg,秒,间隔]`）；
  运行时 `NativeSkillDots.cs`：命中后给每个目标挂标记，每 `interval` 秒结算一次，
  走 `NativeSkillReplay.Adapt(首段行, 1, damage%, dot%)` + `ApplyReplayHit` ——
  保留暴击/无视/强化核心整条乘区，不另起一条 roll 通道。
- **同技能同目标重复命中 = 刷新不是叠加**（源端 dot 是覆盖）；换图/死亡清标记。
- 6 条只有 `dot` 没 `dotTime`：`1241500` `15141500` `2241000` `400011039` `400021042` `400041009`。
  前两条是放置型领域（时长归 `updatableTime`/`ndTime`），后两条 `dotInterval` 不是 DoT 语义
  （`2241000` 暴风雪VI 的间隔另有含义）。全部登记在 `unresolved`，**没有硬套**。
- 守卫 `check_skill_dots.py`。

### 24.4 技能震屏（93 条 = 单段 56 + 多段 37）
- **两种写法都要读**：`trembleEffect{force,time,delay,reduction}` 是单段；
  `trembleEffectList/{0,1,2..}` 是**多段**（每段自带 delay/force/time，共 37 条、其中 33 个多段技能）。
  只读单段的话，暴风系列 / 连续打击类技能一次都不震 —— 第一次就漏了这 37 条。
- `force` 是原端点阵振幅（实测 1~80）、`time` 毫秒、`delay` 起播延迟毫秒。
  多段按 `delay` 升序逐段起协程，不能串行（后面的段会被推到最后）。
- 换算照抄已验证过的怪物震屏（`NativeMobController.TrembleAmplitude = 30f/Art.PPU`）：
  `force / Art.PPU` 就是世界单位振幅。不另设夹值 —— 夹了就跟源端不成比例。
- 导出 `tools/ms282/export_skill_tremble.py` → `NativeSkillTremble.json`；
  运行时 `NativeSkillTremble.cs`（`SkillSystem` 的 partial），`Cast` 里 `PlayTremble(s)`，
  每个 wave 一条协程，按 `delay` 起震、`time` 衰减，走 `HitFeel.Shake`
  （系统设置「画面效果」总开关已经在那里判）。
- 守卫 `check_skill_tremble.py`。

### 24.5 Play Mode 探针的两个致命坑（`Assets/Editor/SkillSidePlayChecks.cs`）
1. **进 Play 会触发域重载**：静态字段与 `EditorApplication.update` 的委托全丢。
   只在 `Run()` 里注册一次 ⇒ `Tick` 永不再跑，进程空转几分钟、日志刷满
   `There are no audio listeners in the scene`。必须在 `[InitializeOnLoadMethod]` 里重新挂。
2. **刷怪要按候选图轮换并设上限**：在同一张空图上反复 `field.Enter` 会无限循环。
   换图计时与阶段计时必须是两个变量，共用一个会让阶段推进乱序。
3. 测试角色没学某条技能时 `TryCastSkill` 回「技能尚未学习」—— 用
   `VMatrixLevels.Capture` 注入一个假核心给它 30 级（`BaseLevelOf` 里镜像优先于存档）。
