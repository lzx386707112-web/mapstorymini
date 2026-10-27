# 原客户端地图显示处理核对

此处以本地原端的代码、WZ 数据和二进制为依据，截图仅用来验收。
原端完整 CMapLoadable / resolution.cpp 源码不在 mxd 目录；兼容源码给出了函数地址，
以下相机与分辨率结论进一步通过本地实际加载的 EXE / DLL 反汇编核对。

## 已核实的依据

- `clien/config.ini`：本地保存值为 800×600；注释列出 1280×720、1366×768、1600×900、
  1920×1080 等模式。因此不能把截图估算的 1366×768 宣称为该客户端唯一分辨率。
  本项目保持 768 像素的逻辑高度，手机全屏按实际宽高比扩展横向视野，地图与人物共用像素单位。
- `tool/client-debug/weather-compat/src/wvs/field.h`：CMapLoadable::m_rcViewRange 位于 +0xF0，
  RestoreViewRange=0x00641EF1，MakeBack=0x0063CD4E。
- `BeiDou.exe` 0x00641EF1..0x0064211D：读取地图 VR 四边；默认值来自 field-space
  边界，依次 -20、-60、+20、+100；左右内缩 400，上下内缩 300；
  若内缩后左右/上下反转，将该轴的两边都设为平均值。
- `ijl15.dll` 0x10011DF5..0x10011E8F：以运行时宽/高的一半分别改写
  0x00641F62、0x00641FC9、0x00642090，证明边界随逻辑分辨率变化，不能锁死 800×600。
- `BeiDou.exe` 0x00437B32..0x00437B39：相机向量 RelMove 的参数 x=0、y=-50。
  当前实现改为双轴跟随、中心高出目标 50 个地图像素，移除自行添加的向前偏移。
- `weather-compat/src/weathersplash.cpp` ReadCamera：从 IWzGr2D.center 读世界相机坐标，
  注释指出动画更新按 CField::m_rcViewRange 裁剪。
- `weather-compat/src/weatherfx.cpp` 365..400：type=4 的 back 独立自动滚动，
  按 20000/abs(rx) ms 移动 100px，即 -5*rx px/s；MakeGrid 以 Gr2D.center 为锚平铺。
  EXE 0x0063DC4A..0x0063E1C2 可核对 type 4/6 的横向滚动和 type 5/7 的纵向滚动分支。
- WZ back 的 x/y、origin、rx/ry、cx/cy、type、alpha、front 均直接导出。
  背景保持独立图层，由相机坐标驱动，不再静态烘焙、镜像或涂抹填补。
- WZ tile/obj 使用原始位置和各自 canvas.origin；foothold 导出整图范围。
  VR 不再由“最长平地”的截图宽度替代，且原地图下方不再人为重复填土。

## 验证范围与仍有的限制

本次覆盖地图坐标、逻辑视野、双轴相机范围、背景平铺/视差/滚动。
相机插值暂保留项目现有指数平滑，未确认与原端 Shape2D 的插值曲线完全相同。
地图 tile/obj 仍烘焙为静态主体图，动画摆件与按角色所在地图层的遮挡尚未迁移。
因此这些修改不能等同于整个地图渲染器已经完全 1:1。

## 背景 back 的坐标约定（2026-10-21 补充核对）

`back` 的 `x/y` 不是绝对地图坐标，而是**相对屏幕中心（相机）的偏移**。证据是同图里成对出现的远景层宽度与 `x` 差值相等，例如：

- `1010004` 的 `grassySoil/6`（1024×294，x=-135）与 `grassySoil/5`（1024×257，x=889），两者 `x` 差 1024 = 图宽，正好并排平铺成一条远景土带；
- `40001` 的 `midForest/3`（1025×503，x=924）与 `midForest/4`（1025×503，x=-100），`x` 差同样等于图宽。

因此运行时位置取 `worldX = (x - origin.x) + 相机地图像素X × (1 + rx/100)`，`worldY` 同理用 `ry`：

- `rx = 0` 时系数为 1，背景随地图 1:1 移动（贴地远景）；
- `rx = -100` 时系数为 0，背景钉在屏幕上；
- `rx` 越大层越“近”。`type` 4/5/6/7 额外按 `-5×rx px/s` 自动滚动。

这条约定与 `tools/wzmap.py` 静态烘焙里的 `my = ground_y + (y - origin.y) - cam_above` 是同一条规则的静态特例（烘焙时相机固定，视差项为常数）。

