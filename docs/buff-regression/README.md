# 临时状态栏回归

本轮迁移对象是 `CTemporaryStatView` 的玩家辅助技能/普通属性药水路径。原端逆向证据集中在 [.agents/skills/beidou-client-reverse/references/temporary-stat-view.md](../../.agents/skills/beidou-client-reverse/references/temporary-stat-view.md)。不要把公共状态注册成功解释成所有特殊技能战斗机制都已完成。

## 实现

- `NativeBuffs`：按来源与 BuffStat 保存效果，独立时钟；共享属性较强优先、相等比较状态数量；非共享属性替换来源；保留与恢复被压制的共享属性；监控上限、刷新、取消、到期、驱散、死亡。
- 从源 `SkillFactory.java` / `StatEffect.java` / `constants/skills` 生成 172 个专用映射、212 个显式分类、27 个召唤分类；保留 fallthrough 和显式零。技能原根节点参与分类，使用 overTime/summon 施加门控。
- GameHUD 右上单行 32 像素逻辑槽：原技能/物品图标、原16张 CoolTime 遮罩、最后3秒闪烁、原技能/物品提示、右键取消。手机沿用已有相对光标与双指右键。没有自绘图标、扇形或数字倒计时。
- 召唤与状态共用到期/取消；换图恢复跟随召唤，固定替身取消；不再只共用一个召唤槽。
- WATK/WDEF/MATK/MDEF/ACC/AVOID、MAPLE_WARRIOR、HYPERBODYHP/MP、RECOVERY周期、INFINITY/CONCENTRATE耗蓝已接线。普通属性药水使用同一状态源，炼金术修正时长；不支持的特殊物品保持拒绝使用，不能悄悄扣除。

## 已执行

- `NativeSkillNodeChecks.Run`：PASS，802个技能、2119等级样本、5749视觉片段、61464帧引用；蜗牛投掷术1/2/3真实施放/飞行/命中；4种召唤实际渲染/固定与跟随行为；HP/MP消耗事务。召唤测试现在先注册权威状态，避免生成无生命周期的孤立召唤。
- `NativeBuffRenderChecks.Run`：PASS，实际施放团队治疗/圣甲术/神圣之火；周期恢复、成对HP/MP上限、右键取消、驱散、死亡清除。800×600及1280×720直接从Unity最终帧缓冲截取，已人工检查图标确实到达右上角。
- `NativeBuffChecks.Run` 已执行：731技能、17646全部等级、3765含状态等级、50状态键；逐等级图标及16张原遮罩可读；共享属性共存/优先/刷新/次优恢复/到期/清除/原坐标通过。最终 `NativeBuffChecks.RunAll` 已重跑 PASS，新增药水交叉优先/取消恢复、单例替换、永久时长溢出回归均通过；随后真实施放/提示/渲染回归再次 PASS。
- C# 编译：0 warning、0 error；`git diff --check` 无错误。

复现：

```sh
rtk proxy python3 tools/export_buff_contract.py
rtk proxy /Applications/Unity/Unity.app/Contents/MacOS/Unity -batchmode -projectPath "$PWD" -executeMethod NativeBuffChecks.RunAll -logFile /tmp/mapledash-buff-all.log
rtk proxy /Applications/Unity/Unity.app/Contents/MacOS/Unity -batchmode -projectPath "$PWD" -executeMethod NativeSkillNodeChecks.Run -logFile /tmp/mapledash-buff-skill-regression.log
```

测试使用内存角色，隔离正常 Boot，不登录和写角色数据库。第一轮渲染测试未隔离引导，已停止作为有效验证使用；以上 PASS 来自隔离后重跑。

## 边界

- 50类状态键可解析不等于其特殊战斗机制全部移植。急速攻速、移速/跳跃最终属性、魔法盾/金钱盾、隐身、反射、元素附魔、暴击、经验/掉落、骑乘、追踪目标及连击仍需要独立逻辑迁移和源端验证。
- 无倒计时的骑乘/追踪/连击不能用普通 WZ time 冒充；当前普通施放路径不会把缺失时长猜成“默认持续若干秒”。
- 特殊药水、区域/组队条件药水、怪物卡、倍率券、Guild Quest和Family权利的来源服务不属于已接通路径。类型3 Guild Quest及类型4 Family的真实用途已记录，未混用成疾病图标。
- 未构建APK，未做Android实机验证；不宣称“全技能和服务端逻辑零遗漏”。

### 2026-10-29 防护与免疫补线
`NativeBuffChecks.RunAll` 本轮退出0，日志 `/tmp/mapledash-guard-all.log`。新增防护结算检查覆盖魔法盾优先级、MP不足、奇数伤害金钱盾整数减半、金币不足取消及恰好足够不取消、mpattack分支。PlayMode 检查实际注册/取消神圣之盾对封印、眩晕、诱惑的影响；原状态栏与施放检查继续 PASS。普通受伤已接防护；Boss固定/百分比和毒伤尚未套用，待对应分类证据。Android未测。
