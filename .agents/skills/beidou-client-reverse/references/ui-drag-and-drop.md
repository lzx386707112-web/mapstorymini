# UI 拖动语义：键位窗与技能窗（2026-10-26 取证）

结论只对应 `BeiDou.exe` SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，
ImageBase `0x400000`（地址均为 VA）。复现入口 `tools/inspect_beidou_client.py`。

**核心结论：源端的「搬东西」是点击语义（按下即 DragStart，下一次按下放下），不是按住拖动。**
证据是这四个窗口的鼠标处理函数都只有 `WM_LBUTTONDOWN(0x201)` 分支，没有 `WM_LBUTTONUP(0x202)` 分支。

## 全局鼠标消息分派

`0x94CF7A`：`sub eax, 0x201` 之后用 `dec/je` 连成 switch（`0x94cf8c..0x94cf9b`）

| msg | 处理函数 |
| --- | --- |
| `0x201` WM_LBUTTONDOWN | `0x94FA64` → 取鼠标坐标后 `call 0x48E51C` |
| `0x202` WM_LBUTTONUP | `0x94FA8E` → 取鼠标坐标后 `call 0x48E631` |
| `0x203` WM_LBUTTONDBLCLK | `0x94FBbf` |
| `0x205` | `0x95009a` |

即：消息先归一化再分派到 CWvsApp 层，窗口自己的 `OnMouseButton(msg, ...)` 拿到的就是 Windows 消息号。

## 技能窗 OnMouseButton `0x8ABA6B`

身份线索：`this-4` 是内嵌控件；命中 `call 0x8ad946(x, y, 1)` 取条目 index；
技能 id 取 `[[edi+8] + index*8 + 4]`，随后 `idiv 0x3e8`（1000）判职业；字符串池 `44='Archer'`、`10='Eqp'`。

```asm
008aba9d cmp      dword ptr [ebp + 8], 0x201     ; 只处理 WM_LBUTTONDOWN
008abaa4 jne      0x8abee4
...
008abe8b push     esi
008abe8c push     ecx
008abe8d mov      ecx, dword ptr [0xbec20c]
008abe97 call     0x9e353d
008abe9f push     0x737                          ; 字符串池 1847 = "DragStart"
008abea5 call     0x79e805                       ; GetString
008abeac call     0x406276
008abeb1 push     dword ptr [eax]
008abeb3 call     0x989588                       ; 派发 DragStart
...
008abee4 cmp      dword ptr [ebp + 8], 0x203     ; 双击
008abeeb jne      0x8abf60                       ; 其余消息（含 0x202）直接返回
008abf02 mov      eax, dword ptr [0xbebf98]      ; CUserLocal
008abf12 cmp      dword ptr [0xbe7910], ebx      ; 非 0 就不处理
008abf5b call     0x966f7a                       ; 双击 = 使用/施放该技能
```

要点：**左键按下就发 `DragStart`（字符串池 1847）**；松开（0x202）在技能窗里不做任何事；
双击走 0x203 → `0x966f7a`（user 为 this 参数，技能 id 为入参）。

## 技能组合栏（宏窗）OnMouseButton `0x8A98FE`

身份线索：`0x8a9980 push 0x1041` → 字符串池 4161 = `UI/UIWindow.img/SkillMacro/Macroicon/%d/icon`。

```asm
008a990e cmp      dword ptr [ebp + 8], 0x201
008a991b jne      0x8a9f40                       ; 非按下直接退出（含 0x202）
008a9942 call     0x8a97c4                       ; 命中测试（x, y, 1, &kind, &index）
008a9955 call     0x8a984d                       ; 记录当前技能（成员 +0x5a4）
008a9ee9 push     0x737                          ; "DragStart"
008a9f01 call     0x989588
```

命中返回两个值：`[ebp-0x24]` 为类别（等于 3 走宏图标分支），`[ebp-0x14]` 为技能/条目。
同样是「按下即 DragStart」，没有松开分支。

## IMG / 服务端

- 组合栏素材：`UI/UIWindow.img/SkillMacro/Macroicon/%d/icon`、`macroslot`、`macroname`（字符串池 4155~4161）。
- 键位窗功能图标：`UI/UIWindow.img/KeyConfig/icon`（40 个：0..27、50..54、100..106）。
- 光标素材：`UI/Basic.img/Cursor` 共 14 组；`11`（22×21 张开手）、`12`（25×23 握拳）。
  **源端用哪一号表达"可抓/抓着"未定位**，11/12 是本项目按素材形态选定的映射（待核实）。
- 服务端：`RecvOpcode.CHANGE_KEYMAP(0x87)` → `KeymapChangeHandler`：mode 0 发多条 `(key, type, action)`，
  mode 1/2 是自动 HP/MP 药（键 91/92，type 7）。技能绑键落库走这条；**协议不能反推交互方式**。

## 迁移状态（2026-10-26）

| 窗口 | 运行时代码 | 已迁移 |
| --- | --- | --- |
| 键盘设定窗 | `KeyConfigWindow.PointerStep / PickAt / DropAt / PickAction / DropOnKey / ReturnToTray` | 是：按下拿起、再按下放下、托盘区收回、Esc 只取消、光标 11/12 |
| 技能窗 / 组合栏 | `SkillWindow`（`carrying`、`HandleDrop`、`DropOnSlot`、`UpdateCursor`） | 是：按下即拾起（不等 4px 位移、不等松手）、下一次按下落下、点空白取消、双击仍是施放、光标 11/12 |
| 底部快捷格 | `BottomBar.DrawQuickSlots` | 手上有技能时点击 = 放下（不再施放），右键 = 取消 |

回归：`NativeKeyboardChecks.CheckKeyConfigCarry`、`CheckSkillWindowCarry`。

## 待核实

- `0x989588` 派发的 `DragStart` 事件：参数（Ztl variant）与接收方未解析，只确定"在 0x201 分支里发出"。
- 「放下」的接收端（快捷栏 `CUIStatusBar` / 键位窗对应的 OnMouseButton）尚未定位，
  因此不能证明源端"落到快捷格"落在哪个函数；当前迁移是按"下一次按下"实现的。
- 源端光标状态编号（是否就是 11/12）未定位。
