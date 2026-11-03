# Android 资源全量包与补丁

从仓库根目录 MapleDash 或外层 mxd-android 根目录均可运行打包入口。

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

## 增量发布

1. 更新 Data 及 `MapleDash/Assets/Resources/NativeAssetIndex.json`（与全量发布使用同一资源导出流程）。
2. 用手机当前安装版本对应的清单生成补丁：

```bash
./pack_resource_patch.sh /绝对路径/releases/v1.manifest.json
```

默认输出 `MapleDashResources/MapleDashResources.patch.zip`，仅含新增/修改文件、完整目标清单、
补丁标记。删除文件由完整目标清单决定。新版本快照为
`MapleDashResources.patch.manifest.json`，发布后另存为 v2，下一次与 v2 比较。
可追加 `--resources /资源目录` 指定 Data 所在目录。

3. 资源清单改变时，重新构建并安装对应 APK；补丁不能绕过 APK 内嵌清单校验。
4. 把补丁放到手机 `/sdcard/mapstory/MapleDashResources.patch.zip`，重启游戏。
   首次使用本功能须先安装含补丁安装逻辑的新 APK；首次安装资源仍需全量包。

## 安装与恢复

loading 优先处理补丁，再走原来的资源抽样/全量安装流程。
补丁中缺省的文件从手机 Data 复制到 Installing，所有目标文件逐个 SHA-256 校验；
只在完整校验后切换目录并写入回执，旧版本多余文件不会进入新 Data。
因此补丁节省传输体积，安装仍需额外一份完整 Data 的临时空间和全量校验时间。
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
错误 APK/损坏底包拒绝，以及成功删除/失败保留压缩包。未替代 Android 实机验收。
