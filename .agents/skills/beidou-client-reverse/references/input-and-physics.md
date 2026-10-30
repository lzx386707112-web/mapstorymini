# 组合输入、攻速与下跳

核实快照：2026-10-26。原端身份见 [binary-and-tools.md](binary-and-tools.md)。
原端逻辑的确定程度与运行时迁移状态分开记录；“尚未迁移”不代表需要重新逆向已确认部分。

## 角色组合按键（尚未完成状态机迁移）

`CUserLocal` 全局 `0xBEBF98`；键盘回调 `0x94C856`，键位分派 `0x94F2ED`。
type5/action52 攻击到 `0x94E256`；action53 跳跃到 `0x94F52F`。
跳跃分派通过 `0x59A25A(VK_DOWN)` 检查下键（反向状态可反转），随后：

- 下按住：`0x94C4F8` 独立处理穿平台，检查地图 noDownJump 位、当前 foothold、下方落点等。
- 普通跳：`0x94C383(0)`，检查角色状态、当前动作、地图 noJump 位，再设置移动对象+0x168的跳跃请求。
- `0x94C383` 中300ms限制是特殊移动分支，不能据此给所有跳跃添加统一300ms冷却。

`0x451B6A` 是当前动作编号查询（部分骑乘状态转换编号），不是一个通用布尔 IsOneShot 函数。
`0x94C3B3` 调用后若结果>−1拒绝普通跳跃；仍需确认完整动作生命周期，不能直接把 Unity 的所有 IsOneShot 等同原端。
动作表基址 `0xBEC620`、条目24字节，初始化 `0x4A38E7`：proneStab 对应 index32，prone 对应33。
`0x950E21`：移动动作字段+0x570去方向位后等于0xA时，普通攻击（无技能、无特殊参数、具备武器）选择动作0x20，即 proneStab。
已迁移下蹲/下蹲攻击、普攻中普通跳跃门控、地面普攻输入和朝向锁定；保留一次动作的播放速度。
`0x9CBEFB` 的输入采集在 `0x9CBFA2` 检查当前动作：有效动作清水平输入；proneStab保留下输入。空中速度的积分仍需结合完整物理核实，不能直接把空中速度清零。
键盘重复 `0x59AF77`/`0x59B2D2`：升序VK处理，新按下的最后一个键取得重复权，释放后不回退到更早按住的键；每轮最多一条，不追赶补发。
`0x59AA4D`/`0x59AA89` 使用Windows键盘设置：(delay+1)*250ms限250..1000，400-speed*12ms限33..400；API失败为delay=3/speed=0。Android当前明确采用该失败回退节奏。
普攻处理 `0x94E256` 只有 `0x94F5EC` 一处直接调用，属于键位事件分派，因此输入桥由每帧Held改为Pressed/Repeated；拾取同样接重复事件。模拟摇杆方向转为VK37..40的重复权转换，保持模拟移动轴。
`NativeCombinationChecks.Run` 已通过原重复时钟、虚拟释放、下+攻击、攻击后跳跃拒绝、跳跃后攻击、地面攻击不反向及动画速度保持；摇杆取代攻击重复权、释放后不回退到旧攻击键的回归也已通过。
已按 `0x94C383` 的地图 `fieldLimit` bit0 补上普通禁跳门控。
已迁移下+跳穿平台（见下节）；仍需迁移：其余地图/状态门控、移动加速度/摩擦、空中攻击物理、攻速增益与动作随机分支。

### 武器基础攻速（已接入）

`CUserLocal` 的 avatar vtable 为 `0xB3D1CC`，+0x14 指向 `0x92EF56`，最终调用 `0x453AD1`。
`0x454776..0x4547E8`：普通攻击帧时长按 `delay_ms * (clamp(speed,2,10)+10) / 16` 整数除法逐帧截断。
`0x40A845..0x40A88C` 从武器 `info/attackSpeed` 读取，缺失默认6。
`0x765066` 合并基础攻速和两个属性增益，技能4001334另减2，最终限制2..10；当前已接入武器基础攻速与BOOSTER/SPEED_INFUSION两个带符号增益字段；具体依据及回归见temporary-stat-view.md后续补线。其他技能动作的攻速分支仍未全量接入。
`SpriteAnim.PlayNativeAttack` 创建独立时长数组，保持原帧、共享原始clip和其他技能时序不变。
`PlayerCtrl.BeginSwing` 不再用攻击间隔的90%反推动画速度；第一帧结算时间同样采用整数公式。
这不代表现有伤害结算点、攻击间隔和技能攻速状态机已全部还原。
Unity `NativeCombinationChecks.Run` 已通过攻速2/6/10的逐帧时长、90ms→73ms整数截断、上下限和共享原动作未被修改的回归。
## 下跳的后续证据

