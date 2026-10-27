using System;
using System.Collections.Generic;

namespace Server.Bot.Social
{
    public sealed class ChatMessage
    {
        public readonly BotActor sender;
        public readonly string content;
        public readonly int mapId;
        public readonly long timestamp;
        public ChatMessage(BotActor sender, string content)
        { this.sender = sender; this.content = content; mapId = sender.mapId; timestamp = BotScheduler.Now(); }
    }
    public enum QueueType { PRIMARY, SECONDARY, TERTIARY }
    public sealed class BotMessages
    {
        readonly Dictionary<QueueType, Queue<ChatMessage>> queues = new Dictionary<QueueType, Queue<ChatMessage>>
        {
            [QueueType.PRIMARY] = new Queue<ChatMessage>(), [QueueType.SECONDARY] = new Queue<ChatMessage>(),
            [QueueType.TERTIARY] = new Queue<ChatMessage>()
        };
        public static QueueType FromString(string name) => name switch
        {
            "primary" => QueueType.PRIMARY, "secondary" => QueueType.SECONDARY, "tertiary" => QueueType.TERTIARY,
            _ => throw new ArgumentException("Queue not found: " + name)
        };
        public void Add(ChatMessage message, QueueType type = QueueType.SECONDARY)
        { if (message == null) throw new ArgumentNullException(nameof(message)); queues[type].Enqueue(message); }
        public ChatMessage Poll(QueueType type = QueueType.SECONDARY) => queues[type].Count == 0 ? null : queues[type].Dequeue();
        public ChatMessage Peek(QueueType type = QueueType.PRIMARY) => queues[type].Count == 0 ? null : queues[type].Peek();
        public bool RemoveIf(bool condition, QueueType type = QueueType.PRIMARY)
        {
            var m = Poll(type);
            if (m == null) return false;
            if (condition) return true;
            Add(m, type); return false;
        }
        public int Count(QueueType type) => queues[type].Count;
        public void Clear() { foreach (var queue in queues.Values) queue.Clear(); }
        public void Clean(long now, long expirationMs = 10000)
        {
            foreach (var queue in queues.Values)
            {
                int count = queue.Count;
                for (int i = 0; i < count; i++) { var m = queue.Dequeue(); if (now - m.timestamp <= expirationMs) queue.Enqueue(m); }
            }
        }
    }
    public sealed class BotInteractors
    {
        public readonly List<BotActor> respondants = new List<BotActor>();
        public readonly List<BotActor> inquirers = new List<BotActor>();
        public BotActor Respondant => respondants.Count == 0 ? null : respondants[0];
        public BotActor Inquirer => inquirers[0];
        public void SetRespondant(BotActor actor) { BotStore.AddRespondant(actor); respondants.Add(actor); }
        public bool IsRespondant(BotActor actor) => respondants.Contains(actor);
        public void RemoveRespondant(BotActor actor) { BotStore.RemoveRespondant(actor); respondants.Remove(actor); }
        public void ResetRespondants() { foreach (var actor in respondants) BotStore.RemoveRespondant(actor); respondants.Clear(); }
        public void SetInquirer(BotActor actor) { BotStore.AddInquirer(actor); if (!inquirers.Contains(actor)) inquirers.Add(actor); }
        public bool IsInquirer(BotActor actor) => inquirers.Contains(actor);
        public void RemoveInquirer(BotActor actor) { BotStore.RemoveInquirer(actor); inquirers.Remove(actor); }
        public void ResetInquirers() { foreach (var actor in inquirers) BotStore.RemoveInquirer(actor); inquirers.Clear(); }
        public bool IsMessageFromRespondant(ChatMessage message) => ReferenceEquals(message.sender, Respondant);
    }
}
