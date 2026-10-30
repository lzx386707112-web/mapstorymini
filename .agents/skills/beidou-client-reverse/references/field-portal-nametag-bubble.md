# 地图传送门触发、NPC 名牌与聊天气泡

核实快照：2026-10-27。二进制身份沿用 [binary-and-tools](binary-and-tools.md)（SHA256 `1b91cf5b…f134b2`，ImageBase `0x400000`）。
本文件区分「已用 IMG/汇编/源端 Java 核实」「用户实测源端行为」「MapleDash 当前实现」「待核实」。

## 传送门触发方式

### 已核实的 WZ 数据（Map/Map/Map9/*.img，原 IMG）

| 地图 | 门名 | pt | 目标 | 备注 |
| --- | --- | --- | --- | --- |
| 910000000 自由市场入口 | `sp` ×3 | 0 | — | 出生点 |
| 910000000 | `out00` | **7** | 999999999 | script `market00`，坐标 (-8, 17)，就在入口大厅中央地面 |
| 910000000 | `st00` | 1 | — | 无 tm/tn 的装饰点 |
| 910000000 | `in01`..`in22` | 1 | 9100000xx | 进各房间的隐形门 |
| 910000000 | `up00`..`up03` / `dn00`..`dn03` | 1 | 同图 | 上下层箭头 |
| 910000000 | `pt_floor1`..`pt_floor5` | 3 | 同图 | 按↑换层 |
| 910000001 房间内 | `out00` | **2** | 910000000 | 按↑出房 |
| 910000001 房间内 | `up00`/`up01`/`dn00`/`dn01` | 1 | 同图 | 房内上下层 |

复现：`python3 tools/imgdump.py "Map/Map/Map9/910000000.img" portal --depth 4`。

### 触发规则（已确定）

- **pt=1 / pt=6**：隐形门、碰撞门——角色踩到即传，不需要按键。自由市场的 `in01..in22`、上下层箭头都属于这类。
- **pt=0 出生点**：不触发。
- **其余（2/3/4/5/7/8/9/10）**：必须站在门上按↑才触发。

上一版把 **pt=7 当成「碰上去就触发」**，导致自由市场入口中央的 `out00`（market00 脚本）在玩家从出生点走下来时自动把角色传出市场。用户在源端实测该门要按↑，与类型语义（CHANGABLE＝可切换门，如选择之岔路）一致，已按此修正。

- MapleDash：`Field/FieldCatalog.cs` 的 `FieldPortal.TouchTrigger`（1/6）与 `PressUpTrigger`（2/3/4/5/7/8/9/10）；`Field/FieldDirector.CheckPortals` 只在按↑门等按键。
- HUD 只对 `PressUpTrigger` 的门提示「按 ↑ / W 进入传送门」（`GameHUD.cs`），踩踏门不提示。

### 待核实

- `CPortal` 类型分派的精确汇编位置未定位（`.text` 内 switch 未对上）；上述类型语义来自客户端 Portal 类型常量与用户实测，未做逐分支反汇编。
- 触发距离判定（当前 MapleDash 用 ±30px 横向 / ±80px 纵向）没有源端数值依据，属待核实。

## NPC 名字牌（脚下紧贴文字的背景，2026-10-29 逆向纠正）

### 二进制与调用链

- `BeiDou.exe` 身份同本文开头（完整 SHA256 见 binary-and-tools）；`Canvas.dll` ImageBase `0x50000000`，
  SHA256 `4383567405edb80427585cc8b62613fd807333761b11d08777d828894a871ae0`。
- NPC 场景初始化函数 `0x6D21CD` 内 `0x6D2251..0x6D2297`：从 NPC 模板 `+4` 取名称，
  `push 0x3E9`，传 NPC 图层 `+0xE4`、位置对象 `+0x90`，`ECX=NPC`，调用共用名牌函数 `0x5F0334`。
  `0x6D229C..0x6D22EC` 仅模板 `+8` 非空时另建 type=1002 的附加名称行；该附加行尚未迁移。
- type=1001 在 `0x5F0E20..0x5F0E2C` 跳到 `0x5F1174`，不用 `UI/NameTag.img` 装饰牌分支。
  原端确实由 Canvas API 创建画布、填色、绘字；原 IMG 中的玩家饰牌不能代替这条分支。

