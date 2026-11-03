using System.Collections.Generic;
using UnityEngine;

// 机器人（Android）纸娃娃外观 —— 源端 CAvatar 给机器人用的是一套独立部件集：
//   Character/000020NN.img     肤色身体（与玩家共用同一份原画，肤色号 2000..2015）
//   Character/000120NN.img     头（= 肤色号 + 10000，与 CharacterIds 同一口径）
//   Character/Face/*.img       脸：模板 face 池里挑一张
//   Character/Hair/*.img       发：模板 hair 池里挑一张
//   basic/<槽>                 模板自带的基本穿着（cap/cape/accessory/longcoat/shoes/gloves）
//   AP 槽 1200..1213           玩家手动换上的时装，覆盖模板默认穿着
//
// 282 客户端的 hair/face 使用完整 id（余数是旧265协议）；本项目导出的模板同样保留完整 id。
// 本层只做两件事：
//   1) 拼成 WzCharacter 认识的 8 位 id 数组（body 必须第一个、weapon 必须最后）；
//   2) 对资源缺失的部件做确定性回退。
//
// 回退为什么必须做：282 有 3 个肤色（2012/2013/2015，body+head 一起缺）、
// 一组发型（30430..30437）和 2 件脸饰（1012832/1022376）在 GMS083 资源树里
// 没有同名 img。硬拼的话 Placements 会在 Equipment() 里拿到 null，整只机器人不显示。
public static class AndroidLook
{
    /// <summary>AndroidState → 8 位 id 数组；连身体都取不到时返回 null（不显示）。</summary>
    public static string[] Ids(AndroidState state)
    {
        if (state == null || state.templateId == 0) return null;
        int skin = Skin(state);
        if (skin == 0) return null;

        var ids = new List<string>(12)
        {
            skin.ToString("D8"),                 // body：必须是第一项（Placements 从 ids[0] 取身体与动作帧）
            (skin + 10000).ToString("D8"),       // head
        };
        int face = Available(state.templateId, "face", state.face);
        if (face > 0) ids.Add(face.ToString("D8"));
        int hair = Available(state.templateId, "hair", state.hair);
        if (hair > 0) ids.Add(hair.ToString("D8"));

        // 套服（105）自带裤裙；此时再叠一件下装会把两片裤腿重起来。
        bool longcoat = state.top / 10000 == 105;
        Add(ids, state.hat);
        Add(ids, state.faceAccessory);
        Add(ids, state.top);
        if (!longcoat) Add(ids, state.bottom);
        Add(ids, state.shoes);
        Add(ids, state.gloves);
        Add(ids, state.cape);

        // weapon 必须最后：Pose()/Placements() 都按最后一项取武器的 info/stand、info/walk 与攻击姿势。
        // 机器人没装武器时不补占位——缺 info/stand 时 Int 会落到默认 1，still stand1/walk1。
        Add(ids, state.weapon);
        return ids.ToArray();
    }

    /// <summary>配方 key：外观变了 key 就变，SpriteAnim 换 key 即整套重建。</summary>
    public static string Key(string[] ids) => "android_" + string.Join("_", ids);

    /// <summary>Character/&lt;目录&gt;/&lt;8位id&gt;.img 是否存在（与 WzCharacter.Equipment 同一映射）。</summary>
    public static bool Exists(int itemId)
    {
        if (itemId <= 0) return false;
        // 资源集运行期不会变，缓存结果：Ids() 每 0.2s 的装备轮询都要问十几次，
        // 编辑器下每次都是 File.Exists 系统调用。
        if (exists.TryGetValue(itemId, out bool known)) return known;
        string dir = DirOf(itemId);
        bool found = dir != null &&
            WzAssets.HasFile("Character/" + (dir.Length == 0 ? "" : dir + "/") + itemId.ToString("D8") + ".img");
        exists[itemId] = found;
        return found;
    }

    static readonly Dictionary<int, bool> exists = new Dictionary<int, bool>();

    static string DirOf(int itemId)
    {
        int type = itemId / 10000;
        switch (type)
        {
            case 0: case 1: return "";            // 身体/头在 Character 根目录
            case 2: case 5: return "Face";
            case 3: case 4: case 6: return "Hair";
        }
        // 其余（100 帽 / 104 上衣 / 105 套服 / 106 裤裙 / 107 鞋 / 108 手套 / 110 披风 /
        //        101..103 脸饰眼饰耳饰 / 武器段）与图标目录同一张表。
        return EquipCatalog.DirOf(itemId);
    }

    /// <summary>肤色：模板池里挑中的那个若缺 body 或 head，回落到 2000（源端第一个白人肤色）。</summary>
    static int Skin(AndroidState state)
    {
        int skin = state.skin >= 2000 && state.skin < 3000 ? state.skin : NativeAndroidService.FallbackSkin;
        if (Exists(skin) && Exists(skin + 10000)) return skin;
        int fallback = NativeAndroidService.FallbackSkin;
        return Exists(fallback) && Exists(fallback + 10000) ? fallback : 0;
    }

    /// <summary>
    /// 从模板池里取一个真正存在的部件：以服务端摇出的那一项为起点沿池向后找，
    /// 绕一圈都没有就放弃这个部位。同角色 + 同资源集 → 结果恒定（不是随机）。
    /// </summary>
    static int Available(int templateId, string key, int picked)
    {
        var pool = NativeAndroidService.Pool(templateId, key);
        if (pool == null || pool.Count == 0) return Exists(picked) ? picked : 0;
        int start = pool.IndexOf(picked);
        if (start < 0) start = 0;
        for (int i = 0; i < pool.Count; i++)
        {
            int candidate = pool[(start + i) % pool.Count];
            if (Exists(candidate)) return candidate;
        }
        return Exists(picked) ? picked : 0;
    }

    static void Add(List<string> ids, int itemId)
    {
        if (itemId > 0 && Exists(itemId)) ids.Add(itemId.ToString("D8"));
    }
}
