# 地图过场与事件实例（2026-10-29）

## 身份与原端证据

BeiDou.exe SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，base0x400000。
Shape2D.dll SHA256 `5e9b27d392b4a373fcb21d8b188c8b6864c1fd60fc33563ae24c782b26d97e43`，base0x51400000。
原文件均位于 Beidou-Server/clien；服务端位于 gms-server。

showIntro 发 SHOW_ITEM_GAIN_INCHAT subtype0x12，EXE `9386D7..93877F`。
调用 `439E00` 解析器时 basez=0xC00614A4，默认x/y来自玩家+11A4向量（403CB7/+20、403CDE/+28）。
`439E00..43A8C1`：type+0、visual+4、sound+8、start+0C=now+start；
type0 x/y在+10/+14，dx/dy是加法偏移，x1/y1在+18/+1C，duration+24，z+20=basez+节点z。
type2 field+38；type1有duration/width/height/probability；type3装备appearance1..51；type4 action；type5 sound；type6 x。

执行入口是 **43A8C4**，不是43A8C2；严格 now>start。type0→43ABDE、type2→43AA44→53035D换图、type5→43A935声音。
visual容器选择**一个随机数字子节点**作为帧属性传给4398F6，不是对子节点再循环生成多个图层。
4398F6→43EA3E 创建无origin/overlay图层，世界坐标；兼容源码
`tool/client-debug/weather-compat/src/weatherfx.h`103..113也明确无origin/overlay由引擎随camera移动。
初始x/y经439DA0→向量vtable+44；定时x1/y1经43AE59→vtable+94，结束时刻now+duration。

Shape2D向量vtable5140C0FC：+44→514075C9，无aux时把x/y加到向量+1C/+20；有aux转514075F5。
+94→514083CF..5140844D，无时间也做加法；有时间创建aux51407FE1→5140833B入队，
aux+10/+14存参数，+18/+1C为基准/结束时间。因此x1/y1是**位移**而非绝对终点。
aux构造5140806D设置vtable5140D8FC，+8→5140809F更新函数：51408122..5140813E使用32位imul/idiv，
位移=distance*(now-start)/(end-start)，向零截断；elapsed<=0不动，结束时直接加完整位移。
当前运行时已按此整数插值修正，并覆盖正/负非整数进度及端点断言。
复现：`tools/pe_module.py <Shape2D.dll> 514083CF`、`... 514075C9`；EXE用inspect_beidou_client.py --fn。

## 原始 IMG 与迁移

Effect/Direction3.img/goAdventure/Scene0、Scene1（男/女）：
cmd0 start0 y=-511，y1=722，duration10000，男/女visual；cmd1 start10000 y211同visual；
cmd2文字start10000 (0,0)；cmd3 type2 start14200 field10000。
地图0 VR=(-400,-300,400,300)，return10000，onUserEnter=goAdventure。

goLith/Scene0、Scene1：back1 start0 x1=50 duration3000 z3；cloud200/z2；light300/z3；shine0/z2；
男/女start0 (-400,200)，位移(400,-200)，duration4500/z3；Whistle4000；文字4500；field104000000/start5000。
图片按原canvas origin/delay/alpha，不自绘、不缩放；过场拥有输入锁并在换图/换角色清理。

NativeIntroTimeline、NativeMapIntro 接入type0/2/5，启动前预检全部图片/声音；其他类型显式拒绝。
NativeMapScripts 路由goAdventure/goLith，NpcApi.showIntro走相同内核。
地图0只有成功启动原过场后才关闭旧returnMap快捷兜底。
当前Unity sorting映射500+z用于过场高于地图/角色；原端全局z排序到Unity的完整映射仍未验证。
`tools/export_sound.py --direction-only` 从原Direction*.img导出20个原MPEG，SoundIndex新增原路径键。
原始音频在兄弟MapleDashResources/Data/Sound/Direction；可用该脚本重建。

## EventManager / EventInstanceManager 合同

