# Android APK 应用内更新

用户已授权此新增移动端功能使用 Android 系统原生弹窗，不属于原端游戏 UI 迁移。

## 发布

1. 仓库必须公开。配置入口固定为 https://gitee.com/lzx_xzl/mxd/raw/master/update.json 。分支改名时同步修改 AppUpdater.java 的 MANIFEST。
2. 应用更新版本取沙盒 files/update.json，与 APK 自身版本独立。文件不存在或无效时按默认版本 1 处理：远端版本为 1 不提示，大于 1 才触发更新。Assets/Resources/update.json 不再用于运行时版本判断。APK versionCode 可以保持 1，但系统不允许降级安装。每次发布递增远端 JSON 的 versionCode。
3. 使用与已安装版本相同的包名和签名密钥构建 APK。在 Gitee Releases 上传 APK，复制实际附件下载链接到远端 apkUrl。必须是公开 HTTPS 下载地址，不能填 Release 页面地址或短期签名跳转地址。
4. 附件上传完成后再提交根目录 update.json，例如：

```json
{
  "versionCode": 2,
  "versionName": "1.0.1",
  "apkUrl": "填写实际公开的 Release APK 附件 HTTPS 链接",
  "forceUpdate": false,
  "updateLog": "修复问题与新增内容"
}
```

versionCode 为数字，forceUpdate 为布尔值，更新日志换行用 JSON 的 \n 转义。版本 1 且 apkUrl 为空表示当前没有更新。

## 行为与边界

启动时在资源加载前检查一次，只比较远端与沙盒 files/update.json 的 versionCode，不读取 APK 版本决定更新，也不校验 APK 版本与 JSON 是否一致。请求连接超时 15 秒、读取超时 30 秒，最多 8 次 HTTPS 重定向，配置大小限制 64 KiB。远端不可达或配置无效时记录日志并继续启动，因此 forceUpdate 不是离线封禁机制。

普通更新可跳过；成功取得有效强更配置后，只能更新或退出。APK 流式下载到应用私有缓存，显示百分比及 MB；服务器未提供长度时显示不定进度。失败清理半成品并支持重试，不支持断点续传。检查 APK 包名及当前签名集合（不支持签名轮换）。不申请新的共享存储权限，不依赖 AndroidX。

下载完自动打开系统安装器。Android 不允许普通应用静默安装；用户必须授权安装未知应用并确认安装。授权返回后继续安装；取消安装后可重新安装。APK 更新不包含 /sdcard/mapstory 独立资源包更新，资源变更仍需原有部署流程。

配置由 HTTPS 保护，无额外清单签名或哈希字段。系统安装器负责最终 APK 完整性与签名校验。请长期保存发布密钥；不同开发机默认 debug 签名通常不同。

## 验证

Java 已针对 Android SDK 35 进行编译检查；Android 条件运行时代码及 Editor 编译检查均为 0 warning / 0 error；完整链路还需设备验收：版本相同/较旧不提示，版本升高提示，普通跳过、强更退出、断网/错误 URL、下载中断重试、错误包名/签名拦截、未知来源授权返回、取消安装重试、同签名覆盖安装后保留存档。Gitee 配置地址已实测可公开读取；测试附件已发布到 update-test-2 Release；安装链路未实机验收。

测试旧 APK 附件时，沙盒 JSON 不存在时按 1 比较，远端版本大于本地有效版本才触发下载。旧 APK 内没有本次更新功能，安装后不会保留此更新检查；此配置仅用于下载/安装链路测试，不代表正式版本 2。

测试远端配置见 [update-test-remote.json](update-test-remote.json)，沙盒文件不存在或版本低于 2 时即可触发远端版本 2。

## 安装成功后同步沙盒版本

下载并校验完成后，先原子写入 files/update-pending.json，记录远端完整 JSON、目标 APK SHA-256 以及安装前 lastUpdateTime。此时不修改 files/update.json。安装后下次启动，在网络请求之前检查安装时间发生变化且当前已安装 APK 的 SHA-256 与目标一致，才原子写入 files/update.json 并清除待安装回执。取消/失败不推进版本；清除应用数据/卸载会清除沙盒版本。此确认不比较 APK versionCode。

新旧 APK 都必须包含更新模块，才能在覆盖安装后执行版本同步。当前已发布测试附件为旧构建，无法验证同步，后续应上传包含更新模块的新 APK。安装成功回执需 Android 真机验收。

更新 UI 使用系统 Theme_Material_Light_Dialog_Alert，统一浅色 Material 弹窗和扁平文字按钮；下载使用原生水平 ProgressBar、独立 MB/百分比与校验状态，不再使用旧 ProgressDialog。具体外观需重新打包在设备上验收。
