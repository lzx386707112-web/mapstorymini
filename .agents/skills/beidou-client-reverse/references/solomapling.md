# SoloMapling 假人：源端依据与迁移边界

## 身份与复现

2026-10-27 核实服务端 Java 源码，根目录 `../BeiDou-Server/gms-server/src/main/java/`（相对 mxd-android）。本记录没有新增 EXE/DLL 逆向结论，不适用二进制 VA、IMG/UI 布局。

| 源文件（相对 soloMapling/） | SHA256 |
| --- | --- |
| `server/BotTickService.java` | `218041a24235fd12c9728ebb4f551adec8f85de17627e9cf766b9af7bb73f15c` |
| `ArtificialPlayer/BotGeneration.java` | `a64bd3ecb604847f697bc00d3c4550ec241b17b02fc9f3b9396505fcc1bb03c0` |
| `Environment/PlatformSpawner.java` | `795a9cf2056f92ea87ca5d73e5b6d392a337221869ad4610dd2534134b8f123d` |

## 确定逻辑

- `SoloMaplingConstants.GameConstants.BOT_BASE_ID=20000`。`BotGeneration.reserveBotId` 从计数器100开始，CAS成功返回 `20000+current`，首个20100。环境上限针对启用限制后创建数量计数，移除不返还配额。
- `BotTickService.register` 是 put-if-absent；250ms固定延迟驱动。`reschedule/nudge → requestFireAt` 同时保存 pending 截止时间，回调执行中不可直接改写 MAX_VALUE 的 nextDue。回调结束取出 pending 再清零，`due=min(now+有效周期,max(pending,now))`。异常也释放执行标志并重新安排。
- governor 窗口5000ms，平均延迟>1000时因子×1.5、上限4；<300时÷1.5、下限1。`noThrottle` 只豁免该条目的周期拉伸。
- `BotGeneration.removeBotFromServer` 清理地图、channel、world、CharacterStorage。C# 单线程适配额外停用状态机/调度，避免字典移除后悬挂回调。
- `PlatformSpawner.snapToGround` 先找 `(raw.x,raw.y-12)` 下方平台，失败后从 foothold 最小Y再找，成功返回 `below.y-1`，失败保留原坐标。`MapleMap.calcPointBelow → FootholdTree.findBelow` 排除竖墙。源端斜坡用整数截断；当前浮点适配尚未实现逐像素完全一致。
- `BotAutoSpawner.onPlayerEnterMap` 只确保 standalone client，不直接生成地图假人。`requestStartupIfEnabled` 在 marketAutoStart 开启时一次性排队2500ms后执行环境填充。
- `BotWanderSystem.tick` 地图变化即停止；无观察者时不推进。决策轮400ms，实际移动委托GCMovement；选点依赖可达区域和ledge，不能以任意全图随机坐标代替。

## 当前实现与验证边界

- `Assets/Scripts/Server/Bot` 是后端骨架。已修复ID、CAS、调度回调内重新定时/增删条目、移除与替换状态机清理、换图索引；生成已接 `Server/Field/MapService` 落点。
- 角色浮点坐标、wander/moving/facing及帧delta接口补齐，解除现有游荡代码编译缺口；帧推进与250ms决策轮分开。
- `BotWander`已移除110px/s临时位移，接实际地面/空中/绳梯/游泳执行器、导航路径执行、可达ledge选点、观察者门控与400ms决策轮；阶段C仍缺完整移动技能业务、接触/坠落伤害及跟随。
- SOCIAL_BOT与TOWN_WANDERER_BOT已映射具体类型，其余15种类型仍映射GenericBot。环境填充、装饰、社交、战斗/组队、交易、赌场及前端BotView未完整实现。`FieldDirector.TickBots`已接真人位置与游戏时钟，尚未接前端显示与自动填充；不能声称游戏内可见或Android已验收。不得为填补缺口自行绘制UI或虚构AI行为。
- 验证入口：项目目录运行 `rtk dotnet run --project tools/server-check --no-restore`；`BotChecks.cs` 覆盖调度、落点、清理、消息分派/过期、事件过滤/退订、移动属性、平台解析、BIN/CSV录制读取和社交时序。当前实际结果：333项通过、0失败，未运行APK构建或Android实机验证。

总体阶段计划仍见外层 `docs/solomapling-analysis.md`；该文档描述的设计不等于已迁移状态。

## 资源与消息系统核实（2026-10-27续）

