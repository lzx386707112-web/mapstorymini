using UnityEngine;

// 主角：左右移动 + 跳跃 + 5 条深度层 + 自动普攻。
// 刻意不用 Rigidbody2D：横版手感要精确，物理引擎的穿透修正和摩擦会让落地发飘。
//
// 坐标约定（很重要，后面所有单位都按这个来）：
//   worldX  —— 水平位置
//   jumpY   —— 离地高度，>= 0
//   laneVis —— 深度层（0 最远 / 4 最近），插值显示
//   脚底最终 y = GroundY + jumpY + DepthOffset
public class PlayerCtrl : MonoBehaviour
{
    public const float GroundY  = -1.4f;
    public const int   Lanes    = 5;
    public const float LaneStep = 0.22f;

    // 基础属性（武器在此之上加成）。改数值只动这里，不要动面板上的默认字段。
    const float BaseAtk = 11f, BaseInterval = 0.55f, BaseReach = 2.3f;

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

    // x 处的地面高度：取覆盖 x 的段里与 refY 最接近的一条（段内线性插值，支持斜坡）。
    // 原版角色就是这样贴着 foothold 走的——上台阶、下斜坡、走到尽头被段边界挡住。
    public static float GroundAt(float x, float refY)
    {
        if (Terrain == null || Terrain.Count == 0) return GroundY;
        float best = refY, bestD = float.MaxValue;
        for (int i = 0; i < Terrain.Count; i++)
        {
            var f = Terrain[i];
            float x1 = f.x, y1 = f.y, x2 = f.z, y2 = f.w;
            if (Mathf.Abs(x2 - x1) < 0.01f) continue;      // 竖直段不可走
            float lo = Mathf.Min(x1, x2), hi = Mathf.Max(x1, x2);
            if (x < lo || x > hi) continue;
            float y = Mathf.Lerp(y1, y2, (x - x1) / (x2 - x1));
            float d = Mathf.Abs(y - refY);
            if (d < bestD) { bestD = d; best = y; }
        }
        return best;
    }

    [Header("移动")]
    public float moveSpeed = 125f / Art.PPU;
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

    public int   lane     = 2;
    public int   facing   = 1;
    public bool  grounded = true;

    // 攻击增益（Buff 技能触发）：到期自动失效
    public float atkBuffMul = 1f;
    float atkBuffUntil;

    [HideInInspector] public float worldX;
    [HideInInspector] public float jumpY;
    [HideInInspector] public float terrainY = GroundY;   // 脚下地形 y（跟随 foothold 起伏）

    // 切关 / 重生时调用：把脚下地形吸附到 x 处的 foothold 上
    public void ResetTerrain()
    {
        terrainY = GroundAt(worldX, GroundY);
        jumpY = 0f;
        vy = 0f;
        grounded = true;
    }

    // 技能系统的施法动画入口。SpriteAnim 挂在 Art.Attach 建的子节点上，
    // GetComponent<SpriteAnim>() 在根节点上找不到——之前技能因此完全没有角色动作。
    public void PlaySkill(string skill)
    {
        if (vis == null) return;
        string action = "skill_" + skill;
        vis.PlayOnce(Art.GetClip(vis.kind, vis.key, action) != null ? action : "atk");
    }

    float laneVis;
    float vy;
    float fallSpeed = 670f / Art.PPU;
    float atkTimer;
    float laneCooldown;
    SpriteRenderer sr;
    SpriteAnim vis;          // 真素材动画（有素材时接管显示）
    bool moving;

    // 装扮覆盖层（服饰）。武器不再单独叠层：
    // 原版客户端的武器是 CharacterRenderer 按 zmap 合成进角色帧的，
    // 程序化画的武器贴在身上只会变成「盖在脸上的静态色块」（武器永远不动，攻击像没动作）。
    SpriteRenderer coatSr;
    Sprite dyedFrame;        // 已经染过色的角色帧（动画换帧才重染）

