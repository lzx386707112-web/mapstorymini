# 现金商城

核实快照：2026-10-26。原端身份见 [binary-and-tools.md](binary-and-tools.md)。
原端逻辑的确定程度与运行时迁移状态分开记录；“尚未迁移”不代表需要重新逆向已确认部分。

## 商城场景与资源布局

原背景 `UI/CashShop.img/Base/backgrnd{,1,2}` 均800×600，包含窗体边框。
`0x468FCC..0x468FEF`：有符号 job/1000==1 选 backgrnd1；`0x4690D9..0x46912B`：job/100==21 或 job==2000 选 backgrnd2；其余用 backgrnd。
商城子窗口由 `0x469410` 起调用 `CWnd::CreateWnd (0x9DE4D2)`：

| 成员偏移 | x,y | width,height |
| --- | --- | --- |
| +0x554 | 0,0 | 256,316 |
| +0x9A4 | −1,318 | 256,104 |
| +0xF40 | 0,426 | 246,163 |
| +0x150C | 272,17 | 508,78 |
| +0x15BC（CSList） | 275,95 | 412,430 |
| +0x1D5C | 690,157 | 90,358 |
| +0x1DE0 | 254,530 | 545,56 |

## 商品格内的文字与图标（2026-10-26 按汇编核实）

CSList 绘制函数入口 `0x4B67FB`（`push ebp;mov ebp,esp`）。格内 x/y 来自 `0x4B965F` 写入的 `[ebp-0x4C]`(=格子x)/`[ebp-0x48]`(=格子y)。

- 名称：`0x4B814B` 先 `push 0x70`(最大宽度112) 构造文本，`0x4B81CC..0x4B81E1` 取 `格子y+6`、`格子x+0x52(82)` 调 `0x414576`→`0x4277A?` 绘制。宽 112 是原端固定值，不要按剩余框宽自适应。
- 名称来源：`0x4B80E9` 起按 itemId 取道具名，`0x460358` 判定为宠物时用 string574 `"[PET]%s"` 加前缀（`0x4B810C` 引用 574）。宠物名不在 `String/Cash.img`，在 `String/Pet.img/<id>/name`。
- 价格：`0x4B8A5A` 取 `[commodity+0x20]` 调 `0x988690`。该函数为「每 3 位插逗号」格式化器：倒序取位、`idiv 3` 后 `edx==2` 时插入字符 `0x2C`，格式串是 string5515 `"%d"`。**价格不是直接打印整数**。
- 货币后缀：格式化结果后面拼 string571 `" NX"`；若 `[commodity+0xc]` 落在 `[0x4C4B400,0x55D4A80)`（8000万~9000万，原端用于金币计价商品）则改用 string5639 `"meso"`（`0x4B8A71..0x4B8AC1`）。运行时中文版对应「点券」/「金币」。
- 数量：`0x4B8B1A` 判 `[commodity+0x1c] > 1` 时用 string572 `"(%d)"` 追加到价格串末尾。
- 图标：`0x4B8066`(普通商品)/`0x4B7FDF`(itemId/100000==91 的 910xxxx 礼包) 两处把锚点算成 `格子y+0x49(73)`、`格子x+7`。白框在 Base 上是 68×70，正好容纳 2 倍点阵的 32px 图标（CSList/ItemIcon 亦为 72×72），因此图标按 **2 倍最近邻**贴图、左下角对齐 (7,73)，不是 1 倍。
- 图标取图：普通道具 `Item/<类>/<前四位>.img` 的节点名是 8 位补零（如 `02000000`）；`Item/Pet/<7位id>.img` 的节点就是根；910xxxx 礼包节点名是原样 7 位且 `name`/`icon` 直接挂在节点下（不是 `info/icon`）。任一路径写成统一补零会取不到名称与图标。

