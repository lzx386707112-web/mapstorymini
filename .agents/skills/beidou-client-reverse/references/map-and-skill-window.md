# 地图渲染与技能窗的已有证据入口

以下模块已有独立维护文档，保留单一正文，不再复制全部迁移历史。

## 相机、分辨率与背景

读取 [original-map-rendering.md](../../../../docs/original-map-rendering.md) 的“已核实的依据”和“对照文件哈希”。

已定位：BeiDou.exe `0x641EF1..0x64211D`（VR 缺省边界和视野内缩）、`0x437B32..0x437B39`（相机 y=-50）、`0x63CD4E`（MakeBack）、`0x63DC4A..0x63E1C2`（背景滚动分支）；ijl15.dll `0x10011DF5..0x10011E8F`（按运行时分辨率改写内缩值）。

2026-10-26 补充：tile/obj 的 z 键公式（`MakeObj 0x63C27B`、`RestoreTile 0x63A911`）与**缺素材不中断整图**（`MakeObj 0x63C263`）已在同一 EXE 核实，并据此修复了 69 张因缺素材整图白屏的地图；910000000 逐节点核对结论、离线探针用法、受影响地图清单见 [original-map-rendering.md](../../../../docs/original-map-rendering.md) 的「tile/obj 的 z 键与缺素材容忍」。

注意：该文档也包含 WZ 数据分析、截图验收和本项目适配，不是每段描述都有 EXE 证据。768px 逻辑高度、指数平滑、绳梯容差、静态地图合图不能归为已确定的原 EXE 常量。旧“攻速尚未迁移”描述以本 skill 的 [输入记录](input-and-physics.md) 所列最新基础攻速实现范围为准。

新发现的 EXE/DLL 地图逻辑更新该文档的相应模块并在本页保留入口；运行时坐标与迁移规范维护在 mapledash-map-port。

2026-10-27：相机迁移已补齐 DLL 的整数半屏边界、视口改变后立即 SnapTo 的刷新，以及无目标状态的 VR 约束；窗口视口修复、82项Unity回归与边界见 [DLL 与手机适配](ijl15-mobile-adaptation.md) 的“后续迁移”。768逻辑高度仍是项目适配选择。

## 技能窗与技能组合栏

读取 [original-gameplay-port.md](../../../../docs/original-gameplay-port.md) 的“技能窗”，以及其中链接的 [skill-window-restoration.md](../../../../docs/skill-window-restoration.md)。

已定位：`0x8AAC69` 页签 x3/y23/单页宽34；`0x8A8504`、`0x8A8926` 技能组合栏槽位、命名区和确认按钮布局。素材为 `UI/Basic.img/Tab2`、`UI/UIWindow.img/Skill`、`UI/UIWindow.img/SkillMacro`。

2026-10-27新手技能图标空白：`Skill/000.img/skill`节点名保留7位前导零（如`0001001`），不能把int技能ID直接拼为节点路径。已修正26条图标别名及两处生成脚本；源IMG身份、调用链与验证入口见 [技能窗记录](../../../../docs/skill-window-restoration.md) 的“新手技能图标为空”。

UI 布局确认不能推导出宏组合施放状态机已经还原。技能逐级数据与服务端执行规则仍应分别查 IMG 和服务端/脚本，不把项目默认回退动作认作 EXE 的已确认分派。


## 技能详情悬浮框（Skill Tooltip，2026-10-29）

二进制身份同上：`clien/BeiDou.exe` SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，ImageBase `0x400000`。复现：`tools/inspect_beidou_client.py <exe> --start 0x8f2400 --end 0x8f3400`。**技能详情框不是 IMG 素材，是代码绘制**：`UI/UIWindow.img/ToolTip` 下只有 Equip 相关节点，技能窗初始化（`0x8AAC69` 段）只加载 `Skill/BtSpUp`、`Skill/skill0`、`skill1`、`recommend/0`、`Skill/line`，没有 tooltip 背景图。

- 画框函数 `0x8F25D0`（`push 7` / `push 0x140`，即 `0x8E6E52` 的 **kind=7、宽 0x140=320**）。高度 = `0x72`(114) + `(字体行高16+4)×(描述超出4行的行数)` + 末块；`0x72` 就是分隔线 y。
- 帧 `0x8F3141`：背景 `0xA0000040`、开关 1，四角与内缩 1px 轮廓镂空 —— 与物品 tooltip 的 `0x8EEEF1` **同一个函数同一组参数**，可直接复用同一条实现（`ItemTooltip.DrawFrame`）。旧自绘浅色面板不成立。
- 标题：`0x8F4818` 字 0xA，名称左上 (18,10) 12 号白；前置标记是 `ToolTip/Equip/Dot/0`（5×5，实测中心 `#55CCFF`）@(10,15)。
- 图标：`0x8F421E` 的 68×68 底 `0xA0FFFFFF` @(10,32)，源 `icon` 画布按 2 倍居中（32×32 → (12,34)）。图标走 `NativeAssetIndex.aliases` 的 `skillicon/<id>`。
- 描述：`0x8F4535`，x=`0x57`(87)、右界 = 窗宽-20、行距 16、12 号白；`#c...#` 强调走 `0xFFFF9900`。文本来自 `String/Skill.img/<7位补零id>/desc`，**"[最高等级 : N]" 是 desc 的第一行，不是 EXE 字符串**。
- 分隔线：`0x8F3BC3`，y=114、左右各内缩 4、白色（实测约 85% α）。
- 末块：`ToolTip/Equip/Dot/1`（3×3 橙 `#FF6600`）+ `[Next Level %d]`（字符串池 `0x2A3`），下一行取 `String/Skill.img/<id>/h{n}`，中文行距同为 16、起点 x=10；已满级时该行换成字符串池 `0xF0C` `[Master Level : %d]`。
- 实测校验（源端截图 1284×752，按窗宽 1066px/320 ≈ 3.334 缩放）：框内文字行 y=9.6~21.3（标题）、31.8/48/64.5（描述三行）、113.4（分隔线）、122.4/137.7（末块两行）；描述左界 x=87.0 与代码常量吻合；框总高 ≈156、宽 1066px。

