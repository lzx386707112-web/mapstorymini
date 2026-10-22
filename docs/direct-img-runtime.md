# 原始 IMG 默认资源管线

分支：`codex/direct-img-runtime`。没有实验开关，地图、人物、怪物、技能、召唤物、伤害数字、鼠标、图标和游戏 UI 默认读取原始 IMG。

## 可行性

可行。本机原客户端的 `clien/Data` 已是独立 IMG，Unity 可以用纯 C# 读取属性树、解密字符串、解压和解码 Canvas；无需先导出 PNG 再导入 Unity。角色仍需按原始锚点组合部件，地图仍需实现图层、动画、视差与事件语义，所以原资源完整不代表整体游戏行为自动达到 100% 还原。

不需要为了读取 IMG 重写服务端或逐个复制 DLL。原 `BeiDou.exe`、`ResMan.dll` 是 Windows x86 程序，不能直接作为 macOS/Android Unity 库加载。Unity 承担渲染与输入，现有游戏规则继续复用；原始资源协议和客户端行为按功能实现。原端 Java 服务端与局部客户端补丁也不等于完整客户端源码。

## 实现

- `ImgFile.cs`：GMS AES、Property、Canvas、UOL、Vector、Convex、listWz 与 Deflate；支持 ARGB4444、BGRA8888、RGB565。未知扩展报错；Sound/Video/RawData 数据块暂时跳过，未新增原音频播放。
- `WzAssets.cs`：统一图片入口，解析 `_inlink`/`_outlink`，读取原 origin，缓存属性、纹理和 Sprite。旧资源名通过 `NativeAssetIndex.json` 指向原 Canvas；少量 UI 切片记录原 Canvas 的矩形区域，不保存像素。
- `WzCharacter.cs`：运行时组合纸娃娃，按 navel/neck/brow/hand 等锚点拼接，保留脚底坐标、武器姿势、动作跳转、z 顺序和原 delay。覆盖现有 30 套职业/发型配置。
- `NativeMapView.cs`：原始 foothold、ladderRope、portal、life、VR，直接渲染 tile/obj/back/NPC/传送门动画。章节模式与原地图模式共用此入口。
- `Art`、技能、召唤物、鼠标和 HUD 已接入统一入口，移动参数直接读取 `Map/Physics.img`。JSON 中仍保留现有玩法配置、资源别名及回归基线，不再作为人物/怪物帧像素来源。
- 已移出旧 PNG `.bytes` 资源，共 3281 张、约 69.45 MiB；本机备份位于 `Library/LegacyArtBeforeNativeIMG`，不参与构建。

## 资源准备与运行

```sh
rtk proxy python3 tools/package_native_assets.py \
  --data /Users/lizixian/Documents/mxd/BeiDou-Server/clien/Data \
  --wzpy /Users/lizixian/Documents/mxd/BeiDou-Server/tool/wz-python
```

此命令仅复制字节不变的原 IMG 依赖并生成索引，不导出角色帧、PNG 或烘焙地图。当前复制完整 Data：52,868 个文件（其中 52,759 个 IMG），共约 6,925.9 MiB，含 1675 条已有资源别名。原 IMG 中含大量尚未使用的内容，因此比旧的精选 PNG 包大；这些数字是资源文件大小，不是实测 APK 大小。新增资源优先使用 `wz://目录/文件.img/节点`，全量文件已包含在包中。

生成目录 `Assets/StreamingAssets/Wz/Data` 已忽略，不提交 Git；换机器必须重新执行准备命令。构建预检查会校验每个原 IMG 的存在性和 SHA-256，缺失或不匹配会阻止构建。原客户端文件不修改。开发时可用环境变量 `MAPLEDASH_IMG_ROOT` 指定另一套原 Data，默认直接使用项目内资源包，无需菜单启用。

