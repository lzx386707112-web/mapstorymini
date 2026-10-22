# 浏览器里跑（WebGL 调试）

Unity 工程可以直接出 WebGL 包，用浏览器开、按 F12 调。这份文档只讲"怎么跑起来"和"哪里做了适配"，玩法与地图数据仍然遵循 `original-gameplay-port.md` / `original-map-rendering.md`。

## 一、本机装 WebGL 模块（只需一次）

本机 Unity（2022.3.62f3c1，装在 `/Applications/Unity`）原本**只有 MacStandaloneSupport**，没有 WebGL 构建模块，所以 `BuildTarget.WebGL` 直接不可用。模块是独立安装包，装法：

```bash
# 1) 下载模块（约 874MB）
curl -L -o /tmp/UnityWebGL.pkg \
  "https://download.unity3d.com/download_unity/1623fc0bbb97/MacEditorTargetInstaller/UnitySetup-WebGL-Support-for-Editor-2022.3.62f3c1.pkg"

# 2) 解开（体积约 2.6GB，临时目录）
pkgutil --expand-full /tmp/UnityWebGL.pkg /tmp/wglpkg

# 3) 拷进编辑器（安装包 install-location 写的是 /Applications/Unity/PlaybackEngines，
#    但 2022 mac 编辑器的实际读取路径是 Unity.app/Contents/PlaybackEngines）
ditto /tmp/wglpkg/TargetSupport.pkg.tmp/Payload \
      /Applications/Unity/Unity.app/Contents/PlaybackEngines/WebGLSupport
```

验证：启动日志里出现

```
Register platform support module: .../PlaybackEngines/WebGLSupport/UnityEditor.WebGL.Extensions.dll
```

日志里的 `Native extension for WebGL target not found` 是编辑器在 `/Applications/Unity/PlaybackEngines`（包外路径）找原生扩展时报的，Android/Mac 也一样报，不影响出包。

## 二、构建

切平台和出包分两个进程跑（同进程切平台再 `BuildPlayer` 会读到旧平台，和 `BuildToDevice` 踩的是同一个坑）：

```bash
UNITY=/Applications/Unity/Unity.app/Contents/MacOS/Unity

$UNITY -batchmode -quit -projectPath /Users/lizixian/MapleDash \
       -executeMethod BuildWebGL.SwitchToWebGL -logFile /tmp/wgl_switch.log

$UNITY -batchmode -quit -projectPath /Users/lizixian/MapleDash \
       -executeMethod BuildWebGL.Build -logFile /tmp/wgl_build.log
```

菜单里也有：`MapleDash ▸ 切换平台到 WebGL` / `MapleDash ▸ 构建 WebGL（浏览器调试）`。
产物：`Builds/WebGL/`（`index.html` + `Build/*`）。首次 IL2CPP 全量编译约 5~15 分钟，之后增量。

构建配置（`Assets/Editor/BuildWebGL.cs`）：

| 项 | 值 | 原因 |
|---|---|---|
| 脚本后端 | IL2CPP Release | WebGL 只有 IL2CPP |
| 代码裁剪 | `stripEngineCode=false` + Managed Stripping Disabled | OnGUI 依赖引擎内置 GUISkin，裁掉就是每帧 NRE（安卓上已踩过） |
| 压缩 | Brotli + `decompressionFallback=true` | 任意静态服务器都能直接跑，不用配 `Content-Encoding: br` |
| 异常 | `FullWithoutStacktrace` | 异常信息进浏览器控制台，方便看栈 |
| 内存 | 初始 512MB / 上限 4096MB / 几何增长 | `Resources/Art` 约 115MB（贴图占大头），默认 32MB 初始堆不够 |
| 模板 | `PROJECT:MapleDash` | 见下节 |

## 三、跑起来

```bash
tools/serve_webgl.sh          # 默认 8099
# 浏览器打开 http://localhost:8099/
```

**必须走 http**，`file://` 打开会被 CORS/WebAssembly 拦掉。

## 四、浏览器里的调试入口

### URL 参数（`Assets/Scripts/WebBoot.cs`，仅 WebGL 编译）

| 参数 | 作用 |
|---|---|
| `?map=40001` | 跳过大厅直接进这张图 |
| `?map=1000000&god=1` | 进图 + 无敌（99999 血） |
| `?auto=1` | 跳过大厅，进上次所在的地图 |
| `?skill=1` | 直接展开技能窗（截图/验收用） |
| `?lv=60` | 临时把角色抬到 60 级（按原成长曲线逐级补，**不写存档**），验收等级牌/经验条用 |
| `?name=小明` | 首次建档时用的角色名（之后写进存档，改它不会改名） |

