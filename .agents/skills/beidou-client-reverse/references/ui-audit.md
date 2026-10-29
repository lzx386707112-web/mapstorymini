# UI 迁移状态与未完成项

核实快照：2026-10-26。原端身份见 [binary-and-tools.md](binary-and-tools.md)。
原端逻辑的确定程度与运行时迁移状态分开记录；“尚未迁移”不代表需要重新逆向已确认部分。

## 家族与其他窗口审计（尚未完成替换）

当前 action 名称注意：`GameAction.Family` 是家族（Guild），`School` 对应原端 Family（学院）。
主家族视图位于 `UI/UIWindow.img/UserList/backgrnd`（312×389），初始化 `0x919CCF`；不是 `0x915F56` 的233×304窗口，后者属于独立管理窗口。
主窗口 tab=2，GuildInfo 初始化 `0x90E8E8`、绘制 `0x90F17C`。标题画布复制到(5,55)，列表视口(6,113)，宽281、高最大192，滚动步长33。
不能使用旧字符串 `UI/UIWindow.img/Guild/backgrnd`；它不对应当前素材。
学院背景是 `UI/UIWindow.img/Family/backgrnd`，224×392。

发现仍含自绘或通用布局的游戏 UI：

- `NativeCashWindow`：旧GUILayout已替换为原端800×600场景和CSNotice/CSGift；交易与背包等剩余交互见[cash-shop.md](cash-shop.md)。
- `NativeCommunityWindow`、`NativeMedalWindow`：仍是通用窗口/按钮。
- `NativeMapWindows.DrawWorld` 已改原端边框/关闭按钮；地图层之外的文字和交互仍需视觉对照。
- `NativeSocialWindow`、`NativeMonsterBook`：引用背景但内部仍用通用列表/按钮。
- `Quest/QuestWindow`、`Client/DeathFlow`、部分 `Client/LoginFlow`、`BottomBar`/`GameHUD`：需逐节点审计。
- `tools/export_hud.py::identity_plate` 会生成自定义身份底板，需追踪当前引用并用原端节点替换。

移动端虚拟操作层及编辑器属于移植适配；用户要求它高于游戏窗口。不能将这些适配界面误认为原端已有窗口。

## 四个主面板版式（已核，2026-10-26）

能力值 / 装备 / 背包 / 技能四窗的完整坐标表、IMG 节点与证据地址见
[../../../../docs/ui-panel-restoration.md](../../../../docs/ui-panel-restoration.md)。要点：

- 控件创建 `push` 顺序是 `(this, id, x, y, w, h)`；**带 origin 的图元 (x,y) 是中心点**
  （`Item/BtGather|BtSort|BtFull|BtSmall` origin(6,6)、`Item/BtCashshop` origin(46,8)），
  只有 origin(0,0) 的才是左上角。0x81CF0E 的 (495,266) 是中心 → 左上 (449,258)。
- 窗口基类 0x92C2E8 只建 id 0x3E8 的边角手柄（中心在 `[esi+0x590],[esi+0x594]`）；
  子类构造函数里没有关闭按钮，本项目统一自绘 (W-19,5)。
- `CUIStat::Draw` 0x8C59FF 的成对 `push y; push 0x3D` 就是 8 行基础文本的 y；
  BtDetail (124,324)（0x8C4E15），职业图 main/sub 在 (62,53)/(62,64)（0x8C5BCF）。
  原端此窗没有音量控件。
- 装备窗网格 (5,35) + 步长 33 + 格 32，小窗 5 列 / 展开 6 列，槽位名印在
  `Equip/backgrnd`、`Equip/FullBackgrnd` 上；BtDetail (5,267)、BtPetEquipShow (102,280)。
- 背包窗页签 (3,23,170=5×34)（0x81C978）、滚动条 (152,51,200)（0x81CCCF）、
  格子 (8,50)+36×34（展开 4 组、组距 149、起 7）、BtCoin (7,266)。
- 技能窗行顶 99/139/179/219 由 0x8AAD3C 循环的 `y=0x77` 与 `+0x28` 推出，与现实现一致。
- 未定：`[esi+0x590]` 恒为图宽减 20 的语义；装备窗 BtDetail 的消息分派分支。


## 2026-10-28 任务窗口更新

QuestWindow已改为原端245+305×396双窗、三个Tab2页签、状态正文、summary独立滚动；QuestAlarm及QuestClear动画/声音已接。原先两页签/固定缩放的旧实现描述不再代表当前代码。详细地址、布局、用户授权的两条截图任务及剩余边界见[任务窗口与告知](quest-window-and-alarm.md)。此项仍未作Android实机验收，不能标成完整原端迁移。
