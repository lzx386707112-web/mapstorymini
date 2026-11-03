using System;
using System.Collections.Generic;
using UnityEngine;

// 机器人实体 —— 源端 CUserPool 里的 android 单位（Char.initAndroid 生成、随主人走同一张图）。
//
// 全场一个本地实例；普通水平决策按已核实282控制器1/2模式。
// 纵向落脚/速度/超距落位仍是移植实现，未迁移完整282物理。
// 逆向依据与边界见根目录 docs/tms282-skill/12-android-regression.md。
//
// 装备联动靠轮询：每 0.2s 比对槽位快照，发生变化才 Resolve，外观 key 变了才重建。
// 这样「装上/脱下/换 AP 时装」都不用在装备流程里额外插通知，也不会漏掉后台 GM 发放。
public sealed class NativeAndroid : MonoBehaviour
{
    const float TeleportGap = 320f;     // 掉队过远直接落位
    const float ReadInterval = .2f;     // 装备轮询间隔
    const float BalloonSeconds = 5f;     // 282 CAndroid chat dispatch: 0x1388 ms
    const float FarewellSeconds = 2.2f; // 卸下时留一点时间把 bye 台词说完再销毁
    const float ArtBaseHeight = 768f;

    static readonly Func<int, int> SlotOf = Profile.EquipOfSlot;
    static readonly string[] ActionFields = { "action", "face", "chat", "sound" };

    PlayerCtrl owner;
    GameObject visual;
    SpriteRenderer body;
    SpriteAnim anim;
    string key;
    int templateId, balloonStyle, lastLevel, lastJob = -1;
    float readAt, facing = 1, lastHp;
    float byeAt;                        // >0 = 正在告别（等这刻销毁）
    bool deathPlayed, spawned;
    Vector2 feet;
    NativeMovementPhysics movementPhysics;
    double horizontalVelocity;
    int followDirection, previousAndroidX;

    string balloonText;
    float balloonUntil;
    float balloonSpan = BalloonSeconds;  // 告别使用单独的较短时长
    float lastActionAt = float.NegativeInfinity;
    readonly System.Random random = new System.Random();
    readonly int[] equipmentSnapshot = new int[2 + NativeAndroidService.ApSlots.Length];
    int snapshotCharacter;
    AndroidState resolved;
    GUIStyle balloonMeasure, balloonLabel;
    LandingQuery ownerLanding, localLanding;

    // Terrain is replaced on map entry. Exact inputs only: no rounding or slower polling.
    struct LandingQuery
    {
        List<Vector4> terrain;
        int count;
        float x, ceiling, result;
        bool valid, found;
        public bool Read(float nextX, float nextCeiling, out float landing)
        {
            var nextTerrain = PlayerCtrl.Terrain;
            int nextCount = nextTerrain?.Count ?? 0;
            if (!valid || !ReferenceEquals(terrain, nextTerrain) || count != nextCount ||
                x != nextX || ceiling != nextCeiling)
            {
                terrain = nextTerrain; count = nextCount; x = nextX; ceiling = nextCeiling;
                found = PlayerCtrl.FindLanding(x, ceiling, float.NegativeInfinity, -1, out result);
                valid = true;
            }
            landing = result;
            return found;
        }
    }

    /// <summary>最近一次装配失败的原因（「請安裝心臟」/「心臟等級過高」/模板缺失），空 = 正常。</summary>
    public string Reason { get; private set; } = "";

    // —————————————— 挂载 ——————————————

    public void Tick(PlayerCtrl player, float dt)
    {
        using var perf = Server.Bot.BotPerf.Timer("Android.Tick");
        owner = player;
        if (!GameSession.InGame || player == null || GameLoop.I == null ||
            GameLoop.I.state != GameLoop.GameState.Playing)
        { Clear(); return; }

        if (Time.unscaledTime >= readAt)
        {
            readAt = Time.unscaledTime + ReadInterval;
            Sync();
        }
        if (visual == null) return;
        Move(dt);
        Events();
    }

    // —————————————— 装备 → 外观 ——————————————

