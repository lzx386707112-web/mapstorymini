using System.Collections.Generic;
using UnityEngine;

// 原版地图会话：换图、传送门、刷怪点、掉落，全部按原服务端语义执行。
//
// 刷怪规则（SpawnPoint.java / MapFactory.java）：
//   1. 地图载入时每个刷怪点立刻生成 1 只（addMonsterSpawn → shouldSpawn 为真）
//   2. 一只怪同一时间只占一个刷怪点；mobTime>0 按秒重生，mobTime==0 等死亡动画结束再重生，
//      mobTime<0 不再重生
//   3. 每 respawn_interval(默认 10s) 补一次，补到 ceil((0.70+0.05*玩家数)*刷怪点数量)
//      —— 单人是 75%，use_enable_full_respawn 默认 false
//   4. 出生位置按 calcPointBelow 落到脚下 foothold
//
// 掉落（MapleMap.dropFromMonster）：
//   chance 分母 999999；条目先打乱再逐条判定；itemid=0 是金币 [min, max-1]；
//   非装备数量 = min + rand(0, max-min+1)
public sealed class FieldDirector : MonoBehaviour
{
    public FieldMap Map { get; private set; }
    public int AliveCount { get; private set; }

    FieldView view;
    readonly List<Point> points = new List<Point>();
    readonly List<Point> candidates = new List<Point>();
    readonly Dictionary<int, Point> byMob = new Dictionary<int, Point>();
    readonly System.Random rng = new System.Random();

    float respawnTimer;
    FieldPortal nearPortal;      // 当前脚下可用的传送门（HUD 用来提示「按 ↑ 进入」）
    float portalLock;
    bool portalArmed;            // 离开门后才置 true；防止进门/出生瞬间来回弹
    float saveTimer;
    bool logKills;

    const float RespawnInterval = 10f;      // GameConfig respawn_interval
    const float SpawnRateBase = 0.70f;      // MapleMap.getCurrentSpawnRate
    const float SpawnRatePerPlayer = 0.05f;
    const int   AggroRangePx = 320;         // 客户端视野内的主动攻击距离（本地适配）

    public FieldPortal NearPortal => nearPortal;

    // 换图渐变状态机（逐帧推进，不用协程：换图必须精确发生在全黑那一帧）
    enum WarpState { None, Out, Hold, In }

    WarpState warpState;
    float warpClock;             // 当前阶段已用时（真实秒）
    FieldPortal warpTarget;      // null = 死亡复活

    bool Warping => warpState != WarpState.None;

    sealed class Point
    {
        public FieldSpawn def;
        public double readyAt;
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
        // 章节模式的兜底背景（纯色条）会盖住原版地图，进入原版模式时直接关掉
        var backdrop = FindObjectOfType<Backdrop>();
        if (backdrop != null) backdrop.gameObject.SetActive(false);
        Enter(startMapId, null);
    }

    public void Enter(int mapId, string spawnPortalName)
    {
        FieldCatalog.Ensure();
        FieldMap map;
        if ((map = FieldCatalog.GetMap(mapId)) == null)
        {
            Debug.LogWarning("[Field] 没有地图 " + mapId);
            return;
        }

        // Resolve artwork before discarding the current scene; full Data can contain dangling links.
        try { view.Show(map); }
        catch (System.Exception e) { Debug.LogError("[Field] 无法进入地图 " + mapId + ": " + e.Message); return; }
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

        Map = map;
        ApplyGeometry(map);
        PlacePlayer(map, spawnPortalName);

        BuildPoints(map);
        SpawnInitial();

        respawnTimer = RespawnInterval;
        portalLock = 0.4f;
        // 出生点常常正好落在传送门上：必须先离开一次才允许触发，否则按着上会来回弹
        portalArmed = false;
        nearPortal = null;
        Profile.LastMapId = map.id;

        Debug.Log("[Field] 进入 " + map.id + " " + map.name + "：刷怪点 " + points.Count +
                  "，传送门 " + map.portals.Count);
    }

    // ——————————————————— 地形 / 相机 ———————————————————

