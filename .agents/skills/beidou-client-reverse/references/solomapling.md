# SoloMapling 假人：源端依据与迁移边界

## 当前接续入口（2026-10-27）

当前未完成说明见 [详细缺口与验收清单](../../../../docs/solomapling-unfinished.md)。17枚举中8种具体、9种Generic占位，整体incomplete；最近后端478通过/0失败，全Unity编译0警告/0错误，原IMG Editor检查PASS，未完成Play全流程及Android验收。下文按时间保留早期333/368等记录及其当时缺口，不能将早期状态当作最新状态。本文后半已包含EXE调查，开头“没有新增EXE结论”仅指最初服务端调查。

本次仅整理说明和校正明确的源文件映射，没有新增业务代码或重新运行构建。BotLogic、BotHelpers、PartyGrindService及三项交易组件的旧not_ported标签改为partial；其他旧映射仍须逐项复核，禁止按文件数计算完成率。

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
- SOCIAL_BOT与TOWN_WANDERER_BOT已映射具体类型，其余14种类型仍映射GenericBot。环境填充、装饰、社交、战斗/组队、交易、赌场及前端BotView未完整实现。`FieldDirector.TickBots`已接真人位置与游戏时钟，尚未接前端显示与自动填充；不能声称游戏内可见或Android已验收。不得为填补缺口自行绘制UI或虚构AI行为。
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
- 最新后端回归357通过/0失败（含环境行为观察门控与移除清理），不是游戏内或Android验收。上一轮Unity检查0错误，新增环境类之后仍需更新编译检查。游戏可见渲染、自动环境填充、14类具体行为、完整属性/战斗/组队、商城赌场、交易前端及存档生命周期仍未完成。

## 城镇生成与市场环境动作续迁移

- TownPresenceConfig按原YAML主地图顺序读取等级范围、wanderers、maps/count、dialogue及ban/boost/pins；TownPinsStore在当前Java源码中所有方法为空/空集合，不虚构磁盘sidecar。TownPresenceSampler保留NPC强度1、portal强度0.45、高斯分母135200/33800、宽度与320px高度衰减、0.15底重、9%均匀尾部、每点7候选、30px拥挤扣分；固定pin先落地，无法落地保留原坐标。禁区是加权与候选过滤，原端在全禁时仍可能回退边界，未擅自改成硬禁生成。
- 6336份地图几何新增原life/type=n坐标。MapFactory.loadLifeFromWz/loadLife使用x/y作为NPC位置，cy是独立字段，选点不误用cy；隐藏NPC同样属于源端采集范围。地图源SHA256随原导出保留。
- BotTownSpawner迁移EnvironmentManager.spawnTownWanderers：每城min(2,wanderers)，主图portal0锚点，逐个生成忽略环境配额，最多5次重试、200*attempt毫秒间隔，整城结束才SetAndStartBots。已挂到BotRuntime生命周期，**完整三波环境启动尚未接通**，没有只启动第三波并声称全环境完成。
- BotMapEntryResponder按MAP_ENTERED通知同world/channel/map假人，150..700ms抖动调用原NudgeSoon（1500ms限频、交易/停止状态不唤醒）；会话持有订阅并退出释放。真人换图已发事件；假人抵达观察图的原直接调用点仍待完整接线。
- FMBot原updateState只执行MarketBotAmbient，RESET设置房间后进入AMBIENT，所有旧购物状态也直接跳回AMBIENT；旧浏览购买调用链不重新启用。FM_BOT已映射具体FMBot；17枚举当前3类具体、14类Generic。保留公开shouldPurchaseItem估价辅助函数。
- MarketBotAmbient实现REST/WALK/SIT/CLIMB生命周期、72/坐姿偏置/86/93/97分支、市场共享路径预算、坐着聊天和附近220x80px假人聊天、原录制微转身、12..28秒坐姿、120..280px散步与换层导航、8种坐骑及1004骑乘技能状态。**KeepsShopChair仍待实际商店服务生产者；骑乘/坐姿/发言输出仍待前端消费**，不可称游戏可见完成。
- MovementCommands.walkAlongX迁移为BotRecordedActions：单bot占用、落地修正、10px/160ms封包、距终点<=20px或18步停止、双命令停止、交易提前退出，换图/移除取消旧回调。该逻辑与GCMovement物理导航是源端并存的两套入口；尚未完整迁移所有MovementCommands锁调用者。MarketBotDirector已使用封包步行占用门控。
- 最新实际后端结果368通过/0失败；新增FM、封包步行、离图清理、估价、城镇配置/选点/生成测试。测试FirstChoiceRandom补齐双参数Next覆盖，避免散步长度/房间号依赖系统随机；生成衣物断言按源HasClothing接受-5或-6。市场改动后Unity编译曾通过0警告/0错误；最终聊天改动后检查见末尾。NativeDamageLines的total局部变量与参数重名导致既有全脚本编译失败，仅重命名局部变量为totalAmount解除阻塞。

## 本地聊天入口接线

