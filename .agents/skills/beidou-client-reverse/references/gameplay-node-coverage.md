# 技能、地图、怪物：节点与执行覆盖

## 2026-10-29：皮奥所在彩虹村箱子漏导出

- `String/Npc.img/10000/name=皮奥`；地图 `1000000` 的 life 包含 NPC 10000。
- 原 `Map/Map/Map0/001000000.img` SHA256 `c79edc6ae6dd6a752d5999383317e745df12386bb5ff6823ebf765603bbeaf6d`：reactor 0/1/2/4/3 均为 2001，依次 `(610,254,90秒)`、`(324,-78,100秒)`、`(1529,132,90秒)`、`(231,253,180秒)`、`(1947,-167,90秒)`；f均为0。坐标为原地图像素，不做脚底修正。
- `Reactor/0002001.img` SHA256 `8982f4bcc290074b150baaf081f8437612f8f920d42289cd48e6d264fcef0705`：state 0→1→2→3→4，事件type=0；站姿64×45、origin=(33,23)，state1/2/3的站姿UOL引用state0；hit帧delay=150ms，state4为1×1透明图。原渲染器直接读取IMG及origin，不新增绘制。
- 缺失原因：`Assets/Resources/Bot/Maps/1.json` 的1000000未包含reactors，`BotNavigationMapLoader`得到空列表，`MapReactorService.Get/Build`无实例，`NativeReactorView.Tick`无物件可画。已增量补入五个原始spawn和2001模板。导出器默认包含该图，地图文件名补齐9位；增量模板导出保留其他地图已有模板。
- 复现：`python3 tools/imgdump.py Map/Map/Map0/001000000.img reactor --depth 3`、`python3 tools/imgdump.py Reactor/0002001.img --depth 5`、`python3 tools/patch_solomapling_reactors.py --maps 1000000`。
- 初次补数据时仅验证五个spawn与原IMG一致、其他地图字段及模板不变；随后已继续补齐普攻、原脚本掉落、权威拾取与Profile同步、任务交付和刷新。新EXE证据、实际验证及通用Reactor边界统一见[任务箱子与Reactor](reactor-boxes.md)，不再沿用初次“普攻/dropItems未接入”的描述。

本记录对应 2026-10-29 全量节点审计，入口为 `docs/gameplay-node-audit.md`。
不是完整玩法迁移声明。已知源端行为、当前代码和缺口分别记录，不能只看 parser PASS。

## 身份与复现

- BeiDou.exe SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，ImageBase `0x400000`。
- 本次没有新增 EXE 地址/调用链结论。技能 Canvas/UOL/动作沿用 `skill-visual-nodes.md`，地图渲染沿用既有模块记录。
- `tools/audit_gameplay_nodes.py` 顺序解析原 `clien/Data`：76 Skill IMG、6,337 Map IMG、2,908 Mob IMG；不保留全部 IMG 的解析缓存，避免枚举时内存堆积。
- `tools/native_gameplay_nodes.json` 保存各文件 SHA256、标准化路径族、每族文件数/样例、802 个技能逐 ID 的根分支与等级字段，以及引用 Java 文件身份。
- 数字路径统一为 `*`，最多 8 层；不展开 UOL，不解码 Canvas。视觉链接正确性由另一个 wzpy fixture 和 Unity 检查负责。含节点不代表值非零，也不代表原服/EXE 消费该节点。

## 确定的源服规则与修复