CSList 构造函数 `0x4B6CBE`；`0x468ABB` 属于商城场景装配，不能混作商品窗口构造函数。构造函数里另有 `0x4D938E/0x4D9481` 文字选择器（页码条，`0x4B6D0B..0x4B6D34` 传 1000/4/412/406 与颜色 `0xFF3A67AA`），不属于商品格。
`0x4B965F`：每页10格，两列五行；格子 local x=(index%2)*206，y=(index/2)*81+2，200×80。
`0x4BA6C9`：购买按钮 local(格子x+80,格子y+53)。
素材 CSList/Base 200×80、ItemIcon72×72，购买/赠送/收藏/移除按钮37×19。
商品列表已迁移为原生两列五行、10件分页，名称(82,6)宽112、价格(82,31)（千分位+货币后缀+数量）、图标锚点(7,73)按2倍点阵，按钮(80/119/158,53)。运行时外围 GUILayout 已删除，整体使用原始800×600场景，等比适配并留黑边；GameHUD 在商城期间不再绘制地图 HUD。仓库、背包、余额及赠送的可见框体均走原始 IMG。交互完成程度见末节。

## 已定位的其他控件

原端场景装配 `0x469410` 关联的子窗口布局继续使用上表，不要把素材铺进任意大小的 GUILayout 容器后声称原版布局完成。

- CSChar：BuyAvatar(16,237)、Default(101,237)、Takeoff(187,237)，坐标属于角色预览窗。
- 背包扩充按钮 x176，y27/54/81/108/135，坐标属于背包窗。
- 余额栏按钮以场景全局坐标记录：Charge(502,543)、Check(543,543)、Coupon(584,543)，均41×41；Exit(632,545)，168×49。
- CSGift/backgrnd=266×169、backgrnd1=473×169；Buddy/Guild/Hide 是相关按钮，不能用任意通用赠送表单替代完整布局分析。

分类点击函数 `0x4B6B3B` 使用 y22..52。宽51候选区的起点表 `0xBD8ACC` 为：
`57,69,107,119,157,169,209,220,260,272,311,323,362,374,3,3,451,451`。
表索引 `2*(candidate-1)+(candidate<selected ? 0 : 1)`；主分类画布为 CSTab/Tab/1..9，508×78。

## 主分类与文字选择器（已核实）

- `0x4B684B/0x4B6898`：主分类1..7对应 `CSTab/Tab/2..8`；8首页和10搜索用Tab/1；9购物车用Tab/9。画布复制local(2,0)，即scene(274,17)。不能直接拿category作为画布编号。
- `0x46CAFE` 主分类改scene+52c，调用0x4B694B与0x4BC2C4，子分类归0；0x46CB2E子分类切换后通过0x46CB5E页码归0。
- `Etc/Category.img` 的Category、CategorySub、Name决定子分类文字；SN/10000000为主分类，SN/100000%100为子分类。
- `0x471B77/0x471BB5`分别查主分类起始/数量与子分类匹配。不要把服务端注释里猜测的Priority排序当成客户端已确定排序。
- 子分类控件`0x4B694B` local(8,59),width485，每组最多20项；页码控件`0x4B6CBE/0x4B96B5` local(4,412),width406，每组最多10页，页码从1开始，每页10商品。
- **纠正**：此控件是横向文字选择器，不是CCtrlComboBox。构造0x4D938E，Init0x4D9481，vtable0xAF2F10，height14。
- `0x4D96FA/0x4D9D9F`：FillRect背景、glyph绘制y-1；字符串宽度相加后居中；分隔符string2771=" |"；前组string2770="◀"(hit=-2)，后组string2772=" ▶"(hit=-1)。
- 0x4D9569颜色三元组(background,normal,selected)，0xBD8B14主分类1..9：
  `(-7353517,black,white),(-1874292,black,white),(-12338980,black,white),(-1664967,black,white),(-11711023,white,black),(-3381933,white,black),(-7127089,white,black),(-12346660,black,white),(-2668626,white,black)`。
- 页码背景FF3A67AA，普通白色，选中黑色。运行时按此规则代码绘制文字条，属于原端已核实代码绘制例外。

## 仓库、背包与余额（已核实）

