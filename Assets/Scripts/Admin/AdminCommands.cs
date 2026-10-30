// 后台「命令库」模块：/command/v1/*（源端 CommandController + CommandService）。
//
// 表 command_info：syntax（指令）/level（当前等级，可改）/enabled/clazz（后端类名）/
// default_level（默认等级，不可改）。源端 description 不落库，是反射实例化
// org.gms.client.command.commands.gm{defaultLevel}.{clazz} 后取 i18n（<类名>.message1）；
// 单机版没有 Java 反射，这里把 186 个类的中文描述内置成表（与 i18n 值一致）。
//
// 种子数据 = 源端 V1.5.1（179 条）+ V1.8.4（中文别名 10 条）+ V1.9.9（fishdrop）
// + V2.0.9（reloadhunt）+ V2.1.0（soloMapling 9 条）。首启灌进 command_info，
// 之后以表为准（level/enabled 的修改就是本模块的编辑功能）。
//
// 三个 reload 按钮：源端复用 GM 命令重载服务端脚本/传送点/地图。单机版对应
// 「清 NPC/任务脚本缓存」「清地图解析缓存（WZ 改过之后）」——不是假装成功，是真的清缓存。
//
// 约束：纯 C#，不引用 UnityEngine（重载动作走 AdminCommands.Reload* 钩子，由 AdminHost 注入）。
using System;
using System.Collections.Generic;

public static class AdminCommands
{
    /// <summary>重载 NPC/任务脚本缓存（≡ !reloadevents）。由 AdminHost 注入 ScriptSource.ClearCache。</summary>
    public static Action ReloadScripts;
    /// <summary>重载地图/传送点解析缓存（≡ !reloadportals / !reloadmap）。由 AdminHost 注入 FieldCatalog 失效。</summary>
    public static Action ReloadMaps;

