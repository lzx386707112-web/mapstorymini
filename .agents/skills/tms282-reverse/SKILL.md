---
name: tms282-reverse
description: TMS282 客户端/服务端逆向与逻辑分析结论沉淀。分析或迁移 282 内容前必读。
---

# tms282-reverse

TMS 282 端（客户端 `~/Documents/mxd/MapleStory`，服务端套件 `~/Documents/mxd/TMS282.5`）的已确定逻辑记录。
规则来源：mxd-android/AGENTS.md 规则 8-12。282 服务端是 swordie 架构 Java 源码（`source/src/main/java/net/swordie/ms`），
**无 js 脚本**（`data/scripts` 为空），原 js 的逻辑全部以 Java 分发实现（`BossManager`/`ScriptManagerImpl`/`CustomFieldScripts`）。

域内细节见 `references/`：

- `lucid-boss.md` — 露希妲（普通 boss 副本 + 變身燃燒 LucidDream）完整逻辑
