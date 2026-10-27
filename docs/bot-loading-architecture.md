# 假人加载架构调研：为什么联机不卡，我们卡在哪

> 结论先行：现在卡不是「加载没分帧」，而是**把服务端该干的活搬到了渲染主线程**，并且
> **每个假人都在做一次不可拆的 CPU 像素合成**。分帧只能摊平数量，摊不平单件成本，
> 所以体感「优化了但没效果」。要真正解决，得按联机客户端的模型重排：**实体按视野产生、
> 外观共享、渲染不做合成、模拟不进渲染线程**。

---

## 一、联机客户端为什么不卡

同样一屏几十个角色，联机客户端能跑满帧，靠的是四条结构性设计，不是某段代码写得快。

### 1. 客户端不模拟任何东西

AI、寻路、掉落、交易、状态机全在服务端。客户端只做一件事：**收位置快照 → 插值播放**。
一帧的工作量是 `O(同屏人数)` 次插值，几乎为零。

我们的现状（`FieldDirector.cs:381-392`）：

```
TickBots()
  → bots.Drive(now)              // 假人 AI 状态机 + 任务泵，跑在渲染线程
      → BotScheduler.Drive()     // 时间轮，每 250ms 一轮
      → BotTasks.Drive(now)      // 每帧 FindAll + Sort + 全部执行，无预算
      → BotWander.AdvanceAll(now)// 同上，无预算
```

`BotTasks.Drive` 的实现（`BotTasks.cs:36-55`）每帧都 `tasks.FindAll(...)` 新建一个 List
（GC 压力），排序，然后**把到点的任务全部执行完**，没有时间上限。`BotScheduler.Drive` 里
虽然有 `MinTicksPerRound` + 预算，但 `BotTasks` / `BotWander` 这两条每帧路径是裸跑的。

### 2. 客户端只处理 AOI 内的实体

服务端只下发玩家兴趣区域内的对象，进出视野才创建/销毁。同屏上限是被**设计**出来的，不是运气。

我们是一次性把整个环境建出来：

| 来源 | 数量 | 配置 |
|---|---|---|
| 自由市场假人 | 10 | `BotConfig.MarketBotMax` = 10 |
| 环境假人上限 | 30 | `BotConfig.EnvironmentBotMax` = 30 |
| 城镇游荡 | 7 城 × 2 = 14 | `TownPresence.yaml` |

`BotRuntime` 构造的最后一行就直接 `BotEnvironment.RequestMarketStartup()`（`BotRuntime.cs:66`），
三波启动把上面这些全建出来，**不管玩家现在在哪张图**。玩家在射手村，自由市场的 10 个假人
照样在生成、照样在 tick、照样占内存。

### 3. 外观是共享资源，不是每次随机生成

联机客户端的远程玩家外观来自服务端下发的一条 look 数据，且**同类部件贴图全局共享**。
同一个发型/同一件衣服，100 个玩家只占一份显存。

我们的假人每次运行都重新随机：`BotGeneration.CreateBot` → `SetBotVariables` →
`decorator.ApplyAppearance(bot, Db)`（`BotGeneration.cs:146-150`），随机从物品池抽装备。
结果就是 `BotAppearance.CharacterIds` 给出的 id 组合几乎**每个假人都不同**。

而纸娃娃缓存是按 `key = "bot_" + 拼起来的 id` 建索引的（`BotField.cs:137-139`）：

```csharp
var ids = BotAppearance.CharacterIds(bot);
string key = "bot_" + string.Join("_", ids);
WzCharacter.Appearance(key, ids);
```

外观唯一 ⇒ key 唯一 ⇒ **缓存命中率 0**，同屏 10 个假人之间一帧都共享不了。

### 4. 渲染是分层 sprite，不是 CPU 合成单张图

原版客户端对角色是**每个部位画一层**（身体/头发/脸/上衣/裤子/鞋/武器…），按 zmap 排序，
alpha 混合交给 GPU。换动作只是换每层的贴图指针，**没有任何 CPU 像素运算**。

