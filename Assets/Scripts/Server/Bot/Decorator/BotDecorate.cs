using System;

namespace Server.Bot.Decorator
{
    public sealed class BotDecorate
    {
        readonly Random random;
        readonly BotCustomization customization;
        readonly BotCosmeticPool cosmetics;
        readonly QuickEquip quick;
        readonly BotDecorateEquips full;
        readonly BotDecorateNX nx;
        public BotDecorate(BotResources resources, BotItemCatalog catalog, Social.BotOutput output, Random random = null)
        {
            this.random = random ?? new Random();
            var data = EquipMetadataCache.LoadPools();
            var cache = new EquipMetadataCache(catalog, data);
            customization = new BotCustomization(catalog, output);
            cosmetics = new BotCosmeticPool(this.random);
            quick = new QuickEquip(new GenericEquipPool(resources, catalog, this.random), customization, this.random);
            full = new BotDecorateEquips(new EquipSelection(cache, this.random), customization, this.random);
            nx = new BotDecorateNX(new NXItemPool(resources, cache, data, this.random), customization, this.random);
        }
        public static int MinimumEquipLevel(int level) => level / 10 * 10 - 9;
        public BotTier RandomTier()
        {
            int roll = random.Next(100);
            return roll < 30 ? BotTier.S : roll < 60 ? BotTier.A : roll < 80 ? BotTier.B : roll < 90 ? BotTier.C : BotTier.D;
        }
        public int GenerateLevel(BotTier tier, int min, int max)
        {
            if (min >= max) throw new ArgumentException("minLevel must be less than maxLevel");
            int range = max - min + 1;
            int low, high;
            switch (tier)
            {
                case BotTier.S: low = min + 3 * range / 4; high = max; break;
                case BotTier.A: low = min + 2 * range / 4; high = min + 3 * range / 4 - 1; break;
                case BotTier.B: low = min + range / 4; high = min + 2 * range / 4 - 1; break;
                case BotTier.C:
                case BotTier.D: low = min; high = min + range / 4 - 1; break;
                default: throw new ArgumentOutOfRangeException(nameof(tier));
            }
            return (int)(random.NextDouble() * (high - low + 1)) + low;
        }
        public int SelectJob(int level)
        {
            if (level < 10) return 0;
            int basic = (int)(random.NextDouble() * 5) + 1;
            if (level < 30) return basic * 100;
            int path = basic <= 2 ? (int)(random.NextDouble() * 3) + 1 : random.NextDouble() < .5 ? 1 : 2;
            return basic * 100 + path * 10 + (level < 70 ? 0 : level < 120 ? 1 : 2);
        }
        public void ApplyAppearance(BotCharacter bot, DbStore db)
        {
            bot.tier = RandomTier(); bot.level = GenerateLevel(bot.tier, 120, 200);
            bot.job = SelectJob(bot.level); bot.gender = random.Next(2);
            if (BotConfig.RandomBodyEnabled(db)) cosmetics.DecorateBody(bot);
            // IMG metadata is loaded synchronously before this service can be constructed.
            if (BotConfig.NormalEquipsEnabled(db))
            {
                if (BotConfig.FullEquipsEnabled(db) && random.NextDouble() < .5) full.Apply(bot);
                else
                {
                    quick.Apply(bot);
                    if (BotConfig.FullEquipsEnabled(db) && !HasClothing(bot)) full.Apply(bot);
                }
            }
            nx.Apply(bot, BotConfig.NxEquipsEnabled(db));
            if (BotConfig.NormalEquipsEnabled(db) && !HasClothing(bot))
            {
                customization.Equip(bot, bot.gender == 1 ? 1041002 : 1040002);
                customization.Equip(bot, bot.gender == 1 ? 1061002 : 1060002);
            }
            if (BotConfig.AnyOutfitFeatureEnabled(db) && !bot.equips.ContainsKey(-11) && !bot.equips.ContainsKey(-111))
                customization.Equip(bot, 1302000);
        }
        static bool HasClothing(BotCharacter bot) => bot.equips.ContainsKey(-5) || bot.equips.ContainsKey(-6);
    }
}
