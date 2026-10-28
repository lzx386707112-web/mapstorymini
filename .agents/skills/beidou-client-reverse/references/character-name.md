# 建号名字校验与错误反馈

## 已核实服务端规则（2026-10-28）

依据 `Beidou-Server/gms-server/src/main/java/org/gms/client/Character.java:1022` 的 `canCreateChar`，SHA256：`b72a138f09a8dadbb88faad54c2f14c1668d74608f1a59b1c394af55b0bce62a`。本条为 Java 源码核验，不涉及 EXE 地址。

调用链：`CheckCharNameHandler` → `Character.canCreateChar` → `existName`；`CharacterFactory` 创建时再次校验。格式为 `[a-zA-Z0-9\u4e00-\u9fa5]{2,12}`，允许 2～12 个中文、ASCII 英文或数字；还检查 `ServerConstants.BLOCKED_NAMES` 和已有名字。旧移植的“4～13 位英文数字”与当前源端不符。

复现：搜索 `Character.java` 的 `canCreateChar` 和 `CheckCharNameHandler.java`。格式允许“中文”“中文Ab12”，拒绝 1 字、13 字及标点。

## 当前迁移及验证边界

`CharNameRules.Valid` 对齐格式，供 `GameSession`、DAO 和创建流程共用。敏感词过滤尚未迁移。`LoginFlow.ConfirmCharName/DoCreate` 的格式错误、重名及创建失败改走现有 `SetAlert`，避免只在页面底部显示状态文字。

复用已有 `DrawAlertDialog`，未新增布局或素材：`UI/Login.img/Notice/backgrnd/0`（362×219），父窗口居中；无对应图文时正文相对 (121,48)、219×78；`Notice/BtYes` 相对 (143,168)、75×32。现有布局依据见 `docs/login-ui-source-analysis.md` 的提示窗分析。未核实 EXE 建号错误到 `Notice/text/<n>` 的专用映射，不指定图片编号，也不声称原端映射已还原。

回归入口：`tools/server-check` 中中文、混合名、12/13 字边界及非法字符用例。Android 键盘提交和弹窗显示仍须实机验收。

本次验证：`dotnet run --project MapleDash/tools/server-check -c Release` 中上述格式边界及建号/重名用例全部通过；完整套件后续在 SoloMapling 检查因 `Bot resource invalid: Bot/SoloMapling` 中止，不能视为全套通过。`git diff --check` 通过。
