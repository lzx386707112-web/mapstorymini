using System.Collections.Generic;
using UnityEngine;
using MapleDash.Wz;

// 原版地图上的地面掉落物：金币与道具共用一套拾取入口（关卡 MesoDrop 已合并到这里）。
// 掉落内容、数量、概率全部来自 FieldDrops.json（原服 SQL 快照），这里不生成任何随机物品。
//
// 动画分三段：
//   1. 出场（原版客户端行为）：掉出瞬间有初速度——上抛 220~300px/s + 左右散开，
//      按 Map/Physics.img 的重力（2000px/s²）下落，落地按 0.34 恢复系数弹跳，最多两次；
//   2. 地面：金币袋用 Item/Special/0900.img/iconRaw 的三帧轻微起伏（原版数据），
//      道具图标原版静止，本地加了一点点呼吸缩放，移动端更好认出「这是可拾取的」；
//   3. 拾取：原地炸一个白点，0.12s 缩放消失，HUD 出获得行。
public sealed class GroundItem : MonoBehaviour
{
    public static readonly List<GroundItem> All = new List<GroundItem>();

    public int itemId;          // 0 = 金币
    public int amount;
    double expires;

    // —— 掉落物理 ——
    float vx, vy;
    int bounces;
    bool resting;
    float restGround;           // 落地时的地面高度（之后每帧不再扫 foothold）
    float restT;
    bool picking;               // 正在播放拾取消失动画

    SpriteRenderer sr;
    Sprite[] idleFrames;        // 金币袋待机帧（无则 null）
    int idleIdx;
    float idleT;

    static readonly Dictionary<string, Sprite> iconCache = new Dictionary<string, Sprite>();
    static string lastPickup = "";
    static double lastPickupAt;
    static double lastAutoPickupAt;
    static float gravityCache = -1f;

    public static string LastPickup
    {
        get { return Time.realtimeSinceStartupAsDouble - lastPickupAt < 2.5 ? lastPickup : ""; }
    }

    // 原版物理：Map/Physics.img 的 gravityAcc（像素/秒²）→ 世界单位/秒²
    static float Gravity
    {
        get
        {
            if (gravityCache < 0f)
            {
                try { gravityCache = WzAssets.FileRoot("Map/Physics.img").Int("gravityAcc", 2000) / Art.PPU; }
                catch { gravityCache = 50f; }
                if (gravityCache <= 0f) gravityCache = 50f;
            }
            return gravityCache;
        }
    }

