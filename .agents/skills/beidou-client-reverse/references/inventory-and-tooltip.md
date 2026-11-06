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


## 玩家背包椅子使用（2026-10-30）

椅子效果原端证据复用[假人系统](solomapling.md)的0x93C7C3/0x941417：effect/effect2、z、fixed、pos1=brow；
不得重新猜成navel。原UseChairHandler校验301类及SETUP持有数量后调用Character.sitChair，椅子不消耗；
Character.java:7565物品椅子设置chair并广播showChair，地图椅子另走registerChairBuff分支。
NativeInventoryActions原先无条件提示“椅子使用功能尚未接入”；现检查存活/背包数量后调用PlayerCtrl.UseChair，
复用NativeBotChair读取原Item/Install/0301.img，不复制或自绘椅子素材。
坐姿取消（移动/跳跃/攀爬/攻击）、死亡、复活和PlaceAt均清除椅子效果。当前只复用已核实的普通pos0/1特效；
复杂多人数/骑乘类特殊椅子、服务端外观广播与钓鱼不在本次迁移范围。
验证入口 `NativeGameplayFixChecks.Run`覆盖3010000/3010001原效果创建、不消耗及攻击/死亡/换图清理。

## 装备槽位、NPC物品与特殊物品入口修复（2026-10-30，本轮）

新记录见 [物品使用与装备槽位](inventory-use-and-equipment.md)。前文“戒指/项链没有Profile槽位”的迁移状态已由本轮替换：
原端 getEquipSlotType 映射、四枚戒指、吊坠、腰带、肩饰、副手、心脏、徽章、纹章与图腾负槽位已贯通数据库→Profile→属性与装备窗。
原IMG背板 Equip/backgrnd175×304、FullBackgrnd208×291，origin0,0；既有格子左上(5,35)、步长33、32×32图标矩形保持。
本次逐图核对：小窗戒指在(row1,col3/4)及(row4,col3/4)，吊坠(row3,col2)，图腾(row5,col4)；大窗戒指在(row1,col4/5)、(row4,col4/5)，吊坠(row3,col2)。
FullBackgrnd没有图腾格，不另绘新格；小窗使用原图的图腾格。未改变背景或新增自定义UI。
饰品持久化用PlayerProfileData平行列表，纸娃娃Appearance与属性Worn分开，避免把无纸娃娃层的图腾/吊坠传给衣帽合成器。
2029001/2029006的spec/isNpc入口已接线；其他缺口以新审计报告为准，不能把脚本入口存在视为完整物品效果通过。


## 2026-10-31反馈补查

ItemNo数字图与原坐标补查见 [本次确认记录](field-feedback-2026-10.md)。保留其中的原端身份、地址、资源参数与验证边界，不将静态资源扫描等同于实机验收。

## 2026-10-31 双击闪烁

InvenWindow.Draw双击时在绘制cell循环内break，导致该帧后续所有物品没画；现继续画已取得的entries/equipment
快照，Pointer.ConsumeClick保证只执行一次物品动作。物品变化下帧自然可见，不重建或清空全窗贴图。
这是项目绘制错误修复，不修改已记录原IMG布局。验证见docs/pet-review/four-feedback-verification.md。

## 2026-11-01 背包金币样式补核

