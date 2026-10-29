// 反应堆（Reactor）运行时实体，纯 C#、无 UnityEngine。
// 对齐源端 org.gms.server.maps.Reactor / MapleReactor：
//   getId()=WZ 模板 id、getObjectId()=地图内实例编号、getState()=已受击次、isAlive()=是否未被破坏，
//   破坏后按 info/reactorTime 秒重生的部分由 MapReactorService 负责。
// 地图几何里的静态描述（WZ reactor 节点）用 MapReactorSpawn；运行时实例用 MapReactor。
namespace Server.Field
{
    /// <summary>
    /// WZ 里 `reactor` 节点下的一条记录（= 源端 MapleReactor 构造时读到的 info）。
    /// 由 BotNavigationMapLoader 从分片 JSON 的 reactors 数组填进 MapRuntime.reactors。
    /// </summary>
    public sealed class MapReactorSpawn
    {
        /// <summary>反应堆模板 id（WZ reactor 节点的 id 属性），如 OPQ 云堆 2002001。</summary>
        public int id;
        public int x, y;
        /// <summary>WZ reactorTime：>0 表示破坏后多少秒重生；-1/0 表示不自动重生。</summary>
        public int reactorTime = -1;
        /// <summary>WZ reactor 子节点名（cloud1 / lp / music …），仅用于日志与调试。</summary>
        public string name = "";

        /// <summary>破坏所需受击次数。WZ 的 breakState 未导出，沿用 OPQ 源端约定的 4。</summary>
        public int maxState = 4;

        /// <summary>源端 reactorTime 语义：大于 0 才排重生。</summary>
        public int RespawnSeconds => reactorTime > 0 ? reactorTime : 0;
    }

    /// <summary>
    /// 一张地图上的一个反应堆实例。objectId 在地图内唯一且稳定（惰性生成时分配一次），
    /// 假人的分配表、命中调用、掉落归属都按 objectId 走（源端也是按 oid 而不是模板 id 过滤）。
    /// </summary>
    public sealed class MapReactor
    {
        public int mapId;
        /// <summary>WZ 模板 id（源端 Reactor.getId()）。</summary>
        public int id;
        /// <summary>地图内实例编号（源端 AbstractMapObject.getObjectId()）。</summary>
        public int objectId;
        public int x, y;
        public string name = "";

        /// <summary>已受击次数（源端 Reactor.getState()）。</summary>
        public int state;
        public int previousState = -1, visualRevision;
        public int eventState;
        public bool shouldCollect = true;
        public MapReactorTemplate template;
        /// <summary>破坏所需受击次数（源端 Reactor.getReactorState() 上限）。</summary>
        public int maxState = 4;

        /// <summary>false = 已被破坏，等待（或不等待）重生。</summary>
        public bool alive = true;
        /// <summary>破坏后多少秒重生；0 = 永不重生（WZ reactorTime &lt;= 0）。</summary>
        public int respawnSeconds;
        /// <summary>最近一次被命中的毫秒时间（MapReactorService.Clock 时基）。</summary>
        public long lastHitAt;
        /// <summary>重生到点时间；0 = 没有排队重生。</summary>
        public long respawnAt;

        public bool Broken => !alive;

        /// <summary>源端 isAlive() &amp;&amp; getState() &lt; 4 的常用组合。</summary>
        public bool Hittable => alive && state < maxState;
    }
}
