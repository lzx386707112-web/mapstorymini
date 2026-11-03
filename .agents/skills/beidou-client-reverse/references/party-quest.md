# 组队任务（KPQ / LudiPQ / EllinPQ / PiratePQ）NPC 与事件实例接口

来源：源端 `gms-server/scripts-zh-CN/{npc,event,portal}/`、`org.gms.scripting.event.EventManager`、
`EventInstanceManager`。本文只记录「点 NPC 直接报错」这一类接口缺口的已核实结论与当前移植实现。

## 1. 招募图 NPC：`em.getEligibleParty(Party)` 必须绑定

**症状**：废弃都市点拉克里斯（9020000）→「我想参加组队任务」，弹
「对话脚本出错（详见日志）」。四个组队任务同时坏。

**根因**：`EventManager.java:994-1011` 的 `getEligibleParty(Party)` 不是 Java 侧算资格，而是
把 `party.getPartyMembersOnline()`（`List<PartyCharacter>`）交给**事件脚本自己的**
`getEligibleParty(party)` 判等级/人数，结果 `party.setEligibleMembers(...)` 后返回；
脚本算不出来时只 `log.error` 并返回空表，**不抛**。

移植端 `NativeEventScriptService.Manager` 当时没绑定它，`Strict` 兜底抛出
`Unmigrated event manager API: getEligibleParty`。这个前缀既不是
`Unmigrated server API: ` 也不是 `Java interop: class not migrated: `，所以
`NpcSession.Fail` 落到通用文案「对话脚本出错（详见日志）」。

**调用面**：源端 24 个 NPC 脚本用它（9020000 / 2040034 / 2133000 / 2094000 / 2013000 /
1012112 / 9220018 / 2041023 / 2095000 / 9201048 / 1052013 / 2112004 / 2020008 / 2112003 /
2030008 / 9103001 / 9105004 / 9040000 / 9310039 / 1061012 / 9310006 / 2083001 / 9000037）。
修一处即全部恢复。

**移植实现**（`NativeEventScriptService.cs`，Manager 构造末尾）：

```
getEligibleParty(party):
  party 为空 → 空数组
  members = party.getPartyMembersOnline()
  eligible = 调用事件脚本的 getEligibleParty(members)
  返回 JS 数组；脚本抛错 → service.Error 记日志 + 返回空数组（对齐源端只 log 不抛）
```

配套：事件脚本 `getEligibleParty` 里是 `party.size()` / `party.toArray()` / `partyList[i]`，
再 `return Java.to(eligible, Java.type('org.gms.net.server.world.PartyCharacter[]'))`。
- `NpcApi.BuildJavaList` 因此必须提供 `toArray()`（底层就是 JsArray，原样返回）。
  `Java.to` 在移植端是恒等转换；`PartyCharacter[]` 走 `JsJavaInterop` 的数组类型占位。
- `PartyCharacter` 暴露 `getMapId/getLevel/isLeader/getPlayer`，够四个 PQ 的门槛判定。

**门槛常量**（事件脚本顶层，随 `GameConfig` 读取）：KPQ 21 级 / LPQ 35 / EPQ 45 / PPQ 55；
`minPlayers` 默认 3，`use_enable_solo_expeditions=true` 时降为 1（单机可开）。

回归：`tools/server-check/PqChecks.cs` —— 对四个 `event/*.js` 实跑
`getEligibleParty`，断言合格队长 1 人、等级不足/不在招募图/无队伍均返回空表。

## 2. 其余同类缺口（本次一并补齐）

| 调用 | 用在哪 | 源端语义 | 移植实现 |
| --- | --- | --- | --- |
| `em.getMonster(mobId)` | `PiratePQ.respawnStages` 波次怪 | `LifeFactory.getMonster` 返回未进图的 Monster | 注入 `MonsterFactory`（`ScriptMonster`），未注入时明确报错 |
| `map.getPlayers()` | `EllinPQ.js:143` | `MapleMap.getPlayers()` | `NativeEventMap` 别名到 `getAllPlayers` |
| `map.spawnAllMonsterIdFromMapSpawnList(mobId, difficulty, isPq)` | `PiratePQ.js:218` | 该模板全部刷怪点立即生成 | `MapSpawner.SpawnPointsOf(mobId)` + 逐点 `Spawn` |
| `player.toggleRecvPartySearchInvite()` | 四个招募 NPC 的第二项 | `Character.java:700` 翻转 `canRecvPartySearchInvite`，开则登记搜索 | 翻 `record.partySearch` + `SearchParty`/`StopSearch`（登记失败不回抛） |
| `player.sendAllWordPQNotice(pbName)` | 9020002 / 2040035 / 2133001 / 2094001 领奖 | `Character.java:11004` 全服 serverNotice(2) | 单机等价为底部公告：`[副本公告] : 恭喜玩家 {name} 完成{pbName}。` |

`em.getProperty/setProperty/getIntProperty/setIntProperty`、`eim.grid*`、`eim.linkToNextStage`、
`eim.showClearEffect/showWrongEffect`、`eim.clearPQ`、`eim.giveEventReward`、
`eim.giveEventPlayersExp/Meso`、`eim.getClearStageBonus` 在上一轮已绑定，本次复查无缺口。

## 3. 复查口径（可重跑）

静态扫描：把 `em.` / `eim.` / `getMapInstance(...).` / `cm.` / `getPlayer().` 的成员调用
与 `NativeEventScriptService.Bind(...)`、`NativeEventMap.Bind(...)`、`NpcApi` 的
`cm.Set/p.Set` 做差集。注意 `Properties(...)` 辅助绑定的四个属性方法不在 `Bind(` 文本里，
要单独放行；`Host.Set("x",Host.Get("y"))` 形式的别名也要放行。

本次扫描范围：NPC 9020000/9020001/9020002/2040034…2040044/2133000/2133001/2133004/
2094000/2094001/2094002，事件 KerningPQ/LudiPQ/EllinPQ/PiratePQ，portal kpq0-4、lpq0-7、
party6_* —— 全部清零。

## 4. 假人陪跑：BotPartyService 的两套编号不可混用

**症状**：玩家在废弃都市组织好队伍、点 NPC 进了副本，队伍里的假人一个都没跟进来
（留在招募图继续闲逛）。

**根因**：`BotPartyService` 有两套编号——
- **角色 id**：`membership` 的键，`For(id)` / `Get(id)` / `ChangeLeader(sender,member)` / `Leave(id)` 都收这个；
- **队伍编号**：`nextId` 从 `1000000001` 起，只有 `Join(actor, partyId)` / `Answer` / `ById` 收这个。

`PqRun.Begin` 当时写的是 `BotTypeManager.Parties.For(partyId)`，拿**队伍编号**去查
角色 id 的索引表，稳定返回 `null` → `bots` 列表始终为空 → `EnterStage` 的
`foreach (bots) WarpBot(...)` 一次都不执行，假人原地不动。

同类错还有 `PqDirector.AnswerInvitations` 的 `Parties.For(invite.party)`：恒为 null，
于是「玩家右键邀请假人」被一律判成「队伍不存在」而拒绝，玩家只能等自动补员。