EXE SHA256仍为 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`。`81DC20`绘制入口，`81DC67..81DC84`读meso并调用`988690`（参数1，每三位逗号）；`81DCB8`用font24测量，`81DCD8`以138减文字宽；`81DD31..81DD73`取font25在(x,266)绘制。`98A707`跳表`98CB81`中24/25分别到`98B618/98B6B7`，两者12px，24白色(-1)、25黑色(FF000000)，字体串5527。复现用inspect_beidou_client.py的上述--start/--end与PE DWORD跳表读取，不把跳表当指令。

InventoryWindow金币由未分组11px中线对齐改为InvariantCulture N0、12px黑色、上对齐、右界138；保持原IMG金币框与图标。Unity字形不等同Windows字体，Android视觉尚未验收。

## 2026-11-02 背包整理 / 合并（ITEM_SORT2 / ITEM_SORT）

源端依据：
- `net/server/channel/handlers/InventorySortHandler.java`（`PacketProcessor:299` → `RecvOpcode.ITEM_SORT2`）
- `net/server/channel/handlers/InventoryMergeHandler.java`（`PacketProcessor:180` → `RecvOpcode.ITEM_SORT`）
- `client/inventory/Inventory.java:264-303`（`move`）、`manipulator/InventoryManipulator.java:489-529`

算法（照抄，不能换实现）：
- 排序用 `PairedQuicksort`：先按主键全排 → USE 栏对 `itemId/10000` 为 206/207/233 的三段各做**一次** `PartitionByProjectileAtk`（不递归，源端的近似倒序，key = `info/incPAD`）→ 再按 itemId 分组、组内用副键排。主键 = `use_item_sort_by_name ? 名称 : itemId`；副键 = 装备(3)按 `inventoryequipment.level`，其余(1)按数量。**这是不稳定快排**，用 OrderBy 会让相同 itemId 的堆叠保持原序，与源端不同。
- 合并 = `InventoryManipulator.move` 的完整分支：dst 空 → 移动；同 id **且非 207 充能且 owner 相同** → 相加（超 `slotMax` 时 dst 填满、源留余量）；否则 **swap 槽位**。`Merge` 只按 itemId 预过滤，owner 不同仍会走 swap。第二段把后面的物品前移填洞。
- 两个 handler 都受 `game_config.use_item_sort`（默认 true）总开关控制，关闭时只回 enableActions / 不动背包。

客户端按钮语义：
- `0x81E3A6..0x81E521` 按状态值 0/1 **二选一**创建同一槽位的按钮：0 → 字符串 1851 `Item/BtGather` + id 0x7D3，1 → 字符串 1852 `Item/BtSort` + id 0x7D4，坐标都是内部宽-15、y=6（origin 6,6 中心）。
- 内部宽 = 窗宽-20，所以小窗 155-15=140、展开 583-15=568。项目按 `width-35` 传（175/603），**两者数值一致**，位置无需再改；BtFull 的 `width-50` 同理对应内部宽-30。
- **待核实**：0x7D3/0x7D4 的消息分派分支没有反编译出来。当前实现按语义推定「状态 0 画 BtGather → 发 ITEM_SORT(合并)，状态 1 画 BtSort → 发 ITEM_SORT2(排序)」，点击后翻转状态。要确认得继续追背包窗的消息处理函数里对 0x7D3/0x7D4 的比较分支。

迁移与验证：
- `Assets/Scripts/Server/World/NativeInventorySort.cs`：`Sort` / `Merge` 全量移植；`NativeInventoryService` 加 `sortItems` / `mergeItems` / `sortInventoryType` 分派；`InventoryEntry.equipLevel` 承接 `inventoryequipment.level`。
- `GameWindows.InvenWindow.Organize` 接真实请求，回包整份背包覆盖内存并投影 Profile；`ApplyInventoryToProfile` 改为按 `(inventoryType, position)` 排序投影，否则服务端改 position 后界面看不出变化。
- 回归 `tools/server-check/InventoryOrganizeChecks.cs`（14 项：主键 itemId、副键数量、装备按 level、合并/超上限留余量/紧排、装备栏不合并、开关关闭、页号越界）→ `dotnet run --project tools/server-check` = **759 通过 / 0 失败**；`tools/unity-compile-check` 0 warning / 0 error。
- 边界：`Profile.itemIds` 是 id→数量的扁平结构，同 id 多格在界面上本来就会并成一格总数，所以「合并」在 UI 上的可见度有限，服务端数据层的槽位与数量是准的；未做 Android 实机验收。


### 2026-11-03 机器人装备描述纠正

“装备通常无desc”不能实现成无条件跳过装备。相同BeiDou身份，装备入口`8E8E81`也调用`5CF69E`读取描述；非空`8E9720..8E975B`用字体7，x10、右界221，`8F4535 ->8E40E4`测量后追加窗口高度；`8E9EC1..8E9F11`在属性后绘制。复现用`inspect_beidou_client.py --fn 0x8e8252`及`--fn 0x8f4535`。

当前282 BMS String/Eqp.img的`Eqp/Android/1662000`有原始desc，1672000只有name无desc。运行时166/167子目录应是Android，移植未复制String整文件，须用Items.json导出的原文回退；完整证据及边界见根目录`docs/tms282-skill/12-android-regression.md`本次反馈节。已接装备描述读取/测量/绘制，未虚构心脏文案；尚无本轮手机视觉验收。


### 2026-11-03 282星力详情显示纠正

旧武器专属星条限制只适用于旧系统。enhancementVersion=282装备按StarForceRules.Cap显示全部支持装备的
星级，属性行须在实例纯基础属性之上加StarForceStats.Calculate派生层，不可把该层再存回实例。
最新实例按itemRowId解析；装备痕迹不计有效星力加成。详见根docs/tms282-skill/16-enhancement-entry.md
「星力成功后装备详情未变化」。此次为项目显示缺口修复，无新原EXE逆向结论，身份及星条绘制证据沿用本记录。


### 2026-11-03 客户端等级星级上限正式核实

用户选择按282客户端等级分档修正。ItemTooltip不再按enhancementVersion切换星条容量，所有可星力模板
统一调用StarForceRules.Cap(id,reqLevel+iIncReq,superiorEqp)，与预览/提交/maxStar保存相同。
282本机EXE SHA256 def89cf3c51b5d196b90776d64a68898d722c26f8ce14f97f8c3930e9c4cfc27、
ImageBase140000000的A08930已核实普通5/8/10/15/20/30、尊贵3/5/8/10/12/15，
门槛95/108/118/128/138；206个特殊ID覆盖链及数据导出详见根docs/tms282-skill/08-star-force.md §19。
不再使用282.5默认30/15或其6项特例，也不能冒用旧北斗StarCapacity。已有超限星数保留展示，继续强化拒绝。
服务回归28通过/0失败、Unity编译0警告/0错误；未做本轮Android实机验收。


### 2026-11-03 用户要求直观显示星数与攻击加成

在既有星力强化N星说明下增加「星力攻击力 : +N」「星力魔法力 : +N」两行。
数值直接取本次已计算的StarForceStats派生watk/matk，表示当前全部星数累计贡献，0也显示；
上方攻击/魔法属性总值已包含该贡献，不重复加算。装备痕迹仅显示保留星数，不显示有效攻击加成。
这是用户明确要求的说明文案适配，复用现有属性行绘制/自动高度，不新增素材或改动结算规则。
本次仅做差异/数据流检查，未运行新编译或Android视觉验收。更新APK生效，无需修改IMG。


### 2026-11-04 缺少装备属性行与强化预览区

新手建角、默认AddItem和后台type5可产生只有inventoryitems的装备，强化拒绝；现用EnsureEquipment
只为已有装备且模板存在的缺行补建模板基础属性/tuc，既有自定义属性及强化字段完全不覆盖，读档持久化。
强化区保护按钮改原IMG直接按状态绘图，15/16/17普通有效预览才启用，其余点击解释；原diff星字图和箭头
替换裸数字，封顶不显示下一星；中间以中文显示累计星力加成及本次差值，概率补全失败不破坏/破坏标签。
详见根docs/tms282-skill/16-enhancement-entry.md「2026-11-04」节（节点、来源、验证与未验收边界）。
本次未改变282上限/概率/费用，不新增EXE结论；星力49项、全服务1664项通过，手机实机未验收。


### 2026-11-05 用户截图指定的装备详情卡

用户明确要求按其截图调整布局。`ItemTooltip.DrawEquipment`改用282原始
`UI/UIToolTip.img/Item/Equip`皮肤，全部装备统一显示324逻辑宽；普通物品提示不变。
这是截图授权的布局适配，不是新发现的EXE调用链或逐像素复刻结论。

资源证据/复现：`tools/ms282/dump_img_tree.py UI/UIToolTip.img --branch Item/Equip`；
`tools/ms282/import_equipment_tooltip.py`复用既有Importer，闭包解析_outlink，
转GMS编码/ARGB4444并逐节点、像素负载校验。原文件及闭包SHA256、36张Canvas、
原始节点参数保存在工作区根`docs/tms282-skill/data/equipment-tooltip-import.json`。
已核实frame/common top324×30、mid324×1、btm324×12、line324×3，
space.frameMarginX=15、frameMarginY=10、reqInfoOffsetX=100；
font/common/itemName14粗体、normal12白、gray B7BFC5，
starForce FFCC00、bonusStat 0AE3AD、potential各品级颜色直接读取原节点。
本轮未新增282 EXE函数地址结论。

迁移：原星条置顶，名称居中，原装备图标及origin按2倍显示，右侧显示实例真实穿戴状态及类别，
下方保留等级/属性/职业需求，属性自动折行，主/附潜能原图标与分隔，
星力派生值以黄色括号拆分，星火绿字仍独立列出。灵魂原有信息全部保留。
超长卡按屏幕可用高/宽统一缩放，测量和绘制同字体/内容宽，避免底部裁失。
截图里的战力比较、交易标记、套装/技能说明及星火并入属性总行并未在本轮新增，
不填示例值、不将星火独立行冒称为总属性；现有单独套装面板继续贴侧边。
编译0warning/0error；Unity实拍验证结果待本节后续补记，Android尚未验收。


### 2026-11-05 详情卡透明框与配色复报纠正

透明原因已定位为项目调用错误：`WzAssets.Texture(string)`参数走别名表，
而新导入UIToolTip只登记files、未登记aliases；导致背景/线/图标全部返回null。
改为`Texture(Node(EquipAtlas+path))`直接读取原IMG，不能用旧北斗自绘帧掩盖。
绘制前保存并重置GUI.color/contentColor，结束恢复，避免继承外层染色。
潜能标题用已记录品级色表，详情词条白色；移除下方重复分类行（分类已在图标旁原底板显示）。
此前“已完成”仅有编译而未看截图，不能作为视觉通过依据。
本次Unity实际四场景截图已生成并检查12星武器裁图，背景/图标底/分隔正常；
`/tmp/equip-layout-fixed`仅为临时截图，复现入口仍是仓库TooltipPreview.Run。
编译0warning/0error；Android实机与完整潜能灵魂长面板仍未验收。


### 2026-11-05 星条、实机资源与完整内容复查

手机 Unity 日志已复现 FileNotFoundException：/sdcard/mapstory/Data/UI/UIToolTip.img，
使 DrawEquipment 中断而整卡消失。外置资源已补传；同时将同一份转换IMG以
Assets/Resources/UI/EquipmentTooltip.bytes随APK打包，优先解析该TextAsset，
不再要求新版渲染代码和外置皮肤碰巧同步。此修复需新APK生效。

新增星条闭包后共41张Canvas、9363字节；textIcon/starForce/normal实际灰色，
不能当点亮星使用；star节点_outlink才是Enchant summaryStar/normal金星，
转换后中心像素FFFFDD00，empty为空星，blue尊贵星。
部分潜能detail/normal为Vector而不是Canvas；绘制前检查Kind，不能盲取Texture。
所有GUI矩阵、颜色和enabled均try/finally恢复。

需求信息按固定两列（标签x15、值x100）逐行测量；固定星火属性用与
NativeGrowthStats.ApplyFlameStat相同映射并入总值，绿字括号；百分比仍独立，
不把全属性百分比错误直接相加。星力黄色括号；无独立卷轴贡献来源时不虚构紫字。
灵魂保留阶段、等级被动、可用选项和技能说明，压缩冗余解释。
交易提示沿用NativePlayerShopService相同flag8/16和tradeBlock/accountSharable/quest判定。

TooltipPreview真实窗口悬停/SetHover+Flush共17场景已出图：0/12星、帽子套装、
主附潜能0..4级及隐藏、满15属性+六潜能+星火+现代灵魂、旧灵魂、装备痕迹、
符文、右下边缘和普通物品。完整长卡712逻辑高，边界及GUI矩阵断言无失败；
本轮编译0warning/0error。星图纠正后第二轮17场景也全部出图并复核金星/完整长卡；手机新版APK未验收。

背景颜色边界：原common/mid转换后的中心RGBA=(34,51,51,255)，确实偏绿；
非GUI染色。参考图偏灰蓝，ARGB4444精度与原皮肤差异仍在，不能冒称逐像素一致。
本轮没有新EXE逆向结论；源文件身份和各_outlink仍见equipment-tooltip-import.json。


### 2026-11-05 用户指出两行星条漏测后补测

此前17场景只有15槽武器，不构成两行星条验证。现TooltipPreview新增真实
1302343神秘之影单手剑（模板reqLevel200/tuc8），断言StarForceRules.Cap=30；
0/15/16/30星及30星+六潜能/星火/灵魂/右下边缘共5场景，总场景22。
实际截图按每颗星中心采样金色，逐行点亮数分别为[0,0]/[15,0]/[15,1]/[15,15]，
完整长卡也是[15,15]；人工检查名称未重叠、第二行及底部灵魂可见，长卡712高。
截图复现目录/tmp/equip-two-row，入口仍TooltipPreview.Run；这是Unity模拟，
不能作为Android实机两行星条验收。前一APK构建被SIGTERM终止，不能声称已安装。

补测视觉边界：右下长卡实拍中InputHub的GameSideMenu悬浮球（GUI.depth=-300）
遮住卡右侧穿戴状态区域。星条/名称/底部没有裁失，但不能由此声称全卡无遮挡；
本轮未改全局菜单层级，后续验收必须保留这一缺口。


### 2026-11-05 实机1402267复报：封印创世模板与名称缺口

用户截图明确itemId1402267，Source String/Eqp.img/Eqp/Weapon/1402267/name=
「封印的創世雙手劍」。Bot/Items.json此前有info却无names条目，ItemCatalog/WzNames
最终退为「道具1402267」。export_solomapling_items.py之前282 String回填只覆盖
Android及Symbol；现从同一282客户端Eqp全部分类补齐已导入且缺失的名称/描述，
保留已有名称；本次实际新增1585个名称。源String身份仍记录在Items.json.sources。

该模板reqLevel200、incPAD340、exceptUpgrade1、exUpgradeBlock1、无tuc；
NativeStarForceService原本要求tuc>0。前次tooltip因IsWeapon/version282等条件过宽
错误显示两行空星，已收紧cash0、tuc>0且无禁止强化标记；已有真实非零星数仍保留显示，
不凭截图生成金星或改存档。源码名及标记复现：dump_img_tree.py
String/Eqp.img --branch Eqp/Weapon/1402267 与导出的Character/Weapon/01402267.img info。
这属于源资源/现有服务规则核对，未新增EXE结论。

TooltipPreview新增用户指定sealed-genesis场景，检查真实名称、0星封印模板及第6阶段
Lv1灵魂；实际出图名称正常、错误空星消失，普通30槽对照仍绘制两行。
Unity编译0warning/0error；截图/tmp/equip-sealed-genesis。Bot/Items.json随APK内置，
仅更新外置IMG不能更新此名称表；本轮未声称Android已安装或验收。


### 2026-11-05 手机长详情自动续栏（用户明确授权布局适配）

用户要求显示不全时在右侧另悬浮一块分割。复用本记录原UIToolTip common
324宽背景/分隔素材，新增FlowEquipRows按测量后的完整行装栏，续栏顶15；
主附潜能标题尽量与三条词条成组，灵魂标题与后续说明成组，顺序不改。
原整卡按高度缩小改为优先正常BottomBar.S，按屏幕可用高度拆成多栏，
栏间4逻辑像素；只有总宽装不下才缩放并重新排版，避免屏幕边缘越界。
套装沿用本记录已核实原代码帧/236宽/12字号/16行高，槽位与各档属性
同样按屏幕高续栏，不再只夹顶边留下底部裁失。整组先算宽高统一夹屏，
装备续栏和套装互不覆盖；LastEquipRect记录全部装备栏包围框。
这是用户授权的新排版，不声称源EXE本来支持自动分栏；未新增EXE地址结论。

验证：TooltipPreview全部23场景，1280x720与1280x600两轮；两行30星+15属性
+六潜能+星火+灵魂+套装/右下角实拍，在600高时拆成两栏装备、两栏套装，
底部技能及套装7件档可见。新增LastSetRect屏内/不与装备组重叠断言，
编译0warning/0error；复现目录/tmp/equip-columns、/tmp/equip-columns-mobile。
手机APK本轮未安装；右侧全局悬浮按钮仍可能遮住局部，这不是分栏边界断言范围。

### 2026-11-06 「可升级次数」行的 282 皮肤与 0 次显示

用户在装备详情里报「漏了一个可升级次数」。根因不是算错，是本工程自己加的
`if (upgrades > 0)` 把 **剩 0 次** 的整行吞掉（銀之心这类 `tuc=0`、以及砸满卷的装备）。
282 的 tooltip 皮肤能直接证伪这种写法：

| 节点（`Data/UI/UIToolTip.img/Item/Equip`） | 尺寸 | 主色（ARGB4444 量化后） | 用途 |
|---|---|---|---|
| `textIcon/scroll/normal` | 11×9 | `#85919F` 灰 | 次数已用尽 |
| `textIcon/scroll/enhanced` | 11×9 | `#AFADFF` 淡紫 | 还有剩余次数 |
| `font/scroll/normal` | 12px | `#AFADFF` | 这一行的文字色 |
| `textIcon/hammer/{normal,enhanced}` | 12×10 | 灰 / `#AFADFF` | 黄金锤剩余次数（同套两态） |

