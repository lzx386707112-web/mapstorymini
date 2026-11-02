# Android聊天发送与主角气泡修复（2026-10-31）

## 缺口与修改

1. 旧DrawChat先消费排队Enter、清掉草稿并关闭键盘，之后才在Repaint读取输入法text。最终组字与Enter/Done同时到达存在丢失窗口。改为每个GUI事件先读键盘快照，再消费Enter；共用FinishChat，Done和排队Enter合并为一次发送。
2. 成功普通All聊天只有AddChat，没有主角气泡；原BotField气泡只为假人View取锚点。现创建自己的气泡，复用UI/ChatBalloon.img/0原图绘制和5000ms寿命，从主角真实SpriteAnim边界取头顶；假人尚未预热/同步也可显示。
3. 不把命令、发送失败和非All聊天显示为普通地图发言；失败仍在聊天窗用既有粉色提示。地图Clear移除气泡。

## 验证

- dotnet Unity编译检查0错误0警告。
- 隔离Unity NativePetSceneChecks最终 **53项通过**，进程退出0；包含前次41项宠物及掉落接线回归和本次聊天回显/气泡/键盘状态检查。日志scene-checks.log。
- 新增测试不只检查焦点：断言真正通过Social服务后的聊天记录全文与数量、自己的气泡文本、Android最终组字覆盖旧草稿、Done+Enter同帧只发送一次且不重开聊天、Visible+屏幕Enter采用最新文本、Canceled不发送、原IMG加载、地图清理。
- 已目视确认截图同时可见聊天记录和主角原IMG气泡；隔离场景使用临时DB，不写正式角色存档。
- Android adb devices当前无连接设备。键盘状态测试是对Unity API快照的注入回归，不是Android实机输入法验证。

原端依据与迁移边界见 .agents/skills/beidou-client-reverse/references/chat-and-system-messages.md 和 field-portal-nametag-bubble.md。
## APK构建

Unity IL2CPP / ARM64 构建成功，166秒，0错误。ZIP完整性及ARM64库检查通过。

- 文件：`Builds/MapleDash-chat-send-20261031-1228.apk`（另有标准路径Builds/MapleDash.apk）
- 时间：2026-10-31T12:28:46.823304+08:00
- 大小：41357746字节
- SHA256：`1cb3f0b23fd0f069df45238c1d573095b47926d5ef0d7d22ff5f0dc1d59910d8`
- 原Data仍按现有工程独立部署；未连接设备，未进行手机安装/验收。

