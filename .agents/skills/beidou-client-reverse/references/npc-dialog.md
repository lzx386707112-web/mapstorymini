# NPC 对话窗口（UIWindow.img/UtilDlgEx）

核实快照：2026-10-28。二进制身份见 [binary-and-tools](binary-and-tools.md)（SHA256 `1b91cf5b…f134b2`，ImageBase `0x400000`）。
本文件区分「已用 IMG/汇编核实的原端行为」与「MapleDash 当前实现」，未核实的推测单列在末尾。

## 窗口素材（`UI/UIWindow.img/UtilDlgEx`）

| 节点 | 尺寸 | 职责 |
| --- | --- | --- |
| `t` | 529×28 | 顶部。第 3..7 行外框，第 8..12 行深色描边，**第 23 行起是白纸** |
| `c` | 529×20 | 中段，按 20 行平铺。**第 152 列起是白纸**，第 506 列是纸面右描边，529 已是窗口外阴影 |
| `s` | 529×58 | 底部。第 0..13 行仍是纸面（**第 14 行是纸面下边**），20..27 行白边，28..52 行蓝灰底板（按钮区），53..57 外框 |
| `bar` | 121×19 | NPC 名字条，横向中心 80，左侧 20 |
| `dot0` / `dot1` | 6×7 | 列表行的圆点，悬停 / 常态 |
| `list0`..`list3` | 119×14 / 102×18 / 26×14 / 106×14 | 脚本用 `#fUI/UIWindow.img/UtilDlgEx/listN#` 插进来的分组标题（客户端里按 `UtilDlgEx/list%d` 拼路径，字符串常量 3235） |
| `line` | 332×2 | 分隔线；宽度也是正文可用宽度的旁证 |
| `notice` | 311×134 | 内嵌说明图（`UI/UIWindow.img/UtilDlgEx/notice` 被 EULA 等文本引用） |
| `ic` / `it` / `is` | 529×20 / 529×28 / 529×58 | 带图片正文的变体框（与 c/t/s 同尺寸） |
| `BtClose` | 85×19 | 关闭 |
| `BtPrev` `BtNext` `BtOK` `BtYes` `BtNo` | 46×18 | 页内按钮 |
| `BtQYes` `BtQNo` `BtQGiveup` | 57×17 | 任务「接受/拒绝/放弃」 |

窗口固定 **529** 宽；纸面可用矩形（相对窗口左上角）= x `152..506`、y `23..(高-45)`。
正文测量宽 **341**（`0x9A3D07`），`line` 的 332 是素材宽度，不能代替排版宽度。旧版按纸面像素推得 328/332 的结论已撤回。

## 常规对话窗口的客户端参数（2026-10-28 补核）

同一 SHA256 的 `CUtilDlgEx`：`+7C` 原生类型，`+80/+84` 正文原点，`+88` 测量高度，`+8C` 正文区高，`+90` 滚动像素，`+94` 启用滚动，`+98/+9C` 窗口尺寸；`+A4=0` 是有 NPC 肖像的普通窗口，`+6E4=0` 为左肖像。

- `0x9A3CAE/0x9A3D07/0x9A3D71/0x9A3DC1`：普通类型 0/1/4 的窗口宽 529、测量宽 341、正文区最小 120、最大 240。输入类型 2/3 的上限不是 240，不能直接外推；小窗、头像和图片类型另有分支。
- `0x9AC552` 调 `0x99DD54` 排版，类型 4 测量后增加 24；`0x9AC6D0/0x9AC6FD` 的 prev/next 标记增加 18。
- `0x9ACC20`：`(contentHeight-240)/8 > 1` 才启用滚动（整数除法）；否则正文区 `max(120,contentHeight)`。窗口高 `body+86`。不滚动时正文原点 `(157, 28+trunc((body-contentHeight-6)/2))`，滚动时 `(155,22)`。这里的 contentHeight 包括上述保留高度。
- `0x9A3E9F`：裁剪从正文原点右下各 1，宽 339、高 `body-2`；内容仍按原点画。
- 字体初始化 `0x9A334E..0x9A3937`：Arial 12（字符串 5527）；黑 `FF000000`、红 `FFFF0000`、绿 `FF00FF00`、蓝 `FF0000FF`、白 `FFFFFFFF`、深紫 `FF51378C`，各有普通/粗体。Android/Unity 中文字体替代并不等于 Windows Arial 字形逐像素一致。
- `0x9A216C..0x9A24B3`：排版内部间距 8，横向也预留两侧 8；`0x9A2D01` 同行最小高度 16；`0x9A2D95` 同行底对齐，链接行垂直再加 10；行间加 2，最终再加尾部间距。`#L` 由 `0x99E4A0` 插入宽 18 的标记 run；`0x9A415F` 按 run 的 x+10/y+2 画 dot，不能把图片从链接命中范围中剥离。
- `0x9A7C8B`：关闭 `(9,h-26)`；OK `(w-54,h-26)`；上一页 `(w-10-122-scroll*8,h-77)`；下一页 `(w-10-68-scroll*8,h-77)`。翻页按钮在纸面底部，不在关闭按钮所在底栏。
- `0x9A8062`：普通 Yes/No 左坐标分别 `w-108/w-54`，任务接受/拒绝为 `w-130/w-65`；y 均 `h-26`。两套按钮必须保留原始尺寸。
- `0x9A6DFD..0x9A7C76`：名字合成画布 121×23，bar 在 y=3，名字文字 y=5，左肖像中心 x=80；肖像+名字共高 H，正常组顶 `28+trunc((body-H)/2)`，放不下时 `h-H-62`；普通 NPC 绘制再加 y=7。当前迁移用 stand 帧包围盒，未覆盖原端 avatar/rightPortrait 特殊模式。

