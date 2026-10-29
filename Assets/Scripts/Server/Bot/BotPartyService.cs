using System;
using System.Collections.Generic;

namespace Server.Bot
{
    // Party / BotPartyCommands / BotPartyQueue的主线程适配。
    public sealed class BotPartyService
    {
        public sealed class Party
        {
            public int id, leader;
            public readonly List<int> members = new List<int>();
        }
        public sealed class Invitation { public int party, timeout; public BotActor inviter; }
        readonly DbStore db;
        readonly Dictionary<int,Party> parties = new Dictionary<int,Party>();
        readonly Dictionary<int,int> membership = new Dictionary<int,int>();
        readonly Dictionary<int,Invitation> invitations = new Dictionary<int,Invitation>();
        readonly Dictionary<int,BotActor> players = new Dictionary<int,BotActor>();
        int nextId = 1000000001;
        public event Action<int> Left;
        public BotPartyService(DbStore db) { this.db = db; }
        public void Bind(BotActor actor) { if (!actor.IsBot()) players[actor.id] = actor; }
        public BotActor Actor(CharRecord record)
        {
            if (players.TryGetValue((int)record.id,out var live)) return live;
            var actor = new BotActor { id = (int)record.id, name = record.name, world = record.world, level = record.level, job = record.job, mapId = record.map };
            players[actor.id] = actor; return actor;
        }
        public BotActor Find(string name, int world)
        {
            foreach (var actor in players.Values) if (actor.online && actor.world == world && actor.name == name) return actor;
            foreach (var actor in BotRegistry.All().Values) if (actor.online && actor.world == world && actor.name == name) return actor;
            return null;
        }
        public BotActor Get(int id) => players.TryGetValue(id,out var actor) ? actor : BotRegistry.Get(id);
        public Party For(int id) => membership.TryGetValue(id,out int party) && parties.TryGetValue(party,out var value) ? value : null;
        void Persist(int id, int party)
        {
            if (Get(id)?.IsBot() ?? true) return;
            db.Table("characters").Update(r => DbStore.Int(r,"id") == id,new DbRow { ["party"] = (long)party });
            if (Get(id).character != null) Get(id).character.record.party = party;
        }
        public bool Create(BotActor actor)
        {
            if (actor == null || For(actor.id) != null || actor.level < 10 && !GameConfigDao.GetBool(db,"use_party_for_starters")) return false;
            Bind(actor); var party = new Party { id = nextId++, leader = actor.id }; party.members.Add(actor.id);
            parties[party.id] = party; membership[actor.id] = party.id; Persist(actor.id,party.id); db.Save(); return true;
        }
        public bool Join(BotActor actor, int partyId)
        {
            if (actor == null || For(actor.id) != null || !parties.TryGetValue(partyId,out var party) || party.members.Count >= 6) return false;
            var leader = Get(party.leader); if (leader == null || leader.world != actor.world) return false;
            Bind(actor); party.members.Add(actor.id); membership[actor.id] = partyId; Persist(actor.id,partyId); db.Save(); return true;
        }
        public bool Invite(BotActor sender, BotActor target)
        {
            if (sender == null || target == null || sender.id == target.id || !target.online || sender.world != target.world ||
                For(target.id) != null || invitations.ContainsKey(target.id)) return false;
            bool starters = GameConfigDao.GetBool(db,"use_party_for_starters");
            if (target.level < 10 && (!starters || sender.level >= 10) || starters && target.level >= 10 && sender.level < 10) return false;
            var party = For(sender.id); if (party == null) { if (!Create(sender)) return false; party = For(sender.id); }
            if (party.members.Count >= 6 || sender.IsBot() && party.leader != sender.id) return false;
            invitations[target.id] = new Invitation { party = party.id, inviter = sender }; return true;
        }
        public void TickInvitationTimeouts()
        {
            foreach (int id in new List<int>(invitations.Keys))
                if (invitations[id].timeout>5) invitations.Remove(id); else invitations[id].timeout++;
        }
        public Invitation Pending(int id) => invitations.TryGetValue(id,out var invitation) ? invitation : null;
        public bool Answer(BotActor actor, int partyId, bool accepted)
        {
            if (!invitations.TryGetValue(actor.id,out var invitation) || invitation.party != partyId) return false;
            invitations.Remove(actor.id); return !accepted || Join(actor,partyId);
        }
        public void Leave(int id)
        {
            invitations.Remove(id); var party = For(id); if (party == null) return;
            if (party.leader == id)
            {
                parties.Remove(party.id);
                foreach (int member in new List<int>(party.members)) { membership.Remove(member); Persist(member,0); Left?.Invoke(member); }
                foreach (int target in new List<int>(invitations.Keys)) if (invitations[target].party == party.id) invitations.Remove(target);
            }
            else { membership.Remove(id); party.members.Remove(id); Persist(id,0); Left?.Invoke(id); }
            db.Save();
        }
        // Party.expelFromParty: only the leader may expel an existing member.
        public bool Expel(int sender, int member)
        {
            var party = For(sender);
            if (party == null || party.leader != sender || !party.members.Contains(member)) return false;
            membership.Remove(member); party.members.Remove(member); Persist(member,0); Left?.Invoke(member);
            db.Save(); return true;
        }
        // PartyOperationHandler case 6 / World.updateParty(CHANGE_LEADER).
        // The source handler does not add a same-map/channel restriction.
        public bool ChangeLeader(int sender, int member)
        {
            var party = For(sender);
            if (party == null || !party.members.Contains(member)) return false;
            party.leader = member; return true;
        }
        public void EndSession(int id) { Leave(id); players.Remove(id); }
        public IEnumerable<BotActor> Members(int id)
        {
            var party = For(id); if (party == null) yield break;
            foreach (int member in party.members) { var actor = Get(member); if (actor != null) yield return actor; }
        }
        public void Clear()
        { foreach (int leader in new List<int>(membership.Keys)) Leave(leader); parties.Clear(); invitations.Clear(); players.Clear(); }
    }
}
