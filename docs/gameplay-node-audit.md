# 技能、地图、怪物节点与运行逻辑审计

本报告区分全量数据枚举、代码接线、源服规则和 EXE 验证；不把“节点可解析”算作玩法实现完毕。

## 覆盖与复现

原始数据：76 个 Skill IMG（802 个技能）、6,337 个地图 IMG、2,908 个怪物 IMG；三组解析错误均为 0。
完整路径族、每族出现的文件数、最多 3 个样例、每份 IMG 的 SHA256，以及每个技能的 rootNodes/levelFields，见 `tools/native_gameplay_nodes.json`。
数字路径规范化为 `*`，深度最多 8 层，UOL 只枚举引用本身，不展开成另一个作者节点；Canvas 不解码。视觉 UOL/帧解析另由 `NativeSkillNodeChecks` 的独立样本验证。
出现次数包含值为 0 的节点，不能当作启用该功能的地图/怪物数。

复现：`rtk proxy python3 tools/audit_gameplay_nodes.py`；Unity `NativeSkillNodeChecks.Run` 会运行 `NativeGameplayNodeChecks.Check`，然后进入隔离 Play 场景。
当前 UI 职业树共 731 个技能：attack 444、buff 162、passive 92、summon 33；另有 71 个源技能未纳入此职业树，不等于数据不存在。
职业树类型仍由 export_jobs.py 的字段启发式分类，**不是完整移植 SkillFactory/StatEffect 的技能 ID 分派**。

## 本次已补的公共缺口

| 范围 | 修改后的行为 | 源端依据 |
|---|---|---|
| 召唤 | 当前技能/等级 summon 分支提供帧、攻击框、目标数和 attackAfter；不再借用天秤天使配置；无 attack1/range 的傀儡不攻击 | Skill IMG；StatEffect.getSummonMovementType；既有天秤数据导出 |
| 召唤移动 | 原服列出的 6 个 STATIONARY ID 留在召唤位置；fly/move 动作按实际存在节点选择 | StatEffect.java:1854 起；其余跟随速度/距离仍是旧本地策略 |
| 元素 | ID 施放保留技能 elemAttr，接入已有元素抗性/弱点判定 | client/SkillFactory.java:121；CombatRules.ElementModifier |
| 生命消耗 | 逐级 hpCon，HP 必须大于消耗；拒绝时不扣蓝/不启动冷却，成功时扣确切消耗 | StatEffect.calcHPChange；Character.applyHpMpChange |
| 链接地图 | 规则参数来自请求图 info，地形来自链接图；106021002 的 fieldLimit=262216，不被链接图 262224 覆盖 | MapFactory.java:166–200（infoData 保持不变） |
| 刷怪 | createMobInterval 毫秒数传入 MapSpawner；910320100 的 3000 不再被固定 5000 覆盖 | MapFactory.java:200；SpawnPoint |
| 传送门 | tm=0 是合法地图，999999999 仍是无目标 | 原端合法 map 0/既有 Character.changeMap 迁移规则 |
| 链接怪 | 控制器动作时序/可移动判断使用与渲染一致的链接素材根；血量、技能表、revive 仍来自自身 info | LifeFactory 链接攻击继承；既有 WzAssets.MobClip 链接规则 |
| 怪物动作时序 | 仅数值帧参与时长，UOL 解析，delay 取绝对值，缺省 100ms，与实际播放一致 | WzAssets.MobClip 的既有动作时序合同 |

## 按技能种类检查结果

| 种类 | 当前执行范围 | 仍未完善 |
|---|---|---|
| 直接攻击/近战/远程/魔法 | 逐级 MP/HP/冷却/次数/目标数/范围/固定伤害、ball/hit、元素；20 个别名保留专项分派 | 其他 ID 的位移、锁定、反射、连锁、蓄力、持续按键、DOT、弹药/道具/金币消耗、服务端特殊 ID 规则并未全接入 |
| 增益/治疗 | 通用属性、SPEED/JUMP普通移动、血蓝上限、魔法盾/金钱盾、状态覆盖/共存/消除、部分周期恢复已接入 | 队友分发、完整被动/专用状态、repeat/affected持续视觉仍未完成；见 temporary-stat-view.md |
| 召唤/傀儡/精灵/飞鸟 | 全 34 个带视觉 summon 分支的技能可解析/解码；每技能动作/范围/目标数；固定召唤物停留 | 已按技能来源分别持有召唤，取消/到期同步；CIRCLE_FOLLOW 的 EXE 环绕轨迹、召唤消失动画、攻击命中帧、黑暗灵魂回血/增益数值周期已接入，周期特效和现代自定义召唤特殊规则未完整迁移 |
| 被动 | 当前职业树识别 passive；主动施放会拒绝 | mastery/暴击/自然回复/武器专精/最终攻击等并未逐技能接入角色属性或事件钩子；prop 不能作为被动/增益的可靠判据 |
| 专用状态节点 | 公共解析已覆盖 keydown/end/finish/state/effect1..3 等 | 不能同时当作施法特效播放；需按 EXE 状态/武器/形态/按键释放条件接线，当前缺口详见 skill-node-audit.md |