复现：`rtk proxy /tmp/mapledash-reverse-venv/bin/python tools/inspect_beidou_client.py ../../Beidou-Server/clien/BeiDou.exe --start 0x9acc20 --end 0x9ace00`；其他范围替换为上列 VA。工具会输出完整 SHA256。

## 共享滚动条（`UI/Basic.img`）

- 资源基址只有两处：字符串常量 **1264 = `UI/Basic.img/HScr`**、**1265 = `UI/Basic.img/VScr`**，
  代码里按样式号拼后缀（`VScr`、`VScr2`..`VScr8`、`HScr`、`HScr2`..`HScr6`）。
- 构造入口 **`0x4D6D96(orient, style, parent)`**：
  - `[ebp+8]` 方向：`0` → HScr(0x4F0)，`1` → VScr(0x4F1)；
  - `[ebp+0xC]` 样式号：`dec eax` 链共 7 个分支，映射到 `0xAF1E18` / `0xAF1E20` / `0xAF2EBC` 等字符串常量；
  - `[ebp+0x10]` 父节点，最后 `0x48EF51` 挂接。
  包装函数 `0x4D6E7A`。已知调用点：`0x4D088B`、`0x8D01CE`。
- 各套尺寸（都是 `base`/`prev`/`next`/`thumb` 四件）：
  `VScr` `VScr2` `VScr6` 12×12；`VScr3` 13×12；**`VScr4` `VScr5` 15×13 + thumb 15×25**；
  `VScr7` 15×14 + thumb 15×25；`VScr8` 15×13 + thumb 13×25；`HScr*` 为横向对称。

## 对话滚动条的确切引用

`0x9A63F2` 经控件虚表 Create `0x4D7037` → `0x4D6D96`，参数方向 1、样式 3；样式 3 取 `0xAF1E14` 的后缀 "4"，因此 **UtilDlgEx 确定使用 VScr4**。旧版“未找到直接调用，因此可能不是通用滚动条”的解释已撤回，是虚表间接调用。

父窗口坐标：`x=textX+341-3`，`y=textY+2`，高 `body+3`；范围 `floor((content-body)/8)+1`，滚动偏移为位置×8。`0x4D7B19..0x4D7C21` 取得 thumb 原图尺寸，位置 `(trackLength-thumbLength)*pos/(range-1)`，直接 Canvas.Copy 绘制，不按可见内容比例拉长 thumb。VScr4 thumb 固定 15×25。

## 灯泡候选与脚本（2026-10-28）

- `0x722B0D` 读取字符串 4063 `autoStart`，写 QuestInfoManager `+138`；`0x722B90` 读取字符串 5111 `autoAccept`，写 `+150`；`0x722DE5..0x722E65` 读取字符串 4166 `autoPreComplete`，仅在 autoStart 同时为真时写 `+168`。
- `0x720F8A/0x720FAB/0x720FCC` 分别查询上述三表。`0xA08AB7→0xA08AF6` 对 autoStart 候选做需求判断；autoAccept 分支自动发送开始请求，其余加入角色上下文 `+3754`。普通本地图任务不是灯泡候选，不能拿服务端 NPC 距离检查代替这个筛选。
- `0x95D260` 打开原生类型 4、**固定 NPC 9010000** 的通知菜单；不是首条任务 NPC 的肖像。`0x724BAC` 用字符串 3191/3192/3193 分组标题（前置两次换行，内嵌 list1/list0/list3），3195 为 `#d#L%d# %s#l#k\r\n`。菜单不应另用一套自定行高/内边距。
- 点击仍使用任务自己的 NPC，调用任务请求 `0xA26E31`。服务器 QuestScriptManager 按数值任务 ID 载入 `quest/<id>.js`，startscript 的字符串是入口标记，不是文件名。
- 已验证远程脚本例：2300，WZ 限制等级 30～50、二转战士职业；原样 JS 经过接受→两页 Next→YesNo 后 `warp(106020000,0)`，随后 `forceStartQuest()`。它不是一级出生任务。1031 是普通 NPC 数据对白，无对应传送 JS，不能凭用户描述给它添加 warp。