边界与待核：技能框相对鼠标的摆放偏移未在 EXE 中定位（当前沿用 `Pointer + (16,14)` 并做屏幕夹取）；标题标记下方那截深色小竖条未识别（`Dot/2` 是 2×7 米色虚线，颜色不符，暂不绘制）；技能窗列表行右侧的橙 ↑ 与底栏"宏指令"是**窗口自身控件**，截图里发暗只是因为被半透明框压住，不属于 tooltip。

## 技能 ID 施放表现与升级动画接入（2026-10-29）

本节是 IMG 数据核对与迁移修复记录，不新增 EXE 函数/状态机结论。原端已核实动作/特效入口仍以技能专项文档为准。

- 缺陷：`TryCastSkill` 用空 `art` 创建 `SkillDef`，导致绑定技能跳过 `PlaySkill`、`BeginVisuals`，与旧槽位入口行为分叉。现在按 `NativeAssetIndex.skills` 将 ID 还原为已审计 key；已有 20 个技能沿用既有完整表现管线。其他 ID 接回 `PlayNativeSkill` 与已有 `PlayNativeReplayEffect` 的原 `effect` 分支；不宣称完整迁移这些技能的弹道、特殊分支或技能规则。
- 新手 `Skill/000.img/skill/0001000..0001002` 使用七位节点名。`WzCharacter.Pose` 与 `PlayNativeReplayEffect` 已统一 `D7`，缺失技能节点返回 null，不再解引用空节点。当前 IMG 的这三个技能没有 `action`；沿用既有普通攻击回退不等于已确认原端施法动作，需要继续核实原 EXE 后才能对齐。
- 源 `Skill/000.img` SHA256 `0c3f6dafa83540139a3a27deb5afb6350ac78432eb86f0aebb8e8e6c7f21604f`：0001001 的 `effect` 13 帧，0001002 的 `effect` 12 帧；`icon` 32×32、origin=(0,32)。虚拟键此前将图标放大至整个按钮，现以原纹理尺寸居中，仅按钮过小时缩小，冷却遮罩同步图标矩形。虚拟键本身及其用户配置缩放属于移动适配，并非原端布局。
- 源 `Effect/BasicEff.img` SHA256 `91501003f961bdcbe368abf2c3ed104754c676da0c4af58e3b6427cfa70bd57b`：`LevelUp/0..20` 共21帧，帧0尺寸86×70/origin=(43,70)，帧3尺寸201×235/origin=(100,235)，无显式 delay 的帧由既有播放器读取缺省100ms。`PlayerCtrl.OnLevelUp` 回满HP/MP后调用 `NativeImgAnimation.Spawn`，挂人物脚底，保留每帧 origin/delay/alpha 与自动结束。
- 复现数据：`tools/wzmap.py` 的 `_wz('Skill/000').get('skill/0001001')`、`_wz('Effect/BasicEff').get('LevelUp')`，枚举 children 并读取 canvas 尺寸、origin 和 delay。此次已用原 IMG 实际读取核对上述帧数和尺寸；手机实际施放/升级画面尚未验收。


### 同日反馈：动作恢复但技能仍无特效

**后续已定位并修复公共等级节点解析，以下是修复前的诊断记录；最新实现和实测边界见 [技能视觉节点与公共解析](skill-visual-nodes.md)。** 蜗牛投掷术并非只有根ball/hit，其资源在level/N下；不能套用下列2001004根节点示例解释蜗牛的全部缺陷。

上次仅补通用 `effect` 的修复不足，不能描述为全部技能特效已修复。原 IMG 实读示例：2001004（`Skill/200.img/skill/2001004`）无 `effect`，有 `ball` 直属 Canvas 帧及 `hit/0`、`hit/1`；3001004 同样含 `ball` 和 `hit/0`。非20个已配置 key 的技能仍未进入弹道管线。资源事实不能推出这些技能完整原端发射/命中时序，须结合实际问题技能继续核实。

本轮实际运行 Unity `SkillVfxChecks.RunAssets`：PASS 76 clips / 942 loaded frames and exact pivots（退出码0）。仅证明既有清单素材可加载及轴心正确，未验证上述 ID 施放、屏幕可见性或 Android；不得拿该结果声称用户反馈已修复。待用户提供具体技能与运行平台定位。
