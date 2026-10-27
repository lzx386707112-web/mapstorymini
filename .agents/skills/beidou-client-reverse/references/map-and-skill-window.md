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
