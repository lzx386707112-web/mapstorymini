# NPC 普通商店全量排查（2026-10-29）

初次排查仅作静态扫描；后续修复与验证见文末。静态扫描不是实际点击/画面验收。

## 原端商店分派

服务端 `src/main/java/org/gms/net/server/channel/handlers/NPCTalkHandler.java`：先调用
`NPCScriptManager.start`；没有脚本时检查 `npc.hasShop()`，再 `npc.sendShop(c)`。
无脚本的普通店主是正常数据，不能补造 JS。`server/life/NPC.java` →
`ShopFactory.getShopForNPC` → `Shop.createFromDB(id,false)` 以 shops.npcid 查店，
商品按 shopitems.shopid、position DESC 读取。
`NPCConversationManager.openShopNPC(id)` 参数是 shopid；原端找不到时回落 11000。

`V1.0.56__shops_insert_data.sql` 共110行、102个不同NPC。11000对应11000/1337；
9001002对应9999992..9999999。不得用npc→单个shopid覆盖这些记录；原查询未指定ORDER，
多店查询不能强造确定顺序。SQL及商品SQL的SHA256保存在 `docs/npc-shop-audit.json/sources`。
继续按版本执行所有修改shops的迁移（1.0.66、1.7.2、1.7.3、2.1.14/15/16）后，
最终为121行、113个不同NPC。上面110/102仅为1.0.56基础表，不是最终全量。

## NPC深度原端证据

`clien/BeiDou.exe` SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，ImageBase `0x400000`。
`0x6D267D..0x6D271E` 取NPC+0x90的运动对象接口，减0xC得到运动对象this；
读movement+0x130/+0x134，计算 `10*(movement[0x130]*3000-movement[0x134])-0x3FFF8AD5`，
通过NPC+0xE4主层的vtable+0xB4写z。即相同运动基准K下NPC为K+5，见rendering-and-validation.md。
`0x9B1381..0x9B1395` 接地分支将当前foothold+0x1C/+0x20写入movement+0x130/+0x134。
本轮未完整追踪foothold构造器，不把这两个字段与WZ路径的对应关系当成新增确定结论。

实例 `Map/Map/Map0/001000003.img`：life/0是11100露茜，x=-258、原始y=84、fh=5，
foothold路径为1/0/5；摊位前景在2/obj/0，引用 `Map/Obj/shop.img/shop/grocery/13`，
x=-257、y=15、z=3、zM=2。相同NPC也在001010003；类似店主还有104000002的1001100米娜、
100000102的1011100卢娜。店主使用原Npc/0011100.img/stand，不需要自行画柜台或改NPC原图。
柜台13/0为154×336、origin(77,168)；露茜stand/0为59×69、origin(27,69)、delay1200ms。
地图对象原公式 `z+30000*layer-0x3FFFF830` 已在original-map-rendering.md核实。

当前 `NativeMapView.Show` 将所有tile/obj整体装入order=-400的SortingGroup；NPC在group外
固定order=100。因此即使原数据的柜台在前景层，也永远遮不住NPC。这是确定的移植层级缺口，
不是NPC站立坐标错误的证据。不能靠移动NPC位置修复遮挡。

复现（项目根目录，Python须有pefile/capstone或wzpy）：

```sh
rtk proxy python tools/inspect_beidou_client.py ../../Beidou-Server/clien/BeiDou.exe --fn 0x6d267d
rtk proxy python tools/inspect_beidou_client.py ../../Beidou-Server/clien/BeiDou.exe --start 0x9b1371 --end 0x9b13bd
rtk proxy python tools/imgdump.py Map/Map/Map0/001000003.img life --depth 2
rtk proxy python tools/imgdump.py Map/Map/Map0/001000003.img 2/obj --depth 2
```

## 点击与脚本宿主缺口

下表是**无任务候选时的静态分派**，不能当成所有存档的逐次点击结果（有任务先走任务菜单）：

| 店主数 | 当前路径 | 缺口 |
| --- | --- | --- |
| 67 | IsSilentNpc提前返回 | 有info/speak、无数字脚本被当装饰NPC，没有查商店表 |
| 33 | Talk提示没有对白脚本 | 缺原端无脚本→数据库商店回退 |
| 13 | 执行原脚本 | 脚本可含额外门槛/菜单，开店分支宿主未实现，示例如下 |