**修法**：
- `BotPartyService` 新增 `ById(int partyId)`（按队伍编号取 `Party`）。
- `PqRun.Begin`：`parties?.ById(partyId) ?? parties?.For(leaderId)`。
- `AnswerInvitations`：`Parties.ById(invite.party)`。
- `PqDirector.AdoptStageEntry`（玩家不点「假人凑队」而是自己带人进本）：
  1. 队长还挂在假人身上 → `ChangeLeader` 让给真人（否则 `startInstance` 与阶段 NPC 全走「请让队长来」）；
  2. 队伍里一个假人都没有 → 从这一摊大厅按 `recruitMap` 拉人补进队（否则副本里永远只有真人）；
  3. 再 `Start(def, party.id, player.id, openViaNpc:false)` 接管跑图。

进本后假人的位置由两条路径保证：`PqRun.EnterStage` 一次性 `WarpBot`，以及
`PqBot.UpdateState → PqRun.DriveBot`（每 700ms，`bot.mapId != CurrentMap` 就 `WarpBot`）。
后者是兜底——首帧地图几何没就绪时 `WarpBot` 会静默失败，靠它补回来。

**回归**：`PqChecks.PartyIndexChecks` —— 建队 → `For(角色 id)` 命中、`ById(队伍编号)` 命中且成员数正确、
`For(队伍编号)` 必须为空（编号语义不可混用的契约守卫）。队伍编号被谁改成兼容角色 id，这条会亮。

## 5. 边界

- 单机实例只登记本地真实角色，`registerParty` 不会伪造远端队员；资格判定仍按源端脚本口径跑，
  人数要求靠 `use_enable_solo_expeditions` 放行。
- `PiratePQ` 的 `spawnAllMonsterIdFromMapSpawnList` 每次调用都会重新生成该模板的全部刷怪点
  （源端每 10 秒一轮，怪被打死才减少），未做去重，属源端行为。
- 静态扫描只证明「调用名有绑定」，不代表分支语义已完整验证；Android 实机仍需人工过一遍四个副本。

## 2026-11-02 大厅进图主线程卡顿

本节是移植端性能修正，不新增EXE行为结论。PqBot.Lobby → BotWander.Start → BotMovementService.Enable → BotNavigationGraphs.Get → GraphBuilder.Build 同步计算全地图轨迹；ground-spot picker还需要Base profile图。废都103000000有519段foothold，离线冷构图实测829.02ms，分帧生成假人无法切开这个同步调用。

新增Graphs.Prepare：主线程取得只读MapGeometry，Task只读已加载地图/共享只读索引，独享builder；缓存/LRU只在主线程发布，不在帧内等待。PqBot开始游荡、PqRun.MoveToward、PQ移动器换图/属性变化先等待所需profile和Base图准备好。原同步Get保留给现有其他业务/离线契约；没有改路径、移动参数或副本规则。大厅SpawnOne续接改PostNextFrame，并拒绝玩家离图或Reset后的旧大厅任务，避免同帧连生及离图后回填。

复现：`rtk dotnet run --project tools/server-check -c Release -- --pq-navigation`。同次废都对比：同步829.02ms，后台请求2.32ms，轮询最大0.63ms，后台完成等待486.04ms（后者不阻塞帧）；逐条比较全部路径端点/launch区间/step/portal/rope/代价一致。小图回归同时覆盖跳跃/下落/绳梯同步与异步结果。服务端检查1209通过/0失败。此为桌面纯C#测量，不代表Android实际帧率或所有进图卡顿已经消除，纸娃娃合成/音频/地图资源仍各走既有预算。

## 6. PQ 怪物不掉任务道具：实例地图必须开服即刷怪（2026-11-02）

**症状**：废都 PQ 第一关杀鳄鱼不掉 4001007 证书，全程无通行证可交。

**根因**：`NativeEventMap` 构造时只建几何不建刷怪点（`Reset()` 原本只由脚本
`resetPQ/resetFully` 触发）。KerningPQ 全程**只**用 `instanceMapRespawn` 补怪
（源端 respawnStages 每 15 秒一次），而 `Respawn()` 里 `spawner==null` 直接 return
→ 实例图从进来到通关一只怪都没有 → 4001007 无从掉落。
LudiPQ/EllinPQ/PiratePQ/HenesysPQ 在 setup 里调 `resetPQ`，所以只有 KPQ 坏。

源端口径：`EventManager.getMapInstance` 返回**频道地图**，刷怪系统自开服常跑、
怪一直在图上。移植端修法（`NativeEventMap` 构造末尾）：

```
void InitialSpawn() { Reset(); foreach (spawner.AllSpawnPoints) if (ShouldForceSpawn()) Spawn(...); }
```

`Spawn` 里的登记改为 `Instance.RegisterMonster(...)`（源端 eim.mobs 只收脚本 spawn 的怪，
模板刷怪点出的怪不进集合——KPQ `killMonster` 递减断言按此口径）。

**掉落数据本身无误**：`FieldDropTable.txt` 与源端 V1.0.51 drop_data 逐条一致——
9300001→4001007(chance=200000, **questid=0**，源端如此，任何队员可拾)；
9300000/9300002/9300003→4001008(100%)；LudiPQ 4001022/4001023；
EllinPQ 4001162/4001163 走**反应堆掉落**（reactordrops，非怪物掉落）；
PiratePQ 4001117/4001120-22。排查工具：`tools/drop-check`（离线滚动链路 35 项断言）。

**连带补齐的 API**（PiratePQ/EllinPQ 跑到才暴露）：

| 调用 | 源端语义 | 移植实现 |
| --- | --- | --- |
| `mob.changeDifficulty(diff, pqMob)` | `Monster.java:1920` → `changeLevel(level*rate, pqMob)` → `ChangeableStats`；rate 2→1.4/3→2.1/4→3.2/5→5.6/6→7.7；非 boss HP=`mobHpVal[newLevel]`×1.5、exp=`mobHpVal/hpRatio`×1.5（hpRatio=hp/exp），boss HP=hp×mod×1.5、exp 不变 | **注意作用点**：PiratePQ 是在 `em.getMonster()` 的**进图前模板**上调用 → 绑定在 `ScriptMonster`（结果存 `DifficultyStats`，进图时 `ApplyTemplate` 重设 MaxHp+exp，不走会 clamp 的 `HpOverride`）；图内 `NativeEventMap.Mob` 也绑了同名方法复用同一静态公式 `ScriptMonster.ChangeableStats()` |
| `map.broadcastStringMessage(type, text)` | `MapleMap.dropMessage` 别名 | `DropMapMessage` 同 dropMessage |

⚠️ **源端 changeDifficulty 实际总是 no-op**：`MonsterStats.setChange` 全工程无调用点，
`changeable` 恒 false。移植端照实守卫（返回 null），公式留全以便将来接入。
watk/matk/wdef/mdef 的难度调整未接入本地战斗链路（PQ 难度差异主要在 HP/exp）。

