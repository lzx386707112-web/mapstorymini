# 背包物品悬浮详情（Item Tooltip）

核实快照：2026-10-27。客户端：`../Beidou-Server/clien/BeiDou.exe`（SHA256 见 binary-and-tools.md）；
WZ 取自 `MapleDashResources/Data`，用 `Beidou-Server/tool/wz-python`（wzpy，region="GMS"）直接读取节点。

## 1. 触发与显示时机

- 鼠标指向背包（Item 窗）或装备（Equip 窗）中某件物品的图标格时，显示悬浮详情。
- 原端无延迟即时显示（hover 即出）。本项目在 `GameWindows.DrawAll` 内：每帧先
  `ItemTooltip.Reset()`，各窗口 Draw 时若指针落在 `id>0` 的格子上调用 `ItemTooltip.SetHover(id)`，
  末尾 `ItemTooltip.Flush()` 统一绘制（保证压在所有窗口之上）。
- 位置跟随光标：默认在指针右侧 +18×S；靠近右/下边缘时翻转到左侧/上侧并夹紧在屏幕内。

## 2. UI 素材与绘制方式（关键结论）

- 节点 `UI/UIWindow.img/ToolTip` **只有一个子节点 `Equip`**，其内容是一组预渲染的小标签位图图集：
  `Can / Cannot / Dot / WeaponCategory / ItemCategory / Speed / Property / mesos / cash /
  GrowthEnabled / GrowthDisabled`。每个再往下是具体位图（如 `Can/reqLEV`、`Can/warrior`、
  `Cannot/reqSTR`、`Dot/0`、`WeaponCategory/30`、`ItemCategory/1`）。
  - 用途：原端用客户端位图字体预渲染的「需求/属性小标签」（reqLEV、职业名、Cannot 等）。
- **本 WZ 的 `ToolTip` 节点下没有任何 `backgrnd / cover / line` 帧图**（已用 wzpy 全树扫描确认，
  `UIWindow.img` 内所有含 backgrnd/cover/line 的画布均属其它窗口如 UserInfo/UtilDlgEx，不在 ToolTip 下）。
  → 即原端物品悬浮框的背景是**用代码绘制的半透明深色框 + 1px 描边**，属于「原端确实用代码绘制」的例外。
- 据此本项目用 GUI 绘制背景：深色半透明填充 `Color(0.06,0.08,0.14,0.95)` + 四边 1×S 浅蓝描边
  `Color(0.55,0.65,0.85,0.90)`，照搬原端「代码绘制」行为，未自行设计新外观。
- 文本（物品名、数值、描述、需求文字）按本项目既有约定走运行时字体 `UiKit.Font`
  （与 StatWindow / SkillWindow / NpcDialog 一致），不逐字贴图集位图——名称/描述是动态字符串，
  本就不可能来自固定位图图集。源端图集仅作还原参考。

## 3. 数据来源（名称 / 描述 / 属性 / 需求）

### 3.1 物品名称与描述：String.wz
按 id 首位分段落在不同文件，**目录层级不一致**（已用 wzpy 核对）：

| id / 1,000,000 | 分类 | 名称/描述节点路径 | 备注 |
| --- | --- | --- | --- |
| 1 | 装备 Eqp | `String/Eqp.img/Eqp/<subcat>/<id>/name` | 比其它类多两层 `Eqp/` |
| 2 | 消耗 Consume | `String/Consume.img/<id>/{name,desc}` | 直接 |
| 3 | 设置 Ins | `String/Ins.img/<id>/{name,desc}` | 直接 |
| 4 | 其他 Etc | `String/Etc.img/Etc/<id>/{name,desc}` | 比其它类多一层 `Etc/` |
| 5 | 现金 Cash | `String/Cash.img/<id>/{name,desc}` | 直接 |

