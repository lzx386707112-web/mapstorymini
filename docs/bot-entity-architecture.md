# 假人「当真角色」接入统一实体系统：架构方案

> 目标：同图人数、全服在线人数增加时，**帧耗时不变**。
> 手段：把源端 `MapleMap.characters + mapobjects + visibleMapObjects + spawn/move/destroy 事件`
> 这套模型搬到移植版，假人和真人共用一条实体/视野/事件路径，视图不再轮询。
>
> 上游依据：`docs/online-entity-architecture.md`（源端源码核实）；本文只讲**怎么改、改哪里、代价多少**。

---

## 0. 结论先行

现在做不到「人多了不卡」不是因为 AI 慢，而是四个**结构性问题**：

| # | 问题 | 位置 | 复杂度 |
|---|---|---|---|
| 1 | **每个假人都自建一份地形索引**（ground / regions / collision） | `BotMovementRuntime` 构造函数 | O(N × footholds) |
| 2 | **视野是「整图开关」**，不是「谁看得见谁」 | `BotObserverTracker.IsActive(mapId)` | 同图 N 人 = N 人全速跑 |
| 3 | **每帧全量扫**（FindAll + Sort + 遍历全部 bot） | `BotScheduler.Drive` / `BotTasks.Drive` / `BotWander.AdvanceAll` | O(全服在线) / 帧 |
| 4 | **移动还在「序列化封包」**，而消费者为空 | `BotMovementRuntime.Emit` → `BotRuntime.Movement`（无订阅者） | 2 次分配 / 每次快照 |

第 1 条是「同图一多就卡」的头号原因（建卡阶段），第 3 条是「全服在线一多就卡」的原因。
把这四条修掉，剩下的就是纯粹的插值和状态同步 —— 和源端一样，成本只跟**视野内实体数**有关。

**推荐路线：四层（模拟 / 世界 / 视野 / 视图）分离，先摘不改行为的性能果实，再动结构。**
不建议一上来就把模拟搬到后台线程（P5）——那是最后一步，且前四步做完后主线程大概率已经够了。

---

## 1. 验收口径（先定指标，再谈实现）

| 指标 | 目标 | 采集方式 |
|---|---|---|
| 同图 120 假人 + 真人在场，假人系统每帧耗时 | P50 ≤ 1.5ms / P99 ≤ 4ms | `debug_bot_perf=1` → `Bot/Frame` 日志 |
| 全服在线 1000 假人、真人在 1 张图 | 模拟域每帧 ≤ 1ms | 同上 |
| 建卡阶段（换图那一帧） | ≤ 1 帧 8ms（纸娃娃预合成已完成时） | `BotField.Create` 计时 |
| 假人相关 GC | 稳态每帧 0 分配 | Unity Profiler / `BotPerf` |
| 视觉 | 与现状逐帧一致（同位置、同姿势、同气泡） | `PreviewShot` 对照图 |

---

## 2. 目标架构：四层 + 一条事件通道

```
┌─────────────────────── 主线程每帧（FieldDirector.Update） ───────────────────────┐
│  BotWorkPump.BeginFrame()                                                      │
│                                                                                │
│  ① 模拟域（纯 C#，无 UnityEngine）                                                │
│     TickWheel.Advance(now)  ── 只处理到点条目，O(due)，零分配                       │
│       └─ 每个实体按热度取周期：Hot 50ms / Warm 250ms / Cold 2000ms                 │
│       └─ 预算超了 → 顺延到下一帧（不丢、不补、按真实 dt 推进）                        │
│                                                                                │
│  ② 世界域（唯一事实来源）                                                          │
│     MapWorlds.Get(mapId) → MapWorld { oid → MapEntity，dense 数组 }               │
│       实体 = 真人 / 假人（将来可扩到怪物、掉落、召唤物）                              │
│       位置、姿势、revision 都写在这里；视图只读，不回写                               │
│                                                                                │
│  ③ 视野域（AOI，对齐源端 850px）                                                   │
│     Interest.Update(player)  ── 10Hz 增量 diff（或位移 > 64px 立刻算）              │
│       进视野 → Spawn 事件   出视野 → Despawn 事件   状态变 → State 事件（结构体）      │
│                                                                                │
│  ④ 视图域（Unity 侧，唯一的渲染入口）                                               │
│     MapViewRouter.Drain(sink)  ── 1.5ms 预算，严格保序，不丢事件                     │
│       └─ BotField : IMapViewSink → 纸娃娃 / SpriteAnim / 气泡 / 特效                │
└────────────────────────────────────────────────────────────────────────────────┘
```

