using System.Collections;
using UnityEngine;

// Boss 单位：状态机 enter → chase → telegraph → air → recover。
// 两个设计要点（都来自原型验证过的结论）：
//   1. 跳砸**锁定释放瞬间的玩家位置**，所以「看见红圈往外走」永远有效，不吃反应速度；
//   2. 落地后有 recover 秒硬直，那是唯一能安心输出的窗口 —— 没有这个窗口玩家会一直被打断。
public class BossUnit : MonoBehaviour
{
    public StageBossDef def;
    public float hp, hpMax;
    public bool  dead;
    public bool  enraged;

    public float HpRatio => hpMax <= 0f ? 0f : Mathf.Clamp01(hp / hpMax);

    enum St { Enter, Chase, Telegraph, Air, Recover, Summon }
    St st = St.Enter;
    float stT;
    float actTimer;
    int   patIdx;
    float contactTimer;
    float airH;
    float targetX;
    int   summonLeft;
    float summonT;
    float laneVis = 2f;
    float flashT;
    float terrainY = PlayerCtrl.GroundY;   // 脚下地形 y（跟随 foothold 起伏）

    SpriteRenderer body;
    SpriteAnim vis;
    Transform ring;
    SpriteRenderer ringSr;
    SpriteRenderer legShade;

    public int Lane => 2;

    float RadiusUnit => Px.ToUnit(def != null ? def.slam.radius : 122f);
    const float GroundY = PlayerCtrl.GroundY;

