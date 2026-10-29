using UnityEngine;

// 场景构建 + 刷怪 + 游戏状态。
// 用 RuntimeInitializeOnLoadMethod 自动引导：打开 Unity 直接按 Play 就能跑，
// 不需要手动拖任何预置体到场景里，也不需要 .unity 场景文件。
public class GameLoop : MonoBehaviour
{
    public static GameLoop I;

    public PlayerCtrl player;
    public CameraRig camRig;
    public StageDirector stage;          // 非空 = 关卡模式（数据驱动）；空 = 无限刷怪模式
    public FieldDirector field;          // 非空 = 原版地图模式（彩虹岛全部地图）
    GameObject hudGo;

    public int   killCount;
    public float elapsed;
    public bool  gameOver;
    public string lastKiller = "";

    // 登录 / 出战 状态机：启动先走登录流程（正规 登录→选区→建号→选角），
    // 选角成功后由 EnterGameFromLogin 进入战斗场景；退出战斗回到 Login 态的角色选择屏。
    public enum GameState { Login, Playing }
    public GameState state = GameState.Login;

    public bool StageMode => stage != null;

    float spawnTimer;
    float eliteTimer;
    float respawnTimer;
    public readonly DeathSettlement deaths = new DeathSettlement();
    readonly System.Collections.Generic.Dictionary<int, SkillRuntimeState> playerCooldowns =
        new System.Collections.Generic.Dictionary<int, SkillRuntimeState>();

    const int MaxEnemies = 11;