一句话对照源端：

| 源端 | 我们 |
|---|---|
| `MapleMap.characters` + `mapobjects` | `MapWorld`（oid 索引 + dense 数组） |
| `Character.visibleMapObjects` | `MapInterest.visibleOids` |
| `spawnAndAddRangedMapObject` | `Interest` diff → `OnSpawn` |
| `movePlayer` + `broadcastMessage` | `MapWorld.Move` → `OnState`（只发给看得见的观察者） |
| `getRangedDistance() = 722500` | 同一个常量，放在 `MapInterest` |
| `Client.createMock()`（包在编码前丢弃） | 无 socket；`IMapViewSink` 就是"客户端" |
| `playerStorage` / `channel.addPlayer` | `BotRegistry` 降级为「按 id 查角色」的目录（门面） |

> 注意：源端移动包是**全图广播**（`POSITIVE_INFINITY`）。我们单进程没有网络，
> 不必复刻这份浪费，直接只推给视野内观察者 —— 这是**优于**源端的地方，不是偏离。

---

## 3. 关键数据结构

```csharp
// 实体：索引对象，不替代 BotCharacter / BotActor，只持有引用
public enum MapEntityKind : byte { Player, Bot }   // Mob/Drop 预留，本期不做

public sealed class MapEntity
{
    public int oid;            // 图内对象号（对齐源端 MapObject.getObjectId），按图自增
    public int actorId;        // 角色 id（真人 record.id / 假人 bot.id）
    public MapEntityKind kind;
    public object owner;       // BotActor 或 BotCharacter
    public int mapId;

    public short x, y, vx, vy;
    public sbyte stance, facing;
    public short foothold;
    public int revision;       // 每次状态变更 ++；视图按 revision 去重
    public byte heat;          // 0=Cold 1=Warm 2=Hot
}
```

```csharp
// 事件：结构体，零分配；单进程不需要"封包"
public readonly struct EntityState
{
    public readonly int oid; public readonly short x, y, vx, vy;
    public readonly sbyte stance, facing; public readonly short foothold;
}

public interface IMapViewSink
{
    void OnSpawn(in EntitySpawn spawn);   // 含外观 key、名字、mapId
    void OnState(in EntityState state);
    void OnDespawn(int oid);
    void OnEffect(int oid, EffectKind kind, int value);   // 攻击/表情/卷轴/气泡
}
```

```csharp
// 地图世界
public sealed class MapWorld
{
    readonly Dictionary<int, MapEntity> byOid = new();
    readonly List<MapEntity> entities = new();       // dense，遍历不分配
    readonly Dictionary<int, List<IMapViewSink>> watchers = new();  // oid → 谁看得见
    int nextOid = 1;
}
```

**关于空间索引**：先别做网格。
单图实体 ≤ 500、观察者 = 1（单机宿主）时，10Hz × 500 = 5000 次距离比较/秒，
折算 0.01ms/帧，可以忽略。等单图 > 1000 实体再加 512px 网格桶（有明确阈值再优化，不提前上结构）。

---

## 4. 关键设计决策

### D1. 热度分级：把「整图开关」换成「实体三档」

| 热度 | 判定 | 模拟周期 | 是否发布 | 典型占比 |
|---|---|---|---|---|
| Hot | 在任一观察者 visible 集合内 | 50ms（对齐源端移动 tick） | 是 | 视野内那 20~30 个 |
| Warm | 同图有真人，但不在视野 | 250ms | 否（状态照写 `MapWorld`） | 同图中等距离的 |
| Cold | 该图没有真人 | 2000ms，且 `BotWander` 停摆 | 否 | 全服绝大多数 |

这样「在线 1000 人」= 每帧约 8 个 Cold tick（1000 / 2s / 60fps），**与人数弱相关**；
「同图 120 人」= 视野内约 25 个 × 20Hz + 其余 95 个 × 4Hz ≈ 每帧 12 次 tick。

### D2. 地形索引按地图共享（最省事、收益最大）

`BotGroundIndex` / `BotWalkRegions` / `BotAirCollision` **只依赖地图，不依赖假人**，
但现在每个 `BotMovementRuntime` 都 new 一份（`BotWalkRegions` 里还有 O(footholds) 的并查集）。
加一层缓存：

