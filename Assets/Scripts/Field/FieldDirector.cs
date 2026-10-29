using System.Collections.Generic;
using UnityEngine;
using Server.Field;

// 原版地图会话：换图、传送门、掉落由本类驱动；**刷怪逻辑放入后端 Server.Field（MapSpawner +
// SpawnPoint）**，逐条对齐源端 SpawnPoint.java / MapleMap.respawn / MapFactory.loadLifeFromWz。
//
// 刷怪规则（后端持有，源端语义）：
//   1. 地图载入时每个刷怪点立刻生成 1 只（MapSpawner.AddMonsterSpawn → shouldSpawn 为真 或 mobTime==-1 强制一次）
//   2. 一只怪同一时间只占一个刷怪点；mobTime>0 按秒重生，mobTime==0 等死亡动画结束再重生，
//      mobTime<0 不再重生（SpawnPoint.ShouldSpawn / OnKilled）
//   3. 每 respawn_interval(默认 10s) 调 MapSpawner.Respawn，补到 ceil((0.70+0.05*玩家数)*刷怪点数量)
//      —— 单人是 75%，use_enable_full_respawn 默认 false
//   4. 出生位置按 calcPointBelow 落到脚下 foothold（前端几何，构造 SpawnPointDef 时折算）
//
// 掉落（MapleMap.dropFromMonster）：
//   chance 分母 999999；条目先打乱再逐条判定；itemid=0 是金币 [min, max-1]；
//   非装备数量 = min + rand(0, max-min+1)
public sealed class FieldDirector : MonoBehaviour
{
    public FieldMap Map { get; private set; }
    public int AliveCount { get; private set; }

    FieldView view;
    MapSpawner spawner;                       // 后端刷怪调度（Server.Field）
    readonly List<Point> points = new List<Point>();
    readonly Dictionary<SpawnPoint, Point> bySpawn = new Dictionary<SpawnPoint, Point>();
    readonly Dictionary<int, Point> byMob = new Dictionary<int, Point>();
    readonly List<EnemyUnit> dynamicMobs = new List<EnemyUnit>();
    readonly System.Random rng = new System.Random();

    float respawnTimer;
    FieldPortal nearPortal;      // 当前脚下可用的传送门（HUD 用来提示「按 ↑ 进入」）
    float portalLock;
    bool portalArmed;            // 离开门后才置 true；防止进门/出生瞬间来回弹
    float saveTimer;
    bool logKills;

    // 假人系统的前端组件：只取一次（原来每帧 GetComponent，白花时间）
    BotCombatField botCombat;
    GroundItemBridge groundLoot;
    BotField botField;
    // debug_frame_log：每 2 秒打一次掉帧统计，验证「进游戏还卡不卡」
    bool logFrames;
    bool logPerf;
    float logClock;
    int hitchFrames;

    const float RespawnInterval = 10f;      // GameConfig respawn_interval（后端 MapSpawner 用同源公式）
    const int   AggroRangePx = 320;         // 客户端视野内的主动攻击距离（本地适配）

    public FieldPortal NearPortal => nearPortal;

    // 换图渐变状态机（逐帧推进，不用协程：换图必须精确发生在全黑那一帧）
    enum WarpState { None, Out, Hold, In }

    WarpState warpState;
    float warpClock;             // 当前阶段已用时（真实秒）
    FieldPortal warpTarget;      // null = 死亡复活

    // Character.blockPortal(scriptName)：脚本把本图某扇脚本门拉黑（活动门重复进入保护等）。
    // 源端 changeMap 时 resetPortals，所以进新图一律清空。
    public static readonly HashSet<string> BlockedPortalScripts = new HashSet<string>();
    int pendingWarpMap = -1;     // 脚本 cm.warp / 任务传送到指定地图
    string pendingWarpPortal;
    int pendingWarpPortalIndex = -1;   // cm.warp(map, portalId)：目标图 portal 下标（= 源端 Portal.getId）
    int deferredWarpMap = -1;    // 过场图（0 号）进场后自动续跳的目标（注释见 Enter 里 view.Show 之后）

    bool Warping => warpState != WarpState.None;

    sealed class Point
    {
        public SpawnPoint sp;        // 后端刷怪点（Server.Field）
        public EnemyUnit mob;
    }

    public void Begin(int startMapId)
    {
        FieldCatalog.Ensure();
        PlayerCtrl.InputLocked = false;      // 防止上一次留在半途的渐变把输入锁死
        warpState = WarpState.None;
        warpTarget = null;
        ScreenFade.Dismiss();
        if (view == null) view = gameObject.AddComponent<FieldView>();
        logKills = PlayerPrefs.GetInt("debug_field_log", 0) != 0;
        if (logKills) PlayerPrefs.DeleteKey("debug_field_log");   // 只在本次运行生效
        logFrames = PlayerPrefs.GetInt("debug_frame_log", 0) != 0;
        if (logFrames) PlayerPrefs.DeleteKey("debug_frame_log");
        // 假人耗时明细：debug_bot_perf=1（一次有效）→ logcat 每 2 秒打一组 [Bot/Perf]。
        // 真正的开关在 TickFrameLog 里同步：日志记录打开时也一并采集，
        // 这样用户从侧边栏分享出去的日志自带掉帧/耗时数据，不用再教他敲 adb。
        logPerf = PlayerPrefs.GetInt("debug_bot_perf", 0) != 0;
        if (logPerf) PlayerPrefs.DeleteKey("debug_bot_perf");
        // 章节模式的兜底背景（纯色条）会盖住原版地图，进入原版模式时直接关掉
        var backdrop = FindObjectOfType<Backdrop>();
        if (backdrop != null) backdrop.gameObject.SetActive(false);
        Enter(startMapId, null);
    }