- Locker Draw0x4B0116、rect0x4B0877：12格，6列2行；local(21+35col,30+35row),32²，scene首格(20,348)，滚动偏移row*6。
- Inventory Draw0x4B1989、rect0x4B1E69：12格，4列3行；local(22+35col,55+35row),32²，scene首格(22,481)，滚动偏移row*4+1（背包槽1起算）。
- 图标以格子left,bottom为基线；选中FillRect=401030CC，边框AA1030CC，各边2px并偏-1。
- 仓库pointer0x4B053B：WM_LBUTTONDOWN(201)选中、WM_LBUTTONDBLCLK(203)取出；移动端短间隔二次点击为适配，不能把单击直接取出称为原版。
- Inventory0x4B1173还包含分类Tab和滚动逻辑，当前未完整迁移。
- 余额Draw0x4BC713：local(120,11/25/40,107,11)清FFF3F4F5；文字右边界220，y9/23/38。场景右边界474。

## 预览（已核实与待核实分开）

- 0x4AB0B0预览canvas local(24,40),212×165，背景Base/Preview/0..2，由0x4ABDCD改变背景索引。
- 0x4AC31E点击preview rect(24,40,236,205)或header(95,5,125,30)切换预览启用状态。**纠正**：点击背景不等于轮换三张背景。
- 0x4AC396：购买穿搭->0x4696B1；默认->0x46BDCB->0x4AC7A5恢复原始装备快照；脱下->0x46BDD6->0x4AC6D6清52个装备槽（index0保留）。
- 0x4AC024：角色名右对齐local225,y268；账号右对齐225,y287。
- 预览actor初始0x9785BD坐标(-350,-250)；CashShopPreview.img foothold/1/0/1为(-380,-125)..(-160,-125)，出生portal0=(-240,-60)。**待核实**：最终精确人物脚底变换和预览模式/背景控件，目前不可把近似脚底映射写成已确定。
- **试穿会走完整纸娃娃合成**：商品格点/双击只写 `previewItem`，下一帧 `DrawPreview` 用它换帽/衣等槽位再合成。因此「某个商品一试穿预览就整块空白」通常不是商城布局问题，而是该道具 IMG 合成失败——例如 `Character/Cap/01002470.img`（电焊工面具）的悬空 UOL 曾让 `Placements` 抛异常，详见 [渲染与验证](rendering-and-validation.md) 的「悬空 UOL」一节。排查顺序：先单独合成该 itemId，再怀疑 UI。

### 试穿道具画到角色身后（2026-10-26 核实并修复）

- 现象：点/双击商城商品试穿，帽子（cap 槽）被画在 body/head/hair 之下，只从头部两侧露出一点。
- 根因链（离线复刻合成算法 + 源端 wzpy 对照确认）：
  1. 端口 `WzCharacter.Z` 套用 `overCapRemap` 把 `cap` 槽重映射为 `capBelowHairOverHead`
     （源端 wzpy/character.py:453 `_OVER_CAP_REMAP`，原本还带 vslot token 条件和 `cap_covers_hair` 判定，端口简化为无条件映射——源端在 cap 不遮头发时也是同样映射，此步不算错）；
  2. `capBelowHairOverHead` **不在 zOrder 表**（`_DEFAULT_ZMAP` 183 项里没有）；
  3. 端口 `BaseZ` 对未知槽位兜底 `names.Count/2 = 91`，落在 body(85)/pants(90) 之上、head(123)/face(127)/hair(136) 之下 → 帽子被身体和头盖住。
- 源端权威实现是 wzpy `_z_index` + `_heuristic_anchor`（character.py:1047/1062）：未知槽位按名字推断——
  `cap*`且含`Below` → `index("cap")-1`；`…Below…` → `index(target)-1`；`…Over…` → `index(target)+1`；其余 → `n-1`（最前）；
  `back*` 分支端口本来就照搬了。端口却写成了"放正中间"，这是移植时的自创兜底。
- 受影响槽位实测：商城 1579 件上架装备里，`capBelowBody` 4 件 + remap 出的 `capBelowHairOverHead`（所有帽子）；
  另有 137 处 `back*/…BelowBody` 槽位是**故意的**背身层（披风/翅膀等），不要动。