    // 种子：syntax|clazz|level（level == default_level，源端种子两列恒等）
    const string Seed = @"
help|HelpCommand|0
commands|HelpCommand|0
droplimit|DropLimitCommand|0
time|TimeCommand|0
credits|StaffCommand|0
uptime|UptimeCommand|0
gacha|GachaCommand|0
dispose|DisposeCommand|0
changel|ChangeLanguageCommand|0
equiplv|EquipLvCommand|0
showrates|ShowRatesCommand|0
rates|RatesCommand|0
online|OnlineCommand|0
gm|GmCommand|0
reportbug|ReportBugCommand|0
points|ReadPointsCommand|0
joinevent|JoinEventCommand|0
leaveevent|LeaveEventCommand|0
ranks|RanksCommand|0
str|StatStrCommand|0
dex|StatDexCommand|0
int|StatIntCommand|0
luk|StatLukCommand|0
enableauth|EnableAuthCommand|0
toggleexp|ToggleExpCommand|0
mylawn|MapOwnerClaimCommand|0
bosshp|BossHpCommand|1
mobhp|MobHpCommand|1
whatdropsfrom|WhatDropsFromCommand|1
whodrops|WhoDropsCommand|1
buffme|BuffMeCommand|1
goto|GotoCommand|1
recharge|RechargeCommand|2
whereami|WhereaMiCommand|2
hide|HideCommand|2
unhide|UnHideCommand|2
sp|SpCommand|2
ap|ApCommand|2
empowerme|EmpowerMeCommand|2
buffmap|BuffMapCommand|2
buff|BuffCommand|2
bomb|BombCommand|2
dc|DcCommand|2
cleardrops|ClearDropsCommand|2
clearslot|ClearSlotCommand|2
clearsavelocs|ClearSavedLocationsCommand|2
warp|WarpCommand|2
warphere|SummonCommand|2
summon|SummonCommand|2
warpto|ReachCommand|2
reach|ReachCommand|2
follow|ReachCommand|2
gmshop|GmShopCommand|2
heal|HealCommand|2
item|ItemCommand|2
drop|ItemDropCommand|2
level|LevelCommand|2
levelpro|LevelProCommand|2
setslot|SetSlotCommand|2
setstat|SetStatCommand|2
maxstat|MaxStatCommand|2
maxskill|MaxSkillCommand|2
resetskill|ResetSkillCommand|2
search|SearchCommand|2
jail|JailCommand|2
unjail|UnJailCommand|2
job|JobCommand|2
unbug|UnBugCommand|2
id|IdCommand|2
gachalist|GachaListCommand|2
loot|LootCommand|2
mobskill|MobSkillCommand|2
warpmap|WarpMapCommand|2
warparea|WarpAreaCommand|2
debuff|DebuffCommand|3
fly|FlyCommand|3
spawn|SpawnCommand|3
mutemap|MuteMapCommand|3
checkdmg|CheckDmgCommand|3
inmap|InMapCommand|3
reloadevents|ReloadEventsCommand|3
reloaddrops|ReloadDropsCommand|3
reloadportals|ReloadPortalsCommand|3
reloadmap|ReloadMapCommand|3
reloadshops|ReloadShopsCommand|3
hpmp|HpMpCommand|3
maxhpmp|MaxHpMpCommand|3
music|MusicCommand|3
monitor|MonitorCommand|3
monitors|MonitorsCommand|3
ignore|IgnoreCommand|3
ignored|IgnoredCommand|3
pos|PosCommand|3
togglecoupon|ToggleCouponCommand|3
togglewhitechat|ChatCommand|3
fame|FameCommand|3
givenx|GiveNxCommand|3
givevp|GiveVpCommand|3
givems|GiveMesosCommand|3
giverp|GiveRpCommand|3
expeds|ExpedsCommand|3
kill|KillCommand|3
seed|SeedCommand|3
maxenergy|MaxEnergyCommand|3
killall|KillAllCommand|3
notice|NoticeCommand|3
rip|RipCommand|3
openportal|OpenPortalCommand|3
closeportal|ClosePortalCommand|3
pe|PeCommand|3
startevent|StartEventCommand|3
endevent|EndEventCommand|3
startmapevent|StartMapEventCommand|3
stopmapevent|StopMapEventCommand|3
online2|OnlineTwoCommand|3
ban|BanCommand|3
unban|UnBanCommand|3
healmap|HealMapCommand|3
healperson|HealPersonCommand|3
hurt|HurtCommand|3
killmap|KillMapCommand|3
night|NightCommand|3
npc|NpcCommand|3
face|FaceCommand|3
hair|HairCommand|3
startquest|QuestStartCommand|3
completequest|QuestCompleteCommand|3
resetquest|QuestResetCommand|3
timer|TimerCommand|3
timermap|TimerMapCommand|3
timerall|TimerAllCommand|3
reloadhunt|ReloadHuntCommand|3
servermessage|ServerMessageCommand|4
proitem|ProItemCommand|4
seteqstat|SetEqStatCommand|4
exprate|ExpRateCommand|4
mesorate|MesoRateCommand|4
droprate|DropRateCommand|4
bossdroprate|BossDropRateCommand|4
questrate|QuestRateCommand|4
travelrate|TravelRateCommand|4
fishrate|FishingRateCommand|4
itemvac|ItemVacCommand|4
forcevac|ForceVacCommand|4
zakum|ZakumCommand|4
horntail|HorntailCommand|4
pinkbean|PinkbeanCommand|4
pap|PapCommand|4
pianus|PianusCommand|4
cake|CakeCommand|4
playernpc|PlayerNpcCommand|4
playernpcremove|PlayerNpcRemoveCommand|4
pnpc|PnpcCommand|4
pnpcremove|PnpcRemoveCommand|4
pmob|PmobCommand|4
pmobremove|PmobRemoveCommand|4
warptolife|WarpToLifeCommand|4
fishdrop|FishDropCommand|4
bot|ArtificialPlayerCommand|4
move|BotMoveCommand|4
env|EnvironmentCommand|4
betafmshop|ArtificialFreeMarketCommand|4
fmbot|FMBotCommand|4
tradebot|TradeBotTestCommand|4
test|TestDevCommand|4
opq|OPQCommands|4
reactor|ReactorCommands|4
debug|DebugCommand|5
set|SetCommand|5
showpackets|ShowPacketsCommand|5
showmovelife|ShowMoveLifeCommand|5
showsessions|ShowSessionsCommand|5
iplist|IpListCommand|5
setgmlevel|SetGmLevelCommand|6
warpworld|WarpWorldCommand|6
saveall|SaveAllCommand|6
dcall|DCAllCommand|6
mapplayers|MapPlayersCommand|6
getacc|GetAccCommand|6
shutdown|ShutdownCommand|6
clearquestcache|ClearQuestCacheCommand|6
clearquest|ClearQuestCommand|6
supplyratecoupon|SupplyRateCouponCommand|6
spawnallpnpcs|SpawnAllPNpcsCommand|6
eraseallpnpcs|EraseAllPNpcsCommand|6
addchannel|ServerAddChannelCommand|6
addworld|ServerAddWorldCommand|6
removechannel|ServerRemoveChannelCommand|6
removeworld|ServerRemoveWorldCommand|6
devtest|DevtestCommand|6
扭蛋奖励|GachaCommand|0
全部加力量|StatStrCommand|0
全部加敏捷|StatDexCommand|0
全部加智力|StatIntCommand|0
全部加运气|StatLukCommand|0
装备信息|EquipLvCommand|0
倍率|ShowRatesCommand|0
jk|DisposeCommand|0
解卡|DisposeCommand|0
帮助|HelpCommand|0";

