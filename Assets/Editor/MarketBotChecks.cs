using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Server.Bot;
using Server.Bot.Environment;

// 自由市场假人 / 店铺实测自检：
//   用真实 DbStore + 真实 GameServer + 真实 BotRuntime 跑一遍环境启动（MarketEnvironmentStartup），
//   统计 910000000（入口）与 910000001..12（房间）上到底生成了多少假人、多少店铺。
//
//   Unity -batchmode -quit -projectPath <proj> -executeMethod MarketBotChecks.Run -logFile /tmp/fm_bots.log
//
// 判定口径：全部是运行时的真实数据，不 mock。
public static class MarketBotChecks
{
    const int FmEntrance = 910000000;
    const int RoomFirst = 910000001;
    const int RoomLast = 910000012;

    public static void Run()
    {
        string path = Path.Combine(Path.GetTempPath(), "mapledash-fm-bots-" + Guid.NewGuid() + ".db");
        var errors = new List<string>();
        Application.LogCallback hook = (condition, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception) errors.Add(condition);
        };
        try
        {
            var db = DbStore.Create(path);
            var server = GameServer.StartWith(db);
            Application.logMessageReceived += hook;

            // World/Bots 首次访问才创建 BotRuntime（其构造里会 RequestMarketStartup）
            BotEnvironment.StartupError = e =>
                Debug.LogError("[FmBot/Startup] 启动步骤异常：" + e);
            var bots = server.Bots;
            Debug.Log("[FmBot] BotRuntime 已创建，开始跑环境启动泵");

            long clock = 0;
            int frames = 0;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!BotEnvironment.IsMarketStartupComplete && frames < 60000 &&
                   watch.Elapsed.TotalSeconds < 240)
            {
                BotWorkPump.BeginFrame();
                clock += 16;
                bots.Drive(clock);
                frames++;
            }

            Debug.Log("[FmBot] 启动完成=" + BotEnvironment.IsMarketStartupComplete +
                      " 帧数=" + frames + " 用时=" + watch.Elapsed.TotalSeconds.ToString("F1") + "s" +
                      " 假人总数=" + BotRegistry.Count() + " 状态机=" + BotStore.GetAllBots().Count +
                      " 失败步骤=" + BotEnvironment.StartupErrorCount);
            DumpStartupState();

            // —— 入口 910000000 ——
            ReportMap(BotRegistry.OnMap(FmEntrance), "入口 910000000");

            // —— 房间 910000001..12：假人与店铺 ——
            int shopsTotal = 0, shopsOpen = 0, playerStores = 0, shopItems = 0;
            for (int map = RoomFirst; map <= RoomLast; map++)
            {
                var actors = new List<BotActor>(BotRegistry.OnMap(map));
                var shops = bots.shops.OnMap(0, 1, map);
                int open = 0, player = 0, items = 0;
                foreach (var shop in shops)
                {
                    if (shop.open) open++;
                    if (shop.playerStore) player++;
                    items += shop.items.Count;
                }
                shopsTotal += shops.Count; shopsOpen += open; playerStores += player; shopItems += items;
                Debug.Log("[FmBot] 房间 " + map + " 假人=" + actors.Count +
                          " 店铺=" + shops.Count + "(开=" + open + ",真人摊=" + player + ",条目=" + items + ")");
            }
            Debug.Log("[FmBot] 房间合计 店铺=" + shopsTotal + " 开=" + shopsOpen +
                      " 真人摊=" + playerStores + " 条目=" + shopItems);

            // —— 进店链路实测：真实角色注册进房间 → Read → Visit，全程走 protocol ——
            CheckVisit(db, server, bots, errors);

