# 后台管理系统（gms-ui）接入说明

把源端 `BeiDou-Server/gms-ui` 原样搬进本项目：游戏进程内起一个只监听 `127.0.0.1` 的
HTTP 服务，同时供出前端静态资源和 REST 接口；游戏内从侧边栏点「后台系统」用系统默认
浏览器打开。前端源码一行没改，改动只在「怎么把它的接口接到当前单机服务端的数据上」。

## 1. 组成

| 位置 | 作用 |
| --- | --- |
| `AdminUi/` | gms-ui 源码（从源端整目录复制，未改动业务代码） |
| `AdminUi/config/vite.config.embed.ts` | 嵌入版构建配置：去掉 imagemin/gzip/visualizer，产物到 `dist-embed/` |
| `tools/build_admin_ui.sh` | 装依赖 + 构建 + 打包一条龙 |
| `tools/pack_admin_ui.py` | 把 `dist-embed/` 打成 `Assets/Resources/AdminUi/adminui.bytes` |
| `Assets/Scripts/Admin/AdminHttp.cs` | 纯 C# 的 HTTP/1.1 服务（TcpListener，Android/IL2CPP 可用） |
| `Assets/Scripts/Admin/AdminStaticBundle.cs` | 资源包解析 + 静态文件/SPA 回退 + MIME |
| `Assets/Scripts/Admin/AdminApi.cs` | 路由、`{code,message,responseId,data}` 信封、token 会话、未接入兜底 |
| `Assets/Scripts/Admin/AdminBackend.cs` | 表 ↔ DTO 映射与增删改查，全部经主线程调度 |
| `Assets/Scripts/Admin/AdminRoutes.cs` | 接口表（URL 与源端控制器一一对应） |
| `Assets/Scripts/Admin/AdminLive.cs` | 后台操作 → 正在运行的游戏（传送/改名/背包同步/封停踢下线） |
| `Assets/Scripts/Server/Login/LoginService.cs` | 登录时校验 `banned` / `tempban`（后台封停真正生效的地方） |
| `Assets/Scripts/Admin/AdminHost.cs` | Unity 宿主：启动服务、每帧排空主线程队列、注入广播/角色钩子 |
| `Assets/Scripts/GameSideMenu.cs` | 侧边栏新增「后台系统」入口 |

## 2. 使用

1. 进游戏（`GameSession.Boot` 起服务端、有 DB）后，点屏幕边缘的橙色悬浮球 → 拉出 → 点开面板
   → 「后台系统」。
2. 首次启动会自动创建一个后台管理员：**admin / admin**（只在本机、只在库里没有任何
   `webadmin` 账号时创建；密码可用账号页改）。控制台会打印 `[Admin] 已创建后台管理员账号`。
3. 首次启动会自动创建一个后台管理员：**admin / admin**（只在本机、只在 `game_config`
   里还没有 `webadmin_seed` 标记时做一次）。如果库里早就有同名游戏账号而密码不是 `admin`，
   这一刻会被重置成 `admin`（控制台会打印 `[Admin] 内置账号 admin 已存在，密码重置为 admin`）；
   之后在账号页改的密码不会再被覆盖。注意后台账号和游戏账号是同一行数据。
