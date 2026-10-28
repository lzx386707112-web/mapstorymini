# 底栏（StatusBar）布局与按钮表

核实快照：2026-10-26。二进制身份见 [binary-and-tools.md](binary-and-tools.md)
（`BeiDou.exe` SHA256 `1b91cf5b…34b2`，ImageBase `0x400000`）。

本文替代此前按截图推测的底栏分组结论。所有坐标都来自 `BeiDou.exe` 的
`CStatusBar` 创建/绘制函数，并用 `UI/StatusBar.img` 原始 canvas 复合成图核对过。

## 客户区高度 578，不是 600

按钮创建函数里 `push 0x242 (578)` 作为底栏绘制区域高度出现；据此换算：
大按钮 `y=543`、小按钮 `y=515`、快捷栏 `y=499`，全部落在 578 高的客户区里，
底栏 71 高时正好 507..578。用 600 换算会让大按钮压到白条上，是错的。

| 元素 | 客户区坐标 | 底栏内坐标（y-507） |
| --- | --- | --- |
| `base/backgrnd` 817×71 | (0, 507) | (0, 0) |
| `base/backgrnd2` 570×71 | (2, 507) | (2, 0) |
| 大按钮 54×34 | x=573+56i, y=543 | y=36 |
| 小按钮 28×20 | x=618+30i, y=515 | y=8 |
| `base/box` 42×19 | (573, 515) | (573, 8) |
| `BtClaim` 20×19 | (573, 515) | (573, 8) |
| 快捷栏 466×80 | (815, 499) | (815, −8) |

`backgrnd2` 画在 x=2（不是 0）：白条与深色底板整体右移 2px，右缘落在 572，
正好接上 (573,515) 的通知槽和 (573,543) 的商城按钮。状态栏底板
（`backgrnd2` 的 y36..70 裁片）必须一起偏 2，否则 LV 铭牌与三条槽会错开。

## 按钮表（创建函数 0x008D2FA6 起）

创建调用形态为 `CreateButton(id, x, y)`，cdecl 逆序压栈 → 汇编里依次出现
`push y` / `push x` / `push id`。节点路径紧跟其前，tooltip 字符串紧跟其后。

| id | 节点 | x | y | 尺寸 |
| --- | --- | --- | --- | --- |
| 1000 | `BtShop` | 573 | 543 | 54×34 |
| 1001 | `BtNPT` | 629 | 543 | 54×34 |
| 1002 | `BtMenu` | 685 | 543 | 54×34 |
| 1007 | `BtShort` | 741 | 543 | 54×34 |
| 2000 | `EquipKey` | 618 | 515 | 28×20 |
| 2001 | `InvenKey` | 648 | 515 | 28×20 |
| 2002 | `StatKey` | 678 | 515 | 28×20 |
| 2003 | `SkillKey` | 708 | 515 | 28×20 |
| 1005 | `KeySet` | 738 | 515 | 28×20 |
| 1006 | `QuickSlot` / `QuickSlotD` | 768 | 515 | 28×20 |
| 1009 | `BtClaim` | 573 | 515 | 20×19 |

要点：

- 第二枚大按钮是 `BtNPT`，字符串池里没有这个名字，路径来自 `.data` 里的静态
  字面量 `0x00B3BAB4` = `"UI/StatusBar.img/BtNPT"`。按"池里没有就当作不存在"会漏掉它。
- `BtClaim` 与 `base/box` 同一坐标。`base/box` 是两枚白色圆角槽（各 19×19），
  `BtClaim` 叠在左槽上，右槽放便签图标。两张源端截图对"是否常驻"给了相反答案
  （2026-10-26 那张没有白槽，2026-10-27 那张两个白槽都在），**用户拍板常驻**，
  详见下面「通知槽」一节。
- `QuickSlot` / `QuickSlotD` 是同一个按钮的两种图标，由 `[esi+0xD10]` 选择
  （`cmp [esi+0xD10],0` / `je` → `QuickSlot`，否则 `QuickSlotD`）。
  方向语义用 tooltip 反查确认，不要靠箭头形状猜：
  `String/ToolTipHelp.img/Game/Button/QuickSlot` 的 `Title` = **打开快捷键**，
  `.../QuickSlotD` 的 `Title` = **关闭快捷键**。所以 `[0xD10]==0` 是"快捷栏收起"，
  图标取向上箭头 `QuickSlot`；展开时取向下箭头 `QuickSlotD`。
  （tooltip 字符串在池里的 id：`0x7cf` / `0x7d0`，节点路径 id：`0x7c0` / `0x7c1`。）
