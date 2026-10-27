using System;
using System.Collections.Generic;

namespace Server.Bot.Decorator
{
    public sealed class EquipDecoratorRules
    {
        readonly Dictionary<string, double> skips = new Dictionary<string, double>();
        readonly List<string[][]> exclusive = new List<string[][]>();
        readonly EquipSelection selection;
        readonly BotCustomization customization;
        readonly Random random;
        public EquipDecoratorRules(EquipSelection selection, BotCustomization customization, Random random = null)
        { this.selection = selection; this.customization = customization; this.random = random ?? new Random(); }
        public void Add(string type, double skip) => skips[type] = skip;
        public void AddExclusive(params string[][] groups) => exclusive.Add(groups);
        public void Apply(BotCharacter bot)
        {
            var members = new HashSet<string>();
            foreach (var group in exclusive) foreach (var option in group) foreach (string type in option) members.Add(type);
            foreach (var pair in skips)
                if (!members.Contains(pair.Key) && random.NextDouble() >= pair.Value)
                    try { customization.Equip(bot, selection.ForWearing(pair.Key, bot)); } catch (Exception) { }
            foreach (var group in exclusive)
                foreach (string type in group[random.Next(group.Length)])
                    if (random.NextDouble() >= (skips.TryGetValue(type, out var skip) ? skip : 0))
                    {
                        int id = selection.ForWearing(type, bot) ?? throw new InvalidOperationException("Exclusive equip selection returned null");
                        customization.Equip(bot, id);
                    }
        }
    }
}
