# 事件宿主与客户端表现：2026-11-01 补完记录

承接 [十二项反馈交接](feedback-12-items-handoff-2026-11-01.md) 里的「仍未做」清单。
本轮按「先补宿主、再补表现、最后补回归」的顺序做，规则仍然只写在原 JS 里。

## 1. 事件实例的三条源端入口

### playerDisconnected（第 9 / 10 / 11 项共同缺口）

源端在 `Client.java:967-970` 断线时调 `eim.playerDisconnected(player)`，
`EventInstanceManager.java:597-605` 的实现只有两件事：调脚本 `playerDisconnected(eim, chr)`，
再把实例登记进 `EventRecallCoordinator`（回场用，移植端没有该协调器）。

- 宿主：`NativeEventScriptService.Instance.PlayerDisconnected(JsValue)`，**不**替脚本摘人，
  与源端一致（摘人由脚本自己的 `unregisterPlayer` 完成）。
- 客户端：`NativeEventScripts.PlayerDisconnected()`，在 `GameSession.DeselectChar()` / `Logout()`
  里、`Character` 置空**之前**调用（否则实例反查不到玩家）。
- 回归：`EventHostChecks.CheckPlayerDisconnected`（脚本收到玩家 → unregisterPlayer → playerUnregistered）。

### monsterValue → killCount（第 9 项 `monsterValue` 记账）

`EventInstanceManager.java:607-626` 是 `monsterKilled(Character, Monster)` 重载，
和 `monsterKilled(Monster, hasKiller)` 是**两条独立入口**：前者只问脚本
`monsterValue(mobId)` 要积分并累加到 `killCount[chr]`，后者才是脚本的击杀回调。

- 宿主：`Instance.MonsterKilledBy(killer, mobId)` / `GetKillCount(chr)`，并把 `getKillCount`
  绑到实例宿主上（`EventInstanceManager.java:628`，各 PQ 用它算奖励档位）。
- 调用点：`NativeEventMap.Remove()` 在 `Instance.MonsterKilled(...)` 之前记账，
  只有 `hasKiller` 成立才记（源端要求 killerId 对应在线角色）。
- `dispose` 时 `killCount` 清空（源端 `:677`）。
- 回归：`EventHostChecks.CheckMonsterValueKillCount`。

### startInstance 重载（第 9 / 11 项）

`EventHostChecks.CheckStartInstanceOverloads` 把 `EventManager` 的五条分派都钉住了：

| 重载 | 源端 | 移植端行为 |
|---|---|---|
| `startInstance(Character)` | `EventManager.java:634` | 起实例，队长=该玩家 |
| `startInstance(Party, MapleMap)` | `:715` → `:738` | 队长必须是本地真实角色，否则 false |
| `startInstance(int, Character)` | `:646` | 显式大厅 |
| `startInstance(int, Party, MapleMap, int)` | `:820` | 显式大厅 + difficulty 传给 `setup` |
| `startInstance(Expedition[,…])` | `:531-605` | 见下节，已按源端顺序实现 |

### 远征队（更正 2026-11-01 早先的错误结论）

**之前写「所有脚本里没有任何 Expedition 字样」是错的**：`grep -rl Expedition scripts/` 实际有
**26 个文件**命中——`BeiDouSpecial/露希妲远征.js` 直接用
`Java.type('org.gms.server.expeditions.ExpeditionType')` + `cm.getExpedition/createExpedition/endExpedition`，
`event/{Zakum,PinkBean,Balrog,Cygnus,Showa,Scarga,Akayrum,Papulatus,CWKPQ}Battle*.js` 用
`eim.isExpeditionTeamLackingNow(true, minPlayers, player)`。这条入口不是死路，必须实现。

已补：

- `Assets/Scripts/Server/Expedition.cs`
  - `ExpeditionType`：`ExpeditionType.java:34-50` 的 17 个枚举 + `(minSize,maxSize,minLevel,maxLevel,registrationMinutes)`
    五参数逐行照抄（`ARIANT*` 7 人 / 20 级，`LUCID` 220 级，`CWKPQ` 90 级……）。
  - `Expedition`：队长、成员表（队长排第一的 `getMemberList`）、黑名单、`addMember/addMemberInt`
    的 0-3 返回码、`isRegistering/isInProgress`、招募 CLOCK 300 秒、`Dispose(log)` 写战绩。
  - `ExpeditionRegistry`：`Channel.java:411-437` 的同类型唯一表 + 招募到点回收（`Tick`）。
  - `ExpeditionBossLog`：`EXPEDITION_BOSSES` 用 `MobId.java` 真值（含 `isCygnusBoss` 8850000..8850013），
    `use_enable_daily_expeditions` 默认 false 时 `attemptBoss` 恒 true。