- `BtWhisper`（12×19）在 `UI/StatusBar.img` 里存在，但客户端**没有**加载它的字面量
  或字符串池条目，不属于底栏；不要往聊天行里塞。
- 字符串池里还有 `UI/StatusBar.img/BtKey`(0x7b9) 与 `UI/StatusBar.img/BtQuick`(0x7ba)，
  全二进制里 `push 0x7b9/0x7ba` 的引用数都是 **0**，是历史遗留名字。
  别因为"池里有"就给底栏补按钮。

## 按钮状态皮肤：`ani/0`、`ani/1` 不是状态贴图

12 个按钮（`BtShop`/`BtNPT`/`BtMenu`/`BtShort`/`BtClaim` 与 6 个小按钮）都有完整的
`normal/0`、`mouseOver/0`、`pressed/0`、`disabled/0` 四态。其中 7 个（`EquipKey`、
`InvenKey`、`StatKey`、`SkillKey`、`KeySet`、`QuickSlot`、`QuickSlotD`）额外带
`ani/0`、`ani/1`，各带 `delay=500`，是**两帧待机动画**，不是 normal/mouseOver。

按像素核对（28×20，numpy 全量比较）：

| 按钮 | `ani/0` 等于 | `ani/1` 等于 |
| --- | --- | --- |
| `EquipKey` `InvenKey` `StatKey` `SkillKey` | `mouseOver/0` | `normal/0` |
| `KeySet` `QuickSlot` `QuickSlotD` | `normal/0` | `mouseOver/0` |

也就是说 ani 帧恰好复刻了状态贴图，但两组按钮的对应关系**相反**。早期
`package_native_assets.py` 用像素摘要自动匹配，把 `_normal`/`_mouseOver` 配到了
`ani/*`，于是 `KeySet`/`QuickSlot` 的待机与悬停互换、`QuickSlotD_mouseOver`
被配成 `key/7`（键名字形，悬停时按钮变成一个小键帽）。现在脚本里按节点结构强制
写成 `normal/0` / `mouseOver/0`，运行期像素与 ani 帧一致，但不再依赖猜测。

`EquipKey`/`InvenKey`/`StatKey`/`SkillKey` 另有一段注册代码（0x8E0246 起）把
`<name>/ani` 登记成动画精灵，即这 4 枚图标在原端会 500ms 两帧闪动；
本项目目前只画静态 `normal/0`，未复刻闪动。

## 聊天行两态（源端两张截图各拍一种）

`base/chat`（566×5）是输入行下沿的凹线，客户端字符串池 id 2892、push 站点 `0x008D7D7D`；
该函数在 `0x008D7C57` 读 `[ebx+0xD08]` 再 `sub eax,5`，即凹线宽度 = 聊天区宽 − 5。

| 状态 | 外观 | 组成 |
| --- | --- | --- |
| 输入态 | 白色输入行 | `base/chatTarget`(81×20) 贴 x2 → 输入文字 → `base/chat` 凹线 |
| 收起态 | **深色条** + 浅色字 | 白条上压一层深色底，最后一条聊天记录用浅色字压在上面；**没有**频道按钮 |

收起态底色的实测值（2026-10-27 截图，条内 x20..430 / y45..61，剔除暗像素后的中位数）：
**RGB(124,117,116)**；同一位置在输入态截图的白色条中位数是 **RGB(253,255,254)**。
按 `255×(1-a)=115` 反推 a≈0.55，即白底上约 55% 黑。

> **行内容不是纯白**：聊天行带类型与颜色（白=玩家、粉=type5、浅蓝=type0/6），
> 由 `SERVERMESSAGE` 的 type 决定，见 [chat-and-system-messages.md](chat-and-system-messages.md)。
> 本节只管条本身的几何与底色。

同一张收起态截图右侧还有两个控件，**2026-10-27 已坐实**：

- 蓝底白十字（12×12）= `UI/Basic.img/BtMax/normal/0`（加号，展开聊天窗用）；
  展开态（自由高度）换成 `BtMin`（减号）。判断条件来自 0x8DF9C2：
  `[esi+0xd00]==3 → BtMin，否则 BtMax`。
