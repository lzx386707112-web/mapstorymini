using System.Collections.Generic;
using Server.Bot;
using UnityEngine;

// 地图假人纸娃娃：复用原IMG轴心、部件z层和SpriteAnim原帧delay。
// 位置按封包节奏插值（源端客户端对远程角色是平滑播放 movement 包，不是瞬移），
// 纸娃娃合成按帧分摊（进图瞬间几十个假人一次性合成会卡主线程），
// 说话用原版 UI/ChatBalloon.img 的九宫格画在头顶。
public sealed class BotField : MonoBehaviour
{
    // 每帧最多合成几个假人的纸娃娃；仓促抬高会在进图时掉帧。
    const int MaxCreatePerFrame = 2;
    // 原端聊天气泡显示 5000ms（0x48A935 的 push 0x1388），最后 500ms 淡出。
    const float BalloonSeconds = 5f;
    const float BalloonFadeSeconds = 0.5f;
    // 封包位置一跳是 10px/160ms（MovementCommands.walkAlongX），按这个节奏插值。
    const float StepSeconds = 0.1f;
    // 假人纸娃娃的头顶高度（px），气泡从名字牌上方冒出。
    const float HeadHeightPx = 84f;

    sealed class View
    {
        public GameObject root;
        public SpriteAnim animation;
        public string appearance;
        public float previousY;
        public Vector3 target;
        public float speed = 100f;
    }

    sealed class Balloon
    {
        public string text;
        public float until;
    }

