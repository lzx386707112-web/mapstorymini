# 纹理采样与移动端输入验证

核实快照：2026-10-26。原端身份见 [binary-and-tools.md](binary-and-tools.md)。
原端逻辑的确定程度与运行时迁移状态分开记录；“尚未迁移”不代表需要重新逆向已确认部分。

## 虚拟输入与渲染验证

已改：虚拟键移入固定 GUI.depth=−300 的 InputBridge.OnGUI，在游戏窗口之后绘制；原生光标/换图黑场保留专用层级。
鼠标指针编号−2不能用>=0判断占用；唯一空闲编号是−1。取消、输入门控关闭或失焦时释放按键/摇杆。
Unity batch `NativeKeyboardChecks.Run`：214通过，0失败；包含鼠标摇杆与按钮的实际注入/释放回归。
dotnet隔离输出编译：0警告、0错误。尚未做 Android 多指/重叠窗口实测。

`verify_native_characters.py`：158帧原始RGBA和脚底相对位置与原合成器完全一致（使用现有Unity快照）。
这排除了所测帧的像素/轴心合成误差，不证明最终手机显示无锯齿。
当前相机768逻辑像素，屏幕高度通常与它不同；人物 world scale=1 不代表最终显示像素倍率=1。

继续核实了 `Gr2D_DX8.dll`，SHA256 `ab1c942229af33da04d913ed1157ab322bff0812f7368bcf745d64b98698d34d`，ImageBase `0x50400000`。
Direct3D8 的 `SetTextureStageState` vtable offset 为 `0xFC`（由本机 mingw d3d8.h 核对）；MAGFILTER=16、MINFILTER=17，POINT=1、LINEAR=2。
`0x5040E2BF` 允许图层明确指定 filter；默认−1分支 `0x5040E2FD` 比较源/目标宽高并检查变换：
满足原尺寸/无变换条件时 `0x5040E346`/`0x5040E356` 设置POINT；其余 `0x5040E377`/`0x5040E387` 设置LINEAR。

据此在 `SpriteAnim.LateUpdate` 对人物按最终正交相机像素倍率选择采样：1:1保留Point，缩放时Bilinear，分辨率改变时重新选择。
没有修改IMG像素、原点、人物尺寸或全局UI贴图采样。当前适配只覆盖项目中无旋转的正交人物图层，不等于整个原端Gr2D所有变换分支。
`NativeCharacterSamplingChecks.Run` 已验证同一缓存帧的1:1→1.40625倍→1:1采样切换，保存并检查了两张Unity渲染图：
`Library/UiInputAnalysis/sampling-192.png`、`sampling-270.png`。人物有正常显示，没有发现新增透明黑框；Android实际画面仍待实测。

## 悬空 UOL：一条坏链接不能打断整帧合成（2026-10-26 核实并修复）

- 现象：商城里点/双击商品「电焊工面具」试穿后，预览区整块空白（修复前截图）；在未加保护的调用路径上，这条异常会打断整个 OnGUI，表现为界面数据全没。
- 数据事实（本版 `clien/Data`，wzpy 逐节点核对）：`Character/Cap/01002470.img` 每个动作帧下都有两个 UOL —
  `default → ../../default/default`（存在）和 `defalutAc → ../../default/defaultAc`（**目标节点不存在，节点名本身也是拼错的**）。
  该文件共 89 条悬空 UOL。`Character/Cap` 3353 个 IMG 里 44 个有此问题，另有 4 个是 UOL 成环
  （`01000098`、`01005080`、`01005299`、`01005301`，wzpy 直接 RecursionError）；`Character` 全量扫描仍在累计（已见 190+ 件）。
- 原端语义：UOL 解析失败按「没有这个节点」返回 null，调用方跳过（`CData::GetItem` 返回 null 的同一口径），不会中断绘制。
- 端口缺陷：`ImgNode.Resolve()` 对悬空 UOL 抛 `InvalidDataException("Missing UOL target: …")`；
  `WzCharacter.Placements` 逐帧对每个 canvas 子节点调 `Resolve()`，于是一个拼错的 UOL 让整个角色合成失败。
- 修复：新增不抛异常的 `ImgNode.TryResolve()`（悬空/成环返回 null），`Resolve()` 改为在其之上抛错；
  `Get()` 路径中间遇到 UOL 也走 `TryResolve`；`WzCharacter.Placements` 的路径节点与子节点、`WzAssets.Frames` 改用 `TryResolve`。
  `WzAssets.Canvas` 保留抛错语义（显式请求一个不存在的画布仍应报错）。
- 复现/验证：`Assets/Editor/CashItemDiagnostics.cs`（合成 + 服务端取出口径，含 7 个悬空/成环帽子批量）与
  `Assets/Editor/CashPreviewProbe.cs`（真实 OnGUI 里点商品格截图）。
  Unity batch：修复前 `Missing UOL target: defalutAc` @ `WzCharacter.cs:414`，修复后 `sprite=48x69`、7 个帽子全部合成成功，
  预览截图 `docs/hud-review/cash-preview-1002470.png` 正常戴帽；服务端 Buy→Withdraw→Read 不受影响。
  两个诊断脚本是临时工具，验证完已删除，需要时按本节重建。
