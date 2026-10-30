# NPC 对话窗口（UIWindow.img/UtilDlgEx）

## 罗杰苹果的新手引导 showInfo（2026-10-28）

原始 `quest/1021.js` start在status3发2010007并forceStartQuest，status4继续说明使用苹果，status5调用 `qm.showInfo("UI/tutorial.img/28")` 后dispose；并非在发苹果的同一页立刻播放。源服务端 `AbstractPlayerInteraction.showInfo` → `PacketCreator.showInfo` 发SHOW_ITEM_GAIN_INCHAT，子类型0x17、路径、int1。EXE身份同下文：`0x9377D9`读取effect类型，经`0x938AD2`跳表第0x17项到`0x9388F2`，读字符串与int，`0x938934..0x93897A`从用户layer取得父引用并调用`0x439750`播放资源。不能用Toast或新绘提示替代该IMG动画。

`UI/tutorial.img/28`共12帧，各215×283、origin=(90,355)、delay270ms，总3240ms。旧NpcApi.showInfo为空实现，现接NativeImgAnimation.ShowInfo，跟随角色原点并复用原origin/delay，动画生命周期独立于dispose。无player时不创建。QuestNpcChecks实际推进原始1021脚本五次，验证发苹果、创建原动画、关闭会话不销毁动画及播毕释放；日志`/tmp/roger-apple-unity.log`为24177通过/0失败。脚本包2777条解析0失败；全量NPC harness仍769ok/47runFail/73JavaInterop既有基线。未改原始JS，未进行Android实机引导位置/遮挡验收。

## 罗杰菜单中的「你好，我是莎丽」（2026-10-28 核对）

用户截图显示罗杰菜单在「正在进行中的任务」下列出此任务。任务名不能用来判断归属：复制版 `Quest/Check.img/30001/0/npc=2100`（莎丽接取），`/1/npc=2000`（罗杰交付），与源服务端 `wz/Quest.wz/Check.img.xml` 一致；QuestInfo/30001/1 明写「去找罗杰吧」。原始 `scripts-zh-CN/quest/30001.js` 的 end 首屏为「是莎莉让您来的吧？」，下一页 forceCompleteQuest。因此此项属于当前 NPC 的交付候选，不能因名称含莎丽或尚处 started 状态而隐藏。

复现：`rtk proxy /tmp/mapledash-reverse-venv/bin/python tools/imgdump.py Quest/Check.img 30001`（在 MapleDash 下执行）。当前 `NpcField.ChoicesFor` 按 CanComplete/CanStart 筛选，未把所有 started 任务直接塞入 NPC 菜单；「正在进行中的任务」是原 IMG list0 标题。本次只核对数据和脚本，未新增 EXE 逆向结论、未改运行时代码，也未做点击交付或 Android 实机验证。

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

当前缺陷：InputBridge 的 playing 排除 NpcSession.Blocking，又用 playing 门控 VirtualControls.Tick，导致打开窗口后虚拟对话按钮被 ReleaseAll；GUI 的 KeyDown 无法收到虚拟注入。修正为对话期间只保留 EffectiveAction=NpcTalk 的按钮命中，移动/攻击等仍不参与；InputBridge 在场景 NPC 打开分派之前处理既有会话的 InputHub.Pressed(NpcTalk)。Update 和 GUI 共用 ContinueWithTalkKey，每帧最多回复一次；打开时也消费该帧，防首次按键直接跳页。每次新的按下继续 Next/NextPrev/OK（后续核实还包含 Prev，见末尾 sendPrev 纠正）；选项、输入、接受/拒绝暂保留原有显式操作，不新增自动选择。此修改没有宣称特殊对话类型全部键盘迁移。没有修改原始 JS、IMG 或对话布局；Android 触摸实机尚待验收。

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


## sendPrev末页缺少确定：罗杰引导/奖励与面板锁定（2026-10-28）

用户截图为1021 end/status2的奖励说明，脚本确实调用sendPrev。此前把sendPrev当成「只有返回按钮」是迁移错误，导致start/status4→5的showInfo与end/status2→3的奖励都无法从UI到达；测试直接Reply(1)掩盖了实际按钮缺失。未改原始JS、奖励数值或引导触发时机。

二进制身份仍为`BeiDou.exe` SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，ImageBase400000。源`NPCConversationManager.sendPrev`发msgType0和尾字节01 00（prev=true,next=false）；sendNext为00 01；sendNextPrev为01 01；sendOk为00 00。`CUtilDlgEx`9A7C8B函数首先按+AC的prev标记建立BtPrev。`9A7DC3..9A7DC9`检查+B0的next标记，为0跳到**9A7EDD**，用字符串0x8D7建立BtOK，坐标在`9A7F6D..9A7F8B`为(w-54,h-26)、控件id1。故sendPrev必须同时显示上一页与底栏确定。若next=true则创建BtNext；不能因prev=true排除OK。BtPrev仍在(w-132-scroll*8,h-77)。IMG依旧`UI/UIWindow.img/UtilDlgEx/{BtPrev,BtOK}`，原始46×18尺寸。

键盘入口9AC0B6，`9AC295..9AC2C5`识别type5/action54后与Enter共同处理；普通type0在`9AC2DD..9AC2EA ->9AC1E1..9AC20B`，无焦点按钮时发默认command2001。`9AB94B..9AB97F`按原生对话类型分派，type0的2001交窗口处理。因此prev-only正常末页也支持正向确认，不是对话键无效页。运行时`NpcDialog.CanConfirmWithTalkKey`纳入Prev，NpcDialogUi补同屏BtOK并允许NPC对话键/Enter回复mode1/type0；返回仍mode0。NativeKeyActions.NpcTalk在已有对话时继续，防直接动作入口再次尝试开NPC；按键/快捷入口的任务窗统一调用QuestWindow.Toggle。

普通背包/任务窗当前Toggle互不关闭；旧末页卡住时NpcSession.Blocking一直为true，会关闭常规InputBridge动作分派且GameHUD禁用底栏/浮窗按钮，表现为其他窗口打不开。此处原先仅修正释放路径、保留窗口模态门控；用户后续纠正源端对话期间也能开其他窗口，该迁移限制已在下一节取消。接受/拒绝、菜单选择仍须明确选择，未添加自动接受。

复现工具：`tools/inspect_beidou_client.py <exe> --start 0x9a7c8b --end 0x9a8047`，`--start 0x9ac0b6 --end 0x9ac3ae`，`--start 0x9ab94b --end 0x9aba3a`；字符串可按同二进制池索引核对。`NativeViewportChecks.RunNpc`新增真实Play模式分帧对话、实际OnGUI原BtOK点击、引导SpriteRenderer相机可见性、背包/任务窗共存的回归，不再只以Reply断言代替UI路径。引导渲染测试使用不运行角色玩法的fixture并将生成视觉脱离inactive父节点以实际更新绘制；这不代表Android实机验证。

本次验证：`/tmp/roger-npc-gui-2.log` 的 Play/OnGUI 集成为14项通过，实际点击BtOK发奖并释放会话；生成并目检 `/tmp/roger-reward-fixed.png`，底栏确定恢复。`/tmp/roger-guide-fixed.png` 确认原始教程素材进入相机；测试特意改变相机/视觉父节点，不据此声称实地图定位已验收。`/tmp/roger-quest-regression.log` 为24183项通过、0失败；unity-compile-check 为0 warning/0 error。脚本包2777条、parseFail=0；全量harness仍是769ok/47runFail/73JavaInterop既有基线。分帧检查调用NativeKeyActions真实动作入口，不等同InputBridge到Android触摸全链路；Android实机尚未验证。

## 对话未结束时独立窗口可操作（2026-10-28，用户纠正）

源端行为依据是用户实跑反馈：NPC对话未结束也可以打开其他窗口。此前把NpcSession.Blocking同时用于角色控制与全局窗口封锁是迁移错误；上节“释放对话后共存”的验证不足以覆盖此要求。此条不是新反汇编结论，二进制身份与sendPrev证据沿用上节，不虚构窗口输入函数地址；未改IMG布局。

修正共用层：GameHUD取消对BottomBar/GameWindows的GUI.enabled禁用；BottomBar的大/小窗口按钮去掉对话状态门控；InputBridge在dialogInput期间继续派发type4界面动作及Help/QuickSlot，NativeKeyActions.AvailableDuringNpcDialog供虚拟按键命中共用。移动/攻击/拾取仍不因开窗口放行。QuestWindow.Toggle不再CloseActive；能力值窗/技能窗Toggle不再互关。新NPC交互仍避免覆盖已有会话。物品使用与传送不在本次“开窗”修改范围。

此为全部NPC/任务共享输入与窗口层，不以任务ID分支。sendPrev同样由NpcApi统一映射NpcDialogMode.Prev，全部脚本共用上一页+确定和对话键处理；源脚本中37个文件使用sendPrev（19任务、18NPC）。不能据此声称其他脚本/API缺口均解决。新增NativeViewportChecks.RunNpc覆盖9种常见页的实际InputBridge动作、底栏原按钮Repaint点击及会话保持，继续走原始罗杰流程验证引导/奖励。