    void ApplyGeometry(FieldMap map)
    {
        var terrain = new List<Vector4>();
        float minX = float.MaxValue, maxX = float.MinValue;
        foreach (var f in map.footholds)
        {
            float x1 = map.WorldX(f.x1), y1 = map.WorldY(f.y1);
            float x2 = map.WorldX(f.x2), y2 = map.WorldY(f.y2);
            if (Mathf.Abs(x2 - x1) < 0.01f) continue;      // 竖直 foothold 是墙，不当地面
            terrain.Add(new Vector4(x1, y1, x2, y2));
            minX = Mathf.Min(minX, x1); maxX = Mathf.Max(maxX, x2);
        }
        PlayerCtrl.SetTerrain(terrain.Count > 0 ? terrain : null);

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

    void PlacePlayer(FieldMap map, string portalName)
    {
        var player = GameLoop.I != null ? GameLoop.I.player : null;
        if (player == null) return;

        // Character.changeMap：目标点名解析不到时回退到 to.getPortal(0)（portal 列表首条）
        var portal = map.Portal(portalName) ?? map.FirstPortal ?? map.SpawnPortal;
        float x = portal != null ? map.WorldX(portal.x) : 0f;
        float y = portal != null ? map.WorldY(portal.y) : 0f;
        float ground = PlayerCtrl.GroundAt(Mathf.Clamp(x, PlayerCtrl.XMin, PlayerCtrl.XMax), y);
        player.PlaceAt(x, ground);
        if (portal != null) player.facing = 1;
        if (GameLoop.I.camRig != null) GameLoop.I.camRig.SnapTo(x);
    }

    // ——————————————————— 刷怪点 ———————————————————

    void BuildPoints(FieldMap map)
    {
        points.Clear();
        byMob.Clear();
        foreach (var s in map.spawns)
            points.Add(new Point { def = s, readyAt = 0 });
    }

    void SpawnInitial()
    {
        // MapFactory.addMonsterSpawn：载入地图时每个点立刻生成一只
        foreach (var p in points) SpawnAt(p);
    }

    void SpawnAt(Point p)
    {
        if (!FieldCatalog.Mobs.TryGetValue(p.def.mob, out var def)) return;

        var go = new GameObject("Mob_" + p.def.mob);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.enabled = false;
        var e = go.AddComponent<EnemyUnit>();

        e.artKey = string.IsNullOrEmpty(def.artKey) ? def.id.ToString() : def.artKey;
        e.fieldMode = true;
        e.fieldMobId = p.def.mob;
        e.label = def.name;
        float x = Map.WorldX(p.def.x);
        float refY = Map.WorldY(p.def.y);
        e.homeX = x;
        float rx0 = p.def.rx0, rx1 = p.def.rx1;
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
        ApplyStats(e, def);

        p.mob = e;
        byMob[e.GetInstanceID()] = p;
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
        => Mathf.Max(0.25f, PlayerCtrl.BaseWalkPx * (100f + wzSpeed) / 100f / Art.PPU);

    // ——————————————————— 每帧 ———————————————————

    void Update()
    {
        // 渐变优先于一切：死亡时 GameLoop 可能已经 gameOver，但黑幕仍要走完
        if (Warping) { TickWarpFade(); return; }

        if (Map == null || GameLoop.I == null || GameLoop.I.state != GameLoop.GameState.Playing) return;
        var player = GameLoop.I.player;
        if (player == null) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        portalLock = Mathf.Max(0f, portalLock - dt);
        CheckPortals(player, dt);
        TickRespawn(dt);

        AliveCount = 0;
        foreach (var p in points) if (p.mob != null && !p.mob.dead) AliveCount++;

        saveTimer += dt;
        if (saveTimer > 20f) { saveTimer = 0f; Profile.Save(); }
    }

    void TickRespawn(float dt)
    {
        respawnTimer -= dt;
        if (respawnTimer > 0f) return;
        respawnTimer = RespawnInterval;

        // 单人：getCurrentSpawnRate(1) = 0.70 + 0.05*1 = 0.75；服务端只在缺怪时补，
        // 且每点仍受自己的 mobTime/死亡动画约束（SpawnPoint.shouldSpawn）
        int target = Mathf.CeilToInt((SpawnRateBase + SpawnRatePerPlayer) * points.Count);
        int alive = 0;
        foreach (var p in points) if (p.mob != null && !p.mob.dead) alive++;
        if (alive >= target) return;

        // 原服 use_monster_random_refresh_position=true → Collections.shuffle(randomSpawn)，
        // 补怪顺序是随机的；不洗牌会固定只补列表靠前的刷怪点。
        candidates.Clear();
        foreach (var p in points)
        {
            if (p.mob != null && !p.mob.dead) continue;
            if (p.def.mobTime < 0) continue;
            candidates.Add(p);
        }
        FieldLoot.Shuffle(candidates, rng);

        double now = Time.realtimeSinceStartupAsDouble;
        foreach (var p in candidates)
        {
            if (alive >= target) break;
            if (p.mob != null && !p.mob.dead) continue;
            if (now < p.readyAt) continue;
            SpawnAt(p);
            alive++;
        }
    }

    // 原端 pt=2/10 的传送门是「站在门上按上方向键」才进，走上去不会自动触发。
    // 原来用「停留 0.3s 自动进」是为了照顾没有方向键的手机，但那等于改变了原玩法。
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
        if (!PressUp()) return;

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
            if (!p.Warps) continue;
            if (p.type != 2 && p.type != 10) continue;
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
                    int back = Map != null ? Map.returnMap : 1000000;
                    Enter(back, null);
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
        Point p;
        if (byMob.TryGetValue(e.GetInstanceID(), out p))
        {
            byMob.Remove(e.GetInstanceID());
            p.mob = null;
            if (p.def.mobTime > 0) p.readyAt = Time.realtimeSinceStartupAsDouble + p.def.mobTime;
            else p.readyAt = Time.realtimeSinceStartupAsDouble + DeathAnimSeconds(e);
        }

        FieldMob mob;
        if (!FieldCatalog.Mobs.TryGetValue(e.fieldMobId, out mob)) return;

        int exp = (int)Art.Num(mob.stats, "exp", 0);
        if (exp > 0) Profile.GrantExperience(exp);

        int dropped = 0, mesos = 0;
        DropLoot(e, mob, ref dropped, ref mesos);
        if (logKills)
            Debug.Log("[Field] 击杀 " + mob.name + "(" + mob.id + ") 经验+" + exp +
                      " 掉落 " + dropped + " 件 金币 " + mesos);
    }

    // mobTime==0 表示等死亡动作播完再重生：用原 die 帧的总时延
    static float DeathAnimSeconds(EnemyUnit e)
    {
        var clip = Art.GetClip("mobs", e.artKey, Art.Resolve("mobs", e.artKey, "die", "die1"));
        return clip != null ? clip.Duration : 0.6f;
    }

    void DropLoot(EnemyUnit e, FieldMob mob, ref int dropped, ref int mesosTotal)
    {
        var pos = e.transform.position;
        int n = 0, mesos = 0;
        FieldLoot.Roll(mob.id, (itemId, qty, d) =>
        {
            if (itemId == 0) mesos += qty;
            n++;
            GroundItem.Spawn(itemId, qty, pos + Offset(d));
        }, rng);
        dropped += n;
        mesosTotal += mesos;
    }

    // 原服 getPos：金币/物品按 d 左右交替散开 25px
    static Vector3 Offset(int d)
        => new Vector3(FieldLoot.OffsetPx(d) / Art.PPU, 0f, 0f);

    // ——————————————————— 清理 / 死亡 ———————————————————

    public void ClearMobs()
    {
        foreach (var p in points)
            if (p.mob != null) { p.mob.gameObject.SetActive(false); Destroy(p.mob.gameObject); p.mob = null; }
        points.Clear();
        byMob.Clear();
        AliveCount = 0;
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
        warpClock = 0f;
        PlayerCtrl.InputLocked = false;
        ScreenFade.Dismiss();

        ClearMobs();
        GroundItem.Clear();
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
