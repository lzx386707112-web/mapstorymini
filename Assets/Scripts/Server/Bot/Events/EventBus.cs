using System.Collections.Generic;

namespace Server.Bot.Events
{
    public sealed class EventBus
    {
        readonly Dictionary<EventType, List<IEventSubscriber>> subscribers = new Dictionary<EventType, List<IEventSubscriber>>();
        public readonly EventStore store = new EventStore(5000);
        public void Publish(GameEvent ev)
        {
            store.Add(ev);
            if (!subscribers.TryGetValue(ev.type, out var interested)) return;
            // 对应源端CopyOnWriteArrayList：回调增删订阅不改变本次遍历。
            foreach (var subscriber in new List<IEventSubscriber>(interested))
                if (subscriber.MatchesFilter(ev)) subscriber.OnEvent(ev);
        }
        public void Subscribe(EventType type, IEventSubscriber subscriber)
        {
            if (!subscribers.TryGetValue(type, out var list)) subscribers[type] = list = new List<IEventSubscriber>();
            list.Add(subscriber);
        }
        public void Unsubscribe(EventType type, IEventSubscriber subscriber)
        { if (subscribers.TryGetValue(type, out var list)) list.Remove(subscriber); }
        public void UnsubscribeAll(IEventSubscriber subscriber)
        { foreach (var list in subscribers.Values) list.Remove(subscriber); }
    }
    public sealed class EventStore
    {
        readonly GameEvent[] buffer;
        int head, size;
        public EventStore(int capacity) { buffer = new GameEvent[capacity]; }
        public void Add(GameEvent ev)
        {
            buffer[head] = ev; head = (head + 1) % buffer.Length;
            if (size < buffer.Length) size++;
        }
        public List<GameEvent> GetEventsByType(EventType type, long sinceTimestamp = long.MaxValue)
            => Filter(e => e.type == type && e.timestamp >= sinceTimestamp);
        public List<GameEvent> GetEventsByLocation(int world, int channel, int? mapId = null)
            => Filter(e => e.world == world && e.channel == channel && (!mapId.HasValue || e.mapId == mapId));
        public List<GameEvent> GetEventsInTimeRange(long start, long end)
            => Filter(e => e.timestamp >= start && e.timestamp <= end);
        List<GameEvent> Filter(System.Predicate<GameEvent> predicate)
        {
            var result = new List<GameEvent>();
            // 原端按物理槽顺序筛选（不排序），保留其语义。
            for (int i = 0; i < size; i++) if (buffer[i] != null && predicate(buffer[i])) result.Add(buffer[i]);
            return result;
        }
        public List<GameEvent> GetRecentEvents(int count)
        {
            var result = new List<GameEvent>();
            int n = System.Math.Min(count, size), start = (head - n + buffer.Length) % buffer.Length;
            for (int i = 0; i < n; i++) if (buffer[(start + i) % buffer.Length] != null) result.Add(buffer[(start + i) % buffer.Length]);
            return result;
        }
    }
    public sealed class BotEventBuffer
    {
        readonly Queue<GameEvent> queue = new Queue<GameEvent>();
        readonly int maxSize;
        public BotEventBuffer(int maxSize) { this.maxSize = maxSize; }
        public bool Add(GameEvent ev)
        {
            if (queue.Count >= maxSize && queue.Count > 0) queue.Dequeue();
            queue.Enqueue(ev); return true;
        }
        public GameEvent Poll() => queue.Count == 0 ? null : queue.Dequeue();
        public bool IsEmpty => queue.Count == 0;
        public int Size => queue.Count;
        public void Clear() => queue.Clear();
    }
}
