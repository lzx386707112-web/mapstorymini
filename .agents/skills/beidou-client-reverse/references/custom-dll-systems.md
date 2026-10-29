# 自定义 DLL：技能、MCV、飘字、伤害皮肤、天气

2026-10-27。**本记录不是全系统迁移完成声明**。源码事实、当前二进制证据、Android 实现和验证范围分开列出。用户已明确授权：先在源端 tool 查找，缺少匹配源码时直接反编译实际 DLL，不因缺源码停止或反复索要源码。

## 身份与复现入口

源目录 `BeiDou-Server/clien`。`docs/custom-dll-inventory.json` 保存全部 31 个 DLL 的 SHA256、大小、ImageBase 和导出；不要把所有 DLL 都当成自定义模块。依赖 WZ/音频/渲染的原有 DLL 也在此清单中。

| 文件 | SHA256 | ImageBase |
| --- | --- | --- |
| BeiDou.exe | `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2` | `0x400000` |
| BeiDouSkillCompatCore.dll | `3882737456d7c95795b2afe63ad91703cd70ef299c6b83fefe5f6f70764b466f` | `0x67280000` |
| BeiDouDamageSkinCompat.dll | `dfaa068aa7ffa8d34f81cae18c063f1af73dd07aa640127bf325ca606b212d73` | `0x6FE80000` |

MapleDash 根目录复现：

```sh
rtk proxy /tmp/mapledash-reverse-venv/bin/python tools/inspect_custom_modules.py
rtk proxy python3 tools/export_damage_skin_catalog.py --validate-glyphs
rtk proxy python3 tools/mcv-check/verify.py
```

`inspect_custom_modules.py` 只对已核实 SHA 的核心套用视频表地址；版本变化直接拒绝，必须重新确认消费者。

## 已核实：当前核心的技能视频表与旧源码不同

- 当前核心 `0x67286027` 为查表播放入口。`0x6728602D` 载入 `0x6728A540`；每项 12 字节，`+0` 技能 ID、`+4` 文件路径指针、`+8` 日志指针。
- `0x67286038..0x67286048` 将输入技能 ID 与表项 `+0` 比较，每次加 `0xC`，在 `0x6728A714` 结束，共 **39 项**。命中后 `0x6728604F` 调 `0x67282597` 检查准备状态，失败不播；`0x6728605F..0x67286065` 取路径并通过 `0x672915C8` 调播放器。
- `Assets/Resources/NativeSkillVideos.json` 是从上述当前二进制导出的表，而非从旧源码手抄。所有指向的源端 MCV 都存在。
- 明确差异：现有 `DawnWarriorSkillCompat.cpp` 表里有 `4221036/4221039`，当前核心表对应的是 **`4221023/4221027`**；当前还有 `5121015` 等差异。不能用旧源码替代当前 DLL。
- `tool/client-debug/dawn-warrior-skill-compat/README.md` 明确说明没有与统一核心匹配的完整源码，v70 HP/MP 使用 wrapper 加载保留的核心。源码可作定位线索；技能动作分类、隐藏 replay、目标数、时间轴、HP/MP 上限等尚不能据旧源码宣称当前核心已全部还原。

复现：

```sh
rtk proxy /tmp/mapledash-reverse-venv/bin/python tools/inspect_beidou_client.py ../../Beidou-Server/clien/BeiDouSkillCompatCore.dll --start 0x67286027 --end 0x672860A8
```

**迁移状态（续作更新）**：39项已接到 `SkillSystem.BeginCast` 成功校验、扣MP后的 `NativeSkillVideo.PlaySkill`，通过原始MCV双流解码播放；视频表现接通不代表伤害时间轴完成。`SkillSystem` 仍存在按别名走的部分技能和通用分支，不能视为全职业 V/VI 已迁移。后续必须连同服务端 `CloseRangeDamageHandler` / `MagicDamageHandler` / `RangedAttackHandler` 及各 `*SkillCompat` 的 tick、取消条件、地图/施法者有效性一起迁移。

## 已核实：伤害皮肤 DLL

- 导出 `BDS_SetSkin=0x6FE8179E`：负 ID 归零，原子写 pending ID `0x6FE84018`，并清失败抑制值 `0x6FE82000`。它不是新的飘字绘制器。
- 初始化 `0x6FE810E1..0x6FE811F3` 检查 EXE 的 ImageBase 与 `0x437D0F` 前五字节后跳到兼容层；原 EXE 函数为数字 `Effect_Hit`。
- 更新入口 `0x6FE812DD..0x6FE8179D` 延迟在 hit 时切换 WZ 缓存。四个原始属性偏移表在 `0x6FE8339C`：相对 `*[0xBEBF6C]` 的 `+0x170/+0x174/+0x188/+0x18C`，名字表 `0x6FE8338C` 对应 `NoRed0/NoRed1/NoCri0/NoCri1`。
- 首次缓存原始四组；skin=0 恢复。skin>0 加载 `Effect/DamageSkin/%d.img`，四组均加载成功且 pending 未变化才整体替换；失败保留当前组并记录失败 skin，避免每个 hit 重试。该失败保留策略尚未原样迁到 Unity，Unity 资源损坏仍显式报错。
- `DamageSkinCatalog.java` 有 **850 项**；`DamageSkinService.getSkinId` 对非法存档回退0，`setSkin` 只允许目录内 ID，更新 DB 成功后发 `DAMAGE_SKIN_UPDATE(int skinId)`。
- 原 NPC `npc/9900009.js`：主菜单 selection=3 进入皮肤页，每页4项；预览 `Effect/DamageSkin.img/preview/<id>`；调用 `getDamageSkinIds/getDamageSkinName/getDamageSkinId/setDamageSkin`。脚本未修改。