- BotRuntime持有Dispatcher与QueueCleaner，在会话退出取消并清空消息；地图角色使用共同入图序号排序，避免先遍历真人或先遍历假人改变原名字子串匹配的消费结果。GameServer将NativeSocialService已通过校验的All频道发言送入primary并发布CHAT_GENERAL；私聊/组队等频道不进入地图分派，斜线内容跳过。这是本地transport集成适配：当前GeneralChatHandler.java未发现原PRIMARY入队调用点，不能把新增适配描述成已找到了该源端hook。
- BotStateMachine.DisplayCommands保留原GuideTalk的单名字提示；FMBot覆盖为聊天选项。前端GuideTalk/Hint/Chat消息的显示消费尚未接线。
- 新增实际会话回归：聊天入队、斜线排除、每两秒分派、真人相同对象成为社交Respondant；此前370通过/0失败。补核TownPresenceSampler的pin调用实际是findGroundPoint（包含上探26px）而非仅Below，已修正并新增边界回归，最新后端371通过/0失败。最终Unity检查首轮被同时变动的BossUnit.TakeReplayDot缺失阻塞，随后该接口已出现；重检又被AdminRoutes.cs:68的AdminCtx.QueryLong缺失与:80的object.ContainsKey阻塞。故最终全脚本检查未通过，不能沿用前一轮0错误结果。未修改这些后台接口。

## 假人地图纸娃娃接线（2026-10-27续）

- `BotAppearance.CharacterIds`按源`org/gms/util/PacketCreator.java:addCharEquips`两阶段投影普通/现金槽；现金`-111`武器独立覆盖且置配方末尾。仅将当前WzCharacter支持的身体、头、脸、头发、帽/衣裤/鞋/手套/盾/披风/武器交给原IMG合成器；饰品和完整vslot遮盖尚待迁移，不声称完整纸娃娃。肤色Body=2000+skin、Head=12000+skin。
- Java身份：PacketCreator SHA256 `2da68878b0c5127ca4e1a9afd7630925ebee91f5fc621981a066b6ca0df9b474`；`constants/game/CharacterStance.java` SHA256 `46c061db6fd6f88295a4aaba4e479119c8e0fadefd800f4a255eb7a05a2a9530`。stance 2/3 walk、4/5 stand、6/7 jump、14/15 ladder、16/17 rope；偶数右奇数左。未核实的编号不猜动作。
- `FieldDirector.TickBots`同步当前玩家真实频道，并接`BotField.Synchronize`；按地图/world/channel维护独立GameObject和原SpriteAnim，坐标x/PPU、-y/PPU，脚底/部件连接点/z与帧delay复用WzCharacter。外观recipe不受本机Profile装备覆盖；外观更新显式强制重播防止SpriteAnim同动作短路。静止绳梯暂停、离图/不同频道/组件释放清理。排序300沿用现有人物默认层，完整原地图page遮挡仍未迁移。
- BotOutput新增Published事件（Poll队列保留），地图显示订阅原表情编号；挥击、技能和其他输出仍待消费者。原BotAttack.basicSwing采用BotAttackData的随机bodyAction及固定速度4，不能用本机玩家普通攻击配方冒充。当前actor末位置直接同步，不声称完成原客户端移动封包插值。坐姿仅复用sit动作，尚无椅子/坐骑原图显示。
- 后端新增现金覆盖、肤色、普通武器恢复回归，373通过/0失败；首次全脚本编译0警告/0错误。`BotPresentationChecks.Run`经真实Unity batch执行通过：原IMG合成、脚底/PPU、八种动作无站立回退、朝向、绳梯停帧、表情入口、现金武器换配方、跨频道实体清理。它是Editor实体/资源烟测，不是Play画面或Android验收。最终后端374通过/0失败（新增同图换频道身份回归）。未构建APK。

### 显示逆向入口（已确认资源，布局仍待核实）

- EXE身份仍为`1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`、ImageBase0x400000，使用`tools/inspect_beidou_client.py --strings`和`--start/--end`复现。字符串1388=`UI/ChatBalloon.img/`，真push调用点0x48A963/0x48B530，调用GetString 0x79E805。0x4890C4为九宫格资源处理函数入口（mov eax,0xA8024D; call 0xA60B98），0x4890ED开始取nw（字符串1394），后续c=1399、arrow=1403；0x48AE18读取clr=1409。0x48A935为气泡资源选择/构造入口之一。**尚未确认完整文字区、角色偏移、超时与调用者合同，不能据此自绘。**
- `UI/ChatBalloon.img/0`原IMG：nw/ne/sw/se均6x6，origin依次(6,6)/(0,6)/(6,0)/(0,0)；n/s为12x6，origin(0,6)/(0,0)；w/e为6x14，origin(6,0)/(0,0)；c为12x14，origin(0,0)；arrow13x13 origin(1,0)；clr=-16777216。复现`rtk proxy python3 tools/imgdump.py UI/ChatBalloon.img 0 --depth 3`。
- 姓名皮肤字符串1303=`UI/NameTag/%d`，真正push位于0x5F04FE，0x5F0504调用GetString，0x5F051E进行格式化；该分支检查参数type=1000。普通姓名无皮肤分支/文字绘制/角色锚点尚未确认。其他DWORD xref存在大量call操作数字节假阳性，不能把所有xref都当字符串使用点。