- 11000赛德：源脚本调用 `Java.type('org.gms.server.ShopFactory').getInstance().getShop(11000).sendShop(cm.getClient())`；BuildJava不支持此类而返回JsNull，宽松空成员调用没有效果。
- 2100002寨尔德、2100003雅思敏：源脚本调用cm.openShopNPC，当前宿主仅提示“商店系统尚未接入”。
- 9201099老莫：任务8224完成才openShopNPC，否则源端本来就只说话；不得去掉门槛。IMG的MoStore命名脚本无有效内容时回落数字脚本。
- 113位店主及下述16位额外商人的源脚本解析结果与包内一致，存在的脚本文本一致；本批未发现需要靠修改JS解决的绑定错误。
- GameWindows.cs的ShopWindow.Toggle/Draw包装的是现金商城，**不是普通NPC商店**；当前缺普通商店窗口及交易通道。AdminShop仅是后台维护接口。
- 当前存在shops/shopitems的schema及后台维护代码，未找到全量普通商店SQL种子接线。本轮未读取真实存档，不能断言某个已有存档的表为空。

## 全量数据与验证边界

持久入口 `tools/audit_npc_shops.py`，用与imgdump.py相同的Python+wzpy环境运行：

```sh
rtk proxy python tools/audit_npc_shops.py
```

扫描源商店SQL、Npc/IMG、String/Npc、源JS/包内JS、全部6337张地图，解析错误0。
121条商店、113个店主、115处地图放置，115处均hide=0且有有效life/fh。
9001002、9090000无静态地图放置，仍保留在商店清单中，不可据此删除动态/脚本商店。
逐NPC的功能、全部shopIds、脚本匹配、地图/life/x/y/fh/layer/group在 `docs/npc-shop-audit.json`。

另外以String/Npc的商人称号补查了16位最终shops表之外的NPC（其中10处静态地图放置），
见JSON的merchantLabelsWithoutShop。它们是**原端迁移SQL即未配置**，不能直接归因于移植漏绑，
也不能凭称号造商品表：包括2110002吉欧、2110004威尔斯、2142000玛亚、9110100山本大叔、
9110101小太郎、9110102田一、9130029吉川元春、9250007/8/9等。
9110100/9110102的源脚本要求打开同ID商店，源端openShopNPC在缺表时回落11000；
2110002的数字脚本正文却是组队任务脚本，属于原端数据/脚本语义问题，不能擅自替换。
尚未检查外部实际运行的MySQL是否人工补过这些记录，本轮结论限仓库迁移数据。

UI前置调查仅定位到 `UI/UIWindow.img/Shop`：backgrnd463×339、select162×35、meso12×12、
token11×12，BtBuy/BtSell/BtRecharge/BtExit、TabSell/TabBuy。原窗口ctor0x75712F、
OnCreate0x7532CE、Draw0x754DA0→左列0x754E78/右列0x75577A。
**尚未完整核实全部布局、状态和交易流程，定位不是完整迁移合同。**
本轮没有Unity绘制/交互或Android验收，没有修复上述运行时缺口。


## 修复与新增原端证据（2026-10-29）

二进制身份同上。foothold 字段已追完：CField::Load `639B3D` 取字符串池1486
`foothold`，调用 `A43E7B`。该函数按WZ三层枚举：`A43F78` atoi第一层路径存
`[ebp-2C]`，`A44098` atoi第二层路径存`[ebp-38]`；`A44770..A44793`将这两个
值分别作为构造器第6/7参数传给`A4534A`。构造器`A4538A..A45393`从
`[ebp+1C]/[ebp+20]`写入foothold`+1C/+20`。因此这两字段确为WZ
foothold的layer/group，并非NPC y或life序号；结合`9B1381`接地复制及`6D267D`
NPC公式，完整得到`10*(3000*layer-group)-0x3FFF8AD5`。

`NativeMapView`现在把静态NPC和tile/obj放入同一个MapLayers排序组，比较原端long z后
转换为Unity整数rank，不再对NPC固定100，不改life坐标或NPC原图。动态NPC的
原端`AbstractPlayerInteraction.spawnNpc`会设置`findBelow(pos).id`，渲染端按同一
平台规则取得层组并插入排序；保留现有动态NPC坐标。

### 商店控件与布局

