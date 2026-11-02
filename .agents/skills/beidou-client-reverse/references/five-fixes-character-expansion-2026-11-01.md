# 五项修复与角色扩展（2026-11-01，持续迁移）

## 身份与复现

原文件 `Beidou-Server/clien/BeiDou.exe`，SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，ImageBase `0x400000`。反汇编使用 `tools/inspect_beidou_client.py <exe> --fn <VA>`；每次只传一个 `--fn`。IMG 使用 `tools/imgdump.py`、`tools/imgpng.py`。

## UserInfo 已确认的扩展布局

- `8FFE3B`创建按钮、`8FF5B0`分派、`8FF863`切换布局、`901185`绘制。`UI/UIWindow.img/UserInfo/` mode0/1/2/3 背景为backgrnd/backgrnd2/backgrnd5/backgrnd7，尺寸275×199/400/362/404，底栏y177/378/340/382。
- 字符串池a0c BtItem ID2000 (208,22)，a0d BtWish ID2001 (208,41)，**a0e BtFamily ID2016 (105,23)**；Party2002(149,23)、Trade2003(149,42)。底部坐骑2008/9 x5、宠物2006/7 x93、手册2010/11 x145、**排除列表2012 x199**。纠正旧reference把学院与赠送位置混淆的结论。
- `8FF7B0`事件表2008..2016依次为mode2、mode0、mode3、mode0、9009BF排除列表、宠物索引0/1/2、学院。学院自己走type1B，他人走type1C关系树。
- `903C50`宠物数据：+688模板、+694名字、+6A0等级byte、+6A4亲密short、+6AA饱食byte、+6B0服装；调用90433A纸娃娃和904AEA宠物装备列表（纠正早期“命令列表”误称：该函数遍历181类装备及1822000/1832000，按宠物槽位分组）。9044E4/EA宠物baseline(53,299)，名字中心52/y306，种类58/331，等级58/348，亲密189/330，饱食189/348。
- `903FC7..9041A5`坐骑baseline(53+xoffset,261+yoffset)；模板信息+1D4scale、+1D8x、+1DCy；1902002特殊scale70/x=-25/y=-25。文字名称58/293、等级58/311、经验189/293、疲劳189/311。另1902015为80%/0/-12，1902016与1902017为80%/-5/-15，1902018为75%/-5/-20；当前使用原stand1合成坐骑纸娃娃。5CC820..5CC920确认info/userInfoScale默认100、userInfoDiffX/Y默认0，字段+1D4/+1D8/+1DC。
- 物品子窗900BD9创建，8FD28E/8FD4C0子控件/绘制；愿望8FDB1B创建、8FDE08绘制、8FE30D赠送。子窗相对主窗(270,mode0?34:196)，240×162，物品backgrnd4/愿望backgrnd3；隐藏钮220/6，VScr4在218/33，高112；3行×40。物品icon baseline17/64、name57/32；愿望BtPresent165/50逐行+40。物品ReqLv细节仍待截图/汇编逐项核对。
- `903E24`遍历装备body slot，跳过0/14/18/19/20和181类；cash weapon存在171替换。903F0D与903F5B分别追加appearance+E9隐藏装备与+19可见装备，不是现金覆盖普通项。DTO现保留同槽普通及现金两项，171现金武器替换仍未完整核验。
- 原服务端 `PacketCreator.charInfo`：仅召唤宠物；坐骑槽18需满足装备等级；wishlist为SN；monsterbook以2388区分普通/特殊，等级累计阈值1+10×level；槽49勋章，completed quest>=29000排序。当前DTO已隔离目标与查看者资料，坐骑按BotItemCatalog原IMG reqLevel实施等级门控，假人槽18已补查。

## 学院原版窗口

- CUIFamily vtable B38CA8，创建80862B、绘制808A54、点击8091D2；`UI/UIWindow.img/Family/backgrnd` 224×392。
- 按钮Junior95/77、Tree164/77、Left15/177、Right196/177、Precept209/55、Special152/322、OK93/368。11项特权循环；Precept原输入1..200；Special调用A3427A(index)，Junior调用8082FE。
- 808177文字参数：标题7/33/w209，同学数52/81/w37，声望54/111/w159，今日106/128/w107，特权名15/178/w193，cost74/198/w43，used175/198/w32，page158/158/w40。描述8E40E4调用push顺序text,1,D6,C3,10，当前按x16/y214/w195迁移，富文本颜色尚待完整对齐。
- 本次`FamilyEntitlements.json`直接解码源服务端`message_zh_CN.properties`的FamilyEntitlement.message1..22，不自行编写描述。
- FamilyTree vtable B38D58，创建809CCC、父绘制809EB0、slot绘制80AD31、载入80A5F7、点击80A252、鼠标80A27A；578×386。VA BE2778的11槽坐标：(222,40),(222,94),(222,146),(222,198),(365,198),(80,250),(365,250),(13,302),(148,302),(297,302),(432,302)。对应首领、祖师、学长、中心角色、同级、2直属、4下属。
- 首领PlateLeader/online，其他PlateOthers/online，name x/y+5/w133，level/job x-10/y+20/w153，首领文字额外y+1；selected origin3/3，因此绘制slot-3/-3。JuniorEntry21/57、Bye71/57。80A27A只选择槽2/5/6（鼠标移过），不能猜成任意点击/双击导航。
- 源`OpenFamilyPedigreeHandler`按名字查同频道在线目标，返回目标关系树。当前新增ReadFamilyTree独立familyTree/treeTarget字段，保留请求者自己的金币/学院等状态；在线/同world/存在学院检查已实现，同频道约束已由GameServer绑定Social.Parties在线actor channel回调。不得把目标Read结果当作查看者Read同步。
- 当前原版学院页接入已有社区服务，恢复邀请/召集确认。关系解除说明、hover tooltip、人数标注、完整刷新与原端控制条件仍待核实。不能宣称完整学院迁移。

