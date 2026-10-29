# 右上角临时状态栏（迁移进行中）

## 二进制与复现
BeiDou.exe SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，ImageBase `0x400000`。使用 `tools/inspect_beidou_client.py` 的 `--start/--end` 复现以下函数。源端 `tool/client-debug/weather-compat/src/wvs/tempstat.h` 提供结构旁证；以汇编为准。

## 已确认 EXE 合同
- CTemporaryStatView 列表元素 TEMPORARY_STAT 大小 0x40；+0x1c 类型、+0x20 来源 ID、+0x28 图标层、+0x2c 遮罩层、+0x30 遮罩索引、+0x34 无遮罩标记、+0x38 剩余毫秒、+0x3c 每格毫秒。
- `0x7B2BB0 AdjustPosition`：800×600 客户区，单行 32 像素槽，首槽 x=`797-count*32`，y=23；图标及遮罩按各自宽高居中。索引从左到右递增。没有换行逻辑。
- `0x7B2E58 ShowToolTip` / `0x7B3055 FindIcon`：y 命中区 [23,55)，同一 x 槽公式。类型 1 为物品提示，类型 2 查技能后调用 `0x8F25D0` 技能提示；类型 3、4 为其他描述提示。类型 3/4 不是可任意替代的技能状态。
- `0x7B3176` 构造器设置类型、ID、状态位及图层。`0x7B4439` 将传入总时间以有符号整数除 16，保存为 tLeftUnit，再调用 SetTimeLeft。
- `0x7B44F4`：无遮罩标记时跳过；索引=`clamp(tLeft/tLeftUnit,0,15)`，零 unit 另有分支。字符串池 3594 为 `UI/UIWindow.img/Skill/CoolTime/%d`。复用原技能冷却遮罩，不是自绘扇形或数字倒计时。
- 字符串池 3581 为 `UI/UIWindow.img/TemporaryStatView/%d`；该目录当前只有 Canvas `1`，32×32、origin=(0,0)。它不是所有技能的通用图标。
- `0x7B2829 Update` 每次从 tLeft 减 30 并调用 `0x7B4819 SetTimeLeft`；来源 `0x4FAA8E` 有豁免。图标实际移除依靠状态重置，不能只观察这一函数推断业务到期。
- `0x7B4819` 在剩余时间从 >3000 跨到 <=3000 时为图标与遮罩启动动画；特殊来源另用 unit 阈值。精确动画参数尚待核实，不要虚构闪烁周期。

## 服务端依据
`StatEffect.loadFromData`：overTime 且非 summon 时追加非零 WATK/WDEF/MATK/MDEF/ACC/AVOID/SPEED/JUMP/EXP_BUFF；技能 switch 追加专用 BuffStat（包括合法零值 SHADOW_CLAW）。怪物减益与玩家增益必须区分，不能按导出器 buff 类型猜测。
`Character.registerEffect`：按 BuffStat 选生效来源，use_buff_most_significant=true 时选较强数值，相等再比状态数量；保留被压制来源，取消后恢复次优。SQL 默认 true。ultra_nimble_feet、ultra_recovery 默认 false。

## 补查的确定分支
- `0x7B396C` 类型 2：读取技能对象 +0xAC 图标。类型 1 查询物品图标；`0x7B3F79..0x7B3FD5` 读取物品 info/noShadow（字符串池 0xFC5），非零时禁止绘制时间遮罩。
- `0x76662D`：5211006、5220011、22151002 三个来源不进入状态栏，不能以“所有有状态的技能都画图标”替代。
- `0xA202BE` GIVE_BUFF 路径：负来源是物品，存在技能数据的正来源是技能；未知正来源不创建技能图标。疾病由 PacketCreator.writeMobSkillId 写两个 short（类型/等级），不能任意借技能图标。没有依据把 TemporaryStatView/1 当作疾病通用图标。
- 类型 3 的调用 `0xA38DBB` 明确传 (type=3,id=1,time=INT_MAX)，描述字符串是 Guild Quest 进入提示；它不是辅助技能。类型 4 `0xA35052` 为 Family 权利状态，图标字符串池 0x11E2=`UI/UIWindow.img/Family/RightIcon/%d`；不可与技能/疾病混用。
- `0x7B24D5` 根据状态位交集删除无剩余位条目，类型 3/4 根据类型+ID处理；类型 3 从列表头插入，其他从尾插入。
- `0x9504DD..0x950522` 查槽后，类型 1 对负物品来源取消；类型 2 调 0x96D873，排除 1311006，5101007 另查能量状态。CancelBuffHandler 默认按技能来源取消整个 effect。
- 图标与遮罩初始层颜色 D2FFFFFF；`0x7B4199..0x7B4224` 插入画布 delay=500、alpha0=210、alpha1=64；`0x7B4819` 最后 3000ms 开始重复动画。16 个 CoolTime Canvas 都是 32×32。

