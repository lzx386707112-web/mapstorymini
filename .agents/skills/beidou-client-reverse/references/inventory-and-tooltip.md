# 背包物品悬浮详情（Item Tooltip）

## 罗杰苹果、背包标题按钮与场景层级补核（2026-10-28）

EXE身份沿用 binary-and-tools.md 的 SHA256 `1b91cf5b…f134b2`。用户补图确认标题按钮为展开、整理、蓝色关闭。`0x81C4C9..0x81C4D9` 的内部宽为155/583，实际窗宽由 `0x81C505..0x81C525` 创建175/603；`0x81CD77..0x81CE69` 根据展开状态选择字符串5580 BtFull/5581 BtSmall，在内部宽-30,y6创建。`0x81E3A6..0x81E521` 用字符串1851 BtGather/1852 BtSort，在内部宽-15,y6创建；关闭由基类 `0x92C445` 引用字符串5577 `UI/Basic.img/BtClose`，`0x92C500..0x92C518` 按保存的基类坐标创建。因此当前175宽小窗对应x125/140/155、y6，均12×12，展开窗同样相对右侧定位。BtFull/BtGather的Canvas origin=(6,6)供按钮控件使用，关闭origin=(0,0)，不能直接把Canvas origin再减到已定位的按钮矩形。原素材/页签/格子未自行重画。

任务苹果2010007原图为 `Item/Consume/0201.img/02010007/info/icon`，26×30、origin=(-4,30)；旧背包仅用FieldCatalog导出子集，缺失该图。现复用 `NativeItemSource.Icon` 直接从IMG定位，与商城/后台共用，覆盖任务物品。`GameHUD` 原先先画GameWindows再由NpcDialogUi画名牌/QuestIcon，导致场景标记穿透面板；现在DrawField在浮窗前、对话在浮窗后，保留原名牌/任务图标几何。

复现：`tools/inspect_beidou_client.py <exe> --start 0x81cd77 --end 0x81ce6c`、`--start 0x81e3a6 --end 0x81e53f`，`tools/imgdump.py Item/Consume/0201.img 02010007`。本次QuestNpcChecks新增真实苹果Canvas解码断言；窗口遮挡/按钮点击未进行Android实机验收。编译0 warning/0 error，Unity检查24177通过/0失败（`/tmp/roger-apple-unity.log`）。

## 悬浮窗逆向纠正（2026-10-28）

旧记录仅因 ToolTip 下没有背景Canvas，就声称自定深蓝色、浅蓝描边及装备中文属性列表“照搬原端”，该结论没有汇编依据，现撤销。缺少IMG不能证明任意代码绘制样式正确。旧 +18 光标偏移、190正文宽、13号标题和装备稀有度颜色同样只是旧实现，不能作为源端规格。

### 二进制身份与调用链

`clien/BeiDou.exe` SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，ImageBase `0x400000`。

- 背包鼠标移动 `0x81D8AD`，`0x81D942..0x81D972` 将鼠标坐标加窗口位置（y再加20），交给 `0x8F5B20`。
- `0x8F5B20` 按分类分派：装备 `0x8E8252`、普通消耗/设置/其他及非宠物现金 `0x8EDFC7`、宠物 `0x8ED8F5`。不可将普通布局直接用于装备/宠物。
- 普通物品 `0x8EE56F..0x8EE58E` 调 `0x5CF63E/0x5CF69E` 读名称和描述；`0x8EEEB8` 调 `0x8E6E52` 设置kind=5、宽290、高116+头部与长描述附加高度。`0x8EEE29..0x8EEEA7` 描述测量宽178，超过四行后按字体高度+4增加窗口高度。
- `0x8EEEF1..0x8EEF06` 调帧函数 `0x8F3141`，参数为背景 `0xA0000040`、角/轮廓开关1。`0x8F3478..0x8F365C` 填充背景，再写四角及内缩1像素轮廓 `0x00FFFFFF`。这是已核实的代码绘制依据，旧自定颜色不成立。位置受799/599边界裁剪（迁移按当前UI逻辑尺寸裁剪）。
- 名称 `0x8EEF67..0x8EEF88` 调 `0x8F4818`，文字坐标(18,10)，Dot/0在(10,15)；`0x8F48B0..0x8F4954` 为绘制证据。Dot图集来源 `0x8E65xx..0x8E66DA`，`UI/UIWindow.img/ToolTip/Equip/Dot/0` 5×5，origin(0,0)，必须引用原IMG。
- 图标 `0x8EF417..0x8EF427` 调 `0x8F421E`，位置(10,32+header)。`0x8F4279..0x8F4289` 绘制68×68、颜色 `0xA0FFFFFF` 的图标底；`0x8F42D2..0x8F4319` 将源图标以2倍尺寸、(x+2,y+66)锚点交给 `0x5D6458`。须保留图标origin，不能拉伸到固定方框。
- 普通描述 `0x8EF43B..0x8EF4DB` 字体7、x92、右界270，y32+header。字体7在 `0x8F3704` 取this+0x440；`0x8E5172..0x8E51A7` 设置12号白色。名称字体1也为12号白色（`0x8E4C89..0x8E4CBE`）。运行时字体仍走UiKit.Font，字体栅格化不保证与Windows一致。
- 任务等标记 `0x8EEF99..0x8EEFF1` 在y31/y50调用 `0x8F466D`，该函数居中绘制，y参数减2；一行增加header19（`0x8EE3EE..0x8EE40B`）。字体11 `0x8E52FB..0x8E532F` 为12号、`0xFFFF8A18`。字符串池693是原EXE的 `Quest Item`；当前任务物品标记采用此原字符串，未自行翻译。

