using System.Collections.Generic;
using Server.Bot;
using UnityEngine;

// 地图假人纸娃娃：复用原IMG轴心、部件z层和SpriteAnim原帧delay。
// 位置按封包节奏插值（源端客户端对远程角色是平滑播放 movement 包，不是瞬移），
// 纸娃娃合成按帧分摊（进图瞬间几十个假人一次性合成会卡主线程），
// 说话用原版 UI/ChatBalloon.img 的九宫格画在头顶。
public sealed class BotField : MonoBehaviour
{
    // 同图内新增（假人从别的图走进来）每帧最多建几个；仓促抬高会在正常游戏时掉帧。
    const int MaxCreatePerFrame = 1;
    // 换图那一帧最多一次建几个。换图本来就有黑幕，且纸娃娃已预合成，
    // 一次性建档等价于联机「进图接收全部 spawn 包」——这是有意为之，不是遗漏。
    const int MaxCreateOnMapEnter = 40;
    // 视图增删（进图/假人换图）与姿势同步不需要每帧重算：100ms 一次足够
    // （假人位移包本身是 160ms 一步），省掉每帧一次 BotRegistry.OnMap 的列表分配与全量遍历。
    const float SyncSeconds = 0.1f;
    // 原端聊天气泡显示 5000ms（0x48A935 的 push 0x1388），最后 500ms 淡出。
    const float BalloonSeconds = 5f;
    const float BalloonFadeSeconds = 0.5f;
    // MovementCommands.walkAlongX：duration=160ms、step=10px（＝62.5px/s），客户端按这个 duration
    // 把角色从上一位置播到新位置。插值窗口必须就是 160ms；用 100ms 会「快走一段再停 60ms」，看着一顿一顿。
    const float StepSeconds = 0.16f;

    sealed class View
    {
        public GameObject root;
        public SpriteAnim animation;
        public string appearance;
        public float previousY;
        public Vector3 target;
        public float speed = 0f;        // 世界单位/秒；由封包位移/时长算出来，别给默认值
        public float measured;          // 实测移动速度（px/s），用来对齐 walk 动画速率
    }

    sealed class Balloon
    {
        public string text;
        public float until;
        // 排版（断行 + 尺寸）只算一次：每帧对每个字 CalcSize 太浪费
        public string wrapped;
        public float innerW, innerH;
    }

    readonly Dictionary<int, View> views = new Dictionary<int, View>();
    readonly Dictionary<int, Balloon> balloons = new Dictionary<int, Balloon>();
    readonly HashSet<int> present = new HashSet<int>();
    readonly List<int> removed = new List<int>();
    readonly Queue<int> pending = new Queue<int>();
    Server.Bot.Social.BotOutput output;
    int currentMap, currentWorld, currentChannel;
    float lastSync = -10f;
    bool synchronized;

    void OnEnable()
    {
        output = BotTypeManager.Output;
        output.Published += OnOutput;
        AudioManager.Preload("Game/EnchantSuccess", "Game/EnchantFailure");
    }
    void OnOutput(Server.Bot.Social.BotOutput.Message message)
    {
        var actor = BotRegistry.Get(message.actorId);
        if (!synchronized || actor == null || message.mapId != currentMap ||
            actor.world != currentWorld || actor.channel != currentChannel) return;
        if (message.kind == Server.Bot.Social.BotOutput.Kind.Chat && !string.IsNullOrEmpty(message.text))
        {
            BottomBar.AddChat(actor.name + " : " + message.text, BottomBar.ChatWhite);
            ShowBalloon(message.actorId, message.text);
        }
        // SocialCommands.BotChatbubble 只冒泡不进聊天记录。
        if (message.kind == Server.Bot.Social.BotOutput.Kind.Bubble && !string.IsNullOrEmpty(message.text))
            ShowBalloon(message.actorId, message.text);
        if (message.kind == Server.Bot.Social.BotOutput.Kind.Emote) Express(message.actorId, message.value);
        if (message.kind == Server.Bot.Social.BotOutput.Kind.Scroll && views.TryGetValue(message.actorId,out var scrolling))
        {
            // SHOW_SCROLL_EFFECT: success is its first bool; CURSE uses the same Failure IMG.
            string result = message.value == 1 ? "Success" : "Failure";
            NativeImgAnimation.Spawn("Effect/BasicEff.img/Enchant/" + result,
                scrolling.root.transform, scrolling.root.transform.position, 360);
            AudioManager.Play("Game/Enchant" + result, pitchVary:false);
        }
        if (message.kind == Server.Bot.Social.BotOutput.Kind.Attack && views.TryGetValue(message.actorId,out var view))
            view.animation.PlayNativeAttack(message.text,message.value);
    }