    public float DepthOffset    => Terrain != null ? 0f : (laneVis - (Lanes - 1) * 0.5f) * LaneStep;
    public int   SortingOrder   => 300 - lane * 12;
    public Vector2 FeetPos      => new Vector2(worldX, terrainY + jumpY + DepthOffset);

    void Awake()
    {
        Profile.Load();
        var physics = Resources.Load<TextAsset>("Physics");
        if (physics != null)
        {
            var values = MiniJson.Parse(physics.text) as System.Collections.Generic.Dictionary<string, object>;
            moveSpeed = Art.Num(values, "walkSpeed", 125f) / Art.PPU;
            jumpSpeed = Art.Num(values, "jumpSpeed", 555f) / Art.PPU;
            gravity = -Art.Num(values, "gravityAcc", 2000f) / Art.PPU;
            fallSpeed = Art.Num(values, "fallSpeed", 670f) / Art.PPU;
        }
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

        // 兜底方块：形状放在子节点上，根节点保持 scale=1
        var go = new GameObject("Body");
        go.transform.SetParent(transform, false);
        go.transform.localScale = new Vector3(0.82f, 1.5f, 1f);
        var r = go.AddComponent<SpriteRenderer>();
        r.sprite = SpriteFactory.Bottom();
        r.color = new Color(0.95f, 0.72f, 0.35f);
        r.sortingOrder = 300;
        sr = r;
    }

    // 武器属性 + 装扮覆盖层：每次（重新）进关都会按当前 Profile 重建
    void ApplyLoadout()
    {
        var w = WeaponDefs.Get(Profile.equipWeapon);
        attackDamage   = (BaseAtk + w.dmgAdd) * w.dmgMul;
        attackInterval = BaseInterval * w.intervalMul;
        reach          = BaseReach + w.reachAdd;
        BuildCosmetics();
    }

    // —— 装扮覆盖层 ——
    // 头发/服饰不是几何色块，而是「把角色原画的那片像素染色」得到的新贴图，
    // 和角色帧同尺寸同轴心，挂在素材节点下 → 翻转、缩放、换帧全部自动跟随。
    void BuildCosmetics()
    {
        if (vis == null) return;
        var host = vis.transform;

        if (coatSr == null)
        {
            var gc = new GameObject("CoatDye");
            gc.transform.SetParent(host, false);
            coatSr = gc.AddComponent<SpriteRenderer>();
            coatSr.sortingOrder = 301;
        }

        dyedFrame = null;
        ApplyDye();
    }