- 竖排两个灰箭头（各 12×12）= `UI/Basic.img/VScr/disabled/prev`、`/next`
  （灰底白箭头，源端就是禁用态）。

位置：以两个白槽（原生 573..615，42×19）为基准反推这张截图的缩放 k≈0.95，
两个控件落在**原生 x≈549..561（BtMax）与 561..573（上下箭头）**，都在白条内部、白条右端，
且底下的灰底读数 ~110..120 ≈ 收起态深色条的 121 —— 说明它们画在深色条**之上**，
属于收起态的一部分。输入态（10-26 截图）同一位置是纯白，没有这两个控件——
2026-10-27 的输入态截图（截图 3）里 + 号仍在条右端，以新截图为准：**+/− 常驻，收起态另有灰箭头**。

`BeiDou.exe` 的**明文里**查不到任何 `BtHide*` / `VScr` / `BtUP` 字面量，但**它们都在加密字符串池里**
（2026-10-27 用 `tools/inspect_beidou_client.py --strings` 解出来，池共 5658 条）：

| 池 id | 串 | 池 id | 串 |
| --- | --- | --- | --- |
| 2561 | `UI/Basic.img/BtHide` | 2598 | `UI/Basic.img/BtUP` |
| 2562 | `UI/Basic.img/BtHide2` | 2599 | `UI/Basic.img/BtDown` |
| 2563 | `UI/Basic.img/BtHide3` | 2299 | `UI/Basic.img` |
| 1261 | `UI/Basic.img/BtMin` | 1262 | `UI/Basic.img/BtMax` |

**教训**：以前"查不到字面量"的结论是因为只搜了明文。节点名全部走字符串池（`push id` + `call 0x79e805`），
搜索明文必然 0 命中。后续凡是要确认某个节点是否被客户端使用，**先解字符串池再下结论**。

`BtHide` 的 10 个 `push 0xa01` 引用点都是别的窗口各自的隐藏按钮；`BtHide2`/`BtHide3`
一次 `push` 都没有（池里有、代码没引用）。`BtMin`/`BtMax`/`BtUP`/`BtDown` 见下节。

## 聊天窗面板（展开态）：CChatLog 参数（2026-10-27 反编译坐实）

函数 `0x8D4A6D SetMode(mode)`（写 `[this+0xd00]`，重复设置直接返回）：

| mode | 行为 | 依据 |
| --- | --- | --- |
| 0 | 收起（无面板，跳过） | `jne 0x8d4cf4` 直落到结尾 |
| 1 | 高 24、1 行、面板顶 513 | 0x8d4c44 |
| 2 | 高 24、1 行、面板顶 486（最小展开，加号态） | 0x8d4ba6 |
| 3 | 自由高度（减号态） | 0x8d4ab1 |

mode 3 的尺寸逻辑（0x8d4ab1 起）：

- 高度 = `[this+0x5c]`，钳制 **[26, 507]**；
- 若 `height % 13 != 0` 则 `height += 2`；
- **行高 = 13**（`idiv 13` → `[this+0xd04] = height/13` = 可见行数）；
- **面板顶 = 509 − height**，即底边固定 y=509（客户区 578）；
- `[0xc4c]` 存的是聊天字体（'Canvas#Font'，0x8D01CE `0x4d6e7a(1,4)`）的行高。

加/减号按钮由 `0x8DF9C2` 创建：`[0xd00]==3 → BtMin`，否则 `BtMax`。
所以**最小高度（mode 2 / 拖到最矮）时按钮仍是加号**，与源端截图 4 一致。

滚动条：展开面板右缘一条竖直滚动条，源端截图实测 15px 宽、
上箭头 15×13、滑块 15×25（圆角淡蓝）——对应 `UI/Basic.img/VScr5`
（enabled: prev0/prev1/next0/next1/base/thumb0/thumb1；disabled: prev/next/base）。
内容不溢出时滚动条仍在（disabled 素材）。拖顶边时在光标处显示
`BtUP`/`BtDown` 的 normal 态（橙红上下箭头）作为拉伸指示。

## 通知槽：两张源端截图互相矛盾，按常驻实现

- 2026-10-26 截图：(573,515) 的 42×19 区域均值 RGB(215,221,226)、最大 252，**没有白槽**。
- 2026-10-27 截图：同一位置两个白槽都在，左槽 `BtClaim`、右槽 `iconMemo`，逐像素形状吻合。