复现入口：`rtk proxy /tmp/mapledash-reverse-venv/bin/python tools/inspect_beidou_client.py <BeiDou.exe> --start <上述起点> --end <上述终点>`；`tools/imgdump.py UI/UIWindow.img ToolTip/Equip/Dot`、`tools/imgdump.py Item/Consume/0201.img 02010007`。临时venv仅提供pefile/capstone，分析脚本在仓库内。

### 已迁移与剩余边界

`ItemTooltip.cs` 已替换公共帧颜色/角轮廓，普通物品恢复290宽、原Dot、名称、左侧原图2倍显示、右侧178宽描述、任务标记。边缘限制沿用当前UI尺寸。当前文本测量使用Unity字形，已接入原说明的转义换行和#c强调：`0x8E4217..0x8E423B`切换强调状态（可选c），`0x8F45BA`取通用字体9，`0x98ADCC`设置12号、`0xFFFF9900`；仍未逐行复刻 `0x8E40E4` 的断行测量。此阶段装备仍保留旧属性/需求内容排版；后续替换结果见末尾「装备独立布局与分类双击补核」。唯一/不可交易等组合标记、现金及特殊物品补充信息仍待还原，不能称为全分类像素级完成。旧文档关于这些配色“照搬原端”的断言已撤销。

### 普通恢复物品使用

源端 `gms-server/src/main/java/org/gms/net/server/channel/handlers/UseItemHandler.java` 校验存活、道具及数量，普通分支先删除1个，再 `getItemEffect(itemId).applyTo(chr)`，满HP仍消耗。`org/gms/server/StatEffect.java` 的loadFromData/calcHPChange/calcMPChange/alchemistModifyVal读取spec hp/mp（short）及hpR/mpR百分比；百分比恢复先截断整数；僵尸状态将固定HP和百分比HP分别整数除2。隐士/夜行者三转炼金术分别用4110000/14110003已学等级的WZ x百分比增强固定恢复。

旧 `NpcSession.UseItem` 仅接受spec/script，苹果无script，直接返回。现无script时接入 `UseRestorative`，仅处理spec由hp/mp/hpR/mpR组成的纯恢复品，避免部分执行buff/特殊效果；脚本物品仍原样解释执行。先确认Profile.RemoveItem成功，再恢复并SaveToDb；背包双击立即ConsumeClick，避免多次Repaint重复扣除。苹果2010007恢复值从原spec/hp=30读取，无ID特判。增益、解除异常及其他特殊消耗品尚不在该纯恢复分支内。

最终Unity QuestNpcChecks通过24183/失败0（`/tmp/item-use-unity-isolated.log`），编译0 warning/0 error，覆盖真实苹果30HP、扣1、HP上限、死亡、空物品、普通蓝药MP恢复；脚本pack2777 parseFail0，NPC全量run仍为769 ok/47 runFail/73 JavaInterop既有基线。尚无Android实机视觉/双击验收。检查入口临时禁用Profile落盘，未接取状态测试显式移除并恢复任务状态，避免读到已有存档造成假失败。

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


## 装备独立布局与分类双击补核（2026-10-28）

