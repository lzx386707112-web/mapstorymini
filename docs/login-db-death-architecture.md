# 登录 / 数据库 / 死亡复活 / 前后端分层 —— 源端逻辑分析与落地方案

> 分析对象：`~/Documents/mxd/BeiDou-Server`（gms-server Java 服务端 + clien 客户端 + scripts-zh-CN 脚本）。
> 本文只做「源端怎么做的」+「我们怎么一比一落地」，不改任何代码。所有结论都带 `文件:行号` 依据。

---

## 0. 结论速览

| 需求 | 源端机制 | 落地要点 |
|---|---|---|
| 登录流程 | LoginServer(独立端口 19696) → 账号密码/自动注册 → 世界列表 → 频道 → 角色列表 → 选角进频道 | 客户端做 5 个 UI 状态；服务端做成纯 C# 模块，进程内直连 |
| 数据库 | MySQL `beidou`，Flyway 220 个迁移；`accounts` + `characters` + 12 张角色从表 | 表结构逐字段复刻成 `Schema.sql`，本地用无依赖嵌入式存储，将来可换真库 |
| 死亡复活 | HP 归零 → `playerDead()` 扣经验/清 buff → 客户端 `dead` 动作 + `raise` 弹窗 → `CHANGE_MAP(dying=1)` → `respawn(returnMap)` 回城 50 血 | 死亡动画→墓碑→确认弹窗→回最近城镇，规则逐条对齐 |
| 前后端分层 | LoginServer / World / ChannelServer 三进程 + DB | `Assets/Scripts/Server`（纯 C#）+ `Assets/Scripts/Client`，协议按源端 opcode 语义建模 |

---

## 1. 源端整体架构

### 1.1 进程/端口

```
BeiDou.exe（客户端，UI.wz 驱动）
   │  TCP 19696（application.yml:66 login-port）
   ▼
LoginServer（net/netty/LoginServer.java，WORLD_ID=-1 / CHANNEL_ID=-1）
   │  登录、世界/频道列表、角色列表、建号、选角
   ▼  getServerIP() 把频道地址回给客户端（CharSelectedHandler.java:105）
ChannelServer × N（每个 world 每 channel 一个）
   │  全部游戏逻辑：移动/战斗/掉宝/地图/脚本
   ▼
MySQL beidou（application.yml:39-42，root/root）
```

- 登录服务器与频道服务器**共用同一套 `PacketProcessor`**，按 `channel < 0` 切换注册哪套 handler（`net/PacketProcessor.java:139-166`）。
- 世界/频道配置在 DB 的 `world_prop` 表（`db/migration/V1.3.0__create_world_prop.sql`）：`channel_size`、`exp_rate`、`flag`（0 普通/1 活动/2 新区/3 热门）、滚动消息等。
- 版本：`ServerConstants.VERSION = 83`，北斗版本 `1.10`。

### 1.2 包序（登录→进游戏）

| # | 客户端 → 服务端 | 服务端处理器 | 服务端 → 客户端 |
|---|---|---|---|
| 1 | `LOGIN_PASSWORD`(账号, 密码, hwid) | `LoginPasswordHandler` | `getAuthSuccess` / `getLoginFailed(码)` |
| 2 | `SERVERLIST_REQUEST` | `ServerlistRequestHandler` | 世界+频道列表 |
| 3 | `CHARLIST_REQUEST`(world, channel) | `CharlistRequestHandler` | 角色列表（含 `characterslots` 上限） |
| 4a | `CHECK_CHAR_NAME` | `CheckCharNameHandler` | 名是否可用 |
| 4b | `CREATE_CHAR`(名, 职业群, 脸/发/肤/衣/裤/鞋/武器, 性别) | `CreateCharHandler` → `BeginnerCreator` | `addNewCharEntry` |
| 5 | `CHAR_SELECT`(charId) | `CharSelectedHandler` | `getServerIP`（频道 ip/port） |
| 6 | 连频道，`PLAYER_LOGGEDIN` | `PlayerLoggedinHandler` | 全量角色数据（属性/背包/技能/任务/键位） |
| 7 | 游戏中 `CHANGE_MAP`(dying=1, returnMap) | `ChangeMapHandler:116` | `respawn(returnMap)` 回城 |

