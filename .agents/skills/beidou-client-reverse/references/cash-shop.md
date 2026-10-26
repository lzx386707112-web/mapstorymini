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

CSList 构造函数 `0x4B6CBE`；`0x468ABB` 属于商城场景装配，不能混作商品窗口构造函数。
`0x4B965F`：每页10格，两列五行；格子 local x=(index%2)*206，y=(index/2)*81+2，200×80。
`0x4BA6C9`：购买按钮 local(格子x+80,格子y+53)。
素材 CSList/Base 200×80、ItemIcon72×72，购买/赠送/收藏/移除按钮37×19。
商品列表已迁移为原生两列五行、10件分页，名称(82,6)、价格(82,31)、图标基线(7,73)，按钮(80/119/158,53)。运行时外围 GUILayout 已删除，整体使用原始800×600场景，等比适配并留黑边；GameHUD 在商城期间不再绘制地图 HUD。仓库、背包、余额及赠送的可见框体均走原始 IMG。交互完成程度见末节。

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

## 当前迁移与验证边界

运行时 `Assets/Scripts/NativeCashWindow.cs` 已删除旧GUILayout商城：原端场景、分类画布、文字选择器、商品格、仓库/背包格、余额、预览按钮、普通购买确认和collapsed赠送均引用IMG或已核实原端绘制。
尚未完整迁移：生日输入精确字体度量/原端全局中心定位、好友/家族赠送列表、购买穿搭、背包完整分类/全部物品、排行商品数据、特殊商品分派/提示、领取赠礼/扩角色入口、充值与优惠券外部服务。原始按钮未接通者不得宣称已完成。
虚拟操作层仍按用户要求位于最上方，属于移动适配，并非商城IMG。

复现分析（使用binary-and-tools.md的capstone环境）：
`python tools/inspect_beidou_client.py <BeiDou.exe> --start 0x4731A9 --end 0x473297`；购买对话框继续查0x469FDF、0x46B537、0x46ABD2、0x46A4B6。
Unity检查入口 `NativeCashSceneChecks.Run`；只用内存商品/余额fixture，不进行实际购买/赠送，截图与命中/取消不代表真机或全部交易通过。最终截图与最新检查结果见本次验证日志；不要继承旧版本PASS。

2026-10-26 本次验证：dotnet编译0 warning/0 error；独立Unity工程副本通过800×600、1280×720场景/购买/赠送截图及分类命中、购买与赠送取消检查，未执行真实交易。检查通过SessionState抑制GameLoop自动启动，fixture不启动玩家/服务器；截图无移动操作覆盖层，不能用它宣称虚拟按键集成通过。

最终增量验证：`/tmp/mapledash-cash-review-final.log`包含NativeCashSceneChecks PASS；两种分辨率均断言赠送OK进入生日输入、未请求交易。截图保存于 `docs/hud-review/cash-{scene,buy,gift,birthday}-{800,1280}.png`。以上是隔离内存fixture，不是APK验收。