- 修复：`WzCharacter.BaseZ` 兜底改为源端 `_heuristic_anchor` 四条规则；`Category` 补 169/170→Weapon
  （此前预览 170xxxx 会抛 NotSupportedException 打断 OnGUI）；`NativeCashWindow.DrawPreview` 合成包 try/catch。
- 验证方法：离线用 wzpy 复刻 Placements/Compose 渲 PNG 对照（cap 01002140 修复前帽体完全被盖、修复后正确戴头上）。

## 购买确认（2026-10-26新增）

- CSList按钮分派0x4B77B4：id2000+cell*3+op；op0->0x4731A9(itemID,SN)，op1赠送->0x46E822，op2收藏->0x470D7D/0x470EDD。
- 0x4731A9是商品类型分派，普通商品->0x46DADD；特殊分支仍需按各函数分析，不能全部视为普通购买。
- 0x46DADD根据余额与价格形成可用货币mask：credit1、points2、prepaid4；免费默认points2；无可用余额显示string535。部分特殊商品强制prepaid4，其余额外商品限制留待迁移。
- 确认调用链0x46E08E->0x469D40->0x469E5F，确认文字string506="Are you sure you want to buy this item?"；取消不发送购买包。
- 0x469D40确认后只允许1/2/4其中一个值，多选提示只选一种。运行时用单选状态避免多选。
- 0x46B537：width286，mode0 height=156+40*commodityCount；单商品H196，中心((800-286)/2,(600-196)/2)=(257,202)。mode1=175+40n、mode2=194+40n、mode3=170，不能混套。
- 绘制0x46ABD2：`UI/CashShop.img/CSNotice/0/{0..4}`。单商品各段y=0,21,61,66,85，尺寸分别286×21/40/5/19/104。它不是通用UtilDlgEx，也不是13px CashItem现金标记。
- 0x46A4B6：单商品图标基线(21,57)，名(63,27)，价格(63,43)，确认文字中心x143,y73。
- 0x469FDF：points/prepaid/credit依序checkbox x25,y=height-95/-80/-65；宽参数首项160、后两项230，height14。单一可用mask默认勾选；不可用者禁用。按钮BtOK2(157,height-37)、BtCancel2(207,height-37)。
- **当前迁移**：普通购买已接原始CSNotice分段、货币选择和取消；特殊商品完整分派/警告、批量购买尚未接完。服务器仍是交易规则最终校验方。

## 赠送（已核实）

- 正确Gift字符串4880..4884的使用点位于0x7E2874、0x7E2905、0x7E29C4、0x7E2483、0x7E2466；旧的DWORD全文件匹配有误命中，不能拿0x4A05xx当gift。
- 0x7E23A9：mode0为266×169 `CSGift/backgrnd`；mode1/2为473×169 `backgrnd1`。
- 0x7E2717 collapsed控件local：收件人(84,52,122,13)，留言两行(26,76,210,13)、(26,92,210,13)，参数0x22限制34字符；Guild(56,139,53,18)、Buddy(110,139,53,18)，OK2(168,139,47,19)、Cancel2(210,139,47,19)；expanded Hide(439,139)。
- 生日验证是独立对话框0x9ACF95，不可硬编码生日。其文本string826，创建CUtilDlgEx 0x9A31AD，设置0x9AC552(type3)，输入限制0x9AC7CB(8,8,1,0)，0x9A3E38定位、0x4EDBA1模态、取消返回-1。
- 0x9A3E38根据当前全局对话框中心BE2DF0/BE2DF4定位并夹在800×600；它并非无条件屏幕居中。布局继续见下条。

### 生日输入控件补充（已核实）

