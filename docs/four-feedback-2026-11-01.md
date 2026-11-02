# 四项反馈核查（2026-11-01）

1. 点券：当前GroundItem和GroundItemBridge已接账号nxCredit；真实临时数据库回归NativePetSceneChecks PASS79，4031865两张+200、4031866两张+500，累计700且不入背包。未改此已接通的奖励代码。首次旧fixture未注入GameSession.Server，在宠物过滤处空引用；本次补齐fixture后完整通过。未验证反馈APK版本及实机安装状态。
2. 背包金币：复核EXE81DC20、988690、字体24/25后修正InvariantCulture每三位逗号、12号黑字、右界138及y266上对齐；原IMG不改。
3. 五转物品2029006：原IMG指定冒险家五六转攻击技能，源端缺此脚本而回退9900001拍卖菜单。扩大搜索scripts/scripts-zh-CN、源端全部隐藏/忽略文件及mxd内同名文件/压缩备份仍未找到。未编造替代JS，未宣称完成；需另一份原脚本来源。
4. 拍卖：确认ijl15.dll初始化10015C58→1000FC30→10016550，把EXE9A3D81的240改480。DialogFlow迁移漏掉扩展，现补480上限，保留原整数滚动门槛、529宽、IMG和内部布局。对普通NPC对话共同生效，与原DLL一致。

## 验证

- unity-compile-check Rebuild：0错误0警告。
- NativePetSceneChecks：PASS79，退出0；日志four-feedback-pet-20261101.log。
- 脚本包2779，parseFail0/mangled0。
- 全量NPC harness：790 ok/27 runFail/72 JavaInterop，既有基线，无脚本改动。
- QuestNpcChecks：24472通过/4失败；本次480高度与495/496滚动门槛断言通过。失败项为quest/2300传送/forceStartQuest及1021/QuestClear动画，不能声称全量回归通过；日志four-feedback-quest-20261101.log。
- DialogLayoutChecks：7项通过，3种视口绘制/点击与矩阵恢复通过；已目检1280截图。此fixture为短对白，不代表拍卖480高度实机画面验收。
- 未构建APK、未Android实机验收。


## 用户追加授权：五转完成后直接显示五六转技能

已增加本地脚本覆盖 `content/script-overrides/npc/9900008.js`：完成女神原条件后写入原永久标记，不再发放/补领2029006，也不再要求消耗栏有空位。既有完成角色从原永久键读取解锁，不用重新交材料或使用道具。按用户后续澄清，技能显示在独立“五转”页签，四转页仅保留四转技能；沿用原 IMG、滚动和加点；本次不自动赠送技能等级。骑士团转职、英雄币合成保持原逻辑。

本次验证：
- `SkillPanelChecks.Run` Unity batchmode 退出0，`FIFTH_ADVANCEMENT_PASS missing cancel costs no_item repeat persisted skill_rows`，实际 NpcSession 完成女神与实际技能页数据路径通过，另有原技能页回归 `SKILL_PANEL_PASS`。日志：`docs/four-feedback-fifth-20261101.log`。
- `tools/unity-compile-check` Rebuild：0 warning / 0 error。
- script pack：2779，parseFail=0，mangled=0。
- 全NPC通用桩执行：790 ok / 27 runFail / 72 javaInterop，与前次基线一致；通用桩缺 getJob().getId()，女神本次以 Unity 真实会话检查为准，不能声称全NPC执行通过。
- 未打APK，未做Android实机显示/操作验收。原缺失脚本的具体技能分组/升级经济规则不在本次恢复范围，现有IMG导出列表含追加攻击组件，详见逆向skill记录。


### 后续澄清：独立五转页签

用户明确要求将五六转技能集中在独立“五转”tab。本次已将它们从四转列表移至第六页；仅在原五转永久完成标记生效时显示。使用原 Skill/Tab/{enabled,disabled}/5 字图及 Tab2 三切片，页头为“五转技能（含六转）”，职业书本图标不变。六页在原有168px范围内各28px，绘制与点击区域一致，不再互相重叠。

Unity `SkillPanelChecks.Run` 本次退出0，`FIFTH_ADVANCEMENT_PASS ... separate_fifth_tab original_tab_assets` 和 `SKILL_PANEL_PASS`；确认四转/五转技能分离、完成标记重新加载、共享技能树不变、两态原页签纹理加载。日志 `docs/four-feedback-fifth-tab-20261101.log`。`git diff --check` 通过。未做Android实机验收。
