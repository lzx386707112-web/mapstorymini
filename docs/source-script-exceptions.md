# 源端脚本异常核对（2026-10-26）

初次核对未修改脚本。随后用户授权修复，现已修复下表11处语法问题，并从源端重新同步全部5065份脚本原件和2777份运行文本。没有删除文件或以英文覆盖中文。

修复仅包含：10份活动脚本将字符串内实际换行写为 `\n`（保留对白换行）；20002对照英文版在 `status--` 后补闭合大括号。标准 Node 和内嵌解释器现在均为2777份全部可解析。以下源端失败处理分析保留，用于解释修复前行为。

## 11 个语法错误

修复前标准 Node 与内嵌解释器均拒绝以下文件，均位于 `gms-server/scripts-zh-CN/quest/`。

| 文件 | 源脚本用途 | 问题 |
| --- | --- | --- |
| -27078.js | 拉契尔恩平安夜（TMS 38458） | 双引号字符串含未转义换行 |
| -31069.js | 阿尔卡娜恢复树藤竖琴1（34467） | 同上 |
| -31103.js | 星光之塔开幕活动（34433） | 同上 |
| -31105.js | 蒂雅头号粉丝（34431） | 同上 |
| -31106.js | 亚美头号粉丝（34430） | 同上 |
| -31107.js | 佩里头号粉丝（34429） | 同上 |
| -31108.js | 萨菲头号粉丝（34428） | 同上 |
| -31109.js | 露比头号粉丝（34427） | 同上 |
| -31142.js | 回收50个睡眠粉（34394） | 同上 |
| -31158.js | 拉契尔恩平安夜（34378） | 同上 |
| 20002.js | 修炼教官奇库 | 第39行 status-- 后缺少闭合大括号 |

核对当前 `wz-zh-CN/Quest.wz/Check.img.xml`、`Act.img.xml`、`QuestInfo.img.xml`：上述10个负数 ID 及其对应无符号 ID 均没有任务节点。因此它们不能仅因存在脚本文件就被视为当前 WZ 中可正常领取的任务；这里不推断自定义脚本调用是否可达。

20002 则有真实任务节点：要求 NPC 1102000、职业1000、前置20001等，startscript=q20002s。Act 的0/1节点均为空。英文 `scripts/quest/20002.js` 有完整闭合结构，并含60经验、两种药水各10个及帽子1个的脚本奖励；中文也写了这些奖励，但语法错误使整个脚本无法装载。

## 源端如何处理

- `AbstractScriptManager.java:68-92`：中文文件存在就选中文，执行失败记录警告并返回 null；不会因语法错误改用英文。
- `QuestScriptManager.java:52-58`：只有勋章任务存在通用 medalQuest 回退。20002 的 Check 节点没有勋章要求。
- `QuestScriptManager.java:214-218`：加载失败时 checkFunctionExists 返回 false。
- `QuestActionHandler.java:87-99,103-120,127-156`：普通和脚本任务入口发现无可用 start/end，回退 quest.start/quest.complete，即 WZ 普通任务流程。仍受 canStart/canComplete 等条件约束。
- 直接调用 QuestScriptManager.start/end 时，加载失败则记录 uncoded 并 dispose；运行途中异常也记录并释放会话，不会从失败处继续发奖励。

因此20002在源端并没有自动修复：入口可回退普通任务，但空的 Act 节点不能补回 JS 对话、自动完成和奖励。不能把“任务状态可能可变更”说成“脚本完整可用”。

## 遗留 Java 导入

| 文件（scripts-zh-CN 下） | 导入 | 用途 |
| --- | --- | --- |
| npc/9110015.js、npc/9110016.js | net.sf.odinms.client | 百宝箱脚本 |
| npc/9110106.js、npc/9110112.js | net.sf.odinms.client | 枫城等地图传送菜单 |
| npc/9110116.js | net.sf.odinms.client | 离开场景传送 |
| quest/2324.js | Packages.client | 蘑菇城堡越过城墙任务 |
| npc/9010000.js | java.lang | 标准 Java 包，不能归为缺失的 OdinMS 类 |

当前 src Java 包为 org.gms；源码中未找到 net.sf.odinms 或旧 client 包实现。原引擎为 graal.js，配置仅开启 host access/class lookup，没有在 AbstractScriptManager 中开启 Nashorn compatibility，也没有自定义 importPackage。当前机器 Maven 缓存的 Graal 组件版本不一致，本次没有据此运行引擎实验，不能声称已复现原部署运行环境。

这些导入位于脚本顶层：若原运行环境无法解析它们，装载阶段就返回 null。NPCScriptManager 的普通入口依次尝试命名 npc、BeiDouSpecial、NPC ID脚本；均失败则 dispose 并返回 false。任务2324则按上述任务入口规则回退 WZ。不能以无条件忽略所有 importPackage 的方式宣称旧 Java 服务端已经迁移；脚本里的 cm/qm 调用仍需要真实的本地服务端接口支持。

## 迁移边界

保留原脚本和源端失败/回退语义。异常源文件与尚未迁移的服务端 API 分开报告；2777个有效脚本中2766个语法可解析不等于2766个玩法完整可运行。原Java逻辑迁入单APK的本地C#服务端可行，但必须逐个系统迁移真实状态与规则，无法仅通过复制JS或空接口完成。


## APK 拍卖主菜单适配

底栏帮助入口调用 `NpcSession.Talk(9900001)`；源端 EnterMTSHandler 在 use_mts=false 时也打开该NPC。主菜单脚本顶层加载 SkillFactory，信息栏调用 getNameplateSeries/getPqPoints，菜单末尾调用 player.isGM。原本这些宿主接口未接入，脚本可能在装载或构建菜单时终止。

现绑定称号系列（永久扩展值 nameplate_title_series，源端范围0..8）、副本积分/总积分（副本积分/副本总积分永久扩展值）、GM判定（gm>1）、已有角色damageCap及SkillFactory的真实技能目录查询。未用空类或假交易代替MTS。

主菜单内50级以上的“拍卖行”选项直接调用EnterMTSHandler.enterMTS；完整交易市场及其界面仍未迁移。本次修复主菜单装载和50级以下的原脚本限制，不宣称完整交易功能已可用。


### 本次验证结果

- 全量脚本：2777份，Node与内嵌解释器语法错误均为0；5065份原件同步校验通过。
- 解释器回归：23项通过。
- Unity真实NpcApi/临时数据库测试：拍卖主菜单打开、称号/积分读取、低于50级的拍卖提示，共3项通过。战力计算仍触发未迁移player.getTotalStr；源脚本自身try/catch将战力回退0，这不代表战斗属性已适配。
- Unity C#编译：0警告、0错误。
- 全NPC mock执行：889份，768成功、48运行错误、73 Java互操作缺口，0语法错误；这些结果不是完整玩法通过率。
- 未连接Android设备，未验证用户已安装APK，未在本次生成/安装新APK。完整MTS交易和主菜单其他功能的接口缺口仍需迁移。