## 排除列表新增确定入口与当前实现

`9009BF`开关子窗，主窗+c2c标志；`900A7C`构造159×147，x主窗+270，y主窗+(mode?196:34)，参数pet index。vtable B3BFF4：OnCreate8FC61B、Draw8FCA75、button8FCD38/8FCD5E等。8FC61B：BtHide142/5，Meso10/124，Register80/124，Delete124/124；VScr在140/25/90，原记录数减5为滚动范围。源`PetExcludeItemsHandler`只允许角色活动宠物ID，提交完整非负ID集合并差异持久化；金币用Integer.MAX_VALUE，拾取读合并排除表。需要完整Draw/按钮/选择分析后再实施，当前NativePetExclusionWindow已接原名单/搜索/结果，UI尚待视觉验收。

## 本轮其他修复和验证边界

- 二段跳`96C052..96C166`使用BasicEff/Flying(4111006)、Flying1(14101004)、SoulRush(11101005)，池号2728/2729/5100；不是Skill.img的effect节点。新增movement clip及实际施放播放。
- 快速移动`957B74`（调用969146）存在pending+2B18、目标+2B20/24、起止时刻+2B28/2C，120ms。当前NativeTeleportMovement按地图限制bit2/方向/技能range/平台查找，PlayerCtrl直接缓存当前FieldMap几何，避免依赖尚未加载的假人地图注册，先校验再扣费，但立即PlaceAt及固定Teleport特效仍非原端完整时序；22101001只有服务端支持证据，需核实EXE。
- DialogFlow原每帧把选中链接拉回可视区，导致人工滚动反弹；改为只有键盘导航显式RevealSelection才自动定位。NativeUi保存可见窗口bounds，底栏统一过滤当前指针及手势起点，避免松手落到底栏。尚需Android触摸实测。
- AdminGive资源键mesos改meso，基于在线Profile.gold、long存储与饱和加法。Unity LoginGameplayFeedbackChecks实际HTTP发放30亿并保存重载通过。
- BotMovementRuntime支撑高度容差之外进入下落；BotField插值尚未接近实际支撑时保留空中stance，避免沿用终点walk。纯服务端有100px悬空回归；非完整地图实机巡检。
- 已完成资源审计802技能/2119等级/5749clip/61464帧，Unity蜗牛三级实际施放、伤害与渲染、4召唤与HP扣费通过。**不是802技能全机制运行时验收**。三种移动施放/像素回归已通过；Teleport在原地图100000005的实际施放、定向落脚、精确扣蓝与无效方向不扣蓝已通过；测试主动取消MapService注册，确认不依赖假人系统注册地图。
- 服务端关系树隔离/频道及宠物过滤用例后725/0，最新DTO变更后重跑仍725/0；宠物用例覆盖去重、装备门控、物品/金币、他人宠物拒绝、非法负ID不覆盖及删除金币过滤。NPC资源包2779/parseFail0/mangled0；通用NPC harness889中790OK、27runFail、72JavaInterop（原有Java/桩接口缺口），不得写成全通过。角色四布局、物品/愿望、学院/关系树已有独立Unity渲染截图并人工检查（docs/character-info-review）；宠物排除/搜索/结果页亦已截图；宠物→坐骑→手册展开及搜索/学院/关系树原关闭钮命中通过。没有Android APK/实机结果。


### 排除列表后续核对及实现