4. 浏览器地址：`http://127.0.0.1:8686/`（8686 被占用时自动顺延到 8687…8695）。
   侧边栏入口现在默认用**应用内 WebView** 打开（`AdminWebView`，Android Dialog，
   系统返回键关闭；游戏不切后台，主线程不会停摆）。桌面/编辑器仍走系统浏览器；
   外部浏览器访问 `http://<手机IP>:8686/` 依然可用。
   - Dialog 用 `Theme_Black_NoTitleBar` + show() 之后 `setSystemUiVisibility(0x1706)`
     隐藏状态栏/导航栏（`_Fullscreen` 主题会让系统忽略 `SOFT_INPUT_ADJUST_RESIZE`，
     横屏弹键盘会盖住表单，所以不用它）。
   - WebView 必须 `setLoadWithOverviewMode(false)`：OVERVIEW 会把整页（含高度）
     缩到一屏，登录页这种高页面会被缩到 0.65，字和提示条看着「被挤压」。
   - 不要用 `AndroidJavaProxy` 给 WebView 设 `WebViewClient`（抽象类会抛
     `is not an interface`）。改用真实 Java 插件 `Assets/Plugins/Android/AdminWebViewClient.java`
     （Gradle 编进 APK）：它把**所有导航锁死在应用内**——没有它时页面的
     `window.location` 整页导航会被 WebView 派发给系统 → 跳外部浏览器 → 游戏切后台
     被厂商冻结（nubia CpuFreezer）→ 本地 HTTP 服务停摆，外部浏览器里全部超时。
   - 前端 axios 拦截器 401 只做 SPA 内跳登录页（`router.push`），**禁止
     `window.location` 整页跳转**（同样会逃逸到外部浏览器）。会话只存在游戏进程内存，
     每次重启游戏后第一次请求必 401，属预期行为：拦截器会清旧 token 并跳登录页
     （已经在登录页时不弹「登录已过期」——此时没有东西过期，弹出来只会和上一条提示叠在一起）。
   - `GET /auth/v1/refreshToken` 是**续期**，不是换发 token：源端 `AuthService.refreshToken`
     签的是 JWT，旧 token 在过期前一直有效（服务端根本没有吊销能力）。这里等价实现为
     「顺延同一会话、token 原样返回」。**绝不能 `DropSession` 再 `CreateSession`**：前端
     `hooks/loading.ts` 在**每次** `setLoading(false)` 都会打续期，登录按钮的 `finally`
     正好和路由守卫的 `/account/v1/info` 并发；旧 token 一旦被自己吊销，info 立刻 401，
     表现为「刚弹『欢迎使用』就弹『登录已过期，请重新登录』、界面停在登录页不动」
     （2026-10-28 报障，CDP 抓请求序列定位；`tools/admin-check` 已加回归断言）。
5. **明文 HTTP 放行**：Android 9+ 默认禁 cleartext，系统 WebView 会直接报
   `net::ERR_CLEARTEXT_NOT_PERMITTED`（表现为一片黑底 + 错误页）。构建钩子
   `Assets/Editor/AndroidNativeDataBuild.cs`（`IPostGenerateGradleAndroidProject`）
   会往生成的 manifest 注入 `android:networkSecurityConfig`，并写一份
   `res/xml/network_security_config.xml`：**只放行 `127.0.0.1`/`localhost`**，
   base-config 仍是禁止明文。所以这项改动必须**重新出包**才生效
   （`MapleDash/构建 APK（IL2CPP 发布）`）；旧包点「后台系统」必定黑屏。

## 3. 为什么是「游戏进程内起服务」

- 源端后台是 Spring Boot + MySQL 的独立进程；本项目是单机 APK，没有独立服务端进程，
  数据库是 `GameServer.Db`（`DbStore`，表结构照抄源端 `Schema.sql`，行数据存 JSON）。
- 所以后台接口只能挂在游戏进程里，用 `TcpListener` 起服务：Android/IL2CPP 上
  `System.Net.HttpListener` 会抛 `PlatformNotSupportedException`，不能用。
- 前端静态文件不能放 `StreamingAssets`（APK 内是 jar，`File.ReadAllBytes` 读不到），
  沿用工程既有链路打包进 `Resources` 的 `.bytes`。
- 打开浏览器后游戏切后台，`AdminHost.Awake` 里强制 `Application.runInBackground = true`，
  否则 Unity 主线程停摆，页面请求会等到超时。

## 4. 线程模型（重要）

`DbStore` 不是线程安全的。所有 HTTP 处理都在 ThreadPool 线程上跑，**只有**通过
`AdminMainThread.Run(...)` 才碰数据：请求线程入队 → `AdminHost.Update` 每帧 `Pump()` →
主线程执行 → 回传结果。主线程 8 秒没响应会返回「游戏主线程繁忙，请求超时」。

Android 上 `runInBackground` 不生效：切到浏览器后 Activity onPause，Unity 主线程整体暂停，
光靠 Update 泵必然超时。所以 `AdminHost` 另起一条 `AdminBackgroundPump` 线程，只在
`OnApplicationPause` 之后（主线程已停，先让 300ms 停稳）接管队列；回前台时
`AwaitIdle` 等它跑完手头一条再交还主线程，DB 始终单线程访问。

