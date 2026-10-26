# 原端 Boss 服务端规则迁移

2026-10-23。此前 `Stage/BossUnit` 只有章节原型的跳砸／红圈／召唤状态机；原版地图中的 Boss 是 `EnemyUnit`，没有运行源端 MobSkill。旧章节原型不属于原版地图 Boss 实现，本次保留。

## 已接入的共用逻辑

| 原端依据 | 当前入口与行为 |
| --- | --- |
| `Monster.canUseSkill/usedSkill`、`MoveLifeHandler`、`MobSkillFactory` | `NativeMobController` 从 `Mob/*.img/info/skill` 和 `Skill/MobSkill.img` 加载技能，按 HP 门槛、MP、封印、独立等级冷却选择；技能动作结束再生效；兼容技能优先级保留。 |
| `LifeFactory`、`MobAttackInfoFactory`、`TakeDamageHandler` | 原攻击动作、范围、`attackAfter`、`conMP`、`fixDamR`、`deadlyAttack`、`mpBurn` 和附加疾病；无可核实范围时不制造全屏攻击。 |
| `MobSkill.applyEffect` | 攻防增益、治疗、物理／魔法免疫、反伤、召唤及数量限制；封印／眩晕／诱惑／虚弱／缓速／混乱等疾病状态、最多两个疾病；放逐按原目标地图和 portal 传送。 |
| `Character.gainExp`、`StatEffect.calcHPChange` | 诅咒减半经验；僵尸状态下道具恢复减半、技能治疗反转。 |
| `Monster.killBy` | 死亡动作完成后按 `info/revive` 生成后继怪物；精确 long HP，十亿级血量不吞掉小额命中。 |
| `MapleMap` 扎昆／黑龙逻辑 | 扎昆首阶段在有手臂时不可伤害；黑龙部件伤害同步共享血量，本体直接受击恢复；混沌黑龙换体补齐部件，八个死亡部件齐全后结算本体。 |
| `MobSkill` 现代兼容分支 | 阿卡伊勒屏裂的延迟／涡旋范围／趴下缩减；森兰丸、威尔、麦格纳斯、露希德、赛莲的兼容百分比伤害。源端 visual-only 的技能仍不擅自增加伤害。 |
| `KaringBossCompat` | 五个 Boss 的入场保护、攻击／技能冷却；穷奇屏幕／近身攻击、咖凌二阶段暗脉冲的延迟、范围和百分比伤害，原端明确抑制的旧疾病不再施加。 |
| `LucidBossCompat` | 蝴蝶血量分档与容量、蝴蝶爆发、粉尘、噩梦和石像上限、龙息、激光雨、十二段弹幕、冲撞路径、玻璃破碎、障碍区和传染炸弹；三阶段 45 秒超时、失败怪、延迟秒杀与十秒后 16% HP 二阶段重生。坐标表直接保留源代码值。 |
| `BossDamageGrowth` | 本地服务端持久化 15 类 Boss 击杀进度；任务／Boss／卡片按 30%／40%／30% 加成，上限两倍；怪物卡按种类计数；重复击杀通知去重。 |
| `ExpeditionBossLog` | `ITransport.Boss` 的检查／进入接口，原十类 Boss 每日次数、配置开关、跨日重置与删除角色清理。源端当前条目全部是 daily，没有编造 weekly 配置。 |
| `Character.getBossDropRate` | Boss 掉落使用 `world_prop.boss_drop_rate` 并叠加当前学院掉落增益。 |

运行接线：`FieldDirector` 初始刷怪、动态召唤和 revive 都绑定 `NativeMobController`；`EnemyUnit.TakeHit` 经过免疫／反伤／精确血量结算；技能命中传入物理／魔法类型。`NativeBossEncounter` 持有定时队列，离图／清图取消包含 revive 在内的所有任务；施法者死亡取消未命中的攻击。NPC 的 `cm.spawnMonster`、地图生成／查询怪物接口，以及 `Java.type` 中的 `LifeFactory.getMonster`、`java.awt.Point` 已接入，不改源脚本。

`NativeBossRules`、精确血量、施法状态、定时队列和成长／次数服务不依赖 Unity。战斗场景适配层位于 `Field/`，继续使用当前单机 `PlayerCtrl`／`EnemyUnit`。这并不是把现有整套战斗改造成可联机的权威服务端。

## 已确认的边界

- 怪物挑选动作仍需本地 AI：源端服务器校验客户端发出的动作，本地每 500ms 尝试挑选。普通伤害／移动沿用当前本地策略，不能据服务器代码声称与原 EXE 的 AI、随机伤害、控制手感完全相同。
- 原 `event/*.js` 的完整 `EventManager/EventInstanceManager`、远征组队、准入 NPC 流程尚未迁移。挑战次数服务已经可调用，但没有伪造一个替代入口，也没有在每次技能召唤时消耗次数。已经进入地图、生成 Boss 后会运行上述控制器。
- 资源审计发现 `8880830/8880831/8880832` 没有 `info/skill` 绑定；当前原始资源和源端客户端目录均没有 `8880837.img/8880842.img`。这些规则有源端代码依据，已经迁移，但缺失绑定／资源的招式不能自动运行；不擅自补写原端配置。
- `8880152` 没有 `info/skill` 属于独立三阶段控制；`8500002` 也无技能绑定，不能将所有无绑定都当成错误。审计会列出事实供核对。
- 现代 `customSkill/*VideoLayer`、`customBoss*` 的视频／屏幕特效目前只发出带原路径的 `BossEffect` 日志，没有视频解码与播放。怪物自身技能动作走原 WZ 动画。不能把服务端伤害已经运行等同于完整视觉还原。
- 黑暗状态已记录，命中率表现仍属于尚未证实的客户端计算；异常状态图标／毒雾图像也未冒充已还原。
- 全量 NPC／任务脚本解释器仍有既有语法／Java 互操作缺口，本次没有修改脚本或解释器来绕过它们。

## 验证

```sh
rtk dotnet run --project tools/boss-check -- ../MapleDashResources/Data
rtk dotnet run --project tools/server-check
rtk dotnet build tools/unity-compile-check/Check.csproj -t:Rebuild
```

Boss 定向检查覆盖原 Java 测试中的关键冷却／血量分档边界，以及 MP 不足、封印不扣 MP、精确 HP、死亡取消延迟伤害、清图取消已到期任务、成长记录去重和重开库、每日次数和跨日重置。另读取 18 种 Boss 原始 IMG 清单，核验现存 16 种中的 53 条技能引用；两种缺失资源单独报告。

全量脚本检查确实执行，但当前并非全绿：NPC 889 条中 `parseFail=7/runFail=27/javaInterop=70`，打包 1183 条 `parseFail=13`。`Scripting/` 无本次改动，不能把该结果报为 Boss 回归通过或顺带改写源脚本。尚未进行 Unity 场景实战或手机验证。