我们的实现走的是另一条路 —— `WzCharacter.Compose`（`WzCharacter.cs:573-602`）：

```csharp
var pixels = new byte[checked(width * height * 4)];      // 每帧新建缓冲区
foreach (var part in parts) {                            // 20~40 个部件
    for (int y ...) for (int x ...) { 逐像素 alpha 混合 }  // 约 5~12 万次内层循环
}
var texture = WzAssets.Upload(pixels, width, height, uri); // new Texture2D + 翻转 + Apply（GPU 上传）
```

一个 `(假人, 动作, 帧)` 组合要跑一遍这个流程。而且结果**永久缓存、从不驱逐**
（`WzCharacter.cache`，`WzCharacter.cs:11`）。

---

## 二、根因判定

按影响排序，四条：

### ① 单件成本太高，分帧摊不平（体感「优化没效果」的直接原因）

分帧只能解决「一帧里做 N 件事」，解决不了「1 件事就要 8ms」。

`Compose` 是不可再拆的一件小事：一次约 3~10ms（几万像素混合 + 一张 RGBA32 纹理上传）。
`BotField.CreateQueued` 现在的预算是每帧最多 1 个假人（`BotField.cs:12`, `111-126`），
但一个假人首次出现要播 `stand`，`SpriteAnim` 逐帧取图 → **每个新帧一次 Compose**。
10 个同屏假人 × 约 8~10 个动作帧 ≈ 80~100 次 Compose，摊到 80~100 帧上，
就是进图后**头一两秒每帧都被啃掉 3~10ms**。玩家感知到的就是「进来卡一段」。

> 这一段是估算，量级依据：角色帧约 90×110 px、20~40 个部件（见
> `WzCharacter.Placements`），内层循环约 5~12 万次，加一次 `LoadRawTextureData + Apply`。
> 要精确值就得埋点，见第五节。

### ② 外观随机 → 缓存必然 miss

见上 §一.3。这不是「优化不够」，是**设计上让缓存无法生效**。

### ③ 纹理永久驻留

`WzCharacter.cache` 只增不减。粗算：50 个假人 × 约 50 个动作帧 × 60KB ≈ **150MB RGBA32 显存**，
还不算 `WzAssets.textures` 里的部件图。Android 上这会直接表现为 GC 抖动、切后台被杀。

### ④ 模拟在主线程，且部分路径完全无预算

`BotTasks.Drive` / `BotWander.AdvanceAll` 每帧跑全量（§一.1）。假人数量越多，
这条路径越贵，而且它**不受 `BotWorkPump.BudgetMs` 约束**。

---

## 三、方案

按「投入产出比 × 与联机模型的一致性」排序。

### P1　假人账号化 + 外观模板池（最像联机，收益最快）

**做什么**
- 假人首次生成时把 look（skin/hair/face/equips）**落库**，之后固定不变；不再每次重新随机。
- 外观不再全空间随机，而是从**有限模板池**（建议 24~32 套）中分配，同屏假人允许撞衫。
- 模板的纸娃娃在**登录界面/进图前分帧预合成**（玩家看不到的那几秒）。

**为什么有效**
- 模板数固定 ⇒ `Compose` 总次数从 `人数 × 帧数` 降到 `模板数 × 帧数`，且**跨地图、跨会话复用**。
- 命中率从 0 提到接近 100%，进图时理论上 **0 次 Compose**。
- 顺带解决「假人每次重启长相全变」这个不像联机的问题（真实玩家外观是存下来的）。
- 落库走现成的 `DbStore` + `CharService`，不新增基础设施。

**风险**：模板池太小会看出「撞脸」。建议 24 套起，按职业/等级段加权。

### P2　AOI：只为当前地图生成，进出才创建

**做什么**
- `BotEnvironment` 三波启动改成**按玩家当前地图惰性触发**：玩家进自由市场才建市场假人，
  进某城镇才建该城游荡假人；离开后假人进入休眠（不 tick、不渲染），可保留状态。
