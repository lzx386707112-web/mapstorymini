using System;
using System.Collections.Generic;

namespace Server.Bot.Trade
{
    public static class BotTradeCommands
    {
        public static void StartRequest(BotActor actor, BotTradeService service) => service.Start(actor);
        public static bool SendRequest(BotActor actor, BotActor invitee, BotTradeService service) => service.Invite(actor, invitee);
        public static void WriteChat(BotActor actor, string text) => actor.trade?.Chat(text);
        public static void SetMeso(BotActor actor, int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            actor.trade?.SetMesoBot(amount);
        }
        public static int ReadMeso(BotActor actor) => actor.trade?.meso ?? 0;
        public static int ReadPartnerMeso(BotActor actor) => actor.trade == null ? 0 : actor.trade.partner.meso;
        public static IList<InventoryEntry> LocalItems(BotActor actor) => actor.trade?.Items;
        public static IList<InventoryEntry> PartnerItems(BotActor actor) => actor.trade?.partner.Items;
        public static bool IsPartnerLocked(BotActor actor) => actor.trade != null && actor.trade.partner.locked;
        public static bool IsConfirmed(BotActor actor) => actor.trade?.locked ?? false;
        public static void Confirm(BotActor actor) => actor.trade?.Confirm();
        public static void Accept(BotActor actor) => actor.trade?.Accept();
        public static void Decline(BotActor actor) => actor.trade?.Decline();
        public static List<int> OccupiedSlots(BotActor actor)
        {
            var slots = new List<int>();
            if (actor.trade != null) foreach (var item in actor.trade.Items) slots.Add(item.position);
            return slots;
        }
        public static List<int> EmptySlots(BotActor actor)
        {
            var slots = new List<int>();
            if (actor.trade == null) return slots;
            var occupied = OccupiedSlots(actor);
            for (int i = 1; i <= 9; i++) if (!occupied.Contains(i)) slots.Add(i);
            return slots;
        }
        public static int? RandomEmptySlot(BotActor actor, Random random)
        { var slots = EmptySlots(actor); return slots.Count == 0 ? (int?)null : slots[random.Next(slots.Count)]; }
        static bool Validate(BotActor actor, int slot)
        {
            if (actor.trade == null) return false;
            if (slot < 1 || slot > 9) throw new ArgumentOutOfRangeException(nameof(slot), "slot must be 1-9");
            return true;
        }
        public static bool AddEquip(BotActor actor, InventoryEntry equip, int slot)
            => Validate(actor, slot) && equip != null && actor.trade.AddItem(equip, slot);
        public static bool AddCleanEquip(BotActor actor, int itemId, int slot)
            => Validate(actor, slot) && AddEquip(actor, BotLogic.GenerateCleanItemEquip(itemId), slot);
        public static bool AddItem(BotActor actor, int itemId, int quantity, int slot)
            => Validate(actor, slot) && actor.trade.AddItem(BotLogic.GenerateCleanItemWithQty(itemId, quantity), slot);
        public static bool SwapItem(BotActor actor, InventoryEntry item, int slot)
            => !IsConfirmed(actor) && Validate(actor, slot) && actor.trade.SwapItem(item, slot);
        public static bool SwapCleanEquip(BotActor actor, int itemId, int slot)
            => !IsConfirmed(actor) && Validate(actor, slot) && SwapItem(actor, BotLogic.GenerateCleanItemEquip(itemId), slot);
        public static bool SwapItem(BotActor actor, int itemId, int quantity, int slot)
            => !IsConfirmed(actor) && Validate(actor, slot) && SwapItem(actor, BotLogic.GenerateCleanItemWithQty(itemId, quantity), slot);
        public static int PartnerFame(BotActor actor) => actor.trade?.partner.actor.fame ?? 0;
        public static int PartnerLevel(BotActor actor) => actor.trade?.partner.actor.level ?? 0;
        public static InventoryEntry InSlot(IList<InventoryEntry> items, int slot)
        {
            if (items == null) throw new InvalidOperationException("Trade items list is null");
            foreach (var item in items) if (item.position == slot) return item;
            return null;
        }
    }
    public sealed class BotTradeLogic
    {
        public readonly BotTradeQueue queue = new BotTradeQueue();
        public readonly BotBlockList blocked = new BotBlockList();
        public bool CheckQueue(BotActor actor)
        {
            if (!queue.HasPending(actor)) return false;
            if (actor.trade?.partner?.actor == null) { queue.Remove(actor); return false; }
            if (blocked.IsBlocked(actor.id, actor.trade.partner.actor.id))
            { queue.Remove(actor); BotTradeCommands.Decline(actor); return false; }
            BotTradeCommands.Accept(actor); queue.Remove(actor); return true;
        }
        public void Clear(BotActor actor) => queue.Remove(actor);
    }
}
