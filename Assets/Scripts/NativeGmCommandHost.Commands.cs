using System;
using System.Collections.Generic;
using Server.Bot;
using Server.Field;
using UnityEngine;

// GM 指令的「场景侧」实现（服务端 NativeGmCommands 鉴权后才会进来）。
// 约定与主文件一致：能碰 Unity 场景的放这里，纯数据/DB 的留在 Server/World/NativeGmCommands.cs。
public static partial class NativeGmCommandHost
{
    static NativeCommandResult Extended(CharRecord c, string name, string[] a, string raw)
    {
        var player = GameLoop.I?.player;
        var field = GameLoop.I?.field;
        if (player == null || field?.Map == null) return NativeCommandResult.Fail("请先进入地图");
        var r = new NativeCommandResult();
        var db = GameSession.Server.Db;
        int px = Mathf.RoundToInt(player.FeetPos.x * Art.PPU), py = Mathf.RoundToInt(-player.FeetPos.y * Art.PPU);
        switch (name)
        {
            // —————— 展示类（源端也是打开原版窗口/原版脚本）——————
            case "GachaCommand":
                NpcSession.Talk(9900001, "gachaponInfo");
                if (NpcSession.Active?.LastError != null) throw new ArgumentException(NpcSession.Active.LastError);
                r.messages.Add("已打开扭蛋机奖励"); return r;

            // —————— 传送类 ——————
            case "JailCommand":
                Warp(field, player, 300000012, null);
                r.messages.Add("已送入监狱地图 300000012"); return r;
            case "UnJailCommand":
                Warp(field, player, 100000000, null);
                r.messages.Add("已离开监狱（100000000）"); return r;
            case "WarpMapCommand":           // 源端是「本图所有玩家」，单机只有自己
            case "WarpAreaCommand":
                Need(a, 1, "!" + name.Replace("Command", "").ToLowerInvariant() + " <地图ID>");
                Warp(field, player, N(a), null);
                r.messages.Add("已传送到地图 " + N(a)); return r;
            case "WarpToLifeCommand":
            {
                // 源端按生物（NPC/怪物）找所在地图。这里只在已解析的地图里找：
                // 6336 张图全量解析会卡住主线程，不做全图扫描。
                Need(a, 1, "!warptolife <NPC或怪物ID>"); int life = N(a);
                foreach (var entry in FieldCatalog.Maps)
                {
                    var map = entry.Value; bool found = false;
                    foreach (var npc in map.npcs) if (npc.id == life) { found = true; break; }
                    if (!found) foreach (var spawn in map.spawns) if (spawn.mob == life) { found = true; break; }
                    if (!found) continue;
                    Warp(field, player, entry.Key, null);
                    r.messages.Add("已在 " + entry.Key + " " + map.name + " 找到 " + life); return r;
                }
                throw new ArgumentException("已解析的地图里没有该生物：" + life + "（进图后地图才会解析）");
            }

            // —————— buff / 状态 ——————
            case "DebuffCommand":
            {
                // 源端给附近玩家上 debuff；单机只有自己，等价于清掉自己身上的 buff。
                var buffs = NativeBuffs.For(player);
                int count = buffs.Visible.Count;
                buffs.Dispel();
                r.messages.Add("已清除 " + count + " 个临时状态"); return r;
            }
            case "HealMapCommand":
                player.hp = player.hpMax; player.mp = player.mpMax;
                GameSession.SaveToDb();
                r.messages.Add("血蓝已回满（本图只有你一个真人）"); return r;
            case "KillMapCommand":
                // 源端杀本图所有玩家；单机就是自己，走正常死亡流程而不是直接清 HP。
                player.hp = 0; GameLoop.I?.OnPlayerDead();
                r.messages.Add("已按源端语义结束本图玩家（自己）的生命"); return r;

            // —————— 掉落 ——————
            case "ItemVacCommand":
            case "ForceVacCommand":
            {
                if (GroundItem.AuthoritativePickup == null) throw new ArgumentException("地面掉落桥未就绪，请重新进入地图");
                var drops = new List<MapItem>(MapItemService.DroppedItems((int)c.map));
                int picked = 0;
                foreach (var drop in drops)
                    if (GroundItem.AuthoritativePickup(drop)) picked++;
                r.messages.Add("已捡起 " + picked + " / " + drops.Count + " 件掉落"); return r;
            }
            case "ReloadDropsCommand":
                DropStore.Invalidate();
                r.messages.Add("掉落表缓存已重载"); return r;
            case "ReloadShopsCommand":
                ScriptSource.ClearCache();
                r.messages.Add("商店脚本缓存已重载"); return r;

            // —————— 生成 NPC / 怪物 ——————
            case "NpcCommand":
            case "PnpcCommand":
            case "PlayerNpcCommand":
            {
                Need(a, 1, "!" + name.Replace("Command", "").ToLowerInvariant() + " <NPCID>");
                int npcId = N(a);
                var runtime = Runtime(field.Map.id);
                runtime.npcs.Add(new MapNpcPosition { id = npcId, x = px, y = py, dynamic = true });
                r.messages.Add("已在当前位置生成 NPC " + npcId + "（换图后消失）"); return r;
            }
            case "PnpcRemoveCommand":
            case "PlayerNpcRemoveCommand":
            case "EraseAllPNpcsCommand":
            {
                var runtime = Runtime(field.Map.id);
                int removed = runtime.npcs.RemoveAll(x => x.dynamic);
                if (removed > 0) field.ReloadCurrentMap();
                r.messages.Add("已移除 " + removed + " 个动态 NPC" + (removed > 0 ? "（重进本图后画面同步）" : "")); return r;
            }
            case "SpawnAllPNpcsCommand":
                throw new ArgumentException("玩家 NPC 用真实角色 id 生成，本服假人不是 NPC 素材，无法按源端生成");
            case "PmobCommand":
            {
                Need(a, 1, "!pmob <怪物ID>");
                int mob = N(a);
                FieldCatalog.LoadMob(mob);
                if (!FieldCatalog.Mobs.ContainsKey(mob)) throw new ArgumentException("怪物不存在：" + mob);
                if (field.SpawnNativeMob(mob, player.transform.position) == null) throw new ArgumentException("怪物生成失败：" + mob);
                r.messages.Add("已生成永久怪物 " + mob + "（换图后消失）"); return r;
            }
            case "PmobRemoveCommand":
            {
                int killed = 0;
                foreach (var mob in EnemyUnit.All.ToArray())
                    if (mob != null && !mob.dead && mob.fieldMode) { mob.TakeBotHit((int)c.id, int.MaxValue, 0); killed++; }
                r.messages.Add("已清除本图 " + killed + " 只怪物"); return r;
            }
            case "ZakumCommand":
            case "HorntailCommand":
            case "CakeCommand":
            {
                // 源端这三个是「多部件联合体」。本移植没有武装/身体的联合状态机，
                // 只生成主体（扎昆 8800000 / 黑龙 8810000），部件用 !spawn 单独补。
                int body = name == "ZakumCommand" ? 8800000 : name == "HorntailCommand" ? 8810000 : 0;
                if (body == 0) throw new ArgumentException("本移植没有巨型蛋糕 BOSS 的素材与血量配置，请用 !spawn <怪物ID>");
                if (field.SpawnNativeMob(body, player.transform.position) == null) throw new ArgumentException("BOSS 生成失败：" + body);
                r.messages.Add("已生成 " + (name == "ZakumCommand" ? "扎昆主体 8800000" : "黑龙主体 8810000") + "（多部件联合体未迁移，部件请用 !spawn）");
                return r;
            }

            // —————— 传送门开关 ——————
            case "OpenPortalCommand":
            case "ClosePortalCommand":
            {
                Need(a, 1, "!" + name.Replace("Command", "").ToLowerInvariant() + " <传送门名>");
                var portal = field.Map.Portal(a[0]) ?? field.Map.Portal(a[0].ToUpperInvariant());
                if (portal == null) throw new ArgumentException("本图没有这个传送门：" + a[0]);
                portal.enabled = name == "OpenPortalCommand";
                r.messages.Add("传送门 " + portal.name + " 已" + (portal.enabled ? "开启" : "关闭")); return r;
            }

            // —————— 环境 ——————
            case "NightCommand":
            {
                var weather = GameServer.Local?.Weather;
                if (weather == null) throw new ArgumentException("天气服务未启动");
                // 源端把当前地图设成夜晚；这里把全局时钟压到 22:00 保持 30 分钟。
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                weather.Override(null, 22 * 60, 30 * 60 * 1000L, now);
                weather.Touch();
                r.messages.Add("已按 22:00 夜色覆盖（30 分钟）"); return r;
            }
            case "MuteMapCommand":
                GameMute.MapMuted = !GameMute.MapMuted;
                r.messages.Add("当前地图聊天已" + (GameMute.MapMuted ? "禁言" : "恢复")); return r;
            case "ToggleCouponCommand":
            {
                bool on = !GameConfigDao.GetBool(db, "rate_coupon_enabled", true);
                var row = db.Table("game_config").Find(x => DbStore.Str(x, "config_code") == "rate_coupon_enabled");
                if (row == null) db.Table("game_config").Insert(new DbRow { ["config_code"] = "rate_coupon_enabled", ["config_type"] = "world", ["config_value"] = on ? "1" : "0" });
                else row["config_value"] = on ? "1" : "0";
                r.changed = true;
                r.messages.Add("倍率卡已" + (on ? "开启" : "关闭")); return r;
            }
            case "ReloadHuntCommand":
                QuestCatalog.ClearCache();
                r.messages.Add("狩猎任务配置已重载"); return r;
            case "ChatCommand":
                throw new ArgumentException("源端只切换 GM 频道的白字显示；本移植聊天窗没有 GM 频道分支，开关不会改变任何显示");
            case "ShowMoveLifeCommand":
                throw new ArgumentException("本移植的生物移动是 BotScheduler/原生控制器的本地状态机，没有可打印的移动包链路");

            // —————— 会话 / 诊断 ——————
            case "DcCommand":
            case "DCAllCommand":
                r.messages.Add("已断开当前会话（本服只有你一个在线角色）");
                GameSession.DeselectChar();
                GameLoop.I?.ExitToCharSelect();
                return r;
            case "ShutdownCommand":
                r.messages.Add("服务端进程结束");
                Application.Quit();
                return r;
            case "ShowSessionsCommand":
                r.messages.Add("账号 " + GameSession.AccountId + " / 角色 " + c.name + "(" + c.id + ") 地图 " + c.map);
                r.messages.Add("假人 " + BotRegistry.Count() + " 只，本图 " + CountOnMap(field.Map.id) + " 只");
                return r;
            case "ShowPacketsCommand":
            case "MonitorCommand":
            case "MonitorsCommand":
                GmDiagnostics.PacketLog = !GmDiagnostics.PacketLog;
                r.messages.Add("封包日志打印已" + (GmDiagnostics.PacketLog ? "开启" : "关闭") + "（输出到 Unity 日志）"); return r;

            // —————— 假人开发指令（gm4）——————
            case "ArtificialPlayerCommand":
                if (a.Length > 0 && (a[0] == "remove" || a[0] == "delete"))
                {
                    int removed = 0;
                    foreach (var bot in new List<BotCharacter>(BotRegistry.OnMap(field.Map.id)))
                    { BotGeneration.RemoveBotFromServer(bot); removed++; }
                    r.messages.Add("已移除本图 " + removed + " 只假人"); return r;
                }
                BotGeneration.CreateBot(px, py, field.Map.id, true);
                r.messages.Add("已在当前位置生成一只假人"); return r;
            case "BotMoveCommand":
            {
                Need(a, 3, "!move <假人名> <x> <y>");
                BotCharacter target = null;
                foreach (var bot in BotRegistry.OnMap(field.Map.id))
                    if (string.Equals(bot.name, a[0], StringComparison.OrdinalIgnoreCase)) { target = bot; break; }
                if (target == null) throw new ArgumentException("本图没有这个假人：" + a[0]);
                BotGeneration.WarpBotToLocation(target, N(a, 1), N(a, 2), field.Map.id);
                r.messages.Add("已移动 " + target.name); return r;
            }
            case "EnvironmentCommand":
                if (a.Length > 0 && a[0] == "limit")
                {
                    if (a.Length > 1 && a[1] == "off") BotGeneration.DisableEnvironmentBotLimit();
                    else BotGeneration.EnableEnvironmentBotLimit();
                    r.messages.Add("环境假人配额已" + (a.Length > 1 && a[1] == "off" ? "关闭" : "开启")); return r;
                }
                r.messages.Add("假人总数 " + BotRegistry.Count() + "，本图 " + CountOnMap(field.Map.id) + " 只" +
                               (BotGeneration.EnvironmentBotLimitReached() ? "（已到环境配额）" : ""));
                return r;

            // —————— 计时器 ——————
            case "TimerCommand":
            case "TimerMapCommand":
            case "TimerAllCommand":
            {
                int seconds = a.Length > 0 ? N(a) : 0;
                if (seconds <= 0) throw new ArgumentException("用法：!" + name.Replace("Command", "").ToLowerInvariant() + " <秒>");
                GmTimers.Add(GameSession.CharacterId, seconds);
                r.messages.Add("已设置 " + seconds + " 秒计时，结束时会在聊天窗提示"); return r;
            }

            // —————— 公告 ——————
            case "RipCommand":
                Need(a, 1, "!rip <角色名> [原因]");
                NativeSystemMessage.Drop(6, "[封禁公告] " + a[0] + (a.Length > 1 ? "：" + raw.Substring(raw.IndexOf(' ') + 1) : " 已被封禁"));
                r.messages.Add("公告已发送"); return r;

            // —————— 本移植确实没有对应子系统的（明确说明，不假装成功）——————
            case "FlyCommand":
                throw new ArgumentException("本移植的移动物理没有飞行状态（NativeMovementPhysics 只有走/跳/爬），该指令无从生效");
            case "MobSkillCommand":
                throw new ArgumentException("怪物技能只有 NativeMobController 的自动施放链路，没有「给全图怪物指定技能」的入口");
            case "BombCommand":
                throw new ArgumentException("炸弹技能属于怪物技能表，本移植只迁移了技能视觉效果，没有该技能的伤害结算");
            case "MaxEnergyCommand":
                throw new ArgumentException("道场（Mu Lung Dojo）系统未迁移，没有能量条可加");
            case "SeedCommand":
                throw new ArgumentException("妙月种子是源端自定义道具集，本移植没有该道具清单");
            case "FishDropCommand":
                throw new ArgumentException("钓鱼系统未迁移");
            case "MapOwnerClaimCommand":
                throw new ArgumentException("地图归属是多人玩法，本服只有你一个真人");
            case "PeCommand":
                throw new ArgumentException("「读文件回放封包」需要抓包文件与真实 socket 层，本移植是进程内传输");
            case "DevtestCommand":
                throw new ArgumentException("devtest.js 依赖源端脚本宿主的测试接口，本移植没有对应脚本");
            case "StartEventCommand":
            case "EndEventCommand":
            case "StartMapEventCommand":
            case "StopMapEventCommand":
            case "JoinEventCommand":
            case "LeaveEventCommand":
                throw new ArgumentException("源端活动（GameEvent 管理/官方地图活动）未迁移；野外 BOSS 事件由地图脚本自行触发");
            case "OPQCommands":
            case "ReactorCommands":
            case "TestDevCommand":
            case "TradeBotTestCommand":
            case "FMBotCommand":
            case "ArtificialFreeMarketCommand":
                throw new ArgumentException("这是源端 gm4 的开发测试指令，本移植没有对应的测试脚本入口");
            default:
                return NativeCommandResult.Fail("此命令尚未接入：" + name);
        }
        r.messages.Add("命令执行完成");
        return r;
    }

