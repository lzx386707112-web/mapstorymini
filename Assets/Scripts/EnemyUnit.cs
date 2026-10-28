using System;
using System.Collections.Generic;
using UnityEngine;

public class EnemyUnit : MonoBehaviour
{
    public readonly Dictionary<int,long> DamageContributions = new Dictionary<int,long>();
    public bool HasBotContribution { get; private set; }
    public long TotalHealed;
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
    public bool nativeFloating;
    public float homeX;
    public float roamMin = float.NegativeInfinity, roamMax = float.PositiveInfinity;
    public float aggroRange = 8f;
    int patrolDir = 1;

    float stunUntil;
    float knockV;
    float laneCooldown;
    float dieT = -1f;          // >=0 表示正在播死亡动画（含末尾淡出）
    float dieDur;
    float terrainY = PlayerCtrl.GroundY;   // 脚下地形 y（跟随 foothold 起伏）
    SpriteRenderer body;
    SpriteAnim vis;
    SpriteRenderer hpBar;
    SpriteRenderer hpBg;
    Transform hpRoot;
    float laneVis;
    float visHeight = 1.15f;

    public int Lane => lane;
    public Dictionary<string, object> CombatStats
    {
        get
        {
            var stats = fieldMode && FieldCatalog.Mobs.TryGetValue(fieldMobId, out var mob) ? mob.stats : NativeCombatData.Mob(artKey);
            return NativeController != null ? NativeController.EffectiveStats(stats) : stats;
        }
    }
    public NativeMobController NativeController { get; private set; }
    public bool IsBoss => NativeCombatData.Int(CombatStats, "boss") != 0;
    public void BindNative(FieldDirector field) => NativeController = NativeMobController.Attach(this, field);
    public void PlayNativeAction(string action) { if (vis != null) vis.PlayOnce(action, 1f); }
    public void DefeatNative() { if (!dead) { hp = 0; Die(); } }

