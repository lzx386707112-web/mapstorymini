# 源端联机角色架构 & 假人能否接入

> 结论先行：源端「很多人同时在线也不卡」不是网络层做得好，而是**成本模型**决定的 ——
> 每个包/每帧的代价只与**同图人数 + 视野内对象数**成正比，与全服人数无关。
> 而假人在源端**本来就是真角色**（进 `MapleMap.characters`，走同一套广播），
> 唯一的区别是它的 `Client` 是 mock：`ioChannel == null`，发往假人的包在**编码之前**就被丢掉。
> 我们的移植版没有网络层，但可以对标它的「实体 + 视野 + 事件」这三件事，这就是假人接入的正确位置。

依据：`~/Documents/mxd/Beidou-Server/gms-server/src/main/java/`（GMS083 fork），行号即该仓库行号。

---

## 一、传输层：异步写，调用方永不等

| 环节 | 实现 | 意义 |
|---|---|---|
| 连接 | Netty `ChannelServerInitializer` → `ServerChannelInitializer.initPipeline` | 每条连接一条 pipeline |
| 加密 | `PacketCodec` + `ClientCyphers`（send/recv IV 各一套） | 编解码在 event loop 上做 |
| Handler 线程 | `initPipeline(channel, client)` **不传 executor** → handler 跑在该连接的 event loop | 所以到处是 `chrRLock/objectRLock`，不是线程池 |
| 发包 | `Client.sendPacket` → `ioChannel.writeAndFlush(packet)`，`announcerLock` 保护 | **异步**：调用方只入队，不参与序列化/不阻塞 |
| 空客户端 | `Client.createMock()` → `ioChannel` 保持 null；`sendPacket` 第一行 `if (ioChannel == null) return;` | 假人收包零成本（连编码都不做） |

关键点：一次广播的实际成本 = `O(人数)` 次「入队一个对象引用」，编码与加密摊到各自的 event loop 线程上，互不阻塞。

## 二、实体层：一张地图三份索引

`MapleMap`：

- `characters`：本图玩家（`chrRLock` 保护）
- `mapobjects`：`oid → MapObject`（NPC/怪物/掉落/玩家/召唤物全在里面，`objectRLock`）
- 每个 `Character.visibleMapObjects`：**这个玩家当前看得见谁** —— AOI 的实际载体

`addPlayer`（2791-3025）把角色同时挂进 `characters`（2796）和 `mapobjects`（2962），
然后 `broadcastSpawnPlayerMapObjectMessage` + `sendObjectPlacement`（把周围已有对象发给新人）。
`World.getPlayerStorage()` + `Channel.addPlayer` 各一份索引，供按 id/名字查找（密语、组队、脚本）。

## 三、视野（AOI）：850px，只管「出生/消失」，不管移动

```java
private static double getRangedDistance() {
    return GameConfig.getServerBoolean("use_max_range") ? POSITIVE_INFINITY : 722500;  // 850²
}
```

- **出生**：`spawnAndAddRangedMapObject`（456-481）只给 `distanceSq <= 722500` 的玩家发 spawn 包并 `addVisibleMapObject`。范围外的人**根本不知道这个对象存在**。
- **消失**：`updateMapObjectVisibility`（3788-3799）越界才 `removeVisibleMapObject` + `sendDestroyData`。
- **移动**：`movePlayer`（3886-3932）只做两件事 —— 重算「自己已经看得见的那些对象」是否越界，再对范围内**新**对象补 spawn。**不遍历全图玩家**。
- **移动广播**：`MovePlayerHandler` 调 `broadcastMessage(source, packet, repeatToSource=false)` —— 这个重载走的是 `POSITIVE_INFINITY`（3255-3257），即**移动包全图广播**（v83 原味）。所以移动成本 = `O(同图人数)`，850px 只决定谁出现在谁屏幕上。

> 这一点很容易记错：「有 AOI」不等于「移动包只发给视野内」。源端只在 spawn/destroy 上做 850px 裁剪，
> 移动包是全图发的；客户端对未知 oid 直接忽略。

## 四、客户端：零模拟 + 共享纹理

客户端只做「收位置快照 → 插值播放」，不做 AI/寻路/掉落/交易；纸娃娃是**分层 sprite 按 zmap 排序**画，
换动作只换每层贴图指针，没有任何 CPU 像素合成。同屏几十人 = 几十次插值 + 几十组 draw call。

对照我们：`WzCharacter.Compose` 是**逐像素合成成单张纹理**，缓存 key 由外观 id 串拼成
（所以 P1 才把外观收敛到模板池、登录期预合成；详见 `bot-loading-architecture.md`）。

## 五、假人在源端就是「真角色」