- `tools/export_solomapling.py` 导出929份资源（含440份BIN移动录制），每份同时保留base64原字节和SHA256；文本/YAML另提供解码/结构数据。`--verify-only`逐文件比较原字节、哈希和完整文件集合，929份全部通过。运行需PyYAML，当前工具环境为`/tmp/mxd-solomapling-tools/bin/python`，临时环境不属于项目依赖。
- `TownWandererDialogue.yaml`原文件首字节为`84 4c c6 4e ...`，不是有效UTF-8；`SoloMaplingResource.openReader`与`BotDialogueHandler`未发现解密流程。原文件不修改，打包字节完整；`BotResources.ReadBytes`可直接取回，ReadText/ReadData明确抛解析错误。此处**尚未恢复可运行的城镇对话**，不能编造台词或把原字节保留当作行为迁移完成。
- `Dispatcher.run`每2000ms消费一条primary，按地图角色顺序匹配名字子串，忽略id100。匹配真人名字后取不到BotSM也会消费消息，不继续匹配后续假人。未运行bot设置respondant后启动；运行的SocialBot忙时忽略，空闲时首次交互；其他类型设置inquirer并显示对应选项。无名字且是respondant时清除提示并进入secondary，否则inquirer进入tertiary。
- `QueueCleaner`每2000ms清理三个队列，只有年龄**大于**10000ms才过期。C#版本只取消自身任务，不关闭共享调度器；这是主线程单机生命周期适配。
- `BotSM`事件队列容量100，按world/channel/map过滤，每次ProcessQueuedEvents仅消费一条，停用解除订阅。完整IDLE/RUNNING/PAUSE/TRADING/FINISHED基类FSM、交易协议FSM、闲置动作和共享市场驱动已实现；真实交易后端已实现，但运行时接线、业务类型和前端仍未完整接入。
- `SocialBot`交互型概率30%；起身600ms、思考2000..3999ms、问候表情延迟400ms。超时严格>35000ms；累计次数<=3正常、4减少、5非语言、>5忽略；冷却严格>300000ms，清理间隔120000ms。当前非阻塞链在停用/移除/结束对话时取消旧回调，这是主线程适配；测试覆盖问候/首选项/超时，未穷尽全部反刷/告别路径。
- `BotMovementProfile`按5分桶（Java Math.round），速度上限200、跳跃上限123；55级刺客/侠客为155/123，较低级为150/115；队友在线、其他id、60级且有急速时只加速度。值相等和哈希相等可避免属性不变时重复重建导航图。禁移动技能地图直接返回基础100/100且无雪鞋标志。地图标志、装备fs读取和队伍成员生产者仍需接线。
- `InPacketReader`BIN帧头是DataOutputStream大端long时间戳+int长度，内部包字节不转换；EOF截断最后一帧则不加入。440份原BIN读取共7441帧；CSV命令数过大遇到非逗号行会回退一行作为下一帧时间戳。读取器不等于移动回放/导航已经接通。

源文件逐项SHA256与C#映射在`docs/solomapling-port-manifest.json`，已有代码均保守标记partial，保留未接线项；不能按文件数计算系统完成率。

## 装饰、市场驱动与交易协议续迁移

- `MarketBotDirector`共用250ms轮，空图休眠8000..11999ms，每轮寻路预算默认3；发言冷却500ms，喇叭全局8000ms。`BotStateMachine`基类处理在线/观察者门控、交易优先级、PAUSE/FINISHED清理。这里只验证后端调度，不代表前端已接入。
- 原`MarketBotFlavor.java`16组1240条字符串由`tools/export_solomapling_flavor.py`原样提取，生成文件头保留源SHA256；近12条去重、最多16次重抽。
- 交易FSM保留60秒严格超时、2000ms非阻塞延时、差价容忍和300秒拒绝黑名单。`BotTradeService`已实现实际背包托管、金币、双边锁定、税费、剪刀标记及数据库事务回滚，并将邀请送入活体假人请求队列。玩家入口禁止使用假人生成物品/金币报价接口。**前端交易窗口/transport与运行时存档生命周期仍未接线，不能称为完整交易系统**。
- `tools/export_solomapling_items.py`从原IMG产出51259个名称、25324条物品info，记录19030个IMG的SHA256。运行时只读标量info；嵌套套装/等级效果等尚不在此目录。`BotItemCatalog`创建干净装备，不再用全零属性代替原数据。
- `tools/export_solomapling_equipment.py`保留28类装备范围、额外/展示戒指范围、7组兜底及原时尚点装脚本504条记录，输出保留三个源文件SHA256。NX池顺序：YAML→时尚脚本覆盖→缓存补空→兜底→戒指去重合并。现金武器-111、副手51、狂热图腾53；清现金槽严格`-160 < slot <= -100`。
- `GenericEquipPool`性别直接用千位数字（不把其他数字归一化），等级硬上限，权重`max(.05,1/(1+gap*.05))`；QuickEquip保留阶级概率、75%整体服优先与上下装同时可用才提交。
- 完整配装缓存按ID升序；等级窗口`maxLevel-max((int)(maxLevel*.25),10)..maxLevel`，reqJob严格相等，失败再查0。源`Job.getJobStyleInternal`弩手返回320，海盗按基础STR/DEX返回510/520；reqJob映射不包含这些值，必须保留其回退通用装备语义。
- 点装每次必选武器/服装，再按阶级加权不放回抽额外部位；没有旧注释写的30%总概率。生成入口已接身体、装备、等级120..200四分段与职业随机。出生移动编排已接录制回放；**角色完整属性重算/套装/HPMP恢复及游戏显示仍未完成**。
- 新增测试覆盖IMG属性、槽位边界、性别/等级筛选、点装开关/强制、职业风格、经典装备权重、等级分段及真实生成入口。当前333通过；没有APK或实机验收。