## MapleDash 迁移与验证边界

`DialogText/DialogFlow/NpcDialogUi` 现迁移上述常规布局、原始字号/颜色、同行底对齐、#L 标记与图片命中、统一任务菜单正文流、固定窗口宽、页按钮位置与 VScr4 原图尺寸。`NpcField` 灯泡按 autoStart/autoAccept/autoPreComplete 读取，NPC 距离规则仍只用于实际请求。原脚本不修改。

`QuestNpcChecks.Run` 包含 2300 真实脚本的传送+接取断言和图片链接断言；2026-10-28 的最终 Unity 日志 `/tmp/quest-dialog-final.log` 为 24116 通过、0 失败；编译检查 0 warning/0 error，脚本包 2777 条、parseFail=0/mangled=0。Play 截图 `/tmp/npc-dialog-final-review/ui00.png` 检查 NPC9900001 长菜单与固定 thumb；没有原端同屏截图，因此不作为逐像素一致的证据。全量 harness `--run` 为 889 条、769 正常、47 runFail、73 Java 互操作分支；本次未修改解释器或其模拟接口，不能写成全量通过。avatar/rightPortrait、特殊图片窗口、autoAccept 自动触发和进行中 list3 状态尚未完成迁移；不得声称所有原端对话类型逐像素一致。一级建号后用户所述的具体传送任务尚未定位，不能把 2300 的通过等同于此问题已完整复现。

## 点「选项」之后跑什么（脚本侧接口面）

对话窗口只负责把 `#L<idx>#` 的序号回给脚本（`NpcSession.Reply(1, idx)` → `action(mode=1, type, selection=idx)`），
真正做事的是脚本调用的 `cm.*` / `player.*`。所以「点了没反应」几乎都不是界面问题，而是**接口没绑**：
`NpcApi.Lenient` 在 `JsHostObject.Missing` 里直接抛 `Unmigrated server API: <owner>.<name>`，
整段 `action` 就此中断（源端这里是真实 Java 调用，不会中断）。

对齐依据必须回源端 Java，不能凭脚本猜：

| 脚本调用 | 源端位置 | MapleDash |
| --- | --- | --- |
| `player.saveLocation(type)` / `saveLocationOnWarp()` | `Character.java:8131 / 8120` | 写 `savedlocations` 表；`saveLocationOnWarp` **只补空格子**，portal 取 `distanceSq` 最近的门 |
| `player.getTotalStr/Dex/Int/Luk/Watk/Magic` | `Character.java:5238-5260`（`localstr`…） | `EquipLoadout.Apply(基础属性, 武器, 0)` 的产出 |
| 战力数值本身 | `PacketCreator.calculateNameplatePower` | `stats*max(1,max(watk,magic)) + maxHp/10 + maxMp/20` |
| `cm.isNotCanHold(type,count)` | `NPCConversationManager.java:1608` | 单机背包不限格，恒 false（与 `canHold` 一致） |
| `cm.getOnlineTime()` | `AbstractPlayerInteraction.java:1547` → `OnlineTimeTask` | `Npc/OnlineTime.cs`，账号扩展值「每日在线时间」，`period=1` 自动跨日归零 |
| `Java.type('...InventoryType')` | `org.gms.client.inventory.InventoryType` | 数值取道具 id 首位（EQUIP=1 USE=2 SETUP=3 ETC=4 CASH=5） |
| `Java.type('...ExpTable')` | `constants.game.ExpTable` | 客户端 `ExpTable.Needed(level)`（同一张 WZ 经验表） |
| `Java.type('org.slf4j.LoggerFactory')` | — | 落 Unity 日志，避免「类不存在」中断对话 |
| `Java.type('org.gms.server.CashShop')` | `CashShop.java:72` | `NX_CREDIT=1 MAPLE_POINT=2 NX_PREPAID=4` |
| `client.getChannelServer().getPlayerStorage().getAllCharacters()` | `Channel/PlayerStorage` | 只返回自己一个（单机只有自己在频道里） |