| 环节 | 源端做法 | 位置 |
|---|---|---|
| 生成 | `Character.getDefault(mockClient)` 建角色，`setID(20000+)` | `BotGeneration.setBotStats` |
| 挂服务器 | `channel.addPlayer` + `world.getPlayerStorage().addPlayer` | `addBotToServer` (253-258) |
| 进地图 | `map.addPlayer(fakechar)`（与真人同一个入口） | `placeBotOnMap` (170-178) |
| 移动 | `map.moveBot(...)` + `map.broadcastMessage(chr, PacketCreator.movePlayer(...), false)` | `MovementCommands` (684-690) |
| 收包 | mock client，`ioChannel=null` → `sendPacket` 直接 return | `Client.createMock` |
| 真人区分 | `BotHelpers.isBot(chr)`：地图脚本 `onFirstUserEnter/onUserEnter` 只跑给真人 | `MapleMap.addPlayer` (2816-2834) |

也就是说：**假人走的是和真人一模一样的实体/广播路径，只是出站包被丢弃**。
「不卡」是因为假人不产生任何序列化/网络成本，而 AI 跑在独立线程池（`ExecutorServiceManager`），
不占 event loop。

## 六、成本模型（为什么人多也不卡）

| 操作 | 源端代价 |
|---|---|
| 一次移动 | `O(同图人数)` 次入队（全图广播）+ `O(可见对象数 + 范围内对象数)` 次距离比较 |
| 一次出生 | `O(同图人数)` 次距离比较，只给范围内的人发包 |
| 一次 AI tick | 后台线程，与渲染/网络无关 |
| 假人收包 | `O(1)`（第一行 return） |
| 客户端一帧 | `O(同屏角色数)` 次插值，零合成 |

**没有一项与「全服在线人数」相关**。这是架构结论，不是优化技巧。

---

## 七、我们的移植版对照

| 维度 | 源端 | 我们现在 | 差距性质 |
|---|---|---|---|
| 实体模型 | `MapleMap.characters` + `mapobjects` + 每角色 `visibleMapObjects` | `BotRegistry`（id/mapId 双索引）+ `MapRuntime`（npc/portal/foothold）；渲染在 `BotField.views` | **两套并行世界**，没有统一的「地图对象」概念 |
| 视野 | 850px 决定 spawn/destroy，移动全图广播 | `BotObserverTracker`（full/halo/sticky 4s）回答「这张图有没有人看」，**不做对象级 AOI** | 粗粒度：整张图 开/关，而不是「谁看得见谁」 |
| 渲染驱动 | 服务端推 spawn/move/destroy 包 | `BotField.Synchronize` **每 100ms 轮询** `BotRegistry.OnMap(mapId)` 全量对比 | 轮询代替事件；`BotRuntime.Movement` 广播无订阅者 |
| 模拟线程 | 独立线程池 | 主线程 + `BotWorkPump` 4ms 预算（`BotTasks`/`BotWander` 仍无预算） | 已知，P5 未做 |
| 假人身份 | 真角色 + mock client | `BotCharacter` 自建实体，不进 `MapRuntime` | 等价物存在，只是没接进实体层 |
| 客户端渲染 | 分层 sprite | 合成后缓存 + 预合成（P1 已做） | 已用「模板池」抵掉大部分成本 |

结论：**能接入**。而且不需要引入网络 —— 需要接的是「实体 + 视野 + 事件」这三件事，不是 socket。

## 八、接入方案（按性价比排序）

### A. 把 `BotCharacter` 提升为地图对象（推荐，等于 P2 的正解）

1. `MapRuntime` 增加 `MapObject` 概念（玩家/怪物/掉落/假人共用），分配 `oid`。
2. 每个「观察者」（真人玩家）持一份 `visibleBots` 集合；`SynchronizePlayer` 换图/移动时按 850px
   做一次增量：进视野 → 发 spawn 事件；出视野 → 发 destroy 事件。
3. `BotField` 从「每 100ms 全量轮询」改成「消费 spawn/move/destroy 事件」，移动走已有的
   `BotMovementService.Broadcast`（现在没人订阅，接上即可）。
4. 收益：轮询消失；AOI 从「整图开关」细化到「对象级」；同屏上限天然受 850px 约束；
   将来接真联机时，这条路径就是现成的远程玩家路径。

### B. 保留轮询，只加对象级过滤（最小改动）

`BotField.Synchronize` 里加距离判定（850px），`BotRegistry.OnMap` 返回后先筛。
改动 10 行，但仍是轮询，且模拟侧的成本不变。

### C. 把模拟搬到后台线程（P5）

`BotScheduler/BotTasks/BotWander` 已经是纯 C# + 注入时钟，本来为此设计。
风险最高：`BotRegistry`/`BotStore`/`MapItemService`/`DbStore` 都是共享可变状态，要逐个确线程安全。

**建议顺序**：A 里的 2、3 步（视野集合 + 事件驱动渲染）先做，它同时解决「轮询开销」和「未来联机」；
B 只作为 A 完成前的止血。C 等 A 之后仍有周期性卡顿再做。

## 九、边界与不做的事

- 不引入 socket/协议层：本项目是单机宿主，`GameServer.Local` 与前端同进程，没有真客户端要发。
- 不改 850px 这个数值：它是源端常量（`722500`），也是 `use_max_range` 的默认关闭态。
- 不动「移动包全图广播」这条：我们本来就没有网络，直接对「视野内」推送即可，无需复刻全图广播的浪费。
