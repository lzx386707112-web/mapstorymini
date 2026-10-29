# 技能视觉节点与公共施放解析

核实日期：2026-10-29。本节为原 IMG / 原服务端源码事实与 Unity 迁移记录；没有新增 EXE 地址或宣称完整技能状态机已逆向。

## 身份与复现

- `Skill/000.img` SHA256 `0c3f6dafa83540139a3a27deb5afb6350ac78432eb86f0aebb8e8e6c7f21604f`。
- 全量源文件身份见 `tools/native_skill_nodes.json` 的 `sources`。该文件由 `tools/audit_skill_nodes.py` 用独立 wzpy 读取原 `clien/Data/Skill/*.img` 生成，不由运行时 C# 解析器生成预期结果。
- 复现：项目根目录执行 `rtk proxy python3 tools/audit_skill_nodes.py`；再用有图形设备的 Unity batchmode 执行 `-executeMethod NativeSkillNodeChecks.Run`，不可用 `-nographics` 代替像素绘制验证。
- 资源分支全量清单及调度边界见 `docs/skill-node-audit.md`。旧20技能的已核实专项调度仍见 `docs/skill-vfx-audit.md`；原 Map/Effect 死亡断层补充层继续保留。

## 已核实资源事实

1. 蜗牛投掷术是 `Skill/000.img/skill/0001000`（ID=1000，节点名七位），不是常量文件中易混淆的1001。应以本版 IMG 与中文 SkillBook 数据为准。
2. 它的根节点没有 `effect`、`ball`、`hit` 或 `action`。三个等级分别有 `level/N/ball`（3帧）和 `level/N/hit/0`（6帧）。只查根节点的 `/effect` 必然漏掉整个投掷表现；只取消20技能白名单、仍只查根节点也不能修复。
3. 各等级 `ball/0` 为32×35、origin=(17,17)、delay=90ms；`hit/0/0` 为76×79、origin=(37,70)、delay=90ms。使用原尺寸、逐帧原点、delay、a0/a1与原RGBA；不能将图标或整棵 level 误当帧。
4. 等级字段 `fixdamage` 分别10/25/40，`mpCon` 分别3/5/7，无 `damage` 百分比。源 `StatEffect.java:488` 读取 `fixdamage`（缺省−1）；`AbstractDealDamageHandler.java:603..606` 校验非0伤害必须符合固定值，`976..978` 用固定值代替伤害上界。本轮接入基础固定伤害；`use_ultra_three_snails` 的蜗牛壳消耗/等级倍乘另有服务端条件，未在本轮实现或宣称通过。
5. 1311001、1311002 的动作在 `level/N/action`，是单个字符串，不是根 `action/0..N` 候选列表：等级1为 `burster1`，等级30为 `burster2`。公共动作解析需同时处理实际等级和 scalar/list 两种形态。

## 公共管线修复

- `SkillDef` 将已知专项调度标识 `art` 与实际视觉身份 `vfx=id@level`、动作身份 `action=id@level` 分开。ID施放不再因缺少旧20技能别名而跳过 `BeginVisuals` / `ProjectileCast`。
- `NativeSkillVfx.Key` 按原节点动态枚举分支，递归解析嵌套容器与 UOL；仅数字 Canvas 构成帧序列。icon、action、整棵 level 不作为动画。实际等级分支存在时选该分支，否则使用根分支；缓存按 ID+等级区分，避免等级/颜色串用。
- 原专项资源中不在 Skill IMG 的补充层仍合并保留；缓存先经裸ID或专项别名访问都不能改变死亡断层 Map/Effect 层。
- `SkillVisuals` 所有原有 prepare/effect/ball/hit/tile/special 等调用均使用同一个等级视觉身份；命中与回放不再另拼没有补零/不含等级的路径。`NativeSkillReplay.PlayNativeReplayEffect` 同样复用解析器。
- `WzCharacter.Pose` 接受 `id@level`，动作来自等级节点或根节点，支持字符串或候选列表。缺少原动作的蜗牛投掷术仍沿用已有普攻回退；这不是已核实的原EXE投掷动作结论。
- `NativeSkillDefinition.FixedDamage` 和 `CombatRules.Attack` 接入源端固定伤害字段，避免按缺失百分比字段计算。命中率等其余本地战斗策略边界仍以 `CombatRules` 注释为准。

## 验证与边界

全量节点检查实际PASS：802个技能、2119个等级样本、5749段动画、61464条帧引用，与独立wzpy的预期帧数和引用Canvas一致。样本覆盖首尾等级以及每个自带视觉节点的等级；共享根资源的中间等级不重复抽样。