桌面读取 StreamingAssets；Android 从独立部署的应用外部文件目录读取；WebGL 启动先下载、校验并暂存依赖文件。设备运行仍需验证，WebGL 首次下载仍为整个依赖包。

旧 `wz_assets.py`、`wz_actions.py`、`wz_stage.py` 保留作历史参考/基线工具，默认运行不再依赖其 PNG 导出，不应再按旧 README 步骤重新生成贴图。

## 已验证

- `verify_direct_img.py`：21 个 IMG、3080 个 Canvas 的 RGBA 哈希与原 Python 读取器一致，覆盖三种像素格式。
- `NativeAssetChecks.Run`：37 张地图的几何与引用纹理检查通过；另检查 4 张章节地图、1675 个别名、30 套人物与 17 类怪物共 1182 帧可读取。部分怪物帧数与旧导出表不同，以 IMG 实际帧数为准。
- `verify_native_characters.py`：五个职业的 158 帧动作，全部 RGBA 像素及脚底相对位置与源端 CharacterRenderer 一致。
- 技能资源检查 `SkillVfxChecks.RunAssets` 通过。
- 切换全部资源后的 Play 冒烟测试两次退出码均为 0：地图 40001，法师/弓箭手释放技能并击杀怪物；检查了人物、怪物、地图、技能窗和底部 UI 截图。现有 GameLoop 优先原地图模式，`-smokeLevel` 不会强制进入章节，故这两次都不作为章节 Play 验证。测试前后已导出/恢复 PlayerPrefs。日志有编辑器 Metal memoryless 提示，无资源缺失或游戏运行时异常。

复现入口：

```sh
rtk proxy python3 tools/verify_direct_img.py --data /path/to/Data --wzpy /path/to/wz-python
rtk proxy /Applications/Unity/Unity.app/Contents/MacOS/Unity -batchmode \
  -projectPath /Users/lizixian/MapleDash -executeMethod NativeAssetChecks.Run \
  -logFile /tmp/mapledash-native-assets.log
rtk proxy python3 tools/verify_native_characters.py --data /path/to/Data --wzpy /path/to/wz-python
```

同一项目仅运行一个 Unity 进程。截图/人物对照帧写入 `Library/NativeAssetChecks`。地图比较保留重复名称的传送门顺序；兼容旧 JSON float 对目标哨兵值的精度损失，直接读取仍保留精确整数。

## 当前边界

- 全量原始资源和地图目录已接入；玩法仍使用现有实现，没有重写服务器协议、脚本、所有职业与 DLL 行为。
- 地图主体跟随实际 `tools/wzmap.py` 排序；尚未实现角色 foothold 层级的完整遮挡。彩虹村房屋底座与挡土墙的排序问题仍需核实，不能宣称最终画面完全一致。
- 无 VR 地图仍以 foothold 包络估算相机边界；特殊对象移动、旋转、alpha、视频、天气和事件逻辑不完整。
- 缓存跨地图保留，尚未增加淘汰/内存预算、异步解析、图集批处理和视口裁剪。直接读取减少离线工作，不保证加载和 DrawCall 更低。
- 源端法师配置引用的 `Character/Shoes/01070030.img` 实际不存在，按旧组合器行为不绘制该部件；包中未用节点有两条悬空 outlink，索引记录，实际选用资源已检查。
- `MovementChecks.Run` 的“walked off ledge without falling”失败：现有 FollowGround 会把 x 端点相接但高度不同的平台直接接地。该函数未修改，本次不将此失败当作通过，也未声称已走通全部平台/绳梯路线。
- 综合 `FieldChecks.Run` 通过 2614 项、失败 3 项：两项现有底栏按钮布局断言，以及“跳跃必须为 Alt”的键位断言；对应 BottomBar/键位实现未修改，资源尺寸和可加载检查通过，未修改断言来掩盖失败。
- Android/IL2CPP、WebGL、手机输入和长时间多地图内存尚未验证。像素对照证明解码/组合一致，不证明原 EXE 的全部显示语义、判定或网络行为一致。


