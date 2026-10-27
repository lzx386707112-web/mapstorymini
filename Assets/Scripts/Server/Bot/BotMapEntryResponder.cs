using System;
using Server.Bot.Events;

namespace Server.Bot
{
    public sealed class BotMapEntryResponder : IEventSubscriber, IDisposable
    {
        readonly EventBus bus;
        readonly Random random;
        bool registered;
        public BotMapEntryResponder(EventBus bus, Random random = null) { this.bus = bus; this.random = random ?? new Random(); }
        public void Register()
        { if (registered) return; registered = true; bus.Subscribe(EventType.MAP_ENTERED, this); }
        public bool MatchesFilter(GameEvent ev) => ev != null && ev.type == EventType.MAP_ENTERED;
        public void OnEvent(GameEvent ev)
        {
            foreach (var actor in BotRegistry.OnMap(ev.mapId))
                if (actor.world == ev.world && actor.channel == ev.channel) OnBotArrivedObserved(actor);
        }
        public void OnBotArrivedObserved(BotCharacter actor)
        {
            if (actor == null) return;
            var bot = BotStore.GetBotById(actor.id);
            if (bot != null && bot.running) bot.NudgeSoon(random.Next(150, 701));
        }
        public void Dispose() { bus.UnsubscribeAll(this); registered = false; }
    }
}
