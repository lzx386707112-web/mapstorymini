using UnityEngine;

// 主角：原地图左右移动、foothold 跳跃与自动普攻；无地图时保留原型深度层。
// 刻意不用 Rigidbody2D：横版手感要精确，物理引擎的穿透修正和摩擦会让落地发飘。
//
// 坐标约定（很重要，后面所有单位都按这个来）：
//   worldX  —— 水平位置
//   jumpY   —— 相对起跳平台的高度，下落至更低平台前可以为负
//   laneVis —— 深度层（0 最远 / 4 最近），插值显示
//   脚底最终 y = GroundY + jumpY + DepthOffset
public class PlayerCtrl : MonoBehaviour
{
    public const float GroundY  = -1.4f;
    public const int   Lanes    = 5;
    public const float LaneStep = 0.22f;

    // 基础属性（武器在此之上加成）。改数值只动这里，不要动面板上的默认字段。
    public const float BaseInterval = 0.55f, BaseReach = 2.3f;
    WeaponDef weapon;
    CharacterAttributes baseAttributes;
    public CharacterAttributes Attributes { get; private set; }
    int weaponAttack, magicAttack;
    float rushRemaining, rushSpeed;
    int rushFacing;
    public bool IsRushing => rushRemaining > 0;

    // 可走范围：由 GameLoop 在切关时按背景切片设，默认是无限刷怪模式的宽度。
    // 原来写死 ±13 会让角色走出地图切片，背景在屏幕边上直接断开。
    public static float XMin = -13f, XMax = 13f;
    public static void SetBounds(float min, float max)
    {
        XMin = min;
        XMax = Mathf.Max(min + 0.5f, max);
    }
    public static float ClampX(float x) => Mathf.Clamp(x, XMin, XMax);

    // ————————————— 地形（foothold）—————————————
    // 关卡模式下由 GameLoop.ApplyTheme 按 manifest 的 footholds 填充（世界坐标 x1,y1,x2,y2）；
    // 空 = 无地形数据（大厅/无限模式），退回恒平 GroundY。
    public static System.Collections.Generic.List<Vector4> Terrain;
    public static void SetTerrain(System.Collections.Generic.List<Vector4> t) => Terrain = t;

    public static System.Collections.Generic.List<Art.ClimbLine> Climbs;
    Art.ClimbLine climb;
    float climbInput, grabCooldown;
    public bool IsClimbing => climb != null;

    // 掉落兜底：低于这个高度就说明脚下一条 foothold 都没接住（原端由服务端复位）。
    // 由 FieldDirector 按地图 VR 底部设置；不设置时保持 -∞，不影响章节模式。
    public static float FallFloor = float.NegativeInfinity;

    // 换图黑场期间冻结输入与物理：原版从按下 ↑ 到人物出现在新图之间是「角色已离场」的状态，
    // 本地这段黑幕有 0.3s，如果不锁住，按住方向键会让角色在黑屏里继续走/掉下平台。
    // 位置仍然同步（SyncTransform 照常跑），否则新图出生点要等黑幕结束才生效。
    public static bool InputLocked;

    // x 处的地面高度：取覆盖 x 的段里与 refY 最接近的一条（段内线性插值，支持斜坡）。
    // 若 x 不在任何段内，退回「水平距离最近的那条段」的端点高度——
    // 传送门/出生点偶尔贴在平台边缘外一两像素，返回 refY 会让角色直接浮空下坠。
    public static float GroundAt(float x, float refY)
    {
        if (Terrain == null || Terrain.Count == 0) return GroundY;
        float best = refY, bestD = float.MaxValue;
        bool covered = false;
        for (int i = 0; i < Terrain.Count; i++)
        {
            var f = Terrain[i];
            float x1 = f.x, y1 = f.y, x2 = f.z, y2 = f.w;
            if (Mathf.Abs(x2 - x1) < 0.01f) continue;      // 竖直段不可走
            float lo = Mathf.Min(x1, x2), hi = Mathf.Max(x1, x2);
            if (x < lo || x > hi) continue;
            float y = Mathf.Lerp(y1, y2, (x - x1) / (x2 - x1));
            float d = Mathf.Abs(y - refY);
            if (d < bestD) { bestD = d; best = y; covered = true; }
        }
        if (covered) return best;
        float atX;
        return NearestGround(x, out atX);
    }

    // 水平方向离 x 最近的一条可走段（含端点外），用于出生点/掉落复位。
    // 距离相同取更低的一条：掉落时应该落到下面接得住的地面。
    static float NearestGround(float x, out float atX)
    {
        atX = x;
        if (Terrain == null || Terrain.Count == 0) return GroundY;
        float bestD = float.MaxValue, bestY = GroundY;
        for (int i = 0; i < Terrain.Count; i++)
        {
            var f = Terrain[i];
            if (Mathf.Abs(f.z - f.x) < 0.01f) continue;
            float lo = Mathf.Min(f.x, f.z), hi = Mathf.Max(f.x, f.z);
            float cx = Mathf.Clamp(x, lo, hi);
            float d = Mathf.Abs(cx - x);
            float y = Mathf.Lerp(f.y, f.w, (cx - f.x) / (f.z - f.x));
            if (d < bestD - 1e-4f || (Mathf.Abs(d - bestD) <= 1e-4f && y < bestY))
            {
                bestD = d; bestY = y; atX = cx;
            }
        }
        return bestY;
    }

