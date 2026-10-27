using System;

namespace Server.Bot
{
    public enum BotTier { S = 5, A = 4, B = 3, C = 2, D = 1 }
    public static class BotTiers
    {
        public const BotTier Default = BotTier.S; // 原端实际返回S，不能照注释改成C。
        public static string Description(this BotTier tier) => tier switch
        {
            BotTier.S => "Elite", BotTier.A => "High", BotTier.B => "Above Average",
            BotTier.C => "Average", BotTier.D => "Below Average", _ => throw new ArgumentOutOfRangeException(nameof(tier))
        };
        public static bool IsAtLeast(this BotTier tier, BotTier minimum) => tier >= minimum;
        public static bool IsBetterThan(this BotTier tier, BotTier other) => tier > other;
        public static bool IsWorseThan(this BotTier tier, BotTier other) => tier < other;
        public static BotTier OrDefault(this BotTier tier, BotTier minimum, BotTier fallback) => tier >= minimum ? tier : fallback;
        public static BotTier BoostIfBelow(this BotTier tier, BotTier threshold, BotTier boostTo) => tier.OrDefault(threshold, boostTo);
        public static BotTier NextHigher(this BotTier tier) => tier == BotTier.S ? tier : (BotTier)((int)tier + 1);
        public static BotTier NextLower(this BotTier tier) => tier == BotTier.D ? tier : (BotTier)((int)tier - 1);
        public static int Difference(this BotTier tier, BotTier other) => (int)tier - (int)other;
        public static BotTier? FromString(string value)
        {
            if (value == null) return null;
            switch (value.ToUpperInvariant())
            {
                case "S": return BotTier.S; case "A": return BotTier.A; case "B": return BotTier.B;
                case "C": return BotTier.C; case "D": return BotTier.D; default: return null;
            }
        }
        public static BotTier SafeSet(BotTier? current, BotTier? value) => value ?? Default;
        public static BotTier Effective(BotTier? current) => (current ?? Default).BoostIfBelow(BotTier.C, BotTier.B);
        public static bool CanPerformAction(BotTier? current, BotTier? required) => current.HasValue && required.HasValue && current.Value >= required.Value;
        public static BotTier Upgrade(BotTier? current) => current.HasValue ? current.Value.NextHigher() : Default;
        public static BotTier Downgrade(BotTier? current) => current.HasValue ? current.Value.NextLower() : Default;
        public static BotTier Safe(BotTier? tier) => tier ?? Default;
    }
}