```csharp
public static class MapGeometry   // Server/Bot/Environment/MapGeometry.cs
{
    // key = mapId；地图结构在运行期不变，可安全长期缓存
    public static (BotGroundIndex ground, BotWalkRegions regions, BotAirCollision collision) Get(MapRuntime map);
    public static void Evict(int mapId);        // 换图/内存压力时清
}
```

收益：建卡成本从 `O(N × footholds)` 降到 `O(footholds)`，内存从 N 份降到 1 份。
地图 2000 foothold、100 假人时，**估算**首帧省 50~150ms、常驻省 5~15MB。

### D3. 时间轮替代每帧全量扫

`BotTasks` 现在是 `List` + 每帧 `FindAll(谓词)` + `Sort`（每帧一次分配）。
换成 **512 槽 × 8ms = 4096ms 覆盖** 的轮子 + 超长延时用远端有序表：

- `Advance(now)`：只走 `lastSlot → nowSlot`（单帧最多推进 8 槽，避免长卡后一次性喷发），
  处理当前槽的侵入式链表 → O(due)，0 分配。
- **`BotTasks.Schedule / ScheduleFixedRate / Handle.Cancel` 的 API 一个字不改**，
  所有调用点零改动。这一条可以让任何「假人调度」类改动都不外溢。
- `BotWander.AdvanceAll` 不再每帧遍历全部 bot，改为每个 wander bot 注册一个 400ms 周期条目。

收益：每帧成本从 `O(在线人数 · log)` 降到 `O(本帧到期数)`，1000 人在线时省约 0.5~1.5ms/帧 + 去掉每帧 GC。

### D4. 移动快照替代封包

`BotMovementRuntime.Emit` 现在每次状态变化都：
`new Command` → `Reconstruct`（建数组）→ `Array.Copy` → 交给一个**没有订阅者**的事件。

改：`Emit` 只 `MapWorld.PublishState(entity)`，`MapViewRouter` 直接消费 `EntityState`。
将来真要联机，再加一个 `NetworkSink : IMapViewSink` 在出口序列化即可 —— **协议不是数据模型**。

收益：每次快照 2 次分配 → 0；100 假人 × 20Hz ⇒ 每秒省 4000 次小对象分配（**估算** 0.5~1MB/s 的 GC 压力）。

### D5. 真实 dt，不用写死的 50ms

`BotMovementService.Tick` 里 `e.aiAccumulator += 50` 假设每次 tick 恰好 50ms。
预算挤掉 tick 时，假人移动速度会**整体变慢**（负载越高越慢，观感像卡）。
改为：`dt = clamp(now - lastTickMs, 0, 100)`，>100ms 按单步处理并重基准。

### D6. 视图侧要能自愈（事件驱动最大的风险）

轮询的好处是"错了会自己纠正"，事件驱动没有这层兜底。补两个廉价保险：

- **保序队列**：同一实体的 `Spawn` 必须早于 `State`；`Despawn` 之后到的 `State` 用 revision 丢弃。
- **看门狗**：每 5s 校验 `BotField.views ⊆ MapWorld.entities`，不一致就 log + 修正（不发散）。
- **换图必须发 Despawn**：`MapWorld.Clear` / `LeaveMap` 要主动给所有 watcher 发 despawn，
  否则视图泄漏（现在是靠 `present` 集合对比兜住的）。

---

## 5. 改动清单（文件级）

### 新增（`Assets/Scripts/Server/World/`，纯 C#）

| 文件 | 职责 | 约行数 |
|---|---|---|
| `MapEntity.cs` | 实体 + `EntityState` / `EntitySpawn` / `MapEntityKind` | 90 |
| `MapWorld.cs` | 单图实体索引、oid 分配、状态发布、watcher 反查 | 220 |
| `MapWorlds.cs` | mapId → MapWorld，懒创建 / 空图回收 | 80 |
| `MapInterest.cs` | 单观察者 visible 集合 + 850px 增量 diff + 事件产出 | 180 |
| `InterestService.cs` | 观察者注册（真人）、10Hz 刷新、halo 预热 | 120 |
| `IMapViewSink.cs` | 视图接口 + 事件结构 | 70 |
| `MapViewRouter.cs` | 事件队列、保序、按 sink 分发、预算 | 150 |
| `BotTickWheel.cs` | 时间轮；`BotTasks` 改为它的门面 | 200 |
| `MapGeometry.cs` | 地形索引共享缓存 | 70 |

