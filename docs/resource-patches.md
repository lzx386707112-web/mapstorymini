# Android 资源全量包与补丁

打包入口在 MapleDash 工程里（外层 mxd-android 已不放同名转发脚本）：在 MapleDash 目录执行
`./pack_resources.sh` / `./pack_resource_patch.sh`，或在任意目录用 `MapleDash/pack_resources.sh` 调用。

## 全量发布

```bash
./pack_resources.sh
# 需要从源端重新导出资源时：
./pack_resources.sh --regen
# 同时重新构建 APK：
./pack_resources.sh --apk
```

输出到 `MapleDashResources/`：`MapleDashResources.zip` 和
`MapleDashResources.manifest.json`。每次发布后，将 manifest 快照另存为带版本号的文件，
例如 `releases/v1.manifest.json`。已有全量包可从 ZIP 根目录取出
`resource-manifest.json` 作为旧基线；不要拿已经更新后的本地清单冒充旧版本。

## 分包（默认每包 2 GiB）

Data 已接近 10 GiB，整包会顶到传输和文件系统上限，Android 侧解析超大 ZIP64 也不稳。
打包脚本默认按 2 GiB 切成**互相独立**的分包：

```
MapleDashResources.part01.zip   ← 带 resource-manifest.json（补丁包另带 resource-patch.json）
MapleDashResources.part02.zip
MapleDashResources.part03.zip …
```

- 每个分包本身都是结构完整的 ZIP（根目录 `resource-pack.json` 写明 `index`/`count`），
  手机端逐包解压，不需要先把 9 GiB 拼回整包，也不依赖 ZIP64 大包解析。
- 分包命名固定为 `<包名>.partNN.zip`（两位以上数字序号），全量包与补丁包各自一套前缀。
- 只有一段时输出仍是 `MapleDashResources.zip` / `MapleDashResources.patch.zip`（不会叫 part01）；
  `--part-size 0` 可强制打整包，`--part-size 3G` 之类可自定上限。

```bash
./pack_resources.sh --part-size 0     # 打整包
./pack_resources.sh --part-size 2G    # 默认
```

传输用 `tools/sync_android_data.py --zip` 会把所有分包推到 `/sdcard/mapstory`，
并删掉上一次多出来的旧包（包数对不上时手机端会拒绝解压）。
校验用 `tools/check_resource_pack.py`（也由 `pack_resources.sh` / `pack_resource_patch.sh` 自动调用）：
清单位置、`Data/` 条目、包号是否 1..N 连续、各包声明的包数/类型/清单摘要是否一致、
每包体积是否超上限、清单内文件是否恰好覆盖一次（补丁包用 `--patch`，只查变更文件不要求全覆盖）；
`--deep` 会逐条解压验 CRC。

手机端（`WzResourceParts`）在**开始解压前**统一校验：缺包、包号重复、包号不从 1 开始、
包数与声明不符、第 1 包没有清单，都会直接报错并保留资源包，不会写出半份 Data。
单包与分包同时存在时取修改时间较新的一份，成功后把所有同前缀包一起删除。

## 增量发布

1. 更新 Data 及 `MapleDash/Assets/Resources/NativeAssetIndex.json`（与全量发布使用同一资源导出流程）。
2. 用手机当前安装版本对应的清单生成补丁：

```bash
./pack_resource_patch.sh /绝对路径/releases/v1.manifest.json
# 补丁也可能超过 2 GiB（比如整批 UI/特效重导），同样会自动分包：
./pack_resource_patch.sh releases/v1.manifest.json --part-size 2G
# 指定 Data 目录：
./pack_resource_patch.sh releases/v1.manifest.json --resources /资源目录
```

默认输出 `MapleDashResources/MapleDashResources.patch.zip`（超过上限则
`MapleDashResources.patch.part01.zip…`），仅含新增/修改文件、完整目标清单、补丁标记
（标记和清单都只在第 1 包里）。删除文件由完整目标清单决定。新版本快照为
`MapleDashResources.patch.manifest.json`，发布后另存为 v2，下一次与 v2 比较。
脚本末尾会跑一遍 `check_resource_pack.py --patch` 自检补丁包结构。

3. 手动部署补丁时，资源清单改变需重新构建并安装对应 APK。在线更新见下节，可使用补丁自带目标清单独立更新资源。
4. 把补丁放进手机 `/sdcard/mapstory/`（单包 `MapleDashResources.patch.zip`，或全部分包
   `MapleDashResources.patch.partNN.zip`），重启游戏。缺一个分包就会在开始解压前报错。
   首次使用本功能须先安装含补丁安装逻辑的新 APK；首次安装资源仍需全量包。

## 安装与恢复

