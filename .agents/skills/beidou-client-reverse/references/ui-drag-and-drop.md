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

2026-10-27视口修复：Stat/Equip/Item/Skill保持原IMG内部几何，对整个窗体应用手机视口约束；技能窗标题拖动后立即同步主窗绘制/命中矩形，组合栏按原重叠2px一起约束。此处是标题栏位置修复，不改变本页已核实的技能/键位“按下拿起、再按下放下”语义。新增`NativeViewportChecks`在五种GameView尺寸注入Pointer检查，82项PASS；具体范围见 [DLL 与手机适配](ijl15-mobile-adaptation.md)。

## 待核实

- `0x989588` 派发的 `DragStart` 事件：参数（Ztl variant）与接收方未解析，只确定"在 0x201 分支里发出"。
- 「放下」的接收端（快捷栏 `CUIStatusBar` / 键位窗对应的 OnMouseButton）尚未定位，
  因此不能证明源端"落到快捷格"落在哪个函数；当前迁移是按"下一次按下"实现的。
- 源端光标状态编号（是否就是 11/12）未定位。

## 背包物品携带与type2键位（2026-10-30）

用户本轮明确要求背包物品同样点击拿起、下一次点击放下；沿用本页已核实的点击交互模型。
NativeInventoryActions.Pick/CarryPointerStep由背包原格命中接入，消耗品、椅子、现金物品可携带；装备/材料未改为直接使用快捷物品。
KeyConfigWindow沿用现有原键帽命中与草稿提交，底栏沿用SlotRect；不新增或重画控件。图标只取NativeItemSource原IMG。
协议依据源KeymapChangeHandler及原keymap type：物品type2/action为正物品ID，技能type1/action为技能ID；
内部QuickSlots以负物品ID区分，但进出数据库边界还原type2/正ID，不用ID数值范围猜技能或物品。
放下动作消费当次按下和随后的释放，避免绑定药水时顺带喝掉；右键/Esc取消，不扣物品。
光标11/12仍继承本页的项目映射待核实边界，没有新增它们是原端确定编号的结论。

2026-11-01补核：技能窗与虚拟控制层曾重复绘制拖拽图标，现仅顶层绘制；技能行与VScr4相交处让滚动条先消费按下。未改按下拿起/再次按下放下语义，证据见[五项补核](five-issues-followup-2026-11-01.md)。

## 背包物品丢到地上（2026-11-02）

**源端没有独立的 ITEM_DROP 包**（`RecvOpcode` 里 `0x49 CANCEL_ITEM_EFFECT` 之后直接是 `0x4B USE_SUMMON_BAG`，`0x4A` 空位）。丢弃是 `ITEM_MOVE(0x47)` 的一个分支：

```
ItemMoveHandler.java:44
  } else if (action == 0) {
      InventoryManipulator.drop(c, type, src, quantity);
```

包体：`int tick` → `byte type` → `short src` → `short action` → `short quantity`；`src<0` 视为已装备栏（先脱再丢）。`InventoryManipulator.drop`（:835）的判定顺序：

1. `chr.isGM() && gmLevel < minimum_gm_level_to_drop` → 拒绝；交易/小游戏中或该格不存在 → 直接返回。
2. `(!ItemConstants.isRechargeable(itemId) && source.quantity < quantity) || quantity < 0` → 返回（**不改背包**）。`isRechargeable` = `itemId/10000 == 207`。
3. `quantity < source.quantity` 且非可充能 → 复制一份 `quantity` 丢出去，原格 `quantity -= 数量`，回包只改这一格。
4. 否则整格 `removeSlot`（已装备栏先 `unequippedItem` 再删，并 `equipChanged()`）。
5. 落地一律 `map.spawnItemDrop(chr, chr, item, dropPos, true, true)`：`dropper=owner=本人`、`playerDrop=true`、`ffaDrop=true` → dropType 2（谁都能捡）；`dropPos = chr.getPosition()`（脚下，不是鼠标点）。`isDisappearingItemDrop`（untradeable 且开 `use_erase_untradeable_drop`）才走 disappearing 支路。
6. 收尾：若丢的是正在生效的 item effect / 黑板 / 阿里安特宝石，分别清效果、关黑板、更新分数。

