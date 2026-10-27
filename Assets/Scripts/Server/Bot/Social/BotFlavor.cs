using System;
using System.Collections.Generic;
using Server.Bot.Environment;

namespace Server.Bot.Social
{
    public sealed class BotFlavor
    {
        public enum Action { Emote, BuffFlex, SkillSwing }
        readonly Random random;
        readonly Dictionary<int, long> cooldown = new Dictionary<int, long>();
        static readonly int[] friendly = { 1, 2, 5, 6 };
        public double Chance = .12;
        public long CooldownMinMs = 15000, CooldownMaxMs = 40000;
        public BotFlavor(Random random = null) { this.random = random ?? new Random(); }
        public void MaybeExpress(BotStateMachine bot)
        {
            if (bot == null || bot.chr == null || bot.state == BotStateMachine.State.TRADING || !bot.CheckMainPlayersOnMap()) return;
            long now = BotScheduler.Now();
            if (cooldown.TryGetValue(bot.chr.id, out long until) && now < until || random.NextDouble() >= Chance) return;
            Force(bot, Pick());
            cooldown[bot.chr.id] = now + CooldownMinMs + (long)(random.NextDouble() * Math.Max(1, CooldownMaxMs - CooldownMinMs));
        }
        public void Forget(BotCharacter actor) { if (actor != null) cooldown.Remove(actor.id); }
        public Action Pick() { int roll = random.Next(11); return roll < 5 ? Action.Emote : roll < 8 ? Action.BuffFlex : Action.SkillSwing; }
        public void Force(BotStateMachine bot, Action action)
        {
            if (bot?.chr == null) return;
            if (action != Action.SkillSwing) bot.output.Emote(bot.chr, friendly[random.Next(friendly.Length)]);
            else
            {
                var motion = BotMovementService.Get(bot.chr);
                if (motion != null) motion.alertedUntilMs = BotScheduler.Now() + 5000;
                bot.output.Send(BotOutput.Kind.Attack, bot.chr, value: 0);
            }
        }
    }
}
