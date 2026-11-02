# 2026-11-01 五项反馈验证记录

## 已通过

- 纯 C# / Unity API 编译检查：0 errors，0 warnings。
- 服务端回归：725 passed / 0 failed，含100px空中假人支撑回归、角色资料隔离、同频道目标关系树、宠物过滤持久化/装备门控等。
- LoginGameplayFeedbackChecks：面板指针和手势起点阻挡、松手和关闭窗口；技能中心原脚本五分支；后台HTTP发放3,000,000,000金币，在线Profile/record同步，保存重载不回滚。退出0。
- NativeSkillNodeChecks：802技能、2119等级、5749 clips、61464 frames资源审计；蜗牛三级实际伤害/弹道/渲染，4种召唤及HP费用，4111006/14101004/11101005实际移动/特效像素；2101002原地图100000005快速移动落点/扣蓝、无效方向不扣蓝，取消MapService注册后仍可施放。退出0。
- NativeCharacterInfoSceneChecks：原端四种UserInfo背景/纸娃娃，宠物与坐骑预览/装备列表，手册非空封面/勋章，物品/愿望侧窗，学院/关系树，宠物排除/搜索结果截图人工检查；展开及原关闭钮命中通过。最后一次11张截图、宠物/坐骑/手册展开、勋章卷动、搜索遮挡及原关闭钮命中通过，退出0；随后侧窗遮挡补齐按下起点判断，经编译检查。

## 截图

本目录userinfo-0..3、userinfo-items、userinfo-wishes、family、family-tree、pet-exclusions、pet-exclusions-search为独立内存渲染fixture结果；不连接服务器、不发放或交易物品、不保存玩家数据。GameLoop.Boot按SessionState测试标志跳过。

## 明确边界

资源遍历不等于802项技能每一种机制均实际施放验收。现有通用攻击/范围兜底、部分现代技能及原端快速移动120ms pending时序仍有迁移边界，见逆向skill的技能/输入记录。学院hover说明、部分人数标注/刷新及字体颜色、宠物王冠、现金武器替换尚未完整迁移。角色扩展主要入口和数据已补，不能标记为所有原端细节完整复刻。没有Android APK及实机触摸验收。
