using System;
using System.Collections.Generic;

namespace Server.Bot.Trade
{
    public enum BotTradeOperation { Read, Invite, OfferItem, OfferMeso, Confirm, Cancel, Chat, Accept }
    public sealed class BotTradeRequest
    {
        public int characterId, botId, inventoryType, inventoryPosition, quantity, slot, meso;
        public string text;
        public BotTradeOperation operation;
    }
    public sealed class BotTradeResponse
    {
        public bool ok = true, active, accepted, locked, partnerLocked, incomingInvitation;
        public string error, partnerName;
        public int partnerId, meso, partnerMeso;
        public FullCharacter character;
        public List<InventoryEntry> items = new List<InventoryEntry>(), partnerItems = new List<InventoryEntry>();
    }
    public sealed class BotTradeProtocol
    {
        readonly BotTradeService service;
        readonly Func<int,BotActor> players;
        public BotTradeProtocol(BotTradeService service, Func<int,BotActor> players) { this.service = service; this.players = players; }
        public BotTradeResponse Handle(BotTradeRequest request)
        {
            var player = players(request.characterId);
            if (player == null || !player.online || player.IsBot()) return new BotTradeResponse { ok = false, error = "角色不在线" };
            string error = null;
            if (request.operation == BotTradeOperation.Invite)
            {
                var bot = BotRegistry.Get(request.botId);
                if (bot == null || !bot.online || bot.mapId != player.mapId || bot.world != player.world || bot.channel != player.channel) error = "交易对象不在当前地图";
                else if (!service.Invite(player,bot)) error = "交易对象正忙";
            }
            else if (request.operation != BotTradeOperation.Read)
            {
                var session = player.trade as BotTradeService.Session;
                if (session == null) error = "交易已结束";
                else if (request.operation == BotTradeOperation.Cancel) session.Decline();
                else if (request.operation == BotTradeOperation.Accept)
                {
                    if (!session.IsIncoming || session.partner?.actor.id != request.botId) error = "交易邀请已变更";
                    else
                    {
                        session.Accept();
                        if (!session.FullTrade) error = "交易邀请已过期";
                    }
                }
                else if (!session.FullTrade) error = "对方尚未接受交易";
                else switch (request.operation)
                {
                    case BotTradeOperation.OfferItem:
                        if (!session.OfferFromInventory(request.inventoryType,request.inventoryPosition,request.quantity,request.slot)) error = "不能放入该物品";
                        break;
                    case BotTradeOperation.OfferMeso:
                        if (request.meso < 0 || request.meso > player.character.record.meso || session.locked) error = "金币不足或交易已锁定";
                        else session.SetMeso(request.meso);
                        break;
                    case BotTradeOperation.Confirm:
                        if (session.partner?.actor == null) error = "交易对象已离开"; else session.Confirm();
                        break;
                    case BotTradeOperation.Chat:
                        if (string.IsNullOrWhiteSpace(request.text) || request.text.Length > 127) error = "聊天内容无效";
                        else session.Chat(request.text);
                        break;
                }
            }
            var current = player.trade as BotTradeService.Session;
            var result = new BotTradeResponse { ok = error == null, error = error, character = player.character, active = current != null };
            if (current != null)
            {
                result.accepted = current.FullTrade; result.locked = current.locked;
                result.incomingInvitation = current.IsIncoming && current.InvitationPending;
                result.meso = current.meso; result.items.AddRange(current.Items);
                if (current.partner != null)
                {
                    result.partnerId = current.partner.actor.id; result.partnerName = current.partner.actor.name;
                    result.partnerMeso = current.partner.meso; result.partnerLocked = current.partner.locked;
                    result.partnerItems.AddRange(current.partner.Items);
                }
            }
            return result;
        }
    }
}