    // 沿 foothold 走一步：段内线性插值；跨出端点时，若另一条段在同一 x 相接，
    // 就当作同一块地面直接接续（原端 foothold 的 prev/next 语义）。
    // 之前用「高度差 ≤ 0.6u 才算支撑」判接缝，台阶差 24px 以上就会误判成踏空，
    // 下面又没有段接住时角色会一路掉到地图外。
    public static bool FollowGround(float fromX, float curY, float toX, out float outY)
    {
        outY = curY;
        if (Terrain == null || Terrain.Count == 0) { outY = GroundY; return true; }

        const float EdgeTol = 1.5f / Art.PPU;    // 端点重合容差：1.5px

        // 1) 当前脚下这条段：覆盖 fromX 且高度最接近 curY
        int cur = -1;
        float curLo = 0f, curHi = 0f, bestD = float.MaxValue;
        for (int i = 0; i < Terrain.Count; i++)
        {
            var f = Terrain[i];
            if (Mathf.Abs(f.z - f.x) < 0.01f) continue;
            float lo = Mathf.Min(f.x, f.z), hi = Mathf.Max(f.x, f.z);
            if (fromX < lo || fromX > hi) continue;
            float y = Mathf.Lerp(f.y, f.w, (fromX - f.x) / (f.z - f.x));
            float d = Mathf.Abs(y - curY);
            if (d < bestD) { bestD = d; cur = i; curLo = lo; curHi = hi; }
        }
        if (cur < 0) return false;               // 脚下没有段：交给下落

        // 2) 目标 x 仍在同一条段上
        var c = Terrain[cur];
        if (toX >= curLo && toX <= curHi)
        {
            outY = Mathf.Lerp(c.y, c.w, (toX - c.x) / (c.z - c.x));
            return true;
        }

        // 3) 跨出端点：找在该端点相接的段（端点重合即视为相接）
        float edge = toX > curHi ? curHi : curLo;
        int next = -1;
        float nextD = float.MaxValue;
        for (int i = 0; i < Terrain.Count; i++)
        {
            if (i == cur) continue;
            var f = Terrain[i];
            if (Mathf.Abs(f.z - f.x) < 0.01f) continue;
            float lo = Mathf.Min(f.x, f.z), hi = Mathf.Max(f.x, f.z);
            bool joined = Mathf.Abs(lo - edge) <= EdgeTol || Mathf.Abs(hi - edge) <= EdgeTol;
            if (!joined) continue;
            float y = Mathf.Lerp(f.y, f.w, (Mathf.Clamp(toX, lo, hi) - f.x) / (f.z - f.x));
            float d = Mathf.Abs(y - curY);
            if (d < nextD) { nextD = d; next = i; }
        }
        if (next < 0) return false;              // 端点外没有接续段：真悬崖

        var n = Terrain[next];
        float nlo = Mathf.Min(n.x, n.z), nhi = Mathf.Max(n.x, n.z);
        outY = Mathf.Lerp(n.y, n.w, (Mathf.Clamp(toX, nlo, nhi) - n.x) / (n.z - n.x));
        return true;
    }

    // 判断 x 处脚下是否存在可走的水平段（不含竖直 foothold 墙）。
    // 「去掉自动上下台阶」后仅用于检测真悬崖：脚下有段就保持 grounded 直线行走，
    // 脚下无段才转入下落，不再把地形高度吸附到 foothold 上。
    static bool GroundExists(float x)
    {
        if (Terrain == null || Terrain.Count == 0) return true;
        for (int i = 0; i < Terrain.Count; i++)
        {
            var f = Terrain[i];
            if (Mathf.Abs(f.z - f.x) < 0.01f) continue;   // 竖直段是墙，不算地面
            float lo = Mathf.Min(f.x, f.z), hi = Mathf.Max(f.x, f.z);
            if (x >= lo && x <= hi) return true;
        }
        return false;
    }

    // 原端 WalkSpeed 的 100% 基准（Character.wz 角色 info/walkSpeed = 125px/s）。
    // 怪物 info/speed 是「相对 100% 的百分比偏移」，两边共用这个基准，
    // 所以怪物速度也要用它换算，不能各写一个魔数（见 FieldDirector.ApplyStats）。
    public const float BaseWalkPx = 125f;

    // 普攻是矩形判定：近战框上沿大约在脚底上方 48px，够不到上层平台。
    // 之前只比 |dx|，站在下层能把上层平台的怪打下来（用户实测截图）。
    public const float MeleeVertical = 1.2f;

    [Header("移动")]
    public float moveSpeed = BaseWalkPx / Art.PPU;
    public float jumpSpeed = 555f / Art.PPU;
    public float gravity   = -2000f / Art.PPU;

    [Header("普攻")]
    public float attackInterval = 0.55f;
    public float reach          = 2.3f;
    public float attackDamage   = 11f;

    [Header("状态")]
    public float hpMax = 100f;
    public float hp    = 100f;
    public float invulnUntil;
    public float mpMax = 100f;
    public float mp = 100f;
    float attackBonus;
    float attackBonusUntil;
    public float SkillActionDuration { get; private set; } = 0.55f;

    public int   lane     = 2;
    public int   facing   = 1;
    public bool  grounded = true;

    [HideInInspector] public float worldX;
    [HideInInspector] public float jumpY;
    [HideInInspector] public float terrainY = GroundY;   // 脚下地形 y（跟随 foothold 起伏）

    // 切关 / 重生时调用：把脚下地形吸附到 x 处的 foothold 上
    public void ResetTerrain()
    {
        CancelRush();
        climb = null;
        grabCooldown = 0f;
        terrainY = GroundAt(worldX, GroundY);
        jumpY = 0f;
        vy = 0f;
        grounded = true;
    }