- 同屏上限（建议 12）硬约束，超出不生成。

**为什么有效**
- 直接把「进游戏要建 40~50 个」降到「建 ≤12 个」，是数量级的下降。
- 完全对应联机的 AOI 语义，且和现在 `BotField.Synchronize` 只渲染当前图的逻辑天然吻合
  （渲染层已经在做视野裁剪，生成层还没做）。

**风险**：玩家在城镇间快速往返时会反复生成（配合 P1 的模板缓存 + 对象池可消除）。

### P3　纹理预算与驱逐

**做什么**
- `WzCharacter.cache` 改成引用计数/LRU：地图内已无该外观的假人时释放其帧纹理。
- 加一个上限（例如 60MB），超了按最近最少使用驱逐。

**为什么有效**：解决 ③，防 OOM 和 GC 抖动。属于必须做的保底项。

### P4　纸娃娃改分层渲染（治本，但工作量大）

**做什么**
- 假人不再 `Compose`，改成每个部件一个 `SpriteRenderer` 子对象，按 zmap 排 `sortingOrder`，
  换动作只切每层的 sprite —— 这就是原版客户端的做法。
- 配套：把常用部件打进 `SpriteAtlas`，让同图集 sprite 走动态合批。

**收益**：`Compose` 彻底消失，单件成本归零，外观随机也无所谓了。
**代价**：每个假人 8~12 个 renderer，50 个就是 500 draw call；WZ 纹理各不相同，
不合图集的话移动端 draw call 会爆炸。**必须配 P2 的 AOI 上限 + 图集**才划算。
**建议**：先做 P1+P2，如果同屏假人还要往 20+ 提，再上 P4。

### P5　模拟移出渲染线程（最终形态）

**做什么**
- `BotScheduler` + `BotTasks` + `BotWander` 跑在独立后台线程（`Server/` 已是纯 C#，
  时钟靠 `BotScheduler.Clock` 注入，本来就是为这个设计的）。
- 主线程只 `SynchronizePlayer` 写入 + 读假人位置快照渲染（双缓冲/快照结构）。

**收益**：彻底消除 ④，模拟再重也不影响帧率。
**风险**：最高。`BotRegistry` / `BotStore` / `MapItemService` / `DbStore` 都是共享可变状态，
要逐个确线程安全；`DbStore` 是 JSON 快照，多线程写必须串行化（现有 `AdminBackgroundPump`
已经踩过这条线，可以复用那套「单线程 DB」约定）。
**建议**：放在 P1~P3 之后，只在仍有周期性卡顿时做。

---

## 四、不采用的方案

| 方案 | 为什么不做 |
|---|---|
| **Addressables / 资源包异步** | 解决的是「资源从磁盘/包进来」的加载，而我们的瓶颈是 **CPU 像素合成 + 纹理上传**，资源早就在内存里了。 |
| **Job System / Burst 并行 Compose** | 像素混合可以并行，但 `new Texture2D` + `Apply` **必须在主线程**，收益上限约 50%，复杂度却翻倍。 |
| **协程 yield 分帧** | 就是现在这套 `BotWorkPump`，已经证明摊不平单件成本。 |
| **单纯降 tick 频率** | 假人动作会明显发迟，源端节奏就错了；而且解决不了生成期的 Compose。 |
| **只做对象池** | 池化省的是 Instantiate/GC，省不了 Compose。 |

---

## 五、怎么验证（建议先做这一步再动手）

在五处加 `Stopwatch`，输出到 logcat，开关沿用现成的 `debug_frame_log`：

1. `BotRuntime` 构造总耗时
2. `BotEnvironment` 每个 pump step 的耗时（找出最贵的单步）
3. `BotField.Create`（含 `WzCharacter.Appearance`）单次耗时
4. `SpriteAnim` 里每次 `Art.Frame` / `Compose` 的耗时与次数
5. `BotScheduler.Drive` + `BotTasks.Drive` 每帧耗时

