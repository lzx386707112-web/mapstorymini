# 原端技能窗还原记录

2026-10-22。参考用户提供的新手截图，资源读取自 MapleDashResources/Data，静态分析 BeiDou.exe。

## 素材与坐标

| 区域 | 原 IMG 节点 | 尺寸/坐标（原像素） |
| --- | --- | --- |
| 左窗 | UI/UIWindow.img/Skill/backgrnd | 175×289 |
| 页签 | UI/Basic.img/Tab2/{left,fill,right}1 | (3,23)，34×19 |
| 书本 | Skill/000.img/info/icon | (10,55)，26×30 |
| 条目 | UI/UIWindow.img/Skill/skill1 | (7,99+40n)，141×35 |
| 技能图标 | Skill/000.img/skill/<id>/icon | (8,100+40n)，32×32 |
| 加点 | UI/UIWindow.img/Skill/BtSpUp | (131,119+40n)，12×12 |
| 左滚动条 | UI/Basic.img/VScr4 | (153,99)，15×155 |
| 右窗 | UI/UIWindow.img/SkillMacro/backgrnd | (173,0)，207×289 |
| 组合槽 | SkillMacro/macroslot | (17,53+45n)，124×38 |
| 宏图标 | SkillMacro/Macroicon/<index>/icon | (146,56+45n)，32×32 |
| 命名区 | SkillMacro/macroname | (17,195)，164×57 |
| 确认 | UI/Basic.img/BtOK2 | (154,265)，47×19 |

左侧控件坐标可见 exe 0x8AAC69、0x8AAD3C，右侧见0x8A8504与0x8A8926。
原版页签实际有IMG三切片，旧文档“GDI生成、WZ无素材”的判断已纠正。

## 验证

Unity Editor 播放模式运行8秒，直接进入40000地图并打开新手技能窗口；截图来自真实OnGUI，
不是Python拼接假运行图。脚本编译及运行正常，无技能窗资源缺失或C#异常；diff --check通过。
最终截图：skill-window-restored.png；并排核对：skill-window-restoration-comparison.jpg。

## 尚存差异与范围

- 背景、图标、页签、按钮、命名区等位图直接用原素材；动态文字仍使用项目现有Arial Unicode MS与Unity字体栅格化。
  原截图中文字来自Windows客户端，资源目录未发现原字体，不能声称动态文字逐像素一致。
- 本地已有Lv.6新手存档，运行图技能点为5、前三个加点按钮可用；参考图为0点禁用状态。
  现有-smokePlayerLevel参数仅能升级，不能降到1；没有为截图修改真实玩家等级/技能点逻辑。
- 宏面板此次实现显示、编辑、拖入/移除与保存配置，未接入组合施放和宏快捷键执行。
- 未构建APK、未做安卓设备截图验收。整体缩放仍沿用BottomBar.S。