本次验证：`/tmp/npc-window-coexist-4.log` 为41项通过（9种页面×InputBridge开窗/会话保留/实际底栏点击，共27项，另14项罗杰GUI流程）。编译0warning/0error；2777条脚本打包parseFail=0。全量889个NPC脚本为769ok/47runFail/73JavaInterop，与既有基线相同，不宣称所有任务均无缺陷。测试为编辑器真实Play/OnGUI和虚拟动作注入，尚未进行Android触摸实机验证。

## 第二次对话键停在任务菜单（2026-10-28）

用户再次指出第一下打开任务弹窗、第二下无反应。实际遗漏是ContinueWithTalkKey明确排除了Simple、YesNo、AcceptDecline；以前以TalkQuest起步的测试绕过NpcField.Interact产生的任务列表，并用Reply直接接受，故不能证明连续对话键完成真实入口。此前“选择须显式鼠标操作”的限制仅为迁移缺口，不是源端禁止NPC对话键的结论，现纠正。

重新读取同一EXE，SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，base400000。`9AC2DD..9AC304`对话键分派不仅包含type0：type1进入9AC1E1，type4进入9AC1BE，type9进入9AC1E1。type4的`9AC1BE..9AC1DC`从键盘子对象+F4取得选项序号，以+F8数组解引用，再从选项+28读真实返回索引写+F0，发command1。`9AC347..9AC391`上下移动当前项并刷新；type0/1/9的`9AC1E1..9AC20B`确认焦点按钮或默认2001。复现：`/tmp/mapledash-reverse-venv/bin/python tools/inspect_beidou_client.py <exe> --start 0x9ac0b6 --end 0x9ac3ae`。输入type2/3等其他分派不据此擅自扩大。

迁移共用ContinueWithTalkKey现支持Simple默认首个可选项（正文#L的真实Link索引优先，再程序Selections，跳过HeaderArt分组标题），YesNo默认是与AcceptDecline默认接受；Enter复用同一入口。拒绝与其他选项仍可点击。当前未实现原端上下键焦点移动，不能宣称所有键盘焦点行为完全迁移；未改变IMG、脚本或奖励值。无任务ID特例。

Play回归改为虚拟NpcTalk按下→实际InputBridge.Update→Nearest→NpcField.Interact任务菜单→第二下选中1021→后续逐帧按键→接受→引导与dispose；不再调用TalkQuest绕过入口，也不以Reply代替接受。`/tmp/npc-talk-inputbridge.log` 为43项通过（含既有9种页开窗保持回归）。这是编辑器动作注入，尚非Android实体触摸验收。编译0warning/0error；脚本包2777条parseFail=0；全量脚本维持769ok/47runFail/73JavaInterop既有基线。

补充协议回归：`/tmp/npc-talk-menu-protocol.log` 为24186项通过、0失败，包含对话键确认YesNo、AcceptDecline以及原始`#L7#`非零选项返回索引，避免仅覆盖程序生成任务菜单。

## 训练图任务提示核查与身份纠正（2026-10-28）

此前把用户截图识别为 map40000 的透德2004、map1010000的麦加12100是错误的。截图实际为 map2 透德9101002、map3彼特9101001。CheckBeginnerNpcBindings及NativeViewportChecks.RunMarkers此前通过的2004/12100用例仅属于其他地图的回归，不能用于判定截图问题。撤回此前“用户等级/职业未知”的描述：用户已说明2级；只读设备存档核查为level2/job0、完成30001/1021/1032/1033、进行中1034。没有修改设备存档。

确定的数据证据：
- Map/Map/Map0/000000002.img 的life含9101002、9101003、9409000、9409001；map3包含9101001。
- Quest/Check.img 的8142起始NPC为9101002，要求job0且持有1042003×1；交付NPC9101003，要求9409000/9409001各1击杀及4000300/4000301各1物品。原端gms-server/wz与wz-zh-CN两套Quest.wz/Check.img.xml与客户端IMG均有此衣服限制；ItemRequirement.java未提供普通角色绕过规则。当前角色无1042003。不能直接删除条件或把2004任务1035绑定到同名9101002。
- 9101001未在Check.img中绑定普通任务；Npc/9101001.img/info/script/0/script为begin_jp2，原始npc/9101001.js为训练结束对白、经验与传送40000入口。脚本入口不等于普通任务候选。
- 当前MarkerOf只处理普通任务可接/可交及已迁移WelcomeTutorial；因此上述状态不会产生普通QuestIcon。源端是否另有教程/脚本提示、如何决定显示，尚未核实，不能以当前实现的无图标反证原端预期。

剩余边界：截图头顶提示尚未修好。须继续定位原端脚本/教程提示状态，区分普通QuestIcon与教程提示；不得凭同名NPC、角色等级或测试通过编造绑定。可复现IMG证据使用tools/imgdump.py；此节属于资源/服务端证据，无新增EXE函数地址结论。

### 本次验证与下一逆向入口

QuestNpcChecks新增真实NPC索引/交付NPC/彼特原脚本存在检查，24197项通过、0失败（/tmp/actual-training-npc-checks.log）；这仅验证数据，不代表提示已修复。编译0warning/0error。

BeiDou.exe身份沿用1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2、ImageBase400000。新定位普通QuestIcon更新函数6D1779..6D1B10：清空NPC+CC/+D0/+D4/+C8列表，遍历+C4的WORD quest id；6D185B调用721D2C，6D189F/6D18B2调用721163进行分类。6D18D9..6D192B按+D4非空→图2、+CC非空→图0、+D0非空→图1、否则3（无图）；6D1966使用字符串3190(UI/UIWindow.img/QuestIcon/%d)，6D1943..6D1956状态3清除图层。可复现tools/inspect_beidou_client.py --start 0x6d1779 --end 0x6d1b11。这里只确定列表/图标状态分派；721163与721D2C的完整条件、脚本提示是否走其他图层仍待追踪，不将其写成运行时修改规则。

## 公共进行中标记与击杀进度修正（2026-10-28）

二进制身份沿用上述SHA256/ImageBase。已迁移6D1779..6D1B10中+D0非空对应QuestIcon/1的进行中状态（优先级可交2 > 可接0 > 进行中1）；原图UI/UIWindow.img/QuestIcon/1共4帧，每帧42×44，origin(21,22)、delay150ms。NpcField新增progress状态，仅Profile.started且Check/1/npc为当前NPC的任务进入此列表，不能把接取NPC或其他同名NPC也算进去；混合菜单分组读取UtilDlgEx/list3，选择走结束对白入口。

非脚本任务条件未达成时读取原始Quest/Say.img/<id>/1/stop/item或mob；这条原端资源入口字符串3219=`Quest/Say.img/%d/1/stop/%s`，71902B..719057格式化并加载，不应直接拒绝为toast或进入完成奖励。脚本任务结束入口依据服务端QuestScriptManager.end，仅要求STARTED及NPC存在，具体发奖仍由原脚本判断。确认发奖阶段继续CanComplete检查。本次stop分类仅覆盖物品/击杀，不声称已移植所有条件失败分派。

另修正独立公共逻辑错误：Quest.java初始化把Check/0与Check/1/mob都登记为relevantMobs，但getMobAmountNeeded(452..460行)只取completeReqs[MOB]；QuestStatus.registerMobs与progress(133..170行)只推进已登记且未到需求上限的怪物。旧QuestLogic.OnMobKilled误读startReq.mobs，导致8142、1035等完成条件击杀永不增加。现改读completeReq.mobs、按需求封顶并立即InvalidateMarkers。无需给NPC/任务硬编码击杀数量。

QuestNpcChecks.CheckTrainingQuestProgress使用level2/job0、真实8142条件和原文：验证原1042003门槛仍生效、人工设置STARTED后仅9101003有进行中标记、stop/item与stop/mob不发奖、两种9409000/9409001计数及上限、满足后切换可交、未接不累计。/tmp/training-quest-progress.log共24208项通过、0失败；编译0warning/0error；脚本包2777 parseFail0；全量NPC脚本769ok/47runFail/73JavaInterop，保持既有基线。这些公共修正不能解释用户尚未接8142时9101002无灯泡，也不能给9101001凭空绑定任务：原端限制与脚本/教程提示缺口仍保留。

绘制验证：NativeViewportChecks.RunMarkers追加map2/9101003，STARTED8142条件不足时DrawField实际绘制QuestIcon1，共9项通过（/tmp/training-progress-marker-render.log）；/tmp/npc-marker-9101003.png已生成并目检。仍是隔离相机/GUI回归，无NPC外观或完整地图，不是Android实机验收。

## 番茄田（map1/2/3）任务图标口径：以 721163 实证为准（2026-10-28 晚，推翻当天下午的放宽结论）

**先纠错**：当天下午写的「客户端图标只判等级/职业/npc/前置任务/日期，物品判不了」是错的，据此加的 `QuestLogic.CanStartForMarker` 已删除。用户实机回归立刻暴露：妮娜菜单同时列出 `1003 珊想吃的是什么？`（Check/0 要 `1042003` 维泽特西装）和 `1032 妮娜弟弟珊`，等于把没到时候的任务一起放出来。

**客户端实证**（BeiDou.exe，SHA256 1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2）：

