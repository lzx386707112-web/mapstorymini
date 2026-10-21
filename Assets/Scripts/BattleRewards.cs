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
        if (amount > 0) MesoDrop.Spawn(amount, position);
    }
}

// 本地掉落实体：击杀不直接加钱，接近后按 Z 拾取；移动端 HUD 提供同一入口。
public sealed class MesoDrop : MonoBehaviour
{
    public static readonly List<MesoDrop> All = new List<MesoDrop>();
    int amount;
    double expires;
    public static void Spawn(int amount, Vector3 position)
    {
        var sprite = SpriteFactory.Make("MesoDrop", null, new Vector2(0.25f, 0.25f), Color.yellow, 290);
        sprite.transform.position = position + Vector3.up * 0.15f;
        var drop = sprite.gameObject.AddComponent<MesoDrop>();
        drop.amount = amount;
        drop.expires = Time.realtimeSinceStartupAsDouble + 180;
    }
    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);
    void Update()
    {
        if (Time.realtimeSinceStartupAsDouble >= expires) { Destroy(gameObject); return; }
    }
    public static void TryPickupNearest()
    {
        var game = GameLoop.I;
        if (game == null || game.state != GameLoop.GameState.Playing || game.gameOver || game.player == null) return;
        MesoDrop best = null;
        float distance = 1.2f * 1.2f;
        foreach (var drop in All)
        {
            if (!drop.enabled || drop.amount <= 0) continue;
            float d = ((Vector2)drop.transform.position - game.player.FeetPos).sqrMagnitude;
            if (d <= distance) { distance = d; best = drop; }
        }
        if (best == null || (long)Profile.gold + best.amount > int.MaxValue) return;
        Profile.AddGold(best.amount);
        best.amount = 0;
        best.enabled = false;
        Destroy(best.gameObject);
    }
    public static void Clear()
    {
        foreach (var drop in All.ToArray()) if (drop != null) { drop.enabled = false; Destroy(drop.gameObject); }
        All.Clear();
    }
}