实现方式只是往 PlayerPrefs 写 `autostart` / `debug_field_map` / `debug_god` / `debug_level` —— 这几个键 `GameLoop.Start` 与 `Profile.Load` 本来就读，所以不改任何游戏逻辑；`#if UNITY_WEBGL && !UNITY_EDITOR` 包着，编辑器和安卓流程完全不受影响。

> 注意：`?lv=` 只抬等级，不改存档。低等级 MP 上限本来就很低（Lv.6 战士约 30），而五/六转技能 `mpCon` 是 22~30 —— 放两下就空是原版行为，不是 bug。

### 页面调试条（`Assets/WebGLTemplates/MapleDash/index.html`）

**右上角**小条（左下角已经交给原版状态栏：LV./名字/HP/MP/EXP；右下角是技能格）：填地图 id → 点「进入」= 带着 `?map=` 重载；「全屏」按钮走 `unityInstance.SetFullscreen`。`window.unityInstance` 暴露在控制台里，可以直接调 Unity 的 JS API。

### 输入

- 键盘：方向键 / WASD（上 = 进光洞、爬绳），跳跃与技能沿用原有键位（见 `PlayerCtrl`）。
- 鼠标：大厅角色与 3D 展示区可拖；进入地图后左半屏按住可当摇杆（`TouchPad` 的鼠标回退分支）。
- 画布点击一次拿到焦点后键盘才生效（`tabindex=-1`），这是 WebGL 的正常行为。

### 看日志

Unity 的 `Debug.Log` 直接进浏览器控制台（`console.log`），异常也会带上 `at ...` 的 wasm 栈。想看更细的引擎日志，构建时把 `PlayerSettings.WebGL.debugSymbols` 打开（会增加构建时间与体积，默认关）。

## 五、已知适配 & 限制（都不影响编辑器/安卓）

| 项 | 说明 |
|---|---|
| 渲染分辨率 | 模板里 `config.devicePixelRatio` 压到 1.5（Retina 上跑 2x 会明显掉帧），可按需改回 |
| 首次加载体积 | `Resources/Art` 全量进包，`Build/*.data` 体积大、首次进图要下载；纯调试够用，要对外发布应改成 Addressables 分包 |
| 画面比例 | 模板按 16:9 等比铺满窗口；游戏 UI 按虚拟高 720 排版（`UiKit`），窄窗口下两侧留黑边 |
| 音频 | WebGL 需要用户手势后才能出声，第一次点击画布后才正常 |
| `Application.Quit` | WebGL 无意义（原代码也没用） |
| 存档 | `PlayerPrefs` → IndexedDB；换浏览器/无痕模式就是新档 |

## 六、已经跑通的验证

无头 Chrome（`--headless=new` + SwiftShader，CDP 驱动）实测：

- `?map=1000000`：控制台依次出现 `[WebBoot] 直接进入地图 1000000` → `[Art] 素材就绪：67 组` → `[Field] 原版地图 37 张，怪物 11 种，掉落表 9 份，物品 126 件` → `[Field] 进入 1000000 彩虹村：刷怪点 0，传送门 13`，画面与编辑器一致（蘑菇屋茎在草皮收口、左侧光洞可见）。
- 键盘：点一下画布后按住 `→` 2.5s，相机跟随平移（前后截图差异 71.9 万像素），说明 WebGL 下 `Input.GetKey` 正常。
- `?map=40001&god=1`：`[Field] 进入 40001 蜗牛打猎场Ⅱ：刷怪点 27，传送门 9`，HP 显示 99999/99999，蜗牛与绳梯正常。

截图：`docs/field-shots/webgl-1000000-彩虹村.png`、`docs/field-shots/webgl-40001-蜗牛打猎场Ⅱ.png`。

浏览器侧的噪音日志（不是故障）：

- `The AudioContext was not allowed to start…` + 后续 `Audio context resumed after N seconds`：浏览器要求先有用户手势才出声，点一下画布即恢复。
- `GL Driver Message … GPU stall due to ReadPixels`：SwiftShader 软件渲染的提示，真机/独立显卡不会出现。

## 七、切回安卓出包

切平台会把编辑器活动平台改成 WebGL，要回到 APK 流程就先切回来（`BuildToDevice` 也会提示并自动切一次）：

```bash
$UNITY -batchmode -quit -projectPath /Users/lizixian/MapleDash \
       -executeMethod BuildToDevice.SwitchToAndroid
```

APK 的构建设置（SDK/JDK/NDK、ARM64、IL2CPP、OnGUI 不裁剪）没有因为 WebGL 改动，两边共用同一套 C# 与 `Field/*.json`。

## 八、和原生构建的关系

WebGL 只是多一个构建目标，不替换 APK 流程：`BuildToDevice` 那套（SDK/JDK/NDK、ARM64、IL2CPP）没动。两边的差异只有上面表格里那几项平台设置，游戏逻辑、地图数据、掉落规则共用同一套 C# 与同一份 `Field/*.json`。