注册表在 `PacketProcessor.registerLoginHandlers()`（`net/PacketProcessor.java:143-165`），一共 21 个登录态包。
频道态包 60+ 个，注册在 `registerChannelHandlers()`。

### 1.3 登录细节（LoginPasswordHandler.java）

- 包体：`login`(string) + `pwd`(string) + skip 6 字节 + `hwid`(4 字节)（`:62-68`）。
- `c.login(login, pwd, hwid)` 返回码：0 成功、5 账号不存在、3 永久封禁、7 已在线（顶号）、6 密码错误。
- **自动注册**：`automatic_register = true`（`V1.7.0__create_game_config.sql:225`）时，loginok==5 直接 INSERT accounts，密码 BCrypt(12) 哈希，记录注册 IP/MAC（`:75-114`）。
- 单 IP/MAC 注册数量上限 `max_accounts_per_ip_or_mac`（默认 0 = 不限）。
- 密码迁移：`bcrypt_migration` 开启时旧 SHA512 哈希自动升级（`:116-128`）。
- 登录成功后 `finishLogin()` → `checkChar(accID)` → `getAuthSuccess` + `registerLoginState`（`:150-155`）。

### 1.4 建号细节（CreateCharHandler + CharacterFactory）

**CreateCharHandler.java:41-115**：包体顺序
`name, job(int), face, hair, hairColor, skinColor, top, bottom, shoes, weapon, gender(byte)`

- `job` 是**职业群**不是职业 id：`0`=骑士团、`1`=冒险家、`2`=战神（`:101-115`）。
- 受 `enable_knights_of_cygnus` / `enable_adventurers` / `enable_the_lord_of_war` 开关控制（默认骑士团 **false**，我们落地按 true 处理，因为用户要求含骑士团）。
- 脸/发/发色/肤/衣/裤/鞋/武器全部走 `ItemConstants.isNewCharDefault*` 白名单校验，非法直接踢（`:55-94`）。
- 创建成功走 `CharacterFactory.createNewCharacter`（`client/creator/CharacterFactory.java:42-108`）。

**初始数值**（`Character.getDefault`，`client/Character.java:581-623`）：

| 字段 | 值 |
|---|---|
| hp/maxhp | 50 / 50 |
| mp/maxmp | 5 / 5 |
| str/dex/int/luk | 12 / 5 / 4 / 4 |
| level / job | 1 / BEGINNER(0) |
| 背包槽 | EQUIP/USE/SETUP/ETC 各 24 |
| keymap | `use_custom_keyset` 选默认键位表（`:604-612`） |
| trock/viptrock | 5 + 10 个空位（`:614-620`） |

> `CharacterFactoryRecipe` 构造器（`CharacterFactoryRecipe.java:53-64`）在 `use_starting_ap_4=false` + `use_auto_assign_starters_ap=true` 时把 str=12、dex=5（与 getDefault 一致）；若关自动分配则 ap=9。

**初始地图**：
- 冒险家：`MUSHROOM_TOWN = 10000`（`MapId.java:23`，`use_beidou_beginner_map=false` 默认）。
- 骑士团：`STARTING_MAP_NOBLESSE = 130030000`（`MapId.java:106`，`NoblesseCreator.java:47`）。

**建号赠品**（`BeginnerCreator.java:38` / `NoblesseCreator.java:37`）：
- 冒险家：`BEGINNERS_GUIDE = 4161001` ×1 → ETC 背包。
- 骑士团：`NOBLESSE_GUIDE = 4161047` ×1 → ETC 背包。

