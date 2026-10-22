# 安装包与资源体积审计（2026-10-22）

## 结论与范围

发现迁移遗留资源、重复资源及未调用代码。初次审计仅只读分析；随后按用户指令执行的清理见下方“已执行清理”。构建配置未变更。

缓存APK：`Library/Bee/Android/Prj/IL2CPP/Gradle/launcher/build/outputs/apk/release/launcher-release.apk`，修改时间2026-10-22 09:18，大小111.548 MiB。`Builds/MapleDash.apk` 当前不存在，所以此APK仅用于已有构建的实测，不代表包含随后HUD修改的新包。

统计口径：MiB=1048576字节；下表“包内占用”是ZIP条目压缩后大小。当前Resources源文件逻辑大小101.18 MiB，排除.meta；du的129MB还包含文件系统块开销等，不能直接当成APK大小。

## APK实际组成

| 类别 | 包内占用MiB | 说明 |
| --- | ---: | --- |
| 地图 | 52.602 | field 28.332，stage 16.583，back 7.409，tiles 0.278 |
| 当前NativeSkillVfx特效 | 15.732 | 实际调用中的完整技能分支 |
| 旧skills特效 | 13.602 | 旧manifest体系，优先清理候选 |
| ARM64原生库 | 13.488 | libunity 9.981，libil2cpp 3.504，其余很小 |
| 中文字体 | 5.950 | ui.ttf源文件8.115 MiB，完整字体数据入包 |
| 角色 | 4.775 | 不同职业/外观/动作，需要按实际需求保留 |
| 召唤物 | 1.590 | NativeSummon仍使用，不能误删 |
| 其他 | 约3.81 | Unity元数据、怪物、UI、图标、JSON、ZIP目录等 |

Unity Data总计压缩后97.284 MiB。仅ARM64，BuildOptions.None；不是双架构或Development包造成的翻倍。

## 优先清理候选

### 1. 旧技能帧与旧弹道代码

- `Assets/Resources/Art/skills`：源文件13.930 MiB；缓存APK中13.602 MiB，910条资源。
- `Assets/Scripts/SkillBall.cs`：只有定义和自身AddComponent，没有其他代码调用Spawn；其GUID在场景/prefab/asset中未发现引用。
- `Art.SkillIcon()`没有调用；`Art.GetClip("skills",...)`仅出现在上述旧SkillBall中。
- 当前SkillFx、SkillVisuals使用NativeSkillVfx和GameplaySkillVfx；当前技能窗图标使用skillicon。
- 清理时应同时移除manifest.skills旧动画声明及其无用加载入口，并核对导出脚本，防止下次导出重新产生旧资源。仅“manifest里有字符串”并不代表资源会实际显示；但在完成引用迁移和技能验收前，不宜单独删图。

### 2. 旧独立背景与地砖素材

- `Assets/Resources/Art/map/back`：源文件7.414 MiB，缓存APK7.409 MiB。
- `Assets/Resources/Art/map/tiles`：源文件0.268 MiB，缓存APK0.278 MiB。
- `Art.BackLayers()`、`Art.TileGroup()`没有调用点；这两组素材路径仅留在manifest.backs/tiles声明中。
- 当前关卡Backdrop使用manifest.stages，实际路径是map/stage；原版地图使用map/field和其_back子目录，不能混为同一组删除。
- 清理应同步移除旧backs/tiles声明及无用读取接口，保留stage/field运行路径。

以上三组资源在现有APK中的占用合计约21.29 MiB。若核对后全部移出打包目录，粗略估计111.55 MiB可降至约90.3 MiB；这是同一构建条件下的推算，尚未重建验证。

### 3. 内容重复，但不能直接删路径

对Resources中的.bytes进行SHA-256比较：444组完全相同内容，冗余源字节合计6.424 MiB。该数值与上述旧资源有交叉，不能简单相加，也不是已测得的APK节省量。

例如：

- map/field/1010003/0_0.bytes 与 map/field/1000003/0_0.bytes：约0.458 MiB。
- map/field/60001/0_0.bytes 与 map/field/2000001/0_0.bytes：约0.445 MiB。
- map/field/1000001/0_0.bytes 与 map/field/1010001/0_0.bytes：约0.444 MiB。
- map/field/30001/0_0.bytes 与 map/field/20001/0_0.bytes：约0.272 MiB。

