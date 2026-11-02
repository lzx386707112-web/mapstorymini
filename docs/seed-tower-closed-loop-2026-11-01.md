# 起源之塔（SeedTower 1～20 层）闭环 —— 2026-11-01

反馈清单第 11 项。目标按交接文档的口径：**先把 `SeedTowerCompat` 接到真实对象和战斗/移动/反应堆钩子，
再补事件地图和玩家 API，继续跑原 JS，不把每层规则重写进 C#。**

结果：宿主侧缺口补齐，20 层整场 + 各层失败分支全部由源端原脚本跑通。
`SeedTowerChecks`（编辑器 batchmode，隔离临时库，真实 `GameServer/NpcSession/NpcApi/事件宿主`）
**46 步全过，退出码 0**。

复跑：

```
/Applications/Unity/Unity.app/Contents/MacOS/Unity -batchmode -nographics \
  -projectPath MapleDash -executeMethod SeedTowerChecks.Run -logFile /tmp/seedtower.log
```

---

## 1. 跑的是哪几份原脚本（一行没改）

| 脚本 | 作用 |
| --- | --- |
| `event/SeedTower20.js`（544 行） | 1～20 层的判定、刷怪、落石、补油、计时、积分全在这一份里 |
| `npc/2540000.js`（129 行） | 艾丽西亚：大厅菜单（挑战/返回/起源宝库四项）+ 塔内「继续/结束本次挑战」 |
| `portal/seedTower.js`（13 行） | 所有塔门的唯一入口，转 `seedPortal` |
| `reactor/9921000.js` `9921001.js` `9922002.js` `9922003.js` | 第 18 层椰子树，转 `seedCoconut` |
| `portal/<各层>/{safeZoneTrap,stage9ptPuzzle,stage12trap00…}.js` | 各层陷阱/谜题门 |

C# 侧只补宿主，没有任何一层的规则写进 C#。

---

## 2. 宿主侧补了什么

### 2.1 `Field/SeedTowerCompat.cs`（源端 `org.gms.server.life.SeedTowerCompat`）

脚本面（`Java.type('org.gms.server.life.SeedTowerCompat')`）只有两个方法，其余四个是服务端内部钩子：

- `move(mob,x,y,?,flying)` → `NativeEventMap.FindByHost` → `Mob.ScriptMove`（改服务端坐标 + 广播）
- `spiderWeb(boss)` → `NativeController.ScriptReflect(4000)`

服务端钩子（都已接线，且各有一份「纯规则版」供无渲染回归验证）：

| 钩子 | 接在哪 | 规则 |
| --- | --- | --- |
| `handleAttack` | `SkillSystem.HitAt` / `PlayerCtrl.HitEnemy` / `SwingEmpty` | 仅 992014000 + 普攻；`invokeScriptFunction("seedAttack", eim, player, skill, targetOid)` |
| `protectsTarget` | `EnemyUnit.TakeHit` / `TakeReplayHit` | 第 14 层保护 9309116/9309117；第 8/13 层保护 9309400..9309402 |
| `controls` | `EnemyUnit.ScriptControlProbe`（0.5s 轮询） | `seedControlled.<mapId>.<oid>=="1"` 的怪位移只由脚本决定 |
| `allowReactorHit` | `MapReactorService.HitPlayer` 最前面 | 992002000 的 9260004..9260007 直接拒绝；992018000 走 `seedReactorHit` 的 500ms 节流 + 重生锁 |

### 2.2 事件实例地图 `Field/NativeEventMap.cs`

每实例一份私有地图（`MapService.Register/Unregister`，Scope 取负数，`dispose` 时回收），绑定脚本要用的面：