    // ————————————— 圆环贴图（程序生成，避免依赖美术资源）—————————————
    static Sprite _ring;
    static Sprite RingSprite()
    {
        if (_ring != null) return _ring;
        const int N = 128;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        var px = new Color[N * N];
        float cx = (N - 1) * 0.5f;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x - cx) / cx, dy = (y - cx) / cx;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                Color c = new Color(1f, 0.22f, 0.26f, 0f);
                if (d <= 1f && d >= 0.82f) c.a = 0.95f;        // 外环
                else if (d < 0.82f)         c.a = 0.16f;        // 内部淡填充
                px[y * N + x] = c;
            }
        tex.SetPixels(px);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply();
        _ring = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
        return _ring;
    }

    public void Init(StageBossDef d, float startX)
    {
        def = d;
        hpMax = Mathf.Max(1f, d.hp);
        hp = hpMax;
        laneVis = Lane;

        // 本体
        float w = 1.5f * Mathf.Max(1f, d.scale) / 3f;
        float h = 2.3f * Mathf.Max(1f, d.scale) / 3f;
        var col = d.type == "slime" ? new Color(0.45f, 0.82f, 0.55f) : new Color(0.78f, 0.34f, 0.42f);
        if (body == null)
            body = SpriteFactory.Make("BossBody", transform, new Vector2(w, h), col, 300);
        body.color = col;

        // 真素材（蘑菇王 / 绿水灵王）：和角色/杂兵同一套规则 —— 1:1 直接用原图像素尺寸。
        // 以前这里按「世界高度 1.15 单位」缩放，蘑菇王 220px 的贴图被压到 46px，
        // 结果 Boss 比主角还小一号。原图本身就是 Boss 级体型（蘑菇王 ≈ 主角 2.6 倍），
        // 所以不需要再乘 JSON 里的 scale —— 那个倍率是给占位方块定的。
        string artKey = d.type == "slime" ? "slimeking" : "mushmom";
        if (Art.Has("mobs", artKey))
        {
            if (vis == null) vis = Art.Attach(transform, "mobs", artKey, "stand", 0f);
            if (vis != null)
            {
                var vr = vis.Renderer;
                vr.sortingOrder = 300;
                vr.color = Color.white;
                if (body != null && body != vr) body.enabled = false;
                body = vr;
                h = vr.bounds.size.y;
                w = vr.bounds.size.x;
            }
        }

        // 地面投影：Boss 体积大，没有影子会看起来在飘
        if (legShade == null)
        {
            legShade = SpriteFactory.Make("BossShadow", transform, new Vector2(w * 1.15f, 0.16f),
                                          new Color(0f, 0f, 0f, 0.42f), 120, false);
            legShade.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        }

        // 红圈（平时隐藏）
        if (ring == null)
        {
            var go = new GameObject("TelegraphRing");
            go.transform.SetParent(null, false);      // 不跟着 Boss 跳，留在落地位置
            ringSr = go.AddComponent<SpriteRenderer>();
            ringSr.sprite = RingSprite();
            ringSr.sortingOrder = 110;
            ringSr.enabled = false;
            ring = go.transform;
        }

        terrainY = PlayerCtrl.GroundAt(startX, PlayerCtrl.GroundY);
        transform.position = new Vector3(startX, terrainY + DepthOffset, 0f);
        st = St.Enter;
        stT = 1.2f;
        actTimer = 1.4f;
        patIdx = 0;
        if (GameLoop.I != null && GameLoop.I.stage != null)
            GameLoop.I.stage.OnBossSpawned(this);
    }

    float DepthOffset => (laneVis - (PlayerCtrl.Lanes - 1) * 0.5f) * PlayerCtrl.LaneStep;

    float CurCooldown()
    {
        float ratio = HpRatio;
        float cd = 2.2f;
        if (def != null && def.phases != null)
            foreach (var p in def.phases)
                if (ratio <= p.at + 0.0001f) cd = p.cooldown;   // 越到后面越快
        return Mathf.Max(0.6f, cd);
    }

    void Update()
    {
        if (dead || def == null) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        var p = GameLoop.I != null ? GameLoop.I.player : null;
        stT     -= dt;
        flashT  -= dt;
        contactTimer -= dt;

        switch (st)
        {
            case St.Enter:
                if (stT <= 0f) { st = St.Chase; actTimer = 1.0f; }
                break;

            case St.Chase:
                if (p != null) ChaseMove(dt, p);
                actTimer -= dt;
                if (actTimer <= 0f) PickAction();
                break;

            case St.Telegraph:
                if (ringSr != null)
                {
                    // 脉冲：越接近落地闪得越快，玩家用余光就能判断时机
                    float k = 1f - Mathf.Clamp01(stT / Mathf.Max(0.01f, def.slam.telegraph));
                    float pulse = 0.55f + 0.45f * Mathf.Sin(Time.time * (10f + 22f * k));
                    var c = ringSr.color;
                    ringSr.color = new Color(c.r, c.g, c.b, 0.75f + 0.25f * pulse);
                    float sc = RadiusUnit * (2.06f - 0.06f * pulse);
                    ring.transform.localScale = new Vector3(sc, sc * 0.42f, 1f);
                }
                if (stT <= 0f) { st = St.Air; stT = def.slam.air; }
                break;

            case St.Air:
                // 滞空阶段沿抛物线抬起再落下（只做视觉，判定在落地时算）
                airH = Mathf.Sin(Mathf.Clamp01(1f - stT / Mathf.Max(0.01f, def.slam.air)) * Mathf.PI) * 1.6f;
                if (stT <= 0f) Land();
                break;

            case St.Recover:
                airH = Mathf.Lerp(airH, 0f, 1f - Mathf.Exp(-18f * dt));
                if (stT <= 0f) { st = St.Chase; actTimer = 0.8f; }
                break;

            case St.Summon:
                summonT -= dt;
                if (summonT <= 0f && summonLeft > 0)
                {
                    summonT = Mathf.Max(0.08f, def.summon.gap);
                    summonLeft--;
                    if (GameLoop.I != null)
                        GameLoop.I.SpawnMob(def.summon.type, transform.position.x + Random.Range(-2.2f, 2.2f));
                }
                if (summonLeft <= 0 && stT <= 0f) { st = St.Chase; actTimer = 0.9f; }
                break;
        }

        // 受击闪白
        if (body != null && flashT <= 0f) body.color = BaseColor();

        if (vis != null)
        {
            string want = st == St.Chase ? "move" : "stand";
            if (st == St.Telegraph || st == St.Air) want = "attack1";
            vis.SetBase(want);
            vis.SetFacing(p != null && p.worldX >= transform.position.x ? 1 : -1);
        }

        laneVis = Mathf.Lerp(laneVis, Lane, 1f - Mathf.Exp(-10f * dt));
        terrainY = PlayerCtrl.GroundAt(transform.position.x, terrainY);
        transform.position = new Vector3(transform.position.x, terrainY + DepthOffset + airH, 0f);
        if (body != null) body.sortingOrder = 300 - Lane * 12;
    }

    Color BaseColor() => vis != null
        ? Color.white
        : (def != null && def.type == "slime"
            ? new Color(0.45f, 0.82f, 0.55f)
            : new Color(0.78f, 0.34f, 0.42f));

    void ChaseMove(float dt, PlayerCtrl p)
    {
        float dx = p.worldX - transform.position.x;
        if (Mathf.Abs(dx) > 1.6f)
        {
            float v = Px.ToUnit(def.spd);
            transform.position += Vector3.right * (Mathf.Sign(dx) * v * dt);
        }
        else if (contactTimer <= 0f && Mathf.Abs(p.lane - Lane) <= 1)
        {
            contactTimer = 0.9f;
            p.TakeDamage(def.contact, dx >= 0f ? 1 : -1);
        }
    }

    void PickAction()
    {
        string act = "chase";
        if (def.pattern != null && def.pattern.Count > 0)
        {
            act = def.pattern[patIdx % def.pattern.Count];
            patIdx++;
        }

        if (act == "slam")
        {
            var p = GameLoop.I != null ? GameLoop.I.player : null;
            // 锁定「按下瞬间」的位置：之后玩家走出去就一定安全
            targetX = p != null ? p.worldX : transform.position.x;
            st = St.Telegraph;
            stT = def.slam.telegraph;
            if (ringSr != null)
            {
                ringSr.enabled = true;
                ringSr.color = new Color(1f, 0.25f, 0.3f, 0.9f);
                float ringY = PlayerCtrl.GroundAt(targetX, terrainY);
                ring.transform.position = new Vector3(targetX, ringY + 0.03f, 0f);
            }
            if (GameLoop.I != null && GameLoop.I.stage != null)
                GameLoop.I.stage.OnBossTelegraph(def.slam.telegraph);
        }
        else if (act == "summon")
        {
            st = St.Summon;
            stT = Mathf.Max(1.2f, def.summon.count * def.summon.gap + 0.4f);
            summonLeft = def.summon.count;
            summonT = 0.15f;
        }
        else
        {
            st = St.Chase;
            actTimer = CurCooldown();
        }
    }

    void Land()
    {
        airH = 0f;
        if (ringSr != null) ringSr.enabled = false;

        var p = GameLoop.I != null ? GameLoop.I.player : null;
        if (p != null)
        {
            float d = Mathf.Abs(p.worldX - targetX);
            if (d <= RadiusUnit && p.jumpY < 0.9f)     // 跳到半空能躲过去，给一个操作奖励
                p.TakeDamage(def.slam.damage, p.worldX >= targetX ? 1 : -1);
        }

        HitFeel.Punch(1.6f);
        float landY = PlayerCtrl.GroundAt(targetX, terrainY);
        Fx.Ring(new Vector3(targetX, landY + 0.06f, 0f), RadiusUnit * 1.6f, new Color(1f, 0.4f, 0.3f, 0.9f), 0.35f);
        Fx.Burst(new Vector3(targetX, landY + 0.3f, 0f), RadiusUnit * 0.8f, new Color(1f, 0.75f, 0.4f, 0.9f), 0.3f);

        st = St.Recover;
        stT = def.slam.recover;                        // ← 唯一的输出窗口
    }

    public void TakeDamage(float dmg, int fromDir)
    {
        if (dead) return;
        // 硬直期伤害 1.25 倍：让玩家学会「等硬直」而不是无脑贴脸
        float mul = st == St.Recover ? 1.25f : 1f;

        bool crit = Random.value < HitFeel.CritRate;
        float real = dmg * mul * (crit ? HitFeel.CritMul : 1f);
        hp -= real;

        if (body != null)
        {
            body.color = Color.white;
            flashT = HitFeel.FlashDur;
        }
        Fx.Burst(transform.position + Vector3.up * 0.9f, crit ? 0.7f : 0.5f,
                 new Color(1f, 0.85f, 0.4f, 0.9f));

        // Boss 体积大，数字要压到头顶，否则会被身体挡住
        float top = body != null && body.sprite != null
            ? body.bounds.max.y
            : transform.position.y + 2.2f;
        DamageText.Show(new Vector3(transform.position.x, top - 0.2f, 0f),
                        real, crit ? "cri" : "red", crit ? 1.15f : 1.06f);

        if (hp <= 0f) Die();
    }

    public void Stun(float seconds)
    {
        // Boss 不吃硬控，只把当前动作往后推 —— 否则「眩晕 3 秒」能直接锁死
        if (st == St.Chase) actTimer = Mathf.Max(actTimer, seconds * 0.35f);
    }

    void Die()
    {
        dead = true;
        if (ringSr != null) ringSr.enabled = false;
        if (vis != null) vis.PlayOnce("die");
        Fx.Burst(transform.position + Vector3.up * 1.0f, 3.2f, new Color(1f, 0.85f, 0.4f, 1f), 0.6f);
        Fx.Ring(transform.position + Vector3.up * 0.8f, 4.5f, new Color(1f, 0.9f, 0.5f, 0.95f), 0.5f);
        HitFeel.Punch(2.4f);
        if (GameLoop.I != null)
        {
            if (GameLoop.I.stage != null) GameLoop.I.stage.OnBossKilled(this);
            GameLoop.I.RewardBossGold();
        }
        Destroy(gameObject, vis != null ? 0.6f : 0.05f);
    }

    void OnDestroy()
    {
        if (ring != null) Destroy(ring.gameObject);
    }
}