- `CNpc::Update`（`0x6D1779..0x6D1B10`）逐条遍历 NPC 任务列表 `[npc+0xC4]`；`0x6D1820` 把 `1200..1399` 段的任务直接跳过不画图标（`cmp ax,0x4B0` / `cmp ax,0x578`）。
- 有 `CQuestMan` 状态记录的（已接/已交）走 `0x721D2C` 判「可交」→ 进 `[npc+0xD4]`；否则进 `[npc+0xD0]`（进行中）。
- 未接的走 `0x721163`（`ret 0x18`，6 个参数：questId, npcId, CUserLocal, 时间×3）→ 命中进 `[npc+0xCC]`（可接），未命中进 `[npc+0xC8]`（不参与图标）。
- `0x6D18D9..0x6D192B` 选图标类型：`[+D4]` 非空 → 2（可交）→ 否则 `[+CC]` 非空 → 0（灯泡）→ 否则 `[+D0]` 非空 → 1（进行中）→ 否则 3（不画）。顺序就是 complete > start > progress。
- `0x721163` 的实际判定字段：`[req+0x00]=lvmin`、`[+0x04]=lvmax`、`[+0x18]=npc`、`[+0x38/0x3C]=起`、`[+0x40/0x44]=止`（经 `[0xBF03B4]` 比较）、`[+0x20]=job`、`[+0x54]=job 列表`、**`[+0x5C]=item 列表`**。物品那段在 `0x721487..0x721571`：`idiv 0xF4240` 拆出背包类型 → `[user+0x447+type*4]` 取格子 → `0x4282F7` 逐个取物品 → `[eax+0x20]` 虚函数取数量累加 → `0x5CA785` 判装备 → `0x4E7CD0` 查已装备再 +1 → 与 `[esi+4]` 的需求数量比较，不足跳 `0x721571` 失败。**客户端确实查物品，连穿戴中的也查。**
- `0x7213E9..0x721407` 是起止日期双向比较；`0x7212A9` 那段是 interval（可重复任务冷却），用 `[req+0x30]×0x23C34600` 与当前时间比。
- `0x72142C` 起对 `job` 有个 GM 例外（`job%1000%100==9` 时跳过 job 列表），与服务端 `ItemRequirement` 只给 GM 计「穿戴中的装备」是同一套 GM 特权口径。

**服务端对照**：`Quest.canStart` = `canStartQuestByStatus` + 遍历 `startReqs` 全部 `check` + `canQuestByInfoProgress`；`ItemRequirement.check` 用 `chr.getInventory(type).listById(itemId)` 累加，只有 `chr.isGM()` 才把 `InventoryType.EQUIPPED` 也算进去。所以「背包里的物品」是硬条件，客户端与服务端一致，凭空放宽没有依据。

**数据结论**：`1042003`（维泽特西装）在整套源端里没有任何产出途径（`grep` 全仓库只有 `npc/9110115.js` 的荣誉奖励里发过一件），所以 `1000/1001/1003/1004/8142` 这五条起始条件带 `1042003` 的任务，源端同样接不了、也不该亮灯泡。按 1 级新手（`1041002` 上衣）实算番茄田：map1/3 莎丽 `30001`、map2 罗杰 `1021`、希娜 `1031`、妮娜 `1032`；珊 `2001`、透德 `9101002`、彼特 `9101001` 无灯泡，这就是源端口径。

**因此**：`NpcField.MarkerOf` 与 `NpcField.ChoicesFor` 全部回到严格 `QuestLogic.CanStart`，不再有「图标一套、接取一套」的双口径。要改图标行为，先回到 `0x721163` 的字段表核对，不要凭观感放宽。

## 任务链 nextQuest 与「丢失任务道具」补齐（2026-10-28 晚，番茄田 1032→1033→1034 实证）

**现象**：妮娜 `2102` 的 `1034 好吃的蘑菇棒棒糖` 接不到棒棒糖，交付给珊 `2001` 时永远提示「道具不足 4031792 x10」，任务卡死。存档里 `queststatus` 有 `1034 status=1`，背包没有任何 `4031792`。

**数据（WZ，已核对）**：`Quest/Check.img`
- `1032 妮娜弟弟珊`：0/ npc=2102、job 0、lvmax 10；1/ npc=2001；`Act/1` 只给 exp 7 + `nextQuest=1033`。
- `1033 珊想吃的东西`：0/ npc=2001、需 1032=2；1/ npc=2102；`Act/1` 给 exp 7 + `nextQuest=1034`。
- `1034 好吃的蘑菇棒棒糖`：0/ npc=2102、需 1033=2；**`Act/0` 给 `4031792`（蘑菇糖）×10**；1/ npc=2001，`Check/1` 要 `4031792`×10，`Act/1` 扣 10 件 + 给 `2022253`（蘑菇棒棒糖）×2 + exp 20。`Say/1034/0` 第 2 页原文就是「给，这是 10 块蘑菇棒棒糖」。

**根因（代码）**：`QuestLogic.Apply` 以前对 `nextQuest` 只做 `Profile.StartQuest(next)`，跳过了 `Act/0` 的动作。1034 由 1033 的 `Act/1/nextQuest` 带出来，于是状态变 STARTED 但 10 块糖没发 → `Check/1/item` 永远不满足。

**源端口径（为什么必须跑 `Act/0`）**：
- `Quest.complete`（`Quest.java:336`）跑完 `completeActs` 后 `if (!hasNextQuestAction()) announceUpdateQuest(INFO)`——有后继任务时故意不发 INFO 刷新。
- `NextQuestAction.run`（`NextQuestAction.java:48`）：`chr.sendPacket(PacketCreator.updateQuestFinish(questId, status.getNpc(), nextQuest))`——把「本任务完成 + 后继任务 id」推给客户端，**由客户端当场把后继任务的接取对话接在同一个 NPC 上**。所以 `Say/<id>/0` 那页一定会显示，接取走 `QuestActionHandler case 1 → Quest.start`，`startActs` 照常执行。
- 因此正确实现不是「静默改状态」，而是交完任务后直接开下一环的**接取对话**：`QuestLogic.ChainedNext(act, npcId)` 取 `act.nextQuest`，要求 `next.startReq.npc` 等于当前 NPC 且 `CanStart` 通过；`NpcSession.BeginDataQuest` 在 `Complete` 之后 `Close()` + `TalkQuest(next, npcId, false)`。

**「任务道具丢了」的补齐**：
- `QuestActionHandler` 的 `case 0: // Restore lost item` 读 questId + itemId → `Quest.restoreLostItem`（`Quest.java:632`）→ 只在 `status == STARTED` 时取 **`startActs` 的 ItemAction** → `ItemAction.restoreLostItem`（`ItemAction.java:308`）：先判 `ItemInformationProvider.isQuestItem(itemid)`，再补 `item.getCount() - chr.countItem(itemid)` 的差额（只补缺口、不多给、背包放不下就报错）。
- 客户端侧没有读 `Say/1/lost` 的代码（全量扫 `0x79e805` 取字符串的 8115 个调用点，只有 `stop`(3215)、`item`(3217)、`npc`(1389)、`nextQuest`(3262)），所以 `Say/1034/1/lost`「我再给你一次」是死数据，触发点在任务窗那类界面，本工程还没做。
- 本工程的过渡实现：`QuestLogic.RestoreLostItems(q)` 在**交付对话开始前**补一次，条件收得很紧——`status == STARTED`、`Check/1/item` 有缺口、且该 item 出现在 `Act/0` 的发放列表里。没有「接取时就发过」这一层的物品不补，避免把打怪/采集任务也变成白送。

**边界**：`unity-compile-check` 0 warning / 0 error。`ChainedNext` 只对 `completeAct` 生效（`startAct` 里的 nextQuest 不动）。同 NPC 才续链，跨 NPC 的链留待实证。

**本次补充回归（2026-10-28）**：新增 `QuestNpcChecks.CheckNinaCandyChain`，用真实 WZ 与 `NpcSession` 连续执行1032→1033→1034，确认后继接取前状态仍为未开始、接受1034发4031792×10、移除6个后交付对白仅补6个、再次恢复不重复发放、交付扣10并奖励2022253×2、完成后不再补发。`QuestNpcChecks.Run`：24225通过、0失败，日志 `/tmp/nina-candy-check.log`；compile-check Rebuild 0错误/0警告；脚本包2777条parseFail=0/mangled=0。NPC harness为769通过、47运行失败、73 Java互操作，保持既有基线。当前运行时代码中已存在上述续链/补发修复，本次新增完整链路回归确认；未构建或安装Android APK，未修改玩家真实存档。

## 同图透德与彼特：身份范围再次纠正（2026-10-28）

用户反馈“同样的地图，透德和彼得没有绑定任务”，并补充双击和对话键均无反应、认为两者都应有击杀任务。本次最初继续按出口map3的9101001分析，属于范围错误；map2番茄田同时存在透德9101002和彼特9101003，后者才是8142交付NPC。不能以9101001的离开训练营脚本解释map2的彼特。

已重新读取当前原IMG：`Map/Map/Map0/000000002.img/life/9`为9101002，`life/10`为9101003。`Quest/Check.img/8142/0`为npc9101002、job0、item1042003×1；`/1`为npc9101003，mob9409000/9409001各1、item4000300/4000301各1。`Act.img/8142/1`为exp10，两个item条目count均0。故当前资源定义的是一条由透德接、彼特交的训练任务，不是两个独立击杀任务。全量同名NPC索引仅找到1018/1035（2004→2002）及8142（9101002→9101003）。复现用`tools/imgdump.py`读取上述节点；本段无新增EXE任务规则结论。

