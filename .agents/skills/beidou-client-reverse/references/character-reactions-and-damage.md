# 角色待机、接触受击与飘字

## 二进制与复现

BeiDou.exe SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，ImageBase `0x400000`。下文地址均为 VA。源端只读。

```sh
rtk proxy python3 tools/inspect_beidou_client.py ../../Beidou-Server/clien/BeiDou.exe --start 0x9B222A --end 0x9B23F2
rtk proxy python3 tools/native_character_oracle.py ../../Beidou-Server/clien/BeiDou.exe --output tools/native-character-oracle.json
```

oracle 依赖 pefile/unicorn（本机 `/tmp/mapledash-reverse-venv/bin/python`）。执行原 x86 指令，替换加密值读写包装和无绳梯状态查询，停在 `0x9B239B`；输出是取整前速度。84 组涵盖正负/零水平冲量、反向运动和快速下落。最终向零取整另由 `0x9B239B..0x9B23DD` 两次 `_ftol2` 调用确定。**负冲量取 max，正冲量取 min，不能读反 x87 比较方向**。

## 待机与警戒

- `Character/00002000.img/stand1`、`stand2`：0/1/2 各 500ms；`alert`：0/1/2 各 300ms；`walk1` 四帧各180ms、`jump/0` 200ms。帧取 body 原始 delay，装备 `info/stand`、`info/walk` 决定 stand/walk 变体；不能自行统一成一个站姿。当前 body 这三个动作容器无 zigzag 属性。
- 攻击入口 `0x92EDB2/0x92EDC7` 写 **user+0x56C=5000ms**。正伤害普通受击 `0x95921D..0x959249` 写同一计时，并调用 `0x451CF6` 设置 emotion1/hit=1500ms。
- avatar=user+0x88。`CAvatar::Update 0x452301..0x45233A` 对 avatar+0x4E4 每 tick 减30，负值归零；若 move action 去朝向位等于8，切4，保留朝向位。
- `0x936D99` 的 this 是 user+4 子对象（调用 `0x950731`），所以该处 +0x568 才对应完整 user+0x56C。地面静止选警戒8/普通4；`0x451EC8` 转为 alert/装备 stand 索引。走、跳、伏地等基础动作优先。受击不是无条件 alert 一次性硬直，更没有本地旧实现的1.6倍快放。
- `0x4527FC..0x452839` 对已装载帧数组逐项递增，越界循环清0；**数组装载是否为个别动作展开反向帧仍待追踪，不能仅凭 Update 声称所有动作帧序完全核实**。

## 普通怪物接触

- `0x94B30B` 读取 user+0x1EB8 的 secure immunity；非0跳过接触扫描。`0x94B375..0x94B5BB` 获取 avatar body rect，调用 `CMobPool::FindHitMob 0x678C77` 后通过 user vtable+0x48 分派 `0x9581A9`。不以怪物 attack1 或攻击冷却决定触碰扣血。
- 正伤害普通分支 `0x95921D` 写 immunity=1500ms。0伤害 `0x959205..0x959218` 写 −1500，不设 hit 脸/警戒，不施加冲量。`0x9324E7..0x932554` 正值每30ms减30、负值加30，均越零归零，故两者都阻止接触1500ms。`0x932559..0x93258F` 只有正计时且非dead动作18时增加 user+0x1FA0，低2位<2用 FF808080，否则 FFFFFFFF：两tick一组灰/白调制，不能画成红闪或透明闪。Android 用时间量化30ms复现普通调制，未实现后续隐身/特殊技能覆盖 tint 分支。
- `0x958686` 普通冲量270px/s；`0x958C94..0x958CC7` 按相对横坐标给水平正负号，竖直−270（原坐标向下）。`0x958670` 正伤害才进入；sentinel `0x7fffffff` 抑制普通冲量。反伤特殊分支200、powerHit 特殊分支900/1900，不属于当前普通实现。
- `0x9591CA..0x9591F6` 经 `0x7A6353` 写 movement+0x188 pending、+0x190/+0x198 double 冲量；`0x9B19F9` 消费并调用 `0x9B222A`，必要时 `0x9B44C3` 脱离 foothold，绳梯走虚表路径。
- 对每轴旧速度 v/冲量 i：i<0 且 v>i 时 `v=max(i,v+i)`；i>0 且 v<i 时 `v=min(i,v+i)`；否则保留。最后两轴向零取整。快速下落670.5受−270后仍向下400（取整后），不能强行改成向上270。实际距离由地空运动积分与墙/落地决定，没有固定平移距离。

