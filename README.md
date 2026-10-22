# MapleDash — M0 手感验证原型

横版 2.5D 割草闯关的最小可运行版本。**目的只有一个：在手机上判断"打这一下爽不爽"。**
不含美术、不含剧情、不含养成，全部角色用纯色方块代替。

---

## 一、打开

**本项目固定在 Unity 2022.3.62f3c1 上。** 原因是：Unity 6 的官方下载在本机网络会被强制重定向到中国镜像，而镜像没有提供 6000.x 的文件（返回 404），装不上。2022.3.62f3c1 是中国区当前可下载的最高 LTS 版本。

这对 M0 没有影响 —— 本项目只用到 2D 基础 API（Sprite、Camera、IMGUI、Input），2022.3 全部支持。将来要升 Unity 6，2022.3 → 6000.x 是官方支持的原地升级路径。

1. 已装好 Unity Hub（3.3.7，中国版）。打开它，登录 Unity 账号。
2. 编辑器安装到 `/Applications/Unity/Hub/Editor/2022.3.62f3c1`（见 `~/Downloads/unity_setup/`）。
3. Unity Hub → `Add` → `Add project from disk` → 选这个目录 `MapleDash`。
4. 打开后直接按 **Play**。场景是运行时用代码生成的，不需要拖任何东西。

> 如果 Unity 报 `Main Camera` 相关警告可以忽略，脚本会自动关掉场景里的默认摄像机。

---

## 二、操作

| 操作 | 键盘 | 手机上 |
|---|---|---|
| 左右移动 | `A` / `D` 或 `←` `→` | 左半屏摇杆 |
| 换深度层（远近） | `W` / `S` 或 `↑` `↓` | 摇杆上下 |
| 跳跃 | `空格` | 右下按键 |
| 技能 1–4 | `1` `2` `3` `4` 或点击底部格子 | 点底部格子 |

普攻是**自动**的：靠近同层或相邻层的敌人就会自己砍。你只需要管走位和技能。

---

## 三、这个原型要验证的东西

### 1. 打击感（最重要）

右上角面板有四根滑条，**拖动即时生效**，不用重新编译：

- **顿帧**：命中瞬间把游戏冻结多少毫秒。这是打击感的第一要素，没有它打上去像打在棉花上。
- **震屏**：摄像机抖动振幅。
- **击退**：敌人被推开的距离。
- **闪白**：受伤白闪持续多久。

面板底部有三个预设（轻快 / 标准 / 厚重），先各点一遍感受差异，再自己微调。

**判断标准**：连续砍 20 下，你会不会觉得"不想停"。如果会，参数就对了。

经验区间：顿帧 `50~90ms`、震屏 `0.12~0.25`、击退 `0.7~1.2`。超出这个范围通常会变成"卡顿"而不是"有力"。

### 2. 深度层是否可读

移动时用 `W`/`S` 换层，观察：
- 换层时角色是"滑过去"还是"跳过去"（应该是平滑滑过去）
- 你能不能一眼看出自己站在第几层（左上角有层号，但**理想状态是不看数字也能感觉到**）
- 近战只打同层和相邻层 —— 你会不会因为打不到而困惑

如果第三点让人困惑，说明层差要收窄（`LaneStep` 调小）或改成"任意层都能打但异层伤害减半"。

### 3. 四格技能的节奏

刻意设计成 6s / 15s / 22s / 45s 的梯度。测试：
- 1 号技能（6 秒）是不是"随手就按"—— 如果不是，说明 CD 太长，玩家会站着等
- 4 号技能（45 秒）放出来的时候，你有没有"值得等"的感觉

### 4. 队友是否碍事

四名英雄会自动跟随并攻击。观察它们会不会：
- 挡住你的视线（不会，它们在你身后）
- 抢走你的目标（会，这是故意的 —— 队友是"降低难度"而不是"替代操作"）
- 挤成一团（`xOffset` 是错开的，不该出现）

---

## 四、代码结构

后续单机功能的职责边界、现有脚本归属及渐进拆分顺序见 [单机架构规划](docs/single-player-architecture.md)。该文档按当前代码核对，以下 M0 说明中的部分限制已随开发更新。

原端源码对应的档案/商店、伤害入口、技能等级配置、死亡奖励与战斗清理迁移，见 [原端玩法迁移记录](docs/original-gameplay-port.md)；其中明确区分已实现规则与尚未完成的 1:1 范围。