二进制身份同上，SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`、ImageBase400000。下列地址可用仓库 `tools/inspect_beidou_client.py` 的 `--start/--end` 复现；跳表须按DWORD读取，不可把表数据当线性指令。

- `8E8252..8EA099` 装备入口；`8E8E29..8E8E43` 调8e6e52，kind4、宽236、最小高166、属性起点152。`8E8E5E/8E8E67` 调8ec70a名称格式化与8eca0c属性列表。`8E97AE..8E97E7` 特殊装备可用A0400000红底，普通A0000040；当前只迁移普通底。
- `8ED0D2..8ED8E0` 主需求区：图标(10,32+baseY)，需求图集 `ToolTip/Equip/{Can,Cannot}/reqLEV,reqSTR,reqDEX,reqINT,reqLUK,reqPOP` 在x94,y32+12*i+baseY；数字在x144。`8F5056` 按实际值是否小于需求选Can/Cannot；POP=0用none。图集数字1为3×7、origin(-1,0)，其他多为5×7；none5×1、origin(0,-3)。LEV专用8f5310还涉及等级折扣，当前未迁移折扣。
- `8ED2C1..8ED2DA ->8EC366` 职业图集：beginner/warrior/magician/bowman/thief/pirate，x10/52/92/132/171/197、y133+baseY。beginner在reqJob<=0可用；其他在reqJob==0或!=-1且有对应bit时可用。这里是装备允许的职业列表，不是当前角色职业的染色。
- 成长显示在x94,y104/116，数值x148。`8ED6DA..8ED8BE` 非成长分支从this+50c读字符串5641 `none`，两行锚点(148,104/116+baseY)，保留origin。当前只显示已确认的非成长分支；成长等级/经验、到期与所有者行未迁移。
- `8ECA0C..8ED0D2` 构建类别、攻速、属性、强化次数行。`5C99FC` 的跳表5c9aaa将武器类型30/31/32/33/37/38/40..49映射字符串601..616（如` ONE-HANDED SWORD`）；`5C9AFA` 的跳表5c9e2d按attackSpeed0..12得到FASTEST/FASTEST/FASTER/FASTER/FAST/FAST/NORMAL/SLOW/SLOW/SLOWER/SLOWER/SLOWEST/SLOWEST。`5C9E61` 配合4606a0/4609fc装备分类，字符串617..631给出HAT/FACE ACCESSORY/EYE ACCESSORY/EARRING/OVERALL/TOP/BOTTOM/SHOES/GLOVE/CAPE/SHIELD/RING/PENDANT/MEDAL/BELT。类别标题字符串5530 `CATEGORY :`，攻速656 `ATTACK SPEED :`。
- 属性顺序STR/DEX/INT/LUK/HP/MP/WEAPON ATTACK/MAGIC ATTACK/WEAPON DEF./MAGIC DEF./ACCURACY/AVOIDABILITY/HANDS/SPEED/JUMP，字符串2042..2047、657..665。`8E7836` 普通正数格式2048 ` +%d`、非正数2049 ` -%d`；部分调用有force位，当前仍只显示正数属性，强制0、负数及特殊属性行待补，不能称完整装备提示。
- `8F3BC3` 在x6,y152+baseY画白色分隔线，宽windowWidth-12；属性起始内部高度161，文字y再减2。普通type1001在`8F3FDD..8F4004` 取x10（特殊缩进16），`8F4156` 将右值接到左标题测量宽后；每行字体高度+4。字体14/15/16在8e5564..8e568a创建：9号，14为FFFAE8CB，15/16白色。当前Unity字体栅格和测量仍不保证Windows逐像素相同。
- 宠物`8ED8F5..8EDFC7` 默认宽290、高116、图标(10,32)、描述x92宽178，不能使用旧装备文字列表。`8EBFDE..8EC366` 是寿命/期限提示，非空增加16高，Dot/1(10,33)、文字(16,29)；需宠物实例期限等字段，不是固定宠物等级文本。当前已改默认几何和`String/Pet.img/<id>`名称/描述，期限等实例状态待接。

### 本次运行时变更与边界

`ItemTooltip` 移除旧中文自绘属性列表，使用上述装备宽度、原需求/职业/非成长图集及属性行；实际持有装备从InventoryEntry取str等属性、剩余强化次数，不用WZ模板冒充强化后值。背包装备按数据库记录区分同ID实例，悬浮与双击使用相同实际格位。其他普通分类仍复用已确认的290宽公共布局。唯一/不可交易/所有者、装备强化名称与品质颜色、成长、宠物期限、特殊现金补充行仍未完整迁移。

`GameWindows` 全分类双击分派到`NativeInventoryActions`，装备页/身上装备取消单击即穿脱；ConsumeClick后退出格子循环。`EquipLoadout` 不再允许未持有WZ防具凭空穿戴；普通已支持槽位向`NativeInventoryService`发equip请求，直接移动inventoryitems原行，不删除再创建装备，从而保留强化和扩展字段。源端`InventoryManipulator.equip`603起及unequip处理原位置互换、套服/裤子冲突、双手武器/盾冲突、背包满拒绝；服务端`ItemInformationProvider.isTwoHanded`1899按武器类型，140..149包含本端普通双手类。新增sourcePosition以选中同ID实际装备。戒指/项链等尚无Profile槽位，明确返回未接入；未导入WeaponDefs的武器同样保留而不伪报成功。原端性别配置、穿后交易旗标、专属装备/戒指限制、强化等级上限及完整穿戴门槛还需继续迁移。

解除异常按源`UseItemHandler.handleCureItems`：2050001只黑暗121；2050002虚弱122+缓慢126；2050003封印120+诅咒124；2050004按`Character.dispelDebuffs`2876只解除120/121/122/124/125/126，**不可解除僵尸133、眩晕123、诱惑128、混乱132**，没有异常也扣1。纯恢复和item脚本沿用前节；椅子、宠物召唤、回城卷、强化卷、技能书、增益及其他现金道具的独立处理尚未接入统一入口，返回明确反馈并保留物品。不能把反馈当成这些使用功能已经实现。

验证：`QuestNpcChecks.RunInventory`在临时数据库与禁用本地落盘下14通过/0失败（`/tmp/inventory-use-check.log`），覆盖同ID强化装备实例移动、旧装备互换、错误槽位、背包满卸装、统一苹果入口、四种异常药、僵尸不可驱散、未支持分类不误扣、死亡与宠物名称。编译0 warning/0 error。尚未进行Android视觉/双击实机验收。

本次全量回归：`QuestNpcChecks.Run`24183通过/0失败（`/tmp/inventory-tooltip-regression.log`），脚本包889 NPC+468 quest解析失败0。该检查不覆盖Android像素布局或全部特殊物品功能。

## 套装面板（BeiDouSetItemCompat.dll）与装备行距修正（2026-10-29）

EXE 身份同上。新增逆向对象 `clien/BeiDouSetItemCompat.dll`，SHA256 `4be45cec77c0abe5751af9a7d6908343a719dbd3994b391934923cc8c9f8f5f3`、ImageBase `0x65C80000`，仅一个导出 `BDS_RegisterPacketHandler`(`0x65C83F41`)，日志写 `BeiDouSetItemCompat.log`，LOAD 行 `v22 frenzy-totem-1013-dispatch`。

### 它是 hook + 自建 native layer，不是扩装备 tooltip

- 安装段 `0x65C83119..0x65C83BE5` 先逐字节校验 EXE 函数再打补丁，校验目标：`0x8ECA0C`(属性行构建)、`0x8E6E23`、`0x942DCC`、`0x8E8D16`(装备 tooltip 主体)、`0x8F3141`(帧)、`0x8E7E5E`、`0x8E97C3`/`0x8E97E7`(特殊装备红底)。失败只写 `ERROR: hook byte mismatch; no set-item hooks installed`。
- 面板是**独立 layer**：`0x65C82F00` 段用 `PcCreateObject`(间接调 `[0x65C8A894]`) 建层，宽度写死 `[0x65C8888C]=0xEC`(236)；高度写 `[0x65C88888] = (setCount + 2 + Σ槽位非零alt)×0x10 + 0x1C`。注意这个高度按候选 alt 总数估，**实际只画每槽一行**，所以源端底部会留下明显空白。
- 悬停入口日志 `INFO: equipment tooltip item=%d sets=%d`(`0x65C81ACF`)；显示完成 `OK: native set panel shown item=%d set=%d`(`0x65C83762`)。

### 布局与配色（0x65C82F00..0x65C83700，逻辑单位，宽 236）

| 元素 | 参数 |
| --- | --- |
| 标题 | `wsprintfW("%s (%d/%d)")`（`0x65C85576`）= 套装名(已穿/完整件数)，y=7，水平居中（`x=(236-文本宽)/2`，宽≥219 时改 x=8），色 `0xFFFFE137` |
| 装备行 | y 从 `0x19`(25) 起步进 `0x10`(16)；左 x=8 装备名，右端到 `0xE4`(228) 右对齐画 `"(%s)"`（`0x65C8558C`）= 槽位名 |
| 行颜色 | 该行 alt 就是当前悬停件 → `0xFFFFE137`；已穿 → `0xFFFFFFFF`；未穿 → `0xFF90949D` |
| 套装效果 | 最后一个装备行 y（已 +16）再 +3，x=8，色 `0xFFFFE137`，文本 `套装效果`(`0x65C85596`) |
| 档位行 | 槽位末 +`0x13`(19)，x=8，文本 `[%d件]`(`0x65C855A0`)；`已穿数 >= 档位` 白 `0xFFFFFFFF`，否则灰 `0xFF90949D` |
| 属性行 | 接在档位行下方，x=`0x10`(16)，文本 `"%s +%d%s"`(`0x65C85612`)，步进 16；已达成 `0xFFD2D2D2`，未达成 `0xFF90949D` |
| 颜色常量 | `0x65C8886C`=FF FFE137、`0x65C88868`=FFFFFFFF、`0x65C88864`=FFD2D2D2、`0x65C88860`=FF90949D（`0x65C82F76..0x65C82FD2` 初始化） |

属性名来自 DLL 内 UTF-16 中文串，按 **strstr 子串**顺序匹配（不是精确等值）：`PAD`→攻击力(`0x65C8543A`)、`MAD`→魔法攻击力、`HP`→最大HP、`MP`→最大MP、`FinalDamage`→最终伤害、`BossDamage`→Boss伤害、`ExpRate`→经验获得、`DropRate`→掉落率、`MesoRate`→金币获得；其余落到 `属性`(`0x65C8548C`)。后缀：含 `StatusRes`/`BuffDuration` 为空，其余 `Damage`/`Rate`/`Pct` 加 `%`。
**注意**：这一块的属性名是 DLL 自带中文，与装备 tooltip 主体用的 EXE 字符串池英文（`STR :`/`WEAPON ATTACK :`）不是一套。

### 0x17A 包字段（服务端 `PacketCreator.setItemUpdate` 7679 起，与 `SetItemPacketTest` 对齐）

`0x17A(short) setCount(short)`，每套：`setId(int) name(str) completeCount(short) slotCount(short)`；每个槽位 `altCount(short)`，每个 alt `itemId(int) equipped(bool) itemName(str≤40) slotName(str) slotIndex(int+1)`；`tierCount(short)`，每档 `requiredCount(short) statCount(short) [key(str) value(int)]*`；包尾 `activeTier(short) story(str) equippedCount(int)×2`，最后 `0(short)`。槽位名映射见服务端 `setItemSlotName`（100帽子/101脸饰/102眼饰/103耳环/104上衣/105套服/106裤子/107鞋子/108手套/109盾牌/110披风/111戒指/112吊坠/113腰带/114勋章/115肩章，其余按首类给"武器/装备"）。

### 本工程的落地

`AdminSetItem` 加 `PanelsFor(itemId, wornIds)` / `SlotNameOf` / `SetPanel*` DTO，直接读同一份 `Admin/SetItemCatalog`（含后台覆盖层），不跑 0x17A 包；`ItemTooltip.DrawSetPanel` 按上表绘制。

**面板位置（2026-10-29 实拍纠错）**：是**并排**贴在装备框右侧，不是叠在下面。证据 `0x65C83012` 段（已显示过一次、走重定位分支）：`x = [ebp-0x3bc](面板宽 236) + [ebp+0xc](装备框 x) + 4`，`y = [ebp+0x10](装备框 y)`。落地：`posX = equip.xMax + 4s`，右侧越界翻左侧 `equip.x - gap - w`，两侧都不行才夹进屏幕；`posY = clamp(equip.y, 4, H-h-4)`。
早期的「贴正下方 + 放不下往上翻」写法是错的：八件套面板(≈520 逻辑高)比装备框还高，往上翻会把整个装备框盖住，看起来就像装备面板根本没画（用户 2026-10-29 报障）。
属性行多到连同面板自身都超屏时才退化为「每档一行、自动折行」（阈值只看面板高度 `h > H-8`，不再叠加装备框高度），信息不丢。

### 同批修正

- 装备 tooltip 高度：源码 `0x8E8E43` 是 `0xA6(166) + 行数×0x10`，属性行 y=`159+16i`。旧实现按 13 行距写死，属性越多越挤，已改回 16。
- 物品 tooltip 高度：`0x8EEE9E` 是 `116 + (字高+4)×(行数-4)`，超出四行才增高；旧实现用 `max(116, 52+文本高)`，长描述会矮一截，已改为源端公式并与实际文本高度取大。

### 仍未迁移

特殊装备红底 `0xA0400000`（判定 `0x5D2914` 读 info 节点 `+0x204`，字段语义未定）、装备名超 222 逐行 +16 的换行高度、属性负数/强制 0 行（EXE `2049 " -%d"` 与 force 位）、唯一/不可交易/所有者标记。

## 装备 tooltip 需求区空白 + 星级条（2026-10-29 二修）

### 空白根因：素材别名没登记，不是布局错

实拍时需求行（`要求等级/力量/敏捷/智力/运气/人气`）、职业图标、`Dot/0`、成长行全空白，日志刷 `[WzAssets] Unmapped resource: UI/UIWindow.img/ToolTip/Equip/Can/reqSTR`。
`NativeAssetIndex.json` 的 `aliases` 是按**旧 PNG 资产像素匹配**生成的（`tools/package_native_assets.py` 只给 `Assets/Resources/Art` 里存在的旧图找源），新代码引用的原生 canvas 没有旧 PNG 对应 → 永远拿不到。
修法：把 `UI/UIWindow.img` 的 `ToolTip` 子树**全部 135 个 canvas** 直接登记成 `"<img路径>": {"source": "<同名路径>"}`（`SetPanelRect` 之外的离线脚本，一次性），重跑实拍 `Unmapped resource` 归零，需求/职业/成长/标题点全部出来。

### 布局复核（对 EXE `0x8ED0D2..0x8ED8E0`）

- 需求 6 行在 x=`94`、y=`32+12*i`，数字 x=`144`、步进 6；`8F5056` 的判定是「need==0 直接算 Can，否则 have<need → Cannot」，所以**0 需求也照画数字 `0`**（不是隐藏）。
- 职业图标 x=10/52/92/132/171/197、y=133；`reqJob=0` 时六个职业全 Can。
- 成长行 x=94、y=104/116（`GrowthDisabled/itemLEV|itemEXP|none`，无 `info/level` 节点时画）。
- 分隔线 x=6、y=152。属性行 y=`159+16i`。
- 实拍核对：`要求等级 : 45` 红（Lv.1 < 45）、`要求力量/敏捷/智力/运气 : 0` 白（need=0 → Can）—— 与源端一致，之前的「全红」是肉眼误判。

### 星级条（BeiDouSetItemCompat.dll 0x65C82B4E）

- 挂点：把 EXE 三个画帧调用 `0x8E7E5E` / `0x8E97C3` / `0x8E97E7`（全是 `call 0x8F3141`）换成 dll 函数，帧画完立刻叠星条。
- 星数：`0x65C81650` 从装备名字符串**逐字节解析**——首字节必须是 `'0'..'9'`、次字节也要是数字（拼成两位数），随后 3 字节必须是 `A1 EF 00`（GBK `★`），再 `cmp stars, capacity` 超限返回 -1。即私服把星级写成**名字后缀 `<名>NN★`**。
- 上限：`0x65C81949` 按**装备等级**查表 ≤`0x5E`(94)→5、≤`0x6B`(107)→8、≤`0x75`(117)→10、≤`0x7F`(127)→15、≤`0x89`(137)→20、其余 25。
- 几何：宽=装备框宽(236)，高 `列数×15+12`（≤15 星 27、>15 星 42）；第一列 y=6、第二列 y=21；每列最多 15 颗、步进 9、**每 5 颗多 4px**、整块水平居中 `x=(236-总宽)/2`；索引 = `列*15+i`，`stars <= 索引` 用灰 `0xFF737B86`，否则金 `0xFFFFD83D`（全局 `0x65c86840/0x65c86844`）。
- 源端是独立 layer：`0x65C82B4E` 的 y 形参**直接取装备框的 y**，星点相对 layer 顶 y=6 / 21 ——
  也就是说星星画在 **框顶**，不是框底（2026-10-29 用户指出后复核确认）。
- 数据：星数解析的是**装备实例的 owner 字符串**，不是名字——源端脚本 `星级强化.js` 强化成功/失败后
  `item.setOwner(item.getStarLevel() + "★")`，客户端从 owner 里读 `NN★`。
  本工程 `inventoryequipment` 另有 `starLevel/starCount/maxStar` 三列（对齐源端 `Equip.java` + 迁移 V1.9.7），
  tooltip 直接读实例字段，owner 解析 `StarFromName` 只在老存档兜底。

### 落地与验收

`ItemTooltip.DrawEquipment` 增加 `equipName/stars/capacity/starBar`，`DrawStarBar` 按上表绘制，`StarCapacity`/`StarFromName` 提成 public 供断言。
`TooltipLayoutChecks.Run` 增补 13 条等级上限 + 5 条名字解析断言 → 同批跑 **264 通过 / 0 失败**；编译 0 warning / 0 error。

## 星之力强化（升星）整套逻辑（2026-10-29）

源端不是 Java 实现，是**脚本 + Equip 字段**：

| 环节 | 源端事实 | 本工程落地 |
|---|---|---|
| 入口 | `BeiDouSpecial/装备中心.js` 第 4 项 → `openNpc("装备制作/星级强化")` | 脚本原样跑（`ScriptSource.LoadNpc` 已支持 `BeiDouSpecial/<子目录>/<名>`） |
| 条件 | 装备栏**第 1 格**、必须是武器（1300000~1492999）、可升级次数已用完 | 同 |
| 上限 | `getMaxStars(reqLevel)`：≤94→5 ≤107→8 ≤117→10 ≤127→15 ≤137→20 其余 25 | 同 |
| 费用 | `1000 + reqLevel³×f(star)`，≤9 星线性、10~14 星 `(s+1)^2.7/400`、15+ 星 `/200`，取整到千 | 同（脚本自己算） |
| 概率 | `STAR_RATES[24]` 万分比（0.95→0.10 成功，18 星起有爆装 6.8%~18%） | 同 |
| 连败保护 | 连败 5 次下一次必成 | 同 |
| 降星 | <17 星不过期；10/15/20 是安全点；否则 -1 | 同 |
| 防爆 | 17→18 可花双倍金币；或 ETC 第 2 格放 `4260012..4260019` 防爆卷（减 200~1600 万分比） | 同 |
| 属性 | ≤15 星主属性 +2/+3、攻击 `floor(watk/50)+1`；16+ 按等级段固定值 | 同 |
| 落库 | `forceUpdateItem(item)` + `setOwner("N★")` | 新增 `ITransport.UpdateEquip` → `NativeEquipService.Patch` 整行覆盖 |

脚本需要而本工程原先缺的接口（已补）：`getInventory(type).getItem(slot)`、item 的
`getStarLevel/setStarLevel / getStarCount/setStarCount / getMaxStar/setMaxStar / setOwner / get|set+四维攻防`、
`InventoryManipulator.removeFromSlot`（按格号删，原先直接抛「未迁移」）、`ItemInformationProvider.getEquipLevelReq`。

**仍未迁移**：星力只改装备实例，战斗数值仍走 `EquipLoadout.Apply` 的 WZ 静态值 ——
要强化真的影响伤害，得让 `Apply` 叠加实例增量（下一步）。


## 属性行标签的中文来源（2026-10-29）

属性行函数 `0x8ECA0C` 里的标签**全部走字符串池 id**，由 `ijl15.dll` 换成中文（机制见 `binary-and-tools.md`「客户端中文从哪来」）。按 id 对账后的实际显示：

| 池 id | 源端英文 | 中文客户端显示 |
| --- | --- | --- |
| 5530 | `CATEGORY :` | `装备分类 :` |
| 656 | `ATTACK SPEED :` | `攻击速度 :` |
| 2042/2043/2044/2045 | `STR :` `DEX :` `INT :` `LUK :` | `力量 :` `敏捷 :` `智力 :` `运气 :` |
| 2046/2047 | `HP :` `MP :` | **未翻译，仍显示 `HP :` `MP :`** |
| 657/658 | `WEAPON ATTACK :` `MAGIC ATTACK :` | `攻击力 :` `魔法力 :` |
| 659/660 | `WEAPON DEF. :` `MAGIC DEF. :` | `防御力 :` `魔法防御力 :` |
| 661/662/663 | `ACCURACY :` `AVOIDABILITY :` `HANDS :` | `命中率 :` `回避率 :` `手技 :` |
| 664/665 | `SPEED :` `JUMP :` | `移动速度 :` `跳跃力 :` |
| 667/669/670 | `THE RATE OF KNOCK-BACK :` / `ADD PREVENT SLIPPING|COLDNESS` | `直接攻击时发生后退现象的几率 :` / `添加防滑` `添加防寒` |
| 668 | `NUMBER OF UPGRADES AVAILABLE :` | `可升级次数 :` |

分类值（`0x8ED0D2` 之后那行）：617 帽子、618 脸饰、619 眼饰、620 耳环、621 套服、622 上衣、623 裤/裙、624 鞋子、625 手套、626 披风、627 盾牌、628 戒指、629 坠子、630 勋章、631 腰带；武器 601 单手剑、602 单手斧、603 单手钝器、604 短刀、605 短杖、606 长杖、607 双手剑、608 双手斧、609 双手钝器、610 枪、611 矛、612 弓、613 弩、615 指节、616 短枪。**614 ` CLAW` 源端漏翻**（表里没有），迁移时按习惯写「拳套」。
攻速值 644..650：非常快／比较快／快／普通／慢／比较慢／非常慢。
物品框任务标记：693 `Quest Item` → `任务道具`。

落地：`ItemTooltip.cs` 里加了 `LCategory/LAttackSpeed/LUpgrades/LQuestItem/StatLabels/SpeedNames/WeaponNames` 常量组，每条注释标了池 id；`DrawEquipment`/`DrawCommon` 不再有英文文案。

技能详情框的末块标签：674 `[Current Level %d]`、675 `[Next Level %d]` 在表里**没有中文**（源端截图里就是英文 `[Next Level 1]`），3852 `[Master Level : %d]` 译作 `[能力 级别：%d]`。本工程按需求统一写成中文 `[下一等级 n]` / `[已达最高等级]`，是有意偏离源端的一处，改回源端只需换 `SkillWindow.DrawSkillData` 里那一行。

## 悬浮框预览形式改造（2026-10-29）

旧实拍 `PreviewShot` 的 tooltip 场景是「钉指针 + 抓整屏」，两个致命依赖：

- 指针位置 = `(窗口 pos + 格内偏移) × BottomBar.S`，而 `S = min(w/1281, h/600) / UiKit.Scale`。
  进 Play 头几帧 `Screen` 没落定，实测出现过 `S=0.067`，`Repin` 因 `S<0.3` 直接 return，
  `Pointer.TestPos` 为 null → 抓出来的图里根本没有悬浮框（11-15 那一轮 `07-star-tooltip.png` 空白）。
- 编辑器里 `Screen.width/height` 会随 GameView 抖动，同一轮录到过 `1206x20`、`2412x40`、`693x394`，
  任何按 `Screen` 换算的裁切/定位都会错。

新形式（`Assets/Editor/TooltipPreview.cs` + `tools/compose_tooltip_sheet.py`）：

- `ItemTooltip.PreviewDraw(id, anchor, instance)` 与指针完全解耦，定点画一次；
  `LastEquipRect` / `LastSetRect` 记录真实外框，供裁切。
- 预览期 `UiPreviewHook.SolidBackdrop` 铺深色底（超大 rect，不用 `UiKit.W/H`）；
  虚拟按键在 `InputHub.OnGUI` 里绘制、晚于 `GameWindows.DrawAll`，`GUI.depth` 压不住，
  所以预览期（`UiPreviewHook.Draw != null`）直接不画 `VirtualControls`。
- meta 存**归一化外框**（÷`UiKit.W/H`），拼图按 PNG 尺寸反算；这样与外框绘制帧的画布尺寸无关。
  抓图前要求 `UiKit.W/H` 连续 3 帧不变（`stableCount`），保证画面帧与外框帧同尺寸。
- 结果：一次运行 ~10 秒出 4 张（0★/12★ 武器、帽子+套装面板、消耗品），
  本地按外框裁切 + 2x 放大拼成一张对照长图；编辑器菜单 `Tools/预览/悬浮框对照图` 同入口（不退编辑器）。

## 套装面板字号（待核）

源端 `BeiDouSetItemCompat.dll 0x65C82F00` 段能定位到颜色常量
`0xffffe137`（标题金）/`0xffd2d2d2`（属性 D2D2D2）/`0xff90949d`（未激活 90949D），
行距 16，但**创建字体那条路没走通**：`0x65C82FE4` 是 `mov edx,0x2000; mov eax,0x8e49b5; call eax`，
`0x65C81019` 反汇编出来是 memset（不是字体），`0x8E49B5` 是 EXE 里 2740 条指令的构造函数，
不像 CreateFont 封装，该调用链待继续确认。

本工程按「两侧行距都是 16、装备框属性行是字体7 = 12 号白色（`0x8F3704` 取 this+0x440，
`0x8E5172..0x8E51A7` 设 12 号）」把套装面板从 9 号统一到 12 号 ——
9 号字摆在 16px 行距里明显偏小（用户 2026-10-29 反馈「下方属性描述的文字又很小」）。
**这是按行距一致性推的，不是 DLL 实测字号，后续如定位到字体创建点需以其为准。**
