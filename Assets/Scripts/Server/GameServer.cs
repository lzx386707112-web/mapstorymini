// GameServer：本地单机的「服务端」门面。
// 拥有 DbStore 与 ITransport；客户端只通过 ITransport 说话。
// 将来联机：在同一台机器起真实服务端，客户端把 InProcessTransport 换成 SocketTransport。
using System;

public sealed class GameServer
{
    public DbStore Db { get; private set; }
    public ITransport Transport { get; private set; }
    public IMapInfo MapInfo { get; set; }   // 客户端注入（FieldCatalog 数据）
    Server.Bot.BotRuntime bots;
    public event Action<Server.Bot.BotActor,int> TradeInventoryCommitted;
    public Server.Bot.BotActor ActiveBotPlayer(int id) => bots?.Player(id);
    public Server.Bot.BotRuntime Bots
    {
        get
        {
            if (bots==null)
            {
                bots=new Server.Bot.BotRuntime(Db,Social.BotMessenger,Social.Parties);
                bots.trades.InventoryCommitted+=(actor,delta)=>TradeInventoryCommitted?.Invoke(actor,delta);
            }
            return bots;
        }
    }
    public NativeScriptValues ScriptValues { get; private set; }
    public NativeScriptService Scripts { get; private set; }
    public NativeSocialService Social { get; private set; }
    public NativeCommunityService Community { get; private set; }
    public NativeCashService Cash { get; private set; }
    public NativeBossService Bosses { get; private set; }
    public NativeWeatherService Weather { get; set; }
    public NativeSkillBackdropService Backdrops { get; } = new NativeSkillBackdropService();

    // 单例（本地单机一个服务端实例）
    public static GameServer Local { get; private set; }

    public static GameServer Start(string dbFileName = "mapstory.db")
    {
        if (Local != null) return Local;
        string path = System.IO.Path.Combine(PlatformRes.PersistentDir(), dbFileName);
        var srv = new GameServer
        {
            Db = DbStore.Open(path),
        };
        srv.ScriptValues = new NativeScriptValues(srv.Db);
        srv.Scripts = new NativeScriptService(srv.Db);
        srv.Social = new NativeSocialService(srv.Db);
        srv.Social.ChatAccepted = srv.RouteBotChat;
        srv.Community = new NativeCommunityService(srv.Db);
        srv.Cash = new NativeCashService(srv.Db);        srv.Bosses = new NativeBossService(srv.Db);
        srv.Transport = new InProcessTransport(srv);
        // 假人资源包在后台线程预解析：等真正进图拉起假人时（World/Bots 首次访问）
        // 就不用把 8.6MB+3.3MB 的 JSON 卡在当前帧。
        Server.Bot.BotWarmup.Start("Bot/Items");
        Server.Bot.BotWarmup.Start("Bot/SoloMapling");
        Server.Bot.BotWarmup.Start("Bot/Maps");     // 导航/世界图共用这一份
        Local = srv;
        return srv;
    }

    // 用已有 DbStore 起服务端（离线单测 / 编辑器自检用，避免往持久化目录写测试库）
    public static GameServer StartWith(DbStore db)
    {
        var srv = new GameServer { Db = db };
        srv.ScriptValues = new NativeScriptValues(srv.Db);
        srv.Scripts = new NativeScriptService(srv.Db);
        srv.Social = new NativeSocialService(db);
        srv.Social.ChatAccepted = srv.RouteBotChat;
        srv.Community = new NativeCommunityService(db);
        srv.Cash = new NativeCashService(db);
        srv.Bosses = new NativeBossService(db);
        srv.Transport = new InProcessTransport(srv);
        Local = srv;
        return srv;
    }

    public static void Stop()
    {
        if (Local != null)
        {
            Local.bots?.Dispose();
            Local.Db.Save();
            Local = null;
        }
    }

    public void Save() => Db.Save();
    public void EndBotSession(int id) => bots?.RemovePlayer(id);
    void RouteBotChat(SocialRequest request)
    {
        if (request.channel == NativeChatChannel.All) bots?.ReceiveChat(request.characterId, request.text);
    }
}