- 边界：只处理「UOL 解析不到」这一类；IMG 解析本身失败、Canvas 像素损坏仍按原样抛错。


## 玩家/普通怪物重叠时换帧遮挡闪烁（2026-10-28）

### 原端确定部分

BeiDou.exe SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`、ImageBase `0x400000`。

- 玩家 `0x92FD16..0x92FD59`：取 user+0x11A4 运动对象的实际this（减0xC），令 `K=10*(movement[0x130]*3000-movement[0x134])-0x3FFF8ADA`。根据 movement+0x18 是否非0选择末位+7/+2，通过 avatar=user+0x88 的 `0x452195` 写纸娃娃 layer z。
- `0x452195..0x4521D1` 对 avatar+0x10C8 层调用 COM vtable+0xB4，参数是上面算出的完整 z。
- 普通怪 `0x664E35..0x664EAA`：取 mob+0x118 运动对象this（减0xC）；普通分支 `0x664E5B..0x664E70` 计算 `10*(movement[0x130]*3000-movement[0x134])-0x3FFF8AD9 = K+1`，通过主图层 mob+0x4C0 的同一 vtable+0xB4 设置z。movement+0x248==3另走固定 `0xC0041F14`；这不是所有怪都可套普通公式的证明。
- 结论：相同运动层级基准下，普通怪与人物z不相等（怪K+1，人物K+2或K+7），不应交给每帧IMG/纹理打破排序平局。普通人物应在普通怪物前。
- 此次没有进一步给 movement+0x130/+0x134 全部状态赋予语义，完整地图遮挡/跳跃跨层仍属于已记载的迁移缺口。

复现：

```sh
rtk proxy python3 tools/inspect_beidou_client.py ../../Beidou-Server/clien/BeiDou.exe --start 0x92FD16 --end 0x92FD59
rtk proxy python3 tools/inspect_beidou_client.py ../../Beidou-Server/clien/BeiDou.exe --start 0x664E35 --end 0x664EAA
rtk proxy python3 tools/inspect_beidou_client.py ../../Beidou-Server/clien/BeiDou.exe --start 0x452195 --end 0x4521D1
```

### 移植缺陷与最小修复

原代码 PlayerCtrl.SortingOrder 与 EnemyUnit 初始化/SyncVisual 都用 `300-lane*12`。相同layer、order、z的透明精灵无明确角色/怪物先后，换动画贴图时可受到纹理/材质批次的平局顺序影响。初查时“Sprite中心随换帧变化”只是线索，未以它作为确定根因；没有通过改pivot或新增高度排序修复。

EnemyUnit 统一用 `BodySortingOrder=299-lane*12`，人物保持既有 `300-lane*12`，让普通同lane主图层按已核实先后分开一档。Unity值是既有管线接入映射，不等于直接搬原EXE巨大z数值。没有改IMG、动画时延、透明度/受击灰白调制、血条/特效、NPC/假人排序或新增旧章节分支。

### 定向验证

持久入口 `ActorOverlapChecks.Run`：真实原IMG人物stand/alert/walk各3帧，与蜗牛stand/walk/hit交叉、lane0/2/4，共81组。每组分别渲染人物、怪物、重叠三张，比较双方不透明交叠像素，要求合成保持人物像素；检查order严格有序。使用有图形设备的Unity batchmode（不可加nographics），没有修改资源或启动游戏存档。实际执行结果见本次交付；该检查不代表全部特殊怪、完整地图层、Android GPU实机已验收。

本轮Unity有图形batchmode实际PASS：81组、35835个不透明重叠像素全部保持角色在前；同一测试临时恢复旧equal-order后36组出现与正确前景不同的像素，45组没有，证实换帧会改变旧平局结果。退出码0；日志 `/tmp/mapledash-overlap.log` 仅作本轮记录，持久复现使用上述Editor入口。`git diff --check`通过。


## 2026-10-29：NPC 对话期间虚拟按钮可见性（移动适配修复）

本节是移动输入覆盖层代码事实，不是原 EXE UI 结论；没有改原 NPC 对话素材、节点、布局或时序。
VirtualControls.Draw 原先在 NpcSession.Blocking 时将每个按钮的配置透明度额外乘0.22，并通过 dialogFade 隐藏文字；InputBridge.Update 同时允许 dialogInput 注入，因此出现按钮近乎不可见但仍能按。现取消该额外淡化及标签隐藏，继续使用用户配置透明度、既有 GUI.depth=-300 和键位设置窗口0.42淡化。输入门控不变：对话键仍可推进，移动/攻击仍受 NPC 会话限制。
验证：git diff --check通过，静态核对 dialogFade 已无引用；未进行 Unity 绘制或 Android 实机验证。此修改仅处理 NPC 会话分支，不宣称其他弹窗已逐一实测。
