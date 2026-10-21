# 技能特效逐项资源核对

由 `tools/export_skill_vfx.py` 从原客户端 IMG 生成；括号为帧数，包含 UOL 引用帧和透明占位帧。

| 槽位 | 原技能 ID | 完整视觉分支 |
|---|---|---|
| dash | 1121006 | `effect` (7)、`effect0` (6) |
| rupture | 1121012 | `effect` (24)、`hit/0` (6)、`special/0` (19)、`special/1` (19)、`special/2` (20)、`special/3` (20)、`screen` (28) |
| illusion | 1121020 | `effect` (88)、`hit/0` (6)、`hit/1` (6) |
| roar | 1111008 | `effect` (17)、`hit/0` (2)、`mob` (3) |
| crush | 1111007 | `effect` (19)、`mob` (11) |
| rage | 1101006 | `effect` (13)、`affected` (7) |
| magnet | 1121001 | `effect` (18) |
| berserk | 1121013 | `effect` (14)、`hit/0` (8) |
| firearrow | 2101004 | `effect` (10)、`hit/0` (5)、`ball` (3) |
| doom | 2111002 | `effect` (12)、`prepare` (13)、`special/0` (6)、`special/1` (6)、`special/2` (3) |
| chain | 2121006 | `effect` (19)、`hit/0` (7) |
| starfall | 2121007 | `effect` (25)、`effect0` (20)、`hit/0` (23)、`tile/0` (23)、`tile/1` (23)、`tile/2` (22)、`tile/3` (25)、`tile/4` (25)、`tile/5` (25)、`tile/6` (27)、`tile/7` (27)、`tile/8` (27) |
| firebolt | 3111003 | `effect` (9)、`hit/0` (5)、`ball` (3)、`special/0` (3)、`special/1` (3)、`special/2` (3)、`tile/0` (3)、`tile/1` (4)、`tile/2` (6) |
| arrowrain | 3111004 | `effect` (12)、`hit/0` (3)、`special/0` (1) |
| dragon | 3121003 | `effect` (14)、`hit/0` (4)、`ball` (4) |
| storm | 3121022 | `effect` (15)、`hit/0` (8) |
| holyarrow | 2301005 | `effect` (7)、`hit/0` (3)、`ball` (3) |
| holy | 2311004 | `effect` (11)、`hit/0` (2) |
| ray | 2321007 | `effect` (15)、`hit/0` (7)、`ball` (1) |
| libra | 2321020 | `effect` (15)、`affected/0` (10)、`hit/0` (7) |
| illusion_slash | 1121021 | `hit/0` (6)、`hit/1` (6) |
| illusion_explosion | 1121022 | `hit/0` (6)、`hit/1` (6) |

## 播放接入与本轮纠正

- 斗气死亡断层：服务端 `CloseRangeDamageHandler.DEATH_FAULT_FIELD_EFFECT` 明确调用 `customSkill/deathFault/full`，实际资源位于 **Map/Effect.img**。已导出完整全屏帧，以相机中心覆盖视口，保留服务端 1000ms 延迟判伤；不再将四组零散 special 当作地面波次铺开。此前“这批技能没有全屏资源”的结论只检查了 Skill IMG，范围不完整，已纠正。
- 烈火箭（3111003）：原等级攻击框 `[-150,-100]..[150,100]` 是以人物为中心的范围攻击。释放时就生成原 5 支落矢，不再依赖 ball 命中或为每只敌人重复生成；地火按原 20px 间距覆盖攻击范围，两侧敌人均参与判定。
- 箭雨（3111004）：使用原 8 支落矢、90ms 间隔和 `y=230px` 发射高度。此前误把 fall 当高度；烈火箭应使用 `y=130px`，而不是从 400px 高空起落。
- 末日烈焰：起手使用 `prepare/action=alert2`，起手结束再播放 `action=alert4` 的真实引用帧与原 effect；爆炸碎片散布于攻击范围内，判伤发生在主体爆发期间，避免旧流程在主体消失后才判伤。
- 狂暴攻击/斗气死亡断层角色动作：`brandish1` 是身体 `action/frame` 引用序列，不能当成贴图直接合成；解析到 swing/stab 的具体身体和装备帧。技能 action 列表是候选动作，不将四个相同 brandish1 拼成四遍。原 28 帧错误重复已改为 7 帧完整身体动作；所有发型同步重新导出。两手武器使用对应 swingTF 分支。
- 火焰箭、圣箭术、飞龙冲击波、光芒飞箭：使用独立弹道目标范围，不再借用缺省 200px 近战框。施法时固定原点与方向；弹道到达目标后才判伤，保留飞龙 ball.delay=640ms。空放弹道也飞完整射程。
- 剑影分身沿用服务端 17 次子技能时间轴，分别使用 1121021、1121022 的命中资源。
- 状态 mob、命中 hit、affected、天降落星的 9 组 tile 和资源 pivot 按各节点接入；取消、死亡、退出地图清理角色持有的特效及延迟命中。

## 尚属本地适配的参数

原图、原点、帧延迟、透明度、节点参数均保留。旧客户端完整调度代码不在服务端内，不能把逐帧资源验证当作最终表现与原 EXE 一致的证明。缺省弹道射程采用 600px、弹速 800px/s；无 ball.delay 的出弹时刻采用角色动作中点；落矢以原 y 起始高度和 duration 时长落地，散布为均匀分布；末日烈焰按主体 effect 中点判伤。这些是明确的单机适配，未宣称原客户端逐毫秒一致。

## 核对方式

`python3 tools/verify_skill_vfx.py`：重新解码 Skill IMG 及 Map/Effect IMG，逐帧比较导出像素、原点和延迟，并生成 [全分支预览](skill-vfx-contact-sheet.jpg)。

Unity `SkillVfxChecks.Run`：验证所有 Sprite 资源和 pivot 后，运行触屏零 MP 释放、全技能流程、10 世界单位（400px）远程目标、范围技能前后两侧目标和取消/退出清理回归。图形模式下另保存这六个问题技能的运行画面到 `docs/skill-regression/`。资源加载成功仅验证资源，范围与时序另由运行测试验证。