- 地图：`getId/getMapId/getMapName/getFieldLimit/getPortal/resetPQ/countMonsters/getSpawnedMonstersOnMap/getAllMonsters/getMonsters/getMobs/getMonsterById/getMonsterByOid/killMonster/killAllMonsters/instanceMapRespawn/spawnMonster/spawnMonsterOnGroundBelow/spawnItemDrop/broadcastMessage/getFootholds`
- 怪物宿主：`getId/getObjectId/getMap/getName/getHp/getMaxHp/heal/getPosition/setPosition/isAlive`
- 反应堆宿主：`getId/getObjectId/getState/getName/getPosition/resetReactorActions`

### 2.3 事件实例 `Server/Scripting/NativeEventScriptService.cs`

`getInstanceMap/getMapInstance`、`getTimeLeft/isTimerStarted/startEventTimer/restartEventTimer/stopEventTimer/schedule/startEvent/setEventCleared/showClearEffect/dropMessage/getPlayers/getPlayerCount/getPlayerById/isLeader/unregisterPlayer/dispose`，
以及 `startInstance` 的全部重载（`(player)`、`(party,map[,difficulty])`、`(lobby,player,leader,difficulty)`）。

### 2.4 反应堆脚本宿主 `Server/Scripting/NativeReactorScriptService.cs`

`reactor/9922003.js` 等写的是 `eim.invokeScriptFunction("seedCoconut", eim, rm.getPlayer(), rm.getReactor())`。
`rm` 上原来没有这两个成员，第 18 层会整层报错。现在绑定：

- `getPlayer` → `MapReactorService.Player`（客户端在 `GameSession`、假人链在 `BotRuntime` 注入）
- `getReactor` → 最小宿主 `getId/getObjectId/getState/getName/isAlive/getPosition`

### 2.5 无渲染宿主下的玩家 API `Npc/NpcApi.cs`

事件脚本大量用 `chr.getHp()/isAlive()/getPosition()`。没有 `PlayerCtrl` 时（batchmode、纯服务端宿主）
原来是 `Pc.hp` 恒 0 → `isAlive()` 恒 false，`tick` 第一拍就 `fail("角色死亡")`。

现在 `getHp/getMp/getMaxHp/getMaxMp/updateHp/updateMp/addHP/addMP/updateHpMp/isAlive` 统一走
`CurrentHp/CurrentMp`（有 `Pc` 用 `Pc`，没有就用换角色时从存档初始化的镜像），
`getPosition()` 回落到 `HeadlessPosition`。

### 2.6 其它

- `Scripting/JsDate.cs`：`Date.now()` 可注入时钟（事件脚本的 200ms 节流、3 分钟限时、补油预警都靠它）。
- `Field/FieldCatalog.cs` + `Wz/NativeMapView.cs`：`FieldPortal.id` 补上（源端 `Portal.getId()`），
  `cm.getPortal().getId()` 才有值。
- 数据：`Assets/Resources/Bot/Maps/992.json` 补 992002000（4 块石碑）/992006000/992018000（24 棵椰子树）的
  `reactors`；`ReactorTemplates.json` 已含 9260004 / 9922002 / 9922003 / 9260020。

---

## 3. 逐条对照验收