    public void Synchronize(int mapId, int world, int channel)
    {
        bool sameMap = synchronized && mapId == currentMap;
        bool sameContext = sameMap && world == currentWorld && channel == currentChannel;
        if (sameContext && Time.unscaledTime - lastSync < SyncSeconds) return;
        // 「还没同步过」也算换图：首次进游戏、或者 Clear() 之后重新进图，
        // 都该把假人一次性摆好（Clear 会把 synchronized 置 false）。
        bool mapChanged = !sameMap;
        lastSync = Time.unscaledTime;
        currentMap = mapId; currentWorld = world; currentChannel = channel; synchronized = true;
        present.Clear();
        foreach (var actor in BotRegistry.OnMap(mapId))
        {
            if (!actor.online || actor.world != world || actor.channel != channel) continue;
            present.Add(actor.id);
            if (!views.ContainsKey(actor.id))
            {
                if (!pending.Contains(actor.id)) pending.Enqueue(actor.id);
                continue;
            }
            Apply(actor, views[actor.id]);
        }
        removed.Clear();
        foreach (var entry in views) if (!present.Contains(entry.Key)) removed.Add(entry.Key);
        foreach (int id in removed) { RemoveView(views[id]); views.Remove(id); balloons.Remove(id); }
        // 换图：假人一次性站好（对齐联机 —— 进图那一刻周边玩家就已经在场上，不是一个个冒出来）。
        // 前提是纸娃娃已经在登录界面预合成完（BotAppearancePrebake.Finished）：
        // 那时建视图只是查表 + new GameObject，一个人 0.1~0.3ms。
        // 预合成没赶完就退回增量排队——宁可晚出现，也不能在进图那一帧连做十几次 CPU 合成。
        if (mapChanged && BotAppearancePrebake.Finished) CreateQueued(MaxCreateOnMapEnter, false);
        else CreateQueued(MaxCreatePerFrame);
    }

    // 每帧按预算补建假人视图：单个纸娃娃要混合几十张 IMG 部件，一件就可能吃掉几毫秒，
    // 所以本帧预算用完就先让位给渲染，剩下的下一帧继续。
    // limit = 本批最多建几个；respectBudget = 是否受每帧毫秒预算约束
    // （换图那一帧有黑幕遮挡，可以不受约束一次建完）。
    void CreateQueued(int limit, bool respectBudget = true)
    {
        int created = 0;
        while (created < limit && pending.Count > 0)
        {
            if (respectBudget && created > 0 && !Server.Bot.BotWorkPump.Has(1f)) break;
            int id = pending.Dequeue();
            if (!present.Contains(id) || views.ContainsKey(id)) continue;
            var actor = BotRegistry.Get(id);
            if (actor == null) continue;
            Create(actor);
            created++;
        }
    }

    void Create(BotActor actor)
    {
        var bot = actor as BotCharacter;
        if (bot == null) return;
        using var perf = Server.Bot.BotPerf.Timer("BotField.Create");
        var root = new GameObject("SoloMapling_" + actor.id);
        root.transform.SetParent(transform, false);
        var renderer = root.AddComponent<SpriteRenderer>();
        renderer.sortingOrder = 300;
        var view = new View { root = root, animation = root.AddComponent<SpriteAnim>(), previousY = actor.y };
        var ids = BotAppearance.CharacterIds(bot);
        string key = "bot_" + string.Join("_", ids);
        WzCharacter.Appearance(key, ids);
        view.animation.Init("chars", key, "stand", renderer);
        // 假人换帧要现合成纸娃娃：由帧预算决定本帧是否合成，别让几十个假人同时换帧拖垮帧率。
        view.animation.throttleWhenBusy = true;
        view.animation.Play("stand", false, true);
        view.appearance = key;
        // 首次直接落位，之后交给插值。
        view.target = new Vector3(actor.x / Art.PPU, -actor.y / Art.PPU, 0f);
        root.transform.position = view.target;
        views.Add(actor.id, view);
        Apply(actor, view);
    }

