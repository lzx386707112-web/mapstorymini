-- MapleDash 本地数据库 Schema
-- 逐字段复刻自 BeiDou-Server/gms-server/src/main/resources/db/migration：
--   accounts          ← V1.0.0__create_accounts.sql
--   characters        ← V1.0.6__create_characters.sql
--                       + V1.9.9 fishLevel / V2.1.51 damageSkinId / V2.1.68 damageCap
--   keymap            ← V1.0.19__create_keymap.sql
--   quickslotkeymapped← V1.0.34__create_quickslotkeymapped.sql
--   skills/skillmacros← V1.0.43__create_skill.sql
--   inventoryitems    ← V1.0.17__create_inventory.sql
--   inventoryequipment ← V1.0.17__create_inventory.sql
--   queststatus       ← V1.0.33__create_quest.sql
--   questprogress     ← V1.0.33__create_quest.sql
--   savedlocations    ← V1.0.39__create_savedlocations.sql
--   world_prop        ← V1.3.0__create_world_prop.sql
--   game_config       ← V1.7.0__create_game_config.sql（只取本项目消费的参数）
-- 未建表的源端对象（guilds/family/duey/mts/pets/bosslog…）当前玩法不消费，
-- 需要时按同一迁移文件补进本文件即可，DbStore 不需要改。
--
-- 说明：源端是 MySQL(InnoDB, AUTO_INCREMENT, TIMESTAMP 默认值)。本地嵌入式存储
-- 只借用「表名/列名/类型/默认值」结构，自增 id 与时间戳由 DbStore 模拟。

CREATE TABLE accounts
(
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    name           TEXT    NOT NULL DEFAULT '',
    password       TEXT    NOT NULL DEFAULT '',
    pin            TEXT    NOT NULL DEFAULT '',
    pic            TEXT    NOT NULL DEFAULT '',
    loggedin       INTEGER NOT NULL DEFAULT 0,
    lastlogin      TEXT    NULL     DEFAULT NULL,
    createdat      TEXT    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    birthday       TEXT    NOT NULL DEFAULT '2005-05-11',
    banned         INTEGER NOT NULL DEFAULT 0,
    banreason      TEXT,
    macs           TEXT,
    nxCredit       INTEGER DEFAULT NULL,
    maplePoint     INTEGER DEFAULT NULL,
    nxPrepaid      INTEGER DEFAULT NULL,
    characterslots INTEGER NOT NULL DEFAULT 3,
    gender         INTEGER NOT NULL DEFAULT 10,
    tempban        TEXT    NOT NULL DEFAULT '2005-05-11 00:00:00',
    greason        INTEGER NOT NULL DEFAULT 0,
    tos            INTEGER NOT NULL DEFAULT 0,
    sitelogged     TEXT,
    webadmin       INTEGER DEFAULT 0,
    nick           TEXT    DEFAULT NULL,
    mute           INTEGER DEFAULT 0,
    email          TEXT    DEFAULT NULL,
    ip             TEXT,
    rewardpoints   INTEGER NOT NULL DEFAULT 0,
    votepoints     INTEGER NOT NULL DEFAULT 0,
    hwid           TEXT    NOT NULL DEFAULT '',
    language       INTEGER NOT NULL DEFAULT 3,
    UNIQUE (name)
);