**回归**：`Assets/Editor/PqInstanceChecks.cs`（batchmode：`Unity -batchmode -nographics
-projectPath <proj> -executeMethod PqInstanceChecks.Run -logFile /tmp/pq_instance.log`）。
五个 PQ（KPQ/LudiPQ/EllinPQ/PiratePQ/HenesysPQ）真脚本 setup 建实例，断言：
入口图初始刷怪数（KPQ 22 / LudiPQ 25 / PiratePQ 26 / HenesysPQ 10）、KPQ `instanceMapRespawn`
补怪不减、`killMonster` 递减、4001007 掉落行 quest=0/chance=200000、20000 次滚动命中
4σ（3980/20000，期望 4000±226）。当前 35 通过 / 0 失败。

**grep 坑**：本机 BSD grep 对 UTF-8 文件要 `LC_ALL=C grep -a`，多模式用 `-E`
（BRE `\|` 不可靠）。


## 2026-11-02 六人资格/追怪/掉落补核

- 源端 `GameConfig.java:331` 的 getServerInt 对缺键返回0；`event/KerningPQ.js:getEligibleParty`
  要求招募图、等级[minLevel,maxLevel]、队长在合格名单、人数[minPlayers,maxPlayers]。
  本地 Schema 缺 mxj_max_level，脚本顶层 maxLevel=0，所以六人也全部被过滤。
  依据源端 SQL `V2.1.50__raise_character_level_cap_to_255_and_rebirth_cap_to_10.sql`，
  DbStore Create/Open 补缺失 mxj/qst_max_level=255（server/Game Mechanics/Integer），保留现有配置。
- PqRun 原先只看 |dx|>55，另一层同X目标会反复被 Strike 的Y检查拒绝；现以
  BotAttack.EngageRange 两轴共同判接近/攻击，到位停步，800ms独立出手CD（源 CompanionGrindBot）。
  同目标16px内不重设导航，避免每500ms把路径进度/卡住计时清零；选怪提高竖向距离权重。
  射程档位仍沿用既有近似移植，未声称原EXE逐武器精确攻击框。
- 招募脚本失败后的20秒裸传模板图已取消：没有 EIM setup 的公共图不能冒充组队任务实例。
- 掉落未改概率：源表9300001→4001007券20%，还可掉4260000；9300000/2/3→4001008通行证100%。
  9300003还可掉装备/卡片。不要把“其他物品同时出现”视为源表错误，也不能把第一关券改成通行证。
- 验证：Unity PqInstanceChecks 77/0（五种真实 Social/NpcApi 六人资格均6，实例初始怪、
  9300001实际模板ID、两种物品地面图标加载、券3980/20000、三种通行证怪各100/100）。
  tools/server-check --pq-feedback 34/0，覆盖旧存档补键、保留配置、跨层追怪、冷却及远程攻击。
  掉落测试未完整跑 Android 击杀→地面→拾取；用户所述“完全不出券”在当前Unity数据未复现。


## 2026-11-02 副本只有真人：两次编号方案冲突的纠正

源端参照仍是 EventInstanceManager.java registerParty(Party,MapleMap):390，在线队员随队入图；本节仅记录移植端接线，不新增EXE行为结论。

早先版本把假人运行态也迁入负Scope，曾通过97项实例回归；后续多图战斗/拾取改成“假人导航保留模板ID，仅战斗、掉落、反应堆经Live转换Scope”，但FieldDirector.TickBots和NpcDialogUi仍按Scope同步/查表。这两个方案混在一起：PqRun把队友放到正模板ID，BotField.OnMap(负Scope)稳定取不到队友，观察者还只登记负Scope，假人导航也不会按当前可见图正常调度。早先97/0是旧方案结果，不能证明当前链路通过。

当前统一口径：
- FieldDirector.BotMapId=Map.id；TryGetMap、SynchronizePlayer、OnLocalMap、PqDirector.LocalMapId、BotField.Synchronize和NpcDialogUi队员名/血条查询全部使用BotMapId。
- PqRun.CurrentMap、bot.mapId和导航继续使用模板ID；实际怪物/掉落/反应堆仍经Live转换至NativeEventMap.Scope，FieldDirector.StateId不变。
- 保留MapRuntime.templateMapId和TemplateMapId用于识别实例身份；未改源JS或人数条件。
- PqInstanceChecks改为以真实NativeEventMap的Template/Scope构建FieldDirector，检查BotMapId和StateId分离，再验证四种陪跑副本五名队友入图及下一阶段跟随。Android画面仍待实机验证。

本次验证：Unity实例回归101/0（四种陪跑副本各五名队友+换阶段）、server-check --pq-feedback 131/0、编译0警告/0错误；脚本pack2779/parseFail0，run基线889/ok790/runFail27/javaInterop72未变。证据：`docs/skill-regression/pq-bot-visibility-2026-11-02.txt`。


## 2026-11-02 假人跑图 vs 关卡脚本：流程差异盘点（KPQ 为主）

结论口径：`PqCatalog` 是「阶段动作表」（打怪/砸堆/站位/直发道具），`PqRun` 只在动作跑完后
以**队长身份静默点一次** NPC。它复现的是「结果」，不是脚本里「谁先找 NPC、要领什么、
道具怎么流动」的过程。以下逐条来自 `npc/9020001.js` + `PqRun.cs/PqCatalog.cs` 现状。

KPQ 第一关（103000800，入口门 sp x≈-1077 在左，Cloto 9020001 x=592 在右，next00 x=716）：
1. 源端队员先到 Cloto 领题：`gridCheck==-1` → 随机题号 `gridInsert` → 需
   `stage1Answers=[10,35,20,25,25,30,8]` 张 4001007（券由 9300001 掉，20%，questid=0）。
   现状：无此环节，假人进图直接 `ClearMobs` 打怪（用户观察到的现象即此）。
2. 源端回 Cloto：`itemQuantity(4001007)==answer` → 扣券 + `gainItem(4001008,1)` + `gridInsert(0)`，
   台词「把通行证交给队长」；队长在旁等，队员把证丢/交易给队长。
   现状：无换证、无等待、无丢证；`PqDirector.Give` 直接把道具写进真人背包（连地面过程都省）。
3. 源端队长分支：`numpasses = eim.getPlayerCount()-1` + `cm.hasItem(4001008,numpasses)` 才 clearStage。
   现状：`NpcApi.getPlayerCount` 恒 1（`NpcApi.cs:472`），`eim.getPlayerCount`=实例登记人数（只有真人）
   ⇒ numpasses=0，队长空手点一次即过，第一关本体校验被单机化绕过。
4. 门：源端 `portal/kpq0.js` 查 `1stageclear` 由玩家自己踩 next00；现状 `AdvanceStage`
   比较模板 id 后直接 `WarpPlayer`，踩门这步被跳过。

KPQ 第 2/3/4 关（103000801/2/3）：源端第一次对话只发规则并把随机组合写进 `stgNProperty`，
**第二次对话**才 `rectangleStages` 核对（`getPlayers().size()<3` 直接放行）。现状每关只点一次
（`talkStage` 守卫）⇒ 只走引言分支，脚本从未 `clearStage`：没有 `Nstageclear`、没有阶段经验
（200/400/800，`linkToNextStage→giveEventPlayersStageReward`）、没有 clear 特效，全靠 `AdvanceStage`
硬传。另外假人站位是「按队伍顺序占前 N 个矩形」，与脚本抽中的组合无关。