1. `MapFactory.loadMapFromWz`（Java 166–200）：读取请求地图的 `infoData`，`info/link` 仅替换 `mapData`；规则字段仍来自原 info。例 `106021002` 链接 `106021001`，fieldLimit 分别为 262216/262224。`NativeMapView.LoadMap` 保留请求图规则、链接图地形/VR；当前不新增 EXE 相机结论。
2. 同函数 `createMobInterval` 缺省 5000ms。`910320100` 配 3000ms。`FieldMap.createMobInterval` 经 `FieldDirector.BuildPoints` 传给已有 `MapSpawner/SpawnPoint`，不再固定传 5000。它不是全局 respawn_interval，不能把两者混成一个计时器。
3. 地图 0 合法；`FieldPortal.Warps` 接受 `0 <= tm < 999999999`，节点缺失/默认无目标使用 999999999。触发方式沿用既有 pt 规则，未新增章节兼容。
4. `SkillFactory.loadFromData`（Java 121 起）读技能根 `elemAttr`。`SkillBook.Native` 之前传空元素，现读当前 ID 原 IMG；通过既有 `CombatRules.ElementModifier` 消费。火箭 2101004=`f`，冰冻术 2201004=`i`。此修复不声称既有伤害随机/抗性倍率已获 EXE 验证。
5. `StatEffect.calcHPChange`（Java 1515 起）对 primary 扣 hpCon；`Character.applyHpMpChange`（8925 起）拒绝扣到 HP<=0。当前 30 个技能含 hpCon。`NativeSkillDefinition.HpCost` 取实际等级；`SkillSystem.BeginCast` 在 MP/冷却写入前校验，成功后扣数值，不走受击/免疫/飘字路径。治疗、ZOMBIFY 的完整 HPChange 组合仍未迁移。
6. 召唤视觉/攻击信息必须读自身 `Skill/<job>.img/skill/<D7>/[level/N/]summon`，不能全借 2321020。`NativeSummon` 现直接解码数值帧，保留原 Canvas origin/delay；`SkillSystem.InRange/HitAt` 读自身 `attack1/info/range/{lt,rb}` 与 `mobCount`，等待时长使用自身 `attackAfter`（ms）。没有 attack1/range 的 puppet 不进行虚构攻击。
7. `StatEffect.getSummonMovementType`（1854 起）：STATIONARY=3111002、3211002、13111004、5211001、5220002、2321031。当前固定在召唤点。其余 FOLLOW/CIRCLE_FOLLOW 仍沿用旧本地跟随策略（主人步速、48px 停距），**并未逆向证明 EXE 飞行轨迹**。
8. `LifeFactory.getMonsterStats` 链接攻击继承不等于整份 stats 继承。现 `NativeMobController` 分开自身 root/info 与 `WzAssets.MobArtRoot`；移动能力、技能/死亡动作时长使用与渲染相同的素材根，攻击 info 优先本体、缺少时用链接素材。自身 HP/MP/skill/revive 保持原来源。例 3000002 仅 info（maxHP=800）且链接 3000001；旧控制器在本体找 move/动作会失效。
9. `AnimationMillis` 与既有 `WzAssets.MobClip` 播放合同一致：排序数值帧、解析 UOL、delay 绝对值、缺省 100ms，忽略 info 元数据。不把此默认值作为新增 EXE 结论。

## 不得擅改的原服差异与剩余缺口

- `MobSkill` 对物理/魔法/双反伤发 reflection.x；`AbstractDealDamageHandler` 665–683 却只枚举 145，物理扣 x、魔法扣 y。当前与此处理器一致。143/144 没有对应扣血分支是原服已有差异，不能凭常识修成另一种规则。
- `SummonDamageHandler.calcMaxDamage` 有独立 pad/mad 上界规则；当前召唤攻击仍进入既有本地 CombatRules。不能把“换回自身外观/攻击框”描述为所有召唤伤害公式已对齐。
- 731 个职业树技能的 attack/buff/passive/summon 分类仍使用导出启发式；`prop/time` 不能替代 SkillFactory/StatEffect 的完整 ID 分派。另 71 个源技能不在当前树中。
- `keydown/end/finish/state/effect1..3/afterimage` 只有公共资源解析，不可一股脑作为施法 effect 同播。
- 通用 Buff 已接入面板攻击/防御/命中/回避、血蓝上限、魔法盾/金钱盾、驱散/疾病免疫和部分周期任务；队伍分发、完整被动属性、移速/跳跃与专用事件钩子仍未全接入。详见 temporary-stat-view.md。
- 地图 decHP/protectItem/everlast 与怪物 MP 恢复/超时消失/自毁阈值已接线，具体测试边界见文末。timeLimit/forcedReturn 的调用者、入口脚本/reactor、水中/飞行物理、怪物飞行/仇恨/技能弹道与命中时刻仍有缺口。逐字段统计和证据入口见审计报告。
- 原服本身未发现消费的节点（如当前搜索中的 mob fixedDamage/onlyNormalAttack），先继续追 EXE；不能根据节点名补一个自定义规则。

## 验证入口与边界

`NativeGameplayNodeChecks.Check` 纳入 `NativeSkillNodeChecks.Run`：34 类 summon 的原图解码、每技能攻击数据、元素、链接图规则、非默认刷怪间隔、地图 0 传送门、链接怪素材/自身 HP/时序；隔离 Play 场景另检查蜗牛三等级及召唤可见像素/静止与跟随、HP 消耗事务。

2026-10-29 本次 Unity 退出码 0，以上静态资源检查与隔离 Play 检查通过；802 技能的 2,119 等级 / 5,749 动画 / 61,464 帧引用匹配独立 fixture。C# 编译 0 warning/0 error。摘要日志 `docs/skill-regression/gameplay-checks.txt`，四类召唤截图同目录，已查看。禁止沿用此 PASS 证明后续新改动。不构建或部署 APK；Android 实机、全部 EXE 状态机及全部地图路线不在这些检查的证明范围。

