using UnityEngine;

// 职业 + 技能定义。每个职业 4 个技能，效果类型与数值都在这里配，
// SkillSystem 只负责按 effect 类型播放，不写死任何职业逻辑。
//
// dmg 是「相对玩家普攻的倍数」：玩家基础攻击约 11，3.0 就是单次约 33 伤害。
public enum SkillEffect
{
    Sweep,   // 身前扇形 AoE
    Bolt,    // 远程直线打击（可贯穿）
    Dash,    // 突进 + 无敌帧
    Aoe,     // 震地：范围 + 眩晕
    Nova,    // 全屏爆发
    Heal,    // 自我治疗
    Buff,    // 自我增益（攻击力 / 护盾）
}

[System.Serializable]
public struct ClassSkill
{
    public string name;
    public SkillEffect effect;
    public Color color;
    public float cd;
    public float dmg;     // 普攻倍数
    public float range;   // 作用距离（世界单位）
    public float extra;   // 眩晕/治疗量/增益时长等附加参数
    public string hint;
    public string art;    // skills/<art>/ 下的真实特效+图标（flame/dash/rupture/illusion），空=仅程序化特效
    public string icon;   // 独立图标 key（skills/<icon>/icon.png，每技能一张原版图标）；空=复用 art
}

[System.Serializable]
public struct ClassDef
{
    public string id;
    public string name;
    public string artKey;     // chars/<artKey> 素材
    public Color accent;
    public ClassSkill[] skills;
}

public static class ClassDefs
{
    static readonly ClassDef[] All = new ClassDef[]
    {
        new ClassDef {
            id = "knight", name = "骑士", artKey = "knight", accent = new Color(0.95f, 0.62f, 0.28f),
            skills = new[] {
                // 每格 = 原版同一个技能 id 的图标+名字+特效（tools/wz_skill.py 同步）
                S("防御崩坏", SkillEffect.Sweep, new Color(0.98f,0.62f,0.28f), 6f, 2.8f, 4.2f, 0f, "身前横扫", "crush"),
                S("突进",     SkillEffect.Dash,  new Color(0.55f,0.82f,0.98f), 12f, 2.0f, 4.4f, 0f, "向前突进·无敌", "dash"),
                S("虎咆哮",   SkillEffect.Aoe,   new Color(0.86f,0.44f,0.35f), 14f, 3.0f, 4.0f, 1.2f, "近身震击·眩晕", "roar"),
                S("剑影分身", SkillEffect.Nova,  new Color(1f,0.42f,0.42f), 40f, 4.5f, 9f, 0f, "全屏剑气", "illusion"),
            }
        },
        new ClassDef {
            id = "warrior", name = "狂战士", artKey = "warrior", accent = new Color(0.90f,0.45f,0.30f),
            skills = new[] {
                S("愤怒之火", SkillEffect.Buff,  new Color(0.95f,0.55f,0.30f), 30f, 0f, 0f, 6f, "攻+60% 6秒", "rage"),
                S("斗气死亡断层", SkillEffect.Sweep, new Color(0.90f,0.45f,0.30f), 8f, 3.2f, 4.6f, 0f, "斗气斩击", "rupture"),
                S("磁石",     SkillEffect.Aoe,   new Color(0.80f,0.40f,0.30f), 16f, 2.6f, 4.6f, 1.0f, "吸附·眩晕", "magnet"),
                S("狂暴攻击", SkillEffect.Nova,  new Color(0.95f,0.30f,0.30f), 40f, 4.5f, 10f, 0f, "全屏爆发", "berserk"),
            }
        },
        new ClassDef {
            id = "mage", name = "魔法师", artKey = "mage", accent = new Color(0.55f,0.80f,1.00f),
            skills = new[] {
                S("火焰箭",   SkillEffect.Bolt, new Color(1f,0.50f,0.30f), 5f, 3.0f, 9f, 0f, "远程火弹", "firearrow"),
                S("末日烈焰", SkillEffect.Aoe,  new Color(0.55f,0.80f,1.00f), 12f, 3.0f, 4.6f, 1.2f, "爆炸·眩晕", "doom"),
                S("连环爆破", SkillEffect.Sweep,new Color(0.70f,0.90f,1.00f), 18f, 3.4f, 4.6f, 0f, "连环轰击", "chain"),
                S("天降落星", SkillEffect.Nova, new Color(0.60f,0.85f,1.00f), 38f, 4.5f, 10f, 0f, "全屏流星", "starfall"),
            }
        },
        new ClassDef {
            id = "archer", name = "弓箭手", artKey = "archer", accent = new Color(0.65f,0.90f,0.50f),
            skills = new[] {
                S("烈火箭",   SkillEffect.Bolt, new Color(0.60f,0.90f,0.50f), 5f, 2.8f, 10f, 0f, "火箭远射", "firebolt"),
                S("箭雨",     SkillEffect.Aoe,  new Color(0.70f,0.85f,0.60f), 14f, 3.0f, 5.0f, 1.0f, "落箭·眩晕", "arrowrain"),
                S("飞龙冲击波", SkillEffect.Sweep, new Color(0.50f,0.85f,0.40f), 20f, 3.6f, 5.0f, 0f, "龙气冲击", "dragon"),
                S("骤雨狂矢", SkillEffect.Nova, new Color(0.80f,0.95f,0.60f), 38f, 4.2f, 11f, 0f, "全屏箭雨", "storm"),
            }
        },
        new ClassDef {
            id = "priest", name = "牧师", artKey = "priest", accent = new Color(0.80f,0.95f,0.70f),
            skills = new[] {
                S("圣箭术",   SkillEffect.Bolt, new Color(1.00f,0.95f,0.60f), 5f, 2.6f, 9f, 0f, "圣光弹", "holyarrow"),
                S("圣光",     SkillEffect.Heal, new Color(0.60f,1.00f,0.70f), 10f, 0f, 0f, 35f, "回血35", "holy"),
                S("光芒飞箭", SkillEffect.Sweep,new Color(0.90f,0.95f,0.70f), 16f, 3.0f, 4.6f, 0f, "光矢横扫", "ray"),
                S("天秤之使", SkillEffect.Nova, new Color(0.80f,1.00f,0.90f), 36f, 4.2f, 10f, 0f, "天使审判", "libra"),
            }
        },
    };

    static ClassSkill S(string name, SkillEffect e, Color c, float cd, float dmg, float range, float extra, string hint, string art = "", string icon = "")
        => new ClassSkill { name = name, effect = e, color = c, cd = cd, dmg = dmg, range = range, extra = extra, hint = hint, art = art, icon = icon };

    public static ClassDef Get(string id)
    {
        foreach (var d in All) if (d.id == id) return d;
        return All[0];
    }

    public static int Count => All.Length;
    public static ClassDef At(int i) => All[i];
}
