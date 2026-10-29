# 当前实现约定

下列文件名相对 MapleDash 项目根目录；以实际代码为准。支持代码读取入口：原端 `tool/client-debug`；WZ 解析库：`tool/wz-python/wzpy`；拆分 IMG 数据：`clien/Data`。当前解析 region 是 GMS，迁移其他版本时先核实格式/密钥。

## 文件职责

| 文件 | 职责 |
| --- | --- |
| `tools/wzmap.py` | 原始 IMG 读取、tile/obj/背景解码、地图栅格合成；DATA/WZPY 当前为本机路径 |
| `tools/wz_stage.py` | STAGES 配置、全图 foothold/ladderRope、VR 和独立背景导出 |
| `tools/wz_actions.py` | 职业/发型角色合成、原点还原、动作与 Physics 导出 |
| `Assets/Resources/NativePresentation.json` | stages、角色帧/轴心、动作与素材索引 |
| `Assets/Scripts/Art.cs` | manifest 解析、StageInfo 世界坐标、ClimbLine、Sprite 创建 |
| `Assets/Scripts/Backdrop.cs` | 地图主体和独立背景运行时渲染 |
| `Assets/Scripts/CameraRig.cs` | 全屏、双轴跟随和原地图 VR 约束 |
| `Assets/Scripts/GameLoop.cs` | ApplyTheme 设置边界/地形/绳梯/相机并 ResetTerrain |
| `Assets/Scripts/PlayerCtrl.cs` | 脚底位置、墙、跳跃、落地、爬绳/梯、动作状态 |
| `Assets/Scripts/TouchInput.cs` | TouchPad.Update 输入、MoveH/MoveV、单次跳跃、手指归属 |
| `Assets/Scripts/SpriteAnim.cs` | 基础循环、一次动作、按原 delay 播放和 speed=0 暂停 |
| `Assets/Resources/Stage/chapter1.json` | 当前章节主题与关卡数据 |
| `Assets/Scripts/Stage/StageModel.cs` | 章节/主题数据解析 |
| `Assets/Editor/MovementChecks.cs` | 真正调用 PlayerCtrl 的定向移动/攀爬检查 |
| `Assets/Editor/PlaySmokeTest.cs` | Unity Play 运行异常与离屏画面检查 |

## 地图与脚底坐标

WZ 地图坐标 y 向下，单位像素；Unity y 向上。当前 `Art.PPU=40`、`PlayerCtrl.GroundY=-1.4`：

```text
worldX = (mapX - ground.x) / PPU
worldY = GroundY + (ground.y - mapY) / PPU
feetY = terrainY + jumpY              # 原地图关闭原型 DepthOffset
```

`ground` 是坐标转换/出生参考点，不是全地图地板高度。不同平台有不同 terrainY；jumpY 在落向更低平台时可以为负。`ResetTerrain` 选择 x 列上最靠近 GroundY 的地面，因此新地图出生位置要核对对应 foothold，不能随意选择装饰物坐标。

manifest stages 每项：`map` 是地图 ID 字符串（保留前导零）；`px=[left,top,width,height]` 是栅格边界；`ground=[x,y]`、`walk=[minX,maxX]`、`view=[left,top,right,bottom]` 是地图像素；`footholds=[[x1,y1,x2,y2],...]`；`ladderRope=[{x,y1,y2,l,uf,page},...]`；`files` 指向主体图；`backgrounds` 保留原背景元数据及独立图片。

不要整体改 foothold 高度来匹配草叶最上缘。先确认 tile 的位置减 origin、主体图挂载位置、人物身体原点和 Sprite pivot 的完整转换。`bsc` 与 `enH0` 的 origin 不同，草皮可延伸到落脚线上方。

## 渲染与相机

当前逻辑高度 768px，orthographicSize=768/(2*PPU)；水平宽度随手机宽高比变化。Camera.rect=(0,0,1,1) 并清除全屏，避免两侧残留其他界面。保持角色与地图同 PPU、不独立拉伸。

VR 来自原 `info.VRLeft/Top/Right/Bottom`；缺失时当前导出器按 field-space 边界 -20/-60/+20/+100。相机边界按逻辑半宽/半高内缩；地图过小时该轴取中点。相机中心高出目标 50px，双轴跟随。768px 高度与指数平滑是当前移植选择，不是原端唯一模式。

独立 back 保留 x/y、origin、rx/ry、type、cx/cy、front、a 等，按相机计算视差、平铺、滚动。当前 type 4/6 横向滚动依据是 -5*rx px/s，纵向分支见 Backdrop。不可将背景整体烘焙后靠镜像、拉伸或填色补边。