    readonly Dictionary<int, View> views = new Dictionary<int, View>();
    readonly Dictionary<int, Balloon> balloons = new Dictionary<int, Balloon>();
    readonly HashSet<int> present = new HashSet<int>();
    readonly List<int> removed = new List<int>();
    readonly Queue<int> pending = new Queue<int>();
    Server.Bot.Social.BotOutput output;
    int currentMap, currentWorld, currentChannel;
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
        currentMap = mapId; currentWorld = world; currentChannel = channel; synchronized = true;
        present.Clear();
        foreach (var actor in BotRegistry.OnMap(mapId))
        {
            if (!actor.online || actor.world != world || actor.channel != channel) continue;
            present.Add(actor.id);
            if (!views.ContainsKey(actor.id))
            {
                // 纸娃娃合成排到后面的帧做，避免一次进图卡住。
                if (!pending.Contains(actor.id)) pending.Enqueue(actor.id);
                continue;
            }
            Apply(actor, views[actor.id]);
        }
        removed.Clear();
        foreach (var entry in views) if (!present.Contains(entry.Key)) removed.Add(entry.Key);
        foreach (int id in removed) { RemoveView(views[id]); views.Remove(id); balloons.Remove(id); }
        CreateQueued();
    }

    // 每帧只补建 MaxCreatePerFrame 个假人。
    void CreateQueued()
    {
        int budget = MaxCreatePerFrame;
        while (budget > 0 && pending.Count > 0)
        {
            int id = pending.Dequeue();
            if (!present.Contains(id) || views.ContainsKey(id)) continue;
            var actor = BotRegistry.Get(id);
            if (actor == null) continue;
            Create(actor);
            budget--;
        }
    }

    void Create(BotActor actor)
    {
        var bot = actor as BotCharacter;
        if (bot == null) return;
        var root = new GameObject("SoloMapling_" + actor.id);
        root.transform.SetParent(transform, false);
        var renderer = root.AddComponent<SpriteRenderer>();
        renderer.sortingOrder = 300;
        var view = new View { root = root, animation = root.AddComponent<SpriteAnim>(), previousY = actor.y };
        var ids = BotAppearance.CharacterIds(bot);
        string key = "bot_" + string.Join("_", ids);
        WzCharacter.Appearance(key, ids);
        view.animation.Init("chars", key, "stand", renderer);
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
            view.speed = Mathf.Clamp(distance / StepSeconds, 45f, 500f);
            view.target = target;
        }
        var bot = actor as BotCharacter;
        if (bot == null) return;
        string pose = bot.chair > 0 ? "sit" : Pose(bot.stance);
        view.animation.SetBase(pose);
        view.animation.SetFacing((bot.stance & 1) == 0 ? 1 : -1);
        view.animation.speed = (pose == "rope" || pose == "ladder") && !bot.moving && bot.y == view.previousY ? 0 : 1;
        view.previousY = bot.y;
    }

    void Update()
    {
        float step = Time.deltaTime;
        foreach (var view in views.Values)
            view.root.transform.position = Vector3.MoveTowards(view.root.transform.position, view.target, view.speed * step);
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
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                clipping = TextClipping.Overflow,
            };
        balloonStyle.normal.textColor = new Color(0f, 0f, 0f, 1f);   // 原版气泡字是黑的
        float now = Time.unscaledTime;
        foreach (var entry in balloons)
        {
            if (!views.TryGetValue(entry.Key, out var view)) continue;
            var balloon = entry.Value;
            float alpha = Mathf.Clamp01((balloon.until - now) / BalloonFadeSeconds);
            var head = view.root.transform.position + new Vector3(0f, HeadHeightPx / Art.PPU, 0f);
            var sp = cam.WorldToScreenPoint(head);
            if (sp.z < 0f) continue;
            var gui = new Vector2(sp.x / UiKit.Scale, (Screen.height - sp.y) / UiKit.Scale);
            var size = balloonStyle.CalcSize(new GUIContent(balloon.text));
            // 九宫格：左右各 6px、上下各 6px 边框，正文区按 12x14 的 c 平铺。
            float innerW = Mathf.Max(12f, Mathf.Ceil(size.x / 12f) * 12f);
            float innerH = Mathf.Max(14f, Mathf.Ceil(size.y / 14f) * 14f);
            float w = innerW + 12f, h = innerH + 12f;
            float arrowH = 13f;
            var rect = new Rect(gui.x - w * .5f, gui.y - arrowH - h, w, h);
            if (rect.xMax < -40f || rect.x > UiKit.W + 40f || rect.yMax < -40f || rect.y > UiKit.H + 40f) continue;
            Color previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            DrawNine(rect, innerW, innerH);
            if (balloonArrow != null)
                GUI.DrawTexture(new Rect(gui.x - balloonArrow.width * .5f, rect.yMax - 1f, balloonArrow.width, arrowH), balloonArrow);
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Label(new Rect(rect.x + 6f, rect.y + 6f, innerW, innerH), balloon.text, balloonStyle);
            GUI.color = previous;
        }
    }

    // 原 IMG 是 6px 角 + 12/14 四边平铺；这里按 tile 逐块铺满，不用缩放（缩放会糊掉边框）。
    static void DrawNine(Rect rect, float innerW, float innerH)
    {
        float x0 = rect.x, y0 = rect.y, w = rect.width, h = rect.height;
        Draw(balloonNw, x0, y0);
        Draw(balloonNe, x0 + 6f + innerW, y0);
        Draw(balloonSw, x0, y0 + 6f + innerH);
        Draw(balloonSe, x0 + 6f + innerW, y0 + 6f + innerH);
        Tile(new Rect(x0 + 6f, y0, innerW, 6f), balloonN);
        Tile(new Rect(x0 + 6f, y0 + h - 6f, innerW, 6f), balloonS);
        Tile(new Rect(x0, y0 + 6f, 6f, innerH), balloonW);
        Tile(new Rect(x0 + w - 6f, y0 + 6f, 6f, innerH), balloonE);
        Tile(new Rect(x0 + 6f, y0 + 6f, innerW, innerH), balloonC);
    }

    static void Draw(Texture2D texture, float x, float y)
    {
        if (texture == null) return;
        GUI.DrawTexture(new Rect(x, y, texture.width, texture.height), texture);
    }

    static void Tile(Rect area, Texture2D texture)
    {
        if (texture == null || area.width <= 0f || area.height <= 0f) return;
        float tw = texture.width, th = texture.height;
        for (float y = area.y; y < area.yMax; y += th)
            for (float x = area.x; x < area.xMax; x += tw)
                GUI.DrawTexture(new Rect(x, y, Mathf.Min(tw, area.xMax - x), Mathf.Min(th, area.yMax - y)), texture);
    }
}
