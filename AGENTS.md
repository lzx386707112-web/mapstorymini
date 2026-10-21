# 项目协作说明

## 地图与角色迁移 skill

处理原版 WZ 地图迁移、地图显示比例与相机、人物脚底锚点、平台碰撞或绳梯动作时，先读取项目内的 skill：

[mapledash-map-port](.agents/skills/mapledash-map-port/SKILL.md)

按任务需要读取该 skill 引用的实现约定和迁移流程。其他 IDE 即使不支持自动发现 skill，也可直接读取这些 Markdown 文件。

本机额外 shell 约定在 `/Users/lizixian/.codex/RTK.md`；该文件存在时遵循，其他环境使用当地可用工具。