源端Java同名类：每manager共用一个脚本engine；setup(level,lobbyId)→设置leader→注册玩家→playerEntry→afterSetup。
注册必须先建立membership；playerUnregistered在移除membership之前调用。
startEventTimer发秒数时钟，timeout先dismissclock再scheduledTimeout。
dispose回调先于disposed标志，之后清玩家/定时任务/地图。
**em.disposeInstance仅延迟释放manager中的名字/lobby，不等于eim.dispose**；
SQL V1.7.0__create_game_config.sql event_lobby_delay默认10秒。
getInstanceMap必须独立map manager，不得返回全局共享map冒充隔离。
resetPQ→resetMapObjects→clearMapObjects/restoreMapSpawnPoints/instanceMapFirstSpawn(difficulty,isPQ)，
包含reactor等状态，不能简化成清全局怪。
monsterKilled(mob,eim,hasKiller)在移除登记怪后调用；最后一个触发allMonstersDead；
monsterValue(eim,mobId)回调结果累计角色击杀计数。2026-11-01 已补怪物登记、击杀/末怪回调；monsterValue 的角色累计记账仍待迁移。

NativeEventScriptService 已接原脚本engine、属性、单角色startInstance、membership、
setup/entry/afterSetup、换图回调、schedule/cancel、事件计时/超时、dispose及延迟释放名字。
NativeEventScripts 按角色维持宿主，NpcApi cm/player/client暴露真实事件对象；
player.changeMap修正为使用捕获session，脱离NPC会话也能换图。
未支持API严格报错，不以空实现假成功。

**2026-11-01 更正**：运行时独立地图已通过 `NativeEventMap` 绑定 InstanceMap，原模板 ID 与内部 Scope 分开；怪物/HP/刷怪器、可变门/NPC、反应堆及掉落按实例隔离。`FieldDirector.StateId` 连接后端状态服务，脚本仍见原 ID；`resetPQ` 依原源端首次只生 mobTime==-1，显式 instanceMapRespawn 才补其他点。反应堆 reset 保留原对象身份并归零，释放实例才清池。详见十二项反馈 reference 与交接文档，不能等同副本整场已验收。
时钟包仍只有宿主回调，原图时钟UI尚未迁移。组队/远征队、lobby信号量/超时、掉线/死亡回调、
交通事件channel/map/船只状态API仍缺；Cabin.js与Boats.js确实存在，不可宣称全部事件脚本已可运行。

## 验证范围

script-harness selftest 28 passed/0 failed：原3rdJob_warrior.js不改源码，
测试宿主提供独立map fixture，覆盖进入/1200秒计时/超时退出105070001/销毁；
两个实例map handle独立；回调顺序、重复注册、取消任务、未知API报错、10秒后释放名字。
这不是Unity独立副本地图已可用的证据。
NativeGameplayNodeChecks 在隔离Unity exit0：男女goAdventure原图尺寸800px、严格起点/14200ms换图、
中途位移、输入锁和图片清理；男女goLith初始并行图层和声音索引；540飞行oracle。
NPC全量889脚本：769成功/47既有runtime fail/73既有JavaInterop，不是全通过；
资源包2779条/npc889/quest470/parseFail0/mangled0；未构建或验收APK。

## 2026-10-29 宿主与审计补充

EventManager原属channel，不应因没有渲染中的field就返回null。NativeEventScripts改为独立 `[NativeEventScripts]` 宿主（Play时DontDestroyOnLoad），换角色清理；事件引擎复用NativeScriptService的GameConfig/Extend/Gachapon显式Java注册，getChannelServer通过委托注入NpcApi真实channel桥。
getTransportationTime已按EventManager.java1262→World.java441→NumberTool.java36接实际world_prop.travel_rate（float乘积、向零截断/Java夹取）；未知world显式报错。未接交通船只状态及可视时钟仍保持缺口；独立地图已在2026-11-01补齐基础接线，边界见上节更正。