    // 怪物音效 ID：原版图模式用 fieldMobId，章节模式把 artKey 翻成源端怪物 ID
    // （mushmom→6130101、slimeking→9300003…，见 NativeAssetIndex.mobs）。
    public string MobSoundId
    {
        get
        {
            if (fieldMode && fieldMobId > 0) return fieldMobId.ToString();
            var mobs = (Dictionary<string, object>)WzAssets.Index["mobs"];
            object raw;
            if (mobs.TryGetValue(artKey, out raw))
            {
                double id;
                if (double.TryParse(Convert.ToString(raw), out id)) return ((int)id).ToString();
            }
            return null;
        }
    }

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
        var placeholder = gameObject.GetComponent<SpriteRenderer>();
        if (placeholder != null) placeholder.enabled = false;
        vis = Art.Has("mobs", artKey) ? Art.Attach(transform, "mobs", artKey, "stand", 0f) : null;
        if (vis == null) throw new System.IO.InvalidDataException("Missing original monster IMG: " + artKey);
        body = vis.Renderer;
        body.sortingOrder = 300 - lane * 12;
        body.color = Color.white;
        visHeight = Mathf.Max(0.6f, body.bounds.size.y);

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
        if (dead) { TickDeathFade(); return; }
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
        if (!stunned && !nativeFloating && (NativeController == null || !NativeController.Busy))
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
                if (gy <= terrainY + 0.6f && (NativeController == null || NativeController.CanMove))
                {
                    transform.position = new Vector3(nx, transform.position.y, 0f);
                    terrainY = gy;
                    moving = true;
                }
            }
            else
            {
                faceDir = dx >= 0f ? 1 : -1;
            }
        }

        // 0x94B30B checks the player's contact immunity before testing mob overlap.
        // Movement/attack cadence and mob action state do not schedule touch damage.
        if (TouchesPlayer(p))
            p.TakeDamage(NativeController != null ? NativeCombatData.Int(CombatStats,"PADamage") : damage,
                p.worldX >= transform.position.x ? 1 : -1, false, NativeCombatData.Int(CombatStats,"level",1));

        laneVis = Mathf.Lerp(laneVis, lane, 1f - Mathf.Exp(-12f * dt));
        if (vis != null)
        {
            vis.SetBase(moving ? "move" : "stand");
            vis.SetFacing(NativeController != null && NativeController.Busy ? NativeController.Direction : faceDir);
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
        if (NativeController != null && !NativeController.CanMove) return false;
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

    // 死亡动画末尾淡出（原版怪物消失前会透明掉）。顿帧期间 dt==0，淡出一起停。
    void TickDeathFade()
    {
        if (dieT < 0f || body == null) return;
        dieT += Time.deltaTime;
        float from = dieDur * 0.65f;
        float a = dieT <= from ? 1f : Mathf.Clamp01(1f - (dieT - from) / Mathf.Max(0.01f, dieDur - from));
        var c = body.color;
        c.a = a;
        body.color = c;
    }

    void SyncVisual()
    {
        // 每帧把脚下地形吸附到所在 x 的 foothold 上（击退、跟随都自然贴合）
        if (!nativeFloating)
        {
            terrainY = PlayerCtrl.GroundAt(transform.position.x, terrainY);
            transform.position = new Vector3(transform.position.x, terrainY + DepthOffset, 0f);
        }
        if (body != null) body.sortingOrder = 300 - lane * 12;
        // 接触判定框随当前动画帧更新（走路/攻击帧宽高不同）
        if (body != null && body.sprite != null)
        {
            var b = body.bounds;
            visHeight = Mathf.Max(0.6f, b.size.y);
        }
    }

    // 真实接触判定：以双方脚底为原点的身体 AABB，X、Y 两个方向都要有实际重叠。
    // 以前只比脚底高度差（field 模式 dy<=1.8 即视作同层）+ 横向 attackRange，
    // 站在高低不同的平台上只要贴近就会被打——现在必须两个身体真的碰到。
    bool TouchesPlayer(PlayerCtrl p)
    {
        // CMobPool::FindHitMob 678D47 -> 667569: notAttack templates cannot hit by contact.
        if (fieldMode && WzAssets.FileRoot("Mob/" + fieldMobId.ToString("D7") + ".img").Int("info/notAttack") != 0) return false;
        if (vis == null || vis.FrameUri == null) return false;
        var frame = WzAssets.Node(vis.FrameUri);
        var lt = frame?.Get("lt"); var rb = frame?.Get("rb");
        if (lt == null || rb == null) return false;
        int left = lt.X, right = rb.X;
        if (body.flipX) { left = -rb.X; right = -lt.X; }
        // CMobPool::FindHitMob 0x678E21: shrink the ordinary body rectangle,
        // retaining at least a 2px center. Custom multi-rect mobs use another path.
        int cx = (left + right) / 2, cy = (lt.Y + rb.Y) / 2;
        left = System.Math.Min(left + 25, cx - 1);
        right = System.Math.Max(right - 25, cx + 1);
        int top = System.Math.Min(lt.Y + 30, cy - 1);
        int bottom = System.Math.Max(rb.Y - 10, cy + 1);
        var bounds = Rect.MinMaxRect(transform.position.x + left / Art.PPU,
            transform.position.y - bottom / Art.PPU, transform.position.x + right / Art.PPU,
            transform.position.y - top / Art.PPU);
        return bounds.Overlaps(p.ContactBounds);
    }

    public void TakeDamage(float dmg, int fromDir)
    {
        if (dead) return;

        TakeHit(CombatRules.Resolve(dmg), fromDir);
    }

    // nativeHit：技能已经用原版 hit 部件打了爆点，这里就不再叠占位的黄色柔光
    public void TakeHit(DamageResult hit, int fromDir, bool nativeHit = false, bool magic = false)
    {
        if (NativeController != null) hit = NativeController.FilterHit(hit, magic);
        ApplyHit(hit, fromDir, nativeHit, true);
    }

    public void TakeReplayHit(DamageResult[] hits, int direction, bool magic, bool indexed, int interval, bool total = false, int ordinarySkill = -1)
    {
        if (dead) return;
        var filtered = new DamageResult[hits.Length];
        long sum = 0; bool critical = false;
        for (int i = 0; i < hits.Length; i++)
        {
            filtered[i] = NativeController != null ? NativeController.FilterHit(hits[i], magic) : hits[i];
            sum += filtered[i].Amount; critical |= filtered[i].Critical;
        }
        NativeDamageLines.Show(this, filtered, indexed, interval, total, ordinarySkill);
        ApplyHit(new DamageResult((int)System.Math.Min(int.MaxValue, sum), critical), direction, true, false);
    }

    // Server-owned periodic damage is already resolved; do not re-roll mob filtering.
    public void TakeReplayDot(int amount) => ApplyHit(new DamageResult(amount,false),0,true,true,false);

    public void TakeBotHit(int actorId,int damage,int fromDir)
    {
        if (dead) return;
        HasBotContribution=true;
        ApplyHit(new DamageResult(damage,false),fromDir,true,false,true,actorId);
    }

    void ApplyHit(DamageResult hit, int fromDir, bool nativeHit, bool showNumber, bool react = true, int attackerId = 0)
    {
        if (dead) return;
        if (hit.Amount <= 0) { if (showNumber && hit.Amount == 0) DamageText.Miss(DamageAnchor + Vector3.up * (15f / Art.PPU)); return; }
        bool crit = hit.Critical;
        float real = hit.Amount;
        if (fieldMode && (attackerId!=0 || GameSession.InGame))
        {
            int source=attackerId!=0 ? attackerId : GameSession.CharacterId;
            long actual=System.Math.Min(NativeController!=null ? NativeController.CurrentHp : (long)hp,hit.Amount);
            DamageContributions.TryGetValue(source,out long previous); DamageContributions[source]=previous+actual;
        }
        if (NativeController != null) NativeController.ApplyDamage(hit.Amount);
        else hp = Mathf.Max(0f, hp - real);

        if (react)
        {
            // 击退按原端 info/pushed 缩放：蜗牛 pushed=1 全额滑动，蘑菇王 pushed=1000 几乎不动。
            // 距离基准仍是面板上的 HitFeel.Knockback（0.9 单位 ≈ 36px 行程，衰减 26/s）。
            // （原版普攻本身不推怪，这里保留一小段滑动是本地手感选择。）
            int pushed = NativeCombatData.Int(CombatStats, "pushed", 1);
            knockV = fromDir * HitFeel.Knockback * 6.1f * Mathf.Clamp01(1f / Mathf.Max(1f, pushed));
            Fx.Flash(body, Color.white);
            if (vis != null) vis.PlayOnce("hit");
            if (!nativeHit)
                Fx.Burst(transform.position + Vector3.up * 0.7f, crit ? 0.6f : 0.45f,
                         new Color(1f, 0.9f, 0.5f, 0.9f));

            // 受击音效按世界位置衰减（Mob/<id>/Damage，源端 Sound/Mob.img）
            string mid = MobSoundId;
            if (mid != null) AudioManager.PlayAt("Mob/" + mid + "/Damage", transform.position, 0.9f, 9f);

        }

        if (showNumber) DamageText.Show(DamageAnchor + Vector3.up * (15f / Art.PPU), real, crit ? "cri" : "red");
        if (crit) HitFeel.Punch(1.35f);

        if (hp <= 0f) Die();
    }

    // 数字从头顶冒出来：素材 1:1 时 bounds 就是实际贴图范围
    public Vector3 DamageAnchor => HeadPos;
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
        if (IsBoss) return;
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

    public void Stun(float seconds) { if (!IsBoss) stunUntil = Mathf.Max(stunUntil, Time.time + seconds); }

    void Die()
    {
        dead = true;
        All.Remove(this);
        if (hpRoot != null) hpRoot.gameObject.SetActive(false);   // 原版死亡即收血条
        Fx.Burst(transform.position + Vector3.up * 0.6f, 1.1f, new Color(1f, 0.8f, 0.35f, 0.95f), 0.3f);
        if (GameLoop.I != null) GameLoop.I.OnEnemyKilled(this);

        // 死亡动画按 WZ die 帧的总时延播（蜗牛 1260ms、猪 840ms、蘑菇王 1100ms），
        // 1 倍速 = 原版节奏；末尾 35% 淡出，播完即毁。以前写死 0.45s，大部分怪的
        // 死亡动作只播了 1/3 就被抹掉。
        dieDur = DeathSeconds();
        dieT = 0f;
        if (vis != null) vis.PlayOnce("die", 1f);
        string mid = MobSoundId;
        if (mid != null) AudioManager.PlayAt("Mob/" + mid + "/Die", transform.position, 1f, 11f);
        Destroy(gameObject, dieDur + 0.05f);
    }

    // die clip 总时长；没有死亡动画时给一个「看得见」的下限
    public static float DeathSeconds(EnemyUnit e)
    {
        var clip = Art.GetClip("mobs", e.artKey, Art.Resolve("mobs", e.artKey, "die", "die1"));
        return clip != null ? clip.Duration : 0.45f;
    }
    float DeathSeconds() => DeathSeconds(this);
}