- 8FCA75绘制名单：x15/y30/14px行距，实际6行；selected FillRect(10,y,138,13) 0xFF5B7B9E、hover 0xFFBBC9D9。8FCEDE客户端上限10、去重，按本地化物品名排序；金币名字字符串1586为mesos。8FCD5E金币toggle，Register开搜索，Delete删除选中项。
- 搜索构造8FC031：219×123，资源ExceptionItemSearch/backgrnd；8FC1BF搜索钮140/76、输入15/78/115/13，长度256；8FC432子结果位置(0,+120)。8FB64B结果162×117，8FB79B按钮BtSave49/94、BtDelete82/94，VScr142/8/75；8FBA69文字10/12/14px间距，5行，选中/hover色同名单。
- 8FBE4B按搜索词去空格并转小写，从物品名表查子串，5D3F1B验证物品存在，5D4A2B读info/cash排除现金物品（字符串7f5=cash）。当前搜索用原IMG Node存在且非cash，客户端物品表来源BotItemCatalog，完整命名排序仍需原版中文客户端截图比对。
- 源PetLootHandler先检查拾取装备，**只有装备1812007 ITEM_IGNORE时**才应用角色所有活动宠物排除列表的合并集合。NativePetItems.Exclusions持久化petignores，校验活动宠物归属、非负完整集合、byte长度、去重和差异更新；ExcludesPickup实现过滤器装备门控、金币int.MaxValue和合并列表。GroundItemBridge在宠物拾取权威入口前过滤，人工拾取不受影响。
- 当前已实现坐骑纸娃娃及装备列表、宠物装备列表/服装同帧、手册封面名称/勋章四行卷动及主子窗合并移动边界；切换模式/宠物重置装备卷动。装备列表坐骑baseline106/212、名称147/179、ReqLv147/198与值183/197；宠物baseline106/248、名称147/217、ReqLv147/236与值183/235，3行×38，VScr252/177或215/h112。手册icon14/182，cover101/199，medal icon8/278、name101/247、count101/265，VScr252/298/h73，4行52/301+18。手册2380000封面与1142000勋章及六项原勋章quest数据已定向截图检查，下一箭头卷动断言通过；学院hover tooltip及全状态条件、现金武器替换、宠物王冠仍为未完项。用户已授权完整迁移，不得再次询问同一授权。

### 后续补核（同日）

- 关闭钮：808404→92C0BF传type3/x206/y6，92C2E8字符串15C9=`UI/Basic.img/BtClose`；809ABB传type4/x558/y10，字符串878=`UI/UIWindow.img/%s/BtClose`，FamilyTree/BtClose确实存在。此前Family/BtClose不存在会导致无法关闭，已修复。8FC031搜索构造传type3/x195/y8（不是200/5），已修复。通过inspect工具各fn可重现。
- 排除入口精确启用：900643..90068D要求本人、存在第一活动宠物及+67C宠物资料，508A95只是secure-int解码，不能视作能力校验函数本身。705E2C按宠物索引0/1/2聚合BD85A0..BD85F0装备位模板+1F0；705FF4以bit80写CPet+17C/+184；5CD61F池C14=`ignorePickup`非零在5CD672设置bit80。UI据此检查第一宠物装备的原info/ignorePickup，不能只凭已召唤宠物启用。
- 学院入口显式传familyTarget。普通键位打开本人学院，不再使用上次邀请/查看保存的InputName；角色资料入口明确传目标名字。
- LoginGameplayFeedbackChecks最新通过面板当前指针/按下位置/松手/隐藏窗口阻挡及HTTP30亿金币同步、保存重载（断言无异常，exit0）。
- 首次新增渲染fixture漏加GameLoop.Boot测试隔离，实际画面是登录页，虽日志无异常不能计作UI PASS。补SessionState隔离后已重跑并人工检查8张原资源界面；新fixture断言InGame/Visible，防止重复误判。后续含排除窗口与按钮交互另记结果。

- 80A5F7在80A63C写本人与查看目标相同标志+638；80AB51..80AB99以该标志和直属数量启用登记/解除按钮；80A27A(this为+4子对象，读取+634)只允许本人关系树的2/5/6槽，离开这些槽会重置选择为-1。已补本人门控及离开清空。
- 80B7CC选择无效时用池11EA输入待解除角色名字（4..12）；只匹配直属学长或直属同学。80B385/80B5D2确认池146D..1470按下属level计算声望损失`(level/20+10)*level*2`，上一级减半（整数）；当前UI恢复关系名输入及相应声望影响确认，费用提示尚未完整迁移。服务端FamilySeparateHandler原代码按操作者与forkOn.senior等级差扣费，踢直属时差为0；不能以“看似错误”为由改成其他公式。
- 学院家训输入8091D2→9AC7CB为1..200字；服务端FamilyPreceptsHandler只允许leader。当前按钮按本人family entry.senior==0启用，沿用原服务端200字约束。

- 最后NativeCharacterInfoSceneChecks通过11张渲染截图、宠物/坐骑/手册展开、非空勋章卷动、搜索遮挡及三处原关闭钮命中。角色侧窗被排除列表/搜索/结果覆盖时禁用输入，同时检查当前点和未结束手势按下点；9009BF与900BD9是独立子窗生命周期，不能靠猜测互斥关闭。验证记录保存在docs/character-info-review/verification.md。
