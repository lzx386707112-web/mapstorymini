# 聊天窗（UI/UIWindow.img/Messenger）：模式、控件坐标与座位布局

核实快照：2026-10-30。二进制身份 `BeiDou.exe` SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，ImageBase `0x400000`。
本文只讲"聊天窗这一个窗口"的几何；底栏聊天行/展开面板见 [statusbar-layout.md](statusbar-layout.md)，
消息类型与落点见 [chat-and-system-messages.md](chat-and-system-messages.md)。

## 一、窗口类与素材（字符串池已解）

| 池 id | 串 |
| --- | --- |
| 1861/1862/1863 | `UI/UIWindow.img/Messenger/backgrnd` / `backgrnd2` / `backgrnd3` |
| 1864 | `UI/UIWindow.img/Messenger/charView` |
| 1858 / 1860 | `UI/UIWindow.img/Messenger/blink` / `chatBalloon` |
| 1867 | `UI/UIWindow/Messenger/NameBar/%d`（注意路径少一个 `.img`，实际取子节点仍成功） |
| 2722 | `UI/UIWindow.img/Messenger/BtEnter`（橙字 ENTER，39×17，normal/pressed/disabled/mouseOver） |
| 1261 / 1262 / 5577 | `UI/Basic.img/BtMin` / `BtMax` / `BtClose`（标题栏三键，12×12，**不是** Messenger 自己的 BtMin/BtMax） |
| 956 | `Canvas`（窗口底图挂在名为 Canvas 的子节点上，CWnd::Draw 0x9E0502 按原生尺寸贴图，不拉伸） |

`UI/UIWindow.img/Messenger/BtMin`、`BtMax` 也在池里（1865/1866），但聊天窗代码没引 —— 那两条属于别的窗口。

`NameBar/0..3` 四张 89×19：源端用 `NameBar/(seat+1)`，座位 0/1/2 取 1/2/3，**0 号没用**。

## 二、三种模式与尺寸（0x850264）

`[this+0x6c]` = mode；尺寸写 `[this+0x24]`（宽）、`[this+0x28]`（高）：

| mode | 尺寸 | 底图 | 依据 |
| --- | --- | --- | --- |
| 0 | 295×364（0x127×0x16c） | backgrnd | 0x85032a |
| 1 | 392×240（0x188×0x0f0） | backgrnd2（295×240，**只贴 295 宽**） | 0x85030d |
| 2 | 184×22（0x0b8×0x016） | backgrnd3 | 0x8502f0 |

mode 1 是"逻辑 392 宽 + 295 宽底图"，右侧 97px 没有素材可贴；照抄原端，不要自行拉伸或补画。

## 三、标题栏三键（0x8505e2）

创建顺序与坐标（cdecl 压栈反序：`this, id, x, y, ?, rect`）：

| id | 素材 | x（mode 0/1） | x（mode 2） | y | 依据 |
| --- | --- | --- | --- | --- | --- |
| 1000 | `UI/Basic.img/BtClose` | 276（0x114） | 166 | 6 | 0x8506b0 / 0x850759 |
| 1001 | `UI/Basic.img/BtMin` | 248（0x0f8） | 138 | 6 | 0x85075f |
| 1002 | `UI/Basic.img/BtMax` | 262（0x106） | 152 | 6 | 0x85081a |

- y：`0x8505f1..0x850612`：`eax = (mode != 2) ? 1 : 0` 再 `add eax, 5` → **mode 0/1 为 6，mode 2 为 5**。
  12px 高的按钮落在底图标题栏白条（原生 y≈5..21）中间；写成 y=2 会顶到白条上沿（本项目旧实现的偏差）。
- x 收起态：三处都是先算 `(mode != 2) ? 0 : -110` 再 `add`，故等于正式态减 110。
- **mode 0 时 id 1002 置灰**：`0x850824 cmp [this+0x6c],0` → `Enable(false)`。所以完整态截图里
  最大化键是灰的不是 bug。