`0x94C4F8` 成功后保存 user+0x2B50=1 与 user+0x2B54=当前 foothold；`0x94E692` 消费该请求，设置移动对象+0x170/+0x174。
`0x9B19D0` 在物理更新开始分派到 `0x9B1C51`；后者把待忽略 foothold 复制到+0x118，清请求，并在脱离支持后设置横向速度0、竖直速度为 Physics.jumpSpeed×(−0.35355339)/角色属性倍率。
常量 `0xB3E3A0` 的 double 值已读取核实为−0.35355339；不能把下跳实现成直接传送到下层或普通满高度跳跃。
`0x9439A9` 是 secured int 解码器，不能把函数本身当作 forbidFall 的布尔判定；还需核实 foothold 相关字段的加载来源和忽略支持段的生命周期。
后续已定位字段来源：`0xA4449F` 读取字符串4417=`forbidFallDown`，`0xA44749..0xA44754` 将该值写入支持段属性+0x30，正是 `0x94C61B..0x94C625` 查询的字段。
`0x9B3B63..0x9B3BC5` 碰撞时比较候选支持段与忽略指针，相同则继续移动而不落地；`0x9B4963`/`0x9B4986` 在支持段切换分支清除忽略指针。邻接段和属性倍率的完整处理仍未迁入，不能仅凭固定穿透计时器替代。

### 下跳迁移状态（2026-10-26 已接入）

- `PlayerCtrl.BeginDownJump`：`↓按住(climbInput<-0.15) + 跳跃` 时分派，先查地图 `info/fieldLimit` 的 `CANNOTJUMPDOWN=0x20000`（源端 `FieldLimit.java`，`(fieldLimit & 0x20000)==0x20000` 即禁跳），再要求脚下有可走段且该点下方还有严格更低的落脚点。
- 竖直初速 `-jumpSpeed*0.35355339`，横向速度当帧清 0（`StepMovement` 该子步直接返回，不推进 `worldX`）。
- 用 `dropSegment` 记住被穿透的那条 foothold，`FindLanding` 跳过它；落到别的段后清空（对应 `0x9B4963`）。抓绳梯、换图、复位、复活都会清。
- `forbidFallDown`：源端 `0xA4449F` 读字符串 4417 写入支持段 +0x30。**该字段确实存在**：全量解析 6337 张地图、394859 条 foothold，其中 225 张图共 2734 条段带 `forbidFallDown!=0`（另有 `cantGo` 2317、`cantThrough` 1711 尚未建模）。地图 IMG 的属性名是 XOR 加密的，`strings`/`grep` 查不到，必须用 WZ 解析器（`MapleDash/tools` 下的 wzpy 用法，或 `NativeMapView.LoadMap`）读。
- 已接：`FieldFoothold.forbidFallDown` → `Art.Stage.ForbidFallDown`（与 `Footholds` 同序）→ `PlayerCtrl.SetTerrain(t, forbidFallDown)` → `BeginDownJump` 命中该段时拒绝下跳。
- 回归：`NativeCombinationChecks.CheckDownJump` 覆盖「穿得下去 / 落得到下一层 / 下方无落点时不把人送进虚空 / 不按↓仍是普通跳 / forbidFallDown 段不许穿」。

## 系统菜单（Esc）与面板

- 原端 Esc 只对应系统菜单动作，关窗口靠各窗口的 X，没有「Esc 逐层关窗」的行为。
- 本地按用户明确要求另加：Esc 先关最后打开的面板（`GameWindows.CloseTop` + `TrackPanels` 观测打开顺序），没有面板才开系统菜单。这属于本地产品决定，不能记成原端逻辑。
- 键位窗、虚拟键编辑器、侧滑菜单、聊天输入、频道下拉、NPC 对话各自处理 Esc，`InputBridge` 在这些层让出这一次按键，避免一键关两层。

## 表情（键位 type6 / FaceExpression，2026-10-26 已核实）

### 键位表结构与分派

- 键位表按虚拟键索引：`[0xBED5A0]` 指向数组，每项 = 1 字节 type + 4 字节 data。`0x94F382` 读 `al = byte[esi]`，`0x94F429`/`0x94F4C1` 读 `dword[esi+1]`。
  **一个扫描码只有一条记录**，原端不存在「同一个键挂两个动作」。本地键位模型必须保持「一键一职」。
- 分派入口 `0x94F2ED`，`type-1` 逐级 `dec/je`：1 技能、2 道具、3 现金道具、5 菜单动作（data-0x32）、6 表情、7 另一类动作。
- type6 分支 `0x94F4D1`：`data - 0x63(99)` 后跳 `0x94F692`，`ecx = [0xBE7918]` 调 `0xA24470`。
  因此 KeyConfig 图标编号 **100..106 就是表情 1..7**（和 `icon/100..106` 节点一一对应）。
- type6 分支**没有动作/状态门控**（type2 会先查攻击动作，type5/7 会查 `[0xBEC1F8+0x3C]`），
  跳跃、攻击、移动中按表情都能生效。