**运行时已接**：`NativeDamageSkinCatalog` 导出原目录，四个 NPC API 绑定在 `NpcApi`；沿现有 `GameSession.SaveToDb` 保存 `CharRecord.damageSkinId`。`NativeDamageNumber` 从该字段选原 IMG；blue/violet 不使用自定义皮肤。与源 DLL 一样在单人客户端中为当前角色选择字体，未实现联机逐攻击者的独立字体。

## 已核实：数字组不是两层，原端合成和时序

此前 `DamageText` / `Art.DmgSkin` 注释把0/1当成内层/描边，是错误解释。以下依据上述 EXE SHA：

- `0x437D35..0x437DBE`：类型0取 red 或 critical 四组，类型1/2走 blue/violet。`[ebp-0x18]` 为1组，`[ebp-0x20]` 为0组。
- `0x437E37..0x437F04` 和 `0x4380CF..0x4382B3`：**首位取1组，后续取0组**；每位只绘制一张 Canvas。
- `0x437F47..0x437F86`：令 `cx` 为字形水平 origin，`right=width-cx`，`x=cursor+cx-overlap`，保存x，`cursor=x+right`，下一位 `overlap=trunc(right*3/5)`。最终行宽为 cursor。
- `0x43805E` 创建行 Canvas：高度 `0x39=57`、宽度为行宽。`0x43834C..0x43839B` 把数字像素插入：左上角 `x=保存x-cx`；`y=57-(偶数位?4:1)-(rand()%2)-height`。不是按 origin.y 排列；原 Copy 受到行 Canvas 的裁剪。
- 暴击 `0x438163..0x438225` 在首位分支先把1组的 `effect` 复制在合成行 `(0,0)`，不是上方95px另造一颗旋转星爆。
- `0x4383DF..0x4383F7` 行左上位置 `x=传入x-trunc(行宽/2)`、`y=传入y-47`。
- `0x43852A..0x43856A` 插入400ms alpha255帧；`0x438642..0x438682` 插入600ms alpha255→0帧。`0x4387F4..0x438849` 使用图层当前时刻+1000，移动到 y−30。
- `0x6691D3..0x66926B` 怪物数字调用：行号乘以30（特定flag为15），上移另加15；正数走 `Effect_Hit`，零走独立 Miss `0x438A21`。Indexed DLL 的源码入口在 `0x66C6CB` 解 `0x80..0x8E` 行号，传入 `0x6691D3`；**该 DLL 源码与当前二进制是否完全相同仍待核**。

**已迁移范围**：普通、暴击、blue/violet 数字走 `NativeDamageNumber`，使用原 IMG Canvas 合成，去掉数字的旧随机横漂、逐位弹入、两层叠字、上移星爆。时间用实时钟，不跟随项目的 timeScale 顿帧。

**更新后的边界**：普通/自定义多段已接 NativeDamageLines 行号，0值走原 Miss，额外暴击1.08等缩放已移除；详见后文 indexed 合同及 [角色受击与飘字](character-reactions-and-damage.md)。原端所有伤害锚点、普通攻击起始命中时点、玩家guard尚未全部还原。CPU source-over 合成不等于对原 Gr2D 所有像素格式及透明舍入逐位验收。

## MCV 容器、双流解码与播放（续作更新）

- 正确后缀是 **MCV**，不是 MVC。源 `tool/client-video/McvFormat.cpp` 有完整容器解析器。
- `MCV0`，固定头36字节；+6 headerLength，+8 FourCC xor `0xA5A5A5A5`（VP80/VP90），+12/+14宽高，+16帧数，+20 flags，+24纳秒delayUnit，+32默认delay。
- flags bit0 alpha表、bit1逐帧delay表、bit2逐帧start表；颜色/alpha的 offset 相对全部表结束位置，不能当绝对偏移。无start表按delay累计；duration为各 start+delay 的最大值，不假定表单调。
- 原边界：尺寸1..8192、帧数1..100000，只接受flags0..7；所有时间乘加检查UInt64溢出、偏移转换检查UInt32溢出、负载必须位于文件内；原解析器不校验版本字节，不擅自增加版本限制。
- 纯C# `McvVideo` 迁移上述容器逻辑，未引用Unity/反射/JIT。
- `tools/mcv-check/verify.py` 编译源端**原样** McvFormat.cpp 为 oracle，比较颜色/alpha绝对偏移、尺寸、FourCC、flags、每帧start/delay与duration。74个真实视频+17个标志/截断/越界/溢出用例全部一致。
- 原 `BeiDouVideo.cpp::DecodeWorker` 使用两个libvpx decoder（颜色与alpha，threads=2），有界FrameSlot队列；`DrawTextureLocked` 用完整render target尺寸，不沿用局部viewport。`BeiDouVideoApi.h` 频道0 player-skill、1 boss-scene。
- `vellum-video-compat/README.md` 记录关键层级：FIELD_EFFECT 7×5 marker应在技能层触发绘制，飘字/UI随后画；移到Present会盖飘字。不能直接搬Present全屏叠层到Android。

**本次已接通**：`Native/Mcv/mcv_decoder.cpp` 移植双libvpx decoder（threads=2），逐帧颜色与可选alpha解码；YUV整数公式、alpha取转换后red与原代码一致，仅BGRA改RGBA。`McvPlayback` 后台线程保持3槽队列，首帧入队启动Stopwatch，消费最新到期帧、仅丢弃更早帧，取消唤醒队列并join线程。`NativeSkillVideo` 上传原像素、垂直翻转一次、Bilinear采样，铺满当前正交游戏相机视口；Boss频道800、技能频道801、现有飘字900。原 `BDV_RenderAll` 依次boss-scene再player-skill（`BeiDouVideo.cpp:747..756`）；这些Unity sortingOrder是当前管线接入值，不是EXE原生z数值。已处理同技能频道替换、跨频道共存、换图/施法者失效/死亡/退出游戏态清理。