## 完整 Data 接入（更新）

资源准备命令现已改为复制整个源 Data，而非计算精选依赖子集。当前本机已复制 **52,868 个文件，约 6,925.9 MiB**，其中 **52,759 个 IMG、6,336 张地图**；包括 Character、Map、Mob、Npc、UI、Skill、Item、Sound 等目录及原始附属文件，文件内容不转换。

运行时地图目录从完整文件索引发现，名称读取 `String/Map.img`；具体地图进入时才解析，不在启动时解码全部地图。地图中的怪物直接读取对应 `Mob/*.img` 的数值和动画，不再受旧彩虹岛怪物导出表限制。原有掉落表/脚本/玩法逻辑并没有随资源复制自动扩充，特殊地图仍可能依赖尚未实现的行为。

Unity 菜单 **MapleDash → 地图目录（完整 Data）** 可按 ID/名称搜索全量地图，点击进入；Play 中可继续切换。无需为新地图运行 PNG 导出器。`FullDataChecks.Run` 提供全地图属性解析及多地区场景渲染检查；详细结果以本节后续记录为准。旧的 37 图/318 MiB 数据是本次扩展前的验证记录，不再是资源包当前规模。

完整目录仍不提交 Git，拉取后使用相同准备命令复制自己的 Data。桌面可以直接读取；全量包约 6.8 GiB，不会直接塞进 Android APK（采用下述独立目录部署）；WebGL 的分包/按需传输尚未实现。全资源可读取与所有地图事件/客户端行为可运行是不同验收项。


完整 Data 本次验证结果：

- 52,868 个源文件与项目副本逐文件 SHA-256 相同，零差异。
- 6,336 个不同地图 ID 的属性树全部解析通过，2,339 张含 `info/link`。Map/Map 下的 6,338 个 IMG 文件还包括 `AreaCode.img` 和一份重复地图 ID 文件，因此可进入的不同 ID 数为 6,336。
- 12 个跨地区地图的几何、原始贴图/动画、NPC 与引用怪物帧检查通过：100000000、101000000、102000000、103000000、104000000、200000000、220000000、230000000、240000000、100010100、220010000、240010000。后三图分别有 20、22、13 个原始刷怪点。
- 全量验证发现：`acc12.img` 保留了指向缺失 `_Canvas/acc12.img` 的 outlink，但其 Canvas 自身含有完整像素。读取器现只在外链文件不存在、且当前 Canvas 像素可成功解码时采用内嵌数据，并输出诊断；无法解码仍报错。不修改原 IMG，不生成替代画面。
- 进入地图前先准备新画面；资源错误时保留当前画面，不先清掉旧场景。

全地图属性解析不等于全地图画面验证；尚未遍历渲染全部 6,336 张地图，也未实现全世界服务器脚本、特殊场景机制及所有怪物 AI。完整 UI 行为还原也不在本次全量资源复制中自动完成。

- 扩展解码对照覆盖 `Map/Obj/acc12.img`：22 个 IMG、3,528 个 Canvas 与原 Python 解码器逐像素哈希相同，覆盖格式 1/2/513。
- `PlaySmokeTest.Run -smokeMap 100010100` 实际运行 12 秒，退出码 0：本套源数据地图名称为“可疑的小山”，生成 20 个怪物、6 个传送门，人物与怪物正常显示，技能触发，无资源缺失/游戏运行时异常；这次没有击杀，不声称战斗判定全通过。截图在 `Library/FullDataChecks/play`。测试前后已恢复 PlayerPrefs。


## Android APK 与完整 Data 分开部署

全量 Data 约 6.8 GiB；直接放进 APK 会先触发 Gradle AAR 的 4 GiB ZIP 上限，不能仅靠启用 ZIP64 解决 Android 安装格式约束。