**normal 是「该效果为空但仍占位」的灰态，不是隐藏**：同一约定在
`textIcon/starForce`（normal 灰 → enhanced `#FFCC00`，等于 `font/starForce`）与
`textIcon/bonusStat`（normal 灰 → enhanced `#0AE3AD`，等于 `font/bonusStat`）上逐条复核过。
所以 282 的装备框恒定画这一行，剩 0 次只把图标换成灰。布局常量在 `Item/Equip/space`：
`statUpgradeOffsetX=123`、`statUpgradeSpaceX=6`、`textIconSpaceX=4`。
`textIcon/hammer` 说明黄金锤次数是**独立一行**（本工程黄金锤机制整体未迁移，未实现）。

落地（用户选「图标 + 中文标签 + 数字」）：
- `tools/ms282/import_equipment_tooltip.py` 的 `SECTIONS` 增 `Item/Equip/textIcon/scroll`
  （只导这两张 11×9，不导同级 hammer）。重导后 43 canvas / 9712 bytes，
  新 `UI/UIToolTip.img` sha256 `7763a0ca68c33f6c5c4cd53c3a987f578534ea98e793374f8b129e6eab1a5f76`，
  已同步 `NativeAssetIndex.json` 与 `MapleDashResources/resource-manifest.json`；
  `Assets/Resources/UI/EquipmentTooltip.bytes` 同步覆盖。
- `ItemTooltip.DrawEquipment`：判据由 `upgrades > 0` 改为
  `info.Get("tuc") != null || upgrades > 0`（模板根本没有 tuc 节点的禁强化装备仍不显示——
  1402267 封印创世剑 `exceptUpgrade1`/`exUpgradeBlock1`/无 tuc，截图核对依旧无此行）；
  `upgradeSlots` 记录本次数值，渲染时 `textIcon/scroll/{enhanced|normal}` + 文字 `#AFADFF`，
  文字 x 后移 `11+4`（图标 + `textIconSpaceX`），图标画在 `y+3`（行高 16 内垂直居中）。