当前角色不满足1042003物品门槛时8142不进入可接列表；无数值/命名脚本的speak NPC被`NpcField.IsSilentNpc`静默处理，因此这对NPC在未接取8142时都可能没有对话。既有`CheckTrainingQuestProgress`人工设置STARTED仅证明击杀/交付判定，不能证明普通新角色可接取。用户记忆与源资源门槛存在差异，尚待用户决定是否解除8142的衣服门槛；未擅自修改任务绑定、数量、奖励或真实存档，亦未宣称本问题已经修复。

### 用户授权解除8142衣服门槛（2026-10-28）

用户随后明确回复“解除限制”，授权仅解除8142的1042003衣服门槛。`QuestCatalog.Build`读取原始Check后仅从8142的startReq.items移除此ID；原IMG与JS不改，职业、接交NPC、完成物品/击杀、Act奖励均保持原数据。此为用户授权的迁移差异，不代表原端没有衣服条件；上文关于该门槛的原端数据结论仍成立，当前迁移行为以本节为准。其他任务（包括1018）的1042003条件不放宽。

`CheckTrainingQuestProgress`改走透德真实菜单→原Say接受页→STARTED，再验证原stop/item、stop/mob、两种怪物击杀封顶及彼特交付菜单→确认→原WZ经验奖励、完成后不能重接；不再用人工SetQuestStarted跳过接取。

本次验证：`QuestNpcChecks.Run`24234通过、0失败（`/tmp/training-unlock-check.log`），compile-check Rebuild为0错误/0警告，脚本包2777条parseFail=0/mangled=0；NPC harness保持769通过/47运行失败/73 Java互操作既有基线。未构建安装Android APK，未改真实角色存档；Android实机仍待更新包体验证。


### 2026-10-28 截图任务纠正

8142不是截图中的两条独立狩猎任务。用户已授权根据截图重建900001/900002本地定义与JS，原端8142保留读取但不再绑定这两个NPC菜单。完整依据、实现及奖励未知边界见[任务窗口与告知](quest-window-and-alarm.md)。此前8142测试仅说明旧数据执行通路，不能作为截图任务内容正确的结论。


## 截图对话框尺寸与 type4「下项」补核（2026-10-28）

二进制身份：`BeiDou.exe` SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`、ImageBase `0x400000`。复用本记录已确认的529宽、正文120..240、窗口body+86、正文341宽与12号字体；不从截图尺寸反推新的原端宽高。

新补核：
- `0x6D3D77..0x6D3DAD`：NPC任务菜单用原生type4，调用`9AC552`排版，再`9AC8E0`收集链接/计算窗口。`0x9A64D4`的type4跳表项实际为`9A63C7 → 9A8FEF`，不是常规Next页的`9A7C8B`。
- `0x9A90E4..0x9A91AA`：type4在flags+6E0 bit0未置位时创建关闭，坐标**(10,h-26)**。`0x9A91AD..0x9A9288`：左肖像type4始终创建BtNext(command0x2001)，左上为**(w-10-68-scroll*8,h-77)**，原图46×18。右肖像另减130，未迁移。`6D3DFC..6D3E29`接受command1或0x2001，按+F4当前选项索引查真实questId；故「下项」是确认选项，不是菜单分页。现有首项确认策略仍未替代原端上下键焦点状态机。
- `0x9A2429..0x9A24B3`逐行取`9A2D01`首尾run，`9A249F..9A24AB`在当前尾run为最后run时停止，然后加尾margin；末尾换行后没有run的空行不能再加整行高度。旧Builder收尾无条件加16+2，导致短菜单窗口多出高度，现按最后有内容的行收尾。`9A247B..9A2492`对中间无run的空行用字体COM高度（`485A45`），当前Builder仍按16行高处理；此项未完成Windows字体高度的逐像素移植，不用截图猜值改写。

移植：`NpcDialogUi.Draw`把整个窗口及Pointer.Pos临时转换到原IMG坐标，finally恢复GUI.matrix和指针；采用`BottomBar.S`，与背包/任务窗的现有视口适配一致。这是项目移动呈现规则，不是声称EXE存在此缩放公式。之前对话框直接以UiKit的720高坐标画529宽，比同屏其他原生窗口大。没有修改全局UiKit、IMG或NPC/任务数据。两张用户截图NPC为9101002透德与9101003彼特、任务状态也不同，不能为匹配截图强改问候语或list分组。String/Npc.img/9101003/d0确有「你应该在旅行前好好训练自己。」，9101002没有d0；当前不会擅自给透德补彼特的文案。

复现入口：`tools/inspect_beidou_client.py <exe> --start 0x9a8fef --end 0x9a9290`、`--start 0x9a2429 --end 0x9a24b3`。`NativeViewportChecks.RunDialogLayout`创建隔离存档/场景，三个分辨率1280×720、2400×1080、800×600绘制真实IMG，对BtNext实际点击并断言返回非零selection7、GUI矩阵/指针恢复及尾换行测量一致；截图菜单仅为布局fixture，不代表真实玩家当前任务状态。


本次实际验证：`/tmp/npc-layout-targeted.log` 定向7项PASS，三个PNG实际尺寸分别1280×720、2400×1080、800×600，已目检1280与2400图；裁切预览保存在`docs/hud-review/npc-dialog-layout-1280.png`。`/tmp/npc-layout-quest-check.log`为24249通过/0失败；dotnet Rebuild为0警告/0错误；脚本包2779条parseFail=0/mangled=0，全量NPC harness仍769ok/47runFail/73JavaInterop既有基线。另跑`NativeViewportChecks.RunNpc`在旧罗杰引导流程「last talk key must reach showInfo and dispose」断言失败（`/tmp/npc-layout-review.log`）；未定位该流程失败是否与现有其他改动有关，不能称整个GUI流程回归通过。独立布局检查明确绕开该流程，并非撤销该失败。未构建APK、未Android实机验收，中文字体与中间空行字体高度仍有上述边界。


## 文案换行符字面转义（2026-10-28 修）

原端文案的换行不是控制字符，而是**字面反斜杠序列**：`Quest/Say.img` 里存的是 `\r\n` 四个字符（`<string value="…？\r\n#b#L0#不可以。#l\r\n#L1#可以。#l" />`），`String/EULA.img` 用双反斜杠写法 `\\r\\n`。源端在文本排版层统一当换行，本工程此前只在 `DialogText` 里认真 `\n`，于是学者罗宾 SP 问答把 `\r\n` 直接印进正文，两个选项行被挤在问题行后面（用户截图 2026-10-28）。

移植：`DialogText.NormalizeBreaks`（Parse / StripMarkup 入口）按 `\\r\\n → \n`、`\\n → \n`、`\r\n → \n`、`\r → \n`、`\t → tab` 归一化。脚本 `sendNext("…\r\n…")` 经 JS 词法已是真换行，重复归一化无副作用；`#L` 未闭合选项（如 `#b#L2#弓箭手\r\n#b#L3#飞侠`）换行后仍留在 link run 内，由 `DialogFlow.Builder` 的 `\n` 分支起新行。

同一根因一并覆盖：任务窗口正文（`QuestWindow` 同样走 `DialogText.Parse`）、NPC 菜单选项（`DialogFlow.Measure` 的 Selection 拼 raw 后进 Parse）、`WelcomeTutorial` 欢迎弹窗（C# 源码里写的是 `"\\r\\n"` 字面）。

验证：新增 `QuestNpcChecks.CheckDialogBreakEscapes`——断言字面 `\r\n`/`\\r\\n`/单独 `\r`/`\n`/`StripMarkup` 均归一化，并遍历真实 `Quest/Say.img` 全部含转义的文案，Parse 后 `Body` 不得残留反斜杠序列。`QuestNpcChecks.Run` 24259 通过 / 0 失败（`/tmp/quest-dialog-break.log`），unity-compile-check 0 warning / 0 error。未构建 APK、未 Android 实机截图验收。

## 左侧立绘与「无 NPC 脚本」的默认形象（2026-10-29）

现象：用户截图里的「强化成功！武器已提升至 12★」对话框左侧整块空白，既没有立绘也没有名字条；另有一些脚本同样没有。

已核实的绘制口径（对应前文 `0x9A6DFD..0x9A7C76`）：左侧头像 = 会话 `NpcId` 对应 `Npc/<7位id>.img` 的 `stand` 帧包围盒，横向中心 x=80、组顶按 `body-H` 居中（放不下时 `h-H-62`）、普通 NPC 再 +7；名字条固定 121×19，左边 20、位于立绘脚下。**立绘与名字条都只取决于会话的 npcId**，窗口本身不另画占位。

