using System;
using System.Collections.Generic;
using Server.Bot.Environment;
using Server.Bot.Decorator;
using Server.Field;

namespace Server.Bot.Social
{
    public sealed class MarketBotAmbient
    {
        enum Phase { Rest, Walk, Sit, Climb }
        sealed class Life { public Phase phase; public long until; public int sitBias, direction; }
        readonly Dictionary<int, Life> lives = new Dictionary<int, Life>();
        readonly Random random;
        readonly PlatformParser platforms;
        readonly Func<DbStore> database;
        public Func<BotCharacter, bool> KeepsShopChair;
        static readonly int[] mounts = { 1902000, 1902001, 1902002, 1902005, 1902008, 1902009, 1902011, 1902012 };
        public MarketBotAmbient(BotResources resources, Func<DbStore> database, Random random = null)
        {
            platforms = new PlatformParser(resources.ReadText, directory => resources.ListFileStems(directory));
            this.database = database; this.random = random ?? new Random();
        }
        public void Forget(BotCharacter actor) { lives.Remove(actor.id); BotRecordedActions.Cancel(actor); }
        public void Clear() { lives.Clear(); }
        public void Act(BotStateMachine bot)
        {
            var actor = bot?.chr;
            if (actor == null || MapService.Get(actor.mapId) == null || actor.trade != null || BotRecordedActions.IsMoving(actor)) return;
            long now = BotScheduler.Now();
            if (!lives.TryGetValue(actor.id, out var life)) lives[actor.id] = life = new Life
                { sitBias = random.Next(8, 14), direction = random.Next(2) == 0 ? 1 : -1, until = now + random.Next(400, 1600) };
            if (KeepsShopChair?.Invoke(actor) ?? false) { Sit(bot); TalkSitting(bot); return; }
            if (life.phase == Phase.Sit && actor.chair > 0 && now < life.until) { TalkSitting(bot); return; }
            if (life.phase == Phase.Sit)
            { Stand(bot); Rest(life, 2500, 7000); Fidget(bot); return; }
            if (life.phase == Phase.Rest && now < life.until) { Fidget(bot); return; }
            if ((life.phase == Phase.Walk || life.phase == Phase.Climb) && now < life.until) return;
            if (!BotConfig.MarketWanderEnabled(database())) { TalkIdle(bot); return; }
            int roll = random.Next(100);
            if (roll < 72) Stroll(bot, life, false);
            else if (roll < 72 + life.sitBias) { Sit(bot); life.phase = Phase.Sit; life.until = now + random.Next(12000, 28000); }
            else if (roll < 86 && actor.mapId == 910000000) Climb(bot, life);
            else if (roll < 93) Stroll(bot, life, true);
            else if (roll < 97)
            {
                actor.mountItemId = mounts[random.Next(mounts.Length)]; actor.mountSkillId = 1004; actor.mountActive = true;
                bot.output.Send(BotOutput.Kind.Mount, actor, value: actor.mountItemId); Stroll(bot, life, false);
            }
            else { TalkIdle(bot); Rest(life, 800, 2200); }
        }
        void Rest(Life life, int min, int max) { life.phase = Phase.Rest; life.until = BotScheduler.Now() + random.Next(min, max); }
        void Sit(BotStateMachine bot)
        { if (bot.chr.chair <= 0) { bot.chr.stance = 6 + (bot.chr.stance & 1); bot.output.Sit(bot.chr, BotFurniture.RandomChair(random)); } }
        void Stand(BotStateMachine bot)
        { if (bot.chr.chair > 0) { bot.chr.stance = 4 + (bot.chr.stance & 1); bot.output.CancelChair(bot.chr); } }
        void Fidget(BotStateMachine bot)
        {
            int roll = random.Next(100);
            if (roll < 18) TalkIdle(bot);
            else if (roll < 28) bot.output.Emote(bot.chr, random.Next(1, 23));
            else if (roll < 36) BotRecordedActions.Turn(bot.chr, !(bot.chr.stance == 1 || bot.chr.stance == 3 || bot.chr.stance == 5));
        }
        void TalkSitting(BotStateMachine bot)
        { if (random.Next(100) < 28 && bot.marketDirector.TryChat(bot.chr)) bot.output.Speak(bot.chr, bot.marketFlavor.Chat()); }
        void TalkIdle(BotStateMachine bot)
        {
            var nearby = new List<BotCharacter>(); var actor = bot.chr;
            foreach (var other in BotRegistry.OnMap(actor.mapId))
                if (other.id != actor.id && Math.Abs(other.x - actor.x) <= 220 && Math.Abs(other.y - actor.y) <= 80) nearby.Add(other);
            var target = nearby.Count == 0 ? null : nearby[random.Next(nearby.Count)];
            if (!bot.marketDirector.TryChat(actor)) return;
            bot.output.Speak(actor, bot.marketFlavor.Chat());
            if (target != null && bot.marketDirector.TryChat(target)) bot.output.Speak(target, bot.marketFlavor.Reply());
        }
        public string CurrentPlatform(BotCharacter actor)
        {
            string best = null; int bestScore = int.MaxValue;
            foreach (string id in platforms.ListPlatformIds(actor.mapId))
            {
                var p = platforms.ParsePlatform(actor.mapId, id);
                if (p.sortedPoints.Count == 0 || actor.x < p.minX - 20 || actor.x > p.maxX + 20) continue;
                int dy = Math.Abs((int)actor.y - p.GetYAtX((int)actor.x)); if (dy > 10) continue;
                int score = dy * 10 + Math.Abs((int)actor.x - (p.minX + p.maxX) / 2) / 10;
                if (score < bestScore) { bestScore = score; best = id; }
            }
            return best;
        }
        List<string> MainPlatforms(int mapId) => platforms.ListPlatformIds(mapId).FindAll(id => id.StartsWith("m", StringComparison.Ordinal));
        Platform Current(BotCharacter actor)
        {
            string id = CurrentPlatform(actor); var main = MainPlatforms(actor.mapId);
            try { return platforms.ParsePlatform(actor.mapId, id ?? (main.Count > 0 ? main[0] : "m1")); }
            catch (Exception) { return null; }
        }
        public BotPoint StrollTarget(Platform platform, int x, int direction)
        {
            int distance = random.Next(120, 281), dir = direction >= 0 ? 1 : -1;
            if (platform == null) return new BotPoint(x + dir * distance, 0);
            int min = platform.minX + 24, max = platform.maxX - 24;
            if (max <= min) return new BotPoint(x, platform.GetYAtX(x));
            if (x + dir * distance > max || x + dir * distance < min) dir = -dir;
            int dest = Math.Max(min, Math.Min(max, x + dir * distance)); return new BotPoint(dest, platform.GetYAtX(dest));
        }
        void Stroll(BotStateMachine bot, Life life, bool jump)
        {
            var actor = bot.chr; Stand(bot); var dest = StrollTarget(Current(actor), (int)actor.x, life.direction);
            life.direction = dest.x >= actor.x ? 1 : -1; life.phase = Phase.Walk; life.until = BotScheduler.Now() + 8000;
            int mapId = actor.mapId;
            bot.marketDirector.RunPathfind(() =>
            {
                if (!ReferenceEquals(BotStore.GetBotById(actor.id), bot) || !bot.running || actor.mapId != mapId) return;
                if (jump) BotRecordedActions.Jump(actor);
                if (!BotRecordedActions.WalkAlongX(actor, dest.x, 18, () => Rest(life, 900, 2800))) Rest(life, 900, 2800);
            });
        }
        void Climb(BotStateMachine bot, Life life)
        {
            var actor = bot.chr; Stand(bot); var ids = MainPlatforms(actor.mapId);
            if (ids.Count < 2) { Stroll(bot, life, false); return; }
            string current = CurrentPlatform(actor); var others = ids.FindAll(id => id != current);
            if (others.Count == 0) others = ids;
            BotPoint dest;
            try
            {
                var platform = platforms.ParsePlatform(actor.mapId, others[random.Next(others.Count)]);
                int x = platform.minX + 40 + random.Next(Math.Max(1, platform.Width - 80)); dest = new BotPoint(x, platform.GetYAtX(x));
            }
            catch (Exception) { Stroll(bot, life, false); return; }
            life.phase = Phase.Climb; life.until = BotScheduler.Now() + 16000; int mapId = actor.mapId;
            bot.marketDirector.RunPathfind(() =>
            {
                if (!ReferenceEquals(BotStore.GetBotById(actor.id), bot) || !bot.running || actor.mapId != mapId) return;
                BotMovementService.Enable(actor); BotMovementService.Move(actor, dest.x, dest.y, () => Rest(life, 1800, 4500));
            });
        }
    }
}