### SendEmotion 0xA24470（this = `[0xBE7918]` CWvsContext）

- `user = [0xBEBF98]`；为空直接返回。
- `[user+0x528] != 0`（骑乘/机械形态）改走 `0xA2A193`。
- `now = [0xBF060C]()`；`now - [this+0x3528] < 0x7D0` → 丢弃。**客户端表情节流是 2000ms**，不是服务端注释里的 1.5 秒。
- `emote < 0 || emote >= 0x17(23)` → 丢弃。
- 先调 `SetEmotion(user+0x88, emote, -1)` **本地立刻换脸**，再组 0x33 号封包发送。

### SetEmotion 0x451CF6（this = CUserLocal+0x88）

- `0x451D4F`：`emote >= 0x17` 拒绝；`0x451D47` 对负数也拒绝。
- `0x451D60`：`0x4165B1(index, &this+0x490)` 取名字对象存 `+0x498`，`+0x49C = 0`。
- `[this+0x4E0] != 0` 时跳过时长/效果设置（`0x453696`）。
- `0x451D8A`：`[0xBEC284 + emote*4]` 非空时拼 `Etc/EmotionEffect/%s`（字符串池 986）生成表情特效；`[0xBEBF6C]==0` 时整段跳过。
- 名表 `0xBEC284` 是 23 项静态数组（初始化 `0x45E8E8..0x45EDFA`，逐项 GetString）：
  `blink, hit, smile, troubled, cry, angry, bewildered, stunned, vomit, oops, cheers, chu, wink, pain, glitter, blaze, shine, love, despair, hum, bowing, hot, dam`。
  **14..18 是 glitter/blaze/shine/love/despair**，和 `Face.img` 节点罗列顺序（…glitter, despair, love, shine, blaze…）不同，必须按这张表，不能按节点顺序推。

### IMG 节点

- `UI/UIWindow.img/KeyConfig/icon`：40 个，编号 0..27、50..54、**100..106**。100..106 = 7 个基础表情的托盘图标。
- `UI/UIWindow.img/KeyConfig/key/<扫描码>`：键帽字形（11~13px 点阵小图），底栏键名也走这套。
- `Character/Face/<faceId>.img`：`default/blink/hit/smile/troubled/cry/angry/bewildered/stunned/vomit/oops/cheers/chu/wink/pain/glitter/despair/love/shine/blaze/hum/bowing/hot/dam`（其后的 `bangle/setback/wickedness` 属更高版本，083 不可达）。

### 服务端

- `RecvOpcode.FACE_EXPRESSION(0x33)`；`FaceExpressionHandler`：`emote > 7` 时要求背包有 `5159992+emote`（即 5160000..5160014，`ItemId.isFaceExpression`），`emote < 1` 直接丢弃。
- `Character.changeFaceExpression`：`now - lastExpression > 1500` 才 `broadcastMessage(this, facialExpression, false)`。
  **`false` = 不发给自己**，所以自己的脸只能靠客户端 `SetEmotion`；服务端的 1500ms 只是广播节流。
- `PacketCreator.facialExpression`：`chrId + expression`。
- 脚本层没有表情接口：`NPCConversationManager`、`gms-server/scripts-zh-CN` 全量检索无 emotion/setEmotion。表情只有「输入 → 本地换脸 + 封包」这一条路径。

### 本地迁移与验证（2026-10-26）

- `SpriteAnim.EmoteNames` = 上表 23 项；`EmoteCooldown = 2s`；`EmoteDuration = 5s` 回默认脸。
- `PlayerCtrl.Express`：`emote == 0` 回默认脸；`1..22` 合法；`8..22` 需要 `GameSession.Character.inventory` 里有 `5159992+emote`（对应服务端门控）。
- 键位一键一职：`KeyMap.Claim`（读档）、`KeyMap.Apply`（整表应用）、`KeyMap.ExportCharacter`（写库一码一行），
  避免 1 号键默认的「对所有人聊天」和表情共键——共键会先开聊天抢焦点，`playing` 变 false，后面所有动作都不再派发，表现为「点了没反应」。
- 屏上按键的按下回执 `VirtualControls.ActionDisplayName` 走键位表中文名，会打印「1 → 微笑 / 1 → 对所有人」，供真机排查键是否对上。
- 回归 `ExpressionChecks.Run`：7 个基础表情逐像素、2 秒节流（1.6s 挡 / 2.1s 放）、名表 23 项、`0/23/-1` 拒绝、
  8 号无道具不显示 / 持有 5160000 后换成 `vomit`、改绑 1/2/3 后键位表无重复且按下能换脸。
- 待核实：`0x451CF6` 的时长参数调用方一律传 `-1`，exe 里没定位到「5 秒回默认脸」的常量；
  当前 5 秒取自服务端 `FaceExpressionHandler` 的注释，属实现约定，不是已核实的客户端行为。