排查结论（三套入口决定 npcId）：
- 地图 NPC / 任务：`NpcSession.Talk(npcId)`、`TalkQuest`，npcId 来自地图 life 或 `Quest/Check.img`，>0。
- 脚本二级跳转：`cm.openNpc(npcId, name)` → `NpcSession.RequestOpenNpc` → `Talk(npcId, name)`。北斗特殊脚本统一写 `function openNpc(n){ cm.dispose(); cm.openNpc(9900001, n); }`（如 `npc/9900001.js`、`BeiDouSpecial/装备中心.js`），所以「昨日小睡」菜单下的装备中心→洗练→星级强化全程 npcId 都是 **9900001**，不是 0。
- 无 NPC：`ItemScriptManager` 的物品脚本（`Item/Consume/*/spec/npc` 多数缺省=0）、`PortalScriptManager`（`NpcSession.RunPortal` 里固定 `new NpcSession(0,...)`）、本地数据对白（`NpcSession.Conversation(2104,...)`，2104 只有 `String/Npc.img` 条目、**没有** `Npc/2104.img`）。

即：截图那两张是实拍脚本 `PreviewShot.SetupStar` 用 `NpcSession.Talk(0, "装备制作/星级强化")` 直接按脚本名进造成的，不是游戏内链路——GameView 里从「昨日小睡」点进去一直是有立绘的。

迁移（`Assets/Scripts/Npc/NpcDialogUi.cs`）：
- 新增 `PortraitNode(id)`：跟 `info/link` 链、要求 `Npc/<id>.img` 存在且 `stand` 帧非空，否则返回 null（2104 这类无素材 NPC 不再走 `FileRoot` 抛异常）。
- `DrawPortrait`：npcId<=0 或素材缺失时回落到 `DefaultSpeaker = 9010000`「冒险岛运营员」——与 `0x95D260` 通知菜单固定 NPC 同一个形象，物品脚本 / 传送门脚本 / 本地对白不再留白。
- 名字优先级：`session.SpeakerName` > `String/Npc.img/<id>/name`（新增 `WzNames.NpcOrNull`，避免把兜底名「NPC 2104」印到名字条）> 运营员名。

验证：`tools/unity-compile-check` 0 warning / 0 error；`PreviewShot`（`-previewStart 17`）出图 `/tmp/mapledash-preview/06-star-result.png` 左侧为「昨日小睡」立绘+名字条，`08-no-npc-dialog.png`（`Talk(0)` 对照）左侧为「冒险岛运营员」立绘+名字条。未覆盖：`avatar`/`rightPortrait` 特殊模式仍未迁移（沿用 stand 帧包围盒）；未做 Android 实机验收。

## #L 选项的「当前项」标记与上下键焦点（2026-10-29，用户指出全选项都是三角）

现象：用户截图里「10级 / 8级」两个选项**都**带红色三角，且方向键不能移动选择。原端只有一个三角，代表当前项。

**二进制实证**（同 SHA256 `1b91cf5b…f134b2`、ImageBase `0x400000`）：

- `#L` 的标记 run 由 `0x99E4A0` 建：`[run+0]=4`（类型 4）、`[run+0x20]=0x12`（宽 18）、`[run+0x28]=链接号`。
- 绘制 `0x9A3FBD..0x9A4034`：**先无条件取 `dot0`（0xb60）放进标记槽**；随后
  `0x9A3FD2` 取 `run+0x28` 与 `[窗口+0xF4]` 比较，相等则 `0x9A3FFA` 用 `dot1`（0xb61）**覆盖**该槽；
  不等时再看键盘焦点 `[+0xF8]/[+0xFC]` 里的项，命中同样换 `dot1`。
  即：**未选中 = dot0（浅灰 #CCCCCC 6×7 三角），当前项 = dot1（红色 6×7 三角）**。
  截图放大对比：dot0 在白纸上是很淡的灰，肉眼几乎看不出；用户看到的「只有一个三角」就是 dot1。
  旧迁移把两者写反（悬停 dot0、其余 dot1），于是每个选项都是醒目的红三角 —— 就是用户看到的 bug。
- 上下键 `0x9AC104 / 0x9AC347`：只处理 `[+0x78]==4`（type4 菜单）。
  上键 `dec [+0xF4]`，<0 则清 0；下键 `inc [+0xF4]`，超出条目数则落到 `count-1`。**两端夹取，不循环**。
  移动后 `0x9AC38E→0x9AC3B4` 把该条目滚进可见区；`0x9AC396` 重绘。
- 左右键 `0x9AC210 / 0x9AC129` 动的是按钮焦点 `[+0x6D8]`（`[+0x6BC]` 按钮数组，循环），与选项无关。
- 确认 `0x9AC1BE`：`[+0xF8]` 数组按 `[+0xF4]` 取条目，再取 `[条目+0x28]` 写 `[+0xF0]` 并 `command=1` 发回脚本；
  所以「下项」按钮和对话键确认的是**当前项**，不是首项。
- 鼠标：`0x9ABEBD` 命中测试（y 在 `[-0x30, bodyH+0x18)`、x 在 `[run.x, run.x+run.width+8)`）返回 `run+0x28`，
  `0x9AC040` 在鼠标消息里写进 `[+0xF4]`；`0x9AC4F6`（滚轮/滚动回调）与 `0x9AC552`（换页 SetDialog）把它复位成 -1。
- `[+0xF4]` 在键盘路径里当**数组下标**用、在绘制/悬停路径里当**链接号**用。`#L` 序号连续时两者等价，
  稀疏（如 `#L7#`）时原端本身就不一致；迁移统一按链接号处理。

**迁移**（`NpcDialog.SelectedLink` = 原端 `+0xF4`）：`DialogFlow.DotArt(selected, link)` 选中才用 dot1；
`DialogFlow.Links(content)` 按显示顺序收集链接（正文 `#L` 在前、程序菜单在后、`HeaderArt` 分组标题不算）；
`NpcDialogUi.MoveSelection(dir)` 夹取移动、`ConfirmSelection` 优先回选中项；`InputBridge` 在对话期间把
`GameAction.Up/Down` 派给 `MoveSelection`（物理方向键与屏上十字键同一条路径）；悬停只在指针真的移动时改写选中项。

验证：`QuestNpcChecks.CheckDialogSelection`（16 项断言：链接顺序、换页复位、两端夹取、确认取当前项、dot0/dot1 取向）
`/tmp/npc-dialog-selection2.log` 24269 通过 / 3 失败——这 3 项（1021 苹果发放、1021 引导动画、QuestClear 特效）
在**移除本用例的基线运行** `/tmp/npc-baseline-noNewCheck.log`（24253 通过 / 3 失败，失败项相同）里同样存在，
属本次改动之前就有的问题，与选项焦点无关。实拍见 `PreviewShot -previewStart 25` 的两张图（已归档 `docs/hud-review/npc-option-menu-idle.png`、
`npc-option-menu-selected.png`）：真实「装备中心」菜单（7 个选项，`npc/9900001.js`），未选中时三行都是浅灰点，
按两次下键后只有「装备洗练」是红三角。
**截图注意**：编辑器 `ScreenCapture` 落盘比调用晚（低帧率时 >1.5s），两个状态要在相邻 phase 里各停留 3s 再拍，
否则两张图会拍到同一个状态（第一次拍的两张就是这样，误判成「状态没变」）。未做 Android 实机验收。

### 默认选中首项（2026-10-29 追加，用户要求）

原端 `SetDialog`（`0x9AC566`）把 `+0xF4/+0xF8/+0x6DC` 一起 `or -1`，即**打开菜单时没有任何选中项**，
靠鼠标悬停（`0x9ABEBD` 命中测试 → `0x9AC040` 写 `[+0xF4]`）点亮当前行。这条在 Android 上不成立：
没有 hover 就无法产生焦点，屏幕上是「全是浅灰点、看不出选的是谁」，所以用户要求**打开即选中第一项**。

实现：`NpcDialog.SelectFirst()`（取 `DialogFlow.Links(content)[0]`，没有可选项仍是 -1），在两处调用：
1. `NpcDialog.Set()` 里 `mode == Simple` 时（覆盖 `#L` 菜单）；
2. **程序化菜单必须在 `Selections` 追加完之后再调一次** —— `NpcField` 的任务/提示菜单是
   `NpcSession.Choose()`（内部已 Set）之后才 `FillQuestMenu` / 追加 script 条目，`Set()` 里算早了会选到空。
另外 `DialogFlow.DrawPane` 的失效兜底改成「旧选中项不在新页里 → 回落首项」（原来回落 -1），保证任何
改写路径都不会停在无焦点状态。**注意 `SelectedLink` 存的是 link 号码而不是下标**：
装备中心真实菜单的 links 是 `[3,1,0,2,4,5,7]`，link 号不连续也不升序，`Links()` 按显示顺序收集，
默认 `SelectedLink=3`（首项），下两次到 `0`（第三项）——只看号码会以为出错。

`QuestNpcChecks.CheckDialogSelection` 已改为断言「默认选中首项」「分组标题不算首项」「非选项页不得有焦点」
「换页后重新默认选中新页首项」。实拍：`docs/hud-review/npc-option-menu-{idle,selected}.png`。

## 任务脚本侧的 selection 语义：带选项类任务脚本全量遍历（2026-10-29）

用户问「带选项类的任务脚本是不是都没校验答案的正确行就直接放行」。逐个跑完的结论：**不是**。
12 个带选项类调用的任务脚本里，只有 1 个（3953）真的不看 selection，其余 11 个要么校验答案、
要么把 selection 当数组下标（还有持有量校验）。

