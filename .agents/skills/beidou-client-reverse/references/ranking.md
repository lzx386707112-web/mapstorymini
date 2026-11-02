# 排行榜 Ranking

源端 `gms-server`：

| 位置 | 作用 |
| --- | --- |
| `net/server/task/RankingLoginTask.java` | 每 **1 小时**重算 `rank` / `rankMove` / `jobRank` / `jobRankMove` |
| `net/server/task/RankingCommandTask.java` | 每 **5 分钟**刷新「榜单」top 列表缓存 |
| `net/server/Server.java:635-655` | `getWorldPlayerRanking` / `reloadWorldsPlayerRanking` |
| `service/CharacterService.java:276-311` | `getWorldsRankPlayers` / `getWorldRankPlayers`，各区前 50 |
| `net/server/guild/GuildPackets.java:314` | `showPlayerRanks` → `GUILD_OPERATION 0x49`，**只发 name + level** |

本项目：`Assets/Scripts/Server/World/NativeRankingService.cs` +
`Assets/Scripts/NativeRankingWindow.cs`，回归 `tools/server-check/RankingChecks.cs`。

## 两套排序不能合并（最容易踩的坑）

- **榜单**（`getWorldRankPlayers`）：`level DESC, exp DESC, lastExpGainTime ASC`，`LIMIT 50`。
- **名次**（`updateRanking`）：在上面三条之后**再**比 `fame DESC, meso DESC`。

所以「列表第 4 位显示自己第 5 名」在同级同经验时是**正常**源端行为，不是 bug，不要「修正」。
完全并列时源端顺序由 MySQL 决定，本项目用 `id ASC` 兜底保证可复现。

全服模式（`use_whole_server_ranking=true`）下 `CompareWholeTop` 先比 `world ASC` 再走 `CompareTop`，
并且要求 `0 <= world <= worlds-1`；逐区模式只取 `world == 自己的 world`。

## 过滤条件

- `gm >= 2` 的角色**不进榜**，也不参与名次（源端 `WHERE gm < 2`）。
- 榜单额外 `LEFT JOIN accounts`：`banned = 0 OR tempban IS NULL`；被封号的角色不显示。
- 名次重算**没有**封号过滤（源端 `updateRanking` 只按 world + job 分组）。
- 单机语义：`characters` 表里的真角色才进榜，假人是内存对象不落库 → 天然不出现。

## rankMove 累加规则（反直觉）

```java
rankMove = (account.lastlogin < lastUpdate || account.loggedin > 0) ? 旧rankMove : 0;
rankMove += 旧rank - 新rank;
```

- 上一轮重算之后**账号没登录过**、或者**正在线上** → 保留上一轮的升降值继续累加；
- 否则（中间登录过）→ 从 0 重新算。
- `use_refresh_rank_move`（默认 **true**）为真时，每轮先把 `rankMove`/`jobRankMove` 全部清零再累加。

`lastUpdate` 是 `RankingLoginTask.lastUpdate`（进程启动时刻，每轮成功后更新），
**不是**当前时间——别写成 `now`。

## 配置默认值

| 键 | 默认 |
| --- | --- |
| `use_whole_server_ranking` | **false** |
| `use_refresh_rank_move` | **true** |

## 包格式

- 请求：`GUILD_OPERATION 0x49`（`showPlayerRanks`）走 `rank` 页；职业页另有 jobGroup 参数。
  本项目的 `RankingRequest.jobGroup`：`-1` = 综合，`>=0` = `job / 100` 职业群。
- 响应只含 `name` + `level`（+ 自己的 rank/rankMove/jobRank/jobRankMove/level/job）。
  **没有** fame、meso、头像、公会等字段，窗口别画。

## 窗口素材

`UI/UIWindow.img/Ranking/`：

- `backgrnd` **295x335**，顶部印着「MY RANKING」白条 + 蓝色标签带 + 一条红线。
- `Tab/enabled|disabled`（实际节点名 `Tab/enabled/0`、`Tab/disabled/0`）**只有 0 号一张**，
  尺寸 53x14，**图上没有文字** → 页签底板用真素材，文字另外叠（中文「综合 / 职业」）。
- 列表排在红线以下，一屏 20 行。
- EXE 字符串池里没有 `UI/UIWindow.img/Ranking/...` 字面量（走 `%s` 拼接），
  按钮/文字坐标没有反汇编依据，按底图空区重排。

入口：底部菜单 `BtRanking`（`GameWindows.ClickInterface(index == 10)`）。
迁移前它是死按钮（`BottomMenu.Enabled=false` + `DisabledHint`），现在已恒 `true`。