## 后续生命周期补线（待本轮回归完成）
- `MapleMap.applyRemoveAfter:2096`：info/removeAfter>0或存在selfDestruction且hp<0时安排移除；无selfDestruction用removeAfter秒、普通死亡动画1；有则用selfDestruction/removeAfter秒及其action。`killMonster:1589`的chr=null分支只移除对象/广播死亡/释放刷新槽，不发经验、掉落、任务信用，不走killBy/revive。`NativeMobController`与`EnemyUnit.ExpireNative`、`FieldDirector.OnEnemyExpired`分开正常击杀路径。
- `MapleMap.damageMonster:1528`：selfDestruction.hp>=0且伤后剩余HP<=阈值，按其action正常击杀（有伤害者、withDrops=true）。本地保留伤害归属、奖励路径，只提前触发死亡并使用die{action}原动画；没有根据节点名虚构额外爆炸伤害。
- `MapManager.updateMaps:135`→`MapleMap.mobMpRecovery:4375`：每respawn_interval（SQL默认10000ms）给存活怪物恢复其level的MP，封顶maxMP。源Java未读hpRecovery/mpRecovery同名字段，不能凭字段名采用值。当前本地按此补回蓝；hpRecovery仍待EXE恢复调度证据。
- `MapFactory:358–364`地图decHP/protectItem/everlast/forcedReturn/timeLimit读请求图info。`World.runPlayerHpDecreaseSchedule:1753`每map_damage_overtime_interval（默认2500ms）计数，模map_damage_overtime_count（默认2）到0执行`Character.doHurtHp:2934`。穿戴protectItem或`buffMapProtection:1719`生效则免伤，其他直接addHP(-decHP)，不走护盾/接触无敌。Map protection=1对returnMap211000000（排除200082300）或193000000；=2对returnMap230000000或图200082300。没有MAP_PROTECTION时，穿戴flag&COLD(4)可免寒冷。`FieldDirector.TickEnvironment`已接入；三个原保护药品2022001/2022186/2022040进同一物品状态注册。
- `MapleMap.registerItemDrop:1076`：普通掉落到期=currentTime+item_expire_time（默认180000ms），everlast图Long.MAX_VALUE；清理条件严格expires<now。MapItemService增加权威到期清理，由BotRuntime.Drive泵；GroundItem未绑定旧掉落消费相同配置和everlast，不再写死180秒。NoExpire条目仍由调用方管理。BotNavigationMapLoader纠正链接图规则取原info，几何仍跟link。
- timeLimit并非可据名字一律强制倒计时：源服只有特定事件/迷你副本调用者，当前仅保留数据，未擅自增加全地图退出。地图入口脚本/reactor、飞行/水中积分和怪物完整AI仍未完成。

### 已执行回归与后续纠正
- `/tmp/mapledash-completion-buffs.log`：NativeBuffChecks 与 NativeBuffRenderChecks 均 PASS，退出0；包含本轮 Dragon Blood/Beholder 数值周期、生命分流、驱散/净化。
- `/tmp/mapledash-completion-drops.log`：GroundItemChecks PASS，退出0；新增普通/everlast掉落到期与严格边界检查通过。
- `StatEffect:493–494,959–964`：技能等级 itemCon/itemConNo 为主施法者物品消耗。当前树9个技能含此需求；SkillSystem 在其他拒绝条件之后、扣蓝/冷却前检查并通过 Profile.RemoveItem 的权威库存桥扣除。缺物品或库存拒绝不得扣蓝/写冷却。`/tmp/mapledash-completion-costs.log`退出0，真实2311002施法缺石拒绝、补石后成功、重复施法不多扣均通过。弹药/飞镖、moneyCon与特殊充能消耗仍未接入，不能将此项称为完整消耗链。
- MP Recovery5101005 的导出被动分类仍会被 SkillCast/SkillWindow/VirtualControls 的入口拒绝；现 SkillData.IsPassive 对已核实 instant-self ID 统一纠正，技能窗门控复用该属性。实际经 SkillCast.TryCast 施放回归通过。未新增UI绘制/坐标或布局。
- 环境装备模型纠正：本地 InventoryDao.Equipped=1，与Equip同值，负position区分穿戴；不能按源服枚举-1判本地DTO。FieldDirector以类型1且position<0检查，防止背包内防护装备生效。新增断言/实际怪物生命周期回归进行中。
- 怪物超时消失改为控制器持有的 FieldDirector 释放，且先保存action，让释放刷新槽时的动画时长与实际die{action}一致，不再借GameLoop当前地图或默认die1。