### 源端 selection 回传链路（已核实）

`NPCMoreTalkHandler`（BeiDou-Server/gms-server/.../handlers/NPCMoreTalkHandler.java）收包：

```
lastMsg = p.readByte();   // 上一次对话框类型
action  = p.readByte();    // 0=否/取消，1=是/确定
if (lastMsg == 2) { … 读文本 … selection = -1 }
else { selection = p.available()>=4 ? p.readInt() : (p.available()>0 ? p.readUnsignedByte() : -1); }
→ QuestScriptManager.start/end(c, action, lastMsg, selection)
→ 脚本 start/end(mode, type, selection)
```

`QuestScriptManager` 初次进入调 `iv.invokeFunction("start", (byte)1, (byte)0, 0)` / `end` 同理——
即**第一次回包的 mode 恒为 1、type 恒为 0、selection 恒为 0**；之后才由 NPCMoreTalkHandler 透传。
本机 `NpcSession.Begin` 对任务脚本调 `CallScript(QuestEnd?"end":"start", 1, 0, 0)`，与源端一致。

对话框类型字节（`NPCConversationManager`）↔ 客户端 `NpcDialog.MessageType`：
sendOk/sendNext/sendPrev/sendNextPrev=0、sendYesNo=1、sendGetText=2、sendGetNumber=3、
sendSimple=4、sendAcceptDecline=12。

### 全量遍历口径

「带选项类」= 脚本里出现 `sendSimple` / `sendGetNumber` / `sendGetText` / `sendSelectLevel` /
`sendNextSelectLevel` / `getInputNumberLevel` / `getInputTextLevel`（**不含** sendYesNo /
sendAcceptDecline —— 那是二值按钮，不是 `#L` 选项列表）。
按这个集合扫三处目录，命中集合完全一致：

| 目录 | quest 脚本总数 | 命中 |
| --- | --- | --- |
| `MapleDashResources/Data/scripts/quest`（运行时副本） | 470 | 12 |
| `BeiDou-Server/gms-server/scripts-zh-CN/quest` | 468 | 12 |
| `BeiDou-Server/gms-server/scripts/quest` | 274 | 12 |
| `content/script-overrides/quest`（截图重建脚本） | — | 0 |

12 个是：`20008 2001 20020 21013 2230 2293 3301 3303 3414 3414_free10rate 3927 3953`。

### 12 个脚本的 selection 语义分类

| 脚本 | 选项用途 | selection 语义 | 选错会怎样 |
| --- | --- | --- | --- |
| `2293` 最后演奏的歌曲 | 选歌 + 数字作答 | **真校验**：sendGetNumber 只能输入 1/2/3，输入 3 才 `forceCompleteQuest()+gainExp(32500)` | 输入 1/2 走失败文案；其它值 `qm.dispose()` |
| `3927` 秘密组织的存在 | 两道问答 | **真校验**：`selection==0` 才继续；status==2 无条件 gainExp(28843)+forceCompleteQuest，但 status==2 只有经 status==1 且 selection==0 才可达 | 选 1 → sendOk「我猜你还没有找到…」+ dispose，不放行 |
| `20008` 骑士之路 | 是否接受测试 | **真校验**：selection 0=再试一次→dispose；1=接受→forceStart+forceComplete | 选 0 不完成任务 |
| `20020` 5条岔路 | 是否看骑士领袖造型 | **真校验**：`selection==1`（「不，我没事」）提前退出 | 选 1 不完成任务（要走完 是/否 才 forceComplete+warp） |
| `2230` 神秘的小蛋 | 单选「什么职责？你是谁？」 | **真校验**：只有 `selection==0 && status==1` 才推进 | 非 0 → 脚本无分支，对话直接结束 |
| `2001` 酋长盖房子 | 选卷轴 | **下标**：`vecItem[selection]`，按职业样式给 2~8 个选项 | 无对错，但选错号会拿到别的卷轴 |
| `3414` / `3414_free10rate` 白外星人 | 选卷轴 | **下标**（同上） | 同上 |
| `3301` / `3303` 蒙特鸠协会考试 | 选矿石 | **下标 + 持有量校验**：`qm.haveItem(oreArray[selection], 2)` 才扣 2 个并完成 | 没矿石 → sendOk「没有矿石」+ dispose |
| `21013` 英雄的椅子 | 单选「(好像有点害羞...)」 | **无对错语义**：只有一个选项且不按 selection 分支 | 选什么都一样 |
| `3953` 说服莫哈莫德 | 四问连续 `#L` | **不校验**：全文不看 selection，选 0 和选 1 都走到 status==4 完成 + gainExp(20000) | —— |

结论：**「没校验就放行」只有 3953 一个**，而且这是源端脚本本身如此，不是本机实现的问题。

### 本轮补齐的本机缺口（照源端改，不自创逻辑）

1. `qm.completeQuest()` / `qm.startQuest()` **无参** = 当前任务。
   源端 `QuestActionManager.completeQuest()`（QuestActionManager.java:66-73）→ `forceCompleteQuest(quest)`；
   `AbstractPlayerInteraction` 只有带 id 的重载。原先本机 `Int(c,0)` 取不到 id → id=0 直接 return，
   于是 2001/3414/3414_free10rate 交付后**任务不会被标记完成**
   （全库 **36 个任务脚本、46 处**调用点用到无参写法）。
   现改为 `Int(c, 0, questId)`（带 id 时仍按 id）。
2. `player.getJobStyle()`：源端 `Character.getJobStyle()`（Character.java:577）=
   `Job.getJobStyleInternal(jobId, str>dex ? 0x80 : 0x40)`（Job.java:144）。
   新增 `Assets/Scripts/Npc/JobEnum.cs`：82 个枚举常量（名字/id/中文名逐条取自
   `Job.java` + `i18n/message_zh_CN.properties`）+ 样式映射。
   **注意源端 `jobid/10 == CROSSBOWMAN.getId()/10`（=32）只命中 320/321/322**，
   风灵使者一族（1300/1310…）归 BOWMAN 而不是 CROSSBOWMAN。
3. `Java.type('org.gms.client.Job')`：脚本拿它比样式（`stance == Job.WARRIOR`）或按名字取常量
   （`Java.type('org.gms.client.Job')["DAWNWARRIOR1"]`，20101~20315 转职脚本）。
   源端是 **Java enum 引用相等**，所以 `getJobStyle()` 必须和 `Job.WARRIOR` 返回**同一个实例**
   （`JobEnum.Js()` 缓存），换成数字比较会恒不等、脚本掉进 else 分支。
4. `cm.gainItem()` 的**返回值**：源端 `AbstractPlayerInteraction.gainItem`（:587-690）正数发完返回
   **Item 对象**（背包满才返回 null），负数走 removeById 返回 null。原先本机一律返回 undefined，
   于是 `quest/3414_free10rate.js` 的 `item = qm.gainItem(item,1); if (item != null)` 永远为假
   （不给经验也不交任务），BeiDouSpecial 制作脚本的 `if (!newItem)` 永远为真（误报「制作失败」）。

### 验证入口

`QuestNpcChecks.CheckQuestOptionScripts`（2026-10-29 新增，85 项断言）：用**真实脚本**
（`ScriptSource.Load("quest", …)`）造任务会话，推进 `session.Reply(1, selection)`，
逐个断言选对/选错的不同结果；其中一条走完整客户端链路
（`NpcDialogUi.MoveSelection` → `ConfirmSelection` → 脚本第三参）。

- Unity batchmode：`/tmp/quest_npc2.log` **24355 通过 / 3 失败**。
- 失败的 3 项（1021 苹果发放、1021 引导动画、QuestClear 特效）在**移除本用例的基线**
  `/tmp/quest_base.log`（24270 通过 / 3 失败，失败项相同）里同样存在，属改动之前就有的问题。
- 编译检查 `tools/unity-compile-check`：0 warning / 0 error。

## 2026-10-29 路卡斯1040乱码与共用文本引用解析

### 身份与原端调用链

EXE：`clien/BeiDou.exe`，SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，ImageBase `0x400000`。中文表来自 `clien/ijl15.dll`，SHA256 `87c0d58d53b4294acf53f053191c67739d1fa1ab2e9532fd764ba6b251f85c7e`，ImageBase `0x10000000`；表导出及替换机制见 `binary-and-tools.md` 与 `string-pool-zh.json`。