## 地图重点缺口

fieldLimit 不是完整执行：当前有跳跃等已迁移门控，不能声称全部 bit 都实现。以下节点只是数据发现/代码缺口，不根据名字自行设计玩法。

| 节点 | 含此节点文件数 | 当前边界 |
|---|---:|---|
| `info/timeLimit` | 885 | 未接入地图时限退出生命周期 |
| `info/forcedReturn` | 6337 | 未完整接入登录/地图时限/强制返回条件 |
| `info/decHP` | 132 | 已接服务端配置周期直扣HP；环境死亡/设备视觉仍需全量场景验证 |
| `info/protectItem` | 116 | 已与类型1、负position穿戴状态关联；MAP_PROTECTION及COLD地区分支已接 |
| `info/swim` | 6080 | 未按原 EXE 实现水中积分，节点为 0 也计入 |
| `info/fly` | 4315 | 未按原 EXE 实现飞行输入/物理，节点为 0 也计入 |
| `info/onUserEnter` | 2741 | 尚无完整入口脚本调度 |
| `info/onFirstUserEnter` | 3297 | 尚无完整首次入场脚本生命周期 |
| `reactor/*/id` | 524 | 尚无完整 reactor 状态/击打/脚本/掉落链 |
| `info/timeMob` | 75 | 已解析id/message并在复活列表命中时发原公告；源服不读起止小时，未自行新增小时调度 |
| `info/everlast` | 28 | 已接权威/未绑定掉落寿命，普通到期严格<now；退出清理仍独立 |

## 怪物重点缺口

| 节点/规则 | 含节点文件数 | 当前边界 |
|---|---:|---|
| `fly` | 121 | 缺原端飞行 AI/运动积分，当前不能按地面巡逻替代 |
| `info/hpRecovery` | 578 | 未接入原端恢复周期/战斗状态条件 |
| `info/mpRecovery` | 628 | 源Java不消费同名字段；已接MapManager每respawn_interval恢复mob.level的MP，非节点值 |
| `info/removeAfter` | 175 | 已接无奖励超时消失、释放所属地图刷新容量 |
| `info/selfDestruction` | 14 | 已接hp阈值正常击杀/有奖励，与无伤害者超时移除区分，使用action原死亡动画；不虚构爆炸伤害 |
| `info/firstAttack` | 1155 | 当前 AI 不是完整原端主动/被动仇恨状态机 |
| `info/fixedDamage` | 128 | 字段已载入 stats，但无明确运行时消费；原服也未检索到该字段的同名消费，不凭名字实现 |
| `info/onlyNormalAttack` | 27 | 同上，需继续追踪 EXE 是否消费 |
| `info/skill` | 666 | 常见 MobSkill 已分派；概率、朝向矩形、动作命中时刻、攻击特效/弹道仍需逐类型对照 |

反伤：MobSkill 的 143/144/145 能施加状态，但源服 AbstractDealDamageHandler:665–683 仅从 145 取扣血值（物理 x、魔法 y）。这也是当前运行时行为；不能把它擅自“修正”为别的规则。MobSkill 发出的 reflection 列表使用 x，与该处理器的魔法 y 存在差异，待 EXE/协议验证。

## 每种等级字段的审计入口

下表为 802 个技能的等级字段并集，计数按技能去重。完整对应技能 ID 在 JSON 的 skills[].levelFields 中。非数值分支也列入，防止只扫描标量漏掉 level 下 ball/hit/action。