- 像素真值校验：用 `wzpy.decode_canvas` 独立解 282 原 `UI/_Canvas/UIToolTip.img` 作真值，
  再解新包比对主色，未用同一 decoder 自证；通道未互换（R/B 正常，ARGB4444 量化误差 ±6）。

验证：`unity-compile-check` 0 warning / 0 error；`TooltipLayoutChecks.Run`
264 通过 / 0 失败；`TooltipPreview` 1280×720 实拍 23 场景——`02-weapon-0star`
显示紫图标 + 「可升级次数 : 7」、`21-symbol` 显示**灰图标 + 「可升级次数 : 0」**（本次核心验收点），
`01-sealed-genesis` 仍无此行。截图目录 `/tmp/mapledash-tooltip`。
`MapleDashResources.zip` 本轮未重打（9.4 GiB），发版前需跑 `./tools/pack_resources.sh`。

### 2026-11-06 战斗力（atkPow）与「万/亿」数值写法

用户报两件事：① 282 有「战斗力的提升量」逻辑，问实现了没；② 金币、装备详情里的战力
超过阈值会从纯数字变成「多少亿多少万」。

**282 侧证据**

- `Data/UI/UIToolTip.img/Item/Equip/imgFont/atkPow` 有 `plus` / `minus` 两套字形：
  `sign` + `0..9` + `a` + `b`，`space=2`，外加一张 `equipped`（145×18，已装备态）。
  像素在 `UI/_Canvas/UIToolTip.img`；`plus/sign` 白、`minus/sign` 洋红 `#F00068`，
  数字 10×21、`a` 19×19、`b` 20×19 ⇒ a/b 是「万」「亿」两个字，且**整套字形没有小数点**。
- 客户端 .rdata 的路径池同段出现 `/atkPow/imgFont`、`/atkPow/highlight`、
  `/atkPow/highlight/particle`、`plus` / `minus`、`boxAppearTiming`、`baseBefore`、
  `baseAfter`、`diff` ⇒ 画的是「穿上这件之后战斗力从 baseBefore 走到 baseAfter」，
  增加时有高亮粒子；`atkPowBox` / `atkPowMinus` 还出现在 `UICubeWz2`（方块）与星火窗口。
- 单位阶梯在 `String/StringPoolWz2.img`：`MasterDisciple/UI/NumberUnit1..4` = 京 / 兆 / 億 / 萬。
  客户端 EXE 里没有 `%d亿` 这类格式串（`億`/`萬` 只以字形图片存在），所以单位是**图数字形**
  拼出来的，不是 printf。
- 文案侧只有 `StringPoolWz2` 的 `FamiliarLinkActivateNotice`「※ 啟用時 戰鬥力提升量 : {}」
  与 `StringTable` 的 `SID_AUTOENCHANT_*_ATTACKPOWER_*`（方块自动洗潜能按战斗力比较），
  装备提示框那一行本身没有 WZ 文案 —— 全是客户端算的。

**界面位置（用户给 282 实拍，2026-11-06 二次修正）**

不是正文行，而是**图标右侧顶部**：上行小灰字 `戰鬥力提升量`，下一行是大号数值
`+ 220萬 4312`（增亮白；减少是洋红 `#F00068`，对应 `minus/sign`），再下面是类别徽标
（`肩膀裝飾`）。**已穿着的装备**把数值那一行换成 `imgFont/atkPow/equipped` 那张 145×18
（8 字 × 18px = 「目前穿戴中的裝備」），所以 282 里没有「未穿戴的装备」这行字 ——
本工程原来那句 `未穿戴的装备` 是自己加的，已按 282 去掉。

数值写法是 `单位 + 空格 + 余数`：2,204,312 → `220萬 4312`（余数照抄、不补零、无千分位），
再上一档 `1亿 2345万 6789`；余数为 0 只留单位。

**本工程原状**：只有名牌 `NativeNameplate.PowerOf`（BeiDou DLL 的
`calculateNameplatePower`），提示框没有战力；金币用 `ToString("N0")`（113px 的框会被大额顶爆）；
全工程没有万/亿格式化。

**落地**

- `Assets/Scripts/NumberFormat.cs`：<1万 逐位带千分位；≥1万 → `X万 Y` / `X亿 Y万 Z` /
  `X兆 …` / `X京 …`（空格分单位，余数不补零，整数截断），阶梯与
  `StringPoolWz2/MasterDisciple/UI/NumberUnit1..4`（京/兆/億/萬）一致。
  只用于展示位置，交易/输入框仍用精确 `N0`。
- `Assets/Scripts/CombatPower.cs`：公式唯一出口
  `(str+dex+int+luk) × max(1, max(watk,matk)) + maxHp/10 + maxMp/20`，乘法溢出饱和到 long 上限；
  **口径不算临时 buff**（用户 2026-11-06 定）：一律走派生层
  `EquipLoadout.Apply(Profile.Attributes, 武器, 0)`，不读 PlayerCtrl 那份带 buff 的 Attributes。
- `TryPreview` 走**完整换装仿真**：`EquipLoadout.Apply(..., swapSlot, swapId, swapEntry)` 把该槽位
  换成悬停的那件重算整块面板 —— 套装条数、潜能 %、星火/灵魂、技能被动全部按「穿完之后」结算。
  配套改动：`EquipCatalog.Apply` 加 `overrideInstance`（背包件 position≥0，按原来的
  `position<0` 找实例会掉回模板、少掉卷轴/星力）；`WornSlots(swapSlot, swapId)` 顶替；
  `AddPotentialBonus` 跳过被换掉那件、补进悬停件；`NativeGrowthStats.Accumulate` 加
  swap 重载（不走缓存），`AccumulateEquipment` 拆出 `AccumulateWornItem` 复用。
  **旧实现（按两件差值手工加减）会把没找到的旧武器当成 0 再叠新武器，实战多算一倍**
  —— 实拍从 `+8,470` 修正到 `+3,157`（当前战力 5,412）。
- `Assets/Scripts/ItemStats.cs`：装备实例的「基础 + 星力 + 星火」三层属性，提示框属性行用；
  战斗力预览已改为整块重算，不再用它做差量。
- 绘制在 `ItemTooltip.DrawEquipment` 的 header 段：`iconY-1` 起 16px 放标签，
  `iconY+16` 起 30px 放数值，右对齐到 `width-margin`；数值字号按字符数退档
  （≤10 字 20px / ≤16 16px / ≤22 14px / 其余 12px，`ItemTooltip.PowerFontSize`），
  兆/京级不会顶出 209px 槽位。类别徽标仍在 `iconY+56`。
- 金币框（`GameWindows.cs` 113px）、名牌「战斗力：N」、称号试炼未达标提示改走 `Compact`。

**验证**：`unity-compile-check` 0/0；`TooltipLayoutChecks` 304 通过 / 0 失败；`TooltipPreview`
新增「换装自检」——把已穿着的那件临时当成背包件再预览一次，差量必须为 0（同件换同件），
本轮 1 件通过、3 件因 harness 的 Profile/背包不一致跳过；当前战力（不含 buff）5,412，
换永恒破甲剑 `+3,157`
（26 条数值缩写覆盖 0/个位/万/亿/兆/京/long.MaxValue/long.MinValue/负数 +
7 条战力公式含溢出饱和 + 7 条字号退档 + 原布局与星级断言）；
`TooltipPreview` 1280×720 实拍：未穿戴的武器显示 `战斗力提升量` + `+ 8,470`，
已穿戴的帽子只显示标签（无数值）+「当前穿戴中的装备」，与 282 两张实拍的位置一致。

## 282 原生套装信息面板（frame/set）布局（2026-11-06，新增）