    void Sync()
    {
        using var perf = Server.Bot.BotPerf.Timer("Android.Sync");
        bool changed = resolved == null || snapshotCharacter != GameSession.CharacterId;
        for (int i = 0; i < equipmentSnapshot.Length; i++)
        {
            int slot = i == 0 ? NativeAndroidService.RobotSlot : i == 1
                ? NativeAndroidService.HeartSlot : NativeAndroidService.ApSlots[i - 2];
            int id = SlotOf(slot);
            if (equipmentSnapshot[i] != id) changed = true;
            equipmentSnapshot[i] = id;
        }
        snapshotCharacter = GameSession.CharacterId;
        if (changed) resolved = NativeAndroidService.Resolve(snapshotCharacter, SlotOf);
        var next = resolved;
        Reason = next.reason;
        if (!next.visible)
        {
            // 卸下机器人 / 拔掉心脏：先说再见再消失。源端 bye 是有台词的
            // （"我會想你的…"），立刻 Destroy 会把气泡一起掐掉，等于这句永远看不到。
            if (visual != null && byeAt == 0f)
            {
                byeAt = Time.unscaledTime + FarewellSeconds;
                PlayAction("bye", FarewellSeconds - .2f);
            }
            if (byeAt > 0f && Time.unscaledTime >= byeAt) { byeAt = 0f; Clear(); }
            return;
        }
        if (!changed && visual != null && byeAt == 0f) return;
        byeAt = 0f;                     // 告别途中又装回来了：取消销毁
        var ids = AndroidLook.Ids(next);
        if (ids == null) { Clear(); Reason = "机器人外观资源缺失"; return; }

        string nextKey = AndroidLook.Key(ids);
        // 换型号后气泡样式跟着换（282 每个模板的 info/chatBalloon 可以不同，虽然实测都是 36）
        if (balloonStyle == 0 || templateId != next.templateId) balloonStyle = BalloonStyleOf(next.templateId);
        templateId = next.templateId;
        if (nextKey == key) return;
        key = nextKey;

        if (visual == null)
        {
            visual = new GameObject("android");
            visual.transform.SetParent(transform, false);
            body = visual.AddComponent<SpriteRenderer>();
            anim = visual.AddComponent<SpriteAnim>();
            feet = owner.FeetPos;
            horizontalVelocity = 0;
            movementPhysics = NativeMovementPhysics.Load();
            previousAndroidX = (int)(feet.x * Art.PPU);
            followDirection = 0;
            spawned = false;
        }
        body.sortingOrder = owner.SortingOrder - 1;
        // 机器人不进职业配方表：直接把 id 数组注册成配方，再让 SpriteAnim 按 key 播动作。
        WzCharacter.Register(key, ids);
        anim.Init("chars", key, "stand", body);
        anim.SetFacing((int)facing);
        if (!spawned) { spawned = true; PlayAction("hello"); }
    }

    /// <summary>模板 info/chatBalloon 的气泡样式号（282 是 36）；缺节点时按 0 号样式。</summary>
    static int BalloonStyleOf(int templateId)
    {
        var template = NativeAndroidService.Template(templateId);
        if (template != null && template.TryGetValue("info", out var node) &&
            node is Dictionary<string, object> info && info.TryGetValue("chatBalloon", out var style))
        {
            int value = Convert.ToInt32(style);
            if (value > 0) return value;
        }
        return 0;
    }

    void Clear()
    {
        if (visual != null)
        {
            if (Application.isPlaying) Destroy(visual); else DestroyImmediate(visual);
        }
        visual = null; body = null; anim = null; key = null; spawned = false;
        balloonText = null;
        lastActionAt = float.NegativeInfinity;
        byeAt = 0f;
        // 重新生成（换角色/换图）时不能把上一只的基准带过来，否则一出现就 alert/levelup
        lastHp = 0f; lastLevel = 0; lastJob = -1; deathPlayed = false;
        templateId = 0; balloonStyle = 0;
        resolved = null;
        ownerLanding = default; localLanding = default;
    }

    void OnDisable() => Clear();

    // —————————————— 跟随 ——————————————

