# 五项反馈修复记录（2026-11-01）

## 结果

1. 选角：GameSession.ApplyToProfile使用Profile.DeferSaves，把每个已学技能的同步存档合并成一次。原恢复链每个SetLearned都会PlayerPrefs.Save，已移除这一确定阻塞来源；未测量Android实际进场总耗时。
2. 任务：击杀更新任务告知时自动展开已有原图栏，蜗牛已杀数直接显示，不需要打开详情。原计数逻辑保持不变。
3. 技能中心：原JS原样执行，修复教授技能的强制0级、master上限、SkillFactory对象id、移除技能键位与绑定Y。偷学不再错误遗忘刚学的技能，保存保留expiration。
4. 技能携带：虚拟层停止动作注入和点击捕获，下一次点击仍可放置绑定，不触发按键原动作。
5. 后台：修正物品/装备/怪物/技能图鉴分页JSON返回；补齐原端资料搜索10类、ID/名称、精确/包含匹配；所有公共预览图改接本地原IMG；验证原有任务树、列表和详情接口。后台前端资源包已重新生成。

## 实际验证

`LoginGameplayFeedbackChecks.Run` 在Unity 2022.3.62f3c1 batchmode中完整执行，退出码0。

- 正式NpcSession/NpcApi+隔离数据库执行技能中心5个选项：二段跳20级并绑定scan21；删除后0级；三宠1级并扣2000万；偷学点不足拦截及成功后技能30级/Y绑定；风影漫步修复复制当前7级；技能全满将1121002升到30并扣1个原脚本黄金枫叶。
- 原Quest IMG查找蜗牛任务，调用真实OnMobKilled，验证已登记任务告知并展开。
- 正常游戏状态下先验证虚拟控件可捕获，携带技能后验证停止捕获。
- 经AdminApi.Handle及AdminJson序列化验证4个图鉴均返回非空records和total；中文“蜗牛”与空types的全类型搜索；任务townTree/list/detail；物品2000000、装备1302000、怪物100100、技能4111006、NPC9900001的本地PNG。
- 两个成功标记：FIVE_FEEDBACK_PASS skill_center_all_five_branches；FIVE_FEEDBACK_PASS catalog_http_json information_search quest_browse original_img_icons。原始日志保存于five-feedback-2026-11-01.log。
- tools/build_admin_ui.sh --skip-install：92个文件，3.35MB，已更新Assets/Resources/AdminUi/adminui.bytes。既存eval/重复CSS提示仍在。
- 最终tools/unity-compile-check执行dotnet build -t:Rebuild：0警告、0错误；git diff --check通过。
- 脚本包：2779条，npc889、quest470、parseFail0/mangled0。
- NPC广泛测试：790ok、parseFail0、27runFail、72javaInterop，与此前记录一致，不能称全量脚本运行通过。

## 边界

未构建APK；adb devices没有设备；未做Android首次/热进图计时、真机触摸放置和画面验收。后台测试覆盖真实HTTP分发与序列化链，未对每个前端页面做浏览器视觉验收。地图预览只在原地图含miniMap/canvas时有图，不造替代图。

原端依据和SHA256见项目逆向skill的references/login-skill-admin-feedback-2026-11-01.md。本次不改原端JS，也没有新增EXE逆向地址结论。
