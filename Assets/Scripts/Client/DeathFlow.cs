// 死亡流程（需求 3）。逐条对齐源端：
//   1) HP 归零 → 客户端播 dead/die 动作并在原地立墓碑（Effect/Tomb.img）
//   2) 服务端结算经验惩罚（Character.playerDead，Character.java:7478-7545）
//   3) 弹出 UIWindow.img/raise 确认窗（源端由玩家点「确定」才发换图包）
//   4) 确认 → ChangeMap(fromDying=1) → 服务端 respawn 到 returnMap 并按规则给血
//      （ChangeMapHandler:99-118 + Character.respawn Character.java:7596-7613）
//
// 表现层（动画/墓碑/弹窗）全在客户端，规则与数值全在 Server/Death/DeathService，
// 两边只通过 ITransport 通信 —— 联机时这段流程不用改。
using System.Collections.Generic;
using UnityEngine;

public class DeathFlow : MonoBehaviour
{
    public static DeathFlow I { get; private set; }

    public enum Phase { Idle, Dying, Confirm }
    public Phase phase = Phase.Idle;
    public bool Active => phase != Phase.Idle;

    // 墓碑：Effect/Tomb.img/fall/0..7（落下）→ land/0（静止）
    const string TombFall = "wz://Effect/Tomb.img/fall/";
    const string TombLand = "wz://Effect/Tomb.img/land/0";
    const int FallFrames = 8;
    const float FallFrameTime = 0.09f;      // 8 帧 ≈ 0.72s 落完
    const float ConfirmDelay = 1.15f;       // 墓碑落地到弹窗的停顿（源端也是有一段黑屏/静止）

    GameObject tombGo;
    SpriteRenderer tombSr;
    readonly List<Sprite> fall = new List<Sprite>();
    Sprite land;
    float timer;
    int fallIndex;

    PlayerDeadResponse settlement;
    int targetMapId = -1;
    string targetMapName = "附近城镇";

    GUIStyle _title, _label, _small, _btn, _warn;

    void Awake() { I = this; }
    void OnDestroy() { if (I == this) I = null; }

    // —————————————————— 进入死亡 ——————————————————

    public void Begin(PlayerCtrl p)
    {
        if (phase != Phase.Idle) return;
        phase = Phase.Dying;
        timer = 0f;
        fallIndex = 0;

        p.PlayDeadPose();
        SpawnTombstone(p);

        // 经验惩罚由服务端算；护符数量也从服务端背包取
        int mapId = GameLoop.I != null && GameLoop.I.field != null ? GameLoop.I.field.Map.id
                    : Profile.LastMapId;
        settlement = GameSession.PlayerDead(mapId);

        // 目标城镇名（确认按钮上直接写出来，和源端 raise 窗口一致）
        targetMapId = GameSession.PeekRespawnMap(mapId);
        FieldCatalog.Ensure();
        if (targetMapId >= 0 && FieldCatalog.Maps.TryGetValue(targetMapId, out var m))
            targetMapName = string.IsNullOrEmpty(m.name) ? m.street : m.name;
        if (string.IsNullOrEmpty(targetMapName)) targetMapName = "附近城镇";

        // 死亡即写盘：源端 playerDead 是同步落库的，避免强退后经验/护符回滚
        GameSession.SaveToDb();
    }

    void SpawnTombstone(PlayerCtrl p)
    {
        fall.Clear();
        for (int i = 0; i < FallFrames; i++)
        {
            var tex = Art.Tex(TombFall + i);
            if (tex != null) fall.Add(SpriteFrom(tex));
        }
        var lt = Art.Tex(TombLand);
        land = lt != null ? SpriteFrom(lt) : (fall.Count > 0 ? fall[fall.Count - 1] : null);

        if (tombGo != null) { Destroy(tombGo); tombGo = null; }
        if (land == null && fall.Count == 0) return;   // 素材缺失时至少不能崩

        tombGo = new GameObject("[Tombstone]");
        var feet = p != null ? (Vector3)p.FeetPos : new Vector3(0f, 0f, 0f);
        tombGo.transform.position = new Vector3(feet.x, feet.y, feet.z + 0.01f);
        tombSr = tombGo.AddComponent<SpriteRenderer>();
        tombSr.sortingOrder = 320;      // 盖在角色上面
        tombSr.sprite = fall.Count > 0 ? fall[0] : land;
    }