loading 优先处理补丁，再走原来的资源抽样/全量安装流程。
补丁中缺省的文件从手机 Data 复制到 Installing，手动部署时所有目标文件逐个 SHA-256 校验；
只在完整校验后切换目录并写入回执，旧版本多余文件不会进入新 Data。
因此手动部署补丁节省传输体积，安装仍需额外一份完整 Data 的临时空间和全量校验时间；在线补丁使用下节增量事务。
基线摘要保存在补丁元数据供追溯；实际兼容性以每个目标文件的哈希为准，
不要求手机旧回执一定存在。底包缺少所需文件、损坏、补丁或 APK 版本不匹配时，
保留旧 Data 和压缩包并显示失败，修正后可重试。

全量包/补丁均在安装切换及回执写入成功后自动删除；失败不会删除。
若系统拒绝删除，不回滚已成功的资源安装。不要同时放不同版本的全量包与补丁。
安装采用现有 PreviousData 目录恢复机制；并非跨进程断电的完整文件系统事务。

## 验证

```bash
cd MapleDash # 若已在仓库根目录则省略
 dotnet run --project tools/resource-installer-check -c Release
```

检查流式哈希、空文件、中断恢复、损坏包拒绝、目录切换失败回滚、补丁增删/复用、
错误 APK/损坏底包拒绝，以及成功删除/失败保留压缩包；分包相关覆盖逐包解压、
缺包/包号重复/包号不从 1 开始时拒绝且不写 Data、单包与分包混放取较新的一份。未替代 Android 实机验收。

## Gitee 在线资源更新

仓库 master 根目录创建 `resUpdate.json`（严格 JSON，最后一个字段后不能有逗号）：

```json
{
  "versionCode": 1,
  "resUrl": ""
}
```

分包发布时用 `resParts` 代替 `resUrl`（数组，按包号顺序；单个补丁也可以继续用 `resUrl`）：

```json
{
  "versionCode": 2,
  "resUrl": "",
  "resParts": ["https://…/MapleDashResources.patch.part01.zip",
               "https://…/MapleDashResources.patch.part02.zip"]
}
```

`resParts` 的每个地址都会依次下载到沙盒再复制到 `/sdcard/mapstory/MapleDashResources.patch.partNN.zip`；
全部到位后才一次性改名成正式文件名，中途被杀/失败不会给 C# 留下缺包的集合。
下载前会清掉上一次残留的整包与多余分包，避免「包数对不上」。

入口为 https://gitee.com/lzx_xzl/mxd/raw/master/resUpdate.json 。只用这三个字段：`versionCode` 必填，
`resUrl`（单包）与 `resParts`（分包数组）至少填一个，两个都有时以 `resParts` 为准。沙盒 files/resUpdate.json 不存在或无效时默认版本 1，因此远端 1 不提示；远端只高 1 时显示与 APK 更新一致的 Material 弹窗，可下载或暂不更新。远端高出超过 1 判定为跨版本，显示当前/最新资源版本并提示“请联系群主手动更新资源包”，仅允许退出，不下载、不推进资源版本、不继续 APK 更新。检查失败不阻断原有资源加载。启动顺序：存储授权 → 资源更新检查及安装 → APK 更新检查及安装 → 资源校验/加载 → 假人预热与进入游戏。两者都有更新时优先处理资源；若用户主动选择暂不更新资源，则继续 APK 检查。资源下载/安装失败时重试或退出，不会提前弹出 APK 更新。

先用 `./pack_resource_patch.sh` 生成补丁并上传公开 Gitee Releases 附件，再递增远端 versionCode、把 `resUrl`（单包）或 `resParts`（全部分包，按包号顺序）填成公开 HTTPS 下载直链。也支持同格式全量包。不能使用任意散文件 ZIP：必须包含 resource-manifest.json（完整目标清单），增量包另含 resource-patch.json 和所需 Data/ 文件。在线更新只允许相邻版本补丁；跨版本用户联系群主手动部署匹配的全量资源与版本回执（Data/.resUpdate.json，和远端相同的两个字段）。补丁未携带文件必须在玩家 Data 中具有与目标一致的哈希。

下载前删除旧的沙盒下载（`cache/resource-update-N.zip`），每个分包流式下载后以 64 KiB 缓冲复制到
`/sdcard/mapstory/MapleDashResources.patch.partNN.zip.part`；同步落盘、长度一致才算这一包到位。
所有分包都到位后才一次性改名成正式文件名（单包则是 `MapleDashResources.patch.zip`），
中途被杀或失败只留下 `.part` 半成品，C# 不会拿缺包的集合去安装。复制不绕过权限，沿用已有 Android 存储授权。
失败可重试或退出；开始更新后不允许带着待安装的补丁跳过进入游戏。安装成功清理所有包；失败保留正式包，
下载半成品与复制半成品会清理。不支持断点续传。

