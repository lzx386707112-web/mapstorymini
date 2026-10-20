using UnityEditor;
using UnityEngine;

// 大厅数据/素材自检：不进 Play，直接验证 ClassDefs / WeaponDefs / Profile / Art / CosmeticFactory
// 在运行时不会抛异常，且素材齐全。
public class LobbyCheck
{
    public static void Run()
    {
        Profile.Load();
        Debug.Log("[Check] 职业数=" + ClassDefs.Count + "  武器数=" + WeaponDefs.Count);

        for (int i = 0; i < ClassDefs.Count; i++)
        {
            var d = ClassDefs.At(i);
            bool hasArt = Art.Has("chars", d.artKey) && Art.GetClip("chars", d.artKey, "stand") != null;
            string skills = "";
            for (int j = 0; j < d.skills.Length; j++)
                skills += d.skills[j].name + "(" + d.skills[j].effect + "," + d.skills[j].cd + "s) ";
            Debug.Log("[Check] 职业 " + d.id + " 素材=" + (hasArt ? "OK" : "缺失") + " 技能=" + skills);
        }

        for (int i = 0; i < WeaponDefs.Count; i++)
        {
            var w = WeaponDefs.At(i);
            Debug.Log("[Check] 武器 " + w.id + " 价=" + w.price + " 类型=" + w.kind +
                      " 伤害x" + w.dmgMul + " 攻速x" + w.intervalMul + " 距离" + w.reachAdd);
        }

        // 装扮贴图：用真实角色帧验证染色/武器贴图在运行时不抛异常
        var clip = Art.GetClip("chars", "knight", "stand");
        Sprite frame = clip != null && clip.frames.Length > 0 ? Art.Frame(clip.frames[0]) : null;
        var hair = CosmeticFactory.DyeHair(frame, CosmeticFactory.HairColor("blue"));
        var coat = CosmeticFactory.DyeCoat(frame, CosmeticFactory.OutfitColor("red"));
        Debug.Log("[Check] 装扮 头发=" + (hair != null) + " 服饰=" + (coat != null));
        foreach (WeaponKind k in System.Enum.GetValues(typeof(WeaponKind)))
            Debug.Log("[Check] 武器贴图 " + k + " = " + (CosmeticFactory.WeaponSprite(k, new Color(0.8f,0.8f,0.9f)) != null));

        // 金币流程模拟
        int g0 = Profile.gold;
        Profile.AddGold(50); Profile.AddGold(15);
        bool buy = Profile.Spend(120);
        Debug.Log("[Check] 金币流程 起始=" + g0 + " +65后=" + Profile.gold + " 花120=" + buy + " 余=" + Profile.gold);

        Debug.Log("[Check] 完成");
        EditorApplication.Exit(0);
    }
}