验证入口：`QuestNpcChecks.RunMenu` 逐个点开 `npc/9900001` 的 31 个选项，
缺接口/缺 Java 类打印 `GAP`，其它异常算回归。**新写/迁移 NPC 脚本相关 API 前先跑它**，
不要再用「对话脚本出错」这种笼统提示去猜。


## 首次建号→出生图→交互链复核（2026-10-28）

源端依据：`gms-server/src/main/java/org/gms/client/creator/novice/{BeginnerCreator,NoblesseCreator,LegendCreator}.java`，`CharacterFactory.java`，`constants/id/{MapId,ItemId}.java`；没有将已存角色地图当成新建角色默认值。

| 建号职业群 | 初始职业 | 出生图 | ETC 指南 |
| --- | --- | --- | --- |
| 1 冒险家 | 0 | 10000；仅 use_beidou_beginner_map=true 时为4 | 4161001 |
| 0 骑士团 | 1000 | 130030000 | 4161047 |
| 2 战神 | 2000 | 914000000 | 4161048 |

`CharFactory` 原实现把战神建成 job0/map10000，并漏读北斗出生图配置；现按上述原工厂修正。临时数据库的真实 CreateCharacter→Transport.CharSelect 回归覆盖四条路线，不修改用户已有角色。

- map10000 的 `info/onUserEnter=go10000`，原样 `map/onUserEnter/go10000.js` 只 unlockUI 与 mapEffect，不传送。life NPC2101、2100、隐藏2007；2101 在本图只显示移动教程；1031 来自 Say.img，既无 JS 也无 warp。
- map4 没有入图脚本；life/0 是 NPC2104。中文原始 `npc/2104.js` start→action(1,0,0) 显示“好的，那么我们出发吧。冒险旅途开始了！”，下一次确认 status1→warp(1)→dispose。此链是 NPC 对话，不可伪称头顶灯泡任务。回归验证原脚本实际改变地图为1。
- 当前复制的 Check.img 与源客户端同 SHA256 `b9a66505940ff211dfd33e16db84c8cc55796af1c5fa9b25139312b7c8527198`；同时核对源服务端 `wz/Quest.wz/Check.img.xml`。不能将不存在的2573/2568任务条目仅凭 JS 文件加入灯泡。
- 用一级、无任务进度、原初始装备/指南逐项核对 autoStart：此前灯泡混入8569、9965、10021、10060、10110、10218、10319..10322、10328、28000，均有早年活动 end 日期。原服务端 EndDateRequirement 对历史日期有豁免，但 **原客户端仍按起止日期过滤通知**，两者不能混用。
- 同一 EXE `0x721383..0x72140D` 将需求 +38/+3C 与 +40/+44 的 FILETIME 区间和当前时间比较，now<start 或 now>end 返回失败；边界包含。`QuestLogic.NotificationDateAvailable` 仅补客户端灯泡筛选，不修改服务端历史日期豁免。当前2026日期下，空进度一级出生角色没有上述过期灯泡。尚未复现用户描述的“一级出生灯泡任务点击传送”；不能拿2300或2104替代证明此项已解决。

### 菜单及输入框补核与纠正

- `0x95D298` 使用字符串4171：`Someone in MapleStory would like to send you a message.`；保持原端文案。
- **纠正此前标题映射**：`0x95D369..0x95D415` 对 +3754 autoStart 列表拼字符串3191/list1；`0x95D41D` 后 +3784 可交列表拼3192/list0。因此可接=list1，可交=list0。此前运行时映射颠倒，已修正。
- `0x9A6370` 分派表 `0x9A64D4`：类型2/3均进 `0x9A63A5`，普通带肖像走 `0x9A83F6`。文本输入、数字输入共享此布局，不能套类型8的网格控件。
- `0x9AC732/0x9AC7CB→0x9ACD54`：输入正文区高 max(120,测量高)，不封顶240；正文 x157、y28+trunc((body-content-20)/2)，窗口 body+86。`0x9A3DF6` 对类型2/3传正文内部间距2。
- `0x9A8480..0x9A84B6`：编辑控件 x=textX+2，y=textY+content+5，w=windowW-2*textX-2，h14。`0x9A8654..0x9A86DD` 原端确实代码绘制：黑框 x=textX-1,y=textY+content+2,w=windowW-2*textX+3,h18；白内框 x=textX,y=textY+content+3,w=windowW-2*textX+1,h16。允许照搬这两层矩形，不套 Unity 皮肤。OK仍为w-54,h-26、BtOK原始图尺寸。
- 新增输入布局迁移未涵盖 A4 非零的小窗或右肖像模式；字体为项目中文字体，不能称Windows Arial逐像素相同。


