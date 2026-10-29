# MapleDash（未来岛）

横版 2.5D 冒险岛风格手游 —— 北斗（BeiDou）冒险岛源端的 Android 客户端重写。

游戏逻辑完全遵循 `beidou.exe` 与北斗服务端；所有 UI 素材直接引用源端 WZ IMG 资源，
禁止自行设计绘制。Unity **2022.3.62f3c1**，Android 优先。

---

## 一、当前状态

已实现：

- 核心战斗：打击感四件套（顿帧/震屏/击退/闪白）、四格技能 + CD、纸娃娃、伤害飘字、Buff/临时状态
- 怪物与 Boss：按源端刷怪逻辑（`SpawnPoint`/`MapSpawner`）、Boss 视频特效、怪物技能
- 假人（队友 AI）：自动跟随与攻击，外观走固定模板池 + 预合成，分帧泵控性能
- 地图：原始 IMG 直读渲染、传送门（按 pt 分流）、NPC 名牌、脚底气泡、天气系统
- UI：HUD、状态栏、底部虚拟按键、商店/装备/背包/道具悬浮框、聊天窗面板、技能窗、键位设置
- NPC 与任务：直跑源端脚本（`gms-server/scripts-zh-CN/{npc,quest,portal,BeiDouSpecial}`，约 1183 条 .js）
- 登录链路：连接 → 新号性别 → 选区，登录 UI 按源端口径
- 后台管理系统：源端 `gms-ui` 接入，游戏内 HTTP 服务 + 内嵌前端

尚未实现（边界）：

- 无音效
- 无平台碰撞
- 组队/远征/公会等整套社区系统缺失

---

## 二、技术架构

```
Assets/Scripts/
├── Server/    纯 C# 服务端复刻（DbStore JSON 快照复刻源端 Schema），禁止引用 UnityEngine
├── Bot/       假人行为（时钟 BotScheduler.Clock，禁 UnityEngine.Time）
├── Field/     刷怪等地图侧服务端逻辑
├── Wz/        原生 WZ IMG 解析（直读运行时）
├── Scripting/ 直跑源端 NPC/任务/传送门脚本（不重写）
└── Npc/Quest/ 对话与任务 UI
```

- 双仓库布局：`MapleDash/`（本仓库，Unity 工程）+ `../MapleDashResources/`（约 6.9 GiB 原始资源，不进 Git）
- 素材源：北斗源端 `~/Documents/mxd/BeiDou-Server/clien/Data`（GMS083）
- 逆向结论沉淀在 `.agents/skills/beidou-client-reverse/references/*.md`，分析或修改原端相关功能前必须先查该 skill

---

## 三、打开工程

**固定在 Unity 2022.3.62f3c1**（中国区当前可下载的最高 LTS；Unity 6 镜像无 6000.x 文件装不上）。
2022.3 → 6000.x 是官方支持的原地升级路径。

1. Unity Hub → `Add` → `Add project from disk` → 选 `MapleDash` 目录
2. 首次拉取需运行 `tools/package_native_assets.py` 复制整个原始 Data（本机约 6.8 GiB）；
   原始 IMG 包不提交 Git
3. 打开工程后运行即可，场景由代码生成

编辑器默认读取 `../MapleDashResources/Data`，也可用环境变量 `MAPLEDASH_IMG_ROOT` 覆盖。
Unity 菜单 **MapleDash → 地图目录（完整 Data）** 可搜索全部 6,336 张地图并进入。

---

## 四、资源管线（原始 IMG 直接加载）

本分支默认从原始 IMG 读取地图、人物纸娃娃、怪物、技能、鼠标和 UI，不再加载离线导出的 PNG。
打包命令、架构与验证边界见 [直接加载说明](docs/direct-img-runtime.md)。

更新资源：

```sh
tools/package_native_assets.py --data <源Data> --wzpy <wz-python>
```

生成 Android 资源包：

```sh
rtk proxy python3 tools/build_resource_pack.py   # 生成 ../MapleDashResources/MapleDashResources.zip
rtk proxy python3 tools/sync_android_data.py --zip
```

- Android 资源目录统一为 `/sdcard/mapstory`：可放 `MapleDashResources.zip`，也可放已展开的 `Data/`
- 首次启动自动解压并逐文件校验；已安装资源启动时校验后直接进游戏
- Android 11+ 需在系统设置允许「所有文件访问」，Android 10 及以下申请存储权限
- ZIP 必须保留 `resource-manifest.json` 与 `Data/` 这一层结构；资源缺失、损坏或权限未授予时停留加载页

