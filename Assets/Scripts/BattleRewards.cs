using System;
using System.Collections.Generic;
using UnityEngine;

// 单机会话内死亡结算去重。奖励以显式原怪物 ID 对应的数据为准。
public sealed class DeathSettlement
{
    readonly HashSet<int> settled = new HashSet<int>();
    public bool TrySettle(int entityId) => settled.Add(entityId);
    public void Clear() => settled.Clear();
}

public static class RewardRules
{
    // MapleMap.dropFromMonster: MESOUP truncates before the float rate product.
    public static int MesoAmount(int quantity, int? mesoUp, double rate)
    {
        if (quantity<=0) return 0;
        if (mesoUp.HasValue) quantity=(int)Math.Min(int.MaxValue,quantity*(mesoUp.Value/100.0));
        float scaled=quantity*(float)rate;
        int result=scaled>=int.MaxValue ? int.MaxValue : (int)scaled;
        return result<=0 ? int.MaxValue : result;
    }

    // Monster.getStatusExpMultiplier; default config gives solo HS one fifth strength.
    public static float HolySymbolMultiplier(int percent, bool partySharers, bool full)
        => (float)(1.0+percent/(partySharers || full ? 100.0 : 500.0));

    // MapleMap 使用 nextInt(999999)，金币数量区间上界不包含。
    public static int RollMeso(int chance, int minimum, int maximum, int chanceRoll, int quantityRoll)
    {
        if (chanceRoll < 0 || chanceRoll >= 999999 || chanceRoll >= chance || minimum < 0 || maximum <= minimum) return 0;
        if (quantityRoll < 0 || quantityRoll >= maximum - minimum) return 0;
        return minimum + quantityRoll;
    }
}

public static class BattleRewards
{
    static Dictionary<string, Dictionary<string, object>> mobs;
    public static void Drop(string artKey, Vector3 position)
    {
        if (mobs == null)
        {
            mobs = new Dictionary<string, Dictionary<string, object>>();
            var asset = Resources.Load<TextAsset>("GameplayRewards");
            if (asset == null) throw new InvalidOperationException("缺少 GameplayRewards 数据");
            var root = (Dictionary<string, object>)MiniJson.Parse(asset.text);
            foreach (Dictionary<string, object> mob in (List<object>)root["mobs"])
                mobs.Add((string)mob["key"], mob);
        }
        if (!mobs.TryGetValue(artKey, out var entry)) return; // 自定义怪物没有原端映射，不能猜奖励。
        Profile.GrantExperience((int)Art.Num(entry, "exp"));
        var meso = (Dictionary<string, object>)entry["meso"];
        int min = (int)Art.Num(meso, "minimum"), max = (int)Art.Num(meso, "maximum");
        int amount = RewardRules.RollMeso((int)Art.Num(meso, "chance"), min, max,
            UnityEngine.Random.Range(0, 999999), max > min ? UnityEngine.Random.Range(0, max - min) : 0);
        if (amount > 0) GroundItem.Spawn(0, amount, position);
    }
}