    static Sprite SpriteFrom(Texture2D tex)
    {
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                             new Vector2(0.5f, 0f), Art.PPU, 0, SpriteMeshType.FullRect);
    }

    // —————————————————— 每帧 ——————————————————

    void Update()
    {
        if (phase == Phase.Idle) return;
        timer += Time.unscaledDeltaTime;

        // 墓碑落下动画
        if (tombSr != null && fall.Count > 0)
        {
            int idx = Mathf.Min(fall.Count - 1, Mathf.FloorToInt(timer / FallFrameTime));
            if (idx != fallIndex || tombSr.sprite == null)
            {
                fallIndex = idx;
                bool done = timer >= FallFrames * FallFrameTime;
                tombSr.sprite = done && land != null ? land : fall[idx];
            }
        }

        if (phase == Phase.Dying && timer >= ConfirmDelay) phase = Phase.Confirm;
    }

    // —————————————————— 复活确认窗 ——————————————————

    void OnGUI()
    {
        if (phase != Phase.Confirm) return;
        EnsureStyles();
        UiKit.Begin();

        float w = 420f, h = 230f;
        float x = UiKit.W / 2f - w / 2f, y = UiKit.H / 2f - h / 2f;

        // 背景：源端 UIWindow.img/raise/backgrnd（三段拼图）
        var top = Art.Tex("wz://UI/UIWindow.img/raise/backgrnd/top");
        if (top != null) GUI.DrawTexture(new Rect(x, y, w, h), top, ScaleMode.StretchToFill);
        else UiKit.Fill(new Rect(x, y, w, h), new Color(0.1f, 0.11f, 0.15f, 0.96f));
        UiKit.Fill(new Rect(x, y, w, 4f), new Color(0.8f, 0.35f, 0.3f));

        GUI.Label(new Rect(x + 24f, y + 26f, w - 48f, 30f), "你 死 了", _title);

        string loss;
        if (settlement == null) loss = "经验值未发生变化";
        else if (settlement.noExpLoss) loss = "新手 / 安全地图：经验值不变";
        else if (settlement.usedSafetyCharm) loss = "安全护符生效：经验值不变（消耗 1 个护符）";
        else loss = "损失经验值 " + settlement.expLost;

        GUI.Label(new Rect(x + 24f, y + 66f, w - 48f, 24f), loss, _label);
        GUI.Label(new Rect(x + 24f, y + 96f, w - 48f, 22f),
                  "复活地点：" + targetMapName, _small);

        if (GUI.Button(new Rect(x + 24f, y + h - 62f, w - 48f, 42f),
                       "回到 " + targetMapName, _btn))
            Confirm();
    }

    void EnsureStyles()
    {
        if (_title != null) return;
        _title = new GUIStyle { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.98f, 0.92f, 0.9f) } };
        _label = new GUIStyle { fontSize = 15, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.9f, 0.88f, 0.86f) } };
        _small = new GUIStyle { fontSize = 13, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.72f, 0.72f, 0.75f) } };
        _btn = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold };
        _warn = new GUIStyle { fontSize = 13, normal = { textColor = new Color(1f, 0.6f, 0.55f) } };
    }

    // —————————————————— 确认：回城 ——————————————————

    void Confirm()
    {
        bool charm = settlement != null && settlement.usedSafetyCharm;
        var res = GameSession.Respawn(charm);
        if (!res.ok)
        {
            // 服务端没给目标图（数据缺失）时退回客户端本地重生，至少不卡死
            LocalRespawn();
            return;
        }

        var gl = GameLoop.I;
        if (gl != null && gl.field != null && res.mapId != gl.field.Map.id)
            gl.field.Enter(res.mapId, null);
        else if (gl != null && gl.field != null)
            gl.field.RespawnPlayer();

        if (gl != null && gl.player != null)
        {
            gl.player.Revive(res.hp, res.mp);
            gl.player.ResetTerrain();
            if (gl.camRig != null) gl.camRig.SnapTo(gl.player.worldX);
        }
        Profile.LastMapId = res.mapId;
        Profile.Save();
        Finish();
    }

    void LocalRespawn()
    {
        var gl = GameLoop.I;
        if (gl != null && gl.field != null) gl.field.RespawnPlayer();
        if (gl != null && gl.player != null)
        {
            gl.player.Revive(50, gl.player.mpMax > 0 ? (int)gl.player.mpMax : 5);
            gl.player.ResetTerrain();
        }
        Finish();
    }

    void Finish()
    {
        phase = Phase.Idle;
        timer = 0f;
        settlement = null;
        targetMapId = -1;
        if (tombGo != null) { Destroy(tombGo); tombGo = null; tombSr = null; }
        var gl = GameLoop.I;
        if (gl != null)
        {
            gl.gameOver = false;
            gl.deaths.Clear();
        }
    }

    /// <summary>退出游戏 / 换角色前强制清场，避免墓碑残留。</summary>
    public void ForceClear()
    {
        if (phase == Phase.Idle) return;
        Finish();
    }
}