| 字段 | 技能数 | 当前公共消费 |
|---|---:|---|
| `6thCount` | 1 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `acc` | 7 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `action` | 2 | 等级专有动作 |
| `attackCount` | 299 | 段数 |
| `ball` | 24 | 等级专有视觉分支 |
| `bdR` | 2 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `bulletConsume` | 4 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `bulletCount` | 79 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `cooltime` | 319 | 施法冷却 |
| `cr` | 1 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `criticalDamage` | 1 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `damage` | 439 | 普通物理百分比；特殊公式未全迁移 |
| `damagepc` | 6 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `dateExpire` | 9 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `dot` | 4 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `dotInterval` | 2 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `dotTime` | 1 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `dummyStr` | 1 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `dummyStr2` | 1 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `dummyStr3` | 1 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `dummyStr4` | 1 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `dummyStr5` | 1 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `dummyStr6` | 1 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `eva` | 7 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `fixdamage` | 3 | 固定伤害 |
| `hit` | 8 | 等级专有视觉分支 |
| `hp` | 5 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `hpCon` | 30 | 生命消耗事务 |
| `hs` | 778 | 说明文本元数据，不参与战斗 |
| `ignoreMobpdpR` | 2 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `itemCon` | 9 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `itemConNo` | 9 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `itemConsume` | 2 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `jump` | 9 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `lt` | 440 | 普通范围（特殊目标策略另计） |
| `lt2` | 1 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `mad` | 288 | 魔法技能强度；增益未全接入 |
| `madprop` | 3 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `mastery` | 59 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `mdd` | 28 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `mobCount` | 427 | 目标数（召唤读 attack1/info） |
| `moneyCon` | 1 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `morph` | 5 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `mp` | 5 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `mpCon` | 621 | 施法消耗；特殊职业减耗未全迁移 |
| `ndTime` | 2 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `pad` | 21 | 通用攻击增益 |
| `pdd` | 34 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `prop` | 96 | 少量专项/怪物效果；非全技能概率调度 |
| `q` | 4 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `q2` | 2 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `range` | 34 | 通用缺省范围 |
| `rb` | 440 | 普通范围 |
| `rb2` | 1 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `s` | 2 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `s2` | 1 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `speed` | 18 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `str` | 3 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `subTime` | 3 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `time` | 318 | 持续时间（仅已有专项/召唤/pad） |
| `u` | 5 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `u2` | 3 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `updatableTime` | 2 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `v` | 3 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `v2` | 1 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `w` | 6 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `w2` | 4 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `x` | 257 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `y` | 54 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |
| `z` | 18 | 未完整接入；必须核对 StatEffect 的通用读取及技能 ID 专项分派 |

## 源码与 EXE 边界

所有原 IMG 身份和本次引用的 7 份 Java 文件 SHA256 已写入 JSON 的 sources/logicSources；EXE 身份在 clientBinary。这里只新增源端数据/Java 对照结论，没有新增 EXE 函数地址结论。EXE 已确定部分继续以 beidou-client-reverse 各 reference 为准。

本次检查不等于“全部技能、地图、怪物玩法均已补齐”，也不是 Android APK/实机验收。

## 本次实际验证（2026-10-29）

- 全量原数据解析：76 / 6,337 / 2,908 份 IMG，错误 0。
- Unity `NativeSkillNodeChecks.Run` 退出码 0：802 技能 / 2,119 等级样本 / 5,749 动画 / 61,464 帧引用匹配独立 wzpy fixture。
- `NativeGameplayNodeChecks.Check`：34 类召唤素材逐帧原图解码；召唤自身攻击数据、元素、链接图本地规则、3000ms 刷怪间隔、地图 0 门、链接怪动作/自身血量检查通过。
- 隔离 Play：蜗牛三等级的可见像素、空放移动、命中和 10/25/40 固伤、取消、左朝向通过；四类召唤可见像素、固定召唤停留/跟随召唤移动通过；HP 不足时不扣蓝/不进入冷却、成功扣到 HP=1 通过。
- 四类召唤实际相机渲染截图为 `docs/skill-regression/summon-{3111002,3111005,5211001,2321020}.png`，已人工查看。
- C# compile-check：0 warning / 0 error；`git diff --check`、Python 语法检查通过。
- 摘要日志保存在 `docs/skill-regression/gameplay-checks.txt`；可按前述入口重跑，不依赖临时日志。

2026-10-29接续状态：以上表格已按当前实现纠正；仍不是全迁移完成声明。原端证据、测试日志及剩余范围统一见 `.agents/skills/beidou-client-reverse/references/gameplay-node-coverage.md`、`temporary-stat-view.md`、`input-and-physics.md`。等级字段表原“未完整接入”是全分支范围，不能反向解读为该字段完全未消费。