**装备位置**（`CharacterFactory.java:69-91`）：top=-5、bottom=-6、shoes=-7、weapon=-11（EQUIPPED 背包）。

**建号是一个事务**（`Character.insertNewChar`，`Character.java:8136-8296`）：
1. `INSERT INTO characters (...)` 24 个字段，`sp` 存 `"0,0,0,0,0,0,0,0,0,0"`（按技能书分组的剩余 SP 数组，`GameConstants.getSkillBook(job)` 取下标）。
2. `INSERT INTO keymap (characterid,key,type,action)` 默认键位。
3. `INSERT INTO quickslotkeymapped (accountid,keymap)`。
4. `ItemFactory.INVENTORY.saveItems(...)` 背包（含上面 4 件装备 + 指南）。
5. `INSERT INTO skills (characterid,skillid,skilllevel,masterlevel,expiration)` 起始技能。
6. 任一失败整体 rollback。

---

## 2. 数据库（220 个 Flyway 迁移，目标库 `beidou`）

### 2.1 账号表 `accounts`（V1.0.0__create_accounts.sql，原样）

```sql
CREATE TABLE IF NOT EXISTS `accounts`
(
    `id`             INT(11)      NOT NULL AUTO_INCREMENT,
    `name`           VARCHAR(13)  NOT NULL DEFAULT '',
    `password`       VARCHAR(128) NOT NULL DEFAULT '',
    `pin`            VARCHAR(10)  NOT NULL DEFAULT '',
    `pic`            VARCHAR(26)  NOT NULL DEFAULT '',
    `loggedin`       TINYINT(4)   NOT NULL DEFAULT '0',
    `lastlogin`      TIMESTAMP    NULL     DEFAULT NULL,
    `createdat`      TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `birthday`       DATE         NOT NULL DEFAULT '2005-05-11',
    `banned`         TINYINT(1)   NOT NULL DEFAULT '0',
    `banreason`      TEXT,
    `macs`           TINYTEXT,
    `nxCredit`       INT(11)               DEFAULT NULL,
    `maplePoint`     INT(11)               DEFAULT NULL,
    `nxPrepaid`      INT(11)               DEFAULT NULL,
    `characterslots` TINYINT(2)   NOT NULL DEFAULT '3',
    `gender`         TINYINT(2)   NOT NULL DEFAULT '10',
    `tempban`        TIMESTAMP    NOT NULL DEFAULT '2005-05-11 00:00:00',
    `greason`        TINYINT(4)   NOT NULL DEFAULT '0',
    `tos`            TINYINT(1)   NOT NULL DEFAULT '0',
    `sitelogged`     TEXT,
    `webadmin`       INT(1)                DEFAULT '0',
    `nick`           VARCHAR(20)           DEFAULT NULL,
    `mute`           INT(1)                DEFAULT '0',
    `email`          VARCHAR(45)           DEFAULT NULL,
    `ip`              TEXT,
    `rewardpoints`   INT(11)      NOT NULL DEFAULT '0',
    `votepoints`     INT(11)      NOT NULL DEFAULT '0',
    `hwid`           VARCHAR(12)  NOT NULL DEFAULT '',
    `language`       INT(1)       NOT NULL DEFAULT '3',
    PRIMARY KEY (`id`),
    UNIQUE KEY `name` (`name`),
    KEY `ranking1` (`id`, `banned`),
    INDEX (id, name),
    INDEX (id, nxCredit, maplePoint, nxPrepaid)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 AUTO_INCREMENT = 1;
```

要点：`characterslots=3`（每账号 3 个角色位）；`name` 唯一；密码 BCrypt。

### 2.2 角色表 `characters`（V1.0.6 + 3 个后续 ALTER）