上节889/47/73为旧Mock统计，不能视为120个已确认游戏故障。最新正式宿主审计为889/829通过/60失败，原120项106通过14失败；固定地图1000000、无事件上下文，仅首条交互路径。selftest现40通过0失败（含交通倍率与原warrior独立fixture），不等于Unity独立副本或Android已验收。详见npc-dialog与docs/script-api-audit.md。


#### 2026-11-01 停止点与验证边界

独立事件地图最终定向回归74路径/0错误，包含resetPQ保留反应堆身份、双实例怪物/掉落隔离、奖励与fquest声望持久化；有边界完整审计688/2，剩MTS及Seed缺类，另16商品目录延后。尚未完成公园六图、航海全程、队伍/死亡/掉线、原倒计时UI或Android画面验收。按用户要求停止继续开发，详见 [十二项交接](../../../../docs/feedback-12-items-handoff-2026-11-01.md) 与同目录菜单装备反馈记录；证据在docs/evidence/menu-feedback-2026-11-01。

#### 2026-11-01 怪物公园六图闭环（交接后继续）

单人公园流程已在正式宿主跑通，回归 `Assets/Editor/MonsterParkChecks.cs`（`MonsterParkChecks.Run`，13 项 / 退出码 0）。
结论与口径：

- 课程图数据：`953030000..953030500` 的 `life` 全部 `mobTime=-1`（一次性刷怪点），出口门是 `pt=7 + script=mPark_nextStage`，
  末图 953030500 的门是 `script=mPark_final`；`info/returnMap=forcedReturn=951000000`（死亡回大厅，不靠事件回调）。
  因此 `resetPQ` 的「只立即生成 mobTime==-1」口径正好覆盖整张课程图，`instanceMapRespawn` 不需要参与公园。
- 刷怪数量实测：苔藓森林及第 2 关各 16 只，六图合计 `16/16/16/16/16/15`（末图 9830055 × 14 + 9800058 × 1）。
- `mPark_final.js` 奖励按 `player.getLevel()*120` 经验、`*80` 金币；纪念币按 `parkRegion` 给 1/5/20（`MPARK_BASIC/MIDDLE/ADVANCED`），
  回退用 `course` 区间；满背包只发提示、不发币，经验/金币照发。每日勋章任务按 `Calendar.DAY_OF_WEEK` 取
  `[0,15187,15181..15186]`，仅当状态为「进行中」才 `forceCompleteQuest`。
- 每日次数落库在 `extend_value`：`extend_id=角色id`、`extend_type=22`（`CHARACTER_EXTEND_DAILY`）、`extend_name=园区key`、每次 +1，上限 2。
  次数判定用 `getPartyMembersOnSameMap`，单人直接取自己。
- 源端 `EventManager.startInstance` 的三种签名要分开：`(Character)`、`([lobby,] Party, MapleMap[, difficulty])`、`(Expedition)`。
  队伍版走 `registerParty(party,map)`（只登记还在招募图上的在线成员）。移植端只有本地角色是真实 Character，
  所以队长不是本地角色时返回 false；单参对象没有 `getId` 一律按远征队报缺迁移，不能当玩家去取 id。
  判断 party 用 `getLeaderId` 是否存在，不能用「是不是对象」——`startInstance(-1,chr,chr,4)` 的 chr 也是对象。
- **实例回收**：源端 `disposeInstance` 只把名字从事件表摘掉、eim 交给 GC；移植端实例地图注册在 `MapService`，
  必须在摘除后（`PlayerCount==0` 时）显式 `dispose`，否则每打一场漏六张图。`setEventCleared` 之后仍有
  3 秒级延迟回调（航海 finishVoyage），所以不能在 `setEventCleared` 当场回收。
- 测试缝：`NativeEventScripts.Clock`（可注入时钟）+ `TickNow()`（手动推进），因为 batchmode 不跑 `Update`，
  10 分钟超时、延迟任务分支否则不可测。编辑器脚本读 `ResolveMap` / `active` 走反射（`internal` 不进 Editor 程序集）。