当前主体 tile/obj 是静态合图，动画物件及按角色地图层排序遮挡尚未完整实现。原场景更大或遮挡更复杂时应扩展此边界，不声称合图已覆盖所有原端效果。

## 人物素材原点与动作

本地 CharacterRenderer 为方便合成将 navel 置于 (0,0)。wz_actions 在解析 UOL 后，从实际 Body/body placement 读取 map/navel=(dx,dy)，以 `(bbox.left-dx, bbox.top-dy, bbox.right-dx, bbox.bottom-dy)` 渲染，还原身体原点坐标。统一画布 pivot 为 `(-bbox.left,-bbox.top)`。原点不能来自 stand 图片最下方的不透明像素（可能是武器），也不能将所有动作固定成同一 navel 偏移。

Sprite manifest pivot 是左上原点像素；Art 转成 Unity 左下归一化 pivot。角色根节点保持 scale=1，子图统一 PPU；不用 targetHeight 重新缩放真实角色。装备、发型与动作共用正确轴心。

- 武器 info.stand/info.walk 选择 stand1/2、walk1/2，不能两套串接。
- 技能姿势依据 Skill IMG action 列表，保留重复动作段；没有原 action 的自定义技能采用项目现有回退，不猜测特殊分派。
- 原帧 delay 可能为负，时长取 abs，不用 max(40,delay) 截断负值。
- rope 和 ladder 使用原身体动作；当前每种各 2 帧、每帧 250ms，其他客户端应重新读取。
- SpriteAnim 的一次动作通常播完回 base；抓绳会显式切循环以打断残留攻击，speed=0 真正停帧。

## 平台、墙与物理

当前原 Map/Physics.img：walkSpeed=125px/s、jumpSpeed=555px/s、gravityAcc=2000px/s²、fallSpeed=670px/s。运行时除 PPU；理论跳高约77px。物理最多8ms子步、解析竖直位移，防止30/60/120Hz跳高差异。

横向跨过竖直 foothold 时，脚底仍在墙的竖直范围内就阻挡；脚底越过墙顶可通过。独立悬空横平台不是横向空气墙。走出支持段后自由下落；下降扫过非竖直 foothold 时，落到扫过的最高有效段。斜坡通过段内插值支持。不要把 GroundAt（邻近高度查询）当成空中自动吸附逻辑。

水平加速/摩擦、武器攻速分档、游泳、完整战斗动作状态机尚未逐项核实迁移。

## 绳梯与输入

WZ ladderRope：x 为绳线，y1/y2 为上下端，l 区分梯/绳，uf 控制上端能否退出；page 保留在导出数据但当前未用于地图层遮挡。Art.ClimbLine 转为世界 x/top/bottom/ladder/upperExit。

GameLoop 切主题要设置 `PlayerCtrl.Climbs` 和 Terrain；无原地图模式清空；ResetTerrain 清除攀爬状态。TouchInput.MoveV 上正下负（GUI y 需反号），保持持续输入，不复用旧五条深度层的单次换层事件。TouchPad.Update 采集、OnGUI 只绘制，按 fingerId 持有摇杆；另一手可跳跃，取消/失焦/释放清零水平和纵向输入。

当前控制行为：上下输入抓取有效高度内最近绳梯，吸附 x；上下爬、松手挂住、方向加跳跃脱绳、下端松脱；uf 上端连接附近真实 foothold，uf=0 不穿过顶部。攀爬期间关闭重力/普攻/技能，受击脱绳，重生/切图清状态。

当前抓取横向容差10px、顶部抓取余量5px、退出平台高度余量12px、脱绳冷却0.2–0.25s、攀爬速度跟随基础walkSpeed。这些是移植参数，未核实为原 EXE 常量；新地图验证发现问题时先看原数据和行为，避免无依据调大容差。

### 台阶墙脚接触（2026-10-28）

`PlayerCtrl.MoveHorizontal` 为步行和现有受击位移共用的水平墙/支持求解。墙命中截在竖直 foothold 的 x，不能停在旧 x：墙脚连接的下层平台可能只从墙线开始，旧 x 会导致沿墙下降时下层永远漏接。接触方向支持墙线上继续阻挡、停步后反向脱离；出生/复位清接触。原端证据、验证边界及全图回归入口见 beidou-client-reverse 的 input-and-physics.md「台阶直角墙脚漏地」。

绳梯方向跳已按EXE普通分支改为必须左右输入，竖速jumpSpeed×0.5、横速walkSpeed×1.3，切jump并设朝向；依据与特殊状态边界见 beidou-client-reverse/input-and-physics.md「绳梯左右＋跳跃」。