- 源服务端 `wz/Quest.wz/QuestInfo.img.xml` 的 `1040/1` 原文为 `#y1041# #b (#u1041#)#k`，后接1042～1044；任务名是麦加的各次修炼。截图的 `1041b()` 来自旧迁移解析器：将 `#y` 当橙色、残留数字后错配下一对井号，再将未知 `u` 丢弃。不是JS函数，也不应改脚本或资源文案。
- `99DD54` 是文本排版/标记分派；`99E16D → 9A264F → 9A283B` 分词；`99E428 → 9A2DE0` 分类。`99E197..99E403` 处理颜色、粗体及选项结束（串2244～2251为k/r/g/b/e/n/l/d），**y不在颜色分支**。`9A2DE0` 默认type17交给替换分支，而非统一颜色。
- `9A1878..9A1BFF` 按标记字符分派；`y` 到 `9A1BB8`，调用 `5CF63E` 取任务名，随后把任务ID存run+4。`u` 到 `9A1D27..9A1EF9`：先由 `9A25FF` 查完成表，再查context的进行中表（+4DF），映射0/1/2到串4296/4297/4298。EXE英文是Not Started/In Progress/Ended，ijl15替换为**未开始/进行中/已结束**。替换文字走 `9A206B` 的当前颜色/粗体设置，不自行改橙色。
- `q` 到 `9A1F54..9A2005`，以`%07d`索引`String/Skill.img`读取name（串1604）；`s` 在`9A2DE0`分为type9，`99E627..99E6A4`通过`75C755`读取技能名及技能对象+AC的原图。迁移路径复用 `Skill/<id/10000,D3>.img/skill/<id,D7>/icon`。真实`npc/6410.js`与`quest/6410.js`含`#s5221009# #b#q5221009##k`，名字“心灵控制”。旧代码把s参数直接打印为数字，q被静默丢弃。
- `@` 到 `9A19B9..9A1A1A`，`p` 到 `9A200A..9A205F`：同样以NPC ID、同一String根（局部-7C）、name串1604调用`98CF56`。@额外写run+3C供原端交互。当前先迁移相同的NPC名称替换；真实`#@1022000:#`为“武术教练”。
- 数值读取统一 `9A262F → A6134A → A612BF`：跳前导空白，读可选正负号和十进制前缀，遇非数字停止（32位算术）。因此`#t1092000:#`、`#i 4000000#`合法；旧`int.TryParse`会误读为0。此修正用于所有共用数值引用，不修改源文案。
- `W` 在`9A2DE0`分为type16；`9A1659..9A1841`剥去`#W`与结尾井号，用串5585=`UI/UIWindow.img/Quest/%s`取图。已迁移为Icon run。`Quest/prob`原图60×14、origin(0,0)，summary/reward等节点仍用原IMG；不自绘。

### 审计范围与剩余缺口

检查当前完整`Data/scripts`文本，以及源端`QuestInfo.img.xml`和`Say.img.xml`。粗略搜`#[A-Za-z]`会把`#L0#Mother...`的闭合井号当成M标记，不能当覆盖率。以下为QuestInfo中完整标记格式命中数（正则非运行时路径覆盖）：y49、u45、数字q38、W101、`[itzv]数字:#`1193、`@数字:#`337；本次已修复这些类别的名称/状态/图标及数值前缀处理。脚本另外存在动态拼接的s/q与`#B`进度条，不能凭静态计数认定所有分支已运行。

仍未迁移：`#M数字#`18处、`#x数字#`17处（1047等怪物卡任务）；`#jcmp/try/min/sec#`等60处（1200等挑战任务）；`#R数字#`18处（如2314）、`#Q数字#`1处（4490）；`#Dhour/min/sec/count/day...#`12处（29003等）；脚本的`#B`进度条。以上仅确认源文案存在且当前无对应解析，完整语义/数据依赖须继续追踪`99DD54`对应分支，不能擅造0值或自绘替代。未知/缺闭合标记仍可能被旧默认分支吞字，尚未改成猜测的容错规则。

`#a`当前由`QuestWindow.ExpandProgress`按当前任务展开，NPC共用解析器未接。补查`9A1A1F → A288ED`可确认编码为`questId=参数/10`、`目标序号=参数%10`；从进行中进度串截取每目标3字符，格式串5516=`%d / %d`。现有QuestWindow使用`1/1`（无空格），本次未扩大到进度协议迁移。此边界不得视为已完整还原。

任务名run+4、NPC引用run+3C的原端点击/悬停语义未完整迁移；本次不把任务/NPC引用伪装为`#L`选项回复。y/u遵循当前颜色，s/W保留所在`#L`的Link/Bold/Color。`StripMarkup`现复用`Parse`的Runs，避免两套解析规则再次分叉，仍保留原纯文本入口的换行（不做Body的Trim）。

### 复现与验证

项目根目录下可复现：

```sh
rtk proxy /tmp/mapledash-reverse-venv/bin/python tools/inspect_beidou_client.py ../../Beidou-Server/clien/BeiDou.exe --fn 0x99DD54 --start 0x9A184B --end 0x9A2074
rtk proxy /tmp/mapledash-reverse-venv/bin/python tools/inspect_beidou_client.py ../../Beidou-Server/clien/BeiDou.exe --fn 0x9A2DE0
rtk proxy /tmp/mapledash-reverse-venv/bin/python tools/inspect_beidou_client.py ../../Beidou-Server/clien/BeiDou.exe --fn 0xA612BF
rtk proxy python3 tools/imgdump.py String/Skill.img 5221009 --depth 1
rtk proxy python3 tools/imgdump.py UI/UIWindow.img Quest/prob --depth 1
```

运行时代码为`Assets/Scripts/Npc/DialogText.cs`；`QuestNpcChecks.CheckDialogReferenceTokens`覆盖1040真实正文、三种状态、标记相邻边界、真实技能图/名称、NPC引用、带冒号/空白/正号参数、原W图标、null纯文本，以及真实QuestInfo全部94处数字y/u引用。未修改源端脚本，未打包APK、未做Android实机或新布局的视觉验收。

最终验证：compile-check Rebuild 0错误0警告；Unity QuestNpcChecks 24470通过/3失败，对照本次修改前基线24355通过/3失败，新增115项全部通过。三项失败均为基线已有的1021苹果发放、1021引导动画、QuestClear动画，不能把全量回归声称为全通过。脚本包2779条parseFail=0、mangled=0；全量NPC889条parseFail=0、runFail=47、javaInterop=73，与已记录基线相同。CLI运行使用`tools/script-harness`工作目录下`--run ../../../MapleDashResources/Data/scripts/npc`（上级规则示例的两层相对路径在当前目录结构下不存在），`--pack ../../Assets/Resources/Scripts/npc_scripts.json`。本次运行日志`/tmp/dialog-baseline.log`、`/tmp/dialog-final.log`、`/tmp/dialog-compile-final.log`、`/tmp/dialog-script-run.log`、`/tmp/dialog-script-pack.log`为临时结果；可持久复现的断言保留于上述Editor检查方法。


## 2026-10-29 NPC Java互操作与运行审计（进行中）

### 纠正旧验收口径

上节的889条 / 47 RUN / 73 JAVA来自 `tools/script-harness/Program.cs` 的 MockConversation，不等于120个已确认游戏故障：测试桩没有正式Java宿主注册，原先即使start中dispose仍继续发action，并轮换任意type/selection。正式 `ScriptApiAudit.Run` 通过NpcSession/NpcApi + 隔离GameServer数据库复测，基线889条691通过198失败；原120条中42条在正式运行接口下已通过。审计仅覆盖level30/job0/金币100000的首个可选路径，最多14次回复，无活动地图/事件实例；事件前置条件错误不能计为已经确认的解析器缺陷。该基线曾未隔离独立的LastMapId偏好，不能用于严格逐项增量比较。当前审计每条固定起始地图1000000并在finally还原mxd.fieldmap（含原key不存在的情况）；正式结果写入 `docs/script-api-audit.json`，口径和剩余项见 `docs/script-api-audit.md`。

### 源端依据与迁移

本节为服务端Java/JS语义迁移，没有新增EXE函数或UI布局结论。源文件根为 `Beidou-Server/gms-server/src/main/java/`：

- `org/gms/server/expeditions/ExpeditionType.java`：17项枚举的min/max人数、等级、注册分钟。各项minSize均1，solo配置不改变当前结果；ARIANT三项maxSize7，其余30，maxLevel255，注册5分钟。按原值建立显式宿主，未知方法继续报错；不代表远征系统完整迁移。
- `org/gms/constants/game/GameConstants.java:428..461`：荣誉殿地图按骑士团/战神/冒险家职业分派；职业阶段按jobid模1000/100/10判定。地图值来自 `constants/id/MapId.java:255..264`。`MapId.isPartyDojo`使用闭区间925030100..925033804。
- `org/gms/util/ExtendUtil.java`：三元字符串键extend_id/type/name精确查询，首次插入create_time、后续保留create_time写update_time。NativeScriptService新增同表持久化宿主，和已有NativeScriptValues共用extend_value表，未将任意type伪装成每日记录。
- `org/gms/client/Character.java:8743` + `BuddyList`：好友扩容修改容量；当前桥通过NativeSocialService写真实角色记录并保存，其他角色不变。MonsterBook.java:60..79/133：getTotalCards为不同卡片种类数，不是卡片数量总和，桥读取社交服务cards.Count。
- MapleMap.java:1383/1422：getMonsters与getAllMonsters均枚举当前地图MONSTER。桥复用当前field怪物对象集合；尚非所有远程地图/独立副本对象。
- 解释器显式Java注册支持importClass的构造器/对象类身份、importPackage的惰性已注册类查找及词法遮蔽，保留未知类/歧义错误。System.out/err输出真实控制台；Calendar保留Java月份从0开始、星期日=1；SimpleDateFormat仅迁移源9390220所用yyyyMMdd，未支持的pattern明确报错。

### 用户授权脚本例外