## 5. 哪些操作真的会影响游戏

后台写的是数据库，游戏里的角色是**进游戏时读进内存、退出时才写回**的。所以「改库」和
「游戏里立刻看到」是两件事，`Assets/Scripts/Admin/AdminLive.cs` 专门处理「当前在线角色」
的实时同步，其余角色落库即生效（下次选角）。

| 后台操作 | 对正在玩的角色 | 对离线角色 |
| --- | --- | --- |
| 广播 | **立即生效**（游戏内提示条） | — |
| 封停 | **立即踢回登录页**并提示原因；同时 `LoginService` 会挡住下次登录 | 下次登录被拒 |
| 解封 | — | 立即可登录 |
| 传送到射手村 | **立即黑场传送到射手村**（同传送门），并同步 `Profile.LastMapId`，退出时不会被写回旧地图 | 下次选角生效 |
| 角色改名 | **立即生效**（内存记录 + 界面名字一起改） | 下次选角生效 |
| 背包改数量/位置/属性、删道具 | **立即同步内存背包**（当前还没有背包窗口，影响的是穿脱装备/武器等读内存的逻辑，也避免玩家再动这件道具时把后台改动写回去） | 下次选角生效 |
| 新增账号、改密码/权限 | 下次登录生效（无需实时） | 下次登录生效 |
| 服务端参数（game_config） | 读取处实时查表（建号限制、组队限制、世界列表等），**下次触发该动作时生效**；已进游戏的会话不受影响 | 同上 |
| 在线状态 / 版本 / 各类列表 | 只读 | — |
| 掉落 / 商店 / 扭蛋 / 命令库 / 商城 | **改库或覆盖层即时生效**：掉落下一次击杀按新表判定（`DropStore.Invalidate`）；
  商城列表每帧实时读 `modified_cash_item`；商店/扭蛋/命令库下次打开对应界面时读库 | — |
| 天气 | **立即生效**：后台与游戏共用同一个 `NativeWeatherService` 实例（`NativeWeatherHost`），
  改配置/强制天气/时段下一帧 Sync 广播就看到；配置持久化在 `game_config` | — |
| BOT 控制（solo_mapling_*） | 落库即生效（`BotConfig` 每次实时读库）；「一键开市」未实现（阶段 F），点按钮返回明确报错 | — |
| 图鉴 / 任务浏览 | 只读（WZ 直读） | — |
| 文件树 | 桌面/编辑器改 `Data/scripts/*.js` **保存即生效**（`ScriptSource` 直接读磁盘）；
  Android 上改的是 `/sdcard/mapstory/Data/scripts` 副本，包体内置脚本需重跑 `tools/sync_npc_scripts.py` | — |
| 套装 | 落库生效（`game_config` 两条覆盖层）；玩法侧尚未消费套装加成，接入时直接读库 | — |
| `give/v1/resource` | 落库；在线角色的金币/经验同步内存，道具需重进背包视图 | — |
| `server/v1/start|stop|restart|shutdown` | 单机没有独立进程，明确返回错误提示，不假装成功 | — |

两个已知的「看着像生效其实没生效」：

- `exp_rate` / `meso_rate` / `drop_rate` 这几个大区倍率已按源端 `Character.setWorldRates`
  语义折进实际结算：击杀经验 = 怪物 exp × 世界 exp_rate × 家族 Buff × 套装 ExpRate%；
  金币 = 掉落表金币 × 世界 meso_rate × 套装 MesoRate%；掉落概率非 Boss 走世界 drop_rate、
  Boss 走 `boss_drop_rate`（家族分量），再乘套装 DropRate%。改完下次击杀即生效。
- 套装加成已接进玩法侧（≡ 源端 `SetItemManager.compute` + `reapplyLocalStats` +
  `applyDamage`）：面板属性（HP/MP/四维/PAD/MAD/防御/命中/回避）、攻击伤害
  （Damage/Boss/Normal/FinalDamage%）、三倍率（Exp/Drop/Meso）。后台改完无需重登，
  下一次属性结算/攻击/击杀生效。