- 0x9AC552准确参数布局由汇编核实：+8=type3，+C=0，+10=text，+14=1(compact)，+18=0。旧伪代码的a0/a1签名有误，不能据此误取大框。
- 0x9A3CAE compact宽266；0x9A3D07 compact type3文字宽341-116=225；0x9A3DF6返回type3的2px度量参数。
- 0x9ACD54 compact：text origin x20,y22，minHeight0，height=textHeight+99；0x9A4F1D compact背景为Basic.img/Notice3/t(266×21)，c(266×20)自y21步20铺到height-78，Basic.img/Notice4/s(266×78)贴底。**纠正**：生日输入不是大版529px UtilDlgEx t/c/s。
- 0x9A6370 type2/3且非特殊a4路径到0x9A83F6；原端输入边框黑矩形(19,textHeight+24,229,18)，内白矩形(20,textHeight+25,227,16)，edit(22,textHeight+27,224,14)。属于核实代码绘制，禁止换自制皮肤。
- OK使用UIWindow.img/UtilDlgEx/BtOK，local(width-54,height-26)；Close local(9,height-26)，string2263/2265、父路径2252。当前font替代导致文本度量由Unity字体计算，是移植边界。
- 运行时已移除硬编码生日，Gift确认只打开上述8位输入，验证并确认后才调用交易；取消输入返回赠送框。原端全局对话框中心夹取与当前屏幕居中不同，属于尚需核对的定位差异。
- 分析陷阱：angr对0x99DD54的函数边界恢复错误，会包含相邻的大段文本解析，不可把整段伪代码当成函数本体；以真实call/ret和汇编为准。

## 商品数据来源链路（2026-10-26 按 exe / IMG / 服务端 Java / 本机 MySQL 核实）

**结论：商品目录本体在客户端 IMG，数据库是逐字段覆盖层，不是商品全量表。**

- 客户端自己加载 `Etc/Commodity.img`（string 2709；另有 `Etc/CashPackage.img` 2865、`Etc/Category.img` 1385 与报错串 "MapleStory Error : Loading commodity failed."）。字符串以**立即数 id** 传给 `0x79e805`（`push 0x23e` = string 574），不是地址引用，因此 `--xref` 查不到，要搜 `68 <id 小端>`。
- 解析函数在 `0x47FFA2` 起，逐字段 `push id → 0x79e805 → std::string → CData::GetItem → 转 int → 存入记录`，记录偏移实测：
  `+0x1c Count`、`+0x20 Price`、`+0x24 (Bonus 位)`、`+0x28 Priority`、`+0x2c Period`、`+0x30 MaplePoint`、`+0x34 Meso`、`+0x38 Premium`、`+0x3c reqLev`、`+0x40 Gender`、`+0x44 OnSale`、`+0x48 Class`、`+0x4c Limit`、`+0x50 PbCash`、`+0x54 PbPoint`、`+0x58 PbGift`。
- 本版 `Etc/Commodity.img` 实际只有 **13 个字段**：Count/ItemId/SN/OnSale/Period/Gender/Price/Priority/Class(1750 行)/PbCash/PbGift/PbPoint(229 行)/Limit(92 行)。**没有** ForPremiumUser、Meso、MaplePoint、Bonus、PackageSN——客户端照样读（取默认 0），移植时也按"读不到即 0"处理，不要凭 Java DO 的 17 个字段去 IMG 里找它们。
- 服务端 `CashShop.CashItemFactory.loadAllCashItems()` 把同一份 IMG 读成 `items`（全量），`loadAllModifiedCashItems()` 读 DB 表 `modified_cash_item` 成 `modifiedCashItems`；`getItem(sn)` = `items` 的 clone + DB 行逐字段 `Optional.ofNullable(db).orElse(wz)`。
- `PacketCreator.openCashShop`(0x…7305) **只下发 `getModifiedCashItems()`（DB 行）**，不下发 WZ 全量 —— 正是因为客户端本地已有全表，下发的是覆盖/补丁。这解释了为什么本机 `beidou` 库 `modified_cash_item` 为 0 行时原端商城依然满屏。
- 本机 MySQL（`root/root`，库 `beidou`，application.yml 的 jdbc 串）实测：`modified_cash_item` **0 行**；`specialcashitems` 仅 1 行 (sn=10000617, modifier=1024, info=1)；`shops`/`shopitems` 是 NPC 商店，与现金商城无关。迁移里 `V2.1.101/102/105` 对该表按 `item_id` 做 DELETE，说明它被当作商品表维护，但**没有**任何 INSERT 种子数据。
- 端口对应实现：`NativeCashService.ImportCommodities()` 首次把 WZ 里「完整且上架」的行写进 `modified_cash_item`（2077 行，剔除 4 条残缺），之后 `Commodity(sn)` 与商城列表一律以表为准，语义等同 `getItem()`；改价/上下架改表即可。覆盖写法遵循源端 `changeOnSale` 的**先 deleteById 再 insertSelective**，否则同 sn 会插入重复行。