也就是说源端这一块是有条件出现的，两张截图正好落在两种状态下。**用户拍板按常驻实现**
（2026-10-27），所以本项目 `DrawNotifyBox()` / `DrawClaim()` 都不再加门控。
若要回到原端的条件行为，需要先接一个真实的"未读便签 / 可领取道具"数据源。


## 快捷栏底图：`base/quickSlot` 就是 26 格那张

`base/quickSlot`(466×80)、`quickSlot26`(466×80)、`quickSlot32`(571×80)、
`quickSlot08`(151×80) 是同一套底图按格数分档：

| 节点 | 尺寸 | 列 × 行 = 格数 | 宽度校验 `11 + 35n` |
| --- | --- | --- | --- |
| `quickSlot08` | 151×80 | 4 × 2 = 8 | 11+140 |
| `base/quickSlot` / `quickSlot26` | 466×80 | 13 × 2 = 26 | 11+455 |
| `quickSlot32` | 571×80 | 16 × 2 = 32 | 11+560 |

`base/quickSlot` 与 `base/quickSlot26` **逐像素完全相同**（466×80，37280 像素 0 差异），
且 `BeiDou.exe` 字符串池里只有 `UI/StatusBar.img/base/quickSlot` 一条（id 2504），
`quickSlot26/32/08` 没有任何字面量引用。所以 26 格就用 `base/quickSlot`，
别名 `ui/bar/quickSlot26 → base/quickSlot` 是对的，不用改指向。

## 空快捷格不画功能图标

源端空格子只有白底 + 左上角红色键名字形（`KeyConfig/key/<扫描码>`），没有任何功能图标。
本项目一度把 `KeyConfigWindow.Icon(action)` 铺进空格，结果 Ctrl 格上叠出一行红字
"攻击"——这是本端自加的元素，不是原端行为，已删除。原端只在已绑定技能/道具的格子里
画图标（`SkillBook` 的 `icon`）。


## 输入行必须用「新建的空 GUIStyle」，不能从 `GUI.skin.textField` 复制

白条是 `base/backgrnd2` 自带的，输入行只能画字。踩过的坑：

1. 只清 `normal`/`focused` 的 `background` → 条内实测 RGB(42,42,42)，白条被盖成深灰；
2. 把 8 个状态的 `background` 全置 `null` **仍然不行**——`GUIStyle` 从皮肤复制后，
   皮肤态会回落到图集，条内还是 (42,42,42)（正好是 Unity 深色皮肤 textField 的 `#2A2A2A`）；
3. 正解：`new GUIStyle { font = UiKit.Font, fontSize, alignment, padding/border/margin = 0 }`，
   一个全新实例任何状态都没有底图。改完实测条内 **RGB(252..255)**，与源端白条一致。

## 迁移状态

- `BottomBar.cs`：按上表画 4 大 + 6 小；通知槽 `base/box` + `base/iconMemo` + `BtClaim`
  **常驻**（用户 2026-10-27 拍板，见上节）；聊天行两态由 `ChatFocused || channelOpen` 切换。
- 聊天窗面板（2026-10-27 补）：+ 收起 / − 展开自由高度、最小高 24（1 行）、
  行高 13、底边距客户区底 69、VScr5 滚动条、顶边拖高（BtUP/BtDown 指示）；
  素材别名 `ui/bar/BtMin_*`、`ui/bar/BtMax_*`、`ui/bar/VScr5_*`、`ui/bar/VScr5d_*`、
  `ui/bar/VScrD_*`（收起态灰箭头）已进 NativeAssetIndex 与打包脚本。
- `StatusBar.cs`：底板跟随 `BottomBar.PlateX = 2`，三条槽仍按 `gauge/bar` 的
  340×31 裁片布局（HP 109 / MP 108 / EXP 123，起于底板 x216）。
- 仍是自绘（原端也用字体排版，不算 canvas）：职业/昵称、聊天频道名、聊天历史行。

## 复现

```sh
# 按钮表：扫 push imm32 定位节点字符串，再按 CreateButton 的压栈顺序取 x/y/id
/tmp/mapledash-reverse-venv/bin/python tools/inspect_beidou_client.py \
  ../../Beidou-Server/clien/BeiDou.exe --start 0x8d2fa6 --end 0x8d3700
```