- `cm.getExpedition / createExpedition / endExpedition / isLeaderExpedition / getExpeditionMemberNames`
  按 `AbstractPlayerInteraction.java:1279-1320` 接到上面这套对象（原来全是 `NotAvailable` / 空列表）。
- `EventManager.startInstance(Expedition[,…])`：按 `:531-605` 的顺序 **选 lobby → setup →
  setLeader → `exped.Start()` → registerExpedition（只登记还在招募图上的成员）→ `afterSetup`**。
- `eim.isExpeditionTeamLackingNow`（`:1176-1183`，不看队长也不看 minPlayers）、
  `eim.checkEventTeamLacking`（`:1165-1174`）、`eim.isEventTeamTogether`（`:1196-1218`）。
- 回归 `Assets/Editor/ExpeditionChecks.cs`（`ExpeditionChecks.Run`）。

### 露希妲副本本体（2026-11-01 补齐）

早先这条写的是「`event/LucidBattle.js` 还依赖三个没提供的 Java 类型，副本内容需单独一轮」。
现已全部补完，入口到本体都能跑：

- **`Assets/Scripts/Field/LucidBossCompat.cs`**：`org.gms.server.life.LucidBossCompat` 的移植层，
  逐条照 `LucidBossCompat.java`——`startPhase/stop/finishFurySuccess/protectFurySuccessMob`
  四个脚本面方法 + `Encounter` 控制器（250ms 一拍）：
  蝴蝶积攒（40 上限，血档 5s/4.5s/4s/3s/2s，破 40 时炸场 30%）、妖精之尘（10%，1200×650 矩形）、
  召唤噩梦（蘑菇 4 / 石巨人 15）、巨龙吐息（100%，6300ms）、激光雨（18%×2）、幻影连射（10%×12）、
  冲刺（RUSH_PATH 加权插值，同轮只结算一次）、碎玻璃（六块区域轮转 + 3s 后补石巨人）、
  障碍伤害（石巨人 ±20×510 竖条）、感染炸弹（35%，3s 后 500×500）。
  阶段 3 的 45 秒暴怒失败：BOSS 换 8880154 → 3s 打满 100% → 10s 后 8880141 以 16% 血复活。
- **`Assets/Scripts/Field/LucidTables.cs`**：`RUSH_PATH` / 两组蝴蝶坐标 / 六块碎玻璃区域，
  与 Java 逐值一致（原来重复在 `NativeLucidEncounter.Data.cs`，这次合并成一份）。
- **`Assets/Scripts/Field/ScriptMonster.cs`**：`LifeFactory.getMonster(int)`。
  源端返回的是「还没进图的 Monster」，脚本先 `setPosition/setFh/setDamageBlockedUntil/addHp`
  再 `spawnMonster(OnGroundBelow)`；移植端进图那一刻由 `NativeEventMap.Mob.ApplyTemplate` 落位。
  顺带补上了 `event/AreaBoss*.js`、`event/PinkBeanBattle.js` 等 20+ 个脚本缺的这条入口。
- **`PacketCreator.showEffect / damagePlayer / movePlayer`**：`showEffect` 走
  `NativeScriptEffect`（`customSkill/*VideoLayer` → MCV 视频，其余 → `Map/Effect.img` 帧动画）。
- **`java.util.concurrent.ThreadLocalRandom`**（`current().nextInt(n)`）、
  **`org.gms.server.maps.AbstractAnimatedMapObject.IDLE_MOVEMENT_PACKET_LENGTH`（=15）**。
- `NativeEventMap` 补 `dropMessage / getAllPlayers / movePlayer / getEventInstance / spawnMonster`
  与服务端控制器要的 `AllMobs / Kill / FindAliveById / SpawnAt / SpawnOnGround / BroadcastEffect`。
- **`eim.getIntProperty(缺失键)` 的口径修正**：源端 `EventInstanceManager.java:793` 缺省按 `"0"` 解析，
  不是抛异常。露希妲的 `playerEntry` 第一句就读 `eliminated_<id>`，以前会直接抛「Invalid event integer property」。