### 确定的尺寸、绘制与原点（单位均为原端地图像素）

| 项目 | 汇编依据与参数 |
| --- | --- |
| 字体 | `0x5F119A..0x5F11CB`：`Arial`（字符串池5527）、12、ARGB `0xFFFFFF00`（黄字）、`BA`（池1411）；调用 `0x46341A` → font vtable `+0x0C` Create |
| BA | Canvas `0x500045C5`：B→flag1，A→flag0x10；`0x50004FDC..0x5000500A`：weight700、抗锯齿，不含描边 |
| 文字宽 | `0x5F11F4..0x5F1226` → `0x42782E` 调字体宽度接口；使用原字体实际整数宽度 |
| 字体高度 | `0x5F1250..0x5F1258` → `0x485A45` → font vtable `+0x14`；Canvas vtable `0x50014294` / `0x50014420` 的 getter `0x5000319C` 读取字体资源 `+0x14` |
| 高度为何是12 | Canvas `0x5000309D` Create → `0x50005907`：size传入key `+4`，`0x50005A05` 复制到资源 `+0x14`；`0x50004FC8` 以负size建 LOGFONT，`0x50004E00..0x50004E0D` 再以abs(lfHeight)写回资源 `+0x14`。返回的是请求高度12，不是TextMetrics行高 |
| 背景宽高 | `0x5F12E5..0x5F12EF`：type1001没有额外图标宽，**width=文字宽+5，height=12+4=16**；`0x5F1348` 调 canvas Create |
| 背景填色 | `0x5F0E25` 初始化 ARGB `0xA0000000`；type1001不覆盖该值；`0x5F139E..0x5F13AF` 填整个背景，黑色alpha160/255 |
| 边缘 | `0x5F1445..0x5F150A` 用 `0x00FFFFFF` 各清掉四角1×1像素；**没有绿色边框**。`0x5F1522` 的另一种填充受玩家牌状态门控，NPC1001不进入 |
| 绘字 | `0x5F15EC..0x5F1603`：无图标时 **DrawText(2,0)**；没有额外上下padding，没有四向文字描边 |
| 水平原点 | `0x5F1654..0x5F1669`：`origin.x=width/2`（整数向零截断，正宽即floor） |
| 竖直原点 | `0x5F1691..0x5F1694`：`origin.y=-2`；第一行名牌在脚点下2px。非首行会在 `0x5F1979..0x5F1AAA` 按已有行高+1继续叠放 |

名字锚点使用 NPC 脚点（`life/y` 吸附地面后的位置），不使用图片高度。
`FieldNpc.head` 只给任务标记用；任务标记当前头顶+26px仍未获原端汇编确认。

### 复现与纠正范围

工作目录 MapleDash，按 RTK 规则运行：

```sh
rtk proxy /tmp/mapledash-reverse-venv/bin/python tools/inspect_beidou_client.py ../../Beidou-Server/clien/BeiDou.exe --fn 0x5F0334
rtk proxy /tmp/mapledash-reverse-venv/bin/python tools/inspect_beidou_client.py ../../Beidou-Server/clien/BeiDou.exe --fn 0x485A45
```

Canvas 以现有 `tools/pe_module.py` 的 `Mod` 读取，调用 `dump(0x5000309D)`、`dump(0x5000319C)`、
`dump(0x50005907)`、`dump(0x50005A05)`、`dump(0x50004D84)`、`dump(0x50004F6B)`、`dump(0x500045C5)`。
原端用户截图归档于 `docs/npc-nameplate-source.png`，仅用于对照，不以裁剪图推导尺寸。
本次扫描 clien 各 DLL 的代码节对本 EXE 函数范围的绝对地址引用，未发现改写上述尺寸常量的直接补丁；
这不等于已穷尽所有间接/运行时hook。

**旧结论作废**：早期 `docs/NPC名字牌绘制.md` 的绿色底色、绿色边框、左右3px/上下2px与四向描边均来自截图估算，
不是EXE依据。绿底可以来自半透明黑底与地图底色混合，不能据截图把混合后的颜色当固定绘制色。
旧记录 `NameGapPx=3` 后来代码为1，本次以canvas origin.y=-2纠正为2。