基础表（V1.0.6__create_characters.sql）字段：`id, accountid, world, name, level, exp, gachaexp, str, dex, luk, int, hp, mp, maxhp, maxmp, meso, hpMpUsed, job, skincolor, gender, fame, fquest, hair, face, ap, sp(VARCHAR128 "0,0,..."), map, spawnpoint, gm, party, buddyCapacity, createdate, rank, rankMove, jobRank, jobRankMove, guildid, guildrank, messengerid, messengerposition, mountlevel, mountexp, mounttiredness, omokwins/losses/ties, matchcardwins/losses/ties, MerchantMesos, HasMerchant, equipslots, useslots, setupslots, etcslots, familyId, monsterbookcover, allianceRank, vanquisherStage, ariantPoints, dojoPoints, lastDojoStage, finishedDojoTutorial, vanquisherKills, summonValue, partnerId, marriageItemId, reborns, PQPoints, dataString, lastLogoutTime, lastExpGainTime, partySearch, jailexpire`。

后续迁移（落地必须带上）：
- `V1.9.9`：`ADD COLUMN fishLevel INT NOT NULL DEFAULT 1 COMMENT '角色钓鱼等级' AFTER level`
- `V2.1.51`：`ADD COLUMN damageSkinId INT NOT NULL DEFAULT 0`
- `V2.1.68`：`ADD COLUMN damageCap INT NOT NULL DEFAULT 19999999`
- `V2.1.43/50`：等级上限 250→255、转生上限 10。

**存档 UPDATE 全字段**见 `Character.saveCharToDB`（`Character.java:8324`），涵盖 level/exp/str/dex/luk/int/hp/mp/maxhp/maxmp/sp/ap/job/hair/face/map/meso/skincolor/gender/spawnpoint/…/damageCap 共 60+ 字段 —— 我们的存档器按同一字段集合写。

### 2.3 角色从表（建号/读号都会用到）

| 表 | 迁移文件 | 关键字段 |
|---|---|---|
| `keymap` | V1.0.19 | characterid, key, type, action |
| `quickslotkeymapped` | V1.0.34 | accountid(PK), keymap(BIGINT) |
| `skills` | V1.0.43 | skillid, characterid, skilllevel, masterlevel, expiration；UNIQUE(skillid,characterid) |
| `skillmacros` | V1.0.43 | characterid, position, skill1..3, name, shout |
| `inventoryitems` | V1.0.17 | type, characterid, accountid, itemid, inventorytype, position, quantity, owner, petid, flag, expiration, giftFrom |
| `inventoryequipment` | V1.0.17 | inventoryitemid(FK), upgradeslots, level, str/dex/int/luk/hp/mp/watk/matk/wdef/mdef/acc/avoid/hands/speed/jump/locked/vicious/itemlevel/itemexp/ringid |
| `queststatus` | V1.0.33 | characterid, quest, status, time, expires, forfeited, completed, info |
| `questprogress` | V1.0.33 | characterid, queststatusid, progressid, progress |
| `savedlocations` | V1.0.39 | characterid, locationtype(ENUM), map, portal |
| `world_prop` | V1.3.0 | id, flag, server_message, event_message, channel_size, exp_rate, meso_rate, drop_rate… |
| `game_config` | V1.7.0 | config_type/sub_type/clazz/code/value/desc（键值参数表） |

其余 60+ 张表（guilds/family/duey/mts/pets/bosslog…）当前玩法用不到， schema 文件里先登记不建数据，将来按需补。

---

## 3. 死亡 / 复活流程（源端）

### 3.1 死亡判定与扣罚（`Character.playerDead()`，Character.java:7478-7545）

触发：`hpChangeAction` 发现 `hp<=0 且 oldHp>hp` → 入地图 stat 更新队列执行 `playerDead()`（`:8847-8868`）。