- Android原生库位于 `Assets/Plugins/Android/arm64-v8a/libmapledash_mcv.so`，静态链接libvpx1.15.2，API23，16KB ELF LOAD对齐，仅依赖系统libc/libm/libdl。项目当前ARM64/minSDK26。macOS为arm64/x86_64 universal（当前Unity编辑器是x86_64，不能只提供Homebrew的arm64库）。
- `Native/Mcv/build.py` 固定上游版本和tar SHA256 `26fcd3db88045dee380e581862a6ef106f49b74b6396ee95c2993a260b4636aa`；源码/构建方法和libvpx LICENSE留在仓库，第三方源码缓存不混进Assets。
- 加载 `WzAssets.DataRoot/Video` 的原文件；既有 `package_native_assets.py` 按所有原文件打包，未转码。实际74份全是VP9，VP8分支没有原端样本，不能宣称真实VP8已测。

**验证边界**：MCV原生解码已在连接的Android设备以独立程序运行，见下节；并非APK渲染验收。Unity无AndroidPlayer模块，未构建APK。尚需手机游戏内视频层级/帧率/内存验收；角色动作和V/VI伤害replay另行迁移。视频铺满相机是Android现有视口的接入，未宣称完整复刻EXE的所有世界对象遮挡关系。

## 已核实：独立 KaringSceneCompat.dll 与 Boss marker

续作发现旧技能兼容源码的Boss表不是现行部署位置。实际为 `clien/KaringSceneCompat.dll`，SHA256 `3d2c5373696f08e1124e0ce16cef74dbe7ba4f0890fbdd984136ac34c0783e41`，ImageBase `0x6D840000`。

- 选片函数 `0x6D8415D1`；`0x6D8415EE` 比较 `[eax*8+0x6D842620]`，`0x6D841607` 比较项数0x24，共36项；路径取 `[eax*8+0x6D842624]`，调用时频道参数1（`0x6D841628..62F`）。表范围 `[0x6D842620,0x6D842740)`。编号1..32和34..37；**33不在表中，是停止指令**。相邻旧源码仅29项，不能覆盖当前表。
- `0x6D841B38` 检查纹理尺寸：宽7..8、高5..8，再读前5像素。A8R8G8B8识别分支 `0x6D841000`（Karing）、`0x6D841A25`（Lucid）、`0x6D8410D3`（Root Abyss）；A4R4G4B4在 `0x6D841C0D..D56`。Unity解析原Canvas后只需对应的RGBA签名。
- Karing前4像素RGB：`221144/445577/99AABB/CCDDDD`，第5像素red/17为1..14；Lucid前4像素 `112244/556677/8899AA/BBCCEE`，第5像素 `(red=n*17,green=0x44,blue=0xDD)`→14+n（n1..15），green=0x55→29+n（n1..4），其中33=furyStop。Root Abyss为 `335577/668899/AABBCC/DDEEFF`，第5 `(n*17,0x88,0xEE)`→33+n（n1..4）。前三组检测alpha255；原X8纹理可忽略alpha，当前原IMG解码保留实有alpha。
- `0x6D8417DA` 消费标记；33分支 `0x6D84180C..863` 调StopChannel(1)、清pending。其他标记若已有scene，`0x6D841882..88B` 只在pending为空时保存首个；结束状态由 `0x6D8416C8..74C` 检查并启动pending。重复纹理帧去重/缺失10帧再允许规则属于D3D marker draw入口；Unity按已有服务器事件调用一次，不重复逐帧触发。
- `tools/inspect_custom_modules.py` 现按此SHA门控导出 `NativeBossVideos.json` 36项，并保留到inventory。当前源端Data/Video缺编号34..37的4个 `root-abyss-*.mcv`；不生成替代资源，不声称可播放这些缺失文件。
- `NativeBossVideoEffect` 从 **`Map/Effect.img/<FIELD_EFFECT path>/0`** 读取真实7×5 Canvas，使用 `NativeVideoMarker` 识别；`NativeBossEncounter.Effect` 的现有Lucid事件已接此入口，Clear清Boss视频。`QueueBoss`保留正在播放的片段和首个待播片段，33仅停止Boss频道，技能频道继续。Karing标记可被相同入口识别，但当前没有新增完整Karing战斗流程。

续作验证：

- `tools/mcv-playback-check/verify_decode.py`：74原MCV×前8帧=592帧，对源端**原样提取**的ConvertFrame函数逐像素比较，仅校验BGRA→RGBA通道重排，PASS。此项使用相同libvpx作为依赖，证明移植转换一致，非独立编解码器认证。
- `tools/mcv-playback-check/PlaybackCheck.csproj`：真实galaxy-star-burst完成定时播放，最终重跑119张显示帧；alpha/颜色输出、3次满队列取消、损坏VPX错误报告PASS。
- Unity `McvPlaybackChecks.Run`：`MCV_UNITY_PASS decoder channels replacement cleanup IMG_markers=19 boss_stop`；19个实际Lucid Canvas全部识别为15..33，验证Boss-only stop和两频道共存。没有写玩家DB，没有完整战斗画面验收。
- Android独立程序 `native_check.cpp` 与主机完整解码比较：galaxy-star-burst119帧，RGBA FNV64=`a6b94c06c39f198b`、透明像素16903401；lucid-bomb70帧，FNV64=`6b45b91d48c0aaaf`、透明像素64492202；两端一致。不是APK/Unity手机渲染测试。
- 重跑入口 `Native/Mcv/README.md`、`tools/mcv-playback-check/verify_android.py`（独立临时目录，测试后清理，不装APK/不访问存档）。


## 天气：源码事实与剩余工作

### 2026-10-29 用户指定的背景天气显示策略（迁移差异）

