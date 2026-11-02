# 自动AP、获得提示、背包数字与Boss血条补查（2026-10-31）

## 身份与复现

BeiDou.exe SHA256 `1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2`，ImageBase `0x400000`。使用项目 `tools/inspect_beidou_client.py <exe> --fn <VA>`，IMG 使用 `tools/imgdump.py`。以下为已确定结论；迁移验证不等于 Android 实机验收。

## 自动 AP 确认

`0x8C8692 → 0x992DFD(text,0,1) → 0x992EDA(mode=1001)`。`0x991899` 使用 `UI/Basic.img/YesNo3/{t,c,s}`（266宽）。`0x99311B` 根据 this+0x80 选字体：默认19/20，非零22/23；自动AP是22/23。字体工厂 `0x98A707` 跳表 `0x98CB81`：22入口 `0x98B4DD`，`0x98B538`传白色FFFFFFFF和12px；23入口 `0x98B57C`，`0x98B5D1`传FF355A84和12px。不是黑字。字符串1951/1952在当前EXE为英文；Android本次将能力名称与说明中文化。NativeUtilPrompt保留原YesNo3切片，确认白字，编辑模式黑字。

## 背包数量

`0x81C7B2`读取字符串1253 `UI/Basic.img/ItemNo`；`0x81DCB5`在 `0x81DF1D..0x81DF54` 调 `0x988345 → 0x98812C`。槽位 `0x81E2C8`：left=8+36*col+149*block，top=50+34*row，bottom=82+34*row。数量起点=left,bottom-12，即top+20。数字从左向右逐canvas拷贝，步长=canvas.width+spacing，调用spacing=0。ItemNo/1宽5，其他宽8，高11，origin=(0,0)。运行时GameWindows使用原数字图；不再普通字体右对齐。此结论只覆盖显示样式，不表示背包聚合/多堆槽位已完整对齐。

## 右下角获得提示

单例 `0xBEDCD0`，构造 `0x89AE9A`：六个290×14的层；添加 `0x89B159`，count>=6回收最早层，textX=290-字体测量宽度（`0x89B2C6`）；主字在(x,0)，阴影在(x+1,1)；初始alpha255，时间终点now+0x1770=6000ms。位置函数 `0x89B60F`：x=504，y=443；聊天展开状态this+0xD10非零则y=365，按14逐行向下；构造即创建六个空层，首条新消息位于第六行(y=513/435)，不是第一行。800×600下右边缘794；移动端按既有BottomBar.S缩放并锚定右下。

EXP入口 `0xA21F09..0xA21F35`：white真时(font0,shadow1)，否则(font3,shadow1)。font0白、font1黑、font3=FFFFFF20，字号12；源Monster.gainExp通常inChat=false。拾取入口 `0xA20B6B..0xA20B7B`使用(font0,shadow1)。Java PacketCreator.getShowExpGain是SHOW_STATUS_INFO/type3，普通拾取是type0，不能当聊天或Toast代替。NativeGainNotice从Profile.GrantExperience及成功GroundItemBridge拾取接入；金币取实际结算差，物品数量在入包可能改写前保存。尚未覆盖所有非拾取奖励包和队友异步分配通知。

## Boss HP

`0x5336CA..0x534A1E`：字符串3288..3294即 `UI/UIWindow.img/MobGage/backgrnd{,2,3,4}`、`Mob/%d`、`Gage/%d/{0,1}`。背景35×37、4×19、1×20、5×20；头像25×25，原拷贝坐标(5,6)。`0x534365..0x534383`计算填充整数截断=(793-baseWidth-offset)*currentHP/maxHP，默认758px。底边框依次x0/35/39..796/797；gage在y4，左cap x38，主段x39，右cap x797，按HP段切换前景/背景颜色；不自绘色条。原版扩展窗口偏移来自BED788+24；Android沿现有800宽底栏坐标居中。

Java Monster.hasBossHPBar明确为isBoss && hpTagColor>0，sendSpawnData时就announce；不是所有boss都强制顶栏。模板8880700/8880803映射8870000。NativeBossHpBar复用原IMG，与当前存活fieldMode怪物的NativeController长整数HP绑定；多Boss选择当前列表最后公告顺序，未验证全部多目标切换策略。

## 全地图刷新审计与迁移边界