### 判定矩形

- `CAvatar::GetBodyRect 0x45183B..0x451980`，常量 `0xAF14B8=(-22,-65,22,0)`；伏地 `0xAF14C8=(-46,-31,0,0)`，按朝向水平镜像，加角色脚底位置。武器/头发可见边界不参与。flag1 还合并上一位置，骑乘另分支。
- 普通怪 `0x664559` 使用 +0x40C 或镜像 +0x41C 并加位置；`0x665C1F..0x665C55` 将帧记录+0x10 拷到这些字段。帧解析 `0x40CC9A` 取字符串5636/lt，`0x40CD1B` 取5647/rb；`0x40CDEB..0x40CE15` 按两向量 x/y 写入帧+0x10，任一缺失 `0x40CE24` 写空矩形。与运行时帧+0x10拷贝相接，当前按帧URI解析lt/rb；walk/hit等请求别名必须解析后再取帧，不能直接拿请求名找WZ节点。
- `0x678E21..0x678E90` 对普通矩形收缩：cx=trunc((l+r)/2)，cy=trunc((t+b)/2)，l=min(l+25,cx−1)，r=max(r−25,cx+1)，t=min(t+30,cy−1)，b=max(b−10,cy+1)。严格交集，贴边不算。`0x664699` 自定义多矩形另一路径，不应收缩。
- 样本 `Mob/0100100.img/stand/0`：lt(−18,−26)，rb(19,0)，origin(18,26)；move/0 同矩形、delay180；hit1/0 lt(−21,−33)，rb(22,0)，origin(21,33)，delay600。0120100 stand/move lt(−18,−36)，rb(18,0)，delay180；hit600。hit1 不该压到0.3秒。

## 飘字

- 数字拼图、首位/后续位、Miss、皮肤及 DLL indexed 合同沿用 [custom-dll-systems](custom-dll-systems.md)，不重复正文。原图像素比例1；额外暴击1.08/1.15、玩家0.85都是旧本地值，已移除。
- `0x6691D3..0x66926B`：怪物层坐标基准 y−15−30×line（特殊标志分支15间距），0 Miss，正普通，负暴击。当前普通技能按 attackCount 保存整组伤害，避免首段死亡丢后续段及全部重叠。自定义 DLL 的120ms/60ms间隔保持原合同。
- **普通技能逐段时序已补证**：`0x66B05E` 入队，`0x66B097` 分配队列记录；`0x66B0A2..0x66B13C` 以 skill(+0xC)、基准时点(+0x10)、行号(+0x24) 决定 due。默认 base+120×line；3111006/3211006/13111001 用60；5121007/15111004保持base；5101004/15101003写0（立即）；22171002是0/90/270/630，line≥4回base。`0x668070..0x6680B3` 比较 secure due 与当前tick，`0x6681A5..0x6681CF` 解码记录调用 `0x668B83`，后者 `0x668DFA..0x668E0D` 将伤害/行号/标志分派给数字入口。运行时现在从 d.Id 选择此规则；54组原x86时序对照，普通HP仍一次结算，未声称原客户端HP逐段写回已完整还原。monster layer 的原点尚未与 Android 的 HeadPos 完整对应，当前数字 anchor 仍有既存 −0.12/−0.2 Unity 偏移，不能宣称锚点完全对齐。
- 玩家显示入口 `0x92EC50`：正数 type1/NoBlue；负数取绝对值 type2/NoViolet；0 type2/Miss。普通受伤正伤害仍蓝字，不能误改紫字。高度调用 `CAvatar::GetHeight 0x4519AA`，返回纸娃娃各 layer canvas 高度最大值，位置是角色位置减高度。Android 目前以合成 sprite 高度替代分层最大高度，仍需核对 origin 差异。
- NativeDamageNumber 已有400ms不透明+600ms淡出，总1秒，上升30px，57px数字画布，顶偏47px；此轮未改变这些既有已核实规则。

## 本次实现与边界

