# ijl15 / 2ijl15 与手机模拟器适配

2026-10-27 静态核实；没有启动 Windows/Wine/手机模拟器，没有验证用户所说的最终运行效果。本次不修改运行时代码和源端文件。

## 二进制身份

路径相对 `BeiDou-Server`，SHA256：

| 文件 | SHA256 | ImageBase |
| --- | --- | --- |
| clien/ijl15.dll | 87c0d58d53b4294acf53f053191c67739d1fa1ab2e9532fd764ba6b251f85c7e | 0x10000000 |
| clien/2ijl15.dll | f991f9f77545b479758641dbf61c9bcd011008556ade775257b29e7282dc2558 | 0x60000000 |
| tool/client-runtime/ijl15_手机用.dll | f95eb56288a8aacb8cdfca63ec63eaeb400c290b3ffde392d23272fea8021da9 | 0x10000000 |
| tool/client-runtime/2ijl15_手机用.dll | 41d37bfb0a39017497e0aa373dc01f71ac9c5046c75f7cd269bee52112a5f3a1 | 0x60000000 |

EXE 身份同 [二进制记录](binary-and-tools.md)。手机用文件不是当前 clien 文件，不能复用全部 VA。

## 已确定：加载与职责

- EXE 静态导入 `ijl15.dll` 的 ijlFree/Read/Init/Write。
- 当前 ijl15 `0x10012C50..0x10012CCD` 用 LoadLibraryA 加载 `2ijl15.dll`，GetProcAddress 取得六个 ijl 函数，保存到 `0x10044354..0x10044368`；导出桩 `0x10012CD0..0x10012D20` 间接跳转。手机版加载器在 `0x10012C20`，导出桩在 `0x10012CA0..0x10012CF0`。
- 2ijl15 六个导出 VA：ErrorStr=0x6002B120、Free=0x600291F0、GetLibVersion=0x6002B110、Init=0x60028A60、Read=0x60029BE0、Write=0x6002A700。字符串 `0x600535A0` 为 Intel(R) JPEG Library，`0x60053C20` 为 1.51.12。
- 两版 2ijl15 的 .text/.rdata/.data/.data1/.rsrc 原始 section 内容逐字节一致，.reloc 不同；当前版另外静态导入 CIL.dll 的 APIDummy 三个符号。因此“手机用”名称不能证明其中增加了手机画面缩放实现，也不能把两文件说成完全相同或纯净官方原版。

## 已确定：配置分辨率与相机补丁

- 当前 ijl15 `0x1001539E..0x1001544C` 读取 general/width、height，缺省参数分别 0x500=1280、0x2D0=720；结果写入 W=`0x10036560`、H=`0x1003655C`。手机版对应读取片段 `0x1001552E..`，W=`0x10037560`、H=`0x1003755C`。当前 config.ini 显式为 800×600，所以缺省1280×720不能当成本机实际运行值。
- 当前 `0x10011DF5..0x10011E8F` 依次将整数 W/2、H/2、H/2 写入 EXE `0x641F62`、`0x641FC9`、`0x642090`，修正 VR 内缩。整数除法由 `cdq; sub eax,edx; sar eax,1` 实现，对正常正分辨率等价于向下取整；后面的 floor 不会恢复被截掉的半像素。
- 手机版同一三个补丁的调用点是 `0x10011DF5`、`0x10011E2A`、`0x10011E5F`，第一段从 `0x10011DC5` 开始。它也读取配置 W/H，不能解释成自动读取 Android 物理屏幕宽高。
- EXE 原始 VR 缺省/内缩/小地图锁中心语义和项目相机迁移见 [地图记录](map-and-skill-window.md)。该补丁改变可见地图范围，不是把整幅800×600画面拉伸成宽屏。

## 已确定：部分布局与窗口定位证据