CREATE TABLE characters
(
    id                   INTEGER PRIMARY KEY AUTOINCREMENT,
    accountid            INTEGER NOT NULL DEFAULT 0,
    world                INTEGER NOT NULL DEFAULT 0,
    name                 TEXT    NOT NULL DEFAULT '',
    level                INTEGER NOT NULL DEFAULT 1,
    exp                  INTEGER NOT NULL DEFAULT 0,
    gachaexp             INTEGER NOT NULL DEFAULT 0,
    str                  INTEGER NOT NULL DEFAULT 12,
    dex                  INTEGER NOT NULL DEFAULT 5,
    luk                  INTEGER NOT NULL DEFAULT 4,
    int                  INTEGER NOT NULL DEFAULT 4,
    hp                   INTEGER NOT NULL DEFAULT 50,
    mp                   INTEGER NOT NULL DEFAULT 5,
    maxhp                INTEGER NOT NULL DEFAULT 50,
    maxmp                INTEGER NOT NULL DEFAULT 5,
    meso                 INTEGER NOT NULL DEFAULT 0,
    hpMpUsed             INTEGER NOT NULL DEFAULT 0,
    job                  INTEGER NOT NULL DEFAULT 0,
    skincolor            INTEGER NOT NULL DEFAULT 0,
    gender               INTEGER NOT NULL DEFAULT 0,
    fame                 INTEGER NOT NULL DEFAULT 0,
    fquest               INTEGER NOT NULL DEFAULT 0,
    hair                 INTEGER NOT NULL DEFAULT 0,
    face                 INTEGER NOT NULL DEFAULT 0,
    ap                   INTEGER NOT NULL DEFAULT 0,
    sp                   TEXT    NOT NULL DEFAULT '0,0,0,0,0,0,0,0,0,0',
    map                  INTEGER NOT NULL DEFAULT 0,
    spawnpoint           INTEGER NOT NULL DEFAULT 0,
    gm                   INTEGER NOT NULL DEFAULT 0,
    party                INTEGER NOT NULL DEFAULT 0,
    buddyCapacity        INTEGER NOT NULL DEFAULT 25,
    createdate           TEXT    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    rank                 INTEGER NOT NULL DEFAULT 1,
    rankMove             INTEGER NOT NULL DEFAULT 0,
    jobRank              INTEGER NOT NULL DEFAULT 1,
    jobRankMove          INTEGER NOT NULL DEFAULT 0,
    guildid              INTEGER NOT NULL DEFAULT 0,
    guildrank            INTEGER NOT NULL DEFAULT 5,
    messengerid          INTEGER NOT NULL DEFAULT 0,
    messengerposition    INTEGER NOT NULL DEFAULT 4,
    mountlevel           INTEGER NOT NULL DEFAULT 1,
    mountexp             INTEGER NOT NULL DEFAULT 0,
    mounttiredness       INTEGER NOT NULL DEFAULT 0,
    omokwins             INTEGER NOT NULL DEFAULT 0,
    omoklosses           INTEGER NOT NULL DEFAULT 0,
    omokties             INTEGER NOT NULL DEFAULT 0,
    matchcardwins        INTEGER NOT NULL DEFAULT 0,
    matchcardlosses      INTEGER NOT NULL DEFAULT 0,
    matchcardties        INTEGER NOT NULL DEFAULT 0,
    MerchantMesos        INTEGER DEFAULT 0,
    HasMerchant          INTEGER DEFAULT 0,
    equipslots           INTEGER NOT NULL DEFAULT 24,
    useslots             INTEGER NOT NULL DEFAULT 24,
    setupslots           INTEGER NOT NULL DEFAULT 24,
    etcslots             INTEGER NOT NULL DEFAULT 24,
    familyId             INTEGER NOT NULL DEFAULT -1,
    monsterbookcover     INTEGER NOT NULL DEFAULT 0,
    allianceRank         INTEGER NOT NULL DEFAULT 5,
    vanquisherStage      INTEGER NOT NULL DEFAULT 0,
    ariantPoints         INTEGER NOT NULL DEFAULT 0,
    dojoPoints           INTEGER NOT NULL DEFAULT 0,
    lastDojoStage        INTEGER NOT NULL DEFAULT 0,
    finishedDojoTutorial INTEGER NOT NULL DEFAULT 0,
    vanquisherKills      INTEGER NOT NULL DEFAULT 0,
    summonValue          INTEGER NOT NULL DEFAULT 0,
    partnerId            INTEGER NOT NULL DEFAULT 0,
    marriageItemId       INTEGER NOT NULL DEFAULT 0,
    reborns              INTEGER NOT NULL DEFAULT 0,
    PQPoints             INTEGER NOT NULL DEFAULT 0,
    dataString           TEXT    NOT NULL DEFAULT '',
    lastLogoutTime       TEXT    NOT NULL DEFAULT '2015-01-01 05:00:00',
    lastExpGainTime      TEXT    NOT NULL DEFAULT '2015-01-01 05:00:00',
    partySearch          INTEGER NOT NULL DEFAULT 1,
    jailexpire           INTEGER NOT NULL DEFAULT 0,
    fishLevel            INTEGER NOT NULL DEFAULT 1,
    damageSkinId         INTEGER NOT NULL DEFAULT 0,
    damageCap            INTEGER NOT NULL DEFAULT 19999999
);

CREATE TABLE keymap
(
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    characterid INTEGER NOT NULL DEFAULT 0,
    key         INTEGER NOT NULL DEFAULT 0,
    type        INTEGER NOT NULL DEFAULT 0,
    action      INTEGER NOT NULL DEFAULT 0
);