    void Apply(BotActor actor, View view)
    {
        var target = new Vector3(actor.x / Art.PPU, -actor.y / Art.PPU, 0f);
        float distance = Vector3.Distance(view.target, target);
        if (distance > 0.0001f)
        {
            // 源端客户端按封包 duration 把上一位置播到新位置；这里按同样的节奏取速度。
            // 注意 distance 是世界单位（1 单位 = 40px），不能拿像素范围（45~500）去夹，
            // 否则 10px（0.25 单位）会被抬到 45 单位/秒＝1800px/s，每一步都瞬移。
            float pixels = distance * Art.PPU;
            view.speed = Mathf.Clamp(pixels / StepSeconds / Art.PPU, 2f / Art.PPU, 800f / Art.PPU);
            view.target = target;
        }
        var bot = actor as BotCharacter;
        if (bot == null) return;
        string pose = bot.chair > 0 ? "sit" : Pose(bot.stance);
        view.animation.SetBase(pose);
        view.animation.SetFacing((bot.stance & 1) == 0 ? 1 : -1);
        // walk 的播放速率按「实际走多快」算（见 Update）：walkAlongX 的 62.5px/s 和录制动作的
        // 真实速度不一样，写死一个系数总有一边滑步。站/跳/爬仍按原 delay 播。
        if (pose == "rope" || pose == "ladder")
            view.animation.speed = !bot.moving && bot.y == view.previousY ? 0f : 1f;
        else if (pose != "walk")
            view.animation.speed = 1f;
        view.previousY = bot.y;
    }

    void Update()
    {
        CreateQueued(MaxCreatePerFrame);
        float step = Time.deltaTime;
        foreach (var view in views.Values)
        {
            var before = view.root.transform.position;
            var after = Vector3.MoveTowards(before, view.target, view.speed * step);
            view.root.transform.position = after;
            if (step <= 0f) continue;
            // 实测速度（px/s）→ walk 动画速率：主角 125px/s 时按 1.0 播，
            // 假人 62.5px/s 就按 0.5 播——否则动画和位移对不上，看着像滑步。
            float instant = Vector3.Distance(before, after) * Art.PPU / step;
            view.measured = Mathf.Lerp(view.measured, instant, 1f - Mathf.Exp(-10f * step));
            if (view.animation.Current == "walk")
                view.animation.speed = Mathf.Clamp(view.measured / PlayerCtrl.BaseWalkPx, 0.25f, 2f);
        }
        if (balloons.Count == 0) return;
        float now = Time.unscaledTime;
        removed.Clear();
        foreach (var entry in balloons) if (now >= entry.Value.until) removed.Add(entry.Key);
        foreach (int id in removed) balloons.Remove(id);
    }

    // CharacterStance.java；其余姿势不能凭编号猜测。
    static string Pose(int stance)
    {
        switch (stance & ~1)
        {
            case 2: return "walk";
            case 6: return "jump";
            case 14: return "ladder";
            case 16: return "rope";
            default: return "stand";
        }
    }

    public void PlaySkillEffect(BotCharacter actor,int skillId)
    {
        if (!views.TryGetValue(actor.id,out var view)) return;
        string path="Skill/"+(skillId/10000).ToString("D3")+".img/skill/"+skillId+"/effect";
        var frames=WzAssets.Frames(WzAssets.Node(path));
        if (frames.Count==0) return;
        float lifetime=0;
        foreach (var frame in frames) lifetime+=Mathf.Max(1,Mathf.Abs(frame.Int("delay",100)))/1000f;
        NativeImgAnimation.Spawn(path,view.root.transform,view.root.transform.position,360,actor.facing>0,lifetime:lifetime);
    }

