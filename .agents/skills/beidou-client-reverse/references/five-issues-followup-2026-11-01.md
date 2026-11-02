# 金币、V/VI列表、窗口共存、拖拽与选角卡顿补核

## 身份与原端证据

- `BeiDou.exe` SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`。
- `clien/BeiDouSkillCompatCore.dll` SHA256 `3882737456d7c95795b2afe63ad91703cd70ef299c6b83fefe5f6f70764b466f`，ImageBase `0x67280000`。
- 当前 DLL `0x6728149A..0x672818F9` 的技能ID分派，证据永久保存在 `docs/reverse-evidence/advanced-skill-damage-dispatch-20261101.txt`。复现：`python3 tools/inspect_beidou_client.py <DLL> --fn 0x6728149a`。
- 英雄1121012..1121030、圣骑1221015..1221032、黑骑1321011..1321026经`0x67281871`跳物理近战`0x9690AE`；火毒2121009..2121036、冰雷2221009..2221031经`0x67281884`跳魔法`0x96928B`。主教扩展魔法分支经`0x67281897`；2321020另走召唤`0x9689DF`，不能据魔法家族推断召唤状态机已完整。
- 弓/弩/标飞/船长扩展走远程`0x9690E9`；刀飞/拳手走近战`0x9690AE`。骑士团1512/1112走近战，1312/1412走远程，1212走魔法。具体排除ID以汇编为准，旧C++仅作线索。
- **纠正**：不能用等级节点存在`mad`判断扩展技能是否魔法。源IMG有174个物理职业技能也包含该字段。`SkillBook.MagicDamageOf`按已核实职业攻击家族纠正，未改原始等级数值。

## 原IMG与列表

`tools/audit_advanced_skills.py`直接读取源Skill IMG、与复制IMG核对SHA，并逐字段核对JobTree等级字典、面板列表、MCV存在性、回放组件引用及视觉节点清单。结果：17个四转职业文件、382技能、11000等级、0差异。**382包含普通四转和扩展组件，不是382个五六转主动技能。** 各IMG身份和逐技能结果见 `docs/advanced-skill-audit-2026-11-01.json`。

圣骑两个当前DLL视频技能：1221020圣域展开、1221030圣狮之主，均在原IMG及面板数据中。当前39个视频映射在`NativeSkillVideos.json`；不能用旧DLL较少的视频表判定缺失。

技能窗仍复用原 `UI/UIWindow.img/Skill` 与 `UI/Basic.img/VScr4`。现有技能行x=7、宽154，与滚动条x=153..168重叠；此次让滚动条命中优先并消耗Pressed，`RowAt`/`HandleRows`排除滚动条。原布局/素材不变，四行视口后面的技能可继续滚动。

## 本地缺陷与修正

- `BotActorLooter.GainMeso`在正常拾金币时把玩家完整余额强转int；给100亿后再拾取导致截断。去掉强转、long checked相加，服务器回归验证100亿+123及数据库重载。
- `SkillWindow.Draw`与`VirtualControls.DrawDropTargets`同时绘制同一拖拽图标且缩放不同，符合用户截图。只在InputBridge顶层调用一次`SkillWindow.DrawDragGhost`，原按下拿起/再次按下放下语义保持。
- 键位窗原先作为全屏输入遮挡；改为通过NativeUi注册自身矩形。移除普通面板入口/侧栏/虚拟界面键的全局禁用。键位编辑仍不触发攻击、移动等游戏操作；这与面板能否共存分开。
- `SkillDef`构造曾调用`NativeSkillVfx.Key`，仅构造旧四格也预加载视觉目录；改为首次访问vfx时解析。回归构造1000次不增加VFX缓存。此测试不是Android选角整段耗时。
- 普通有界回放选敌改为复用已核实的怪物身体框重叠，避免只检测脚底导致边缘漏怪；朝向镜像/lt-rb单位沿用`SkillSystem.InRange(Component,...)`，原数据不变。

## 验证与边界

服务端726/0（含100亿拾取保存）；`LoginGameplayFeedbackChecks`通过（含实际后台100亿发放）；`CurrentFeedbackChecks`验证11000组运行时参数、攻击家族、键位窗共存及延迟VFX。

Play回归结果与日志见 `docs/five-issues-followup-2026-11-01.md`。独立参数/节点清单一致不能证明全部职业的特殊召唤、概率、位移、组件时序均已完整迁移。现有`custom-dll-systems`列出的专属状态机、120ms行间隔和特殊弹道起点等缺口仍须逐项实际验证。未连接Android设备，不把Unity PASS当成APK实机验收。