蜗牛投掷术等级1/2/3已实际运行通过：ID施放、正确level路径、独立摄像机可见像素、空放弹道移动、有目标的等级命中特效、10/25/40伤害、取消清理。截图 `docs/skill-regression/snail-level-{1,2,3}.png` 为真实Unity摄像机绘制。测试仅使用内存角色，不登录、不写DB/角色存档。

全量解析通过不等于802个技能的完整游戏状态已还原。`keydown/end/finish`、召唤、门、CharLevel、武器残影、effect1/2/3等条件分支已列出并可解析；不能在施法时无条件同时播放以冒充覆盖。已有通用缺省弹速800px/s、射程600px、无ball.delay时动作中点出弹仍是已记载的移植参数。Android最终绘制、多指虚拟键及所有特殊技能原EXE时序均未据本轮宣称验收。


## 2026-10-29：按住/蓄力原端执行链（已确认，尚未完整接运行时）

二进制沿用 BeiDou.exe SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，base0x400000。
`4FB08F` 是key-down技能分类器，常量16项（十六进制）：
111ae9、12a189、142829、205d29、21e3c9、236a69、2f9f6c、312609、4dd5cc、4f5c6a、4faa8c、c80eda、d7511e、e66c4b、1518a28、151ff59。
`500971` 对非key-down返回0；3121004/5221004/13111002返回2000ms，其余13项1000ms。
**该值是松键时计算的elapsed上限，不能据此做自动松键或持续技能寿命。**
`95BEDF` 结束时清user+3154/+3134/+3138/+313C，取now−user+312C，先最少30ms再钳到500971。
三种持续技能走93640B(action6)→96D873；BigBang/Evan走95571F(elapsed)，Piercing3211001走950921准入→9537D5(elapsed)，
投掷蓄力经4ACD7B后虚表+50。末尾A61040清user+2AE8的12字节。
`94BA6A..94BC2C` 持续射击：3121004/13111002以user+3144、5221004以+3148计时，严格大于100ms才进入射击。
7616F6取技能/等级；949B5D资源准入（Hurricane特定TemporaryStat+2B8可跳过）；MP比较技能等级对象+A0；
94BE52包含57502E/94BE7D/94BE9C状态准入；950921后9537D5攻击；准入失败经95BEDF收尾，成功更新时间字段。
复现：`tools/inspect_beidou_client.py <BeiDou.exe> --fn 0x4FB08F`，500971、95BEDF、94BA6A同法；
区间筛选用 `--start/--end`，不可用线性sweep误判分支。
原服 `AbstractDealDamageHandler:781` 的蓄力值读取名单包括Evan两种Breath、三法BigBang、Grenade、两种Corkscrew、PoisonBomb。

旧迁移边界（已由下面的持续技能接线更新）：此前QuickSlots只处理Pressed，SkillSystem单次BeginCast。
开始动作、keydown/end/finish的条件、每次射击的权威资源扣除及蓄力值影响仍须沿上述调用继续核对。
不能无条件播放全部keydown/end帧，也不能循环调用TryCastSkill冒充原持续攻击。


## 2026-10-29：三种持续射击运行时接线

QuickSlots→TryCastBoundSkill→SkillHeldInput 保存按键，3121004/5221004/13111002 起手只播放一次动作与prepare/keydown，
不扣攻击费；Update严格大于100ms逐次调用共用BeginCast成本校验/扣除与立即弹道。没有补帧连发。
原服 SkillEffectHandler.java:47–82 起手仅广播，AbstractDealDamageHandler.java:210–235 每次攻击applyTo扣费。
松键/换绑定/输入锁/死亡/离场与资源不足结束，清理循环特效，松键播放keydownend。
NativeHeldSkill 提供16ID分类与松键elapsed夹值；其余13种蓄力的松键攻击、伤害换算仍未接入。
角色动作目前只播放一次后回基础动作，尚未还原原端完整持键动作帧；不能称完整蓄力系统。
此外速射回归暴露武器表仅导出新手村商店、无枪：WeaponDefs.TryGet补读EquipCatalog原IMG及String名称，
分类/倍率沿用tools/export_combat_data.py的TYPES，不把未导出的枪回退成剑。
当前测试过程及最终结果见 docs/gameplay-node-audit.md 本轮补记；Android多指输入未验收。

最终持续射击三ID运行时断言通过：NativeBuffChecks.RunAll，
/tmp/mapledash-priority-four-runtime2.log 退出0；编译0 warning/0 error。
