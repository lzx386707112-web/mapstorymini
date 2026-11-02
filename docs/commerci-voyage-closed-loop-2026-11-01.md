# 航海（凯梅尔兹贸易）闭环

更新时间：2026-11-01。对应交接文档第 10 项。目标是把「菜单不报错」推进到「买货 → 出航 → 清波/超时 → 到港 → 领取」整条链路
在正式宿主上有回归证据，脚本本身一行未改。

## 1. 入口与数据口径

| 项 | 位置 | 说明 |
|---|---|---|
| 交易所 / 出航 | `npc/9390220.js` | 主菜单 7 项：贸易、舰船状态、买货、船舱、升船舱、升能量、升船 |
| 领取 / 兑换 | `npc/9390221.js` | 待领收益领取 + 26 件漩涡装备兑换 |
| 事件 | `event/CommerciVoyage.js` | 6 条航线、波次表、到港结算 |
| 状态键 | `areaInfo_32010` | 落 `extend_value`：`extend_type=21`（CHARACTER 永久）、`extend_name=areaInfo_32010`、`C1` 编码共 21 段 |
| 复活/出口 | `865000001` | 交易所；航线图 `865000100/200/300/400/501/900` |

状态段序（`encodeState`）：`0 C1 | 1 day | 2 tier | 3 level | 4 exp | 5 energy | 6 energyCap | 7 cargoCap |
8 unlockMask | 9 counts | 10 stock | 11 cargo | 12 pendingCoins | 13 pendingExp | 14 pendingItem |
15 pendingItemQty | 16 active | 17 activeRoute | 18 activeReward | 19 activeExp | 20 activeCargo`。

## 2. 本轮补的宿主缺口

1. **`startInstance` 重载识别（真 bug）**：`IsParty` 原先用 `GetMember(value,"getLeaderId")` 探测。
   移植端未实现的成员由 `Lenient` 兜底返回「调用才抛」的函数，因此 `GetMember` 对**任何**角色宿主都返回函数，
   结果 `startInstance(-1, player, player, difficulty)` 被误判成队伍重载，进而 `party.getLeader()` 报
   `Unmigrated server API: player.getLeader`——9390220 单人出航直接失败。
   现改为只看宿主自己显式绑定的成员（`JsHostObject.Members.Has("getLeaderId")`），单参的远征队判定同样处理。
2. **队伍成员宿主**：`BuildParty` 补 `getPartyMembers` / `getPartyMembersOnline`，元素按源端 `PartyCharacter`
   给 `getPlayer()/isLeader()/isOnline()/getMapId()/getLevel()/getId()/getName()`。远端成员没有真实 `Character`，
   `getPlayer()` 按源端语义返回 null。`getLeader()` 的远端分支也补 `isLeader/getPlayer`。
3. **`Java.type("X[]")`**：`CommerciVoyage.getEligibleParty` 末尾用 `Java.to(eligible, Java.type("...PartyCharacter[]"))`。
   移植端集合本身就是 JS 数组、`Java.to` 是恒等操作，原来却因「class not migrated」直接报错；现对 `[]` 结尾返回类型占位。

事件侧需要的 `map.resetPQ / killAllMonsters / countMonsters / getFootholds().findBelow /
spawnMonsterOnGroundBelow / eim.setIntProperty / schedule / startEventTimer / stopEventTimer /
setEventCleared / unregisterPlayer / dispose` 在 `NativeEventMap` 与 `NativeEventScriptService` 已有，
本轮未新增；`LifeFactory.getMonster`（含 `setFh`）此前已迁移。

## 3. 与源端对齐的业务规则

- **出航**（9390220 `launchVoyage`）：先逐人校验（已解锁 / 能量足够 / 有货 / 无待领收益 / 不在航行中），
  再备份状态 → 扣能量 → 置 `active=1`、`activeRoute`、`activeReward=cargoReward`、`activeExp=routeExperience`、
  `activeCargo` → 清空船舱 → 存库；实例起不来就用备份整体回滚（不吞能量和货物）。
- **收益**（`cargoReward`）= `routeBaseReward[route] + Σ cargo[i]×goods[i].profit[route]`。肥皂 1 份走 route 0 = 1+2 = 3。
- **波次**（`CommerciVoyage.js`）：`setup` 里 `route=difficulty-1`、`wave=0`，`resetPQ(1)` 后 `killAllMonsters()`；
  `afterSetup` 立刻刷第一波并起 `routeMinutes×60000` 计时。`monsterKilled` 在本图 `countMonsters()>0`
  或 `waveTransition!=0` 时直接返回；清空后置 `waveTransition=1`，还有下一波则 `schedule("spawnWave",2000)`，
  最后一波则 `completeVoyage(eim,true)`。