此共享目录方案用于当前测试，商店发布时需按渠道要求调整资源目录/分发方式。

---

## 五、接 Android 真机

装好 Android Build Support 后的设置顺序：

1. `Edit → Project Settings → Player`
2. `Other Settings → Target Architectures` 勾 **ARM64**
3. `Other Settings → Scripting Backend` 选 **IL2CPP**（ARM64 只支持这个）
4. `Other Settings → Minimum API Level` 设 **26**
5. `Resolution → Default Orientation` 选 **Landscape Left**
6. `File → Build Settings → Android → Switch Platform`，勾 `Development Build` 和 `Autoconnect Profiler`，然后 `Build And Run`

真机问题先 `adb logcat -s Unity`。日志走 `Client/FileLog`（log_to_file 默认 1）+
`LogShare`（MediaStore.Downloads，不要换 FileProvider）。

---

## 六、后台管理系统

源端 `gms-ui` 已原样接入：游戏进程内起一个只监听 `127.0.0.1` 的 HTTP 服务，
侧边栏「后台系统」用系统浏览器打开 `http://127.0.0.1:8686/`
（默认账号 admin / admin，首次启动自动创建）。

- 前端源码在 `AdminUi/`，构建与打包用 `tools/build_admin_ui.sh`，接口清单见 [后台系统说明](docs/admin-console.md)
- **改前端必须 `./tools/build_admin_ui.sh --skip-install` 重打 `Assets/Resources/AdminUi/adminui.bytes` 并重新出包**，否则游戏里还是旧页面
- 接口自检：`cd tools/admin-check && dotnet run`
- 调试后台页面：`tools/admin-check -- --serve` + 无头 Chrome（CDP 脚本）

---

## 七、验证工具

| 工具 | 用途 |
|---|---|
| `tools/unity-compile-check` | dotnet 编译检查，0 error 才过 |
| `tools/server-check`（`tools/boss-check` 等） | 服务端逻辑回归 |
| `tools/scan_script_api.py` | NPC 脚本 API 绑定扫描（「点了没反应」先跑这个） |
| `QuestNpcChecks`（Unity batchmode） | NPC 脚本链路检查 |
| `tools/admin-check` | 后台接口自检 |
| `tools/mcv-check` / `mcv-playback-check` | 视频播放检查 |
| `tools/inspect_beidou_client.py` | BeiDou.exe 递归下降反汇编（`--fn` / `--fields` / `--strings`） |

UI 验收优先写无渲染断言（`LoginFlowChecks` 那种：new 组件 + 反射调真实方法），
不要指望 batchmode 截图脚本；给用户看效果用 `Assets/Editor/PreviewShot.cs` 实拍（真实存档+真穿装备）。

---

## 八、开发规约（摘要）

完整规则见根目录 [AGENTS.md](../AGENTS.md)：

- 游戏逻辑必须完全遵循 `beidou.exe` 和服务端，禁止凭空推断；找不到对应逻辑先问用户
- UI 素材全部引用源端 IMG；仅已核实原端用代码绘制的细节允许严格照搬原绘制代码
- 逆向 `BeiDou.exe` 新发现必须沉淀到 `beidou-client-reverse` skill，先查再做，避免重复分析
- NPC 脚本不重写，直跑源端 .js；补脚本 API 必须回源端 Java 核对，禁止凭脚本猜
- 改刷怪先动 `Server/Field`，改假人行为先动 `Server/Bot`
- `Server/` 禁 UnityEngine；IL2CPP 禁 1252 编码；`Schema.sql` 与 `Schema.txt` 保持同步

---

## 九、文档索引

| 文档 | 内容 |
|---|---|
| [直接加载说明](docs/direct-img-runtime.md) | 原始 IMG 直读架构与打包 |
| [后台系统说明](docs/admin-console.md) | 后台接口清单与未接入模块 |
| [单机架构规划](docs/single-player-architecture.md) | 职责边界与脚本归属 |
| [原端玩法迁移记录](docs/original-gameplay-port.md) | 档案/商店/伤害入口/死亡奖励等 |
| [假人加载架构](docs/bot-loading-architecture.md) | Bot 外观池、预合成、分帧泵 |
| [登录与档案架构](docs/login-db-death-architecture.md) | 登录链路、DB、死亡处理 |
| [原端地图渲染](docs/original-map-rendering.md) | 地图显示处理与差异 |
| [源端脚本异常记录](docs/source-script-exceptions.md) | 脚本兼容性 |
| [SoloMapling 移植清单](docs/solomapling-port-manifest.md) | 移植范围记录 |