**对象与身份**：TMS282 客户端 `~/Documents/mxd/MapleStory/Data/UI/UIToolTip.img`（结构）+ `Data/UI/_Canvas/UIToolTip.img`（像素），BMS 密钥；像素用 `wzpy.canvas.decode_canvas(node, region="BMS")` 独立解出，全部 format 2。它与 beidou `BeiDouSetItemCompat.dll` 自建的 236 宽 native layer **不是同一套 UI**。本工程 `tools/ms282/import_equipment_tooltip.py` 的 SECTIONS 至今只导 `frame/common` 等 9 组，**`frame/set` 与 `textIcon/set` 均未迁移**（`docs/tms282-skill/data/equipment-tooltip-import.json` 的 263 节点中零命中）。

**复现命令**：
- `python3 tools/ms282/dump_img_tree.py UI/UIToolTip.img --branch Item/Equip --depth 4`
- `python3 tools/ms282/dump_ui_layout.py UI/UIToolTip.img/Item/Equip/frame/set`
- `python3 tools/ms282/dump_ui_layout.py UI/UIToolTip.img/Item/Equip/textIcon/set`
- `python3 tools/ms282/dump_img_tree.py Etc/SetItemInfo.img --branch 504`

**已确定：框体** `Item/Equip/frame/set/{top,mid,btm,line,boxNormal,boxLucky}`：top 298×30、mid 298×1（纵向拉伸）、btm 298×12、line 298×3，origin x=149（水平中心锚点）；中心像素 top (49,58,67,242)、mid/btm (46,53,61,242)、line (54,66,73,243)，**顶栏无烘焙文字**。`boxNormal/{w,c,e}` = 8×14 / 1×14 / 8×14（w/e 复用 `frame/common/category`），`boxLucky/{w,c,e}` = 8×16 / 1×16 / 8×16；caps 像素 (143,172,204,51) 半透明蓝，是行高亮底框。对比装备卡 `frame/common` 宽 324。

**已确定：`Item/Equip/set` 坐标常量（逻辑像素，相对框体左上）**

| 键 | 值 | 作用 |
| --- | --- | --- |
| lineSpace / cashLineSpace | 15 / 50 | 普通装备行 / 现金道具行行距 |
| titleCanvasPos / titleNamePos / setCountPos | (14,20) / (46,19) / (260,19) | SET 徽标 / 套装名 / 已穿·完整件数 |
| itemPartPos / itemNamePos | (14,48) / (89,48) | 部件名 / 道具名，逐行 y = 48 + 15·i |
| cashItemIconPos / cashItemNamePos / cashItemPartPos | (13,48) / (60,48) / (283,48) | 现金道具行的图标·名称·部件 |
| setPartPos / setEffectPos | (14,10) / (89,10) | 效果档位标签 / 效果正文（相对偏移） |
| itemListBottomMargin / setEffectMargin / setListBottomMargin | 7 / 7 / 12 | 列表尾留白 / 档位间距 / 效果区尾留白 |
| jokerRelPos / canvasBoxMargin | (4,3) / 0 | jokerPossible 标记位移 / 高亮框边距 |

**已确定：文字图标** `Item/Equip/textIcon/set/{normal 23×11, guide 61×12, lucky 7×7}`，origin 均 (0,0)。实拍解码：normal = 灰底深字「SET」徽标；guide = 烘焙字「套組效果」+ 滑鼠捲動提示图标；lucky = 亮黄绿 (204,255,0) 四瓣方块（幸運物品标记）。

**已确定：字体** `Item/Equip/font/common` 无 set 专用条目，套装区复用通用表：itemName Dotum 14 粗 `FFFFFFFF`；normal 12 白、gray 12 `FFB7BFC5`、darkGray 12 `FF85919F`、emphasis 12 `FFFFCC00`、red 12 `FFFF8A18`（其余 12px 均为 MD摩利斯9）。

**已确定：文案** `String/StringTable.img` 的 `SID_TOOLTIP_SETLIST_EQUIPPED=裝備中`、`SID_TOOLTIP_SETLIST_NOT_EQUIPPED=未裝備`、`SID_TOOLTIP_SETLIST_NOT_EXIST=(無)`；`SID_TOOLTIP_LUCKY_ITEM_HELP` 为 lucky 变体说明文案。

**已确定：数据源** 客户端 `Data/Etc/SetItemInfo.img/<setId>`：`setItemName / completeCount / Effect/<件数>/{incPAD,incMAD,...} / Option / ItemID/<n>[typeName,representName] / Desc / jokerPossible`。例 504「航海師套裝(戰士)」7 件、Effect 档 2..7、`Desc.weapon=選擇一件航海師武器`；`Character/Cap/01004422.img/info/setItemID=504`，中文道具名在 `String/Eqp.img`（如 1004422=航海師劍士頭盔）。服务端同名 XML 在 `TMS282.5/data/wz282/Etc.wz/SetItemInfo.img.xml`（约 877 套），**未与客户端逐套比对**。

**lucky 变体**：`Item/Equip/lucky` 另有独立常量 `lineSpace 15 / setPos (14,49) / setListBottomMargin 7 / helpTextTopMargin 10 / helpTextBottomMargin 19 / titleNamePos (25,19) / titleCanvasPos (14,22) / bodyPartPos (264,19) / canvasBoxMargin 1`，配 16 高 `boxLucky` 与 `SID_TOOLTIP_LUCKY_ITEM_HELP`。

**已确定：EXE 直接引用（2026-11-06 更正）**。`MapleStory.exe`（186,193,888 B）`strings -a` 的 1665518–1665530 是一段连续的套装面板资源表，逐条命中：
`/textIcon/set/lucky`、`/textIcon/set/normal`、`/frame/set/boxNormal/{w,c,e}`、`/frame/set/boxLucky/{w,c,e}`、
`SID_TOOLTIP_SETLIST_EQUIPPED / NOT_EXIST / NOT_EQUIPPED`、`SID_TOOLTIP_LUCKY_ITEM_HELP`，紧邻格式串 `- %s`、` +%d%%`。
另在 1575043 命中 `Etc/SetItemInfo.img`、1575040 `completeCount`、1575054 `setItemName`，
1584161 `SID_TOOLTIP_SETITEM_CATEGORY`、1589444 `SID_TOOLTIP_SETEFFECT`、
1589438/1589449 `SID_TOOLTIP_JOKER_SET_ITEM_LIMIT` / `SID_TOOLTIP_JOKERITEM`。
**此前「EXE 未命中 frame/set」是假阴性**：那次 grep 用了 BRE 的 `\|` 而本机 `rtk grep` 走 ERE，模式整体不匹配。
复现：`strings -a MapleStory.exe | grep -nF -e 'frame/set' -e 'textIcon/set'`（UTF-16 侧 `strings -el` 无命中，这些路径以 ASCII 存放）。
⇒ 结论修正为：**客户端确实加载 frame/set 皮肤与 SET 状态文案**，但 EXE 里没有 `frame/set/top|mid|btm|line`、`textIcon/set/guide`、
`Item/Equip/set` 各常量键的字面路径（疑由通用 tooltip 框架按前缀拼接），**绘制函数地址与调用链仍未定位**。

**待核（不得写成确定结论）**：① 绘制函数地址 / 调用链已定位（见下一节），但「两框相对位置、狀態文案落点的具体 x/y、字色与达成态的逐分支对应、guide 图标确切摆放、`frame/set/top|mid|btm|line` 由通用框架拼接的方式」仍未逐指令坐实，属推断；② `setPartPos/setEffectPos` 的相对基准点未定；③ 现金道具行 50 行距与 `jokerRelPos` 的实际绘制未复现；④ `SID_TOOLTIP_SETEFFECT`、`SID_TOOLTIP_SETITEM_CATEGORY` 等值未在 `String/StringTable.img` 找到（可能在其它 String 表）；⑤ 客户端 SetItemInfo.img 与服务端 XML 未逐套比对（数量同为 877，字段结构已确认**不一致**，见下一节）。