- 本节仅记录 Android/Unity 显示策略变更，不修改下面原 DLL 的身份、地址、公式或原端结论。用户要求天气不再使房屋、路面、NPC 整体变暗。
- `NativeMapView.WeatherTintBacks` 按地图 `back` 节点的 `front=0` 收集原后景；`NativeWeatherField.Bind` 不再收集全地图 SpriteRenderer。tile/obj、NPC、portal 和 `front!=0` 原前景均不参与天气染色；后景平铺副本仍由 `UpdateBackgrounds` 复制原 renderer 颜色。
- 原21张天气 IMG、布局/滚动参数和 profile 保留；天气背景统一 sorting `-1100+index`（替代部分 `700+index` 前景层），原生天气粒子从 `710` 改为 `-700`，位于普通后景 `-800+index` 之上、地图地形 SortingGroup `-400` 之下。既有天空提升 `-1200+index` 不变。天气粒子因地形遮挡而不可见是该显示策略的预期。
- 地面附着保留原素材/生成逻辑，颜色白色，溅水仅保留雨量 alpha，不再继承夜间染色。并非关闭全部地面天气，也不声称与原端整场景调色一致。
- 验证：本次代码差异静态检查及 `git diff --check`；未启动 Unity、未构建 APK、未做实机画面验收。

- 完整模块入口在 `tool/client-debug/weather-compat/src/WeatherCompat.cpp`：通过 `BeiDouSetItemCompat.dll::BDS_RegisterPacketHandler` 接 `0x373D` 天气与 **`0x373F` skillback**，不是天气一个包；每次app tick推进天气、skillback及落地附着/水洼/摆动。
- 服务端 `WeatherPackets.java` 当前负载26字节：minute ushort、msPerMinute int、sky byte、flags byte、elapsedSeconds int、tintRGB3字节、rainbowSeconds ushort、palette byte、elapsedMs int、skyToken int。客户端 `Weather_HandleWorldState` 最低8字节，后面各尾字段按剩余长度依次可选解码；包offset从进入位置相对跳过opcode2字节，不能假定offset=0。
- `SetWorldState`：disabled强制720分钟/clear/frozen/无彩虹；分钟越界归0，msPerMinute最少100，未知sky归clear；elapsedSeconds非负，旧包无elapsedMs时以seconds×1000饱和到INT_MAX回退。
- 本地时钟只在未frozen时以真实dt/msPerMinute推进；dt≥60000ms不推进，等服务端下一包重同步；换图先清成clear，等待新图snap包，避免雨区状态污染下一地图。
- 九种sky：clear/rain/snow/overcast/storm/blizzard/leaves/blossom/sandstorm。渲染还涉及 `weather_profiles.inc`、`weather_palettes.inc`、skytable、nosky maps、灯光表、风、雪远近双层、积水/积雪/溅水、环境声音、树木摆动。
- 该DLL项目明确不含weather-footing，雪不改变物理，也不包含服务端战斗惩罚。当前 `clien/config.ini` 的 `enableWeatherSystem=false`，迁移不能据此把全局天气悄悄改为启用。
- README记录的EXE SHA `06cdac...ba18` 与当前 `1b91cf...f134b2` 不同。上述为源码与服务器合同；尚需对应当前DLL确认启用分支/地址/资源节点，不宣称当前DLL全部已复核。

**历史边界（已由下节续作更新）**：首次记录时天气与skillback未迁移；当前已接入状态、原背景层及skillback，尚余粒子、附着、声音等子模块。不能以Unity临时粒子或自画雨雪替代原IMG与原代码参数。

## 本次验证

- 原端WZ解析器已逐项验证850套皮肤的四组十位数字，共34000个直接Canvas节点；目录未缺文件、解析无truncated/warnings。没有做全量像素解压/渲染验收。
- Unity batch `NativeDllPortChecks.Run`：`NATIVE_DLL_PORT_PASS`。验证850个IMG文件存在、原 `NoRed1/5` 像素确实单张进入57px行Canvas、400/600ms淡出与30px移动；没有登录/写玩家DB。没有做全850皮肤逐帧渲染或Android实机验收。
- MCV oracle：74真实视频+17边界用例，逐帧一致；这是容器回归，新增的VPX解码/播放验证见上方续作章节。
- `tools/unity-compile-check` rebuild：0 error（数字渲染首次实现后）；之后新增MCV与Editor检查由真实Unity批处理编译通过。
- script pack：2777项（npc889、quest468），parseFail=0、mangled=0。
- 全NPC运行冒烟：889项，ok768、parseFail0、runFail48、javaInterop73；未通过项为harness的缺接口/Java桥接/原脚本运行状态等，不能报告全部脚本PASS。harness不编译本次修改的NpcApi，不能把这些失败归为已验证的皮肤API回归或宣称API端到端验收。

持久复现入口均在仓库；临时Unity日志 `/tmp/mapledash-native-dll-check.log` 只是运行日志，本文件保存结论和重跑方式：

```sh
rtk proxy /Applications/Unity/Unity.app/Contents/MacOS/Unity -batchmode -nographics -quit -projectPath "$PWD" -executeMethod NativeDllPortChecks.Run -logFile /tmp/mapledash-native-dll-check.log
```


## 2026-10-27 续作：伤害回放、飘字、skillback、天气

### 回放数据与当前接入