### MapleDash迁移与验证边界

`Npc/NpcDialogUi.cs` 采用宽+5/固定高16、文字(2,0)、黄色粗体、半透明黑底与四角透明；
明确清掉 `GUI.skin.label` 的padding/margin，背景不再使用Unity的行盒高度。
GUI按相机的地图像素尺度换算（`UiKit.H/(2*orthographicSize*Art.PPU)`），名牌随地图统一缩放。
`UiIssueChecks.NpcNamePlate` 用异常大的24px行盒验证背景仍16px，以及整数水平原点和(2,0)绘字落点。
仍使用项目内 `UiKit.Font`（含中文），没有移植Windows Arial及其中文fallback的GDI字形栅格器；
故文字实际宽度、字形和抗锯齿不声称与Windows逐像素相同。
本次验证：`tools/unity-compile-check` 的 `dotnet build -t:Rebuild` 为0 warning/0 error；
Unity batch `UiIssueChecks.Run` 48项通过/0失败（含5项名牌几何检查）；脚本pack检查2779条、parseFail=0/mangled=0。
目录脚本run检查889条：parseFail=0、runFail=47、javaInterop=73，失败包括缺少getCapacity/getId等宿主接口；
该harness不编译本次修改的NpcDialogUi/UiIssueChecks，不能将其失败归因于这次绘制变更，也不声称全脚本通过。
未完成Android实机与原端同场景逐像素对照。

## 头顶聊天气泡