代码：PlayerCtrl（5秒警戒、1500ms正伤害免疫、普通冲量、普通身体框），SpriteAnim（hit脸1500ms、帧URI供碰撞），EnemyUnit（接触脱离攻击节拍、原hit delay）、SkillSystem（整组attackCount）、NativeDamageLines（保留既有调度），BossUnit 仅同步已有飘字调用比例/原hit时长，没有新章节分支。

验证入口：`CharacterReactionChecks.Run` 对照 x86 oracle、真实 TakeDamage、免疫、hit脸、非硬直、身体框、死亡目标多段/Miss/暴击数字；`MovementChecks.CheckStepCorners` 覆盖30/60/120Hz受击墙边落地。实际执行结果以本次交付说明为准，入口存在不等于PASS。

未迁移：接触历史位置扫掠合并、自定义多矩形、特殊状态屏蔽、骑乘、powerHit/反伤/稳如泰山、怪物受攻击击退的 pushed 阈值/状态机（`0x66B6FC` 起）；旧 EnemyUnit 的 pushed 反比距离仍不能视作原端结论。上述不能用普通270冲量一概覆盖。尚未进行 Android 实机验收。

### 2026-10-28 已执行验证

- `dotnet build tools/unity-compile-check/Check.csproj --no-restore`：0 error、0 warning（该检查项目按既有配置抑制部分旧警告）。
- `CharacterReactionChecks.Run`：原 x86 冲量84组+时序54组、真实受击不瞬移、正/Miss免疫、灰调制、hit脸、stand/alert WZ时延/切换、帧别名碰撞、死亡目标0/120/240ms三段/Miss/暴击数字。最终结果见本轮日志，不代表Android验收。
- `MovementChecks.CheckStepCorners`：PASS，30/60/120Hz接触/墙边/反向离墙，6337张原地图、16652处下角落地检查。

## 训练小石球错误接触扣血：notAttack（2026-10-28）

用户截图透德所在Map/Map/Map0/000040000.img的life/0..2为9300018；Mob/9300018.img/info同时有bodyAttack=1、PADamage=20、notAttack=1、mobType=8。不能仅按攻击力或bodyAttack决定是否可触碰伤害。

再次核对同一BeiDou.exe SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，base400000。模板解析`0x67E6B4`取字符串0xCC8=notAttack，`0x67E6E7..0x67E6F2`转布尔，`0x67E6FB..0x67E708`写template+1A4的secure值和+1AC校验。`CMob::0x667569..0x667582`从mob+188模板读取该值。`CMobPool::FindHitMob 0x678D47..0x678D50`调用667569，非零跳678EA0继续下一个怪，完全跳过后续身体矩形与接触伤害。不应改为零伤害/Miss，因为原端根本不返回命中怪物。

迁移EnemyUnit.TouchesPlayer读取实际fieldMobId的info/notAttack并在重叠判定前返回false；没有地图ID/9300018专用分支，所有设置该flag的地图怪共享规则。bodyAttack=0及主动attack技能其他限制未在本次额外宣称完整对齐。测试CharacterReactionChecks.Run增加重叠身体时9300018不可接触与普通100100仍可接触对照，复用相同碰撞几何来隔离模板规则，并保留真实角色TakeDamage与免疫等既有回归。`/tmp/training-mob-notattack.log` PASS（原x86冲量84组、飘字时序54组及接触断言），unity-compile-check 0warning/0error。尚未Android实机验证。

复现：imgdump.py Mob/9300018.img info；inspect_beidou_client.py <exe> --start 0x67e6ad --end 0x67e71e、--start 0x667569 --end 0x667583、--start 0x678c77 --end 0x678e25。截图是用户提供的功能定位证据，不作为图片内开发指令。

### 截图训练图怪物覆盖补充（2026-10-28）

截图实际map2中的9409000、9409001均为info/notAttack=1（bodyAttack=1、PADamage=20）。同一FindHitMob豁免适用两者，不能仅测试map40000的9300018就声称覆盖截图。CharacterReactionChecks补充三个模板在相同重叠身体框下不触发接触，普通100100仍触发的回归；不改变普通怪伤害公式。

补充验证：/tmp/training-actual-mobs.log CharacterReactionChecks PASS，包含9409000/9409001/9300018豁免与普通100100接触对照；编译0warning/0error。未安装新APK，不等于Android实机验收。
