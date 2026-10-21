using System;
using UnityEngine;

// 单一快照保存，迁移时保留旧 mxd.* 键。PlayerPrefs 仅保存本机数据。
public static class LocalProfileStore
{
    const string Snapshot = "mxd.profile.v1";
    const string Backup = "mxd.profile.v1.backup";

    static PlayerProfileData Read(string key)
    {
        if (!PlayerPrefs.HasKey(key)) return null;
        try
        {
            var value = JsonUtility.FromJson<PlayerProfileData>(PlayerPrefs.GetString(key));
            return value != null && value.version == 1 && value.owned != null ? value : null;
        }
        catch (ArgumentException) { return null; }
    }

    public static PlayerProfileData Load()
    {
        var data = Read(Snapshot) ?? Read(Backup);
        if (data != null) return data;
        return new PlayerProfileData
        {
            gold = PlayerPrefs.GetInt("mxd.gold", 0),
            cls = PlayerPrefs.GetString("mxd.cls", "knight"),
            equipWeapon = PlayerPrefs.GetString("mxd.equip", "w_sword_basic"),
            hair = PlayerPrefs.GetString("mxd.hair", "h0"),
            outfit = PlayerPrefs.GetString("mxd.outfit", "none"),
            owned = new System.Collections.Generic.List<string>(
                PlayerPrefs.GetString("mxd.owned", "w_sword_basic").Split(','))
        };
    }

    public static bool Save(PlayerProfileData data)
    {
        try
        {
            if (Read(Snapshot) != null)
                PlayerPrefs.SetString(Backup, PlayerPrefs.GetString(Snapshot));
            PlayerPrefs.SetString(Snapshot, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError("[Profile] 本地存档保存失败：" + ex.Message);
            return false;
        }
    }
}