迁移：客户端 `NativeInventoryActions.PickFromInventory`（背包格点一下拿起，装备也允许携带）→ 落在窗口与底栏之外的地面时 `DropToField`，可堆叠道具先问「扔掉多少？」（字符串池 885）；服务端 `NativeInventoryService.DropToField` 照抄上面 2~5。回归 `tools/server-check` 的 `DropChecks`（19 项：拆堆/整堆/超量/0/负数/装备行连带删除/dropType2/归属）。

## 数量/文本输入框的移动端软键盘（2026-11-02，本次补接入）

### 2026-11-02 数量窗外观补核（覆盖此前“肉眼一致”的结论）

同一 BeiDou.exe SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2` / ImageBase400000。
`9AC552` 将 A4（compact）传给 `99DD54`；`99DD7A..99DDA0` 将非零参数映射到默认字体索引4（白色，字体初始化见 npc-dialog）。因此 compact 正文不是黑色；编辑控件 `9A8424` 则明确 FF000000。正文测量宽225（`9A3D07` compact分支），内部margin2（`9A3DF6`），不能直接用GUIStyle.CalcHeight代替原正文流。
`9ACD54`正文原点20,22，总高contentHeight+99。**控件分支纠正**：此前误用非compact的`9A83F6`（黑白框、左下BtClose），这些不能用于丢弃数量窗。背包丢弃字符串885的调用点`4F384C`进入`4F55C4`，`4F561F..4F5644`传compact=1；`9A6370`的`9A63A5..9A63BC`按+A4分派，非零调用`9A8716`，零才调用`9A83F6`。compact编辑框为(19,height-73,226,15)，没有非compact的额外黑白框绘制；`9A87EE`字符串1257=`UI/Basic.img/BtOK2`，位置(158,height-30)，`9A886F`字符串1258=`UI/Basic.img/BtCancel2`，位置(208,height-30)，均使用原始47×19。所以丢物品窗是右下确定+取消，不是单确定，也没有左下结束对话。金币`81D996`也传compact=1。当前NativeUtilPrompt数量分支已据此纠正；保留Esc取消/回车提交。新增复现汇编见`docs/skill-regression/compact-number-branch-correction-2026-11-02.txt`。
背景仍为Notice3/t(266×21)、c(266×20)、Notice4/s(266×78)。代码仅照搬已核实的黑白输入框绘制；其他素材继续直接取IMG。
复现：`tools/inspect_beidou_client.py <exe> --fn 0x9a83f6`，持久汇编见 `docs/skill-regression/compact-number-controls-2026-11-02.txt`；排版使用既有DialogFlow输入模式，默认#w、margin2，数字单独黑字。Windows Arial与Unity中文字形差异仍是迁移边界；未作Android截图验收。

**结论：数量弹窗本身早已接入，但「点击输入框拉起系统键盘」此前完全没接**， Android 真机点输入框不弹或行为不可靠。本次补齐并统一收口。

### 排查结论（改动前）
- 丢弃 `NativeInventoryActions.cs:73` `DropToField`：可堆叠（`quantity>1` 且 `id/1000000!=1` 非装备）弹 `NativeUtilPrompt.Number("扔掉多少？", …)`；装备/单件直接丢 1 个。
- 出售 `NativeNpcShopWindow.cs:88` `SellSelected`：可充能道具或 `quantity<=1` 走 `Confirm`，其余弹 `NativeUtilPrompt.Number("要出售多少个？", …)`。购买 `BuySelected` 同样走 `Number`。
- 弹窗本体 `NativeUtilPrompt.cs`：`Number/NumberLong/Text/Confirm` 四种，数值用原图 `Notice3/Notice4` 切图（原错误实现套用 `UI/UIWindow.img/UtilDlgEx/BtOK|BtClose`，现已纠正为compact的Basic按钮），输入框是裸 `GUI.TextField`（数值过滤非数字、最多 19 位），Esc 取消 / 回车提交、`min..max` 校验。**全文件没有任何 `TouchScreenKeyboard.Open`，输入框矩形也没有点击命中检测**。
- 全工程显式拉软键盘原本只有 `LoginFlow.cs`（账号/密码/角色名）、`BottomBar.cs`（聊天）两处；`VirtualControls.cs` 只读 `visible/area` 做避让。
- **决定性对照**：`LoginFlow.cs:826` 同样是 `GUI.TextField`，但点击时（`Application.isMobilePlatform`）额外调 `OpenKeyboard` → `TouchScreenKeyboard.Open`，并注释「关掉再重开在 Android 上是真会失败的」——证明本工程不信任 IMGUI 输入框自动弹键盘，必须显式拉起。数量弹窗缺的正是这段。

### 迁移（新增 `NativeEditKeyboard.cs` 共享桥 + 三处接线）
把登录页 / 聊天已验证的两条时序坑抽成一处，别再各自重写：
1. **同框不重开**（`LoginFlow.cs:843-846` 原注释）：Android「`active=false` 再 `Open`」真会失败（返回非空但不弹）。`ShouldReopen(owner, active, key)`：同 owner 且键盘 active → 不重开；换输入框 / 已收起才允许重新 Open。
2. **只在 Done/Canceled 收键盘**（`LoginFlow.cs:194-199` 原注释）：刚弹出头几帧 status 还不是 `Visible`，切前后台会短暂 `LostFocus`；按「非 Visible 就收」等于刚弹出来就被自己收走。`Apply` 里 `Visible/LostFocus` 一律返回 `None`、保留键盘与归属。
3. **每 GUI 事件先读快照再消费 Enter**（`BottomBar.DrawChat:300-302` 原注释）：Android 输入法最终合成文本可能与 `Done/Enter` 同帧到达；`Done` 与回车同帧只提交一次（`Done` 后桥已 Close，同帧再 `Sync` 返回 `None`）。`Canceled` 收键盘但不提交。

接线三处（owner 字符串隔离，避免 A 框键盘被 B 读走）：
| 输入框 | 文件 | 命中坐标口径 | 键盘类型 | 提交 |
| --- | --- | --- | --- | --- |
| 数量 / 文本弹窗 | `NativeUtilPrompt.cs` | `Pointer.Pos/scale`（IMG 坐标，`editRect`） | numeric→NumberPad / 文本→Default | `Done`/回车 提交，Cancel 只收键盘；`Clear/Cancel` 收键盘 |
| CUtilDlgEx 文本窗 | `NativeDlgInput.cs` | `Pointer.Pos`（屏幕像素，`field`） | Default | `Done`/回车 提交；`Close` 收键盘 |
| 仓库金币双框 | `NativeStorageWindow.cs` | `p`（group 内 IMG 坐标，`takeRect/putRect`） | NumberPad | **不改交易口径**：仍走 BtOutCoin/BtInCoin，`Done` 只收键盘不代提交；`Reset` 收键盘 |

命中统一 `EventType.Repaint && GUI.enabled && Pointer.Clicked && rect.Contains(pointer)` → `Pointer.ConsumeClick()`，与 `NativeUi.Button` 同口径；输入框矩形与各窗口按钮不重叠，点击消费不冲突。

### 验证与边界
- `Assets/Editor/NativeEditKeyboardChecks.cs`（`UNITY_EDITOR` 注入快照，走真机同一条 `Sync/Apply` 路径）：覆盖最终文本回灌、null 文本不覆盖、Visible/LostFocus 不收、Done 回灌且幂等、Canceled 不提交、`ShouldReopen` 四种组合、owner 隔离、三处关闭路径收键盘、数量弹窗开合。
- **边界**：以上为纯逻辑 + dotnet 自检，**未连 Android 实机**；IMGUI `GUI.TextField` 自身在移动端的输入行为仍以显式键盘为权威，不宣称实机手感验收。丢弃/出售业务门控由既有 `DropChecks` / `npc-shop-check` 覆盖，本次未改服务端与回调链。

## 丢弃物品的门控（按物品类型，2026-11-03 反汇编）

同一 BeiDou.exe SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2` / ImageBase `0x400000`。
**地址口径坑**：`pe.get_memory_mapped_image()` 返回的是 **RVA 索引** 的字节，真实 VA = 索引 + ImageBase；直接把 RVA 当 VA 会整体高 0x400000（本次曾因此失步）。

