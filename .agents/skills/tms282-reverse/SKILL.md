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
- `skill-system.md` — **技能子系统**：数据双层拆分（`.ms` 定义层 + `_Canvas` 像素层）、
  `common` 公式 schema vs `level` 旧 schema、公式 DSL 的 `logN` 取整语义、25 族特效层、
  MCV0（VP9 双流 + alpha）容器、EXE 关键函数 RVA（`0x589d140`/`0xdc5c80`/`0x111e390`…）、
  两套框架（legacy `SkillSystem::` vs `WZ2`）、5 条已证伪假设。**五/六转共 1,618 条技能，
  全量正文在 `mxd-android/docs/tms282-skill/`（6 篇 + 3 份数据集）**，本 skill 只留开工所需事实。
