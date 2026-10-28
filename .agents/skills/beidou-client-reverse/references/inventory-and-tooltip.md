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