    // 背景：素材就位时交给 Backdrop（天空 + 视差层 + 地面瓦片）
    SpriteRenderer skySr, groundSr, edgeSr;
    readonly System.Collections.Generic.List<SpriteRenderer> farSr = new System.Collections.Generic.List<SpriteRenderer>();
    readonly System.Collections.Generic.List<SpriteRenderer> pillarSr = new System.Collections.Generic.List<SpriteRenderer>();
    readonly System.Collections.Generic.List<SpriteRenderer> tickSr = new System.Collections.Generic.List<SpriteRenderer>();
    Backdrop backdrop;
    string themeKey = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
#if UNITY_EDITOR
        if (UnityEditor.SessionState.GetBool("NativeBuffRenderChecks", false) ||
            UnityEditor.SessionState.GetBool("NativeSkillNodeChecks", false) ||
            UnityEditor.SessionState.GetBool("NativeCashSceneChecks", false) ||
            UnityEditor.SessionState.GetBool("NativeViewportChecks", false)) return;
#endif
#if UNITY_WEBGL && !UNITY_EDITOR
        WzWebLoader.Begin(CreateGame);
#elif UNITY_ANDROID && !UNITY_EDITOR
        WzAndroidLoader.Begin(CreateGame);
#else
        CreateGame();
#endif
    }

    static void CreateGame()
    {
        var go = new GameObject("[GameLoop]");
        DontDestroyOnLoad(go);
        go.AddComponent<HitFeelRunner>();
        go.AddComponent<GameLoop>();
        go.AddComponent<InputBridge>();  // 键盘/虚拟键 → 游戏意图（原版是纯键盘操作）
#if !UNITY_ANDROID || UNITY_EDITOR
        // Android 已在资源加载页完成预热，不能在这里重新挂载并重置合成状态。
        go.AddComponent<BotPreload>();
        go.AddComponent<BotAppearancePrebake>();
#endif
    }

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
    }

    void Start()
    {
        // 职业由存档/大厅选择决定。这里曾经硬编码 SetClass("beginner")，
        // 每进一次游戏就把玩家职业重置成新手并写回存档（技能窗/技能树跟着变）。
        Profile.Load();
        BuildBackground();           // 背景只建一次，进出大厅不重建

        // 摄像机（登录/大厅与战斗共用，避免登录页里没有相机导致黑屏）
        var existing = GameObject.Find("Main Camera");
        if (existing != null && existing.GetComponent<CameraRig>() == null)
            existing.SetActive(false);
        var camGo = new GameObject("MainCamera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        camRig = camGo.AddComponent<CameraRig>();

        // 正规流程：未登录时先挂登录界面；已选区进游戏的直接开局（调试用）
        if (!GameSession.InGame)
        {
            state = GameState.Login;
            gameObject.AddComponent<LoginFlow>();
        }
        else StartGame();
    }

    // 登录流程选角完成 → 进入游戏（角色数据已由 GameSession.ApplyToProfile 灌入 Profile）
    public void EnterGameFromLogin()
    {
        if (player != null) { state = GameState.Playing; return; }
        StartGame();
    }

    // 出战：构建战斗场景并（如有）进入关卡
    public void StartGame()
    {
        if (player != null) { state = GameState.Playing; return; }
        state = GameState.Playing;
        gameOver = false; killCount = 0; elapsed = 0f;
        deaths.Clear();
        BuildScene();

        // 原版地图模式优先：只要 Field 数据存在，就不再进章节关卡。
        FieldCatalog.Ensure();
        if (FieldCatalog.Ready)
        {
            field = gameObject.AddComponent<FieldDirector>();
            int startMap = Profile.LastMapId;
            // 调试/自动化：临时指定进入的地图，只影响本次运行
            if (PlayerPrefs.HasKey("debug_field_map"))
            {
                startMap = PlayerPrefs.GetInt("debug_field_map");
                PlayerPrefs.DeleteKey("debug_field_map");
            }
            if (!FieldCatalog.Maps.ContainsKey(startMap)) startMap = 1000000;
            field.Begin(startMap);
            Debug.Log("[GameLoop] 原版地图模式：" + FieldCatalog.Maps.Count + " 张原始地图");
            if (PlayerPrefs.GetInt("debug_god", 0) == 1)
            {
                PlayerPrefs.DeleteKey("debug_god");
                if (player != null) { player.hpMax = 99999f; player.hp = 99999f; }
                Debug.Log("[GameLoop] 调试无敌已开启");
            }
            return;
        }

        // 有关卡数据就走数据驱动；没有则退回无限刷怪（保证项目任何状态下都能按 Play）
        var ch = StageChapter.Load("Stage/chapter1");
        if (ch != null && ch.levels.Count > 0)
        {
            int startLevel = 0;
            if (PlayerPrefs.HasKey("debug_start_level"))
            {
                startLevel = Mathf.Clamp(PlayerPrefs.GetInt("debug_start_level"), 0, ch.levels.Count - 1);
                PlayerPrefs.DeleteKey("debug_start_level");
            }

            stage = gameObject.AddComponent<StageDirector>();
            stage.Begin(ch, startLevel);
            Debug.Log("[GameLoop] 关卡模式：" + ch.levels.Count + " 关，Boss " + ch.bosses.Count +
                      " 个，从第 " + (startLevel + 1) + " 关开始");

            if (PlayerPrefs.GetInt("debug_god", 0) == 1)
            {
                PlayerPrefs.DeleteKey("debug_god");
                if (player != null) { player.hpMax = 99999f; player.hp = 99999f; }
                Debug.Log("[GameLoop] 调试无敌已开启");
            }
        }
        else
        {
            Debug.Log("[GameLoop] 无关卡数据，进入无限刷怪模式");
        }
    }

    // 拆除战斗世界，保留背景（不切状态，调用方负责）
    public void TeardownBattle()
    {
        gameOver = false;
        if (field != null) { field.Shutdown(); Destroy(field); field = null; }
        if (stage != null) { stage.StopSession(); Destroy(stage); stage = null; }
        ClearBattleEntities();
        if (player != null)
        {
            player.GetComponent<SkillSystem>()?.CancelActions();
            player.gameObject.SetActive(false);
            Destroy(player.gameObject); player = null;
        }
        if (hudGo != null) { Destroy(hudGo); hudGo = null; }
        deaths.Clear();
        camRig.target = null;
        camRig?.SnapTo(0f);
        HitFeel.Reset();
        Profile.Save();
    }

    /// <summary>
    /// 退出战斗回到角色选择（替代原来的「返回大厅」：大厅已随预设角色一起删除）。
    /// 先存档到 DB，再拆场景，最后把登录流程切回选角屏。
    /// </summary>
    public void ExitToCharSelect()
    {
        if (DeathFlow.I != null) DeathFlow.I.ForceClear();
        GameSession.SaveToDb();
        GameSession.DeselectChar();
        TeardownBattle();
        state = GameState.Login;
        var flow = LoginFlow.I != null ? LoginFlow.I : gameObject.AddComponent<LoginFlow>();
        flow.BackToCharSelect();
    }

    void ClearBattleEntities()
    {
        foreach (var summon in FindObjectsOfType<NativeSummon>())
            if (summon != null) { summon.gameObject.SetActive(false); Destroy(summon.gameObject); }
        foreach (var e in FindObjectsOfType<EnemyUnit>())
            if (e != null) { e.gameObject.SetActive(false); Destroy(e.gameObject); }
        EnemyUnit.All.Clear();
        foreach (var b in FindObjectsOfType<BossUnit>())
            if (b != null) { b.gameObject.SetActive(false); Destroy(b.gameObject); }
        GroundItem.Clear();
        foreach (var fx in FindObjectsOfType<SkillFx>())
            if (fx != null) { fx.gameObject.SetActive(false); Destroy(fx.gameObject); }
        HitFeel.Reset();
    }

    // ——————————————————————— 场景 ———————————————————————

    void BuildScene()
    {
        // 摄像机已在 Start 里建过（大厅/战斗共用），这里不再重复建

        // 玩家
        var pGo = new GameObject("Player");
        var pSr = pGo.AddComponent<SpriteRenderer>();
        pSr.sprite = SpriteFactory.Bottom();
        pSr.color = new Color(0.98f, 0.76f, 0.38f);
        pSr.sortingOrder = 300;
        // 保持 scale=1：贴图已经是地图 1:1 的像素尺寸，
        // 任何非等比缩放都会把人压扁/拉长（旧版的 0.82 x 1.5 就是这个 bug）
        pGo.transform.localScale = Vector3.one;
        player = pGo.AddComponent<PlayerCtrl>();
        pGo.AddComponent<SkillSystem>().BindCooldowns(playerCooldowns);

        camRig.target = pGo.transform;
        camRig.SnapTo(0f);

        // 队友（AllyAI）先不生成：横版画面里五个并排的角色会挡住战斗，
        // 而且原版单人场景本来就只有一个角色。代码保留，等做「组队/挂机」功能再开。
        // var allyArt = new[] { "warrior", "mage", "archer", "priest" };

        // HUD
        hudGo = new GameObject("HUD");
        hudGo.AddComponent<GameHUD>();

        spawnTimer = 1.2f;
        eliteTimer = 14f;
    }

    void BuildBackground()
    {
        Art.Ensure();

        var go = new GameObject("Backdrop");
        backdrop = go.AddComponent<Backdrop>();
        backdrop.Build();

        // 有真实地图切片时，地面线是画在瓦片里的（草皮/石沿），
        // 再叠一条人工高光线反而假，所以只在没有素材的兜底模式下画。
        if (Art.Ready) return;

        var edge = SpriteFactory.Make("GroundEdge", null, new Vector2(60f, 0.06f),
                                      new Color(0.42f, 0.62f, 0.5f, 0.75f), 1, false);
        edge.transform.position = new Vector3(0f, PlayerCtrl.GroundY + 0.02f, 0f);
        edgeSr = edge;

        // —— 以下只在没有素材时使用 ——
        var sky = SpriteFactory.Make("Sky", null, new Vector2(46f, 18f),
                                     new Color(0.11f, 0.13f, 0.18f), -300, false);
        sky.transform.position = new Vector3(0f, 2.5f, 0f);
        skySr = sky;

        float[] hs = { 4.2f, 3.0f, 2.2f };
        Color[] mc =
        {
            new Color(0.16f, 0.19f, 0.25f),
            new Color(0.19f, 0.23f, 0.29f),
            new Color(0.22f, 0.27f, 0.33f),
        };
        for (int i = 0; i < 3; i++)
        {
            for (int k = -3; k <= 3; k++)
            {
                var m = SpriteFactory.Make("Far" + i + "_" + k, null,
                                           new Vector2(7.5f + i * 1.4f, hs[i]),
                                           mc[i], -250 + i * 10 + (k > 0 ? 1 : 0));
                m.transform.position = new Vector3(k * 7.2f + i * 1.7f, PlayerCtrl.GroundY, 0f);
                farSr.Add(m);
            }
        }

        var ground = SpriteFactory.Make("Ground", null, new Vector2(46f, 1.5f),
                                        new Color(0.17f, 0.19f, 0.23f), -5, false);
        ground.transform.position = new Vector3(0f, PlayerCtrl.GroundY - 0.75f, 0f);
        groundSr = ground;

        for (int i = -14; i <= 14; i++)
        {
            var tick = SpriteFactory.Make("Tick" + i, null, new Vector2(0.1f, 0.3f),
                                          new Color(0.28f, 0.32f, 0.38f), 2, false);
            tick.transform.position = new Vector3(i * 1.6f, PlayerCtrl.GroundY - 0.55f, 0f);
            tickSr.Add(tick);
        }
    }

    // 关卡主题：素材模式下切背景图层；无素材时退回换色
    public void ApplyTheme(StageTheme t) => ApplyTheme(t, themeKey);

    public void ApplyTheme(StageTheme t, string key)
    {
        if (t == null) return;
        player?.GetComponent<SkillSystem>()?.CancelActions();
        GroundItem.Clear();
        if (!string.IsNullOrEmpty(key)) themeKey = key;

        if (backdrop != null) backdrop.Apply(themeKey, t);

        // 关卡边界：镜头夹在切片内，角色夹在切片里的可走段；
        // 没有切片时退回无限模式的自由范围。
        if (backdrop != null && backdrop.HasStage)
        {
            var st = Art.Stage(themeKey);
            PlayerCtrl.SetBounds(backdrop.WalkMin, backdrop.WalkMax);
            // foothold 地形交给角色/怪物：贴台阶、走斜坡、到段尽头被挡住
            PlayerCtrl.SetTerrain(st != null ? st.Footholds : null, st != null ? st.ForbidFallDown : null, st != null ? st.FootholdForces : null, st != null ? st.FootholdDrag : null);
            PlayerCtrl.Climbs = st != null ? st.Climbs : null;
            if (camRig != null && st != null) camRig.SetStage(st);
        }
        else
        {
            PlayerCtrl.SetBounds(-13f, 13f);
            PlayerCtrl.SetTerrain(null);
            PlayerCtrl.Climbs = null;
            if (camRig != null) camRig.ClearStage();
        }
        // 换关时角色可能落在新关的可走范围之外，立刻夹回来并把脚下地形吸附好
        if (player != null)
        {
            player.worldX = PlayerCtrl.ClampX(player.worldX);
            player.ResetTerrain();
        }

        if (edgeSr != null) edgeSr.color = t.accent;
        if (skySr != null) skySr.color = t.bgTop;
        if (groundSr != null) groundSr.color = t.ground;
        for (int i = 0; i < farSr.Count; i++)
        {
            float k = 0.78f + 0.11f * (i % 7);
            farSr[i].color = Color.Lerp(t.bgBottom, t.mid, k);
        }
        foreach (var p in pillarSr) if (p != null) p.color = t.groundDeep;
        foreach (var d in tickSr) if (d != null) d.color = Color.Lerp(t.groundDeep, t.accent, 0.25f);
    }

    // ——————————————————————— 战斗流程 ———————————————————————

    void Update()
    {
        if (state != GameState.Playing) return;

        if (gameOver)
        {
            // 死亡流程进行中（墓碑/确认弹窗）不自动重开，等玩家点确认
            if (DeathFlow.I != null && DeathFlow.I.Active) return;
            respawnTimer -= Time.unscaledDeltaTime;
            if (respawnTimer <= 0f) Restart();
            return;
        }

        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        elapsed += dt;

        // 原版地图模式：刷怪/换图/掉落由 FieldDirector 按原端规则驱动
        if (field != null) return;

        // 关卡模式下刷怪由 StageDirector 按 segments 控制，这里不能再随机刷
        if (stage != null) return;

        spawnTimer -= dt;
        if (spawnTimer <= 0f && EnemyUnit.All.Count < MaxEnemies)
        {
            spawnTimer = Mathf.Max(0.75f, 1.7f - elapsed * 0.004f);
            Spawn(false);
        }

        eliteTimer -= dt;
        if (eliteTimer <= 0f)
        {
            eliteTimer = Mathf.Max(9f, 22f - elapsed * 0.05f);
            Spawn(true);
        }
    }

    void Spawn(bool elite)
    {
        var go = new GameObject(elite ? "Elite" : "Mob");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = SpriteFactory.Bottom();
        float sc = elite ? 1.15f : 1f;          // 只做等比缩放
        go.transform.localScale = new Vector3(sc, sc, 1f);
        var e = go.AddComponent<EnemyUnit>();
        e.hpMax = elite ? 150f + elapsed * 2.5f : 42f + elapsed * 1.6f;
        e.damage = elite ? 16f : 8f;
        e.speed = elite ? 1.35f : Random.Range(1.6f, 2.4f);
        e.elite = elite;
        e.label = elite ? "精英" : "杂兵";
        e.Init(Random.Range(0, PlayerCtrl.Lanes), player.worldX + 11f);
    }

    // ——————————— 关卡模式刷怪 ———————————

    // 怪物属性表：type 只是名字，数值在这里定。
    // 想调强度改这张表即可，不用碰 JSON（JSON 描述的是「谁、几只、从哪来」）。
    public EnemyUnit SpawnMob(string type, float x, int lane = -1)
    {
        var go = new GameObject("Mob_" + type);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = SpriteFactory.Bottom();

        var e = go.AddComponent<EnemyUnit>();
        Color tint = new Color(0.62f, 0.36f, 0.72f);
        float scale = 1f;
        string artKey = "";

        switch (type)
        {
            case "slime":
                e.hpMax = 30f; e.damage = 6f; e.speed = 2.3f;
                tint = new Color(0.42f, 0.8f, 0.52f);
                artKey = "slime";
                break;
            case "mushroom":
                e.hpMax = 44f; e.damage = 9f; e.speed = 1.85f;
                tint = new Color(0.85f, 0.42f, 0.45f);
                artKey = "mushroom";
                break;
            case "greenmush":
                e.hpMax = 52f; e.damage = 10f; e.speed = 1.75f;
                artKey = "greenmush";
                break;
            case "bat":
                e.hpMax = 34f; e.damage = 7f; e.speed = 2.7f;
                tint = new Color(0.55f, 0.5f, 0.78f);
                break;
            case "crab":
                e.hpMax = 60f; e.damage = 11f; e.speed = 1.5f;
                tint = new Color(0.92f, 0.55f, 0.35f);
                break;
            case "pig":
                e.hpMax = 52f; e.damage = 10f; e.speed = 1.75f;
                tint = new Color(0.95f, 0.72f, 0.68f);
                artKey = "pig";
                break;
            case "snail":
                e.hpMax = 26f; e.damage = 5f; e.speed = 1.3f;
                artKey = "snail";
                break;
            default:
                e.hpMax = 42f; e.damage = 8f; e.speed = 2.0f;
                break;
        }

        e.tint = tint;
        e.artKey = artKey;
        e.label = type;
        // 等比缩放：怪物和地图都是 1:1 抽出来的，尺寸本来就是原版关系，
        // 再按 x/y 分开缩放就会变形（史莱姆会被压成扁饼）
        go.transform.localScale = new Vector3(scale, scale, 1f);
        e.Init(lane < 0 ? Random.Range(0, PlayerCtrl.Lanes) : lane, ClampSpawnX(x));
        return e;
    }

    // 刷怪点必须落在关卡切片里，否则怪会站在「空气」上
    public float ClampSpawnX(float x)
    {
        float m = 1.2f;
        return Mathf.Clamp(x, PlayerCtrl.XMin + m, PlayerCtrl.XMax - m);
    }

    public void OnEnemyKilled(EnemyUnit e)
    {
        if (state != GameState.Playing || gameOver || e == null || !e.dead || !deaths.TrySettle(e.GetInstanceID())) return;
        killCount++;
        if (field != null) { field.OnEnemyKilled(e); return; }
        BattleRewards.Drop(e.artKey, e.transform.position);
    }

    public void OnBossKilled(BossUnit boss)
    {
        if (state != GameState.Playing || gameOver || boss == null || !boss.dead || !deaths.TrySettle(boss.GetInstanceID())) return;
        BattleRewards.Drop(boss.def.type == "slime" ? "slimeking" : "mushmom", boss.transform.position);
    }

    // 数据现在落在数据库里（Server/Db），切后台/退出时要把运行时状态回写，
    // 否则手机被系统回收进程后会丢掉这一局的进度。
    void OnApplicationPause(bool paused)
    {
        if (paused) GameSession.SaveToDb();
    }

    void OnApplicationQuit()
    {
        GameSession.SaveToDb();
        GameServer.Stop();
    }

    public void OnPlayerDead()
    {
        if (gameOver) return;
        gameOver = true;
        player?.GetComponent<NativeBuffs>()?.Clear();
        stage?.StopSession();
        player?.GetComponent<SkillSystem>()?.CancelActions();
        HitFeel.Reset();

        // 正规死亡流程：dead 姿势 + 墓碑 + 确认弹窗 + 回城（Client/DeathFlow）。
        // 只有原版地图模式有 returnMap 概念；关卡/无限模式保留原来的自动重开。
        var flow = DeathFlow.I != null ? DeathFlow.I : gameObject.AddComponent<DeathFlow>();
        if (field != null) { flow.Begin(player); return; }

        respawnTimer = 1.6f;
    }

    void Restart()
    {
        gameOver = false;
        killCount = 0;
        elapsed = 0f;
        spawnTimer = 1.2f;
        eliteTimer = 14f;

        ClearBattleEntities();
        deaths.Clear();

        if (field != null)
        {
            // 原版地图：死亡后回到本图 returnMap 的出生传送门（和原服 changeMap 一致）
            if (player != null)
            {
                player.ResetCombatState();
                player.hp = player.hpMax;
                player.invulnUntil = Time.unscaledTime + 1f;
            }
            field.RespawnPlayer();
            return;
        }

        if (player != null)
        {
            player.ResetCombatState();
            player.worldX = 0f;
            player.ResetTerrain();
            player.lane = 2;
            player.invulnUntil = Time.unscaledTime + 1f;
        }
        if (camRig != null) camRig.SnapTo(0f);

        // 关卡模式：只重跑当前这一关，不从第一章重来
        if (stage != null) stage.RestartLevel();
    }
}
