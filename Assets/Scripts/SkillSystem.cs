using System.Collections;
using System.Linq;
using UnityEngine;

public class SkillDef
{
    public string name;
    public string icon = "";        // skills/<icon>/icon.png 原版图标
    public string art = "";         // skills/<art>/cast 真实释放特效（空=仅程序化特效）
    public float  cd;
    public float  left;
    public string hint;
    public Color  color;
    public SkillEffect effect;
    public float  dmg;              // 普攻倍数
    public float  range;
    public float  extra;            // 眩晕/治疗/增益时长
    public System.Action<SkillSystem> cast;
}

// 四格技能：构成循环，不能四个都长 CD。
// 技能配置来自 ClassDefs（每职业一套），这里只按 effect 类型播放，不写死职业逻辑。
// 数值都用「玩家普攻的倍数 / 世界单位」，方便统一调参。
public class SkillSystem : MonoBehaviour
{
    public SkillDef[] skills = new SkillDef[4];
    PlayerCtrl p;

    void Awake()
    {
        p = GetComponent<PlayerCtrl>();
        Profile.Load();
        var def = ClassDefs.Get(Profile.cls);
        for (int i = 0; i < 4; i++)
        {
            int idx = i;
            var cs = def.skills[i];
            skills[idx] = new SkillDef
            {
                name = cs.name, cd = cs.cd, hint = cs.hint, color = cs.color,
                effect = cs.effect, dmg = cs.dmg, range = cs.range, extra = cs.extra,
                art = cs.art,
                // 图标独立配置（每技能一个原版图标）；没配时退回特效 key 的图标
                icon = !string.IsNullOrEmpty(cs.icon) ? cs.icon : cs.art,
                cast = sys => sys.Fire(sys.skills[idx]),
            };
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        for (int i = 0; i < skills.Length; i++)
            if (skills[i].left > 0f) skills[i].left = Mathf.Max(0f, skills[i].left - dt);
    }

    public bool TryCast(int i)
    {
        if (i < 0 || i >= skills.Length || (p != null && p.IsClimbing)) return false;
        var s = skills[i];
        if (s.left > 0f) return false;
        s.left = s.cd;
        s.cast(this);
        return true;
    }

    // —— 伤害结算的复用片段 ——
    float Dmg(SkillDef s) => p.attackDamage * s.dmg;

    void HitMobs(float dmg, float maxDx, int laneTol, bool frontal, float stun = 0f)
    {
        foreach (var e in EnemyUnit.All.ToArray())
        {
            if (e == null || e.dead) continue;
            if (Mathf.Abs(e.Lane - p.lane) > laneTol) continue;
            float dx = e.transform.position.x - p.worldX;
            if (frontal && dx * p.facing < -0.5f) continue;
            if (Mathf.Abs(dx) > maxDx) continue;
            e.TakeDamage(dmg, dx >= 0f ? 1 : -1);
            if (stun > 0f) e.Stun(stun);
        }
    }

    // Boss 不在 EnemyUnit.All 里（状态机/硬直窗口不同），单独判一次
    void HitBosses(float dmg, float range, bool stun, float stunSec, bool frontOnly)
    {
        foreach (var b in FindObjectsOfType<BossUnit>())
        {
            if (b == null || b.dead) continue;
            if (Mathf.Abs(b.Lane - p.lane) > 1) continue;
            float dx = b.transform.position.x - p.worldX;
            if (frontOnly && dx * p.facing < -1.2f) continue;
            if (Mathf.Abs(dx) <= range)
            {
                b.TakeDamage(dmg, dx >= 0f ? 1 : -1);
                if (stun) b.Stun(stunSec);
            }
        }
    }

    // —— 总调度 ——
    public void Fire(SkillDef s)
    {
        p.PlaySkill(s.art);
        switch (s.effect)
        {
            case SkillEffect.Sweep: CastSweep(s); break;
            case SkillEffect.Bolt:  CastBolt(s);  break;
            case SkillEffect.Dash:  CastDash(s);  break;
            case SkillEffect.Aoe:   CastAoe(s);   break;
            case SkillEffect.Nova:  CastNova(s);  break;
            case SkillEffect.Heal:  CastHeal(s);  break;
            case SkillEffect.Buff:  CastBuff(s);  break;
        }
    }

    // 角色脚底：WZ 技能特效的 origin 就是相对角色原点（脚底）存的，
    // 1:1 复刻下锚点直接落在 FeetPos，特效位置和原版逐像素一致
    Vector3 CastAt() => new Vector3(p.worldX, p.FeetPos.y, 0f);

    // 身前扇形 AoE
    void CastSweep(SkillDef s)
    {
        SkillFx.Play(s.art, CastAt(), p.facing);
        HitFeel.Punch(1.2f);
        HitMobs(Dmg(s), s.range, 1, true);
        HitBosses(Dmg(s), s.range, false, 0f, true);
    }

    // 远程直线打击（带飞行特效，命中沿途敌人）
    void CastBolt(SkillDef s)
    {
        SkillFx.Play(s.art, CastAt(), p.facing);
        HitFeel.Punch(1.0f);
        HitMobs(Dmg(s), s.range, 1, true, s.extra > 0f ? s.extra : 0f);
        HitBosses(Dmg(s), s.range, s.extra > 0f, s.extra, true);
        StartCoroutine(BoltFx(s));
    }

    IEnumerator BoltFx(SkillDef s)
    {
        float t = 0f, dur = 0.22f;
        float x0 = p.worldX + p.facing * 0.4f;
        float x1 = p.worldX + p.facing * s.range;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = t / dur;
            Vector3 pos = new Vector3(Mathf.Lerp(x0, x1, k), p.FeetPos.y + 0.9f, 0f);
            var sr = Fx.Quad("Bolt", pos, FxTex.Glow(), new Vector2(0.5f, 0.5f), s.color, 360);
            sr.gameObject.AddComponent<FxLife>().life = 0.12f;
            yield return null;
        }
    }