## 地图、观察者与移动物理续迁移（2026-10-27）

- `tools/export_solomapling_maps.py`从原Map IMG导出6336张地图的平台完整身份、prev/next、forbidFallDown、绳梯、传送门及地图info。`Bot/Maps.json`保存索引与世界portal邻接表，`Bot/Maps/<百万位>.json`分片；加载器只缓存最后一片，link按原端只解一层。导出记录每个原IMG的SHA256。
- `BotWorldGraph`实现原BFS跳数限制、portal边与六城出租车。原`BotScriptedWarp`三个方法本身为空，不新增猜测的脚本传送。
- `BotObserverTracker`每2000ms刷新真人所在图与距portal350px以内的邻图；活跃驻留4000ms在`<=now`过期；强制观察2000ms只在`<now`过期。已由BotRuntime.SynchronizePlayer接游戏真人生产者。
- `BotGroundIndex`按64px桶组织，稳定插入排序严格使用原`Foothold.compareTo`：`a.y2 < b.y1`为-1、`a.y1 > b.y2`为1，其余0。**不是minY/id排序**。查询排除竖墙与x1>x2反向底边；坡面沿用三角公式及整数截断。脚下比较当前点与上探26px结果的距离。
- `BotFootholdCollision`按next形成至少3段且端点完全相连的闭环，包围面积稳定降序，嵌套实体/孔洞交替，8px内薄壳继承父分类；只有实体底边可从下方碰撞。原Foothold.isCollidableWall仅检查竖墙。
- `BotWalkRegions`只沿prev/next将满足端点曼哈顿距离<=2及dx<=12、dy[-26,16]的地面合并。按minY/minX排区域、按minX/minY/id排段，随后添加rope区域。地面采样优先真正包含x的段，否则仅容许2px接缝；dy仍[-26,16]。当前已生成跳跃、下落、下跳、绳梯及同图portal边，包含路径搜索；实际移动manager已接线，完整跟随/技能/伤害业务仍缺。
- `BotGroundMotion`接8ms加速到实际区域采样，阻墙归零，离开支持面返回离地状态；`BotAirMotion`以每50ms像素为速度单位，重力5、终速33.5、普通跳27.75、rope跳18.75、下跳9.8、二段水平27.5按Java舍入为28。下跳350ms计时由调用方与原manager同序推进。落地保留坡面切线投影、半速和坠落距离；绳梯抓取/爬行、游泳物理和轨迹预测已迁移为独立helper；manager已接实际执行，坠落伤害业务仍未接线。
- 当前Java源文件是反编译产物；`Segment.pointAt`、壁/顶碰撞progress、横向落地扫描progress两操作数均int，当前C#按可用Java源码保留整数除法。没有找到可复核这些类的原JAR/class，故不能声称已用字节码确认这些表达式；后续若获得二进制应优先核实，不能把源码的舍入证据混为EXE汇编结论。
- 实际验证：`rtk dotnet run --project tools/server-check --no-restore`，333通过/0失败。覆盖真实交易与邀请、玩家防绕过、世界路线、350px/4秒/2秒观察边界、反向底边、区域接缝、闭环孔洞/薄壳、连续行走与离地、普通跳及二段触发。此为此前验证记录；后续已替换110px/s，仍不能声称游戏运行时完整迁移。

新增源端身份（相对soloMapling/）：

