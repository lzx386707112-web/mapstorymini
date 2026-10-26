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
`0x765066` 合并基础攻速和两个属性增益，技能4001334另减2，最终限制2..10；当前只接入武器基础攻速，尚未接入这两个增益字段。
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