- `tools/export_skill_replays.py` 读取原 `gms-server/src/main/java/org/gms/net/server/channel/handlers/{CloseRangeDamageHandler,MagicDamageHandler,RangedAttackHandler}.java`，执行原 `ExplorerOtherSkillCompat.java` 的范围/拼接函数，不按视频长度猜时点。源文件及技能常量 SHA256 留在 `docs/skill-replay-source.json`。当前 `NativeSkillReplays.json` 为73个施放入口（新增追踪风与箭雨状态入口），116阶段、1925时点，覆盖39个MCV入口；单次原攻击的3个视频入口也明确保留。
- `NativeSkillReplay` 在成功施放/扣MP/冷却后捕获原攻击模板；阶段按技能等级上限读取真实WZ，循环复用模板、Java非负四舍五入、保留逐段暴击、19999999上限。目标死亡、换图、施法者失效停止；HP在阶段触发时一次结算，数字可延迟。
- 固定原点集合依据原 `usesFixedThunderBreakerOrigin`，包含五个冒险家战士视频技能、奔雷/迅灭/雷枪；不是仅雷枪。PhoenixDriveVI实时朝向，剑影最多8目标，海龙拳终结矩形±1000px、上450/下200；风魔手里剑在420..780ms沿朝向移动600px、280px方形。具体分支及数值保存在导出器与运行时。
- fallback从原Java规则迁移物理/魔法最大伤害、烈焰增幅、战士最低伤害与精通；**首击仍经过项目现有RollAttack，并未完成EXE随机、防御、动作分类的完整逆向**。
- `NativeDamageLines`：indexed包按120ms间隔（Galaxy60ms），每行30px、起点额外15px；怪物逐段FilterHit后立即合计扣HP，不重复过滤。保留每段Miss和critical。
- 本次纠正：CloseRange五参重载的 `NONE` 是不发额外本地数字，原攻击包仍有逐段数字；不应合并成非暴击总数。Unity现保留逐段数据。只有原海龙拳终结无视频的 `TOTAL` 分支合计非暴击显示。剑影使用原生CLOSE_RANGE_ATTACK构造器，不应标成额外INDEXED包。**NONE原生攻击构造器的全部时序尚未复核，现沿用120ms调度，不能据此称逐帧完全一致**。
- 尚缺：双刀followup、FourSeasons/reactiveArrowRain、追踪风与Mistral精灵、部分法师场地/召唤及概率分支；当前71入口不等于完整五六转系统。

### Miss与伤害皮肤

- 当前EXE身份沿用本文 `1b91cf...f134b2`。`Effect_Miss0x438A21` 类型0取root+0x170（NoRed0）、类型2取+0x180（NoViolet0），类型1返回；字符串池0x3D8为大写 `Miss`。
- 调 `0x43DE28`：left=x−trunc(width/2)、top=y−height，不使用Canvas origin；400ms保持255、600ms淡出、1000ms上移30px。Unity `NativeDamageNumber` 直接加载原Miss Canvas；皮肤四组全部成功后才原子切换，失败保留前皮肤并抑制重复加载，成功选择可重试。
- 实际 `IndexedDamageNumberCompat.dll` SHA256 `e7f0a2f99501e538dcbfb9f5b5a8f35794f1a2c381175dee10273b7c799cdc23`，base0x6B340000；hook0x6B34101B将0x80..0x8E解为低四位行号，否则0，再调用EXE0x6691D3。
- EXE0x6691D3：行距30px（特殊flag为15），y−15−行距×index；0为Miss，正数普通数字，负数取负并走暴击。服务器signbit编码到该函数之间可能有其他兼容hook，**未宣称整个符号归一化链已复核**；Unity独立保存伤害与critical。

### skillback

- `NativeSkillBackdropState/Service` 和 `NativeSkillBackdrop` 接0x373F状态、地图级/本地显示、清除、到期与换图清理；NpcApi接四个原名show/clear（本地与地图）。无独立网络传输适配，现有本地服务使用类型状态。
- 包offset指向opcode，相对跳2；mode0清除，其他值显示；ushort长度≤192，后接字节及int duration，截断/非法保持旧状态。允许ASCII字母数字 `_./-`，拒绝 `..`；MCV后缀大小写敏感，允许裸文件或恰好 `Video/<file>.mcv`；非MCV必须含`.img/`。服务端trim、加Video前缀、duration限制0..86400000；客户端≤0为无限。
- 原fallback顺序：`Map/Effect.img/customSkill/skillBackdrop/videoLayer`，再dawnWarrior的galaxyStarBurstVideoLayer、soulEclipseVideoLayer、eclipseForceVideoLayer。原生z为1000×(InjectedLastIndex+5)−0x4001F400、hills+8。
- Unity MCV沿用频道0，排序−900，在背景山−800/地图砖−400之后；为当前渲染管线映射，不是EXE数值。普通IMG按原Canvas/delay循环；尚未完整移植CreateAnimLayer元数据。非MCV缺资源fallback触发视频marker的完整D3D链、BuildMap直接API仍有边界。

### 天气当前DLL验证与实现

- 当前 `BeiDouWeatherCompat.dll` SHA256 `31e8e486c2f339ff1d90d4a4bc6509ab92c779e11f84a9d9d235941399aeffcc`，base0x10000000。dispatcher0x100014E0：0x1000150B识别373D→0x100068F0；0x1000152B识别373F→0x10021750。
- `tools/verify_weather_binary.py` 将实际DLL与原源码表逐字节比较：9profile在0x1003E460/stride32、27palette在0x1003E580/stride12；g_back0=0x1003F680、g_back1a=0x1003F6A8、night=0x1003F6D0、dust=0x1003F748、rainbow=0x1003F798、cloud=0x1003F7C0、fog=0x1003F838、snow=0x1003F8D8、rain=0x1003F928，全部一致。不是只根据旧README认定当前版本。
- `export_weather.py` 与 `docs/weather-source.json` 保存来源/哈希。`NativeWeatherState` 实现旧/新包、非零offset、冻结时钟、单次dt≥60000不推进、timeline/token/有符号彩虹字段。`NativeWeatherService` 实现UTC世界时钟、区域权重、15分钟天气轮换、4小时昼夜、覆盖到期/彩虹和60秒同步；客户端沿原配置默认关闭。
- `NativeWeatherField` 使用原21张背景层、57天空key与264排除地图：sky2/dust2/night3/rainbow1/cloud3/rain4/fog4/snow2；沿用NativeMapView背景铺图、原BACKENTRY字段、调色/profile、黎明黄昏、30ms淡入。水下230仅染色无降水。普通云雾漂移为引擎−5×rx，不另加推测风偏移。
- 纠正旧注释：当前weatherfx的blizzard不使用snow背景层，而是4雾层加280个原生粒子；普通snow只有2背景层。**当前仅迁移背景层，blizzard缺280粒子，不得报完整暴雪**。
- `NativeWeatherLightning.cs` 由原闪电方程机械导出：scene release .697、sky release .578、storm且rainlevel≥.7；`verify_lightning.py` 对原样C++公式729756组向量比较，误差≤1e−6。
- Unity天气层−1100+index/前景700+index，天空−1200，山−800；这是接入映射。此段历史剩余项中的原生粒子、环境声音、地面天气已由下节接入；灯与植物摆动、完整场景重新照明及外部配置替换仍未移植。不得以泛用Unity粒子代替。