顺序：
1. CPQ 地图 → 扣 CP 并广播 `playerDiedMessage`，**直接 return**（`:7480-7488`）。
2. `cancelAllBuffs(false)` + `dispelDebuffs()`。
3. 事件实例 `eim.playerKilled(this)`。
4. **安全护符**（`SAFETY_CHARM` / `EASTER_BASKET` / `EASTER_CHARM`，道场地图除外）：消耗 1 个，不扣经验，`usedSafetyCharm=true`（`:7497-7511`）。
5. 否则 **职业非新手** 且地图无 `NO_EXP_DECREASE` fieldLimit → 扣经验（`:7512-7533）：
   - `XPdummy = ExpTable.getExpNeededForLevel(当前等级)`
   - **城镇地图** → 扣 1%（`XPdummy/100`）
   - 野外 → LUK<50 扣 10%，LUK≥50 扣 20%
   - 当前经验不足则扣全部
6. 取消 MORPH / MONSTER_RIDING / 坐下，`enableActions()`。

> 新手（job==0）**不扣经验**；`usedSafetyCharm` 会影响复活血量。

### 3.2 复活（`Character.respawn(eim, returnMap)`，Character.java:7596-7613）

客户端死亡确认后发 `CHANGE_MAP`，包体首字节 `1 = from dying`（`ChangeMapHandler.java:85`），服务端：

```
if (!chr.isAlive())
    ├─ 有命运轮币(WHEEL_OF_FORTUNE) → 原地复活 50 血（:102-109）
    └─ 标准路径 → chr.respawn(map.getReturnMapId())   （:116）
```

`respawn(returnMap)`：
1. `changeMap(returnMap)` —— **目标地图 = 当前地图 info 的 `returnMap`**（WZ `Map/Map/MapX/…/info/returnMap`，`MapleMap.java:328-336`；我们 `FieldMaps.json` 已导出该字段）。
2. `cancelAllBuffs(false)`。
3. 血量：
   - `FULL_HP_RESPAWN_MAPS = {262030300, 262031300, 450010100, 221040001, 450009400, 900000207, 410002060}` → 满血（`Character.java:95-98`）
   - `usedSafetyCharm` → 30% HP/MP
   - 其余 → **HP=50**
4. `setStance(0)`。

### 3.3 客户端表现（源端 C++ 客户端）

- 角色死亡动作：`Character/00002000.img` 的 `dead` action（wzpy 已确认存在，同目录还有 `prone`/`prone2`）。
- 死亡弹窗：`UI/UIWindow.img/raise`（`backgrnd` + `0..25` 按钮帧，即「是否原地复活/回城」Yes/No 窗）。
- 城镇判定：地图 info 的 `town` 字段（我们 FieldMaps.json 有 `town`）。

### 3.4 当前客户端差距

`GameLoop.cs` 无任何玩家死亡分支（`DeathSettlement` 只是怪物掉掉落币去重）；玩家 HP 归零后没有任何表现、无回城。需要新增：死亡动画（`dead` action）→ 墓碑 → 确认弹窗 → 回城（按 3.1/3.2 规则）。

---

## 4. 前后端分层方案

### 4.1 目标结构

```
Assets/Scripts/
├── Server/                 ← 纯 C#，不引用 UnityEngine（可用 tools/ 离线单测）
│   ├── Db/
│   │   ├── Schema.sql      ← 与源端 220 个迁移等价的本项目 schema（accounts/characters/从表）
│   │   ├── DbStore.cs      ← 嵌入式存储：Schema.sql 解析 → 表/列/类型 → 行存取
│   │   └── Dao.cs          ← AccountsDao / CharactersDao / SkillsDao / InventoryDao …
│   ├── Login/              ← 账号认证（BCrypt 兼容哈希）、会话、注册
│   ├── World/              ← world_prop 驱动的大区/频道列表
│   ├── Char/               ← 建号 recipe、读档全量数据、存档全量字段
│   ├── Death/              ← playerDead 规则、respawn 规则
│   └── Protocol/           ← 与源端 opcode 一一对应的请求/响应记录 + ITransport
├── Client/                 ← Unity 侧（现有脚本逐步归位）
│   ├── LoginFlow.cs        ← 登录 UI 状态机（登录→选区→建号→选角）
│   ├── DeathFlow.cs        ← 死亡动画/墓碑/弹窗/回城
│   └── ServerGateway.cs    ← 进程内 Transport（将来换 socket 即联机）
└── （现有 GameLoop/FieldDirector/… 作为「频道态」客户端逻辑）
```

### 4.2 关键设计

1. **协议按源端语义建模，不按字节**：`Protocol/` 定义 `LoginRequest/LoginResponse/ServerListRequest/CharListRequest/CreateCharRequest/CharSelectRequest/PlayerLoggedinResponse/ChangeMapRequest…`，字段与源端包体一致（如 CreateChar 的 name/job群/face/hair/hairColor/skin/top/bottom/shoes/weapon/gender）。将来联机只需加一个把 record 序列化的 SocketTransport，业务代码零改动。
2. **单机 = 进程内直连**：`ServerGateway` 默认把 `ITransport` 绑定到内存 Server 实例，延迟 0、无网络权限要求（Android 友好）。
3. **DB 存储**：`Schema.sql` 用源端同名字段；`DbStore` 是「表结构一比一 + JSON 行存储」的嵌入式库（IL2CPP 安全、无原生依赖），接口预留 `IDbConnection` 形状，将来可换 MySQL/SQLite 实现不动业务层。
4. **存档边界**：客户端不再直接写 PlayerPrefs 角色数据；所有角色状态经 Server 读写 DB。PlayerPrefs 只留本地偏好（键位、音量、debug 开关）。
5. **旧存档迁移**：`Profile.Load` 检测到旧版 PlayerPrefs 角色 → 按 `cls` 反推 jobId 灌进 `characters` 表（一次性迁移，写 `migrated` 标记）。

### 4.3 登录 UI 状态机（对齐源端 UI.wz）

```
Boot → Login(账号/密码, 自动注册开关同源端)
     → ServerSelect(world_prop: flag 图标/滚动消息/频道数/倍率)
     → ChannelSelect
     → CharSelect(角色位 3 个, 删除角色, 「创建新角色」)
     → CharCreate(职业群: 冒险家/骑士团 → 脸/发/发色/肤/衣/裤/鞋/武器/性别, 名字唯一校验)
     → InGame(初始地图: 冒险家 10000 / 骑士团 130030000)