## 市场商品、装备备货和卷轴成本续迁移（2026-10-27）
- 纠正导出语义：源YamlBeans 1.17无类型`YamlReader.read()`返回字符串标量，`01002026`保留原拼写，后续`Integer.parseInt`按十进制得1002026。PyYAML safe_load误作八进制263190。现统一BaseLoader，保留929份原base64/SHA；`export_solomapling.py --verify-only`同时核对YAML结构和字符串标量，已验证929份。源Town二进制仍仅保留原字节，未虚构解析结果。
- MarketItemPool按原六个池的顺序建索引，后项覆盖；版本取不超过当前版本的最大键，默认55。卷轴四抽、药水/ETC/技能书三抽、椅子一抽保留循环tier变更与首ID去重；飞镖两抽每次从传入tier独立分配且不去重，v39前不生成诅咒卷轴。数量取原itemQuantities闭区间，缺失回退5。
- MarketEconomy保留12个两小时指数、星期/随机/房间/门口溢价及每步Java整数截断；clean number原源码是整数除法，不擅改四舍五入。市场波动和价格风格已按源代码迁移，尚未接实际市场店铺服务。
- MarketScrollPricing迁移ScrollInfoManager原支持映射与版本门控。最高差值同值时保留ComparisonResult以差值为Map key后写覆盖（watt、matt、str、dex、int、luk，最后者胜出）；不添加原端没有支持的武器。强化成本表按(cost+卷轴价)/成功率推导，跨成功次数取最低、修正异常后非首档乘0.6；不存在属性档回退1，超过int上限饱和。
- ScrollPatternGenerator在游戏版本40切换分布；第一组不按槽数裁剪。源ScrollGivenItem调用scrollEquipWithId(...isGM=true)，原迁移SQL默认use_perfect_gm_scroll=true，故必成功且不扣次数，最后统一清零槽。关闭时保留scroll_chance_rolls及失败消耗配置；源循环忽略诅咒返回null，不擅自销毁其仍持有对象。增益取原IMG并按short上限处理，强化次数写equipmentData.level。
- MarketEquipmentPool迁移YAML类别无放回抽索引后恢复源顺序、按模式重复、不去重装备；热房S模式2/2/1/1。缓存商品最多15次尝试取4件，过滤现金/不可交易/任务/职业/性别后取等级窗口和最低WZ价；显式LoadDesirable保留原启动加载时机，不提前调用。低于等于50的原WZ价按源getWzPrice替换5000000，再按职业估价。缓存上品随机化只改变源指定非零属性；ForJob合并原五职业YAML与缓存商品。
- 验证：后端399通过/0失败；新增链路覆盖实际原IMG强化和估价、YAML备货、缓存筛选；929资源原字节/哈希/YAML语义核对通过。纯Server/Bot无UnityEngine。商人/NX/卷轴交易状态机、三波环境、真实店铺和显示交互仍未接完整，不计入已完成bot类型数。没有新增UI布局或EXE逆向结论，没有APK/Android验收。

本次依据身份（相对soloMapling/）：

| 源文件 | SHA256 |
| --- | --- |
| `FreeMarket/FMEconomyManager.java` | `8bb45d02674b8d429d0926b5da27e4b80d96488b53794701e15c279cfc32f429` |
| `FreeMarket/ArtificialShopGenerator.java` | `dd772d749fb15fd9accbf8c58aef0497e23a1a6178c187eb00fa9d7e6bd69ea9` |
| `FreeMarket/EquipListGenerator.java` | `c779f7745141b98e8d50b92a36b89f7aa591fbfe6c3a97debe032e9c8378ca02` |
| `FreeMarket/FMItem.java` | `c89b327b78c301d29c9f51b91ccba0c178f88c920bcca217197f13ca7b20dcde` |
| `FreeMarket/FMEquip.java` | `11b7bdbc52c3b2d7be42e8e8f266e1f98db7dea395f0d9dcb7d99d4152173d1f` |
| `itemPool/ItemDatabase.java` | `418fff5e94d20acb76880233150295d2ff3cbae207b0412e3a4c0b7bcb816eb7` |
| `itemPool/ItemSelector.java` | `914b41a76cc2f9e8b4a61b09b2eecbcdd41fd5452fed88743796db7cc94c4a16` |
| `itemPool/QuantitySelector.java` | `dc4bb52eec175ca565643ca67f187681a92976b11b4dc4c0096a1fd0a07fa512` |
| `itemPool/ScrollInfoManager.java` | `96dd8103e2a7c45af5ac50dce3a1a472121e96e484c56c98490e76b49f67dd03` |
| `itemPool/ScrollPatternGenerator.java` | `a69886f3e1629214ce9660208798259acaa720740e52dd3d3fd8220d3176c854` |
| `itemPool/ScrolledItemComparator.java` | `34f19ea90670ea9f1ccb8a0e08edc11a2b98f244c89971a5097da97823317778` |
| `itemPool/ComparisonResult.java` | `596450d4f9d53dafe9711b6e301a469de7c45f01f0853734010ef28d1fe556b0` |
| `itemPool/UniqueStatBonusList.java` | `3af5f887f0e8f492db7f868d4cbdb31127a2033eb518d8ad6a4fe0e53659f016` |
| `itemPool/UpgradeSimulator.java` | `3c64f153992d9a34bbeb5d74e30aaf48a302ab460f37f55f4643a214fb3a2084` |
| `itemPool/ItemUtilities.java` | `15f0c8722647d0c906dcc26cf7febafc2c7200b44a20a5ff813ff4cd2a6b5a34` |
| `itemPool/DesirableEquipList.java` | `e3c68aaa443ec4de1bea4956d55f06c42d3f95089729642fa5300c7dbdb95755` |

最终复检：新增ForJob及低WZ价回退之后，后端399通过/0失败，全Unity脚本编译0警告/0错误；仍非完整系统交付。

## 商人业务、信使、队伍状态与攻击显示接线续迁移（2026-10-27）