| 源文件 | SHA256 |
| --- | --- |
| `ArtificialPlayer/GCMoveSystem/BotPhysicsEngine.java` | `ac8b67a45fac175e4b5539f1bec2858a6c828f6bace6a43ae1bb9f476168650f` |
| `ArtificialPlayer/GCMoveSystem/BotNavigationGraph.java` | `a59046abdc08456edef0cac20b3925ebbce95162267098f14e308642c435605b` |
| `ArtificialPlayer/GCMoveSystem/BotNavigationGraphProvider.java` | `6b04c06f3f56b318f2362b7eb2cc031cfb44a600131d838987528c6cc14a92bd` |
| `ArtificialPlayer/GCMoveSystem/BotNavigationMapLoader.java` | `1048226bf609ac1ff6496269a731a23c5202d2f4196b062e531bf991538cea15` |
| `ArtificialPlayer/GCMoveSystem/ObserverTracker.java` | `37119118cfd93e32e17b56d6ce81a10740becd5a8d184749258d35268de42d63` |
| `ArtificialPlayer/GCMoveSystem/GCWorldGraph.java` | `6f66cd6d60e7d490843e19efe344245d1d45c4626c45be9f3e3ddc76ab010fda` |
| `ArtificialPlayer/GCMoveSystem/GCTaxi.java` | `91fb197a7e8cbdab826ad7886a65401ac5455009483f7f85a39d18ac7435656e` |

## 录制回放与入场续迁移

- `MovementPacketConstructor`使用9字节前缀、小端命令参数、signed byte命令数；不同命令宽度与`AbstractMovementPacketHandler.updatePositionBot`逐项对照。重建器对所有命令都写14字节是现有Java源码行为，保留而不自行改协议。
- offset每一包重新取当前角色位置，以首命令坐标计算偏移，平台替换为当前Below结果；不是整段共用固定偏移。服务端立即应用末命令位置，广播只含count与实际消费命令；前端插值消费尚未接线。
- `BotMovementPlayback`以BotTasks非阻塞执行相邻timestamp差；末包后立即完成。支持中断原双命令停止包、每5包midCheck、换图/移除取消且抑制旧业务回调。stopPoint按原始录制坐标的欧氏距离<=80判断，命中后停止并正常完成。
- `BotGeneration.PlaySpawnChoreography`等待500..1200+1500ms后播放map0/portalenterdrop.bin，完成后50%等待1000..1500ms再执行左转录制；stance3/5/7不转。WarpBotToLocation完成回调替代源端线程阻塞。尚未完成游戏内入场显示验收。
- 更正mockclient：standalone只保存host模板，每个bot单独创建client并令player指向自己。环境配额启用使用原CAS(start,0)语义；重复启用不任意刷新起点。
- 本轮实际后端检查333通过/0失败，新增8条覆盖封包偏移、末命令、逐包等待、完成、中断及换图取消。Unity全脚本编译检查0警告/0错误；未执行APK或Android验收。

## 实际执行器与城镇行为接线（2026-10-27续）

- `BotMovementRuntime`执行50ms物理（地面内部8ms），`BotNavigationRuntime`消费路径边和跳跃窗口；`BotMovementService`实现50/500/2500ms观察节流、100ms AI、20秒属性刷新、8秒无进展退出、空中/越界恢复。换图保留目的与回调，重建目标图执行器；观察图悬空60px等待1500..2100ms再下落。移动技能限制读取fieldLimit 0x02，雪鞋只取-7槽fs>=1。完整移动锁、技能业务、跟随、伤害尚未迁完。
- `BotRuntime`由GameServer持有，FieldDirector每帧同步真人脚底（世界坐标乘PPU，Y反号）与realtime时钟；延迟加载原IMG几何；退服释放执行器、观察者、调度、地图与输出连接。GCPortals按目标门名查找，缺失回退portal0。没有新增游戏UI或自动地图生成。
- `TownWandererBot`已具体实现游荡、表情及升级祝贺；源`BotChatter`三个方法为空/false，`discoverTownFamily`只有home，因此不添加聊天AI或跨城行为。源Town与LevelUpCongrats均捕获对话解析异常并跳过台词；保留原YAML字节并跳过发言与当前可用源码一致，不能声称已恢复文件中的对话。
- `BotFlavor`保留0.12概率、15..40秒冷却、5/3/3权重。动作输出仍待前端消费。
- `AmbientMapBot`迁移可达行为（85/5/5/5权重、24次选点、180px距离、原录制转身、配置调度与停止清理）。**全源树未发现构造调用者**，因此没有擅自加入自动地图生成；未调用的walkToward/walkSmallSteps不作为运行时逻辑迁入。该类不计入17类枚举已接入数量。
- 最新后端回归357通过/0失败（含环境行为观察门控与移除清理），不是游戏内或Android验收。上一轮Unity检查0错误，新增环境类之后仍需更新编译检查。游戏可见渲染、自动环境填充、15类具体行为、完整属性/战斗/组队、商城赌场、交易前端及存档生命周期仍未完成。