    // 技能系统的施法动画入口。SpriteAnim 挂在 Art.Attach 建的子节点上，
    // GetComponent<SpriteAnim>() 在根节点上找不到——之前技能因此完全没有角色动作。
    public void PlaySkill(string skill,float phaseDuration=0)
    {
        if (vis == null || IsClimbing) return;
        string action = "skill_" + skill;
        if (Art.GetClip(vis.kind, vis.key, action) == null) action = "atk";
        SkillActionDuration = GetSkillActionDuration(skill);
        float playbackSpeed=phaseDuration>0 ? SkillActionDuration/phaseDuration : 1;
        if(phaseDuration>0)SkillActionDuration=phaseDuration;
        vis.PlayOnce(action,playbackSpeed);
    }

    public float GetSkillActionDuration(string skill)
    {
        if(vis==null)return attackInterval;
        var clip=Art.GetClip(vis.kind,vis.key,"skill_"+skill) ?? Art.GetClip(vis.kind,vis.key,"atk");
        float duration=0;
        if(clip!=null)foreach(float delay in clip.delays)duration+=delay;
        return duration>0 ? duration : attackInterval;
    }

    float laneVis;
    float vy;
    float fallSpeed = 670f / Art.PPU;
    float atkTimer;
    bool attackRequested;
    float laneCooldown;
    SpriteRenderer sr;
    SpriteAnim vis;          // 真素材动画（有素材时接管显示）
    bool moving;

    // —— 普攻前摇 ——
    // 原版的伤害不是按下就结算，而是挥到「砍中那一帧」才出来：
    // swingO1 三帧 300/150/350ms，第 1 帧（150ms 起）才是刀划过的时候。
    // 所以普攻先把前摇计时器挂上，到点再结算伤害/顿帧/飘字——
    // 挥砍动作立即播，伤害晚一点点到，手感从「打空气」变成「刀确实砍中了」。
    struct PendingHit
    {
        public EnemyUnit enemy;
        public BossUnit boss;
        public DamageResult hit;
        public float timer;
        public int facing;
        public bool active;
    }
    PendingHit pending;

    // 残影：与当前挥砍同步的帧表和游标
    string trailName;
    string trailPose;              // 残影数据索引用的普攻姿势名（起挥时算一次）
    AfterimageFx.Frame[] trailFrames;
    int trailCursor;
    float swingPlayback = 1f;
    float swingSfxAt = -1f;      // >=0：到点播挥砍音效（空挥路径，无 pending）

    // 装扮覆盖层（服饰）。武器不再单独叠层：
    // 原版客户端的武器是 CharacterRenderer 按 zmap 合成进角色帧的，
    // 程序化画的武器贴在身上只会变成「盖在脸上的静态色块」（武器永远不动，攻击像没动作）。
    SpriteRenderer coatSr;
    Sprite dyedFrame;        // 已经染过色的角色帧（动画换帧才重染）

    public float DepthOffset    => Terrain != null ? 0f : (laneVis - (Lanes - 1) * 0.5f) * LaneStep;
    public int   SortingOrder   => 300 - lane * 12;
    public Vector2 FeetPos      => new Vector2(worldX, terrainY + jumpY + DepthOffset);

    // 身体判定框（世界尺寸，脚底为原点）。怪物接触伤害必须真实碰到身体，
    // 站在高低不同的平台上、只是 Y 方向错位重叠不算碰到。
    public float BodyHeight
    {
        get
        {
            float h = 1.5f;                                  // 兜底方块 0.82 x 1.5
            if (sr != null && sr.enabled && sr.sprite != null) h = sr.bounds.size.y;
            return Mathf.Max(0.8f, h);
        }
    }
    public float BodyHalfWidth
    {
        get
        {
            float w = 0.41f;
            if (sr != null && sr.enabled && sr.sprite != null) w = sr.bounds.size.x * 0.5f;
            return Mathf.Max(0.22f, w);
        }
    }

    void Awake()
    {
        Profile.Load();
        Profile.LeveledUp += OnLevelUp;
        var physics = WzAssets.FileRoot("Map/Physics.img");
        moveSpeed = physics.Int("walkSpeed", 125) / Art.PPU;
        jumpSpeed = physics.Int("jumpSpeed", 555) / Art.PPU;
        gravity = -physics.Int("gravityAcc", 2000) / Art.PPU;
        fallSpeed = physics.Int("fallSpeed", 670) / Art.PPU;
        laneVis = lane;
        // 根节点的 SpriteRenderer 只是「有没有素材」的占位，形状不放在这里：
        // 根节点一旦被非等比缩放（旧版 0.82 x 1.5），贴图就会被压扁拉长，
        // 角色看起来像被揉过。所有缩放只允许等比，且只作用在子节点上。
        sr = GetComponent<SpriteRenderer>();
        if (sr != null) { sr.enabled = false; }
        BuildArt();
        ApplyLoadout();
        SyncTransform();
    }

    // 有 WZ 素材就用素材；没有就退回纯色方块（保证任何状态下都能跑）
    // 升级后按原端 Character.levelUp → changeHpMp(localMaxHp, localMaxMp) 直接回满。
    void OnLevelUp(int level) => ApplyProgression();

    public void ApplyProgression()
    {
        baseAttributes = Profile.Attributes;
        RefreshAttributes();
        hpMax = Attributes.maxHp;
        mpMax = Attributes.maxMp;
        hp = hpMax;
        mp = mpMax;
        attackDamage = EffectiveAtk;
    }

    void OnDestroy()
    {
        Profile.LeveledUp -= OnLevelUp;
    }

    // —— 魔力 ——
    // 原端 skill.mpCon 是硬门槛（客户端先判、服务端再判一次）。本地没有服务端，
    // 判定在 SkillSystem.TryCast 里做，这里只提供扣蓝的原子操作。
    public bool TrySpendMp(int amount)
    {
        if (amount <= 0) return true;
        if (mp < amount) return false;
        mp -= amount;
        return true;
    }