跑一次真机、进自由市场，`adb logcat -s Unity | grep Bot/Perf`。

目标数字：**找出耗时最大的单次调用**。如果 `Compose` 单次 > 3ms 且进图 1 秒内调用上百次，
直接按 P1 走；如果 `BotEnvironment` 某个 step 单次 > 30ms，先拆那一步（多半是
`PlanPlatformSeats` 的 foothold 采样）。

---

## 六、建议执行顺序

1. **加埋点，拿一次真机数据**（半天，不改行为）
2. **P1 外观模板池 + 落库 + 预合成**（收益最大、风险最低）
3. **P2 AOI 惰性生成 + 同屏上限**
4. **P3 纹理预算/驱逐**
5. 视数据决定是否上 P4（分层渲染）/ P5（模拟后台线程）

预期：走完 1~3，进图应该做到「0 次 Compose、≤12 个假人」，卡顿基本消失；
剩下的是持续性的模拟开销，由 P5 兜底。

---

## 七、实施进度

| 项 | 状态 | 落地位置 |
|---|---|---|
| 埋点（第五节） | ✅ 已完成 | `Server/Bot/BotPerf.cs`；开关 `debug_bot_perf=1`，输出 `[Bot/Perf]` |
| **P1 外观模板池 + 预合成** | ✅ 已完成 | `Server/Bot/BotAppearanceTemplates.cs`（固定种子 32 套，键 `solo_mapling_appearance_templates`）、`Client/BotAppearancePrebake.cs`、`WzCharacter.PrecomposeFrame*` |
| **P1.5 假人账号化（落库）** | ✅ 已完成 | `Server/Bot/BotDirectory.cs` + `bot_profile` 表（默认 300 条，键 `solo_mapling_directory_size`） |
| **P1.6 登录期预热（真正的联机加载点）** | ✅ 已完成 | `Client/BotPreload.cs` 改为登录期驱动 `BotTasks/BotScheduler`；`BotField` 换图一次性建视图 |
| P2 AOI 惰性生成 | ⬜ 待做 | `BotEnvironment` 三波启动改为按当前地图触发 |
| P3 纹理流失驱逐 | ⬜ 待做 | `WzCharacter.cache` 加引用计数/LRU |
| P4 分层渲染 | ⬜ 视需要 | 同屏假人还要往上提才做 |
| P5 模拟移后台线程 | ⬜ 视数据 | `BotTasks.Drive` / `BotWander` 目前每帧裸跑，埋点会给出数字 |

P1 的实测结果（server-check）：生成 40 个假人，外观从 40 种收敛到 **22 种**；
同模板的假人共享同一份纸娃娃缓存，第二次出现时 Compose 次数为 0。

---

## 八、按联机架构重做：关键不在「怎么加载」，在「在哪加载」

前六节把卡顿归因到「单件成本太高」，方向没错，但漏掉了**位置**这个维度。
用户的原话是「联机是进图就全加载了，而不是要等」——这句话指向一个更深的问题：

> 联机客户端**进图前**就加载完了；我们却是**进图后**才开始加载，只是把它摊薄了。

### 根因：假人系统整个挂在「游戏内」这个开关后面

```
FieldDirector.Update
  └─ TickBots()                        ← 首句就要求 GameSession.InGame
       ├─ BotRuntime.SynchronizePlayer
       └─ BotRuntime.Drive(now)        ← 唯一的时钟注入点
            ├─ BotScheduler.Clock = now
            └─ BotScheduler.Drive() → BotTasks.Drive()
                 └─ BotEnvironment 的分帧泵（三波假人全部在这里排队）
```

`BotTasks.Drive` 只有这一个调用点。也就是说：

- 登录 / 选角界面期间，假人系统**一帧都没跑过**；
- 进图第一帧才开始建「市场 10 + 环境 30 + 城镇 14」个假人；
- 分帧（4ms/帧）只是把这段忙从「一帧卡死」变成「几十帧都忙」，
  位置仍然在玩家**已经能操作之后** —— 体感就是「进图后要等一会儿才顺」。