## 四、日志 / 滚动条 / 输入行（0x8505e2）

| 控件 | mode 0 | mode 1 | 依据 |
| --- | --- | --- | --- |
| 滚动条 id 1006 | (273, 148)，高 154 | (273, 55)，高 125 | 0x850b4c / 0x850943 |
| 可见行数 `[this+0x70]` | 11 | 9 | 0x850afe(=0xb) / 0x8508fb(=0x9) |
| 行首索引 | `count - lines + 1`（自动贴底） | 同 | 0x85094c..0x850968 |
| 行宽 `[ctrl+0x34]` | 250（0xfa） | 250 | 0x85096d / 0x850b83 |
| 输入框 id 1005 | (9, 318, 207, 15) | (9, 194, 207, 15) | 0x850bcf / 0x8509c6 |
| 发送键 id 1003（BtEnter 39×17） | (249, 317) | (249, 193) | 0x850cfe / 0x850af5 |

底图核对（`backgrnd` 295×364）：白色日志框 x 9..265、y 150..301；白色输入条 x 8..246、y 317..331。
所以 **ENTER 的 249 起点落在输入白条右侧的灰条里**，39 宽到 288，不压输入框边框。
旧实现写 225（`edit.xMax - 39 + 8`）会盖住白条右边框，是可见的错位来源。

日志行高 13（11 行 ×13 = 143，容于 152 高的白框）。

另一条 id 1004 也用 BtClaim 素材建在 (225,315)（mode 1 为 (225,191)），随后被 `[eax+0x1c]` 以 0 调用，
**未确定其语义**（隐藏？还是别的状态位），所以本项目不画它；后续若要补，坐标已在此。

## 五、三个座位（0x84f44b / 0x84fff8）

- 座位索引 `i` 的字段基址 `[this + 0xc8 + i*0x20]`。
- 小人：`0x84f49d imul eax,eax,0x5e`（94）+ `0x84f4a7 add eax,0x36`（54）→ x=54+94i；
  `push 0x76`（118）→ 脚点 y=118。
- 名字条 89×19 画在 x=9+94i；名字文本水平居中于条内 45px 处（`0x84f892 push 0x2d`，
  `45 - textWidth/2`），本项目用 `MiddleCenter` 等效。
- 名字条上沿 122（**视觉核对值**：与 backgrnd 里三处"空位灰条"下缘相接，灰条本身在 y 116..121）。
  只有有人的座位才贴 NameBar 画布，空位露出底图灰条。

## 六、迁移状态与边界

- `Assets/Scripts/NativeMessengerWindow.cs` 按上表出图：`SizeOf/TitleClose/TitleMin/TitleMax/Scroll/Edit/Enter/Lines/SeatAnchor/SeatNameBar`
  都是纯函数，`Assets/Editor/MessengerLayoutChecks.cs` 对其做无渲染断言。
- 未迁移：`blink`（对方输入中）、`chatBalloon`、`charView` 专用控件、模式 1 的右侧空白区、
  "点名字条邀请/私聊"这类点击行为。
- 只还原 mode 0 做过实机核对；mode 1/2 坐标照抄但未实拍。

## 七、复现

```sh
# 尺寸表 / 控件坐标
python3 Tools/inspect_beidou_client.py ../../Beidou-Server/clien/BeiDou.exe --fn 0x850264
python3 Tools/inspect_beidou_client.py ../../Beidou-Server/clien/BeiDou.exe --fn 0x8505e2
# 座位与名字条
python3 Tools/inspect_beidou_client.py ../../Beidou-Server/clien/BeiDou.exe --fn 0x84f44b
# 字符串池（节点名全在池里，明文 grep 必 0 命中）
python3 Tools/inspect_beidou_client.py ../../Beidou-Server/clien/BeiDou.exe --strings /tmp/pool.json
# 底图坐标核对
python3 Tools/imgpng.py UI/UIWindow.img Messenger/backgrnd /tmp/msgr.png --scale 2
```