    // 只在动画换帧时重染（换帧才需要新贴图，缓存命中后几乎零开销）
    void ApplyDye()
    {
        if (coatSr == null) return;
        var src = sr != null ? sr.sprite : null;
        if (src == null) return;
        if (dyedFrame == src) return;
        dyedFrame = src;

        // 服饰：把角色原画的胸腹段保明暗换色（跳过皮肤），叠在角色之上
        var cs = CosmeticFactory.DyeCoat(src, CosmeticFactory.OutfitColor(Profile.outfit));
        if (coatSr.sprite != cs) coatSr.sprite = cs;
        coatSr.enabled = cs != null && Profile.outfit != "none";
        coatSr.flipX = sr.flipX;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt > 0f) Step(dt);   // 顿帧期间 dt==0，逻辑自动跳过
        SyncTransform();
    }

    void Step(float dt)
    {
        // —— 横向 ——
        // 键盘优先（编辑器调试），没按键时读摇杆。两者共用同一个 h，后面的逻辑完全一致
        float h = Input.GetAxisRaw("Horizontal");
        if (Mathf.Abs(h) < 0.15f) h = TouchInput.MoveH;
        bool jumpReq = Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.JoystickButton0);
        if (TouchInput.TakeJump()) jumpReq = true;
        int steps = Mathf.Max(1, Mathf.CeilToInt(dt / 0.008f));
        for (int i = 0; i < steps; i++) StepMovement(h, jumpReq && i == 0, dt / steps);

        // —— 深度层：一次换一层，带冷却 ——
        laneCooldown -= Time.unscaledDeltaTime;
        int d = 0;
        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))   d = -1;
        if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) d = +1;
        float v = Input.GetAxisRaw("Vertical");
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

        if (vis != null) vis.SetBase(!grounded ? "jump" : moving ? "walk" : "stand");

        // —— 自动普攻 ——
        // Boss 优先：Boss 体积大、通常只有一只，误判成本比杂兵高得多
        atkTimer -= dt;
        if (atkTimer <= 0f && (vis == null || !vis.IsOneShot))
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
                    // 没目标时短轮询即可。若这里不重置，atkTimer 会一直 <=0，
                    // 于是每帧都跑一次 FindObjectsOfType —— 那是白白烧 CPU
                    atkTimer = 0.12f;
                }
            }
        }
    }

    // 使用原 Map/Physics.img 的像素单位：125px/s、555px/s、2000px/s²。
    // 空中位置独立于脚下平台；只在下降时扫过 foothold 才落地。
    void StepMovement(float h, bool jumpReq, float dt)
    {
        if (jumpReq && grounded)
        {
            vy = jumpSpeed;
            grounded = false;
        }
        float previousY = terrainY + jumpY;
        float nextX = ClampX(worldX + h * moveSpeed * dt);
        moving = Mathf.Abs(h) > 0.15f;
        if (moving) facing = h > 0f ? 1 : -1;

        float ground = GroundAt(nextX, terrainY);
        bool support = HasSupport(nextX, ground);
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
            ground = GroundAt(nextX, terrainY);
            support = HasSupport(nextX, ground);
            if (support && Mathf.Abs(ground - terrainY) <= 0.6f)
                terrainY = ground;
            else
            {
                grounded = false;
                vy = 0f;
            }
        }
        worldX = nextX;
        if (grounded) return;

        // 帧内细分由调用方控制；用解析位移，跳高不随 30/60/120Hz 改变。
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
        b.TakeDamage(EffectiveAtk, facing);
        HitFeel.Punch(1f);
        if (vis != null) vis.PlayOnce("atk", 1f);
        // 原版近战的视觉就是角色挥砍帧本身（武器合成在帧里），没有额外的弧光贴片
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
            if (Mathf.Abs(dx) > reach) continue;

            float d = Mathf.Abs(dx);
            if (d < bestD) { bestD = d; best = e; }
        }
        return best;
    }

    void Strike(EnemyUnit e)
    {
        e.TakeDamage(EffectiveAtk, facing);
        HitFeel.Punch(1f);
        if (vis != null) vis.PlayOnce("atk", 1f);
        // 同 StrikeBoss：挥砍帧即视觉，不再叠黄色弧光
    }

    public void TakeDamage(float dmg, int dir)
    {
        if (Time.unscaledTime < invulnUntil) return;
        hp = Mathf.Max(0f, hp - dmg);
        invulnUntil = Time.unscaledTime + 0.5f;
        worldX += dir * 0.25f;
        Fx.Flash(sr, new Color(1f, 0.35f, 0.35f));
        if (vis != null) vis.PlayOnce(hp <= 0f ? "die" : "hit");
        HitFeel.Punch(0.6f);
        // 玩家挨打是原版的蓝字（NoBlue），和打怪的红字区分开
        DamageText.Show(new Vector3(FeetPos.x, FeetPos.y + 2.55f, 0f), dmg, "blue", 0.85f);
        if (hp <= 0f && GameLoop.I != null) GameLoop.I.OnPlayerDead();
    }

    public void Heal(float v) => hp = Mathf.Min(hpMax, hp + v);

    // Buff 技能：临时提升普攻倍率
    public void AddAtkBuff(float mul, float sec)
    {
        atkBuffMul = mul;
        atkBuffUntil = Time.unscaledTime + sec;
    }
    float AtkMul() => Time.unscaledTime < atkBuffUntil ? atkBuffMul : 1f;
    public float EffectiveAtk => attackDamage * AtkMul();

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