    public void Express(int actorId, int expression)
    {
        if (!views.TryGetValue(actorId, out var view)) return;
        if (expression == 0) view.animation.ClearExpression();
        else view.animation.Express(expression);
    }

    public void Clear()
    {
        foreach (var view in views.Values) RemoveView(view);
        views.Clear(); present.Clear(); removed.Clear(); pending.Clear(); balloons.Clear();
        synchronized = false;
        lastSync = -10f;
    }
    static void RemoveView(View view)
    {
        if (Application.isPlaying) Destroy(view.root);
        else DestroyImmediate(view.root);
    }
    void OnDisable()
    {
        if (output != null) output.Published -= OnOutput;
        output = null;
        Clear();
    }

    // ——————————————— 头顶聊天气泡 ———————————————

    void ShowBalloon(int actorId, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (balloons.Count > 64) balloons.Clear();   // 兜底：假人异常刷屏时不把 GUI 拖死
        balloons[actorId] = new Balloon { text = text, until = Time.unscaledTime + BalloonSeconds };
    }

    static GUIStyle balloonStyle;
    static Texture2D balloonNw, balloonNe, balloonSw, balloonSe, balloonN, balloonS, balloonW, balloonE, balloonC, balloonArrow;
    static bool balloonArtTried;

    static void EnsureBalloonArt()
    {
        if (balloonArtTried) return;
        balloonArtTried = true;
        const string root = "UI/ChatBalloon.img/0/";
        balloonNw = TryTexture(root + "nw"); balloonNe = TryTexture(root + "ne");
        balloonSw = TryTexture(root + "sw"); balloonSe = TryTexture(root + "se");
        balloonN = TryTexture(root + "n"); balloonS = TryTexture(root + "s");
        balloonW = TryTexture(root + "w"); balloonE = TryTexture(root + "e");
        balloonC = TryTexture(root + "c"); balloonArrow = TryTexture(root + "arrow");
    }

    static Texture2D TryTexture(string path)
    {
        try
        {
            var node = WzAssets.Node(path);
            return node == null ? null : WzAssets.Texture(node);
        }
        catch (System.Exception) { return null; }
    }

    // 原端 UI 的绘制基准是 768 高的虚拟屏（NameTag / ChatBalloon 都是这套），
    // 所以气泡整体按 UiKit.H/768 缩放；九宫格在「原图像素」空间里平铺，
    // 缩放交给 GUI.matrix，6/12/14 的边框才不会被非整数缩放糊掉或挤出缝。
    const float ArtBaseHeight = 768f;