- **`monsterRevive` 回调补上**：源端 `MapleMap.reviveMonster` 在整份 Java 里**没有任何调用点**
  （`monsterRevive` 在源端是死代码）；但移植端客户端真的会按 `Mob.wz/info/revives` 刷后继形态
  （8880141 → 8880152），所以 `NativeMobController.OnKilled` 刷完 revive 后调
  `Instance.ReviveMonster`。不补这一步，露希妲第三阶段的控制器永远不会启动。
- 同时修掉一处双份结算：`NativeBossEncounter.OnSpawn` 对 `eventOwned` 的怪不再建客户端
  `NativeLucidEncounter`，副本里的露希妲只由脚本 + `LucidBossCompat` 驱动。

**跑回归时才发现的三处宿主缺口（都已按源端补齐）：**

- **`player.respawn(eim, map)` 原来是空实现**（`NpcApi`）。源端 `Character.respawn` 是
  「先 `eim.unregisterPlayer(this)`，再 `changeMap`」——露希妲死亡满 50 次就靠这一步退本，
  之后脚本的 `disposeIfEmpty` 才会回收实例。空实现会让实例永远挂在 manager 上，
  而 `LucidBattle` 只有 1 个 lobby → 这个角色之后再开本一律失败（`startInstance` 返回 false，
  玩家看到「其他远征队已经开始挑战」）。现按源端实现：摘人 + 走 `FieldDirector.WarpTo`（无场景时落 `Profile.LastMapId`）。
  受影响的脚本共 10 个（`event/NineSpirit|GuardianNex|GuildQuest|RockSpirit|RockSpiritVIP|Puppeteer|
  Cygnus_Magic_Library|LucidBattle`、`map/onUserEnter/armoredSamuraiBoss|emperorToadBoss`）。
- **`EventManager` 释放实例后要等 `event_lobby_delay`（默认 10s）才从 `instances` 摘名字**
  （`Manager.Remove` 是延后调度，源端同款）。所以「按角色查实例」为 null ≠ 实例已经没了；
  回归里判断「上一场收干净了」只能看 manager 的 `instances` 表，别用 `NativeEventScripts.Instance()`。
- **同一拍里可能连播两个特效**：`castFlowerExplosion`（脚本 2 秒循环）和控制器技能可能落在同一毫秒，
  谁后播谁覆盖 `LastPacket`。`NativeEventMap` 因此加了 `EffectLog`（按顺序记录全部 `showEffect`），
  回归断言用 `SawEffect(mapId, 片段)` 而不是 `LastPacket.Contains`；`Advance` 也改成按 250ms 一拍推进
  （源端 `TimerManager.register(this, 250)`），避免一次跨几秒把两件事压进同一拍。

- 回归 `Assets/Editor/LucidBattleChecks.cs`（`LucidBattleChecks.Run`，**21 项全过 / 退出码 0**）：
  类型解析 → 门/NPC/远征队入口 → 阶段 1 技能排程 → 花爆扣血（35% + 变体不重复）→ P1 死亡转阶段 2 →
  阶段 2 四个专属技能 → P3 复苏回调 → 45 秒暴怒失败重置（16% 血）→ 击杀 P3 通关 → 50 次死亡上限 →
  eliminated 分支 → 中途离图回收。

## 2. 客户端表现

### 事件倒计时（第 9 / 10 / 11 项共同缺口）

链路：`EventInstanceManager.startEventTimer` → `PacketCreator.getClock(秒)`（`CLOCK`，type 2）→
客户端倒计时；`dismissEventTimer` → `removeClock()`（`STOP_CLOCK`）。

素材证据：EXE 字符串池 2783 = `Map/Obj/etc.img/clock/fontTime`。
`etc.img` 下 `coconut` / `battleField` / `space` / `killing` / `partyRace` 五个面板都用
`fontScore/fontTime` 以 UOL 指回 `../clock/fontTime`，说明它是客户端通用倒计时字体；
`clock` 节点本身**只有字体、没有背板**（数字 `0-9` 各 26×35，`comma` 17×35 实际是冒号）。

- 实现：`NativeScreenEffect` 同目录的 `NativeEventClock`，按原图逐字符拼 `mm:ss`，
  只叠数字不自造底板；`Clock(instance,null)` 收掉。
- 位置：`Top=6`、水平居中。**版面常量没有从 EXE 证出来**（CLOCK 包里只有秒数，没有坐标），
  取的是原版观感；实机若位置不对只改 `Top` 和居中方式，别动字体路径。