```

资源：登录界面背景/按钮在 `MapleDashResources/Data/UI/Login.img`（已随资源包落地），死亡窗 `UI/UIWindow.img/raise`。

---

## 5. 实施阶段（按依赖排序）

| 阶段 | 内容 | 验收 |
|---|---|---|
| S1 数据库 | `Schema.sql` + `DbStore` + Dao；accounts/characters 全字段 CRUD | 离线单测：建号事务（characters+keymap+skills+inventoryitems+quickslot）落库、读档 round-trip |
| S2 服务端核心 | Login/World/Char/Death/Protocol；旧存档迁移 | `tools/server-check`：登录→自动注册→建号→选角→读档→死亡扣经验→回城 全链路过 |
| S3 登录 UI | 5 个界面 + 过渡（替换现有 Lobby 预设角色） | 打开游戏进登录页；注册/登录/选区/建号/选角/进图 |
| S4 死亡流程 | dead 动画 + 墓碑 + raise 弹窗 + 回城；服务端扣经验规则 | 死亡→弹窗→确认→回 returnMap 且 HP=50；城镇只扣 1% |
| S5 联机预留 | Transport 抽象 + 协议记录序列化桩 | 单机路径不变；文档说明切 socket 的步骤 |

> 职业系统（已完成数据导出的 `JobTree.json` + `JobDefs/JobSystem`）在 S2/S3 接入：建号时职业群决定 job(0/1000) 与初始地图；一转规则已按源端数值落地在 JobTree.json 的 `advance` 字段。

---

## 6. 需要用户确认的实现细节

1. **登录密码存储**：源端 BCrypt(12)。本地单机版默认同样 BCrypt（`automatic_register=true` 语义），还是允许空密码直接进？（建议 BCrypt，与源端一致）
2. **大区数量**：`world_prop` 默认 1 个世界 3 频道。本地版做「1 世界 3 频道」的假列表（点频道只是记录，不真分频道），可以吗？
3. **旧存档**：当前 PlayerPrefs 里的角色（等级/金币/装备）要不要迁移进 DB，还是开档重来？（建议迁移）

---

## 7. 落地状态（S1~S4 已完成）

| 阶段 | 落地文件 | 状态 |
|---|---|---|
| S1 数据库 | `Assets/Scripts/Server/Db/{Schema.sql,DbStore.cs,MiniJsonDb.cs,Dao.cs,CharactersDao.cs}` | ✅ 72 项离线自检全过 |
| S2 服务端 | `Server/{GameServer.cs,PlatformRes.cs}`、`Server/Login`、`Server/World`、`Server/Char/{CharFactory,CharService,ServerExpTable,JobBook,CharNameRules,LegacyImport}`、`Server/Death/DeathService`、`Server/Protocol/Protocol.cs` | ✅ |
| S3 登录 UI | `Client/LoginFlow.cs`（5 屏：登录/选区/频道/选角/建号 + 删除二次确认）；已删除 `Assets/Scripts/Lobby.cs`（预设角色）与 `Assets/Editor/LobbyShot.cs` | ✅ 编译通过 |
| S4 死亡 | `Client/DeathFlow.cs` + `PlayerCtrl.PlayDeadPose/Revive` + `GameLoop.OnPlayerDead/Update` 联动 | ✅ 编译通过 |

### 分层约定（需求 4）

```
Assets/Scripts/
  Server/           ← 纯 C#，不引用 UnityEngine；拥有 DbStore，所有写库都在这里
    Db/             Schema.sql + 嵌入式存储 + Dao
    Login/ World/ Char/ Death/ Protocol/
    GameServer.cs   ITransport 的实现方（InProcessTransport）
  Client/           ← 依赖 Unity；只通过 ITransport 说话
    GameSession.cs  会话 + DB↔PlayerProfileData 双向同步
    LoginFlow.cs    登录流程 UI
    DeathFlow.cs    死亡表现层
    FieldMapInfo.cs 把 FieldCatalog 适配成服务端的 IMapInfo（注入，不是反向依赖）
