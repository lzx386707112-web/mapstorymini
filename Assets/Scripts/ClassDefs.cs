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
                S("烈焰翔斩", SkillEffect.Sweep, new Color(0.98f,0.62f,0.28f), 6f, 3.0f, 4.6f, 0f, "身前火焰横扫", "flame"),
                S("突进",     SkillEffect.Dash,  new Color(0.55f,0.82f,0.98f), 12f, 2.0f, 4.4f, 0f, "向前突进·无敌", "dash"),
                S("斗气死亡断层", SkillEffect.Aoe, new Color(0.86f,0.44f,0.35f), 20f, 3.5f, 4.0f, 1.2f, "震地·眩晕", "rupture"),
                S("剑影分身", SkillEffect.Nova,  new Color(1f,0.42f,0.42f), 40f, 4.5f, 9f, 0f, "全屏剑气", "illusion"),
            }
        },
        new ClassDef {
            id = "warrior", name = "狂战士", artKey = "warrior", accent = new Color(0.90f,0.45f,0.30f),
            skills = new[] {
                S("旋风斩",   SkillEffect.Sweep, new Color(0.95f,0.55f,0.30f), 6f, 2.6f, 3.6f, 0f, "贴身旋风", "flame"),
                S("冲锋",     SkillEffect.Dash,  new Color(0.90f,0.75f,0.40f), 11f, 2.4f, 5.2f, 0f, "猛冲·无敌", "dash"),
                S("震地裂",   SkillEffect.Aoe,   new Color(0.80f,0.40f,0.30f), 18f, 3.2f, 4.6f, 1.5f, "砸地·长眩", "rupture"),
                S("狂暴",     SkillEffect.Buff,  new Color(0.95f,0.30f,0.30f), 35f, 0f, 0f, 6f, "攻+60% 6秒"),
            }
        },
        new ClassDef {
            id = "mage", name = "魔法师", artKey = "mage", accent = new Color(0.55f,0.80f,1.00f),
            skills = new[] {
                S("火球",     SkillEffect.Bolt, new Color(1f,0.50f,0.30f), 6f, 3.2f, 9f, 0f, "远程火球", "flame"),
                S("冰封",     SkillEffect.Bolt, new Color(0.50f,0.80f,1.00f), 12f, 2.4f, 9f, 1.2f, "冻缓·眩晕", "flame"),
                S("闪电链",   SkillEffect.Nova, new Color(0.70f,0.90f,1.00f), 20f, 3.6f, 9f, 0f, "全屏雷击", "illusion"),
                S("魔法盾",   SkillEffect.Buff, new Color(0.60f,0.90f,0.90f), 30f, 0f, 0f, 40f, "回血40"),
            }
        },
        new ClassDef {
            id = "archer", name = "弓箭手", artKey = "archer", accent = new Color(0.65f,0.90f,0.50f),
            skills = new[] {
                S("箭矢",     SkillEffect.Bolt, new Color(0.60f,0.90f,0.50f), 5f, 2.6f, 10f, 0f, "远程箭", "flame"),
                S("后撤步",   SkillEffect.Dash, new Color(0.70f,0.85f,0.60f), 10f, 1.2f, 4f, 0f, "后跳·无敌", "dash"),
                S("贯穿箭",   SkillEffect.Bolt, new Color(0.50f,0.85f,0.40f), 18f, 3.4f, 12f, 1f, "贯穿远射", "flame"),
                S("箭雨",     SkillEffect.Nova, new Color(0.80f,0.95f,0.60f), 38f, 4.0f, 11f, 0f, "全屏箭雨", "illusion"),
            }
        },
        new ClassDef {
            id = "priest", name = "牧师", artKey = "priest", accent = new Color(0.80f,0.95f,0.70f),
            skills = new[] {
                S("治愈",     SkillEffect.Heal, new Color(0.60f,1.00f,0.70f), 6f, 0f, 0f, 30f, "回血30"),
                S("圣光",     SkillEffect.Bolt, new Color(1.00f,0.95f,0.60f), 12f, 2.8f, 9f, 0f, "远程圣光"),
                S("神圣场",   SkillEffect.Aoe,  new Color(0.90f,0.95f,0.70f), 20f, 3.0f, 4.6f, 1.0f, "光环·眩晕"),
                S("庇护",     SkillEffect.Buff, new Color(0.80f,1.00f,0.90f), 34f, 0f, 0f, 8f, "攻+50% 8秒"),
            }
        },
    };

    static ClassSkill S(string name, SkillEffect e, Color c, float cd, float dmg, float range, float extra, string hint, string art = "")
        => new ClassSkill { name = name, effect = e, color = c, cd = cd, dmg = dmg, range = range, extra = extra, hint = hint, art = art };

    public static ClassDef Get(string id)
    {
        foreach (var d in All) if (d.id == id) return d;
        return All[0];
    }

    public static int Count => All.Length;
    public static ClassDef At(int i) => All[i];
}