- Selling/Buying/NX三个枚举改为具体FSM，当前6/17具体、11/17仍Generic。商人按原商品池、先递增索引、买入80%..90%/卖出90%、叫卖概率和共享市场调度运行；NX原占位道具4031865×100、5000万报价、15轮转换。未接原交易UI、商城店铺和全部市场业务，不称完整商人系统。
- 新增BotTradeProtocol并接ITransport/本地运行时：同world/channel/map校验、接受前禁止托管、真实库存/金币托管、锁定和取消。真人离图/换频道/退服及运行时释放取消交易并持久化返还。失败自我邀请不再残留单边session。**客户端存档快照与托管库存权威关系仍需进一步接线，不能称生命周期完成。**
- BotMessenger提供三人房、首邀请、接受/拒绝、消息序列、typing和离开；NativeSocial响应实际成员/消息。NX成功后按源15次(0..28秒，每2秒)等接受，3/7/2/2秒交付并离开；停止取消待执行回调。原同步flavor对白的等待用BusyFor原duration保留，不允许后台FSM提前推进。邀请超时和原信使界面尚未完整。
- NXCodeManager原随机码字符集、3×5段和数据库两表接线；建表使用DbStore支持的声明。该本地解析器把TEXT DEFAULT NULL误读为字符串NULL，retriever采用隐式nullable。源注释称type4 prepaid不可信：CouponCodeHandler实际type4发nxCredit，type2才prepaid，现按实际handler发10000 credit并拒重复兑换。没有迁完其他兑换类型。
- BotPartyService接NativeSocial与BotRuntime的共同成员状态，世界队伍序列从1000000001起，最多6人、低等级配置、首邀请、假人只有队长可发邀请、普通离队/队长解散及退出清理；NativeSocialWindow保存真实partyId，取消旧角色ID冒充。移动profile读取实际队友，60级飞侠疾行加入/移除生效。BotPartyLogic队列仅OPQBot源调用，未擅自给所有bot自动接受。
- BotAttack使用普通-11武器动作桶、固定速度4，BotFlavor SkillSwing生产真实Attack输出；BotField按自身最后同步map/world/channel过滤输出并调用PlayNativeAttack，不依赖单例FieldDirector才可显示。真实Editor IMG烟测覆盖五个单手挥/刺动作、事件订阅和SetBase不打断oneShot；原移动测试加强为检查Resolve实际无stand回退。Edit模式需显式触发OnEnable，不能把Edit激活差异当运行时攻击失败。
- PartyGrindFloors迁移动态float平均、Java round、80px带稳定聚类、floorMod、无平台±400px回退和地面站位钳制。GCMovement.groundPointBelow实际调用BotPhysicsEngine.findGroundPoint（含上探26px），**不是仅PointBelow**。updateStrDexIntLuk(primary)源AbstractCharacterObject:692明确给四属性同值。五人召集、实际怪物选择/伤害归属、技能表现、30分钟watch尚未接完整，Companion仍不计具体类型。
- 最终本轮验证：后端425通过/0失败，全Unity编译0警告/0错误；真实Unity batch BotPresentationChecks PASS（原IMG/脚底/移动/表情/攻击输出/现金覆盖/频道清理）。未构建APK，未完成Play或Android验收；系统继续标为incomplete。无新增EXE布局结论，姓名/气泡/交易窗不凭猜测绘制。

本轮源身份（SHA256，相对soloMapling）：

| 源文件 | SHA256 |
| --- | --- |
| `ArtificialPlayer/BotTypes/SellingMerchantBot.java` | `610d9be7f317760e26f21bc67119a8160fd9318030e607b29a4b35eb06bdcd6b` |
| `ArtificialPlayer/BotTypes/BuyingMerchantBot.java` | `bb32cc2d7318ebb5fb3d54a7c621ac9ec1622dfc6b602232bd6889cbf28d958c` |
| `ArtificialPlayer/BotTypes/NXMerchantBot.java` | `90b1fb854168726123f8bc7b49c296053a9cf35735cd7a636b4897fc57d14a64` |
| `ArtificialPlayer/BotCommandsPack/MapleMessengerCommands.java` | `d3356726305bb71a6e3a8d726a4bc07ee52ff7f5062b677453349dd455e08be6` |
| `server/NXCodeManager.java` | `8ca70e9490d500abc8bd730164a708e385aecc5615687ec1b42d9e87b8ecabd5` |
| `ArtificialPlayer/BotCommandsPack/BotAttack.java` | `0d395ba2cb5174af07da32d289294dfc92fdc242b099cca21ee4c0c8182d9a1d` |
| `ArtificialPlayer/BotAttackSystem/BotAttackData.java` | `7c711a90f52d554ac953236b304a80678010280c8cf7f906314a0134df9d759e` |
| `ArtificialPlayer/BotPartySystem/BotDamageModel.java` | `cb5cb5e3a55f13f6fb42b95ac044312345ff139754ea05c3e13d78c9faa6e103` |
| `ArtificialPlayer/BotPartySystem/PartyGrindFloors.java` | `aa65de362e0072b18d3153f156258947e1e409a9cdc27afb25e087d97fd1a9c8` |

追加真实成交回归：取消后重新邀请、原报价金币托管、双边确认、商品实际入包与重读存档均通过；最新后端427通过/0失败。以上没有消除交易UI和快照权威生命周期缺口。

## 基友战斗、卷轴商人和交易快照继续接线（2026-10-27）

本节更新前文历史数量：目前8/17具体类型，9种仍Generic，系统仍为incomplete。此前“仅保留资源”“接口存在”不作为完成依据。

