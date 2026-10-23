using System.Collections.Generic;
using UnityEngine;
using MapleDash.Wz;

public static class NativeMonsterBook
{
    public static bool Visible;
    static SocialResponse state;
    static readonly Dictionary<int, ImgNode> cards = new Dictionary<int, ImgNode>();
    static readonly List<int> ids = new List<int>();
    static Vector2 listScroll, detailScroll;
    static int selected;
    static string search = "";
    static void Ensure()
    {
        if (cards.Count > 0) return;
        foreach (var item in WzAssets.FileRoot("Item/Consume/0238.img").Children)
            if (int.TryParse(item.Name, out int id) && item.Int("info/monsterBook") != 0)
            { cards[id] = item; ids.Add(id); }
        ids.Sort();
    }
    public static void Toggle()
    {
        Visible = !Visible;
        if (!Visible) return;
        Ensure(); state = NativeSocialWindow.Request(SocialOperation.Read);
        if (selected == 0 && ids.Count > 0) selected = ids[0];
    }
    public static bool Collect(int cardId, int quantity)
    {
        if (!GameSession.InGame || GameSession.Net == null) return false;
        Ensure(); if (!cards.ContainsKey(cardId)) return false;
        // Original addCard caps each card at five, and duplicate cards do not add book levels.
        for (int i = 0; i < Mathf.Min(quantity, 5); i++)
        {
            state = GameSession.Net.Social(new SocialRequest { characterId = GameSession.CharacterId,
                operation = SocialOperation.AddMonsterCard, cardId = cardId });
            if (!state.ok) return false;
        }
        return true;
    }
    static string NameOf(int card)
    {
        int mob = cards[card].Int("info/mob");
        return WzAssets.FileRoot("String/Mob.img").Get(mob.ToString())?.Text("name") ?? mob.ToString();
    }
    public static void Draw()
    {
        if (!Visible || state == null) return;
        float s = BottomBar.S;
        var r = new Rect((UiKit.W - 475 * s) / 2, 40, 475 * s, 349 * s);
        GUI.DrawTexture(r, WzAssets.Texture("wz://UI/UIWindow.img/MonsterBook/backgrnd"));
        if (NativeUi.Button(new Rect(r.xMax - 22 * s, r.y + 5 * s, 12 * s, 12 * s), "UI/Basic.img/BtClose")) { Visible = false; return; }
        var oldFont = GUI.skin.font; GUI.skin.font = UiKit.Font;
        GUILayout.BeginArea(new Rect(r.x + 26 * s, r.y + 42 * s, 185 * s, 272 * s));
        GUILayout.Label("怪物手册 Lv." + state.bookLevel + " · " + state.cards.Count + "种");
        search = GUILayout.TextField(search, 32);
        listScroll = GUILayout.BeginScrollView(listScroll);
        foreach (int id in ids)
        {
            string name = NameOf(id); if (search != "" && !name.Contains(search)) continue;
            int count = state.cards.TryGetValue(id, out int value) ? value : 0;
            if (GUILayout.Button(name + "  " + count + "/5")) { selected = id; detailScroll = Vector2.zero; }
        }
        GUILayout.EndScrollView(); GUILayout.EndArea();
        GUILayout.BeginArea(new Rect(r.x + 244 * s, r.y + 42 * s, 201 * s, 272 * s));
        detailScroll = GUILayout.BeginScrollView(detailScroll);
        if (cards.TryGetValue(selected, out var card))
        {
            GUILayout.Label(NameOf(selected));
            GUILayout.Label(WzAssets.Texture(card.Get("info/icon")), GUILayout.Width(27 * s), GUILayout.Height(38 * s));
            var info = WzAssets.FileRoot("String/MonsterBook.img").Get(card.Int("info/mob").ToString());
            GUILayout.Label((info?.Text("episode") ?? "").Replace("\\n", "\n"));
            if (info?.Get("map") != null)
            {
                GUILayout.Label("出现地图"); FieldCatalog.Ensure();
                foreach (var mapId in info.Get("map").Children)
                    if (FieldCatalog.Maps.TryGetValue(System.Convert.ToInt32(mapId.Value), out var map)) GUILayout.Label(map.street + " / " + map.name);
            }
            if (state.cards.ContainsKey(selected) && GUILayout.Button(state.bookCover == selected ? "取消封面" : "设为封面"))
            {
                int cover = state.bookCover == selected ? 0 : selected;
                state = GameSession.Net.Social(new SocialRequest { characterId = GameSession.CharacterId, operation = SocialOperation.SetBookCover, cardId = cover });
                // Preserve this server-owned field when the existing character snapshot is saved.
                GameSession.Character.record.monsterbookcover = state.bookCover;
            }
        }
        GUILayout.EndScrollView(); GUILayout.EndArea(); GUI.skin.font = oldFont;
    }
}
