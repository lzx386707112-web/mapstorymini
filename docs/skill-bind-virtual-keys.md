# 技能 ↔ 虚拟按键：绑定与施放（源端行为迁移）

2026-10-22。针对三个问题：显示键盘（键位窗口/输入法）时虚拟按键消失、技能窗技能点不起来也拖不到虚拟按键、
技能双击或按虚拟按键不出手。

## 一、源端逻辑（BeiDou.exe + WZ 证据）

原端是**纯客户端**行为，服务端不参与绑定：

| 环节 | 源端实现 | 证据 |
| --- | --- | --- |
| 键位表 | 客户端自带一张 80 键的「键 → 功能」映射，窗口 `UI/UIWindow.img/KeyConfig` | `backgrnd` 原始 628×372（origin 314,186），`icon/0..54` 是功能图标（装备/背包/能力值/技能/拾取/攻击/跳跃…），`key/2..` 是键帽贴图 |
| 拖放绑定 | 界面里把功能/技能图标拖到键帽上完成绑定，双击键帽把功能退回待选区 | KeyConfig 只有 `icon` + `key` 两组素材，没有列表控件 —— 说明绑定就是"图标落在键帽上" |
| 快捷栏 | 技能窗（`UIWindow.img/Skill`）把技能图标拖到快捷格（`Basic.img/QuickSlot`）绑定，格子由键盘键触发；技能窗右侧 `SkillMacro` 可拖进宏槽 | 现有 `SkillWindow` 已完成这一层（`DropOnSlot` / `DropOnMacro`） |
| 施放 | 按键 → 快捷格 → 技能；服务端只做合法性校验，不存绑定 | `gms-server` 1474 个 Java 文件里没有 KeyConfig/QuickSlot 的持久化模型 |

结论：绑定是**纯客户端、纯本地**的数据，迁移只需在本地存档里加一条「技能 → 屏上按键」，
不需要服务端协议。

## 二、三个问题各自的原因

1. **键盘时虚拟按键消失**
   - `VirtualControls.Draw()` 开头 `if (KeyConfigWindow.Visible) return;`，键位窗口一开，整层直接不画。
   - 安卓输入法弹出时，屏幕下半被 IME 盖住；按键照常画，但用户看不见（收起后才恢复）。

2. **技能点不起来、拖不到按键**
   - 拾取只认 32px 的图标区域（手机上难点），且虚拟按键层根本没有"技能"这个概念，
     `DropOnSlot` 只认宏槽 / 键帽 / 底部快捷格。

3. **双击不出手**
   - 双击判定容差 `sqrMagnitude <= 25`（5 个虚拟像素 ≈ 7 物理像素），手指几乎不可能命中，
     于是每次双击都被拆成两次单击（= 两次"拾起"）。

## 三、迁移后的行为

- `VCElem.skillId`：虚拟按键可以直接绑技能，存盘字段 `"sk"`（旧存档缺字段按 0 处理，向后兼容）。
- 技能窗**整行**都能拾起；拖到任意虚拟按键上即绑定，拖动时所有可绑定按键亮蓝环、悬停那个变黄环，
  拖拽图标/光圈画在浮窗之上（`VirtualControls.DropTargets`）。
- 绑了技能的按键：按下直接 `SkillCast.TryCast`，显示技能图标 + 16 帧冷却遮罩；
  **不再同时注入动作/键**，避免和快捷格通道重复施放（一次点击放两次）。
- 技能窗**双击**任意行直接施放；双击容差放宽到 16 个虚拟像素。
- 键位窗口打开时虚拟按键降到 42% 透明度继续绘制（只显示、不注入）；
  安卓输入法弹出时整层按键盘高度上移（`TouchScreenKeyboard.area`，adjustResize 的机型不重复上移）。
- 编辑器「编辑」页新增「绑定技能」一行：查看当前技能，点一下解除。

## 四、涉及文件

- `Assets/Scripts/VirtualControls.cs`：`skillId` 字段与序列化、`Center()`/`LiftY`、`TryBindSkill`、`DrawDropTargets`、技能键施放与冷却遮罩
- `Assets/Scripts/SkillWindow.cs`：整行拾起、双击施放、拖到虚拟按键、`Dragging`/`DragSkillId`
- `Assets/Scripts/SkillBook.cs`：`SkillCast`（技能窗/虚拟键/快捷格统一施放入口）
- `Assets/Scripts/GameHUD.cs`：在浮窗之后画拖放目标
- `Assets/Scripts/InputHub.cs`：双击容差 5 → 16
- `Assets/Scripts/VirtualControlsEditor.Panel.cs`：编辑页「绑定技能」

## 五、待真机验证

- 安卓 IME 弹出时的实际上移量（`TouchScreenKeyboard.area` 在部分 ROM 返回 0，已按 42% 屏高兜底）
- 触屏上"拾起 → 拖动"的手感（`Pointer` 是相对轨迹板语义）
- IL2CPP 构建后的 `TouchScreenKeyboard.area` 行为
