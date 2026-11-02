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