- 资源：`UI/ChatBalloon.img/0`。九宫格实测 `nw/ne/sw/se` 6×6，`n`/`s` 12×6，`w`/`e` 6×14，`c` 12×14，`arrow` 13×13 origin(1,0)，`clr` = -16777216。复现：`python3 tools/imgdump.py "UI/ChatBalloon.img" 0 --depth 2`。
- 构造入口 `0x48A935`（ECX=层管理器）：先取字符串 1388 `UI/ChatBalloon.img/`；参数 type 走 `[ebp+0x18]-1000` 的 5 路分派：1000/1001 用默认节点，1002 → 5643 `pet`，1003 → 1390 `adboard`，1004 → 1593 `mob`；`[ebp+0x20]!=0` 时取 5623 `dead`。文字走九宫格函数 `0x4890C4`。
- 调用点 `0x93746C..0x9374D0`：`push 0x3e8`(type 1000)、`push 0x1388`(5000ms)、角色 `+0x1140` / `+0x1150` 两个对象、文本，`lea ecx,[edi+0x14]` 后 call `0x48A935`；`0x9375F2` 取返回值。显示期恒为 5000ms；淡出时长字段在 `this+0x64`，`0x48E6D4` 里按剩余时间做线性 alpha，具体默认值未确认。
- MapleDash：`Field/BotField.cs` 的 `OnGUI` 用原 IMG 九宫格逐块平铺绘制，文字黑色、显示 5 秒、最后 0.5 秒淡出；`Kind.Chat` 与 `Kind.Bubble` 都冒泡（Chat 另进聊天记录，对应 `BotFullChat`/`BotChatbubble` 的区别）。
- **几何（第三版，逐像素比对 IMG 后的结论）**：
  - `arrow` 的 `origin=(1,0)`（dead 版 `14x15 origin=(2,0)`）⇒ 图像 x1 是参考列、图像**左下角是尾尖**；
    气泡以左下角贴锚点往右上展开，尾尖落在角色头顶。
  - **叠放顺序：主体在下、尾巴在上。** 依据是 `arrow` 顶部 5 行的像素与 `s`（下边框）逐行相同
    （`eeeeee` / `ccddee` / `bbccdd` / `bbbbdd` / 黑），它就是下边框带的复制品，靠覆盖把边框「抹掉」接成一体。
    把尾巴挂在下边框**下面**（`y = rect.yMax`）就会同时露出一块白板和一条边框线，气泡像被切成两半。
    正解：`arrowTop = rect.yMax - 6`（正好盖住 6px 边框带），`arrowLeft = rect.x + 6 - 1`。
  - 水平位置：`arrow` 顶部几行**没有圆角处的黑色**，所以尾巴开在直边段上（`sw` 圆角 6px 之后），
    不能压在左下角（压住会把角的黑边刷成浅蓝，缺一块轮廓）。
  - 尾尖落在头顶 ⇒ `rect.y = headY - (arrow.height - 6) - h`。
  - 正文区**宽度自适应**：`innerW = max(12, ceil(最宽那行的实际像素宽))`（**不做 12px 步进取整**，
    这是 2026-10-27 用户明确纠正的「宽度是写死的」）。`innerH = max(14, 行数 × 行盒高)`，行盒高取
    `style.lineHeight`——ui.ttf 在 fontSize=12 时 **ascent=13 / descent=4 ⇒ 17px**，CJK 墨迹 11px，
    上下各留 3px；`GUI.Label` 的 `MiddleCenter` 居中的就是这个行盒，所以墨迹上下间距天然相等（3/3）。
    **别把行高写死 14**：行盒比正文区高 3px，文字整体下沉、下沿被 6px 边框裁掉，看起来就是
    「上面内边距大、下面没有」——2026-10-27 用户在对照图上指出的就是这个坑（我的预览脚本写死了 14）。
    `innerH` 也**不能用 CalcSize 的高**（它含行距，单行文字会被撑成两层高、气泡中间一圈空白）。
    四周内边距固定 `6px`，所以气泡宽 = 正文宽 + 12。短句窄、长句才宽。
  - 原端 UI 是 768 高虚拟屏基准，气泡整体按 `UiKit.H/768` 缩放。
    **九宫格必须先合成成一张整图再一次性画**（`BotField.BalloonBody` 按 `innerW/innerH` 缓存
    `Texture2D`，用 `Blit` 把 9 块按规则平铺进去）。分块 `GUI.DrawTexture` 时 GUI 会把每个四边形
    对齐到整数像素，缩放比又不是整数（手机上约 2.7×），相邻块之间留下 1px 空隙，
    透出背后的地图 —— 表现就是气泡左侧一条**竖直透明线**（用户报的「气泡被分割」）。
  - 锚点 = 角色**当前站立帧的头顶**（`SpriteRenderer.sprite.bounds.max.y`），不是脚点上固定 84px。
  - **整体居中（源端实测）**：气泡以角色**头顶为水平中心**（`rect.x = headX - w/2`），
    尾巴也在气泡底边上**居中**（`tailX = rect.x + (w-13)/2`）。尾尖本身在 arrow 图像左下角，
    所以尖尖朝下偏左、正好指着头顶。**不要改成左对齐**（气泡左边 = 头顶会整体偏右半个气泡）。
  - **正文 = 昵称 + ":" + 空格 + 内容**，`BotField.OnOutput` 拼好再交给气泡。
  - **折行按「字数」不是像素宽**（用户对源端截图实测）：一行最多填 **11 个字，空格不占位**
    （`ieskk: 11222` 正好 11 个字一行）。到 11 个字就**硬断**，不在空格处退让——
    退让会让长消息第一行只剩「昵称: 」，气泡里冒出一行半空白（2026-10-27 对照图已排出：
    同样一句话 A 填满两行 / B 退让变三行且首行只有名字）。`BotField.MaxCharsPerLine = 11`。
    行高取 `GUIStyle.lineHeight`（不小于 14），否则多行文字比正文区高、会压到上下边框。
- 待核实：`0x48B133`/`0x48B121` 传参 `+0x20`/`+0x18` 的语义、完整文字区几何仍未确认；
  6px 边框带重合量是像素比对推出来的，不是逐指令反汇编。
  「11 个字硬断」是用户按截图数出来的，客户端里对应的折行例程（按字数还是按像素宽）**没有反汇编确认**；
  对照图：`docs/chat-balloon-autowidth.png`，生成脚本 `tools/preview_chat_balloon.py`
  （用真 IMG 切图 + 同一套 Measure/BalloonBody 公式，2.2× 缩放；改气泡几何先跑它看效果）。

## 假人走路（封包节奏 + 动画速率）

- 源端 `MovementPacketConstructor.createWalkStepPacket`：`stance = movingLeft ? MOVING_LEFT : MOVING_RIGHT`，
  `MovementEnums.StanceValues` 里 `MOVING_LEFT=3`、`MOVING_RIGHT=2`。
