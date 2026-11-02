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

## 4. 边界

- 单机实例只登记本地真实角色，`registerParty` 不会伪造远端队员；资格判定仍按源端脚本口径跑，
  人数要求靠 `use_enable_solo_expeditions` 放行。
- `PiratePQ` 的 `spawnAllMonsterIdFromMapSpawnList` 每次调用都会重新生成该模板的全部刷怪点
  （源端每 10 秒一轮，怪被打死才减少），未做去重，属源端行为。
- 静态扫描只证明「调用名有绑定」，不代表分支语义已完整验证；Android 实机仍需人工过一遍四个副本。
