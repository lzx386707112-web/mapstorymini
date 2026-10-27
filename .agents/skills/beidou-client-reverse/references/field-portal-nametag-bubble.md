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

## NPC 头顶名字牌

- 名字锚点应贴 NPC 站姿首帧的画面顶端，即脚点往上 `origin.y` 像素。原 IMG 实测：
  `Npc/09250025.img` stand/0 为 442×570、origin (321, 569)——自由市场入口的巨型雕像；
  `Npc/09030000.img` 95×94 origin (48, 94)；`Npc/0900018.img` 109×95 origin (21, 95)；
  `Npc/09030100.img` 115×71；`Npc/09010000.img` 46×69。
- 旧实现固定「脚点上方 82px」：矮 NPC 名字飘在空中，94/95 高的名字压在头顶，570 高的雕像名字贴在脚踝。已改为 `NpcHeadHeight()`（`Wz/NativeMapView.cs`，读 stand 首帧 `origin.y`，失败回落 80）→ `FieldNpc.head`，`Npc/NpcDialogUi.cs` 用它算名字与任务图标锚点。
- 待核实：名字相对顶端的精确像素间距（当前 +2px）与任务图标行距（当前 +26px）来自排版常识，未从汇编确认。

## 头顶聊天气泡

- 资源：`UI/ChatBalloon.img/0`。九宫格实测 `nw/ne/sw/se` 6×6，`n`/`s` 12×6，`w`/`e` 6×14，`c` 12×14，`arrow` 13×13 origin(1,0)，`clr` = -16777216。复现：`python3 tools/imgdump.py "UI/ChatBalloon.img" 0 --depth 2`。
- 构造入口 `0x48A935`（ECX=层管理器）：先取字符串 1388 `UI/ChatBalloon.img/`；参数 type 走 `[ebp+0x18]-1000` 的 5 路分派：1000/1001 用默认节点，1002 → 5643 `pet`，1003 → 1390 `adboard`，1004 → 1593 `mob`；`[ebp+0x20]!=0` 时取 5623 `dead`。文字走九宫格函数 `0x4890C4`。
- 调用点 `0x93746C..0x9374D0`：`push 0x3e8`(type 1000)、`push 0x1388`(5000ms)、角色 `+0x1140` / `+0x1150` 两个对象、文本，`lea ecx,[edi+0x14]` 后 call `0x48A935`；`0x9375F2` 取返回值。显示期恒为 5000ms；淡出时长字段在 `this+0x64`，`0x48E6D4` 里按剩余时间做线性 alpha，具体默认值未确认。
- MapleDash：`Field/BotField.cs` 的 `OnGUI` 用原 IMG 九宫格逐块平铺绘制，文字黑色、显示 5 秒、最后 0.5 秒淡出；`Kind.Chat` 与 `Kind.Bubble` 都冒泡（Chat 另进聊天记录，对应 `BotFullChat`/`BotChatbubble` 的区别）。
- 待核实：气泡相对角色的精确锚点（当前取脚点上 84px）。`0x48B133`/`0x48B121` 传参 `+0x20`/`+0x18` 的语义、完整文字区几何仍未确认，不能假定所有气泡类型共用同一锚点。

## 假人走路姿势

- 源端 `MovementPacketConstructor.createWalkStepPacket`：`stance = movingLeft ? MOVING_LEFT : MOVING_RIGHT`，`MovementEnums.StanceValues` 里 `MOVING_LEFT=3`、`MOVING_RIGHT=2`。
- MapleDash `Server/Bot/Environment/BotRecordedActions.Step` 曾传 `left ? 1 : 0`（IDLE 站立姿势），假人走路时定格在站立帧、只有坐标在跳。已改为 3/2，并由 `tests/server-check` 的「封包步行使用 MOVING_LEFT/MOVING_RIGHT 姿势」回归守住。
- 客户端侧还要按封包节奏插值；直接每帧套用服务端坐标会表现为 10px 一跳的卡顿（`BotField` 现按 `StepSeconds=0.1` 取速度 `MoveTowards`）。

## 假人启动开销（移植适配，非源端语义）

- 源端在市场启动编排跑在后台线程；Unity 主线程同一帧连开十几个假人（解析 8.6MB `Bot/Items.json`、合成纸娃娃、建导航）会明显卡住进图。
- 现做法：`BotWarmup`（`Server/Bot/BotWarmup.cs`）在主线程取文本、后台线程解析大 JSON（WebGL 退化同步）；`BotEnvironment.MarketEnvironmentStartup` 把三波拆成每帧一步（`startupSteps` + `BotTasks.Schedule(Step,0)`）；`BotField` 每帧最多合成 2 个假人。
- 这不改变源端业务顺序（仍是一次性启动、配额与波次不变），只是把同一份工作摊到多帧。