            Debug.Log("[FmBot] 运行期错误 " + errors.Count + " 条");
            for (int i = 0; i < Math.Min(12, errors.Count); i++)
                Debug.Log("[FmBot]   err: " + errors[i].Replace("\n", " "));
        }
        catch (Exception e)
        {
            Debug.LogError("[FmBot] 自检自身异常：" + e);
        }
        finally
        {
            Application.logMessageReceived -= hook;
            GameServer.Stop();
            if (File.Exists(path)) File.Delete(path);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }

    // 玩家视角的进店链路：与 FieldDirector.TickBots（SynchronizePlayer）和
    // NativeMarketWindow.Visit（protocol Visit）走同一条路，不 mock。
    static void CheckVisit(DbStore db, GameServer server, BotRuntime bots, List<string> errors)
    {
        try
        {
            int account = AccountsDao.AutoRegister(db, "MarketVisit", "test");
            var created = CharFactory.CreateCharacter(db, account, 0, "FmVisitor", 1,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            if (created.code != CharFactory.Ok) { errors.Add("建号失败 code=" + created.code); return; }
            var selected = server.Transport.CharSelect(new CharSelectRequest { accountId = account, characterId = created.characterId });
            if (!selected.ok) { errors.Add("选角失败"); return; }
            var character = selected.character;
            var actor = bots.SynchronizePlayer(character, RoomFirst, 100, 100, 1);
            Debug.Log("[FmBot/Visit] 玩家 actor id=" + actor.id + " map=" + actor.mapId +
                      " world=" + actor.world + " channel=" + actor.channel + " online=" + actor.online);

            var read = bots.marketProtocol.Handle(new MarketRequest { characterId = (int)created.characterId, operation = MarketOperation.Read });
            Debug.Log("[FmBot/Visit] Read ok=" + read.ok + " 店铺=" + read.shops.Count);
            if (!read.ok || read.shops.Count == 0) { errors.Add("房间店铺 Read 为空"); return; }

            // 每间店的关键 UI 输入：店员美术是否可加载（NativeMarketField 靠它出商人立绘）。
            // 真人摊（playerStore）的 permit 是 514xxxx 系，走 0503 默认皮肤，不在此检查。
            var permits = new HashSet<int>();
            foreach (var shop in read.shops) if (!shop.playerStore) permits.Add(shop.permitId);
            foreach (int permit in permits)
            {
                var node = WzAssets.Node("Item/Cash/0503.img/" + permit.ToString("D8") + "/employee/stand");
                Debug.Log("[FmBot/Visit] 店员美术 permit=" + permit + " -> " + (node != null ? "OK" : "缺失!"));
                if (node == null) errors.Add("0503 店员美术缺失 permit=" + permit);
            }

            var first = read.shops[0];
            var visit = bots.marketProtocol.Handle(new MarketRequest { characterId = (int)created.characterId,
                operation = MarketOperation.Visit, ownerId = first.ownerId });
            int items = visit.visiting?.items.Count ?? 0;
            Debug.Log("[FmBot/Visit] Visit 店主=" + first.ownerId + "(" + first.ownerName + ") ok=" + visit.ok +
                      " 窗口商品条目=" + items + (visit.ok ? "" : " err=" + visit.error));
            if (!visit.ok || visit.visiting == null) { errors.Add("Visit 失败: " + visit.error); return; }
            if (items == 0) errors.Add("店铺商品为空（窗口会没东西可买）");
        }
        catch (Exception e)
        {
            errors.Add("Visit 自检异常: " + e.Message);
            Debug.LogError("[FmBot/Visit] " + e);
        }
    }

    // 泵状态：startupStarted / startupComplete / steps 队列剩余 / 限额起始计数。
    static void DumpStartupState()
    {
        var flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        var t = typeof(BotEnvironment);
        object Get(string name) => t.GetField(name, flags)?.GetValue(null);
        var steps = Get("steps") as System.Collections.IEnumerable;
        int remaining = 0;
        if (steps != null)
        {
            var count = steps.GetType().GetProperty("Count");
            if (count != null) remaining = (int)count.GetValue(steps, null);
        }
        Debug.Log("[FmBot/State] startupStarted=" + Get("startupStarted")
                  + " startupComplete=" + Get("startupComplete")
                  + " steps剩余=" + remaining
                  + " environmentBotLimitStart=" + Get("environmentBotLimitStart")
                  + " 当前bot计数=" + BotGeneration.GetBotsCreatedCount());
    }

    static void ReportMap(IEnumerable<BotActor> actors, string label)
    {
        var types = new Dictionary<string, int>();
        int count = 0;
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (var actor in actors)
        {
            count++;
            var sm = BotStore.GetBotById(actor.id);
            string type = sm != null ? sm.botType : "(无状态机)";
            types.TryGetValue(type, out int n); types[type] = n + 1;
            minX = Math.Min(minX, actor.x); maxX = Math.Max(maxX, actor.x);
            minY = Math.Min(minY, actor.y); maxY = Math.Max(maxY, actor.y);
        }
        var sb = new System.Text.StringBuilder();
        foreach (var pair in types) sb.Append(pair.Key).Append('=').Append(pair.Value).Append(' ');
        string range = count > 0 ? " x[" + minX.ToString("F0") + "," + maxX.ToString("F0") + "] y[" +
            minY.ToString("F0") + "," + maxY.ToString("F0") + "]" : "";
        Debug.Log("[FmBot] " + label + " 假人=" + count + range + " 类型：" + sb);
    }
}