2026-10-29继续补齐：STANCE消费原prop，按EXE限制0..100与fieldLimit bit1判定，成功只抑制普通击退；INVINCIBLE仅物理伤害在防御后、整数化前按百分比减伤。两项原x86对照84/20组及独立Unity受击回归PASS。特殊抗击退动画/被动、基础伤害公式、水中/飞行/骑乘等仍未完整迁移，不能将此批次解释为全部完成。

2026-10-29续补：远程弹药选择/等级限制/单槽费用/影分身弹数与SOULARROW、SHADOW_CLAW豁免已接真实施放和普攻入口；飞镖/子弹耗尽保留零槽。71个未导出技能已能直接读取原IMG等级数据（未增补技能窗职业布局）。三种崩坏接公共主动入口和对应怪物增益清除，净化补怪物攻防增益清除。详细源端依据、日志及边界见gameplay-node-coverage.md；不代表蓄力、影分身伤害、远程弹道或全部主动分类完成。


2026-10-29续补挑衅：4121003/4221003从误导出的自身buff纠正为原远程攻击状态路径，成功命中登记SHOWDOWN，拒绝Boss、重复覆盖、原duration+effect延时-100ms到期；经验同时作用单人/队伍个人和组队奖励，掉落只加概率，不加金币数量。四个RangedAttackHandler前置分流技能补免弹药选择。双防状态解释、挑衅原EXE选敌范围及攻击表现尚未补齐，不能把本项称为完整挑衅移植；详见reverse skill记录。


2026-10-29优先项续补：有lt/rb的普通技能选敌新增原服怪物身体框交叠，保留UOL、origin回退、朝向/noFlip和自身视觉优先link规则；
经典4111006/14101004接普通空中二段跳准入、原冲量、单次离地标记与落地/绳梯重置；
怪物施法HP门槛纠正为当前HP百分比不高于配置hp（原实现比较方向错误），保留float除法后整数截断。
隔离Unity `/tmp/mapledash-priority-ai-nodes.log` 与 `/tmp/mapledash-priority-ai-runtime.log` 均退出0；
实际HitAt擦身体边缘、二段跳施法/拒绝/冲量/落地和怪物半血施法均通过，未运行Android实机。
尚未完成：无lt/rb的EXE选敌、11101005及特殊移动二段跳门控、按住/蓄力技能状态机、地图事件脚本宿主、firstAttack/追击/飞行AI。
蓄力函数地址和地图进场源服合同已归档reverse skill；本批不是全部迁移完成。


## 2026-10-29 四项优先逻辑接续（未全部完成）

- 持续射击3121004/5221004/13111002：真实快捷键按住/松开，起手不扣费，严格>100ms逐次扣MP/弹药，资源不足结束；13种其他蓄力仍缺松键攻击/伤害换算，持键角色帧未完整对齐。
- 地图进场：请求地图自己的onFirstUserEnter/onUserEnter执行原JS，缓存/first标记/延时传送检查；实际karing_first.js通过。过场、远端地图、事件实例与Boss联动仍缺。
- Soul Rush11101005：地面支撑、WEAKEN122、独立1500ms门槛、等级翻倍冲量已接；共同状态与特殊区域门控未全核。
- 追击：修正模板速度先+100夹0..140再加修正夹10..140；地面基准读Physics.walkSpeed，追击仅有chase动画且有目标时选chaseSpeed。完整选敌/加速度/寻路和飞行AI仍缺。
- 持续速射回归发现完整武器目录缺失：WeaponDefs现可补读原始武器IMG，枪不再回退成剑；分类/倍率复用既有导出TYPES（原服WeaponType.GUN=3.6/KNUCKLE=4.8）。

原端地址、掩码、调用链、资源与迁移边界分别归档reverse skill的skill-visual-nodes、input-and-physics、monster-skills、gameplay-node-coverage。
本轮实际持续技能/Soul Rush运行时检查 `/tmp/mapledash-held-soulrush-final3.log` 退出0，包含三种技能起手/扣费/间隔/松键/MP耗尽与SoulRush全部新增断言。
节点检查 `/tmp/mapledash-gameplay-final2.log` 退出0，包含原地图脚本、速度限幅、elapsed限幅与既有节点回归。
最终追击分支与整轮持续/SoulRush运行时回归 `/tmp/mapledash-priority-four-runtime2.log` 退出0；持久摘要见 docs/skill-regression/priority-four-checks.txt。
NPC脚本harness全量执行仍有47运行失败/73Java互操作，不能写全量通过；self-test26通过、pack2779脚本解析错误0。
没有构建或部署APK，也未做Android实机验收。
