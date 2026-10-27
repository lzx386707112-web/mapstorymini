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

## NPC 名字牌（在脚下，与图高无关）

- **锚点 = NPC 脚点（`life/y` 经 foothold 吸附后的位置），所有 NPC 名字在同一条水平线上，名字排在脚点下方。**
  这是用户在源端实测的行为，也和「名字不跟着图高走」的排版一致。
- 走过两次弯路，别再犯：
  1. 固定「脚点上方 82px」——矮 NPC 飘在空中，570 高的雕像贴在脚踝；
  2. 按 stand 首帧 `origin.y` 贴头顶——变成每个 NPC 名字高度都不一样。
  两者都不是源端行为。`Npc/NpcDialogUi.cs` 现在直接用 `map.WorldY(npc.y)`（脚点）+ `NameGapPx=3` 起排，
  `TextAnchor.UpperCenter`。
- `FieldNpc.head`（`Wz/NativeMapView.cs` 的 `NpcHeadHeight()`）现在**只**给头顶任务图标（!/?）用，
  不再参与名字定位。任务图标仍在头顶上方 26px。
- 待核实：任务图标相对头顶的精确间距（当前 +26px）没有汇编依据。

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
  - 正文区按 `c` 的 12×14 取整：`innerW = ceil(textW/12)*12`、`innerH = 行数*14`（**不能用
    CalcSize 的高度**，它含字体行距，单行文字会被撑成两层高、气泡中间一圈空白）。
  - 原端 UI 是 768 高虚拟屏基准，气泡整体按 `UiKit.H/768` 缩放。
    **九宫格必须先合成成一张整图再一次性画**（`BotField.BalloonBody` 按 `innerW/innerH` 缓存
    `Texture2D`，用 `Blit` 把 9 块按规则平铺进去）。分块 `GUI.DrawTexture` 时 GUI 会把每个四边形
    对齐到整数像素，缩放比又不是整数（手机上约 2.7×），相邻块之间留下 1px 空隙，
    透出背后的地图 —— 表现就是气泡左侧一条**竖直透明线**（用户报的「气泡被分割」）。
  - 锚点 = 角色**当前站立帧的头顶**（`SpriteRenderer.sprite.bounds.max.y`），不是脚点上固定 84px。
  - **横向锚点要按尾尖算**：尾尖在 arrow 图像的 x1，尾巴又开在气泡左边往右 6px，
    所以气泡左边 = 头顶 x − 6，尾尖才正好落在头顶（写成 `气泡左边 = 头顶` 会偏右 6px）。
  - **正文要按宽度断行**（原端 `CreateTextCanvas` 折行）：长消息是往**下**长成多行，
    不是横向无限拉长。断行上限 `BotField.MaxTextWidth`（当前 144px ≈ 12 个汉字/行）——
    这条是本实现里唯一没有反汇编依据的值（构造 0x48A935 → 九宫格 0x4890C4 之间只走素材与文本，
    没找到独立的宽度常量），要调就改这一个常量。行高取 `GUIStyle.lineHeight`（不小于 14），
    否则多行文字比正文区高、会压到上下边框。
- 待核实：`0x48B133`/`0x48B121` 传参 `+0x20`/`+0x18` 的语义、完整文字区几何仍未确认；
  上述贴边量（6px 边框带、圆角后 6px 起排）是像素比对推出来的，不是逐指令反汇编。

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