### 续作验证与边界

- `dotnet run --project tools/dll-state-check/StateCheck.csproj -- Assets/Resources/NativeWeather.json`：DLL_STATE_PASS，覆盖skillback校验/截断/到期、天气旧包/时钟/区域/覆盖/彩虹/禁用。
- `python3 tools/dll-state-check/verify_lightning.py`：LIGHTNING_ORACLE_PASS vectors=729756。
- `tools/verify_weather_binary.py`：WEATHER_BINARY_TABLES_PASS。
- Unity `NativeReplayChecks.Run` 检查71入口/116阶段/1925时点WZ存在、模板取整/循环/critical/cap、原Miss/skillback marker、三段伤害100→91立即结算；射手村/魔法密林/水下3图的21层天气资源与天空分类。**不是全部技能战斗/视觉验收或APK**。
- NPC重新打包2777项、parseFail0/mangled0；889脚本运行结果ok768/parseFail0/runFail48/javaInterop73，与之前基线相同，未称全部通过。UseAppHost=false构建后顺序运行/打包，避免同项目并发编译apphost竞争。
- AndroidPlayer模块仍缺失；未构建APK，尚需手机完整技能与天气渲染验收。


### 雷枪12按伤害链（本次追加）

- 源 `CloseRangeDamageHandler.advanceLightningSpearCombo` / `repeatLightningSpearThunder` / `scheduleLightningSpearFinisher`：主技能15121002；combo visual15121022..15121033；雷击15121009、终结15121010、大雷15121011。常量及handler哈希随导出记录保存。
- 60秒窗口，从首次按键计时；有效按键间隔≥180ms；第12按设finishing，终结期间拒绝推进。换图/死亡/角色失效走原combat lifecycle取消；到期只清未finishing状态。
- 每次按键原捕获攻击先结算；再依原捕获顺序向最多3个仍活目标追加一次雷击（不是向同一目标连打3次），使用首个伤害模板依原/雷击技能百分比调整，找不到源模板才生成近战fallback。主段和雷击额外本地数字使用总数分支。
- 第12按从该次捕获生成终结模板；510ms终结，840/1170/1500ms大雷，目标/原点实时跟随角色，使用各子技能WZ范围与目标数；最后一次结束清状态。
- `NativeLightningSpearState` 为无Unity状态机，`NativeSkillReplay`接上述伤害段，CancelActions重置；纯状态回归覆盖179/180ms、12按、finishing锁、60秒到期、生命周期清除。**此处为历史边界，已由下节 numeric action/原 effect 接入更新；180ms 是原服务器节流与当前移植输入门，不等于已逆向 EXE 完整动作锁。**


## 2026-10-27 续作：追击、雷枪动作、落地天气与环境声

### 服务端追击合同与当前运行时

`tools/export_skill_replays.py` 同时导出 `NativeSkillFollowups.json`，数组与常量来自当前原 Java；源文件 SHA 随 `docs/skill-replay-source.json` 保存。当前73入口/116标准阶段/1925标准时点；动态索敌波次不计入1925。

- `CloseRangeDamageHandler.triggerDualBladeFollowUps`：SHADOWER 家族且原攻击 numAttacked>0，每个原攻击计数一次（不是每个伤害行计数）。BladeFuryVI 4221020 触发 HauntedEdge 共享12s门：Asura4221014在0ms，Projectile4221015在120..720ms间隔120。PhantomBlowVI4221018触发Rakshasa4221016在 **30/120/210/300/390ms**。累计15次触发4221017，390/420/450/480ms，1s门，最多储存60次。未自行添加“必须学过隐藏技能”条件。
- 四季雨4121023：有捕获目标才存下一次狂乱；下一次使用原4121024阶段且消耗该状态，不重新存狂乱，放行这一次冷却。
- 箭雨3121010激活70s；有原攻击目标时每5s最多一场3121033，固定施放原点，0..2400ms/240ms，等级取3121010。普通弓/弩攻击也从实际已 roll 的伤害模板接入，不重新随机。
- MercilessWinds13121003：10个索敌任务在0..900ms/100ms启动，8s截止；固定施放框，空目标100ms后重试。Boss优先，Boss maxHP降序，然后距离、对象ID；Unity最后一项使用实例ID（不是原OID）。命中过的目标只乘一次85%，并非逐次指数衰减。13121018原WZ伤害与段数；1100%单段DOT，每秒一次共9次，同目标用generation刷新而取消旧DOT。DOT直接扣血，保留死亡处理，不重新过滤命中、不打断怪物动作、不播Damage音。
- Mistral13121013：4080ms后、每2100ms一波，20s内10波，13121015/16/17轮换，数量13/5/3，寿命3000/4000/2400ms，启用延迟270/120/120ms，总截止施放后25s。索敌100ms重试，无WZ框才使用原Java ±1200x±800范围。数字indexed120ms。各隐藏技能现直接读取 `Skill/<job>.img/skill/<id>/hit/0`，保留帧delay、origin，不借主技能hit代替。
- `NativeSkillFollowups.cs` 接以上状态与伤害；地图身份改变、角色死亡/失效、退出Playing会终止任务；`CancelActions`清任务、DOT与自有动画。箭雨/狂乱状态本身与Java弱角色映射一样不因换图自动重置。