    void Move(float dt)
    {
        if (owner == null) return;
        Vector2 target = owner.FeetPos;
        int ownerX = (int)(target.x * Art.PPU);
        int androidX = (int)(feet.x * Art.PPU);
        bool facesLeft = owner.facing < 0;
        followDirection = NativeAndroidService.FollowDirection(facesLeft, facesLeft,
            ownerX, androidX, previousAndroidX, followDirection);
        previousAndroidX = androidX;
        bool far = Mathf.Abs(target.x - feet.x) > TeleportGap / Art.PPU;
        float horizontalStep = 0;

        // 纵向参照「主人脚下踩的那块平台」：主人跳跃（jumpY>0）时这块平台高度不变，
        // 机器人自然不跟着飞；主人上绳时同理留在最后的地面。
        float ground = target.y;
        if (ownerLanding.Read(target.x, target.y + 12f / Art.PPU, out float ownerGround))
            ground = ownerGround;

        if (far)
        {
            // 掉队过远（传送/被撞开）：直接落到主人脚下的平台，不再逐帧追
            feet.x = target.x;
            followDirection = 0;
            horizontalVelocity = 0;
            feet.y = ground;
        }
        else
        {
            // The 282 direction is an input to CVecCtrl's ground acceleration/braking,
            // not an instantaneous velocity. Keep the existing migration speed limit.
            double oldVelocity = horizontalVelocity;
            horizontalVelocity = movementPhysics.Ground(horizontalVelocity, followDirection, 0, 1,
                Mathf.Max(.5f, owner.moveSpeed) * Art.PPU * 1.15 / movementPhysics.WalkSpeed, 0, dt);
            horizontalStep = (float)((oldVelocity + horizontalVelocity) * .5 * dt / Art.PPU);
            feet.x += horizontalStep;
            // 在机器人自己的 x 上找同一高度的平台：站在台阶边缘外时它会落到低一级地面。
            float landing = ground;
            if (localLanding.Read(feet.x, ground + 12f / Art.PPU, out float local))
                landing = local;
            // 上升（台阶/高台）贴地，下落才平滑 —— 原来的实现两头都 MoveTowards，
            // 主人跳上落差 >260px 的高台时机器人会永远卡在下面，只能等横向超距才传上去。
            if (landing > feet.y) feet.y = landing;
            else feet.y = Mathf.MoveTowards(feet.y, landing, 420f / Art.PPU * dt);
        }

        bool moving = !far && horizontalStep != 0;
        if (moving) facing = horizontalStep > 0 ? 1 : -1;
        else facing = target.x >= feet.x ? 1 : -1;      // 站住时面向主人
        visual.transform.position = new Vector3(feet.x, feet.y, 0f);
        // 主人换 lane（走到不同高度）时 SortingOrder 会变，机器人得跟着，
        // 否则站在高台上会压到本该在前面的怪物。
        body.sortingOrder = owner.SortingOrder - 1;
        anim.SetBase(moving ? "walk" : "stand");
        anim.SetFacing((int)facing);
    }

    // —————————————— 事件台词 ——————————————

    void Events()
    {
        if (Profile.level > lastLevel && lastLevel > 0) PlayAction("levelup");
        lastLevel = Profile.level;
        if (Profile.JobId != lastJob && lastJob >= 0) PlayAction("job");
        lastJob = Profile.JobId;

        bool dead = owner.hp <= 0;
        if (dead && !deathPlayed) { deathPlayed = true; PlayAction("dead"); }
        else if (!dead) deathPlayed = false;

        // alert：主人挨打时掷一次。源端模板里 alert 就是「prob 10 的动作 + prob 90 的空条目」，
        // 加权由 RollAction 负责，这里只决定「什么时候掷」——掉血且没死。
        if (!dead && lastHp > 0f && owner.hp < lastHp) PlayAction("alert");
        lastHp = owner.hp;
    }