- MapleDash `BotRecordedActions.Step` 曾传 `left ? 1 : 0`（IDLE 站立姿势），假人走路定格在站立帧。
  已改为 3/2，并由 `tools/server-check` 的「封包步行使用 MOVING_LEFT/MOVING_RIGHT 姿势」回归守住。
- 源端 `walkAlongX` 的常量：`final short duration = 160; final int step = 10;`，循环里
  `createWalkStepPacket(...)` 后 `sleepAmountSeconds(duration)`（毫秒）。即 **10px / 160ms = 62.5px/s**。
- 客户端插值窗口必须就是 160ms。曾写 `StepSeconds=0.1`：每步 100ms 走完再停 60ms，看起来一顿一顿。
- **世界单位 vs 像素的坑**：`Art.PPU=40`，10px 只有 0.25 世界单位。旧代码
  `Mathf.Clamp(distance / StepSeconds, 45f, 500f)` 拿像素范围去夹世界单位，把 0.25/0.16≈1.6 抬到 45，
  等于 1800px/s——每一步都是瞬移。现在夹的是 `2..800 px/s` 换算后的世界单位。
- 动画速率：假人 62.5px/s，主角基准 `PlayerCtrl.BaseWalkPx=125`，`walk` 播放速率取
  `62.5/125=0.5`；body 的 walk1 是 4 帧、帧 delay 缺省 120ms（`WzCharacter.Clip` 回落 120），
  一个循环 480ms，主角 125px/s 走 60px、假人 62.5px/s 也走 60px，步幅才对得上。按 1.0 播就是两倍速滑步。

## 假人启动开销（移植适配，非源端语义）

- 源端在市场启动编排跑在后台线程；Unity 主线程同一帧连开十几个假人（解析 8.6MB `Bot/Items.json`、合成纸娃娃、建导航）会明显卡住进图。
- 现做法：`BotWarmup`（`Server/Bot/BotWarmup.cs`）在主线程取文本、后台线程解析大 JSON（WebGL 退化同步）；
  `BotField` 每帧最多合成 1 个假人；`Bot/Maps.json` 也走同一份预热结果（导航与世界图不再各解析一次）。
- **关键：`FieldDirector.TickBots` 必须先问 `BotWarmup.Ready(...)`，没就绪就整帧跳过。**
  直接访问 `GameServer.Bots` 会在构造 `BotRuntime` 时通过 `BotItemCatalog.Shared` →
  `BotWarmup.Root` 的 `Thread.Join()` 把整段解析搬到主线程——这就是「进游戏后卡一段时间才能动」。
  跳过期间玩家可以正常移动，假人稍后补上。
- 这不改变源端业务顺序（仍是一次性启动、配额与波次不变），只是把同一份工作摊到多帧。

## 新手训练营地图（0~4）与 cm.warp 的地图不存在语义

核实快照：2026-10-28。依据：`MapleDashResources/Data/Map/Map/Map0/*.img`（imgdump 读出的 info/life/portal）、
`String/Map.img` 的 `maple` 组、源端 `scripts-zh-CN/npc/2100.js`、`2101.js`、`2104.js`、
`gms-server/src/main/java/org/gms/client/Character.java`。

### 地图身份（北斗把原版 Maple Road 编号重做成了自定义新手区）

| id | mapName（String/Map.img `maple/x`） | 关键事实 |
| --- | --- | --- |
| 0 | 蘑菇村西入口 | **黑场过场舞台**：`info/onUserEnter = goAdventure`、`returnMap`/`forcedReturn = 10000`、`back/0/bS` 为空串 + `tile 1` 是 `allblackTile`、`life` 空、只有一个 `sp` 出生点、无任何门 |
| 1 | 番茄田 | `portal 3 = next00`（pt 2）→ tm 2；NPC 2100（塞拉） |
| 2 | 番茄田 | 玩家最多 NPC 的一张：2000/2001/2102/**2101（希娜）**/9101002/9101003；`portal 1 = front00` → tm 1 |
| 3 | 番茄田 | town（`returnMap = 3`）、NPC 2100；`portal 2 = st00`(pt 1, tm 999999999) 是装饰点 |
| 4 | 踏入旅途 | NPC 2104 → `cm.warp(1)`，即从这张图进训练营 |