### 修改

| 文件 | 改动 |
|---|---|
| `Server/Bot/BotRegistry.cs` | 保留全部公开 API，内部转发 `MapWorlds`；`OnMap` 改为返回复用缓冲（去掉每次 new List） |
| `Server/Bot/Environment/BotMovementRuntime.cs` | `Emit`/`EmitFlash` 改发 `EntityState`；地形索引走 `MapGeometry` |
| `Server/Bot/Environment/BotMovementService.cs` | `IsObserved(mapId)` → `HeatOf(entity)`；tick 周期按热度取；真实 dt |
| `Server/Bot/Environment/BotObserverTracker.cs` | 退役为 `InterestService` 的兼容壳（halo 语义保留） |
| `Server/Bot/BotTasks.cs` | 内部换 `BotTickWheel`，对外 API 不变 |
| `Server/Bot/BotWander.cs` | `AdvanceAll` 删除；改为按 bot 注册 400ms 条目 |
| `Server/Bot/BotScheduler.cs` | 时间轮统一驱动；governor 保留（它对"卡"的反应是对的） |
| `Server/Bot/BotRuntime.cs` | `SynchronizePlayer` 写 `MapWorld` + `Interest`；`Movement` 事件退役 |
| `Field/BotField.cs` | 实现 `IMapViewSink`；删 `Synchronize` 轮询；加 View 池 + 看门狗 |
| `Field/FieldDirector.cs` | `TickBots` 只做：时钟注入 → `Interest.Update` → `Router.Drain` |

调用面影响：`BotRegistry.OnMap` 有 6 个调用方（聊天、组队、掉落、地图入场、市场、渲染），
保留门面后**全部零改动** —— 这是这次改造能分期落地的关键。

---

## 6. 优化清单（按性价比排序）

| 优先级 | 项 | 现状 | 改后 | 估算收益 |
|---|---|---|---|---|
| P0 | 地形索引共享（D2） | N 份 × O(footholds) | 1 份 | 同图 100 人建卡 −50~150ms，内存 −5~15MB |
| P0 | 移动快照替代封包（D4） | 2 分配 / 快照 | 0 | GC −0.5~1MB/s |
| P0 | 时间轮（D3） | 每帧 FindAll+Sort | O(due) | 1000 在线时 −0.5~1.5ms/帧 |
| P1 | 实体热度分级（D1/D5） | 同图全员 50ms | 视野内 50ms / 其余 250ms | 同图 120 人 tick 数 −70% |
| P1 | 视图事件化 | 100ms 全量轮询 + 每帧遍历 | 事件 | 去掉 List 分配与全量对比 |
| P1 | View 池化 | 换图 Destroy/Create ~100 个 | 复用 | 换图尖峰 −5~10ms |
| P2 | 绘制批次 | 每帧每个假人 1 张合成纹理 | 同 appearance+frame 可合批 | draw call ≈ 可见数（≤ 30 通常够） |
| P2 | 同模板帧图集 | 每帧一张 Texture2D | 打包成 atlas | draw call 可压到个位数（工作量大，按需） |
| P3 | 模拟移出主线程 | 主线程 | Job/线程 + 双缓冲 | 只有前几项做完还超预算才做 |

> 关于绘制：`WzCharacter.Compose` 是**逐像素合成成单张纹理**，每个 animation frame 一张。
> 所以不同帧/不同外观不可合批。同模板、同姿势、同帧的假人能合批（共用纹理 + 默认材质 + 同 sortingOrder）。
> 30 个可见假人的 draw call 在 30 以内，Android 上完全不是瓶颈；真要压再做图集。

---

## 7. 实施阶段（每期独立可验收、可回退）

### 阶段 0：先有度量（0.5 天）
- 加压测开关：`solo_mapling_stress_bots`（默认 0；>0 时在真人所在图生成 N 个假人）。
- `debug_bot_perf=1` 扩展为输出 P50/P95/P99 + 分配量。
- **先跑出基线**，没有基线的优化都是猜。

### 阶段 1：零行为改动的性能三件事（1~2 天）
- D2 地形索引共享、D3 时间轮、D4 快照替代封包、D5 真实 dt。
- 验收：`server-check` 全过；`PreviewShot` 截图与改动前逐像素一致；同图 120 人 P99 下降（记录数字）。

