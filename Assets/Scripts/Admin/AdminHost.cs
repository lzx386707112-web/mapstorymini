// 后台管理系统宿主：在游戏进程里起一个只监听 127.0.0.1 的 HTTP 服务，
// 把后台前端（gms-ui 的构建产物）和 REST 接口一起供出去；侧边栏按钮用系统浏览器打开它。
//
// 生命周期：
//   · RuntimeInitializeOnLoadMethod 建一个常驻 GameObject（不进场景、不随场景销毁）
//   · 第一次需要时（侧边栏点击 / 游戏启动后自动）才真正监听端口，失败不影响游戏
//   · 每帧排空 AdminMainThread 队列：HTTP 线程只入队，DB 读写一律回主线程
//
// 为什么必须 runInBackground=true：点开浏览器后游戏切到后台，如果 Unity 主线程停了，
// 请求就会一直等到超时。开启后主线程继续跑，后台页面才拿得到数据。
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public sealed class AdminHost : MonoBehaviour
{
    public const int DefaultPort = 8686;
    public const string ResourcePath = "AdminUi/adminui";

    static AdminHost instance;

    AdminHttpServer server;
    AdminApi api;
    string startError;
    bool adminChecked;

    public static bool Running => instance != null && instance.server != null && instance.server.Running;
    public static int Port => instance != null && instance.server != null ? instance.server.Port : DefaultPort;
    public static string BaseUrl => "http://127.0.0.1:" + Port + "/";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (instance != null) return;
        var go = new GameObject("AdminHost");
        UnityEngine.Object.DontDestroyOnLoad(go);
        instance = go.AddComponent<AdminHost>();
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;

        AdminMainThread.MainThreadId = Thread.CurrentThread.ManagedThreadId;
        AdminMainThread.Pumping = true;
        AdminMainThread.OnError = e => Debug.LogError("[Admin] 主线程任务异常: " + e);

        AdminBackend.Broadcast = msg =>
        {
            try
            {
                // 单机版没有独立聊天服，广播落到快捷栏上方的提示条
                BottomBar.Toast(msg);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Admin] 广播失败: " + e.Message);
            }
        };
        AdminBackend.CurrentCharacterId = () => GameSession.CharacterId;

        // 后台列表的名字解析（掉落/商店/扭蛋 DTO 里的 itemName/dropperName/questName）：
        // AdminNames 是函数槽（admin-check 离线时退「ID: xxx」兜底），这里接上 WZ 真实现。
        AdminNames.Mob = WzNames.Mob;
        AdminNames.Npc = WzNames.Npc;
        AdminNames.Item = WzNames.Item;
        AdminNames.Map = WzNames.Map;
        AdminNames.ItemDesc = ItemCatalog.Desc;
        AdminNames.Quest = id =>
        {
            var n = WzAssets.FileRoot("Quest/QuestInfo.img").Get(id.ToString())?.Text("name");
            return string.IsNullOrEmpty(n) ? null : n;
        };

        // 浏览器在前台时游戏要继续跑，否则 HTTP 请求等不到主线程
        Application.runInBackground = true;
    }

    void Update()
    {
        AdminMainThread.Pump();

        if (!adminChecked && GameServer.Local != null)
        {
            adminChecked = true;
            try
            {
                string created = AdminBackend.EnsureWebAdmin();
                if (!string.IsNullOrEmpty(created)) Debug.Log("[Admin] " + created);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Admin] 初始化后台账号失败: " + e.Message);
            }
        }
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        StopServer();
    }

    void OnApplicationQuit()
    {
        StopServer();
    }

    // —————————— 对外：启动 / 打开 ——————————
    // 返回可打开的地址；失败时抛异常，由调用方提示用户。
    public static string EnsureStarted()
    {
        if (instance == null) Boot();
        if (instance == null) throw new InvalidOperationException("后台宿主未初始化");

        if (instance.server != null && instance.server.Running) return BaseUrl;
        if (!string.IsNullOrEmpty(instance.startError)) throw new InvalidOperationException(instance.startError);

        instance.StartServer();
        return BaseUrl;
    }

    // 侧边栏入口：启动服务并用系统默认浏览器打开。返回给用户看的提示文本。
    public static string OpenInBrowser()
    {
        string url = EnsureStarted();
        Application.OpenURL(url);
        return url;
    }

    void StartServer()
    {
        try
        {
            api = new AdminApi { Log = msg => Debug.Log("[Admin] " + msg) };

            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset != null && asset.bytes != null && asset.bytes.Length > 0)
            {
                api.Statics = AdminStaticBundle.Load(asset.bytes);
                Debug.Log("[Admin] 后台前端资源包已加载，共 " + api.Statics.Count + " 个文件");
            }
            else
            {
                Debug.LogWarning("[Admin] 找不到 Resources/" + ResourcePath + ".bytes，后台页面将只显示构建提示。" +
                                 "先跑 tools/build_admin_ui.sh 生成。");
            }

            AdminRoutes.Register(api);
            AdminRoutes.RegisterUnityOnly(api);   // WZ 依赖模块（图鉴/任务/商城/文件树/套装）
            // 假人计数钩子：AdminBots 不直接引用 Server.Bot，离线自检才编得过
            AdminBots.ActiveBots = () => Server.Bot.BotRegistry.Count();
            AdminBots.MarketStartupComplete = () => Server.Bot.Environment.BotEnvironment.IsMarketStartupComplete;
            AdminBots.MarketStartupRunning = () => Server.Bot.Environment.BotEnvironment.IsMarketStartupRunning;
            AdminBots.RequestMarketStartup = Server.Bot.Environment.BotEnvironment.RequestMarketStartup;

            server = AdminHttpServer.StartOn(DefaultPort, api.Handle, msg => Debug.Log("[Admin] " + msg));
            // 图鉴懒加载缓存的后台预热：装备 3.2 万 IMG 首建要数秒，别让后台页面首开卡住
            AdminCatalogData.Prewarm();
            Debug.Log("[Admin] 后台管理已就绪: " + BaseUrl + "（仅本机可访问）");
            startError = null;
        }
        catch (Exception e)
        {
            startError = "后台服务启动失败：" + e.Message;
            Debug.LogError("[Admin] " + startError);
            throw;
        }
    }

    void StopServer()
    {
        try { server?.Dispose(); }
        catch (Exception) { }
        server = null;
    }

    // —————————— 编辑器自检用 ——————————
    public static AdminHttpServer ServerForTest => instance != null ? instance.server : null;
}