    public void Enter(int mapId, string spawnPortalName, int spawnPortalIndex = -1)
    {
        FieldCatalog.Ensure();
        BlockedPortalScripts.Clear();   // 源端 changeMap → resetPortals
        FieldMap map;
        if ((map = FieldCatalog.GetMap(mapId)) == null)
        {
            Debug.LogWarning("[Field] 没有地图 " + mapId);
            return;
        }

        // Resolve artwork before discarding the current scene; full Data can contain dangling links.
        try { view.Show(map); }
        catch (System.Exception e) { Debug.LogError("[Field] 无法进入地图 " + mapId + ": " + e.Message); return; }

        // 0 号图（原版 Maple Road : Entrance）是「踏入旅途」开场演出的黑场舞台：
        //   info/onUserEnter = goAdventure、背景 bS 为空、整屏 allblackTile，图里没有 NPC 也没有门，
        //   唯一出路是 info/returnMap（10000 小蘑菇）。原版靠客户端播完
        //   Effect/Direction3.img/goAdventure/Scene{gender} 之后自己跳走。
        //   仅在原始过场未成功启动时保留 returnMap 兜底；启动后由过场的 type 2 指令换图。
        //   2101.js（番茄田的希娜「训练结束了就送你出训练营」）最后就是 cm.warp(0)，走的就是这条路。
        deferredWarpMap = map.id == 0 && map.returnMap > 0 && map.returnMap != map.id ? map.returnMap : -1;
        // 换图打一条分隔线：看分享出来的日志时，能立刻知道掉帧发生在哪张图
        FileLog.Note("进入地图 " + mapId);
        ClearMobs();
        // 章节/大厅遗留的 EnemyUnit 也会进 EnemyUnit.All：玩家会自动去打它们，
        // 它们也会照常攻击玩家（彩虹村这种 town 图本不该有怪）。原版模式下只留本图刷的怪。
        foreach (var stray in FindObjectsOfType<EnemyUnit>())
            if (stray != null && !stray.fieldMode)
            {
                stray.gameObject.SetActive(false);
                Destroy(stray.gameObject);
            }
        GroundItem.Clear();
        foreach (var summon in FindObjectsOfType<NativeSummon>())
            if (summon != null) { summon.gameObject.SetActive(false); Destroy(summon.gameObject); }
        var player = GameLoop.I != null ? GameLoop.I.player : null;
        player?.GetComponent<SkillSystem>()?.CancelActions();

        bool appearanceChanged = (Map != null && Map.fieldType == 82) != (map.fieldType == 82);
        Map = map;
        NativeEventScripts.ChangedMap(map.id,false);
        nextEnvironmentDamage=Time.realtimeSinceStartupAsDouble+EnvironmentInterval();
        NpcField.Bind(map);
        if (appearanceChanged) player?.RefreshFieldAppearance();
        ApplyGeometry(map);
        PlacePlayer(map, spawnPortalName, spawnPortalIndex);
        player?.GetComponent<SkillSystem>()?.RestoreBuffSummons();

        // 原版每张图自带 bgm（FieldMaps.json 的 info/bgm，如 Bgm00/FloralLife）；
        // Sound/Weapon/Mob/Skill 等音效由各系统按需播放，这里只管地图音乐。
        AudioManager.PlayBgm(map.bgm);

        BuildPoints(map);

        respawnTimer = RespawnInterval;
        portalLock = 0.4f;
        // 出生点常常正好落在传送门上：必须先离开一次才允许触发，否则按着上会来回弹
        portalArmed = false;
        nearPortal = null;
        Profile.LastMapId = map.id;
        NativeSkillBackdrop.Enter(this);
        NativeMapScripts.Enter(this);
        if(NativeMapIntro.Active(this))deferredWarpMap=-1;
        NativeEventScripts.ChangedMap(map.id,true);

        Debug.Log("[Field] 进入 " + map.id + " " + map.name + "：刷怪点 " + points.Count +
                  "，传送门 " + map.portals.Count);
    }

    // ——————————————————— 地形 / 相机 ———————————————————

    void ApplyGeometry(FieldMap map)
    {
        var terrain = new List<Vector4>();
        var forces = new List<int>();
        var drag = new List<int>();
        var noFallThrough = new List<bool>();
        float minX = float.MaxValue, maxX = float.MinValue;
        foreach (var f in map.footholds)
        {
            float x1 = map.WorldX(f.x1), y1 = map.WorldY(f.y1);
            float x2 = map.WorldX(f.x2), y2 = map.WorldY(f.y2);
            terrain.Add(new Vector4(x1, y1, x2, y2));
            forces.Add(f.force);
            drag.Add(f.drag);
            noFallThrough.Add(f.forbidFallDown != 0);
            minX = Mathf.Min(minX, x1); maxX = Mathf.Max(maxX, x2);
        }
        PlayerCtrl.SetTerrain(terrain.Count > 0 ? terrain : null, noFallThrough, forces, drag);

        var climbs = new List<Art.ClimbLine>();
        foreach (var r in map.ropes)
            climbs.Add(new Art.ClimbLine
            {
                x = map.WorldX(r.x), top = map.WorldY(r.y1), bottom = map.WorldY(r.y2),
                ladder = r.ladder, upperExit = r.upperExit,
            });
        PlayerCtrl.Climbs = climbs.Count > 0 ? climbs : null;

        if (terrain.Count > 0) PlayerCtrl.SetBounds(minX, maxX);
        else PlayerCtrl.SetBounds(map.ViewMinX, map.ViewMaxX);

        // 掉到 VR 底部以下就复位（原端由服务端处理）。留 4u 余量，正常下落不会误触。
        PlayerCtrl.FallFloor = map.ViewMinY - 4f;

        var rig = GameLoop.I != null ? GameLoop.I.camRig : null;
        if (rig != null) rig.SetBounds(map.ViewMinX, map.ViewMaxX, map.ViewMinY, map.ViewMaxY);
    }

