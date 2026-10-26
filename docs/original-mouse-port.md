# 原版鼠标素材与输入适配

## 已核实来源

原客户端只读：`/Users/lizixian/Documents/mxd/BeiDou-Server/clien/BeiDou.exe`。
PE 导入表包含 `dinput8.dll!DirectInput8Create`、`user32.dll!ShowCursor`，说明输入使用 DirectInput，并具有系统光标显隐入口。入口 VA 为 `0xA63FF3`。这些证据不足以确定鼠标状态机或双击时限；本次没有还原完整 EXE 鼠标函数。

`clien/Data/UI/Basic.img/Cursor` 包含 0–13 共 14 组素材。0 为截图中的普通指向手；11/12 为手势变化；1/4/13 带鼠标图示且每帧 500ms。其他组包含物品、房屋、方向箭头等上下文动画。资源视觉形态不能单独证明 EXE 的状态编号语义。

`tools/export_cursor.py` 导出全部有效帧，保留逐帧 origin 和 delay。无 delay 节点以 100ms 填充（静态单帧不影响外观）。运行时使用原点对齐指针热点，不按图片边框定位。

## 当前交互

- 停用 `Input.simulateMouseWithTouches`，改由统一指针识别屏幕触控板手势。键盘移动、跳跃和攻击继续保留。
- 支持实体鼠标和屏幕虚拟鼠标：移动端光标初始化在屏幕中央，单指滑动相对移动光标（不跳到触点），抬手保留位置。轻点产生左键，连续轻点产生双击；双指轻点产生右键。单指静止按住 350ms 后按下左键，随后滑动拖放，抬起松开。滑动本身不点击。
- 系统触控板开启“双指辅助点按”后，双指点击也可由系统转换为右键。
- 单击：原有按钮。双击：大厅已拥有武器装备；统一入口提供 `DoubleClicked`。
- 右键：快捷栏清除技能。
- 按住并移动：只剩窗口标题拖动。实体鼠标无需等待；屏幕虚拟鼠标需先长按 350ms 再拖。
- 搬东西（键盘设定窗的功能按钮、技能窗的技能）**都不走拖动**：悬停变张开手（Cursor/11）、按下即拿起（Cursor/12）、
  在目标上再按下放下。源端依据见 skill 的 [UI 拖动语义](../MapleDash/.agents/skills/beidou-client-reverse/references/ui-drag-and-drop.md)：
  技能窗 `0x8ABA6B` 与组合栏 `0x8A98FE` 在 `WM_LBUTTONDOWN(0x201)` 里就发 `DragStart`（字符串池 1847），
  两个函数都没有 `WM_LBUTTONUP(0x202)` 分支；双击走 `0x203`。
- 快捷栏仅在没有拖动的左键释放时激活，拖入技能不再额外施放一次。
- 输入在 Update 开头采样一次，避免原先 LateUpdate 才更新指针导致 UI 落后一帧。失焦不产生点击释放。

## 明确的适配参数

双击间隔 500ms、两次落点距离 5 个虚拟像素、拖动阈值 4 个虚拟像素；时间使用 unscaledTime，不受暂停影响。普通态使用 0，按下使用 12，释放后播放 1 的两帧原生动画共 1000ms。该映射是本项目选用，尚未证明与 BeiDou.exe 完全相同。暂未接入 NPC/物品/缩放等上下文指针状态。

## 验证与限制

导出后逐帧对比原 WZ 解码像素、尺寸、原点和帧时长，检查所有资源均可由 manifest 定位；diff 空白检查通过。当前机器未在 README 指定的 `/Applications/Unity/Hub/Editor` 找到 Unity，未执行 Unity 编译或 Play Mode；需要在编辑器实际验证鼠标显示、双击装备、快捷栏右键清除和跨窗口拖放。

屏幕虚拟鼠标修复验证：使用 .NET 8 执行从项目提取的实际 Pointer 类（Unity 设备 API 由模拟输入替代），通过中央初始化、相对滑动、不误点击、轻点、双击、双指右键、长按拖放、失焦与取消检查。尚未做手机视觉与手感验证。

## 大厅光标被遮挡修复

根因：NativeCursor 只在 Repaint 临时改 GUI.depth 又恢复，未保留该 OnGUI 组件的前景层级，大厅背景随后覆盖光标。现在每种 IMGUI 事件都设置并保留 -10000 层级，初始状态设为 0。

通过 `CursorLobbyChecks.Run` 在 Unity 2022.3 的真实启动大厅做屏幕像素回归：修复前光标不透明像素匹配 0/396（失败），修复后 396/396（通过）。截图 `/tmp/mapledash-lobby-cursor-fixed.png`。检查走 GameLoop.Boot，不手工创建光标组件。该检查固定指针位置以验证绘制，触屏事件仍需设备验收。
