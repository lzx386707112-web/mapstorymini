using System.Collections.Generic;
using UnityEngine;
using MapleDash.Wz;

// Original drop state machine and raw IMG canvases; see drop-and-pickup.md.
public sealed class GroundItem : MonoBehaviour
{
    public static readonly List<GroundItem> All = new List<GroundItem>();
    public int itemId; // 0 = mesos
    public int amount;
    public Server.Field.MapItem bound;
    public static System.Func<Server.Field.MapItem, bool> AuthoritativePickup;

    SpriteRenderer sr;
    Sprite[] frames;
    Vector3 canvasOffset;
    int sourceX, sourceY, targetX, targetY;
    int landingTick = -1;
    double born, removedAt = -1;
    double expiresAt=double.PositiveInfinity;
    bool resting, picking, removing;
    System.Func<Vector2?> pickupTarget;
    static string lastPickup = "";
    static double lastPickupAt;
    static double lastDropSoundAt = -1;
    static readonly Dictionary<int, Sprite[]> icons = new Dictionary<int, Sprite[]>();

    public static string LastPickup => Time.realtimeSinceStartupAsDouble - lastPickupAt < 2.5 ? lastPickup : "";

    public static GroundItem Spawn(int itemId, int amount, Vector3 position,
        Server.Field.MapItem bound = null, Vector3? from = null, bool alreadyOnGround = false)
    {
        if (amount <= 0) return null;
        Sprite[] art = LoadIcons(itemId, amount);
        if (art == null || art.Length == 0 || art[0] == null)
        {
            Debug.LogError("[GroundItem] Missing original iconRaw: " + itemId);
            return null;
        }
        var go = new GameObject(itemId == 0 ? "MesoDrop" : "ItemDrop_" + itemId);
        var drop = go.AddComponent<GroundItem>();
        drop.sr = go.AddComponent<SpriteRenderer>();
        drop.itemId = itemId;
        drop.amount = amount;
        drop.bound = bound;
        drop.frames = art;
        drop.sr.sprite = art[0];
        // Native layer is centered on its vector; frame origin deltas stay intact.
        drop.canvasOffset = -art[0].bounds.center;
        int halfHeight = (int)art[0].rect.height / 2;
        drop.canvasOffset.y += (art[0].rect.height * 0.5f - halfHeight) / Art.PPU;
        // Keep the original meso ground clearance (origin.y - canvas height).
        // Items share the small-coin baseline instead of sinking below it.
        Sprite baseline = itemId == 0 ? art[0] : LoadIcons(0, 1)[0];
        int clearance = Mathf.RoundToInt(-baseline.pivot.y);
        Vector3 start = from ?? position;
        drop.sourceX = (int)(start.x * Art.PPU);
        drop.sourceY = (int)(-start.y * Art.PPU) - halfHeight - clearance;
        drop.targetX = (int)(position.x * Art.PPU);
        drop.targetY = (int)(-position.y * Art.PPU) - halfHeight - clearance;
        drop.born = Time.realtimeSinceStartupAsDouble;
        if (bound==null && GameLoop.I?.field?.Map?.everlast!=true)
            drop.expiresAt=drop.born+(GameServer.Local==null ? 180000 : GameConfigDao.GetInt(GameServer.Local.Db,"item_expire_time",180000))/1000.0;
        drop.resting = alreadyOnGround;
        if (alreadyOnGround) drop.landingTick = 0;
        // Original drop z is K+9 (user K+2, mob K+1).
        drop.sr.sortingOrder = 307;
        drop.RenderAt(0);
        if (!alreadyOnGround && drop.born - lastDropSoundAt > 0.3)
        {
            AudioManager.Play("Game/DropItem", pitchVary: false);
            lastDropSoundAt = drop.born;
        }
        return drop;
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Update()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        if (!removing && bound == null && now>expiresAt) Remove(0, null);
        RenderAt((int)((now - born) * 1000));
    }

    void RenderAt(int ageMs)
    {
        sr.sprite = frames[itemId == 0 ? NativeDropMotion.MesoFrame(amount, ageMs) : 0];
        int x, y;
        if (removing)
        {
            int elapsed = (int)((Time.realtimeSinceStartupAsDouble - removedAt) * 1000);
            if (picking)
            {
                var target = pickupTarget?.Invoke();
                if (!target.HasValue) { Destroy(gameObject); return; }
                if (!NativeDropMotion.Pickup(targetX, targetY, (int)(target.Value.x * Art.PPU),
                    (int)(-target.Value.y * Art.PPU), elapsed, out x, out y, out int alpha))
                { Destroy(gameObject); return; }
                sr.color = new Color32(255, 255, 255, (byte)alpha);
            }
            else
            {
                if (elapsed >= 1000) { Destroy(gameObject); return; }
                x = targetX; y = targetY;
                sr.color = new Color(1, 1, 1, 1 - elapsed / 1000f);
            }
            transform.rotation = Quaternion.identity;
        }
        else
        {
            int tick = ageMs / 30;
            if (!resting)
            {
                NativeDropMotion.Flight(sourceX, sourceY, targetX, targetY, ageMs, out x, out y, out resting);
                if (resting)
                {
                    landingTick = NativeDropMotion.LandingTick(sourceY, targetY);
                }
            }
            else { x = targetX; y = targetY; }
            if (resting)
            {
                // Source converts the sine displacement with truncation, unlike flight rounding.
                y = (int)(targetY + NativeDropMotion.Bob(tick - landingTick));
                transform.rotation = Quaternion.identity;
            }
            else if (itemId != 0)
                transform.rotation = Quaternion.Euler(0, 0, -(ageMs % 300) * 360f / 300f);
        }
        transform.position = new Vector3(x / Art.PPU, -y / Art.PPU, 0);
        // Renderer is on this root: compensate raw canvas origin in the sprite's own rotated basis.
        transform.position += transform.rotation * canvasOffset;
    }