- `bot-control/v1/market/start` 已实现（≡ 源端 `EnvironmentManager` 市场启动编排：
  FM 入口闲逛/商人/社交三波 + 扭蛋 + 城镇游荡，`already/running/started` 三态）。
  FM 房间 hired-merchant 商店填充与 21 点/OPQ 行为类待搬（见 BotEnvironment.cs 文件头）。
- 后台只改库，不会主动推送给客户端；除上表标注「立即」的几项外，都需要重新选角/重进游戏。

## 6. 接口对照

已接入（读写当前单机库，前端页面可用）：

| 模块 | 接口 |
| --- | --- |
| 认证 | `POST /auth/v1/login`、`DELETE /auth/v1/logout`、`GET /auth/v1/refreshToken` |
| 我的信息 | `GET /account/v1/info`（`webadmin` 恒为 true，前端据此放行 admin 菜单） |
| 账号 | `GET /account/v1`、`GET /account/v1/{id}/characters/maps`、`POST /account/v1`、`PUT /account/v1/{id}`、`DELETE /account/v1/{id}`、`PUT /account/v1/{id}/ban|unban|reset/logged|moveToHenesys` |
| 角色 | `POST /character/v1/online/list`、`PUT /character/v1/{id}/moveToHenesys`、`PUT /character/v1/moveToHenesys`、`PUT /character/v1/{id}/changeName` |
| 背包 | `GET /inventory/v1/getInventoryTypeList`、`POST /inventory/v1/getCharacterList`、`POST /inventory/v1/getInventoryList`、`POST /inventory/v1/updateInventory`、`POST /inventory/v1/deleteInventory` |
| 服务端参数 | `GET /config/v1/getConfigTypeList`、`POST /config/v1/getConfigList|addConfig|updateConfig|deleteConfigList`、`DELETE /config/v1/deleteConfig/{id}` |
| 服务器 | `GET /server/v1/online`、`GET /server/v1/version`、`POST /server/v1/broadcast`（落成游戏内提示条） |
| 掉落 | `POST /drop/v1/getDropList|getGlobalDropList`、`PUT /drop/v1/addDropData|addGlobalDropData`、`POST /drop/v1/updateDropData|updateGlobalDropData`、`DELETE /drop/v1/deleteDropData/{id}|deleteGlobalDropData/{id}` |
| 怪物掉落 | `GET/PUT /mob/v1/{mobId}/drops`、`DELETE /mob/v1/drops/{dropId}` |
| 商店 | `POST /shop/v1/getShopList|getShopItemList`、`GET /shop/v1/getShopItem/{id}`、`PUT /shop/v1/addShopItem`、`POST /shop/v1/updateShopItem`、`DELETE /shop/v1/deleteShopItem/{id}` |
| 扭蛋 | `POST /gachapon/v1/getPools|updatePool|deletePool|getRewards|updateReward|deleteReward` |
| 自动封禁 | `GET /autoban/v1/getConfigList`、`POST /autoban/v1/updateConfig`（AutobanFactory 全枚举内置默认） |
| GM 命令库 | `POST /command/v1/getCommandListFromDB|updateCommand`、`GET /command/v1/reloadEventsByGMCommand|reloadPortalsByGMCommand|reloadMapsByGMCommand` |
| 发放 | `POST /give/v1/resource`（type: item/equip/meso/exp 等） |
| 天气 | `GET /weather/v1/state|config|regions`、`PUT /weather/v1/config|regions/{region}`、`POST/DELETE /weather/v1/override`、`POST /weather/v1/broadcast|wind` |
| BOT 控制 | `GET /bot-control/v1/state|config`、`PUT /bot-control/v1/config`（market/appearance/environment 分组）、`POST /bot-control/v1/market/start`（≡ EnvironmentManager 三态） |
| 组队伴侣 | `GET /bot-control/v1/party`、`PUT /bot-control/v1/party`（solo_mapling_party_* 五项）、`POST /bot-control/v1/party/summon|dismiss` |
| 商城 | `GET /cashShop/v1/getAllCategoryList`、`POST /cashShop/v1/getCommodityByCategory|onSale|offSale|batchOnSale` |
| 文件树 | `POST /file/v1/tree|tree/read|tree/write`（限 `Data/scripts` 子树，key 防逃逸） |
| 图鉴 | `GET /setItem/v1/item/catalog|equipment/catalog`、`GET /mob/v1/catalog`、`GET /skill/v1/catalog|detail/{id}|jobs`、`POST /skill/v1/reload`、`GET /assets/{item,equipment,mob,skill}-icons/{id}.png` |
| 任务浏览 | `GET /quest/v1/townTree`、`POST /quest/v1/list|detail` |
| 套装 | `GET /setItem/v1/catalog`、`PUT/DELETE /setItem/v1/{id}`、`PUT /setItem/v1/series/{id}`、`GET /setItem/v1/equipment/search`、`POST /setItem/v1/custom`、`PUT /setItem/v1/{id}/enabled`、`DELETE /setItem/v1/custom/{id}` |