    // 突进 + 无敌帧
    void CastDash(SkillDef s)
    {
        StartCoroutine(DashRoutine(s));
    }

    IEnumerator DashRoutine(SkillDef s)
    {
        float from = p.worldX;
        float to = PlayerCtrl.ClampX(p.worldX + p.facing * s.range);
        p.invulnUntil = Time.unscaledTime + 0.35f;
        SkillFx.Play(s.art, CastAt(), p.facing);

        float t = 0f;
        while (t < 0.16f)
        {
            t += Time.deltaTime;
            p.worldX = Mathf.Lerp(from, to, t / 0.16f);
            yield return null;
        }
        p.worldX = to;
        HitFeel.Punch(1.3f);
        foreach (var e in EnemyUnit.All.ToArray())
        {
            if (e == null || e.dead) continue;
            if (Mathf.Abs(e.Lane - p.lane) > 1) continue;
            if (e.transform.position.x > Mathf.Min(from, to) && e.transform.position.x < Mathf.Max(from, to))
                e.TakeDamage(Dmg(s), p.facing);
        }
        HitBosses(Dmg(s), Mathf.Abs(to - from) * 0.5f + 1.5f, false, 0f, false);
    }

    // 震地：范围 + 眩晕
    void CastAoe(SkillDef s)
    {
        SkillFx.Play(s.art, CastAt(), p.facing);
        HitFeel.Punch(1.6f);
        HitMobs(Dmg(s), s.range, 2, false, s.extra);
        HitBosses(Dmg(s), s.range, s.extra > 0f, s.extra, false);
    }

    // 全屏爆发
    void CastNova(SkillDef s)
    {
        SkillFx.Play(s.art, CastAt(), p.facing);
        HitFeel.Punch(2.2f);
        foreach (var e in EnemyUnit.All.ToArray())
        {
            if (e == null || e.dead) continue;
            e.TakeDamage(Dmg(s), e.transform.position.x >= p.worldX ? 1 : -1);
        }
        HitBosses(Dmg(s), 999f, false, 0f, false);
    }

    // 自我治疗：原版圣光/群体治愈也有自己的 effect 帧，一并播放
    void CastHeal(SkillDef s)
    {
        if (!string.IsNullOrEmpty(s.art)) SkillFx.Play(s.art, CastAt(), p.facing);
        p.Heal(s.extra);
        Fx.Burst(new Vector3(p.worldX, p.FeetPos.y + 1.0f, 0f), 1.1f, s.color, 0.4f);
        DamageText.Show(new Vector3(p.FeetPos.x, p.FeetPos.y + 2.55f, 0f), s.extra, "blue", 0.9f);
    }

    // 自我增益（攻击力）：愤怒之火等 buff 技能同样带原版 effect 帧
    void CastBuff(SkillDef s)
    {
        if (!string.IsNullOrEmpty(s.art)) SkillFx.Play(s.art, CastAt(), p.facing);
        p.AddAtkBuff(1.6f, s.extra);
        Fx.Ring(new Vector3(p.worldX, p.FeetPos.y + 0.4f, 0f), 1.4f, s.color, 0.35f);
    }

    // 兼容旧接口（若有其它地方直接调用）
    Vector3 Anchor => (Vector3)p.FeetPos + Vector3.up * (21f / 40f);
}