### 阶段 2：实体层（2 天）
- `MapEntity` / `MapWorld` / `MapWorlds` + `BotRegistry` 门面。
- 真人也在 `MapWorld` 里注册（现在已经在 `BotRuntime.players`，只是没有地图索引）。
- 验收：新增断言 —— oid 唯一、进出图事件成对、`OnMap` 与 `MapWorld` 内容一致。

### 阶段 3：AOI（2 天）
- `MapInterest` + `InterestService` + 实体热度分级。
- `BotObserverTracker` 退役（halo 语义并入：真人站在门口 350px 时邻图记 Warm，不再记 Hot）。
- 验收：断言 —— 移动真人跨越 850px 边界时 spawn/despawn 成对；热度档位切换正确；
  冷图假人停在原地（与现状一致）。

### 阶段 4：视图事件化（2 天）
- `IMapViewSink` + `MapViewRouter` + `BotField` 重写 + View 池 + 看门狗。
- 验收：同图 20/60/120 三档截图无差异；换图来回 20 次无泄漏（`views.Count == 可见数`）；
  停掉 `Synchronize` 后仍能正常显示（证明没有隐藏轮询依赖）。

### 阶段 5（可选）：后台线程
只有在阶段 1~4 之后 P99 仍 > 4ms 才做。届时 `MapWorld` 已经是唯一状态源，
双缓冲快照的边界天然清晰 —— 这也是为什么先做世界层再做线程。

---

## 8. 风险与回退

| 风险 | 影响 | 缓解 |
|---|---|---|
| `BotRegistry` 语义变化波及 6 个调用方 | 聊天/组队/掉落异常 | 门面转发，API 不变；阶段 2 一口气迁完，不并存两套 |
| 事件驱动丢 spawn/despawn | 假人凭空消失/残留 | 保序队列 + revision 校验 + 5s 看门狗 |
| 热度降级让"远处假人"动作变慢 | 观感 | Warm 仍按 250ms 推进（不是停摆）；靠近即升 Hot，肉眼可见的只有视野内 |
| 时间轮 bug 导致 tick 停摆 | 全部假人静止 | `BotTasks` 保留原实现做开关（`bot_legacy_tasks=1`）一键回退 |
| `MapGeometry` 缓存与地图重载冲突 | 落点错误 | 只缓存"运行期不变"的结构；`MapService.Unregister` 时 `Evict` |

---

## 9. 明确不做的事

- **不引入 socket / 协议层**：本工程是单机宿主，`GameServer.Local` 与前端同进程，没有真客户端要发。
  快照结构体就是"协议"，将来要联机再加一层序列化 sink。
- **不改 850px**：源端常量（`722500`），也是 `use_max_range` 关闭态的默认值。
- **不把怪物/掉落并进 `MapWorld`**（本期）：它们现在由前端 `NativeMobController` / `GroundItem` 管理，
  合并是另一个大工程；`MapEntityKind` 留了扩展位。
- **不为了"架构漂亮"重写 AI**：`BotNavigationRuntime` / `BotGroundMotion` 是照源端逐行搬的，行为正确性优先。

---

## 10. 实测修订（2026-10-29，阶段1 已落地）

基线数据见 `docs/bot-perf-baseline.md`，据此调整原计划：

| 原计划 | 实测 | 修订 |
|---|---|---|
| D2 地形索引共享（P0） | 建卡 204.8ms → 2.6ms（60 人 / 1203 foothold） | **已做**，收益最大 |
| D4 快照替代封包（P0） | 每次快照分配 53.75B → 0.02B | **已做**（改为"无人订阅就不构包"，比结构体改造更小） |
| D3 时间轮（P0） | 1000 个周期任务仅 0.002ms/帧 | **暂缓**：风险收益比不划算，等每帧到期任务上千再上线 |
| D1/D5 实体热度 + 真实 dt（P1） | 120 人同图全速跑，服务端 0.30ms/帧 | **降级为可选**：不是服务端瓶颈；若改，必须与视图 AOI 同步上线，否则远处假人会看得见地抖 |
| 阶段4 视图事件化 | 离线测不到 | 保持计划，但先做真机 `debug_bot_perf` 采样再决定 |

结论修正：**服务端每帧成本不是瓶颈，建卡与视图侧才是。** 先修建卡（已完成），
下一步应该量视图侧，而不是继续重构调度层。