    // 自然回复：原版客户端逻辑（服务端没有这段，所以只能按客户端行为复刻）。
    //   MP —— 每 10 秒自动回复，最低 3 点（Mapletip/MP：「regained automatically …
    //          at a minimum rate of 3 MP per 10 seconds」）；高等级按最大 MP 的 1% 递增。
    //   HP —— 站立不动每 10 秒回 10 点（Mapletip/Health Points：「By default,
    //          you gained 10 HP when standing still per 10 seconds」）。
    // 节拍固定 10 秒，用 unscaledDeltaTime 累加，不受顿帧/慢动作影响。
    public const float RegenInterval = 10f;
    public float RegenTimer { get; private set; }

    public void TickRegen(float dt)
    {
        if (dt <= 0f || hp <= 0f) return;
        RegenTimer += dt;
        if (RegenTimer < RegenInterval) return;
        RegenTimer -= RegenInterval;
        if (RegenTimer < 0f) RegenTimer = 0f;

        int mpGain = Mathf.Max(3, Mathf.CeilToInt(mpMax * 0.01f));
        mp = Mathf.Min(mpMax, mp + mpGain);
        // 「站立不动」才回 HP：走动/跳跃/攀爬期间不给，和原版一致
        if (grounded && !moving && climb == null) hp = Mathf.Min(hpMax, hp + 10f);
    }

    void BuildArt()
    {
        // 发型是真实素材：artKey = 职业 + "_" + 发型下标（chars/<职业>_h0…）
        string artKey = Profile.cls + "_" + HairStyles.KeyOf(HairStyles.IndexOf(Profile.hair));
        if (Art.Has("chars", artKey))
        {
            vis = Art.Attach(transform, "chars", artKey, "stand", 0f);
            if (vis != null)
            {
                var vr = vis.Renderer;
                vr.sortingOrder = 300;
                sr = vr;                      // 受击闪红等反馈改打到素材层
                BuildCosmetics();
                return;
            }
        }

        throw new System.IO.InvalidDataException("Missing original character IMG recipe: " + artKey);
    }

    // 武器属性 + 装扮覆盖层：每次（重新）进关都会按当前 Profile 重建
    void ApplyLoadout()
    {
        weapon = WeaponDefs.Get(Profile.equipWeapon);
        baseAttributes = Profile.Attributes;
        RefreshAttributes();
        hpMax = Attributes.maxHp; mpMax = Attributes.maxMp; hp = hpMax; mp = mpMax;
        CancelRush();
        attackDamage = EffectiveAtk;
        attackInterval = BaseInterval;
        reach = BaseReach;
        BuildCosmetics();
    }

    // —— 装扮覆盖层 ——
    // 头发/服饰不是几何色块，而是「把角色原画的那片像素染色」得到的新贴图，
    // 和角色帧同尺寸同轴心，挂在素材节点下 → 翻转、缩放、换帧全部自动跟随。
    void BuildCosmetics()
    {
        // Preserve original equipment pixels; old local dye overlays are not client equipment.
        if (coatSr != null) coatSr.enabled = false;
    }

    // 只在动画换帧时重染（换帧才需要新贴图，缓存命中后几乎零开销）
    void ApplyDye()
    {
        if (coatSr != null) coatSr.enabled = false;
    }

    void Update()
    {
        if (GameLoop.I != null && (GameLoop.I.state != GameLoop.GameState.Playing || GameLoop.I.gameOver)) return;
        if (InputLocked) { SyncTransform(); return; }   // 换图黑场中：不读输入、不跑物理
        float dt = Time.deltaTime;
        if (dt > 0f) Step(dt);   // 顿帧期间 dt==0，逻辑自动跳过
        TickRegen(Time.unscaledDeltaTime);
        SyncTransform();
    }

    void Step(float dt)
    {
        // —— 横向 ——
        // 键盘优先（编辑器调试），没按键时读摇杆。两者共用同一个 h，后面的逻辑完全一致
        float h = TouchInput.MoveH;
        bool jumpReq = TouchInput.TakeJump();
        climbInput = TouchInput.MoveV;
        int steps = Mathf.Max(1, Mathf.CeilToInt(dt / 0.008f));
        for (int i = 0; i < steps; i++) StepMovement(h, jumpReq && i == 0, dt / steps);

        // —— 深度层：一次换一层，带冷却 ——
        laneCooldown -= Time.unscaledDeltaTime;
        int d = 0;
        if (InputHub.Pressed(GameAction.Up)) d = -1;
        if (InputHub.Pressed(GameAction.Down)) d = +1;
        float v = TouchInput.MoveV;
        if (d == 0 && Mathf.Abs(v) > 0.6f && laneCooldown <= 0f)
        {
            d = v > 0f ? -1 : 1;
            laneCooldown = 0.22f;
        }
        if (d == 0) d = TouchInput.TakeLaneStep();   // 摇杆上下拨
        if (Terrain != null) { lane = 2; d = 0; }
        if (d != 0) lane = Mathf.Clamp(lane + d, 0, Lanes - 1);

        // 指数插值：快但不生硬。瞬间跳层会像"走格子"，破坏 2.5D 错觉
        laneVis = Mathf.Lerp(laneVis, lane, 1f - Mathf.Exp(-14f * dt));

        if (vis != null)
        {
            string action = IsClimbing ? (climb.ladder ? "ladder" : "rope") :
                !grounded ? "jump" : moving ? "walk" : "stand";
            vis.SetBase(action);
            if (IsClimbing) vis.Play(action, false); // 抓绳打断攻击/受击残留
            vis.speed = IsClimbing ? Mathf.Abs(climbInput) : 1f;
        }

        // —— 自动普攻 ——
        // Boss 优先：Boss 体积大、通常只有一只，误判成本比杂兵高得多
        atkTimer -= dt;
        if ((Terrain == null || attackRequested) && !IsClimbing && atkTimer <= 0f && (vis == null || !vis.IsOneShot))
        {
            var b = FindBossTarget();
            if (b != null)
            {
                atkTimer = attackInterval;
                facing = b.transform.position.x >= worldX ? 1 : -1;
                StrikeBoss(b);
            }
            else
            {
                var t = FindTarget();
                if (t != null)
                {
                    atkTimer = attackInterval;
                    facing = t.transform.position.x >= worldX ? 1 : -1;
                    Strike(t);
                }
                else
                {
                    // 没有目标也挥一下刀：普攻是自动索敌的，没有怪时原本连动作都没有，
                    // 于是「按了攻击键」看不出任何反馈（虚拟键最容易被误判成没生效）
                    if (attackRequested) SwingEmpty();
                    // 没目标时短轮询即可。若这里不重置，atkTimer 会一直 <=0，
                    // 于是每帧都跑一次 FindObjectsOfType —— 那是白白烧 CPU
                    atkTimer = 0.12f;
                }
            }
        }
        attackRequested = false;

        // —— 前摇结算 + 残影跟进（顿帧期间 dt==0，两者一起停，节奏不会乱）——
        TickPendingHit(dt);
        TickTrail();

        // 走过就捡：原版自动拾取（按键 Pickup 是手动补拾，见 InputHub）
        GroundItem.TryAutoPickup(FeetPos);
    }