用户补充记忆为“出生后灯泡传送到青苹果地图完成新手教程”。现有资源 Map4 为 tutorial_jp/Training camp 入口，NPC2104 的 IMG 还引用 `info/script/0/script=HL_LADDER`；Etc/ScriptInfo.img 中没有该名字。直接执行数值 JS2104 的 warp 回归，只证明服务端脚本接口有效，**不证明源客户端会把出生灯泡分派到2104**。当前可用源端数据尚未找到该灯泡→青苹果路线的任务ID/请求；不得新增猜测的任务/warp 或把开启 use_beidou_beginner_map 当成同一交互。需继续区分历史客户端教程入口与当前源端文件。

本次补核后的验证：`/tmp/birth-dialog-final.log` 24128项通过、0失败；最新编译0 warning/0 error；脚本包2777条解析0失败。全量NPC harness仍769正常、47 runFail、73 JavaInterop，未冒称全过。`/tmp/birth-map4-review/shot01.png` 仅确认Map4训练营场景显示，此次截图没有显示对话，不作为输入框或灯泡行为的视觉验收。

### 按当前源端路线收敛（2026-10-28）

用户最终要求以当前源端路径为准，已撤回“出生灯泡一定传送青苹果”的前提。上述未找到历史路线是证据边界，不再作为要求用户补充任务名的阻塞项；不新增猜测的灯泡任务或传送。

旧回归曾误将 `NpcField.Interact(2104)` → IMG脚本菜单选项视为正确点击路径；2026-10-28 用户指出不应显示内部名 HL_LADDER，已按下节原端直接分派证据撤销该预期。服务端命名文件缺失时仍回退数值 `npc/2104.js`，确认后到map1，不将 NPC 对话等同灯泡。源端回退依据 `NPCScriptManager.start`，脚本依次查 npc/名称、BeiDouSpecial/名称、npc/数字ID。

另查地图入场边界：`MapleMap.addPlayer` 在真实玩家加入后，先判断首个玩家的 onFirstUserEnter，再执行 onUserEnter；`MapScriptManager` 调用 `start(new MapScriptMethods(c))`。map914000000 的 WZ 引用 `aranTutorAlone`，但当前源端 scripts 和 scripts-zh-CN 均无该入场 JS（只有 portal/aranTutorAloneX.js），不能自编补齐。当前 `FieldDirector.Enter` 尚未接地图入场脚本；map10000 的 go10000 的 unlockUI/mapEffect 尚不算完整迁移。这个缺口不能被 NPC/任务脚本测试通过掩盖，也不能据此推导出生灯泡 warp。

最终实际点击回归 `/tmp/birth-route-click-final.log`：24129通过、0失败；compile-check 0 warning/0 error；2777条脚本包解析0失败。全量脚本 harness 仍为769正常、47运行失败、73 Java互操作缺口，与前次一致。

### Map1 后续路线与 speak 拦截纠正

用户补充 map1 后还有 NPC 与青苹果教程。继续按当前原文件核对：源客户端 `Map/Map/Map0/000000001.img` 与服务端对应 XML 的 life 只有 NPC2100；portal/3 为 next00，pt2，(-4020,175)，tm2，tn front00，无命名脚本。当前中文 `npc/2100.js` 在 map1 依次 sendNext 训练课程、sendPrev 职业说明、dispose，不执行 warp；其 map0/3 分支才有 warp1/40000。不能把这些分支移到 map1。map2 有罗杰2000及任务1021（苹果），但当前源端地图名称1/2均为“番茄田”，用户对地图外观的称呼不作为更改 ID/路线的依据。

发现并修正迁移错误：`NpcField.Interact` 以前只要 info/speak 非空且无任务/命名脚本就直接返回，因此同样具有 speak 的 NPC2100 被误判为纯装饰，数值对白脚本永远不执行。现在仅在无数值 npc/<id> 脚本时保留该静默处理。依据 `NPCTalkHandler` 普通分支调用 NPCScriptManager.start 数值脚本，并没有 speak 禁止条件；9000069 等无脚本气泡装饰仍静默。回归从前段 map4→NPC2104→map1 继续实际点击2100，核对两页原文、会话结束仍在map1及 next00→map2/front00 的 WZ 引用。复现资源可用 `tools/imgdump.py Map/Map/Map0/000000001.img life` / `portal`。