    /// <summary>播一条模板事件：hello/levelup/dead/bye/job/alert。</summary>
    public void PlayAction(string action, float seconds = BalloonSeconds)
    {
        if (templateId == 0 || anim == null) return;
        var entry = NativeAndroidService.RollAction(templateId, action, random);
        if (entry == null) return;
        // 282 RVA 1D117B0: action/face/chat share a 1000ms gate. Empty
        // weighted entries never enter that executor (RVA 1D11500).
        bool hasEffect = false;
        foreach (string field in ActionFields)
            if (entry.TryGetValue(field, out var value) && value is string text && text.Length > 0)
            { hasEffect = true; break; }
        if (!hasEffect || Time.unscaledTime - lastActionAt < 1f) return;
        lastActionAt = Time.unscaledTime;
        // action 字段是「额外做一个动作」（alert 的抬手示警就是靠它），
        // 纸娃娃不一定每个模板都有这个动作，PlayOnce 会自己判空。
        if (entry.TryGetValue("action", out var move) && move is string animName && animName.Length > 0)
            anim.PlayOnce(animName);
        if (entry.TryGetValue("face", out var face) && face is string name)
        {
            int emote = Array.IndexOf(SpriteAnim.EmoteNames, name);
            // Android has already passed its own 1s gate; CAndroid::SetEmotion
            // (RVA 1D0FE10) has no player's keyboard-emote cooldown.
            if (emote > 0) anim.Express(emote, checkCooldown: false);
        }
        if (entry.TryGetValue("chat", out var chat) && chat is string line && line.Length > 0)
        {
            balloonText = line;
            balloonSpan = Mathf.Max(.2f, seconds);
            balloonUntil = Time.unscaledTime + seconds;
        }
    }

    // —————————————— 台词气泡 ——————————————

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (string.IsNullOrEmpty(balloonText)) return;
        float now = Time.unscaledTime;
        if (now > balloonUntil) { balloonText = null; return; }
        var cam = Camera.main;
        if (cam == null || body == null || visual == null) return;

        if (balloonMeasure == null)
            balloonMeasure = new GUIStyle(GUI.skin.label) { font = UiKit.Font, fontSize = 12, wordWrap = false };
        int inner = (int)Mathf.Max(24f, balloonMeasure.CalcSize(new GUIContent(balloonText)).x);
        var layout = NativeChatBalloon.Prepare(balloonStyle, inner, 1)
            ?? NativeChatBalloon.Prepare(0, inner, 1)
            ?? NativeChatBalloon.Prepare("pet/0", inner, 1);
        if (layout == null) return;

        float s = Mathf.Max(.35f, UiKit.H / ArtBaseHeight);
        float headY = body.sprite != null ? body.bounds.max.y : feet.y + .3f;
        var screen = cam.WorldToScreenPoint(new Vector3(feet.x, headY, 0f));
        if (screen.z < 0f) return;
        var anchor = new Vector2(screen.x / UiKit.Scale / s, (Screen.height - screen.y) / UiKit.Scale / s);
        float alpha = Mathf.Clamp01((balloonUntil - now) / balloonSpan);

        UiKit.Begin();
        var matrix = GUI.matrix;
        var color = GUI.color;
        GUI.matrix = matrix * Matrix4x4.Scale(new Vector3(s, s, 1f));
        GUI.color = new Color(1f, 1f, 1f, alpha);
        var frame = layout.Frame(now);
        var rect = new Rect(anchor.x - layout.anchorX, anchor.y - frame.height, frame.width, frame.height);
        GUI.DrawTexture(rect, frame);

        if (balloonLabel == null) balloonLabel = new GUIStyle(GUI.skin.label)
        {
            font = UiKit.Font,
            fontSize = Mathf.Max(12, NativeChatBalloon.FontSize(balloonStyle)),
            alignment = TextAnchor.MiddleCenter,
            wordWrap = false,
            clipping = TextClipping.Overflow,
        };
        var text = balloonLabel;
        text.fontSize = Mathf.Max(12, NativeChatBalloon.FontSize(balloonStyle));
        // WZ 的 clr 按 0xBBGGRR 存，Prepare 按 RRGGBB 解释 → 这里交换 r/b 还原。
        var c = layout.color;
        text.normal.textColor = new Color(c.b, c.g, c.r, c.a * alpha);
        var textRect = layout.text;
        textRect.position += rect.position;
        GUI.Label(textRect, balloonText, text);
        GUI.color = color;
        GUI.matrix = matrix;
    }
}