- 项目中的 `Assets/StreamingAssets/Wz/Data` 保持完整。Android Gradle 生成回调只删除生成工程内的 Data 副本，避免将其归档进 AAR/APK；源 Data 不动。
- APK 保存代码、字体、资源索引和玩法配置。手机原始资源位于 `/sdcard/mapstory/Data`，运行时通过 `Application.persistentDataPath` 定位并直接读取 IMG。
- `./build_android.sh`、`./install_apk.sh` 在安装 APK 后自动调用 `tools/sync_android_data.py`。首次同步约 6.8 GiB，后续使用 `adb push --sync` 增量同步；仅传索引中的资源，不传 Unity `.meta`、备份或系统文件。
- 同步开始先移除完成标记，整套同步成功后才发布与 APK 资源索引匹配的 SHA-256 标记。缺少/不匹配时游戏显示资源未就绪提示，不进入缺资源场景。
- `--no-install` 仅构建 APK。之后使用 `./install_apk.sh`，或安装 APK 后执行 `rtk proxy python3 tools/sync_android_data.py` 同步，再启动游戏。卸载应用会删除此目录，需要重新同步。

这套方式用于 USB 真机开发验证；没有实现应用商店资源分发、在线下载或全量 Data 的手机内存/加载性能验收。

Android 本次验证：IL2CPP/ARM64 实际构建成功，错误 0；APK ZIP 完整性检查通过，确认不含 `assets/Wz/Data/`。Unity BuildReport 的 totalSize 仍将构建过程中暂存的资源计入，不能作为最终 APK 大小；以 `Builds/MapleDash.apk` 文件实际大小为准。尚未执行手机安装与资源同步。


## 项目外资源目录（当前结构）

完整资源已从 Unity Assets 移到 `/Users/lizixian/MapleDashResources/Data`，默认布局为项目同级 `../MapleDashResources`。本节覆盖上文历史路径；项目内只保留 `NativeAssetIndex.json` 等轻量配置。构建预检查和 USB 同步脚本均读取外部目录。

`tools/build_resource_pack.py` 生成支持 ZIP64 的 `MapleDashResources.zip`（清单 + `Data/`），适合一次性传输到手机。手机端默认从应用外部文件目录 `Wz/MapleDashResources.zip` 安装，也可输入应用有权读取的其他绝对路径。Android 的共享 Download 等目录可能受系统权限限制，因此建议使用应用自身目录；本版没有调用系统文件选择器。

资源安装在后台线程解压、逐文件 SHA-256 校验，清单必须匹配当前 APK。全部成功才提升临时目录为 Data 并发布完成标记；校验失败保留旧 Data。不自动删除 ZIP，确认正常后可自行删除压缩包回收空间。程序不会直接随机读取 ZIP 内 IMG，实际游戏读取解压后的目录。

迁移后验证：外部目录 52,844 个必需文件全部 SHA-256 通过；资源 ZIP 已生成（约 6.08 GiB），条目与清单完全匹配。ZIP 安装定向测试通过正常解压、损坏拒绝及旧目录保留；Unity 编辑器编译与 Android 专用加载器编译通过。尚未在手机上实测 ZIP64 解压，也未重建 APK；使用新安装界面前需重新构建。WebGL 构建脚本改为构建成功后从外部目录发布原资源到输出 StreamingAssets，尚未执行 WebGL 构建验证。


当前 Android 资源目录统一为 `/sdcard/mapstory`：可放 `MapleDashResources.zip`，也可放已展开的 `Data/`。游戏启动自动显示进度条；首次自动解压并逐文件校验，已安装资源启动时校验后进入游戏，无需点击安装按钮。Android 11 及以上首次需要在系统设置允许“所有文件访问”，Android 10 及以下申请存储权限。资源缺失、损坏或权限未授予时停留加载页并提示，不进入游戏。ZIP 保留，成功后可自行删除。此共享目录方案用于当前测试，应用商店发布时需调整为符合渠道要求的资源目录/分发方式。