    // 描述 = 源端 i18n（<类名>.message1）；gm4 的 bot 系列在源端就是英文字符串字面量
    const string Descriptions = @"
ChangeLanguageCommand|更改语言
DisposeCommand|解卡npc对话
DropLimitCommand|查看当前地图最大掉落数
EnableAuthCommand|重置PIC或PIN验证冷却时间
EquipLvCommand|展示装备等级
GachaCommand|展示扭蛋机奖励
GmCommand|给gm发送消息
HelpCommand|展示可用的gm指令
JoinEventCommand|参加当前的活动
LeaveEventCommand|离开当前活动
MapOwnerClaimCommand|开启地图归属
OnlineCommand|展示所有在线的玩家信息
RanksCommand|展示玩家排名信息
RatesCommand|展示玩家倍率
ReadPointsCommand|展示全部点数
ReportBugCommand|发送漏洞信息
ShowRatesCommand|展示大区和玩家倍率
StaffCommand|展示服务端参与贡献的人员
StatDexCommand|将属性点分配到敏捷
StatIntCommand|将属性点分配到智力
StatLukCommand|将属性点分配到运气
StatStrCommand|将属性点分配到力量
TimeCommand|展示当前服务器时间
ToggleExpCommand|开启或关闭经验获取
UptimeCommand|展示当前服务器已运行时间
BossHpCommand|展示当前地图的boss血量
BuffMeCommand|激活GM buff
GotoCommand|传送到预设地图
MobHpCommand|展示当前地图的怪物血量
WhatDropsFromCommand|查询怪物掉落
WhoDropsCommand|查询物品掉落
ApCommand|设置属性点
BombCommand|对玩家使用炸弹技能
BuffCommand|激活指定buff
BuffMapCommand|全图激活GM buff
ClearDropsCommand|清空当前玩家的掉落
ClearSavedLocationsCommand|清空玩家所有的存档点
ClearSlotCommand|清空当前玩家的指定背包栏
DcCommand|强制玩家下线
EmpowerMeCommand|激活所有可用的buff
GachaListCommand|展示扭蛋机奖励
GmShopCommand|打开GM商店
HealCommand|血蓝加满
HideCommand|隐身
IdCommand|根据ID在手册中查询
ItemCommand|生成物品到背包
ItemDropCommand|生成物品到地上
JailCommand|把某人关进监狱
JobCommand|玩家转职
LevelCommand|自定义等级，直接更改
LevelProCommand|自定义等级，一级一级升
LootCommand|展示您拥有的所有物品
MaxSkillCommand|技能全满
MaxStatCommand|状态全满
MobSkillCommand|给当前地图的所有怪物应用指定技能
ReachCommand|传送到指定玩家的位置
RechargeCommand|充值所有能够充值的消耗品
ResetSkillCommand|重置技能
SearchCommand|全局搜索
SetSlotCommand|设置所有背包格子数
SetStatCommand|设置角色全部属性
SpCommand|设置技能点
SummonCommand|传送指定玩家到您的位置
UnBugCommand|解卡动作
UnHideCommand|取消隐身
UnJailCommand|把某人从监狱中释放
WarpAreaCommand|把当前地图附近的玩家传送到指定的地图
WarpCommand|传送到指定地图
WarpMapCommand|把当前地图的所有玩家传送到指定的地图
WhereaMiCommand|展示当前地图信息
BanCommand|封禁玩家
ChatCommand|开关GM聊天模式
CheckDmgCommand|展示某个玩家的状态和伤害
ClosePortalCommand|关闭当前地图的传送点
DebuffCommand|给附近玩家上debuff
EndEventCommand|结束当前正在进行的活动
ExpedsCommand|展示所有正在进行的远征
FaceCommand|改变玩家的脸型
FameCommand|设置玩家人气
FlyCommand|开启或关闭飞行模式
GiveMesosCommand|给玩家发放金币
GiveNxCommand|给玩家发放点券
GiveRpCommand|给玩家发放奖励点数
GiveVpCommand|给玩家发放投票点数
HairCommand|改变玩家的发型
HealMapCommand|回满全图血蓝
HealPersonCommand|回满玩家血蓝
HpMpCommand|设置玩家HP/MP
HurtCommand|伤害玩家到1HP
IgnoreCommand|添加或移除玩家的自动封禁白名单
IgnoredCommand|展示所有自动封禁白名单玩家
InMapCommand|展示当前地图的所有玩家
KillAllCommand|击杀当前地图所有怪物
KillCommand|击杀玩家
KillMapCommand|击杀当前地图的所有玩家
MaxEnergyCommand|道场能量条加满
MaxHpMpCommand|设置玩家最大HP/MP
MonitorCommand|开启或关闭监控某个玩家的发包
MonitorsCommand|开启或关闭监控所有玩家的发包
MusicCommand|在当前地图播放音乐
MuteMapCommand|开启或关闭禁言
NightCommand|将当前地图设置为夜晚
NoticeCommand|发送全服公告
NpcCommand|在当前的位置生成一个NPC
OnlineTwoCommand|展示所有在线玩家
OpenPortalCommand|开启当前地图的传送点
PeCommand|从文件中读取发包内容模拟客户端发送
PosCommand|展示玩家当前位置信息
QuestCompleteCommand|完成一个任务
QuestResetCommand|重置一个任务
QuestStartCommand|开始一个任务
ReloadDropsCommand|重载掉落
ReloadEventsCommand|重载活动
ReloadHuntCommand|重新加载狩猎任务配置并重新发布任务
ReloadMapCommand|重载地图
ReloadPortalsCommand|重载传送点
ReloadShopsCommand|重载商店
RipCommand|发送全服封禁公告
SeedCommand|掉落所有的妙月种子
SpawnCommand|在当前位置生成怪物
StartEventCommand|在当前地图开启一个活动
StartMapEventCommand|开启当前地图的官方活动，仅活动地图有效
StopMapEventCommand|停止当前地图的官方活动，仅活动地图有效
TimerAllCommand|给当前大区设置一个计时器
TimerCommand|给玩家设置一个计时器
TimerMapCommand|给当前地图设置一个计时器
ToggleCouponCommand|开启或关闭倍率卡
UnBanCommand|解封玩家
ArtificialFreeMarketCommand|Artificial FM Test.
ArtificialPlayerCommand|Artificial Player Commands Test.
BossDropRateCommand|设置大区Boss掉落倍率
BotMoveCommand|Bot Movement Commands Test.
CakeCommand|在当前位置召唤特殊血量的巨型蛋糕BOSS
DropRateCommand|设置大区掉落倍率
EnvironmentCommand|Environment Commands.
ExpRateCommand|设置大区经验倍率
FMBotCommand|FmBot Commands.
FishDropCommand|钓鱼掉落物品配置
FishingRateCommand|设置大区钓鱼倍率
ForceVacCommand|强制捡起全图掉落
HorntailCommand|在当前位置召唤黑龙
ItemVacCommand|捡起全图掉落
MesoRateCommand|设置大区金币倍率
OPQCommands|OPQ bot dev commands. Try: !opq help
PapCommand|在当前位置召唤帕普拉图斯
PianusCommand|在当前位置召唤皮亚奴斯
PinkbeanCommand|在当前位置召唤品克缤
PlayerNpcCommand|在当前位置生成一个玩家NPC
PlayerNpcRemoveCommand|移除一个玩家NPC
PmobCommand|在当前位置生成一个永久怪物
PmobRemoveCommand|移除当前地图的永久怪物
PnpcCommand|在当前位置生成一个永久NPC
PnpcRemoveCommand|移除当前地图的永久NPC
ProItemCommand|生成自定义属性的装备
QuestRateCommand|设置大区任务倍率
ReactorCommands|Reactor inspection and bot-attack test commands.
ServerMessageCommand|设置服务端滚动消息
SetEqStatCommand|将背包的所有装备设置成指定属性
TestDevCommand|Template Commands Test.
TradeBotTestCommand|Trade Bot Commands.
TravelRateCommand|设置大区传送倍率
WarpToLifeCommand|传送到生物所在地图，不包含玩家
ZakumCommand|在当前位置召唤扎昆
DebugCommand|打印debug信息
IpListCommand|打印所有玩家ip列表
SetCommand|把传入的参数存到服务端变量中，用于后续测试
ShowMoveLifeCommand|打印生物移动信息开关
ShowPacketsCommand|打印服务端收包开关
ShowSessionsCommand|打印sessions信息
ClearQuestCacheCommand|重载任务
ClearQuestCommand|重载指定任务
DCAllCommand|下线所有玩家
DevtestCommand|运行devtest.js脚本，在不重启服务的情况下测试某些内容
EraseAllPNpcsCommand|移除所有玩家NPC
GetAccCommand|展示指定玩家的账号名
MapPlayersCommand|展示当前地图所有玩家
SaveAllCommand|玩家存档
ServerAddChannelCommand|给指定大区添加频道
ServerAddWorldCommand|添加新大区
ServerRemoveChannelCommand|移除指定大区的最后一个频道
ServerRemoveWorldCommand|移除最后一个大区
SetGmLevelCommand|给玩家设置GM
ShutdownCommand|关服
SpawnAllPNpcsCommand|将所有玩家在当前地图生成玩家NPC
SupplyRateCouponCommand|倍率卡使用开关
WarpWorldCommand|转区";