## foothold 传送带 force（2026-10-28）

二进制身份同 binary-and-tools.md 中 EXE SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`。

- 地图1 `Map/Map/Map0/000000001.img/foothold`：14/15/16/20/21/25/30/38/41/42/48/50/54/55/56 共15段 `force=-100`，例如20段 (215,128)→(544,128)。低一层 y180 的普通地面没有 force。不是 obj 动画或服务端 NPC 脚本驱动。
- `0xA44442` 读取字符串2972=`force`，默认0；`0xA44730..0xA44744` 转 double 乘 `0xAF14F0=0.01`，写入支持段属性+0x24（secured double）。因此原始-100→-1，0为普通地面。
- 地面移动 `0x9B23F2`：movement+0x110 为支持段，其+0x28为属性；`0x9B2582` 解码属性+0x24。`0x9B2602` 无输入时 force×基础力；`0x9B26FA..0x9B2712` 用绝对 force 整数值×基础速度作为速度限制。对教程图-100，静止时最终向左125px/s。
- `0x9B25AA..0x9B2600` / `0x9B26A5..0x9B26F8`：输入与 force 同向，倍率2×abs(int(force))；反向倍率0.2/abs(int(force))（2026-10-28继续核实常量0xAF1628为0.2，纠正首轮误记0.5）。教程-100，基础125时顺向上限250、逆向25px/s。基础速度见 `0x9B2187..0x9B21BD`，Map/Physics.img/walkSpeed=125。
- 原端仍对目标速度做加速度/阻力积分（`0x9B2805`后）。本次不宣称该完整积分已还原：项目既有水平移动是直接速度模型，本次接入上述传送方向和速度倍率；起步/刹车过程与极端非整数 force 的原端语义仍有迁移缺口。

迁移链：NativeMapView→FieldFoothold.force→Art.Stage.FootholdForces/GameLoop 或 FieldDirector.ApplyGeometry→PlayerCtrl.TerrainForces→StepMovement。两条现有原地图加载路径都按 foothold 同序传递；SetTerrain 清空/长度不符时清除旧力。FieldDirector 之前还漏传 forbidFallDown，现随同序属性一起传入。只对 grounded 当前 SupportSegment 生效，保留输入朝向/动作和原有 FollowGround/墙碰撞；不按场景贴图识别传送带。

定向验证入口 `MovementChecks.Run`：地图1实际解析、30/60/120Hz静止传送、顺逆向、下层普通平台不受上层force影响、跳跃无地面force、重设地形后清除force。本次 Unity batchmode 实跑通过上述检查及原有绳梯、墙、斜坡、落地回归（退出码0，日志 `/tmp/mapledash-conveyor-check.log`）。Android实机手感尚未核对。

### 原端积分迁移与独立对照（2026-10-28 后续）

上节“直接速度模型”的迁移边界现已更新：`NativeMovementPhysics` 替换普通地面/传送带/斜坡和普通空中水平直接设速。不是以最终速度接近为验收标准。

- `CLifeMoveParam` 构造 `0x4FE802..0x4FE97B`：+0x0C质量=100（0xAF29E0），+0x18/+0x24/+0x30=1，+0x3C滑坡阈值=.9（0xAF32D8），+0x48/+0x54/+0x60/+0x6C=1，+0x78/+0x84=0。用户移动参数持有者 `0x9448EE..0x944948` 创建此对象。
- `Map/Physics.img` 基础参数：walkForce140000、walkSpeed125、walkDrag80000、slipForce60000、slipSpeed120、floatDrag1 100000、floatDrag2 10000、floatCoefficient .01、gravityAcc2000、fallSpeed670、jumpSpeed555、maxFriction2、minFriction.05。运行时从 IMG 读取，不按手机手感自调。
- foothold属性 `drag`（字符串2971）同force乘.01，原值0不覆盖构造默认1。`0x9B2793..0x9B27DB` 把drag限制minFriction..maxFriction，小于1再乘.5；加速度是force/mass，地面静止阻力是drag×walkDrag/mass。速度超限先按阻力回落，再积分输入力；不得直接Clamp到目标速度。
- 坡面 `0x9B2433..0x9B247A` 的正弦与符号参与 `1±sin²` 力修正；超过actor阈值.9进入slipForce/slipSpeed分支。`NativeMovementPhysics.Ground` 照此分支迁移。
- `0x9B2B69..0x9B2BB7` 地面位移=(旧速度+新速度)×dt×.5；`0x9B3421..0x9B34BE` 空中x/y同为梯形积分。dt原端传入毫秒后乘常量0xAF0E10=.001。
- 普通空中 `0x96C1D3` 分支→`0x9B2CCF..0x9B2EDE`：按键力=2×floatDrag2×方向，限制=walkSpeed/walkForce×floatDrag2；已有超出该小上限的速度保留，不硬截断。松键时竖速未达fallSpeed，水平阻力乘floatCoefficient（正常默认为1px/s²），达到fallSpeed则100px/s²。因此不能把松开摇杆当作空中横速清0。
- 普通起跳 `0x9B1E43..0x9B1FFE` 保留地面横向速度；有输入时 `0xAF0D40=.8` 的起跳助推与方向/坡面上限比较，先加再限（不是任何跳跃横速都设125）。`0x9B1553/0x9B1646` 是地面切线和二维速度投影的转换入口。本地已接基础起跳助推、落地投影、墙/边界速度清理、下跳/换图/攀爬速度重置。

独立验证工具 `tools/native_movement_oracle.py`：SHA白名单检查后用Unicorn执行原EXE地面`0x9B2402..0x9B2BB7`和普通空中`0x9B2C46..0x9B34C1`，仅替换secured数值读写、临时分配/释放和明确的普通移动状态入口，不替换积分算术。输出含签名、输入和原函数输出。fixture `tools/native-movement-oracle.json` 有945组（900地面/坡面/force/drag，45普通空中），`MovementChecks.CheckNativeOracle` 比较C#速度/位移，容差0.0001px或px/s，已实跑PASS（包括普通空中竖速校验，最终复核见本节末）。**这是受控参数的原函数对照，不是完整Windows进程/Android操作手感验收**。

复现：`rtk proxy /tmp/mapledash-reverse-venv/bin/python tools/native_movement_oracle.py <BeiDou.exe> --output tools/native-movement-oracle.json`；依赖pefile、unicorn。Unity batchmode入口 `MovementChecks.CheckNativeOracle`。普通地面输入阈值映射到原端整数-1/0/1；手机摇杆采样与原Windows键盘事件时序仍是不同输入设备。

剩余边界：游泳/飞行（独立native分支）、骑乘/特殊技能位移、完整角色/地图/状态属性倍率与原端碰撞分层/固定tick调度尚未全量迁移验证；不得将这945组通过宣称为全游戏所有移动状态1:1。Android实机输入延迟/帧呈现尚待对照。

本次最终复核：独立Unity副本中 `MovementChecks.CheckNativeOracle` 945组全部PASS（含空中竖速，退出码0，`/tmp/native-physics-oracle-final.log`）；`MovementChecks.Run` 的传送带、起步/松键/跳跃惯性、绳梯与全部原图平台落地回归PASS（退出码0，`/tmp/native-movement-final.log`）。`NativeCombinationChecks.Run` 组合输入/下跳/逐帧攻速PASS（退出码0，`/tmp/native-combination-physics-final2.log`）；其中释放下键的预期由普通stand修正为攻击后五秒alert，未改变运行时动作逻辑。当前源码dotnet编译0 warning/0 error，diff空白检查通过。持久复现入口为上述Editor方法与仓库内oracle脚本/fixture，临时日志仅为本次运行记录。

## 台阶直角墙脚漏地（2026-10-28）

### 原端证据与边界

二进制身份同 binary-and-tools.md，EXE SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`。
碰撞处理 `0x9B3BC8..0x9B3C58`：候选支持段在 edi，方向分量在 +0x30/+0x38，端点坐标 +0x0C/+0x10；以非零方向分量将碰撞位置换算为段内距离，经 `0x5393B6` 写回，并将距离限制到 0..段长度(+0x40)。碰撞位置落在支持段几何上，不是简单保留碰撞前的旧横坐标。
复现：项目 `tools/inspect_beidou_client.py`，`--start 0x9B3BC8 --end 0x9B3C60`；忽略段条件仍见 `0x9B3B63..0x9B3BC5`。本次没有还原原端完整碰撞调度、受击速度或击退距离。

