# 怪物公园六图闭环：本轮完成与剩余

更新时间：2026-11-01（接 [十二项反馈交接](feedback-12-items-handoff-2026-11-01.md) 的第 9 项）。
本轮只推进「怪物公园」这一项，把交接建议里的第 1 步（公园闭环）在正式宿主上跑通并写成回归；
没有动航海、起源之塔、MTS、坐骑和关系戒指。

## 1. 结论

**单人公园流程现在有完整回归证据：入口 → 六张私有图 → 逐图清怪放行 → 末图结算 → 回大厅 → 实例回收。**
回归 `MonsterParkChecks.Run` 13 项全通过、退出码 0；全程没有 Error/Exception 日志。

仍然**不能**说「第 9 项完成」：

- 没有 Android 实机验收（画面、手感、掉帧）。
- 六张图只逐关验证了刷怪/门/结算，没有在实机里真打怪、掉线、死亡三种中断各跑一遍。
- 组队分支只验证了「队长是本地角色」的宿主重载；**远征队 `startInstance(expedition)` 仍显式报缺失**。
- 事件倒计时原图 UI 仍未迁移（只在宿主层打 warning）。

## 2. 本轮改了什么

| 位置 | 改动 | 依据 |
|---|---|---|
| `Server/Scripting/NativeEventScriptService.cs` | 补 `startInstance([lobby,] party, map[, difficulty])` | `EventManager.java:715/726/808`；`registerParty(party,map)` 只登记还在招募图上的成员（`:390`） |
| 同上 | 单参对象没有 `getId` 时明确报 `startInstance(expedition)` 缺迁移 | 源端单参只有 `Character` / `Expedition` 两种 |
| 同上 | 实例从事件表摘除后，**没人留在副本里**就 `dispose()` | 源端 `disposeInstance` 只摘名字、eim 交给 GC；移植端实例地图是 `MapService` 全局资源，不显式回收会漏图 |
| `Field/NativeEventScripts.cs` | `Clock` 可注入 + `TickNow()` 测试缝 | batchmode 不跑 `Update`，10 分钟超时 / 2 秒换波这类分支不注入时钟根本测不到 |
| 同上 | 注入 `LocalPlayerId`（`GameSession.CharacterId`） | 队伍版重载判断「队长是不是真实角色」 |
| `Editor/MonsterParkChecks.cs`（新增） | 公园闭环回归 13 项 | 真实 NPC/portal/event 脚本 + 真实事件宿主 + 隔离数据库 |

没有改任何源端 JS，也没有为了通过测试放宽脚本。

## 3. 回归覆盖的 13 项

1. 大厅 `npc/mPark_in00.js` 原菜单：园区名、十条课程、`今日剩余次数`。
2. 选课后真的起实例（`em.startInstance(cm.getPlayer())`），实例记下 `course` / `parkRegion`。
3. 六张课程图都是独立实例地图（`Scope<0`，脚本仍见原 ID），按原 `life` 刷怪 **16/16/16/16/16/15**。
4. 实例计时按 `eventTime=10` 分钟启动（`getTimeLeft()` 在 599000..600000）。
5. 进入后按园区把每日次数写进 `extend_value`（`type=22 CHARACTER_EXTEND_DAILY`、`name=MPARK_BASIC` = 1）。
6. 次数用完时原脚本拦住第二条路线，且不会新建第二份实例。
7. 五道 `next00` 门：本图还有怪就不放行（`Profile.LastMapId` 不动）；清空才进下一关，且下一关是自己的私有图。
8. 末图 `portal/mPark_final.js`：经验 `level*120=24000`、金币 `level*80=16000`、初级园区 1 枚纪念币 `4310020`。
9. 当日勋章任务（按 `Calendar.DAY_OF_WEEK` 取 `15187/15181..15186`）在进行中时被 `forceCompleteQuest` 完成。
10. 结算走 `setEventCleared`（发事件任务积分），玩家 `playerExit` 回大厅 951000000。
11. `event_lobby_delay`（10 秒）后实例释放，六张私有图的 `MapService` 几何注册一并回收。
12. 满背包分支：经验/金币照发，纪念币按原提示 `其他栏已满…` 不发、也不吞物品。
13. 超时：`clock+601000` 后 `scheduledTimeout → end(eim)` → 回大厅 + 释放地图；关内 `npc/mPark_retire.js` 主动退出回大厅。

另外两项宿主断言（同一回归里）：

- 组队重载拒绝「队长不是本地角色」的远端队伍（移植端没有可注册的真实角色）。
- 组队重载用本地队长 `startInstance(party, map)` 起实例并刷六图。

## 4. 复现

```bash
# 工作目录：MapleDash
rtk proxy /Applications/Unity/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics \
  -projectPath /Users/lizixian/Documents/mxd/mxd-android/MapleDash \
  -executeMethod MonsterParkChecks.Run \
  -logFile /tmp/monster-park.log
```

本轮实际结果（证据 [park-closed-loop.log](evidence/menu-feedback-2026-11-01/park-closed-loop.log)）：

| 检查 | 结果 |
|---|---|
| 公园闭环回归 `MonsterParkChecks.Run` | 13 通过 / 0 失败，退出码 0 |
| 菜单定向回归 `MenuFeedbackChecks.RunFocused` | 74 路径 / 0 错误，退出码 0 |
| 有边界完整菜单审计 `MenuFeedbackChecks.Run` | 688 路径 / 2 错误（仍是 MTS `EnterMTSHandler` 与起源之塔 `SeedTowerCompat`），pendingRoutes=0、deferredCatalogues=16，退出码 1（真实错误，不为 CI 改成 0） |
| 服务端检查 | 726 通过 / 0 失败 |
| 编译（`tools/unity-compile-check`） | 0 error |

没有构建 APK、没有 Android 实机验收。

## 5. 下一步（公园剩余）

1. Android 实机按原流程打一条完整六图路线：清怪→门→结算，记帧时间；顺便看私有图进出有没有残影/错位（`FieldDirector.Enter` 走 `ResolveMap`）。
2. 中断矩阵：死亡（`returnMap=951000000`）、主动退出、掉线、超时各一次，确认不会残留 active 实例或漏图。
3. 事件倒计时原图 UI：`Map/UI.img` 相关节点布局需先逆向，不要自绘；宿主侧 `Clock(instance,seconds)` 已经能拿到秒数，只差渲染。
4. 远征队入口（`startInstance(expedition)`）与 6 人队伍真实成员注册：依赖组队/远征整套系统，目前显式报缺失。
5. `monsterValue` 的角色累计击杀记账仍未接（`MonsterPark.js` 该函数固定返回 1，不影响本闭环，但其他副本依赖它）。