    static Dictionary<string, string> descMap;

    static Dictionary<string, string> DescMap
    {
        get
        {
            if (descMap != null) return descMap;
            descMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in Descriptions.Split('\n'))
            {
                var t = line.Trim();
                int p = t.IndexOf('|');
                if (p <= 0) continue;
                descMap[t.Substring(0, p)] = t.Substring(p + 1);
            }
            return descMap;
        }
    }

    public static void Register(AdminApi api)
    {
        api.Post("/command/v1/getCommandListFromDB", GetList);
        api.Post("/command/v1/updateCommand", Update);
        api.Get("/command/v1/reloadEventsByGMCommand", ctx =>
        {
            ReloadScripts?.Invoke();
            return null;
        });
        api.Get("/command/v1/reloadPortalsByGMCommand", ctx =>
        {
            ReloadMaps?.Invoke();
            return null;
        });
        api.Get("/command/v1/reloadMapsByGMCommand", ctx =>
        {
            ReloadMaps?.Invoke();
            return null;
        });
    }

    // CommandService.loadCommands: level 0..6, enabled rows, first syntax wins.
    // Reuse the same seed and i18n catalog as the administration endpoint.
    public static List<KeyValuePair<string, string>>[] ScriptCommandDescriptions(DbStore db)
    {
        EnsureSeed(db);
        var groups = new List<KeyValuePair<string, string>>[7];
        var registered = new HashSet<string>(StringComparer.Ordinal);
        for (int level = 0; level < groups.Length; level++)
        {
            groups[level] = new List<KeyValuePair<string, string>>();
            foreach (var row in db.Table("command_info").rows)
            {
                if (DbStore.Int(row, "level") != level || DbStore.Int(row, "enabled") == 0) continue;
                // A missing command class cannot be registered by the Java service.
                if (!DescMap.TryGetValue(DbStore.Str(row, "clazz"), out string description)) continue;
                string syntax = DbStore.Str(row, "syntax").ToLowerInvariant();
                if (!registered.Add(syntax)) continue;
                groups[level].Add(new KeyValuePair<string, string>(syntax, description));
            }
        }
        return groups;
    }

    static void EnsureSeed(DbStore db)
    {
        if (!db.HasTable("command_info")) return;
        var table = db.Table("command_info");
        if (table.rows.Count > 0) return;
        foreach (var line in Seed.Split('\n'))
        {
            var t = line.Trim();
            if (t.Length == 0 || t[0] == '#') continue;
            var parts = t.Split('|');
            if (parts.Length < 3) continue;
            table.Insert(new DbRow
            {
                ["syntax"] = parts[0],
                ["clazz"] = parts[1],
                ["level"] = long.Parse(parts[2]),
                ["default_level"] = long.Parse(parts[2]),
                ["enabled"] = 1L,
            });
        }
        db.Save();
    }

    static object GetList(AdminCtx ctx)
    {
        var form = AdminJson.AsDict(ctx.Body) ?? new Dictionary<string, object>();
        int pageNo = AdminJson.GetInt(form, "pageNo", 1);
        int pageSize = AdminJson.GetInt(form, "pageSize", 20);
        bool onlyTotal = AdminJson.GetBool(form, "onlyTotal");
        bool notPage = AdminJson.GetBool(form, "notPage");
        string syntax = AdminJson.GetString(form, "syntax");
        var levelList = AdminJson.GetList(form, "levelList");
        var defaultLevelList = AdminJson.GetList(form, "defaultLevelList");
        bool hasEnabled = AdminJson.Get(form, "enabled") != null;
        bool enabled = AdminJson.GetBool(form, "enabled");

        return AdminMainThread.Run(() =>
        {
            var db = AdminBackend.RequireDb();
            EnsureSeed(db);

            var rows = new List<DbRow>();
            foreach (var row in db.Table("command_info").rows)
            {
                long level = DbStore.Long(row, "level");
                long defaultLevel = DbStore.Long(row, "default_level");
                if (levelList != null && levelList.Count > 0 && !Contains(levelList, level)) continue;
                if (defaultLevelList != null && defaultLevelList.Count > 0 && !Contains(defaultLevelList, defaultLevel)) continue;
                if (!string.IsNullOrEmpty(syntax) &&
                    DbStore.Str(row, "syntax").IndexOf(syntax, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (hasEnabled && (DbStore.Long(row, "enabled") != 0) != enabled) continue;
                rows.Add(row);
            }
            rows.Sort((a, b) =>
            {
                int byLevel = DbStore.Long(a, "default_level").CompareTo(DbStore.Long(b, "default_level"));
                return byLevel != 0 ? byLevel : DbStore.Str(a, "syntax").CompareTo(DbStore.Str(b, "syntax"));
            });

            return AdminDbUtil.Page(rows, pageNo, pageSize, onlyTotal, notPage, Dto);
        });
    }

    static bool Contains(List<object> list, long value)
    {
        foreach (var item in list)
            if (AdminJson.GetLong(new Dictionary<string, object> { ["v"] = item }, "v") == value) return true;
        return false;
    }

    static Dictionary<string, object> Dto(DbRow row)
    {
        string clazz = DbStore.Str(row, "clazz");
        string description;
        if (!DescMap.TryGetValue(clazz, out description)) description = "";
        return AdminJson.Obj(
            "id", DbStore.Long(row, "id"),
            "level", DbStore.Long(row, "level"),
            "syntax", DbStore.Str(row, "syntax"),
            "defaultLevel", DbStore.Long(row, "default_level"),
            "clazz", clazz,
            "description", description,
            "enabled", DbStore.Long(row, "enabled") != 0);
    }

    // 源端 updateCommand 只允许改 level / enabled（syntax/defaultLevel/clazz 不可改）
    static object Update(AdminCtx ctx)
    {
        var form = AdminJson.AsDict(ctx.Body);
        if (form == null) throw new AdminApiException("缺少命令数据");
        long id = AdminJson.GetLong(form, "id");

        return AdminMainThread.Run(() =>
        {
            var db = AdminBackend.RequireDb();
            EnsureSeed(db);
            var table = db.Table("command_info");
            var row = table.Find(DbStore.Eq("id", id));
            if (row == null) throw new AdminApiException("命令不存在: " + id);

            var values = new DbRow();
            if (AdminJson.Get(form, "level") != null) AdminDbUtil.Set(values, "level", AdminJson.GetLong(form, "level"));
            if (AdminJson.Get(form, "enabled") != null) AdminDbUtil.Set(values, "enabled", AdminJson.GetBool(form, "enabled") ? 1L : 0L);
            table.Update(DbStore.Eq("id", id), values);
            db.Save();
            return (object)Dto(table.Find(DbStore.Eq("id", id)));
        });
    }
}