- 装备 `<subcat>` 由 id 前缀(÷10000)决定，映射（与 Character WZ 目录名不同，需单独映射）：
  `100→Cap`、`101/102/103/112..119→Accessory`、`104→Coat`、`105→Longcoat`、`106→Pants`、
  `107→Shoes`、`108→Glove`、`109→Shield`、`110→Cape`、`111→Ring`、其余(`121..160/169/170`)→`Weapon`。
  （`Eqp.img/Eqp` 实际子目录名：Accessory/Cap/Cape/Coat/Dragon/Glove/Longcoat/Pants/PetEquip/
  Ring/Shield/Shoes/Taming/Weapon/Face/Hair。）
- 装备通常只有 `name` 无 `desc`；消耗/设置/其他/现金有 `desc`（可能含 `\n` 多行）。

### 3.2 装备属性与需求：Character.wz（与 EquipCatalog 同源）
`Character/<dir>/<8位id>.img/info` 的字段，原端 `ItemInformationProvider` / `EquipmentCatalogService` 用同一批：
- 加成：`incSTR incDEX incINT incLUK incMHP incMMP incPAD incMAD incPDD incMDD incACC incEVA incSpeed incJump`
- 需求：`reqLevel reqJob reqSTR reqDEX reqINT reqLUK`
- `reqJob` 为位掩码（1 战士 / 2 法师 / 4 弓箭手 / 8 飞侠 / 16 海盗），用 `JobDefs.JobBit` 比对当前职业。
- 本项目经 `EquipCatalog.Info(id)` 读取（已带缓存）。

## 4. 显示内容与配色（照搬原端）

- 标题：物品名，按稀有度着色——现金道具(`id/1e6==5`)金色 `Color(1,0.85,0.2)`，其余白色。
- 装备副标题：槽位中文名（`EquipSlot.NameOf`）。
- 属性块：仅列出 `inc* != 0` 的项，格式 `力量 : +N`（绿色系 `Color(0.7,1,0.8)`）。
- 需求块（与属性块之间留一空行作分隔）：
  - `需要等级 N`、 `需要职业 <职业名>`（按位掩码解码为 战士/法师/弓箭手/飞侠/海盗）、
    `需要力量/敏捷/智力/运气 N`。
  - 未满足项标红 `Color(1,0.5,0.5)`，已满足项浅色 `Color(0.8,0.85,0.95)`（原端对不可装备项用 Cannot 红字图集）。
- 非装备：显示 `desc` 多行（自动换行，浅色 `Color(0.86,0.9,1)`）。

## 5. 已迁移 / 验证状态

- `MapleDash/Assets/Scripts/ItemTooltip.cs`：`ItemCatalog`（名称/描述取数 + 稀有度色）与 `ItemTooltip`
  （帧绘制、内容排版、边缘翻转、Flush 时序）。
- 接入点：`GameWindows.cs` 的 `InvenWindow.Draw`（物品格）与 `EquipWindow.Draw`（装备格）调 `SetHover`，
  `GameWindows.DrawAll` 调 `Reset`/`Flush`。
- 编译：`tools/unity-compile-check` 全量编译，本次新增文件 0 error（仓库内另存 3 个预存的
  `Server/Bot/*` 命名空间错误 `Server.Db`，与本次无关）。
- 待真机/编辑器验证：光标跟随与边缘翻转的视觉表现、各分类名称读取命中率。

## 6. 缺口 / 待核实

- 原端图集 `ToolTip/Equip/Can` 等多语言标签的具体位图内容未逐一解码（中文客户端应为中文「等级/职业」等），
  本项目用运行时字体渲染等价中文，未使用这些位图——若日后要求像素级一致，需解码 Can/Cannot 位图并对齐。
- 套装效果、潜力(potential)、星力、可升级次数等 v83 之后才有的字段本客户端未实现，tooltip 暂不含。
- 字段掉落物(GroundItem) 的悬浮详情未接入（本次只覆盖背包/装备窗），如需再扩展 `FieldLoot`。