| 验收项 | 结果 | 用例 |
| --- | --- | --- |
| 每层至少成功/失败各一条 | ✅ 20 层成功链路 + 9 条失败分支 | 见下 |
| 第 1 层普通怪 100 只 + BOSS，两种击杀顺序都可完成 | ✅ | ① 满 100 只再杀 BOSS（通关用例）；② 只满 100 只不杀 BOSS → `out00` 被挡在本层 |
| 第 14 层非法技能 | ✅ | `HandleAttack(100,0)` → `seedAttack` 只 `dropMessage("[本层只能使用普通攻击]")`，`shots` 不涨 |
| 第 14 层 99 发 | ✅ | 98 发仍在本层，第 99 发 `fail("弹药耗尽")` → 回大厅 0 分 |
| 3 次以上空挥扣时 | ✅ | 前 3 次免费，第 4 次 `remaining -= 30000` + `restartEventTimer` |
| 3 分钟限时 | ✅ | `age>=180000` → `fail("三分钟时限到")` |
| 第 19 层过远 | ✅ | 距离 >500 连续 10s（`far>=10000`）→ `fail` |
| 第 19 层死亡 | ✅ | Fleta 死 → tick 的 `escort.isAlive()` 判 false → `fail` |
| 第 20 层补油成功 | ✅ | 预警 6s 没打断 → `heal(maxHp*10%)` |
| 第 20 层补油被打断 | ✅ | 入口 `roar` 门 → `seedRoar` 清 `oilAt` |
| 通关 40 分 | ✅ | `award="clear"` → 起源积分 40 / 累计 40 |
| 超时 = 已过层数 ×2 | ✅ | 记 3 层后耗尽 50 分钟 → +6 分 |
| 主动退出 = 0 | ✅ | 塔内 NPC「结束本次挑战」（先 `markClear` 记 1 层也不发分） |
| 死亡 = 0 | ✅ | `addHP(-hp)` → tick `isAlive` → 结束，积分不变 |
| 写入当前/累计并重登校验 | ✅ | `extend_value(extend_type=21)` 落库，`CharService.Load` 重读后大厅仍显示 40 |
| 不可重复结算 | ✅ | 每条用例都对比「结算前/后」的积分值，只有该发分的用例会变 |

另外覆盖到的、验收没点名但脚本里有的分支：

- 第 2 层四块石碑：错卡不消耗也不通过、正确卡消耗并 `resetReactorActions(7)`、四块齐才 `markClear`；
  石碑物理攻击被 `allowReactorHit` 拦下。
- 第 8 层火柱：9s 预警 → 命中把 HP/MP 压到 1%（不致死）→ 1s 后视觉怪自行消失。
- 第 10 层：25s 织网（反伤）→ 蜘蛛卵被打裂出 3 只幼蛛 → 击败巨大蜘蛛过关。
- 第 13 层：五波共 80 只（一波 16，清完自动下一波）；漏 5 只 → `fail("石碑被击碎")`；青色落石 `addHP(-hp)` 致死。
- 第 18 层：500ms 受击节流、没打满 3/4 下不掉落、四击树必掉 1 个椰子、三击树的「掉椰子 / 裂 3 只螃蟹」两条分支、
  15 秒重生锁到期 `resetReactorActions(0)`。
- 组队重载 `startInstance(party, map)`：本地队长能起实例，远端队长（`LocalPlayerId` 不匹配）被拒。
- 中途离图：`changedMap` → `end()` → 把已经离图的玩家送回大厅（源端 `end()` 里就有 `chr.changeMap(exitMap)`）。

---

## 4. 回归里几个必须写下来的坑（都踩过）

1. **`markClear` 不停止计时器**。所以 `enterFloor` 里 `set(remaining, getTimeLeft())` 只有在计时器活着时才有意义；
   休息层（5/15）走的是 `if(floor!=5&&floor!=15)` 的豁免分支。回归里 `Jump()` 因此要先断言 `isTimerStarted()`。
2. **休息层会「进层即记一层」**。`enterFloor(5)` / `enterFloor(15)` 直接 `markClear`，
   所以一进第 5 层 `cleared` 就是 5、一进第 15 层就是 15，不是 4/14。
3. **第 2 层石碑初始态不是 0**。`enterFloor` 里 `reactor.resetReactorActions(rank-1)`，
   封印等级每局随机 1..7，所以断言必须写成「state == seal.X - 1」。
4. **第 2 层卡牌 id 是 `4009900 + 色序号*7 + (卡面数字-1)`**，能过的是「卡面数字 **大于** 封印等级」；
   封印等级 7 时没有更大的同色卡，只能用紫卡 4009928。
5. **第 13 层的 tick 是 `eim.schedule("tick",500)`**。回归每拍必须推进 ≥500ms 才会真的跑一拍；
   dt=500 时怪走 55px，小于相邻生成点间距 65px，才能保证「一拍只漏一只」（200ms 推进会让一拍漏两只，`leaks` 从 3 直接跳到 5）。