## 当前迁移与验证边界

运行时 `Assets/Scripts/NativeCashWindow.cs` 已删除旧GUILayout商城：原端场景、分类画布、文字选择器、商品格、仓库/背包格、余额、预览按钮、普通购买确认和collapsed赠送均引用IMG或已核实原端绘制。
尚未完整迁移：生日输入精确字体度量/原端全局中心定位、好友/家族赠送列表、购买穿搭、背包完整分类/全部物品、排行商品数据（scene(690,157,90,358) 右侧热销榜窗）、商品格 CSEffect(new/sale/hot/event/time) 顶部效果条、特殊商品分派/提示、领取赠礼/扩角色入口、充值与优惠券外部服务。原始按钮未接通者不得宣称已完成。
虚拟操作层仍按用户要求位于最上方，属于移动适配，并非商城IMG。

商品名缺失（2026-10-26 二次核实，已推翻前一条结论）：`Etc/Commodity.img` 共 9073 行、`OnSale=1` 2081 行，其中只有 4 条取不到名字——SN 21000126~21000129 / itemId 1702800、1702799、1702794、1702793。它们在 `Commodity.img` 里排在最后（节点 9072~9075，Price 4500），是后来追加的自定义 cash 武器；`Character/Weapon/01702793.img` 等美术文件**是存在的**（`cash=1`、`info/icon` 齐全），缺的只有 `String/*.img` 里的名字条目（全库深搜 0 命中）。因此不是路径 bug，也不能伪造名字；按源端数据现状这 4 条属于残缺商品，不进商城列表。

装备目录映射（2026-10-26 按 clien/Data/Character 实际文件分布核实，勿再凭记忆写）：
Cap=100、Coat=104、Longcoat=105、Pants=106、Shoes=107、Glove=108、Shield=109、Cape=110、Ring=111、
Accessory=101/102/103/112~119/167、Weapon=130~151/160/169/170（170 有 1040 个文件，商城里 259 件点装武器全是 170xxxx）、
PetEquip=180~183、TamingMob=190/191/193/198/199、Android=167。旧映射漏掉 169/170 与 180~183，导致 259+55 件商品取不到图标。

复现分析（使用binary-and-tools.md的capstone环境）：
`python tools/inspect_beidou_client.py <BeiDou.exe> --start 0x4731A9 --end 0x473297`；购买对话框继续查0x469FDF、0x46B537、0x46ABD2、0x46A4B6。
Unity检查入口 `NativeCashSceneChecks.Run`；只用内存商品/余额fixture，不进行实际购买/赠送，截图与命中/取消不代表真机或全部交易通过。最终截图与最新检查结果见本次验证日志；不要继承旧版本PASS。

2026-10-26 本次验证：dotnet编译0 warning/0 error；独立Unity工程副本通过800×600、1280×720场景/购买/赠送截图及分类命中、购买与赠送取消检查，未执行真实交易。检查通过SessionState抑制GameLoop自动启动，fixture不启动玩家/服务器；截图无移动操作覆盖层，不能用它宣称虚拟按键集成通过。

最终增量验证：`/tmp/mapledash-cash-review-final.log`包含NativeCashSceneChecks PASS；两种分辨率均断言赠送OK进入生日输入、未请求交易。截图保存于 `docs/hud-review/cash-{scene,buy,gift,birthday}-{800,1280}.png`。以上是隔离内存fixture，不是APK验收。
