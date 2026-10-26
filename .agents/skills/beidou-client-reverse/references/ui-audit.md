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