### 项目缺陷与迁移

旧 `StepMovement` 对竖直 foothold 命中设置 `nextX=worldX`，会把沿墙下降的角色留在墙线外；墙脚下层平台从墙线开始时，`FindLanding` 的严格 x 范围一直不覆盖该角色，最终掉到地图底部。`TakeDamage` 另有 `worldX += dir*0.25f`，受击位移绕过墙与支持段检测。

`PlayerCtrl.MoveHorizontal` 复用走路的连续支持段与竖直墙判断，墙碰撞截在 `wall.x`；记录接触方向以处理“从墙线上继续向墙内移动”，反向可离墙，零位移保留接触方向。显式放置/复位清除接触；原地形与 IMG 不修改。本次最初将 `TakeDamage` 的 0.25 世界单位位移接入该求解器；工作区随后并行更新为原端接触冲量（见当前 `TakeDamage` 与其对应逆向记录），受击速度现经 `StepMovement` 调用同一墙/支持求解器，不再保留直接横向位移。

回归入口 `MovementChecks.CheckStepCorners`：双向 30/60/120Hz 墙脚下落、墙线上起步/停步/反向离墙、步行不穿墙、跳上台阶和实际 `TakeDamage`；原有突进、绳梯检查；扫描资源全部地图的竖直段下端点接地情况。每条原地图墙脚调用实际 `MoveHorizontal`、`FindLanding` 和 `StepMovement`，验证接触与后续下落，而非仅重复几何公式。
本次 Unity batchmode 实跑 PASS：6337 张地图、16652 个连接下层平台的竖直墙脚，双侧共 33304 个起点均调用实际移动子步接住平台；双向 30/60/120Hz 接触/停步/反向、真实受击、跳台、突进及绳梯检查也通过。相交平台可能高于墙脚，因此判定是落在起始脚底下方的有效支持，不强制吸到墙脚最低点。日志 `/tmp/mapledash-step-corners-final.log`，可由上述持久入口重跑。不代表所有地图路线、不同层相交选择、Android 实机和原端完整物理已验收。
旧 `CheckAllFootholds` 全段静止半秒后必须留在原高度的检查，在当前工作区悬崖斜段 `(-22.93,13.05)→(-22.05,13.88)` 失败；该检查没有隔离落地后斜坡运动，未作为本次墙脚通过依据，后续需单独区分漏接与落地后滑移。


