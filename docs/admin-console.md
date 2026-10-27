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
3. 浏览器地址：`http://127.0.0.1:8686/`（8686 被占用时自动顺延到 8687…8695）。

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
| 未接入模块 | 无效（返回空数据） | — |

两个已知的「看着像生效其实没生效」：

- `exp_rate` / `meso_rate` / `drop_rate` 这几个大区参数：当前单机版的击杀经验走的是
  `NativeCommunityService.RecordKill` 返回的家族 Buff 倍率，不读 `game_config` 的大区倍率，
  所以改这几个值只会影响世界列表显示。这是单机版原有实现，不是后台的问题。
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

未接入（`AdminApi` 兜底返回 `code=20000` + `data=null`，页面能打开、表格为空，不会弹错）：

- 掉落 `drop`、商店 `shop`、商城 `cashShop`、扭蛋 `gachapon`
- 图鉴 `item/mob/skill/equipment/setItem`（含 `/assets/*-icons` 图标，目前无图标源）
- 任务 `quest`、文件树 `file`、GM 命令 `command`
- 天气 `weather`、BOT 控制 `bot-control`、自动封号 `autoban`
- `give/v1/resource`（发放道具）、`server/v1/start|stop|restart|shutdown`（单机没有独立进程，
  这几个按钮明确返回错误提示，不假装成功）

要继续接某个模块，在 `AdminRoutes.cs` 加路由 + 在 `AdminBackend.cs` 加数据方法即可，
前端不用动；接口形状以源端 `gms-server/src/main/java/org/gms/controller/*.java` 为准。

已删除的页面：**地图检测**（`views/game/mapDetect`、`api/mapDetect.ts`、路由与语言包条目）。
它是 WZ 地图节点自检工具，单机版没有对应的服务端数据源，按要求整块移除，菜单里不再出现。

## 7. 改前端 / 重新打包

```bash
./tools/build_admin_ui.sh              # 首次会 npm install
./tools/build_admin_ui.sh --skip-install
```

产物 `Assets/Resources/AdminUi/adminui.bytes`（当前 94 个文件，约 3.4 MB）。
改完前端必须重新跑这一步，否则游戏里还是旧页面。

## 8. 验证

```bash
cd tools/admin-check && dotnet run          # 起临时库，跑 16 项接口断言
cd tools/admin-check && dotnet run -- --serve   # 只起服务，用浏览器人工看界面
cd tools/unity-compile-check && dotnet build -t:Rebuild   # Unity 侧编译
```

`admin-check` 覆盖：登录、token、账号信息、菜单、账号分页、角色列表、角色所在图、
背包类型/列表、参数类型/分页/新增、在线状态、未接入兜底、401 鉴权、封停后登录被拒、
解封后可登录、首页 HTML（共 20 项）。

## 9. 已知限制

- 端口只绑 `IPAddress.Loopback`，同一 WiFi 下的其他设备访问不到（刻意如此）。
- 静态资源没有 gzip，首屏几个大 chunk（arco 约 1 MB、chart 约 570 KB）在手机浏览器上
  首次加载会慢 1~2 秒，之后走浏览器缓存。
- 后台只认 `webadmin=1` 的账号登录；普通玩家账号即使密码正确也进不去。
- 单机版没有「在线玩家」概念：`character/v1/online/list` 返回库里全部角色（带 `loggedin`
  账号状态），不是真实在线列表。
- 地图名、道具名、图标都要读 WZ，后台暂不解析（地图显示地图 ID，图鉴页为空）。