### 命中后数值钩子（本批新增，待定向回归）
- 原 `AbstractDealDamageHandler:405–439` 对目标行伤害先做免疫过滤，再将本目标伤害饱和累加到攻击累计totDamage。吸血四技能4101005/5111004/15111001/14101006使用**截至当前目标的累计伤害**乘x/100截断，再限制怪物maxHP、玩家maxHP整数半值，直接addHP。不受治疗技能/僵尸逻辑影响。SkillSystem.HitAt复用EnemyUnit.TakeReplayHit返回的过滤后总伤害，不按剩余血量截断吸血基数。未擅自将此公式套给Aran.COMBO_DRAIN等不同机制。
- 原 `MagicDamageHandler:833–840` 在本次魔法攻击后，使用`(job-job%10)*10000`选MP Eater，逐个仍在地图的怪物调用StatEffect.applyPassive；后者只有2100000/2200000/2300000，按prop概率、非Boss、maxMP*x/100截断与当前MP取较小值，扣怪MP并addMP。NativeMobController.AbsorbMp与普通魔法命中路径接线；已死亡对象不吸蓝，召唤不触发。原showOwnBuffEffect视觉未在此批移植，延迟追加魔法攻击的完整钩子还需核验。
- selfDestruction.action在LifeFactory:548显式转Java byte；本地现用sbyte，避免把高位值误作正数动作编号。
- `/tmp/mapledash-completion-drain.log`退出0：环境装备门控、怪物实际回蓝、阈值自毁动作、拥有地图释放/无经验、吸血四ID数值上限/截断与既有状态栏施放回归通过。新增实际HitAt吸血和Boss不可吸MP测试仍待下一日志，不沿用上次PASS。

- `/tmp/mapledash-completion-hit-hooks3.log`退出0，实际HitAt吸血回血、Boss不吸MP、怪物生命周期及已有状态栏回归PASS。此前hit-hooks日志因Editor跨程序集调用internal方法编译失败、hit-hooks2因禁用EnemyUnit使其退出All导致无目标失败，均已修正测试装置；不将失败日志当PASS。

### 受伤结算补线（本批新增，回归进行中）
- 源TakeDamageHandler:238–280：damagefrom=-1（接触）且POWERGUARD存在时，反伤=int(damage*percent/(boss?200:100))，限制monster.maxHP/10，先从玩家伤害扣除，再给怪物结算该量。非接触攻击的MANA_REFLECTION在attackInfo存在、非Boss且职业212/222/232及buff来源匹配(job*10000+1002)时，按技能prop抽取，反伤damage*x/100且限maxHP/5，不扣原受伤值。当前以EnemyUnit.TakeReplayDot结算，复用正常怪物死亡/贡献/掉落，不重新掷怪物免疫。源showOwnBuffEffect(id,5)视觉未在此补齐。
- 接着COMBO_BARRIER（仅有attacker时）与Achilles（1120004/1220005/1320005，非-3/-4伤害）顺序乘x/1000D，各步Java复合赋值截断；HIGH_DEFENSE21120004沿用源Math.ceil(x/1000D)，不得自行修成百分比减伤。本地NativeBuffs.PassiveIncoming保存原顺序。此路径仅普通TakeDamage，既有Boss固定/百分比与地图直扣仍不套用，类别完整对齐仍待后续证据。
- NativeMobController普通攻击将mpBurn传入PlayerCtrl.TakeDamage的mpAttack，参与MAGIC_GUARD仅在mpattack=0时生效的原条件；原先在受伤后统一扣MP会错误先吃魔法盾。固定/百分比/致命攻击仍保留独立路径，未借此声称其全链路已还原。
- `/tmp/mapledash-completion-mana-oracle.log`退出0：192组EXE属性对照、实际吸血/MP Eater转移和空怪MP、Boss免疫、怪物生命周期、状态栏与技能消耗回归PASS。`/tmp/mapledash-completion-movement-input2.log`退出0：192组EXE属性对照及原组合输入/普攻时序/下跳检查PASS。THRUST speed字段纠正已编译于后者，但其实际角色新断言尚待reflection日志。

- `/tmp/mapledash-completion-reflection2.log`退出0，192组原移动属性oracle、THRUST实际speed字段、真实接触POWERGUARD反伤、静态Boss半倍率/maxHP上限与防御顺序截断、实际MP Eater及既有状态/生命周期回归PASS。reflection首日志因上一Unity实例尚持有项目锁未执行检查，不是代码PASS/FAIL。MANA_REFLECTION实际触发仍待专项测试；没有Android实测。

### 治愈术施放入口纠正
- `StatEffect.applyTo:952`、`calcHPChange:1490–1530`、`isHeal:1644`确定2301002是主动治疗，单人时按maxHP*等级hp/100f截断。原导出type=passive不能作为拒绝施放依据。现纳入既有瞬时自身技能入口，复用原技能动作、资源特效、mpCon与Healing的ZOMBIFY反向治疗。
- 此项只补齐本机施法者治疗与绑定入口；队伍affectedPlayers分摊、对不死系的独立攻击动作/伤害公式尚未补齐，不能将通用物理攻击作为替代。新增实际SkillCast入口断言（lv1、maxHP100、HP80→90、MP100→88），`/tmp/mapledash-completion-heal.log`中NativeBuffChecks/NativeBuffRenderChecks PASS，进程退出0。

