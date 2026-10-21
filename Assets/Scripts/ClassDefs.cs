using UnityEngine;

// 职业技能槽只映射名称/美术；数值和效果来自原 WZ 等级配置。
[System.Serializable]
public struct ClassSkill
{
    public string name;
    public string art;
    public string icon;
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
                S("防御崩坏", "crush"),
                S("突进", "dash"),
                S("虎咆哮", "roar"),
                S("剑影分身", "illusion"),
            }
        },
        new ClassDef {
            id = "warrior", name = "狂战士", artKey = "warrior", accent = new Color(0.90f,0.45f,0.30f),
            skills = new[] {
                S("愤怒之火", "rage"),
                S("斗气死亡断层", "rupture"),
                S("磁石", "magnet"),
                S("狂暴攻击", "berserk"),
            }
        },
        new ClassDef {
            id = "mage", name = "魔法师", artKey = "mage", accent = new Color(0.55f,0.80f,1.00f),
            skills = new[] {
                S("火焰箭", "firearrow"),
                S("末日烈焰", "doom"),
                S("连环爆破", "chain"),
                S("天降落星", "starfall"),
            }
        },
        new ClassDef {
            id = "archer", name = "弓箭手", artKey = "archer", accent = new Color(0.65f,0.90f,0.50f),
            skills = new[] {
                S("烈火箭", "firebolt"),
                S("箭雨", "arrowrain"),
                S("飞龙冲击波", "dragon"),
                S("骤雨狂矢", "storm"),
            }
        },
        new ClassDef {
            id = "priest", name = "牧师", artKey = "priest", accent = new Color(0.80f,0.95f,0.70f),
            skills = new[] {
                S("圣箭术", "holyarrow"),
                S("圣光", "holy"),
                S("光芒飞箭", "ray"),
                S("天秤之使", "libra"),
            }
        },
    };

    static ClassSkill S(string name, string art)
        => new ClassSkill { name = name, art = art };

    public static ClassDef Get(string id)
    {
        foreach (var d in All) if (d.id == id) return d;
        return All[0];
    }

    public static int Count => All.Length;
    public static ClassDef At(int i) => All[i];
}