KPQ 第 5 关（103000804）：现状 `KillBoss` + `Give(4001008,10)` → 点一次即 clearStage+clearPQ；
源端 10 张券来自 9300000/9300002/9300003（各 100%）。且过关后脚本要求**再点一次** Cloto
（`5stageclear` 且 stage>=5 的分支不 dispose）→ status1 → `giveEventReward` + `warp(103000805,st00)`。
现状无第二次对话 ⇒ 永远进不了奖励关。

奖励关 103000805：源端由 9020002 收尾（确定离开 → 前三名额外奖励 + `recordEntryPQ` + warp 103000000）。
现状 `PqCatalog` 没有这张图的阶段，一旦玩家真的到了这里，`PqRun` 按 offMap 8s 直接 `End(true)`
传到 103000890，既不领奖也不记次数。

其它副本同型：LudiPQ 各阶段脚本是「点一次设置 `statusStgN=0`、点一次核对」，但
`getIntProperty` 缺省 0（源端同）导致 `state==-1` 的引言分支实际不可达 ⇒ 一次对话+持有足量
4001022 就能真 clearStage（现在靠 `Give` 直发，能过但「打怪攒券」是演的）。PiratePQ 第二关
3 轮对话已显式建模（`PqStep.rounds/openOnly`），但三色勋章同样是 `Give` 直发。

抽象层未落地字段：`PqStep.need`、`PqStep.rounds`、`PqRun.TalkRetryMs` 目前都没有驱动逻辑
（打怪/砸堆一律「清空即完成」，不看数量）。

待补齐的能力（决定能走多真）：
- 假人执行 NPC 脚本：`NpcSession.scriptCharacterId` 写死 `GameSession.CharacterId`，
  `cm.isEventLeader/eim.gridCheck` 全按本地角色算 ⇒ 队员分支目前物理上跑不了。
- 假人进实例：`Instance.Register` 只登记真人，人数类判定（numpasses、minPlayers）全部退化为 1 人。
- 假人背包/拾取：`DropCommands.botLoot*` 只是「让地面物品消失」，不入库；券无法真正攒到假人身上。


## 2026-11-02 按脚本流程重接废都：队长按 NPC 自己算的人数收证

口径来源（用户）：**队长按它自己和 NPC 对话得到的数量来收**，不引入「假人交几张就要求几张」这类
自定义耦合。落到实现上就是恢复源端那句 `numpasses = eim.getPlayerCount()-1`。

### 1. 人数口径：假人真的登记进事件实例
- 新增 `NativeEventScripts.RegisterBotPlayer/UnregisterBotPlayer`：用 `BotNpcApi.Player(null,bot)`
  造常驻 player 宿主，`Instance.Register(host, entry:false)`（不触发 `playerEntry` 的 changeMap）。
  `BotNpcApi.FallbackCm` 补了无会话版 cm，因为 `eim.unregisterPlayer` 会拿
  `getAbstractPlayerInteraction` 清 `setEventExclusiveItems` 的独占道具。
- `PqRun.EnterStage` → `RegisterBots()`；`End`/核对兜底 → `ReleaseBots()`。
- 效果：6 人队进本后 `getPlayerCount()==6`，队长点 Cloto 时 `numpasses==5`，
  `cm.hasItem(4001008,5)` 才 clearStage —— 空手过关的口子没了。

### 2. 第一关：队员分支真跑（`PqAction.BotAsk/BotCollect/BotExchange/BotWait/HandOver`）
- `BotAsk`：假人走到 Cloto（9020001）→ `PqBotNpcDriver.Talk` 以**队员身份**跑
  `gridCheck==-1 → 随机题号`，把脚本那一页题目原文念出来，再读 `Instance.Grid(botId)`
  → `PqCatalog.Stage1Answer`（照抄 `9020001.js:38` 的 `[10,35,20,25,25,30,8]`）算出要收几张。
- `BotCollect`：打 9300001，**自己捡地上的 4001007**（`Field/BotGroundPickup.cs`：
  `MapItemService.ItemsInRange` + `MapItemPickup.PickupItem` + `BotActorLooter`，与真人同一条结算链）。
  按「还差几张」封顶拾取 —— 脚本判定是 `itemQuantity == answer` 严格相等，多捡一张反而交不掉。
- `BotExchange`：券数正好相等才回 Cloto 换证；`gainItem(4001008,1)` 成功后说
  「换到通行证了，我在 Cloto 旁边等队长」。多出来的券在这里丢掉。
- `BotWait` → `HandOver`：站 NPC 旁等，再把证从**假人背包**扣掉落到队长背包
  （`PqDirector.BotItem` 钩子 → `BotNpcApi.Gain`），一人一张。`questScriptFailed`
  或数量不足时按差额补给队长并在提示里写明（保底，不让整趟卡死）。
- 队长最后那一次静默点 NPC 就是脚本的判定分支，数量由脚本自己算。

### 3. 第 2/3/4 关：两次对话 + 按组合站位（`PqAction.StageCheck`）
- 第一步 `Talk` 让队长听规则（脚本写 `stgNProperty`），`WorkTalk` 现在**等回调完成**再进下一步；
  `talkSent/talkDone` 改为按步骤重置（以前一关只发一次对话，第二次对话永远不发）。
- `StageCheck`：读 `stgNProperty` 行号 → `PqCatalog.Combo`（照抄 `9020001.js:49-60` 的
  4/10/20 组）→ 组合为 1 的矩形各放一个假人，其余退到出生门（三个阶段出生点都在判定矩形之外，
  已按 WZ 坐标核对）→ 队长点第二次核对；`Nstageclear` 出现即通过。
- 两次核对仍不过 → `ReleaseBots()`（实例人数回落，脚本 `getPlayers().size()<3` 直接放行）+
  再点一次，最多 5 轮。这是源端自带的单机宽容条款，不是另造条件；像素级站位（尤其 4px 宽的绳子矩形）
  误差不会被放大成死锁。

### 4. 第 5 关 / 奖励关
- 先 `KillBoss`，再按 9300000/2/3 的 100% 通行证掉落 `BotCollect(itemId=4001008, need=10)`
  捡够 10 张 → `HandOver(itemCount=10)` → 队长点 Cloto 交 10 张 → `clearStage + clearPQ`，
  同一段对话的 status1 分支 `giveEventReward + warp(103000805,st00)`。
- `PqCatalog` 补了 103000805 阶段（9020002，确定离开 → 发奖 + 记次数 + 传回 103000000）。
  以前这张图没有阶段，玩家真到了这里会被 offMap 8s 直接 End。

### 5. 回归
- `tools/server-check --pq-feedback` 52/0，新增 12 条：答案表/组合表与脚本一致、第一关五步齐全、
  第一关不再直发证、实例登记后 PlayerCount 递增/重复登记不重复计数、`gridInsert→Grid` 读回题号、
  题号 3 → 20 张券、交证后假人扣减+队长入账。
- `tools/unity-compile-check` 0 warning / 0 error。
- 未跑：Unity `PqInstanceChecks`（需 batchmode）与 Android 实机。实机上要盯三件事：
  ① 假人是否真的去捡券（掉落归属=队长，靠同队归属窗口放行）；② 第 2/3/4 关组合站位是否落进矩形；
  ③ 券的掉落节奏够不够 5 分钟内收满（20% 掉落，22 只鳄鱼/波）。