用户明确允许对以下三处添加最小本地覆盖。2040022.js：源端辅助剂说明分支dispose后缺少return，随后读取未赋值currentItemSet.length；覆盖文件 `content/script-overrides/npc/2040022.js` 仅在该dispose后增加return，源服务端脚本不变，统一sync工具生成运行资源。2010005.js：在status2金币/船票判断前仅增加 `var access = false;`，防止不足时读取未定义变量。2143003.js：仅将 `ExpeditionType.Cydnus` 改为真实枚举 `CYGNUS`。三份源端文件保持不变，授权不扩展到其他脚本改写。

### 本轮补充的已核实接口

- `JsInterpreter` 的普通函数无接收者调用采用全局this；全局this属性与Global作用域双向对应，箭头仍捕获词法this。原credits/9977777的 `this[函数名]()` 因此可分派，不把undefined全局改成任意空对象。
- `Character.getQuestRecord` 的customData是QuestStatus瞬时字段，源端getCustomData无DB写出调用；按角色/任务键保留到角色会话结束，不冒称持久化。Channel.getStoredVar/setStoredVar是world/channel/key共享内存，默认0、跨角色共用，服务实例重建清空。
- `GachaponService.getRewardsByNpcId` 与抽奖共用有效奖池查询：NPC匹配**或公共池**，按有效期过滤、池id升序；修复既有GachaDraw遗漏公共池。ServerManager仅接入真实gachaponService查询，其他bean明确报错。
- `CommandService.java:37..54/101..149`、`CommandsExecutor.loadCommandsExecutor`：0..6级按启用状态建立名称/描述Pair，名称小写且重复只注册首个。NpcApi.CommandsExecutor复用AdminCommands原命令种子和i18n描述及command_info实时配置；没有复制已停用的旧registerLv0..6硬编码表。读列表不等于全部GM命令执行器迁移完成。
- `EventManager.java:1262`→`World.java:441`→`NumberTool.java:36..46`：transportationTime采用world_prop.travel_rate的Java float乘积，向零截断，溢出按Java整数转换夹取。NativeEventScripts注入当前world的实际配置；无world显式报错。
- `PacketCreator.playSound/musicChange`构造延迟消费的命令，只有sendPacket/broadcastMessage才播放；原PacketCreator.java:4327/4331分别为effect3/4。`tools/export_sound.py --npc-field-only`直接导出源Sound/Field.img的orbis音符、Party1/Clear/Failed及Bgm04/Shinin'Harbor原始音频；未接的其他效果包仍报错。
- MapleMap reactor getter按name/id/oid读取真实MapReactorService对象，缺对象返回null；forceHitReactor(newState)不能误绑为ForceHit自增状态。远程地图只暴露已有真实读能力，不能把远程spawn转到当前地图。

### 当前验证与阻塞（继续处理中）

2026-10-29：纯解释器/服务自测40通过0失败；含导入/日期、global this、ExtendUtil、customData与channel隔离、公共扭蛋池、交通倍率，以及2010005金币/船票不足两条分支。compile-check Rebuild为0错误0警告；包2779条（npc889/quest470）、parseFail0/mangled0。

正式NpcSession/NpcApi审计：889条829通过60失败；原Mock的120条清单106通过14失败，25项直接宿主断言通过。首条菜单路径、最多14次回复、level30/job0/金币100000、起始地图1000000、无渲染场景/事件/队伍；不是所有选项或APK验收。`docs/npc-script-mock-baseline.json`固定原120项身份，`docs/script-api-audit.json`保存逐项结果。

社交服务3项好友容量持久化/隔离断言已通过；完整server-check在既有MarketEconomy.TrailingDigit的Int32溢出中断，不能声称全量通过。普通商店并行改动的先前编译阻塞已经解除。

剩余14项含真正未迁移系统和缺副本/队伍/地图上下文的脚本。9000018依赖npccashshopdajieda，但全源仓库SQL/XML/CSV/JSON/Java/JS搜索仅该脚本命中，尚无表结构/商品数据依据；用户确认没有备份，明确要求无法修复的部分先按源端保留，故此项暂留原脚本，不再等待备份。未知系统不得以返回null、假地图、假成功或修改原业务脚本掩盖。

### 无备份后的保留范围与补接（2026-10-29）

用户明确答复无数据库备份：“能修就修，修不了先按源端来”。9000018保留源JS和原SQL，不构造空npccashshopdajieda或猜填商品；cm.getConnection由返回null改为明确的未迁移接口错误，避免后续prepareStatement空指针掩盖真正缺口。

Character.java:712读取canRecvPartySearchInvite，该值在6941从characters.partySearch装载（默认true）并在8437保存。NpcApi.isRecvPartySearchInviteEnabled已绑定当前真实角色partySearch，未登录明确报错；toggleRecvPartySearchInvite及其PartySearchCoordinator登记流程仍未迁移，不据此声称组队搜索全功能完成。

原 `soloMapling/ArtificialPlayer/BotTypes/Blackjack/BlackjackDealerBot.java:68..89` 按同地图荷官欧氏距离取最近并调用joinPlayerFromNpc。NpcApi显式类注册调用已有C#牌桌服务；仅使用GameServer.ActiveBotPlayer登记的当前真人，拒绝不同ID、未登记/离线/地图不同的身份，不构造替身玩家。原无荷官提示完整保留“请确认21点桌功能已开启，并重新进入自由市场”。9000055仍依赖未迁移BotBossCombatManager，类入口补接不等于该NPC全部分支通过。

ScriptApiAudit.CheckPlayerBridges覆盖组队搜索状态0/1、不同ID拒绝、无field登记拒绝、真实登记无荷官、两荷官取最近及实际牌桌成员加入、重复加入不增员；测试对象与respondant在finally清理。当前没有新的EXE/UI布局结论，亦没有整局21点/Android实机验收。


## Say.img 数据问答：瑞恩选错仍判正确的纠正（2026-10-30）

身份：BeiDou.exe SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，ImageBase `0x400000`。
旧「12 个选项任务」审计只统计 JS，不包含无 JS 的 Say.img 问答，不可据此声称全任务选项覆盖。
NPC 瑞恩12101对应任务1009..1015，原问答来自 `Quest/Say.img/<id>/1`，不是 rein.js。

确定调用链：`0x71737B`读取ask，非0到`0x717963`问答循环；`0x7179E9`读取stop，
`0x717A68`取页号子节点；`0x717D7D..0x717D86`从对话对象+F4取得选中链接号；
`0x717D97..0x717DD8`用该号读取 `stop/<page>/<selection>`字符串；
`0x717E08..0x717E11`非空去`0x717F80`显示普通确定反馈，之后返回false；空则`0x717E17`页号递增，
最终`0x718070`返回true。不能把一次mode=1当成任意答案都正确，也不能硬编码选项0。

原数据：1009正确0/I，1012正确3/Z，1013正确1/8级；stop对应错误选项有解释，正确项不含错误字符串。
`stop/0/answer`虽有1/4/2等值，本分支已核实使用的是所选链接号对应的stop字符串，不直接比较answer。
`NpcSession.BeginDataQuest`按ask展示原#L菜单，检查有效链接；错误只展示stop并结束，不交付、不发奖励；正确推进原下一页。
原JS不变。复现：`tools/imgdump.py Quest/Say.img 1012/1 --depth 5`；
`tools/inspect_beidou_client.py <BeiDou.exe> --fn 0x717963`。验证入口 `NativeGameplayFixChecks.Run`逐题遍历全部可见链接，区分错误反馈/不交付和正确翻页。

## 2026-10-30 对话Esc归属与路过误触发

NpcDialogUi.DrawDialog原本有OnGUI KeyDown Escape→NpcSession.Cancel，但InputBridge把NPC从全局Esc分派排除，依赖GUI事件独占消费，运行中会漏关。
现于已有会话输入分派首先处理物理Esc或GameAction.Menu，调用同一Cancel（发送取消并关闭），保留本帧dialogInput快照归属，避免关对话后同一Esc又打开侧菜单。
删除FieldDirector.CheckNpc中的HeldUp→TryInteractNearest额外入口；显式NativeKeyActions.NpcTalk和鼠标点NPC入口保留。
这项是输入接线修复，不改变原脚本对白或IMG布局，不新增“靠近自动说话”逻辑。
InteractionRegressionChecks.Run通过真实NpcSession+InputBridge.Update注入Menu动作检查关闭且不打开侧菜单；物理键平台事件仍需实机验证。

### 本轮验证范围（2026-10-30 第二批反馈）

隔离Unity工程InteractionRegressionChecks.Run退出0：真实Quest需求下怪物/全局任务掉落四阶段、普通0/−1掉落、实际NpcSession及InputBridge的Esc动作关闭与菜单不穿透、按上门原地松开/再按、触碰门防循环、HP/MP 700ms中点、死亡视觉origin/旋转/alpha及清理。
NativeGameplayFixChecks.Run退出0：包括原IMG墓碑600px起点、880ms落地、land保持和2.2秒确认阶段。
compile-check Rebuild为0错误0警告；脚本包2779条parseFail0/mangled0。全量NPC Mock运行889条790通过，27 runFail及72 Java interop缺口与此前基线一致，不能称全量脚本验证通过。
死亡层叠尚无EXE逐帧画面oracle；未打包APK、未做Android实机验收。EXP主条方案仍待用户选择，不以HP/MP通过代替EXP验证。
