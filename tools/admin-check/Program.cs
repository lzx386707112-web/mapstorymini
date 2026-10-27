// 后台管理系统离线自检：不开 Unity，直接用临时库起 HTTP 服务，把主要接口打一遍。
//
// 目的：后台前端是 axios 直连的，接口形状（code=20000 + camelCase 字段）一旦写错，
// 在 Unity 里要等进游戏点开浏览器才能发现。这里用 HttpClient 走真实 TCP，
// 覆盖 登录 → 账号列表 → 参数列表 → 背包列表 → 静态资源 这条主链路。
//
//   cd tools/admin-check && dotnet run
//
// 退出码 0 = 全部通过。
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AdminCheck
{
    static class Program
    {
        static int failures;
        static HttpClient http;
        static string baseUrl;
        static DbStore DbRef;

        static async Task<int> Main(string[] args)
        {
            string dbPath = Path.Combine(Path.GetTempPath(), "maple-admin-check-" + Guid.NewGuid().ToString("N") + ".db");
            var db = DbStore.Open(dbPath);
            DbRef = db;
            AdminBackend.DbOverride = db;
            AdminMainThread.Pumping = false;

            string created = AdminBackend.EnsureWebAdmin();
            Console.WriteLine("初始化后台账号: " + (created ?? "已存在 webadmin 账号"));

            // 造一个账号 + 角色 + 背包道具，覆盖列表类接口
            int accId = AccountsDao.AutoRegister(db, "tester", "tester123");
            db.Table("characters").Insert(new DbRow
            {
                ["accountid"] = (long)accId,
                ["name"] = "TestHero",
                ["level"] = 12L,
                ["job"] = 100L,
                ["map"] = 100000000L,
            });
            long charId = 0;
            foreach (var r in db.Table("characters").Select(DbStore.Eq("accountid", (long)accId)))
                charId = Convert.ToInt64(r["id"]);
            db.Table("inventoryitems").Insert(new DbRow
            {
                ["type"] = 0L,
                ["characterid"] = charId,
                ["accountid"] = (long)accId,
                ["itemid"] = 1302000L,
                ["inventorytype"] = 1L,
                ["position"] = 1L,
                ["quantity"] = 1L,
            });
            db.Save();

            var api = new AdminApi { Log = m => Console.WriteLine("  [srv] " + m) };
            var bundlePath = Path.Combine("../../Assets/Resources/AdminUi/adminui.bytes");
            if (File.Exists(bundlePath))
            {
                api.Statics = AdminStaticBundle.Load(File.ReadAllBytes(bundlePath));
                Console.WriteLine("静态资源包: " + api.Statics.Count + " 个文件");
            }
            else
            {
                Console.WriteLine("静态资源包: 未生成（跳过静态检查）");
            }

            AdminRoutes.Register(api);
            var server = AdminHttpServer.StartOn(18686, api.Handle, m => Console.WriteLine("  [srv] " + m));
            baseUrl = "http://127.0.0.1:" + server.Port;
            http = new HttpClient { BaseAddress = new Uri(baseUrl) };

            // --serve：只起服务不跑断言，方便用浏览器人工验界面
            if (args != null && Array.IndexOf(args, "--serve") >= 0)
            {
                Console.WriteLine("服务已启动: " + baseUrl + "  (Ctrl+C 退出)");
                Console.WriteLine("后台账号: admin / admin");
                await Task.Delay(-1);
                return 0;
            }

            try
            {
                await RunAll();
            }
            finally
            {
                server.Dispose();
                http.Dispose();
                try { File.Delete(dbPath); } catch (Exception) { }
            }

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "全部通过" : failures + " 项失败");
            return failures == 0 ? 0 : 1;
        }

        static async Task RunAll()
        {
            // —— 登录 ——
            var login = await Post("/auth/v1/login", new { username = "admin", password = "admin" }, auth: false);
            Check("登录返回 20000", login.Code == 20000, login.Raw);
            string token = login.Json.RootElement.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object &&
                           d.TryGetProperty("token", out var t) ? t.GetString() : null;
            Check("拿到 token", !string.IsNullOrEmpty(token), login.Raw);
            if (string.IsNullOrEmpty(token)) return;
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // —— 身份 ——
            var info = await Get("/account/v1/info");
            Check("账号信息 webadmin=true", info.Code == 20000 && info.Raw.Contains("\"webadmin\":true"), info.Raw);

            var menu = await Get("/account/v1/menu");
            Check("菜单接口可访问", menu.Code == 20000, menu.Raw);

            // —— 账号列表 ——
            var accounts = await Get("/account/v1?page=1&size=20");
            Check("账号列表分页", accounts.Code == 20000 && accounts.Raw.Contains("\"records\"") && accounts.Raw.Contains("tester"), accounts.Raw);

            // —— 角色 ——
            var online = await Post("/character/v1/online/list", new { pageNo = 1, pageSize = 20 });
            Check("角色列表含测试角色", online.Code == 20000 && online.Raw.Contains("TestHero"), online.Raw);

            var maps = await Get("/account/v1/" + AccountIdOf(accounts) + "/characters/maps");
            Check("角色所在图", maps.Code == 20000 && maps.Raw.Contains("characterName"), maps.Raw);

            // —— 背包 ——
            var invTypes = await Get("/inventory/v1/getInventoryTypeList");
            Check("背包类型列表", invTypes.Code == 20000 && invTypes.Raw.Contains("装备"), invTypes.Raw);

            var inv = await Post("/inventory/v1/getInventoryList", new { characterId = 0, pageNo = 1, pageSize = 20 });
            Check("背包列表", inv.Code == 20000 && inv.Raw.Contains("1302000"), inv.Raw);

            // —— 服务端参数 ——
            var cfgTypes = await Get("/config/v1/getConfigTypeList");
            Check("参数类型列表", cfgTypes.Code == 20000 && cfgTypes.Raw.Contains("world"), cfgTypes.Raw);

            var cfg = await Post("/config/v1/getConfigList", new { type = "world", pageNo = 1, pageSize = 50 });
            Check("参数分页", cfg.Code == 20000 && cfg.Raw.Contains("configCode"), cfg.Raw);

            var addCfg = await Post("/config/v1/addConfig", new
            {
                configType = "server",
                configSubType = "Check",
                configClazz = "java.lang.Integer",
                configCode = "admin_check_probe",
                configValue = "1",
                configDesc = "自检写入",
            });
            Check("新增参数", addCfg.Code == 20000, addCfg.Raw);

            // —— 状态 / 未接入兜底 ——
            var onlineState = await Get("/server/v1/online");
            Check("服务器在线状态", onlineState.Code == 20000 && onlineState.Raw.Contains("true"), onlineState.Raw);

            // —— 新接入的 DB 类模块（掉落/商店/扭蛋/封禁/命令）——
            var drops = await Post("/drop/v1/getDropList", new { pageNo = 1, pageSize = 10 });
            Check("掉落列表", drops.Code == 20000 && drops.Raw.Contains("\"records\""), drops.Raw);

            var shop = await Post("/shop/v1/getShopList", new { pageNo = 1, pageSize = 10 });
            Check("商店列表", shop.Code == 20000 && shop.Raw.Contains("\"records\""), shop.Raw);

            var pools = await Post("/gachapon/v1/getPools", new { pageNo = 1, pageSize = 10 });
            Check("扭蛋奖池列表", pools.Code == 20000, pools.Raw);

            var autoban = await Get("/autoban/v1/getConfigList");
            Check("自动封禁配置", autoban.Code == 20000 && autoban.Raw.Contains("points"), autoban.Raw);

            var cmds = await Post("/command/v1/getCommandListFromDB", new { pageNo = 1, pageSize = 20 });
            Check("GM 命令库", cmds.Code == 20000 && cmds.Raw.Contains("syntax"), cmds.Raw);

            // —— 天气（NativeWeather.json 直读，纯 C#）——
            var weather = await Get("/weather/v1/state");
            Check("天气状态", weather.Code == 20000 && weather.Raw.Contains("minuteOfDay"), weather.Raw);

            var weatherCfg = await Get("/weather/v1/config");
            Check("天气配置", weatherCfg.Code == 20000 && weatherCfg.Raw.Contains("dayLengthMs"), weatherCfg.Raw);

            var weatherRegions = await Get("/weather/v1/regions");
            Check("天气区域列表", weatherRegions.Code == 20000 && weatherRegions.Raw.Contains("currentProfile"), weatherRegions.Raw);

            // —— 假人控制（solo_mapling_* 配置）——
            var botCfg = await Get("/bot-control/v1/config");
            Check("假人配置分组", botCfg.Code == 20000 && botCfg.Raw.Contains("solo_mapling_market_bot_max"), botCfg.Raw);

            var botState = await Get("/bot-control/v1/state");
            Check("假人状态", botState.Code == 20000 && botState.Raw.Contains("activeBots"), botState.Raw);

            var stub = await Post("/drop/v1/getDropList", new { pageNo = 1, pageSize = 10 });
            Check("未接入模块返回空数据而不是报错", stub.Code == 20000, stub.Raw);

            // —— 鉴权 ——
            http.DefaultRequestHeaders.Authorization = null;
            var unauth = await Get("/account/v1?page=1&size=1");
            Check("未登录返回 401", unauth.Status == 401, unauth.Raw);

            // —— 封停必须真的挡住登录（后台「封停」按钮的实际意义）——
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            long testerId = AccountIdOf(accounts);
            var ban = await Send(HttpMethod.Put, "/account/v1/" + testerId + "/ban", new { reason = "自检封停" }, true);
            Check("封停接口返回成功", ban.Code == 20000, ban.Raw);
            var bannedLogin = LoginService.Login(DbRef, "tester", "tester123");
            Check("封停后登录被拒", bannedLogin.code == LoginResultCode.Banned, bannedLogin.code.ToString());

            var unban = await Send(HttpMethod.Put, "/account/v1/" + testerId + "/unban", null, true);
            Check("解封接口返回成功", unban.Code == 20000, unban.Raw);
            var okLogin = LoginService.Login(DbRef, "tester", "tester123");
            Check("解封后可以登录", okLogin.code == LoginResultCode.Ok, okLogin.code.ToString());

            // —— 静态资源 ——
            if (File.Exists(Path.Combine("../../Assets/Resources/AdminUi/adminui.bytes")))
            {
                var index = await http.GetAsync("/");
                string html = await index.Content.ReadAsStringAsync();
                Check("首页返回 HTML", index.IsSuccessStatusCode && html.Contains("<div id=\"app\">"), html.Substring(0, Math.Min(120, html.Length)));
            }
        }

        static long AccountIdOf(Result accounts)
        {
            try
            {
                var records = accounts.Json.RootElement.GetProperty("data").GetProperty("records");
                foreach (var r in records.EnumerateArray())
                    if (r.GetProperty("name").GetString() == "tester") return r.GetProperty("id").GetInt64();
            }
            catch (Exception) { }
            return 0;
        }

        sealed class Result
        {
            public int Status;
            public int Code;
            public string Raw = "";
            public JsonDocument Json;
        }

        // 模拟前端 axios：POST/PUT 的 body 会包一层 { requestId, data }
        static async Task<Result> Post(string path, object body, bool auth = true)
            => await Send(HttpMethod.Post, path, body, auth);

        static async Task<Result> Send(HttpMethod method, string path, object body, bool auth)
        {
            var req = new HttpRequestMessage(method, path);
            if (body != null)
            {
                var payload = new Dictionary<string, object> { { "requestId", Guid.NewGuid().ToString() }, { "data", body } };
                req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            }
            var res = await http.SendAsync(req);
            return await Read(res);
        }

        static async Task<Result> Get(string path)
        {
            var res = await http.GetAsync(path);
            return await Read(res);
        }

        static async Task<Result> Read(HttpResponseMessage res)
        {
            string raw = await res.Content.ReadAsStringAsync();
            var r = new Result { Status = (int)res.StatusCode, Raw = raw };
            try
            {
                r.Json = JsonDocument.Parse(raw);
                if (r.Json.RootElement.ValueKind == JsonValueKind.Object &&
                    r.Json.RootElement.TryGetProperty("code", out var code) &&
                    code.ValueKind == JsonValueKind.Number)
                    r.Code = code.GetInt32();
            }
            catch (Exception) { }
            return r;
        }

        static void Check(string name, bool ok, string detail)
        {
            Console.WriteLine((ok ? "  OK   " : "  FAIL ") + name + (ok ? "" : "\n         " + Trim(detail)));
            if (!ok) failures++;
        }

        static string Trim(string s) => string.IsNullOrEmpty(s) ? "" : (s.Length > 300 ? s.Substring(0, 300) + "…" : s);
    }
}