    // 起一次挥砍：播动画、按攻击间隔换算播放速度、挂残影帧表，返回前摇秒数。
    // 没有素材时返回 0，调用方立即结算。
    float BeginSwing()
    {
        pending.active = false;      // 新挥砍开始，上一段前摇作废（理论上前摇必早于挥砍结束）
        swingSfxAt = -1f;            // 上一段空挥计时作废
        swingPlayback = 1f;
        trailFrames = null;
        trailPose = null;
        trailCursor = 0;
        if (vis == null) return 0f;
        var clip = Art.GetClip(vis.kind, vis.key, "atk");
        if (clip == null || clip.delays.Length == 0) return 0f;
        // 挥砍播放速度：整套动作在攻击间隔的 90% 内播完。
        // 原版挥砍速度由武器攻速决定（本地无该表，用间隔反推，快武器=快挥砍）。
        swingPlayback = Mathf.Clamp(clip.Duration / Mathf.Max(0.12f, attackInterval * 0.9f), 0.8f, 2.2f);
        vis.PlayOnce("atk", swingPlayback);

        // 残影帧表：按当前武器 + 实际普攻姿势取（swingO1/stabT1/shoot1…）
        trailName = WzCharacter.Afterimage(vis.key);
        if (trailName != null)
        {
            trailPose = WzCharacter.AttackPoseName(vis.key);
            var frames = trailPose != null ? AfterimageFx.Frames(trailName, trailPose) : null;
            if (frames != null && frames.Length > 0) trailFrames = frames;
        }
        return clip.delays[0] / swingPlayback;   // 第 0 帧（起手）播完 = 刀挥到
    }

    // 空挥：没有目标也要有完整动作和速度
    void SwingEmpty()
    {
        pending.active = false;
        float windup = BeginSwing();
        // 挥砍音效与伤害同帧：空挥没有 pending，用独立计时器到点播
        swingSfxAt = windup > 0.001f ? windup : 0f;
    }

    // 挥砍音效（源端 Sound/Weapon.img 按武器 info/attack 取家族：单手剑/双手/弓/弩/枪/杖/爪/指虎/火器）
    void PlaySwingSfx()
    {
        if (vis == null) return;
        AudioManager.Play(WzCharacter.AttackSoundKey(vis.key) ?? "Weapon/swordL/Attack", 0.85f);
    }

    void TickPendingHit(float dt)
    {
        // 空挥音效计时（顿帧期间 dt==0，与伤害/刀光同步停）
        if (swingSfxAt >= 0f)
        {
            swingSfxAt -= dt;
            if (swingSfxAt < 0f) PlaySwingSfx();
        }
        if (!pending.active) return;
        pending.timer -= dt;
        if (pending.timer > 0f) return;
        pending.active = false;
        var p = pending;
        if (p.boss != null)
        {
            if (!p.boss.dead) HitBoss(p.boss, p.hit);
        }
        else if (p.enemy != null && !p.enemy.dead)
        {
            HitEnemy(p.enemy, p.hit, p.facing);
        }
        // 挥砍音效与伤害同一帧发出
        PlaySwingSfx();
    }

    // 残影跟进：角色挥到残影帧号时生成那一帧的全部画布。
    // 受击反应（alert）会抢走一次性动作，那时不再出刀光。
    void TickTrail()
    {
        if (trailFrames == null || vis == null || !vis.IsOneShot || vis.Current != "atk") return;
        while (trailCursor < trailFrames.Length && trailFrames[trailCursor].index < vis.FrameIndex) trailCursor++;
        if (trailCursor >= trailFrames.Length) return;
        if (trailFrames[trailCursor].index != vis.FrameIndex) return;
        AfterimageFx.Spawn(trailName, trailPose, trailFrames[trailCursor], vis.transform, facing, swingPlayback);
        trailCursor++;
    }