### 奖励状态消费补线（回归待定）
- 源`Monster.getStatusExpMultiplier:694–715`：HOLY_SYMBOL默认非共享队伍按percent/500，配置use_full_holy_symbol或存在共享成员时按percent/100；默认配置见V1.7.0__create_game_config.sql:147=false。`giveExpToCharacter:730–768`先乘状态与经验倍率，再对personal加EXP_INCREASE、存在EXP_BUFF乘2，最后Math.round；party奖金不套personal的平加/翻倍。当前FieldDirector单人击杀已消费这三项；假人贡献/组队路径现将状态参数送入BotPartyExperience，在原资格筛选后的共享人数决定祈祷全额/五分之一；个人与队伍奖金分别取整，平加/翻倍仅作用个人。此处仍缺怪物SHOWDOWN状态与完整人物等级经验倍率，不将本项写为全体完成。
- 源`MapleMap.dropFromMonster:754–761`：有MESOUP时先quantity*value/100D经NumberTool.doubleToInt截断，再乘float MesoRate经floatToInt截断；<=0设Integer.MAX_VALUE。NumberTool:36–45只对正溢出饱和，不是Math.round。RewardRules.MesoAmount与地图掉落接线保留此顺序；未虚构技能效果。
- `/tmp/mapledash-completion-all-skills2.log`退出0：802技能、2119等级、5749片段、61464帧引用检查及蜗牛三级实际施放/飞行/固定伤害、四类召唤表现/驻留/HP消耗事务PASS。它验证资源与列明路径，不证明全部技能玩法。

- 奖励验证：server-check退出0，538通过/0失败，含六人祈祷个人414/队伍60断言；`/tmp/mapledash-completion-reward-buffs3.log`退出0，6项MESOUP截断顺序、浮点倍率截断/饱和、单人与全额祈祷边界PASS。综合GameplayChecks.Run在旧NativeSummon/libra导出素材origin检查失败，未将其记为整体PASS。
- `RangedAttackHandler:1415–1428`确定金钱攻击4111004在moneyCon上加nextInt(moneyCon/2)，超过现有金币则扣到0，不因金币不足拒绝。当前SkillSystem成功施放后接入此消耗，不在拒绝施放时扣钱；实际足额/不足额回归待`mapledash-completion-shadow-meso.log`。该技能独立伤害公式及客户端最低金币门槛仍需核对，不借此声明完整。

### 怪物复活/刷怪点死亡计时来源纠正
- `LifeFactory:567–575`逐个原monsterData（不跟info/link）动作的全部子项delay缺省0相加，保留符号，登记AnimationTime；`Monster.killBy:866`复活子怪延迟以及`Monster:950`死亡监听均固定请求die1，并不随客户端die2/自毁动作选择变化。
- NativeMobController.ServerDeathMillis读取自身die1并复用ServerMillis；复活调度及FieldDirector刷怪点OnKilled改消费该值。视觉死亡动作继续按die{action}播放，两类计时不能混用。linked-only3000002服务端die1为0，不借3000001素材时长。新增signed-delay与链接怪0时长断言；`/tmp/mapledash-completion-death-timing.log`退出0，全节点、全技能帧引用与实际蜗牛/召唤回归PASS。
- `/tmp/mapledash-completion-shadow-meso.log`退出0，实际金钱攻击随机扣款范围/余额不足扣到0、治愈术主动入口，以及既有状态栏/移动oracle/反伤/吸蓝回归PASS。

### 地图timeMob公告
- MapFactory:205–208只读取timeMob/id与message，不消费startHour/endHour。Monster.killBy:801–805在revives包含该ID时立即发serverNotice(6,message)，随后才按die1延迟生成复活怪；不能把节点名推断为小时刷怪限制。FieldMap/NativeMapView保留请求图的timeMob字段，NativeMobController.OnKilled复用既有NativeSystemMessage.DropMap(6)，不新增UI皮肤/布局。
- 原IMG `Map/Map/Map2/211010000.img/info/timeMob`：id6090001、startHour22、endHour14、message为雪山魔女原文；103000105/103000202为id5090000与Shade原文。`/tmp/mapledash-completion-time-mob.log`退出0，实际地图解析保留id与原文断言及既有节点检查PASS；尚未真实击杀母怪观察公告。

### 无形标与单槽位消耗
- `StatEffect.isShadowClaw:1756`只指NightLord.SHADOW_STARS4121006；`applyTo:1022–1044`按USE栏正槽位递增选择首个isThrowingStar且本组quantity>=bulletConsume的物品，扣该槽；ItemConstants:96的飞镖判据为itemId/10000==207。不能将多组同ID总数合并作为单组门槛。
- SkillSystem对4121006读取原等级bulletConsume，在线选择真实InventoryEntry最小正position，Profile/NativeInventoryService新增指定sourcePosition负数量消耗与单组验证，保留失败不变、精确槽位及归零删除；不消费其他组凑数。离线测试档案只有按ID合并的itemIds/itemCounts，只验证单组近似，不声称具备原端多个同ID槽位语义。
- 施放前无足够飞镖不扣MP/冷却；成功后扣原数量再注册既有SHADOW_CLAW状态。普通远程攻击的弹药门控/每次消耗仍待单独迁移，不能只凭初始消耗声称完成。实际无飞镖拒绝/足量扣除与服务端双组隔离断言回归进行中。