`tools/audit_wild_bosses.py`读取全部6337张IMG，1149个boss模板；6229条直接life刷新记录，2339张链接地图解析后共7281条记录，解析错误0。清单 `docs/wild-boss-map-audit.json`。这是资源覆盖扫描，不是7281个Boss逐场景实测。

MapFactory.loadLifeRaw：Boss刷新倍率固定1，mobTime按boss_respawn_mob_time_rate缩放；-1强制一次；正常走SpawnPoint。FieldDirector本次连接刷新配置，按地图保留调度器/刷新点、存活标记与HP，避免离图清视图后丢计时。源map/onUserEnter/armoredSamuraiBoss.js与emperorToadBoss.js实际调用LifeFactory.getMonster → setPosition(Point) → map.addMonsterSpawn(mob,7200,-1)。NpcApi原先缺后两接口，本次补齐并复用MapSpawner；源脚本不改写。离图不应清引擎的_spawnRegistered，改为保留对应刷新点；更换角色才清脚本引擎和地图状态。

仍未迁移完整远程地图后台战斗/全部动态怪物、数据库plife热增删、全部事件副本实例，不可将全资源扫描称为完整服务器地图生命周期等价。

## 本次其余确定的代码故障

- AudioManager.TrimBgmCache曾销毁仍绑定淡出AudioSource的旧clip；现在保护两个source，淡出后再回收；停止同时淡出两声道，淡入使用unscaledDeltaTime并尊重静音；URI转义和请求超时；缓存命中协程先yield，避免同步结束后bgmCo被赋成僵尸句柄。
- FieldDirector轮回图腾summoned阶段owner.SortingOrder-1，stand/die曾改用owner.SortingOrder形成同层竞争；三个阶段固定同一值。不是擅改原IMG像素。
- BotRecordedActions原SnapToGround无地面返回旧y，导致录制步行跨断层；改为同一可走foothold区域采样，不可达处停止。BotField插值站走帧按实际地面校正，跳跃/绳梯/椅子不套此修正。
- BotRuntime构造会Root(ReactorTemplates/ReactorDrops)并Join，但登录期预读/就绪检查只包含Items/SoloMapling/Maps。本次补齐两资源，移除这个确定主线程等待来源；尚无Android首次入场耗时测量，不能断言全部卡顿消失。
- admin默认GM6依据用户要求及源V1.0.67__admin.sql；按账号而非角色名。后续GM实现已接入聊天执行器，105个语法有实际分支（含14个部分接入），另95个未迁移。以references/gm-commands.md与docs/gm-command-runtime-audit.md为当前状态，不能按目录条数声称全量完成。

## 实际验证（本次新增）

- tools/audit_wild_bosses.py 全量6337地图解析错误0；链接展开7281条Boss刷新记录。
- Unity FeedbackRegressionChecks：PASS；冷勇士部落map_ms=688.6648，热加载28.8258；这是Editor地图/天气路径，不是Android进场性能结论。
- Unity FieldFeedbackChecks：FIELD_FEEDBACK_PASS wild_boss_initial_spawn live_hp_reentry cooldown_reentry script_spawn_reentry。使用原图100000005与Boss6130101，实际创建NativeController，验证HP与刷新器占用、死亡冷却、脚本注册重入；编辑器用DestroyImmediate拆卸视图。未运行所有Boss战斗。
- 服务端最终离线检查668通过/0失败，覆盖admin新旧角色GM与假人20px断层。Unity Scripts+Editor最终编译0 warning/0 error。
- NPC harness 889条：parseFail0、ok790、runFail27、javaInterop72；pack2779条parseFail0/mangled0。宿主缺口保留，不称全脚本通过。
- 未连接Android设备，未执行APK构建或手机画面/声音/键盘验收。详细项表见docs/feedback-nine-2026-10-31.md。

## 2026-11-02：全地图 BGM 补齐（出岛后静音）

起因：排查出租车传送黑屏时发现 `Sound/Bgm/` 只有 7 个 mp3，进泰国/少林/新加坡等图静音。根因是 `export_sound.py` 旧版只按 `FieldMaps.json`（37 张彩虹岛图）导出；脚本虽已改成读 `Field/FieldIndex.json` 的 `bgms`（264 首，覆盖全部 6337 张图），但**全量分支从未重跑**，索引仍停在 7 首。

音频落地是三段链路，缺一段真机就静音：