    // 使用原 Map/Physics.img 的像素单位：125px/s、555px/s、2000px/s²。
    // 空中位置独立于脚下平台；只在下降时扫过 foothold 才落地。
    void StepMovement(float h, bool jumpReq, float dt)
    {
        if (rushRemaining > 0)
        {
            h = rushFacing * rushSpeed / moveSpeed;
            float step = Mathf.Min(dt, rushRemaining / rushSpeed);
            float before = worldX;
            rushRemaining = Mathf.Max(0,rushRemaining-rushSpeed*step);
            // 同一套墙/坡/落地求解；递归一次时不再进入突进分支。
            float remaining = rushRemaining;
            rushRemaining = 0;
            float climbBefore = climbInput; climbInput = 0;
            StepMovement(h, false, step);
            climbInput = climbBefore;
            rushRemaining = Mathf.Abs(worldX-before) < .00001f ? 0 : remaining;
            return;
        }
        grabCooldown = Mathf.Max(0f, grabCooldown - dt);
        if (StepClimb(h, jumpReq, dt)) return;
        if (jumpReq && grounded)
        {
            vy = jumpSpeed;
            grounded = false;
            AudioManager.Play("Game/Jump", 0.7f, false);
        }
        float previousY = terrainY + jumpY;
        float nextX = ClampX(worldX + h * moveSpeed * dt);
        moving = Mathf.Abs(h) > 0.15f;
        if (moving) facing = h > 0f ? 1 : -1;

        // 原 WZ 的竖直 foothold 是墙；独立悬空平台只在下降时承接角色。
        if (Terrain != null)
            foreach (var wall in Terrain)
            {
                if (Mathf.Abs(wall.z - wall.x) >= 0.01f) continue;
                if ((worldX < wall.x && nextX >= wall.x) || (worldX > wall.x && nextX <= wall.x))
                    if (previousY < Mathf.Max(wall.y, wall.w) - 0.01f &&
                        previousY >= Mathf.Min(wall.y, wall.w) - 0.01f)
                        nextX = worldX;
            }
        if (grounded)
        {
            // 已去掉「走路自动上下台阶」：角色水平移动时不再把地形高度吸附到脚下
            // foothold（含跨段台阶/斜坡的自动接续），保持当前 y 直线行走；只有跳跃
            // （jumpY 抛物线）才改变高度。脚下没有任何可走段（真悬崖）时才转入下落。
            if (!GroundExists(nextX))
            {
                grounded = false;
                vy = 0f;
            }
        }
        worldX = nextX;
        if (grounded) return;

        // 帧内细分由调用方控制；用解析位移避免跳高随帧率变化。
        float nextY = previousY + vy * dt + 0.5f * gravity * dt * dt;
        vy = Mathf.Max(vy + gravity * dt, -fallSpeed);
        float landing;
        if (vy <= 0f && FindLanding(worldX, previousY, nextY, out landing))
        {
            terrainY = landing;
            jumpY = 0f;
            vy = 0f;
            grounded = true;
        }
        else jumpY = nextY - terrainY;

        // 掉到地图可视范围以下：说明一条 foothold 都没接住（真空缺口，或数据异常）。
        // 原端会由服务端把角色复位回最近地面；这里做等价兜底，避免一路掉到屏幕外卡死。
        if (nextY < FallFloor) RecoverFromFall();
    }

    void RecoverFromFall()
    {
        float atX;
        float y = NearestGround(worldX, out atX);
        worldX = ClampX(atX);
        terrainY = y;
        jumpY = 0f;
        vy = 0f;
        grounded = true;
        climb = null;
        grabCooldown = 0.3f;
        Debug.Log("[Player] 掉出地图，复位到 x=" + atX.ToString("F2") + " y=" + y.ToString("F2"));
    }

    bool StepClimb(float h, bool jumpReq, float dt)
    {
        float y = terrainY + jumpY;
        if (climb == null && grabCooldown <= 0f && Mathf.Abs(climbInput) > 0.15f && Climbs != null)
        {
            // 抓取横向距离沿用原端的「站在绳梯正下方」语义（±10px）。
            // 纵向给 100px 容差：WZ 里绳梯 y2 是图元下端，常常悬在落脚平台上方
            // （全量统计：88 条差 ≤40px，17 条 40~80px，3 条 80~120px），
            // 原来用 y < bottom 直接判死，站在梯脚按上根本抓不到。
            float distance = 10f / Art.PPU;
            float bottomSlack = 100f / Art.PPU;
            float topSlack = 8f / Art.PPU;
            foreach (var line in Climbs)
            {
                float dx = Mathf.Abs(worldX - line.x);
                if (dx > distance) continue;
                if (y < line.bottom - bottomSlack || y > line.top + topSlack) continue;
                // 站在平台顶端正上方按上 = 想往上走，不是想爬；只有按上时忽略顶端这条线。
                if (grounded && climbInput > 0f && y >= line.top - topSlack) continue;
                distance = dx;
                climb = line;
            }
            if (climb != null)
            {
                worldX = climb.x;
                grounded = false;
                vy = 0f;
                // 吸附到绳身上：从地面够到悬空的绳底时，脚底先贴到 y2 再往上爬
                float cy = terrainY + jumpY;
                if (cy < climb.bottom) jumpY = climb.bottom - terrainY;
                else if (cy > climb.top) jumpY = climb.top - terrainY;
                // 必须用吸附后的高度继续算位移：不然第一帧还在绳底下方，
                // 下面「nextY < climb.bottom 就脱手」会立刻把刚抓住的绳子放掉。
                y = terrainY + jumpY;
            }
        }
        if (climb == null) return false;
        // 在绳上按跳跃 = 脱手（原端就是这样跳离的，不需要同时按方向）
        if (jumpReq)
        {
            climb = null;
            grabCooldown = 0.25f;
            vy = jumpSpeed;
            if (Mathf.Abs(h) > 0.15f) facing = h > 0f ? 1 : -1;
            return false;
        }
        float nextY = y + climbInput * moveSpeed * dt;
        if (nextY >= climb.top)
        {
            if (climb.upperExit && climbInput > 0f)
            {
                // 梯子顶端通常穿出平台十几像素，容差按 30px 取；HasSupport 保证落点真有地面。
                float top = GroundAt(worldX, climb.top);
                if (HasSupport(worldX, top) && Mathf.Abs(top - climb.top) <= 30f / Art.PPU)
                {
                    terrainY = top; jumpY = 0f; grounded = true;
                    climb = null; grabCooldown = 0.2f;
                    return true;
                }
            }
            nextY = climb.top;
        }
        if (nextY < climb.bottom)
        {
            climb = null; grabCooldown = 0.2f; vy = 0f;
            return false;
        }
        jumpY = nextY - terrainY;
        return true;
    }