## 2026-11-02 玩具塔101 / 毒雾森林 / 老海盗：按脚本落地面流程

用户口径：「全部组队任务都要支持」。做法和废都第一关一致——不重写脚本，把假人放进
脚本要求的位置上：**队长点 NPC 听规则 → 队员真去打怪/砸箱子把道具攒到手上 → 交给队长 →
队长再点一次让脚本判定**。`PqCatalog` 里三个副本的阶段表全部按源端 npc 脚本重写。

### 1. 玩具塔101（LudiPQ，npc/2040036~2040044.js）

九关一个套路，四步缺一不可（以前是 `Give` 直发券 + 只点一次，脚本停在 `statusStgN=0` 永不 clearStage）：

| 关 | 图 | NPC | 要几张 4001022 | 怎么来 |
| --- | --- | --- | --- | --- |
| 1 | 922010100 | 2040036 | 25 | 9300005×15 + 9300006×10 各掉 1 |
| 2 | 922010200(+201) | 2040037 | 15 | 11 个 2202003（dropItems 掉券）+ 陷阱房 922010201 的 4 个 |
| 3 | 922010300 | 2040038 | 32 | 图上 8 只 9300007 + 8 个 2201001 各 spawnMonster×3 |
| 4 | 922010400 | 2040039 | 6 | 黑暗塔 922010401~405，9300008/9300014 |
| 5 | 922010500 | 2040040 | 24 | 六个子图 922010501~506 各 4 个 2202003 |
| 6 | 922010600 | — | 0 | 纯传送门迷宫，只等玩家爬（gateMs=20s） |
| 7 | 922010700 | 2040042 | 3 | 3 个 2201002 各刷 1 只 9300010 |
| 8 | 922010800 | 2040043 | 0 | 五个箱子占位（脚本对 <7 人直接放行） |
| 9 | 922010900 | 2040044 | 钥匙 4001023 | 砸 2201003 召 9300012，BOSS 100% 掉钥匙 |

⚠️ **2201001 / 2201002 只 `rm.spawnMonster`，不 `dropItems`**（源端 SQL 里 2201001 有一条
4001022 记录，但脚本从不调 dropItems ⇒ 实际不掉）。给这两种箱子再配"砸开掉券"会凭空多出
8 / 3 张，第 3 关变成 40、第 7 关变成 6。

### 2. 毒雾森林（EllinPQ）

- 930000100：portal `party6_stage` 要求 `getMonsters().size()==0` → 清 50 只 9300172。
- 930000200：`spine` 3009000 要砸到 state≥4 才放行（另有 3002000「水池」dropItems→4001162）。
- 930000500：9 个石堆 = **8×3002002（无脚本，砸了不掉东西）+ 1×3002001**，
  只有 3002001 走 `dropItems` 掉 4001163（reactordrops 概率 1）。假人砸出来交给队长，
  队长带着石头点 2133004（不带会被当场传回 930000800 = 放弃本趟）。
- 930000600：砸 3001000 召 9300180（WZ revive 链 180→181→182），打掉 9300182 才 clearPQ。
- 930000700：2133001 发奖 + 记次数 + 传回 930000800。

### 3. 老海盗（PiratePQ）

- 925100100：三轮勋章 4001120→4001121→4001122 各 20 个，由 9300114/5/6 各 10% 掉。
  **2094002 只能点一页**（npc/2094002.js 再点一次就把队长传回城）⇒ 三轮全用 `openOnly`，
  完成信号是实例属性 `stage2 = "1"/"2"/"3"`（`PqStep.awaitProperty/awaitValue`）。
- 925100200 / 925100300：真正要清的在子舱 925100201 / 925100301（各 48 只）；
  另有 2511001 反应堆，砸开各刷 6+6 只（怪不打完门不开）。
- 925100400：四个动力门 2519000~2519003（`sMob1~4`，脚本判 `getReactorByName(...).getState()>=1`），
  清完还得玩家自己踩 `next00`——那扇门的脚本把 BOSS 刷进 925100500（`gateMs=20s`）。
- 925100600：2094001 发奖。

### 4. 反应堆脚本宿主：一个实现、一处接线

以前 `MapReactorService.Act` 有两个实现（`Server/Scripting/NativeReactorScriptService` 与一个
Field 层重复品），而且 `MapReactorService.Hit` 破坏时根本不调 Act——291 个 `reactor/*.js`
一次都没跑过。现在只保留 `NativeReactorScriptService`，由 `BotRuntime` 接
`MapReactorService.Act`，GameSession 只补两个「只有 Unity 侧拿得到」的钩子
（`MapReactorService.MapHost` / `SpawnMonsterAt`）。

已修的重载/绑定缺口：

| 脚本 | 以前为什么坏 | 现在 |
| --- | --- | --- |
| `2202003.js` `rm.dropItems()` | 宿主只认 5 参重载，0 参直接抛「overload is not migrated: 0」 | 0/4/5/6/7 参全支持，统一折到 `NativeReactorDrops.Drop` |
| `3002001.js` `rm.getEventInstance().showClearEffect(rm.getMap().getId()); rm.dropItems()` | `rm.getMap` 没绑定 → 抛在 dropItems 之前，石头永远不掉 | 绑 `getMap`（实例地图宿主，缺实例时退化成只认 getId 的最小宿主） |
| `2201001.js` / `3001000.js` `rm.spawnMonster(...)` | 没绑定 | `(id)/(id,qty)/(id,x,y)/(id,qty,x,y)` 四档 → `NativeEventMap.SpawnAt`；未接线只记一条明确错误，不吞同段脚本里的 dropItems |
| `2519000~2519003.js` `rm.getReactor().getMap()` | 反应堆宿主没有 getMap | `getReactor().getMap()` 与 `rm.getMap()` 同一宿主 |

`MapReactorService` 侧同时补了两条源端行为：

- `Hit()`（CustomReactor 路径，假人/OPQ 走这条）破坏时 `Act?.Invoke(reactor, owner)`；
- `HitPlayer()` 在模板**没有状态机数据**时退回「state++ 然后 act」——源端 `stats.getType(state)`
  返回 -1 就是这个分支。组队任务的箱子/石堆全属于这一类；不兜这一下，玩家砸玩具塔箱子毫无反应、
  只有假人砸得开（`NativeReactorView.FindTarget` 的过滤同口径放宽）。
- `RespawnAll(mapId)`：`reactorTime<=0` 的箱子破坏后不会自己长回来，`PqRun.EnterStage`
  按「本关主图 + 各步骤 maps」各重置一次，否则同一趟会话里第二遍跑玩具塔一个箱子都没有。

`NativeReactorDrops.Drop` 缺表不再抛异常：源端 reactordrops 没有行就是「这一款箱子不掉物品」，
抛异常会把它后面那段脚本一起吞掉（玩具塔奖励关 36 个 2202004 的 `sprayItems(...,15)` 就靠这一条）。
缺的模板 id 记进 `NativeReactorDrops.MissingTables`，自检/日志去看。

### 5. 地图数据：分片原来根本没有 reactors 数组

