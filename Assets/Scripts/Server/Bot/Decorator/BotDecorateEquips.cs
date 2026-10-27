using System;

namespace Server.Bot.Decorator
{
    public sealed class BotDecorateEquips
    {
        readonly EquipSelection selection;
        readonly BotCustomization customization;
        readonly Random random;
        readonly JobPathLogic paths;
        public BotDecorateEquips(EquipSelection selection, BotCustomization customization, Random random = null)
        { this.selection = selection; this.customization = customization; this.random = random ?? new Random(); paths = new JobPathLogic(this.random); }
        void Equip(BotCharacter bot, string type) => customization.Equip(bot, selection.ForWearing(type, bot));
        public void Apply(BotCharacter bot)
        {
            int? coat = selection.ForWearing("COAT", bot), pants = selection.ForWearing("PANTS", bot), overall = selection.ForWearing("LONGCOAT", bot);
            if (coat.HasValue && pants.HasValue) { customization.Equip(bot, coat); customization.Equip(bot, pants); }
            else if (overall.HasValue) customization.Equip(bot, overall);
            else if (coat.HasValue) customization.Equip(bot, coat);
            else customization.Equip(bot, pants);
            Weapon(bot);
            foreach (string type in new[] { "CAP", "GLOVES", "SHOES", "CAPE" }) Equip(bot, type);
            var rules = new EquipDecoratorRules(selection, customization, random);
            rules.Add("CAPE", .50); rules.Add("EARRING", .50); rules.Add("ACCESSORY", .98); rules.Apply(bot);
        }
        bool Coin() => random.Next(2) == 0;
        public void Weapon(BotCharacter bot)
        {
            int style = EquipSelection.JobStyle(bot);
            bool shield = false;
            string weapon;
            switch (style)
            {
                case 100:
                    if (bot.job % 100 == 0)
                    {
                        bool two = Coin(); weapon = two ? (Coin() ? "SWORD_2H" : "AXE_2H") : (Coin() ? "SWORD" : "AXE"); shield = !two;
                    }
                    else
                    {
                        int path = paths.DetermineWarriorPath(bot.job);
                        if (path == 130) weapon = Coin() ? "SPEAR" : "POLEARM";
                        else
                        {
                            bool two = Coin(), first = Coin(); shield = !two;
                            weapon = path == 110 ? (first ? "AXE" : "SWORD") : (first ? "SWORD" : "AXE");
                            if (two) weapon += "_2H";
                        }
                    }
                    break;
                case 200: weapon = Coin() ? "WAND" : "STAFF"; shield = Coin(); break;
                case 300:
                    weapon = bot.job % 100 == 0 ? (Coin() ? "BOW" : "CROSSBOW") : paths.DetermineBowmanPath(bot.job) == 310 ? "BOW" : "CROSSBOW"; break;
                case 320: weapon = "CROSSBOW"; break;
                case 400:
                    bool claw = bot.job % 100 == 0 ? Coin() : paths.DetermineThiefPath(bot.job) == 410;
                    weapon = claw ? "CLAW" : "DAGGER"; shield = !claw; break;
                case 510:
                case 500: weapon = bot.job == 500 ? (Coin() ? "KNUCKLER" : "PISTOL") : bot.job / 10 == 52 ? "PISTOL" : "KNUCKLER"; break;
                case 520: weapon = "PISTOL"; break;
                default: weapon = "SWORD"; break;
            }
            Equip(bot, weapon); if (shield) Equip(bot, "SHIELD");
        }
    }
}
