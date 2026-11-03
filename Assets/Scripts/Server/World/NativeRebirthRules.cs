// Job.java changeJobByLevel/getBaseJobType/isA/isTargetJob.
// Preserve the source's missing pirate/Aran branches; do not infer new job rules.
public static class NativeRebirthRules
{
    static readonly int[] BaseJobs = { 110,120,130,200,210,220,230,300,310,320,400,410,420,500,510,520,
        1100,1200,1300,1400,1500,2000 };
    static readonly int[] AdventurerBranches = { 220,210,230,110,120,130,310,320,410,420 };
    static bool IsA(int job, int parent) => job / 10 == parent / 10 && job >= parent ||
        parent / 10 % 10 == 0 && job / 100 == parent / 100;
    public static bool IsCygnusFifth(int job) => job >= 1112 && job <= 1512 && job % 100 == 12;

    public static int JobAtLevel(int current, int level, int requestedJob, bool completedCygnusFifth)
    {
        int next = requestedJob;
        if (next <= 0)
        {
            int baseJob = current;
            foreach (int candidate in BaseJobs)
                if (candidate >= 1000 ? candidate / 100 == current / 100 : candidate / 10 == current / 10)
                { baseJob = candidate; break; }
            next = current;
            foreach (int branch in AdventurerBranches)
                if (IsA(baseJob, branch))
                { next = level < 8 ? 0 : level < 31 ? branch / 100 * 100 : level < 70 ? branch : level < 120 ? branch + 1 : branch + 2; break; }
            for (int branch = 1100; branch <= 1500; branch += 100)
                if (IsA(baseJob, branch))
                    next = level < 10 ? 1000 : level < 30 ? branch : level < 70 ? branch + 10 : level < 120 ? branch + 11 : branch + 12;
            if (IsA(baseJob, 2000))
                next = level < 10 ? 2000 : level < 30 ? 2100 : level < 70 ? 2110 : level < 120 ? 2111 : 2112;
        }
        return !completedCygnusFifth && IsCygnusFifth(next) ? next - 1 : next;
    }
}