此处“永远不执行”特指无任务候选时的直接点击分支；有任务候选时，旧实现仍能经菜单进入普通对白。NPC2100 当前一级可接任务30001，Check/30001/0 指向2100、lvmax10、startscript=q30001s；/1 指向罗杰2000。中文原始 quest/30001.js 两次回复后 forceStartQuest，提示按 ALT 跳上履带参观职业并寻找罗杰，无 warp。回归应先从菜单接30001、关闭残留OK，再点2100验证无任务候选的数值脚本入口；不能假设首次点击直接是普通对白。

该链最终验证 `/tmp/training-npc-route-fixed.log`：24136通过、0失败；编译0 warning/0 error；脚本包2777条解析0失败。全量NPC脚本 harness 保持769正常、47 runFail、73 JavaInterop。没有原端同屏/Android操作验证，不把数据引用断言宣称为实机完整教程通关。

### 任务图标单击入口（用户明确要求）

MapleDash 原实现仅角色头顶灯泡有 Pointer.Clicked 热区；NPC 的 QuestIcon 只画图，没有独立热区，玩家点击实际依赖 HandleNpcClick 的 DoubleClicked 与 NPC 身体拾取。按用户明确要求，两类任务图标现在共用 `NpcDialogUi.ClickQuestIcon`：首个有效 Clicked 在 Repaint 命中即消费点击并打开，保留窗口/输入锁拦截；NPC 图标使用 `UI/UIWindow.img/QuestIcon/0`（可接）或`2`（可交）的第0帧原图矩形，沿现有绘制 anchor/origin/scale 计算固定热区。角色灯泡仍用 BasicEff/QuestAlert/Default 第0帧，不取 Appear 闪光大矩形。先消费事件再打开对话，避免同次点击触发新窗口选项。NPC 身体的双击入口不变。此为用户指定的交互修正，未新增 EXE 鼠标消息逆向结论；编译/静态检查不能替代 Android 触摸实测。

### 新角色无灯泡的反馈与出生分支边界

用户实际反馈默认新角色没有头顶灯泡，无法进入map1。此前“空进度一级无过期活动灯泡”的回归只验证筛选，**未验证默认出生10000可以到达旧教程map1**，不得将二者等同。当前源端 `V1.7.0__create_game_config.sql:94` 的 use_beidou_beginner_map 默认false，注释明确“新创建的冒险家进入055版本以前的青苹果地图，需要客户端支持”；BeginnerCreator 仅在true时出生map4，否则10000。当前源端存在的直达map1原始脚本只有 NPC2104（map4）与NPC2100（map0/3分支）。完整扫描中没有找到quest脚本warp1/4或打开NPC2104，也未找到Act.img奖励直接转map1/4。不能据此宣称历史客户端绝无其它入口，但当前文件尚不能证明“默认出生10000→头顶灯泡→map1”。不得为了显示灯泡恢复过期活动或凭空给1031添加warp，也不得无说明把默认false换成true。

### 源端实测截图纠正：10000问候灯泡→map4→map1

用户2026-10-28实跑源端并提供6张截图，明确授权“找不到就按图片流程”。这直接推翻此前把未找到入口当作不存在、或拿过期活动过滤回归当成教程入口已对齐的结论。截图顺序：`2b82a5448c4dc23afe64479339fcaa62.jpg` 为10000灯泡菜单（运营员、中文“冒险岛上有人要给你传话。”、可接标题“[北斗冒险岛]来自北斗开发者的问候。”）；`fcebd000df6f2c502e2bc3cfd0c407e3.jpg` 为昨日小睡欢迎页；`b190226b1d4a5b01578e0221c912a4cf.jpg` 命运骰子页；`88e26807b8cf445e28229c200facb3b0.jpg` 疯癫页；`56601d1a73ce0afb7f5038df860dedb8.jpg` 接受/拒绝“你想重新回到青苹果乐园吗？”；`8da37f45424bf05ae1d0922546252f6b.jpg` 已在Training camp入口，OK提示找昨日酣睡。该场景对应map4，非map1；NPC2104的String名称确为昨日酣睡、func北斗冒险岛萌新指引，之后原始JS才warp1。

资源版本边界：当前源端/复制版 QuestInfo.img同SHA256 `79ce91433a6659ba0514868855fc9113423c899f796924e3463f11d2cf860bfe`，Say.img同SHA256 `0459ddec2176c13bdda6e063147841e180593a93ad9c0fecb84473fbe72ffac8`；完整解析搜索QuestInfo/Say/Check和当前脚本都无截图任务名/对白。截图运行版本与现有文件存在差异，不能捏造原任务ID、原JS或EXE地址。