- 当前 `0x10011974..0x100119C6` 计算并保存 `dy=trunc((H-600)/2)` 至 `0x1004434C`、`dx=trunc((W-800)/2)` 至 `0x10044350`。例如后继 `0x100119D0..0x10011A0A` 使用 `dy+318` 和 `dx-1` 准备补丁，目标 EXE `0x46942F`。这证明存在“保留原始坐标，加分辨率差值偏移”的适配，不是所有窗口统一乘比例。
- 这些偏移的每个消费者仍需分别确认窗口含义、完整调用链和 IMG 后才能迁移。不能将上述局部证据推广为整个 UI 的通用锚点规则。
- `0x10013220..0x10013278` 是 CreateWindowExA 包装：GetSystemMetrics(0/1) 后计算 x=trunc((桌面宽-窗口宽)/2)、y=trunc((桌面高-窗口高)/4)，保留尺寸参数并给扩展样式加 0x20000，再调用原 API。手机版对应 `0x100131F0..0x10013248`。这是 Windows 窗口定位，不是 Android 刘海/安全区适配。
- 存在 `UI/MapleEzorsiaV2wzfiles.img/Common/frame1024`、frame1366、frame1920 字符串，只能作为分辨率专用登录素材的入口；本次没有完整追踪选择分支、节点几何及素材存在性，不据此新增 UI。

## 对 Android 项目的迁移价值与边界

1. **可复用且已有部分实现**：逻辑视野驱动 VR 半屏内缩、地图过小时锁轴中心。`Assets/Scripts/CameraRig.cs` 已按相机尺寸计算边界；固定768逻辑高度是本项目策略，不是本 DLL 的固定要求。整数半屏与 Unity 浮点半屏仍有差别，不能称逐像素相同。
2. **最有价值的下一步**：逐窗还原 DLL 对原800×600坐标的偏移/锚点规则。地图当前768逻辑高，`UiKit.cs` DesignH=720、Scale=clamp(Screen.height/720,0.5,4)，`BottomBar.cs` 又使用 min(width/1281,height/600) 缩放。这些不同坐标域会影响地图、NPC 对话和窗口的相对观感；它们不自动构成 bug，但不能直接宣称等价于原 DLL 布局。
3. **可借鉴原则，不能直接替换数值**：共同定义逻辑视口，再让界面锚点和点击命中使用配套坐标换算；只有完整确认各窗口原端布局后才实现，不能仅把720改768就称迁移完成。项目 BottomBar 等已有未提交修改，本次未覆盖。
4. **无需迁移为适配机制**：2ijl15 的 JPEG 解码接口、Windows 内存补丁/导出代理、CreateWindowExA 窗口位置。Android 使用现有 Unity/IMG 解析管线，不直接加载32位 Windows x86 DLL。
5. **尚未证实**：自动适应任意手机比例、触屏多指/按键映射、DPI、安全区、最终缩放滤镜由这两个 DLL 实现。静态导入和所查函数没有提供这些结论；动态 GetProcAddress 或其他 DLL 仍可能参与，不能用“没看到”证明不存在。需要实际模拟器种类、使用中的 config 与模块列表、逻辑客户区/物理输出尺寸及输入映射配置作运行对照。源端读取 fixWine 等配置的完整分支也未在本次还原。

## 复现

在 MapleDash 根目录，用已装 pefile/capstone 的 Python（本机 `/tmp/mapledash-reverse-venv/bin/python`）：

```sh
rtk proxy /tmp/mapledash-reverse-venv/bin/python tools/inspect_beidou_client.py ../../Beidou-Server/clien/ijl15.dll --start 0x10012c50 --end 0x10012d30
rtk proxy /tmp/mapledash-reverse-venv/bin/python tools/inspect_beidou_client.py ../../Beidou-Server/clien/ijl15.dll --start 0x1001539e --end 0x10015451
rtk proxy /tmp/mapledash-reverse-venv/bin/python tools/inspect_beidou_client.py ../../Beidou-Server/clien/ijl15.dll --start 0x10011974 --end 0x10011a0f
rtk proxy /tmp/mapledash-reverse-venv/bin/python tools/inspect_beidou_client.py ../../Beidou-Server/clien/ijl15.dll --start 0x10011df5 --end 0x10011e94
rtk proxy /tmp/mapledash-reverse-venv/bin/python tools/inspect_beidou_client.py ../../Beidou-Server/tool/client-runtime/ijl15_手机用.dll --start 0x10011dc5 --end 0x10011e64
rtk proxy /tmp/mapledash-reverse-venv/bin/python tools/inspect_beidou_client.py ../../Beidou-Server/clien/ijl15.dll --start 0x10013220 --end 0x10013279
```

文件身份/导入导出/section对比用 pefile.PE：读取 OPTIONAL_HEADER.ImageBase、DIRECTORY_ENTRY_IMPORT/EXPORT；用 hashlib.sha256(Path.read_bytes()) 计算哈希，按 section.Name 配对比较 section.get_data()。本次只做静态分析与源代码对照，没有新增 Unity/Android 验收，不运行构建。