未接入（`AdminApi` 兜底返回 `code=20000` + `data=null`，页面能打开、表格为空，不会弹错）：

- `server/v1/start|stop|restart|shutdown`（见上表说明）

要继续接某个模块，在 `AdminRoutes.cs` 加路由 + 在 `AdminBackend.cs` 加数据方法即可，
前端不用动；接口形状以源端 `gms-server/src/main/java/org/gms/controller/*.java` 为准。

已删除的页面：**地图检测**（`views/game/mapDetect`、`api/mapDetect.ts`、路由与语言包条目）。
它是 WZ 地图节点自检工具，单机版没有对应的服务端数据源，按要求整块移除，菜单里不再出现。

## 6.1 天气：后台点了「没反应」的三个坑（2026-10-28 修）

后台天气管理本身是好的，但客户端有三处断点，任何一处不修都会表现成「改了没变化」：

1. **客户端天生关着。** `NativeWeather.json` 的 `clientEnabled` 是 `tools/export_weather.py`
   从源端 `clien/config.ini` 的 `enableWeatherSystem=false` 导出来的，而
   `NativeWeatherField.Bind` 第一行就是 `if(!ClientEnabled)return;` → 天空层、雨雪、地面
   积水、粒子、音效**一样都不会挂**。现在 `ClientEnabled` 默认 `true`，是否真的显示改由
   后台「启用视觉天气」（服务端 `Enabled`）经 Sync 的 `Disabled` 位控制。
2. **客户端要等 60 秒才重新 Sync。** `NativeWeatherField.Tick` 原来只在
   `now>=syncAt`（60s 一轮）或自然轮换时才 `Sync`；后台改的 `Override/forced/风` 全在服务
   那份实例里，客户端不知道。现在 `NativeWeatherService.Revision` 在
   `Override/ClearOverride/ApplyForced/RerollWind/Transition/配置更新(Touch)` 时自增，
   客户端每帧比对，变了立刻 Sync 且带 `snap`（直接切画面，不走过渡）。
3. **excluded 地图没有天空层就什么都不显示。** 源端「无天空地图」清单（草地/城镇等）
   `HasSky=false`，进图时整块天气层都不挂，运营在这些图点「下雨」自然毫无反应。现在
   判定改成 `HasSky || Forced`（服务端有 override 或该区域 forced），且这种地图只跳过
   `sky` 组贴图（整屏天空会盖掉原画），夜色/云/雨/雪/雾/彩虹照常叠；**玩家先进图、后台
   随后才点强制天气**时，`Tick` 里发现 `!mounted` 会补挂一次图层。

另外 `NativeWeatherHost.Service` 的实例复用也修了：后台可能**先于**玩家进图访问天气
（`Host.Service` 会自建一份），原来进图时 `NativeWeatherField` 又新建一份覆盖它，后台做过
的强制天气全丢；现在客户端优先接管 `NativeWeatherHost.Shared`。

## 6.2 组队伴侣（消耗品 2431159「基友集合」）