丢弃入口函数（含 0x4f3430..0x4f38b1），流程：先建婚戒对话框 → 若干类型判定 → 数量弹窗(885) → 发 ITEM_MOVE。判定的字符串引用点（VA）：

| 字符串 id | 文案 | 引用点 | 触发条件（已核实） |
| --- | --- | --- | --- |
| 882 (0x372) | 这种道具不能扔掉。 | `0x4f3887` | 现金道具 `info/cash==1`（`0x5d4a2b` 读字符串2037=`cash`）；以及 itemId/1000==4220 段 |
| 5571 (0x15c3) | 这种道具丢弃后无法再次获得还想丢弃吗？ | `0x4f35b7` | 任务道具 `info/quest==1`（`0x5d50b8` 读2973=`quest`）；点确定仍可丢 |
| 4658 (0x1232) | 丢弃物品后获得的话，会变成无法交易状态… | `0x4f3658` | 不可交易/`info/tradeBlock==1`（`0x5d4db9` 读3186=`tradeBlock`；`0x5d28ae` 组合 vtable[0x10] 或 tradeBlock） |
| 885 (0x375) | 扔掉多少？ | `0x4f382a` | 可堆叠且数量>1（`[item]+0x20` 取数量） |
| 4259/4260 | 丢弃订婚取消… | `0x4f3430`/`0x4f347c` | 婚戒 |
| 4875 (0x130b) | 你想扔掉这个卡片? | `0x4f3790` | itemId/10000==430（卡片段） |
| 3416 (0xd58) | 已封印的道具无法贩卖、交换或丢弃。 | `0x4f376c` | 封印道具（item vtable[+4] 非 0） |
| 4558 | 这里无法丢弃物品。 | `0xe09067`/`0xe23e25` | 地图禁止丢弃 |