    void PlacePlayer(FieldMap map, string portalName, int portalIndex = -1)
    {
        var player = GameLoop.I != null ? GameLoop.I.player : null;
        if (player == null) return;

        // Character.changeMap：目标点名解析不到时回退 to.getPortal(portalId)，再回退 getPortal(0)
        var portal = map.Portal(portalName)
            ?? (portalIndex >= 0 && portalIndex < map.portals.Count ? map.portals[portalIndex] : null)
            ?? map.FirstPortal ?? map.SpawnPortal;
        float x = portal != null ? map.WorldX(portal.x) : 0f;
        float y = portal != null ? map.WorldY(portal.y) : 0f;
        float ground = PlayerCtrl.GroundAt(Mathf.Clamp(x, PlayerCtrl.XMin, PlayerCtrl.XMax), y);
        player.PlaceAt(x, ground);
        if (portal != null) player.facing = 1;
        if (GameLoop.I.camRig != null) GameLoop.I.camRig.SnapTo(x);
    }

    // ——————————————————— 刷怪点 ———————————————————

    // 构建后端刷怪器并登记每个刷怪点；进入地图时按源端 addMonsterSpawn 立即生成 mobTime>=0 / -1 的点。
    void BuildPoints(FieldMap map)
    {
        points.Clear();
        byMob.Clear();
        bySpawn.Clear();

        spawner = new MapSpawner();
        spawner.Clock = NowMs;                 // 与帧时钟对齐（源端 Server.getCurrentTime）
        spawner.SetMobInterval(Map.createMobInterval); // MapFactory info/createMobInterval (ms)
        spawner.SetRandomRefresh(true);        // use_monster_random_refresh_position（与原客户端一致）

        foreach (var s in map.spawns)
        {
            // calcPointBelow：落到脚下 foothold（源端 addMonsterSpawn 内部做，newpos.y-=1）
            float droppedY = DropToFoothold(s.x, s.y) - 1f;
            var def = new SpawnPointDef
            {
                mob = s.mob,
                mobTime = s.mobTime,
                fh = s.fh,
                f = s.dir,
                team = s.team,
                cy = (int)s.cy,
                x = s.x,
                y = droppedY,
                rx0 = s.rx0,
                rx1 = s.rx1,
                immobile = false,
                isBoss = IsBossMob(s.mob),
            };
            var sp = spawner.AddMonsterSpawn(def);   // 构造并登记（含 boss 倍率缩放）
            var p = new Point { sp = sp, mob = null };
            points.Add(p);
            bySpawn[sp] = p;
            // 源端 addMonsterSpawn：sp.shouldSpawn() || mobTime == -1 时立即生成
            if (sp.ShouldSpawn(NowMs()) || def.mobTime == -1)
                SpawnAt(p);
        }
    }

    // 源端 MapleMap.calcPointBelow：在 mapX 处找脚下（y>=mapY 且最接近）的 foothold 的 y。
    // 地图像素坐标，y 向下为正；找不到下方 foothold 时返回原 y（不掉落）。
    float DropToFoothold(float mapX, float mapY)
    {
        if (Map == null) return mapY;
        float best = mapY;
        bool found = false;
        foreach (var fh in Map.footholds)
        {
            float minX = Mathf.Min(fh.x1, fh.x2), maxX = Mathf.Max(fh.x1, fh.x2);
            if (mapX < minX || mapX > maxX) continue;
            float t = (fh.x2 == fh.x1) ? 0f : (mapX - fh.x1) / (fh.x2 - fh.x1);
            float yAtX = fh.y1 + (fh.y2 - fh.y1) * t;
            if (yAtX >= mapY && (!found || yAtX < best)) { best = yAtX; found = true; }
        }
        return best;
    }

    static bool IsBossMob(int mobId)
        => FieldCatalog.Mobs.TryGetValue(mobId, out var m) && Art.Num(m.stats, "boss", 0) > 0;

    // 当前毫秒时钟（与 Unity Time.realtimeSinceStartup 一致），注入后端 MapSpawner.Clock。
    static long NowMs() => (long)(Time.realtimeSinceStartupAsDouble * 1000.0);

    void SpawnAt(Point p)
    {
        var sd = p.sp.Def;                       // 后端刷怪点定义
        if (!FieldCatalog.Mobs.TryGetValue(sd.mob, out var mdef)) return;

        var go = new GameObject("Mob_" + sd.mob);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.enabled = false;
        var e = go.AddComponent<EnemyUnit>();

        e.artKey = string.IsNullOrEmpty(mdef.artKey) ? mdef.id.ToString() : mdef.artKey;
        e.fieldMode = true;
        e.fieldMobId = sd.mob;
        e.label = mdef.name;
        float x = Map.WorldX(sd.x);
        float refY = Map.WorldY(sd.y);
        e.homeX = x;
        float rx0 = sd.rx0, rx1 = sd.rx1;
        if (rx1 > rx0)
        {
            e.roamMin = Map.WorldX(rx0);
            e.roamMax = Map.WorldX(rx1);
        }
        else
        {
            e.roamMin = x - 3f;
            e.roamMax = x + 3f;
        }
        e.aggroRange = AggroRangePx / Art.PPU;

        e.Init(2, x, refY);
        ApplyStats(e, mdef);
        e.BindNative(this);

        p.mob = e;
        p.sp.MarkSpawned(NowMs());              // ≡ SpawnPoint.getMonster：占用该刷怪点
        byMob[e.GetInstanceID()] = p;
    }