-- V1.0.5__create_buddies.sql: pending requests survive character switches.
CREATE TABLE buddies
(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    characterid INTEGER NOT NULL,
    buddyid INTEGER NOT NULL,
    pending INTEGER NOT NULL DEFAULT 0,
    `group` TEXT DEFAULT 'Default Group'
);

-- V1.0.23__create_monster.sql
CREATE TABLE monsterbook
(
    charid INTEGER NOT NULL,
    cardid INTEGER NOT NULL,
    level INTEGER NOT NULL DEFAULT 1,
    PRIMARY KEY (charid, cardid)
);

CREATE TABLE quickslotkeymapped
(
    accountid INTEGER PRIMARY KEY,
    keymap    INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE skills
(
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    skillid     INTEGER NOT NULL DEFAULT 0,
    characterid INTEGER NOT NULL DEFAULT 0,
    skilllevel  INTEGER NOT NULL DEFAULT 0,
    masterlevel INTEGER NOT NULL DEFAULT 0,
    expiration  INTEGER NOT NULL DEFAULT -1,
    UNIQUE (skillid, characterid)
);

CREATE TABLE skillmacros
(
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    characterid INTEGER NOT NULL DEFAULT 0,
    position    INTEGER NOT NULL DEFAULT 0,
    skill1      INTEGER NOT NULL DEFAULT 0,
    skill2      INTEGER NOT NULL DEFAULT 0,
    skill3      INTEGER NOT NULL DEFAULT 0,
    name        TEXT    DEFAULT NULL,
    shout       INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE inventoryitems
(
    inventoryitemid INTEGER PRIMARY KEY AUTOINCREMENT,
    type            INTEGER NOT NULL,
    characterid     INTEGER DEFAULT NULL,
    accountid       INTEGER DEFAULT NULL,
    itemid          INTEGER NOT NULL DEFAULT 0,
    inventorytype   INTEGER NOT NULL DEFAULT 0,
    position        INTEGER NOT NULL DEFAULT 0,
    quantity        INTEGER NOT NULL DEFAULT 0,
    owner           TEXT    NOT NULL DEFAULT '',
    petid           INTEGER NOT NULL DEFAULT -1,
    flag            INTEGER NOT NULL DEFAULT 0,
    expiration      INTEGER NOT NULL DEFAULT -1,
    giftFrom        TEXT    NOT NULL DEFAULT ''
);

CREATE TABLE inventoryequipment
(
    inventoryequipmentid INTEGER PRIMARY KEY AUTOINCREMENT,
    inventoryitemid      INTEGER NOT NULL DEFAULT 0,
    upgradeslots         INTEGER NOT NULL DEFAULT 0,
    level                INTEGER NOT NULL DEFAULT 0,
    str                  INTEGER NOT NULL DEFAULT 0,
    dex                  INTEGER NOT NULL DEFAULT 0,
    int                  INTEGER NOT NULL DEFAULT 0,
    luk                  INTEGER NOT NULL DEFAULT 0,
    hp                   INTEGER NOT NULL DEFAULT 0,
    mp                   INTEGER NOT NULL DEFAULT 0,
    watk                 INTEGER NOT NULL DEFAULT 0,
    matk                 INTEGER NOT NULL DEFAULT 0,
    wdef                 INTEGER NOT NULL DEFAULT 0,
    mdef                 INTEGER NOT NULL DEFAULT 0,
    acc                  INTEGER NOT NULL DEFAULT 0,
    avoid                INTEGER NOT NULL DEFAULT 0,
    hands                INTEGER NOT NULL DEFAULT 0,
    speed                INTEGER NOT NULL DEFAULT 0,
    jump                 INTEGER NOT NULL DEFAULT 0,
    locked               INTEGER NOT NULL DEFAULT 0,
    vicious              INTEGER NOT NULL DEFAULT 0,
    itemlevel            INTEGER NOT NULL DEFAULT 1,
    itemexp              INTEGER NOT NULL DEFAULT 0,
    ringid               INTEGER NOT NULL DEFAULT -1
);

CREATE TABLE queststatus
(
    queststatusid INTEGER PRIMARY KEY AUTOINCREMENT,
    characterid   INTEGER NOT NULL DEFAULT 0,
    quest         INTEGER NOT NULL DEFAULT 0,
    status        INTEGER NOT NULL DEFAULT 0,
    time          INTEGER NOT NULL DEFAULT 0,
    expires       INTEGER NOT NULL DEFAULT 0,
    forfeited     INTEGER NOT NULL DEFAULT 0,
    completed     INTEGER NOT NULL DEFAULT 0,
    info          INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE questprogress
(
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    characterid   INTEGER NOT NULL,
    queststatusid INTEGER NOT NULL DEFAULT 0,
    progressid    INTEGER NOT NULL DEFAULT 0,
    progress      TEXT    NOT NULL DEFAULT ''
);

CREATE TABLE savedlocations
(
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    characterid   INTEGER NOT NULL,
    locationtype  TEXT    NOT NULL,
    map           INTEGER NOT NULL,
    portal        INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE world_prop
(
    id                INTEGER PRIMARY KEY,
    flag              INTEGER DEFAULT 0,
    server_message    TEXT,
    event_message     TEXT,
    recommend_message TEXT,
    channel_size      INTEGER,
    exp_rate          REAL,
    meso_rate         REAL,
    drop_rate         REAL,
    boss_drop_rate    REAL,
    quest_rate        REAL,
    fishing_rate      REAL,
    travel_rate       REAL,
    level_exp_rate    REAL,
    quick_level       REAL,
    quick_level_exp_rate REAL,
    enabled           INTEGER DEFAULT 0
);

-- 种子数据：V1.3.0__create_world_prop.sql 的表结构 + 一条默认大区（本地单机 1 大区 3 频道）。
-- 联机时 world_prop 由运营后台维护，这里只是让「选区」界面有真实数据源。
INSERT INTO world_prop (id, flag, server_message, event_message, recommend_message,
                        channel_size, exp_rate, meso_rate, drop_rate, boss_drop_rate,
                        quest_rate, fishing_rate, travel_rate, level_exp_rate, enabled) VALUES
(0, 0, '欢迎来到枫之谷！', '未来岛', '推荐新玩家进入', 3, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1);

CREATE TABLE game_config
(
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    config_type     TEXT NOT NULL,
    config_sub_type TEXT,
    config_clazz    TEXT,
    config_code     TEXT NOT NULL,
    config_value    TEXT NOT NULL,
    config_desc     TEXT,
    update_time     TEXT
);

-- 种子数据：与 V1.7.0__create_game_config.sql 一致的大区/频道参数（本地默认 1 世界 3 频道）
INSERT INTO game_config (config_type, config_sub_type, config_clazz, config_code, config_value, config_desc) VALUES
('world', '0', 'java.lang.Integer', 'flag', '0', '0=普通大区，1=活动大区，2=新区，3=热门大区'),
('world', '0', 'java.lang.String', 'server_message', 'Welcome to Scania!', '顶部滚动信息'),
('world', '0', 'java.lang.String', 'event_message', 'Scania', '大区描述'),
('world', '0', 'java.lang.String', 'recommend_message', 'Welcome to Scania!', '大区推荐信息'),
('world', '0', 'java.lang.Integer', 'channel_size', '3', '启动频道数'),
('world', '0', 'java.lang.Float', 'exp_rate', '1.0', '经验倍率'),
('world', '0', 'java.lang.Float', 'meso_rate', '1.0', '金币倍率'),
('world', '0', 'java.lang.Float', 'drop_rate', '1.0', '掉落倍率'),
('world', '0', 'java.lang.Float', 'quest_rate', '1.0', '任务倍率');

-- 本项目消费的服务端参数（值取自源端迁移默认值）
INSERT INTO game_config (config_type, config_sub_type, config_clazz, config_code, config_value, config_desc) VALUES
('server', 'Safe', 'java.lang.Boolean', 'automatic_register', 'true', '登录时账号不存在则自动注册'),
('server', 'Game Mechanics', 'java.lang.Boolean', 'use_auto_assign_starters_ap', 'true', '10级以下自动分配属性点'),
('server', 'Game Mechanics', 'java.lang.Boolean', 'use_starting_ap_4', 'false', '早期GMS 4/4/4/4起始属性'),
('server', 'Game Mechanics', 'java.lang.Boolean', 'enable_adventurers', 'true', '允许创建冒险家'),
('server', 'Game Mechanics', 'java.lang.Boolean', 'enable_knights_of_cygnus', 'true', '允许创建骑士团'),
('server', 'Game Mechanics', 'java.lang.Boolean', 'use_beidou_beginner_map', 'false', '冒险家新手进青苹果地图(10000)'),
('server', 'Game Mechanics', 'java.lang.Integer', 'level_up_ap_gain', '5', '每级AP'),
('server', 'Game Mechanics', 'java.lang.Integer', 'level_up_sp_gain', '3', '每级SP'),
('server', 'Game Mechanics', 'java.lang.Boolean', 'use_enforce_job_sp_range', 'false', '按职业限制SP上限');

-- GuildOperationHandler / V1.0.15; members are characters.guildid/guildrank.
CREATE TABLE guilds (
 guildid INTEGER PRIMARY KEY AUTOINCREMENT,
 leader INTEGER NOT NULL DEFAULT 0,
 GP INTEGER NOT NULL DEFAULT 0,
 name TEXT NOT NULL DEFAULT '',
 rank1title TEXT DEFAULT 'Master',
 rank2title TEXT DEFAULT 'Jr. Master',
 rank3title TEXT DEFAULT 'Member',
 rank4title TEXT DEFAULT 'Member',
 rank5title TEXT DEFAULT 'Member',
 capacity INTEGER DEFAULT 10,
 logo INTEGER DEFAULT 0,
 logoColor INTEGER DEFAULT 0,
 logoBG INTEGER DEFAULT 0,
 logoBGColor INTEGER DEFAULT 0,
 notice TEXT DEFAULT '',
 signature INTEGER DEFAULT 0,
 allianceId INTEGER DEFAULT 0
);
CREATE TABLE family_character (
 cid INTEGER PRIMARY KEY,
 familyid INTEGER NOT NULL,
 seniorid INTEGER DEFAULT 0,
 reputation INTEGER DEFAULT 0,
 todaysrep INTEGER DEFAULT 0,
 totalreputation INTEGER DEFAULT 0,
 reptosenior INTEGER DEFAULT 0,
 precepts TEXT DEFAULT '',
 lastresettime INTEGER DEFAULT 0
);
CREATE TABLE family_entitlement (
 id INTEGER PRIMARY KEY AUTOINCREMENT,
 charid INTEGER NOT NULL,
 entitlementid INTEGER NOT NULL,
 timestamp INTEGER DEFAULT 0
);
-- Local representation of active family buff timer (source Character.startFamilyBuffTimer).
CREATE TABLE family_buff (
 charid INTEGER PRIMARY KEY,
 expires INTEGER DEFAULT 0,
 expRate REAL DEFAULT 1,
 dropRate REAL DEFAULT 1
);
-- Source modified_cash_item nullable overrides; zero is a real override, not absent.
CREATE TABLE modified_cash_item (
 sn INTEGER PRIMARY KEY,
 item_id INTEGER,
 count INTEGER,
 price INTEGER,
 period INTEGER,
 on_sale INTEGER,
 commodity_gender INTEGER,
 priority INTEGER,
 bonus INTEGER,
 maple_point INTEGER,
 meso INTEGER,
 for_premium_user INTEGER,
 class INTEGER,
 limit INTEGER,
 pb_cash INTEGER,
 pb_point INTEGER,
 pb_gift INTEGER,
 package_sn INTEGER
);
CREATE TABLE cash_items (
 id INTEGER PRIMARY KEY AUTOINCREMENT,
 accountid INTEGER NOT NULL,
 world INTEGER NOT NULL,
 jobgroup INTEGER NOT NULL,
 sn INTEGER NOT NULL,
 itemid INTEGER NOT NULL,
 quantity INTEGER DEFAULT 1,
 expiration INTEGER DEFAULT -1,
 characterid INTEGER DEFAULT 0,
 inventoryitemid INTEGER DEFAULT 0,
 giftFrom TEXT DEFAULT ''
);
CREATE TABLE cash_wishlist (
 id INTEGER PRIMARY KEY AUTOINCREMENT,
 characterid INTEGER NOT NULL,
 sn INTEGER NOT NULL
);
CREATE TABLE cash_gifts (
 id INTEGER PRIMARY KEY AUTOINCREMENT,
 characterid INTEGER NOT NULL,
 sn INTEGER NOT NULL,
 sender TEXT DEFAULT '',
 message TEXT DEFAULT ''
);

-- BossDamageGrowth kill progress and ExpeditionBossLog entry records.
CREATE TABLE boss_growth (
 characterid INTEGER NOT NULL,
 boss TEXT NOT NULL,
 kills INTEGER NOT NULL DEFAULT 0,
 PRIMARY KEY (characterid, boss)
);
CREATE TABLE bosslog_daily (
 id INTEGER PRIMARY KEY AUTOINCREMENT,
 characterid INTEGER NOT NULL,
 bosstype TEXT NOT NULL,
 attempttime INTEGER NOT NULL
);
