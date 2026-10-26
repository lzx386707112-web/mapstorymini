# 地图渲染与技能窗的已有证据入口

以下模块已有独立维护文档，保留单一正文，不再复制全部迁移历史。

## 相机、分辨率与背景

读取 [original-map-rendering.md](../../../../docs/original-map-rendering.md) 的“已核实的依据”和“对照文件哈希”。

已定位：BeiDou.exe `0x641EF1..0x64211D`（VR 缺省边界和视野内缩）、`0x437B32..0x437B39`（相机 y=-50）、`0x63CD4E`（MakeBack）、`0x63DC4A..0x63E1C2`（背景滚动分支）；ijl15.dll `0x10011DF5..0x10011E8F`（按运行时分辨率改写内缩值）。

注意：该文档也包含 WZ 数据分析、截图验收和本项目适配，不是每段描述都有 EXE 证据。768px 逻辑高度、指数平滑、绳梯容差、静态地图合图不能归为已确定的原 EXE 常量。旧“攻速尚未迁移”描述以本 skill 的 [输入记录](input-and-physics.md) 所列最新基础攻速实现范围为准。

新发现的 EXE/DLL 地图逻辑更新该文档的相应模块并在本页保留入口；运行时坐标与迁移规范维护在 mapledash-map-port。

## 技能窗与技能组合栏

读取 [original-gameplay-port.md](../../../../docs/original-gameplay-port.md) 的“技能窗”，以及其中链接的 [skill-window-restoration.md](../../../../docs/skill-window-restoration.md)。

已定位：`0x8AAC69` 页签 x3/y23/单页宽34；`0x8A8504`、`0x8A8926` 技能组合栏槽位、命名区和确认按钮布局。素材为 `UI/Basic.img/Tab2`、`UI/UIWindow.img/Skill`、`UI/UIWindow.img/SkillMacro`。

UI 布局确认不能推导出宏组合施放状态机已经还原。技能逐级数据与服务端执行规则仍应分别查 IMG 和服务端/脚本，不把项目默认回退动作认作 EXE 的已确认分派。