```

**客户端不再直接引用任何 Dao**（已核验 `Client/*.cs` 零 `Dao.`/`Db.` 调用）。
联机时的唯一改动：新增 `SocketTransport : ITransport` 把 record 序列化成字节流，`Server/` 与 `Client/` 业务代码不动。

### 关键实现点

- **登录**：`LoginService.Login` 走 `automatic_register`（账号不存在即注册），密码 `pbkdf2$<iter>$<salt>$<hash>`。
  IL2CPP 没有 BCrypt 原生依赖，改用 BCL 自带的 PBKDF2（`Rfc2898DeriveBytes` 的等价实现），格式与源端一致地 salt+hash 分离存储。
- **初始地图**：冒险家 `10000`（蘑菇村）/ 骑士团 `130030000`；`130030000` 目前不在 `FieldMaps.json`，
  建号后可进但会回退到 `1000000`——**这是已知缺口**，需要后续补骑士团地图导出。
- **死亡**：`Effect/Tomb.img/fall/0..7 → land/0` 是源端墓碑资源；确认窗背景 `UI/UIWindow.img/raise/backgrnd/top`。
  规则全在 `DeathService`：新手/安全图不扣经验，城镇 1%，野外 LUK<50 扣 10% 否则 20%，护符免扣；
  复活血 特殊图满血 / 护符 30% / 其余 50。
- **world_prop**：Schema 已加种子行（1 大区 3 频道），`WorldService` 读表，表空才退回 `game_config`。
