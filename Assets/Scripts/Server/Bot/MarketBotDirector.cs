using System;
using System.Collections.Generic;

namespace Server.Bot
{
    // server/MarketBotDirector.java；主线程固定延迟驱动，所有市场bot共享路径预算。
    public sealed class MarketBotDirector
    {
        readonly Dictionary<int, BotStateMachine> bots = new Dictionary<int, BotStateMachine>();
        readonly Dictionary<int, long> due = new Dictionary<int, long>(), lastSpeech = new Dictionary<int, long>();
        readonly Func<DbStore> database;
        readonly Random random;
        BotTasks.Handle loop;
        long configuredTick = -1, lastMegaphone;
        int pathfinds;
        public Action<Exception> OnError;
        public MarketBotDirector(Func<DbStore> database, Random random = null)
        { this.database = database; this.random = random ?? new Random(); }
        public int ActiveCount => bots.Count;
        public void Register(BotStateMachine bot, long initialDelay)
        {
            if (bot?.chr == null) return;
            bots[bot.chr.id] = bot; due[bot.chr.id] = BotScheduler.Now() + Math.Max(0, initialDelay); EnsureLoop();
        }
        public void Unregister(BotStateMachine bot)
        { if (bot?.chr != null) { bots.Remove(bot.chr.id); due.Remove(bot.chr.id); } }
        public bool TryChat(BotActor actor)
        {
            if (actor == null) return false;
            long now = BotScheduler.Now();
            if (lastSpeech.TryGetValue(actor.id, out long last) && now - last < BotConfig.MarketSpeechGapMs(database())) return false;
            lastSpeech[actor.id] = now; return true;
        }
        public bool TrySpeak(BotActor actor) => BotConfig.MarketHawkEnabled(database()) && TryChat(actor);
        public bool TryMegaphone()
        {
            long now = BotScheduler.Now();
            if (now - lastMegaphone < 8000) return false;
            lastMegaphone = now; return true;
        }
        public bool TryPathfind() => ++pathfinds <= BotConfig.MarketPathfindPerTick(database());
        public bool RunPathfind(Action movement)
        {
            if (movement == null || !TryPathfind()) return false;
            BotTasks.Schedule(movement, 0); return true;
        }
        public void RefreshTickInterval()
        {
            long tick = BotConfig.MarketTickMs(database());
            if (loop != null && !loop.Cancelled && !loop.Completed && configuredTick == tick) return;
            loop?.Cancel(); loop = null; configuredTick = tick;
            if (bots.Count > 0) EnsureLoop();
        }
        void EnsureLoop()
        {
            long tick = BotConfig.MarketTickMs(database());
            if (loop != null && !loop.Cancelled && !loop.Completed && configuredTick == tick) return;
            loop?.Cancel(); configuredTick = tick;
            loop = BotTasks.ScheduleFixedDelay(Tick, tick, tick);
        }
        public void Tick()
        {
            pathfinds = 0; long now = BotScheduler.Now();
            var snapshot = new List<BotStateMachine>(bots.Values);
            var observed = new Dictionary<int, bool>();
            foreach (var bot in snapshot)
                if (bot.chr != null && !observed.ContainsKey(bot.chr.mapId)) observed[bot.chr.mapId] = bot.CheckMainPlayersOnMap();
            foreach (var bot in snapshot)
            {
                var chr = bot.chr;
                if (chr == null || !bots.TryGetValue(chr.id, out var current) || !ReferenceEquals(bot, current)) continue;
                if (due.TryGetValue(chr.id, out long next) && now < next) continue;
                if (chr.trade == null && chr.moving) continue;
                bool players = !observed.TryGetValue(chr.mapId, out bool value) || value;
                if (BotConfig.MarketIdleWhenEmpty(database()) && !players)
                { due[chr.id] = now + 8000 + random.Next(4000); continue; }
                try { bot.UpdateState(); } catch (Exception error) { OnError?.Invoke(error); }
                if (bots.TryGetValue(chr.id, out current) && ReferenceEquals(current, bot))
                    due[chr.id] = BotScheduler.Now() + (players ? 800 + random.Next(1200) : 4000 + random.Next(3000));
            }
        }
        public void Stop()
        { loop?.Cancel(); loop = null; bots.Clear(); due.Clear(); }
    }
}