`Assets/Resources/Bot/Maps/*.json` 由 `export_solomapling_maps.py` 导出，**只有
footholds/portals/ropes/npcs**，没有 `reactors`。于是四个副本的箱子/石堆/动力门在移植端
一个都不存在——再怎么改假人逻辑也砸不到东西。

补法：`tools/patch_solomapling_reactors.py --maps <47 张副本图>`，从
`MapleDashResources/Data/Map/Map*/**.img` 的 `reactor` 节点增量写入分片，并把用到的模板
导出进 `ReactorTemplates.json`（49 条，包含组队任务全集；注意这些模板 `states` 是空的，
所以只能走上面那条「无状态机」兜底）。关键数量（也是分片导出的验收口径）：

```
922010200 11×2202003 + 1×2200002(陷阱箱, warpMap 922010201)   922010201 4×2202003
922010300 8×2201001      922010700 3×2201002      922010900 1×2201003
922010501~506 各 4×2202003（主图 922010500 一个都没有）
930000200 3002000 + spine 3009000     930000500 8×3002002 + 1×3002001     930000600 1×3001000
925100400 sMob1~4 = 2519000~2519003   925100201/301 各 1 个宝箱 2512001
```

### 6. 两套图 id：真实 id 与 Scope 负 id

副本图每实例各一份，客户端用的是 `NativeEventMap.Scope`（`FieldDirector.StateId = EventMap.Scope`），
刷怪快照（`BotCombatField`）、地面掉落（`MapItemService`）、反应堆实例都挂在这个 id 下；
而假人的 `mapId`、阶段表、传送门、存档一律是**真实 id**。以前 PqRun 直接拿真实 id 去问这三套系统，
`BotCombatField.Snapshot` 的 `field.StateId != mapId` 恒成立 ⇒ 假人永远看不到怪（多图步骤还会
因为「一张图都没活」被当成已完成而静默跳过）。

修法：`PqDirector.LiveMapId`（FieldDirector 注入 `NativeEventScripts.ResolveMap(map)?.Scope ?? map`）
+ `PqRun.Live()`，只在「战斗快照 / 地面掉落 / 反应堆」三处转换；`bot.mapId`、`WarpBot`、阶段比较
仍走真实 id。`BotGroundPickup.PickNearby` 同理（它直接查 `MapItemService.ItemsInRange`）。

⚠️ 单客户端的硬边界：只有玩家当前所在的那张图有 `EnemyUnit`，其它图 `Snapshot` 恒空。
所以「多图清怪」步骤里，非当前图会被当作已清——玩家走到子图后假人才跟过去接着打。
这是渲染单图的必然结果，不是脚本条件。

### 7. 回归

- `tools/server-check --pq-feedback` **129/0**（本轮新增 77 条）：LPQ 九关张数与四步形态、
  子图覆盖、EPQ 反应堆顺序与魔力石独占、PPQ 三轮勋章 + `stage2` 序列 + 四个动力门、
  `reactordrops` 三条源端行、`2202003.js` 真脚本跑通（0 参 dropItems）、`2201001.js` 刷 3 只
  9300007、`3002001.js` 跑通（getMap+dropItems）、`spawnMonster` 未接线只报错不掉链、
  分片 reactors 计数（11+4=15 / 6×4=24 / 4 个动力门…）、`RespawnAll` 让砸坏的箱子回到初始状态。
- 全量 `tools/server-check` 1311/0；`tools/unity-compile-check` 0 warning / 0 error。
- 未跑：Unity `PqInstanceChecks`（需 batchmode）、Android 实机。
  实机要看：① 玩家自己砸箱子有没有反应（`HitPlayer` 兜底）；② 跨子图（陷阱房/黑暗塔/子舱）
  玩家走过去后假人是否跟上；③ 三轮勋章 10% 掉落的节奏（20 个/轮 × 3 轮）。

## 2026-11-02 夜：废都第一关「走到 Cloto 就停住、不去打怪」

实机反馈：假人全走到右边 NPC 之后就没下文，不会去打鳄鱼。三个独立断点叠在一起，
缺任何一个都表现为「站着不动」，所以以前看日志只看到"领题成功"。

### 1. `StrikeNearest` 还在用真实图 id 查怪（`BotCollect` 专用路径）

`WorkMobs` / `WorkReactors` / `GroundCount` / `TargetCount` 在上一次 Live 改造里都换成了
`Live(map)`，但 `WorkBotCollect` 走的 `StrikeNearest(bot, step, now)` 漏了：
`grind.Monsters(bot.mapId, ...)` → `BotCombatField.Snapshot` 里 `field.StateId != mapId`
恒成立 ⇒ 返回空表 ⇒ 找不到目标直接 return。表现就是「人不走、不打」。

### 2. `BotCombatField.Hit` 拿 `bot.mapId` 和 `field.StateId` 比 —— 副本里每一刀都被丢掉

即使找到怪、走过去挥刀，伤害也会在 `Hit` 入口被这个判断静默吞掉：
假人拿的是真实图 id（103000800），`field.StateId` 是每实例一份的 Scope 负 id。
改成比「怪自己所在的图」：`mob.mapId != field.StateId`（`mob.mapId` 就是 Snapshot
传进来的那个 id，天然是 Live 口径）。这条影响全部四个副本，不只是废都。

### 3. 普通怪掉落根本不进 `MapItemService`，假人永远捡不到券

`FieldDirector.DropLoot`（普通怪死亡）走的是老式 `GroundItem.Spawn(itemId, qty, pos)`
——`bound == null`，没有服务端 `MapItem` 实体；而 `BotGroundPickup.PickNearby` 查的是
`MapItemService.ItemsInRange`。两套掉落系统并存：反应堆/脚本掉的走权威链，怪掉的走老式链。
所以废都的 4001007、玩具塔的 4001022、毒雾的 4001163（怪掉的）假人一张都拿不到。

补法（不动真人拾取链路）：

| 层 | 改动 |
| --- | --- |
| `GroundItem` | 新增 `TakeForBot(mx,my,rangeSq,max)` / `MapX,MapY` / `Ready`：把无实体的老式掉落整份交给假人，返回件数 |
| `BotGroundPickup` | `PickNearby` 在权威掉落之后再扫一遍 `GroundItem.All`（只认 `bound==null` 且 `itemId` 匹配），先 `HasSpace` 再拿走；新增 `WalkTo(bot,item)` 找最近一件（两套都看）并让假人走过去 |
| `PqDirector` | 新增 `WalkToItem` 委托 + `WalkToDrop()` 包装（Server 层看不到 GroundItem，只能注入） |
| `FieldDirector` | 接线 `PqDirector.WalkToItem = (bot,item) => botPickup.WalkTo(bot,item)` |
| `PqRun` | `WalkToDrop` 改调 `PqDirector.WalkToDrop`（原来自己查 `MapItemService`，看不见老式掉落） |

只动 `bound == null` 的那些，真人点地面物品仍走 `GroundItem.Pickup → MapItemPickup`。

### 4. 收券数量：废都第一关不能写死 `need`

`PqCatalog.Kerning()` 第一关的 `BotCollect` 没有 `itemId/need`（每人题目不同，靠
`couponNeed` 各收各的）。`WorkBotCollect` 原来一律 `need = max(1, step.need)` ⇒ 全队凑够
**1 张**就停、直接跳到换证，队长只拿到一张证也交不掉。改成：

