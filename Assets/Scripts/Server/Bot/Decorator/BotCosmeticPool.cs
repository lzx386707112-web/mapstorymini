// BotCosmeticPool.java 的原始池与概率表；数值从源端逐项提取。
using System;
using System.Collections.Generic;
namespace Server.Bot.Decorator
{
    public enum CosmeticTier { PREMIUM, STANDARD, BASIC }
    public sealed class BotCosmeticPool
    {
        readonly Random random;
        public BotCosmeticPool(Random random = null) { this.random = random ?? new Random(); }
        static readonly Dictionary<BotTier, int[]> HAIR_EYE_WEIGHTS = new Dictionary<BotTier, int[]>
        {
            [BotTier.S] = new[] { 55,      35,      10 },
            [BotTier.A] = new[] { 40,      45,      15 },
            [BotTier.B] = new[] { 20,      55,      25 },
            [BotTier.C] = new[] { 10,      50,      40 },
            [BotTier.D] = new[] { 5,       40,      55 },
        };
        static readonly Dictionary<CosmeticTier, int[]> MALE_HAIR = new Dictionary<CosmeticTier, int[]>
        {
            [CosmeticTier.PREMIUM] = new[] { 48700, 48710, 48720, 48730, 42220, 48740, 48750, 42230, 42240, 42250, 48760, 48770 },
            [CosmeticTier.STANDARD] = new[] { 40070, 40080, 42100, 42220 },
            [CosmeticTier.BASIC] = new[] { 40070, 40080, 42100 },
        };
        static readonly Dictionary<CosmeticTier, int[]> FEMALE_HAIR = new Dictionary<CosmeticTier, int[]>
        {
            [CosmeticTier.PREMIUM] = new[] { 46540, 46550, 47140, 48670, 48680, 48690, 48700, 48710, 48720, 48730, 42220, 48740 },
            [CosmeticTier.STANDARD] = new[] { 43280, 44430, 44460, 42220 },
            [CosmeticTier.BASIC] = new[] { 43280, 44430, 44460 },
        };
        static readonly Dictionary<CosmeticTier, int[]> MALE_EYES = new Dictionary<CosmeticTier, int[]>
        {
            [CosmeticTier.PREMIUM] = new[] { 20005 },
            [CosmeticTier.STANDARD] = new[] { 20000, 20001, 20002 },
            [CosmeticTier.BASIC] = new[] { 20000, 20001, 20002 },
        };
        static readonly Dictionary<CosmeticTier, int[]> FEMALE_EYES = new Dictionary<CosmeticTier, int[]>
        {
            [CosmeticTier.PREMIUM] = new[] { 21003, 21004 },
            [CosmeticTier.STANDARD] = new[] { 21001, 21000, 21002 },
            [CosmeticTier.BASIC] = new[] { 21000, 21001, 21002 },
        };
        static readonly Dictionary<BotTier, double> HAIR_COLOR_CHANCE = new Dictionary<BotTier, double>
        {
            [BotTier.S] = 0.60,
            [BotTier.A] = 0.45,
            [BotTier.B] = 0.30,
            [BotTier.C] = 0.18,
            [BotTier.D] = 0.10,
        };
        static readonly Dictionary<BotTier, double> EYE_COLOR_CHANCE = new Dictionary<BotTier, double>
        {
            [BotTier.S] = 0.55,
            [BotTier.A] = 0.40,
            [BotTier.B] = 0.25,
            [BotTier.C] = 0.15,
            [BotTier.D] = 0.08,
        };
        static readonly int[] WILD_MALE_HAIR = { 46540, 46550, 47140, 48670, 48680, 48690, 42220, 42260, 42270 };
        static readonly int[] WILD_FEMALE_HAIR = { 46540, 46550, 47140, 48670, 48680, 48690, 42220, 42260, 42270 };
        static readonly int[] WILD_MALE_EYES = { 20012 };
        static readonly int[] WILD_FEMALE_EYES = { 21009 };
        CosmeticTier RollTier(BotTier tier)
        {
            int[] weights = HAIR_EYE_WEIGHTS[tier];
            int roll = random.Next(100);
            return roll < weights[0] ? CosmeticTier.PREMIUM : roll < weights[0] + weights[1] ? CosmeticTier.STANDARD : CosmeticTier.BASIC;
        }
        int Pick(int[] pool) => pool[random.Next(pool.Length)];
        public int SelectHair(int gender, BotTier tier)
        {
            int hair = random.NextDouble() < .01
                ? Pick(gender == 1 ? WILD_FEMALE_HAIR : WILD_MALE_HAIR)
                : Pick((gender == 1 ? FEMALE_HAIR : MALE_HAIR)[RollTier(tier)]);
            return random.NextDouble() < HAIR_COLOR_CHANCE[tier] ? hair + random.Next(7) + 1 : hair;
        }
        public int SelectEyes(int gender, BotTier tier)
        {
            int eyes = random.NextDouble() < .01
                ? Pick(gender == 1 ? WILD_FEMALE_EYES : WILD_MALE_EYES)
                : Pick((gender == 1 ? FEMALE_EYES : MALE_EYES)[RollTier(tier)]);
            return random.NextDouble() < EYE_COLOR_CHANCE[tier] ? eyes + (random.Next(8) + 1) * 100 : eyes;
        }
        public void DecorateBody(BotCharacter bot)
        {
            bot.skinColor = random.NextDouble() < .8 ? 0 : 1;
            bot.face = SelectEyes(bot.gender, bot.tier);
            bot.hair = SelectHair(bot.gender, bot.tier);
        }
    }
}