    void OnGUI()
    {
        if (!synchronized || balloons.Count == 0) return;
        var cam = Camera.main;
        if (cam == null) return;
        EnsureBalloonArt();
        if (balloonC == null) return;
        UiKit.Begin();
        if (balloonStyle == null)
            balloonStyle = new GUIStyle(GUI.skin.label)
            {
                font = UiKit.Font,
                fontSize = 12,                       // 原气泡正文 12px，行高 14px
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                clipping = TextClipping.Overflow,
            };
        float s = Mathf.Max(0.35f, UiKit.H / ArtBaseHeight);
        float now = Time.unscaledTime;
        foreach (var entry in balloons)
        {
            if (!views.TryGetValue(entry.Key, out var view)) continue;
            var balloon = entry.Value;
            float alpha = Mathf.Clamp01((balloon.until - now) / BalloonFadeSeconds);
            // 锚点 = 角色当前站立帧的头顶（不是脚点上固定 84px：矮角色会飘一截、高角色会压脸）
            var renderer = view.animation.Renderer;
            float headY = view.root.transform.position.y +
                (renderer != null && renderer.sprite != null ? renderer.sprite.bounds.max.y : 0.55f);
            var sp = cam.WorldToScreenPoint(new Vector3(view.root.transform.position.x, headY, 0f));
            if (sp.z < 0f) continue;
            var anchor = new Vector2(sp.x / UiKit.Scale / s, (Screen.height - sp.y) / UiKit.Scale / s);
            // 正文排版：原端气泡超出宽度就断行，长消息是往下长成多行，
            // 不是横向一直拉长（横向长度没有上限的话，短角色配超长气泡就完全脱节）。
            if (balloon.wrapped == null) Measure(balloon, balloonStyle);
            float innerW = balloon.innerW;
            float innerH = balloon.innerH;
            float w = innerW + 12f, h = innerH + 12f;
            // 尾巴几何（UI/ChatBalloon.img/0/arrow，13x13，origin=(1,0)）：
            //   0 行 x0 透明、x1 起才有像素；图像左下角是尾尖，尾尖落在角色头顶；
            //   前 5 行是下边框带的逐像素复制（ee/ccddee/bbccdd/bbbbdd/黑），
            //   所以它必须整体「压」在主体的 6px 下边框上，不能挂在下边框下面——
            //   挂下面就是一块白板 + 一条边框线横切，气泡看上去被切成两半。
            //   这几行里没有圆角处的黑色，说明尾巴开在直边段上（sw 圆角之后 6px），
            //   而不是压住左下角（压住会把角的黑边刷成浅蓝，缺一块轮廓）。
            const float Border = 6f;                    // 九宫格下边框厚度（sw/s/se 的高度）
            const float Corner = 6f;                    // 九宫格圆角宽度
            // 水平：尾巴开在气泡左边往右 Corner，尾尖（arrow 图像 x1）要正好落在角色头顶，
            // 所以气泡左边 = 头顶 - Corner，而不是「气泡左边 = 头顶」——那样尾尖会偏右 6px。
            var rect = new Rect(anchor.x - Corner, anchor.y - (balloonArrow != null ? balloonArrow.height - Border : 7f) - h, w, h);
            var tailX = rect.x + Corner - 1f;            // origin.x = 1：尾巴贴在下边框直边段的起点
            float viewW = UiKit.W / s, viewH = UiKit.H / s;
            if (rect.xMax < -40f || rect.x > viewW + 40f || rect.yMax < -40f || rect.y > viewH + 40f) continue;

            var previousMatrix = GUI.matrix;
            var previousColor = GUI.color;
            GUI.matrix = previousMatrix * Matrix4x4.Scale(new Vector3(s, s, 1f));
            GUI.color = new Color(1f, 1f, 1f, alpha);
            // 主体整张画（不是九块分开画）：分开画时 GUI 把每个四边形对齐到整数像素，
            // 缩放比又不是整数，相邻块之间会留下 1px 空隙——就是那条「透明的线」。
            var body = BalloonBody((int)innerW, (int)innerH);
            if (body != null) GUI.DrawTexture(rect, body);
            // 尾巴最后画（在主体之上）：尾巴顶部那 5 行本来就是下边框带的复制品，
            // 压上去才能把边框「抹掉」接成一体；画在下面就会露出一条边框线和白板。
            if (balloonArrow != null)
                GUI.DrawTexture(new Rect(tailX, rect.yMax - Border, balloonArrow.width, balloonArrow.height), balloonArrow);
            balloonStyle.normal.textColor = new Color(0f, 0f, 0f, alpha);   // clr = -16777216，黑字
            GUI.Label(new Rect(rect.x + 6f, rect.y + 6f, innerW, innerH), balloon.wrapped, balloonStyle);
            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
        }
    }

    // 正文区宽度上限：12px 字体一行最多 12 个汉字（144px），超出就断行、往下长。
    // 反汇编链路（构造 0x48A935 → 九宫格 0x4890C4）里只走素材与文本，没有独立的宽度常量可比对，
    // 所以这是本实现里唯一一个「按排版常识」定的值：要改一行就够。
    const float MaxTextWidth = 144f;