    static bool HasSupport(float x, float y)
    {
        if (Terrain == null || Terrain.Count == 0) return true;
        foreach (var f in Terrain)
        {
            if (x < Mathf.Min(f.x, f.z) || x > Mathf.Max(f.x, f.z) || Mathf.Abs(f.z - f.x) < 0.01f) continue;
            float height = Mathf.Lerp(f.y, f.w, (x - f.x) / (f.z - f.x));
            if (Mathf.Abs(height - y) < 0.01f) return true;
        }
        return false;
    }

    static bool FindLanding(float x, float previousY, float nextY, out float landing)
    {
        landing = float.NegativeInfinity;
        if (Terrain == null || Terrain.Count == 0)
        {
            landing = GroundY;
            return previousY >= GroundY && nextY <= GroundY;
        }
        foreach (var f in Terrain)
        {
            if (x < Mathf.Min(f.x, f.z) || x > Mathf.Max(f.x, f.z) || Mathf.Abs(f.z - f.x) < 0.01f) continue;
            float height = Mathf.Lerp(f.y, f.w, (x - f.x) / (f.z - f.x));
            if (previousY >= height - 0.01f && nextY <= height && height > landing)
                landing = height;
        }
        return !float.IsNegativeInfinity(landing);
    }

    BossUnit FindBossTarget()
    {
        foreach (var b in FindObjectsOfType<BossUnit>())
        {
            if (b == null || b.dead) continue;
            if (Mathf.Abs(b.Lane - lane) > 2) continue;      // Boss 占 3 层
            float dx = b.transform.position.x - worldX;
            // Boss 本体宽，判定给一点宽限，否则贴脸时反而打不到
            if (dx < -(reach + 1.3f) || dx > reach + 1.3f) continue;
            return b;
        }
        return null;
    }

    void StrikeBoss(BossUnit b)
    {
        var hit = RollAttack(b.CombatStats);
        float windup = BeginSwing();
        if (windup <= 0.001f)
        {
            HitBoss(b, hit);
            PlaySwingSfx();          // 无素材：立即出声
            return;
        }
        pending = new PendingHit { boss = b, enemy = null, hit = hit, timer = windup, facing = facing, active = true };
    }

    EnemyUnit FindTarget()
    {
        EnemyUnit best = null;
        float bestD = float.MaxValue;
        foreach (var e in EnemyUnit.All)
        {
            if (e == null || e.dead) continue;
            if (Mathf.Abs(e.Lane - lane) > 1) continue;           // 层差 > 1 够不着

            // 双向查找，不按 facing 过滤：这个游戏的操作只有「移动 + 点技能」，
            // 普攻全自动。若只打正面，站着不动时背后的怪永远打不着，
            // 而「清完怪才推进」会让关卡彻底卡死（自动化测试实测卡了 50 秒）。
            float dx = e.transform.position.x - worldX;
            if (!InMeleeBand(FeetPos, e.transform.position, reach)) continue;

            float d = Mathf.Abs(dx);
            if (d < bestD) { bestD = d; best = e; }
        }
        return best;
    }

    // 普攻判定框：横向 ±reach，纵向 ±MeleeVertical（以双方脚底为准）。
    // 原客户端的近战框就是「orig 前方的矩形」，高度远小于一层平台；
    // 本地没有武器攻击框 WZ 数据，用固定高度近似，并按全地图 foothold 统计校验
    // （同层台阶/斜坡都 < 1.2u，层与层都远大于它）。
    public static bool InMeleeBand(Vector2 feet, Vector3 target, float reach)
    {
        if (Mathf.Abs(target.x - feet.x) > reach) return false;
        return Mathf.Abs(target.y - feet.y) <= MeleeVertical;
    }

    // 键盘 Ctrl / 虚拟攻击键：把自动挥砍的计时器清零，下一次 Step 立刻结算。
    // 原版普攻是玩家主动挥的，这里保留自动索敌，只把「什么时候挥」交给玩家。
    public void RequestAttack() { attackRequested = true; }

    void Strike(EnemyUnit e)
    {
        var hit = RollAttack(e.CombatStats);
        float windup = BeginSwing();
        if (windup <= 0.001f)
        {
            HitEnemy(e, hit, facing);
            PlaySwingSfx();          // 无素材：立即出声
            return;
        }
        pending = new PendingHit { enemy = e, boss = null, hit = hit, timer = windup, facing = facing, active = true };
    }

    // 前摇到点：真正结算。原版近战的视觉就是角色挥砍帧本身（武器合成在帧里），
    // 没有额外的弧光贴片；伤害/顿帧/飘字都在这一下出去。
    void HitEnemy(EnemyUnit e, DamageResult hit, int dir)
    {
        if (e == null || e.dead || hit.Amount <= 0) return;
        e.TakeHit(hit, dir);
        HitFeel.Punch(1f);
    }

    void HitBoss(BossUnit b, DamageResult hit)
    {
        if (b == null || b.dead || hit.Amount <= 0) return;
        b.TakeHit(hit, facing);
        HitFeel.Punch(1f);
    }

