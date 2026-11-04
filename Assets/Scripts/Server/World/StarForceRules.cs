using System;
using System.Collections.Generic;

// TMS282.5 GameConstants + enhance/starforce.tsv. All probabilities use per-10000.
// Caps: verified TMS282 client A08930 and InstanceTableWZ2 (clientCaps in JSON).
// User override: two consecutive star drops guarantee the following attempt.
public sealed class StarForceRules
{
    public sealed class Rate
    {
        public int success, destroy;
        public bool down;
        public long meso;
    }
    public sealed class Quote
    {
        public long cost;
        public int success, destroy;
        public bool chanceTime, safeguard;
    }
    public enum Outcome { Success = 1, Destroy = 2, Maintain = 3 }
    public sealed class Result
    {
        public Outcome outcome;
        public int stars, dropStreak;
        public bool vestige;
    }
    readonly Rate[] normal, superior;
    readonly Dictionary<string, object> itemCaps;
    readonly int[] levelMinimums, normalCaps, superiorCaps;
    static StarForceRules shared;
    public static StarForceRules Shared => shared ??= new StarForceRules(PlatformRes.LoadText("Enhancement/StarForce"));
    public StarForceRules(string json)
    {
        var root = MiniJsonDb.Parse(json ?? "") as Dictionary<string, object>;
        if (root == null || Number(root, "schemaVersion") != 1 || Number(root, "scale") != 10000)
            throw new InvalidOperationException("282 星力规则数据缺失或版本错误");
        normal = ReadRates(root, "normal", 30);
        superior = ReadRates(root, "superior", 15);
        var caps = (Dictionary<string, object>)root["clientCaps"];
        itemCaps = (Dictionary<string, object>)caps["itemCaps"];
        levelMinimums = ReadInts(caps, "levelMinimums");
        normalCaps = ReadInts(caps, "normal");
        superiorCaps = ReadInts(caps, "superior");
        if (levelMinimums.Length != 6 || normalCaps.Length != 6 || superiorCaps.Length != 6)
            throw new InvalidOperationException("282 客户端星级上限表不完整");
    }
    static int Number(Dictionary<string, object> row, string key) => Convert.ToInt32(row[key]);
    static Rate[] ReadRates(Dictionary<string, object> root, string key, int count)
    {
        var list = (List<object>)root[key];
        if (list.Count != count) throw new InvalidOperationException("星力概率表不完整");
        var result = new Rate[count];
        foreach (Dictionary<string, object> row in list)
        {
            int star = Number(row, "star"), success = Number(row, "success"), destroy = Number(row, "destroy");
            long meso = Convert.ToInt64(row["meso"]);
            if (star < 0 || star >= count || result[star] != null || success < 0 || destroy < 0 || success + destroy > 10000 || meso < -1)
                throw new InvalidOperationException("星力概率表无效");
            result[star] = new Rate { success = success, destroy = destroy, down = Convert.ToBoolean(row["down"]), meso = meso };
        }
        return result;
    }
    static int[] ReadInts(Dictionary<string, object> root, string key)
        => ((List<object>)root[key]).ConvertAll(Convert.ToInt32).ToArray();
    // Client A08930: level = template reqLevel + instance iIncReq.
    public int Cap(int itemId, int level, bool isSuperior)
    {
        if (itemCaps.TryGetValue(itemId.ToString(System.Globalization.CultureInfo.InvariantCulture), out var cap))
            return Convert.ToInt32(cap);
        int bucket = 0;
        while (bucket + 1 < levelMinimums.Length && level >= levelMinimums[bucket + 1]) bucket++;
        return (isSuperior ? superiorCaps : normalCaps)[bucket];
    }
    Rate At(int star, bool isSuperior)
    {
        var table = isSuperior ? superior : normal;
        if (star < 0 || star >= table.Length) throw new ArgumentOutOfRangeException(nameof(star));
        return table[star];
    }
    public bool CanSafeguard(int star, bool isSuperior) => !isSuperior && star >= 15 && star <= 17;
    public long Cost(int itemId, int level, int star, bool isSuperior)
    {
        var row = At(star, isSuperior);
        if (level < 0 || level > 1000) throw new ArgumentOutOfRangeException(nameof(level));
        if (itemId == 1122444 || itemId == 1122445) return 0;
        if (row.meso >= 0) return row.meso;
        double cube = Math.Pow(level, 3), cost;
        if (isSuperior) cost = 1000 + Math.Pow(level, 3.56);
        else if (star < 10) cost = 1000 + cube * (star + 1) / 25.0;
        else
        {
            double divisor = star == 10 ? 400 : star == 11 ? 220 : star == 12 ? 150 : star == 13 ? 110 : star == 14 ? 75
                : star == 17 ? 150 : star == 18 ? 70 : star == 19 ? 45 : star == 21 ? 125 : 200;
            cost = 1000 + cube * Math.Pow(star + 1, 2.7) / divisor;
        }
        // Java Math.round for non-negative costs, not banker's rounding.
        return checked((long)Math.Floor(cost / 100 + 0.5) * 100);
    }
    public Quote Preview(int itemId, int level, int star, int dropStreak, bool isSuperior,
        bool safeguardUI = false, bool protectionScroll = false, int vipGrade = 0, bool miniGame = false)
    {
        if (star >= Cap(itemId, level, isSuperior)) throw new ArgumentException("装备已达到最大星级");
        if (dropStreak < 0 || vipGrade < 0 || vipGrade > 20) throw new ArgumentOutOfRangeException();
        if (safeguardUI && !CanSafeguard(star, isSuperior)) throw new ArgumentException("防破坏保护仅适用于普通装备15至17星");
        var row = At(star, isSuperior);
        bool chance = dropStreak >= 2;
        int success = row.success, destroy = row.destroy;
        if (vipGrade > 0) success = checked((int)Math.Floor(success * (1 + .025 * vipGrade) + .5));
        if (miniGame) success = (int)Math.Floor(success * 1.045 + .5);
        if (!isSuperior && (safeguardUI || protectionScroll)) destroy = 0;
        if (chance) { success = 10000; destroy = 0; }
        success = Math.Min(success, 10000 - destroy);
        long cost = checked(Cost(itemId, level, star, isSuperior) * (safeguardUI ? 3 : 1));
        cost = checked(cost * (100 - vipGrade * 5)) / 100;
        return new Quote { cost = cost,
            success = success, destroy = destroy, chanceTime = chance, safeguard = safeguardUI };
    }
    public Result Roll(int star, int dropStreak, bool isSuperior, Quote quote, int draw)
    {
        var row = At(star, isSuperior);
        if (quote == null || draw < 0 || draw >= 10000) throw new ArgumentOutOfRangeException();
        if (draw < quote.success) return new Result { outcome = Outcome.Success, stars = star + 1 };
        if (draw < quote.success + quote.destroy)
            return new Result { outcome = Outcome.Destroy, stars = isSuperior ? 0 : star, vestige = true };
        bool down = row.down && star > 0;
        return new Result { outcome = Outcome.Maintain, stars = down ? star - 1 : star, dropStreak = down ? Math.Min(2, dropStreak + 1) : 0 };
    }
}
