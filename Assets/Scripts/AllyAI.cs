using UnityEngine;

// 跟随英雄：AI 自动战斗，只做三件事——站位、追人、打最近的。
// 关键约束：英雄不帮你躲红圈、不打弱点，该躲的还得玩家自己躲。
public class AllyAI : MonoBehaviour
{
    public int   lane = 2;
    public float xOffset = -1.6f;
    public float speed = 8.0f;
    public float attackInterval = 0.9f;
    public float reach = 2.0f;
    public float damage = 7f;
    public Color tint = Color.cyan;

    float laneVis;
    float atkTimer;
    SpriteRenderer body;
    SpriteAnim vis;
    PlayerCtrl owner;
    string artKey = "warrior";

    public void Setup(PlayerCtrl p, Color c, int laneIndex, float offset, string heroArt = null)
    {
        owner = p;
        tint = c;
        lane = laneIndex;
        laneVis = laneIndex;
        xOffset = offset;
        if (!string.IsNullOrEmpty(heroArt)) artKey = heroArt;
        Build();
    }

    void Build()
    {
        body = GetComponent<SpriteRenderer>();
        if (body == null)
            body = SpriteFactory.Make("AllyBody", transform, new Vector2(0.7f, 1.25f), tint, 280);
        body.color = tint;

        if (Art.Has("chars", artKey))
        {
            vis = Art.Attach(transform, "chars", artKey, "stand", 0f);
            if (vis != null)
            {
                var vr = vis.Renderer;
                vr.sortingOrder = 280;
                vr.color = Color.white;      // 用素材就别再染色，否则整张图发紫
                if (body != null && body != vr) body.enabled = false;
                body = vr;
            }
        }
    }

    float DepthOffset => (laneVis - (PlayerCtrl.Lanes - 1) * 0.5f) * PlayerCtrl.LaneStep;

    void Update()
    {
        if (owner == null) return;
        float dt = Time.deltaTime;
        if (dt > 0f) Step(dt);
        Sync();
    }

    void Step(float dt)
    {
        // 站到玩家斜后方，不占正前方（正前方留给玩家输出）
        float targetX = owner.worldX - xOffset * (owner.facing >= 0 ? 1f : -1f);
        targetX = Mathf.Clamp(targetX, PlayerCtrl.XMin, PlayerCtrl.XMax);

        float d = targetX - transform.position.x;
        bool moving = Mathf.Abs(d) > 0.12f;
        if (moving)
        {
            // 落后越多跑越快，避免队员被拖成一条线
            float s = speed * Mathf.Clamp(Mathf.Abs(d) / 2f, 0.55f, 1.6f);
            transform.position += Vector3.right * (Mathf.Sign(d) * s * dt);
        }

        laneVis = Mathf.Lerp(laneVis, lane, 1f - Mathf.Exp(-11f * dt));

        if (vis != null)
        {
            vis.SetBase(moving ? "walk" : "stand");
            int face = moving ? (d >= 0f ? 1 : -1) : (owner != null ? owner.facing : 1);
            vis.SetFacing(face);
        }

        atkTimer -= dt;
        if (atkTimer <= 0f)
        {
            // Boss 优先。少了这一段，4 个英雄在 Boss 战里全部变成观众，
            // 玩家从「5 人小队」退化成单挑，Boss 血量会被迫拉长一倍以上。
            var b = FindBoss();
            if (b != null)
            {
                atkTimer = attackInterval;
                b.TakeDamage(damage, b.transform.position.x >= transform.position.x ? 1 : -1);
                HitFeel.Punch(0.5f);
                if (vis != null) vis.PlayOnce("atk", 1.2f);
                Fx.Slash(new Vector3(transform.position.x + (b.transform.position.x >= transform.position.x ? 0.8f : -0.8f),
                                     transform.position.y + 0.65f, 0f),
                         new Color(tint.r, tint.g, tint.b, 0.8f),
                         b.transform.position.x >= transform.position.x ? 1 : -1);
                return;
            }

            var t = FindTarget();
            if (t != null)
            {
                atkTimer = attackInterval;
                t.TakeDamage(damage, t.transform.position.x >= transform.position.x ? 1 : -1);
                // 队友的打击感弱一档：只有 60% 顿帧，否则满屏都在卡
                HitFeel.Punch(0.55f);
                if (vis != null) vis.PlayOnce("atk", 1.2f);
                Fx.Slash(transform.position + Vector3.up * 0.65f,
                         new Color(tint.r, tint.g, tint.b, 0.8f),
                         t.transform.position.x >= transform.position.x ? 1 : -1);
            }
            else atkTimer = 0.15f;
        }
    }

    BossUnit FindBoss()
    {
        foreach (var b in FindObjectsOfType<BossUnit>())
        {
            if (b == null || b.dead) continue;
            // Boss 体型大，占 3 个深度层（差 2 以内都算贴上了），
            // 否则队形里最外那两个英雄在 Boss 战里永远够不着
            if (Mathf.Abs(b.Lane - lane) > 2) continue;
            if (Mathf.Abs(b.transform.position.x - transform.position.x) <= reach + 2.4f) return b;
        }
        return null;
    }

    EnemyUnit FindTarget()
    {
        EnemyUnit best = null;
        float bestD = float.MaxValue;
        foreach (var e in EnemyUnit.All)
        {
            if (e == null || e.dead) continue;
            if (Mathf.Abs(e.Lane - lane) > 1) continue;
            float dx = Mathf.Abs(e.transform.position.x - transform.position.x);
            if (dx > reach) continue;
            if (dx < bestD) { bestD = dx; best = e; }
        }
        return best;
    }

    void Sync()
    {
        transform.position = new Vector3(transform.position.x,
                                         PlayerCtrl.GroundY + DepthOffset, 0f);
        if (body != null) body.sortingOrder = 300 - lane * 12 - 2;
    }
}