    public static GroundItem Spawn(int itemId, int amount, Vector3 position)
    {
        if (amount <= 0) return null;
        var go = new GameObject(itemId == 0 ? "MesoDrop" : "ItemDrop_" + itemId);
        var s = go.AddComponent<SpriteRenderer>();
        go.transform.position = position + Vector3.up * 0.2f;
        var drop = go.AddComponent<GroundItem>();
        drop.itemId = itemId;
        drop.amount = amount;
        drop.expires = Time.realtimeSinceStartupAsDouble + 180;   // 原版 3 分钟
        drop.sr = s;
        if (itemId == 0)
        {
            drop.idleFrames = MesoBag(amount);
            s.sprite = drop.idleFrames != null ? drop.idleFrames[0] : MesoSprite();
        }
        else
        {
            s.sprite = ItemSprite(itemId);
        }
        s.sortingOrder = 290;

        // 出场初速度：上抛 + 左右散开（原版掉落物就是这个手感）
        drop.vy = Random.Range(5.5f, 7.5f);
        drop.vx = Random.Range(-0.45f, 0.45f);
        // 掉出瞬间的小白点：告诉玩家「东西出来了」
        Fx.Sparkle(go.transform.position, new Color(1f, 1f, 1f, 0.9f), 0.5f, 0.16f);
        AudioManager.Play("Game/DropItem", 0.55f);
        return drop;
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Update()
    {
        if (picking) return;
        if (Time.realtimeSinceStartupAsDouble >= expires) { Destroy(gameObject); return; }
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        if (!resting)
        {
            // 抛物线 + 弹跳（解析步进，避免落地判定随帧率抖）
            vy -= Gravity * dt;
            float nx = transform.position.x + vx * dt;
            float ny = transform.position.y + vy * dt;
            float ground = PlayerCtrl.GroundAt(nx, transform.position.y);
            if (vy < 0f && ny <= ground)
            {
                transform.position = new Vector3(nx, ground, 0f);
                if (bounces < 2 && -vy > 2.5f)
                {
                    vy = -vy * 0.34f;          // 原版掉落物的弹性
                    vx *= 0.55f;
                    bounces++;
                }
                else
                {
                    resting = true;
                    restGround = ground;
                    vy = 0f; vx = 0f;
                    restT = 0f;
                }
            }
            else transform.position = new Vector3(nx, ny, 0f);
            return;
        }

        // —— 地面闲置 ——
        restT += dt;
        // 金币袋：iconRaw 三帧轻微起伏（原版数据本身就这样）
        if (idleFrames != null && idleFrames.Length > 1)
        {
            idleT += dt;
            if (idleT >= 0.18f)
            {
                idleT -= 0.18f;
                idleIdx = (idleIdx + 1) % idleFrames.Length;
                sr.sprite = idleFrames[idleIdx];
            }
        }
        // 呼吸：±0.5px 上下浮动 + 1.5% 缩放（本地增量，原版地面物品静止）
        float bob = Mathf.Sin(restT * 2.4f);
        transform.position = new Vector3(transform.position.x, restGround + bob * 0.0125f, 0f);
        float k = 1f + bob * 0.015f;
        transform.localScale = new Vector3(k, k, 1f);
    }

    // ————————————— 图标 —————————————
    static Sprite meso;

    // 程序生成的小金币（0900.img 没打包时的兜底）
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

    // 原版金币袋：Item/Special/0900.img/0900000<tier>/iconRaw/0..2。
    // 分档按金额（小/中/大/特大四档袋子）；三帧是图标自带的轻微起伏。
    static readonly Sprite[][] mesoBags = new Sprite[4][];

    static Sprite[] MesoBag(int amount)
    {
        int tier = amount < 1000 ? 0 : amount < 10000 ? 1 : amount < 100000 ? 2 : 3;
        if (mesoBags[tier] != null) return mesoBags[tier];
        var frames = new List<Sprite>();
        string path = "Item/Special/0900.img";
        if (WzAssets.HasFile(path))
        {
            for (int i = 0; i < 3; i++)
            {
                var node = WzAssets.Node(path + "/0900000" + tier + "/iconRaw/" + i);
                if (node == null) continue;
                frames.Add(WzAssets.Sprite(node, "mesobag:" + tier + ":" + i));
            }
        }
        var result = frames.Count > 0 ? frames.ToArray() : null;
        mesoBags[tier] = result;
        return result;
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

    // ————————————— 拾取 —————————————
    // 按键拾取：半径 0.65u 内取最近的一个（原版也是走近了才吃）
    public static void TryPickupNearest()
    {
        var game = GameLoop.I;
        if (game == null || game.state != GameLoop.GameState.Playing || game.gameOver || game.player == null) return;
        PickupInRange(game.player.FeetPos, 0.65f);
    }

    // 走动自动拾取：原版走过就捡。加 0.12s 节流，一堆东西不会在一帧里全消失
    public static void TryAutoPickup(Vector2 feet)
    {
        if (Time.realtimeSinceStartupAsDouble - lastAutoPickupAt < 0.12) return;
        if (PickupInRange(feet, 0.55f)) lastAutoPickupAt = Time.realtimeSinceStartupAsDouble;
    }

    static bool PickupInRange(Vector2 feet, float radius)
    {
        GroundItem best = null;
        float bestD = radius * radius;
        foreach (var drop in All)
        {
            if (drop == null || !drop.enabled || drop.amount <= 0 || drop.picking) continue;
            float d = ((Vector2)drop.transform.position - feet).sqrMagnitude;
            if (d <= bestD) { bestD = d; best = drop; }
        }
        if (best == null) return false;
        best.Pickup();
        return true;
    }

    void Pickup()
    {
        if (itemId == 0)
        {
            if ((long)Profile.gold + amount > int.MaxValue) return;
            Profile.AddGold(amount);
            lastPickup = "+" + amount + " 金币";
        }
        else
        {
            FieldItemInfo info;
            string name = FieldCatalog.Items.TryGetValue(itemId, out info) && !string.IsNullOrEmpty(info.name)
                ? info.name : itemId.ToString();
            Profile.AddItem(itemId, amount);
            lastPickup = name + " x" + amount;
        }
        lastPickupAt = Time.realtimeSinceStartupAsDouble;

        // 拾取反馈：原地白点 + 缩放消失（0.12s）
        All.Remove(this);
        picking = true;
        Fx.Sparkle(transform.position, new Color(1f, 1f, 1f, 0.95f), 0.55f, 0.14f);
        AudioManager.Play("Game/PickUpItem", 0.8f);
        var f = gameObject.AddComponent<FxLife>();
        f.life = 0.12f;
        f.grow = new Vector3(0.05f, 0.05f, 1f);
        Destroy(gameObject, 0.13f);
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