服务端对照（权威）：`ItemInformationProvider.isDropRestricted = isLootRestricted(info/tradeBlock || accountRestricted) || isQuestItem(info/quest)`；
`InventoryManipulator.drop` 本身**不拦**任务物品，但 `isDisappearingItemDrop`（含 isDropRestricted）会让它走 `map.disappearingItemDrop`——丢出即消失、不能拾取。现金道具由客户端拦，服务端 `drop` 未显式判 cash。

**本项目落地（2026-11-03）**：`NativeInventoryActions.DropToField` 先按 `NativeItemSource.Node(id).Get("info")` 判定：
cash→`Notice("这种道具不能扔掉。")`；quest→`Confirm("这种道具丢弃后无法再次获得，还想丢弃吗？")`；tradeBlock→`Confirm("…会变成无法交易状态…")`；确认后进入数量弹窗/直接丢。
服务端 `NativeInventoryService.DropToField` 兜底：cash 直接拒绝；`info/quest` 丢弃后**不生成可拾取地面掉落**（等价 disappearing，无法再次获得）。
封印/婚戒/卡片/4220xxx 本项目暂无对应系统，记录在案未接。

## 副本独占道具出本自动消失（2026-11-03）

源端 `EventInstanceManager`：`setExclusiveItems(list)` 记录副本独占道具（多为任务/通行证），
`unregisterPlayer(chr)` 末尾 `dropExclusiveItems(chr)` → `api.removeAll(item)`；`dropAllExclusiveItems()` 对全员清一次。
`Character.respawn(eim,returnMap)` 在死亡返回时会 `eim.unregisterPlayer(this)`（`Character.java:7598`），普通换图不自动清，
其余由 PQ 脚本 `em.exitPlayer/unregisterPlayer` 驱动。`dispose()` 本身不直接清独占道具。
脚本侧：`npc_scripts.json` 里 50+ 个 `event/*` 调 `setExclusiveItems`，其中 Kerning/Ludi/Orbis 调 `dropAllExclusiveItems`。