    public void TakeDamage(float dmg, int dir, bool magic = false, int attackerLevel = 1)
    {
        if (hp <= 0f || Time.unscaledTime < invulnUntil) return;
        RefreshAttributes();
        dmg = CombatRules.Incoming(dmg, Attributes, magic, attackerLevel, Random.value).Amount;
        if (dmg <= 0) return;
        hp = Mathf.Max(0f, hp - dmg);
        invulnUntil = Time.unscaledTime + 0.5f;
        if (IsClimbing) { climb = null; grabCooldown = 0.25f; vy = 0f; grounded = false; }
        worldX += dir * 0.25f;
        Fx.Flash(sr, new Color(1f, 0.35f, 0.35f));
        if (vis != null)
        {
            // 受击反应是 alert（3 帧 x 300ms）。原版播完整段，但本地玩家受击后还要继续跑，
            // 整段 900ms 的硬直会把走路动画摁住，所以压到 1.6 倍速（≈560ms，接近无敌窗）
            vis.PlayOnce(hp <= 0f ? "die" : "hit", hp <= 0f ? 1f : 1.6f);
        }
        HitFeel.Punch(0.6f);
        // 玩家挨打是原版的蓝字（NoBlue），和打怪的红字区分开
        DamageText.Show(new Vector3(FeetPos.x, FeetPos.y + 2.55f, 0f), dmg, "blue", 0.85f);
        if (hp <= 0f && GameLoop.I != null) GameLoop.I.OnPlayerDead();
    }

    public void Heal(float v) => hp = Mathf.Min(hpMax, hp + v);

    // 死亡确认后由服务端下发的血量复活（源端 Character.respawn）：
    // 不是满血 —— 常规 50，护符 30%，特殊图满血。
    public void Revive(int hpValue, int mpValue)
    {
        CancelRush();
        climb = null;
        grabCooldown = 0f;
        hp = Mathf.Clamp(hpValue, 1, hpMax);
        mp = Mathf.Clamp(mpValue, 0, mpMax);
        pending.active = false;
        attackBonus = 0f; attackBonusUntil = 0f;
        invulnUntil = Time.unscaledTime + 1.5f;
        GetComponent<SkillSystem>()?.CancelActions();
        if (vis != null) { vis.SetBase("stand"); vis.Play("stand", true); }
        Fx.Flash(sr, new Color(1f, 1f, 1f));
    }

    // 死亡姿势：源端动作名是 dead（躺在地上），不是 die（倒地过程）
    public void PlayDeadPose()
    {
        if (vis == null) return;
        var action = Art.GetClip(vis.kind, vis.key, "dead") != null ? "dead" : "die";
        vis.SetBase(action);
        vis.Play(action, false);
    }

    public void AddAttackBonus(float amount, float seconds)
    {
        attackBonus = amount;
        attackBonusUntil = Time.unscaledTime + seconds;
    }

    void RefreshAttributes()
    {
        baseAttributes = Profile.Attributes;
        Attributes = baseAttributes.WithWeapon(weapon, Time.unscaledTime < attackBonusUntil ? Mathf.RoundToInt(attackBonus) : 0,
            out weaponAttack, out magicAttack);
    }
    public DamageResult RollAttack(System.Collections.Generic.Dictionary<string, object> target, NativeSkillDefinition skill = null)
    {
        RefreshAttributes();
        return CombatRules.Attack(Attributes, weapon, weaponAttack, magicAttack, target, skill,
            Random.value, Random.value, Random.value);
    }
    public float EffectiveAtk
    {
        get
        {
            RefreshAttributes();
            bool dexWeapon = weapon.kind == WeaponKind.bow || weapon.kind == WeaponKind.crossbow || weapon.kind == WeaponKind.gun;
            if (weapon.kind == WeaponKind.claw) return CombatRules.MaxPhysicalDamage(Attributes.luk,Attributes.dex+Attributes.str,weaponAttack,weapon.multiplier);
            return dexWeapon ? CombatRules.MaxPhysicalDamage(Attributes.dex, Attributes.str, weaponAttack, weapon.multiplier) :
                CombatRules.MaxPhysicalDamage(Attributes.str, Attributes.dex, weaponAttack, weapon.multiplier);
        }
    }
    public void BeginRush(NativeSkillDefinition skill, float duration)
    {
        // WZ Rush 无距离/速度字段，以前向攻击框宽度作为最大行程；时长取原角色动作。
        // 精确 EXE 移动常量尚未提供，此处是显式的本地运动适配。
        rushRemaining = Mathf.Max(0, skill.Max.x-skill.Min.x);
        rushSpeed = rushRemaining / Mathf.Max(.01f,duration);
        rushFacing = facing;
    }
    public void CancelRush() { rushRemaining = 0; }

    // 换地图 / 从传送门落地：直接指定落脚点（地面 y 由调用方按 foothold 求出）。
    public void PlaceAt(float x, float groundY)
    {
        CancelRush();
        climb = null;
        grabCooldown = 0f;
        worldX = ClampX(x);
        terrainY = groundY;
        jumpY = 0f;
        vy = 0f;
        grounded = true;
        laneVis = lane;
        SyncTransform();
    }

    public void ResetCombatState()
    {
        hp = hpMax; mp = mpMax;
        CancelRush();
        attackBonus = 0; attackBonusUntil = 0;
        pending.active = false;
        GetComponent<SkillSystem>()?.CancelActions();
    }

    void SyncTransform()
    {
        transform.position = new Vector3(worldX, terrainY + jumpY + DepthOffset, 0f);
        if (sr == null) return;
        sr.sortingOrder = SortingOrder;
        if (vis != null)
        {
            vis.SetFacing(facing);        // 素材靠 flipX 转向，别再翻 scale
            ApplyDye();
            return;
        }
        var s = sr.transform.localScale;
        sr.transform.localScale = new Vector3(Mathf.Abs(s.x) * (facing >= 0 ? 1f : -1f), s.y, 1f);
    }
}
