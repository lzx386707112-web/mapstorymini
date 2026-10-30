using UnityEngine;

// Native KeyConfig/icon IDs and the server's keymap type/action protocol.
public static class NativeKeyActions
{
    public sealed class Entry
    {
        public readonly GameAction action;
        public readonly int id;
        public readonly string name;
        public int Type => id >= 100 ? 6 : id >= 50 ? 5 : 4;
        public Entry(GameAction action, int id, string name)
        { this.action = action; this.id = id; this.name = name; }
    }
    public static readonly Entry[] Entries = {
        new Entry(GameAction.Equip, 0, "装备窗"),
        new Entry(GameAction.Inven, 1, "道具"),
        new Entry(GameAction.Stat, 2, "能力值"),
        new Entry(GameAction.Skill, 3, "技能"),
        new Entry(GameAction.Buddy, 4, "好友"),
        new Entry(GameAction.WorldMap, 5, "大地图"),
        new Entry(GameAction.Chat, 6, "聊天"),
        new Entry(GameAction.Minimap, 7, "小地图"),
        new Entry(GameAction.Quest, 8, "任务"),
        new Entry(GameAction.KeySet, 9, "键盘设定"),
        new Entry(GameAction.ChatAll, 10, "对所有人"),
        new Entry(GameAction.ChatWhisper, 11, "悄悄话"),
        new Entry(GameAction.ChatParty, 12, "对组员"),
        new Entry(GameAction.ChatBuddy, 13, "对好友"),
        new Entry(GameAction.Interface, 14, "界面"),
        new Entry(GameAction.Shortcut, 15, "快捷键"),
        new Entry(GameAction.ChatRoom, 16, "对话板"),
        new Entry(GameAction.Family, 17, "家族"),
        new Entry(GameAction.ChatFamily, 18, "对家族"),
        new Entry(GameAction.Party, 19, "组队"),
        new Entry(GameAction.Messenger, 20, "讯息"),
        new Entry(GameAction.ChatSpouse, 21, "对夫妻说"),
        new Entry(GameAction.MonsterBook, 22, "怪物手册"),
        new Entry(GameAction.Shop, 23, "商城"),
        new Entry(GameAction.ChatAlliance, 24, "对联盟说"),
        new Entry(GameAction.PartySearch, 25, "寻找组队"),
        new Entry(GameAction.School, 26, "学院"),
        new Entry(GameAction.Medal, 27, "勋章"),
        new Entry(GameAction.Pickup, 50, "捡取"),
        new Entry(GameAction.Sit, 51, "坐下"),
        new Entry(GameAction.Attack, 52, "攻击"),
        new Entry(GameAction.Jump, 53, "跳跃"),
        new Entry(GameAction.NpcTalk, 54, "NPC 对话"),
        new Entry(GameAction.FaceHit, 100, "受伤"),
        new Entry(GameAction.FaceSmile, 101, "微笑"),
        new Entry(GameAction.FaceTroubled, 102, "苦恼"),
        new Entry(GameAction.FaceCry, 103, "哭泣"),
        new Entry(GameAction.FaceAngry, 104, "生气"),
        new Entry(GameAction.FaceBewildered, 105, "惊讶"),
        new Entry(GameAction.FaceStunned, 106, "发呆"),
    };
    // NPC conversation blocks character control, not independent UI windows.
    public static bool AvailableDuringNpcDialog(GameAction action) =>
        action == GameAction.NpcTalk || action == GameAction.Menu || action == GameAction.Help ||
        action == GameAction.QuickSlot || Of(action)?.Type == 4;

    // Existing windows/actions share the same path for hardware keys and quick-bar clicks.
    public static void Execute(GameAction action)
    {
        var player = GameLoop.I != null ? GameLoop.I.player : null;
        var entry = Of(action);
        if (entry != null && entry.Type == 6) { player?.Express(entry.id - 99); return; }
        switch (action)
        {
            case GameAction.Equip: EquipWindow.Toggle(); return;
            case GameAction.Inven: InvenWindow.Toggle(); return;
            case GameAction.Stat: StatWindow.Toggle(); return;
            case GameAction.Skill: SkillWindow.Toggle(); return;
            case GameAction.KeySet: KeyConfigWindow.Toggle(); return;
            case GameAction.QuickSlot: BottomBar.QuickCollapsed = !BottomBar.QuickCollapsed; return;
            // Esc：面板优先关闭（后开的先关），没有面板才开/关系统菜单
            case GameAction.Menu:
                if (GameWindows.CloseTop()) return;
                // 登录/读图阶段还没有角色，别把系统菜单叠在登录界面上
                if (player == null) return;
                GameMenuWindow.Toggle();
                return;
            case GameAction.Shop: ShopWindow.Toggle(); return;
            case GameAction.Shortcut: ShortcutWindow.Toggle(); return;
            case GameAction.Help: HelpWindow.Toggle(); return;
            case GameAction.Quest: QuestWindow.Toggle(); return;
            case GameAction.Interface: GameHUD.InterfaceHidden = !GameHUD.InterfaceHidden; return;
            case GameAction.Sit: player?.ToggleSit(); return;
            case GameAction.NpcTalk:
                if (NpcSession.Blocking) { NpcDialogUi.ContinueWithTalkKey(); return; }
                NpcDialogUi.ConsumeTalkKeyFrame();
                NpcField.TryInteractNearest(); return;
            case GameAction.Chat: BottomBar.FocusChat(); return;
            case GameAction.ChatAll: BottomBar.FocusChat(NativeChatChannel.All); return;
            case GameAction.Family: NativeGuildWindow.Toggle(); return;
            case GameAction.School: case GameAction.ChatRoom:
                NativeCommunityWindow.Toggle(action); return;
            case GameAction.Medal: NativeMedalWindow.Toggle(); return;
            case GameAction.MonsterBook: NativeMonsterBook.Toggle(); return;
            case GameAction.ChatWhisper: BottomBar.FocusChat(NativeChatChannel.Whisper); return;
            case GameAction.ChatParty: BottomBar.FocusChat(NativeChatChannel.Party); return;
            case GameAction.ChatBuddy: BottomBar.FocusChat(NativeChatChannel.Buddy); return;
            case GameAction.ChatFamily: BottomBar.FocusChat(NativeChatChannel.Guild); return;
            case GameAction.ChatAlliance: BottomBar.FocusChat(NativeChatChannel.Alliance); return;
            case GameAction.ChatSpouse: BottomBar.FocusChat(NativeChatChannel.Spouse); return;
            case GameAction.WorldMap: NativeMapWindows.ToggleWorld(); return;
            case GameAction.Minimap: NativeMapWindows.ToggleMini(); return;
            case GameAction.Buddy: case GameAction.Party: case GameAction.PartySearch: case GameAction.Messenger:
                NativeSocialWindow.Toggle(action); return;
        }
        if (entry != null) BottomBar.Toast(entry.name + "功能尚未开放");
    }

    public static Entry Of(GameAction action)
    {
        foreach (var entry in Entries) if (entry.action == action) return entry;
        return null;
    }
    public static bool Find(int type, int id, out GameAction action)
    {
        foreach (var entry in Entries)
            if (entry.id == id && entry.Type == type) { action = entry.action; return true; }
        action = default;
        return false;
    }
}
