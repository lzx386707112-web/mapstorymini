using System;

namespace Server.Bot.Decorator
{
    public sealed class QuickEquip
    {
        public bool Enabled = true;
        readonly GenericEquipPool pool;
        readonly BotCustomization customization;
        readonly Random random;
        public QuickEquip(GenericEquipPool pool, BotCustomization customization, Random random = null)
        { this.pool = pool; this.customization = customization; this.random = random ?? new Random(); }
        public void Apply(BotCharacter bot)
        {
            if (!Enabled) return;
            double chance = bot.tier switch { BotTier.S => .90, BotTier.A => .75, BotTier.B => .55, BotTier.C => .35, BotTier.D => .15, _ => .30 };
            if (random.NextDouble() < chance)
            {
                if (random.NextDouble() < .75) { if (!Overall(bot)) TopBottom(bot); }
                else if (!TopBottom(bot)) Overall(bot);
            }
            foreach (var slot in new[] { ("weapons", .90), ("caps", .60), ("shoes", .50), ("capes", .30), ("gloves", .30) })
                if (random.NextDouble() < chance * slot.Item2) customization.Equip(bot, Pick(bot, slot.Item1));
        }
        int? Pick(BotCharacter bot, string category) => pool.Pick(category, bot.level, bot.gender);
        bool Overall(BotCharacter bot)
        { int? id = Pick(bot, "overalls"); if (!id.HasValue) return false; customization.Equip(bot, id); return true; }
        bool TopBottom(BotCharacter bot)
        {
            int? top = Pick(bot, "tops"), bottom = Pick(bot, "bottoms");
            if (!top.HasValue || !bottom.HasValue) return false;
            customization.Equip(bot, top); customization.Equip(bot, bottom); return true;
        }
    }
}
