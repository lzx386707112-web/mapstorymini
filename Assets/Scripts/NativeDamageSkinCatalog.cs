using System;
using System.Collections.Generic;
using UnityEngine;

// Source: DamageSkinCatalog.java / DamageSkinService.java. Names and ids are
// exported by tools/export_damage_skin_catalog.py, never inferred from filenames.
public static class NativeDamageSkinCatalog
{
    static Dictionary<string, object> catalog;
    static Dictionary<string, object> Catalog
    {
        get
        {
            if (catalog == null)
            {
                var asset = Resources.Load<TextAsset>("DamageSkinCatalog");
                if (asset == null) throw new InvalidOperationException("Missing DamageSkinCatalog");
                catalog = (Dictionary<string, object>)MiniJson.Parse(asset.text);
            }
            return catalog;
        }
    }

    public static bool Contains(int id) => Catalog.ContainsKey(id.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public static string Name(int id) => Catalog.TryGetValue(id.ToString(System.Globalization.CultureInfo.InvariantCulture), out var name)
        ? (string)name : "未知伤害皮肤";
    public static int[] Ids()
    {
        var ids = new List<int>();
        foreach (var key in Catalog.Keys) ids.Add(int.Parse(key, System.Globalization.CultureInfo.InvariantCulture));
        ids.Sort();
        return ids.ToArray();
    }
    public static int Selected
    {
        get
        {
            int id = GameSession.Character?.record.damageSkinId ?? 0;
            return Contains(id) ? id : 0;
        }
    }
    public static bool Select(int id)
    {
        if (!GameSession.InGame || GameSession.Character == null || !Contains(id)) return false;
        int previous = GameSession.Character.record.damageSkinId;
        GameSession.Character.record.damageSkinId = id;
        try { GameSession.SaveToDb(); }
        catch (Exception error)
        {
            GameSession.Character.record.damageSkinId = previous;
            Debug.LogError("Unable to save damage skin " + id + ": " + error);
            return false;
        }
        NativeDamageNumber.ResetSkinFailure();
        return true;
    }
}