**本项目落地**：`NativeEventScriptService` 已绑定 `setExclusiveItems`/`dropAllExclusiveItems`，`Instance.Unregister` 会
`removeAll(exclusiveItems)`（对齐源端 unregisterPlayer），`PqInstanceChecks` 有 `dropAllExclusiveItems→removeAll` 覆盖。
结论：该机制已实现且忠实源端；若个别副本不消失，问题是该副本脚本未调 exitPlayer/setExclusiveItems，需按副本名单独查。


## 背包格到背包格：移动 / 合并 / 互换（2026-11-03）

二进制身份同上（SHA256 `1b91cf5b…f134b2`、ImageBase `0x400000`）。复现：
`python3 tools/inspect_beidou_client.py <clien/BeiDou.exe> --fn 0x4F3057`、`--fn 0x4F31A5`、`--fn 0xA0900A`；
调用点扫描按 `E8 rel32` 找 `0xA0900A` 的 5 个射点：`4F116A / 4F193F / 4F2B84（穿戴）/ 4F3131（见 0x4F3057）/ 4F3873（丢弃）`。

### 客户端

- **ITEM_MOVE(0x47) 打包函数 `0xA0900A`**：`push 0x47` 建包（`0x6ec9ce`）→ `0x987257` 写 tick →
  `0x406549` 写 **byte type** → `0x427f74` 连写 **三个 short = src / action / quantity** → `0x49637b` 发送。
  与服务端 `ItemMoveHandler`（skip4 → byte type → short src → short action → short quantity）逐字段对齐。
- **点击背包格 `0x81D42E`**（InvenWindow 的 `OnMouseButton`，只处理 `0x201`）：命中格号 `0x81DB7E(this, x, y)`
  （从 `+0x5e0` 的首格号开始逐格 `0x81E2C8` 算矩形做 PtInRect，返回**绝对格号（1 起，0 = 没命中）**），
  把 `{+0x18 = 当前页签, +0x1c = 格号, +0x24 = 窗口}` 装进 0x28 字节载荷，再 `0x989588` 派发字符串 1847
  `DragStart`。**按下即拿起**，不发包。（`0x81d447` 的 `this` 是挂在窗口 +4 上的子控件，`lea ecx,[ebx-4]`
  才是窗口基址，所以子控件 `+0x5e0` == 窗口 `+0x5e4`。）
- **放下到背包格 `0x4F3057`**（由拖放分派处 `0x4EFBB8` 调用，参数 `(窗口, x, y)`）：
  1. `[0xbe7910] != 0` → 直接返回（已有拖拽/模态）；
  2. `0x4F5697(1)` 必须为真（可操作状态）；
  3. `0x81DB7E` 取目标格号，0 → 返回；
  4. **`[窗口+0x5e4] != 载荷+0x18`（页签不同）→ 返回**（跨背包页不落格，连包都不发）；
  5. `src == dst` → 返回（**点回原格不发请求**）；
  6. 源格道具存在（非 2/3/4 类或 207/233 充能时还要求 `quantity > 0`，`0x4F3144`）；
  7. `0xA0900A(type, src, dst, quantity = -1)`。
- **放下到地面 `0x4F31A5`**：`0xA0900A(type, src, action = 0, quantity)`，可堆叠道具先弹字符串 885 数量窗（见上一节）。
- 结论：**源端两种目标，同一个包**——`action == 0` 丢地面、`action > 0` 落到格号 `action`、`action < 0` 穿戴；
  客户端只负责填 `src / dst`，合法性全在服务端。

### 服务端

`ItemMoveHandler:54`（`action > 0`）→ `InventoryManipulator.move:489` → `Inventory.move:264`：

| 目标格 | 结果 |
| --- | --- |
| 空 | 源格 `setPosition(dst)` |
| 同 id、非 207 充能、owner 相同 | 数量相加；超过 `slotMax` 时 dst 填满、源格留余量 |
| 其余（不同 id / EQUIP / CASH / 充能 / owner 不同） | **两个槽位互换**（`swap`） |

`InventoryManipulator.move` 只回 `ModifyInventory`（1/3），不整包下发；本工程沿用「回包带整份背包」的既有口径。

### 落地（本项目）

