# 物品使用入口审计（2026-10-30，继续接入中）

运行 `rtk proxy python3 tools/audit_inventory_functions.py` 复现。扫描复制的原IMG，静态入口与实际验证分别记录，不能据分类数量声称全功能完成。明细在 inventory-function-audit.json。

用户已决定：26件源端服务端也不存在引用脚本的道具，保持源端不可用，归为“源端缺失”，不编写替代业务。

## 当前静态分类

| 状态与路由 | IMG条目数 |
| --- | ---: |
| implemented-controls-partial:morph | 31 |
| implemented-needs-runtime-check:cash-equipment | 13 |
| implemented-needs-runtime-check:extended-disease | 4 |
| implemented-needs-runtime-check:face-expression | 15 |
| implemented-needs-runtime-check:jukebox | 1 |
| implemented-needs-runtime-check:monster-capture | 13 |
| implemented-needs-runtime-check:mount-fatigue | 1 |
| implemented-needs-runtime-check:pet-food | 24 |
| implemented-needs-runtime-check:reward-box | 60 |
| implemented-needs-runtime-check:stat-potion | 110 |
| implemented-needs-runtime-check:world-quest-draw | 1 |
| implemented-passive:scheduled-rate-coupon | 44 |
| implemented-service-ui-partial:equipment-scroll | 775 |
| implemented-ui-validation-pending:death-wheel | 1 |
| implemented-ui-validation-pending:vega-scroll | 2 |
| implemented:banked-experience | 13 |
| implemented:chair | 427 |
| implemented:cure | 4 |
| implemented:experience-potion | 1 |
| implemented:money-bag | 3 |
| implemented:monster-card | 343 |
| implemented:remote-npc-shop | 2 |
| implemented:restorative | 190 |
| implemented:return-scroll | 26 |
| implemented:skill-book | 178 |
| implemented:stat-potion | 215 |
| implemented:summon-bag | 284 |
| implemented:timed-item-grant | 4 |
| not-double-click:ammunition | 45 |
| not-double-click:etc | 2823 |
| not-double-click:install | 257 |
| npc-route-needs-runtime-check:cash-npc-or-quest-ticket | 158 |
| npc-route:npc-menu | 2 |
| script-route-needs-runtime-check:item-script | 39 |
| script-route-needs-runtime-check:pet-water-of-life | 1 |
| source-disabled:cash-client-no-op | 14 |
| source-disabled:cash-source-disabled | 10 |
| source-missing-script:item-script | 26 |
| unsupported:cash-item | 225 |
| unsupported:special-consumable | 82 |

统计包含历史活动、拾取触发、NPC兑换券、被动携带与无效果占位，尚未逐类完成源端用途核对。不能将所有现金物品视为普通双击消耗品，也不能将未知条目默认消费。

## 已验证范围

原入口35项，加物品功能扩展后最新隔离Unity `InventoryPortalChecks.Run` PASS215，日志 docs/skill-regression/inventory-items-isolated33-2026-10-30.txt（临时隔离工程排除共享 PreviewShot 编译错误；音乐盒/表情/宠物切换新增覆盖，宠物视觉仍缺）；包含装备/图腾槽/拖放键位、隐藏传送门、吸怪、限时券、卡片、技能书master持久化、卷轴属性实例、吊坠、轮回刷怪点生命周期、回城、突破石脚本、倍率券时段、容量查询、金锤、WorldQuest/北斗之星/偷学技能点/兵法书。

脚本pack2779/npc889/quest470、parseFail0/mangled0。NPC全量mock基线仍为files889/ok790/runFail27/javaInterop72，不能报告全通过。

## 仍需完成

- 变身原Morph动画/捕捉已接线，仍需控制和实际运行验证；宠物已接基础行、到期玩偶、原生命之水脚本与212/524食品规则，召唤与饥饿已接服务规则，宠物进化原NPC脚本及亲密度已验证；跟随与技能仍缺。
- 现金物品的独立专用处理、NPC/被动/源端禁用分类与真实运行验证。
- 白卷确认已按EXE接线待运行；织女星与匠人之魂的原窗口交互、原成功/失败动画。
- 暂存经验领取原输入入口、倍率券状态图已接线待运行；药品尚未覆盖全部特殊效果与可见表现。
- 39条原item脚本中其余地图条件/事件分支验证；源端缺失26条保持缺失。
- 原资源视觉与Android实机验证。Unity通过不等于APK完成验收。

精灵吊坠计时加成、1016轮回施放和地图生命周期已接线；轮回原IMG视觉与实际场景仍需验证。相关源端函数、公式和边界统一维护于 .agents/skills/beidou-client-reverse/references/inventory-use-and-equipment.md。


本次已按用户要求停止扩展，下一次从 [续接记录](inventory-functions-next-session.md) 继续。该文档保留当前批收尾范围、未接307件、158件NPC/任务券验证范围、26件源端缺失决策和全部迁移边界。