因此实机里偶尔能在屏幕边缘看到「地形带断开、露出蓝天」的缝隙：那是原图远景层本身的覆盖范围（`type 0` 不重复的层有边），不是贴图错位。已用同一份 WZ 数据烘焙的参考图逐块比对确认——`40001`、`1010004` 的平台、树木、土带位置与实机截图一一对应。

## 对照文件哈希

- BeiDou.exe: `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`- ijl15.dll: `87c0d58d53b4294acf53f053191c67739d1fa1ab2e9532fd764ba6b251f85c7e`


## 全屏、输入与跳跃修正（2026-10-21）

- 全屏 Camera.rect=(0,0,1,1)，逻辑高度仍为 768px。每帧清除整个画面，
  避免之前窄视口两侧保留大厅 GUI / 角色等旧内容。手机显示宽度决定逻辑地图宽度。
- TouchPad.Update 采集输入，OnGUI 只绘制；圆形使用独立透明贴图，不再由重叠矩形拼接。
  按 fingerId 持有摇杆，另一只手可跳跃，失焦/暂停/手指取消时清除输入。
  原地图关闭原型的五条深度层，纵向拖动不会再把角色推离 foothold。
- 直接读取原 Map/Physics.img：walkSpeed=125、jumpSpeed=555、gravityAcc=2000、fallSpeed=670，
  除以 40 PPU 转为世界单位。数值保存在 Resources/Physics.json，wz_actions.py 可重新导出。
  理论基础跳高约 77px，因此 60px 台阶属于可跳高度。
- 导出保留竖直 foothold 作为台阶墙。脚底越过墙顶即可横向穿过；空中独立积分，
  下降扫过平台时落地，走出平台转为下落。物理以最多 8ms 的步长积分，覆盖 30/60/120Hz。
- 原武器 info.walk / info.stand 决定 walk1/2、stand1/2，避免把两套动作串接成一个循环。
  各动作使用原身体帧和 delay，空中切 jump；移除普攻和施法入口随意设置的 1.3～1.5 倍播放速度。
  攻击动作未播完时不被下一轮自动普攻截断。导出脚本：tools/wz_actions.py。

范围：基础姿势、帧顺序、帧延时和基础跳跃参数来自原端；武器攻速分档、
攀爬/游泳、水平加速与摩擦尚未完整迁移，不能据此声称整套角色控制器已完全 1:1。

技能角色姿势也按原 Skill IMG 的 action 列表导出：突进 rush2、虎咆哮/防御崩坏 alert3、
剑影分身 brandish1 等；包含原 action 列表里的重复段，治疗/增益也进入对应角色姿势。
原帧 delay 可能为负值，播放时取绝对时长，不能用 max(40,delay) 把 -300ms 截成 40ms。
缺少 action 的自定义技能仍退回职业普攻姿势，未凭空推测原端特殊分派。


### Foothold anchors and ladderRope (2026-10-21)

Character export now restores each resolved Body/body canvas's `map/navel`
translation after the local CharacterRenderer aligns navel to zero. Sprite pivot
is the native body origin, shared with foothold movement coordinates, rather than
the opaque bounding box of stand1 (which also contains equipment). UOL body
references use the renderer's resolved placements. No global floor offset is added;
original tile origins and foothold coordinates remain unchanged.

All four maps export `ladderRope` x/y1/y2/l/uf/page (16 entries). Runtime uses
continuous up/down input, 10px horizontal grab tolerance, original rope/ladder
frames and delays, stationary animation pause, directional jump-off, and `uf`
controlled top exits onto a nearby actual foothold. Climb speed currently follows
base walk speed; grab tolerance and detach cooldown are port behavior parameters,
not verified original executable constants. Attacks/skills are disabled while
hanging; damage detaches the character. Map switching resets climbing state.

MovementChecks exercises every nonvertical foothold landing across all four maps,
each native rope/ladder and upper exit, hanging, descending, jumping off, existing
30/60/120Hz steps/ledge behavior and touch release. These verify collision support,
not an end-to-end route to every platform. Native accelerated movement and every
combat action transition are not yet a verified byte-for-byte client recreation.

## 每层的绘制顺序：先摆件（z），后瓦片（zM）（2026-10-21 修正）

原来把瓦片和摆件放进同一个 (layer, z) 序列里排序，瓦片的 z 取 `zM×100`。结果是**同层摆件永远压在地形前面**：`001000000`（彩虹村）里那些「蘑菇屋」摆件的贴图底边是齐平的硬切边（作者按「下半截埋进土里」画的），就被画到了挡土墙中间，看起来像房子插进了地板。

