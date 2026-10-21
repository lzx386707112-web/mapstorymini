# 新地图迁移与验收

命令在目标项目根目录执行；以下路径为本机示例。先确认原客户端数据和 Unity 版本路径存在，读取 git 状态，保留当前工作。

## 接入原地图

1. 从用户指定地图 ID 或原端关卡数据确定目标；ID 不明时先检索原数据，无法唯一确定再问。用 `wzmap.find_map_sub(map_id)`、`GameMap` 检查 info、foothold、ladderRope、tile、obj、back。从真实 foothold/portal 选择出生参考位置，核对哪些高台须跳跃或绳梯到达。
2. 在 `tools/wz_stage.py` 的 STAGES 增加或替换 `(map_id,gx,gy)`，使用明确主题键。仅导出目标主题，保留 manifest 其他地图与角色。当前命令：

   ```sh
   rtk proxy python3 tools/wz_stage.py <theme> --unity
   ```

   中间 PNG 在 Library/WzStage；Unity 内容在 Assets/Resources/Art，以 PNG 内容的 .bytes 由运行时加载。不要误删非目标地图或整个 manifest。
3. 当前导出器对任一纹理宽/高超过4096会拒绝；不能裁掉远端平台或缩小地图规避。目标超限时按原坐标分块并接入运行时，保持同 PPU、完整碰撞与 VR。无 foothold/特殊地图需按其原端类型处理，不能伪造底部平地掩盖不支持。
4. 在章节 JSON 的主题/关卡配置中接入主题键，核对 StageModel 和 GameLoop 实际调用。Art.Stage 能加载后，确认 ApplyTheme 将 WalkMin/Max、Footholds、Climbs、相机 Stage 都传入，再 ResetTerrain。UI/敌人坐标若仍假设最低地面，需按这次迁移实际范围处理。
5. 通常添加地图无需重导出人物。只有人物原点/动作/职业装备/原端版本变化时才运行：

   ```sh
   rtk proxy python3 tools/wz_actions.py
   ```

   该脚本会重导出全部当前职业/发型、Physics 及动作元数据，改动范围较大；先检查其实现和 manifest。不要用它作为每次加地图的例行步骤。

## 数据验证

- 对照原 IMG 核对全部 foothold（含竖直墙）数量、坐标、斜坡和 ladderRope 字段。
- VR、图片 px 边界与出生 ground 各自正确；所有引用的 .bytes 存在，背景无漏层。
- 新地图没有绳梯时空列表是正常情况，不能沿用前一地图的 Climbs。
- 若修改人物，确认各职业/发型的原点、必需动作、帧数和 abs(delay)，检查 UOL/动作回退，避免导出异常被跳帧逻辑静默掩盖。

## 定向物理与运行检查

`MovementChecks` 当前显式列举 harbor/forest/cliff/nest；添加地图时将新主题纳入相关循环，并为目标地图有辨识度的台阶/斜坡/绳梯路线补检查。没有绳梯的地图不要断言必须存在绳梯。

```sh
rtk proxy /Applications/Unity/Unity.app/Contents/MacOS/Unity -batchmode -projectPath /Users/lizixian/MapleDash -executeMethod MovementChecks.Run -logFile /tmp/mapledash-movement.log
```

检查实际退出码及 `[MovementChecks] PASS`，不能仅凭启动成功。编辑器非 Play 创建 PlayerCtrl 时 Awake 不自动执行，夹具需显式初始化，才能检查真实资源和动作状态。当前检查包含：

- 30/60/120Hz 台阶、走出边缘、落向低处、头顶悬空平台不变横墙。
- 每段非竖直 foothold 的局部落地；原图绳梯抓取、静止、下爬、uf 顶部退出、方向跳离。
- 攀爬动作选择、停帧、触屏手指归属与释放清零。

每段局部可落地不代表从出生点可达。另检查至少一条从低处经跳跃/绳梯到高处的实际路线，核对绳底离地间距是否在原跳高范围；不能将绳梯任意延长到地面。

运行画面/状态接线变化适合 Play smoke；文档或 skill 编辑无需运行游戏。示例：

```sh
rtk proxy /Applications/Unity/Unity.app/Contents/MacOS/Unity -batchmode -projectPath /Users/lizixian/MapleDash -executeMethod PlaySmokeTest.Run -smokeLevel <level-index> -smokeDuration 12 -smokeWidth 1200 -smokeHeight 540 -smokeShots /tmp/mapledash-map-shots -logFile /tmp/mapledash-map-smoke.log
```

同一项目不能同时开两个 Unity 进程；上述测试入口自行退出，勿加 -quit 使异步检查提前终止。核对日志异常/退出、查看实际输出图片。PlaySmokeTest 会设置测试用 PlayerPrefs（autostart/debug_god 等）；留意现有用户配置，不覆盖职业/存档，必要时恢复本次更改的测试选项。

离屏截图在目标宽高比下渲染，但 batchmode 的 Screen/IMGUI 可能仍是编辑器尺寸；离屏图不能证明手机 UI 已验证。至少检查宽屏两侧背景、相机上下跟随、人物脚底及站立/行走/跳跃/攀爬动作。需要实机验收时按用户授权构建；不要默认打 APK。

最后运行 `rtk proxy git diff --check` 并审查实际 diff。交付说明目标地图、数据来源、通过的检查、是否走通路线、是否实机验证；标注尚未复刻的动画物件、地图层遮挡或特殊移动行为。