- 客户端 `InvenWindow`：格子内容改为**按服务端槽位**取（`inventoryType == tab+1 && position > 0` → 字典），
  空格留空、同 id 的两个堆叠各占一格、数量画**本格** `quantity`（原实现用 `Profile.itemIds` 扁平投影，
  同 id 会并成一格总数，导致没有落点可言）。`SlotAt(pointer, out position)` 按当前窗位实时算矩形，
  空槽也返回 1 起槽位号；未进游戏（读不到 `Character.inventory`）才退回旧扁平投影。
- 客户端 `NativeInventoryActions`：`Pick` 记 `carriedType = id/1000000`；`CarryPointerStep` 的按下分支在
  卷轴/现金装备落点之后、绑键与地面之前处理背包格 → `MoveToSlot(src, dst, type)` 发
  `{ moveFrom, moveTo, moveInventoryType }`；跨页保持携带、点回原格取消携带、服务端拒绝时保留携带并 toast。
  拖拽松手（`Pointer.Released && Pointer.DragEnded`，项目补的口径）同样落格，要求真的拖动过，
  否则「点一下拿起」的抬手会被当成落格。
- 服务端 `NativeInventoryService`：`moveFrom/moveTo` 分派到 `NativeInventorySort.MoveItem`，
  复用 `NativeInventorySort.Move`（整理/合并已照抄的同一套 `Inventory.move` 分支）+ 抽出的 `Persist`
  落库（写 position、删被并掉的行）。
- 回归：`tools/server-check/InventoryMoveChecks.cs`（20 项：空格移动、互换、同 id 合并、超 slotMax 留余量、
  装备同 id 换位不合并、207 充能换位、src==dst/空格/越界/页号非法拒绝）→ 全量 **1387 通过 / 0 失败**；
  `tools/unity-compile-check` 0 warning / 0 error。
- **边界**：未做 Unity 编辑器交互回归与 Android 实机点选验收；跨背包页移动按源端拒绝（保持携带），
  不同 id 同页只互换、不合并；`Profile` 扁平投影仍在其它读取方（宠物/交易校验）存在，本次只改背包窗显示口径。


## 2026-11-05 新增道具拿起后不能落格：迁移载荷修正

沿用上文已核实的同一 BeiDou.exe 身份与 `0x81D42E` 来源页载荷、`0x4F3057` 同页落格门控、`0x4F30E6` 同格不发包结论；本次没有新增 EXE/282 行为推断，也不改 UI 布局或 IMG。

项目缺陷：`DoubleClick → TryUse` 的 `204xxxx`（含轮回星火）/心脏卷轴、现金装备分支调用 `Pick(itemId)`，把已知来源格丢成 position=0、carriedType=0、carriedFromInventory=false。随后两种落格路径均要求 carriedFromInventory，故无法点回原格、移动或互换；这不是服务端禁止移动星火。修正为 `PickOwnedItem`：从真实背包实例解析来源（传入格号时严格匹配；快捷栏未传则取实际一堆），再调用 `PickFromInventory` 保留来源格及页签。普通键位载荷的 Pick 语义不变。

检查边界：新增星火、灵魂卷轴/灵魂石、ARC/AUT/Grand 徽章、方块、核心宝石与普通/心脏卷轴的背包拿起/放回；代表道具经过实际 CarryPointerStep → InProcessTransport → NativeInventorySort.MoveItem 检查空格移动和拖动互换。入口为 `Assets/Editor/InventoryCarryChecks.cs`，使用隔离 Unity 与临时数据库，不写玩家存档。此检查只覆盖背包拖放，不代表所有新增道具的使用功能均已验收；未做 Android 真机验收。

验证结果：`dotnet build tools/unity-compile-check/Check.csproj --no-restore` 0 error/0 warning；隔离 Unity `InventoryCarryChecks.Run` 退出0，`PASS audited=1750 move/swap=16`（1750种相关目录道具拿起/放回，8种代表道具各验证空格移动与拖动互换）。日志 `/tmp/mapledash-carry-regression-20261105.log`；统计含原有卷轴，不能解释为新增了1750种道具。