证据有两条，方向相反，只能靠「分成两组、组间不交叉」同时满足：

- `Obj/houseGS.img/house10/basic/0` = 189×265，底边在 `y-oy` 处是平的。它挂在 layer 1、`z=2`；同层挡土墙是 `Tile/woodMarble` 的 `bsc/enH0`、`zM=0`。原版里这截是埋在土里的，所以地形必须压住摆件。
- `000040001`（蜗牛打猎场Ⅱ）的栅栏是 `Obj/guide/midForest/wood/4`、layer 1、`z=9`，它必须压在 `zM=0` 的地面上；`001000000` 的 `door/grassySoil/window`（`z=4`）也必须压在墙上。若把整层瓦片无差别压在摆件上，这些会整块消失（实测栅栏与窗户直接不见）。`FieldChecks` 与实机截图都验证过这一点。

所以 `tools/wzmap.py` 现在的规则是：

```
items = [ (layer, 0, z,  'obj') ] + [ (layer, 1, zM, 'tile') ]   # 层 → 组 → 组内 z
```

- 层与层之间仍然是 layer 小在前（远景摆件所在层先画）；
- 同一层内：摆件按 `z` 升序，瓦片按 `zM` 升序，**组之间不比较** —— 摆件＝摆进地形里的布景，地形体量（`zM=0` 的草地/挡土墙/路面）盖住它的底座；前景草叶（全量统计里 `zM` 最大到 84）再盖住所有东西；
- `zM` 原值范围 0..84、摆件 `z` 范围 0..15（62 张 Map0 地图统计，没有负值），这个量级差也说明两组本来就不是同一个 z 空间。

重烘焙后 `001000000` 蘑菇屋的茎在草皮处收口、`000040001` 的栅栏与路牌仍在墙前；`FieldChecks` 1455 项通过 0 失败。实机对比图：`docs/field-shots/1000000-蘑菇屋-层级修正后.png`、`/tmp/smoke_shots/shot00.png`（已归档为 `1000000-传送门与层级.png`）。

## 传送门「光洞」是独立资源

`Map0/<id>.img/portal` 只有 `pn/tn/pt/tm/x/y/script`，没有贴图。`pt=2`（VISIBLE）的门由客户端用 `MapHelper.img/portal/game/pv` 的 8 帧动画绘制，锚点是每帧自己的 `origin`。因此地图主体烘焙图里永远没有门，运行时必须单独画（`FieldView.Portal`，`sortingOrder=200`）。帧上无 `delay`，取 120ms 并登记为适配项。


## 2026-10-23：原始地图落脚位置修复

- `FieldDirector.ApplyGeometry` 保留竖直 foothold 传给 PlayerCtrl；地面查询自行排除竖直段，横向移动仍使用它们挡墙。
- 地面行走重新使用 `FollowGround` 跟随坡面。接续段必须 x/y 端点都重合，不能因为远下方有平台就悬空保持 grounded，也不能自动跳上有高度差的台阶。
- 静态 NPC 缺少原客户端落地控制器，现在按 life/fh 指定段在 life/x 处插值，显示、交互及任务标记共用落地坐标；无有效支撑段保留原始 life/y，不猜测最近平台。此为静态落地适配，不等同于完整原客户端 NPC 移动模拟。
- 原始 10000：希娜 life/y=293，cy=305，fh51=(96,305)→(174,305)，故应落在305（差12px）；莎丽 y=cy=125 不变。20000 罗杰 y=58，cy=65，fh7 支撑高度65（差7px）。地图/素材 origin/角色 Body 原点与 PPU 不作整体偏移。

## tile/obj 的 z 键与缺素材容忍（2026-10-26 EXE 复核）