这也解释了为什么「虚拟按键按不动」：不是渲染掉帧，是主线程在那几十帧里持续被占用。

### 联机模型的三件事，逐条对齐

用户的描述基本正确，但有两处要精确化：

| 用户的说法 | 精确化 |
|---|---|
| 「进图就全加载了」 | 加载的是**该地图上的所有玩家**，不是全世界。几百个账号分布在几十张图上，每张图只加载自己的那几十个 |
| 「先在数据库预设几百个假人」 | 对应联机的**账号表**：每个账号在库里唯一，别人看到他永远是同一张脸、同一个名字 |
| 「再在特定场景加载一定数量出来」 | 对应联机的 **spawn 下发**：进哪张图就下发哪张图的实体 |

真正的差异不在「加载快慢」，而在**加载发生在哪个阶段**：

| | 联机 | 改造前 | 改造后 |
|---|---|---|---|
| 数据来源 | 数据库（账号固定） | 运行时随机 | **`bot_profile` 表，300 条固定档案** |
| 外观 | 账号自带，稳定 | 每次随机，缓存必 miss | **档案指向固定模板（32 套）** |
| 加载时机 | **进图前（loading 期）** | 进图后，几十帧摊开 | **登录/选角界面驱动完成** |
| 进图动作 | 把已有数据摆上去 | 边摆边造 | **只建 GameObject，0 次 Compose** |

### 落地：三个改动

**① `bot_profile` 表 —— 假人账号化**（`Server/Bot/BotDirectory.cs`）

```sql
CREATE TABLE bot_profile (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,       -- 固定名字
    template INTEGER,         -- 指向外观模板（同模板共享纸娃娃纹理）
    map_id INTEGER,           -- >0 = 只出现在该图
    channel INTEGER, bound INTEGER, enabled INTEGER
);
```

- 首次启动按固定种子（20261027）补齐 300 条，**之后只读不改**；
- 上线时从档案领一个（`Take`），下线归还（`Release`）——与联机的「登录/下线」同构；
- 改 `solo_mapling_directory_size` 只会往后追加，老档案的身份不会被改写。

**② 登录期驱动**（`Client/BotPreload.cs`）

原来它只在登录界面构造 `BotRuntime` 就自我销毁。现在构造完之后**继续驱动**
`BotTasks + BotScheduler + BotWorkPump`，直到 `BotEnvironment.IsMarketStartupComplete`。
这就是把「进图后几十帧的忙」整段搬到「玩家在点按钮的时候」。

**③ 顺序保证：纹理先于实体**

```
BotAppearancePrebake 铺 stand 帧  →  StandReady = true
        ↓
BotPreload 开始建假人（纯对象构造，几十个约 0.1~0.3s）
        ↓
进图：BotField 一次性建该图全部视图（MapChanged 时不再逐个出队）
```

`BotField.Synchronize` 在换图那一帧用 `MaxCreateOnMapEnter=40` 一次性建完，
不再「一个个冒出来」。前提是 `BotAppearancePrebake.Finished`——
预合成没赶完就退回增量，宁可晚出现一两个，也不在进图那帧连做十几次 CPU 合成。

### 效果与边界

- **正常路径**（登录界面停留 ≥2~3 秒）：进图 0 次 Compose、假人一次性站好，掉帧消失。
- **神速进图**（登录后 1 秒内进图）：预合成没铺完，退回增量 + 按需合成。这是刻意保留的降级，
  也是唯一残留的卡顿路径 —— 想做干净就得加真正的 loading 门控（联机就是这么做的）。
- **内存**：300 条档案是纯文本记录（几十 KB），不占显存；在场 54 个假人共用 32 套纹理。
- 留了开关便于排查：`solo_mapling_directory_size=0` 关闭档案（回退运行时随机），
  `solo_mapling_appearance_templates=0` 关闭模板池。