- `CollectDone`：表里写了 `need` → 全队合计；没写 → 每个假人各自凑够自己那份（`couponNeed`）；
- `CollectShortfall`：同上口径算「这个假人还差几张」；
- 另外 `WorkBotExchange` 里 `if (shortAny) return WorkMobs(...)` 也不对：`WorkMobs` 在
  「一张图都没怪」时返回 true，会把这一步判成完成、带着空手进交证步。改成干活后一律 `return false`，
  交给超时兜底。

### 5. 回归

- `--pq-feedback` **131/0**（新增 2 条：废都第一关收券步 `need/itemId` 必须留空、只打 9300001）。
- 全量 `tools/server-check` **1313/0**；`unity-compile-check` 0 warning / 0 error。
- 实机待验：走到 Cloto → 领题 → 去打鳄鱼 → 捡券 → 换证 → 等队长 → 交证 → 队长点 Cloto 过关。

## 2026-11-02 深夜：仍然是「走到 Cloto 就站着」——根因是句柄当模板 id 用

上一轮的三个断点都修了，实机还是走到右边不动。这次是数据结构层面的错：

**`BotGrindMob.id` 填的是 `enemy.GetInstanceID()`（Unity 运行时句柄，随机大数），但阶段表里
`PqStep.ids` 写的是怪物模板 id（废都的 `9300001`）。** PqRun 有三处拿 `mob.id` 去比
`step.ids`：

| 位置 | 后果 |
| --- | --- |
| `WorkMobs`（KillMobs/ClearMobs 选怪） | 玩具塔第 4 关（9300008/9300014）一只都选不出来 |
| `TargetCount`（收券步的「还剩多少活」） | 恒 0 ⇒ `AssignMaps` 认为这张图没活，假人不派过去 |
| `StrikeNearest`（BotCollect 的打怪） | `best == null` 直接 return ⇒ **站在 NPC 旁边不动** |

废都第一关的收券步 `ids = {9300001}`，三处全中，所以表现就是「领完题站着」。野外挂机看不出来
（`BotPartyGrindService.Target` 不做怪种过滤），只有副本的按怪种步骤会暴露。

修法：

- `BotGrindMob` 加 `mobId`（模板 id），`BotCombatField.Snapshot` 填 `mobId = enemy.fieldMobId`；
  `id` 继续只当运行时句柄（认领 / `Hit` 反查 `EnemyUnit` 都用它，语义不变）。
- `PqRun.IsWanted(step, mob)`：统一按 `mobId` 过滤，`mobId == 0`（自检造的假数据）才退回 `id`。

顺带把「一只怪都没有」从静默改成可观察：

- `StrikeNearest` 返回 bool；`WorkBotCollect` 拿不到目标就 `Sweep(bot)`：每 2.6 秒换方向走
  半张图，别守着 NPC 那一段等。刷怪点是分段的，废都的鳄鱼、玩具塔的分层都靠这一下才找得到。
- 连续 8 秒选不出目标 → `PqDirector.Notify` 一条带「真实图 id / 实例 Scope / 场上有几只但不是这种」
  的粉字提示。以后再出类似问题，实机上直接能看出是「没刷怪」还是「过滤写错」。

⚠️ 记住这条口径：**`BotGrindMob.id` 是句柄，`BotGrindMob.mobId` 是怪种。** 任何
「只打某某怪」的判断都必须用 `mobId`。

### 回归

- `--pq-feedback` **135/0**（新增 4 条：模板 id 命中 / 句柄恰好等于模板 id 也不能误判 /
  旧数据没填 mobId 的降级 / `BotCombatField` 快照真的填了 `mobId`）。
- 全量 `tools/server-check` **1323/0**；`unity-compile-check` 0 warning / 0 error。

## 2026-11-03 PQ 追怪分工与收券抢占修正（移植端）

本节为移植端 AI 修复，不新增 BeiDou.exe 地址、资源或原端行为结论；任务答案、怪种、掉率、收券数量继续沿用上文已核实脚本。

- `PqRun.WorkBotCollect → WalkToDrop → BotGroundPickup.WalkTo` 原来全图追券优先于攻击，附近有怪仍可能被远处掉落拉走；WalkTo 每拍重设 Move，会重置移动进度检测。现在先捡脚下、追加权距离 340 内的券，再选全图怪；无目标时再全图收券。同一落点用 IsMovingToward 保留现有移动，不重置进度。
- `WorkMobs/StrikeNearest → Nearest` 共享每队员目标认领：优先未认领的活怪，保持原活目标，怪少时允许合击；不增加寻怪半径或队长附近限制。收齐券的队员释放认领，阶段/步骤切换清空。StrikeNearest 补齐既有 Strike 的 800ms 攻击节奏。
- `Sweep` 的 2600ms 节流由全队共享改为每假人独立，避免每轮只有首个队员获准扫图。
- 回归：`rtk dotnet run --project tools/server-check -c Release -- --pq-feedback`（工作目录 MapleDash）139/0，新增近怪/远处高层未认领怪/追逐保持/死怪后合击四条。Unity 编译检查 0 warning / 0 error，diff whitespace 检查通过。
- 边界：纯 C# 选择测试证明远处高层目标可被分配，不证明 103000800 每条绳梯路径实际可走；尚未做 Unity 游戏内或 Android 实机验收。340 为本次移植端收券调度参数，不是已逆向确认的原 EXE 常量。


## 2026-11-03 废都第一关追怪复报：长绳执行死点与收券抢占

这是移植端 AI 执行修复，不新增原 EXE 地址或原端行为结论。复用已导出的原图 `Assets/Resources/Bot/Maps/103.json` 中 `103000800` 的 214 条 foothold、6 条 rope；不修改地图几何、脚本题目/怪种/收券数量。以下坐标均为地图像素，y 向下。

- **确定死点**：`BotNavigationRuntime.Resolve → Execute(Climb) → Waypoint` 在底层到上层路径上选到 `start=(-347,101), rope.x=-437` 的绳边。Execute 的 `CanWalkTowardRope` 判定脚下还可以走，故推迟跳抓；旧 Waypoint 却仍返回已经到达的起跳点，角色永远不向绳子接近。只加选怪范围/怪物认领无法修复这个执行矛盾。
- 修复：`CanWalkTowardRope` 改为复用 `BotGroundMotion.Step` 检查整段地面路线，只有能不掉层/不阻墙地走到 `BotRopeMotion.CanGrab` 的位置才延迟跳抓；仅第一步可走不再阻止已验证的跳抓。可直接走近时 Waypoint 也指向绳子的 x，不能一直指向已到达的起跳点。该导航由 PQ 和野外假人共同使用。
- **对照复现**：`tools/server-check/PqChecks.CheckKerningRoutes` 用真实几何、125/105 档、从 `(282,-82)` 到 `(-642,-1125)` 跑 50ms 物理 tick，不启用移动服务的随机跳跃救援。临时撤掉这段修复后，120s 仍停在 `(-347,101)`；恢复后可进入近战射程。另有 100/100、125/105、130/110、155/123 四档，每档从 `(-1077,111)` 到六个平台，执行实际 `BotMovementService` 调度和 `PqRun.StrikeNearest → BotPartyGrindService.Strike → Hit` 回调，共 24 条路线。
- **收券抢占**：`WorkBotCollect` 旧逻辑即使当前目标已在射程内，也会先被 `WalkToDrop` 抢走这一拍。现在仍先捡脚下券，其次对已在射程内的认领目标出手，再考虑走向附近券/追怪；射程与 800ms 冷却不变。新增模拟持续有待追券的测试，证明近身攻击不会被寻券委托抢占。
- **分工粒度**：仅保证不同怪的 id 不同，仍会让所有队员认领同一片平台上的怪。新目标评分在原距离上加入队友认领目标周围的拥挤成本 `max(0,1400-|dx|-2|dy|)`；既有活目标仍保留，怪少时仍合击。1400 是用户授权 AI 优化的移植端参数，并非原 EXE 常量。
- 验证边界：离线测试使用真实导航/物理/攻击服务和伤害回调，怪物目标为平台坐标上的测试对象；不等同于 Unity `EnemyUnit` 场景战斗或 Android 实机验收。旧 `tools/bot-move-check` 以 8px 到达判定而未开 precise，可能全部停在距离目标 9~21px 却仍输出 OK；本次不将它的 OK 当作追怪成功证据，而以进入射程且收到 Hit 回调为准。

