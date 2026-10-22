using System.Collections.Generic;
using UnityEngine;

public class EnemyUnit : MonoBehaviour
{
    public static readonly List<EnemyUnit> All = new List<EnemyUnit>();

    public string  label = "杂兵";
    public string  artKey = "mushroom";     // 对应 art/manifest.json 里 mobs 的键
    public float   hpMax = 40f;
    public float   hp;
    public float   damage = 8f;
    public float   speed = 1.9f;
    public float   attackRange = 1.15f;
    public float   attackInterval = 1.3f;
    public int     lane = 2;
    public bool    elite;
    public bool    dead;
    public Color   tint = new Color(0.62f, 0.36f, 0.72f);   // 关卡数据按 type 指定

    // —— 原版地图模式 ——
    // 刷怪点给的巡逻范围（rx0..rx1）与出生点；玩家进入视野才追击，否则在自己的台子上来回走。
    public bool  fieldMode;
    public int   fieldMobId;
    public float homeX;
    public float roamMin = float.NegativeInfinity, roamMax = float.PositiveInfinity;
    public float aggroRange = 8f;
    int patrolDir = 1;

    float atkTimer;
    float stunUntil;
    float knockV;
    float laneCooldown;
    float terrainY = PlayerCtrl.GroundY;   // 脚下地形 y（跟随 foothold 起伏）
    SpriteRenderer body;
    SpriteAnim vis;
    SpriteRenderer hpBar;
    SpriteRenderer hpBg;
    Transform hpRoot;
    float laneVis;
    float visHeight = 1.15f;
    float bodyHalfW = 0.39f;    // 身体半宽（接触判定用，随当前动画帧同步）

    public int Lane => lane;
    public Dictionary<string, object> CombatStats => NativeCombatData.Mob(artKey);

    void OnEnable()  => All.Add(this);
    void OnDisable() => All.Remove(this);

    public void Init(int laneIndex, float startX)
    {
        Init(laneIndex, startX, float.NaN);
    }

    // refY：出生点在地图里的 y（世界坐标）。原版地图有高低差，
    // 不能用全局 GroundY 当参考，否则会吸附到最近的最底层平台。
    public void Init(int laneIndex, float startX, float refY)
    {
        lane = laneIndex;
        laneVis = laneIndex;
        var native = CombatStats;
        hpMax = NativeCombatData.Int(native,"maxHP",40);
        damage = NativeCombatData.Int(native,"PADamage",8);
        hp = hpMax;
        terrainY = PlayerCtrl.GroundAt(startX, float.IsNaN(refY) ? PlayerCtrl.GroundY : refY);
        transform.position = new Vector3(startX, terrainY + DepthOffset, 0f);
        BuildVisual();
    }

    float DepthOffset => (laneVis - (PlayerCtrl.Lanes - 1) * 0.5f) * PlayerCtrl.LaneStep;
    const float GroundY = PlayerCtrl.GroundY;

    void BuildVisual()
    {
        if (body == null)
        {
            body = gameObject.GetComponent<SpriteRenderer>();
            if (body == null)
                body = SpriteFactory.Make("EnemyBody", transform, new Vector2(0.78f, 1.15f),
                                          new Color(0.62f, 0.36f, 0.72f), 250);
        }
        body.color = elite ? new Color(0.85f, 0.25f, 0.3f) : tint;
        body.sortingOrder = 300 - lane * 12;

        // 有素材就换真怪：方块只作为没有素材时的兜底
        if (Art.Has("mobs", artKey))
        {
            vis = Art.Attach(transform, "mobs", artKey, "stand", 0f);
            if (vis != null)
            {
                var vr = vis.Renderer;
                vr.sortingOrder = 300 - lane * 12;
                vr.color = elite ? new Color(1f, 0.82f, 0.82f) : Color.white;
                if (body != null && body != vr) body.enabled = false;
                body = vr;
                visHeight = Mathf.Max(0.6f, body.bounds.size.y);
            }
        }

        // 血条：底 + 条，用 scale 表现比例，不做图集。
        // 注意两者必须挂在同一个 scale=1 的空节点下，
        // 否则条的 localScale 会再乘上底的 scale，宽度直接算错。
        if (hpRoot == null)
        {
            hpRoot = new GameObject("HpRoot").transform;
            hpRoot.SetParent(transform, false);
            hpRoot.localPosition = new Vector3(0f, visHeight + 0.15f, 0f);

            hpBg = SpriteFactory.Make("HpBg", hpRoot, new Vector2(0.9f, 0.11f),
                                      new Color(0.08f, 0.08f, 0.1f, 0.9f), 410, false);
            hpBg.transform.localPosition = Vector3.zero;

            hpBar = SpriteFactory.Make("HpBar", hpRoot, new Vector2(0.86f, 0.07f),
                                       new Color(0.4f, 0.95f, 0.45f), 411, false);
            hpBar.transform.localPosition = Vector3.zero;
        }
        // 原客户端只在怪物「掉过血」之后才画血条，满血时是干净的。
        // 之前每个怪头顶都常驻血条，看起来像全场都在挨打（用户截图里所有平台的蜗牛都顶着一根）。
        hpRoot.gameObject.SetActive(hp < hpMax);
    }

