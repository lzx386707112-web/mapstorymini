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

## 2026-10-27：新手技能图标为空的资源别名修复

用户指出`native-viewport-2400.png`技能列表有名称、无图标。前次视口回归只验证窗口坐标，未验证技能图标能解析，不能据此前次PASS宣称面板内容完整。

原始`clien/Data/Skill/000.img`与项目`MapleDashResources/Data/Skill/000.img`一致，SHA256均为
`0c3f6dafa83540139a3a27deb5afb6350ac78432eb86f0aebb8e8e6c7f21604f`。
该IMG技能节点保留7位前导零：`0001001`团队治疗、`0001002`疾风步、`0001000`蜗牛投掷术、`0000012`精灵的祝福；
四个`icon`均为32×32、origin(0,32)。沿用已核实的行内32×32绘制区域(8,100+40n)，此次不改布局。

根因调用链：`SkillWindow.SkillIcon` → `Art.Tex(SkillData.icon)` → `WzAssets.Texture/AliasNode` →
`NativeAssetIndex.aliases[skillicon/1001].source`曾为`Skill/000.img/skill/1001/icon`（节点不存在）→返回null→DrawRows跳过图标。
应指向`Skill/000.img/skill/0001001/icon`。731条SkillBook记录里727条声明有图标，逐一核对原IMG后发现26条别名路径错误，全部属于000.img前导零丢失，其余701条节点存在。

修复26条现有别名；`export_jobs.py`从实际`node.name`构建路径，并纠正已有错误source（旧版仅增不改导致错误持久保留）；
`package_native_assets.py`引导旧skillicon资源别名时也保留真实节点名，防止重新打包写回错误。
没有改IMG像素、技能ID、名称、SP或战斗逻辑。另4条记录源数据未声明可用图标，未补画占位图。

复现/验证入口：`rtk proxy python3 tools/imgdump.py Skill/000.img skill --depth 1`，
`rtk proxy python3 tools/export_jobs.py --check`，以及`NativeViewportChecks.Run`中的`SkillIcons`：
在Unity中遍历声明有图标的技能，用实际`Art.Tex`解码并检查尺寸和非零alpha，随后重跑面板视口回归并重拍截图。

本次实际结果：`export_jobs.py --check`为63职业、731技能、727条有图标记录，修正后的别名再生成预览为“新增0、纠正0”；
Unity日志`/tmp/mapledash-skill-icons-checks.log`确认`decoded 727 nonempty skill icons`及**1536项PASS、exit=0**。
已重拍800×600、2400×1080两张视口图，并打开检查`docs/hud-review/skill-icons-fixed.png`
（从2400×1080真实OnGUI截图裁出，非重绘）：新手四行图标均已显示。
该结果仍不等同于Android设备验收。
