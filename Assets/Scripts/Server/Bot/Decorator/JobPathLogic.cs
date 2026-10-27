using System;

namespace Server.Bot.Decorator
{
    public sealed class JobPathLogic
    {
        readonly Random random;
        public JobPathLogic(Random random = null) { this.random = random ?? new Random(); }
        public int DetermineThiefPath(int job)
            => job >= 410 && job <= 412 ? 410 : job >= 420 && job <= 422 ? 420 : random.Next(2) == 0 ? 410 : 420;
        public int DetermineWarriorPath(int job)
            => job >= 110 && job <= 112 ? 110 : job >= 120 && job <= 122 ? 120 : job >= 130 && job <= 132 ? 130 : 110 + random.Next(3) * 10;
        public int DetermineBowmanPath(int job)
            => job >= 310 && job <= 312 ? 310 : job >= 320 && job <= 322 ? 320 : random.Next(2) == 0 ? 310 : 320;
    }
}
