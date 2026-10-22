using System.Collections.Generic;
using UnityEngine;

// 原版地图上的地面掉落物：金币与道具共用一套拾取入口。
// 掉落内容、数量、概率全部来自 FieldDrops.json（原服 SQL 快照），这里不生成任何随机物品。
public sealed class GroundItem : MonoBehaviour
{
    public static readonly List<GroundItem> All = new List<GroundItem>();

    public int itemId;          // 0 = 金币
    public int amount;
    double expires;

    static readonly Dictionary<string, Sprite> iconCache = new Dictionary<string, Sprite>();
    static string lastPickup = "";
    static double lastPickupAt;

    public static string LastPickup
    {
        get { return Time.realtimeSinceStartupAsDouble - lastPickupAt < 2.5 ? lastPickup : ""; }
    }

    public static GroundItem Spawn(int itemId, int amount, Vector3 position)
    {
        if (amount <= 0) return null;
        var go = new GameObject(itemId == 0 ? "MesoDrop" : "ItemDrop_" + itemId);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = itemId == 0 ? MesoSprite() : ItemSprite(itemId);
        sr.sortingOrder = 290;
        go.transform.position = position + Vector3.up * 0.2f;
        var drop = go.AddComponent<GroundItem>();
        drop.itemId = itemId;
        drop.amount = amount;
        drop.expires = Time.realtimeSinceStartupAsDouble + 180;
        return drop;
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Update()
    {
        if (Time.realtimeSinceStartupAsDouble >= expires) Destroy(gameObject);
    }

    static Sprite meso;

    static Sprite MesoSprite()
    {
        if (meso == null)
        {
            var tex = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            var px = new Color32[16 * 16];
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    float dx = x - 7.5f, dy = y - 7.5f;
                    bool inside = dx * dx + dy * dy <= 49f;
                    px[y * 16 + x] = inside
                        ? new Color32(255, 214, 64, 255)
                        : new Color32(0, 0, 0, 0);
                }
            tex.SetPixels32(px);
            tex.filterMode = FilterMode.Point;
            tex.Apply();
            meso = Sprite.Create(tex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), Art.PPU);
        }
        return meso;
    }

    static Sprite ItemSprite(int itemId)
    {
        FieldItemInfo info;
        if (!FieldCatalog.Items.TryGetValue(itemId, out info) || string.IsNullOrEmpty(info.icon))
            return Fallback();
        Sprite sp;
        if (iconCache.TryGetValue(info.icon, out sp) && sp != null) return sp;
        var tex = Art.Tex(info.icon);
        if (tex == null) return Fallback();
        sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                           new Vector2(0.5f, 0.5f), Art.PPU);
        sp.name = info.icon;
        iconCache[info.icon] = sp;
        return sp;
    }

    static Sprite fallback;

    // 装备类掉落（1xxxxxx）图标在 Character.wz，本阶段未导出；用中性方块占位，
    // 但数量与物品 ID 仍然是原服掉落表里的真实值。
    static Sprite Fallback()
    {
        if (fallback == null)
        {
            var tex = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            var px = new Color32[16 * 16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(190, 205, 235, 255);
            tex.SetPixels32(px);
            tex.filterMode = FilterMode.Point;
            tex.Apply();
            fallback = Sprite.Create(tex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), Art.PPU);
        }
        return fallback;
    }

    public static void TryPickupNearest()
    {
        var game = GameLoop.I;
        if (game == null || game.state != GameLoop.GameState.Playing || game.gameOver || game.player == null) return;

        GroundItem best = null;
        float distance = 1.2f * 1.2f;
        foreach (var drop in All)
        {
            if (!drop.enabled || drop.amount <= 0) continue;
            float d = ((Vector2)drop.transform.position - game.player.FeetPos).sqrMagnitude;
            if (d <= distance) { distance = d; best = drop; }
        }
        if (best == null) return;

        if (best.itemId == 0)
        {
            if ((long)Profile.gold + best.amount > int.MaxValue) return;
            Profile.AddGold(best.amount);
            lastPickup = "+" + best.amount + " 金币";
        }
        else
        {
            FieldItemInfo info;
            string name = FieldCatalog.Items.TryGetValue(best.itemId, out info) && !string.IsNullOrEmpty(info.name)
                ? info.name : best.itemId.ToString();
            Profile.AddItem(best.itemId, best.amount);
            lastPickup = name + " x" + best.amount;
        }
        lastPickupAt = Time.realtimeSinceStartupAsDouble;
        best.amount = 0;
        best.enabled = false;
        Destroy(best.gameObject);
    }

    public static int Count()
    {
        int n = 0;
        foreach (var d in All) if (d != null && d.enabled) n++;
        return n;
    }

    public static void Clear()
    {
        foreach (var drop in All.ToArray()) if (drop != null) { drop.enabled = false; Destroy(drop.gameObject); }
        All.Clear();
    }
}