剩余：Android 实机逐图打怪与死亡/掉线中断矩阵、事件倒计时原图 UI、远征队入口、`monsterValue` 角色累计击杀记账。
详见 [怪物公园六图闭环](../../../../docs/monster-park-closed-loop-2026-11-01.md)。

#### 2026-11-01 航海（CommerciVoyage）闭环

回归 `Assets/Editor/CommerciVoyageChecks.cs`（`CommerciVoyageChecks.Run`，24 项 / 退出码 0）。要点：

- 状态键 `areaInfo_32010` 落 `extend_value`（`extend_type=21` CHARACTER、`extend_name=areaInfo_32010`），
  原脚本 `C1` 21 段；`getAreaInfos().put` 即时写库，重登后 pending 仍在。
- **`startInstance` 重载判定不能用 GetMember 探测**：`NpcApi.Lenient` 的兜底会为未实现成员返回一个
  「调用才抛」的函数，于是 `GetMember(player,"getLeaderId") is JsFunction` 恒真，把任何角色判成队伍，
  `startInstance(-1,player,player,difficulty)` 直接走队伍分支并报 `player.getLeader`。判定只看宿主显式绑定的
  成员（`JsHostObject.Members.Has`）。单参远征队判断同理。
- 队伍版还要求 `PartyCharacter.getPlayer()`：本地角色返回真实 Character，远端没有真实角色返回 null；
  `getPartyMembersOnline` 走本地队伍服务（成员表即在线成员）。
- `Java.type("X[]")` 只做类型占位：`Java.to(array, type)` 在移植端是恒等操作，但 `Java.type` 原来会报缺类，
  导致 `CommerciVoyage.getEligibleParty` 直接失败。
- 事件侧不需要新宿主：`NativeEventMap` 的 `resetPQ/killAllMonsters/countMonsters/getFootholds().findBelow/
  spawnMonsterOnGroundBelow` 与 `LifeFactory.getMonster`（含 `setFh`）已经够跑 `CommerciVoyage.js`。
- 结算口径：全清 100%，超时 `100-剩余波数×10`（下限 50）；BOSS 25% 追加 5..15；route≥3 且无待领物品时
  10% 掉 bossLoot；BOSS 且未满载 15% 得稀有货。`end(eim)` 只处理仍在实例里的玩家，所以中途离图的人不会被拉回；
  离图不改 `active`，回大厅由 `loadState` 清理（源端的中断损失语义）。
- 未接：Channel 断线 → `eim.playerDisconnected` 的链路（移植端断线=退出登录），以及事件倒计时原图 UI。

#### 2026-11-01 起源之塔（SeedTower 1～20 层）闭环

回归 `Assets/Editor/SeedTowerChecks.cs`（`SeedTowerChecks.Run`，46 项 / 退出码 0）。规则一行没搬进 C#，
仍是 `event/SeedTower20.js` + `npc/2540000.js` + `portal/seedTower.js` + `reactor/9922xxx.js`。要点：

