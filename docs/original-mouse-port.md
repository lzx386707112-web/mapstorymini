# 原版鼠标素材与输入适配

## 已核实来源

原客户端只读：`/Users/lizixian/Documents/mxd/BeiDou-Server/clien/BeiDou.exe`。
PE 导入表包含 `dinput8.dll!DirectInput8Create`、`user32.dll!ShowCursor`，说明输入使用 DirectInput，并具有系统光标显隐入口。入口 VA 为 `0xA63FF3`。这些证据不足以确定鼠标状态机或双击时限；本次没有还原完整 EXE 鼠标函数。

`clien/Data/UI/Basic.img/Cursor` 包含 0–13 共 14 组素材。0 为截图中的普通指向手；11/12 为手势变化；1/4/13 带鼠标图示且每帧 500ms。其他组包含物品、房屋、方向箭头等上下文动画。资源视觉形态不能单独证明 EXE 的状态编号语义。

`tools/export_cursor.py` 导出全部有效帧，保留逐帧 origin 和 delay。无 delay 节点以 100ms 填充（静态单帧不影响外观）。运行时使用原点对齐指针热点，不按图片边框定位。

## 当前交互

- 停用 `Input.simulateMouseWithTouches`：触屏不再模拟左键。键盘移动、跳跃和攻击继续保留。
- 使用实体鼠标；系统触控板开启“双指辅助点按”后，双指点击由系统转换为右键。不支持把手机屏幕的双指触摸转换为右键。
- 单击：原有按钮。双击：大厅已拥有武器装备；统一入口提供 `DoubleClicked`。
- 右键：快捷栏清除技能。
- 按住并移动：窗口标题、技能和键位的现有拖放。无需先等长按计时；可长按后再拖。
- 快捷栏仅在没有拖动的左键释放时激活，拖入技能不再额外施放一次。
- 输入在 Update 开头采样一次，避免原先 LateUpdate 才更新指针导致 UI 落后一帧。失焦不产生点击释放。

## 明确的适配参数

双击间隔 500ms、两次落点距离 5 个虚拟像素、拖动阈值 4 个虚拟像素；时间使用 unscaledTime，不受暂停影响。普通态使用 0，按下使用 12，释放后播放 1 的两帧原生动画共 1000ms。该映射是本项目选用，尚未证明与 BeiDou.exe 完全相同。暂未接入 NPC/物品/缩放等上下文指针状态。

## 验证与限制

导出后逐帧对比原 WZ 解码像素、尺寸、原点和帧时长，检查所有资源均可由 manifest 定位；diff 空白检查通过。当前机器未在 README 指定的 `/Applications/Unity/Hub/Editor` 找到 Unity，未执行 Unity 编译或 Play Mode；需要在编辑器实际验证鼠标显示、双击装备、快捷栏右键清除和跨窗口拖放。