在线安装使用补丁内目标清单：旧安装回执与旧清单匹配时，哈希记录未变化的旧文件仅复制，不重新计算哈希；补丁内新增/修改文件始终逐个 SHA-256 校验。没有有效旧安装回执时回退全量校验。缺失文件、被校验文件损坏或基线不适配时不切换 Data。Data/.resource-manifest.json 与 Data/.resUpdate.json 随 Data 目录一起切换，成功后将回执原子写入沙盒 files/resUpdate.json。下次启动先同步 Data 回执，可恢复切换成功但尚未写沙盒时的中断。启动资源验证与 WzAssets 索引使用已安装清单，使新增/删除资源无需仅为清单重打 APK。补丁不能更新 C# 代码、Resources 内嵌脚本/数据库等 APK 内容；这些变化仍需 APK 更新。

沙盒/外部目录需分别容纳下载 ZIP；在线补丁额外空间主要为变更文件暂存与受影响旧文件备份，全量安装仍需完整 Data 暂存空间。HTTPS 与包内文件哈希用于传输/内容校验，未额外引入资源发布签名。仅测试工具验证安装/回执切换与回滚，在线下载、权限与 Android 启动全链路仍需设备验收。

安装/校验弹窗每 100 ms 刷新，阶段文案同步安装器；校验阶段按字节、应用阶段按操作数计算百分比，最终等待版本回执提交成功才关闭。

在线补丁复用模式信任既有安装回执，因此不保证在本次更新时发现未变化旧文件后来被外部修改的情况；后续启动保留原有抽样校验及失败时完整校验。安装百分比包含旧文件复制与新文件校验，阶段文案区分两者；旧 Data 仍复制到 Installing，以保留失败回滚，存储 I/O 时间不会完全消失。

## 在线补丁增量事务安装（当前实现）

在线 patch.zip 不再复制整个 Data。先解压并 SHA-256 校验补丁携带文件到 ResourcePatchTransaction/new；未携带文件要求旧清单哈希与目标一致且文件存在。旧安装回执有效时不读取未变化资源内容；回执无效时只补校验旧文件，不复制旧文件。全量 ZIP 与手动补丁保留原目录安装流程。

验证完成后生成持久安装日志，列出变更、删除、目标清单和版本/安装回执，以及每项原先是否存在；日志落盘前不修改 Data。只将受影响旧文件移动到同文件系统内的 backup，再移动新文件到原位。未变化资源不移动、不复制、不重新哈希。异常逆序恢复，新增文件删除、修改/删除文件从备份恢复，版本与清单一起回滚。提交标记完成后只清理备份；清理前先重命名整个事务目录，防止清理被杀导致提交状态丢失。启动在网络更新/版本同步之前先恢复未提交事务，恢复失败禁止进入游戏。

校验进度按补丁解压字节计算（没有可信旧回执时包含旧资源校验字节），应用阶段重置进度按受影响文件操作数显示。零字节文件仍校验空内容哈希。进度不会再以全部旧资源文件数作为安装分母。

已用测试覆盖未变化文件时间戳保留、坏补丁拒绝、提交前错误回滚、模拟进程中断恢复、重复恢复、已提交事务清理。FileStream.Flush(true) 与同文件系统重命名用于减少中断窗口；共享存储/FUSE/设备突然掉电的实际持久性仍需真机验证，不承诺跨文件系统或硬件故障的绝对原子性。

完成阶段优化：已恢复或已提交事务先改名隔离，旧备份递归删除改为后台执行，不再阻塞安装弹窗。校验/应用阶段最多显示 99%，提交成功返回后才显示完成；本次刚完成在线安装时跳过启动重复抽样（后续启动仍保留）。APK 检查和 AI 假人预热属于随后独立阶段，不属于资源安装进度。

## 当前简化模式（用户要求，覆盖上面的旧校验说明）

全量资源 ZIP 在存储授权后、在线补丁检查前直接流式解压到 Data。不比较 APK 内嵌清单、不逐文件计算 SHA-256、不在进入游戏前抽样或全量校验。ZIP 的 resource-manifest.json 仅用于加载资源索引。已有 Data 无全量 ZIP 时直接加载。解压失败保留 ZIP 供重新解压；直接解压不提供旧 Data 的事务回滚。

在线/手动增量补丁也不重新计算新旧资源哈希、不检查旧文件内容和清单哈希是否匹配；保留受影响文件的增量备份及事务恢复，保留 ZIP 路径范围检查。错误或缺失资源可能直到游戏读取时才暴露。进度文案改为“解压/安装”，不再称为哈希校验。