- 单槽位消耗回归：server-check退出0，540通过/0失败，含同ID双组不足拒绝与仅扣指定组；unity-compile-check Rebuild退出0、0warning/0error。shadow-stars两次Unity启动均因外部PreviewShot占用项目退出134，不是施放检查PASS；不得清理他人锁文件或关闭他人预览。后续实际施放回归仍待可用Unity实例。

- `/tmp/mapledash-completion-shadow-stars4.log`退出0：无形标无飞镖拒绝/原200枚消耗、治愈术主动回血耗蓝、金钱攻击足额/不足额消耗及既有状态栏/移动/反伤回归PASS。shadow-stars3在无形标断言后因测试未在取消buff刷新后重置血量上限导致治愈术断言失败，装置已纠正，不计为PASS。

### 2026-10-29 受击状态续补

STANCE接入原EXE概率/fieldLimit bit1限制，成功保留伤害与免疫但跳过普通冲量；INVINCIBLE接入物理防御后、整数化前百分比减伤，魔法分支不参与。原x86 oracle分别84/20组，详细地址、真实回归结果及未覆盖特殊冲量/成功反馈动画见character-reactions-and-damage.md。基础受伤随机公式仍未全部还原，不标记整模块完成。


### 2026-10-29 远程弹药、源技能数据与怪物驱散续补

原服依据：RangedAttackHandler:1394–1502，StatEffect:490–491/1022–1044，ItemConstants:96–126，InventoryManipulator:412–445，Inventory.removeItem:318–329，ItemInformationProvider:357–405。普通攻击bulletCount缺省1；等级bulletCount转short，影分身对非NightWalker/WindArcher V/VI翻倍。按USE正槽递增找单组quantity>=count、武器/等级兼容的弹药；207飞镖/233子弹/2060弓箭/2061弩箭，1472063魔法手套用两类箭。2070007/2070018需70级、2070016需50级；2331000/2332000需70级，普通子弹level>(id%10)*20+9。

选中弹药后SOULARROW或SHADOW_CLAW不扣；只有SOULARROW能独立绕过“没选到弹药”的攻击准入。影分身、bulletConsume覆盖的Java byte截断、现代技能单枚消耗及原明确豁免ID逐项照搬，未将所有现代子技能一律豁免。BALANCED_FURY2070018在扫描时quantity-bulletCount<=10会设置负消耗为slotMax，Inventory.removeItem实际是加slotMax，**不是设到slotMax**；低于选取门槛仍不准入。源Inventory先short转换再负数归零，飞镖/子弹consume=true保留零槽，箭耗尽删槽。

NativeProjectiles保存选择/费用规则，SkillSystem在MP/冷却前校验，PlayerCtrl普通空放/命中普攻均在动作入口扣一次；Profile.ConsumeProjectile/NativeInventoryService.projectile精确更新指定槽，并保留可充弹药零槽。无形标初始消耗也改走此保留空槽路径。离线Profile仍按ID合并，没有原多组槽位，只有在线GameSession inventory具有完整单槽语义；此边界没有隐藏。此次未补普通远程弹道和弹药攻击力、影分身伤害公式，不能把费用对齐描述为所有远程攻击已完成。

验证：NativeProjectileChecks原服规则断言PASS（/tmp/mapledash-completion-projectiles.log退出0）；server-check543通过/0失败，新增飞镖空槽、末箭删除和错误槽位拒绝。/tmp/mapledash-completion-projectile-casts.log退出0，真实SkillCast二连射缺箭不扣MP、成功扣2、单枚不足拒绝、普通空放扣末箭、SOULARROW无箭施放，以及既有技能/状态回归通过。首编译因Math.Max short/int重载歧义失败，显式转int修复后通过，不把失败当PASS。

职业树漏导出的71个技能主要是800/900/910、2000/2100/2110/2111/2112原文件技能，并非资源缺失。SkillBook.Get在JobTree查不到时直接读取Skill/<jobD3>.img/skill/<D7>/level，保留scalar、Vector、等级上限及String/Skill原名称、原icon URI，不新增技能窗列表或推断新布局。802技能/2119等级fixture均可读取，防守策略21120004 lv1 x995与原名、21121003及20000012检查通过（/tmp/mapledash-completion-source-skills.log退出0）。**fallback沿用既有分类启发式，不表示未迁移主动技能行为已补齐**；原17个误分类主动技能只针对已实现路径逐项纠正。

