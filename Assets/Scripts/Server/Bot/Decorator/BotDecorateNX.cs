using System;
using System.Collections.Generic;

namespace Server.Bot.Decorator
{
    public sealed class BotDecorateNX
    {
        public bool Enabled = true;
        readonly NXItemPool pool;
        readonly BotCustomization customization;
        readonly Random random;
        static readonly string[] categories = { "weapons", "caps", "clothing", "shoes", "gloves", "capes", "earrings", "face", "eye" };
        static readonly int[] weights = { 10, 10, 10, 6, 6, 6, 3, 3, 3 };
        public BotDecorateNX(NXItemPool pool, BotCustomization customization, Random random = null)
        { this.pool = pool; this.customization = customization; this.random = random ?? new Random(); }
        public void Apply(BotCharacter bot, bool configEnabled, bool forced = false)
        {
            if (!forced && (!Enabled || !configEnabled)) return;
            Equip(bot, "weapons"); Clothing(bot);
            int min = bot.tier == BotTier.S ? 2 : 1;
            int max = bot.tier switch { BotTier.S => 5, BotTier.A => 3, BotTier.B => 2, BotTier.C => 1, BotTier.D => 1, _ => 2 };
            int intensity = random.Next(min, max + 1);
            var remaining = new List<int>(); for (int i = 0; i < categories.Length; i++) remaining.Add(i);
            for (int n = 0; n < intensity && remaining.Count > 0; n++)
            {
                int total = 0; foreach (int i in remaining) total += weights[i];
                int roll = random.Next(total), accumulated = 0;
                for (int j = 0; j < remaining.Count; j++)
                {
                    int i = remaining[j]; accumulated += weights[i];
                    if (roll >= accumulated) continue;
                    remaining.RemoveAt(j);
                    if (categories[i] == "clothing") Clothing(bot); else Equip(bot, categories[i]);
                    break;
                }
            }
            double chance = bot.tier switch { BotTier.S => .40, BotTier.A => .25, BotTier.B => .15, BotTier.C => .05, BotTier.D => .02, _ => .05 };
            if (random.NextDouble() < chance) Equip(bot, "rings");
        }
        void Equip(BotCharacter bot, string category) => customization.Equip(bot, pool.Pick(category, bot.gender));
        void Clothing(BotCharacter bot)
        {
            if (random.NextDouble() < .75) { if (!Overall(bot)) TopBottom(bot); }
            else if (!TopBottom(bot)) Overall(bot);
        }
        bool Overall(BotCharacter bot)
        { int? id = pool.Pick("overalls", bot.gender); if (!id.HasValue) return false; customization.Equip(bot, id); return true; }
        bool TopBottom(BotCharacter bot)
        {
            int? top = pool.Pick("tops", bot.gender), bottom = pool.Pick("bottoms", bot.gender);
            if (!top.HasValue || !bottom.HasValue) return false;
            customization.Equip(bot, top); customization.Equip(bot, bottom); return true;
        }
    }
}