1. `Field/FieldIndex.json` → `bgms`（264 首，由 `tools/export_field_index.py` 从全量地图生成）
2. `tools/export_sound.py` 导出 mp3 到 `MapleDashResources/Data/Sound/`，并写 `Assets/Resources/SoundIndex.json`（`AudioManager.Index` 读它，按 `WzAssets.DataRoot + entry.path` 走 `UnityWebRequestMultimedia` 加载）
3. `tools/export_sound.py --register` 把 mp3 登记进 `NativeAssetIndex.json["files"]`（含 sha256），同步 `MapleDashResources/resource-manifest.json`。**`sync_android_data.py` 与 `build_resource_pack.py` 只推送 files 列表里的文件**，mp3 不在源端 WZ 树，`package_native_assets.py` 扫描的是源端目录，所以这一步不能省。

三个必须记住的坑：

- **`BGM06/FinalFight` 大小写笔误**：地图数据写大写，源端文件是 `Bgm06.img`（且确实含 FinalFight）。`image()` 原先直接 `read_bytes()`，单个坏条目抛 `FileNotFoundError` 中断整批 264 首。现由 `image_name()` 大小写归一化解析源文件名，逻辑 key 保持地图原样；真缺失的 img/节点统一记 MISS 跳过，不抛。
- **52 个文件带非 MPEG 前缀**：48 个在 offset 82、4 个在 80（集中在 `BgmCN`/`BgmSG`/`BgmJp`/`BgmJp2` 与部分 `Skill/*/Use`）。`ffprobe` 能跳过后识别成 mp3，但 Unity 的 `AudioType.MPEG` 不保证跳过前导垃圾。现由 `trim_to_mp3()` 扫前 512 字节找首个合法 MPEG 帧头（校验 version/layer/bitrate/samplerate 非保留值）后截断，ID3 标签原样保留。剥离 52 个 / 4256 字节，`file` 已全部识别为 `MPEG ADTS`。
- **过去漏登记 38 个音频**：Direction 系列与 `Bgm04/Shinin'Harbor` 物理存在但从未进 files。本次 `--register` 全量重扫 Sound 树，新增 290 条、Sound 共 369 条；同时加入「清理失效条目」逻辑——残留的失效 mp3 记录会让 `sync_android_data.py` 在推送前直接 `FileNotFoundError` 中断。

`SoundIndex.json` 的 `ms` 字段解析进 `Entry` 但运行时**从未读取**（`AudioManager` 播放依赖 AudioClip 自身长度），40 首 BGM 的 `ms=0` 属源端 `length_ms` 缺失，不影响播放。

覆盖结果：**257 / 264**。7 首是源端 GMS083 本身没有该节点（与「源端即静默」同口径，不做跨 img 猜测映射）：`Bgm03/BygoneGarden`(13 图)、`Bgm03/CavernOfShadows`(6)、`Bgm16/RedWitch`(2)、`Bgm16/RustyThrone`(4)、`Bgm20/Subway`(20，源端 Bgm20 只有 UnderSubway/UnderSubway2)、`Bgm57/Historical Site of Slime`(1)、`BgmEvent/halloweenMainHall`(10)。

验收：369 个物理 mp3 / 369 条 SoundIndex / 369 条 NativeAssetIndex Sound 三向零差异，path 失效 0、sha256 不符 0、遗留前缀 0、manifest 与索引一致；ffprobe 抽样 5 首（含弯引号文件名 `Bgm50_TheWorld’sEnd.mp3`）全部可解码。`unity-compile-check` 0 warning/0 error；`server-check` 首轮 1195/3（CPQ 平局延长用例时序抖动）、复跑 **1198/0**。音频总量 437.36 MiB（BGM 428.54 MiB）。

边界：**未连接 Android 设备**，`MapleDashResources.zip` 当前不存在，要让真机生效仍需跑 `tools/sync_android_data.py`（或先 `build_resource_pack.py` 再 `--zip`）推送；体积增量约 +437 MiB，首次推送会明显变长。

格式坑：`--register` 必须**固定按 `indent=1` 重写** `NativeAssetIndex.json`（仓库既有 21.9 万行格式）。首次实现沿用了 `package_native_assets.py` 的 `separators=(",", ":")` 紧凑输出，把整份索引压成单行，`git diff` 虚增到 21 万行、真实改动被淹没；改为 `indent=1` 后 diff 收敛到 1188 行。`resource-manifest.json` 与索引写同一份 bytes。