    // Monster.killBy revives / MobSkill.summonMonsters share the field lifecycle.
    public EnemyUnit SpawnNativeMob(int mobId, Vector3 position, bool ground = true, bool floating = false)
    {
        if (Map == null) return null;
        FieldCatalog.LoadMob(mobId);
        if (!FieldCatalog.Mobs.TryGetValue(mobId, out var def)) return null;
        var go = new GameObject("Mob_" + mobId);
        var e = go.AddComponent<EnemyUnit>();
        e.artKey = def.artKey; e.fieldMode = true; e.fieldMobId = mobId; e.label = def.name;
        e.homeX = position.x; e.roamMin = position.x; e.roamMax = position.x;
        e.aggroRange = AggroRangePx / Art.PPU;
        e.Init(2, position.x, position.y);
        e.nativeFloating = floating;
        if (!ground) e.transform.position = position;
        ApplyStats(e, def);
        dynamicMobs.Add(e);
        e.BindNative(this);
        return e;
    }

    // 数值全部取 Mob.wz/info；未提供的量不回填任何自造值。
    void ApplyStats(EnemyUnit e, FieldMob def)
    {
        var s = def.stats;
        e.hpMax = Mathf.Max(1, Art.Num(s, "maxHP", 1));
        e.hp = e.hpMax;
        e.damage = Mathf.Max(0, Art.Num(s, "PADamage", 0));
        // 服务端不推动怪物移动；原客户端用 WZ speed（相对 100% 的百分比偏移）决定行走快慢。
        // 100% 基准取角色 walkSpeed（125px/s，同一套百分比语义）：
        // 蜗牛 speed=-65 → 35% → 43.75px/s，与它的 move 动画步幅（0.9s 走约 39px ≈ 一个身位）吻合。
        // 之前除了这个换算还额外乘 0.35，巡逻只有 9.8px/s，这才是「怪物移动很慢」的直接原因。
        float sp = Art.Num(s, "speed", 0);
        e.speed = MobSpeed(sp);
    }

    // WZ info/speed → 世界单位/秒。抽成静态方法，供自检与运行时共用同一套公式。
    public static float MobSpeed(float wzSpeed)
        => (float)(PlayerCtrl.BaseWalkPx * NativeMobMotion.Multiplier((int)wzSpeed) / Art.PPU);

    // ——————————————————— 每帧 ———————————————————

    void Update()
    {
        // 假人系统的分帧预算以这一句为起点：之后（TickBots / BotField.Update）花在假人上的
        // 时间都算进同一份预算，超了就把剩下的活留到下一帧。
        Server.Bot.BotWorkPump.BeginFrame();
        TickBots();
        TickFrameLog();
        // 渐变优先于一切：死亡时 GameLoop 可能已经 gameOver，但黑幕仍要走完
        if (Warping) { TickWarpFade(); return; }

        // 过场图（0 号）已经站上去了，接着按它的 returnMap 走（见 Enter 里的 deferredWarpMap 注释）
        if (deferredWarpMap >= 0)
        {
            int to = deferredWarpMap;
            deferredWarpMap = -1;
            WarpTo(to);
            return;
        }

        if (Map == null || GameLoop.I == null || GameLoop.I.state != GameLoop.GameState.Playing) return;
        var player = GameLoop.I.player;
        if (player == null) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        portalLock = Mathf.Max(0f, portalLock - dt);
        CheckPortals(player, dt);
        CheckNpc(player);
        TickRespawn(dt);
        TickEnvironment(player,Time.realtimeSinceStartupAsDouble);

        AliveCount = 0;
        foreach (var mob in EnemyUnit.All) if (mob.fieldMode && !mob.dead) AliveCount++;

        saveTimer += dt;
        if (saveTimer > 20f) { saveTimer = 0f; Profile.Save(); }
    }

    double nextEnvironmentDamage;
    static double EnvironmentInterval()
    {
        int interval=GameServer.Local==null ? 2500 : GameConfigDao.GetInt(GameServer.Local.Db,"map_damage_overtime_interval",2500);
        int count=GameServer.Local==null ? 2 : GameConfigDao.GetInt(GameServer.Local.Db,"map_damage_overtime_count",2);
        return System.Math.Max(1,interval)*System.Math.Max(1,count)/1000.0;
    }
    public static bool FieldProtection(int mapId,int returnMap,int protection,bool coldEquipment)
    {
        bool cold=(returnMap==211000000 && mapId!=200082300) || returnMap==193000000;
        // Character.buffMapProtection returns immediately for an existing protection stat.
        if (protection!=0) return protection==1 ? cold : protection==2 && (returnMap==230000000 || mapId==200082300);
        return coldEquipment && cold;
    }
    public static bool IsEnvironmentalEquipment(InventoryEntry item)
        => item!=null && item.inventoryType==InventoryDao.Equipped && item.position<0;
    public void TickEnvironment(PlayerCtrl player,double now)
    {
        if (Map==null || Map.decHp<=0 || player==null || player.hp<=0 || now<nextEnvironmentDamage) return;
        nextEnvironmentDamage=now+EnvironmentInterval();
        bool equipped=false,cold=false;
        if (GameSession.InGame)
            foreach (var item in GameSession.Character.inventory)
                if (IsEnvironmentalEquipment(item))
                { equipped|=Map.protectItem>0 && item.itemId==Map.protectItem; cold|=(item.flag&4)!=0; }
        else for (int slot=-1;slot>=-59;slot--) equipped|=Map.protectItem>0 && Profile.EquipOfSlot(slot)==Map.protectItem;
        int protection=player.GetComponent<NativeBuffs>()?.Value("MAP_PROTECTION") ?? 0;
        if (equipped || FieldProtection(Map.id,Map.returnMap,protection,cold)) return;
        // Character.doHurtHp -> addHP: independent of defense, contact i-frames and guard skills.
        player.hp=Mathf.Max(0,player.hp-Map.decHp);
        if (player.hp<=0) GameLoop.I?.OnPlayerDead();
    }