- **`java 里 friendly 标记是死的**：`server/life/MonsterStats.java:57 public boolean friendly` 在整个
  BeiDou 源码里**没有任何赋值点**，`isFriendly()` 恒 false。所以 `dispatchMonsterKilled` 永远走
  `eim.monsterKilled`，`friendlyKilled`/`friendlyItemDrop` 是永不触发的分支。第 19 层 Fleta 死亡的真正判定
  是 `tick` 里的 `if(!escort || !escort.isAlive()) fail(...)`（`SeedTower20.js` 的 f==19 块），
  移植端也**不要**自造 friendly 标记。
- **`GenericPortal.enterPortal` 的 tm/tn 回退是 `else if`**（`server/maps/GenericPortal.java:137-151`）：
  只有 `getScriptName() == null` 的门才按 WZ tm/tn warp；有脚本的门即使 `enter(pi)` 返回 false 也**不回退**。
  `PortalScriptManager.executePortalScript` 返回的是 `script.enter(pi)` 的真实布尔（`PortalScript` 接口，
  Nashorn 把 `undefined` 转成 false）。移植端 `FieldDirector.UsePortal` 目前是「有脚本且 `RunPortal` 返回 true
  就 return，否则 `StartWarp`」，即「有脚本但脚本文件缺失/抛错」时会多走一次 tm/tn——与源端的已知偏差，
  塔内各层不受影响（它们全部有脚本）。
- **计时器语义**：移植端 `stopEventTimer()` 之后 `eventTime=0, timerStarted=0`，`getTimeLeft()` 会返回负值。
  `SeedTower20.enterFloor` 开头就是 `set(remaining, max(0,getTimeLeft()))`，所以**只能在计时器活着时进下一层**；
  休息层 5/15 靠 `if(floor!=5 && floor!=15)` 豁免。`enterFloor(5)/enterFloor(15)` 会直接 `markClear`
  （「进层即记一层」），而且 `markClear` **不停表**。
- **第 2 层石碑**：`enterFloor` 对每块石碑 `resetReactorActions(random(7)+1-1)`，所以初始 state 是 0..6 而不是 0。
  卡牌 id = `4009900 + 色序号*7 + (卡面数字-1)`（色序 blue/yellow/red/green 对应键 b/y/r/g），
  能提交的是「卡面数字 **大于** 封印等级」，封印 7 时没有更大的同色卡，只能用紫卡 4009928。
- **第 18 层**：椰子树受击节流与 15s 重生锁都在 `seedReactorHit`（`floor==18 && chr.getMapId()==992018000`），
  掉落由 `seedCoconut` 走 `map.spawnItemDrop` 落到**地面**，要等 `tick` 的 `collect` 收进背包才计分。
  `reactor/9921000|9921001|9922002|9922003.js` 是先 `rm.getPlayer()`/`rm.getReactor()` 再转调，
  宿主少这两个成员整层就废。
- **`SeedTowerCompat.handleAttack`**（`server/life/SeedTowerCompat.java`）在 `finally` 里 `enableActions`，
  且第 14 层 `skill != 0` 时脚本只 `dropMessage` 就 return —— 但这仍然是「已被接管」（返回 true），
  不能让普攻伤害真的落到蝙蝠身上。
- **`startInstance(party, map)`** 用 `party.getLeader().getPlayer()` 判本地队长
  （`EventManager.java:738`），远端队长在移植端拿不到真实 Character，直接拒绝。
- 塔图数据：`Assets/Resources/Bot/Maps/992.json` 原来没有 `reactors` 字段，用
  `tools/patch_solomapling_reactors.py --maps 992002000 992006000 992018000` 从 WZ 补（992018000 共 24 棵，
  `reactorTime=15`）；`ReactorTemplates.json` 需要 9260004（type 999，不能直接打）/9922002（三击）/9922003（四击）。
- 踩坑：`GameServer.Bots` 懒初始化时 `BotRuntime` 构造函数会把 `MapReactorService.Clock` 覆盖成
  `BotScheduler.Clock`；事件地图一建出来它就跑了，回归里要在建实例之后重新钉回可控时钟。

##### 2026-11-01 补：事件怪「原地闪」= 巡逻范围被压成单点（渲染层）

反馈「起源之塔的怪物只会原地闪」。这是**客户端表现层**缺陷，逻辑层 batchmode 回归（46 步）测不到，
因为 `NativeEventMap.Mob.Render` 在 `renderer==null` 时根本不生成 `EnemyUnit`。

- 复现链：`FieldDirector.SpawnNativeMob`（GM 召唤 / Boss revives / 事件实例怪共用入口）曾写
  `roamMin = roamMax = position.x`；而同文件 `SpawnAt`（普通地图刷怪点）用 `Map.WorldX(rx0/rx1)`，
  无 rx 时 fallback **±3 世界单位**。塔图 `992.json` 本身没有 `spawns` 字段，怪全部由
  `NativeEventMap.Reset()` 的 `Template.spawns`（`NativeMapView.LoadMap` 从 WZ `life` 节点读，
  含 rx0/rx1）+ 脚本 `spawn()` + `instanceMapRespawn` 生成，最终都走 `Mob.Render → SpawnNativeMob`。
- 机理：`EnemyUnit.Roam` 在零宽区间里每帧把 `nx` 夹回出生点并翻转 `patrolDir`，而
  `faceDir = patrolDir`，返回 `moving=true`。于是 `Update` 里 `vis.SetBase("move")`（原地播走路动画）
  + `vis.SetFacing(faceDir)` 每帧 flipX 翻转 → 视觉上就是怪物原地左右镜像高频闪烁，
  且 `transform.x` 每帧被钉回出生点、永远走不出刷怪点。`aggro` 为 false（`NativeMobPursuit`
  未激活）时一直卡在 Roam；玩家打一下触发 `pursuit.Acquire` 后才走追击分支。
- 修复口径（**不自造数值**）：`SpawnNativeMob` 改成与 `SpawnAt` 的 else 分支完全一致的
  `position.x ± 3f`；`EnemyUnit.Roam` 另加零宽兜底（`roamMax-roamMin < 0.01` 直接 `return false`），
  防止后续再被压成单点时又出现逐帧翻转。
- 不受影响：脚本登记的 `seedControlled` 怪（第 13 层波次 / 14 层蝙蝠 / 19 层 Fleta / 8·13 层
  火柱落石）走 `SeedTowerCompat.move → Mob.ScriptMove → EnemyUnit.ScriptMoveTo`，
  `ScriptControlled=true` 使 `stunned` 恒真，根本不进 Roam 分支。
- 回归：`SeedTowerChecks.CheckEventMobRoam` 实际生成一只真实 `EnemyUnit`，断言
  `roamMax-roamMin` 非零，并反射调私有 `Roam` 验证零宽时不再返回 moving / 不再翻转朝向。
- 边界：一行 `event/SeedTower20.js` 都没改；塔内各层巡逻/追击手感、门位置仍需真机复核。

#### 2026-11-01 补正：远征队（Expedition）不是死路

早先记录「脚本里没有任何 `Expedition` 字样」是**错的**。`grep -rl Expedition scripts/` 实际命中 26 个文件：

- `BeiDouSpecial/露希妲远征.js`：`Java.type('org.gms.server.expeditions.ExpeditionType').LUCID` +
  `cm.getExpedition / createExpedition / endExpedition` + `expedition.isLeader / isRegistering /
  isInProgress / contains / addMember / getMemberList / ban` + `em.startInstance(expedition)`。
  NPC `3003208`（地图 450004000）用 `cm.openNpc(9900001,"露希妲远征")` 打开它。
- `event/{Zakum,PinkBean,Balrog,Cygnus,Showa,Scarga,Akayrum,Papulatus,CWKPQ}Battle*.js`：
  `eim.isExpeditionTeamLackingNow(true, minPlayers, player)`。

已补（`Assets/Scripts/Server/Expedition.cs`）：`ExpeditionType`（`ExpeditionType.java:34-50` 五参数）、
`Expedition`（成员表/黑名单/招募 CLOCK 300s/`addMemberInt` 的 0-3 返回码）、
`ExpeditionRegistry`（`Channel.java:411-437` 同类型唯一 + 招募到点回收）、
`ExpeditionBossLog`（`EXPEDITION_BOSSES` 用 `MobId.java` 真值）。

口径：

- **`startInstance(Expedition)` 的顺序**（`EventManager.java:531-605`）：选 lobby → `setup` → `setLeader`
  → **`exped.start()`** → `registerExpedition` → `afterSetup`。`exped.start()` 必须在 `afterSetup` 之前，
  否则脚本里 `isInProgress()` 还是招募态。
- **`registerExpeditionTeam`（`:406-414`）只登记「还在招募图上」的成员**，队长不在招募图就一个人都不登记。
- **三条队伍判定不能混用**：
  `isExpeditionTeamLackingNow`（`:1176`）清关后看「离场且 ≤1 人」、未清关只看 **≤1 人**（既不看队长
  也不看 minPlayers，源码注释明说）；`isEventTeamLackingNow`（`:1185`）未清关时「离场的是队长」直接算缺人，
  否则 `≤ minPlayers`；`checkEventTeamLacking`（`:1165`）是 `!队长在场` + `< minPlayers`（注意是 `<` 不是 `≤`）。
- **`monsterValue` 的签名是 `monsterValue(eim, mobId)`**（`EventInstanceManager.java:609`
  `invokeScriptFunction("monsterValue", EventInstanceManager.this, mob.getId())`）——第一个参数必须是实例宿主。
  移植端早期只传了 mobId，导致所有分支 `if (mobId == xxx)` 都拿到 undefined（26 个脚本写 `return 0`）。
- `expedition` 挂到实例上后：`monsterKilled` 且 `inc != 0` 时顺带记 BOSS 战绩（`:619-621`），
  `dispose` 时 `expedition.dispose(eventCleared)` + 摘频道表 + 置空（`:1107-1116`）。
- **`use_enable_daily_expeditions` 默认 false**（`V1.7.0__create_game_config.sql:90`），
  此时 `ExpeditionBossLog.attemptBoss` 恒 true，不需要 `bosslog_daily` 表。
- 入口（远征队对象 + `startInstance(Expedition)`）通了之后，副本本体是另一轮，见下节。

#### 2026-11-01 露希妲副本本体（LucidBattle）落地口径

`event/LucidBattle.js` 顶层 6 个 `Java.type` 一个都不能少，缺任何一个脚本加载即抛异常（表现为「点了没反应」）：
`org.gms.server.life.LifeFactory` / `LucidBossCompat` / `org.gms.server.maps.AbstractAnimatedMapObject` /
`java.awt.Point` / `java.util.concurrent.ThreadLocalRandom` / `org.gms.util.PacketCreator`。

新增/改动（全部是「脚本拿不到的服务器能力」，规则一条没搬进 C#）：

- `Assets/Scripts/Field/LucidBossCompat.cs`（`org.gms.server.life.LucidBossCompat`）：脚本面 4 个方法
  `startPhase(map,boss,phase)`（`invalid Lucid encounter phase` / `Lucid phase and boss ID do not match` 两条
  校验照搬）/ `stop(map)` / `finishFurySuccess(map)` / `protectFurySuccessMob(mob)` + `isLucidBoss(mobId)`。
  控制器 `Encounter` 250ms 一拍，按 `butterfly → dust → summon → dragon → laser → shoot → rush →
  stainedGlass → hurdle → bomb → fury` 顺序判定；定时任务用 `List<(long,Action)>` 复刻 `TimerManager.schedule`，
  **不要用 Unity 协程**（batchmode 不回调）。
- `Assets/Scripts/Field/LucidTables.cs`：`RUSH_PATH`/`RUSH_SPEEDS`/蝴蝶两阶段 id/六块碎玻璃区域/
  冲刺包围盒/暴怒复活表，逐值照搬 Java 常量表。
- `Assets/Scripts/Field/ScriptMonster.cs`（`LifeFactory.getMonster`）：脚本拿到的 mob 是「模板」，
  真正刷怪时才把 `X/Y/Fh/damageBlockedUntil/hp` 带过去。`getPosition()` 缺省 `(0,0)`——
  `AbstractMapObject` 语义，`LucidBattle.js` 里的 `new Point(1000,0)` 是脚本后设的。
- `Assets/Scripts/NativeScriptEffect.cs`：`PacketCreator.showEffect(String)` 的落点，
  `customSkill/*VideoLayer` 走 `NativeBossVideoEffect`，其余走 `Map/Effect.img/<path>`。
- `MapleMap` 面新增：`spawnMonsterOnGroundBelow` / `dropMessage` / `getAllPlayers` / `getCharacters` /
  `movePlayer` / `getEventInstance`；`NativeEventMap` 上对等实现并保留 `LastPacket`/`LastEffect` 供回归断言。
- **`getIntProperty(缺失键)` 必须返回 0**（`EventInstanceManager.java:793`），不能抛异常：
  `LucidBattle.js` 的 `playerEntry` 第一句就读 `canJoin`/`death_<cid>`。
- **`monsterRevive` 是客户端驱动的死代码**：源端 `MapleMap.reviveMonster` 没有调用点，但客户端按
  `Mob.wz/info/revives` 自己刷后继形态，所以移植端必须在 `NativeMobController.OnKilled` 刷完 revives 后
  回调 `Instance.ReviveMonster(mob)`，否则 8880141→8880152 不出现、阶段 3 永不启动。
- **`NativeBossEncounter.OnSpawn` 要跳过 `eventOwned` 的怪**：副本里的露希妲只由脚本 + `LucidBossCompat` 驱动，
  再建一份客户端控制器就是双份伤害结算。
- 阶段/失败口径（照 Java）：P1 死亡 → 脚本 `phase=2` → 2500ms 后 `advanceToPhaseTwo`（清场 + 换 450004250 +
  刷 8880141）；P2 死亡后控制器**自行退出**（`ActiveCount` 归 0），靠 revives 复活 8880152 再 `monsterRevive`
  重新 `startPhase(3)`；暴怒 45 秒失败换成 8880154 → 3000ms 后 100% 伤害 → 10000ms 后 8880141 以 **16% 血**
  重来并把脚本 `phase` 改回 2；击杀 8880152 → `phase=4` → `finishFurySuccess` 播 furyStop → 5 秒后 `clearPQ`。
- 死亡上限 50 次写在脚本自己的 `playerRevive` 里（`death_<cid>` / `eliminated_<cid>`），C# 只提供
  `invokeScriptFunction` 与 `playerEntry` 的 eliminated 分支，别在宿主里另算一份。
- **`player.respawn(eim, map)` 不是空操作**：源端 `Character.respawn` = `eim.unregisterPlayer(this)` +
  `changeMap`。露希妲满 50 次死亡就靠它退本，随后脚本 `disposeIfEmpty` 回收实例；当空实现会让实例永远
  挂在 manager 上 → 该事件只有 1 个 lobby → 这个角色之后再开本 `startInstance` 恒 false（玩家看到
  「其他远征队已经开始挑战」）。全区共 10 个脚本用到 `respawn`（8 个 event + 2 个 map/onUserEnter 的 BOSS）。
- **实例从 manager 摘名字有延迟**：`Manager.Remove` 走 `event_lobby_delay`（默认 10s）再删表，
  所以「按角色查实例为 null」不代表实例没了；判断场地是否收干净要看 manager 的 `instances` 表。
- **同一拍会连播两个特效**：脚本的 2 秒花爆循环和控制器技能可能落在同一毫秒，后播的覆盖 `LastPacket`。
  `NativeEventMap.EffectLog` 按顺序记录全部 `showEffect`，回归用 `SawEffect(mapId, 片段)` 断言；
  推进时间必须按 250ms 一拍（源端 `TimerManager.register(this, 250)`），一次跨几秒会把两件事压进同一拍。

回归：`Assets/Editor/LucidBattleChecks.cs`（`-executeMethod LucidBattleChecks.Run`），21 项 / 退出码 0。
用例按「距开场绝对毫秒」推进，覆盖 5 个 Java 类型解析、花爆 35%、控制器 P1/P2 全技能、阶段切换、
暴怒失败重置、通关、50 次死亡上限、eliminated 分支、中途离图。
