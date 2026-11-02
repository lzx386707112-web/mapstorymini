# 五转技能反馈修复（2026-11-01）

- 用户授权V文字改为白色粗体，选中时带黑描边，底板仍取原Tab2 IMG。原Skill/Tab/5确为细黑字，不能据资源存在就认为与其他页签一致。
- 删除选择/拿起技能时所有虚拟按钮的青色脉动/黄色悬停圈，保留顶层拖拽图标。
- 修复TryBindSkill只写e.skillId但显式键按钮实际读KeyMap/QuickSlots的问题。拖入后更新实际键位绑定，从同一技能ID取图标并施放。未改技能图标、消耗或MCV表。

Unity真实Play模式：`AdvancedSkillFeedbackChecks.Run`退出0，标记`ADVANCED_SKILL_FEEDBACK_PASS drag_explicit_key keyboard_drop icon_identity virtual_input casts=3 original_MCV replay`。

覆盖正义崛起1221029、圣狮之主1221030、圣十字架1221015的虚拟键拖入、图标身份、输入映射和施放；额外检查键盘设定窗口DropSkill保留1221030及原图标；圣狮之主从实际BeginCast触发源MCV并获得原生解码帧。原服务端1221030回放调度存在。

日志：`advanced-skill-feedback-20261101.log`；真实OnGUI截图：`advanced-skill-feedback.png`。Unity有已有代码的编译警告及Metal memoryless depth提示，测试退出0。没有用截图拼接替代实际运行。

当前DLL SHA256匹配既有逆向记录；复用其39项视频表和播放器。此次不宣称全职业V/VI所有组件/伤害时间轴已迁移或全量验收，未构建APK、未做本次Android实机验收。