    // FieldMap坐标转换为WorldX=x/PPU、WorldY=-y/PPU；同步真实脚底位置。
    void TickBots()
    {
        if (Map == null || GameServer.Local == null || !GameSession.InGame || GameLoop.I == null || GameLoop.I.player == null) return;
        // 假人资源（Items 8.6MB / SoloMapling 3.3MB）在后台线程解析，没解析完之前不碰假人系统：
        // 一旦在这里 new BotRuntime，BotItemCatalog/BotResources 会 Join 后台线程，
        // 把整段解析时间堵在进图那一帧（就是「进游戏后卡一会儿才能动」）。
        if (!Server.Bot.BotWarmup.Ready("Bot/Items") || !Server.Bot.BotWarmup.Ready("Bot/SoloMapling") ||
            !Server.Bot.BotWarmup.Ready("Bot/Maps")) return;
        var player = GameLoop.I.player;
        var bots = GameServer.Local.Bots;
        bots.SynchronizePlayer(GameSession.Character, Map.id, player.transform.position.x * Art.PPU, -player.transform.position.y * Art.PPU, GameSession.Channel);
        // 组件只取一次：以前每帧 4 次 GetComponent（含 AddComponent 兜底），纯浪费。
        if (botCombat == null) botCombat = GetComponent<BotCombatField>() ?? gameObject.AddComponent<BotCombatField>();
        botCombat.Bind(this, bots);
        if (groundLoot == null) groundLoot = GetComponent<GroundItemBridge>() ?? gameObject.AddComponent<GroundItemBridge>();
        groundLoot.Bind(this, bots);
        // Drive 内部：BotTasks 的分帧泵 + 受预算约束的状态机分发，不会一次跑完全部假人。
        bots.Drive((long)(Time.realtimeSinceStartup * 1000));
        view.SyncDynamicNpcs();
        if (botField == null) botField = GetComponent<BotField>() ?? gameObject.AddComponent<BotField>();
        // 假人视图的增删本身也降到 10Hz（BotField 内部节流），纸娃娃合成改在 BotField.Update 里按预算出队。
        botField.Synchronize(Map.id, GameSession.World, GameSession.Channel);
    }

    // debug_frame_log=1（一次有效）时，每 2 秒把掉帧情况打到 logcat：
    // `adb logcat -s Unity | grep Bot/Frame` 就能确认还有没有长帧。
    // 日志记录开关打开时自动带上（否则用户分享出去的日志里没有现场数据）。
    void TickFrameLog()
    {
        bool wantPerf = logPerf || FileLog.Enabled;
        if (Server.Bot.BotPerf.Enabled != wantPerf)
        {
            Server.Bot.BotPerf.Enabled = wantPerf;
            Server.Bot.BotPerf.Sink = wantPerf ? (System.Action<string>)Debug.Log : null;
        }

        bool frameLog = logFrames || FileLog.Enabled;
        if (!frameLog && !wantPerf) return;
        float dt = Time.unscaledDeltaTime;
        if (dt > 0.05f) hitchFrames++;
        logClock += dt;
        if (logClock < 2f) return;
        if (frameLog)
            Debug.Log("[Bot/Frame] 2 秒内 " + hitchFrames + " 帧超 50ms，假人 " + Server.Bot.BotRegistry.Count() +
                      " 个，本帧 " + (dt * 1000f).ToString("F1") + "ms");
        if (wantPerf) Server.Bot.BotPerf.Flush();
        logClock = 0f; hitchFrames = 0;
    }

    // 靠近 NPC 按 ↑ 对话。传送门优先（同位置时按原端行为进图）。
    void CheckNpc(PlayerCtrl player)
    {
        if (Warping || NpcSession.Active != null) return;
        if (nearPortal != null) return;
        if (!PressUp()) return;
        NpcField.TryInteractNearest();
    }

    void TickRespawn(float dt)
    {
        respawnTimer -= dt;
        if (respawnTimer > 0f) return;
        respawnTimer = RespawnInterval;

        if (spawner == null) return;
        // 后端按源端 MapleMap.respawn：计算应补数量 + 遍历（可打乱）刷怪点 + ShouldSpawn 闸门。
        var toSpawn = spawner.Respawn(1);
        foreach (var sp in toSpawn)
        {
            if (bySpawn.TryGetValue(sp, out var p) && (p.mob == null || p.mob.dead))
                SpawnAt(p);
        }
    }

    // 原端只有 pt=1（隐形传送门，如 FM 入口 in01..in22）和 pt=6（碰撞门）是踩上去即传；
    // pt=2/3/7/10（含自由市场出口 out00、选择之岔路）都要站在门上按上方向键才进。
    void CheckPortals(PlayerCtrl player, float dt)
    {
        if (Warping)                 // 渐变中：不再接受任何新的门
        {
            nearPortal = null;
            return;
        }

        // 在绳梯上按上只应该往上爬，不能顺手把绳子旁边的传送门点开
        if (player.IsClimbing)
        {
            nearPortal = null;
            portalArmed = false;
            return;
        }

        FieldPortal hit = FindPortal(player);
        nearPortal = hit;

        if (hit == null)
        {
            portalArmed = true;      // 走开门 → 重新允许触发
            return;
        }
        if (portalLock > 0f || !portalArmed) return;
        // 触碰门不需要按键；按 ↑ 的门必须等按键（portalArmed 防止按住不放连续触发）
        if (!hit.TouchTrigger && !PressUp()) return;

        portalArmed = false;
        UsePortal(hit);
    }