    // 逐字量宽断行（原端是 CreateTextCanvas 按宽度折行）；同时算出正文区尺寸：
    // 宽按 12 取整、高按行数×14（**不能用 CalcSize 的高**，它含字体行距，
    // 单行会被撑成两层高、气泡中间多一圈空白）。
    static void Measure(Balloon balloon, GUIStyle style)
    {
        string text = balloon.text ?? string.Empty;
        var builder = new System.Text.StringBuilder(text.Length + 8);
        var line = new System.Text.StringBuilder();
        float lineWidth = 0f, widest = 0f;
        int lines = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n')
            {
                widest = Mathf.Max(widest, lineWidth);
                builder.Append(line).Append('\n');
                line.Length = 0; lineWidth = 0f; lines++;
                continue;
            }
            float charWidth = style.CalcSize(new GUIContent(c.ToString())).x;
            if (line.Length > 0 && lineWidth + charWidth > MaxTextWidth)
            {
                widest = Mathf.Max(widest, lineWidth);
                builder.Append(line).Append('\n');
                line.Length = 0; lineWidth = 0f; lines++;
            }
            line.Append(c); lineWidth += charWidth;
        }
        widest = Mathf.Max(widest, lineWidth);
        builder.Append(line); lines++;
        balloon.wrapped = builder.ToString();
        balloon.innerW = Mathf.Max(12f, Mathf.Ceil(widest / 12f) * 12f);
        // 行高取字体自己的行距（原端 12px 字体是 14px 行距，这里不小于 14），
        // 否则多行文字会比正文区高、压到上下边框上。
        float lineHeight = Mathf.Max(14f, style.lineHeight);
        balloon.innerH = Mathf.Max(14f, lines * lineHeight);
    }

    // 九宫格先合成成一张整图（按尺寸缓存），再一次性画：内部不可能出现缝，
    // 缩放成任何比例都只有外边缘受影响。
    static readonly Dictionary<int, Texture2D> balloonBodies = new Dictionary<int, Texture2D>();

    static Texture2D BalloonBody(int innerW, int innerH)
    {
        int key = (innerW << 16) | innerH;
        if (balloonBodies.TryGetValue(key, out var cached) && cached != null) return cached;
        if (balloonBodies.Count > 48)
        {
            foreach (var texture in balloonBodies.Values) if (texture != null) Destroy(texture);
            balloonBodies.Clear();
        }
        int w = innerW + 12, h = innerH + 12;
        var pixels = new Color32[w * h];                 // 默认全透明
        Blit(balloonNw, pixels, w, h, 0, 0, 6, 6);
        Blit(balloonNe, pixels, w, h, w - 6, 0, 6, 6);
        Blit(balloonSw, pixels, w, h, 0, h - 6, 6, 6);
        Blit(balloonSe, pixels, w, h, w - 6, h - 6, 6, 6);
        Blit(balloonN, pixels, w, h, 6, 0, innerW, 6);
        Blit(balloonS, pixels, w, h, 6, h - 6, innerW, 6);
        Blit(balloonW, pixels, w, h, 0, 6, 6, innerH);
        Blit(balloonE, pixels, w, h, w - 6, 6, 6, innerH);
        Blit(balloonC, pixels, w, h, 6, 6, innerW, innerH);
        var body = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            name = "ChatBalloon_" + w + "x" + h,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        body.SetPixels32(pixels); body.Apply();
        balloonBodies[key] = body;
        return body;
    }

    // 把切图按原样平铺进整图；top 是自上而下的行号，越界自动裁掉。
    static void Blit(Texture2D piece, Color32[] dst, int dw, int dh, int x, int top, int w, int h)
    {
        if (piece == null || w <= 0 || h <= 0) return;
        var src = piece.GetPixels32();
        int sw = piece.width, sh = piece.height;
        for (int ty = 0; ty < h; ty++)
        {
            int y = top + ty;
            if (y < 0 || y >= dh) continue;
            int row = (dh - 1 - y) * dw;                 // GetPixels32 自下而上
            int sy = sh - 1 - (ty % sh);                 // 源图同样自下而上
            for (int tx = 0; tx < w; tx++)
            {
                int col = x + tx;
                if (col < 0 || col >= dw) continue;
                dst[row + col] = src[sy * sw + (tx % sw)];
            }
        }
    }
}
