// GameServer：本地单机的「服务端」门面。
// 拥有 DbStore 与 ITransport；客户端只通过 ITransport 说话。
// 将来联机：在同一台机器起真实服务端，客户端把 InProcessTransport 换成 SocketTransport。
using System;

public sealed class GameServer
{
    public DbStore Db { get; private set; }
    public ITransport Transport { get; private set; }
    public IMapInfo MapInfo { get; set; }   // 客户端注入（FieldCatalog 数据）
    public NativeSocialService Social { get; private set; }
    public NativeCommunityService Community { get; private set; }
    public NativeCashService Cash { get; private set; }
    public NativeBossService Bosses { get; private set; }

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
        srv.Social = new NativeSocialService(srv.Db);
        srv.Community = new NativeCommunityService(srv.Db);
        srv.Cash = new NativeCashService(srv.Db);
        srv.Bosses = new NativeBossService(srv.Db);
        srv.Transport = new InProcessTransport(srv);
        Local = srv;
        return srv;
    }

    // 用已有 DbStore 起服务端（离线单测 / 编辑器自检用，避免往持久化目录写测试库）
    public static GameServer StartWith(DbStore db)
    {
        var srv = new GameServer { Db = db };
        srv.Social = new NativeSocialService(db);
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
            Local.Db.Save();
            Local = null;
        }
    }

    public void Save() => Db.Save();
}