## 绳梯左右＋跳跃（2026-10-28）

身份：BeiDou.exe SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`、ImageBase0x400000。

- `0x9B19E5..0x9B19F4` 消费 movement+0x168 请求，经 `0x9B2202` 调 `0x9B1D3D`。
- `0x9B1D54..0x9B1D8A` 解码绳梯引用+0x124/+0x12C；若在绳梯上，水平输入（该路径+0x150/+0x158 secure int）为0则跳到尾部，不脱绳。旧本地注释“无需方向也跳离”错误，已纠正。
- `0x9B1D90..0x9B1D97` 虚表+0x14脱离绳梯；`0x9B1D9A..0x9B1DF2` 设置原Y向下速度 `−Physics.jumpSpeed×角色jump属性/场景属性×factor`，普通factor为 `0xAF0D48=0.5`；`0x9B214A` 特殊游泳/飞行等条件为真时选 `0xAFE800=0.3`。当前仅迁普通路径。
- `0x9B1DF5..0x9B1E3B` 水平初速 `输入方向×Physics.walkSpeed×角色walk属性×1.3`，常量 `0xAFE898=1.3`。普通默认125/555时即±162.5px/s及向上277.5px/s。后续由既有空中积分减速/落地，不是完整555竖直跳也不是从横速0缓慢起步。
- `0x936E53..0x936EBA` 无foothold/无绳梯后普通空中move action为6或7（按左右输入决定朝向；无左右保留朝向），并非继续rope/ladder动作；特殊游泳/飞行另为12/13。原IMG仍是 `Character/00002000.img/jump`，无新绘制素材。

复现：`rtk proxy python3 tools/inspect_beidou_client.py ../../Beidou-Server/clien/BeiDou.exe --start 0x9B1D3D --end 0x9B1E43`；常量用PE映射的上述VA读little-endian double。动作分派可读 `0x936D99..0x936EBE`。

运行时只改 `PlayerCtrl.StepClimb`：要求方向、竖速0.5、横速1.3并设朝向。沿用已有jump动画切换，既有0.25秒再抓冷却未作为原端已核实参数。未调整抓取容差、攀爬速度或特殊状态属性。

定向回归 `RopeJumpChecks.Run`：绳/梯×左/右×30/60/120Hz，检查单跳不脱绳、组合键脱绳、起跳初速、朝向、停帧攀爬切到正常速度jump、离绳轨迹与当帧不重抓。实际执行结果见本轮交付，不等于Android实机/特殊状态验收。

## 普通移动属性接线（2026-10-29）

身份：同本文EXE SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`、ImageBase0x400000。

- `0x78074A..0x7807A8` 将基础Speed限制100..上限（默认140），Jump限制100..123；`0x78044F..0x780479`另加`0x765C5E`提供的THRUST，后者按job祖先311/321/1310读取3110000/3210000/13100004，返回等级对象+0x70的speed（当前WZ等级1=2）。不能将该字段误作x。
- `0x8C457C..0x8C45E7`：基础speed + max(临时负值时直接用负值，否则比较临时值与持有者值)；`0x8C45E8..0x8C4632`：基础jump+max(临时值,持有者值)。当前本地仅实现已支持的装备、THRUST、SPEED/JUMP，特殊持有者/骑乘分支仍有缺口。
- `0x94D3D9` 更新CLifeMoveParam，`0x94D411`取walk，`0x94D44E`取jump；`0x94D418..0x94D449`状态+0x504非零覆盖walk。此状态由`0x783B44..0x783B70`读取flag全局BEFFC0后调用`0x786F14`设置。`0x790C14..0x790C3E`构造该flag的bit32，对应Disease.SLOW第二个64位标记的低位1（源Disease.java）；不是在输入轴上相乘。
- 普通无骑乘分支`0x94D8F1..0x94D9C3`将walk限制80..默认140、jump限制80..123，乘常量0xAF14F0=.01写CLifeMoveParam+0x6C/+0x60/+0x24（walk）及+0x48（jump）。骑乘分支可到190，尚未迁移，不能套普通上限。
- 源`SetItemNativeStats.mergedValue`和`Character.setItemNativeStatValue:5308`：只遍历-100<position<0穿戴行，套装SPD/JMP在equipment+skill上apply后减equipment，夹short范围，再由客户端与装备基数相加。当前复用既有EquipLoadout支持的穿戴槽和AdminSetItem.ApplyStat；未声称所有饰品槽/实例强化字段已齐。

