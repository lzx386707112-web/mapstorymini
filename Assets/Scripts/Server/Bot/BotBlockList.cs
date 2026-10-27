using System;
using System.Collections.Generic;
using System.Linq;

namespace Server.Bot
{
    // BotBlockList.java：同一交易者可有多条block，过期条件严格大于（不是>=）。
    public sealed class BotBlockList
    {
        public sealed class BlockEntry
        {
            public readonly int botId, traderId;
            public readonly long blockTime, expirationTime;
            public BlockEntry(int botId, int traderId, long blockTime, long expirationTime)
            { this.botId = botId; this.traderId = traderId; this.blockTime = blockTime; this.expirationTime = expirationTime; }
            public bool IsExpired(long now) => now > expirationTime;
        }
        readonly List<BlockEntry> entries = new List<BlockEntry>();
        readonly Func<long> clock;
        public BotBlockList(Func<long> clock = null) { this.clock = clock ?? BotScheduler.Now; }
        public BlockEntry AddToBlockList(int botId, int traderId, long seconds = 300)
        {
            long now = clock();
            var entry = new BlockEntry(botId, traderId, now, now + seconds * 1000);
            entries.Add(entry);
            return entry;
        }
        public bool IsBlocked(int botId, int traderId) => GetBlockEntry(botId, traderId) != null;
        public BlockEntry GetBlockEntry(int botId, int traderId)
        { RemoveExpiredEntries(); return entries.Find(e => e.botId == botId && e.traderId == traderId); }
        public void RemoveExpiredEntries() { long now = clock(); entries.RemoveAll(e => e.IsExpired(now)); }
        public bool RemoveBlock(int botId, int traderId) => entries.RemoveAll(e => e.botId == botId && e.traderId == traderId) > 0;
        public List<BlockEntry> GetBlockList() { RemoveExpiredEntries(); return new List<BlockEntry>(entries); }
        public List<BlockEntry> GetBlocksForBot(int botId) => GetBlockList().Where(e => e.botId == botId).ToList();
        public List<int> GetBlockedTraders(int botId) => GetBlocksForBot(botId).Select(e => e.traderId).ToList();
        public void ClearBlockList() => entries.Clear();
        public int ActiveBlockCount { get { RemoveExpiredEntries(); return entries.Count; } }
    }
}