- **到港结算**：全清 `rewardPercent=100`；超时 `100-剩余波数×10`，下限 50。
  `reward=max(1,floor(activeReward×percent/100))`；BOSS（9390803/9390804）被清时 25% 额外 +5..15；
  route≥3 且当前无待领物品时 10% 掉 `bossLoot`；BOSS 且未满载时 15% 追加稀有货（第 4/5 号）。
  之后 `counts[route]+1`、清 `active` 五段、`schedule("finishVoyage",3000)`、`setEventCleared()`。
- **结束**：`finishVoyage → end(eim) → playerExit(每人)` 送回 865000001，再 `eim.dispose()`。
  源端 `end` 只处理**仍在实例里**的玩家，所以中途离图的玩家不会被强行拉回。
- **中断**：`changedMap` 发现不在本航线图就 `unregisterPlayer`，人数为 0 时 `end`。
  此时不改玩家的 `active`；玩家回交易所再打开菜单时由 `loadState` 的
  「`active==1` 且 `getMapId()==865000001`」分支清掉，属于源端的中断损失语义（货已消耗、不返还）。
- **领取**：先 `canHold(DENARO,n)` 与 `canHold(loot,qty)`，任一不足只提示、**不清 pending**；
  发币发物后 `exp+=pendingExp`，每 10000 升 1 级（上限 10，满级后 exp 归 0），再清 pending 四段。
- **升级**：船舱 `20+(cargoCap-2)×10` 枚、上限按 tier `2/4/6`；能量 `20+((cap-100)/5)×10` 枚、上限 `100/110/120`；
  船只升阶要求 Lv.10 且金币 `1000 万/3000 万`，升阶后等级重置为 1。
- **每日**：`todayKey()` 变化时把能量恢复满、库存回 `[6,5,4,3,1,1]`。

## 4. 回归

`Assets/Editor/CommerciVoyageChecks.cs` → `CommerciVoyageChecks.Run`，24 项断言、退出码 0，真脚本真宿主：

```bash
cd MapleDash
rtk proxy /Applications/Unity/Unity.app/Contents/MacOS/Unity -batchmode -nographics \
  -projectPath "$PWD" -executeMethod CommerciVoyageChecks.Run -logFile /tmp/voyage.log
```

覆盖：主菜单/货物表 → 买货（金币扣款、船舱、库存落库）→ 船舱上限与超量拦截 → 出航（能量/货物/active 存档、
进私有图、第一波 9 只、3 分钟计时）→ 三波逐波清（2 秒换波）→ 全清到港（pending 币/经验/完成次数、清 active）
→ 3 秒回大厅并回收航线图 → 重登后领取（发币、舰船经验）→ 重复领取拦截 → 超时 70% 结算 →
满背包领取被拒且 pending 保留 → 恢复后补领 → 兑换扣 160 枚 → 跨天重置 → 六条航线首波数量与波数 →
解锁支付 → 售罄 → 升级前置/升阶/船舱/能量 → 中途离图 → 组队重载。

日志证据：[voyage-closed-loop.log](evidence/menu-feedback-2026-11-01/voyage-closed-loop.log)。

## 5. 仍未覆盖（不能据此关闭第 10 项）

1. **玩家掉线**：源端 Channel 断线会回调 `eim.playerDisconnected`（`unregisterPlayer` + 没人就 `end`）。
   移植端「断线」等于退出登录/杀进程，没有对应回调链路；本轮只验证了主动换图的 `changedMap`。
2. **Android 实机**：没有出包、没有实机跑图、没有原版事件倒计时 UI（当前仍是一条 warning）。
3. **六条航线只验证首波与波数**：逐波清完整场只做了 route 0；route 3~5 的 4/5 波递进按同一段脚本逻辑，
   但没有逐条跑完。
4. **概率分支**：BOSS 追加币（25%）、route≥3 掉落（10%）、稀有货（15%）用真实随机，回归只断言了区间与
   不出现的分支，没有做可控随机的定值断言。
5. **组队**：只有本地角色是真实 `Character`，多人航海的「全员 140 级同图」校验无法端到端复现；
   组队重载本身（本地队长）已覆盖。