迁移：CharacterAttributes保留equipmentSpeed/equipmentJump；EquipCatalog与武器原IMG读取incSpeed/incJump；PlayerCtrl.RefreshMovementAttributes在状态变更及Update消费Physics.img基速，取消/到期/装备更换沿用属性刷新。NativeMobDebuffs不再缩放输入轴（旧积分会取符号导致无效），SLOW直接覆盖walk。水中/飞行、骑乘、特殊移速上限来源、场景属性尚未完整接入。

独立复现：`tools/native_movement_stat_oracle.py`校验原二进制SHA，Unicorn执行`0x78074A..0x7807A8`、两个原getter与`0x94D8F1..0x94D9C3`，仅替换secured存储包装/holder访问，原算术和分支不替换。fixture `tools/native-movement-stat-oracle.json`192组装备正负值、THRUST、增益/减益、jump上限与SLOW覆盖。Unity入口NativeBuffChecks.CheckMovementOracle（通过Run/RunAll或RunMovementOracle调用）。

本批`/tmp/mapledash-completion-movement-buffs.log`退出0，192组原x86对照与实际4101004急速20级→walk140%/jump120%、SLOW→80%、取消恢复基速均PASS；未沿用此PASS覆盖后续THRUST字段纠正或新增魔力吸收测试，后者另记。

## 二段跳/灵魂迅移冲量（2026-10-29，新核实算术，门控仍待补）

身份：同本文EXE SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，ImageBase0x400000。

- `0x96BE38`入口读取skill指针与等级；`0x96BEF3..0x96BF12`只接受4111006、11101005、14101004。`0x96BFCA..0x96C038`对4111006/14101004使用level，对11101005先level*2，然后有符号整数除4为q。原坐标水平速度为facing*(350+40*q)px/s，竖直速度为-250-20*q px/s（WZ向下为正）；不是按IMG范围或time决定飞行距离。`0x96C03C..0x96C04D`转double后调已知冲量入口0x7A6353，最终冲量保留较快同向速度的语义沿用角色受击记录。
- `0x96BFB4..0x96BFC5`提交动作/技能，随后user+0x2B58置1；入口该字段非0拒绝。`0x96BF52..0x96BF91`的4111006/14101004分支要求movement+0x110为0并查询0x705343/0x705309/0x96C1D3/0x96C1AA。11101005走相反的+0x110条件，另外查询0x94C4D9以及user+0x3158技能时刻表；`0x96C16B..0x96C191`为其写入0x9535E3返回时长+当前时刻。不要将三技能套同一空中/冷却门控。
- **待核实（落地重置现已补证，见下一段）**：此前未找到+0x2B58的落地重置调用链；0x705343/0x705309对应特殊移动模式；0x96C1D3/0x96C1AA支持段/垂直速度组合条件；11101005的独立地面/冷却语义。未将这些不完整门控接入PlayerCtrl，不声称二段跳已可用。

复现：`tools/native_flash_jump_oracle.py`校验EXE身份，Unicorn执行原0x96BFCA..0x96C038，仅stub 0x52EF17移动事件记录，保留分支/除法/移位，输出`tools/native-flash-jump-oracle.json`。本轮180组（三技能×1..30级×双向）全部对照通过。fixture只证明初始冲量算术，不证明完整施放/重置状态机。


### 二段跳重置子对象偏移纠正（2026-10-29）

同页EXE身份/base。原构造948E33写[user+4]=B3D1E8，[user+88]=B3D1CC（948E41）；B3D1EC（二级接口vtable+4）为950555。该函数95056A保存ecx到edi，故edi是user+4，不是完整user。9506F6的[edi+2B54]对应完整user+2B58，**此前把它当作下跳user+2B54指针是错误的**。

9506F6..95071F仅在该值非0时读取movement参数[ebp+14]：支持段movement+110非0，或705309、705343任一非0，写[edi+2B54]=0；纯空中无绳梯保留。705309/705343解码同一movement+124/+12C绳梯引用，并分别以对象+4非0/为0区别两类；不是未知游泳/飞行模式函数。950725..950731再转原基类936D99，后者avatar使用this+84，进一步印证二级接口+4偏移。

