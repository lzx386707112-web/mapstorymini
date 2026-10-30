// Native death presentation: CUser 0x92FD5A, notice 0x897B5F/0x897C94.
using System.Collections.Generic;
using UnityEngine;
using MapleDash.Wz;

public class DeathFlow : MonoBehaviour
{
    public static DeathFlow I { get; private set; }

    public enum Phase { Idle, Dying, Confirm }
    public Phase phase = Phase.Idle;
    public bool Active => phase != Phase.Idle;

    const float ConfirmDelay = 2.2f;
    const float DropDuration = .88f;
    GameObject tombGo;
    SpriteRenderer tombSr;
    readonly List<ImgNode> fall = new List<ImgNode>();
    Sprite land;
    Vector3 tombTarget;
    float timer;
    PlayerCtrl deadPlayer;

    PlayerDeadResponse settlement;
    void Awake() { I = this; }
    void OnDestroy() { RemoveTombstone(); if (I == this) I = null; }
    void RemoveTombstone()
    {
        deadPlayer?.ClearDeathPresentation();
        deadPlayer = null;
        if (tombGo != null)
        {
            if (Application.isPlaying) Destroy(tombGo);
            else DestroyImmediate(tombGo);
        }
        tombGo = null; tombSr = null;
    }

    // —————————————————— 进入死亡 ——————————————————

    public void Begin(PlayerCtrl p)
    {
        if (phase != Phase.Idle) return;
        phase = Phase.Dying;
        timer = 0f;

        p.PlayDeadPose();
        SpawnTombstone(p);

        // 经验惩罚由服务端算；护符数量也从服务端背包取
        int mapId = GameLoop.I != null && GameLoop.I.field != null ? GameLoop.I.field.Map.id
                    : Profile.LastMapId;
        settlement = GameSession.PlayerDead(mapId);

        // 死亡即写盘：源端 playerDead 是同步落库的，避免强退后经验/护符回滚
        GameSession.SaveToDb();
    }

    void SpawnTombstone(PlayerCtrl p)
    {
        fall.Clear();
        fall.AddRange(WzAssets.Frames(WzAssets.Node("Effect/Tomb.img/fall")));
        land = WzAssets.Sprite(WzAssets.Node("Effect/Tomb.img/land/0"));
        RemoveTombstone();
        deadPlayer = p;
        deadPlayer?.PresentDeath(0f);
        tombGo = new GameObject("[Tombstone]");
        tombTarget = p != null ? (Vector3)p.FeetPos : Vector3.zero;
        tombGo.transform.position = tombTarget + Vector3.up * (600f / Art.PPU);
        tombSr = tombGo.AddComponent<SpriteRenderer>();
        tombSr.sortingOrder = p != null ? p.SortingOrder + 1 : 320;
        tombSr.sprite = fall.Count > 0 ? WzAssets.Sprite(fall[0]) : land;
    }

    void Update()
    {
        if (phase == Phase.Idle) return;
        timer += Time.unscaledDeltaTime;
        deadPlayer?.PresentDeath(timer);
        if (tombSr != null)
        {
            if (deadPlayer != null) tombSr.sortingOrder = deadPlayer.SortingOrder + 1;
            tombGo.transform.position = tombTarget + Vector3.up *
                (600f / Art.PPU * (1f - Mathf.Clamp01(timer / DropDuration)));
            float age = timer;
            Sprite frame = land;
            foreach (var node in fall)
            {
                float duration = Mathf.Max(1, Mathf.Abs(node.Int("delay", 100))) / 1000f;
                if (age < duration) { frame = WzAssets.Sprite(node); break; }
                age -= duration;
            }
            tombSr.sprite = frame;
        }
        if (phase == Phase.Dying && timer > ConfirmDelay) phase = Phase.Confirm;
    }

    // 4A95BF excludes special fields and event-map families before offering
    // Notice/2. The server independently checks possession at confirmation.
    public static bool WheelAllowed(int fieldType,int mapId)
        => fieldType!=1 && !(fieldType>=3 && fieldType<=5) && fieldType!=7 &&
            fieldType!=10 && fieldType!=11 && fieldType!=15 && mapId/100000000!=9 &&
            mapId/1000!=200090 && mapId/1000000!=390;
    public static bool OfferWheel
    {
        get
        {
            var map=GameLoop.I?.field?.Map;
            return map!=null && WheelAllowed(map.fieldType,map.id) && Profile.ItemCount(DeathService.WheelOfFortune)>0;
        }
    }
    void OnGUI()
    {
        if (phase != Phase.Confirm) return;
        UiKit.Begin();
        var matrix = GUI.matrix;
        int depth = GUI.depth;
        float scale = BottomBar.S;
        GUI.matrix = matrix * Matrix4x4.Scale(new Vector3(scale, scale, 1));
        GUI.depth = -100;
        try
        {
            // Original 800x600 position (257,97), retain its offset from screen center.
            float x = UiKit.W / scale / 2f - 143;
            float y = UiKit.H / scale / 2f - 203;
            bool wheel=OfferWheel;
            var background = Art.Tex("wz://UI/UIWindow.img/Notice/"+(wheel ? 2 : 0));
            if (background != null) GUI.DrawTexture(new Rect(x, y, 286, 146), background);
            bool submit=wheel ? NativeUi.Button(new Rect(x+92,y+115,47,19),"UI/Basic.img/BtYes2",Pointer.Pos/scale) :
                NativeUi.Button(new Rect(x+124,y+115,47,19),"UI/Basic.img/BtOK2",Pointer.Pos/scale);
            if(wheel && NativeUi.Button(new Rect(x+148,y+115,47,19),"UI/Basic.img/BtNo2",Pointer.Pos/scale))
            {Confirm(false);return;}
            var e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            { submit = true; e.Use(); }
            if (submit) Confirm(wheel);
            else if(timer>ConfirmDelay+600f) Confirm(false);
        }
        finally { GUI.matrix = matrix; GUI.depth = depth; }
    }

    // —————————————————— 确认：回城 ——————————————————

    void Confirm(bool wheel=false)
    {
        bool charm = settlement != null && settlement.usedSafetyCharm;
        var p=GameLoop.I?.player;
        var res = GameSession.Respawn(charm,wheel,p!=null ? (int)(p.FeetPos.x*Art.PPU) : 0,p!=null ? (int)(-p.FeetPos.y*Art.PPU) : 0);
        if (!res.ok)
        {
            // 服务端没给目标图（数据缺失）时退回客户端本地重生，至少不卡死
            LocalRespawn();
            return;
        }

        var gl = GameLoop.I;
        if(res.wheelUsed && gl?.field!=null)
            gl.field.WarpTo(res.mapId,portalIndex:res.spawnPoint);
        else if (gl != null && gl.field != null && res.mapId != gl.field.Map.id)
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
        RemoveTombstone();
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