**本工程迁移状态（2026-11-06 已迁移）**：`ItemTooltip.DrawSetColumns` 已从 beidou DLL 口径（宽 236、代码帧 `A0000040`、金色 `FFE137`）换成 282 `frame/set` 298 宽皮肤 + 三列坐标；装备详情卡仍用 `frame/common` 324。资源按 AGENTS 规则 11 转 GMS 编码 / ARGB4444，经 `tools/ms282/import_equipment_tooltip.py` 导入 `Assets/Resources/UI/EquipmentTooltip.bytes`（57 canvas / 12,916 B）。落地细节见本节末尾「实现落地」。

**本次产出**：`docs/preview/282-set-item-panel.png`，用原端 canvas 像素 + 实测坐标合成的还原示意图（含标注与证据边界）；合成脚本为临时文件 `/tmp/compose_282_set_panel.py`，未写入工程源码。

## 282 套装面板 EXE 绘制链实证（2026-11-06 补充逆向）

**二进制身份**：`~/Documents/mxd/MapleStory/MapleStory.exe`，PE32+ x86-64 GUI，186,193,888 B，ImageBase `0x140000000`，
EntryPoint RVA `0xC34E058`。节区：`.text` RVA `0x1000`/`0x8193AAA`（明文可执行）、`.rdata` `0x8195000`、
`.pdata` `0xA247000`（RUNTIME_FUNCTION 表，约 30 万条，可精确取函数边界）、`.data` `0x9A7E000`、
`.themida`（Raw=0，加壳）、`.boot`。⇒ **Themida 只包了启动层，主 `.text`/`.rdata`/`.pdata` 可直接静态反汇编**，
此前「64 位 themida 无法逆」的判断只适用于 `atkPow` 那条链，不适用于 UI 绘制链。

**两种字符串编码（关键）**：资源路径片段 `/frame/set/boxNormal/w` 等是 **ASCII**（`0x087A40A0–0x087A41D0` 连续表）；
WZ 属性键名 `/lineSpace`、`/itemPartPos`、`/titleNamePos`… 与字体名 `/normal`、`/gray` 是 **UTF-16LE**
（`0x087A3DF0–0x087A4068` 连续表）。铁律「EXE 节点名是 UTF-16，只搜 ASCII 会漏」在本模块再次成立：
上一轮 `strings -a` 搜不到布局键名即因此，而 `strings -el` 那次又只搜了完整路径模式，两边都漏。

**已确定：函数地址与调用链**（RVA，均由 `.pdata` 取边界）
| 地址 | 大小 | 作用 |
| --- | --- | --- |
| `0x00A38600` | `0x49B1` | `Etc/SetItemInfo.img` 数据装载；调用者 `0x009FDA3A` |
| `0x051941E0` | `0x14DC` | **顶层套装面板 draw**；`0x05194699`→lucky help、`0x05194730`/`0x0519479F`→装备列表 |
| `0x0518EF10` | `0x11AE` | lucky 变体说明（`SID_TOOLTIP_LUCKY_ITEM_HELP`），读 `titleCanvasPos`/`titleNamePos`/`canvasBoxMargin` |
| `0x051900D0` | `0x26B7` | **装备列表 + 三态状态文案**；`0x051907D9` EQUIPPED、`0x0519086E` NOT_EXIST、`0x051908D6` NOT_EQUIPPED；末尾 `0x05190993` 调件数 |
| `0x05192790` / `0x051956D0` | — | 效果区 / 现金道具变体，同样消费整套布局常量 |
| `0x05239280` | — | 件数 `"%d / %d"`（格式串 RVA `0x0843CAD8`）+ `boxNormal` 高亮框；用 `normalBold`/`grayBold` 两态 |
| `0x052384D0` / `0x05238640` | `0xB9` | `/textIcon/set/lucky`、`/textIcon/set/normal` 资源 getter |
| `0x05238700` / `0x05238970` | `0x264` | `boxNormal/{w,c,e}`、`boxLucky/{w,c,e}` 三个 canvas 的 getter |
| `0x05B90080` | — | 通用文字绘制（字体对象 + 文本对象 + x/y + style） |

**已确定：布局常量按名字读取，不是硬编码**。`0x05234xxx` 是一串 getter，每个 `lea r8,[rip+…]` 指向 UTF-16 键名，
再用 `call 0x140407240`（路径拼接）→ `0x1413af880`（读 int/vector）。完整映射：

| getter | 键 | 主消费者 |
| --- | --- | --- |
| `0x05234AE0` | `/lineSpace` ↔ `/cashLineSpace`（按参数二选一） | `0x051900D0`,`0x05192790`,`0x051956D0` |
| `0x05234C20` | `/itemPartPos` ↔ `/cashItemPartPos` | 同上 |
| `0x05234D60` | `/itemNamePos` ↔ `/cashItemNamePos` | 同上 |
| `0x05234EA0` | `/cashItemIconPos` | 同上 |
| `0x05234F50` / `0x05235000` | `/itemListBottomMargin` / `/setListBottomMargin` | 同上 |
| `0x052350B0` / `0x05235160` / `0x05235210` | `/setPartPos` / `/setEffectPos` / `/setEffectMargin` | `0x05192790`,`0x051956D0` |
| `0x052352C0` / `0x05235370` / `0x05235420` | `/titleNamePos` / `/titleCanvasPos` / `/setCountPos` | 列表 + 件数 |
| `0x05234D60` 等 | `/setListBottomMargin`、`/titleNamePos`、`/titleCanvasPos`、`/canvasBoxMargin` 亦被 `0x0518EF10` 直接读 | lucky 变体 |

**行坐标实证**：`0x05190422` `mov ecx,[rbp]`（= lineSpace）→ `imul ecx,ebx`（行号）→ `add ecx,[rbp+0x44]`（起始 y），
即 **y = 起始 y + lineSpace·i**，与 IMG 的 `lineSpace 15` / `itemPartPos (14,48)` 完全对应。
`0x05190730` 起是逐行循环，`cmp esi,[r12+0x2f0]` 控制件数；`0x0519075D jae 0x0519086E` 越界→`NOT_EXIST (無)`，
否则按状态取 `EQUIPPED 裝備中` / `NOT_EQUIPPED 未裝備`。`0x141303550` 是字符串表查表函数。

**已确定：字体态**。`0x05234020 /normal`、`0x05234110 /gray`、`0x05234200 /normalBold`、
`0x052342F0 /grayBold`、`0x052343E0 /darkGray` 是按 `font/common` 名字取的 getter；
前三者同时被 `0x051900D0`（列表）、`0x05192790`（效果区）、`0x051956D0`（现金变体）加载，
`normalBold`/`grayBold` 只被件数 `0x05239280` 用。⇒ 三态 normal/gray/darkGray 确实服务于套装行的达成状态，
件数另用粗体两态；**但逐分支（哪态对应 EQUIPPED / NOT_EQUIPPED / NOT_EXIST）未逐指令确认**。

**已确定：数据装载字段**。`0x00A38600` 的 UTF-16 字段簇 `0x08211680–0x08211838`：
`rdItemID / completeCount / parts / jokerPossible / zeroWeaponJokerPossible / magicGradeWz2 / expandToolTip /
representName / byGender / activeSkill`，配合 `0x08211720 Etc/SetItemInfo.img`、`0x082118A0 setItemName`、
`0x081C3700 Desc`、`0x081BF290 typeName`、`0x082117D8 representName`。ASCII 侧另有 `Etc/SetItemInfo.img`、
`setItemName`、`completeCount`（`strings -a` 1575040–1575054）。

**复现命令**（在 `MapleDash/` 下）：
- `strings -a MapleStory.exe | grep -nF -e 'frame/set' -e 'textIcon/set'` → ASCII 路径表
- `python3 /tmp/ms282_u16.py` 式脚本：按 UTF-16LE 扫 `0x087A3DF0–0x087A4068` 得键名簇
- `pefile` 读 `.pdata` RUNTIME_FUNCTION 取函数边界；`capstone` 反汇编 `0x051900D0` / `0x05234xxx`
- numpy 扫 `.text` 的 RIP-relative `LEA`（`REX 8D`，ModRM `(b&0xC7)==0x05`，`target = rva+7+disp32`）做 key→getter→caller 映射