**边界**：这些是服务端规则及已接代码，不是完整职业战斗验收。泛用多飞弹/突进路径仍需核对每个原攻击包的追击计数，而非按每次局部HitAt计数。当前隐藏技能命中特效已接，**Merciless/Mistral召唤飞弹的起点、轨迹、朝向和发射层尚未完整迁移**，不能把即时伤害等同于完整召唤表现。

### 雷枪 numeric action 与原 effect

- `Skill/1512.img/skill/15121022..15121033/action/0` 全为 `alert5`，主技能及15121009/10/11也为该动作。`Character/00002000.img/alert5` 3帧重定向alert0/1/2，delay为−300/+300/+300，时长按绝对值900ms。combo effect 的fix=1。没有自画雷枪。
- `WzCharacter.Pose`支持 `skill_<数字ID>` 查真实action；每次有效按键 `PlayerCtrl.PlayNativeSkill(15121022+count-1)`；`NativeSkillReplay.PlayNativeReplayEffect`直接加载原effect，fix决定固定地图/跟随人物，z<0置于人物后层，原朝向翻转，保留原origin和delay。15121009追加、15121010/11终结阶段也接原effect。
- 当前施放门改为180ms，来源是服务器的最小按键间隔；**尚未证明 EXE 的动作取消窗口与这个门完全相同**。未验收12按整套手机手感。

### 当前 EXE 的原生粒子与 Shape2D 证据

EXE身份沿用本记录首表；新增依赖 `clien/Shape2D.dll` SHA256 `5e9b27d392b4a373fcb21d8b188c8b6864c1fd60fc33563ae24c782b26d97e43`，ImageBase `0x51400000`。

- `CField::BlowWeather 0x534A35` → `0x534F2D..534F48` push speed、direction、type、path → `0x63FE57`。builder +8=path,+C=type,+10=direction,+14=speed；天气物品direction缺省0。不要把direction分支误认为type。
- `0x640448..64048C`：幅度15+(rand()%4)*(rand()%4)，初角rand()%360，初始渐变3000..5999ms。非ani资源随机一个原Canvas变体。random=1时数量为 floor(count*(rand()%25+30)/100)，count缺省100；不是固定count。
- direction0 `0x6406BD..640700`：起点在原screen box；endY=startY+screenHeight/2；`0x64041C`得到50000−speed*8000，主位移周期为该值/幅度，整数除法；`0x64189B..641916`以VM_FOREVER重复位移。
- `weatherwind.cpp`：独立xorshift32初值0x9E3779B9，token混入sky再按Mix哈希。沙尘风量±1200..1800、粒子散布±260；暴雪 prevailing±150..300，但实际雪漂移固定向右，|wind|夹在fall的45%..75%再加±90；叶/花prevailing−300..300，加±150。生成后不随帧重新滚风向。
- `0x640D98..640F7F`：独立原点旋转初角后Rotate(0,2000)，其子点坐标幅度/2,0；`0x6411EF..6411FF` ratio连接参数100/100/100/5，扣除captured起始点。Shape2D `0x51406FA2..51407045` 将父旋转应用到局部坐标，正负值分别加/减0.5后转整数，不能截断代替四舍五入；ratio的5%纵差本身仍整数截断。COM vtable从`0x5140C0FC`开始，+90=`0x5140828F` RelMove，+98=`0x5140847D` ratio，+9C=`0x514086B3` wrap，+A0=`0x51408A50` Rotate。`0x51408B08`角度0意味着持续360度/period（常量`0x5140D8C0`=360.0），并非“不动”。ratio实现`0x514085B3`按整数差×numerator/denominator累加。wrap helper `0x514088A9`为正余数。
- `0x641026..64106B` wrap左上−halfWidth−幅度/−halfHeight−幅度，宽高screenSize+幅度；最后两个是宽高，不是右下坐标。`weatherfx.cpp`只在native item ID改变时重吹；同种天气换token不使旧粒子突然改轨迹。
- `NativeWeatherParticles`按以上原Canvas、数量、速度、风、主位移和ratio摆动接入；Android将resolution.cpp固定screen box换为当前正交视口，图像保持原PPU，sorting710是Unity管线映射。原MSVCRT rand序列用本地同算法状态，不可能还原整个EXE共享rand的运行历史。
- 原生粒子white颜色与background/scenery tint分开。**Gr2D alpha vector的完整合成/取整尚未逐像素oracle核实**；当前初始渐变是移植实现，不能称整个粒子渲染已逐帧与EXE一致。分辨率动态改动会重建Android粒子箱，是移动接入行为。

原资源：`Item/Cash/0512.img/05120996..05120999/info/path`分别指sandGrit/blizzardSnow/leafMix/blossomMix，type2，speed6/4/2/2。砂14种4..6px、雪7种3..6px、叶7种11..19px、花6种12..24px；原origin直接由Sprite保留。砂count1260，雪280，叶/花缺省100。普通雪sky2用远近双背景层；05120995/snowFall虽存在但原profile未引用，不额外生成。

### 积雪、脚印、水洼、溅水与声音

`NativeWeatherGround`依据原 `weatheraccum.cpp/weatherpuddle.cpp/weathersplash.cpp`，沿真实 `FieldMap.footholds` 查找视口内地面（y向下像素）；竖直墙不作为承接面。原端物理空间插值/分层查找尚无EXE逐点oracle，不据此称整个碰撞完全一致。