`2100.js` 头注释还写着 OdinMS 时代的「Training Camp (0), (1), (3)」，是**旧编号的遗留注释**，
不能当作现在 0 号图是训练营入口的证据；现版本 0 号图 `life` 里没有塞拉。

### NPC 2101（希娜）的送别与 `cm.warp(0)`

`npc/2101.js`：`start()` 里 `getMapId() === 10000` → `sendOk` 讲操作说明（10000 小蘑菇也有这个 NPC）；
其余地图 → `sendYesNo("你的训练完成了吗？…我会让你离开这个训练营。")`，确认两次后 `cm.warp(0); cm.dispose();`
（脚本第 45 行 `// cm.warp(40000, 0);` 是被注释掉的原版目标）。

`warp(0)` 的意图 = 走 0 号过场的 `returnMap` 回到 10000 小蘑菇（原版靠客户端播完
`Effect/Direction3.img/goAdventure/Scene{gender}` 自己跳走）。

### 源端 `warp` 到不存在地图：提示，不抛错

- `AbstractPlayerInteraction.warp(int[, Object])` → `Character.changeMap(map, pt)`（Character.java:1597）
  → `getMap(map, true)`（Character.java:10893）：`getMapFactory().getMap(mapid)` 抛的异常被吞掉、返回 null 时
  只做 `dropMessage(5, msg)` + `dropMessage(1, msg)` + `enableActions()`，然后 `changeMap` 直接 `return`。
- 文案（`gms-server/src/main/resources/i18n/message_zh_CN.properties`
  `Character.Map.Change.message1`）：`[系统] 你无法去到不存在的\r\n地图({0})，请联系GM处理。`
- 结论：**脚本里 `warp` 无效地图不会中断脚本**，所以「对话脚本出错」不可能来自这条路。
  只有 Java 层真的抛异常（如 `getSavedLocation` 之外的空对象）才会打断脚本。

### MapleDash 修复（2026-10-28）

- `Npc/NpcApi.cs` 原来 `if (target <= 0) throw ...`：把**合法的 0 号图**当无效 id，
  于是 2101.js 末尾 `cm.warp(0)` 直接抛错 → 玩家看到「对话脚本出错（详见日志）」。
  现改为 `target < 0` 才抛（保留 portal/market00.js 的 try/catch 兜底），
  非负但 `FieldCatalog.Maps` 里没有的 id 按源端发 `NativeSystemMessage.Drop(5)` + `Drop(1)` 两条提示后返回。
- `Field/FieldDirector.cs`：`WarpTo` 的 `mapId <= 0` 与 `TickWarpFade` 里 `pendingWarpMap` 的
  `target <= 0` 同样把 0 当哨兵，一并改成 `< 0`（哨兵只有 -1）。
- 0 号图没有演出系统可播（本端 `showIntro` 是空实现），停进去就是一片黑且无门出不来：
  现在目标为 0 时在 `TickWarpFade` 里直接换成它的 `returnMap`（10000），`Enter` 里另有
  `deferredWarpMap` 兜住「真的站上 0 号图」的情况（如存档 `LastMapId = 0`）。
  以后若补上 `Effect/Direction3.img/goAdventure` 开场演出，这两处应改为「播完再跳」。


## 可见传送门与麦加训练入口（2026-10-30）

身份同本文（BeiDou.exe SHA256 `1b91cf5b…f134b2`）。`0x711F89..0x711FAC`明确比较pt=7、2、4，
都调用`0x712313`同一可见图层；该函数依次读取 `Map/MapHelper.img/portal/game/pv`。
pt3/9走`0x712243`另一分支，不能由此推定所有按上门都应绘制pv，也不能把缺失的pg节点造出来。
复现：`inspect_beidou_client.py <exe> --fn 0x711F89`和`--fn 0x712313`。
这次确认的是视觉分派，不代表上文触发距离的待核实项已经解决。

地图1010000「冒险家修炼入口」原`Map/Map/Map0/001010000.img/portal/1`：
pn=in00、pt7、(x,y)=(74,154)、tm999999999、script=entertraining。
NativeMapView旧实现仅绘制pt2，故有脚本触发入口但无光洞；现将2/4/7统一使用原pv动画，原坐标与帧origin保持不变。
原 `portal/entertraining.js` 按已接任务1041/1042/1043/1044分别warp1010100/1010200/1010300/1010400，portal4；
无对应任务只提示，不擅自开放训练。原JS及FieldDirector触发规则均未改。
验证入口 `NativeGameplayFixChecks.Run`加载实际1010000并断言Portal_1有原sprite、pt7和原脚本名。

