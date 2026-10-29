using System;
using System.Collections.Generic;

namespace Server.Bot.Social
{
    // MapleMessengerCommands / MessengerHandler的本地会话适配，最多三人。
    public sealed class BotMessenger
    {
        public sealed class Entry
        {
            public long sequence;
            public int senderId;
            public string sender, text;
            public bool typing;
        }
        sealed class Room
        {
            public int id;
            public readonly List<BotActor> members = new List<BotActor>();
            public readonly Dictionary<int,int> positions = new Dictionary<int,int>();
            public readonly List<Entry> messages = new List<Entry>();
        }
        sealed class InvitationData { public Room room; public BotActor sender, target; public int timeout; }
        readonly Dictionary<int, Room> membership = new Dictionary<int, Room>();
        readonly Dictionary<int, InvitationData> invites = new Dictionary<int, InvitationData>();
        int nextRoom;
        long nextMessage;
        public void Open(BotActor actor)
        {
            if (actor==null || membership.ContainsKey(actor.id)) return;
            invites.Remove(actor.id);
            var room=new Room { id=++nextRoom }; room.members.Add(actor); room.positions[actor.id]=0; membership[actor.id]=room;
        }
        // Ordinary MessengerHandler invitations retain the existing room.
        public bool InviteMember(BotActor sender,BotActor target)
        {
            if (sender==null || target==null || !target.online || sender.world!=target.world || sender.id==target.id ||
                membership.ContainsKey(target.id) || invites.ContainsKey(target.id) || !membership.TryGetValue(sender.id,out var room) || room.members.Count>=3) return false;
            invites[target.id]=new InvitationData { room=room,sender=sender,target=target }; return true;
        }
        // MapleMessengerCommands.sendMessengerInviteComplete always creates a new bot room.
        public bool Invite(BotActor sender, BotActor target)
        {
            if (sender == null || target == null || !target.online || sender.world != target.world || sender.id == target.id) return false;
            Leave(sender.id); Open(sender);
            if (invites.ContainsKey(target.id)) return false;
            invites[target.id]=new InvitationData { room=membership[sender.id],sender=sender,target=target }; return true;
        }
        public void TickInvitationTimeouts()
        {
            foreach (int id in new List<int>(invites.Keys))
                if (invites[id].timeout>5) invites.Remove(id); else invites[id].timeout++;
        }
        public int Invitation(int id) => invites.TryGetValue(id, out var invite) ? invite.room.id : 0;
        public string Inviter(int id) => invites.TryGetValue(id, out var invite) ? invite.sender.name : null;
        public bool Answer(int id, int roomId, bool accepted)
        {
            if (!invites.TryGetValue(id, out var invite) || invite.room.id != roomId) return false;
            invites.Remove(id);
            if (!accepted) return true;
            if (membership.ContainsKey(id) || invite.room.members.Count >= 3 || !membership.TryGetValue(invite.sender.id, out var active) || active != invite.room) return false;
            // Messenger.getLowestPosition: leaving frees only that seat; surviving
            // members keep their positions, including when the creator leaves.
            int position = 0;
            while (invite.room.positions.ContainsValue(position)) position++;
            invite.room.positions[id] = position;
            invite.room.members.Add(invite.target); membership[id] = invite.room; return true;
        }
        public bool Accepted(BotActor sender, BotActor target)
            => sender != null && target != null && membership.TryGetValue(sender.id, out var room)
                && room.members.Exists(m => m.name == target.name);
        public bool Contains(int id) => membership.ContainsKey(id);
        public void Send(BotActor sender, string text, bool typing = false)
        {
            if (sender == null || !membership.TryGetValue(sender.id, out var room)) return;
            room.messages.Add(new Entry { sequence = ++nextMessage, senderId = sender.id, sender = sender.name, text = text, typing = typing });
        }
        public void Send(int id, string text)
        { if (membership.TryGetValue(id,out var room)) Send(room.members.Find(m => m.id == id),text); }
        public List<Entry> Messages(int id, long after)
            => membership.TryGetValue(id, out var room) ? room.messages.FindAll(m => m.sequence > after && m.senderId != id) : new List<Entry>();
        public List<SocialMember> Members(int id)
        {
            var result = new List<SocialMember>();
            if (membership.TryGetValue(id, out var room)) foreach (var actor in room.members)
                result.Add(new SocialMember { id = actor.id, name = actor.name, level = actor.level, messengerPosition = room.positions[actor.id] });
            return result;
        }
        public void Leave(int id)
        {
            invites.Remove(id);
            if (!membership.TryGetValue(id, out var room)) return;
            membership.Remove(id); room.members.RemoveAll(m => m.id == id); room.positions.Remove(id);
            foreach (int target in new List<int>(invites.Keys)) if (invites[target].sender.id == id) invites.Remove(target);
        }
        public void Clear() { membership.Clear(); invites.Clear(); }
    }
}