- snow/leaf/petal最多70，90s累积、25s融化，14次/s，26px槽位，最多3层、抬高2px，surfaceDrop5，边缘±14px探测容差10，单次最多6次尝试，视口margin80，旧天气类型完全融化后再长新类型；按服务器sky整秒年龄播种，单帧最多25补种，未用补种退回。3倍半视口外剔除；减少目标时每帧删最远一份。
- 脚印仅雪：落地或22px步幅，必须站在实际雪sprite半宽覆盖区、脚下−6..+8px地面容差8，11s寿命、末3.5s淡出、最多20。原源码注释称脚印会清雪，但实际StepFootprints未清deposit，移植遵循实码。
- 水洼最多26，75s积水、40s干燥，3次/s、最多12次尝试，平坦±12px容差3；tier半宽15/24/35（30/48/70原图），扩宽边缘half+2容差10，按最老可容纳tier提升，放置失败仍可扩宽旧水洼。surfaceDrop6，同层dy24，间隔由两者可容纳最大半宽之和+56决定。按年龄补种，单帧最多10。
- 踩水dy14px，进水6份溅射，每26px步幅2份，横散布±11；雨落地溅射22.4/s×rainlevel，最多40，520ms寿命，surfaceDrop4、margin64，1/3尝试已有水洼。原IMG三类雨溅射、九套水洼及原脚印/沉积Canvas，不画替代粒子。
- 沉积/水洼/脚印取scenery tint，雨滴溅射取rain tint；地面sorting−399、脚印/溅射−398，是现有map SortingGroup−400上方的Unity映射，不等于原EXE的所有地形遮挡。Clear/OnDisable移除自有层。
- `NativeWeatherSound`迁移weathersound：原Sound/Weather.img rain/wind两个60s loop，音量SFX×0.7，雨开启0.35、关闭0.10，雨退到关闭阈值前不切风；切换旧voice1500ms释放。clip异步读到后才启动voice；缺索引/解码失败按weather失败集合抑制重试。Unity直接更新音量，不需要原CSoundMan静音后重启的修复。
- 原weathersound的CSoundMan PlaySE=0x43FB25、StopSE=0x773156、singleton=0xBEBF94。**原踩水脚步声已移除，Sound/Weather.img/splash存在不代表被使用，当前保留静默。**
- `tools/export_weather_effects.py`可重放原始MP3提取并更新SoundIndex；`docs/weather-effects-source.json`保存原六个IMG与两个MP3 SHA。`docs/weather-source.json`新增以上四个cpp哈希。

### 新检查与尚未验收

Unity batch：`NativeWeatherEffectsChecks.Run`，日志标记：

```
NATIVE_REPLAY_PASS casts=73 stages=116 ticks=1925 WZ Miss marker template
WEATHER_IMG_PASS maps=3 original_sheets=21 sky_classification
WEATHER_EFFECTS_PASS snow_seed_melt snow_footprint_idle_gate puddle_seed_promote_dry particle_profiles=4 native_count frame_position cleanup audio_index followup_hits spear_actions=12
```

检查实际原IMG与地面运行时代码：老雪补种/25s融化、实际雪层落地脚印/原地不重复生成、雨后水洼/扩宽/40s干燥、四种原粒子数量区间/图像/坐标、clear切换/清理函数、两条音频索引、追击hit节点、12个雷枪action。没有登录、存档或DB写入。批处理Edit模式不触发普通组件OnDisable，因此清理断言直接调用同一Clear函数，不能据此宣称Play模式生命周期已全验收。

`NativeReplayChecks`先预热原数字glyph再断言120ms pending状态，避免首个IMG/皮肤目录加载耗时把时钟推进到所有行已到期。这不是修改运行时伤害时序。

仍待完整验收：追击冷却/空目标重试/地图取消的真实战斗，连续步行脚印与踩水的玩家移动回归，环境声音实际解码和交叉淡出听测，召唤飞弹完整表现，Gr2D粒子alpha合成。树木/灯与其他未列天气子模块并未因此完成。AndroidPlayer模块仍缺；未构建APK、未做本次手机游戏内验收。

### 继续逆向的准确入口：当前核心 native projectile hook

新增已核实入口，避免下次重复定位：

- 当前核心 `0x6728429D..6728432D`安装 EXE `0x954596` 9字节补丁，目标`0x6728121C`；hook恢复原mov后保存flags/寄存器，依次传`&[ebp−0x28]`、`&[ebp−0x24]`、`&[ebp−0x2C]`给`0x67285F0A`，随后按原flags返回95459F/9546AC。
- `0x67285F0A..67286026`检查native window `0x6728DB80`和target window `0x6728DB8C`均未过期、targetCount `0x6728DB94`>0；原子递增`0x6728DB88`按targetCount取余，读取`0x6728DBA0+index*8` x/y写入目标，travel参数若>0x294则夹为 **660ms**。与旧cpp的AssignNativeRangedProjectileTarget合同一致，已核当前二进制。
- 原EXE `0x95445F..954511`按起终点欧氏距离×常量`0xAFE808` **1.5ms/px** 计算普通弹道时长（个别原技能另分支）；`0x954683..95469B`将start/end及时间传给magicBulletManager`0x435DF3`。`0x953D9D..953DAA`起始Y取角色位置−28px；`0x953DCD..953DDE`起始X还含调用参数的有向偏移，**该调用参数的所有施放/远程回放消费者仍待追踪**，不能自行假定永远0。
- 原CUser local射击过程与服务器回放RANGED_ATTACK(0xBB)的远程处理不能混为一谈；当前Java replayTargetedAttack确实向自身sendPacket再向地图广播，未延迟服务器HP结算到飞弹落点。故飞弹呈现迁移不可改变已确定的服务器伤害时间点。
