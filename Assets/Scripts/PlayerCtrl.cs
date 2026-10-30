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
    public bool UsesRangedWeapon => weapon.kind==WeaponKind.bow || weapon.kind==WeaponKind.crossbow || weapon.kind==WeaponKind.gun || weapon.kind==WeaponKind.claw;
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
    // 与 Terrain 一一对应：true 表示这条 foothold 在原端带 forbidFallDown，
    // 站在上面按 ↓+跳跃不许穿下去（0x94C4F8 查的支持段 +0x30）。
    public static System.Collections.Generic.List<bool> TerrainNoFallThrough;
    public static System.Collections.Generic.List<int> TerrainForces;
    public static System.Collections.Generic.List<int> TerrainDrag;
    // 长度对不上就当没有这个数据：宁可允许下跳，也不要按错段号误禁。
    public static void SetTerrain(System.Collections.Generic.List<Vector4> t,
                                  System.Collections.Generic.List<bool> forbidFallDown = null,
                                  System.Collections.Generic.List<int> forces = null,
                                  System.Collections.Generic.List<int> drag = null)
    {
        Terrain = t;
        TerrainDrag = t != null && drag != null && drag.Count == t.Count ? drag : null;
        TerrainForces = t != null && forces != null && forces.Count == t.Count ? forces : null;
        TerrainNoFallThrough = t != null && forbidFallDown != null && forbidFallDown.Count == t.Count
            ? forbidFallDown : null;
    }

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
    // 脚本 lockUI/unlockUI（源端 PacketCreator.lockUI，教程/过场用）：计数锁，
    // unlockUI 全清，避免脚本分支漏配对后玩家永久卡住。
    public static int ScriptUiLock;

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

    // Follow the current foothold by interpolation. Continue only across endpoints
    // that agree in both x and y; a disconnected lower platform requires falling.
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
        if (cur < 0 || bestD > EdgeTol) return false;               // 脚下没有段：交给下落

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
            float edgeY = Mathf.Lerp(c.y, c.w, (edge - c.x) / (c.z - c.x));
            float joinY = Mathf.Lerp(f.y, f.w, (edge - f.x) / (f.z - f.x));
            if (!joined || Mathf.Abs(edgeY - joinY) > EdgeTol || toX < lo || toX > hi) continue;
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

    // 原端 WalkSpeed 的 100% 基准（Character.wz 角色 info/walkSpeed = 125px/s）。
    // 怪物 info/speed 是「相对 100% 的百分比偏移」，两边共用这个基准，
    // 所以怪物速度也要用它换算，不能各写一个魔数（见 FieldDirector.ApplyStats）。
    public const float BaseWalkPx = 125f;

    // ————————————— 下跳（↓ + 跳跃穿平台）—————————————
    // 源端 0x94C4F8 成功后 0x9B1C51 在脱离支持时把横向速度清 0、
    // 竖直速度设为 Physics.jumpSpeed × (−0.35355339)，并把刚离开的那条 foothold
    // 记成「忽略」，下落途中不能被它重新接住（0x9B3B63 比较候选段与忽略指针）。
    // 常量 0xB3E3A0 的 double 已核实为 −0.35355339。
    public const float DownJumpRatio = 0.35355339f;
    // 源端读取的地图 info/fieldLimit：CANNOTJUMPDOWN = 0x20000（FieldLimit.java）
    public const int FieldLimitCannotJumpDown = 0x20000;
    // 正在穿透的 foothold 序号（-1 = 没有）。落地/换图/抓绳都要清掉。
    int dropSegment = -1;
    public bool IsDroppingThrough => dropSegment >= 0;

    // 普攻是矩形判定：近战框上沿大约在脚底上方 48px，够不到上层平台。
    // 之前只比 |dx|，站在下层能把上层平台的怪打下来（用户实测截图）。
    public const float MeleeVertical = 1.2f;

    [Header("移动")]
    public float moveSpeed = BaseWalkPx / Art.PPU;
    public float jumpSpeed = 555f / Art.PPU;
    public float gravity   = -2000f / Art.PPU;
    NativeMovementPhysics movementPhysics;
    double horizontalVelocity; // px/s: preserved when leaving a foothold.
    bool flashJumpUsed;
    double soulRushUntil;
    // 96BF52..96BF91, ordinary movement branch (96C1D3=true).
    public bool CanFlashJump => !flashJumpUsed && !grounded && !IsClimbing && vy>=0 &&
        (vis==null || !vis.IsOneShot) && (GameLoop.I?.field?.Map?.fieldLimit & 2 ?? 0)==0;
    // 96BF14: Soul Rush needs a foothold, rejects WEAKEN (+4BC), and has its own 1500ms deadline.
    public bool CanUseFlashJump(int skill) => skill==11101005
        ? !flashJumpUsed && grounded && !IsClimbing && Time.realtimeSinceStartupAsDouble>=soulRushUntil &&
            GetComponent<NativeMobDebuffs>()?.Has(122)!=true && (vis==null || !vis.IsOneShot) &&
            (GameLoop.I?.field?.Map?.fieldLimit & 2 ?? 0)==0
        : CanFlashJump;
    public void ApplyFlashJump(int level,int skill=4111006)
    {
        int tier=(skill==11101005 ? level*2 : level)/4;
        if(skill==11101005)
        {
            grounded=false;
            soulRushUntil=Time.realtimeSinceStartupAsDouble+1.5;
        }
        horizontalVelocity=ContactImpulse(horizontalVelocity,facing*(350+40*tier));
        vy=-(float)ContactImpulse(-vy*Art.PPU,-250-20*tier)/Art.PPU;
        flashJumpUsed=true;
        vis?.Play("jump",false);
    }
    float contactWallX;
    int contactWallDirection;

    [Header("普攻")]
    public float attackInterval = 0.55f;
    public float reach          = 2.3f;
    public float attackDamage   = 11f;

    [Header("状态")]
    public float hpMax = 100f;
    public float hp    = 100f;
    public float invulnUntil;
    // 0x670C99 lea ecx,[esi+0x46C] with esi = the target of a mob attack:
    //   0x670CA1 sub edx,[ecx] / 0x670CA3 cmp edx,0x7D0 / jl <return>
    //   0x670CB2 mov [ecx],eax        ; stamped BEFORE the damage is computed
    // So a mob attack resolves against the same target at most once per 2000 ms,
    // independent of which mob swung. 0 means "never hit yet".
    public long lastMobAttackMs;
    float alertUntil;
    float contactTintStarted;
    int contactTintPhase;
    bool contactTintActive;
    public void BeginNativeAlert() => alertUntil = Time.unscaledTime + 5f;
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
        dropSegment = -1;
        grabCooldown = 0f;
        terrainY = GroundAt(worldX, GroundY);
        jumpY = 0f;
        vy = 0f;
        horizontalVelocity = 0;
        contactWallDirection = 0;
        grounded = true;
        flashJumpUsed = false;
    }

    // 技能系统的施法动画入口。SpriteAnim 挂在 Art.Attach 建的子节点上，
    // GetComponent<SpriteAnim>() 在根节点上找不到——之前技能因此完全没有角色动作。
    public void PlaySkill(string skill,float phaseDuration=0)
    {
        if (vis == null || IsClimbing) return;
        BeginNativeAlert();
        string action = "skill_" + skill;
        if (Art.GetClip(vis.kind, vis.key, action) == null) action = "atk";
        SkillActionDuration = GetSkillActionDuration(skill);
        float playbackSpeed=phaseDuration>0 ? SkillActionDuration/phaseDuration : 1;
        if(phaseDuration>0)SkillActionDuration=phaseDuration;
        vis.PlayOnce(action,playbackSpeed);
    }

    public void FinishHeldSkill() { vis?.Play("stand",false,true); }

    public void PlayNativeSkill(int id) => PlaySkill(id.ToString());

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
        public Server.Field.MapReactor reactor;
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

    // CAvatar::GetBodyRect 0x45183B; ordinary body constants at 0xAF14B8/C8.
    // Equipment, weapon and hair pixel bounds do not enlarge this rectangle.
    public Rect ContactBounds
    {
        get
        {
            float left = Crouching ? (facing > 0 ? 0 : -46) : -22;
            float right = Crouching ? (facing > 0 ? 46 : 0) : 22;
            return Rect.MinMaxRect(FeetPos.x + left / Art.PPU,
                FeetPos.y, FeetPos.x + right / Art.PPU, FeetPos.y + (Crouching ? 31 : 65) / Art.PPU);
        }
    }

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
        movementPhysics = NativeMovementPhysics.Load();
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
    void OnLevelUp(int level)
    {
        ApplyProgression();
        NativeImgAnimation.Spawn("Effect/BasicEff.img/LevelUp", transform, FeetPos, SortingOrder + 1);
    }

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

    bool sitting;
    NativeBotChair chairEffect;
    public int ChairId { get; private set; }
    public bool Sitting
    {
        get => sitting;
        private set
        {
            sitting = value;
            if (!value) { ChairId = 0; if (chairEffect != null) chairEffect.Set(0, vis); }
        }
    }
    public bool UseChair(int itemId)
    {
        if (!grounded || IsClimbing || hp <= 0f || vis == null) return false;
        Sitting = true;
        ChairId = itemId;
        vis.SetBase("sit");
        if (chairEffect == null) chairEffect = gameObject.AddComponent<NativeBotChair>();
        chairEffect.Set(itemId, vis);
        return true;
    }
    public bool Crouching { get; private set; }
    string swingAction = "atk";
    bool NativeBasicAttackActive => Terrain != null && vis != null && vis.IsOneShot &&
        (vis.Current == "atk" || vis.Current == "proneStab");
    public void ToggleSit()
    {
        if (!grounded || IsClimbing || hp <= 0f) return;
        Sitting = !Sitting;
        if (vis != null) vis.SetBase(Sitting ? "sit" : "stand");
    }
    // 原端链路：键位 type6 → CWvsContext::SendEmotion(0xA24470) → 本地 SetEmotion + 封包 0x33。
    // 0xA24470 只要求角色存在；1..22 合法，节流 2 秒（见 SpriteAnim）。
    // 8..22 是现金表情道具（FaceExpressionHandler：emote>7 需要 5159992+emote，即 5160000..5160014），
    // 没有道具时服务端直接丢弃，这里同样不显示。
    public void Express(int emote)
    {
        if (vis == null || hp <= 0f) return;
        if (emote == 0) { vis.ClearExpression(); return; }
        if (emote < 1 || emote > SpriteAnim.MaxEmote) return;
        if (emote > 7 && !OwnsFaceExpression(emote)) return;
        vis.Express(emote);
    }

    static bool OwnsFaceExpression(int emote)
    {
        var inventory = GameSession.Character != null ? GameSession.Character.inventory : null;
        if (inventory == null) return false;
        int itemId = 5159992 + emote;
        foreach (var item in inventory)
            if (item != null && item.itemId == itemId) return true;
        return false;
    }

    void BuildArt()
    {
        // 已登录角色按真实外观合成；旧版离线入口才使用职业配方。
        string artKey = WzCharacter.CurrentAppearanceKey()
            ?? Profile.cls + "_" + HairStyles.KeyOf(HairStyles.IndexOf(Profile.hair));
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

    public void RefreshFieldAppearance()
    {
        if (vis == null) return; // 首次生成由 Start 按已绑定地图构建。
        vis.gameObject.SetActive(false); Destroy(vis.gameObject); vis = null;
        BuildArt();
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
        if (InputLocked || ScriptUiLock > 0) { SyncTransform(); return; }   // 换图黑场/脚本锁UI：不读输入、不跑物理
        RefreshMovementAttributes();
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
        // BeiDou.exe 0094C383: FIELD_LIMIT_JUMP (bit 0) rejects normal jumps.
        var map = GameLoop.I != null && GameLoop.I.field != null ? GameLoop.I.field.Map : null;
        if (map != null && (map.fieldLimit & 1) != 0) jumpReq = false;
        climbInput = TouchInput.MoveV;
        var diseases = GetComponent<NativeMobDebuffs>();
        diseases?.Input(ref h, ref jumpReq, ref climbInput);
        // 0094C3AD checks the active action before submitting a normal jump.
        // Grounded attack -> jump is rejected; jump -> attack remains possible.
        // Do not treat alert/hit reaction animation as an attack action.
        if (NativeBasicAttackActive) {
            jumpReq = false;
            // CMovePath local input 009CBFA2–009CBFF6 clears horizontal input
            // during an action; proneStab keeps the down input set.
            if (grounded) h = 0;
            climbInput = vis.Current == "proneStab" ? -1 : 0;
        }
        if (jumpReq || Mathf.Abs(h) > 0.15f || Mathf.Abs(climbInput) > 0.15f || !grounded) Sitting = false;
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

        Crouching = Terrain != null && grounded && !IsClimbing && !moving && climbInput < -0.15f;
        if (vis != null)
        {
            string action = IsClimbing ? (climb.ladder ? "ladder" : "rope") :
                !grounded ? "jump" : moving ? "walk" : Crouching ? "prone" : Sitting ? "sit" :
                Time.unscaledTime < alertUntil ? "alert" : "stand";
            vis.SetBase(action);
            if (IsClimbing) vis.Play(action, false); // 抓绳打断攻击/受击残留
            if (IsClimbing) vis.speed = Mathf.Abs(climbInput);
            else if (!vis.IsOneShot) vis.speed = 1f;
        }

        // —— 自动普攻 ——
        // Boss 优先：Boss 体积大、通常只有一只，误判成本比杂兵高得多
        atkTimer -= dt;
        if ((diseases == null || !diseases.BlocksAttack) && (Terrain == null || attackRequested) && !IsClimbing && atkTimer <= 0f && (vis == null || !vis.IsOneShot))
        {
            var b = FindBossTarget();
            if (attackRequested && StrikeReactor())
            {
                atkTimer = attackInterval;
            }
            else if (b != null)
            {
                atkTimer = attackInterval;
                if (Terrain == null) facing = b.transform.position.x >= worldX ? 1 : -1;
                StrikeBoss(b);
            }
            else
            {
                var t = FindTarget();
                if (t != null)
                {
                    atkTimer = attackInterval;
                    if (Terrain == null) facing = t.transform.position.x >= worldX ? 1 : -1;
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
    }

    // 起一次挥砍：按原武器攻速逐帧换算 delay、挂残影帧表，返回前摇秒数。
    // 没有素材时返回 0，调用方立即结算。
    float BeginSwing()
    {
        if (!UsesRangedWeapon) GetComponent<NativeBuffs>()?.OnCloseAttack();
        BeginNativeAlert();
        pending.active = false;      // 新挥砍开始，上一段前摇作废（理论上前摇必早于挥砍结束）
        swingSfxAt = -1f;            // 上一段空挥计时作废
        swingPlayback = 1f;
        trailFrames = null;
        trailPose = null;
        trailCursor = 0;
        if (vis == null) return 0f;
        // BeiDou.exe 00950E21–00950E4E: basic attack in prone movement
        // state selects action 32 (proneStab), not the standing weapon swing.
        swingAction = Crouching ? "proneStab" : "atk";
        var clip = Art.GetClip(vis.kind, vis.key, swingAction);
        if (clip == null || clip.delays.Length == 0) return 0f;
        int weaponSpeed = WzCharacter.WeaponAttackSpeed(vis.key);
        weaponSpeed = GetComponent<NativeBuffs>()?.AttackSpeed(weaponSpeed) ?? weaponSpeed;
        swingPlayback = 16f / (Mathf.Clamp(weaponSpeed, 2, 10) + 10);
        vis.PlayNativeAttack(swingAction, weaponSpeed);

        // 残影帧表：按当前武器 + 实际普攻姿势取（swingO1/stabT1/shoot1…）
        trailName = WzCharacter.Afterimage(vis.key);
        if (trailName != null)
        {
            trailPose = swingAction == "proneStab" ? "proneStab" : WzCharacter.AttackPoseName(vis.key);
            var frames = trailPose != null ? AfterimageFx.Frames(trailName, trailPose) : null;
            if (frames != null && frames.Length > 0) trailFrames = frames;
        }
        return WzCharacter.AttackFrameDelay(clip.delays[0], weaponSpeed);
    }

    // 空挥：没有目标也要有完整动作和速度
    bool StrikeReactor()
    {
        if (Terrain==null || vis==null || GameLoop.I?.field?.Map==null) return false;
        var local=AfterimageFx.AttackBounds(WzCharacter.Afterimage(vis.key),
            Crouching ? "proneStab" : WzCharacter.AttackPoseName(vis.key));
        if (!local.HasValue) return false;
        var target=NativeReactorView.FindTarget(GameLoop.I.field.Map.id,
            NativeTargetGeometry.World(local.Value,FeetPos,facing>0));
        if (target==null) return false;
        float windup=BeginSwing();
        pending=new PendingHit { reactor=target,timer=windup,facing=facing,active=true };
        return true;
    }

    void SwingEmpty()
    {
        if (!SpendBasicProjectile()) return;
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
        if (p.reactor != null)
        {
            var r=p.reactor;
            if (GameLoop.I?.field?.Map?.id==r.mapId &&
                ReferenceEquals(Server.Field.MapReactorService.Find(r.mapId,r.objectId),r))
                Server.Field.MapReactorService.HitPlayer(r.mapId,r.objectId,GameSession.CharacterId);
        }
        else if (p.boss != null)
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
        if (trailFrames == null || vis == null || !vis.IsOneShot || vis.Current != swingAction) return;
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
            // ↓ + 跳跃 = 穿平台下跳；条件不满足才退回普通跳跃
            if (climbInput < -0.15f && BeginDownJump()) return;
            int support = SupportSegment(worldX, terrainY);
            double slope = 0, cosine = 1;
            if (support >= 0) {
                var f = Terrain[support];
                double dx = System.Math.Abs(f.z - f.x), dy = -(f.w - f.y) * Mathf.Sign(f.z - f.x);
                double length = System.Math.Sqrt(dx * dx + dy * dy);
                cosine = dx / length; slope = dy / length;
            }
            horizontalVelocity *= cosine;
            int input = h > .15f ? 1 : h < -.15f ? -1 : 0;
            if (input != 0) {
                double limit = moveSpeed * Art.PPU * (1 + System.Math.Sign(input * slope) * slope * slope);
                if (input * horizontalVelocity < .8 * limit) horizontalVelocity += input * .8 * limit;
                if (input * horizontalVelocity > limit) horizontalVelocity = input * limit;
            }
            vy = jumpSpeed;
            grounded = false;
            AudioManager.Play("Game/Jump", 0.7f, false);
        }
        float previousY = terrainY + jumpY;
        double oldVelocity = horizontalVelocity;
        int direction = h > .15f ? 1 : h < -.15f ? -1 : 0;
        float tangentX = 1f;
        if (grounded)
        {
            int support = SupportSegment(worldX, previousY);
            double force = support >= 0 && TerrainForces != null ? TerrainForces[support] * .01 : 0;
            double drag = support >= 0 && TerrainDrag != null && TerrainDrag[support] != 0 ? TerrainDrag[support] * .01 : 1;
            double slope = 0;
            if (support >= 0) {
                var f = Terrain[support];
                float length = new Vector2(f.z - f.x, f.w - f.y).magnitude;
                tangentX = Mathf.Abs(f.z - f.x) / length;
                slope = -(f.w - f.y) * Mathf.Sign(f.z - f.x) / length;
            }
            horizontalVelocity = movementPhysics.Ground(horizontalVelocity, direction, force, drag,
                moveSpeed * Art.PPU / movementPhysics.WalkSpeed, slope, dt);
        }
        else horizontalVelocity = movementPhysics.Air(horizontalVelocity, -vy * Art.PPU, direction, dt);
        float displacement = (float)((oldVelocity + horizontalVelocity) * .5 * dt / Art.PPU) * tangentX;
        // Rush is a separate skill movement; ordinary movement always uses native integration.
        if (Mathf.Abs(h) > 1f) { displacement = h * moveSpeed * dt; horizontalVelocity = 0; }
        float nextX = ClampX(worldX + displacement);
        if (worldX + displacement < XMin || worldX + displacement > XMax) horizontalVelocity = 0;
        moving = Mathf.Abs(h) > 0.15f;
        if (moving && !NativeBasicAttackActive) facing = h > 0f ? 1 : -1;

        if (MoveHorizontal(nextX)) horizontalVelocity *= tangentX;
        if (grounded) return;

        // 帧内细分由调用方控制；用解析位移避免跳高随帧率变化。
        float beforeVy = vy;
        vy = Mathf.Max(vy + gravity * dt, -fallSpeed);
        float nextY = previousY + (beforeVy + vy) * .5f * dt;
        float landing;
        if (vy <= 0f && FindLanding(worldX, previousY, nextY, dropSegment, out landing))
        {
            terrainY = landing;
            int support = SupportSegment(worldX, landing);
            if (support >= 0) {
                var f = Terrain[support];
                double dx = System.Math.Abs(f.z - f.x), dy = -(f.w - f.y) * Mathf.Sign(f.z - f.x);
                double length = System.Math.Sqrt(dx * dx + dy * dy);
                horizontalVelocity = horizontalVelocity * dx / length - vy * Art.PPU * dy / length;
            }
            jumpY = 0f;
            vy = 0f;
            grounded = true;
            flashJumpUsed = false;
            dropSegment = -1;      // 落到别的段上：忽略指针作废（0x9B4963）
        }
        else jumpY = nextY - terrainY;

        // 掉到地图可视范围以下：说明一条 foothold 都没接住（真空缺口，或数据异常）。
        // 原端会由服务端把角色复位回最近地面；这里做等价兜底，避免一路掉到屏幕外卡死。
        if (nextY < FallFloor) RecoverFromFall();
    }

    // Walking and contact displacement must use the same wall/support resolution.
    bool MoveHorizontal(float nextX)
    {
        // 连续支持段优先：上层平台的竖直侧边可能穿过下层可走地面。
        // 只有脚下不能连续通过（真实台阶/边缘）或处于空中时，才检查竖直墙。
        bool wasGrounded = grounded;
        float previousY = terrainY + jumpY;
        float supportY = previousY;
        int direction = System.Math.Sign(nextX - worldX);
        int blockedDirection = direction == 0 ? contactWallDirection : 0;
        float blockedX = direction == 0 ? contactWallX : 0;
        bool followsGround = grounded && FollowGround(worldX, previousY, nextX, out supportY);
        if (Terrain != null)
            foreach (var wall in Terrain)
            {
                if (Mathf.Abs(wall.z - wall.x) >= 0.01f) continue;
                if ((worldX < wall.x && nextX >= wall.x) || (worldX > wall.x && nextX <= wall.x) ||
                    (worldX == wall.x && direction != 0 &&
                     (contactWallDirection == 0 || contactWallX != wall.x || direction == contactWallDirection)))
                {
                    // 目标恰在墙线上时，也必须确认墙另一侧有接续地面。
                    float beyondY;
                    if (followsGround && FollowGround(worldX, previousY,
                        wall.x + Mathf.Sign(nextX - worldX) * 0.001f, out beyondY)) continue;
                    if (previousY < Mathf.Max(wall.y, wall.w) - 0.01f &&
                        previousY >= Mathf.Min(wall.y, wall.w) - 0.01f)
                    {
                        // Stay on the wall plane so its lower endpoint can support landing.
                        nextX = wall.x;
                        blockedX = wall.x;
                        blockedDirection = direction;
                        horizontalVelocity = 0;
                        followsGround = false;
                    }
                }
            }
        contactWallX = blockedX;
        contactWallDirection = blockedDirection;
        if (grounded)
        {
            // Only follow the supporting segment and geometrically connected slopes.
            // An unrelated platform at the same x must not keep us suspended.
            if (followsGround || FollowGround(worldX, previousY, nextX, out supportY))
            {
                terrainY = supportY;
                jumpY = 0f;
            }
            else
            {
                grounded = false;
                vy = 0f;
            }
        }
        worldX = nextX;
        return wasGrounded && !grounded;
    }

    void RecoverFromFall()
    {
        float atX;
        float y = NearestGround(worldX, out atX);
        worldX = ClampX(atX);
        terrainY = y;
        jumpY = 0f;
        vy = 0f;
        horizontalVelocity = 0;
        contactWallDirection = 0;
        grounded = true;
        flashJumpUsed = false;
        climb = null;
        dropSegment = -1;
        grabCooldown = 0.3f;
        Debug.Log("[Player] 掉出地图，复位到 x=" + atX.ToString("F2") + " y=" + y.ToString("F2"));
    }

    bool StepClimb(float h, bool jumpReq, float dt)
    {
        float y = terrainY + jumpY;
        if (climb == null && grabCooldown <= 0f && Mathf.Abs(climbInput) > 0.15f && Climbs != null)
        {
            // Only grab a rope already reached by the feet. Never lift a passer-by
            // across the empty space below its bottom endpoint.
            float distance = 10f / Art.PPU;
            float topSlack = 8f / Art.PPU;
            foreach (var line in Climbs)
            {
                float dx = Mathf.Abs(worldX - line.x);
                if (dx > distance) continue;
                if (y < line.bottom || y > line.top + topSlack) continue;
                // 站在平台顶端正上方按上 = 想往上走，不是想爬；只有按上时忽略顶端这条线。
                if (grounded && climbInput > 0f && y >= line.top - topSlack) continue;
                distance = dx;
                climb = line;
            }
            if (climb != null)
            {
                flashJumpUsed=false;
                worldX = climb.x;
                horizontalVelocity = 0;
                grounded = false;
                vy = 0f;
                dropSegment = -1;      // 抓住绳梯 = 离开原来那条平台，忽略指针作废
                if (y > climb.top) jumpY = climb.top - terrainY;
                y = terrainY + jumpY;
            }
        }
        if (climb == null) return false;
        // 0x9B1D74: a rope/ladder jump requires horizontal input.
        // Ordinary branch: half vertical jump speed, 1.3x horizontal walk speed.
        if (jumpReq && Mathf.Abs(h) > 0.15f)
        {
            climb = null;
            grabCooldown = 0.25f;
            vy = jumpSpeed * .5f;
            facing = h > 0f ? 1 : -1;
            horizontalVelocity = facing * moveSpeed * Art.PPU * 1.3;
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

    static bool FindLanding(float x, float previousY, float nextY, int skip, out float landing)
    {
        landing = float.NegativeInfinity;
        if (Terrain == null || Terrain.Count == 0)
        {
            landing = GroundY;
            return previousY >= GroundY && nextY <= GroundY;
        }
        for (int i = 0; i < Terrain.Count; i++)
        {
            // 下跳正在穿过的那条段不算落脚点，否则一松手就被自己踩的台子接回去
            if (i == skip) continue;
            var f = Terrain[i];
            if (x < Mathf.Min(f.x, f.z) || x > Mathf.Max(f.x, f.z) || Mathf.Abs(f.z - f.x) < 0.01f) continue;
            float height = Mathf.Lerp(f.y, f.w, (x - f.x) / (f.z - f.x));
            if (previousY >= height - 0.01f && nextY <= height && height > landing)
                landing = height;
        }
        return !float.IsNegativeInfinity(landing);
    }

    // 当前脚下踩的是哪条 foothold（序号）；没有可走段返回 -1。
    // 选段规则与 FollowGround 一致（覆盖 x 且高度最接近 refY，容差 1.5px）。
    static int SupportSegment(float x, float y)
    {
        if (Terrain == null || Terrain.Count == 0) return -1;
        int best = -1;
        float bestD = 1.5f / Art.PPU;
        for (int i = 0; i < Terrain.Count; i++)
        {
            var f = Terrain[i];
            if (Mathf.Abs(f.z - f.x) < 0.01f) continue;
            if (x < Mathf.Min(f.x, f.z) || x > Mathf.Max(f.x, f.z)) continue;
            float d = Mathf.Abs(Mathf.Lerp(f.y, f.w, (x - f.x) / (f.z - f.x)) - y);
            if (d <= bestD) { bestD = d; best = i; }
        }
        return best;
    }

    // 当前段正下方（严格更低）还有没有接得住的落脚点。
    // 源端 0x94C4F8 也是先看下方落点再置下跳请求，悬空边不该把人送进虚空。
    static bool HasLandingBelow(float x, float y, int skip)
    {
        if (Terrain == null || Terrain.Count == 0) return y > GroundY + 0.01f;
        for (int i = 0; i < Terrain.Count; i++)
        {
            if (i == skip) continue;
            var f = Terrain[i];
            if (Mathf.Abs(f.z - f.x) < 0.01f) continue;
            if (x < Mathf.Min(f.x, f.z) || x > Mathf.Max(f.x, f.z)) continue;
            if (Mathf.Lerp(f.y, f.w, (x - f.x) / (f.z - f.x)) < y - 0.01f) return true;
        }
        return false;
    }

    // ↓ + 跳跃：穿过脚下这条 foothold 往下掉。
    // 门控沿用源端：地图 fieldLimit 的 CANNOTJUMPDOWN、支持段的 forbidFallDown、
    // 必须站在可走段上、且下方要有落脚点。
    bool BeginDownJump()
    {
        var map = GameLoop.I != null && GameLoop.I.field != null ? GameLoop.I.field.Map : null;
        if (map != null && (map.fieldLimit & FieldLimitCannotJumpDown) != 0) return false;
        float y = terrainY + jumpY;
        int segment = SupportSegment(worldX, y);
        if (segment < 0 || !HasLandingBelow(worldX, y, segment)) return false;
        if (TerrainNoFallThrough != null && TerrainNoFallThrough[segment]) return false;
        dropSegment = segment;
        grounded = false;
        horizontalVelocity = 0;
        vy = -jumpSpeed * DownJumpRatio;   // 脱离支持时横向速度清 0：本帧不再推进 worldX
        jumpY = 0f;
        AudioManager.Play("Game/Jump", 0.7f, false);
        return true;
    }

    BossUnit FindBossTarget()
    {
        foreach (var b in FindObjectsOfType<BossUnit>())
        {
            if (b == null || b.dead) continue;
            if (Mathf.Abs(b.Lane - lane) > 2) continue;      // Boss 占 3 层
            float dx = b.transform.position.x - worldX;
            if (Terrain != null && dx * facing < 0) continue;
            // Boss 本体宽，判定给一点宽限，否则贴脸时反而打不到
            if (dx < -(reach + 1.3f) || dx > reach + 1.3f) continue;
            return b;
        }
        return null;
    }

    void StrikeBoss(BossUnit b)
    {
        if (!SpendBasicProjectile()) return;
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

            float dx = e.transform.position.x - worldX;
            if (Terrain != null && dx * facing < 0) continue;
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
    public void RequestAttack() { Sitting = false; attackRequested = true; }

    void Strike(EnemyUnit e)
    {
        if (!SpendBasicProjectile()) return;
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

    bool SpendBasicProjectile()
    {
        if (!UsesRangedWeapon) return true;
        if (NativeProjectiles.Spend(NativeProjectiles.For(this,0,1))) return true;
        GetComponent<SkillSystem>()?.Report("缺少可用弹药");
        return false;
    }

    // 前摇到点：真正结算。原版近战的视觉就是角色挥砍帧本身（武器合成在帧里），
    // 没有额外的弧光贴片；伤害/飘字都在这一下出去。
    void HitEnemy(EnemyUnit e, DamageResult hit, int dir)
    {
        if (e!=null && !e.dead && UsesRangedWeapon) GetComponent<NativeBuffs>()?.OnRangedAttack(1);
        if(e!=null&&!e.dead&&(weapon.kind==WeaponKind.bow||weapon.kind==WeaponKind.crossbow))
            GetComponent<SkillSystem>()?.NativeBasicRangedHit(e,hit,dir);
        if (e == null || e.dead) return;
        e.TakeHit(hit, dir);
    }

    void HitBoss(BossUnit b, DamageResult hit)
    {
        if (b!=null && !b.dead && UsesRangedWeapon) GetComponent<NativeBuffs>()?.OnRangedAttack(1);
        if(b!=null&&!b.dead&&(weapon.kind==WeaponKind.bow||weapon.kind==WeaponKind.crossbow))
            GetComponent<SkillSystem>()?.NativeBasicRangedHit(b,hit,facing);
        if (b == null || b.dead) return;
        b.TakeHit(hit, facing);
    }

    public void TakeDamage(float dmg, int dir, bool magic = false, int attackerLevel = 1, EnemyUnit attacker = null, int damageFrom = -1, int mpAttack = 0)
    {
        if (hp <= 0f || Time.unscaledTime < invulnUntil) return;
        RefreshAttributes();
        dmg = CombatRules.Incoming(dmg, Attributes, magic, attackerLevel, Random.value,
            GetComponent<NativeBuffs>()?.Value("INVINCIBLE") ?? 0).Amount;
        if (dmg <= 0)
        {
            // Native -1500 timer approaches zero by +30 each tick; it blocks
            // another contact, but does not set hit face/alert or apply an impulse.
            invulnUntil = Time.unscaledTime + 1.5f;
            DamageText.Miss(DamageAnchor, 1f, "violet");
            return;
        }
        var buffs=GetComponent<NativeBuffs>();
        if (attacker!=null && buffs!=null)
        {
            long maximum=attacker.NativeController?.MaximumHp ?? (long)attacker.hpMax;
            if (damageFrom==-1 && buffs.HasStat("POWERGUARD"))
            {
                int reflected=NativeBuffs.PowerGuardDamage((int)dmg,buffs.Value("POWERGUARD"),maximum,attacker.IsBoss);
                dmg-=reflected;
                if (reflected>0) attacker.TakeReplayDot(reflected);
            }
            else if (damageFrom>=0 && !attacker.IsBoss && buffs.HasStat("MANA_REFLECTION"))
            {
                int source=Profile.JobId*10000+1002,level=SkillProgress.LevelOf(source);
                if ((Profile.JobId==212 || Profile.JobId==222 || Profile.JobId==232) && level>0 && buffs.SourceOf("MANA_REFLECTION")==source)
                {
                    var row=SkillBook.Get(source)?.Level(level);
                    if (Random.value<Art.Int(row,"prop",100)/100f)
                        attacker.TakeReplayDot((int)System.Math.Min(maximum/5,(int)dmg*(long)Art.Int(row,"x")/100));
                }
            }
        }
        int achillesId=Profile.JobId<200 && Profile.JobId%10==2 ? Profile.JobId*10000+(Profile.JobId==112 ? 4 : 5) : 0;
        int achillesLevel=achillesId==0 ? 0 : SkillProgress.LevelOf(achillesId);
        int defenseLevel=SkillProgress.LevelOf(21120004);
        dmg=NativeBuffs.PassiveIncoming((int)dmg,attacker!=null ? buffs?.Value("COMBO_BARRIER") ?? 0 : 0,
            damageFrom!=-3 && damageFrom!=-4 && achillesLevel>0 ? Art.Int(SkillBook.Get(achillesId)?.Level(achillesLevel),"x") : 0,
            damageFrom!=-3 && damageFrom!=-4 && defenseLevel>0 ? Art.Int(SkillBook.Get(21120004)?.Level(defenseLevel),"x") : 0);
        if (buffs != null)
        {
            bool magicGuard=buffs.HasStat("MAGIC_GUARD"), mesoGuard=buffs.HasStat("MESOGUARD");
            NativeBuffs.GuardLoss((int)dmg,(int)mp,Profile.gold,buffs.Value("MAGIC_GUARD"),buffs.Value("MESOGUARD"),
                magicGuard,mesoGuard,mpAttack,
                out int hpLoss,out int mpLoss,out int mesoLoss,out bool cancelMeso);
            hp=Mathf.Max(0f,hp-hpLoss); mp=Mathf.Max(0f,mp-mpLoss);
            if (mesoLoss>0) Profile.Spend(mesoLoss);
            if (cancelMeso) buffs.CancelStat("MESOGUARD");
            // Native damage packet retains incoming damage for Magic Guard, halves it for Meso Guard.
            if (!magicGuard && mesoGuard) dmg=hpLoss;
        }
        else { hp = Mathf.Max(0f, hp - dmg); mp=Mathf.Max(0f,mp-mpAttack); }
        TickContactTint();
        contactTintStarted = Time.unscaledTime;
        contactTintActive = true;
        invulnUntil = Time.unscaledTime + 1.5f;
        BeginNativeAlert();
        // Ordinary contact: 0x958686/0x958C94 -> 0x7A6353 -> 0x9B222A.
        // Apply signed 270px/s impulses; native air/terrain integration determines distance.
        bool resistKnockback=NativeBuffs.ResistsKnockback(buffs?.Value("STANCE") ?? 0,
            GameLoop.I?.field?.Map?.fieldLimit ?? 0,Random.Range(0,100));
        if (!resistKnockback)
        {
            if (IsClimbing) { climb = null; grabCooldown = 0.25f; }
            grounded = false;
            horizontalVelocity = ContactImpulse(horizontalVelocity, System.Math.Sign(dir) * 270);
            vy = -(float)ContactImpulse(-vy * Art.PPU, -270) / Art.PPU;
        }
        vis?.ShowHitExpression();
        if (vis != null && hp <= 0f) vis.PlayOnce("die");
        DamageText.Show(DamageAnchor, dmg, "blue");
        if (hp <= 0f && GameLoop.I != null) GameLoop.I.OnPlayerDead();
    }

    // 0x9B227F..0x9B239B preserves an already faster velocity in the impulse direction.
    internal static double ContactImpulse(double velocity, double impulse)
    {
        if (impulse < 0 && velocity > impulse) velocity = System.Math.Max(impulse, velocity + impulse);
        else if (impulse > 0 && velocity < impulse) velocity = System.Math.Min(impulse, velocity + impulse);
        return System.Math.Truncate(velocity); // 0x9B239B..0x9B23DD, _ftol2.
    }

    // 0x92EC50 subtracts avatar layer height, without a hand-tuned scale/offset.
    public Vector3 DamageAnchor => new Vector3(FeetPos.x,
        FeetPos.y + (sr != null && sr.sprite != null ? sr.bounds.size.y : BodyHeight), 0);

    // Boss percent/fixed damage is authoritative: no defense roll or contact i-frame.
    public void TakeBossDamage(float value)
    {
        if (hp <= 0 || value <= 0) return;
        hp = Mathf.Max(0,hp-value);
        DamageText.Show(DamageAnchor,value,"blue");
        if (hp <= 0 && GameLoop.I != null) GameLoop.I.OnPlayerDead();
    }
    public void DispelNativeBuffs()
    {
        GetComponent<NativeBuffs>()?.Dispel();
        attackBonus = 0; attackBonusUntil = 0; partyGrindDefense=0;
        GetComponent<SkillSystem>()?.CancelActions();
    }
    public void Heal(float v, bool skill = false)
    {
        v = GetComponent<NativeMobDebuffs>()?.Healing(v,skill) ?? v;
        if (v < 0) TakeBossDamage(-v);
        else hp = Mathf.Min(hpMax,hp+v);
    }

    // 死亡确认后由服务端下发的血量复活（源端 Character.respawn）：
    // 不是满血 —— 常规 50，护符 30%，特殊图满血。
    public void Revive(int hpValue, int mpValue)
    {
        Sitting = false;
        GetComponent<NativeMobDebuffs>()?.Clear();
        CancelRush();
        climb = null;
        dropSegment = -1;
        grabCooldown = 0f;
        hp = Mathf.Clamp(hpValue, 1, hpMax);
        mp = Mathf.Clamp(mpValue, 0, mpMax);
        pending.active = false;
        GetComponent<NativeBuffs>()?.Clear();
        attackBonus = 0f; attackBonusUntil = 0f;
        invulnUntil = Time.unscaledTime + 1.5f;
        GetComponent<SkillSystem>()?.CancelActions();
        if (vis != null) { vis.SetBase("stand"); vis.Play("stand", true); }
        Fx.Flash(sr, new Color(1f, 1f, 1f));
    }

    // 死亡姿势：源端动作名是 dead（躺在地上），不是 die（倒地过程）
    public void PlayDeadPose()
    {
        Sitting = false;
        if (vis == null) return;
        var action = Art.GetClip(vis.kind, vis.key, "dead") != null ? "dead" : "die";
        vis.SetBase(action);
        vis.Play(action, false);
    }

    // 92FD5A: origin (x,y-20), rotating child (-10,0), 2000ms;
    // avatar alpha moves from 0 to 255 over 2500ms.
    public void PresentDeath(float seconds)
    {
        if (vis == null) return;
        float angle = seconds * Mathf.PI;
        // Shape2D rounds the rotated child to integer pixels (ties away from zero).
        float x = -10f * Mathf.Cos(angle), y = 10f * Mathf.Sin(angle);
        x = Mathf.Sign(x) * Mathf.Floor(Mathf.Abs(x) + .5f);
        y = Mathf.Sign(y) * Mathf.Floor(Mathf.Abs(y) + .5f);
        vis.transform.localPosition = new Vector3(x, 20f + y, 0f) / Art.PPU;
        var color = vis.Renderer.color;
        color.a = Mathf.Clamp01(seconds / 2.5f);
        vis.Renderer.color = color;
    }

    public void ClearDeathPresentation()
    {
        if (vis == null) return;
        vis.transform.localPosition = Vector3.zero;
        vis.Renderer.color = Color.white;
    }

    public void AddAttackBonus(float amount, float seconds)
    {
        attackBonus = amount;
        attackBonusUntil = Time.unscaledTime + seconds;
    }

    int partyGrindDefense;
    public void SetPartyGrindEffect(bool active)
    {
        partyGrindDefense=active ? WzAssets.Node("Item/Consume/0243.img/02431159/spec").Int("pdd") : 0;
    }
    void RefreshAttributes()
    {
        baseAttributes = Profile.Attributes;
        var buffs = GetComponent<NativeBuffs>();
        int attack = buffs != null ? buffs.Value("WATK") : 0;
        Attributes = baseAttributes.WithWeapon(weapon, attack + (Time.unscaledTime < attackBonusUntil ? Mathf.RoundToInt(attackBonus) : 0),
            out weaponAttack, out magicAttack);
        Attributes.weaponDefense+=partyGrindDefense;
        if (buffs != null)
        {
            Attributes.weaponDefense += buffs.Value("WDEF");
            Attributes.magicDefense += buffs.Value("MDEF");
            Attributes.accuracy += buffs.Value("ACC");
            Attributes.avoidability += buffs.Value("AVOID");
            magicAttack += buffs.Value("MATK");
            int warrior=buffs.Value("MAPLE_WARRIOR");
            Attributes.str += baseAttributes.str*warrior/100;
            Attributes.dex += baseAttributes.dex*warrior/100;
            Attributes.intelligence += baseAttributes.intelligence*warrior/100;
            Attributes.luk += baseAttributes.luk*warrior/100;
            int hpBuff=buffs.Value("HYPERBODYHP"),mpBuff=buffs.Value("HYPERBODYMP");
            if (hpBuff!=0) Attributes.maxHp=(int)System.Math.Max(50,System.Math.Min(50000,Attributes.maxHp+(long)Attributes.maxHp*hpBuff/100));
            if (mpBuff!=0) Attributes.maxMp=(int)System.Math.Max(5,System.Math.Min(50000,Attributes.maxMp+(long)Attributes.maxMp*mpBuff/100));
        }
    }
    public void RefreshBuffAttributes()
    {
        float previousHpMax=hpMax,previousMpMax=mpMax;
        RefreshAttributes();
        hpMax=Attributes.maxHp; mpMax=Attributes.maxMp;
        if (hpMax<previousHpMax) hp=Mathf.Min(hp,hpMax);
        if (mpMax<previousMpMax) mp=Mathf.Min(mp,mpMax);
        RefreshMovementAttributes();
    }
    // BeiDou 0x78074A / 0x8C457C / 0x94D8F1: equipment base first,
    // then temporary stats, then ordinary walk/jump limits; slow overrides walk.
    public static int MovementPercent(int equipment,int passive,int temporary,int maximum,int forced=0)
    {
        int basis=System.Math.Max(100,System.Math.Min(maximum,100+equipment+passive));
        return System.Math.Max(80,System.Math.Min(maximum,forced!=0 ? forced : basis+temporary));
    }
    static int MovementSetBuff(System.Collections.Generic.Dictionary<string,int> set,string key,int equipment,int buff)
    {
        if (set==null || (!set.ContainsKey(key) && !set.ContainsKey(key+"Pct"))) return buff;
        return System.Math.Max(short.MinValue,System.Math.Min(short.MaxValue,AdminSetItem.ApplyStat(set,key,equipment+buff)-equipment));
    }
    public void RefreshMovementAttributes()
    {
        if (Attributes==null || movementPhysics==null) return;
        var buffs=GetComponent<NativeBuffs>();
        int speed=MovementSetBuff(Attributes.setItemBonus,"SPD",Attributes.equipmentSpeed,buffs?.Value("SPEED") ?? 0);
        int jump=MovementSetBuff(Attributes.setItemBonus,"JMP",Attributes.equipmentJump,buffs?.Value("JUMP") ?? 0);
        int job=Profile.JobId;
        int thrust=job==311 || job==312 ? 3110000 : job==321 || job==322 ? 3210000 : job>=1310 && job<=1312 ? 13100004 : 0;
        int level=thrust==0 ? 0 : SkillProgress.LevelOf(thrust);
        int passive=level>0 ? Art.Int(SkillBook.Get(thrust)?.Level(level),"speed") : 0;
        moveSpeed=(float)(movementPhysics.WalkSpeed/Art.PPU)*MovementPercent(Attributes.equipmentSpeed,passive,speed,140,GetComponent<NativeMobDebuffs>()?.ForcedWalkSpeed ?? 0)/100;
        jumpSpeed=(float)(movementPhysics.JumpSpeed/Art.PPU)*MovementPercent(Attributes.equipmentJump,0,jump,123)/100;
    }
    public DamageResult RollAttack(System.Collections.Generic.Dictionary<string, object> target, NativeSkillDefinition skill = null)
    {
        RefreshAttributes();
        return CombatRules.Attack(Attributes, weapon, weaponAttack, magicAttack, target, skill,
            Random.value, Random.value, Random.value);
    }
    // Original handlers only synthesize a template when the initial attack captured
    // no damage (ranged also falls back for all-zero captures). No target defense roll.
    public DamageResult[] ReplayFallback(NativeSkillDefinition skill, int count, bool close)
    {
        RefreshAttributes();
        long maximum;
        if (skill.Magic)
        {
            long value = (long)(System.Math.Ceiling((magicAttack * System.Math.Ceiling(magicAttack / 1000.0) + magicAttack) / 30.0)
                + System.Math.Ceiling(Attributes.intelligence / 200.0));
            int amplification = SkillProgress.LevelOf(12110001);
            var row = amplification > 0 ? SkillBook.Get(12110001)?.Level(amplification) : null;
            if (row != null) value = value * (int)Art.Num(row,"y") / 100;
            maximum = (long)(value * skill.MagicAttack);
        }
        else
        {
            int job = Profile.JobId;
            bool dex = weapon.kind == WeaponKind.bow || weapon.kind == WeaponKind.crossbow || weapon.kind == WeaponKind.gun;
            bool luk = weapon.kind == WeaponKind.claw || (weapon.kind == WeaponKind.dagger && job/100==4);
            int main = luk ? Attributes.luk : dex ? Attributes.dex : Attributes.str;
            int secondary = luk ? Attributes.dex+Attributes.str : dex ? Attributes.str : Attributes.dex;
            int basis;
            if (!string.IsNullOrEmpty(Profile.equipWeapon)) basis = CombatRules.MaxPhysicalDamage(main,secondary,Mathf.Max(14,weaponAttack),weapon.multiplier);
            else if(job/100==5 || job/100==15)
                basis = (int)System.Math.Ceiling((Attributes.str*(job%100==0?3:4.2)+Attributes.dex)*System.Math.Min(System.Math.Floor((2.0*Attributes.level+31)/3),31)/100);
            else basis = 1;
            maximum = (long)System.Math.Floor(basis * (double)skill.DamagePercent / 100 + .5);
        }
        maximum = System.Math.Max(1,System.Math.Min(int.MaxValue,maximum));
        int mastery = Profile.JobId/100==11 ? System.Math.Min(10,(SkillProgress.LevelOf(11100000)+1)/2) : 0;
        long minimum = System.Math.Max(1,(long)System.Math.Floor(maximum*(10+mastery*5)/100.0+.5));
        var values = new DamageResult[count];
        for(int i=0;i<count;i++) values[i]=new DamageResult((int)(close ? System.Math.Min(maximum,minimum+System.Math.Floor(Random.value*(maximum-minimum+1))) : maximum),false);
        return values;
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
        Sitting = false;
        CancelRush();
        climb = null;
        dropSegment = -1;
        grabCooldown = 0f;
        worldX = ClampX(x);
        terrainY = groundY;
        jumpY = 0f;
        vy = 0f;
        horizontalVelocity = 0;
        contactWallDirection = 0;
        grounded = true;
        flashJumpUsed = false;
        laneVis = lane;
        SyncTransform();
    }

    public void ResetCombatState()
    {
        alertUntil = 0;
        contactTintActive = false;
        if (sr != null) sr.color = Color.white;
        hp = hpMax; mp = mpMax;
        CancelRush();
        GetComponent<NativeBuffs>()?.Clear();
        attackBonus = 0; attackBonusUntil = 0; partyGrindDefense=0;
        pending.active = false;
        GetComponent<SkillSystem>()?.CancelActions();
    }

    void SyncTransform()
    {
        transform.position = new Vector3(worldX, terrainY + jumpY + DepthOffset, 0f);
        if (sr == null) return;
        TickContactTint();
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

    // 0x93257A: positive immunity alternates FF808080/FFFFFFFF for two
    // 30ms ticks each. Negative (Miss) immunity does not tint the avatar.
    void TickContactTint()
    {
        if (!contactTintActive) return;
        int ticks = Mathf.Clamp(Mathf.FloorToInt((Time.unscaledTime - contactTintStarted) / .03f), 0, 50);
        if (Time.unscaledTime >= contactTintStarted + 1.5f)
        {
            contactTintPhase = (contactTintPhase + ticks) & 3;
            contactTintActive = false;
            if (sr != null) sr.color = Color.white;
            return;
        }
        if (sr != null) sr.color = hp > 0 && ((contactTintPhase + ticks + 1) & 3) < 2
            ? (Color)new Color32(128, 128, 128, 255) : Color.white;
    }
}