## 后续迁移：窗口视口约束与相机（2026-10-27）

用户授权完善项目后，新增以下迁移；上文“不修改运行时代码”仅指最初的 DLL 分析阶段。

- `CameraRig.ClampX/ClampY` 将逻辑半屏尺寸截为整数地图像素，再除 PPU；依据上文当前 DLL 的 `0x10011DF5..0x10011E8F`。保留项目768逻辑高度、按宽高比扩展横向视野及共同PPU。它不是把 DLL 配置默认值1280×720套到所有手机。
- `SnapTo` 在计算边界前更新相机视口，消除分辨率/targetTexture刚变时切图使用旧宽高比的问题；无跟随目标时的 `LateUpdate` 也应用已有VR边界，避免绕过小地图锁中心。
- `NativeUi.ClampWindowPosition` 只约束窗口整体位置，不改变 IMG 尺寸、origin、内部布局或既有倍率。Stat/Equip/Item/Skill原图几何见 [主面板复核](../../../../docs/ui-panel-restoration.md)，技能组合栏见 [拖动记录](ui-drag-and-drop.md) 及技能窗源码。
- 能力值窗补上非拖动时的位置约束；装备/背包窗在更新拖动坐标之后立即约束，修复当帧越界；技能窗按175−2+207=380px整体约束主窗+组合栏，并在拖动后同步主窗绘制/命中矩形，修复主窗旧坐标、组合栏新坐标混用。
- **归属边界**：窗口整体保持在手机可见区域是本项目移动适配约束，不声称已经逆向出原端CWnd的全部拖动边界；没有将局部dx/dy消费者推广成所有窗口的锚点规则，也没有把720/768不同坐标域直接判定为缺陷。源端布局常量、原IMG资源、现有底栏倍率均保留。

回归入口：`Assets/Editor/NativeViewportChecks.cs`，隔离内存Profile和停用GameLoop/PlayerCtrl，不登录、不保存用户数据。在真实OnGUI中直接注入Pointer字段，覆盖四面板越界拖动、旧位置恢复、技能窗同帧坐标更新后的关闭命中/组合栏位置；这是绘制/命中坐标回归，不是对真实触摸设备的回放。相机覆盖换视口后立即SnapTo、奇数逻辑高767的整数半屏、双轴小地图锁中心和无目标状态。

### 本次实际验证

- 隔离输出编译：`dotnet build tools/unity-compile-check/Check.csproj -p:BaseIntermediateOutputPath=/tmp/mapledash-viewport-compile/obj/ -p:OutputPath=/tmp/mapledash-viewport-compile/bin/ --nologo -v:q`，0 warning、0 error；未构建APK。
- Unity执行命令（外层按本机要求加`rtk proxy`）：`/Applications/Unity/Unity.app/Contents/MacOS/Unity -batchmode -projectPath "$PWD" -executeMethod NativeViewportChecks.Run -logFile /tmp/mapledash-native-viewport-checks.log`。该异步Play检查自行退出，不加`-quit`。
- 结果：**82项PASS，进程exit=0**；GameView实际输出1280×720、2400×1080、2560×1080、1024×768、800×600。四窗每个尺寸检查非拖动位置恢复、拖动当帧右下/左上约束；展开背包603px、展开装备208px和技能+宏380px均包含在检查内。
- 原IMG面板截图：`docs/hud-review/native-viewport-2400.png`（2400×1080）及`native-viewport-800.png`（800×600），已打开查看，窗体和组合栏完整可见。为隔离fixture画面，不是完整地图/手机游戏实机截图。
- 未验证：Android多指输入、厂商DPI、刘海安全区、真实手机动态窗口/旋转，以及本次未分析的DLL窗口锚点分支。既有Unity通过不替代这些边界。

随后用户指出上述视口截图中的新手技能图标为空：前次82项仅覆盖布局，遗漏了图标内容验收。已定位并修正前导零丢失的26条别名及生成脚本；`NativeViewportChecks`新增727张图标实际解码/非空像素检查，总计1536项PASS，并更新上述两张截图。根因、IMG身份和裁图见 [技能窗图标修复](../../../../docs/skill-window-restoration.md)。