原端商店包入口`7529AD`、OnCreate`7532CE`、Draw`754DA0`。材质来自
`UI/UIWindow.img/Shop`，窗口463×339，五行、行距40。

- `7535DC..7535E5`：PersonalShop/BtExit，(142,15)，id2。
- `753659..753665`：Shop/BtBuy，(142,35)，id1000。
- `7536D9..7536E5`：Shop/BtSell，(372,35)，id1001。
- Buy页签控件(5,95)，width222，style2，id1002；`753A06`将固定宽设50。
  Sell页签控件(235,95)，width222，style2，id1003。页签标签选中用disabled、未选中用enabled。
- `4DE15F` style2分支`4DE1BB`：接头12、两端6；固定宽非0时内部宽为fixedWidth-8，
  否则(width-(count-1)*12-12)/count取整。`4DD903`使用Basic.img/Tab2边片与填充，
  标签居中、y加2；这些是原端代码布局，不是设计的新边框。
- `753DB8..753DCE` / `753E19..753E2F`：滚动条(210,127)/(441,127)，高度194，id1004/1005。
- Buy基准y148、选中(43,y-21)、图标(8-origin.x,y+13-origin.y)、名字(49,y-20)。
  Sell基准y146、选中(273,y-21)、图标锚点(238,y+15)、名字(279,y-20)、价格(279,y)，
  Recharge按钮(404,y-19)。行点击区`75602F`左(6,127)、右(236,127)，每40一格。
- `755E72`金币右边界447、y64。`754860..754D9F`窗口NPC原图层在(56,76)，
  读取原NPC帧、origin和delay；尚未迁移右侧角色纸娃娃及推荐装备筛选页签。

### 运行时接线与交易边界

- `NativeNpcShopService` / ITransport.NpcShop 独立于现金商城。
- 无脚本店主通过数据库npcid回退；有任务候选时保留普通店主对话入口，不能只剩任务菜单。
  `cm.openShopNPC`按shopid打开并按原端回落11000；`Java.type(ShopFactory)`宿主支持
  `getInstance/getShop/sendShop`，11000等原JS不改。
- `tools/export_npc_shops.py`按版本重放所有shops/shopitems迁移，含删除过时装备的临时表JOIN；
  产出Resources/Shop/NpcShops.json：121家店、4404条商品，所有来源SQL哈希随资源保存。
  首次接入用持久化标记导入未配置店铺，保留已有管理员库存；后续不复活已删除库存。
- 买、卖、补充通过服务端事务处理，装备属性/堆叠/容量复用BotItemCatalog和TradeItemRules，
  售价与unitPrice舍入照Shop.java、ItemInformationProvider.java。完整校验slot/itemid/数量/
  金币/背包空间，响应同步角色背包和金币；切图、退出及Esc关闭会话。
- 原端自动附加的零价飞镖/子弹保留服务端stock条目，只提供补充；仓库迁移中的4404条商品
  均有price或pitch，未为原端缺配置的商人编造商品。

### 定向验证

`rtk dotnet run --project tools/npc-shop-check/Check.csproj -c Release`：覆盖全部121店开店、
无脚本NPC、shopid回退、药水/装备购买出售、错商品/无钱/超量/满背包拒绝、飞镖补充、
重复补充不扣款、关店/换图拒绝交易和导入标记保留删除。

`NativeNpcShopChecks.Run`：实际Unity宿主验证无脚本商人、原11000/2100002/2100003 JS、
9201099任务8224门槛、Esc及地图001000003/001010003/104000002/100000102柜台深度。
实际宿主与柜台深度检查通过23项；交易服务回归通过144项。
PlayMode画面冒烟截图已人工查看：`docs/hud-review/npc-shop-counter.png`中露茜位于柜台后，
`docs/hud-review/npc-shop-window.png`显示原图商店窗口与商品。截图夹具使用独立临时数据库。
这不等于Android实机点击验收；本次未构建APK或进行实机验证。

脚本harness `--run`使用其简化宿主，889文件：789成功、parseFail0、runFail28、javaInterop72；
其中ShopFactory在简化宿主未注册，与上面的实际NpcApi宿主不同。未改源JS来消除这些既有报告。
`--pack`：2779条脚本、parseFail0、mangled0。Unity compile-check已通过0error/0warning。