    FieldPortal FindPortal(PlayerCtrl player)
    {
        Vector2 feet = player.FeetPos;
        FieldPortal best = null;
        float bestDx = float.MaxValue;
        foreach (var p in Map.portals)
        {
            if (!p.Usable) continue;
            if (!p.TouchTrigger && !p.PressUpTrigger) continue;
            // Character.blockPortal（源端）：脚本把当前图指定 script 的门拉黑，
            // 换图时清空（Character.changeMap → resetPortals）。
            if (p.HasScript && BlockedPortalScripts.Contains(p.script)) continue;
            float px = Map.WorldX(p.x), py = Map.WorldY(p.y);
            float dx = Mathf.Abs(feet.x - px);
            if (dx > 30f / Art.PPU) continue;
            if (Mathf.Abs(feet.y - py) > 80f / Art.PPU) continue;
            if (dx < bestDx) { bestDx = dx; best = p; }
        }
        return best;
    }

    static bool PressUp()
    {
        if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W)) return true;
        if (InputHub.Held(GameAction.Up)) return true;
        if (Input.GetAxisRaw("Vertical") > 0.6f) return true;
        return TouchInput.MoveV > 0.6f;    // 摇杆向上
    }

    // 换图走原版的三段渐变：渐隐到全黑 → 在全黑里换图 → 渐显（ScreenFade）。
    // 原来是瞬间切图，用户反馈「有点突然」；原客户端也不是硬切。
    void UsePortal(FieldPortal portal)
    {
        if (Warping) return;
        // GenericPortal.enterPortal（源端）：portal 带脚本属性就先跑 portal/<script>.js 的 enter(pi)，
        // 由脚本决定 warp 到哪（FM 出口 market00、活动门等）；没跑成脚本才回退到 WZ 的 tm/tn。
        if (portal.HasScript && NpcSession.RunPortal(portal)) return;
        StartWarp(portal);
    }

    void StartWarp(FieldPortal portal)
    {
        warpTarget = portal;
        warpState = WarpState.Out;
        warpClock = 0f;
        PlayerCtrl.InputLocked = true;
        ScreenFade.Attach(this);
        ScreenFade.Set(0f);
        AudioManager.Play("Game/Portal", 0.7f, false);   // 原版进门音效

        // 原版按下 ↑ 后人物立刻离开地图；这里黑幕有 0.3s，先给一段无敌，
        // 否则站在怪堆里的门上会被那 0.3s 白白打掉血。
        var pc = GameLoop.I != null ? GameLoop.I.player : null;
        if (pc != null) pc.invulnUntil = Time.unscaledTime + ScreenFade.TotalSeconds + 0.2f;
    }

    // 把 alpha 按真实时间从 from 推到 to，到点返回 true
    static bool Advance(float from, float to, float duration, float elapsed)
    {
        float k = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
        ScreenFade.Set(Mathf.Lerp(from, to, k));
        return k >= 1f;
    }

    void TickWarpFade()
    {
        warpClock += Time.unscaledDeltaTime;

        switch (warpState)
        {
            case WarpState.Out:
                if (!Advance(0f, 1f, ScreenFade.OutSeconds, warpClock)) return;
                // 全黑之后才真正换图：玩家看不到任何一帧跳变
                if (warpTarget == null)
                {
                    int target = pendingWarpMap;
                    pendingWarpMap = -1;
                    string portalName = pendingWarpPortal;
                    pendingWarpPortal = null;
                    int portalIndex = pendingWarpPortalIndex;
                    pendingWarpPortalIndex = -1;
                    // pendingWarpMap 的「无目标」哨兵是 -1；0 号图是合法目标（见 WarpTo 注释）
                    if (target < 0) target = Map != null ? Map.returnMap : 1000000;
                    // 0 号图是黑场过场舞台（见 Enter 里 deferredWarpMap 的注释）：本端没有开场演出，
                    // 直接按它的 returnMap 送走，别让玩家先进黑图再跳一次。
                    if (target == 0)
                    {
                        try
                        {
                            var pass = FieldCatalog.GetMap(0);
                            if (pass != null && pass.returnMap > 0 && pass.returnMap != 0) target = pass.returnMap;
                        }
                        catch (System.Exception e) { Debug.LogWarning("[Field] 0 号过场图解析失败，仍按原目标进入：" + e.Message); }
                    }
                    Enter(target, portalName, portalIndex);
                }
                else if (warpTarget.tm == Map.id) WarpInsideMap(warpTarget);
                else Enter(warpTarget.tm, string.IsNullOrEmpty(warpTarget.targetName) ? null : warpTarget.targetName);
                warpState = WarpState.Hold;
                warpClock = 0f;
                return;

            case WarpState.Hold:
                if (warpClock < ScreenFade.HoldSeconds) return;
                warpState = WarpState.In;
                warpClock = 0f;
                return;

            case WarpState.In:
                if (!Advance(1f, 0f, ScreenFade.InSeconds, warpClock)) return;
                ScreenFade.Set(0f);
                warpState = WarpState.None;
                warpTarget = null;
                PlayerCtrl.InputLocked = false;
                return;
        }
    }

    // 同图内部传送（如选择之岔路的上下通道）
    void WarpInsideMap(FieldPortal portal)
    {
        var target = Map.Portal(portal.targetName);
        var player = GameLoop.I != null ? GameLoop.I.player : null;
        if (target == null || player == null) return;

        float x = Map.WorldX(target.x);
        player.PlaceAt(x, PlayerCtrl.GroundAt(x, Map.WorldY(target.y)));
        if (GameLoop.I.camRig != null) GameLoop.I.camRig.SnapTo(x);
        portalLock = 0.4f;
        portalArmed = false;
    }

    // ——————————————————— 击杀与掉落 ———————————————————

    public void OnEnemyKilled(EnemyUnit e)
    {
        if (e == null) return;
        e.NativeController?.OnKilled();
        dynamicMobs.Remove(e);
        Point p;
        if (byMob.TryGetValue(e.GetInstanceID(), out p))
        {
            byMob.Remove(e.GetInstanceID());
            p.mob = null;
            // 源端 MonsterListener.monsterKilled：mobTime>0 按秒、mobTime==0 按死亡动画时长
            p.sp.OnKilled((long)(DeathAnimSeconds(e) * 1000.0), NowMs());
        }

        FieldMob mob;
        if (!FieldCatalog.Mobs.TryGetValue(e.fieldMobId, out mob)) return;

        int exp = (int)Art.Num(mob.stats, "exp", 0);
        var rates = GameSession.InGame ? GameSession.Net.Community(new CommunityRequest { characterId = GameSession.CharacterId, operation = CommunityOperation.Read }) : new CommunityResponse();
        bool questCredit=true;
        int reputationCalls=1;
        if (e.HasBotContribution && GameSession.InGame && GameServer.Local!=null)
        {
            var bots=GameServer.Local.Bots;
            var buffs=GameLoop.I?.player?.GetComponent<NativeBuffs>();
            var award=Server.Bot.BotPartyExperience.Calculate(GameServer.Local.Db,bots.parties,bots.Player(GameSession.CharacterId),
                (int)Art.Num(mob.stats,"level",1),exp,(e.NativeController!=null ? e.NativeController.MaximumHp : (long)e.hpMax)+e.TotalHealed,
                e.DamageContributions,(float)rates.expRate,(float)rates.expRate,
                buffs?.Value("HOLY_SYMBOL") ?? 0,GameConfigDao.GetBool(GameServer.Local.Db,"use_full_holy_symbol",false),
                buffs?.Value("EXP_INCREASE") ?? 0,buffs?.HasStat("EXP_BUFF")==true,e.NativeController?.Showdown ?? 0);
            reputationCalls=award.reputationCalls;
            questCredit=award.eligible && GameLoop.I?.player!=null && GameLoop.I.player.hp>0;
            if (GameLoop.I?.player!=null && GameLoop.I.player.hp>0 && award.eligible)
                Profile.GrantExperience((int)System.Math.Min(int.MaxValue,(long)award.personal+award.party));
        }
        else if (exp > 0)
        {
            // ≡ Monster.java:734 personalExp × attacker.getExpRate()：
            // getExpRate = 个人倍率(×世界,×家族Buff) × (100+套装ExpRate)/100。
            // 源端另有 getMobExpRate（level_exp_rate/quick_level 等级练级倍率），本工程无对应配置键。
            var set = EquipLoadout.CurrentSetBonus();
            var buffs=GameLoop.I?.player?.GetComponent<NativeBuffs>();
            bool full=GameServer.Local!=null && GameConfigDao.GetBool(GameServer.Local.Db,"use_full_holy_symbol",false);
            float personal=exp*Server.Bot.BotPartyExperience.StatusMultiplier(buffs?.Value("HOLY_SYMBOL") ?? 0,false,full,e.NativeController?.Showdown ?? 0);
            personal*=(float)AdminSetItem.ApplyRate(set,"ExpRate",rates.expRate);
            personal+=buffs?.Value("EXP_INCREASE") ?? 0;
            if (buffs?.HasStat("EXP_BUFF")==true) personal*=2;
            Profile.GrantExperience(personal>=int.MaxValue ? int.MaxValue : (int)System.Math.Floor((double)personal+.5));
        }

        for (int rep=0; GameSession.InGame && rep<reputationCalls; rep++)
            GameSession.Net.Community(new CommunityRequest { characterId=GameSession.CharacterId, operation=CommunityOperation.RecordKill,
                value=(int)Art.Num(mob.stats,"maxHP",0), targetId=(int)Art.Num(mob.stats,"boss",0) });

        // 任务击杀计数（原端 Character 收到 MOB_KILLED 后推进已接任务的 mob 进度）
        if (questCredit) QuestLogic.OnMobKilled(mob.id);

        int dropped = 0, mesos = 0;
        DropLoot(e, mob, ref dropped, ref mesos);
        if (logKills)
            Debug.Log("[Field] 击杀 " + mob.name + "(" + mob.id + ") 经验+" + exp +
                      " 掉落 " + dropped + " 件 金币 " + mesos);
    }

    // MapleMap.killMonster(monster, null, false): release spawn capacity without rewards/revive/quest credit.
    public void OnEnemyExpired(EnemyUnit e)
    {
        if (e==null) return;
        dynamicMobs.Remove(e);
        if (byMob.TryGetValue(e.GetInstanceID(),out var point))
        {
            byMob.Remove(e.GetInstanceID()); point.mob=null;
            point.sp.OnKilled((long)(DeathAnimSeconds(e)*1000),NowMs());
        }
    }

    // mobTime==0 表示等死亡动作播完再重生：用原 die 帧的总时延
    static float DeathAnimSeconds(EnemyUnit e)
    {
        if (e.NativeController!=null) return e.NativeController.ServerDeathMillis/1000f;
        var clip = Art.GetClip("mobs", e.artKey, e.NativeController!=null ? "die"+e.NativeController.DeathAction : Art.Resolve("mobs", e.artKey, "die", "die1"));
        return clip != null ? clip.Duration : 0.6f;
    }

    void DropLoot(EnemyUnit e, FieldMob mob, ref int dropped, ref int mesosTotal)
    {
        var pos = e.transform.position;
        int n = 0, mesos = 0;
        // ≡ MapleMap.dropFromMonster：非 boss chRate = chr.getDropRate()（世界×家族×套装），
        // boss 走 getBossDropRate()（家族×world.boss_drop_rate，世界 drop_rate 除回去）。
        var rsp = GameSession.InGame ? GameSession.Net.Community(new CommunityRequest { characterId = GameSession.CharacterId, operation = CommunityOperation.Read }) : new CommunityResponse();
        double dropRate = rsp.dropRate, mesoRate = rsp.mesoRate;
        if (e.IsBoss && GameSession.InGame)
            dropRate = rsp.familyDropRate * GameSession.Net.Boss(new BossRequest { characterId = GameSession.CharacterId }).dropRate;
        var set = EquipLoadout.CurrentSetBonus();
        dropRate = AdminSetItem.ApplyRate(set, "DropRate", dropRate);
        int showdown=e.NativeController?.Showdown ?? 0;
        if (showdown!=0) dropRate=(float)(dropRate*(1.0+showdown/100.0));
        // ≡ MapleMap.java:757 mesos × chr.getMesoRate()（世界 meso_rate × 套装 MesoRate%）
        mesoRate = AdminSetItem.ApplyRate(set, "MesoRate", mesoRate);
        var buffs=GameLoop.I?.player?.GetComponent<NativeBuffs>();
        int? mesoUp=buffs?.HasStat("MESOUP")==true ? (int?)buffs.Value("MESOUP") : null;
        FieldLoot.Roll(mob.id, (itemId, qty, d) =>
        {
            if (itemId == 0)
            {
                qty = RewardRules.MesoAmount(qty,mesoUp,mesoRate);
                mesos += qty;
            }
            n++;
            var landing = pos + Offset(d);
            MapItemService.CalcDropPos(Map.id, landing.x * Art.PPU, -landing.y * Art.PPU,
                pos.x * Art.PPU, -pos.y * Art.PPU, out float dropX, out float dropY);
            GroundItem.Spawn(itemId, qty, new Vector3(dropX / Art.PPU, -dropY / Art.PPU, 0), from: pos);
        }, rng, dropRate);
        dropped += n;
        mesosTotal += mesos;
    }

    // 原服 getPos：金币/物品按 d 左右交替散开 25px
    static Vector3 Offset(int d)
        => new Vector3(FieldLoot.OffsetPx(d) / Art.PPU, 0f, 0f);

    // ——————————————————— 清理 / 死亡 ———————————————————

    public void ClearMobs()
    {
        foreach (var mob in dynamicMobs)
            if (mob != null) { mob.gameObject.SetActive(false); Destroy(mob.gameObject); }
        dynamicMobs.Clear();
        GetComponent<NativeBossEncounter>()?.Clear();
        foreach (var p in points)
            if (p.mob != null) { p.mob.gameObject.SetActive(false); Destroy(p.mob.gameObject); p.mob = null; }
        points.Clear();
        byMob.Clear();
        bySpawn.Clear();
        spawner = null;
        AliveCount = 0;
    }

    // 脚本 cm.warp(mapId[, 名字|下标])：和传送门一样走黑场过渡，而不是瞬间切图。
    // 注意地图 id 0 是合法地图（Data/Map/Map/Map0/000000000.img，原版 Maple Road : Entrance
    // 的开场过场舞台），不能用 <=0 当「无效」的哨兵——2101.js 送新手出训练营就是 cm.warp(0)。
    public void WarpTo(int mapId, string portalName = null, int portalIndex = -1)
    {
        if (Warping || mapId < 0) return;
        if (Map != null && Map.id == mapId && string.IsNullOrEmpty(portalName) && portalIndex < 0) return;
        pendingWarpMap = mapId;
        pendingWarpPortal = portalName;
        pendingWarpPortalIndex = portalIndex;
        StartWarp(null);
    }

    // 原版死亡复活同样是黑场过渡（回到本图 returnMap 的出生门），不是瞬间跳回来
    public void RespawnPlayer()
    {
        if (Warping) return;
        StartWarp(null);
    }

    public void Shutdown()
    {
        warpState = WarpState.None;
        warpTarget = null;
        deferredWarpMap = -1;
        warpClock = 0f;
        PlayerCtrl.InputLocked = false;
        ScreenFade.Dismiss();

        ClearMobs();
        GroundItem.Clear();
        NpcField.Clear();
        if (botField == null) botField = GetComponent<BotField>();
        botField?.Clear();
        NpcSession.CloseActive();
        if (view != null) view.Clear();
        Map = null;
        PlayerCtrl.SetTerrain(null);
        PlayerCtrl.Climbs = null;
        PlayerCtrl.FallFloor = float.NegativeInfinity;
        var rig = GameLoop.I != null ? GameLoop.I.camRig : null;
        if (rig != null) rig.ClearStage();
        var backdrop = FindObjectOfType<Backdrop>();
        if (backdrop != null) backdrop.gameObject.SetActive(true);
    }
}