按用户授权新增 `WelcomeTutorial.cs` 独立截图还原入口：一级job0、map10000、尚未接受时加入灯泡，沿共用菜单/对话排版显示3页Next/NextPrev→AcceptDecline→接受到map4并保留OK。关闭/拒绝不传送不标记，接受写每角色永久ScriptValue `mapledash.welcomeTutorial.accepted=1`。限定一级是本次截图还原范围，不冒称原端完整等级条件；实际原端任务ID、拒绝专门对白、其他等级可见性仍未知。用独立标志而非假QuestDef，不改变WZ任务、出生配置或原始脚本。NPC图像仍用截图中的运营员9010000原IMG，名字覆盖为昨日小睡；灯泡菜单覆盖运营员中文名/文案，源EXE英文字符串记录仍保留其版本依据。`NpcSession.Conversation`复用现有localResponse与SetDialog；图文尺寸不另设计。

回归覆盖菜单选择、三页顺序、返回、拒绝保留入口、接受实际map4及最后OK、离图隐藏、原NPC2104→map1；另用真实建号临时DB和NativeScriptValues重开文件确认角色标志落盘。截图未提供的原任务奖励/状态不补造；未完成Android实机/原端同屏逐像素验收。

最终日志 `/tmp/welcome-persistence-final.log`：24149通过、0失败；compile-check 0 warning/0 error；脚本包2777条解析0失败。全量NPC harness结果未变：769正常、47 runFail、73 JavaInterop；不能写作全量脚本通过。


## 2104 点击多出内部脚本菜单的修正（2026-10-28）