**282 SetItemInfo 迁入约束（重要）**：客户端 `Data/Etc/SetItemInfo.img` 共 **877 套**（服务端 XML 也 877，但字段结构不一致，按 AGENTS 规则 8 以客户端为准）。
`ItemID` 有**两代结构**：
① 老式 249 套带 `typeName` / 230 套带 `representName`：`ItemID/<n>` 是 imgdir，含多个候选 itemId + `typeName`（如「（帽子）」）+ `representName`（如「暴風雪護甲」），
这两列正是面板 `itemPartPos` / `itemNamePos` 的直接数据源（例 set 1）；
② 新式（如 504 航海師套裝(戰士)）`ItemID/<n>` 是**纯 int**，无 typeName/representName，部件名/道具名须由 itemId 走代码映射 + `String/Eqp.img` 取 name。
另 548 套有 `parts`、118 套有 `Desc`、517 套有 `jokerPossible`。
⇒ 迁数据必须兼容两代 ItemID，否则现代套装的「部件名 / 道具名」两列会空。
工程现有 `AdminSetItem` catalog 仅 55 套（beidou），且被 `ComputeBonus`/`EquipLoadout` 用于**玩法结算**；
直接替换会改数值，建议显示层与结算层分离（待用户确认）。

## 282 套装 Effect 的 Option / activeSkill 链（2026-11-06 续）

**背景**：用户要求迁入 282 SetItemInfo 并全量替换结算数据。客户端 `Etc/SetItemInfo.img` 877 套 / 2081 个 Effect 档位，
共 24 种字段；工程 `AdminSetItem.SupportedStats` 是另一套口径，且无 `activeSkill`/`Option` 机制。
按 AGENTS 规则 9 先做逆向，本轮**未改业务代码**。

**已确定：`inc*` 属性字段与解析函数**。EXE UTF-16 簇 `0x0820974E–0x08209858`：
`incSTR incDEX incINT incLUK incMHP incMMP incMHPr incMMPr incPAD incMAD incPDD incMDD incACC incEVA incSpeed incJump incNBDR`，
末尾紧跟 `String/Skill.img`；簇首还有 `setItemID` / `allowOverlappedSet`（`0x08209646/9660`）。
`0x008EC030` 逐个按名字读这些字段并写入结构体（`[r14]` = incSTR、`[r14+4]` = incDEX …，取节点用 `0x14043cc10`、
转 int 用 `0x14043f560`），调用者 `0x00ADC7C0`（`0x305`，另有 `" "`/`"con"` 格式串，疑为 tooltip 侧）。
其它字段消费函数：`0x00A3DE30`（incPAD/incMAD/incCraft* Min/Max，调用者 `0x009F90A0`/`0x00A41A70`/`0x00A6BFA0`）、
`0x00A41A70`、`0x009EBF20`（incCr/incPQEXPr，调用者 `0x009F7700`）。
数据侧另有 `incAllStat`(650 档)、`nbdR`(37)、`incCr/incCDr`、`incPQEXPr`、`incPVPDamage`、`bonusByTime`、`OptionToMob`。

**已确定：`Option` 复用装备潜能（ItemOption）系统，不是套装私有机制**。
结构 `Effect/<档位>/Option/<n>/{option, level}`；样例 option 60023 / 60024、level 13。
`option` 补零到 6 位 → `Data/Item/ItemOption.img/060024`：
`info/string = "攻擊Boss怪物時傷害 +#incDAMr%"`、`level/13 = {boss:1, incDAMr:10, fixedGrade:1}`；
`060023`：`info/string = "無視怪物防禦率 +#ignoreTargetDEF%"`、`level/13 = {ignoreTargetDEF:10, fixedGrade:1}`。
EXE 的通用属性键表 `0x0825AA00–0x0825B9E0`（`inc*`/`indie*`/`ignoreTargetDEF`/`incDAMr`/`nbdR` 等 100+ 键）
与 `Item/ItemOption.img` 路径 `0x0825B978`、`Item/SkillOption` `0x0825B9E0` 同簇。
⇒ 显示取 `info/string` 模板 + `level/<n>` 数值；结算按该档的通用属性键并入角色属性。
**边界**：该键表通过查表/哈希消费（无 RIP-relative LEA 直引），Option 的**结算函数地址未定位**。

**已确定：`activeSkill` 是「套装档位授予技能」，技能 id 在 8000xxxx 段**。
结构 `Effect/<档位>/activeSkill/<n>/{id, level}`；827 个档位带此节点，level 多为 1。
样例：set 40 档 2 → 80001037 lv1、档 3 → 80001039 lv1；set 102 档 1/2/3 → 80000007/8/9 lv1。
`String/Skill.img/<id>` 给出名称与描述（80001037 = 「獨角獸比約恩騎乘技能」，`[最高等級 : 1]` +
`MP10消耗，防禦力10增加，移動速度120，跳躍力120`）；EXE 有格式串 `String/Skill.img/{}`。
装载点 `0x00A399B4`（在 `0x00A38600` 内）：`call 0x140a0ef20` 按名取 `activeSkill` 节点，
`0x00A399EA call 0x140a0f3f0` 遍历，`0x00A39A60–0x00A39AD2` 循环把条目写入套装定义对象 `+0x48` 容器
（含 `cmp rdx, 0x3e8` / `cmp rdx,-1` 校验）。数据装载器 `0x00A38600` 的调用者是 `0x009FD9E0`。
**边界**：**运行时「档位达成 → 授予 / 卸下移除」的触发链未逐指令定位**；数据链（字段、id、字符串表、装载函数）已完整。

**服务端对照**：`TMS282.5/data/wz282/Etc.wz/SetItemInfo.img.xml` 同为 877 套，Effect 档的
`activeSkill` / `Option` 子节点结构与客户端一致（set 40 两档 activeSkill、set 504 各档 Option）。
但 TMS282.5 只有 `bin/` 编译产物，无可读源码；按 AGENTS 规则 8 仅作参考，不以服务端为准。

**全量实现方案（2026-11-06 已落地）**：
1. 导出客户端 `Etc/SetItemInfo.img` 877 套为工程 JSON：两代 `ItemID` 结构、Effect 全 24 字段、
   `activeSkill` 列表、`Option` 列表、`Desc`、`jokerPossible` 等。
2. 导出被引用的 `Item/ItemOption.img/<6位>/{info/string, level/*}` 与 `String/Skill.img/<8000xxxx>` 名称/描述。
3. 建繁→简对照表，覆盖套装名、`representName`、`typeName`、Option 模板、技能名/描述。
4. `AdminSetItem` 字段映射：`incPAD→PAD`、`incMAD→MAD`、`incMHP→HP`、`incMMP→MP`、`incSTR/DEX/INT/LUK` 同名、
   `incAllStat→四属性`、`incMHPr/incMMPr→HPPct/MPPct`、`incSpeed/incJump→SPD/JMP`、`nbdR→IgnoreDefense`、
   `incACC/incEVA→ACC/EVA`、`incPDD/incMDD→PDD/MDD`；`Option` 展开为 ItemOption 通用键；`activeSkill` 新增授予接口。
5. 显示层按前述 282 面板结论改造（298 宽 / 保持分栏 / 简体格式 / 推断项标注）；守卫 `TooltipLayoutChecks` 同步。
**风险**：全量替换结算会把 877 套的 282 数值口径带进 `EquipLoadout`/`CombatRules`，需完整回归。

## 282 套装面板与 SetItemInfo 实现落地（2026-11-06，已写码）

**数据出口**：`tools/ms282/export_set_item_data.py` → `Assets/Resources/Admin/SetItem282.json`
（1,149,011 B；`setCount=877`；`options=26`；`skills=818`；`equipNames=5461`；`unmappedFields={'incPQEXPr':3}`）。
源仍是 282 客户端 IMG（AGENTS 规则 8），显示串用 `zhconv` 转简体，`*Tw` 保留原文供审计。