复现tools/native_flash_jump_reset_oracle.py执行原9506F6..950725，只stub两个绳梯布尔getter，使用edi=user+4，16组used/支持段/绳/梯全部通过，fixture为tools/native-flash-jump-reset-oracle.json。直接搜索完整user+2B58会漏掉此reset，后续不再重复搜索。

剩余准入：96BE38首先地图fieldLimit bit1（context+2030权限bit0可绕过）；96BEBE的95F914及94BE9C是角色状态门控。96BF52要求无支持段、无两种绳梯；96BF7D先查96C1D3，再按96C1AA拒绝：后者读movement+68 secured double与0比较（>0返回true），已由既有物理oracle确认+68为向下坐标竖速。96C1D3在无支持段、7046AC false、movement vtable+8 false、actor+78 secured值<=0时返回true。7046AC又查movement+1A0区域状态（704710、区域+3C包含角色位置），**不能简单把它全部归成游泳**。11101005仍有独立地面/技能时刻表门控，其完整迁移尚未完成；本次没有把半套准入接入可无限施放的通用攻击。


### 2026-10-29：经典二段跳普通运动分支

二进制身份沿用本文 BeiDou.exe SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`、ImageBase0x400000。
`96BE38`：96BEB5检查full-user+2B58使用标记；96BE4F的context+2030权限位0可绕过fieldLimit位2。
96BF52经典技能要求无支撑且不在绳梯；96BF7D调用96C1D3/96C1AA，后者读取WZ向下速度+68>0（下落）。
451B6A检查avatar+88 one-shot动作；95F914另有临时状态/动作准入。
`PlayerCtrl.CanFlashJump/ApplyFlashJump` 与 `SkillSystem.BeginCast` 现接4111006、14101004：
普通空中非下落、无one-shot、不在绳梯、地图未禁跳、此次离地未使用才扣费；原冲量按等级/4整除计算。
真实落地、重新放置、绳梯抓取清使用标记；二次空中施法拒绝不扣MP，不进入普通近战结算。
`NativeBuffChecks.RunAll` 覆盖站地/下落拒绝、Lv20水平550/竖直350px/s、重复拒绝与真实StepMovement落地重置；
`/tmp/mapledash-priority-flash-jump.log` 与 `/tmp/mapledash-priority-range.log` exit0。
范围边界：尚未搬11101005地面Soul Rush、特殊区域/游泳/飞行/坐骑准入及权限绕过。
57502E读取TemporaryStat+2DC、94BE9C读取+630/+638的状态身份仍未确定，不能猜成某种疾病。


## 2026-10-29：Soul Rush 11101005 准入补记

同版 BeiDou.exe SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，base0x400000。
`96BF14` 要求 movement+110 地面支撑；`94C4D9` 读取 TemporaryStat+4BC。
`783976..7839A2` 检查 BEFFD0 并经786E60写+4BC；`790BB4..790BDE` 构造此掩码。
Unicorn执行原构造指令后BEFFD0四word为(0,0,0,0x40000000)，对应原服Disease.WEAKEN掩码
0x4000000000000000 / MobSkillType.WEAKNESS=122。
`9535E3→95365B→953694` 对11101005返回1500ms；`96C16B..96C191` 存入user+3158，
`96BF2C..96BF4B` 在准入时检查截止时间。复现用 inspect_beidou_client.py --fn 对上述入口。
PlayerCtrl.CanUseFlashJump/ApplyFlashJump 已接地面、WEAKEN122、独立1500ms、等级翻倍后的冲量与离地。
NativeBuffRenderChecks.CheckFlashJump 检查拒绝不扣MP、落地后仍受1500ms限制与过期放行。
57502E/94BE9C共同状态及特殊区域门控仍未全部核实，不宣称特殊移动准入完整迁移。

本批 Soul Rush 新增准入/冲量/落地/1500ms 断言在隔离Unity
NativeBuffChecks.RunAll（/tmp/mapledash-priority-four-runtime2.log）退出0；未作Android验证。


## 去除绳底下方的自动抬升（2026-10-30）

本次属于删除迁移代码中自行增加的100px绳底容差及吸附，不是新认定的原端物理常量。
旧PlayerCtrl.StepClimb在有上/下输入分量时，允许脚底低于绳底100px并立即把jumpY设到绳底，
导致斜向经过绳子下方会被抬起。现只在脚底达到bottom后才允许抓取，不改已有原端绳梯左右跳初速。
验证 `NativeGameplayFixChecks.Run`覆盖脚底低于绳底50px且有向上分量不附着/不抬升，以及绳身范围内正常抓取；
`RopeJumpChecks.Run`继续验证左右跳0.5/1.3与方向门控。