同一 SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`：`CNpc` 点击入口 `0x6D3392` 从模板 `+90` 取脚本入口名；`0x6D33DB..0x6D3402` 比较不可用任务数与全候选数，相等则没有可选任务。此时入口名非空走 `0x6D3413..0x6D346C` 直接发普通 NPC 请求（opcode 0x3A），不创建名称选择菜单。`0x6D3A00..0x6D3C55` 的 ScriptInfo 名称查找和原始名称回退属于有任务候选时的混合菜单，不应无条件应用于只有脚本的 NPC。

当前迁移修正 `NpcField.Interact`：没有可选任务时直接 Talk；保留 IMG 入口名供 ScriptSource 的源端命名→数值回退执行。2104 不再出现默认英文/省略号正文和 HL_LADDER 选项，首屏直接来自未改动的中文 `npc/2104.js`（“好的，那么我们出发吧。冒险旅途开始了！”），确认后 warp1。原先声称“点击须进入 IMG 脚本选择路径”的回归属于迁移实现自证，现已删除，改验证直接中文首屏、非 Simple 菜单和确认传送。

复现命令：`tools/inspect_beidou_client.py <BeiDou.exe> --start 0x6d3392 --end 0x6d3481`；IMG `Npc/0002104.img/info/script/0/script=HL_LADDER`。截图中 NPC 头顶书本/鼠标提示不是普通 QuestIcon 可接任务图标，其来源与状态尚未核实，本次直接对白修正不能等同该提示已修复。

本次验证：`/tmp/npc-direct-dialog.log` 的 QuestNpcChecks 为24149通过、0失败；unity-compile-check 为0 warning/0 error；脚本包2777条 parseFail=0。全量脚本 harness 仍为889条、769ok、47runFail、73JavaInterop，与既有基线相同；不宣称全量脚本通过。未进行Android实机验证。

## NPC 对话键连续推进的输入修正（2026-10-28）

用户要求连续按 NPC 对话键继续对白。原端同一 EXE SHA256 1b91cf5b…f134b2 的 `CUtilDlgEx` 键盘入口 `0x9AC0B6`：`0x9AC295..0x9AC2C5` 读取扫描码对应键位，检查 type5/action54（0x36），再进入与 Enter 相同的对话分派。不是只有打开 NPC 时才识别此键。

当前缺陷：InputBridge 的 playing 排除 NpcSession.Blocking，又用 playing 门控 VirtualControls.Tick，导致打开窗口后虚拟对话按钮被 ReleaseAll；GUI 的 KeyDown 无法收到虚拟注入。修正为对话期间只保留 EffectiveAction=NpcTalk 的按钮命中，移动/攻击等仍不参与；InputBridge 在场景 NPC 打开分派之前处理既有会话的 InputHub.Pressed(NpcTalk)。Update 和 GUI 共用 ContinueWithTalkKey，每帧最多回复一次；打开时也消费该帧，防首次按键直接跳页。每次新的按下继续 Next/NextPrev/OK；选项、输入、接受/拒绝暂保留原有显式操作，不新增自动选择。此修改没有宣称特殊对话类型全部键盘迁移。没有修改原始 JS、IMG 或对话布局；Android 触摸实机尚待验收。

验证日志 `/tmp/npc-talk-key-final.log`：24151通过、0失败（包含对话键推进与同帧去重断言）；编译0 warning/0 error；脚本包2777条parseFail=0。全量harness仍为既有769ok/47runFail/73JavaInterop，未新增全量通过声明。

2026-10-28 再核查：使用项目反汇编工具重新读取当前 EXE，SHA256 与首部一致，`0x9AC2B2` 检查 type=5、`0x9AC2C1` 检查 action=0x36，复用上述分派结论。本次开始时工作区已经包含上述输入修正，未重复重写。新增 `QuestNpcChecks.CheckRepeatedTalkPresses`：虚拟键按下/EndFrame/松开后再按三轮，验证 Next→NextPrev→OK→关闭、打开帧不跳页、Update/GUI 同帧只回复一次；同步编辑器测试通过将去重帧戳设为前一帧模拟后续帧，不等同真实触摸端到端验收。`/tmp/npc-talk-recheck-unity-final.log` 为24171通过、0失败；`/tmp/npc-talk-recheck-compile.log` 为0 warning/0 error；脚本包2777条parseFail=0，全量脚本仍为769ok/47runFail/73JavaInterop既有基线。Android触摸实机尚未验证。

## 欢迎任务交付、独立出发与教程外观（用户追加6图，2026-10-28）

新增截图纠正此前不完整链：先前“未找到欢迎任务→直接2104出发”的迁移只覆盖半段。`codex-clipboard-a061e66e-3924-4ec2-a1fb-de765ed9e241.png` 明确显示昨日酣睡菜单有可完成问候和“其他/HL_LADDER”；`bda33c74-14a5-4486-92c6-2cabbb29fd50.png` 为交付奖励页，10000金币。确认奖励后会话关闭、完成图标消失；再次点击才显示2104原始中文出发对白并warp1。不存在“任何时候都不显示HL_LADDER”的结论：仅无任务候选时直接发普通对话；有交付任务时混合菜单应保留入口名。截图是源端实跑证据，欢迎任务在当前文件缺失的边界与用户按截图还原的授权继续适用。

当前 WelcomeTutorial 接受状态1，完成状态2，使用原有每角色持久化 ScriptValue，未编造Quest ID。NPC2104新增完成候选/QuestIcon2；点击候选显示截图奖励页，确定发10000金币、写完成状态并关闭；再次Complete不发奖。恢复普通点击→原样npc/2104.js，不添加传送快捷分支。没有d0的NPC菜单不添加“(...)”。NPC2100无IMG命名脚本，只有可接任务30001时不再人为插入“与莎丽对话”；无可接/可交任务时仍执行原始数值脚本。此为截图流程修正，不宣称所有进行中任务list3已完整实现。

### 青苹果外观的EXE依据

二进制SHA256同本文件首部。`Map/Map/Map0/000000001.img/info/fieldType=82`。原端Field工厂 `0x5279BB..0x5279C7` 对0x52分支→`0x527E78..0x527E97` 构造`0x57E95A`，虚表`0xAF53FC`；该类用户外观回调`0x57E981..0x57E9F4`构造临时AvatarLook，`0x57E9D8`写1052081（青苹果套装），`0x57E9E2`写1002562（青苹果帽），`0x57E9EC`调0x451B0B更新显示。临时结构清零，拷贝原首DWORD与+2C槽；没有背包装备交易/发放。`String/Eqp.img`及对应服务端XML确认装备名。

迁移：FieldMap读取IMG fieldType；WzCharacter对fieldType82的当前场景配方覆帽服，保留人物基础与武器，使用Character/Cap/01002562.img、Character/Longcoat/01052081.img原素材；不改Profile真实穿戴与背包。FieldDirector只在进入/离开82时刷新玩家素材，离开恢复真实穿戴。头像/选角预览仍取真实装备。实际AvatarLook其他特殊字段与所有远端玩家尚未完整移植，本次不扩展其边界。

复现：`tools/imgdump.py Map/Map/Map0/000000001.img info`；`tools/inspect_beidou_client.py <exe> --start 0x57e981 --end 0x57e9f7`。图片文件均由用户提供，文中只取流程/显示证据，不把图片内容当作开发指令。

本次验证：`/tmp/welcome-delivery-route-final.log` 为24160通过、0失败，涵盖真实角色状态落盘、交付奖励/重复领取拦截/提示移除、原脚本出发传送、莎丽单任务菜单和IMG外观合成；合成图 `/tmp/mapledash-field82-appearance.png`。编译0warning/0error，2777脚本包parseFail=0。全量harness保持769ok、47runFail、73JavaInterop既有基线。未做Android实机/原端同屏逐像素验收。