## 运行时迁移
- `NativeBuffs` 保存来源、等级、状态键、总时长、剩余时间，来源刷新不重复；共享状态取较强（相等比状态数量）、保留次优并在取消/到期后恢复；非共享状态替换旧来源。读取 game_config 的优先级、监控上限和永久/自定义时长配置；默认与 SQL 一致。
- `tools/export_buff_contract.py` 从源 Java 常量、SkillFactory 与 StatEffect switch 提取 172 个专用映射、212 个显式分类和 27 个召唤分类，保留 Java fallthrough 和合法零值，生成 NativeBuffContract.json。代码分类取源根节点 skillType/effect/hit/ball/action，而非现有导出器 type 猜测；施加还受原 overTime/summon 门控。
- `TemporaryStatView` 接 GameHUD：原图、单行32槽、右边3、y23、16级原遮罩、临期闪烁、按实际生效等级显示技能提示、右键取消。移动端复用 Pointer 已有双指右键，不另造按钮。
- 成功施放注册状态；死亡/复活清除、驱散遵循技能来源过滤；跟随召唤绑定同一状态时钟并在换图恢复，固定召唤取消。普通定时属性药水接同一注册/互斥路径，使用物品图标、提示、info/noShadow 和炼金术时长；仍不应把未支持特殊物品当普通药水消耗。
- 已接入 WATK/WDEF/MATK/MDEF/ACC/AVOID、MAPLE_WARRIOR、HYPERBODYHP/MP 的角色属性刷新；RECOVERY 按服务端 5s（ultra 为2s）周期恢复，INFINITY/CONCENTRATE 影响实际技能耗蓝。

## 验证边界与剩余缺口
`NativeBuffChecks.Run` 首轮实际 PASS：731 个技能/17646等级；3765等级含状态/50键；原图与16张遮罩均可读取，共存/较强优先/刷新/次优恢复/到期/清除/槽坐标通过。最终 `NativeBuffChecks.RunAll` 再次 PASS，含普通药水优先级/取消恢复、非共享状态替换、永久时长溢出；其后真实施放/图标与技能提示/右键取消/恢复周期/血蓝上限/驱散/死亡回归再次 PASS。原蜗牛与4种召唤/血蓝消耗回归也重跑 PASS。细节和截图见 docs/buff-regression/README.md。

这不是“50种状态的全部战斗机制已迁移”：移速/跳跃的特殊分支与骑乘上限、技能动作攻速、隐身/完整伤害反射、元素武器/暴击、经验/掉落修正、骑乘/追踪目标/连击等专用机制仍需各自源端模块验证及接线。现有技能分类仍有 passive/active 覆盖边界。特殊药水、怪物卡/倍率券/区域生效、Family/Guild Quest 来源没有因本次通用栏接入而自动完成。骑乘/追踪目标/连击的无倒计时状态不可用原 WZ time 的普通计时冒充。Android 实机尚未验证。


## 2026-10-29 伤害防护与疾病免疫补线
- 依据源端 `gms-server/src/main/java/org/gms/net/server/channel/handlers/TakeDamageHandler.java:286–315`：MAGIC_GUARD 在 mpattack=0 时优先于 MESOGUARD；MP 损失为截断的 damage×百分比，不足 MP 的差额转 HP。MESOGUARD 的 `Math.round(damage / 2)` 两个操作数均为 int，实际先整数除法；金币消耗从减半后的伤害再乘百分比截断。不足金币仍减半本次伤害，扣完金币并取消来源；恰好够不取消。伤害显示仍沿用原 incoming 值（魔法盾）或减半值（金钱盾）。
- `Character.java:2823–2840 giveDebuff`：来源 Bishop.HOLY_SHIELD（2321005）阻止疾病施加，但 SEDUCE/STUN 除外；`Disease.java`→`MobSkillType` 对应本地 ID 128/123。只拦截新施加，不清除已有疾病。
- 运行时：`NativeBuffs.GuardLoss` 与 `PlayerCtrl.TakeDamage` 接入普通受伤；取消通过状态赢家来源，保持图标同步。`NativeMobDebuffs.Apply` 查实际技能来源。未把此规则套到地图毒伤、自耗血、Boss 固定/百分比伤害；后者需独立核实来源分类及 mpattack。没有修改 UI 资源、坐标或新增绘制。
- 不新增 EXE 地址结论；上面是服务端语义，不代表客户端减伤、反射、隐身、姿态或骑乘已完成。
- 本轮验证：`NativeBuffChecks.RunAll` 退出 0（`/tmp/mapledash-guard-all.log`）；新增魔法盾优先/不足 MP 转 HP、奇数伤害整数减半、金币不足/恰好够用、mpattack 绕过魔法盾断言全部通过。PlayMode 实际注册2321005后封印被拦截、眩晕/诱惑可施加，取消后封印恢复施加；原真实施放和状态栏回归 PASS。独立 C# 编译 0 warnings/errors。尚未 Android 实测。