6. **第 18 层椰子走地面掉落**（`map.spawnItemDrop`），要等 tick 的 `collect` 把它收进背包才计分。
   生产链路是 `GroundItemBridge`（`MapItemService.Spawned` → `GroundItem` → 点击 → `MapItemPickup`）；
   batchmode 没有 `FieldDirector` 可点，回归里显式「走过去捡」。
7. **第 19 层 Fleta 死亡不是 `friendlyKilled`**。BeiDou 的 `MonsterStats.friendly` 在整个 Java 源码里
   没有任何赋值点，所以 `isFriendly()` 恒 false，`friendlyKilled` 永远不触发；
   真正起作用的是 tick 里的 `if(!escort || !escort.isAlive()) fail(...)`。移植端保持一致（不额外造一个 friendly 标记）。
8. **`GameServer.Bots` 懒初始化会抢时钟**。`BotRuntime` 构造函数把 `MapReactorService.Clock` 覆盖成
   `BotScheduler.Clock`；事件地图一建出来它就跑了，所以回归要在 `StartTower` 之后重新钉回可控时钟。
9. **配置项是毫秒时间戳**（`warningAt` / `oilAt` / `lastShot`），读它们必须用 long，Int32 一定溢出翻负。

---

## 5. 已知未做 / 边界（不能包装成已完成）

- **事件倒计时原图 UI 没接**：`NativeEventScripts` 里 `service.Clock` 回调只打了
  `[EventScript] Clock display not migrated: SeedTower20-0/3000` 的 warning，塔内剩余时间还没画到原 IMG 时钟上。
- **塔内 clear 特效**：`showClearEffect` 目前退化成系统提示，原 `Effect/quest/party/clear` + `Sound/Party1/Clear` 未迁移。
- **远征队入口**：`startInstance(Expedition)` 仍明确抛「未迁移」，源端这条分支本工程还没有远征队系统。
- **掉线中断矩阵**：只覆盖了 `changedMap` / 死亡 / 主动退出 / 超时，真实断线重连没走。
- **实机未验**：本条验收在逻辑层完成（batchmode，真实宿主无渲染）。手感、特效、门的位置、掉落可见性需要真机复核。

---

## 6. 回归入口

`Assets/Editor/SeedTowerChecks.cs`（46 步）。结构照 `CommerciVoyageChecks` / `MonsterParkChecks`：
隔离临时 DB、注入 `NativeEventScripts.Clock` 与 `JsDate.Clock`、注入反应堆时钟/脚本源/实例/玩家、
收集 `LogError`（最后断言 `errors.Count == 0`）、`EditorApplication.Exit(exitCode)`。

用例清单：

```
CheckLobbyMenu          大厅菜单 / 积分读数 / 起源宝库四项 / 返回射手村
CheckPortalLobby        大厅 ptOut、go1F、非塔图的 seedTower 门
CheckHappyPath          第 1→20 层整场 + 通关 40 分 + 重登校验
CheckFloor1Incomplete   只打满 100 只不打 BOSS → 门挡住
CheckTowerMobGuards     石碑不可打 / seedReactorHit 只管第 18 层 / protectsTarget 表
CheckFloor13Leak        漏 5 只 → 石碑被击碎
CheckFloor13RockDeath   青色落石致死 → 结束
CheckFloor14Ammo        99 发打空
CheckFloor14Deadline    3 分钟限时
CheckFloor19TooFar      离 Fleta 太远
CheckFloor19FletaDeath  Fleta 死亡
CheckFloor18Reactor     节流 / 未打满不掉 / 四击必掉 / 三击树两分支
CheckTimeoutAward       超时按已过层数 ×2
CheckVoluntaryExit      塔内 NPC「结束本次挑战」
CheckDeathAward         角色死亡
CheckLeaveMap           中途离图
CheckPartyOverload      组队重载（本地队长 / 远端队长）
```
