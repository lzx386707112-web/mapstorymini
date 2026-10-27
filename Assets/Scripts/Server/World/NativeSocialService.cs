using System;
using System.Collections.Generic;

// Rules translated from BuddylistModifyHandler, Party.createParty,
// PartySearchStartHandler, MessengerHandler and the chat handlers.
// This transport hosts one active character; other saved characters are offline.
public enum SocialOperation { Read, AddBuddy, AcceptBuddy, DeleteBuddy, CreateParty, LeaveParty, InviteParty, SearchParty, StopSearch, OpenMessenger, CloseMessenger, InviteMessenger, SendChat, AddMonsterCard, SetBookCover, AcceptMessenger, DeclineMessenger }
public enum NativeChatChannel { All, Whisper, Party, Buddy, Guild, Alliance, Spouse, Messenger }
public sealed class SocialRequest
{
    public int characterId;
    public SocialOperation operation;
    public NativeChatChannel channel;
    public string target = "", text = "", group = "Default Group";
    public int minLevel, maxLevel;
    public int cardId;
    public int messengerRoom;
    public long afterMessengerMessage;
}
public sealed class SocialMember
{
    public int id, level;
    public string name, group;
    public bool pending;
}
public sealed class SocialResponse
{
    public bool ok = true, party, searching, messenger;
    public string error = "";
    public int capacity;
    public int bookLevel = 1, bookCover;
    public Dictionary<int, int> cards = new Dictionary<int, int>();
    public List<SocialMember> buddies = new List<SocialMember>();
    public int messengerInvite;
    public string messengerInviter;
    public List<SocialMember> messengerMembers = new List<SocialMember>();
    public List<Server.Bot.Social.BotMessenger.Entry> messengerMessages = new List<Server.Bot.Social.BotMessenger.Entry>();
}
public sealed class NativeSocialService
{
    readonly DbStore db;
    public Action<SocialRequest> ChatAccepted;
    public readonly Server.Bot.Social.BotMessenger BotMessenger = new Server.Bot.Social.BotMessenger();
    int partyOwner, messengerOwner, searchOwner;
    readonly Dictionary<int, long> lastChat = new Dictionary<int, long>();
    public NativeSocialService(DbStore db) { this.db = db; }
    public void EndSession(int characterId)
    {
        if (partyOwner == characterId) {
            partyOwner = searchOwner = 0;
            db.Table("characters").Update(r => DbStore.Int(r, "id") == characterId, new DbRow { ["party"] = 0L });
        }
        if (messengerOwner == characterId) messengerOwner = 0;
        BotMessenger.Leave(characterId);
        lastChat.Remove(characterId);
    }
    public SocialResponse Handle(SocialRequest request)
    {
        var self = CharactersDao.FindById(db, request.characterId);
        if (self == null) return new SocialResponse { ok = false, error = "角色不存在" };
        string error = null;
        switch (request.operation)
        {
            case SocialOperation.AddBuddy: error = AddBuddy(self, request); break;
            case SocialOperation.AcceptBuddy: error = AcceptBuddy(self, request.target); break;
            case SocialOperation.DeleteBuddy:
                var other = Find(request.target, self.world);
                if (other != null) db.Table("buddies").Delete(r => DbStore.Int(r, "characterid") == self.id && DbStore.Int(r, "buddyid") == other.id);
                db.Save(); break;
            case SocialOperation.CreateParty:
                if (partyOwner == self.id) error = "已经加入组队";
                else if (self.level < 10 && !GameConfigDao.GetBool(db, "use_party_for_starters")) error = "达到10级才能创建组队";
                else { partyOwner = (int)self.id; db.Table("characters").Update(r => DbStore.Int(r, "id") == self.id, new DbRow { ["party"] = self.id }); db.Save(); }
                break;
            case SocialOperation.LeaveParty: if (partyOwner == self.id) { partyOwner = searchOwner = 0; db.Table("characters").Update(r => DbStore.Int(r, "id") == self.id, new DbRow { ["party"] = 0L }); db.Save(); } break;
            case SocialOperation.InviteParty: error = request.target == self.name ? "不能邀请自己" : "该角色不在线"; break;
            case SocialOperation.SearchParty:
                if (request.minLevel > request.maxLevel) error = "最低等级不能高于最高等级";
                else if (request.maxLevel - request.minLevel > 30) error = "搜索等级范围不能超过30级";
                else if (self.level < request.minLevel || self.level > request.maxLevel) error = "搜索范围必须包含自己的等级";
                else if (partyOwner != self.id) error = "只有队长可以发起组队搜索";
                else searchOwner = (int)self.id;
                break;
            case SocialOperation.StopSearch: if (searchOwner == self.id) searchOwner = 0; break;
            case SocialOperation.OpenMessenger: messengerOwner = (int)self.id; break;
            case SocialOperation.CloseMessenger: if (messengerOwner == self.id) messengerOwner = 0; BotMessenger.Leave((int)self.id); break;
            case SocialOperation.AcceptMessenger:
            case SocialOperation.DeclineMessenger:
                if (!BotMessenger.Answer((int)self.id, request.messengerRoom, request.operation == SocialOperation.AcceptMessenger)) error = "聊天邀请已失效";
                break;
            case SocialOperation.InviteMessenger: error = request.target == self.name ? "不能邀请自己" : "该角色不在线"; break;
            case SocialOperation.SendChat: error = ValidateChat(self, request); break;
            case SocialOperation.AddMonsterCard:
                if (request.cardId / 10000 != 238) { error = "无效怪物卡片"; break; }
                var card = db.Table("monsterbook").Find(r => DbStore.Int(r, "charid") == self.id && DbStore.Int(r, "cardid") == request.cardId);
                if (card == null) db.Table("monsterbook").Insert(new DbRow { ["charid"] = self.id, ["cardid"] = (long)request.cardId, ["level"] = 1L });
                else card["level"] = (long)Math.Min(5, DbStore.Int(card, "level") + 1);
                db.Save(); break;
            case SocialOperation.SetBookCover:
                if (request.cardId != 0 && request.cardId / 10000 != 238) { error = "无效怪物卡片封面"; break; }
                self.monsterbookcover = request.cardId;
                CharactersDao.Save(db, self); db.Save(); break;
        }
        var response = new SocialResponse { ok = error == null, error = error ?? "", capacity = self.buddyCapacity,
            party = partyOwner == self.id, searching = searchOwner == self.id, messenger = messengerOwner == self.id || BotMessenger.Contains((int)self.id),
            messengerInvite = BotMessenger.Invitation((int)self.id), messengerInviter = BotMessenger.Inviter((int)self.id),
            messengerMembers = BotMessenger.Members((int)self.id), messengerMessages = BotMessenger.Messages((int)self.id, request.afterMessengerMessage) };
        foreach (var row in db.Table("buddies").Select(r => DbStore.Int(r, "characterid") == self.id))
        {
            var buddy = CharactersDao.FindById(db, DbStore.Int(row, "buddyid"));
            if (buddy != null) response.buddies.Add(new SocialMember { id = (int)buddy.id, name = buddy.name,
                level = buddy.level, group = DbStore.Str(row, "group"), pending = DbStore.Int(row, "pending") != 0 });
        }
        foreach (var card in db.Table("monsterbook").Select(r => DbStore.Int(r, "charid") == self.id))
            response.cards[DbStore.Int(card, "cardid")] = DbStore.Int(card, "level");
        int next = 1;
        do { next += response.bookLevel * 10; if (response.cards.Count < next) break; response.bookLevel++; } while (true);
        response.bookCover = self.monsterbookcover;
        return response;
    }
    CharRecord Find(string name, int world)
    {
        var row = db.Table("characters").Find(r => DbStore.Int(r, "world") == world &&
            string.Equals(DbStore.Str(r, "name"), name, StringComparison.OrdinalIgnoreCase));
        return row == null ? null : CharactersDao.FindById(db, DbStore.Int(row, "id"));
    }
    int BuddyCount(long id) => db.Table("buddies").Select(r => DbStore.Int(r, "characterid") == id && DbStore.Int(r, "pending") == 0).Count;
    DbRow Buddy(long id, long other) => db.Table("buddies").Find(r => DbStore.Int(r, "characterid") == id && DbStore.Int(r, "buddyid") == other);
    string AddBuddy(CharRecord self, SocialRequest request)
    {
        if (request.target == null || request.target.Length < 2 || request.target.Length > 13 || request.group == null || request.group.Length > 16) return "角色名或分组名称长度无效";
        var other = Find(request.target, self.world);
        if (other == null) return "角色不存在";
        if (other.id == self.id) return "不能添加自己";
        var row = Buddy(self.id, other.id);
        if (row != null && DbStore.Int(row, "pending") == 0)
        { row["group"] = request.group; db.Save(); return null; }
        if (BuddyCount(self.id) >= self.buddyCapacity) return "好友列表已满";
        if (BuddyCount(other.id) >= other.buddyCapacity) return "对方好友列表已满";
        var reverse = Buddy(other.id, self.id);
        PutBuddy(self.id, other.id, request.group, 0);
        if (reverse == null) PutBuddy(other.id, self.id, "Default Group", 1);
        db.Save(); return null;
    }
    void PutBuddy(long id, long other, string group, int pending)
    {
        var row = Buddy(id, other);
        if (row != null) { row["pending"] = (long)pending; row["group"] = group; }
        else db.Table("buddies").Insert(new DbRow { ["characterid"] = id, ["buddyid"] = other, ["group"] = group, ["pending"] = (long)pending });
    }
    string AcceptBuddy(CharRecord self, string name)
    {
        var other = Find(name, self.world);
        var pending = other == null ? null : Buddy(self.id, other.id);
        if (pending == null || DbStore.Int(pending, "pending") == 0) return "好友申请已失效";
        if (BuddyCount(self.id) >= self.buddyCapacity) return "好友列表已满";
        pending["pending"] = 0L; db.Save(); return null;
    }
    string ValidateChat(CharRecord self, SocialRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.text)) return "请输入聊天内容";
        if (request.text.Length > 127) return "聊天内容不能超过127个字符";
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (lastChat.TryGetValue((int)self.id, out var previous) && now - previous < 200) return "发送过于频繁";
        switch (request.channel)
        {
            case NativeChatChannel.Whisper: return Find(request.target, self.world) == null ? "角色不存在" : "对方不在线";
            case NativeChatChannel.Party: if (partyOwner != self.id) return "尚未加入组队"; break;
            case NativeChatChannel.Buddy: if (BuddyCount(self.id) == 0) return "好友列表为空"; break;
            case NativeChatChannel.Guild: if (self.guildid <= 0) return "尚未加入家族"; break;
            case NativeChatChannel.Alliance: return "尚未加入联盟";
            case NativeChatChannel.Spouse: return self.partnerId <= 0 ? "尚未结婚" : "配偶不在线";
            case NativeChatChannel.Messenger: if (messengerOwner != self.id && !BotMessenger.Contains((int)self.id)) return "讯息会话已关闭"; break;
        }
        lastChat[(int)self.id] = now;
        ChatAccepted?.Invoke(request);
        return null;
    }
}