## 后续补线（数值与施放回归已通过，视觉/完整分支仍有缺口）
- EXE（身份同首节）`0x765066..0x765094`：参数 `(baseSpeed, skillId, signedBuff1, signedBuff2)`，相加两个增益；skillId=4001334 时减2；最后限2..10。普通攻击调用`0x95101C`，前置`0x950FD1..0x951019`读取属性对象+0x204与+0xCC4持有值；其余调用见`0x942AF3/0x953D29/0x96AFF0/0x96D006`。结合StatEffect中BOOSTER/SPEED_INFUSION均取带符号x（快速剑1101004=-2、极速领域5121009=-1/-2），`PlayerCtrl.BeginSwing`已消费两状态，仍使用既有逐帧整数公式。尚不能把此结论扩展成所有技能动作都使用同一攻速分支。
- `Character.dispel:2711`只取消技能来源，保留物品来源与Aran.COMBO_ABILITY=21000000；`use_undispel_holy_shield`开启且有2321005时不取消任何增益。`NativeBuffs.Dispel`替换怪物驱散原先的Clear。死亡、复活仍Clear。
- `Character.prepareDragonBlood:7615`每4000ms直接addHP(-x)，不经过接触防御/魔法盾；取消来源停止任务。Beholder注册分支4407–4455：1320008按x秒addHP(hp)，1320009按x秒applyTo自身；取消召唤同步停止周期。NativeBuffs保存来源的独立下次时间，刷新来源重置任务。恢复绕过ZOMBIFY，与源addHP一致。周期showOwnBuffEffect的客户端视觉尚需另接，不以数值任务代替特效完成。
- `StatEffect.calcMPChange:1549`职业放大先乘x/100再截断扣蓝，再INFINITY清零或CONCENTRATE减耗。对应2110001/2210001/12110001/22150000。`SpecialMoveHandler:127`生命分流5101005先safeAddHP(-maxHP/x)，最低保留1，再将**实际扣血**乘y/100D截断转MP；不再因导出type=passive拒绝。
- `StatEffect.calcHPChange`按maxHP×hp/100恢复；CHAKRA4211001为`[int(LUK*2.3*y/100),int(LUK*3.5*y/100)]`区间随机。Dispel与Hero's Will依据`Character.dispelDebuffs/purgeDebuffs:2876`：前者清CURSE/DARKNESS/POISON/SEAL/WEAKEN/SLOW；后者额外清SEDUCE/ZOMBIFY/CONFUSE，仍不清STUN。Hero's Will ID由export_buff_contract.py从源isHerosWill switch导出，避免遗漏职业。Time Leap5121010清其他技能冷却、保留自身。队伍施加/治疗分摊尚待整体链路核验。

- 最新数值验证：`/tmp/mapledash-completion-buffs.log`及`/tmp/mapledash-completion-costs.log`均退出0，静态状态检查和真实施放PlayMode回归PASS；覆盖生命分流入口、Dragon Blood扣血、Beholder恢复、疾病净化与原状态栏。不是周期showOwnBuffEffect视觉验收。技能物品消耗/入口纠正和怪物/地图后续验证见gameplay-node-coverage.md。

- 普通SPEED/JUMP/THRUST、套装移动属性与SLOW覆盖已接入；192组原x86属性getter/限制对照及实际急速/减速/取消回归通过，详见input-and-physics.md。Power Guard/魔法反击、Achilles/Combo Barrier数值接线与明确测试边界见gameplay-node-coverage.md，仍不是全部防护/隐身/姿态机制已完成。

### 远程攻击解除隐身（本批接线，回归待定）
- 原`RangedAttackHandler:1562–1567`在numAttacked>0时，先检查学会NightWalker.VANISH14100005且DARKSIGHT存在、来源非9101004，取消该效果后cancelBuffStats(DARKSIGHT)；否则检查学会WindArcher.WIND_WALK13101006且该状态存在，同样取消。空放不取消，不以伤害>0作为条件。`Character.cancelBuffStats:4016`遍历所有buffEffects抽走该stat，因此不能只取消赢家后让被压制隐身恢复。
- NativeBuffs.OnRangedAttack对成功选择到目标的普通远程武器攻击/非魔法Attack技能接线，保留原条件和else-if顺序。取消赢家整份效果，再从剩余holder只抽走该stat，移除空holder并刷新原状态栏。不将此钩子套给召唤/魔法/净化/拉怪。
- 新增空目标保留、双来源隐身均清除、风影解除断言，验证进行中。一般隐身下能否施放各类攻击仍需EXE入口门控，未借此实现任意隐身攻击权限。

- 补查`CloseRangeDamageHandler:1454–1459`：近战分支无numAttacked>0条件，DARKSIGHT只需VANISH14100005或Rogue.DARK_SIGHT4001003已学之一，且不保留远程的GM来源9101004例外；否则WIND_WALK按已学13101006取消。NativeBuffs.OnCloseAttack与普通近战BeginSwing/非魔法近战Attack接线，空挥亦调用。测试覆盖普通Rogue隐身远程保留而近战解除；不把远程条件误套近战。

验证补充：/tmp/mapledash-completion-hidden-cancel-isolated2.log退出0，包含追加攻击回放接入及取消时点移动后的NativeBuffRenderChecks。使用独立/tmp/mapledash-gap-check工程，未终止其他会话Unity。