| 文件 | 作用 |
|---|---|
| `GameLoop.cs` | 场景构建、刷怪、游戏状态。**改数值先看这里** |
| `PlayerCtrl.cs` | 主角移动、跳跃、深度层、自动普攻 |
| `SkillSystem.cs` | 四格技能与 CD |
| `AllyAI.cs` | 英雄跟随与自动攻击 |
| `EnemyUnit.cs` | 敌人行为、血条、受击 |
| `HitFeel.cs` | **打击感四件套**，所有手感参数的唯一来源 |
| `Fx.cs` | 临时特效（挥击、爆点、冲击波、闪白） |
| `CameraRig.cs` | 跟随与震屏 |
| `GameHUD.cs` | 技能栏、血条、调参面板 |
| `SpriteFactory.cs` | 纯色方块生成（美术就位后替换这里） |

玩法数据现由原端快照提供：`GameplaySkills.json`、`GameplayCombat.json`、`GameplayRewards.json`；使用 `tools/export_gameplay.py` 和 `tools/export_combat_data.py` 重导出。原端依据及单机适配边界见 [原端玩法核对](docs/original-gameplay-port.md)。

---

## 五、已知限制（M0 阶段故意不做）

- 地图：harbor/forest/cliff/nest 四关按原 WZ 坐标导出整图主体、全部 foothold 及独立背景；镜头按 VR 双轴夹边。主体动画与按角色地图层的遮挡尚未完整迁移，详见文末源码核对记录。
- 没有碰撞体，跳跃只是抛物线，不处理平台
- 没有音效 —— 打击感缺了听觉那一半，这是下一步要补的
- 没有存档、没有 UI 框架、没有养成
- 敌人 AI 只会直线走过来打你，不会任何技能

---

## 六、M0 的验收线

**在真机上推着走 60 秒，再砍 20 下。如果这两件事都让你觉得"顺"，才继续做 M1。**

不顺手就调参数；调完还是不顺手，说明横版 2.5D 这个方向本身有问题 ——
那也比做完 30 周才发现要好。

---

## 七、接 Android 真机

Unity 里的设置顺序（装好 Android Build Support 之后）：

1. `Edit → Project Settings → Player`
2. `Other Settings → Target Architectures` 勾 **ARM64**（Google Play 强制要求，且 32 位已淘汰）
3. `Other Settings → Scripting Backend` 选 **IL2CPP**（ARM64 只支持这个）
4. `Other Settings → Minimum API Level` 设 **26**（Android 8.0，覆盖面足够且省掉大量兼容代码）
5. `Resolution → Default Orientation` 选 **Landscape Left**
6. `File → Build Settings → Android → Switch Platform`，勾 `Development Build` 和 `Autoconnect Profiler`，然后 `Build And Run`

用 USB 连手机，第一次会装 Unity Remote 或直接推 APK。**手感必须在真机上试，鼠标键盘上的判断不作数。**

---

## 八、美术素材管线（WZ → Unity）

素材来自本地北斗客户端 `~/Documents/mxd/BeiDou-Server/clien/Data`（GMS083 结构），
由 `tools/wz_assets.py` 用 `tool/wz-python` 解析 `.img` 后导出。

```sh
# 全量重抽（角色 / 怪物 / 瓦片 / 背景）+ 写入 Unity Resources
/Users/lizixian/.workbuddy/binaries/python/envs/default/bin/python tools/wz_assets.py all --unity

# 只重抽某一块
... tools/wz_assets.py chars            # 5 个职业：stand/walk/jump/atk/hit/die
... tools/wz_assets.py mobs             # 蘑菇/绿蘑菇/绿水灵/猪/蜗牛/蘑菇王/绿水灵王
... tools/wz_assets.py map              # grass/沙滩瓦片 + 4 套视差背景
... tools/wz_assets.py preview          # 只重出拼版图 art/preview/contact.png
```

- 中间产物：`art/`（PNG + `manifest.json`，尺寸/轴心/帧延时都在里面）。
- Unity 侧：`Assets/Resources/Art/**/*.bytes` + `Art/manifest.json`。
  **必须用 `.bytes` 且不带 `.png` 后缀** —— `Resources.Load` 会把路径末段的扩展名剥掉，
  `stand_0.png` 会被找成 `stand_0` 而返回 null（踩过）。
- 运行时：`Art.cs`（贴图/Sprite/轴心）、`SpriteAnim.cs`（逐帧动画）、`Backdrop.cs`（天空+视差+地面）。
- 轴心：角色取 WZ 的 navel（脚底中线），怪物取 canvas 的 `origin`；没有素材时自动退回纯色方块。

### 技能特效（每个技能按原版拆部件）

资源由 `tools/export_skill_vfx.py` 从 Skill IMG 和服务端引用的 Map/Effect IMG 导出到
`GameplaySkillVfx.json` / `Art/NativeSkillVfx/`。角色技能动作由 `tools/wz_actions.py` 解析身体
`action/frame` 引用后合成，候选动作不作为重复连播序列。