StatEffect:762–765/1206–1227，Monster.debuffMob:1463–1495：1111007/1211009/1311007分别封印技能并移除WDEF/MATK/WATK增益；2311001净化无额外抽概率移除四类攻防增益，玩家疾病解除仍有独立概率。NativeMobController.Dispel清除100/110/150等同状态不同来源组；use_anti_immunity_crash默认false，开启才按原物理/魔法反伤保护移除免疫。SkillBook.SpecialKind纠正三种崩坏的施放入口及Dispel类型，SkillSystem命中分支执行封印+移除；净化自身操作后遍历原范围与mobCount处理怪物。没有新增自绘特效。/tmp/mapledash-completion-crash-nodes2.log退出0，包含802技能等级数据、原icon可解码、三崩坏/净化四类状态组清除及既有节点检查。首crash-nodes日志因隔离副本未同步其他会话新增的MobAttackArea.Overlaps重载编译失败，已仅同步该依赖后重跑，不改动或终止其他会话。/tmp/mapledash-completion-crash-casts.log退出0，三种崩坏实际SkillCast入口及既有弹药/状态/治愈等回归PASS。后续金钱攻击按AbstractDealDamageHandler.parseDamage:1042–1044在本目标伤害采样前清除双防增益，/tmp/mapledash-completion-meso-dispel.log退出0，五类驱散映射与802技能节点回归PASS。


## 挑衅 SHOWDOWN 奖励状态续补（2026-10-29）

源端身份沿用上文审计的 Java 源码与 EXE 身份，本次没有新增 EXE 地址结论。StatEffect:806–810 将 4121003/4221003 的 x 写入 SHOWDOWN/WDEF/MDEF；**isMonsterBuff:1609–1633 不包含挑衅**，不能从“无 damage 字段、导出为 buff”推断成范围 applyMonsterBuff。RangedAttackHandler:1375–1378 将两挑衅、5121002 ENERGY_ORB、15111006 SPARK 在弹药选择前分流到 applyAttack(...,1)；AbstractDealDamageHandler:636–640 在目标总伤害>0 时按 prop 施加怪物状态。原 JobTree 将挑衅误导为自身 buff，现 SpecialKind 纠正为 Attack；普通 HitAt 成功命中时登记 SHOWDOWN，弹药公共选择补上这四个原分流 ID 的免选取/免消耗。

Monster.applyStatus:1173–1213 拒绝不适用元素及 Boss；两个挑衅没有 elemAttr，当前登记入口拒绝 Boss。Monster:1161–1166/1338–1342 状态寿命是 duration + Skill.animationTime -100ms；SkillFactory:377–381 从技能根 effect 子项的 delay 求和，缺省0，不能用播放帧绝对 delay。NativeMobController.ApplyShowdown 保存一个可覆盖/到期的 SHOWDOWN；覆盖不叠乘。Monster.getStatusExpMultiplier:694–715 先把 HS 的 double 因子复合赋给 float，再乘 SHOWDOWN 的 double 因子并转 float；BotPartyExperience.StatusMultiplier 共享该顺序，单人/队伍个人经验与组队奖励均消费。FieldDirector 的普通击杀经验也消费；MapleMap.dropFromMonster:885–887 的 SHOWDOWN 因子接入掉落概率，不额外放大金币数量。

迁移边界：这是 SHOWDOWN 状态和奖励链路补充，**没有声称挑衅的 WDEF/MDEF 客户端解释、缺 lt/rb 时的原 EXE 选目标/攻击范围、实际动作/投射物以及基础伤害公式已对齐**；现仍沿用通用攻击管线，需继续逆向这些缺口。曾短暂尝试独立范围状态分派，查到 isMonsterBuff 与 RangedAttackHandler 后已移除，不将该临时假设作为确定逻辑。实际验证结果另附。

地图规则补查线索（本次未改运行时）：MapFactory:362/364从原info读forcedReturn缺省999999999和timeLimit缺省-1；Character:7094–7102及8362–8369在保存角色时优先写forcedReturn，否则死亡写returnMap，否则写当前map。不能把forcedReturn推断成进入即传送或任意timeLimit到期跳图。timeLimit还由Channel创建MiniDungeon以及SystemRescue:68–72救援流程消费；一般地图计时执行、存档返回桥接仍需逐入口核对。

验证：/tmp/mapledash-completion-taunt-final.log退出0，覆盖802技能数据、两挑衅Attack分类、Boss拒绝、错误ID拒绝、重复覆盖/过期、HS再乘SHOWDOWN的float顺序及既有节点回归。/tmp/mapledash-completion-taunt-hit.log退出0，包含两挑衅真实SkillCast入口、真实HitAt后怪物SHOWDOWN值、四个免弹药ID断言、既有17646等级/3765状态等级/50状态键及HUD/技能回归。测试使用fixdamage构造命中以隔离尚未对齐的原挑衅伤害/选敌问题，不声称原挑衅实际画面或Android实机通过。

服务端回归：tools/server-check 本轮退出0，544通过/0失败；新增六人队伍原个人133/组队40基础值，在HS50+SHOWDOWN50+个人平加7+个人双倍时得到个人614/组队90，验证状态乘区、平加和双倍顺序。