服务端引擎早就搬好了（`BotPartyGrindService` + `CompanionGrindBot` + `BotPartyGrindFloors`），
但原来三处没接：`Assets` 里没有任何地方 `new BotPartyGrindService` 之外去调 `Start`、
后台 `party` 分组恒为空（源端 `BotControlService.config()` 也是 `List.of()`，数值写死在
`PartyGrindCompat` 常量里）、前端「组队」标签页只是一句「尚未开放」。

现在：

- 参数落库成 `solo_mapling_party_companion_enabled / _require_party / _count / _minutes /
  _level_jitter`，由 `BotConfig.PartyCompanion*` 每次实时读（改完下一次召唤生效）。
  默认值与源端一致：5 个、30 分钟、等级 ±3、必须先自己开组队。
- 后台「召唤到当前角色 / 全部解散」直接打 `BotPartyGrindService.Start/Stop`，
  与玩家自己用道具完全同一条路径（后台召唤不消耗道具，用于演示与排查）。
- 面板状态来自 `BotPartyGrindService.ActiveLeaders/Expiry/CompanionCountOf`，显示每支队的
  地图、伴侣数、剩余时间。这些通过 `AdminHost` 注入的钩子传给 `AdminBots`——
  `AdminBots` 不直接引用 `Server.Bot`，否则 `tools/admin-check` 得拖整条 Bot 链才能编译。
- **道具入口仍未接**：本工程还没有「使用消耗品」这条链路（`UseItem` 全工程 0 处），
  所以游戏内用 2431159 不会触发；要彻底还原得先做道具使用系统。

## 7. 改前端 / 重新打包

```bash
./tools/build_admin_ui.sh              # 首次会 npm install
./tools/build_admin_ui.sh --skip-install
```

产物 `Assets/Resources/AdminUi/adminui.bytes`（当前 92 个文件，约 3.35 MB）。
改完前端必须重新跑这一步，否则游戏里还是旧页面。

手机适配的样式在 `src/assets/style/mobile.less`（不动 gms-ui 业务代码，只兜底布局）：
弹窗限高、表格横滑、输入框最小宽；顶部提示条（`.arco-message-list-top`）从 Arco 默认的
`top: 40px` 贴到 `8px`、间距收到 `8px`——游戏内 WebView 是手机横屏，可视高度只有 ~350px，
40px 起排会把提示条压到登录页标题和输入框上。

## 8. 验证

```bash
cd tools/admin-check && dotnet run          # 起临时库，跑 41 项接口断言
cd tools/admin-check && dotnet run -- --serve   # 只起服务，用浏览器人工看界面
cd tools/unity-compile-check && dotnet build -t:Rebuild   # Unity 侧编译
```

`admin-check` 覆盖：登录、token、续期不吊销旧 token、账号信息、菜单、账号分页、角色列表、
角色所在图、背包类型/列表、参数类型/分页/新增、在线状态、掉落/商店/扭蛋/封禁/命令库列表、
天气状态/配置/区域/强制天气(版本号自增 + Sync 带到客户端状态)/清除覆盖、
假人配置/状态、组队伴侣面板/实时状态/配置读写/拒绝跨分组写入/未进游戏时召唤报错、
401 鉴权、封停后登录被拒、解封后可登录、首页 HTML。

注意：依赖 WZ 的模块（图鉴/任务浏览/商城/文件树/套装）由 `AdminRoutes.RegisterUnityOnly`
在游戏进程里注册（`AdminHost` 调用），不进 admin-check 编译范围——它们的契约靠
真机/编辑器里开后台页面验证。

## 9. 已知限制

- 端口只绑 `IPAddress.Loopback`，同一 WiFi 下的其他设备访问不到（刻意如此）。
- 静态资源没有 gzip，首屏几个大 chunk（arco 约 1 MB、chart 约 570 KB）在手机浏览器上
  首次加载会慢 1~2 秒，之后走浏览器缓存。
- 后台只认 `webadmin=1` 的账号登录；普通玩家账号即使密码正确也进不去。
- 单机版没有「在线玩家」概念：`character/v1/online/list` 返回库里全部角色（带 `loggedin`
  账号状态），不是真实在线列表。
- 地图名、道具名、图标、怪物名都从 WZ 直读（图鉴页/掉落列表的名称与图标可用）；
  离线自检里这些名字退回「ID: xxx」兜底。
