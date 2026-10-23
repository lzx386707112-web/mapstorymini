// 大区 / 频道列表。数据源是 world_prop 表（源端 V1.3.0__create_world_prop.sql），
// 由 ServerlistRequestHandler / CharlistRequestHandler 按 world + channel 取列表。
//
// 单机默认库里只有一条 world_prop（id=0，3 频道）。联机时运营后台往这张表加行，
// 客户端「选区」界面无需改代码即可显示更多大区。
using System;
using System.Collections.Generic;

public sealed class WorldEntry
{
    public int id;
    public int flag;                 // 0 普通 / 1 活动 / 2 新区 / 3 热门
    public string serverMessage;     // 顶部滚动
    public string eventMessage;      // 大区描述
    public string recommendMessage;
    public int channelSize;
    public double expRate, mesoRate, dropRate, questRate;
}

public sealed class ChannelEntry
{
    public int worldId;
    public int channel;              // 1..channelSize（源端 p.readByte()+1）
}

public static class WorldService
{
    public const int DefaultWorldId = 0;

    public static List<WorldEntry> Worlds(DbStore db)
    {
        var outp = new List<WorldEntry>();
        var table = db.Table("world_prop");
        foreach (var row in table.Select(null))
        {
            // enabled=0 的大区不显示（源端 world_prop.enabled 是运营开关）
            if (DbStore.Int(row, "enabled", 0) == 0 && table.rows.Count > 1) continue;
            outp.Add(new WorldEntry
            {
                id = (int)DbStore.Long(row, "id", 0),
                flag = (int)DbStore.Long(row, "flag", 0),
                serverMessage = DbStore.Str(row, "server_message", ""),
                eventMessage = DbStore.Str(row, "event_message", ""),
                recommendMessage = DbStore.Str(row, "recommend_message", ""),
                channelSize = Math.Max(1, (int)DbStore.Long(row, "channel_size", 3)),
                expRate = DbStore.Dbl(row, "exp_rate", 1),
                mesoRate = DbStore.Dbl(row, "meso_rate", 1),
                dropRate = DbStore.Dbl(row, "drop_rate", 1),
                questRate = DbStore.Dbl(row, "quest_rate", 1),
            });
        }

        // 表为空（老库 / 迁移缺失）时退回 game_config 里的 world/0 参数组，保证选区不空白
        if (outp.Count == 0)
            outp.Add(new WorldEntry
            {
                id = DefaultWorldId,
                flag = GameConfigDao.GetInt(db, "flag", 0),
                serverMessage = GameConfigDao.Get(db, "server_message", "Welcome to Scania!"),
                eventMessage = GameConfigDao.Get(db, "event_message", "Scania"),
                recommendMessage = GameConfigDao.Get(db, "recommend_message", "Welcome to Scania!"),
                channelSize = GameConfigDao.GetInt(db, "channel_size", 3),
                expRate = GameConfigDao.GetFloat(db, "exp_rate", 1),
                mesoRate = GameConfigDao.GetFloat(db, "meso_rate", 1),
                dropRate = GameConfigDao.GetFloat(db, "drop_rate", 1),
                questRate = GameConfigDao.GetFloat(db, "quest_rate", 1),
            });
        return outp;
    }

    public static List<ChannelEntry> Channels(DbStore db, int worldId)
    {
        var outp = new List<ChannelEntry>();
        int size = 3;
        var row = db.Table("world_prop").Find(DbStore.Eq("id", (long)worldId));
        if (row != null) size = Math.Max(1, (int)DbStore.Long(row, "channel_size", 3));
        else size = Math.Max(1, GameConfigDao.GetInt(db, "channel_size", 3));
        for (int i = 1; i <= size; i++) outp.Add(new ChannelEntry { worldId = worldId, channel = i });
        return outp;
    }
}