- 基友道具原`item/consume_2431159.js`不改写：`im`绑定原脚本宿主，读`Item/Consume/0243.img/02431159/spec`的npc9900001、script consume_2431159、time1800000、pdd1。源`Character.startPartyGrindCompanions`要求真实在线、已有仅自己一人的队伍，召集5人后才扣道具。已接实际NPC道具入口、五人FSM、30分钟watch和效果清理。
- CompanionGrindBot原220ms轮、800ms攻击冷却、同图120×120接敌、280px队长横向约束、560px选怪范围、排除Boss/fake、同队认领及楼层分配已接。源resolveLeader从**假人当前频道**CharacterStorage查队长，因此换频道/世界应解散；同频道换图跟随。真实回归覆盖换图、跨频道、离队、退服、到期清理。四属性全相同来自AbstractCharacterObject.updateStrDexIntLuk，不能推断HPMP已完整重算。
- BotAttack.resolveBossAttackSkill转写可用`tools/export_solomapling_attack_skills.py --source <Java根目录>`复现；Job.isA、技能常量、普通武器兼容/回退、分段最少1保留。ItemInformationProvider.getWeaponType:648..654的134/135/136/139类别为NOT_A_WEAPON，不能误判近战。当前真实地图伤害接EnemyUnit，按实际扣HP记录贡献及治疗总量；原IMG技能effect有显式帧生命周期，含单帧不残留。**远程/魔法目前仍用普通挥击，未完成原动作与投射物合同**。
- Monster.distributePartyExperience:558起按实际队伍贡献/MVP、等级门槛、等级总和、common0.8/MVP0.2和每人5%队伍加成，个人/队伍经验分开Java round。非攻击且等级低于怪物-config5不合格；经验/任务要求活着，familyRep既在distributePlayerExperience末尾又在distributePartyExperience循环内调用，**组队每名合格成员两次、单人一次**；两处均在存活经验检查之外，因此合格的死亡成员仍可获声望。FieldDirector先Read倍率、判资格，再决定RecordKill；不再让不合格队长获得声望/任务或整份怪物经验。完整状态倍率、掉落归属仍需迁移。
- ScrollingBot源listIndex从未递增：始终处理生成列表第0件；预先强化的level作为成功次数、原干净装备tuc作为槽数，Fisher-Yates随机排列显示结果，**不会在播放时再强化装备**。失败调用VFXCommands.botScrollFail实际发送CURSE，非FAIL；20%原对白，随后10..15秒等待，10%物品喇叭，原装备估价/价格修正，5..8分钟后下一批。C#等待主线程续接；原无货while改为下一轮重试避免卡住Unity。Scroll/ItemMegaphone保留结构输出，原UI消费者尚未迁完。
- InviteCoordinator.runTimeoutSchedule先检查计数>5再递增，Server:789每30秒首轮及周期。因此第6轮后仍存在，第7轮移除，不用固定180秒近似。队伍/信使共用运行时30秒轮并测试边界；普通信使邀请复用已有房间，NX完整邀请仍新建房，真实最多3人。NativeSocialWindow只更换已有布局中的人数/队长/成员状态与250ms刷新，没有新增猜测布局。
- BotTradeService提交事务后发布真实库存和金币delta；GameSession只同步这部分镜像，保留经验任务。FullCharacter.tradeRevision是**本地会话适配字段**，不新增原数据库列；SaveCharacter及真人帧同步拦截旧交易快照，防止托管金额补回或取消返还被旧金币覆盖。原CharService.Save本就不覆写inventory表，不能错误宣称新增背包全快照落库。真实交易UI、完整交互与客户端运行验收仍待处理。
- 验证检查点：后端464通过/0失败（含成交后旧快照保存保护与权威背包同步），Unity全脚本编译0警告/0错误。真实UnityEditor BotPresentationChecks通过原IMG技能effect及过期检查，以及原Mob IMG创建的NativeMobController真实扣血、伤害归属和旧频道攻击拦截；不是实机或Play验收。NPC源目录实际在项目外层MapleDashResources；889脚本parseFail0，768执行、48宿主接口运行失败、73Java互操作；2777打包脚本parseFail0，不能称全脚本业务通过。

源身份（SHA256）：

| 文件 | SHA256 |
| --- | --- |
| `soloMapling/ArtificialPlayer/BotTypes/ScrollingBot.java` | `3628052a016524d653da81def7db10f8fdd01260d993b3cec5b4106a2ced40bc` |
| `soloMapling/ArtificialPlayer/BotTypes/CompanionGrindBot.java` | `3326a2c26a7d72860922f55547fb05627067134d1d43400a42228fb25a3de92d` |
| `soloMapling/ArtificialPlayer/BotPartySystem/PartyGrindService.java` | `881e0634bd3a397cf925abb704fcf9108b65dad45187aa7399cf619ce98f82f4` |
| `org/gms/server/life/Monster.java` | `5ba9c02cdcb3becee14439c7d0b2a9a6b2576fe9c9cec95139e012460a80a566` |
| `org/gms/net/server/coordinator/world/InviteCoordinator.java` | `ce9332f9403e0f344a2fd7127544be7a8e1bede2ed8b551682da52635cf0e3d3` |
| `org/gms/server/ItemInformationProvider.java` | `86b777480f979d7c15c469dc43e745fd10e7ecc551ba79216f54be9c2ef81ee8` |

原SocialCommands.BotFullChat(mode0)与BotChatbubble(mode1)有明确区别；BotField现将Chat输出接入现有BottomBar聊天记录，Bubble仍不写聊天记录。
2026-10-27 续：Chat 与 Bubble 现在都冒原版 `UI/ChatBalloon.img/0` 头顶气泡（5秒、末0.5秒淡出），Chat 另外进聊天记录。九宫格尺寸、资源节点分派（type 1000/1001 默认、1002 pet、1003 adboard、1004 mob、dead）与 5000ms 显示期已核实，**角色锚点仍是待核实项**（当前按脚点上 84px），详见 [传送门/名牌/气泡](field-portal-nametag-bubble.md)。