## 2026-10-30 传送后原地再按上的重入

触发类型仍按本文已核实pt分类。问题来自FieldDirector.portalArmed只在FindPortal=null时复位，目的地角色站在门上则永远不复位。
现按上门在原地松开上方向时也重新允许，后续新按上即可进入；保留400ms到图锁和渐变期间不接新门。
触碰型pt1/6仍必须离开范围再复位，到图初始portalArmed=false，不能因没有按上就重启触碰门形成循环。
InteractionRegressionChecks.Run验证到图仍按上不反弹、原地释放可重置、新按上启动渐变，以及触碰型原地释放不重置。
这里是本项目输入状态锁修正，没有将400ms项目锁推断为EXE新结论。

### 本轮验证范围（2026-10-30 第二批反馈）

隔离Unity工程InteractionRegressionChecks.Run退出0：真实Quest需求下怪物/全局任务掉落四阶段、普通0/−1掉落、实际NpcSession及InputBridge的Esc动作关闭与菜单不穿透、按上门原地松开/再按、触碰门防循环、HP/MP 700ms中点、死亡视觉origin/旋转/alpha及清理。
NativeGameplayFixChecks.Run退出0：包括原IMG墓碑600px起点、880ms落地、land保持和2.2秒确认阶段。
compile-check Rebuild为0错误0警告；脚本包2779条parseFail0/mangled0。全量NPC Mock运行889条790通过，27 runFail及72 Java interop缺口与此前基线一致，不能称全量脚本验证通过。
死亡层叠尚无EXE逐帧画面oracle；未打包APK、未做Android实机验收。EXP主条方案仍待用户选择，不以HP/MP通过代替EXP验证。

## 明珠港船下隐藏传送门（2026-10-30，本轮）

二进制身份沿用本页 SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`。
`Map/Map/Map1/104000000.img/portal/30` 为 h_west01，pt10，(-572,191)，目标同图 h_east01；
portal29 为配对 h_east01，(2894,423)。节点已被 NativeMapView.LoadMap 解析；旧实现缺的是隐藏门特效，不是没有 portal 数据。

已核实调用链：
- `0x711F89` 分派中 pt10/11 进入 `0x712157`，加入 manager+0xC 隐藏门列表；image 为空时 `0x712210..0x71223C` 写 default。
- `0x950573..0x9505B8` 从玩家位置调用 `0x712C57`，传入半宽100；`0x712CAA..0x712CDC` 构造 x±传入半宽、y±50 的 RECT，并调用 PtInRect。逆序选首个命中门。
- `0x712D35` 在新旧门指针不同时切换；旧门读 portalExit（池4390，`0x712E7F`），新门读 portalStart（池4388，`0x713147`）；pt10用 `0x71259D`（字段工具可复现 portal/game/ph），pt11起始用 `0x712827`（psh）。
- `0x71332C` 在当前层动画结束后读 portalContinue（池4389，`0x71340D`）；该段调用 `0x71259D`，不能据节点名推导成始终绘制普通 pv。
- `Map/MapHelper.img/portal/game/ph/default`：Start4帧、Continue7帧、Exit3帧；Canvas自带origin（例如Start0为92×119 origin45,117，Continue0为104×118 origin44,115）。psh/default同尺寸；帧无delay，本项目沿用无delay默认100ms，不把此默认称为已逆向的原端时长。

复现：`tools/imgdump.py Map/Map/Map1/104000000.img portal --depth 3`；`--fn 0x712C57`、`--fn 0x712D35`、`--fn 0x71259D --fields 0x71259D --pool <字符串池JSON>`、`--fn 0x71332C`。
迁移：NativeMapView为pt10保存隐藏门三段原动画，未靠近时隐藏，靠近后Start→Continue、离开Exit；保留节点坐标与原origin。
pt11自定义image与ph共享Continue的原端细节尚需继续逐图核对，本轮未扩展pt11渲染，不将默认ph/psh相同推广为任意自定义图形相同。