### 2026-10-29：服务端选敌身体框

依据 `LifeFactory.buildMonsterBoundingBox/resolveMonsterVisualData` 与
`AbstractDealDamageHandler.getWorldBbox`：汇总 stand/move/fly/jump/attack/hit 动作Canvas帧，
解析动作/帧UOL，排除die/skill；缺lt/rb时按origin及Canvas宽高推导。合并原始lt/rb后，世界框归一化且宽高最少1px，
按怪物朝向翻转x，own info/noFlip禁止翻转；选视觉源先保留有fly/0、stand/0 Canvas或有效bbox的自己文件，才追info/link，循环检测。
新 `NativeTargetGeometry` 与 `EnemyUnit.SelectionBounds` 接入 `SkillSystem.HitAt`：有明确lt/rb的普通攻击先保留原脚点命中，再检测身体框交叠。
召唤攻击保持自身范围分支；无lt/rb的600px弹道/200px兜底尚未据此还原EXE选敌，不能称完整范围迁移。
验证：`NativeGameplayNodeChecks.Run` 合成帧覆盖UOL/Canvas缺省尺寸、die排除、镜像；
`NativeBuffChecks.RunAll` 实际HitAt窄框只擦身体边缘、脚点在框外时仍选中目标。
原始检查日志 `/tmp/mapledash-priority-range.log` exit0（增加own-visual优先规则前）。


### 2026-10-29：地图进场脚本缺口的原服合同

原服 `MapleMap.addPlayer:2817–2834`：首位入图者启动监视器，只有真实玩家触发onFirstUserEnter；
onUserEnter每次真实玩家入图触发，Bot不触发。`MapScriptManager.runMapScript` 对firstUser先检查
Character.hasEntered(path,mapid)，**执行/加载前**登记enteredScript；脚本缺失也不会本会话重复尝试firstUser。
按mapScriptPath缓存Invocable，JS全局变量跨入图保留，入口为原样 `map/<onFirstUserEnter|onUserEnter>/<name>.js` 的 `start(ms)`。
MapScriptMethods继承AbstractPlayerInteraction，不能套NPC start()/Active对话生命周期。
中文源脚本map目录322份；调用最多为playerMessage/getMapId/getMap/getPlayer/getClient，另有scheduleMapWarp、Karing延时出生、intro、勋章探索等。
当前尚无公共进场脚本调度：FieldDirector的map0自动跳returnMap仅为既存兜底，**不等于**goAdventure演出已迁移。
NpcApi.showEffect/showIntro仍为空、getMap参数尚不解析目标地图，不能直接作为完整map宿主宣称完成。
下一实现须补独立缓存/firstUser门控/地图宿主及事件调度，保持原JS文本，不在C#重写脚本业务。

最终选敌实现（含自身视觉优先link）已随 `/tmp/mapledash-priority-ai-runtime.log` 实际HitAt回归通过，Unity退出0。


## 2026-10-29：原地图进场脚本执行接线

原服 MapScriptManager.start 的脚本缓存与角色已进入集合被移到 NativeMapScriptService：
按path缓存JS全局，firstUser在读取前登记，缺源/异常仍保留登记，缓存调用抛JsRuntimeException时失效并重载重试。
FieldMap/NativeMapView读取请求地图自身info/onUserEnter与onFirstUserEnter；FieldDirector.Enter真实玩家入口调用
NativeMapScripts，使用独立NpcSession.MapContext，不占用NPC对话。原JS通过ScriptSource直接执行start(ms)。
支持当前地图getMap、scheduleMapWarp原延时与源地图检查、音乐/声音，以及既有NpcApi数据方法；
远端地图与尚空的过场/引导API显式抛未迁移错误，禁止静默当成功。
实际 karing_first.js 的410007100→410007120/portal0、提前离图取消与NPC隔离已在
NativeGameplayNodeChecks.CheckMapScripts实测通过（/tmp/mapledash-map-entry-nodes.log退出0）。
这不等于322地图脚本完整迁移：远端场景、过场、事件实例/副本、Karing出生死亡联动、勋章探索仍有缺口。
脚本harness self-test为26通过；全量NPC执行769成功/47运行失败/73 Java互操作（889文件），不得写全量通过。


### 2026-10-29 过场、事件宿主与飞行追击增量

参见[地图过场与事件实例](map-intro-and-events.md)及[怪物技能与攻击](monster-skills.md)末节。
原goAdventure/goLith type0/2/5已执行；事件实例生命周期已接，但运行时私有map/clockUI/怪物与掉线回调仍缺。
firstAttack消费与整数距离、目标寿命、飞行追击及540组EXE物理对照已接；空闲区域飞行与完整地面决策未完成。
旧“showIntro空实现/firstAttack未定位/飞行不移动”描述以这两篇新记录为准，不能扩大为全事件/全AI完成。