- 最终回归：PQ 定向 **166/0**；全量 Server **1367/0**；Unity 编译检查 **0 error / 0 warning**；diff whitespace 检查通过。前后对照摘要保存在 `docs/skill-regression/pq-navigation-{before,after}-2026-11-03.log`。

## 2026-11-03 招募图大厅「丢东西」不许丢任务道具

原端事实（`tools/imgdump.py` 直接查 `Item/Etc/0400.img`，非推测）：四个副本用到的道具全部带 **`info/pquest = 1`**，没有 `info/quest`；price 多为 1。

| id | 名称 | 用途 |
| --- | --- | --- |
| 4001007 | 证书（优惠券） | 废都第一关：砸箱/打鳄鱼掉，换 4001008 |
| 4001008 | 通行许可证 | 废都：交给 Cloto 过关 |
| 4001022 | 次元的通行证 | 玩具塔 2/5/6/7/8 关 |
| 4001023 | 次元的钥匙 | 玩具塔第 9 关 BOSS 阿丽莎乐掉 |
| 4001163 | 紫色魔力石 | 毒雾森林 930000500 |
| 4001120/1/2 | 初级/升华/资深海盗勋章 | 老海盗 925100100 三轮 |

对照物（大厅氛围掉落池里真正的杂物，**不带** quest/pquest）：2000000 红色药水、2000001 橙色药水、4020000 石榴石母矿、4000004 绿液球。

移植端问题与修复：`PqBot.DropSomething` 原来把 4001007/4001008/4001022 和杂物混在一个池里往招募图地上丢。源端脚本用 `haveItem(id) == 题目张数` 判定（废都第一关尤其硬），地上凭空多一摞券会被队员/玩家捡走，直接把阶段判定打乱。现在：池子只留纯杂物；`IsQuestItem` 按 `Bot/Items.json` 的 `info.quest`/`info.pquest` 过滤（查不到或表没加载成功一律当任务道具挡掉），`AmbientPool()` 首次丢东西时才筛、`DropSomething` 再兜一次。

回归：`PqChecks.AmbientDropChecks`（反射读 `PqBot.LobbyDrops`）——池内 4 条无任务道具、7 种阶段道具全部带 `pquest`、三个历史肇事 id 单独点名。`--pq-feedback` **183/0**；全量 `tools/server-check` **1404/0**；`unity-compile-check` 0 error / 0 warning。

## 2026-11-03 组队任务「通往下一关的门」不许提前出现

用户反馈：玩具塔 101（LudiPQ）里**通关用的下一关门一开始就显示出来了**，应该「完成条件、点 NPC 之后才出现」。

原端事实（只读核对，无猜测）：

- WZ 里 `922010100..922010900` 的 `next00` 除第 6 关塔顶 `922010600`（`pt=2`，本来就有光洞）外**全是 `pt=5`（隐形门，客户端不画任何东西）**；来源 `MapleDashResources/Data/Map/Map/Map9/9220101xx.img` 与源端 `clien/Data` 一致。
- 关卡隔离写在 `gms-server/scripts/portal/lpqN.js` 的 `enter(pi)` 里：`var avail = eim.getProperty("Nstageclear"); if (avail == null) { dropMessage(5,"某种封印阻挡着这扇门，暂时无法进入。"); return false; }`，通关才 `changeMap`。闸门属性逐关为 lpq0..lpq7 → `1,2,3,4,5,5,7,8stageclear`（第 6 关塔顶的 lpq5 判 `5stageclear`，进图顺带置 `6stageclear`）；KPQ `kpq0..4` → `1..5stageclear`。
- `GenericPortal.enterPortal` 脚本分支 **绝不回退 tm/tn**（移植端 `NpcSession.RunPortal` 返回 true = 「脚本跑过了」，`FieldDirector.UsePortal` 因此本来就不会回退，无需改动）；`PortalFactory.loadPortal` 只读 6 个字段，**没有「初始隐藏」位**。

移植端真正的问题：`GameHUD` 的「按 ↑ / W 进入传送门」只看脚下有没有 `pt=1/2/4/5/…` 的门，于是站在还没清关的隐形 `next00` 上也会弹提示 —— 观感就是「通关门一开始就在那儿」。

修复：新增 `Assets/Scripts/Field/PortalGate.cs`（源端脚本逐条手抄的 `script → 闸门属性` 表），`FieldDirector.FindPortal` 在距离判定之后过滤掉闸门未开的门：这类门**不进 `nearPortal`**（不提示、不被当成可进入的门），只在按 ↑ 上升沿时把脚本跑一遍，让脚本自己回「某种封印阻挡着这扇门」（与源端唯一的反馈一致）。表里没有的脚本一律当「没闸门」，完全退回改动前行为。

- 为什么不用静态解析：LPQ 把 `getProperty` 结果存进局部变量 `avail` 再判 null，第一版正则只认出 lpq5 里那条 `6stageclear`（还是「进图置位」而非闸门）；GPQ 用 `getIntProperty` 复合条件更不可靠。故改为显式表 + 回归钉住。
- 回归：`Assets/Editor/PqPortalGateChecks.cs`（`-executeMethod PqPortalGateChecks.Run`，19 项 PASS）；`PlaySmokeTest -smokeMap 922010100 -smokePqGate 1` 实机核对 `next00 pt=5 script=lpq0 tm=922010200 闸门属性=1stageclear 现在开着=False / NearPortal=null / HUD 提示=不显示`（EXIT=0）；`--pq-feedback` **183/0**；`unity-compile-check` 0 error / 0 warning。
- 遗留：`NativeEventScriptService.LinkPortal`（`linkToNextStage`/`linkPortalToScript` 的出口）仍然只声明不赋值。LPQ/KPQ 的门靠 WZ 自带脚本名 + 脚本内闸门即可工作；若要给「WZ 里没有脚本」的事件门补 `setScriptName`，需先确认不会把 GPQ 这类 `glpqPortalN` 的门改成不存在的 `glpqX`。