    // 动态 NPC 要挂在地图运行时上（NativeMapView.SyncDynamicNpcs 每帧按 runtime.npcs 补画）。
    static MapRuntime Runtime(int mapId)
    {
        var runtime = MapService.Get(mapId);
        if (runtime == null)
        {
            var bots = GameServer.Local?.Bots;
            if (bots != null) { try { runtime = bots.GetMap(mapId); } catch (ArgumentException) { runtime = null; } }
        }
        if (runtime == null) throw new ArgumentException("当前地图没有运行时几何，无法动态生成：请重新进入地图");
        return runtime;
    }

    static int CountOnMap(int mapId)
    {
        int count = 0;
        foreach (var bot in BotRegistry.OnMap(mapId)) count++;
        return count;
    }
}

// !mutemap：源端只禁当前地图的聊天；本移植在发送前拦一道。
public static class GameMute
{
    public static bool MapMuted;
}

// !showpackets / !monitor(s)：本机没有 socket 层，开关落到「是否把协议回调打进日志」。
public static class GmDiagnostics
{
    public static bool PacketLog;
}

// !timer / !timermap / !timerall：源端是倒计时条，本移植先按「到时在聊天窗提示」落地。
// 每帧只跑一次（GUI 一帧会进多次），用 frameCount 去重。
public static class GmTimers
{
    struct Entry { public int owner; public double until; }
    static readonly List<Entry> entries = new List<Entry>();
    static int lastFrame = -1;

    public static int Count => entries.Count;

    public static void Add(int owner, int seconds)
    {
        entries.Add(new Entry { owner = owner, until = Time.realtimeSinceStartupAsDouble + seconds });
    }

    public static void Tick()
    {
        if (Time.frameCount == lastFrame || entries.Count == 0) return;
        lastFrame = Time.frameCount;
        double now = Time.realtimeSinceStartupAsDouble;
        for (int i = entries.Count - 1; i >= 0; i--)
            if (entries[i].until <= now)
            {
                entries.RemoveAt(i);
                NativeSystemMessage.Drop(6, "[计时] 时间到");
            }
    }
}