**字段映射（导出期一处收敛，可重跑审计）**
- Effect `inc*` → 工程键：`STAT_MAP`（新增 `incPDDr→PDDPct`、`incMDDr→MDDPct`）；
  `incCr→**CriticalChance**`（不是 CriticalRate，工程 PotentialStats/NativeBuffs 的规范键）。
- `incAllStat` 在导出期展开成 STR/DEX/INT/LUK 四条。
- Option 的 `incDAMr` 有歧义，**按 level 节点是否带 `boss:1` 判别**：带 → `BossDamage`，不带 → `Damage`
  （实证：60024/30601/603 带 boss:1；10070/22070/30070/40070 不带）。
- Option 其它键：`ignoreTargetDEF→IgnoreDefense`、`incCr→CriticalChance`、`incCriticaldamage→CriticalDamage`、
  `incAsrR→StatusRes`、`incSTRr/incDEXr/incINTr/incLUKr→STRPct/DEXPct/INTPct/LUKPct`；
  `boss / fixedGrade / attackType / prop / level / time / incAllskill / incAllStat` 不是属性，不透传。
- `incPQEXPr`（3 档）无对应机制，留在 `unmapped`，面板显示「组队任务经验获得 +N（未接入）」标红。

**结算（`AdminSetItem`）**
- 内置套装改读 `Admin/SetItem282`；旧的 55 套 `SetItemCatalog` 只在资源缺失时回退
  （`Using282Data` / `BuiltInSetCount` 暴露给守卫）。
- **计件口径改为 282 语义**：`AchievedDefs` 按「穿着件数中属于本套装的数量」计，重复戒指/同类武器各算一件
  （282 `completeCount=5` 但 `ItemID` 只有一个戒指 id 的套装因此可达 5 档）；旧 catalog 与后台
  `slotOverrides` 仍走「逐槽组一组只计一件」。`ComputeBonus` 与 `ActiveSkillsFor` 共用这一处，避免两套计数漂移。
- 档位累加语义不变（`count >= requiredCount` 的每一档都加）；`OptionStats` 与 `Stats` 一起并入，
  但后台 `bonusStore` 差量只作用于 `Stats`（Option 不提供差量编辑）。
- `SupportedStats` 增 5 键：`IgnoreDefense / CriticalChance / CriticalDamage / StatusRes / PvpDamage`，
  `IsRate` 同步认 `chance` / `ignoredefense`。新增 `CriticalChance` 目前只有套装与潜能两条来源，
  `EquipLoadout` 已把 `CriticalChance` 计入 `result.criticalChance`（潜能路径），套装路径走 `setItemBonus`，
  供 `CombatRules` 读取。
- 后台覆盖层（`set_item_bonus_overrides` / `..._customizations`）语义与接口完全不变；
  旧 11000 段的差量键在 282 id 空间里不会命中，等于自然失效。

**activeSkill 授予**
- `AdminSetItem.ActiveSkillsFor(wornIds, jobIndex)` → `List<GrantedSkill{Id,Level,Name,Desc}>`，同 id 取最高等级。
- `NativeSetItemSkills`（新文件）承接运行时表：`Refresh(wornIds, jobIndex)` 带 worn 签名缓存，
  未换装直接跳过；`IsSkill/LevelOf/NameOf` 供 UI 与技能系统查询。
- 触发点：`EquipLoadout.CurrentSetBonus()` 每次按当前穿着重算 —— 换装/进游戏都会走这条路，
  卸下装备后表里没有该技能即等价于移除，不写存档。
- `SkillProgress.BaseLevelOf` 在 `SkillBook.Get()` **之前**判 `NativeSetItemSkills.IsSkill(id)`，
  因为这些 8000xxxx 技能不在 GMS083 的 `Skill.wz` 树里（`SkillBook.Get` 去读 `Skill/8000.img` 会抛
  `FileNotFoundException`）。边界：EXE 的逐指令触发链仍未坐实，刷新时机由工程侧决定。

**显示层（`ItemTooltip`）**
- 常量与 `Item/Equip/set` 一一对应：`SetPanelWidth 298` / `SetPanelPitch 302` / `SetLineSpace 15` /
  `SetTitleH 30` / `SetBtmH 12` / `SetPartX 14` / `SetNameX 89` / `SetStatusRight 283` / `SetFirstRowY 48` /
  `SetItemBottomMargin 7` / `SetEffectMargin 7` / `SetListBottomMargin 12`。
- 行模型是**变高**的：正文行 15、分隔行 10（7 留白 + 3 的 `frame/set/line`）、档位间距行 7。
  切栏按累计高度 `SplitSetColumns(rows, maxHeight)`，不能再按行数均分 ——
  早期按行数均分把 `line` 画到了最后一行装备上（2026-11-06 实拍发现并修正）。
- 三列：`（部件）` x=14 / 道具名 x=89 / 状态右对齐 283。状态文案简体「装备中 / 未装备 / (无)」；
  悬停件名 `#FFCC00`、已穿白、未穿 `#B7BFC5`、不存在 `#85919F`；达成档 `[N 件]` 金、未达成 `#85919F`。
- 顶栏：`textIcon/set/normal` 23×11 放在 `titleCanvasPos` 附近（按 30px 栏垂直居中）、套装名 14px 粗白、
  件数 `"%d / %d"` 右对齐 283 金色、`jokerPossible` 时在套装名后画 `textIcon/set/lucky` 7×7。
- 效果区标题：画 `textIcon/set/guide` 61×12，再把左 48px（烘焙的繁体「套組效果」）盖成框体底色
  `(46,53,61)` 写简体「套装效果」，右侧 `x=52..61` 的滑鼠捲动图标保留原像素。
- 行高亮：悬停行画 `frame/set/boxNormal/{w,c,e}`（8/拉伸/8，14 高）；`jokerPossible` 套装用 `boxLucky`（16 高）。
- 多栏拆分保留（用户决策「保持拆开」），每栏重画顶栏，跨栏不丢 SET 徽标与件数。

**守卫与验证**
- `TooltipLayoutChecks`：装备卡按真实 324、套装按 298 重做落点断言；
  **298+324 在 611 宽屏上几何上摆不下两栏**，所以「单侧放得下 ⇒ 零重叠 + 完整在屏内」，
  「两侧都放不下 ⇒ 只要求夹进屏幕、顶边可见」，不再强求零重叠（旧 236 宽度下不存在这个窗口）。
- 新增 282 数据断言：877 套 / set 504 名称简体「航海师套装(战士)」/ 件数 6-7 /
  Option 文案简体 / `EquipName282` 命中 / boss:1→BossDamage 与 非 boss→Damage 双向 /
  activeSkill 授予与卸下移除 / `BaseLevelOf` 接线。
  最近一次：`[Tooltip] 布局断言 317 通过 / 0 失败`（`unity_exit=0`）。
- 编译：`cd MapleDash/tools/unity-compile-check && dotnet build Check.csproj --no-incremental` → 0 error 0 warning。
- 合成图：`docs/preview/282-set-item-panel-implemented.png`（按已落地几何 + 真实 canvas + 真实数据重绘，
  脚本 `/tmp/compose_282_set_panel_impl.py`，未入库）；首版合成图 `docs/preview/282-set-item-panel.png` 保留为分析快照。

**仍未坐实（不得写成确定结论）**
① 现代 `ItemID` 的槽位分组函数未定位，按 id 前缀推断；27 套的 `required` 高于推断出的槽组数
（`completeCount 5 / slots 1` 的戒指套等），显示上靠「补 `(无)` 行 + 重复末槽」兜，结算靠多件计件；
② `jokerPossible` / `zeroWeaponJokerPossible` 的「替代件」机制未实现，只画 `lucky` 标记；
③ `Option` 的结算函数地址未定位；④ `activeSkill` 的运行时增删触发链未逐指令定位；
⑤ 状态文案 x/y、达成态字色逐分支、guide 精确摆放、两框相对位置仍属推断。

