using System;

namespace Server.Bot.Events
{
    public enum EventType
    {
        GACHAPON_REWARD, LEVEL_UP, SCROLLING, ITEM_MEGAPHONE, CHAT_MEGAPHONE,
        MASTERY_RESULT, EMOTE, CHAT_GENERAL, MAP_ENTERED
    }
    public static class EventTypes
    {
        public static string Description(this EventType type) => type switch
        {
            EventType.GACHAPON_REWARD => "Gachapon Reward", EventType.LEVEL_UP => "Level Up",
            EventType.SCROLLING => "Scrolling Result", EventType.ITEM_MEGAPHONE => "Item Megaphone Announcement",
            EventType.CHAT_MEGAPHONE => "Chat Megaphone", EventType.MASTERY_RESULT => "Mastery Result",
            EventType.EMOTE => "Emote", EventType.CHAT_GENERAL => "General Chat", EventType.MAP_ENTERED => "Player Entered Map",
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
    }
    public sealed class GameEvent
    {
        static int nextId = 1;
        public readonly int id, world, channel, mapId, playerId;
        public readonly long timestamp;
        public readonly BotActor actor;
        public readonly string playerName, message;
        public readonly EventType type;
        public readonly InventoryEntry item;
        public readonly bool? pass;
        public GameEvent(BotActor actor, EventType type, string message = null, InventoryEntry item = null, bool? pass = null)
        {
            id = nextId++;
            timestamp = BotScheduler.Now();
            this.actor = actor; world = actor.world; channel = actor.channel; mapId = actor.mapId;
            playerName = actor.name; playerId = actor.id;
            this.type = type; this.message = message; this.item = item; this.pass = pass;
        }
    }
    public static class EventFactory
    {
        public static GameEvent Gachapon(BotActor actor, InventoryEntry item) => new GameEvent(actor, EventType.GACHAPON_REWARD, item: item);
        public static GameEvent LevelUp(BotActor actor) => new GameEvent(actor, EventType.LEVEL_UP);
        public static GameEvent Scroll(BotActor actor, InventoryEntry item, bool success) => new GameEvent(actor, EventType.SCROLLING, item: item, pass: success);
        public static GameEvent ItemMegaphone(BotActor actor, InventoryEntry item, string message) => new GameEvent(actor, EventType.ITEM_MEGAPHONE, message, item);
        public static GameEvent ChatMegaphone(BotActor actor, string message) => new GameEvent(actor, EventType.CHAT_MEGAPHONE, message);
        public static GameEvent ChatGeneral(BotActor actor, string message) => new GameEvent(actor, EventType.CHAT_GENERAL, message);
    }
    public interface IEventSubscriber
    {
        void OnEvent(GameEvent ev);
        bool MatchesFilter(GameEvent ev);
    }
}