这些路径可能被不同地图或动作分别引用，去重需把引用统一到共享文件，并保留各自origin、delay等元数据。不同路径的相同二进制在缓存APK中仍分别打包，不能依赖Unity自动去重。

### 4. 少量无效配置与资源备份

`Assets/Resources/UI/BarLayout.json`没有运行读取，内容还是旧布局说明。`bar_lv_full.bytes`仅由Editor检查读取，是原始底板备份；quickSlot32等资源未见当前运行使用。它们很小，不是体积主因；开发参考应移至Resources之外，保留所需检查与导出来源。

## 体积原因与下一步顺序

1. 所有素材放在Resources，未使用也会入包。先剔除已核实的旧资源，收益最明确。
2. PNG作为.bytes由TextAsset原样打包，运行时LoadImage；Unity ASTC/ETC贴图导入压缩不会作用于它们。不能只改TextureImporter就期待包体变小。后续可评估PNG无损优化、共享资源引用或资源加载方案迁移。
3. ui.ttf完整字体打包可通过保留中文所需字符的字体子集优化；昵称允许用户自定义，不能只按现有界面文字裁字，否则新昵称可能缺字。字体当前实际压缩占5.95 MiB，节省量须用具体方案测量。
4. BuildToDevice明确关闭stripEngineCode和ManagedStripping，注释记录过IMGUI/GUISkin裁剪问题。可以另行评估保留规则后开启裁剪，但不能直接勾选发布；此项暂不估算收益。
5. 旧关卡模式仍可达，map/stage的16.65 MiB源文件不能仅因原版地图模式已加入就当作废弃。若产品决定彻底移除旧模式，才能连同入口、数据和资源一起处理。

## 只占开发磁盘、不影响APK的文件

Library约2.6GiB，是导入/编译/构建缓存；docs约20MiB、Builds约101MiB、.git约101MiB。它们不作为普通项目资源打入APK。清理Library只能省本机空间，且会导致重新导入和编译，不能让安装包变小。Editor测试脚本、工具脚本、文档截图也不是APK资源大头。

## 复核数据

- `Library/ResourceAudit/apk-entries.json`：APK条目、源资源GUID映射、压缩与未压缩字节数。
- `Library/ResourceAudit/duplicate-resources.json`：444组完全重复资源路径。

分析依据是当前源码、序列化资源引用、资源清单及已存在APK；没有执行删除、构建或安装，未将静态无调用判断表述为已通过运行回归。

## 已执行清理

用户确认删除后，移除了以下运行时无调用的文件和对应.meta：

- Art/skills旧技能帧、map/back旧背景、map/tiles旧地砖。
- SkillBall.cs旧弹道组件，以及Art中旧SkillIcon、BackLayers、TileGroup接口和加载逻辑。
- UI/BarLayout.json旧布局说明、bar_lv_full底板备份、status_plate重复底板和未使用的quickSlot32。
- manifest移除backs、tiles和skills旧帧路径。skills仅保留name/id/actions，供export_gameplay、export_skill_vfx、wz_actions原版数据导出使用，不再加载旧图。当前导出脚本只生成NativeSkillVfx，不会生成被删的旧skills目录。
- FieldChecks的完整底板检查改为仍在实际使用的backgrnd2_full，不再依赖资源备份。

共删除1071个非.meta文件；Resources逻辑大小由101.181降至79.479 MiB，减少21.702 MiB（包含清单精简）。所有仍有运行引用的重复帧、4套关卡主题、37张原版地图、新版技能特效、中文字体均保留。

验证：检查2989处JSON图片引用，无缺失或指向已删除目录的引用；2700张去重后的引用图片全部通过PNG完整性校验；Unity SkillVfxChecks通过76段动画、942帧的加载及轴心验证；HudChecks通过223项，0失败。未重新构建APK，缓存旧APK不会因删源码自动缩小；下一次构建预计减少约21MiB，以新包测量为准。删除清单保存在Library/ResourceAudit/cleanup.json。