文件 SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`。两处 z 公式已在本地 EXE 逐条指令核对，与 `NativeMapView.Show()` 现行实现**逐字节一致**：

- `MakeObj 0x0063C27B..0x0063C289`：`imul eax,eax,0x7530`(30000×layer) → `mov ecx,[ebp+0x1c]`(obj z) → `lea eax,[eax+ecx-0x3FFFF830]`。即 `Z = obj.z + 30000×layer - 0x3FFFF830`，层优先、z 次之。
- `RestoreTile 0x0063A911..0x0063A91F`：`imul ecx,ecx,0xBB8`(3000×layer) → `sub ecx,esi`(zM) → `lea ecx,[ecx+ecx*4]`(×5) → `lea edi,[eax+ecx*2-0x3FFFB1EA]`。即 `Z = tile.canvas.z + 10×(3000×layer − zM) − 0x3FFFB1EA`。
- 排序稳定性：同 z 时原端按 ZMap 插入顺序（即 WZ 节点顺序）绘制，运行时的 `Sequence`（节点序）等价。910000000 实测 326 个 obj 按 `(layer, z)` 升序无逆层。

### 缺素材不中断整图（2026-10-26 核实并修复）

`MakeObj 0x0063C25D..0x0063C269`：解析得到的节点指针 `cmp eax,edi`(edi=0) → `je 0x63C275`，**该分支只跳过一次引用计数调用（0x63C26B），随后照常计算 z 并创建对象**，函数没有提前 return/抛错路径。`RestoreTile` 同理。也就是说：**素材缺失只少那一件摆件，不会让整张地图加载失败**。

迁移侧此前 `NativeMapView.Show()` 对缺 tile/obj/back/NPC 素材直接 `throw`，`FieldView` 捕获后销毁整个地图对象 → **整图白屏**。已改为「跳过该条目 + 去重告警 + 计入 `MissingArt`」，与原端行为等价（不生成空渲染器）。

**受影响地图实测（离线全量扫描 `Map/Map/Map*/*.img`，共 6337 张）**：69 张地图存在 12194 个解析不到的节点（OBJ 6713 / TILE 5208 / BACK 257 / NPC 16）。修复前这些地图全部整图失败；修复后按原端只缺那几件。

| 类别 | 例子 | 根因 |
| --- | --- | --- |
| TILE | 702300000..702300010 引用 `Map/Tile/eventCN1.img` | **源端 `BeiDou-Server/clien/Data` 也没有该文件**（本版 WZ 不含，非打包遗漏） |
| BACK | 211080000..211080600 引用 `blizzardCastle/back/26..28` | 文件存在但节点缺（版本差异） |
| OBJ | 541020800 引用 `guide/citySG/Jungle/house/15` | 同上 |
| NPC | 010006000..010006090 引用 `Npc/0000700..0000707`、`0800015`；262000000 引用 `2182xxx/2183xxx` | **源端 Data 也没有这些 NPC img** |

受影响最多的地图：702300004/702300007（各702条）、709000101..709000601（各438）、749020000..749020920、749050200。
另：`NativeAssetChecks.Run` 报的 `skillicon/1003 source` 失败是 `NativeAssetIndex.json` 里别名 `Skill/000.img/skill/1003/icon` 过期（该 img 内是 8 位 ID `0001003`），与地图渲染无关，基线（未改代码）同样失败。

### 910000000（自由市场）逐节点核对结果（2026-10-26）

以 Unity 批处理探针（`Assets/Editor/MapDrawProbe.cs`，`PROBE_MAPS`/`PROBE_OUT` 环境变量）实例化 `Show(910000000)` 导出全部 SpriteRenderer，与 wzpy 离线解析逐条比对：

- 数据层：0 tile、326 obj（全部可解析）、31 back（全部可解析）、40 portal、9 life（8 个可见 NPC + 1 个 `hide=1`），`Npc/*` 与 `stand` 帧齐全。
- 运行层：`draws=365`（326 obj + 31 back + 8 NPC + 0 portal），`missingArt=0`，无异常。
- 326 个 obj 的 **位置/尺寸/origin 与 WZ 逐条完全一致（0 条差异）**，排序无逆层。
- `portal` 的 `pt` 分布为 `{0:3, 1:32, 3:4, 7:1}`，**没有 pt=2**，而原端/运行时只对 `pt==2` 画 `MapHelper.img/portal/game/pv`，所以自由市场本来就不画传送门光效——门内蓝色光圈是 `Obj/signboard.img/market/potal0|potal1` 摆件，不是 portal。
- 门牌数字是 `Obj/signboard.img/market/number/<n>`：底排 1..6（y≈-70）、上排 7..12（y≈-352）、再上 13..17、18..22。
- 截图里门 1 位置的「卡车」就是 NPC `9071003`（stand 133×101 货车造型，x=1450），即 NPC 确有渲染；其余可见 NPC 9900008/9900009/9063168 也在其授权位置。
- 小地图 `miniMap/canvas` 246×143 透明底，`width=3937/height=2289/centerX=1321/centerY=2044/mag=4`；246<280、143<150，视口不缩放、滚动恒为 0，整张画布完整显示，8 个 NPC 绿点 + 玩家黄点齐全。

结论：**自由市场这张图的野外与小地图渲染没有缺口**；用户看到的「缺元素」来自 69 张缺素材地图的同类问题在其它图上的表现，以及小地图（已撤销的）不透明白底。