### 气泡调用链续查（同一EXE身份）

新增只读复现入口`tools/inspect_beidou_client.py <exe> --callers 0x48a935`；结果是相对CALL候选，须反汇编核对边界。已在原汇编确认0x93746C..0x9374D0调用链：向0x48A935传入文本对象、角色+0x1150和+0x1140对象、立即数0x1388(5000)、type0x3E8(1000)、0x9375F2返回值及0。另0x978B2C..0x978B97有相同5000/type1000路径。**目前只确认这些实际传参值；未完成参数用途、角色锚点及持续时间字段的对应证明，不能把5000直接当所有聊天气泡寿命**。0x48B133/0x48B121分别传递其参数+0x20/+0x18，0x48B154调用九宫格函数0x4890C4。后续从0x48B2A7..0x48B4E8及0x4890C4的返回字段继续核对，不能采用错误EH反编译局部变量偏移。


### 玩家范围查询与卷轴效果消费者（2026-10-27 续）

- 移除`BotLogic.WaitForPlayerInRange/GetRealPlayersInRange/IsNpcPresent`恒空/恒false占位，接BotRuntime真实在线角色及MapRuntime.npcs。源`BotLogic.java:37..63,119..126,193..207`：等待接口只排除自己，**可以返回另一假人**；真人接口另排除bot。限定当前地图/世界/频道，按地图入场顺序返回。`BotHelpers.createRectangle:137..155`采用整数halfWidth/halfHeight、`int(height*.2)`，top=`centerY-2*halfHeight+offset`；java.awt.Rectangle含左上、不含右下。C#使用地图像素整数坐标；没有将宽高当半径，也没有擅自把脚底设成矩形中心。掉落查询仍为占位，未因此宣称BotLogic完整。
- 源身份：BotLogic SHA256 `d42a0cd656da92ddd2ae53e871c8180e96b1fd8cdbbb1c0f202bdac90ad874bd`；BotHelpers SHA256 `47e0b851104cd62d0a1a6cafd0bbbb1c97326e393504c4a00f1d3272cb84556e`。
- 卷轴源VFXCommands发送`getScrollEffect`；PacketCreator:2507将SUCCESS编码为第一bool、CURSE为第二bool，非把枚举整数直接当成功bool。原EXE（本节沿用SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`、base0x400000）`0x933573..0x9335A8`依次读四个byte；`0x933917..0x93395E`以第一个byte及legendarySpirit传`0x43B736`，取角色+0x11A4运动向量（`0x4AD42B`）和+0x1150层对象。CURSE不单独选第三种动画。
- `0x43B75A..0x43B7A8`：success非零读字符串3439=`Effect/BasicEff.img/Enchant/Success`，否则3440=`Effect/BasicEff.img/Enchant/Failure`。`0x43B93D..0x43B979`向通用层加载`0x43E86F`传不翻转、alpha255、z3、原层与运动向量、x/y偏移均0；不添加自定义头顶偏移，位置由IMG origin决定。`0x933896/0x9338B4`选字符串1246/1247=`EnchantSuccess/EnchantFailure`，`0x9338D2`音量100。迁移以现有纸娃娃运动根作父对象、现有前景特效排序360适配Unity排序；不是原EXE z值与Unity sortingOrder的直接同值映射。
- 原`Effect/BasicEff.img` SHA256 `91501003f961bdcbe368abf2c3ed104754c676da0c4af58e3b6427cfa70bd57b`：Success21帧，Failure16帧；首帧均30×26、origin(16,130)，故无需另抬130px。所有帧原delay50，Success13与Failure14未写delay，沿既有原IMG播放器默认100。BotField消费Scroll事件，成功/普通失败/诅咒分别选原节点，一次播放后销毁；换图/频道清理父对象同时清理效果。ItemMegaphone仍缺原UI消费者。
- `tools/export_sound.py`加入原Game/EnchantSuccess、Game/EnchantFailure；已定向导出源Sound/Game.img的MP3有效负载并逐字节比对，更新SoundIndex。预加载并固定pitch1播放，无随机音高。batch烟测不等于音频设备听音验收。
- 只读复现：`tools/inspect_beidou_client.py <exe> --callers 0x43b736`（实际0x93395E、0x7F69DC），再`--start 0x933560 --end 0x9339c0`、`--start 0x43b736 --end 0x43b990`。原IMG可使用源tool/wz-python解析`Enchant/Success|Failure`，不可用像素截图猜origin。

### 气泡持续时间字段确认（同一EXE身份）

补充上节未确认项：`0x48B3F1`取`0x987257`时钟并存this+0x40，`0x48B3FC`将参数+0x14存this+0x44；`0x987257`读取全局[0xBE7B38]+0x18。`0x48E6D4`更新在层this+4存在且this+0x54为0时计算elapsed=`now-this[0x44]-this[0x40]`。elapsed<=0保留层（除非更新参数非零要求移除）；超过显示期后，this+0x64为0或剩余淡出时间<=0则清层，否则alpha=`remaining*255/fadeDuration`整数除法。`0x48E846`将负淡出时长钳为0。因而已核对普通聊天调用的5000是显示毫秒数，但构造默认淡出时间及完整文字几何/头顶锚点仍待核实，不能把这些字段扩展为所有气泡类型统一规则。

本次续迁移验证：后端471通过/0失败（新增范围及NPC真实会话回归7项），全Unity脚本编译0警告/0错误，真实Unity batch BotPresentationChecks PASS，新增成功/失败/CURSE的原IMG消费者、首帧sprite pivot(16,-104)、运动根锚点、不翻转及过期清理断言。音效仅比对原字节并接播放入口，没有声卡听音验证。具体类型仍8/17；本次没有以新的接口冒充已完成业务类型，整体不作完整交付。

### 假人主动交易邀请接受接线（2026-10-27 续）

- 源`org/gms/server/Trade.java` SHA256 `edf758a4b7b97b0fcbee152a8905d7a6deabb2a883e86ec5b96f16bc8c72eb44`，inviteTrade:519..527建立双边会话但不设fullTrade，visitTrade:537..559仅接收方有效邀请可设双边fullTrade；过期访问取消双边。InviteCoordinator第七轮移除邀请，不主动清Trade会话。
- BotTradeProtocol新增末尾枚举Accept，保留既有操作编号；Read暴露incomingInvitation，Accept校验当前邀请方ID与接收身份，拒绝发起方自行接受，避免旧窗口接受另一假人交易。BotTradeService追踪incoming/pending及原30秒计数，同现有party/messenger运行时钟。重复Accept对已打开会话不重复消费邀请；过期Accept取消双边，取消/成交移除邀请记录。**这是实际transport业务入口补齐，原邀请窗口/交易窗口UI仍未迁完**。
- 真实runtime/transport回归新增7项，覆盖主动邀请读取、错误对象不消费、真人接受、发起方拒绝、第6/7轮边界、过期visit取消。最新后端478通过/0失败；继续保持整体incomplete。

### 地面掉落权威服务（E03）源端依据（2026-10-27 续）

源身份（gms-server，均以本次核对时字节为准）：

| 源文件 | SHA256 |
| --- | --- |
| `org/gms/server/maps/MapItem.java` | `eccf0e4072f3500fbf6f0c27106255a2099126c1c0b47395a078d135a59dafbc` |
| `org/gms/server/maps/MapleMap.java` | `435c938a6e387e9c060339631c9fd0e7d8dd8d71547b3d21ca9f0e90ffccabe4` |
| `soloMapling/ArtificialPlayer/BotCommandsPack/DropCommands.java` | `aa83c0b927837700ca2ef0f9e9c980bdf23a6a0c8a93b157ea4b30376fbe1642` |
| `org/gms/client/Character.java` | `b72a138f09a8dadbb88faad54c2f14c1668d74608f1a59b1c394af55b0bce62a` |
| `org/gms/client/inventory/manipulator/InventoryManipulator.java` | `defa04b22c7989773d73b65be6a506de899c6dc9afc5a1ee27b3b1fe534dbc21` |
| `org/gms/constants/id/MapId.java` | `b759524c980df8f76075c3da320ccb53baaacc35ac48c4611bc7172d2399e680` |
| `org/gms/constants/id/ItemId.java` | `c684f078f266ffbc11d990a7797c60928ce3e143897a43b1688782b64624fdf3` |

确定逻辑（已在 `Server/Field/MapItem.cs`、`MapItemService.cs`、`MapItemPickup.cs`、`Server/Bot/DropCommands.cs`、`BotActorLooter.cs` 落地）：

- **`setPermanentOwner(true)` 不产生拾取限制**：`MapItem.canBePickedBy:135..157` 第一行 `if (character_ownerid <= 0 || isFFADrop()) return true;`，而 `isFFADrop:127..129` = `type == 2 || type == 3 || hasExpiredOwnershipTime()`；`hasExpiredOwnershipTime:131..133` = `now - dropTime >= 15_000`。归属窗口一过期即被第一行短路，末行 `return !permanentOwner && hasExpiredOwnershipTime();` 在该结构下不可达（`hasExpiredOwnershipTime()` 为 false 时恒 false）。全仓只有三处写、一处读，无其它消费者。**结论：字段与三处 `setPermanentOwner` 调用点照搬保留，不改成「永久只给归属者」**；DropCommands 的 `botThrowItemNoExpireOwnerOnly`/`botDropItemQtyOwnerOnly`/`botThrowItemToOwner` 的实际限制来自 `dropType 0`（15 秒归属窗口），不是 permanentOwner。
- `canBePickedBy` 的「同队收养」写回：`party_ownerid == -1` 且 `chr.isPartyMember(owner)` 时把 `party_ownerid` 改成 `chr.getPartyId()`；`party_ownerid != -1` 且 `chr.getId() == owner` 时同样改写。是**有副作用**的谓词，不能当纯函数实现。
- `MapItem.getItemId()`:92..97 —— `meso > 0` 时返回**金额**（packet 语义），道具才返回 `item.getItemId()`。过滤/押注字符串都用它，金币掉落不会被按 itemId 过滤命中。
- `MapleMap.pickItemDrop:1187..1202` 只广播 + `removeMapObject` + `setPickedUp(true)`，**不入背包**；入背包在 `Character.pickupItem:2281..2408`。DropCommands 的 `botLoot*` 全走 `pickItemDrop`（假人「捡」= 让地面物品消失，Gacha 回收/Dice 赌注清理/Blackjack 牌面回收都依赖它），与真人拾取是两条路径，不可互相替代。
- `Character.pickupItem` 的拒绝分支：`now - dropTime < 400 || !canBePickedBy(this)` 只 `enableActions(); return;`，**不发** `showItemUnavailable`；发 `showItemUnavailable` 的只有「`isPickedUp()` 竞争」「`isSelfLootableOnly` 地图上的他人 playerDrop」「`!needQuestItem`」三处。
- `getMapObjectsInRange(Point from, double rangeSq, types)` 的第二参数是**距离平方**（`from.distanceSq(pos) <= rangeSq`）。DropCommands 调用点传 1000/3000/9000/10000，`Documents/Dev Commands Cheat Sheet.md:70..71` 佐证 `botLoot(fakechar, 1000)` = 脚边、`9000` ≈ 2.5 角色宽。**不要换算成半径**。
- 落点：`calcPointBelow:553..567` 的三角插值 `s5 = cos(atan(s2/s1)) * (|dx| / cos(atan(s1/s2)))` 化简为 `|dy| * |dx| / |dx_total|`，即线性插值，与 `MapRuntime.PointBelow` 等价。`calcDropPos:620..635` 先夹到 `xLimits`（`generateMapDropRangeCache` 用 `bsearchDropPos` 从 mapArea 左右边界二分，内收 14px），再 `calcPointBelow(x, y-85)`，失败走 `bsearchDropPos`，落点跑出 `mapArea` 则回退 fallback。`calcItemDropPos:655..659` 仅在 `playerDrop && mapid == MapId.FM_ENTRANCE(910000000)` 时改用 `calcPlayerDropPos`（`y-6` 采样）。
- `instantiateItemDrop:1046..1070`：`getDroppedItemCount() >= GameConfig item_limit_on_map`（migration 默认 **200**）时从 `registeredDrops`（弱引用 FIFO）逐出最早的；`spawnItemDropNoExpire:2567..2596` **不调用** `instantiateItemDrop`，因此不会被逐出，过期完全由调用方安排（`DropCommands.botDropItemWithExpiry` 排两个 `TimerManager` 任务：`expiryMs` 后广播移除、`+PICKUP_GRACE_PERIOD_MS(3000)` 后 `makeDisappearItemFromMap`）。
- `InventoryManipulator.addFromDropInternal:184..270`：`isPickupRestricted`（`info/only==1`）且非怪物卡只允许一件；非装备先按 `slotMax` 填满既有同 itemId 且同 flag/owner 的堆（`listById` 按槽位序），再开新格；可充能（`itemId/10000 == 207` 星 / `233` 弹）与 petId != -1 整份进单格；装备 `quantity` 必须为 1，否则发 inventoryFull + showItemUnavailable。`getSlotMax:371..403`：无 `info/slotMax` 时装备 1、USE/ETC 且 `item_slot_max > 0` 取配置、否则 100；有 `info/slotMax` 时值为 1 直接取 1，否则 USE/ETC 且 `item_slot_max > 0` 取配置、否则取 WZ 值。`item_slot_max` migration 默认 **0**。
- `MapId.isSelfLootableOnly:165..168` = Happyville 树 `209000001..209000015` 或 GPQ 泉 `990000500..990000502`；这些地图 `!playerDrop || dropper == player` 才能捡，否则 `showItemUnavailable`。
- `ItemId.NX_CARD_100 = 4031865` / `NX_CARD_250 = 4031866`；`isNxCard` 即这两个。拾取 NX 走 `gainCash(NX_CREDIT, (id==4031865?100:250) * quantity)`，不入背包。

平台适配（已记录，非源端语义）：

- `lootItemListOnFloor:225..232` 的 `BotHelpers.sleepAmountSeconds(100)`（= `Thread.sleep(100)`）改为 `BotTasks` 可取消时序链，保留「按列表顺序、每件间隔 100ms、可取消」。
- `MapItemService` 按 `mapId` 分桶（与 `MapService`/`BotRegistry` 现状一致），当前单频道；多频道需按 (world, channel, mapId) 分桶。
- 效果系统分支（`applyConsumeOnPickup`、`ScriptedItem.runOnPickup`、`needQuestItem` 深层、`CashShop.gainCash`、`updateAriantScore`）不自行发明数值，经 `BotLootHooks` 委托给上层，未接线即退化为直接入包。

验证：`tools/server-check` 509 通过 / 0 失败（其中地面掉落回归 31 项：落点、归属窗口、队伍收养、400ms 延迟、拾取入库、满包回退、botLoot 不入包、permanentOwner 原端短路语义、限时掉落宽限、item_limit 逐出、一字排开）；`tools/unity-compile-check` 0 警告 0 错误。**未做实机显示/听音验收**，整体仍 incomplete。


## Android 启动预热时机（2026-10-28，迁移实现）

- 本节是 Android 启动编排调整，不是新增原端逆向结论。资源校验成功后，`WzAndroidLoader.StartGameRoutine` 先调用幂等的 `GameSession.Boot`，在加载页挂载 `BotAppearancePrebake` 与 `BotPreload`，沿用原帧预算、stand 就绪后驱动生成的顺序及假人预热 45 秒兜底。
- 等两个组件完成并自行销毁后才调用 `CreateGame` 创建登录流程；纸娃娃等待 stand/walk/sit/jump 全部预合成完。Android 的 `GameLoop.CreateGame` 不再重复挂载组件，避免重置 Finished/StandReady；Editor 与其他平台保留原路径。加载页素材、布局、服务端假人行为规则未调整。
- 验证：启动顺序与 Android 条件编译分支的定向静态断言通过，diff 空白检查通过；未运行构建或 Android 实机耗时/流畅度验收。
