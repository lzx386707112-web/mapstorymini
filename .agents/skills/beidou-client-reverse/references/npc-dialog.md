# NPC 对话窗口（UIWindow.img/UtilDlgEx）

核实快照：2026-10-26。二进制身份见 [binary-and-tools](binary-and-tools.md)（SHA256 `1b91cf5b…f134b2`，ImageBase `0x400000`）。
本文件区分「已用 IMG/汇编核实的原端行为」与「MapleDash 当前实现」，未核实的推测单列在末尾。

## 窗口素材（`UI/UIWindow.img/UtilDlgEx`）

| 节点 | 尺寸 | 职责 |
| --- | --- | --- |
| `t` | 529×28 | 顶部。第 3..7 行外框，第 8..12 行深色描边，**第 23 行起是白纸** |
| `c` | 529×20 | 中段，按 20 行平铺。**第 152 列起是白纸**，第 506 列是纸面右描边，529 已是窗口外阴影 |
| `s` | 529×58 | 底部。第 0..13 行仍是纸面（**第 14 行是纸面下边**），20..27 行白边，28..52 行蓝灰底板（按钮区），53..57 外框 |
| `bar` | 121×19 | NPC 名字条，起于窗口左侧 18 |
| `dot0` / `dot1` | 6×7 | 列表行的圆点，悬停 / 常态 |
| `list0`..`list3` | 119×14 / 102×18 / 26×14 / 106×14 | 脚本用 `#fUI/UIWindow.img/UtilDlgEx/listN#` 插进来的分组标题（客户端里按 `UtilDlgEx/list%d` 拼路径，字符串常量 3235） |
| `line` | 332×2 | 分隔线；宽度也是正文可用宽度的旁证 |
| `notice` | 311×134 | 内嵌说明图（`UI/UIWindow.img/UtilDlgEx/notice` 被 EULA 等文本引用） |
| `ic` / `it` / `is` | 529×20 / 529×28 / 529×58 | 带图片正文的变体框（与 c/t/s 同尺寸） |
| `BtClose` | 85×19 | 关闭 |
| `BtPrev` `BtNext` `BtOK` `BtYes` `BtNo` | 46×18 | 页内按钮 |
| `BtQYes` `BtQNo` `BtQGiveup` | 57×17 | 任务「接受/拒绝/放弃」 |

窗口固定 **529** 宽；纸面可用矩形（相对窗口左上角）= x `152..506`、y `23..(高-45)`。
正文本身按 `332` 宽排版（`line` 节点宽度），即纸面左右各留约 4px、右边再留一条 15px 的滚动条列。

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

## 待核实

- **UtilDlgEx 正文滚动用的是哪一号样式**：对话模块（`UtilDlgEx` 节点构造集中在 `0x9A7CEB`..`0x9AA600` 一带的 `push 0x8CC`）里没有找到对 `0x4D6D96`/`0x4D6E7A` 的直接调用，
  说明正文滚动条不属于这个通用滚动条类，或由别的类二次封装。下一步应从对话窗口类里找正文字段的滚动条成员再定位样式号。
  在核实之前，MapleDash 暂用 `VScr4`（与物品/技能窗一致，纯 IMG 像素、无自绘）。

## MapleDash 迁移现状

- `Assets/Scripts/Npc/DialogText.cs`：脚本原文 → `DialogRun` 列表（Text / Link / Icon）。
  `#L<idx>#...#l` 是**正文流里的行内选项**，不再抽成独立列表（旧实现把选项搬到正文末尾，长菜单会把正文挤成空白）。
  `#i/#v` 画道具 `info/icon` 原图，`#f` 画给定 IMG 路径；都按原始像素、行高随图标长高。
- `Assets/Scripts/Npc/DialogFlow.cs`：纸面矩形取自上面表格，正文宽 328（≈`line` 的 332），
  自由折行（行首不折行、折行处丢行首空格），滚动条用 `UI/Basic.img/VScr4/enabled` 的 prev/next/base/thumb，
  支持点箭头翻行、拖滑块、滚轮。任务菜单（`DialogContent.Selections`）仍按 dot0/dot1 + `listN` 标题图渲染。
- 验证：`Assets/Editor/QuestNpcChecks.cs`（对白标记解析）+ 批处理 Play 截图（npc 9900001 主菜单、超长文本滚动、任务菜单）。

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