运行时 `SkillFx` 播放原帧，`SkillVisuals` 处理释放、落矢、弹道与全屏层，`SkillSystem` 负责判定。
烈火箭/箭雨按原攻击框进行范围攻击，火焰箭等使用独立远程目标范围。
斗气死亡断层额外使用原 `Map/Effect.img/customSkill/deathFault/full` 全屏动画。
原图验证、各技能接入及仍属本地适配的参数详见 [逐技能核对](docs/skill-vfx-audit.md)。

```sh
rtk proxy python3 tools/export_skill_vfx.py
rtk proxy python3 tools/verify_skill_vfx.py
```

自动截图（验证画面用）：

```sh
/Applications/Unity/Unity.app/Contents/MacOS/Unity -batchmode -projectPath "$PWD" \
  -executeMethod PlaySmokeTest.Run -smokeLevel 1 -smokeDuration 12 -smokeShots /tmp/shots \
  -smokeCast 1 -smokeShotEvery 0.4 -smokeClass mage
```
`-smokeLevel` 是关卡索引（0=1-1 … 3=1-4），截图走 RenderTexture，batchmode 下也能出图。
`-smokeCast 1` 每 2.2 秒按顺序放四格技能，`-smokeClass` 指定职业（knight/warrior/mage/archer/priest），
用来逐个职业核对释放、弹道、命中部件。


## 原客户端地图显示处理

处理依据及与原端尚存的差异见 [源码核对记录](docs/original-map-rendering.md)。
手机端全屏显示，保持 768px 逻辑高度并按屏幕比例扩展横向视野，地图、人物共用 40 PPU。
相机按整图 VR 边界双轴跟随，背景按 WZ 的 rx/ry/type 独立视差、平铺和自动滚动。

从本地原客户端重新导出四关地图（保留人物、技能等已有 manifest 配置）：

```sh
python3 tools/wz_stage.py --unity
```

需要 Pillow 和 `~/Documents/mxd/BeiDou-Server/tool/wz-python`。
中间 PNG 位于 `Library/WzStage`；原始客户端文件只读。

以下旧导出流程仅保留作历史参考；当前分支请使用文末的原始 IMG 管线，无需重新导出人物或地图。
基础移动/跳跃检查：Unity batchmode 执行 `MovementChecks.Run`，覆盖 30/60/120Hz 的台阶、落地和摇杆手指归属。

## 原始 IMG 直接加载

本分支默认从原始 IMG 读取地图、人物纸娃娃、怪物、技能、鼠标和 UI，不再加载离线导出的 PNG。
首次拉取需运行 `tools/package_native_assets.py` 复制整个原始 Data（本机约 6.8 GiB）；原始 IMG 包不提交 Git。
打包命令、架构与验证边界见 [直接加载说明](docs/direct-img-runtime.md)。

Unity 菜单 **MapleDash → 地图目录（完整 Data）** 可搜索全部 6,336 张地图并进入，地图/怪物按需读取对应 IMG。

## 独立资源目录

完整原始资源现位于项目同级的 `../MapleDashResources/Data`，不在 Unity Assets 中。编辑器默认直接读取该目录，也可用 `MAPLEDASH_IMG_ROOT` 覆盖。更新资源使用 `tools/package_native_assets.py --data <源Data> --wzpy <wz-python>`。

`rtk proxy python3 tools/build_resource_pack.py` 生成 `../MapleDashResources/MapleDashResources.zip`，含清单与完整 Data。Android 可直接同步目录（原安装脚本已适配），也可将 ZIP 放到 `/sdcard/mapstory/MapleDashResources.zip`，启动后点击“安装资源包”。ZIP 必须保留 `resource-manifest.json` 与 `Data/` 这一层结构；安装需预留 ZIP 加解压目录的空间，更新期间还会保留旧 Data。

传输压缩包（APK 需重新构建以包含资源安装界面）：

```sh
rtk proxy python3 tools/build_resource_pack.py
rtk proxy python3 tools/sync_android_data.py --zip
```

之后启动游戏，点击“安装资源包”。也可以在电脑解压后按原目录结构同步到手机 `files/Wz/Data`，使用目录同步脚本会自动生成完成标记。


当前 Android 资源目录统一为 `/sdcard/mapstory`：可放 `MapleDashResources.zip`，也可放已展开的 `Data/`。游戏启动自动显示进度条；首次自动解压并逐文件校验，已安装资源启动时校验后进入游戏，无需点击安装按钮。Android 11 及以上首次需要在系统设置允许“所有文件访问”，Android 10 及以下申请存储权限。资源缺失、损坏或权限未授予时停留加载页并提示，不进入游戏。ZIP 保留，成功后可自行删除。此共享目录方案用于当前测试，应用商店发布时需调整为符合渠道要求的资源目录/分发方式。