    void Update()
    {
        if (dead) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) { SyncVisual(); return; }

        if (knockV != 0f)
        {
            transform.position += Vector3.right * (knockV * dt);
            knockV = Mathf.MoveTowards(knockV, 0f, 26f * dt);
        }

        var p = GameLoop.I != null ? GameLoop.I.player : null;
        if (p == null) return;

        // 层差 > 1 就打不到对方，这是「站位有意义」的代价。
        // 但敌人如果在远处层不主动靠拢，玩家不换层时它就成了打不着的幽灵，
        // 而「清完怪才推进」会让关卡彻底卡死（实测卡了 40 秒以上）。
        // 所以敌人主动向玩家所在层收拢，一层一层挪，不瞬移。
        laneCooldown -= dt;
        if (!fieldMode && laneCooldown <= 0f && Mathf.Abs(lane - p.lane) > 1)
        {
            laneCooldown = 0.35f;
            lane += p.lane > lane ? 1 : -1;
        }

        bool stunned = Time.time < stunUntil;
        bool moving = false;
        int faceDir = 1;
        float dx = p.worldX - transform.position.x;
        bool aggro = true;
        if (fieldMode)
        {
            // 只有同一高度附近、且在视野内的玩家才追；否则留在刷怪点的台子上巡逻
            float dy = Mathf.Abs(p.FeetPos.y - transform.position.y);
            aggro = p.hp > 0f && Mathf.Abs(dx) <= aggroRange && dy <= 1.8f;
        }
        if (!stunned)
        {
            if (fieldMode && !aggro)
            {
                moving = Roam(dt, out faceDir);
            }
            else if (Mathf.Abs(dx) > attackRange)
            {
                int dir = dx > 0f ? 1 : -1;
                faceDir = dir;
                float nx = transform.position.x + dir * speed * dt;
                // 贴地形走：高出台阶 0.6u 以上的墙挡住（和玩家同一套规则）
                float gy = PlayerCtrl.GroundAt(nx, terrainY);
                if (gy <= terrainY + 0.6f)
                {
                    transform.position = new Vector3(nx, transform.position.y, 0f);
                    terrainY = gy;
                    moving = true;
                }
            }
            else
            {
                faceDir = dx >= 0f ? 1 : -1;
                atkTimer -= dt;
                // 只有身体真的碰到才造成接触伤害；
                // 只在 Y 方向错位重叠（比如站在高低不同的平台上）不扣血。
                if (atkTimer <= 0f && TouchesPlayer(p))
                {
                    atkTimer = attackInterval;
                    p.TakeDamage(damage, dx > 0f ? 1 : -1, false, NativeCombatData.Int(CombatStats,"level",1));
                    if (vis != null) vis.PlayOnce("atk", 1.1f);
                }
            }
        }

        laneVis = Mathf.Lerp(laneVis, lane, 1f - Mathf.Exp(-12f * dt));
        if (vis != null)
        {
            vis.SetBase(moving ? "move" : "stand");
            vis.SetFacing(faceDir);
        }
        if (hpBar != null)
        {
            if (hpRoot.gameObject.activeSelf != (hp < hpMax)) hpRoot.gameObject.SetActive(hp < hpMax);
            float ratio = Mathf.Clamp01(hp / hpMax);
            float w = 0.86f * ratio;
            hpBar.transform.localScale = new Vector3(w, 0.07f, 1f);
            // 左对齐递减：中心随宽度左移，视觉上血条从右往左退缩
            hpBar.transform.localPosition = new Vector3(-(0.86f - w) * 0.5f, 0f, 0f);
        }