- 回归：`NativeEventUiChecks.CheckClockGlyphs`（11 个图块尺寸）+ `CheckClockState`。

### clear 特效（第 9 / 11 项）

`EventInstanceManager.java:1293-1329` 四个重载最终都是
`showEffect("quest/party/clear")` + `playSound("Party1/Clear")`，`hasGate` 时再加
`environmentChange("gate", 2)`。

素材：`Map/Effect.img/quest/party/clear` 共 4 帧，delay 依次 130 / 130 / 130 / 2000，
每帧自带 origin（299,184 / 255,180 / 183,129 / 183,93）。

- 实现：`NativeScreenEffect`——帧序列播放器，锚点取屏幕中心（`showEffect` 是客户端屏幕特效，
  不走 `Map/Obj` 那套坐标），按 origin 反推左上角。
- 音效仍未迁移（本工程没有 AudioManager），所以只播动画。
- 原来那句 `[完成] xxx 已通过。` 的系统提示**已移除**：源端这里只有特效和音效，没有文字。
- 回归：`NativeEventUiChecks.CheckClearEffectFrames` / `CheckScreenEffectPlayback`。

## 3. 拍卖（MTS）入口的真相（第 12 项）

2026-11-02 纠正：此前把封包入口与静态方法混为一谈，本节旧结论不成立。

- `handlePacket` 第 51–56 行检查 `use_mts`，关闭时才重开 9900001。
- 菜单脚本的 `拍卖系统()` **直接调用 `enterMTS(client)`**，不经过该配置检查。
- 静态方法第 59 行开始，校验后调用 `openCashShop(c, true)`，进入原版 MTS。
- 对应客户端资源是 `UI/ITC.img`，不是普通商城。
- 当前项目尚缺 ITC 窗口及 `mts_items` 交易服务。已移除错误的菜单回环与默认配置静默成功；该入口明确报告未迁移，**第 11 项仍未完成**。

详见 reverse skill 的 `menu-feedback-2026-11-02.md`，不得沿用旧版「默认返回菜单即源端行为」结论。

## 4. quest/2001 的交付 NRE

`QuestNpcChecks` 从 24472/4 变成 24471/5 的根因不在任务逻辑，而在 `gainFame`：

- `quest/2001.js` 的交付顺序是 `gainItem×5 → gainExp → gainMeso → gainFame → completeQuest`；
- 旧 `NpcApi.ChangeFame` 直接 `GameSession.Net.Script(...)`，而编辑器回归里 `GameSession` 没有接线
  （`Net == null`）→ NRE 打断脚本 → `completeQuest` 根本没跑到；
- 现在按源端 `Character.gainFame`（`Character.java:3005`，直接改值并夹 -30000..30000）的语义：
  没有角色就只跳过这条表现，不中断脚本；有传输层仍然问服务端拿同一份实现。

## 5. 航海六条航线逐波清完（第 10 项）

`CommerciVoyageChecks.CheckRouteTable` 原来只校验 routeWaves 表的数字（首波数量 / 波数），
现在对六条航线**每一条都真的逐波清空**：断言波次下标、`waveTransition=1` 防重复推进、
2 秒后刷下一波、换波时仍在航线图内、末波清完 `finished=1`。

## 6. 复跑方式

```bash
cd MapleDash/tools/unity-compile-check && dotnet build -v q --nologo   # 0 error
# 工作目录 MapleDash，逐个跑（Unity 同一时间只能开一个项目实例）
Unity -batchmode -nographics -projectPath "$PWD" -executeMethod ExpeditionChecks.Run    -logFile /tmp/exped.log
Unity -batchmode -nographics -projectPath "$PWD" -executeMethod EventHostChecks.Run       -logFile /tmp/event_host.log
Unity -batchmode -nographics -projectPath "$PWD" -executeMethod NativeEventUiChecks.Run   -logFile /tmp/event_ui.log
Unity -batchmode -nographics -projectPath "$PWD" -executeMethod QuestNpcChecks.Run        -logFile /tmp/quest.log
Unity -batchmode -nographics -projectPath "$PWD" -executeMethod CommerciVoyageChecks.Run  -logFile /tmp/voyage.log
Unity -batchmode -nographics -projectPath "$PWD" -executeMethod MenuFeedbackChecks.Run    -logFile /tmp/menu.log
```

注意：`Temp/UnityLockfile` 残留会报 `Multiple Unity instances cannot open the same project`，
跑之前确认没有别的一次在跑（或删掉残留锁）。
