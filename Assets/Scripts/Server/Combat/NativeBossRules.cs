using System;
using System.Collections.Generic;

// Port of Monster.canUseSkill/usedSkill, KaringBossCompat and LucidBossCompat.
// Milliseconds and original WZ pixel coordinates; independent of Unity/frame rate.
public static class NativeBossRules
{
    public static bool IsKaring(int id) => id == 8880830 || id == 8880831 || id == 8880832 || id == 8880837 || id == 8880842;
    public static bool IsLucid(int id) => id == 8880140 || id == 8880141 || id == 8880152;
    public static int RegenMillis(int id)
    {
        switch (id) { case 8880830: return 3240; case 8880831: return 2520;
            case 8880832: return 2340; case 8880837: return 6660; case 8880842: return 8100;
            case 8880153: return 5000; default: return 0; }
    }
    public static int AttackCooldown(int id, int position, int fallback)
    {
        if ((id == 8880140 && position == 1) || (id == 8880141 && position == 2)) return 60000;
        int[] values = null;
        switch (id)
        {
            case 8880830: values = new[] {15000,15000,25000,60000,20000}; break;
            case 8880831: values = new[] {60000,15000,15000,25000}; break;
            case 8880832: values = new[] {15000,12000,5400,8000,12000,0}; break;
            case 8880837: values = new[] {12000,12000,18000,15000,30000,7000}; break;
            case 8880842: values = new[] {12000,12000,18000,15000}; break;
        }
        return values != null && position >= 0 && position < values.Length && values[position] > 0 ? values[position] : fallback;
    }
    public static long SkillCooldown(int mob, int skill, int level, long fallback)
    {
        if ((mob == 8880140 || mob == 8880141) && skill == 128 && level == 16) return 60000;
        if (mob == 8880830 && skill == 128 && level == 1) return 60000;
        if (mob == 8880830 && skill == 126 && level == 7) return 20000;
        if (mob == 8880831 && skill == 123 && level == 3) return 10000;
        if (mob == 8880831 && skill == 120 && level == 5) return 45000;
        if (mob == 8880837 && skill == 128 && level == 2) return 18000;
        return fallback;
    }
    public static bool Priority(int mob, int skill) =>
        (mob == 8880301 || mob == 8880000 || mob == 8880140 || mob == 8880342) && skill >= 183 && skill <= 187;
    public static int ButterflyInterval(int hp) => hp >= 90 ? 5000 : hp >= 70 ? 4500 : hp >= 50 ? 4000 : hp >= 20 ? 3000 : 2000;
    public static int ButterflyCount(int hp) => hp >= 90 ? 5 : hp >= 70 ? 7 : hp >= 50 ? 10 : hp >= 20 ? 15 : 20;
    public static int PercentDamage(int maximum, int percent) => (int)Math.Min(int.MaxValue, Math.Max(1L, (long)maximum * percent / 100));
}

public sealed class NativeMobCastState
{
    readonly Dictionary<string, long> ready = new Dictionary<string, long>();
    public int Mp { get; private set; }
    public NativeMobCastState(int mp) { Mp = Math.Max(0, mp); }
    public bool CanUse(string key, long now, int mpCost, bool sealedSkills) =>
        !sealedSkills && Mp >= mpCost && (!ready.TryGetValue(key, out long at) || now >= at);
    public bool TryUse(string key, long now, int mpCost, long cooldown, bool sealedSkills)
    {
        if (!CanUse(key, now, mpCost, sealedSkills)) return false;
        Mp -= mpCost;
        ready[key] = now + Math.Max(0, cooldown);
        return true;
    }
    public void HealMp(int amount, int maximum) => Mp = Math.Max(0, Math.Min(maximum, Mp + amount));
}

// Mob HP is long on the source server. A float loses small hits above ~2^24.
public sealed class NativeMobHealth
{
    public long Maximum { get; }
    public long Current { get; private set; }
    public NativeMobHealth(long maximum) { Maximum = Math.Max(1,maximum); Current = Maximum; }
    public long Damage(long amount)
    {
        long applied = Math.Min(Current,Math.Max(0,amount));
        Current -= applied;
        return applied;
    }
    public void Heal(long amount)
    {
        if (amount >= 0) Current += Math.Min(Maximum-Current,amount);
        else Damage(amount == long.MinValue ? long.MaxValue : -amount);
    }
    public void SetPercent(int percent)
    {
        percent = Math.Max(0,Math.Min(100,percent));
        Current = Math.Max(1,Maximum / 100 * percent + Maximum % 100 * percent / 100);
    }
}

public sealed class NativeBossTimeline
{
    sealed class Entry { public long at, order; public Func<bool> valid; public Action action; }
    readonly List<Entry> pending = new List<Entry>();
    long sequence, generation;
    public void Schedule(long now, long delay, Func<bool> valid, Action action)
        => pending.Add(new Entry { at = now + Math.Max(0,delay), order = sequence++, valid = valid, action = action });
    public void Clear() { generation++; pending.Clear(); }
    public void Tick(long now)
    {
        long current = generation;
        var due = new List<Entry>();
        for (int i = pending.Count-1; i >= 0; i--)
            if (pending[i].at <= now) { due.Add(pending[i]); pending.RemoveAt(i); }
        due.Sort((a,b) => { int time = a.at.CompareTo(b.at); return time != 0 ? time : a.order.CompareTo(b.order); });
        foreach (var e in due)
        {
            if (current != generation) break;
            if (e.valid == null || e.valid()) e.action();
        }
    }
}
