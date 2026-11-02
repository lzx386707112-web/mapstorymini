# 选角、任务进度、技能中心与后台资料反馈（2026-11-01）

本次没有新增 EXE 地址结论。任务告知与技能拖放的二进制身份、调用链和 IMG 几何继续以 quest-window-and-alarm.md、ui-drag-and-drop.md 为准；本次不改原图或布局。

## 已核实的服务端依据

源根 BeiDou-Server/gms-server；SHA256：

- Character.java：b72a138f09a8dadbb88faad54c2f14c1668d74608f1a59b1c394af55b0bce62a。
  1442 addSkillToKeyboard：keymap.put(scanCode, KeyBinding(1, skillId))，发送键位；1471 removeBySkillId：仅删除 type1/action匹配的键位，**不删除技能等级**。
- AbstractPlayerInteraction.java：223abe10c79e6e20002ac4b8782890ad5d928d74219a38ebb1fc95dfb7fcdbfc。
  980..997 teachSkill：非force保留更高已有等级/master；force允许0级；changeSkillLevel保存master与expiration。
- CommonService.java：c83d104ead6fce1b7b2a7c3f7d76494be6da7508726a9f7e3e4d5ddd0b8b283d；CommonInformation.java：cf4b50dc4ad308171ffe869650d7773a06ca4aff13ee5ff1056187361e5dc948。
  getInformation→getStringInformation→searchXML；types空=全部10类；filter为空报错；filterType0=ID或名称、1=ID、2=名称，fullMatch切换相等/包含；逐类按原节点顺序返回type/id/name/desc。
  节点：String/{Cash,Consume,Ins,Mob,Npc,Pet,Skill}.img直接子节点；Eqp.img/Eqp/<类别>；Etc.img/Etc；Map.img/<类别>，地图取mapName/streetName，其余name/desc。资料查询不是全量装备属性扫描。

技能中心唯一规则仍是原始 BeiDouSpecial/{技能中心,快捷技能,技能全满,三宠技能,技能偷学}.js：没有改写脚本。
快捷技能绑定scan21（Y），删除分支teachSkill(...,0,...,true)；技能偷学先教授→移除旧键位→绑定Y，旧迁移误将第二步解释为遗忘技能；三宠由脚本检查/扣2000万；技能全满的等级和黄金枫叶要求仍由原JS执行。风影漫步修复使用SkillFactory对象传给getSkillLevel/changeSkillLevel，因此宿主必须保留id属性。

## 本次迁移故障与修改

- GameSession.ApplyToProfile原先ClearAll及每个SetLearned各同步Profile.Save→PlayerPrefs.Save；改为DeferSaves整批提交一次，减少选角时按技能数增长的同步写盘。未变更角色选择/地图流程；没有Android耗时数据，不能声称所有进图卡顿消失。
- QuestLogic.OnMobKilled已有计数和QuestAlarm.Progress，但Track默认不展开；现在更新任务告知时显式展开并显示原有目标计数行。继续使用Basic/QuestAlarm和UIWindow/QuestAlarm的原IMG、18px行高，不新增面板。
- NpcApi补齐上述键位操作、0级强制重置、master更新和SkillFactory.id；GameSession存档同步保留expiration。
- VirtualControls手持技能时释放注入且Capturing=false，Pointer不再被虚拟层吞掉；下一次点击仍通过SkillWindow.DropOnSlot绑定原虚拟按键，不施放旧技能。不是禁用物理键盘或禁止放置技能。
- AdminCatalog四个分页路由必须调用CatalogPage.ToDict；原AdminJson不反射任意对象，直接返回CatalogPage会输出字符串，导致物品/装备/怪物/技能列表取不到records。
- 注册/common/v1/informationSearch，按上文原端String IMG语义实现；图片统一本地/assets/*-icons，仅解码原IMG，不再取外部版本物品图。NPC取stand/0并跟info/link；地图取miniMap/canvas；缺原图返回404。
- 任务浏览已有townTree/list/detail与原WZ实现，本次验证实际返回数据，未把它重写成另一套接口。

## 复现及验证入口

- Assets/Editor/LoginGameplayFeedbackChecks.cs：隔离临时数据库，正式NpcSession/NpcApi执行技能中心全部5个菜单入口；校验技能、键位、重置、费用、偷学成功/不足点数与风影漫步；蜗牛真实任务击杀推进；虚拟控件技能携带门控；经AdminApi.Handle真实JSON链测试四类图鉴分页、中文/全类型资料查询、任务树/列表/详情、原IMG PNG。
- Unity：-batchmode -nographics -quit -projectPath <MapleDash> -executeMethod LoginGameplayFeedbackChecks.Run。
- 编译：tools/unity-compile-check dotnet build -t:Rebuild。
- 脚本包2779、parseFail0/mangled0；889条NPC广泛harness为790ok/27runFail/72javaInterop，与既有记录相同，不是全量脚本已通过。
- tools/build_admin_ui.sh --skip-install已生成92文件的Resources/AdminUi/adminui.bytes；构建既存eval、重复CSS输出提示不影响产物生成。

实际定向验证结果见 docs/five-feedback-2026-11-01.md。没有Android连接设备，未构建APK、未做触屏/画面/首次进图耗时实机验收。