        SyncVisual();
    }

    // 原地巡逻：在刷怪点给的 rx0..rx1 之间来回走。
    // 原端怪物行走速度与转向由客户端 AI 决定；巡逻和追击都用同一个 WZ 速度，
    // 以前巡逻额外乘 0.35（蜗牛只剩 9.8px/s），看起来像站桩，已去掉。
    bool Roam(float dt, out int faceDir)
    {
        faceDir = patrolDir;
        float nx = transform.position.x + patrolDir * speed * dt;
        if (nx < roamMin) { nx = roamMin; patrolDir = 1; }
        if (nx > roamMax) { nx = roamMax; patrolDir = -1; }
        float gy = PlayerCtrl.GroundAt(nx, terrainY);
        if (gy > terrainY + 0.6f || gy < terrainY - 1.2f) { patrolDir = -patrolDir; return false; }
        faceDir = patrolDir;
        transform.position = new Vector3(nx, transform.position.y, 0f);
        terrainY = gy;
        return true;
    }

    void SyncVisual()
    {
        // 每帧把脚下地形吸附到所在 x 的 foothold 上（击退、跟随都自然贴合）
        terrainY = PlayerCtrl.GroundAt(transform.position.x, terrainY);
        transform.position = new Vector3(transform.position.x, terrainY + DepthOffset, 0f);
        if (body != null) body.sortingOrder = 300 - lane * 12;
        // 接触判定框随当前动画帧更新（走路/攻击帧宽高不同）
        if (body != null && body.sprite != null)
        {
            var b = body.bounds;
            visHeight = Mathf.Max(0.6f, b.size.y);
            bodyHalfW = Mathf.Max(0.2f, b.size.x * 0.5f);
        }
    }

    // 真实接触判定：以双方脚底为原点的身体 AABB，X、Y 两个方向都要有实际重叠。
    // 以前只比脚底高度差（field 模式 dy<=1.8 即视作同层）+ 横向 attackRange，
    // 站在高低不同的平台上只要贴近就会被打——现在必须两个身体真的碰到。
    bool TouchesPlayer(PlayerCtrl p)
    {
        const float eps = 0.03f;    // 贴边也算碰到的小容差
        var pf = p.FeetPos;
        bool xOverlap = Mathf.Abs(pf.x - transform.position.x) <= p.BodyHalfWidth + bodyHalfW;
        bool yOverlap = pf.y + p.BodyHeight   >= transform.position.y + eps &&
                        transform.position.y + visHeight >= pf.y + eps;
        return xOverlap && yOverlap;
    }

    public void TakeDamage(float dmg, int fromDir)
    {
        if (dead) return;

        TakeHit(CombatRules.Resolve(dmg), fromDir);
    }

    // nativeHit：技能已经用原版 hit 部件打了爆点，这里就不再叠占位的黄色柔光
    public void TakeHit(DamageResult hit, int fromDir, bool nativeHit = false)
    {
        if (dead || hit.Amount <= 0) return;
        bool crit = hit.Critical;
        float real = hit.Amount;
        hp = Mathf.Max(0f, hp - real);

        knockV = fromDir * HitFeel.Knockback * 14f;
        Fx.Flash(body, Color.white);
        if (vis != null) vis.PlayOnce("hit", 1.4f);
        if (!nativeHit)
            Fx.Burst(transform.position + Vector3.up * 0.7f, crit ? 0.6f : 0.45f,
                     new Color(1f, 0.9f, 0.5f, 0.9f));

        DamageText.Show(HeadPos, real, crit ? "cri" : "red", crit ? 1.08f : 1f);
        if (crit) HitFeel.Punch(1.35f);

        if (hp <= 0f) Die();
    }

    // 数字从头顶冒出来：素材 1:1 时 bounds 就是实际贴图范围
    Vector3 HeadPos
    {
        get
        {
            float top = body != null && body.sprite != null
                ? body.bounds.max.y
                : transform.position.y + visHeight;
            return new Vector3(transform.position.x, top - 0.12f, 0f);
        }
    }

    public float SkillsSealedUntil { get; private set; }
    public void SealSkills(float seconds) => SkillsSealedUntil = Time.unscaledTime + Mathf.Max(0, seconds);

    public void PullTowards(float x)
    {
        var pos = transform.position;
        pos.x = PlayerCtrl.ClampX(x + (pos.x >= x ? 1f : -1f));
        pos.y = PlayerCtrl.GroundAt(pos.x, pos.y);
        terrainY = pos.y;
        transform.position = pos;
    }

    public void Push(int direction, NativeSkillDefinition skill)
    {
        knockV = direction * Mathf.Max(0f, skill.Max.x - skill.Min.x);
    }

    public void Stun(float seconds) => stunUntil = Mathf.Max(stunUntil, Time.time + seconds);

    void Die()
    {
        dead = true;
        All.Remove(this);
        Fx.Burst(transform.position + Vector3.up * 0.6f, 1.1f, new Color(1f, 0.8f, 0.35f, 0.95f), 0.3f);
        if (GameLoop.I != null) GameLoop.I.OnEnemyKilled(this);

        // 有死亡动画就先播完再消失，0.5s 是"看得见"和"不拖节奏"的折中
        if (vis != null) vis.PlayOnce("die", 1.2f);
        Destroy(gameObject, vis != null ? 0.45f : 0f);
    }
}