    static Sprite[] LoadIcons(int id, int amount)
    {
        int key = id == 0 ? -1 - NativeDropMotion.MesoTier(amount) : id;
        if (icons.TryGetValue(key, out var cached)) return cached;
        var result = new List<Sprite>();
        if (id == 0)
        {
            string path = "Item/Special/0900.img/0900000" + NativeDropMotion.MesoTier(amount) + "/iconRaw/";
            for (int i = 0; i < 4; i++)
            {
                var node = WzAssets.Node(path + i);
                if (node == null) return null;
                result.Add(WzAssets.Sprite(node, path + i));
            }
        }
        else
        {
            var node = NativeItemSource.Node(id);
            var raw = node?.Get("info/iconRaw") ?? node?.Get("iconRaw") ?? NativeItemSource.IconNode(id);
            if (raw == null) return null;
            result.Add(WzAssets.Sprite(raw, "drop:" + id));
        }
        return icons[key] = result.ToArray();
    }

    public static void TryPickupNearest()
    {
        var game = GameLoop.I;
        if (game == null || game.state != GameLoop.GameState.Playing || game.gameOver || game.player == null) return;
        PickupInRange(game.player.FeetPos);
    }

    static bool PickupInRange(Vector2 feet)
    {
        int x = (int)(feet.x * Art.PPU), y = (int)(-feet.y * Art.PPU);
        // CDropPool walks its list and requests the first eligible resting drop, not nearest visual sprite.
        foreach (var drop in All)
        {
            if (drop == null || !drop.enabled || drop.removing || !drop.resting || drop.amount <= 0) continue;
            var item = drop.bound;
            if (item != null && item.dropperId != 0 && item.ownerCharacterId > 0 && item.type < 2 &&
                item.AgeMs((long)(Time.realtimeSinceStartupAsDouble * 1000)) < 15000)
            {
                int party = Server.Field.MapItemService.PartyOf?.Invoke(GameSession.CharacterId) ?? -1;
                if (item.ownerCharacterId != GameSession.CharacterId &&
                    !(item.partyOwnerId >= 0 && item.partyOwnerId == party)) continue;
            }
            if (NativeDropMotion.InPickupRange(x, y, drop.targetX, drop.targetY)) return drop.Pickup();
        }
        return false;
    }

    bool Pickup()
    {
        if (bound != null)
        {
            // Synchronous Removed event starts the visual; a refused request leaves it on the ground.
            return AuthoritativePickup != null && AuthoritativePickup(bound);
        }
        if (itemId == 0)
        {
            if ((long)Profile.gold + amount > int.MaxValue) return false;
            Profile.AddGold(amount);
            lastPickup = "+" + amount + " 金币";
        }
        else
        {
            FieldItemInfo info;
            string name = FieldCatalog.Items.TryGetValue(itemId, out info) && !string.IsNullOrEmpty(info.name)
                ? info.name : itemId.ToString();
            if (itemId / 10000 == 238)
            {
                if (!NativeMonsterBook.Collect(itemId, amount)) return false;
            }
            else Profile.AddItem(itemId, amount);
            lastPickup = name + " x" + amount;
        }
        lastPickupAt = Time.realtimeSinceStartupAsDouble;
        Remove(2, LocalPickupTarget, true);
        return true;
    }

    internal static Vector2? LocalPickupTarget()
    {
        var player = GameLoop.I != null ? GameLoop.I.player : null;
        return player != null ? player.FeetPos + Vector2.up * (player.BodyHeight * 0.5f) : (Vector2?)null;
    }

    public void Remove(int reason, System.Func<Vector2?> target, bool playSound = false)
    {
        if (removing) return;
        removing = true;
        removedAt = Time.realtimeSinceStartupAsDouble;
        picking = reason == 2 || reason == 3 || reason == 5;
        pickupTarget = target;
        if (reason == 1 || (picking && target == null)) { Destroy(gameObject); return; }
        if (picking && playSound) AudioManager.Play("Game/PickUpItem", pitchVary: false);
    }

    public static int Count()
    {
        int count = 0;
        foreach (var drop in All) if (drop != null && drop.enabled && !drop.removing) count++;
        return count;
    }

    public static void Clear()
    {
        foreach (var drop in All.ToArray()) if (drop != null) { drop.enabled = false; Destroy(drop.gameObject); }
        All.Clear();
    }
}
